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
    // Postes des autres (demande de witefold, 11/10 : « chaque voiture sa radio ») : le jeu deplace l'unique jeu de canaux
    // dans le poste qu'on allume -- le geste d'un autre, rejoue ici, l'arrachait a notre poste (une seule radio a la fois).
    // Le vrai jeu de canaux reste aux gestes d'ici ; un poste allume par un autre joue une copie (canaux et Fetch du jeu,
    // tirage coupe) dans son haut-parleur, sur le meme morceau au meme moment, avec son volume et sa station : on entend
    // chaque voiture la ou elle est. Voir RemoteChange / LocalChange.
    public static class Radio
    {
        static AudioSource[] ch;              // Channel1, Folk
        static PlayMakerFSM[] upd;
        static bool found, muted;
        static float nextSend, nextFind, nextClips, nextLog;
        static Dictionary<string, AudioClip> clips;

        public static void OnLevelLoaded()
        {
            found = muted = false; ch = null; upd = null; clips = null; nextFind = 0;
            ghosts.Clear(); realT = null; fetch = null; lastSet = false; winK = null; winUntil = 0f;
        }

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
                realT = t; fetch = f;
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
            Watch(now);
            if (!Session.IsHost || now < nextSend || Session.RemoteCount == 0) return;
            nextSend = now + 1f;
            var w = new NetWriter(Msg.Radio).U8(ch.Length);
            foreach (AudioSource a in ch)
            {
                AudioClip c = a != null ? a.clip : null;
                w.Str(ClipKey(c)).F32(c != null ? a.time : 0f).Bool(a != null && a.isPlaying);
            }
            Session.Broadcast(w, false);
        }

        // Morceaux importes (dossier Radio : Toivoradio) : charges depuis les fichiers, SANS NOM -- l'hote envoyait un nom vide,
        // et l'invite coupait le canal (retour d'un joueur, 10/10 : « la radio perso ne marche que pour l'hote »). Ils sont
        // designes par leur rang dans la liste du jeu (Radio/Folk, ArrayList "Songs" : track1, track2... dans l'ordre, la
        // meme chez l'invite qui a recu les fichiers de l'hote) : "#chanson:<rang>".
        static string ClipKey(AudioClip c)
        {
            if (c == null) return "";
            if (c.name.Length > 0) return c.name;
            System.Collections.ArrayList songs = Songs();
            int i = songs != null ? songs.IndexOf(c) : -1;
            return i >= 0 ? "#chanson:" + i : "";
        }

        static System.Collections.ArrayList Songs()
        {
            GameObject folk = GameObject.Find("Radio/Folk");
            if (folk == null) return null;
            foreach (Component comp in folk.GetComponents<Component>())
            {
                if (comp == null || comp.GetType().Name != "PlayMakerArrayListProxy") continue;
                var rn = comp.GetType().GetField("referenceName");
                if (rn != null && (rn.GetValue(comp) as string) != "Songs") continue;
                var al = comp.GetType().GetField("_arrayList", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return al != null ? al.GetValue(comp) as System.Collections.ArrayList : null;
            }
            return null;
        }

        static AudioClip ClipNamed(string n)
        {
            AudioClip c;
            if (n.StartsWith("#chanson:"))
            {
                int i; System.Collections.ArrayList songs = Songs();
                return int.TryParse(n.Substring(9), out i) && songs != null && i >= 0 && i < songs.Count ? songs[i] as AudioClip : null;
            }
            if (clips != null && clips.TryGetValue(n, out c) && c != null) return c;
            if (clips != null && Time.realtimeSinceStartup < nextClips) return null;
            nextClips = Time.realtimeSinceStartup + 5f;   // (morceaux du dossier Radio : charges en cours de partie)
            clips = new Dictionary<string, AudioClip>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(AudioClip))) if (o.name.Length > 0 && !clips.ContainsKey(o.name)) clips[o.name] = (AudioClip)o;
            return clips.TryGetValue(n, out c) ? c : null;
        }

        // ---------------------------------------------------------------- postes des autres
        class Ghost { public Transform K; public GameObject Go; public AudioSource[] Ch; public PlayMakerFSM Fetch; public int Who; }
        struct Snap { public Transform Parent; public Vector3 LP; public Quaternion LR; public bool On, M1, MF, St; }
        static readonly List<Ghost> ghosts = new List<Ghost>();
        static Transform realT; static PlayMakerFSM fetch;
        static Snap last, remoteBase; static bool lastSet, inRemote;
        static Transform winK; static int winWho; static float winUntil;

        static bool FB(string n) { FsmBool b = fetch.FsmVariables.FindFsmBool(n); return b != null && b.Value; }
        static void SetFB(PlayMakerFSM f, string n, bool v) { FsmBool b = f.FsmVariables.FindFsmBool(n); if (b != null) b.Value = v; }

        static Snap Take()
        {
            return new Snap { Parent = realT.parent, LP = realT.localPosition, LR = realT.localRotation, On = fetch.enabled,
                              M1 = FB("OnMuteChannel1"), MF = FB("OnMuteFolk"), St = FB("OnStatic") };
        }
        static bool Same(Snap a, Snap b) { return a.Parent == b.Parent && a.On == b.On && a.M1 == b.M1 && a.MF == b.MF && a.St == b.St; }
        static bool Off(Snap s) { return s.Parent == null || s.Parent.root.name == "RADIO"; }

        static void Restore(Snap s)
        {
            realT.parent = s.Parent; realT.localPosition = s.LP; realT.localRotation = s.LR;
            SetFB(fetch, "OnMuteChannel1", s.M1); SetFB(fetch, "OnMuteFolk", s.MF); SetFB(fetch, "OnStatic", s.St);
            if (fetch.enabled != s.On) fetch.enabled = s.On;
        }

        // Poste d'un automate : sa voiture, sinon sa racine (chaine de la maison).
        static Transform KeyOf(Transform t) { return VehicleSync.CarRoot(t) ?? t.root; }

        static Ghost GhostOf(Transform k) { foreach (Ghost g in ghosts) if (g.K == k) return g; return null; }

        static Ghost MakeGhost(Transform k, Transform speaker, int who)
        {
            var go = (GameObject)Object.Instantiate(realT.gameObject);
            go.name = "MWCoop-RadioAutre";
            foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName != "Fetch") f.enabled = false;   // (tirage des morceaux : celui du vrai)
            go.transform.parent = speaker; go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
            var g = new Ghost { K = k, Go = go, Who = who, Fetch = Game.FsmOn(go, "Fetch") };
            Transform c1 = go.transform.Find("Channel1"), fk = go.transform.Find("Folk");
            g.Ch = new[] { c1 != null ? c1.GetComponent<AudioSource>() : null, fk != null ? fk.GetComponent<AudioSource>() : null };
            if (g.Fetch != null) { g.Fetch.enabled = true; g.Fetch.SendEvent("FETCH"); }
            ghosts.Add(g);
            Log.Info("radio : poste de " + k.name + " allume par #" + who + ", joue ici sa propre copie (" + Recon.Path(speaker) + ")");
            return g;
        }

        static void Kill(Ghost g, string why)
        {
            if (g.Go != null) Object.Destroy(g.Go);
            ghosts.Remove(g);
            Log.Info("radio : poste de " + (g.K != null ? g.K.name : "?") + " " + why);
        }

        static void Flags(Ghost g, Snap s)
        {
            if (g.Fetch == null) return;
            SetFB(g.Fetch, "OnMuteChannel1", s.M1); SetFB(g.Fetch, "OnMuteFolk", s.MF); SetFB(g.Fetch, "OnStatic", s.St);
        }

        // Jobs : geste d'un autre rejoue sur une commande (bouton, molette). Ce qu'il fait au vrai jeu de canaux, s'il ne
        // joue pas dans ce poste-la, va a la copie de ce poste ; le vrai est remis comme avant.
        public static void BeforeRemote()
        {
            inRemote = found && realT != null && fetch != null;
            if (inRemote) remoteBase = Take();
        }

        public static void AfterRemote(Transform fsm, int who, string state)
        {
            if (!inRemote || realT == null || fsm == null) return;
            inRemote = false;
            Transform k = KeyOf(fsm);
            Snap now = Take();
            if (!Same(remoteBase, now)) RemoteChange(k, who, remoteBase, now);
            // (« Off » envoie OFF au jeu de canaux -- le vrai, eteint ici, n'en fait rien : la copie de ce poste s'arrete)
            if (state == "Off") { Ghost g = GhostOf(k); if (g != null) Kill(g, "eteint par #" + who); }
            winK = k; winWho = who; winUntil = Time.realtimeSinceStartup + 0.6f;   // (la suite de son geste, aux images suivantes)
            last = Take(); lastSet = true;
        }

        static void RemoteChange(Transform k, int who, Snap b, Snap n)
        {
            if (b.Parent != null && b.Parent.IsChildOf(k)) return;   // le vrai joue dans ce poste : son geste s'y applique
            Ghost g = GhostOf(k);
            if (Off(n)) { if (g != null) Kill(g, "eteint par #" + who); }
            else
            {
                Transform sp = n.Parent != null && n.Parent.IsChildOf(k) ? n.Parent : (g != null && g.Go != null ? g.Go.transform.parent : null);
                if (sp != null)
                {
                    if (g == null) g = MakeGhost(k, sp, who);
                    else if (g.Go.transform.parent != sp) { g.Go.transform.parent = sp; g.Go.transform.localPosition = Vector3.zero; g.Go.transform.localRotation = Quaternion.identity; }
                    g.Who = who;
                    Flags(g, n);
                }
            }
            Restore(b);
        }

        // Geste d'ici qui a change le vrai jeu de canaux : vers le poste d'une copie, il le reprend (copie retiree) ; sur le
        // poste d'une copie plus proche que le sien (eteindre, changer de station la radio d'un autre), il va a la copie.
        static void LocalChange(Snap b, Snap n)
        {
            if (n.Parent != null)
                foreach (Ghost g in ghosts) if (n.Parent.IsChildOf(g.K)) { Kill(g, "repris ici"); return; }
            Transform pl = Game.PlayerT;
            if (pl == null || ghosts.Count == 0) return;
            Ghost near = null; float nd = 3f * 3f;
            foreach (Ghost g in ghosts)
            {
                if (g.Go == null) continue;
                float d = (g.Go.transform.position - pl.position).sqrMagnitude;
                if (d < nd) { nd = d; near = g; }
            }
            if (near == null || (b.Parent != null && b.Parent.IsChildOf(near.K))) return;
            float dr = !Off(b) ? (b.Parent.position - pl.position).sqrMagnitude : float.MaxValue;
            if (nd >= dr) return;
            if (Off(n)) Kill(near, "eteint ici");
            else Flags(near, n);
            Restore(b);
        }

        static void Watch(float now)
        {
            if (realT == null || fetch == null) return;
            if (!lastSet) { last = Take(); lastSet = true; }
            Snap cur = Take();
            if (!Same(last, cur))
            {
                if (now < winUntil && winK != null) RemoteChange(winK, winWho, last, cur);
                else LocalChange(last, cur);
                last = Take();
            }
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                Ghost g = ghosts[i];
                if (g.Go == null || g.Go.transform.parent == null) { ghosts.RemoveAt(i); continue; }
                if (!Session.Players.ContainsKey(g.Who)) { Kill(g, "coupe : #" + g.Who + " parti"); continue; }
                for (int c = 0; c < 2 && c < g.Ch.Length; c++)
                {
                    AudioSource src = ch[c], dst = g.Ch[c];
                    if (src == null || dst == null) continue;
                    if (dst.gameObject.activeSelf != src.gameObject.activeSelf) dst.gameObject.SetActive(src.gameObject.activeSelf);
                    if (dst.clip != src.clip) { dst.clip = src.clip; if (src.clip != null) dst.time = Mathf.Min(src.time, src.clip.length - 0.05f); }
                    if (src.isPlaying && !dst.isPlaying && dst.clip != null && dst.gameObject.activeInHierarchy) { dst.Play(); dst.time = src.time; }
                    else if (!src.isPlaying && dst.isPlaying) dst.Stop();
                    else if (dst.isPlaying && dst.clip == src.clip && Mathf.Abs(dst.time - src.time) > 0.5f) dst.time = src.time;
                }
            }
        }

        // Essais : postes joues ici par copie.
        public static string GhostState()
        {
            var sb = new System.Text.StringBuilder("vrai jeu de canaux sous " + (realT != null && realT.parent != null ? Recon.Path(realT.parent) : "-") + (fetch != null ? (fetch.enabled ? " (allume" : " (eteint") + ", locale " + (!FB("OnMuteChannel1")) + ", folk " + (!FB("OnMuteFolk")) + ")" : ""));
            foreach (Ghost g in ghosts)
                sb.Append(" ; copie ").Append(g.K != null ? g.K.name : "?").Append(" de #").Append(g.Who).Append(" : ")
                  .Append(g.Ch[0] != null && g.Ch[0].clip != null ? g.Ch[0].clip.name + " a " + g.Ch[0].time.ToString("F0") + " s" : "-")
                  .Append(g.Ch[0] != null ? (g.Ch[0].isPlaying ? " joue" : " arrete") + (g.Ch[0].mute ? " muet" : "") + " vol " + g.Ch[0].volume.ToString("F2") : "");
            return sb.ToString();
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
                    bool swap = a.clip == null || ClipKey(a.clip) != name;
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
