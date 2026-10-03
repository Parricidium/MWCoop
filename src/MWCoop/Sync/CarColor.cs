using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Couleur de la CORRIS choisie dans le lanceur (demande de JD) et apercu 3D du lanceur.
    //  - Export : une fois par installation, le maillage de la CORRIS (carrosserie et pieces montees)
    //    est ecrit dans MWCoop\cache\corris.mesh depuis le jeu du joueur (aucune donnee du jeu dans
    //    le depot ni dans le zip). Format (petit-boutiste) :
    //      "MWCM", u32 version=1, u32 n, puis n fois :
    //        u16 longueur + nom UTF-8, u8 peignable (1 = prend la couleur choisie),
    //        3 x f32 couleur de base (0..1), u32 nb sommets + nb x (3 x f32) en repere de la voiture
    //        (x droite, y haut, z avant, metres), u32 nb indices + nb x u32 (triangles).
    //  - Nouvelle partie : la carrosserie tire sa couleur au hasard des le chargement. Au menu, le
    //    passage de Licence/Buttons/ButtonBegin (Commencer) par 'Generate ID' marque une nouvelle
    //    partie ; une fois en jeu (12 s, apres l'injection des actions de Paint), chez l'hote ou en
    //    solo, si [Coop] CouleurVoiture=RRGGBB est fixee, la carrosserie est repeinte de cette couleur
    //    comme d'un coup de bombe (REPAINT) : la synchro de peinture la donne aux invites.
    public static class CarColor
    {
        static float exportAt = -1, applyAt = -1;
        static bool exported, applied;
        public static bool NewGame;      // une nouvelle partie vient d'etre lancee depuis le menu

        public static string MeshPath
        {
            get { return Path.Combine(Path.Combine(System.Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "cache"), "corris.mesh"); }
        }

        public static void OnLevelLoaded()
        {
            exported = applied = false;
            exportAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 20f : -1;
            applyAt = PlayerSync.InGame && NewGame ? Time.realtimeSinceStartup + 12f : -1;
            if (PlayerSync.InGame) NewGame = false;
        }

        static PlayMakerFSM begin;

        public static void Update()
        {
            if (Application.loadedLevelName == "MainMenu")
            {
                // Bouton Commencer de la carte du permis : 'Generate ID' = nouvelle partie.
                if (begin == null) { GameObject b = Game.FindAny("Licence/Buttons/ButtonBegin"); if (b != null) begin = Game.FsmOn(b, "SetSize"); }
                if (begin != null && !NewGame && (begin.ActiveStateName == "Generate ID" || begin.ActiveStateName == "State 3" || begin.ActiveStateName == "Load"))
                {
                    NewGame = true;
                    Log.Info("voiture : nouvelle partie lancee");
                }
                return;
            }
            if (applyAt > 0 && Time.realtimeSinceStartup >= applyAt) { applyAt = -1; ApplyChosen(); }
            if (exportAt < 0 || Time.realtimeSinceStartup < exportAt) return;
            if (!exported)
            {
                exported = true;
                if (!File.Exists(MeshPath) || Config.GetInt("Test", "ExportVoiture", 0) != 0)
                    try { Log.Info("voiture : apercu exporte, " + Export()); } catch (System.Exception e) { Log.Warn("voiture : export " + e.Message); }
            }
        }

        static bool IsPaintable(Transform t, Transform root)
        {
            for (; t != null && t != root.parent; t = t.parent)
                if (Game.FsmOn(t.gameObject, "Paint") != null) return true;
            return false;
        }

        static string Export()
        {
            GameObject car = GameObject.Find("CORRIS");
            if (car == null) return "CORRIS introuvable";
            Transform root = car.transform;
            var parts = new List<KeyValuePair<Renderer, Mesh>>();
            foreach (MeshFilter mf in car.GetComponentsInChildren<MeshFilter>())
            {
                Renderer r = mf.GetComponent<Renderer>();
                if (r != null && r.enabled && mf.sharedMesh != null) parts.Add(new KeyValuePair<Renderer, Mesh>(r, mf.sharedMesh));
            }
            foreach (SkinnedMeshRenderer s in car.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (s.enabled && s.sharedMesh != null) parts.Add(new KeyValuePair<Renderer, Mesh>(s, s.sharedMesh));

            Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));
            int written = 0, tris = 0;
            using (var w = new BinaryWriter(File.Create(MeshPath)))
            {
                w.Write(new[] { (byte)'M', (byte)'W', (byte)'C', (byte)'M' });
                w.Write(1);
                long countPos = w.BaseStream.Position;
                w.Write(0);
                foreach (KeyValuePair<Renderer, Mesh> kv in parts)
                {
                    Renderer r = kv.Key;
                    Mesh m = kv.Value;
                    Material mat = r.sharedMaterial;
                    string mn = mat != null ? mat.name.ToLowerInvariant() : "";
                    if (mn.Contains("glass") || mn.Contains("window") || mn.Contains("shadow") || mn.Contains("alpha")) continue;
                    if (!m.isReadable) continue;
                    Vector3[] v = m.vertices;
                    int[] idx = m.triangles;
                    if (v.Length == 0 || idx.Length == 0 || v.Length > 60000) continue;
                    byte[] name = System.Text.Encoding.UTF8.GetBytes(r.name);
                    w.Write((ushort)name.Length); w.Write(name);
                    w.Write((byte)(IsPaintable(r.transform, root) ? 1 : 0));
                    Color c = mat != null && mat.HasProperty("_Color") ? mat.color : Color.gray;
                    w.Write(c.r); w.Write(c.g); w.Write(c.b);
                    w.Write(v.Length);
                    Matrix4x4 toCar = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    foreach (Vector3 p in v)
                    {
                        Vector3 q = toCar.MultiplyPoint3x4(p);
                        w.Write(q.x); w.Write(q.y); w.Write(q.z);
                    }
                    w.Write(idx.Length);
                    foreach (int i in idx) w.Write(i);
                    written++;
                    tris += idx.Length / 3;
                }
                w.BaseStream.Position = countPos;
                w.Write(written);
            }
            return written + " morceaux, " + tris + " triangles, " + MeshPath;
        }

        static void ApplyChosen()
        {
            if (applied || (Session.Active && !Session.IsHost)) return;   // invite : la couleur vient de l'hote
            GameObject body = Game.FindAny("CORRIS/BODY/car body(xxxxx)");
            PlayMakerFSM paint = body != null ? Game.FsmOn(body, "Paint") : null;
            if (paint == null) { Log.Warn("voiture : carrosserie introuvable"); return; }
            TryOverride(paint);
        }

        // Repeint la carrosserie de la couleur choisie dans le lanceur (une fois par partie).
        public static bool TryOverride(PlayMakerFSM paint)
        {
            if (applied || (Session.Active && !Session.IsHost)) return false;
            if (paint.transform.root.name != "CORRIS" || !paint.name.StartsWith("car body")) return false;
            Color c;
            if (!Parse(Config.Get("Coop", "CouleurVoiture", ""), out c)) return false;
            applied = true;
            FsmColor col = paint.FsmVariables.FindFsmColor("Color");
            if (col == null) return false;
            col.Value = c;
            FsmInt type = paint.FsmVariables.FindFsmInt("PaintType");
            if (type != null && type.Value > 2) type.Value = 1;   // couleur speciale tiree : brillant
            Log.Info("voiture : couleur choisie " + c + " appliquee a la nouvelle CORRIS");
            paint.SendEvent("REPAINT");
            return true;
        }

        public static bool Parse(string hex, out Color c)
        {
            c = Color.white;
            if (hex == null || hex.Length != 6) return false;
            int v;
            if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
            c = new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
            return true;
        }
    }
}
