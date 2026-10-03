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
    public static class Paint
    {
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static float nextScan = -1, loadedAt;
        static bool applying;

        class Hook : FsmStateAction
        {
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                if (!applying) OnLocal(F);
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            hooked.Clear();
            loadedAt = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? loadedAt + 9f : -1;
        }

        public static void Update()
        {
            if (nextScan < 0 || Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 15f;
            int n = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Paint" || hooked.Contains(f)) continue;
                FsmState s = null;
                foreach (FsmTransition tr in f.Fsm.GlobalTransitions)
                    if (tr.EventName == "REPAINT") s = f.Fsm.GetState(tr.ToState);
                if (s == null) continue;
                try
                {
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Insert(0, new Hook { F = f });
                    s.Actions = list.ToArray();
                }
                catch { continue; }
                hooked.Add(f);
                n++;
            }
            if (n > 0) Log.Info("peinture : " + n + " pieces suivies (" + hooked.Count + " en tout)");
        }

        static void OnLocal(PlayMakerFSM f)
        {
            if (!Session.Active) return;
            FsmTransition tr = f.Fsm.LastTransition;
            bool sprayed = tr != null && tr.EventName == "REPAINT";
            bool random = f.Fsm.PreviousActiveState != null && f.Fsm.PreviousActiveState.Name == "Save color";
            if (!sprayed && !(random && Session.IsHost && Time.realtimeSinceStartup - loadedAt > 20f)) return;
            string id = KeyOf(f);
            var w = new NetWriter(Msg.Paint).U8(Session.LocalId).Str(id);
            WriteVars(f, w);
            if (sprayed) Log.Info("peinture : " + id + " (" + Describe(f) + ")");
            Session.SendAll(w, true);
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
            PlayMakerFSM f = null;
            foreach (PlayMakerFSM h in hooked) if (h != null && KeyOf(h) == id) { f = h; break; }
            if (f == null) { Log.Warn("peinture : " + id + " introuvable ici"); return; }
            FsmVariables v = f.FsmVariables;
            foreach (var x in ints) { FsmInt t = v.FindFsmInt(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in floats) { FsmFloat t = v.FindFsmFloat(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in colors) { FsmColor t = v.FindFsmColor(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in strs) { FsmString t = v.FindFsmString(x.Key); if (t != null) t.Value = x.Value; }
            applying = true;
            try { f.SendEvent("REPAINT"); }
            finally { applying = false; }
            Log.Info("peinture : " + id + " repeinte (joueur #" + who + ")");
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
            f.SendEvent("REPAINT");
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
