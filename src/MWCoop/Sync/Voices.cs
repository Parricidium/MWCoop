using System.Collections.Generic;
using System.Reflection;
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
    // PNJ MENES PAR UN AUTRE (Npcs : clients, hostiles, policiers dont toute la logique est coupee ici, telephone du
    // personnel) : leur voix est celle de leur auteur. Chez l'auteur, leurs repliques partent aussi quand c'est un
    // autre joueur qui est a moins de 15 m (Npcs.MirroredSpeaker) ; chez les autres, ce qu'ils disent encore ici (appel
    // en cours au moment du suivi, replique relancee par un automate laisse tourner) est coupe (Npcs.MutedSpeaker), et
    // la copie recue est accrochee au PNJ (elle le suit). Une replique envoyee qui se tait avant la fin de son clip
    // (MasterAudioStopAllOfSound : Teppo insulte raccroche, PNJ interrompu) envoie un arret : les copies se taisent.
    public static class Voices
    {
        static readonly HashSet<string> PlayerGroups = new HashSet<string> {
            "Swearing", "Drunk", "Fuck", "Hangover", "Shit", "Burb", "Yes", "Death", "PlayerMisc" };
        static readonly HashSet<string> NpcGroups = new HashSet<string> {
            "Callers", "DrunkLifter", "Katsastaja", "Teimo", "Fleetari", "StatusD", "NPH", "Pig", "Uncle", "Mummo",
            "Shitman", "Fighter", "Janitor", "Autovittuilija", "CopHome", "CopHighway", "Latanen", "Drunks",
            "Worker1", "Worker2", "Worker3Jani", "Boss", "Serkku", "MusicCritic", "TaxiCustomers", "MakkaraUkko",
            "WoodPeople", "KeijoPSK", "JouniPSK", "Livaloinen", "TaxiOwner", "Kirpparimummo", "TeppoSarkain" };

        // PNJ qui parlent seuls : si le joueur local est lui aussi pres d'eux, il les entend deja.
        static readonly HashSet<string> Chatty = new HashSet<string> {
            "Worker1", "Worker2", "Worker3Jani", "Drunks", "DrunkLifter", "Kirpparimummo", "MakkaraUkko", "WoodPeople", "TaxiCustomers", "Fighter" };
        static float nextWarn;

        // Sent : son depart a ete envoye (un arret partira s'il se tait avant la fin du clip).
        class Var
        {
            public string Group, Name; public AudioSource Src; public bool Was, Sent, Npc; public float LastTime; public AudioClip LastClip;
            public Component Updater; public FieldInfo FollowField; public bool Looked;
        }
        static readonly List<Var> vars = new List<Var>();
        static readonly Dictionary<string, Var> byKey = new Dictionary<string, Var>();
        // Copies jouees ici : par joueur d'origine et groupe/variante (un arret ou un nouveau depart les remplace).
        class Copy { public int Who; public string Key; public AudioSource Src; }
        static readonly List<Copy> copies = new List<Copy>();
        static float nextScan = -1, nextPoll;
        static Transform player;
        static int sent, played, stopsSent, stopsRecv, hushed;
        static bool followLogged;

        public static void OnLevelLoaded()
        {
            vars.Clear(); byKey.Clear(); copies.Clear(); player = null;
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
                    var x = new Var { Group = g.name, Name = v.name, Src = s, Was = s.isPlaying, Npc = NpcGroups.Contains(g.name) };
                    vars.Add(x);
                    byKey[g.name + "/" + v.name] = x;
                }
            }
            Log.Info("voix : " + vars.Count + " repliques suivies");
        }

        // Objet que la variante suit (SoundGroupVariationUpdater de MasterAudio : celui qui parle) ; null si elle
        // a ete jouee a une position fixe ou si cette version de MasterAudio ne le dit pas (on juge alors a la position).
        static Transform Follow(Var x)
        {
            if (!x.Looked)
            {
                x.Looked = true;
                foreach (Component c in x.Src.GetComponents<Component>())
                {
                    if (c == null) continue;
                    string tn = c.GetType().Name;
                    if (tn != "SoundGroupVariationUpdater" && tn != "SoundGroupVariation") continue;
                    foreach (FieldInfo fi in c.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        if (fi.FieldType == typeof(Transform) && fi.Name.ToLowerInvariant().Contains("follow")) { x.Updater = c; x.FollowField = fi; break; }
                    if (x.FollowField != null) break;
                }
                if (!followLogged) { followLogged = true; Log.Info("voix : objet suivi par les variantes " + (x.FollowField != null ? x.Updater.GetType().Name + "." + x.FollowField.Name : "introuvable (jugement a la position)")); }
            }
            if (x.FollowField == null || x.Updater == null) return null;
            try { return x.FollowField.GetValue(x.Updater) as Transform; } catch { return null; }
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
                // Replique envoyee finie ou remplacee ; coupee avant la fin de son clip : arret envoye.
                if (x.Sent && (started || !playing))
                {
                    x.Sent = false;
                    if (!playing && x.LastClip != null && x.LastTime < x.LastClip.length - 0.3f) SendStop(x);
                }
                x.Was = playing;
                x.LastTime = playing ? x.Src.time : 0f;
                x.LastClip = x.Src.clip;
                if (!playing || x.Src.clip == null) continue;
                Vector3 pos = x.Src.transform.position;
                if (x.Npc)
                {
                    Transform follow = Follow(x);
                    // PNJ mene par un autre, sa logique coupee ici : sa voix est celle de son auteur.
                    if (Npcs.MutedSpeaker(follow, pos) != null) { Hush(x, "PNJ mene par un autre joueur : sa voix vient de lui"); continue; }
                    if (!started) continue;
                    if ((pos - player.position).sqrMagnitude > 225f && !(RemoteNear(pos, 15f) && Npcs.MirroredSpeaker(follow, pos))) continue;
                }
                else if (!started) continue;
                Session.SendAll(new NetWriter(Msg.Voice).U8(Session.LocalId).Str(x.Group).Str(x.Name)
                    .Vec(pos).F32(x.Src.volume).F32(x.Src.pitch), true);
                x.Sent = true;
                if (++sent <= 20 || sent % 50 == 0) Log.Info("voix : " + sent + " repliques envoyees (" + x.Group + "/" + x.Name + ")");
            }
        }

        // Un autre joueur en partie a moins de r m.
        static bool RemoteNear(Vector3 pos, float r)
        {
            foreach (KeyValuePair<int, PlayerInfo> kv in Session.Players)
                if (kv.Key != Session.LocalId && kv.Value != null && kv.Value.Level == 1 && (kv.Value.State.Feet - pos).sqrMagnitude < r * r) return true;
            return false;
        }

        // Arret : meme message, volume negatif.
        static void SendStop(Var x)
        {
            Session.SendAll(new NetWriter(Msg.Voice).U8(Session.LocalId).Str(x.Group).Str(x.Name)
                .Vec(x.Src.transform.position).F32(-1f).F32(1f), true);
            if (++stopsSent <= 20 || stopsSent % 50 == 0) Log.Info("voix : " + x.Group + "/" + x.Name + " coupee a " + x.LastTime.ToString("F1") + " s, arret envoye (" + stopsSent + ")");
        }

        // Variante MasterAudio coupee comme le fait le jeu (SoundGroupVariation.Stop : son etat interne suit), sinon
        // la source.
        static readonly Dictionary<System.Type, MethodInfo> stopMethods = new Dictionary<System.Type, MethodInfo>();
        static void Hush(Var x, string why)
        {
            bool done = false;
            try
            {
                Component v = x.Src.GetComponent("SoundGroupVariation");
                if (v != null)
                {
                    MethodInfo m;
                    if (!stopMethods.TryGetValue(v.GetType(), out m))
                    {
                        foreach (MethodInfo c in v.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (c.Name != "Stop") continue;
                            bool bools = true;
                            foreach (ParameterInfo p in c.GetParameters()) if (p.ParameterType != typeof(bool)) bools = false;
                            if (bools && (m == null || c.GetParameters().Length < m.GetParameters().Length)) m = c;
                        }
                        stopMethods[v.GetType()] = m;
                    }
                    if (m != null)
                    {
                        var args = new object[m.GetParameters().Length];
                        for (int i = 0; i < args.Length; i++) args[i] = false;
                        m.Invoke(v, args);
                        done = true;
                    }
                }
            }
            catch { }
            if (!done || x.Src.isPlaying) x.Src.Stop();
            x.Was = false; x.LastTime = 0f;
            if (++hushed <= 20 || hushed % 50 == 0) Log.Info("voix : " + x.Group + "/" + x.Name + " coupee ici (" + why + ")");
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string group = r.Str(), name = r.Str();
            Vector3 pos = r.Vec();
            float vol = r.F32(), pitch = r.F32();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Voice).U8(who).Str(group).Str(name).Vec(pos).F32(vol).F32(pitch), true, who);
            string key = group + "/" + name;
            if (vol < 0f)
            {
                int n = StopCopies(who, key);
                if (++stopsRecv <= 20 || stopsRecv % 50 == 0) Log.Info("voix : " + key + " du joueur #" + who + " coupee chez lui, " + n + " copie(s) arretee(s) ici");
                return;
            }
            if (vars.Count == 0 && PlayerSync.InGame) Scan();
            Var x;
            if (!byKey.TryGetValue(key, out x) || x.Src == null || x.Src.clip == null)
            {
                if (Time.realtimeSinceStartup >= nextWarn) { nextWarn = Time.realtimeSinceStartup + 10f; Log.Warn("voix : " + key + " introuvable ici"); }
                return;
            }
            Transform speaker = null;
            if (PlayerGroups.Contains(group))
            {
                // Voix d'un joueur : depuis sa tete (sinon la variante est collee a la camera de l'autre).
                PlayerInfo p;
                if (Session.Players.TryGetValue(who, out p) && p.State.Head != Vector3.zero) pos = p.State.Head;
            }
            else
            {
                // PNJ mene par l'autre (logique coupee ici) : sa voix est la sienne, meme si ce PNJ bavarde seul ou
                // dit encore ici une replique (coupee).
                speaker = Npcs.MutedSpeaker(null, pos);
                if (speaker != null) HushGroup(group);
                else
                {
                    if (Chatty.Contains(group) && player != null && (player.position - pos).sqrMagnitude < 225f) return;
                    if (GroupTalking(group)) { Log.Info("voix : " + key + " ignoree, ce PNJ parle deja ici"); return; }
                }
            }
            StopCopies(who, key);
            Play(x.Src, pos, vol, pitch, who, key, speaker);
            if (++played <= 20 || played % 50 == 0) Log.Info("voix : " + key + " du joueur #" + who + " jouee en " + pos.ToString("F0") + (speaker != null ? " (sur " + speaker.name + ")" : ""));
        }

        static bool GroupTalking(string group)
        {
            foreach (Var v in vars)
                if (v.Group == group && v.Src != null && v.Src.isPlaying) return true;
            return false;
        }

        // Variantes de ce groupe qui jouent ici pour un PNJ mene par un autre.
        static void HushGroup(string group)
        {
            foreach (Var v in vars)
                if (v.Group == group && v.Src != null && v.Src.isPlaying && Npcs.MutedSpeaker(Follow(v), v.Src.transform.position) != null)
                    Hush(v, "remplacee par la replique de son auteur");
        }

        static int StopCopies(int who, string key)
        {
            int n = 0;
            for (int i = copies.Count - 1; i >= 0; i--)
            {
                Copy c = copies[i];
                if (c.Src == null) { copies.RemoveAt(i); continue; }
                if (c.Who != who || c.Key != key) continue;
                c.Src.Stop();
                Object.Destroy(c.Src.gameObject);
                copies.RemoveAt(i);
                n++;
            }
            return n;
        }

        static void Play(AudioSource model, Vector3 pos, float vol, float pitch, int who, string key, Transform follow)
        {
            var go = new GameObject("MWCoop voix");
            go.transform.position = pos;
            if (follow != null) go.transform.parent = follow;   // suit le PNJ (pose de son auteur)
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
            for (int i = copies.Count - 1; i >= 0; i--) if (copies[i].Src == null) copies.RemoveAt(i);
            copies.Add(new Copy { Who = who, Key = key, Src = s });
        }

        // Essais : ce qui parle a moins de r m (variantes MasterAudio de PNJ et copies recues), avec leur instant.
        public static string AudioNear(Vector3 pos, float r)
        {
            var sb = new System.Text.StringBuilder();
            float r2 = r * r;
            foreach (Var x in vars)
                if (x.Npc && x.Src != null && x.Src.isPlaying && (x.Src.transform.position - pos).sqrMagnitude < r2)
                    sb.Append(" variante ").Append(x.Group).Append('/').Append(x.Name).Append('@').Append(x.Src.time.ToString("F1"));
            foreach (Copy c in copies)
                if (c.Src != null && c.Src.isPlaying && (c.Src.transform.position - pos).sqrMagnitude < r2)
                    sb.Append(" copie ").Append(c.Key).Append('@').Append(c.Src.time.ToString("F1")).Append(" de #").Append(c.Who);
            return (sb.Length == 0 ? "silence" : sb.ToString().Substring(1)) + " (coupees ici " + hushed + ", arrets envoyes " + stopsSent + ", recus " + stopsRecv + ")";
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
