using System;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Compatibilite du mod « Machtwagen 300D » (Homura et Bogle911 ; retour de joueurs, 10/10 : « on n'arrive pas a refermer
    // les portes »). Le mod copie le taxi (« Machtwagen300D ») et ajoute un verrouillage centralise (CentralLocking) : il met
    // DoorLockedCheck en tete de l'etat « Open door » de chaque portiere (et de la trappe a essence) puis, verrouille, coupe
    // les 8 actions qui suivent (Actions[1..8] ; 4 pour la trappe). Deux soucis en coop :
    //  - CarDoors ajoute aussi son action en tete de « Open door » : les numeros glissent d'un cran -- le mod coupait sa propre
    //    verification et laissait une action du jeu, la porte a moitie bloquee ;
    //  - le verrouillage restait a celui qui verrouille : chez lui les portes ouvertes par les autres ne bougeaient plus.
    // Ici : l'etat verrouille est envoye a chaque changement et par l'hote toutes les 10 s (@300d), applique ailleurs
    // (LockDoors / UnlockDoors du mod), et les actions sont remises d'apres la place reelle de DoorLockedCheck.
    public static class ModMachtwagen300D
    {
        static Component locking;
        static FieldInfo fLocked;
        static float nextLook, nextHost;
        static bool last, known, applying;

        public static void OnLevelLoaded() { locking = null; known = false; nextLook = 0f; parts.Clear(); partsAt = 0f; keysSent = false; nextParts = 0f; testStep = 0; }

        // ---------------------------------------------------------------- equipements (retour de Menos, 10/10 : toit ouvrant,
        // boite automatique, boite a gants, compartiments, vitres non synchronises ; la cle « partagee »).
        // Chacun garde son etat dans un champ que le mod ne change qu'a l'arret d'un mouvement : envoye a chaque changement
        // (@300f type, chemin, valeur), applique ailleurs avec les fonctions du mod (pose de l'animation, levier deplace).
        // Cle : un seul porteur -- celui qui la prend (keysGot passe a vrai) l'annonce (@300k), les autres ne l'ont plus ;
        // l'hote l'annonce au chargement s'il l'a.
        class Part { public string Type, Path; public Component C; public float Last = float.NaN; }
        static readonly System.Collections.Generic.List<Part> parts = new System.Collections.Generic.List<Part>();
        static float partsAt, nextParts;
        static Component saveVars; static bool keysWas, keysSent;
        static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static void ScanParts()
        {
            parts.Clear(); saveVars = null;
            // (objets inactifs compris : la boite automatique n'est active qu'au volant ; pas les modeles des AssetBundles)
            foreach (MonoBehaviour m in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (m == null || m.GetType().Namespace != "Machtwagen300D_MWC" || m.hideFlags != HideFlags.None) continue;
                if (!m.transform.root.gameObject.activeInHierarchy && m.transform.root.name != "Machtwagen300D") continue;
                string tn = m.GetType().Name;
                if (tn == "SaveVars") { saveVars = m; continue; }
                if (tn == "ElectricSunroof" || tn == "PowerWindow" || tn == "OpenableCompartment" || tn == "Machtmatic")
                    parts.Add(new Part { Type = tn, Path = Recon.Path(m.transform), C = m });
            }
            if (saveVars != null) keysWas = Get<bool>(saveVars, "keysGot");
            Log.Info("mods : Machtwagen 300D, " + parts.Count + " equipements suivis (toit, vitres, compartiments, boite auto) et la cle");
        }

        static T Get<T>(object o, string f) { FieldInfo fi = o.GetType().GetField(f, Any); object v = fi != null ? fi.GetValue(o) : null; return v is T ? (T)v : default(T); }
        static void SetF(object o, string f, object v) { FieldInfo fi = o.GetType().GetField(f, Any); if (fi != null) fi.SetValue(o, v); }
        static void CallM(object o, string m, params object[] a) { MethodInfo mi = o.GetType().GetMethod(m, Any); if (mi != null) try { mi.Invoke(o, a); } catch (Exception e) { Log.Warn("mods : 300D " + m + " : " + (e.InnerException ?? e).Message); } }

        // Valeur d'etat (stable : rien en mouvement), NaN si en mouvement.
        static float StateOf(Part p)
        {
            switch (p.Type)
            {
                case "ElectricSunroof":
                    if (Get<bool>(p.C, "moving") || Get<bool>(p.C, "movingTilt")) return float.NaN;
                    float tilt = Get<float>(p.C, "TiltState");
                    return tilt > 0f ? -tilt : Get<float>(p.C, "WindowState");   // (negatif : entrebaille)
                case "PowerWindow":
                    if (Get<bool>(p.C, "moving")) return float.NaN;
                    object st = Get<object>(p.C, "StateM");
                    return st != null ? Get<float>(st, "State") : float.NaN;
                case "OpenableCompartment":
                    Animation an = Get<Animation>(p.C, "anim");
                    if (an != null && an.isPlaying) return float.NaN;
                    return (Get<bool>(p.C, "Opened") ? 1f : 0f) + (Get<bool>(p.C, "Locked") ? 2f : 0f);
                case "Machtmatic":
                    return Get<int>(p.C, "ATGear");
            }
            return float.NaN;
        }

        static void ApplyState(Part p, float v)
        {
            switch (p.Type)
            {
                case "ElectricSunroof":
                    if (v < 0f)
                    {
                        Animation a = Get<Animation>(p.C, "anim"); string clip = Get<string>(p.C, "animnameTilt");
                        SetF(p.C, "WindowState", 0f); SetF(p.C, "TiltState", -v);
                        if (a != null && clip != null && a[clip] != null) { a.Play(clip); a[clip].time = -v / 60f; a[clip].speed = 0f; }
                    }
                    else { SetF(p.C, "TiltState", 0f); CallM(p.C, "SetWindowState", v); }
                    break;
                case "PowerWindow": CallM(p.C, "SetWindowState", v); break;
                case "OpenableCompartment":
                    {
                        bool open = (((int)v) & 1) != 0, locked = (((int)v) & 2) != 0;
                        if (Get<bool>(p.C, "Opened") != open)
                        {
                            Animation a = Get<Animation>(p.C, "anim");
                            string clip = Get<string>(p.C, open ? "open_name" : "close_name");
                            if (a != null && clip != null) a.Play(clip);
                            SetF(p.C, "Opened", open);
                            GameObject light = Get<GameObject>(p.C, "light");
                            if (light != null) light.SetActive(open);
                        }
                        SetF(p.C, "Locked", locked);
                        break;
                    }
                case "Machtmatic":
                    SetF(p.C, "ATGear", (int)v);
                    CallM(p.C, "MoveShifterPos");
                    break;
            }
        }

        static void UpdateParts()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextParts) return;
            nextParts = now + 0.25f;
            if (parts.Count == 0 || saveVars == null) { if (now >= partsAt) { partsAt = now + 10f; ScanParts(); } if (parts.Count == 0) return; }
            foreach (Part p in parts)
            {
                if (p.C == null) continue;
                float v = StateOf(p);
                if (float.IsNaN(v)) continue;
                if (float.IsNaN(p.Last)) { p.Last = v; continue; }   // (premier releve : etat du chargement, le meme chez tous)
                if (Mathf.Abs(v - p.Last) < 0.01f) continue;
                p.Last = v;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@300f").Str(p.Type).Str(p.Path).F32(v), true);
                Log.Info("mods : 300D " + p.Type + " " + v.ToString("F1") + " ici (" + p.Path + ")");
            }
            if (saveVars != null)
            {
                bool k = Get<bool>(saveVars, "keysGot");
                if (k && (!keysWas || Session.IsHost && !keysSent))
                {
                    keysSent = true;
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@300k"), true);
                    Log.Info("mods : 300D, la cle est ici");
                }
                keysWas = k;
            }
        }

        public static void OnPart(int who, string type, string path, float v)
        {
            if (parts.Count == 0) ScanParts();
            foreach (Part p in parts)
                if (p.Type == type && p.Path == path && p.C != null)
                {
                    applying = true;
                    try { ApplyState(p, v); } finally { applying = false; }
                    p.Last = v;
                    Log.Info("mods : 300D " + type + " " + v.ToString("F1") + " de #" + who);
                    return;
                }
        }

        // [Test] Autotest=300d : l'hote, a 40 s, ouvre un compartiment, baisse une vitre a moitie, ouvre le toit a moitie et passe
        // le levier en D ; chacun note a 55 s l'etat de chaque equipement.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "300d") return;
            if (parts.Count == 0) return;
            if (Session.IsHost && t > 40f && testStep == 0)
            {
                testStep = 1;
                bool comp = false, win = false, roof = false, gear = false;
                foreach (Part p in parts)
                {
                    if (p.Type == "OpenableCompartment" && !comp) { comp = true; CallM(p.C, "OpenGlovebox"); }
                    if (p.Type == "PowerWindow" && !win) { win = true; CallM(p.C, "SetWindowState", 30f); }
                    if (p.Type == "ElectricSunroof" && !roof) { roof = true; CallM(p.C, "SetWindowState", 40f); }
                    if (p.Type == "Machtmatic" && !gear) { gear = true; SetF(p.C, "ATGear", 3); CallM(p.C, "MoveShifterPos"); }
                }
                Log.Info("autotest : 300d, equipements changes ici");
            }
            if (t > 55f && testStep < 2)
            {
                testStep = 2;
                var sb = new System.Text.StringBuilder("autotest : 300d, etats :");
                foreach (Part p in parts) sb.Append(' ').Append(p.Type).Append('=').Append(StateOf(p).ToString("F1"));
                Log.Info(sb.ToString());
            }
        }

        public static void OnKeys(int who)
        {
            if (saveVars == null) ScanParts();
            if (saveVars == null) return;
            SetF(saveVars, "keysGot", false);
            keysWas = false;
            // (la cle posee a prendre, s'il y en a une ici : prise par l'autre)
            object pk = Get<object>(saveVars, "keys");
            Component pkc = pk as Component;
            if (pkc != null && pkc.gameObject.activeSelf) pkc.gameObject.SetActive(false);
            Log.Info("mods : 300D, la cle est chez #" + who + " (plus ici)");
        }

        static bool Find()
        {
            if (locking != null) return true;
            if (Time.realtimeSinceStartup < nextLook) return false;
            nextLook = Time.realtimeSinceStartup + 5f;
            foreach (MonoBehaviour m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (m != null && m.GetType().Name == "CentralLocking" && m.GetType().Namespace == "Machtwagen300D_MWC") { locking = m; break; }
            if (locking == null) return false;
            fLocked = locking.GetType().GetField("doorsLocked");
            Log.Info("mods : Machtwagen 300D present, verrouillage centralise synchronise");
            return fLocked != null;
        }

        static bool Locked { get { return fLocked != null && locking != null && (bool)fLocked.GetValue(locking); } }

        // Actions coupees par le verrouillage, d'apres la place de DoorLockedCheck dans l'etat (et non Actions[1..n]).
        static void Fix()
        {
            bool on = !Locked;
            foreach (string name in new[] { "flState", "frState", "rlState", "rrState", "fuelState" })
            {
                FieldInfo fi = locking.GetType().GetField(name);
                FsmState s = fi != null ? fi.GetValue(locking) as FsmState : null;
                if (s == null || s.Actions == null) continue;
                int at = -1;
                for (int i = 0; i < s.Actions.Length; i++) if (s.Actions[i] != null && s.Actions[i].GetType().Name == "DoorLockedCheck") { at = i; break; }
                if (at < 0) continue;
                int n = name == "fuelState" ? 4 : 8;
                for (int i = 0; i < s.Actions.Length; i++)
                {
                    if (s.Actions[i] == null) continue;
                    if (i <= at) s.Actions[i].Enabled = true;                 // (actions d'avant -- celle de CarDoors -- et la verification)
                    else if (i <= at + n) s.Actions[i].Enabled = on;
                }
            }
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Find()) return;
            UpdateParts();
            bool l = Locked;
            float now = Time.realtimeSinceStartup;
            if (!known || l != last)
            {
                bool changed = known;
                known = true; last = l;
                Fix();
                if (changed && !applying)
                {
                    Log.Info("mods : Machtwagen 300D " + (l ? "verrouillee" : "deverrouillee") + " ici");
                    Send(l, true);
                }
            }
            if (Session.IsHost && now >= nextHost) { nextHost = now + 10f; Send(l, false); }
        }

        static void Send(bool l, bool reliable) { Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@300d").Bool(l), reliable); }

        public static void OnRemote(int who, bool l)
        {
            if (!Find() || Locked == l) return;
            applying = true;
            try
            {
                MethodInfo m = locking.GetType().GetMethod(l ? "LockDoors" : "UnlockDoors");
                if (m != null) m.Invoke(locking, new object[] { true });
            }
            catch (Exception e) { Log.Warn("mods : Machtwagen 300D : " + (e.InnerException ?? e).Message); }
            finally { applying = false; }
            last = Locked; known = true;
            Fix();
            Log.Info("mods : Machtwagen 300D " + (l ? "verrouillee" : "deverrouillee") + " par #" + who);
        }
    }
}
