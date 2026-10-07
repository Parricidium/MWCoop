// ---------------------------------------------------------------- salon par Steam (inclus par launcher.cpp)
// Meme salon que par IP (pseudos, tenues, versions, PRET, partie, LANCER), sur un salon Steam "amis seulement" :
// l'hote invite ses amis depuis le lanceur (ses amis Steam en ligne), l'ami accepte dans Steam. Le lanceur
// s'initialise comme le jeu (AppID 4164420) : s'il est ouvert, Steam lui livre l'invitation acceptee
// (GameLobbyJoinRequested) ou recue (LobbyInvite, prise par REJOINDRE) ; s'il est ferme, Steam lance le jeu avec
// "+connect_lobby <salon>" -- avec l'option de lancement Steam  "<MWCoop.exe>" %command%  c'est le lanceur qui
// demarre et entre dans le salon (un 2e lanceur passe le salon au 1er, WM_COPYDATA).
// Donnees du salon (hote) : mwcoopsalon=1, version, hote, partie=continuer|nouvelle, go=<partie> (LANCER), ferme=1.
// Donnees de chaque membre : pseudo, tenue, version, pret=0|1 (l'hote : toujours pret).
// LANCER : go ; chaque invite quitte le salon et lance son jeu en invite Steam, HoteSteam=<hote> dans lancement.ini
// (le mod se connecte directement a l'hote en pair-a-pair) ; l'hote attend leur depart (3 s au plus) puis lance.
// Le salon du jeu (mod, donnee "mwcoop") n'est pas celui du lanceur : y entrer propose REJOINDRE EN JEU.
// steam_api64.dll du jeu chargee a la demande (le lanceur marche sans Steam). SDK ancien, celui du jeu : interfaces
// SteamFriends015, SteamMatchMaking009, SteamUser019, SteamUtils008 ; API "plate" SteamAPI_ISteamX_F(intptr_t, ...),
// CSteamID passe et rendu en uint64 (x64).

typedef uint64_t SteamId;
static const uint32_t kSteamApp = 4164420;

namespace sapi {
static HMODULE dll;
static intptr_t fr, mm, us, ut;
static bool (*Init)();
static void (*Shutdown)();
static void (*RunCallbacks)();
static void (*RegisterCallback)(void *, int);
static void (*UnregisterCallback)(void *);
static intptr_t (*Client)();
static int (*HUser)();
static int (*HPipe)();
static intptr_t (*GetFriends)(intptr_t, int, int, const char *);
static intptr_t (*GetMatchmaking)(intptr_t, int, int, const char *);
static intptr_t (*GetUser)(intptr_t, int, int, const char *);
static intptr_t (*GetUtils)(intptr_t, int, const char *);
static uint64_t (*CreateLobby)(intptr_t, int, int);
static uint64_t (*JoinLobby)(intptr_t, uint64_t);
static void (*LeaveLobby)(intptr_t, uint64_t);
static bool (*InviteUserToLobby)(intptr_t, uint64_t, uint64_t);
static int (*GetNumLobbyMembers)(intptr_t, uint64_t);
static uint64_t (*GetLobbyMemberByIndex)(intptr_t, uint64_t, int);
static const char *(*GetLobbyData)(intptr_t, uint64_t, const char *);
static bool (*SetLobbyData)(intptr_t, uint64_t, const char *, const char *);
static const char *(*GetLobbyMemberData)(intptr_t, uint64_t, uint64_t, const char *);
static void (*SetLobbyMemberData)(intptr_t, uint64_t, const char *, const char *);
static uint64_t (*GetLobbyOwner)(intptr_t, uint64_t);
static bool (*SetLobbyJoinable)(intptr_t, uint64_t, bool);
static int (*GetFriendCount)(intptr_t, int);
static uint64_t (*GetFriendByIndex)(intptr_t, int, int);
static int (*GetFriendPersonaState)(intptr_t, uint64_t);
static const char *(*GetFriendPersonaName)(intptr_t, uint64_t);
static bool (*GetFriendGamePlayed)(intptr_t, uint64_t, void *);
static int (*GetMediumFriendAvatar)(intptr_t, uint64_t);
static bool (*RequestUserInformation)(intptr_t, uint64_t, bool);
static bool (*SetRichPresence)(intptr_t, const char *, const char *);
static void (*ClearRichPresence)(intptr_t);
static bool (*GetImageSize)(intptr_t, int, uint32_t *, uint32_t *);
static bool (*GetImageRGBA)(intptr_t, int, uint8_t *, int);
static bool (*IsAPICallCompleted)(intptr_t, uint64_t, bool *);
static bool (*GetAPICallResult)(intptr_t, uint64_t, void *, int, int, bool *);
static uint64_t (*GetSteamID)(intptr_t);
// pair-a-pair (mods de l'hote, modsync.inc) : facultatif
static intptr_t nw;
static intptr_t (*GetNetworking)(intptr_t, int, int, const char *);
static bool (*SendP2PPacket)(intptr_t, uint64_t, const void *, uint32_t, int, int);
static bool (*IsP2PPacketAvailable)(intptr_t, uint32_t *, int);
static bool (*ReadP2PPacket)(intptr_t, void *, uint32_t, uint32_t *, uint64_t *, int);
static bool (*AcceptP2PSessionWithUser)(intptr_t, uint64_t);
}

template <class F> static bool SteamSym(F &f, const char *n) { f = (F)GetProcAddress(sapi::dll, n); if (!f) TestLog("steam : %s absente", n); return f != NULL; }
static bool SteamBind()
{
    using namespace sapi;
    bool ok = true;
#define STEAM_SYM(f, n) ok = SteamSym(f, n) && ok
    STEAM_SYM(Init, "SteamAPI_Init"); STEAM_SYM(Shutdown, "SteamAPI_Shutdown"); STEAM_SYM(RunCallbacks, "SteamAPI_RunCallbacks");
    STEAM_SYM(RegisterCallback, "SteamAPI_RegisterCallback"); STEAM_SYM(UnregisterCallback, "SteamAPI_UnregisterCallback");
    STEAM_SYM(Client, "SteamClient"); STEAM_SYM(HUser, "SteamAPI_GetHSteamUser"); STEAM_SYM(HPipe, "SteamAPI_GetHSteamPipe");
    STEAM_SYM(GetFriends, "SteamAPI_ISteamClient_GetISteamFriends"); STEAM_SYM(GetMatchmaking, "SteamAPI_ISteamClient_GetISteamMatchmaking");
    STEAM_SYM(GetUser, "SteamAPI_ISteamClient_GetISteamUser"); STEAM_SYM(GetUtils, "SteamAPI_ISteamClient_GetISteamUtils");
    STEAM_SYM(CreateLobby, "SteamAPI_ISteamMatchmaking_CreateLobby"); STEAM_SYM(JoinLobby, "SteamAPI_ISteamMatchmaking_JoinLobby");
    STEAM_SYM(LeaveLobby, "SteamAPI_ISteamMatchmaking_LeaveLobby"); STEAM_SYM(InviteUserToLobby, "SteamAPI_ISteamMatchmaking_InviteUserToLobby");
    STEAM_SYM(GetNumLobbyMembers, "SteamAPI_ISteamMatchmaking_GetNumLobbyMembers"); STEAM_SYM(GetLobbyMemberByIndex, "SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex");
    STEAM_SYM(GetLobbyData, "SteamAPI_ISteamMatchmaking_GetLobbyData"); STEAM_SYM(SetLobbyData, "SteamAPI_ISteamMatchmaking_SetLobbyData");
    STEAM_SYM(GetLobbyMemberData, "SteamAPI_ISteamMatchmaking_GetLobbyMemberData"); STEAM_SYM(SetLobbyMemberData, "SteamAPI_ISteamMatchmaking_SetLobbyMemberData");
    STEAM_SYM(GetLobbyOwner, "SteamAPI_ISteamMatchmaking_GetLobbyOwner"); STEAM_SYM(SetLobbyJoinable, "SteamAPI_ISteamMatchmaking_SetLobbyJoinable");
    STEAM_SYM(GetFriendCount, "SteamAPI_ISteamFriends_GetFriendCount"); STEAM_SYM(GetFriendByIndex, "SteamAPI_ISteamFriends_GetFriendByIndex");
    STEAM_SYM(GetFriendPersonaState, "SteamAPI_ISteamFriends_GetFriendPersonaState"); STEAM_SYM(GetFriendPersonaName, "SteamAPI_ISteamFriends_GetFriendPersonaName");
    STEAM_SYM(GetFriendGamePlayed, "SteamAPI_ISteamFriends_GetFriendGamePlayed"); STEAM_SYM(GetMediumFriendAvatar, "SteamAPI_ISteamFriends_GetMediumFriendAvatar");
    STEAM_SYM(RequestUserInformation, "SteamAPI_ISteamFriends_RequestUserInformation"); STEAM_SYM(SetRichPresence, "SteamAPI_ISteamFriends_SetRichPresence");
    STEAM_SYM(ClearRichPresence, "SteamAPI_ISteamFriends_ClearRichPresence");
    STEAM_SYM(GetImageSize, "SteamAPI_ISteamUtils_GetImageSize"); STEAM_SYM(GetImageRGBA, "SteamAPI_ISteamUtils_GetImageRGBA");
    STEAM_SYM(IsAPICallCompleted, "SteamAPI_ISteamUtils_IsAPICallCompleted"); STEAM_SYM(GetAPICallResult, "SteamAPI_ISteamUtils_GetAPICallResult");
    STEAM_SYM(GetSteamID, "SteamAPI_ISteamUser_GetSteamID");
#undef STEAM_SYM
    bool p2p = SteamSym(GetNetworking, "SteamAPI_ISteamClient_GetISteamNetworking");   // (sans : pas de mods par Steam)
    p2p = SteamSym(SendP2PPacket, "SteamAPI_ISteamNetworking_SendP2PPacket") && p2p;
    p2p = SteamSym(IsP2PPacketAvailable, "SteamAPI_ISteamNetworking_IsP2PPacketAvailable") && p2p;
    p2p = SteamSym(ReadP2PPacket, "SteamAPI_ISteamNetworking_ReadP2PPacket") && p2p;
    p2p = SteamSym(AcceptP2PSessionWithUser, "SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser") && p2p;
    if (!p2p) GetNetworking = NULL;
    return ok;
}

// Rappels de Steam : meme disposition que CCallbackBase du SDK (pointeur de table virtuelle, m_nCallbackFlags,
// m_iCallback ; deux Run surcharges puis GetCallbackSizeBytes, dans cet ordre : MSVC les range comme steam_api).
// Appeles par SteamAPI_RunCallbacks, donc dans le fil de la fenetre.
class SteamCbBase {
public:
    virtual void Run(void *p) = 0;
    virtual void Run(void *p, bool ioFailure, uint64_t call) = 0;
    virtual int GetCallbackSizeBytes() = 0;
    uint8_t m_flags = 0;
    int m_id = 0;
};
struct SteamCb : SteamCbBase {
    int size;
    void (*fn)(const void *);
    SteamCb(int s, void (*f)(const void *)) : size(s), fn(f) {}
    void Run(void *p) override { fn(p); }
    void Run(void *p, bool, uint64_t) override { fn(p); }
    int GetCallbackSizeBytes() override { return size; }
};

#pragma pack(push, 8)
struct SLobbyCreated { int result; SteamId lobby; };                                    // LobbyCreated_t (513)
struct SLobbyEnter { SteamId lobby; uint32_t perms; bool locked; uint32_t response; };   // LobbyEnter_t (504)
struct SJoinRequested { SteamId lobby, from; };                                         // GameLobbyJoinRequested_t (333)
struct SLobbyInvite { SteamId user, lobby; uint64_t game; };                            // LobbyInvite_t (503)
struct SGameInfo { uint64_t game; uint32_t ip; uint16_t port, query; SteamId lobby; };   // FriendGameInfo_t
#pragma pack(pop)

static int g_steamUp;                 // 1 : Steam pret dans le lanceur
static std::wstring g_steamWhy;       // sinon, pourquoi (statut)
static SteamId g_steamMe;
static SteamId g_sLobby;              // salon Steam ou l'on est
// (g_sHost, g_sHostName : hote du salon rejoint, gardes apres le salon pour lancement.ini -- declares plus haut)
static uint64_t g_sCall;              // creation (513) ou entree (504) en cours
static int g_sCallKind;
static bool g_sCallStale;             // ... abandonnee entre-temps : salon quitte des qu'il arrive
static SteamId g_sInvite;             // invitation recue lanceur ouvert : REJOINDRE la prend
static std::wstring g_sInviteName;    // ... et qui l'envoie
static SteamId g_sConnect;            // salon a rejoindre des que possible (+connect_lobby, invitation acceptee)
static std::map<SteamId, int> g_sIds; // numero de chaque membre dans le salon (couleur, sons)
static int g_sNextId;
static bool g_sFriendsView;           // hote : liste des amis a inviter (a la place des joueurs)
struct SteamFriend { SteamId id; std::wstring name; int state; bool mwc; };
static std::vector<SteamFriend> g_sFriends;
static int g_sOffline;
static std::map<SteamId, DWORD> g_sInvited;
static std::map<SteamId, Bitmap *> g_steamAv;
static std::map<SteamId, DWORD> g_steamAvTry;
static std::wstring g_testSteamFile;  // /teststeam : fichier du numero de salon (l'hote l'ecrit, l'invite le lit)

static void OnSteamJoinRequested(const void *p);
static void OnSteamInvite(const void *p);
static SteamCb g_cbJoin(sizeof(SJoinRequested), OnSteamJoinRequested), g_cbInvite(sizeof(SLobbyInvite), OnSteamInvite);

static bool SteamUp()
{
    if (g_steamUp == 1) return true;
    if (g_gameDir.empty()) { g_steamWhy = T(L"My Winter Car introuvable", L"My Winter Car not found"); return false; }
    if (!sapi::dll) {
        std::wstring p = g_gameDir + L"steam_api64.dll";
        sapi::dll = LoadLibraryExW(p.c_str(), NULL, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);   // (chemin complet ; ses dependances : son dossier, System32)
        if (!sapi::dll) {
            g_steamWhy = T(L"steam_api64.dll introuvable dans le dossier du jeu", L"steam_api64.dll not found in the game folder");
            TestLog("steam : %s introuvable (erreur %lu)", Narrow(p, CP_UTF8).c_str(), GetLastError());
            return false;
        }
        if (!SteamBind()) {
            FreeLibrary(sapi::dll);
            sapi::dll = NULL;
            g_steamWhy = T(L"steam_api64.dll du jeu inattendue", L"Unexpected steam_api64.dll in the game");
            return false;
        }
    }
    // (comme steam_appid.txt pour le jeu : le lanceur passe pour My Winter Car aupres de Steam)
    SetEnvironmentVariableW(L"SteamAppId", L"4164420");
    SetEnvironmentVariableW(L"SteamGameId", L"4164420");
    if (!sapi::Init()) {
        g_steamWhy = T(L"Steam n'est pas ouvert (ou pas connecté)", L"Steam is not running (or not logged in)");
        TestLog("steam : SteamAPI_Init a echoue");
        return false;
    }
    intptr_t c = sapi::Client();
    int u = sapi::HUser(), pipe = sapi::HPipe();
    sapi::fr = c ? sapi::GetFriends(c, u, pipe, "SteamFriends015") : 0;
    sapi::mm = c ? sapi::GetMatchmaking(c, u, pipe, "SteamMatchMaking009") : 0;
    sapi::us = c ? sapi::GetUser(c, u, pipe, "SteamUser019") : 0;
    sapi::ut = c ? sapi::GetUtils(c, pipe, "SteamUtils008") : 0;
    sapi::nw = c && sapi::GetNetworking ? sapi::GetNetworking(c, u, pipe, "SteamNetworking005") : 0;
    if (!sapi::nw) TestLog("steam : pas de pair-a-pair (SteamNetworking005) : pas de mods de l'hote par Steam");
    if (!sapi::fr || !sapi::mm || !sapi::us || !sapi::ut) {
        sapi::Shutdown();
        g_steamWhy = T(L"Interfaces Steam introuvables", L"Steam interfaces not found");
        TestLog("steam : interfaces introuvables (%d %d %d %d)", (int)!!sapi::fr, (int)!!sapi::mm, (int)!!sapi::us, (int)!!sapi::ut);
        return false;
    }
    sapi::RegisterCallback(&g_cbJoin, 333);
    sapi::RegisterCallback(&g_cbInvite, 503);
    g_steamMe = sapi::GetSteamID(sapi::us);
    g_steamUp = 1;
    g_steamWhy.clear();
    TestLog("steam : pret (compte %llu)", (unsigned long long)g_steamMe);
    return true;
}

static void SteamDown()
{
    if (g_steamUp != 1) return;
    if (g_sLobby) { sapi::LeaveLobby(sapi::mm, g_sLobby); g_sLobby = 0; }
    sapi::ClearRichPresence(sapi::fr);
    sapi::UnregisterCallback(&g_cbJoin);
    sapi::UnregisterCallback(&g_cbInvite);
    sapi::Shutdown();
    g_steamUp = 0;
    g_sCall = 0;
    TestLog("steam : arrete");
}

// Canaux pair-a-pair des mods : 5. Essai avec un seul compte Steam (/testsalon ... /steam : hote et invite ont le meme
// SteamID, un paquet vers soi-meme arrive au premier qui lit) : l'hote lit le 5 et repond sur le 6, l'invite lit le 6.
static int P2PRecvChannel() { return !g_testSteamFile.empty() && g_lobby != LB_HOST ? 6 : 5; }
static int P2PSendChannel() { return !g_testSteamFile.empty() && g_lobby == LB_HOST ? 6 : 5; }
static void SteamSendP2P(SteamId to, const std::string &d)
{
    if (g_steamUp != 1 || !sapi::nw || !to || d.empty()) return;
    if (!sapi::SendP2PPacket(sapi::nw, to, d.data(), (uint32_t)d.size(), 2 /* fiable */, P2PSendChannel())) TestLog("steam : envoi pair-a-pair impossible (%u octets)", (unsigned)d.size());
}

static void SteamAvFree() { for (auto &a : g_steamAv) delete a.second; g_steamAv.clear(); }

// Avatar Steam (64 x 64) d'un joueur, NULL tant qu'il n'est pas arrive (redemande chaque seconde).
static Bitmap *SteamAvatar(SteamId id)
{
    auto it = g_steamAv.find(id);
    if (it != g_steamAv.end()) return it->second;
    if (g_steamUp != 1 || !id) return NULL;
    DWORD now = GetTickCount() | 1;
    DWORD &t = g_steamAvTry[id];
    if (t && now - t < 1000) return NULL;
    if (!t) sapi::RequestUserInformation(sapi::fr, id, false);   // (pas un ami : Steam va chercher son profil)
    t = now;
    int h = sapi::GetMediumFriendAvatar(sapi::fr, id);
    uint32_t w = 0, hh = 0;
    if (h <= 0 || !sapi::GetImageSize(sapi::ut, h, &w, &hh) || !w || !hh || w > 256 || hh > 256) return NULL;
    std::vector<uint8_t> px((size_t)w * hh * 4);
    if (!sapi::GetImageRGBA(sapi::ut, h, px.data(), (int)px.size())) return NULL;
    Bitmap *b = new Bitmap((INT)w, (INT)hh, PixelFormat32bppARGB);
    BitmapData bd;
    Rect r(0, 0, (INT)w, (INT)hh);
    if (b->LockBits(&r, ImageLockModeWrite, PixelFormat32bppARGB, &bd) == Ok) {
        for (uint32_t y = 0; y < hh; y++) {
            uint8_t *row = (uint8_t *)bd.Scan0 + (size_t)y * bd.Stride;
            const uint8_t *src = &px[(size_t)y * w * 4];
            for (uint32_t x = 0; x < w; x++) { row[x * 4] = src[x * 4 + 2]; row[x * 4 + 1] = src[x * 4 + 1]; row[x * 4 + 2] = src[x * 4]; row[x * 4 + 3] = src[x * 4 + 3]; }
        }
        b->UnlockBits(&bd);
    }
    g_steamAv[id] = b;
    return b;
}
// Avatar dans un carre arrondi (bord clair) ; false s'il n'est pas (encore) la.
static bool DrawSteamAvatar(Graphics &g, RectF r, SteamId id)
{
    Bitmap *b = SteamAvatar(id);
    if (!b) return false;
    GraphicsPath p;
    p.AddEllipse(r);   // en rond, comme en jeu (demande de JD, 07/10)
    Region old;
    g.GetClip(&old);   // (la liste a deja sa propre zone)
    g.SetClip(&p, CombineModeIntersect);
    g.DrawImage(b, r);
    g.SetClip(&old);
    Pen pen(Color(230, 255, 255, 255), 1.4f);
    g.DrawPath(&pen, &p);
    return true;
}

static std::wstring SteamName(SteamId id)
{
    const char *n = g_steamUp == 1 && id ? sapi::GetFriendPersonaName(sapi::fr, id) : NULL;
    return n && *n ? Widen(n, CP_UTF8) : std::wstring(L"?");
}
static std::string SLobbyData(const char *k)
{
    const char *v = g_steamUp == 1 && g_sLobby ? sapi::GetLobbyData(sapi::mm, g_sLobby, k) : NULL;
    return v ? v : "";
}
static std::string SMemberData(SteamId u, const char *k)
{
    const char *v = g_steamUp == 1 && g_sLobby ? sapi::GetLobbyMemberData(sapi::mm, g_sLobby, u, k) : NULL;
    return v ? v : "";
}
static void SSetMember(const char *k, const std::string &v) { if (g_steamUp == 1 && g_sLobby) sapi::SetLobbyMemberData(sapi::mm, g_sLobby, k, v.c_str()); }
static void SSetLobby(const char *k, const std::string &v) { if (g_steamUp == 1 && g_sLobby) sapi::SetLobbyData(sapi::mm, g_sLobby, k, v.c_str()); }
static std::string SIdStr(SteamId id) { char b[32]; sprintf_s(b, "%llu", (unsigned long long)id); return b; }

// Le salon : liste des joueurs refaite d'apres les membres et leurs donnees.
static void SteamLobbySync();

static void SteamLobbyHost()
{
    if (!SteamUp()) { SetStatus(K_ERR, L"%s", g_steamWhy.c_str()); return; }
    if (g_sCall) { SetStatus(K_WARN, T(L"Un instant…", L"One moment…")); return; }
    SavePlayer();
    g_lobbyGen++;
    EnterCriticalSection(&g_lcs);
    g_peers.clear();
    g_peers.push_back({ 0, MyName(), MySkin(), MyVersion(), true, 0, UDP_NA, 0, g_steamMe });
    LeaveCriticalSection(&g_lcs);
    g_sIds.clear();
    g_sIds[g_steamMe] = 0;
    g_sNextId = 1;
    g_sHost = g_steamMe;
    SyncOfferReset();
    g_sInvited.clear();
    g_mySkinSent = MySkin();
    g_partie = PARTIE_CONTINUER;
    g_goSent = false;
    g_myId = 0;
    g_joinFallback = false;
    g_lobbySteam = true;
    g_lobby = LB_CONNECTING;
    g_tab = TAB_LOBBY;
    g_scroll[TAB_LOBBY] = 0;
    g_focus = -1;
    g_sCall = sapi::CreateLobby(sapi::mm, 1 /* amis seulement */, kLobbyMax);
    g_sCallKind = 513;
    g_sCallStale = false;
    SetStatus(K_NORMAL, T(L"Création du salon Steam…", L"Creating the Steam lobby…"));
    TestLog("salon steam : creation (version %s, %s, %s)", MyVersion().c_str(), MyName().c_str(), MySkin().c_str());
}

static void SteamLobbyJoin(SteamId lobby)
{
    if (!lobby) return;
    if (!SteamUp()) { SetStatus(K_ERR, L"%s", g_steamWhy.c_str()); return; }
    if (g_lobby != LB_NONE) {
        if (g_lobbySteam && g_sLobby == lobby) return;
        LobbyClose();
    }
    if (!g_steamNet) {   // (invitation acceptee lanceur en mode IP : on passe en Steam)
        g_steamNet = true;
        WritePrivateProfileStringW(L"Lanceur", L"Reseau", L"steam", g_iniLauncher.c_str());
    }
    SavePlayer();
    SyncReset();
    g_lobbyGen++;
    g_meReady = false;
    g_joinFallback = false;
    g_myId = -1;
    EnterCriticalSection(&g_lcs);
    g_peers.clear();
    LeaveCriticalSection(&g_lcs);
    g_sIds.clear();
    g_sNextId = 1;
    g_sHost = 0;
    g_partie = PARTIE_CONTINUER;
    g_mySkinSent = MySkin();
    g_lobbySteam = true;
    g_lobby = LB_CONNECTING;
    g_tab = TAB_LOBBY;
    g_scroll[TAB_LOBBY] = 0;
    g_focus = -1;
    g_sCall = sapi::JoinLobby(sapi::mm, lobby);
    g_sCallKind = 504;
    g_sCallStale = false;
    SetStatus(K_NORMAL, T(L"Entrée dans le salon Steam…", L"Joining the Steam lobby…"));
    TestLog("salon steam : entree dans %llu (version %s, %s, %s)", (unsigned long long)lobby, MyVersion().c_str(), MyName().c_str(), MySkin().c_str());
}

static void SteamPublishMe(bool host)
{
    SSetMember("pseudo", MyName());
    SSetMember("tenue", MySkin());
    SSetMember("version", MyVersion());
    if (!host) SSetMember("pret", g_meReady ? "1" : "0");
}
static void SteamPresence()
{
    if (g_steamUp != 1) return;
    int n = g_sLobby ? sapi::GetNumLobbyMembers(sapi::mm, g_sLobby) : 0;
    char b[96];
    sprintf_s(b, "MWCoop : salon (%d/%d)", n, kLobbyMax);
    sapi::SetRichPresence(sapi::fr, "status", g_sLobby ? b : "MWCoop");
}

static void SteamCreated(bool ok, int result, SteamId lobby)
{
    if (g_sCallStale || !g_lobbySteam || g_lobby != LB_CONNECTING) {   // (salon ferme pendant la creation)
        if (ok && lobby) sapi::LeaveLobby(sapi::mm, lobby);
        return;
    }
    if (!ok || result != 1 || !lobby) {
        LobbyClose();
        SetStatus(K_ERR, T(L"Steam n'a pas créé le salon (code %d)", L"Steam could not create the lobby (code %d)"), ok ? result : -1);
        TestLog("salon steam : creation refusee (ok=%d code %d)", (int)ok, result);
        return;
    }
    g_sLobby = lobby;
    SSetLobby("mwcoopsalon", "1");
    SSetLobby("version", MyVersion());
    SSetLobby("hote", MyName());
    SSetLobby("partie", PartieName(g_partie));
    sapi::SetLobbyJoinable(sapi::mm, lobby, true);
    SteamPublishMe(true);
    SteamPresence();
    g_lobby = LB_HOST;
    g_sFriendsView = g_testSalon.empty();   // (tout de suite : qui inviter)
    g_scroll[TAB_LOBBY] = 0;
    SetStatus(K_OK, T(L"Salon Steam ouvert : invite tes amis", L"Steam lobby open: invite your friends"));
    TestLog("salon steam : %llu cree", (unsigned long long)lobby);
    if (!g_testSteamFile.empty()) {
        FILE *f = _wfopen(g_testSteamFile.c_str(), L"wb");
        if (f) { fprintf(f, "%llu", (unsigned long long)lobby); fclose(f); }
    }
}

static void SteamEntered(bool ok, uint32_t response, SteamId lobby)
{
    if (g_sCallStale || !g_lobbySteam || g_lobby != LB_CONNECTING) {
        if (ok && response == 1 && lobby) sapi::LeaveLobby(sapi::mm, lobby);
        return;
    }
    if (!ok || response != 1) {
        LobbyClose();
        // EChatRoomEnterResponse : 2 inexistant, 3 pas autorise, 4 plein, 6 banni, 7 limite, 8 verrouille...
        if (ok && response == 4) SetStatus(K_ERR, T(L"Salon complet (%d joueurs)", L"Lobby is full (%d players)"), kLobbyMax);
        else if (ok && (response == 2 || response == 3 || response == 8)) SetStatus(K_WARN, T(L"Ce salon n'existe plus (ou il est fermé)", L"This lobby no longer exists (or is closed)"));
        else SetStatus(K_ERR, T(L"Impossible d'entrer dans le salon Steam (code %u)", L"Could not join the Steam lobby (code %u)"), ok ? response : 0u);
        TestLog("salon steam : entree refusee (ok=%d reponse %u)", (int)ok, response);
        return;
    }
    g_sLobby = lobby;
    SteamId owner = sapi::GetLobbyOwner(sapi::mm, lobby);
    std::wstring hostName = SteamName(owner);
    std::string salon = SLobbyData("mwcoopsalon"), inGame = SLobbyData("mwcoop"), ver = SLobbyData("version"), go = SLobbyData("go");
    TestLog("salon steam : dans %llu (hote %llu, salon=%s jeu=%s version=%s go=%s)", (unsigned long long)lobby, (unsigned long long)owner,
            salon.c_str(), inGame.c_str(), ver.c_str(), go.c_str());
    if (salon != "1" || !go.empty()) {   // salon du jeu (l'hote joue deja) ou partie deja lancee : rejoindre en jeu
        bool game = !inGame.empty() || !go.empty();
        LobbyClose();
        if (game) {
            g_sHost = owner;
            g_sHostName = hostName;
            g_joinFallback = true;
            SetStatus(K_WARN, T(L"%s joue déjà : « Rejoindre en jeu »", L"%s is already playing: \"Join in game\""), hostName.c_str());
        } else SetStatus(K_ERR, T(L"Ce salon Steam n'est pas un salon MWCoop", L"This Steam lobby is not an MWCoop lobby"));
        return;
    }
    if (ver != MyVersion()) {
        LobbyClose();
        std::wstring mine = Widen(MyVersion()), his = Widen(ver);
        SetStatus(K_ERR, T(L"Version différente (toi %s, hôte %s) : mettez à jour, relancez MWCoop.exe", L"Different version (you %s, host %s): update, restart MWCoop.exe"), mine.c_str(), his.c_str());
        return;
    }
    g_sHost = owner;
    g_sHostName = hostName;
    g_sIds[owner] = 0;
    g_partie = SLobbyData("partie") == "nouvelle" ? PARTIE_NOUVELLE : PARTIE_CONTINUER;
    SteamPublishMe(false);
    SteamPresence();
    g_lobby = LB_GUEST;
    SetStatus(K_OK, T(L"Dans le salon Steam de %s", L"In %s's Steam lobby"), hostName.c_str());
    SteamLobbySync();
}

// Invite : l'hote a lance (go). Il quitte le salon et lance son jeu un peu apres (l'hote demarre le premier).
static void SteamGo(const std::string &go)
{
    int partie = go == "nouvelle" ? PARTIE_NOUVELLE : PARTIE_CONTINUER;
    TestLog("salon steam : GO recu (%s), hote %llu", go.c_str(), (unsigned long long)g_sHost);
    g_syncKeep = true;
    LobbyClose();
    g_partie = partie;
    g_goWait = true;
    g_launchWarn.clear();
    SetStatus(K_OK, T(L"L'hôte lance la partie… ton jeu démarre dans un instant", L"The host is starting the game… yours starts in a moment"));
    SetTimer(g_wnd, 4, 2500, NULL);
}

static void SteamLobbySync()
{
    SteamId lobby = g_sLobby;
    bool host = g_lobby == LB_HOST;
    if (!host) {   // (avant tout : l'hote qui lance quitte le salon juste apres)
        std::string go = SLobbyData("go");
        if (!go.empty()) { SteamGo(go); return; }
    }
    int n = sapi::GetNumLobbyMembers(sapi::mm, lobby);
    SteamId owner = sapi::GetLobbyOwner(sapi::mm, lobby);
    std::vector<LobbyPeer> v;
    bool hostHere = false;
    for (int i = 0; i < n && i < 32; i++) {
        SteamId u = sapi::GetLobbyMemberByIndex(sapi::mm, lobby, i);
        if (!u) continue;
        hostHere |= u == g_sHost;
        auto it = g_sIds.find(u);
        int id = it != g_sIds.end() ? it->second : (g_sIds[u] = u == g_sHost ? 0 : g_sNextId++);
        LobbyPeer p = {};
        p.id = id;
        p.sid = u;
        p.name = SMemberData(u, "pseudo");
        if (p.name.empty()) p.name = Narrow(SteamName(u), CP_UTF8);
        p.skin = SMemberData(u, "tenue");
        if (p.skin.empty()) p.skin = "char_shirt21";
        p.ver = SMemberData(u, "version");
        p.ready = id == 0 || SMemberData(u, "pret") == "1";
        p.udp = UDP_NA;
        v.push_back(p);
    }
    std::sort(v.begin(), v.end(), [](const LobbyPeer &a, const LobbyPeer &b) { return a.id < b.id; });
    EnterCriticalSection(&g_lcs);
    g_peers = v;
    LeaveCriticalSection(&g_lcs);
    auto me = g_sIds.find(g_steamMe);
    g_myId = me != g_sIds.end() ? me->second : -1;
    if (host) {
        static int lastN;
        if (n != lastN) { lastN = n; SteamPresence(); }
        return;
    }
    if (!hostHere || owner != g_sHost || SLobbyData("ferme") == "1") {
        TestLog("salon steam : hote parti (present=%d proprietaire %llu ferme=%s)", (int)hostHere, (unsigned long long)owner, SLobbyData("ferme").c_str());
        LobbyClose();
        SetStatus(K_WARN, T(L"L'hôte a fermé le salon", L"The host closed the lobby"));
        return;
    }
    g_partie = SLobbyData("partie") == "nouvelle" ? PARTIE_NOUVELLE : PARTIE_CONTINUER;
}

// LobbyClose : quitter le salon Steam (l'hote le dit d'abord aux invites).
static void SteamLobbyLeave(bool host)
{
    if (g_sCall) g_sCallStale = true;
    if (g_steamUp == 1 && g_sLobby) {
        if (host && !g_goSent) SSetLobby("ferme", "1");
        sapi::LeaveLobby(sapi::mm, g_sLobby);
        TestLog("salon steam : quitte (%llu)", (unsigned long long)g_sLobby);
    }
    g_sLobby = 0;
    g_sFriendsView = false;
    g_lobbySteam = false;
    SteamPresence();
}

// LANCER (hote) : go dans le salon, puis attendre que les invites l'aient vu et soient partis (3 s au plus).
static void SteamHostStart(int partie)
{
    SSetLobby("partie", PartieName(partie));
    SSetLobby("go", PartieName(partie));
    sapi::SetLobbyJoinable(sapi::mm, g_sLobby, false);
    DWORD t0 = GetTickCount();
    int n = 0;
    while (GetTickCount() - t0 < 3000) {
        sapi::RunCallbacks();
        n = sapi::GetNumLobbyMembers(sapi::mm, g_sLobby);
        if (n <= 1) break;
        Sleep(50);
    }
    TestLog("salon steam : GO, %d membre(s) encore la apres %lu ms", n, GetTickCount() - t0);
}

// Hote : amis Steam en ligne (ceux qui jouent a My Winter Car d'abord).
static void SteamFriendsRefresh()
{
    if (g_steamUp != 1) return;
    g_sFriends.clear();
    g_sOffline = 0;
    int n = sapi::GetFriendCount(sapi::fr, 4 /* k_EFriendFlagImmediate */);
    for (int i = 0; i < n; i++) {
        SteamId id = sapi::GetFriendByIndex(sapi::fr, i, 4);
        int st = sapi::GetFriendPersonaState(sapi::fr, id);
        if (st == 0) { g_sOffline++; continue; }
        SGameInfo gi = {};
        bool mwc = sapi::GetFriendGamePlayed(sapi::fr, id, &gi) && (gi.game & 0xFFFFFF) == kSteamApp;
        g_sFriends.push_back({ id, SteamName(id), st, mwc });
    }
    std::sort(g_sFriends.begin(), g_sFriends.end(), [](const SteamFriend &a, const SteamFriend &b) {
        if (a.mwc != b.mwc) return a.mwc;
        return _wcsicmp(a.name.c_str(), b.name.c_str()) < 0;
    });
}
static void SteamShowFriends(bool on)
{
    g_sFriendsView = on;
    g_scroll[TAB_LOBBY] = 0;
    g_lobbyHot = -1;
    if (on) SteamFriendsRefresh();
}
static bool SteamInLobby(SteamId id)
{
    EnterCriticalSection(&g_lcs);
    bool in = false;
    for (auto &p : g_peers) in |= p.sid == id;
    LeaveCriticalSection(&g_lcs);
    return in;
}
static void SteamInviteFriend(int i)
{
    if (i < 0 || i >= (int)g_sFriends.size() || g_steamUp != 1 || !g_sLobby || g_lobby != LB_HOST) return;
    const SteamFriend &f = g_sFriends[i];
    if (SteamInLobby(f.id)) return;
    bool ok = sapi::InviteUserToLobby(sapi::mm, g_sLobby, f.id);
    if (ok) {
        g_sInvited[f.id] = GetTickCount();
        SetStatus(K_OK, T(L"Invitation envoyée à %s", L"Invite sent to %s"), f.name.c_str());
    } else SetStatus(K_ERR, T(L"Steam a refusé l'invitation", L"Steam refused the invite"));
    TestLog("salon steam : invitation (ok=%d)", (int)ok);
}

// REJOINDRE (Steam) : l'invitation recue, sinon le salon MWCoop d'un ami.
static void SteamJoinClick()
{
    if (!SteamUp()) { SetStatus(K_ERR, L"%s", g_steamWhy.c_str()); return; }
    if (g_sInvite) { SteamId l = g_sInvite; g_sInvite = 0; SteamLobbyJoin(l); return; }
    std::vector<std::pair<SteamId, std::wstring>> found;
    int n = sapi::GetFriendCount(sapi::fr, 4);
    for (int i = 0; i < n; i++) {
        SteamId id = sapi::GetFriendByIndex(sapi::fr, i, 4);
        SGameInfo gi = {};
        if (sapi::GetFriendGamePlayed(sapi::fr, id, &gi) && (gi.game & 0xFFFFFF) == kSteamApp && gi.lobby) found.push_back({ gi.lobby, SteamName(id) });
    }
    TestLog("salon steam : %d salon(s) d'amis", (int)found.size());
    if (found.empty()) {
        SetStatus(K_WARN, T(L"Aucun salon d'ami : demande à l'hôte de t'inviter", L"No friend lobby: ask the host to invite you"));
        return;
    }
    SteamLobbyJoin(found[0].first);
}

static void OnSteamJoinRequested(const void *p)
{
    const SJoinRequested *r = (const SJoinRequested *)p;
    TestLog("salon steam : invitation acceptee dans Steam (salon %llu)", (unsigned long long)r->lobby);
    g_sConnect = r->lobby;   // (SteamTick : des que le lanceur est libre)
    if (IsIconic(g_wnd)) ShowWindow(g_wnd, SW_RESTORE);
    SetForegroundWindow(g_wnd);
}
static void OnSteamInvite(const void *p)
{
    const SLobbyInvite *r = (const SLobbyInvite *)p;
    if ((r->game & 0xFFFFFF) != kSteamApp || g_lobby != LB_NONE) return;
    g_sInvite = r->lobby;
    g_sInviteName = SteamName(r->user);
    g_joinFallback = false;
    SetStatus(K_OK, T(L"%s t'invite : clique REJOINDRE", L"%s invites you: click JOIN"), SteamName(r->user).c_str());
    if (g_testSalon.empty()) LobbySound(SND_JOIN);
}

static void SteamTick()
{
    static DWORD lastTry, lastSync, lastFriends;
    DWORD now = GetTickCount();
    if (g_steamUp != 1) {
        // Mode Steam : pret des l'ouverture (les invitations arrivent au lanceur), nouvel essai toutes les 5 s.
        bool want = (g_steamNet || g_sConnect) && g_state == ST_IDLE && !g_goWait && !g_gameDir.empty();
        if (!want || (lastTry && now - lastTry < 5000)) return;
        lastTry = now;
        if (!SteamUp()) return;
    }
    sapi::RunCallbacks();
    if (g_sCall) {
        bool failed = false;
        if (sapi::IsAPICallCompleted(sapi::ut, g_sCall, &failed)) {
            uint64_t call = g_sCall;
            int kind = g_sCallKind;
            g_sCall = 0;
            if (kind == 513) {
                SLobbyCreated r = {};
                bool ok = sapi::GetAPICallResult(sapi::ut, call, &r, sizeof(r), 513, &failed) && !failed;
                SteamCreated(ok, r.result, r.lobby);
            } else {
                SLobbyEnter r = {};
                bool ok = sapi::GetAPICallResult(sapi::ut, call, &r, sizeof(r), 504, &failed) && !failed;
                SteamEntered(ok, r.response, r.lobby);
            }
            g_sCallStale = false;
        }
    }
    if (g_sConnect && !g_sCall && g_state == ST_IDLE && !g_busy && !g_goWait && g_modOk) {
        SteamId l = g_sConnect;
        g_sConnect = 0;
        if (!(g_lobbySteam && g_sLobby == l)) SteamLobbyJoin(l);
    }
    if (g_lobbySteam && g_sLobby && (g_lobby == LB_HOST || g_lobby == LB_GUEST) && now - lastSync >= 100) { lastSync = now; SteamLobbySync(); }
    if (g_sFriendsView && now - lastFriends >= 3000) { lastFriends = now; SteamFriendsRefresh(); }
}

// ---- dessin : liste des amis (hote, a la place des joueurs)
static const float kFriendRowH = 66;
static const RectF kFriendsBack(0, 0, 0, 0);   // (le retour est dans l'en-tete du salon)
static float SteamFriendsMaxScroll() { return max(0.0f, g_sFriends.size() * kFriendRowH - kLobbyList.Height); }
static int SteamFriendAt(float x, float y)
{
    if (!kLobbyList.Contains(x, y)) return -1;
    int i = (int)((y - kLobbyList.Y + g_scroll[TAB_LOBBY]) / kFriendRowH);
    return i >= 0 && i < (int)g_sFriends.size() ? i : -1;
}
static void DrawSteamFriends(Graphics &g)
{
    Title(g, T(L"Inviter des amis", L"Invite friends"), RectF(kLobbyCard.X + 22, kLobbyCard.Y + 18, 300, 26), 17, kInk);
    wchar_t sub[160];
    if (g_sFriends.empty()) swprintf_s(sub, T(L"Aucun ami Steam en ligne (%d hors ligne)", L"No Steam friend online (%d offline)"), g_sOffline);
    else swprintf_s(sub, T(L"Amis Steam en ligne : %d · %d hors ligne", L"Steam friends online: %d · %d offline"), (int)g_sFriends.size(), g_sOffline);
    Text(g, sub, RectF(kLobbyCard.X + 22, kLobbyCard.Y + 40, 400, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    if (g_sFriends.empty()) {
        Para(g, T(L"Tes amis Steam apparaîtront ici dès qu'ils sont en ligne. Ils peuvent aussi rejoindre depuis Steam (« Rejoindre la partie »).",
                  L"Your Steam friends show up here as soon as they are online. They can also join from Steam (\"Join game\")."),
             RectF(kLobbyList.X + 80, kLobbyList.Y + 120, kLobbyList.Width - 160, 60), 13, kGrey, StringAlignmentCenter);
        return;
    }
    float sc = g_scroll[TAB_LOBBY], ms = SteamFriendsMaxScroll();
    DWORD now = GetTickCount();
    GraphicsState gst = g.Save();
    g.SetClip(kLobbyList, CombineModeIntersect);
    for (int i = 0; i < (int)g_sFriends.size(); i++) {
        const SteamFriend &f = g_sFriends[i];
        RectF r(kLobbyList.X, kLobbyList.Y + i * kFriendRowH - sc, kLobbyList.Width - (ms > 0 ? 12 : 0), kFriendRowH - 8);
        if (r.Y + r.Height < kLobbyList.Y || r.Y > kLobbyList.Y + kLobbyList.Height) continue;
        bool in = SteamInLobby(f.id);
        auto inv = g_sInvited.find(f.id);
        bool invited = inv != g_sInvited.end() && now - inv->second < 60000;
        bool hot = g_lobbyHot == 100 + i && !in;
        GraphicsPath rp;
        RoundRect(rp, r, 16);
        SolidBrush rb(hot ? TH(cardSel) : TH(card));
        g.FillPath(&rb, &rp);
        Pen rpen(hot ? WithA(kAcc, 0.7f) : TH(choiceBorder), 1.0f);
        g.DrawPath(&rpen, &rp);
        RectF av(r.X + 12, r.Y + 10, r.Height - 20, r.Height - 20);
        if (!DrawSteamAvatar(g, av, f.id)) {
            GraphicsPath ap; ap.AddEllipse(av);
            SolidBrush ab(WithA(kGrey, 0.35f)); g.FillPath(&ab, &ap);
            Text(g, f.name.substr(0, 1), av, 13, FontStyleBold, kInk);
        }
        Text(g, f.name, RectF(r.X + 70, r.Y + 10, 300, 22), 15, FontStyleBold, kInk, StringAlignmentNear);
        const wchar_t *st = f.mwc ? T(L"Joue à My Winter Car", L"Playing My Winter Car")
                          : f.state == 2 ? T(L"Occupé", L"Busy") : f.state == 3 ? T(L"Absent", L"Away") : f.state == 4 ? T(L"Endormi", L"Snooze")
                          : T(L"En ligne", L"Online");
        Text(g, st, RectF(r.X + 70, r.Y + 32, 300, 18), 12, FontStyleRegular, f.mwc ? kOk : kGrey, StringAlignmentNear);
        RectF br(r.X + r.Width - 140, r.Y + (r.Height - 34) / 2, 124, 34);
        GraphicsPath bp;
        RoundRect(bp, br, 17);
        const wchar_t *bl;
        if (in) { bl = T(L"DANS LE SALON", L"IN THE LOBBY"); SolidBrush hb(WithA(kAcc, 0.22f)); g.FillPath(&hb, &bp); }
        else if (invited && !hot) { bl = T(L"INVITÉ ✓", L"INVITED ✓"); Pen np(WithA(kAcc, 0.9f), 1.2f); g.DrawPath(&np, &bp); }
        else { bl = invited ? T(L"RÉINVITER", L"INVITE AGAIN") : T(L"INVITER", L"INVITE"); SolidBrush lg(kAcc); g.FillPath(&lg, &bp); }
        Text(g, bl, br, 11.5f, FontStyleBold, in ? kInk : invited && !hot ? kAcc : kOnAcc);
    }
    g.Restore(gst);
    if (ms > 0) {
        float h = kLobbyList.Height * kLobbyList.Height / (kLobbyList.Height + ms), y = kLobbyList.Y + (kLobbyList.Height - h) * sc / ms;
        GraphicsPath sp; RoundRect(sp, RectF(kLobbyList.X + kLobbyList.Width - 6, y, 4, h), 2);
        SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
    }
}
