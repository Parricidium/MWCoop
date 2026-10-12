using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Peinture perso de la CORRIS choisie dans le lanceur (suggestion de .nolly, 12/10). Le jeu sait deja peindre la CORRIS
    // d'une image : CORRIS/AssembyDatabase/PaintMaterials, automate 'ImportPNG', charge <jeu>/Images/car.png (gabarit livre
    // avec le jeu) dans {CarCustomPaint} puis dans _MainTex de CAR_PAINT_DECAL (PaintMaterials[5]) ; la peinture
    // « Custom paint made by artist » de Fleetari (Paintjob 'Work', PaintType 5) met cette matiere sur la carrosserie.
    // Le lanceur importe l'image du joueur dans %LOCALAPPDATA%\MWCoop\peinture.png sans toucher au dossier du jeu ;
    // [Coop] PeinturePerso=1 : ici (hote ou solo), une fois car.png charge par le jeu, l'image du joueur est rechargee
    // DANS la meme texture (LoadImage : les copies de matiere deja faites par les pieces peintes la voient aussi).
    // Les invites recoivent la peinture de l'hote a la place de Images\car.png (contenu envoye, lanceur) : leur jeu la
    // charge lui-meme (Contenu) ; la leur ne compte pas (la voiture est celle de l'hote).
    public static class CustomPaint
    {
        static float at = -1, until;
        static Texture2D target;   // texture rechargee (celle du jeu, ou la notre si car.png manquait)
        static int logs;

        static string File_ { get { string o = Config.Get("Test", "RacineVisages", ""); return Path.Combine(o.Length > 0 ? o : Path.Combine(System.Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? ".", "MWCoop"), "peinture.png"); } }
        static bool Wanted { get { return Config.GetInt("Coop", "PeinturePerso", 0) != 0 && File.Exists(File_); } }

        public static void OnLevelLoaded()
        {
            target = null;
            at = PlayerSync.InGame ? Time.realtimeSinceStartup + 2f : -1;
            until = Time.realtimeSinceStartup + 90f;
        }

        public static void Update()
        {
            if (at < 0 || Time.realtimeSinceStartup < at) return;
            at = Time.realtimeSinceStartup + (target != null ? 5f : 1f);
            if (Session.Active && !Session.IsHost) { at = -1; return; }
            if (!Wanted) { at = -1; return; }
            GameObject pm = Game.FindAny("CORRIS/AssembyDatabase/PaintMaterials");
            PlayMakerFSM f = pm != null ? Game.FsmOn(pm, "ImportPNG") : null;
            if (f == null) { if (Time.realtimeSinceStartup > until) { at = -1; Log.Warn("peinture perso : ImportPNG introuvable"); } return; }
            string st = f.ActiveStateName;
            if (st != "State 3" && st != "State 2" && Time.realtimeSinceStartup < until) return;   // (le jeu charge encore car.png)
            FsmMaterial fm = f.FsmVariables.FindFsmMaterial("Mat");
            Material mat = fm != null ? fm.Value : null;
            if (mat == null) { at = -1; Log.Warn("peinture perso : matiere CAR_PAINT_DECAL introuvable"); return; }
            Texture2D cur = mat.mainTexture as Texture2D;
            if (target != null && cur == target) return;   // deja fait (sinon le jeu a recharge car.png : on recommence)
            byte[] data;
            try { data = File.ReadAllBytes(File_); } catch (System.Exception e) { at = -1; Log.Warn("peinture perso : " + e.Message); return; }
            bool fresh = cur == null;
            Texture2D t = cur ?? new Texture2D(2, 2, TextureFormat.RGB24, true);
            if (!t.LoadImage(data))
            {
                if (fresh) { Object.Destroy(t); at = -1; Log.Warn("peinture perso : image illisible"); return; }
                t = new Texture2D(2, 2, TextureFormat.RGB24, true);   // (texture du jeu non modifiable : une a nous)
                if (!t.LoadImage(data)) { Object.Destroy(t); at = -1; Log.Warn("peinture perso : image illisible"); return; }
                fresh = true;
            }
            t.name = "MWCoop custom paint";
            if (fresh)
            {
                mat.mainTexture = t;
                FsmTexture v = f.FsmVariables.FindFsmTexture("CarCustomPaint") ?? FsmVariables.GlobalVariables.FindFsmTexture("CarCustomPaint");
                if (v != null) v.Value = t;
            }
            target = t;
            if (logs++ < 5) Log.Info("peinture perso : " + t.width + "x" + t.height + " posee sur CAR_PAINT_DECAL (" + (fresh ? "nouvelle texture" : "dans celle de car.png") + ", ImportPNG [" + st + "])");
        }

        // [Test] Autotest=peintureperso (solo, [Coop] PeinturePerso=1, [Test] RacineVisages avec peinture.png) : une fois
        // l'image posee, la carrosserie prend la peinture « Custom paint made by artist » (PaintType 5, REPAINT, comme
        // Fleetari) et le joueur se place a 6 m sur le flanc de la CORRIS, tourne vers elle (capture : [Test] Captures).
        static int tStep;
        public static void Test(string mode, float t)
        {
            if (mode != "peintureperso") return;
            if (tStep == 0 && (target != null || (t > 12f && !Wanted)))
            {
                tStep = 1;
                GameObject body = Game.FindAny("CORRIS/BODY/car body(xxxxx)");
                PlayMakerFSM p = body != null ? Game.FsmOn(body, "Paint") : null;
                if (p == null) { Log.Info("autotest : peintureperso, Paint introuvable"); return; }
                p.FsmVariables.FindFsmInt("PaintType").Value = 5;
                p.SendEvent("REPAINT");
                Log.Info("autotest : peintureperso, carrosserie en PaintType 5 (REPAINT)");
            }
            if (tStep == 1 && t > 14f)
            {
                tStep = 2;
                GameObject bo = Game.FindAny("CORRIS/BODY/car body(xxxxx)");
                Renderer r = bo != null ? bo.GetComponent<Renderer>() : null;
                if (r == null) return;
                Vector3 aim = r.bounds.center;
                Transform car = bo.transform.root;
                // camera d'essai sans collision (la CORRIS est dans la grange) : coin avant droit, ou [Test] PeintureVue=x,y,z
                // en repere de la voiture (droite, haut, avant)
                Vector3 off = new Vector3(1.6f, 0.7f, 3.0f);
                string[] c = Config.Get("Test", "PeintureVue", "").Split(',');
                if (c.Length == 3) { var ci = System.Globalization.CultureInfo.InvariantCulture; off = new Vector3(float.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci)); }
                var go = new GameObject("MWCoop test camera");
                Camera cam = go.AddComponent<Camera>();
                cam.depth = 100; cam.fieldOfView = 60; cam.nearClipPlane = 0.05f;
                go.transform.position = aim + car.right * off.x + Vector3.up * off.y + car.forward * off.z;
                go.transform.LookAt(aim);
                Object.Destroy(go, 30f);
                Log.Info("autotest : peintureperso, carrosserie en " + aim.ToString("F1") + ", camera en " + go.transform.position.ToString("F1") + " ; matiere " + (r.sharedMaterial != null ? r.sharedMaterial.name + " / " + (r.sharedMaterial.mainTexture != null ? r.sharedMaterial.mainTexture.name : "sans texture") : "?"));
            }
        }
    }
}
