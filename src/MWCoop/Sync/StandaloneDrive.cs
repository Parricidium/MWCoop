using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Contrat de conduite de la voiture standalone (spec du 28/10, §2 et « Conduite ») : ce que fait l'automate DriveTrigger des
    // voitures de MSC (releve sur la Ruscko et le bateau, ref/msc-v.250908-04), fait par notre code -- rien de copie :
    //  - zone d'entree : a cote de la portiere conducteur (la caisse est fermee : boites de collision), voiture presque arretee et
    //    sur ses roues -> GUIdrive + « ENTER DRIVING MODE » ; touche du jeu DrivingMode (cInput) pour monter ;
    //  - au volant : PlayerStop, PlayerCurrentVehicle (« R5Maxi » : nom a nous, aucun automate du jeu ne le reconnait -- avec
    //    « Ruscko », les phares de la vraie Ruscko repondraient), PlayerInCar de l'automate Crouch (camera basse, joueur sans
    //    controle), CharacterController coupe, PLAYER range sous le pivot du conducteur, yeux poses au point de vue du modele
    //    (pivot de tete + EyeFromPivot du jeu, comme la copie) ; touches du jeu lues par cInput (gaz, frein, direction, frein a
    //    main, embrayage, rapport +/-) ;
    //  - descente : meme touche, voiture arretee (< 0,5 m/s) ; joueur pose au sol a cote de la portiere, tout remis ;
    //  - tableau de bord : boutons du modele (Knobs) -- contact (rouge : clic tenu = demarreur, clic moteur tournant = arret),
    //    phares (blanc : clic) ; vises au centre de l'ecran comme les commandes du jeu ;
    //  - moteur coupe a la naissance (Drivetrain.Start le demarre tout seul), son de demarrage de l'addon (VazSound).
    public static partial class StandaloneCar
    {
        public const string VehicleName = "R5Maxi";
        static VazCar.Built driving;
        static VazCar.Built prompted;        // (voiture dont nous avons pose le texte d'entree)
        static int hovered = -1;             // (bouton du tableau de bord vise, texte pose par nous)
        static float enteredAt, exitBlock, footHeight = -1f;
        static FieldInfo engineVelo;

        public static bool IsDriving { get { return driving != null; } }

        // Camera du joueur (FPSCamera/FPSCamera, celle qui rend) : PlayerSync.LocalCamera n'est remplie qu'en partie a plusieurs
        // (essai monte du 28/10 : vide en solo, yeux jamais recales)
        static Transform playerCam;
        static Transform PlayerCam()
        {
            if (playerCam != null) return playerCam;
            GameObject pl = Game.PlayerGo;
            if (pl == null) return null;
            playerCam = pl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera") ?? pl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera");
            return playerCam;
        }

        // ---------------------------------------------------------------- cInput (touches du jeu, reglables dans ses options)
        static MethodInfo axisRaw, buttonDown;
        static bool cinTried;
        static void Cin()
        {
            if (cinTried) return;
            cinTried = true;
            Type t = Type.GetType("cInput, cInput");
            if (t == null) foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType("cInput"); if (t != null) break; }
            if (t == null) { Log.Warn("standalone : cInput introuvable (clavier de secours)"); return; }
            axisRaw = t.GetMethod("GetAxisRaw", new[] { typeof(string) });
            buttonDown = t.GetMethod("GetButtonDown", new[] { typeof(string) });
            buttonHeld = t.GetMethod("GetButton", new[] { typeof(string) });
            keyDefined = t.GetMethod("IsKeyDefined", new[] { typeof(string) });
            keyText = t.GetMethod("GetText", new[] { typeof(string), typeof(int) });
            // (noms des touches et axes du jeu que lit la standalone : definis dans cInput ?)
            MethodInfo ax = t.GetMethod("IsAxisDefined", new[] { typeof(string) }), kd = t.GetMethod("IsKeyDefined", new[] { typeof(string) });
            var sb = new System.Text.StringBuilder("standalone : cInput");
            try
            {
                foreach (string n in new[] { "Throttle", "Brake", "Horizontal", "Handbrake", "Clutch" }) sb.Append(" ").Append(n).Append(ax != null && (bool)ax.Invoke(null, new object[] { n }) ? "" : " (ABSENT)");
                foreach (string n in new[] { "ShiftUp", "ShiftDown", "DrivingMode" }) sb.Append(" ").Append(n).Append(kd != null && (bool)kd.Invoke(null, new object[] { n }) ? "" : " (ABSENT)");
            }
            catch (Exception e) { sb.Append(" : ").Append(e.Message); }
            Log.Info(sb.ToString());
            // ([Test] TouchesListe=1 : toutes les touches que le jeu a definies dans cInput -- noms et touches, pour les commandes des voitures)
            if (Config.GetInt("Test", "TouchesListe", 0) != 0)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    if (f.FieldType == typeof(string[]))
                    {
                        string[] v = f.GetValue(null) as string[];
                        if (v == null) continue;
                        var l = new System.Text.StringBuilder(); int cn = 0;
                        foreach (string x in v) if (!string.IsNullOrEmpty(x)) { l.Append(x).Append(" | "); if (++cn > 90) break; }
                        Log.Info("touches du jeu : " + f.Name + " : " + l);
                    }
            if (Config.GetInt("Test", "TouchesListe", 0) != 0)
            {
                var ms = new System.Text.StringBuilder();
                foreach (Mesh me in Resources.FindObjectsOfTypeAll<Mesh>()) if (me != null && System.Text.RegularExpressions.Regex.IsMatch(me.name, "watch|wrist|kello|clock|arm_", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ms.Append(me.name).Append(" (").Append(me.vertexCount).Append(") | ");
                Log.Info("maillages montre : " + ms);
            }
        }
        public static float Axis(string n)
        {
            Cin();
            if (axisRaw == null) return 0f;
            try { float v = (float)axisRaw.Invoke(null, new object[] { n }); return float.IsNaN(v) ? 0f : Mathf.Clamp(v, -1f, 1f); } catch { return 0f; }
        }
        static MethodInfo buttonHeld;
        // ([Test] : touche tenue / appuyee simulee -- la touche du jeu ne se simule pas autrement)
        static string simHeld = "", simDown = "";
        // Les deux jeux n'ont pas les memes actions (MSC : HighBeam ; MWC : HiBeamToggle, LightsOff, LightsDrive...) : une action absente
        // n'est jamais lue (cInput s'en plaindrait a chaque image). Bound : action definie ET une touche dessus (StartEngine n'a pas de
        // touche par defaut, ni dans MSC ni dans MWC -- Донской 09/11 : « le demarreur a la touche ne marche pas »). Relu toutes les 5 s
        // (le joueur peut changer ses touches dans les options du jeu).
        static MethodInfo keyDefined, keyText;
        static readonly Dictionary<string, bool> keyDef = new Dictionary<string, bool>(), keyBound = new Dictionary<string, bool>();
        static float keyBoundAt = -10f;
        static bool Defined(string n)
        {
            Cin();
            if (keyDefined == null) return buttonDown != null;
            bool v;
            if (!keyDef.TryGetValue(n, out v)) { try { v = (bool)keyDefined.Invoke(null, new object[] { n }); } catch { v = false; } keyDef[n] = v; }
            return v;
        }
        static bool Bound(string n)
        {
            if (!Defined(n)) return false;
            if (keyText == null) return true;
            if (Time.realtimeSinceStartup - keyBoundAt > 5f) { keyBound.Clear(); keyBoundAt = Time.realtimeSinceStartup; }
            bool v;
            if (!keyBound.TryGetValue(n, out v))
            {
                v = false;
                try { for (int i = 1; i <= 2 && !v; i++) { string s = keyText.Invoke(null, new object[] { n, i }) as string; v = !string.IsNullOrEmpty(s) && s != "None"; } }
                catch { v = true; }
                keyBound[n] = v;
            }
            return v;
        }
        // Demarreur : StartEngine du jeu s'il a une touche, sinon Ignition (MWC), sinon une touche a nous : [Touches] Demarreur (Y par defaut).
        static KeyCode starterKey = KeyCode.None; static string starterFrom;
        static string StarterAction() { return Bound("StartEngine") ? "StartEngine" : Bound("Ignition") ? "Ignition" : null; }
        static KeyCode StarterKey()
        {
            if (starterKey == KeyCode.None)
            {
                string s = Config.Get("Touches", "Demarreur", "Y");
                try { starterKey = (KeyCode)Enum.Parse(typeof(KeyCode), s, true); } catch { starterKey = KeyCode.Y; }
            }
            return starterKey;
        }
        static void StarterLog()
        {
            string a = StarterAction(), f = a != null ? "touche du jeu " + a : "touche " + StarterKey() + " ([Touches] Demarreur ; StartEngine du jeu sans touche)";
            if (f != starterFrom) { starterFrom = f; Log.Info("standalone : demarreur sur " + f); }
        }
        static bool StarterHeld() { if (simHeld == "StartEngine") return true; string a = StarterAction(); return a != null ? Held(a) : Input.GetKey(StarterKey()); }
        static bool StarterDown() { if (simDown == "StartEngine") return true; string a = StarterAction(); return a != null ? Down(a) : Input.GetKeyDown(StarterKey()); }
        public static bool Held(string n)
        {
            if (simHeld == n) return true;
            Cin();
            if (buttonHeld == null || !Defined(n)) return false;
            try { return (bool)buttonHeld.Invoke(null, new object[] { n }); } catch { return false; }
        }
        public static bool Down(string n)
        {
            if (simDown == n) return true;
            Cin();
            if (buttonDown == null) return n == "DrivingMode" && Input.GetKeyDown(KeyCode.Return);
            if (!Defined(n)) return false;
            try { return (bool)buttonDown.Invoke(null, new object[] { n }); } catch { return false; }
        }

        // ---------------------------------------------------------------- construction (appelee par Build)
        static void SetupDrive(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            VazCar.Model m = b.M;
            // (nom du jeu : Seats en tire la place du passager avant en miroir, l'avatar d'un conducteur distant s'y assoit)
            var head = new GameObject("DriverHeadPivot").transform;
            head.SetParent(b.Car.transform, false);
            head.localPosition = m.Head;
            st.Pivot = LeanRig(b, st, head);
            Bounds cb; st.HalfWidth = VazCar.ModelBounds(m, "carrosserie.p205", out cb) ? Mathf.Max(Mathf.Abs(cb.min.x), Mathf.Abs(cb.max.x)) : 0.85f;
            // cle de contact du modele (cle.p205, infos.txt cle_centre / cle_axe -- Golf, JD 31/10) : comme celle de la Satsuma, mise,
            // contact, demarreur tenu, retiree (Knobs) ; elle remplace le bouton rouge du contact
            Vector3 kc, ka;
            if (System.IO.File.Exists(System.IO.Path.Combine(m.Dir, "cle.p205")) && inf.TryGetValue("cle_centre", out kc) && inf.TryGetValue("cle_axe", out ka) && ka.sqrMagnitude > 0.01f)
            {
                var kp = new GameObject("cle").transform; kp.SetParent(b.Body, false); kp.localPosition = kc;
                var ki = new GameObject("pieces").transform; ki.SetParent(kp, false); ki.localPosition = -kc;
                VazCar.AddModel(b, ki, "cle.p205");
                st.Key = kp; st.KeyAxis = ka.normalized; kp.gameObject.SetActive(false);
            }
            st.ModelButtons = inf.ContainsKey("boutons_modele");
            SetupFuel(b, st, inf);
            SetupBrakeLamp(b, st, inf);
            SetupGearDisplay(b, st, inf);
            SetupLcd(b, st, inf);
            // boutons visibles du tableau de bord (cylindres colores, comme sur la copie)
            VazCar.Knob[] kns = KnobsOf(m);
            if (kns != null)
                for (int i = 0; i < kns.Length; i++)
                {
                    if (i == KIgnition && st.Key != null) continue;
                    if (st.ModelButtons) continue;   // (boutons du modele : rien a dessiner)
                    VazCar.Knob kn = kns[i];
                    GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.name = "bouton_" + i;
                    g.transform.SetParent(b.Body, false);
                    g.transform.localPosition = kn.At;
                    g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, kn.Normal);
                    g.transform.localScale = new Vector3(0.017f, 0.006f, 0.017f);
                    Shader ks = Shader.Find("Legacy Shaders/Specular") ?? Shader.Find("Specular");
                    if (ks != null) g.GetComponent<MeshRenderer>().sharedMaterial = new Material(ks) { color = kn.Col, name = "bouton_" + i };
                }
            // phares : deux projecteurs aux feux avant du modele
            // (pleins phares : un second projecteur par phare, plus serre, plus loin, presque a l'horizontale, ajoute au croisement --
            // Донской 09/11 : « je ne vois pas les pleins phares », la portee seule ne se voyait pas)
            var heads = new List<Light>(); var highs = new List<Light>();
            foreach (string n in new[] { "headlight_l", "headlight_r" })
            {
                Vector3 p;
                if (!inf.TryGetValue(n, out p)) continue;
                var lg = new GameObject("phare_" + n.Substring(10)).transform;
                lg.SetParent(b.Body, false);
                lg.localPosition = p + new Vector3(0f, 0f, 0.08f);
                lg.localRotation = Quaternion.Euler(4f, 0f, 0f);
                Light l = lg.gameObject.AddComponent<Light>();
                HeadBeam(l, false); l.enabled = false;
                heads.Add(l);
                var hg = new GameObject("plein").transform;
                hg.SetParent(lg, false);
                hg.localRotation = Quaternion.Euler(-3.5f, 0f, 0f);
                Light h = hg.gameObject.AddComponent<Light>();
                HighBeamLight(h); h.enabled = false;
                highs.Add(h);
            }
            st.Heads = heads.ToArray(); st.HighHeads = highs.ToArray();
            // portieres sur charniere (JD 28/10 : « les portes ne s'ouvrent pas ») : pivot au point de charniere du modele (infos.txt
            // door_dside_f / door_pside_f, arete avant), axe vertical, la portiere du modele accrochee dessous ; clic pour ouvrir (Doors)
            st.Doors = new Transform[2]; st.DoorCenter = new Vector3[2]; st.DoorOpen = new bool[2]; st.DoorAngle = new float[2];
            string[][] dd = { new[] { "portiere_g.p205", "door_dside_f" }, new[] { "portiere_d.p205", "door_pside_f" } };
            for (int i = 0; i < 2; i++)
            {
                if (!System.IO.File.Exists(System.IO.Path.Combine(m.Dir, dd[i][0]))) continue;
                Vector3 h;
                if (!inf.TryGetValue(dd[i][1], out h)) { VazCar.AddModel(b, b.Body, dd[i][0]); continue; }   // (sans charniere : portiere fixe)
                var piv = new GameObject("portiere_" + i).transform;
                piv.SetParent(b.Body, false); piv.localPosition = h;
                var inner = new GameObject("pieces").transform;
                inner.SetParent(piv, false); inner.localPosition = -h;
                VazCar.AddModel(b, inner, dd[i][0]);
                // vitre de portiere et sa manivelle (vitre_<g|d>.p205, manivelle_<g|d>.p205 ; infos.txt vitre_<g|d>_bas, manivelle_<g|d>_centre /
                // _axe -- Golf, JD 31/10) : sur la portiere, la vitre descend quand on tourne la manivelle (Knobs, WindowAnim)
                string sx = i == 0 ? "g" : "d"; Vector3 wd, mc, ma;
                if (System.IO.File.Exists(System.IO.Path.Combine(m.Dir, "vitre_" + sx + ".p205")) && inf.TryGetValue("vitre_" + sx + "_bas", out wd))
                {
                    var wg = new GameObject("vitre_" + sx).transform; wg.SetParent(inner, false);
                    VazCar.AddModel(b, wg, "vitre_" + sx + ".p205");
                    st.Glass[i] = wg; st.GlassDown[i] = wd;
                }
                if (System.IO.File.Exists(System.IO.Path.Combine(m.Dir, "manivelle_" + sx + ".p205")) && inf.TryGetValue("manivelle_" + sx + "_centre", out mc) && inf.TryGetValue("manivelle_" + sx + "_axe", out ma) && ma.sqrMagnitude > 0.01f)
                {
                    var cp = new GameObject("manivelle_" + sx).transform; cp.SetParent(inner, false); cp.localPosition = mc;
                    var ci = new GameObject("pieces").transform; ci.SetParent(cp, false); ci.localPosition = -mc;
                    VazCar.AddModel(b, ci, "manivelle_" + sx + ".p205");
                    st.Crank[i] = cp; st.CrankAxis[i] = ma.normalized;
                }
                Bounds db; st.DoorCenter[i] = VazCar.ModelBounds(m, dd[i][0], out db) ? db.center : h;
                st.Doors[i] = piv;
                // (portiere conducteur : elle ferme l'habitacle pour le joueur et tourne avec la portiere -- ouverte, on passe)
                if (buildPlayerLayer >= 0 && db.size.y > 0.3f)
                {
                    if (i == 0) st.DriverDoor = db;
                    st.DoorBox[i] = db;
                    var dc = new GameObject("joueur_portiere"); dc.layer = buildPlayerLayer;
                    dc.transform.SetParent(inner, false);
                    var bc = dc.AddComponent<BoxCollider>(); bc.center = db.center; bc.size = db.size; st.DoorCol[i] = bc;
                }
            }

            // hayon et plage arriere sur charniere (infos.txt hayon_charniere, plage_charniere -- Golf, JD 31/10) : clic pour ouvrir / relever
            Vector3 hc;
            if (System.IO.File.Exists(System.IO.Path.Combine(m.Dir, "hayon.p205")) && inf.TryGetValue("hayon_charniere", out hc))
            {
                var hp = new GameObject("hayon").transform; hp.SetParent(b.Body, false); hp.localPosition = hc;
                var hi = new GameObject("pieces").transform; hi.SetParent(hp, false); hi.localPosition = -hc;
                VazCar.AddModel(b, hi, "hayon.p205");
                Bounds hb; st.Hatch = hp; st.HatchCenter = VazCar.ModelBounds(m, "hayon.p205", out hb) ? hb.center : hc;
            }
            if (System.IO.File.Exists(System.IO.Path.Combine(m.Dir, "plage.p205")) && inf.TryGetValue("plage_charniere", out hc))
            {
                var sp = new GameObject("plage").transform; sp.SetParent(b.Body, false); sp.localPosition = hc;
                var si = new GameObject("pieces").transform; si.SetParent(sp, false); si.localPosition = -hc;
                VazCar.AddModel(b, si, "plage.p205");
                Bounds sb; st.Shelf = sp; st.ShelfCenter = VazCar.ModelBounds(m, "plage.p205", out sb) ? sb.center : hc;
            }

            // essuie-glaces et comodo (essuie_<av1|av2|ar>.p205, comodo.p205 ; infos.txt essuie_<k>_centre / _axe, comodo_centre / _axe --
            // Golf, JD 31/10) : le balai arriere suit le hayon
            string[] wk = { "av1", "av2", "ar" };
            for (int i = 0; i < 3; i++)
            {
                Vector3 wc, wa;
                string wf = "essuie_" + wk[i] + ".p205";
                if (!System.IO.File.Exists(System.IO.Path.Combine(m.Dir, wf)) || !inf.TryGetValue("essuie_" + wk[i] + "_centre", out wc) || !inf.TryGetValue("essuie_" + wk[i] + "_axe", out wa)) continue;
                Transform par = b.Body;
                if (i == 2 && st.Hatch != null && st.Hatch.Find("pieces") != null) par = st.Hatch.Find("pieces");
                var wp = new GameObject("essuie_" + wk[i]).transform; wp.SetParent(par, false); wp.localPosition = wc;
                var wi = new GameObject("pieces").transform; wi.SetParent(wp, false); wi.localPosition = -wc;
                VazCar.AddModel(b, wi, wf);
                st.Wipers[i] = wp; st.WiperAxis[i] = wa.normalized; st.WiperMax[i] = i == 2 ? RearSweep : 88f;
                // (course propre -- monobalai du BX 4TC, JD 08/11 : couche a droite, il balaie jusqu'au montant gauche)
                Vector3 wco; if (i < 2 && inf.TryGetValue("essuie_" + wk[i] + "_course", out wco) && wco.x > 10f && wco.x < 200f) { st.WiperMax[i] = wco.x; st.WiperCourse[i] = true; }
                if (i == 2)
                {
                    // (JD 31/10, video : le balai de la lunette « descend sur le hayon » au retour, trace en miroir -- dans le modele, bras en
                    // L : a plat vers la droite depuis le pied, raclette debout le long du bord droit de la vitre ; c'est son repos. Course :
                    // le bras monte, la raclette balaie vers la gauche)
                    Vector3 ax = st.WiperAxis[2], far = wc; float fd = 0f;
                    foreach (MeshFilter mf in wi.GetComponentsInChildren<MeshFilter>())
                        foreach (Vector3 v in mf.sharedMesh.vertices) { Vector3 q = b.Body.InverseTransformPoint(mf.transform.TransformPoint(v)); if ((q - wc).sqrMagnitude > fd) { fd = (q - wc).sqrMagnitude; far = q; } }
                    Vector3 rgt = Vector3.ProjectOnPlane(Vector3.right, ax).normalized, up = Vector3.ProjectOnPlane(Vector3.up, ax).normalized;
                    st.WiperSign[2] = Mathf.Sign(Vector3.Dot(ax, Vector3.Cross(rgt, up)));   // (de la droite vers le haut)
                    st.WiperRest[2] = 0f;
                    Vector3 d0 = far - wc; st.RearWiperA0 = Mathf.Atan2(-d0.x, d0.y) * Mathf.Rad2Deg;
                    Log.Info("standalone : essuie-glace arriere, raclette au repos a " + st.RearWiperA0.ToString("F0") + " degres de la verticale, course " + RearSweep.ToString("F0") + " vers la gauche");
                }
            }
            Vector3 sc0, sa0;
            if (System.IO.File.Exists(System.IO.Path.Combine(m.Dir, "comodo.p205")) && inf.TryGetValue("comodo_centre", out sc0) && inf.TryGetValue("comodo_axe", out sa0))
            {
                var sp0 = new GameObject("comodo").transform; sp0.SetParent(b.Body, false); sp0.localPosition = sc0;
                var si0 = new GameObject("pieces").transform; si0.SetParent(sp0, false); si0.localPosition = -sc0;
                VazCar.AddModel(b, si0, "comodo.p205");
                st.Stalk = sp0; st.StalkAxis = sa0.normalized;
                Vector3 tip0; if (inf.TryGetValue("comodo_bout", out tip0)) { st.HasStalkTip = true; st.StalkTip = tip0 - sc0; }
            }
            // interrupteur d'essuie-glaces sans comodo (infos.txt bouton_essuie : pictogramme essuie-glace de la planche -- BMW M3 E30, JD 04/11)
            Vector3 wb0;
            if (st.Stalk == null && inf.TryGetValue("bouton_essuie", out wb0)) { st.HasWiperBtn = true; st.WiperBtn = wb0; Log.Info("standalone : interrupteur d'essuie-glaces " + wb0.ToString("F3")); }
            // ceintures (ceinture_<cg|cd|ag|ad>.p205 : sangle pendue au montant ; infos.txt ceinture_<k>_haut / _bas -- Golf, JD 31/10 :
            // « les ceintures fonctionnelles, avant et arriere ») : la sangle pendue cachee quand la place est bouclee, la bouclee
            // dessinee (BeltTick) ; 0 conducteur (gauche), 1 passager avant, 2 arriere gauche, 3 arriere droite
            string[] bk = { "cg", "cd", "ag", "ad" };
            for (int i = 0; i < 4; i++)
            {
                Vector3 bt, bl; string bf = "ceinture_" + bk[i] + ".p205";
                if (!System.IO.File.Exists(System.IO.Path.Combine(m.Dir, bf)) || !inf.TryGetValue("ceinture_" + bk[i] + "_haut", out bt) || !inf.TryGetValue("ceinture_" + bk[i] + "_bas", out bl)) continue;
                var bp = new GameObject("ceinture_" + bk[i]).transform; bp.SetParent(b.Body, false);
                VazCar.AddModel(b, bp, bf);
                st.BeltHang[i] = bp; st.BeltTop[i] = bt; st.BeltLow[i] = bl;
            }
            // dossiers des sieges avant rabattables (dossier_<g|d>.p205 ; infos.txt dossier_<k>_pivot / _axe -- JD 31/10 : « rabattre les
            // sieges avant pour pouvoir passer a l'arriere ») : clic dessus, portiere ouverte (Doors) ; releve, il ferme le passage vers
            // la banquette pour le joueur (Shell). Banquette (infos.txt banquette, habitacle_arriere) : places arriere (Seats), habitacle
            // creux jusque derriere elle.
            for (int i = 0; i < 2; i++)
            {
                string sk = i == 0 ? "g" : "d"; Vector3 dp, da; Bounds sbb;
                if (!System.IO.File.Exists(System.IO.Path.Combine(m.Dir, "dossier_" + sk + ".p205")) || !inf.TryGetValue("dossier_" + sk + "_pivot", out dp) || !inf.TryGetValue("dossier_" + sk + "_axe", out da)) continue;
                var dpv = new GameObject("dossier_" + sk).transform; dpv.SetParent(b.Body, false); dpv.localPosition = dp;
                var dpi = new GameObject("pieces").transform; dpi.SetParent(dpv, false); dpi.localPosition = -dp;
                VazCar.AddModel(b, dpi, "dossier_" + sk + ".p205");
                st.SeatBack[i] = dpv; st.SeatBackAxis[i] = da.normalized;
                st.SeatBackBox[i] = VazCar.ModelBounds(m, "dossier_" + sk + ".p205", out sbb) ? sbb : new Bounds(dp + new Vector3(0f, 0.3f, -0.05f), new Vector3(0.5f, 0.6f, 0.2f));
            }
            for (int i = 0; i < 4; i++) { Vector3 sr; if (inf.TryGetValue("siege_" + bk[i], out sr)) { st.SeatRef[i] = sr; st.HasSeatRef[i] = true; } }
            Vector3 rb0;
            if (inf.TryGetValue("banquette", out rb0)) { st.RearOffset = rb0; st.HasRear = true; }
            if (inf.TryGetValue("habitacle_arriere", out rb0)) st.CabinRear = rb0.z;
            SetupRain(b, st, inf);
            SetupHeater(b, st, inf);
            Shell(b, st);
            // son moteur de l'addon (boucles + demarrage) au moteur du modele
            var eng = new GameObject("moteur_son").transform;
            eng.SetParent(b.Body, false); eng.localPosition = m.Engine;
            b.Sound = VazSound.Create(b.Car, eng, m);
            st.EngineOn = false;
            Lamps(b, st);
            // klaxon : le son du CarHorn de la voiture du jeu (Ruscko : LOD/Dashboard/Knobs/ButtonHorn/CarHorn, Satsuma :
            // CarSimulation/CarHorn -- le jeu l'allume tant que le clic est tenu sur le bouton), a l'avant de la voiture
            GameObject tpl = VazCar.Template(); AudioSource th = null;
            foreach (string hp in new[] { "LOD/Dashboard/Knobs/ButtonHorn/CarHorn", "CarSimulation/CarHorn", "Simulation/CarHorn" })   // (My Winter Car, SORBET : Simulation/CarHorn) { Transform x = tpl != null ? tpl.transform.Find(hp) : null; if (x != null && x.GetComponent<AudioSource>() != null) { th = x.GetComponent<AudioSource>(); break; } }
            // (sinon un CarHorn du jeu avec son clip -- My Winter Car : celui de la SORBET est eteint, clip pose plus tard)
            if (th == null || th.clip == null) foreach (AudioSource a in Resources.FindObjectsOfTypeAll<AudioSource>()) if (a != null && a.name == "CarHorn" && a.clip != null) { th = a; break; }
            if (th != null && th.clip != null)
            {
                var hg = new GameObject("CarHorn"); hg.transform.SetParent(b.Body, false); hg.transform.localPosition = new Vector3(0f, 0.5f, 1.5f);
                st.Horn = hg.AddComponent<AudioSource>();
                st.Horn.clip = th.clip; st.Horn.loop = true; st.Horn.playOnAwake = false; st.Horn.spatialBlend = 1f; st.Horn.minDistance = th.minDistance > 0f ? th.minDistance : 3f; st.Horn.maxDistance = th.maxDistance > 0f ? th.maxDistance : 150f; st.Horn.volume = th.volume > 0f ? th.volume : 1f; st.Horn.outputAudioMixerGroup = th.outputAudioMixerGroup;
            }
            else Log.Warn("standalone : son du klaxon de la voiture du jeu introuvable");
            st.Speedo = Gauge.Make(b, inf, "v", "cadran_vitesse.png");
            st.Tacho = Gauge.Make(b, inf, "r", "cadran_regime.png");
            st.Turbo = Gauge.Make(b, inf, "t", "cadran_turbo.png");
            SetupClock(b, inf);
            MakeMirrors(b, st, inf);
        }

        // Boutons du tableau de bord : ceux du modele (copie), sauf la R5 standalone -- JD 28/10, capture annotee : en ligne sur le dessus
        // de la console centrale, sous la barre blanche, de gauche a droite contact (rouge), starter (bleu), phares (blanc), warning
        // (orange) ; points releves par rayons de la camera du conducteur sur le modele (dessus de console a y 0,619, face vers le haut).
        static readonly Vector3 ConsoleUp = new Vector3(0f, 1f, -0.06f).normalized;
        static readonly VazCar.Knob[] R5Knobs = {
            new VazCar.Knob { At = new Vector3(0.036f, 0.623f, 0.494f), Normal = ConsoleUp, Col = new Color(0.80f, 0.06f, 0.05f) },
            new VazCar.Knob { At = new Vector3(0.085f, 0.623f, 0.494f), Normal = ConsoleUp, Col = new Color(0.10f, 0.25f, 0.85f) },
            new VazCar.Knob { At = new Vector3(0.137f, 0.623f, 0.490f), Normal = ConsoleUp, Col = new Color(0.92f, 0.92f, 0.90f) },
            new VazCar.Knob { At = new Vector3(0.183f, 0.623f, 0.485f), Normal = ConsoleUp, Col = new Color(0.95f, 0.45f, 0.05f) } };
        // boutons poses par JD dans l'atelier 3D (infos.txt : bouton_<role> point, bouton_<role>_n normale -- outils/rf1/edition.py),
        // sinon ceux d'ici ; dans l'ordre contact, starter, phares, warning (klaxon sur la standalone)
        static readonly string[] KnobRoles = { "contact", "starter", "phares", "warning" };
        static VazCar.Knob[] KnobsOf(VazCar.Model m)
        {
            VazCar.Knob[] def = m == VazCar.R5 ? R5Knobs : m.Knobs;
            Dictionary<string, Vector3> inf = VazCar.ModelInfo(m);
            if (def == null || inf == null || !inf.ContainsKey("bouton_" + KnobRoles[0])) return def;
            var ks = new VazCar.Knob[Mathf.Max(def.Length, KnobRoles.Length)];
            for (int i = 0; i < ks.Length; i++)
            {
                VazCar.Knob d = i < def.Length ? def[i] : def[def.Length - 1];
                Vector3 p, n;
                if (i < KnobRoles.Length && inf.TryGetValue("bouton_" + KnobRoles[i], out p))
                    ks[i] = new VazCar.Knob { Path = d.Path, Choke = d.Choke, Col = d.Col, At = p, Normal = inf.TryGetValue("bouton_" + KnobRoles[i] + "_n", out n) && n.sqrMagnitude > 0.01f ? n.normalized : d.Normal };
                else ks[i] = d;
            }
            return ks;
        }

        // ---------------------------------------------------------------- compteurs
        // Compteurs fonctionnels (JD 28/10 : « importer les compteurs et qu'ils soient fonctionnels ») : comme le mod rFactor, une
        // aiguille (aiguille.png) tournee sur la face du cadran (cadran_vitesse.png / cadran_regime.png) autour de son centre ;
        // reperes et angles dans infos.txt (outils/rf1/r5maxi_cadrans.py) : cadran_<v|r>_centre, _haut, _droite (demi-cote de la
        // texture), _angles (degres depuis le haut, sens horaire), _valeurs. Face et aiguille sans eclairage (lisibles la nuit).
        public class Gauge
        {
            // (Shown tres bas au depart : le premier Set pose toujours l'aiguille -- avec -1, un compte-tours a 0 tr/min restait
            // vertical, sous le seuil de 0,2 % de 9 000 ; JD 03/11)
            public Transform Needle; public Vector3 Axis; public float A0, A1, V0, V1, Max, Shown = -1e9f;   // (Max : pression maxi du turbo, cadran_t_valeurs z)
            // (face de nuit, <face>_nuit.png : graduations vertes avec les phares, comme le tableau de bord eclaire de la Golf -- JD 31/10)
            public Material Face; public Texture2D Day, Night; public bool Lit;
            // (eclairage du tableau de bord, JD 03/11 : « qu'ils s'eclairent lorsque les phares sont allumes » -- phares eteints, face et
            // aiguille eclairees par la scene comme la planche (sombres la nuit) ; phares allumes, sans eclairage de la scene, face de nuit
            // s'il y en a une)
            public MeshRenderer FaceR, NeedleR; public Material FaceOff, NeedleOff, NeedleOn;
            public void Light(bool on)
            {
                if (on == Lit || FaceR == null) return;
                Lit = on;
                FaceR.sharedMaterial = on ? Face : FaceOff;
                if (NeedleR != null) NeedleR.sharedMaterial = on ? NeedleOn : NeedleOff;
            }

            public static Gauge Make(VazCar.Built b, Dictionary<string, Vector3> inf, string k, string faceFile)
            {
                Vector3 c, up, right, ang, val;
                if (!inf.TryGetValue("cadran_" + k + "_centre", out c) || !inf.TryGetValue("cadran_" + k + "_haut", out up) || !inf.TryGetValue("cadran_" + k + "_droite", out right)
                    || !inf.TryGetValue("cadran_" + k + "_angles", out ang) || !inf.TryGetValue("cadran_" + k + "_valeurs", out val)) return null;
                Texture2D face = Png(b.M, faceFile), needle = Png(b.M, "aiguille.png");
                if (face == null || needle == null) return null;
                Vector3 n = Vector3.Cross(right, up).normalized;   // (vers l'avant : le conducteur regarde le cadran dans ce sens)
                Shader unlit = Shader.Find("Unlit/Texture"), transp = Shader.Find("Unlit/Transparent") ?? Shader.Find("Unlit/Transparent Cutout");
                var fq = Quad("cadran_" + k, b.Body, c - n * 0.001f, Vector3.zero, up, right);
                // (face ronde a coins transparents -- 205 T16 : decoupee, la planche se voit autour)
                bool round = face.GetPixel(1, 1).a < 0.5f && face.GetPixel(face.width - 2, face.height - 2).a < 0.5f;
                Shader cut = round ? Shader.Find("Unlit/Transparent Cutout") : null;
                string nf = faceFile.Replace(".png", "_nuit.png");
                Texture2D night = System.IO.File.Exists(System.IO.Path.Combine(b.M.Dir, nf)) ? Png(b.M, nf) : null;
                // (allume : sans eclairage de la scene, face de nuit s'il y en a une ; eteint : eclaire par la scene)
                var fm = new Material(cut ?? unlit) { mainTexture = night ?? face, color = new Color(0.85f, 0.85f, 0.85f) };
                Shader litCut = Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse") ?? Shader.Find("Transparent/Cutout/Diffuse");
                Shader lit = Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse") ?? Shader.Find("Standard");
                var fOff = new Material(round && litCut != null ? litCut : lit ?? unlit) { mainTexture = face, color = Color.white };
                if (fOff.HasProperty("_Cutoff")) fOff.SetFloat("_Cutoff", 0.5f);
                var fr = fq.GetComponent<MeshRenderer>(); fr.sharedMaterial = fOff;
                var pivot = new GameObject("aiguille_" + k).transform;
                pivot.SetParent(b.Body, false); pivot.localPosition = c - n * 0.003f;
                var nq = Quad("pieces", pivot, Vector3.zero, Vector3.zero, up, right);
                Shader litTr = Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Transparent/Diffuse");
                var nOn = new Material(transp) { mainTexture = needle };
                var nOff = litTr != null ? new Material(litTr) { mainTexture = needle, color = Color.white } : nOn;
                var nr = nq.GetComponent<MeshRenderer>(); nr.sharedMaterial = nOff;
                Log.Info("standalone : compteur " + (k == "v" ? "vitesse" : k == "t" ? "turbo" : "regime") + " pose (" + val.x + "-" + val.y + ", " + ang.x + "-" + ang.y + " degres)");
                return new Gauge { Needle = pivot, Axis = n, A0 = ang.x, A1 = ang.y, V0 = val.x, V1 = val.y, Max = val.z, Face = fm, Day = face, Night = night,
                                   FaceR = fr, NeedleR = nr, FaceOff = fOff, NeedleOff = nOff, NeedleOn = nOn };
            }

            public void Set(float value)
            {
                if (Needle == null) return;
                float v = Mathf.Clamp(value, V0, V1 + (V1 - V0) * 0.05f);
                if (Mathf.Abs(v - Shown) < 0.002f * (V1 - V0)) return;
                Shown = v;
                // (sens horaire vu par le conducteur, qui regarde le long d'Axis : angle NEGATIF autour d'Axis -- capture de pres du 28/10,
                // aiguilles a 100 km/h et 4 000 tr/min pointees a droite au lieu de la gauche avec l'angle positif)
                Needle.localRotation = Quaternion.AngleAxis(-(A0 + (A1 - A0) * (v - V0) / (V1 - V0)), Axis);
            }

            // carre plat centre en c (repere du parent), cotes 2 x up et 2 x right, texture entiere (haut de l'image vers up)
            public static GameObject Quad(string name, Transform parent, Vector3 c, Vector3 unused, Vector3 up, Vector3 right)
            {
                var g = new GameObject(name);
                g.transform.SetParent(parent, false); g.transform.localPosition = c;
                var m = new Mesh { name = name };
                m.vertices = new[] { -right - up, right - up, right + up, -right + up };
                m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                // (face visible depuis le conducteur, cote -Cross(right, up) : triangles tournes vers lui)
                m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                m.RecalculateNormals(); m.RecalculateBounds();
                g.AddComponent<MeshFilter>().sharedMesh = m;
                g.AddComponent<MeshRenderer>();
                return g;
            }

            internal static Texture2D Png(VazCar.Model m, string file)
            {
                string f = System.IO.Path.Combine(m.Dir, file);
                if (!System.IO.File.Exists(f)) { Log.Info("standalone : " + file + " absent (compteurs du modele)"); return null; }
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "standalone_" + file, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
                t.LoadImage(System.IO.File.ReadAllBytes(f));
                return t;
            }
        }

        // [Test] StandaloneSonde=x,y|x,y|... (pixels d'une capture 1280 x 720, depuis le haut a gauche) : rayons de la camera du
        // conducteur sur le modele (colliders de maillage poses le temps du releve) -> point et normale, repere de la voiture.
        // (colliders poses a un appel, rayons a l'appel suivant : la physique ne les connait qu'apres son pas ; vrai quand c'est fini)
        static List<MeshCollider> probeCols; static bool probeDone;
        static bool Probe(VazCar.Built b)
        {
            string spec = Config.Get("Test", "StandaloneSonde", "");
            Transform ct = PlayerCam(); Camera cam = ct != null ? ct.GetComponent<Camera>() : null;
            if (probeDone || spec.Length == 0 || cam == null) return true;
            if (probeCols == null)
            {
                probeCols = new List<MeshCollider>();
                foreach (MeshFilter mf in b.Body.GetComponentsInChildren<MeshFilter>()) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; probeCols.Add(mc); }
                return false;
            }
            var added = probeCols;
            Log.Info("standalone : sonde, camera " + cam.pixelWidth + " x " + cam.pixelHeight + " en " + b.Car.transform.InverseTransformPoint(ct.position).ToString("F3") + " vers " + b.Car.transform.InverseTransformDirection(ct.forward).ToString("F2") + ", champ " + cam.fieldOfView.ToString("F0"));
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string pt in spec.Split('|'))
            {
                string[] xy = pt.Split(',');
                if (xy.Length != 2) continue;
                float px = float.Parse(xy[0], ci) * cam.pixelWidth / 1280f, py = cam.pixelHeight - float.Parse(xy[1], ci) * cam.pixelHeight / 720f;
                Ray r = cam.ScreenPointToRay(new Vector3(px, py, 0f));
                float best = 9f; RaycastHit bh = new RaycastHit(); bool any = false;
                foreach (MeshCollider mc in added) { RaycastHit h; if (mc.Raycast(r, out h, 3f) && h.distance < best) { best = h.distance; bh = h; any = true; } }
                Log.Info("standalone : sonde " + pt + " -> " + (any ? bh.collider.name + " " + b.Car.transform.InverseTransformPoint(bh.point).ToString("F3") + " normale " + b.Car.transform.InverseTransformDirection(bh.normal).ToString("F2") : "rien"));
            }
            foreach (MeshCollider mc in added) UnityEngine.Object.Destroy(mc);
            probeCols = new List<MeshCollider>();   // (fait : plus de releve)
            probeDone = true;
            return true;
        }

        static bool gaugeFreeze; static Vector3 eyesSaved;   // (essai standalonemonte)
        static void Gauges(VazCar.Built b, State st)
        {
            if (gaugeFreeze) return;
            // (voiture menee ailleurs : corps cinematique sans vitesse -- vitesse tiree du deplacement, lissee)
            float now = Time.time;
            if (b.Rb.isKinematic && now > st.LastPosT)
            {
                if (st.LastPosT > 0f && now - st.LastPosT < 0.5f) st.RemoteKmh = Mathf.Lerp(st.RemoteKmh, Mathf.Abs(b.Car.transform.InverseTransformDirection(b.Car.transform.position - st.LastPos).z) / (now - st.LastPosT) * 3.6f, 0.3f);
                st.LastPos = b.Car.transform.position; st.LastPosT = now;
            }
            float kmh = b.Rb.isKinematic ? st.RemoteKmh : Mathf.Abs(b.Car.transform.InverseTransformDirection(b.Rb.velocity).z) * 3.6f;
            if (st.Speedo != null) { st.Speedo.Set(kmh); st.Speedo.Light(st.LightsOn); }
            if (st.Tacho != null) { st.Tacho.Set(st.EngineOn ? b.Dt.rpm : 0f); st.Tacho.Light(st.LightsOn); }
            if (st.Turbo != null)
            {
                // (pression du turbo simulee, JD 03/11 : moteur coupe 0 ; pied leve, depression du ralenti si le cadran descend sous 0 ;
                // en charge, pression maxi x gaz x regime (pleine a partir de 5 500 tr/min) ; monte en ~1 s, retombe vite)
                float tgt = 0f;
                if (st.EngineOn && b.Dt != null)
                {
                    float thr = Mathf.Clamp01(b.Dt.throttle), boost = (st.Turbo.Max > 0f ? st.Turbo.Max : st.Turbo.V1 * 0.6f) * thr * Mathf.Clamp01((b.Dt.rpm - 2500f) / 3000f);
                    tgt = st.Turbo.V0 < 0f ? Mathf.Lerp(-0.6f, 0f, thr) + boost : boost;
                }
                st.Boost = Mathf.MoveTowards(st.Boost, tgt, Time.deltaTime * (tgt > st.Boost ? 1.4f : 3.5f));
                st.Turbo.Set(st.Boost); st.Turbo.Light(st.LightsOn);
            }
            GearDisplay(b, st);
            LcdDash(b, st);
            if (st.RevLamp != null)
            {
                bool rv = st.EngineOn && b.Dt != null && b.Dt.rpm >= st.RevAt;
                if (st.RevLamp.activeSelf != rv) st.RevLamp.SetActive(rv);
            }
            Clock(b, st);
            if (st.BrakeLamp != null)
            {
                var cc = b.Axis as CarController;
                bool on = cc != null && cc.handbrakeInput > 0.1f && (st.Key == null || st.KeyPos > 0 || st.EngineOn);
                if (st.BrakeLamp.activeSelf != on) st.BrakeLamp.SetActive(on);
            }
        }

        // ---------------------------------------------------------------- montre digitale (infos.txt montre_centre / haut / droite : ecran
        // du modele -- Golf, JD 31/10) : heure du jeu en 7 segments, ecran vert eclaire avec les phares. Heure : aiguilles de l'horloge de
        // la cour (YARD/.../SuomiClock/Clock/hour et minute, tournees autour de leur y : 30 degres par heure, 6 par minute) ; matin ou
        // apres-midi : la tranche de deux heures du soleil (MAP/SUN/Pivot/SUN::Color, Time) la plus proche.

        // voyant du frein a main (infos.txt voyant_frein_centre / haut / droite, voyant_frein.png -- Golf, JD 31/10) : symbole rouge
        // allume par-dessus celui du tableau de bord quand le frein a main est serre, contact mis
        static void SetupBrakeLamp(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            Vector3 c, up, right;
            if (!inf.TryGetValue("voyant_frein_centre", out c) || !inf.TryGetValue("voyant_frein_haut", out up) || !inf.TryGetValue("voyant_frein_droite", out right)) return;
            string f = System.IO.Path.Combine(b.M.Dir, "voyant_frein.png");
            if (!System.IO.File.Exists(f)) return;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "standalone_voyant_frein", wrapMode = TextureWrapMode.Clamp };
            t.LoadImage(System.IO.File.ReadAllBytes(f));
            Vector3 n = Vector3.Cross(right, up).normalized;
            var q = Gauge.Quad("voyant_frein", b.Body, c - n * 0.0015f, Vector3.zero, up, right);
            q.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Texture")) { mainTexture = t, name = "standalone_voyant_frein" };
            q.SetActive(false);
            st.BrakeLamp = q;
        }

        // afficheur du rapport engage (infos.txt rapport_centre / haut / droite : petit ecran du modele -- 205, JD 03/11) : R, N, 1..n en
        // sept segments rouges, contact mis ; voyant de regime (voyant_regime_centre / haut / droite, voyant_regime_seuil) : pastille rouge
        // allumee au-dessus du seuil
        static void SetupGearDisplay(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            Vector3 c, up, right, s;
            if (inf.TryGetValue("rapport_centre", out c) && inf.TryGetValue("rapport_haut", out up) && inf.TryGetValue("rapport_droite", out right))
            {
                var q = Gauge.Quad("rapport", b.Body, c, Vector3.zero, up, right);
                st.GearTex = new Texture2D(GW, GH, TextureFormat.RGB24, false) { name = "standalone_rapport", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                q.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Texture")) { mainTexture = st.GearTex, name = "standalone_rapport" };
            }
            if (inf.TryGetValue("voyant_regime_centre", out c) && inf.TryGetValue("voyant_regime_haut", out up) && inf.TryGetValue("voyant_regime_droite", out right))
            {
                var t = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "standalone_voyant_regime", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[32 * 32];
                for (int j = 0; j < 32; j++)
                    for (int i = 0; i < 32; i++)
                    {
                        float d = Mathf.Sqrt((i - 15.5f) * (i - 15.5f) + (j - 15.5f) * (j - 15.5f)) / 15.5f;
                        px[j * 32 + i] = new Color(1f, 0.12f + 0.5f * Mathf.Clamp01(0.5f - d), 0.08f, d < 1f ? 1f : 0f);
                    }
                t.SetPixels(px); t.Apply(false);
                var q = Gauge.Quad("voyant_regime", b.Body, c, Vector3.zero, up, right);
                q.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Transparent") ?? Shader.Find("Unlit/Texture")) { mainTexture = t, name = "standalone_voyant_regime" };
                q.SetActive(false); st.RevLamp = q;
                st.RevAt = inf.TryGetValue("voyant_regime_seuil", out s) && s.x > 0f ? s.x : 7000f;
            }
        }
        const int GW = 20, GH = 32;
        static void GearDisplay(VazCar.Built b, State st)
        {
            if (st.GearTex == null || b.Dt == null || b.Dt.gearRatios == null) return;
            bool lit = st.EngineOn || st.KeyPos > 0;
            if (b.Dt.gear == st.GearShown && lit == st.GearLit) return;
            st.GearShown = b.Dt.gear; st.GearLit = lit;
            int n = Array.IndexOf(b.Dt.gearRatios, 0f), g = b.Dt.gear;
            // (R : segments e et g ; N : c, e, g ; rapports : chiffres)
            int seg = g == n ? 0x54 : g < n ? 0x50 : Seg[Mathf.Clamp(g - n, 0, 9)];
            Color bg = new Color(0.05f, 0.02f, 0.02f), ink = new Color(1f, 0.16f, 0.10f), ghost = new Color(0.10f, 0.03f, 0.03f);
            var px = new Color[GW * GH]; for (int i = 0; i < px.Length; i++) px[i] = bg;
            for (int sg = 0; sg < 7; sg++) DrawSegIn(px, GW, GH, 3, 4, sg, lit && (seg & (1 << sg)) != 0 ? ink : ghost);
            st.GearTex.SetPixels(px); st.GearTex.Apply(false);
        }
        static void DrawSegIn(Color[] px, int w, int h, int x, int y, int sg, Color c)
        {
            int[][] r = { new[] { x + 2, y, 10, 3 }, new[] { x + 11, y + 2, 3, 10 }, new[] { x + 11, y + 12, 3, 10 }, new[] { x + 2, y + 21, 10, 3 },
                          new[] { x, y + 12, 3, 10 }, new[] { x, y + 2, 3, 10 }, new[] { x + 2, y + 10, 10, 3 } };
            int[] q = r[sg];
            for (int j = q[1]; j < q[1] + q[3]; j++) for (int i = q[0]; i < q[0] + q[2]; i++) if (i >= 0 && i < w && j >= 0 && j < h) px[(h - 1 - j) * w + i] = c;
        }

        // ecran LCD de planche numerique (infos.txt lcd_centre / _haut / _droite, lcd_led_<nn>, lcd_rapport, lcd_vitesse, lcd_max ; fond
        // lcd.png -- outils/ac/lcd_ac.py) : LED du regime allumees en arc, rapport et vitesse en sept segments ; contact coupe : ecran vide ;
        // eclaire avec les phares, eclaire par la scene sinon (comme les compteurs) -- Delta Evo II, JD 03/11
        static void SetupLcd(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            Vector3 c, up, right, v;
            if (!inf.TryGetValue("lcd_centre", out c) || !inf.TryGetValue("lcd_haut", out up) || !inf.TryGetValue("lcd_droite", out right)) return;
            Texture2D bg = Gauge.Png(b.M, "lcd.png");
            if (bg == null) return;
            var leds = new List<Vector2>();
            for (int i = 0; i < 99 && inf.TryGetValue("lcd_led_" + i.ToString("00"), out v); i++) leds.Add(new Vector2(v.x, v.y));
            st.LcdLeds = leds.ToArray();
            st.LcdGear = inf.TryGetValue("lcd_rapport", out v) ? v : Vector3.zero;
            st.LcdSpeed = inf.TryGetValue("lcd_vitesse", out v) ? v : Vector3.zero;
            st.LcdMax = inf.TryGetValue("lcd_max", out v) && v.x > 0f ? v.x : 8000f;
            st.LcdTex = new Texture2D(bg.width, bg.height, TextureFormat.RGB24, false) { name = "standalone_lcd", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            st.LcdBase = bg.GetPixels32();
            st.LcdTex.SetPixels32(st.LcdBase); st.LcdTex.Apply(false);
            var q = Gauge.Quad("lcd", b.Body, c, Vector3.zero, up, right);
            st.LcdR = q.GetComponent<MeshRenderer>();
            st.LcdOn = new Material(Shader.Find("Unlit/Texture")) { mainTexture = st.LcdTex, name = "standalone_lcd_allume" };
            Shader lit = Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse") ?? Shader.Find("Unlit/Texture");
            st.LcdOff = new Material(lit) { mainTexture = st.LcdTex, color = Color.white, name = "standalone_lcd" };
            st.LcdR.sharedMaterial = st.LcdOff;
            Log.Info("standalone : ecran LCD pose (" + st.LcdLeds.Length + " LED, " + st.LcdMax + " tr/min)");
        }
        static void LcdDash(VazCar.Built b, State st)
        {
            if (st.LcdTex == null || b.Dt == null) return;
            if (st.LcdLit != st.LightsOn) { st.LcdLit = st.LightsOn; st.LcdR.sharedMaterial = st.LcdLit ? st.LcdOn : st.LcdOff; }
            bool on = st.EngineOn || st.KeyPos > 0;
            int n = st.LcdLeds.Length, nl = on ? Mathf.Clamp(Mathf.RoundToInt((st.EngineOn ? b.Dt.rpm : 0f) / st.LcdMax * n), 0, n) : -1;
            int gi = -99;
            if (on && b.Dt.gearRatios != null) { int n0 = Array.IndexOf(b.Dt.gearRatios, 0f); gi = b.Dt.gear == n0 ? -1 : b.Dt.gear < n0 ? -2 : b.Dt.gear - n0; }
            int kmh = on ? Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(b.Car.transform.InverseTransformDirection(b.Rb.isKinematic ? Vector3.zero : b.Rb.velocity).z) * 3.6f + (b.Rb.isKinematic ? st.RemoteKmh : 0f)), 0, 999) : -1;
            int key = (nl + 1) * 1000000 + (gi + 3) * 1000 + (kmh + 1);
            if (key == st.LcdKey) return;
            st.LcdKey = key;
            int w = st.LcdTex.width, h = st.LcdTex.height;
            var px = (Color32[])st.LcdBase.Clone();
            if (on)
            {
                var ink = new Color32(22, 24, 18, 255);
                for (int i = 0; i < nl; i++) Disc(px, w, h, st.LcdLeds[i], 4.5f, ink);
                float s = st.LcdGear.z > 0f ? st.LcdGear.z / 30f : 3f;
                int gs = gi == -1 ? 0x54 : gi == -2 ? 0x50 : gi >= 0 ? Seg[Mathf.Clamp(gi, 0, 9)] : 0;
                Digit(px, w, h, st.LcdGear.x - 7f * s, st.LcdGear.y - 12f * s, s, gs, ink);
                if (kmh >= 0 && st.LcdSpeed.z > 0f)
                {
                    float sv = st.LcdSpeed.z / 30f, dx = 17f * sv;
                    string ks = kmh.ToString();
                    for (int k = 0; k < ks.Length; k++)
                        Digit(px, w, h, st.LcdSpeed.x + (k - ks.Length / 2f) * dx + 1.5f * sv, st.LcdSpeed.y - 12f * sv, sv, Seg[ks[k] - '0'], ink);
                }
            }
            st.LcdTex.SetPixels32(px); st.LcdTex.Apply(false);
        }
        // (pixels de l'image, haut en 0 : ligne Unity = h - 1 - y)
        static void Disc(Color32[] px, int w, int h, Vector2 p, float r, Color32 c)
        {
            for (int y = (int)(p.y - r); y <= (int)(p.y + r); y++)
                for (int x = (int)(p.x - r); x <= (int)(p.x + r); x++)
                    if (x >= 0 && x < w && y >= 0 && y < h && (x - p.x) * (x - p.x) + (y - p.y) * (y - p.y) <= r * r) px[(h - 1 - y) * w + x] = c;
        }
        static void Digit(Color32[] px, int w, int h, float x, float y, float s, int seg, Color32 c)
        {
            float[][] r = { new[] { 2f, 0f, 10f, 3f }, new[] { 11f, 2f, 3f, 10f }, new[] { 11f, 12f, 3f, 10f }, new[] { 2f, 21f, 10f, 3f },
                            new[] { 0f, 12f, 3f, 10f }, new[] { 0f, 2f, 3f, 10f }, new[] { 2f, 10f, 10f, 3f } };
            for (int sg = 0; sg < 7; sg++)
            {
                if ((seg & (1 << sg)) == 0) continue;
                int x0 = Mathf.RoundToInt(x + r[sg][0] * s), y0 = Mathf.RoundToInt(y + r[sg][1] * s), x1 = Mathf.RoundToInt(x + (r[sg][0] + r[sg][2]) * s), y1 = Mathf.RoundToInt(y + (r[sg][1] + r[sg][3]) * s);
                for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) if (xx >= 0 && xx < w && yy >= 0 && yy < h) px[(h - 1 - yy) * w + xx] = c;
            }
        }

        public static void SetupClock(VazCar.Built b, Dictionary<string, Vector3> inf)
        {
            State st; Vector3 c, up, right;
            if (!states.TryGetValue(b, out st) || !inf.TryGetValue("montre_centre", out c) || !inf.TryGetValue("montre_haut", out up) || !inf.TryGetValue("montre_droite", out right)) return;
            Vector3 n = Vector3.Cross(right, up).normalized;
            var q = Gauge.Quad("montre", b.Body, c - n * 0.0012f, Vector3.zero, up, right);
            st.ClockTex = new Texture2D(96, 32, TextureFormat.RGB24, false) { name = "standalone_montre", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            st.ClockMat = new Material(Shader.Find("Unlit/Texture")) { mainTexture = st.ClockTex, name = "standalone_montre" };
            q.GetComponent<MeshRenderer>().sharedMaterial = st.ClockMat;
        }
        static Transform clockHour, clockMinute; static float clockFind;
        static int GameMinutes()
        {
            if ((clockHour == null || clockMinute == null) && Time.time >= clockFind)
            {
                clockFind = Time.time + 10f;
                GameObject h = Game.FindAny("YARD/Building/Dynamics/SuomiClock/Clock/hour"), m = Game.FindAny("YARD/Building/Dynamics/SuomiClock/Clock/minute");
                clockHour = h != null ? h.transform : null; clockMinute = m != null ? m.transform : null;
            }
            if (clockHour == null || clockMinute == null) return -1;
            int min = Mathf.RoundToInt(clockMinute.localEulerAngles.y / 6f) % 60;
            int h12 = ((Mathf.RoundToInt((clockHour.localEulerAngles.y - min * 0.5f) / 30f) % 12) + 12) % 12;
            int h24 = h12;
            PlayMakerFSM sun = Game.FindFsm("MAP/SUN/Pivot/SUN", "Color");
            if (sun != null && sun.Fsm.Initialized)
            {
                int slot = sun.FsmVariables.GetFsmInt("Time").Value % 24;
                int d0 = Mathf.Abs(h12 - slot), d1 = Mathf.Abs(h12 + 12 - slot);
                d0 = Mathf.Min(d0, 24 - d0); d1 = Mathf.Min(d1, 24 - d1);
                if (d1 < d0) h24 = h12 + 12;
            }
            return h24 * 60 + min;
        }
        // segments de chaque chiffre (bits a b c d e f g) ; ecran 96 x 32 : HH:MM (dizaine d'heures vide sous 10 h ; heure inconnue : tirets)
        static readonly int[] Seg = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };
        static void Clock(VazCar.Built b, State st)
        {
            if (st.ClockTex == null) return;
            int t = GameMinutes();
            if (t == st.ClockShown && st.LightsOn == st.ClockLit) return;
            st.ClockShown = t; st.ClockLit = st.LightsOn;
            Color bg = st.LightsOn ? new Color(0.42f, 0.86f, 0.30f) : new Color(0.62f, 0.66f, 0.56f), ink = new Color(0.07f, 0.09f, 0.07f), ghost = Color.Lerp(bg, ink, 0.08f);
            var px = new Color[96 * 32]; for (int i = 0; i < px.Length; i++) px[i] = bg;
            int[] dig = t < 0 ? new[] { -1, -1, -1, -1 } : new[] { t < 600 ? -2 : t / 600, t / 60 % 10, t % 60 / 10, t % 10 };
            int[] x0 = { 8, 28, 54, 74 };
            for (int k = 0; k < 4; k++)
            {
                int s = dig[k] >= 0 ? Seg[dig[k]] : dig[k] == -1 ? 0x40 : 0;
                for (int sg = 0; sg < 7; sg++) DrawSeg(px, x0[k], 4, sg, (s & (1 << sg)) != 0 ? ink : ghost);
            }
            Rect(px, 47, 10, 3, 3, ink); Rect(px, 47, 19, 3, 3, ink);
            st.ClockTex.SetPixels(px); st.ClockTex.Apply(false);
        }
        // segment sg d'un chiffre de 14 x 24 px en (x, y) depuis le haut : a haut, b haut droite, c bas droite, d bas, e bas gauche,
        // f haut gauche, g milieu
        static void DrawSeg(Color[] px, int x, int y, int sg, Color c)
        {
            switch (sg)
            {
                case 0: Rect(px, x + 2, y, 10, 3, c); break;
                case 1: Rect(px, x + 11, y + 2, 3, 10, c); break;
                case 2: Rect(px, x + 11, y + 12, 3, 10, c); break;
                case 3: Rect(px, x + 2, y + 21, 10, 3, c); break;
                case 4: Rect(px, x, y + 12, 3, 10, c); break;
                case 5: Rect(px, x, y + 2, 3, 10, c); break;
                case 6: Rect(px, x + 2, y + 10, 10, 3, c); break;
            }
        }
        static void Rect(Color[] px, int x, int y, int w, int h, Color c)
        {
            for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) if (i >= 0 && i < 96 && j >= 0 && j < 32) px[(31 - j) * 96 + i] = c;
        }

        // ---------------------------------------------------------------- chaque image
        // Sons du jeu sur les voitures standalone (JD 04/11 : portes, frein a main, essuie-glaces, « pour les addons et la Golf ») : variantes du
        // groupe MasterAudio CarFoley, les memes dans les deux jeux (open_door1, close_door1, open_trunk1, close_trunk1, handbrake_on / _off,
        // wiper_loop, dash_button -- automates de la Ferndale et de la Ruscko). Declenches par l'etat, pas par le clic : tous les joueurs les
        // entendent. (Source a nous : celles du groupe sont reglees pour 8 m, et MasterAudio ne dit pas qui les joue.)
        static readonly System.Collections.Generic.Dictionary<string, AudioClip> foleyClips = new System.Collections.Generic.Dictionary<string, AudioClip>();
        static readonly System.Collections.Generic.HashSet<string> foleyLogged = new System.Collections.Generic.HashSet<string>();
        static AudioClip FoleyClip(string v)
        {
            AudioClip c;
            if (foleyClips.TryGetValue(v, out c) && c != null) return c;
            GameObject g = GameObject.Find("MasterAudio/CarFoley/" + v);
            AudioSource src = g != null ? g.GetComponent<AudioSource>() : null;
            c = src != null ? src.clip : null;
            if (c != null) foleyClips[v] = c;
            if (foleyLogged.Add("?" + v)) Log.Info("standalone : son du jeu CarFoley/" + v + (c != null ? ", " + c.name + " " + c.length.ToString("F1") + " s" : " INTROUVABLE"));
            return c;
        }
        static AudioSource FoleySource(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            AudioSource a = go.AddComponent<AudioSource>();
            a.spatialBlend = 1f; a.minDistance = 1f; a.maxDistance = 30f; a.dopplerLevel = 0f; a.playOnAwake = false;
            return a;
        }
        static void Foley(VazCar.Built b, string v, Vector3 at, float vol)
        {
            AudioClip c = FoleyClip(v);
            if (c == null) return;
            AudioSource a = FoleySource(b.Car.transform, "son_" + v);
            a.transform.position = at; a.clip = c; a.volume = vol; a.Play();
            UnityEngine.Object.Destroy(a.gameObject, c.length + 0.2f);
            string k = v + (b.Rb.isKinematic ? " (copie)" : "");
            if (foleyLogged.Add(k)) Log.Info("standalone : son " + k + " joue");
        }
        // frein a main : un son par cran serre ou desserre ; les autres le recoivent par l'etat
        static void HandTick(VazCar.Built b, State st, StandaloneDriver d)
        {
            float act = d.HandAct;
            if (st.HandPrev < 0f) { st.HandPrev = act; st.HandSent = act > 0.1f; return; }
            if (Mathf.Abs(act - st.HandPrev) > 0.1f)
            {
                bool serre = act > st.HandPrev;
                st.HandPrev = act;
                if (Time.time - st.HandSndT > 0.2f) { st.HandSndT = Time.time; Foley(b, serre ? "handbrake_on" : "handbrake_off", b.BrakePivot != null ? b.BrakePivot.position : b.Car.transform.position, 1f); }
            }
            if (!b.Rb.isKinematic && (act > 0.1f) != st.HandSent) { st.HandSent = act > 0.1f; SendState(b, st); }
        }

        static void Drive(VazCar.Built b, State st)
        {
            var d = b.Axis as StandaloneDriver;
            if (d == null) return;
            // (copie menee par un autre, Drivetrain coupe ici : rapport recu de son conducteur -- afficheur R/N/1-5, ecran LCD et feux de
            // recul comme chez lui ; JD 08/11 : passagers et autres joueurs voyaient le rapport d'avant, fige)
            if (b.Rb.isKinematic && b.Dt != null && b.Dt.gearRatios != null)
            {
                int rg = VehicleSync.RemoteGear(b.Car.transform);
                if (rg >= 0 && rg < b.Dt.gearRatios.Length && rg != b.Dt.gear) b.Dt.gear = rg;
            }
            Engine(b, st);
            if (b.Sound != null) b.Sound.Update();
            Lamps(b, st);
            KeyVisual(b, st);
            WindowAnim(b, st);
            WiperAnim(b, st);
            RainTick(b, st);
            DoorAnim(b, st);
            HandTick(b, st, d);
            BeltTick(b, st);
            Gauges(b, st);
            FuelTick(b, st);
            RadioTick(b, st);
            HeaterTick(b, st);
            // (voiture que personne ne mene : l'hote renvoie son etat toutes les 3 s, comme le conducteur -- un joueur arrive en cours voyait
            // ses vitres et portieres fermees, ses phares eteints, jusqu'au prochain changement)
            if (driving != b && Net.Session.Active && Net.Session.IsHost && Time.realtimeSinceStartup >= st.NextState && !VehicleSync.RemotelySeated(b.Car.transform)) SendState(b, st);
            GameObject pl = Game.PlayerGo;
            if (pl == null || !PlayerSync.InGame) return;
            WheelMask(b, st);
            if (driving == null)
            {
                // (commandes de la planche hors volant, JD 08/11 : « comme pour la Satsuma ... meme si ont est pas assis en conducteur ou
                // passager ... les passager, peuvent toucher a tout sauf le volant et les pedales » -- assis dans la voiture, camera dans
                // l'habitacle, ou dehors du cote d'une portiere ouverte ou d'une vitre baissee ; une voiture a la fois)
                if (hoverCar != null && (hoverCar.Car == null || !cars.Contains(hoverCar))) hoverCar = null;
                if ((hoverCar == null || hoverCar == b) && Reach(b, st)) { Knobs(b, st); hoverCar = hovered >= 0 ? b : null; }
                else if (hoverCar == b) { ClearHover(); CrankStop(b, st); HornOff(b, st); hoverCar = null; }
                if (hoverCar != b) Doors(b, st);
                CabinPresence(b, st, pl.transform);
                // (texte du plein : efface en s'eloignant, seulement sur la voiture qui a un reservoir)
                if (st.HasFuel)
                {
                    bool rf = RefillPrompt(b, st, pl.transform);
                    if (refilling && !rf) { Game.SetGlobalBool("GUIuse", false); Game.SetGlobal("GUIinteraction", ""); }
                    refilling = rf;
                }
                bool zone = !QueteGolf.Locked(b) && InZone(b, st, pl.transform) && b.Rb.velocity.magnitude < 2f && Vector3.Angle(b.Car.transform.up, Vector3.up) < 45f;   // (voiture de la quete s21 pas encore pretee : fermee)
                if (zone)
                {
                    prompted = b;
                    Game.SetGlobalBool("GUIdrive", true); if (hoverCar != b) Game.SetGlobal("GUIinteraction", "ENTER DRIVING MODE");   // (bouton vise : son texte)
                    if (Time.realtimeSinceStartup > exitBlock && Down("DrivingMode")) Enter(b, st);
                }
                else if (prompted == b) { prompted = null; Game.SetGlobalBool("GUIdrive", false); if (!refilling) Game.SetGlobal("GUIinteraction", ""); }
                return;
            }
            if (driving != b) return;
            if (Crash(b, st)) return;
            Knobs(b, st);
            Lean(b, st);
            if (hovered < 0) Doors(b, st);
            GearText(b);
            GameKeys(b, st);
            // (etat renvoye toutes les 3 s par le conducteur : un joueur arrive en cours l'a aussi)
            if (Time.realtimeSinceStartup >= st.NextState) SendState(b, st);
            if (Time.realtimeSinceStartup - enteredAt > 0.5f && Down("DrivingMode"))
            {
                if (b.Rb.velocity.magnitude < 0.5f) Exit(b, false);
                else Log.Info("standalone : descente refusee, la voiture roule (" + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + " km/h)");
            }
        }

        // Commandes de toutes les voitures autonomes sur les touches du jeu, reglables dans ses options (suggestion de Донской, 04/11) :
        //  HighBeam (L -- JD 28/10) : phares, pleins phares, eteints ; Wipers : essuie-glaces ; IndicatorLeft / IndicatorRight : clignotant,
        //  les deux ensemble : warnings ; StartEngine : tenue, contact et demarreur (0,9 s) ; moteur tournant, appui : arret.
        static void GameKeys(VazCar.Built b, State st)
        {
            if (Menu.ChatOpen || Menu.Open) return;
            Vector3 dash = b.Car.transform.TransformPoint(st.T.Yeux + new Vector3(0f, -0.2f, 0.45f));
            // (MSC : HighBeam (L) : phares, pleins phares, eteints. MWC n'a pas HighBeam : LightsDrive (L) allume / eteint, HiBeamToggle
            // (virgule) pleins phares oui / non, LightsOff eteint, LightsPark allume)
            bool cycle = Down("HighBeam"), lDrive = Down("LightsDrive"), lHigh = Down("HiBeamToggle"), lOff = Down("LightsOff"), lPark = Down("LightsPark");
            if (cycle || lDrive || lHigh || lOff || lPark)
            {
                if (lOff) { st.LightsOn = false; st.HighBeam = false; }
                else if (lPark) { st.LightsOn = true; st.HighBeam = false; }
                else if (lDrive) { st.LightsOn = !st.LightsOn; st.HighBeam = false; }
                else if (lHigh) { st.HighBeam = !st.LightsOn || !st.HighBeam; st.LightsOn = true; }
                else if (!st.LightsOn) { st.LightsOn = true; st.HighBeam = false; } else if (!st.HighBeam) st.HighBeam = true; else { st.LightsOn = false; st.HighBeam = false; }
                Foley(b, "dash_button", dash, 0.6f); SendState(b, st);
                Log.Info("standalone : phares " + (!st.LightsOn ? "eteints" : st.HighBeam ? "pleins phares" : "allumes") + " (touche)");
            }
            if (Down("Wipers") && (st.Wipers[0] != null || st.Wipers[1] != null || st.Wipers[2] != null || st.Stalk != null))
            {
                st.WipersOn = !st.WipersOn; Foley(b, "dash_button", dash, 0.6f); SendState(b, st);
                Log.Info("standalone : essuie-glaces " + (st.WipersOn ? "en marche" : "arretes") + " (touche)");
            }
            bool dl = Down("IndicatorLeft"), dr = Down("IndicatorRight");
            if (dl || dr)
            {
                if ((dl && Held("IndicatorRight")) || (dr && Held("IndicatorLeft"))) { st.Hazard = !st.Hazard; st.Turn = 0; }
                else { int t = dl ? 1 : 2; st.Turn = st.Turn == t ? 0 : t; }
                Foley(b, "dash_button", dash, 0.6f); SendState(b, st);
                Log.Info("standalone : clignotant " + st.Turn + ", warnings " + st.Hazard + " (touche)");
            }
            StarterLog();
            bool held = StarterHeld();
            if (!held) st.StartLatch = false;
            if (st.EngineOn)
            {
                st.KeyCrank = false;
                if (StarterDown() && !st.StartLatch) { st.StartLatch = true; st.KeyPos = 0; SetEngine(b, false); }
                return;
            }
            if (held && !st.StartLatch)
            {
                if (st.Key != null && (!st.KeyIn || st.KeyPos == 0)) { st.KeyIn = true; st.KeyPos = 1; st.KeyWait = false; SendState(b, st); }
                st.KeyCrank = true;
                if (st.CrankT < 0f) { st.CrankT = Time.time; if (b.Sound != null) b.Sound.ExternalStarter(true, false); }
                if (Time.time - st.CrankT > 0.9f && CanStart(st)) { st.KeyCrank = false; st.StartLatch = true; SetEngine(b, true); if (b.Sound != null) b.Sound.ExternalStarter(false, true); }
            }
            else if (st.KeyCrank) { st.KeyCrank = false; CrankStop(b, st); }
        }

        // ---------------------------------------------------------------- reservoir et temperature (voiture de la quete, suggestion de Донской)
        // Deux petites fenetres du compte-tours du modele : temperature a gauche, carburant a droite (barre qui se deplace dessus). Conso :
        // regime et gaz, 40 L ; a sec, le moteur cale. Le plein : a pied a cote de la voiture, moteur coupe, a moins de 12 m d'une pompe
        // (clic tenu, paye). Panne loin de tout : 4 s de demarreur retrouvent le fond du reservoir (4 %) -- jamais bloque sur la route.
        const float TankLitres = 40f, FuelPerLitre = 5f;
        static bool CanStart(State st)
        {
            if (!st.HasFuel || st.Fuel > 0f) return true;
            if (Time.time - st.CrankT < 4f) return false;
            st.Fuel = 0.04f; st.FuelWarned = false;
            Log.Info("standalone : fond du reservoir retrouve");
            return true;
        }
        static void SetupFuel(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            if (!b.M.QuestOnly) return;
            st.HasFuel = true; st.Fuel = QueteGolf.SavedFuel; st.EngineTemp = QueteGolf.SavedTemp;
            if (QueteGolf.Metal) VazCar.Repaint(b, QueteGolf.Paint, true);
            // (autoradio : le poste du modele, au-dessus des commandes du chauffage)
            st.HasRadio = true; st.RadioAt = new Vector3(Config.GetFloat("Test", "RadioX", -0.02f), Config.GetFloat("Test", "RadioY", 0.905f), Config.GetFloat("Test", "RadioZ", 0.54f));
            var rg = new GameObject("autoradio"); rg.transform.SetParent(b.Body, false); rg.transform.localPosition = st.RadioAt;
            st.RadioSrc = rg.AddComponent<AudioSource>();
            st.RadioSrc.playOnAwake = false; st.RadioSrc.loop = false; st.RadioSrc.spatialBlend = 1f; st.RadioSrc.minDistance = 1.5f; st.RadioSrc.maxDistance = 25f; st.RadioSrc.rolloffMode = AudioRolloffMode.Linear;
            Vector3 c, up, right;
            if (!inf.TryGetValue("cadran_r_centre", out c) || !inf.TryGetValue("cadran_r_haut", out up) || !inf.TryGetValue("cadran_r_droite", out right)) return;
            Vector3 n = Vector3.Cross(right, up).normalized;
            // (fenetres du cadran, en pixels de la face de 256 : y 222, gauche de 30 a 105, droite de 153 a 228 ; barre du haut de la fenetre au bas)
            Func<float, float, Vector3> at = (px, py) => c - n * 0.0022f + right * ((px - 128f) / 128f) + up * ((128f - py) / 128f);
            st.BarDir = right / 128f; st.BarRun = 75f;
            st.TempBar0 = at(30f, 222f); st.FuelBar0 = at(153f, 222f);
            Shader sh = Shader.Find("Unlit/Color") ?? Shader.Find("Unlit/Texture");
            var mat = new Material(sh) { color = new Color(0.95f, 0.93f, 0.85f), name = "standalone_barre" };
            foreach (bool isFuel in new[] { false, true })
            {
                GameObject q = Gauge.Quad(isFuel ? "barre_carburant" : "barre_temperature", b.Body, isFuel ? st.FuelBar0 : st.TempBar0, Vector3.zero, up * (15f / 128f), right * (1.6f / 128f));
                q.GetComponent<MeshRenderer>().sharedMaterial = mat;
                if (isFuel) st.FuelBar = q.transform; else st.TempBar = q.transform;
            }
        }
        static void FuelTick(VazCar.Built b, State st)
        {
            if (!st.HasFuel) return;
            float dt = Time.deltaTime;
            if (float.IsNaN(st.EngineTemp)) st.EngineTemp = Ambient();
            bool mine = !b.Rb.isKinematic;
            if (mine)
            {
                float rpm = b.Dt != null ? b.Dt.rpm : 0f, thr = b.Dt != null ? Mathf.Clamp01(b.Dt.throttle) : 0f, kmh = b.Rb.velocity.magnitude * 3.6f;
                if (st.EngineOn)
                {
                    // (ralenti 0,8 L/h, plein regime et plein gaz ~ 24 L/h ; le temps de jeu va plus vite que le temps reel : x 2)
                    float lh = 0.8f + rpm / 6000f * (0.15f + 0.85f * thr) * 23f;
                    st.Fuel = Mathf.Max(0f, st.Fuel - lh * 2f / 3600f / TankLitres * dt);
                    if (st.Fuel <= 0.1f && !st.FuelWarned) { st.FuelWarned = true; if (driving == b) Hud.Toast(Lang.T("Réservoir presque vide", "Fuel tank nearly empty")); }
                    if (st.Fuel <= 0f) { st.KeyPos = 0; SetEngine(b, false); Log.Info("standalone : panne seche"); }
                    // (monte a 90 degres en ~4 min, thermostat ; le plein regime sans air la pousse, le vent la ramene)
                    float load = rpm / 6000f * (0.3f + 0.7f * thr), target = 88f + Mathf.Clamp01(load - 0.5f) * 36f - Mathf.Clamp01(kmh / 80f) * Mathf.Clamp01(1f - load) * 4f;
                    st.EngineTemp += (target > st.EngineTemp ? 0.35f + load * 0.6f : -0.2f) * dt;
                }
                else st.EngineTemp = Mathf.MoveTowards(st.EngineTemp, Ambient(), (0.04f + kmh * 0.002f) * dt);
            }
            if (st.TempBar != null) st.TempBar.localPosition = st.TempBar0 + st.BarDir * st.BarRun * Mathf.InverseLerp(40f, 125f, st.EngineTemp);
            if (st.FuelBar != null) st.FuelBar.localPosition = st.FuelBar0 + st.BarDir * st.BarRun * Mathf.Clamp01(st.Fuel);
        }
        static float Ambient() { return Game.Msc ? 18f : -5f; }
        // Meme morceau, meme instant que le canal du jeu (commun a tous : Radio) ; contact coupe : silence.
        static void RadioTick(VazCar.Built b, State st)
        {
            if (!st.HasRadio || st.RadioSrc == null) return;
            bool powered = st.EngineOn || (st.KeyIn && st.KeyPos > 0);
            AudioSource src = st.RadioCh > 0 && powered ? Radio.Channel(st.RadioCh - 1) : null;
            if (src == null || src.clip == null) { if (st.RadioSrc.isPlaying) st.RadioSrc.Stop(); return; }
            st.RadioSrc.volume = st.RadioVol;
            if (st.RadioSrc.clip != src.clip) { st.RadioSrc.clip = src.clip; st.RadioSrc.time = Mathf.Min(src.time, Mathf.Max(0f, src.clip.length - 0.1f)); st.RadioSrc.Play(); return; }
            if (!st.RadioSrc.isPlaying) { st.RadioSrc.time = Mathf.Min(src.time, Mathf.Max(0f, src.clip.length - 0.1f)); st.RadioSrc.Play(); return; }
            if (Mathf.Abs(st.RadioSrc.time - src.time) > 1f && src.time < src.clip.length - 0.1f) st.RadioSrc.time = src.time;
        }
        // Visee sur le poste : clic, station suivante puis eteint ; molette, volume.
        static bool RadioInput(VazCar.Built b, State st, Transform cam)
        {
            if (!st.HasRadio) return false;
            Vector3 v = b.Car.transform.TransformPoint(st.RadioAt) - cam.position;
            float along = Vector3.Dot(v, cam.forward);
            if (tKnob != KRadio && (along < 0.1f || along > 1.3f || (v - cam.forward * along).magnitude > 0.07f)) return false;
            Game.SetGlobalBool("GUIuse", true);
            Game.SetGlobal("GUIinteraction", st.RadioCh == 0 ? "RADIO" : st.RadioCh == 1 ? "RADIO 1  " + Mathf.RoundToInt(st.RadioVol * 100f) + " %" : "RADIO FOLK  " + Mathf.RoundToInt(st.RadioVol * 100f) + " %");
            if (MDown()) { st.RadioCh = (st.RadioCh + 1) % 3; Foley(b, "dash_button", cam.position + v, 0.6f); SendState(b, st); Log.Info("standalone : radio " + st.RadioCh); }
            float sc = Input.GetAxis("Mouse ScrollWheel");
            if (sc != 0f) st.RadioVol = Mathf.Clamp01(st.RadioVol + Mathf.Sign(sc) * 0.05f);
            return true;
        }
        public static void SetQuestFuelIfIdle(float f)
        {
            foreach (VazCar.Built b in cars) { State st; if (QueteGolf.IsQuestCar(b) && states.TryGetValue(b, out st) && driving != b && !st.EngineOn) st.Fuel = f; }
        }
        // (couleur de la voiture de la quete : la matiere de peinture du modele, partagee par toutes ses pieces)
        public static void RepaintQuest(Color c, bool metal)
        {
            foreach (VazCar.Built b in cars)
            {
                if (!QueteGolf.IsQuestCar(b)) continue;
                VazCar.Repaint(b, c, metal);
            }
        }
        // [Test] QueteCommandes=1 (QueteGolf.TestTick) : au volant, photos de la planche, touches simulees, reservoir, temperature, couleur.
        static int qStep; static float qAt, qNextLog;
        public static void QuestControlsTest(float e)
        {
            VazCar.Built b = null; State st = null;
            foreach (VazCar.Built x in cars) if (QueteGolf.IsQuestCar(x)) { b = x; states.TryGetValue(x, out st); }
            if (b == null || st == null) return;
            Func<string> etat = () => "moteur " + st.EngineOn + ", phares " + st.LightsOn + ", pleins " + st.HighBeam + ", essuie " + st.WipersOn + ", clignotant " + st.Turn + ", warnings " + st.Hazard + ", cle " + st.KeyIn + "/" + st.KeyPos + ", reservoir " + st.Fuel.ToString("F3") + ", temp " + st.EngineTemp.ToString("F1");
            simDown = "";
            // (a deux : l'invite regarde ce que l'hote fait au volant, puis demande une autre couleur quand il voit la sienne)
            if (!Net.Session.IsHost)
            {
                if (e > qNextLog)
                {
                    qNextLog = e + 3f;
                    float rv; float rf = QuestFuel(out rv);
                    Log.Info("standalone : commandes, invite : " + etat() + ", radio " + st.RadioCh + ", reservoir vu " + rf.ToString("F3") + ", temp vue " + rv.ToString("F1"));
                }
                if (qAt <= 0f) qAt = e;
                if (qStep == 0 && e - qAt > 45f)
                {
                    qStep = 1;
                    QueteGolf.Shot("quete-invite", b.Car.transform.position + b.Car.transform.right * 5f + Vector3.up * 1.5f, b.Car.transform.position);
                    Log.Info("standalone : commandes, fin2, invite");
                }
                return;
            }
            if (qStep == 0 && e > 8f)
            {
                qStep = 1; qAt = e;
                GameObject pl = Game.PlayerGo; var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                pl.transform.position = b.Car.transform.TransformPoint(new Vector3(b.M.Head.x, 0.5f, st.T.EntreeZ));
                if (cc != null) cc.enabled = true;
                Enter(b, st);
                Log.Info("standalone : commandes, au volant : " + etat());
                st.Fuel = 0.6f; st.EngineTemp = 85f; FuelTick(b, st);
                Vector3 tacho = st.Tacho != null ? b.Car.transform.InverseTransformPoint(st.Tacho.Needle.position) : st.T.Yeux + Vector3.forward;
                Photo(b, "quete-compte-tours", st.T.Yeux + new Vector3(0.05f, -0.04f, 0.1f), tacho, 16f);
                Photo(b, "quete-planche", st.T.Yeux, tacho, 46f);
                Photo(b, "quete-console", new Vector3(0f, st.T.Yeux.y - 0.05f, st.T.Yeux.z), new Vector3(0f, 0.72f, 0.55f), 42f);
            }
            float d = e - qAt;
            string[] seq = { "HighBeam", "HighBeam", "HighBeam", "Wipers", "Wipers", "IndicatorLeft", "IndicatorLeft", "IndicatorRight" };
            if (qStep >= 1 && qStep <= seq.Length && d > 2f + qStep * 0.5f) { simDown = seq[qStep - 1]; GameKeys(b, st); simDown = ""; Log.Info("standalone : commandes, " + seq[qStep - 1] + " : " + etat()); qStep++; }
            if (qStep == seq.Length + 1 && d > 8f) { qStep++; simHeld = "StartEngine"; Log.Info("standalone : commandes, demarreur tenu : " + etat()); }
            if (qStep == seq.Length + 2 && d > 9.3f) { qStep++; simHeld = ""; Log.Info("standalone : commandes, demarreur relache : " + etat()); }
            if (qStep == seq.Length + 3 && d > 11f) { qStep++; simDown = "StartEngine"; simHeld = "StartEngine"; GameKeys(b, st); simDown = ""; simHeld = ""; GameKeys(b, st); Log.Info("standalone : commandes, arret a la touche : " + etat()); }
            if (qStep == seq.Length + 4 && d > 12f) { qStep++; st.Fuel = 0.002f; simHeld = "StartEngine"; }
            if (qStep == seq.Length + 5 && d > 14f) { qStep++; Log.Info("standalone : commandes, a sec apres 2 s de demarreur : " + etat()); st.Fuel = 0f; if (st.EngineOn) SetEngine(b, false); st.CrankT = -1f; st.StartLatch = false; }
            if (qStep == seq.Length + 6 && d > 19.5f) { qStep++; simHeld = ""; Log.Info("standalone : commandes, 5 s de demarreur a vide (fond retrouve) : " + etat()); }
            if (qStep == seq.Length + 7 && d > 21f)
            {
                qStep++;
                simHeld = "IndicatorRight"; simDown = "IndicatorLeft"; GameKeys(b, st); Log.Info("standalone : commandes, deux clignotants : " + etat()); GameKeys(b, st); simHeld = ""; simDown = "";
                Photo(b, "quete-radio", st.T.Yeux + new Vector3(0.2f, -0.03f, 0.1f), st.RadioAt, 20f);
                st.RadioCh = 1; st.KeyIn = true; st.KeyPos = 1; RadioTick(b, st);
                AudioSource rc = Radio.Channel(0);
                Log.Info("standalone : commandes, radio : canal " + (rc != null && rc.clip != null ? rc.clip.name + " a " + rc.time.ToString("F1") + " joue " + rc.isPlaying : "absent") + ", poste " + (st.RadioSrc.clip != null ? st.RadioSrc.clip.name + " a " + st.RadioSrc.time.ToString("F1") + " joue " + st.RadioSrc.isPlaying : "vide"));
                pumpsAt = null; bool np = NearPump(b.Car.transform.position);
                var ps = new System.Text.StringBuilder();
                if (pumpsAt != null) foreach (Transform t in pumpsAt) if (t != null) ps.Append(" ").Append(t.name).Append(" ").Append(t.position.ToString("F0"));
                Log.Info("standalone : commandes, pompes" + ps + ", voiture a moins de 12 m : " + np);
                // (plein : voiture posee a cote de la premiere pompe, 5 s de clic simule, sans passer par la marche a pied)
                if (!np && pumpsAt != null && pumpsAt.Length > 0 && pumpsAt[0] != null)
                {
                    Vector3 at = pumpsAt[0].position + pumpsAt[0].right * 4f + Vector3.up * 1.2f;
                    b.Car.transform.position = at;
                    Rigidbody rb = b.Car.GetComponent<Rigidbody>();
                    if (rb != null) { rb.position = at; rb.velocity = Vector3.zero; }
                }
                if (st.EngineOn) SetEngine(b, false);
                st.Fuel = 0.1f; st.FuelWarned = false; refillMoney0 = Wallet.Cash;
            }
            if (qStep == seq.Length + 8 && d > 22f && d <= 27f)
            {
                refillSim = true;
                bool pris = RefillPrompt(b, st, b.Car.transform);
                refillSim = false;
                if (!qPrompt && pris) { qPrompt = true; Log.Info("standalone : commandes, plein propose, " + Mathf.RoundToInt(st.Fuel * TankLitres) + " L"); }
            }
            if (qStep == seq.Length + 8 && d > 27f)
            {
                qStep++;
                Log.Info("standalone : commandes, plein : pompe proche " + NearPump(b.Car.transform.position) + ", propose " + qPrompt + ", argent " + refillMoney0.ToString("F0") + " -> " + Wallet.Cash.ToString("F0"));
                Log.Info("standalone : commandes, fini : " + etat());
            }
            if (qStep == seq.Length + 9) { qStep++; Log.Info("standalone : commandes, fin2, hote"); }
        }
        public static bool Driving { get { return driving != null; } }
        public static float QuestFuel(out float temp)
        {
            foreach (VazCar.Built b in cars) { State st; if (QueteGolf.IsQuestCar(b) && states.TryGetValue(b, out st)) { temp = st.EngineTemp; return st.Fuel; } }
            temp = float.NaN; return -1f;
        }
        // (plein a la pompe : a pied, voiture a moins de 12 m d'une pompe du jeu ; 6 L/s, paye en liquide)
        static Transform[] pumpsAt; static float nextPumps;
        static bool NearPump(Vector3 p)
        {
            if ((pumpsAt == null || pumpsAt.Length == 0 || pumpsAt[0] == null) && Time.realtimeSinceStartup > nextPumps)
            {
                nextPumps = Time.realtimeSinceStartup + 20f;
                var l = new List<Transform>();
                if (Game.Msc)
                {
                    foreach (string s in new[] { "STORE/LOD/Pumps/GasolinePump98", "STORE/LOD/Pumps/GasolinePumpD", "STORE/LOD/Pumps/GasolinePumpPÖ" })
                    { GameObject g = Game.FindAny(s); if (g != null) l.Add(g.transform); }
                }
                else
                    foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
                        if (t.name.StartsWith("FuelPumps_") && t.gameObject.hideFlags == HideFlags.None) l.Add(t);
                pumpsAt = l.ToArray();
            }
            if (pumpsAt == null) return false;
            foreach (Transform t in pumpsAt) if (t != null && (t.position - p).sqrMagnitude < 144f) return true;
            return false;
        }
        static float refillPaid, refillMoney0; static bool refillSim, qPrompt;
        // vrai : le texte du jeu est pris
        static bool RefillPrompt(VazCar.Built b, State st, Transform pl)
        {
            if (!st.HasFuel || st.EngineOn || QueteGolf.Locked(b) || st.Fuel >= 0.995f) return false;
            if ((pl.position - b.Car.transform.position).sqrMagnitude > 16f || !NearPump(b.Car.transform.position)) return false;
            Game.SetGlobalBool("GUIuse", true);
            Game.SetGlobal("GUIinteraction", Lang.TG("FAIRE LE PLEIN", "FILL UP") + " " + Mathf.RoundToInt(st.Fuel * TankLitres) + " L");
            if ((Input.GetMouseButton(0) || refillSim) && (!Wallet.Ready || Wallet.Cash >= 1f))
            {
                float dl = 6f * Time.deltaTime;
                refillPaid += dl * FuelPerLitre;
                st.Fuel = Mathf.Min(1f, st.Fuel + dl / TankLitres); st.FuelWarned = false;
                if (refillPaid >= 1f && Wallet.Ready) { int mk = Mathf.FloorToInt(refillPaid); refillPaid -= mk; Wallet.Spend(mk); }
            }
            else if (Input.GetMouseButtonUp(0)) SendState(b, st);
            return true;
        }

        // Portieres : visee au centre de l'ecran (a moins de 35 cm du centre de la portiere, a moins de 2 m), texte du jeu, clic gauche
        // ou touche Use : ouverte (elle part sur sa charniere) ou refermee (poussee jusqu'au pene). Hayon (2) et plage arriere (3) aussi.
        static int doorHover = -1;
        static void Doors(VazCar.Built b, State st)
        {
            Transform cam = PlayerCam();
            if (cam == null || st.Doors == null) return;
            int hit = -1; float best = 0.35f;
            for (int i = 0; i < 6; i++)
            {
                Vector3 c;
                if (i >= 4)
                {
                    // (dossier avant : portiere de son cote ouverte, ou depuis l'habitacle ; pas au volant)
                    int k = i - 4;
                    if (st.SeatBack[k] == null || driving != null) continue;
                    if (st.DoorAngle[k] < 20f && Mathf.Abs(b.Car.transform.InverseTransformPoint(cam.position).x) > 0.65f) continue;
                    Bounds sb = st.SeatBackBox[k];
                    c = st.SeatBack[k].TransformPoint(new Vector3(sb.center.x, sb.max.y - 0.18f, sb.center.z) - st.SeatBack[k].localPosition);
                }
                else if (i < 2) { if (st.Doors[i] == null) continue; c = st.Doors[i].TransformPoint(st.DoorCenter[i] - st.Doors[i].localPosition); }
                else if (i == 2) { if (st.Hatch == null) continue; c = st.Hatch.TransformPoint(st.HatchCenter - st.Hatch.localPosition); }
                else
                {
                    // (plage : coffre ouvert, ou depuis l'habitacle -- JD 31/10 : on la relevait de dehors, hayon ferme)
                    if (st.Shelf == null) continue;
                    if (!st.HatchOpen && b.Car.transform.InverseTransformPoint(cam.position).z < st.ShelfCenter.z + 0.15f) continue;
                    c = st.Shelf.TransformPoint(st.ShelfCenter - st.Shelf.localPosition);
                }
                Vector3 v = c - cam.position;
                float along = Vector3.Dot(v, cam.forward);
                if (along < 0.1f || along > 2f) continue;
                float perp = (v - cam.forward * along).magnitude;
                if (perp < best) { best = perp; hit = i; }
            }
            if (hit < 0) { if (doorHover >= 0) { doorHover = -1; Game.SetGlobalBool("GUIuse", false); if (prompted == null) Game.SetGlobal("GUIinteraction", ""); } return; }
            doorHover = hit;
            Game.SetGlobalBool("GUIuse", true);
            // (voiture de la quete s21, pas encore pretee : portieres et hayon fermes a cle)
            if (hit <= 2 && QueteGolf.Locked(b)) { if (prompted == null) Game.SetGlobal("GUIinteraction", "LOCKED"); return; }
            if (prompted == null) Game.SetGlobal("GUIinteraction", hit < 2 ? (st.DoorOpen[hit] ? "CLOSE DOOR" : "OPEN DOOR") : hit == 2 ? (st.HatchOpen ? "CLOSE TRUNK" : "OPEN TRUNK") : hit == 3 ? (st.ShelfUp ? "LOWER SHELF" : "LIFT SHELF") : (st.Folded[hit - 4] ? "RAISE SEAT" : "FOLD SEAT"));
            if (!Input.GetMouseButtonDown(0) && !Down("Use")) return;
            if (hit >= 4) { st.Folded[hit - 4] = !st.Folded[hit - 4]; SendState(b, st); Log.Info("standalone : dossier " + (hit == 4 ? "gauche" : "droit") + (st.Folded[hit - 4] ? " rabattu" : " releve")); return; }
            if (hit == 2) { st.HatchOpen = !st.HatchOpen; SendState(b, st); Log.Info("standalone : hayon " + (st.HatchOpen ? "ouvert" : "ferme")); return; }
            if (hit == 3) { st.ShelfUp = !st.ShelfUp; Log.Info("standalone : plage arriere " + (st.ShelfUp ? "relevee" : "baissee")); return; }
            ToggleDoor(b, st, hit);
        }
        // (portiere : ouverte = lancee sur sa charniere ; fermee = poussee vers le pene -- elle se verrouille en y arrivant)
        static void ToggleDoor(VazCar.Built b, State st, int hit)
        {
            string side = hit == 0 ? "conducteur" : "passager";
            if (!st.DoorOpen[hit])
            {
                st.DoorOpen[hit] = true; st.DoorVel[hit] = 260f;
                if (b.Rb.isKinematic) st.DoorLocalT[hit] = Time.realtimeSinceStartup;
                SendState(b, st); Log.Info("standalone : portiere " + side + " ouverte");
            }
            // (desync du 02/11, JD et GG : passager de la VAZ menee par l'autre, « CLOSE DOOR » ne faisait rien -- la vitesse de charniere
            // ne sert que sur une voiture physique ici ; la copie cinematique suit DoorOpen, reste vrai, et rien n'etait envoye : ouverte
            // chez tous jusqu'a ce que l'inertie de la voiture du conducteur la referme. Copie : fermee ici et envoyee, celui qui mene la
            // voiture la pousse vers le pene a la reception. [Test] PortiereCopieFermee=0 : comme avant)
            else if (b.Rb.isKinematic && Config.GetInt("Test", "PortiereCopieFermee", 1) != 0)
            {
                st.DoorOpen[hit] = false; st.DoorVel[hit] = 0f; st.DoorLocalT[hit] = Time.realtimeSinceStartup;
                SendState(b, st); Log.Info("standalone : portiere " + side + " fermee (copie menee par un autre)");
            }
            else { st.DoorVel[hit] = -420f; Log.Info("standalone : portiere " + side + " fermeture demandee"); }
        }
        // Portieres comme celles du jeu (charniere physique, JD 31/10) : libres une fois ouvertes -- elles suivent l'inertie de la voiture
        // (freinage : elles s'ouvrent, acceleration : elles se ferment), rebondissent en butee a 70 degres, se verrouillent en revenant
        // a 1 degre ; le joueur a pied les pousse (il ne peut pas les traverser, la portiere s'ecarte devant lui). Voiture menee
        // ailleurs (corps cinematique) : ouverte / fermee selon l'etat recu. Hayon et plage : animes vers leur position.
        // Portiere qui pivote dans le joueur d'ici a pied (JD 09/11 : « la portiere qui projette un joueur debout ») : elle ne pivote pas par la
        // physique (rotation posee chaque image, la copie d'une voiture menee ailleurs comme la voiture d'ici) ; entree dans la capsule du
        // joueur, le CharacterController l'en sortait d'un coup -- souvent par le haut -- et le CharacterMotor du jeu prenait ce deplacement
        // pour une vitesse : joueur envoye a ~100 m sur My Summer Car (essai des commandes du 08/11, invite poste dans la course de la
        // portiere de la copie). Les portieres du jeu sont des corps sur charniere (HingeJoint) : elles butent sur le joueur. Ici : angle
        // borne pour que la portiere (droite de la charniere a son bord, epaisseur comprise) n'entre jamais dans la capsule -- elle s'arrete
        // sur lui, a l'ouverture comme a la fermeture, et repart quand il s'ecarte. Portiere = sa boite du modele (fermee, celle de la boite
        // joueur « joueur_portiere »), tournee autour de la charniere ; joueur = sa capsule (rayon, hauteur).
        static float DoorBlock(VazCar.Built b, State st, int i, float s, float from, float to, GameObject pl)
        {
            if (pl == null || Mathf.Abs(to - from) < 1e-4f) return to;
            var cc = pl.GetComponent<CharacterController>();
            Vector3 sc = pl.transform.lossyScale;
            Vector3 cw = cc != null ? pl.transform.TransformPoint(cc.center) : pl.transform.position;
            float rc = (cc != null ? cc.radius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.z)) : 0.3f) + 0.03f;
            float half = cc != null ? Mathf.Max(cc.height * 0.5f * Mathf.Abs(sc.y), rc) : 0.9f;
            Vector3 hp = st.Doors[i].localPosition, pp = b.Body.InverseTransformPoint(cw);
            Bounds db = DoorShape(st, i);
            if (pp.y + half < db.min.y || pp.y - half > db.max.y) return to;
            float d0 = DoorGap(db, hp, pp, s, from), d1 = DoorGap(db, hp, pp, s, to);
            if (d1 >= rc) return to;                       // (rien a l'arrivee -- pas de portiere assez rapide pour le traverser en une image)
            if (d0 < rc) return d1 > d0 ? to : from;       // (deja dedans : seulement ce qui l'en eloigne)
            float lo = from, hi = to;                       // (premier contact entre les deux : on s'arrete juste avant)
            for (int k = 0; k < 10; k++) { float mid = (lo + hi) * 0.5f; if (DoorGap(db, hp, pp, s, mid) >= rc) lo = mid; else hi = mid; }
            return lo;
        }
        // (distance horizontale du point a la boite de la portiere tournee de a degres autour de la charniere : on tourne le point en sens inverse)
        static float DoorGap(Bounds db, Vector3 hp, Vector3 pp, float s, float a)
        {
            Vector3 q = Quaternion.Euler(0f, -s * a, 0f) * (pp - hp) + hp;
            float dx = Mathf.Max(db.min.x - q.x, 0f, q.x - db.max.x), dz = Mathf.Max(db.min.z - q.z, 0f, q.z - db.max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
        // (boite de la portiere fermee ; sans boite du modele : 20 cm d'epaisseur, longueur double de la charniere au centre)
        static Bounds DoorShape(State st, int i)
        {
            Bounds db = st.DoorBox[i];
            if (db.size.y > 0.3f) return db;
            Vector3 hp = st.Doors[i].localPosition, c = st.DoorCenter[i];
            return new Bounds(new Vector3(c.x, hp.y, c.z), new Vector3(0.2f, 1.4f, Mathf.Max(0.6f, 2f * Mathf.Abs(c.z - hp.z))));
        }
        // (longueur de la portiere : de la charniere a son point le plus loin, portiere fermee)
        static float DoorReach(State st, int i)
        {
            Vector3 hp = st.Doors[i].localPosition; Bounds db = DoorShape(st, i); float r = 0f;
            foreach (float x in new[] { db.min.x, db.max.x }) foreach (float z in new[] { db.min.z, db.max.z }) r = Mathf.Max(r, new Vector2(x - hp.x, z - hp.z).magnitude);
            return Mathf.Clamp(r, 0.5f, 2.2f);
        }
        static void DoorAnim(VazCar.Built b, State st)
        {
            if (st.Doors == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            bool phys = !b.Rb.isKinematic;
            Vector3 acc = Vector3.zero;
            if (phys)
            {
                Vector3 v = b.Car.transform.InverseTransformDirection(b.Rb.velocity);
                if (st.LastVelT > 0f && Time.time - st.LastVelT < 0.2f) acc = (v - st.LastVel) / dt;
                st.LastVel = v; st.LastVelT = Time.time;
            }
            GameObject pl = Game.PlayerGo;
            bool afoot = pl != null && driving == null && !Seats.Seated;
            bool guardOn = Config.GetInt("Test", "PortiereGarde", 1) != 0;   // ([Test] PortiereGarde=0 : comme avant, pour comparer)
            for (int i = 0; i < 2; i++)
            {
                if (st.Doors[i] == null) continue;
                float s = i == 0 ? 1f : -1f;
                if (!phys)
                {
                    float want = st.DoorOpen[i] ? 70f : 0f, prev = st.DoorAngle[i];
                    st.DoorAngle[i] = Mathf.MoveTowards(prev, want, 220f * dt);
                    if (afoot && guardOn) st.DoorAngle[i] = DoorBlock(b, st, i, s, prev, st.DoorAngle[i], pl);   // (copie : elle s'arrete sur le joueur d'ici)
                }
                else if (st.DoorOpen[i])
                {
                    float a = st.DoorAngle[i], vel = st.DoorVel[i], prev = a;
                    vel += -Mathf.Clamp(acc.z, -30f, 30f) * 22f * dt;
                    vel *= Mathf.Exp(-2.2f * dt);
                    a += vel * dt;
                    if (a > 70f) { a = 70f; vel = -Mathf.Abs(vel) * 0.25f; }
                    // (joueur a pied pres de la portiere : elle s'ecarte devant lui -- vers l'exterieur s'il est dedans, vers le pene s'il pousse
                    // du dehors)
                    if (afoot)
                    {
                        Vector3 hp = st.Doors[i].localPosition, pp = b.Body.InverseTransformPoint(pl.transform.position);
                        float wx = pp.x - hp.x, wz = pp.z - hp.z, r = Mathf.Sqrt(wx * wx + wz * wz);
                        if (r > 0.2f && r < (guardOn ? DoorReach(st, i) + 0.35f : 1.15f) && Mathf.Abs(pp.y - hp.y) < 1.2f)
                        {
                            float pa = Mathf.Atan2(-s * wx, -wz) * Mathf.Rad2Deg, m = Mathf.Rad2Deg * 0.32f / r;
                            if (pa > -10f && pa < a && a - pa < m) { a = Mathf.Min(70f, pa + m); vel = Mathf.Max(vel, 0f); }
                            else if (pa >= a && pa - a < m) { a = Mathf.Max(0f, pa - m); vel = Mathf.Min(vel, 0f); }
                        }
                        // (et jamais dans sa capsule, quelle que soit sa vitesse : arretee sur lui)
                        if (guardOn) { float g = DoorBlock(b, st, i, s, prev, a, pl); if (g != a) { a = g; vel = 0f; } }
                    }
                    if (a <= 1f && vel <= 0f)
                    {
                        a = 0f; vel = 0f; st.DoorOpen[i] = false; SendState(b, st);
                        Log.Info("standalone : portiere " + (i == 0 ? "conducteur" : "passager") + " fermee");
                    }
                    st.DoorAngle[i] = Mathf.Max(0f, a); st.DoorVel[i] = vel;
                }
                else { st.DoorAngle[i] = 0f; st.DoorVel[i] = 0f; }
                // (boite joueur de la portiere : seulement fermee -- ouverte, c'est la portiere qui s'ecarte devant le joueur)
                if (st.DoorCol[i] != null) st.DoorCol[i].enabled = st.DoorAngle[i] < 1f;
                st.Doors[i].localRotation = Quaternion.Euler(0f, s * st.DoorAngle[i], 0f);
                // (son : ouverte des qu'elle quitte le pene, fermee en y revenant -- rien au premier passage, voiture chargee portiere ouverte)
                bool shut = !st.DoorOpen[i] && st.DoorAngle[i] < 1f;
                if (st.SndInit && shut != st.DoorShut[i]) Foley(b, shut ? "close_door1" : "open_door1", st.Doors[i].TransformPoint(st.DoorCenter[i] - st.Doors[i].localPosition), 1f);
                st.DoorShut[i] = shut;
            }
            if (st.Hatch != null)
            {
                st.HatchAngle = Mathf.MoveTowards(st.HatchAngle, st.HatchOpen ? 78f : 0f, 110f * dt);
                st.Hatch.localRotation = Quaternion.Euler(st.HatchAngle, 0f, 0f);
                bool shut = !st.HatchOpen && st.HatchAngle < 1f;
                if (st.SndInit && shut != st.HatchShut) Foley(b, shut ? "close_trunk1" : "open_trunk1", st.Hatch.position, 1f);
                st.HatchShut = shut;
            }
            st.SndInit = true;
            for (int k = 0; k < 2; k++)
            {
                if (st.SeatBack[k] == null) continue;
                st.FoldAngle[k] = Mathf.MoveTowards(st.FoldAngle[k], st.Folded[k] ? 42f : 0f, 140f * dt);
                st.SeatBack[k].localRotation = Quaternion.AngleAxis(st.FoldAngle[k], st.SeatBackAxis[k]);
                if (st.SeatBackCol[k] != null) st.SeatBackCol[k].enabled = st.FoldAngle[k] < 5f;
            }
            if (st.Shelf != null)
            {
                // (relevee a la main, ou soulevee par ses cordons quand le hayon s'ouvre)
                float want = st.ShelfUp ? 80f : st.HatchAngle / 78f * 24f;
                st.ShelfAngle = Mathf.MoveTowards(st.ShelfAngle, want, 120f * dt);
                st.Shelf.localRotation = Quaternion.Euler(st.ShelfAngle, 0f, 0f);
            }
        }

        // Habitacle ou l'on entre a pied (JD 28/10 : « je ne peux pas rentrer dans la voiture, comme dans les voitures du jeu, apres avoir
        // ouvert la porte ») -- comme les voitures du jeu : coque creuse pour le joueur (couche PlayerColl) -- plancher, toit, avant
        // (planche), arriere, flanc droit, morceau derriere la portiere, et la portiere conducteur elle-meme qui tourne avec elle --,
        // bornes tirees de la portiere conducteur du modele (habitacle de son arriere -13 cm jusqu'a 27 cm avant sa charniere, entre
        // son bas et son haut, largeur a son bord interieur).
        static void Shell(VazCar.Built b, State st)
        {
            Bounds bb, db = st.DriverDoor;
            if (buildPlayerLayer < 0 || db.size.y < 0.3f || !VazCar.ModelBounds(b.M, "carrosserie.p205", out bb)) { Log.Warn("standalone : pas d'habitacle creux (couche joueur ou portiere absente)"); return; }
            int L = buildPlayerLayer; Transform ct = b.Car.transform;
            float zMin = db.min.z - 0.13f, zMax = db.max.z - 0.27f, fl = db.min.y + 0.02f, rf = db.max.y + 0.02f, ix = Mathf.Min(Mathf.Abs(db.max.x), Mathf.Abs(db.min.x)) + 0.01f;
            // (banquette : habitacle jusque derriere son dossier -- les dossiers avant, releves, en ferment le passage ; JD 31/10)
            float zDoor = zMin;
            if (st.HasRear && st.CabinRear < zMin && st.CabinRear > bb.min.z + 0.1f) zMin = st.CabinRear;
            float x0 = bb.min.x, x1 = bb.max.x;
            // (rien sur la course des roues : le rayon d'une roue prenait le plancher pour le sol -- essai du 28/10, voiture envolee ;
            // plancher sous l'habitacle seulement, blocs avant / arriere au-dessus des roues, et cette couche retiree du rayon : WheelMask)
            float wheelTop = 0f; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>(true)) wheelTop = Mathf.Max(wheelTop, w.transform.localPosition.y - StandaloneCar.HubDrop + w.radius + 0.03f);
            float fb = Mathf.Max(fl, wheelTop);
            // (plancher du joueur 8 cm sous le bas de la portiere : marche de 24 cm depuis le sol -- a 32 cm le joueur restait parfois
            // bloque sur le seuil, essai du 29/10)
            Box(ct, L, "joueur_plancher", new Vector3(0f, fl - 0.16f, (zMin + zMax) * 0.5f), new Vector3(2f * ix, 0.16f, zMax - zMin));
            Box(ct, L, "joueur_toit", new Vector3(0f, (rf + bb.max.y + 0.02f) * 0.5f, bb.center.z), new Vector3(bb.size.x, bb.max.y + 0.02f - rf, bb.size.z));
            Box(ct, L, "joueur_avant", new Vector3(0f, (fb + rf) * 0.5f, (zMax + bb.max.z) * 0.5f), new Vector3(bb.size.x, rf - fb, bb.max.z - zMax));
            Box(ct, L, "joueur_arriere", new Vector3(0f, (fb + rf) * 0.5f, (bb.min.z + zMin) * 0.5f), new Vector3(bb.size.x, rf - fb, zMin - bb.min.z));
            // (cote passager : comme le conducteur si sa portiere est la -- JD 28/10 : « pas cote passager, le passage est toujours
            // bloque, porte ouverte » --, sinon flanc plein)
            Bounds pd = st.DoorBox[1];
            if (pd.size.y > 0.3f) Box(ct, L, "joueur_flanc_d_ar", new Vector3((ix + x1) * 0.5f, (fl + rf) * 0.5f, (zMin + pd.min.z) * 0.5f), new Vector3(x1 - ix, rf - fl, pd.min.z - zMin));
            else Box(ct, L, "joueur_flanc_d", new Vector3((ix + x1) * 0.5f, (fl + rf) * 0.5f, (zMin + zMax) * 0.5f), new Vector3(x1 - ix, rf - fl, zMax - zMin));
            Box(ct, L, "joueur_flanc_g_ar", new Vector3((x0 - ix) * 0.5f, (fl + rf) * 0.5f, (zMin + db.min.z) * 0.5f), new Vector3(-ix - x0, rf - fl, db.min.z - zMin));
            for (int k = 0; k < 2; k++)
            {
                if (st.SeatBack[k] == null || zMin >= zDoor) continue;
                Bounds sb = st.SeatBackBox[k]; float xa = k == 0 ? -ix : 0f, xb = k == 0 ? 0f : ix;
                st.SeatBackCol[k] = Box(ct, L, "joueur_dossier_" + (k == 0 ? "g" : "d"), new Vector3((xa + xb) * 0.5f, (fl + rf) * 0.5f, sb.center.z), new Vector3(xb - xa, rf - fl, Mathf.Max(0.12f, sb.size.z)));
            }
            st.Cabin = new Bounds(); st.Cabin.SetMinMax(new Vector3(-ix, fl, zMin), new Vector3(ix, rf, zMax));
            st.HasCabin = true;
            Log.Info("standalone : habitacle creux " + st.Cabin.min.ToString("F2") + " - " + st.Cabin.max.ToString("F2") + ", couche " + LayerMask.LayerToName(L));
            // (toutes les voitures, JD 07/11 : « tout passe a travers » ; boites invisibles a part, rien ne change au modele)
            if (!Config.Testing || Config.GetInt("Test", "CoqueSans", 0) == 0) ItemShell(b, st, bb, ix, fl, rf, fb, zMin, zMax);
        }

        // Coque pour les objets (Golf, JD 01/11 : « les objets passent a travers, qu'ils restent a l'interieur comme dans les autres
        // voitures ») : les objets du jeu (couche Parts) ne heurtent pas la couche des boites de la caisse ; comme la Satsuma (habitacle
        // sur Collider2, sieges sur HingedObjects -- releve SondeCouches du 01/11) : plancher de l'habitacle et du coffre, toit, planche,
        // flancs derriere les portieres sur Collider2 ; une paroi dans chaque portiere et dans le hayon (elles s'ecartent avec eux : on
        // sort les objets portiere ou hayon ouvert) ; assises et dossiers des sieges sur HingedObjects (les dossiers avant suivent le
        // rabattement). Les deux couches retirees du rayon des roues (WheelMask) ; inertie de la caisse gardee (pas de changement de tenue).
        static int itemLayer = -1, seatItemLayer = -1;
        // (le jeu affiche le nom de l'objet vise, moins ses 7 derniers signes -- son « (itemx) » : une boite « objets_assise_0 » donnait OBJETS_A au milieu de l'ecran, JD 07/11.
        // Un nom de 7 signes : aucun texte. Couches et collisions inchangees.)
        static string ShellName(string old) { return Config.Testing && Config.GetInt("Test", "CoqueNoms", 0) != 0 ? old : "coque_x"; }
        static void ItemShell(VazCar.Built b, State st, Bounds bb, float ix, float fl, float rf, float fb, float zMin, float zMax)
        {
            int IL = LayerMask.NameToLayer("Collider2"), SL = LayerMask.NameToLayer("HingedObjects"), parts = LayerMask.NameToLayer("Parts");
            var shellClock = System.Diagnostics.Stopwatch.StartNew();
            if (IL < 0 || SL < 0 || parts < 0 || Physics.GetIgnoreLayerCollision(parts, IL) || Physics.GetIgnoreLayerCollision(parts, SL)) { Log.Warn("standalone : pas de coque pour les objets (couches Collider2 / HingedObjects)"); return; }
            itemLayer = IL; seatItemLayer = SL;
            Transform ct = b.Car.transform;
            Vector3 inertia = b.Rb.inertiaTensor; Quaternion inertiaRot = b.Rb.inertiaTensorRotation; Vector3 com = b.Rb.centerOfMass;
            const float T = 0.08f;   // (epaisseur des parois)
            float zBack = bb.min.z + 0.14f;   // (fond du coffre : derriere, le hayon)
            // (paquet sans la place des sieges : toit lu sur la peinture du modele -- le haut de la portiere n'y est pas toujours, 1,04 m sur l'une pour 1,39 m de toit --, et limite a sa longueur)
            float roofBack = zBack, roofFront = zMax;
            if (!st.HasSeatRef[0] && b.M != null)
            {
                float top, lo, hi;
                if (VazCar.RoofSpan(b.M, zMin, zMax, b.M.Head.y, out top, out lo, out hi)) { rf = top - T; roofBack = Mathf.Max(zBack, lo); roofFront = Mathf.Min(zMax, hi); }
            }
            // (plancher sur la couche que le joueur ne heurte pas, comme les sieges : sur celle des parois, il marchait 8 cm plus haut que sur son propre plancher, et la tete,
            // gardee ou elle etait en s'asseyant, restait dans le toit -- JD 07/11 : « je suis trop haut a l'interieur »)
            Box(ct, SL, ShellName("objets_plancher"), new Vector3(0f, fl - T * 0.5f, (zMin + zMax) * 0.5f), new Vector3(2f * ix, T, zMax - zMin));
            Box(ct, IL, ShellName("objets_coffre"), new Vector3(0f, fb - T * 0.5f, (zBack + zMin) * 0.5f), new Vector3(2f * ix, T, zMin - zBack));
            Box(ct, IL, ShellName("objets_toit"), new Vector3(0f, rf + T * 0.5f, (roofBack + roofFront) * 0.5f), new Vector3(2f * ix, T, roofFront - roofBack));
            // (marche entre le plancher et le coffre, plus haut : un objet y passait dessous et sortait de la voiture au premier coup de frein -- essai du 07/11)
            if (fb > fl + 0.02f) Box(ct, IL, ShellName("objets_marche"), new Vector3(0f, (fl + fb) * 0.5f, zMin - T * 0.5f), new Vector3(2f * ix, fb - fl, T));
            Box(ct, IL, ShellName("objets_planche"), new Vector3(0f, (fl + rf) * 0.5f, zMax + T * 0.5f), new Vector3(2f * ix, rf - fl, T));
            // flancs : de l'arriere de chaque portiere au fond du coffre (sans portiere de ce cote : tout le long)
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? -1f : 1f; Bounds d = st.DoorBox[s];
                float zEnd = d.size.y > 0.3f ? d.min.z : zMax;
                Box(ct, IL, ShellName("objets_flanc_" + (s == 0 ? "g" : "d")), new Vector3(sx * (ix + T * 0.5f), (fl + rf) * 0.5f, (zBack + zEnd) * 0.5f), new Vector3(T, rf - fl, zEnd - zBack));
                // paroi dans la portiere (tourne avec elle)
                if (st.Doors[s] != null && d.size.y > 0.3f)
                {
                    Transform inner = st.Doors[s].Find("pieces");
                    if (inner != null) Box(inner, IL, ShellName("objets_portiere"), new Vector3(sx * (ix + T * 0.5f), (fl + rf) * 0.5f, d.center.z), new Vector3(T, rf - fl, d.size.z));
                }
            }
            // fond du coffre : dans le hayon s'il s'ouvre
            Transform hp = st.Hatch != null ? st.Hatch.Find("pieces") : null;
            Box(hp ?? ct, IL, ShellName("objets_hayon"), new Vector3(0f, (fb + rf) * 0.5f, zBack - T * 0.5f), new Vector3(2f * ix, rf - fb, T));
            // sieges : assises (avant, banquette) fixes ; dossiers avant dans leur pivot (rabattus avec eux), dossier de la banquette fixe
            for (int k = 0; k < 4; k++)
            {
                // (paquet sans la place des sieges : les deux avant d'apres la tete du conducteur, ecarts mesures sur les baquets des modeles -- dossier 2 a 18 cm derriere la tete, assise 60 cm sous elle ; pas de banquette)
                if (!st.HasSeatRef[k] && (k >= 2 || b.M == null || b.M.Head == Vector3.zero)) continue;
                Vector3 c = st.HasSeatRef[k] ? st.SeatRef[k] : new Vector3((k == 0 ? -1f : 1f) * Mathf.Abs(b.M.Head.x), b.M.Head.y - 0.60f, b.M.Head.z - 0.05f); bool rear = k >= 2;
                if (rear && k == 3) continue;   // (banquette : une seule assise et un seul dossier, sur toute la largeur)
                float cx = rear ? 0f : c.x, w = rear ? 2f * ix - 0.1f : 0.46f;
                Box(ct, SL, ShellName("objets_assise_" + k), new Vector3(cx, c.y - 0.06f, c.z + 0.27f), new Vector3(w, 0.12f, 0.46f));
                Transform par = !rear && st.SeatBack[k] != null ? st.SeatBack[k].Find("pieces") : null;
                Box(par ?? ct, SL, ShellName("objets_dossier_" + k), new Vector3(cx, c.y + 0.3f, c.z - 0.06f), new Vector3(w, 0.5f, 0.12f));
            }
            b.Rb.inertiaTensor = inertia; b.Rb.inertiaTensorRotation = inertiaRot; b.Rb.centerOfMass = com;
            GameObject pl = Game.PlayerGo;
            Log.Info("standalone : coque pour les objets, " + shellClock.ElapsedMilliseconds + " ms (" + LayerMask.LayerToName(IL) + ", sieges " + LayerMask.LayerToName(SL) + ")" + (pl != null ? ", le joueur heurte " + LayerMask.LayerToName(IL) + " : " + !Physics.GetIgnoreLayerCollision(IL, pl.layer) + ", " + LayerMask.LayerToName(SL) + " : " + !Physics.GetIgnoreLayerCollision(SL, pl.layer) : ""));
        }

        // Choc mortel. Les voitures du jeu tuent par la liaison du pivot de tete du conducteur (releve 29/10 sur la Satsuma : tete de
        // 0,5 kg, liaison bloquee sur les trois axes, rupture a 300 N au volant, 500 avec le casque -- automate HelmetSim) : ~43 km/h
        // perdus en un pas de physique (0,02 s) au point de la tete. Ici la meme mesure (CrashSensor, a chaque pas), mais seulement a
        // l'horizontale : un vrai choc contre un obstacle, pas une bosse prise vite, la suspension qui talonne ni la reception d'un saut
        // (JD 29/10 : « je suis mort trop facilement en passant vite sur une bosse » -- la 1re version comptait toute vitesse perdue en
        // 0,1 s, verticale comprise, des 40 km/h). Seuil : tenue.ini [Poste] ChocMortel (45 km/h a 50 pas/s ; x1,6 casque mis). Puis la
        // mort du jeu par accident (Systems/Death, cause Crash : sang, ecran, reapparition en co-op -- Respawn.TestDie).
        public class CrashSensor : MonoBehaviour
        {
            public Rigidbody Rb; public Vector3 Head;   // (tete, repere de la voiture)
            public float MaxH, MaxV;                    // (plus grandes vitesses perdues en un pas depuis la derniere lecture, m/s)
            Vector3 prev; bool has;
            public void Reset() { has = false; MaxH = MaxV = 0f; }   // (vitesse posee a la main : essais, teleportation)
            void FixedUpdate()
            {
                if (Rb == null || Rb.isKinematic) { has = false; return; }
                Vector3 v = Rb.GetPointVelocity(transform.TransformPoint(Head));
                if (has)
                {
                    Vector3 d = v - prev, vert = Vector3.Project(d, Vector3.up);
                    MaxH = Mathf.Max(MaxH, (d - vert).magnitude); MaxV = Mathf.Max(MaxV, vert.magnitude);
                }
                prev = v; has = true;
            }
        }

        // Journal des contacts de la caisse (JD 09/11 : « en rallye sur les petites routes, une collision comme si je tapais un arbre alors
        // que ce sont juste des petites bosses ») -- rien ne change a la conduite, seulement le journal : a chaque nouveau contact notable
        // de la voiture conduite ici (vitesse le long de la normale > 2 m/s), la piece de NOTRE voiture qui touche (boite_caisse,
        // boite_habitacle, portiere...), ce qu'elle touche, la normale dans le repere de la voiture (y : vers le haut, z : vers l'avant),
        // la vitesse, la position et le cap ; 30 lignes par minute au plus. Au choc mortel, les 6 derniers contacts (meme ceux sous le
        // seuil), pour savoir ce qui a tue.
        public class ContactLog : MonoBehaviour
        {
            public Rigidbody Rb; public Transform Car;
            struct Hit { public float At, Normal, Speed; public string Part, Other; public Vector3 N, Pos; public bool Stay, Hard; }
            readonly Hit[] ring = new Hit[8]; int ringN;
            float minuteAt; int minuteN;
            // (dernier vrai obstacle -- JD 09/11 18:57, mort a la reception d'un saut sans rien heurter : un contact dont la normale n'est pas
            // tournee vers le haut (|n.y| < 0,8 : arbre, mur, rocher, voiture, talus pris de face), ou du haut de l'habitacle (tonneau), a 6 m/s
            // au moins contre la surface)
            public float HardAt = -10f; public string HardLine = "";
            void OnCollisionEnter(Collision c) { Note(c, false); }
            // (contact qui dure : la meme boite sur le meme collider -- la route -- ne fait pas de nouvelle entree ; le pas fatal du 09/11 18:57
            // n'etait donc pas au journal. Note seulement s'il pousse fort : 2 m/s contre la surface)
            void OnCollisionStay(Collision c) { Note(c, true); }
            void Note(Collision c, bool stay)
            {
                if (c == null || c.collider == null || c.contacts == null || c.contacts.Length == 0 || Car == null) return;
                ContactPoint p = c.contacts[0];
                float vn = 0f;
                foreach (ContactPoint q in c.contacts) { float x = Mathf.Abs(Vector3.Dot(c.relativeVelocity, q.normal)); if (x > vn) { vn = x; p = q; } }
                if (stay && vn < 2f) return;
                bool hard = (Mathf.Abs(p.normal.y) < 0.8f || (p.thisCollider != null && p.thisCollider.name == "boite_habitacle")) && vn >= 6f;   // (habitacle : toit qui tape le sol, tonneau -- mortel comme avant)
                var h = new Hit { At = Time.time, Normal = vn, Speed = Rb != null ? Rb.velocity.magnitude : 0f,
                                  Part = p.thisCollider != null ? p.thisCollider.name : "?", Other = c.collider.name,
                                  N = Car.InverseTransformDirection(p.normal), Pos = Car.InverseTransformPoint(p.point), Stay = stay, Hard = hard };
                ring[ringN++ % ring.Length] = h;
                if (hard) { HardAt = Time.time; HardLine = Line(h); }
                if (vn < 2f) return;
                if (Time.realtimeSinceStartup - minuteAt > 60f) { minuteAt = Time.realtimeSinceStartup; minuteN = 0; }
                if (minuteN++ >= 30) return;
                Log.Info("standalone : contact, " + Line(h) + ", voiture en " + Car.position.ToString("F0") + " cap " + Car.eulerAngles.y.ToString("F0"));
            }
            static string Line(Hit h)
            {
                return h.Part + (h.Stay ? " (contact qui dure)" : "") + " touche " + h.Other + " a " + (h.Speed * 3.6f).ToString("F0") + " km/h (" + (h.Normal * 3.6f).ToString("F0") + " km/h contre la surface), normale " + h.N.ToString("F2") + ", point " + h.Pos.ToString("F2") + (h.Hard ? ", OBSTACLE" : "");
            }
            public void Dump(string why)
            {
                int n = Mathf.Min(ringN, ring.Length);
                for (int k = 1; k <= n; k++)
                {
                    Hit h = ring[(ringN - k) % ring.Length];
                    Log.Info("standalone : " + why + ", contact il y a " + (Time.time - h.At).ToString("F2") + " s : " + Line(h));
                }
                if (n == 0) Log.Info("standalone : " + why + ", aucun contact de la caisse note");
            }
        }

        static bool Crash(VazCar.Built b, State st)
        {
            CrashSensor s = st.Sensor;
            if (s == null) { s = st.Sensor = b.Car.AddComponent<CrashSensor>(); s.Rb = b.Rb; s.Head = b.M.Head; }
            if (st.Contacts == null && b.Rb != null) { st.Contacts = b.Rb.gameObject.AddComponent<ContactLog>(); st.Contacts.Rb = b.Rb; st.Contacts.Car = b.Car.transform; }
            float h = s.MaxH * 3.6f, v = s.MaxV * 3.6f;
            s.MaxH = s.MaxV = 0f;
            if (b.Rb.isKinematic || Time.realtimeSinceStartup - enteredAt < 1f) return false;
            float lim = st.T.ChocMortel * Time.fixedDeltaTime / 0.02f;   // (meme force sur la tete : la vitesse perdue en un pas suit la duree du pas)
            FsmBool helmet = FsmVariables.GlobalVariables.FindFsmBool("PlayerHelmet");
            if (helmet != null && helmet.Value) lim *= 1.6f;
            if (st.DriverBelted) lim *= 1.6f;   // (ceinture bouclee, comme le casque)
            if (h > crashMaxH) crashMaxH = h; if (v > crashMaxV) crashMaxV = v;
            if (h < lim) return false;
            // (JD 09/11 18:57 : mort a la reception d'un saut, 105 km/h perdus sans rien heurter -- seul contact, le bas de caisse pose a plat sur
            // la route. Une perte de vitesse ne tue que si un vrai obstacle a ete touche dans les 0,15 s ; un frottement ou un accroc sur le sol
            // pris a plat ne tue plus. [Test] ChocObstacle=0 : comme avant, pour les essais)
            if (Config.GetInt("Test", "ChocObstacle", 1) != 0 && st.Contacts != null && Time.time - st.Contacts.HardAt > 0.15f)
            {
                if (Time.realtimeSinceStartup >= softLogAt)
                {
                    softLogAt = Time.realtimeSinceStartup + 2f;
                    Log.Info("standalone : choc sans obstacle, pas mortel : " + h.ToString("F0") + " km/h perdus a l'horizontale en un pas (" + v.ToString("F0") + " a la verticale, limite " + lim.ToString("F0") + ")");
                    st.Contacts.Dump("choc sans obstacle");
                }
                return false;
            }
            Log.Info("standalone : choc mortel, " + h.ToString("F0") + " km/h perdus a l'horizontale en un pas (" + v.ToString("F0") + " a la verticale, limite " + lim.ToString("F0") + ")");
            if (st.Contacts != null) st.Contacts.Dump("choc mortel");
            // (mort AVANT de sortir le joueur : le jeu lit la voiture conduite pour faire mourir ses passagers -- Respawn, @accident)
            Log.Info("standalone : " + Respawn.TestDie("Crash"));
            Exit(b, true);
            return true;
        }
        static float crashMaxH, crashMaxV, softLogAt;   // (plus grands chocs vus au volant : essais ; journal des chocs sans obstacle)

        // Releve de la liaison du pivot de tete des voitures du jeu (comment le jeu decide la mort au volant)
        static void HeadJointProbe()
        {
            foreach (ConfigurableJoint j in UnityEngine.Object.FindObjectsOfType<ConfigurableJoint>())
            {
                if (j.name != "DriverHeadPivot") continue;
                Rigidbody r = j.GetComponent<Rigidbody>();
                Log.Info("standalone : tete " + Recon.Path(j.transform) + " : masse " + (r != null ? r.mass.ToString("F1") : "?") + ", drag " + (r != null ? r.drag.ToString("F1") : "?")
                         + ", axes x/y/z " + j.xMotion + "/" + j.yMotion + "/" + j.zMotion + ", rotations " + j.angularXMotion + "/" + j.angularYMotion + "/" + j.angularZMotion
                         + ", limite " + j.linearLimit.limit.ToString("F3") + ", ressort x " + j.xDrive.positionSpring.ToString("F0") + " y " + j.yDrive.positionSpring.ToString("F0") + " z " + j.zDrive.positionSpring.ToString("F0")
                         + ", rupture " + j.breakForce.ToString("F0") + " / " + j.breakTorque.ToString("F0") + ", ancre " + j.anchor.ToString("F2") + ", relie a " + (j.connectedBody != null ? j.connectedBody.name : "monde"));
            }
        }

        // Se pencher au volant (JD 29/10 : « je ne peux pas me pencher en conducteur comme dans les autres voitures ») -- comme les
        // voitures du jeu (automate Strafe de DriverHeadPivot/CameraPivot, releve sur la Satsuma) : joueur range sous
        // DriverHeadPivot/CameraPivot/Pivot, CameraPivot porte les animations car_strafe_left1/2 et right1/2 du jeu (clips repris de
        // la voiture du jeu, rien de recopie), touches ReachLeft / ReachRight tenues (cInput) ; vers la portiere seulement si elle est
        // ouverte de plus de 20 degres (la Satsuma : porte absente, vitre baissee ou porte ouverte). Positions de repos de CameraPivot
        // et Pivot reprises de la voiture du jeu ; sens de chaque clip mesure sur nos objets (fin du clip : Pivot a gauche ou a droite).
        static Transform LeanRig(VazCar.Built b, State st, Transform head)
        {
            Transform srcHead = null;
            foreach (string pth in new[] { "SATSUMA(557kg, 248)/DriverHeadPivot", "RCO_RUSCKO12(270)/LOD/DriverHeadPivot", "SORBET(190-200psi)/Functions/PlayerTrigger/DriverHeadPivot", "CORRIS/Functions/DriverHeadPivot" }) { GameObject g = Game.FindAny(pth); if (g != null && (g.transform.Find("CameraPivot/Pivot") != null || g.transform.Find("CameraPivotPLR/Pivot") != null)) { srcHead = g.transform; break; } }   // (My Winter Car : CameraPivotPLR)
            Transform cp = new GameObject("CameraPivot").transform; cp.SetParent(head, false);
            Transform pv = new GameObject("Pivot").transform; pv.SetParent(cp, false);
            if (srcHead != null)
            {
                Transform scp = srcHead.Find("CameraPivot/Pivot") != null ? srcHead.Find("CameraPivot") : srcHead.Find("CameraPivotPLR"), spv = scp.Find("Pivot");
                Transform root = srcHead.root;
                head.localRotation = Quaternion.Inverse(root.rotation) * srcHead.rotation;
                cp.localPosition = scp.localPosition; cp.localRotation = scp.localRotation;
                pv.localPosition = spv.localPosition; pv.localRotation = spv.localRotation;
                Animation sa = scp.GetComponent<Animation>();
                if (sa != null)
                {
                    Animation an = cp.gameObject.AddComponent<Animation>(); an.playAutomatically = false; an.cullingType = AnimationCullingType.AlwaysAnimate;   // (aucun rendu sous le pivot : sinon jamais animee)
                    foreach (string c in new[] { "car_strafe_left1", "car_strafe_left2", "car_strafe_right1", "car_strafe_right2" }) { AnimationClip clip = sa.GetClip(c); if (clip != null) { an.AddClip(clip, c); an[c].wrapMode = c.EndsWith("1") ? WrapMode.ClampForever : WrapMode.Once; } }   // (penche tenu tant que la touche l'est)
                    st.Lean = an;
                }
            }
            st.RestCarFromPivot = b.Car.transform.worldToLocalMatrix * pv.localToWorldMatrix;
            // sens des clips : Pivot en fin de « 1 », repere de la voiture, puis retour au repos
            if (st.Lean != null && st.Lean.GetClip("car_strafe_left1") != null && st.Lean.GetClip("car_strafe_right1") != null)
            {
                Vector3 rest = b.Car.transform.InverseTransformPoint(pv.position), cpPos = cp.localPosition; Quaternion cpRot = cp.localRotation;
                float lx = LeanEnd(st.Lean, "car_strafe_left1", b.Car.transform, pv) - rest.x; cp.localPosition = cpPos; cp.localRotation = cpRot;
                float rx = LeanEnd(st.Lean, "car_strafe_right1", b.Car.transform, pv) - rest.x; cp.localPosition = cpPos; cp.localRotation = cpRot;
                float doorSide = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
                bool leftIsOut = Mathf.Sign(lx) == doorSide;
                st.LeanOut = leftIsOut ? "car_strafe_left1" : "car_strafe_right1"; st.LeanBackOut = leftIsOut ? "car_strafe_left2" : "car_strafe_right2";
                st.LeanIn = leftIsOut ? "car_strafe_right1" : "car_strafe_left1"; st.LeanBackIn = leftIsOut ? "car_strafe_right2" : "car_strafe_left2";
                Log.Info("standalone : se pencher, animations de " + (srcHead != null ? srcHead.root.name : "?") + " : left1 " + lx.ToString("F2") + " m, right1 " + rx.ToString("F2") + " m (x de la voiture) ; vers la portiere = " + st.LeanOut);
            }
            else Log.Warn("standalone : animations pour se pencher introuvables (voiture du jeu absente)");
            return pv;
        }
        static float LeanEnd(Animation an, string clip, Transform car, Transform pv)
        {
            AnimationState s = an[clip];
            s.enabled = true; s.weight = 1f; s.time = s.length; an.Sample(); s.enabled = false;
            return car.InverseTransformPoint(pv.position).x;
        }

        // Passager d'une voiture autonome, vers sa portiere : comme le conducteur (Lean), portiere ouverte de plus de 20 degres, ou vitre baissee ; places arriere : vitre fixe, jamais.
        // false : pas une voiture autonome.
        public static bool SeatSide(Transform car, Vector3 seat, out bool open)
        {
            open = false;
            VazCar.Built b = car != null ? Find(car.gameObject) : null;
            State st;
            if (b == null || !states.TryGetValue(b, out st)) return false;
            float doorSide = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
            int i = Mathf.Sign(seat.x) == doorSide ? 0 : 1;
            bool front = Mathf.Abs(seat.z - b.M.Head.z) < 0.45f;
            open = front && (st.DoorAngle[i] > 20f || (st.Glass[i] != null && st.WinPos[i] > 0.2f));
            return true;
        }
        public static string TestOpenSeat(Transform car, Vector3 seat)
        {
            VazCar.Built b = Find(car.gameObject);
            State st;
            if (b == null || !states.TryGetValue(b, out st)) return null;
            float doorSide = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
            int i = Mathf.Sign(seat.x) == doorSide ? 0 : 1;
            if (st.Doors[i] == null) return "pas de portiere " + i;
            if (!st.DoorOpen[i]) ToggleDoor(b, st, i);
            return "portiere " + i + " ouverte";
        }

        // 0 droit, 1 penche vers la portiere, 2 vers l'interieur ; touche ReachLeft / ReachRight tenue (gauche = cote conducteur ici)
        static int leanForce = -1;   // (essai : penche impose, les touches ne se simulent pas)
        static void Lean(VazCar.Built b, State st)
        {
            if (st.Lean == null || st.LeanOut == null) return;
            float doorSide = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
            bool outKey = Held(doorSide < 0f ? "ReachLeft" : "ReachRight"), inKey = Held(doorSide < 0f ? "ReachRight" : "ReachLeft");
            bool doorOpen = st.DoorAngle != null && st.DoorAngle[0] > 20f;
            int want = leanForce >= 0 ? leanForce : outKey && doorOpen ? 1 : inKey ? 2 : 0;
            if (want == st.LeanState) return;
            if (st.LeanState == 1) st.Lean.Play(st.LeanBackOut, PlayMode.StopAll);
            else if (st.LeanState == 2) st.Lean.Play(st.LeanBackIn, PlayMode.StopAll);
            if (want == 1) st.Lean.Play(st.LeanOut, PlayMode.StopAll);
            else if (want == 2) st.Lean.Play(st.LeanIn, PlayMode.StopAll);
            st.LeanState = want;
        }

        // Couche des colliders du joueur retiree du rayon des roues (pose par Wheel.Start, apres l'allumage)
        static FieldInfo wheelMaskField;
        static void WheelMask(VazCar.Built b, State st)
        {
            if (st.MaskDone || buildPlayerLayer < 0) return;
            if (wheelMaskField == null) wheelMaskField = typeof(Wheel).GetField("layerMask", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (wheelMaskField == null) { st.MaskDone = true; return; }
            int n = 0;
            foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>(true))
            {
                object v = wheelMaskField.GetValue(w);
                int m = v is LayerMask ? ((LayerMask)v).value : v is int ? (int)v : 0;
                if (m == 0) return;   // (Start pas encore passe)
                int nm = m & ~(1 << buildPlayerLayer);
                if (itemLayer >= 0) nm &= ~(1 << itemLayer); if (seatItemLayer >= 0) nm &= ~(1 << seatItemLayer);   // (coque pour les objets)
                if (nm != m) { if (v is LayerMask) wheelMaskField.SetValue(w, (LayerMask)nm); else wheelMaskField.SetValue(w, nm); n++; }
            }
            st.MaskDone = true;
            Log.Info("standalone : couche joueur retiree du rayon de " + n + " roue(s)");
        }

        // Joueur dans l'habitacle ou devant la portiere conducteur ouverte : PlayerInCar de l'automate Crouch (le jeu le rend petit,
        // camera basse -- ce que fait le PlayerTrigger autour de l'habitacle des voitures du jeu), rendu a la sortie de la zone.
        static VazCar.Built cabinCar;
        static void CabinPresence(VazCar.Built b, State st, Transform pl)
        {
            if (!st.HasCabin) return;
            if (Seats.Seated && Config.GetInt("Test", "CabinePassager", 1) != 0) return;   // (passager assis : la camera basse est tenue par Seats ; penche vers sa portiere, le joueur depassait l'habitacle et la camera remontait de 0,8 m)
            Vector3 lp = b.Car.transform.InverseTransformPoint(pl.position);
            Bounds c = st.Cabin, d = st.DriverDoor;
            bool inside = Mathf.Abs(lp.x) < c.max.x + 0.05f && lp.z > c.min.z && lp.z < c.max.z && lp.y > -0.8f && lp.y < 1.8f;
            bool atDoor = st.DoorOpen[0] && lp.x < -c.max.x && lp.x > -(st.HalfWidth + 0.45f) && lp.z > d.min.z && lp.z < d.max.z - 0.1f && lp.y > -0.8f && lp.y < 1.8f;
            Bounds p = st.DoorBox[1];
            if (!atDoor && p.size.y > 0.3f && st.DoorOpen[1]) atDoor = lp.x > c.max.x && lp.x < st.HalfWidth + 0.45f && lp.z > p.min.z && lp.z < p.max.z - 0.1f && lp.y > -0.8f && lp.y < 1.8f;
            bool want = inside || atDoor;
            if (want && cabinCar != b) { cabinCar = b; InCar(true); Log.Info("standalone : joueur dans l'habitacle" + (inside ? "" : " (devant la portiere ouverte)")); }
            else if (!want && cabinCar == b) { cabinCar = null; InCar(false); Log.Info("standalone : joueur sorti de l'habitacle"); }
        }

        // Zone d'entree en conduite (le « trigger » des voitures du jeu) : sur le siege conducteur, dans l'habitacle -- tenue.ini [Poste]
        // EntreeZ (centre, depart : le siege) et EntreeLongueur ; et, portiere ouverte, devant elle dehors (si l'on ne passe pas).
        // (JD 28/10 : la zone d'avant, dehors le long du flanc, etait mal centree par rapport au vrai siege)
        static bool InZone(VazCar.Built b, State st, Transform pl)
        {
            Vector3 lp = b.Car.transform.InverseTransformPoint(pl.position);
            // (siege mesure dans le modele -- JD 01/11 : « les zones pour s'asseoir par rapport aux assises » : l'assise du conducteur, 30 cm
            // autour de son milieu ; sinon tenue.ini [Poste] EntreeZ / EntreeLongueur)
            float zc = st.HasSeatRef[0] ? st.SeatRef[0].z + 0.25f : st.T.EntreeZ, zh = st.HasSeatRef[0] ? 0.32f : st.T.EntreeLongueur * 0.5f;
            float xc = st.HasSeatRef[0] ? st.SeatRef[0].x : b.M.Head.x, xh = st.HasSeatRef[0] ? 0.3f : 0.32f;
            bool alongSeat = Mathf.Abs(lp.z - zc) < zh && lp.y > -1.0f && lp.y < 2.0f;
            if (!alongSeat) return false;
            if (Mathf.Abs(lp.x - xc) < xh) return true;
            float side = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f, flank = st.HalfWidth > 0.3f ? st.HalfWidth : 0.85f;
            return st.DoorOpen != null && st.DoorOpen[0] && lp.x * side > flank - 0.12f && lp.x * side < flank + 1.1f;
        }

        static void Enter(VazCar.Built b, State st)
        {
            if (hoverCar != null) { ClearHover(); hoverCar = null; }   // (bouton vise a pied : le conducteur repart de rien)
            GameObject pl = Game.PlayerGo;
            float gy;
            footHeight = Ground(pl.transform.position + Vector3.up * 0.3f, pl.transform, b.Car.transform, out gy) ? pl.transform.position.y - gy : 1.0f;
            footHeight = Mathf.Clamp(footHeight, 0.2f, 2f);
            // (comme les voitures du jeu : la tete reste ou elle etait en appuyant sur la touche -- a 30 cm au plus du point de vue du
            // siege, JD 31/10 : levier hors d'atteinte depuis le point de vue fixe)
            Transform cam0 = PlayerCam();
            st.HasEyeHold = false;
            if (cam0 != null)
            {
                Vector3 e = b.Car.transform.InverseTransformPoint(cam0.position), dv = e - st.T.Yeux;
                if (dv.magnitude < 0.6f) { st.EyeHold = st.T.Yeux + Vector3.ClampMagnitude(dv, 0.30f); st.HasEyeHold = true; }
            }
            var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            Game.SetGlobalBool("GUIdrive", false); Game.SetGlobal("GUIinteraction", ""); prompted = null;
            Game.SetGlobalBool("PlayerStop", true);
            Game.SetGlobal("PlayerCurrentVehicle", VehicleName);
            InCar(true);
            pl.transform.SetParent(st.Pivot, true);
            pl.transform.localRotation = Quaternion.identity;
            pl.transform.localPosition = Vector3.zero;
            var d = (StandaloneDriver)b.Axis;
            d.Keys = true; d.Handbrake = 0f;
            driving = b; enteredAt = Time.realtimeSinceStartup;
            TrafficGate(b, true);
            if (st.Folded[0]) { st.Folded[0] = false; SendState(b, st); }   // (dossier conducteur releve en s'asseyant)
            GearHud(true, b);
            Log.Info("standalone : joueur au volant de " + b.Car.name + " (pied a " + footHeight.ToString("F2") + " m)");
        }

        // Circulation (JD 05/11 : aucune voiture sur la grande route, sauf le bus et le cousin) : le jeu allume TRAFFIC/VehiclesHighway ou
        // VehiclesDirtRoad quand un objet au tag Player passe une porte de TRAFFIC/TriggerManager. Au volant, c'est le <voiture>/TrafficTrigger
        // de la voiture du jeu (capsule, tag et couche Player, pas un declencheur), allume par son automate PlayerTrigger le temps de la
        // conduite ; nos voitures n'en avaient pas : la route restait eteinte. Le notre : meme forme que le sien (capsule de 2,5 m le long de la voiture, plus fine), sous le
        // plancher, dans la caisse. Les portes sont des plans sans epaisseur : une boule de 20 cm (556b11c1) les sautait ou ne voyait que la seconde, des 79 km/h a l'essai (50 pas/s),
        // qui eteint la route (JD 06/11). Un declencheur ne les reveille pas (essai du 05/11). Inertie et centre de gravite remis apres coup. [Test] TraficPorte=0 : sans.
        static readonly Dictionary<VazCar.Built, GameObject> trafficGates = new Dictionary<VazCar.Built, GameObject>();
        static void TrafficGate(VazCar.Built b, bool on)
        {
            if (Config.GetInt("Test", "TraficPorte", 1) == 0) return;
            GameObject g;
            if (!trafficGates.TryGetValue(b, out g) || g == null)
            {
                if (!on || b.Car == null) return;
                State st; states.TryGetValue(b, out st);
                int layer = LayerMask.NameToLayer("Player");
                g = new GameObject("TrafficTrigger");
                g.SetActive(false);
                g.tag = "Player";
                if (layer >= 0) g.layer = layer;
                g.transform.SetParent(b.Car.transform, false);
                Bounds bb; float zc = VazCar.ModelBounds(b.M, "carrosserie.p205", out bb) ? bb.center.z : 0f;
                g.transform.localPosition = new Vector3(0f, st != null && st.HasCabin ? st.Cabin.min.y - 0.05f : 0.45f, zc);
                var c = g.AddComponent<CapsuleCollider>();
                c.direction = 2; c.height = 2.5f; c.radius = 0.04f;
                trafficGates[b] = g;
                Log.Info("standalone : TrafficTrigger pose sur " + b.Car.name + " en " + g.transform.localPosition.ToString("F2") + " (couche " + LayerMask.LayerToName(g.layer) + ")");
            }
            if (g.activeSelf == on) return;
            Vector3 inertia = b.Rb.inertiaTensor, com = b.Rb.centerOfMass; Quaternion ir = b.Rb.inertiaTensorRotation;
            g.SetActive(on);
            b.Rb.inertiaTensor = inertia; b.Rb.inertiaTensorRotation = ir; b.Rb.centerOfMass = com;
        }

        // force : sortie d'urgence (voiture detruite, fin de partie) sans condition de vitesse
        static void Exit(VazCar.Built b, bool force)
        {
            GameObject pl = Game.PlayerGo;
            Transform ct = b.Car != null ? b.Car.transform : null;
            State xs; bool cabin = !force && ct != null && states.TryGetValue(b, out xs) && xs.HasCabin;
            if (states.TryGetValue(b, out xs) && xs.DriverBelted) { xs.DriverBelted = false; SendState(b, xs); }   // (on sort ceinture detachee)
            if (pl != null)
            {
                pl.transform.SetParent(null, true);
                // (descente normale : on reste a sa place, dans l'habitacle, petit comme dans les voitures du jeu -- JD 28/10 : « quand je
                // sors du mode conducteur, que ca me laisse dans la voiture, et non pas la teleportation a l'exterieur » ; sortie forcee
                // ou sans habitacle creux : a cote de la portiere, comme avant)
                if (cabin)
                {
                    // (bas de la capsule du joueur pose 3 cm au-dessus du plancher de l'habitacle : rallume dans le plancher, il en etait
                    // expulse et partait a 12 m -- essai du 28/10)
                    pl.transform.rotation = Quaternion.Euler(0f, ct.eulerAngles.y, 0f);
                    var cc0 = pl.GetComponent<CharacterController>();
                    State cs; states.TryGetValue(b, out cs);
                    if (cc0 != null && cs != null)
                    {
                        float bottom = pl.transform.position.y + (cc0.center.y - cc0.height * 0.5f) * pl.transform.lossyScale.y;
                        float floorW = ct.TransformPoint(new Vector3(0f, cs.Cabin.min.y, 0f)).y + 0.03f;
                        if (bottom < floorW) pl.transform.position += Vector3.up * (floorW - bottom);
                    }
                }
                else if (ct != null)
                {
                    float side = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
                    State es; float ez = states.TryGetValue(b, out es) ? es.T.EntreeZ : b.M.Head.z, ew = es != null && es.HalfWidth > 0.3f ? es.HalfWidth : 0.85f;
                    Vector3 at = ct.TransformPoint(new Vector3(side * (ew + 0.75f), 0.4f, ez));
                    float gy;
                    if (Ground(at + Vector3.up * 2f, pl.transform, ct, out gy)) at.y = gy + (footHeight > 0f ? footHeight : 1f);
                    pl.transform.position = at;
                    pl.transform.rotation = Quaternion.Euler(0f, ct.eulerAngles.y, 0f);
                }
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = true;
            }
            Game.SetGlobalBool("PlayerStop", false);
            Game.SetGlobal("PlayerCurrentVehicle", "");
            if (cabin) { InCar(true); cabinCar = b; } else { InCar(false); cabinCar = null; }
            GearHud(false, b);
            ClearHover();
            var d = b.Axis as StandaloneDriver;
            if (d != null) { d.Keys = false; d.Throttle = 0f; d.Brake = 0f; d.Steer = 0f; d.Clutch = 0f; d.Handbrake = 1f; }
            TrafficGate(b, false);
            driving = null; exitBlock = Time.realtimeSinceStartup + 0.6f;
            Log.Info("standalone : joueur descendu" + (force ? " (force)" : "") + (pl != null ? " en " + pl.transform.position.ToString("F1") : ""));
        }

        // Rapport en bas a droite (HUD du jeu) : GUI/Indicators/Gear allume au volant, texte GUIgear (R, N, 1-6) -- ce que font
        // l'automate PlayerTrigger et le GearIndicator des voitures du jeu (JD 28/10 : « pas d'indication de vitesse en bas a droite »)
        static GameObject gearGui; static int gearShown = -99;
        static void GearHud(bool on, VazCar.Built b)
        {
            if (gearGui == null) gearGui = Game.FindAny("GUI/Indicators/Gear");
            if (gearGui != null) gearGui.SetActive(on);
            gearShown = -99;
            if (on) GearText(b); else Game.SetGlobal("GUIgear", "");
        }
        static void GearText(VazCar.Built b)
        {
            Drivetrain dt = b.Dt;
            if (dt == null || dt.gearRatios == null || dt.gear == gearShown) return;
            gearShown = dt.gear;
            int n = Array.IndexOf(dt.gearRatios, 0f);
            Game.SetGlobal("GUIgear", dt.gear == n ? "N" : dt.gear < n ? "R" : (dt.gear - n).ToString());
        }

        static void InCar(bool on)
        {
            GameObject pl = Game.PlayerGo;
            PlayMakerFSM cr = pl != null ? Game.FsmOn(pl, "Crouch") : null;
            FsmBool v = cr != null ? cr.FsmVariables.FindFsmBool("PlayerInCar") : null;
            if (v != null) v.Value = on;
        }

        // Sol sous un point (premier collider plein qui n'est ni le joueur ni la voiture)
        static bool Ground(Vector3 from, Transform player, Transform car, out float y)
        {
            y = 0f; float best = float.MaxValue; bool ok = false;
            foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 6f))
            {
                if (h.collider == null || h.collider.isTrigger) continue;
                if (player != null && h.collider.transform.IsChildOf(player)) continue;
                if (car != null && h.collider.transform.IsChildOf(car)) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; ok = true; }
            }
            return ok;
        }

        // Apres les automates et la camera du joueur : yeux du joueur ramenes au point de vue (pivot de tete + EyeFromPivot du jeu,
        // comme la copie) ; volant visible tourne avec la direction.
        static void DriveLate(VazCar.Built b, State st)
        {
            var d = b.Axis as CarController;
            // (volant visible d'apres l'angle REEL des roues avant -- braquage selon la vitesse compris --, x tenue.ini [Direction]
            // Demultiplication (14 : comme une voiture de serie), et lisse a 720 degres de volant par seconde : JD 29/10, « l'animation
            // du volant doit suivre la rotation des roues pour etre coherente » -- avant, la touche : un tour complet a toute vitesse)
            if (b.SteerPivot != null && b.FrontWheels != null)
            {
                float wa = 0f; int nw = 0;
                foreach (Wheel w in b.FrontWheels) if (w != null) { wa += w.steering * w.maxSteeringAngle; nw++; }
                float want = nw > 0 ? wa / nw * st.T.Demultiplication : 0f;
                st.WheelVis = Mathf.MoveTowards(st.WheelVis, want, 720f * Time.deltaTime);
                b.SteerPivot.localRotation = Quaternion.AngleAxis(-st.WheelVis, b.SteerAxis);
            }
            else if (b.SteerPivot != null && b.FrontWheels == null && b.Car.GetComponent<Axles>() != null) b.FrontWheels = b.Car.GetComponent<Axles>().frontAxle.wheels;
            if (driving != b) return;
            GameObject pl = Game.PlayerGo;
            Transform cam = PlayerCam();
            if (pl == null || cam == null || pl.transform.parent != st.Pivot) return;
            // (yeux portes par le pivot, anime quand on se penche : point de vue au repos ramene dans le repere du pivot)
            Vector3 eye = st.Pivot.TransformPoint(st.RestCarFromPivot.inverse.MultiplyPoint3x4(st.HasEyeHold ? st.EyeHold : st.T.Yeux));
            Vector3 delta = eye - cam.position;
            if (delta.sqrMagnitude > 1e-6f && delta.sqrMagnitude < 9f) pl.transform.position += delta;
        }

        // ---------------------------------------------------------------- moteur
        public static bool EngineRunning(VazCar.Built b) { State st; return states.TryGetValue(b, out st) && st.EngineOn; }
        public static void SetEngine(VazCar.Built b, bool on)
        {
            State st;
            if (!states.TryGetValue(b, out st)) return;
            st.EngineOn = on; st.CrankT = -1f;
            if (on) b.Dt.StartEngine();
            SendState(b, st);
            Log.Info("standalone : moteur " + (on ? "demarre" : "coupe"));
        }

        static void Engine(VazCar.Built b, State st)
        {
            if (st.EngineOn || b.Rb.isKinematic) return;
            // (moteur coupe : Drivetrain.Start le demarre a la naissance, et le ralenti le relancerait -- regime tenu a zero)
            if (b.Dt.rpm > 1f)
            {
                if (engineVelo == null) engineVelo = typeof(Drivetrain).GetField("engineAngularVelo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (engineVelo != null) engineVelo.SetValue(b.Dt, 0f);
            }
            b.Dt.idlethrottle = 0f;
        }

        // ---------------------------------------------------------------- co-op : etat de la voiture (Vaz V_STD)
        // Moteur, phares, portieres : envoyes a tous par celui qui les change (et toutes les 3 s par le conducteur) ; chacun les
        // pose sur sa voiture du meme numero (Built.Number : MWCoop-VAZ-<n> partout). Le mouvement, le regime et le conducteur
        // passent par VehicleSync comme pour les autres voitures du F10.
        static void SendState(VazCar.Built b, State st)
        {
            st.NextState = Time.realtimeSinceStartup + 3f;
            if (!Net.Session.Active || b.Number < 1) return;
            int f = (st.EngineOn ? 1 : 0) | (st.LightsOn ? 2 : 0) | (st.DoorOpen != null && st.DoorOpen[0] ? 4 : 0) | (st.DoorOpen != null && st.DoorOpen[1] ? 8 : 0) | (st.HornOn ? 16 : 0)
                  | (st.KeyIn ? 32 : 0) | (st.KeyPos > 0 ? 64 : 0) | (st.HatchOpen ? 128 : 0);   // (cle mise, contact, hayon ouvert)
            // (octet de plus : warnings -- les anciennes versions lisent les deux premiers et ignorent le reste)
            // (octet de plus : warnings 1, essuie-glaces 2, ceinture du conducteur 4, dossiers rabattus 8 / 16, frein a main 32 : pour le son)
            // (puis le chauffage : marche 1, ventilateur x 2 (0-3) ; temperature et direction 0-255 -- StandaloneHeater)
            var w = new Net.NetWriter(Net.Msg.Race).U8(Vaz.V_STD).U8(b.Number).U8(f).U8((st.Hazard ? 1 : 0) | (st.WipersOn ? 2 : 0) | (st.DriverBelted ? 4 : 0) | (st.Folded[0] ? 8 : 0) | (st.Folded[1] ? 16 : 0) | ((b.Rb.isKinematic ? st.HandRemote : st.HandSent) ? 32 : 0));
            // (chauffage : toujours 3 octets, a zero sans chauffage ; puis 1 pleins phares, 2 / 4 clignotant ; puis reservoir et temperature, voiture de la quete)
            if (st.Heat != null) w.U8((st.Heat.On ? 1 : 0) | (st.Heat.FanStep << 1)).U8(Mathf.RoundToInt(st.Heat.TempPos * 255f)).U8(Mathf.RoundToInt(st.Heat.DirPos * 255f)); else w.U8(0).U8(0).U8(0);
            w.U8((st.HighBeam ? 1 : 0) | (st.Turn == 1 ? 2 : 0) | (st.Turn == 2 ? 4 : 0) | (st.RadioCh << 3));
            if (st.HasFuel) w.U8(Mathf.RoundToInt(Mathf.Clamp01(st.Fuel) * 255f)).U8(float.IsNaN(st.EngineTemp) ? 255 : Mathf.Clamp(Mathf.RoundToInt(st.EngineTemp + 40f), 0, 254));
            // (vitres de portiere : apres le reste, seulement les voitures qui en ont)
            if (st.Glass[0] != null || st.Glass[1] != null) w.U8(Mathf.RoundToInt(st.WinPos[0] * 255f)).U8(Mathf.RoundToInt(st.WinPos[1] * 255f));
            // (puis une action : frein a main serre (3) ou desserre (1) ici sur une copie -- celui qui mene la voiture l'applique a son levier ;
            // 0 : rien. JD 08/11, passagers et pietons aux commandes ; les anciennes versions s'arretent avant)
            w.U8(st.HandAsk < 0 ? 0 : st.HandAsk > 0 ? 3 : 1); st.HandAsk = -1;
            Net.Session.SendAll(w, true);
        }

        public static void OnState(Net.Peer from, Net.NetReader r)
        {
            int n = r.U8(), f = r.U8();
            byte[] extra = r.Rest();   // (warnings ; absent chez les anciennes versions)
            foreach (VazCar.Built b in cars)
            {
                State st;
                if (b.Number != n || !states.TryGetValue(b, out st)) continue;
                // (commande reglee ici il y a moins d'1 s -- passager, pieton ou conducteur : l'etat d'un autre parti avant ne la defait pas)
                bool mine = Time.realtimeSinceStartup - st.KnobLocalT < 1f;
                bool eng = (f & 1) != 0;
                if (mine) eng = st.EngineOn;
                if (eng && !st.EngineOn && !b.Rb.isKinematic) b.Dt.StartEngine();
                if (eng != st.EngineOn) Log.Info("standalone : n " + n + " moteur " + (eng ? "demarre" : "coupe") + " chez un autre joueur");
                st.EngineOn = eng; if (!mine) st.LightsOn = (f & 2) != 0;
                if (st.DoorOpen != null)
                    for (int k = 0; k < 2; k++)
                    {
                        // (portiere libre ici : ouverte ou fermee par un autre joueur -- lancee sur sa charniere, comme au clic)
                        bool o = (f & (4 << k)) != 0;
                        // (copie menee par un autre, portiere cliquee ici il y a moins d'1 s : l'etat periodique du conducteur parti avant
                        // le clic ne la rouvre / referme pas -- desync du 02/11, JD et GG ; [Test] PortiereCopieFermee=0 : comme avant)
                        if (b.Rb.isKinematic && o != st.DoorOpen[k] && Time.realtimeSinceStartup - st.DoorLocalT[k] < 1f && Config.GetInt("Test", "PortiereCopieFermee", 1) != 0) continue;
                        if (!b.Rb.isKinematic) { if (o && !st.DoorOpen[k]) st.DoorVel[k] = 260f; else if (!o && st.DoorOpen[k]) { st.DoorVel[k] = -420f; continue; } }
                        st.DoorOpen[k] = o;
                    }
                if (!st.HornMine) SetHorn(b, st, (f & 16) != 0, false);
                if (!mine) { st.KeyIn = (f & 32) != 0; st.KeyPos = (f & 64) != 0 ? 1 : 0; }
                st.HatchOpen = (f & 128) != 0;
                if (extra.Length > 0)
                {
                    if (!mine) { st.Hazard = (extra[0] & 1) != 0; st.WipersOn = (extra[0] & 2) != 0; }
                    if (driving != b) st.DriverBelted = (extra[0] & 4) != 0;
                    st.Folded[0] = (extra[0] & 8) != 0; st.Folded[1] = (extra[0] & 16) != 0;
                    if (b.Rb.isKinematic && driving != b && !mine)
                    {
                        bool hb = (extra[0] & 32) != 0;
                        if (st.HandRemoteSet && hb != st.HandRemote) Foley(b, hb ? "handbrake_on" : "handbrake_off", b.BrakePivot != null ? b.BrakePivot.position : b.Car.transform.position, 1f);
                        st.HandRemote = hb; st.HandRemoteSet = true;
                    }
                }
                if (extra.Length > 3 && st.Heat != null && !mine)
                {
                    st.Heat.On = (extra[1] & 1) != 0; st.Heat.FanStep = Mathf.Clamp((extra[1] >> 1) & 3, 0, FanSteps);
                    st.Heat.TempPos = extra[2] / 255f; st.Heat.DirPos = extra[3] / 255f;
                    HeaterParts(st);
                }
                if (extra.Length > 4 && !mine) { st.HighBeam = (extra[4] & 1) != 0; st.Turn = (extra[4] & 2) != 0 ? 1 : (extra[4] & 4) != 0 ? 2 : 0; st.RadioCh = (extra[4] >> 3) & 3; }
                if (extra.Length > 6 && st.HasFuel) { st.Fuel = extra[5] / 255f; if (extra[6] != 255) st.EngineTemp = extra[6] - 40f; }
                int wa = st.HasFuel ? 7 : 5;
                // (vitres : chez tous, conducteur compris -- il ignorait celles des autres, et son renvoi des 3 s refermait chez tous la vitre
                // baissee par un passager, JD 07/11 ; sauf celle tournee ici il y a moins d'1 s, l'etat d'un autre parti avant ne la remet pas)
                if (extra.Length > wa + 1 && (st.Glass[0] != null || st.Glass[1] != null))
                    for (int k = 0; k < 2; k++) if (Time.realtimeSinceStartup - st.WinLocalT[k] >= 1f) st.WinPos[k] = extra[wa + k] / 255f;
                // (action d'un autre : frein a main tire ou desserre sur sa copie -- applique ici si la voiture est physique ici, chez celui qui la mene)
                int aa = wa + (st.Glass[0] != null || st.Glass[1] != null ? 2 : 0);
                if (extra.Length > aa && (extra[aa] & 1) != 0 && !b.Rb.isKinematic)
                {
                    var sd = b.Axis as StandaloneDriver;
                    if (sd != null) { sd.HandLatch = (extra[aa] & 2) != 0 ? 1f : 0f; Log.Info("standalone : n " + n + " frein a main " + (sd.HandLatch > 0f ? "serre" : "desserre") + " par un autre joueur"); }
                }
                st.NextState = Time.realtimeSinceStartup + 3f;
            }
        }

        // ---------------------------------------------------------------- tableau de bord
        const int KIgnition = 0, KLights = 2, KHorn = 3, KWheelHub = 99, KCrank0 = 200, KHand = 300, KStalk = 400, KBelt = 500, KHeater = 600, KRadio = 700;
        static void SetHorn(VazCar.Built b, State st, bool on, bool send)
        {
            if (on == st.HornOn) return;
            st.HornOn = on;
            if (st.Horn != null) { if (on) st.Horn.Play(); else st.Horn.Stop(); }
            if (send) SendState(b, st);
        }
        // (klaxon : chacun ne coupe que celui qu'il a lance -- un passager ne coupe pas celui du conducteur a chaque image, ni l'inverse)
        static void HornOff(VazCar.Built b, State st) { if (!st.HornMine) return; st.HornMine = false; SetHorn(b, st, false, true); }
        // (a portee des commandes sans etre au volant, JD 08/11 : assis dans la voiture, camera dans l'habitacle, ou dehors a moins d'1 m du
        // cote d'une portiere ouverte ou d'une vitre a moitie baissee (ou sans portiere) -- comme on se penche dans la Satsuma ; portiere
        // fermee et vitre levee, ou devant, derriere, sur le toit : rien a travers la tole)
        static VazCar.Built hoverCar;
        // (passager : un bouton de la planche vise ici -- Seats ne prend alors pas le clic pour sa ceinture, la boucle est pres du frein a main)
        public static bool KnobAimed(Transform car) { return hovered >= 0 && hoverCar != null && hoverCar.Car != null && car != null && hoverCar.Car == car.gameObject; }
        static bool Reach(VazCar.Built b, State st)
        {
            if (Seats.SeatedCar == b.Car.transform) return true;
            Transform cam = PlayerCam();
            if (cam == null || !st.HasCabin) return false;
            Vector3 p = b.Car.transform.InverseTransformPoint(cam.position);
            Bounds cb = st.Cabin;
            if (cb.Contains(p)) return true;
            // (debout dehors : yeux au-dessus du toit, permis jusqu'a 80 cm ; de cote seulement, pas devant, derriere ni sur le toit)
            if ((cb.ClosestPoint(p) - p).sqrMagnitude > 1f || p.z < cb.min.z || p.z > cb.max.z || p.y > cb.max.y + 0.8f || Mathf.Abs(p.x - cb.center.x) < cb.extents.x) return false;
            int k = Mathf.Sign(p.x) == Mathf.Sign(b.M.Head.x) ? 0 : 1;   // (cote du conducteur : portiere 0)
            if (st.Doors == null || st.Doors[k] == null) return true;
            return st.DoorOpen[k] || st.DoorAngle[k] > 20f || (WinOn(b, st, k) && st.WinPos[k] >= 0.5f);
        }
        // (essais : bouton vise sans regarder, clic -- [Test] StandaloneCommandes)
        static int tKnob = -1; static bool tDown, tRight;
        static bool MDown() { return tDown || Input.GetMouseButtonDown(0); }
        static bool MRight() { return tRight || Input.GetMouseButtonDown(1); }
        static void Knobs(VazCar.Built b, State st)
        {
            float sc0 = Input.GetAxis("Mouse ScrollWheel");
            bool act = MDown() || MRight() || sc0 != 0f;
            KnobsIn(b, st, driving == b);
            if (act && hovered >= 0) st.KnobLocalT = Time.realtimeSinceStartup;   // (reglage d'ici : l'etat d'un autre parti avant ne le defait pas)
            tKnob = -1; tDown = false; tRight = false;
        }
        static void KnobsIn(VazCar.Built b, State st, bool driver)
        {
            Transform cam = PlayerCam();
            VazCar.Knob[] ks = KnobsOf(b.M);
            if (cam == null || ks == null) return;
            // chauffage (commandes de la console, visee serree : l'interrupteur est a 2,4 cm de celui des warnings) ; assis hors volant :
            // c'est Seats qui le regle (un seul clic compte)
            string ht = !driver && Seats.SeatedCar == b.Car.transform ? null : HeaterInput(b, st, cam);
            if (ht != null) { hovered = KHeater; CrankStop(b, st); HornOff(b, st); Game.SetGlobalBool("GUIuse", true); Game.SetGlobal("GUIinteraction", ht); return; }
            if (RadioInput(b, st, cam)) { hovered = KRadio; CrankStop(b, st); HornOff(b, st); return; }
            int hit = -1; float bestPerp = 0.035f;
            foreach (int i in new[] { KIgnition, KLights, KHorn, KWheelHub })
            {
                if (i != KWheelHub && i >= ks.Length) continue;
                if (i == KWheelHub && (b.SteerPivot == null || !driver)) continue;   // (le volant reste au conducteur)
                Vector3 v = (i == KWheelHub ? b.SteerPivot.position : i == KIgnition && st.Key != null ? st.Key.position : b.Car.transform.TransformPoint(ks[i].At)) - cam.position;
                float along = Vector3.Dot(v, cam.forward);
                if (along < 0.15f || along > 1.3f) continue;
                float perp = (v - cam.forward * along).magnitude;
                if (perp < (i == KWheelHub ? 0.07f : bestPerp) && (hit < 0 || perp < bestPerp)) { bestPerp = Mathf.Min(bestPerp, perp); hit = i; }
            }
            // manivelles des vitres (les deux, du siege conducteur) : molette, comme les voitures du jeu
            // (essais : qwAim, regard vers cette manivelle ; qwScroll, cran de molette -- QuestWindowTest)
            Vector3 aimFwd = qwAim >= 0 && st.Crank[qwAim] != null ? (st.Crank[qwAim].position - cam.position).normalized : cam.forward;
            for (int w = 0; w < 2; w++)
            {
                if (st.Crank[w] == null || !WinOn(b, st, w)) continue;
                Vector3 v = st.Crank[w].position - cam.position;
                float along = Vector3.Dot(v, aimFwd);
                if (along < 0.15f || along > 1.6f) continue;
                float perp = (v - aimFwd * along).magnitude;
                if (perp < 0.05f && (hit < 0 || perp < bestPerp)) { bestPerp = perp; hit = KCrank0 + w; }
            }
            // comodo des essuie-glaces : vise, clic = marche / arret
            if (st.Stalk != null)
            {
                Vector3 v = b.Body.TransformPoint(StalkTipLocal(st)) - cam.position;   // (bout du levier)
                float along = Vector3.Dot(v, cam.forward), perp = (v - cam.forward * along).magnitude;
                if (along > 0.15f && along < 1.3f && perp < 0.06f && (hit < 0 || perp < bestPerp)) { bestPerp = perp; hit = KStalk; }
            }
            else if (st.HasWiperBtn)
            {
                Vector3 v = b.Body.TransformPoint(st.WiperBtn) - cam.position;   // (interrupteur de la planche)
                float along = Vector3.Dot(v, cam.forward), perp = (v - cam.forward * along).magnitude;
                if (along > 0.15f && along < 1.3f && perp < 0.035f && (hit < 0 || perp < bestPerp)) { bestPerp = perp; hit = KStalk; }
            }
            if (tKnob >= 0 && tKnob < KHeater) hit = tKnob;   // (essais : bouton choisi sans viser ; radio : RadioInput)
            if ((hit == KWheelHub || hit == KBelt) && !driver) hit = -1;   // (volant et ceinture du conducteur : a lui)
            if (hit == KStalk)
            {
                hovered = hit; CrankStop(b, st); HornOff(b, st);
                Game.SetGlobalBool("GUIuse", true); Game.SetGlobal("GUIinteraction", st.WipersOn ? "WIPERS OFF" : "WIPERS ON");
                if (MDown()) { st.WipersOn = !st.WipersOn; SendState(b, st); Log.Info("standalone : essuie-glaces " + (st.WipersOn ? "en marche" : "arretes")); }
                return;
            }
            // ceinture du conducteur : viser la boucle (a la hanche, cote interieur), clic = bouclee / detachee
            if (driver && tKnob < 0 && st.BeltHang[0] != null)
            {
                Vector3 v = b.Body.TransformPoint(BuckleAt(st, 0, st.T.Yeux)) - cam.position;
                float along = Vector3.Dot(v, cam.forward), perp = (v - cam.forward * along).magnitude;
                if (along > 0.15f && along < 1.3f && perp < 0.07f && (hit < 0 || perp < bestPerp)) { bestPerp = perp; hit = KBelt; }
            }
            if (hit == KBelt)
            {
                hovered = hit; CrankStop(b, st); HornOff(b, st);
                Game.SetGlobalBool("GUIuse", true); Game.SetGlobal("GUIinteraction", st.DriverBelted ? "UNFASTEN SEATBELT" : "FASTEN SEATBELT");
                if (MDown())
                {
                    st.DriverBelted = !st.DriverBelted; SendState(b, st);
                    Seats.PlayFoley(st.DriverBelted ? "seatbelt_fasten" : "seatbelt_unfasten", b.Body.TransformPoint(BuckleAt(st, 0, st.T.Yeux)));
                    Log.Info("standalone : ceinture du conducteur " + (st.DriverBelted ? "bouclee" : "detachee"));
                }
                return;
            }
            // levier du frein a main (frein.p205) : vise, molette (vers l'avant : serre) ou clic (serre / desserre), comme les voitures du jeu
            var sdh = b.Axis as StandaloneDriver;
            if (tKnob < 0 && b.BrakePivot != null && sdh != null)
            {
                Vector3 g = b.BrakePivot.TransformPoint(b.M.BrakeGrip - b.M.BrakePivot), v = g - cam.position;
                float along = Vector3.Dot(v, cam.forward), perp = (v - cam.forward * along).magnitude;
                if (along > 0.15f && along < 1.3f && perp < 0.06f && (hit < 0 || perp < bestPerp)) { bestPerp = perp; hit = KHand; }
            }
            if (hit == KHand && sdh != null)
            {
                hovered = hit; CrankStop(b, st); HornOff(b, st);
                Game.SetGlobalBool("GUIuse", true); Game.SetGlobal("GUIinteraction", "HANDBRAKE");
                float sc = Input.GetAxis("Mouse ScrollWheel");
                // (JD 08/11 : « clique gauche il se serre, clique droit il se dessere », comme dans le jeu de base -- conducteur, passager,
                // a pied ; molette : vers l'avant serre, vers soi desserre)
                bool pull = MDown() || sc > 0f, release = MRight() || sc < 0f;
                // (copie menee par un autre : le levier d'ici ne freine rien -- serre ou desserre envoye a celui qui la mene, son levier suit)
                if (b.Rb.isKinematic && driving != b)
                {
                    bool now = st.HandRemoteSet ? st.HandRemote : sdh.HandLatch > 0.5f, want = pull ? true : release ? false : now;
                    if (want != now)
                    {
                        st.HandRemote = want; st.HandRemoteSet = true; st.HandAsk = want ? 1 : 0;
                        Foley(b, want ? "handbrake_on" : "handbrake_off", b.BrakePivot != null ? b.BrakePivot.position : b.Car.transform.position, 1f);
                        SendState(b, st); Log.Info("standalone : frein a main " + (want ? "serre" : "desserre") + " sur la copie, envoye");
                    }
                    return;
                }
                if (pull) sdh.HandLatch = 1f; else if (release) sdh.HandLatch = 0f;
                return;
            }
            if (hit >= KCrank0 && hit < KCrank0 + 2)
            {
                hovered = hit; CrankStop(b, st); HornOff(b, st);
                Game.SetGlobalBool("GUIuse", true); Game.SetGlobal("GUIinteraction", "WINDOW");
                // (molette comme les voitures du jeu : vers soi = vitre baissee ; un cran = 1/8 de la course)
                int w = hit - KCrank0; float sc = qwAim >= 0 ? qwScroll : Input.GetAxis("Mouse ScrollWheel");
                qwScroll = 0f; qwHit = w;
                if (sc != 0f) { st.WinPos[w] = Mathf.Clamp01(st.WinPos[w] - Mathf.Sign(sc) * 0.125f); st.WinLocalT[w] = Time.realtimeSinceStartup; SendState(b, st); }
                return;
            }
            if (hit < 0) { ClearHover(); CrankStop(b, st); HornOff(b, st); return; }
            hovered = hit;
            Game.SetGlobalBool("GUIuse", true);
            string ign = st.Key == null ? (st.EngineOn ? "ENGINE OFF" : "START ENGINE") : !st.KeyIn ? "INSERT KEY" : st.EngineOn ? "ENGINE OFF" : st.KeyPos == 0 ? "IGNITION" : "START ENGINE";
            Game.SetGlobal("GUIinteraction", hit == KIgnition ? ign : hit == KLights ? (st.LightsOn ? "LIGHTS OFF" : "LIGHTS ON") : hit == KHorn && st.ModelButtons ? "HAZARD LIGHTS" : "HORN");
            // (boutons du modele : le 4e est le bouton des warnings -- le klaxon reste au moyeu du volant)
            if (hit == KHorn && st.ModelButtons) { HornOff(b, st); if (MDown()) { st.Hazard = !st.Hazard; SendState(b, st); Log.Info("standalone : warnings " + (st.Hazard ? "allumes" : "eteints")); } return; }
            // klaxon : bouton orange ou moyeu du volant, tant que le clic est tenu (comme le bouton du jeu)
            if (hit == KHorn || hit == KWheelHub)
            {
                if (Input.GetMouseButton(0) || tDown) { st.HornMine = true; SetHorn(b, st, true, true); } else HornOff(b, st);
                return;
            }
            HornOff(b, st);
            if (hit == KLights) { if (MDown()) { st.LightsOn = !st.LightsOn; st.HighBeam = false; SendState(b, st); Log.Info("standalone : phares " + (st.LightsOn ? "allumes" : "eteints")); } return; }
            // cle (comme la Satsuma) : clic = mise ; clic = contact ; relacher puis clic tenu = demarreur ; moteur tournant, clic = cle
            // ramenee, moteur coupe ; clic droit moteur coupe = contact coupe, puis cle retiree
            if (st.Key != null)
            {
                if (!st.KeyIn) { if (MDown()) { st.KeyIn = true; st.KeyPos = 0; SendState(b, st); Log.Info("standalone : cle mise"); } return; }
                if (!st.EngineOn && Input.GetMouseButtonDown(1)) { CrankStop(b, st); if (st.KeyPos > 0) st.KeyPos = 0; else st.KeyIn = false; SendState(b, st); Log.Info("standalone : " + (st.KeyIn ? "contact coupe" : "cle retiree")); return; }
                if (st.EngineOn) { if (MDown()) { st.KeyPos = 0; SetEngine(b, false); } return; }
                if (st.KeyPos == 0) { if (MDown()) { st.KeyPos = 1; st.KeyWait = true; SendState(b, st); Log.Info("standalone : contact mis"); } return; }
                if (st.KeyWait) { if (Input.GetMouseButton(0)) return; st.KeyWait = false; }
            }
            // contact : clic moteur tournant = arret ; moteur coupe, clic tenu = demarreur, le moteur prend apres 0,9 s
            if (st.EngineOn) { if (MDown()) SetEngine(b, false); return; }
            if (Input.GetMouseButton(0))
            {
                if (st.CrankT < 0f) { st.CrankT = Time.time; if (b.Sound != null) b.Sound.ExternalStarter(true, false); }
                if (Time.time - st.CrankT > 0.9f && CanStart(st)) { SetEngine(b, true); if (b.Sound != null) b.Sound.ExternalStarter(false, true); }
            }
            else CrankStop(b, st);
        }
        static bool refilling;
        static void CrankStop(VazCar.Built b, State st) { if (st.KeyCrank) return; if (st.CrankT >= 0f) { st.CrankT = -1f; if (b.Sound != null) b.Sound.ExternalStarter(false, false); } }
        // cle : visible mise, tournee sur son axe (contact 40 degres, demarreur 70, retour au contact) ; moteur demarre d'ailleurs (autre
        // joueur, essais) : cle mise au contact
        // essuie-glaces : aller-retour en 1,3 s (angle = maxi x (1 - cos) / 2), rangement en fin de course a l'arret ; comodo baisse en marche
        // bout du comodo (repere de la caisse) : celui d'infos.txt (comodo_bout), tourne avec le levier ; sinon 10 cm sur le cote (Golf)
        static Vector3 StalkTipLocal(State st)
        {
            if (st.HasStalkTip) return st.Stalk.localPosition + st.Stalk.localRotation * st.StalkTip;
            return st.Stalk.localPosition + new Vector3(0.10f * Mathf.Sign(st.StalkAxis.z), 0f, 0f);
        }
        static void WiperAnim(VazCar.Built b, State st)
        {
            if (st.Wipers[0] == null && st.Wipers[1] == null && st.Wipers[2] == null && st.Stalk == null) return;
            float dt = Time.deltaTime, ph0 = st.WiperPhase;
            if (st.WipersOn || st.WiperPhase % 1f > 0.0005f)
            {
                float ph = st.WiperPhase + dt / 1.3f;
                if (!st.WipersOn && Mathf.Floor(ph) > Mathf.Floor(st.WiperPhase)) ph = Mathf.Floor(ph);   // (arret : balais ranges)
                st.WiperPhase = ph % 1000f;
            }
            Transform wp = st.Wipers[0] != null ? st.Wipers[0] : st.Wipers[1] != null ? st.Wipers[1] : st.Wipers[2];
            if (st.WipersOn != st.WipersSnd) { st.WipersSnd = st.WipersOn; Foley(b, "dash_button", wp != null ? wp.position : st.Stalk != null ? st.Stalk.position : b.Car.transform.position, 0.4f); }
            if (wp != null && st.WipersOn && (ph0 % 1f <= 0.0005f || Mathf.Floor(st.WiperPhase) != Mathf.Floor(ph0)) && Time.time - st.WiperSndT > 0.5f)
            {
                AudioClip wc = FoleyClip("wiper_loop");
                if (wc != null)
                {
                    if (st.WiperSrc == null) { st.WiperSrc = FoleySource(wp.parent, "son_essuie"); st.WiperSrc.transform.position = wp.position; st.WiperSrc.clip = wc; st.WiperSrc.pitch = wc.length / 1.3f; Log.Info("standalone : son wiper_loop" + (b.Rb.isKinematic ? " (copie)" : "") + " joue"); }
                    st.WiperSndT = Time.time; st.WiperSrc.Play();
                }
            }
            float k = (1f - Mathf.Cos(st.WiperPhase * 2f * Mathf.PI)) * 0.5f;
            for (int i = 0; i < 3; i++)
                if (st.Wipers[i] != null) st.Wipers[i].localRotation = Quaternion.AngleAxis(st.WiperRest[i] + st.WiperSign[i] * st.WiperMax[i] * k, st.WiperAxis[i]);
            if (st.Stalk != null)
            {
                st.StalkAngle = Mathf.MoveTowards(st.StalkAngle, st.WipersOn ? 14f : 0f, 90f * dt);
                st.Stalk.localRotation = Quaternion.AngleAxis(st.StalkAngle, st.StalkAxis);
            }
        }
        // ---------------------------------------------------------------- pluie sur les vitres (infos.txt pluie -- Golf, JD 31/10 : « que les
        // vitres recoivent la pluie comme sur les vraies voitures du jeu ») : composant windshield du jeu (celui de la Satsuma :
        // SATSUMA/RainScript) copie sur la voiture, ses reglages repris, nos vitres a sa place. Il simule les gouttes dans une texture
        // en trois bandes de coordonnees (pare-brise frontUV en bas, cotes sideUV, arriere rearUV) que la matiere de pluie du jeu
        // (CAR_WINDOWS_RAIN, shader Windshield/windshield) lit avec les UV des vitres : les UV de nos vitres (matieres « glass ») sont
        // refaites a plat dans leur bande -- pare-brise : u de la droite vers la gauche (balais au repos vers -u, comme ceux du jeu),
        // v vers le haut ; cotes : v vers le bas ; lunette : v vers le haut, comme les reperes NormalFront / Side / Rear de la Satsuma.
        // Pluie et vent : ceux de la Satsuma, recopies a chaque image ; balais : nos deux balais avant (wiper1 / wiper2, position 0..1).
        static readonly Dictionary<string, System.Reflection.FieldInfo> rainF = new Dictionary<string, System.Reflection.FieldInfo>();
        static System.Reflection.FieldInfo RF(Component c, string n)
        {
            System.Reflection.FieldInfo f;
            if (!rainF.TryGetValue(n, out f)) { f = c.GetType().GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); rainF[n] = f; }
            return f;
        }
        static void SetupRain(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            if (!inf.ContainsKey("pluie")) return;
            try
            {
                // (My Winter Car : celui de la SORBET, sinon de la CORRIS -- ref v.261008-01)
                GameObject sat = Game.FindAny("SATSUMA(557kg, 248)/RainScript") ?? Game.FindAny("SORBET(190-200psi)/Functions/RainScript") ?? Game.FindAny("CORRIS/LOD/Activator/RainScript");
                Component src = sat != null ? sat.GetComponent("windshield") : null;
                var srcR = src != null ? RF(src, "windowRenderers").GetValue(src) as MeshRenderer[] : null;
                if (src == null || srcR == null || srcR.Length == 0 || srcR[0] == null) { Log.Warn("standalone : pluie, composant windshield de la Satsuma introuvable"); return; }
                Transform ct = b.Car.transform;
                // vitres du modele et classe de chaque sommet (0 pare-brise, 1 cotes, 2 lunette) d'apres sa normale, repere de la voiture
                var rs = new List<MeshFilter>();
                foreach (MeshRenderer mr in b.Car.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material sm = mr.sharedMaterial;
                    if (sm == null || !sm.name.StartsWith("vaz_") || VazCar.KindOf(b.M, sm.name.Substring(4)) != "glass") continue;
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    // (vitres seulement : pas le verre des compteurs ni les petits verres de l'habitacle -- JD 31/10 : « pas l'effet de pluie sur
                    // les compteurs »)
                    // (et le verre d'une portiere dont la vitre est un fichier a part : la petite vitre fixe, autrefois dans le meme maillage qu'elle)
                    bool doorPane = false;
                    for (int w = 0; w < 2; w++)
                    {
                        if (st.Glass[w] == null || st.Doors == null || st.Doors[w] == null || !mr.transform.IsChildOf(st.Doors[w])) continue;
                        foreach (MeshRenderer gr in st.Glass[w].GetComponentsInChildren<MeshRenderer>(true)) if (gr.sharedMaterial != null && gr.sharedMaterial.name == sm.name) doorPane = true;
                    }
                    if (mf != null && mf.sharedMesh != null && (doorPane || Mathf.Max(mf.sharedMesh.bounds.size.x, mf.sharedMesh.bounds.size.y, mf.sharedMesh.bounds.size.z) >= 0.5f)) rs.Add(mf);
                }
                if (rs.Count == 0) { Log.Warn("standalone : pluie, pas de vitre (matiere glass) dans le modele"); return; }
                var lo = new Vector3[3]; var hi = new Vector3[3]; for (int k = 0; k < 3; k++) { lo[k] = Vector3.one * 1e9f; hi[k] = -Vector3.one * 1e9f; }
                Vector3 upF = new Vector3(0f, 0.77f, -0.64f);
                Func<Vector3, int> cls = n => n.z > 0.35f && Mathf.Abs(n.x) < 0.6f ? 0 : n.z < -0.35f && Mathf.Abs(n.x) < 0.6f ? 2 : 1;
                Func<Vector3, int, Vector3> key = (p, k) => k == 0 ? new Vector3(p.x, Vector3.Dot(p, upF), 0f) : k == 1 ? new Vector3(p.z, p.y, 0f) : new Vector3(p.x, p.y, 0f);
                var pos = new List<Vector3[]>(); var cl = new List<int[]>();
                foreach (MeshFilter mf in rs)
                {
                    Mesh m = mf.sharedMesh; Vector3[] v = m.vertices, n = m.normals;
                    var pc = new Vector3[v.Length]; var cc = new int[v.Length];
                    for (int i = 0; i < v.Length; i++)
                    {
                        pc[i] = ct.InverseTransformPoint(mf.transform.TransformPoint(v[i]));
                        cc[i] = cls(ct.InverseTransformDirection(mf.transform.TransformDirection(n.Length == v.Length ? n[i] : Vector3.up)));
                        Vector3 q = key(pc[i], cc[i]); lo[cc[i]] = Vector3.Min(lo[cc[i]], q); hi[cc[i]] = Vector3.Max(hi[cc[i]], q);
                    }
                    pos.Add(pc); cl.Add(cc);
                }
                Rect[] rect = { (Rect)RF(src, "frontUV").GetValue(src), (Rect)RF(src, "sideUV").GetValue(src), (Rect)RF(src, "rearUV").GetValue(src) };
                // (JD 31/10 : « trop claire » et, la pluie finie, « les textures des vitres sont buggees » : la matiere de pluie montre sa
                // texture de fond -- celle des vitres de la Satsuma, claire et rayee, sur nos UV -- des que l'eau seche (_Raining 0). Fond
                // = notre verre (texture et teinte de sa matiere), normale a plat ; eau plus sombre et moins opaque : comme la petite vitre)
                var rainMats = new Dictionary<Material, Material>();
                var flat = new Texture2D(2, 2, TextureFormat.ARGB32, false) { name = "standalone_normale_plate" };
                flat.SetPixels(new[] { new Color(0.5f, 0.5f, 1f, 0.5f), new Color(0.5f, 0.5f, 1f, 0.5f), new Color(0.5f, 0.5f, 1f, 0.5f), new Color(0.5f, 0.5f, 1f, 0.5f) }); flat.Apply(false);
                Func<Material, Material> rainFor = orig =>
                {
                    Material rm;
                    if (orig != null && rainMats.TryGetValue(orig, out rm)) return rm;
                    rm = new Material(srcR[0].sharedMaterial) { name = "standalone_vitre_pluie" };
                    if (orig != null)
                    {
                        rm.SetTexture("_MainTex", orig.mainTexture); rm.SetTextureScale("_MainTex", Vector2.one); rm.SetTextureOffset("_MainTex", Vector2.zero);
                        if (orig.HasProperty("_Color")) rm.SetColor("_Color", orig.color);
                    }
                    rm.SetTexture("_BumpMap", flat);
                    rm.SetFloat("_RainOpacity", rm.GetFloat("_RainOpacity") * RainOpacityK);
                    if (orig != null) rainMats[orig] = rm;
                    return rm;
                };
                // (lunette avec son essuie-glace -- JD 31/10 : « l'essuie-glace arriere n'essuie pas la pluie » : le composant du jeu n'a que
                // deux balais, ceux du pare-brise : la lunette a son propre composant, ou elle tient la bande du pare-brise et son balai est
                // le premier)
                var rear = new bool[rs.Count];
                for (int r = 0; r < rs.Count; r++) { rear[r] = st.Wipers[2] != null; foreach (int q in cl[r]) if (q != 2) { rear[r] = false; break; } }
                for (int r = 0; r < rs.Count; r++)
                {
                    Mesh m = UnityEngine.Object.Instantiate(rs[r].sharedMesh); m.name = rs[r].sharedMesh.name + "_pluie";
                    var uv = new Vector2[pos[r].Length];
                    for (int i = 0; i < uv.Length; i++)
                    {
                        int k = cl[r][i]; Vector3 q = key(pos[r][i], k), sz = hi[k] - lo[k];
                        float a = sz.x > 1e-4f ? (q.x - lo[k].x) / sz.x : 0.5f, c = sz.y > 1e-4f ? (q.y - lo[k].y) / sz.y : 0.5f;
                        if (k == 0 || rear[r]) a = 1f - a; else if (k == 1) c = 1f - c;   // (lunette dans la bande du pare-brise : meme sens que lui)
                        Rect rk = rear[r] ? rect[0] : rect[k];
                        uv[i] = new Vector2(rk.x + rk.width * (0.02f + 0.96f * a), rk.y + rk.height * (0.02f + 0.96f * c));
                    }
                    m.uv = uv; FaceNormals(m); rs[r].sharedMesh = m;
                    int[] nc = new int[3]; foreach (int q in cl[r]) nc[q]++;
                    Vector2 ul = uv.Length > 0 ? uv[0] : Vector2.zero, uh = ul; foreach (Vector2 q in uv) { ul = Vector2.Min(ul, q); uh = Vector2.Max(uh, q); }
                    Log.Info("standalone : pluie, " + Recon.Path(rs[r].transform) + " : " + uv.Length + " sommets (pare-brise " + nc[0] + ", cotes " + nc[1] + ", arriere " + nc[2] + (rear[r] ? ", composant de la lunette" : "") + "), uv " + ul.ToString("F2") + ".." + uh.ToString("F2") + ", bornes " + rs[r].sharedMesh.bounds.size.ToString("F2"));
                    MeshRenderer mr0 = rs[r].GetComponent<MeshRenderer>();
                    mr0.sharedMaterial = rainFor(mr0.sharedMaterial);
                }
                // composant a nous, reglages de celui de la Satsuma ; reperes (normales, vent) a la meme orientation dans la voiture
                Transform satRoot = sat.transform.root;
                Func<string, List<MeshFilter>, Component> make = (nm, list) =>
                {
                    var go = new GameObject(nm); go.SetActive(false);
                    go.transform.SetParent(ct, false);
                    Component cc = go.AddComponent(src.GetType());
                    foreach (System.Reflection.FieldInfo fi in src.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                    {
                        if (fi.Name == "windowRenderers" || fi.Name == "cachedWindowMaterials" || fi.Name.StartsWith("renderTexture") || fi.Name == "active" || fi.Name.StartsWith("previous")) continue;
                        object val = fi.GetValue(src);
                        if (val is Transform)
                        {
                            var tr = new GameObject(((Transform)val).name).transform; tr.SetParent(go.transform, false);
                            tr.rotation = ct.rotation * (Quaternion.Inverse(satRoot.rotation) * ((Transform)val).rotation);
                            val = tr;
                        }
                        fi.SetValue(cc, val);
                    }
                    RF(cc, "windowRenderers").SetValue(cc, list.ConvertAll(x => x.GetComponent<MeshRenderer>()).ToArray());
                    RF(cc, "Wet").SetValue(cc, new Color(RainWet, RainWet, RainWet, 1f));
                    return cc;
                };
                var front = new List<MeshFilter>(); var back = new List<MeshFilter>();
                for (int r = 0; r < rs.Count; r++) (rear[r] ? back : front).Add(rs[r]);
                // balais : moyeux et portee dans la bande du pare-brise (largeur = 1) ; portee bornee a la hauteur de la bande -- au-dela,
                // le balai essuyait la bande des vitres de cote (JD 31/10 : « on voit le passage de l'essuie-glace » sur les portieres)
                Func<int, int, float> reachOf = (w, k) =>
                {
                    Vector3 pv0 = ct.InverseTransformPoint(st.Wipers[w].position); float reach = 0f;
                    foreach (MeshFilter mf in st.Wipers[w].GetComponentsInChildren<MeshFilter>())
                        foreach (Vector3 v in mf.sharedMesh.vertices) reach = Mathf.Max(reach, (ct.InverseTransformPoint(mf.transform.TransformPoint(v)) - pv0).magnitude);
                    return Mathf.Clamp(reach / Mathf.Max(0.1f, hi[k].x - lo[k].x), 0.1f, k == 2 ? 1f : rect[0].height * 0.95f);   // (lunette : seule dans sa texture)
                };
                if (front.Count > 0)
                {
                    Component c2 = make("RainScript", front);
                    for (int w = 0; w < 2; w++)
                    {
                        if (st.Wipers[w] == null) { RF(c2, "wiper" + (w + 1) + "Length").SetValue(c2, 0f); continue; }
                        Vector3 pv = ct.InverseTransformPoint(st.Wipers[w].position);
                        RF(c2, "wiper" + (w + 1)).SetValue(c2, new Vector2(Mathf.Clamp01(1f - (pv.x - lo[0].x) / Mathf.Max(0.1f, hi[0].x - lo[0].x)), 0f));
                        RF(c2, "wiper" + (w + 1) + "Length").SetValue(c2, reachOf(w, 0));
                        if (st.WiperCourse[w])
                        {
                            // (course propre : du repos couche a droite, -90 dans le composant, jusqu'a -90 + course -- monobalai du BX 4TC)
                            float a0 = -90f, a1 = a0 + st.WiperMax[w];
                            RF(c2, "wiper" + (w + 1) + "MaxAngle").SetValue(c2, (a1 - a0) * 0.5f); RF(c2, "wiper" + (w + 1) + "AngleOffset").SetValue(c2, (a0 + a1) * 0.5f);
                        }
                    }
                    c2.gameObject.SetActive(true);
                    st.Rain = c2;
                }
                if (back.Count > 0)
                {
                    // (lunette : ses reperes de normale et de vent a la place de ceux du pare-brise ; balai du repos, vers -u, a 25 degres
                    // au-dela de la verticale -- sa course de 115 degres)
                    Component c3 = make("RainScriptLunette", back);
                    RF(c3, "frontWindowNormal").SetValue(c3, RF(c3, "rearWindowNormal").GetValue(c3));
                    RF(c3, "frontWind").SetValue(c3, RF(c3, "rearWind").GetValue(c3));
                    Vector3 pv = ct.InverseTransformPoint(st.Wipers[2].position);
                    // (JD 31/10, video : trace en miroir et plus courte que le balai -- u retourne comme au pare-brise, portee entiere ; angle du
                    // composant : 0 vers le haut de la vitre, +90 vers +u -- la gauche de la voiture apres le retournement ; du repos de la
                    // raclette, course vers la gauche)
                    RF(c3, "wiper1").SetValue(c3, new Vector2(Mathf.Clamp01(1f - (pv.x - lo[2].x) / Mathf.Max(0.1f, hi[2].x - lo[2].x)), 0f));
                    RF(c3, "wiper1Length").SetValue(c3, reachOf(2, 2));
                    float a0 = st.RearWiperA0, a1 = a0 + RearSweep;
                    RF(c3, "wiper1MaxAngle").SetValue(c3, (a1 - a0) * 0.5f); RF(c3, "wiper1AngleOffset").SetValue(c3, (a0 + a1) * 0.5f);
                    RF(c3, "wiper2Length").SetValue(c3, 0f);
                    c3.gameObject.SetActive(true);
                    st.Rain2 = c3;
                }
                st.RainSrc = src;
                Log.Info("standalone : pluie sur " + rs.Count + " vitre(s) (" + back.Count + " lunette a part), composant windshield de la Satsuma copie");
            }
            catch (Exception e) { Log.Warn("standalone : pluie : " + e.Message); }
        }
        // (eau sur nos vitres : teinte des gouttes et opacite, par rapport a la Satsuma -- [Test] PluieTeinte, PluieOpacite)
        // (balai de la lunette : course, degres -- [Test] EssuieArCourse)
        static float RearSweep { get { return Config.GetFloat("Test", "EssuieArCourse", 100f); } }
        static float RainWet { get { return Config.GetFloat("Test", "PluieTeinte", 0.30f); } }
        static float RainOpacityK { get { return Config.GetFloat("Test", "PluieOpacite", 0.38f); } }
        // (JD 31/10, video : vitres de gauche et lunette sous un voile blanc, celles de droite justes -- sur les faces vues de dehors, des
        // normales du modele tournees vers l'interieur (pieces en miroir, verre a deux faces) : le reflet du shader de pluie partait a
        // l'envers. Normales refaites d'apres le sens de chaque triangle, sommets separes)
        static void FaceNormals(Mesh m)
        {
            Vector3[] v = m.vertices, n0 = m.normals; Vector2[] u = m.uv; int[] t = m.triangles;
            var nv = new Vector3[t.Length]; var nn = new Vector3[t.Length]; var nu = new Vector2[t.Length]; var nt = new int[t.Length];
            int inv = 0;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]], n = Vector3.Cross(b - a, c - a).normalized;
                if (n0.Length == v.Length && Vector3.Dot(n0[t[i]] + n0[t[i + 1]] + n0[t[i + 2]], n) < 0f) inv++;
                for (int j = 0; j < 3; j++) { nv[i + j] = v[t[i + j]]; nn[i + j] = n; nu[i + j] = u.Length == v.Length ? u[t[i + j]] : Vector2.zero; nt[i + j] = i + j; }
            }
            Log.Info("standalone : pluie, " + m.name + " : " + inv + " triangle(s) sur " + t.Length / 3 + " avaient leur normale a l'envers");
            string nm = m.name; m.Clear(); m.name = nm;
            m.vertices = nv; m.normals = nn; m.uv = nu; m.triangles = nt; m.RecalculateBounds();
        }
        static void RainTick(VazCar.Built b, State st)
        {
            if (st.RainSrc == null) return;
            float forced = Config.GetFloat("Test", "StandalonePluie", -1f);   // ([Test] StandalonePluie=1 : pluie forcee, essais)
            float k = (1f - Mathf.Cos(st.WiperPhase * 2f * Mathf.PI)) * 0.5f;
            foreach (Component c in new[] { st.Rain, st.Rain2 })
            {
                if (c == null) continue;
                RF(c, "rain").SetValue(c, forced >= 0f ? forced : RF(st.RainSrc, "rain").GetValue(st.RainSrc));
                RF(c, "wind").SetValue(c, RF(st.RainSrc, "wind").GetValue(st.RainSrc));
                RF(c, "disabledRain").SetValue(c, forced >= 0f ? false : RF(st.RainSrc, "disabledRain").GetValue(st.RainSrc));
                RF(c, "wiper1Position").SetValue(c, k);
                RF(c, "wiper2Position").SetValue(c, k);
            }
        }
        // ---------------------------------------------------------------- ceintures (Golf, JD 31/10 : « fonctionnelles, avant et arriere,
        // la sangle comme celles de Menor ») : place bouclee -- sangle pendue du modele cachee, ceinture trois points dessinee de
        // l'ancrage du montant a l'epaule, en travers du buste jusqu'a la boucle (cote interieur, a la hanche), et de l'ancrage du
        // plancher sur les cuisses jusqu'a la boucle ; matiere de la sangle du modele (texture de Menor). Conducteur : clic sur la boucle
        // (Knobs), envoye avec l'etat ; passagers : Seats (clic sur la boucle de leur place), vus de tous avec leur place.
        // Points du buste d'apres les yeux de la place (repere de la voiture) ; s : vers l'exterieur.
        public static Vector3 BuckleOf(Vector3 e) { float s = e.x < 0f ? -1f : 1f; return e + new Vector3(-s * 0.21f, -0.60f, 0.02f); }
        // (place mesuree dans le modele -- infos.txt siege_<k> : milieu x, dessus de l'assise, avant du bas du dossier ; les yeux de la
        // Golf sont presque dans l'appui-tete, le buste d'apres eux passait dans le dossier)
        static Vector3 SeatPoint(State st, int k, Vector3 e, float side, float up, float fwd)
        {
            if (!st.HasSeatRef[k]) { float se = e.x < 0f ? -1f : 1f; return e + new Vector3(se * side, up - 0.62f, fwd); }
            Vector3 c = st.SeatRef[k]; float s = c.x < 0f ? -1f : 1f;
            return new Vector3(c.x + s * side, c.y + up, c.z + fwd);
        }
        static Vector3 BuckleAt(State st, int k, Vector3 e) { return st.HasSeatRef[k] ? SeatPoint(st, k, e, -0.20f, 0.06f, 0.07f) : BuckleOf(e); }
        static int BeltSeat(State st, Vector3 head)
        {
            float split = st.HasRear ? (st.BeltTop[0].z + st.BeltTop[2].z) * 0.5f : -99f;
            return (head.z > split ? 0 : 2) + (head.x < 0f ? 0 : 1);
        }
        static readonly List<Vector3> beltHeads = new List<Vector3>();
        static void BeltTick(VazCar.Built b, State st)
        {
            if (st.BeltHang[0] == null && st.BeltHang[1] == null && st.BeltHang[2] == null && st.BeltHang[3] == null) return;
            var want = new Vector3?[4];
            if (st.DriverBelted || testBelts) want[0] = st.T.Yeux;
            Seats.BeltedHeads(b.Car.transform, beltHeads);
            foreach (Vector3 h in beltHeads) { int k = BeltSeat(st, h); if (k > 0) want[k] = h; }
            if (testBelts)
            {
                Vector3 e = st.T.Yeux, p = new Vector3(-e.x, e.y, e.z);
                if (want[1] == null) want[1] = p;
                if (st.HasRear) { if (want[2] == null) want[2] = e + st.RearOffset; if (want[3] == null) want[3] = p + st.RearOffset; }
            }
            for (int k = 0; k < 4; k++)
            {
                if (st.BeltHang[k] == null) continue;
                bool on = want[k].HasValue;
                if (on && (st.BeltOn[k] == null || (st.BeltHead[k] - want[k].Value).sqrMagnitude > 0.0009f))
                {
                    if (st.BeltOn[k] != null) { UnityEngine.Object.Destroy(st.BeltOn[k].GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.Destroy(st.BeltOn[k]); }
                    st.BeltOn[k] = BeltRibbon(b, st, k, want[k].Value); st.BeltHead[k] = want[k].Value;
                }
                if (st.BeltOn[k] != null && st.BeltOn[k].activeSelf != on) st.BeltOn[k].SetActive(on);
                if (st.BeltHang[k].gameObject.activeSelf == on) st.BeltHang[k].gameObject.SetActive(!on);
            }
        }
        static bool testBelts;
        const float BeltWidth = 0.048f;
        static GameObject BeltRibbon(VazCar.Built b, State st, int k, Vector3 e)
        {
            if (st.BeltMat == null)
            {
                // (la sangle : le plus long morceau de la piece -- elle porte aussi la languette, plastique et chrome)
                float best = 0f;
                foreach (MeshRenderer r in st.BeltHang[k].GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter f = r.GetComponent<MeshFilter>();
                    if (r.sharedMaterial != null && f != null && f.sharedMesh != null && f.sharedMesh.bounds.size.magnitude > best) { best = f.sharedMesh.bounds.size.magnitude; st.BeltMat = r.sharedMaterial; }
                }
            }
            float s = e.x < 0f ? -1f : 1f;
            Vector3 sh = e + new Vector3(s * 0.16f, -0.21f, -0.07f), chest = e + new Vector3(-s * 0.02f, -0.37f, 0.07f), bu = BuckleAt(st, k, e);
            Vector3 hip = e + new Vector3(s * 0.21f, -0.58f, 0.03f), lap = e + new Vector3(0f, -0.55f, 0.14f);
            if (st.HasSeatRef[k])
            {
                // (epaule 50 cm au-dessus de l'assise, buste 13 cm devant le dossier, boucle a la hanche cote interieur, cuisses)
                sh = SeatPoint(st, k, e, 0.15f, 0.50f, -0.02f); chest = SeatPoint(st, k, e, -0.03f, 0.30f, 0.13f);
                hip = SeatPoint(st, k, e, 0.21f, 0.08f, 0.09f); lap = SeatPoint(st, k, e, 0f, 0.13f, 0.24f);
            }
            var vs = new List<Vector3>(); var ns = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            Strap(new[] { st.BeltTop[k], sh, chest, bu }, new Vector3(0f, 0.25f, 1f), vs, ns, uv, tr);
            Strap(new[] { st.BeltLow[k], hip, lap, bu }, new Vector3(0f, 1f, 0.6f), vs, ns, uv, tr);
            var mesh = new Mesh { name = "ceinture_bouclee_" + k };
            mesh.vertices = vs.ToArray(); mesh.normals = ns.ToArray(); mesh.uv = uv.ToArray(); mesh.triangles = tr.ToArray(); mesh.RecalculateBounds();
            var go = new GameObject("ceinture_bouclee_" + k); go.layer = st.BeltHang[k].gameObject.layer;
            foreach (Renderer r in st.BeltHang[k].GetComponentsInChildren<Renderer>(true)) { go.layer = r.gameObject.layer; break; }
            go.transform.SetParent(b.Body, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = st.BeltMat;
            Log.Info("standalone : ceinture " + k + " bouclee, yeux " + e.ToString("F2") + ", haut " + st.BeltTop[k].ToString("F2") + ", boucle " + bu.ToString("F2") + ", " + vs.Count + " sommets, matiere " + (st.BeltMat != null ? st.BeltMat.name + " [" + st.BeltMat.shader.name + "]" : "-") + ", couche " + LayerMask.LayerToName(go.layer) + ", monde " + go.transform.TransformPoint(chest).ToString("F1"));
            return go;
        }
        // sangle le long des points (courbe de Catmull-Rom), a plat face a 'face', deux faces ; u le long (une texture par largeur)
        static void Strap(Vector3[] pts, Vector3 face, List<Vector3> vs, List<Vector3> ns, List<Vector2> uv, List<int> tr)
        {
            var path = new List<Vector3>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Length - 1, i + 2)];
                for (int j = 0; j < 8; j++)
                {
                    float t = j / 8f, t2 = t * t, t3 = t2 * t;
                    path.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            path.Add(pts[pts.Length - 1]);
            face = face.normalized; float len = 0f;
            int b0 = vs.Count;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 tg = (path[Mathf.Min(path.Count - 1, i + 1)] - path[Mathf.Max(0, i - 1)]).normalized;
                Vector3 w = Vector3.Cross(tg, face); if (w.sqrMagnitude < 1e-6f) w = Vector3.Cross(tg, Vector3.up); w = w.normalized * (BeltWidth * 0.5f);
                Vector3 n = Vector3.Cross(w, tg).normalized; if (Vector3.Dot(n, face) < 0f) n = -n;
                if (i > 0) len += (path[i] - path[i - 1]).magnitude;
                float u = len / BeltWidth;
                vs.Add(path[i] - w); vs.Add(path[i] + w); ns.Add(n); ns.Add(n); uv.Add(new Vector2(u, 0f)); uv.Add(new Vector2(u, 1f));
                vs.Add(path[i] - w); vs.Add(path[i] + w); ns.Add(-n); ns.Add(-n); uv.Add(new Vector2(u, 0f)); uv.Add(new Vector2(u, 1f));
            }
            for (int i = 0; i < path.Count - 1; i++)
            {
                int a = b0 + i * 4, c = a + 4;
                tr.AddRange(new[] { a, c, a + 1, a + 1, c, c + 1 });
                tr.AddRange(new[] { a + 2, a + 3, c + 2, a + 3, c + 3, c + 2 });
            }
        }
        const float KeyOnDeg = 40f, KeyStartDeg = 70f;
        // vitres : position 0 (fermee) a 1 (baissee), manivelle 3 tours sur la course
        static void WindowAnim(VazCar.Built b, State st)
        {
            BindWindows(b, st);
            for (int w = 0; w < 2; w++)
            {
                if (!WinOn(b, st, w)) st.WinPos[w] = 0f;
                if (st.Glass[w] != null)
                {
                    VitreFiles.Glass v = st.WinV[w];
                    if (v == null) st.Glass[w].localPosition = st.GlassDown[w] * st.WinPos[w];
                    else if (st.WinPos[w] != st.WinShown[w])
                    {
                        float f = st.WinShown[w] = st.WinPos[w];
                        st.Glass[w].localPosition = Vector3.Lerp(v.HasUp ? v.UpP : Vector3.zero, v.DownP, f);
                        st.Glass[w].localRotation = Quaternion.Slerp(v.HasUp ? v.UpQ : Quaternion.identity, v.DownQ, f);
                        if (v.HasCut)
                        {
                            if (st.WinCuts[w] == null) st.WinCuts[w] = WindowCranks.MakeCuts(st.Glass[w], b.Car.transform);
                            foreach (WindowCranks.Cut c in st.WinCuts[w]) WindowCranks.ApplyPlane(c, st.Glass[w].parent, v, f);
                        }
                        else WindowCranks.RestoreCuts(st.WinCuts[w]);
                    }
                }
                if (st.Crank[w] != null) st.Crank[w].localRotation = Quaternion.AngleAxis(st.WinPos[w] * 1080f, st.CrankAxis[w]);
            }
        }
        // Vitres de la voiture de la quete (JD 06/11) : reglees dans l'outil des places (onglet Vitres, MWCoop\vitres\<jeu>\quete-s21.txt, relu
        // en partie). Sans fichier : fixes, manivelle inerte, comme avant.
        static bool WinOn(VazCar.Built b, State st, int w) { return st.WinV[w] != null || b.M == null || !b.M.QuestOnly; }
        static void BindWindows(VazCar.Built b, State st)
        {
            if (st.WinGen == VitreFiles.Generation) return;
            st.WinGen = VitreFiles.Generation;
            string key = b.M != null && b.M.QuestOnly ? "quete-s21" : Vaz.SeatKey(b.Car.name);
            for (int w = 0; w < 2; w++)
            {
                if (st.Glass[w] == null) continue;
                VitreFiles.Glass v = VitreFiles.Find(key, WindowCranks.PathIn(b.Car.transform, st.Glass[w]));
                if (st.WinV[w] != null && v == null)
                {
                    st.Glass[w].localPosition = Vector3.zero; st.Glass[w].localRotation = Quaternion.identity;
                    WindowCranks.RestoreCuts(st.WinCuts[w]);
                }
                st.WinV[w] = v;
                st.WinShown[w] = -1f;
                if (st.WinCuts[w] != null) foreach (WindowCranks.Cut c in st.WinCuts[w]) c.LastF = -1f;
                if (v != null) Log.Info("standalone : " + st.Glass[w].name + " reglee par le fichier de vitres");
            }
        }
        // (export pour l'outil des places : vitres de portiere, course de la manivelle)
        public static List<Transform> WindowsOf(VazCar.Built b, out float travel)
        {
            var list = new List<Transform>(); travel = 0f;
            State st;
            if (b == null || !states.TryGetValue(b, out st)) return list;
            for (int w = 0; w < 2; w++) if (st.Glass[w] != null) { list.Add(st.Glass[w]); travel = Mathf.Max(travel, st.GlassDown[w].magnitude); }
            return list;
        }
        static void KeyVisual(VazCar.Built b, State st)
        {
            if (st.Key == null) return;
            if (st.EngineOn && (!st.KeyIn || st.KeyPos == 0)) { st.KeyIn = true; st.KeyPos = 1; }
            if (st.Key.gameObject.activeSelf != st.KeyIn) st.Key.gameObject.SetActive(st.KeyIn);
            float want = st.CrankT >= 0f ? KeyStartDeg : st.KeyPos > 0 ? KeyOnDeg : 0f;
            st.KeyAngle = Mathf.MoveTowards(st.KeyAngle, want, 400f * Time.deltaTime);
            st.Key.localRotation = Quaternion.AngleAxis(st.KeyAngle, st.KeyAxis);
        }
        static void ClearHover() { if (hovered < 0) return; hovered = -1; Game.SetGlobalBool("GUIuse", false); Game.SetGlobal("GUIinteraction", ""); }

        // Feux du modele : avant allumes avec les phares, arriere faibles avec les phares et forts au freinage, recul en marche arriere.
        // Comme les phares des voitures du jeu (SATSUMA, FERNDALE, CORRIS : BeamsShort, BeamsLong) -- JD et Донской 06/11 : « les arbres s'allument comme
        // un sapin de Noel ». Masque du jeu : sans la couche Forest (26). Les arbres (Nature/Tree Soft Occlusion) sont eclaires par sommet, ou un
        // projecteur compte comme une lumiere ponctuelle : avec le masque par defaut, tout s'allumait autour de la voiture, derriere aussi.
        const int BeamMask = -88084481;
        static void HeadBeam(Light l, bool high)
        {
            l.type = LightType.Spot; l.cullingMask = BeamMask; l.renderMode = LightRenderMode.ForcePixel;
            l.spotAngle = 50f; l.range = high ? 200f : 50f; l.intensity = high ? 4f : 3f; l.color = new Color(1f, 0.902f, 0.581f);
        }
        // (faisceau des pleins phares, en plus du croisement : 0,5 degre sous l'horizontale, 30 degres, 250 m, force 6)
        static void HighBeamLight(Light l)
        {
            l.type = LightType.Spot; l.cullingMask = BeamMask; l.renderMode = LightRenderMode.ForcePixel;
            l.spotAngle = 30f; l.range = 250f; l.intensity = 6f; l.color = new Color(1f, 0.95f, 0.8f);
        }
        static void HighOn(State st, bool on) { if (st.HighHeads != null) foreach (Light h in st.HighHeads) if (h != null) h.enabled = on; }
        // ([Test] PharesPhoto=1 : memes vues, de jour puis de nuit, voiture face aux arbres puis dos a eux : phares eteints, ancien reglage,
        // nouveau, pleins phares, et lumieres copiees d'une voiture du jeu au meme endroit)
        static int headStep; static float headAt;
        public static void HeadlightTest(float e)
        {
            if (headStep < 0 || e < 12f) return;
            VazCar.Built b = null; State st = null;
            foreach (VazCar.Built x in cars) if (QueteGolf.IsQuestCar(x)) { b = x; states.TryGetValue(x, out st); }
            if (b == null || st == null || st.Heads == null) return;
            if (headStep == 0) { headStep = 1; HeadSeries(b, st, "jour"); headAt = e; SetNight(); }
            else if (headStep == 1 && e - headAt > 10f) { headStep = -1; HeadSeries(b, st, "nuit"); Log.Info("standalone : phares photographies"); }
        }
        static void SetNight()
        {
            PlayMakerFSM sun = Game.FindFsm(Game.Msc ? "MAP/SUN/Pivot/SUN" : "MAP/Sun/PivotSun/SUN", "Color");
            if (sun == null) { Log.Warn("standalone : phares, soleil introuvable"); return; }
            sun.FsmVariables.GetFsmInt("Time").Value = 24; sun.SendEvent("TIMESKIP");
        }
        static Light GameBeam(string prefix)
        {
            foreach (Light l in Resources.FindObjectsOfTypeAll<Light>())
                if (l.gameObject.hideFlags == HideFlags.None && l.type == LightType.Spot && l.name.StartsWith(prefix)) return l;
            return null;
        }
        static void HeadSeries(VazCar.Built b, State st, string when)
        {
            Transform c = b.Car.transform;
            Vector3 p0 = c.position; Quaternion r0 = c.rotation;
            Vector3 dir = c.forward; dir.y = 0f; dir.Normalize();
            Vector3 eye = p0 - dir * 7f + Vector3.up * 2.2f, look = p0 + dir * 22f + Vector3.up * 0.5f;
            Light[] mine = st.Heads;
            System.Action<string> both = n =>
            {
                QueteGolf.Shot("phares-" + when + "-" + n + "-face", eye, look);
                c.rotation = Quaternion.AngleAxis(180f, Vector3.up) * r0;
                QueteGolf.Shot("phares-" + when + "-" + n + "-dos", eye, look);
                c.rotation = r0;
            };
            System.Action<bool> own = on => { foreach (Light l in mine) if (l != null) l.enabled = on; };
            own(false); both("0-eteints");
            own(true);
            foreach (Light l in mine) if (l != null) { l.cullingMask = -1; l.renderMode = LightRenderMode.Auto; l.range = 45f; l.spotAngle = 70f; l.intensity = 2.2f; l.color = new Color(1f, 0.96f, 0.86f); }
            both("1-ancien");
            foreach (Light l in mine) if (l != null) HeadBeam(l, false);
            both("2-nouveau");
            HighOn(st, true);
            both("3-pleins");
            HighOn(st, false);
            own(false);
            foreach (string pre in new[] { "BeamShort", "BeamLong" })
            {
                Light src = GameBeam(pre);
                if (src == null) { Log.Warn("standalone : phares, pas de " + pre + " dans le jeu"); continue; }
                Log.Info("standalone : phares du jeu " + Recon.Path(src.transform) + " masque " + src.cullingMask + " portee " + src.range + " angle " + src.spotAngle + " force " + src.intensity);
                var clones = new List<GameObject>();
                foreach (Light l in mine)
                {
                    if (l == null) continue;
                    var g = new GameObject("essai_" + pre); g.transform.SetParent(l.transform.parent, false);
                    g.transform.position = l.transform.position; g.transform.rotation = l.transform.rotation;
                    Light k = g.AddComponent<Light>();
                    k.type = src.type; k.cullingMask = src.cullingMask; k.renderMode = src.renderMode; k.range = src.range; k.spotAngle = src.spotAngle; k.intensity = src.intensity; k.color = src.color; k.cookie = src.cookie; k.shadows = src.shadows;
                    clones.Add(g);
                }
                both("4-jeu-" + pre);
                foreach (GameObject g in clones) UnityEngine.Object.DestroyImmediate(g);
            }
            foreach (Light l in mine) if (l != null) { HeadBeam(l, false); l.enabled = st.LightsOn; }
            HighOn(st, st.LightsOn && st.HighBeam);
        }
        static void Lamps(VazCar.Built b, State st)
        {
            var d = b.Axis as CarController;
            bool f = st.LightsOn, br = d != null && d.brake > 0.1f, rv = b.Dt != null && b.Dt.gearRatios != null && b.Dt.gear >= 0 && b.Dt.gear < b.Dt.gearRatios.Length && b.Dt.gearRatios[b.Dt.gear] < 0f;
            bool flash = Time.time % 0.8f < 0.4f, blink = (st.Hazard || st.Turn != 0) && flash;   // (warnings et clignotant : 1,25 Hz)
            bool high = f && st.HighBeam;
            if (b.LampsInit && f == b.FrontOn && br == b.BrakeOn && rv == b.ReverseOn && blink == st.BlinkShown && high == st.HighShown && st.Turn == st.TurnShown) return;
            b.LampsInit = true; b.FrontOn = f; b.RearOn = f; b.BrakeOn = br; b.ReverseOn = rv; st.BlinkShown = blink; st.HighShown = high; st.TurnShown = st.Turn;
            // (pleins phares : plus loin et plus serres)
            if (st.Heads != null) foreach (Light l in st.Heads) if (l != null) { l.enabled = f; HeadBeam(l, false); }
            HighOn(st, high);
            foreach (KeyValuePair<MeshRenderer, string> kv in b.Lamps)
            {
                if (kv.Key == null) continue;
                string k = kv.Value; Material m = null;
                if (k.EndsWith("_ar")) { if (br) b.Lit.TryGetValue(k, out m); else if (f && !b.Dim.TryGetValue(k, out m)) b.Lit.TryGetValue(k, out m); }
                else if ((k.EndsWith("_av") && f) || (k.EndsWith("_re") && rv) || (k.EndsWith("_cg") && blink && (st.Hazard || st.Turn == 1)) || (k.EndsWith("_cd") && blink && (st.Hazard || st.Turn == 2))) b.Lit.TryGetValue(k, out m);
                Material basic; b.Mats.TryGetValue(k, out basic);
                kv.Key.sharedMaterial = m ?? basic;
            }
        }

        // ---------------------------------------------------------------- [Test] Autotest=standalonemonte (+ AddonStandalone=1,
        // StandaloneEssai=1) : sur la piste plate du banc, 20 cycles (voiture tournee de 0 / 120 / 240 degres) : joueur pose dans la zone
        // d'entree (texte du jeu verifie), monte (Enter : la touche du jeu ne se simule pas), yeux au point de vue, 2 s de gaz en 1re
        // puis frein, descend ; au sol a cote, CharacterController et PlayerStop rendus. Bilan : cycles reussis.
        static int mStep = -1, mCycle, mOk, mSide; static float mAt, mLog; static float mFly, mLeanAt;
        // (essai : voiture posee en p0, cap +z, joueur au volant ; entree remise a plus d'1 s pour que le choc compte tout de suite)
        static void PlaceSeated(VazCar.Built b, State st, GameObject pl, Vector3 p0) { PlaceSeated(b, st, pl, p0, Quaternion.identity); }
        static void PlaceSeated(VazCar.Built b, State st, GameObject pl, Vector3 p0, Quaternion r)
        {
            Transform ct = b.Car.transform;
            b.Rb.velocity = Vector3.zero; b.Rb.angularVelocity = Vector3.zero;
            ct.position = p0; ct.rotation = r; b.Rb.position = p0; b.Rb.rotation = r;
            st.DoorOpen[0] = st.DoorOpen[1] = false; st.DoorAngle[0] = st.DoorAngle[1] = 0f;
            if (st.Doors[0] != null) st.Doors[0].localRotation = Quaternion.identity; if (st.Doors[1] != null) st.Doors[1].localRotation = Quaternion.identity;
            if (driving != b)
            {
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                pl.transform.position = ct.TransformPoint(new Vector3(b.M.Head.x, 0.5f, st.T.EntreeZ));
                if (cc != null) cc.enabled = true;
                Enter(b, st);
            }
            enteredAt = Time.realtimeSinceStartup - 2f;
        } static string mWhy; static bool mShot, mOthersAway;
        // (point du choc mortel de JD dans la 405, 09/11 12:50 : voiture 3,3 s avant, point du choc ; essai [Test] StandalonePoint=1)
        // ([Test] PointA=x,z / PointZ=x,z : autre point -- 2e mort de JD, 09/11 18:57, reception d'un saut sur SS1, depart (1754.6, -936.1),
        // point (1563.2, -980.8))
        static Vector3 PtA { get { return PtCfg("PointA", new Vector3(882.5f, 0f, 1137.7f)); } }
        static Vector3 PtZ { get { return PtCfg("PointZ", new Vector3(968.5f, 0f, 1050.2f)); } }
        static int mWp; static List<Vector3> chemin; static string cheminTxt;
        static List<Vector3> Chemin()
        {
            string txt = Config.Get("Test", "PointChemin", "");
            if (chemin != null && txt == cheminTxt) return chemin;
            cheminTxt = txt; chemin = new List<Vector3>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string q in txt.Split('|'))
            {
                string[] w = q.Split(','); float x, z;
                if (w.Length == 2 && float.TryParse(w[0], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(w[1], System.Globalization.NumberStyles.Float, ci, out z)) chemin.Add(new Vector3(x, 0f, z));
            }
            return chemin;
        }
        static Vector3 PtCfg(string key, Vector3 def)
        {
            string[] w = Config.Get("Test", key, "").Split(',');
            float x, z; var ci = System.Globalization.CultureInfo.InvariantCulture;
            return w.Length == 2 && float.TryParse(w[0], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(w[1], System.Globalization.NumberStyles.Float, ci, out z) ? new Vector3(x, 0f, z) : def;
        }
        // (sol sous p, du haut : premier collider plein qui n'est ni la voiture ni le joueur)
        static bool Ground(Vector3 p, VazCar.Built b, out string name, out float y)
        {
            name = null; y = 0f; float best = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, 300f, p.z), Vector3.down, 600f))
            {
                if (h.collider == null || h.collider.isTrigger || h.distance >= best) continue;
                if (b.Car != null && h.collider.transform.IsChildOf(b.Car.transform)) continue;
                if (Game.PlayerGo != null && h.collider.transform.IsChildOf(Game.PlayerGo.transform)) continue;
                best = h.distance; name = h.collider.name; y = h.point.y;
            }
            return name != null;
        }
        public static void MountTest(float t)
        {
            Test(t);
            if (mStep == 99 || cars.Count == 0 || cars[0].Car == null) return;
            VazCar.Built b = cars[0];
            // (voiture d'essai, StandaloneEssai=1 : celle-la seulement -- sur My Summer Car, la voiture de la quete posee d'office est la
            // premiere de la liste ; essai du 09/11 fausse : la mauvaise voiture conduite, la voiture d'essai apparue devant elle. On attend
            // la voiture d'essai et les autres sont rangees loin de la piste.)
            if (Config.GetInt("Test", "StandaloneEssai", 0) != 0)
            {
                VazCar.Built tb = null; foreach (VazCar.Built c in cars) if (c.Car != null && c.Car.name.StartsWith(Prefix)) { tb = c; break; }
                if (tb == null) return;
                if (!mOthersAway)
                {
                    mOthersAway = true; int n = 0;
                    foreach (VazCar.Built c in cars)
                        if (c != tb && c.Car != null && c.Rb != null) { c.Rb.velocity = Vector3.zero; c.Rb.isKinematic = true; c.Car.transform.position = new Vector3(2500f + 20f * n++, VazTenue.TrackY + 50f, 2500f); }
                    if (n > 0) Log.Info("standalone : essai, " + n + " autre(s) voiture(s) rangee(s) loin de la piste");
                }
                b = tb;
            }
            State st; if (!states.TryGetValue(b, out st)) return;
            var d = (StandaloneDriver)b.Axis;
            GameObject pl = Game.PlayerGo; if (pl == null) return;
            float e = t - mAt;
            Transform ct = b.Car.transform;
            switch (mStep)
            {
                case -1:
                    VazTenue.MakeTrack(); mStep = Config.GetInt("Test", "StandalonePoint", 0) != 0 ? 30 : 0; mAt = t; mCycle = 0; mOk = 0;   // ([Test] StandalonePoint=1 : point reel)
                    if (!EngineRunning(b)) SetEngine(b, true);
                    break;
                case 0:   // voiture posee, joueur dans la zone
                {
                    Quaternion r = Quaternion.Euler(0f, (mCycle % 3) * 120f, 0f);
                    Vector3 p0 = new Vector3(mCycle * 30f, VazTenue.TrackY + 0.6f, 0f);
                    b.Rb.velocity = Vector3.zero; b.Rb.angularVelocity = Vector3.zero;
                    ct.position = p0; ct.rotation = r; b.Rb.position = p0; b.Rb.rotation = r;
                    var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                    pl.transform.position = ct.TransformPoint(new Vector3(-(st.HalfWidth + 0.45f), 0.6f, st.T.EntreeZ));
                    pl.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(ct.right, Vector3.up));   // (face a la portiere)
                    if (cc != null) cc.enabled = true;
                    // (portiere conducteur ouverte : on entre par elle)
                    st.DoorOpen[0] = true; st.DoorAngle[0] = 70f; st.Doors[0].localRotation = Quaternion.Euler(0f, 70f, 0f);
                    mWhy = ""; mStep = 1; mAt = t;
                    break;
                }
                case 1:
                    // 1er cycle : le joueur marche de dehors jusqu'au siege, a travers la portiere ouverte (3 s au plus) ; il doit passer dans
                    // l'habitacle (PlayerInCar : petit) et etre dans la zone d'entree ; les autres cycles : devant la portiere ouverte
                    if (mCycle == 0 && e < 3.2f)
                    {
                        var cm = pl.GetComponent<CharacterController>();
                        Vector3 goal = ct.TransformPoint(new Vector3(b.M.Head.x, 0f, st.T.EntreeZ)), d0 = Vector3.ProjectOnPlane(goal - pl.transform.position, Vector3.up);
                        if (cm != null && cm.enabled && d0.magnitude > 0.08f) cm.Move(d0.normalized * Mathf.Min(1.5f * Time.deltaTime, d0.magnitude) + Vector3.down * 0.2f * Time.deltaTime);
                        if (e > 0.9f && e < 1.0f && !mShot) { mShot = true; Autotest.CaptureSoon("standalone-portiere", 0f); }
                        break;
                    }
                    if (e < 1.5f) break;
                    if (mCycle == 0)
                    {
                        Vector3 lq = ct.InverseTransformPoint(pl.transform.position);
                        Log.Info("standalone : monte, joueur entre par la portiere : arrive a " + lq.ToString("F2") + (cabinCar == b ? ", dans l'habitacle (PlayerInCar)" : ", PAS dans l'habitacle"));
                        if (cabinCar != b) mWhy += " pas entre dans l'habitacle";
                    }
                    FsmString gi = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
                    if (!InZone(b, st, pl.transform)) mWhy += " hors zone (" + ct.InverseTransformPoint(pl.transform.position).ToString("F2") + ")";
                    else if (gi == null || gi.Value != "ENTER DRIVING MODE") mWhy += " texte '" + (gi != null ? gi.Value : "?") + "'";
                    Enter(b, st); mStep = 2; mAt = t;
                    break;
                case 2:
                    if (e < 1.5f) break;
                    if (mCycle == 1 && !Probe(b)) break;
                    {
                        FsmBool ps = FsmVariables.GlobalVariables.FindFsmBool("PlayerStop"); FsmString cv = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
                        Transform cam = PlayerCam();
                        float eyeErr = cam != null ? (cam.position - ct.TransformPoint(st.T.Yeux)).magnitude : 9f;
                        if (ps == null || !ps.Value) mWhy += " PlayerStop faux";
                        if (cv == null || cv.Value != VehicleName) mWhy += " vehicule '" + (cv != null ? cv.Value : "?") + "'";
                        if (pl.transform.parent != st.Pivot) mWhy += " pas sous le pivot";
                        if (eyeErr > 0.05f) mWhy += " yeux a " + eyeErr.ToString("F2") + " m";
                        if (mCycle == 2 && st.Lean != null && st.LeanIn != null) { leanForce = 2; Autotest.CaptureSoon("standalone-penche", 0.8f); mLeanAt = t; }
                        if (mCycle == 0)
                        {
                            // (cadrans de pres, aiguilles tenues a 100 km/h et 4 000 tr/min : sens et graduations verifiables)
                            st.LightsOn = true; gaugeFreeze = true; if (st.Speedo != null) st.Speedo.Set(100f); if (st.Tacho != null) st.Tacho.Set(4000f);   // (la 405 n'a pas de compteur de vitesse)
                            eyesSaved = st.T.Yeux; st.T.Yeux = new Vector3(-0.295f, 0.735f, 0.36f);
                            Autotest.CaptureSoon("standalone-cadrans", 0.4f);
                        }
                        FsmString gg = FsmVariables.GlobalVariables.FindFsmString("GUIgear");
                        if (gg == null || gg.Value != "N") mWhy += " rapport affiche '" + (gg != null ? gg.Value : "?") + "'";
                        if (gearGui == null || !gearGui.activeSelf) mWhy += " indicateur de rapport eteint";
                        d.Keys = false; d.Handbrake = 0f; d.Gear = 2; d.Throttle = 0.5f; d.Brake = 0f;
                        mStep = 3; mAt = t;
                    }
                    break;
                case 3:
                    if (e < 2f) break;
                    if (mCycle == 2 && mLeanAt > 0f)
                    {
                        Transform cm2 = PlayerCam();
                        Vector3 off = cm2 != null ? ct.InverseTransformPoint(cm2.position) - st.T.Yeux : Vector3.zero;
                        AnimationState ls = st.Lean[st.LeanIn];
                        Log.Info("standalone : monte, penche vers l'interieur : yeux deplaces de " + off.ToString("F2") + " (repere de la voiture) ; animation " + (st.Lean.isPlaying ? "en cours" : "arretee") + ", t " + (ls != null ? ls.time.ToString("F2") + "/" + ls.length.ToString("F2") + " " + ls.wrapMode : "?") + ", CameraPivot " + st.Lean.transform.localPosition.ToString("F2") + " " + st.Lean.transform.localEulerAngles.ToString("F0") + ", parent du joueur " + (pl.transform.parent != null ? pl.transform.parent.name : "-"));
                        if (off.magnitude < 0.08f) mWhy += " ne se penche pas";
                        leanForce = -1; mLeanAt = 0f;
                    }
                    if (mCycle == 0) { st.T.Yeux = eyesSaved; gaugeFreeze = false; Autotest.CaptureSoon("standalone-compteurs", 0f); Log.Info("standalone : monte, compteurs a " + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + " km/h, " + b.Dt.rpm.ToString("F0") + " tr/min"); }
                    d.Throttle = 0f; d.Brake = 1f; d.Gear = 1; mStep = 4; mAt = t;
                    if (b.Rb.velocity.magnitude < 1f) mWhy += " n'a pas roule";
                    if (pl.transform.parent != st.Pivot) mWhy += " ejecte en roulant";
                    break;
                case 4:
                    if (e < 2.5f && b.Rb.velocity.magnitude > 0.3f) break;
                    if (b.Rb.velocity.magnitude >= 0.5f) { mWhy += " pas arretee"; b.Rb.velocity = Vector3.zero; }
                    d.Brake = 0f; d.Gear = -1;
                    Exit(b, false); mStep = 5; mAt = t;
                    break;
                case 5:   // descendu : assis dans l'habitacle, petit
                    if (e < 1.0f) break;
                    {
                        FsmBool ps = FsmVariables.GlobalVariables.FindFsmBool("PlayerStop");
                        var cc = pl.GetComponent<CharacterController>();
                        Vector3 lp = ct.InverseTransformPoint(pl.transform.position);
                        if (pl.transform.parent != null) mWhy += " encore range";
                        if (ps != null && ps.Value) mWhy += " PlayerStop vrai";
                        if (cc != null && !cc.enabled) mWhy += " CharacterController coupe";
                        if (cabinCar != b || Mathf.Abs(lp.x) > st.Cabin.max.x + 0.05f) mWhy += " pas reste dans l'habitacle (" + lp.ToString("F2") + ")";
                        // sortie a pied : un cycle sur deux par la portiere passager (ouverte), sinon par celle du conducteur
                        mSide = mCycle % 2 == 1 ? 1 : 0;
                        if (mSide == 1) { st.DoorOpen[1] = true; st.DoorAngle[1] = 70f; st.Doors[1].localRotation = Quaternion.Euler(0f, -70f, 0f); }
                        mStep = 6; mAt = t;
                    }
                    break;
                case 6:   // marche vers dehors, 3 s au plus
                    if (e < 3f)
                    {
                        var cm = pl.GetComponent<CharacterController>();
                        Bounds db = st.DoorBox[mSide];
                        Vector3 goal = ct.TransformPoint(new Vector3((mSide == 0 ? -1f : 1f) * (st.HalfWidth + 1.0f), 0f, db.center.z - 0.15f));
                        Vector3 d0 = Vector3.ProjectOnPlane(goal - pl.transform.position, Vector3.up);
                        if (cm != null && cm.enabled && d0.magnitude > 0.08f) cm.Move(d0.normalized * Mathf.Min(1.5f * Time.deltaTime, d0.magnitude) + Vector3.down * 0.2f * Time.deltaTime);
                        break;
                    }
                    {
                        Vector3 lp = ct.InverseTransformPoint(pl.transform.position);
                        if (Mathf.Abs(lp.x) < st.HalfWidth + 0.1f) mWhy += " pas sorti par la portiere " + (mSide == 0 ? "conducteur" : "passager") + " (" + lp.ToString("F2") + ")";
                        if (cabinCar == b) mWhy += " PlayerInCar encore vrai dehors";
                        if (pl.transform.position.y < VazTenue.TrackY - 0.2f || pl.transform.position.y > VazTenue.TrackY + 3f) mWhy += " hauteur " + (pl.transform.position.y - VazTenue.TrackY).ToString("F2");
                        if (mSide == 1) { st.DoorOpen[1] = false; st.DoorAngle[1] = 0f; st.Doors[1].localRotation = Quaternion.identity; }
                        bool ok = mWhy.Length == 0;
                        if (ok) mOk++;
                        Log.Info("standalone : monte " + (mCycle + 1) + "/20 (cap " + ((mCycle % 3) * 120) + ", sortie " + (mSide == 0 ? "conducteur" : "passager") + ") " + (ok ? "OK" : "ECHEC :" + mWhy) + ", joueur dehors a " + lp.ToString("F2") + " de la voiture");
                        mCycle++;
                        if (mCycle >= Config.GetInt("Test", "StandaloneCycles", 20)) { Log.Info("standalone : monte fini, " + mOk + "/" + mCycle + " reussis"); mStep = Config.GetInt("Test", "StandaloneChoc", 1) != 0 ? 10 : 99; mAt = t; }
                        else { mStep = 0; mAt = t; }
                    }
                    break;
                case 10:   // bosses et saut a 100 km/h, au volant : pas de mort attendue
                {
                    HeadJointProbe();
                    int tl = LayerMask.NameToLayer("Terrain");
                    Vector3 p0 = new Vector3(-200f, VazTenue.TrackY + 0.6f, -400f);
                    // (bosses arrondies : cylindres couches en travers, 15 cm hors du sol ; tremplin : 7 m a 12 degres)
                    for (int k = 0; k < 6; k++)
                    {
                        var bump = GameObject.CreatePrimitive(PrimitiveType.Cylinder); bump.name = "MWCoop-BosseEssai";
                        if (tl >= 0) bump.layer = tl;
                        bump.transform.position = new Vector3(p0.x, VazTenue.TrackY - 0.20f, p0.z + 40f + k * 6f);
                        bump.transform.rotation = Quaternion.Euler(0f, 0f, 90f); bump.transform.localScale = new Vector3(0.7f, 6f, 0.7f);
                    }
                    var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.name = "MWCoop-TremplinEssai";
                    if (tl >= 0) ramp.layer = tl;
                    ramp.transform.rotation = Quaternion.Euler(-12f, 0f, 0f); ramp.transform.localScale = new Vector3(10f, 0.4f, 7f);
                    ramp.transform.position = new Vector3(p0.x, VazTenue.TrackY + 0.53f, p0.z + 95f);
                    PlaceSeated(b, st, pl, p0);
                    d.Keys = false; d.Handbrake = 0f; d.Gear = 6; d.Throttle = 1f; d.Brake = 0f;
                    b.Rb.velocity = ct.forward * 27.8f; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>()) w.angularVelocity = 27.8f / Mathf.Max(0.1f, w.radius);
                    if (st.Sensor != null) st.Sensor.Reset();
                    crashMaxH = crashMaxV = 0f; mFly = 0f;
                    mStep = 11; mAt = t;
                    Log.Info("standalone : choc, bosses (6 x 15 cm) et tremplin a 100 km/h");
                    break;
                }
                case 11:
                    if (b.Car.transform.position.y > VazTenue.TrackY + 1.6f) mFly = Mathf.Max(mFly, b.Car.transform.position.y - VazTenue.TrackY);
                    if (driving != b) { Log.Info("standalone : choc, ECHEC : mort sur les bosses ou le saut"); mStep = 12; mAt = t; break; }
                    if (e < 7f) break;
                    Log.Info("standalone : choc, bosses et saut OK, pas de mort (vitesse " + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + " km/h, saut jusqu'a " + mFly.ToString("F1") + " m, plus grand choc " + crashMaxH.ToString("F0") + " km/h a l'horizontale / " + crashMaxV.ToString("F0") + " a la verticale)");
                    mStep = 12; mAt = t;
                    break;
                // ([Test] StandalonePoint=1, [Directives] 09/11 : au point reel du choc mortel de JD dans la 405, 12:50:13 -- a 12:50:09.8 la
                // voiture en (882.5, -1.6, 1137.7), au choc (127 km/h perdus en un pas) en (968.5, -0.5, 1050.2), 3,3 s plus tard : ~135 km/h,
                // cap ~135 degres, speciale de My Summer Car. Releve d'abord le sol sur la corde (collider touche et hauteur tous les 2 m),
                // puis pose la voiture au depart, lancee a [Test] PointVitesse km/h (135), et la mene : vise le milieu de la route (collider
                // "Road") 14 m devant, la branche la plus proche du point du choc ; gaz pour tenir la vitesse. Fin 25 m apres le point.)
                case 30:
                {
                    if (driving == b) { d.Throttle = 0f; d.Brake = 1f; }
                    if (e < 3f) break;
                    Vector3 dir = (PtZ - PtA).normalized; float len = (PtZ - PtA).magnitude + 25f, lastY = float.NaN;
                    var sb = new System.Text.StringBuilder();
                    for (float s = 0f; s <= len; s += 2f)
                    {
                        string nm; float y;
                        if (!Ground(PtA + dir * s, b, out nm, out y)) { nm = "rien"; y = float.NaN; }
                        sb.Append(" ").Append(s.ToString("F0")).Append(":").Append(nm).Append(" ").Append(y.ToString("F2"));
                        if (!float.IsNaN(lastY) && !float.IsNaN(y)) sb.Append(" (").Append(((y - lastY) * 50f).ToString("F0")).Append("%)");
                        lastY = y;
                        if (sb.Length > 500) { Log.Info("standalone : point, sol" + sb); sb.Length = 0; }
                    }
                    if (sb.Length > 0) Log.Info("standalone : point, sol" + sb);
                    string n0; float y0, y1, y2;
                    Ground(PtA, b, out n0, out y0); Ground(PtA - dir * 2f, b, out n0, out y1); Ground(PtA + dir * 2f, b, out n0, out y2);
                    Vector3 p0 = new Vector3(PtA.x, y0 + 0.6f, PtA.z);
                    Quaternion r = Quaternion.LookRotation(dir * 4f + Vector3.up * (y2 - y1), Vector3.up);
                    PlaceSeated(b, st, pl, p0, r);
                    if (Config.GetInt("Test", "PointCeinture", 0) != 0) st.DriverBelted = true;   // (ceinture bouclee : limite x1,6, comme JD le 09/11 18:57)
                    float v = Config.GetFloat("Test", "PointVitesse", 135f) / 3.6f;
                    d.Keys = false; d.Handbrake = 0f; d.Gear = 6; d.Throttle = 1f; d.Brake = 0f; d.Steer = 0f;
                    b.Rb.velocity = ct.forward * v; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>()) w.angularVelocity = v / Mathf.Max(0.1f, w.radius);
                    if (st.Sensor != null) st.Sensor.Reset();
                    if (b.Rb.GetComponent<CarHits.Watch>() == null) b.Rb.gameObject.AddComponent<CarHits.Watch>();
                    crashMaxH = crashMaxV = 0f; mFly = 0f; mLog = 0f;
                    mStep = 31; mAt = t; mWp = 0;
                    Log.Info("standalone : point, " + (Config.GetInt("Test", "PointCorde", 0) != 0 ? "ligne droite de JD, " : "") + "depart en " + p0.ToString("F1") + " cap " + r.eulerAngles.y.ToString("F0") + " a " + (v * 3.6f).ToString("F0") + " km/h, caisse " + (Config.GetInt("Test", "CaisseRampe", 1) != 0 ? "a patins" : "plate (avant)"));
                    break;
                }
                case 31:
                {
                    Vector3 pos = new Vector3(ct.position.x, 0f, ct.position.z), dir = (PtZ - PtA).normalized;
                    float along = Vector3.Dot(pos - PtZ, dir), toZ = Vector3.Distance(pos, PtZ);
                    string tail = " (plus grand choc a la tete " + crashMaxH.ToString("F0") + " km/h a l'horizontale / " + crashMaxV.ToString("F0") + " a la verticale)";
                    if (driving != b) { Log.Info("standalone : point, ECHEC : mort en " + ct.position.ToString("F1") + ", " + toZ.ToString("F0") + " m du point du choc de JD" + tail); Log.Info("standalone : point fini"); mStep = 99; break; }
                    float spd = b.Rb.velocity.magnitude;
                    d.Throttle = spd * 3.6f < Config.GetFloat("Test", "PointVitesse", 135f) ? 1f : 0f; d.Brake = 0f;
                    Vector3 fw = Vector3.ProjectOnPlane(ct.forward, Vector3.up).normalized, rt = Vector3.Cross(Vector3.up, fw), look = pos + fw * 14f;
                    Vector3 refDir = along < -14f ? (PtZ - pos).normalized : fw;
                    bool found = false; Vector3 aim = Vector3.zero; float bestAng = float.MaxValue; int run = int.MinValue;
                    // ([Test] PointCorde=1 : la ligne droite de JD, sans suivre la route -- 123 m en 3,3 s, sa vitesse moyenne sur la corde
                    // est celle de son choc : il est alle presque tout droit, et la route tourne vers 98 m)
                    bool corde = Config.GetInt("Test", "PointCorde", 0) != 0;
                    if (corde) { found = true; aim = along < -5f ? PtZ : pos + dir * 20f; }
                    else if (Chemin().Count > 1)
                    {
                        // ([Test] PointChemin=x,z|x,z|... : points du trace de la speciale, suivis dans l'ordre -- la route tourne, la corde n'y est pas)
                        List<Vector3> wp = Chemin();
                        while (mWp < wp.Count - 1 && (Vector3.Distance(pos, wp[mWp]) < 14f || Vector3.Dot(wp[mWp] - pos, fw) < 0f)) mWp++;
                        found = true; aim = wp[mWp];
                        if (mWp == wp.Count - 1 && Vector3.Dot(wp[mWp] - pos, fw) < 0f) aim = pos + fw * 20f;
                    }
                    else for (int i = -10; i <= 11; i++)
                    {
                        string nm; float gy;
                        bool road = i <= 10 && Ground(look + rt * i, b, out nm, out gy) && nm.ToLower().Contains("road");
                        if (road && run == int.MinValue) run = i;
                        else if (!road && run != int.MinValue)
                        {
                            Vector3 c = look + rt * ((run + i - 1) * 0.5f); float ang = Vector3.Angle(c - pos, refDir);
                            if (ang < bestAng) { bestAng = ang; aim = c; found = true; }
                            run = int.MinValue;
                        }
                    }
                    Vector3 to = found ? aim - pos : PtZ - pos;
                    d.Steer = Mathf.Clamp(Vector3.Angle(fw, to) * Mathf.Sign(Vector3.Dot(rt, to)) / 10f, -1f, 1f);
                    if (t - mLog > 0.5f)
                    {
                        mLog = t;
                        Log.Info("standalone : point, " + e.ToString("F1") + " s en " + ct.position.ToString("F1") + " a " + (spd * 3.6f).ToString("F0") + " km/h, cap " + ct.eulerAngles.y.ToString("F0") + ", volant " + d.Steer.ToString("F2") + (found ? "" : ", PAS de route devant") + ", " + toZ.ToString("F0") + " m du point du choc");
                    }
                    if (along > 25f || e > 9f) { Log.Info("standalone : point, " + (along > 25f ? "OK, passe le point du choc de JD sans mort" : "pas arrive au point du choc en 9 s (" + toZ.ToString("F0") + " m)") + ", vitesse " + (spd * 3.6f).ToString("F0") + " km/h" + tail); Log.Info("standalone : point fini"); mStep = 99; }
                    break;
                }
                case 12:   // mur a 60 km/h, de face : mort attendue
                {
                    if (driving == b) { d.Throttle = 0f; d.Brake = 1f; }
                    if (e < 1f) break;
                    Vector3 p0 = new Vector3(200f, VazTenue.TrackY + 0.6f, -400f);
                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "MWCoop-MurEssai";
                    int tl = LayerMask.NameToLayer("Terrain"); if (tl >= 0) wall.layer = tl;   // (la caisse ne heurte pas la couche Default)
                    wall.transform.position = p0 + new Vector3(0f, 1f, 18f); wall.transform.localScale = new Vector3(20f, 3f, 1f);
                    PlaceSeated(b, st, pl, p0);
                    d.Keys = false; d.Handbrake = 0f; d.Gear = 4; d.Throttle = 0.3f; d.Brake = 0f;
                    b.Rb.velocity = ct.forward * 16.7f; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>()) w.angularVelocity = 16.7f / Mathf.Max(0.1f, w.radius);
                    if (st.Sensor != null) st.Sensor.Reset();
                    crashMaxH = crashMaxV = 0f;
                    mStep = 13; mAt = t;
                    Log.Info("standalone : choc, mur a 18 m, 60 km/h");
                    break;
                }
                case 13:
                    if (driving != b) { Log.Info("standalone : choc fini, mur : conducteur mort"); mStep = 99; break; }
                    if (e > 6f) { Log.Info("standalone : choc fini, mur : PAS de mort (plus grand choc " + crashMaxH.ToString("F0") + " km/h, vitesse " + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + ")"); mStep = 99; }
                    break;
            }
        }

        // ---------------------------------------------------------------- [Test] Autotest=standalonecoop (2 joueurs, MSC, les deux avec
        // AddonStandalone=1 et TestReapparition=1) : piste plate chez les deux ; l'hote prend la R5 standalone (n 3) au F10, monte,
        // demarre, phares ; l'invite s'assoit a la place du passager ; l'hote roule 5 s ; l'invite releve sa copie (standalone ?
        // cinematique ? vitesse, moteur, phares recus, toujours assis -- l'invite charge ~20 s apres l'hote : l'hote roule a 78 s) ; puis
        // choc contre un mur a 60 km/h : morts et reapparition
        // chez les deux (plantage RemapPPtrTransfer a surveiller, JD 29/10).
        static int cStep; static float cAt, cLog; static bool cPhotos;
        static void Photo(VazCar.Built b, string name, Vector3 local) { Photo(b, name, local, new Vector3(0f, 0.6f, 0f)); }
        static void Photo(VazCar.Built b, string name, Vector3 local, Vector3 target, float fov = 45f, float near = 0.05f)
        {
            var go = new GameObject("standalone_photo");
            try
            {
                var cam = go.AddComponent<Camera>(); cam.enabled = false;
                Transform pc = PlayerCam(); Camera main = pc != null ? pc.GetComponent<Camera>() : null;
                if (main != null) cam.CopyFrom(main);
                cam.fieldOfView = fov; cam.nearClipPlane = near; cam.farClipPlane = 500f;
                go.transform.position = b.Car.transform.TransformPoint(local);
                go.transform.LookAt(b.Car.transform.TransformPoint(target));
                var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                string png = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "photo-" + name + ".png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(png));
                System.IO.File.WriteAllBytes(png, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
                Log.Info("standalone : photo " + png);
            }
            catch (Exception e) { Log.Warn("standalone : photo " + name + " : " + e.Message); }
            UnityEngine.Object.Destroy(go);
        }
        static bool cFrein, cSeatShots, cMirrorShots, cBeltDone, cPlanche, cPhotoCond; static int cHeat; static float cHeatLog;

        // [Test] StandaloneRapports=1 (avec Autotest=standalonecoop, JD 08/11 : « syncronisé les rapport afficher sur les tableaux de bords
        // des voitures addons ») : l'hote au volant a l'arret, des que l'invite est assis (95 s au plus), engage R, N, 1..n toutes les
        // 3 s (embrayage enfonce, freins serres), puis roule comme avant. Chaque joueur note chaque rapport de sa boite et ce que montre sa
        // planche (l'invite : aussi le rapport recu) ; photo de la planche de ses yeux en 3e.
        // [Test] StandaloneCommandes=1 (avec Autotest=standalonecoop, JD 08/11 : « que l'ont puisse faire toutes les actions dans une voiture
        // ... meme si ont est pas assis en conducteur ou passager ... sauf le volant et les pedales ») : l'hote au volant a l'arret (moteur,
        // phares) note chaque changement de l'etat de sa voiture ; l'invite, assis passager, actionne toutes les 3 s un bouton (bouton choisi
        // sans viser, clic simule -- tKnob, tDown) : phares, warnings ou klaxon, radio, frein a main serre puis desserre, moyeu du volant
        // (refuse), essuie-glaces, cle (moteur coupe) ; puis il descend, se poste dehors cote passager, portiere fermee : phares (refuse) ;
        // portiere ouverte : phares (accepte).
        static int cmdStep = -1; static float cmdAt; static string cmdSeen = ""; static float cmdY0 = -9999f, cmdYMax, cmdDoorMax;
        static string CmdState(VazCar.Built b, State st)
        {
            var d = b.Axis as StandaloneDriver;
            return "moteur " + (st.EngineOn ? 1 : 0) + ", phares " + (st.LightsOn ? 1 : 0) + ", warnings " + (st.Hazard ? 1 : 0) + ", klaxon " + (st.HornOn ? 1 : 0) + ", radio " + st.RadioCh
                   + ", essuie " + (st.WipersOn ? 1 : 0) + ", cle " + (st.KeyIn ? 1 : 0) + "/" + st.KeyPos + ", frein a main " + (b.Rb.isKinematic ? (st.HandRemote ? "1 (recu)" : "0 (recu)") : d != null ? d.HandLatch.ToString("F0") : "?")
                   + ", portiere passager " + (st.DoorOpen != null && st.DoorOpen.Length > 1 && st.DoorOpen[1] ? "ouverte" : "fermee");
        }
        static void CommandsTest(VazCar.Built b, State st, bool host, float t, GameObject pl)
        {
            if (host)
            {
                if (cStep < 3 || driving != b) return;
                string s = CmdState(b, st);
                if (s != cmdSeen) { cmdSeen = s; Log.Info("standalone : commandes, hote voit : " + s); }
                return;
            }
            if (cStep < 3 || pl == null) return;
            if (cmdStep < 0) { cmdStep = 0; cmdAt = t + 5f; Log.Info("standalone : commandes, invite assis " + (Seats.SeatedCar == b.Car.transform) + " : " + CmdState(b, st)); return; }
            // ([Test] PortiereDansJoueur=1, JD 09/11 : invite poste DANS la course de la portiere passager qui s'ouvre -- hauteur du joueur et
            // angle de la portiere suivis a chaque image ; avant la garde : envoye a ~100 m sur My Summer Car)
            if (cmdStep == 11 && cmdY0 > -9000f) { cmdYMax = Mathf.Max(cmdYMax, pl.transform.position.y - cmdY0); if (st.DoorAngle != null && st.DoorAngle.Length > 1) cmdDoorMax = Mathf.Max(cmdDoorMax, st.DoorAngle[1]); }
            if (t < cmdAt) return;
            // (resultat du pas d'avant, puis le suivant)
            if (cmdStep > 0)
            {
                Log.Info("standalone : commandes, invite apres le pas " + cmdStep + " (a portee " + Reach(b, st) + ", bouton " + (tKnob >= 0 ? "PAS PRIS" : "pris") + ") : " + CmdState(b, st));
                if (cmdStep == 11 && cmdY0 > -9000f)
                {
                    Log.Info("standalone : commandes, portiere dans le joueur : hauteur du joueur au plus +" + cmdYMax.ToString("F2") + " m, portiere ouverte au plus de " + cmdDoorMax.ToString("F0") + " degres"
                             + (cmdYMax > 1.0f ? " -- ECHEC : joueur projete" : " -- OK, pas projete"));
                    cmdY0 = -9999f;
                }
                // (levier du frein a main vu de la place passager, serre puis desserre -- leviers de la M3 et de la Fiad, JD 08/11)
                if ((cmdStep == 4 || cmdStep == 5) && b.BrakePivot != null && PlayerCam() != null)
                {
                    Log.Info("standalone : commandes, levier du frein a main " + b.BrakePivot.localEulerAngles.ToString("F0"));
                    Photo(b, cmdStep == 4 ? "frein-serre" : "frein-desserre", b.Car.transform.InverseTransformPoint(PlayerCam().position), b.M.BrakeGrip, 40f);
                }
                tKnob = -1; tDown = false; tRight = false;
            }
            cmdAt = t + 3f; cmdStep++;
            string what;
            switch (cmdStep)
            {
                case 1: tKnob = KLights; what = "phares"; break;
                case 2: tKnob = KHorn; what = st.ModelButtons ? "warnings" : "klaxon"; break;
                case 3: tKnob = KRadio; what = st.HasRadio ? "radio" : "radio (pas de poste)"; break;
                case 4: tKnob = KHand; what = "frein a main, clic gauche (serre)"; break;
                case 5: tKnob = KHand; tRight = true; what = "frein a main, clic droit (desserre)"; break;
                case 6: tKnob = KWheelHub; what = "moyeu du volant (doit etre refuse)"; break;
                case 7: tKnob = KStalk; what = st.Stalk != null || st.HasWiperBtn ? "essuie-glaces" : "essuie-glaces (pas de commande)"; break;
                case 8: tKnob = KIgnition; what = "cle (moteur coupe)"; break;
                case 9:
                    {
                        Log.Info("standalone : commandes, invite descend : " + Seats.TestLeave());
                        float sx = -Mathf.Sign(b.M.Head.x);
                        if (st.DoorOpen != null && st.DoorOpen.Length > 1 && st.DoorOpen[1] && st.Doors[1] != null) ToggleDoor(b, st, 1);
                        var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                        pl.transform.position = b.Car.transform.TransformPoint(new Vector3(st.Cabin.center.x + sx * (st.Cabin.extents.x + 0.45f), 0.3f, st.Cabin.center.z));
                        if (cc != null) cc.enabled = true;
                        what = "poste dehors cote passager, portiere fermee"; break;
                    }
                case 10: tKnob = KLights; what = "phares de dehors, portiere fermee (doit etre refuse)"; break;
                case 11:
                    {
                        // (ecarte de 2,5 m avant d'ouvrir : la portiere qui s'ouvre dans le joueur l'envoyait en l'air sur My Summer Car ;
                        // [Test] PortiereDansJoueur=1 : au contraire, poste a 45 cm du flanc, au milieu de la portiere -- elle doit s'arreter sur lui)
                        // ([Test] PortiereDansJoueur=2 : laisse ou le pas 9 l'a poste -- 45 cm du flanc, milieu de l'habitacle : la place de l'essai du 08/11)
                        int dansMode = Config.GetInt("Test", "PortiereDansJoueur", 0); bool dans = dansMode != 0;
                        float sx = -Mathf.Sign(b.M.Head.x); var cc = pl.GetComponent<CharacterController>();
                        if (dansMode != 2)
                        {
                            if (cc != null) cc.enabled = false;
                            pl.transform.position = b.Car.transform.TransformPoint(new Vector3(st.Cabin.center.x + sx * (st.Cabin.extents.x + (dans ? 0.45f : 2.5f)), 0.3f, dans && st.Doors[1] != null ? st.DoorCenter[1].z : st.Cabin.center.z));
                            if (cc != null) cc.enabled = true;
                        }
                        if (dans) { cmdY0 = pl.transform.position.y; cmdYMax = 0f; cmdDoorMax = 0f; }
                        if (st.Doors[1] != null && !st.DoorOpen[1]) ToggleDoor(b, st, 1); what = dans ? "poste dans la course de la portiere, portiere passager ouverte" : "ecarte, portiere passager ouverte"; break;
                    }
                case 12:
                    {
                        // (revenu dans l'ouverture, en arriere de la charniere)
                        float sx = -Mathf.Sign(b.M.Head.x); var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                        pl.transform.position = b.Car.transform.TransformPoint(new Vector3(st.Cabin.center.x + sx * (st.Cabin.extents.x + 0.45f), 0.3f, st.Cabin.center.z - 0.2f));
                        if (cc != null) cc.enabled = true;
                        what = "revenu dehors, portiere ouverte"; break;
                    }
                case 13: tKnob = KLights; what = "phares de dehors, portiere ouverte"; break;
                case 14: Photo(b, "commandes-dehors", b.Car.transform.InverseTransformPoint(PlayerCam() != null ? PlayerCam().position : pl.transform.position), st.T.Yeux + new Vector3(0f, -0.2f, 0.4f), 50f); what = "photo"; break;
                default: cmdStep = 99; cmdAt = float.MaxValue; Log.Info("standalone : commandes fini (invite)"); return;
            }
            tDown = tKnob >= 0 && !tRight;
            Transform pc = PlayerCam();
            Log.Info("standalone : commandes, invite pas " + cmdStep + " : " + what + (pc != null ? " (yeux " + b.Car.transform.InverseTransformPoint(pc.position).ToString("F2") + ")" : ""));
        }

        static int cGearI = -1, cGearSeen = -99; static float cGearAt, cGearSeenAt; static bool cGearLogged, cGearShot;
        static string GearLabel(VazCar.Built b, int g)
        {
            if (b.Dt == null || b.Dt.gearRatios == null || g < 0) return "?";
            int n = Array.IndexOf(b.Dt.gearRatios, 0f);
            return g == n ? "N" : g < n ? "R" : (g - n).ToString();
        }
        // (ce que montre la planche : afficheur sept segments -- dernier rapport dessine -- ou ecran LCD -- rapport tire de sa cle)
        static string DashGear(VazCar.Built b, State st)
        {
            if (st.GearTex != null) return (st.GearLit ? "" : "eteint ") + GearLabel(b, st.GearShown);
            if (st.LcdTex != null && st.LcdKey >= 0) { int gi = st.LcdKey / 1000 % 1000 - 3; return "LCD " + (gi == -1 ? "N" : gi == -2 ? "R" : gi >= 1 ? gi.ToString() : "eteint"); }
            return "sans afficheur";
        }
        static Vector3 DashAt(VazCar.Built b, State st)
        {
            Transform q = st.LcdR != null ? st.LcdR.transform : b.Body.Find("rapport");
            if (q == null && st.Tacho != null) q = st.Tacho.Needle;
            return q != null ? b.Car.transform.InverseTransformPoint(q.position) : st.T.Yeux + Vector3.forward;
        }
        static void GearsTest(VazCar.Built b, State st, bool host, float t)
        {
            if (host && driving == b && cStep == 3 && t > 48f && cGearI < b.Dt.gearRatios.Length && t >= cGearAt)
            {
                bool seated = false;
                for (int id = 0; id < 16 && !seated; id++)
                {
                    Transform tc; Vector3 hd; string cn;
                    if (id != Net.Session.LocalId && Seats.RemoteSeat(id, out tc, out hd, out cn) && tc != null && (tc == b.Car.transform || tc.IsChildOf(b.Car.transform))) seated = true;
                }
                if (!seated && t < 95f) return;
                var d = (StandaloneDriver)b.Axis;
                if (cGearI < 0) Log.Info("standalone : rapports, hote : " + (seated ? "invite assis" : "PAS D'INVITE ASSIS a 95 s") + ", " + b.Dt.gearRatios.Length + " positions");
                cGearI++; cGearAt = t + (cGearI == 0 ? 2f : 3f);
                if (cGearI >= b.Dt.gearRatios.Length) { d.Gear = Array.IndexOf(b.Dt.gearRatios, 0f); d.Clutch = 0f; d.Brake = 0f; Log.Info("standalone : rapports, hote : fini, point mort"); return; }
                d.Keys = false; d.Throttle = 0f; d.Brake = 1f; d.Handbrake = 1f; d.Clutch = 1f; d.Gear = cGearI;
            }
            if (cStep < 3 || (host && driving != b)) return;
            if (b.Dt.gear != cGearSeen) { cGearSeen = b.Dt.gear; cGearSeenAt = t; cGearLogged = false; }
            if (cGearLogged || t < cGearSeenAt + 0.6f) return;   // (planche redessinee depuis, et l'invite a recu)
            cGearLogged = true;
            GameObject pl = Game.PlayerGo;
            string g = GearLabel(b, b.Dt.gear);
            Log.Info("standalone : rapports, " + (host ? "hote (conducteur)" : "invite (" + (pl != null && pl.transform.IsChildOf(b.Car.transform) ? "assis" : "DEHORS") + ")")
                     + " : boite " + g + ", planche " + DashGear(b, st) + ", contact " + (st.EngineOn ? "moteur" : st.KeyPos > 0 ? "mis" : "COUPE")
                     + (host ? "" : ", recu " + GearLabel(b, VehicleSync.RemoteGear(b.Car.transform)) + ", copie " + (b.Rb.isKinematic ? "cinematique" : "PHYSIQUE ICI")));
            if (g == "3" && !cGearShot)
            {
                cGearShot = true; Transform pc = PlayerCam();
                Photo(b, "rapport-" + (host ? "hote" : "invite") + "-" + b.M.Folder, pc != null ? b.Car.transform.InverseTransformPoint(pc.position) : st.T.Yeux, DashAt(b, st), 28f);
            }
        }
        // (console du chauffage vue du milieu de l'habitacle, entre les sieges, avec une lampe le temps des photos)
        static void HeatShots(VazCar.Built b, State st, string tag)
        {
            var lg = new GameObject("essai_lampe"); lg.transform.SetParent(b.Body, false); lg.transform.localPosition = new Vector3(0f, 1.0f, 0.25f);
            Light li = lg.AddComponent<Light>(); li.type = LightType.Point; li.range = 1.5f; li.intensity = 2.5f;
            Photo(b, "console-" + tag, new Vector3(0.0f, 1.0f, 0.12f), new Vector3(-0.01f, 0.87f, 0.53f), 26f);
            if (st.Heat != null && st.Heat.HasSwitch) Photo(b, "chauffage-voyant-" + tag, new Vector3(0.05f, 1.0f, 0.18f), st.Heat.SwitchAt, 12f);
            UnityEngine.Object.Destroy(lg);
        }
        static List<GameObject> cObjets; static int cObjLog;
        static float wLog; static bool wDump;
        // (essai des portieres d'une copie menee par un autre, desync du 02/11 : meme chemin que le clic -- k 0 conducteur, 1 passager)
        public static string TestDoor(string car, int k)
        {
            foreach (VazCar.Built x in cars)
            {
                State st;
                if (x.Car == null || !x.Car.name.StartsWith(car) || !states.TryGetValue(x, out st) || st.Doors == null || st.Doors[k] == null) continue;
                ToggleDoor(x, st, k);
                return DoorState(x, st, k);
            }
            return "pas de portiere " + k + " sur " + car;
        }
        static string DoorState(VazCar.Built b, State st, int k)
        {
            return "portiere " + (k == 0 ? "conducteur" : "passager") + " de " + b.Car.name + " DoorOpen=" + st.DoorOpen[k] + " angle=" + st.DoorAngle[k].ToString("F0") + " cinematique=" + b.Rb.isKinematic;
        }
        static int cDoor; static float cDoorAt, cDoorLog; static bool cDoorSeen, cDoorWas;

        // [Test] Autotest=quetevitres (seul ou a 2, voiture de la quete s21) : le premier joueur prend le volant ; vitre gauche et droite posees
        // a 0, 1/2, 1 puis 0 (comme la molette : WinPos, puis SendState) ; chacun note verre, sommets, maillage. Sans fichier de vitres :
        // tout reste a 0. Photos dehors et dedans (dumps\photo-vitres-*.png) a chaque position ; [Test] StandalonePluie=1 : sous la pluie.
        static float qwT0 = -1; static int qwStep; static bool qwAbsent;
        static int qwAim = -1, qwHit = -1, qwWheel; static float qwScroll, qwWheelAt;
        static string WinState(VazCar.Built b, State st)
        {
            var sb = new System.Text.StringBuilder();
            for (int w = 0; w < 2; w++)
            {
                if (st.Glass[w] == null) { sb.Append(w == 0 ? "g pas de vitre" : " ; d pas de vitre"); continue; }
                int nv = 0; string mesh = ""; bool wet = false;
                foreach (MeshFilter mf in st.Glass[w].GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) { nv += mf.sharedMesh.vertexCount; mesh = mf.sharedMesh.name; }
                foreach (MeshRenderer mr in st.Glass[w].GetComponentsInChildren<MeshRenderer>(true)) if (mr.sharedMaterial != null && mr.sharedMaterial.name.StartsWith("standalone_vitre_pluie")) wet = true;
                sb.Append(w == 0 ? "g " : " ; d ").Append("pos " + st.WinPos[w].ToString("F2") + " verre " + st.Glass[w].localPosition.ToString("F3") + " " + nv + " sommets (" + mesh + (wet ? ", matiere de pluie" : "") + ")" + (st.Crank[w] != null ? " manivelle " + st.Crank[w].localEulerAngles.ToString("F0") : "") + (st.WinV[w] != null ? " fichier" : " sans fichier"));
            }
            return sb.ToString();
        }
        static void WinPhotos(VazCar.Built b, string pos)
        {
            Photo(b, "vitres-" + pos + "-dehors-g", new Vector3(-3.4f, 1.15f, 0.2f), new Vector3(-0.7f, 0.9f, 0.1f));
            Photo(b, "vitres-" + pos + "-dehors-d", new Vector3(3.4f, 1.15f, 0.2f), new Vector3(0.7f, 0.9f, 0.1f));
            Vector3 head = b.M.Head; head.x = 0f;
            Photo(b, "vitres-" + pos + "-dedans-g", head, new Vector3(-0.8f, 0.85f, 0.1f), 70f);
            Photo(b, "vitres-" + pos + "-dedans-d", head, new Vector3(0.8f, 0.85f, 0.1f), 70f);
        }
        // [Test] Autotest=vitresgaree (2 joueurs, l'invite en retard : -Delai) : a 25 s l'hote baisse les deux vitres de la voiture de la
        // quete garee, sans la mener ; l'invite note leur etat a 30 et 45 s de sa partie (attendu : baissees, comme chez l'hote).
        static int pwStep;
        public static void ParkedWindowTest(float t)
        {
            VazCar.Built b = null; State st = null;
            foreach (VazCar.Built x in cars) if (QueteGolf.IsQuestCar(x) && x.Car != null) { b = x; states.TryGetValue(x, out st); }
            if (b == null || st == null) return;
            bool host = !Net.Session.Active || Net.Session.IsHost;
            if (host && pwStep == 0 && t > 25f) { pwStep = 1; st.WinPos[0] = st.WinPos[1] = 1f; SendState(b, st); Log.Info("autotest : vitresgaree, hote : " + WinState(b, st)); }
            if (host && pwStep == 1 && t > 90f) { pwStep = 2; Log.Info("autotest : vitresgaree, hote t=90 : " + WinState(b, st)); }
            if (!host && ((pwStep == 0 && t > 30f) || (pwStep == 1 && t > 45f))) { pwStep++; Log.Info("autotest : vitresgaree, invite t=" + t.ToString("F0") + " : " + WinState(b, st)); }
        }

        public static void QuestWindowTest(float t)
        {
            float now = Time.realtimeSinceStartup;
            bool coop = Net.Session.Active;
            if (t < 25f || (coop && qwT0 < 0 && !Jobs.OtherInGame(20f))) return;
            VazCar.Built b = null; State st = null;
            foreach (VazCar.Built x in cars) if (QueteGolf.IsQuestCar(x) && x.Car != null) { b = x; states.TryGetValue(x, out st); }
            if (b == null || st == null) { if (t > 70f && !qwAbsent) { qwAbsent = true; Log.Info("autotest : quetevitres, voiture de la quete absente"); } return; }
            if (qwT0 < 0) { qwT0 = now; Log.Info("autotest : quetevitres, repos : " + WinState(b, st)); }
            float d = now - qwT0;
            bool first = !coop || Net.Session.IsHost;
            GameObject pl = Game.PlayerGo;
            if (first && qwStep == 0 && pl != null)
            {
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                pl.transform.position = b.Car.transform.TransformPoint(new Vector3(b.M.Head.x, 0.5f, st.T.EntreeZ));
                if (cc != null) cc.enabled = true;
                Enter(b, st); SendState(b, st);
                Log.Info("autotest : quetevitres, au volant de " + b.Car.name);
            }
            // Apres la serie : le vrai chemin du joueur, regard sur chaque manivelle et crans de molette (vers soi : baisse ; 8 crans = la course).
            if (first && qwStep >= 9 && qwWheel < 6 && d > 37f && now >= qwWheelAt)
            {
                qwWheelAt = now + 1.2f;
                int cote = qwWheel / 3, etape = qwWheel % 3;
                if (etape == 0) { qwAim = cote; qwHit = -1; qwScroll = -0.1f; }
                else if (etape == 1) { Log.Info("autotest : quetevitres, regard manivelle " + (cote == 0 ? "g" : "d") + " : visee " + (qwHit == cote ? "oui" : qwHit < 0 ? "non" : "l'autre") + ", texte « " + HutongGames.PlayMaker.FsmVariables.GlobalVariables.GetFsmString("GUIinteraction").Value + " », cran vers soi : " + WinState(b, st)); qwScroll = 0.1f; }
                else { Log.Info("autotest : quetevitres, cran inverse : " + WinState(b, st)); qwAim = -1; }
                qwWheel++;
            }
            float[] when = { 12f, 18f, 24f, 30f }, val = { 0f, 0.5f, 1f, 0f };
            string[] nom = { "0", "demi", "bas", "remontee" };
            if (qwStep == 0) qwStep = 1;
            for (int i = 0; i < when.Length; i++)
                if (qwStep == 1 + 2 * i && d > when[i])
                {
                    qwStep++;
                    // ([Test] QueteVitresInvite=1 : l'invite les tourne, l'hote reste au volant -- vitres changees par un autre que le conducteur)
                    if (Config.GetInt("Test", "QueteVitresInvite", 0) != 0 ? coop && !first : first) { st.WinPos[0] = st.WinPos[1] = val[i]; st.WinLocalT[0] = st.WinLocalT[1] = now; SendState(b, st); }
                }
                else if (qwStep == 2 + 2 * i && d > when[i] + 3f)
                {
                    qwStep++;
                    Log.Info("autotest : quetevitres, T+" + d.ToString("F0") + " " + nom[i] + " (" + (first ? "hote" : "invite") + ") : " + WinState(b, st));
                    if (first && Config.GetInt("Test", "VitresPhotos", 1) != 0) WinPhotos(b, nom[i]);
                    break;
                }
        }
        // [Test] Autotest=queteportiere (2 joueurs, voiture de la quete s21 ; My Winter Car surtout, ou elle est la seule voiture
        // standalone) : 20 s apres l'arrivee de l'autre, l'hote prend le volant (T+0) et reste a l'arret ; l'invite s'assoit en passager
        // (T+5), ouvre sa portiere (T+8) et la referme (T+12), meme chemin que le clic (ToggleDoor) ; chacun note la portiere passager
        // toutes les 0,5 s de T+8 a T+18. Attendu : fermee, angle 0, des deux cotes ; [Test] PortiereCopieFermee=0 : ouverte (bug).
        static float qdT0 = -1, qdLog; static int qdStep;
        public static void QuestDoorTest(float t)
        {
            if (!Net.Session.Active) return;
            float now = Time.realtimeSinceStartup;
            if (qdT0 < 0) { if (!Jobs.OtherInGame(20f)) return; qdT0 = now; }
            float d = now - qdT0;
            VazCar.Built b = null; State st = null;
            foreach (VazCar.Built x in cars) if (QueteGolf.IsQuestCar(x) && x.Car != null) { b = x; states.TryGetValue(x, out st); }
            if (b == null || st == null || st.Doors == null || st.Doors[1] == null) { if (qdStep == 0) { qdStep = -1; Log.Info("autotest : queteportiere, voiture de la quete absente ou sans portiere passager"); } return; }
            GameObject pl = Game.PlayerGo;
            if (pl == null) return;
            bool host = Net.Session.IsHost;
            if (host && qdStep == 0)
            {
                qdStep = 1;
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                pl.transform.position = b.Car.transform.TransformPoint(new Vector3(b.M.Head.x, 0.5f, st.T.EntreeZ));
                if (cc != null) cc.enabled = true;
                Enter(b, st); SendState(b, st);
                Log.Info("autotest : queteportiere, hote au volant de " + b.Car.name);
            }
            if (!host && qdStep == 0 && d > 5f) { qdStep = 1; Log.Info("autotest : queteportiere, invite passager -> " + Seats.TestSit(b.Car.name, 0) + ", copie " + (b.Rb.isKinematic ? "cinematique" : "PHYSIQUE")); }
            if (!host && qdStep == 1 && d > 8f) { qdStep = 2; ToggleDoor(b, st, 1); Log.Info("autotest : queteportiere, invite ouvre : " + DoorState(b, st, 1)); }
            if (!host && qdStep == 2 && d > 12f) { qdStep = 3; ToggleDoor(b, st, 1); Log.Info("autotest : queteportiere, invite ferme : " + DoorState(b, st, 1)); }
            if (d > 8f && d < 18f && now >= qdLog)
            {
                qdLog = now + 0.5f;
                Log.Info("autotest : queteportiere T+" + d.ToString("F1") + " (" + (host ? "hote" : "invite") + ") : " + DoorState(b, st, 1));
            }
        }
        static int cSon;
        static bool cCoupe, cPneu, cEntree; static float cMesT, cEntreeAt, cEntreeLog;
        static void LayerRows()
        {
            foreach (string n in new[] { "Collider2", "HingedObjects" })
            {
                int l = LayerMask.NameToLayer(n); var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 32; i++) if (!Physics.GetIgnoreLayerCollision(l, i)) { string q = LayerMask.LayerToName(i); sb.Append(' ').Append(q.Length > 0 ? q : i.ToString()); }
                Log.Info("standalone : couche " + n + " heurte :" + sb);
            }
        } static int cMesN;
        static int swView, swFrames, swHitN; static Camera swCam, swOld; static Vector3 swDir; static readonly HashSet<string> swTexts = new HashSet<string>(), swHits = new HashSet<string>();
        // [Test] CoqueTour=12,14,... (StandaloneModele = le premier) : dans une seule partie, chaque voiture, sans les boites, avec le plancher que le joueur heurte (ancien), avec celui qu'il ne heurte pas :
        // le joueur marche de dehors vers le siege, portiere ouverte, puis est pose dedans. Pieds, ecart des yeux au point de vue du siege : ce que la tete gardee en s'asseyant reprendrait.
        static int tourCar, tourVar, tourStep, tourTrace; static float tourAt, tourPrevY = -10f; static string tourLine;
        static int TV(int i) { string[] v = Config.Get("Test", "CoqueTourVar", "0,1,2").Split(','); return i < 0 ? v.Length : int.Parse(v[i]); }
        static string TourUnder(GameObject pl, Transform car)
        {
            var sb = new System.Text.StringBuilder(" sous :"); int n = 0; var seen = new HashSet<string>();
            foreach (Vector3 off in new[] { Vector3.zero, Vector3.right * 0.25f, Vector3.left * 0.25f, Vector3.forward * 0.25f, Vector3.back * 0.25f })
                foreach (RaycastHit h in Physics.RaycastAll(pl.transform.position + car.TransformDirection(off) + Vector3.up * 0.5f, Vector3.down, 1.6f))
                {
                    if (h.collider.isTrigger || h.collider.transform.root == pl.transform.root || n >= 8) continue;
                    string q = h.collider.name + "/" + LayerMask.LayerToName(h.collider.gameObject.layer) + "@" + car.InverseTransformPoint(h.point).y.ToString("F2");
                    if (seen.Add(q)) { sb.Append(' ').Append(q); n++; }
                }
            // (ce que la capsule du joueur touche, couches qu'il heurte)
            CharacterController cc = pl.GetComponent<CharacterController>();
            if (cc != null)
            {
                sb.Append(" ; touche (rayon ").Append(cc.radius.ToString("F2")).Append(", posee ").Append(cc.isGrounded).Append(") :");
                Vector3 c = pl.transform.position + cc.center;
                var near = new HashSet<string>();
                foreach (Collider q in Physics.OverlapSphere(c + Vector3.down * Mathf.Max(0f, cc.height * 0.5f - cc.radius), cc.radius + 0.12f))
                {
                    if (q.isTrigger || q.transform.root == pl.transform.root || Physics.GetIgnoreLayerCollision(pl.layer, q.gameObject.layer)) continue;
                    if (near.Add(q.name)) { Bounds bq = q.bounds; sb.Append(' ').Append(q.name).Append('/').Append(LayerMask.LayerToName(q.gameObject.layer)).Append(' ').Append(car.InverseTransformPoint(bq.min).ToString("F2")).Append("..").Append(car.InverseTransformPoint(bq.max).ToString("F2")); }
                }
            }
            return sb.ToString();
        }
        static void Tour(float t, string ids, Transform car, VazCar.Built b, State st, GameObject pl)
        {
            string[] list = ids.Split(',');
            if (tourCar >= list.Length) return;
            CharacterController cc = pl.GetComponent<CharacterController>();
            Transform cam = PlayerCam();
            if (cc == null || cam == null) return;
            if (tourStep == 4)
            {
                if (t < tourAt + 1.5f) return;
                tourCar++;
                if (tourCar >= list.Length) { Log.Info("coque tour : fini"); return; }
                Log.Info("coque tour : demande -> " + Vaz.Request(3, int.Parse(list[tourCar])));
                tourStep = 0; tourAt = t;
                return;
            }
            if (tourStep == 0)
            {
                if (b != null && st != null && st.HasCabin && b.M.Folder == VazCar.ModelOf(int.Parse(list[tourCar])).Folder && t > tourAt + 3f) { tourStep = 1; tourVar = 0; }
                return;
            }
            if (b == null || st == null) { tourStep = 0; tourAt = t; return; }
            float zw = st.DoorBox[0].center.z - 0.1f;
            if (tourStep == 1)
            {
                int cab = 0;
                foreach (BoxCollider bc in b.Car.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (bc.name != "coque_x") continue;
                    bc.enabled = TV(tourVar) != 0;
                    if (bc.gameObject.layer == seatItemLayer || bc.gameObject.layer == itemLayer) { Bounds wb = bc.bounds; Vector3 lc = car.InverseTransformPoint(wb.center); if (Mathf.Abs(lc.y - (st.Cabin.min.y - 0.04f)) < 0.02f && Mathf.Abs(lc.z - st.Cabin.center.z) < 0.02f) { bc.gameObject.layer = TV(tourVar) == 1 ? itemLayer : seatItemLayer; cab++; } }
                }
                st.DoorOpen[0] = true; st.DoorAngle[0] = 70f; st.DoorVel[0] = 0f; DoorAnim(b, st);
                cc.enabled = false; pl.transform.position = car.TransformPoint(new Vector3(-2.6f, 0.4f, zw)); cc.enabled = true;   // (loin de la portiere ouverte : pose contre elle, le joueur etait pousse sur le toit)
                tourStep = 2; tourAt = t;
                if (cab != 1) Log.Warn("coque tour : " + cab + " plancher(s) trouves");
                return;
            }
            if (tourStep == 2)
            {
                Vector3 go = car.TransformPoint(new Vector3(b.M.Head.x, 0f, zw)) - pl.transform.position; go.y = 0f;
                if (t < tourAt + 3.5f)
                {
                    Vector3 l0 = car.InverseTransformPoint(pl.transform.position);
                    if (tourPrevY > -9f && l0.y > tourPrevY + 0.03f && tourTrace < 12) { tourTrace++; Log.Info("coque tour : monte de " + tourPrevY.ToString("F2") + " a " + l0.ToString("F2") + ", voiture " + car.position.ToString("F2") + " haut " + car.up.ToString("F2") + " vitesse " + b.Rb.velocity.ToString("F2") + " joueur " + pl.transform.position.ToString("F2") + TourUnder(pl, car)); }
                    tourPrevY = l0.y;
                    cc.Move(go.normalized * Mathf.Min(1.5f * Time.deltaTime, go.magnitude) + Vector3.down * 2f * Time.deltaTime);
                    return;
                }
                tourPrevY = -10f;
                Vector3 lp = car.InverseTransformPoint(pl.transform.position), dv = car.InverseTransformPoint(cam.position) - st.T.Yeux;
                tourLine = "marche " + (Mathf.Abs(lp.x - b.M.Head.x) < 0.15f ? "ENTRE" : "BLOQUE") + " en " + lp.ToString("F2") + " yeux " + dv.ToString("F2") + TourUnder(pl, car);
                cc.enabled = false; pl.transform.position = car.TransformPoint(new Vector3(b.M.Head.x, st.Cabin.min.y + 0.45f, st.T.EntreeZ)); cc.enabled = true;
                tourStep = 3; tourAt = t;
                return;
            }
            if (tourStep == 3)
            {
                if (t < tourAt + 1.2f) { cc.Move(Vector3.down * 2f * Time.deltaTime); return; }
                Vector3 lp = car.InverseTransformPoint(pl.transform.position), dv = car.InverseTransformPoint(cam.position) - st.T.Yeux;
                Log.Info("coque tour : " + b.M.Folder + " " + (TV(tourVar) == 0 ? "sans" : TV(tourVar) == 1 ? "ancien" : "nouveau") + " : " + tourLine + " ; pose dedans, y " + lp.y.ToString("F2") + " yeux " + dv.ToString("F2") + " (plancher " + st.Cabin.min.y.ToString("F2") + ")" + TourUnder(pl, car));
                tourVar++;
                if (tourVar < TV(-1)) { tourStep = 1; return; }
                // (pour finir, le vrai chemin : boites en place, a pied depuis dehors, assis, tete gardee)
                foreach (BoxCollider bc in b.Car.GetComponentsInChildren<BoxCollider>(true)) if (bc.name == "coque_x") { bc.enabled = true; Vector3 lc = car.InverseTransformPoint(bc.bounds.center); if (Mathf.Abs(lc.y - (st.Cabin.min.y - 0.04f)) < 0.02f && Mathf.Abs(lc.z - st.Cabin.center.z) < 0.02f) bc.gameObject.layer = seatItemLayer; }
                st.DoorOpen[0] = true; st.DoorAngle[0] = 70f; st.DoorVel[0] = 0f; DoorAnim(b, st);
                cc.enabled = false; pl.transform.position = car.TransformPoint(new Vector3(-2.6f, 0.4f, zw)); cc.enabled = true;
                tourStep = 5; tourAt = t;
                return;
            }
            if (tourStep == 5)
            {
                Vector3 go = car.TransformPoint(new Vector3(b.M.Head.x, 0f, zw)) - pl.transform.position; go.y = 0f;
                if (t < tourAt + 3.5f) { cc.Move(go.normalized * Mathf.Min(1.5f * Time.deltaTime, go.magnitude) + Vector3.down * 2f * Time.deltaTime); return; }
                Enter(b, st); tourStep = 6; tourAt = t;
                return;
            }
            if (tourStep == 6)
            {
                if (t < tourAt + 2f) return;
                Vector3 e = car.InverseTransformPoint(cam.position);
                Log.Info("coque tour : " + b.M.Folder + " assis : yeux " + (e - st.T.Yeux).ToString("F2") + " du siege, tete gardee " + st.HasEyeHold);
                Photo(b, "assis-" + b.M.Folder, e, e + new Vector3(0f, -0.08f, 1f), 60f);
                Exit(b, true); tourStep = 7; tourAt = t;
                return;
            }
            if (tourStep == 7)
            {
                if (t < tourAt + 0.5f) return;
                cc.enabled = false; pl.transform.position = car.position + car.right * 7f + Vector3.up; cc.enabled = true;
                Vaz.Release(); tourStep = 4; tourAt = t;
                return;
            }
        }
        // Pose d'un conducteur distant, au format du releve de l'outil des places ([Test] PlacesPoses=<dossier> : pose-<cle>-c.json, racine et
        // noeuds locaux, repere de la voiture) -- essais des jambes du conducteur, JD 09/11
        static void DumpDriverPose(Avatar a, Transform car, string key)
        {
            string pd = Config.Get("Test", "PlacesPoses", "");
            if (pd.Length == 0 || a == null || a.Root == null || car == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            System.Func<float, string> F = x => x.ToString("R", ci);
            System.Func<Vector3, string> V = v => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
            System.Func<Quaternion, string> Q = q => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
            Transform r = a.Root.transform;
            var sb = new System.Text.StringBuilder("{\"cle\":\"" + key + "\",\"place\":\"c\",\"racine\":{\"p\":" + V(car.InverseTransformPoint(r.position)) + ",\"q\":" + Q(Quaternion.Inverse(car.rotation) * r.rotation) + "},\"noeuds\":{");
            var seen = new HashSet<string>(); int n = 0;
            foreach (Transform x in r.GetComponentsInChildren<Transform>())
            {
                if (x == r || !seen.Add(x.name)) continue;
                sb.Append(n++ > 0 ? "," : "").Append('"').Append(x.name.Replace("\"", "")).Append("\":{\"p\":").Append(V(x.localPosition)).Append(",\"q\":").Append(Q(x.localRotation)).Append('}');
            }
            sb.Append("}}");
            string safe = System.Text.RegularExpressions.Regex.Replace(key, "[^A-Za-z0-9_-]", "_");
            try { System.IO.Directory.CreateDirectory(pd); System.IO.File.WriteAllText(System.IO.Path.Combine(pd, "pose-" + safe + "-c.json"), sb.ToString()); Log.Info("essai : pose du conducteur ecrite (" + key + ")"); }
            catch (System.Exception ex) { Log.Warn("essai : pose du conducteur : " + ex.Message); }
        }

        // ([Test] PhotoConducteurVoiture=<debut du nom>, JD 09/11 : jambes des conducteurs comme dans l'outil des places, voitures du jeu aussi) :
        // un conducteur distant assis depuis 8 s dans cette voiture -> photos (depuis la place passager, contre la portiere, par la vitre)
        // et sa pose ; une fois. Appele chaque image par StandaloneCar.Update.
        static float pcSince = -1f; static bool pcDone;
        static void TestPhotoConducteur()
        {
            if (pcDone) return;
            string want = Config.Get("Test", "PhotoConducteurVoiture", "");
            if (want.Length == 0) { pcDone = true; return; }
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null || a.Player == null || !a.InVehicle || Seats.RemoteSeatIndex(a.Player.Id) >= 0) continue;
                string cn = VehicleSync.RemoteCarName(a.Player.Id);
                Transform ct = VehicleSync.RemoteCarTransform(a.Player.Id);
                if (cn == null || ct == null || !cn.StartsWith(want)) continue;
                float now = Time.realtimeSinceStartup;
                if (pcSince < 0f) { pcSince = now; return; }
                if (now < pcSince + 8f) return;
                pcDone = true;
                Vector3 e;
                if (!Seats.DriverHead(ct, out e)) e = ct.InverseTransformPoint(a.Root.transform.position) + new Vector3(0f, 1.2f, 0f);
                string tag = "-" + System.Text.RegularExpressions.Regex.Replace(cn.Split('(')[0], "[^A-Za-z0-9_-]", "_");
                SewageHose.Shot("-conducteur-passager" + tag, ct.TransformPoint(new Vector3(-e.x, e.y + 0.02f, e.z - 0.1f)), ct.TransformPoint(new Vector3(e.x + 0.05f, e.y - 0.8f, e.z + 0.55f)));
                SewageHose.Shot("-conducteur-portiere" + tag, ct.TransformPoint(new Vector3(e.x - 0.42f, e.y - 0.4f, e.z + 0.2f)), ct.TransformPoint(new Vector3(e.x + 0.1f, e.y - 0.65f, e.z + 0.35f)));
                SewageHose.Shot("-conducteur-vitre" + tag, ct.TransformPoint(new Vector3(e.x - 1.6f, e.y - 0.1f, e.z + 0.2f)), ct.TransformPoint(new Vector3(e.x, e.y - 0.4f, e.z + 0.2f)));
                Log.Info("essai : photos du conducteur dans " + cn + " (yeux " + e.ToString("F2") + ")");
                DumpDriverPose(a, ct, SeatFiles.KeyOf(cn));
                return;
            }
        }

        public static void CoopTest(float t)
        {
            if (!Net.Session.Active) return;
            bool host = Net.Session.IsHost;
            GameObject pl = Game.PlayerGo;
            if (pl == null) return;
            Transform car = Vaz.CarOf(host ? Vaz.MeId : Vaz.OwnerOf(3));
            VazCar.Built b = null; State st = null;
            foreach (VazCar.Built x in cars) if (car != null && x.Car == car.gameObject) { b = x; states.TryGetValue(x, out st); }
            if (cStep == 0 && t > 22f)
            {
                cStep = 1; VazTenue.MakeTrack();
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                pl.transform.position = new Vector3(host ? 0f : 4f, VazTenue.TrackY + 1.2f, 0f); pl.transform.rotation = Quaternion.identity;
                if (cc != null) cc.enabled = true;
                Log.Info("standalone : coop, sur la piste (" + (host ? "hote" : "invite") + ")");
            }
            if (host && cStep == 1 && t > 26f) { cStep = 2; Log.Info("standalone : coop, demande -> " + Vaz.Request(3, Config.GetInt("Test", "StandaloneModele", VazCar.ModelR5S))); }   // ([Test] StandaloneModele=6 : l'ancienne R5, remplacee sur MSC)
            // ([Test] StandalonePhotos=1 : la voiture vue de dehors avant de monter -- dumps\photo-<vue>.png, modeles 3D des addons)
            if (host && cStep == 2 && t > 32f && b != null && !cPhotos && Config.GetInt("Test", "StandalonePhotos", 0) != 0)
            {
                cPhotos = true;
                // ([Test] StandaloneVitres=0.6 : vitres de portiere baissees pour les photos)
                float wv = Config.GetFloat("Test", "StandaloneVitres", 0f); if (wv > 0f && st != null) { st.WinPos[0] = st.WinPos[1] = Mathf.Clamp01(wv); WindowAnim(b, st); }
                // ([Test] StandaloneEssuie=0.25 : essuie-glaces figes a cette phase pour les photos, comodo baisse)
                float we = Config.GetFloat("Test", "StandaloneEssuie", 0f); if (we > 0f && st != null) { st.WipersOn = false; st.WiperPhase = we; st.StalkAngle = 14f; WiperAnim(b, st); }
                // ([Test] StandaloneHayon=1 : hayon ouvert, plage relevee, portiere conducteur entrouverte a 40 degres pour les photos)
                if (Config.GetInt("Test", "StandaloneHayon", 0) != 0 && st != null)
                {
                    st.HatchOpen = true; st.HatchAngle = 78f; st.ShelfUp = true; st.ShelfAngle = 80f;
                    if (st.Doors[0] != null) { st.DoorOpen[0] = true; st.DoorAngle[0] = 40f; st.DoorVel[0] = 0f; }
                    DoorAnim(b, st);
                }
                // ([Test] StandaloneCeintures=1 : les quatre ceintures bouclees ; StandaloneDossier=1 : dossier droit rabattu)
                if (Config.GetInt("Test", "StandaloneCeintures", 0) != 0 && st != null) { testBelts = true; BeltTick(b, st); }
                if (Config.GetInt("Test", "StandaloneDossier", 0) != 0 && st != null) { st.Folded[1] = true; st.FoldAngle[1] = 42f; if (st.Doors[1] != null) { st.DoorOpen[1] = true; st.DoorAngle[1] = 65f; st.DoorVel[1] = 0f; } DoorAnim(b, st); }
                if (Config.GetInt("Test", "StandalonePhotos", 0) >= 2 && st != null)
                {
                    // (vues de l'habitacle : de la place conducteur vers la portiere passager et le ciel de toit, de la portiere passager
                    // ouverte vers la banquette, comodo, roue avant gauche de pres)
                    Vector3 e = st.T.Yeux;
                    Photo(b, "habitacle", e + new Vector3(0.05f, 0.02f, -0.05f), new Vector3(0.75f, 1.12f, -0.35f));
                    Photo(b, "banquette", new Vector3(1.35f, 1.05f, 0.15f), new Vector3(0.0f, 0.6f, -0.8f));
                    Photo(b, "ceintures", new Vector3(0.5f, 1.05f, 0.4f), new Vector3(-0.33f, 0.72f, -0.2f));
                    Photo(b, "arriere", new Vector3(0.12f, 1.1f, 0.05f), new Vector3(0f, 0.7f, -1.0f));
                    Photo(b, "comodo", e + new Vector3(0.0f, -0.05f, 0.05f), st.Stalk != null ? StalkTipLocal(st) : e + Vector3.forward);
                    Photo(b, "roue", new Vector3(-1.6f, 0.45f, 1.25f), new Vector3(-0.7f, 0.3f, 1.25f));
                }
                Photo(b, "34av", new Vector3(-3.2f, 1.6f, 3.6f)); Photo(b, "34ar", new Vector3(3.0f, 1.8f, -3.8f));
                Photo(b, "cote", new Vector3(-4.6f, 0.9f, 0.2f)); Photo(b, "dessus", new Vector3(-1.5f, 4.5f, -3.0f));
            }
            // ([Test] CoqueEntree=1 : le joueur marche vers la place du conducteur, portiere ouverte, de 36 a 44 s, a la place de monter au volant : entre-t-il ?)
            if (host && cStep == 2 && t > 36f && b != null && Config.GetInt("Test", "CoqueEntree", 0) != 0)
            {
                var ce = pl.GetComponent<CharacterController>();
                if (ce != null)
                {
                    if (!cEntree)
                    {
                        cEntree = true; LayerRows(); ce.enabled = false; st.DoorOpen[0] = true; st.DoorAngle[0] = 70f; st.DoorVel[0] = 0f; DoorAnim(b, st);
                        pl.transform.position = car.TransformPoint(new Vector3(-2.6f, 0.4f, st.DoorBox[0].center.z - 0.1f)); ce.enabled = true;
                        Log.Info("standalone : entree, depart " + car.InverseTransformPoint(pl.transform.position).ToString("F2") + ", marche du joueur " + ce.stepOffset.ToString("F2") + " m");
                    }
                    Vector3 go = car.TransformPoint(new Vector3(b.M.Head.x, 0f, st.DoorBox[0].center.z - 0.1f)) - pl.transform.position; go.y = 0f;
                    if (t >= cEntreeLog) { cEntreeLog = t + 1f; Log.Info("standalone : entree, en marche " + car.InverseTransformPoint(pl.transform.position).ToString("F2") + ", capsule " + ce.height.ToString("F2")); }
                    if (t < 44f) ce.Move(go.normalized * Mathf.Min(1.5f * Time.deltaTime, go.magnitude) + Vector3.down * 2f * Time.deltaTime);
                    else
                    {
                        // (debout a la place : pieds et yeux ; puis assis par le vrai chemin, tete gardee ou elle etait)
                        cStep = 3; Transform ec = PlayerCam(); Vector3 ep = car.InverseTransformPoint(pl.transform.position), ee = ec != null ? car.InverseTransformPoint(ec.position) : Vector3.zero;
                        Log.Info("standalone : entree a pied, arrive en " + ep.ToString("F2") + " (place : x " + b.M.Head.x.ToString("F2") + ")" + (Mathf.Abs(ep.x - b.M.Head.x) < 0.15f ? " : ENTRE" : " : BLOQUE") + ", yeux debout " + ee.ToString("F2") + ", yeux du siege " + st.T.Yeux.ToString("F2") + ", marche " + ce.stepOffset.ToString("F2") + ", capsule " + ce.height.ToString("F2") + " centre " + ce.center.y.ToString("F2"));
                        Enter(b, st); cEntreeAt = t;
                    }
                }
            }
            if (host && cEntreeAt > 0f && t > cEntreeAt + 2.5f && b != null && st != null)
            {
                cEntreeAt = 0f; Transform ec = PlayerCam();
                Log.Info("standalone : entree, assis, yeux " + (ec != null ? car.InverseTransformPoint(ec.position).ToString("F2") : "?") + " pour " + st.T.Yeux.ToString("F2") + ", tete gardee " + st.HasEyeHold + " " + st.EyeHold.ToString("F2") + " ; ecart " + (st.HasEyeHold ? (st.EyeHold - st.T.Yeux).ToString("F2") : "0"));
                Photo(b, "entree-" + b.M.Folder, ec != null ? car.InverseTransformPoint(ec.position) : st.T.Yeux, (ec != null ? car.InverseTransformPoint(ec.position) : st.T.Yeux) + new Vector3(0f, -0.08f, 1f), 60f);
                Log.Info("standalone : entree, fini");
            }
            string tourIds = host ? Config.Get("Test", "CoqueTour", "") : "";
            if (tourIds.Length > 0 && cStep == 2 && t > 32f) { Tour(t, tourIds, car, b, st, pl); return; }
            if (host && cStep == 2 && t > 36f && b != null && Config.GetInt("Test", "CoqueEntree", 0) == 0)
            {
                cStep = 3;
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                pl.transform.position = car.TransformPoint(new Vector3(b.M.Head.x, 0.5f, st.T.EntreeZ));
                if (cc != null) cc.enabled = true;
                Enter(b, st); SetEngine(b, true); st.LightsOn = true; SendState(b, st);
                Log.Info("standalone : coop, hote au volant de " + car.name + ", moteur, phares");
            }
            if (!host && cStep == 1 && t > 44f && car != null)
            {
                cStep = 3;
                Log.Info("standalone : coop, invite : copie " + car.name + (b != null ? " standalone" : " PAS standalone") + ", passager -> " + Seats.TestSit(car.name, Config.GetInt("Test", "StandalonePlace", 0)));   // ([Test] StandalonePlace=1 : banquette)
            }
            // ([Test] StandaloneCeinturePassager=1 : l'invite boucle sa ceinture 1 s apres s'etre assis -- photos de l'hote avec la ceinture)
            if (!host && cStep >= 3 && !cBeltDone && t > 45f && Config.GetInt("Test", "StandaloneCeinturePassager", 0) != 0) { cBeltDone = true; Log.Info("standalone : coop, invite : " + Seats.TestStandaloneBelt()); }
            // ([Test] StandalonePhotoConducteur=1, JD 09/11 : jambes du conducteur comme dans l'outil des places -- l'invite assis, a 54 s :
            // portiere conducteur ouverte sur sa copie seulement, photos du conducteur (l'hote) par la portiere, de l'arriere gauche et depuis
            // la place passager)
            if (!host && cStep >= 3 && !cPhotoCond && t > 54f && b != null && st != null && Config.GetInt("Test", "StandalonePhotoConducteur", 0) != 0)
            {
                cPhotoCond = true;
                if (st.Doors != null && st.Doors[0] != null) { st.DoorOpen[0] = true; st.DoorAngle[0] = 75f; st.DoorVel[0] = 0f; DoorAnim(b, st); }
                Vector3 e = st.T.Yeux;
                Photo(b, "conducteur-porte", new Vector3(e.x - 1.6f, e.y - 0.2f, e.z + 0.3f), new Vector3(e.x, e.y - 0.6f, e.z + 0.3f), 60f);
                Photo(b, "conducteur-arriere", new Vector3(e.x - 1.3f, e.y - 0.25f, e.z - 0.9f), new Vector3(e.x, e.y - 0.65f, e.z + 0.45f), 60f);
                Photo(b, "conducteur-passager", new Vector3(-e.x, e.y + 0.02f, e.z - 0.1f), new Vector3(e.x + 0.05f, e.y - 0.8f, e.z + 0.55f), 75f);
                Log.Info("standalone : coop, invite : photos du conducteur (" + car.name + ", yeux " + e.ToString("F2") + ")");
                // (et sa pose, au format du releve de l'outil -- [Test] PlacesPoses=<dossier>)
                foreach (Avatar a in PlayerSync.Avatars)
                    if (a.Root != null && a.Player != null && (a.Root.transform.position - car.position).sqrMagnitude <= 9f && Seats.RemoteSeatIndex(a.Player.Id) < 0)
                        DumpDriverPose(a, car, Vaz.SeatKey(car.name) ?? car.name);
            }
            // ([Test] StandalonePortierePassager=1, desync du 02/11 : l'invite assis (StandalonePlace=-1 : reste a pied sur la piste) ouvre
            // la portiere passager de sa copie 3 s apres, la referme 7 s apres (meme chemin que le clic), angle toutes les 0,5 s jusqu'a
            // 11 s ; l'hote, au volant a l'arret, note chaque changement de la portiere et ne roule qu'apres l'avoir vue refermee (115 s au
            // plus). Attendu : fermee et angle 0 des deux cotes ; PortiereCopieFermee=0 : ouverte des deux cotes (bug))
            bool portTest = Config.GetInt("Test", "StandalonePortierePassager", 0) != 0 && b != null && st != null && st.Doors != null && st.Doors[1] != null;
            if (portTest && !host && cStep >= 3)
            {
                if (cDoor == 0) { cDoor = 1; cDoorAt = t; }
                if (cDoor == 1 && t > cDoorAt + 3f) { cDoor = 2; ToggleDoor(b, st, 1); Log.Info("standalone : coop, invite ouvre : " + DoorState(b, st, 1)); }
                if (cDoor == 2 && t > cDoorAt + 5f) { cDoor = 3; Log.Info("standalone : coop, invite, ouverte depuis 2 s : " + DoorState(b, st, 1)); }
                if (cDoor == 3 && t > cDoorAt + 7f) { cDoor = 4; ToggleDoor(b, st, 1); Log.Info("standalone : coop, invite ferme : " + DoorState(b, st, 1)); cDoorLog = t + 0.5f; }
                if (cDoor == 4 && t >= cDoorLog) { cDoorLog = t + 0.5f; Log.Info("standalone : coop, invite, " + (t - cDoorAt - 7f).ToString("F1") + " s apres la fermeture : " + DoorState(b, st, 1)); if (t > cDoorAt + 11f) cDoor = 5; }
            }
            if (portTest && host && cStep >= 3)
            {
                if (st.DoorOpen[1] != cDoorWas) { if (cDoorWas) { cDoorSeen = true; cDoorAt = t; } cDoorWas = st.DoorOpen[1]; Log.Info("standalone : coop, hote voit : " + DoorState(b, st, 1)); }
                if (cDoorSeen && cDoor == 0 && t > cDoorAt + 2f) { cDoor = 1; Log.Info("standalone : coop, hote, 2 s apres la fermeture : " + DoorState(b, st, 1)); }
            }
            // ([Test] StandalonePhotosPlaces=1 : a 58 s, les avatars assis -- chez l'hote, le passager (l'invite) ; chez l'invite, le
            // conducteur (l'hote) : par la vitre de cote, par le pare-brise, de l'arriere ; StandalonePhotosHote=s : heure des photos de l'hote,
            // 73 par defaut -- l'invite s'assoit a ses 44 s, parfois apres les 73 s de l'hote)
            if (Config.GetInt("Test", "StandalonePhotosPlaces", 0) != 0 && b != null && t > (host ? Config.GetFloat("Test", "StandalonePhotosHote", 73f) : 58f) && !cSeatShots)
            {
                cSeatShots = true; float sx = host ? 1f : -1f; string w = host ? "passager" : "conducteur";
                if (host && Config.GetInt("Test", "StandalonePlace", 0) > 0)
                {
                    Photo(b, "arriere-cote", new Vector3(-1.8f, 1.1f, -0.9f), new Vector3(-0.3f, 0.85f, -0.9f));
                    Photo(b, "arriere-face", new Vector3(0.3f, 1.2f, 0.35f), new Vector3(-0.3f, 0.85f, -0.95f));
                }
                Photo(b, w + "-cote", new Vector3(sx * 1.7f, 1.1f, -0.2f), new Vector3(sx * 0.33f, 0.8f, -0.2f));
                Photo(b, w + "-face", new Vector3(sx * 0.3f, 1.25f, 1.7f), new Vector3(sx * 0.33f, 0.85f, -0.25f));
                Photo(b, w + "-profil", new Vector3(-sx * 0.05f, 1.05f, 0.35f), new Vector3(sx * 0.4f, 0.75f, -0.35f));
            }
            // ([Test] StandalonePlanche=1 : a 42 s, l'hote au volant, planche figee -- 7 500 tr/min, 100 km/h, turbo aux 9/10 du maxi, 4e
            // affichee, voyant de regime allume -- et vue de ses yeux vers le compte-tours : compteurs, afficheur et voyant, JD 03/11)
            if (host && b != null && st != null && driving == b && t > 42f && !cPlanche && Config.GetInt("Test", "StandalonePlanche", 0) != 0)
            {
                cPlanche = true; gaugeFreeze = true; st.LightsOn = false;
                if (st.Speedo != null) st.Speedo.Set(100f);
                if (st.Tacho != null) st.Tacho.Set(7500f);
                if (st.Turbo != null) st.Turbo.Set(st.Turbo.Max > 0f ? st.Turbo.Max * 0.9f : st.Turbo.V1 * 0.5f);
                int n0 = b.Dt.gearRatios != null ? Array.IndexOf(b.Dt.gearRatios, 0f) : 1, g0 = b.Dt.gear;
                b.Dt.gear = n0 + 4; st.GearShown = -99; GearDisplay(b, st); b.Dt.gear = g0;
                if (st.RevLamp != null) st.RevLamp.SetActive(true);
                Vector3 at = st.Tacho != null ? b.Car.transform.InverseTransformPoint(st.Tacho.Needle.position) : st.T.Yeux + Vector3.forward;
                foreach (Gauge g in new[] { st.Speedo, st.Tacho, st.Turbo }) if (g != null) g.Light(false);
                Photo(b, "planche", st.T.Yeux, at, 38f);
                // (meme vue phares allumes : compteurs eclaires)
                foreach (Gauge g in new[] { st.Speedo, st.Tacho, st.Turbo }) if (g != null) g.Light(true);
                Photo(b, "planche-phares", st.T.Yeux, at, 38f);
                // ([Test] StandalonePlanche=2 : aussi la console, centree sur le contact et les phares -- commandes posees sur le modele)
                VazCar.Knob[] kz = KnobsOf(b.M);
                if (Config.GetInt("Test", "StandalonePlanche", 0) >= 2 && kz != null && kz.Length > 2)
                    Photo(b, "planche-boutons", st.T.Yeux, (kz[0].At + kz[2].At + kz[kz.Length - 1].At) / 3f, 34f);
                Log.Info("standalone : planche figee (rapport affiche " + (st.GearTex != null) + ", voyant " + (st.RevLamp != null) + ", turbo " + (st.Turbo != null) + ")");
            }
            // ([Test] StandaloneRetros=1 : a 70 s, l'hote au volant, chaque glace vue de ses yeux, zoomee -- retroviseurs fonctionnels)
            if (host && b != null && st != null && st.Mirrors != null && driving == b && t > 70f && !cMirrorShots && Config.GetInt("Test", "StandaloneRetros", 0) != 0)
            {
                cMirrorShots = true; Transform pc = PlayerCam();
                if (pc != null)
                    foreach (Mirror m in st.Mirrors)
                        if (m != null && m.Glass != null)
                        {
                            Log.Info("standalone : retro " + m.Key + " " + (m.Mr.sharedMaterial == m.Live ? "vivant" : "sombre") + ", glace " + b.Car.transform.InverseTransformPoint(m.Glass.position).ToString("F2"));
                            Photo(b, "retro-" + m.Key, b.Car.transform.InverseTransformPoint(pc.position), b.Car.transform.InverseTransformPoint(m.Glass.position), m.Key == "i" ? 22f : 14f);
                        }
            }
            // ([Test] StandaloneChauffage=1 : a 40 s, l'hote au volant met le chauffage a fond (marche, ventilateur 3, chaud, direction 0,3) ;
            // etat toutes les 10 s jusqu'a 100 s (liquide, habitacle, source de chaleur, PlayerTemp) ; a 70 et 72 s, la console vue des yeux
            // (reglages a fond puis ventilateur 1, froid, direction en bas) -- chez l'invite aussi l'etat recu)
            if (b != null && st != null && Config.GetInt("Test", "StandaloneChauffage", 0) != 0)
            {
                if (host && driving == b && t > 40f && cHeat == 0) { cHeat = 1; TestHeater(b.Car, true, 3, 0f, 0.7f); }
                if (t > 40f && t < 101f && t >= cHeatLog)
                {
                    cHeatLog = t + 10f; HutongGames.PlayMaker.FsmFloat pt = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat("PlayerTemp");
                    Log.Info("standalone : coop, " + HeaterState(b.Car) + (pt != null ? ", PlayerTemp " + pt.Value.ToString("F2") : ""));
                }
                Transform pc = PlayerCam();
                if (host && driving == b && pc != null && cHeat == 1 && t > 70f) { cHeat = 2; HeatShots(b, st, "a-fond"); TestHeater(b.Car, true, 1, 1f, 1f); }
                if (host && driving == b && pc != null && cHeat == 2 && t > 72f) { cHeat = 3; HeatShots(b, st, "froid"); TestHeater(b.Car, true, 3, 0f, 0.7f); }
            }
            // ([Test] CoqueBalayage=s : a partir de s secondes, le regard balaie l'habitacle depuis trois places. Ce que la main du jeu a retenu (PickedPart : nom de l'objet
            // vise a 1 m, moins 7 signes) est lu a chaque vue. Sa visee (MousePick) part de Camera.main et de la souris : camera d'essai, tournee pour que CE rayon soit celui voulu,
            // car celle du joueur est replacee par le jeu et la souris d'une fenetre hors ecran est n'importe ou.)
            float swAt = Config.GetFloat("Test", "CoqueBalayage", 0f);
            if (host && swAt > 0f && t > swAt && b != null && st != null && swView >= 0)
            {
                HutongGames.PlayMaker.FsmString gt = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString("PickedPart");
                if (gt != null)
                {
                    if (swCam == null)
                    {
                        swOld = Camera.main; if (swOld != null) swOld.tag = "Untagged";
                        GameObject swg = new GameObject("essai_regard"); swg.tag = "MainCamera";
                        swCam = swg.AddComponent<Camera>(); swCam.cullingMask = 0; swCam.clearFlags = CameraClearFlags.Nothing; swCam.depth = -100f; swCam.nearClipPlane = 0.01f;
                    }
                    Vector3[] swAtP = { new Vector3(-0.32f, 1.05f, -0.2f), new Vector3(0.32f, 1.05f, -0.2f), new Vector3(0f, 0.95f, 0.25f) };
                    const int Yaws = 12, Pitches = 7;
                    int total = swAtP.Length * Yaws * Pitches;
                    if (swView > 0 && swFrames == 3)
                    {
                        if (!string.IsNullOrEmpty(gt.Value)) swTexts.Add(gt.Value);
                        int swMask = (1 << LayerMask.NameToLayer("HingedObjects")) | (1 << LayerMask.NameToLayer("Parts")) | (1 << LayerMask.NameToLayer("Collider2"));
                        RaycastHit swh;
                        if (Physics.Raycast(swCam.transform.position, swDir, out swh, 1f, swMask)) { swHitN++; swHits.Add(swh.collider.name + " (" + LayerMask.LayerToName(swh.collider.gameObject.layer) + ")"); }
                    }
                    if (swView >= total)
                    {
                        var sbt = new System.Text.StringBuilder(); foreach (string s0 in swTexts) sbt.Append(" [").Append(s0).Append("]");
                        var sbh = new System.Text.StringBuilder(); foreach (string s0 in swHits) sbh.Append(" [").Append(s0).Append("]");
                        Log.Info("standalone : balayage, " + total + " vues, " + swHitN + " sur la coque ; texte retenu par la main du jeu :" + (sbt.Length > 0 ? sbt.ToString() : " aucun") + " ; boites :" + sbh);
                        swView = -1;
                        UnityEngine.Object.Destroy(swCam.gameObject); if (swOld != null) swOld.tag = "MainCamera";
                    }
                    else
                    {
                        int pi = swView / (Yaws * Pitches), rest = swView % (Yaws * Pitches);
                        swDir = b.Car.transform.rotation * Quaternion.Euler(-60f + 20f * (rest % Pitches), 30f * (rest / Pitches), 0f) * Vector3.forward;
                        Vector3 swPos = b.Car.transform.TransformPoint(swAtP[pi]);
                        swCam.transform.position = swPos; swCam.transform.rotation = Quaternion.identity;
                        swCam.transform.rotation = Quaternion.FromToRotation(swCam.ScreenPointToRay(Input.mousePosition).direction, swDir);
                        swCam.transform.position += swPos - swCam.ScreenPointToRay(Input.mousePosition).origin;
                        swFrames++;
                        if (swFrames >= 4) { swFrames = 0; swView++; }
                    }
                }
            }
            // ([Test] CoqueCoupe=s : vues en coupe de la voiture a l'arret -- de cote au milieu et a la place du passager, de dessus sous le toit ; boites de la coque en couleur,
            // vert les parois, orange les sieges ; puis la meme coupe sans les boites)
            float cpAt = Config.GetFloat("Test", "CoqueCoupe", 0f);
            if (host && cpAt > 0f && t > cpAt && !cCoupe && b != null && st != null)
            {
                cCoupe = true; LayerRows(); Vector3 cc = st.Cabin.center; string cn = b.M.Folder; int boxes = 0;
                var lg = new GameObject("essai_lampe"); lg.transform.SetParent(b.Car.transform, false); lg.transform.localPosition = new Vector3(1.6f, cc.y + 0.6f, cc.z);
                Light li = lg.AddComponent<Light>(); li.type = LightType.Point; li.range = 7f; li.intensity = 2f;
                var made = new List<GameObject>();
                foreach (BoxCollider bc in b.Car.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (bc.name != "coque_x") continue;
                    boxes++;
                    GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.transform.SetParent(bc.transform, false); g.transform.localPosition = bc.center; g.transform.localScale = bc.size;
                    Material m = new Material(Shader.Find("Unlit/Color")); m.color = bc.gameObject.layer == seatItemLayer ? new Color(1f, 0.5f, 0.05f) : new Color(0.1f, 0.85f, 0.25f);
                    g.GetComponent<Renderer>().sharedMaterial = m; made.Add(g);
                }
                Photo(b, "coupe-" + cn + "-milieu", new Vector3(4f, cc.y, cc.z), new Vector3(0f, cc.y, cc.z), 38f, 4f);
                Photo(b, "coupe-" + cn + "-place", new Vector3(4f, cc.y, cc.z), new Vector3(0f, cc.y, cc.z), 38f, 3.65f);
                Photo(b, "coupe-" + cn + "-dessus", new Vector3(0f, 5f, cc.z), new Vector3(0f, 0f, cc.z + 0.01f), 38f, 5f - (st.Cabin.max.y - 0.1f));
                foreach (GameObject g in made) g.SetActive(false);
                Photo(b, "coupe-" + cn + "-nu", new Vector3(4f, cc.y, cc.z), new Vector3(0f, cc.y, cc.z), 38f, 3.65f);
                foreach (GameObject g in made) UnityEngine.Object.Destroy(g);
                UnityEngine.Object.Destroy(lg);
                Log.Info("standalone : coupes de " + cn + ", " + boxes + " boites, habitacle " + st.Cabin.min.ToString("F2") + " - " + st.Cabin.max.ToString("F2"));
            }
            // ([Test] PneuPhoto=s : photos d'une roue avant, chez chaque joueur -- de pres, de cote, de face -- et nombre de pneus du jeu poses)
            float ppAt = Config.GetFloat("Test", "PneuPhoto", 0f);
            if (ppAt > 0f && t > ppAt && !cPneu && b != null)
            {
                cPneu = true; string who = b.M.Folder + (host ? "-hote" : "-invite");
                Vector3 wl = new Vector3(-0.75f, 0.32f, 1.2f); int snow = 0;
                foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>(true)) { Vector3 q = b.Car.transform.InverseTransformPoint(w.transform.position); if (q.x < 0f && q.z > 0f) wl = q; }
                foreach (Transform x in b.Car.GetComponentsInChildren<Transform>(true)) if (x.name == "pneu_neige") snow++;
                Photo(b, "pneu-" + who + "-roue", wl + new Vector3(-1.35f, 0.12f, 0.2f), wl, 28f);
                Photo(b, "pneu-" + who + "-cote", new Vector3(-6f, 0.9f, 0f), new Vector3(0f, 0.6f, 0f), 30f);
                Photo(b, "pneu-" + who + "-face", new Vector3(-1.9f, 0.55f, 4.4f), new Vector3(-0.2f, 0.5f, 0.6f), 30f);
                Log.Info("pneus : fini, " + b.M.Name + ", " + snow + " pneus du jeu sur la voiture ici (" + who + ")");
            }
            // ([Test] CoqueMesure=1 : temps moyen par image de 60 a 80 s, voiture a l'arret moteur tourne, a comparer avec CoqueSans=1 : sans la coque)
            if (host && Config.GetInt("Test", "CoqueMesure", 0) != 0 && t > 60f && cMesN >= 0)
            {
                if (t < 80f) { cMesT += Time.unscaledDeltaTime; cMesN++; }
                else { Log.Info("standalone : mesure, " + cMesN + " images, " + (cMesN > 0 ? cMesT / cMesN * 1000f : 0f).ToString("F2") + " ms par image, physique " + Time.fixedDeltaTime + " s"); cMesN = -1; }
            }
            // ([Test] StandaloneObjets=1 : a 50 s, trois cubes du jeu (couche Parts, 2 kg) poses dans la voiture -- siege passager, plancher
            // arriere, coffre ; releves a 70 s (arret) et 88 s (apres le roulage et le freinage) : toujours dedans ?)
            if (host && b != null && st != null && Config.GetInt("Test", "StandaloneObjets", 0) != 0)
            {
                if (cObjets == null && t > 50f)
                {
                    cObjets = new List<GameObject>(); int parts = LayerMask.NameToLayer("Parts");
                    Vector3[] at = { st.HasSeatRef[1] ? st.SeatRef[1] + new Vector3(0f, 0.15f, 0.25f) : new Vector3(Mathf.Abs(b.M.Head.x), b.M.Head.y - 0.40f, b.M.Head.z + 0.22f),
                                     new Vector3(-0.2f, st.Cabin.min.y + 0.12f, st.HasSeatRef[2] ? st.SeatRef[2].z + 0.55f : st.Cabin.min.z + 0.2f),
                                     new Vector3(0.1f, st.HasSeatRef[2] ? st.Cabin.min.y + 0.35f : st.Cabin.max.y - 0.2f, st.HasSeatRef[2] ? -1.45f : st.Cabin.min.z - 0.35f),
                                     new Vector3(0f, 1.8f, st.Cabin.center.z) };   // (le 4e tombe sur le toit)
                    foreach (Vector3 p in at)
                    {
                        var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "essai_objet"; g.layer = parts; g.transform.localScale = Vector3.one * 0.15f;
                        g.transform.position = b.Car.transform.TransformPoint(p); g.AddComponent<Rigidbody>().mass = 2f; cObjets.Add(g);
                    }
                    Log.Info("standalone : coop, objets poses dans la voiture : " + cObjets.Count);
                }
                if (cObjets != null && (t > 70f && cObjLog == 0 || t > 88f && cObjLog == 1))
                {
                    cObjLog++; var sb = new System.Text.StringBuilder();
                    foreach (GameObject g in cObjets) { Vector3 lp = b.Car.transform.InverseTransformPoint(g.transform.position); bool inside = Mathf.Abs(lp.x) < 0.75f && lp.y > -0.2f && lp.y < 1.7f && lp.z > -2f && lp.z < 1.2f; sb.Append(lp.ToString("F2")).Append(inside ? " dedans" : " DEHORS").Append(" ; "); }
                    Log.Info("standalone : coop, objets (" + (cObjLog == 1 ? "a l'arret" : "apres roulage") + ") : " + sb);
                }
            }
            // ([Test] StandaloneEssuieMarche=66 : essuie-glaces mis en marche a ce temps -- pluie sur les vitres, captures avant / apres)
            float wOn = Config.GetFloat("Test", "StandaloneEssuieMarche", 0f);
            if (host && wOn > 0f && st != null && !st.WipersOn && t > wOn && t < wOn + 1f) { st.WipersOn = true; Log.Info("standalone : coop, essuie-glaces en marche"); }
            // (texture de simulation de la pluie enregistree a wOn + 6 s : dumps/pluie-rt.png)
            if (host && wOn > 0f && st != null && st.Rain != null && t > wOn + 6f && !wDump)
            {
                wDump = true;
                foreach (Component rc in new[] { st.Rain, st.Rain2 })   // (pluie-rt.png : pare-brise et cotes ; pluie-rt-lunette.png)
                {
                var rt = rc != null ? RF(rc, "active").GetValue(rc) as RenderTexture : null;
                if (rt != null)
                {
                    RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
                    var tx = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tx.Apply();
                    RenderTexture.active = prev;
                    string f = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), rc == st.Rain ? "pluie-rt.png" : "pluie-rt-lunette.png");
                    System.IO.File.WriteAllBytes(f, tx.EncodeToPNG()); UnityEngine.Object.Destroy(tx);
                    Log.Info("standalone : coop, texture de pluie " + rt.width + "x" + rt.height + " -> " + f);
                }
                }
            }
            if (host && wOn > 0f && st != null && t > wOn + 2f && Time.time >= wLog) { wLog = Time.time + 1f; Log.Info("standalone : coop, essuie-glaces " + st.WipersOn + " phase " + st.WiperPhase.ToString("F2") + " balai " + (st.Wipers[0] != null ? st.Wipers[0].localEulerAngles.ToString("F0") : "absent") + " dt " + Time.deltaTime.ToString("F3")); }
            // ([Test] StandaloneSons=1 : l'hote au volant, voiture arretee : portiere, hayon, frein a main, essuie-glaces ; chaque joueur note au journal le son qu'il joue)
            if (host && cStep == 3 && b != null && st != null && cSon < 8 && t > 48f + cSon * 3f && Config.GetInt("Test", "StandaloneSons", 0) != 0)
            {
                var ds = (StandaloneDriver)b.Axis;
                switch (cSon)
                {
                    case 0: case 1: if (st.Doors != null && st.Doors[0] != null) ToggleDoor(b, st, 0); if (cSon == 0) foreach (VazCar.Built bh in cars) if (bh.M != null && bh.M.QuestOnly) { Photo(bh, "capot", new Vector3(0f, 1.6f, 2.6f), new Vector3(0f, 0.85f, 1.0f)); Photo(bh, "capot-g", new Vector3(-0.55f, 1.25f, 1.75f), new Vector3(-0.55f, 0.85f, 1.05f)); Photo(bh, "ext-34av", new Vector3(-3.2f, 1.6f, 3.6f)); Photo(bh, "ext-34ar", new Vector3(3.0f, 1.8f, -3.8f)); Photo(bh, "int-planche", new Vector3(-0.05f, 1.02f, -0.35f), new Vector3(-0.2f, 0.78f, 0.75f), 60f); Photo(bh, "int-volant", new Vector3(-0.3f, 1.0f, -0.1f), new Vector3(-0.3f, 0.8f, 0.5f), 55f); Photo(bh, "int-porte", new Vector3(0.3f, 1.0f, 0.15f), new Vector3(-0.7f, 0.65f, 0.3f), 65f); Photo(bh, "int-sieges", new Vector3(0f, 1.08f, 0.62f), new Vector3(0f, 0.68f, -0.6f), 65f); Photo(bh, "int-sol", new Vector3(0.28f, 0.95f, 0.1f), new Vector3(-0.28f, 0.28f, 0.42f), 65f); Photo(bh, "int-plafond", new Vector3(0f, 0.8f, 0f), new Vector3(0f, 1.3f, -0.3f), 70f); } break;
                    case 2: case 3: foreach (VazCar.Built bh in cars) { State sh; if (states.TryGetValue(bh, out sh) && sh.Hatch != null) { sh.HatchOpen = !sh.HatchOpen; SendState(bh, sh); Log.Info("standalone : coop, hayon de " + bh.Car.name + (sh.HatchOpen ? " ouvert" : " ferme")); } } break;
                    case 4: ds.HandLatch = 1f; break;
                    case 5: ds.HandLatch = 0f; break;
                    case 6: st.WipersOn = true; SendState(b, st); break;
                    case 7: Log.Info("standalone : coop, essuie-glaces, son " + (st.WiperSrc != null ? (st.WiperSrc.isPlaying ? "joue" : "ARRET") + " a " + st.WiperSrc.time.ToString("F2") + " s, hauteur " + st.WiperSrc.pitch.ToString("F2") : "ABSENT")); st.WipersOn = false; SendState(b, st); break;
                }
                cSon++;
            }
            bool gearTest = b != null && st != null && b.Dt != null && b.Dt.gearRatios != null && Config.GetInt("Test", "StandaloneRapports", 0) != 0;
            if (gearTest) GearsTest(b, st, host, t);
            bool cmdTest = b != null && st != null && Config.GetInt("Test", "StandaloneCommandes", 0) != 0;
            if (cmdTest) CommandsTest(b, st, host, t, pl);
            if (host && cStep == 3 && t > 75f && t < 76.5f && !cmdTest) SetHorn(b, st, true, true);
            if (host && cStep == 3 && t > 78f && !(portTest && !cDoorSeen && t < 115f) && !(gearTest && cGearI < b.Dt.gearRatios.Length) && !cmdTest)
            {
                cStep = 4; cAt = t; SetHorn(b, st, false, true); var d = (StandaloneDriver)b.Axis; d.Keys = false; d.Handbrake = 0f; d.Gear = 2; d.Throttle = 0.6f;
                // ([Test] StandaloneFrein=1 : frein a main serre au levier les 2,5 premieres secondes, gaz ouverts -- puis desserre)
                if (Config.GetInt("Test", "StandaloneFrein", 0) != 0) { d.HandLatch = 1f; cFrein = true; }
                Log.Info("standalone : coop, hote roule" + (cFrein ? " (frein a main serre au levier)" : ""));
            }
            if (host && cStep == 4 && cFrein && t > cAt + 2.5f)
            {
                cFrein = false; var d = (StandaloneDriver)b.Axis;
                Log.Info("standalone : coop, frein a main serre 2,5 s gaz ouverts : " + (b.Rb.velocity.magnitude * 3.6f).ToString("F1") + " km/h, entree " + d.handbrakeInput.ToString("F2") + " ; desserre");
                d.HandLatch = 0f;
            }
            if (host && cStep == 4 && t > cAt + 5f) { cStep = 5; var d = (StandaloneDriver)b.Axis; d.Throttle = 0f; d.Brake = 1f; Log.Info("standalone : coop, hote freine (" + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + " km/h)"); }
            if (!host && cStep == 3 && t > 46f && t < 100f && (t >= cLog || (st != null && st.HornOn && t >= cLog - 1f)) && car != null)
            {
                cLog = t + 1.5f;
                Log.Info("standalone : coop, invite voit " + car.name + " en " + car.position.ToString("F1") + ", " + (car.GetComponent<Rigidbody>().isKinematic ? "cinematique" : "PHYSIQUE ICI")
                         + (st != null ? ", compteur " + st.RemoteKmh.ToString("F0") + " km/h, moteur " + (st.EngineOn ? "oui" : "non") + ", phares " + (st.LightsOn ? "oui" : "non") + ", klaxon " + (st.HornOn ? (st.Horn != null && st.Horn.isPlaying ? "JOUE" : "vrai sans son") : "non") : "")
                         + ", regime " + (b != null ? b.Dt.rpm.ToString("F0") : "?") + ", joueur " + (pl.transform.IsChildOf(car) ? "dans la voiture" : "DEHORS"));
            }
            if (host && cStep == 5 && t > 90f && b != null)
            {
                cStep = 6;
                Vector3 p0 = car.position;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "MWCoop-MurEssai";
                int tl = LayerMask.NameToLayer("Terrain"); if (tl >= 0) wall.layer = tl;
                wall.transform.position = p0 + car.forward * 9f + Vector3.up; wall.transform.rotation = Quaternion.LookRotation(car.forward); wall.transform.localScale = new Vector3(20f, 3f, 1f);
                var d = (StandaloneDriver)b.Axis; d.Brake = 0f; d.Handbrake = 0f; d.Gear = 4; d.Throttle = 1f;
                b.Rb.velocity = car.forward * 17.5f; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>()) w.angularVelocity = 17.5f / Mathf.Max(0.1f, w.radius); if (st.Sensor != null) st.Sensor.Reset();
                enteredAt = Time.realtimeSinceStartup - 2f;
                Log.Info("standalone : coop, choc a 60 km/h contre un mur");
            }
            if (cStep >= 1 && cStep < 9 && t > (host ? 120f : 105f)) { cStep = 9; Log.Info("standalone : coop, " + (host ? "hote" : "invite") + " vivant apres la reapparition (" + pl.transform.position.ToString("F0") + ")"); }
            if (cStep == 9 && t > (host ? 125f : 110f)) { cStep = 10; Log.Info("standalone : coop fini"); }
        }
    }
}
