using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Rallye avec la VAZ-2108 (demande de JD, 18/10 : « comme pour la course sur glace », un onglet Rallye au F10).
    //  - feuille (Sheets/RallyRegistration/Functions/ButtonConfirm 'Button', donnee par Fleetari a sa tente, RegisterRally :
    //    samedi 8-12 h) : 'State 2' a 'State 9' relisent l'inspection de la Rivett (extincteur, arceau, harnais, casque,
    //    pneus, blindage, chauffage, controle technique), puis 'Check status'. Accroche au debut de 'State 2' : la VAZ du
    //    joueur d'ici a moins de 40 m de la tente -> le reste de l'etat coupe le temps du passage, evenement MWCOOP_VAZ ->
    //    'Class?' (classe, 1000 mk, nom, inscription 'Saturday' : REGISTER comme le jeu). Nom du pilote : la globale
    //    PlayerName vaut le pseudo tant que la feuille est ouverte (comme la course sur glace) ;
    //  - speciales : le depart (SS1-3 StartArea/StartLineObjects/RallyTree 'Clock' : 'Check car', voiture a moins de 6 m de
    //    CarCheck) et le parc ferme (RACES/RALLY/ParcFerme 'Logic' : voiture a moins de 25-50 m) lisent la variable CAR
    //    (la CORRIS) : elle vaut la VAZ du joueur d'ici tant qu'il est inscrit avec elle. Les points de passage (tag
    //    Player, PlayerCurrentVehicle non vide) comptent n'importe quelle voiture conduite ;
    //  - garde par monde et par semaine (rallye-vaz.ini) ; remis a zero a la semaine suivante.
    public static partial class Rally
    {
        const float FleetariDist = 40f;
        static readonly string[] CarFsms = { "RACES/RALLY/SS1/StartArea/StartLineObjects/RallyTree", "RACES/RALLY/SS2/StartArea/StartLineObjects/RallyTree",
                                             "RACES/RALLY/SS3/StartArea/StartLineObjects/RallyTree", "RACES/RALLY/ParcFerme" };
        static readonly string[] CarFsmNames = { "Clock", "Clock", "Clock", "Logic" };
        static bool vazMine, vazRestored, formHooked, nameSet, vazPassed;
        static int vazWeek = -1, mutedFrame;
        static float nextCar, nextFormLook, nextTentToast;
        static PlayMakerFSM form;
        static Transform tent;
        static FsmEvent vazEvent;
        static string nameWas, carLogged = "";
        static readonly List<FsmStateAction> formMuted = new List<FsmStateAction>();

        static void VazReset() { vazMine = vazRestored = formHooked = nameSet = vazPassed = false; vazWeek = -1; form = null; tent = null; formMuted.Clear(); carLogged = ""; nextCar = nextFormLook = 0f; }

        static int Week() { FsmInt wk = FsmVariables.GlobalVariables.FindFsmInt("GlobalWeeksPassed"); return wk != null ? wk.Value : 0; }
        static string VazPath { get { return System.IO.Path.Combine(Log.DataDir, "rallye-vaz.ini"); } }
        static string VazKey { get { return World() + "|" + Week() + "="; } }

        static void VazStore()
        {
            var lines = new List<string>();
            if (System.IO.File.Exists(VazPath)) foreach (string l in System.IO.File.ReadAllLines(VazPath)) if (!l.StartsWith(VazKey)) lines.Add(l);
            if (vazMine) lines.Add(VazKey + "1");
            try { System.IO.File.WriteAllLines(VazPath, lines.ToArray()); } catch { }
        }

        static void VazTick(float now)
        {
            int w = Week();
            if (w != vazWeek)
            {
                if (vazWeek >= 0 && vazMine) Log.Info("rallye : semaine " + w + " : inscription avec la VAZ remise a zero");
                if (vazWeek >= 0) vazMine = false;
                vazWeek = w;
            }
            if (!vazRestored && data != null)
            {
                vazRestored = true;
                if (Bool("Registered") && System.IO.File.Exists(VazPath))
                    foreach (string l in System.IO.File.ReadAllLines(VazPath)) if (l == VazKey + "1") { vazMine = true; Log.Info("rallye : inscription avec ma VAZ reprise (semaine " + w + ")"); }
            }
            HookForm(now);
            FormName();
            if (formMuted.Count > 0 && Time.frameCount > mutedFrame) { foreach (FsmStateAction a in formMuted) a.Enabled = true; formMuted.Clear(); }
            if (now >= nextCar) { nextCar = now + 1f; PointCar(); }
        }

        // Variable CAR du depart des speciales et du parc ferme : la VAZ d'ici si inscrit avec elle, sinon la CORRIS.
        static void PointCar()
        {
            Transform vaz = Vaz.CarOf(Session.LocalId);
            bool useVaz = vazMine && vaz != null && data != null && Bool("Registered");
            GameObject want = useVaz ? vaz.gameObject : Game.FindAny("CORRIS");
            if (want == null) return;
            int changed = 0;
            for (int i = 0; i < CarFsms.Length; i++)
            {
                PlayMakerFSM f = Game.FindFsm(CarFsms[i], CarFsmNames[i]);
                FsmGameObject car = f != null ? f.FsmVariables.FindFsmGameObject("CAR") : null;
                if (car == null || car.Value == want) continue;
                car.Value = want; changed++;
            }
            string key = want.name;
            if (changed > 0 && key != carLogged) { carLogged = key; Log.Info("rallye : depart des speciales et parc ferme suivent " + key + " (" + changed + " automates)"); }
        }

        // ---------------------------------------------------------------- feuille d'inscription
        class FormHook : ModHook
        {
            public override string Module { get { return "courses"; } }
            public override void OnEnter()
            {
                try
                {
                    if (VazAtFleetari())
                    {
                        foreach (FsmStateAction a in State.Actions) if (a != null && a != this && a.Enabled) { a.Enabled = false; formMuted.Add(a); }
                        mutedFrame = Time.frameCount;
                        vazPassed = true;
                        Fsm.Event(vazEvent);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Apres l'inscription (ResultsWeekend 'State 1', OnRegistered) : avec la VAZ -> garde.
        static void VazRegistered()
        {
            if (!vazPassed) return;
            vazPassed = false;
            vazMine = true;
            VazStore();
            nextCar = 0f;
            Hud.Toast(Lang.T("Rallye : inscrit avec ta VAZ (départs et parc fermé avec elle)", "Rally: registered with your VAZ (starts and parc ferme with it)"));
            Log.Info("rallye : inscrit avec ma VAZ");
        }

        static Transform Tent()
        {
            if (tent != null) return tent;
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                if (f.FsmName == "Activate" && f.gameObject.name == "RegisterRally" && f.gameObject.hideFlags == HideFlags.None) { tent = f.transform; break; }
            return tent;
        }

        static bool VazAtFleetari()
        {
            Transform car = Vaz.CarOf(Session.LocalId), t = Tent();
            if (car == null || t == null) return false;
            float d = Vector3.Distance(car.position, t.position);
            if (d >= FleetariDist && !(Config.Testing && Config.GetInt("Test", "VazPartout", 0) != 0))
            {
                Log.Info("rallye : feuille d'inscription : ma VAZ est a " + d.ToString("F0") + " m de la tente de Fleetari (il faut moins de " + FleetariDist + " m)");
                if (Time.realtimeSinceStartup >= nextTentToast)
                {
                    nextTentToast = Time.realtimeSinceStartup + 10f;
                    Hud.Toast(Lang.T("Ta VAZ est à ", "Your VAZ is ") + d.ToString("F0") + Lang.T(" m de la tente de Fleetari : amène-la à côté (moins de 40 m) pour courir avec elle",
                                                                                           " m from Fleetari's tent: bring it next to it (under 40 m) to race with it"));
                }
                return false;
            }
            Log.Info("rallye : feuille d'inscription : ma VAZ a " + d.ToString("F1") + " m de la tente, inspection de la Rivett sautee");
            return true;
        }

        static void HookForm(float now)
        {
            if (formHooked) return;
            if (form == null)
            {
                if (now < nextFormLook) return;
                nextFormLook = now + 3f;
                GameObject go = Game.FindAny("Sheets/RallyRegistration/Functions/ButtonConfirm");
                form = go != null ? Game.FsmOn(go, "Button") : null;
                if (form == null)
                    foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                        if (f.FsmName == "Button" && f.gameObject.name == "ButtonConfirm" && f.transform.parent != null && f.transform.parent.parent != null
                            && f.transform.parent.parent.name == "RallyRegistration" && f.gameObject.hideFlags == HideFlags.None) { form = f; break; }
                if (form == null) return;
                Log.Info("rallye : feuille d'inscription trouvee (" + (form.gameObject.activeInHierarchy ? "ouverte" : "rangee") + ")");
            }
            if (string.IsNullOrEmpty(form.ActiveStateName)) return;   // (jamais ouverte : automate pas demarre)
            formHooked = true;
            FsmState s = form.Fsm.GetState("State 2"), cls = form.Fsm.GetState("Class?");
            if (s == null || cls == null) { Log.Warn("rallye : feuille d'inscription : etats 'State 2' / 'Class?' absents, VAZ pas reconnue"); return; }
            vazEvent = FsmEvent.GetFsmEvent("MWCOOP_VAZ");
            var tr = new FsmTransition { FsmEvent = vazEvent, ToState = "Class?" };
            System.Reflection.PropertyInfo tp = typeof(FsmTransition).GetProperty("ToFsmState");
            if (tp != null && tp.CanWrite) tp.SetValue(tr, cls, null);
            var list = new List<FsmTransition>(s.Transitions ?? new FsmTransition[0]);
            list.Add(tr);
            s.Transitions = list.ToArray();
            FsmHooks.Prepend(s, new FormHook());
            Log.Info("rallye : feuille d'inscription accrochee (VAZ acceptee a moins de " + FleetariDist + " m de la tente de Fleetari)");
        }

        static void FormName()
        {
            if (form == null || form.transform.parent == null || form.transform.parent.parent == null) return;
            bool open = form.transform.parent.parent.gameObject.activeInHierarchy;
            FsmString pn = FsmVariables.GlobalVariables.FindFsmString("PlayerName");
            if (pn == null) return;
            string me = Session.Me != null && !string.IsNullOrEmpty(Session.Me.Name) ? Session.Me.Name : null;
            if (open && me != null && !nameSet) { nameWas = pn.Value; nameSet = true; pn.Value = me; Log.Info("rallye : feuille d'inscription : nom du pilote " + me); }
            else if (!open && nameSet) { nameSet = false; pn.Value = nameWas ?? ""; }
        }

        // ---------------------------------------------------------------- etat pour l'interface (RacePanel)
        public class Entrant { public string Name, Cls; public bool Registered, Me; public int Stages; public float Total; }
        public class Standing { public int Pos; public string Name; public float Total; public bool Me, Human; }

        public static bool Ready { get { return data != null; } }
        public static bool MeRegistered { get { return data != null && Bool("Registered"); } }
        public static bool MeWithVaz { get { return MeRegistered && vazMine; } }
        public static string MyClass { get { return data != null ? Cls() : null; } }
        public static int MyStages { get { if (data == null || playerStages == null) return 0; int n = 0; int m = Mask(); for (int i = 0; i < 3; i++) if ((m & (1 << i)) != 0) n++; return n; } }
        public static float MyTotal { get { FsmFloat t = data != null ? F("PlayerTimeTotal") : null; return t != null ? t.Value : 0f; } }
        // heures de depart SS1, SS2, SS3 (0 : pas encore donnees)
        public static int[] MyStarts
        {
            get
            {
                var r = new int[3];
                if (data == null) return r;
                FsmInt a = I("TimeSS1"), b = I("TimeSS2"), c = I("TimeSS3");
                r[0] = a != null ? a.Value : 0; r[1] = b != null ? b.Value : 0; r[2] = c != null ? c.Value : 0;
                return r;
            }
        }

        public static List<Entrant> Entrants()
        {
            var l = new List<Entrant>();
            foreach (Human h in humans.Values)
            {
                int n = 0; for (int i = 0; i < 3; i++) if ((h.Mask & (1 << i)) != 0) n++;
                l.Add(new Entrant { Name = h.Name, Cls = h.Cls, Registered = h.Registered, Me = h.Id == Session.LocalId, Stages = n, Total = h.Total });
            }
            if (l.Count == 0 && data != null)
                l.Add(new Entrant { Name = Session.Me != null ? Session.Me.Name : "?", Cls = Cls(), Registered = Bool("Registered"), Me = true, Stages = MyStages, Total = MyTotal });
            l.Sort(delegate(Entrant a, Entrant b) { if (a.Registered != b.Registered) return a.Registered ? -1 : 1; if (a.Stages != b.Stages) return b.Stages.CompareTo(a.Stages); return a.Total.CompareTo(b.Total); });
            return l;
        }

        // Classement de ma classe apres les speciales que j'ai faites : adversaires communs (meme tirage que le jeu chez tous),
        // moi, les autres humains de ma classe au meme nombre de speciales.
        public static List<Standing> Standings()
        {
            var l = new List<Standing>();
            if (data == null) return l;
            string cls = Cls();
            int mask = Mask();
            if (mask == 0) return l;
            foreach (KeyValuePair<string, float[]> o in Opponents(cls))
            {
                float total = 0; for (int i = 0; i < 3; i++) if ((mask & (1 << i)) != 0) total += o.Value[i];
                l.Add(new Standing { Name = o.Key, Total = total });
            }
            l.Add(new Standing { Name = Session.Me != null ? Session.Me.Name : "?", Total = MyTotal, Me = true, Human = true });
            foreach (Human h in humans.Values) if (h.Id != Session.LocalId && h.Cls == cls && h.Mask == mask) l.Add(new Standing { Name = h.Name, Total = h.Total, Human = true });
            l.Sort(delegate(Standing a, Standing b) { return a.Total.CompareTo(b.Total); });
            for (int i = 0; i < l.Count; i++) l[i].Pos = i + 1;
            return l;
        }

        // ---------------------------------------------------------------- essai
        // Autotest « rallyevaz » : samedi 9 h d'une semaine de rallye (GlobalWeeksPassed pair) posee a 12 s, VAZ prise a 16 s,
        // joueur et VAZ (Faire venir) a la tente de Fleetari a 30 s, feuille ouverte a 40 s et confirmee a 43 s ; bilan a 55 s
        // (inscrit, VAZ, classe, departs, variables CAR). [Test] VazPartout=1 : distance ignoree.
        static int vazTest;

        // Autotest « sauverallye » : prepare une partie pour essayer le rallye (demande de JD, 18/10) -- a 15 s semaine de
        // rallye (GlobalWeeksPassed pair), samedi 8 h, argent porte a 3000 mk au moins, joueur a 4 m de la tente de Fleetari
        // (face a elle) ; a 24 s sauvegarde comme le jeu (Game.SaveInPlace). Autotest « lieu » : jour, heure, semaine, argent et
        // place du joueur a 15 s (verification apres rechargement).
        static int saveTest;
        static int GInt(string n) { FsmInt v = FsmVariables.GlobalVariables.FindFsmInt(n); return v != null ? v.Value : -1; }
        public static void TestSave(string mode, float t)
        {
            if (mode == "lieu" && saveTest == 0 && t > 15f)
            {
                saveTest = 1; GameObject pl = GameObject.Find("PLAYER"); Transform tt = Tent();
                Log.Info("autotest lieu : semaine " + Week() + ", jour " + GInt("GlobalDay") + ", " + GInt("GlobalHour") + " h, argent " + Game.GlobalFloat("PlayerMoney").ToString("F0")
                         + ", joueur " + (pl != null ? pl.transform.position.ToString("F1") : "?") + (pl != null && tt != null ? " a " + Vector3.Distance(pl.transform.position, tt.position).ToString("F1") + " m de la tente de Fleetari" : ""));
            }
            if (mode != "sauverallye") return;
            if (saveTest == 0 && t > 15f)
            {
                saveTest = 1;
                FsmInt wk = FsmVariables.GlobalVariables.FindFsmInt("GlobalWeeksPassed");
                if (wk != null && wk.Value % 2 != 0) wk.Value++;
                FsmInt gd = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay"); if (gd != null) gd.Value = 6;
                PlayMakerFSM sun = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                if (sun != null) { sun.FsmVariables.GetFsmInt("Time").Value = 8; sun.SendEvent("TIMESKIP"); }
                PlayMakerFSM sw = Game.FindFsm("RACES", "SwapDates"); if (sw != null) sw.SendEvent("WAKEUP");
                FsmFloat money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney"); if (money != null && money.Value < 3000f) money.Value = 3000f;
                Transform tt = Tent(); GameObject pl = GameObject.Find("PLAYER");
                if (tt != null && pl != null)
                {
                    Vector3 back = Vector3.ProjectOnPlane(tt.forward, Vector3.up).normalized; if (back.sqrMagnitude < 0.1f) back = Vector3.forward;
                    Vector3 pos = tt.position + back * 4f + Vector3.up * 0.5f;
                    pl.transform.position = pos; pl.transform.rotation = Quaternion.LookRotation(-back, Vector3.up);
                }
                Log.Info("autotest sauverallye : semaine " + (wk != null ? wk.Value : -1) + ", samedi 8 h, argent " + (money != null ? money.Value.ToString("F0") : "?") + ", joueur a " + (tt != null ? tt.position.ToString("F0") : "?"));
            }
            if (saveTest == 1 && t > 24f) { saveTest = 2; Game.SaveInPlace(); Log.Info("autotest sauverallye : sauvegarde envoyee"); }
        }
        static void TestVaz(float t)
        {
            if (vazTest == 0 && t > 12f && Session.IsHost)
            {
                vazTest = 1;
                FsmInt wk = FsmVariables.GlobalVariables.FindFsmInt("GlobalWeeksPassed");
                if (wk != null && wk.Value % 2 != 0) wk.Value++;
                FsmInt gd = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay"); if (gd != null) gd.Value = 6;
                PlayMakerFSM sun = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                if (sun != null) { sun.FsmVariables.GetFsmInt("Time").Value = 9; sun.SendEvent("TIMESKIP"); }
                PlayMakerFSM sw = Game.FindFsm("RACES", "SwapDates");
                if (sw != null) sw.SendEvent("WAKEUP");
                Log.Info("autotest rallyevaz : semaine " + (wk != null ? wk.Value : -1) + ", samedi 9 h");
            }
            if (vazTest <= 1 && t > 16f) { vazTest = 2; Log.Info("autotest rallyevaz : VAZ -> " + Vaz.Request(Session.IsHost ? 3 : 5)); }
            if (vazTest == 2 && t > 30f)
            {
                vazTest = 3;
                Transform tt = Tent(); GameObject pl = GameObject.Find("PLAYER");
                if (tt != null && pl != null) pl.transform.position = tt.position + (Session.IsHost ? Vector3.right : Vector3.left) * 3f + Vector3.up * 1f;
                FsmFloat money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
                if (money != null && money.Value < 2000f) money.Value = 3000f;
                Log.Info("autotest rallyevaz : joueur a la tente de Fleetari " + (tt != null ? tt.position.ToString("F0") : "?") + ", suivi " + Ready);
            }
            if (vazTest == 3 && t > 33f) { vazTest = 4; Vaz.Call(); }
            if (vazTest == 4 && t > 40f)
            {
                vazTest = 5;
                PlayMakerFSM reg = null;
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>()) if (f.FsmName == "Activate" && f.gameObject.name == "RegisterRally" && f.gameObject.hideFlags == HideFlags.None) reg = f;
                if (reg != null && !string.IsNullOrEmpty(reg.ActiveStateName)) { Game.SetState(reg, "Open form"); Log.Info("autotest rallyevaz : feuille ouverte par Fleetari"); }
                else { GameObject fo = Game.FindAny("Sheets/RallyRegistration"); if (fo != null) fo.SetActive(true); Log.Info("autotest rallyevaz : feuille ouverte a la main (Fleetari " + (reg != null ? "'" + reg.ActiveStateName + "'" : "introuvable") + ")"); }
            }
            if (vazTest == 5 && t > 43f)
            {
                vazTest = 6;
                if (form != null) Game.SetState(form, "State 2");
                Autotest.CaptureSoon("rallye-feuille", 1f);
                Log.Info("autotest rallyevaz : confirmer -> " + (form != null ? form.ActiveStateName : "?"));
            }
            if (vazTest == 6 && t > 55f)
            {
                vazTest = 7;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < CarFsms.Length; i++) { PlayMakerFSM f = Game.FindFsm(CarFsms[i], CarFsmNames[i]); FsmGameObject c = f != null ? f.FsmVariables.FindFsmGameObject("CAR") : null; sb.Append(' ').Append(i < 3 ? "SS" + (i + 1) : "parc").Append('=').Append(c != null && c.Value != null ? c.Value.name : "?"); }
                int[] st = MyStarts;
                Log.Info("autotest rallyevaz : inscrit " + MeRegistered + ", VAZ " + vazMine + ", classe " + MyClass + ", departs " + st[0] + "/" + st[1] + "/" + st[2] + " ; CAR" + sb);
                Menu.Show(Menu.TabRally); Autotest.CaptureSoon("rallye-f10", 1.5f);
            }
            if (vazTest == 7 && t > 60f) { vazTest = 8; Menu.Show(Menu.TabRace); Autotest.CaptureSoon("glace-horssemaine", 1.5f); }
            if (vazTest == 8 && t > 64f) { vazTest = 9; Menu.Open = false; }
        }
    }
}
