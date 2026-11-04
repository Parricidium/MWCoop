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
        static int septicFix = -1, septicLogs;   // (dossiers #41/#46 : derive d'une fosse ou de la citerne remise a l'autorite -- voir Update)
        static bool tightOn, battItemOn;   // (audit MSC 02/11 : serrage filtre/bougies, charge de la batterie demontee -- voir Scan)

        class Watch
        {
            public string Key; public PlayMakerFSM Fsm; public FsmFloat Var;
            public System.Collections.ArrayList List; public int Idx;   // valeur d'une liste ArrayMaker (jeu aux soupapes) : Var null
            public Transform Rot;                                        // rotation locale Y d'un objet (calage) : Var null
            public float Last, NextSend;
            public int Sends, Recvs; public float WindowStart; public bool Contested;
            public bool Septic;   // fosse ou citerne : envoyee par l'autorite de la GIFU seulement
            public float Pending, NextDelta;   // remplissage fait ici, pas encore envoye (valeur que l'on n'envoie pas)
            public float Auth; public bool HasAuth;   // derniere valeur recue de celui qui fait autorite
            public bool Gesture; public PlayMakerFSM Screw;   // serrage a la main (filtre a huile, bougies, MSC) : un geste, toujours a nous ; vis recalee a la reception
            public FsmBool Installed;    // charge d'une batterie (MSC, Use.Charge) : suivie seulement demontee
            public float GestureUntil;   // vidange faite ici (OilContamination, MSC) : fin de la fenetre ou toute baisse reste a nous
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
        static Transform CarT(Transform t)
        {
            if (Game.Msc && t != null && t.root.name == "Database")
            {
                if (satsuma == null) { GameObject s = Game.FindAny("SATSUMA(557kg, 248)"); satsuma = s != null ? s.transform : null; }
                if (satsuma != null) return satsuma;
            }
            return t;
        }
        // (audit MSC 02/11 : les regles « geste d'ici » de NotOurs -- nettoyage, plein/vidange, reglages -- demandaient
        // RemotelyDriven sur le transform Database/*, hors de la SATSUMA : toujours faux. Invite au volant, la copie de l'hote
        // consommait l'essence, l'eau, l'huile et envoyait sa derive ; les deux cotes envoyaient, valeur « disputee », puis seul
        // l'hote faisait foi : le niveau du conducteur suivait la simulation de la copie. Meme renvoi vers la SATSUMA que
        // Driven. [Test] RenvoiDatabase=0 : comme avant.)
        static int renvoiDb = -1;
        static Transform RemoteCarT(Transform t)
        {
            if (renvoiDb < 0) renvoiDb = Config.GetInt("Test", "RenvoiDatabase", 1);
            return renvoiDb != 0 ? CarT(t) : t;
        }
        static bool Driven(Transform t)
        {
            t = CarT(t);
            if (VehicleSync.RemotelyDriven(t)) return true;
            // Voiture garee (personne ne la mene) : chacun la consommait chez lui -- batterie videe par les phares, la radio, le
            // demarreur rejoue chez l'invite -- et envoyait sa valeur, qui ecrasait celle de l'autre : deux consommations pour une,
            // batterie de la SATSUMA videe deux fois plus vite (JD et GG, 27/10 : 69,2 chez l'hote, 60,8 recue de l'invite). Seul
            // celui qui fait autorite sur la voiture (l'hote tant que personne ne la mene) envoie sa derive ; les pleins faits
            // ailleurs partent en ajout, comme pour une voiture menee par un autre. ([Test] ConsoAncienne=1 : regle d'avant.)
            if (oldConso < 0) oldConso = Config.GetInt("Test", "ConsoAncienne", 0);
            return oldConso == 0 && VehicleSync.IsCar(t) && !VehicleSync.SimAuthority(t);
        }
        static int oldConso = -1;
        // (nettoyage : Dirt qui baisse sur une voiture garee, lavee ici -- garde comme un geste d'ici)
        static bool NotOurs(Watch x, float v)
        {
            // (serrage du filtre a huile et des bougies : un geste a la main, jamais une consommation -- toujours a nous)
            if (x.Gesture) return false;
            Transform car = RemoteCarT(x.Fsm.transform);
            if (x.Var != null && x.Var.Name == "Dirt" && v < x.Last && !VehicleSync.RemotelyDriven(car)) return false;
            // (audit MSC 02/11 : vidange faite par un invite sur la SATSUMA garee -- l'huile neuve versee fait baisser
            // Oilpan.OilContamination de 3,2/s, CapTrigger_MotorOil 'Pouring' -- : la baisse n'etait jamais envoyee et la valeur de
            // l'hote la remettait, huile toujours sale chez lui (usure des bougies, frottement) et dans sa sauvegarde. Une baisse
            // nette (> 0,5 par releve : le filtre, moteur en marche, n'en fait que quelques milliemes) est un geste d'ici.
            // Puis 3 s pendant lesquelles toute baisse reste un geste : le dernier releve du versement (arret en cours de seconde,
            // butee FloatClamp min 1 de Oilpan 'Data') baisse de moins de 0,5 ; il tombait dans Driven et Update remettait l'huile
            // sale recue avant le versement (relecture du 02/11). [Test] VidangePropre=0 : comme avant.)
            float vNow = Time.realtimeSinceStartup;
            if (x.Var != null && x.Var.Name == "OilContamination" && v < x.Last && (x.Last - v > 0.5f || vNow < x.GestureUntil) && !VehicleSync.RemotelyDriven(car))
            {
                if (vidangePropre < 0) vidangePropre = Config.GetInt("Test", "VidangePropre", 1);
                if (vidangePropre != 0) { if (x.Last - v > 0.5f) x.GestureUntil = vNow + 3f; return false; }
            }
            // (liquides qu'on verse ou qu'on vidange -- huile, eau, liquide de frein, essence -- sur une voiture garee : vidange faite ici,
            // un geste, comme le plein ; moteur coupe, rien ne les consomme -- regle d'avant. Retour de [MWC] 27/10 : la vidange du
            // radiateur d'une voiture garee par un invite etait annulee.)
            if (Fill(x) && !VehicleSync.RemotelyDriven(car)) return false;
            // (reglages a la main -- jeu aux soupapes, calage -- : des gestes, pas une consommation ; regle d'avant)
            if (x.List != null || x.Rot != null) return VehicleSync.RemotelyDriven(car);
            return Driven(x.Fsm.transform);
        }
        static int vidangePropre = -1;

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
            septicLogs = 0;
            // (My Summer Car : liquides de la SATSUMA dans Database/* 'Data' -- embrayage, qualite d'huile, bidons)
            if (Game.Msc) SkipFsm.Add("Obstacle");   // (MSC : Obstacle.WaterLevel des PNJ, passagers du bus)
            if (Game.Msc) foreach (string n in new[] { "ClutchFluid", "OilContamination", "OilGrade", "FluidGasoline", "FluidDiesel", "FluidOil", "FluidCoolant" }) Names.Add(n);
            // (audit MSC 02/11 : le liquide d'embrayage verse par celui qui ne fait pas autorite -- invite sur la SATSUMA garee, hote
            // quand un invite conduit -- etait remis a la valeur de l'autre, jamais envoye : maitre-cylindre vide chez l'hote,
            // Transmission 'Lining Leak' -> voiture qui ne roule plus. Un niveau qu'on remplit, comme BrakeFluidF/R : en ajout.
            // [Test] EmbrayageVerse=0 : comme avant.)
            if (Game.Msc && Config.GetInt("Test", "EmbrayageVerse", 1) != 0) FillNames.Add("ClutchFluid");
            tightOn = Game.Msc && Config.GetInt("Test", "SerrageFiltreBougies", 1) != 0;
            battItemOn = Game.Msc && Config.GetInt("Test", "ChargeBatterieDemontee", 1) != 0;
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
                    // (My Summer Car, passe Satsuma 27/10 : usure centrale CarSimulation/MechanicalWear::Data -- WearAlternator, WearPiston1..4,
                    // WearGearbox..., CarbChamberLevel -- comptee par Oil chez celui qui mene ; jamais suivie, ses noms ne sont pas « Wear »)
                    bool wearSim = Game.Msc && f.FsmName == "Data" && t.name == "MechanicalWear" && (v.Name.StartsWith("Wear") || v.Name == "CarbChamberLevel");
                    // (et l'usure des pneus de la SATSUMA, wheel_regula::Use.TireHealth -- cle = ID de la roue, wheel_steel1... ; comptee par
                    // Condition chez celui qui mene, tiree au sort a l'achat)
                    bool tire = Game.Msc && f.FsmName == "Use" && v.Name == "TireHealth";
                    // (audit MSC 02/11 : serrage du filtre a huile et des bougies, oilfilter*/sparkplug*::Use.Tightness -- la valeur
                    // sauvee, recopiee chaque seconde dans Database/PartsStatus. Leurs automates 'Screw' ne sont pas des boulons pour
                    // Parts/PartsMsc (ni BoltTightness ni PartAssembled), Jobs ecarte les « (Clone) », WorldFsms tout ce qui est sous
                    // la SATSUMA : jamais envoye. Filtre serre par l'invite, 0 chez l'hote : Oil 'Oilfilter leak' (8 - Tightness)/800
                    // vidait le carter chez l'autorite, bougies desserrees -> rates (Cylinders), et la sauvegarde de l'hote gardait 0.
                    // [Test] SerrageFiltreBougies=0 : comme avant.)
                    bool tight = tightOn && f.FsmName == "Use" && v.Name == "Tightness" && f.FsmVariables.FindFsmBool("Consumed") != null
                                 && (owner.StartsWith("oilfilter") || owner.StartsWith("sparkplug"));
                    // (audit MSC 02/11 : charge de la batterie demontee, battery*::Use.Charge -- le chargeur du garage la remplit chez
                    // celui qui l'y pose (sa main seulement : TriggerCharger 'Find battery'), les autres la gardaient vide ; remontee,
                    // 'Set data' recopiait chez chacun SA charge dans Database/PartsStatus/Battery, et la valeur de l'hote (vide)
                    // faisait foi. Montee, Use.Charge n'est qu'une copie de Database (suivie par la regle 'Data'.Charge) : pas suivie.
                    // [Test] ChargeBatterieDemontee=0 : comme avant.)
                    bool battItem = battItemOn && f.FsmName == "Use" && v.Name == "Charge" && owner.StartsWith("battery")
                                    && f.FsmVariables.FindFsmFloat("ChargeMax") != null && f.FsmVariables.FindFsmBool("Installed") != null;
                    if (!Names.Contains(v.Name) && !septic && !battery && !wearSim && !tire && !tight && !battItem) continue;
                    if (septic && f.FsmName == "Waste") gifu = f.transform.root.GetComponent<Rigidbody>();
                    Add(new Watch { Key = owner + ":" + f.FsmName + "." + v.Name, Fsm = f, Var = v, Last = v.Value, Septic = septic,
                                    Gesture = tight, Screw = tight ? Game.FsmOn(f.gameObject, "Screw") : null,
                                    Installed = battItem ? f.FsmVariables.FindFsmBool("Installed") : null });
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
                if (x.Installed != null && x.Installed.Value) { x.Last = v; continue; }   // (batterie montee : copie de Database, voir Scan)
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
                    // (dossiers #41 et #46, xZOTIK et Markos 03/11 : « la fosse n'est videe que chez le client » -- la copie de la pompe
                    // d'ici, tuyau dans la fosse ici seulement, la vidait ; l'autorite, dont le niveau ne bougeait pas, n'envoyait rien qui
                    // l'ecrase. Remise a la derniere valeur de l'autorite des qu'elle s'en ecarte. [Test] FosseDerive=1 : comme avant.)
                    if (!septicHere)
                    {
                        if (septicFix < 0) septicFix = Config.GetInt("Test", "FosseDerive", 0) == 0 ? 1 : 0;
                        if (septicFix == 1 && x.HasAuth && Mathf.Abs(v - x.Auth) > (x.Var != null && x.Var.Name == "Waste" ? 20f : 0.05f))
                        {
                            if (septicLogs++ < 10) Log.Info("liquides et usure : " + x.Key + " = " + v.ToString("F2") + " ici, remise a " + x.Auth.ToString("F2") + " (autorite #" + SepticAuthority() + ")");
                            SetVal(x, x.Auth); x.Last = Val(x); continue;
                        }
                        x.Last = v; continue;
                    }
                    x.Auth = v; x.HasAuth = true;   // (la notre fait foi : reprise telle quelle si l'autorite passe a un autre)
                    if (now < x.NextSend) continue;
                }
                else if (NotOurs(x, v) || (x.Contested && !Session.IsHost) || now < x.NextSend)
                {
                    bool notOurs = NotOurs(x, v) || (x.Contested && !Session.IsHost);
                    if (notOurs && Fill(x) && v > x.Last) { x.Pending += v - x.Last; x.Last = v; continue; }   // rempli ici : en ajout
                    // Consommation d'ici sur une valeur qui n'est pas a nous (voiture garee : autorite a l'hote ; menee par un autre) :
                    // remise a la derniere valeur recue. Avant, elle restait plus basse ici jusqu'au prochain changement chez l'autre
                    // (batterie a 110,6 chez l'invite, 119,2 chez l'hote, essai 27/10). Pas un remplissage en cours d'envoi.
                    if (notOurs && x.HasAuth && !x.Septic && x.Var != null && x.Pending <= 0.02f && !Fill(x) && Mathf.Abs(v - x.Auth) > 0.005f + 0.001f * Mathf.Abs(v))
                    { SetVal(x, x.Auth); x.Last = Val(x); continue; }
                    if (now >= x.NextSend) x.Last = v;   // derive locale ignoree
                    continue;
                }
                x.Last = v;
                x.NextSend = now + 1f;
                // (vidange d'ici envoyee : c'est desormais la valeur qui fait foi chez l'hote, qui ne la renvoie pas. Sans ca, le
                // premier releve « pas a nous » apres la fenetre remettait l'huile sale recue avant le versement.)
                if (x.HasAuth && now < x.GestureUntil) x.Auth = v;
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
                x.Last = Val(x); x.Auth = x.Last; x.HasAuth = true;
                if (x.Screw != null) ScrewSync(x);
                Tally(x, false, now);
                if (++applied % 200 == 1) Log.Info("liquides et usure : " + applied + " valeurs recues (" + key + " = " + v + ", joueur #" + who + ")");
            }
            if (relay != null) Session.Broadcast(relay, true, who);
        }

        // Serrage recu (filtre a huile, bougies) : l'automate 'Screw' garde son cran (Stage) et la position de la piece ; sans
        // recalage, le prochain cran d'ici repartait de la valeur recue avec une piece visuellement desserree. Pas au milieu d'un
        // cran d'ici (Wait..., AddFsmFloat deja joue), ni automate coupe (piece demontee : 'Setup' relira la valeur au montage).
        //  - bougies : 'Save data' relit Use.Tightness dans Stage, 'Stages 2' tourne la bougie, puis 'Idle' ;
        //  - filtre : 'Save data' finirait dans 'Get scroll', qui lit la molette d'ici a son entree -- un joueur qui serre un boulon
        //    ailleurs a cet instant donnerait un cran au filtre. Cran et position poses directement, comme ses etats '1'..'8'
        //    (SetPosition Self x=0 y=0 z=-0,0025 par cran) ; TIGHTEN n'y est pas global, pas de rejeu par evenement.
        static void ScrewSync(Watch x)
        {
            PlayMakerFSM s = x.Screw;
            if (s == null || !s.enabled) return;
            FsmBool inst = x.Fsm.FsmVariables.FindFsmBool("Installed");   // (piece libre : sa position locale serait celle du monde)
            if (inst == null || !inst.Value) return;
            FsmInt stage = s.FsmVariables.FindFsmInt("Stage");
            int want = Mathf.Clamp(Mathf.RoundToInt(x.Var.Value), 0, 8);
            if (stage == null || stage.Value == want) return;
            string st = s.ActiveStateName ?? "";
            if (st == "Wait" || st == "Wait 2" || st == "Wait 3" || st == "Wait 4") return;
            if (s.Fsm.GetState("Get scroll") != null) { stage.Value = want; s.transform.localPosition = new Vector3(0f, 0f, -0.0025f * want); }
            else Game.SetState(s, "Save data");
        }

        // Essais (audit MSC 02/11, filtre a huile et bougies) : serrage suivi (Use.Tightness) et cran de la vis (Screw.Stage, etat).
        public static string TestScrew(string part)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Watch x in watches)
            {
                if (x.Fsm == null || !x.Gesture || !x.Key.Contains(part)) continue;
                FsmInt stage = x.Screw != null ? x.Screw.FsmVariables.FindFsmInt("Stage") : null;
                sb.Append(sb.Length > 0 ? " ; " : "").Append(x.Key).Append(" = ").Append(x.Var.Value.ToString("F0"))
                  .Append(" (Stage=").Append(stage != null ? stage.Value.ToString() : "?")
                  .Append(", ").Append(x.Screw != null ? (x.Screw.enabled ? x.Screw.ActiveStateName : "coupe") : "sans Screw").Append(')');
            }
            return sb.Length > 0 ? sb.ToString() : "aucun serrage '" + part + "' (" + watches.Count + " suivies)";
        }

        // Essais : un cran de molette sur la 1re piece serree dont la cle contient 'part' (etat 'Screw' du filtre, 'Screw 2' des
        // bougies : Stage +1 puis AddFsmFloat Use.Tightness +1, comme le jeu).
        public static string TestNotch(string part)
        {
            foreach (Watch x in watches)
            {
                if (x.Fsm == null || !x.Gesture || x.Screw == null || !x.Key.Contains(part)) continue;
                if (!x.Screw.enabled) return x.Key + " : automate Screw coupe (piece demontee ?)";
                string st = x.Screw.Fsm.GetState("Screw") != null ? "Screw" : "Screw 2";
                Game.SetState(x.Screw, st);
                return x.Key + " : cran (" + st + "), Tightness avant = " + x.Var.Value.ToString("F0");
            }
            return "aucun serrage '" + part + "'";
        }

        // [Test] Autotest=plein : l'hote au volant de la SORBET (15/20 s, moteur tenu a 1500 tr/min) ; l'invite verse 5 l dans
        // son reservoir de 30 a 35 s (0,25 l toutes les 0,25 s, comme le pistolet) ; chacun note le niveau a 28 et 45 s.
        // (audit MSC 02/11 : aussi sur My Summer Car -- SATSUMA, reservoir Database/DatabaseMechanics/FuelTank ; [Test] PleinVoiture,
        // PleinCle, PleinRegime pour changer de voiture, de valeur, de regime ; PleinInviteVolant=1 : l'invite conduit, l'hote verse.)
        // [Test] Autotest=carburant (dossier #43, Matilda Prime 03/11 : « le reservoir se vide parfois alors qu'il avait ete rempli », en
        // entrant dans la partie). CarburantEtape=1 (l'hote, seul ou non) : a 30 s chaque FuelLevel suivi passe a 12,34, a 38 s
        // sauvegarde en jeu. CarburantEtape=2 (partie relancee, invite lance en retard -- essai -Delai) : l'hote note tous les FuelLevel
        // a 25 s, puis chacun 5 s et 20 s apres l'arrivee de l'autre.
        static int cbStep; static float cbAt;
        static void TestFuelReentry(float t)
        {
            int etape = Config.GetInt("Test", "CarburantEtape", 1);
            bool host = Session.IsHost || !Session.Active;
            if (etape == 1)
            {
                if (host && cbStep == 0 && t > 30f)
                {
                    cbStep = 1; int n = 0;
                    foreach (Watch x in watches) if (x.Fsm != null && x.Var != null && x.Var.Name == "FuelLevel") { SetVal(x, 12.34f); n++; }
                    Log.Info("autotest : carburant, " + n + " reservoir(s) mis a 12.34 : " + FuelState());
                }
                // ([Test] CarburantSauve=jeu, en solo : la sauvegarde du jeu lui-meme -- SAVEGAME, comme aux toilettes, retour au menu)
                if (host && cbStep == 1 && t > 38f)
                {
                    cbStep = 2;
                    bool jeu = Config.Get("Test", "CarburantSauve", "") == "jeu";
                    Log.Info("autotest : carburant, " + (jeu ? "sauvegarde du jeu (SAVEGAME)" : "sauvegarde en jeu") + " : " + FuelState());
                    if (jeu) PlayMakerFSM.BroadcastEvent("SAVEGAME"); else Game.SaveInPlace();
                }
                return;
            }
            if (host && cbStep == 0 && t > 25f) { cbStep = 1; Log.Info("autotest : carburant, au chargement : " + FuelState()); }
            if (cbStep <= 1 && Jobs.OtherInGame(5f)) { cbStep = 2; cbAt = Time.realtimeSinceStartup; Log.Info("autotest : carburant, 5 s apres l'arrivee de l'autre : " + FuelState()); }
            float[] at = { 15f, 30f, 45f };
            if (cbStep >= 2 && cbStep - 2 < at.Length && Time.realtimeSinceStartup - cbAt > at[cbStep - 2]) { Log.Info("autotest : carburant, " + (at[cbStep - 2] + 5f).ToString("F0") + " s apres l'arrivee de l'autre : " + FuelState()); cbStep++; }
        }
        static string FuelState()
        {
            var l = new List<string>();
            foreach (Watch x in watches) if (x.Fsm != null && x.Var != null && x.Var.Name == "FuelLevel") l.Add(x.Key.Replace(":Data.FuelLevel", "").Replace("p:", "") + "=" + Val(x).ToString("F2"));
            l.Sort(string.CompareOrdinal);
            return l.Count + " : " + string.Join(" ", l.ToArray());
        }

        static int fuelStep; static float fuelNext;
        public static void TestFill(string mode, float t)
        {
            if (mode == "carburant") { TestFuelReentry(t); return; }
            if (mode == "vidange") { TestDrain(t); return; }
            if (mode != "plein") return;
            string car = Config.Get("Test", "PleinVoiture", Game.Msc ? "SATSUMA(557kg, 248)" : "SORBET(190-200psi)");
            string Part = Config.Get("Test", "PleinCle", Game.Msc ? "FuelTank:Data.FuelLevel" : "FuelTankSorbett:Data.FuelLevel");
            bool driver = Config.GetInt("Test", "PleinInviteVolant", 0) != 0 ? !Session.IsHost : Session.IsHost;
            if (driver)
            {
                if (t > 15f && fuelStep == 0) { fuelStep = 1; Log.Info("autotest : plein, " + VehicleSync.TestEnter(car, false)); }
                if (t > 20f && fuelStep == 1) { fuelStep = 2; Log.Info("autotest : plein, volant -> " + VehicleSync.TestEnter(car, true)); }
                VehicleSync.TestEngine(t > 21f && t < 70f ? Config.GetFloat("Test", "PleinRegime", 1500f) : -1f, 0.2f);
            }
            else if (t > 30f && t < 35f && Time.realtimeSinceStartup >= fuelNext) { fuelNext = Time.realtimeSinceStartup + 0.25f; TestBump(Part, 0.25f); }
            if (t > 28f && fuelLog == 0 || t > (driver ? 60f : 45f) && fuelLog == 1)
            {
                fuelLog++;
                foreach (Watch x in watches) if (x.Fsm != null && x.Key.Contains(Part)) Log.Info("autotest : plein, " + x.Key + " = " + Val(x).ToString("F2") + (x.Contested ? " (disputee)" : "") + (VehicleSync.RemotelyDriven(RemoteCarT(x.Fsm.transform)) ? " (conduite par un autre)" : ""));
            }
        }
        static int fuelLog;

        // [Test] Autotest=vidange (audit MSC 02/11, relecture) : l'invite verse de l'huile neuve dans la SATSUMA garee -- baisse de
        // OilContamination de 3,2/s comme CapTrigger_MotorOil 'Pouring', butee a 1 comme FloatClamp de Oilpan 'Data' -- de 40 s
        // a 40 + VidangeDuree s (6,1 par defaut : arret en cours de releve) ; chacun note la valeur 5 s apres l'arret puis 15 s apres.
        static int drainLog;
        static void TestDrain(float t)
        {
            string part = Config.Get("Test", "VidangeCle", "Oilpan:Data.OilContamination");
            float end = 40f + Config.GetFloat("Test", "VidangeDuree", 6.1f);
            if (!Session.IsHost && t > 40f && t < end)
                foreach (Watch x in watches)
                    if (x.Fsm != null && x.Var != null && x.Key.Contains(part)) { SetVal(x, Mathf.Max(1f, Val(x) - 3.2f * Time.deltaTime)); break; }
            if (t > 38f && drainLog == 0 || t > end + 5f && drainLog == 1 || t > end + 15f && drainLog == 2)
            {
                drainLog++;
                Log.Info("autotest : vidange " + (drainLog == 1 ? "avant, " : "") + TestState(part));
            }
        }

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

        // Journal de la fosse (Jobs.WellJournal, dossier #49) : pompe, regime, niveau, citerne, chez qui.
        public static string PumpJournal(GameObject well)
        {
            List<Watch> tank = SepticWatches("Waste");
            PlayMakerFSM pump = tank.Count > 0 ? Game.FsmOn(tank[0].Fsm.gameObject, "Pump") : null, lv = well != null ? Game.FsmOn(well, "Level") : null;
            System.Func<string, string> b = n => { FsmBool v = pump != null ? pump.FsmVariables.FindFsmBool(n) : null; return v != null ? (v.Value ? "1" : "0") : "?"; };
            FsmFloat ls = pump != null ? pump.FsmVariables.FindFsmFloat("LitresPerSecond") : null, lvl = lv != null ? lv.FsmVariables.FindFsmFloat("ShitLevel") : null;
            return "pompe " + (pump != null ? pump.ActiveStateName : "?") + " (marche " + b("PumpRunning") + ", fosse " + b("HoseInShit") + ", raccorde " + b("HoseAttached") + ", aspire " + b("Sucking") + ", " + (ls != null ? ls.Value.ToString("F1") : "?") + " L/s)"
                + ", regime " + (gifu != null ? VehicleSync.EngineRpm(gifu.transform).ToString("F0") : "?") + ", niveau " + (lvl != null ? lvl.Value.ToString("F2") : "?") + ", citerne " + (tank.Count > 0 ? tank[0].Var.Value.ToString("F0") + " L" : "?") + ", autorite #" + SepticAuthority();
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
