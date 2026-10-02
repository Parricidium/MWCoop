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
    public static class World
    {
        static float nextSend;
        static bool muted;
        static Transform clouds, cloudObjects;
        static PlayMakerFSM sunColor, sunRotation, weather, forecast, temperature;
        static bool found;

        public static void OnLevelLoaded()
        {
            found = muted = false;
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
            if (Session.IsHost)
            {
                if (Time.realtimeSinceStartup < nextSend) return;
                nextSend = Time.realtimeSinceStartup + 1f;
                if (Session.RemoteCount == 0) return;
                var w = new NetWriter(Msg.World)
                    .I32(GlobalInt("GlobalDay")).I32(GlobalInt("GlobalDaysPassed")).I32(GlobalInt("GlobalWeeksPassed"))
                    .I32(sunColor.FsmVariables.GetFsmInt("Time").Value)
                    .F32(sunRotation != null ? sunRotation.FsmVariables.GetFsmFloat("Rotation").Value : 0f)
                    .Vec(clouds != null ? clouds.position : Vector3.zero)
                    .Vec(cloudObjects != null ? cloudObjects.position : Vector3.zero)
                    .Quat(cloudObjects != null ? cloudObjects.rotation : Quaternion.identity)
                    .F32(GlobalFloat("AmbientTemperature"))
                    .Bool(forecast != null && forecast.FsmVariables.GetFsmBool("Snowing").Value);
                Session.Broadcast(w, false);
            }
        }

        public static void OnMessage(Msg type, Peer from, NetReader r)
        {
            if (type != Msg.World) { Log.Warn("message non gere : " + type); return; }
            if (Session.IsHost || !PlayerSync.InGame || !Find()) return;
            int day = r.I32(), daysPassed = r.I32(), weeksPassed = r.I32(), hour = r.I32();
            float sunRot = r.F32();
            Vector3 cloudsPos = r.Vec(), objPos = r.Vec();
            Quaternion objRot = r.Quat();
            float temp = r.F32();
            bool snowing = r.Bool();

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
            if (t.Value != hour)
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

        static int GlobalInt(string n) { FsmInt v = FsmVariables.GlobalVariables.FindFsmInt(n); return v != null ? v.Value : 0; }
        static float GlobalFloat(string n) { FsmFloat v = FsmVariables.GlobalVariables.FindFsmFloat(n); return v != null ? v.Value : 0f; }
        static void SetGlobalInt(string n, int x) { FsmInt v = FsmVariables.GlobalVariables.FindFsmInt(n); if (v != null) v.Value = x; }
    }
}
