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

        public static void OnLevelLoaded() { locking = null; known = false; nextLook = 0f; parts.Clear(); partsAt = 0f; keysSent = false; nextParts = 0f; testStep = 0; glides.Clear(); ign = null; ignCol = null; keyInWas = false; ignLook = 0f; keyHolder = -1; nextKeyAnnounce = 0f; }

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

        // Types du mod, cherches dans son assembly (charge par MSCLoader) : avant, tous les scripts de la scene etaient passes
        // en revue toutes les 5 s, chez tout le monde, mod installe ou non (retour de joueurs 10/10 : performances).
        static Assembly modAsm;
        static float asmLook;
        static Type ModType(string name)
        {
            if (modAsm == null)
            {
                if (Time.realtimeSinceStartup < asmLook) return null;
                asmLook = Time.realtimeSinceStartup + 30f;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = null;
                    try { t = a.GetType("Machtwagen300D_MWC.CentralLocking", false); } catch { }
                    if (t != null) { modAsm = a; break; }
                }
                if (modAsm == null) return null;
            }
            return modAsm.GetType("Machtwagen300D_MWC." + name, false);
        }

        static readonly string[] PartTypes = { "SaveVars", "ElectricSunroof", "PowerWindow", "OpenableCompartment", "Machtmatic", "AutoAC",
                                               "Sunvisor", "InteriorLight", "FrontFogSwitch", "RecircSwitch", "DefrosterSwitch", "RearDomeLightSwitch", "HornToneSwitch", "HornButton",
                                               "OpenableTrunk", "GloveboxLock" };
        // Interrupteurs a deux positions (SwitchOn ; retour de Menos, 10/10 : pare-soleil et le reste non synchronises).
        static bool IsSwitch(string t) { return t == "FrontFogSwitch" || t == "RecircSwitch" || t == "DefrosterSwitch" || t == "RearDomeLightSwitch" || t == "HornToneSwitch"; }

        static void ScanParts()
        {
            parts.Clear(); saveVars = null; glides.Clear();
            // (objets inactifs compris : la boite automatique n'est active qu'au volant ; pas les modeles des AssetBundles)
            foreach (string tn in PartTypes)
            {
                Type t = ModType(tn);
                if (t == null) continue;
                foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(t))
                {
                    var m = o as MonoBehaviour;
                    if (m == null || m.hideFlags != HideFlags.None) continue;
                    if (!m.transform.root.gameObject.activeInHierarchy && m.transform.root.name != "Machtwagen300D") continue;
                    if (tn == "SaveVars") { saveVars = m; continue; }
                    parts.Add(new Part { Type = tn, Path = Recon.Path(m.transform), C = m });
                }
            }
            // Trappe a essence (automate du jeu, copie du taxi ; retour de Menor, 11/10 : pas synchronisee) : ouverte ou non.
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = o as PlayMakerFSM;
                if (f == null || f.hideFlags != HideFlags.None || f.FsmName != "Use" || f.transform.root.name != "Machtwagen300D") continue;
                if (f.gameObject.name == "Trigger" && f.transform.parent != null && f.transform.parent.parent != null && f.transform.parent.parent.name == "FuelFiller")
                    parts.Add(new Part { Type = "FuelCap", Path = Recon.Path(f.transform), C = f });
            }
            if (saveVars != null) keysWas = Get<bool>(saveVars, "keysGot");
            Log.Info("mods : Machtwagen 300D, " + parts.Count + " equipements suivis (toit, vitres, compartiments, boite auto, climatisation) et la cle");
        }

        static T Get<T>(object o, string f) { FieldInfo fi = o.GetType().GetField(f, Any); object v = fi != null ? fi.GetValue(o) : null; return v is T ? (T)v : default(T); }
        static void SetF(object o, string f, object v) { FieldInfo fi = o.GetType().GetField(f, Any); if (fi != null) fi.SetValue(o, v); }
        static void CallM(object o, string m, params object[] a) { MethodInfo mi = o.GetType().GetMethod(m, Any); if (mi != null) try { mi.Invoke(o, a); } catch (Exception e) { Log.Warn("mods : 300D " + m + " : " + (e.InnerException ?? e).Message); } }

        // Valeur d'etat ; toit et vitres : aussi pendant le mouvement (position de l'animation), suivie chez les autres qui
        // l'animent a la meme vitesse (« l'animation du toit ne marche pas », Menos, 10/10 : il sautait a sa place finale).
        static float StateOf(Part p)
        {
            switch (p.Type)
            {
                case "ElectricSunroof":
                    {
                        Animation a = Get<Animation>(p.C, "anim");
                        if (Get<bool>(p.C, "moving")) return ClipTime(a, Get<string>(p.C, "animname")) * 60f;
                        if (Get<bool>(p.C, "movingTilt")) return -ClipTime(a, Get<string>(p.C, "animnameTilt")) * 60f;
                        float tilt = Get<float>(p.C, "TiltState");
                        return tilt > 0f ? -tilt : Get<float>(p.C, "WindowState");   // (negatif : entrebaille)
                    }
                case "PowerWindow":
                    {
                        if (Get<bool>(p.C, "moving")) return ClipTime(Get<Animation>(p.C, "anim"), Get<string>(p.C, "animname")) * 60f;
                        object st = Get<object>(p.C, "StateM");
                        return st != null ? Get<float>(st, "State") : float.NaN;
                    }
                case "Sunvisor": return Get<float>(p.C, "currentPos");
                case "HornButton": { AudioSource hs = Get<AudioSource>(p.C, "hornsound"); return hs != null && hs.isPlaying ? 1f : 0f; }   // (klaxon du tableau de bord : retour de Menos 10/10)
                case "InteriorLight": return Get<int>(p.C, "LightState");
                case "FrontFogSwitch": case "RecircSwitch": case "DefrosterSwitch": case "RearDomeLightSwitch": case "HornToneSwitch":
                    return Get<bool>(p.C, "SwitchOn") ? 1f : 0f;
                case "AutoAC":
                    {
                        // ventilation (1 auto, 2 mini, 3 maxi, 4 arret) x 10 + direction (1 degivrage, 2 normal, 3 haut-bas)
                        int fan = Get<bool>(p.C, "OFF") ? 4 : Get<bool>(p.C, "AutoMode") ? 1 : Get<bool>(p.C, "MinFan") ? 2 : Get<bool>(p.C, "MaxFan") ? 3 : 0;
                        int dir = Get<bool>(p.C, "Defrost") ? 1 : Get<bool>(p.C, "Normal") ? 2 : Get<bool>(p.C, "UpDown") ? 3 : 0;
                        return fan * 10 + dir;
                    }
                case "OpenableCompartment":
                    Animation an = Get<Animation>(p.C, "anim");
                    if (an != null && an.isPlaying) return float.NaN;
                    return (Get<bool>(p.C, "Opened") ? 1f : 0f) + (Get<bool>(p.C, "Locked") ? 2f : 0f);
                case "Machtmatic":
                    return Get<int>(p.C, "ATGear");
                // Coffre (retour de Menor, 11/10 : pas synchronise) : ouvert ou non ; pas pendant qu'il s'ouvre ou se ferme.
                case "OpenableTrunk":
                    if (Get<bool>(p.C, "isOpening") || Get<bool>(p.C, "isClosing")) return float.NaN;
                    return Get<bool>(p.C, "tailgateopen") ? 1f : 0f;
                case "GloveboxLock": return float.NaN;   // (serrure : coupee sans la cle, voir Ignition)
                case "FuelCap": { FsmBool d = ((PlayMakerFSM)p.C).FsmVariables.FindFsmBool("DoorOpen"); return d != null ? (d.Value ? 1f : 0f) : float.NaN; }
            }
            return float.NaN;
        }

        static float ClipTime(Animation a, string clip) { return a != null && clip != null && a[clip] != null ? a[clip].time : 0f; }

        // Pose finale (toit : negatif = entrebaille).
        static void Pose(Part p, float v)
        {
            if (p.Type == "PowerWindow") { CallM(p.C, "SetWindowState", v); return; }
            if (v < 0f)
            {
                Animation a = Get<Animation>(p.C, "anim"); string clip = Get<string>(p.C, "animnameTilt");
                SetF(p.C, "WindowState", 0f); SetF(p.C, "TiltState", -v);
                if (a != null && clip != null && a[clip] != null) { a.Play(clip); a[clip].time = -v / 60f; a[clip].speed = 0f; }
            }
            else { SetF(p.C, "TiltState", 0f); CallM(p.C, "SetWindowState", v); }
        }

        // Toit et vitres recus : animes jusqu'a la valeur recue, a la vitesse du mod (avec un peu d'avance pour rattraper), avec
        // le bruit du moteur electrique ; la valeur peut changer en route (positions envoyees pendant le mouvement).
        class Glide { public Part P; public float Cur, Target; public AudioSource Snd; }
        static readonly System.Collections.Generic.Dictionary<Part, Glide> glides = new System.Collections.Generic.Dictionary<Part, Glide>();

        static float Current(Part p)
        {
            if (p.Type == "PowerWindow") { object st = Get<object>(p.C, "StateM"); return st != null ? Get<float>(st, "State") : 0f; }
            float tilt = Get<float>(p.C, "TiltState");
            return tilt > 0f ? -tilt : Get<float>(p.C, "WindowState");
        }

        static void StartGlide(Part p, float v)
        {
            Glide g;
            if (!glides.TryGetValue(p, out g))
            {
                g = new Glide { P = p, Cur = Current(p) };
                if (Mathf.Abs(g.Cur - v) < 0.5f) { Pose(p, v); return; }
                g.Snd = Get<AudioSource>(p.C, p.Type == "ElectricSunroof" && (v < 0f || g.Cur < 0f) ? "tiltSnd" : "sound");
                if (g.Snd != null && !g.Snd.isPlaying) g.Snd.Play();
                glides[p] = g;
            }
            g.Target = v;
        }

        static readonly System.Collections.Generic.List<Glide> glideDone = new System.Collections.Generic.List<Glide>();
        static void UpdateGlides()
        {
            if (glides.Count == 0) return;
            glideDone.Clear();
            foreach (Glide g in glides.Values)
            {
                Part p = g.P;
                if (p.C == null) { glideDone.Add(g); continue; }
                // (vitesses du mod : toit 0,4 et entrebaillement 0,35 de clip par seconde, soit 24 et 21 crans/s ; vitres de meme)
                float rate = (p.Type == "ElectricSunroof" && (g.Target < 0f || g.Cur < 0f) ? 21f : 24f) * 1.15f;
                // Du toit ouvert a l'entrebaille (ou l'inverse) : par la position fermee.
                float goal = p.Type == "ElectricSunroof" && g.Cur * g.Target < 0f ? 0f : g.Target;
                g.Cur = Mathf.MoveTowards(g.Cur, goal, rate * Time.deltaTime);
                Animation a = Get<Animation>(p.C, "anim");
                string clip = p.Type == "ElectricSunroof" && g.Cur < 0f ? Get<string>(p.C, "animnameTilt") : Get<string>(p.C, "animname");
                if (a != null && clip != null && a[clip] != null)
                {
                    if (!a.IsPlaying(clip)) a.Play(clip);
                    a[clip].time = Mathf.Abs(g.Cur) / 60f; a[clip].speed = 0f;
                }
                if (Mathf.Abs(g.Cur - g.Target) < 0.01f) glideDone.Add(g);
            }
            foreach (Glide g in glideDone)
            {
                glides.Remove(g.P);
                if (g.P.C == null) continue;
                applying = true;
                try { Pose(g.P, g.Target); } finally { applying = false; }
                g.P.Last = g.Target;
                if (g.Snd != null && g.Snd.isPlaying) g.Snd.Stop();
                AudioSource fin = Get<AudioSource>(g.P.C, "fin");
                if (fin != null && (Mathf.Abs(g.Target) < 0.05f || Mathf.Abs(g.Target) > 59.9f)) fin.Play();
            }
        }

        static void ApplyState(Part p, float v)
        {
            switch (p.Type)
            {
                case "ElectricSunroof":
                case "PowerWindow":
                    StartGlide(p, v);
                    break;
                case "Sunvisor": CallM(p.C, "SetVisorPos", v); break;
                case "HornButton": { AudioSource hs = Get<AudioSource>(p.C, "hornsound"); if (hs != null) { if (v > 0.5f && !hs.isPlaying) hs.Play(); else if (v < 0.5f && hs.isPlaying) hs.Stop(); } break; }
                case "InteriorLight": CallM(p.C, "LoadLightState", (int)v); break;
                case "FrontFogSwitch": case "RecircSwitch": case "DefrosterSwitch": case "RearDomeLightSwitch": case "HornToneSwitch":
                    {
                        bool on = v > 0.5f;
                        if (Get<bool>(p.C, "SwitchOn") == on) break;
                        Transform sw = Get<Transform>(p.C, "switchTransform");
                        if (sw != null) sw.localEulerAngles = new Vector3(on ? -25f : 0f, 0f, 0f);
                        SetF(p.C, "SwitchOn", on);
                        GameObject fx = Get<GameObject>(p.C, p.Type == "FrontFogSwitch" ? "fogs" : "light");
                        if (fx != null && (p.Type == "FrontFogSwitch" || p.Type == "RearDomeLightSwitch")) fx.SetActive(on);
                        if (p.Type == "DefrosterSwitch") { FsmBool b = Get<FsmBool>(p.C, "defrosterBool"); if (b != null) b.Value = on; }
                        if (p.Type == "HornToneSwitch")
                        {
                            if (on) CallM(p.C, "SetHorn1");
                            else
                            {
                                AudioSource hs = Get<AudioSource>(p.C, "hornSnd"); AudioClip h0 = Get<AudioClip>(p.C, "horn0");
                                if (hs != null) { if (h0 != null) hs.clip = h0; hs.volume = 1f; hs.pitch = 0.9f; }
                            }
                        }
                        break;
                    }
                case "AutoAC":
                    {
                        int fan = (int)v / 10, dir = (int)v % 10;
                        if (fan == 4) { CallM(p.C, "setOFF"); break; }
                        if (fan == 1) CallM(p.C, "SetAuto"); else if (fan == 2) CallM(p.C, "SetMinFan"); else if (fan == 3) CallM(p.C, "SetMaxFan");
                        if (dir == 1) CallM(p.C, "SetDefrost"); else if (dir == 2) CallM(p.C, "SetNormal"); else if (dir == 3) CallM(p.C, "SetUpDown");
                        break;
                    }
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
                case "FuelCap":
                    {
                        var ff = (PlayMakerFSM)p.C;
                        FsmBool d = ff.FsmVariables.FindFsmBool("DoorOpen");
                        if (d == null || d.Value == v > 0.5f || ff.Fsm == null || !ff.Fsm.Initialized) break;
                        Replay.Depth++;
                        try { Game.SetState(ff, v > 0.5f ? "Open door" : "Close door"); } finally { Replay.Depth--; }
                        break;
                    }
                case "OpenableTrunk":
                    {
                        // Comme le mod au clic : ouvert -- butees ouvertes, couple d'ouverture jusqu'au cran (60 degres), lumiere ;
                        // ferme -- butees ouvertes, il retombe, et le mod le verrouille ferme sous 2 degres.
                        bool open = v > 0.5f;
                        if (Get<bool>(p.C, "tailgateopen") == open) break;
                        HingeJoint hj = Get<HingeJoint>(p.C, "hingejoint");
                        if (hj != null) hj.limits = Get<JointLimits>(p.C, "openlimits");
                        if (open)
                        {
                            SetF(p.C, "tailgateopen", true); SetF(p.C, "isOpening", true);
                            GameObject lt = Get<GameObject>(p.C, "light"); if (lt != null) lt.SetActive(true);
                        }
                        else SetF(p.C, "isClosing", true);
                        Rigidbody tr = Get<Rigidbody>(p.C, "rig"); if (tr != null) tr.WakeUp();
                        break;
                    }
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
                if (p.C == null || glides.ContainsKey(p)) continue;   // (anime ici d'apres un autre)
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
                Ignition();
                bool k = Get<bool>(saveVars, "keysGot");
                if (k && (!keysWas || Session.IsHost && !keysSent))
                {
                    keysSent = true;
                    keyHolder = Session.LocalId;
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@300k"), true);
                    Log.Info("mods : 300D, la cle est ici");
                }
                keysWas = k;
                // Hote : le porteur redit toutes les 10 s (« la cle est partagee », Menos 10/10 : un invite qui chargeait le mod
                // apres l'annonce de l'hote gardait la cle de la sauvegarde -- deux porteurs).
                if (Session.IsHost && keyHolder >= 0 && Time.realtimeSinceStartup >= nextKeyAnnounce)
                {
                    nextKeyAnnounce = Time.realtimeSinceStartup + 10f;
                    Session.SendAll(new NetWriter(Msg.Job).U8((byte)keyHolder).Str("@300k"), true);
                }
            }
        }

        // Cle sur le contact (retour de Menos, 10/10 : « mon ami ne peut pas prendre la cle quand elle est sur le contact ») : le
        // mod n'allume le contact (collisionneur IGNITIONx...) que chez le porteur de la cle. Cle sur le contact : contact allume
        // chez tous -- chacun peut la tourner ou la retirer ; celui qui la retire (clic d'ici) en devient le porteur (@300k).
        static PlayMakerFSM ign;
        static Collider ignCol;
        static bool keyInWas;
        static float ignLook;
        static readonly System.Collections.Generic.HashSet<string> KeyIn = new System.Collections.Generic.HashSet<string> { "ACC on", "Wait2", "Wait START", "Motor starting", "Shut off", "State 1" };
        static void Ignition()
        {
            if (ign == null)
            {
                if (locking == null || Time.realtimeSinceStartup < ignLook) return;
                ignLook = Time.realtimeSinceStartup + 10f;
                foreach (Transform t in locking.transform.root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "IGNITIONxMachtwagen300D") { ign = Game.FsmOn(t.gameObject, "Use"); ignCol = t.GetComponent<Collider>(); break; }
                if (ign == null) return;
                Log.Info("mods : 300D, contact trouve (" + Recon.Path(ign.transform) + ", etat " + ign.ActiveStateName + ")");
            }
            bool holder = Get<bool>(saveVars, "keysGot");
            bool keyIn = KeyIn.Contains(ign.ActiveStateName);
            if (ignCol != null && ignCol.enabled != (holder || keyIn)) ignCol.enabled = holder || keyIn;
            // Serrure de la boite a gants : seulement chez le porteur de la cle (retour de Menor, 11/10 : « mon ami a la cle,
            // je ne peux rien deverrouiller, mais je peux encore verrouiller la boite a gants »).
            foreach (Part p in parts)
                if (p.Type == "GloveboxLock" && p.C != null)
                {
                    Collider lc = Get<Collider>(p.C, "col");
                    if (lc != null && lc.enabled != holder) lc.enabled = holder;
                }
            if (keyInWas && !keyIn && !holder && Jobs.KeyTurnedHere(ign.transform, 3f))
            {
                SetF(saveVars, "keysGot", true);
                Log.Info("mods : 300D, cle retiree du contact ici : elle est ici");
            }
            keyInWas = keyIn;
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
                    if (type != "Machtmatic" && type != "SaveVars") PlayerSync.ReachFor(who, p.C.transform);   // (son avatar appuie : retour de Menor, 11/10)
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
                if (Locked) { MethodInfo um = locking.GetType().GetMethod("UnlockDoors"); if (um != null) um.Invoke(locking, new object[] { true }); Log.Info("autotest : 300d, deverrouille"); }
                bool comp = false, win = false, roof = false, gear = false, ac = false, vis = false, fog = false;
                foreach (Part p in parts)
                {
                    if (p.Type == "OpenableCompartment" && !comp) { comp = true; CallM(p.C, "OpenGlovebox"); }
                    if (p.Type == "PowerWindow" && !win) { win = true; CallM(p.C, "SetWindowState", 30f); }
                    if (p.Type == "ElectricSunroof" && !roof) { roof = true; CallM(p.C, "SetWindowState", 40f); }
                    if (p.Type == "HornButton") { AudioSource hs = Get<AudioSource>(p.C, "hornsound"); if (hs != null) hs.Play(); }
                    if (p.Type == "Sunvisor" && !vis) { vis = true; CallM(p.C, "SetVisorPos", 90f); }
                    if (p.Type == "FrontFogSwitch" && !fog) { fog = true; CallM(p.C, "LoadSwitchOn"); }
                    if (p.Type == "AutoAC" && !ac) { ac = true; CallM(p.C, "SetMinFan"); CallM(p.C, "SetDefrost"); }
                    if (p.Type == "Machtmatic" && !gear) { gear = true; SetF(p.C, "ATGear", 3); CallM(p.C, "MoveShifterPos"); }
                    if (p.Type == "OpenableTrunk") ApplyState(p, 1f);   // (comme un clic : il s'ouvre, l'etat part une fois ouvert)
                    if (p.Type == "FuelCap") { var ff = (PlayMakerFSM)p.C; if (ff.Fsm != null && ff.Fsm.Initialized) Game.SetState(ff, "Open door"); }
                }
                Log.Info("autotest : 300d, equipements changes ici");
            }
            if (t > 55f && testStep < 2)
            {
                testStep = 2;
                var sb = new System.Text.StringBuilder("autotest : 300d, etats :");
                foreach (Part p in parts) sb.Append(' ').Append(p.Type).Append('=').Append(StateOf(p).ToString("F1"));
                // (trappe a essence : automates de LOD/FuelFiller, leur etat ; serrure de la boite a gants : active ou non)
                foreach (Transform tf in locking != null ? locking.transform.root.GetComponentsInChildren<Transform>(true) : new Transform[0])
                    if (tf.name == "FuelFiller") foreach (PlayMakerFSM f in tf.GetComponentsInChildren<PlayMakerFSM>(true)) sb.Append(" | trappe ").Append(Recon.Path(f.transform)).Append("::").Append(f.FsmName).Append(' ').Append(f.ActiveStateName);
                foreach (Part p in parts) if (p.Type == "GloveboxLock") { Collider lc = Get<Collider>(p.C, "col"); sb.Append(" | serrure boite a gants ").Append(lc != null ? (lc.enabled ? "active" : "coupee") : "?"); }
                Log.Info(sb.ToString());
            }
        }

        static int keyHolder = -1;
        static float nextKeyAnnounce;
        public static void OnKeys(int who)
        {
            keyHolder = who;
            if (who == Session.LocalId) return;   // (rappel de l'hote : c'est nous)
            if (saveVars == null) ScanParts();
            if (saveVars == null) return;
            if (!Get<bool>(saveVars, "keysGot") && !keysWas) return;   // (deja sans cle : rien a dire)
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
            Type lt = ModType("CentralLocking");
            if (lt == null) return false;
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(lt)) { locking = o as Component; if (locking != null) break; }
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
            UpdateGlides();
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
