using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Heure, jour et meteo dictes par l'hote (1 fois par seconde).
    //  - heure : MAP/Sun/PivotSun/SUN :: Color, un etat par heure ; sa transition globale TIMESKIP
    //    saute a l'heure de sa variable Time. Rotation du soleil : PivotSun :: Rotation (var Rotation).
    //  - meteo : MAP/WEATHER/Clouds (position glissee par son automate Weather) et CloudObjects
    //    (place au hasard chaque jour) ; la pluie suit les nuages. Forecast :: Logic tire la meteo
    //    de la semaine, Clouds :: Temperature la temperature : chez l'invite, ces trois automates
    //    sont coupes et l'etat de l'hote est applique tel quel.
    //  - sommeil : une heure dure GlobalTimeScale secondes (300 eveille ; le lit la met a 0,5 et
    //    l'heure en cours finit aussitot, l'EaseColor du soleil relit la duree a chaque image).
    //    En coop le temps ne passe vite que quand TOUS les joueurs dorment (chacun doit recuperer sa
    //    fatigue) : le lit met GlobalTimeScale a sa variable TimeScaleSleep, que le mod regle sur tous
    //    les lits (300 tant que quelqu'un est eveille, 0,5 pendant la phase commune). La phase commence
    //    quand le lit de chaque joueur compte les heures, et dure tant qu'un lit les compte encore
    //    (les gros dormeurs finissent leur nuit). L'invite prend l'echelle de l'hote et n'est pas
    //    ramene en arriere pendant l'acceleration.
    //  - evanouissement (ivresse, froid) : Systems/PassOut :: Activate dort comme un lit (State 2 tire
    //    5 a 11 heures ; "Day change" met GlobalTimeScale a SA variable TimeScaleSleep ; "Sleep time"
    //    retire une heure a chaque changement d'heure ; "Calc rates 2" applique faim, soif... et
    //    reveille). Il n'etait pas compte comme un lit : l'echelle etait remise a 300 aussitot (chez
    //    l'hote meme sans invite), et l'evanoui restait 5 a 11 heures de jeu (25 a 55 min) dans le
    //    noir. Il compte maintenant comme un lit ; et si les autres ne dorment pas, au bout de
    //    PassOutWait s il se reveille seul : ses heures restantes lui sont appliquees (fatigue, puis la
    //    faim... de "Calc rates 2" par ABORT, la transition globale du jeu), l'heure commune ne bouge pas.
    public static class World
    {
        static float nextSend;
        static bool muted;
        static Transform clouds, cloudObjects;
        static PlayMakerFSM sunColor, sunRotation, weather, forecast, temperature;
        static bool found;
        const float SleepScale = 0.5f, NormalScale = 300f;
        const float PassOutWait = 30f;
        static bool phase;            // hote : tout le monde dort, le temps file
        static bool hostFast;         // invite : l'hote dit que le temps file
        static float waitToastAt, nextBedScan;
        static readonly System.Collections.Generic.List<PlayMakerFSM> beds = new System.Collections.Generic.List<PlayMakerFSM>();
        static PlayMakerFSM passOut;  // Systems/PassOut :: Activate (aussi dans 'beds')
        static float passOutSince = -1;

        public static void OnLevelLoaded()
        {
            found = muted = phase = hostFast = false;
            beds.Clear();
            passOut = null; passOutSince = -1;
            nextBedScan = 0;
        }

        // Lits (et canape) : SleepTrigger :: Activate ; evanouissement : PassOut :: Activate (memes etats
        // "Day change" / "Sleep time", meme variable TimeScaleSleep). Rescannes toutes les 20 s.
        static void ScanBeds()
        {
            if (Time.realtimeSinceStartup < nextBedScan) return;
            nextBedScan = Time.realtimeSinceStartup + 20f;
            beds.Clear();
            passOut = null;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Activate") continue;
                bool bed = f.gameObject.name == "SleepTrigger", pass = f.gameObject.name == "PassOut";
                if ((bed || pass) && f.FsmVariables.FindFsmFloat("TimeScaleSleep") != null)
                {
                    beds.Add(f);
                    if (pass) passOut = f;
                }
            }
        }

        // Le lit du joueur local compte-t-il les heures (il dort vraiment, animation finie) ?
        public static bool BedCounting()
        {
            ScanBeds();
            foreach (PlayMakerFSM f in beds)
            {
                if (f == null) continue;
                string s = f.ActiveStateName;
                if (s == "Alarm clock?" || s == "Day change" || s == "Sleep time") return true;
            }
            return false;
        }

        static void SetBeds(float v)
        {
            ScanBeds();
            foreach (PlayMakerFSM f in beds)
            {
                if (f == null) continue;
                FsmFloat x = f.FsmVariables.FindFsmFloat("TimeScaleSleep");
                if (x != null && x.Value != v) x.Value = v;
            }
        }

        // Le joueur local dort mais pas les autres : on le lui dit (toutes les 30 s).
        static void WaitToast(bool fast)
        {
            if (fast || Session.RemoteCount == 0 || !Game.GlobalBool("PlayerSleeps") || Time.realtimeSinceStartup < waitToastAt) return;
            waitToastAt = Time.realtimeSinceStartup + 30f;
            Hud.Toast("Le temps passera quand tout le monde dormira");
        }

        static bool Find()
        {
            if (found) return sunColor != null;
            found = true;
            GameObject sun = GameObject.Find("MAP/Sun/PivotSun/SUN");
            GameObject pivot = GameObject.Find("MAP/Sun/PivotSun");
            GameObject cl = GameObject.Find("MAP/WEATHER/Clouds");
            GameObject fc = GameObject.Find("MAP/WEATHER/Forecast");
            sunColor = sun != null ? Game.FsmOn(sun, "Color") : null;
            sunRotation = pivot != null ? Game.FsmOn(pivot, "Rotation") : null;
            if (cl != null)
            {
                clouds = cl.transform;
                weather = Game.FsmOn(cl, "Weather");
                temperature = Game.FsmOn(cl, "Temperature");
                Transform co = cl.transform.Find("CloudObjects");
                cloudObjects = co;
            }
            forecast = fc != null ? Game.FsmOn(fc, "Logic") : null;
            Log.Info("monde : soleil " + (sunColor != null) + ", rotation " + (sunRotation != null) + ", nuages " + (weather != null)
                     + ", previsions " + (forecast != null) + ", temperature " + (temperature != null));
            return sunColor != null;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Find()) return;
            if (!Session.IsHost)
            {
                // Chaque image : le lit ne doit pas accelerer le temps tout seul.
                SetBeds(hostFast ? SleepScale : NormalScale);
                FsmFloat sc = FsmVariables.GlobalVariables.FindFsmFloat("GlobalTimeScale");
                if (sc != null && !hostFast && sc.Value < 1f) sc.Value = NormalScale;
                WaitToast(hostFast);
                PassOutAlone(hostFast);
            }
            if (Session.IsHost)
            {
                bool fast = HostSleep();
                PassOutAlone(fast);
                if (Time.realtimeSinceStartup < nextSend) return;
                nextSend = Time.realtimeSinceStartup + (fast ? 0.1f : 1f);
                if (Session.RemoteCount == 0) return;
                var w = new NetWriter(Msg.World)
                    .I32(GlobalInt("GlobalDay")).I32(GlobalInt("GlobalDaysPassed")).I32(GlobalInt("GlobalWeeksPassed"))
                    .I32(sunColor.FsmVariables.GetFsmInt("Time").Value)
                    .F32(sunRotation != null ? sunRotation.FsmVariables.GetFsmFloat("Rotation").Value : 0f)
                    .Vec(clouds != null ? clouds.position : Vector3.zero)
                    .Vec(cloudObjects != null ? cloudObjects.position : Vector3.zero)
                    .Quat(cloudObjects != null ? cloudObjects.rotation : Quaternion.identity)
                    .F32(GlobalFloat("AmbientTemperature"))
                    .Bool(forecast != null && forecast.FsmVariables.GetFsmBool("Snowing").Value)
                    .F32(GlobalFloat("GlobalTimeScale"));
                Session.Broadcast(w, false);
            }
        }

        // Hote : phase commune de sommeil (tous les lits comptent les heures) -> horloge acceleree,
        // jusqu'a ce qu'aucun lit ne compte plus. Rend vrai si l'horloge va vite.
        static bool HostSleep()
        {
            FsmFloat scale = FsmVariables.GlobalVariables.FindFsmFloat("GlobalTimeScale");
            if (scale == null) return false;
            int players = 1, counting = BedCounting() ? 1 : 0, sleeping = Game.GlobalBool("PlayerSleeps") ? 1 : 0;
            foreach (PlayerInfo p in Session.Players.Values)
            {
                if (p.Local || p.Level != 1 || Time.realtimeSinceStartup - p.StateTime > 3f) continue;
                players++;
                if ((p.State.Flags & PlayerSync.F_SleepFast) != 0) counting++;
                if ((p.State.Flags & PlayerSync.F_Sleep) != 0) sleeping++;
            }
            if (!phase && counting == players) { phase = true; Log.Info("monde : tout le monde dort (" + players + "), le temps passe vite"); }
            else if (phase && counting == 0) { phase = false; Log.Info("monde : plus personne ne dort, horloge normale"); }
            SetBeds(phase ? SleepScale : NormalScale);
            if (phase) scale.Value = SleepScale;
            else if (scale.Value < 1f) scale.Value = NormalScale;   // un lit l'avait mise a 0,5
            WaitToast(phase);
            return phase;
        }

        // Evanoui alors que les autres ne dorment pas (fast faux) : apres PassOutWait s, reveil de ce joueur
        // seul. Les heures que "Sleep time" aurait comptees une a une lui sont appliquees d'un coup (fatigue
        // -FatigueRemovalRate et Rate +10 par heure, Hours = 0 ; le deplacement des nuages par heure est
        // laisse : la meteo est commune), puis ABORT -> "Calc rates 2" (faim, soif, ivresse...), et le jeu
        // le reveille la ou il l'aurait reveille. Une image : seulement des comparaisons de textes.
        static void PassOutAlone(bool fast)
        {
            if (passOut == null) return;
            string s = passOut.ActiveStateName;
            bool counting = s == "Day change" || s == "Sleep time";
            float now = Time.realtimeSinceStartup;
            // (Temps rapide commun, ou seul en jeu : il dort comme dans un lit ; l'attente ne court pas.)
            if (!counting || fast || Session.RemoteCount == 0) { passOutSince = counting ? now : -1; return; }
            if (passOutSince < 0)
            {
                passOutSince = now;
                Log.Info("monde : evanoui (" + s + "), les autres ne dorment pas : reveil seul dans " + PassOutWait + " s");
                Hud.Toast("Evanoui : reveil dans " + (int)PassOutWait + " s, l'heure ne bouge pas pour les autres (sauf si tout le monde dort)");
                return;
            }
            if (now - passOutSince < PassOutWait) return;
            passOutSince = -1;
            FsmVariables v = passOut.FsmVariables;
            FsmInt hours = v.FindFsmInt("Hours");
            FsmFloat rate = v.FindFsmFloat("Rate"), removal = v.FindFsmFloat("FatigueRemovalRate");
            FsmFloat fatigue = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
            int h = hours != null ? Mathf.Max(0, hours.Value) : 0;
            float before = fatigue != null ? fatigue.Value : 0f;
            if (fatigue != null && removal != null) fatigue.Value = Mathf.Max(0f, fatigue.Value - removal.Value * h);
            if (rate != null) rate.Value += 10f * h;
            if (hours != null) hours.Value = 0;
            Log.Info("monde : evanoui seul : " + h + " h de sommeil appliquees a ce joueur (fatigue " + before.ToString("F1") + " -> "
                     + (fatigue != null ? fatigue.Value.ToString("F1") : "?") + "), reveil sans toucher a l'heure commune");
            passOut.SendEvent("ABORT");
        }

        // Essais ([Test] Autotest=evanoui) : le joueur s'evanouit a 30 s (PassOut -> "Pass out", comme
        // l'ivresse ou le froid) ; etat, heure, echelle et fatigue toutes les 2 s jusqu'au reveil.
        // Attendu, les autres eveilles : "reveil seul dans 30 s", puis "evanoui seul : N h ... reveil",
        // l'heure commune inchangee chez tous (aucune ligne "monde : heure" chez les invites).
        static int testStep;
        static float testLogAt;
        public static void Test(string mode, float t)
        {
            if (mode != "evanoui") return;
            float now = Time.realtimeSinceStartup;
            if (testStep == 0 && t > 30f)
            {
                testStep = 1;
                nextBedScan = 0; ScanBeds();
                if (passOut == null) { Log.Warn("autotest : evanoui : Systems/PassOut :: Activate introuvable"); testStep = 3; return; }
                Log.Info("autotest : evanoui : " + passOut.ActiveStateName + " -> Pass out, heure " + (sunColor != null ? sunColor.FsmVariables.GetFsmInt("Time").Value : -1));
                Game.SetState(passOut, "Pass out");
            }
            if (testStep == 1 && passOut != null && now >= testLogAt)
            {
                testLogAt = now + 2f;
                string s = passOut.ActiveStateName;
                FsmFloat fat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
                FsmInt hours = passOut.FsmVariables.FindFsmInt("Hours");
                Log.Info("autotest : evanoui : etat " + s + ", heures restantes " + (hours != null ? hours.Value : -1)
                         + ", heure " + (sunColor != null ? sunColor.FsmVariables.GetFsmInt("Time").Value : -1)
                         + ", echelle " + GlobalFloat("GlobalTimeScale") + ", fatigue " + (fat != null ? fat.Value.ToString("F1") : "?")
                         + ", dort " + Game.GlobalBool("PlayerSleeps"));
                if (s == "Test alc" && t > 45f) { testStep = 2; Log.Info("autotest : evanoui : reveille"); }
            }
        }

        public static void OnMessage(Msg type, Peer from, NetReader r)
        {
            // Msg 9 (StartGame, reserve jusqu'ici) : sauvegarde coop des toilettes ; Session passe ici les
            // types qu'il ne connait pas.
            if (type == SaveTransfer.CoopMsg) { SaveTransfer.OnCoop(from, r); return; }
            if (type != Msg.World) { Log.Warn("message non gere : " + type); return; }
            if (Session.IsHost || !PlayerSync.InGame || !Find()) return;
            int day = r.I32(), daysPassed = r.I32(), weeksPassed = r.I32(), hour = r.I32();
            float sunRot = r.F32();
            Vector3 cloudsPos = r.Vec(), objPos = r.Vec();
            Quaternion objRot = r.Quat();
            float temp = r.F32();
            bool snowing = r.Bool();
            float hostScale = r.F32();
            bool fast = hostScale <= SleepScale + 0.01f;
            hostFast = fast;
            SetBeds(fast ? SleepScale : NormalScale);
            FsmFloat scale = FsmVariables.GlobalVariables.FindFsmFloat("GlobalTimeScale");
            if (scale != null && Mathf.Abs(scale.Value - hostScale) > 0.01f)
            {
                Log.Info("monde : echelle du temps " + scale.Value + " -> " + hostScale + " (hote)");
                scale.Value = hostScale;
            }

            if (!muted)
            {
                muted = true;
                foreach (PlayMakerFSM f in new[] { weather, forecast, temperature })
                    if (f != null) f.enabled = false;
                Log.Info("monde : meteo locale coupee, celle de l'hote s'applique");
            }
            SetGlobalInt("GlobalDay", day);
            SetGlobalInt("GlobalDaysPassed", daysPassed);
            SetGlobalInt("GlobalWeeksPassed", weeksPassed);
            FsmInt t = sunColor.FsmVariables.GetFsmInt("Time");
            int ahead = ((t.Value - hour) % 24 + 24) % 24;   // heures d'avance de l'invite
            if (t.Value != hour && !(fast && ahead > 0 && ahead <= 2))
            {
                Log.Info("monde : heure " + t.Value + " -> " + hour + " (hote)");
                t.Value = hour;
                sunColor.SendEvent("TIMESKIP");
                if (weather != null) weather.FsmVariables.GetFsmInt("Time").Value = hour;
            }
            if (sunRotation != null)
            {
                FsmFloat rot = sunRotation.FsmVariables.GetFsmFloat("Rotation");
                if (Mathf.Abs(Mathf.DeltaAngle(rot.Value, sunRot)) > 0.5f) rot.Value = sunRot;
            }
            if (clouds != null) clouds.position = cloudsPos;
            if (cloudObjects != null) { cloudObjects.position = objPos; cloudObjects.rotation = objRot; }
            FsmFloat amb = FsmVariables.GlobalVariables.FindFsmFloat("AmbientTemperature");
            if (amb != null) amb.Value = temp;
            if (forecast != null) forecast.FsmVariables.GetFsmBool("Snowing").Value = snowing;
        }

        public static float LocalScale { get { return GlobalFloat("GlobalTimeScale"); } }

        static int GlobalInt(string n) { FsmInt v = FsmVariables.GlobalVariables.FindFsmInt(n); return v != null ? v.Value : 0; }
        static float GlobalFloat(string n) { FsmFloat v = FsmVariables.GlobalVariables.FindFsmFloat(n); return v != null ? v.Value : 0f; }
        static void SetGlobalInt(string n, int x) { FsmInt v = FsmVariables.GlobalVariables.FindFsmInt(n); if (v != null) v.Value = x; }
    }
}
