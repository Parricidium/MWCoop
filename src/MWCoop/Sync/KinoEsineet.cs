using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Cinema KINO TAHTI : objets achetes au comptoir (popcorn, chips, boisson, bonbons) et bobine de film (vendue a
    // l'Osuuskauppa). Des objets du coin video (Esine : pris en main, poses, sauvegardes, envoyes a tous).
    //  - a manger / boire : en main, F = une bouchee (Data = bouchees restantes) ; la derniere, l'emballage disparait.
    //    Faim (PlayerHunger) et soif (PlayerThirst) du jeu baissent, avec ses sons (PlayerMisc : eat_chips, eating, drinking).
    //  - bobine : comme une cassette (Data = "tyhja" ou "f1+f2~titre"), mise sur le projecteur d'une salle (KinoSali).
    public static partial class Kauppa
    {
        public const string KPopcorn = "popcorn", KSipsit = "sipsit", KLimsa = "limsa", KKarkit = "karkit", KKela = "kela";
        public const int KelaPrice = 49;

        static bool IsKinoKind(string k) { return k == KPopcorn || k == KSipsit || k == KLimsa || k == KKarkit || k == KKela; }
        static bool IsFood(string k) { return k == KPopcorn || k == KSipsit || k == KLimsa || k == KKarkit; }
        static string KinoItemName(string k) { return k == KPopcorn ? "popcorn" : k == KSipsit ? "potato chips" : k == KLimsa ? "soda" : k == KKarkit ? "candy" : "film reel"; }
        static float KinoItemMass(string k) { return k == KKela ? 1.2f : k == KLimsa ? 0.4f : 0.2f; }

        // bouchees par emballage, et ce que fait chacune
        static int Portions(string k) { return k == KPopcorn ? 5 : k == KSipsit ? 4 : k == KLimsa ? 4 : 3; }
        static void Bite(string k)
        {
            if (k == KLimsa) { Game.SetGlobalFloat("PlayerThirst", Mathf.Max(0f, Game.GlobalFloat("PlayerThirst") - 9f)); }
            else
            {
                float h = k == KPopcorn ? 5f : k == KSipsit ? 4f : 2.5f;
                Game.SetGlobalFloat("PlayerHunger", Mathf.Max(0f, Game.GlobalFloat("PlayerHunger") - h));
                if (k == KKarkit) Game.SetGlobalFloat("PlayerStress", Mathf.Max(0f, Game.GlobalFloat("PlayerStress") - 3f));
                if (k == KSipsit || k == KPopcorn) Game.SetGlobalFloat("PlayerThirst", Game.GlobalFloat("PlayerThirst") + 1f);   // (sale : un peu soif)
            }
            Transform cam = Game.MainCam != null ? Game.MainCam.transform : null;
            string v = k == KLimsa ? "drinking" : k == KSipsit || k == KPopcorn ? "eat_chips" : "eating";
            try { if (cam != null) MasterAudio.PlaySound3DAtVector3AndForget("PlayerMisc", cam.position, 1f, null, 0f, v); } catch { }
        }

        // ---------------------------------------------------------------- modeles
        static bool KinoModel(string kind, GameObject g, out Vector3 size)
        {
            size = Vector3.one * 0.1f;
            if (!IsKinoKind(kind)) return false;
            if (blackPlastic == null) { blackPlastic = Plain(new Color(0.07f, 0.07f, 0.08f)); greyMetal = Plain(new Color(0.2f, 0.2f, 0.21f)); }
            if (kind == KPopcorn)
            {
                // pot a rayures (cylindre), dome de popcorn jaune dessus
                size = new Vector3(0.17f, 0.24f, 0.17f);
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, -0.02f, 0), Vector3.zero, new Vector3(0.16f, 0.1f, 0.16f), TexMat("kino-popcorn.png"));
                var pop = Plain(new Color(1f, 0.9f, 0.55f));
                var rnd = new System.Random(3);
                for (int i = 0; i < 14; i++)
                {
                    float a = i * 2.4f, rr = 0.02f + (float)rnd.NextDouble() * 0.045f;
                    Piece(g, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * rr, 0.085f + (float)rnd.NextDouble() * 0.03f, Mathf.Sin(a) * rr), Vector3.zero, Vector3.one * (0.035f + (float)rnd.NextDouble() * 0.015f), pop);
                }
            }
            else if (kind == KSipsit)
            {
                size = new Vector3(0.17f, 0.24f, 0.06f);
                Piece(g, PrimitiveType.Cube, Vector3.zero, Vector3.zero, new Vector3(0.17f, 0.24f, 0.055f), Plain(new Color(0.95f, 0.6f, 0.1f)));
                Piece(g, PrimitiveType.Quad, new Vector3(0, 0, -0.0285f), Vector3.zero, new Vector3(0.165f, 0.235f, 1), TexMat("kino-sipsit.png"));
                Piece(g, PrimitiveType.Quad, new Vector3(0, 0, 0.0285f), new Vector3(0, 180, 0), new Vector3(0.165f, 0.235f, 1), TexMat("kino-sipsit.png"));
            }
            else if (kind == KLimsa)
            {
                // gobelin carton, couvercle blanc, paille rouge
                size = new Vector3(0.09f, 0.2f, 0.09f);
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, -0.02f, 0), Vector3.zero, new Vector3(0.085f, 0.075f, 0.085f), TexMat("kino-limsa.png"));
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, 0.058f, 0), Vector3.zero, new Vector3(0.09f, 0.004f, 0.09f), Plain(new Color(0.95f, 0.95f, 0.95f)));
                Piece(g, PrimitiveType.Cylinder, new Vector3(0.012f, 0.1f, 0), new Vector3(0, 0, -8f), new Vector3(0.008f, 0.05f, 0.008f), Plain(new Color(0.85f, 0.1f, 0.1f)));
            }
            else if (kind == KKarkit)
            {
                size = new Vector3(0.11f, 0.15f, 0.06f);
                Piece(g, PrimitiveType.Cube, Vector3.zero, Vector3.zero, new Vector3(0.11f, 0.15f, 0.055f), Plain(new Color(0.92f, 0.88f, 0.8f)));
                Piece(g, PrimitiveType.Quad, new Vector3(0, 0, -0.0285f), Vector3.zero, new Vector3(0.105f, 0.145f, 1), TexMat("kino-karkit.png"));
                Piece(g, PrimitiveType.Quad, new Vector3(0, 0, 0.0285f), new Vector3(0, 180, 0), new Vector3(0.105f, 0.145f, 1), TexMat("kino-karkit.png"));
            }
            else
            {
                // bobine dans sa boite ronde (couvercle etiquete)
                size = new Vector3(0.36f, 0.045f, 0.36f);
                Piece(g, PrimitiveType.Cylinder, Vector3.zero, Vector3.zero, new Vector3(0.36f, 0.0225f, 0.36f), greyMetal);
                Piece(g, PrimitiveType.Quad, new Vector3(0, 0.0228f, 0), new Vector3(90, 0, 0), new Vector3(0.34f, 0.34f, 1), TexMat("kino-kela.png"));
            }
            return true;
        }

        // ---------------------------------------------------------------- manger / boire (objet en main, F)
        static Esine heldFood;
        static bool foodHint;

        static void TickFood()
        {
            TickEmptied();
            Esine f = null;
            foreach (Esine e in esineet) if (IsFood(e.Kind) && e.Go != null && Held(e) && Game.UnderPlayer(e.Go.transform) && !emptied.Contains(e)) { f = e; break; }
            if (f == null) { if (foodHint) { foodHint = false; KinoClearGui(); } heldFood = null; return; }
            heldFood = f;
            KinoGui();
            if (looked == null && lookedItem == null && kinoLooked == null)
            {
                guiUse.Value = true;
                guiText.Value = (f.Kind == KLimsa ? Lang.TG("F : BOIRE", "F: DRINK") : Lang.TG("F : MANGER", "F: EAT")) + " (" + Lang.TG(FoodFr(f.Kind), KinoItemName(f.Kind).ToUpperInvariant()) + ")";
                foodHint = true;
            }
            if (!Input.GetKeyDown(KeyCode.F)) return;
            int left;
            if (!int.TryParse(f.Data, out left)) left = Portions(f.Kind);
            Bite(f.Kind);
            left--;
            L("cinema : " + f.Kind + " " + f.Id + " : une bouchee, reste " + left + " (faim " + Game.GlobalFloat("PlayerHunger").ToString("F0") + ", soif " + Game.GlobalFloat("PlayerThirst").ToString("F0") + ")");
            if (left > 0) { SetData(f, left.ToString()); return; }
            // emballage vide : la main du jeu le lache (« Drop part »), puis il disparait (chez tous) -- detruit en main, la
            // main du jeu garderait un objet mort
            SetData(f, "0");
            emptied.Add(f);
            PlayMakerFSM pf = PickUpFsm();
            if (pf != null) Game.SetState(pf, "Drop part");
            KinoClearGui(); foodHint = false;
        }

        static readonly List<Esine> emptied = new List<Esine>();
        static void TickEmptied()
        {
            for (int i = emptied.Count - 1; i >= 0; i--)
            {
                Esine e = emptied[i];
                if (e.Go == null || !esineet.Contains(e)) { emptied.RemoveAt(i); continue; }
                if (Held(e)) continue;
                emptied.RemoveAt(i);
                RemoveItem(e, true);
            }
        }

        static string FoodFr(string k) { return k == KPopcorn ? "POPCORN" : k == KSipsit ? "CHIPS" : k == KLimsa ? "LIMONADE" : "BONBONS"; }
    }
}
