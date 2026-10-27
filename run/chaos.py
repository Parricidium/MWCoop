r"""Relais UDP « mauvaise connexion » entre un invite et l'hote (voie IP seulement), pour les essais de robustesse.

    python -I run\chaos.py --ecoute 7873 --cible 127.0.0.1:7871 --profil enfer --journal dumps\chaos.jsonl
                           [--script "60:coupure:5:h2g,90:coupure:14:h2g,130:coupure:16,200:profil:lan"]
                           [--regle perte=0.02 --regle debit_kbps=2000] [--graine 1] [--duree 300] [--t0 lancement]

L'hote ecoute comme d'habitude ([Coop] Port, 7871 sur les instances Joueur1/2) ; le relais ecoute sur --ecoute et
transmet a --cible ; l'invite met [Coop] Port=<ecoute> dans SON ini (Adresse=127.0.0.1). Bibliotheque standard
seulement, rien ne sort de 127.0.0.1. Chaque sens (g2h : invite -> hote, h2g : hote -> invite) subit, dans l'ordre :
coupure programmee, perte (simple ou en rafale, modele a deux etats), debit limite (file bornee : au-dela, perte),
latence +- gigue, desordre (livre en avance d'un peu, derriere les suivants), corruption (un bit inverse : passe
sous la somme de controle UDP, que le noyau recalcule), doublon. Rejouable : --graine. Le journal (JSONL, une ligne
par evenement et un bilan par sens toutes les 5 s) porte l'heure murale (celle de mwcoop.log) et t = secondes
depuis le premier paquet de l'invite (ou depuis le lancement du relais : --t0 lancement), meme origine pour --script.

Script : "T:coupure:S[:sens]" (plus rien ne passe pendant S s ; sens g2h, h2g ou les deux), "T:profil:NOM",
"T:regle:cle=val[;cle=val]". Regles (--regle ou profil) : latence_ms, gigue_ms, perte (0-1), rafale_p (passage
en rafale par paquet), rafale_perte (perte pendant la rafale), rafale_len (paquets, duree moyenne), debit_kbps
(0 : illimite), file_ms (attente max dans la file de debit), desordre (0-1), desordre_ms, corruption (0-1),
doublon (0-1).
"""
import argparse
import heapq
import json
import random
import selectors
import socket
import sys
import time

PROFILS = {
    "lan": dict(latence_ms=1, gigue_ms=0.5),
    "wifi_moyen": dict(latence_ms=35, gigue_ms=25, perte=0.02, rafale_p=0.02, rafale_perte=0.5, rafale_len=8, desordre=0.01, desordre_ms=30),
    "mobile": dict(latence_ms=60, gigue_ms=30, perte=0.01, debit_kbps=2000, file_ms=300),
    "transatlantique": dict(latence_ms=120, gigue_ms=20, perte=0.005),
    "enfer": dict(latence_ms=200, gigue_ms=150, perte=0.08, rafale_p=0.05, rafale_perte=0.6, rafale_len=10,
                  desordre=0.10, desordre_ms=150, doublon=0.05, debit_kbps=512, file_ms=500),
    "doublons": dict(latence_ms=20, gigue_ms=10, doublon=0.3, desordre=0.3, desordre_ms=200),
    "corruption": dict(latence_ms=20, gigue_ms=10, corruption=0.01),
}
DEFAUT = dict(latence_ms=0.0, gigue_ms=0.0, perte=0.0, rafale_p=0.0, rafale_perte=0.0, rafale_len=1.0, debit_kbps=0.0,
              file_ms=300.0, desordre=0.0, desordre_ms=0.0, corruption=0.0, doublon=0.0)
SENS = ("g2h", "h2g")


class Etat:
    """Compteurs et etat d'un sens."""

    def __init__(self):
        self.coupe_jusqua = 0.0
        self.rafale = False
        self.libre_a = 0.0        # file de debit : instant ou le lien est libre
        self.dernier_du = 0.0     # ordre : instant de livraison du dernier paquet (sauf desordre)
        self.n = dict(recus=0, livres=0, octets=0, perte=0, coupure=0, file=0, desordre=0, corruption=0, doublon=0)
        self.latence_total = 0.0


class Relais:
    def __init__(self, a):
        self.a = a
        self.regles = dict(DEFAUT)
        self.rng = random.Random(a.graine)
        self.etats = {s: Etat() for s in SENS}
        self.file = []            # (du, numero, sens, donnees, socket, destination)
        self.numero = 0
        self.invites = {}         # adresse de l'invite -> (socket vers l'hote, dernier paquet)
        self.par_socket = {}      # socket vers l'hote -> adresse de l'invite
        self.t0 = None
        self.depart = time.perf_counter()
        self.journal = open(a.journal, "a", encoding="utf-8") if a.journal else None
        self.script = []
        hote, port = a.cible.rsplit(":", 1)
        self.cible = (hote, int(port))
        self.sel = selectors.DefaultSelector()
        self.ecoute = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.ecoute.setblocking(False)
        self.ecoute.bind(("127.0.0.1" if a.local else "0.0.0.0", a.ecoute))
        self.sel.register(self.ecoute, selectors.EVENT_READ, "ecoute")
        self.bilan_a = 0.0
        if a.profil:
            self.profil(a.profil)
        for r in a.regle:
            self.regle(r)
        for item in [s for s in a.script.split(",") if s.strip()]:
            champs = item.strip().split(":")
            self.script.append((float(champs[0]), champs[1:]))
        self.script.sort(key=lambda x: x[0])
        if a.t0 == "lancement":
            self.t0 = self.depart

    # ---------------------------------------------------------------- journal
    def t(self):
        return None if self.t0 is None else round(time.perf_counter() - self.t0, 3)

    def note(self, ev, **champs):
        ligne = dict(heure=time.strftime("%H:%M:%S") + ".%03d" % int(time.time() * 1000 % 1000), t=self.t(), ev=ev)
        ligne.update(champs)
        s = json.dumps(ligne, ensure_ascii=False)
        if self.journal:
            self.journal.write(s + "\n")
            self.journal.flush()
        print(s, flush=True)

    def bilan(self):
        for s in SENS:
            e = self.etats[s]
            n = dict(e.n)
            n["retard_boucle_ms"] = round(1000 * e.latence_total / e.n["livres"], 1) if e.n["livres"] else 0   # (en plus de la latence voulue)
            n["rafale"] = e.rafale
            self.note("bilan", sens=s, **n)

    # ---------------------------------------------------------------- reglages
    def profil(self, nom):
        if nom not in PROFILS:
            raise SystemExit("profil inconnu : " + nom + " (" + ", ".join(PROFILS) + ")")
        self.regles = dict(DEFAUT)
        self.regles.update(PROFILS[nom])
        self.note("profil", nom=nom, regles=self.regles)

    def regle(self, texte):
        for kv in texte.split(";"):
            k, v = kv.split("=", 1)
            k = k.strip()
            if k not in DEFAUT:
                raise SystemExit("regle inconnue : " + k + " (" + ", ".join(DEFAUT) + ")")
            self.regles[k] = float(v)
        self.note("regle", regles=self.regles)

    def coupure(self, secondes, sens):
        jusqua = time.perf_counter() + secondes
        for s in ([sens] if sens in SENS else SENS):
            self.etats[s].coupe_jusqua = jusqua
        self.note("coupure", secondes=secondes, sens=sens if sens in SENS else "les deux")

    def script_step(self):
        if self.t0 is None or not self.script:
            return
        tt = time.perf_counter() - self.t0
        while self.script and self.script[0][0] <= tt:
            _, champs = self.script.pop(0)
            quoi = champs[0]
            if quoi == "coupure":
                self.coupure(float(champs[1]), champs[2] if len(champs) > 2 else "")
            elif quoi == "profil":
                self.profil(champs[1])
            elif quoi == "regle":
                self.regle(":".join(champs[1:]))
            else:
                self.note("script_inconnu", champs=champs)

    # ---------------------------------------------------------------- un paquet
    def transmettre(self, sens, donnees, sock, dest):
        e = self.etats[sens]
        r = self.regles
        now = time.perf_counter()
        e.n["recus"] += 1
        e.n["octets"] += len(donnees)
        if now < e.coupe_jusqua:
            e.n["coupure"] += 1
            return
        # perte simple ou en rafale (deux etats : bon / rafale)
        if e.rafale:
            if self.rng.random() < 1.0 / max(1.0, r["rafale_len"]):
                e.rafale = False
        elif self.rng.random() < r["rafale_p"]:
            e.rafale = True
        if self.rng.random() < (r["rafale_perte"] if e.rafale else r["perte"]):
            e.n["perte"] += 1
            return
        # debit limite : file d'attente bornee
        base = now
        if r["debit_kbps"] > 0:
            duree = len(donnees) * 8.0 / (r["debit_kbps"] * 1000.0)
            debut = max(now, e.libre_a)
            if debut - now > r["file_ms"] / 1000.0:
                e.n["file"] += 1
                return
            e.libre_a = debut + duree
            base = e.libre_a
        # latence et gigue
        lat = max(0.0, r["latence_ms"] + self.rng.uniform(-r["gigue_ms"], r["gigue_ms"])) / 1000.0
        du = base + lat
        if r["desordre"] > 0 and self.rng.random() < r["desordre"]:
            du = max(now, du - r["desordre_ms"] / 1000.0)
            if du < e.dernier_du:
                e.n["desordre"] += 1
        else:
            du = max(du, e.dernier_du)
            e.dernier_du = du
        # corruption : un bit inverse n'importe ou (en-tete compris)
        if r["corruption"] > 0 and self.rng.random() < r["corruption"]:
            b = bytearray(donnees)
            i = self.rng.randrange(len(b))
            b[i] ^= 1 << self.rng.randrange(8)
            donnees = bytes(b)
            e.n["corruption"] += 1
        self.planifier(du, sens, donnees, sock, dest)
        if r["doublon"] > 0 and self.rng.random() < r["doublon"]:
            e.n["doublon"] += 1
            self.planifier(du + self.rng.uniform(0.001, 0.005), sens, donnees, sock, dest)

    def planifier(self, du, sens, donnees, sock, dest):
        self.numero += 1
        heapq.heappush(self.file, (du, self.numero, sens, donnees, sock, dest))

    def livrer(self, now):
        while self.file and self.file[0][0] <= now:
            du, _, sens, donnees, sock, dest = heapq.heappop(self.file)
            try:
                sock.sendto(donnees, dest)
            except OSError:
                continue
            e = self.etats[sens]
            e.n["livres"] += 1
            e.latence_total += now - du + 0.0  # (retard de la boucle en plus de la latence voulue : ~0)

    # ---------------------------------------------------------------- boucle
    def socket_hote(self, invite):
        ent = self.invites.get(invite)
        if ent is None:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.setblocking(False)
            s.bind(("127.0.0.1" if self.a.local else "0.0.0.0", 0))
            try:
                # (sans cela, un ICMP « port injoignable » ferme la reception sous Windows)
                s.ioctl(socket.SIO_UDP_CONNRESET, False)
            except (AttributeError, OSError):
                pass
            self.sel.register(s, selectors.EVENT_READ, "hote")
            self.par_socket[s] = invite
            self.invites[invite] = [s, time.perf_counter()]
            self.note("invite", adresse="%s:%d" % invite, port_relais=s.getsockname()[1])
            if self.t0 is None:
                self.t0 = time.perf_counter()
                self.note("t0", quoi="premier paquet de l'invite")
            ent = self.invites[invite]
        ent[1] = time.perf_counter()
        return ent[0]

    def oublier(self, now):
        for invite, (s, dernier) in list(self.invites.items()):
            if now - dernier > 60.0:
                self.sel.unregister(s)
                s.close()
                del self.par_socket[s]
                del self.invites[invite]
                self.note("invite_oublie", adresse="%s:%d" % invite)

    def boucle(self):
        self.note("depart", ecoute=self.a.ecoute, cible="%s:%d" % self.cible, graine=self.a.graine, profil=self.a.profil or "",
                  script=self.a.script)
        try:
            while True:
                now = time.perf_counter()
                if self.a.duree and now - self.depart > self.a.duree:
                    break
                self.script_step()
                self.livrer(now)
                attente = 0.005
                if self.file:
                    attente = max(0.0, min(attente, self.file[0][0] - now))
                for cle, _ in self.sel.select(attente):
                    for _ in range(64):
                        try:
                            donnees, adresse = cle.fileobj.recvfrom(4096)
                        except (BlockingIOError, InterruptedError):
                            break
                        except OSError:
                            break
                        if cle.data == "ecoute":
                            s = self.socket_hote(adresse)
                            self.transmettre("g2h", donnees, s, self.cible)
                        else:
                            invite = self.par_socket.get(cle.fileobj)
                            if invite is not None:
                                self.invites[invite][1] = now
                                self.transmettre("h2g", donnees, self.ecoute, invite)
                if now - self.bilan_a >= 5.0:
                    self.bilan_a = now
                    self.bilan()
                    self.oublier(now)
        except KeyboardInterrupt:
            pass
        finally:
            self.bilan()
            self.note("fin")
            if self.journal:
                self.journal.close()


def main():
    p = argparse.ArgumentParser(description="relais UDP mauvaise connexion (essais MWCoop)")
    p.add_argument("--ecoute", type=int, default=7873, help="port UDP ou l'invite se connecte")
    p.add_argument("--cible", default="127.0.0.1:7871", help="adresse:port de l'hote")
    p.add_argument("--profil", default="", help=", ".join(PROFILS))
    p.add_argument("--regle", action="append", default=[], help="cle=val[;cle=val]")
    p.add_argument("--script", default="", help='"T:coupure:S[:sens],T:profil:NOM,T:regle:cle=val"')
    p.add_argument("--journal", default="", help="fichier JSONL")
    p.add_argument("--graine", type=int, default=1)
    p.add_argument("--duree", type=float, default=0.0, help="s'arrete apres S secondes (0 : jamais)")
    p.add_argument("--t0", default="invite", choices=["invite", "lancement"])
    p.add_argument("--local", action="store_true", default=True, help="n'ecoute que sur 127.0.0.1 (defaut)")
    a = p.parse_args()
    if sys.platform == "win32":
        try:
            import ctypes
            ctypes.windll.winmm.timeBeginPeriod(1)   # minuteur a 1 ms pendant le relais
        except Exception:
            pass
    Relais(a).boucle()


if __name__ == "__main__":
    main()
