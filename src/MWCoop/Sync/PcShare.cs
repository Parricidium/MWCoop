using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Ordinateur de la maison partage (decision de JD, 12/11 : « 1 seul joueur peut l'utiliser a la fois mais les autres joueurs voient ce
    // que voit a l'ecran le joueur qui l'utilise »). Module commun aux deux jeux ; My Summer Car : YARD/Building/BEDROOM1/COMPUTER (My Winter
    // Car : a brancher par [MWC], Resolve()).
    //  - Un seul utilisateur. On s'assied par Computer/TriggerPlayMode :: PlayerTrigger ('Press return' -> touche DrivingMode -> 'Play',
    //    PlayerComputer vrai). L'hote decide qui l'a (fiable, a chaque changement et toutes les 5 s) ; tant qu'un autre l'a, la touche de
    //    'Press return' est coupee ici et le jeu affiche « occupe par X » ; assis quand meme (deux en meme temps), le refuse est releve
    //    (GLOBALEVENT, comme GetOut). Rendu en se levant, ou quand l'utilisateur part.
    //  - L'ecran se voit : chez l'utilisateur, l'image de COMPUTER/SYSTEM/Camera (sa texture de rendu) est reduite (Largeur x Hauteur),
    //    compressee en JPG et envoyee [Test] PcImagesSec fois par seconde, par morceaux sans garantie de livraison (jamais par le canal
    //    fiable), seulement aux joueurs a moins de 8 m de l'ordinateur ; plus personne pres d'ici ou ordinateur eteint (camera ou SYSTEM
    //    eteints) : rien n'est envoye. Chez eux, seule la derniere image complete compte (image en retard ou incomplete : sautee) ; elle est
    //    posee sur l'ecran de leur moniteur, rendu a son etat d'avant 2 s apres la derniere image.
    //  - Messages : canal « monde » generique Msg.MondeGenerique (meme valeur que Msg.Suski, nom historique), sortes 10 a 12 (un mod de la meme version reseau qui ne les connait pas les ignore sans bruit).
    // [Test] PcPartage=0 : comme avant (chacun son ordinateur, rien de partage).
    public static class PcShare
    {
        public const int K_USE = 10, K_OWNER = 11, K_IMG = 12;
        const int Chunk = 1000, None = 255;
        const float NearDist = 8f;
        static bool On { get { return Config.GetInt("Test", "PcPartage", 1) != 0; } }
        static int W { get { return Config.GetInt("Test", "PcLargeur", 256); } }
        static int H { get { return Config.GetInt("Test", "PcHauteur", 192); } }

        static Transform root, seat;
        static PlayMakerFSM trigger;
        static FsmStateAction keyDown;   // 'Press return' : GetButtonDown DrivingMode
        static Camera cam;
        static Renderer screenR; static int screenIdx = -1;
        static Material screenMat; static Texture screenOrig;
        static int owner = None;
        static bool usingHere, keyCut, shown;
        static float nextFind, nextOwnerSend, nextImg, lastImgAt, nextStat, toastAt;
        static RenderTexture small; static Texture2D grab, view;
        static ushort frameOut;
        // reception : image en cours
        static int inFrame = -1, inParts, inGot, inWho = -1; static byte[][] inBuf;
        static long bytesOut, bytesIn; static int framesOut, framesIn, framesSkipped;
        static readonly Dictionary<int, long> bytesTo = new Dictionary<int, long>();

        public static void OnLevelLoaded()
        {
            root = seat = null; trigger = null; keyDown = null; cam = null; screenR = null; screenIdx = -1; screenMat = null; screenOrig = null;
            owner = None; usingHere = keyCut = shown = false; nextFind = nextOwnerSend = nextImg = lastImgAt = nextStat = toastAt = 0f;
            inFrame = -1; inBuf = null; bytesOut = bytesIn = 0; framesOut = framesIn = framesSkipped = 0; bytesTo.Clear();
            testStep = 0; testNext = 0f; lastShot = 0f;
        }

        static string BasePath { get { return Game.Msc ? "YARD/Building/BEDROOM1/COMPUTER" : "COMPUTER"; } }

        static void Resolve()
        {
            GameObject r = Game.FindAny(BasePath);
            if (r == null) return;
            root = r.transform;
            Transform s = root.Find("Computer/TriggerPlayMode");
            if (s != null) { seat = s; trigger = Game.FsmOn(s.gameObject, "PlayerTrigger"); }
            Transform c = root.Find("SYSTEM/Camera");
            if (c != null) cam = c.GetComponent<Camera>();
            // ecran du moniteur : materiau dont le nom ou la texture dit « screen » / « 386_video » / « tv_off »
            Transform sys = root.Find("SYSTEM");
            foreach (Renderer rr in root.GetComponentsInChildren<Renderer>(true))
            {
                if (sys != null && rr.transform.IsChildOf(sys)) continue;
                Material[] ms = rr.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    Material m = ms[i];
                    if (m == null) continue;
                    string n = m.name.ToLowerInvariant(), tn = m.mainTexture != null ? m.mainTexture.name.ToLowerInvariant() : "";
                    if (n.Contains("screen") || tn.Contains("386_video") || tn.Contains("tv_off")) { screenR = rr; screenIdx = i; break; }
                }
                if (screenR != null) break;
            }
            FindKey();
            Log.Info("ordinateur partage : " + Recon.Path(root) + ", place " + (trigger != null ? trigger.ActiveStateName : "introuvable") + ", camera " + (cam != null ? (cam.targetTexture != null ? cam.targetTexture.name + " " + cam.targetTexture.width + "x" + cam.targetTexture.height : "sans texture") : "introuvable")
                     + ", ecran " + (screenR != null ? Recon.Path(screenR.transform) + "#" + screenIdx + " (" + screenR.sharedMaterials[screenIdx].name + ")" : "introuvable") + ", touche " + (keyDown != null ? "trouvee" : "introuvable"));
        }

        // Touche de 'Press return' : seulement une fois l'automate initialise (place eteinte tant que l'ordinateur n'est pas monte : lire
        // ses actions levait une exception, essai pc1).
        static void FindKey()
        {
            if (keyDown != null || trigger == null || !trigger.gameObject.activeInHierarchy || trigger.Fsm == null) return;
            try
            {
                foreach (FsmState st in trigger.Fsm.States)
                    if (st.Name == "Press return")
                        foreach (FsmStateAction a in st.Actions) if (a != null && a.GetType().Name == "GetButtonDown") keyDown = a;
            }
            catch (System.Exception e) { Log.Warn("ordinateur partage : place pas prete (" + e.GetType().Name + ")"); }
        }

        static string NameOf(int id) { PlayerInfo p; return Session.Players.TryGetValue(id, out p) ? p.Name : "#" + id; }

        public static void Update()
        {
            if (!On || !Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (root == null) { if (now < nextFind) return; nextFind = now + 5f; Resolve(); if (root == null) return; }
            if (keyDown == null && now >= nextFind) { nextFind = now + 2f; FindKey(); }
            bool inUse = trigger != null && trigger.ActiveStateName == "Play";
            if (inUse != usingHere)
            {
                usingHere = inUse;
                if (inUse)
                {
                    if (owner != None && owner != Session.LocalId) { Refuse(); return; }
                    Log.Info("ordinateur partage : assis ici, demande");
                    if (Session.IsHost) SetOwner(Session.LocalId); else Session.SendToHost(new NetWriter(Msg.MondeGenerique).U8(K_USE).U8(1), true);
                }
                else
                {
                    Log.Info("ordinateur partage : leve ici, rendu");
                    if (Session.IsHost) { if (owner == Session.LocalId) SetOwner(None); }
                    else Session.SendToHost(new NetWriter(Msg.MondeGenerique).U8(K_USE).U8(0), true);
                }
            }
            if (Session.IsHost)
            {
                if (owner != None && owner != Session.LocalId && !Session.Players.ContainsKey(owner)) SetOwner(None);   // (parti)
                if (now >= nextOwnerSend) { nextOwnerSend = now + 5f; SendOwner(); }
            }
            // un autre l'a : la touche pour s'asseoir est coupee ici
            bool cut = owner != None && owner != Session.LocalId;
            if (keyDown != null && keyDown.Enabled == cut) keyDown.Enabled = !cut;
            if (cut != keyCut) { keyCut = cut; Log.Info("ordinateur partage : " + (cut ? "occupe par " + NameOf(owner) + " : place fermee ici" : "libre : place ouverte ici")); }
            if (cut && trigger != null && trigger.ActiveStateName == "Press return" && now >= toastAt) { toastAt = now + 4f; Hud.Toast(Lang.T("Ordinateur occupé par ", "Computer in use by ") + NameOf(owner)); }
            if (usingHere && owner == Session.LocalId) SendImage(now);
            if (shown && now - lastImgAt > 2f) Unshow();
            if (now >= nextStat && (framesOut > 0 || framesIn > 0)) { nextStat = now + 10f; Stats(); }
        }

        static void Refuse()
        {
            Log.Info("ordinateur partage : deja utilise par " + NameOf(owner) + " : releve ici");
            Hud.Toast(Lang.T("Ordinateur occupé par ", "Computer in use by ") + NameOf(owner));
            usingHere = false;
            StandUp();
        }

        // Releve de la place comme GetOut ('Create player' : PlayerComputer, PlayerSeated... faux, puis 'Wait for player') -- etat force :
        // un evenement n'arrive pas a un automate dont l'objet est eteint, et la place restait en 'Play' (pc1 : refus a chaque image).
        static void StandUp()
        {
            if (trigger == null) return;
            Game.SetState(trigger, "Create player");
            if (trigger.ActiveStateName == "Play") Game.SetState(trigger, "Wait for player");
        }

        static void SetOwner(int id)
        {
            if (id == owner) return;
            owner = id;
            Log.Info("ordinateur partage : " + (id == None ? "libre" : "utilise par " + NameOf(id)));
            SendOwner();
        }

        static void SendOwner() { if (Session.IsHost && Session.RemoteCount > 0) Session.Broadcast(new NetWriter(Msg.MondeGenerique).U8(K_OWNER).U8(owner), true); }

        // ---------------------------------------------------------------- image
        static readonly List<int> near = new List<int>();
        static void NearPlayers()
        {
            near.Clear();
            Vector3 at = seat != null ? seat.position : root.position;
            foreach (PlayerInfo p in Session.Players.Values)
                if (!p.Local && p.Level == 1 && (p.State.Feet - at).sqrMagnitude < NearDist * NearDist) near.Add(p.Id);
        }

        static void SendImage(float now)
        {
            if (now < nextImg || cam == null) return;
            nextImg = now + 1f / Mathf.Max(0.5f, Config.GetFloat("Test", "PcImagesSec", 4f));
            if (!cam.gameObject.activeInHierarchy || !cam.enabled) return;   // (ordinateur eteint)
            NearPlayers();
            if (near.Count == 0) return;
            byte[] jpg = Capture();
            if (jpg == null) return;
            frameOut++;
            int parts = (jpg.Length + Chunk - 1) / Chunk;
            if (parts > 255) return;
            for (int i = 0; i < parts; i++)
            {
                int n = Mathf.Min(Chunk, jpg.Length - i * Chunk);
                var w = new NetWriter(Msg.MondeGenerique).U8(K_IMG).U8(Session.LocalId).U16(frameOut).U8(i).U8(parts).Bytes(jpg, i * Chunk, n);
                if (Session.IsHost) Session.SendTo(near, w, false); else Session.SendToHost(w, false);
            }
            framesOut++; bytesOut += jpg.Length;
            foreach (int id in near) { long b; bytesTo.TryGetValue(id, out b); bytesTo[id] = b + jpg.Length; }
        }

        static byte[] Capture()
        {
            RenderTexture src = cam.targetTexture;
            if (small == null || small.width != W || small.height != H) { small = new RenderTexture(W, H, 0); grab = new Texture2D(W, H, TextureFormat.RGB24, false); }
            RenderTexture prev = RenderTexture.active;
            try
            {
                if (src != null) Graphics.Blit(src, small);
                else { RenderTexture t = cam.targetTexture; cam.targetTexture = small; cam.Render(); cam.targetTexture = t; }
                RenderTexture.active = small;
                grab.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                grab.Apply(false);
            }
            finally { RenderTexture.active = prev; }
            return grab.EncodeToJPG(Config.GetInt("Test", "PcQualite", 55));
        }

        static void OnImage(Peer from, NetReader r, byte[] raw)
        {
            int who = r.U8(), frame = r.U16(), part = r.U8(), parts = r.U8();
            byte[] data = r.Bytes();
            if (Session.IsHost)
            {
                who = from.Id;
                if (who != owner) return;
                // relais aux autres joueurs pres de l'ordinateur ; et ici si l'hote est pres
                if (root == null) Resolve();
                if (root != null)
                {
                    NearPlayers(); near.Remove(who);
                    if (near.Count > 0) { Session.SendTo(near, new NetWriter(Msg.MondeGenerique).U8(K_IMG).U8(who).U16(frame).U8(part).U8(parts).Bytes(data, 0, data.Length), false); foreach (int id in near) { long b; bytesTo.TryGetValue(id, out b); bytesTo[id] = b + data.Length; } }
                    if (seat != null && Game.PlayerT != null && (Game.PlayerT.position - seat.position).sqrMagnitude > NearDist * NearDist) return;
                }
            }
            if (who == Session.LocalId) return;
            // derniere image seulement : une image plus recente remplace celle en cours (incomplete : sautee)
            if (frame != inFrame || who != inWho)
            {
                if (inBuf != null && inGot < inParts && IsNewer(frame, inFrame)) framesSkipped++;
                if (inFrame >= 0 && who == inWho && !IsNewer(frame, inFrame)) return;   // (en retard)
                inFrame = frame; inWho = who; inParts = parts; inGot = 0; inBuf = new byte[parts][];
            }
            if (part >= inParts || inBuf[part] != null) return;
            inBuf[part] = data; inGot++; bytesIn += data.Length;
            if (inGot < inParts) return;
            int len = 0; foreach (byte[] b in inBuf) len += b.Length;
            var all = new byte[len]; int at = 0;
            foreach (byte[] b in inBuf) { System.Buffer.BlockCopy(b, 0, all, at, b.Length); at += b.Length; }
            inBuf = null;
            Show(all);
        }

        static bool IsNewer(int a, int b) { return (short)(a - b) > 0; }

        static void Show(byte[] jpg)
        {
            if (root == null) Resolve();
            if (screenR == null) return;
            if (view == null) view = new Texture2D(W, H, TextureFormat.RGB24, false);
            if (!view.LoadImage(jpg)) return;
            if (!shown)
            {
                Material[] ms = screenR.materials;   // (instances : le materiau partage du jeu n'est pas touche)
                screenMat = ms[screenIdx]; screenOrig = screenMat.mainTexture;
                shown = true;
                Log.Info("ordinateur partage : ecran de " + NameOf(inWho) + " affiche ici");
            }
            screenMat.mainTexture = view;
            lastImgAt = Time.realtimeSinceStartup; framesIn++;
        }

        static void Unshow()
        {
            shown = false;
            if (screenMat != null) screenMat.mainTexture = screenOrig;
            Log.Info("ordinateur partage : plus d'image, ecran rendu ici");
        }

        static void Stats()
        {
            var sb = new System.Text.StringBuilder("ordinateur partage : ");
            if (framesOut > 0) { sb.Append(framesOut).Append(" image(s) envoyee(s), ").Append(bytesOut / Mathf.Max(1, framesOut)).Append(" o/image"); foreach (KeyValuePair<int, long> kv in bytesTo) sb.Append(", vers ").Append(NameOf(kv.Key)).Append(' ').Append((kv.Value / 10240f).ToString("F1")).Append(" Ko/s"); }
            if (framesIn > 0) sb.Append(framesOut > 0 ? " ; " : "").Append(framesIn).Append(" image(s) recue(s), ").Append((bytesIn / 10240f).ToString("F1")).Append(" Ko/s, ").Append(framesSkipped).Append(" sautee(s)");
            Log.Info(sb.ToString());
            framesOut = framesIn = framesSkipped = 0; bytesOut = bytesIn = 0; bytesTo.Clear();
        }

        // ---------------------------------------------------------------- messages
        public static void OnMessage(int kind, Peer from, NetReader r, byte[] raw)
        {
            if (!On) return;
            switch (kind)
            {
                case K_USE:
                {
                    if (!Session.IsHost) return;
                    bool take = r.U8() != 0;
                    if (take) { if (owner == None) SetOwner(from.Id); else if (owner != from.Id) SendOwner(); }   // (refuse : il le verra et se levera)
                    else if (owner == from.Id) SetOwner(None);
                    break;
                }
                case K_OWNER:
                {
                    if (Session.IsHost) return;
                    int o = r.U8();
                    if (o == owner) return;
                    owner = o;
                    Log.Info("ordinateur partage : " + (o == None ? "libre" : "utilise par " + NameOf(o)) + " (hote)");
                    if (usingHere && o != Session.LocalId && o != None) Refuse();
                    break;
                }
                case K_IMG: OnImage(from, r, raw); break;
            }
        }

        // ---------------------------------------------------------------- essais
        static int testStep; static float testNext, lastShot;
        // [Test] Autotest=pc : l'ordinateur est monte et allume chez chacun (essai : base du jeu Installed, interrupteur ON) a 20 s ;
        // [Test] PcUtilisateur (invite) s'assied a 30 s ('Play') ; l'autre se pose a 2 m de l'ecran a 32 s et essaie de s'asseoir a 45 s (doit
        // etre releve : « occupe ») ; l'utilisateur se leve a [Test] PcLeve (70) ; l'autre s'assied a 76 s (doit l'avoir). Chacun note toutes
        // les 2 s la place, le proprietaire, l'ecran ; captures [Test] PcCaptures (40,60) la camera sur l'ecran.
        public static void Test(string mode, float t)
        {
            if (mode != "pc") return;
            if (root == null) Resolve();
            if (root == null) return;
            bool user = (Config.Get("Test", "PcUtilisateur", "invite") == "hote") == Session.IsHost;
            if (testStep == 0 && t > 20f) { testStep = 1; Log.Info("autotest pc : " + TestPowerUp()); }
            if (user && testStep == 1 && t > 30f) { testStep = 2; Sit("utilisateur"); }
            if (!user && testStep == 1 && t > 32f) { testStep = 2; StandNear(); }
            if (!user && testStep == 2 && t > 45f) { testStep = 3; Sit("autre (doit etre refuse)"); }
            if (user && testStep == 2 && t > Config.GetFloat("Test", "PcLeve", 70f)) { testStep = 3; Log.Info("autotest pc : utilisateur se leve"); StandUp(); }
            if (user && testStep == 2 && Config.GetFloat("Test", "PcQuitte", 0f) > 0f && t > Config.GetFloat("Test", "PcQuitte", 0f)) { testStep = 9; Log.Info("autotest pc : utilisateur quitte le jeu (deconnexion)"); Application.Quit(); }
            if (!user && testStep == 3 && t > Config.GetFloat("Test", "PcLeve", 70f) + 6f) { testStep = 4; Sit("autre (place libre)"); }
            foreach (string s in Config.Get("Test", "PcCaptures", "40,60").Split(','))
            {
                float at;
                if (!float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out at) || t < at - 1f || t > at + 1.5f) continue;
                if (!usingHere && screenR != null) LookAt(screenR.bounds.center);
                if (t < at || lastShot >= at) continue;
                lastShot = at;
                string dir = System.IO.Path.Combine(Log.DataDir, "dumps"); System.IO.Directory.CreateDirectory(dir);
                string png = System.IO.Path.Combine(dir, "pc-" + (Session.IsHost ? "hote" : "invite") + "-t" + ((int)at).ToString("000") + ".png");
                Application.CaptureScreenshot(png);
                Log.Info("autotest pc : capture " + png);
            }
            if (t < 20f || t > 100f || Time.realtimeSinceStartup < testNext) return;
            testNext = Time.realtimeSinceStartup + 2f;
            Log.Info("autotest pc : place " + (trigger != null ? trigger.ActiveStateName : "-") + ", utilise par " + (owner == None ? "personne" : NameOf(owner)) + (usingHere ? " (moi assis)" : "") + ", touche " + (keyDown != null ? (keyDown.Enabled ? "ouverte" : "coupee") : "-")
                     + ", camera " + (cam != null ? (cam.gameObject.activeInHierarchy && cam.enabled ? "allumee" : "eteinte") : "-") + ", ecran " + (shown ? "image de " + NameOf(inWho) : "le sien"));
        }

        static void Sit(string who)
        {
            if (seat != null) Autotest.PutPlayer(seat.position + Vector3.up * 0.2f);
            string before = trigger != null ? trigger.ActiveStateName : "-";
            if (trigger != null && keyDown != null && !keyDown.Enabled) { Log.Info("autotest pc : " + who + " : touche coupee ('" + before + "'), s'asseoit de force pour verifier le refus"); }
            if (trigger != null) Game.SetState(trigger, "Play");
            Log.Info("autotest pc : " + who + " s'assied ('" + before + "' -> '" + (trigger != null ? trigger.ActiveStateName : "-") + "')");
        }

        static void StandNear()
        {
            if (seat == null) return;
            Autotest.PutPlayer(seat.position + seat.right * 1.6f + Vector3.up * 0.3f);
            Log.Info("autotest pc : l'autre se pose pres de l'ordinateur");
        }

        // (essai) l'ordinateur monte (Database/DatabaseComputer/* Installed) et allume (ButtonPower :: Use 'ON' si l'etat existe)
        static string TestPowerUp()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string part in new[] { "Case", "Monitor", "Peripherals" })
            {
                GameObject db = Game.FindAny("Database/DatabaseComputer/" + part);
                PlayMakerFSM d = db != null ? Game.FsmOn(db, "Data") : null;
                FsmBool ins = d != null ? d.FsmVariables.FindFsmBool("Installed") : null;
                sb.Append(part).Append(' ').Append(ins == null ? "?" : ins.Value ? "monte" : "PAS monte").Append(", ");
            }
            // (essai, copie de sauvegarde seulement) pieces posees comme le jeu au chargement : Installed vrai, 'Installed 2' (meshes et place allumes)
            foreach (string part in new[] { "Case", "Monitor", "Peripherals" })
            {
                GameObject db = Game.FindAny("Database/DatabaseComputer/" + part);
                PlayMakerFSM d = db != null ? Game.FsmOn(db, "Data") : null;
                FsmBool ins = d != null ? d.FsmVariables.FindFsmBool("Installed") : null;
                if (ins != null && !ins.Value) { ins.Value = true; Game.SetState(d, "Installed 2"); sb.Append(part).Append(" monte par l'essai, "); }
            }
            Transform bp = root.Find("Computer/Meshes/386_case(xxxxx)/ButtonPower");
            PlayMakerFSM use = bp != null ? Game.FsmOn(bp.gameObject, "Use") : null;
            if (use != null) { FsmBool po = use.FsmVariables.FindFsmBool("PowerOn"); if (po != null) po.Value = true; Game.SetState(use, "ON"); }
            sb.Append("interrupteur ").Append(use != null ? use.ActiveStateName : "introuvable").Append(", place ").Append(trigger != null ? trigger.ActiveStateName : "-")
              .Append(", camera ").Append(cam != null ? (cam.gameObject.activeInHierarchy ? "allumee" : "eteinte") : "-");
            return sb.ToString();
        }

        static void LookAt(Vector3 target)
        {
            GameObject p = Game.PlayerGo;
            Transform c = p != null ? p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera") : null;
            if (c == null) return;
            foreach (Behaviour b in p.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            foreach (Behaviour b in c.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            Vector3 to = target - c.position;
            p.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0);
            c.localRotation = Quaternion.Euler(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, 0, 0);
        }
    }
}
