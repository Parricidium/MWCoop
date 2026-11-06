using System;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Quete de la boutique s21 (JD 31/10) : une voiture (VazCar.Golf83, standalone jamais au F10, nommee nulle part -- ni lanceur,
    // ni catalogue, ni textes du jeu) est garee chez Teimo, fermee a cle ; un client de la boutique s21, devant la vitrine des
    // consoles (Kauppa, KauppaGolf.cs), la prete quand on lui parle et tend la cle -- tous les joueurs la recoivent ; elle s'ouvre.
    // Comme une quete du jeu : quete faite et place de la voiture gardees dans la partie de l'hote (MWCoop-s21.txt : « fait 0|1 »,
    // « place x y z cap »), reprises au chargement ; nouvelle partie : fichier efface (WorldFiles).
    // Hote (ou solo) : pose la voiture (Vaz.PlaceWorldCar, proprietaire Owner, numero Number) et fait autorite sur la quete ; un
    // invite qui recoit la cle le dit a l'hote (V_QUETE 1) ; l'hote diffuse l'etat (V_QUETE 0|1, a chaque changement et toutes les
    // 5 s). Fichiers de la voiture : dans la DLL (assets\kauppa\s21.pak, golf83/paquet-s21.py de MWCoop-voiture), poses dans
    // MWCoop\cache\s21 au premier lancement du jeu (refaits a chaque nouvelle version du paquet).
    public static class QueteGolf
    {
        public const int Owner = 99, Number = 16;
        public static readonly Color PaintBase = new Color(0.72f, 0.05f, 0.05f);   // (rouge, comme celle de JD)
        // Couleur choisie (F10, onglet des voitures -- suggestion de Донской, 04/11), reservoir et temperature gardes dans la sauvegarde de la quete.
        public static readonly Color[] Palette = {
            PaintBase, new Color(0.06f, 0.20f, 0.62f), new Color(0.18f, 0.50f, 0.86f), new Color(0.02f, 0.28f, 0.13f),
            new Color(0.03f, 0.03f, 0.03f), new Color(0.86f, 0.86f, 0.84f), new Color(0.50f, 0.52f, 0.54f), new Color(0.90f, 0.66f, 0.05f) };
        public static int PaintIndex { get; private set; }
        public static Color Paint { get { return Palette[Mathf.Clamp(PaintIndex, 0, Palette.Length - 1)]; } }
        public static float SavedFuel = 1f, SavedTemp = float.NaN;
        // (joueur : demande a l'hote ; hote : pose, renvoie a tous, repeinte tout de suite)
        public static void ChoosePaint(int i)
        {
            i = Mathf.Clamp(i, 0, Palette.Length - 1);
            if (Session.Active && !Session.IsHost) Session.SendToHost(new NetWriter(Msg.Race).U8(Vaz.V_QUETE).U8(10 + i), true);
            ApplyPaint(i);
            if (Host) Send();
        }
        static void ApplyPaint(int i)
        {
            if (i == PaintIndex) return;
            PaintIndex = i;
            StandaloneCar.RepaintQuest(Paint);
            L("couleur " + i);
        }
        // (garee chez Teimo, a droite de la terrasse du pub, l'arriere contre la cloture, le nez vers la route -- capture de JD, 31/10 ;
        // repere du magasin STORE : x vers le pub, z vers l'arriere ; [Test] QueteGolfPlace=x,z,cap pour la deplacer)
        static readonly Vector3 SpotStore = new Vector3(11.4f, 0f, 3.6f); const float YawStore = 180f;
        // (My Winter Car : comme sur MSC -- JD 01/11 : « au meme endroit que MSC, dans le coin ou y a les barrieres » : a droite de la
        // terrasse du pub, l'arriere contre la cloture (STORE_AREA/Stuff/LOD/Fence 2), le flanc gauche le long de son retour, le nez vers
        // la route ; repere STORE_AREA)
        static readonly Vector3 SpotStoreMwc = new Vector3(8.7f, 0f, 8.5f); const float YawStoreMwc = 153f;
        public static bool Done { get; private set; }
        static bool loaded, placed, hasSaved; static Vector3 savedPos; static float savedYaw;
        static float nextSend, nextCheck, firstInGame = -1f, placedAt; static bool on;
        static string FilePath { get { return WorldFiles.PathOf("MWCoop-s21.txt"); } }
        static bool Host { get { return !Session.Active || Session.IsHost; } }
        static void L(string s) { Log.Info("quete s21 : " + s); }

        // quete possible ici : les deux jeux (StandaloneCar.Quest), boutique s21 construite (ajout COOP), voitures du F10 (VAZ) et la voiture posee
        public static bool On { get { return on; } }
        // la voiture de la quete, pas encore pretee : portieres, hayon et contact fermes
        public static bool Locked(VazCar.Built b) { return b != null && b.Number == Number && b.M != null && b.M.QuestOnly && !Done; }
        public static bool IsQuestCar(VazCar.Built b) { return b != null && b.Number == Number && b.M != null && b.M.QuestOnly; }

        public static void OnLevelLoaded()
        {
            EnsurePack();
            Done = false; loaded = placed = hasSaved = false; on = false; nextSend = nextCheck = 0f; firstInGame = -1f;
        }

        public static void Update()
        {
            if (!StandaloneCar.Quest || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextCheck) { nextCheck = now + 2f; on = Kauppa.ShopOn && Vaz.Enabled && VazCar.Golf83.HasFiles; }
            if (!Host) { if (Config.GetInt("Test", "QueteCommandes", 0) != 0) StandaloneCar.QuestControlsTest(now); return; }
            if (!loaded) { loaded = true; Load(); }
            if (firstInGame < 0f) firstInGame = now;
            // (pose apres l'arrivee en partie : sol et magasin charges, ajouts de l'hote connus)
            if (!placed && on && now - firstInGame > Config.GetFloat("Test", "QueteGolfDelai", 15f) && !Vaz.HasWorldCar(Owner))
            {
                Vector3 pos; float yaw;
                if (Spot(out pos, out yaw)) { placed = true; placedAt = now; Vaz.PlaceWorldCar(Owner, Number, VazCar.ModelGolf, pos, yaw); L("voiture posee en " + pos.ToString("F1") + " cap " + yaw.ToString("F0") + (hasSaved ? " (sauvegarde)" : " (chez Teimo)") + ", quete " + (Done ? "faite" : "a faire")); }
            }
            if (placed && Config.GetInt("Test", "QueteGolfEssai", 0) != 0) TestTick(now);
            if (placed && Config.GetInt("Test", "QueteCommandes", 0) != 0) StandaloneCar.QuestControlsTest(now - placedAt);
            if (Session.Active && Session.RemoteCount > 0 && now >= nextSend) { nextSend = now + 5f; Send(); }
        }

        // (village Assakyla, Ville.cs, JD 04/11 : place tant que la quete n'est pas faite -- quete faite : la voiture reste ou on l'a laissee ; [Quete] Place=x,z,cap pour essayer)
        static bool VillageSpot(out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            string[] c = Config.Get("Quete", "Place", Ville.On ? Ville.QuestCar : "").Split(','); var ci = System.Globalization.CultureInfo.InvariantCulture; float x, z;
            if (c.Length != 3 || !float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out x) || !float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out z) || !float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out yaw)) return false;
            float gy = float.MinValue;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, 60f, z), Vector3.down, 100f))
                if (!h.collider.isTrigger && h.collider.attachedRigidbody == null && h.point.y > gy) gy = h.point.y;
            if (gy == float.MinValue) return false;
            pos = new Vector3(x, gy + 0.8f, z);
            return true;
        }
        static bool AtTeimo(Vector3 p)
        {
            GameObject st = GameObject.Find(Game.Msc ? "STORE" : "STORE_AREA");
            if (st == null) return false;
            Vector3 d = st.transform.TransformPoint(Game.Msc ? SpotStore : SpotStoreMwc) - p; d.y = 0f;
            return d.magnitude < 6f;
        }
        // Voiture garee dans le petit village (son rectangle) : ce que le client en dit
        public static bool ParkedInVillage
        {
            get
            {
                Transform car = Vaz.CarOf(Owner);
                if (car == null) return false;
                Vector3 p = car.position;
                return p.x > -760f && p.x < -480f && p.z > 1600f && p.z < 1840f;
            }
        }

        static bool Spot(out Vector3 pos, out float yaw)
        {
            if (!Done && VillageSpot(out pos, out yaw)) return true;
            // (quete faite avant le village, voiture jamais bougee de sa place chez Teimo : au village avec les autres -- JD 04/11 ; deplacee par le joueur : elle reste ou elle est)
            if (hasSaved && AtTeimo(savedPos) && VillageSpot(out pos, out yaw)) { L("voiture restee chez Teimo : posee au village"); hasSaved = false; return true; }
            if (hasSaved && !Bogged(savedPos)) { pos = savedPos; yaw = savedYaw; return true; }
            // (laissee au marais ou sous l'eau -- dossier #39, квазарик 01/11 : le sol Swamp du jeu (resistance au roulement 2, contre
            // 0,018 sur la route) enlise toutes les voitures, le jeu a sa depanneuse pour les siennes ; la voiture de la quete, unique,
            // y restait d'une partie a l'autre -- ramenee chez Teimo au chargement, comme remorquee)
            if (hasSaved) { L("voiture laissee au marais en " + savedPos.ToString("F1") + " : ramenee (remorquee)"); hasSaved = false; }
            if (VillageSpot(out pos, out yaw)) return true;
            pos = Vector3.zero; yaw = 0f;
            GameObject st = GameObject.Find(Game.Msc ? "STORE" : "STORE_AREA");
            if (st == null) return false;
            Vector3 local = Game.Msc ? SpotStore : SpotStoreMwc; float y0 = Game.Msc ? YawStore : YawStoreMwc;
            string t = Config.Get("Test", "QueteGolfPlace", "");
            if (t.Length > 0)
            {
                string[] c = t.Split(','); var ci = System.Globalization.CultureInfo.InvariantCulture; float a, b, d;
                if (c.Length == 3 && float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out a) && float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out b) && float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out d)) { local = new Vector3(a, 0f, b); y0 = d; }
            }
            Vector3 p = st.transform.TransformPoint(local);
            float gy = float.MinValue;
            foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 20f))
                if (!h.collider.isTrigger && h.collider.attachedRigidbody == null && h.point.y > gy) gy = h.point.y;
            if (gy == float.MinValue) return false;
            pos = new Vector3(p.x, gy + 0.8f, p.z); yaw = st.transform.eulerAngles.y + y0;
            return true;
        }

        // Sol du marais sous la place (matiere Swamp, colliders SWAMP / SwampCollider* du jeu) ou sous le niveau de l'eau (My Summer Car)
        static bool Bogged(Vector3 p)
        {
            var sb = new System.Text.StringBuilder();
            foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 12f))
            {
                string m = h.collider.sharedMaterial != null ? h.collider.sharedMaterial.name : "";
                sb.Append(" [").Append(h.collider.name).Append(" ").Append(m).Append(" y ").Append(h.point.y.ToString("F1")).Append(h.collider.isTrigger ? " declencheur" : "").Append(h.collider.attachedRigidbody != null ? " corps" : "").Append("]");
                if (h.collider.isTrigger || h.collider.attachedRigidbody != null) continue;
                if (m.StartsWith("Swamp") || h.collider.name == "SWAMP" || h.collider.name.StartsWith("SwampCollider")) { L("sol sous " + p.ToString("F1") + " :" + sb); return true; }
            }
            L("sol sous " + p.ToString("F1") + " :" + sb);
            return Game.Msc && p.y < -3.5f;
        }

        // ---------------------------------------------------------------- cle
        // Le joueur d'ici recoit la cle (le client la lui tend) : a tous, par l'hote.
        public static void GiveKey()
        {
            if (Host) SetDone(true, true);
            else { Session.SendToHost(new NetWriter(Msg.Race).U8(Vaz.V_QUETE).U8(1), true); SetDone(true, true); }
        }

        // fresh : la cle vient d'etre donnee (message et son chez chacun) ; sinon l'etat de la quete (chargement, arrivee d'un invite)
        static void SetDone(bool d, bool fresh)
        {
            if (d == Done) return;
            Done = d;
            L(d ? (fresh ? "cle donnee, voiture ouverte" : "quete deja faite, voiture ouverte") : "quete a faire (etat de l'hote)");
            if (d && fresh)
            {
                Hud.Toast("You received the car key.");
                AudioSource ks = FindKeySound(); if (ks != null && ks.clip != null && Game.MainCam != null) AudioSource.PlayClipAtPoint(ks.clip, Game.MainCam.transform.position, 0.8f);
            }
            if (Host) Send(d && fresh);
        }

        static float SavedFuelNow() { float t; float f = StandaloneCar.QuestFuel(out t); return f >= 0f ? f : SavedFuel; }
        // (hote -> tous : 0 / 1 l'etat, 2 la cle vient d'etre donnee ; puis couleur et reservoir. Invite -> hote : 1 la cle, 10 + n la couleur)
        static void Send(bool fresh = false)
        {
            if (Session.Active && Session.RemoteCount > 0) Session.Broadcast(new NetWriter(Msg.Race).U8(Vaz.V_QUETE).U8(fresh ? 2 : Done ? 1 : 0).U8(PaintIndex).U8(Mathf.RoundToInt(Mathf.Clamp01(SavedFuelNow()) * 255f)), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int v = r.U8();
            if (Session.IsHost)
            {
                if (v == 1) SetDone(true, true);
                else if (v >= 10) { ApplyPaint(Mathf.Clamp(v - 10, 0, Palette.Length - 1)); Send(); }
            }
            else
            {
                SetDone(v != 0, v == 2);
                byte[] rest = r.Rest();
                if (rest.Length > 0) ApplyPaint(rest[0]);
                if (rest.Length > 1) StandaloneCar.SetQuestFuelIfIdle(rest[1] / 255f);
            }
        }

        static AudioSource FindKeySound()
        {
            foreach (string p in new[] { "MasterAudio/CarFoley/carkeys_in", "MasterAudio/CarFoley/ignition_keys", "MasterAudio/CarFoley/seatbelt_fasten" })
            {
                GameObject g = GameObject.Find(p);
                if (g != null && g.GetComponent<AudioSource>() != null) return g.GetComponent<AudioSource>();
            }
            return null;
        }

        // ---------------------------------------------------------------- essai ([Test] QueteGolfEssai=1, hote)
        // 8 s apres la pose : voiture fermee ? photo chez Teimo (quete-voiture) ; 10 s : on parle au client (Kauppa.TestTalk), photos
        // a 2,5 s (il parle, quete-client) et 8 s (la cle tendue, quete-cle) ; 24 s : quete faite ? sauvegarde ecrite et relue.
        static int tStep;
        static void TestTick(float now)
        {
            float e = now - placedAt;
            Transform car = Vaz.CarOf(Owner);
            if (tStep == 0 && e > 8f && car != null)
            {
                tStep = 1;
                VazCar.Built b = StandaloneCar.Find(car.gameObject);
                L("essai : voiture en " + car.position.ToString("F1") + ", fermee " + Locked(b));
                GameObject st = GameObject.Find(Game.Msc ? "STORE" : "STORE_AREA");
                if (st != null) Shot("quete-voiture", st.transform.TransformPoint(new Vector3(2.5f, 2.6f, -8f)), car.position + Vector3.up * 0.6f);
                Shot("quete-voiture-haut", car.position + Vector3.up * 14f - car.forward * 9f, car.position);   // (la place vue d'en haut, de l'arriere)
                Shot("quete-voiture-cote", car.position + car.right * 5f + Vector3.up * 1.6f, car.position + Vector3.up * 0.6f);
            }
            if (tStep == 1 && e > 10f) { tStep = 2; L("essai : " + Kauppa.TestTalk()); }
            Vector3 eye, head;
            if (tStep == 2 && e > 12.5f) { tStep = 3; if (Kauppa.TestTalkView(out eye, out head)) Shot("quete-client", eye, head); }
            if (tStep == 3 && e > 18f) { tStep = 4; if (Kauppa.TestTalkView(out eye, out head)) Shot("quete-cle", eye + Vector3.up * 0.2f, head - Vector3.up * 0.5f); }
            if (tStep == 4 && e > 24f)
            {
                tStep = 5;
                VazCar.Built b = car != null ? StandaloneCar.Find(car.gameObject) : null;
                L("essai : quete " + (Done ? "faite" : "PAS faite") + ", voiture fermee " + Locked(b));
                OnGameSave();
                try { L("essai : fichier : " + File.ReadAllText(FilePath).Replace("\n", " | ")); } catch (Exception ex) { L("essai : fichier illisible " + ex.Message); }
            }
        }
        internal static void Shot(string name, Vector3 at, Vector3 target)
        {
            var go = new GameObject("quete_photo");
            try
            {
                var cam = go.AddComponent<Camera>(); cam.enabled = false;
                if (Game.MainCam != null) cam.CopyFrom(Game.MainCam);
                cam.fieldOfView = 55f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 800f;
                go.transform.position = at; go.transform.LookAt(target);
                var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                string png = Path.Combine(Path.Combine(Log.DataDir, "dumps"), "photo-" + name + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(png));
                File.WriteAllBytes(png, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
                L("essai : photo " + png);
            }
            catch (Exception ex) { Log.Warn("quete s21 : photo " + ex.Message); }
            UnityEngine.Object.Destroy(go);
        }

        // ---------------------------------------------------------------- sauvegarde (hote)
        static void Load()
        {
            try
            {
                WorldFiles.Check(Path.GetDirectoryName(FilePath));
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                // ([Test] QueteGolfSauvee=x,y,z,cap : place lue comme dans une sauvegarde -- essai du marais, dossier #39)
                string ts = Config.Get("Test", "QueteGolfSauvee", "");
                // (« boutique » : quete faite, voiture a sa place devant le magasin -- commandes sur les deux jeux)
                if (ts == "boutique") { Done = true; L("essai : quete faite, voiture devant le magasin"); return; }
                if (ts.Length > 0)
                {
                    string[] c = ts.Split(','); float x, y, z, w;
                    if (c.Length == 4 && float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out y)
                        && float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out z) && float.TryParse(c[3], System.Globalization.NumberStyles.Float, ci, out w))
                    { savedPos = new Vector3(x, y, z); savedYaw = w; hasSaved = true; Done = true; L("essai : place sauvee posee en " + savedPos.ToString("F1")); return; }
                }
                if (!File.Exists(FilePath)) { L("pas de sauvegarde : quete a faire, voiture chez Teimo"); return; }
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Trim().Split(' ');
                    if (p[0] == "fait" && p.Length > 1) Done = p[1] == "1";
                    else if (p[0] == "couleur" && p.Length > 1) { int ci2; if (int.TryParse(p[1], out ci2)) PaintIndex = Mathf.Clamp(ci2, 0, Palette.Length - 1); }
                    else if (p[0] == "reservoir" && p.Length > 2)
                    {
                        float fu, te;
                        if (float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out fu)) SavedFuel = Mathf.Clamp01(fu);
                        if (float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out te) && !float.IsNaN(te)) SavedTemp = te;
                    }
                    else if (p[0] == "place" && p.Length > 4)
                    {
                        float x, y, z, w;
                        if (float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out y)
                            && float.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out z) && float.TryParse(p[4], System.Globalization.NumberStyles.Float, ci, out w)
                            && !float.IsNaN(x + y + z + w)) { savedPos = new Vector3(x, y, z); savedYaw = w; hasSaved = true; }
                    }
                }
                L("sauvegarde lue : quete " + (Done ? "faite" : "a faire") + (hasSaved ? ", voiture en " + savedPos.ToString("F1") : ""));
            }
            catch (Exception e) { Log.Warn("quete s21 : lecture " + e.Message); }
        }

        public static void OnGameSave()
        {
            if (!StandaloneCar.Quest || !Host || !loaded) return;
            try
            {
                Transform c = Vaz.CarOf(Owner);
                if (c != null) { savedPos = c.position + Vector3.up * 0.3f; savedYaw = c.eulerAngles.y; hasSaved = true; }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                string s = "fait " + (Done ? "1" : "0") + "\n" + (hasSaved ? "place " + savedPos.x.ToString("F3", ci) + " " + savedPos.y.ToString("F3", ci) + " " + savedPos.z.ToString("F3", ci) + " " + savedYaw.ToString("F1", ci) + "\n" : "");
                float tt, ff = StandaloneCar.QuestFuel(out tt);
                if (ff >= 0f) { SavedFuel = ff; SavedTemp = tt; }
                s += "\ncouleur " + PaintIndex + "\nreservoir " + SavedFuel.ToString("F3", ci) + " " + (float.IsNaN(SavedTemp) ? "NaN" : SavedTemp.ToString("F1", ci));
                File.WriteAllText(FilePath, s);
                L("sauvegardee : quete " + (Done ? "faite" : "a faire") + (hasSaved ? ", voiture en " + savedPos.ToString("F1") : ""));
            }
            catch (Exception e) { Log.Warn("quete s21 : ecriture " + e.Message); }
        }

        // ---------------------------------------------------------------- fichiers de la voiture (dans la DLL)
        // s21.pak : « S21P », nombre de fichiers, puis pour chacun : nom (longueur + UTF-8), taille, taille compressee, donnees
        // (deflate brut ; Inflate : le Mono d'Unity 5.0 n'a pas zlib). Poses dans MWCoop\cache\s21 si la marque (paquet.txt : taille et
        // empreinte du paquet) a change ; VazCar.Golf83 y lit ses fichiers.
        static bool packDone;
        static void EnsurePack()
        {
            if (packDone || !StandaloneCar.Quest) return;
            packDone = true;
            try
            {
                string dir = Path.Combine(Path.Combine(System.Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "cache"), "s21");
                byte[] b;
                using (Stream s = typeof(QueteGolf).Assembly.GetManifestResourceStream("MWCoop.kauppa.s21.pak"))
                {
                    if (s == null) { L("paquet absent de la DLL"); return; }
                    b = new byte[s.Length]; int got = 0;
                    while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) break; got += n; }
                }
                VazCar.Golf83.DirOverride = dir;
                // (marque : taille et empreinte FNV-1a du paquet -- une retouche de meme taille refait aussi les fichiers, essai du 01/11)
                uint fnv = 2166136261; for (int i = 0; i < b.Length; i++) { fnv ^= b[i]; fnv *= 16777619; }
                string mark = Path.Combine(dir, "paquet.txt"), want = b.Length + " " + fnv.ToString("x8");
                if (File.Exists(mark) && File.ReadAllText(mark).Trim() == want && File.Exists(Path.Combine(dir, "carrosserie.p205"))) return;
                if (b.Length < 8 || b[0] != 'S' || b[1] != '2' || b[2] != '1' || b[3] != 'P') { L("paquet illisible"); return; }
                Directory.CreateDirectory(dir);
                int at = 4, count = BitConverter.ToInt32(b, at); at += 4;
                for (int i = 0; i < count; i++)
                {
                    int nl = BitConverter.ToInt32(b, at); at += 4;
                    string name = System.Text.Encoding.UTF8.GetString(b, at, nl); at += nl;
                    int us = BitConverter.ToInt32(b, at), cs = BitConverter.ToInt32(b, at + 4); at += 8;
                    // (seul sous-dossier admis : sons/, son de la voiture -- JD 04/11)
                    string sub = name.StartsWith("sons/") ? "sons" : null, leaf = sub != null ? name.Substring(5) : name;
                    if (leaf.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || leaf.StartsWith(".")) { at += cs; continue; }
                    string to = dir;
                    if (sub != null) { to = Path.Combine(dir, sub); Directory.CreateDirectory(to); }
                    File.WriteAllBytes(Path.Combine(to, leaf), Inflate.Run(b, at, cs, us));
                    at += cs;
                }
                File.WriteAllText(mark, want);
                L(count + " fichier(s) poses dans " + dir);
            }
            catch (Exception e) { Log.Warn("quete s21 : paquet " + e.Message); }
        }
    }
}
