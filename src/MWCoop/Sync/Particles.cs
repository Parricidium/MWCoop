using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Particules du monde chez les invites (retour de JD, 14/10 : « fumee du sauna, trainee de neige, fumee, terre / neige
    // soulevee par les vehicules PNJ ne sont pas visibles chez le client »). Les emetteurs (ParticleEmitter, legacy) sont
    // allumes par des automates : la poussiere (RoosterTail) et la trainee de neige (SnowCloudTrail*) du trafic par
    // Throttle / Steering / Shifting de la voiture, coupes chez l'invite (copie cinematique, Traffic.cs) ; la fumee du sauna
    // (SmokeHomeKiuas) par Simulation::Burn, active seulement chez celui dont le feu brule. Releve chez l'invite (essai
    // Autotest=particules) : rien n'emettait, chez l'hote RoosterTail, SnowCloudTrail, Exhaust du bus.
    // Ici : l'hote dit quels emetteurs sont allumes (@emit : a chaque changement, et tous toutes les 5 s) ; l'invite les pose.
    // Vehicules du trafic, PNJ et courses (copies) : repose a chaque seconde (un automate local, LOD, peut les eteindre) ;
    // le reste (poeles, cheminees) : seulement aux changements de l'hote. A part : voitures des joueurs (celui qui les mene
    // fait foi : VehicleSync, message M_FX -- fumee d'echappement et de la simulation, traces et fumee de glisse des roues de
    // la copie ; depuis le 16/10), le joueur, douches et robinets, vapeur de l'eau jetee sur le poele, SkidSmoke (fait par
    // roue chez celui qui mene : VehicleSync aussi).
    //
    // Objets allumes par les automates des copies (refonte, revue du 14/10 : feux stop du trafic, des PNJ et du bus jamais
    // allumes chez l'invite -- Throttle 'Cruise 2' : ActivateGameObject BrakeLights) : l'hote releve les cibles des actions
    // ActivateGameObject des automates de TRAFFIC / NPC_CARS / RACES / TRAIN (pas une voiture entiere : rien qui porte un
    // corps rigide) et dit leur etat, cle "a:" racine|rangs depuis la racine|nom ; l'invite retrouve l'objet par ces rangs.
    public static class Particles
    {
        class Em
        {
            public string Key; public ParticleEmitter E; public GameObject Go; public bool Last, Want, Has, Copy; public float NextTry;
            public bool On { get { return E != null ? E.emit : Go != null && Go.activeSelf; } }
        }
        static readonly Dictionary<string, Em> byKey = new Dictionary<string, Em>();
        static readonly HashSet<PlayMakerFSM> examined = new HashSet<PlayMakerFSM>();
        static float nextScan, nextSend, nextFull, nextApply;
        static readonly KeyTable table = new KeyTable("@emitk");   // (cles courtes, Nightly 5)
        public static void OnDefs(int who, NetReader r) { if (Session.IsHost || who != 0) { r.U8(); return; } table.OnDefs(r); }
        // joueur arrive : toute la table de nouveau, et tout l'etat tout de suite
        public static void ScheduleSnapshot() { table.ResendAll(); nextFull = 0f; }
        static int sentLogs, gotLogs, actLogs;

        public static void OnLevelLoaded() { byKey.Clear(); examined.Clear(); table.Clear(); nextScan = 0f; nextFull = 0f; actLogs = 0; }

        static bool Eligible(ParticleEmitter pe)
        {
            string n = pe.name;
            if (n.Contains("Shower") || n.Contains("Tap") || n.StartsWith("Steam") || n.Contains("Skid")) return false;
            Transform t = pe.transform;
            string root = t.root.name;
            if (root == "PLAYER" || root.StartsWith("MWCoop")) return false;
            if (VehicleSync.IsPlayerCar(t)) return false;   // (voiture d'un joueur : son conducteur)
            return true;
        }
        static bool IsCopyRoot(string r) { return r == "TRAFFIC" || r == "NPC_CARS" || r == "RACES" || r == "TRAIN"; }
        static bool IsCopy(Transform t) { return IsCopyRoot(t.root.name); }

        // Cle d'un objet allume : racine, puis a chaque niveau le nom et le rang parmi les freres DU MEME NOM, puis le nom
        // ("a:TRAFFIC|VehiclesDirtRoad~0/Rally~0/HEPPA~0/BrakeLights~0|BrakeLights"). D'abord le seul rang parmi tous les
        // freres : chez l'invite la HEPPA copiee n'a pas ses enfants dans le meme ordre (rang 4 = une roue au lieu des feux
        // stop, essai du 14/10) -- 1 lumiere sur 5 retrouvee.
        static string ActiveKey(Transform t) { return ChainKey("a:", t); }
        // (aussi CopySounds, prefixe "s:")
        internal static string ChainKey(string prefix, Transform t)
        {
            var segs = new List<string>();
            Transform c = t;
            for (; c.parent != null; c = c.parent)
            {
                int n = 0;
                Transform par = c.parent;
                for (int i = 0; i < par.childCount; i++) { Transform o = par.GetChild(i); if (o == c) break; if (o.name == c.name) n++; }
                segs.Add(c.name + "~" + n);
            }
            segs.Reverse();
            return prefix + c.name + "|" + string.Join("/", segs.ToArray()) + "|" + t.name;
        }
        internal static GameObject ResolveActive(string key)   // (cle ChainKey, prefixe de 2 caracteres)
        {
            string[] p = key.Substring(2).Split('|');
            if (p.Length != 3) return null;
            GameObject root = Game.FindAny(p[0]);
            if (root == null) return null;
            string[] segs = p[1].Length > 0 ? p[1].Split('/') : new string[0];
            Transform t = Follow(root.transform, segs, 0);
            if (t == null && segs.Length > 1)
            {
                // Range ailleurs ici (le bus : NPC_CARS/BUS chez l'hote, NPC_CARS/BusSpawnPerajarvi/BUS chez l'invite) : le
                // premier objet du meme nom sous la racine, puis le reste du chemin depuis lui.
                string first = SegName(segs[0]);
                foreach (Transform d in root.GetComponentsInChildren<Transform>(true))
                    if (d.name == first && d != root.transform && (t = Follow(d, segs, 1)) != null) break;
            }
            return t != null && t.name == p[2] ? t.gameObject : null;
        }
        static string SegName(string seg) { int k = seg.LastIndexOf('~'); return k < 0 ? seg : seg.Substring(0, k); }
        static Transform Follow(Transform t, string[] segs, int from)
        {
            for (int s = from; s < segs.Length; s++)
            {
                string seg = segs[s];
                int tilde = seg.LastIndexOf('~'), want;
                if (tilde < 0 || !int.TryParse(seg.Substring(tilde + 1), out want)) return null;
                string name = seg.Substring(0, tilde);
                Transform next = null;
                for (int i = 0, n = 0; i < t.childCount; i++)
                {
                    Transform o = t.GetChild(i);
                    if (o.name != name) continue;
                    if (n++ == want) { next = o; break; }
                }
                if (next == null) return null;
                t = next;
            }
            return t;
        }

        static void Scan()
        {
            foreach (ParticleEmitter pe in Object.FindObjectsOfType<ParticleEmitter>())
            {
                if (pe == null || !Eligible(pe)) continue;
                string k = Recon.Path(pe.transform) + "#" + pe.transform.GetSiblingIndex();
                Em e;
                if (!byKey.TryGetValue(k, out e)) { e = new Em { Key = k }; byKey[k] = e; e.Last = pe.emit; }
                e.E = pe; e.Copy = IsCopy(pe.transform);
            }
            if (Session.IsHost) ScanActivated();
        }

        // Seulement les lumieres d'un vehicule copie (feux stop, phares, clignotants) : sous un corps rigide (la voiture), sans
        // en porter (pas la voiture entiere), nom de lumiere. Pas les LOD, declencheurs, PNJ, radars (essai du 14/10 : sans ce
        // filtre, LOD des voitures, Markku, CrouchTrigger, radar -- objets que l'invite tient lui-meme, a distance de lui).
        static readonly string[] LightWords = { "Light", "Lamp", "Brake", "Blink", "Signal", "Indicator", "Reverse", "Turn" };
        static bool IsVehicleLight(GameObject go, PlayMakerFSM f)
        {
            Transform t = go.transform;
            if (t.parent == null || t.root != f.transform.root || go.GetComponentsInChildren<Rigidbody>(true).Length > 0) return false;
            bool word = false;
            foreach (string w in LightWords) if (go.name.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0) { word = true; break; }
            if (!word) return false;
            for (Transform c = t.parent; c != null; c = c.parent) if (c.GetComponent<Rigidbody>() != null) return true;
            return false;
        }

        // Hote : cibles des ActivateGameObject des automates des copies. Seulement les automates qui tournent (actions
        // chargees ; ceux d'une voiture eteinte au loin : quand elle s'allume) et d'un vehicule (sous un corps rigide) ; chacun
        // une seule fois, 4 ms au plus par releve (premiere version : 800 automates relus toutes les 5 s, exceptions
        // comprises, 4,8 s de jeu fige chez l'hote -- essai du 14/10).
        static void ScanActivated()
        {
            int added = 0, seen = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (Object o in Game.AllFsms())
            {
                var f = o as PlayMakerFSM;
                if (f == null || !f.enabled || f.hideFlags != HideFlags.None || examined.Contains(f) || !f.gameObject.activeInHierarchy) continue;
                if (!IsCopyRoot(f.transform.root.name)) { examined.Add(f); continue; }
                examined.Add(f);
                bool inVehicle = false;
                for (Transform c = f.transform; c != null && !inVehicle; c = c.parent) inVehicle = c.GetComponent<Rigidbody>() != null;
                if (!inVehicle) continue;
                seen++;
                FsmState[] sts = null;
                try { sts = f.FsmStates; } catch { }
                if (sts != null)
                    foreach (FsmState st in sts)
                    {
                        FsmStateAction[] acts = null;
                        try { acts = st.Actions; } catch { }
                        if (acts == null) continue;
                        foreach (FsmStateAction a in acts)
                        {
                            var ag = a as ActivateGameObject;
                            if (ag == null || ag.gameObject == null) continue;
                            GameObject go = ag.gameObject.OwnerOption == OwnerDefaultOption.UseOwner ? f.gameObject : ag.gameObject.GameObject != null ? ag.gameObject.GameObject.Value : null;
                            if (go == null || !IsVehicleLight(go, f)) continue;
                            string k = ActiveKey(go.transform);
                            if (byKey.ContainsKey(k)) continue;
                            byKey[k] = new Em { Key = k, Go = go, Copy = true, Last = go.activeSelf };
                            added++;
                            if (actLogs++ < 10) Log.Info("particules : " + Recon.Path(go.transform) + " allume par " + f.FsmName + " / " + st.Name + " (suivi)");
                        }
                    }
                if (sw.ElapsedMilliseconds > 4) break;   // (la suite au prochain releve)
            }
            if (added > 0) Log.Info("particules : " + added + " lumiere(s) de vehicule suivie(s), " + seen + " automates lus en " + sw.ElapsedMilliseconds + " ms");
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 5f; Scan(); }
            if (Session.IsHost)
            {
                if (Session.RemoteCount == 0 || now < nextSend) return;
                nextSend = now + 0.2f;
                bool full = now >= nextFull;
                if (full) nextFull = now + 5f;
                // Les changements partent toujours en fiable, meme pendant un tour complet (relecture du 15/10, 3.1 : un
                // changement tombe sur le tour complet partait en non fiable -- feu de vehicule perdu 5 s) ; le reste du
                // tour complet en non fiable, comme avant.
                var changed = new List<Em>();
                var rest = new List<Em>();
                foreach (Em e in byKey.Values)
                {
                    if (e.E == null && e.Go == null) continue;
                    bool v = e.On;
                    if (v != e.Last) changed.Add(e); else if (full) rest.Add(e);
                    e.Last = v;
                }
                foreach (Em e in changed) table.Id(e.Key);
                foreach (Em e in rest) table.Id(e.Key);
                table.SendDefs();   // (cles nouvelles d'abord, en fiable)
                SendBatch(changed, true);
                SendBatch(rest, false);
                if (changed.Count > 0 && sentLogs++ < 10) Log.Info("particules : " + changed.Count + " emetteur(s) change(s) envoye(s) (" + changed[0].Key + " " + changed[0].Last + ")");
                return;
            }
            if (now < nextApply) return;   // invite : copies reposees chaque seconde
            nextApply = now + 1f;
            foreach (Em e in byKey.Values) if (e.Has && e.Copy) Apply(e);
        }

        // Hote : etat des emetteurs 'list' (@emit, par 64), fiable ou non.
        static void SendBatch(List<Em> list, bool reliable)
        {
            for (int i = 0; i < list.Count; i += 64)
            {
                int n = Mathf.Min(64, list.Count - i);
                var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@emit").U8(n);
                for (int k = 0; k < n; k++) w.U16(table.Id(list[i + k].Key)).Bool(list[i + k].Last);
                Session.SendAll(w, reliable);
            }
        }

        // (essai Autotest=particules) lumieres suivies : nombre, retrouvees ici, voulues allumees, allumees ici
        public static string LightReport()
        {
            int n = 0, found = 0, want = 0, on = 0, same = 0;
            string miss = "";
            foreach (Em e in byKey.Values)
            {
                if (!e.Key.StartsWith("a:")) continue;
                n++;
                if (e.Go == null && !Session.IsHost) e.Go = ResolveActive(e.Key);
                if (e.Go != null) found++; else if (miss.Length < 600) miss += " " + e.Key;
                bool w = Session.IsHost ? e.Last : e.Want;
                if (w) want++;
                if (e.Go != null && e.Go.activeSelf) on++;
                if (e.Go != null && e.Go.activeSelf == w) same++;
            }
            return n + " suivies, " + found + " retrouvees, " + want + " allumees chez l'hote, " + on + " allumees ici, " + same + " pareilles" + (miss.Length > 0 ? " ; pas retrouvees :" + miss : "");
        }

        static bool Apply(Em e)
        {
            if (e.E != null) { if (e.E.emit == e.Want) return false; e.E.emit = e.Want; return true; }
            if (e.Go == null && e.Key.StartsWith("a:") && Time.realtimeSinceStartup >= e.NextTry) { e.Go = ResolveActive(e.Key); if (e.Go == null) e.NextTry = Time.realtimeSinceStartup + 10f; }
            if (e.Go == null || e.Go.activeSelf == e.Want) return false;
            e.Go.SetActive(e.Want);
            return true;
        }

        public static void OnMessage(int who, NetReader r)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                int id = r.U16(); bool v = r.Bool();
                if (Session.IsHost || who != 0) continue;
                string k = table.Key(id);
                if (k == null) continue;   // (table pas encore recue : le tout suivant)
                Em e;
                if (!byKey.TryGetValue(k, out e)) { e = new Em { Key = k, Copy = k.StartsWith("a:") }; byKey[k] = e; }
                bool changed = !e.Has || e.Want != v;
                e.Want = v; e.Has = true;
                if (changed && Apply(e) && gotLogs++ < 10) Log.Info("particules : " + k + " " + (v ? "allume" : "eteint") + " (hote)");
            }
        }
    }
}
