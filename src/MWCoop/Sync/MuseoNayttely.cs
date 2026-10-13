using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWCoop
{
    // Musee de la Corse : l'exposition. Les images (tableaux, cartes, cartes postales, photos de rallye d'avant 2000, toutes
    // libres ou sous licence Creative Commons, auteurs credites dans le lanceur) et leurs plaques FR/EN sont des fichiers a
    // part, MWCoop\musee\*.jpg / *-kyltti.png, places d'apres MWCoop\musee\nayttely.txt :
    //   nom|genre|x|y|z|face|largeur[|plaque]   (plaque : rien = a droite, ala = dessous, ei = aucune)
    // genre : maalaus (tableau, cadre dore), kuva (photo, passe-partout blanc et cadre noir), kartta (carte, cadre bois
    // sombre), otsikko (titre de salle, sans cadre ni plaque) ; face : px nx pz nz (sens de la normale du mur, repere du
    // musee) ; x y z : centre de l'image ; largeur en metres (la hauteur suit l'image).
    public static partial class Kauppa
    {
        static string MuseoDir { get { return Path.Combine(Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "musee"); } }

        static Texture2D FileTex(string path, bool alpha)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, true) { name = "museo_" + Path.GetFileNameWithoutExtension(path) };
            t.LoadImage(File.ReadAllBytes(path));
            t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 4;
            return t;
        }

        static int unlitLogged;
        static void TexMat(string key, Texture2D t)
        {
            // images et plaques sans ombrage, comme sous des spots de galerie (eclairees pareil le jour et la nuit) ;
            // a defaut, le diffus des murs un peu force
            Shader unlit = Shader.Find("Unlit/Texture");
            Material basis = GameMat("wood_panel7");
            var m = new Material(unlit ?? (basis != null ? basis.shader : Shader.Find("Legacy Shaders/Diffuse"))) { mainTexture = t, name = "osuus_" + key };
            if (unlit == null && m.HasProperty("_Color")) m.color = new Color(1.12f, 1.12f, 1.12f);
            if (unlitLogged++ == 0) L("musee : images " + (unlit != null ? "sans ombrage (Unlit/Texture)" : "en diffus, Unlit/Texture absent"));
            mats[key] = m; tiles[key] = 1f;
        }

        // boite donnee dans le repere d'un mur : u a droite (vu de face), v en haut, d vers la salle
        static void WallBox(Vector3 c, Vector3 n, float u0, float u1, float v0, float v1, float d0, float d1, string key)
        {
            Vector3 r = Vector3.Cross(Vector3.up, -n).normalized;
            Vector3 a = c + r * u0 + Vector3.up * v0 + n * d0, b = c + r * u1 + Vector3.up * v1 + n * d1;
            Box(Vector3.Min(a, b), Vector3.Max(a, b), key, false);
        }

        static void MuseoExhibits()
        {
            Mat("mu_kulta", "metal_galvanized", 1f, new Color(0.86f, 0.66f, 0.3f));   // cadres dores
            Mat("mu_musta", "metal_galvanized", 1f, new Color(0.08f, 0.08f, 0.09f));  // cadres noirs
            Mat("mu_tumma", "wood12", 1f, new Color(0.45f, 0.3f, 0.2f));                 // bois sombre
            Mat("mu_passe", "wood_panel20", 4f, new Color(1f, 0.99f, 0.96f));          // passe-partout
            {
                // passe-partout sans ombrage comme les images (sinon violet dans la lumiere bleutee des fenetres)
                var white = new Texture2D(4, 4, TextureFormat.RGB24, false) { name = "museo_passe" };
                var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = new Color(0.93f, 0.91f, 0.86f);
                white.SetPixels(px); white.Apply();
                if (Shader.Find("Unlit/Texture") != null) TexMat("mu_passe", white);
            }
            string list = Path.Combine(MuseoDir, "nayttely.txt");
            if (!File.Exists(list)) { L("musee : pas d'exposition (" + list + ")"); return; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int n = 0, missing = 0;
            foreach (string raw in File.ReadAllLines(list))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] a = line.Split('|');
                if (a.Length < 7) continue;
                try
                {
                    string name = a[0].Trim(), kind = a[1].Trim();
                    Vector3 c = new Vector3(float.Parse(a[2], ci), float.Parse(a[3], ci), float.Parse(a[4], ci));
                    string f = a[5].Trim();
                    Vector3 nrm = f == "px" ? Vector3.right : f == "nx" ? Vector3.left : f == "pz" ? Vector3.forward : Vector3.back;
                    float w = float.Parse(a[6], ci);
                    bool png = kind == "otsikko";
                    Texture2D t = FileTex(Path.Combine(MuseoDir, name + (png ? ".png" : ".jpg")), false);
                    if (t == null) { missing++; continue; }
                    string key = "mu_img_" + name;
                    TexMat(key, t);
                    float h = w * t.height / (float)t.width;
                    Exhibit(kind, key, c, nrm, w, h);
                    string mode = a.Length > 7 ? a[7].Trim() : "";
                    Texture2D pl = kind == "otsikko" || mode == "ei" ? null : FileTex(Path.Combine(MuseoDir, name + "-kyltti.png"), false);
                    if (pl != null)
                    {
                        string pk = key + "_k";
                        TexMat(pk, pl);
                        float pw = 0.42f, ph = pw * pl.height / (float)pl.width;
                        float frame = kind == "kuva" ? 0.16f : 0.1f;
                        // plaque a droite du cadre, a hauteur des yeux si l'image est haute, sinon sous le coin droit
                        Vector3 r = Vector3.Cross(Vector3.up, -nrm).normalized;
                        Vector3 pc = mode == "ala" ? c + Vector3.up * (-h / 2 - frame - 0.06f - ph / 2)
                            : c + r * (w / 2 + frame + 0.12f + pw / 2) + Vector3.up * Mathf.Clamp(1.45f - c.y, -h / 2 + ph / 2, h / 2 - ph / 2);
                        WallBox(pc, nrm, -pw / 2 - 0.012f, pw / 2 + 0.012f, -ph / 2 - 0.012f, ph / 2 + 0.012f, 0f, 0.012f, "mu_musta");
                        Plane(pk, pc + nrm * 0.014f, pw, ph, nrm);
                    }
                    n++;
                }
                catch (Exception e) { L("musee : ligne illisible '" + line + "' : " + e.Message); }
            }
            MuseoRelief();
            L("musee : " + n + " piece(s) exposee(s)" + (missing > 0 ? ", " + missing + " image(s) absente(s)" : ""));
        }

        static void Exhibit(string kind, string key, Vector3 c, Vector3 n, float w, float h)
        {
            float hw = w / 2, hh = h / 2;
            if (kind == "otsikko") { WallBox(c, n, -hw - 0.02f, hw + 0.02f, -hh - 0.02f, hh + 0.02f, 0f, 0.02f, "mu_tumma"); Plane(key, c + n * 0.022f, w, h, n); return; }
            if (kind == "kuva")
            {
                // passe-partout blanc puis cadre noir fin
                float m = 0.1f, f = 0.035f;
                WallBox(c, n, -hw - m, hw + m, -hh - m, hh + m, 0f, 0.02f, "mu_passe");
                Plane(key, c + n * 0.022f, w, h, n);
                Frame(c, n, hw + m, hh + m, f, 0.045f, "mu_musta");
                return;
            }
            // tableau / carte : toile sur chassis, cadre large
            float fw = kind == "kartta" ? 0.07f : 0.1f;
            WallBox(c, n, -hw, hw, -hh, hh, 0f, 0.03f, kind == "kartta" ? "mu_tumma" : "mu_kulta");
            Plane(key, c + n * 0.032f, w, h, n);
            Frame(c, n, hw, hh, fw, 0.07f, kind == "kartta" ? "mu_tumma" : "mu_kulta");
            if (kind == "maalaus") Frame(c, n, hw + fw, hh + fw, 0.025f, 0.05f, "mu_tumma");
        }

        // cadre (4 baguettes) autour d'un rectangle de demi-taille hw x hh
        static void Frame(Vector3 c, Vector3 n, float hw, float hh, float f, float depth, string key)
        {
            WallBox(c, n, -hw - f, hw + f, hh, hh + f, 0f, depth, key);
            WallBox(c, n, -hw - f, hw + f, -hh - f, -hh, 0f, depth, key);
            WallBox(c, n, -hw - f, -hw, -hh, hh, 0f, depth, key);
            WallBox(c, n, hw, hw + f, -hh, hh, 0f, depth, key);
        }

        // mobilier : cimaises (murs d'accrochage) au milieu des ailes, banquettes, pupitre de la carte
        static void MuseoFurniture()
        {
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * 6.6f;
                Box(new Vector3(x - 0.1f, 0f, -2.7f), new Vector3(x + 0.1f, 2.9f, 2.7f), "mu_valko", "mu_valko", "lista", null, "mu_valko", "mu_valko");
                Box(new Vector3(x - 0.13f, 0f, -2.73f), new Vector3(x + 0.13f, 0.1f, 2.73f), "mu_tumma", false);       // plinthe
                Box(new Vector3(x - 0.13f, 2.9f, -2.73f), new Vector3(x + 0.13f, 2.95f, 2.73f), "mu_tumma", false);    // couvre-mur
            }
            // banquettes du hall (bois sombre sur pieds noirs)
            foreach (float z in new[] { -2.5f })
            {
                Box(new Vector3(-1.1f, 0.42f, z - 0.22f), new Vector3(1.1f, 0.48f, z + 0.22f), "mu_tumma");
                foreach (float x in new[] { -0.95f, 0.95f })
                    Box(new Vector3(x - 0.04f, 0f, z - 0.18f), new Vector3(x + 0.04f, 0.42f, z + 0.18f), "mu_musta", false);
            }
        }

        // maquette en relief de la Corse au milieu du hall, sous vitrine : grille d'altitudes (korsika-relief.bin :
        // nx, ny, largeur reelle en m, puis nx*ny altitudes en m, mer < 0) et texture teintee (korsika-relief.jpg).
        // Nord vers le fond du hall (-z), donc l'est a -x (a droite vu de l'entree) ; relief exagere 5 fois.
        static void MuseoRelief()
        {
            string bin = Path.Combine(MuseoDir, "korsika-relief.bin");
            Texture2D tex = FileTex(Path.Combine(MuseoDir, "korsika-relief.jpg"), false);
            if (!File.Exists(bin) || tex == null) { L("musee : pas de maquette"); return; }
            int nx, ny; float realW; float[] hgt;
            using (var r = new BinaryReader(File.OpenRead(bin)))
            {
                nx = r.ReadInt32(); ny = r.ReadInt32(); realW = r.ReadSingle();
                hgt = new float[nx * ny];
                for (int i = 0; i < hgt.Length; i++) hgt[i] = r.ReadSingle();
            }
            const float w = 1.3f, cz = 1.0f, top = 0.8f, exag = 5f;
            float d = w * ny / nx, s = w / realW;
            // table et vitrine
            Box(new Vector3(-w / 2 - 0.12f, 0f, cz - d / 2 - 0.12f), new Vector3(w / 2 + 0.12f, top, cz + d / 2 + 0.12f), "mu_tumma");
            Box(new Vector3(-w / 2 - 0.15f, top - 0.04f, cz - d / 2 - 0.15f), new Vector3(w / 2 + 0.15f, top + 0.006f, cz + d / 2 + 0.15f), "mu_kulta", false);
            float gy = top + 0.45f, gx = w / 2 + 0.1f, gz = d / 2 + 0.1f;
            Box(new Vector3(-gx, top, cz - gz), new Vector3(gx, gy, cz + gz), "lasi", "lasi", "lasi", null, "lasi", "lasi");
            Box(new Vector3(-gx, top, cz - gz), new Vector3(gx, gy, cz + gz), null, null, null, "lasi", null, null, false);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Box(new Vector3(sx * gx - 0.015f, top, cz + sz * gz - 0.015f), new Vector3(sx * gx + 0.015f, gy, cz + sz * gz + 0.015f), "mu_kulta", false);
            // relief
            var v = new Vector3[nx * ny]; var uv = new Vector2[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float h = hgt[j * nx + i];
                    float y = top + 0.012f + (h > 0 ? h * s * exag + 0.004f : 0f);
                    v[j * nx + i] = new Vector3(w / 2 - w * i / (nx - 1), y, cz - d / 2 + d * j / (ny - 1));
                    uv[j * nx + i] = new Vector2(i / (float)(nx - 1), 1f - j / (float)(ny - 1));
                }
            var t = new int[(nx - 1) * (ny - 1) * 6]; int k = 0;
            for (int j = 0; j + 1 < ny; j++)
                for (int i = 0; i + 1 < nx; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, e = c + 1;
                    t[k++] = a; t[k++] = b; t[k++] = c; t[k++] = b; t[k++] = e; t[k++] = c;
                }
            var m = new Mesh { name = "museo_korsika" };
            m.vertices = v; m.uv = uv; m.triangles = t;
            m.RecalculateNormals(); m.RecalculateBounds();
            var g = new GameObject("museo_korsika");
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            Material basis = GameMat("wood_panel7");
            var mat = new Material(basis != null ? basis.shader : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = tex, name = "museo_korsika" };
            if (mat.HasProperty("_Color")) mat.color = new Color(1.25f, 1.25f, 1.25f);
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // plaque sur le devant de la table
            Texture2D pl = FileTex(Path.Combine(MuseoDir, "M-relief-kyltti.png"), false);
            if (pl != null)
            {
                TexMat("mu_relief_k", pl);
                float pw = 0.5f, ph = pw * pl.height / (float)pl.width;
                Plane("mu_relief_k", new Vector3(0, top - 0.06f - ph / 2, cz + d / 2 + 0.121f), pw, ph, Vector3.forward);
            }
            L("musee : maquette de la Corse " + nx + "x" + ny + " (" + (realW / 1000f).ToString("F0") + " km de large)");
        }
    }
}
