using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace MWCoop.Net
{
    // Jeu en reseau par Steam ([Coop] Reseau=steam, choisi dans le lanceur). La version Steam du jeu embarque
    // Steamworks.NET (Assembly-CSharp-firstpass) et l'initialise elle-meme (SteamManager, RunCallbacks a chaque image) :
    // le mod s'en sert sous l'identifiant du jeu (4164420), sans Spacewar.
    //  - SteamLink : le lien de datagrammes du transport (Transport) par SteamNetworking (pair-a-pair, relais de Valve :
    //    ni port a ouvrir ni pare-feu, la cause des « delai depasse » en IP). Canal 0, envois non fiables : la fiabilite
    //    reste celle du transport (numeros, accuses, renvois), la meme qu'en UDP ;
    //  - salon : l'hote cree un salon Steam « amis » (8 places, donnee mwcoop = version reseau) ; on y invite par l'overlay
    //    (Maj+Tab, ou F10 > Inviter) et les amis peuvent aussi « Rejoindre la partie » depuis leur liste d'amis ;
    //  - invite : il rejoint le salon de l'invitation acceptee en jeu, sinon tout seul celui d'un ami qui heberge (amis
    //    dans My Winter Car avec un salon marque mwcoop) ; le proprietaire du salon est l'hote, a qui le transport se connecte.
    //  - avatars : avatar Steam moyen (64x64) de chaque joueur, en texture, pour les pseudos (Hud, F10).
    // Une invitation acceptee jeu ferme demarre le jeu d'origine sans le mod : il faut passer par MWCoop.exe (Rejoindre
    // via Steam), puis accepter l'invitation ou attendre que le salon de l'ami soit trouve.
    public static class SteamNet
    {
        // SteamManager du jeu (pas public) : sa propriete statique Initialized, lue par reflexion.
        static System.Reflection.PropertyInfo initProp;
        static bool initLooked;
        public static bool Ready
        {
            get
            {
                try
                {
                    if (!initLooked)
                    {
                        initLooked = true;
                        System.Type t = System.Type.GetType("SteamManager, Assembly-CSharp");
                        if (t != null) initProp = t.GetProperty("Initialized", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (initProp == null) Log.Warn("steam : SteamManager.Initialized introuvable");
                    }
                    return initProp != null && (bool)initProp.GetValue(null, null);
                }
                catch { return false; }
            }
        }

        public static ulong MyId { get { try { return Ready ? SteamUser.GetSteamID().m_SteamID : 0; } catch { return 0; } } }

        static bool callbacksMade;
        static Callback<LobbyEnter_t> cbEnter;
        static Callback<GameLobbyJoinRequested_t> cbJoinReq;
        static Callback<P2PSessionRequest_t> cbP2P;
        static Callback<P2PSessionConnectFail_t> cbP2PFail;
        static Callback<LobbyDataUpdate_t> cbData;
        static CallResult<LobbyCreated_t> crCreated;

        public static ulong Lobby;              // salon ou l'on est
        public static ulong LobbyOwner;         // son proprietaire (l'hote)
        public static ulong DirectHost;         // hote donne par le salon Steam du lanceur : pas de recherche chez les amis
        static bool hosting, creating, joining, testWritten;
        static float nextSearch, nextCreate, nextPics;
        static readonly HashSet<ulong> asked = new HashSet<ulong>();
        public static string Note = "";         // etat lisible (ecran d'attente)

        static void MakeCallbacks()
        {
            if (callbacksMade) return;
            callbacksMade = true;
            cbEnter = Callback<LobbyEnter_t>.Create(OnEnter);
            cbJoinReq = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            cbP2P = Callback<P2PSessionRequest_t>.Create(OnP2PRequest);
            cbP2PFail = Callback<P2PSessionConnectFail_t>.Create(r => Log.Warn("steam : session pair-a-pair avec " + r.m_steamIDRemote.m_SteamID + " en echec (" + r.m_eP2PSessionError + ")"));
            cbData = Callback<LobbyDataUpdate_t>.Create(r => { });
            crCreated = CallResult<LobbyCreated_t>.Create(OnCreated);
            try { SteamNetworking.AllowP2PPacketRelay(true); } catch { }
        }

        // ---------------------------------------------------------------- hote
        public static void Host()
        {
            hosting = true;
            MakeCallbacks();
            nextCreate = 0;
        }

        public static void Update()
        {
            if (!Ready) return;
            MakeCallbacks();
            float now = Time.realtimeSinceStartup;
            if (hosting && Lobby == 0 && !creating && now >= nextCreate)
            {
                creating = true;
                nextCreate = now + 10f;
                crCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 8));
                Note = "creation du salon Steam";
                Log.Info("steam : creation du salon");
            }
            // Essais ([Test] SteamSalonFichier=chemin) : l'hote y ecrit son salon, l'invite le rejoint (pas d'ami a soi-meme).
            string tf = Config.Get("Test", "SteamSalonFichier", "");
            if (tf.Length > 0)
            {
                if (hosting && Lobby != 0 && !testWritten) { testWritten = true; try { System.IO.File.WriteAllText(tf, Lobby.ToString()); } catch { } }
                ulong tl;
                if (!hosting && Lobby == 0 && !joining && System.IO.File.Exists(tf) && ulong.TryParse(System.IO.File.ReadAllText(tf).Trim(), out tl)) { Log.Info("essai : salon " + tl + " lu dans " + tf); Join(tl); }
            }
            if (!hosting && DirectHost == 0 && Lobby == 0 && !joining && now >= nextSearch) { nextSearch = now + 3f; SearchFriends(); }
            // Avatars des joueurs charges d'avance (pseudos au-dessus des joueurs, liste F10).
            if (now >= nextPics) { nextPics = now + 2f; foreach (PlayerInfo pi in Session.Players.Values) Avatar(pi.SteamId); }
        }

        static void OnCreated(LobbyCreated_t r, bool fail)
        {
            creating = false;
            if (fail || r.m_eResult != EResult.k_EResultOK) { Note = "salon Steam impossible (" + r.m_eResult + ")"; Log.Warn("steam : salon non cree (" + r.m_eResult + ")"); return; }
            Lobby = r.m_ulSteamIDLobby;
            LobbyOwner = MyId;
            var id = new CSteamID(Lobby);
            SteamMatchmaking.SetLobbyData(id, "mwcoop", Session.NetVersion.ToString());
            SteamMatchmaking.SetLobbyData(id, "hote", Session.Me != null ? Session.Me.Name : "");
            SteamMatchmaking.SetLobbyData(id, "version", Version.Text);
            SteamMatchmaking.SetLobbyJoinable(id, true);
            try { SteamFriends.SetRichPresence("status", "MWCoop : partie coop (" + Version.Text + ")"); } catch { }
            Note = "salon Steam pret";
            Log.Info("steam : salon " + Lobby + " cree, amis invitables (Maj+Tab ou F10)");
            Hud.Toast(Lang.T("Salon Steam prêt : invite tes amis (F10 ou Maj+Tab)", "Steam lobby ready: invite your friends (F10 or Shift+Tab)"));
        }

        public static void InviteDialog()
        {
            if (!Ready || Lobby == 0) { Hud.Toast(Lang.T("Salon Steam pas encore prêt", "Steam lobby not ready yet")); return; }
            SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(Lobby));
            Log.Info("steam : fenetre d'invitation ouverte");
        }

        // L'hote accepte les sessions des membres de son salon (et de tout ami : la version est verifiee a la connexion).
        static void OnP2PRequest(P2PSessionRequest_t r)
        {
            if (!Session.Active) return;
            SteamNetworking.AcceptP2PSessionWithUser(r.m_steamIDRemote);
            Log.Info("steam : session pair-a-pair acceptee avec " + Name(r.m_steamIDRemote.m_SteamID));
        }

        // ---------------------------------------------------------------- invite
        static void OnJoinRequested(GameLobbyJoinRequested_t r)
        {
            if (hosting) { Log.Info("steam : invitation ignoree (on heberge)"); return; }
            Log.Info("steam : invitation de " + Name(r.m_steamIDFriend.m_SteamID) + " acceptee, salon " + r.m_steamIDLobby.m_SteamID);
            Join(r.m_steamIDLobby.m_SteamID);
        }

        static void Join(ulong lobby)
        {
            if (Lobby == lobby || joining) return;
            if (Lobby != 0) SteamMatchmaking.LeaveLobby(new CSteamID(Lobby));
            Lobby = 0; LobbyOwner = 0;
            joining = true;
            Note = "entree dans le salon Steam";
            SteamMatchmaking.JoinLobby(new CSteamID(lobby));
        }

        static void OnEnter(LobbyEnter_t r)
        {
            joining = false;
            if (r.m_EChatRoomEnterResponse != 1) { Note = "salon Steam refuse (" + r.m_EChatRoomEnterResponse + ")"; Log.Warn("steam : entree refusee (" + r.m_EChatRoomEnterResponse + ")"); return; }
            Lobby = r.m_ulSteamIDLobby;
            var id = new CSteamID(Lobby);
            LobbyOwner = SteamMatchmaking.GetLobbyOwner(id).m_SteamID;
            if (hosting) return;
            Note = "salon de " + Name(LobbyOwner);
            Log.Info("steam : dans le salon " + Lobby + " de " + Name(LobbyOwner) + " (mwcoop " + SteamMatchmaking.GetLobbyData(id, "mwcoop") + ", " + SteamMatchmaking.GetLobbyData(id, "version") + ")");
        }

        // Amis dans My Winter Car dont le salon est marque mwcoop : on rejoint le premier (une invitation passe avant).
        static void SearchFriends()
        {
            try
            {
                uint app = SteamUtils.GetAppID().m_AppId;
                int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < n; i++)
                {
                    CSteamID f = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    FriendGameInfo_t g;
                    if (!SteamFriends.GetFriendGamePlayed(f, out g)) continue;
                    if ((uint)(g.m_gameID.m_GameID & 0xFFFFFF) != app || g.m_steamIDLobby.m_SteamID == 0) continue;
                    ulong lb = g.m_steamIDLobby.m_SteamID;
                    string tag = SteamMatchmaking.GetLobbyData(g.m_steamIDLobby, "mwcoop");
                    if (string.IsNullOrEmpty(tag)) { if (asked.Add(lb)) SteamMatchmaking.RequestLobbyData(g.m_steamIDLobby); continue; }
                    Log.Info("steam : " + Name(f.m_SteamID) + " heberge une partie MWCoop (salon " + lb + ", reseau " + tag + ")");
                    Join(lb);
                    return;
                }
                Note = "attente d'une invitation Steam";
            }
            catch (System.Exception e) { Log.Warn("steam : recherche des amis : " + e.Message); nextSearch = Time.realtimeSinceStartup + 15f; }
        }

        public static void Leave()
        {
            try { if (Ready && Lobby != 0) SteamMatchmaking.LeaveLobby(new CSteamID(Lobby)); } catch { }
            Lobby = LobbyOwner = 0;
            hosting = creating = joining = false;
        }

        public static string Name(ulong id)
        {
            try { string n = Ready ? SteamFriends.GetFriendPersonaName(new CSteamID(id)) : null; return string.IsNullOrEmpty(n) ? id.ToString() : n; }
            catch { return id.ToString(); }
        }

        // ---------------------------------------------------------------- avatars
        class Pic { public Texture2D Tex; public float Next; public int Tries; }
        static readonly Dictionary<ulong, Pic> pics = new Dictionary<ulong, Pic>();

        // Avatar moyen (64x64) ; null tant que Steam ne l'a pas (redemande toutes les 2 s, 20 fois).
        public static Texture2D Avatar(ulong id)
        {
            if (id == 0 || !Ready) return null;
            Pic p;
            if (!pics.TryGetValue(id, out p)) { p = new Pic(); pics[id] = p; try { SteamFriends.RequestUserInformation(new CSteamID(id), false); } catch { } }
            if (p.Tex != null || p.Tries >= 20 || Time.realtimeSinceStartup < p.Next) return p.Tex;
            p.Next = Time.realtimeSinceStartup + 2f; p.Tries++;
            try
            {
                int img = SteamFriends.GetMediumFriendAvatar(new CSteamID(id));
                uint w, h;
                if (img <= 0 || !SteamUtils.GetImageSize(img, out w, out h) || w == 0 || h == 0) return null;
                var raw = new byte[w * h * 4];
                if (!SteamUtils.GetImageRGBA(img, raw, raw.Length)) return null;
                // Steam : lignes de haut en bas ; texture Unity : de bas en haut.
                var flip = new byte[raw.Length];
                int row = (int)w * 4;
                for (int y = 0; y < h; y++) System.Buffer.BlockCopy(raw, y * row, flip, ((int)h - 1 - y) * row, row);
                // En rond (demande de JD, 07/10) : hors du cercle inscrit, transparent ; bord adouci sur un pixel.
                float cx = w / 2f, cy = h / 2f, rad = Mathf.Min(w, h) / 2f - 0.5f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float a = Mathf.Clamp01(rad - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                        int k = (y * (int)w + x) * 4 + 3;
                        flip[k] = (byte)(flip[k] * a);
                    }
                var t = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false);
                t.LoadRawTextureData(flip);
                t.Apply();
                t.wrapMode = TextureWrapMode.Clamp;
                p.Tex = t;
                Log.Info("steam : avatar de " + Name(id) + " charge (" + w + "x" + h + ")");
            }
            catch (System.Exception e) { p.Tries = 99; Log.Warn("steam : avatar de " + id + " : " + e.Message); }
            return p.Tex;
        }
    }

    // Lien de datagrammes par Steam (SteamNetworking, pair-a-pair). Adresse : identifiant Steam (ulong).
    // Deux canaux : les invites envoient sur 0 (lu par l'hote seul), l'hote sur 1 (lu par les invites). Sans cela, deux
    // jeux du meme compte (essais) se volaient leurs paquets dans la meme file.
    public class SteamLink : ILink
    {
        readonly int sendCh, recvCh;
        public SteamLink(bool host) { sendCh = host ? 1 : 0; recvCh = host ? 0 : 1; }

        public bool Send(object to, byte[] data, int len)
        {
            if (!SteamNet.Ready) return false;
            try { return SteamNetworking.SendP2PPacket(new CSteamID((ulong)to), data, (uint)len, EP2PSend.k_EP2PSendUnreliable, sendCh); }
            catch { return false; }
        }

        public bool Receive(byte[] buf, out int len, out object from)
        {
            len = 0; from = null;
            if (!SteamNet.Ready) return false;
            uint size;
            if (!SteamNetworking.IsP2PPacketAvailable(out size, recvCh)) return false;
            uint got; CSteamID who;
            if (size > buf.Length)
            {
                var big = new byte[size];
                SteamNetworking.ReadP2PPacket(big, size, out got, out who, recvCh);   // trop gros pour nous : lu et jete
                return true;
            }
            if (!SteamNetworking.ReadP2PPacket(buf, (uint)buf.Length, out got, out who, recvCh)) return false;
            len = (int)got;
            from = who.m_SteamID;
            return true;
        }

        public string KeyOf(object addr) { return addr != null ? "steam:" + addr : ""; }
        public void Forget(object addr) { try { if (addr is ulong) SteamNetworking.CloseP2PSessionWithUser(new CSteamID((ulong)addr)); } catch { } }
        public string Describe(object addr) { return addr is ulong ? SteamNet.Name((ulong)addr) : KeyOf(addr); }
        public void Close() { }
    }
}
