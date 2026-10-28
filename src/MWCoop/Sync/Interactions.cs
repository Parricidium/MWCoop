using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MWCoop
{
    // Portes, interrupteurs, tele... vus par tous. Les objets du decor portent un automate 'Use' :
    // l'etat 'Wait button' recoit USE au clic du joueur, puis la logique suit (ouvrir/fermer...).
    // Une action est injectee au debut de l'etat ou mene USE : quand le joueur local l'atteint,
    // on envoie (automate, etat, booleens de l'automate) ; chez les autres, les booleens sont
    // recopies puis l'automate est mis dans cet etat -> meme decision, meme animation, meme son.
    // Liste blanche : rien qui depense de l'argent, nourrit, soigne ou deplace le joueur.
    public static class Interactions
    {
        static readonly string[,] Allowed =
        {
            // objet          etat vise par USE
            { "Handle",       "Check position" },   // portes des maisons et batiments
            { "DoorHandle",   "Check position" },
            { "Trigger",      "Check position" },
            { "Mesh",         "Check position" },
            { "Switch",       "Sound" },            // interrupteurs, radiateurs
            { "switch_*",     "Switch" },           // interrupteurs de la maison (switch_kitchen...)
            { "TVSwitch",     "Switch" },
            { "CDSwitch",     "Switch" },
            { "Eject",        "Switch" },
            { "ButtonLightModes", "Mode add" },     // phares des vehicules (CORRIS...)
            { "ButtonLightModes", "Test" },         // phares (tracteur...)
            { "ButtonWipers", "Test" },             // essuie-glaces
        };

        // My Summer Car (vidage v.250908-04, 19/10) : en plus -- tele et disjoncteur ("Switch" -> "Switch"), portes de la salle de bal.
        static readonly string[,] AllowedMsc =
        {
            { "Switch",           "Switch" },        // tele (YARD/.../TV/Switch, JAIL/TV/Switch)
            { "MainSwitch",       "Switch" },        // disjoncteur (YARD/Building/Dynamics/FuseTable/Fusetable/MainSwitch)
            { "dancehut_door*",   "Check position" },
        };
        static int Rows { get { return Allowed.GetLength(0) + (Game.Msc ? AllowedMsc.GetLength(0) : 0); } }
        static string Pat(int i) { int n = Allowed.GetLength(0); return i < n ? Allowed[i, 0] : AllowedMsc[i - n, 0]; }
        static string Tgt(int i) { int n = Allowed.GetLength(0); return i < n ? Allowed[i, 1] : AllowedMsc[i - n, 1]; }

        class Entry { public string Id; public PlayMakerFSM Fsm; public int DoorKind; public FsmBool Door; public bool WasActive, Seen; public float StuckSince = -1f, StuckTime; }   // DoorKind : 0 a lire, 1 porte (Open door / Close door), -1 non
        static readonly Dictionary<string, Entry> byId = new Dictionary<string, Entry>();
        static readonly HashSet<PlayMakerFSM> tracked = new HashSet<PlayMakerFSM>();
        static bool applying;
        static float scanAt = -1;

        // Action injectee : previent a l'entree de l'etat.
        class Hook : ModHook
        {
            public override string Module { get { return "interactions"; } }
            public Entry Target;
            public string StateName;
            public override void OnEnter()
            {
                try
                {
                    if (!applying && Replay.Depth == 0)
                    {
                        if (IsDoor(Target)) pending.Add(new Pending { E = Target, Was = Target.Door.Value, Until = Time.realtimeSinceStartup + 1.5f });
                        else Send(Target, StateName);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            byId.Clear(); tracked.Clear(); pending.Clear();
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 3f : -1;
        }

        static readonly List<KeyValuePair<float, Entry>> checks = new List<KeyValuePair<float, Entry>>();

        // Portes (Markos, MSC 24/10 : « la porte se bloque et devient invisible », journal de l'hote : la porte du magasin
        // ouverte par l'invite restait fermee chez l'hote a chaque fois, DoorOpen False). Rejouer "Check position" refaisait
        // chez chacun le test du verrou avec SES valeurs : horaires du magasin (STORE/OpeningHours.OpenStore), cle de la
        // maison (PlayerKeyHome, a chacun la sienne)... -- ouverte chez l'un, fermee (LOCKED) chez l'autre. Maintenant celui
        // qui clique decide : quand son jeu a ouvert ou ferme la porte (DoorOpen change dans les 1,5 s), il envoie le
        // RESULTAT ("=porte") ; chez les autres, "Open door" ou "Close door" directement (animation, son, DoorOpen), sans
        // verrou. Porte refusee chez lui (fermee a cle) : rien n'est envoye.
        class Pending { public Entry E; public bool Was; public float Until; }
        static readonly List<Pending> pending = new List<Pending>();
        const string DoorResult = "=porte";

        static bool IsDoor(Entry e)
        {
            if (e.DoorKind == 0)
            {
                e.DoorKind = -1;
                try
                {
                    FsmBool b = e.Fsm.FsmVariables.FindFsmBool("DoorOpen");
                    if (b != null && SetsDoor(e.Fsm, "Open door", true) && SetsDoor(e.Fsm, "Close door", false)) { e.Door = b; e.DoorKind = 1; }
                }
                catch { }
            }
            return e.DoorKind == 1 && e.Door != null;
        }

        static bool SetsDoor(PlayMakerFSM f, string state, bool value)
        {
            FsmState st = f.Fsm.GetState(state);
            if (st == null) return false;
            foreach (FsmStateAction a in st.Actions)
            {
                var sb = a as HutongGames.PlayMaker.Actions.SetBoolValue;
                if (sb != null && sb.boolVariable != null && sb.boolVariable.Name == "DoorOpen" && sb.boolValue != null && sb.boolValue.Value == value) return true;
            }
            return false;
        }

        // Portes figees (dossier de Донской, MWC 28/10 : « l'hote ne peut plus ouvrir les portes de l'appartement »). Quand la piece
        // s'eteint de loin puis se rallume (HOMENEW/LOD : l'hote au taxi, puis de retour), l'Animation de ses portes reste figee : chaque
        // PlayAnimation suivant reste « en cours » a t=0 (vitesse 1, poids 1), la porte ne bouge plus et son automate reste dans
        // Open door / Close door, ou aucun clic n'est lu -- pour nos clics comme pour les coups rejoues des autres. Essai porteappart
        // (hote a 300 m puis devant la porte des WC) : figee a chaque coup ; couper puis rallumer l'Animation la remet en marche.
        // Donc : 1) porte suivie qui redevient active -> son Animation est relancee ainsi ; 2) garde : porte active dans Open door /
        // Close door dont l'animation ne joue pas ou n'avance plus depuis 2,5 s -> pose de fin, Animation relancee, automate a Wait player.
        // [Coop] PorteDebloque=0 : ni l'un ni l'autre.
        static float doorWatchAt;
        const float StuckAfter = 2.5f;

        // Porte d'ici mise ouverte (true) ou fermee comme chez un autre joueur (resultat de son clic, ou etat de l'hote a l'arrivee).
        // Objet inactif ici (piece eteinte) : pas par l'etat anime (PlayAnimation sur un objet inactif : rien ne bouge, l'automate
        // reste dans l'etat anime) ; on pose DoorOpen et la fin de l'animation. [Coop] PorteInactive=0 : comme avant.
        static void SetDoor(Entry e, bool open)
        {
            if (!e.Fsm.gameObject.activeInHierarchy && Config.GetInt("Coop", "PorteInactive", 1) != 0)
            {
                e.Door.Value = open;
                PoseDoor(e, open);
                return;
            }
            applying = true; Replay.Depth++;
            try { Game.SetState(e.Fsm, open ? "Open door" : "Close door"); }
            finally { applying = false; Replay.Depth--; }
        }

        // Animation (et clip) que joue l'etat Open door / Close door de la porte (son PlayAnimation).
        static Animation DoorAnim(Entry e, bool open, out AnimationClip clip)
        {
            clip = null;
            FsmState st = e.Fsm.Fsm.GetState(open ? "Open door" : "Close door");
            if (st == null) return null;
            foreach (FsmStateAction a in st.Actions)
            {
                var pa = a as HutongGames.PlayMaker.Actions.PlayAnimation;
                if (pa == null || pa.animName == null) continue;
                GameObject go = e.Fsm.Fsm.GetOwnerDefaultTarget(pa.gameObject);
                Animation an = go != null ? go.GetComponent<Animation>() : null;
                clip = an != null ? an.GetClip(pa.animName.Value) : null;
                return clip != null ? an : null;
            }
            return null;
        }

        // Pose finale de l'animation, sans la jouer. Faux si rien a poser.
        static bool PoseDoor(Entry e, bool open)
        {
            AnimationClip clip;
            Animation an = DoorAnim(e, open, out clip);
            if (an == null) return false;
            an.Stop();
            clip.SampleAnimation(an.gameObject, clip.length);
            return true;
        }

        // Animation de la porte coupee puis rallumee (la remet en marche apres l'extinction de la piece).
        static void Revive(Entry e)
        {
            AnimationClip clip;
            Animation an = DoorAnim(e, true, out clip);
            if (an == null || !an.enabled) return;
            an.enabled = false; an.enabled = true;
        }

        static void Unstick(Entry e)
        {
            string s = e.Fsm.ActiveStateName;
            if (!PoseDoor(e, s == "Open door")) return;
            Revive(e);
            applying = true; Replay.Depth++;
            try { Game.SetState(e.Fsm, "Wait player"); }
            finally { applying = false; Replay.Depth--; }
            Log.Info("interaction " + e.Id + " : porte figee dans " + s + ", debloquee");
        }

        // 2 fois/s : portes suivies rallumees (Revive) et portes figees (Unstick).
        static void WatchDoors()
        {
            float now = Time.realtimeSinceStartup;
            if (now < doorWatchAt) return;
            doorWatchAt = now + 0.5f;
            if (Config.GetInt("Coop", "PorteDebloque", 1) == 0) return;
            foreach (Entry e in byId.Values)
            {
                if (e.Fsm == null || e.DoorKind < 0) continue;
                bool act = e.Fsm.gameObject.activeInHierarchy;
                if (!act) { e.WasActive = false; e.Seen = true; e.StuckSince = -1f; continue; }
                if (!IsDoor(e)) continue;
                if (e.Seen && !e.WasActive) Revive(e);
                e.WasActive = true; e.Seen = true;
                string s = e.Fsm.ActiveStateName;
                if (s != "Open door" && s != "Close door") { e.StuckSince = -1f; continue; }
                // (figee = son animation ne joue pas, ou son temps n'avance plus ; une porte qui s'anime encore, meme lente, est laissee)
                AnimationClip clip;
                Animation an = DoorAnim(e, s == "Open door", out clip);
                AnimationState ast = an != null ? an[clip.name] : null;
                float at = ast != null && ast.enabled ? ast.time : -1f;
                if (e.StuckSince < 0f || at != e.StuckTime) { e.StuckSince = now; e.StuckTime = at; continue; }
                if (now - e.StuckSince > StuckAfter) { e.StuckSince = -1f; Unstick(e); }
            }
        }

        public static void Update()
        {
            if (snapshots.Count > 0) SendSnapshots();
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending pd = pending[i];
                if (pd.E.Fsm == null || pd.E.Door == null) { pending.RemoveAt(i); continue; }
                if (pd.E.Door.Value != pd.Was) { pending.RemoveAt(i); Send(pd.E, DoorResult); continue; }
                if (Time.realtimeSinceStartup > pd.Until) pending.RemoveAt(i);   // (fermee a cle ici : rien)
            }
            for (int i = checks.Count - 1; i >= 0; i--)
            {
                if (Time.realtimeSinceStartup < checks[i].Key) continue;
                Entry c = checks[i].Value;
                checks.RemoveAt(i);
                if (c.Fsm == null) continue;
                FsmBool open = c.Fsm.FsmVariables.GetFsmBool("DoorOpen");
                Log.Info("interaction " + c.Id + " : etat " + c.Fsm.ActiveStateName + (open != null ? ", DoorOpen " + open.Value : "")
                         + ", angle " + c.Fsm.transform.parent.localEulerAngles.ToString("F0"));
            }
            WatchDoors();
            // Nouveau passage toutes les 20 s : les objets lointains (inactifs) ne peuvent etre suivis
            // qu'une fois actives (leur automate n'est pas charge avant).
            if (scanAt < 0 || Time.realtimeSinceStartup < scanAt) return;
            scanAt = Time.realtimeSinceStartup + 20f;
            Scan();
        }

        static void Scan()
        {
            var seen = new Dictionary<string, int>();
            var all = new List<PlayMakerFSM>();
            foreach (Object o in Game.FsmsNamed("Use"))
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Use") continue;
                string n = f.gameObject.name;
                if (n.Contains("(Clone)") || n.Contains("(itemx)")) continue;   // objets crees en jeu : ids differents
                if (!NameAllowed(n)) continue;   // (avant le chemin : releve de 10 ms sinon ; meme chemin = meme nom, rangs inchanges)
                all.Add(f);
            }
            // Ordre stable des deux cotes : chemin, puis rang parmi les homonymes.
            var paths = new List<KeyValuePair<string, PlayMakerFSM>>();
            foreach (PlayMakerFSM f in all) paths.Add(new KeyValuePair<string, PlayMakerFSM>(Recon.Path(f.transform), f));
            paths.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            int hooked = 0;
            foreach (KeyValuePair<string, PlayMakerFSM> kv in paths)
            {
                int k;
                seen.TryGetValue(kv.Key, out k);
                seen[kv.Key] = k + 1;
                string target = AllowedTarget(kv.Value);
                if (target == null) continue;
                var e = new Entry { Id = kv.Key + "#" + k, Fsm = kv.Value };
                if (byId.ContainsKey(e.Id)) continue;
                if (Inject(e, target)) { byId[e.Id] = e; tracked.Add(e.Fsm); hooked++; }
            }
            if (hooked > 0) Log.Info("interactions : " + hooked + " objets de plus suivis (portes, interrupteurs...), " + byId.Count + " en tout");
        }

        static bool NameAllowed(string obj)
        {
            for (int i = 0; i < Rows; i++)
            {
                string pat = Pat(i);
                if (pat.EndsWith("*") ? obj.StartsWith(pat.Substring(0, pat.Length - 1)) : pat == obj) return true;
            }
            return false;
        }

        static string AllowedTarget(PlayMakerFSM f)
        {
            string obj = f.gameObject.name;
            for (int i = 0; i < Rows; i++)
            {
                string pat = Pat(i);
                if (pat.EndsWith("*") ? !obj.StartsWith(pat.Substring(0, pat.Length - 1)) : pat != obj) continue;
                foreach (FsmState s in f.Fsm.States)
                    foreach (FsmTransition t in s.Transitions)
                        if (t.EventName == "USE" && t.ToState == Tgt(i)) return t.ToState;
            }
            return null;
        }

        static bool Inject(Entry e, string stateName)
        {
            if (e.Fsm.Fsm == null || !e.Fsm.Fsm.Initialized) return false;   // (pas encore charge : au prochain passage, sans l'avertissement de PlayMaker a chaque essai)
            FsmState s = e.Fsm.Fsm.GetState(stateName);
            if (s == null) return false;
            try
            {
                FsmHooks.Prepend(s, new Hook { Target = e, StateName = stateName });
                return true;
            }
            catch (Exception) { return false; }   // automate pas encore charge (objet inactif) : au prochain passage
        }

        static void Send(Entry e, string state)
        {
            if (!Session.Active) return;
            var w = new NetWriter(Msg.Interact).U8(Session.LocalId).Str(e.Id).Str(state);
            Bools(e.Fsm, w);
            Session.SendAll(w, true);
        }

        static void Bools(PlayMakerFSM f, NetWriter w)
        {
            FsmBool[] bools = f.FsmVariables.BoolVariables;
            w.U8(bools.Length);
            foreach (FsmBool b in bools) w.Str(b.Name).Bool(b.Value);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str(), state = r.Str();
            int n = r.U8();
            var names = new string[n];
            var vals = new bool[n];
            for (int i = 0; i < n; i++) { names[i] = r.Str(); vals[i] = r.Bool(); }
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.Interact).U8(who).Str(id).Str(state).U8(n);
                for (int i = 0; i < n; i++) w.Str(names[i]).Bool(vals[i]);
                Session.Broadcast(w, true, who);
            }
            Entry e;
            if (!byId.TryGetValue(id, out e) || e.Fsm == null) { if (state != "=etat") Log.Warn("interaction inconnue : " + id); return; }
            // "Police a la porte" (CopsAtHome des poignees) : a chacun le sien (Police) -- la porte ouverte par un joueur pas
            // recherche effacait celui du joueur recherche, qui ne pouvait plus etre arrete de la session.
            for (int i = 0; i < n; i++) if (names[i] == "CopsAtHome") { FsmBool lb = e.Fsm.FsmVariables.GetFsmBool(names[i]); if (lb != null) vals[i] = lb.Value; }
            if (state == "=etat") { if (!Session.IsHost) ApplySnapshot(e, names, vals); return; }
            if (state == DoorResult)
            {
                bool want = false; int at = System.Array.IndexOf(names, "DoorOpen"); if (at >= 0) want = vals[at];
                if (!IsDoor(e)) { Log.Warn("interaction " + id + " : resultat de porte, pas une porte ici"); return; }
                bool was = e.Door.Value;
                if (was != want) SetDoor(e, want);
                checks.Add(new KeyValuePair<float, Entry>(Time.realtimeSinceStartup + 2f, e));
                PlayerSync.ReachFor(who, e.Fsm.transform);
                Log.Info("interaction de #" + who + " : " + id + " " + (want ? "ouverte" : "fermee") + (was == want ? " (deja ici)" : "") + (e.Fsm.gameObject.activeInHierarchy ? "" : " (objet inactif)"));
                return;
            }
            for (int i = 0; i < n; i++)
            {
                FsmBool b = e.Fsm.FsmVariables.GetFsmBool(names[i]);
                if (b != null) b.Value = vals[i];
            }
            applying = true; Replay.Depth++;
            try { Game.SetState(e.Fsm, state); }
            finally { applying = false; Replay.Depth--; }
            checks.Add(new KeyValuePair<float, Entry>(Time.realtimeSinceStartup + 2f, e));
            PlayerSync.ReachFor(who, e.Fsm.transform);   // (son avatar appuie : interrupteur, porte, robinet...)
            Log.Info("interaction de #" + who + " : " + id + " -> " + state + (e.Fsm.gameObject.activeInHierarchy ? "" : " (objet inactif)"));
        }

        // Arrivee d'un invite en cours de partie : l'hote lui envoie l'etat de tout ce qui est suivi.
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        public static void ScheduleSnapshot(Peer p) { snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 12f, p)); }

        static void SendSnapshots()
        {
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (Time.realtimeSinceStartup < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted) continue;
                int n = 0;
                foreach (Entry e in byId.Values)
                {
                    if (e.Fsm == null || e.Fsm.FsmVariables.BoolVariables.Length == 0) continue;
                    var w = new NetWriter(Msg.Interact).U8(0).Str(e.Id).Str("=etat");
                    Bools(e.Fsm, w);
                    Session.T.SendReliable(p, w.ToArray());
                    n++;
                }
                Log.Info("interactions : etat de " + n + " objets envoye a " + p);
            }
        }

        // Invite : objet dans un autre etat que chez l'hote -> on remet la variable decisive a
        // l'oppose de celle de l'hote puis on rejoue l'action : le jeu bascule (porte, interrupteur)
        // avec son animation et son son, et retombe sur l'etat de l'hote.
        static void ApplySnapshot(Entry e, string[] names, bool[] vals)
        {
            // (porte : directement ouverte ou fermee, pas par son verrou -- voir pending)
            int di = System.Array.IndexOf(names, "DoorOpen");
            if (di >= 0 && IsDoor(e))
            {
                if (e.Door.Value != vals[di]) { SetDoor(e, vals[di]); Log.Info("interactions : " + e.Id + " remis comme chez l'hote (" + (vals[di] ? "ouverte" : "fermee") + ")"); }
                return;
            }
            string decisive = null;
            for (int i = 0; i < names.Length; i++)
            {
                FsmBool b = e.Fsm.FsmVariables.GetFsmBool(names[i]);
                if (b == null || b.Value == vals[i]) continue;
                if (decisive == null) decisive = names[i];
                b.Value = vals[i];
            }
            if (decisive == null) return;
            FsmBool d = e.Fsm.FsmVariables.GetFsmBool(decisive);
            d.Value = !d.Value;
            applying = true; Replay.Depth++;
            try { Game.SetState(e.Fsm, AllowedTarget(e.Fsm)); }
            finally { applying = false; Replay.Depth--; }
            Log.Info("interactions : " + e.Id + " remis comme chez l'hote (" + decisive + ")");
        }

        // Automate que ce module prend (ou prendra des qu'il sera actif) : portes, interrupteurs...
        public static bool Wants(PlayMakerFSM f)
        {
            if (f.FsmName != "Use") return false;
            try { return AllowedTarget(f) != null; } catch { return false; }
        }

        public static bool Tracks(PlayMakerFSM f)
        {
            return tracked.Contains(f);
        }

        // Essais : actionne l'objet suivi dont l'identifiant contient 'part' (comme un clic).
        public static string TestNamed(string part)
        {
            foreach (Entry e in byId.Values)
            {
                if (e.Fsm == null || !e.Id.Contains(part)) continue;
                string target = AllowedTarget(e.Fsm);
                Game.SetState(e.Fsm, target);
                checks.Add(new KeyValuePair<float, Entry>(Time.realtimeSinceStartup + 2f, e));
                return e.Id + " -> " + target;
            }
            return "rien pour " + part;
        }

        // Essais : ouvre/ferme la porte la plus proche SANS rien envoyer (desynchronisation voulue).
        public static string TestLocalDoor(Vector3 pos)
        {
            applying = true; Replay.Depth++;
            try { return TestNearestDoor(pos); }
            finally { applying = false; Replay.Depth--; }
        }

        // Essais : declenche la premiere porte proche du joueur (comme un clic).
        public static string TestNearestDoor(Vector3 pos)
        {
            Entry best = null;
            float bd = float.MaxValue;
            foreach (Entry e in byId.Values)
            {
                if (e.Fsm == null || !e.Fsm.gameObject.activeInHierarchy || AllowedTarget(e.Fsm) != "Check position") continue;
                float d = (e.Fsm.transform.position - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            if (best == null) return null;
            Game.SetState(best.Fsm, "Check position");   // la Hook envoie, comme pour un vrai clic
            checks.Add(new KeyValuePair<float, Entry>(Time.realtimeSinceStartup + 2f, best));
            return best.Id;
        }
    }
}
