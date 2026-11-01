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
        public static readonly Color Paint = new Color(0.72f, 0.05f, 0.05f);   // (rouge, comme celle de JD)
        // (garee chez Teimo, a droite de la terrasse du pub, l'arriere contre la cloture, le nez vers la route -- capture de JD, 31/10 ;
        // repere du magasin STORE : x vers le pub, z vers l'arriere ; [Test] QueteGolfPlace=x,z,cap pour la deplacer)
        static readonly Vector3 SpotStore = new Vector3(11.4f, 0f, 3.6f); const float YawStore = 180f;
        // (My Winter Car : chez Teimo aussi, repere STORE_AREA -- a caler sur une capture de JD)
        static readonly Vector3 SpotStoreMwc = new Vector3(4f, 0f, -7f); const float YawStoreMwc = 0f;
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
            if (!Host) return;
            if (!loaded) { loaded = true; Load(); }
            if (firstInGame < 0f) firstInGame = now;
            // (pose apres l'arrivee en partie : sol et magasin charges, ajouts de l'hote connus)
            if (!placed && on && now - firstInGame > Config.GetFloat("Test", "QueteGolfDelai", 15f) && !Vaz.HasWorldCar(Owner))
            {
                Vector3 pos; float yaw;
                if (Spot(out pos, out yaw)) { placed = true; placedAt = now; Vaz.PlaceWorldCar(Owner, Number, VazCar.ModelGolf, pos, yaw); L("voiture posee en " + pos.ToString("F1") + " cap " + yaw.ToString("F0") + (hasSaved ? " (sauvegarde)" : " (chez Teimo)") + ", quete " + (Done ? "faite" : "a faire")); }
            }
            if (placed && Config.GetInt("Test", "QueteGolfEssai", 0) != 0) TestTick(now);
            if (Session.Active && Session.RemoteCount > 0 && now >= nextSend) { nextSend = now + 5f; Send(); }
        }

        static bool Spot(out Vector3 pos, out float yaw)
        {
            if (hasSaved) { pos = savedPos; yaw = savedYaw; return true; }
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

        // (hote -> tous : 0 / 1 l'etat, 2 la cle vient d'etre donnee)
        static void Send(bool fresh = false)
        {
            if (Session.Active && Session.RemoteCount > 0) Session.Broadcast(new NetWriter(Msg.Race).U8(Vaz.V_QUETE).U8(fresh ? 2 : Done ? 1 : 0), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int v = r.U8();
            if (Session.IsHost) { if (v == 1) SetDone(true, true); }
            else SetDone(v != 0, v == 2);
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
        static void Shot(string name, Vector3 at, Vector3 target)
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
                if (!File.Exists(FilePath)) { L("pas de sauvegarde : quete a faire, voiture chez Teimo"); return; }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Trim().Split(' ');
                    if (p[0] == "fait" && p.Length > 1) Done = p[1] == "1";
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
                File.WriteAllText(FilePath, s);
                L("sauvegardee : quete " + (Done ? "faite" : "a faire") + (hasSaved ? ", voiture en " + savedPos.ToString("F1") : ""));
            }
            catch (Exception e) { Log.Warn("quete s21 : ecriture " + e.Message); }
        }

        // ---------------------------------------------------------------- fichiers de la voiture (dans la DLL)
        // s21.pak : « S21P », nombre de fichiers, puis pour chacun : nom (longueur + UTF-8), taille, taille compressee, donnees
        // (deflate brut ; Inflate : le Mono d'Unity 5.0 n'a pas zlib). Poses dans MWCoop\cache\s21 si la marque (paquet.txt : taille du
        // paquet) a change ; VazCar.Golf83 y lit ses fichiers.
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
                string mark = Path.Combine(dir, "paquet.txt"), want = b.Length.ToString();
                if (File.Exists(mark) && File.ReadAllText(mark).Trim() == want && File.Exists(Path.Combine(dir, "carrosserie.p205"))) return;
                if (b.Length < 8 || b[0] != 'S' || b[1] != '2' || b[2] != '1' || b[3] != 'P') { L("paquet illisible"); return; }
                Directory.CreateDirectory(dir);
                int at = 4, count = BitConverter.ToInt32(b, at); at += 4;
                for (int i = 0; i < count; i++)
                {
                    int nl = BitConverter.ToInt32(b, at); at += 4;
                    string name = System.Text.Encoding.UTF8.GetString(b, at, nl); at += nl;
                    int us = BitConverter.ToInt32(b, at), cs = BitConverter.ToInt32(b, at + 4); at += 8;
                    if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.StartsWith(".")) { at += cs; continue; }
                    File.WriteAllBytes(Path.Combine(dir, name), Inflate.Run(b, at, cs, us));
                    at += cs;
                }
                File.WriteAllText(mark, want);
                L(count + " fichier(s) poses dans " + dir);
            }
            catch (Exception e) { Log.Warn("quete s21 : paquet " + e.Message); }
        }
    }
}
