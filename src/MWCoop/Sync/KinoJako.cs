using System;
using System.Collections.Generic;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Cinema : partage du film d'une bobine avant la seance (JD, 23/10 : « quand un joueur met une bobine avec un .mp4, il faut
    // que ca demande aux autres s'ils acceptent le fichier ; une fois accepte, attendre que tout le monde ait telecharge le film,
    // et la, ca lance » -- son ami ne voyait pas le film de 302 Mo).
    //  - celui qui met une bobine dont il a le film (importe ou filme) dans un projecteur devient le partageur : il propose le film
    //    aux autres (@kinojako op 1 : films, titre, taille, salle) ; la seance attend (projecteur bloque, texte d'attente).
    //  - chez chacun des autres : deja le film -> pret ; sinon la question « Telecharger ? » ([Y] oui, [U] non, 60 s = non).
    //    Reponses et avancement a tous (op 2 : 0 non, 1 telecharge + %, 2 pret). Un refus : pas de film chez lui (rien ne se
    //    telecharge, l'ecran reste blanc), la seance ne l'attend pas.
    //  - le partageur envoie le film des le premier « oui » (KauppaFilmi, au debit du canal fiable), dit l'attente a tous
    //    (op 3 : salle, texte) ; quand tous ont repondu et que ceux qui ont dit oui l'ont recu : la seance commence toute seule.
    //  - la bobine retiree pendant l'attente : partage annule (op 4).
    public static partial class Kauppa
    {
        class KinoShare
        {
            public List<string> Films; public string Title, Content; public int Sali; public long Size; public float Started, NextSay;
            public readonly Dictionary<int, int> State = new Dictionary<int, int>();
            public readonly Dictionary<int, float> Pct = new Dictionary<int, float>();
        }
        class KinoAsk { public List<string> Films; public string Title; public long Size; public int From, Sali; public float At; }   // (Size en Ko)

        static KinoShare kinoShare;                 // (chez le partageur)
        static KinoAsk kinoAsk;                     // (chez un autre : la question affichee)
        static readonly string[] kinoWait = { "", "", "" };   // salle -> texte d'attente ("" : pas d'attente), dit par le partageur
        static readonly HashSet<string> kinoRefused = new HashSet<string>(), kinoAccepted = new HashSet<string>();
        static List<string> kinoGetting;            // films acceptes ici, en cours de telechargement
        static string kinoGettingTitle = "";
        static long kinoGettingKb;
        static string SizeText(long kb) { return kb >= 1024 * 1024 ? (kb / 1048576f).ToString("F1") + Lang.T(" Go", " GB") : Mathf.Max(1, Mathf.RoundToInt(kb / 1024f)) + Lang.T(" Mo", " MB"); }
        static float kinoNextPct;
        const float KinoAskTimeout = 60f;

        static bool KinoFilmRefused(string film) { return kinoRefused.Contains(film); }
        static bool KinoWaiting(int sali) { return sali >= 1 && sali <= 2 && kinoWait[sali].Length > 0; }

        static void ResetKinoJako()
        {
            kinoShare = null; kinoAsk = null; kinoWait[1] = kinoWait[2] = ""; kinoGetting = null; kinoGettingTitle = "";
        }

        static List<string> ShareFilms(string content)
        {
            var l = new List<string>();
            if (string.IsNullOrEmpty(content) || LinkOf(content) != null) return l;
            foreach (string c in Clips(content)) if (c.Length > 0 && !l.Contains(c)) l.Add(c);
            return l;
        }

        static string Name(int id) { PlayerInfo pi; return Session.Players.TryGetValue(id, out pi) && pi != null && !string.IsNullOrEmpty(pi.Name) ? pi.Name : "#" + id; }

        // ---------------------------------------------------------------- partageur
        // bobine posee ici dans un projecteur : faut-il partager son film avant la seance ?
        static void KinoShareStart(Projector p)
        {
            if (!Session.Active || Session.RemoteCount <= 0 || p == null) return;
            List<string> films = ShareFilms(p.Content);
            if (films.Count == 0 || !films.TrueForAll(HaveFilm)) return;
            long size = 0;
            foreach (string f in films) try { size += new FileInfo(FilmPath(f)).Length; } catch { }
            var sh = new KinoShare { Films = films, Title = TapeTitle(p.Content), Content = p.Content, Sali = p.Sali, Size = size, Started = Time.realtimeSinceStartup };
            foreach (int id in Session.Players.Keys) if (id != Session.LocalId) sh.State[id] = -1;
            kinoShare = sh;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kinojako").U8(1).U8(p.Sali).Str(sh.Title).I32((int)(size / 1024)).U8(films.Count);
            foreach (string f in films) w.Str(f);
            Session.SendAll(w, true);
            KinoSayWait(sh);
            L("cinema : film de la salle " + p.Sali + " propose aux autres (" + string.Join(", ", films.ToArray()) + ", " + size / 1048576 + " Mo)");
            Hud.Toast(Lang.T("Film proposé aux autres joueurs (", "Film offered to the other players (") + SizeText(size / 1024) + Lang.T(") : la séance commence quand chacun l'a.", "): the show starts once everyone has it."));
        }

        static void KinoShareCancel(string why)
        {
            if (kinoShare == null) return;
            int sali = kinoShare.Sali;
            kinoShare = null;
            kinoWait[sali] = "";
            if (Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kinojako").U8(4).U8(sali), true);
            L("cinema : partage du film de la salle " + sali + " annule (" + why + ")");
        }

        static void TickKinoShare(float now)
        {
            KinoShare sh = kinoShare;
            if (sh == null) return;
            Projector p = projectors[sh.Sali];
            if (p == null || p.Content != sh.Content || !Session.Active) { KinoShareCancel("bobine retiree"); return; }
            // joueurs partis, ou muets trop longtemps (question sans reponse : non)
            foreach (int id in new List<int>(sh.State.Keys))
            {
                if (!Session.Players.ContainsKey(id)) { sh.State.Remove(id); continue; }
                if (sh.State[id] == -1 && now - sh.Started > KinoAskTimeout + 8f) sh.State[id] = 0;
            }
            bool done = true;
            foreach (int st in sh.State.Values) if (st == -1 || st == 1) done = false;
            if (done)
            {
                kinoShare = null;
                kinoWait[sh.Sali] = "";
                if (Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kinojako").U8(3).U8(sh.Sali).Str(""), true);
                SetProjector(p, p.Content, true, 0, true);
                L("cinema : tout le monde a le film (ou l'a refuse), la seance de la salle " + sh.Sali + " commence");
                return;
            }
            if (now >= sh.NextSay) KinoSayWait(sh);
        }

        // texte d'attente : qui on attend, et ou il en est
        static void KinoSayWait(KinoShare sh)
        {
            sh.NextSay = Time.realtimeSinceStartup + 1f;
            var parts = new List<string>();
            foreach (KeyValuePair<int, int> kv in sh.State)
            {
                if (kv.Value == -1) parts.Add(Name(kv.Key) + " ?");
                else if (kv.Value == 1) { float pc; sh.Pct.TryGetValue(kv.Key, out pc); parts.Add(Name(kv.Key) + " " + Mathf.FloorToInt(pc * 100f) + " %"); }
            }
            string t = parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : "…";
            kinoWait[sh.Sali] = t;
            if (Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kinojako").U8(3).U8(sh.Sali).Str(t), true);
        }

        // ---------------------------------------------------------------- les autres
        static void KinoReply(int state, float pct)
        {
            if (!Session.Active) return;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kinojako").U8(2).U8(state).F32(pct), true);
        }

        static float GettingProgress()
        {
            if (kinoGetting == null || kinoGetting.Count == 0) return 1f;
            float s = 0f;
            foreach (string f in kinoGetting) s += Mathf.Max(0f, FilmProgress(f));
            return s / kinoGetting.Count;
        }

        // film arrive ici (KauppaFilmi.OnFilmPart)
        static void KinoFilmArrived(string film)
        {
            if (kinoGetting == null || !kinoGetting.Contains(film)) return;
            if (!kinoGetting.TrueForAll(HaveFilm)) return;
            kinoGetting = null;
            KinoReply(2, 1f);
            Hud.Toast(Lang.T("Film reçu : la séance commence quand tout le monde l'a.", "Film received: the show starts once everyone has it."));
        }

        static void KinoAnswer(bool yes)
        {
            KinoAsk a = kinoAsk;
            if (a == null) return;
            kinoAsk = null;
            if (!yes)
            {
                foreach (string f in a.Films) kinoRefused.Add(f);
                KinoReply(0, 0f);
                L("cinema : film « " + a.Title + " » refuse");
                return;
            }
            foreach (string f in a.Films) { kinoAccepted.Add(f); kinoRefused.Remove(f); }
            kinoGetting = a.Films.FindAll(f => !HaveFilm(f)); kinoGettingTitle = a.Title; kinoGettingKb = a.Size;
            if (kinoGetting.Count == 0) { kinoGetting = null; KinoReply(2, 1f); return; }
            KinoReply(1, 0f);
            L("cinema : film « " + a.Title + " » accepte, telechargement (" + SizeText(a.Size) + ")");
        }

        static void TickKinoJako(float now)
        {
            TickKinoShare(now);
            if (kinoAsk != null)
            {
                bool typing = Menu.ChatOpen || Menu.Open;
                if (!typing && Input.GetKeyDown(KeyCode.Y)) KinoAnswer(true);
                else if (!typing && Input.GetKeyDown(KeyCode.U)) KinoAnswer(false);
                else if (now - kinoAsk.At > KinoAskTimeout) KinoAnswer(false);
            }
            if (kinoGetting != null && now >= kinoNextPct)
            {
                kinoNextPct = now + 1f;
                if (kinoGetting.TrueForAll(HaveFilm)) { kinoGetting = null; KinoReply(2, 1f); }
                else KinoReply(1, GettingProgress());
            }
        }

        public static void OnKinoJako(int who, NetReader r)
        {
            int op = r.U8();
            if (op == 1)
            {
                int sali = r.U8(); string title = r.Str(); long kb = r.I32(); int n = r.U8();
                var films = new List<string>();
                for (int i = 0; i < n; i++) { string f = Session.Clean(r.Str(), 64); if (f.Length > 0 && f.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) films.Add(f); }
                if (films.Count == 0) return;
                if (films.TrueForAll(HaveFilm)) { KinoReply(2, 1f); return; }
                if (films.TrueForAll(f => kinoAccepted.Contains(f))) { kinoAsk = new KinoAsk { Films = films, Title = title, Size = kb, From = who, Sali = sali }; KinoAnswer(true); return; }
                kinoAsk = new KinoAsk { Films = films, Title = title, Size = kb, From = who, Sali = sali, At = Time.realtimeSinceStartup };
                L("cinema : #" + who + " propose le film « " + title + " » (" + SizeText(kb) + ") pour la salle " + sali);
                return;
            }
            if (op == 2)
            {
                int st = r.U8(); float pct = r.F32();
                KinoShare sh = kinoShare;
                if (sh == null || !sh.State.ContainsKey(who)) return;
                int was = sh.State[who];
                sh.State[who] = st; sh.Pct[who] = pct;
                if (st == 1 && was != 1) { foreach (string f in sh.Films) SendFilm(f); L("cinema : " + Name(who) + " telecharge le film"); }
                if (st == 0 && was != 0) L("cinema : " + Name(who) + " refuse le film");
                if (st == 2 && was != 2) L("cinema : " + Name(who) + " a le film");
                return;
            }
            if (op == 3) { int sali = r.U8(); string t = r.Str(); if (sali >= 1 && sali <= 2) kinoWait[sali] = t; return; }
            if (op == 4)
            {
                int sali = r.U8();
                if (sali >= 1 && sali <= 2) kinoWait[sali] = "";
                if (kinoAsk != null && kinoAsk.From == who && kinoAsk.Sali == sali) kinoAsk = null;
            }
        }

        // ---------------------------------------------------------------- dessin (Core.OnGUI)
        public static void DrawKinoJako()
        {
            if (Event.current.type != EventType.Repaint || (kinoAsk == null && kinoGetting == null && !KinoWaitHere())) return;
            float w = Style.Px(620), x = (Screen.width - w) / 2f, y = Style.Px(90);
            if (kinoAsk != null)
            {
                KinoAsk a = kinoAsk;
                float h = Style.Px(118);
                Style.Round(new Rect(x, y, w, h), Style.Px(14), new Color(0.05f, 0.07f, 0.12f, 0.88f));
                string sz = SizeText(a.Size);
                Style.Text(new Rect(x, y + Style.Px(10), w, Style.Px(30)), Lang.T("CINÉMA KINO TÄHTI", "KINO TÄHTI CINEMA"), 15, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.35f));
                Style.Text(new Rect(x + Style.Px(14), y + Style.Px(38), w - Style.Px(28), Style.Px(30)),
                           string.Format(Lang.T("{0} veut projeter « {1} ». Télécharger le film ? Poids : {2}", "{0} wants to show \"{1}\". Download the film? Size: {2}"), Name(a.From), a.Title.Length > 0 ? a.Title : "?", sz),
                           16, TextAnchor.MiddleCenter, Color.white, false);
                int left = Mathf.Max(0, Mathf.CeilToInt(KinoAskTimeout - (Time.realtimeSinceStartup - a.At)));
                Style.Text(new Rect(x, y + Style.Px(74), w, Style.Px(30)), string.Format(Lang.T("[Y] OUI     [U] NON     ({0} s)", "[Y] YES     [U] NO     ({0} s)"), left), 17, TextAnchor.MiddleCenter, new Color(0.55f, 0.9f, 1f));
                return;
            }
            string line = "";
            if (kinoGetting != null) line = string.Format(Lang.T("Film « {0} » : {1} % ({2} sur {3})", "Film \"{0}\": {1} % ({2} of {3})"), kinoGettingTitle, Mathf.FloorToInt(GettingProgress() * 100f), SizeText((long)(GettingProgress() * kinoGettingKb)), SizeText(kinoGettingKb));
            else
                for (int s = 1; s <= 2; s++)
                    if (KinoWaiting(s) && (InSalle(s) || (kinoShare != null && kinoShare.Sali == s)))
                        line = Lang.T("Séance en attente du film : ", "Show waiting for the film: ") + kinoWait[s];
            if (line.Length == 0) return;
            Style.Round(new Rect(x, y, w, Style.Px(40)), Style.Px(12), new Color(0.05f, 0.07f, 0.12f, 0.8f));
            Style.Text(new Rect(x, y, w, Style.Px(40)), line, 15, TextAnchor.MiddleCenter, Color.white, false);
        }

        static bool KinoWaitHere()
        {
            if (kino == null) return false;
            for (int s = 1; s <= 2; s++) if (KinoWaiting(s) && (InSalle(s) || (kinoShare != null && kinoShare.Sali == s))) return true;
            return false;
        }
    }
}
