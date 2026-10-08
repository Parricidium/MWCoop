using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Reapparition en coop (demande de JD) : la mort, quelle qu'elle soit, passe par Systems/Death, active par
    // ce qui tue (un booleen de cause + ActivateGameObject). Son automate 'Activate Dead Body' detruit ensuite
    // les commandes du joueur ("State 3" : FPSInputController, CharacterMotor, CharacterController), joue la mort,
    // sauvegarde puis recharge le MENU : en coop, le joueur quittait la partie (l'hote : tout le monde).
    // Ici, un composant pose sur Systems/Death (son OnEnable passe a l'activation, avant le Start de l'automate)
    // coupe l'automate avant son premier etat, et l'objet est desactive a l'image suivante (une action ajoutee
    // ne tient pas : l'automate d'un objet jamais active n'a pas encore charge ses actions) ; le joueur choisit ou revenir : l'appartement (le depart) ou la maison
    // des parents. Besoins remis comme le jeu le fait a la mort ("Mute audio"), sorti du vehicule, teleporte.
    // Accident de voiture : la liaison de la tete du conducteur (DriverHeadPivot, ConfigurableJoint) a casse et
    // le mannequin de la voiture (DeadBody) a ete ejecte : liaison recreee a l'identique (notee au depart),
    // mannequin range. Hors coop (solo), la mort du jeu est inchangee. [Coop] Reapparition=0 : la desactive.
    public static class Respawn
    {
        public static bool Choosing;
        static string cause = "";
        static PlayMakerFSM deathFsm;
        static float scanAt = -1, deactivateAt = -1, restoreAt = -1;

        // Lieux : point au sol cherche par un rayon vers le bas depuis une hauteur sous le plafond.
        struct Place { public string Name, NameEn; public Vector3 Top; }
        static readonly Place[] places = {
            new Place { Name = "l'appartement", NameEn = "the apartment", Top = new Vector3(-1285.5f, 1.2f, 1076.4f) },
            new Place { Name = "la maison des parents", NameEn = "the parents' house", Top = new Vector3(-6.2f, 1.0f, 6.6f) },
        };

        // Accident : liaison de la tete et mannequin de chaque voiture, notes au depart.
        class Head
        {
            public string Car; public Rigidbody Pivot; public Transform PivotParent; public Vector3 PivotPos; public Quaternion PivotRot;
            public Dictionary<System.Reflection.PropertyInfo, object> Joint = new Dictionary<System.Reflection.PropertyInfo, object>();
            public PlayMakerFSM Fsm; public GameObject DeadBody; public Transform DeadParent; public Vector3 DeadPos; public Quaternion DeadRot;
        }
        static readonly List<Head> heads = new List<Head>();
        static readonly List<KeyValuePair<Behaviour, bool>> overlays = new List<KeyValuePair<Behaviour, bool>>();

        // Pose sur Systems/Death : son OnEnable passe a l'activation, avant le Start de l'automate du jeu.
        public class Guard : MonoBehaviour
        {
            void OnEnable()
            {
                try { if (Intercept()) { deathFsm.enabled = false; deactivateAt = Time.realtimeSinceStartup; } }
                catch (System.Exception e) { MWCoop.Log.Error("reapparition : " + e); }
            }
        }

        public static void OnLevelLoaded()
        {
            Choosing = false; deathFsm = null; heads.Clear(); overlays.Clear();
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 10f : -1;
        }

        static bool Enabled { get { return Session.Active && Config.GetInt("Coop", "Reapparition", 1) != 0; } }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (scanAt > 0 && now >= scanAt) { scanAt = -1; Scan(); }
            if (deactivateAt > 0 && now >= deactivateAt)
            {
                deactivateAt = -1;
                if (deathFsm != null) { deathFsm.gameObject.SetActive(false); deathFsm.enabled = true; }
            }
            // Accident : la tete rattachee des que l'automate de la voiture a fini (sinon la vue tombe pendant le choix).
            if (restoreAt > 0 && now >= restoreAt) { restoreAt = -1; foreach (Head h in heads) RestoreHead(h); }
            TestUpdate();
            if (!Choosing) return;
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) Choose(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) Choose(1);
        }

        static void Scan()
        {
            GameObject death = Game.FindAny("Systems/Death");
            deathFsm = death != null ? Game.FsmOn(death, "Activate Dead Body") : null;
            if (deathFsm == null) { Log.Warn("reapparition : Systems/Death introuvable"); return; }
            if (death.GetComponent<Guard>() == null) death.AddComponent<Guard>();
            // Accident : tetes et mannequins des voitures.
            foreach (Rigidbody car in Object.FindObjectsOfType<Rigidbody>())
            {
                if (car.transform.parent != null || car.GetComponent("CarDynamics") == null) continue;
                foreach (ConfigurableJoint j in car.GetComponentsInChildren<ConfigurableJoint>(true))
                {
                    if (j.gameObject.name != "DriverHeadPivot") continue;
                    var h = new Head { Car = car.name, Pivot = j.GetComponent<Rigidbody>(), PivotParent = j.transform.parent, PivotPos = j.transform.localPosition, PivotRot = j.transform.localRotation };
                    foreach (System.Reflection.PropertyInfo p in typeof(ConfigurableJoint).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (!p.CanRead || !p.CanWrite || p.DeclaringType == typeof(Component) || p.DeclaringType == typeof(Object) || p.GetIndexParameters().Length > 0) continue;
                        try { h.Joint[p] = p.GetValue(j, null); } catch { }
                    }
                    h.Fsm = Game.FsmOn(j.gameObject, "Death");
                    FsmGameObject db = h.Fsm != null ? h.Fsm.FsmVariables.FindFsmGameObject("DeadBody") : null;
                    if (db != null && db.Value != null) { h.DeadBody = db.Value; h.DeadParent = db.Value.transform.parent; h.DeadPos = db.Value.transform.localPosition; h.DeadRot = db.Value.transform.localRotation; }
                    heads.Add(h);
                }
            }
            // Voile noir que la mort allume (ScreenOverlay de la camera) : etat normal note.
            GameObject cam = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
            if (cam != null) foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b.GetType().Name == "ScreenOverlay") overlays.Add(new KeyValuePair<Behaviour, bool>(b, b.enabled));
            Log.Info("reapparition : prete (" + heads.Count + " tetes de conducteur)");
        }

        // La mort vient d'activer Systems/Death : on la garde pour nous si la coop est la.
        static bool Intercept()
        {
            if (!Enabled || deathFsm == null) return false;
            if (Choosing) return true;   // (deja en train de choisir)
            var causes = new List<string>();
            foreach (FsmBool b in deathFsm.FsmVariables.BoolVariables)
                if (b.Value && b.Name != "Smoking") { causes.Add(CauseName(b.Name)); b.Value = false; }
            cause = causes.Count > 0 ? string.Join(", ", causes.ToArray()) : "inconnue";
            Log.Info("reapparition : mort (" + cause + "), mort du jeu arretee");
            Needs();
            restoreAt = Time.realtimeSinceStartup + 0.3f;   // (apres son "State 3" : Wait 0,1 puis "State 1")
            Choosing = true;
            if (Session.Active) Chat.Send(Lang.T("* mort (", "* died (") + cause + ") *");
            if (Config.GetInt("Test", "TestReapparition", 0) > 0) testChoiceAt = Time.realtimeSinceStartup + 3f;
            return true;
        }

        static float testChoiceAt = -1;
        public static void TestUpdate()
        {
            if (testChoiceAt > 0 && Time.realtimeSinceStartup >= testChoiceAt) { testChoiceAt = -1; Choose(Config.GetInt("Test", "TestReapparition", 1) - 1); }
        }

        // Besoins remis comme le jeu le fait a la mort (Activate Dead Body :: Mute audio), plus ce qui tue d'un coup.
        static void Needs()
        {
            Set("PlayerFatigue", 10f); Set("PlayerHunger", 12f); Set("PlayerThirst", 8f); Set("PlayerDrunkAdjusted", 0f); Set("PlayerDrunk", 0f);
            Set("PlayerStress", 0f); Set("PlayerTemp", 75f); Set("PlayerUrine", 0f); Set("PlayerBurns", 0f); Set("PlayerAllergy", 0f);
        }

        static void Set(string name, float v)
        {
            FsmFloat f = FsmVariables.GlobalVariables.FindFsmFloat(name);
            if (f != null) f.Value = v;
        }

        static void Choose(int i)
        {
            if (!Choosing || i < 0 || i >= places.Length) return;
            Choosing = false;
            try { Revive(places[i]); }
            catch (System.Exception e) { MWCoop.Log.Error("reapparition : " + e); }
        }

        static void Revive(Place place)
        {
            GameObject pl = GameObject.Find("PLAYER");
            if (pl == null) return;
            // Accident : liaisons de tete recreees, mannequins ranges (avant de sortir du vehicule).
            foreach (Head h in heads) RestoreHead(h);
            if (Seats.Seated) Seats.TestLeave();
            VehicleSync.ExitLocal();
            if (pl.transform.parent != null) pl.transform.parent = null;
            foreach (KeyValuePair<Behaviour, bool> kv in overlays) if (kv.Key != null) kv.Key.enabled = kv.Value;
            OpenEyes(pl);
            Game.SetGlobalBool("PlayerStop", false);
            var cc = pl.GetComponent<CharacterController>();
            foreach (Behaviour b in pl.GetComponents<Behaviour>())
            {
                string n = b.GetType().Name;
                if (n == "CharacterMotor" || n == "FPSInputController") b.enabled = true;
            }
            Vector3 to = Ground(place, cc);
            if (cc != null) cc.enabled = false;
            pl.transform.position = to;
            pl.transform.rotation = Quaternion.Euler(0f, pl.transform.eulerAngles.y, 0f);
            if (cc != null) cc.enabled = true;
            Needs();
            Log.Info("reapparition : a " + place.Name + " en " + to.ToString("F2"));
            Hud.Toast(Lang.T("De retour \u00E0 " + place.Name, "Back at " + place.NameEn));
            if (Session.Active) Chat.Send("* revient a " + place.Name + " *");
        }

        // Paupieres : l'intoxication au monoxyde (FPSCamera/Carbon/PassoutEyes) ferme deux barres noires devant la
        // camera (animation sleep_on) et baisse le son du jeu (GameVolume -> 0), PUIS active Systems/Death. La mort
        // arretee ici, les paupieres restaient fermees : ecran noir pour toujours apres la reapparition (vraie partie
        // du 06/10). Toute animation d'yeux encore allumee sous la camera est eteinte (comme son etat "State 4"), et
        // le son rendu.
        static void OpenEyes(GameObject pl)
        {
            int n = 0;
            foreach (Animation an in pl.GetComponentsInChildren<Animation>(true))
            {
                GameObject g = an.gameObject;
                if (!g.activeSelf || !g.name.Contains("Eyes")) continue;
                PlayMakerFSM f = Game.FsmOn(g, "Logic");
                if (f != null) f.enabled = false;
                g.SetActive(false);
                if (f != null) f.enabled = true;
                n++;
                Log.Info("reapparition : paupieres " + Recon.Path(g.transform) + " rouvertes");
            }
            FsmFloat vol = FsmVariables.GlobalVariables.FindFsmFloat("GameVolume");
            if (vol != null && vol.Value < 0.99f) { Log.Info("reapparition : son du jeu rendu (" + vol.Value.ToString("F2") + " -> 1)"); vol.Value = 1f; }
        }

        // Point au sol sous le lieu (premier obstacle solide sous la hauteur choisie), pieds du joueur dessus.
        static Vector3 Ground(Place place, CharacterController cc)
        {
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in Physics.RaycastAll(place.Top, Vector3.down, 6f))
                if (h.collider != null && !h.collider.isTrigger && Game.RootName(h.collider.transform) != "PLAYER" && h.point.y > best) best = h.point.y;
            float floor = best > float.NegativeInfinity ? best : place.Top.y - 1.2f;
            float feet = cc != null ? cc.center.y - cc.height / 2f : -0.9f;
            return new Vector3(place.Top.x, floor + 0.05f - feet, place.Top.z);
        }

        static void RestoreHead(Head h)
        {
            if (h.Pivot == null || h.Pivot.GetComponent<ConfigurableJoint>() != null) return;
            // Pose de depart (la reference de la liaison est la pose a sa creation), puis liaison a l'identique.
            h.Pivot.transform.parent = h.PivotParent;
            h.Pivot.transform.localPosition = h.PivotPos; h.Pivot.transform.localRotation = h.PivotRot;
            h.Pivot.position = h.Pivot.transform.position; h.Pivot.rotation = h.Pivot.transform.rotation;
            h.Pivot.velocity = Vector3.zero; h.Pivot.angularVelocity = Vector3.zero;
            var j = h.Pivot.gameObject.AddComponent<ConfigurableJoint>();
            System.Reflection.PropertyInfo cb = null;
            foreach (KeyValuePair<System.Reflection.PropertyInfo, object> kv in h.Joint)
            {
                if (kv.Key.Name == "connectedBody") { cb = kv.Key; continue; }
                try { kv.Key.SetValue(j, kv.Value, null); } catch { }
            }
            if (cb != null) cb.SetValue(j, h.Joint[cb], null);
            if (h.DeadBody != null)
            {
                h.DeadBody.SetActive(false);
                h.DeadBody.transform.parent = h.DeadParent;
                h.DeadBody.transform.localPosition = h.DeadPos; h.DeadBody.transform.localRotation = h.DeadRot;
            }
            if (h.Fsm != null && h.Fsm.Fsm.GetState("Has joint") != null) Game.SetState(h.Fsm, "Has joint");
            Log.Info("reapparition : tete du conducteur de " + h.Car + " rattachee");
        }

        static string CauseName(string v)
        {
            switch (v)
            {
                case "Crash": return Lang.T("accident", "crash");
                case "RunOver": case "RunOverRally": return Lang.T("renvers\u00E9", "run over");
                case "Train": return "train";
                case "Sewage": return Lang.T("fosse septique", "septic tank");
                case "Murder": return Lang.T("meurtre", "murder");
                case "Fatigue": return Lang.T("\u00E9puisement", "exhaustion");
                case "Hunger": return Lang.T("faim", "hunger");
                case "Thirst": return Lang.T("soif", "thirst");
                case "Urine": return Lang.T("vessie", "bladder");
                case "Stress": return "stress";
                case "Gasolinefire": case "Burn": return Lang.T("br\u00FBl\u00E9", "burnt");
                case "DrunkDrown": case "Drown": return Lang.T("noyade", "drowned");
                case "Electrocute": case "PissTV": return Lang.T("\u00E9lectrocution", "electrocuted");
                case "InJail": return Lang.T("prison", "jail");
                case "Hypothermia": return Lang.T("froid", "cold");
                case "PTO": case "CutterBlade": return Lang.T("machine agricole", "farm machine");
                case "Carbon": return Lang.T("monoxyde de carbone", "carbon monoxide");
                case "HeartAttack": return Lang.T("crise cardiaque", "heart attack");
            }
            return v;
        }

        // ------------------------------------------------------------ choix
        // Carte verre (habillage de Style) ; 1 / 2 au clavier, ou clic.
        public static void Draw()
        {
            if (!Choosing) return;
            Style.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.10f, 0.01f, 0.03f, 0.55f));
            float w = Mathf.Min(Style.Px(600), Screen.width - Style.Px(32)), h = Style.Px(330), pad = Style.Px(30);
            float x = Mathf.Round((Screen.width - w) / 2), y = Mathf.Round((Screen.height - h) / 2);
            Style.Glass(new Rect(x, y, w, h), Style.Px(24));
            Style.Title(new Rect(x, y + Style.Px(26), w, Style.Px(52)), Lang.T("Vous \u00EAtes mort", "You are dead"), Style.White, TextAnchor.MiddleCenter, 38);
            Style.Text(new Rect(x + pad, y + Style.Px(88), w - 2 * pad, Style.Px(26)), Lang.T("Cause : ", "Cause: ") + cause, 18, TextAnchor.MiddleCenter, Style.Warn, false);
            Style.Text(new Rect(x + pad, y + Style.Px(122), w - 2 * pad, Style.Px(26)), Lang.T("O\u00F9 voulez-vous r\u00E9appara\u00EEtre ?", "Where do you want to come back?"), 18, TextAnchor.MiddleCenter, Style.Dim, false);
            float bw = (w - 2 * pad - Style.Px(14)) / 2, by = y + Style.Px(172), bh = Style.Px(58);
            if (Style.Button(new Rect(x + pad, by, bw, bh), Lang.T("1  \u00B7  L'appartement", "1  \u00B7  The apartment"), true, 19)) Choose(0);
            if (Style.Button(new Rect(x + pad + bw + Style.Px(14), by, bw, bh), Lang.T("2  \u00B7  Chez les parents", "2  \u00B7  Parents' house"), true, 19)) Choose(1);
            Style.Text(new Rect(x + pad, by + bh + Style.Px(22), w - 2 * pad, Style.Px(24)), Lang.T("La partie continue pour les autres joueurs.", "The game goes on for the other players."), 15, TextAnchor.MiddleCenter, Style.Dim, false);
        }

        // ------------------------------------------------------------ essais
        // Meurt comme le jeu le fait : un booleen de cause, puis Systems/Death active.
        public static string TestDie(string why)
        {
            if (deathFsm == null) return "pas d'automate de mort";
            FsmBool b = deathFsm.FsmVariables.FindFsmBool(why);
            if (b == null) return "cause inconnue : " + why;
            b.Value = true;
            deathFsm.gameObject.SetActive(true);
            return "mort par " + why + (Choosing ? " : arretee, choix ouvert" : " : PAS arretee");
        }

        // Accident : la liaison de la tete du conducteur de 'car' casse (comme sous un choc).
        public static string TestCrash(string car)
        {
            foreach (Head h in heads)
                if (h.Car.StartsWith(car) && h.Pivot != null)
                {
                    var j = h.Pivot.GetComponent<ConfigurableJoint>();
                    if (j == null) return "deja cassee";
                    Object.Destroy(j);
                    return "tete de " + h.Car + " detachee";
                }
            return "pas de tete pour " + car;
        }

        public static string State()
        {
            GameObject pl = GameObject.Find("PLAYER");
            var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
            string s = "choix " + Choosing + ", mort active " + (deathFsm != null && deathFsm.gameObject.activeSelf) + ", joueur " + (pl != null ? pl.transform.position.ToString("F2") : "?")
                       + (pl != null && pl.transform.parent != null ? " dans " + pl.transform.parent.name : "") + ", controleur " + (cc != null ? (cc.enabled ? "actif" : "coupe") : "DETRUIT");
            foreach (Head h in heads) if (h.Pivot != null && h.Pivot.GetComponent<ConfigurableJoint>() == null) s += ", tete de " + h.Car + " detachee";
            return s;
        }
    }
}
