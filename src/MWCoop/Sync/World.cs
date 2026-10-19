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
        static PlayMakerFSM sunColor, sunRotation, weather, forecast, temperature, playerRain;
        // Aiguilles de l'horloge du soleil (SUN/Clock : 'Time' les tourne chaque seconde depuis la derniere heure pile) : les
        // montres du jeu les lisent (TimeRotationHour / Minute). Chez un invite arrive en cours d'heure, la minute repartait de
        // 0 a son arrivee -- montres decalees (Dr.Robi, 11/10). Envoyees avec l'heure (champs facultatifs, en fin).
        static Transform needleHour, needleMinute;
        static bool found;
        const float SleepScale = 0.5f, NormalScale = 300f;
        const float PassOutWait = 30f;
        const float BedHourEvery = 8f;   // dormir seul : une heure de repos toutes les 8 s (pour soi, l'heure commune ne bouge pas)
        static float bedAloneNext = -1;
        public static bool WakeRequested;   // menu F10 > Se reveiller
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
            hourMismatchSince = -1;
        }

        // Lits (et canape) : SleepTrigger :: Activate ; evanouissement : PassOut :: Activate (memes etats
        // "Day change" / "Sleep time", meme variable TimeScaleSleep). Rescannes toutes les 20 s.
        static void ScanBeds()
        {
            if (Time.realtimeSinceStartup < nextBedScan) return;
            nextBedScan = Time.realtimeSinceStartup + 20f;
            beds.Clear();
            passOut = null;
            foreach (Object o in Game.FsmsNamed("Activate"))
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
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
            Hud.Toast(Lang.T("Tu dors seul : tu te reposes, l'heure ne bouge pas pour les autres. Espace : se r\u00E9veiller",
                             "Sleeping alone: you rest, the clock does not move for the others. Space: wake up"));
        }

        // Dormir dans un lit alors que les autres sont eveilles (demande d'un joueur, 07/10) : le jeu compterait les
        // heures au rythme normal (300 s chacune). Ici, une heure de "Sleep time" pour ce joueur seul toutes les
        // BedHourEvery s (fatigue -FatigueRemovalRate, Rate +10, comme le lit), sans toucher a l'heure commune ; reveil
        // tout seul une fois repose (fatigue a 0), ou tout de suite avec Espace (ou F10) : ABORT -> "Calc rates" (faim,
        // soif... d'apres Rate) -> reveil, comme le jeu.
        static void BedAlone(bool fast)
        {
            PlayMakerFSM bed = null;
            foreach (PlayMakerFSM f in beds)
            {
                if (f == null || f == passOut) continue;
                string st = f.ActiveStateName;
                if (st == "Day change" || st == "Sleep time") { bed = f; break; }
            }
            if (bed == null || fast || Session.RemoteCount == 0) { bedAloneNext = -1; WakeRequested = false; return; }
            float now = Time.realtimeSinceStartup;
            FsmFloat fatigue = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
            if (bedAloneNext < 0)
            {
                bedAloneNext = now + BedHourEvery;
                waitToastAt = 0;
                Log.Info("monde : dort seul (les autres sont eveilles) : une heure de repos toutes les " + BedHourEvery + " s, Espace pour se reveiller");
                return;
            }
            FsmVariables v = bed.FsmVariables;
            if (now >= bedAloneNext)
            {
                bedAloneNext = now + BedHourEvery;
                FsmFloat rate = v.FindFsmFloat("Rate"), removal = v.FindFsmFloat("FatigueRemovalRate");
                if (fatigue != null && removal != null) fatigue.Value = fatigue.Value - removal.Value;
                if (rate != null) rate.Value += 10f;
            }
            bool wake = WakeRequested || Input.GetKeyDown(KeyCode.Space) || (fatigue != null && fatigue.Value <= 0f);
            if (!wake) return;
            if (fatigue != null && fatigue.Value < 0f) fatigue.Value = 0f;
            Log.Info("monde : reveil (dormi seul" + (WakeRequested ? ", menu" : fatigue != null && fatigue.Value <= 0f ? ", repose" : ", Espace") + "), fatigue "
                     + (fatigue != null ? fatigue.Value.ToString("F1") : "?") + ", l'heure commune n'a pas bouge");
            WakeRequested = false;
            bedAloneNext = -1;
            bed.SendEvent("ABORT");
        }
        public static bool SleepingAlone { get { return bedAloneNext >= 0; } }

        static bool Find()
        {
            if (found) return sunColor != null;
            found = true;
            if (Game.Msc) return FindMsc();
            GameObject sun = GameObject.Find("MAP/Sun/PivotSun/SUN");
            GameObject pivot = GameObject.Find("MAP/Sun/PivotSun");
            GameObject cl = GameObject.Find("MAP/WEATHER/Clouds");
            GameObject fc = GameObject.Find("MAP/WEATHER/Forecast");
            sunColor = sun != null ? Game.FsmOn(sun, "Color") : null;
            sunRotation = pivot != null ? Game.FsmOn(pivot, "Rotation") : null;
            if (sun != null) { needleHour = sun.transform.Find("Clock/hour/NeedleHour"); needleMinute = sun.transform.Find("Clock/minute/NeedleMinute"); }
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

        // ---------------------------------------------------------------- My Summer Car (vidage v.250908-04, 19/10)
        //  - heure : MAP/SUN/Pivot/SUN :: Color, une tranche de 2 h (Time = 2, 4 .. 24, debut de la tranche) ; pas de TIMESKIP :
        //    Time pose puis WAKEUP a Color (State 3 rejoue la tranche) et a SUN :: Clock (comme Cheats.SetHourMsc). Aiguilles :
        //    YARD/Building/Dynamics/SuomiClock/Clock (son automate Time les recopie dans TimeRotationHour / Minute).
        //  - meteo : MAP/CloudSystem/Clouds :: Weather. « Move clouds » le glisse (PosX -= 4/s, SetPosition chaque image) et teinte
        //    les nuages : il reste allume chez l'invite, qui recoit PosX, PosZ et Rotation. Le temps (WeatherType 0 sec, 1 pluie,
        //    2 orage ; WeatherCloudID, le nuage de pluie 1..6 ; ThunderForce) change quand l'hote retire au sort (PosX < -18000) :
        //    l'invite remet alors les materiaux, pose les variables et passe par « Set cloud » (le chemin du chargement d'une
        //    partie : materiau cloud_rain, Thunder place, puis Rain / No weather / Thunder et RainYes).
        //  - sommeil : instantane dans MSC (le lit ajoute SleepTime a Color.Time puis WAKEUP) : rien a accelerer ; un invite qui
        //    dort seul garde sa fatigue reprise, l'heure de l'hote le ramene.
        static Material cloudMat;
        static Renderer[] cloudRends;
        static float hourMismatchSince = -1;
        static int jailLogs;

        static bool FindMsc()
        {
            GameObject sun = Game.FindAny("MAP/SUN/Pivot/SUN");
            GameObject pivot = Game.FindAny("MAP/SUN/Pivot");
            GameObject cl = Game.FindAny("MAP/CloudSystem/Clouds");
            GameObject clock = Game.FindAny("YARD/Building/Dynamics/SuomiClock/Clock");
            sunColor = sun != null ? Game.FsmOn(sun, "Color") : null;
            sunRotation = pivot != null ? Game.FsmOn(pivot, "Rotation") : null;
            if (clock != null) { needleHour = clock.transform.Find("hour/NeedleHour"); needleMinute = clock.transform.Find("minute/NeedleMinute"); }
            forecast = temperature = null;
            if (cl != null)
            {
                clouds = cl.transform;
                weather = Game.FsmOn(cl, "Weather");
                cloudObjects = cl.transform.Find("CloudObjects");
            }
            cloudMat = null;
            cloudRends = cloudObjects != null ? cloudObjects.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            // (materiau normal des nuages : celui qui ne porte pas « rain » -- un seul nuage a cloud_rain, la zone de pluie rain_area)
            foreach (Renderer rd in cloudRends)
                if (rd != null && rd.transform.parent == cloudObjects && rd.sharedMaterial != null && !rd.sharedMaterial.name.Contains("rain")) { cloudMat = rd.sharedMaterial; break; }
            Log.Info("monde (MSC) : soleil " + (sunColor != null) + ", rotation " + (sunRotation != null) + ", nuages " + (weather != null)
                     + ", materiau des nuages " + (cloudMat != null) + ", aiguilles " + (needleHour != null && needleMinute != null));
            return sunColor != null;
        }

        static void SendMsc()
        {
            FsmVariables wv = weather != null ? weather.FsmVariables : null;
            var w = new NetWriter(Msg.World)
                .I32(GlobalInt("GlobalDay"))
                .I32(sunColor.FsmVariables.GetFsmInt("Time").Value)
                .F32(sunRotation != null ? sunRotation.FsmVariables.GetFsmFloat("Rotation").Value : 0f)
                .F32(needleHour != null ? needleHour.localEulerAngles.y : 0f).F32(needleMinute != null ? needleMinute.localEulerAngles.y : 0f)
                .Bool(wv != null)
                .F32(wv != null ? wv.GetFsmFloat("PosX").Value : 0f).F32(wv != null ? wv.GetFsmFloat("PosZ").Value : 0f)
                .F32(wv != null ? wv.GetFsmFloat("Rotation").Value : 0f)
                .I32(wv != null ? wv.GetFsmInt("WeatherType").Value : 0).I32(wv != null ? wv.GetFsmInt("WeatherCloudID").Value : 0)
                .F32(wv != null ? wv.GetFsmFloat("ThunderForce").Value : 0f)
                .Bool(wv != null && wv.GetFsmBool("Rain").Value);
            Session.Broadcast(w, false);
        }

        static void OnMessageMsc(NetReader r)
        {
            int day = r.I32(), hour = r.I32();
            float sunRot = r.F32(), hy = r.F32(), my = r.F32();
            bool hasWx = r.Bool();
            float posX = r.F32(), posZ = r.F32(), wrot = r.F32();
            int wtype = r.I32(), cloudId = r.I32();
            float thunder = r.F32();
            bool rain = r.Bool();
            if (Police.InJail) { if (jailLogs++ < 3) Log.Info("monde : en prison ici, jour et heure de l'hote pas appliques"); }
            else {
            SetGlobalInt("GlobalDay", day);
            // Heure : un invite passe a la tranche suivante une fraction de seconde avant ou apres l'hote ; on ne le recale
            // qu'apres 5 s d'ecart (WAKEUP relance le fondu des couleurs de la tranche : pas d'aller-retour a chaque frontiere).
            FsmInt t = sunColor.FsmVariables.GetFsmInt("Time");
            float now = Time.realtimeSinceStartup;
            if (t.Value == hour) hourMismatchSince = -1;
            else if (hourMismatchSince < 0) hourMismatchSince = now;
            else if (now - hourMismatchSince > 5f)
            {
                hourMismatchSince = -1;
                Log.Info("monde : heure " + t.Value + " -> " + hour + " (hote)");
                t.Value = hour;
                sunColor.SendEvent("WAKEUP");
                PlayMakerFSM clock = Game.FsmOn(sunColor.gameObject, "Clock");
                if (clock != null && clock.Fsm.Initialized) clock.SendEvent("WAKEUP");
            }
            if (sunRotation != null)
            {
                FsmFloat rot = sunRotation.FsmVariables.GetFsmFloat("Rotation");
                if (Mathf.Abs(Mathf.DeltaAngle(rot.Value, sunRot)) > 0.5f) rot.Value = sunRot;
            }
            }
            if (needleHour != null && Mathf.Abs(Mathf.DeltaAngle(needleHour.localEulerAngles.y, hy)) > 0.5f) needleHour.localEulerAngles = new Vector3(0f, hy, 0f);
            if (needleMinute != null && Mathf.Abs(Mathf.DeltaAngle(needleMinute.localEulerAngles.y, my)) > 1f)
            {
                if (needleLogs++ < 3) Log.Info("monde : aiguille des minutes " + needleMinute.localEulerAngles.y.ToString("F0") + " -> " + my.ToString("F0") + " (hote)");
                needleMinute.localEulerAngles = new Vector3(0f, my, 0f);
            }
            if (!hasWx || weather == null || !weather.Fsm.Initialized) return;
            FsmVariables wv = weather.FsmVariables;
            wv.GetFsmFloat("PosX").Value = posX; wv.GetFsmFloat("PosZ").Value = posZ;
            clouds.position = new Vector3(posX, clouds.position.y, posZ);
            if (Mathf.Abs(Mathf.DeltaAngle(wv.GetFsmFloat("Rotation").Value, wrot)) > 0.5f || Mathf.Abs(Mathf.DeltaAngle(clouds.eulerAngles.y, wrot)) > 0.5f)
            {
                wv.GetFsmFloat("Rotation").Value = wrot;
                clouds.rotation = Quaternion.Euler(0f, wrot, 0f);
            }
            FsmInt wt = wv.GetFsmInt("WeatherType"), wid = wv.GetFsmInt("WeatherCloudID");
            if (wt.Value != wtype || wid.Value != cloudId || wv.GetFsmBool("Rain").Value != rain)
            {
                Log.Info("monde : temps " + wt.Value + "/" + wid.Value + " -> " + wtype + "/" + cloudId + (rain ? " (pluie)" : "") + " (hote)");
                if (cloudMat != null) foreach (Renderer rd in cloudRends) if (rd != null && rd.transform.parent == cloudObjects) rd.sharedMaterial = cloudMat;
                wt.Value = wtype; wid.Value = cloudId;
                wv.GetFsmFloat("ThunderForce").Value = thunder;
                Game.SetState(weather, "Set cloud");
            }
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Find()) return;
            if (Game.Msc)
            {
                if (!Session.IsHost || Time.realtimeSinceStartup < nextSend) return;
                nextSend = Time.realtimeSinceStartup + 1f;
                if (Session.RemoteCount > 0) SendMsc();
                return;
            }
            if (!Session.IsHost)
            {
                // Chaque image : le lit ne doit pas accelerer le temps tout seul.
                SetBeds(hostFast ? SleepScale : NormalScale);
                FsmFloat sc = FsmVariables.GlobalVariables.FindFsmFloat("GlobalTimeScale");
                if (sc != null && !hostFast && sc.Value < 1f) sc.Value = NormalScale;
                WaitToast(hostFast);
                PassOutAlone(hostFast);
                BedAlone(hostFast);
            }
            if (Session.IsHost)
            {
                bool fast = HostSleep();
                PassOutAlone(fast);
                BedAlone(fast);
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
                if (needleHour != null && needleMinute != null) w.F32(needleHour.localEulerAngles.y).F32(needleMinute.localEulerAngles.y);
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
                Hud.Toast(string.Format(Lang.T("\u00C9vanoui : r\u00E9veil dans {0} s, l'heure ne bouge pas pour les autres (sauf si tout le monde dort)",
                              "Passed out: waking up in {0} s, the clock does not move for the others (unless everyone sleeps)"), (int)PassOutWait));
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

        static int needleLogs;
        public static void OnMessage(Msg type, Peer from, NetReader r)
        {
            // Msg 9 (StartGame, reserve jusqu'ici) : sauvegarde coop des toilettes ; Session passe ici les
            // types qu'il ne connait pas.
            if (type == SaveTransfer.CoopMsg) { SaveTransfer.OnCoop(from, r); return; }
            if (type != Msg.World) { Log.Warn("message non gere : " + type); return; }
            if (Session.IsHost || !PlayerSync.InGame || !Find()) return;
            if (Game.Msc) { OnMessageMsc(r); return; }
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
            if (r.More)
            {
                float hy = r.F32(), my = r.F32();
                if (needleHour != null && Mathf.Abs(Mathf.DeltaAngle(needleHour.localEulerAngles.y, hy)) > 0.5f) needleHour.localEulerAngles = new Vector3(0f, hy, 0f);
                if (needleMinute != null && Mathf.Abs(Mathf.DeltaAngle(needleMinute.localEulerAngles.y, my)) > 1f)
                {
                    if (needleLogs++ < 3) Log.Info("monde : aiguille des minutes " + needleMinute.localEulerAngles.y.ToString("F0") + " -> " + my.ToString("F0") + " (hote)");
                    needleMinute.localEulerAngles = new Vector3(0f, my, 0f);
                }
            }
            if (clouds != null) clouds.position = cloudsPos;
            if (cloudObjects != null) { cloudObjects.position = objPos; cloudObjects.rotation = objRot; }
            FsmFloat amb = FsmVariables.GlobalVariables.FindFsmFloat("AmbientTemperature");
            if (amb != null) amb.Value = temp;
            if (forecast != null) forecast.FsmVariables.GetFsmBool("Snowing").Value = snowing;
            // Ce que fait 'Fudge' (Forecast, coupe ici) avec Snowing : les nuages (CloudObjects) n'existent que les
            // jours de neige, et la neige qui tombe sur le joueur (PLAYER/Rain :: Rain, RainYes). Sans cela le ciel de
            // l'invite restait celui du chargement (couvert chez lui, degage chez l'hote).
            if (cloudObjects != null && cloudObjects.gameObject.activeSelf != snowing)
            {
                cloudObjects.gameObject.SetActive(snowing);
                Log.Info("monde : nuages " + (snowing ? "affiches" : "caches") + " (hote)");
            }
            if (playerRain == null) { GameObject pr = Game.PlayerPart("Rain"); if (pr != null) playerRain = Game.FsmOn(pr, "Rain"); }
            FsmBool rainYes = playerRain != null ? playerRain.FsmVariables.FindFsmBool("RainYes") : null;
            if (rainYes != null) rainYes.Value = snowing;
        }

        public static float LocalScale { get { return GlobalFloat("GlobalTimeScale"); } }

        static int GlobalInt(string n) { FsmInt v = FsmVariables.GlobalVariables.FindFsmInt(n); return v != null ? v.Value : 0; }
        static float GlobalFloat(string n) { FsmFloat v = FsmVariables.GlobalVariables.FindFsmFloat(n); return v != null ? v.Value : 0f; }
        static void SetGlobalInt(string n, int x) { FsmInt v = FsmVariables.GlobalVariables.FindFsmInt(n); if (v != null) v.Value = x; }
    }
}
