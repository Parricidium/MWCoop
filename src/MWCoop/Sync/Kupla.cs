using System;
using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Kupla (« bulle » en finnois) : module d'optimisation de MWCoop, decide par JD le 27/10 et ecrit de zero (jamais un code
    // d'un autre mod d'optimisation : regle de la salle blanche, spec MWCoop-spec-kupla-modules.md). Beta, eteint par defaut.
    //  - [Coop] KuplaRendu=0/1 : rendu local, chacun pour soi, lu UNE fois au lancement du jeu. A 0 (defaut) : ce module n'est ni
    //    dans la boucle des modules (Core.Active) ni appele au chargement du niveau -- aucun code de Kupla ne tourne, le jeu reste
    //    exactement celui de MWCoop sans Kupla. Les reglages [Graphismes] (Gfx.cs, Lights.cs) restent les maitres : Kupla n'ajoute
    //    qu'un filtre en plus, jamais une valeur contraire.
    //    P1 (27/10) :
    //     . retroviseurs : les cameras des retros (RT, *MirrorCam) rendent 1 image sur N ([Coop] KuplaRetros, 2 par defaut), par
    //       le rappel Camera.onPreCull : sur une image sautee, rien n'est dessine ni efface (masque 0, pas d'effacement), la texture
    //       garde l'image d'avant. Le jeu reste le seul a allumer ou eteindre ces cameras (Retroviseurs=0 : rien ne se passe) ;
    //       elles suivent la voiture ou le joueur est assis (conducteur ou passager, copies comprises : ce sont les memes cameras).
    //     (mesure du 27/10, Carte-MWC a pied : une coupe par calque -- boulons, pieces, vitres -- ne change rien, presque tout le
    //      monde est sur le calque Default ; abandonnee. Les deux jeux n'ont pas de Terrain Unity : rien a regler de ce cote.)
    //  - [Coop] KuplaBulles=0/1 : bulles co-op (P2), option du Salon decidee par l'HOTE et imposee aux invites (message @kupla,
    //    comme @triches). En P1 : lue et transmise seulement, sans effet.
    public static class Kupla
    {
        static int on = -1;
        public static bool On { get { if (on < 0) on = Config.GetInt("Coop", "KuplaRendu", 0) != 0 ? 1 : 0; return on == 1; } }

        // --- bulles (P2) : valeur de l'hote
        static bool hostBubbles, hostBubblesKnown;
        static float nextAnnounce;
        public static bool Bubbles { get { return Session.Active && !Session.IsHost ? hostBubbles : Config.GetInt("Coop", "KuplaBulles", 0) != 0; } }
        public static void OnBubbles(bool b)
        {
            if (!hostBubblesKnown || b != hostBubbles) Log.Info("kupla : bulles co-op " + (b ? "allumees" : "eteintes") + " par l'hote");
            hostBubbles = b; hostBubblesKnown = true;
        }

        // --- retroviseurs
        class Mirror { public Camera Cam; public int Slot, Mask; public CameraClearFlags Clear; public bool Skipped; }
        static readonly List<Mirror> mirrors = new List<Mirror>();
        static int every = 2;
        static bool hooked;
        static float nextScan = -1;
        static int skipped, rendered;

        static float logAt = -1;

        public static void OnLevelLoaded()
        {
            if (!On) return;
            mirrors.Clear(); skipped = rendered = 0;
            every = Mathf.Clamp(Config.GetInt("Coop", "KuplaRetros", 2), 1, 8);
            if (!PlayerSync.InGame) { nextScan = -1; logAt = -1; return; }
            nextScan = Time.realtimeSinceStartup + 12f;   // (les terrains et les cameras du jeu sont en place ; Lights fait de meme)
            logAt = nextScan + 1f;
            if (!hooked) { hooked = true; Camera.onPreCull += PreCull; Camera.onPostRender += PostRender; }
        }

        public static void Update()
        {
            if (Session.Active && Session.IsHost && Time.realtimeSinceStartup >= nextAnnounce)
            {
                nextAnnounce = Time.realtimeSinceStartup + 10f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kupla").Bool(Bubbles), true);
            }
            if (nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 10f; Scan(); }
            if (logAt > 0 && now >= logAt) { logAt = -1; Log.Info("kupla : " + Describe()); }
        }

        static void Scan()
        {
            // cameras des retroviseurs : rendu dans une texture, nom *MirrorCam (MWC : Systems/Mirrors/Disable/...)
            var seen = new HashSet<Camera>();
            foreach (Mirror m in mirrors) if (m.Cam != null) seen.Add(m.Cam);
            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (c == null || c.hideFlags != HideFlags.None || seen.Contains(c) || c.targetTexture == null) continue;
                if (c.name.IndexOf("mirror", StringComparison.OrdinalIgnoreCase) < 0) continue;
                mirrors.Add(new Mirror { Cam = c, Slot = mirrors.Count });
            }
            mirrors.RemoveAll(m => m.Cam == null);
        }

        static void PreCull(Camera c)
        {
            if (every < 2 || c.targetTexture == null) return;
            for (int i = 0; i < mirrors.Count; i++)
            {
                Mirror m = mirrors[i];
                if (m.Cam != c) continue;
                if ((Time.frameCount + m.Slot) % every == 0) { rendered++; return; }
                m.Mask = c.cullingMask; m.Clear = c.clearFlags; m.Skipped = true;
                c.cullingMask = 0; c.clearFlags = CameraClearFlags.Nothing;
                skipped++;
                return;
            }
        }

        static void PostRender(Camera c)
        {
            for (int i = 0; i < mirrors.Count; i++)
            {
                Mirror m = mirrors[i];
                if (m.Cam != c || !m.Skipped) continue;
                m.Skipped = false; c.cullingMask = m.Mask; c.clearFlags = m.Clear;
                return;
            }
        }

        // Journal et essais (Autotest=kupla).
        public static string Describe()
        {
            if (!On) return "eteint (KuplaRendu=0) : module absent de la boucle, aucun rappel camera, rien de change";
            var sb = new System.Text.StringBuilder("rendu : retroviseurs 1 image sur " + every + " (" + mirrors.Count + " cameras");
            foreach (Mirror m in mirrors) if (m.Cam != null) sb.Append(' ').Append(m.Cam.name).Append(m.Cam.enabled && m.Cam.gameObject.activeInHierarchy ? "+" : "-");
            sb.Append(", ").Append(rendered).Append(" rendues / ").Append(skipped).Append(" sautees)");
            sb.Append(", bulles ").Append(Bubbles ? "allumees" : "eteintes").Append(" (P2 : sans effet)");
            return sb.ToString();
        }
    }
}
