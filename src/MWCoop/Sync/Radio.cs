using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Radio commune (demande d'un joueur, 07/10 : "meme morceau au meme moment"). Le jeu n'a qu'un jeu de canaux,
    // RadioChannels (Channel1 : radio locale, programmes et musique ; Folk : la radio folk), deplace vers le poste
    // allume (Fetch :: SetParent) ; chaque canal est une AudioSource dont l'automate 'Update' tire le morceau suivant AU
    // HASARD (ArrayListGetRandom) : chacun entendait autre chose. Les programmes suivent deja l'heure (commune).
    //  - hote : chaque seconde, pour chaque canal, le morceau (nom du clip), sa position et s'il joue ;
    //  - invite : l'automate 'Update' des canaux coupe (plus de tirage local) ; meme clip (retrouve par son nom : ceux
    //    du jeu, et ceux du dossier Radio, recopies depuis l'hote depuis 0.37), meme position (ecart > 1,5 s :
    //    recalee), lecture comme chez l'hote. Volume, poste allume, parasites : a chacun (Fetch reste local).
    public static class Radio
    {
        static AudioSource[] ch;              // Channel1, Folk
        static PlayMakerFSM[] upd;
        static bool found, muted;
        static float nextSend, nextFind, nextClips, nextLog;
        static Dictionary<string, AudioClip> clips;

        public static void OnLevelLoaded() { found = muted = false; ch = null; upd = null; clips = null; nextFind = 0; }

        static bool Find()
        {
            if (found) return ch != null;
            if (Time.realtimeSinceStartup < nextFind) return false;
            nextFind = Time.realtimeSinceStartup + 5f;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Fetch" || f.gameObject.name != "RadioChannels") continue;
                Transform t = f.transform;
                Transform c1 = t.Find("Channel1"), fk = t.Find("Folk");
                if (c1 == null || fk == null) continue;
                ch = new[] { c1.GetComponent<AudioSource>(), fk.GetComponent<AudioSource>() };
                upd = new[] { Game.FsmOn(c1.gameObject, "Update"), Game.FsmOn(fk.gameObject, "Update") };
                if (ch[0] == null || ch[1] == null) { ch = null; continue; }
                found = true;
                Log.Info("radio : canaux trouves (" + Recon.Path(t) + ")");
                return true;
            }
            return false;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Find()) return;
            float now = Time.realtimeSinceStartup;
            if (!Session.IsHost || now < nextSend || Session.RemoteCount == 0) return;
            nextSend = now + 1f;
            var w = new NetWriter(Msg.Radio).U8(ch.Length);
            foreach (AudioSource a in ch)
            {
                AudioClip c = a != null ? a.clip : null;
                w.Str(c != null ? c.name : "").F32(c != null ? a.time : 0f).Bool(a != null && a.isPlaying);
            }
            Session.Broadcast(w, false);
        }

        static AudioClip ClipNamed(string n)
        {
            AudioClip c;
            if (clips != null && clips.TryGetValue(n, out c) && c != null) return c;
            if (clips != null && Time.realtimeSinceStartup < nextClips) return null;
            nextClips = Time.realtimeSinceStartup + 5f;   // (morceaux du dossier Radio : charges en cours de partie)
            clips = new Dictionary<string, AudioClip>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(AudioClip))) if (o.name.Length > 0 && !clips.ContainsKey(o.name)) clips[o.name] = (AudioClip)o;
            return clips.TryGetValue(n, out c) ? c : null;
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            if (Session.IsHost || !PlayerSync.InGame || !Find()) return;
            if (!muted)
            {
                muted = true;
                foreach (PlayMakerFSM f in upd) if (f != null) f.enabled = false;
                Log.Info("radio : morceaux choisis par l'hote (tirage local coupe)");
            }
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                string name = r.Str();
                float t = r.F32();
                bool playing = r.Bool();
                if (i >= ch.Length || ch[i] == null) continue;
                AudioSource a = ch[i];
                try
                {
                    if (name.Length == 0) { if (a.isPlaying) a.Stop(); continue; }
                    bool swap = a.clip == null || a.clip.name != name;
                    if (swap)
                    {
                        AudioClip c = ClipNamed(name);
                        if (c == null)
                        {
                            if (Time.realtimeSinceStartup >= nextLog) { nextLog = Time.realtimeSinceStartup + 30f; Log.Warn("radio : morceau de l'hote introuvable ici : " + name); }
                            continue;
                        }
                        a.clip = c;
                    }
                    if (swap || Mathf.Abs(a.time - t) > 1.5f) a.time = Mathf.Clamp(t, 0f, Mathf.Max(0f, a.clip.length - 0.05f));
                    if (playing && !a.isPlaying && a.gameObject.activeInHierarchy) a.Play();
                    else if (!playing && a.isPlaying) a.Stop();
                    if (swap) Log.Info("radio : canal " + (i == 0 ? "local" : "folk") + " -> " + name + " a " + t.ToString("F0") + " s (hote)");
                }
                catch (System.Exception e) { if (Time.realtimeSinceStartup >= nextLog) { nextLog = Time.realtimeSinceStartup + 30f; Log.Warn("radio : " + e.Message); } }
            }
        }
    }
}
