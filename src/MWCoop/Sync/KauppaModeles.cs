using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWCoop
{
    // Modeles 3D des objets du mod (JD 07/11 : « changer les models temporaires ») : modeles Sketchfab (CC-BY 4.0, auteurs dans
    // MWCoop\CREDITS-objets.txt) convertis par run/objets/objets.py en un seul fichier, assets/objets/objets.pak, embarque dans
    // la DLL. Maillages et matieres lus une fois, partages par tous les exemplaires ; repere du mod (y en haut, facade vers -z,
    // centre). Seul le visuel change : boites de collision, masses et comportements restent ceux d'avant. Fichier absent ou
    // illisible : l'ancien modele. [Test] ObjetsAnciens=1 : les anciens modeles (photos avant / apres).
    public static partial class Kauppa
    {
        class ObjModel { public Vector3 Size; public readonly List<KeyValuePair<string, KeyValuePair<Mesh, Material>>> Parts = new List<KeyValuePair<string, KeyValuePair<Mesh, Material>>>(); }
        static Dictionary<string, ObjModel> objModels;
        static bool objTried;

        // Pose les pieces du modele « name » sous g (un enfant par piece, a son nom). Faux : pas de modele (l'appelant garde l'ancien).
        static bool ModelFile(string name, GameObject g, out Vector3 size)
        {
            size = Vector3.zero;
            if (Config.Testing && Config.GetInt("Test", "ObjetsAnciens", 0) != 0) return false;
            LoadObjModels();
            ObjModel m;
            if (objModels == null || !objModels.TryGetValue(name, out m)) return false;
            foreach (var p in m.Parts)
            {
                var c = new GameObject(p.Key);
                c.transform.SetParent(g.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = p.Value.Key;
                c.AddComponent<MeshRenderer>().sharedMaterial = p.Value.Value;
            }
            size = m.Size;
            return true;
        }

        static void LoadObjModels()
        {
            if (objTried) return;
            objTried = true;
            try
            {
                using (Stream s = typeof(Kauppa).Assembly.GetManifestResourceStream("MWCoop.objets.pak"))
                {
                    if (s == null) { L("objets : objets.pak absent de la DLL, anciens modeles"); return; }
                    var r = new BinaryReader(s);
                    if (new string(r.ReadChars(4)) != "MWOB" || r.ReadInt32() != 1) { L("objets : objets.pak illisible"); return; }
                    Material tpl = GameMat("wood_panel7");
                    Shader sh = tpl != null ? tpl.shader : Shader.Find("Legacy Shaders/Diffuse");
                    var all = new Dictionary<string, ObjModel>();
                    int n = r.ReadInt32(), tris = 0;
                    for (int i = 0; i < n; i++)
                    {
                        string name = r.ReadString();
                        var m = new ObjModel { Size = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) };
                        var tex = new List<Texture2D>();
                        int nt = r.ReadInt32();
                        for (int k = 0; k < nt; k++)
                        {
                            string tn = r.ReadString();
                            byte[] data = r.ReadBytes(r.ReadInt32());
                            bool pal = tn.EndsWith("_pal.png");   // (palette de couleurs : pas de mipmaps ni de filtrage, les couleurs ne se melangent pas)
                            var t = new Texture2D(2, 2, TextureFormat.RGB24, !pal) { name = "objet_" + tn };
                            t.LoadImage(data);
                            if (pal) t.filterMode = FilterMode.Point;
                            t.wrapMode = TextureWrapMode.Clamp;
                            tex.Add(t);
                        }
                        int np = r.ReadInt32();
                        for (int k = 0; k < np; k++)
                        {
                            string pn = r.ReadString();
                            int ti = r.ReadInt32();
                            var col = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                            int nv = r.ReadInt32();
                            var v = new Vector3[nv]; var nn = new Vector3[nv]; var uv = new Vector2[nv];
                            for (int j = 0; j < nv; j++) v[j] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                            for (int j = 0; j < nv; j++) nn[j] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                            for (int j = 0; j < nv; j++) uv[j] = new Vector2(r.ReadSingle(), r.ReadSingle());
                            int ni = r.ReadInt32();
                            var idx = new int[ni];
                            for (int j = 0; j < ni; j++) idx[j] = r.ReadInt32();
                            var mesh = new Mesh { name = "objet_" + name + "_" + pn, vertices = v, normals = nn, uv = uv, triangles = idx };
                            mesh.RecalculateBounds();
                            var mat = new Material(sh) { name = "objet_" + name + "_" + pn, color = ti >= 0 ? Color.white : col };
                            if (ti >= 0 && ti < tex.Count) mat.mainTexture = tex[ti];
                            m.Parts.Add(new KeyValuePair<string, KeyValuePair<Mesh, Material>>(pn, new KeyValuePair<Mesh, Material>(mesh, mat)));
                            tris += ni / 3;
                        }
                        int npt = r.ReadInt32();
                        for (int k = 0; k < npt; k++) { r.ReadString(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle(); }
                        all[name] = m;
                    }
                    objModels = all;
                    L("objets : " + all.Count + " modeles (" + tris + " triangles)");
                }
            }
            catch (Exception e) { L("objets : modeles illisibles, anciens gardes : " + e.Message); objModels = null; }
        }

        // [Test] ObjetsPhotos=s : a s secondes, chaque objet seul en l'air (loin de tout, une lampe), vu de trois quarts et de face ;
        // ecrans de la TV et du GPS avec une image du mod (sens de l'image). ObjetsAnciens=1 : les memes avec les anciens modeles.
        static bool objPhotoDone;
        static void ObjetsPhotoTest()
        {
            float at = Config.GetFloat("Test", "ObjetsPhotos", 0f);
            if (objPhotoDone || at <= 0f || Time.timeSinceLevelLoad < at) return;
            GameObject pl = Game.PlayerGo;
            if (pl == null) return;
            objPhotoDone = true;
            string tag = Config.GetInt("Test", "ObjetsAnciens", 0) != 0 ? "avant" : "apres";
            Vector3 o = pl.transform.position + Vector3.up * 40f;
            var lg = new GameObject("essai_lampe");
            Light li = lg.AddComponent<Light>(); li.type = LightType.Point; li.range = 5f; li.intensity = 1.4f;
            string[] kinds = { KVhs, KNauhuri, KTv, KKamera, KKartturi, KPopcorn, KKela };
            for (int i = 0; i < kinds.Length; i++)
            {
                Vector3 size; Transform scr;
                GameObject g = Model(kinds[i], out size, out scr);
                g.transform.position = o + Vector3.right * (i * 3f);
                if (scr != null) scr.GetComponent<Renderer>().sharedMaterial = TexMat(kinds[i] == KTv ? "konsoli-demo.png" : "vhs-kansi.png");
                float d = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 2.4f + 0.12f;
                Vector3 c = g.transform.position;
                lg.transform.position = c + new Vector3(-0.4f * d, 0.8f * d, -0.9f * d);
                QueteGolf.Shot("obj-" + tag + "-" + kinds[i], c + new Vector3(-0.6f * d, 0.45f * d, -0.75f * d), c);
                QueteGolf.Shot("obj-" + tag + "-" + kinds[i] + "-face", c + new Vector3(0f, 0.08f * d, -d), c);
                if (kinds[i] == KKamera) { lg.transform.position = c + new Vector3(0.3f * d, 0.6f * d, 0.9f * d); QueteGolf.Shot("obj-" + tag + "-" + kinds[i] + "-objectif", c + new Vector3(0.15f * d, 0.1f * d, 0.7f * d), c); }
                L("essai objets : " + kinds[i] + " taille " + size.ToString("F3") + (scr != null ? ", ecran " + scr.name : ""));
                UnityEngine.Object.Destroy(g);
            }
            UnityEngine.Object.Destroy(lg);
            // en place : rayons du magasin et comptoir du cinema (premier exemplaire de chaque, vu de face a 1 m)
            var seen = new HashSet<string>();
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (!t.name.StartsWith("osuus_") || t.parent == null || Array.IndexOf(kinds, t.name.Substring(6)) < 0 || !seen.Add(t.name)) continue;
                Vector3 c = t.position;
                QueteGolf.Shot("obj-" + tag + "-rayon-" + t.name.Substring(6), c - t.forward * 0.9f + Vector3.up * 0.35f, c);
                L("essai objets : en place " + t.name + " en " + c.ToString("F1"));
            }
            L("essai objets : photos faites ; fini");
        }

        // Rectangle d'une texture du mod (uv : partie de l'image, 0..1, origine en bas a gauche), pose a plat : normale vers « up »,
        // haut de l'image vers « fwd ». Pour une etiquette posee sur un modele.
        static GameObject LabelQuad(GameObject parent, string name, string png, Rect uv, Vector3 pos, Vector3 up, Vector3 fwd, Vector2 size)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.LookRotation(-up, fwd);   // (quad : visible depuis -z local, haut +y local)
            float w = size.x / 2f, h = size.y / 2f;
            var mesh = new Mesh
            {
                name = "etiquette_" + name,
                vertices = new[] { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0) },
                uv = new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax) },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = TexMat(png);
            return g;
        }
    }
}
