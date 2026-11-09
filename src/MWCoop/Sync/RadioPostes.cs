using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Plusieurs radios en meme temps (JD, 08/11 : « on peut faire que ca marche ainsi par defaut » -- dossier #53, radio de la SATSUMA
    // coupee des qu'on allume celle de la HAYOSIKO). Le jeu n'a qu'un jeu de canaux (RADIO/RadioChannels) : le poste qu'on allume le
    // rattache a son haut-parleur, et celui qui l'avait se tait. Ici, TOUT poste allume a soit le vrai jeu de canaux, soit une copie
    // (Ghost, comme pour les postes des autres depuis le 11/10) qui joue le meme morceau au meme moment, avec son volume, ses basses,
    // ses parasites et sa station -- le geste vienne d'ici, d'un autre joueur (rejoue), de la cle ou du courant revenu.
    //  - l'etat de chaque poste vient de ses propres automates : au demarrage (et toutes les 10 s), les automates qui pointent vers le
    //    jeu de canaux sont releves ; les etats ou ils le rattachent a leur haut-parleur (SetParent) = « allume », ceux ou ils lui
    //    envoient OFF = « eteint ». Une action ajoutee a ces etats (FsmHooks) previent ici a chaque entree. Le poste ou joue le vrai
    //    jeu de canaux est aussi tenu pour allume (etat d'avant le releve : partie chargee radio allumee).
    //  - le vrai jeu de canaux va ou le jeu le met (dernier poste allume) ; le poste qui le perd, encore allume, recoit une copie.
    //  - le OFF d'un poste qui n'a PAS le vrai jeu de canaux (sa copie joue, ou il etait deja eteint : couper le contact de la SATSUMA,
    //    « Not work ») eteignait chez le jeu le poste qui l'avait : remis aussitot.
    //  - copies econes : une seule voix (le canal de sa station), aucune a volume nul, en pause au-dela de RadioCopieLoin (80 m) de
    //    l'auditeur ; reprise au morceau et a la position du moment.
    //  - arrivant en cours de partie : l'hote dit toutes les 2 s ses postes allumes (@postes) ; l'invite joue en copie ceux qui sont
    //    eteints chez lui (et arrete la copie d'un poste que l'hote ne dit plus allume depuis 6 s).
    // [Test] RadioUnSeulPoste=1 : comme avant (un seul poste a la fois, copies pour les gestes des autres seulement).
    public static partial class Radio
    {
        static bool Multi { get { return Config.GetInt("Test", "RadioUnSeulPoste", 0) == 0; } }

        class Poste
        {
            public Transform K; public string Key;
            public Transform Speaker;                 // haut-parleur ou le jeu met les canaux de ce poste
            public bool SpeakerGuess;                 // (trouve par son nom sous le poste, pas par un automate : revu au releve suivant)
            public bool On; public float OnAt = -10f, OffAt = -10f;
            public bool ByHost; public float HostSeen = -10f;   // allume chez l'hote (instantane @postes), pas ici
            public Snap Flags; public bool HasFlags;
            public Ghost G; public bool Far;
        }
        static readonly Dictionary<Transform, Poste> postes = new Dictionary<Transform, Poste>();

        class Ctrl
        {
            public PlayMakerFSM F; public Transform K; public bool Bad;
            public readonly List<string> OnStates = new List<string>(), OffStates = new List<string>();
            public readonly List<FsmGameObject> Parents = new List<FsmGameObject>();
            public readonly Dictionary<string, List<FsmStateAction>> OffActs = new Dictionary<string, List<FsmStateAction>>();   // (actions OFF de chaque etat « eteint »)
        }
        static readonly Dictionary<PlayMakerFSM, Ctrl> ctrls = new Dictionary<PlayMakerFSM, Ctrl>();
        static readonly HashSet<string> hooked = new HashSet<string>();
        static readonly HashSet<PlayMakerFSM> notCtrl = new HashSet<PlayMakerFSM>();   // (deja vus, ne pointent pas vers le jeu de canaux)
        static float nextCtrlScan;
        static Snap offBase; static Poste offPost; static float offAt = -10f;
        static float hostListAt = -10f, nextPostes;
        static int postLogs;

        class PostHook : ModHook
        {
            public override string Module { get { return "radio"; } }
            public Ctrl C; public int Kind;   // 0 : allume (en tete, avant le SetParent du jeu) ; 1 : eteint (avant son OFF) ; 2 : eteint (apres)
            public override void OnEnter()
            {
                try { PostEvent(C, Kind, State != null ? State.Name : "?"); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static void PostesLevelLoaded()
        {
            postes.Clear(); ctrls.Clear(); hooked.Clear(); notCtrl.Clear(); nextCtrlScan = 0f; offPost = null; offAt = -10f; hostListAt = -10f; nextPostes = 0f; postLogs = 0;
            offMuted.Clear(); livePost = null; liveTo = null;
            measAt = 0f; measMs = 0.0; measMax = 0.0; measN = 0;
        }

        static object Field(object a, string name)
        {
            var f = a.GetType().GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return f != null ? f.GetValue(a) : null;
        }
        static GameObject OwnerGo(FsmOwnerDefault od, PlayMakerFSM f)
        {
            if (od == null) return null;
            return od.OwnerOption == OwnerDefaultOption.UseOwner ? f.gameObject : od.GameObject != null ? od.GameObject.Value : null;
        }

        static Poste PosteOf(Transform k, bool create)
        {
            if (k == null) return null;
            Poste p;
            if (postes.TryGetValue(k, out p)) return p;
            if (!create) return null;
            p = new Poste { K = k, Key = SetKey(k) };
            postes[k] = p;
            return p;
        }

        // Automates qui pointent vers le jeu de canaux : etats « allume » (SetParent vers un haut-parleur de leur poste) et « eteint »
        // (OFF a son Fetch). Toutes les 10 s : voitures et pieces apparues, automates demarres depuis (etats pas encore charges).
        static bool ScanCtrls(float now)
        {
            if (now < nextCtrlScan || realT == null) return false;
            nextCtrlScan = now + 10f;
            GameObject rc = realT.gameObject;
            int added = 0;
            foreach (Object o in Game.AllFsms(10f))
            {
                var f = o as PlayMakerFSM;
                if (f == null || f.hideFlags != HideFlags.None || f.Fsm == null || f.transform.IsChildOf(realT)) continue;
                Ctrl c;
                if (!ctrls.TryGetValue(f, out c))
                {
                    if (notCtrl.Contains(f)) continue;
                    bool refs = false;
                    FsmGameObject[] gv = f.FsmVariables != null ? f.FsmVariables.GameObjectVariables : null;
                    if (gv != null) foreach (FsmGameObject v in gv) if (v != null && v.Value == rc) { refs = true; break; }
                    if (!refs) { notCtrl.Add(f); continue; }
                    c = new Ctrl { F = f, K = KeyOf(f.transform) };
                    ctrls[f] = c;
                }
                if (c.Bad) continue;
                foreach (FsmState s in f.FsmStates)
                {
                    if (s == null || !s.IsInitialized || s.Actions == null) continue;
                    foreach (FsmStateAction a in s.Actions)
                    {
                        if (a == null || a is ModHook) continue;
                        string tn = a.GetType().Name;
                        if (tn == "SetParent")
                        {
                            if (OwnerGo(Field(a, "gameObject") as FsmOwnerDefault, f) != rc) continue;
                            var par = Field(a, "parent") as FsmGameObject;
                            // (un poste ne range les canaux que chez lui : les basses de la SATSUMA -- Database/.../Subwoofers -- les deplacent
                            // d'un haut-parleur a l'autre de la voiture, ce n'est pas un poste)
                            if (par != null && par.Value != null && KeyOf(par.Value.transform) != c.K) { c.Bad = true; break; }
                            if (!c.OnStates.Contains(s.Name)) { c.OnStates.Add(s.Name); c.Parents.Add(par); }
                        }
                        else if (tn == "SendEventByName" || tn == "SendEvent")
                        {
                            var et = Field(a, "eventTarget") as FsmEventTarget;
                            if (et == null || et.target != FsmEventTarget.EventTarget.GameObjectFSM || OwnerGo(et.gameObject, f) != rc) continue;
                            if (et.fsmName == null || et.fsmName.Value != "Fetch") continue;
                            object ev = Field(a, "sendEvent");
                            string en = ev is FsmString ? ((FsmString)ev).Value : ev is FsmEvent ? ((FsmEvent)ev).Name : null;
                            if (en != "OFF") continue;
                            if (!c.OffStates.Contains(s.Name)) c.OffStates.Add(s.Name);
                            List<FsmStateAction> offs;
                            if (!c.OffActs.TryGetValue(s.Name, out offs)) c.OffActs[s.Name] = offs = new List<FsmStateAction>();
                            if (!offs.Contains(a)) offs.Add(a);
                        }
                    }
                    if (c.Bad) break;
                }
                if (c.Bad) continue;
                foreach (string sn in c.OnStates) if (Hook(c, sn, 0)) added++;
                foreach (string sn in c.OffStates) { if (Hook(c, sn, 1)) added++; Hook(c, sn, 2); }
            }
            if (added > 0 && postLogs++ < 20)
            {
                var sb = new System.Text.StringBuilder("radio : postes suivis");
                foreach (Ctrl c in ctrls.Values)
                    if (!c.Bad && c.F != null && (c.OnStates.Count > 0 || c.OffStates.Count > 0))
                        sb.Append(" ; ").Append(c.K != null ? c.K.name : "?").Append(" (").Append(c.F.FsmName).Append(" : allume ").Append(string.Join("/", c.OnStates.ToArray()))
                          .Append(", eteint ").Append(string.Join("/", c.OffStates.ToArray())).Append(')');
                Log.Info(sb.ToString());
            }
            return true;
        }

        static bool Hook(Ctrl c, string state, int kind)
        {
            string key = c.F.GetInstanceID() + ":" + state + ":" + kind;
            if (hooked.Contains(key)) return false;
            FsmState s = c.F.Fsm.GetState(state);
            if (s == null || !s.IsInitialized) return false;
            try
            {
                var h = new PostHook { C = c, Kind = kind };
                // (« allume » en tete aussi : « Check tune » repart vers sa station pendant ses propres actions -- une accroche en fin
                // n'etait jamais appelee, essais du 08/11 ; seul « eteint, apres » reste en fin)
                if (kind == 2) FsmHooks.Append(s, h); else FsmHooks.Prepend(s, h);
                hooked.Add(key);
                return true;
            }
            catch (System.Exception e) { Log.Warn("radio : accroche " + c.F.FsmName + "/" + state + " : " + e.Message); return false; }
        }

        static void PostEvent(Ctrl c, int kind, string state)
        {
            if (!Multi || realT == null || c == null || c.F == null) return;
            float now = Time.realtimeSinceStartup;
            // (poste recalcule ici : l'autoradio de la SATSUMA est une piece, sous CARPARTS au releve puis dans la voiture une fois montee)
            Transform k = KeyOf(c.F.transform);
            Poste p = PosteOf(k, true);
            if (kind == 0)
            {
                // (dossier #56, Say my name 08/11 : « quand je change le volume, la radio repart sur un autre morceau ». Chaque cran de la molette de
                // volume -- et de la station -- repasse par « Check tune » : SetParent du vrai jeu de canaux vers CE poste, Fetch relance. Poste
                // deja allume qui joue en copie : il prenait le vrai jeu a celui qui l'avait, qui repartait sur une copie neuve. Le vrai est remis
                // a son poste juste apres l'etat (KeepHolder) ; la copie de ce poste prend sa station, et son Fetch suit deja le volume de son
                // haut-parleur. [Test] RadioMolettePrend=1 : comme avant.)
                // (dossier #57, Say my name 08/11 : le meme « Check tune » sert a ALLUMER un poste. Allume pendant qu'un autre poste allume a le vrai
                // jeu de canaux, il le lui prenait et Fetch repartait ; ce poste-ci garde maintenant une copie qui rejoint le morceau du moment.
                // [Test] RadioRallumagePrend=1 : comme avant.)
                bool keepRule = p.On ? Config.GetInt("Test", "RadioMolettePrend", 0) == 0 : !RawOnOff;
                if (keepRule && Off(Take()) == false && realT.parent != null && KeyOf(realT.parent) != k)
                {
                    keepBase = Take(); keepPost = p; keepAt = now; keepTurnOn = !p.On;
                    if (RallumageRepris) SnapChannels(now);   // (dossier #59 : morceau et position du vrai jeu juste avant l'allumage, rendus par KeepHolder)
                }
                // (en tete de l'etat : le SetParent du jeu suit ; deux postes allumes dans la meme image ont chacun leur « allume »)
                if (!p.On) { p.OnAt = now; if (postLogs++ < 200) Log.Info("radio : " + p.K.name + " allume (" + c.F.FsmName + " « " + state + " »)"); }
                p.On = true; p.ByHost = false;
                if (p.Speaker == null || p.SpeakerGuess) p.Speaker = SpeakerOf(k, out p.SpeakerGuess);
            }
            else if (kind == 1)
            {
                offBase = Take(); offPost = p; offAt = now;
                SnapChannels(now);
                bool held = !Off(offBase) && offBase.Parent != null && KeyOf(offBase.Parent) == k;
                string kept = !RawOnOff && OffRetenu && !Off(offBase) && offBase.Parent != null ? HoldOff(c, state, p, held, now) : null;
                // (poste qui a le vrai jeu de canaux : apres son « eteint », le vrai jeu passe a un autre poste allume -- HandOver)
                if (kept == null && !RawOnOff && held) { handPost = p; handBase = offBase; handAt = now; }
                if (p.On && postLogs++ < 200) Log.Info("radio : " + p.K.name + " eteint (" + c.F.FsmName + " « " + state + " »)" + (kept ?? ""));
                p.On = false; p.ByHost = false; p.OffAt = now;
            }
            else { UnmuteOff(); StrayOff(now); }
        }

        // (dossier #59, Say my name 09/11 : « la radio perso -- chansons importees -- eteinte puis rallumee dans la HAYOSIKO : toutes les radios
        // passent au morceau suivant ». L'« eteint » d'un poste envoyait OFF aux canaux du jeu meme quand ce poste n'avait pas le vrai jeu de
        // canaux (sa copie jouait) : le jeu les arretait, puis StrayOff / HandOver les remettaient au meme morceau et a la meme position
        // (dossier #57) -- sauf une chanson importee, lue en flux, qui ne se positionne pas : le jeu tirait le morceau suivant, et toutes les
        // copies suivaient. Ce OFF peut etre retenu (ses actions eteintes pour ce passage, remises juste apres) : poste sans le vrai jeu
        // -> rien ne s'arrete ; poste qui l'a, un autre poste allume -> le vrai jeu y passe sans s'arreter (HandOverLive) ; aucun autre poste
        // allume -> OFF laisse au jeu, la radio se tait. Essais sa59/sb59 : l'« eteint » n'arretait pas la chanson -- le vrai coupable est
        // l'allumage (KeepHolder, ResumeChannels) ; retenue gardee pour essai seulement : [Test] RadioOffRetenu=1.)
        static int offRetenu = -1;
        static bool OffRetenu { get { if (offRetenu < 0) offRetenu = Config.GetInt("Test", "RadioOffRetenu", 0) != 0 ? 1 : 0; return offRetenu == 1; } }
        static readonly List<FsmStateAction> offMuted = new List<FsmStateAction>();
        static Poste livePost, liveTo; static float liveAt = -10f;

        static string HoldOff(Ctrl c, string state, Poste p, bool held, float now)
        {
            List<FsmStateAction> acts;
            // (hors partie a plusieurs, MultiWatch ne tourne pas : rien ne remettrait le vrai jeu de canaux -- OFF laisse au jeu)
            if (!Session.Active || !PlayerSync.InGame || !c.OffActs.TryGetValue(state, out acts) || acts.Count == 0) return null;
            Poste to = null;
            if (held)
            {
                foreach (Poste q in postes.Values) if (q != p && q.K != null && q.On && q.Speaker != null && (to == null || q.OnAt > to.OnAt)) to = q;
                if (to == null) return null;   // (plus aucun autre poste allume : la radio se tait, comme dans le jeu)
            }
            else
            {
                to = PosteOf(KeyOf(offBase.Parent), false);
                if (to == null || to == p || !(to.On || to.ByHost)) return null;
            }
            foreach (FsmStateAction a in acts) if (a != null && a.Enabled) { a.Enabled = false; offMuted.Add(a); }
            if (held) { livePost = p; liveTo = to; liveAt = now; return ", OFF retenu"; }   // (HandOverLive, a l'image suivante, dit ou il passe)
            offPost = null;   // (rien a remettre : StrayOff n'a plus a jouer)
            string res = ", OFF retenu : " + to.K.name + " continue";
            if (p.On) return res + " (" + Playing(Take()) + ")";   // (premier « eteint » : journal de PostEvent)
            if (postLogs++ < 200) Log.Info("radio : « eteint » de " + p.K.name + " retenu : " + to.K.name + " continue (" + Playing(Take()) + ")");
            return res;
        }

        static void UnmuteOff()
        {
            if (offMuted.Count == 0) return;
            foreach (FsmStateAction a in offMuted) if (a != null) a.Enabled = true;
            offMuted.Clear();
        }

        // Poste qui avait le vrai jeu de canaux eteint, son OFF retenu : le vrai jeu passe tel quel (meme morceau, meme position, il joue) a
        // l'autre poste allume, avec la station de celui-ci.
        static void HandOverLive(float now)
        {
            if (livePost == null) return;
            Poste was = livePost, to = liveTo; livePost = null; liveTo = null;
            if (realT == null || fetch == null) return;
            Snap cur = Take();
            if (Off(cur) || cur.Parent == null || KeyOf(cur.Parent) != was.K) return;   // (deja parti ailleurs, ou coupe autrement)
            if (to == null || to.K == null || !to.On || to.Speaker == null || now - liveAt > 0.6f)
            {
                fetch.SendEvent("OFF");   // (l'autre poste s'est eteint entre-temps : le OFF retenu est rendu au jeu, la radio se tait)
                if (postLogs++ < 200) Log.Info("radio : " + was.K.name + " eteint : plus d'autre poste allume, OFF rendu au jeu");
                return;
            }
            Snap s = cur; s.Parent = to.Speaker; s.LP = Vector3.zero; s.LR = Quaternion.identity; s.On = true;
            if (to.HasFlags) { s.M1 = to.Flags.M1; s.MF = to.Flags.MF; s.St = to.Flags.St; }
            Restore(s);
            fetch.SendEvent("FETCH");
            last = Take(); lastSet = true;
            if (postLogs++ < 200) Log.Info("radio : " + was.K.name + " eteint : le vrai jeu de canaux passe a " + to.K.name + " sans s'arreter (" + Station(s, true) + ", " + Playing(s) + ")");
        }

        // Ce que fait entendre le vrai jeu de canaux sur sa station : morceau et position (journal).
        static string Playing(Snap s)
        {
            int c = !s.M1 ? 0 : !s.MF ? 1 : -1;
            AudioSource a = c >= 0 && ch != null && c < ch.Length ? ch[c] : null;
            return a == null || a.clip == null ? "rien" : "'" + ClipKey(a.clip) + "' a " + a.time.ToString("F0") + " s" + (a.isPlaying ? "" : ", arrete");
        }

        // OFF d'un poste qui n'avait pas le vrai jeu de canaux : chez le jeu, il eteignait celui qui l'avait -- remis.
        static void StrayOff(float now)
        {
            if (offPost == null || now - offAt > 0.6f || realT == null || fetch == null) return;
            if (!Off(Take()) || Off(offBase)) return;
            Poste h = offBase.Parent != null ? PosteOf(KeyOf(offBase.Parent), false) : null;
            Poste was = offPost; offPost = null;
            if (h == null || h == was || !(h.On || h.ByHost)) return;
            Restore(offBase);
            fetch.SendEvent("FETCH");
            string res = RawOnOff ? "" : ResumeChannels(now);
            last = Take(); lastSet = true;
            if (postLogs++ < 200) Log.Info("radio : « eteint » de " + was.K.name + " ne coupe plus " + h.K.name + " (vrai jeu de canaux remis" + res + ")");
        }

        // (dossier #57, Say my name 08/11 : « quand mon ami eteint puis rallume la radio de la HAYOSIKO, le morceau suivant demarre sur toutes les
        // radios ». Le poste qui avait le vrai jeu de canaux s'eteint : le jeu coupe les canaux, et les copies des autres postes allumes, qui les
        // suivent, se taisaient ; au rallumage, Fetch repartait sur le morceau suivant, partout. Le vrai jeu passe ici a un autre poste allume --
        // le dernier allume --, meme morceau a la meme position ; sa copie s'arrete. [Test] RadioRallumagePrend=1 : comme avant.)
        static int rawOnOff = -1;
        static bool RawOnOff { get { if (rawOnOff < 0) rawOnOff = Config.GetInt("Test", "RadioRallumagePrend", 0) != 0 ? 1 : 0; return rawOnOff == 1; } }
        static Snap handBase; static Poste handPost; static float handAt = -10f;
        static void HandOver(float now)
        {
            if (handPost == null) return;
            if (now - handAt > 0.6f || realT == null || fetch == null) { handPost = null; return; }
            if (!Off(Take())) return;   // (pas encore coupe dans cette image)
            Poste was = handPost; handPost = null;
            Poste to = null;
            foreach (Poste q in postes.Values) if (q != was && q.K != null && q.On && q.Speaker != null && (to == null || q.OnAt > to.OnAt)) to = q;
            if (to == null) return;   // (plus aucun poste allume : la radio se tait, comme dans le jeu)
            Snap s = handBase; s.Parent = to.Speaker; s.LP = Vector3.zero; s.LR = Quaternion.identity; s.On = true;
            if (to.HasFlags) { s.M1 = to.Flags.M1; s.MF = to.Flags.MF; s.St = to.Flags.St; }
            Restore(s);
            fetch.SendEvent("FETCH");
            string res = ResumeChannels(now);
            last = Take(); lastSet = true;
            if (postLogs++ < 200) Log.Info("radio : " + was.K.name + " eteint : le vrai jeu de canaux passe a " + to.K.name + " (" + Station(s, true) + res + ")");
        }

        // Morceau et position des canaux juste avant un « eteint » ou un allumage (SnapChannels), rendus apres (ResumeChannels) si le jeu les a
        // arretes ou changes -- au rallumage, le jeu passait au morceau suivant. (Chanson importee lue en flux : pas de position, laissee au jeu.
        // Dossier #59 : sur My Summer Car, les chansons importees sont chargees entieres -- DecompressOnLoad, essai sa59 -- elles se positionnent
        // et sont reprises aussi ; [Test] RadioRepriseImportee=0 : jamais, comme avant.)
        static readonly AudioClip[] offClip = new AudioClip[2]; static readonly float[] offTime = new float[2]; static readonly bool[] offPlay = new bool[2];
        static float snapAt = -10f;
        static void SnapChannels(float now)
        {
            snapAt = now;
            for (int c = 0; c < 2; c++)
            {
                AudioSource a = ch != null && c < ch.Length ? ch[c] : null;
                offClip[c] = a != null ? a.clip : null; offTime[c] = a != null ? a.time : 0f; offPlay[c] = a != null && a.isPlaying;
            }
        }
        static bool CantResume(AudioClip clip)
        {
            if (clip.name.Length > 0) return false;
            return Config.GetInt("Test", "RadioRepriseImportee", 1) == 0 || clip.loadType == AudioClipLoadType.Streaming;
        }
        // (dossier #59, essais sa59/sb59 : a l'allumage d'un poste pendant qu'un autre a le vrai jeu -- KeepHolder --, le canal local s'arretait
        // sur la chanson importee et ne repartait plus ; il est repris au meme morceau, a la meme position. [Test] RadioRallumageRepris=0 : comme avant.)
        static bool RallumageRepris { get { return Config.GetInt("Test", "RadioRallumageRepris", 1) != 0; } }
        static string ResumeChannels(float now)
        {
            if (ch == null) return "";
            int n = 0;
            for (int c = 0; c < 2 && c < ch.Length; c++) if (ResumeOne(c, now)) n++;
            resumeUntil = now + 1.2f;   // (le jeu coupe parfois les canaux une image apres son « eteint » -- essai rc57 : surveille encore 1,2 s)
            return n > 0 ? ", " + n + " canal(aux) repris au meme morceau" : "";
        }
        // Canal c rendu a son morceau et a sa position d'avant : arrete, change, ou relance du debut (essai sh59 : relance a 0 une fraction de
        // seconde apres la reprise, la meme chanson rejouait du debut pendant 0,5 s -- l'ecart de position compte aussi, au-dela de 1 s).
        static bool ResumeOne(int c, float now)
        {
            AudioSource a = ch[c]; AudioClip clip = offClip[c];
            if (a == null || clip == null || !offPlay[c] || CantResume(clip)) return false;
            float want = Mathf.Min(offTime[c] + Mathf.Max(0f, now - snapAt), clip.length - 0.05f);
            if (a.clip == clip && a.isPlaying && Mathf.Abs(a.time - want) < 1f) return false;
            if (a.clip != clip) a.clip = clip;
            if (!a.isPlaying) a.Play();
            a.time = want;
            return true;
        }
        static float resumeUntil = -10f; static int resumeLogs;
        static void ResumeWatch(float now)
        {
            if (now > resumeUntil || ch == null) return;
            int n = 0;
            for (int c = 0; c < 2 && c < ch.Length; c++) if (ResumeOne(c, now)) n++;
            if (n > 0 && resumeLogs++ < 20) Log.Info("radio : " + n + " canal(aux) coupe(s) apres un « eteint » ou un allumage, repris au meme morceau (" + (now - snapAt).ToString("F2") + " s apres)");
        }

        // Molette (volume, station) d'un poste allume qui jouait en copie : le vrai jeu de canaux, parti dans ce poste, est remis a celui qui
        // l'avait ; la station que l'etat vient de poser devient celle de la copie de ce poste.
        static Snap keepBase; static Poste keepPost; static float keepAt = -10f; static bool keepTurnOn;
        static void KeepHolder(float now)
        {
            if (keepPost == null) return;
            if (now - keepAt > 0.6f || realT == null || fetch == null) { keepPost = null; return; }
            Snap cur = Take();
            if (Off(cur) || cur.Parent == null || KeyOf(cur.Parent) != keepPost.K) return;   // (pas encore pris dans cette image)
            Poste p = keepPost; keepPost = null;
            Poste h = keepBase.Parent != null ? PosteOf(KeyOf(keepBase.Parent), false) : null;
            if (h == null || h == p || !(h.On || h.ByHost)) return;   // (l'ancien n'est plus allume : ce poste garde le vrai jeu)
            p.Flags = cur; p.HasFlags = true;
            Restore(keepBase);
            if (Config.GetInt("Test", "RadioGardeFetch", 1) != 0) fetch.SendEvent("FETCH");   // ([Test] RadioGardeFetch=0 : essai, sans FETCH)
            string res = RallumageRepris ? ResumeChannels(now) : "";
            last = Take(); lastSet = true;
            if (p.G != null) Flags(p.G, p.Flags);
            if (postLogs++ < 200) Log.Info("radio : " + (keepTurnOn ? "allumage" : "molette") + " de " + p.K.name + " (" + Station(p.Flags, true) + ") : le vrai jeu de canaux reste a " + h.K.name + res);
        }

        static void MultiWatch(float now)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool rescanned = ScanCtrls(now);
            UnmuteOff();   // (OFF retenu : actions remises meme si l'etat n'est pas alle jusqu'au bout)
            StrayOff(now);
            HandOver(now);
            HandOverLive(now);
            if (!RawOnOff) ResumeWatch(now);
            KeepHolder(now);
            KeepParent(now);
            Snap cur = Take();
            Poste holder = Off(cur) || !cur.On || cur.Parent == null ? null : PosteOf(KeyOf(cur.Parent), true);
            // (radio portable sans piles allumee : le jeu y met quand meme le vrai jeu de canaux, volume a 0 -- tenue pour allumee puis
            // eteinte toutes les 0,6 s, sans fin, chez tous les joueurs ; les 200 lignes du journal radio y passaient en une minute,
            // essais de [MWC] 08/11. Elle n'est pas un poste allume.)
            if (holder != null && holder.K.name == Boombox && !BoomboxAlive(cur.Parent)) holder = null;
            if (holder != null)
            {
                if (!holder.On && now - holder.OffAt > 0.6f)
                {
                    holder.On = true; holder.OnAt = now;
                    if (postLogs++ < 200) Log.Info("radio : " + holder.K.name + " allume (le vrai jeu de canaux y joue)");
                }
                holder.Flags = cur; holder.HasFlags = true; holder.Speaker = cur.Parent; holder.SpeakerGuess = false; holder.ByHost = false;
            }
            Camera cam = Game.MainCam;
            Vector3 ear = cam != null ? cam.transform.position : Game.PlayerT != null ? Game.PlayerT.position : Vector3.zero;
            foreach (Poste p in postes.Values)
            {
                if (p.K == null) continue;
                if (p.On && p.K.name == Boombox && !BoomboxAlive(p.Speaker != null ? p.Speaker : p.K))
                {
                    p.On = false; p.OffAt = now;
                    if (postLogs++ < 200) Log.Info("radio : " + p.K.name + " eteint (piles ou arret)");
                }
                // (haut-parleur cherche ici, apres le releve des automates -- pas a l'arrivee de @postes, avant le premier releve : il
                // etait pris par son nom sous le poste ; revu tant qu'aucun automate ne l'a donne, et la copie suit)
                if ((p.On || p.ByHost) && (p.Speaker == null || (p.SpeakerGuess && rescanned))) p.Speaker = SpeakerOf(p.K, out p.SpeakerGuess);
                if (p.G != null && p.G.Go != null && p.Speaker != null && p.G.Go.transform.parent != p.Speaker)
                {
                    p.G.Go.transform.parent = p.Speaker; p.G.Go.transform.localPosition = Vector3.zero; p.G.Go.transform.localRotation = Quaternion.identity;
                    if (postLogs++ < 200) Log.Info("radio : copie de " + p.K.name + " portee par " + Recon.Path(p.Speaker));
                }
                bool want = (p.On || p.ByHost) && p != holder && p.Speaker != null;
                if (want && p.G == null) p.G = MakePostGhost(p);
                else if (!want && p.G != null) KillPost(p, p == holder ? "reprend le vrai jeu de canaux" : "eteint");
                if (p.G != null) SyncPost(p, ear);
            }
            if (Session.IsHost) SendPostes(now);
            else HostPostesExpire(now);
            sw.Stop();
            Measure(now, sw.Elapsed.TotalMilliseconds);
        }

        static Ghost MakePostGhost(Poste p)
        {
            var go = (GameObject)Object.Instantiate(realT.gameObject);
            go.name = "MWCoop-RadioCopie";
            bool cf = Config.GetInt("Test", "RadioCopieFetch", 1) != 0;
            foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName != "Fetch" || !cf) f.enabled = false;   // (tirage des morceaux : celui du vrai)
            go.transform.parent = p.Speaker; go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
            var g = new Ghost { K = p.K, Go = go, Who = -1, Fetch = Game.FsmOn(go, "Fetch"), Born = Time.realtimeSinceStartup };
            Transform c1 = go.transform.Find("Channel1"), fk = go.transform.Find("Folk");
            g.Ch = new[] { c1 != null ? c1.GetComponent<AudioSource>() : null, fk != null ? fk.GetComponent<AudioSource>() : null };
            foreach (AudioSource a in g.Ch) if (a != null) a.Stop();
            if (g.Fetch != null && cf) { g.Fetch.enabled = true; if (p.HasFlags) Flags(g, p.Flags); g.Fetch.SendEvent("FETCH"); }   // ([Test] RadioCopieFetch=0 : essai, Fetch de la copie coupe)
            ghosts.Add(g);
            p.Far = false;
            if (postLogs++ < 200) Log.Info("radio : " + p.K.name + " joue en copie (" + Recon.Path(p.Speaker) + ", " + Station(p.Flags, p.HasFlags) + (p.ByHost ? ", allume chez l'hote" : "") + ")");
            return g;
        }

        static void KillPost(Poste p, string why)
        {
            if (p.G != null)
            {
                DropSongs(p.G);
                if (p.G.Go != null) Object.Destroy(p.G.Go);
                ghosts.Remove(p.G);
                p.G = null;
            }
            if (postLogs++ < 200) Log.Info("radio : copie de " + p.K.name + " arretee (" + why + ")");
        }

        static string Station(Snap s, bool known) { return !known ? "station ?" : !s.M1 ? "radio locale" : !s.MF ? "radio folk" : s.St ? "parasites" : "muet"; }

        // Copie d'un poste : le morceau et la position du vrai jeu de canaux, une seule voix (le canal de sa station), aucune a volume
        // nul ; au-dela de RadioCopieLoin (80 m) de l'auditeur, en pause (Fetch coupe : ni parasites ni antenne), reprise a la position du moment.
        static void SyncPost(Poste p, Vector3 ear)
        {
            Ghost g = p.G;
            if (g.Go == null || g.Go.transform.parent == null) { ghosts.Remove(g); p.G = null; return; }
            if (p.ByHost && p.HasFlags && g.Fetch != null) Flags(g, p.Flags);
            float loin = Config.GetFloat("Test", "RadioCopieLoin", 80f);
            float d2 = (g.Go.transform.position - ear).sqrMagnitude;
            bool far = p.Far ? d2 > (loin - 5f) * (loin - 5f) : d2 > loin * loin;
            if (far != p.Far)
            {
                p.Far = far;
                if (g.Fetch != null) { g.Fetch.enabled = !far; if (!far) { if (p.HasFlags) Flags(g, p.Flags); g.Fetch.SendEvent("FETCH"); } }
                if (far) foreach (AudioSource a in g.Go.GetComponentsInChildren<AudioSource>(true)) { if (a.isPlaying) a.Stop(); }
                foreach (Transform t in g.Go.transform) if (far && (t.name == "Static" || t.name == "StaticAntenna")) t.gameObject.SetActive(false);
            }
            if (far) return;
            int act = !p.HasFlags ? -1 : !p.Flags.M1 ? 0 : !p.Flags.MF ? 1 : -1;
            for (int c = 0; c < 2 && c < g.Ch.Length; c++)
            {
                AudioSource src = ch[c], dst = g.Ch[c];
                if (src == null || dst == null) continue;
                if (dst.gameObject.activeSelf != src.gameObject.activeSelf) dst.gameObject.SetActive(src.gameObject.activeSelf);
                bool wantVoice = c == act && dst.volume > 0.005f && dst.gameObject.activeInHierarchy;
                if (!wantVoice) { if (dst.isPlaying) dst.Stop(); continue; }
                string key = src.clip != null && src.clip.name.Length == 0 ? ClipKey(src.clip) : null;
                if (key != null && key.StartsWith("#chanson:")) { OwnSong(g, c, key, src, dst); continue; }
                if (g.SongKey[c] != null) DropSong(g, c, dst);
                if (dst.clip != src.clip) { dst.clip = src.clip; if (src.clip != null) dst.time = Mathf.Min(src.time, src.clip.length - 0.05f); }
                if (src.isPlaying && !dst.isPlaying && dst.clip != null) { dst.Play(); dst.time = src.time; }
                else if (!src.isPlaying && dst.isPlaying) dst.Stop();
                else if (dst.isPlaying && dst.clip == src.clip && Mathf.Abs(dst.time - src.time) > 0.5f) dst.time = src.time;
            }
        }

        // ---------------------------------------------------------------- arrivant en cours de partie : postes allumes chez l'hote
        static void SendPostes(float now)
        {
            if (now < nextPostes || Session.RemoteCount == 0) return;
            nextPostes = now + 2f;
            var list = new List<Poste>();
            foreach (Poste p in postes.Values) if (p.K != null && p.On) list.Add(p);   // (station pas encore lue : 8, inconnue)
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@postes").U8(Mathf.Min(list.Count, 255));
            int n = 0;
            foreach (Poste p in list)
            {
                if (n++ >= 255) break;
                w.Str(p.Key).U8(!p.HasFlags ? 8 : (p.Flags.M1 ? 1 : 0) | (p.Flags.MF ? 2 : 0) | (p.Flags.St ? 4 : 0));
            }
            Session.SendAll(w, false);
        }

        public static void OnPostes(int who, NetReader r)
        {
            if (Session.IsHost || !Multi || !PlayerSync.InGame || !Find()) return;
            float now = Time.realtimeSinceStartup;
            hostListAt = now;
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                string key = r.Str(); int fl = r.U8();
                Transform k = PostKeyFromWire(key);
                if (k == null) continue;
                Poste p = PosteOf(k, true);
                p.HostSeen = now;
                if (p.On || p.ByHost) continue;
                p.ByHost = true;
                if ((fl & 8) == 0) { p.Flags = new Snap { M1 = (fl & 1) != 0, MF = (fl & 2) != 0, St = (fl & 4) != 0, On = true }; p.HasFlags = true; }
                if (postLogs++ < 200) Log.Info("radio : " + k.name + " allume chez l'hote");   // (haut-parleur : MultiWatch, apres le releve des automates)
            }
        }

        static void HostPostesExpire(float now)
        {
            if (hostListAt < 0f) return;   // (hote sans @postes)
            foreach (Poste p in postes.Values)
            {
                if (p.ByHost && now - p.HostSeen > 6f)
                {
                    p.ByHost = false;
                    if (postLogs++ < 200) Log.Info("radio : " + (p.K != null ? p.K.name : "?") + " eteint chez l'hote");
                }
                // (poste allume ici mais plus chez l'hote depuis 6 s -- son « eteint » rejoue perdu : sa copie s'arrete ; jamais le vrai jeu de canaux)
                if (p.On && p.G != null && now - hostListAt < 3f && now - p.HostSeen > 6f && now - p.OnAt > 6f)
                {
                    p.On = false; p.OffAt = now;
                    if (postLogs++ < 200) Log.Info("radio : " + p.K.name + " eteint chez l'hote : copie arretee ici");
                }
            }
        }

        // Poste d'une cle recue. Game.FindAny coupe le chemin au premier « / » : « GIFU(750/450psi) » (les deux jeux) n'etait jamais
        // trouve -- la GIFU de l'hote jamais jouee chez un arrivant, et sa copie arretee 6 s apres (« eteint chez l'hote ») alors que
        // l'hote l'entendait encore (essais de [MWC], 08/11). D'abord les postes deja connus, les automates releves et les voitures.
        static Transform PostKeyFromWire(string key)
        {
            if (key == Boombox)
            {
                foreach (Ctrl c in ctrls.Values) if (c.F != null && c.F.name.Length > 0) { Transform b = BoomboxOf(c.F.transform); if (b != null) return b; }
                return null;
            }
            foreach (Poste p in postes.Values) if (p.K != null && p.Key == key) return p.K;
            foreach (Ctrl c in ctrls.Values)
            {
                if (c.F == null) continue;
                Transform k = KeyOf(c.F.transform);
                if (k != null && SetKey(k) == key) return k;
            }
            for (int i = 0; i < VehicleSync.LocalCount; i++)
            {
                Rigidbody rb = VehicleSync.LocalBody(i);
                Transform k = rb != null ? VehicleSync.CarRoot(rb.transform) : null;
                if (k != null && SetKey(k) == key) return k;
            }
            GameObject g = Game.FindAny(key);
            return g != null ? g.transform : null;
        }

        // Haut-parleur d'un poste : celui ou ses automates mettent le jeu de canaux ; a defaut, le premier qui en porte le nom sous le
        // poste (guess : revu au releve suivant des automates).
        static Transform SpeakerOf(Transform k, out bool guess)
        {
            guess = false;
            foreach (Ctrl c in ctrls.Values)
                if (!c.Bad && c.F != null && KeyOf(c.F.transform) == k)
                    foreach (FsmGameObject v in c.Parents) if (v != null && v.Value != null) return v.Value.transform;
            guess = true;
            Transform best = null;
            foreach (Transform t in k.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == "SpeakerDash" || n == "SoundSpeaker" || n == "Speaker") return t;
                if (best == null && n.Contains("Speaker")) best = t;
            }
            return best;
        }

        // ---------------------------------------------------------------- mesure ([Test] RadioMesure=1 : toutes les 10 s)
        static float measAt; static double measMs, measMax; static int measN;
        static void Measure(float now, double ms)
        {
            if (!Config.Testing || Config.GetInt("Test", "RadioMesure", 0) == 0) return;
            measMs += ms; measN++; if (ms > measMax) measMax = ms;
            if (now < measAt) return;
            if (measAt <= 0f) { measAt = now + 10f; return; }
            measAt = now + 10f;
            int on = 0, copies = 0, voix = 0, loin = 0;
            foreach (Poste p in postes.Values)
            {
                if (p.On || p.ByHost) on++;
                if (p.G == null) continue;
                copies++; if (p.Far) loin++;
                foreach (AudioSource a in p.G.Go.GetComponentsInChildren<AudioSource>(true)) if (a.isPlaying) voix++;
            }
            int all = 0;
            foreach (AudioSource a in Object.FindObjectsOfType<AudioSource>()) if (a.isPlaying) all++;
            Log.Info("radio : mesure, " + on + " poste(s) allume(s), " + copies + " copie(s) (" + voix + " voix, " + loin + " en pause), vrai sous "
                     + (realT != null && realT.parent != null ? Recon.Path(realT.parent) : "-") + " ; " + all + " sources qui jouent dans la scene ; radio "
                     + (measN > 0 ? (measMs / measN).ToString("F3") : "-") + " ms par image en moyenne, " + measMax.ToString("F2") + " ms au plus");
            measMs = 0.0; measMax = 0.0; measN = 0;
        }

        // Essais (mscradio, [Test] RadioVidage=t, dossier #59) : automates du jeu de canaux (Fetch, Update de Channel1 et Folk) et des postes
        // de la SATSUMA et de la HAYOSIKO, etat par etat (actions, transitions) et variables.
        public static void TestDump()
        {
            if (realT == null || fetch == null) { Log.Info("autotest : radio, vidage : pas de canaux"); return; }
            DumpFsm("Fetch", fetch);
            if (upd != null) { DumpFsm("Channel1::Update", upd[0]); if (upd.Length > 1) DumpFsm("Folk::Update", upd[1]); }
            var seen = new HashSet<PlayMakerFSM>();
            foreach (Ctrl c in ctrls.Values)
            {
                if (c.Bad || c.F == null || !seen.Add(c.F)) continue;
                Transform k = KeyOf(c.F.transform);
                if (k == null || !(k.name.StartsWith("SATSUMA") || k.name.StartsWith("HAYOSIKO") || k.name.StartsWith("radio("))) continue;
                DumpFsm(Recon.Path(c.F.transform) + "::" + c.F.FsmName, c.F);
            }
        }
        static void DumpFsm(string label, PlayMakerFSM f)
        {
            if (f == null) { Log.Info("autotest : radio, vidage " + label + " : absent"); return; }
            Log.Info("autotest : radio, vidage " + label + " [" + f.ActiveStateName + "]" + (f.enabled ? "" : " (coupe)"));
            foreach (FsmState st in f.Fsm.States)
            {
                var tr = new System.Text.StringBuilder();
                foreach (FsmTransition x in st.Transitions) tr.Append(' ').Append(x.EventName).Append("->").Append(x.ToState);
                Log.Info("autotest :   " + Jobs.DumpActions(f, st.Name) + " ; transitions" + tr);
            }
            foreach (FsmTransition gt in f.FsmGlobalTransitions) Log.Info("autotest :   globale " + gt.EventName + " -> " + gt.ToState);
            foreach (NamedVariable v in f.FsmVariables.GetAllNamedVariables()) Log.Info("autotest :   var " + v.Name + " = " + v);
        }

        // Essais : etat des postes (allume, copie, station, distance).
        public static string PostesState()
        {
            var sb = new System.Text.StringBuilder("postes :");
            foreach (Poste p in postes.Values)
            {
                if (p.K == null) continue;
                sb.Append(' ').Append(p.K.name).Append(p.On ? " allume" : p.ByHost ? " allume chez l'hote" : " eteint")
                  .Append(p.G != null ? " (copie" + (p.Far ? " en pause" : "") + ")" : realT != null && realT.parent != null && KeyOf(realT.parent) == p.K ? " (vrai)" : "")
                  .Append(", ").Append(Station(p.Flags, p.HasFlags)).Append(" ;");
            }
            return sb.ToString();
        }
    }
}
