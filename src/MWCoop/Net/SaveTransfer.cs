using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop.Net
{
    // L'hote envoie sa sauvegarde (fichiers .txt du dossier du jeu) a chaque invite qui arrive,
    // une fois en jeu depuis 15 s (lancement groupe : pieces posees, couleur de voiture appliquee).
    // Nouvelle partie : le jeu n'ecrit en la commencant qu'une sauvegarde partielle (700 octets,
    // sans les pieces) ; l'hote sauvegarde alors pour de bon (SAVEGAME, comme en quittant) avant
    // le premier envoi, pour que l'invite ait le meme monde.
    // L'invite ne l'ecrit QUE dans un profil isole (MWCoop\profils\<profil>, cf. le chargeur) :
    // sa propre sauvegarde n'est jamais touchee.
    // Un invite revenu au menu de lui-meme pendant que l'hote joue redemande la sauvegarde actuelle
    // (sinon Flow le refaisait entrer sur sa vieille copie). Sauvegarde coop des toilettes : plus bas.
    public static class SaveTransfer
    {
        const int Chunk = 1000;
        static readonly string[] Skip = { "options.txt", "steam_autocloud.vdf", "Mods.txt" };

        public static bool Done;              // invite : transfert termine (meme vide ou refuse)
        public static bool Received;          // invite : sauvegarde de l'hote recue et ecrite
        public static bool HostHasSave;       // invite : l'hote avait une sauvegarde a envoyer
        public static float Progress;         // invite : 0..1 pendant la reception
        static List<string> names;
        static byte[][] files;
        static int expected, got;
        static readonly List<Peer> waiting = new List<Peer>();
        static float inGameSince = -1, sendAt = -1;
        static bool newGame;             // hote : nouvelle partie lancee, pas encore de vraie sauvegarde
        static int lastHostLevel = -1;   // invite : niveau de l'hote vu au dernier passage
        static float backToMenuAt = -1, nextWaitToast;
        static bool wasInGame;           // invite : en partie au dernier passage

        // Hote : un invite vient d'arriver ; sa sauvegarde partira des que possible.
        public static void Queue(Peer p)
        {
            if (!waiting.Contains(p)) waiting.Add(p);
            if (!PlayerSync.InGame) Log.Info("sauvegarde : " + p + " attend que l'hote soit en jeu");
        }

        public static void Update()
        {
            if (!Session.Active) return;
            float now = Time.realtimeSinceStartup;
            Buttons(now);
            if (!Session.IsHost) { GuestFollow(now); GuestCoop(now); return; }
            HostCoop(now);
            if (!PlayerSync.InGame) { inGameSince = sendAt = -1; if (CarColor.NewGame) newGame = true; return; }
            if (inGameSince < 0)
            {
                // L'hote (re)entre en jeu : chaque invite deja la recevra cette partie.
                inGameSince = now;
                foreach (Peer p in Session.T.Peers) if (p.Accepted && !waiting.Contains(p)) waiting.Add(p);
            }
            // Nouvelle partie : 13 s pour que les pieces se posent et que la couleur choisie soit appliquee.
            if (waiting.Count == 0 || newGame && now - inGameSince < 13f) return;
            if (sendAt < 0)
            {
                // Partie qui vient d'etre chargee (Continuer) : la sauvegarde du disque EST le monde, envoi
                // tout de suite. Sinon (nouvelle partie, invite arrive en cours de route) : on sauve d'abord.
                bool fresh = !newGame && now - inGameSince < 60f && File.Exists(Path.Combine(SaveDir, "savefile.txt"));
                if (!fresh)
                {
                    newGame = false;
                    Log.Info("sauvegarde : l'hote sauvegarde le monde actuel avant d'envoyer");
                    Game.SaveInPlace();
                    sendAt = now + 3f;
                    return;
                }
                sendAt = now;
            }
            if (now < sendAt || Game.Saving) return;
            if (!Settled()) { sendAt = now + 0.5f; return; }
            sendAt = -1;
            foreach (Peer p in waiting) if (p.Accepted && Session.T.Peers.Contains(p)) SendTo(p);
            waiting.Clear();
        }

        // Le jeu ecrit ses fichiers sur plusieurs images (jusqu'a 5 s) : fini quand rien n'a bouge depuis 2 s.
        static bool Settled()
        {
            DateTime newest = DateTime.MinValue;
            foreach (string f in SaveFiles()) { DateTime m = File.GetLastWriteTime(f); if (m > newest) newest = m; }
            return (DateTime.Now - newest).TotalSeconds >= 2;
        }

        // Invite : l'hote revient au menu -> on y revient aussi (sans sauver : la sauvegarde recue
        // doit rester intacte) et on attend sa prochaine partie et sa nouvelle sauvegarde.
        static void GuestFollow(float now)
        {
            PlayerInfo host = Session.Host;
            int lv = host != null ? host.Level : -1;
            // Au menu en attendant : dire ce qui se passe plutot que rien.
            if (!PlayerSync.InGame && !Done && Session.T != null && Session.T.Peers.Count > 0 && now >= nextWaitToast)
            {
                nextWaitToast = now + 8f;
                Hud.Toast(lv == 1 ? "L'hote prepare sa sauvegarde, vous entrez en jeu dans un instant..." : "En attente : l'hote n'est pas encore en jeu");
            }
            if (lastHostLevel == 1 && lv == 0)
            {
                Done = Received = HostHasSave = false;
                if (PlayerSync.InGame)
                {
                    backToMenuAt = now + 3f;
                    Hud.Toast("L'hote est revenu au menu : retour au menu");
                    Log.Info("l'hote est revenu au menu : l'invite le suit");
                }
            }
            lastHostLevel = lv;
            if (backToMenuAt > 0 && now >= backToMenuAt)
            {
                backToMenuAt = -1;
                if (PlayerSync.InGame && lv == 0) Application.LoadLevel("MainMenu");
            }
            // Revenu au menu DE LUI-MEME (menu du jeu, toilettes sans hote...) pendant que l'hote joue : sa
            // copie de la sauvegarde est perimee, voire remplacee par la sienne (sauvegarde coop comparee),
            // et Flow l'aurait fait rentrer dessus, sur son monde a lui. On redemande celle de l'hote.
            bool inGame = PlayerSync.InGame;
            if (wasInGame && !inGame && lv == 1 && backToMenuAt < 0)
            {
                Done = Received = HostHasSave = false;
                Session.SendToHost(new NetWriter(CoopMsg).U8(K_NEED), true);
                Log.Info("sauvegarde : revenu au menu pendant que l'hote joue, sa sauvegarde actuelle est redemandee");
            }
            wasInGame = inGame;
        }

        public static string SaveDir { get { return Application.persistentDataPath; } }

        public static bool IsolatedProfile
        {
            get { return SaveDir.Replace('\\', '/').Contains("/MWCoop/profils/"); }
        }

        // Profil isole tout neuf : reprend les options du joueur (graphismes, sensibilite...) depuis
        // son vrai dossier de sauvegardes, en lecture seule.
        public static void CopyPlayerOptions()
        {
            if (!IsolatedProfile) return;
            string mine = Path.Combine(SaveDir, "options.txt");
            string real = Path.Combine(Path.Combine(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "",
                @"AppData\LocalLow"), "Amistech"), @"My Winter Car\options.txt");
            try
            {
                if (File.Exists(mine) || !File.Exists(real)) return;
                Directory.CreateDirectory(SaveDir);
                File.Copy(real, mine);
                Log.Info("options du joueur reprises dans le profil");
            }
            catch (Exception e) { Log.Warn("options du joueur : " + e.Message); }
        }

        static List<string> SaveFiles()
        {
            var list = new List<string>();
            if (!Directory.Exists(SaveDir)) return list;
            foreach (string f in Directory.GetFiles(SaveDir, "*.txt"))
                if (Array.IndexOf(Skip, Path.GetFileName(f)) < 0) list.Add(f);
            return list;
        }

        public static void SendTo(Peer p)
        {
            List<string> list = SaveFiles();
            var data = new List<byte[]>();
            long total = 0;
            foreach (string f in list) { byte[] b = File.ReadAllBytes(f); data.Add(b); total += b.Length; }
            var w = new NetWriter(Msg.SaveBegin).U8(list.Count);
            for (int i = 0; i < list.Count; i++) w.Str(Path.GetFileName(list[i])).I32(data[i].Length);
            Session.T.SendReliable(p, w.ToArray());
            for (int i = 0; i < data.Count; i++)
                for (int off = 0; off < data[i].Length; off += Chunk)
                {
                    int n = Math.Min(Chunk, data[i].Length - off);
                    Session.T.SendReliable(p, new NetWriter(Msg.SaveChunk).U8(i).I32(off).Bytes(data[i], off, n).ToArray());
                }
            Session.T.SendReliable(p, new NetWriter(Msg.SaveEnd).U8(list.Count).ToArray());
            Log.Info("sauvegarde envoyee a " + p + " : " + list.Count + " fichiers, " + total / 1024 + " Ko");
        }

        public static void OnMessage(Msg type, NetReader r)
        {
            if (Session.IsHost) return;
            switch (type)
            {
                case Msg.SaveBegin:
                    int n = r.U8();
                    names = new List<string>();
                    files = new byte[n][];
                    expected = got = 0;
                    for (int i = 0; i < n; i++)
                    {
                        string name = Path.GetFileName(r.Str());
                        int size = r.I32();
                        names.Add(name);
                        files[i] = new byte[size];
                        expected += size;
                    }
                    Received = Done = false;
                    HostHasSave = n > 0;
                    Progress = 0;
                    Log.Info("reception de la sauvegarde de l'hote : " + n + " fichiers, " + expected / 1024 + " Ko");
                    break;
                case Msg.SaveChunk:
                    if (files == null) return;
                    int idx = r.U8(), off = r.I32();
                    byte[] part = r.Bytes();
                    Buffer.BlockCopy(part, 0, files[idx], off, part.Length);
                    got += part.Length;
                    Progress = expected > 0 ? (float)got / expected : 1f;
                    break;
                case Msg.SaveEnd:
                    if (files != null) Write();
                    Done = true;
                    break;
            }
        }

        static void Write()
        {
            if (!IsolatedProfile)
            {
                Log.Error("sauvegarde de l'hote NON ecrite : le jeu n'est pas lance dans un profil MWCoop (" + SaveDir + ")");
                Hud.Toast("Sauvegarde de l'hote ignoree : lancez le jeu depuis MWCoop.exe");
                files = null;
                return;
            }
            try
            {
                Directory.CreateDirectory(SaveDir);
                foreach (string f in SaveFiles()) File.Delete(f);
                for (int i = 0; i < names.Count; i++) File.WriteAllBytes(Path.Combine(SaveDir, names[i]), files[i]);
            }
            catch (Exception e)
            {
                // Fichier bloque (antivirus, synchro OneDrive...) : on le dit au lieu de rester au menu.
                Log.Error("sauvegarde de l'hote non ecrite : " + e.Message);
                Hud.Toast("Sauvegarde de l'hote non ecrite : " + e.Message);
                files = null;
                return;
            }
            Log.Info("sauvegarde de l'hote ecrite dans " + SaveDir);
            Hud.Toast("Sauvegarde de l'hote recue");
            files = null;
            Received = true;
            Progress = 1;
        }

        // ============================================================ sauvegarde coop (toilettes)
        // Toilettes du jeu : SAVEGAME :: Button (HOMENEW, YARD, COTTAGE, CABIN, STORE_AREA, LANDFILL,
        // REPAIRSHOP, JAIL), "SAVE AND QUIT TO MENU" : Wait for click -CLICK-> Mute audio (son coupe) ->
        // Save (0,3 s) -> Save all (SAVEGAME a tous) -> Wait 2 (0,4 s) -> Load menu (LoadLevel MainMenu) ;
        // prison : CLICK -> State 1 (jours restants) -> Mute audio. Chez l'hote, tous les invites
        // repartaient au menu ; chez un invite, sa sauvegarde (profil isole) ne servait a rien : Flow le
        // refaisait entrer dessus, sur SON monde, et le prochain envoi de l'hote l'ecrasait.
        // En coop, le CLICK mene a "Wait" (transition redirigee) ; une action en tete de "Wait" voit le clic
        // (derniere transition : CLICK depuis "Wait for click") :
        //  - hote : sauvegarde EN JEU (Game.SaveInPlace), annoncee aux invites et faite FlushDelay s plus
        //    tard (le temps que leurs derniers changements arrivent) ; personne ne part. Un 2e clic dans les
        //    QuitWindow s qui suivent = sauver et quitter comme le jeu (les invites suivent au menu, puis
        //    reviennent avec l'hote et sa sauvegarde neuve) ;
        //  - invite : demande cette meme sauvegarde a l'hote et reste en jeu ;
        //  - seul (hote sans invite, invite sans hote) : comme le jeu (etat d'origine du clic).
        // Le texte du survol le dit. Ce qu'un invite a change et que l'hote n'a pas sera perdu a la prochaine
        // session (sa sauvegarde est la seule envoyee) : avant d'ecrire, l'hote note les ecarts durables de
        // l'audit ; chaque invite lui dit ses actions non partagees, puis (profil isole) sauve aussi chez lui
        // et lui envoie ses fichiers : l'hote compare les deux sauvegardes cle par cle, avec tolerance sur
        // les nombres et les positions -> dumps/ecarts-sauvegarde-joueur<n>.txt ([Coop] ComparerSauvegardes=0 : sans).
        // Messages : Msg 9 (StartGame, reserve jusqu'ici ; Session le passe a World.OnMessage qui le renvoie
        // a OnCoop), 1er octet = genre.
        public const Msg CoopMsg = Msg.StartGame;
        const int K_REQ = 1, K_START = 2, K_NOW = 3, K_DONE = 4, K_QUIT = 5, K_NEED = 6, K_REPORT = 7, K_FBEGIN = 8, K_FCHUNK = 9, K_FEND = 10;
        //   K_REQ     invite -> hote : sauvegarde demandee (clic aux toilettes)
        //   K_START   hote -> invites : U16 n, Str demandeur : sauvegarde dans FlushDelay s
        //   K_NOW     hote -> invites : U16 n, Bool comparer : l'hote ecrit maintenant (l'invite aussi, s'il compare)
        //   K_DONE    hote -> invites : U16 n, Str demandeur : sauvegarde ecrite, tout le monde reste
        //   K_QUIT    hote -> invites : l'hote sauvegarde et quitte au menu (2e clic)
        //   K_NEED    invite -> hote : revenu au menu, renvoyer la sauvegarde actuelle
        //   K_REPORT  invite -> hote : U16 n, U16 actions non partagees, U8 k, k x Str (les plus recentes)
        //   K_FBEGIN  invite -> hote : U16 n, U8 fichiers, (Str nom, I32 taille) x fichiers
        //   K_FCHUNK  invite -> hote : U16 n, U8 fichier, I32 decalage, Bytes (<= Chunk)
        //   K_FEND    invite -> hote : U16 n
        const float FlushDelay = 2f, QuitWindow = 15f;
        const int MaxCompared = 128 * 1024;   // speedcam.txt (photos des radars, 225 Ko) : pas compare
        const string OrigLabel = "SAVE AND QUIT TO MENU";
        static bool CompareOn { get { return Config.GetInt("Coop", "ComparerSauvegardes", 1) != 0; } }

        class SaveButton
        {
            public PlayMakerFSM F;
            public string Target;     // etat d'origine du CLICK : "Mute audio" (prison : "State 1")
            public FsmTransition Click;
            public bool Hooked, Broken;
            public FsmString Label;   // texte du survol (SetStringValue de "Wait for click")
        }

        class ButtonHook : ModHook
        {
            public override string Module { get { return "sauvegarde"; } }
            public SaveButton B;
            public override void OnEnter()
            {
                try
                {
                    FsmTransition t = Fsm.LastTransition;
                    FsmState prev = Fsm.PreviousActiveState;
                    if (Replay.Depth == 0 && t != null && t.EventName == "CLICK" && prev != null && prev.Name == "Wait for click") Clicked(B);
                }
                catch (Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static readonly List<SaveButton> buttons = new List<SaveButton>();
        static bool buttonsScanned;
        static float buttonsAt = -1, nextButtonCheck, lastClickAt = -10;
        static SaveButton proceed;          // clic a laisser au jeu (sauver et quitter), a l'image suivante
        // Hote
        static int coopSeq, coopPhase, coopDone;   // phase : 0 rien, 1 annoncee (derniers messages des invites), 2 ecriture
        static float coopAt, quitUntil, lastCoopDone = -100;
        static string coopBy = "";
        static bool coopByHost;
        static Dictionary<string, byte[]> hostSave;  // fichiers de la derniere sauvegarde coop (comparaison)
        static int hostSaveSeq;
        class GuestSave { public int Seq; public string Name; public string[] Names; public byte[][] Data; public bool Complete; }
        static readonly Dictionary<int, GuestSave> guestSaves = new Dictionary<int, GuestSave>();
        // Invite
        static float guestAskedAt = -100, guestCmpAt;
        static int guestCmpSeq, guestDone;
        static string lastUnsharedTop;

        // ------------------------------------------------------------ boutons
        static void Buttons(float now)
        {
            if (!PlayerSync.InGame) { buttons.Clear(); buttonsScanned = false; buttonsAt = -1; proceed = null; return; }
            if (proceed != null)
            {
                SaveButton b = proceed;
                proceed = null;
                if (b.F != null && b.F.enabled && b.F.gameObject.activeInHierarchy)
                {
                    Log.Info("toilettes : sauver et quitter au menu comme le jeu (" + Recon.Path(b.F.transform) + " -> " + b.Target + ")");
                    Game.SetState(b.F, b.Target);
                }
            }
            if (now < nextButtonCheck) return;
            nextButtonCheck = now + 1f;
            // Releve 3 s apres l'entree en jeu (et de nouveau si la scene a ete rechargee).
            if (buttonsScanned && buttons.Count > 0 && buttons[0].F == null) buttonsScanned = false;
            if (!buttonsScanned)
            {
                if (buttonsAt < 0) buttonsAt = now + 3f;
                if (now < buttonsAt) return;
                buttonsScanned = true;
                ScanButtons();
            }
            // Toilettes sous un LOD eteint : automate jamais demarre, ses actions ne sont pas chargees (et le
            // seraient de nouveau a son demarrage) ; l'action est ajoutee des qu'il a demarre.
            string label = LabelText(now);
            foreach (SaveButton b in buttons)
            {
                if (b.F == null || b.Broken) continue;
                if (!b.Hooked) Hook(b);
                if (b.Label != null && b.Label.Value != label) b.Label.Value = label;
            }
        }

        static void ScanButtons()
        {
            buttons.Clear();
            var names = new StringBuilder();
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Button" || f.gameObject.name != "SAVEGAME") continue;
                FsmState wfc = f.Fsm.GetState("Wait for click");
                if (wfc == null || f.Fsm.GetState("Wait") == null || f.Fsm.GetState("Load menu") == null) continue;
                FsmTransition click = null;
                foreach (FsmTransition t in wfc.Transitions) if (t.EventName == "CLICK") click = t;
                if (click == null || click.ToState == "Wait") continue;
                if (!Replay.Claim(f, "sauvegarde")) { Log.Warn("toilettes : " + Recon.Path(f.transform) + " deja accrochees par " + Replay.Owner(f)); continue; }
                // La redirection tient meme avant le demarrage de l'automate (les transitions ne sont pas
                // rechargees) : un clic avant l'ajout de l'action ne ferait que revenir a "Wait".
                var b = new SaveButton { F = f, Target = click.ToState, Click = click };
                click.ToState = "Wait";
                buttons.Add(b);
                Hook(b);
                names.Append(names.Length > 0 ? ", " : "").Append(f.transform.root.name).Append(b.Hooked ? "" : " (pas demarre)");
            }
            Log.Info("toilettes : " + buttons.Count + " boutons SAVEGAME en mode coop : " + names);
        }

        static void Hook(SaveButton b)
        {
            FsmState s = b.F.Fsm.GetState("Wait"), w = b.F.Fsm.GetState("Wait for click");
            if (s == null || w == null || !s.IsInitialized || !w.IsInitialized) return;
            try
            {
                var list = new List<FsmStateAction>(s.Actions);
                list.Insert(0, new ButtonHook { B = b });
                s.Actions = list.ToArray();
                b.Hooked = true;
                // Texte du survol : le parametre FsmString du SetStringValue qui affiche "SAVE AND QUIT TO MENU".
                foreach (FsmStateAction a in w.Actions)
                {
                    if (a == null) continue;
                    foreach (System.Reflection.FieldInfo fi in a.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (fi.FieldType != typeof(FsmString)) continue;
                        var fs = fi.GetValue(a) as FsmString;
                        if (fs != null && !fs.UseVariable && fs.Value == OrigLabel) b.Label = fs;
                    }
                }
            }
            catch (Exception e)
            {
                // Action non posee : le clic reprend son chemin d'origine (comme le jeu) plutot que de ne rien faire.
                b.Broken = true;
                if (!b.Hooked) b.Click.ToState = b.Target;
                Log.Warn("toilettes : " + Recon.Path(b.F.transform) + " : " + e.Message);
            }
        }

        static string LabelText(float now)
        {
            if (Session.IsHost)
            {
                if (Session.RemoteCount == 0) return OrigLabel;
                return now < quitUntil ? "RECLIQUER : SAUVER ET QUITTER AU MENU" : "SAUVEGARDER (COOP, ON RESTE EN JEU)";
            }
            return Session.Host == null ? OrigLabel : "SAUVEGARDER (CHEZ L'HOTE)";
        }

        // Clic du joueur local sur des toilettes (action de tete de "Wait").
        static void Clicked(SaveButton b)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastClickAt < 1f) return;
            lastClickAt = now;
            string where = b.F.transform.root.name;
            if (Session.IsHost)
            {
                if (Session.RemoteCount == 0) { Log.Info("toilettes (" + where + ") : aucun invite, sauver et quitter comme le jeu"); proceed = b; return; }
                if (coopPhase != 0) { Hud.Toast("Sauvegarde en cours..."); return; }
                if (now < quitUntil)
                {
                    quitUntil = 0;
                    Session.Broadcast(new NetWriter(CoopMsg).U8(K_QUIT), true);
                    Log.Info("toilettes (" + where + ") : 2e clic, l'hote sauve et quitte au menu ; les invites le suivent et reviendront avec lui");
                    proceed = b;
                    return;
                }
                Log.Info("toilettes (" + where + ") : clic de l'hote, sauvegarde coop en jeu");
                StartCoop(Session.Me.Name, true);
                return;
            }
            if (Session.Host == null) { Log.Info("toilettes (" + where + ") : pas d'hote, sauver et quitter comme le jeu"); proceed = b; return; }
            if (now - guestAskedAt < 6f) { Hud.Toast("Sauvegarde deja demandee a l'hote"); return; }
            guestAskedAt = now;
            Session.SendToHost(new NetWriter(CoopMsg).U8(K_REQ), true);
            Log.Info("toilettes (" + where + ") : sauvegarde demandee a l'hote, l'invite reste en jeu");
            Hud.Toast("Sauvegarde demandee a l'hote : vous restez en jeu");
        }

        // ------------------------------------------------------------ hote
        static void StartCoop(string by, bool hostButton)
        {
            float now = Time.realtimeSinceStartup;
            if (!PlayerSync.InGame) { Log.Info("sauvegarde coop demandee par " + by + " : l'hote n'est pas en jeu"); return; }
            if (coopPhase != 0) { Log.Info("sauvegarde coop demandee par " + by + " : deja en cours (" + coopBy + ")"); return; }
            if (now - lastCoopDone < 5f)
            {
                // Deuxieme demande juste apres (deux joueurs, deux clics) : elle vient d'etre faite.
                Log.Info("sauvegarde coop demandee par " + by + " : faite il y a " + (now - lastCoopDone).ToString("F0") + " s, rien de plus");
                Session.Broadcast(new NetWriter(CoopMsg).U8(K_DONE).U16(coopSeq).Str(coopBy), true);
                return;
            }
            coopSeq = (coopSeq + 1) & 0xFFFF;
            if (coopSeq == 0) coopSeq = 1;   // (0 : aucune comparaison en attente chez l'invite)
            coopPhase = 1; coopAt = now + FlushDelay; coopBy = by; coopByHost = hostButton;
            hostSave = null; guestSaves.Clear();
            Session.Broadcast(new NetWriter(CoopMsg).U8(K_START).U16(coopSeq).Str(by), true);
            Log.Info("sauvegarde coop #" + coopSeq + " demandee par " + by + " : ecriture dans " + FlushDelay + " s (derniers changements des invites)");
            Hud.Toast(hostButton ? "Sauvegarde coop : les invites restent en jeu" : by + " demande une sauvegarde : dans " + (int)FlushDelay + " s");
        }

        static void HostCoop(float now)
        {
            if (coopPhase == 0) return;
            if (!PlayerSync.InGame) { coopPhase = 0; Log.Warn("sauvegarde coop #" + coopSeq + " abandonnee : l'hote n'est plus en jeu"); return; }
            if (now < coopAt || Game.Saving) return;
            if (coopPhase == 1)
            {
                // Ecarts que l'audit voit en ce moment avec les invites (durables) : ils partent tels quels.
                if (Audit.Desyncs.Count > 0)
                {
                    Log.Info("sauvegarde coop #" + coopSeq + " : " + Audit.Desyncs.Count + " ecart(s) durable(s) avec les invites selon l'audit :");
                    for (int i = 0; i < Audit.Desyncs.Count && i < 8; i++) Log.Info("  ecart audit : " + Audit.Desyncs[i]);
                }
                Game.SaveInPlace();
                Session.Broadcast(new NetWriter(CoopMsg).U8(K_NOW).U16(coopSeq).Bool(CompareOn), true);
                coopPhase = 2; coopAt = now + 3f;
                return;
            }
            if (!Settled()) { coopAt = now + 0.5f; return; }
            coopPhase = 0; lastCoopDone = now; coopDone++;
            Session.Broadcast(new NetWriter(CoopMsg).U8(K_DONE).U16(coopSeq).Str(coopBy), true);
            int inGame = 0;
            foreach (PlayerInfo p in Session.Players.Values) if (!p.Local && p.Level == 1) inGame++;
            Log.Info("sauvegarde coop #" + coopSeq + " ecrite (demandee par " + coopBy + ") : personne n'a quitte la partie, "
                     + inGame + "/" + Session.RemoteCount + " invite(s) en jeu");
            if (coopByHost)
            {
                quitUntil = now + QuitWindow;
                Hud.Toast("Partie sauvegardee, tout le monde reste en jeu. Recliquez dans les " + (int)QuitWindow + " s pour sauver et quitter au menu");
            }
            else Hud.Toast("Partie sauvegardee (demandee par " + coopBy + ")");
            if (CompareOn) { hostSave = ReadSaveFiles(); hostSaveSeq = coopSeq; TryCompare(); }
        }

        // Messages de la sauvegarde coop (Msg 9), des deux cotes.
        public static void OnCoop(Peer from, NetReader r)
        {
            int k = r.U8();
            float now = Time.realtimeSinceStartup;
            if (Session.IsHost)
            {
                PlayerInfo pi;
                string name = Session.Players.TryGetValue(from.Id, out pi) ? pi.Name : "#" + from.Id;
                switch (k)
                {
                    case K_REQ: Log.Info("toilettes : " + name + " demande une sauvegarde"); StartCoop(name, false); break;
                    case K_NEED: Log.Info("sauvegarde : " + name + " est revenu au menu, il recevra la sauvegarde actuelle"); Queue(from); break;
                    case K_REPORT: OnReport(name, r); break;
                    case K_FBEGIN: case K_FCHUNK: case K_FEND: OnGuestFile(from, name, k, r); break;
                }
                return;
            }
            switch (k)
            {
                case K_START:
                {
                    int seq = r.U16();
                    string by = r.Str();
                    Log.Info("sauvegarde coop #" + seq + " annoncee par l'hote (demandee par " + by + ")");
                    Hud.Toast("Sauvegarde coop dans " + (int)FlushDelay + " s (" + by + ") : vous restez en jeu");
                    SendReport(seq);
                    break;
                }
                case K_NOW:
                {
                    int seq = r.U16();
                    bool cmp = r.Bool();
                    if (!cmp || !PlayerSync.InGame) break;
                    // Jamais dans la vraie sauvegarde du joueur : seulement dans son profil isole.
                    if (!IsolatedProfile) { Log.Info("sauvegarde coop #" + seq + " : pas de comparaison, profil non isole"); break; }
                    Game.SaveInPlace();
                    guestCmpSeq = seq; guestCmpAt = now + 3f;
                    break;
                }
                case K_DONE:
                {
                    int seq = r.U16();
                    string by = r.Str();
                    guestDone++;
                    Log.Info("sauvegarde coop #" + seq + " ecrite chez l'hote (demandee par " + by + "), l'invite reste en jeu");
                    Hud.Toast("Partie sauvegardee chez l'hote (" + by + ")");
                    break;
                }
                case K_QUIT:
                    Log.Info("l'hote sauvegarde et quitte au menu (toilettes) : l'invite le suivra");
                    Hud.Toast("L'hote sauvegarde et quitte au menu : vous le suivez, puis revenez avec lui");
                    break;
            }
        }

        // Hote : actions non partagees d'un invite (audit) depuis la sauvegarde precedente.
        static void OnReport(string name, NetReader r)
        {
            int seq = r.U16(), total = r.U16(), n = r.U8();
            if (total == 0) { Log.Info("sauvegarde coop #" + seq + " : " + name + " : aucune action non partagee vue par son audit depuis la derniere sauvegarde"); return; }
            Log.Info("sauvegarde coop #" + seq + " : " + name + " : " + total + " action(s) NON PARTAGEE(S) depuis la derniere sauvegarde (audit ; "
                     + "perdues a la prochaine session si elles touchent la sauvegarde) :");
            for (int i = 0; i < n; i++) Log.Info("  non partagee : " + r.Str());
        }

        static void OnGuestFile(Peer from, string name, int k, NetReader r)
        {
            int seq = r.U16();
            GuestSave gs;
            if (k == K_FBEGIN)
            {
                int n = r.U8();
                if (n > 32) return;
                gs = new GuestSave { Seq = seq, Name = name, Names = new string[n], Data = new byte[n][] };
                for (int i = 0; i < n; i++)
                {
                    gs.Names[i] = Path.GetFileName(r.Str());
                    int size = r.I32();
                    if (size < 0 || size > MaxCompared) return;
                    gs.Data[i] = new byte[size];
                }
                guestSaves[from.Id] = gs;
                return;
            }
            if (!guestSaves.TryGetValue(from.Id, out gs) || gs.Seq != seq) return;
            if (k == K_FCHUNK)
            {
                int idx = r.U8(), off = r.I32();
                byte[] part = r.Bytes();
                if (idx >= gs.Data.Length || off < 0 || off + part.Length > gs.Data[idx].Length) return;
                Buffer.BlockCopy(part, 0, gs.Data[idx], off, part.Length);
                return;
            }
            gs.Complete = true;
            Log.Info("sauvegarde coop #" + seq + " : sauvegarde de " + name + " recue (" + gs.Names.Length + " fichiers), comparaison");
            TryCompare();
        }

        static void TryCompare()
        {
            if (hostSave == null) return;
            foreach (KeyValuePair<int, GuestSave> kv in new List<KeyValuePair<int, GuestSave>>(guestSaves))
            {
                GuestSave gs = kv.Value;
                if (!gs.Complete || gs.Seq != hostSaveSeq) continue;
                guestSaves.Remove(kv.Key);
                var files = new Dictionary<string, byte[]>();
                for (int i = 0; i < gs.Names.Length; i++) files[gs.Names[i]] = gs.Data[i];
                try { CompareSaves(kv.Key, gs.Name, gs.Seq, files); }
                catch (Exception e) { Log.Error("sauvegarde coop : comparaison avec " + gs.Name + " : " + e); }
            }
        }

        static Dictionary<string, byte[]> ReadSaveFiles()
        {
            var d = new Dictionary<string, byte[]>();
            foreach (string f in SaveFiles())
            {
                try { if (new FileInfo(f).Length <= MaxCompared) d[Path.GetFileName(f)] = File.ReadAllBytes(f); }
                catch (Exception e) { Log.Warn("sauvegarde : lecture de " + Path.GetFileName(f) + " : " + e.Message); }
            }
            return d;
        }

        // ------------------------------------------------------------ invite
        static void GuestCoop(float now)
        {
            if (guestCmpSeq == 0 || now < guestCmpAt || Game.Saving) return;
            if (!PlayerSync.InGame) { guestCmpSeq = 0; return; }
            if (!Settled()) { guestCmpAt = now + 0.5f; return; }
            int seq = guestCmpSeq;
            guestCmpSeq = 0;
            Dictionary<string, byte[]> files = ReadSaveFiles();
            var names = new List<string>();
            // (en-tete < 1100 octets : 20 fichiers aux noms courts au plus ; le jeu en ecrit 8)
            foreach (string n in files.Keys) if (names.Count < 20 && n.Length <= 40) names.Add(n);
            var w = new NetWriter(CoopMsg).U8(K_FBEGIN).U16(seq).U8(names.Count);
            long total = 0;
            foreach (string n in names) { w.Str(n).I32(files[n].Length); total += files[n].Length; }
            Session.SendToHost(w, true);
            for (int i = 0; i < names.Count; i++)
            {
                byte[] b = files[names[i]];
                for (int off = 0; off < b.Length; off += Chunk)
                    Session.SendToHost(new NetWriter(CoopMsg).U8(K_FCHUNK).U16(seq).U8(i).I32(off).Bytes(b, off, Math.Min(Chunk, b.Length - off)), true);
            }
            Session.SendToHost(new NetWriter(CoopMsg).U8(K_FEND).U16(seq), true);
            Log.Info("sauvegarde coop #" + seq + " : sauvegarde de l'invite envoyee a l'hote pour comparaison (" + names.Count + " fichiers, " + total / 1024 + " Ko)");
        }

        // Actions de ce joueur que l'audit a vues partir chez personne, depuis le rapport precedent
        // (Audit.Unshared : les plus recentes en tete, 40 au plus).
        static void SendReport(int seq)
        {
            var fresh = new List<string>();
            // (Par reference : la meme action refaite donne un texte identique mais une autre chaine.)
            foreach (string s in Audit.Unshared) { if ((object)s == (object)lastUnsharedTop) break; fresh.Add(s); }
            if (Audit.Unshared.Count > 0) lastUnsharedTop = Audit.Unshared[0];
            // (4 lignes de 100 caracteres au plus : < 1100 octets meme avec des noms accentues)
            int n = Math.Min(4, fresh.Count);
            var w = new NetWriter(CoopMsg).U8(K_REPORT).U16(seq).U16(fresh.Count).U8(n);
            for (int i = 0; i < n; i++) w.Str(Session.Clean(fresh[i], 100));
            Session.SendToHost(w, true);
        }

        // ------------------------------------------------------------ comparaison des sauvegardes
        // Fichier de sauvegarde du jeu (ES2) : suite de "~", cle (longueur en 7 bits + UTF-8), longueur
        // (I32), valeur. Valeur simple : FF, type (4 octets), donnee, 7B. Rend null si le format surprend.
        static Dictionary<string, byte[]> ParseEs2(byte[] b)
        {
            var d = new Dictionary<string, byte[]>();
            int p = 0;
            try
            {
                while (p < b.Length)
                {
                    if (b[p++] != 0x7E) return null;
                    int n = 0, shift = 0;
                    while (true)
                    {
                        int c = b[p++];
                        n |= (c & 0x7F) << shift;
                        if (c < 0x80) break;
                        shift += 7;
                        if (shift > 28) return null;
                    }
                    if (n < 0 || p + n > b.Length) return null;
                    string tag = Encoding.UTF8.GetString(b, p, n);
                    p += n;
                    int len = BitConverter.ToInt32(b, p);
                    p += 4;
                    if (len < 0 || p + len > b.Length) return null;
                    var v = new byte[len];
                    Buffer.BlockCopy(b, p, v, 0, len);
                    p += len;
                    d[tag] = v;
                }
            }
            catch (Exception) { return null; }
            return d;
        }

        // Types ES2 (octets 1 a 4 de la valeur, petit-boutiste).
        const uint T_Int = 0xE2A80856, T_Float = 0x6E3ED76B, T_Bool = 0xAD4D7C9C, T_String = 0xFDE9F1EE,
                   T_Transform = 0x097AFA76, T_Vector3 = 0xEC66DC46, T_Color = 0x32CF4B31;

        static uint TypeOf(byte[] v) { return v.Length >= 6 && v[0] == 0xFF ? BitConverter.ToUInt32(v, 1) : 0u; }
        static float F(byte[] v, int at) { return BitConverter.ToSingle(v, at); }
        static bool Near(float a, float b, float tol) { return Math.Abs(a - b) <= tol; }

        // Meme valeur, aux arrondis pres : nombres (1 %), vecteurs, couleurs, poses (1 m, 10 degres : les
        // voitures a l'arret ne sont recalees chez l'invite qu'au-dela de 1 m, VehicleSync).
        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length == b.Length)
            {
                int i = 0;
                while (i < a.Length && a[i] == b[i]) i++;
                if (i == a.Length) return true;
            }
            uint t = TypeOf(a);
            if (t == 0 || t != TypeOf(b) || a.Length != b.Length) return false;
            switch (t)
            {
                case T_Float:
                    if (a.Length < 9) return false;
                    float x = F(a, 5), y = F(b, 5);
                    return Near(x, y, 0.01f + 0.01f * Math.Max(Math.Abs(x), Math.Abs(y)));
                case T_Vector3:
                    if (a.Length < 17) return false;
                    for (int i = 0; i < 3; i++) if (!Near(F(a, 5 + 4 * i), F(b, 5 + 4 * i), 0.05f)) return false;
                    return true;
                case T_Color:
                    if (a.Length < 21) return false;
                    for (int i = 0; i < 4; i++) if (!Near(F(a, 5 + 4 * i), F(b, 5 + 4 * i), 0.02f)) return false;
                    return true;
                case T_Transform:
                {
                    // FF, type, 1 octet, position (3 x F32), rotation (4), echelle (3), etiquette, 7B.
                    if (a.Length < 47) return false;
                    float dx = F(a, 6) - F(b, 6), dy = F(a, 10) - F(b, 10), dz = F(a, 14) - F(b, 14);
                    if (dx * dx + dy * dy + dz * dz > 1f) return false;
                    float dot = 0f;
                    for (int i = 0; i < 4; i++) dot += F(a, 18 + 4 * i) * F(b, 18 + 4 * i);
                    if (Math.Abs(dot) < 0.996f) return false;   // ~10 degres
                    for (int i = 0; i < 3; i++) if (!Near(F(a, 34 + 4 * i), F(b, 34 + 4 * i), 0.01f)) return false;
                    for (int i = 46; i < a.Length; i++) if (a[i] != b[i]) return false;
                    return true;
                }
            }
            return false;
        }

        // (Nombres a point decimal, quelle que soit la langue du systeme : "pose (1285.5, 0.4, 1076.4)".)
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        static string Describe(byte[] v)
        {
            try
            {
                switch (TypeOf(v))
                {
                    case T_Int: if (v.Length >= 9) return BitConverter.ToInt32(v, 5).ToString(Inv); break;
                    case T_Float: if (v.Length >= 9) return F(v, 5).ToString("0.###", Inv); break;
                    case T_Bool: return v[5] != 0 ? "vrai" : "faux";
                    case T_String:
                        if (v[5] < 0x80 && 6 + v[5] <= v.Length) return "\"" + Encoding.UTF8.GetString(v, 6, v[5]) + "\"";
                        break;
                    case T_Vector3: if (v.Length >= 17) return "(" + F(v, 5).ToString("0.##", Inv) + ", " + F(v, 9).ToString("0.##", Inv) + ", " + F(v, 13).ToString("0.##", Inv) + ")"; break;
                    case T_Color: if (v.Length >= 21) return "rgba(" + F(v, 5).ToString("0.##", Inv) + ", " + F(v, 9).ToString("0.##", Inv) + ", " + F(v, 13).ToString("0.##", Inv) + ", " + F(v, 17).ToString("0.##", Inv) + ")"; break;
                    case T_Transform: if (v.Length >= 47) return "pose (" + F(v, 6).ToString("0.0", Inv) + ", " + F(v, 10).ToString("0.0", Inv) + ", " + F(v, 14).ToString("0.0", Inv) + ")"; break;
                }
            }
            catch (Exception) { }
            // Autre (listes, tables : leurs elements sont du texte) : ce qui est lisible, tronque.
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length && sb.Length < 80; i++)
            {
                char c = (char)v[i];
                if (c >= ' ' && c < 127) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            return "[" + v.Length + " o] " + sb.ToString().Trim();
        }

        // Etat propre a chaque joueur (sa faim, son argent, sa position...) : un ecart y est normal.
        static bool Personal(string tag)
        {
            return tag.StartsWith("Player", StringComparison.Ordinal) || tag.StartsWith("Hangover", StringComparison.Ordinal)
                || tag.StartsWith("BankAccount", StringComparison.Ordinal);
        }

        static void CompareSaves(int id, string name, int seq, Dictionary<string, byte[]> guest)
        {
            var onlyGuest = new List<string>(); var differ = new List<string>(); var onlyHost = new List<string>(); var personal = new List<string>();
            int same = 0;
            var files = new List<string>(hostSave.Keys);
            foreach (string f in guest.Keys) if (!hostSave.ContainsKey(f)) files.Add(f);
            files.Sort(StringComparer.Ordinal);
            foreach (string f in files)
            {
                byte[] hb, gb;
                hostSave.TryGetValue(f, out hb);
                guest.TryGetValue(f, out gb);
                if (hb == null) { onlyGuest.Add(f + " (fichier entier)"); continue; }
                if (gb == null) { onlyHost.Add(f + " (fichier entier)"); continue; }
                Dictionary<string, byte[]> h = ParseEs2(hb), g = ParseEs2(gb);
                if (h == null || g == null) { differ.Add(f + " : illisible chez " + (h == null ? "l'hote" : "l'invite")); continue; }
                var tags = new List<string>(h.Keys);
                foreach (string t in g.Keys) if (!h.ContainsKey(t)) tags.Add(t);
                tags.Sort(StringComparer.Ordinal);
                foreach (string t in tags)
                {
                    byte[] hv, gv;
                    h.TryGetValue(t, out hv);
                    g.TryGetValue(t, out gv);
                    if (hv != null && gv != null && Same(hv, gv)) { same++; continue; }
                    string line = f + " | " + t + " : " + (hv == null ? "absente chez l'hote" : "hote " + Describe(hv))
                                  + " / " + (gv == null ? "absente chez l'invite" : "invite " + Describe(gv));
                    if (Personal(t)) personal.Add(line);
                    else if (hv == null) onlyGuest.Add(line);
                    else if (gv == null) onlyHost.Add(line);
                    else differ.Add(line);
                }
            }
            string summary = "sauvegarde coop #" + seq + " comparee avec " + name + " : " + same + " cles identiques, "
                             + onlyGuest.Count + " SEULEMENT CHEZ L'INVITE (perdues a la prochaine session), " + differ.Count + " differentes, "
                             + onlyHost.Count + " seulement chez l'hote, " + personal.Count + " personnelles (normales)";
            var sb = new StringBuilder();
            sb.Append("Sauvegarde coop #").Append(seq).Append(" du ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
              .Append(" : hote ").Append(Session.Me != null ? Session.Me.Name : "?").Append(" / invite ").Append(name).Append('\n')
              .Append(summary).Append("\n\nLa sauvegarde de l'hote est la seule envoyee aux joueurs a la session suivante : ce que l'invite\n")
              .Append("est seul a avoir (ou a avoir autrement) y sera perdu. Tolerances : nombres 1 %, poses 1 m / 10 degres.\n");
            Section(sb, "SEULEMENT CHEZ L'INVITE", onlyGuest);
            Section(sb, "DIFFERENTES", differ);
            Section(sb, "SEULEMENT CHEZ L'HOTE (l'invite ne les avait pas)", onlyHost);
            Section(sb, "PERSONNELLES (chaque joueur les siennes : normal)", personal);
            string dir = Path.Combine(Log.DataDir, "dumps");
            string file = Path.Combine(dir, "ecarts-sauvegarde-joueur" + id + ".txt");
            try { Directory.CreateDirectory(dir); File.WriteAllText(file, sb.ToString()); }
            catch (Exception e) { Log.Warn("sauvegarde coop : " + file + " : " + e.Message); }
            Log.Info(summary + " -> " + file);
            int shown = 0;
            foreach (List<string> l in new[] { onlyGuest, differ, onlyHost })
                foreach (string s in l) { if (shown++ >= 10) break; Log.Info("  ecart sauvegarde : " + s); }
            int lost = onlyGuest.Count + differ.Count;
            if (lost > 0) Hud.Toast("Sauvegarde : " + lost + " ecart(s) avec " + name + " (dumps\\ecarts-sauvegarde-joueur" + id + ".txt)");
        }

        static void Section(StringBuilder sb, string title, List<string> lines)
        {
            sb.Append("\n===== ").Append(title).Append(" (").Append(lines.Count).Append(")\n");
            foreach (string l in lines) sb.Append("  ").Append(l).Append('\n');
        }

        // ------------------------------------------------------------ essais
        // [Test] Autotest=sauve | sauve-hote (compteur propre, garde d'un chargement de la partie a l'autre).
        //  sauve (invite) : a 40 s, clic sur les toilettes accrochees les plus proches, par leur automate ;
        //    attendu chez lui "sauvegarde demandee a l'hote", "ecrite chez l'hote", et a 65 s toujours
        //    en jeu, 1 seule entree. Hote : "demandee par", "ecrite ... personne n'a quitte la partie",
        //    la comparaison des sauvegardes, et a 70 s les invites (en jeu).
        //  sauve-hote (hote) : clic a 40 s -> sauvegarde en jeu, invites restes ; [Test] TestQuitter=1 : 2e clic
        //    3 s apres -> sauver et quitter, les invites suivent au menu ; avec [Test] Continuer=1 l'hote
        //    revient ("entree en jeu n2") et les invites aussi, sauvegarde neuve. Invites : chaque entree.
        static int testStep, testEntries;
        static float testLastT = float.MaxValue, testQuitAt = -1;

        public static void Test(string mode, float t)
        {
            if (mode != "sauve" && mode != "sauve-hote") return;
            bool host = Session.IsHost;
            if (t < testLastT) { testEntries++; Log.Info("autotest : " + mode + " : entree en jeu n" + testEntries + (host ? " (hote)" : " (invite)")); }
            testLastT = t;
            float now = Time.realtimeSinceStartup;
            if (mode == "sauve")
            {
                if (!host && testStep == 0 && t > 40f) { testStep = 1; Log.Info("autotest : sauve : " + TestPress()); }
                if (!host && testStep == 1 && t > 65f)
                {
                    testStep = 2;
                    Log.Info("autotest : sauve : invite toujours en jeu (" + Application.loadedLevelName + "), entrees en jeu " + testEntries
                             + ", sauvegardes de l'hote annoncees " + guestDone);
                }
                if (host && testStep == 0 && t > 70f) { testStep = 1; Log.Info("autotest : sauve : " + coopDone + " sauvegarde(s) coop, invites : " + Guests()); }
                return;
            }
            if (host)
            {
                if (testStep == 0 && t > 40f) { testStep = 1; Log.Info("autotest : sauve-hote : " + TestPress()); }
                if (testStep == 1 && coopDone > 0 && coopPhase == 0)
                {
                    testStep = 2;
                    Log.Info("autotest : sauve-hote : sauvegarde en jeu faite, invites : " + Guests());
                    if (Config.GetInt("Test", "TestQuitter", 0) != 0) testQuitAt = now + 3f;
                }
                if (testStep == 2 && testQuitAt > 0 && now >= testQuitAt) { testQuitAt = -1; testStep = 3; Log.Info("autotest : sauve-hote : 2e clic : " + TestPress()); }
                if (testStep == 3 && testEntries >= 2 && t > 45f) { testStep = 4; Log.Info("autotest : sauve-hote : hote revenu en jeu, invites : " + Guests()); }
            }
            else if (t > 30f && testStep < testEntries)
            {
                testStep = testEntries;
                Log.Info("autotest : sauve-hote : invite en jeu depuis 30 s (entree n" + testEntries + "), hote " + (Session.Host != null && Session.Host.Level == 1 ? "en jeu" : "absent/au menu"));
            }
        }

        static string Guests()
        {
            var sb = new StringBuilder();
            foreach (PlayerInfo p in Session.Players.Values)
                if (!p.Local) sb.Append(sb.Length > 0 ? ", " : "").Append(p.Name).Append(p.Level == 1 ? " en jeu" : " au menu");
            return sb.Length > 0 ? sb.ToString() : "aucun";
        }

        // Clic d'essai sur les toilettes accrochees les plus proches, par leur automate (jamais la souris) :
        // "Wait for click" avec ses MousePickEvent coupes (sans curseur dessus, ils renverraient aussitot a
        // "Wait"), puis CLICK : la vraie transition, et l'action de tete de "Wait".
        static string TestPress()
        {
            GameObject pl = GameObject.Find("PLAYER");
            Vector3 me = pl != null ? pl.transform.position : Vector3.zero;
            SaveButton best = null;
            float bd = float.MaxValue;
            foreach (SaveButton b in buttons)
            {
                if (b.F == null || !b.Hooked || !b.F.enabled || !b.F.gameObject.activeInHierarchy) continue;
                float d = (b.F.transform.position - me).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            if (best == null) return "aucunes toilettes accrochees et actives (" + buttons.Count + " relevees)";
            var off = new List<FsmStateAction>();
            foreach (FsmStateAction a in best.F.Fsm.GetState("Wait for click").Actions)
                if (a != null && a.Enabled && a.GetType().Name == "MousePickEvent") { a.Enabled = false; off.Add(a); }
            try { Game.SetState(best.F, "Wait for click"); best.F.SendEvent("CLICK"); }
            finally { foreach (FsmStateAction a in off) a.Enabled = true; }
            return "clic sur " + Recon.Path(best.F.transform) + " a " + Mathf.Sqrt(bd).ToString("F0") + " m -> " + best.F.ActiveStateName;
        }
    }
}
