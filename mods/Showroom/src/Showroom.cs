using System;
using System.Collections.Generic;
using System.Globalization;
using MSCLoader;
using UnityEngine;

namespace Showroom
{
    // Showroom (demande de JD, 28/10) : mod MSCLoader a part pour My Summer Car, a activer / desactiver comme les autres. Une
    // salle de photo hors de la carte (Salle.cs) ; une touche (F8 par defaut, reglable dans MSCLoader) y emmene et ramene la ou
    // l'on etait. Au tableau : cinq projecteurs a allumer un par un, et une voiture du F10 de MWCoop (VAZ-2108 ou addon) sortie
    // au centre -- la vraie voiture de MWCoop (CoopApi 4, ShowF10Car : il faut MWCoop msc-1.15.25 ou plus).
    public class Showroom : Mod
    {
        public override string ID { get { return "Showroom"; } }
        public override string Name { get { return "Showroom"; } }
        public override string Author { get { return "JD"; } }
        public override string Version { get { return "0.1.0"; } }
        public override string Description { get { return "Salle de photo hors de la carte : projecteurs et voitures du F10 de MWCoop (touche F8)."; } }
        public override Game SupportedGames { get { return Game.MySummerCar; } }

        public static Showroom Instance;
        internal SettingsKeybind Touche;

        public override void ModSetup()
        {
            Instance = this;
            SetupFunction(Setup.OnLoad, OnLoad);
            SetupFunction(Setup.OnSave, OnSave);
            SetupFunction(Setup.ModSettings, Reglages);
        }

        void Reglages()
        {
            Touche = Keybind.Add("entrer", "Entrer / sortir du showroom", KeyCode.F8);
        }

        Pilote pilote;

        void OnLoad()
        {
            Journal.Start();
            Jeu.Reset();
            Journal.Info("Showroom " + Version + " : chargement (" + (Coop.Present ? "avec" : "sans") + " MWCoop)");
            try { Salle.Build(); } catch (Exception e) { Journal.Info("construction : " + e); }
            pilote = Salle.Root != null ? Salle.Root.AddComponent<Pilote>() : null;
            if (pilote == null) return;
            try
            {
                if (SaveLoad.ValueExists(this, "spots")) pilote.Spots = SaveLoad.ReadValue<string>(this, "spots") ?? "";
                if (SaveLoad.ValueExists(this, "retour")) pilote.Retour = SaveLoad.ReadValue<string>(this, "retour") ?? "";
                if (SaveLoad.ValueExists(this, "voiture")) pilote.Choix = SaveLoad.ReadValue<int>(this, "voiture");
            }
            catch (Exception e) { Journal.Info("lecture de la sauvegarde : " + e.Message); }
            pilote.Start2();
        }

        void OnSave()
        {
            if (pilote == null) return;
            try
            {
                SaveLoad.WriteValue(this, "spots", pilote.Spots);
                SaveLoad.WriteValue(this, "retour", pilote.Retour);
                SaveLoad.WriteValue(this, "voiture", pilote.Choix);
                Journal.Info("sauvegarde : projecteurs " + pilote.Spots + ", retour " + (pilote.Retour.Length > 0 ? pilote.Retour : "-"));
            }
            catch (Exception e) { Journal.Info("sauvegarde : " + e.Message); }
        }
    }

    // Tout ce qui tourne a chaque image (composant de la salle : Update / LateUpdate d'Unity, apres les automates du jeu)
    class Pilote : MonoBehaviour
    {
        public string Spots = "11111";   // (projecteurs allumes, 1 a 5)
        public string Retour = "";       // (« x;y;z;cap » : ou l'on etait avant d'entrer)
        public int Choix = -1;           // (voiture choisie : identifiant du F10)
        List<KeyValuePair<int, string>> cars = new List<KeyValuePair<int, string>>();
        int sel;
        float lastClick, nextCars;
        bool hand, dedans;
        // (eclairage du jeu coupe dedans, rendu en sortant)
        readonly List<Light> soleils = new List<Light>();
        bool fog; Color ambient; float ambientIntensity; UnityEngine.Rendering.AmbientMode ambientMode;
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public void Start2()
        {
            if (Spots == null || Spots.Length != 5) Spots = "11111";
            for (int i = 0; i < 5; i++) Salle.SetSpot(i, Spots[i] == '1');
            RefreshCars(true);
            Essai.Start(this);
        }

        void RefreshCars(bool log)
        {
            cars = Coop.Cars();
            sel = Math.Max(0, cars.FindIndex(c => c.Key == Choix));
            ShowName();
            if (log) Journal.Info("voitures du F10 : " + (cars.Count == 0 ? (Coop.HasCars ? "aucune (VAZ-2108 coupee ?)" : "MWCoop absent ou trop ancien (API < 4)") : string.Join(", ", cars.ConvertAll(c => c.Key + " " + c.Value).ToArray())));
        }

        void ShowName()
        {
            Textes.Set(Salle.NomVoiture, cars.Count == 0 ? Jeu.T("(aucune voiture du F10)", "(no F10 car)") : cars[sel].Value.ToUpperInvariant());
        }

        void Update()
        {
            if (!Jeu.Ready) return;
            Jeu.TickSay();
            Textes.Tick();
            Showroom m = Showroom.Instance;
            if ((m != null && m.Touche != null && m.Touche.GetKeybindDown()) || Essai.Take("touche")) Toggle();
            if (Time.realtimeSinceStartup >= nextCars) { nextCars = Time.realtimeSinceStartup + 5f; int n = cars.Count; cars = Coop.Cars(); if (cars.Count != n) { sel = Math.Max(0, cars.FindIndex(c => c.Key == Choix)); ShowName(); } }
            Essai.Tick();
            Aim();
        }

        // ---------------------------------------------------------------- entree / sortie
        public void Toggle()
        {
            Transform p = Jeu.Player;
            if (p == null) return;
            if (Jeu.InCar != null && Jeu.InCar.Value) { Jeu.Say("Descends de la voiture d'abord.", "Get out of the car first."); return; }
            if (Salle.Inside(p.position))
            {
                Vector3 back; float yaw;
                if (ParseRetour(out back, out yaw)) Jeu.Teleport(back, yaw);
                else { Jeu.Teleport(new Vector3(-6.2f, 0.5f, 6.6f), 0f); Journal.Info("pas de lieu de retour : maison"); }   // (la maison des parents, comme /lieu maison de MWCoop)
                Retour = "";
            }
            else
            {
                Vector3 feet = p.position - Vector3.up * 1.0f;
                Retour = string.Format(Ci, "{0:F2};{1:F2};{2:F2};{3:F1}", feet.x, feet.y, feet.z, p.eulerAngles.y);
                Jeu.Teleport(Salle.Entree, 0f);
                RefreshCars(true);
                Jeu.Say("Showroom : le tableau est derrière toi (projecteurs, voiture). " + "Touche " + KeyName() + " pour sortir.", "Showroom: the panel is behind you (spotlights, car). Press the showroom key " + KeyName() + " to leave.");
            }
        }

        string KeyName() { return Jeu.T("du showroom (F8 par défaut)", "(F8 by default)"); }

        bool ParseRetour(out Vector3 v, out float yaw)
        {
            v = Vector3.zero; yaw = 0f;
            string[] s = (Retour ?? "").Split(';');
            float x, y, z;
            if (s.Length < 4 || !float.TryParse(s[0], NumberStyles.Float, Ci, out x) || !float.TryParse(s[1], NumberStyles.Float, Ci, out y) || !float.TryParse(s[2], NumberStyles.Float, Ci, out z) || !float.TryParse(s[3], NumberStyles.Float, Ci, out yaw)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        // ---------------------------------------------------------------- tableau
        void Aim()
        {
            Camera c = Camera.main;
            Salle.Bouton hit = null;
            if (c != null && Salle.Inside(c.transform.position))
            {
                Ray ray = c.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                float best = Jeu.Reach; RaycastHit h;
                foreach (Salle.Bouton b in Salle.Boutons) if (b.Col.Raycast(ray, out h, best)) { best = h.distance; hit = b; }
            }
            string forced = Essai.TakeButton();
            if (forced != null) { hit = Salle.Boutons.Find(b => (b.Act + b.Index) == forced || b.Act == forced); if (hit != null) Press(hit); return; }
            if (hit == null) { if (hand) { Jeu.ClearHand(); hand = false; } return; }
            hand = true;
            Jeu.GuiUse.Value = true;
            Jeu.GuiText.Value = Label(hit);
            float now = Time.realtimeSinceStartup;
            if (Input.GetMouseButtonDown(0) && now - lastClick > 0.25f) { lastClick = now; Press(hit); }
        }

        string Label(Salle.Bouton b)
        {
            switch (b.Act)
            {
                case "spot": return Jeu.T("PROJECTEUR ", "SPOTLIGHT ") + (b.Index + 1) + (Spots[b.Index] == '1' ? Jeu.T(" : ÉTEINDRE", ": OFF") : Jeu.T(" : ALLUMER", ": ON"));
                case "tout": return Spots.Contains("1") ? Jeu.T("TOUT ÉTEINDRE", "ALL OFF") : Jeu.T("TOUT ALLUMER", "ALL ON");
                case "prec": return Jeu.T("VOITURE PRÉCÉDENTE", "PREVIOUS CAR");
                case "suiv": return Jeu.T("VOITURE SUIVANTE", "NEXT CAR");
                default: return cars.Count > 0 ? Jeu.T("SORTIR : ", "BRING: ") + cars[sel].Value.ToUpperInvariant() : Jeu.T("AUCUNE VOITURE DU F10", "NO F10 CAR");
            }
        }

        void Press(Salle.Bouton b)
        {
            char[] s = Spots.ToCharArray();
            switch (b.Act)
            {
                case "spot": s[b.Index] = s[b.Index] == '1' ? '0' : '1'; break;
                case "tout": { char v = Spots.Contains("1") ? '0' : '1'; for (int i = 0; i < 5; i++) s[i] = v; break; }
                case "prec": if (cars.Count > 0) { sel = (sel + cars.Count - 1) % cars.Count; Choix = cars[sel].Key; ShowName(); } return;
                case "suiv": if (cars.Count > 0) { sel = (sel + 1) % cars.Count; Choix = cars[sel].Key; ShowName(); } return;
                case "sortir": Bring(); return;
            }
            Spots = new string(s);
            for (int i = 0; i < 5; i++) Salle.SetSpot(i, Spots[i] == '1');
            Journal.Info("projecteurs " + Spots);
        }

        void Bring()
        {
            if (cars.Count == 0)
            {
                if (!Coop.Present) Jeu.Say("Il faut MWCoop pour sortir une voiture du F10.", "MWCoop is needed to bring an F10 car.");
                else if (!Coop.HasCars) Jeu.Say("MWCoop trop ancien : mets-le à jour (msc-1.15.25 ou plus).", "MWCoop is too old: update it (msc-1.15.25 or newer).");
                else Jeu.Say("Aucune voiture du F10 (VAZ-2108 coupée dans le lanceur ?).", "No F10 car (VAZ-2108 off in the launcher?).");
                return;
            }
            Choix = cars[sel].Key;
            // (au centre, 0,8 m au-dessus du sol ; de trois quarts face a l'entree)
            bool ok = Coop.Show(Choix, Salle.C + new Vector3(0f, 0.8f, 0f), 210f);
            Journal.Info("sortie de " + cars[sel].Value + " (n " + Choix + ") : " + (ok ? "demandee" : "refusee"));
        }

        // ---------------------------------------------------------------- lumiere : dedans, seuls les projecteurs eclairent
        void LateUpdate()
        {
            Camera c = Camera.main;
            bool d = c != null && Salle.Inside(c.transform.position);
            if (d && !dedans)
            {
                dedans = true;
                fog = RenderSettings.fog; ambient = RenderSettings.ambientLight; ambientIntensity = RenderSettings.ambientIntensity; ambientMode = RenderSettings.ambientMode;
                soleils.Clear();
                foreach (Light l in FindObjectsOfType<Light>())
                    if (l.enabled && l.type == LightType.Directional && !l.transform.IsChildOf(transform)) { soleils.Add(l); l.enabled = false; }
                Journal.Info("dedans : " + soleils.Count + " lumiere(s) du jeu coupee(s), ambiance " + ambientMode + " " + ambient + " x" + ambientIntensity);
            }
            else if (!d && dedans)
            {
                dedans = false;
                foreach (Light l in soleils) if (l != null) l.enabled = true;
                soleils.Clear();
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient; RenderSettings.ambientIntensity = ambientIntensity;
                Journal.Info("dehors : lumieres du jeu rendues");
            }
            if (dedans)
            {
                // (le jeu les remet a chaque image : jour, nuit, meteo)
                foreach (Light l in soleils) if (l != null && l.enabled) l.enabled = false;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;   // (le ciel du jeu ne compte plus : couleur unie)
                RenderSettings.ambientLight = new Color(0.30f, 0.30f, 0.32f); RenderSettings.ambientIntensity = 1f;   // (murs gris fonce visibles ; le jeu est a 0,28 le jour)
            }
        }

        void OnDestroy()
        {
            foreach (Light l in soleils) if (l != null) l.enabled = true;
            if (dedans) { RenderSettings.fog = fog; RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient; RenderSettings.ambientIntensity = ambientIntensity; }
        }
    }
}
