using System;
using System.Collections.Generic;
using UnityEngine;

namespace Showroom
{
    // La salle (JD 28/10) : une piece gris fonce de 20 x 20 x 7 m hors de la carte (inaccessible a pied, 150 m en l'air), cinq
    // projecteurs au plafond braques sur le centre, et un tableau au mur sud : un interrupteur par projecteur (et « tout »),
    // le choix d'une voiture du F10 de MWCoop (precedente / suivante) et « sortir la voiture » (posee au centre).
    // Dedans, le soleil, le brouillard et la lumiere ambiante du jeu sont coupes : seuls les projecteurs eclairent.
    static class Salle
    {
        public static readonly Vector3 C = new Vector3(3200f, 150f, -3200f);   // (centre du sol)
        const float L = 10f, H = 7f;                                            // (demi-largeur, hauteur)
        public static readonly Vector3 Entree = C + new Vector3(0f, 0f, -7.6f);  // (pieds du joueur a l'arrivee, face au centre)
        public static GameObject Root;
        public static Light[] Spots = new Light[5];
        static Renderer[] lenses = new Renderer[5];
        static Material lensOn, lensOff, btnOn, btnOff;

        public class Bouton { public string Act; public int Index; public BoxCollider Col; public Renderer R; }
        public static readonly List<Bouton> Boutons = new List<Bouton>();
        public static TextMesh NomVoiture;

        static Shader Diffuse { get { return Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse"); } }
        static Material Mat(Color c, bool spec = false)
        {
            Shader sh = spec ? (Shader.Find("Legacy Shaders/Specular") ?? Shader.Find("Specular") ?? Diffuse) : Diffuse;
            var m = new Material(sh) { color = c };
            if (spec && m.HasProperty("_Shininess")) m.SetFloat("_Shininess", 0.35f);
            if (spec && m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(0.35f, 0.35f, 0.35f));
            return m;
        }
        static Material Unlit(Color c)
        {
            Shader sh = Shader.Find("Unlit/Color") ?? Diffuse;
            return new Material(sh) { color = c };
        }

        static GameObject Bloc(string name, Vector3 centre, Vector3 taille, Material m, bool collider = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(Root.transform, false);
            g.transform.position = centre; g.transform.localScale = taille;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            return g;
        }

        public static void Build()
        {
            if (Root != null) return;
            Root = new GameObject("SHOWROOM");
            Boutons.Clear(); Textes.Clear();
            Material sol = Mat(new Color(0.13f, 0.13f, 0.14f), true), mur = Mat(new Color(0.22f, 0.22f, 0.23f)), plafond = Mat(new Color(0.12f, 0.12f, 0.13f));
            float e = 0.4f;
            Bloc("sol", C + new Vector3(0f, -e / 2f, 0f), new Vector3(2 * L + 2 * e, e, 2 * L + 2 * e), sol);
            Bloc("plafond", C + new Vector3(0f, H + e / 2f, 0f), new Vector3(2 * L + 2 * e, e, 2 * L + 2 * e), plafond);
            Bloc("mur_n", C + new Vector3(0f, H / 2f, L + e / 2f), new Vector3(2 * L + 2 * e, H, e), mur);
            Bloc("mur_s", C + new Vector3(0f, H / 2f, -L - e / 2f), new Vector3(2 * L + 2 * e, H, e), mur);
            Bloc("mur_e", C + new Vector3(L + e / 2f, H / 2f, 0f), new Vector3(e, H, 2 * L), mur);
            Bloc("mur_o", C + new Vector3(-L - e / 2f, H / 2f, 0f), new Vector3(e, H, 2 * L), mur);
            // plinthe et cercle du centre, un peu plus clairs (repere de la voiture sur les photos)
            var disque = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disque.name = "centre"; disque.transform.SetParent(Root.transform, false);
            disque.transform.position = C + new Vector3(0f, 0.005f, 0f); disque.transform.localScale = new Vector3(7f, 0.005f, 7f);
            disque.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.17f, 0.17f, 0.18f), true);
            UnityEngine.Object.Destroy(disque.GetComponent<Collider>());

            // projecteurs : quatre aux coins du plafond, un au-dessus du centre ; boitier noir, verre allume / eteint
            lensOn = Unlit(new Color(1f, 0.95f, 0.82f)); lensOff = Mat(new Color(0.05f, 0.05f, 0.05f));
            Material boitier = Mat(new Color(0.04f, 0.04f, 0.045f), true);
            Vector3[] at = { new Vector3(-6.5f, 6.6f, -6.5f), new Vector3(6.5f, 6.6f, -6.5f), new Vector3(-6.5f, 6.6f, 6.5f), new Vector3(6.5f, 6.6f, 6.5f), new Vector3(0f, 6.6f, 0.01f) };
            Vector3 cible = C + new Vector3(0f, 0.7f, 0f);
            for (int i = 0; i < 5; i++)
            {
                var pivot = new GameObject("projecteur_" + (i + 1));
                pivot.transform.SetParent(Root.transform, false);
                pivot.transform.position = C + at[i];
                pivot.transform.rotation = Quaternion.LookRotation(cible - pivot.transform.position, Vector3.up);
                var tige = GameObject.CreatePrimitive(PrimitiveType.Cylinder);   // (tige jusqu'au plafond)
                tige.transform.SetParent(Root.transform, false);
                tige.transform.position = C + at[i] + new Vector3(0f, 0.2f, 0f); tige.transform.localScale = new Vector3(0.05f, 0.2f, 0.05f);
                tige.GetComponent<Renderer>().sharedMaterial = boitier; UnityEngine.Object.Destroy(tige.GetComponent<Collider>());
                var corps = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                corps.transform.SetParent(pivot.transform, false);
                corps.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); corps.transform.localScale = new Vector3(0.36f, 0.25f, 0.36f);
                corps.GetComponent<Renderer>().sharedMaterial = boitier; UnityEngine.Object.Destroy(corps.GetComponent<Collider>());
                var verre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                verre.transform.SetParent(pivot.transform, false);
                verre.transform.localPosition = new Vector3(0f, 0f, 0.255f); verre.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                verre.transform.localScale = new Vector3(0.30f, 0.01f, 0.30f);
                lenses[i] = verre.GetComponent<Renderer>(); UnityEngine.Object.Destroy(verre.GetComponent<Collider>());
                var lg = new GameObject("lumiere");
                lg.transform.SetParent(pivot.transform, false);
                lg.transform.localPosition = new Vector3(0f, 0f, 0.3f);
                Light l = lg.AddComponent<Light>();
                l.type = LightType.Spot; l.color = new Color(1f, 0.96f, 0.88f); l.range = 18f; l.spotAngle = i == 4 ? 55f : 42f;
                l.intensity = i == 4 ? 2.2f : 2.8f; l.shadows = LightShadows.Soft; l.shadowStrength = 0.85f;
                l.renderMode = LightRenderMode.ForcePixel;
                Spots[i] = l;
            }

            // tableau au mur sud (lu depuis la salle : on regarde vers -z ; sa droite est -x)
            float zw = C.z - L;   // (face interieure du mur ; la salle est du cote +z)
            Quaternion face = Quaternion.Euler(0f, 180f, 0f);
            Bloc("tableau", new Vector3(C.x, C.y + 1.35f, zw + 0.01f), new Vector3(3.6f, 1.9f, 0.02f), Unlit(new Color(0.07f, 0.07f, 0.075f)), false);
            btnOn = Unlit(new Color(1f, 0.78f, 0.25f)); btnOff = Unlit(new Color(0.42f, 0.42f, 0.45f));   // (non eclaires : lisibles projecteurs eteints)
            Func<float, float, Vector3> P = (xr, y) => new Vector3(C.x - xr, C.y + y, zw + 0.04f);   // (boutons : 1 a 7 cm du mur)
            Color blanc = new Color(0.92f, 0.92f, 0.92f), gris = new Color(0.65f, 0.65f, 0.68f);
            Textes.Make(Root.transform, Jeu.T("PROJECTEURS", "SPOTLIGHTS"), P(0f, 2.12f) + new Vector3(0f, 0f, -0.015f), face, 0.16f, gris);
            for (int i = 0; i < 5; i++)
            {
                Bouton b = AddBouton("spot", i, P(-1.2f + i * 0.5f, 1.78f), new Vector3(0.30f, 0.30f, 0.06f));
                Textes.Make(Root.transform, (i + 1).ToString(), P(-1.2f + i * 0.5f, 1.78f) + new Vector3(0f, 0f, 0.035f), face, 0.14f, new Color(0.08f, 0.08f, 0.08f));
            }
            AddBouton("tout", 0, P(1.38f, 1.78f), new Vector3(0.42f, 0.30f, 0.06f));
            Textes.Make(Root.transform, Jeu.T("TOUT", "ALL"), P(1.38f, 1.78f) + new Vector3(0f, 0f, 0.035f), face, 0.11f, new Color(0.08f, 0.08f, 0.08f));
            Textes.Make(Root.transform, Jeu.T("VOITURE", "CAR"), P(0f, 1.38f) + new Vector3(0f, 0f, -0.015f), face, 0.16f, gris);
            AddBouton("prec", 0, P(-1.38f, 1.06f), new Vector3(0.36f, 0.30f, 0.06f));
            Textes.Make(Root.transform, "<", P(-1.38f, 1.06f) + new Vector3(0f, 0f, 0.035f), face, 0.18f, new Color(0.08f, 0.08f, 0.08f));
            AddBouton("suiv", 0, P(1.38f, 1.06f), new Vector3(0.36f, 0.30f, 0.06f));
            Textes.Make(Root.transform, ">", P(1.38f, 1.06f) + new Vector3(0f, 0f, 0.035f), face, 0.18f, new Color(0.08f, 0.08f, 0.08f));
            NomVoiture = Textes.Make(Root.transform, "-", P(0f, 1.06f) + new Vector3(0f, 0f, -0.015f), face, 0.15f, blanc);
            AddBouton("sortir", 0, P(0f, 0.66f), new Vector3(1.9f, 0.30f, 0.06f));
            Textes.Make(Root.transform, Jeu.T("SORTIR LA VOITURE AU CENTRE", "BRING THE CAR TO THE CENTRE"), P(0f, 0.66f) + new Vector3(0f, 0f, 0.035f), face, 0.10f, new Color(0.08f, 0.08f, 0.08f));
            Journal.Info("salle construite en " + C + " (" + Boutons.Count + " boutons)");
        }

        static Bouton AddBouton(string act, int index, Vector3 at, Vector3 size)
        {
            GameObject g = Bloc("bouton_" + act + index, at, size, btnOff);
            var b = new Bouton { Act = act, Index = index, Col = g.GetComponent<BoxCollider>(), R = g.GetComponent<Renderer>() };
            Boutons.Add(b);
            return b;
        }

        public static void SetSpot(int i, bool on)
        {
            if (Spots[i] == null) return;
            Spots[i].enabled = on;
            lenses[i].sharedMaterial = on ? lensOn : lensOff;
            foreach (Bouton b in Boutons) if (b.Act == "spot" && b.Index == i) b.R.sharedMaterial = on ? btnOn : btnOff;
        }

        public static bool Inside(Vector3 p) { return Mathf.Abs(p.x - C.x) < L + 1f && Mathf.Abs(p.z - C.z) < L + 1f && p.y > C.y - 3f && p.y < C.y + H + 2f; }
    }
}
