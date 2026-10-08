using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Givre et buee des vitres des voitures (CORRIS, SORBET, taxi MACHTWAGEN sous JOBS...).
    //  - Glace : l'automate 'Freezing' de <voiture>/Simulation/CarTemp* garde un seuil par vitre
    //    (CutoffDoorleft, CutoffDoorright, CutoffRear, CutoffSideLeft, CutoffSideRight, CutoffWindshield ;
    //    0 : prise dans la glace, 1 : degagee) que son etat Update pose sur le _Cutoff des materiaux {1}..{6}
    //    (corris_/daily_/taxi_frozen_doorleft... dans cet ordre). Au demarrage de l'automate ('Check roof'),
    //    une voiture dehors est gelee. Le grattoir (automates 'Scrape' des vitres : bouton gauche TENU et
    //    mouvement de souris, que ni le monde ni l'audit ne voient) envoie a chaque passe l'evenement de la
    //    vitre (WINDSHIELD, REAR...) a 'Freezing', qui ajoute ScrapeEfficiency (0,005) au seuil. Rien d'autre
    //    ne le fait baisser en cours de partie (FREEZE n'est envoye par personne dans le jeu).
    //    Celui qui gratte envoie le seuil des vitres qui ont bouge (4 fois/s au plus par vitre, le reste au
    //    plus tard 1 s apres). Ailleurs on garde le plus grand : deux grattoirs a la fois ne se reprennent pas
    //    leurs passes ; et un seuil recu plus bas que le sien fait renvoyer le sien (voiture regelee chez un
    //    seul joueur par un redemarrage de son automate : elle se recale). Les sauts (gel au chargement) ne
    //    partent pas. L'arrivant recoit de l'hote l'etat complet, applique tel quel (son chargement a pu geler
    //    une voiture que l'hote a grattee, ou degager une voiture rangee depuis sous un toit).
    //  - Buee : 'GlassFrosting' (Frost 0..1, alpha du materiau FrostGlass) monte et descend selon la
    //    temperature de l'habitacle et la presence du joueur LOCAL dedans (PlayerIn). Celui qui fait autorite
    //    sur la voiture (conducteur, sinon l'hote) envoie sa valeur toutes les 2 s si elle a bouge. Un joueur
    //    assis dans la voiture garde la sienne, sauf si l'envoyeur y est aussi (conducteur et passager).
    //  Les automates 'Freezing', 'GlassFrosting' et 'Scrape' sont reserves a ce module : le monde ne rejoue pas
    //  les passes de grattoir du taxi (sous JOBS), qui s'ajouteraient aux seuils envoyes ici.
    public static class Frost
    {
        const string Module = "givre";
        // Vitres de 'Freezing', dans l'ordre de ses materiaux {1}..{6}, et l'evenement de leur grattoir.
        static readonly string[] Cutoffs = { "CutoffDoorleft", "CutoffDoorright", "CutoffRear", "CutoffSideLeft", "CutoffSideRight", "CutoffWindshield" };
        static readonly string[] Events = { "DOORLEFT", "DOORRIGHT", "REAR", "SIDELEFT", "SIDERIGHT", "WINDSHIELD" };
        const int N = 6, FOG = 1 << 6, INSIDE = 1 << 7;   // Msg.Frost : bits 0-5 seuils, 6 buee, 7 envoyeur dans la voiture
        const int LIVE = 0, SNAP = 1;                     // grattage / buee ; etat complet de l'hote pour un arrivant

        class Car
        {
            public string Key, Name;                     // chemin de l'objet CarTemp* ; voiture (journal)
            public int Index = -1;                       // voiture de VehicleSync (-1 : taxi sous JOBS... : l'hote fait autorite)
            public PlayMakerFSM Freezing, Frosting;
            public readonly FsmFloat[] Cut = new FsmFloat[N];
            public readonly FsmMaterial[] Mat = new FsmMaterial[N];
            public readonly GameObject[] Glass = new GameObject[N];   // vitre (objet de son automate 'Scrape')
            public readonly float[] Last = new float[N];               // dernier seuil envoye ou applique ici
            public readonly float[] SentAt = new float[N];
            public readonly bool[] Fix = new bool[N];                  // un plus petit recu : renvoyer le notre
            public FsmFloat Frost; public FsmBool PlayerIn; public float FrostLast;
        }

        static readonly List<Car> cars = new List<Car>();
        static readonly Dictionary<string, Car> byKey = new Dictionary<string, Car>();
        static readonly HashSet<string> ambiguous = new HashSet<string>();
        static readonly Dictionary<int, int> levels = new Dictionary<int, int>();   // hote : niveau vu de chaque joueur
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static readonly float[] vals = new float[N + 1];
        static float scanAt = -1, quietUntil, nextPoll, nextFog, nextArrivals, nextWarn;
        static int scans, sent, applied;

        static Frost()
        {
            Session.PlayerLeft += p => levels.Remove(p.Id);
        }

        public static void OnLevelLoaded()
        {
            cars.Clear(); byKey.Clear(); ambiguous.Clear(); levels.Clear(); snapshots.Clear();
            scans = 0;
            testStrokes = 0; testNext = testLog = 0; testWarned = false;
            float now = Time.realtimeSinceStartup;
            // Avant Jobs (10 s) et le monde (16 s) : les automates reserves ici ne sont pas pris par eux.
            scanAt = PlayerSync.InGame ? now + 8f : -1;
            // Le gel du chargement ('Check roof', apres un delai) ne part pas : rien n'est envoye avant 30 s.
            quietUntil = now + 30f;
        }

        public static void Update()
        {
            if (!Session.Active || scanAt < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= scanAt) { Scan(); scanAt = ++scans < 2 ? now + 32f : float.MaxValue; }   // 2e releve : objets actives entre-temps
            if (cars.Count == 0) return;
            if (Session.IsHost) Arrivals(now);
            if (now >= nextPoll) { nextPoll = now + 0.2f; PollIce(now); }
            if (now >= nextFog) { nextFog = now + 2f; PollFog(); }
        }

        // ---------------------------------------------------------------- releve
        static void Scan()
        {
            int before = cars.Count;
            var scrapes = new List<PlayMakerFSM>();
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                string fn = f.FsmName;
                if (fn != "Freezing" && fn != "GlassFrosting" && fn != "Scrape") continue;
                if (f.hideFlags != HideFlags.None || !f.transform.root.gameObject.activeInHierarchy) continue;   // modeles (prefabs)
                FsmVariables v = f.FsmVariables;
                if (fn == "Scrape")
                {
                    if (v.FindFsmString("EventName") != null && v.FindFsmGameObject("Windows") != null) scrapes.Add(f);
                    continue;
                }
                bool freezing = fn == "Freezing";
                if (freezing ? v.FindFsmFloat("CutoffWindshield") == null : v.FindFsmFloat("Frost") == null) continue;
                string key = Recon.Path(f.transform);
                if (ambiguous.Contains(key)) continue;
                Car c;
                if (!byKey.TryGetValue(key, out c))
                {
                    c = new Car { Key = key };
                    int s = key.IndexOf("/Simulation/");
                    c.Name = s > 0 ? key.Substring(0, s) : key;
                    c.Index = VehicleSync.CarIndex(f.transform.root.GetComponent<Rigidbody>());
                    byKey[key] = c;
                    cars.Add(c);
                }
                PlayMakerFSM had = freezing ? c.Freezing : c.Frosting;
                if (had == f) continue;
                if (had != null)
                {
                    // Deux objets au meme chemin : on ne saurait pas lequel l'autre designe.
                    ambiguous.Add(key); byKey.Remove(key); cars.Remove(c);
                    Log.Warn("givre : " + key + " en double, ignore");
                    continue;
                }
                if (!Replay.Claim(f, Module)) { Log.Warn("givre : " + key + "::" + fn + " deja suivi par " + Replay.Owner(f)); continue; }
                if (freezing)
                {
                    c.Freezing = f;
                    for (int i = 0; i < N; i++)
                    {
                        c.Cut[i] = v.FindFsmFloat(Cutoffs[i]);
                        c.Mat[i] = v.FindFsmMaterial((i + 1).ToString());
                        c.Last[i] = c.Cut[i] != null ? c.Cut[i].Value : 0f;
                    }
                }
                else
                {
                    c.Frosting = f;
                    c.Frost = v.FindFsmFloat("Frost");
                    c.PlayerIn = v.FindFsmBool("PlayerIn");
                    c.FrostLast = c.Frost.Value;
                }
            }
            // Grattoirs : reserves ici, et la vitre de chaque evenement (le son d'une passe y est joue).
            int claimed = 0;
            foreach (PlayMakerFSM f in scrapes)
            {
                if (Replay.Claim(f, Module)) claimed++;
                GameObject win = f.FsmVariables.FindFsmGameObject("Windows").Value;
                Car c;
                if (win == null || !byKey.TryGetValue(Recon.Path(win.transform), out c)) continue;
                int i = System.Array.IndexOf(Events, f.FsmVariables.FindFsmString("EventName").Value);
                if (i >= 0 && c.Glass[i] == null) c.Glass[i] = f.gameObject;
            }
            if (cars.Count == before) return;
            var sb = new System.Text.StringBuilder("givre : " + cars.Count + " voitures suivies (" + claimed + " grattoirs) :");
            foreach (Car c in cars)
                sb.Append(' ').Append(c.Name).Append(" [pare-brise ").Append(c.Cut[5] != null ? c.Cut[5].Value.ToString("F2") : "-")
                  .Append(", buee ").Append(c.Frost != null ? c.Frost.Value.ToString("F2") : "-").Append(']');
            Log.Info(sb.ToString());
        }

        // ---------------------------------------------------------------- envoi
        // Seuils changes ici (grattoir local), 5 fois/s.
        static void PollIce(float now)
        {
            bool alone = Session.RemoteCount == 0, quiet = now < quietUntil;
            NetWriter w = null;
            for (int k = 0; k < cars.Count; k++)
            {
                Car c = cars[k];
                if (c.Freezing == null) continue;
                int mask = 0;
                for (int i = 0; i < N; i++)
                {
                    FsmFloat v = c.Cut[i];
                    if (v == null) continue;
                    float x = v.Value, d = x - c.Last[i];
                    if (!c.Fix[i] && d < 0.0005f && d > -0.0005f) continue;
                    // Personne a prevenir (l'arrivant aura l'etat complet), chargement, saut (gel de 'Check roof') : pas un grattage.
                    if (alone || quiet || d > 0.2f || d < -0.2f) { c.Last[i] = x; c.Fix[i] = false; continue; }
                    if (now - c.SentAt[i] < 0.25f) continue;
                    if (!c.Fix[i] && d < 0.01f && d > -0.01f && now - c.SentAt[i] < 1f) continue;   // une passe isolee : groupee
                    c.Fix[i] = false;
                    c.Last[i] = x;
                    c.SentAt[i] = now;
                    vals[i] = x;
                    mask |= 1 << i;
                }
                if (mask == 0) continue;
                Add(ref w, c, mask, LIVE, null);
                if (++sent % 40 == 1) Log.Info("givre : " + sent + " envois de seuils (" + c.Name + " " + Describe(c) + ")");
            }
            if (w != null) Flush(w, null);
        }

        // Buee : celui qui fait autorite sur la voiture, toutes les 2 s si elle a bouge.
        static void PollFog()
        {
            bool alone = Session.RemoteCount == 0;
            NetWriter w = null;
            for (int k = 0; k < cars.Count; k++)
            {
                Car c = cars[k];
                if (c.Frost == null) continue;
                float x = c.Frost.Value;
                if (x - c.FrostLast < 0.01f && x - c.FrostLast > -0.01f) continue;
                c.FrostLast = x;
                if (alone || VehicleSync.Authority(c.Index) != Session.LocalId) continue;   // pas a nous : derive locale
                vals[N] = x;
                Add(ref w, c, FOG | (c.PlayerIn != null && c.PlayerIn.Value ? INSIDE : 0), LIVE, null);
            }
            if (w != null) Flush(w, null);
        }

        // Entree d'un lot : chemin, masque, valeurs des bits poses (vals). Lots de 1000 octets au plus.
        static void Add(ref NetWriter w, Car c, int mask, int kind, Peer to)
        {
            if (w != null && w.Length + 40 + c.Key.Length * 3 > 1000) { Flush(w, to); w = null; }
            if (w == null) w = new NetWriter(Msg.Frost).U8(Session.LocalId).U8(kind);
            w.Str(c.Key).U8(mask);
            for (int i = 0; i <= N; i++) if ((mask & (1 << i)) != 0) w.F32(vals[i]);
        }

        static void Flush(NetWriter w, Peer to)
        {
            if (to == null) Session.SendAll(w, true);
            else if (Session.T != null) Session.T.SendReliable(to, w.ToArray());
        }

        // Hote : invite arrive en jeu (niveau 1) -> 28 s plus tard (ses voitures sont relevees et leur gel du
        // chargement est passe : 'Freezing' est dans Update avant 25 s), l'etat complet.
        static void Arrivals(float now)
        {
            if (now >= nextArrivals)
            {
                nextArrivals = now + 1f;
                foreach (PlayerInfo pi in Session.Players.Values)
                {
                    if (pi.Local || pi.Peer == null) continue;
                    int old;
                    levels.TryGetValue(pi.Id, out old);
                    if (pi.Level == 1 && old != 1) snapshots.Add(new KeyValuePair<float, Peer>(now + 28f, pi.Peer));
                    levels[pi.Id] = pi.Level;
                }
            }
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || Session.T == null || !Session.T.Peers.Contains(p)) continue;
                NetWriter w = null;
                int n = 0;
                foreach (Car c in cars)
                {
                    int mask = 0;
                    for (int g = 0; g < N; g++) if (c.Cut[g] != null) { vals[g] = c.Cut[g].Value; mask |= 1 << g; }
                    if (c.Frost != null) { vals[N] = c.Frost.Value; mask |= FOG | (c.PlayerIn != null && c.PlayerIn.Value ? INSIDE : 0); }
                    if (mask == 0) continue;
                    Add(ref w, c, mask, SNAP, p);
                    n++;
                }
                if (w != null) Flush(w, p);
                Log.Info("givre : etat des vitres de " + n + " voitures envoye a " + p);
            }
        }

        // ---------------------------------------------------------------- reception
        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            int kind = r.U8();
            // L'etat complet va de l'hote a un seul invite : jamais relaye.
            NetWriter relay = Session.IsHost && kind == LIVE ? new NetWriter(Msg.Frost).U8(who).U8(kind) : null;
            float now = Time.realtimeSinceStartup;
            int n = 0;
            while (r.More)
            {
                string key = r.Str();
                int mask = r.U8();
                for (int i = 0; i <= N; i++) if ((mask & (1 << i)) != 0) vals[i] = r.F32();
                if (relay != null)
                {
                    relay.Str(key).U8(mask);
                    for (int i = 0; i <= N; i++) if ((mask & (1 << i)) != 0) relay.F32(vals[i]);
                }
                Car c;
                if (!byKey.TryGetValue(key, out c))
                {
                    if (now >= nextWarn && scans > 0) { nextWarn = now + 10f; Log.Warn("givre : " + key + " introuvable ici"); }
                    continue;
                }
                Apply(c, mask, kind == SNAP, who);
                n++;
            }
            if (relay != null) Session.Broadcast(relay, true, who);
            if (kind == SNAP) Log.Info("givre : etat des vitres de " + n + " voitures recu de l'hote");
        }

        static void Apply(Car c, int mask, bool absolute, int who)
        {
            for (int i = 0; i < N; i++)
            {
                if ((mask & (1 << i)) == 0 || c.Cut[i] == null) continue;
                float v = vals[i], mine = c.Cut[i].Value;
                if (absolute || v > mine + 0.0005f)
                {
                    c.Cut[i].Value = v;
                    c.Last[i] = v;
                    c.Fix[i] = false;
                    // Tout de suite sur le materiau (l'etat Update du jeu le reposera a son prochain tour).
                    FsmMaterial m = c.Mat[i];
                    if (m != null && m.Value != null) m.Value.SetFloat("_Cutoff", v);
                    if (++applied % 40 == 1) Log.Info("givre : " + applied + " seuils recus (" + c.Name + " " + Cutoffs[i] + " = " + v.ToString("F3") + ", joueur #" + who + ")");
                }
                else if (mine > v + 0.0005f) c.Fix[i] = true;   // l'autre a moins gratte : il aura le notre
            }
            if ((mask & FOG) != 0 && c.Frost != null)
            {
                bool meIn = c.PlayerIn != null && c.PlayerIn.Value;
                if (absolute || (mask & INSIDE) != 0 || !meIn) { c.Frost.Value = vals[N]; c.FrostLast = vals[N]; }
            }
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=givre : l'hote gratte le pare-brise de [Test] GivreVoiture (SORBET par defaut) de 30 a
        // 40 s, 10 passes par seconde, par le chemin du jeu ('Scrape 2' : GlassPos puis evenement WINDSHIELD a
        // 'Freezing', +0,005 chacune : environ +0,5). Chacun note chaque seconde, de 15 a 65 s (l'invite arrive
        // une dizaine de secondes apres l'hote), le seuil du pare-brise de cette voiture (variable et materiau) et sa buee.
        static int testStrokes;
        static float testNext, testLog;
        static bool testWarned;

        public static void Test(string mode, float t)
        {
            if (mode != "givre" || cars.Count == 0) return;
            string want = Config.Get("Test", "GivreVoiture", "SORBET");
            Car c = null;
            foreach (Car x in cars) if (x.Name.StartsWith(want) && x.Freezing != null) { c = x; break; }
            if (c == null)
            {
                if (!testWarned) { testWarned = true; Log.Info("autotest : givre : pas de voiture " + want + " (" + cars.Count + " suivies)"); }
                return;
            }
            if (Session.IsHost && t >= 30f && t < 40f && t >= testNext)
            {
                testNext = t + 0.1f;
                if (testStrokes == 0) Log.Info("autotest : givre : l'hote gratte le pare-brise de " + c.Name + " (" + Describe(c) + ")");
                string how = Stroke(c, 5);
                if (++testStrokes == 1 || testStrokes == 100) Log.Info("autotest : givre : passe " + testStrokes + " (" + how + ") -> " + Describe(c));
            }
            if (t >= 15f && t < 65f && t >= testLog)
            {
                testLog = t + 1f;
                Log.Info("autotest : givre t=" + t.ToString("F0") + " " + c.Name + " " + Describe(c));
            }
        }

        // Une passe de grattoir sur la vitre i, comme l'etat 'Scrape 2' du jeu. Automate arrete (taxi range) ou
        // vitre inconnue : le seuil directement.
        static string Stroke(Car c, int i)
        {
            if (c.Cut[i] == null) return "pas de seuil";
            PlayMakerFSM f = c.Freezing;
            if (c.Glass[i] != null && f.gameObject.activeInHierarchy && f.enabled && !string.IsNullOrEmpty(f.ActiveStateName))
            {
                FsmGameObject pos = f.FsmVariables.FindFsmGameObject("GlassPos");
                if (pos != null) pos.Value = c.Glass[i];
                f.SendEvent(Events[i]);
                return "evenement " + Events[i];
            }
            c.Cut[i].Value += 0.005f;
            return "seuil direct";
        }

        static string Describe(Car c)
        {
            var sb = new System.Text.StringBuilder("pare-brise ");
            sb.Append(c.Cut[5] != null ? c.Cut[5].Value.ToString("F3") : "?");
            FsmMaterial m = c.Mat[5];
            if (m != null && m.Value != null && m.Value.HasProperty("_Cutoff")) sb.Append(" (materiau ").Append(m.Value.GetFloat("_Cutoff").ToString("F3")).Append(')');
            sb.Append(", buee ").Append(c.Frost != null ? c.Frost.Value.ToString("F3") : "?");
            if (c.PlayerIn != null && c.PlayerIn.Value) sb.Append(" (joueur dedans)");
            sb.Append(", autorite #").Append(VehicleSync.Authority(c.Index));
            return sb.ToString();
        }
    }
}
