using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Retroviseurs fonctionnels de la voiture standalone (JD 29/10 : « mettre les retroviseurs fonctionnels ? et celui dans
    // l'habitacle ») : une camera par glace, placee au reflet de l'oeil du joueur dans le plan de la glace, frustum decentre
    // cadre exactement sur la glace (plan proche = la glace : la coque et l'habitacle derriere elle ne sont pas rendus) ; son
    // image va sur la glace, coins pour coins -- le reflet suit la tete, sans shader a part. Reperes : infos.txt, lignes
    // retro_<g|d|i>_centre / _haut / _droite (outils/rf1/r5maxi_retros.py) ; g et d sur les portieres (ils tournent avec).
    // Cout : rendu seulement pour le joueur assis dans CETTE voiture, une glace par image (interieur une image sur deux),
    // petites textures, sans ombres ; dehors, glace sombre.
    public static partial class StandaloneCar
    {
        public class Mirror
        {
            public Transform Glass; public Vector3 Up, Right; public Camera Cam; public RenderTexture Rt; public MeshRenderer Mr;
            public Material Live, Dark; public bool Copied; public string Key;

            public static Mirror Make(VazCar.Built b, Dictionary<string, Vector3> inf, string k, Transform parent, int w, int h, bool oval)
            {
                Vector3 c, up, right;
                if (!inf.TryGetValue("retro_" + k + "_centre", out c) || !inf.TryGetValue("retro_" + k + "_haut", out up) || !inf.TryGetValue("retro_" + k + "_droite", out right)) return null;
                Shader unlit = Shader.Find("Unlit/Texture");
                if (unlit == null) return null;
                var g = new GameObject("retro_" + k);
                g.transform.SetParent(parent, false); g.transform.localPosition = c;
                // glace : ovale (portieres) ou rectangle (interieur) ; u = 0 du cote +right (l'image vue de derriere la glace)
                var mesh = new Mesh { name = "retro_" + k };
                var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                if (oval)
                {
                    const int n = 24;
                    vs.Add(Vector3.zero); uv.Add(new Vector2(0.5f, 0.5f));
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * Mathf.PI * 2f / n, s = Mathf.Cos(a), t = Mathf.Sin(a);
                        vs.Add(right * s + up * t); uv.Add(new Vector2((1f - s) * 0.5f, (1f + t) * 0.5f));
                        // (face vers le conducteur, cote -Cross(right, up), comme les cadrans)
                        tri.Add(0); tri.Add(1 + (i + 1) % n); tri.Add(1 + i);
                    }
                }
                else
                {
                    foreach (var st in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
                    { vs.Add(right * st.x + up * st.y); uv.Add(new Vector2((1f - st.x) * 0.5f, (1f + st.y) * 0.5f)); }
                    tri.AddRange(new[] { 0, 2, 1, 0, 3, 2 });
                }
                mesh.vertices = vs.ToArray(); mesh.uv = uv.ToArray(); mesh.triangles = tri.ToArray();
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = g.AddComponent<MeshRenderer>();
                var rt = new RenderTexture(w, h, 16) { name = "retro_" + k, wrapMode = TextureWrapMode.Clamp };
                var dt = new Texture2D(1, 1); dt.SetPixel(0, 0, new Color(0.16f, 0.18f, 0.20f)); dt.Apply();
                var m = new Mirror { Glass = g.transform, Up = up, Right = right, Rt = rt, Mr = mr, Key = k,
                    Live = new Material(unlit) { mainTexture = rt }, Dark = new Material(unlit) { mainTexture = dt } };
                mr.sharedMaterial = m.Dark;
                var cg = new GameObject("retro_camera_" + k);
                cg.transform.SetParent(b.Car.transform, false);
                m.Cam = cg.AddComponent<Camera>();
                m.Cam.enabled = false;
                Log.Info("standalone : retroviseur " + k + " pose (" + (right.magnitude * 200f).ToString("F0") + " x " + (up.magnitude * 200f).ToString("F0") + " cm, " + w + " x " + h + ")");
                return m;
            }

            // rend la glace vue depuis eye (camera du joueur) ; faux si l'oeil est derriere la glace
            public bool Render(Camera main)
            {
                if (Glass == null || Cam == null || main == null) return false;
                if (!Copied) { Cam.CopyFrom(main); Cam.enabled = false; Copied = true; }
                Cam.targetTexture = Rt;
                Vector3 c = Glass.position, ru = Glass.TransformVector(Right), uu = Glass.TransformVector(Up);
                Vector3 fwd = Vector3.Cross(ru, uu).normalized;   // (vers l'avant de la glace, a l'oppose du joueur)
                Vector3 eye = main.transform.position;
                float de = Vector3.Dot(eye - c, -fwd);
                if (de < 0.02f) return false;
                Vector3 pe = eye + fwd * (2f * de);   // (oeil reflechi, derriere la glace)
                Cam.transform.position = pe;
                Cam.transform.rotation = Quaternion.LookRotation(-fwd, uu);
                Vector3 rc = Cam.transform.right, uc = Cam.transform.up;
                float cx = Vector3.Dot(c - pe, rc), cy = Vector3.Dot(c - pe, uc);
                float hw = Mathf.Abs(Vector3.Dot(ru, rc)) + Mathf.Abs(Vector3.Dot(uu, rc)), hh = Mathf.Abs(Vector3.Dot(uu, uc)) + Mathf.Abs(Vector3.Dot(ru, uc));
                float near = de + 0.004f, far = 300f, s = near / de;
                float l = (cx - hw) * s, r = (cx + hw) * s, bo = (cy - hh) * s, to = (cy + hh) * s;
                var p = new Matrix4x4();
                p[0, 0] = 2f * near / (r - l); p[0, 2] = (r + l) / (r - l);
                p[1, 1] = 2f * near / (to - bo); p[1, 2] = (to + bo) / (to - bo);
                p[2, 2] = -(far + near) / (far - near); p[2, 3] = -(2f * far * near) / (far - near);
                p[3, 2] = -1f;
                Cam.nearClipPlane = near; Cam.farClipPlane = far;
                Cam.projectionMatrix = p;
                float sd = QualitySettings.shadowDistance;
                QualitySettings.shadowDistance = 0f;
                try { Cam.Render(); }
                finally { QualitySettings.shadowDistance = sd; }
                if (Mr.sharedMaterial != Live) Mr.sharedMaterial = Live;
                return true;
            }

            public void Off() { if (Mr != null && Mr.sharedMaterial != Dark) Mr.sharedMaterial = Dark; }

            public void Free()
            {
                if (Rt != null) { Rt.Release(); Object.Destroy(Rt); Rt = null; }
                if (Live != null) Object.Destroy(Live);
                if (Dark != null) { Object.Destroy(Dark.mainTexture); Object.Destroy(Dark); }
            }
        }

        static readonly int[] mirrorOrder = { 2, 0, 2, 1 };   // (interieur une image sur deux, puis gauche, puis droit)

        static void MakeMirrors(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            Transform[] doors = new Transform[2];
            for (int i = 0; i < 2; i++) doors[i] = st.Doors != null && st.Doors[i] != null ? st.Doors[i].Find("pieces") : null;
            st.Mirrors = new[] { Mirror.Make(b, inf, "g", doors[0] ?? b.Body, 192, 112, true), Mirror.Make(b, inf, "d", doors[1] ?? b.Body, 192, 112, true),
                                 Mirror.Make(b, inf, "i", b.Body, 320, 96, false) };
        }

        // apres le recalage des yeux (DriveLate) : seulement pour le joueur assis dans cette voiture
        static void Mirrors(VazCar.Built b, State st)
        {
            if (st.Mirrors == null) return;
            bool inside = driving == b || cabinCar == b;
            Transform ct = inside ? PlayerCam() : null;
            Camera main = ct != null ? ct.GetComponent<Camera>() : null;
            if (main == null)
            {
                if (st.MirrorsLive) { foreach (Mirror m in st.Mirrors) if (m != null) m.Off(); st.MirrorsLive = false; }
                return;
            }
            st.MirrorsLive = true;
            for (int tries = 0; tries < mirrorOrder.Length; tries++)
            {
                Mirror m = st.Mirrors[mirrorOrder[st.MirrorNext]];
                st.MirrorNext = (st.MirrorNext + 1) % mirrorOrder.Length;
                if (m != null && m.Render(main)) break;
            }
        }

        static void FreeMirrors(State st)
        {
            if (st == null || st.Mirrors == null) return;
            foreach (Mirror m in st.Mirrors) if (m != null) m.Free();
            st.Mirrors = null;
        }
    }
}
