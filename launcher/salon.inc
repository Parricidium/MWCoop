// (inclus par launcher.cpp, a cette place : SALON (reseau du salon : hote, invites, etat) -- decoupe de la refonte du 14/10)
// ---------------------------------------------------------------- salon
// Le lanceur de l'hote ouvre un salon en TCP sur le port de la partie (le jeu, lui, est en UDP sur ce meme numero :
// pas de conflit). Chaque connexion commence par "MWS1" (pas compatible avec le salon de SACoop), puis des messages
// [u16 longueur][u8 type][...] :
//   HELLO (proto, version, pseudo, apparence, code de session) -> WELCOME (numero) ou REJECT (raison : "version X",
//   "full N", "started", "closed", "proto", "code") ; STATE (joueurs : numero, pseudo, apparence, version, pret, ping ;
//   puis le choix de partie) ; READY (u8) ; SKIN (apparence changee dans l'onglet COOP) ; PING / PONG (u32, heure de
//   l'hote) ; GO (u8 partie).
// Salon prive / public et options de la partie (0.96.4, demande de JD du 16/10) : l'hote choisit Public ou Prive (prive :
// un code de session de 6 caracteres, montre en grand dans le salon, a copier ; les invites par IP le tapent sur
// l'accueil, HELLO le porte, sinon REJECT "code" ; par Steam, les donnees du salon le portent : un ami invite entre sans
// le taper, un ami venu de Steam sans invitation doit le taper), le nombre de joueurs (2 a 16) et les options de la
// partie (celles de la categorie Coop des Options du jeu que l'hote decide : argent de depart, triches, renverser, PvP,
// ajouts). Les valeurs viennent de mwcoop.ini a l'ouverture du salon et ne changent que POUR CE SALON : elles vont
// dans lancement.ini (le mod les lit 3 minutes, devant mwcoop.ini), chez l'hote et chez chaque invite. STATE les porte,
// APRES le nom de la sauvegarde (un ancien lanceur les ignore) : u8 joueurs max, u8 prive, str code, u8 n, puis n fois
// (str cle, u16 valeur). Chaque joueur a aussi ses options a lui (vocal, visages, carte, envoi de l'hote...) pour ce
// salon seulement, dans son lancement.ini. Le protocole (LB_PROTO) ne change pas : tout est ajoute en fin de message.
// GO : l'hote lance son jeu (lancement.ini : Partie=continuer|nouvelle, il entre en partie tout seul) ; chaque invite
// attend ~4 s (deux jeux sur le meme PC ne demarrent pas ensemble) puis lance le sien en invite : il suit l'hote.
//
// Test UDP (le salon passe en TCP, le jeu en UDP : une box qui ne redirige que le TCP laisse entrer dans le salon,
// puis le jeu de l'invite ne recoit jamais rien). Pendant le salon, l'hote ecoute aussi en UDP sur le port :
//   sonde de l'invite  : "MWU1" + 8 octets (nonce de l'invite) + u8 (son numero du salon)   [13 octets]
//   reponse de l'hote  : "MWU2" + les 8 memes octets                                         [12 octets]
// L'invite sonde toutes les 2 s (10 essais, puis toutes les 5 s) jusqu'a la reponse. L'hote note la sonde recue et
// l'ajoute a STATE, APRES le choix de partie : u8 n, puis n fois (u8 numero, u8 etat UDP_*). Un ancien lanceur ignore
// ces octets en trop (et les sondes : il n'ecoute pas en UDP). La socket UDP de l'hote est fermee par LobbyClose(),
// donc avant le lancement de son jeu (qui ouvre ce meme port UDP).
enum { LB_PROTO = 1, M_HELLO = 1, M_WELCOME, M_REJECT, M_STATE, M_READY, M_GO, M_PING, M_PONG, M_SKIN };
enum { M_MODS = 20, M_MODGET, M_MODDATA, M_MODSREQ, M_MODSX };   // mods de l'hote (modsync.inc ; M_MODSX : suite de la liste)
// M_SYNCST (invite -> hote) : u8 avancement de la reception de l'envoi de l'hote (0-99, 255 : rien en cours) ; M_LATE (hote ->
// invite en retard, a la place de WELCOME) : la partie est lancee, voici les mods (puis M_MODS...) -- u8 0 sur Steam : rien.
enum { M_SYNCST = 25, M_LATE };
// M_CHAT (0.97.0, tchat du salon) : invite -> hote [u16 n][n octets UTF-8] ; hote -> tous [u8 numero][u16 n][n octets]. Au plus
// 200 caracteres. LB_PROTO ne bouge pas : un ancien lanceur ignore les types qu'il ne connait pas (hote et invite), et la
// version doit de toute facon etre la meme. M_REJECT "kicked" en cours de salon : l'hote met l'invite dehors (clic sur sa ligne).
enum { M_CHAT = 27 };
static SOCKET g_listen = INVALID_SOCKET, g_guestSock = INVALID_SOCKET;
static SOCKET g_udpHost = INVALID_SOCKET;           // hote : ecoute UDP du salon (sous g_lcs)
static int g_udpHostErr;                            // hote : erreur de l'ouverture du port UDP (0 = ouvert)
static const DWORD kUdpWaitMs = 22000;              // hote : sans sonde apres ce delai, l'UDP de l'invite est bloque
struct Conn { SOCKET s; int id; };
static std::vector<Conn> g_conns;                   // hote : invites du salon (sous g_lcs)
static std::atomic<bool> g_goSent(false);
static std::atomic<int> g_lobbyGen(0);              // change a chaque ouverture/fermeture : messages perimes ignores
static std::wstring g_lobbyAddr, g_myAddresses;
static bool g_lobbySteam;                           // le salon en cours est un salon Steam (steam.inc)
static void SteamLobbyLeave(bool host);
static void DrawSyncCard(Graphics &g, RectF c, int hot);
static void SteamHostStart(int partie);
static void SSetLobby(const char *k, const std::string &v);
static void SSetMember(const char *k, const std::string &v);
static void SteamDown();
static int g_lobbyPort = 7870;
static int g_lobbyHot = -1;                         // choix de partie survole (0 continuer, 1 nouvelle)
static std::string g_mySkinSent;                    // derniere apparence annoncee au salon
// Mise en page du salon (0.97.0, maquette du 17/10) : bandeau en haut (code de session chez l'hote, l'hote chez l'invite),
// dessous deux colonnes Joueurs / Options, a droite une seule carte (partie, envoi, mods, conseil, LANCER / PRET).
static RectF kLobbyList(280, 274, 288, 454);   // liste des joueurs (placee par DrawLobby : pleine carte en connexion / amis Steam)
static const RectF kLobbyCard(264, 88, 654, 656);          // carte unique de gauche (connexion, amis Steam a inviter)
static const RectF kLobbyBanner(264, 88, 654, 108);
static const RectF kLobbyPlayersCard(264, 210, 320, 534), kLobbyOptsCard(598, 210, 320, 534);
static const RectF kLobbyRight(932, 88, 332, 656);
static RectF kChoiceR[2] = { RectF(950, 136, 296, 50), RectF(950, 192, 296, 50) };   // (placees par la carte de droite)
static const float kLobbyRowH = 72;
static DWORD g_lobbySinceT;                         // entree dans ce salon (bandeau de l'invite : « dans ce salon depuis »)
static int g_lobbySinceGen = -1;                    // ... pour quel salon (g_lobbyGen)
// Identifiants des nouvelles commandes du salon (suite de UI_LOBBY_* de uikit.inc ; 3200-3399 : LobbyOptClick, ui.inc)
enum { UI_LOBBY_AJOUTS = 3312,   // + k : ajouts MWCoop Tous / Choisir / Aucun
       UI_LOBBY_MAXSEG = 3316,   // + k : joueurs au plus 4 / 8 / 12 / 16
       UI_LOBBY_OPTRESET = 3320, UI_LOBBY_OPTKEEP,   // Remettre (les Reglages), Garder comme defauts
       UI_LOBBY_MODS = 3322,     // Gerer (page MODS)
       UI_LOBBY_CHAT = 3323, UI_LOBBY_SEND,   // tchat : champ « Message au salon », ENVOYER
       UI_LOBBY_SEGV = 3330,     // + rang : pastilles segmentees de « Les miennes » (kLobbySegKeys, a la suite ; 3330-3359)
       UI_LOBBY_DIFF = 3360 };   // + niveau : difficulte de depart (carte de droite, Nouvelle partie ; 1.8.9)
static RectF g_lobbyGearR, g_lobbyPopR;               // engrenage de l'en-tete, son menu

// Sons du salon (repris de SACoop) : arrivee, depart, pret, plus pret. Petites notes synthetisees (WAV en memoire).
enum { SND_JOIN, SND_LEAVE, SND_READY, SND_UNREADY, SND_COUNT };
static std::vector<uint8_t> g_snd[SND_COUNT];
static void MakeSound(std::vector<uint8_t> &w, std::initializer_list<float> notes, float noteMs, float vol)
{
    const int rate = 22050, per = (int)(rate * noteMs / 1000.0f), tail = rate / 5;
    int total = per * (int)notes.size() + tail;
    std::vector<float> buf(total, 0.0f);
    int k = 0;
    for (float f : notes) {
        int start = k++ * per;
        for (int i = 0; i < per + tail && start + i < total; i++) {
            float t = i / (float)rate;
            float env = (i < rate / 200 ? i / (rate / 200.0f) : 1.0f) * expf(-t * 9.0f);
            buf[start + i] += env * (sinf(6.2831853f * f * t) + 0.25f * sinf(6.2831853f * f * 2 * t));
        }
    }
    uint32_t data = total * 2;
    w.resize(44 + data);
    uint8_t *p = w.data();
    auto u32 = [&](int at, uint32_t v) { memcpy(p + at, &v, 4); };
    auto u16 = [&](int at, uint16_t v) { memcpy(p + at, &v, 2); };
    memcpy(p, "RIFF", 4); u32(4, 36 + data); memcpy(p + 8, "WAVEfmt ", 8); u32(16, 16); u16(20, 1); u16(22, 1);
    u32(24, rate); u32(28, rate * 2); u16(32, 2); u16(34, 16); memcpy(p + 36, "data", 4); u32(40, data);
    for (int i = 0; i < total; i++) {
        float v = max(-1.0f, min(1.0f, buf[i] * vol));
        int16_t sv = (int16_t)(v * 32767);
        memcpy(p + 44 + i * 2, &sv, 2);
    }
}
static void LobbySound(int which)
{
    if (g_snd[0].empty()) {
        MakeSound(g_snd[SND_JOIN], { 523.3f, 659.3f, 784.0f }, 90, 0.30f);     // do mi sol : quelqu'un arrive
        MakeSound(g_snd[SND_LEAVE], { 784.0f, 659.3f, 523.3f }, 90, 0.26f);    // sol mi do : il part
        MakeSound(g_snd[SND_READY], { 987.8f, 1318.5f }, 70, 0.24f);           // si mi aigus : pret
        MakeSound(g_snd[SND_UNREADY], { 659.3f, 493.9f }, 80, 0.22f);          // mi si graves : plus pret
    }
    PlaySoundW((LPCWSTR)g_snd[which].data(), NULL, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
}

struct Wr {
    std::string d;
    void u8(int v) { d += (char)(uint8_t)v; }
    void u16(int v) { uint16_t x = (uint16_t)v; d.append((const char *)&x, 2); }
    void u32(uint32_t v) { d.append((const char *)&v, 4); }
    void str(const std::string &s) { size_t n = min<size_t>(s.size(), 255); u8((int)n); d.append(s.data(), n); }
};
struct Rd {
    const std::string &d; size_t p = 0; bool ok = true;
    Rd(const std::string &x) : d(x) {}
    int u8() { if (p + 1 > d.size()) { ok = false; return 0; } return (uint8_t)d[p++]; }
    int u16() { if (p + 2 > d.size()) { ok = false; return 0; } uint16_t x; memcpy(&x, d.data() + p, 2); p += 2; return x; }
    uint32_t u32() { if (p + 4 > d.size()) { ok = false; return 0; } uint32_t x; memcpy(&x, d.data() + p, 4); p += 4; return x; }
    std::string str() { int n = u8(); if (!ok || p + n > d.size()) { ok = false; return ""; } std::string s = d.substr(p, n); p += n; return s; }
};
static bool SendAllS(SOCKET s, const void *d, int n)
{
    const char *p = (const char *)d;
    while (n > 0) { int r = send(s, p, n, 0); if (r <= 0) return false; p += r; n -= r; }
    return true;
}
static bool RecvAllS(SOCKET s, void *d, int n)
{
    char *p = (char *)d;
    while (n > 0) { int r = recv(s, p, n, 0); if (r <= 0) return false; p += r; n -= r; }
    return true;
}
static bool SendMsg(SOCKET s, const Wr &w);
static void SyncOnOffer(const std::string &m);
static void SyncOnOfferMore(const std::string &m);
static void SyncOnData(const std::string &m);
static bool SyncServe(const std::string &m, Wr &out);
static void SyncWelcome(SOCKET s);
static void SyncReset();
static void SyncOfferReset();
static bool SendMsg(SOCKET s, const Wr &w)
{
    uint16_t n = (uint16_t)w.d.size();
    return SendAllS(s, &n, 2) && SendAllS(s, w.d.data(), n);
}
static bool RecvMsg(SOCKET s, std::string &out)
{
    uint16_t n;
    if (!RecvAllS(s, &n, 2)) return false;
    out.resize(n);
    return n == 0 || RecvAllS(s, &out[0], n);
}

static int LobbyPort() { const Opt *po = OptByKey("Port"); return po ? OptGet(*po) : 7870; }

// --- options du salon (voir en tete) : cle de [Coop], valeur par defaut (celle du mod), decidee par l'hote ou par chacun
struct LobbyOptDef { const char *key; int def; bool host; };
static const LobbyOptDef kLobbyOptDefs[] = {
    { "ArgentDepart", 1, true }, { "Triches", 0, true }, { "Renverser", 1, true }, { "PvP", 0, true }, { "Jokkis", 0, true }, { "VenttiEnjeux", 1, true },
    { "Difficulte", 0, true }, { "VitesseVitres", 1, true }, { "PencheVitre", 1, true },
    { "SansAjouts", 0, true }, { "AjoutDrone", 1, true }, { "AjoutSacs", 1, true }, { "AjoutBoutique", 1, true }, { "AjoutMusee", 1, true }, { "AjoutVaz", 1, true },
    { "Vocal", 1, false }, { "VocalPortee", 35, false }, { "VolumeVocal", 100, false }, { "GainMicro", 100, false },
    { "VocalActivation", 0, false }, { "VocalSeuil", 12, false }, { "Ronflements", 1, false },
    { "VoirVisages", 1, false }, { "CarteMaintenir", 0, false }, { "Marqueurs", 1, false }, { "ModsAuto", 0, false },
};
static const int kLobbyOptN = (int)(sizeof(kLobbyOptDefs) / sizeof(kLobbyOptDefs[0]));
static std::map<std::string, int> g_lobbyOpt;       // mes choix pour CE salon (hote : options de la partie et les miennes ; invite : les miennes)
static std::map<std::string, int> g_hostOpt;        // invite : options de la partie recues de l'hote (sous g_lcs)
static int g_lobbyOptTab = -1;                      // carte des options du salon : 0 la partie (de l'hote), 1 les miennes ; -1 : selon le role
static bool g_lobbyAjoutsPick;                      // hote : ajouts MWCoop « Choisir » (les quatre interrupteurs montres)
static DWORD g_codeCopiedT;                         // COPIER du code de session : "copie" pendant 2 s
static int LobbyOptGet(const char *key)
{
    auto it = g_lobbyOpt.find(key);
    if (it != g_lobbyOpt.end()) return it->second;
    for (auto &d : kLobbyOptDefs) if (!strcmp(d.key, key)) return d.def;
    return 0;
}
// Ouverture d'un salon : les valeurs du moment (MWCoop\mwcoop.ini [Coop], comme GfxLoad), sans y toucher ensuite.
static void LobbyOptImport()
{
    g_lobbyOpt.clear();
    EnterCriticalSection(&g_lcs); g_hostOpt.clear(); LeaveCriticalSection(&g_lcs);
    g_lobbyOptTab = -1;
    g_lobbyAjoutsPick = false;
    std::string ini = ModIniA();
    for (auto &d : kLobbyOptDefs) g_lobbyOpt[d.key] = GetPrivateProfileIntA("Coop", d.key, d.def, ini.c_str());
    g_lobbyPrive = GetPrivateProfileIntW(L"Lanceur", L"SalonPrive", 1, g_iniLauncher.c_str()) != 0;
    g_lobbyMax = max(2, min(kLobbyMaxAbs, (int)GetPrivateProfileIntW(L"Lanceur", L"SalonMax", 8, g_iniLauncher.c_str())));
}
static std::string LobbyOptText()   // "cle=valeur;..." des options de la partie (salon Steam, journal)
{
    std::string s;
    for (auto &d : kLobbyOptDefs) if (d.host) s += std::string(d.key) + "=" + std::to_string(LobbyOptGet(d.key)) + ";";
    return s;
}
static void LobbyOptParse(const std::string &s, std::map<std::string, int> &out)   // (l'inverse)
{
    out.clear();
    size_t p = 0;
    while (p < s.size()) {
        size_t e = s.find(';', p), q = s.find('=', p);
        if (e == std::string::npos) e = s.size();
        if (q != std::string::npos && q < e) out[s.substr(p, q - p)] = atoi(s.c_str() + q + 1);
        p = e + 1;
    }
}
// Lignes de lancement.ini (lu par le mod comme [Coop], devant mwcoop.ini, 3 minutes) : chez l'hote tout ; chez l'invite
// les options de la partie de l'hote et les siennes. JoueursMax : pour le salon Steam du jeu (invites en retard).
static std::string LobbyCoopLines(bool host)
{
    std::string t = "[Coop]\r\n";
    std::map<std::string, int> ho;
    if (!host) { EnterCriticalSection(&g_lcs); ho = g_hostOpt; LeaveCriticalSection(&g_lcs); }
    for (auto &d : kLobbyOptDefs) {
        if (d.host && !host) { auto it = ho.find(d.key); if (it != ho.end()) t += std::string(d.key) + "=" + std::to_string(it->second) + "\r\n"; continue; }
        t += std::string(d.key) + "=" + std::to_string(LobbyOptGet(d.key)) + "\r\n";
    }
    if (host) t += "JoueursMax=" + std::to_string(g_lobbyMax) + "\r\n";
    return t;
}
// Code de session : 6 caracteres sans ambiguite (ni 0/O ni 1/I), tires au sort.
static std::string MakeSessionCode()
{
    static const char *al = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    static uint64_t x;
    if (!x) x = (uint64_t)qpc.QuadPart ^ ((uint64_t)GetCurrentProcessId() << 32) ^ ((uint64_t)GetTickCount() << 11) ^ 0x9E3779B97F4A7C15ULL;
    std::string c;
    for (int i = 0; i < 6; i++) { x ^= x << 13; x ^= x >> 7; x ^= x << 17; c += al[(x >> 24) % 32]; }
    return c;
}
static std::string CodeNorm(const std::wstring &w)   // code tape : majuscules, sans espaces
{
    std::string c;
    for (wchar_t ch : w) if (iswalnum(ch) && ch < 128) c += (char)towupper(ch);
    return c;
}
static std::wstring LobbyModeText()   // sous-titre : "Public" ou "Prive . code ABC123"
{
    if (!g_lobbyPrive) return T(L"Public", L"Public");
    return std::wstring(T(L"Privé · code", L"Private · code")) + L" " + Widen(g_lobbyCode);
}
static std::string MyShirt() { const Opt *ap = OptByKey("Apparence"); return ap ? ap->svals[OptGet(*ap)] : DefaultShirt(); }
// Apparence annoncee au salon : complete ("haut|pantalon|visage|corps|chapeau|lunettes|cheveux", comme le mod).
static std::string MySkin() { return PersoLookString(); }
static std::string MyName() { return Narrow(PlayerName(), CP_UTF8); }
static std::string MyVersionRaw() { return g_localVer.empty() ? "dev" : Narrow(g_localVer, CP_UTF8); }   // MWCoop\version.txt
// Version annoncee au salon (TCP et Steam) : celle du mod, plus "@msc" sur My Summer Car (19/10) -- un invite sur l'autre jeu
// est refuse comme une autre version, et VersionMismatch lui propose de changer de jeu.
static std::string MyVersion() { return MyVersionRaw() + (g_game == GAME_MSC ? "@msc" : ""); }
static int GameOfVersion(const std::wstring &v) { return v.find(L"@msc") != std::wstring::npos ? GAME_MSC : GAME_MWC; }
static std::wstring VersionOnly(const std::wstring &v) { size_t a = v.find(L'@'); return a == std::wstring::npos ? v : v.substr(0, a); }

// Tchat du salon (0.97.0, maquette du 17/10) : les trois derniers messages sous les joueurs, un champ et ENVOYER (Entree
// envoie). Par IP, l'hote recoit M_CHAT de l'invite et le renvoie a tous (lui compris) ; par Steam, chacun pose sa derniere
// ligne dans ses donnees de membre ("chat" = "<numero>|<texte>"), les autres la lisent a chaque SteamLobbySync. L'historique
// vit le temps du salon (LobbyChatClear a l'ouverture et a la fermeture) ; chaque ligne va aussi dans le journal du lanceur.
struct ChatMsg { int id; std::string name; std::wstring text; };
static std::vector<ChatMsg> g_chat;                   // (sous g_lcs)
static std::wstring g_chatText;                       // ce que je tape
static bool g_chatFocus;                              // le champ a le clavier
static int g_chatSeq;                                 // Steam : numero de ma derniere ligne
static std::map<uint64_t, int> g_sChatSeen;           // Steam : derniere ligne lue de chaque membre
static const size_t kChatMax = 200;
static int g_kickArm = -1;                            // hote : invite dont la ligne a ete cliquee une fois (numero)
static DWORD g_kickArmT;
static void GuestSend(const Wr &w);
static void LobbyChatClear()
{
    EnterCriticalSection(&g_lcs); g_chat.clear(); LeaveCriticalSection(&g_lcs);
    g_chatText.clear(); g_chatFocus = false; g_chatSeq = 0; g_sChatSeen.clear(); g_kickArm = -1;
}
static void LobbyChatAdd(int id, const std::string &name, const std::string &utf8)
{
    std::wstring t = Widen(utf8, CP_UTF8);
    if (t.size() > kChatMax) t.resize(kChatMax);
    if (t.empty()) return;
    EnterCriticalSection(&g_lcs);
    g_chat.push_back({ id, name, t });
    if (g_chat.size() > 60) g_chat.erase(g_chat.begin());
    LeaveCriticalSection(&g_lcs);
    LaunchLog("salon : tchat %s (joueur %d) : %s", name.c_str(), id, utf8.c_str());
}
// Hote : la ligne a tous les invites (et chez lui).
static void LobbyChatRelay(int id, const std::string &name, const std::string &utf8)
{
    LobbyChatAdd(id, name, utf8);
    Wr w; w.u8(M_CHAT); w.u8(id); w.u16((int)utf8.size()); w.d += utf8;
    EnterCriticalSection(&g_lcs);
    for (auto &c : g_conns) SendMsg(c.s, w);
    LeaveCriticalSection(&g_lcs);
}
// ENVOYER (ou Entree) : ce que je tape, selon le role et le transport.
static void LobbyChatSend()
{
    std::wstring t = Trim(g_chatText);
    g_chatText.clear();
    int lobby = g_lobby;
    if (t.empty() || (lobby != LB_HOST && lobby != LB_GUEST)) return;
    if (t.size() > kChatMax) t.resize(kChatMax);
    std::string u = Narrow(t, CP_UTF8);
    if (g_lobbySteam) { SSetMember("chat", std::to_string(++g_chatSeq) + "|" + u); return; }   // (lu par SteamLobbySync, chez moi aussi)
    if (lobby == LB_HOST) { LobbyChatRelay(0, MyName(), u); return; }
    Wr w; w.u8(M_CHAT); w.u16((int)u.size()); w.d += u;
    GuestSend(w);
}
// Caractere tape dans le champ du tchat (WM_CHAR) ; vrai si pris.
static bool LobbyChatChar(wchar_t c)
{
    if (!g_chatFocus || g_state != ST_IDLE || g_tab != TAB_LOBBY || (g_lobby != LB_HOST && g_lobby != LB_GUEST)) return false;
    if (c == 27) g_chatFocus = false;
    else if (c == 13) LobbyChatSend();
    else if (c == 8) { if (!g_chatText.empty()) g_chatText.pop_back(); }
    else if (c == 127) g_chatText.clear();
    else if (c == 22 && OpenClipboard(g_wnd)) {   // Ctrl+V
        HANDLE d = GetClipboardData(CF_UNICODETEXT);
        const wchar_t *s = d ? (const wchar_t *)GlobalLock(d) : NULL;
        if (s) { for (; *s && *s != L'\r' && *s != L'\n' && g_chatText.size() < kChatMax; s++) if (*s >= 32) g_chatText += *s; GlobalUnlock(d); }
        CloseClipboard();
    }
    else if (c >= 32 && g_chatText.size() < kChatMax) g_chatText += c;
    g_time = 0.2f;
    return true;
}
// Hote (IP) : met un invite dehors (M_REJECT "kicked", puis sa connexion coupee : DropConn suit).
static void LobbyKick(int id)
{
    if (g_lobby != LB_HOST || g_lobbySteam || id <= 0) return;
    std::string name;
    EnterCriticalSection(&g_lcs);
    for (auto &c : g_conns) if (c.id == id) {
        for (auto &p : g_peers) if (p.id == id) name = p.name;
        Wr w; w.u8(M_REJECT); w.str("kicked"); SendMsg(c.s, w);
        shutdown(c.s, SD_BOTH);
    }
    LeaveCriticalSection(&g_lcs);
    if (!name.empty()) { SetStatus(K_WARN, T(L"%S mis dehors", L"%S kicked out"), name.c_str()); LaunchLog("salon : %s (joueur %d) mis dehors par l'hote", name.c_str(), id); }
}

// Rejoindre un hote d'une autre version (demande de JD, 14/10 : « que ca check la version, si elle n'est pas bonne, une
// proposition de mettre a jour »). Hote plus recent : proposer la mise a jour tout de suite (le lanceur se relance, puis on
// rejoint) ; hote plus ancien : c'est a lui de mettre a jour.
static bool GameSet(int game, bool host);   // (saves.inc)
static void VersionMismatch(const std::wstring &hisFull)
{
    // Autre jeu (MSC / MWC) : proposer de passer sur celui de l'hote (le lanceur se relance)
    if (GameOfVersion(hisFull) != g_game) {
        int hg = GameOfVersion(hisFull);
        SetStatus(K_ERR, T(L"L'hôte joue à %s", L"The host plays %s"), kGames[hg].name);
        TestLog("salon : l'hote joue a %s (version %s) : changement de jeu propose", Narrow(kGames[hg].id).c_str(), Narrow(hisFull, CP_UTF8).c_str());
        GameSet(hg, true);
        return;
    }
    std::wstring mine = Widen(MyVersionRaw()), his = VersionOnly(hisFull);
    if (mine != L"dev" && !IsReworkedVer(his)) {   // (hote encore en Legacy, retiree le 19/10 : a lui de mettre a jour)
        SetStatus(K_ERR, T(L"Version différente (toi %s, hôte %s) : l'hôte doit mettre à jour MWCoop", L"Different version (you %s, host %s): the host must update MWCoop"), mine.c_str(), his.c_str());
        return;
    }
    if (mine != L"dev" && CmpVer(his, mine) > 0) {
        SetStatus(K_ERR, T(L"Version diff\u00E9rente (toi %s, h\u00F4te %s) : mets \u00E0 jour", L"Different version (you %s, host %s): update"), mine.c_str(), his.c_str());
        wchar_t q[600];
        swprintf_s(q, T(L"L'h\u00F4te a MWCoop %s et toi %s : il faut la m\u00EAme version pour jouer ensemble.\n\nMettre \u00E0 jour maintenant ? Le lanceur se relance tout seul, puis tu pourras rejoindre.",
                        L"The host has MWCoop %s and you have %s: you need the same version to play together.\n\nUpdate now? The launcher restarts by itself, then you can join."), his.c_str(), mine.c_str());
        TestLog("salon : version de l'hote %s, la mienne %s : mise a jour proposee", Narrow(his, CP_UTF8).c_str(), Narrow(mine, CP_UTF8).c_str());
        if (MessageBoxW(g_wnd, q, L"MWCoop", MB_YESNO | MB_ICONQUESTION) == IDYES) StartUpdate(true);
        return;
    }
    SetStatus(K_ERR, T(L"Version diff\u00E9rente (toi %s, h\u00F4te %s) : l'h\u00F4te doit mettre \u00E0 jour MWCoop", L"Different version (you %s, host %s): the host must update MWCoop"), mine.c_str(), his.c_str());
}
// Apparence lisible ("Tenue 21", "Policier") : libelle de l'onglet COOP, sinon le nom brut.
static std::wstring SkinLabel(const std::string &look)
{
    std::string skin = look.substr(0, look.find('|'));   // (apparence complete : le haut)
    if (const Opt *ap = OptByKey("Apparence"))
        for (size_t i = 0; i < ap->svals.size(); i++)
            if (!_stricmp(ap->svals[i].c_str(), skin.c_str())) return TLab(ap->labFr, ap->labEn, i);
    auto il = g_skinLabels.find(SkinKey(skin));   // tenue plus recente que ce lanceur : libelle de index.txt
    if (il != g_skinLabels.end() && !il->second.empty()) return il->second;
    return skin.empty() ? L"?" : Widen(skin, CP_UTF8);
}
static const char *PartieName(int p) { return p == PARTIE_NOUVELLE ? "nouvelle" : "continuer"; }
// Couleur de la CORRIS choisie dans l'onglet VOITURE (pour la note "nouvelle partie").
static std::wstring CarColorName()
{
    int sel = CarSwatchSel();
    if (sel == 0) return g_game == GAME_MSC ? T(L"d'origine", L"original") : T(L"au hasard", L"random");   // (MSC : une seule teinte de depart, voiture.inc)
    if (sel > 0) return T(kSwatches[sel - 1].fr, kSwatches[sel - 1].en);
    wchar_t b[48];
    swprintf_s(b, T(L"personnalis\u00E9e #%06X", L"custom #%06X"), g_carColor & 0xFFFFFF);
    return b;
}

// --- hote
static void BroadcastState()
{
    Wr w;
    w.u8(M_STATE);
    EnterCriticalSection(&g_lcs);
    w.u8((int)g_peers.size());
    for (auto &p : g_peers) { w.u8(p.id); w.str(p.name); w.str(p.skin); w.str(p.ver); w.u8(p.ready); w.u16(min(p.ping, 9999)); }
    w.u8(g_partie);
    w.u8((int)g_peers.size());   // test UDP de chacun (ignore par un ancien lanceur)
    for (auto &p : g_peers) { w.u8(p.id); w.u8(p.udp); }
    { std::string sn = Narrow(SaveCurrentName(), CP_UTF8); if (sn.size() > 200) sn.resize(200); w.str(sn); }   // (sauvegarde de l'hote : saves.inc ; ignore par un ancien lanceur)
    w.u8(g_lobbyMax); w.u8(g_lobbyPrive ? 1 : 0); w.str(g_lobbyPrive ? g_lobbyCode : "");   // (0.96.4 : salon et options de la partie, voir en tete)
    int nh = 0;
    for (auto &d : kLobbyOptDefs) if (d.host) nh++;
    w.u8(nh);
    for (auto &d : kLobbyOptDefs) if (d.host) { w.str(d.key); w.u16(LobbyOptGet(d.key)); }
    for (auto &c : g_conns) SendMsg(c.s, w);
    LeaveCriticalSection(&g_lcs);
}
static LobbyPeer *PeerById(int id) { for (auto &p : g_peers) if (p.id == id) return &p; return NULL; }
static void DropConn(SOCKET s)
{
    EnterCriticalSection(&g_lcs);
    for (size_t i = 0; i < g_conns.size(); i++)
        if (g_conns[i].s == s) {
            int id = g_conns[i].id;
            g_conns.erase(g_conns.begin() + i);
            for (size_t k = 0; k < g_peers.size(); k++) if (g_peers[k].id == id) { TestLog("salon : %s part (joueur %d)", g_peers[k].name.c_str(), id); g_peers.erase(g_peers.begin() + k); break; }
            break;
        }
    LeaveCriticalSection(&g_lcs);
    closesocket(s);
}
static bool LateSession(SOCKET s, const std::string &name);   // (modsync.inc)
static void LobbySession(SOCKET s)
{
    std::string m;
    if (!RecvMsg(s, m)) { closesocket(s); return; }
    Rd r(m);
    int type = r.u8(), proto = r.u8();
    std::string ver = r.str(), name = r.str(), skin = r.str();
    std::string code = r.ok && r.p < m.size() ? r.str() : "";   // (code de session, 0.96.4 ; absent : ancien lanceur)
    auto reject = [&](const std::string &why) { Wr w; w.u8(M_REJECT); w.str(why); SendMsg(s, w); shutdown(s, SD_SEND); closesocket(s); };
    if (!r.ok || type != M_HELLO || proto != LB_PROTO) { TestLog("salon : connexion refusee (protocole)"); reject("proto"); return; }
    std::string mine = MyVersion();
    if (ver != mine) {
        TestLog("salon : %s refuse (sa version %s, celle de l'hote %s)", name.c_str(), ver.c_str(), mine.c_str());
        SetStatus(K_WARN, T(L"%S refus\u00E9 : version %S (la tienne : %S)", L"%S refused: version %S (yours: %S)"), name.c_str(), ver.c_str(), mine.c_str());
        reject("version " + mine);
        return;
    }
    if (g_lobbyPrive && _stricmp(code.c_str(), g_lobbyCode.c_str())) {   // salon prive : le code, avant tout (meme en retard)
        TestLog("salon : %s refuse (code de session %s, attendu %s)", name.c_str(), code.c_str(), g_lobbyCode.c_str());
        SetStatus(K_WARN, T(L"%S refusé : code de session incorrect", L"%S refused: wrong session code"), name.c_str());
        reject("code");
        return;
    }
    if (g_goSent || g_lobby != LB_HOST) {
        if (g_lateServe && LateSession(s, name)) return;   // (partie lancee : ses mods, puis il rejoint en jeu)
        reject(g_goSent ? "started" : "closed");
        return;
    }
    int id = -1;
    EnterCriticalSection(&g_lcs);   // (WELCOME envoye sous le verrou : pas de STATE glisse avant lui)
    for (int k = 1; k < g_lobbyMax && id < 0; k++) if (!PeerById(k)) id = k;
    if (id > 0) {
        g_peers.push_back({ id, name, skin, ver, false, 0, UDP_WAIT, GetTickCount() });
        g_peers.back().dl = -2;   // (son etat de l'envoi : pas encore dit)
        g_conns.push_back({ s, id });
        Wr w; w.u8(M_WELCOME); w.u8(id); SendMsg(s, w);
    }
    LeaveCriticalSection(&g_lcs);
    if (id < 0) { TestLog("salon : %s refuse (salon complet, %d joueurs)", name.c_str(), g_lobbyMax); reject("full " + std::to_string(g_lobbyMax)); return; }
    TestLog("salon : %s arrive (joueur %d, version %s, apparence %s)", name.c_str(), id, ver.c_str(), skin.c_str());
    BroadcastState();
    SyncWelcome(s);
    DWORD to = 60000;   // (l'invite repond aux pings toutes les 2 s)
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char *)&to, sizeof(to));
    while (RecvMsg(s, m)) {
        Rd q(m);
        int t = q.u8();
        if (t == M_MODGET) {   // (fichier lu hors du verrou)
            Wr w;
            if (SyncServe(m, w)) { EnterCriticalSection(&g_lcs); SendMsg(s, w); LeaveCriticalSection(&g_lcs); }
            continue;
        }
        EnterCriticalSection(&g_lcs);
        LobbyPeer *p = PeerById(id);
        if (p && t == M_READY) { p->ready = q.u8() != 0; TestLog("salon : %s (joueur %d) pret=%d", p->name.c_str(), id, (int)p->ready); }
        else if (p && t == M_PONG) { uint32_t sent = q.u32(); if (q.ok) p->ping = (int)(GetTickCount() - sent); }
        else if (p && t == M_SKIN) { std::string sk = q.str(); if (q.ok) { p->skin = sk; TestLog("salon : joueur %d apparence %s", id, sk.c_str()); } }
        else if (p && t == M_SYNCST) { int v = q.u8(); if (q.ok && (v == 255 ? -1 : v) != p->dl) { p->dl = v == 255 ? -1 : v; if (p->dl < 0 || p->dl % 25 == 0) TestLog("salon : %s (joueur %d) envoi de l'hote %d%%", p->name.c_str(), id, p->dl); } }
        std::string chatName, chatText;
        if (p && t == M_CHAT) { int n = q.u16(); if (q.ok && q.p + n <= m.size() && n <= 4 * (int)kChatMax) { chatName = p->name; chatText = m.substr(q.p, n); } }
        LeaveCriticalSection(&g_lcs);
        if (!chatText.empty()) LobbyChatRelay(id, chatName, chatText);   // (hors du verrou : LobbyChatRelay le prend)
        if (t == M_READY || t == M_SKIN) BroadcastState();
    }
    DropConn(s);
    BroadcastState();
}
static DWORD WINAPI ConnThread(void *param)
{
    SOCKET s = (SOCKET)param;
    DWORD to = 15000;
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char *)&to, sizeof(to));
    DWORD sto = 3000;   // un invite bloque ne fige pas la fenetre (envois sous g_lcs)
    setsockopt(s, SOL_SOCKET, SO_SNDTIMEO, (const char *)&sto, sizeof(sto));
    char magic[4];
    if (RecvAllS(s, magic, 4) && !memcmp(magic, "MWS1", 4)) LobbySession(s);   // (ferme la socket)
    else closesocket(s);
    return 0;
}
static DWORD WINAPI AcceptThread(void *param)
{
    SOCKET ls = (SOCKET)param;
    for (;;) {
        SOCKET c = accept(ls, NULL, NULL);
        if (c == INVALID_SOCKET) break;   // (salon ferme : socket d'ecoute fermee)
        HANDLE t = CreateThread(NULL, 0, ConnThread, (void *)c, 0, NULL);
        if (t) CloseHandle(t); else closesocket(c);
    }
    return 0;
}
static std::wstring LocalAddresses()
{
    char host[256];
    std::wstring out;
    if (gethostname(host, sizeof(host)) != 0) return out;
    addrinfo hints = {}, *res = NULL;
    hints.ai_family = AF_INET;
    if (getaddrinfo(host, NULL, &hints, &res) != 0) return out;
    int n = 0;
    for (addrinfo *a = res; a && n < 2; a = a->ai_next) {
        char ip[64];
        inet_ntop(AF_INET, &((sockaddr_in *)a->ai_addr)->sin_addr, ip, sizeof(ip));
        if (!strncmp(ip, "127.", 4)) continue;
        if (!out.empty()) out += L" \u00B7 ";
        out += Widen(ip);
        n++;
    }
    freeaddrinfo(res);
    return out;
}

// --- test UDP
#ifndef SIO_UDP_CONNRESET
#define SIO_UDP_CONNRESET _WSAIOW(IOC_VENDOR, 12)
#endif
// Socket UDP sans l'erreur "connexion reinitialisee" de Windows (un ICMP "port injoignable" apres un envoi ferait
// echouer le recvfrom suivant).
static SOCKET UdpSocket()
{
    SOCKET s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s == INVALID_SOCKET) return s;
    BOOL off = FALSE;
    DWORD got = 0;
    WSAIoctl(s, SIO_UDP_CONNRESET, &off, sizeof(off), NULL, 0, &got, NULL, NULL);
    return s;
}
// Hote : repond aux sondes et note l'invite qui a sonde ; s'arrete quand LobbyClose() ferme la socket.
static DWORD WINAPI UdpHostThread(void *param)
{
    SOCKET s = (SOCKET)param;
    char buf[64];
    for (;;) {
        sockaddr_in from = {};
        int fl = sizeof(from);
        int n = recvfrom(s, buf, sizeof(buf), 0, (sockaddr *)&from, &fl);
        if (n < 0) {
            int e = WSAGetLastError();
            if (e == WSAECONNRESET || e == WSAEMSGSIZE) continue;
            break;   // (socket fermee : fin du salon)
        }
        if (n < 12 || memcmp(buf, "MWU1", 4)) continue;
        char reply[12];
        memcpy(reply, "MWU2", 4);
        memcpy(reply + 4, buf + 4, 8);
        sendto(s, reply, sizeof(reply), 0, (sockaddr *)&from, fl);
        if (n < 13) continue;
        int id = (uint8_t)buf[12];
        bool changed = false;
        std::string name;
        EnterCriticalSection(&g_lcs);
        LobbyPeer *p = id != 0 ? PeerById(id) : NULL;
        if (p && p->udp != UDP_OK) { p->udp = UDP_OK; changed = true; name = p->name; }
        LeaveCriticalSection(&g_lcs);
        if (changed) {
            char ip[64] = "";
            inet_ntop(AF_INET, &from.sin_addr, ip, sizeof(ip));
            TestLog("udp : sonde de %s (joueur %d) recue de %s:%d, reponse envoyee", name.c_str(), id, ip, ntohs(from.sin_port));
            BroadcastState();
        }
    }
    return 0;
}
static void UdpHostOpen(u_long bindAddr)
{
    g_udpHostErr = 0;
    if (g_testNoUdp) { TestLog("udp : pas d'ecoute UDP (essai /sansudp)"); return; }
    SOCKET s = UdpSocket();
    sockaddr_in a = {};
    a.sin_family = AF_INET;
    a.sin_addr.s_addr = htonl(bindAddr);
    a.sin_port = htons((u_short)g_lobbyPort);
    if (s == INVALID_SOCKET || bind(s, (sockaddr *)&a, sizeof(a)) != 0) {
        g_udpHostErr = WSAGetLastError();
        if (s != INVALID_SOCKET) closesocket(s);
        TestLog("udp : ecoute impossible sur le port %d (erreur %d)", g_lobbyPort, g_udpHostErr);
        return;
    }
    EnterCriticalSection(&g_lcs);
    g_udpHost = s;
    LeaveCriticalSection(&g_lcs);
    HANDLE t = CreateThread(NULL, 0, UdpHostThread, (void *)s, 0, NULL);
    if (t) CloseHandle(t);
    TestLog("udp : ecoute sur le port UDP %d", g_lobbyPort);
}

// Invite : sonde l'hote (2 s entre deux essais ; apres 10 essais sans reponse : bloque, puis un essai toutes les
// 5 s au cas ou l'hote corrige sa box). Fin : reponse recue, l'hote dit avoir recu une sonde, ou salon quitte.
static DWORD WINAPI UdpProbeThread(void *param)
{
    int gen = (int)(intptr_t)param;
    addrinfo hints = {}, *res = NULL;
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_DGRAM;
    if (getaddrinfo(Narrow(g_lobbyAddr).c_str(), NULL, &hints, &res) != 0 || !res) { TestLog("udp : adresse de l'hote introuvable"); return 0; }
    sockaddr_in to = *(sockaddr_in *)res->ai_addr;
    freeaddrinfo(res);
    to.sin_port = htons((u_short)g_lobbyPort);
    SOCKET s = UdpSocket();   // (port quelconque ; fermee ici meme, au plus 1/4 s apres la fin du salon)
    if (s == INVALID_SOCKET) return 0;
    char probe[13];
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    uint64_t nonce = (uint64_t)qpc.QuadPart * 6364136223846793005ULL ^ ((uint64_t)GetCurrentProcessId() << 32) ^ GetTickCount();
    memcpy(probe, "MWU1", 4);
    memcpy(probe + 4, &nonce, 8);
    probe[12] = (char)(uint8_t)g_myId;
    for (int tries = 0; gen == g_lobbyGen && g_udpMine != UDP_OK; tries++) {
        if (tries == 10 && g_udpMine == UDP_WAIT) { g_udpMine = UDP_FAIL; TestLog("udp : aucune reponse de l'hote apres 10 essais"); }
        if (sendto(s, probe, sizeof(probe), 0, (sockaddr *)&to, sizeof(to)) < 0) TestLog("udp : envoi impossible (erreur %d)", WSAGetLastError());
        DWORD t0 = GetTickCount(), wait = tries < 10 ? 2000 : 5000;
        while (gen == g_lobbyGen && g_udpMine != UDP_OK && GetTickCount() - t0 < wait) {
            fd_set rd;
            FD_ZERO(&rd); FD_SET(s, &rd);
            timeval tv = { 0, 250000 };   // (quart de seconde : suit la fermeture du salon)
            if (select(0, &rd, NULL, NULL, &tv) <= 0) continue;
            char buf[64];
            int n = recv(s, buf, sizeof(buf), 0);
            if (n == 12 && !memcmp(buf, "MWU2", 4) && !memcmp(buf + 4, &nonce, 8)) {
                g_udpMine = UDP_OK;
                TestLog("udp : reponse de l'hote (essai %d, %lu ms)", tries + 1, GetTickCount() - t0);
            }
        }
    }
    closesocket(s);
    return 0;
}

