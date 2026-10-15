using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Peinture des pieces et de la carrosserie. Automate 'Paint' de chaque piece : la bombe
    // (PLAYER/.../SprayCan :: Paint, etat 'Regular paint') y ecrit PaintType et Color puis envoie
    // REPAINT, transition globale vers l'etat qui applique (Set mat, Paint type 2, Rusty... selon la
    // piece ; la carrosserie de la CORRIS a code couleur et vinyle). Une piece neuve tire sa couleur
    // au hasard (... -> 'Save color').
    //  - une action injectee au debut de l'etat vise par REPAINT envoie toutes les variables de
    //    l'automate (entiers, reels, couleurs, textes hors cles de sauvegarde) quand on y arrive par
    //    REPAINT (coup de bombe), ou apres un tirage au hasard chez l'hote (sinon chaque joueur
    //    aurait sa couleur pour la meme piece neuve) ;
    //  - ailleurs : variables recopiees puis REPAINT, comme la bombe.
    // Piece designee par son ID (Data/Use) ou, a defaut (carrosserie), par son chemin.
    // Pieces toutes neuves (colis : retour d'un joueur, 09/10, « la couleur aussi ») : la peinture de l'hote arrivait avant
    // que la piece de l'invite soit suivie ("introuvable ici" : perdue), ou l'invite tirait la sienne apres l'avoir recue.
    // Gardee 1 min : posee des que la piece est suivie (releve rapproche tant qu'il en attend), et reposee apres un tirage
    // local de l'invite.
    public static class Paint
    {
        class Vars { public List<KeyValuePair<string, int>> I; public List<KeyValuePair<string, float>> F; public List<KeyValuePair<string, Color>> C; public List<KeyValuePair<string, string>> S; public int Who; public float At; public bool Done; }
        static readonly Dictionary<string, Vars> received = new Dictionary<string, Vars>();
        static readonly List<string> reapply = new List<string>();

        // Releve bientot (colis ouvert : pieces neuves a suivre).
        // (et toutes les 2 s pendant 20 s : l'automate d'une piece neuve n'est pas encore pret au premier releve -- il ne
        // l'etait qu'au suivant, 45 s plus tard, sa couleur tiree entre-temps ; portieres de colis beige chez l'un, rouge
        // chez l'autre, .nolly 13/10)
        public static void ScanSoon() { if (nextScan > 0) { nextScan = Mathf.Min(nextScan, Time.realtimeSinceStartup + 1.5f); burstUntil = Time.realtimeSinceStartup + 20f; } }
        static float burstUntil;
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> rejected = new HashSet<PlayMakerFSM>();   // automates Paint sans REPAINT : plus revus
        // Evenement du coup de bombe : REPAINT, ou GLOBALEVENT (-> "Paint type") pour les pieces du moteur (bloc, culasse,
        // carter...) -- ecartees jusqu'ici, leur peinture restait chez celui qui peignait (Crowbangus, 12/10).
        static readonly Dictionary<PlayMakerFSM, string> repaintEvent = new Dictionary<PlayMakerFSM, string>();
        static string RepaintOf(PlayMakerFSM f) { string e; return repaintEvent.TryGetValue(f, out e) ? e : "REPAINT"; }
        static float nextScan = -1, loadedAt;
        static int lateLogs;
        static bool applying;

        class Hook : ModHook
        {
            public override string Module { get { return "peinture"; } }
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) OnLocal(F); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            hooked.Clear(); rejected.Clear(); received.Clear(); reapply.Clear(); repaintEvent.Clear();
            loadedAt = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? loadedAt + 9f : -1;
        }

        public static void Update()
        {
            if (reapply.Count > 0)
            {
                foreach (string id in reapply) { Vars v; if (received.TryGetValue(id, out v)) Apply(id, v, true); }
                reapply.Clear();
            }
            if (nextScan < 0 || Time.realtimeSinceStartup < nextScan) return;
            // (Toutes les 15 s les 2 premieres minutes, puis 45 s : le releve de tous les automates coutait 70-90 ms ; 2 s
            // tant qu'une peinture recue attend sa piece.)
            float now0 = Time.realtimeSinceStartup;
            bool waiting = false;
            foreach (string k in new List<string>(received.Keys)) { if (now0 - received[k].At > 60f) received.Remove(k); else if (!received[k].Done) waiting = true; }
            nextScan = now0 + (waiting || now0 < burstUntil ? 2f : now0 - loadedAt < 120f ? 15f : 45f);
            int n = 0;
            foreach (Object o in Game.FsmsNamed("Paint"))
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.FsmName != "Paint" || f.hideFlags != HideFlags.None || hooked.Contains(f) || rejected.Contains(f)) continue;
                FsmState s = null;
                string ev = "REPAINT";
                foreach (FsmTransition tr in f.Fsm.GlobalTransitions)
                    if (tr.EventName == "REPAINT") s = f.Fsm.GetState(tr.ToState);
                if (s == null)
                    foreach (FsmTransition tr in f.Fsm.GlobalTransitions)
                        if (tr.EventName == "GLOBALEVENT" && tr.ToState == "Paint type") { s = f.Fsm.GetState(tr.ToState); ev = "GLOBALEVENT"; }
                if (s == null) { rejected.Add(f); continue; }
                // (Objet jamais actif : ses actions ne sont pas chargees -- les lire les chargeait a grands frais puis
                // echouait, a chaque releve, pour chaque piece en carton : 65-80 ms. Accroche des que l'objet a vecu.)
                if (!s.IsInitialized) continue;
                try
                {
                    FsmHooks.Prepend(s, new Hook { F = f });
                }
                catch { continue; }
                hooked.Add(f);
                if (ev != "REPAINT") repaintEvent[f] = ev;
                n++;
                // Hote : piece accrochee en cours de partie (colis ouvert, objet apparu) -- sa couleur, deja tiree, fait foi.
                if (Session.IsHost && Session.Active && Session.RemoteCount > 0 && now0 - loadedAt > 60f)
                {
                    var w = new NetWriter(Msg.Paint).U8(Session.LocalId).Str(KeyOf(f));
                    WriteVars(f, w);
                    if (TooLong(w, KeyOf(f))) continue;
                    Session.SendAll(w, true);
                    if (lateLogs++ < 20) Log.Info("peinture : " + KeyOf(f) + " accrochee en cours de partie, sa couleur envoyee (" + Describe(f) + ")");
                }
            }
            if (n > 0) Log.Info("peinture : " + n + " pieces suivies (" + hooked.Count + " en tout)");
            foreach (KeyValuePair<string, Vars> kv in new List<KeyValuePair<string, Vars>>(received)) if (!kv.Value.Done) Apply(kv.Key, kv.Value, false);
        }

        static void OnLocal(PlayMakerFSM f)
        {
            if (!Session.Active) return;
            FsmTransition tr = f.Fsm.LastTransition;
            bool sprayed = tr != null && tr.EventName == RepaintOf(f);
            bool random = f.Fsm.PreviousActiveState != null && f.Fsm.PreviousActiveState.Name == "Save color";
            // Invite qui tire la couleur d'une piece dont il a deja la peinture de l'hote : reposee juste apres.
            if (random && !Session.IsHost) { string rid = KeyOf(f); if (received.ContainsKey(rid)) reapply.Add(rid); return; }
            if (!sprayed && !(random && Session.IsHost && Time.realtimeSinceStartup - loadedAt > 20f)) return;
            string id = KeyOf(f);
            var w = new NetWriter(Msg.Paint).U8(Session.LocalId).Str(id);
            WriteVars(f, w);
            if (TooLong(w, id)) return;
            if (sprayed) Log.Info("peinture : " + id + " (" + Describe(f) + ")");
            Session.SendAll(w, true);
        }

        // Message borne (relecture du 15/10, #11) : WriteVars met TOUTES les variables de l'automate dans un fiable, sans
        // garde ; un automate bavard (chaines longues) depassait le paquet et l'envoi levait. Vrai : trop long, pas envoye
        // (dit une fois dans le journal).
        static bool longLogged;
        static bool TooLong(NetWriter w, string id)
        {
            if (w.Length <= 1000) return false;
            if (!longLogged) { longLogged = true; Log.Warn("peinture : " + id + " trop long (" + w.Length + " octets), pas envoye"); }
            return true;
        }

        static string KeyOf(PlayMakerFSM f)
        {
            string id = Props.ItemId(f.gameObject);
            return id.Length > 0 ? id : "p:" + Recon.Path(f.transform);
        }

        static bool Skip(string name) { return name.StartsWith("UT") || name.StartsWith("Unique") || name == "ID"; }

        static void WriteVars(PlayMakerFSM f, NetWriter w)
        {
            FsmVariables v = f.FsmVariables;
            var ints = new List<FsmInt>(); foreach (FsmInt x in v.IntVariables) if (!Skip(x.Name)) ints.Add(x);
            var floats = new List<FsmFloat>(); foreach (FsmFloat x in v.FloatVariables) if (!Skip(x.Name)) floats.Add(x);
            var colors = new List<FsmColor>(); foreach (FsmColor x in v.ColorVariables) if (!Skip(x.Name)) colors.Add(x);
            var strs = new List<FsmString>(); foreach (FsmString x in v.StringVariables) if (!Skip(x.Name)) strs.Add(x);
            w.U8(ints.Count); foreach (FsmInt x in ints) w.Str(x.Name).I32(x.Value);
            w.U8(floats.Count); foreach (FsmFloat x in floats) w.Str(x.Name).F32(x.Value);
            w.U8(colors.Count); foreach (FsmColor x in colors) w.Str(x.Name).F32(x.Value.r).F32(x.Value.g).F32(x.Value.b).F32(x.Value.a);
            w.U8(strs.Count); foreach (FsmString x in strs) w.Str(x.Name).Str(x.Value);
        }

        static string Describe(PlayMakerFSM f)
        {
            FsmInt t = f.FsmVariables.FindFsmInt("PaintType");
            FsmColor c = f.FsmVariables.FindFsmColor("Color");
            return "type " + (t != null ? t.Value.ToString() : "?") + ", couleur " + (c != null ? c.Value.ToString() : "?");
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str();
            var ints = new List<KeyValuePair<string, int>>();
            var floats = new List<KeyValuePair<string, float>>();
            var colors = new List<KeyValuePair<string, Color>>();
            var strs = new List<KeyValuePair<string, string>>();
            for (int i = 0, n = r.U8(); i < n; i++) ints.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
            for (int i = 0, n = r.U8(); i < n; i++) floats.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            for (int i = 0, n = r.U8(); i < n; i++) colors.Add(new KeyValuePair<string, Color>(r.Str(), new Color(r.F32(), r.F32(), r.F32(), r.F32())));
            for (int i = 0, n = r.U8(); i < n; i++) strs.Add(new KeyValuePair<string, string>(r.Str(), r.Str()));
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.Paint).U8(who).Str(id);
                w.U8(ints.Count); foreach (var x in ints) w.Str(x.Key).I32(x.Value);
                w.U8(floats.Count); foreach (var x in floats) w.Str(x.Key).F32(x.Value);
                w.U8(colors.Count); foreach (var x in colors) w.Str(x.Key).F32(x.Value.r).F32(x.Value.g).F32(x.Value.b).F32(x.Value.a);
                w.U8(strs.Count); foreach (var x in strs) w.Str(x.Key).Str(x.Value);
                Session.Broadcast(w, true, who);
            }
            var got = new Vars { I = ints, F = floats, C = colors, S = strs, Who = who, At = Time.realtimeSinceStartup };
            received[id] = got;
            if (!Apply(id, got, false)) { Log.Info("peinture : " + id + " de #" + who + " recue, piece pas encore suivie ici (posee des qu'elle l'est)"); ScanSoon(); }
        }

        static bool Apply(string id, Vars g, bool again)
        {
            PlayMakerFSM f = null;
            foreach (PlayMakerFSM h in hooked) if (h != null && KeyOf(h) == id) { f = h; break; }
            if (f == null) return false;
            FsmVariables v = f.FsmVariables;
            foreach (var x in g.I) { FsmInt t = v.FindFsmInt(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in g.F) { FsmFloat t = v.FindFsmFloat(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in g.C) { FsmColor t = v.FindFsmColor(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in g.S) { FsmString t = v.FindFsmString(x.Key); if (t != null) t.Value = x.Value; }
            applying = true; Replay.Depth++;
            try { f.SendEvent(RepaintOf(f)); }
            finally { applying = false; Replay.Depth--; }
            g.Done = true;
            Log.Info("peinture : " + id + " repeinte (joueur #" + g.Who + ")" + (again ? " apres le tirage d'ici" : ""));
            return true;
        }

        static PlayMakerFSM Find(string part)
        {
            foreach (PlayMakerFSM h in hooked) if (h != null && KeyOf(h).Contains(part)) return h;
            return null;
        }

        // Essais : repeint la piece dont la cle contient 'part' comme un coup de bombe (type 1 = brillant).
        public static string TestSpray(string part, Color c)
        {
            PlayMakerFSM f = Find(part);
            if (f == null) return "pas d'automate Paint suivi pour " + part + " (" + hooked.Count + " suivis)";
            string id = KeyOf(f);
            f.FsmVariables.GetFsmInt("PaintType").Value = 1;
            f.FsmVariables.GetFsmColor("Color").Value = c;
            f.SendEvent(RepaintOf(f));
            return id + " peinte en " + c;
        }

        public static string State(string part)
        {
            PlayMakerFSM f = Find(part);
            if (f == null) return "?";
            GameObject go = f.gameObject;
            Renderer rd = go.GetComponent<Renderer>();
            return "type " + f.FsmVariables.GetFsmInt("PaintType").Value + ", couleur " + f.FsmVariables.GetFsmColor("Color").Value
                   + (rd != null && rd.material != null && rd.material.HasProperty("_Color") ? ", materiau " + rd.material.name + " " + rd.material.color : "");
        }
    }
}
