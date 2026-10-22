using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Sommeil des joueurs (Донской, 15/10 : « qui dort en ce moment, comme Minecraft » ; квазарик et Menor, 15/10 : « un son quand
    // un joueur dort, celui des PNJ ») : quand la liste des dormeurs change, un message « Dorment : A, B (2/4) » chez tous ; et
    // chaque avatar endormi ronfle -- le son de ronflement du jeu (groupe MasterAudio des PNJ qui dorment), en boucle sur sa tete,
    // entendu a 12 m. [Coop] Ronflements=0 : sans le son.
    public static class Sleepers
    {
        static readonly List<int> was = new List<int>();
        static readonly Dictionary<int, AudioSource> snores = new Dictionary<int, AudioSource>();
        static AudioClip clip; static bool clipLooked;
        static float next;

        public static void OnLevelLoaded()
        {
            was.Clear();
            foreach (AudioSource s in snores.Values) if (s != null) Object.Destroy(s.gameObject);
            snores.Clear(); clip = null; clipLooked = false; next = 0f;
        }

        public static void Update()
        {
            if (!PlayerSync.InGame || !Session.Active) return;
            float now = Time.realtimeSinceStartup;
            if (now < next) return;
            next = now + 0.5f;
            var cur = new List<int>(); var names = new List<string>(); int total = 0;
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (pi.Level != 1) continue;
                total++;
                bool asleep = pi.Local ? Game.GlobalBool("PlayerSleeps") : (pi.State.Flags & PlayerSync.F_Sleep) != 0;
                if (asleep) { cur.Add(pi.Id); names.Add(pi.Name); }
            }
            bool changed = cur.Count != was.Count;
            for (int i = 0; !changed && i < cur.Count; i++) changed |= !was.Contains(cur[i]);
            if (changed)
            {
                bool wasSome = was.Count > 0;
                was.Clear(); was.AddRange(cur);
                if (cur.Count > 0) Hud.Toast(Lang.T("Dorment : ", "Sleeping: ") + string.Join(", ", names.ToArray()) + " (" + cur.Count + "/" + total + ")");
                else if (wasSome) Hud.Toast(Lang.T("Tout le monde est debout", "Everyone is up"));
            }
            Snoring(cur);
        }

        static void Snoring(List<int> cur)
        {
            bool on = Config.GetInt("Coop", "Ronflements", 1) != 0;
            var gone = new List<int>();
            foreach (KeyValuePair<int, AudioSource> kv in snores) if (!Session.Players.ContainsKey(kv.Key) || !on || !cur.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int id in gone) { AudioSource s = snores[id]; if (s != null) Object.Destroy(s.gameObject); snores.Remove(id); }
            if (!on) return;
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (pi.Local || !cur.Contains(pi.Id)) continue;
                if (clip == null && !clipLooked) { clipLooked = true; clip = FindClip(); }
                if (clip == null) return;
                AudioSource s;
                if (!snores.TryGetValue(pi.Id, out s) || s == null)
                {
                    var go = new GameObject("MWCoop-ronflement");
                    s = go.AddComponent<AudioSource>();
                    s.clip = clip; s.loop = true; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1.5f; s.maxDistance = 12f;
                    s.volume = Mathf.Clamp01(Config.GetFloat("Coop", "VolumeRonflements", 80f) / 100f);
                    s.Play();
                    snores[pi.Id] = s;
                    Log.Info("sommeil : " + pi.Name + " ronfle (" + clip.name + ")");
                }
                s.transform.position = pi.State.Head;
            }
        }

        // Le ronflement du jeu : le clip « snore » des PNJ qui dorment (AudioPlay de leur automate), cherche parmi les clips charges.
        static AudioClip FindClip()
        {
            foreach (AudioClip c in Resources.FindObjectsOfTypeAll<AudioClip>()) if (c != null && c.name == "snore") return c;
            Log.Warn("sommeil : clip « snore » introuvable");
            return null;
        }
    }
}
