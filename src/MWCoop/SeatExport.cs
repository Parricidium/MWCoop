using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Export pour l'outil des places de JD (25/10, idee de Menor : placer soi-meme les joueurs assis dans chaque voiture et
    // regler leur posture os par os ; outil prive, outils-prives/places). ExportPlaces=<dossier> -- ligne de lancement du lanceur
    // ([Lancement] de lancement.ini, profil places-export, jeu hors de l'ecran : personnes choisies par JD, cle d'acces 26/10), ou
    // [Test] ExportPlaces : une fois GAME pose (ExportPlacesDelai, 25 s), le mod ecrit dans <dossier> :
    //  - avatar.json + avatar.bin : le corps des avatars (copie d'un marcheur du jeu, squelette « fat ») : maillage avec ses
    //    poids, os (nom, parent, pose de liaison), poses (assis = worker1_sitdown tenu, volant voiture / camion = conducteurs
    //    de la circulation), texture ;
    //  - voiture-<i>.json + voiture-<i>.bin : chaque voiture du jeu (une par zone de conducteur connue de Seats), puis la VAZ
    //    et chaque addon installe (sortis un a un du F10, rendus apres) : maillages dans le repere de la voiture, matieres,
    //    pivot de tete du conducteur, zone du conducteur, places calculees aujourd'hui par Seats (valeurs de depart) ;
    //  - voitures.json (la liste), textures/<i>.png, etat.txt (avancement). Puis ferme le jeu.
    // Rien du jeu ne quitte le PC : le dossier est a JD. Formats des .bin comme l'export de la carte (MapExport) : par
    // maillage, sommets n, drapeaux (1 normales, 2 uv, 4 os), sous-maillages k, k tailles d'indices, positions, normales, uv,
    // [os : 4 indices + 4 poids par sommet], indices (u32). Repere d'Unity (main gauche) : l'outil retourne z.
    public static class SeatExport
    {
        static string dir;
        static bool? on;
        public static bool Active
        {
            get
            {
                if (on == null)
                {
                    dir = Config.Get("Coop", "ExportPlaces", Config.Get("Test", "ExportPlaces", ""));   // (lancement.ini passe devant [Coop])
                    on = dir.Length > 0;
                    if (on.Value) Log.Info("export des places demande : " + dir);
                }
                return on.Value;
            }
        }

        static IEnumerator<bool> job;
        static float startAt = -1, quitAt = -1, waitUntil;

        public static void Update()
        {
            if (!Active) return;
            AudioListener.volume = 0f;
            if (quitAt > 0) { if (Time.realtimeSinceStartup >= quitAt) System.Diagnostics.Process.GetCurrentProcess().Kill(); return; }
            if (Game.LevelName != "GAME") return;
            if (startAt < 0) startAt = Time.realtimeSinceStartup + Config.GetFloat("Test", "ExportPlacesDelai", 25f);
            if (job == null && Time.realtimeSinceStartup >= startAt) job = Run();
            if (job == null || Time.realtimeSinceStartup < waitUntil) return;
            try { if (!job.MoveNext()) { job = null; Finish(null); } }
            catch (Exception e) { job = null; Log.Error("export des places : " + e); Finish(e.Message); }
        }

        static void Finish(string error)
        {
            State(error == null ? "fini" : "erreur", error ?? "Places exportees", true);
            Log.Info("export des places : " + (error ?? "fini") + ", fermeture du jeu");
            quitAt = Time.realtimeSinceStartup + 1f;
        }

        static void State(string stage, string text, bool done)
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "etat.txt"), "etape=" + stage + "\ntexte=" + text + "\nfini=" + (done ? 1 : 0) + "\n", Encoding.UTF8);
            }
            catch (Exception e) { Log.Warn("export des places : etat.txt (" + e.Message + ")"); }
        }

        static void Wait(float s) { waitUntil = Time.realtimeSinceStartup + s; }

        // ---------------------------------------------------------------- deroulement
        class CarEntry { public string Key, Name, Kind, File; }
        static readonly List<CarEntry> cars = new List<CarEntry>();
        static readonly Dictionary<Texture, int> texIndex = new Dictionary<Texture, int>();

        static IEnumerator<bool> Run()
        {
            Directory.CreateDirectory(Path.Combine(dir, "textures"));
            State("avatar", "Corps et squelette des avatars", false);
            yield return true;
            try { ExportAvatar(); } catch (Exception e) { Log.Warn("export des places : avatar : " + e); }
            yield return true;

            // voitures du jeu : celles qui ont une zone de conducteur (Seats)
            State("voitures", "Voitures du jeu", false);
            Rescan();
            foreach (Transform car in GameCars())
            {
                try { ExportCar(car, car.name, car.name, "jeu"); } catch (Exception e) { Log.Warn("export des places : " + car.name + " : " + e.Message); }
                yield return true;
            }

            // VAZ et addons installes : sortis un a un (comme le F10), exportes, rendus
            var models = new List<int> { VazCar.ModelVaz };
            for (int i = 1; i < VazCar.Models.Length; i++) if (VazCar.Has(i)) models.Add(i);
            foreach (int id in models)
            {
                VazCar.Model m = id == VazCar.ModelVaz ? VazCar.Vaz2108 : VazCar.Models[id];
                State("addons", "Voiture " + m.Name, false);
                if (!Vaz.Request(1, id)) { Log.Warn("export des places : " + m.Name + " refusee"); continue; }
                float until = Time.realtimeSinceStartup + 20f;
                while (Vaz.CarOf(Session.Active ? Session.LocalId : 0) == null && Time.realtimeSinceStartup < until) { Wait(0.5f); yield return true; }
                Transform car = Vaz.CarOf(Session.Active ? Session.LocalId : 0);
                if (car == null) { Log.Warn("export des places : " + m.Name + " pas construite"); continue; }
                Wait(6f); yield return true;
                Rescan();   // (places de la copie)
                try { ExportCar(car, "modele-" + m.Folder, m.Name, "addon"); } catch (Exception e) { Log.Warn("export des places : " + m.Name + " : " + e.Message); }
                Vaz.Release();
                until = Time.realtimeSinceStartup + 10f;
                while (Vaz.CarOf(Session.Active ? Session.LocalId : 0) != null && Time.realtimeSinceStartup < until) { Wait(0.5f); yield return true; }
            }

            var sb = new StringBuilder("{\"jeu\":" + S(Game.Msc ? "msc" : "mwc") + ",\"mod\":" + S(Version.Text) + ",\"avatar\":\"avatar.json\",\"voitures\":[");
            for (int i = 0; i < cars.Count; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"cle\":").Append(S(cars[i].Key)).Append(",\"nom\":").Append(S(cars[i].Name)).Append(",\"sorte\":").Append(S(cars[i].Kind)).Append(",\"fichier\":").Append(S(cars[i].File)).Append('}');
            sb.Append("]}");
            File.WriteAllText(Path.Combine(dir, "voitures.json"), sb.ToString(), Encoding.UTF8);
            Log.Info("export des places : " + cars.Count + " voitures, " + texIndex.Count + " textures");
        }

        // Voitures du jeu : racines des zones de conducteur connues de Seats (champ prive driverZones), hors nos copies.
        static List<Transform> GameCars()
        {
            var l = new List<Transform>();
            FieldInfo f = typeof(Seats).GetField("driverZones", BindingFlags.NonPublic | BindingFlags.Static);
            var zones = f != null ? f.GetValue(null) as IEnumerable : null;
            if (zones != null)
                foreach (object kv in zones)
                {
                    PropertyInfo vp = kv.GetType().GetProperty("Value");
                    Transform t = vp != null ? vp.GetValue(kv, null) as Transform : null;
                    if (t != null && !l.Contains(t) && !t.name.StartsWith(Vaz.Prefix)) l.Add(t);
                }
            // voitures de VehicleSync (celles de Seats) qui ont une zone de conducteur (automate PlayerTrigger sur DriveTrigger*)
            int fromSync = 0;
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody rb = VehicleSync.LocalBody(ci);
                if (rb == null || l.Contains(rb.transform) || rb.name.StartsWith(Vaz.Prefix) || rb.name.StartsWith("MWCoop")) continue;
                foreach (PlayMakerFSM pf in rb.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (pf.FsmName == "PlayerTrigger" && pf.gameObject.name.StartsWith("DriveTrigger")) { l.Add(rb.transform); fromSync++; break; }
            }
            // (et les DriveTrigger* de la scene, voiture = premier parent avec un Rigidbody)
            int fromScene = 0;
            var roots = Game.RootSet(true);
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(typeof(Transform)))
            {
                var t = (Transform)o;
                if (!t.name.StartsWith("DriveTrigger") || t.gameObject.hideFlags != HideFlags.None || !roots.Contains(t.root)) continue;
                Transform car = null;
                for (Transform p = t.parent; p != null && car == null; p = p.parent) if (p.GetComponent<Rigidbody>() != null) car = p;
                if (car == null || l.Contains(car) || car.name.StartsWith(Vaz.Prefix) || car.name.StartsWith("MWCoop")) continue;
                l.Add(car); fromScene++;
            }
            Log.Info("export des places : " + l.Count + " voitures du jeu (" + fromSync + " de VehicleSync, " + fromScene + " de la scene)");
            return l;
        }

        // ---------------------------------------------------------------- une voiture
        static void ExportCar(Transform car, string key, string name, string kind)
        {
            int idx = cars.Count;
            string file = "voiture-" + idx;
            var mats = new List<Material>(); var matIdx = new Dictionary<Material, int>();
            var objs = new StringBuilder(); var beltObjs = new StringBuilder();
            int nObj = 0, nBelt = 0, unreadable = 0;
            Matrix4x4 toCar = car.worldToLocalMatrix;
            using (var bin = new BinaryWriter(File.Create(Path.Combine(dir, file + ".bin"))))
            {
                // (ceintures a part, meme eteintes -- bouclees, copies du mod : l'outil les montre a la place qu'on regle)
                foreach (MeshRenderer r in car.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Transform belt = BeltRoot(r.transform, car);
                    bool shown = r.enabled && Shown(r.transform, car);
                    if (!shown && belt == null) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    Mesh m = mf != null ? mf.sharedMesh : null;
                    if (m == null) continue;
                    long at = bin.BaseStream.Position;
                    if (!WriteMesh(bin, m, toCar * r.localToWorldMatrix, null)) { unreadable++; continue; }
                    var mi = new List<int>();
                    foreach (Material mt in r.sharedMaterials)
                    {
                        int k = -1;
                        if (mt != null && !matIdx.TryGetValue(mt, out k)) { k = mats.Count; matIdx[mt] = k; mats.Add(mt); }
                        mi.Add(k);
                    }
                    StringBuilder to = belt != null ? beltObjs : objs;
                    to.Append((belt != null ? nBelt++ : nObj++) > 0 ? "," : "").Append("{\"nom\":").Append(S(Recon.Path(r.transform).Substring(Recon.Path(car).Length))).Append(",\"bin\":").Append(at).Append(",\"mats\":[").Append(string.Join(",", mi.ConvertAll(x => x.ToString()).ToArray())).Append("]");
                    if (belt != null) to.Append(",\"vu\":").Append(shown && r.gameObject.activeInHierarchy ? "true" : "false").Append(",\"mod\":").Append(belt.name.StartsWith("MWCoop") ? "true" : "false");
                    to.Append('}');
                }
            }
            var sb = new StringBuilder("{\"cle\":" + S(key) + ",\"nom\":" + S(name) + ",\"sorte\":" + S(kind) + ",\"objets\":[" + objs + "],\"ceintures\":[" + beltObjs + "]");
            string att = BeltAnchors(car);
            if (att != null) sb.Append(",\"attaches\":").Append(att);
            sb.Append(",\"matieres\":[");
            for (int i = 0; i < mats.Count; i++) sb.Append(i > 0 ? "," : "").Append(MatJson(mats[i]));
            sb.Append(']');
            Transform head = FindDeep(car, "DriverHeadPivot");
            if (head != null) sb.Append(",\"tete\":{\"p\":").Append(V(car.InverseTransformPoint(head.position))).Append(",\"q\":").Append(Q(Quaternion.Inverse(car.rotation) * head.rotation)).Append('}');
            sb.Append(Controls(car, head));
            Transform zone = FindDeep(car, "DriveTrigger");
            Collider zc = zone != null ? zone.GetComponent<Collider>() : null;
            if (zc != null) sb.Append(",\"zone\":{\"c\":").Append(V(car.InverseTransformPoint(zc.bounds.center))).Append(",\"s\":").Append(V(zc.bounds.size)).Append('}');
            sb.Append(",\"places\":[").Append(SeatsJson(car)).Append("]}");
            File.WriteAllText(Path.Combine(dir, file + ".json"), sb.ToString(), Encoding.UTF8);
            cars.Add(new CarEntry { Key = key, Name = name, Kind = kind, File = file + ".json" });
            Log.Info("export des places : " + name + " (" + key + ") : " + nObj + " objets, " + nBelt + " de ceintures" + (att != null ? " (attaches)" : "") + ", " + mats.Count + " matieres" + (unreadable > 0 ? ", " + unreadable + " illisibles" : "") + (head != null ? "" : ", PAS de pivot de tete"));
        }

        // Volant et levier du conducteur (Avatar.SteerOf / GearOf, comme en jeu) pour choisir les prises des mains dans l'outil (JD 29/10) :
        // matrice volant -> voiture (lignes), chemin de l'objet (ses maillages tournent avec lui), axe de rotation (repere du volant : celui
        // de l'addon standalone, sinon l'axe du volant le plus proche de la colonne), prises a 10 h 10 du jeu ; levier : matrice, chemin,
        // haut du pommeau (repere du levier).
        static string Controls(Transform car, Transform head)
        {
            var sb = new StringBuilder();
            Vector3 hp = head != null ? head.position : car.TransformPoint(new Vector3(-0.35f, 1.1f, 0f));
            Transform steer = Avatar.SteerOf(car);
            if (steer != null)
            {
                Vector3 gr, gl;
                Avatar.DefaultGrips(car, steer, hp, out gr, out gl);
                Vector3 ax;
                if (steer.name == "VAZ_volant") ax = AddonSteerAxis(car);   // (addon : l'axe de son modele, volant_axe d'infos.txt)
                else
                {
                    Vector3 la = steer.InverseTransformDirection(steer.position - hp);
                    float x = Mathf.Abs(la.x), y = Mathf.Abs(la.y), z = Mathf.Abs(la.z);
                    ax = x >= y && x >= z ? new Vector3(Mathf.Sign(la.x), 0, 0) : y >= z ? new Vector3(0, Mathf.Sign(la.y), 0) : new Vector3(0, 0, Mathf.Sign(la.z));
                }
                sb.Append(",\"volant\":{\"m\":").Append(Mx(car.worldToLocalMatrix * steer.localToWorldMatrix)).Append(",\"chemin\":").Append(S(Recon.Path(steer).Substring(Recon.Path(car).Length)))
                  .Append(",\"axe\":").Append(V(ax)).Append(",\"prises\":{\"droite\":").Append(V(gr)).Append(",\"gauche\":").Append(V(gl)).Append("}}");
            }
            Transform gear = Avatar.GearOf(car);
            if (gear != null)
                sb.Append(",\"levier\":{\"m\":").Append(Mx(car.worldToLocalMatrix * gear.localToWorldMatrix)).Append(",\"chemin\":").Append(S(Recon.Path(gear).Substring(Recon.Path(car).Length)))
                  .Append(",\"pommeau\":").Append(V(gear.InverseTransformPoint(Avatar.DefaultKnob(car, gear)))).Append('}');
            Log.Info("export des places : " + car.name + " volant " + (steer != null ? steer.name : "-") + ", levier " + (gear != null ? gear.name : "-"));
            return sb.ToString();
        }
        // Axe du volant d'un addon (VazCar.Built.SteerAxis : standalone, sinon la copie louee -- champ prive Vaz.rents)
        static Vector3 AddonSteerAxis(Transform car)
        {
            VazCar.Built b = StandaloneCar.Find(car.gameObject);
            if (b == null)
            {
                FieldInfo f = typeof(Vaz).GetField("rents", BindingFlags.NonPublic | BindingFlags.Static);
                var d = f != null ? f.GetValue(null) as IDictionary : null;
                if (d != null)
                    foreach (object r in d.Values)
                    {
                        var rb = Get<VazCar.Built>(r, r.GetType(), "Car");
                        if (rb != null && rb.Car == car.gameObject) { b = rb; break; }
                    }
            }
            return b != null ? b.SteerAxis : Vector3.forward;
        }
        static string Mx(Matrix4x4 m) { var sb = new StringBuilder("["); for (int k = 0; k < 16; k++) sb.Append(k > 0 ? "," : "").Append(F(m[k / 4, k % 4])); return sb.Append(']').ToString(); }

        // allume jusqu'a la voiture, sauf un « LOD » eteint par la distance (voiture garee loin du joueur)
        static bool Shown(Transform t, Transform car)
        {
            for (Transform p = t; p != null && p != car; p = p.parent)
                if (!p.gameObject.activeSelf && p.name != "LOD") return false;
            return true;
        }

        // Voitures et places recalculees de force : hors partie, VehicleSync et Seats ne cherchent rien (Session.Active). Puis les
        // ceintures arriere du mod (modeles de Menos : SORBET, MACHTWAGEN), posees par Seats.UpdateRearBelts comme en partie.
        static void Rescan()
        {
            foreach (Type t in new[] { typeof(VehicleSync), typeof(Seats) })
            {
                MethodInfo m = t.GetMethod("Scan", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                try { if (m != null) m.Invoke(null, null); else Log.Warn("export des places : " + t.Name + ".Scan introuvable"); }
                catch (Exception e) { Log.Warn("export des places : " + t.Name + ".Scan : " + (e.InnerException ?? e).Message); }
            }
            MethodInfo rb = typeof(Seats).GetMethod("UpdateRearBelts", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
            try { if (rb != null) rb.Invoke(null, null); else Log.Warn("export des places : Seats.UpdateRearBelts introuvable"); }
            catch (Exception e) { Log.Warn("export des places : ceintures arriere : " + (e.InnerException ?? e).Message); }
        }

        // Objet de ceinture : sous Seatbelts (du jeu ; « seatbelts » sur la HAYOSIKO de MSC), PassengerBelt, DriverBelt, ou une
        // ceinture du mod (MWCoop-Ceinture...).
        static readonly string[] BeltNames = { "Seatbelts", "PassengerBelt", "DriverBelt", "MWCoop-Ceinture" };
        static Transform BeltRoot(Transform t, Transform car)
        {
            Transform found = null;
            for (Transform p = t; p != null && p != car; p = p.parent)
                foreach (string n in BeltNames)
                    if (p.name.StartsWith(n, StringComparison.OrdinalIgnoreCase)) found = p;
            return found;
        }

        // Attaches calculees par Seats (BeltOf : champ prive belts) -- boucle, haut, plancher de la ceinture du conducteur, boucle cote
        // passager (en miroir), bout de la languette -- repere de la voiture ; null sans ceinture.
        static string BeltAnchors(Transform car)
        {
            MethodInfo of = typeof(Seats).GetMethod("BeltOf", BindingFlags.NonPublic | BindingFlags.Static);
            object b = null;
            try { b = of != null ? of.Invoke(null, new object[] { car }) : null; }
            catch (Exception e) { Log.Warn("export des places : ceinture de " + car.name + " : " + (e.InnerException ?? e).Message); }
            if (b == null) return null;
            Type t = b.GetType();
            return "{\"boucle\":" + V(Get<Vector3>(b, t, "DriverBuckle")) + ",\"bouclePassager\":" + V(Get<Vector3>(b, t, "Buckle")) + ",\"haut\":" + V(Get<Vector3>(b, t, "Top"))
                 + ",\"plancher\":" + V(Get<Vector3>(b, t, "Floor")) + ",\"languette\":" + V(Get<Vector3>(b, t, "MeshBuckle")) + ",\"calcule\":" + (Get<bool>(b, t, "Anchors") ? "true" : "false") + "}";
        }

        // Places calculees aujourd'hui par Seats pour cette voiture (champ prive seats : Index, Head, Yaw, zone, MaxX, Bed = ridelle, Stand = debout).
        static string SeatsJson(Transform car)
        {
            var sb = new StringBuilder();
            FieldInfo f = typeof(Seats).GetField("seats", BindingFlags.NonPublic | BindingFlags.Static);
            var list = f != null ? f.GetValue(null) as IEnumerable : null;
            if (list == null) return "";
            int n = 0;
            foreach (object s in list)
            {
                Type t = s.GetType();
                Transform ct = Get<Transform>(s, t, "CarT");
                if (ct != car) continue;
                sb.Append(n++ > 0 ? "," : "").Append("{\"n\":").Append(Get<int>(s, t, "Index")).Append(",\"tete\":").Append(V(Get<Vector3>(s, t, "Head")))
                  .Append(",\"cap\":").Append(F(Get<float>(s, t, "Yaw"))).Append(",\"zone\":[").Append(F(Get<float>(s, t, "ZoneX"))).Append(',').Append(F(Get<float>(s, t, "ZoneZ")))
                  .Append("],\"maxx\":").Append(F(Get<float>(s, t, "MaxX"))).Append(",\"ridelle\":").Append(Get<bool>(s, t, "Bed") ? "true" : "false")
                  .Append(",\"debout\":").Append(Get<bool>(s, t, "Stand") ? "true" : "false").Append('}');
            }
            return sb.ToString();
        }
        static T Get<T>(object o, Type t, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object v = f != null ? f.GetValue(o) : null;
            return v is T ? (T)v : default(T);
        }

        // ---------------------------------------------------------------- avatar
        static void ExportAvatar()
        {
            Avatar a = Avatar.CreatePreview("MWCoop-ExportPlaces", 0);
            if (a == null) { Log.Warn("export des places : avatar impossible (pas de marcheur ?)"); return; }
            try
            {
                FieldInfo fBody = typeof(Avatar).GetField("body", BindingFlags.NonPublic | BindingFlags.Instance), fAnim = typeof(Avatar).GetField("anim", BindingFlags.NonPublic | BindingFlags.Instance);
                var smr = fBody != null ? fBody.GetValue(a) as SkinnedMeshRenderer : null;
                var anim = fAnim != null ? fAnim.GetValue(a) as Animation : null;
                if (smr == null || smr.sharedMesh == null) { Log.Warn("export des places : avatar sans maillage"); return; }
                Transform[] bones = smr.bones;
                var bi = new Dictionary<Transform, int>();
                for (int i = 0; i < bones.Length; i++) if (bones[i] != null) bi[bones[i]] = i;
                var sb = new StringBuilder("{\"os\":[");
                Matrix4x4[] bp = smr.sharedMesh.bindposes;
                for (int i = 0; i < bones.Length; i++)
                {
                    Transform b = bones[i];
                    int parent = -1;
                    if (b != null && b.parent != null && bi.ContainsKey(b.parent)) parent = bi[b.parent];
                    sb.Append(i > 0 ? "," : "").Append("{\"nom\":").Append(S(b != null ? b.name : "")).Append(",\"parent\":").Append(parent)
                      .Append(",\"p\":").Append(V(b != null ? b.localPosition : Vector3.zero)).Append(",\"q\":").Append(Q(b != null ? b.localRotation : Quaternion.identity))
                      .Append(",\"liaison\":[");
                    for (int k = 0; k < 16; k++) sb.Append(k > 0 ? "," : "").Append(F(bp[i][k / 4, k % 4]));
                    sb.Append("]}");
                }
                sb.Append(']');
                // maillage (repere du maillage, comme les poses de liaison) avec ses poids
                using (var bin = new BinaryWriter(File.Create(Path.Combine(dir, "avatar.bin")))) WriteMesh(bin, smr.sharedMesh, Matrix4x4.identity, smr.sharedMesh.boneWeights);
                sb.Append(",\"maillage\":0,\"matiere\":").Append(MatJson(smr.sharedMaterial));
                // tout l'arbre de Char (os du maillage et noeuds intermediaires : HeadPivot, racine du squelette...), repere de la racine de
                // l'avatar (parent -1) ; le maillage est sous l'un d'eux ("maillage" : son indice)
                var all = new List<Transform>();
                Transform ch = smr.transform;
                while (ch.parent != null && ch.parent != a.Root.transform) ch = ch.parent;
                foreach (Transform t in ch.GetComponentsInChildren<Transform>(true)) all.Add(t);
                sb.Append(",\"arbre\":[");
                for (int i = 0; i < all.Count; i++)
                {
                    Transform t = all[i];
                    sb.Append(i > 0 ? "," : "").Append("{\"nom\":").Append(S(t.name)).Append(",\"parent\":").Append(t == ch ? -1 : all.IndexOf(t.parent))
                      .Append(",\"p\":").Append(V(t.localPosition)).Append(",\"q\":").Append(Q(t.localRotation)).Append(",\"s\":").Append(V(t.localScale)).Append('}');
                }
                sb.Append("],\"noeudMaillage\":").Append(all.IndexOf(smr.transform));
                // poses : assis (clip worker1_sitdown tenu a sa fin), volant voiture / camion (conducteurs de la circulation)
                sb.Append(",\"poses\":{");
                int np = 0;
                AnimationState st = anim != null ? anim["assis"] : null;
                if (st != null)
                {
                    foreach (AnimationState o in anim) o.enabled = false;
                    st.enabled = true; st.weight = 1f; st.time = st.length; anim.Sample();
                    sb.Append("\"assis\":").Append(PoseJson(bones, null)); np++;
                    st.enabled = false;
                }
                MethodInfo build = typeof(Avatar).GetMethod("BuildDriverPoses", BindingFlags.NonPublic | BindingFlags.Static);
                if (build != null) build.Invoke(null, null);
                foreach (string pn in new[] { "poseCar", "poseTruck" })
                {
                    FieldInfo pf = typeof(Avatar).GetField(pn, BindingFlags.NonPublic | BindingFlags.Static);
                    var d = pf != null ? pf.GetValue(null) as Dictionary<string, Quaternion> : null;
                    if (d == null) continue;
                    sb.Append(np++ > 0 ? "," : "").Append(S(pn == "poseCar" ? "volant" : "camion")).Append(':').Append(PoseJson(bones, d));
                }
                sb.Append("}}");
                File.WriteAllText(Path.Combine(dir, "avatar.json"), sb.ToString(), Encoding.UTF8);
                Log.Info("export des places : avatar, " + bones.Length + " os, " + smr.sharedMesh.vertexCount + " sommets, " + np + " poses");
            }
            finally { a.Destroy(); }
        }

        // pose : rotation locale de chaque os (de la table d, sinon telle qu'elle est)
        static string PoseJson(Transform[] bones, Dictionary<string, Quaternion> d)
        {
            var sb = new StringBuilder("{");
            int n = 0;
            foreach (Transform b in bones)
            {
                if (b == null) continue;
                Quaternion q;
                if (d != null) { if (!d.TryGetValue(b.name, out q)) continue; }
                else q = b.localRotation;
                sb.Append(n++ > 0 ? "," : "").Append(S(b.name)).Append(':').Append(Q(q));
            }
            return sb.Append('}').ToString();
        }

        // ---------------------------------------------------------------- maillages, matieres
        static bool WriteMesh(BinaryWriter bin, Mesh m, Matrix4x4 tr, BoneWeight[] weights)
        {
            Vector3[] v; Vector3[] nr; Vector2[] uv;
            try { v = m.vertices; nr = m.normals; uv = m.uv; } catch { return false; }
            if (v == null || v.Length == 0) return false;
            bool hasN = nr != null && nr.Length == v.Length, hasUv = uv != null && uv.Length == v.Length, hasW = weights != null && weights.Length == v.Length;
            int k = m.subMeshCount;
            var tris = new int[k][];
            for (int i = 0; i < k; i++) { try { tris[i] = m.GetTriangles(i); } catch { tris[i] = new int[0]; } }
            bin.Write(v.Length); bin.Write((hasN ? 1 : 0) | (hasUv ? 2 : 0) | (hasW ? 4 : 0)); bin.Write(k);
            foreach (int[] t in tris) bin.Write(t.Length);
            foreach (Vector3 p0 in v) { Vector3 p = tr.MultiplyPoint3x4(p0); bin.Write(p.x); bin.Write(p.y); bin.Write(p.z); }
            if (hasN) foreach (Vector3 n0 in nr) { Vector3 n = tr.MultiplyVector(n0).normalized; bin.Write(n.x); bin.Write(n.y); bin.Write(n.z); }
            if (hasUv) foreach (Vector2 p in uv) { bin.Write(p.x); bin.Write(p.y); }
            if (hasW) foreach (BoneWeight w in weights) { bin.Write(w.boneIndex0); bin.Write(w.boneIndex1); bin.Write(w.boneIndex2); bin.Write(w.boneIndex3); bin.Write(w.weight0); bin.Write(w.weight1); bin.Write(w.weight2); bin.Write(w.weight3); }
            foreach (int[] t in tris) foreach (int i in t) bin.Write(i);
            return true;
        }

        static string MatJson(Material m)
        {
            if (m == null) return "{}";
            Color c = m.HasProperty("_Color") ? m.color : Color.white;
            int tex = -1;
            Texture t = m.HasProperty("_MainTex") ? m.mainTexture : null;
            if (t != null && !texIndex.TryGetValue(t, out tex))
            {
                tex = texIndex.Count;
                texIndex[t] = tex;
                try { SaveTexture(t, Path.Combine(Path.Combine(dir, "textures"), tex + ".png"), 512); } catch (Exception e) { Log.Warn("export des places : texture " + t.name + " : " + e.Message); }
            }
            string sh = m.shader != null ? m.shader.name : "";
            return "{\"nom\":" + S(m.name) + ",\"couleur\":[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "],\"tex\":" + tex
                   + ",\"transparent\":" + (sh.Contains("Transparent") || sh.Contains("Cutout") ? "true" : "false") + "}";
        }

        static void SaveTexture(Texture t, string path, int max)
        {
            int w = t.width, h = t.height;
            while (w > max || h > max) { w = Mathf.Max(1, w / 2); h = Mathf.Max(1, h / 2); }
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static string F(float f) { return float.IsNaN(f) || float.IsInfinity(f) ? "0" : f.ToString("R", CultureInfo.InvariantCulture); }
        static string V(Vector3 v) { return "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]"; }
        static string Q(Quaternion q) { return "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]"; }
        static string S(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
