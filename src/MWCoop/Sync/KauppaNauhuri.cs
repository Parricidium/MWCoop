using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MWCoop
{
    // Magnetoscope (JD 07/11) : les boutons de la facade -- POWER, EJECT, REW, PLAY, FWD, SETUP, STOP, PAUSE -- se visent et se
    // pressent avec F, et s'enfoncent ; l'afficheur en bas de la facade donne l'etat et le compteur ; la cassette entre et sort
    // par la fente. Data : "<cassette>|<mode>|<position, s>|<dernier bouton>.<n>" ; mode 0 arret, 1 lecture, 2 pause, 3 avance,
    // 4 retour, 5 eteint. Ancien format ("<cassette>|1", "|0") : position 0. Chaque joueur recale la position quand Data change
    // (ancre locale) ; la position ecrite est celle de celui qui presse ; le bouton et son numero font enfoncer le meme bouton chez
    // les autres. Arret : la bande garde sa position (comme un vrai magnetoscope) ; REW la ramene au debut.
    public static partial class Kauppa
    {
        const int VStop = 0, VPlay = 1, VPause = 2, VFwd = 3, VRew = 4, VOff = 5;
        const float VcrFast = 5f;            // (avance et retour : cinq fois la vitesse)
        const float VcrPressDepth = 0.0022f; // (bouton enfonce, m)
        static readonly string[] VcrButtons = { "power", "eject", "rew", "play", "fwd", "setup", "stop", "pause" };
        static int vcrPressSeq;

        class VcrAnchor { public string Data; public float Pos, T; public int Mode; public string Btn = "", Tape = ""; }
        static readonly Dictionary<string, VcrAnchor> vcrAnchors = new Dictionary<string, VcrAnchor>();
        class VcrLook
        {
            public Texture2D Tex; public Color32[] Px; public Material Mat; public string Shown; public string Msg; public float MsgUntil;
            public readonly Dictionary<string, float> Pressed = new Dictionary<string, float>();
            public readonly Dictionary<string, Transform> Buttons = new Dictionary<string, Transform>();
            public GameObject Go;
        }
        static readonly Dictionary<string, VcrLook> vcrLooks = new Dictionary<string, VcrLook>();
        class TapeAnim { public GameObject Go; public Transform Vcr; public Vector3 From, To; public float T0, Dur; public Action Done; }
        static readonly List<TapeAnim> tapeAnims = new List<TapeAnim>();
        static readonly HashSet<string> localInsert = new HashSet<string>(), localEject = new HashSet<string>();
        static Material vcrIdleMat;
        static float vcrCleanAt;

        // ---------------------------------------------------------------- etat
        static int VcrModeOf(string data) { string[] a = data.Split('|'); int m; return a.Length > 1 && int.TryParse(a[1], out m) ? m : VStop; }
        static float VcrPosOf(string data) { string[] a = data.Split('|'); float p; return a.Length > 2 && float.TryParse(a[2], NumberStyles.Float, CultureInfo.InvariantCulture, out p) ? p : 0f; }
        static string VcrBtnOf(string data) { string[] a = data.Split('|'); return a.Length > 3 ? a[3] : ""; }
        static int VcrMode(Esine v) { return VcrModeOf(v.Data); }
        static float Rate(int m) { return m == VPlay ? 1f : m == VFwd ? VcrFast : m == VRew ? -VcrFast : 0f; }

        static VcrAnchor Anchor(Esine v, float now)
        {
            VcrAnchor a;
            if (!vcrAnchors.TryGetValue(v.Id, out a)) { a = new VcrAnchor(); vcrAnchors[v.Id] = a; }
            if (a.Data != v.Data)
            {
                string tape = Tape(v), btn = VcrBtnOf(v.Data);
                if (a.Data != null)
                {
                    // (chez les autres : le bouton presse s'enfonce, la cassette entre ou sort par la fente)
                    if (btn.Length > 0 && btn != a.Btn) { int dot = btn.IndexOf('.'); VcrPressAnim(v, dot > 0 ? btn.Substring(0, dot) : btn); }
                    if (a.Tape.Length == 0 && tape.Length > 0 && !localInsert.Remove(v.Id)) TapeInAnim(v, null);
                    if (a.Tape.Length > 0 && tape.Length == 0 && !localEject.Remove(v.Id)) TapeOutAnim(v, null);
                }
                a.Data = v.Data; a.Pos = VcrPosOf(v.Data); a.T = now; a.Mode = VcrModeOf(v.Data); a.Btn = btn; a.Tape = tape;
            }
            return a;
        }

        // position de la bande (s), bornee a 0 et, sauf en lecture (la neige apres la fin, comme avant), a la longueur connue
        static float VcrPos(Esine v, float now)
        {
            VcrAnchor a = Anchor(v, now);
            float p = Mathf.Max(0f, a.Pos + Rate(a.Mode) * (now - a.T));
            float len = TapeLength(Tape(v));
            if (len > 0f && a.Mode != VPlay) p = Mathf.Min(p, len);
            return p;
        }

        static void VcrSet(Esine v, int mode, float pos, string btn)
        {
            SetData(v, Tape(v) + "|" + mode + "|" + Mathf.Max(0f, pos).ToString("F2", CultureInfo.InvariantCulture) + "|" + btn);
        }

        static void VcrPress(Esine v, string btn, float now)
        {
            string tape = Tape(v);
            int m = VcrMode(v), nm = m;
            float pos = VcrPos(v, now);
            VcrLook lk = Look(v);
            VcrPressAnim(v, btn);
            string tag = btn + "." + (++vcrPressSeq % 1000);
            if (btn == "eject") { if (tape.Length > 0) Eject(v); else { lk.Msg = "NO TAPE"; lk.MsgUntil = now + 1.5f; VcrSet(v, m, pos, tag); } return; }
            if (btn == "power") nm = m == VOff ? VStop : VOff;
            else if (m == VOff) { }   // (eteint : seul POWER repond)
            else if (btn == "setup") { lk.Msg = "SETUP"; lk.MsgUntil = now + 2f; }
            else if (tape.Length == 0) { lk.Msg = "NO TAPE"; lk.MsgUntil = now + 1.5f; }
            else if (btn == "play") nm = VPlay;
            else if (btn == "pause") nm = m == VPause ? VPlay : m == VStop ? VStop : VPause;
            else if (btn == "stop") nm = VStop;
            else if (btn == "fwd") nm = m == VFwd ? VPlay : VFwd;
            else if (btn == "rew") nm = m == VRew ? VPlay : VRew;
            VcrSet(v, nm, pos, tag);
            L("video : " + v.Id + " bouton " + btn + " (" + m + " -> " + nm + ", " + pos.ToString("F1") + " s)");
        }

        static void OnLevelLoadedVcr()
        {
            foreach (TapeAnim a in tapeAnims) if (a.Go != null) UnityEngine.Object.Destroy(a.Go);
            tapeAnims.Clear(); vcrAnchors.Clear(); vcrLooks.Clear(); localInsert.Clear(); localEject.Clear();
        }

        static void VcrPressAnim(Esine v, string btn) { Look(v).Pressed[btn] = Time.realtimeSinceStartup + 0.18f; }

        // bouton vise (repere de chaque bouton, sa boite un peu agrandie), le plus proche
        static string VcrAimedButton(Esine v, Ray ray, float maxDist)
        {
            string best = null; float bd = maxDist;
            VcrLook lk = Look(v);
            foreach (KeyValuePair<string, Transform> kv in lk.Buttons)
            {
                MeshFilter mf = kv.Value != null ? kv.Value.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds lb = mf.sharedMesh.bounds; lb.Expand(0.004f);
                Ray lr = new Ray(kv.Value.InverseTransformPoint(ray.origin), kv.Value.InverseTransformDirection(ray.direction));
                float d;
                if (lb.IntersectRay(lr, out d) && d < bd) { bd = d; best = kv.Key; }
            }
            return best;
        }

        // ---------------------------------------------------------------- modele : boutons et afficheur
        static void VcrDress(GameObject g)
        {
            foreach (string b in VcrButtons)
            {
                Transform bt = g.transform.Find("bouton_" + b), lab = g.transform.Find("bouton_" + b + "_etiquette");
                if (bt != null && lab != null) lab.SetParent(bt, true);
            }
            Vector3 lo, hi;
            if (!ModelPoint("magnetoscope", "afficheur_min", out lo) || !ModelPoint("magnetoscope", "afficheur_max", out hi)) return;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(q.GetComponent<Collider>());
            q.name = "afficheur";
            q.transform.SetParent(g.transform, false);
            q.transform.localPosition = new Vector3((lo.x + hi.x) / 2f, (lo.y + hi.y) / 2f, lo.z - 0.0004f);
            q.transform.localScale = new Vector3(hi.x - lo.x, hi.y - lo.y, 1f);
            Renderer r = q.GetComponent<Renderer>();
            r.sharedMaterial = VcrIdleMat(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        const int DispW = 192, DispH = 18;
        static readonly Color32 DispBg = new Color32(7, 9, 11, 255), DispFg = new Color32(120, 236, 226, 255), DispDim = new Color32(34, 62, 64, 255);
        static Material DispMat(Texture2D t) { Shader us = Shader.Find("Unlit/Texture"); return new Material(us != null ? us : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = t, name = "magnetoscope_afficheur" }; }
        static Texture2D DispTex() { return new Texture2D(DispW, DispH, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "magnetoscope_afficheur" }; }

        // (expose en rayon, sans cassette : 0:00:00 eteint)
        static Material VcrIdleMat()
        {
            if (vcrIdleMat != null) return vcrIdleMat;
            Texture2D t = DispTex(); var px = new Color32[DispW * DispH];
            DrawDisplay(px, "", "0:00:00", true);
            t.SetPixels32(px); t.Apply(false);
            vcrIdleMat = DispMat(t);
            return vcrIdleMat;
        }

        static void DrawDisplay(Color32[] px, string state, string counter, bool dim)
        {
            for (int i = 0; i < px.Length; i++) px[i] = DispBg;
            Color32 fg = dim ? DispDim : DispFg;
            if (state.Length > 0) Kartturi.DrawText(px, DispW, DispH, 4, 7, state, fg, 1);
            if (counter.Length > 0) Kartturi.DrawText(px, DispW, DispH, 96 - Kartturi.TextWidth(counter, 2) / 2, 4, counter, fg, 2);
            Kartturi.DrawText(px, DispW, DispH, DispW - 4 - Kartturi.TextWidth("HI-FI", 1), 7, "HI-FI", dim ? DispDim : fg, 1);
        }

        static VcrLook Look(Esine v)
        {
            VcrLook lk;
            if (vcrLooks.TryGetValue(v.Id, out lk) && lk.Go == v.Go) return lk;
            if (lk != null) { if (lk.Mat != null) UnityEngine.Object.Destroy(lk.Mat); if (lk.Tex != null) UnityEngine.Object.Destroy(lk.Tex); }
            lk = new VcrLook { Go = v.Go };
            vcrLooks[v.Id] = lk;
            if (v.Go == null) return lk;
            foreach (string b in VcrButtons) { Transform t = v.Go.transform.Find("bouton_" + b); if (t != null) lk.Buttons[b] = t; }
            Transform d = v.Go.transform.Find("afficheur");
            if (d != null)
            {
                lk.Tex = DispTex(); lk.Px = new Color32[DispW * DispH]; lk.Mat = DispMat(lk.Tex);
                d.GetComponent<Renderer>().sharedMaterial = lk.Mat;
            }
            return lk;
        }

        static string Counter(float s) { int t = Mathf.FloorToInt(s); return (t / 3600) + ":" + (t / 60 % 60).ToString("00") + ":" + (t % 60).ToString("00"); }

        // ---------------------------------------------------------------- chaque image
        static void TickVcrs(float now)
        {
            foreach (Esine v in esineet)
            {
                if (v.Kind != KNauhuri || v.Go == null) continue;
                VcrAnchor a = Anchor(v, now);
                string tape = Tape(v);
                float pos = VcrPos(v, now);
                // (fin de la course : retour au debut, avance a la fin -- l'hote seul l'ecrit, pour tous)
                if (BagAuthority && tape.Length > 0)
                {
                    float raw = a.Pos + Rate(a.Mode) * (now - a.T), len = TapeLength(tape);
                    if (a.Mode == VRew && raw <= 0f) VcrSet(v, VStop, 0f, a.Btn);
                    else if (a.Mode == VFwd && len > 0f && raw >= len) VcrSet(v, VStop, len, a.Btn);
                }
                VcrLook lk = Look(v);
                float rt = Time.realtimeSinceStartup;
                foreach (KeyValuePair<string, Transform> kv in lk.Buttons)
                {
                    if (kv.Value == null) continue;
                    float until;
                    bool down = lk.Pressed.TryGetValue(kv.Key, out until) && rt < until;
                    Vector3 want = down ? new Vector3(0f, 0f, VcrPressDepth) : Vector3.zero;
                    if (kv.Value.localPosition != want) kv.Value.localPosition = want;
                }
                if (lk.Tex == null) continue;
                string state, counter;
                if (a.Mode == VOff) { state = ""; counter = ""; }
                else
                {
                    state = now < lk.MsgUntil ? lk.Msg : tape.Length == 0 ? "NO TAPE" : a.Mode == VPlay ? "PLAY" : a.Mode == VPause ? "PAUSE" : a.Mode == VFwd ? "FF" : a.Mode == VRew ? "REW" : "STOP";
                    counter = tape.Length == 0 ? "-:--:--" : Counter(pos);
                }
                string key = state + "/" + counter;
                if (key == lk.Shown) continue;
                lk.Shown = key;
                if (a.Mode == VOff) { for (int i = 0; i < lk.Px.Length; i++) lk.Px[i] = DispBg; }
                else DrawDisplay(lk.Px, state, counter, false);
                lk.Tex.SetPixels32(lk.Px); lk.Tex.Apply(false);
            }
            TickTapeAnims();
            if (now >= vcrCleanAt)
            {
                vcrCleanAt = now + 10f;
                var gone = new List<string>();
                foreach (string id in vcrLooks.Keys) if (FindItem(id) == null) gone.Add(id);
                foreach (string id in gone) { VcrLook lk = vcrLooks[id]; if (lk.Mat != null) UnityEngine.Object.Destroy(lk.Mat); if (lk.Tex != null) UnityEngine.Object.Destroy(lk.Tex); vcrLooks.Remove(id); vcrAnchors.Remove(id); }
            }
        }

        // ---------------------------------------------------------------- cassette dans la fente
        // fente (repere du magnetoscope) : dedans (cachee par la caisse), dehors (juste devant la facade)
        static bool TapeSlot(out Vector3 inside, out Vector3 outside)
        {
            inside = outside = Vector3.zero;
            Vector3 lo, hi;
            if (!ModelPoint("magnetoscope", "fente_min", out lo) || !ModelPoint("magnetoscope", "fente_max", out hi)) return false;
            Vector3 ts = ModelSize("cassette", new Vector3(0.188f, 0.025f, 0.103f));
            float x = (lo.x + hi.x) / 2f, y = (lo.y + hi.y) / 2f, front = lo.z;
            inside = new Vector3(x, y, front + ts.z / 2f + 0.01f);
            outside = new Vector3(x, y, front - ts.z / 2f - 0.004f);
            return true;
        }

        static GameObject AnimTape(Transform vcr)
        {
            Vector3 s; Transform sc;
            GameObject g = Model(KVhs, out s, out sc);
            g.name = "vhs (animation)";
            g.transform.SetParent(vcr, false);
            g.transform.localRotation = Quaternion.identity;   // (tranche vers l'exterieur, dessus en haut)
            return g;
        }

        static void AddTapeAnim(Transform vcr, GameObject g, Vector3 from, Vector3 to, float dur, Action done)
        {
            g.transform.localPosition = from;
            tapeAnims.Add(new TapeAnim { Go = g, Vcr = vcr, From = from, To = to, T0 = Time.realtimeSinceStartup, Dur = dur, Done = done });
        }

        // entree : depuis la main (si connue) jusqu'a la fente, puis dedans
        static bool TapeInAnim(Esine v, Vector3? fromWorld)
        {
            Vector3 ins, outs;
            if (v.Go == null || !TapeSlot(out ins, out outs)) return false;
            Transform t = v.Go.transform;
            GameObject g = AnimTape(t);
            Action slide = () => { if (g != null && t != null) AddTapeAnim(t, g, outs, ins, 0.55f, () => UnityEngine.Object.Destroy(g)); else if (g != null) UnityEngine.Object.Destroy(g); };
            if (fromWorld.HasValue) AddTapeAnim(t, g, t.InverseTransformPoint(fromWorld.Value), outs, 0.25f, slide);
            else slide();
            return true;
        }

        // sortie : de dedans jusque devant la facade ; done (chez celui qui ejecte) pose la vraie cassette a la fin
        static bool TapeOutAnim(Esine v, Action done)
        {
            Vector3 ins, outs;
            if (v.Go == null || !TapeSlot(out ins, out outs)) return false;
            Transform t = v.Go.transform;
            GameObject g = AnimTape(t);
            AddTapeAnim(t, g, ins, outs, 0.6f, () => { UnityEngine.Object.Destroy(g); if (done != null) done(); });
            return true;
        }

        static void TickTapeAnims()
        {
            float rt = Time.realtimeSinceStartup;
            for (int i = tapeAnims.Count - 1; i >= 0; i--)
            {
                TapeAnim a = tapeAnims[i];
                // (magnetoscope disparu en route : la suite quand meme -- une cassette ejectee ne se perd jamais)
                if (a.Go == null || a.Vcr == null) { if (a.Go != null) UnityEngine.Object.Destroy(a.Go); tapeAnims.RemoveAt(i); if (a.Done != null) a.Done(); continue; }
                float k = Mathf.Clamp01((rt - a.T0) / a.Dur);
                a.Go.transform.localPosition = Vector3.Lerp(a.From, a.To, k * k * (3f - 2f * k));
                if (k >= 1f) { tapeAnims.RemoveAt(i); if (a.Done != null) a.Done(); }
            }
        }

        // vraie cassette, a la fin de l'ejection : devant la fente si la place est libre, sinon sur le dessus (comme avant)
        static void EjectSpawn(Esine v, string tape, Vector3 lastPos, Quaternion lastRot)
        {
            Vector3 ins, outs;
            Transform t = v.Go != null ? v.Go.transform : null;
            Vector3 pos = t != null && TapeSlot(out ins, out outs) ? t.TransformPoint(outs) : lastPos;
            Quaternion rot = t != null ? t.rotation : lastRot;
            bool free = true;
            Vector3 right = rot * Vector3.right;
            foreach (Vector3 c in new[] { pos, pos + right * 0.07f, pos - right * 0.07f })
                foreach (Collider col in Physics.OverlapSphere(c, 0.012f))
                    if (col != null && !col.isTrigger && (v.Col == null || col != v.Col) && col.gameObject.layer != 2) { free = false; break; }
            if (!free && t != null)
            {
                Bounds b = v.Col != null ? v.Col.bounds : new Bounds(t.position, Vector3.one * 0.2f);
                pos = new Vector3(b.center.x, b.max.y + 0.045f, b.center.z);
            }
            SpawnItem(KVhs, tape, pos, rot);
            L("video : cassette ejectee de " + v.Id + (free ? " (devant la fente)" : " (sur le dessus)"));
        }
    }
}
