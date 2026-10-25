using System.Collections.Generic;
using UnityEngine;

namespace Voyage
{
    // Les lieux : agence de voyage (Perajarvi), aerodrome (champ pres du cinema) ; a destination, un guichet du retour pose
    // pres du point d'arrivee du circuit charge (Destinations).
    static partial class Lieux
    {
        public enum Kind { Counter, Airfield, Return }
        public class Spot { public Kind Kind; public Collider Col; public Destination Dest; public float Reach = Jeu.Reach; }
        public static readonly List<Spot> Spots = new List<Spot>();

        public static GameObject Agence, Aerodrome, TerminalGo, ReturnGo;

        // le point est-il dans le batiment (repere de sa racine, demi-largeur, demi-profondeur, sous 4 m) ?
        public static bool Inside(GameObject b, Vector3 world, float hw, float hd)
        {
            if (b == null) return false;
            Vector3 l = b.transform.InverseTransformPoint(world);
            return Mathf.Abs(l.x) < hw && Mathf.Abs(l.z) < hd && l.y > -0.5f && l.y < 4f;
        }

        public static Vector3 AirfieldArrival, ReturnArrival;
        public static float AirfieldArrivalYaw, ReturnArrivalYaw;

        public static void Build()
        {
            Spots.Clear();
            // (chaque lieu a part : une erreur dans l'un ne prive pas des autres)
            try { Agency(); } catch (System.Exception e) { Journal.Info("agence : " + e); }
            try { if (Jeu.Msc) AirfieldMsc(); else Airfield(); } catch (System.Exception e) { Journal.Info("aerodrome : " + e); }
            // l'aerodrome sur la carte M et dans /lieu de MWCoop (devant le guichet ; demande de [Directives], 25/10)
            if (Aerodrome != null && AirfieldArrival != Vector3.zero)
                Coop.MapPlace("aerodrome", "airfield", "Aérodrome (Voyage)", "Airfield (Voyage)", AirfieldArrival + Vector3.up, AirfieldArrival + Quaternion.Euler(0f, AirfieldArrivalYaw, 0f) * Vector3.forward * 5f);
        }

        static Spot AddSpot(Kind k, Transform parent, Vector3 local, Vector3 size, Destination d = null, float reach = Jeu.Reach)
        {
            var g = new GameObject("voyage_visee_" + k);
            g.layer = 2;   // (Ignore Raycast : ne gene pas la main du jeu)
            g.transform.SetParent(parent, false);
            g.transform.localPosition = local;
            var bc = g.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = size;
            var s = new Spot { Kind = k, Col = bc, Dest = d, Reach = reach };
            Spots.Add(s);
            return s;
        }

        // PNJ debout (corps de MWCoop, sinon une silhouette simple) et sa collision ; regarde +z local
        static void Npc(Construction c, string name, Vector3 local, float yaw)
        {
            GameObject n = Coop.Npc(name);
            if (n != null)
            {
                n.transform.SetParent(c.Root.transform, false);
                n.transform.localPosition = local;
                n.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
            else
            {
                c.Cylinder("pnj_corps", local, 0.22f, 0.2f, 1.45f, 10);
                c.Cylinder("pnj_tete", local + Vector3.up * 1.45f, 0.11f, 0.11f, 0.26f, 10);
                c.Quad("pnj_tete", local + new Vector3(-0.11f, 1.71f, -0.11f), local + new Vector3(0.11f, 1.71f, -0.11f), local + new Vector3(0.11f, 1.71f, 0.11f), local + new Vector3(-0.11f, 1.71f, 0.11f));
            }
            c.Collider(local + new Vector3(-0.28f, 0f, -0.28f), local + new Vector3(0.28f, 1.8f, 0.28f));
        }

        // ---------------------------------------------------------------- guichet du retour (destination chargee)
        // Petit abri vitre a cote du point d'arrivee, sur le sol du circuit ; le voyageur arrive dos au guichet.
        public static void ReturnDesk(Destination d)
        {
            DropReturnDesk();
            Quaternion r = Quaternion.Euler(0f, d.Yaw, 0f);
            Vector3 at = d.ArrivalWorld + r * new Vector3(-6f, 0f, -2f);
            float lo, hi;
            Footprint(at, d.Yaw, 3.3f, 2.3f, out lo, out hi);
            var k = new Construction("VOYAGE_RETOUR", new Vector3(at.x, hi + 0.15f, at.z), d.Yaw, d.Root.transform);
            ReturnGo = k.Root;
            CommonMats(k);
            k.Color("kyltti", new Color(0.12f, 0.27f, 0.55f));
            k.Box(new Vector3(-3.2f, lo - hi - 0.4f, -2.2f), new Vector3(3.2f, -0.05f, 2.2f), "kivi");
            k.Box(new Vector3(-3.2f, -0.05f, -2.2f), new Vector3(3.2f, 0f, 2.2f), "lattia");
            k.Box(new Vector3(-3.2f, 0f, -2.2f), new Vector3(3.2f, 2.8f, -2.0f), "lauta");
            k.Box(new Vector3(-3.2f, 0f, -2.0f), new Vector3(-3.0f, 2.8f, 2.2f), "metalli");
            k.Box(new Vector3(3.0f, 0f, -2.0f), new Vector3(3.2f, 2.8f, 2.2f), "metalli");
            k.Box(new Vector3(-3.4f, 2.8f, -2.4f), new Vector3(3.4f, 2.95f, 2.6f), "metalli", "metalli", "lista", "lista", "metalli", "metalli");
            k.Box(new Vector3(-1.6f, 0f, -0.9f), new Vector3(1.6f, 1.05f, -0.4f), "tiski");
            k.Box(new Vector3(-3.0f, 2.25f, 2.2f), new Vector3(3.0f, 2.75f, 2.26f), "kyltti", false);
            k.Text(Jeu.T("VOYAGE · RETOUR EN FINLANDE", "VOYAGE · BACK TO FINLAND", "VOYAGE · ОБРАТНО В ФИНЛЯНДИЮ"), new Vector3(0f, 2.5f, 2.27f), 180f, 0.1f, Color.white);
            string credit = d.Author.Length > 0 ? Jeu.T(d.Name + " : circuit de " + d.Author + " (converti depuis ton Assetto Corsa)", d.Name + ": track by " + d.Author + " (converted from your Assetto Corsa)",
                                                         d.Name + ": трасса " + d.Author + " (сконвертирована из твоей Assetto Corsa)")
                                                : Jeu.T(d.Name + " : converti depuis ton Assetto Corsa", d.Name + ": converted from your Assetto Corsa", d.Name + ": сконвертирована из твоей Assetto Corsa");
            k.Text(credit, new Vector3(0f, 1.6f, -1.97f), 180f, 0.035f, new Color(0.9f, 0.9f, 0.85f));
            Npc(k, "VOYAGE-retour", new Vector3(0f, 0f, -1.5f), 0f);
            AddSpot(Kind.Return, k.Root.transform, new Vector3(0f, 1.0f, -1.0f), new Vector3(1.6f, 1.9f, 1.4f), d);
            k.Lamp(new Vector3(0f, 2.6f, 0f), new Color(1f, 0.85f, 0.65f), 1.1f, 6f);
            k.Flush();
            ReturnArrival = k.Root.transform.TransformPoint(new Vector3(0f, 0.05f, 4f));
            ReturnArrivalYaw = d.Yaw;
        }

        public static void DropReturnDesk()
        {
            Spots.RemoveAll(s => s.Kind == Kind.Return);
            if (ReturnGo != null) Object.Destroy(ReturnGo);
            ReturnGo = null;
        }
    }
}
