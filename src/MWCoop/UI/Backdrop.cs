using UnityEngine;

namespace MWCoop
{
    // Flou derriere les panneaux « verre » (Style.Glass). Un composant pose sur la derniere camera plein ecran
    // recopie son image (OnRenderImage), la reduit par moities jusqu'au 1/32 puis la remonte au 1/8 (filtrage
    // bilineaire : flou doux, ~1 ms). PIEGE : Graphics.Blit change la cible active ; sur la derniere camera
    // (menu principal, camera GUI du jeu) la cible laissee recoit ensuite le reste de l'image (interface) : la copie
    // vers l'ecran (src -> dst) se fait donc EN DERNIER (sinon l'image n'arrive pas a l'ecran normalement, captures
    // du jeu jamais ecrites, vu le 05/10). Une CommandBuffer AfterEverything lisait du noir (Unity 5.0, D3D11).
    // Le composant ne travaille que si un panneau verre a ete dessine dans la demi-seconde, sinon il se coupe.
    // Le panneau est un maillage arrondi (GL) qui lit ce flou aux coordonnees de l'ecran.
    public class Backdrop : MonoBehaviour
    {
        static Backdrop inst;
        static Camera cam;
        static float wantedUntil, nextFind;
        static RenderTexture blur;
        static int blurFrame = -10;
        static bool logged;

        static bool Off { get { return Config.GetInt("Coop", "Flou", 1) == 0 || Config.GetInt("Test", "SansFlou", 0) == 1; } }

        // Demande le flou pour cette image et les suivantes (appele par Style.Glass a chaque passage).
        public static void Want()
        {
            if (Off) return;
            float now = Time.realtimeSinceStartup;
            wantedUntil = now + 0.5f;
            // Camera revue chaque seconde (le jeu en change au chargement) : la derniere a dessiner a l'ecran entier.
            if (now >= nextFind)
            {
                nextFind = now + 1f;
                Camera c = null;
                foreach (Camera x in Camera.allCameras)
                    if (x.targetTexture == null && x.isActiveAndEnabled && x.rect.width > 0.99f && x.rect.height > 0.99f && (c == null || x.depth > c.depth)) c = x;
                if (c != null && c != cam)
                {
                    if (inst != null) Destroy(inst);
                    cam = c;
                    inst = c.gameObject.GetComponent<Backdrop>();
                    if (inst == null) inst = c.gameObject.AddComponent<Backdrop>();
                    var sb = new System.Text.StringBuilder("interface : flou des panneaux sur la camera " + Recon.Path(c.transform) + " ; cameras :");
                    foreach (Camera x in Camera.allCameras)
                        sb.Append(" [").Append(Recon.Path(x.transform)).Append(" profondeur ").Append(x.depth).Append(" efface ").Append(x.clearFlags)
                          .Append(x.tag == "MainCamera" ? " main" : "").Append("]");
                    Log.Info(sb.ToString());
                }
            }
            if (inst != null && !inst.enabled) inst.enabled = true;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (Time.realtimeSinceStartup <= wantedUntil)
            {
                try
                {
                    int w = src.width, h = src.height;
                    RenderTexture a = Down(src, w / 2, h / 2);
                    RenderTexture b = Down(a, w / 4, h / 4); RenderTexture.ReleaseTemporary(a);
                    RenderTexture c = Down(b, w / 8, h / 8); RenderTexture.ReleaseTemporary(b);
                    RenderTexture d = Down(c, w / 16, h / 16);
                    RenderTexture e = Down(d, w / 32, h / 32);
                    Graphics.Blit(e, d); RenderTexture.ReleaseTemporary(e);
                    Graphics.Blit(d, c); RenderTexture.ReleaseTemporary(d);
                    if (blur == null || blur.width != c.width || blur.height != c.height)
                    {
                        if (blur != null) { blur.Release(); Destroy(blur); }
                        blur = new RenderTexture(c.width, c.height, 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                        blur.Create();
                    }
                    Graphics.Blit(c, blur);
                    RenderTexture.ReleaseTemporary(c);
                    blurFrame = Time.frameCount;
                }
                catch (System.Exception ex) { if (!logged) { logged = true; Log.Error("interface : flou impossible : " + ex.Message); } wantedUntil = 0; }
            }
            else enabled = false;
            Graphics.Blit(src, dst);   // en dernier : la cible active redevient celle de la camera
        }

        static RenderTexture Down(RenderTexture from, int w, int h)
        {
            RenderTexture t = RenderTexture.GetTemporary(Mathf.Max(1, w), Mathf.Max(1, h), 0);
            t.filterMode = FilterMode.Bilinear;
            Graphics.Blit(from, t);
            return t;
        }

        // Dessine le flou dans un rectangle arrondi (coordonnees GUI). Faux s'il n'y a pas de flou recent, ou pendant
        // un fondu : l'alpha de l'image des cameras n'est pas fiable, on la pose opaque (Unlit/Texture, qui l'ignore).
        public static bool Draw(Rect r, float radius, float alpha)
        {
            if (blur == null || Time.frameCount - blurFrame > 2 || alpha < 0.99f) return false;
            if (!Opaque()) return false;
            opaque.mainTexture = blur;
            opaque.SetPass(0);
            Mesh(r, radius, new Rect(0, 0, Screen.width, Screen.height), 1f);
            return true;
        }

        // Image (apercu 3D d'une tenue) dans un rectangle arrondi, sans tenir compte de son alpha.
        // Faux sans ce shader ou pendant un fondu : l'appelant la dessine carree.
        public static bool DrawImage(Rect r, float radius, Texture tex, float alpha)
        {
            if (tex == null || Event.current.type != EventType.Repaint || alpha < 0.99f || !Opaque()) return false;
            opaque.mainTexture = tex;
            opaque.SetPass(0);
            Mesh(r, radius, r, 1f);
            return true;
        }

        static Material opaque;
        static bool opaqueTried;

        static bool Opaque()
        {
            if (!opaqueTried)
            {
                opaqueTried = true;
                Shader sh = Shader.Find("Unlit/Texture");
                if (sh != null) opaque = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                Log.Info("interface : verre et apercus arrondis " + (sh != null ? "avec Unlit/Texture" : "impossibles (pas de Unlit/Texture) : panneaux opaques"));
            }
            return opaque != null;
        }

        // Maillage d'un rectangle arrondi (eventail depuis le centre, 8 segments par coin), coordonnees GUI ;
        // uv : position dans uvSpace (v vers le haut, comme une texture rendue).
        static void Mesh(Rect r, float radius, Rect uvSpace, float alpha)
        {
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(1f, 1f, 1f, alpha));
            float rad = Mathf.Max(0f, Mathf.Min(radius - 0.5f, Mathf.Min(r.width, r.height) / 2f));
            Vector2 center = r.center;
            const int seg = 8;
            Vector2 prev = Vector2.zero, start = Vector2.zero;
            bool first = true;
            // Quatre coins (centre de l'arc, angle de depart), dans le sens horaire a l'ecran.
            for (int k = 0; k < 4; k++)
            {
                Vector2 cc = k == 0 ? new Vector2(r.xMax - rad, r.y + rad) : k == 1 ? new Vector2(r.xMax - rad, r.yMax - rad)
                           : k == 2 ? new Vector2(r.x + rad, r.yMax - rad) : new Vector2(r.x + rad, r.y + rad);
                float a0 = -90f + 90f * k;
                for (int i = 0; i <= seg; i++)
                {
                    float an = (a0 + 90f * i / seg) * Mathf.Deg2Rad;
                    var p = new Vector2(cc.x + Mathf.Cos(an) * rad, cc.y + Mathf.Sin(an) * rad);
                    if (first) { start = p; first = false; }
                    else { V(center, uvSpace); V(prev, uvSpace); V(p, uvSpace); }
                    prev = p;
                }
            }
            V(center, uvSpace); V(prev, uvSpace); V(start, uvSpace);
            GL.End();
            GL.PopMatrix();
        }

        static void V(Vector2 p, Rect uv)
        {
            GL.TexCoord2((p.x - uv.x) / uv.width, 1f - (p.y - uv.y) / uv.height);
            GL.Vertex3(p.x, p.y, 0f);
        }
    }
}
