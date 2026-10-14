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
    // le reste (poeles, cheminees) : seulement aux changements de l'hote. A part : voitures des joueurs (leur conducteur
    // fait foi, VehicleSync), le joueur, douches et robinets, vapeur de l'eau jetee sur le poele, traces de pneus.
    //
    // Objets allumes par les automates des copies (refonte, revue du 14/10 : feux stop du trafic, des PNJ et du bus jamais
    // allumes chez l'invite -- Throttle 'Cruise 2' : ActivateGameObject BrakeLights) : l'hote releve les cibles des actions
    // ActivateGameObject des automates de TRAFFIC / NPC_CARS / RACES / TRAIN (pas une voiture entiere : rien qui porte un
    // corps rigide) et dit leur etat, cle "a:" racine|rangs depuis la racine|nom ; l'invite retrouve l'objet par ces rangs.
    public static class Particles
    {
        class Em
        {
            public string Key; public ParticleEmitter E; public GameObject Go; public bool Last, Want, Has, Copy;
            public bool On { get { return E != null ? E.emit : Go != null && Go.activeSelf; } }
        }
        static readonly Dictionary<string, Em> byKey = new Dictionary<string, Em>();
        static readonly HashSet<PlayMakerFSM> examined = new HashSet<PlayMakerFSM>();
        static float nextScan, nextSend, nextFull, nextApply;
        static int sentLogs, gotLogs, actLogs, diagLogs;

        public static void OnLevelLoaded() { byKey.Clear(); examined.Clear(); nextScan = 0f; nextFull = 0f; actLogs = 0; }

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

        // Cle d'un objet allume : racine, rangs parmi les freres depuis la racine, nom ("a:TRAFFIC|0.3.1|BrakeLights").
        static string ActiveKey(Transform t)
        {
            var idx = new List<int>();
            Transform c = t;
            for (; c.parent != null; c = c.parent) idx.Add(c.GetSiblingIndex());
            idx.Reverse();
            var sb = new System.Text.StringBuilder("a:").Append(c.name).Append('|');
            for (int i = 0; i < idx.Count; i++) { if (i > 0) sb.Append('.'); sb.Append(idx[i]); }
            return sb.Append('|').Append(t.name).ToString();
        }
        static GameObject ResolveActive(string key)
        {
            string[] p = key.Substring(2).Split('|');
            if (p.Length != 3) return null;
            GameObject root = Game.FindAny(p[0]);
            if (root == null) return null;
            Transform t = root.transform;
            if (p[1].Length > 0)
                foreach (string s in p[1].Split('.'))
                {
                    int i;
                    if (!int.TryParse(s, out i) || i < 0 || i >= t.childCount) return null;
                    t = t.GetChild(i);
                }
            return t.name == p[2] ? t.gameObject : null;
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

        // Hote : cibles des ActivateGameObject des automates des copies (une fois par automate, actions chargees)
        static void ScanActivated()
        {
            int added = 0, nf = 0, na = 0, nag = 0, nnull = 0, nrej = 0;
            foreach (Object o in Game.AllFsms())
            {
                var f = o as PlayMakerFSM;
                if (f == null || examined.Contains(f) || f.hideFlags != HideFlags.None || !IsCopyRoot(f.transform.root.name)) continue;
                nf++;
                FsmState[] sts;
                try { sts = f.FsmStates; } catch { continue; }
                bool loaded = false;
                foreach (FsmState st in sts)
                {
                    FsmStateAction[] acts;
                    try { acts = st.Actions; } catch { continue; }
                    if (acts == null) continue;
                    loaded = true;
                    foreach (FsmStateAction a in acts)
                    {
                        na++;
                        if (a != null && a.GetType().Name == "ActivateGameObject") nag++;
                        var ag = a as ActivateGameObject;
                        if (ag == null || ag.gameObject == null) continue;
                        GameObject go = ag.gameObject.OwnerOption == OwnerDefaultOption.UseOwner ? f.gameObject : ag.gameObject.GameObject != null ? ag.gameObject.GameObject.Value : null;
                        if (go == null) { nnull++; continue; }
                        if (!IsVehicleLight(go, f)) { nrej++; continue; }
                        string k = ActiveKey(go.transform);
                        if (byKey.ContainsKey(k)) continue;
                        byKey[k] = new Em { Key = k, Go = go, Copy = true, Last = go.activeSelf };
                        added++;
                        if (actLogs++ < 10) Log.Info("particules : " + Recon.Path(go.transform) + " allume par " + f.FsmName + " / " + st.Name + " (suivi)");
                    }
                }
                if (loaded) examined.Add(f);
            }
            if (added > 0) Log.Info("particules : " + added + " objet(s) allume(s) par les automates des copies suivi(s)");
            if (nf > 0 && diagLogs++ < 3) Log.Info("particules : releve des copies, " + nf + " automates, " + na + " actions, " + nag + " ActivateGameObject, " + nnull + " sans cible, " + nrej + " ecartes, " + added + " suivis");
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
                var batch = new List<Em>();
                foreach (Em e in byKey.Values)
                {
                    if (e.E == null && e.Go == null) continue;
                    bool v = e.On;
                    if (full || v != e.Last) batch.Add(e);
                    e.Last = v;
                }
                for (int i = 0; i < batch.Count; i += 16)
                {
                    int n = Mathf.Min(16, batch.Count - i);
                    var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@emit").U8(n);
                    for (int k = 0; k < n; k++) w.Str(batch[i + k].Key).Bool(batch[i + k].Last);
                    Session.SendAll(w, !full);
                }
                if (!full && batch.Count > 0 && sentLogs++ < 10) Log.Info("particules : " + batch.Count + " emetteur(s) change(s) envoye(s) (" + batch[0].Key + " " + batch[0].Last + ")");
                return;
            }
            if (now < nextApply) return;   // invite : copies reposees chaque seconde
            nextApply = now + 1f;
            foreach (Em e in byKey.Values) if (e.Has && e.Copy) Apply(e);
        }

        // (essai Autotest=particules) lumieres suivies : nombre, retrouvees ici, voulues allumees, allumees ici
        public static string LightReport()
        {
            int n = 0, found = 0, want = 0, on = 0, same = 0;
            foreach (Em e in byKey.Values)
            {
                if (!e.Key.StartsWith("a:")) continue;
                n++;
                if (e.Go == null && !Session.IsHost) e.Go = ResolveActive(e.Key);
                if (e.Go != null) found++;
                bool w = Session.IsHost ? e.Last : e.Want;
                if (w) want++;
                if (e.Go != null && e.Go.activeSelf) on++;
                if (e.Go != null && e.Go.activeSelf == w) same++;
            }
            return n + " suivies, " + found + " retrouvees, " + want + " allumees chez l'hote, " + on + " allumees ici, " + same + " pareilles";
        }

        static bool Apply(Em e)
        {
            if (e.E != null) { if (e.E.emit == e.Want) return false; e.E.emit = e.Want; return true; }
            if (e.Go == null && e.Key.StartsWith("a:")) e.Go = ResolveActive(e.Key);
            if (e.Go == null || e.Go.activeSelf == e.Want) return false;
            e.Go.SetActive(e.Want);
            return true;
        }

        public static void OnMessage(int who, NetReader r)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                string k = r.Str(); bool v = r.Bool();
                if (Session.IsHost || who != 0) continue;
                Em e;
                if (!byKey.TryGetValue(k, out e)) { e = new Em { Key = k, Copy = k.StartsWith("a:") }; byKey[k] = e; }
                bool changed = !e.Has || e.Want != v;
                e.Want = v; e.Has = true;
                if (changed && Apply(e) && gotLogs++ < 10) Log.Info("particules : " + k + " " + (v ? "allume" : "eteint") + " (hote)");
            }
        }
    }
}
