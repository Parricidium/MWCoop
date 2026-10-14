using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Sons des copies chez les invites (Nightly 5, revue du 14/10 : « sorties muettes ») : le moteur du bus, des voitures du
    // trafic et des PNJ est joue et regle (volume, hauteur selon le regime) par leurs automates, coupes chez l'invite (copie
    // cinematique). Essai Autotest=sonsonde a la PSK : chez l'hote NPC_CARS/.../BUS/LOD/EngineSound joue (volume et hauteur
    // de 0,5 a 0,8), chez l'invite rien. Ici : l'hote suit les sources actives de TRAFFIC et NPC_CARS a moins de 300 m d'un
    // joueur et dit si elles jouent, leur volume et leur hauteur (@son : changements 4 fois par seconde, tout toutes les 3 s) ;
    // l'invite les pose (cle "s:" racine|rangs|nom, comme les lumieres de Particles).
    public static class CopySounds
    {
        class Snd { public string Key; public AudioSource A; public bool Play, Has; public float Vol, Pitch; public bool LastPlay; public float LastVol = -1f, LastPitch = -1f, NextTry; }
        static readonly Dictionary<string, Snd> byKey = new Dictionary<string, Snd>();
        static float nextScan, nextSend, nextFull, nextApply;
        static readonly KeyTable table = new KeyTable("@sonk");   // (cles courtes)
        public static void OnDefs(int who, NetReader r) { if (Session.IsHost || who != 0) { r.U8(); return; } table.OnDefs(r); }
        public static void ScheduleSnapshot() { table.ResendAll(); nextFull = 0f; }
        static int logs;
        const float Range = 300f;

        public static void OnLevelLoaded() { byKey.Clear(); table.Clear(); nextScan = nextFull = 0f; logs = 0; }

        static bool NearPlayer(Vector3 p)
        {
            Transform me = Game.PlayerT;
            if (me != null && (me.position - p).sqrMagnitude < Range * Range) return true;
            foreach (PlayerInfo pi in Session.Players.Values)
                if (!pi.Local && (pi.State.Feet - p).sqrMagnitude < Range * Range) return true;
            return false;
        }

        static void Scan()
        {
            int added = 0;
            foreach (string root in new[] { "TRAFFIC", "NPC_CARS" })
            {
                GameObject r = Game.FindAny(root);
                if (r == null) continue;
                foreach (AudioSource a in r.GetComponentsInChildren<AudioSource>(false))
                {
                    if (a == null || !NearPlayer(a.transform.position)) continue;
                    string k = Particles.ChainKey("s:", a.transform);
                    Snd s;
                    if (byKey.TryGetValue(k, out s)) { s.A = a; continue; }
                    byKey[k] = new Snd { Key = k, A = a };
                    added++;
                }
            }
            if (added > 0 && logs++ < 10) Log.Info("sons des copies : " + added + " source(s) de plus suivie(s) (" + byKey.Count + ")");
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (Session.IsHost)
            {
                if (Session.RemoteCount == 0) return;
                if (now >= nextScan) { nextScan = now + 2f; Scan(); }
                if (now < nextSend) return;
                nextSend = now + 0.25f;
                bool full = now >= nextFull;
                if (full) nextFull = now + 3f;
                var batch = new List<Snd>();
                foreach (Snd s in byKey.Values)
                {
                    if (s.A == null) continue;
                    bool play = s.A.isPlaying && s.A.enabled && s.A.gameObject.activeInHierarchy;
                    float vol = s.A.volume, pitch = s.A.pitch;
                    if (full || play != s.LastPlay || play && (Mathf.Abs(vol - s.LastVol) > 0.04f || Mathf.Abs(pitch - s.LastPitch) > 0.04f))
                    {
                        if (!full || play || s.LastPlay) batch.Add(s);   // (tout : seulement ce qui joue, ou vient de s'arreter)
                        s.LastPlay = play; s.LastVol = vol; s.LastPitch = pitch;
                    }
                }
                foreach (Snd s in batch) table.Id(s.Key);
                table.SendDefs();
                for (int i = 0; i < batch.Count; i += 64)
                {
                    int n = Mathf.Min(64, batch.Count - i);
                    var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@son").U8(n);
                    for (int k = 0; k < n; k++)
                    {
                        Snd s = batch[i + k];
                        w.U16(table.Id(s.Key)).Bool(s.LastPlay).U8(Mathf.Clamp(Mathf.RoundToInt(s.LastVol * 255f), 0, 255)).U8(Mathf.Clamp(Mathf.RoundToInt(s.LastPitch / 3f * 255f), 0, 255));
                    }
                    Session.SendAll(w, false);
                }
                return;
            }
            if (now < nextApply) return;   // invite : repose chaque seconde (rien ne les regle ici)
            nextApply = now + 1f;
            foreach (Snd s in byKey.Values) if (s.Has) Apply(s);
        }

        static void Apply(Snd s)
        {
            if (s.A == null && Time.realtimeSinceStartup >= s.NextTry) { GameObject g = Particles.ResolveActive(s.Key); s.A = g != null ? g.GetComponent<AudioSource>() : null; if (s.A == null) s.NextTry = Time.realtimeSinceStartup + 10f; }
            if (s.A == null || !s.A.gameObject.activeInHierarchy) return;
            s.A.volume = s.Vol; s.A.pitch = s.Pitch;
            if (s.Play && !s.A.isPlaying) { s.A.enabled = true; s.A.Play(); if (logs++ < 10) Log.Info("sons des copies : " + s.Key + " joue (hote)"); }
            else if (!s.Play && s.A.isPlaying) s.A.Stop();
        }

        public static void OnMessage(int who, NetReader r)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                int id = r.U16(); bool play = r.Bool(); float vol = r.U8() / 255f, pitch = r.U8() / 255f * 3f;
                if (Session.IsHost || who != 0) continue;
                string k = table.Key(id);
                if (k == null) continue;
                Snd s;
                if (!byKey.TryGetValue(k, out s)) byKey[k] = s = new Snd { Key = k };
                s.Play = play; s.Vol = vol; s.Pitch = pitch; s.Has = true;
                Apply(s);
            }
        }

        // (essai Autotest=sonsonde) sources suivies qui jouent ici
        public static string Report()
        {
            int n = 0, playing = 0, want = 0;
            foreach (Snd s in byKey.Values)
            {
                n++;
                if (s.A != null && s.A.isPlaying) playing++;
                if (Session.IsHost ? s.LastPlay : s.Play) want++;
            }
            return "sons des copies : " + n + " suivies, " + want + " jouent chez l'hote, " + playing + " jouent ici";
        }
    }
}
