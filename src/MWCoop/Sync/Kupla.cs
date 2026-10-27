﻿using System;
using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Kupla (« bulle » en finnois) : module d'optimisation de MWCoop, decide par JD le 27/10 et ecrit de zero (jamais un code
    // d'un autre mod d'optimisation : regle de la salle blanche, spec MWCoop-spec-kupla-modules.md). Beta « petites configs »
    // (JD, 27/10 : « on peut le proposer quand meme, comme ca les petites configs testent et me donnent leurs retours »), eteinte
    // par defaut ; sur le PC de JD aucun de ces reglages ne change le temps d'image (mesures du 27/10, run/kupla/conception-p2.md).
    //  - [Coop] KuplaRendu=0/1 : rendu local, chacun pour soi, lu UNE fois au lancement du jeu. A 0 (defaut) : ce module n'est ni
    //    dans la boucle des modules (Core.Active) ni appele au chargement du niveau -- aucun code de Kupla ne tourne, le jeu reste
    //    exactement celui de MWCoop sans Kupla. Les reglages [Graphismes] (Gfx.cs, Lights.cs) restent les maitres : Kupla n'ajoute
    //    qu'une reduction en plus, jamais une valeur contraire (farClipPlane, qualite, ombres : intacts).
    //  - [Coop] KuplaNiveau=1/2/3 (leger / moyen / fort, defaut 1), lu au chargement du niveau :
    //     . retroviseurs : les cameras des retros (RT, *MirrorCam, les memes dans MWC et MSC) rendent 1 image sur 2 / 3 / 4, par le
    //       rappel Camera.onPreCull : sur une image sautee, rien n'est dessine ni efface (masque 0, pas d'effacement), la texture
    //       garde l'image d'avant. Le jeu reste le seul a allumer ou eteindre ces cameras (Retroviseurs=0 : rien ne se passe) ;
    //       elles suivent la voiture ou le joueur est assis (conducteur ou passager, copies comprises).
    //     . decor lointain : distance de coupe par calque sur la camera du joueur, 3000 / 2000 / 1200 m, sauf le ciel et les nuages
    //       (calque Cloud), l'eau, l'interface et le joueur ; jamais au-dela de la distance d'affichage du jeu (option Distance).
    //     . petits objets du decor : les maillages statiques du monde (sans automate sur eux, sans corps rigide au-dessus, hors
    //       voitures, joueurs, objets, trafic, PNJ, boulots, MWCoop*) sont caches au-dela de 400 / 250 / 150 fois leur taille (au
    //       moins 80 m) : un seau a 1 m de large disparait a 400 m, une maison de 10 m a 4000 m. Renderer.enabled seulement, remis
    //       en approchant ; un objet que quelqu'un d'autre a cache ou remontre sort de la liste.
    //  - [Coop] KuplaCompteur=0/1 : compteur d'images par seconde en haut a droite (« 142 i/s · Kupla éteint / niveau 2 ») et une
    //    ligne au journal toutes les 60 s, pour comparer eteint / allume. Module a part (« compteur d'images »), marche Kupla eteint.
    //  - [Coop] KuplaBulles=0/1 : bulles co-op (P2), option du Salon decidee par l'HOTE et imposee aux invites (message @kupla) :
    //    lue et transmise seulement, sans effet -- P2 abandonnee le 27/10 (rien a couper chez l'hote : conception-p2.md).
    public static class Kupla
    {
        static int on = -1, counter = -1;
        public static bool On { get { if (on < 0) on = Config.GetInt("Coop", "KuplaRendu", 0) != 0 ? 1 : 0; return on == 1; } }
        public static bool Counter { get { if (counter < 0) counter = Config.GetInt("Coop", "KuplaCompteur", 0) != 0 ? 1 : 0; return counter == 1; } }
        public static int Level { get { return level; } }
        static int level = 1;
        static readonly int[] kMirrorEvery = { 2, 3, 4 };
        static readonly float[] kFar = { 3000f, 2000f, 1200f };
        static readonly float[] kSizeK = { 400f, 250f, 150f };

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
        static float nextScan = -1, nextDecor = -1;
        static int skipped, rendered;

        // --- decor lointain (calques) et petits objets
        static Camera cullCam;
        static float farCap;
        class Decor { public Renderer R; public Vector3 C; public float Cut; public bool Hidden; }
        static readonly List<Decor> decor = new List<Decor>();
        static readonly HashSet<Renderer> known = new HashSet<Renderer>();
        static int decorAt, hidden;
        static Renderer[] pending; static int pendingAt, pendingAdded;   // (releve etale sur plusieurs images : 32 ms d'un coup sinon)
        static float logAt = -1;

        public static void OnLevelLoaded()
        {
            if (!On) return;
            mirrors.Clear(); decor.Clear(); known.Clear(); cullCam = null; hidden = 0; decorAt = 0; skipped = rendered = 0; pending = null;
            level = Mathf.Clamp(Config.GetInt("Coop", "KuplaNiveau", 1), 1, 3);
            every = kMirrorEvery[level - 1]; farCap = kFar[level - 1];
            if (!PlayerSync.InGame) { nextScan = -1; nextDecor = -1; logAt = -1; return; }
            nextScan = Time.realtimeSinceStartup + 12f;    // (les cameras du jeu sont en place ; Lights fait de meme)
            nextDecor = Time.realtimeSinceStartup + 20f;   // (apres les releves du mod a 15-20 s : rien de cache pendant qu'ils cherchent)
            logAt = nextDecor + 2f;
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
            if (now >= nextScan) { nextScan = now + 10f; ScanMirrors(); CullLayers(); }
            if (now >= nextDecor) { nextDecor = now + 300f; pending = UnityEngine.Object.FindObjectsOfType<Renderer>(); pendingAt = 0; pendingAdded = 0; }
            if (pending != null) ScanDecor();
            StepDecor();
            if (logAt > 0 && now >= logAt) { logAt = -1; Log.Info("kupla : " + Describe()); }
        }

        static void ScanMirrors()
        {
            // cameras des retroviseurs : rendu dans une texture, nom *MirrorCam (Systems/Mirrors/Disable/..., MWC et MSC)
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

        // Distance de coupe par calque de la camera du joueur : le decor s'arrete a farCap, pas le ciel. Reposee si la camera
        // change ou si le jeu a remis ses valeurs ; 0 = la distance d'affichage du jeu, gardee si elle est plus courte.
        static void CullLayers()
        {
            Camera cam = Game.MainCam;
            if (cam == null) return;
            float far = cam.farClipPlane;
            float want = farCap < far ? farCap : 0f;
            float[] d = cam.layerCullDistances;
            bool same = cam == cullCam;
            for (int i = 0; i < 32 && same; i++) same = Mathf.Approximately(d[i], Keep(i) ? 0f : want);
            if (same) return;
            cullCam = cam;
            for (int i = 0; i < 32; i++) d[i] = Keep(i) ? 0f : want;
            cam.layerCullDistances = d;
        }
        static bool Keep(int layer)
        {
            string n = LayerMask.LayerToName(layer);
            return n == "Cloud" || n == "Water" || n == "UI" || n == "GUI" || n == "Player" || n == "TransparentFX";
        }

        // Petits objets du decor : releve des maillages statiques (toutes les 5 min : le monde en cree peu), 300 par image, taille
        // et centre gardes.
        static void ScanDecor()
        {
            int end = Mathf.Min(pending.Length, pendingAt + 300);
            for (; pendingAt < end; pendingAt++)
            {
                Renderer r = pending[pendingAt];
                if (r == null || known.Contains(r) || !IsDecor(r)) continue;
                known.Add(r);
                Bounds b = r.bounds;
                float size = b.extents.magnitude;
                if (size <= 0.01f || size > 60f) continue;   // (routes, terrain, grands batiments : la coupe par calque s'en charge)
                decor.Add(new Decor { R = r, C = b.center, Cut = Mathf.Max(80f, size * kSizeK[level - 1]) });
                pendingAdded++;
            }
            if (pendingAt < pending.Length) return;
            if (pendingAdded > 0 && decor.Count == pendingAdded) Log.Info("kupla : " + decor.Count + " petits objets du decor suivis (niveau " + level + " : caches au-dela de " + kSizeK[level - 1] + " x leur taille, 80 m au moins)");
            pending = null;
        }

        static bool IsDecor(Renderer r)
        {
            if (!(r is MeshRenderer) || !r.enabled) return false;
            Transform t = r.transform, root = t.root;
            string rn = root.name;
            if (rn == "PLAYER" || rn == "GUI" || rn.StartsWith("MWCoop") || rn == "TRAFFIC" || rn == "NPC_CARS" || rn == "HUMANS" || rn == "Spawner"
                || rn == "CARPARTS" || rn == "ITEMS" || rn == "EQUIPMENTS" || rn == "Systems" || rn == "Sheets" || rn == "COMPUTER" || rn == "RACES"
                || rn == "JOBS" || rn == "Skidmarks" || rn == "RALLY" || rn == "TRAIN") return false;
            if (root.GetComponent("CarDynamics") != null || VehicleSync.CarRoot(t) != null) return false;
            string n = t.name;
            if (n.Contains("(itemx)") || n.Contains("(Clone)") || n.Contains("(item") || n.StartsWith("MWCoop")) return false;
            if (t.GetComponent<PlayMakerFSM>() != null || t.GetComponent<Light>() != null) return false;   // (clignotants, enseignes : leur automate montre et cache)
            for (Transform p = t; p != null; p = p.parent) if (p.GetComponent<Rigidbody>() != null) return false;
            return true;
        }

        // Un huitieme de la liste par image : distance au joueur, cache au-dela de Cut + 20 m, remontre en deca de Cut.
        static void StepDecor()
        {
            if (decor.Count == 0) return;
            Camera cam = Game.MainCam;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            int n = Mathf.Max(1, decor.Count / 8);
            for (int k = 0; k < n; k++)
            {
                if (decorAt >= decor.Count) decorAt = 0;
                Decor d = decor[decorAt++];
                Renderer r = d.R;
                if (r == null) { Drop(d); continue; }
                bool en = r.enabled;
                if (en == d.Hidden) { Drop(d); continue; }   // (quelqu'un d'autre l'a cache ou remontre : plus a nous)
                float dist = (d.C - eye).magnitude;
                if (!d.Hidden && dist > d.Cut + 20f) { r.enabled = false; d.Hidden = true; hidden++; }
                else if (d.Hidden && dist < d.Cut) { r.enabled = true; d.Hidden = false; hidden--; }
            }
        }
        static void Drop(Decor d)
        {
            if (d.Hidden) { hidden--; if (d.R != null) d.R.enabled = true; }
            decor.Remove(d);
            if (decorAt > 0) decorAt--;
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

        // --- compteur d'images (module « compteur d'images », KuplaCompteur=1, Kupla eteint ou non)
        static int cFrames, cFramesMin; static float cSince, cShown, cSinceMin, cLogAt = -1;
        public static void CounterUpdate()
        {
            float now = Time.realtimeSinceStartup;
            cFrames++; cFramesMin++;
            if (cSince == 0) { cSince = now; cSinceMin = now; cLogAt = now + 60f; }
            if (now - cSince >= 0.5f) { cShown = cFrames / (now - cSince); cFrames = 0; cSince = now; }
            if (now >= cLogAt && PlayerSync.InGame)
            {
                Log.Info("images : " + (cFramesMin / (now - cSinceMin)).ToString("F0") + " images/s (moyenne 60 s), " + State());
                cFramesMin = 0; cSinceMin = now; cLogAt = now + 60f;
            }
        }
        public static void DrawCounter()
        {
            if (!PlayerSync.InGame) return;
            string s = cShown.ToString("F0") + " " + Lang.T("i/s", "fps") + " · Kupla " + (On ? Lang.T("niveau ", "level ") + level : Lang.T("éteint", "off"));
            Style.Text(new Rect(Screen.width - 330, 8, 320, 24), s, 15, TextAnchor.UpperRight, new Color(1f, 1f, 1f, 0.85f));
        }
        static string State() { return On ? "kupla niveau " + level + (hidden > 0 ? ", " + hidden + " objets caches" : "") : "kupla eteint"; }

        // Journal et essais (Autotest=kupla, kuplamesure).
        public static string Describe()
        {
            if (!On) return "eteint (KuplaRendu=0) : module absent de la boucle, aucun rappel camera, rien de change";
            var sb = new System.Text.StringBuilder("niveau " + level + " : retroviseurs 1 image sur " + every + " (" + mirrors.Count + " cameras");
            foreach (Mirror m in mirrors) if (m.Cam != null) sb.Append(' ').Append(m.Cam.name).Append(m.Cam.enabled && m.Cam.gameObject.activeInHierarchy ? "+" : "-");
            sb.Append(", ").Append(rendered).Append(" rendues / ").Append(skipped).Append(" sautees)");
            sb.Append(", decor coupe a ").Append(farCap.ToString("F0")).Append(" m").Append(cullCam != null ? "" : " (pas encore)");
            sb.Append(", petits objets : ").Append(hidden).Append(" caches / ").Append(decor.Count).Append(" suivis");
            sb.Append(", bulles ").Append(Bubbles ? "allumees" : "eteintes").Append(" (sans effet)");
            return sb.ToString();
        }
    }
}
