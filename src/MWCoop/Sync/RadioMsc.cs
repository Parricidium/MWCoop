using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Autoradios et lecteurs CD des vehicules de My Summer Car (dossier #6 rouvert, 27/10 ; JD : « chaque variable la meme pour tout
    // le monde » sur la SATSUMA). Leurs boutons sont des molettes (automate 'Knob' : « Get scroll » retombe sur « Mouse off 2 » des
    // que la souris n'y est pas) : rejoues par Jobs, ils s'eteignaient chez l'autre et renvoyaient leur « Off » (22/10, Jobs.MscSkip) --
    // laisses a chacun depuis : marche, volume, station, disque et piste differents chez chaque joueur. Ici, plus de rejeu de gestes :
    // l'ETAT de chaque poste monte dans un vehicule est partage.
    //  - autoradio (SATSUMA radio(Clone), FERNDALE, HAYOSIKO, KEKMET, GIFU : RadioPivot/Radio) : ButtonsRadio/RadioVolume::Knob Volume
    //    (0 = eteint ; « Check tune » : jeu de canaux dans le haut-parleur, station d'apres le Tune du bouton, « Rotate knob » : volume
    //    des haut-parleurs ; « Off » : jeu de canaux rendu a RADIO) et TunerPivot/RadioTuner::Knob Tune (0,091 radio locale, 0,071
    //    radio folk, sinon parasites ; « Wait1 » : aiguille, molette, sauvegarde) ;
    //  - lecteur CD (SATSUMA cd player(Clone)/ButtonsCD) : RadioVolume::Knob Volume (« On » : allume, +0,1 ; « Off » : eteint ;
    //    « Wait1 » : volume), RadioCDSwitch::Use RadioOn (mode radio / CD ; « Switch 2 »), TrackChannelSwitch::ChangeChannel Channel
    //    (folk / locale ; « State 1 »), trigger_disc::Assembly CDin (disque insere : « Assemble » avec le disque en Part, ejecte :
    //    « Eject »), piste suivante (ChangeTrack « State 1 » -> NEXT au lecteur).
    // Chacun releve l'etat de ses postes a chaque image ; un changement qui ne vient pas d'un message (geste d'ici) part aussitot
    // (@radio, fiable, relaye par l'hote) et s'applique chez les autres en posant variables et etats -- jamais par la souris.
    // Toutes les 2 s l'hote envoie l'etat de tous ses postes (arrivant en cours de partie, message perdu) ; un invite s'y recale sauf
    // geste d'ici dans les 3 s. La musique elle-meme (morceau, position ; piste et position du disque) suit deja l'hote (Radio.cs) ;
    // la piste suivante d'un invite va a l'hote, dont le lecteur fait reference.
    public static class RadioMsc
    {
        class Unit
        {
            public string Key; public bool Cd; public Transform Car;
            public PlayMakerFSM Vol, Tuner, Switch, Chan, Track, Disc, Player;
            public float V, T; public bool On, Ch, In;         // dernier etat connu (releve ici, envoye ou applique)
            public float Guard, Changed;                       // application en cours (pas un geste d'ici) ; dernier geste d'ici
            public string TrackState = "";
            public bool Pending;                               // lecteur CD eteint faute de courant quand son etat est arrive : rallume au retour
        }
        static readonly List<Unit> units = new List<Unit>();
        static float nextScan = -1, nextSnap, loadedAt, nextWarn;
        static int logs;

        public static void OnLevelLoaded()
        {
            units.Clear();
            loadedAt = Time.realtimeSinceStartup;
            nextScan = Game.Msc && PlayerSync.InGame ? loadedAt + 12f : -1;
            nextSnap = 0;
        }

        static PlayMakerFSM FsmUnder(Transform root, string obj, string fsm)
        {
            if (root == null) return null;
            foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.gameObject.name == obj && f.FsmName == fsm) return f;
            return null;
        }

        static void Scan()
        {
            units.RemoveAll(u => u.Vol == null || VehicleSync.CarRoot(u.Vol.transform) != u.Car);   // (poste demonte : son chemin change)
            int before = units.Count;
            foreach (Object o in Game.FsmsNamed("Knob", 0.5f))
            {
                PlayMakerFSM f = o as PlayMakerFSM;
                if (f == null || f.hideFlags != HideFlags.None || f.FsmName != "Knob" || f.gameObject.name != "RadioVolume" || f.Fsm == null) continue;
                if (units.Exists(y => y.Vol == f)) continue;
                Transform car = VehicleSync.CarRoot(f.transform);
                if (car == null) continue;   // (poste pas monte : piece libre)
                bool cd = f.Fsm.GetState("Check status") != null && f.Fsm.GetState("On") != null && f.Fsm.GetState("Off 2") != null;
                bool radio = f.Fsm.GetState("Check tune") != null && f.Fsm.GetState("Rotate knob") != null && f.Fsm.GetState("Off") != null;
                if (!cd && !radio) continue;
                Transform root = f.transform.parent != null ? f.transform.parent.parent : null;
                var u = new Unit { Key = Recon.Path(f.transform), Cd = cd, Car = car, Vol = f };
                if (cd)
                {
                    u.Switch = FsmUnder(root, "RadioCDSwitch", "Use");
                    u.Chan = FsmUnder(root, "TrackChannelSwitch", "ChangeChannel");
                    u.Track = FsmUnder(root, "TrackChannelSwitch", "ChangeTrack");
                    u.Disc = FsmUnder(root, "trigger_disc", "Assembly");
                    u.Player = FsmUnder(car, "CDPlayer", "Update");
                }
                else u.Tuner = FsmUnder(root, "RadioTuner", "Knob");
                Read(u, out u.V, out u.T, out u.On, out u.Ch, out u.In);
                units.Add(u);
            }
            if (units.Count != before)
            {
                var sb = new System.Text.StringBuilder();
                foreach (Unit u in units) sb.Append(' ').Append(u.Car.name).Append(u.Cd ? " (CD)" : " (radio)");
                Log.Info("radio (MSC) : " + units.Count + " poste(s) partage(s) :" + sb);
            }
        }

        static float F(PlayMakerFSM f, string n) { FsmFloat v = f != null ? f.FsmVariables.FindFsmFloat(n) : null; return v != null ? v.Value : 0f; }
        static bool B(PlayMakerFSM f, string n) { FsmBool v = f != null ? f.FsmVariables.FindFsmBool(n) : null; return v != null && v.Value; }
        static void SetF(PlayMakerFSM f, string n, float x) { FsmFloat v = f != null ? f.FsmVariables.FindFsmFloat(n) : null; if (v != null) v.Value = x; }
        static void SetB(PlayMakerFSM f, string n, bool x) { FsmBool v = f != null ? f.FsmVariables.FindFsmBool(n) : null; if (v != null) v.Value = x; }

        static void Read(Unit u, out float v, out float t, out bool on, out bool ch, out bool inn)
        {
            v = F(u.Vol, "Volume"); t = F(u.Tuner, "Tune"); on = B(u.Switch, "RadioOn"); ch = B(u.Chan, "Channel"); inn = B(u.Disc, "CDin");
        }
        static bool Differs(Unit u, float v, float t, bool on, bool ch)
        {
            return Mathf.Abs(v - u.V) > 0.01f || Mathf.Abs(t - u.T) > 0.0005f || on != u.On || ch != u.Ch;
        }

        // Cle du disque (Props) : le disque tenu en main que trigger_disc a pris (Part) -- ou, insere, l'enfant du chariot.
        static string DiscKey(Unit u)
        {
            FsmGameObject p = u.Disc != null ? u.Disc.FsmVariables.FindFsmGameObject("Part") : null;
            return p != null && p.Value != null ? Props.KeyOf(p.Value) ?? "" : "";
        }

        public static void Update()
        {
            if (nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 10f; Scan(); }
            if (units.Count == 0) return;
            bool net = Session.Active && now - loadedAt > 20f;
            foreach (Unit u in units)
            {
                if (u.Vol == null) continue;
                float v, t; bool on, ch, inn;
                Read(u, out v, out t, out on, out ch, out inn);
                // Piste suivante d'ici (ChangeTrack « State 1 » : un clic, jamais pose d'ici) : a l'hote, dont le lecteur fait reference.
                string ts = u.Track != null && u.Track.enabled ? u.Track.ActiveStateName : "";
                if (ts == "State 1" && u.TrackState != "State 1" && net && !Session.IsHost)
                {
                    Session.SendToHost(Head(4, u), true);
                    Log.Info("radio (MSC) : " + u.Car.name + " : piste suivante demandee a l'hote");
                }
                u.TrackState = ts;
                if (u.Pending && u.Vol.ActiveStateName != "Off 2")
                {
                    u.Pending = false;
                    float keep = u.V;
                    SetF(u.Vol, "Volume", 0f);   // (« eteint » : Apply le rallume par « On »)
                    Apply(u, keep, u.T, u.On, u.Ch);
                    Log.Info("radio (MSC) : " + u.Car.name + " : courant revenu, lecteur rallume comme chez les autres (" + Describe(u) + ")");
                    continue;
                }
                if (now < u.Guard) continue;   // (etat pose par un message : il s'installe)
                if (inn != u.In)
                {
                    u.In = inn;
                    if (net)
                    {
                        string dk = inn ? DiscKey(u) : "";
                        Session.SendAll(Head(inn ? 2 : 3, u).Str(dk), true);
                        Log.Info("radio (MSC) : " + u.Car.name + " : disque " + (inn ? "insere ici (" + dk + ")" : "ejecte ici"));
                    }
                }
                if (!Differs(u, v, t, on, ch)) continue;
                u.V = v; u.T = t; u.On = on; u.Ch = ch;
                if (!net) continue;
                u.Changed = now;
                Session.SendAll(State(0, u), true);
                if (logs++ < 40) Log.Info("radio (MSC) : " + u.Car.name + " : geste d'ici, " + Describe(u));
            }
            if (net && Session.IsHost && Session.RemoteCount > 0 && now >= nextSnap)
            {
                nextSnap = now + 2f;
                foreach (Unit u in units) if (u.Vol != null) Session.SendAll(State(1, u).Str(u.In ? DiscKey(u) : ""), false);
            }
        }

        static NetWriter Head(int kind, Unit u) { return new NetWriter(Msg.Job).U8(Session.LocalId).Str("@radio").U8(kind).Str(u.Key); }
        static NetWriter State(int kind, Unit u) { return Head(kind, u).F32(u.V).F32(u.T).Bool(u.On).Bool(u.Ch).Bool(u.In); }
        static string Describe(Unit u)
        {
            return (u.V > 0.001f ? "allume, volume " + u.V.ToString("F1") : "eteint") + (u.Cd ? (u.On ? ", mode radio " + (u.Ch ? "folk" : "locale") : ", mode CD") + (u.In ? ", disque" : ", sans disque")
                   : ", station " + u.T.ToString("F3"));
        }

        static Unit Find(string key)
        {
            Unit u = units.Find(x => x.Key == key && x.Vol != null);
            if (u == null && Time.realtimeSinceStartup >= nextScan - 9f) { Scan(); u = units.Find(x => x.Key == key && x.Vol != null); }
            return u;
        }

        public static void OnMessage(int who, NetReader r)
        {
            if (!Game.Msc || !PlayerSync.InGame) return;
            int kind = r.U8(); string key = r.Str();
            Unit u = Find(key);
            if (u == null)
            {
                if (Time.realtimeSinceStartup >= nextWarn) { nextWarn = Time.realtimeSinceStartup + 30f; Log.Info("radio (MSC) : poste " + key + " pas monte ici (message de #" + who + ")"); }
                return;
            }
            float now = Time.realtimeSinceStartup;
            switch (kind)
            {
                case 0:
                case 1:
                {
                    float v = r.F32(), t = r.F32(); bool on = r.Bool(), ch = r.Bool(), inn = r.Bool();
                    string dk = kind == 1 && r.More ? r.Str() : "";
                    if (kind == 1 && (Session.IsHost || now - u.Changed < 3f || now < u.Guard)) return;   // (instantane : un geste d'ici recent l'emporte)
                    if (Differs(u, v, t, on, ch)) { Apply(u, v, t, on, ch); Log.Info("radio (MSC) : " + u.Car.name + " : " + (kind == 1 ? "recale sur l'hote" : "geste de #" + who) + ", " + Describe(u)); }
                    if (kind == 1 && inn != u.In) { if (inn) Insert(u, dk, who); else Eject(u, who); }
                    break;
                }
                case 2: Insert(u, r.Str(), who); break;
                case 3: Eject(u, who); break;
                case 4:
                    if (!Session.IsHost || u.Player == null) break;
                    u.Player.SendEvent("NEXT");
                    Log.Info("radio (MSC) : " + u.Car.name + " : piste suivante de #" + who + " (lecteur " + u.Player.ActiveStateName + ")");
                    break;
            }
        }

        static void Set(PlayMakerFSM f, string state)
        {
            if (f == null || !f.enabled || f.Fsm == null || f.Fsm.GetState(state) == null) return;
            try { Game.SetState(f, state); } catch (System.Exception e) { Log.Warn("radio (MSC) : " + Recon.Path(f.transform) + " -> " + state + " : " + e.Message); }
        }

        static void Apply(Unit u, float v, float t, bool on, bool ch)
        {
            float now = Time.realtimeSinceStartup;
            u.Guard = now + 0.8f;
            float was = F(u.Vol, "Volume");
            string vs = u.Vol.ActiveStateName;
            if (!u.Cd)
            {
                bool tuned = u.Tuner != null && Mathf.Abs(F(u.Tuner, "Tune") - t) > 0.0005f;
                if (tuned) { SetF(u.Tuner, "Tune", t); Set(u.Tuner, "Wait1"); }
                SetF(u.Vol, "Volume", v);
                // (poste sans courant, « Not work » : variables seulement -- il reprend tout seul « Check tune » au retour du courant)
                if (vs != "Not work")
                {
                    if (v <= 0.001f) { if (was > 0.001f) Set(u.Vol, "Off"); }
                    else if (tuned || Mathf.Abs(was - v) > 0.01f || was <= 0.001f) Set(u.Vol, "Check tune");
                }
            }
            else
            {
                bool modeCh = u.Switch != null && B(u.Switch, "RadioOn") != on, chanCh = u.Chan != null && B(u.Chan, "Channel") != ch;
                SetB(u.Switch, "RadioOn", on); SetB(u.Chan, "Channel", ch);
                if (vs == "Off 2")
                {
                    // (sans courant : variables seulement ; au retour du courant, Update rallume le lecteur comme ici -- sinon il restait
                    // a moitie allume, interrupteurs coupes : volume montre mais ni radio ni disque, arrivant en cours de partie)
                    SetF(u.Vol, "Volume", v); u.Pending = v > 0.001f;
                }
                else if (v <= 0.001f) { SetF(u.Vol, "Volume", 0f); u.Pending = false; if (was > 0.001f) Set(u.Vol, "Off"); }
                else
                {
                    // Allumer : « On » (+0,1, interrupteur radio / CD et consommation rallumes) ; puis le mode (« Switch 2 » : pistes ou canaux),
                    // puis la station en mode radio (« State 1 » : jeu de canaux dans le haut-parleur).
                    bool turnOn = was <= 0.001f || u.Switch != null && !u.Switch.enabled;
                    u.Pending = false;
                    if (turnOn) { SetF(u.Vol, "Volume", v - 0.1f); Set(u.Vol, "On"); }
                    else if (Mathf.Abs(was - v) > 0.01f) { SetF(u.Vol, "Volume", v); Set(u.Vol, "Wait1"); }
                    if (turnOn || modeCh) Set(u.Switch, "Switch 2");
                    if (on && (turnOn || modeCh || chanCh)) Set(u.Chan, "State 1");
                }
            }
            u.V = v; u.T = t; u.On = on; u.Ch = ch;
        }

        static void Insert(Unit u, string dk, int who)
        {
            if (u.Disc == null || B(u.Disc, "CDin")) { u.In = B(u.Disc, "CDin"); return; }
            GameObject disc = dk.Length > 0 ? Props.ObjectOf(dk) : null;
            if (disc == null) { Log.Warn("radio (MSC) : " + u.Car.name + " : disque " + dk + " de #" + who + " introuvable ici"); return; }
            u.Guard = Time.realtimeSinceStartup + 1.5f;
            Props.ReleaseRemote(disc);
            u.Disc.FsmVariables.GetFsmGameObject("Part").Value = disc;
            if (!u.Disc.enabled) u.Disc.enabled = true;
            Set(u.Disc, "Assemble");
            u.In = true;
            Log.Info("radio (MSC) : " + u.Car.name + " : disque " + dk + " insere par #" + who);
        }

        static void Eject(Unit u, int who)
        {
            if (u.Disc == null || !B(u.Disc, "CDin")) { u.In = false; return; }
            u.Guard = Time.realtimeSinceStartup + 1.5f;
            if (u.Player != null) u.Player.SendEvent("STOP CD");
            Set(u.Disc, "Eject");
            u.In = false;
            Log.Info("radio (MSC) : " + u.Car.name + " : disque ejecte par #" + who);
        }

        // Essais (mscetat) : etat de chaque poste partage, une ligne par poste.
        public static List<KeyValuePair<string, string>> EtatLines()
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (Unit u in units)
            {
                if (u.Vol == null) continue;
                float v, t; bool on, ch, inn;
                Read(u, out v, out t, out on, out ch, out inn);
                var sb = new System.Text.StringBuilder();
                sb.Append(" Volume=").Append(v.ToString("F2"));
                if (u.Cd)
                {
                    sb.Append(" RadioOn=").Append(on).Append(" Channel=").Append(ch).Append(" CDin=").Append(inn);
                    if (u.Player != null)
                    {
                        FsmString cd = u.Player.FsmVariables.FindFsmString("CurrentCD"); FsmInt idx = u.Player.FsmVariables.FindFsmInt("Index");
                        AudioSource a = u.Player.GetComponent<AudioSource>();
                        sb.Append(" disque=").Append(inn && cd != null ? cd.Value : "-").Append(" piste=").Append(inn && idx != null ? idx.Value.ToString() : "-")
                          .Append(" joue=").Append(a != null && a.isPlaying);
                    }
                }
                else sb.Append(" Tune=").Append(t.ToString("F3"));
                l.Add(new KeyValuePair<string, string>("radio:" + u.Car.name + (u.Cd ? "/cd" : "/radio"), sb.ToString()));
            }
            return l;
        }

        // Essais (Autotest=mscradio) : geste simule ici comme le ferait la molette / le bouton (variables + etat), detecte et envoye
        // par Update comme un vrai geste.
        public static string TestGesture(string car, string what)
        {
            Unit u = units.Find(y => y.Vol != null && y.Car.name.StartsWith(car));
            if (u == null) { Scan(); u = units.Find(y => y.Vol != null && y.Car.name.StartsWith(car)); }
            if (u == null) return "aucun poste dans " + car;
            string[] w = what.Split('=');
            float x = 0f;
            if (w.Length > 1) float.TryParse(w[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
            switch (w[0])
            {
                case "volume":
                    if (!u.Cd) { SetF(u.Vol, "Volume", x); if (x <= 0.001f) Set(u.Vol, "Off"); else Set(u.Vol, "Check tune"); }
                    else if (x <= 0.001f) { SetF(u.Vol, "Volume", 0f); Set(u.Vol, "Off"); }
                    else if (F(u.Vol, "Volume") <= 0.001f) { SetF(u.Vol, "Volume", x - 0.1f); Set(u.Vol, "On"); }
                    else { SetF(u.Vol, "Volume", x); Set(u.Vol, "Wait1"); }
                    break;
                case "station": SetF(u.Tuner, "Tune", x); Set(u.Tuner, "Wait1"); break;
                case "mode": Set(u.Switch, "Switch"); break;          // (bascule radio / CD, comme le clic)
                case "canal": Set(u.Chan, "Check status"); break;     // (bascule folk / locale, comme le clic)
                case "suivante": Set(u.Track, "State 1"); break;
                case "insere":   // (disque w[1] = cd(item1).. pose dans la fente, comme le clic : « Assemble »)
                {
                    GameObject d = Game.FindAny(w.Length > 1 ? w[1] : "cd(item1)");
                    if (d == null || u.Disc == null) return "pas de disque " + what;
                    u.Disc.FsmVariables.GetFsmGameObject("Part").Value = d; if (!u.Disc.enabled) u.Disc.enabled = true; Set(u.Disc, "Assemble");
                    return u.Car.name + " disque " + d.name + " (" + Props.KeyOf(d) + ") -> fente " + u.Disc.ActiveStateName;
                }
                case "acc": return VehicleSync.TestKey(u.Car.name, "Test");         // (contact : « Test » allume Electrics, puis « ACC on » et le demarreur)
                case "accok": return VehicleSync.TestKey(u.Car.name, "Shut off");   // (cle relachee : contact mis)
                case "ejecte": if (u.Player != null) u.Player.SendEvent("STOP CD"); Set(u.Disc, "Eject"); break;
                default: return "geste inconnu " + what;
            }
            return u.Car.name + " " + what + " -> " + Describe(u) + " (molette " + u.Vol.ActiveStateName + ")";
        }
        public static string TestState()
        {
            var sb = new System.Text.StringBuilder();
            GameObject st = Game.FindAny("Database/PartsStatus/Radio");
            PlayMakerFSM sf = st != null ? Game.FsmOn(st, "Status") : null;
            if (sf != null) sb.Append("courant radio ").Append(B(sf, "RadioOK")).Append(" cd ").Append(B(sf, "CDPlayerOK")).Append(" ; ");
            GameObject bat = Game.FindAny("Database/PartsStatus/Battery");
            PlayMakerFSM bf = bat != null ? Game.FsmOn(bat, "Data") : null;
            if (bf != null) sb.Append("batterie ").Append(F(bf, "Charge").ToString("F3")).Append(" ; ");
            foreach (Unit u in units) if (u.Vol != null) sb.Append(u.Car.name).Append(u.Cd ? "/cd" : "/radio").Append(" molette ").Append(u.Vol.ActiveStateName)
                .Append(u.Switch != null ? " inter " + (u.Switch.enabled ? u.Switch.ActiveStateName : "coupe") : "").Append(u.Track != null ? " pistes " + (u.Track.enabled ? u.Track.ActiveStateName : "coupe") : "")
                .Append(u.Chan != null ? " canaux " + (u.Chan.enabled ? u.Chan.ActiveStateName : "coupe") : "").Append(u.Disc != null ? " fente " + u.Disc.ActiveStateName : "").Append(" ; ");
            foreach (var kv in EtatLines()) sb.Append(kv.Key).Append(kv.Value).Append(" ; ");
            return sb.ToString();
        }
    }
}
