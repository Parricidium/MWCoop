using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // VAZ-2108 Rally (demande de JD, 18/10) : une voiture par joueur, choisie au F10 (onglet VAZ-2108), chacune d'une couleur
    // unique avec son numero sur les portieres (couleur n = numero n, 1 a 16). Modele « VAZ-2108 Rally » de matthew0451
    // (Sketchfab, CC BY-NC 4.0 ; credit dans le lanceur). Construction : VazCar ; tenue de route : VazHandling.
    // Autorite (comme les Jokkis) : l'hote attribue les couleurs (premier arrive premier servi), diffuse la table (V_SET :
    // joueur, couleur, place d'apparition) ; chacun construit / detruit les voitures MWCoop-VAZ-<n> (meme nom partout :
    // VehicleSync les suit comme les autres). En solo, la meme chose sans reseau.
    // La voiture apparait a cote du joueur qui la prend ; « Faire venir » la ramene a cote de lui (l'hote, qui fait autorite
    // sur une voiture sans conducteur, la deplace ; VehicleSync recale les autres). Rendue par le joueur, ou quand il quitte ;
    // Rien n'est garde (JD, 18/10 soir) : un joueur qui quitte perd sa VAZ (detruite chez tous) ; a la reconnexion, a une
    // nouvelle partie ou a un chargement, toutes les couleurs sont libres -- on la reprend au F10.
    // Interrupteur : Ajouts.Vaz ([Coop] AjoutVaz, lanceur ; choix de l'hote valable pour tous).
    // Messages Msg.Race, rangs 30-35 (Races.OnMessage les passe ici).
    // Evo III Gr. A (JD, 20/10 ; extension privee MWCoop/evo3, VazCar.Evo3) : meme table, chaque place porte son modele (0 VAZ,
    // 1 Evo) et l'empreinte de la livree du joueur (EvoLivery ; "" : couleur + numero comme la VAZ). Les couleurs et numeros
    // sont communs aux deux modeles (jamais deux voitures de la meme couleur). La voiture garde le nom MWCoop-VAZ-<n> chez
    // tous : un joueur sans l'extension construit une VAZ a la place, de la meme couleur et du meme numero (VehicleSync la
    // suit pareil). Courses sur glace et rallye : CarOf(joueur), quel que soit le modele.
    public static class Vaz
    {
        public const string Prefix = "MWCoop-VAZ-";
        public const int Count = 16;
        //  30 V_ASK   invite -> hote : couleur voulue (0 : rendre la sienne), place (x, y, z, cap)
        //  31 V_SET   hote -> tous : n, (joueur, couleur, x, y, z, cap)... -- table complete
        //  32 V_DENY  hote -> un invite : couleur, motif (1 deja prise, 2 en a deja une, 3 inconnue, 4 coupee chez l'hote)
        //  33 V_CALL  invite -> hote : faire venir sa VAZ a cette place (x, y, z, cap)
        //  34 V_ROOF  trappe de toit : invite -> hote (couleur, ouverte ?), hote -> tous (meme contenu, applique)
        //  35 V_LIV   invite -> hote : empreinte de sa livree (changee au lanceur pendant la partie)
        // (V_ASK et V_SET portent aussi le modele et l'empreinte de la livree : Session.NetVersion 43)
        public const int V_ASK = 30, V_SET = 31, V_DENY = 32, V_CALL = 33, V_ROOF = 34, V_LIV = 35;

        static readonly string[] NamesFr = { "Rouge", "Bleu", "Jaune", "Vert", "Blanc", "Noir", "Orange", "Violet", "Bleu ciel", "Rose", "Gris", "Bordeaux", "Turquoise", "Vert pomme", "Marron", "Bleu nuit" };
        static readonly string[] NamesEn = { "Red", "Blue", "Yellow", "Green", "White", "Black", "Orange", "Purple", "Sky blue", "Pink", "Grey", "Burgundy", "Turquoise", "Lime", "Brown", "Navy" };
        static readonly Color[] Colors = {
            new Color(0.72f, 0.05f, 0.05f), new Color(0.06f, 0.22f, 0.68f), new Color(0.95f, 0.74f, 0.05f), new Color(0.08f, 0.45f, 0.16f),
            new Color(0.92f, 0.92f, 0.90f), new Color(0.05f, 0.05f, 0.06f), new Color(0.95f, 0.40f, 0.04f), new Color(0.38f, 0.13f, 0.58f),
            new Color(0.34f, 0.64f, 0.92f), new Color(0.92f, 0.40f, 0.62f), new Color(0.48f, 0.50f, 0.53f), new Color(0.40f, 0.04f, 0.09f),
            new Color(0.06f, 0.60f, 0.60f), new Color(0.50f, 0.80f, 0.14f), new Color(0.38f, 0.22f, 0.11f), new Color(0.05f, 0.09f, 0.26f) };

        public static string ColorName(int n) { return n >= 1 && n <= Count ? Lang.T(NamesFr[n - 1], NamesEn[n - 1]) : "?"; }
        public static Color ColorOf(int n) { return n >= 1 && n <= Count ? Colors[n - 1] : Color.white; }

        class Slot { public int Number, Model; public string Livery = ""; public Vector3 Pos; public float Yaw; }
        class Rent { public int Id, Number, Model = -1; public VazCar.Built Car; public float NextBuild; public int Tries; public int LiveryGen = -1; }

        static readonly Dictionary<int, Slot> wanted = new Dictionary<int, Slot>();     // joueur -> couleur + place (table de l'hote)
        static readonly Dictionary<int, Rent> rents = new Dictionary<int, Rent>();      // joueur -> voiture construite ici
        static readonly Dictionary<int, int> lastLevel = new Dictionary<int, int>();
        static bool hooked;
        static float nextTick;
        public static event System.Action Changed;

        static bool Solo { get { return !Session.Active; } }
        static bool Host { get { return !Session.Active || Session.IsHost; } }
        static int Me { get { return Session.Active ? Session.LocalId : 0; } }
        public static bool Enabled { get { return Ajouts.Vaz && VazCar.HasFiles; } }

        // ---------------------------------------------------------------- interface (F10)
        public static int NumberOf(int playerId) { Slot s; return wanted.TryGetValue(playerId, out s) ? s.Number : 0; }
        public static int Mine { get { return NumberOf(Me); } }
        public static bool IsFree(int n)
        {
            if (n < 1 || n > Count) return false;
            foreach (Slot s in wanted.Values) if (s.Number == n) return false;
            return true;
        }
        public static int OwnerOf(int n) { foreach (KeyValuePair<int, Slot> kv in wanted) if (kv.Value.Number == n) return kv.Key; return -1; }
        public static Transform CarOf(int playerId) { Rent r; return rents.TryGetValue(playerId, out r) && r.Car != null && r.Car.Car != null ? r.Car.Car.transform : null; }
        public static bool IsVaz(Transform carRoot) { return carRoot != null && carRoot.name.StartsWith(Prefix); }
        // Modele choisi par ce joueur (VazCar.ModelVaz / ModelEvo) et son nom ; Evo disponible ici (extension installee).
        public static int ModelOf(int playerId) { Slot s; return wanted.TryGetValue(playerId, out s) ? s.Model : VazCar.ModelVaz; }
        public static string ModelName(int playerId) { return VazCar.ModelOf(ModelOf(playerId)).Name; }
        public static bool EvoAvailable { get { return VazCar.Evo3.HasFiles; } }
        public static int MineModel { get { return ModelOf(Me); } }
        public static string MineModelName { get { return ModelName(Me); } }
        public static string LiveryOf(int playerId) { Slot s; return wanted.TryGetValue(playerId, out s) ? s.Livery ?? "" : ""; }

        // Place a cote du joueur d'ici : 4 m a sa droite, meme cap, posee sur le sol.
        static void SpotNearMe(out Vector3 pos, out float yaw)
        {
            GameObject pl = GameObject.Find("PLAYER");
            Transform t = pl != null ? pl.transform : null;
            Transform cam = t != null ? t.Find("Pivot/AnimPivot/Camera/FPSCamera") : null;
            Vector3 fwd = cam != null ? cam.forward : t != null ? t.forward : Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 at = (t != null ? t.position : Vector3.zero) + right * 3.5f + fwd * 2f;
            if (Config.GetInt("Test", "VazDevant", 0) != 0) at = (t != null ? t.position : Vector3.zero) + fwd * 6.5f;   // (essais : de profil devant la camera)
            float y = at.y + 0.8f, best = float.MaxValue;
            // (depuis 2 m au-dessus des pieds : un auvent ou un toit plus haut n'est pas le sol -- essai du 18/10, chute de 3 m)
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(at.x, at.y + 2f, at.z), Vector3.down, 12f))
            {
                if (h.collider.isTrigger || h.collider.attachedRigidbody != null) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y + 0.8f; }
            }
            pos = new Vector3(at.x, y, at.z);
            yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg + (Config.GetInt("Test", "VazDevant", 0) != 0 ? 90f : 0f);
        }

        // Prendre la couleur n (gratuit). Faux : refus connu d'avance (message a l'ecran).
        public static bool Request(int n, int model = VazCar.ModelVaz)
        {
            if (!Enabled) { Hud.Toast(Lang.T("VAZ-2108 coup\u00E9e (lanceur, Ajouts)", "VAZ-2108 is off (launcher, Add-ons)")); return false; }
            if (!PlayerSync.InGame) return false;
            if (n < 1 || n > Count) return false;
            if (model == VazCar.ModelEvo && !EvoAvailable) model = VazCar.ModelVaz;
            if (wanted.ContainsKey(Me)) { Deny(2, n); return false; }
            if (!IsFree(n)) { Deny(1, n); return false; }
            Vector3 pos; float yaw; SpotNearMe(out pos, out yaw);
            string liv = model == VazCar.ModelEvo ? EvoLivery.MyHash : "";
            if (Host) { Assign(Me, new Slot { Number = n, Model = model, Livery = liv, Pos = pos, Yaw = yaw }); return true; }
            Session.SendToHost(new NetWriter(Msg.Race).U8(V_ASK).U8(n).Vec(pos).F32(yaw).U8(model).Str(liv), true);
            Log.Info("vaz : n " + n + " demandee a l'hote");
            return true;
        }

        // Rendre la sienne (toujours possible, meme l'ajout coupe).
        public static void Release()
        {
            if (Host) { if (wanted.Remove(Me)) { HostChanged("rendue par " + NameOf(Me)); } return; }
            if (wanted.ContainsKey(Me)) Session.SendToHost(new NetWriter(Msg.Race).U8(V_ASK).U8(0).Vec(Vector3.zero).F32(0f).U8(0).Str(""), true);
        }

        // Faire venir la sienne a cote de soi.
        public static void Call()
        {
            if (!wanted.ContainsKey(Me)) return;
            Vector3 pos; float yaw; SpotNearMe(out pos, out yaw);
            if (Host) Move(Me, pos, yaw);
            else Session.SendToHost(new NetWriter(Msg.Race).U8(V_CALL).Vec(pos).F32(yaw), true);
        }

        // Trappe de toit de la VAZ n (clic d'un joueur) : l'hote applique et diffuse ; un invite le demande a l'hote.
        public static void RoofToggle(int n, bool open)
        {
            if (Host) { ApplyRoof(n, open); if (Session.Active && Session.RemoteCount > 0) Session.Broadcast(new NetWriter(Msg.Race).U8(V_ROOF).U8(n).Bool(open), true); }
            else Session.SendToHost(new NetWriter(Msg.Race).U8(V_ROOF).U8(n).Bool(open), true);
        }

        static void ApplyRoof(int n, bool open)
        {
            foreach (Rent r in rents.Values)
                if (r.Number == n && r.Car != null) r.Car.RoofOpen = open;
        }

        // ---------------------------------------------------------------- cycle
        public static void OnLevelLoaded()
        {
            foreach (Rent r in rents.Values) VazCar.Destroy(r.Car);
            rents.Clear(); wanted.Clear(); lastLevel.Clear(); nextTick = 0; testStep = 0; testMoved = false; entryStep = 0; openDone = false;
            if (!hooked)
            {
                hooked = true;
                Session.PlayerLeft += pi => { if (Session.IsHost && wanted.ContainsKey(pi.Id)) { wanted.Remove(pi.Id); HostChanged("depart de " + pi.Name); } lastLevel.Remove(pi.Id); };
            }
        }

        static string World() { var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat("PlayerID"); return v != null ? v.Value.ToString("F0") : ""; }
        static string NameOf(int id) { PlayerInfo pi; return Session.Players.TryGetValue(id, out pi) ? pi.Name : Solo && id == 0 ? "solo" : "#" + id; }
        static string Clean(string s) { return (s ?? "").Replace(";", "").Replace(":", "").Replace("=", "").Replace("|", ""); }

        // Apres la physique et les automates (VehicleSync.LateUpdate) : ralenti des VAZ (VazHandling.Idle).
        public static void LateUpdate()
        {
            if (!PlayerSync.InGame) return;
            foreach (Rent r in rents.Values) if (r.Car != null && r.Car.Car != null) VazHandling.Idle(r.Car);
        }

        public static void Update()
        {
            if (!PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            foreach (Rent r in rents.Values) if (r.Car != null && r.Car.Car != null) VazCar.Tick(r.Car, now);
            if (now < nextTick) return;
            nextTick = now + 0.5f;
            if (EvoLivery.Poll()) MyLiveryChanged();
            if (Host) HostStep(now);
            Reconcile(now);
        }

        // Hote : table envoyee a chaque joueur qui arrive en partie ; ajout coupe -> plus de VAZ.
        static void HostStep(float now)
        {
            if (!Enabled)
            {
                if (wanted.Count > 0) { wanted.Clear(); HostChanged("ajout coupe"); }
                return;
            }
            if (Solo) return;
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                int inGame = pi.Local ? 1 : pi.Level;
                int was; lastLevel.TryGetValue(pi.Id, out was);
                if (inGame == 1 && was != 1 && !pi.Local && pi.Peer != null && Session.T != null) Session.T.SendReliable(pi.Peer, SetMsg().ToArray());
                lastLevel[pi.Id] = inGame;
            }
        }

        static void Assign(int id, Slot s)
        {
            wanted[id] = s;
            string mn = VazCar.ModelOf(s.Model).Name;
            Hud.Toast(NameOf(id) + Lang.T(" prend la " + mn + " n\u00B0 ", " takes " + mn + " no. ") + s.Number + " (" + ColorName(s.Number) + ")");
            HostChanged(NameOf(id) + " -> n " + s.Number + " " + mn + (s.Livery.Length > 0 ? " livree " + s.Livery : ""));
        }

        // Livree du joueur d'ici changee (lanceur, pendant la partie) : sa place dans la table (Evo seulement).
        static void MyLiveryChanged()
        {
            Slot s;
            if (!wanted.TryGetValue(Me, out s) || s.Model != VazCar.ModelEvo) return;
            string h = EvoLivery.MyHash;
            if (Host) { if (s.Livery != h) { s.Livery = h; HostChanged("livree de " + NameOf(Me)); } }
            else Session.SendToHost(new NetWriter(Msg.Race).U8(V_LIV).Str(h), true);
        }

        static void Move(int id, Vector3 pos, float yaw)
        {
            Rent r;
            if (!rents.TryGetValue(id, out r) || r.Car == null || r.Car.Car == null) return;
            if (VehicleSync.RemotelyDriven(r.Car.Car.transform) || VehicleSync.LocalDrivingRoot == r.Car.Car.transform) { Hud.Toast(Lang.T("La VAZ est conduite", "The VAZ is being driven")); return; }
            Transform t = r.Car.Car.transform;
            t.position = pos; t.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (r.Car.Rb != null) { r.Car.Rb.velocity = Vector3.zero; r.Car.Rb.angularVelocity = Vector3.zero; }
            Log.Info("vaz : n " + r.Number + " ramenee pres de " + NameOf(id) + " en " + pos.ToString("F1"));
        }

        static void HostChanged(string why)
        {
            if (!Host) return;
            Log.Info("vaz : table (" + why + ") : " + Describe());
            if (Session.Active && Session.RemoteCount > 0) Session.Broadcast(SetMsg(), true);
            if (Changed != null) Changed();
            Reconcile(Time.realtimeSinceStartup);
        }

        static NetWriter SetMsg()
        {
            var w = new NetWriter(Msg.Race).U8(V_SET).U8(wanted.Count);
            foreach (KeyValuePair<int, Slot> kv in wanted)
            {
                Slot s = kv.Value; Transform c = CarOf(kv.Key);
                Vector3 p = c != null ? c.position + Vector3.up * 0.3f : s.Pos; float y = c != null ? c.eulerAngles.y : s.Yaw;
                w.U8(kv.Key).U8(s.Number).Vec(p).F32(y).U8(s.Model).Str(s.Livery ?? "");
            }
            return w;
        }

        static string Describe()
        {
            if (wanted.Count == 0) return "aucune VAZ";
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, Slot> kv in wanted) sb.Append(sb.Length > 0 ? ", " : "").Append(NameOf(kv.Key)).Append(" n ").Append(kv.Value.Number).Append(kv.Value.Model == VazCar.ModelEvo ? " evo" + (kv.Value.Livery.Length > 0 ? " livree " + kv.Value.Livery : "") : "");
            return sb.ToString();
        }

        static void Deny(int why, int n)
        {
            Hud.Toast(why == 1 ? Lang.T("VAZ n\u00B0 " + n + " d\u00E9j\u00E0 prise", "VAZ no. " + n + " already taken")
                    : why == 2 ? Lang.T("Vous avez d\u00E9j\u00E0 une VAZ", "You already have a VAZ")
                    : why == 4 ? Lang.T("VAZ-2108 coup\u00E9e chez l'h\u00F4te", "VAZ-2108 is off on the host")
                    : Lang.T("VAZ : num\u00E9ro inconnu ", "VAZ: unknown number ") + n);
        }

        // ---------------------------------------------------------------- messages (Msg.Race 30-33)
        public static void OnMessage(int kind, Peer from, NetReader r)
        {
            if (kind == V_ASK && Session.IsHost)
            {
                int n = r.U8(); Vector3 pos = r.Vec(); float yaw = r.F32();
                int model = r.More ? r.U8() : VazCar.ModelVaz; string liv = r.More ? r.Str() : "";
                if (model != VazCar.ModelEvo) { model = VazCar.ModelVaz; liv = ""; }
                if (n == 0) { if (wanted.Remove(from.Id)) { HostChanged("rendue par " + NameOf(from.Id)); } return; }
                int why = !Enabled ? 4 : n < 1 || n > Count ? 3 : wanted.ContainsKey(from.Id) ? 2 : !IsFree(n) ? 1 : 0;
                if (why != 0)
                {
                    if (Session.T != null) Session.T.SendReliable(from, new NetWriter(Msg.Race).U8(V_DENY).U8(n).U8(why).ToArray());
                    Log.Info("vaz : n " + n + " refusee a " + NameOf(from.Id) + " (motif " + why + ")");
                    return;
                }
                Assign(from.Id, new Slot { Number = n, Model = model, Livery = liv ?? "", Pos = pos, Yaw = yaw });
                return;
            }
            if (kind == V_LIV && Session.IsHost)
            {
                string h = r.Str(); Slot s;
                if (wanted.TryGetValue(from.Id, out s) && s.Model == VazCar.ModelEvo && s.Livery != h) { s.Livery = h ?? ""; HostChanged("livree de " + NameOf(from.Id)); }
                return;
            }
            if (kind == V_ROOF)
            {
                int n = r.U8(); bool open = r.Bool();
                if (Session.IsHost) RoofToggle(n, open); else ApplyRoof(n, open);
                return;
            }
            if (kind == V_CALL && Session.IsHost) { Vector3 pos = r.Vec(); float yaw = r.F32(); Move(from.Id, pos, yaw); return; }
            if (kind == V_SET && !Session.IsHost)
            {
                wanted.Clear();
                for (int i = 0, n = r.U8(); i < n; i++) { int id = r.U8(), num = r.U8(); Vector3 p = r.Vec(); float y = r.F32(); int md = r.U8(); string lv = r.Str(); wanted[id] = new Slot { Number = num, Model = md, Livery = lv ?? "", Pos = p, Yaw = y }; }
                Log.Info("vaz : table de l'hote : " + Describe());
                if (Changed != null) Changed();
                Reconcile(Time.realtimeSinceStartup);
                return;
            }
            if (kind == V_DENY && !Session.IsHost) { int n = r.U8(), why = r.U8(); Deny(why, n); }
        }

        // ---------------------------------------------------------------- table -> voitures d'ici
        static void Reconcile(float now)
        {
            if (!PlayerSync.InGame) return;
            var gone = new List<int>();
            foreach (KeyValuePair<int, Rent> kv in rents) { Slot s; if (!wanted.TryGetValue(kv.Key, out s) || s.Number != kv.Value.Number || (kv.Value.Model >= 0 && BuildModel(s) != kv.Value.Model)) gone.Add(kv.Key); }
            foreach (int id in gone) { VazCar.Destroy(rents[id].Car); Log.Info("vaz : n " + rents[id].Number + " de " + NameOf(id) + " retiree"); rents.Remove(id); if (Changed != null) Changed(); }
            foreach (KeyValuePair<int, Slot> kv in wanted)
            {
                Rent r;
                if (!rents.TryGetValue(kv.Key, out r)) { r = new Rent { Id = kv.Key, Number = kv.Value.Number }; rents[kv.Key] = r; }
                if ((r.Car == null || r.Car.Car == null) && now >= r.NextBuild && r.Tries < 5)
                {
                    r.NextBuild = now + 2f; r.Tries++;
                    r.Model = BuildModel(kv.Value); r.LiveryGen = -1;
                    if (kv.Value.Model == VazCar.ModelEvo && r.Model != VazCar.ModelEvo) Log.Info("vaz : n " + r.Number + " de " + NameOf(kv.Key) + " : Evo III sans l'extension ici -> VAZ a la place");
                    try { r.Car = VazCar.Build(Prefix + r.Number, r.Number, ColorOf(r.Number), kv.Value.Pos, Quaternion.Euler(0f, kv.Value.Yaw, 0f), VazCar.ModelOf(r.Model)); }
                    catch (System.Exception e) { Log.Error("vaz : construction de la n " + r.Number + " : " + e); if (r.Car != null) VazCar.Destroy(r.Car); r.Car = null; }
                    if (r.Car != null && Changed != null) Changed();
                }
                Livery(r, kv.Value);
            }
        }

        // Modele construit ici pour cette place : l'Evo si l'extension est la, sinon la VAZ.
        static int BuildModel(Slot s) { return s.Model == VazCar.ModelEvo && EvoAvailable ? VazCar.ModelEvo : VazCar.ModelVaz; }

        // Livree de la place sur la voiture d'ici (Evo seulement ; image pas encore recue : couleur du joueur, reprise a son
        // arrivee -- EvoLivery.Generation).
        static void Livery(Rent r, Slot s)
        {
            if (r.Car == null || r.Car.Car == null || r.Model != VazCar.ModelEvo) return;
            string want = s.Livery ?? "";
            if ((r.Car.LiveryHash ?? "") == want && (want.Length == 0 || r.LiveryGen == EvoLivery.Generation)) return;
            Texture2D t = want.Length > 0 ? EvoLivery.Get(want) : null;
            r.LiveryGen = EvoLivery.Generation;
            if (want.Length > 0 && t == null) { if (r.Car.LiveryHash != null) VazCar.SetLivery(r.Car, null, null); return; }
            if ((r.Car.LiveryHash ?? "") != want) VazCar.SetLivery(r.Car, t, want.Length > 0 ? want : null);
        }

        // ---------------------------------------------------------------- autotest "vaz"
        // 20 s : l'hote prend la n 3 ; 30 s : il note sa voiture ; 40 s : l'invite note la sienne (celle de l'hote chez lui) ;
        // 50 s : l'hote la fait venir ; 60 s : il la rend ; 70 s : chacun note voiture partie. [Test] VazGarder=1 : pas rendue.
        static int testStep;
        static bool testMoved, placesDone, openDone;
        static int entryStep;

        static void PutPlayer(Vector3 local)
        {
            Transform car = CarOf(Me); GameObject pl = GameObject.Find("PLAYER");
            if (car == null || pl == null) return;
            pl.transform.position = car.TransformPoint(local) + Vector3.up * 0.05f;
            pl.transform.rotation = Quaternion.LookRotation(car.forward);
            Log.Info("autotest vaz : joueur pose en " + local.ToString("F2") + " (repere de la voiture)");
        }

        static string EntryState()
        {
            Transform car = CarOf(Me); GameObject pl = GameObject.Find("PLAYER");
            var d = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool("GUIdrive");
            var p = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool("GUIpassenger");
            Transform dt = car != null ? car.Find("Functions/PlayerTrigger/DriveTriggerX") : null;
            PlayMakerFSM f = dt != null ? Game.FsmOn(dt.gameObject, "PlayerTrigger") : null;
            string geo = "";
            if (entryStep <= 1 && dt != null)
            {
                foreach (Collider c in dt.GetComponents<Collider>())
                {
                    var cap = c as CapsuleCollider;
                    geo += " ; zone " + c.GetType().Name + " actif " + c.enabled + "/" + dt.gameObject.activeInHierarchy + " declencheur " + c.isTrigger + " centre " + car.InverseTransformPoint(dt.TransformPoint(cap != null ? cap.center : Vector3.zero)).ToString("F2")
                           + (cap != null ? " rayon " + cap.radius.ToString("F2") + " hauteur " + cap.height.ToString("F2") + " axe " + cap.direction : "") + " echelle " + dt.lossyScale.ToString("F2") + " couche " + LayerMask.LayerToName(dt.gameObject.layer);
                }
                if (pl != null)
                    foreach (Collider c in pl.GetComponentsInChildren<Collider>(true))
                        geo += " ; joueur " + Recon.Path(c.transform) + " " + c.GetType().Name + " tag " + c.tag + " couche " + LayerMask.LayerToName(c.gameObject.layer) + " actif " + c.enabled + "/" + c.gameObject.activeInHierarchy
                               + (c is CharacterController ? " centre " + ((CharacterController)c).center.ToString("F2") + " rayon " + ((CharacterController)c).radius.ToString("F2") + " h " + ((CharacterController)c).height.ToString("F2") : "");
                geo += " ; couches qui se touchent (zone/joueur) : " + (pl != null && dt != null ? (!Physics.GetIgnoreLayerCollision(dt.gameObject.layer, pl.layer)).ToString() : "?");
            }
            return "GUIdrive " + (d != null && d.Value) + ", GUIpassenger " + (p != null && p.Value) + ", DriveTriggerX " + (f != null ? f.ActiveStateName + (f.enabled ? "" : " (COUPE)") : "?")
                   + ", joueur en " + (car != null && pl != null ? car.InverseTransformPoint(pl.transform.position).ToString("F2") : "?") + geo;
        }
        // Autotest « vazcle » ([Test] TestVoiture=MWCoop-VAZ-3) : la n 3 prise a 8 s ; l'essai « cle » de Jobs fait le reste
        // (volant 22 s, contact 32 s, demarreur 34 s tenu CleTenue s). Chaque seconde de 31 a 50 s : regime, reservoir,
        // conduite d'essence, batterie ; a 35 s les variables de l'automate Starter.
        static float cleLog; static bool cleDump, panneDone, sonDone, moteurDone;
        static void TestKey(float t)
        {
            bool asker = Config.GetInt("Test", "CleInvite", 0) != 0 ? !Host : Host;
            if (asker && testStep == 0 && t > (Host ? 8f : 4f)) { testStep = 1; Log.Info("autotest vazcle : demande n 3 -> " + Request(3)); }
            GameObject car = Game.FindAny(Config.Get("Test", "TestVoiture", "MWCoop-VAZ-3"));
            // [Test] VazPanne=ligne (conduite d'essence vide) | batterie (charge 20) | noyee (chambre a 30) : pose a 30 s, pour retrouver le refus de JD
            if (car != null && t > 30f && !panneDone)
            {
                panneDone = true;
                string pan = Config.Get("Test", "VazPanne", "");
                var sb0 = new System.Text.StringBuilder("autotest vazcle : automates a charge/essence :");
                foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
                    foreach (FsmFloat v in f.FsmVariables.FloatVariables)
                    {
                        string n = v.Name.ToLower();
                        if (!(n.Contains("charge") || n.Contains("fuel") || n.Contains("temp") || n.Contains("choke"))) continue;
                        sb0.Append(' ').Append(Recon.Path(f.transform).Substring(car.name.Length)).Append(':').Append(f.FsmName).Append('.').Append(v.Name).Append('=').Append(v.Value.ToString("F2"));
                        if ((pan == "ligne" && f.FsmName == "FuelLine" && v.Name == "FuelLevel") || (pan == "batterie" && f.FsmName != "Starter" && v.Name == "Charge") || (pan == "noyee" && f.FsmName == "Priming" && v.Name == "FuelChamber")) { v.Value = pan == "ligne" ? 0f : pan == "noyee" ? 30f : 20f; sb0.Append("->").Append(v.Value); }
                    }
                Log.Info(sb0.ToString());
                if (pan == "sec")   // moteur froid pas amorce : chambre a 10, PrimingOK faux (comme un hiver sans starter)
                    foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
                        if (f.FsmName == "Priming") { f.FsmVariables.FindFsmFloat("FuelChamber").Value = 10f; f.FsmVariables.FindFsmBool("PrimingOK").Value = false; Log.Info("autotest vazcle : " + Recon.Path(f.transform) + " chambre 10, PrimingOK faux (" + f.ActiveStateName + ")"); }
            }
            if (car == null || t < 31f || t > 50f || t < cleLog) return;
            cleLog = Mathf.Floor(t) + 1f;
            Drivetrain dt = car.GetComponent<Drivetrain>();
            var sb = new System.Text.StringBuilder("autotest vazcle : regime " + (dt != null ? dt.rpm.ToString("F0") + " gaz " + dt.throttle.ToString("F3") + " ralenti " + dt.idlethrottle.ToString("F3") + (dt.enabled ? "" : " DRIVETRAIN COUPE") + (dt.canStall ? " peut caler" : "") + " rb " + (car.GetComponent<Rigidbody>() != null && car.GetComponent<Rigidbody>().isKinematic ? "cinematique" : "libre") + " " + VehicleSync.StarterState(car.name) : "?"));
            foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f.FsmName == "Data" && f.name == "FuelTankSorbett") { FsmFloat v = f.FsmVariables.FindFsmFloat("FuelLevel"); sb.Append(", reservoir ").Append(v != null ? v.Value.ToString("F1") : "?"); }
                if (f.FsmName == "FuelLine") { FsmFloat v = f.FsmVariables.FindFsmFloat("FuelLevel"); sb.Append(", conduite ").Append(v != null ? v.Value.ToString("F2") : "?").Append(" (").Append(f.ActiveStateName).Append(f.enabled ? "" : " COUPE").Append(')'); }
                if (f.FsmName == Config.Get("Test", "VazDump", "Starter") && (Config.Get("Test", "VazDump", "") == "" || f.name == "Radiator") && !cleDump && t > 35f)
                {
                    cleDump = true;
                    var d = new System.Text.StringBuilder("autotest vazcle : Starter " + Recon.Path(f.transform) + " etat " + f.ActiveStateName + " :");
                    foreach (FsmFloat v in f.FsmVariables.FloatVariables) d.Append(' ').Append(v.Name).Append('=').Append(v.Value.ToString("F2"));
                    foreach (FsmBool v in f.FsmVariables.BoolVariables) d.Append(' ').Append(v.Name).Append('=').Append(v.Value);
                    foreach (FsmGameObject v in f.FsmVariables.GameObjectVariables) d.Append(' ').Append(v.Name).Append("->").Append(v.Value != null ? Recon.Path(v.Value.transform) : "null");
                    foreach (FsmState st in f.FsmStates) { d.Append('\n').Append("  [").Append(st.Name).Append(']'); foreach (FsmStateAction a in st.Actions) d.Append(' ').Append(a.GetType().Name); foreach (FsmTransition tr in st.Transitions) d.Append(" -").Append(tr.EventName).Append("->").Append(tr.ToState); }
                    Log.Info(d.ToString());
                    // actions de Fuel Mixture / Fuel Mixture 2 / Turn key / Crank up avec leurs parametres (pour savoir ce qui decide)
                    var d2 = new System.Text.StringBuilder("autotest vazcle : actions du Starter :");
                    foreach (FsmState st in f.FsmStates)
                    {
                        if (Config.Get("Test", "VazDump", "") == "" && st.Name != "Fuel Mixture" && st.Name != "Fuel Mixture 2" && st.Name != "Turn key" && st.Name != "Crank up" && st.Name != "ACC") continue;
                        d2.Append('\n').Append("[").Append(st.Name).Append(']');
                        foreach (FsmStateAction ac in st.Actions)
                        {
                            d2.Append('\n').Append("   ").Append(ac.GetType().Name).Append(':');
                            foreach (System.Reflection.FieldInfo fi in ac.GetType().GetFields())
                            {
                                object v = fi.GetValue(ac); if (v == null) continue;
                                string sv;
                                if (v is NamedVariable) { NamedVariable nv = (NamedVariable)v; sv = (nv.UseVariable ? nv.Name + "=" : "") + (v is FsmFloat ? ((FsmFloat)v).Value.ToString("F2") : v is FsmBool ? ((FsmBool)v).Value.ToString() : v is FsmString ? ((FsmString)v).Value : v is FsmGameObject ? (((FsmGameObject)v).Value != null ? ((FsmGameObject)v).Value.name : "null") : v is FsmInt ? ((FsmInt)v).Value.ToString() : "?"); }
                                else if (v is FsmOwnerDefault) { FsmOwnerDefault od = (FsmOwnerDefault)v; sv = od.OwnerOption == OwnerDefaultOption.UseOwner ? "(proprio)" : (od.GameObject.Value != null ? Recon.Path(od.GameObject.Value.transform) : "null"); }
                                else if (v is FsmEvent) sv = "ev:" + ((FsmEvent)v).Name;
                                else if (fi.FieldType.IsEnum || fi.FieldType.IsPrimitive) sv = v.ToString();
                                else continue;
                                d2.Append(' ').Append(fi.Name).Append('=').Append(sv);
                            }
                        }
                    }
                    Log.Info(d2.ToString());
                }
            }
            Log.Info(sb.ToString());
            // [Test] VazMoteur=1 : a 37 s, les reglages (champs flottants) du Drivetrain de la VAZ, de la SORBET et de la CORRIS
            if (t > 37f && !moteurDone && Config.GetInt("Test", "VazMoteur", 0) != 0)
            {
                moteurDone = true;
                foreach (GameObject g in new[] { car, Game.FindAny("SORBET(190-200psi)"), Game.FindAny("CORRIS") })
                {
                    Drivetrain d = g != null ? g.GetComponent<Drivetrain>() : null;
                    if (d == null) continue;
                    var sd = new System.Text.StringBuilder("autotest vazcle : Drivetrain " + g.name + " :");
                    foreach (System.Reflection.FieldInfo fi in typeof(Drivetrain).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        if (fi.FieldType == typeof(float) || fi.FieldType == typeof(bool) || fi.FieldType == typeof(int)) sd.Append(' ').Append(fi.Name).Append('=').Append(fi.FieldType == typeof(float) ? ((float)fi.GetValue(d)).ToString("0.###") : fi.GetValue(d).ToString());
                    Log.Info(sd.ToString());
                }
            }
            // [Test] VazSon=1 : a 40 s (moteur tourne), les automates du son de la Rivett copie et ses sources (volume, hauteur)
            if (t > 40f && !sonDone && Config.GetInt("Test", "VazSon", 0) != 0)
            {
                sonDone = true;
                Transform snd = car.transform.Find("VAZ/VAZ_moteur_son/AudioEngineCorrisTune");
                if (snd == null) Log.Info("autotest vazcle : son de la Rivett introuvable");
                else { foreach (PlayMakerFSM f in snd.GetComponentsInChildren<PlayMakerFSM>(true)) Log.Info("autotest vazcle : son " + DumpFsm(f)); foreach (AudioSource a in snd.GetComponentsInChildren<AudioSource>(true)) Log.Info("autotest vazcle : source " + Recon.Path(a.transform) + " " + (a.clip != null ? a.clip.name : "-") + (a.gameObject.activeInHierarchy ? "" : " inactif") + (a.isPlaying ? " joue" : "") + " v " + a.volume.ToString("F2") + " h " + a.pitch.ToString("F2") + (a.mute ? " muet" : "")); }
                GameObject ct = Game.FindAny("CORRIS/Simulation/ExhaustCorris/ExhaustSmoke/AudioEngineCorrisTune");
                if (ct != null) foreach (PlayMakerFSM f in ct.GetComponentsInChildren<PlayMakerFSM>(true)) Log.Info("autotest vazcle : CORRIS " + DumpFsm(f));
            }
        }

        // Essais : un automate, ses variables, et pour chaque etat ses actions avec leurs parametres (Fsm, objets, valeurs).
        static string DumpFsm(PlayMakerFSM f)
        {
            var d = new System.Text.StringBuilder(Recon.Path(f.transform) + "::" + f.FsmName + (f.enabled ? "" : " (COUPE)") + " etat " + f.ActiveStateName + " :");
            foreach (FsmFloat v in f.FsmVariables.FloatVariables) d.Append(' ').Append(v.Name).Append('=').Append(v.Value.ToString("F2"));
            foreach (FsmBool v in f.FsmVariables.BoolVariables) d.Append(' ').Append(v.Name).Append('=').Append(v.Value);
            foreach (FsmGameObject v in f.FsmVariables.GameObjectVariables) d.Append(' ').Append(v.Name).Append("->").Append(v.Value != null ? Recon.Path(v.Value.transform) : "null");
            foreach (FsmObject v in f.FsmVariables.ObjectVariables) d.Append(' ').Append(v.Name).Append("=>").Append(v.Value != null ? v.Value.GetType().Name + "@" + (v.Value is Component ? Recon.Path(((Component)v.Value).transform) : v.Value.name) : "null");
            foreach (FsmState st in f.FsmStates)
            {
                d.Append('\n').Append(" [").Append(st.Name).Append(']');
                foreach (FsmTransition tr in st.Transitions) d.Append(" -").Append(tr.EventName).Append("->").Append(tr.ToState);
                foreach (FsmStateAction ac in st.Actions)
                {
                    d.Append('\n').Append("    ").Append(ac.GetType().Name).Append(ac.Enabled ? "" : "(off)").Append(':');
                    foreach (System.Reflection.FieldInfo fi in ac.GetType().GetFields())
                    {
                        object v = fi.GetValue(ac); if (v == null) continue;
                        string sv;
                        if (v is FsmObject) { FsmObject o = (FsmObject)v; sv = (o.UseVariable ? o.Name + "=" : "") + (o.Value != null ? o.Value.GetType().Name + "@" + (o.Value is Component ? Recon.Path(((Component)o.Value).transform) : o.Value.name) : "null"); }
                        else if (v is NamedVariable) { NamedVariable nv = (NamedVariable)v; sv = (nv.UseVariable ? nv.Name + "=" : "") + (v is FsmFloat ? ((FsmFloat)v).Value.ToString("F2") : v is FsmBool ? ((FsmBool)v).Value.ToString() : v is FsmString ? ((FsmString)v).Value : v is FsmGameObject ? (((FsmGameObject)v).Value != null ? Recon.Path(((FsmGameObject)v).Value.transform) : "null") : v is FsmInt ? ((FsmInt)v).Value.ToString() : "?"); }
                        else if (v is FsmOwnerDefault) { FsmOwnerDefault od = (FsmOwnerDefault)v; sv = od.OwnerOption == OwnerDefaultOption.UseOwner ? "(proprio)" : (od.GameObject.Value != null ? Recon.Path(od.GameObject.Value.transform) : "null"); }
                        else if (v is FsmProperty) { FsmProperty pr = (FsmProperty)v; sv = pr.PropertyName + " de " + (pr.TargetObject != null && pr.TargetObject.Value != null ? pr.TargetObject.Value.GetType().Name + "@" + (pr.TargetObject.Value is Component ? Recon.Path(((Component)pr.TargetObject.Value).transform) : pr.TargetObject.Value.name) : "null"); }
                        else if (v is FsmEvent) sv = "ev:" + ((FsmEvent)v).Name;
                        else if (fi.FieldType.IsEnum || fi.FieldType.IsPrimitive || v is string) sv = v.ToString();
                        else continue;
                        d.Append(' ').Append(fi.Name).Append('=').Append(sv);
                    }
                }
            }
            return d.ToString();
        }

        public static void Test(string mode, float t)
        {
            if (mode == "vazcle") { TestKey(t); return; }
            if (mode != "vaz") return;
            bool host = Host;
            if (testStep == 0 && !testMoved && t > 12f && Config.GetInt("Test", "VazDehors", 0) != 0)
            {
                // (captures : dans la cour, a 7 m a droite de la Rivett, regard dans son sens)
                testMoved = true;
                GameObject c = Game.FindAny("CORRIS"), pl = GameObject.Find("PLAYER");
                if (c != null && pl != null)
                {
                    Vector3 f = c.transform.forward; f.y = 0f; f.Normalize();
                    pl.transform.position = c.transform.position + new Vector3(f.z, 0f, -f.x) * (host ? 7f : 9f) - f * 4f + Vector3.up * 1f;
                    pl.transform.rotation = Quaternion.LookRotation(f);
                    Log.Info("autotest vaz : joueur pose dans la cour en " + pl.transform.position.ToString("F1"));
                }
            }
            // ([Test] VazModele=1 : une Evo III, extension privee)
            if (host && testStep == 0 && t > 20f) { testStep = 1; Log.Info("autotest vaz : demande n 3 -> " + Request(3, Config.GetInt("Test", "VazModele", 0))); }
            if (host && testStep == 1 && t > 30f) { testStep = 2; Log.Info("autotest vaz : " + State()); Autotest.CaptureSoon("vaz-hote", 1f); }
            if (!host && testStep == 0 && t > 40f) { testStep = 2; Log.Info("autotest vaz : " + State()); Autotest.CaptureSoon("vaz-invite", 1f); }
            // [Test] VazOuvrir=1 : trappe de toit et hayon ouverts a 31 s (hayon : butees de la charniere a 60-65 degres, comme son
            // automate), capture vaz-ouvert a 35 s.
            if (host && testStep == 2 && t > 31f && !openDone && Config.GetInt("Test", "VazOuvrir", 0) != 0)
            {
                openDone = true;
                RoofToggle(Mine, true);
                Transform c = CarOf(Me);
                Transform sh = c != null ? c.Find("Hatch/Hatch") : null;
                HingeJoint hj = sh != null ? sh.GetComponent<HingeJoint>() : null;
                if (hj != null) { JointLimits l = hj.limits; l.min = 60f; l.max = 65f; hj.limits = l; hj.useLimits = true; }
                Autotest.CaptureSoon("vaz-ouvert", 4f);
                Log.Info("autotest vaz : trappe et hayon ouverts (" + (hj != null ? "charniere du hayon" : "PAS de hayon") + ")");
            }
            if (host && testStep == 2 && t > 35f && !placesDone && Config.GetInt("Test", "VazPlaces", 0) != 0) { placesDone = true; try { Places(); } catch (System.Exception e) { Log.Warn("autotest vaz : places : " + e); } }
            // [Test] VazEntree=1 : joueur pose debout sur la place du conducteur (38 s) puis du passager (43 s) ; 2 s apres, ce que
            // propose le jeu (icone du volant GUIdrive, etat de DriveTriggerX ; icone passager GUIpassenger de Seats).
            if (host && Config.GetInt("Test", "VazEntree", 0) != 0 && testStep >= 2)
            {
                // balayage de l'ouverture des portieres : 2 x 6 points de chaque cote, 1,2 s chacun (pose, puis releve)
                int k = (int)((t - 38f) / 1.2f);
                if (t > 38f && k < 24 && k != entryStep - 1)
                {
                    if (entryStep > 0) Log.Info("autotest vaz : entree, point " + (entryStep - 1) + " : " + EntryState());
                    if (entryStep == 0)
                    {
                        // (portieres ouvertes simulees : leurs colliders coupes le temps du balayage)
                        Transform cv = CarOf(Me);
                        if (cv != null) foreach (string dp in new[] { "Doors/DoorFront(leftx)", "Doors/DoorFront(right)" }) { Transform d = cv.Find(dp); if (d != null) foreach (Collider c in d.GetComponentsInChildren<Collider>(true)) c.enabled = false; }
                    }
                    entryStep = k + 1;
                    int side = k / 12, j = k % 12;
                    float x = (j < 6 ? 0.78f : 0.62f) * (side == 0 ? -1f : 1f), z = 0.7f - 0.2f * (j % 6);
                    PutPlayer(new Vector3(x, -0.05f, z));
                }
            }
            if (host && testStep == 2 && t > 50f) { testStep = 3; Call(); Log.Info("autotest vaz : appelee, " + State()); }
            if (Config.GetInt("Test", "VazGarder", 0) != 0) return;
            if (host && testStep == 3 && t > 60f) { testStep = 4; Release(); Log.Info("autotest vaz : rendue"); }
            if (testStep >= 2 && testStep < 5 && t > 70f) { testStep = 5; Log.Info("autotest vaz : apres retour : " + State()); }
        }

        // [Test] VazPlaces=1 (autotest vaz, 35 s) : carte de l'habitacle (repere de la voiture, pas 10 cm) : pour chaque point du
        // plancher, le joueur (capsule de son CharacterController, pieds au plancher) y tient-il sans toucher un collider de la
        // voiture, touche-t-il la zone d'entree du conducteur (DriveTriggerX) ? . libre, # bloque, D libre ET dans la zone.
        static void Places()
        {
            Transform car = CarOf(Me);
            GameObject pl = GameObject.Find("PLAYER");
            CharacterController cc = pl != null ? pl.GetComponent<CharacterController>() : null;
            if (car == null || cc == null) { Log.Info("autotest vaz : places : voiture ou joueur absent"); return; }
            Transform dt = car.Find("Functions/PlayerTrigger/DriveTriggerX");
            Collider dtc = dt != null ? dt.GetComponent<Collider>() : null;
            float r = cc.radius * Mathf.Max(pl.transform.lossyScale.x, pl.transform.lossyScale.z), h = cc.height * pl.transform.lossyScale.y;
            var sb = new System.Text.StringBuilder("autotest vaz : places (rayon " + r.ToString("F2") + ", hauteur " + h.ToString("F2") + ", zone " + (dtc != null ? car.InverseTransformPoint(dtc.bounds.center).ToString("F2") + " taille " + dtc.bounds.size.ToString("F2") : "?") + ")");
            var block = new System.Collections.Generic.Dictionary<string, int>();
            for (float z = 1.0f; z >= -1.0f; z -= 0.1f)
            {
                sb.Append("\n z " + z.ToString("+0.0;-0.0") + " ");
                for (float x = -1.2f; x <= 1.21f; x += 0.1f)
                {
                    Vector3 feet = car.TransformPoint(new Vector3(x, -0.05f, z));
                    Vector3 a = feet + car.up * (r + 0.02f), b = feet + car.up * Mathf.Max(r + 0.02f, h - r);
                    bool hit = false, inZone = false;
                    // (Unity 5.0 : pas d'OverlapCapsule -> spheres le long de la capsule)
                    var cols = new System.Collections.Generic.List<Collider>();
                    for (float k = 0f; k <= 1.001f; k += 0.25f) cols.AddRange(Physics.OverlapSphere(Vector3.Lerp(a, b, k), r));
                    foreach (Collider c in cols)
                    {
                        if (c == null || !c.transform.IsChildOf(car)) continue;
                        if (c == dtc) { inZone = true; continue; }
                        if (c.isTrigger) continue;
                        hit = true;
                        string n = VehicleSync.RelPath(car, c.transform); int k; block.TryGetValue(n, out k); block[n] = k + 1;
                    }
                    sb.Append(hit ? '#' : inZone ? 'D' : '.');
                }
            }
            sb.Append("\n bloquants : ");
            foreach (var kv in block) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(", ");
            Log.Info(sb.ToString());
        }

        static string State()
        {
            var sb = new System.Text.StringBuilder(Describe());
            foreach (Rent r in rents.Values)
            {
                Transform c = r.Car != null && r.Car.Car != null ? r.Car.Car.transform : null;
                if (c == null) { sb.Append(" ; n " + r.Number + " pas construite"); continue; }
                sb.Append(" ; modele " + (r.Car.M != null ? r.Car.M.Name : "?") + ", livree " + (r.Car.LiveryHash ?? "aucune"));
                Rigidbody rb = c.GetComponent<Rigidbody>(); Drivetrain dt = c.GetComponent<Drivetrain>();
                sb.Append(" ; " + c.name + " en " + c.position.ToString("F1") + ", " + (dt != null ? dt.maxPower.ToString("F0") + " ch " + dt.transmission : "?") + (rb != null ? ", " + rb.mass.ToString("F0") + " kg" : "")
                          + ", VehicleSync " + (VehicleSync.RootByKey(c.name) != null ? "connue" : "inconnue"));
            }
            return sb.ToString();
        }
    }
}
