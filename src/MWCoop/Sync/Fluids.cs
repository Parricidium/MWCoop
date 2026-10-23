using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Liquides et usure : essence, huile, liquide de refroidissement et de frein, encrassement,
    // usure des pieces, contenu des bidons et seaux. Ce sont des variables float des automates
    // des pieces (Data : Wear, FuelLevel, Dirt, Coolant...) et de la simulation des vehicules
    // (Simulation/... : Oil, WaterLevel, BrakeFluidF...). Chaque cote envoie ce qui a change chez lui
    // (par lots, une fois par seconde), l'autre recopie la valeur. Deux garde-fous :
    //  - une voiture conduite par un autre : c'est son conducteur qui la consomme, la derive locale
    //    de ses valeurs est ignoree ;
    //  - une valeur que les deux cotes recalculent sans cesse (derivee chaque image) est dite
    //    disputee : seul l'hote l'envoie encore.
    // Fosses septiques (boulot de la GIFU) : le niveau de chaque fosse (JOBS/HouseShit*/.../ShitLevelTrigger
    // 'Level', ShitLevel) et le contenu de la citerne (GIFU/ShitTank 'Waste', Waste) ne sont tenus que par
    // l'autorite de la GIFU (son conducteur, celui qui a laisse son moteur tourner, sinon l'hote : c'est chez
    // lui que la pompe tourne au vrai regime, tuyau dans la fosse -- voir Jobs, @tuyau). Lui seul les envoie ;
    // ailleurs la derive locale (pompage refait sur la copie avec le regime recu, montee lente du niveau) n'est
    // jamais envoyee et la valeur recue l'ecrase. Jamais disputees : une seule source.
    // Reglages du moteur qui ne sont pas des variables flottantes (16/10, retour de deux joueurs : apres un changement de piece,
    // cliquetis chez l'un, frottement chez l'autre -- les automates SymptomsEngine/Valves tournent chez chacun sur ses propres
    // entrees) :
    //  - jeu aux soupapes : liste ArrayMaker 'Valves' (8 flottants) de la culasse, reglee par les vis ValveAdjustment (automates
    //    'Screw' sans BoltTightness : pas des boulons pour Parts) et lue par Simulation/Engine/Valves (> 5 +- tolerance :
    //    valve_knock, le cliquetis) -- jamais envoyee jusqu'ici ;
    //  - calage de la distribution : rotation locale Y de VINP_CrankPulley et VINP_CamshaftSprocket (Adjust/Adjust 2 de
    //    TimingData, et Misalign qui tire au sort chez chacun) lue par Combustion « Calc timing » (hors calage : Break ->
    //    Valvedamage, damage_bearing ou valve_knock au hasard).
    //  Suivis comme les autres valeurs (cle, valeur), instantane aux arrivants compris.
    public static class Fluids
    {
        static readonly HashSet<string> Names = new HashSet<string> {
            "Wear", "Condition", "Dirt", "Oil", "OilLevel", "Fluid", "FuelLevel", "Fuel", "WaterLevel", "Water",
            "Coolant", "BrakeFluidF", "BrakeFluidR", "Level" };
        // "Starter" (ProTonChiK, 18/10) : le carburant du demarreur de la GIFU (STARTERxGifu, Starter.Fuel) monte tout seul au
        // demarrage chez chacun ; pris pour un plein, il partait en ajout et doublait chez l'autre (86 -> 172 : moteur noye,
        // ralenti qui ne tient plus). Moteur simule par son conducteur : rien a suivre ici.
        static readonly HashSet<string> SkipFsm = new HashSet<string> {
            "Scale", "Explosion", "Temp", "Assembly", "Flicker", "Jumping", "Paint", "Pressure", "Starter" };
        static readonly HashSet<string> TimingPoints = new HashSet<string> { "VINP_CrankPulley", "VINP_CamshaftSprocket" };

        class Watch
        {
            public string Key; public PlayMakerFSM Fsm; public FsmFloat Var;
            public System.Collections.ArrayList List; public int Idx;   // valeur d'une liste ArrayMaker (jeu aux soupapes) : Var null
            public Transform Rot;                                        // rotation locale Y d'un objet (calage) : Var null
            public float Last, NextSend;
            public int Sends, Recvs; public float WindowStart; public bool Contested;
            public bool Septic;   // fosse ou citerne : envoyee par l'autorite de la GIFU seulement
            public float Pending, NextDelta;   // remplissage fait ici, pas encore envoye (valeur que l'on n'envoie pas)
        }

        static float Val(Watch x)
        {
            if (x.List != null)
            {
                object o = x.Idx < x.List.Count ? x.List[x.Idx] : null;
                return o is float ? (float)o : o is double ? (float)(double)o : o is int ? (int)o : 0f;
            }
            if (x.Rot != null) return x.Rot.localEulerAngles.y;
            return x.Var.Value;
        }
        static void SetVal(Watch x, float v)
        {
            if (x.List != null) { if (x.Idx < x.List.Count) x.List[x.Idx] = v; }
            else if (x.Rot != null) { Vector3 e = x.Rot.localEulerAngles; x.Rot.localEulerAngles = new Vector3(e.x, v, e.z); }
            else x.Var.Value = v;
        }
        // Valeur d'une voiture conduite par un autre. My Summer Car : les liquides et l'usure de la SATSUMA sont dans Database/*
        // (racine a part, pas sous la voiture) -- comptes comme ceux de la SATSUMA.
        static Transform satsuma;
        static bool Driven(Transform t)
        {
            if (Game.Msc && t != null && t.root.name == "Database")
            {
                if (satsuma == null) { GameObject s = Game.FindAny("SATSUMA(557kg, 248)"); satsuma = s != null ? s.transform : null; }
                if (satsuma != null) t = satsuma;
            }
            return VehicleSync.RemotelyDriven(t);
        }

        static bool Changed(Watch x, float v)
        {
            return x.Rot != null ? Mathf.Abs(Mathf.DeltaAngle(v, x.Last)) > 0.5f : Mathf.Abs(v - x.Last) > 0.005f + 0.001f * Mathf.Abs(v);
        }

        // Plein fait par un invite (retour d'un joueur, 10/10 : « l'argent part, l'essence n'arrive pas ») : le niveau
        // d'essence est recalcule des deux cotes (consommation), donc dispute -- seul l'hote l'envoie -- et la valeur
        // recue ecrasait celle versee ici. Un niveau a remplir ne monte que si on le remplit : ce qui monte ici sur une
        // valeur que l'on n'envoie pas part en AJOUT (« +cle », volume), garde ici d'ici la, ajoute chez les autres.
        static readonly HashSet<string> FillNames = new HashSet<string> {
            "FuelLevel", "Fuel", "Oil", "OilLevel", "Fluid", "WaterLevel", "Water", "Coolant", "BrakeFluidF", "BrakeFluidR" };
        static bool Fill(Watch x) { return !x.Septic && x.Var != null && FillNames.Contains(x.Var.Name); }

        static Rigidbody gifu;   // corps de la GIFU (racine de la citerne)

        static bool SepticVar(PlayMakerFSM f, FsmFloat v)
        {
            return v.Name == "Waste" && f.FsmName == "Waste" && f.gameObject.name == "ShitTank"
                   || v.Name == "ShitLevel" && f.FsmName == "Level" && f.gameObject.name == "ShitLevelTrigger";
        }

        // Qui tient les fosses et la citerne : l'autorite de la GIFU (l'hote tant qu'elle n'est pas trouvee).
        static int SepticAuthority() { return gifu != null ? VehicleSync.Authority(gifu) : 0; }
        static readonly Dictionary<string, Watch> byKey = new Dictionary<string, Watch>();
        static readonly List<Watch> watches = new List<Watch>();
        static readonly HashSet<PlayMakerFSM> seen = new HashSet<PlayMakerFSM>();
        static readonly HashSet<string> ambiguous = new HashSet<string>();
        static float nextScan = -1, nextPoll, nextWarn;
        static int fills, fillsIn;
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();

        // Hote : invite arrive en jeu -> 20 s plus tard (ses automates sont trouves), toutes les valeurs.
        public static void ScheduleSnapshot(Peer p) { snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        static void SendSnapshots(float now)
        {
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || !Session.T.Peers.Contains(p)) continue;
                NetWriter w = null;
                int n = 0;
                foreach (Watch x in watches)
                {
                    if (x.Fsm == null) continue;
                    if (w != null && w.Length + 6 + System.Text.Encoding.UTF8.GetByteCount(x.Key) > 1000) { Session.T.SendReliable(p, w.ToArray()); w = null; }
                    if (w == null) w = new NetWriter(Msg.Fluid).U8(Session.LocalId);
                    float sv = Val(x);
                    if (float.IsNaN(sv) || float.IsInfinity(sv)) continue;
                    w.Str(x.Key).F32(sv);
                    n++;
                }
                if (w != null) Session.T.SendReliable(p, w.ToArray());
                Log.Info("liquides et usure : instantane de " + n + " valeurs envoye a " + p);
            }
        }
        static int sent, applied;

        public static int Count { get { return watches.Count; } }

        public static void OnLevelLoaded()
        {
            // (My Summer Car : liquides de la SATSUMA dans Database/* 'Data' -- embrayage, qualite d'huile, bidons)
            if (Game.Msc) SkipFsm.Add("Obstacle");   // (MSC : Obstacle.WaterLevel des PNJ, passagers du bus)
            if (Game.Msc) foreach (string n in new[] { "ClutchFluid", "OilContamination", "OilGrade", "FluidGasoline", "FluidDiesel", "FluidOil", "FluidCoolant" }) Names.Add(n);
            satsuma = null;
            byKey.Clear(); watches.Clear(); seen.Clear(); ambiguous.Clear(); snapshots.Clear();
            gifu = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 14f : -1;
        }

        static void Scan()
        {
            int before = watches.Count;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || seen.Contains(f) || SkipFsm.Contains(f.FsmName)) continue;
                Transform t = f.transform;
                string root = Game.RootName(t);
                if (root == "GUI" || root == "PLAYER" || t.name.StartsWith("CapTrigger") || Game.Msc && root == "HUMANS") continue;   // (MSC : HUMANS/.../Obstacle.WaterLevel)
                // (MSC : compteur de litres des pompes, STORE/LOD/Pumps/*/Functions/Trigger*::Use.Fuel -- ce que le pompiste doit a la caisse,
                // a lui seul ; suivi comme une valeur commune, l'un payait l'essence de l'autre, ou la remise a zero d'un paiement effacait
                // le plein de l'autre en cours)
                if (Game.Msc && root == "STORE" && f.FsmName == "Use" && t.name.StartsWith("Trigger")) continue;
                seen.Add(f);
                bool later;
                string owner = KeyOf(t, out later);
                if (later) seen.Remove(f);   // bidon tenu ou range : cle au prochain releve
                if (owner == null) continue;
                foreach (FsmFloat v in f.FsmVariables.FloatVariables)
                {
                    bool septic = SepticVar(f, v);
                    // (charge des batteries, 'Data'.Charge : le chargeur du garage la remplit chez celui ou il tourne -- les autres
                    // voyaient la batterie vide ou pleine, retour d'un joueur, 09/10)
                    bool battery = v.Name == "Charge" && f.FsmName == "Data";
                    if (!Names.Contains(v.Name) && !septic && !battery) continue;
                    if (septic && f.FsmName == "Waste") gifu = f.transform.root.GetComponent<Rigidbody>();
                    Add(new Watch { Key = owner + ":" + f.FsmName + "." + v.Name, Fsm = f, Var = v, Last = v.Value, Septic = septic });
                }
                if (f.FsmName != "Data") continue;
                // Jeu aux soupapes (liste 'Valves' de la culasse) et calage (sprockets) : voir l'en-tete.
                foreach (PlayMakerArrayListProxy px in f.GetComponents<PlayMakerArrayListProxy>())
                {
                    if ((px.referenceName ?? "") != "Valves" || px._arrayList == null) continue;
                    for (int i = 0; i < px._arrayList.Count && i < 16; i++)
                    {
                        var w = new Watch { Key = owner + ":Valves[" + i + "]", Fsm = f, List = px._arrayList, Idx = i };
                        w.Last = Val(w);
                        Add(w);
                    }
                }
                if (TimingPoints.Contains(f.name))
                {
                    var w = new Watch { Key = owner + ":RotY", Fsm = f, Rot = f.transform };
                    w.Last = Val(w);
                    Add(w);
                }
            }
            if (watches.Count != before) Log.Info("liquides et usure : " + watches.Count + " valeurs suivies");
        }

        // Homonymes : l'ordre de decouverte differe d'une machine a l'autre, la cle est abandonnee.
        static void Add(Watch w)
        {
            if (ambiguous.Contains(w.Key)) return;
            Watch old;
            if (byKey.TryGetValue(w.Key, out old))
            {
                if (old.Fsm == w.Fsm) return;
                byKey.Remove(w.Key); watches.Remove(old); ambiguous.Add(w.Key);
                return;
            }
            byKey[w.Key] = w;
            watches.Add(w);
        }

        // Cle stable : ID de la piece porteuse (lui-meme ou jusqu'a 4 parents) + chemin relatif, sinon
        // chemin complet. Les objets clones sans ID ((Clone), (itemx) sans ID) sont ignores.
        // Bidons d'essence et de gazole (EQUIPMENTS/gasoline(itemx), diesel(itemx)) : pas d'ID, leur niveau
        // (FluidTrigger 'Data'.Fluid) etait ignore -- rempli chez l'un, vide chez l'autre (retour d'un joueur,
        // 08/10). Cle d'objet du monde de Props (la meme chez chacun, prise au chargement). Pas les seaux ni la
        // casserole : plusieurs copies de travail de Water, recalculees par le jeu.
        static string KeyOf(Transform t, out bool later)
        {
            later = false;
            string rel = "";
            Transform p = t;
            for (int i = 0; p != null && i < 5; i++, p = p.parent)
            {
                string id = Props.ItemId(p.gameObject);
                if (id.Length > 0) return id + (rel.Length > 0 ? "/" + rel : "");
                if (t.name == "FluidTrigger" && p.name.EndsWith("(itemx)") && p.GetComponent<Rigidbody>() != null)
                {
                    string wk = Props.WorldKeyOf(p.GetComponent<Rigidbody>());
                    if (wk == null) { later = true; return null; }
                    return wk + "/" + rel;
                }
                rel = rel.Length > 0 ? p.name + "/" + rel : p.name;
            }
            string path = Recon.Path(t);
            if (path.Contains("(Clone)") || path.Contains("(itemx)")) return null;
            return "p:" + path;
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 20f; watches.RemoveAll(x => x.Fsm == null); Scan(); }
            if (snapshots.Count > 0 && Session.IsHost) SendSnapshots(now);
            if (now < nextPoll) return;
            nextPoll = now + 1f;
            bool alone = Session.RemoteCount == 0;
            bool septicHere = SepticAuthority() == Session.LocalId;
            NetWriter w = null;
            foreach (Watch x in watches)
            {
                if (x.Fsm == null) continue;
                float v = Val(x);
                if (x.Contested && now - x.WindowStart > 30f) x.Contested = false;   // plus de conflit depuis 30 s
                if (x.Pending > 0.02f && now >= x.NextDelta && !alone)
                {
                    if (w != null && w.Length + 6 + System.Text.Encoding.UTF8.GetByteCount(x.Key) > 1000) { Session.SendAll(w, true); w = null; }
                    if (w == null) w = new NetWriter(Msg.Fluid).U8(Session.LocalId);
                    w.Str("+" + x.Key).F32(x.Pending);
                    if (++fills % 20 == 1) Log.Info("liquides et usure : " + x.Key + " rempli ici de " + x.Pending.ToString("F2") + ", envoye en ajout");
                    x.Pending = 0f; x.NextDelta = now + 0.5f;
                }
                if (float.IsNaN(v) || float.IsInfinity(v)) continue;   // (jamais envoyee : chez l'autre, moteur a valeurs folles)
                if (!Changed(x, v)) continue;
                if (alone) { x.Last = v; continue; }   // personne a prevenir : l'arrivant recevra l'instantane
                if (x.Septic)
                {
                    // Fosse, citerne : pas a nous, la derive d'ici ne part jamais (la valeur recue l'ecrase).
                    if (!septicHere) { x.Last = v; continue; }
                    if (now < x.NextSend) continue;
                }
                else if (Driven(x.Fsm.transform) || (x.Contested && !Session.IsHost) || now < x.NextSend)
                {
                    bool notOurs = Driven(x.Fsm.transform) || (x.Contested && !Session.IsHost);
                    if (notOurs && Fill(x) && v > x.Last) { x.Pending += v - x.Last; x.Last = v; continue; }   // rempli ici : en ajout
                    if (now >= x.NextSend) x.Last = v;   // derive locale ignoree
                    continue;
                }
                x.Last = v;
                x.NextSend = now + 1f;
                Tally(x, true, now);
                // Lots limites en octets (un message fiable tient dans un paquet de 1150 octets).
                if (w != null && w.Length + 6 + System.Text.Encoding.UTF8.GetByteCount(x.Key) > 1000) { Session.SendAll(w, true); w = null; }
                if (w == null) w = new NetWriter(Msg.Fluid).U8(Session.LocalId);
                w.Str(x.Key).F32(v);
            }
            if (w != null) Session.SendAll(w, true);
        }

        // Fenetre de 10 s : 3 envois et 3 receptions de la meme valeur -> elle est disputee.
        static void Tally(Watch x, bool send, float now)
        {
            if (now - x.WindowStart > 10f) { x.WindowStart = now; x.Sends = x.Recvs = 0; }
            if (send) x.Sends++; else x.Recvs++;
            if (!x.Septic && !x.Contested && x.Sends >= 3 && x.Recvs >= 3)
            {
                x.Contested = true;
                Log.Info("liquides et usure : " + x.Key + " recalculee des deux cotes, seul l'hote l'envoie");
            }
            if (send && ++sent % 200 == 1) Log.Info("liquides et usure : " + sent + " valeurs envoyees (" + x.Key + " = " + Val(x) + ")");
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            var relay = Session.IsHost ? new NetWriter(Msg.Fluid).U8(who) : null;
            float now = Time.realtimeSinceStartup;
            while (r.More)
            {
                string key = r.Str();
                float v = r.F32();
                if (float.IsNaN(v) || float.IsInfinity(v)) { if (now >= nextWarn) { nextWarn = now + 10f; Log.Warn("liquides et usure : " + key + " = " + v + " de #" + who + " ignoree"); } continue; }   // (ni appliquee ni relayee)
                if (relay != null) relay.Str(key).F32(v);
                Watch x;
                bool add = key.Length > 1 && key[0] == '+';
                if (add) key = key.Substring(1);
                if (!byKey.TryGetValue(key, out x) || x.Fsm == null)
                {
                    if (now >= nextWarn) { nextWarn = now + 10f; Log.Warn("liquides et usure : " + key + " introuvable ici"); }
                    continue;
                }
                if (add)
                {
                    // Rempli par un autre : ajoute a la valeur d'ici (pas un releve : ne compte pas comme dispute).
                    SetVal(x, Val(x) + v); x.Last += v;
                    if (++fillsIn % 20 == 1) Log.Info("liquides et usure : " + key + " rempli de " + v.ToString("F2") + " par le joueur #" + who + " -> " + Val(x).ToString("F2"));
                    continue;
                }
                // Valeur disputee (recalculee des deux cotes : usure de la courroie d'alternateur de MSC...) : seul l'hote fait foi.
                // Il n'envoyait deja plus que lui, mais il appliquait encore ce qu'un invite envoyait avant de le savoir : la
                // courroie abimee chez l'hote « revenait a son etat d'origine » (Teodor_Takahashi, 23/10 : Use.Wear = 2.23 recue de #1).
                if (x.Contested && who != 0) { Tally(x, false, now); continue; }
                // (remplissage fait ici depuis le dernier releve, pas encore parti : garde par-dessus la valeur recue)
                float here = Val(x);
                if (Fill(x) && here > x.Last && (Driven(x.Fsm.transform) || x.Contested && !Session.IsHost)) x.Pending += here - x.Last;
                SetVal(x, v + (Fill(x) ? x.Pending : 0f));
                x.Last = Val(x);
                Tally(x, false, now);
                if (++applied % 200 == 1) Log.Info("liquides et usure : " + applied + " valeurs recues (" + key + " = " + v + ", joueur #" + who + ")");
            }
            if (relay != null) Session.Broadcast(relay, true, who);
        }

        // [Test] Autotest=plein : l'hote au volant de la SORBET (15/20 s, moteur tenu a 1500 tr/min) ; l'invite verse 5 l dans
        // son reservoir de 30 a 35 s (0,25 l toutes les 0,25 s, comme le pistolet) ; chacun note le niveau a 28 et 45 s.
        static int fuelStep; static float fuelNext;
        public static void TestFill(string mode, float t)
        {
            if (mode != "plein") return;
            const string Part = "FuelTankSorbett:Data.FuelLevel";
            if (Session.IsHost)
            {
                if (t > 15f && fuelStep == 0) { fuelStep = 1; Log.Info("autotest : plein, " + VehicleSync.TestEnter("SORBET(190-200psi)", false)); }
                if (t > 20f && fuelStep == 1) { fuelStep = 2; Log.Info("autotest : plein, volant -> " + VehicleSync.TestEnter("SORBET(190-200psi)", true)); }
                VehicleSync.TestEngine(t > 21f && t < 70f ? 1500f : -1f, 0.2f);
            }
            else if (t > 30f && t < 35f && Time.realtimeSinceStartup >= fuelNext) { fuelNext = Time.realtimeSinceStartup + 0.25f; TestBump(Part, 0.25f); }
            if (t > 28f && fuelLog == 0 || t > (Session.IsHost ? 60f : 45f) && fuelLog == 1)
            {
                fuelLog++;
                foreach (Watch x in watches) if (x.Fsm != null && x.Key.Contains(Part)) Log.Info("autotest : plein, " + x.Key + " = " + Val(x).ToString("F2") + (x.Contested ? " (disputee)" : "") + (VehicleSync.RemotelyDriven(x.Fsm.transform) ? " (conduite par un autre)" : ""));
            }
        }
        static int fuelLog;

        // Essais : ajoute 3 a la premiere valeur Fluid/FuelLevel non nulle la plus proche du joueur.
        public static string TestNearest(string name)
        {
            GameObject p = Game.PlayerGo;
            Watch best = null;
            float bd = float.MaxValue;
            foreach (Watch x in watches)
            {
                if (x.Fsm == null || x.Var == null || x.Var.Name != name || x.Key.StartsWith("p:CORRIS/Simulation")) continue;
                float d = (x.Fsm.transform.position - p.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = x; }
            }
            if (best == null) return "aucune valeur " + name + " (" + watches.Count + " suivies)";
            best.Var.Value += 3f;
            return best.Key + " = " + best.Var.Value + " a " + Mathf.Sqrt(bd).ToString("F0") + " m (" + watches.Count + " suivies)";
        }

        // Essais : ajoute 'delta' a la 1re valeur suivie dont la cle contient 'part' (bidon rempli a la pompe, jeu aux soupapes).
        public static string TestBump(string part, float delta)
        {
            foreach (Watch x in watches)
                if (x.Fsm != null && x.Key.Contains(part)) { SetVal(x, Val(x) + delta); return x.Key + " = " + Val(x).ToString("F2"); }
            return "aucune valeur '" + part + "'";
        }

        // Essais : valeurs suivies dont la cle contient 'part' (cle = valeur).
        public static string TestState(string part)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Watch x in watches)
                if (x.Fsm != null && x.Key.Contains(part)) sb.Append(sb.Length > 0 ? " ; " : "").Append(x.Key).Append(" = ").Append(Val(x).ToString("F2"));
            return sb.Length > 0 ? sb.ToString() : "aucune valeur '" + part + "' (" + watches.Count + " suivies)";
        }

        // Essais (Jobs, [Test] Autotest=fosse) : fosses (par cle) et citerne suivies.
        static List<Watch> SepticWatches(string var)
        {
            var l = new List<Watch>();
            foreach (Watch x in watches) if (x.Septic && x.Fsm != null && x.Var != null && x.Var.Name == var) l.Add(x);
            l.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return l;
        }

        public static string SepticState()
        {
            var sb = new System.Text.StringBuilder("fosses (autorite #" + SepticAuthority() + ") :");
            foreach (Watch x in SepticWatches("ShitLevel"))
            {
                string[] seg = x.Key.Split('/');
                sb.Append(' ').Append(seg.Length > 1 ? seg[1] : x.Key).Append('=').Append(x.Var.Value.ToString("F2"));
            }
            List<Watch> tank = SepticWatches("Waste");
            sb.Append(" ; citerne ").Append(tank.Count > 0 ? tank[0].Var.Value.ToString("F0") + " L" : "?");
            if (tank.Count > 0)
            {
                PlayMakerFSM pump = Game.FsmOn(tank[0].Fsm.gameObject, "Pump");
                FsmBool hose = pump != null ? pump.FsmVariables.FindFsmBool("HoseInShit") : null;
                sb.Append(", HoseInShit=").Append(hose != null ? hose.Value.ToString() : "?").Append(" (pompe ").Append(pump != null ? pump.ActiveStateName : "?").Append(')');
            }
            return sb.ToString();
        }

        // Essais : change ici le niveau de la fosse 'well' (rang par cle) et la citerne, comme la pompe.
        public static string TestSeptic(int well, float dLevel, float dWaste)
        {
            List<Watch> wl = SepticWatches("ShitLevel"), tank = SepticWatches("Waste");
            string r = "";
            if (well < wl.Count && dLevel != 0f) { wl[well].Var.Value += dLevel; r += wl[well].Key + " = " + wl[well].Var.Value.ToString("F2") + " "; }
            if (tank.Count > 0 && dWaste != 0f) { tank[0].Var.Value += dWaste; r += "citerne = " + tank[0].Var.Value.ToString("F0") + " L "; }
            return (r.Length > 0 ? r : "rien (" + wl.Count + " fosses, " + tank.Count + " citerne) ") + "(autorite #" + SepticAuthority() + (SepticAuthority() == Session.LocalId ? " : envoye" : " : pas envoye") + ")";
        }
    }
}
