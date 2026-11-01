using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Chauffage de la voiture standalone (Golf, JD 01/11 : « l'adapter pour l'hiver », capture annotee de la console) -- memes regles
    // que le chauffage de la SORBET de My Winter Car (ref v.261008-01, HeaterUnit / ButtonHeater*) :
    //  - interrupteur du chauffage (bouton_chauffage) : clic gauche, marche / arret ; icone orange allumee (voyant_chauffage) ;
    //  - ventilateur (chauf_souffle.p205, molette 0-1-2-3) : clic gauche, cran suivant en aller-retour (0 1 2 3 2 1 0) ;
    //  - temperature et direction (chauf_temp / chauf_dir.p205, une glissiere chacune -- le 2e bouton de la direction retire) : molette
    //    (vers le haut : vers la droite ; temperature chaud a gauche, froid a droite ; direction haut a gauche, bas a droite), 16 crans.
    // Chaleur (My Winter Car seulement : pas de temperature du corps sur My Summer Car) : comme la SORBET, chaque seconde, l'habitacle
    // perd 0,02 degre (plus les portieres ouvertes), gagne (1 + direction) x ventilateur x liquide x temperature / 100 (contact mis,
    // chauffage en marche), borne entre l'air dehors et le liquide / 5 ; liquide : chauffe moteur tournant jusqu'au thermostat (88),
    // refroidit sinon et par le chauffage. Source de chaleur du jeu (copie de HeatSourceSorbet, dans la liste HeatSources de
    // PLAYER/BodyTemp) au milieu de l'habitacle : le joueur assis s'y rechauffe. Reglages envoyes avec l'etat de la voiture (V_STD) :
    // chacun calcule sa chaleur.
    public static partial class StandaloneCar
    {
        public class Heater
        {
            public Transform Dir, Temp, Fan; public Vector3 DirC, TempC, FanC, FanAxis; public Vector2 DirX, TempX; public float FanZero;
            public bool HasSwitch; public Vector3 SwitchAt; public GameObject Lamp; public AudioSource FanSound;
            public bool On, FanUp = true; public int FanStep; public float TempPos, DirPos = 0.7f;   // (defauts de la SORBET : chaud, direction 0,3)
            public float Coolant = float.NaN, Interior = float.NaN, NextTick, NextSrc; public GameObject Src; public FsmFloat SrcTemp;
        }
        const int FanSteps = 3; const float SlideStep = 1f / 16f;

        static void SetupHeater(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            var h = new Heater();
            Vector3 v;
            h.Dir = HeatPart(b, inf, "dir", out h.DirC); h.Temp = HeatPart(b, inf, "temp", out h.TempC); h.Fan = HeatPart(b, inf, "souffle", out h.FanC);
            if (inf.TryGetValue("chauf_dir_course", out v)) h.DirX = new Vector2(v.x, v.y); else h.Dir = null;
            if (inf.TryGetValue("chauf_temp_course", out v)) h.TempX = new Vector2(v.x, v.y); else h.Temp = null;
            if (inf.TryGetValue("chauf_souffle_axe", out v)) h.FanAxis = v.normalized; else h.Fan = null;
            if (inf.TryGetValue("chauf_souffle_zero", out v)) h.FanZero = v.x;
            h.HasSwitch = inf.TryGetValue("bouton_chauffage", out h.SwitchAt);
            if (h.Dir == null && h.Temp == null && h.Fan == null && !h.HasSwitch) return;
            Vector3 c, up, right;
            string f = System.IO.Path.Combine(b.M.Dir, "voyant_chauffage.png");
            if (inf.TryGetValue("voyant_chauffage_centre", out c) && inf.TryGetValue("voyant_chauffage_haut", out up) && inf.TryGetValue("voyant_chauffage_droite", out right) && System.IO.File.Exists(f))
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "standalone_voyant_chauffage", wrapMode = TextureWrapMode.Clamp };
                t.LoadImage(System.IO.File.ReadAllBytes(f));
                Vector3 n = Vector3.Cross(right, up).normalized;
                h.Lamp = Gauge.Quad("voyant_chauffage", b.Body, c - n * 0.0015f, Vector3.zero, up, right);
                h.Lamp.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Texture")) { mainTexture = t, name = "standalone_voyant_chauffage" };
                h.Lamp.SetActive(false);
            }
            // souffle du ventilateur : celui de la SORBET (HeaterUnit/BlowerSound), au tableau de bord
            GameObject tpl = VazCar.Template();
            Transform bs = tpl != null ? tpl.transform.Find("Simulation/Electricity/PowerON/HeaterUnit/BlowerSound") : null;
            AudioSource src = bs != null ? bs.GetComponent<AudioSource>() : null;
            if (src != null && src.clip != null)
            {
                var g = new GameObject("chauffage_souffle"); g.transform.SetParent(b.Body, false); g.transform.localPosition = h.FanC;
                h.FanSound = g.AddComponent<AudioSource>(); h.FanSound.clip = src.clip; h.FanSound.loop = true; h.FanSound.playOnAwake = false;
                h.FanSound.spatialBlend = 1f; h.FanSound.minDistance = 1f; h.FanSound.maxDistance = 10f; h.FanSound.outputAudioMixerGroup = src.outputAudioMixerGroup;
            }
            st.Heat = h;
            HeaterParts(st);
            HeatSource(b, st);
            Log.Info("standalone : chauffage (" + (h.HasSwitch ? "interrupteur" : "sans interrupteur") + (h.Lamp != null ? ", voyant" : "") + (h.Dir != null ? ", direction" : "") + (h.Temp != null ? ", temperature" : "")
                     + (h.Fan != null ? ", ventilateur (repere a " + h.FanZero.ToString("F0") + " degres)" : "") + (h.FanSound != null ? ", souffle de la SORBET" : "")
                     + (h.Src != null ? ", source de chaleur du jeu" : ", source de chaleur plus tard (joueur pas encore pret)") + ")");
        }
        // source de chaleur du jeu (My Winter Car) : copie de celle de la SORBET, au milieu de l'habitacle, dans la liste HeatSources de
        // PLAYER/BodyTemp -- reessayee toutes les 5 s tant que le joueur n'est pas pret (invite : voiture construite pendant l'arrivee)
        static void HeatSource(VazCar.Built b, State st)
        {
            Heater h = st.Heat;
            if (h.Src != null || Game.Msc || Time.realtimeSinceStartup < h.NextSrc) return;
            h.NextSrc = Time.realtimeSinceStartup + 5f;
            GameObject tpl = VazCar.Template();
            Transform hs = tpl != null ? tpl.transform.Find("Simulation/CarTempSorbet/HeatSourceSorbet") : null;
            GameObject body = GameObject.Find("PLAYER/BodyTemp");
            PlayMakerArrayListProxy list = null;
            if (body != null) foreach (PlayMakerArrayListProxy p in body.GetComponents<PlayMakerArrayListProxy>()) if (p.referenceName == "HeatSources") list = p;
            if (hs == null || list == null || list.arrayList == null) return;
            h.Src = Object.Instantiate(hs.gameObject);
            h.Src.name = "HeatSourceStandalone";
            h.Src.transform.SetParent(b.Body, false);
            h.Src.transform.localPosition = new Vector3(0f, st.HasSeatRef[0] ? st.SeatRef[0].y + 0.4f : 0.95f, st.HasSeatRef[0] ? st.SeatRef[0].z - 0.2f : -0.3f);
            PlayMakerFSM fd = null;
            foreach (PlayMakerFSM x in h.Src.GetComponents<PlayMakerFSM>()) if (x.FsmName == "Data") fd = x;
            FsmFloat dist = fd != null ? fd.FsmVariables.FindFsmFloat("Distance") : null;
            h.SrcTemp = fd != null ? fd.FsmVariables.FindFsmFloat("Temperature") : null;
            if (dist != null) dist.Value = 1.0f;   // (places avant et arriere ; la SORBET : 0,8)
            list.arrayList.Add(h.Src);
            Log.Info("standalone : chauffage, source de chaleur du jeu posee (" + list.arrayList.Count + " dans HeatSources)");
        }
        static Transform HeatPart(VazCar.Built b, Dictionary<string, Vector3> inf, string k, out Vector3 c)
        {
            string f = "chauf_" + k + ".p205";
            if (!inf.TryGetValue("chauf_" + k + "_centre", out c) || !System.IO.File.Exists(System.IO.Path.Combine(b.M.Dir, f))) return null;
            var p = new GameObject("chauf_" + k).transform; p.SetParent(b.Body, false); p.localPosition = c;
            var i = new GameObject("pieces").transform; i.SetParent(p, false); i.localPosition = -c;
            VazCar.AddModel(b, i, f);
            return p;
        }
        static void HeaterParts(State st)
        {
            Heater h = st.Heat;
            if (h.Dir != null) h.Dir.localPosition = new Vector3(Mathf.Lerp(h.DirX.x, h.DirX.y, h.DirPos), h.DirC.y, h.DirC.z);
            if (h.Temp != null) h.Temp.localPosition = new Vector3(Mathf.Lerp(h.TempX.x, h.TempX.y, h.TempPos), h.TempC.y, h.TempC.z);
            // (crans du ventilateur : 0 a -60 degres du haut, 3 a +60 -- sens des aiguilles d'une montre vu du conducteur, axe vers l'avant)
            if (h.Fan != null) h.Fan.localRotation = Quaternion.AngleAxis(-(-60f + 40f * h.FanStep - h.FanZero), h.FanAxis);
            if (h.Lamp != null && h.Lamp.activeSelf != h.On) h.Lamp.SetActive(h.On);
        }

        // Viser et regler (conducteur : Knobs ; passager : Seats) -- texte du jeu a montrer, ou null si rien n'est vise
        static string HeaterInput(VazCar.Built b, State st, Transform view)
        {
            Heater h = st.Heat;
            if (h == null || view == null) return null;
            int hit = -1; float best = 0f;
            Vector3[] at = { h.HasSwitch ? b.Body.TransformPoint(h.SwitchAt) : Vector3.zero, h.Fan != null ? h.Fan.position : Vector3.zero,
                             h.Temp != null ? h.Temp.position : Vector3.zero, h.Dir != null ? h.Dir.position : Vector3.zero };
            bool[] has = { h.HasSwitch, h.Fan != null, h.Temp != null, h.Dir != null };
            float[] r = { 0.012f, 0.015f, 0.012f, 0.012f };
            for (int i = 0; i < 4; i++)
            {
                if (!has[i]) continue;
                Vector3 v = at[i] - view.position; float along = Vector3.Dot(v, view.forward);
                if (along < 0.15f || along > 1.3f) continue;
                float perp = (v - view.forward * along).magnitude;
                if (perp < r[i] && (hit < 0 || perp < best)) { hit = i; best = perp; }
            }
            if (hit < 0) return null;
            bool changed = false;
            if (hit == 0 && Input.GetMouseButtonDown(0)) { h.On = !h.On; changed = true; Log.Info("standalone : chauffage " + (h.On ? "en marche" : "arrete")); }
            if (hit == 1 && Input.GetMouseButtonDown(0))
            {
                if (h.FanStep >= FanSteps) h.FanUp = false; else if (h.FanStep <= 0) h.FanUp = true;
                h.FanStep += h.FanUp ? 1 : -1; changed = true;
                Log.Info("standalone : ventilateur " + h.FanStep);
            }
            if (hit >= 2)
            {
                float sc = Input.GetAxis("Mouse ScrollWheel");
                if (sc != 0f)
                {
                    float d = Mathf.Sign(sc) * SlideStep;
                    if (hit == 2) h.TempPos = Mathf.Clamp01(h.TempPos + d); else h.DirPos = Mathf.Clamp01(h.DirPos + d);
                    changed = true;
                }
            }
            if (changed)
            {
                HeaterParts(st);
                if (hit <= 1) Seats.PlayFoley("corris_switch1", at[hit]);
                SendState(b, st);
            }
            return hit == 0 ? "HEATER" : hit == 1 ? "BLOWER" : hit == 2 ? "TEMPERATURE" : "DIRECTION";
        }
        // passager (Seats) : la voiture dont il est l'occupant
        public static string HeaterInputFor(Transform car, Transform view)
        {
            if (car == null) return null;
            foreach (VazCar.Built b in cars) { State st; if (b.Car == car.gameObject && states.TryGetValue(b, out st)) return HeaterInput(b, st, view); }
            return null;
        }

        // chaque image : voyant, souffle ; chaque seconde : liquide, habitacle, source de chaleur
        static FsmFloat ambient; static bool ambientSeen;
        static void HeaterTick(VazCar.Built b, State st)
        {
            Heater h = st.Heat;
            if (h == null) return;
            bool power = st.EngineOn || (st.Key != null && st.KeyIn && st.KeyPos > 0);
            if (h.Lamp != null) { bool on = h.On && power; if (h.Lamp.activeSelf != on) h.Lamp.SetActive(on); }
            if (h.FanSound != null)
            {
                bool blow = power && h.FanStep > 0;
                h.FanSound.volume = (float)h.FanStep / FanSteps;
                if (blow && !h.FanSound.isPlaying) h.FanSound.Play(); else if (!blow && h.FanSound.isPlaying) h.FanSound.Stop();
            }
            float now = Time.realtimeSinceStartup;
            if (now < h.NextTick) return;
            h.NextTick = now + 1f;
            HeatSource(b, st);
            if (!ambientSeen) { ambientSeen = true; ambient = FsmVariables.GlobalVariables.FindFsmFloat("AmbientTemperature"); }
            if (ambient == null) return;   // (My Summer Car : pas de temperature du corps)
            float air = ambient.Value;
            if (float.IsNaN(h.Coolant)) h.Coolant = air;
            if (float.IsNaN(h.Interior)) h.Interior = air;
            float blower = (float)h.FanStep / FanSteps, temp = 0.5f - h.TempPos, dir = 1f - h.DirPos;
            bool heating = power && h.On;
            // liquide (Radiator de la SORBET, toutes les 0,78 s : moteur tournant + 0,18..1,3 jusqu'au thermostat ; air -max(0,03, -air/270) ;
            // chauffage -ventilateur x temperature / 5)
            float k = 1f / 0.78f;
            if (st.EngineOn && h.Coolant < 88f) h.Coolant += 1.2f * k;   // (PowerOutput / 67 du moteur de la SORBET en marche : ~1,2)
            h.Coolant -= Mathf.Max(0.03f, -air / 270f) * k;
            if (heating) h.Coolant -= Mathf.Max(0f, blower * temp / 5f) * k;
            h.Coolant = Mathf.Clamp(h.Coolant, air, 89f);
            // habitacle (CarTempSorbet + HeaterUnit) : pertes, portieres ouvertes (angle / 1100 chacune), chauffage
            float loss = 0.02f;
            if (st.DoorAngle != null) foreach (float a in st.DoorAngle) loss += Mathf.Abs(a) / 1100f;
            if (st.HatchOpen) loss += 0.05f;
            h.Interior -= loss;
            if (heating) h.Interior += Mathf.Clamp((1f + dir) * blower * h.Coolant * temp / 100f, -1f, 1f);
            h.Interior = Mathf.Clamp(h.Interior, air, Mathf.Max(air, h.Coolant / 5f));
            if (h.SrcTemp != null) h.SrcTemp.Value = h.Interior;
        }

        static void FreeHeater(State st)
        {
            if (st == null || st.Heat == null || st.Heat.Src == null) return;
            GameObject body = GameObject.Find("PLAYER/BodyTemp");
            if (body != null) foreach (PlayMakerArrayListProxy p in body.GetComponents<PlayMakerArrayListProxy>()) if (p.referenceName == "HeatSources" && p.arrayList != null) p.arrayList.Remove(st.Heat.Src);
            Object.Destroy(st.Heat.Src); st.Heat.Src = null;
        }

        // ([Test] : etat du chauffage, pour les essais)
        public static string HeaterState(GameObject car)
        {
            foreach (VazCar.Built b in cars)
            {
                State st;
                if (b.Car != car || !states.TryGetValue(b, out st) || st.Heat == null) continue;
                Heater h = st.Heat;
                return "chauffage " + (h.On ? "marche" : "arret") + ", ventilateur " + h.FanStep + ", temperature " + h.TempPos.ToString("F2") + ", direction " + h.DirPos.ToString("F2")
                       + ", liquide " + h.Coolant.ToString("F1") + ", habitacle " + h.Interior.ToString("F1") + (h.SrcTemp != null ? ", source " + h.SrcTemp.Value.ToString("F1") : "");
            }
            return "pas de chauffage";
        }
        public static void TestHeater(GameObject car, bool on, int fan, float temp, float dir)
        {
            foreach (VazCar.Built b in cars)
            {
                State st;
                if (b.Car != car || !states.TryGetValue(b, out st) || st.Heat == null) continue;
                st.Heat.On = on; st.Heat.FanStep = fan; st.Heat.TempPos = temp; st.Heat.DirPos = dir; HeaterParts(st); SendState(b, st);
            }
        }
    }
}
