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
    // Radio portable (EQUIPMENTS/radio(itemx), retour d'un joueur du 16/10 : « pas entendue par les autres ») : un poste comme
    // les autres (son bouton Power :: Knob 'Check channel' deplace le jeu de canaux dans son SoundSpeaker), mais ses boutons
    // sont des enfants de l'objet, rejoues par ItemFsms (sous Radio.BeforeRemote / AfterRemote, comme les molettes des
    // voitures par Jobs). Son poste = l'objet radio(itemx) (KeyOf), cle « radio(itemx) » sur le fil (SetKey : son chemin change
    // quand on la porte, PLAYER/...). La copie chez les autres nait dans son SoundSpeaker (enfant : elle suit l'objet porte) ;
    // volume et piles : le Play :: Charge local de chacun regle Settings.Volume du haut-parleur, que la copie lit (meme
    // sauvegarde au depart, meme consommation : a peu pres pareil) ; piles a plat chez son porteur (Charge 0, ou moins de
    // 10 piles : 'State 3'), son @poste dit « eteint » et la copie s'arrete chez les autres.
    // Lecteurs CD (CDAudioSource* : CORRIS, SORBET, maison, taxi) : une source PAR appareil, pas le jeu de canaux ; la piste est
    // sequentielle (Index remis a 0 a l'insertion, +1 a la fin de chaque piste ou par NEXT), rien n'est tire au sort ;
    // insertion (WorldFsms), lecture / piste suivante (Jobs en voiture, WorldFsms a la maison) et marche (Interactions) sont
    // rejouees -- mais un arrivant repart de la piste 1, les debuts different d'une latence, et un rejeu rate decale pour de
    // bon. Depuis le 16/10 l'hote fait reference pour les lecteurs comme pour les canaux : chaque seconde, par lecteur, le
    // disque (CurrentCD), le rang (Index), la position et s'il joue ; chez l'invite, l'automate 'Update' du lecteur est coupe
    // au premier message (plus d'enchainement local), le morceau est retrouve dans la liste du disque (Radio/CD, meme liste
    // chez tous : Contenu), rang et disque recopies dans ses variables. (L'afficheur « track 05 » du lecteur, ecrit par cet
    // automate, ne bouge plus chez l'invite.)
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
            cds.Clear(); nextCdFind = 0f; cdRoot = null; beatRecv.Clear(); tbStep = 0; tbLog = 0f;
        }

        static bool Find()
        {
            if (found) return ch != null;
            if (Time.realtimeSinceStartup < nextFind) return false;
            nextFind = Time.realtimeSinceStartup + 5f;
            foreach (Object o in Game.FsmsNamed("Fetch"))
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
            Beat(now);
            foreach (Cd c in cds) Nudge(c);
            if (Session.IsHost) CdResume(now);
            else if (now >= nextSend) { nextSend = now + 1f; ReportCds(now); }
            if (!Session.IsHost || now < nextSend || Session.RemoteCount == 0) return;
            nextSend = now + 1f;
            var w = new NetWriter(Msg.Radio).U8(ch.Length);
            foreach (AudioSource a in ch)
            {
                AudioClip c = a != null ? a.clip : null;
                w.Str(ClipKey(c)).F32(c != null ? a.time : 0f).Bool(a != null && a.isPlaying);
            }
            WriteCds(w, now);
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

        static System.Collections.ArrayList Songs() { return ArrayListOf(GameObject.Find("Radio/Folk"), "Songs"); }

        // Liste PlayMaker (PlayMakerArrayListProxy) de reference 'reference' sur 'go' : chansons de la radio (Radio/Folk,
        // "Songs"), pistes d'un disque (Radio/CD, "CD1"...).
        static System.Collections.ArrayList ArrayListOf(GameObject go, string reference)
        {
            if (go == null) return null;
            foreach (Component comp in go.GetComponents<Component>())
            {
                if (comp == null || comp.GetType().Name != "PlayMakerArrayListProxy") continue;
                var rn = comp.GetType().GetField("referenceName");
                if (rn != null && (rn.GetValue(comp) as string) != reference) continue;
                var al = comp.GetType().GetField("_arrayList", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return al != null ? al.GetValue(comp) as System.Collections.ArrayList : null;
            }
            return null;
        }

        // ---------------------------------------------------------------- lecteurs CD (voir l'en-tete)
        // Lecteur loin de l'hote (dossier #26, Podpivas_70RUS 30/10 : « le CD s'arrete chez l'invite a ~300 m de l'hote, reprend au
        // debut quand il revient ») : hors de portee chez l'hote, son objet est eteint (LOD de la voiture) -- l'hote, reference,
        // envoyait « arrete » et l'invite coupait sa musique ; au retour, l'automate du lecteur de l'hote repartait de zero. Depuis le
        // 30/10 : lecteur eteint chez l'hote -> rang -2 (« pas de reference ici ») ; l'invite qui l'a allume le rend a son jeu sans
        // couper la piste (Leading) et envoie son etat a l'hote chaque seconde ; l'hote le recopie (Report) et, son lecteur rallume,
        // reprend cette piste a cette position avant de redevenir la reference. [Test] CdLoinHote=0 : comme avant.
        class Cd { public string Name; public AudioSource A; public PlayMakerFSM Upd; public bool Muted; public string Last; public bool Leading; public float ReportAt = -10f, ReportT; public int ReportWho; public string Nudge; }
        static readonly List<Cd> cds = new List<Cd>();
        static float nextCdFind; static GameObject cdRoot;

        // Lecteurs du jeu (objets « CDAudioSource... » portant un automate 'Update' et une source) : revus toutes les 10 s
        // (taxi, voiture ajoutee). Nom unique par appareil : la cle sur le fil.
        static void FindCds(float now)
        {
            if (now < nextCdFind) return;
            nextCdFind = now + 10f;
            foreach (Object o in Game.FsmsNamed("Update"))
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                // (My Summer Car, 22/10 : le lecteur de la SATSUMA est SATSUMA/Electricity/SpeakerDash/CDPlayer -- memes automate 'Update',
                // variables CurrentCD / Index et listes Radio/CD ; le jeu de canaux RadioChannels (Channel1, Folk) et Radio/Folk sont les memes)
                if (f.hideFlags != HideFlags.None || f.FsmName != "Update" || !(f.gameObject.name.StartsWith("CDAudioSource") || Game.Msc && f.gameObject.name == "CDPlayer")) continue;
                bool known = false;
                foreach (Cd c in cds) if (c.Upd == f || c.Name == f.gameObject.name) { known = true; break; }
                if (known) continue;
                AudioSource a = f.GetComponent<AudioSource>();
                if (a == null) continue;
                cds.Add(new Cd { Name = f.gameObject.name, A = a, Upd = f });
                Log.Info("radio : lecteur CD trouve (" + Recon.Path(f.transform) + ")");
            }
        }

        static Cd CdNamed(string name) { foreach (Cd c in cds) if (c.Name == name) return c; return null; }

        // Hote : apres les canaux, les lecteurs -- nom, disque, rang, position, joue. (Un invite d'une version sans cette suite
        // s'arrete apres les canaux ; un hote sans elle n'envoie rien de plus : OnMessage lit si More.)
        static void WriteCds(NetWriter w, float now)
        {
            FindCds(now);
            int n = 0;
            foreach (Cd c in cds) if (c.A != null && c.Upd != null) n++;
            w.U8(Mathf.Min(n, 255));
            foreach (Cd c in cds)
            {
                if (c.A == null || c.Upd == null || n-- <= 0) continue;
                WriteCd(w, c, !c.A.gameObject.activeInHierarchy && LoinHote);
            }
        }
        static bool LoinHote { get { return Config.GetInt("Test", "CdLoinHote", 1) != 0; } }

        // Un lecteur sur le fil : nom, disque, rang (-2 : eteint ici, pas de reference -- dossier #26), position, joue.
        static void WriteCd(NetWriter w, Cd c, bool away)
        {
            FsmString cd = c.Upd.FsmVariables.FindFsmString("CurrentCD");
            FsmInt idx = c.Upd.FsmVariables.FindFsmInt("Index");
            bool playing = c.A.clip != null && c.A.isPlaying && c.A.gameObject.activeInHierarchy;
            int rank = idx != null ? idx.Value : -1;
            // (rang du morceau qui joue vraiment, pas la variable : sur la DERNIERE piste du disque, l'automate du jeu passe Index a -1
            // (« Reset ») avant de la jouer -- l'invite ne la trouvait pas et n'entendait pas la meme chose, dossier #21 рома 28/10)
            if (playing && cd != null && Config.GetInt("Test", "CdRangVariable", 0) == 0)
            {
                int k = RankOf(cd.Value, c.A.clip);
                if (k >= 0) rank = k;
            }
            w.Str(c.Name).Str(cd != null ? cd.Value ?? "" : "").I32(away ? -2 : rank).F32(playing ? c.A.time : 0f).Bool(playing);
        }
        static int RankOf(string cd, AudioClip clip)
        {
            if (cdRoot == null) cdRoot = GameObject.Find("Radio/CD");
            System.Collections.ArrayList l = ArrayListOf(cdRoot, cd);
            return l != null && clip != null ? l.IndexOf(clip) : -1;
        }

        // Invite : les lecteurs qu'il mene (eteints chez l'hote, dossier #26), a l'hote chaque seconde -- message Radio sans canaux
        // (un hote plus ancien ignore un Radio d'invite).
        static void ReportCds(float now)
        {
            int n = 0;
            foreach (Cd c in cds) if (c.Leading && c.A != null && c.Upd != null && c.A.gameObject.activeInHierarchy && c.A.isPlaying) n++;
            if (n == 0) return;
            var w = new NetWriter(Msg.Radio).U8(0).U8(Mathf.Min(n, 255));
            foreach (Cd c in cds)
                if (c.Leading && c.A != null && c.Upd != null && c.A.gameObject.activeInHierarchy && c.A.isPlaying && n-- > 0) WriteCd(w, c, false);
            Session.SendToHost(w, false);
        }

        // Rend le lecteur a l'automate du jeu SANS couper la piste qui joue : variables posees sur elle (Index, CurrentTrack, Audio, et
        // MaxTracks -- 0 chez qui n'avait jamais lance le disque lui-meme : la meme piste revenait en boucle), automate rallume tel quel ;
        // la piste finie, la suivante est lancee par 'Play track' (Nudge). ('Play track' tout de suite relancait la piste en cours : 20 s ->
        // 6 s en essai.)
        static void GiveBack(Cd c, string why)
        {
            AudioSource a = c.A;
            AudioClip clip = a.clip; float t = a.time; bool was = a.isPlaying;
            c.Muted = false; c.Last = null;
            FsmString cv = c.Upd.FsmVariables.FindFsmString("CurrentCD");
            int k = clip != null && cv != null ? RankOf(cv.Value, clip) : -1;
            if (k >= 0)
            {
                if (cdRoot == null) cdRoot = GameObject.Find("Radio/CD");
                System.Collections.ArrayList l = ArrayListOf(cdRoot, cv.Value);
                FsmInt iv = c.Upd.FsmVariables.FindFsmInt("Index"); if (iv != null) iv.Value = k;
                FsmInt ct = c.Upd.FsmVariables.FindFsmInt("CurrentTrack"); if (ct != null) ct.Value = k + 1;
                FsmInt mt = c.Upd.FsmVariables.FindFsmInt("MaxTracks"); if (mt != null && l != null) mt.Value = l.Count - 1;
                FsmObject au = c.Upd.FsmVariables.FindFsmObject("Audio"); if (au != null) au.Value = clip;
            }
            c.Upd.enabled = true;
            c.Nudge = was && k >= 0 ? c.Upd.ActiveStateName : null;
            Log.Info("radio : lecteur " + c.Name + " rendu a ce joueur, " + why + (was ? " (piste " + (k + 1) + " a " + t.ToString("F0") + " s, " + (a.isPlaying ? "continue" : "ARRETEE") + ", automate [" + c.Upd.ActiveStateName + "])" : ""));
        }

        // Lecteur rendu au jeu en pleine piste : a sa fin, piste suivante ('Play track' sur elle ; l'automate enchaine ensuite) -- sauf s'il a bouge entre-temps
        // (bouton, disque ejecte : il mene deja) ou s'il enchaine seul ('Play track').
        static void Nudge(Cd c)
        {
            if (c.Nudge == null || c.Muted || c.A == null || c.Upd == null) return;
            if (c.Nudge == "Play track" || c.Upd.ActiveStateName != c.Nudge || !c.Upd.enabled) { c.Nudge = null; return; }
            if (c.A.isPlaying || !c.A.gameObject.activeInHierarchy) return;
            c.Nudge = null;
            // (piste suivante prise dans la liste du disque, Radio/CD, comme partout ici -- apres la derniere, la premiere, comme le jeu)
            FsmString cv = c.Upd.FsmVariables.FindFsmString("CurrentCD");
            if (cdRoot == null) cdRoot = GameObject.Find("Radio/CD");
            System.Collections.ArrayList l = cv != null ? ArrayListOf(cdRoot, cv.Value) : null;
            int k = cv != null ? RankOf(cv.Value, c.A.clip) : -1;
            if (l == null || l.Count == 0 || k < 0) { Game.SetState(c.Upd, "State 6"); Log.Info("radio : lecteur " + c.Name + " : piste finie, suivante par le jeu"); return; }
            int next = k + 1 >= l.Count ? 0 : k + 1;
            AudioClip nc = l[next] as AudioClip;
            FsmInt iv = c.Upd.FsmVariables.FindFsmInt("Index"); if (iv != null) iv.Value = next;
            FsmInt ct = c.Upd.FsmVariables.FindFsmInt("CurrentTrack"); if (ct != null) ct.Value = next + 1;
            FsmInt mt = c.Upd.FsmVariables.FindFsmInt("MaxTracks"); if (mt != null) mt.Value = l.Count - 1;
            FsmObject au = c.Upd.FsmVariables.FindFsmObject("Audio"); if (au != null) au.Value = nc;
            Game.SetState(c.Upd, "Play track");
            Log.Info("radio : lecteur " + c.Name + " : piste finie, suivante " + (next + 1) + "/" + l.Count + " (" + (c.A.clip != null ? c.A.clip.name : "-") + ", " + (c.A.isPlaying ? "joue" : "arretee") + ")");
        }

        // Hote : etat d'un lecteur mene par un invite (eteint ici) -- recopie sans le jouer ; rallume ici (l'hote revient), la piste
        // de l'invite reprend a sa position et le lecteur est rendu au jeu (redevient la reference).
        static void OnReport(Peer from, NetReader r)
        {
            if (!LoinHote) return;
            int n = r.U8();
            for (int i = 0; i < n && r.More; i++) { r.Str(); r.F32(); r.Bool(); }
            if (!r.More) return;
            FindCds(Time.realtimeSinceStartup);
            int m = r.U8();
            for (int i = 0; i < m; i++)
            {
                string name = r.Str(), cd = r.Str(); int idx = r.I32(); float t = r.F32(); bool playing = r.Bool();
                Cd c = CdNamed(name);
                if (c == null || c.A == null || c.Upd == null || !playing || c.A.gameObject.activeInHierarchy) continue;
                AudioClip clip = CdClip(cd, idx);
                if (clip == null) continue;
                if (!c.Muted || c.ReportAt < Time.realtimeSinceStartup - 3f) Log.Info("radio : lecteur " + c.Name + " eteint ici, mene par #" + from.Id + " (" + cd + ":" + idx + ")");
                c.Muted = true; if (c.Upd.enabled) c.Upd.enabled = false;
                FsmString cv = c.Upd.FsmVariables.FindFsmString("CurrentCD"); if (cv != null && cd.Length > 0) cv.Value = cd;
                FsmInt iv = c.Upd.FsmVariables.FindFsmInt("Index"); if (iv != null) iv.Value = idx;
                c.A.clip = clip; c.ReportT = t; c.ReportAt = Time.realtimeSinceStartup; c.ReportWho = from.Id;
            }
        }

        // Hote, chaque image : lecteur mene par un invite, rallume ici -> reprise de sa piste, rendu au jeu ; plus de nouvelles (3 s) et
        // toujours eteint -> rendu au jeu tel quel.
        static void CdResume(float now)
        {
            foreach (Cd c in cds)
            {
                if (c.A == null || c.Upd == null || c.ReportAt < 0f) continue;
                bool on = c.A.gameObject.activeInHierarchy;
                if (on && c.Muted)
                {
                    float t = c.ReportT + (now - c.ReportAt);
                    AudioClip clip = c.A.clip;
                    if (clip != null && now - c.ReportAt < 3f && t < clip.length - 0.5f)
                    {
                        c.A.Play();
                        if (clip.loadType != AudioClipLoadType.Streaming) c.A.time = Mathf.Clamp(t, 0f, clip.length - 0.05f);
                    }
                    c.ReportAt = -10f;
                    GiveBack(c, "rallume ici, piste de #" + c.ReportWho + " reprise");
                }
                else if (!on && c.Muted && now - c.ReportAt > 3f) { c.ReportAt = -10f; c.Muted = false; c.Upd.enabled = true; Log.Info("radio : lecteur " + c.Name + " : plus de nouvelles de #" + c.ReportWho + ", rendu au jeu"); }
            }
        }

        // Piste 'idx' (0..) du disque 'cd' : la liste du disque sur Radio/CD (celle que lit l'automate du lecteur, ArrayListGet).
        static AudioClip CdClip(string cd, int idx)
        {
            if (cdRoot == null) cdRoot = GameObject.Find("Radio/CD");
            System.Collections.ArrayList l = ArrayListOf(cdRoot, cd);
            return l != null && idx >= 0 && idx < l.Count ? l[idx] as AudioClip : null;
        }

        // Invite : le lecteur suit l'hote. Son automate 'Update' coupe au premier message (il enchainerait sinon la piste
        // suivante tout seul a chaque arret d'ici) ; disque et rang recopies dans ses variables (pour qui les lit).
        static void ApplyCd(Cd c, string cd, int idx, float t, bool playing)
        {
            AudioSource a = c.A;
            // My Summer Car : les boutons du lecteur de la SATSUMA restent a chacun (Jobs.MscSkip, 22/10) -- l'hote ne sait donc pas qu'un invite
            // a mis un disque, et son « arrete » coupait chaque seconde le lecteur de l'invite : le CD ne marchait jamais chez lui (dossier #6,
            // 27/10). Lecteur de l'hote arrete : celui de l'invite lui est rendu (enchainement local) ; il ne suit l'hote que quand l'hote joue.
            if (Game.Msc && !playing)
            {
                if (c.Muted) { c.Muted = false; c.Upd.enabled = true; c.Last = null; Log.Info("radio : lecteur " + c.Name + " rendu a ce joueur (celui de l'hote est arrete)"); }
                return;
            }
            // (rang -2 : lecteur eteint chez l'hote, loin de lui -- dossier #26 : il reste a ce joueur, piste en cours non coupee ; l'hote en
            // recoit l'etat, ReportCds. Allume ici et qui joue : mene d'ici ; sinon rendu au jeu tel quel.)
            if (idx == -2 && LoinHote)
            {
                if (!c.Leading) { c.Leading = true; if (c.Muted) GiveBack(c, "eteint chez l'hote (loin)"); }
                return;
            }
            if (c.Leading) { c.Leading = false; Log.Info("radio : lecteur " + c.Name + " de nouveau suivi sur l'hote"); }
            // (prise en main par l'hote : la position est recalee meme sous le seuil -- le lecteur d'ici, lance avant, gardait ~1 s d'avance)
            bool taken = false;
            if (!c.Muted) { c.Muted = true; taken = true; if (c.Upd.enabled) c.Upd.enabled = false; Log.Info("radio : lecteur " + c.Name + " mene par l'hote (enchainement local coupe)"); }
            FsmString cv = c.Upd.FsmVariables.FindFsmString("CurrentCD"); if (cv != null && cd.Length > 0) cv.Value = cd;
            FsmInt iv = c.Upd.FsmVariables.FindFsmInt("Index"); if (iv != null && idx >= 0) iv.Value = idx;
            string key = cd + ":" + idx;
            if (!playing) { if (a.isPlaying) a.Stop(); c.Last = null; return; }
            AudioClip clip = CdClip(cd, idx);
            if (clip == null)
            {
                if (c.Last != key && Time.realtimeSinceStartup >= nextLog) { nextLog = Time.realtimeSinceStartup + 30f; Log.Warn("radio : lecteur " + c.Name + ", piste " + key + " de l'hote introuvable ici"); }
                c.Last = key;
                return;
            }
            bool swap = a.clip != clip;
            if (swap) { a.clip = clip; if (a.isPlaying) a.Stop(); }
            bool seekable = clip.loadType != AudioClipLoadType.Streaming;   // (un flux -- CD importe -- ne se positionne pas)
            // (0,75 s : avec 1,5 s, jusqu'a 0,9 s de retard restait apres chaque fin de piste, 27/10)
            if (seekable && (swap || taken || Mathf.Abs(a.time - t) > 0.75f)) a.time = Mathf.Clamp(t, 0f, Mathf.Max(0f, clip.length - 0.05f));
            if (!a.isPlaying && a.gameObject.activeInHierarchy) a.Play();
            if (c.Last != key) { c.Last = key; Log.Info("radio : lecteur " + c.Name + " -> " + key + " a " + t.ToString("F0") + " s (hote)"); }
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
        class Ghost
        {
            public Transform K; public GameObject Go; public AudioSource[] Ch; public PlayMakerFSM Fetch; public int Who; public float Born;
            // Chanson importee (flux WWW) : un seul lecteur a la fois -- la copie charge son propre exemplaire.
            public string[] SongKey = new string[2]; public WWW[] SongWww = new WWW[2]; public AudioClip[] SongClip = new AudioClip[2];
        }
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

        // Poste d'un automate : la radio portable qui le porte, sinon sa voiture, sinon sa racine (chaine de la maison).
        static Transform KeyOf(Transform t) { return BoomboxOf(t) ?? VehicleSync.CarRoot(t) ?? t.root; }

        const string Boombox = "radio(itemx)";
        static Transform BoomboxOf(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent) if (p.name == Boombox) return p;
            return null;
        }

        // Cle d'un poste sur le fil (@poste) : son chemin ; la radio portable par son nom (portee, elle est sous PLAYER/... chez
        // son porteur, et son chemin changeait a chaque prise en main : copie coupee chez les autres).
        static string SetKey(Transform k) { return k.name == Boombox ? Boombox : Recon.Path(k); }

        // Radio portable allumee et alimentee : son objet Play actif (Power 'Check channel'), et son automate Play :: Charge pas
        // a plat (Charge > 0, et pas 'State 3' : moins de 10 piles, volume force a 0).
        static bool BoomboxAlive(Transform parent)
        {
            Transform b = BoomboxOf(parent);
            if (b == null) return false;
            Transform play = b.Find("Play");
            if (play == null || !play.gameObject.activeSelf) return false;
            PlayMakerFSM ch = Game.FsmOn(play.gameObject, "Charge");
            if (ch == null) return true;
            FsmFloat c = ch.FsmVariables.FindFsmFloat("Charge");
            return (c == null || c.Value > 0f) && ch.ActiveStateName != "State 3";
        }

        static Ghost GhostOf(Transform k) { foreach (Ghost g in ghosts) if (g.K == k) return g; return null; }

        static Ghost MakeGhost(Transform k, Transform speaker, int who)
        {
            var go = (GameObject)Object.Instantiate(realT.gameObject);
            go.name = "MWCoop-RadioAutre";
            foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName != "Fetch") f.enabled = false;   // (tirage des morceaux : celui du vrai)
            go.transform.parent = speaker; go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
            var g = new Ghost { K = k, Go = go, Who = who, Fetch = Game.FsmOn(go, "Fetch"), Born = Time.realtimeSinceStartup };
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

        // Le vrai jeu de canaux lit volume et basses a chaque image sur la radio notee dans sa variable Parent (GetParent de
        // son etat Fetch). Le geste d'un autre rejoue ici (allumer sa radio) le deplacait dans son poste et relancait Fetch :
        // remis a sa place par RemoteChange, il gardait les reglages de l'autre poste -- les basses ou le volume d'un joueur
        // passaient aux radios des autres (retour de witefold, 0.75.37 ; essai radioautre RadioLocale=18 : radio du KEKMET
        // d'ici reglee sur la BACHGLOTZ de l'hote). Recale sur le poste ou il joue, et ses filtres relus (FETCH).
        static float parentLog;
        static void KeepParent(float now)
        {
            if (!fetch.enabled || realT.parent == null || Off(Take())) return;
            FsmGameObject pv = fetch.FsmVariables.FindFsmGameObject("Parent");
            if (pv == null || pv.Value == realT.parent.gameObject) return;
            string was = pv.Value != null ? pv.Value.transform.root.name : "-";
            pv.Value = realT.parent.gameObject;
            fetch.SendEvent("FETCH");
            if (now >= parentLog) { parentLog = now + 10f; Log.Info("radio : reglages relus sur " + Recon.Path(realT.parent) + " (ils venaient de " + was + ")"); }
        }

        static void Watch(float now)
        {
            if (realT == null || fetch == null) return;
            if (!lastSet) { last = Take(); lastSet = true; }
            KeepParent(now);
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
                    // Chanson importee (Radio/trackN.ogg, lue en flux) : un seul lecteur a la fois -- la jouer aussi ici arretait
                    // le vrai canal, que le jeu croyait fini : morceau suivant, et ainsi de suite jusqu'a une pub (retour d'un
                    // joueur, 0.75.35 : « plusieurs radios allumees : que des pubs »). La copie joue son propre exemplaire,
                    // charge depuis le fichier, depuis le debut (un flux ne se positionne pas).
                    string key = src.clip != null && src.clip.name.Length == 0 ? ClipKey(src.clip) : null;
                    if (key != null && key.StartsWith("#chanson:"))
                    {
                        OwnSong(g, c, key, src, dst);
                        continue;
                    }
                    if (g.SongKey[c] != null) { g.SongKey[c] = null; g.SongWww[c] = null; g.SongClip[c] = null; }
                    if (dst.clip != src.clip) { dst.clip = src.clip; if (src.clip != null) dst.time = Mathf.Min(src.time, src.clip.length - 0.05f); }
                    if (src.isPlaying && !dst.isPlaying && dst.clip != null && dst.gameObject.activeInHierarchy) { dst.Play(); dst.time = src.time; }
                    else if (!src.isPlaying && dst.isPlaying) dst.Stop();
                    else if (dst.isPlaying && dst.clip == src.clip && Mathf.Abs(dst.time - src.time) > 0.5f) dst.time = src.time;
                }
            }
        }

        // Ou joue mon vrai poste (retour de RullER, 14/10 : « contact coupe, la radio continue chez mon ami ») : la copie chez
        // les autres ne nait et ne meurt que des gestes rejoues (bouton, molette) ; un poste coupe autrement (contact, batterie,
        // pile) restait allume chez eux. Chacun dit toutes les 2 s ou joue son vrai jeu de canaux (« » : eteint) ; chez les
        // autres, sa copie d'un autre poste s'arrete (pas dans ses 3 premieres secondes : le geste peut devancer ce message).
        static float nextBeat; static string beatSent;
        static void Beat(float now)
        {
            if (Session.RemoteCount == 0 || realT == null || fetch == null || now < nextBeat) return;
            Snap s = Take();
            // (radio portable : Fetch n'y fait pas foi -- allumee si son Play est actif et ses piles pas a plat)
            bool boombox = !Off(s) && BoomboxOf(s.Parent) != null;
            bool on = boombox ? BoomboxAlive(s.Parent) : s.On;
            string k = Off(s) || !on ? "" : SetKey(KeyOf(s.Parent));
            if (k == beatSent && now < nextBeat + 8f) { nextBeat = now + 2f; return; }
            nextBeat = now + 2f; beatSent = k;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@poste").Str(k), false);
        }

        // Essais (radioautre, RadioCoupe=1) : le vrai jeu de canaux rendu a RADIO sans geste, comme un poste qui perd le courant.
        public static string TestCut()
        {
            GameObject r = GameObject.Find("RADIO");
            if (realT == null || fetch == null || r == null) return "radio : pas de canaux ou pas de RADIO";
            realT.parent = r.transform; fetch.enabled = false;
            last = Take(); lastSet = true;
            return "radio : vrai jeu de canaux rendu a RADIO (coupe sans geste)";
        }

        static readonly Dictionary<int, string> beatRecv = new Dictionary<int, string>();   // dernier @poste de chacun (essais)
        public static void OnBeat(int who, string key)
        {
            float now = Time.realtimeSinceStartup;
            beatRecv[who] = key;
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                Ghost g = ghosts[i];
                if (g.Who != who || now - g.Born < 3f || g.K == null) continue;
                if (key.Length == 0 || SetKey(g.K) != key) Kill(g, "coupe : #" + who + " ne l'ecoute plus" + (key.Length == 0 ? " (eteint chez lui)" : " (" + key + ")"));
            }
        }

        static void OwnSong(Ghost g, int c, string key, AudioSource src, AudioSource dst)
        {
            if (g.SongKey[c] != key)
            {
                g.SongKey[c] = key; g.SongClip[c] = null; g.SongWww[c] = null;
                int i;
                string file = int.TryParse(key.Substring(9), out i) ? Contenu.SongFile(i) : null;
                if (file != null) g.SongWww[c] = new WWW("file:///" + file);
                else Log.Info("radio : copie de " + (g.K != null ? g.K.name : "?") + ", fichier de " + key + " introuvable : muette pendant ce morceau");
                if (dst.isPlaying) dst.Stop();
            }
            if (g.SongWww[c] != null && g.SongWww[c].isDone)
            {
                try { g.SongClip[c] = g.SongWww[c].GetAudioClip(false, true, AudioType.OGGVORBIS); } catch (System.Exception e) { Log.Warn("radio : " + e.Message); }
                g.SongWww[c] = null;
            }
            AudioClip own = g.SongClip[c];
            if (own == null) return;
            if (dst.clip != own) dst.clip = own;
            if (src.isPlaying && !dst.isPlaying && dst.gameObject.activeInHierarchy) dst.Play();
            else if (!src.isPlaying && dst.isPlaying) dst.Stop();
        }

        // Essai [Test] Autotest=radioflux : une chanson importee (Radio/trackN.ogg, flux WWW) jouee par une source A, puis a
        // 45 s par une source B en meme temps (comme le vrai canal et la copie d'un poste) : A continue-t-elle ?
        static AudioSource testA, testB; static float testLog;
        static bool testFolk;
        // Essai (radioautre, RadioFlux=1) : le vrai canal folk d'ici joue la chanson importee 0 (plus suivi par l'hote).
        public static string TestFolkSong()
        {
            System.Collections.ArrayList songs = Songs();
            AudioClip c = songs != null && songs.Count > 0 ? songs[0] as AudioClip : null;
            if (c == null || ch == null || ch[1] == null) return "pas de chanson importee";
            testFolk = true;
            ch[1].clip = c; ch[1].Play();
            return "canal folk d'ici : chanson importee 0";
        }
        public static void TestStream(float t)
        {
            if (t < 40f) return;
            if (testA == null)
            {
                System.Collections.ArrayList songs = Songs();
                AudioClip c = songs != null && songs.Count > 0 ? songs[0] as AudioClip : null;
                if (c == null) { if (t >= testLog) { testLog = t + 5f; Log.Info("autotest : radioflux, pas de chanson importee (" + (songs != null ? songs.Count : -1) + ")"); } return; }
                testA = new GameObject("MWCoop-TestFluxA").AddComponent<AudioSource>(); testA.clip = c; testA.Play();
                Log.Info("autotest : radioflux, A joue la chanson 0 (" + c.loadType + ", " + c.length.ToString("F0") + " s)");
            }
            if (t >= 45f && testB == null) { testB = new GameObject("MWCoop-TestFluxB").AddComponent<AudioSource>(); testB.clip = testA.clip; testB.Play(); testB.time = testA.time; Log.Info("autotest : radioflux, B joue la meme chanson"); }
            if (t < 58f && t >= testLog)
            {
                testLog = t + 1f;
                Log.Info("autotest : radioflux, A " + (testA.isPlaying ? "joue" : "arrete") + " a " + testA.time.ToString("F1") + " s" + (testB != null ? ", B " + (testB.isPlaying ? "joue" : "arrete") + " a " + testB.time.ToString("F1") + " s" : ""));
            }
        }

        // Essais : postes joues ici par copie.
        public static string GhostState()
        {
            var sb = new System.Text.StringBuilder("vrai jeu de canaux sous " + (realT != null && realT.parent != null ? Recon.Path(realT.parent) : "-") + (fetch != null ? (fetch.enabled ? " (allume" : " (eteint") + ", locale " + (!FB("OnMuteChannel1")) + ", folk " + (!FB("OnMuteFolk")) + ")" : ""));
            if (fetch != null)
            {
                FsmGameObject par = fetch.FsmVariables.FindFsmGameObject("Parent");
                sb.Append(" ; reglages lus sur ").Append(par != null && par.Value != null ? Recon.Path(par.Value.transform) : "-").Append(" [").Append(fetch.ActiveStateName).Append("]");
            }
            if (ch != null)
                for (int c = 0; c < ch.Length; c++)
                    if (ch[c] != null)
                        sb.Append(c == 0 ? " ; local " : " ; folk ").Append(ch[c].clip != null ? "'" + ClipKey(ch[c].clip) + "' " + ch[c].clip.loadType + " " + ch[c].time.ToString("F1") + "/" + ch[c].clip.length.ToString("F0") + " s" : "-")
                          .Append(ch[c].isPlaying ? " joue" : " arrete");
            foreach (Ghost g in ghosts)
                sb.Append(" ; copie ").Append(g.K != null ? g.K.name : "?").Append(" de #").Append(g.Who).Append(" : ")
                  .Append(g.Ch[0] != null && g.Ch[0].clip != null ? g.Ch[0].clip.name + " a " + g.Ch[0].time.ToString("F0") + " s" : "-")
                  .Append(g.Ch.Length > 1 && g.Ch[1] != null && g.Ch[1].clip != null ? " / folk '" + ClipKey(g.Ch[1].clip) + "' a " + g.Ch[1].time.ToString("F0") + " s" + (g.Ch[1].isPlaying ? " joue" : " arrete") : "")
                  .Append(g.Ch[0] != null ? (g.Ch[0].isPlaying ? " joue" : " arrete") + (g.Ch[0].mute ? " muet" : "") + " vol " + g.Ch[0].volume.ToString("F2") : "");
            foreach (Cd c in cds)
            {
                if (c.A == null || c.Upd == null || (c.A.clip == null && !c.A.isPlaying)) continue;
                FsmString cd = c.Upd.FsmVariables.FindFsmString("CurrentCD"); FsmInt idx = c.Upd.FsmVariables.FindFsmInt("Index");
                sb.Append(" ; cd ").Append(c.Name).Append(" '").Append(cd != null ? cd.Value : "?").Append(":").Append(idx != null ? idx.Value.ToString() : "?").Append("' ")
                  .Append(c.A.clip != null ? c.A.time.ToString("F0") + "/" + c.A.clip.length.ToString("F0") + " s" : "-").Append(c.A.isPlaying ? " joue" : " arrete").Append(c.Muted ? " (hote)" : " [" + c.Upd.ActiveStateName + "]");
            }
            return sb.ToString();
        }

        // Essais (mscradio / mscradiodump) : pistes de chaque disque (nom, chargement, duree) et lecteurs CD (clip joue, position).
        public static string CdTestState()
        {
            var sb = new System.Text.StringBuilder();
            if (cdRoot == null) cdRoot = GameObject.Find("Radio/CD");
            foreach (string cd in new[] { "CD1", "CD2", "CD3" })
            {
                System.Collections.ArrayList l = ArrayListOf(cdRoot, cd);
                if (l == null || l.Count == 0) continue;
                sb.Append(cd).Append(" [");
                for (int i = 0; i < l.Count && i < 12; i++) { AudioClip c = l[i] as AudioClip; sb.Append(i > 0 ? " " : "").Append(c != null ? (c.name.Length > 0 ? c.name : "?") + "/" + c.length.ToString("F0") + "s" + (c.loadType == AudioClipLoadType.Streaming ? "~" : "") : "null"); }
                sb.Append("] ");
            }
            FindCds(Time.realtimeSinceStartup);
            foreach (Cd c in cds)
            {
                if (c.A == null || c.Upd == null) continue;
                FsmString cd = c.Upd.FsmVariables.FindFsmString("CurrentCD"); FsmInt idx = c.Upd.FsmVariables.FindFsmInt("Index");
                sb.Append("lecteur ").Append(c.Name).Append(" ").Append(cd != null ? cd.Value : "?").Append(":").Append(idx != null ? idx.Value.ToString() : "?")
                  .Append(" clip ").Append(c.A.clip != null ? (c.A.clip.name.Length > 0 ? c.A.clip.name : "?") + " " + c.A.time.ToString("F1") + "/" + c.A.clip.length.ToString("F0") : "-")
                  .Append(c.A.isPlaying ? " joue" : " arrete").Append(c.Muted ? " (hote)" : " [" + (c.Upd.enabled ? c.Upd.ActiveStateName : "coupe") + "]").Append(" ; ");
            }
            return sb.ToString();
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            if (Session.IsHost) { if (PlayerSync.InGame && Find()) OnReport(from, r); return; }   // (lecteurs menes par un invite, dossier #26)
            if (!PlayerSync.InGame || !Find()) return;
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
                if (i >= ch.Length || ch[i] == null || (i == 1 && testFolk)) continue;
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
            // Lecteurs CD (suite du message, hote depuis le 16/10 ; rien de plus avec un hote plus ancien).
            if (!r.More) return;
            FindCds(Time.realtimeSinceStartup);
            int m = r.U8();
            for (int i = 0; i < m; i++)
            {
                string name = r.Str(), cd = r.Str();
                int idx = r.I32();
                float t = r.F32();
                bool playing = r.Bool();
                Cd c = CdNamed(name);
                if (c == null || c.A == null || c.Upd == null) continue;
                try { ApplyCd(c, cd, idx, t, playing); }
                catch (System.Exception e) { if (Time.realtimeSinceStartup >= nextLog) { nextLog = Time.realtimeSinceStartup + 30f; Log.Warn("radio : lecteur " + name + " : " + e.Message); } }
            }
        }

        // Essais (Autotest=mwccd, dossier #26) : 'n' pistes de 'len' s (son synthetique, memes noms chez tous) ajoutees au disque
        // 'cd' (les copies d'essai n'ont pas de musique importee) ; lecture du disque par le lecteur 'name' comme le bouton (PLAY CD) ;
        // etat d'un lecteur (piste, position, joue, allume ou non, mene par qui).
        public static string TestCdFill(string cd, int n, float len)
        {
            if (cdRoot == null) cdRoot = GameObject.Find("Radio/CD");
            System.Collections.ArrayList l = ArrayListOf(cdRoot, cd);
            if (l == null) return "disque " + cd + " introuvable";
            for (int k = 0; k < n; k++)
            {
                int f = 22050, s = (int)(len * f);
                AudioClip c = AudioClip.Create("essai-" + cd + "-" + k, s, 1, f, false);
                float[] d = new float[s]; float hz = 220f * (k + 2);
                for (int i = 0; i < s; i++) d[i] = 0.05f * Mathf.Sin(2f * Mathf.PI * hz * i / f);
                c.SetData(d, 0);
                l.Add(c);
            }
            return cd + " : " + l.Count + " pistes";
        }
        public static string TestCdPlay(string name, string cd)
        {
            FindCds(Time.realtimeSinceStartup);
            Cd c = CdNamed(name);
            if (c == null || c.Upd == null) return "lecteur " + name + " introuvable";
            FsmString cv = c.Upd.FsmVariables.FindFsmString("CurrentCD"); if (cv != null) cv.Value = cd;
            FsmInt iv = c.Upd.FsmVariables.FindFsmInt("Index"); if (iv != null) iv.Value = 0;
            if (!c.Upd.enabled) c.Upd.enabled = true;   // (coupe par l'autoradio eteint)
            // (PLAY CD laisse l'automate en 'State 1' en essai : piste 0 posee et 'Play track' directement, comme GiveBack)
            AudioClip clip = CdClip(cd, 0);
            FsmObject au = c.Upd.FsmVariables.FindFsmObject("Audio"); if (au != null) au.Value = clip;
            FsmInt ct = c.Upd.FsmVariables.FindFsmInt("CurrentTrack"); if (ct != null) ct.Value = 1;
            FsmInt mt = c.Upd.FsmVariables.FindFsmInt("MaxTracks"); System.Collections.ArrayList l = ArrayListOf(cdRoot, cd); if (mt != null && l != null) mt.Value = l.Count - 1;
            Game.SetState(c.Upd, "Play track");
            return "lecteur " + name + " : " + cd + " piste 1 [" + c.Upd.ActiveStateName + "] " + (c.A.isPlaying ? "joue" : "arrete");
        }
        public static string TestCdActive(string name, bool on)
        {
            Cd c = CdNamed(name);
            if (c == null || c.A == null) return "lecteur " + name + " introuvable";
            c.A.gameObject.SetActive(on);
            return "lecteur " + name + (on ? " rallume" : " eteint") + " (comme le LOD de sa voiture)";
        }
        public static string TestCd(string name)
        {
            Cd c = CdNamed(name);
            if (c == null || c.A == null || c.Upd == null) return name + " ?";
            FsmString cv = c.Upd.FsmVariables.FindFsmString("CurrentCD");
            return name + (c.A.gameObject.activeInHierarchy ? "" : " (ETEINT)") + " " + (c.A.clip != null ? c.A.clip.name + " a " + c.A.time.ToString("F1") + " s" : "-")
                   + (c.A.isPlaying ? " joue" : " arrete") + (c.Muted ? " (suit)" : " [" + (c.Upd.enabled ? c.Upd.ActiveStateName : "coupe") + "]") + (c.Leading ? " (mene d'ici)" : "")
                   + (cv != null ? " " + cv.Value : "") + " Index " + (c.Upd.FsmVariables.FindFsmInt("Index") != null ? c.Upd.FsmVariables.FindFsmInt("Index").Value.ToString() : "?") + "/" + (c.Upd.FsmVariables.FindFsmInt("MaxTracks") != null ? c.Upd.FsmVariables.FindFsmInt("MaxTracks").Value.ToString() : "?") + (Game.PlayerGo != null ? ", joueur a " + Vector3.Distance(Game.PlayerGo.transform.position, c.A.transform.position).ToString("F0") + " m" : "");
        }

        // Essai [Test] Autotest=boombox : l'hote allume la radio portable a 25 s par son bouton de marche comme un clic (Power :: Knob
        // 'Get scroll' -FINISHED-> 'Switch' -> 'Check channel' -> 'Channel 1', message ItemFsms), piles pleines pour l'essai (Charge 1,
        // 10 piles) ; l'invite note a 40 s si une copie joue dans radio(itemx)/SoundSpeaker et le dernier @poste recu de l'hote ;
        // chacun note l'etat des postes toutes les 3 s de 20 a 60 s. Attendu chez l'invite : copie presente, qui joue, @poste
        // « radio(itemx) » ; son vrai jeu de canaux pas deplace.
        static int tbStep; static float tbLog;
        public static void TestBoombox(float t)
        {
            if (Session.IsHost && t > 25f && tbStep == 0) { tbStep = 1; Log.Info("autotest boombox : " + TestBoomboxOn()); }
            if (!Session.IsHost && t > 40f && tbStep == 0) { tbStep = 1; Log.Info("autotest boombox : " + TestBoomboxState()); }
            if (t > 20f && t < 60f && t >= tbLog) { tbLog = t + 3f; Log.Info("autotest boombox : " + GhostState()); }
        }

        static string TestBoomboxOn()
        {
            GameObject b = Game.FindAny("EQUIPMENTS/" + Boombox);
            if (b == null) return "radio portable introuvable (EQUIPMENTS/" + Boombox + ")";
            try
            {
                // (piles : comme Data :: Idle au chargement -- variables de Play :: Charge et de Insert :: Assembly, objets eteints ou non)
                Transform play = b.transform.Find("Play"), ins = b.transform.Find("ChangeBatteries/Insert");
                PlayMakerFSM ch = play != null ? Game.FsmOn(play.gameObject, "Charge") : null;
                PlayMakerFSM asm = ins != null ? Game.FsmOn(ins.gameObject, "Assembly") : null;
                FsmFloat c = ch != null ? ch.FsmVariables.FindFsmFloat("Charge") : null; if (c != null) c.Value = 1f;
                FsmInt bc = ch != null ? ch.FsmVariables.FindFsmInt("Batteries") : null; if (bc != null) bc.Value = 10;
                FsmInt ba = asm != null ? asm.FsmVariables.FindFsmInt("Batteries") : null; if (ba != null) ba.Value = 10;
                PlayMakerFSM data = Game.FsmOn(b, "Data");
                if (data != null) { FsmFloat dc = data.FsmVariables.FindFsmFloat("Charge"); if (dc != null) dc.Value = 1f; FsmInt db = data.FsmVariables.FindFsmInt("Batteries"); if (db != null) db.Value = 10; }
            }
            catch (System.Exception e) { Log.Warn("autotest boombox : piles : " + e.Message); }
            return ItemFsms.TestSend(Boombox, "Power/Knob", "Get scroll", "FINISHED", "Switch");
        }

        static string TestBoomboxState()
        {
            GameObject b = Game.FindAny("EQUIPMENTS/" + Boombox);
            Transform sp = b != null ? b.transform.Find("SoundSpeaker") : null;
            Transform g = sp != null ? sp.Find("MWCoop-RadioAutre") : null;
            var sb = new System.Text.StringBuilder();
            sb.Append("copie dans ").Append(Boombox).Append("/SoundSpeaker : ");
            if (b == null) sb.Append("radio portable introuvable");
            else if (g == null) sb.Append("aucune");
            else
            {
                Transform c1 = g.Find("Channel1"), fk = g.Find("Folk");
                AudioSource a1 = c1 != null ? c1.GetComponent<AudioSource>() : null, af = fk != null ? fk.GetComponent<AudioSource>() : null;
                sb.Append("oui, locale ").Append(a1 != null ? (a1.isPlaying ? "joue" : "arretee") + (a1.mute ? " muette" : "") + " vol " + a1.volume.ToString("F2") : "-")
                  .Append(", folk ").Append(af != null ? (af.isPlaying ? "joue" : "arretee") + (af.mute ? " muette" : "") : "-");
            }
            string k; sb.Append(" ; @poste de l'hote : ").Append(beatRecv.TryGetValue(0, out k) ? "'" + k + "'" : "aucun recu");
            Transform play = b != null ? b.transform.Find("Play") : null;
            sb.Append(" ; Play ici ").Append(play != null ? (play.gameObject.activeSelf ? "actif" : "eteint") : "?");
            return sb.ToString();
        }
    }
}
