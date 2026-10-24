using System;
using MSCLoader;
using UnityEngine;

namespace Voyage
{
    // Voyage (demande de JD, 23/10) : une agence de voyage a Perajarvi vend un billet aller-retour vers une destination ;
    // a l'aerodrome, le guichetier demande si on l'utilise ; oui -> le vol et l'arrivee sur le circuit, hors de la carte du
    // jeu, ou un guichet ramene en Finlande. Destinations (24/10) : les circuits Assetto Corsa que le joueur a convertis
    // lui-meme avec le convertisseur Voyage (Destinations.cs) ; rien n'est livre avec le mod.
    // Mod MSCLoader a part, compatible MWCoop : en co-op, chacun prend son billet ; MWCoop montre les autres ou qu'ils soient
    // (la position est deja partagee). Sans MWCoop, il marche seul.
    public class Voyage : Mod
    {
        public override string ID { get { return "Voyage"; } }
        public override string Name { get { return "Voyage"; } }
        public override string Author { get { return "JD"; } }
        public override string Version { get { return "0.2.1"; } }   // (0.2.0 : My Summer Car aussi, 24/10 ; 0.2.1 : places d'arrivee pour 16 joueurs)
        public override string Description { get { return "Agence de voyage, aérodrome et vols vers tes circuits Assetto Corsa convertis (compatible MWCoop)."; } }
        public override Game SupportedGames { get { return Game.MySummerCar_And_MyWinterCar; } }

        public static Voyage Instance;

        public override void ModSetup()
        {
            Instance = this;
            SetupFunction(Setup.OnLoad, OnLoad);
            SetupFunction(Setup.Update, OnUpdate);
            SetupFunction(Setup.OnSave, OnSave);
            SetupFunction(Setup.OnGUI, OnGui);
        }

        // ---------------------------------------------------------------- etat du joueur
        string ticket = "", abroad = "";   // (identifiants de destination : billet en poche, destination ou l'on est)
        float loadedAt;
        bool loaded;

        string Key(string k) { string n = Coop.LocalName; return n.Length > 0 && !Coop.IsHost ? k + "_" + n : k; }

        void OnLoad()
        {
            Journal.Start();
            Jeu.Reset();
            Journal.Info("Voyage " + Version + " : chargement (" + (Coop.Present ? "avec" : "sans") + " MWCoop)");
            try
            {
                if (SaveLoad.ValueExists(this, Key("billet2"))) ticket = SaveLoad.ReadValue<string>(this, Key("billet2")) ?? "";
                if (SaveLoad.ValueExists(this, Key("ailleurs2"))) abroad = SaveLoad.ReadValue<string>(this, Key("ailleurs2")) ?? "";
            }
            catch (Exception e) { Journal.Info("lecture de la sauvegarde : " + e.Message); }
            try { Destinations.Scan(); } catch (Exception e) { Journal.Info("destinations : " + e); }
            try { Lieux.Build(); } catch (Exception e) { Journal.Info("construction : " + e); }
            // sauvegarde faite a destination : le circuit est recharge (s'il a ete supprime, la garde ramene a l'aerodrome)
            if (abroad.Length > 0 && !Destinations.Load(Destinations.Find(abroad))) Journal.Info("destination " + abroad + " introuvable : retour a l'aerodrome");
            loaded = true; loadedAt = Time.realtimeSinceStartup;
            Journal.Info(State());
            Essai.Start();
            Coop.Listen(msg => { if (msg == "decolle") Avion.TakeOff(); else if (msg == "atterrit") Avion.Land(); });
        }

        void OnSave()
        {
            try
            {
                SaveLoad.WriteValue(this, Key("billet2"), ticket);
                SaveLoad.WriteValue(this, Key("ailleurs2"), abroad);
                Journal.Info("sauvegarde : " + State());
            }
            catch (Exception e) { Journal.Info("sauvegarde : " + e.Message); }
        }

        public string State() { return "billet " + (ticket.Length > 0 ? ticket : "-") + ", a destination " + (abroad.Length > 0 ? abroad : "-") + (seq != null ? ", en vol" : ""); }

        // ---------------------------------------------------------------- actions
        internal void BuyTicket(Destination d)
        {
            if (d == null)
            {
                Jeu.Say(Destinations.All.Count == 0 ? "Agent : aucun vol pour l'instant. Convertis un circuit avec le convertisseur Voyage." : "Agent : choisis ta destination sur une affiche.",
                        Destinations.All.Count == 0 ? "Agent: no flights yet. Convert a track with the Voyage converter." : "Agent: pick your destination on a poster.");
                return;
            }
            if (ticket.Length > 0)
            {
                Destination t = Destinations.Find(ticket);
                Jeu.Say("Agent : tu as déjà ton billet pour " + (t != null ? t.Name : ticket) + ". L'aérodrome est " + Jeu.AirFr + ".", "Agent: you already have your ticket to " + (t != null ? t.Name : ticket) + ". The airfield is " + Jeu.AirEn + ".");
                return;
            }
            if (Jeu.Money == null || Jeu.Money.Value < d.Price)
            {
                noMoneyUntil = Time.realtimeSinceStartup + 2f;
                Jeu.Say("Agent : il te manque de l'argent. Le billet pour " + d.Name + " coûte " + d.Price + " mk.", "Agent: you're short on money. The ticket to " + d.Name + " is " + d.Price + " mk.");
                return;
            }
            Jeu.Money.Value -= d.Price;   // (MWCoop, Wallet : une depense reste au joueur)
            ticket = d.Id; ticketPrice = d.Price;
            if (Lieux.Agence != null) Jeu.Sound("Store", "cash_register_2", Lieux.Agence.transform.position);
            Jeu.Say("Agent : Kiitos ! Aller-retour pour " + d.Name + ". Présente-toi au guichet de l'aérodrome, " + Jeu.AirFr + ".",
                    "Agent: Kiitos! Return ticket to " + d.Name + ". Go to the airfield desk, " + Jeu.AirEn + ".");
            Journal.Info("billet achete pour " + d.Id + ", reste " + Jeu.Money.Value.ToString("F0") + " mk");
        }
        int ticketPrice = 400;

        public void Board()
        {
            if (ticket.Length == 0) { Jeu.Say("Guichet : il te faut un billet. L'agence de voyage est à Perajarvi.", "Desk: you need a ticket. The travel agency is in Perajarvi."); return; }
            Destination d = Destinations.Find(ticket);
            if (d == null)
            {
                Jeu.Say("Guichet : ce vol n'existe plus (le circuit a été supprimé). Ton billet est remboursé.", "Desk: this flight no longer exists (the track was deleted). Your ticket is refunded.");
                if (Jeu.Money != null) Jeu.Money.Value += ticketPrice;
                ticket = "";
                return;
            }
            ticket = "";
            Coop.Send("decolle");
            // noir, le decollage vu du bord de la piste, noir « Vol pour ... » (le circuit se charge), arrivee
            Begin("vers " + d.Id,
                new Step(0.7f, Fade.Out, null, () => { Avion.TakeOff(); Cine.Watch(Lieux.CineDepart); }),
                new Step(0.7f, Fade.In),
                new Step(13f, Fade.None),
                new Step(1.6f, Fade.Out, null, () => { Cine.Stop(); }),
                new Step(0.2f, Fade.Black, Jeu.T("Vol pour " + d.Name + "…", "Flying to " + d.Name + "…", "Рейс: " + d.Name + "…"), () =>
                {
                    if (!Destinations.Load(d))
                    {
                        Journal.Info("vol : " + d.Id + " ne se charge pas, retour");
                        ticket = d.Id; Jeu.Teleport(Lieux.AirfieldArrival, Lieux.AirfieldArrivalYaw);
                        Jeu.Say("Guichet : vol annulé, le circuit n'a pas pu être chargé.", "Desk: flight cancelled, the track could not be loaded.");
                        return;
                    }
                    abroad = d.Id; Jeu.Teleport(Spread(d.ArrivalWorld, d.Yaw), d.Yaw);
                }),
                new Step(2.8f, Fade.Black, Jeu.T("Vol pour " + d.Name + "…", "Flying to " + d.Name + "…", "Рейс: " + d.Name + "…"), () => { if (abroad == d.Id) Jeu.Teleport(Spread(d.ArrivalWorld, d.Yaw), d.Yaw); }),
                new Step(1.6f, Fade.In, null, Arrived));
        }

        public void ComeBack()
        {
            if (abroad.Length == 0) return;
            Vector3 to = Spread(Lieux.AirfieldArrival, Lieux.AirfieldArrivalYaw);
            float yaw = Lieux.AirfieldArrivalYaw;
            // noir « Retour en Finlande », l'atterrissage vu du bord de la piste, le terminal ; le circuit est decharge
            Begin("vers la Finlande",
                new Step(1.6f, Fade.Out),
                new Step(3f, Fade.Black, Jeu.T("Retour en Finlande…", "Flying back to Finland…", "Обратно в Финляндию…"),
                         () => { Jeu.Teleport(to, yaw); abroad = ""; Destinations.Unload(); Coop.Send("atterrit"); Avion.Land(); Cine.Watch(Lieux.CineArrivee); }),
                new Step(0.8f, Fade.In),
                new Step(13f, Fade.None),
                new Step(0.8f, Fade.Out, null, () => { Cine.Stop(); Jeu.Teleport(to, yaw); }),
                new Step(0.8f, Fade.In, null, Arrived));
        }

        // (co-op : chacun son pas a l'arrivee, pas tous sur le meme point ; rangees de 5 de front, la suivante 1,2 m derriere --
        // le salon va jusqu'a 16 joueurs, le 6e arrivait sur le 1er)
        static Vector3 Spread(Vector3 at, float yaw)
        {
            int id = Mathf.Max(0, Coop.LocalId);
            return at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(((id % 5) - 2) * 1.1f, 0f, -(id / 5) * 1.2f);
        }

        void Arrived()
        {
            if (Jeu.Stop != null) Jeu.Stop.Value = false;
            lastHello = Time.realtimeSinceStartup; inTerminal = abroad.Length == 0;   // (pas de « bonjour » du guichet en arrivant)
            Destination d = Destinations.Find(abroad);
            if (d != null) Jeu.Say("Bienvenue à " + d.Name + " ! Le guichet du retour est juste à côté.", "Welcome to " + d.Name + "! The return desk is right next to you.");
            else Jeu.Say("Tervetuloa takaisin ! Bon retour en Finlande.", "Tervetuloa takaisin! Welcome back to Finland.");
            Journal.Info("vol : arrive, " + State());
        }

        // ---------------------------------------------------------------- sequence du vol : etapes avec fondus
        enum Fade { None, Out, Black, In }
        class Step
        {
            public float Time; public Fade Fade; public string Text; public System.Action End;
            public Step(float time, Fade fade, string text = null, System.Action end = null) { Time = time; Fade = fade; Text = text; End = end; }
        }
        Step[] seq;
        int seqAt;
        float stepAt;

        void Begin(string what, params Step[] steps)
        {
            seq = steps; seqAt = 0; stepAt = Time.realtimeSinceStartup;
            if (Jeu.Stop != null) Jeu.Stop.Value = true;
            Jeu.ClearHand();
            Journal.Info("vol : depart " + what);
        }

        void TickSeq()
        {
            if (Jeu.Stop != null) Jeu.Stop.Value = true;
            while (seq != null && Time.realtimeSinceStartup - stepAt >= seq[seqAt].Time)
            {
                Step s = seq[seqAt];
                stepAt += s.Time;
                seqAt++;
                if (seqAt >= seq.Length) seq = null;
                try { if (s.End != null) s.End(); } catch (Exception e) { Journal.Info("vol : " + e); }
            }
        }

        float Darkness
        {
            get
            {
                if (seq == null) return 0f;
                Step s = seq[seqAt];
                float k = Mathf.Clamp01((Time.realtimeSinceStartup - stepAt) / Mathf.Max(s.Time, 0.01f));
                switch (s.Fade)
                {
                    case Fade.Out: return k;
                    case Fade.Black: return 1f;
                    case Fade.In: return 1f - k;
                    default: return 0f;
                }
            }
        }

        void OnGui()
        {
            float d = Darkness;
            if (d <= 0f) return;
            Color keep = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, d);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            Step s = seq != null ? seq[seqAt] : null;
            if (s != null && s.Text != null)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((Time.realtimeSinceStartup - stepAt) * 2f));
                var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(18, Screen.height / 28), fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(0, 0, Screen.width, Screen.height), s.Text, st);
            }
            GUI.color = keep;
        }

        // ---------------------------------------------------------------- visee : comptoir de l'agence, guichetiers
        float noMoneyUntil, lastClick, askUntil;
        Lieux.Spot asking;
        bool handSet;

        void OnUpdate()
        {
            if (!loaded) return;
            try
            {
                Construction.TickTexts();
                Jeu.TickSay();
                Essai.Tick();
                Avion.Tick();
                Cine.Tick();
                if (!Jeu.Ready) return;
                if (seq != null) { TickSeq(); return; }
                Guard();
                Greet();
                Hand();
            }
            catch (Exception e) { Journal.Info("mise a jour : " + e); loaded = Essai.Active; }
        }

        void Hand()
        {
            Lieux.Spot hit = null;
            Ray ray;
            bool aimed = Jeu.Aim(out ray);
            if (Essai.AimRay.HasValue) { ray = Essai.AimRay.Value; aimed = true; }
            if (aimed && !(Jeu.InCar != null && Jeu.InCar.Value))
            {
                float best = 99f;
                RaycastHit h;
                foreach (Lieux.Spot s in Lieux.Spots) if (s.Col != null && s.Col.Raycast(ray, out h, Mathf.Min(best, s.Reach))) { best = h.distance; hit = s; }
            }
            float now = Time.realtimeSinceStartup;
            if (asking != null && (hit != asking || now > askUntil))
            {
                if (now <= askUntil) Jeu.Say(asking.Kind == Lieux.Kind.Return ? "Guichet : d'accord, profite encore un peu." : "Guichet : d'accord, une autre fois.",
                                             asking.Kind == Lieux.Kind.Return ? "Desk: alright, enjoy it a bit longer." : "Desk: alright, another time.");
                asking = null;
            }
            if (hit == null)
            {
                if (handSet) { Jeu.ClearHand(); handSet = false; }
                return;
            }
            handSet = true;
            bool click = (Input.GetMouseButtonDown(0) || Essai.Click) && now - lastClick > 0.5f;
            Essai.Click = false;
            if (click) lastClick = now;
            switch (hit.Kind)
            {
                case Lieux.Kind.Counter:
                {
                    Destination only = Destinations.All.Count == 1 ? Destinations.All[0] : null;
                    Destination target = hit.Dest ?? only;
                    Jeu.GuiBuy.Value = target != null; Jeu.GuiUse.Value = target == null;
                    Jeu.GuiText.Value = now < noMoneyUntil ? "NOT ENOUGH MONEY"
                        : ticket.Length > 0 ? Jeu.T("BILLET DÉJÀ ACHETÉ", "TICKET ALREADY BOUGHT")
                        : target == null ? Jeu.T("PARLER", "TALK")
                        : Jeu.T("BILLET ALLER-RETOUR · ", "RETURN TICKET · ") + target.Name.ToUpperInvariant() + " " + target.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " MK";
                    if (click) BuyTicket(target);
                    break;
                }
                case Lieux.Kind.Airfield:
                case Lieux.Kind.Return:
                    bool island = hit.Kind == Lieux.Kind.Return;
                    Jeu.GuiUse.Value = true; Jeu.GuiBuy.Value = false;
                    if (asking == hit)
                    {
                        Jeu.GuiText.Value = Jeu.T("OUI : CLIQUE  ·  NON : REGARDE AILLEURS", "YES: CLICK  ·  NO: LOOK AWAY");
                        if (click) { asking = null; if (island) ComeBack(); else Board(); }
                    }
                    else
                    {
                        Jeu.GuiText.Value = Jeu.T("PARLER", "TALK");
                        if (!click) break;
                        if (!island && ticket.Length == 0) { Jeu.Say("Guichet : il te faut un billet. L'agence de voyage est " + Jeu.AgencyFr + ".", "Desk: you need a ticket. The travel agency is " + Jeu.AgencyEn + "."); break; }
                        if (island && abroad.Length == 0) { Jeu.Say("Guichet : bonjour !", "Desk: hello!"); break; }
                        asking = hit; askUntil = now + 12f;
                        Destination td = Destinations.Find(ticket);
                        string tn = td != null ? td.Name : ticket;
                        if (island) Jeu.Say("Guichet : tu veux prendre le vol retour pour la Finlande ?", "Desk: do you want to take the flight back to Finland?");
                        else Jeu.Say("Guichet : tu veux utiliser ton billet pour " + tn + " ? L'avion part tout de suite.", "Desk: do you want to use your ticket to " + tn + "? The plane leaves right away.");
                    }
                    break;
            }
        }

        // ---------------------------------------------------------------- accueil en entrant dans l'agence ou le terminal
        bool inAgency, inTerminal;
        float lastHello = -100f;

        void Greet()
        {
            Transform p = Jeu.Player;
            if (p == null) return;
            float now = Time.realtimeSinceStartup;
            bool a = Lieux.Inside(Lieux.Agence, p.position, 3.4f, 2.6f), t = Lieux.Inside(Lieux.TerminalGo, p.position, 5.8f, 3.8f);
            if (a && !inAgency && now - lastHello > 40f)
            {
                lastHello = now;
                if (ticket.Length > 0) Jeu.Say("Agent : ton billet est prêt. L'aérodrome est " + Jeu.AirFr + ".", "Agent: your ticket is ready. The airfield is " + Jeu.AirEn + ".");
                else if (Destinations.All.Count == 0) Jeu.Say("Agent : Tervetuloa ! Pas encore de vol : convertis un circuit avec le convertisseur Voyage.", "Agent: Tervetuloa! No flights yet: convert a track with the Voyage converter.");
                else Jeu.Say("Agent : Tervetuloa ! Choisis ta destination sur une affiche.", "Agent: Tervetuloa! Pick your destination on a poster.");
            }
            if (t && !inTerminal && now - lastHello > 40f && abroad.Length == 0)
            {
                lastHello = now;
                Destination td = Destinations.Find(ticket);
                Jeu.Say(td != null ? "Guichet : bonjour ! Le vol pour " + td.Name + " part dès que tu es prêt, viens me voir." : "Guichet : bonjour ! Les billets se prennent à l'agence de voyage de Perajarvi.",
                        td != null ? "Desk: hello! The flight to " + td.Name + " leaves as soon as you're ready, come and see me." : "Desk: hello! Tickets are sold at the travel agency in Perajarvi.");
            }
            inAgency = a; inTerminal = t;
        }

        // ---------------------------------------------------------------- garde-fous (chargement, chute a l'eau)
        float farSince = -1f;

        void Guard()
        {
            Transform p = Jeu.Player;
            if (p == null) return;
            float now = Time.realtimeSinceStartup;
            Destination d = Destinations.Find(abroad);
            if (abroad.Length > 0 && (d == null || d.Root == null))   // (circuit supprime ou illisible : retour a l'aerodrome)
            {
                Journal.Info("destination " + abroad + " absente : retour a l'aerodrome");
                abroad = ""; Jeu.Teleport(Lieux.AirfieldArrival, Lieux.AirfieldArrivalYaw);
                Jeu.Say("Ce circuit n'est plus là : retour à l'aérodrome.", "This track is gone: back to the airfield.");
                return;
            }
            bool near = d != null && Destinations.Near(d, p.position);
            if (d != null && near && p.position.y < d.MinY - 30f)
            {
                Journal.Info("tombe sous le circuit (" + p.position + ") : retour au guichet");
                Jeu.Teleport(Lieux.ReturnArrival, Lieux.ReturnArrivalYaw);
                Jeu.Say("On t'a ramené au guichet.", "You were brought back to the desk.");
                return;
            }
            // au chargement (ou chez un invite) : la position ne colle pas avec l'etat du voyage -> au bon endroit
            bool farAway = new Vector2(p.position.x, p.position.z).magnitude > 15000f;   // (cases des destinations, a 25 km)
            bool wrong = d != null ? !near : farAway;
            if (!wrong) { farSince = -1f; return; }
            if (farSince < 0f) farSince = now;
            if (now - farSince < 4f) return;
            farSince = -1f;
            if (d != null) { Journal.Info("a " + d.Id + " d'apres la sauvegarde mais en " + p.position + " : envoye au point d'arrivee"); Jeu.Teleport(d.ArrivalWorld, d.Yaw); }
            else { Journal.Info("sur une case de destination sans y etre d'apres la sauvegarde : envoye a l'aerodrome"); Jeu.Teleport(Lieux.AirfieldArrival, Lieux.AirfieldArrivalYaw); }
        }
    }
}
