using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Voix : repliques des PNJ (Teimo, Fleetari, l'oncle, la police...) et jurons du joueur.
    // Le jeu les joue par MasterAudio : MasterAudio/<groupe>/<variante>, une AudioSource par
    // variante, placee sur celui qui parle. On regarde 20 fois par seconde les variantes des groupes
    // de voix ; quand l'une demarre, on envoie groupe, variante, position, volume et hauteur.
    // L'autre cote joue le meme clip au meme endroit sur une source a part (copie des reglages 3D),
    // la voix d'un joueur sortant de la tete de son avatar. Rien n'est renvoye en echo : la copie
    // n'est pas une variante MasterAudio.
    // Les PNJ qui bavardent seuls (usine, marche aux puces) parlent deja des deux cotes : une replique
    // de PNJ ne part que si le joueur local est a moins de 15 m (c'est lui qui la provoque), et une
    // replique recue est ignoree si ce PNJ parle deja ici.
    public static class Voices
    {
        static readonly HashSet<string> PlayerGroups = new HashSet<string> {
            "Swearing", "Drunk", "Fuck", "Hangover", "Shit", "Burb", "Yes", "Death", "PlayerMisc" };
        static readonly HashSet<string> NpcGroups = new HashSet<string> {
            "Callers", "DrunkLifter", "Katsastaja", "Teimo", "Fleetari", "StatusD", "NPH", "Pig", "Uncle", "Mummo",
            "Shitman", "Fighter", "Janitor", "Autovittuilija", "CopHome", "CopHighway", "Latanen", "Drunks",
            "Worker1", "Worker2", "Worker3Jani", "Boss", "Serkku", "MusicCritic", "TaxiCustomers", "MakkaraUkko",
            "WoodPeople", "KeijoPSK", "JouniPSK", "Livaloinen", "TaxiOwner", "Kirpparimummo", "TeppoSarkain" };

        class Var { public string Group, Name; public AudioSource Src; public bool Was; public float LastTime; public AudioClip LastClip; }
        static readonly List<Var> vars = new List<Var>();
        static readonly Dictionary<string, Var> byKey = new Dictionary<string, Var>();
        static float nextScan = -1, nextPoll;
        static Transform player;
        static int sent, played;

        public static void OnLevelLoaded()
        {
            vars.Clear(); byKey.Clear(); player = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 6f : -1;
        }

        static void Scan()
        {
            vars.Clear(); byKey.Clear();
            GameObject ma = GameObject.Find("MasterAudio");
            if (ma == null) return;
            foreach (Transform g in ma.transform)
            {
                if (!PlayerGroups.Contains(g.name) && !NpcGroups.Contains(g.name)) continue;
                foreach (Transform v in g)
                {
                    AudioSource s = v.GetComponent<AudioSource>();
                    if (s == null) continue;
                    var x = new Var { Group = g.name, Name = v.name, Src = s, Was = s.isPlaying };
                    vars.Add(x);
                    byKey[g.name + "/" + v.name] = x;
                }
            }
            Log.Info("voix : " + vars.Count + " repliques suivies");
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = float.MaxValue; Scan(); }
            if (now < nextPoll || Session.RemoteCount == 0) return;
            nextPoll = now + 0.05f;
            if (player == null) { GameObject p = GameObject.Find("PLAYER"); if (p == null) return; player = p.transform; }
            foreach (Var x in vars)
            {
                if (x.Src == null) continue;
                bool playing = x.Src.isPlaying;
                bool started = playing && (!x.Was || x.Src.clip != x.LastClip || x.Src.time + 0.05f < x.LastTime);
                x.Was = playing;
                x.LastTime = playing ? x.Src.time : 0f;
                x.LastClip = x.Src.clip;
                if (!started || x.Src.clip == null) continue;
                if (NpcGroups.Contains(x.Group) && (x.Src.transform.position - player.position).sqrMagnitude > 225f) continue;
                Session.SendAll(new NetWriter(Msg.Voice).U8(Session.LocalId).Str(x.Group).Str(x.Name)
                    .Vec(x.Src.transform.position).F32(x.Src.volume).F32(x.Src.pitch), true);
                if (++sent <= 20 || sent % 50 == 0) Log.Info("voix : " + sent + " repliques envoyees (" + x.Group + "/" + x.Name + ")");
            }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string group = r.Str(), name = r.Str();
            Vector3 pos = r.Vec();
            float vol = r.F32(), pitch = r.F32();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Voice).U8(who).Str(group).Str(name).Vec(pos).F32(vol).F32(pitch), true, who);
            if (vars.Count == 0 && PlayerSync.InGame) Scan();
            Var x;
            if (!byKey.TryGetValue(group + "/" + name, out x) || x.Src == null || x.Src.clip == null) { Log.Warn("voix : " + group + "/" + name + " introuvable ici"); return; }
            if (PlayerGroups.Contains(group))
            {
                // Voix d'un joueur : depuis sa tete (sinon la variante est collee a la camera de l'autre).
                PlayerInfo p;
                if (Session.Players.TryGetValue(who, out p) && p.State.Head != Vector3.zero) pos = p.State.Head;
            }
            else if (GroupTalking(group)) { Log.Info("voix : " + group + "/" + name + " ignoree, ce PNJ parle deja ici"); return; }
            Play(x.Src, pos, vol, pitch);
            if (++played <= 20 || played % 50 == 0) Log.Info("voix : " + group + "/" + name + " du joueur #" + who + " jouee en " + pos.ToString("F0"));
        }

        static bool GroupTalking(string group)
        {
            foreach (Var v in vars)
                if (v.Group == group && v.Src != null && v.Src.isPlaying) return true;
            return false;
        }

        static void Play(AudioSource model, Vector3 pos, float vol, float pitch)
        {
            var go = new GameObject("MWCoop voix");
            go.transform.position = pos;
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = model.clip;
            s.volume = vol;
            s.pitch = pitch <= 0.01f ? 1f : pitch;
            s.spatialBlend = 1f;
            s.rolloffMode = model.rolloffMode;
            s.minDistance = model.minDistance;
            s.maxDistance = model.maxDistance;
            s.dopplerLevel = 0f;
            s.outputAudioMixerGroup = model.outputAudioMixerGroup;
            s.Play();
            Object.Destroy(go, model.clip.length / s.pitch + 0.2f);
        }

        // Essais : fait parler le premier PNJ du groupe donne (comme le jeu : la variante joue).
        public static string Test(string group)
        {
            if (nextScan == float.MaxValue && vars.Count == 0) Scan();
            foreach (Var x in vars)
                if (x.Group == group && x.Src.clip != null)
                {
                    x.Src.transform.position = GameObject.Find("PLAYER").transform.position + Vector3.forward * 2f;
                    x.Src.Play();
                    return group + "/" + x.Name + " (" + x.Src.clip.name + ", " + x.Src.clip.length.ToString("F1") + " s)";
                }
            return "aucune variante jouable dans " + group + " (" + vars.Count + " suivies)";
        }
    }
}
