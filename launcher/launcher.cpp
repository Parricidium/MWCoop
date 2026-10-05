// MWCoop - lanceur (MWCoop.exe, a poser dans le dossier du jeu, livre dans le paquet). Porte de celui de SACoop.
//
//  - Fenetre sans cadre, forme et transparence prises des PNG integres (ressources 2 et 3 : launcher.png et
//    launcher-sombre.png, dessines par make-art.ps1 ; fenetre "layered", alpha par pixel) ; textes, champs et boutons
//    dessines par-dessus avec GDI+.
//  - Cherche My Winter Car : le dossier du lanceur, sinon celui retenu dans mwcoop-lanceur.ini, sinon les
//    bibliotheques Steam (registre + steamapps\libraryfolders.vdf), sinon l'exe choisi par le joueur. Jeu reconnu :
//    mywintercar.exe + mywintercar_Data\Managed\Assembly-CSharp.dll. Version du jeu : 1re ligne de changelog.txt.
//  - A chaque lancement : derniere version publiee sur GitHub (Parricidium/MWCoop, pre-versions comprises). Plus
//    recente que MWCoop\version.txt (ou mod absent) : telechargement du zip, extraction (tar.exe de Windows), copie
//    dans le dossier du jeu. MWCoop\mwcoop.ini garde les valeurs du joueur (seules les cles nouvelles sont ajoutees) ;
//    version.txt est copie en dernier. Le lanceur se remplace lui-meme (renomme en .old) puis se relance. Depot sans
//    release (ou pas encore public) : rien a installer, pas d'erreur.
//  - Heberger ouvre un SALON (TCP sur le port de la partie ; le jeu, lui, est en UDP) : liste des joueurs, choix de
//    la partie (continuer / nouvelle), puis LANCER : chaque lanceur demarre son jeu. Rejoindre entre dans le salon
//    de l'adresse saisie (sinon, si l'hote joue deja, propose de rejoindre directement en jeu). Jouer en solo :
//    lancement immediat.
//  - Lancement : ecrit MWCoop\lancement.ini (lu par le chargeur et le mod, valable 3 minutes : Steam peut relancer
//    le jeu sans sa ligne de commande), lance mywintercar.exe avec les memes reglages en arguments
//    (-mwcoop-mode ...), puis reste en ecran d'attente jusqu'a la fenetre du jeu (UnityWndClass).
//  - Options du joueur : MWCoop\mwcoop.ini, section [Coop] (Pseudo, Adresse, Port, Apparence, CouleurVoiture).
//  - Onglet VOITURE : couleur de la CORRIS pour une nouvelle partie, apercu 3D (MWCoop\cache\corris.mesh, rendu
//    logiciel).
//  - Onglet TENUE : l'Apparence choisie facon GTA (personnage qui tourne, fleches, galerie de portraits) ; images
//    pre-rendues par le mod dans MWCoop\cache\skins (portraits repris dans le salon).
//
// Options de ligne de commande (tests, jamais de fenetre) :
//   /capture <png> <menu|coop|voiture|tenue|tenue-survol|notes|notesvide|journaux|attente|attente-udp|maj|sansjeu|salon|
//            salon-invite|salon-udp> [/theme clair|sombre] [/lang fr|en] [/echelle k] [/skins <dossier>] : rendu d'un
//            etat dans un PNG (/skins : images des tenues prises dans ce dossier au lieu de MWCoop\cache\skins) ;
//   /testsalon <hote|invite> <journal> [/partie continuer|nouvelle] [/sansudp] : salon sans fenetre visible (fenetre
//            "message only"), dans un dossier de jeu jetable (celui du lanceur, obligatoirement) : l'hote ouvre le salon
//            et lance des que l'invite est pret (et son test UDP fini) ; l'invite rejoint et se met pret. Chacun ecrit
//            son lancement.ini et le recopie dans le journal, SANS lancer le jeu. Pas de mise a jour ; salon sur
//            127.0.0.1 seulement. /sansudp : l'hote ne repond pas aux sondes UDP (port UDP "pas redirige").
//   /maj <dossier du jeu> <journal> [/depot proprietaire/depot] : mise a jour sans fenetre, journal = etat final ;
//   /jeu <journal> : jeu trouve (dossier, version) ;
//   /zip <fichier.zip> : le zip des journaux (bouton de la page JOURNAUX), ecrit la ou on le demande, sans explorateur.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <mmsystem.h>
#include <shlwapi.h>
#include <tlhelp32.h>
#include <algorithm>
using std::min;
using std::max;
#include <objidl.h>
#include <gdiplus.h>
#include <winhttp.h>
#include <commdlg.h>
#include <shellapi.h>
#include <shlobj.h>
#include <string>
#include <vector>
#include <map>
#include <atomic>
#include <stdio.h>
#include <math.h>
#include <stdarg.h>
#include <string.h>
#include <time.h>

using namespace Gdiplus;

static std::wstring g_repo = L"Parricidium/MWCoop";   // (/depot : autre depot, pour tester la mise a jour)
static const wchar_t *kStoreUrl = L"https://store.steampowered.com/app/4164420/";
static const float kImgW = 1000, kImgH = 620;   // mise en page (coordonnees de launcher.png)

// ---------------------------------------------------------------- etat
enum { ST_IDLE, ST_LAUNCH, ST_CLOSING };
enum { K_NORMAL, K_OK, K_WARN, K_ERR };
enum { MODE_SOLO, MODE_HOST, MODE_GUEST };
enum { REL_OFFLINE = -1, REL_NONE = 0, REL_OK = 1 };   // reponse de GitHub

static HWND g_wnd;
static bool g_fr;
static std::wstring g_dir, g_self, g_iniLauncher;   // dossier du lanceur (avec \), chemin du lanceur
static std::wstring g_gameDir, g_gameVer;           // dossier de My Winter Car (avec \, vide = introuvable), version
static std::wstring g_localVer;                     // MWCoop\version.txt
static bool g_modOk;                                // version.dll + MWCoop\MWCoop.dll presents
static int g_state = ST_IDLE;
static float g_scale = 1, g_alpha = 0, g_time = 0;
static float g_sceneT = 12;   // horloge de la scene animee (g_time est remis a zero par le curseur des champs)
static int g_bench;           // /capture ... /images n : temps de n images (journal a cote de la capture)
static Bitmap *g_bg, *g_bgDark;
static Bitmap *g_bgCache, *g_bgCacheSrc;   // fond deja a l'echelle de la fenetre (le redimensionner coutait ~30 ms par image)
static int g_winW, g_winH;
static HDC g_memDC;
static HBITMAP g_dib;
static void *g_bits;

static CRITICAL_SECTION g_cs;
static std::wstring g_status;
static int g_statusKind = K_NORMAL;
static std::atomic<float> g_progress(-1.0f);   // -1 = pas de barre, -2 = indeterminee, 0..1
static std::atomic<bool> g_busy(false);
static std::atomic<int> g_relState(REL_OFFLINE);

static HANDLE g_proc;
static DWORD g_pid, g_launchT, g_winSeenT, g_noProcT;
static std::vector<HWND> g_preWnds;                 // fenetres Unity deja la au lancement (un autre jeu sur ce PC)
static std::wstring g_launchInfo;

#define WM_APP_RELAUNCH (WM_APP + 1)
#define WM_APP_GO (WM_APP + 2)          // invite : l'hote a lance la partie
#define WM_APP_LOBBYEND (WM_APP + 3)    // invite : salon ferme (0), refuse (1) ou injoignable (2)

// Salon : etat partage entre la fenetre et les fils reseau (sous g_lcs)
enum { LB_NONE, LB_HOST, LB_CONNECTING, LB_GUEST };
enum { PARTIE_CONTINUER, PARTIE_NOUVELLE };
static const int kLobbyMax = 8;                     // joueurs dans un salon (hote compris)
// Test UDP du salon (le jeu passe en UDP sur le port du salon) : sans objet (ancien lanceur), en cours, recu, bloque.
enum { UDP_NA, UDP_WAIT, UDP_OK, UDP_FAIL };
struct LobbyPeer { int id; std::string name, skin, ver; bool ready; int ping; int udp; DWORD since; };
static std::atomic<int> g_lobby(LB_NONE);
static CRITICAL_SECTION g_lcs;
static std::vector<LobbyPeer> g_peers;
static int g_myId;
static std::atomic<int> g_partie(PARTIE_CONTINUER); // choix de l'hote (recopie chez les invites)
static std::atomic<bool> g_meReady(false);          // invite : pret
static bool g_goWait;                               // invite : GO recu, lancement dans un instant
static bool g_joinFallback;                         // invite : pas de salon chez l'hote -> rejoindre directement en jeu
static std::string g_rejectWhy;                     // invite : raison du refus (sous g_lcs)
static std::wstring g_testSalon, g_testSalonLog;    // /testsalon hote|invite <journal>
static std::wstring g_testPartie = L"continuer";    // /testsalon : choix de l'hote
static bool g_testNoUdp;                            // /testsalon hote ... /sansudp : l'hote ne repond pas en UDP
static std::atomic<int> g_udpMine(UDP_NA);          // invite : reponse UDP de l'hote a ses sondes
static std::atomic<bool> g_hostUdpTest(false);      // invite : l'hote fait le test UDP (sinon : ancien lanceur)
static std::wstring g_launchWarn;                   // ecran d'attente : avertissement (UDP non confirme)
static const wchar_t *T(const wchar_t *fr, const wchar_t *en) { return g_fr ? fr : en; }

static void SetStatus(int kind, const wchar_t *fmt, ...)
{
    wchar_t buf[512];
    va_list ap;
    va_start(ap, fmt);
    _vsnwprintf_s(buf, _countof(buf), _TRUNCATE, fmt, ap);
    va_end(ap);
    EnterCriticalSection(&g_cs);
    g_status = buf;
    g_statusKind = kind;
    LeaveCriticalSection(&g_cs);
}

// Journal du mode /testsalon (UTF-8, heure en ms) ; rien hors de ce mode.
static void TestLog(const char *fmt, ...)
{
    if (g_testSalonLog.empty()) return;
    char b[2048];
    va_list ap;
    va_start(ap, fmt);
    _vsnprintf_s(b, _countof(b), _TRUNCATE, fmt, ap);
    va_end(ap);
    FILE *f = _wfopen(g_testSalonLog.c_str(), L"ab");
    if (f) { fprintf(f, "[%lu] %s\r\n", GetTickCount(), b); fclose(f); }
}

// ---------------------------------------------------------------- utilitaires
static std::wstring Widen(const std::string &s, UINT cp = CP_UTF8)
{
    if (s.empty()) return L"";
    int n = MultiByteToWideChar(cp, 0, s.c_str(), (int)s.size(), NULL, 0);
    std::wstring w(n, 0);
    MultiByteToWideChar(cp, 0, s.c_str(), (int)s.size(), &w[0], n);
    return w;
}
static std::string Narrow(const std::wstring &w, UINT cp = CP_ACP)
{
    if (w.empty()) return "";
    int n = WideCharToMultiByte(cp, 0, w.c_str(), (int)w.size(), NULL, 0, NULL, NULL);
    std::string s(n, 0);
    WideCharToMultiByte(cp, 0, w.c_str(), (int)w.size(), &s[0], n, NULL, NULL);
    return s;
}
static bool FileExists(const std::wstring &p) { DWORD a = GetFileAttributesW(p.c_str()); return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY); }
static std::wstring DirOf(const std::wstring &p) { size_t k = p.find_last_of(L"\\/"); return k == std::wstring::npos ? L"" : p.substr(0, k + 1); }
static bool ReadAll(const std::wstring &p, std::vector<unsigned char> &out)
{
    HANDLE f = CreateFileW(p.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return false;
    DWORD size = GetFileSize(f, NULL), got = 0;
    bool ok = size != INVALID_FILE_SIZE;
    if (ok) {
        out.resize(size);
        ok = size == 0 || (ReadFile(f, out.data(), size, &got, NULL) && got == size);
    }
    CloseHandle(f);
    return ok;
}
static std::wstring Trim(std::wstring s)
{
    while (!s.empty() && (s.back() == L'\r' || s.back() == L'\n' || s.back() == L' ' || s.back() == L'\t')) s.pop_back();
    size_t k = 0;
    while (k < s.size() && (s[k] == L' ' || s[k] == L'\t' || s[k] == 0xFEFF)) k++;
    return s.substr(k);
}
static std::wstring WithSlash(std::wstring d)
{
    for (auto &c : d) if (c == L'/') c = L'\\';
    if (!d.empty() && d.back() != L'\\') d += L'\\';
    return d;
}

// ---------------------------------------------------------------- My Winter Car
static bool IsGameDir(const std::wstring &d)
{
    return !d.empty() && FileExists(d + L"mywintercar.exe") && FileExists(d + L"mywintercar_Data\\Managed\\Assembly-CSharp.dll");
}

// Bibliotheques Steam : dossiers de Steam (registre, emplacement par defaut), puis chaque "path" de leur
// steamapps\libraryfolders.vdf.
static void SteamLibraries(std::vector<std::wstring> &out)
{
    std::vector<std::wstring> roots;
    wchar_t v[MAX_PATH];
    DWORD sz = sizeof(v);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Valve\\Steam", L"SteamPath", RRF_RT_REG_SZ, NULL, v, &sz) == ERROR_SUCCESS) roots.push_back(WithSlash(v));
    sz = sizeof(v);
    if (RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\WOW6432Node\\Valve\\Steam", L"InstallPath", RRF_RT_REG_SZ, NULL, v, &sz) == ERROR_SUCCESS) roots.push_back(WithSlash(v));
    roots.push_back(L"C:\\Program Files (x86)\\Steam\\");
    auto add = [&](const std::wstring &d) {
        for (auto &o : out) if (!_wcsicmp(o.c_str(), d.c_str())) return;
        out.push_back(d);
    };
    for (const std::wstring &r : roots) {
        add(r);
        std::vector<unsigned char> d;
        if (!ReadAll(r + L"steamapps\\libraryfolders.vdf", d)) continue;
        std::string t(d.begin(), d.end());
        for (size_t p = t.find("\"path\""); p != std::string::npos; p = t.find("\"path\"", p + 6)) {
            size_t q = t.find('"', p + 6);
            if (q == std::string::npos) break;
            std::string path;
            for (size_t i = q + 1; i < t.size() && t[i] != '"' && t[i] != '\n'; i++) {
                if (t[i] == '\\' && i + 1 < t.size()) i++;   // "C:\\Steam" -> C:\Steam
                path += t[i];
            }
            if (!path.empty()) add(WithSlash(Widen(path)));
        }
    }
}

static std::wstring FindGame()
{
    if (IsGameDir(g_dir)) return g_dir;
    wchar_t saved[MAX_PATH] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Jeu", L"", saved, MAX_PATH, g_iniLauncher.c_str());
    if (saved[0] && IsGameDir(WithSlash(saved))) return WithSlash(saved);
    std::vector<std::wstring> libs;
    SteamLibraries(libs);
    for (const std::wstring &l : libs) {
        std::wstring d = l + L"steamapps\\common\\My Winter Car\\";
        if (IsGameDir(d)) return d;
    }
    return L"";
}

// Version du jeu : premiere ligne de changelog.txt ("v.260917-03").
static std::wstring ReadGameVersion()
{
    std::vector<unsigned char> d;
    if (g_gameDir.empty() || !ReadAll(g_gameDir + L"changelog.txt", d)) return L"";
    std::string t(d.begin(), d.end());
    if (t.size() >= 3 && (unsigned char)t[0] == 0xEF && (unsigned char)t[1] == 0xBB && (unsigned char)t[2] == 0xBF) t.erase(0, 3);
    std::wstring v = Trim(Widen(t.substr(0, t.find('\n'))));
    return v.size() > 32 ? v.substr(0, 32) : v;
}

// ---------------------------------------------------------------- reglages (MWCoop\mwcoop.ini, section [Coop])
struct Field { std::wstring text; RectF r; size_t maxLen; bool address; };
static Field g_fields[2];   // 0 = pseudo, 1 = adresse
static int g_focus = -1;
static int g_carColor = -1;   // [Coop] CouleurVoiture : 0xRRGGBB, -1 = au hasard (comme le jeu)

static std::wstring ModIni() { return g_gameDir + L"MWCoop\\mwcoop.ini"; }
static std::string ModIniA() { return Narrow(ModIni()); }
static void EnsureModDir() { if (!g_gameDir.empty()) CreateDirectoryW((g_gameDir + L"MWCoop").c_str(), NULL); }

// Pseudo par defaut : le nom de la session Windows (ASCII seulement, comme le champ), sinon "Joueur".
static std::wstring DefaultName()
{
    wchar_t u[128] = L"";
    DWORD n = _countof(u);
    std::wstring s;
    if (GetUserNameW(u, &n)) for (wchar_t *c = u; *c && s.size() < 23; c++) if (*c >= 32 && *c <= 126) s += *c;
    s = Trim(s);
    return s.empty() ? L"Joueur" : s;
}

static void LoadPlayer()
{
    char v[128];
    std::string ini = ModIniA();
    GetPrivateProfileStringA("Coop", "Pseudo", "", v, sizeof(v), ini.c_str());
    g_fields[0].text = v[0] ? Widen(v, CP_ACP) : DefaultName();
    GetPrivateProfileStringA("Coop", "Adresse", "", v, sizeof(v), ini.c_str());
    g_fields[1].text = Widen(v, CP_ACP);
    // CouleurVoiture=RRGGBB ("#" accepte) ; vide ou illisible : au hasard
    GetPrivateProfileStringA("Coop", "CouleurVoiture", "", v, sizeof(v), ini.c_str());
    const char *h = v[0] == '#' ? v + 1 : v;
    char *end = NULL;
    long c = strlen(h) == 6 ? strtol(h, &end, 16) : -1;
    g_carColor = (c >= 0 && end && !*end) ? (int)c : -1;
}

static std::wstring PlayerName() { std::wstring n = Trim(g_fields[0].text); return n.empty() ? DefaultName() : n; }

static void SavePlayer()
{
    if (g_gameDir.empty()) return;
    EnsureModDir();
    std::string ini = ModIniA();
    WritePrivateProfileStringA("Coop", "Pseudo", Narrow(PlayerName()).c_str(), ini.c_str());
    WritePrivateProfileStringA("Coop", "Adresse", Narrow(Trim(g_fields[1].text)).c_str(), ini.c_str());
    WritePrivateProfileStringA("Coop", "Langue", g_fr ? "fr" : "en", ini.c_str());   // textes du mod en jeu dans la langue du lanceur
}

static void LoadLocalVersion()
{
    std::vector<unsigned char> d;
    g_localVer.clear();
    g_modOk = !g_gameDir.empty() && FileExists(g_gameDir + L"version.dll") && FileExists(g_gameDir + L"MWCoop\\MWCoop.dll");
    if (g_modOk && ReadAll(g_gameDir + L"MWCoop\\version.txt", d))
        g_localVer = Trim(Widen(std::string(d.begin(), d.end())));
}

// "MWCoop 0.1.0-prealpha", "MWCoop (dev)" (fichiers du mod sans version.txt), "MWCoop".
static std::wstring ModLabel()
{
    if (!g_localVer.empty()) return L"MWCoop " + g_localVer;
    return g_modOk ? L"MWCoop (dev)" : L"MWCoop";
}

static void SetGame(const std::wstring &dir)
{
    g_gameDir = dir;
    g_gameVer = ReadGameVersion();
    LoadLocalVersion();
    if (!g_gameDir.empty()) LoadPlayer();
}

// "0.1.0-prealpha" : nombres compares un a un, puis le suffixe (a egalite, sans suffixe = plus recent).
static int CmpVer(std::wstring a, std::wstring b)
{
    if (!a.empty() && (a[0] == L'v' || a[0] == L'V')) a.erase(0, 1);
    if (!b.empty() && (b[0] == L'v' || b[0] == L'V')) b.erase(0, 1);
    size_t i = 0, j = 0;
    while (i < a.size() || j < b.size()) {
        bool da = i < a.size() && iswdigit(a[i]), db = j < b.size() && iswdigit(b[j]);
        if (!da || !db) break;
        long na = wcstol(a.c_str() + i, NULL, 10), nb = wcstol(b.c_str() + j, NULL, 10);
        if (na != nb) return na < nb ? -1 : 1;
        while (i < a.size() && iswdigit(a[i])) i++;
        while (j < b.size() && iswdigit(b[j])) j++;
        if (i < a.size() && a[i] == L'.' && j < b.size() && b[j] == L'.') { i++; j++; continue; }
        break;
    }
    std::wstring sa = a.substr(min(i, a.size())), sb = b.substr(min(j, b.size()));
    if (sa == sb) return 0;
    if (sa.empty() != sb.empty()) return sa.empty() ? 1 : -1;
    return sa < sb ? -1 : 1;
}

// ---------------------------------------------------------------- HTTP (WinHTTP)
// GET sur une URL https ; corps dans out (ou dans le fichier toFile), progression 0..1 si progress ; status : code HTTP
// (0 = pas de reponse).
static bool HttpGet(const std::wstring &url, std::string *out, const std::wstring &toFile, bool progress, DWORD *status = NULL)
{
    if (status) *status = 0;
    URL_COMPONENTS uc = { sizeof(uc) };
    wchar_t host[256] = {}, path[2048] = {};
    uc.lpszHostName = host; uc.dwHostNameLength = _countof(host);
    uc.lpszUrlPath = path; uc.dwUrlPathLength = _countof(path);
    wchar_t extra[1024] = {};
    uc.lpszExtraInfo = extra; uc.dwExtraInfoLength = _countof(extra);
    if (!WinHttpCrackUrl(url.c_str(), 0, 0, &uc)) return false;
    std::wstring full = std::wstring(path) + extra;

    HINTERNET s = WinHttpOpen(L"MWCoop-Launcher", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, NULL, NULL, 0);
    if (!s) s = WinHttpOpen(L"MWCoop-Launcher", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, NULL, NULL, 0);
    if (!s) return false;
    WinHttpSetTimeouts(s, 8000, 8000, 15000, 30000);
    bool ok = false;
    HANDLE f = INVALID_HANDLE_VALUE;
    HINTERNET c = WinHttpConnect(s, host, uc.nPort, 0);
    HINTERNET r = c ? WinHttpOpenRequest(c, L"GET", full.c_str(), NULL, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES,
                                         uc.nScheme == INTERNET_SCHEME_HTTPS ? WINHTTP_FLAG_SECURE : 0) : NULL;
    // en-tete de l'API seulement pour l'API (le zip : requete nue, comme un navigateur)
    const wchar_t *hdr = toFile.empty() ? L"Accept: application/vnd.github+json\r\n" : WINHTTP_NO_ADDITIONAL_HEADERS;
    if (r && WinHttpSendRequest(r, hdr, toFile.empty() ? (DWORD)-1 : 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0)
          && WinHttpReceiveResponse(r, NULL)) {
        DWORD code = 0, len = sizeof(code);
        WinHttpQueryHeaders(r, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &code, &len, WINHTTP_NO_HEADER_INDEX);
        if (status) *status = code;
        DWORD total = 0; len = sizeof(total);
        if (!WinHttpQueryHeaders(r, WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &total, &len, WINHTTP_NO_HEADER_INDEX)) total = 0;
        if (code == 200) {
            if (!toFile.empty()) f = CreateFileW(toFile.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
            ok = toFile.empty() || f != INVALID_HANDLE_VALUE;
            DWORD got = 0;
            std::vector<char> buf(64 * 1024);
            while (ok) {
                DWORD n = 0;
                if (!WinHttpReadData(r, buf.data(), (DWORD)buf.size(), &n)) { ok = false; break; }
                if (!n) break;
                if (f != INVALID_HANDLE_VALUE) { DWORD w = 0; if (!WriteFile(f, buf.data(), n, &w, NULL) || w != n) ok = false; }
                else out->append(buf.data(), n);
                got += n;
                if (progress && total) g_progress = (float)got / total;
            }
            if (ok && total && got != total) ok = false;
        }
    }
    if (f != INVALID_HANDLE_VALUE) CloseHandle(f);
    if (r) WinHttpCloseHandle(r);
    if (c) WinHttpCloseHandle(c);
    WinHttpCloseHandle(s);
    return ok;
}

// Chaine JSON a partir de son guillemet ouvrant, echappements decodes (en UTF-8).
static std::string JsonDecode(const std::string &json, size_t q)
{
    std::string v;
    for (size_t i = q + 1; i < json.size() && json[i] != '"'; i++) {
        char c = json[i];
        if (c != '\\' || i + 1 >= json.size()) { v += c; continue; }
        char e = json[++i];
        if (e == 'n') v += '\n';
        else if (e == 'r') {}
        else if (e == 't') v += ' ';
        else if (e == 'u' && i + 4 < json.size()) {
            unsigned cp = strtoul(json.substr(i + 1, 4).c_str(), NULL, 16);
            i += 4;
            if (cp >= 0xD800 && cp <= 0xDFFF) continue;   // emoji (paires) : laisses
            if (cp < 0x80) v += (char)cp;
            else if (cp < 0x800) { v += (char)(0xC0 | (cp >> 6)); v += (char)(0x80 | (cp & 0x3F)); }
            else { v += (char)(0xE0 | (cp >> 12)); v += (char)(0x80 | ((cp >> 6) & 0x3F)); v += (char)(0x80 | (cp & 0x3F)); }
        } else v += e;
    }
    return v;
}
// Valeur texte d'une cle, cherchee dans [from, to).
static std::string JsonField(const std::string &json, const char *key, size_t from, size_t to)
{
    std::string k = std::string("\"") + key + "\"";
    size_t p = json.find(k, from);
    if (p == std::string::npos || p >= to) return "";
    p = json.find(':', p + k.size());
    size_t q = p == std::string::npos ? p : json.find_first_not_of(" \t\r\n", p + 1);
    return q != std::string::npos && json[q] == '"' ? JsonDecode(json, q) : "";
}

// ---------------------------------------------------------------- installation
static bool RunHidden(const std::wstring &cmd, DWORD *exitCode)
{
    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi;
    std::vector<wchar_t> c(cmd.begin(), cmd.end());
    c.push_back(0);
    if (!CreateProcessW(NULL, c.data(), NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) return false;
    WaitForSingleObject(pi.hProcess, 120000);
    GetExitCodeProcess(pi.hProcess, exitCode);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return true;
}

static void DeleteTree(const std::wstring &dir)
{
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((dir + L"\\*").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            std::wstring n = fd.cFileName;
            if (n == L"." || n == L"..") continue;
            std::wstring p = dir + L"\\" + n;
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) DeleteTree(p);
            else { SetFileAttributesW(p.c_str(), FILE_ATTRIBUTE_NORMAL); DeleteFileW(p.c_str()); }
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    RemoveDirectoryW(dir.c_str());
}

// mwcoop.ini deja la : garde les valeurs du joueur, ajoute seulement les cles apparues dans la nouvelle version.
static void MergeIni(const std::wstring &newIni, const std::wstring &userIni)
{
    std::vector<unsigned char> d;
    if (!ReadAll(newIni, d)) return;
    std::string text(d.begin(), d.end()), section, u = Narrow(userIni);
    if (text.size() >= 3 && (unsigned char)text[0] == 0xEF && (unsigned char)text[1] == 0xBB && (unsigned char)text[2] == 0xBF) text.erase(0, 3);
    size_t p = 0;
    while (p < text.size()) {
        size_t e = text.find('\n', p);
        if (e == std::string::npos) e = text.size();
        std::string line = text.substr(p, e - p);
        p = e + 1;
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
        if (line.empty() || line[0] == ';' || line[0] == '#') continue;
        if (line[0] == '[') { section = line.substr(1, line.find(']') - 1); continue; }
        size_t eq = line.find('=');
        if (eq == std::string::npos || section.empty()) continue;
        std::string key = line.substr(0, eq), val = line.substr(eq + 1);
        char cur[8];
        GetPrivateProfileStringA(section.c_str(), key.c_str(), "\x01", cur, sizeof(cur), u.c_str());
        if (cur[0] == 1 && !cur[1]) WritePrivateProfileStringA(section.c_str(), key.c_str(), val.c_str(), u.c_str());
    }
}

// Copie l'arbre extrait dans le dossier du jeu. selfReplaced : le lanceur en cours a ete remplace.
static DWORD g_copyError;
static bool CopyTree(const std::wstring &src, const std::wstring &dst, bool *selfReplaced, std::wstring *failed)
{
    CreateDirectoryW(dst.c_str(), NULL);
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((src + L"\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return true;
    bool ok = true;
    do {
        std::wstring n = fd.cFileName;
        if (n == L"." || n == L"..") continue;
        std::wstring s = src + L"\\" + n, d = dst + L"\\" + n;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) { ok = CopyTree(s, d, selfReplaced, failed) && ok; continue; }
        if (!_wcsicmp(d.c_str(), ModIni().c_str()) && FileExists(d)) { MergeIni(s, d); continue; }
        if (!_wcsicmp(d.c_str(), (g_gameDir + L"MWCoop\\version.txt").c_str())) continue;   // en dernier (UpdateThread)
        if (!_wcsicmp(d.c_str(), g_self.c_str())) {
            std::wstring old = g_self + L".old";
            DeleteFileW(old.c_str());
            if (!MoveFileExW(g_self.c_str(), old.c_str(), MOVEFILE_REPLACE_EXISTING)) { g_copyError = GetLastError(); ok = false; *failed = n; continue; }
            *selfReplaced = true;
        }
        SetFileAttributesW(d.c_str(), FILE_ATTRIBUTE_NORMAL);
        if (!CopyFileW(s.c_str(), d.c_str(), FALSE)) { g_copyError = GetLastError(); ok = false; *failed = n; }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return ok;
}

// Le dossier accepte-t-il l'ecriture (Program Files sans droits, par exemple) ?
static bool IsWritableDir(const std::wstring &dir)
{
    std::wstring p = dir + L"mwcoop-ecriture.tmp";
    HANDLE f = CreateFileW(p.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, NULL);
    if (f == INVALID_HANDLE_VALUE) return false;
    CloseHandle(f);
    return true;
}

// ---------------------------------------------------------------- notes des versions (onglet NOUVEAUTES)
// Texte de chaque release GitHub : en francais, puis une ligne "---", puis en anglais. Gardees dans
// <jeu>\MWCoop\notes-maj.json pour les lire hors ligne.
struct Note { std::wstring ver, date, fr, en, zip; };
static std::vector<Note> g_notes;
static volatile bool g_notesDone;
static float g_notesH;

// Markdown simple : titres, gras et code retires ; puces "- " -> "\u2022".
static std::wstring CleanNote(const std::wstring &s)
{
    std::wstring o;
    for (size_t i = 0; i < s.size(); i++) {
        bool lineStart = i == 0 || s[i - 1] == L'\n';
        if (s[i] == L'`') continue;
        if (s[i] == L'*' && i + 1 < s.size() && s[i + 1] == L'*') { i++; continue; }
        if (lineStart && s[i] == L'#') { while (i < s.size() && (s[i] == L'#' || s[i] == L' ')) i++; i--; continue; }
        if (lineStart && (s[i] == L'-' || s[i] == L'*') && i + 1 < s.size() && s[i + 1] == L' ') { o += L"\u2022"; continue; }
        o += s[i];
    }
    size_t b = o.find_first_not_of(L"\n "), e = o.find_last_not_of(L"\n ");
    return b == std::wstring::npos ? L"" : o.substr(b, e - b + 1);
}

// Liste des releases (les plus recentes d'abord), brouillons exclus.
static std::vector<Note> ParseReleases(const std::string &json)
{
    std::vector<Note> list;
    for (size_t at = 0;;) {
        size_t p = json.find("\"tag_name\"", at);
        if (p == std::string::npos) break;
        size_t next = json.find("\"tag_name\"", p + 10), end = next == std::string::npos ? json.size() : next;
        // l'objet de la release commence avant "tag_name" (url, id...) : on cherche les assets dans [p, fin)
        Note n;
        n.ver = Widen(JsonField(json, "tag_name", p, end));
        if (!n.ver.empty() && (n.ver[0] == L'v' || n.ver[0] == L'V')) n.ver.erase(0, 1);
        std::string d = JsonField(json, "published_at", p, end);
        if (d.size() >= 10) n.date = Widen(d.substr(8, 2) + "/" + d.substr(5, 2) + "/" + d.substr(0, 4));
        std::wstring body = Widen(JsonField(json, "body", p, end));
        size_t sep = body.find(L"\n---");
        if (sep == std::wstring::npos) n.fr = n.en = CleanNote(body);
        else {
            size_t enAt = body.find(L'\n', sep + 1);
            n.fr = CleanNote(body.substr(0, sep));
            n.en = CleanNote(enAt == std::wstring::npos ? L"" : body.substr(enAt + 1));
            if (n.en.empty()) n.en = n.fr;
        }
        for (size_t k = p; k < end;) {
            size_t u = json.find("\"browser_download_url\"", k);
            if (u == std::string::npos || u >= end) break;
            std::string url = JsonField(json, "browser_download_url", u, end);
            k = u + 20;
            if (url.size() > 4 && !_stricmp(url.c_str() + url.size() - 4, ".zip")) { n.zip = Widen(url); break; }
        }
        bool draft = json.find("\"draft\": true", p) < end || json.find("\"draft\":true", p) < end;
        if (!n.ver.empty() && !draft) list.push_back(n);
        at = end;
    }
    return list;
}

// REL_OK : liste recue ; REL_NONE : GitHub repond, mais aucune release (ou depot pas encore public : 404) ;
// REL_OFFLINE : pas de reponse (json : le cache, pour les notes seulement).
static int FetchReleases(std::string &json)
{
    std::wstring cache = g_gameDir.empty() ? L"" : g_gameDir + L"MWCoop\\notes-maj.json";
    DWORD code = 0;
    bool got = HttpGet(L"https://api.github.com/repos/" + g_repo + L"/releases?per_page=40", &json, L"", false, &code);
    if (got && json.find("\"tag_name\"") != std::string::npos) {
        if (!cache.empty()) {
            EnsureModDir();
            HANDLE f = CreateFileW(cache.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
            if (f != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(f, json.data(), (DWORD)json.size(), &w, NULL); CloseHandle(f); }
        }
        return REL_OK;
    }
    json.clear();
    if ((got && code == 200) || code == 404) return REL_NONE;
    if (!cache.empty()) {
        HANDLE f = CreateFileW(cache.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
        if (f != INVALID_HANDLE_VALUE) {
            DWORD size = GetFileSize(f, NULL), r = 0;
            if (size != INVALID_FILE_SIZE && size < 8u << 20) { json.resize(size); ReadFile(f, &json[0], size, &r, NULL); json.resize(r); }
            CloseHandle(f);
        }
    }
    return REL_OFFLINE;
}

// ---------------------------------------------------------------- mise a jour
static DWORD WINAPI UpdateThread(void *)
{
    SetStatus(K_NORMAL, T(L"Recherche de mises \u00E0 jour\u2026", L"Checking for updates\u2026"));
    g_progress = -2;
    std::string json;
    std::wstring local = g_localVer, label = ModLabel();
    bool mod = g_modOk;
    int rs = FetchReleases(json);
    std::vector<Note> rel = json.empty() ? std::vector<Note>() : ParseReleases(json);
    EnterCriticalSection(&g_cs);
    g_notes = rel;
    LeaveCriticalSection(&g_cs);
    g_relState = rs;
    g_notesDone = true;
    if (rs == REL_OFFLINE) {
        g_progress = -1;
        if (!mod) SetStatus(K_ERR, T(L"Hors ligne : MWCoop n'est pas install\u00E9 ici", L"Offline: MWCoop is not installed here"));
        else SetStatus(K_WARN, T(L"Hors ligne \u00B7 %s", L"Offline \u00B7 %s"), label.c_str());
        g_busy = false;
        return 0;
    }
    std::wstring remote, zipUrl;
    for (const Note &n : rel) if (!n.zip.empty()) { remote = n.ver; zipUrl = n.zip; break; }   // la plus recente avec un zip
    if (remote.empty() || zipUrl.empty()) {   // pas encore de release : rien a installer
        g_progress = -1;
        if (!mod) SetStatus(K_WARN, T(L"Aucune version de MWCoop publi\u00E9e pour l'instant", L"No MWCoop release published yet"));
        else SetStatus(K_OK, T(L"%s \u00B7 aucune version publi\u00E9e", L"%s \u00B7 no release published"), label.c_str());
        g_busy = false;
        return 0;
    }
    if (!local.empty() && CmpVer(remote, local) <= 0) {
        g_progress = -1;
        SetStatus(K_OK, T(L"%s \u00B7 \u00E0 jour", L"%s \u00B7 up to date"), label.c_str());
        g_busy = false;
        return 0;
    }

    wchar_t tmp[MAX_PATH];
    GetTempPathW(MAX_PATH, tmp);
    // Dossier propre a ce lanceur (un reste verrouille d'une tentative precedente faisait echouer le telechargement)
    std::wstring work = std::wstring(tmp) + L"MWCoop-maj-" + std::to_wstring(GetCurrentProcessId());
    DeleteTree(work);
    CreateDirectoryW(work.c_str(), NULL);
    std::wstring zip = work + L"\\MWCoop.zip", ext = work + L"\\x";
    SetStatus(K_NORMAL, T(L"T\u00E9l\u00E9chargement de MWCoop %s\u2026", L"Downloading MWCoop %s\u2026"), remote.c_str());
    bool got = false;
    DWORD err = 0;
    for (int attempt = 0; attempt < 3 && !got; attempt++) {   // trois essais (serveurs de GitHub parfois lents a repondre)
        if (attempt) {
            SetStatus(K_NORMAL, T(L"T\u00E9l\u00E9chargement de MWCoop %s\u2026 (essai %d / 3)", L"Downloading MWCoop %s\u2026 (try %d / 3)"), remote.c_str(), attempt + 1);
            Sleep(1500);
        }
        g_progress = 0;
        got = HttpGet(zipUrl, NULL, zip, true);
        if (!got) err = GetLastError();
    }
    if (!got) {
        g_progress = -1;
        SetStatus(K_ERR, T(L"Mise \u00E0 jour impossible (t\u00E9l\u00E9chargement, erreur %lu)", L"Update failed (download, error %lu)"), err);
        DeleteTree(work);
        g_busy = false;
        return 0;
    }
    SetStatus(K_NORMAL, T(L"Installation de MWCoop %s\u2026", L"Installing MWCoop %s\u2026"), remote.c_str());
    g_progress = -2;
    CreateDirectoryW(ext.c_str(), NULL);
    wchar_t sys[MAX_PATH];
    GetSystemDirectoryW(sys, MAX_PATH);
    DWORD code = 1;
    std::wstring cmd = L"\"" + std::wstring(sys) + L"\\tar.exe\" -xf \"" + zip + L"\" -C \"" + ext + L"\"";
    if (!RunHidden(cmd, &code) || code != 0) {
        g_progress = -1;
        SetStatus(K_ERR, T(L"Mise \u00E0 jour impossible (archive)", L"Update failed (archive)"));
        DeleteTree(work);
        g_busy = false;
        return 0;
    }
    if (!FileExists(ext + L"\\MWCoop\\version.txt")) {   // pas un paquet de MWCoop (ou zip range dans un sous-dossier)
        g_progress = -1;
        SetStatus(K_ERR, T(L"Mise \u00E0 jour impossible (paquet incomplet : MWCoop\\version.txt manquant)", L"Update failed (incomplete package: MWCoop\\version.txt missing)"));
        DeleteTree(work);
        g_busy = false;
        return 0;
    }
    bool self = false;
    std::wstring failed;
    std::wstring gd =g_gameDir.substr(0, g_gameDir.size() - 1);
    bool ok = CopyTree(ext, gd, &self, &failed);
    // version.txt seulement si tout est en place : une installation interrompue sera reprise au prochain lancement
    CreateDirectoryW((g_gameDir + L"MWCoop").c_str(), NULL);
    if (ok && !CopyFileW((ext + L"\\MWCoop\\version.txt").c_str(), (g_gameDir + L"MWCoop\\version.txt").c_str(), FALSE)) { ok = false; failed = L"version.txt"; }
    DeleteTree(work);
    g_progress = -1;
    if (!ok && g_copyError == ERROR_ACCESS_DENIED && !IsWritableDir(g_gameDir)) {
        SetStatus(K_ERR, T(L"Dossier du jeu prot\u00E9g\u00E9 : lance MWCoop.exe en administrateur pour mettre \u00E0 jour", L"Game folder is protected: run MWCoop.exe as administrator to update"));
        g_busy = false;
        return 0;
    }
    if (!ok) {
        SetStatus(K_ERR, T(L"%s est occup\u00E9 : ferme le jeu, puis relance le lanceur", L"%s is in use: close the game, then restart the launcher"), failed.c_str());
        g_busy = false;
        return 0;
    }
    LoadLocalVersion();
    SetStatus(K_OK, T(L"Mis \u00E0 jour \u00B7 MWCoop %s", L"Updated \u00B7 MWCoop %s"), g_localVer.empty() ? remote.c_str() : g_localVer.c_str());
    g_busy = false;
    if (self) PostMessageW(g_wnd, WM_APP_RELAUNCH, 0, 0);
    return 0;
}

// Sans jeu, ou mise a jour coupee (MajAuto=0 dans mwcoop-lanceur.ini) : les notes quand meme.
static DWORD WINAPI NotesOnlyThread(void *)
{
    std::string json;
    int rs = FetchReleases(json);
    std::vector<Note> rel = json.empty() ? std::vector<Note>() : ParseReleases(json);
    EnterCriticalSection(&g_cs);
    g_notes = rel;
    LeaveCriticalSection(&g_cs);
    g_relState = rs;
    g_notesDone = true;
    return 0;
}

static void StartUpdate()
{
    if (g_gameDir.empty() || g_busy) return;
    bool autoUpdate = GetPrivateProfileIntW(L"Lanceur", L"MajAuto", 1, g_iniLauncher.c_str()) != 0;
    if (!autoUpdate) {
        SetStatus(K_NORMAL, T(L"%s \u00B7 mise \u00E0 jour automatique coup\u00E9e", L"%s \u00B7 automatic update disabled"), ModLabel().c_str());
        HANDLE nt = CreateThread(NULL, 0, NotesOnlyThread, NULL, 0, NULL);
        if (nt) CloseHandle(nt);
        return;
    }
    g_busy = true;
    HANDLE t = CreateThread(NULL, 0, UpdateThread, NULL, 0, NULL);
    if (t) CloseHandle(t); else g_busy = false;
}

// ---------------------------------------------------------------- boutons
// Onglets du panneau de droite (TAB_LOGS : page du bouton journaux, pas d'onglet ; TAB_LOBBY : pendant un salon)
enum { TAB_COOP, TAB_NOTES, TAB_LOGS, TAB_CAR, TAB_LOBBY, TAB_SKIN, TAB_COUNT };
static int g_tab = -1;

enum { B_HOST, B_JOIN, B_SOLO, B_EXE, B_BUY, B_THEME, B_CLOSE, B_MIN, B_LOGS, B_COLOR, B_LOGDIR, B_LOGZIP, B_COUNT };
struct Button { RectF r; float hover; bool visible, enabled; };
static Button g_btn[B_COUNT];
static int g_hot = -1, g_pressed = -1;

static void Layout()
{
    g_fields[0].r = RectF(76, 276, 304, 36); g_fields[0].maxLen = 23; g_fields[0].address = false;
    g_fields[1].r = RectF(76, 338, 304, 36); g_fields[1].maxLen = 63; g_fields[1].address = true;
    g_btn[B_HOST].r = RectF(76, 390, 148, 46);
    g_btn[B_JOIN].r = RectF(232, 390, 148, 46);
    g_btn[B_SOLO].r = RectF(76, 444, 304, 26);
    g_btn[B_EXE].r = RectF(272, 478, 108, 20);
    g_btn[B_BUY].r = RectF(266, 554, 114, 26);
    g_btn[B_CLOSE].r = RectF(938, 76, 28, 28);
    g_btn[B_MIN].r = RectF(904, 76, 28, 28);
    g_btn[B_THEME].r = RectF(62, 100, 26, 26);   // coin du panneau, a gauche du logo
    g_btn[B_LOGS].r = RectF(368, 100, 26, 26);   // coin oppose : page des journaux
    g_btn[B_COLOR].r = RectF(756, 439, 180, 30);  // onglet VOITURE : "Autre couleur..."
    g_btn[B_LOGDIR].r = RectF(616, 122, 136, 26); // page JOURNAUX : ouvrir le dossier, zip a envoyer
    g_btn[B_LOGZIP].r = RectF(760, 122, 176, 26);
}

static void UpdateButtons()
{
    bool menu = g_state == ST_IDLE, game = !g_gameDir.empty(), busy = g_busy;
    int lobby = g_lobby;
    for (int i = 0; i < B_COUNT; i++) g_btn[i].visible = true;
    g_btn[B_HOST].visible = g_btn[B_JOIN].visible = g_btn[B_SOLO].visible = g_btn[B_EXE].visible = menu;
    g_btn[B_SOLO].visible = g_btn[B_EXE].visible = menu && lobby == LB_NONE && !g_goWait;   // (pendant un salon : caches)
    bool can = menu && game && !busy && g_modOk && !g_goWait;
    g_btn[B_HOST].enabled = can && lobby != LB_CONNECTING;   // HEBERGER, LANCER (hote) ou PRET (invite)
    g_btn[B_JOIN].enabled = can;                             // REJOINDRE, FERMER LE SALON, QUITTER ou ANNULER
    g_btn[B_SOLO].enabled = can;
    g_btn[B_EXE].enabled = menu && !busy;
    g_btn[B_CLOSE].enabled = g_btn[B_MIN].enabled = g_btn[B_BUY].enabled = g_btn[B_THEME].enabled = true;
    g_btn[B_LOGS].visible = menu && game;
    g_btn[B_LOGS].enabled = true;
    g_btn[B_COLOR].visible = menu && game && g_tab == TAB_CAR;
    g_btn[B_COLOR].enabled = true;
    g_btn[B_LOGDIR].visible = g_btn[B_LOGZIP].visible = menu && game && g_tab == TAB_LOGS;
    g_btn[B_LOGDIR].enabled = g_btn[B_LOGZIP].enabled = true;
}

// ---------------------------------------------------------------- dessin
static void RoundRect(GraphicsPath &p, RectF r, float rad)
{
    float d = rad * 2;
    p.AddArc(r.X, r.Y, d, d, 180, 90);
    p.AddArc(r.X + r.Width - d, r.Y, d, d, 270, 90);
    p.AddArc(r.X + r.Width - d, r.Y + r.Height - d, d, d, 0, 90);
    p.AddArc(r.X, r.Y + r.Height - d, d, d, 90, 90);
    p.CloseFigure();
}

// Hiver : theme sombre = bleu nuit, textes blancs, accents bleu glacier ; theme clair = blanc neige, textes bleu
// nuit, accents bleu profond. Pas de vert (JD). Seuls les etats gardent une couleur (erreur en rouge, attention en
// orange, PRE-ALPHA en rouge). Themes : bouton lune / soleil ; Theme=clair|sombre dans mwcoop-lanceur.ini, sinon
// celui de Windows.
struct Theme {
    Color ink, grey, panel, panelBorder, sep, card, cardSel, choiceBorder, toggleOff, field, fieldBorder, placeholder,
          tab, tabHot, pill, btn2, btn2Hot, circle, circleHot, fallA, fallB, accent, accent2, onAccent, pillHot;
};
static const Theme kLight = {
    Color(255, 16, 30, 48), Color(255, 92, 108, 128), Color(255, 248, 251, 254), Color(150, 255, 255, 255), Color(255, 218, 228, 238),
    Color(255, 255, 255, 255), Color(255, 230, 240, 250), Color(255, 194, 210, 228), Color(255, 204, 214, 226),
    Color(235, 255, 255, 255), Color(255, 198, 212, 228), Color(255, 160, 174, 190),
    Color(185, 255, 255, 255), Color(240, 255, 255, 255), Color(235, 20, 40, 64), Color(215, 255, 255, 255), Color(240, 232, 242, 252),
    Color(150, 255, 255, 255), Color(235, 255, 255, 255), Color(255, 246, 250, 254), Color(255, 205, 220, 236),
    Color(255, 26, 98, 172), Color(255, 70, 152, 220), Color(255, 255, 255, 255), Color(255, 42, 84, 128) };
static const Theme kDark = {
    Color(255, 236, 244, 252), Color(255, 138, 158, 182), Color(255, 12, 20, 34), Color(60, 200, 225, 255), Color(255, 36, 50, 70),
    Color(255, 22, 34, 52), Color(255, 34, 50, 74), Color(255, 62, 84, 112), Color(255, 52, 66, 86),
    Color(235, 16, 26, 42), Color(255, 58, 78, 104), Color(255, 96, 114, 138),
    Color(200, 18, 30, 48), Color(240, 34, 50, 74), Color(235, 38, 66, 100), Color(215, 14, 24, 40), Color(240, 30, 46, 70),
    Color(170, 18, 30, 48), Color(235, 38, 56, 84), Color(255, 10, 18, 34), Color(255, 24, 40, 64),
    Color(255, 150, 208, 255), Color(255, 232, 244, 255), Color(255, 10, 22, 40), Color(255, 58, 98, 140) };
static bool g_dark;
#define TH(x) ((g_dark ? kDark : kLight).x)
#define kInk TH(ink)
#define kGrey TH(grey)
#define kAcc TH(accent)
#define kAcc2 TH(accent2)
#define kOnAcc TH(onAccent)
static const Color kRed(255, 214, 48, 72);

static Color Mix(Color a, Color b, float t)
{
    auto L = [&](BYTE x, BYTE y) { return (BYTE)(x + (y - x) * t); };
    return Color(L(a.GetA(), b.GetA()), L(a.GetR(), b.GetR()), L(a.GetG(), b.GetG()), L(a.GetB(), b.GetB()));
}
static Color WithA(Color c, float a) { return Color((BYTE)(c.GetA() * a), c.GetR(), c.GetG(), c.GetB()); }

static void Text(Graphics &g, const std::wstring &s, RectF r, float px, int style, Color c, StringAlignment h = StringAlignmentCenter)
{
    FontFamily fam(L"Segoe UI");
    Font font(&fam, px, style, UnitPixel);
    StringFormat sf;
    sf.SetAlignment(h);
    sf.SetLineAlignment(StringAlignmentCenter);
    sf.SetTrimming(StringTrimmingEllipsisCharacter);
    sf.SetFormatFlags(StringFormatFlagsNoWrap);
    SolidBrush b(c);
    g.DrawString(s.c_str(), -1, &font, r, &sf, &b);
}

// Texte sur plusieurs lignes (coupe aux mots), en haut du cadre.
static void Para(Graphics &g, const std::wstring &s, RectF r, float px, Color c, StringAlignment v = StringAlignmentNear)
{
    FontFamily fam(L"Segoe UI");
    Font font(&fam, px, FontStyleRegular, UnitPixel);
    StringFormat sf;
    sf.SetLineAlignment(v);
    sf.SetTrimming(StringTrimmingEllipsisWord);
    SolidBrush b(c);
    g.DrawString(s.c_str(), -1, &font, r, &sf, &b);
}

static float MeasureW(Graphics &g, const std::wstring &s, float px, int style)
{
    FontFamily fam(L"Segoe UI");
    Font font(&fam, px, style, UnitPixel);
    RectF box;
    g.MeasureString(s.c_str(), -1, &font, PointF(0, 0), &box);
    return box.Width;
}

static void DrawButton(Graphics &g, int id, const wchar_t *label, bool primary)
{
    Button &b = g_btn[id];
    if (!b.visible) return;
    float a = b.enabled ? 1.0f : 0.38f;
    RectF r = b.r;
    if (g_pressed == id && g_hot == id) r.Offset(0, 1);
    GraphicsPath p;
    RoundRect(p, r, 12);
    if (primary) {
        GraphicsPath sp;   // ombre
        RoundRect(sp, RectF(r.X + 2, r.Y + 5, r.Width - 4, r.Height), 12);
        SolidBrush sb(Color((BYTE)(45 * a), 0, 0, 0));
        g.FillPath(&sb, &sp);
        LinearGradientBrush lg(r, WithA(kAcc, a), WithA(kAcc2, a), LinearGradientModeHorizontal);
        g.FillPath(&lg, &p);
        SolidBrush hi(Color((BYTE)(60 * b.hover * a), 255, 255, 255));
        g.FillPath(&hi, &p);
    } else {
        SolidBrush fill(Mix(WithA(TH(btn2), a), WithA(TH(btn2Hot), a), b.hover));
        g.FillPath(&fill, &p);
        Pen pen(WithA(kAcc, a), 1.6f);
        g.DrawPath(&pen, &p);
    }
    float px = 15;   // libelles longs (FERMER LE SALON, REJOINDRE EN JEU) : un peu plus petits
    while (px > 11 && MeasureW(g, label, px, FontStyleBold) > r.Width - 14) px -= 0.5f;
    Text(g, label, r, px, FontStyleBold, WithA(primary ? kOnAcc : kAcc, a));
}

// Petit bouton (Jouer en solo) : pilule discrete, accent au survol.
static void DrawSmallButton(Graphics &g, int id, const wchar_t *label)
{
    Button &b = g_btn[id];
    if (!b.visible) return;
    float a = b.enabled ? 1.0f : 0.38f;
    RectF r = b.r;
    if (g_pressed == id && g_hot == id) r.Offset(0, 1);
    GraphicsPath p;
    RoundRect(p, r, r.Height / 2);
    SolidBrush fill(Mix(WithA(TH(btn2), a * 0.75f), WithA(TH(btn2Hot), a), b.hover));
    g.FillPath(&fill, &p);
    Color c = WithA(Mix(kGrey, kAcc, b.hover), a);
    Pen pen(WithA(c, 0.7f), 1.1f);
    g.DrawPath(&pen, &p);
    Text(g, label, r, 12, FontStyleBold, c);
}

static void DrawField(Graphics &g, int i, const wchar_t *label)
{
    Field &f = g_fields[i];
    Text(g, label, RectF(f.r.X + 2, f.r.Y - 18, f.r.Width, 16), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
    GraphicsPath p;
    RoundRect(p, f.r, 9);
    SolidBrush fill(TH(field));
    g.FillPath(&fill, &p);
    Pen pen(g_focus == i ? kAcc : TH(fieldBorder), g_focus == i ? 2.0f : 1.2f);
    g.DrawPath(&pen, &p);
    RectF tr(f.r.X + 12, f.r.Y, f.r.Width - 24, f.r.Height);
    std::wstring shown = f.text;
    bool placeholder = shown.empty() && g_focus != i;
    if (placeholder) shown = f.address ? T(L"ex. 26.12.34.56 (adresse:port accept\u00E9)", L"e.g. 26.12.34.56 (address:port accepted)") : DefaultName();
    Text(g, shown, tr, placeholder && f.address ? 13.0f : 15.0f, FontStyleRegular, placeholder ? TH(placeholder) : kInk, StringAlignmentNear);
    if (g_focus == i && fmodf(g_time, 1.0f) < 0.55f) {
        FontFamily fam(L"Segoe UI");
        Font font(&fam, 15, FontStyleRegular, UnitPixel);
        StringFormat sf(StringFormat::GenericTypographic());
        sf.SetFormatFlags(StringFormatFlagsMeasureTrailingSpaces | StringFormatFlagsNoWrap);
        RectF box;
        g.MeasureString(f.text.c_str(), -1, &font, PointF(0, 0), &sf, &box);
        float x = min(tr.X + box.Width + 1, tr.X + tr.Width);
        Pen cp(kAcc, 1.6f);
        g.DrawLine(&cp, x, f.r.Y + 9, x, f.r.Y + f.r.Height - 9);
    }
}

static void DrawBar(Graphics &g, RectF r, float p)
{
    GraphicsPath bg;
    RoundRect(bg, r, r.Height / 2);
    SolidBrush b(WithA(kAcc, 0.22f));
    g.FillPath(&b, &bg);
    RectF fr = r;
    if (p >= 0) fr.Width = max(r.Height, r.Width * min(p, 1.0f));
    else {   // indeterminee : un segment qui glisse
        float w = r.Width * 0.3f, t = fmodf(g_time * 0.8f, 1.0f);
        fr.X = r.X - w + (r.Width + w) * t;
        fr.Width = w;
        g.SetClip(&bg);
    }
    GraphicsPath fp;
    RoundRect(fp, fr, r.Height / 2);
    LinearGradientBrush lg(RectF(fr.X - 1, fr.Y, fr.Width + 2, fr.Height), kAcc, kAcc2, LinearGradientModeHorizontal);
    g.FillPath(&lg, &fp);
    g.ResetClip();
}

// ---------------------------------------------------------------- options (MWCoop\mwcoop.ini, [Coop])
// Memes cles et valeurs par defaut que le mod (Net\Session.cs) ; ecrites tout de suite, prises au prochain lancement.
enum { O_TOGGLE, O_CHOICE };
struct Opt {
    int tab; const char *key; int def; int kind; std::vector<int> vals;
    std::vector<std::string> svals;              // valeurs texte (Apparence) : vals = 0..n-1, def = index
    const wchar_t *fr, *en;
    std::vector<std::wstring> labFr, labEn;      // vide : la valeur + suffixe
    const wchar_t *suffix;
    const wchar_t *dFr, *dEn;
};
static std::vector<Opt> g_opts;
static int g_optHot = -1, g_optPart = 0, g_tabHot = -1;
static float g_scroll[TAB_COUNT];
static RectF g_tabR[TAB_COUNT];
static const RectF kOptPanel(440, 116, 512, 472), kOptList(452, 128, 488, 396);
static const float kRowH = 34;

static void BuildOptions()
{
    {   // Port
        Opt o = {};
        o.tab = TAB_COOP; o.key = "Port"; o.def = 7870; o.kind = O_CHOICE; o.vals = { 7870, 7871, 7872, 7873, 7874, 7875 };
        o.fr = L"Port"; o.en = L"Port"; o.suffix = L"";
        o.dFr = L"Port de la partie (7870 par d\u00E9faut) : UDP pour le jeu, TCP pour le salon. L'h\u00F4te l'ouvre sur sa box (UDP et TCP) ; les invit\u00E9s prennent le m\u00EAme.";
        o.dEn = L"Session port (7870 by default): UDP for the game, TCP for the lobby. The host opens it on their router (UDP and TCP); guests use the same one.";
        g_opts.push_back(o);
    }
    {   // Apparence : materiaux des corps des PNJ du jeu (Sync\Avatar.cs)
        Opt o = {};
        o.tab = TAB_COOP; o.key = "Apparence"; o.kind = O_CHOICE;
        // Tenues qui existent vraiment dans le jeu (Avatar.SkinNames, releve du 05/10 : pas de 05, 06, 08, 14, 15, 27).
        const int shirts[] = { 1, 2, 3, 4, 7, 9, 10, 11, 12, 13, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 28 };
        for (int i : shirts) {
            char k[32]; sprintf_s(k, "char_shirt%02d", i);
            o.svals.push_back(k);
            o.labFr.push_back(L"Tenue " + std::to_wstring(i));
            o.labEn.push_back(L"Outfit " + std::to_wstring(i));
        }
        const char *extra[] = { "cop_shirt", "cop_shirt2", "rally_shirt", "psk_shirt", "inspector_shirt" };
        const wchar_t *fr[] = { L"Policier", L"Policier 2", L"Pilote de rallye", L"Employ\u00E9 PSK", L"Inspecteur" };
        const wchar_t *en[] = { L"Police officer", L"Police officer 2", L"Rally driver", L"PSK employee", L"Inspector" };
        for (int i = 0; i < 5; i++) { o.svals.push_back(extra[i]); o.labFr.push_back(fr[i]); o.labEn.push_back(en[i]); }
        for (int i = 0; i < (int)o.svals.size(); i++) o.vals.push_back(i);
        o.def = 0;
        for (int i = 0; i < (int)o.svals.size(); i++) if (o.svals[i] == "char_shirt21") o.def = i;   // defaut du mod
        o.fr = L"Apparence"; o.en = L"Appearance"; o.suffix = L"";
        o.dFr = L"Le personnage que les autres joueurs voient : la tenue d'un habitant, ou celle du policier, du pilote de rallye\u2026 Aper\u00E7u en 3D : onglet TENUE.";
        o.dEn = L"The character the other players see: a local's outfit, or the police officer's, the rally driver's\u2026 3D preview: OUTFIT tab.";
        g_opts.push_back(o);
    }
}

static int OptGet(const Opt &o)
{
    std::string ini = ModIniA();
    if (!o.svals.empty()) {
        char v[64];
        GetPrivateProfileStringA("Coop", o.key, "", v, sizeof(v), ini.c_str());
        for (size_t i = 0; i < o.svals.size(); i++) if (!_stricmp(v, o.svals[i].c_str())) return (int)i;
        return o.def;
    }
    return GetPrivateProfileIntA("Coop", o.key, o.def, ini.c_str());
}
static void OptSet(const Opt &o, int v)
{
    EnsureModDir();
    std::string ini = ModIniA();
    if (!o.svals.empty()) { if (v >= 0 && v < (int)o.svals.size()) WritePrivateProfileStringA("Coop", o.key, o.svals[v].c_str(), ini.c_str()); return; }
    char b[16];
    wsprintfA(b, "%d", v);
    WritePrivateProfileStringA("Coop", o.key, b, ini.c_str());
}
static const Opt *OptByKey(const char *key) { for (auto &o : g_opts) if (!strcmp(o.key, key)) return &o; return NULL; }

static const wchar_t *TabName(int t)
{
    static const wchar_t *fr[] = { L"COOP", L"NOUVEAUT\u00C9S", L"JOURNAUX", L"VOITURE", L"SALON", L"TENUE" }, *en[] = { L"CO-OP", L"UPDATES", L"LOGS", L"CAR", L"LOBBY", L"OUTFIT" };
    return g_fr ? fr[t] : en[t];
}
// JOURNAUX : onglet aussi (l'icone seule, les amis de JD ne la trouvaient pas).
static bool TabVisible(int t) { return t == TAB_LOBBY ? g_lobby != LB_NONE : true; }
static void LayoutTabs()
{
    static const int order[] = { TAB_LOBBY, TAB_COOP, TAB_SKIN, TAB_CAR, TAB_NOTES, TAB_LOGS };
    float x = 440, pad = 12, gap = 6;
    Bitmap bm(1, 1);
    Graphics mg(&bm);
    for (int t = 0; t < TAB_COUNT; t++) g_tabR[t] = RectF(0, 0, 0, 0);
    for (int t : order) {
        if (!TabVisible(t)) continue;
        float w = pad + MeasureW(mg, TabName(t), 11.5f, FontStyleBold);
        g_tabR[t] = RectF(x, 78, w, 26);
        x += w + gap;
    }
}
static std::vector<int> TabRows(int t)
{
    std::vector<int> r;
    for (int i = 0; i < (int)g_opts.size(); i++) if (g_opts[i].tab == t) r.push_back(i);
    return r;
}
static float NotesMaxScroll();
static float LogsMaxScroll();
static float LobbyMaxScroll();
static float MaxScroll(int t) { return t == TAB_LOBBY ? LobbyMaxScroll() : t == TAB_LOGS ? LogsMaxScroll() : t == TAB_NOTES ? NotesMaxScroll() : max(0.0f, TabRows(t).size() * kRowH - kOptList.Height); }

static int ValueIndex(const Opt &o, int v)
{
    for (int i = 0; i < (int)o.vals.size(); i++) if (o.vals[i] == v) return i;
    for (int i = 0; i < (int)o.vals.size(); i++) if (o.vals[i] > v) return i;   // valeur hors liste : la suivante
    return (int)o.vals.size() - 1;
}
static std::wstring ValueText(const Opt &o, int v)
{
    int i = ValueIndex(o, v);
    const std::vector<std::wstring> &lab = g_fr ? o.labFr : o.labEn;
    if (!lab.empty()) return lab[i];
    wchar_t b[32];
    swprintf_s(b, L"%d%s", v, o.suffix);
    return b;
}
static void OptStep(int idx, int dir)
{
    const Opt &o = g_opts[idx];
    int v = OptGet(o);
    if (o.kind == O_TOGGLE) { OptSet(o, v ? 0 : 1); return; }
    int i = ValueIndex(o, v);
    if (o.vals[i] != v && dir > 0) i--;
    i = (i + dir + (int)o.vals.size()) % (int)o.vals.size();
    OptSet(o, o.vals[i]);
}

static bool NotesUnseen();

static void DrawTabs(Graphics &g)
{
    if (g_gameDir.empty()) return;
    for (int t = 0; t < TAB_COUNT; t++) {
        if (!TabVisible(t)) continue;
        RectF r = g_tabR[t];
        GraphicsPath p;
        RoundRect(p, r, r.Height / 2);
        bool on = g_tab == t, hot = g_tabHot == t;
        if (on) { LinearGradientBrush lg(r, kAcc, kAcc2, LinearGradientModeHorizontal); g.FillPath(&lg, &p); }
        else { SolidBrush b(hot ? TH(tabHot) : TH(tab)); g.FillPath(&b, &p); }
        Text(g, TabName(t), r, 11.5f, FontStyleBold, on ? kOnAcc : Mix(kGrey, kInk, hot ? 1.0f : 0.0f));
        if (t == TAB_NOTES && !on && NotesUnseen()) {   // pastille : des notes pas encore lues
            SolidBrush dot(kRed);
            g.FillEllipse(&dot, r.X + r.Width - 7, r.Y - 1, 8.0f, 8.0f);
        }
    }
}

static void DrawPanel(Graphics &g)
{
    GraphicsPath pp;
    RoundRect(pp, kOptPanel, 18);
    SolidBrush bg(TH(panel));
    g.FillPath(&bg, &pp);
    Pen border(TH(panelBorder), 1.5f);
    g.DrawPath(&border, &pp);
}

static void DrawNotes(Graphics &g);
static void DrawLogs(Graphics &g);
static void DrawCar(Graphics &g);
static void DrawLobby(Graphics &g);
static void DrawSkins(Graphics &g);

static void DrawOptions(Graphics &g)
{
    if (g_tab < 0 || g_gameDir.empty()) return;
    if (g_tab == TAB_LOBBY) { DrawLobby(g); return; }
    if (g_tab == TAB_SKIN) { DrawSkins(g); return; }
    if (g_tab == TAB_CAR) { DrawCar(g); return; }
    if (g_tab == TAB_NOTES) { DrawNotes(g); return; }
    if (g_tab == TAB_LOGS) { DrawLogs(g); return; }
    DrawPanel(g);
    std::vector<int> rows = TabRows(g_tab);
    float sc = g_scroll[g_tab];
    g.SetClip(kOptList);
    for (int k = 0; k < (int)rows.size(); k++) {
        const Opt &o = g_opts[rows[k]];
        RectF r(kOptList.X, kOptList.Y + k * kRowH - sc, kOptList.Width - 10, kRowH);
        if (r.Y + r.Height < kOptList.Y || r.Y > kOptList.Y + kOptList.Height) continue;
        bool hot = g_optHot == rows[k];
        if (hot) { GraphicsPath hp; RoundRect(hp, RectF(r.X, r.Y + 2, r.Width, r.Height - 4), 9); SolidBrush hb(TH(cardSel)); g.FillPath(&hb, &hp); }
        Text(g, g_fr ? o.fr : o.en, RectF(r.X + 12, r.Y, 280, r.Height), 13.5f, FontStyleRegular, kInk, StringAlignmentNear);
        int v = OptGet(o);
        if (o.kind == O_TOGGLE) {
            RectF tr(r.X + r.Width - 54, r.Y + 7, 42, 20);
            GraphicsPath tp; RoundRect(tp, tr, 10);
            if (v) { LinearGradientBrush lg(tr, kAcc, kAcc2, LinearGradientModeHorizontal); g.FillPath(&lg, &tp); }
            else { SolidBrush ob(TH(toggleOff)); g.FillPath(&ob, &tp); }
            SolidBrush knob(v ? kOnAcc : Color(255, 255, 255, 255));
            g.FillEllipse(&knob, v ? tr.X + 24 : tr.X + 2, tr.Y + 2, 16.0f, 16.0f);
        } else {
            RectF cr(r.X + r.Width - 190, r.Y + 5, 178, 24);
            GraphicsPath cp; RoundRect(cp, cr, 12);
            SolidBrush cb(TH(card)); g.FillPath(&cb, &cp);
            Pen cpen(TH(choiceBorder), 1.2f); g.DrawPath(&cpen, &cp);
            Color al = (hot && g_optPart < 0) ? kAcc : WithA(kAcc, 0.78f), ar = (hot && g_optPart > 0) ? kAcc : WithA(kAcc, 0.78f);
            Text(g, L"\u2039", RectF(cr.X + 4, cr.Y - 2, 18, cr.Height), 18, FontStyleBold, al);
            Text(g, L"\u203A", RectF(cr.X + cr.Width - 22, cr.Y - 2, 18, cr.Height), 18, FontStyleBold, ar);
            Text(g, ValueText(o, v), RectF(cr.X + 20, cr.Y, cr.Width - 40, cr.Height), 12.5f, FontStyleBold, kInk);
        }
    }
    g.ResetClip();
    if (g_tab == TAB_COOP) {   // sous les options : comment jouer ensemble
        float y = kOptList.Y + rows.size() * kRowH + 18;
        Pen sep(TH(sep), 1);
        g.DrawLine(&sep, kOptPanel.X + 18, y, kOptPanel.X + kOptPanel.Width - 18, y);
        Text(g, T(L"JOUER ENSEMBLE", L"PLAYING TOGETHER"), RectF(kOptList.X + 12, y + 12, 300, 18), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
        Para(g, T(L"\u2022 H\u00C9BERGER : ouvre un salon ; quand tout le monde est pr\u00EAt, LANCER d\u00E9marre le jeu de chacun. "
                  L"Ouvre le port ci-dessus sur ta box (UDP et TCP) et donne ton adresse IP publique.\n\n"
                  L"\u2022 REJOINDRE : entre l'adresse IP de l'h\u00F4te \u00E0 gauche (adresse:port si l'h\u00F4te a chang\u00E9 de port).\n\n"
                  L"\u2022 L'invit\u00E9 joue dans un profil \u00E0 part qui re\u00E7oit la sauvegarde de l'h\u00F4te : sa propre sauvegarde "
                  L"n'est pas touch\u00E9e.\n\n"
                  L"\u2022 JOUER EN SOLO : le jeu normal, avec MWCoop charg\u00E9 mais sans r\u00E9seau.",
                  L"\u2022 HOST: opens a lobby; once everyone is ready, START launches everyone's game. "
                  L"Open the port above on your router (UDP and TCP) and share your public IP address.\n\n"
                  L"\u2022 JOIN: enter the host's IP address on the left (address:port if the host changed the port).\n\n"
                  L"\u2022 The guest plays in a separate profile that receives the host's save: their own save is left untouched.\n\n"
                  L"\u2022 PLAY SOLO: the normal game, with MWCoop loaded but no network."),
             RectF(kOptList.X + 12, y + 36, kOptList.Width - 30, 532 - (y + 40)), 12.5f, kInk);
    }
    // description de la ligne survolee
    Pen sep(TH(sep), 1);
    g.DrawLine(&sep, kOptPanel.X + 18, 532.0f, kOptPanel.X + kOptPanel.Width - 18, 532.0f);
    std::wstring d = g_optHot >= 0 ? (g_fr ? g_opts[g_optHot].dFr : g_opts[g_optHot].dEn)
                                   : T(L"\u00C9crit dans MWCoop\\mwcoop.ini, pris au prochain lancement du jeu.", L"Saved in MWCoop\\mwcoop.ini, applied the next time the game starts.");
    Para(g, d, RectF(kOptPanel.X + 20, 536, kOptPanel.Width - 40, 44), 12, kGrey, StringAlignmentCenter);
}

// Survol : ligne d'option et cote du selecteur (-1 gauche, +1 droite, 0 libelle)
static void HitOption(float x, float y, int *row, int *part)
{
    *row = -1; *part = 0;
    if (g_tab < 0 || g_tab == TAB_NOTES || g_tab == TAB_LOGS || g_tab == TAB_CAR || g_tab == TAB_LOBBY || g_tab == TAB_SKIN || !kOptList.Contains(x, y)) return;
    std::vector<int> rows = TabRows(g_tab);
    int k = (int)((y - kOptList.Y + g_scroll[g_tab]) / kRowH);
    if (k < 0 || k >= (int)rows.size()) return;
    *row = rows[k];
    const Opt &o = g_opts[*row];
    float right = kOptList.X + kOptList.Width - 10;
    if (o.kind == O_CHOICE && x >= right - 190) *part = x < right - 190 + 89 ? -1 : 1;
}
static int HitTab(float x, float y)
{
    if (g_state != ST_IDLE || g_gameDir.empty()) return -1;
    for (int t = 0; t < TAB_COUNT; t++) if (TabVisible(t) && g_tabR[t].Contains(x, y)) return t;
    return -1;
}

// ---------------------------------------------------------------- onglet NOUVEAUTES
static std::wstring NewestNote()
{
    EnterCriticalSection(&g_cs);
    std::wstring v = g_notes.empty() ? L"" : g_notes[0].ver;
    LeaveCriticalSection(&g_cs);
    return v;
}
static bool NotesUnseen()
{
    std::wstring v = NewestNote();
    if (v.empty()) return false;
    wchar_t seen[64] = {};
    GetPrivateProfileStringW(L"Lanceur", L"NotesVues", L"", seen, 64, g_iniLauncher.c_str());
    return !seen[0] || CmpVer(v, seen) > 0;
}
static void NotesMarkSeen()
{
    std::wstring v = NewestNote();
    if (!v.empty()) WritePrivateProfileStringW(L"Lanceur", L"NotesVues", v.c_str(), g_iniLauncher.c_str());
}

static RectF NotesArea() { return RectF(kOptList.X, kOptList.Y, kOptList.Width, kOptPanel.Y + kOptPanel.Height - 14 - kOptList.Y); }
static float NotesMaxScroll() { return max(0.0f, g_notesH - NotesArea().Height); }

static void DrawNotes(Graphics &g)
{
    DrawPanel(g);
    RectF area = NotesArea();
    std::vector<Note> notes;
    EnterCriticalSection(&g_cs);
    notes = g_notes;
    LeaveCriticalSection(&g_cs);
    if (notes.empty()) {
        const wchar_t *msg = !g_notesDone ? T(L"Chargement des notes de version\u2026", L"Loading release notes\u2026")
                           : g_relState == REL_NONE ? T(L"Aucune version publi\u00E9e pour l'instant.", L"No release published yet.")
                           : T(L"Notes de version indisponibles (hors ligne).", L"Release notes unavailable (offline).");
        Text(g, msg, area, 13, FontStyleRegular, kGrey);
        return;
    }
    FontFamily fam(L"Segoe UI");
    Font fh(&fam, 14.5f, FontStyleBold, UnitPixel), fd(&fam, 11.5f, FontStyleRegular, UnitPixel), fb(&fam, 12.5f, FontStyleRegular, UnitPixel);
    StringFormat sf;
    SolidBrush ink(kInk), grey(kGrey), acc(kAcc);
    Pen sep(TH(sep), 1);
    float sc = g_scroll[TAB_NOTES], y = area.Y - sc, w = area.Width - 14;
    g.SetClip(area);
    for (size_t i = 0; i < notes.size(); i++) {
        const Note &n = notes[i];
        const std::wstring &body = g_fr ? n.fr : n.en;
        RectF box;
        g.MeasureString(body.c_str(), -1, &fb, RectF(0, 0, w - 16, 100000), &sf, &box);
        float h = 26 + (body.empty() ? 0 : box.Height) + 16;
        if (y + h >= area.Y && y <= area.Y + area.Height) {
            std::wstring title = L"MWCoop " + n.ver;
            g.DrawString(title.c_str(), -1, &fh, PointF(area.X + 6, y), &acc);
            float tw = MeasureW(g, title, 14.5f, FontStyleBold);
            g.DrawString(n.date.c_str(), -1, &fd, PointF(area.X + 12 + tw, y + 3), &grey);
            int cmp = g_localVer.empty() ? 1 : CmpVer(n.ver, g_localVer);
            if (cmp >= 0 && !g_localVer.empty()) {   // version installee, ou plus recente (a venir)
                const wchar_t *lab = cmp == 0 ? T(L"INSTALL\u00C9E", L"INSTALLED") : T(L"NOUVELLE", L"NEW");
                RectF br(area.X + w - 12 - 7.0f * (float)wcslen(lab), y + 2, 12 + 7.0f * (float)wcslen(lab), 17);
                GraphicsPath bp; RoundRect(bp, br, 8.5f);
                SolidBrush bb(cmp == 0 ? WithA(kInk, 0.12f) : WithA(kAcc, 0.28f));
                g.FillPath(&bb, &bp);
                Text(g, lab, br, 9.5f, FontStyleBold, kInk);
            }
            if (!body.empty()) g.DrawString(body.c_str(), -1, &fb, RectF(area.X + 10, y + 26, w - 16, box.Height + 4), &sf, &ink);
            if (i + 1 < notes.size()) g.DrawLine(&sep, area.X + 6, y + h - 8, area.X + w, y + h - 8);
        }
        y += h;
    }
    g.ResetClip();
    g_notesH = y + sc - area.Y;
    float ms = NotesMaxScroll();
    if (ms > 0) {
        float bh = area.Height * area.Height / (area.Height + ms), by = area.Y + (area.Height - bh) * sc / ms;
        GraphicsPath sp; RoundRect(sp, RectF(area.X + area.Width - 5, by, 4, bh), 2);
        SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
    }
}

// ---------------------------------------------------------------- page JOURNAUX (onglet, et bouton rond)
// Les journaux du chargeur et du mod (MWCoop\logs\chargeur.log, mwcoop.log, remplaces a chaque lancement du jeu),
// ceux du profil invite (MWCoop\profils\invite\logs) et celui de Unity s'il existe (mywintercar_Data\output_log.txt).
// Un clic ouvre le journal, l'icone dossier le montre dans l'explorateur. En haut : "Ouvrir le dossier" (celui du
// dernier journal ouvert, sinon MWCoop\) et "Creer un zip a envoyer" (tous les journaux + mwcoop.ini et
// lancement.ini, sur le Bureau : un seul fichier a envoyer).
enum { LOG_MOD, LOG_LOADER, LOG_UNITY };
struct LogEntry { std::wstring path, profile; int kind; bool guest; uint64_t bytes; FILETIME mt; int errors; };
static std::vector<LogEntry> g_logList;
static int g_logRowHot = -1, g_logPart = 0;          // g_logPart : 0 la ligne (ouvrir), 1 dossier
static const RectF kLogsR(452, 156, 488, 370);
static std::wstring g_logSelPath;                     // dernier journal ouvert d'un clic (bouton "Ouvrir le dossier")
static const float kLogRowH = 54;

static std::wstring LogsDir() { return g_gameDir + L"MWCoop\\logs\\"; }

// %LOCALAPPDATA%\MWCoop\ : trace de chargement du mod, et ses donnees si le dossier du jeu est en lecture seule.
static std::wstring LocalDir()
{
    wchar_t l[MAX_PATH] = L"";
    GetEnvironmentVariableW(L"LOCALAPPDATA", l, MAX_PATH);
    return l[0] ? std::wstring(l) + L"\\MWCoop\\" : L"";
}

// Profils du mod dans le dossier du jeu (MWCoop\profils\<nom>) : invite, ou un profil d'essai.
static std::vector<std::wstring> Profiles()
{
    std::vector<std::wstring> out;
    if (g_gameDir.empty()) return out;
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((g_gameDir + L"MWCoop\\profils\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return out;
    do {
        if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && fd.cFileName[0] != L'.') out.push_back(fd.cFileName);
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return out;
}

// Dernier lancement par le lanceur (lancement.ini, Horodatage) sans aucun journal du chargeur ecrit
// apres : le jeu a demarre sans le mod (version.dll bloque par l'antivirus, autre dossier du jeu...).
static bool LastLaunchWithoutMod()
{
    if (g_gameDir.empty()) return false;
    wchar_t ts[32];
    GetPrivateProfileStringW(L"Lancement", L"Horodatage", L"0", ts, 32, (g_gameDir + L"MWCoop\\lancement.ini").c_str());
    long long t = _wtoi64(ts), now = (long long)_time64(NULL);
    if (t <= 0 || now - t > 2 * 86400 || now - t < 90) return false;   // (trop vieux, ou le jeu demarre encore)
    long long newest = 0;
    auto look = [&](const std::wstring &p) {
        WIN32_FILE_ATTRIBUTE_DATA a;
        if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a)) return;
        ULARGE_INTEGER u; u.LowPart = a.ftLastWriteTime.dwLowDateTime; u.HighPart = a.ftLastWriteTime.dwHighDateTime;
        long long unix = (long long)(u.QuadPart / 10000000ULL) - 11644473600LL;
        if (unix > newest) newest = unix;
    };
    look(LogsDir() + L"chargeur.log");
    if (!LocalDir().empty()) look(LocalDir() + L"dernier-lancement.txt");
    for (const std::wstring &pr : Profiles()) look(g_gameDir + L"MWCoop\\profils\\" + pr + L"\\logs\\chargeur.log");
    return newest < t - 5;
}

// Lignes d'erreur du journal (ERREUR, exception) ; au-dela de 4 Mo, les 4 derniers seulement.
static int CountErrors(const std::wstring &path, uint64_t bytes)
{
    HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return 0;
    std::string t;
    uint64_t n = min<uint64_t>(bytes, 4u << 20);
    if (bytes > n) { LARGE_INTEGER at; at.QuadPart = (LONGLONG)(bytes - n); SetFilePointerEx(f, at, NULL, FILE_BEGIN); }
    t.resize((size_t)n);
    DWORD r = 0;
    if (n) ReadFile(f, &t[0], (DWORD)n, &r, NULL);
    t.resize(r);
    CloseHandle(f);
    int count = 0;
    for (size_t p = 0; p < t.size();) {
        size_t e = t.find('\n', p);
        if (e == std::string::npos) e = t.size();
        std::string line = t.substr(p, e - p);
        if (line.find("ERREUR") != std::string::npos || line.find("xception") != std::string::npos) count++;
        p = e + 1;
    }
    return count;
}

static void LogsScan()
{
    std::vector<LogEntry> list;
    auto add = [&](const std::wstring &p, int kind, const std::wstring &profile) {
        WIN32_FILE_ATTRIBUTE_DATA a;
        if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a) || (a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) return;
        LogEntry e;
        e.path = p; e.kind = kind; e.profile = profile; e.guest = !_wcsicmp(profile.c_str(), L"invite");
        e.bytes = ((uint64_t)a.nFileSizeHigh << 32) | a.nFileSizeLow;
        e.mt = a.ftLastWriteTime;
        e.errors = kind == LOG_UNITY ? -1 : CountErrors(p, e.bytes);   // (Unity : trop d'exceptions sans gravite)
        list.push_back(e);
    };
    if (!g_gameDir.empty()) {
        add(LogsDir() + L"mwcoop.log", LOG_MOD, L"");
        add(LogsDir() + L"chargeur.log", LOG_LOADER, L"");
        // Chaque profil (invite, ou celui de mwcoop.ini [Test] Profil) a ses propres journaux.
        for (const std::wstring &pr : Profiles()) {
            std::wstring d = g_gameDir + L"MWCoop\\profils\\" + pr + L"\\logs\\";
            add(d + L"mwcoop.log", LOG_MOD, pr);
            add(d + L"chargeur.log", LOG_LOADER, pr);
        }
        add(g_gameDir + L"mywintercar_Data\\output_log.txt", LOG_UNITY, L"");
    }
    // Repli quand le dossier du jeu est en lecture seule, et trace de chargement du mod.
    std::wstring ld = LocalDir();
    if (!ld.empty()) {
        add(ld + L"logs\\mwcoop.log", LOG_MOD, L"(secours)");
        add(ld + L"logs\\chargeur.log", LOG_LOADER, L"(secours)");
        add(ld + L"profils\\invite\\logs\\mwcoop.log", LOG_MOD, L"invite (secours)");
        add(ld + L"profils\\invite\\logs\\chargeur.log", LOG_LOADER, L"invite (secours)");
        add(ld + L"dernier-lancement.txt", LOG_LOADER, L"trace de lancement");
    }
    // Les plus recents d'abord : le dernier lancement est en haut.
    std::sort(list.begin(), list.end(), [](const LogEntry &a, const LogEntry &b) { return CompareFileTime(&a.mt, &b.mt) > 0; });
    g_logList.swap(list);
    g_scroll[TAB_LOGS] = min(g_scroll[TAB_LOGS], LogsMaxScroll());
}

static float LogsMaxScroll() { return max(0.0f, g_logList.size() * kLogRowH - kLogsR.Height); }

static std::wstring LogTitle(const LogEntry &e)
{
    std::wstring s = e.kind == LOG_MOD ? T(L"Journal du mod", L"Mod log") : e.kind == LOG_LOADER ? T(L"Journal du chargeur", L"Loader log") : T(L"Journal de Unity", L"Unity log");
    if (e.guest) s += T(L" \u00B7 profil invit\u00E9", L" \u00B7 guest profile");
    else if (!e.profile.empty()) s += T(L" \u00B7 profil ", L" \u00B7 profile ") + e.profile;
    return s;
}
static std::wstring LogDate(const FILETIME &ft)
{
    FILETIME lt;
    SYSTEMTIME st;
    FileTimeToLocalFileTime(&ft, &lt);
    FileTimeToSystemTime(&lt, &st);
    wchar_t b[64];
    if (g_fr) swprintf_s(b, L"%02d/%02d/%04d %02dh%02d", st.wDay, st.wMonth, st.wYear, st.wHour, st.wMinute);
    else swprintf_s(b, L"%04d-%02d-%02d %02d:%02d", st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute);
    return b;
}

static void DrawFolderCircle(Graphics &g, RectF c, bool hot)
{
    SolidBrush cb(hot ? TH(circleHot) : TH(circle));
    g.FillEllipse(&cb, c);
    Pen pen(hot ? kAcc : kInk, 1.4f);
    pen.SetLineJoin(LineJoinRound); pen.SetStartCap(LineCapRound); pen.SetEndCap(LineCapRound);
    float cx = c.X + c.Width / 2, cy = c.Y + c.Height / 2;
    PointF p[] = { PointF(cx - 7, cy - 4.5f), PointF(cx - 2.5f, cy - 4.5f), PointF(cx - 1, cy - 2.5f), PointF(cx + 7, cy - 2.5f),
                   PointF(cx + 7, cy + 5), PointF(cx - 7, cy + 5) };
    g.DrawPolygon(&pen, p, 6);
}

static RectF LogRowRect(int i) { return RectF(kLogsR.X, kLogsR.Y + i * kLogRowH - g_scroll[TAB_LOGS], kLogsR.Width - 10, kLogRowH - 6); }
static RectF LogIconRect(const RectF &r) { return RectF(r.X + r.Width - 38, r.Y + (r.Height - 26) / 2, 26, 26); }

static void DrawLogs(Graphics &g)
{
    DrawPanel(g);
    Text(g, T(L"JOURNAUX", L"LOGS"), RectF(460, 122, 150, 26), 17, FontStyleBold, kInk, StringAlignmentNear);
    DrawSmallButton(g, B_LOGDIR, T(L"Ouvrir le dossier", L"Open folder"));
    {   // zip a envoyer : pilule pleine (l'action a faire quand ca plante)
        Button &b = g_btn[B_LOGZIP];
        RectF r = b.r;
        if (g_pressed == B_LOGZIP && g_hot == B_LOGZIP) r.Offset(0, 1);
        GraphicsPath zp;
        RoundRect(zp, r, r.Height / 2);
        LinearGradientBrush lg(r, kAcc, kAcc2, LinearGradientModeHorizontal);
        g.FillPath(&lg, &zp);
        SolidBrush hi(Color((BYTE)(60 * b.hover), 255, 255, 255));
        g.FillPath(&hi, &zp);
        Text(g, T(L"Cr\u00E9er un zip \u00E0 envoyer", L"Create a zip to send"), r, 12, FontStyleBold, kOnAcc);
    }
    if (g_logList.empty())
        Para(g, T(L"Aucun journal pour l'instant : le jeu en \u00E9crit \u00E0 chaque lancement avec MWCoop.", L"No logs yet: the game writes them every time it starts with MWCoop."),
             kLogsR, 13, kGrey, StringAlignmentCenter);
    float sc = g_scroll[TAB_LOGS];
    g.SetClip(kLogsR);
    for (int i = 0; i < (int)g_logList.size(); i++) {
        const LogEntry &e = g_logList[i];
        RectF r = LogRowRect(i);
        if (r.Y + r.Height < kLogsR.Y || r.Y > kLogsR.Y + kLogsR.Height) continue;
        bool hot = i == g_logRowHot;
        GraphicsPath rp;
        RoundRect(rp, r, 10);
        SolidBrush rb(hot && g_logPart == 0 ? TH(cardSel) : TH(card));
        g.FillPath(&rb, &rp);
        Pen rpen(hot ? kAcc : TH(choiceBorder), 1.2f);
        g.DrawPath(&rpen, &rp);
        SolidBrush dot(e.errors > 0 ? kRed : WithA(kAcc, 0.8f));   // pastille : rouge s'il y a des erreurs
        g.FillEllipse(&dot, r.X + 12, r.Y + r.Height / 2 - 4, 8.0f, 8.0f);
        Text(g, LogTitle(e), RectF(r.X + 28, r.Y + 5, r.Width - 170, 20), 13, FontStyleBold, kInk, StringAlignmentNear);
        std::wstring name = e.path.substr(e.path.find_last_of(L'\\') + 1);
        wchar_t info[200], size[32];
        if (e.bytes < 1048576) swprintf_s(size, L"%.0f %s", e.bytes / 1024.0, T(L"Ko", L"KB"));
        else swprintf_s(size, L"%.1f %s", e.bytes / 1048576.0, T(L"Mo", L"MB"));
        swprintf_s(info, L"%s \u00B7 %s \u00B7 %s", name.c_str(), LogDate(e.mt).c_str(), size);
        Text(g, info, RectF(r.X + 28, r.Y + 25, r.Width - 170, 18), 10.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        if (e.errors > 0) {   // etiquette : nombre de lignes d'erreur
            wchar_t lab[48];
            swprintf_s(lab, e.errors == 1 ? T(L"1 ERREUR", L"1 ERROR") : T(L"%d ERREURS", L"%d ERRORS"), e.errors);
            float lw = 14 + 6.6f * (float)wcslen(lab);
            RectF br(r.X + r.Width - 50 - lw, r.Y + (r.Height - 17) / 2, lw, 17);
            GraphicsPath bp; RoundRect(bp, br, 8.5f);
            SolidBrush bb(Color(45, 214, 48, 72));
            g.FillPath(&bb, &bp);
            Text(g, lab, br, 9.5f, FontStyleBold, kRed);
        }
        DrawFolderCircle(g, LogIconRect(r), hot && g_logPart == 1);
    }
    g.ResetClip();
    float ms = LogsMaxScroll();
    if (ms > 0) {
        float h = kLogsR.Height * kLogsR.Height / (kLogsR.Height + ms), y = kLogsR.Y + (kLogsR.Height - h) * sc / ms;
        GraphicsPath sp; RoundRect(sp, RectF(kLogsR.X + kLogsR.Width - 4, y, 4, h), 2);
        SolidBrush sb(WithA(kInk, 0.4f)); g.FillPath(&sb, &sp);
    }
    Pen sep(TH(sep), 1);
    g.DrawLine(&sep, kOptPanel.X + 18, 536.0f, kOptPanel.X + kOptPanel.Width - 18, 536.0f);
    Para(g, T(L"Un souci en jeu : \u00AB Cr\u00E9er un zip \u00E0 envoyer \u00BB juste apr\u00E8s la partie (les journaux sont "
              L"remplac\u00E9s \u00E0 chaque lancement du jeu), puis envoie le zip pos\u00E9 sur le Bureau.",
              L"Trouble in game: \"Create a zip to send\" right after the session (logs are replaced every time the game "
              L"starts), then send the zip saved on the Desktop."),
         RectF(kOptPanel.X + 20, 540, kOptPanel.Width - 40, 42), 12, kGrey, StringAlignmentCenter);
}

static int LogRowAt(float x, float y, int *part)
{
    *part = 0;
    if (!kLogsR.Contains(x, y)) return -1;
    int i = (int)((y - kLogsR.Y + g_scroll[TAB_LOGS]) / kLogRowH);
    if (i < 0 || i >= (int)g_logList.size()) return -1;
    RectF r = LogRowRect(i);
    if (!r.Contains(x, y)) return -1;
    RectF f = LogIconRect(r);
    f.Inflate(2, 2);
    *part = f.Contains(x, y) ? 1 : 0;
    return i;
}

static bool LogsMouseDown(float x, float y)
{
    int part, i = LogRowAt(x, y, &part);
    if (i < 0) return kOptPanel.Contains(x, y);
    std::wstring path = g_logList[i].path;
    g_logSelPath = path;
    if (part == 1) {
        std::wstring arg = L"/select,\"" + path + L"\"";
        ShellExecuteW(g_wnd, L"open", L"explorer.exe", arg.c_str(), NULL, SW_SHOWNORMAL);
    } else if ((INT_PTR)ShellExecuteW(g_wnd, L"open", path.c_str(), NULL, NULL, SW_SHOWNORMAL) <= 32) {
        std::wstring arg = L"\"" + path + L"\"";   // (.log sans programme associe)
        ShellExecuteW(g_wnd, L"open", L"notepad.exe", arg.c_str(), NULL, SW_SHOWNORMAL);
    }
    return true;
}

// ---------------------------------------------------------------- onglet VOITURE (couleur de la CORRIS, apercu 3D)
// [Coop] CouleurVoiture=RRGGBB (vide : au hasard, comme le jeu). Le mod la pose sur la carrosserie a une NOUVELLE
// partie ; l'hote la donne aux invites.
// Apercu : <jeu>\MWCoop\cache\corris.mesh, exporte par le mod depuis le jeu du joueur (jamais livre avec le mod).
// Format petit-boutiste : "MWCM", u32 version (1), u32 n, puis n morceaux : u16 longueur + nom UTF-8, u8 peignable
// (1 = prend la couleur choisie), 3 x f32 couleur de base (0..1), u32 nb sommets + nb x (3 x f32) (repere de la
// voiture : x droite, y haut, z avant, metres, repere gauche d'Unity), u32 nb indices + nb x u32 (triangles).
// Rendu logiciel : perspective, tampon de profondeur, normales lissees par sommet (Gouraud), lumiere directionnelle
// + lumiere d'appoint + ambiante + reflet ; rendu en 2x puis reduit (antialiasing), vitres en transparence. Au plus
// ~30 images/s, et seulement quand l'onglet est ouvert.
struct CarSwatch { int rgb; const wchar_t *fr, *en; };
static const CarSwatch kSwatches[] = {   // teintes des annees 70-80
    { 0xD9C49C, L"Beige Sahara", L"Sahara beige" },
    { 0x8DB33A, L"Vert pomme", L"Apple green" },
    { 0xE0702A, L"Orange", L"Orange" },
    { 0x86B8E2, L"Bleu ciel", L"Sky blue" },
    { 0x1E2C52, L"Bleu marine", L"Navy blue" },
    { 0xB3221E, L"Rouge", L"Red" },
    { 0xEEECE4, L"Blanc", L"White" },
    { 0x161616, L"Noir", L"Black" },
    { 0x8E949B, L"Gris m\u00E9tal", L"Metallic grey" },
    { 0x5B3B22, L"Brun", L"Brown" },
    { 0xD3A21C, L"Jaune moutarde", L"Mustard yellow" },
    { 0x23452D, L"Vert fonc\u00E9", L"Dark green" },
    { 0x6C1B28, L"Bordeaux", L"Burgundy" },
    { 0x2A9C9A, L"Turquoise", L"Turquoise" },
    { 0x707A2C, L"Vert avocat", L"Avocado green" },
};
static const int kSwatchN = 1 + _countof(kSwatches);   // pastille 0 : au hasard
static const RectF kCarView(452, 128, 488, 270);
static const float kPitchMin = 0.04f, kPitchMax = 0.55f;
static int g_carHot = -1;                // pastille survolee

struct CarPart { bool paint, glass; float r, g, b; };
struct CarModel {
    int state = 0;                       // 0 pas lu, 1 pret, -1 absent, -2 illisible
    std::wstring path;
    FILETIME mt = {};
    DWORD checkT = 0;
    std::vector<float> pos, nrm;         // 3 par sommet (nrm : normale lissee)
    std::vector<uint32_t> tri;           // 3 par triangle
    std::vector<uint16_t> triPart;
    std::vector<CarPart> parts;
    float mn[3], mx[3], c[3], dist;      // boite de cadrage (carrosserie), son centre, distance de la camera
    float fx;                            // cadrage : max |x/z| sur tous les angles
};
static CarModel g_car;
static float g_carYaw = 3.75f, g_carPitch = 0.28f;
static bool g_carDrag, g_carDirty = true;
static float g_carDragX, g_carDragY;
static DWORD g_carIdleT, g_carDrawT;     // fin du dernier glisser (la rotation reprend 2,5 s apres), dernier rendu
static std::vector<uint32_t> g_carBig, g_carPix;   // rendu 2x et image reduite (PARGB)
static std::vector<float> g_carZ;
static int g_carW, g_carH, g_carDrawnColor = -2;

struct CarRot { float cy, sy, cp, sp; };
static inline void CarXf(const CarRot &R, float x, float y, float z, float &X, float &Y, float &Z)
{
    float x1 = x * R.cy + z * R.sy, z1 = -x * R.sy + z * R.cy;   // lacet (autour de y)
    X = x1;
    Y = y * R.cp + z1 * R.sp;                                     // tangage : le dessus vient vers la camera
    Z = -y * R.sp + z1 * R.cp;
}

static bool CarParse(const std::vector<unsigned char> &d, CarModel &m)
{
    struct Raw { std::string name; bool paint; float col[3]; std::vector<float> v; std::vector<uint32_t> idx; float mn[3], mx[3]; };
    std::vector<Raw> raw;
    size_t p = 0;
    auto get = [&](void *out, size_t n) { if (n > d.size() - p) return false; memcpy(out, d.data() + p, n); p += n; return true; };
    char magic[4];
    uint32_t ver = 0, n = 0;
    if (!get(magic, 4) || memcmp(magic, "MWCM", 4) || !get(&ver, 4) || ver != 1 || !get(&n, 4) || n > 4096) return false;
    for (uint32_t i = 0; i < n; i++) {
        Raw r;
        uint16_t len = 0;
        uint8_t pe = 0;
        uint32_t nv = 0, ni = 0;
        if (!get(&len, 2) || len > d.size() - p) return false;
        r.name.assign((const char *)d.data() + p, len);
        p += len;
        if (!get(&pe, 1) || !get(r.col, 12) || !get(&nv, 4) || nv > (d.size() - p) / 12) return false;
        r.v.resize((size_t)nv * 3);
        if (nv && !get(r.v.data(), (size_t)nv * 12)) return false;
        if (!get(&ni, 4) || ni > (d.size() - p) / 4 || ni % 3) return false;
        r.idx.resize(ni);
        if (ni && !get(r.idx.data(), (size_t)ni * 4)) return false;
        bool ok = nv && ni;
        for (uint32_t k : r.idx) if (k >= nv) ok = false;
        for (int a = 0; a < 3; a++) { r.mn[a] = 1e9f; r.mx[a] = -1e9f; }
        for (size_t k = 0; ok && k < r.v.size(); k++) {
            float x = r.v[k];
            if (!(fabsf(x) < 100)) ok = false;   // NaN ou valeur absurde : morceau ignore
            r.mn[k % 3] = min(r.mn[k % 3], x);
            r.mx[k % 3] = max(r.mx[k % 3], x);
        }
        r.paint = pe != 0;
        if (ok) raw.push_back(std::move(r));
    }
    // Boite de cadrage : les morceaux peignables (la carrosserie), sinon tout.
    bool anyPaint = false;
    for (const Raw &r : raw) anyPaint |= r.paint;
    float mn[3] = { 1e9f, 1e9f, 1e9f }, mx[3] = { -1e9f, -1e9f, -1e9f };
    for (const Raw &r : raw)
        if (r.paint || !anyPaint) for (int a = 0; a < 3; a++) { mn[a] = min(mn[a], r.mn[a]); mx[a] = max(mx[a], r.mx[a]); }
    if (raw.empty() || mn[0] > mx[0]) return false;

    m.pos.clear(); m.nrm.clear(); m.tri.clear(); m.triPart.clear(); m.parts.clear();
    for (const Raw &r : raw) {
        // Morceau qui depasse nettement de la carrosserie (ceinture pendante, cable du frein a main sous la voiture) :
        // laisse de cote, il deformerait l'apercu.
        bool out = false;
        for (int a = 0; a < 3; a++) if (r.mn[a] < mn[a] - 0.15f || r.mx[a] > mx[a] + 0.15f) out = true;
        if (out || m.parts.size() >= 65535) continue;
        std::string low = r.name;
        for (char &ch : low) ch = (char)tolower((unsigned char)ch);
        CarPart cp;
        cp.paint = r.paint;
        cp.glass = !r.paint && (low.find("window") != std::string::npos || low.find("windshield") != std::string::npos || low.find("glass") != std::string::npos);
        cp.r = min(max(r.col[0], 0.0f), 1.0f); cp.g = min(max(r.col[1], 0.0f), 1.0f); cp.b = min(max(r.col[2], 0.0f), 1.0f);
        // Blanc pur hors carrosserie : matiere texturee dans le jeu (habitacle, pieces) ; gris fonce dans l'apercu.
        if (!cp.paint && !cp.glass && cp.r > 0.95f && cp.g > 0.95f && cp.b > 0.95f) { cp.r = 0.24f; cp.g = 0.24f; cp.b = 0.25f; }
        if (cp.glass) { cp.r = 0.45f; cp.g = 0.56f; cp.b = 0.66f; }   // vitres (givre du jeu en blanc) : verre bleute
        uint16_t pi = (uint16_t)m.parts.size();
        m.parts.push_back(cp);
        uint32_t base = (uint32_t)(m.pos.size() / 3);
        m.pos.insert(m.pos.end(), r.v.begin(), r.v.end());
        m.nrm.resize(m.pos.size(), 0.0f);
        for (size_t t = 0; t + 2 < r.idx.size(); t += 3) {
            uint32_t ia = base + r.idx[t], ib = base + r.idx[t + 1], ic = base + r.idx[t + 2];
            const float *a = &m.pos[ia * 3], *b = &m.pos[ib * 3], *c = &m.pos[ic * 3];
            float e1[3] = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, e2[3] = { c[0] - a[0], c[1] - a[1], c[2] - a[2] };
            float fn[3] = { e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0] };
            for (uint32_t v : { ia, ib, ic }) for (int k = 0; k < 3; k++) m.nrm[v * 3 + k] += fn[k];   // ponderee par l'aire
            m.tri.push_back(ia); m.tri.push_back(ib); m.tri.push_back(ic);
            m.triPart.push_back(pi);
        }
    }
    if (m.tri.empty()) return false;
    for (size_t v = 0; v + 2 < m.nrm.size(); v += 3) {
        float *nv = &m.nrm[v], l = sqrtf(nv[0] * nv[0] + nv[1] * nv[1] + nv[2] * nv[2]);
        if (l > 1e-12f) { nv[0] /= l; nv[1] /= l; nv[2] /= l; } else { nv[0] = 0; nv[1] = 1; nv[2] = 0; }
    }
    float rad = 0;
    for (int a = 0; a < 3; a++) {
        m.mn[a] = mn[a]; m.mx[a] = mx[a]; m.c[a] = (mn[a] + mx[a]) / 2;
        rad += (mx[a] - mn[a]) * (mx[a] - mn[a]) / 4;
    }
    m.dist = 3.2f * sqrtf(rad);
    // Largeur du cadrage : la boite tient dans l'image sous tous les angles (lacet complet, tangage permis) ; la
    // hauteur se cale au tangage actuel (CarCamera).
    m.fx = 1e-3f;
    for (int ia = 0; ia < 72; ia++)
        for (int ip = 0; ip <= 4; ip++) {
            float yaw = ia * 6.2831853f / 72, pitch = kPitchMin + (kPitchMax - kPitchMin) * ip / 4;
            CarRot R = { cosf(yaw), sinf(yaw), cosf(pitch), sinf(pitch) };
            for (int k = 0; k < 8; k++) {
                float X, Y, Z;
                CarXf(R, ((k & 1) ? mx[0] : mn[0]) - m.c[0], ((k & 2) ? mx[1] : mn[1]) - m.c[1], ((k & 4) ? mx[2] : mn[2]) - m.c[2], X, Y, Z);
                Z += m.dist;
                m.fx = max(m.fx, fabsf(X / Z));
            }
        }
    return true;
}

// Relit corris.mesh s'il a change (le mod le reecrit a chaque partie) ; au plus toutes les 2 s.
static void CarCheckFile()
{
    DWORD now = GetTickCount();
    std::wstring path = g_gameDir + L"MWCoop\\cache\\corris.mesh";
    if (g_car.state != 0 && path == g_car.path && now - g_car.checkT < 2000) return;
    g_car.checkT = now;
    WIN32_FILE_ATTRIBUTE_DATA a;
    bool exists = !g_gameDir.empty() && GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &a) && !(a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY);
    if (!exists) {
        if (g_car.state != -1) { g_car = CarModel(); g_car.state = -1; g_car.checkT = now; g_carDirty = true; }
        g_car.path = path;
        return;
    }
    if (g_car.state != 0 && g_car.state != -1 && path == g_car.path && CompareFileTime(&a.ftLastWriteTime, &g_car.mt) == 0) return;
    CarModel m;
    std::vector<unsigned char> d;
    m.state = ReadAll(path, d) && CarParse(d, m) ? 1 : -2;
    m.path = path;
    m.mt = a.ftLastWriteTime;
    m.checkT = now;
    g_car = std::move(m);
    g_carDirty = true;
}

struct CarSV { float x, y, iz, r, g, b; };

// Triangle en pixels (rendu 2x), profondeur en 1/z ; alpha < 0 : opaque (ecrit la profondeur), sinon vitre melangee.
static void CarTri(const CarSV &v0, const CarSV &v1, const CarSV &v2, int W, int H, uint32_t *pix, float *zb, float alpha)
{
    float area = (v1.x - v0.x) * (v2.y - v0.y) - (v1.y - v0.y) * (v2.x - v0.x);
    if (fabsf(area) < 1e-4f) return;
    int x0 = max(0, (int)floorf(min(v0.x, min(v1.x, v2.x)))), x1 = min(W - 1, (int)ceilf(max(v0.x, max(v1.x, v2.x))));
    int y0 = max(0, (int)floorf(min(v0.y, min(v1.y, v2.y)))), y1 = min(H - 1, (int)ceilf(max(v0.y, max(v1.y, v2.y))));
    if (x0 > x1 || y0 > y1) return;
    float inv = 1.0f / area;
    // poids barycentriques au centre des pixels : w0 (arete v1-v2), w1 (arete v2-v0), w2 = 1 - w0 - w1 ; pas en x (a)
    // et en y (b), valeur au pixel (x0, y0) (r)
    float a0 = -(v2.y - v1.y) * inv, b0 = (v2.x - v1.x) * inv;
    float a1 = -(v0.y - v2.y) * inv, b1 = (v0.x - v2.x) * inv;
    float a2 = -(a0 + a1), b2 = -(b0 + b1);
    float px = x0 + 0.5f, py = y0 + 0.5f;
    float r0 = ((v2.x - v1.x) * (py - v1.y) - (v2.y - v1.y) * (px - v1.x)) * inv;
    float r1 = ((v0.x - v2.x) * (py - v2.y) - (v0.y - v2.y) * (px - v2.x)) * inv;
    float r2 = 1.0f - r0 - r1;
    // attributs lineaires a l'ecran (1/z, couleur) : meme forme que les poids
    struct Pl { float at, dx, dy; };
    auto plane = [&](float q0, float q1, float q2) { return Pl{ q0 * r0 + q1 * r1 + q2 * r2, q0 * a0 + q1 * a1 + q2 * a2, q0 * b0 + q1 * b1 + q2 * b2 }; };
    Pl pz = plane(v0.iz, v1.iz, v2.iz), pr = plane(v0.r, v1.r, v2.r), pg = plane(v0.g, v1.g, v2.g), pb = plane(v0.b, v1.b, v2.b);
    const float eps = -1e-5f;
    for (int y = y0; y <= y1; y++) {
        int dy = y - y0;
        // portion de la ligne dans le triangle : w + a * k >= eps pour les trois aretes
        float lo = 0, hi = (float)(x1 - x0);
        bool empty = false;
        auto clip = [&](float w, float a) {
            if (a > 1e-12f) lo = max(lo, (eps - w) / a);
            else if (a < -1e-12f) hi = min(hi, (eps - w) / a);
            else if (w < eps) empty = true;
        };
        clip(r0 + b0 * dy, a0);
        clip(r1 + b1 * dy, a1);
        clip(r2 + b2 * dy, a2);
        if (empty || lo > hi) continue;
        int ks = (int)ceilf(lo), ke = (int)floorf(hi);
        float iz = pz.at + pz.dy * dy + pz.dx * ks, r = pr.at + pr.dy * dy + pr.dx * ks;
        float g = pg.at + pg.dy * dy + pg.dx * ks, b = pb.at + pb.dy * dy + pb.dx * ks;
        uint32_t *prow = pix + (size_t)y * W;
        float *zrow = zb + (size_t)y * W;
        for (int x = x0 + ks; x <= x0 + ke; x++, iz += pz.dx, r += pr.dx, g += pg.dx, b += pb.dx) {
            if (iz <= zrow[x]) continue;
            // (bords : l'extrapolation peut sortir de 0..255 d'un rien)
            float cr = min(max(r, 0.0f), 255.0f), cg = min(max(g, 0.0f), 255.0f), cb = min(max(b, 0.0f), 255.0f);
            if (alpha < 0) {
                zrow[x] = iz;
                prow[x] = 0xFF000000u | ((uint32_t)cr << 16) | ((uint32_t)cg << 8) | (uint32_t)cb;
            } else {   // premultiplie : dst = src * a + dst * (1 - a)
                uint32_t d = prow[x];
                float k = 1.0f - alpha;
                uint32_t A = (uint32_t)(alpha * 255 + (d >> 24) * k);
                uint32_t R = (uint32_t)min(255.0f, cr * alpha + ((d >> 16) & 255) * k);
                uint32_t G = (uint32_t)min(255.0f, cg * alpha + ((d >> 8) & 255) * k);
                uint32_t B = (uint32_t)min(255.0f, cb * alpha + (d & 255) * k);
                prow[x] = (A << 24) | (R << 16) | (G << 8) | B;
            }
        }
    }
}

// Projection (pixels du rendu 2x de taille BW x BH) d'un point de la voiture.
struct CarCam { CarRot R; float f, cx, cy, mid; };
static CarCam CarCamera(int BW, int BH)
{
    CarCam c;
    c.R = { cosf(g_carYaw), sinf(g_carYaw), cosf(g_carPitch), sinf(g_carPitch) };
    // hauteur : boite sous tous les lacets, au tangage actuel (largeur : g_car.fx, tous angles) ; les coins de la
    // boite debordent de la voiture (formes arrondies), d'ou des marges un peu plus serrees que 1
    float fy0 = 1e9f, fy1 = -1e9f;
    for (int ia = 0; ia < 72; ia++) {
        float yaw = ia * 6.2831853f / 72;
        CarRot R = { cosf(yaw), sinf(yaw), c.R.cp, c.R.sp };
        for (int k = 0; k < 8; k++) {
            float X, Y, Z;
            CarXf(R, ((k & 1) ? g_car.mx[0] : g_car.mn[0]) - g_car.c[0], ((k & 2) ? g_car.mx[1] : g_car.mn[1]) - g_car.c[1],
                  ((k & 4) ? g_car.mx[2] : g_car.mn[2]) - g_car.c[2], X, Y, Z);
            Z += g_car.dist;
            fy0 = min(fy0, Y / Z);
            fy1 = max(fy1, Y / Z);
        }
    }
    c.f = min(BW * 0.53f / g_car.fx, BH * 0.98f / (fy1 - fy0));
    c.mid = (fy0 + fy1) / 2;
    c.cx = BW * 0.5f;
    c.cy = BH * 0.5f;
    return c;
}
static void CarProject(const CarCam &c, float x, float y, float z, float &sx, float &sy, float &iz)
{
    float X, Y, Z;
    CarXf(c.R, x - g_car.c[0], y - g_car.c[1], z - g_car.c[2], X, Y, Z);
    Z += g_car.dist;
    iz = 1.0f / max(Z, 0.05f);
    sx = c.cx + c.f * X * iz;
    sy = c.cy - c.f * (Y * iz - c.mid);
}

static void CarRender(int W, int H)
{
    const int S = 2, BW = W * S, BH = H * S;
    g_carBig.assign((size_t)BW * BH, 0);
    g_carZ.assign((size_t)BW * BH, 0.0f);
    g_carPix.assign((size_t)W * H, 0);
    if (g_car.state != 1) return;
    CarCam cam = CarCamera(BW, BH);
    size_t nv = g_car.pos.size() / 3;
    static std::vector<float> vp, sv, lit;   // position vue, ecran (x, y, 1/z), eclairage (diffus, reflet ; face, dos)
    vp.resize(nv * 3); sv.resize(nv * 3); lit.resize(nv * 4);
    // Lumieres (repere de la camera, vers la lumiere) : principale en haut a gauche devant, appoint a droite.
    auto norm = [](float x, float y, float z, float *o) { float l = sqrtf(x * x + y * y + z * z); o[0] = x / l; o[1] = y / l; o[2] = z / l; };
    float L1[3], L2[3], Hh[3];
    norm(-0.5f, 0.8f, -0.45f, L1);
    norm(0.75f, 0.2f, -0.3f, L2);
    norm(L1[0], L1[1], L1[2] - 1.0f, Hh);   // demi-vecteur avec la direction de la camera (0, 0, -1)
    for (size_t v = 0; v < nv; v++) {
        const float *p = &g_car.pos[v * 3], *n = &g_car.nrm[v * 3];
        float X, Y, Z;
        CarXf(cam.R, p[0] - g_car.c[0], p[1] - g_car.c[1], p[2] - g_car.c[2], X, Y, Z);
        Z += g_car.dist;
        vp[v * 3] = X; vp[v * 3 + 1] = Y; vp[v * 3 + 2] = Z;
        float iz = 1.0f / max(Z, 0.05f);
        sv[v * 3] = cam.cx + cam.f * X * iz;
        sv[v * 3 + 1] = cam.cy - cam.f * (Y * iz - cam.mid);
        sv[v * 3 + 2] = iz;
        float nx, ny, nz;
        CarXf(cam.R, n[0], n[1], n[2], nx, ny, nz);
        for (int side = 0; side < 2; side++) {
            float s = side ? -1.0f : 1.0f;
            float d1 = s * (nx * L1[0] + ny * L1[1] + nz * L1[2]), d2 = s * (nx * L2[0] + ny * L2[1] + nz * L2[2]);
            float h = s * (nx * Hh[0] + ny * Hh[1] + nz * Hh[2]), sky = s * ny;
            lit[v * 4 + side * 2] = 0.30f + 0.10f * sky + 0.78f * max(d1, 0.0f) + 0.22f * max(d2, 0.0f);
            float hs = max(h, 0.0f), h2 = hs * hs, h4 = h2 * h2, h8 = h4 * h4, h16 = h8 * h8;
            lit[v * 4 + side * 2 + 1] = h16 * h8 * h4;   // h^28
        }
    }
    float paint[3];
    if (g_carColor >= 0) { paint[0] = ((g_carColor >> 16) & 255) / 255.0f; paint[1] = ((g_carColor >> 8) & 255) / 255.0f; paint[2] = (g_carColor & 255) / 255.0f; }
    else paint[0] = paint[1] = paint[2] = 0.80f;   // au hasard : gris clair
    uint32_t *pix = g_carBig.data();
    float *zb = g_carZ.data();
    size_t nt = g_car.tri.size() / 3;
    for (int pass = 0; pass < 2; pass++) {   // 0 : opaque ; 1 : vitres (apres, sans ecrire la profondeur)
        for (size_t t = 0; t < nt; t++) {
            const CarPart &part = g_car.parts[g_car.triPart[t]];
            if (part.glass != (pass == 1)) continue;
            uint32_t i[3] = { g_car.tri[t * 3], g_car.tri[t * 3 + 1], g_car.tri[t * 3 + 2] };
            const float *a = &vp[i[0] * 3], *b = &vp[i[1] * 3], *c = &vp[i[2] * 3];
            if (a[2] < 0.1f || b[2] < 0.1f || c[2] < 0.1f) continue;
            // face tournee vers la camera ? (normale d'Unity : cross(b - a, c - a)) ; sinon eclairee comme le dos
            float e1[3] = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, e2[3] = { c[0] - a[0], c[1] - a[1], c[2] - a[2] };
            float fn[3] = { e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0] };
            int side = fn[0] * a[0] + fn[1] * a[1] + fn[2] * a[2] < 0 ? 0 : 1;
            const float *base = part.paint ? paint : &part.r;
            float ks = part.glass ? 0.9f : part.paint ? 0.55f : 0.12f;
            CarSV s[3];
            for (int k = 0; k < 3; k++) {
                float df = lit[i[k] * 4 + side * 2], sp = lit[i[k] * 4 + side * 2 + 1] * ks;
                s[k].x = sv[i[k] * 3]; s[k].y = sv[i[k] * 3 + 1]; s[k].iz = sv[i[k] * 3 + 2];
                s[k].r = min(255.0f, (base[0] * df + sp) * 255);
                s[k].g = min(255.0f, (base[1] * df + sp) * 255);
                s[k].b = min(255.0f, (base[2] * df + sp) * 255);
            }
            CarTri(s[0], s[1], s[2], BW, BH, pix, zb, pass ? 0.4f : -1.0f);
        }
    }
    // Reduction 2x2 (antialiasing) ; moyenne des quatre canaux premultiplies
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++) {
            const uint32_t *q = pix + (size_t)(y * 2) * BW + x * 2;
            uint32_t c4[4] = { q[0], q[1], q[BW], q[BW + 1] }, out = 0;
            for (int sh = 0; sh < 32; sh += 8) {
                uint32_t sum = 0;
                for (uint32_t c : c4) sum += (c >> sh) & 255;
                out |= ((sum + 2) / 4) << sh;
            }
            g_carPix[(size_t)y * W + x] = out;
        }
}

static int CarSwatchColor(int i) { return i <= 0 ? -1 : kSwatches[i - 1].rgb; }
// Pastille du choix actuel : 0 au hasard, 1.. une teinte, -1 couleur personnalisee
static int CarSwatchSel()
{
    if (g_carColor < 0) return 0;
    for (int i = 1; i < kSwatchN; i++) if (kSwatches[i - 1].rgb == g_carColor) return i;
    return -1;
}
static RectF CarSwatchRect(int i) { return RectF(458 + (i % 8) * 36.0f, 422 + (i / 8) * 36.0f, 28, 28); }
static int CarSwatchAt(float x, float y)
{
    for (int i = 0; i < kSwatchN; i++) {
        RectF r = CarSwatchRect(i);
        r.Inflate(3, 3);
        if (r.Contains(x, y)) return i;
    }
    return -1;
}
static Color CarRgb(int rgb, BYTE a = 255) { return Color(a, (BYTE)(rgb >> 16), (BYTE)(rgb >> 8), (BYTE)rgb); }

static void CarSetColor(int rgb)
{
    g_carColor = rgb;
    g_carDirty = true;
    if (g_gameDir.empty()) return;
    EnsureModDir();
    char v[16] = "";
    if (rgb >= 0) sprintf_s(v, "%06X", rgb & 0xFFFFFF);
    WritePrivateProfileStringA("Coop", "CouleurVoiture", v, ModIniA().c_str());
}

// Selecteur de couleur de Windows (couleurs perso : les teintes de la palette).
static void CarPickColor()
{
    static COLORREF custom[16];
    static bool init;
    if (!init) {
        for (int i = 0; i < 16 && i < (int)_countof(kSwatches); i++) { int c = kSwatches[i].rgb; custom[i] = RGB(c >> 16, (c >> 8) & 255, c & 255); }
        init = true;
    }
    int cur = g_carColor >= 0 ? g_carColor : 0xC8C8C8;
    CHOOSECOLORW cc = { sizeof(cc) };
    cc.hwndOwner = g_wnd;
    cc.lpCustColors = custom;
    cc.rgbResult = RGB(cur >> 16, (cur >> 8) & 255, cur & 255);
    cc.Flags = CC_FULLOPEN | CC_RGBINIT | CC_ANYCOLOR;
    if (ChooseColorW(&cc)) CarSetColor((GetRValue(cc.rgbResult) << 16) | (GetGValue(cc.rgbResult) << 8) | GetBValue(cc.rgbResult));
}

// Camembert de six teintes (pastille "au hasard", bouton "Autre couleur").
static void DrawCarWheel(Graphics &g, RectF r)
{
    static const int pick[] = { 5, 2, 10, 1, 13, 3 };
    for (int k = 0; k < 6; k++) { SolidBrush b(CarRgb(kSwatches[pick[k]].rgb)); g.FillPie(&b, r, k * 60.0f - 90, 60.0f); }
}

static void DrawCar(Graphics &g)
{
    CarCheckFile();
    DrawPanel(g);
    RectF view = kCarView;
    {   // carte de l'apercu : leger degrade
        GraphicsPath cp;
        RoundRect(cp, view, 12);
        LinearGradientBrush lg(RectF(view.X, view.Y - 1, view.Width, view.Height + 2), Mix(TH(card), kAcc, g_dark ? 0.10f : 0.06f), TH(card), LinearGradientModeVertical);
        g.FillPath(&lg, &cp);
        Pen pen(TH(choiceBorder), 1.2f);
        g.DrawPath(&pen, &cp);
    }
    if (g_car.state != 1) {
        const wchar_t *msg = g_car.state == -2 ? T(L"Aper\u00E7u illisible (MWCoop\\cache\\corris.mesh) : lancez une partie pour le refaire.", L"Preview unreadable (MWCoop\\cache\\corris.mesh): start a game to rebuild it.")
                                               : T(L"Lance une partie une fois pour voir l'aper\u00E7u de la voiture", L"Start a game once to see the car preview");
        FontFamily fam(L"Segoe UI");
        Font font(&fam, 13.5f, FontStyleRegular, UnitPixel);
        StringFormat sf;
        sf.SetAlignment(StringAlignmentCenter);
        sf.SetLineAlignment(StringAlignmentCenter);
        SolidBrush gb(kGrey);
        g.DrawString(msg, -1, &font, RectF(view.X + 40, view.Y, view.Width - 80, view.Height), &sf, &gb);
    } else {
        int W = max(8, (int)(view.Width * g_scale + 0.5f)), H = max(8, (int)(view.Height * g_scale + 0.5f));
        DWORD now = GetTickCount();
        if (W != g_carW || H != g_carH || g_carDirty || g_carDrawnColor != g_carColor || now - g_carDrawT >= 33) {
            g_carW = W; g_carH = H;
            g_carDirty = false;
            g_carDrawnColor = g_carColor;
            g_carDrawT = now;
            CarRender(W, H);
        }
        {   // ombre douce sous la voiture : empreinte au sol projetee
            CarCam cam = CarCamera(W * 2, H * 2);
            float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
            for (int k = 0; k < 4; k++) {
                float sx, sy, iz;
                CarProject(cam, (k & 1) ? g_car.mx[0] : g_car.mn[0], g_car.mn[1], (k & 2) ? g_car.mx[2] : g_car.mn[2], sx, sy, iz);
                x0 = min(x0, sx); x1 = max(x1, sx); y0 = min(y0, sy); y1 = max(y1, sy);
            }
            float k = 1.0f / (2 * g_scale);
            RectF sr(view.X + x0 * k, view.Y + y0 * k, (x1 - x0) * k, (y1 - y0) * k);
            sr.Inflate(sr.Width * 0.06f, max(6.0f, sr.Height * 0.12f));
            sr.Offset(0, 4);
            GraphicsPath sp;
            sp.AddEllipse(sr);
            PathGradientBrush pb(&sp);
            pb.SetCenterColor(Color(g_dark ? 150 : 90, 0, 0, 0));
            Color edge(0, 0, 0, 0);
            int one = 1;
            pb.SetSurroundColors(&edge, &one);
            g.SetClip(view);
            g.FillPath(&pb, &sp);
            g.ResetClip();
        }
        Bitmap bm(g_carW, g_carH, g_carW * 4, PixelFormat32bppPARGB, (BYTE *)g_carPix.data());
        InterpolationMode im = g.GetInterpolationMode();
        g.SetInterpolationMode(InterpolationModeNearestNeighbor);   // 1 pixel pour 1 pixel (deja lisse)
        g.DrawImage(&bm, view);
        g.SetInterpolationMode(im);
        Text(g, T(L"Glisse pour tourner", L"Drag to rotate"), RectF(view.X + 12, view.Y + view.Height - 24, view.Width - 24, 18), 10.5f, FontStyleRegular, WithA(kGrey, 0.85f), StringAlignmentFar);
    }

    // Choix de la couleur : nom (survol, sinon choix actuel) et pastilles
    int sel = CarSwatchSel(), show = g_carHot >= 0 ? g_carHot : sel;
    wchar_t hex[16] = L"";
    std::wstring name;
    if (show == 0) name = T(L"Couleur au hasard (comme le jeu)", L"Random color (like the game)");
    else {
        int c = show > 0 ? kSwatches[show - 1].rgb : g_carColor;
        swprintf_s(hex, L" \u00B7 #%06X", c & 0xFFFFFF);
        name = (show > 0 ? (g_fr ? kSwatches[show - 1].fr : kSwatches[show - 1].en) : T(L"Couleur personnalis\u00E9e", L"Custom color")) + std::wstring(hex);
    }
    Text(g, T(L"COULEUR DE LA CARROSSERIE", L"BODY COLOR"), RectF(458, 402, 220, 18), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
    Text(g, name, RectF(640, 402, 296, 18), 12, FontStyleBold, kInk, StringAlignmentFar);
    for (int i = 0; i < kSwatchN; i++) {
        RectF r = CarSwatchRect(i);
        if (i == sel) { Pen ring(kAcc, 2.2f); g.DrawEllipse(&ring, r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8); }
        else if (i == g_carHot) { Pen ring(WithA(kGrey, 0.75f), 1.4f); g.DrawEllipse(&ring, r.X - 3.5f, r.Y - 3.5f, r.Width + 7, r.Height + 7); }
        if (i == 0) {   // au hasard : camembert et point d'interrogation
            DrawCarWheel(g, r);
            SolidBrush mid(TH(card));
            g.FillEllipse(&mid, r.X + 7, r.Y + 7, r.Width - 14, r.Height - 14);
            Text(g, L"?", RectF(r.X, r.Y + 0.5f, r.Width, r.Height), 11.5f, FontStyleBold, kInk);
        } else {
            SolidBrush b(CarRgb(kSwatches[i - 1].rgb));
            g.FillEllipse(&b, r);
        }
        Pen edge(WithA(kInk, 0.22f), 1.0f);
        g.DrawEllipse(&edge, r);
    }
    {   // Autre couleur... : pilule ; la couleur personnalisee y est montree (et entouree) quand elle est choisie
        Button &b = g_btn[B_COLOR];
        RectF r = b.r;
        if (g_pressed == B_COLOR && g_hot == B_COLOR) r.Offset(0, 1);
        GraphicsPath p;
        RoundRect(p, r, r.Height / 2);
        SolidBrush fill(Mix(TH(btn2), TH(btn2Hot), b.hover));
        g.FillPath(&fill, &p);
        Pen pen(sel < 0 ? kAcc : WithA(Mix(kGrey, kAcc, b.hover), 0.8f), sel < 0 ? 2.2f : 1.2f);
        g.DrawPath(&pen, &p);
        RectF dot(r.X + 7, r.Y + 6, r.Height - 12, r.Height - 12);
        if (sel < 0) { SolidBrush db(CarRgb(g_carColor)); g.FillEllipse(&db, dot); }
        else DrawCarWheel(g, dot);
        Pen de(WithA(kInk, 0.22f), 1.0f);
        g.DrawEllipse(&de, dot);
        Text(g, T(L"Autre couleur\u2026", L"Other color\u2026"), RectF(dot.X + dot.Width + 4, r.Y, r.Width - dot.Width - 18, r.Height), 12.5f, FontStyleBold, Mix(kInk, kAcc, b.hover));
    }
    Pen sep(TH(sep), 1);
    g.DrawLine(&sep, kOptPanel.X + 18, 532.0f, kOptPanel.X + kOptPanel.Width - 18, 532.0f);
    Para(g, T(L"S'applique \u00E0 une nouvelle partie ; l'h\u00F4te la donne aux invit\u00E9s.", L"Applies to a new game; the host gives it to the guests."),
         RectF(kOptPanel.X + 20, 536, kOptPanel.Width - 40, 44), 12, kGrey, StringAlignmentCenter);
}

// Clic dans l'onglet : une pastille, ou l'apercu (debut du glisser).
static bool CarMouseDown(float x, float y)
{
    int s = CarSwatchAt(x, y);
    if (s >= 0) { CarSetColor(CarSwatchColor(s)); return true; }
    if (g_car.state == 1 && kCarView.Contains(x, y)) {
        g_carDrag = true;
        g_carDragX = x; g_carDragY = y;
        SetCapture(g_wnd);
        return true;
    }
    return kOptPanel.Contains(x, y);
}
static void CarDragTo(float x, float y)
{
    g_carYaw = fmodf(g_carYaw - (x - g_carDragX) * 0.012f + 6.2831853f, 6.2831853f);
    g_carPitch = min(max(g_carPitch + (y - g_carDragY) * 0.008f, kPitchMin), kPitchMax);
    g_carDragX = x; g_carDragY = y;
    g_carDirty = true;
}

// ---------------------------------------------------------------- onglet TENUE (Apparence, apercu facon GTA)
// Images pre-rendues par le mod dans <jeu>\MWCoop\cache\skins\ (apres ~30 s en jeu) :
//   <tenue>.png          : bande de 16 vues de 160 x 320 (RGBA, fond transparent) ; vue i = personnage tourne de
//                          i x 22,5 degres, vue 0 face a la camera ;
//   <tenue>-portrait.png : 128 x 128, tete et epaules (aussi dans le salon) ;
//   index.txt            : "MWSK 1 <version du mod>" puis "<tenue>\t<libelle>" par ligne ; ecrit en dernier : sans lui
//                          (ou avec un autre format), le dossier est ignore.
// Cache : PNG lus en memoire (le mod peut les reecrire, rien n'est garde ouvert) et copies en PARGB ; tout est oublie
// quand index.txt change (date verifiee au plus toutes les 3 s). Portraits charges au besoin (quelques-uns par image
// pour ne pas figer la fenetre), bandes seulement pour la tenue montree (6 au plus en memoire, ~3 Mo chacune).
// Choisir une tenue ecrit [Coop] Apparence comme l'onglet COOP ; dans un salon, LobbyTick l'annonce aussitot.
struct SkinPic {
    Bitmap *strip = NULL, *portrait = NULL;
    bool stripTried = false, portraitTried = false;
    DWORD used = 0;
    Bitmap *thumb[2] = {};                                  // portrait deja reduit et masque (0 galerie, 1 salon)
    int thumbW[2] = {}, thumbH[2] = {};
};
static std::map<std::string, SkinPic> g_skinPics;           // par tenue (nom en minuscules)
static std::map<std::string, std::wstring> g_skinLabels;    // libelles de index.txt (tenue inconnue du lanceur)
static std::wstring g_skinsArg;                             // /skins <dossier> : images de test
static std::wstring g_skinDirSeen;
static int g_skinState;                                     // 0 pas vu, 1 index.txt valide, -1 absent ou illisible
static FILETIME g_skinIdxMt;
static DWORD g_skinCheckT;
static int g_skinBudget = 1000;                             // portraits a charger pendant cette image
static bool g_skinAnnounce;                                 // salon : annoncer l'apparence sans attendre
static const RectF kSkinView(452, 128, 220, 330);
static const float kSkinCell = 46, kSkinGap = 6;
static const int kSkinCols = 5;
static float g_skinYaw;                                     // vue montree (0..16, la plus proche est dessinee)
static bool g_skinDrag;
static float g_skinDragX;
static DWORD g_skinIdleT, g_skinChangeT;                    // fin du dernier glisser / changement de tenue
static int g_skinChangeDir = 1;
static int g_skinHot = -1, g_skinArrowHot;                  // portrait survole ; fleche survolee (-1, +1)
static Bitmap *g_skinFrame;                                 // vue montree, deja a la taille de l'ecran
static const Bitmap *g_skinFrameSrc;
static int g_skinFrameN = -1, g_skinFrameW, g_skinFrameH;

// PNG en memoire -> copie PARGB (dessin rapide) ; NULL si illisible ou trop grand.
static Bitmap *LoadPngMem(const void *p, size_t n, UINT maxW, UINT maxH)
{
    IStream *s = SHCreateMemStream((const BYTE *)p, (UINT)n);
    if (!s) return NULL;
    Bitmap *copy = NULL;
    Bitmap *b = Bitmap::FromStream(s);
    if (b && b->GetLastStatus() == Ok && b->GetWidth() > 0 && b->GetHeight() > 0 && b->GetWidth() <= maxW && b->GetHeight() <= maxH) {
        copy = new Bitmap(b->GetWidth(), b->GetHeight(), PixelFormat32bppPARGB);
        if (copy->GetLastStatus() != Ok) { delete copy; copy = NULL; }
        else {
            Graphics g(copy);
            g.SetCompositingMode(CompositingModeSourceCopy);
            g.DrawImage(b, 0, 0, b->GetWidth(), b->GetHeight());
        }
    }
    delete b;
    s->Release();
    return copy;
}

static std::wstring SkinsDir() { return !g_skinsArg.empty() ? g_skinsArg : g_gameDir.empty() ? L"" : g_gameDir + L"MWCoop\\cache\\skins\\"; }

// Nom de tenue sur (il vient aussi du reseau et finit dans un chemin) : lettres, chiffres, _ et -, en minuscules.
static std::string SkinKey(const std::string &skin)
{
    if (skin.empty() || skin.size() > 64) return "";
    std::string k;
    for (char c : skin) {
        if (!isalnum((unsigned char)c) && c != '_' && c != '-') return "";
        k += (char)tolower((unsigned char)c);
    }
    return k;
}

static void SkinsFree()
{
    for (auto &kv : g_skinPics) { delete kv.second.strip; delete kv.second.portrait; delete kv.second.thumb[0]; delete kv.second.thumb[1]; }
    g_skinPics.clear();
    delete g_skinFrame;
    g_skinFrame = NULL;
    g_skinFrameSrc = NULL;
}

// index.txt apparu, disparu ou reecrit : cache oublie. Au plus toutes les 3 s.
static void SkinsCheck()
{
    DWORD now = GetTickCount();
    std::wstring dir = SkinsDir();
    if (g_skinState != 0 && dir == g_skinDirSeen && now - g_skinCheckT < 3000) return;
    g_skinCheckT = now;
    WIN32_FILE_ATTRIBUTE_DATA a;
    bool exists = !dir.empty() && GetFileAttributesExW((dir + L"index.txt").c_str(), GetFileExInfoStandard, &a) && !(a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY);
    FILETIME mt = {};
    if (exists) mt = a.ftLastWriteTime;
    if (g_skinState != 0 && dir == g_skinDirSeen && exists == (g_skinState == 1) && CompareFileTime(&mt, &g_skinIdxMt) == 0) return;
    SkinsFree();
    g_skinLabels.clear();
    g_skinDirSeen = dir;
    g_skinIdxMt = mt;
    g_skinState = -1;
    std::vector<unsigned char> d;
    if (!exists || !ReadAll(dir + L"index.txt", d)) return;
    std::string t(d.begin(), d.end());
    if (t.size() >= 3 && (unsigned char)t[0] == 0xEF && (unsigned char)t[1] == 0xBB && (unsigned char)t[2] == 0xBF) t.erase(0, 3);
    size_t pos = 0;
    for (int line = 0; pos <= t.size(); line++) {
        size_t e = t.find('\n', pos);
        std::string l = t.substr(pos, e == std::string::npos ? std::string::npos : e - pos);
        pos = e == std::string::npos ? t.size() + 1 : e + 1;
        while (!l.empty() && (l.back() == '\r' || l.back() == ' ')) l.pop_back();
        if (line == 0) { if (l != "MWSK 1" && l.compare(0, 7, "MWSK 1 ")) return; g_skinState = 1; continue; }
        size_t tab = l.find('\t');
        std::string k = SkinKey(l.substr(0, tab));
        if (!k.empty() && tab != std::string::npos) g_skinLabels[k] = Trim(Widen(l.substr(tab + 1)));
    }
}

static SkinPic *SkinPicFor(const std::string &skin, std::string *key)
{
    if (g_skinState != 1) return NULL;
    *key = SkinKey(skin);
    return key->empty() ? NULL : &g_skinPics[*key];
}
static Bitmap *SkinLoad(const std::string &key, const wchar_t *suffix, UINT maxW, UINT maxH)
{
    std::vector<unsigned char> d;
    if (!ReadAll(g_skinDirSeen + Widen(key) + suffix, d) || d.empty()) return NULL;
    return LoadPngMem(d.data(), d.size(), maxW, maxH);
}
static Bitmap *SkinPortrait(const std::string &skin)
{
    std::string k;
    SkinPic *p = SkinPicFor(skin, &k);
    if (!p) return NULL;
    if (!p->portraitTried && g_skinBudget > 0) {
        g_skinBudget--;
        p->portraitTried = true;
        p->portrait = SkinLoad(k, L"-portrait.png", 1024, 1024);
    }
    return p->portrait;
}
static Bitmap *SkinStrip(const std::string &skin)
{
    std::string k;
    SkinPic *p = SkinPicFor(skin, &k);
    if (!p) return NULL;
    p->used = GetTickCount();
    if (!p->stripTried) {
        p->stripTried = true;
        p->strip = SkinLoad(k, L".png", 16 * 1024, 2048);
        if (p->strip && p->strip->GetWidth() < 16) { delete p->strip; p->strip = NULL; }
        for (;;) {   // au plus 6 bandes en memoire : la plus ancienne repart
            int n = 0;
            SkinPic *old = NULL;
            for (auto &kv : g_skinPics)
                if (kv.second.strip) { n++; if (&kv.second != p && (!old || kv.second.used < old->used)) old = &kv.second; }
            if (n <= 6 || !old) break;
            if (g_skinFrameSrc == old->strip) g_skinFrameSrc = NULL;
            delete old->strip; old->strip = NULL; old->stripTried = false;
        }
    }
    return p->strip;
}

// Portrait reduit une fois a la taille de l'ecran et masque (0 : carre arrondi de la galerie, 1 : cercle du salon),
// bords lisses ; ensuite copie pixel pour pixel a chaque image. Refait si la taille change.
static Bitmap *SkinThumb(const std::string &skin, int shape, float w, float h)
{
    Bitmap *pt = SkinPortrait(skin);
    if (!pt) return NULL;
    SkinPic &p = g_skinPics[SkinKey(skin)];
    int W = max(4, (int)(w * g_scale + 0.5f)), H = max(4, (int)(h * g_scale + 0.5f));
    if (p.thumb[shape] && p.thumbW[shape] == W && p.thumbH[shape] == H) return p.thumb[shape];
    delete p.thumb[shape];
    p.thumb[shape] = NULL;
    Bitmap tmp(W, H, PixelFormat32bppPARGB), *th = new Bitmap(W, H, PixelFormat32bppPARGB);
    float kx = W / w, ky = H / h;
    {
        Graphics tg(&tmp);
        tg.Clear(Color(0, 0, 0, 0));
        tg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        tg.SetPixelOffsetMode(PixelOffsetModeHalf);
        tg.DrawImage(pt, RectF(1 * kx, 2.5f * ky, (w - 2) * kx, (h - 2) * ky));
    }
    {
        Graphics g(th);
        g.Clear(Color(0, 0, 0, 0));
        g.SetSmoothingMode(SmoothingModeAntiAlias);
        GraphicsPath mask;
        if (shape == 0) RoundRect(mask, RectF(0, 0, (float)W, (float)H), 10 * kx);
        else mask.AddEllipse(RectF(0, 0, (float)W, (float)H));
        TextureBrush tb(&tmp, WrapModeClamp);
        g.FillPath(&tb, &mask);
    }
    p.thumb[shape] = th;
    p.thumbW[shape] = W;
    p.thumbH[shape] = H;
    return th;
}
// Copie d'une image deja a la taille de l'ecran (pas de reechantillonnage).
static void DrawPixels(Graphics &g, Bitmap *b, RectF r)
{
    InterpolationMode im = g.GetInterpolationMode();
    g.SetInterpolationMode(InterpolationModeNearestNeighbor);
    g.DrawImage(b, r);
    g.SetInterpolationMode(im);
}

// Pastille d'un joueur (salon) : son portrait dans un cercle borde de sa couleur, sinon l'initiale sur sa couleur.
static void DrawSkinAvatar(Graphics &g, RectF c, const std::string &skin, Color col, const std::wstring &name)
{
    Bitmap *th = SkinThumb(skin, 1, c.Width, c.Height);
    if (!th) {
        SolidBrush ab(col);
        g.FillEllipse(&ab, c);
        Text(g, name.empty() ? L"?" : name.substr(0, 1), RectF(c.X, c.Y + 0.5f, c.Width, c.Height), c.Height * 0.45f, FontStyleBold, Color(255, 255, 255, 255));
        return;
    }
    LinearGradientBrush bg(RectF(c.X, c.Y - 1, c.Width, c.Height + 2), Mix(TH(card), col, g_dark ? 0.55f : 0.40f), Mix(TH(card), col, g_dark ? 0.25f : 0.15f), LinearGradientModeVertical);
    g.FillEllipse(&bg, c);
    DrawPixels(g, th, c);
    Pen ring(col, 2.0f);
    g.DrawEllipse(&ring, c);
}

static const Opt *SkinOpt() { return OptByKey("Apparence"); }
static int SkinCount() { const Opt *ap = SkinOpt(); return ap ? (int)ap->svals.size() : 0; }
static RectF SkinCellRect(int i) { return RectF(685 + (i % kSkinCols) * (kSkinCell + kSkinGap), 154 + (i / kSkinCols) * (kSkinCell + kSkinGap), kSkinCell, kSkinCell); }
static RectF SkinArrowRect(int side) { return RectF(side < 0 ? kSkinView.X + 8 : kSkinView.X + kSkinView.Width - 38, kSkinView.Y + kSkinView.Height / 2 - 25, 30, 30); }
static int SkinCellAt(float x, float y)
{
    for (int i = 0; i < SkinCount(); i++) { RectF r = SkinCellRect(i); r.Inflate(2, 2); if (r.Contains(x, y)) return i; }
    return -1;
}
static int SkinArrowAt(float x, float y)
{
    for (int s = -1; s <= 1; s += 2) { RectF r = SkinArrowRect(s); r.Inflate(4, 4); if (r.Contains(x, y)) return s; }
    return 0;
}

static void SkinSelect(int i, int dir)
{
    const Opt *ap = SkinOpt();
    if (!ap || i < 0 || i >= (int)ap->svals.size() || i == OptGet(*ap)) return;
    OptSet(*ap, i);
    g_skinAnnounce = true;
    g_skinYaw = 0;   // nouvelle tenue : de face, puis la rotation reprend
    g_skinChangeT = g_skinIdleT = GetTickCount();
    g_skinChangeDir = dir;
}
static void SkinStep(int dir)
{
    int n = SkinCount();
    if (n <= 0) return;
    SkinSelect((OptGet(*SkinOpt()) + dir + n) % n, dir);
}

// Silhouette neutre (pas d'image) : tete et buste.
static void DrawSkinGhost(Graphics &g, float cx, float top, float k)
{
    SolidBrush b(WithA(kGrey, 0.22f));
    g.FillEllipse(&b, cx - 22 * k, top, 44 * k, 48 * k);
    GraphicsPath bp;
    RoundRect(bp, RectF(cx - 42 * k, top + 56 * k, 84 * k, 96 * k), 24 * k);
    g.FillPath(&b, &bp);
}

static void DrawSkins(Graphics &g)
{
    SkinsCheck();
    DrawPanel(g);
    const Opt *ap = SkinOpt();
    if (!ap) return;
    int n = (int)ap->svals.size(), sel = OptGet(*ap);
    const std::string &skin = ap->svals[sel];
    const std::vector<std::wstring> &lab = g_fr ? ap->labFr : ap->labEn;
    RectF view = kSkinView;
    {   // carte de l'apercu : degrade et halo derriere le personnage
        GraphicsPath cp;
        RoundRect(cp, view, 12);
        LinearGradientBrush lg(RectF(view.X, view.Y - 1, view.Width, view.Height + 2), Mix(TH(card), kAcc, g_dark ? 0.14f : 0.08f), TH(card), LinearGradientModeVertical);
        g.FillPath(&lg, &cp);
        GraphicsPath hp;
        hp.AddEllipse(view.X + 10, view.Y + 30, view.Width - 20, view.Height - 70);
        PathGradientBrush hb(&hp);
        hb.SetCenterColor(WithA(kAcc, g_dark ? 0.20f : 0.13f));
        Color edge(0, 0, 0, 0);
        int one = 1;
        hb.SetSurroundColors(&edge, &one);
        g.SetClip(&cp);
        g.FillPath(&hb, &hp);
        g.ResetClip();
        Pen pen(TH(choiceBorder), 1.2f);
        g.DrawPath(&pen, &cp);
    }
    float cx = view.X + view.Width / 2, top = view.Y + 8, figH = view.Height - 30, figW = figH / 2;
    Bitmap *strip = SkinStrip(skin), *portrait = strip ? NULL : SkinPortrait(skin);
    // changement de tenue : glisse et apparait (180 ms)
    float k = min((GetTickCount() - g_skinChangeT) / 180.0f, 1.0f);
    k = 1 - (1 - k) * (1 - k);
    if (strip) {
        {   // ombre au sol
            RectF sr(cx - figW * 0.42f, top + figH - 14, figW * 0.84f, 16);
            GraphicsPath sp;
            sp.AddEllipse(sr);
            PathGradientBrush pb(&sp);
            pb.SetCenterColor(Color(g_dark ? 150 : 80, 0, 0, 0));
            Color edge(0, 0, 0, 0);
            int one = 1;
            pb.SetSurroundColors(&edge, &one);
            g.FillPath(&pb, &sp);
        }
        float fw = strip->GetWidth() / 16.0f, fh = (float)strip->GetHeight();
        int fr = (int)floorf(g_skinYaw + 0.5f) & 15;   // vue la plus proche de l'angle
        float h = figH, w = h * fw / fh;
        if (w > view.Width - 76) { w = view.Width - 76; h = w * fh / fw; }
        RectF dst(cx - w / 2 + (1 - k) * 22 * g_skinChangeDir, top + figH - h, w, h);
        if (k < 1) {
            ColorMatrix cm = { { { 1, 0, 0, 0, 0 }, { 0, 1, 0, 0, 0 }, { 0, 0, 1, 0, 0 }, { 0, 0, 0, k, 0 }, { 0, 0, 0, 0, 1 } } };
            ImageAttributes ia;
            ia.SetColorMatrix(&cm);
            g.DrawImage(strip, dst, fr * fw, 0, fw, fh, UnitPixel, &ia);
        } else {   // vue reduite une fois (elle change ~2 fois par seconde), puis copiee
            int W = max(4, (int)(dst.Width * g_scale + 0.5f)), H = max(4, (int)(dst.Height * g_scale + 0.5f));
            if (!g_skinFrame || g_skinFrameSrc != strip || g_skinFrameN != fr || g_skinFrameW != W || g_skinFrameH != H) {
                delete g_skinFrame;
                g_skinFrame = new Bitmap(W, H, PixelFormat32bppPARGB);
                Graphics fg(g_skinFrame);
                fg.Clear(Color(0, 0, 0, 0));
                fg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
                fg.SetPixelOffsetMode(PixelOffsetModeHalf);
                fg.DrawImage(strip, RectF(0, 0, (float)W, (float)H), fr * fw, 0, fw, fh, UnitPixel);
                g_skinFrameSrc = strip; g_skinFrameN = fr; g_skinFrameW = W; g_skinFrameH = H;
            }
            DrawPixels(g, g_skinFrame, dst);
        }
        Text(g, T(L"Glisse pour tourner", L"Drag to rotate"), RectF(view.X + 8, view.Y + view.Height - 22, view.Width - 16, 16), 10.5f, FontStyleRegular, WithA(kGrey, 0.85f));
    } else if (portrait) {   // pas de bande pour cette tenue : le portrait
        g.DrawImage(portrait, RectF(cx - 64, view.Y + 90, 128, 128));
    } else {
        DrawSkinGhost(g, cx, view.Y + 40, 1.0f);
        const wchar_t *msg = g_skinState == 1 ? T(L"Pas d'aper\u00E7u pour cette tenue.", L"No preview for this outfit.")
                                              : T(L"Les aper\u00E7us des tenues apparaissent apr\u00E8s une premi\u00E8re partie (30 s en jeu) avec cette version.",
                                                  L"Outfit previews appear after a first game (30 s in game) with this version.");
        Para(g, msg, RectF(view.X + 18, view.Y + 196, view.Width - 36, view.Height - 206), 12.5f, kGrey);
    }
    // fleches : tenue precedente / suivante
    for (int s = -1; s <= 1; s += 2) {
        RectF r = SkinArrowRect(s);
        bool hot = g_skinArrowHot == s;
        SolidBrush cb(hot ? TH(circleHot) : TH(circle));
        g.FillEllipse(&cb, r);
        Pen pen(hot ? kAcc : WithA(kAcc, 0.6f), 1.2f);
        g.DrawEllipse(&pen, r);
        Text(g, s < 0 ? L"\u2039" : L"\u203A", RectF(r.X + (s < 0 ? -1.0f : 1.0f), r.Y - 2, r.Width, r.Height), 22, FontStyleBold, hot ? kAcc : WithA(kAcc, 0.85f));
    }
    // nom de la tenue, en grand ; dessous : genre et rang
    Text(g, lab[sel], RectF(view.X, view.Y + view.Height + 8, view.Width, 28), 19, FontStyleBold, kInk);
    wchar_t rank[64];
    swprintf_s(rank, L"%s \u00B7 %d / %d", ap->svals[sel].compare(0, 10, "char_shirt") ? T(L"Uniforme", L"Uniform") : T(L"Habitant", L"Local"), sel + 1, n);
    Text(g, rank, RectF(view.X, view.Y + view.Height + 36, view.Width, 18), 11.5f, FontStyleRegular, kGrey);

    // galerie : un portrait par tenue (sinon le numero ou l'initiale)
    Text(g, T(L"TENUES", L"OUTFITS"), RectF(686, 128, 100, 18), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
    if (g_skinHot >= 0 && g_skinHot < n) Text(g, lab[g_skinHot], RectF(760, 128, 178, 18), 12, FontStyleBold, kInk, StringAlignmentFar);
    for (int i = 0; i < n; i++) {
        RectF r = SkinCellRect(i);
        GraphicsPath cp;
        RoundRect(cp, r, 10);
        bool on = i == sel, hot = i == g_skinHot;
        SolidBrush cb(on ? TH(cardSel) : hot ? Mix(TH(card), TH(cardSel), 0.6f) : TH(card));
        g.FillPath(&cb, &cp);
        if (Bitmap *th = SkinThumb(ap->svals[i], 0, r.Width, r.Height)) DrawPixels(g, th, r);
        else {
            const std::string &sv = ap->svals[i];
            std::wstring t = !sv.compare(0, 10, "char_shirt") ? Widen(sv.substr(10)) : lab[i].substr(0, 2);   // (Po, Pi : policier, pilote)
            Text(g, t, r, 13, FontStyleBold, on ? kAcc : kGrey);
        }
        if (on) { GraphicsPath rp; RoundRect(rp, RectF(r.X - 2.5f, r.Y - 2.5f, r.Width + 5, r.Height + 5), 12); Pen ring(kAcc, 2.2f); g.DrawPath(&ring, &rp); }
        else { Pen edge(hot ? WithA(kGrey, 0.9f) : TH(choiceBorder), hot ? 1.4f : 1.0f); g.DrawPath(&edge, &cp); }
    }

    Pen sep(TH(sep), 1);
    g.DrawLine(&sep, kOptPanel.X + 18, 532.0f, kOptPanel.X + kOptPanel.Width - 18, 532.0f);
    Para(g, T(L"Ce que les autres joueurs voient. Fl\u00E8ches \u2190 \u2192 ou un portrait pour changer ; dans un salon, les autres le voient aussit\u00F4t.",
              L"What the other players see. Arrow keys \u2190 \u2192 or a portrait to change; in a lobby, the others see it right away."),
         RectF(kOptPanel.X + 20, 536, kOptPanel.Width - 40, 44), 12, kGrey, StringAlignmentCenter);
}

// Clic dans l'onglet : un portrait, une fleche, ou l'apercu (debut du glisser).
static bool SkinMouseDown(float x, float y)
{
    int c = SkinCellAt(x, y);
    if (c >= 0) { const Opt *ap = SkinOpt(); SkinSelect(c, ap && c < OptGet(*ap) ? -1 : 1); return true; }
    int a = SkinArrowAt(x, y);
    if (a) { SkinStep(a); return true; }
    if (kSkinView.Contains(x, y)) {
        g_skinDrag = true;
        g_skinDragX = x;
        SetCapture(g_wnd);
        return true;
    }
    return kOptPanel.Contains(x, y);
}
static void SkinDragTo(float x)
{
    g_skinYaw = fmodf(g_skinYaw - (x - g_skinDragX) / 14.0f, 16.0f);   // une vue tous les 14 px
    if (g_skinYaw < 0) g_skinYaw += 16;
    g_skinDragX = x;
}

// ---------------------------------------------------------------- interface
static void DrawUI(Graphics &g)
{
    UpdateButtons();
    std::wstring status;
    int kind;
    EnterCriticalSection(&g_cs);
    status = g_status; kind = g_statusKind;
    LeaveCriticalSection(&g_cs);
    Color sc = kind == K_OK ? kInk : kind == K_WARN ? Color(255, 205, 120, 30) : kind == K_ERR ? kRed : kGrey;
    float prog = g_progress;

    {   // theme : lune (passer en sombre) ou soleil (passer en clair)
        Button &b = g_btn[B_THEME];
        SolidBrush cb(Mix(TH(circle), TH(circleHot), b.hover));
        g.FillEllipse(&cb, b.r);
        Color ic = Mix(kInk, kAcc, b.hover);
        float cx = b.r.X + b.r.Width / 2, cy = b.r.Y + b.r.Height / 2;
        if (!g_dark) {
            SolidBrush moon(ic);
            GraphicsPath mp;
            mp.AddEllipse(cx - 6.5f, cy - 6.5f, 13.0f, 13.0f);
            Region rg(&mp);
            GraphicsPath cut;
            cut.AddEllipse(cx - 2.5f, cy - 9.0f, 13.0f, 13.0f);
            rg.Exclude(&cut);
            g.FillRegion(&moon, &rg);
        } else {
            SolidBrush sun(ic);
            g.FillEllipse(&sun, cx - 4.0f, cy - 4.0f, 8.0f, 8.0f);
            Pen ray(ic, 1.6f);
            ray.SetStartCap(LineCapRound); ray.SetEndCap(LineCapRound);
            for (int k = 0; k < 8; k++) { float a = k * 0.7854f; g.DrawLine(&ray, cx + cosf(a) * 6.5f, cy + sinf(a) * 6.5f, cx + cosf(a) * 9.0f, cy + sinf(a) * 9.0f); }
        }
    }
    if (g_btn[B_LOGS].visible) {   // journaux : feuille lignee ; libelle au survol
        Button &b = g_btn[B_LOGS];
        bool on = g_tab == TAB_LOGS;
        if (on) { SolidBrush ob(kInk); g.FillEllipse(&ob, b.r); }
        else { SolidBrush cb(Mix(TH(circle), TH(circleHot), b.hover)); g.FillEllipse(&cb, b.r); }
        Color ic = on ? TH(panel) : Mix(kInk, kAcc, b.hover);
        float cx = b.r.X + b.r.Width / 2, cy = b.r.Y + b.r.Height / 2;
        Pen pen(ic, 1.5f);
        pen.SetLineJoin(LineJoinRound); pen.SetStartCap(LineCapRound); pen.SetEndCap(LineCapRound);
        GraphicsPath sheet;
        RoundRect(sheet, RectF(cx - 5.5f, cy - 7.0f, 11.0f, 14.0f), 2.0f);
        g.DrawPath(&pen, &sheet);
        for (int k = 0; k < 3; k++) g.DrawLine(&pen, cx - 3.0f, cy - 3.5f + k * 3.5f, k == 2 ? cx + 1.0f : cx + 3.0f, cy - 3.5f + k * 3.5f);
        if (b.hover > 0.02f && !on) Text(g, T(L"Journaux", L"Logs"), RectF(b.r.X - 84, b.r.Y, 78, b.r.Height), 11.5f, FontStyleBold, WithA(kInk, b.hover), StringAlignmentFar);
    }
    for (int id : { B_MIN, B_CLOSE }) {
        Button &b = g_btn[id];
        SolidBrush cb(Mix(TH(circle), TH(circleHot), b.hover));
        g.FillEllipse(&cb, b.r);
        Pen pen(Mix(kInk, kAcc, b.hover), 1.8f);
        float cx = b.r.X + b.r.Width / 2, cy = b.r.Y + b.r.Height / 2;
        if (id == B_CLOSE) { g.DrawLine(&pen, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(&pen, cx + 5, cy - 5, cx - 5, cy + 5); }
        else g.DrawLine(&pen, cx - 5, cy, cx + 5, cy);
    }

    // PRE-ALPHA : pastille bien visible sous le logo
    {
        RectF pr(170, 184, 116, 20);
        GraphicsPath pp; RoundRect(pp, pr, 10);
        SolidBrush pb(Color(230, 206, 52, 52));
        g.FillPath(&pb, &pp);
        Text(g, L"PRE-ALPHA", pr, 11, FontStyleBold, Color(255, 255, 255, 255));
    }

    if (g_state == ST_LAUNCH || g_state == ST_CLOSING) {
        int dots = (int)(g_time * 2.5f) % 4;
        std::wstring title = T(L"My Winter Car se lance", L"My Winter Car is starting");
        title += std::wstring(dots, L'.') + std::wstring(3 - dots, L' ');
        Text(g, title, RectF(60, 300, 336, 40), 22, FontStyleBold, kInk);
        Text(g, g_launchInfo, RectF(60, 340, 336, 26), 14, FontStyleRegular, kGrey);
        DrawBar(g, RectF(96, 390, 264, 6), -2);
        Text(g, T(L"La fen\u00EAtre du jeu va appara\u00EEtre.", L"The game window will appear shortly."), RectF(60, 410, 336, 24), 12.5f, FontStyleRegular, kGrey);
        Para(g, T(L"Une petite fen\u00EAtre Unity peut d'abord demander la r\u00E9solution : choisis-la puis clique sur Play.",
                  L"A small Unity window may ask for the resolution first: pick it, then click Play."),
             RectF(76, 436, 304, 40), 11.5f, WithA(kGrey, 0.9f));
        if (!g_launchWarn.empty()) Para(g, g_launchWarn, RectF(76, 236, 304, 48), 12, Color(255, 205, 120, 30), StringAlignmentCenter);
    } else {
        DrawTabs(g);
        DrawOptions(g);
        Text(g, status, RectF(60, 212, 336, 22), 13, FontStyleBold, sc);
        if (prog != -1.0f) DrawBar(g, RectF(96, 238, 264, 5), prog);
        DrawField(g, 0, T(L"PSEUDO", L"NICKNAME"));
        DrawField(g, 1, T(L"ADRESSE DE L'H\u00D4TE", L"HOST ADDRESS"));
        const wchar_t *hostLabel = T(L"H\u00C9BERGER", L"HOST"), *joinLabel = g_joinFallback ? T(L"REJOINDRE EN JEU", L"JOIN IN GAME") : T(L"REJOINDRE", L"JOIN");
        int lobby = g_lobby;
        if (lobby == LB_HOST) { hostLabel = T(L"LANCER", L"START"); joinLabel = T(L"FERMER LE SALON", L"CLOSE LOBBY"); }
        else if (lobby == LB_GUEST) { hostLabel = g_meReady ? T(L"PR\u00CAT \u2713", L"READY \u2713") : T(L"PR\u00CAT ?", L"READY?"); joinLabel = T(L"QUITTER", L"LEAVE"); }
        else if (lobby == LB_CONNECTING) { hostLabel = T(L"CONNEXION\u2026", L"CONNECTING\u2026"); joinLabel = T(L"ANNULER", L"CANCEL"); }
        DrawButton(g, B_HOST, hostLabel, true);
        DrawButton(g, B_JOIN, joinLabel, false);
        DrawSmallButton(g, B_SOLO, T(L"JOUER EN SOLO", L"PLAY SOLO"));
        std::wstring exeLine;
        Color ec = kGrey;
        if (!g_gameDir.empty()) { exeLine = L"My Winter Car " + g_gameVer + L" \u2713"; ec = kInk; }
        else { exeLine = T(L"My Winter Car introuvable", L"My Winter Car not found"); ec = kRed; }
        Text(g, exeLine, RectF(78, 478, 192, 20), 11.5f, FontStyleBold, ec, StringAlignmentNear);
        Button &eb = g_btn[B_EXE];
        bool ok = !g_gameDir.empty();
        const wchar_t *el = ok ? T(L"Changer de dossier", L"Change folder") : T(L"Choisir l'exe\u2026", L"Choose exe\u2026");
        Color lc = Mix(ok ? kGrey : kAcc, kAcc, eb.hover);
        if (eb.visible) Text(g, el, eb.r, 11.5f, ok ?FontStyleUnderline : FontStyleBold | FontStyleUnderline, WithA(lc, eb.enabled ? 1.0f : 0.4f), StringAlignmentFar);
    }
    const Color lg = WithA(kGrey, 0.8f);
    Text(g, T(L"Mod non officiel et non commercial.", L"Unofficial, non-commercial mod."), RectF(56, 506, 344, 14), 10, FontStyleRegular, lg);
    Text(g, T(L"Non affili\u00E9 \u00E0 Amistech Games.", L"Not affiliated with Amistech Games."), RectF(56, 519, 344, 14), 10, FontStyleRegular, lg);
    Text(g, T(L"N\u00E9cessite une copie l\u00E9gale de My Winter Car.", L"Requires a legal copy of My Winter Car."), RectF(56, 532, 344, 14), 10, FontStyleRegular, lg);
    {   // Acheter le jeu : pastille avec un panier, vers la page Steam
        Button &b = g_btn[B_BUY];
        Text(g, T(L"Achetez My Winter Car :", L"Buy My Winter Car:"), RectF(56, b.r.Y, b.r.X - 56 - 8, b.r.Height), 12, FontStyleBold, kInk, StringAlignmentFar);
        GraphicsPath p;
        RoundRect(p, b.r, b.r.Height / 2);
        SolidBrush fill(Mix(TH(pill), TH(pillHot), b.hover));
        g.FillPath(&fill, &p);
        Pen cart(Color(255, 255, 255, 255), 1.6f);
        cart.SetLineJoin(LineJoinRound);
        cart.SetStartCap(LineCapRound);
        cart.SetEndCap(LineCapRound);
        float x = b.r.X + 13, y = b.r.Y + 7;
        PointF basket[] = { PointF(x - 2, y), PointF(x + 1, y), PointF(x + 3.5f, y + 9), PointF(x + 12, y + 9), PointF(x + 14, y + 3), PointF(x + 2.2f, y + 3) };
        g.DrawLines(&cart, basket, 6);
        SolidBrush white(Color(255, 255, 255, 255));
        g.FillEllipse(&white, x + 3.2f, y + 10.4f, 3.2f, 3.2f);
        g.FillEllipse(&white, x + 9.8f, y + 10.4f, 3.2f, 3.2f);
        Text(g, L"Steam", RectF(b.r.X + 30, b.r.Y, b.r.Width - 36, b.r.Height), 12, FontStyleBold, Color(255, 255, 255, 255));
    }
}

// ---------------------------------------------------------------- scene animee
// Par-dessus le fond fixe (launcher\make-art.ps1, memes coordonnees) : neige qui tombe en trois plans, fumee de la
// cheminee et du pot d'echappement, fenetres qui vacillent (feu de bois), etoiles qui scintillent et etoile filante
// (theme sombre). Tout se calcule a partir de g_sceneT, sans etat : /capture ... /temps t donne l'image a l'instant t.
static float Rnd01(int i, int k)
{
    unsigned h = (unsigned)i * 2654435761u ^ (unsigned)(k + 1) * 2246822519u;
    h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
    return (h & 0xFFFFFF) / 16777216.0f;
}

static float SceneFade(float x)   // 0 sous le panneau, 1 dans le paysage (comme le degrade du fond, de 400 a 580)
{
    if (x <= 400) return 0;
    if (x >= 580) return 1;
    float k = (x - 400) / 180.0f;
    return k * k * (3 - 2 * k);
}

static void Puff(Graphics &g, SolidBrush &b, Color c, float x, float y, float r, float a)
{
    if (a < 1) return;
    b.SetColor(Color((BYTE)min(a * 0.45f, 255.0f), c.GetR(), c.GetG(), c.GetB()));
    g.FillEllipse(&b, x - r * 1.35f, y - r * 1.35f, r * 2.7f, r * 2.7f);
    b.SetColor(Color((BYTE)min(a * 0.7f, 255.0f), c.GetR(), c.GetG(), c.GetB()));
    g.FillEllipse(&b, x - r, y - r, r * 2, r * 2);
}

static void DrawScene(Graphics &g)
{
    float t = g_sceneT;
    GraphicsPath card;
    RoundRect(card, RectF(20, 60, 960, 540), 26);
    g.SetClip(&card);
    SolidBrush b(Color(0, 0, 0, 0));

    if (g_dark) {   // etoiles qui scintillent (hors de la lune et de l'accroche)
        for (int i = 0; i < 46; i++) {
            float x = 585 + Rnd01(i, 1) * 390, y = 66 + Rnd01(i, 2) * 250;
            if ((x - 905) * (x - 905) + (y - 128) * (y - 128) < 70 * 70) continue;
            if (x > 565 && x < 865 && y > 138 && y < 275) continue;
            float s = sinf(t * (0.6f + Rnd01(i, 3) * 1.6f) + Rnd01(i, 4) * 6.283f);
            float a = s > 0 ? powf(s, 6) * (150 + 100 * Rnd01(i, 5)) : 0;
            if (a < 3) continue;
            float r = 0.7f + 0.6f * Rnd01(i, 6);
            b.SetColor(Color((BYTE)a, 225, 235, 255));
            g.FillEllipse(&b, x - r, y - r, 2 * r, 2 * r);
            if (a > 120) {   // petite croix de lumiere
                Pen pen(Color((BYTE)(a * 0.45f), 210, 225, 255), 0.7f);
                g.DrawLine(&pen, x - 4, y, x + 4, y);
                g.DrawLine(&pen, x, y - 4, x, y + 4);
            }
        }
        // etoile filante : une toutes les 17 s, 0,8 s, en haut du ciel (au-dessus de l'accroche)
        int n = (int)(t / 17);
        float p = fmodf(t, 17) - 6;
        if (p > 0 && p < 0.8f) {
            float x0 = 700 + Rnd01(n, 7) * 220, y0 = 68 + Rnd01(n, 8) * 18;
            float hx = x0 - p * 230, hy = y0 + p * 230 * 0.28f;
            float a = 230 * sinf(p / 0.8f * 3.14159f);
            PointF head(hx, hy), tail(hx + 60, hy - 60 * 0.28f);
            LinearGradientBrush lb(tail, head, Color(0, 220, 232, 255), Color((BYTE)a, 235, 242, 255));
            Pen pen(&lb, 1.4f);
            pen.SetStartCap(LineCapRound); pen.SetEndCap(LineCapRound);
            g.DrawLine(&pen, tail, head);
        }
    }

    // fenetres de la maison : feu de bois (vitres, halo, lumiere sur la neige)
    for (int k = 0; k < 2; k++) {
        float wx = k ? 744.0f : 658.0f, wy = 396;
        float f = 0.5f + 0.5f * (0.5f * sinf(t * 7.3f + k * 2.1f) + 0.3f * sinf(t * 12.7f + k * 4.4f) + 0.2f * sinf(t * 2.3f + k));
        f = min(max(f, 0.0f), 1.0f);
        b.SetColor(Color((BYTE)(10 * f), 255, 190, 110));
        g.FillEllipse(&b, wx + 14 - 38, wy + 15 - 38, 76.0f, 76.0f);
        b.SetColor(Color((BYTE)(14 + 48 * f), 255, 236, 186));
        g.FillRectangle(&b, wx + 1.2f, wy + 1.2f, 11.6f, 12.6f);
        g.FillRectangle(&b, wx + 15.2f, wy + 1.2f, 11.6f, 12.6f);
        g.FillRectangle(&b, wx + 1.2f, wy + 16.2f, 11.6f, 12.6f);
        g.FillRectangle(&b, wx + 15.2f, wy + 16.2f, 11.6f, 12.6f);
        b.SetColor(Color((BYTE)((g_dark ? 22 : 10) * f), 255, 200, 120));
        g.FillEllipse(&b, wx + 14 - 40, 454.0f, 80.0f, 16.0f);
    }

    // fumee : cheminee (monte doucement, vers la droite, s'efface avant l'accroche) et pot d'echappement
    Color smoke = g_dark ? Color(255, 200, 210, 228) : Color(255, 150, 162, 182);
    for (int i = 0; i < 8; i++) {
        const float life = 5.0f;
        float u = fmodf(t + i * life / 8, life) / life;
        float x = 751 + 22 * u + 14 * u * u + 3 * sinf(t * 0.8f + i * 1.7f) * u;
        float y = 315 - 54 * u;
        float a = (g_dark ? 64.0f : 58.0f) * min(u / 0.12f, 1.0f) * powf(1 - u, 1.4f);
        Puff(g, b, smoke, x, y, 3.5f + 11 * u, a);
    }
    for (int i = 0; i < 6; i++) {
        const float life = 2.2f;
        float u = fmodf(t + i * life / 6, life) / life;
        float x = 805 - 22 * u - 8 * u * u, y = 461 - 12 * u + 2 * sinf(t * 1.3f + i);
        float a = (g_dark ? 62.0f : 56.0f) * min(u / 0.15f, 1.0f) * powf(1 - u, 1.6f);
        Puff(g, b, smoke, x, y, 1.8f + 6 * u, a);
    }

    // neige qui tombe : loin (petits, lents), milieu, pres (gros, rapides, flous) ; vent qui forcit et retombe
    struct Layer { int n; float s0, s1, speed, sway, alpha; } layers[3] = {
        { 210, 1.0f, 1.9f, 15, 5, 150 }, { 110, 1.9f, 3.0f, 30, 9, 190 }, { 22, 3.2f, 4.6f, 58, 15, 210 } };
    float drift = 9 * t - 46.0f * cosf(t * 0.13f);   // integrale du vent 9 + 6 sin(0.13 t) px/s
    Color halo = g_dark ? Color(255, 160, 180, 220) : Color(255, 110, 140, 185);
    for (int l = 0; l < 3; l++) {
        const Layer &L = layers[l];
        for (int i = 0; i < L.n; i++) {
            float sp = L.speed * (0.8f + 0.4f * Rnd01(i, 10 + l));
            float y = 50 + fmodf(Rnd01(i, 20 + l) * 560 + sp * t, 560);
            float x = 10 + fmodf(Rnd01(i, 30 + l) * 980 + drift * L.speed / 30 + L.sway * sinf(t * (0.7f + 0.5f * Rnd01(i, 40 + l)) + Rnd01(i, 50 + l) * 6.283f) + 98000, 980);
            float fade = SceneFade(x);
            if (fade <= 0) continue;
            float a = L.alpha * (0.5f + 0.5f * Rnd01(i, 60 + l)) * fade;
            float r = (L.s0 + (L.s1 - L.s0) * Rnd01(i, 70 + l)) / 2;
            if (l == 2) {   // pres : flou (halos de plus en plus clairs vers le centre)
                Color c = g_dark ? Color(255, 232, 238, 255) : halo;
                for (int k = 0; k < 2; k++) {
                    float rr = r * (k ? 1.35f : 1.9f);
                    b.SetColor(Color((BYTE)(a * (k ? 0.22f : 0.1f)), c.GetR(), c.GetG(), c.GetB()));
                    g.FillEllipse(&b, x - rr, y - rr, 2 * rr, 2 * rr);
                }
                r *= 0.75f;
            } else if (!g_dark) {   // theme clair : contour bleute (flocons blancs sur ciel pale)
                b.SetColor(Color((BYTE)min(a * 0.5f, 255.0f), halo.GetR(), halo.GetG(), halo.GetB()));
                g.FillEllipse(&b, x - r * 1.6f, y - r * 1.6f, r * 3.2f, r * 3.2f);
            }
            b.SetColor(Color((BYTE)a, g_dark ? 232 : 255, g_dark ? 238 : 255, 255));
            g.FillEllipse(&b, x - r, y - r, 2 * r, 2 * r);
        }
    }
    g.ResetClip();
}

static void RenderTo(Bitmap &target, float scale)
{
    g_skinBudget = g_wnd ? 6 : 100000;   // portraits lus par image (capture : tous)
    Graphics g(&target);
    g.Clear(Color(0, 0, 0, 0));
    Bitmap *bgi = (g_dark && g_bgDark) ? g_bgDark : g_bg;
    if (bgi) {
        int w = (int)target.GetWidth(), h = (int)target.GetHeight();
        if (!g_bgCache || g_bgCacheSrc != bgi || (int)g_bgCache->GetWidth() != w || (int)g_bgCache->GetHeight() != h) {
            delete g_bgCache;
            g_bgCache = new Bitmap(w, h, PixelFormat32bppPARGB);
            g_bgCacheSrc = bgi;
            Graphics cg(g_bgCache);
            cg.Clear(Color(0, 0, 0, 0));
            cg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
            cg.SetPixelOffsetMode(PixelOffsetModeHalf);
            cg.ScaleTransform(scale, scale);
            cg.DrawImage(bgi, RectF(0, 0, kImgW, kImgH));
        }
        g.SetCompositingMode(CompositingModeSourceCopy);   // (copie telle quelle, a la meme taille)
        g.SetInterpolationMode(InterpolationModeNearestNeighbor);
        g.DrawImage(g_bgCache, 0, 0, w, h);
        g.SetCompositingMode(CompositingModeSourceOver);
    }
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);
    g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
    g.SetPixelOffsetMode(PixelOffsetModeHalf);
    g.ScaleTransform(scale, scale);
    if (bgi) DrawScene(g);
    else {   // pas d'image : carte simple
        GraphicsPath p;
        RoundRect(p, RectF(20, 60, 960, 540), 26);
        LinearGradientBrush lg(RectF(20, 60, 960, 540), TH(fallA), TH(fallB), LinearGradientModeVertical);
        g.FillPath(&lg, &p);
    }
    DrawUI(g);
}

static void Present()
{
    if (!g_wnd || !g_memDC) return;
    {
        Bitmap frame(g_winW, g_winH, g_winW * 4, PixelFormat32bppPARGB, (BYTE *)g_bits);
        RenderTo(frame, g_scale);
    }
    GdiFlush();
    RECT wr;
    GetWindowRect(g_wnd, &wr);
    POINT dst = { wr.left, wr.top }, src = { 0, 0 };
    SIZE sz = { g_winW, g_winH };
    BLENDFUNCTION bf = { AC_SRC_OVER, 0, (BYTE)(255 * min(max(g_alpha, 0.0f), 1.0f)), AC_SRC_ALPHA };
    HDC screen = GetDC(NULL);
    UpdateLayeredWindow(g_wnd, screen, &dst, &sz, g_memDC, &src, 0, &bf, ULW_ALPHA);
    ReleaseDC(NULL, screen);
}

// ---------------------------------------------------------------- actions
static void ChooseExe()
{
    wchar_t file[MAX_PATH] = L"mywintercar.exe";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"mywintercar.exe\0mywintercar.exe\0*.exe\0*.exe\0";
    of.lpstrFile = file;
    of.nMaxFile = MAX_PATH;
    std::wstring init = !g_gameDir.empty() ? g_gameDir : g_dir;
    of.lpstrInitialDir = init.c_str();
    of.lpstrTitle = T(L"Choisir mywintercar.exe (dossier de My Winter Car)", L"Choose mywintercar.exe (My Winter Car folder)");
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&of)) return;
    std::wstring dir = DirOf(file);
    if (!IsGameDir(dir)) {
        MessageBoxW(g_wnd, T(L"Ce dossier ne contient pas My Winter Car.\n\nChoisis le mywintercar.exe du dossier du jeu "
                             L"(Steam : clic droit sur le jeu > G\u00E9rer > Parcourir les fichiers locaux).",
                             L"This folder does not contain My Winter Car.\n\nPick the mywintercar.exe from the game folder "
                             L"(Steam: right-click the game > Manage > Browse local files)."),
                    L"MWCoop", MB_ICONWARNING | MB_OK);
        return;
    }
    SetGame(dir);
    WritePrivateProfileStringW(L"Lanceur", L"Jeu", g_gameDir.c_str(), g_iniLauncher.c_str());
    SetStatus(K_NORMAL, L"%s", ModLabel().c_str());
    StartUpdate();
}

// Processus et fenetres du jeu (mywintercar.exe : celui lance, ou celui que Steam a relance a sa place).
static bool IsGamePid(DWORD pid)
{
    HANDLE p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!p) return false;
    wchar_t path[MAX_PATH];
    DWORD n = MAX_PATH;
    bool ok = QueryFullProcessImageNameW(p, 0, path, &n) && !_wcsicmp(path + std::wstring(path).find_last_of(L'\\') + 1, L"mywintercar.exe");
    CloseHandle(p);
    return ok;
}
// Copie de lancement pour une installation Steam : un jeu lance depuis steamapps ne charge pas notre version.dll
// (Steam y injecte son overlay au demarrage, et la version.dll de Windows est deja en memoire quand le jeu cherche
// la sienne ; vu le 05/10 sur l'installation de JD, alors que les copies hors de steamapps marchent). On lance
// donc, comme les copies COOPTEST, une copie de l'exe dans %LOCALAPPDATA%\MWCoop\jeu, les gros dossiers et
// MWCoop relies par jonctions (memes donnees, reglages, journaux et sauvegardes). Vide : pas de copie.
static std::wstring MirrorDir()
{
    std::wstring low = g_gameDir;
    for (auto &c : low) c = towlower(c);
    if (low.find(L"\\steamapps\\") == std::wstring::npos || LocalDir().empty()) return L"";
    return LocalDir() + L"jeu\\";
}

static std::wstring GameExe() { std::wstring m = MirrorDir(); return (m.empty() ? g_gameDir : m) + L"mywintercar.exe"; }

static bool SameFile(const std::wstring &a, const std::wstring &b)
{
    WIN32_FILE_ATTRIBUTE_DATA x, y;
    if (!GetFileAttributesExW(a.c_str(), GetFileExInfoStandard, &x) || !GetFileAttributesExW(b.c_str(), GetFileExInfoStandard, &y)) return false;
    return x.nFileSizeLow == y.nFileSizeLow && x.nFileSizeHigh == y.nFileSizeHigh && CompareFileTime(&x.ftLastWriteTime, &y.ftLastWriteTime) == 0;
}

// Prepare la copie (fichiers recopies s'ils ont change, jonctions creees si absentes). Faux si impossible.
static bool PrepareMirror(const std::wstring &m)
{
    SHCreateDirectoryExW(NULL, m.c_str(), NULL);
    const wchar_t *files[] = { L"mywintercar.exe", L"steam_api64.dll", L"version.dll", L"changelog.txt" };
    for (const wchar_t *f : files) {
        std::wstring src = g_gameDir + f, dst = m + f;
        if (GetFileAttributesW(src.c_str()) == INVALID_FILE_ATTRIBUTES || SameFile(src, dst)) continue;
        if (!CopyFileW(src.c_str(), dst.c_str(), FALSE)) { TestLog("copie de lancement : %ls impossible (erreur %lu)", f, GetLastError()); return false; }
    }
    HANDLE a = CreateFileW((m + L"steam_appid.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (a != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(a, "4164420", 7, &n, NULL); CloseHandle(a); }
    const wchar_t *dirs[] = { L"mywintercar_Data", L"MWCoop", L"CD1", L"CD2", L"CD3", L"Extra", L"Images", L"Radio" };
    for (const wchar_t *d : dirs) {
        std::wstring src = g_gameDir + d, dst = m + d;
        if (GetFileAttributesW(src.c_str()) == INVALID_FILE_ATTRIBUTES || GetFileAttributesW(dst.c_str()) != INVALID_FILE_ATTRIBUTES) continue;
        DWORD code = 1;
        RunHidden(L"cmd.exe /c mklink /J \"" + dst + L"\" \"" + src + L"\"", &code);
        if (GetFileAttributesW(dst.c_str()) == INVALID_FILE_ATTRIBUTES) { TestLog("copie de lancement : jonction %ls impossible", d); return false; }
    }
    TestLog("copie de lancement prete : %ls", m.c_str());
    return true;
}

// Le jeu de CE dossier tourne-t-il deja ? Les jeux d'autres dossiers (copies pour jouer a deux sur
// un PC, instances de test) ont leur propre profil MWCoop, donc leur propre verrou d'instance unique.
static bool GameProcessRunning()
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W pe = { sizeof(pe) };
    std::wstring mine = g_gameDir + L"mywintercar.exe", mirror = GameExe();
    bool found = false;
    for (BOOL ok = Process32FirstW(snap, &pe); ok && !found; ok = Process32NextW(snap, &pe)) {
        if (_wcsicmp(pe.szExeFile, L"mywintercar.exe")) continue;
        HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pe.th32ProcessID);
        if (!h) { found = true; break; }   // inconnu : prudence
        wchar_t path[MAX_PATH];
        DWORD n = MAX_PATH;
        if (!QueryFullProcessImageNameW(h, 0, path, &n) || !_wcsicmp(path, mine.c_str()) || !_wcsicmp(path, mirror.c_str())) found = true;
        CloseHandle(h);
    }
    CloseHandle(snap);
    return found;
}
static BOOL CALLBACK ListUnityWindows(HWND h, LPARAM lp)
{
    wchar_t cls[64];
    if (GetClassNameW(h, cls, 64) && !wcscmp(cls, L"UnityWndClass")) ((std::vector<HWND> *)lp)->push_back(h);
    return TRUE;
}
// Une fenetre Unity visible de mywintercar.exe, apparue depuis le lancement ?
static bool GameWindowShown()
{
    std::vector<HWND> list;
    EnumWindows(ListUnityWindows, (LPARAM)&list);
    for (HWND h : list) {
        if (!IsWindowVisible(h) || std::find(g_preWnds.begin(), g_preWnds.end(), h) != g_preWnds.end()) continue;
        RECT r;
        DWORD pid = 0;
        GetWindowThreadProcessId(h, &pid);
        if (GetWindowRect(h, &r) && r.right - r.left >= 320 && IsGamePid(pid)) return true;
    }
    return false;
}

// MWCoop\lancement.ini (contrat avec le chargeur et le mod), ecrit d'un coup (fichier temporaire puis renomme).
// partie (hote lance depuis le salon) : "continuer" | "nouvelle" -> Partie=..., le jeu entre en partie tout seul.
static bool WriteLaunchFile(int mode, const std::wstring &addr, int port, const char *partie)
{
    const Opt *ap = OptByKey("Apparence");
    std::string skin = ap ? ap->svals[OptGet(*ap)] : "char_shirt21";
    static const char *modes[] = { "solo", "hote", "invite" };
    std::string t = "[Lancement]\r\n";
    t += std::string("Mode=") + modes[mode] + "\r\n";
    if (mode == MODE_GUEST) t += "Adresse=" + Narrow(addr, CP_UTF8) + "\r\n";
    t += "Port=" + std::to_string(port) + "\r\n";
    t += "Pseudo=" + Narrow(PlayerName(), CP_UTF8) + "\r\n";
    t += "Apparence=" + skin + "\r\n";
    t += std::string("Profil=") + (mode == MODE_GUEST ? "invite" : "") + "\r\n";
    if (partie && *partie) t += std::string("Partie=") + partie + "\r\n";
    t += "Horodatage=" + std::to_string((long long)_time64(NULL)) + "\r\n";
    EnsureModDir();
    std::wstring path = g_gameDir + L"MWCoop\\lancement.ini", tmp = path + L".tmp";
    HANDLE f = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return false;
    DWORD w = 0;
    bool ok = WriteFile(f, t.data(), (DWORD)t.size(), &w, NULL) && w == t.size();
    CloseHandle(f);
    ok = ok && MoveFileExW(tmp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING);
    if (!ok) DeleteFileW(tmp.c_str());
    return ok;
}

static void Launch(int mode, const char *partie = NULL)
{
    if (mode == MODE_SOLO) g_launchWarn.clear();   // (avertissement UDP : salon seulement)
    if (g_gameDir.empty() || g_busy || !g_modOk) { TestLog("lancement impossible (jeu=%d occupe=%d mod=%d)", (int)!g_gameDir.empty(), (int)g_busy, (int)g_modOk); return; }
    std::wstring addr = Trim(g_fields[1].text);
    const Opt *po = OptByKey("Port");
    int port = po ? OptGet(*po) : 7870;
    if (mode == MODE_GUEST) {
        if (addr.empty()) {
            SetStatus(K_ERR, T(L"Entre l'adresse de l'h\u00F4te", L"Enter the host address"));
            g_focus = 1;
            return;
        }
        size_t colon = addr.find(L':');   // adresse:port accepte
        if (colon != std::wstring::npos) {
            int p = _wtoi(addr.c_str() + colon + 1);
            if (p > 0 && p < 65536) port = p;
            addr = addr.substr(0, colon);
        }
    } else if (g_testSalon.empty() && GameProcessRunning()) {   // (instance unique : un 2e jeu hors profil se fermerait aussitot)
        SetStatus(K_ERR, T(L"My Winter Car est d\u00E9j\u00E0 lanc\u00E9 : ferme-le d'abord", L"My Winter Car is already running: close it first"));
        return;
    }
    SavePlayer();
    if (!WriteLaunchFile(mode, addr, port, partie)) {
        SetStatus(K_ERR, T(L"Impossible d'\u00E9crire MWCoop\\lancement.ini (erreur %lu)", L"Could not write MWCoop\\lancement.ini (error %lu)"), GetLastError());
        TestLog("lancement.ini : ecriture impossible");
        return;
    }
    if (!g_testSalon.empty()) {   // mode d'essai : le lancement.ini dans le journal, et JAMAIS le jeu
        std::vector<unsigned char> d;
        ReadAll(g_gameDir + L"MWCoop\\lancement.ini", d);
        std::string text(d.begin(), d.end());
        TestLog("lancement.ini ecrit (%s) :\n%s", Narrow(g_gameDir + L"MWCoop\\lancement.ini", CP_UTF8).c_str(), text.c_str());
        TestLog("test : fin (jeu NON lance, mode d'essai)");
        DestroyWindow(g_wnd);
        return;
    }
    // steam_appid.txt a cote du jeu : sans lui, le jeu lance directement se ferme et demande a Steam de le
    // relancer ; la copie relancee par Steam (overlay injecte au demarrage) a deja la version.dll de Windows en
    // memoire, et le mod ne se charge pas (vu le 05/10 sur l'installation Steam de JD). Steam doit etre ouvert.
    {
        std::wstring appid = g_gameDir + L"steam_appid.txt";
        if (GetFileAttributesW(appid.c_str()) == INVALID_FILE_ATTRIBUTES) {
            HANDLE f = CreateFileW(appid.c_str(), GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
            if (f != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(f, "4164420", 7, &n, NULL); CloseHandle(f); TestLog("steam_appid.txt cree"); }
            else TestLog("steam_appid.txt : creation impossible (erreur %lu)", GetLastError());
        }
    }
    static const wchar_t *modes[] = { L"solo", L"hote", L"invite" };
    std::wstring args = std::wstring(L"-mwcoop-mode ") + modes[mode] + L" -mwcoop-port " + std::to_wstring(port);
    if (mode == MODE_GUEST) args += L" -mwcoop-adresse " + addr + L" -mwcoop-profil invite";
    g_preWnds.clear();
    EnumWindows(ListUnityWindows, (LPARAM)&g_preWnds);
    // Lance comme un double-clic dans l'explorateur : un mode de compatibilite de l'exe peut exiger l'administrateur ;
    // CreateProcess echoue alors (erreur 740), ShellExecuteEx affiche la demande de Windows.
    std::wstring exe = g_gameDir + L"mywintercar.exe", runDir = g_gameDir;
    std::wstring mirror = MirrorDir();
    if (!mirror.empty()) {
        if (PrepareMirror(mirror)) { exe = mirror + L"mywintercar.exe"; runDir = mirror; }
        else SetStatus(K_WARN, T(L"Copie de lancement impossible : le jeu part de Steam (le mod risque de ne pas se charger)", L"Could not prepare the launch copy: starting from Steam (the mod may not load)"));
    }
    SHELLEXECUTEINFOW sei = { sizeof(sei) };
    sei.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
    sei.hwnd = g_wnd;
    sei.lpVerb = L"open";
    sei.lpFile = exe.c_str();
    sei.lpParameters = args.c_str();
    sei.lpDirectory = runDir.c_str();
    sei.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&sei) || !sei.hProcess) {
        DWORD e = GetLastError();
        if (e == ERROR_CANCELLED) SetStatus(K_WARN, T(L"Lancement annul\u00E9 (demande d'administrateur refus\u00E9e)", L"Launch cancelled (administrator prompt declined)"));
        else SetStatus(K_ERR, T(L"Impossible de lancer mywintercar.exe (erreur %lu)", L"Could not start mywintercar.exe (error %lu)"), e);
        return;
    }
    g_proc = sei.hProcess;
    g_pid = GetProcessId(sei.hProcess);
    g_launchT = GetTickCount();
    g_winSeenT = g_noProcT = 0;
    std::wstring name = PlayerName();
    wchar_t info[160];
    if (mode == MODE_HOST) swprintf_s(info, T(L"%s h\u00E9berge la partie (port %d)", L"%s is hosting (port %d)"), name.c_str(), port);
    else if (mode == MODE_GUEST) swprintf_s(info, T(L"%s rejoint %s", L"%s joins %s"), name.c_str(), addr.c_str());
    else swprintf_s(info, T(L"%s joue en solo", L"%s plays solo"), name.c_str());
    g_launchInfo = info;
    g_focus = -1;
    g_tab = -1;
    g_state = ST_LAUNCH;
}

// ---------------------------------------------------------------- salon
// Le lanceur de l'hote ouvre un salon en TCP sur le port de la partie (le jeu, lui, est en UDP sur ce meme numero :
// pas de conflit). Chaque connexion commence par "MWS1" (pas compatible avec le salon de SACoop), puis des messages
// [u16 longueur][u8 type][...] :
//   HELLO (proto, version, pseudo, apparence) -> WELCOME (numero) ou REJECT (raison : "version X", "full",
//   "started", "closed", "proto") ; STATE (joueurs : numero, pseudo, apparence, version, pret, ping ; puis le choix
//   de partie) ; READY (u8) ; SKIN (apparence changee dans l'onglet COOP) ; PING / PONG (u32, heure de l'hote) ;
//   GO (u8 partie).
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
static SOCKET g_listen = INVALID_SOCKET, g_guestSock = INVALID_SOCKET;
static SOCKET g_udpHost = INVALID_SOCKET;           // hote : ecoute UDP du salon (sous g_lcs)
static int g_udpHostErr;                            // hote : erreur de l'ouverture du port UDP (0 = ouvert)
static const DWORD kUdpWaitMs = 22000;              // hote : sans sonde apres ce delai, l'UDP de l'invite est bloque
struct Conn { SOCKET s; int id; };
static std::vector<Conn> g_conns;                   // hote : invites du salon (sous g_lcs)
static std::atomic<bool> g_goSent(false);
static std::atomic<int> g_lobbyGen(0);              // change a chaque ouverture/fermeture : messages perimes ignores
static std::wstring g_lobbyAddr, g_myAddresses;
static int g_lobbyPort = 7870;
static int g_lobbyHot = -1;                         // choix de partie survole (0 continuer, 1 nouvelle)
static std::string g_mySkinSent;                    // derniere apparence annoncee au salon
static const RectF kLobbyList(452, 172, 488, 250), kChoiceR[2] = { RectF(456, 450, 236, 32), RectF(700, 450, 236, 32) };
static const float kLobbyRowH = 58;

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
static std::string MySkin() { const Opt *ap = OptByKey("Apparence"); return ap ? ap->svals[OptGet(*ap)] : "char_shirt21"; }
static std::string MyName() { return Narrow(PlayerName(), CP_UTF8); }
static std::string MyVersion() { return g_localVer.empty() ? "dev" : Narrow(g_localVer, CP_UTF8); }   // MWCoop\version.txt
// Apparence lisible ("Tenue 21", "Policier") : libelle de l'onglet COOP, sinon le nom brut.
static std::wstring SkinLabel(const std::string &skin)
{
    if (const Opt *ap = OptByKey("Apparence"))
        for (size_t i = 0; i < ap->svals.size(); i++)
            if (!_stricmp(ap->svals[i].c_str(), skin.c_str())) return (g_fr ? ap->labFr : ap->labEn)[i];
    auto il = g_skinLabels.find(SkinKey(skin));   // tenue plus recente que ce lanceur : libelle de index.txt
    if (il != g_skinLabels.end() && !il->second.empty()) return il->second;
    return skin.empty() ? L"?" : Widen(skin, CP_UTF8);
}
static const char *PartieName(int p) { return p == PARTIE_NOUVELLE ? "nouvelle" : "continuer"; }
// Couleur de la CORRIS choisie dans l'onglet VOITURE (pour la note "nouvelle partie").
static std::wstring CarColorName()
{
    int sel = CarSwatchSel();
    if (sel == 0) return T(L"au hasard", L"random");
    if (sel > 0) return g_fr ? kSwatches[sel - 1].fr : kSwatches[sel - 1].en;
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
static void LobbySession(SOCKET s)
{
    std::string m;
    if (!RecvMsg(s, m)) { closesocket(s); return; }
    Rd r(m);
    int type = r.u8(), proto = r.u8();
    std::string ver = r.str(), name = r.str(), skin = r.str();
    auto reject = [&](const std::string &why) { Wr w; w.u8(M_REJECT); w.str(why); SendMsg(s, w); shutdown(s, SD_SEND); closesocket(s); };
    if (!r.ok || type != M_HELLO || proto != LB_PROTO) { TestLog("salon : connexion refusee (protocole)"); reject("proto"); return; }
    std::string mine = MyVersion();
    if (ver != mine) {
        TestLog("salon : %s refuse (sa version %s, celle de l'hote %s)", name.c_str(), ver.c_str(), mine.c_str());
        SetStatus(K_WARN, T(L"%S refus\u00E9 : version %S (la tienne : %S)", L"%S refused: version %S (yours: %S)"), name.c_str(), ver.c_str(), mine.c_str());
        reject("version " + mine);
        return;
    }
    if (g_goSent) { reject("started"); return; }
    if (g_lobby != LB_HOST) { reject("closed"); return; }
    int id = -1;
    EnterCriticalSection(&g_lcs);   // (WELCOME envoye sous le verrou : pas de STATE glisse avant lui)
    for (int k = 1; k < kLobbyMax && id < 0; k++) if (!PeerById(k)) id = k;
    if (id > 0) {
        g_peers.push_back({ id, name, skin, ver, false, 0, UDP_WAIT, GetTickCount() });
        g_conns.push_back({ s, id });
        Wr w; w.u8(M_WELCOME); w.u8(id); SendMsg(s, w);
    }
    LeaveCriticalSection(&g_lcs);
    if (id < 0) { reject("full"); return; }
    TestLog("salon : %s arrive (joueur %d, version %s, apparence %s)", name.c_str(), id, ver.c_str(), skin.c_str());
    BroadcastState();
    DWORD to = 60000;   // (l'invite repond aux pings toutes les 2 s)
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char *)&to, sizeof(to));
    while (RecvMsg(s, m)) {
        Rd q(m);
        int t = q.u8();
        EnterCriticalSection(&g_lcs);
        LobbyPeer *p = PeerById(id);
        if (p && t == M_READY) { p->ready = q.u8() != 0; TestLog("salon : %s (joueur %d) pret=%d", p->name.c_str(), id, (int)p->ready); }
        else if (p && t == M_PONG) { uint32_t sent = q.u32(); if (q.ok) p->ping = (int)(GetTickCount() - sent); }
        else if (p && t == M_SKIN) { std::string sk = q.str(); if (q.ok) { p->skin = sk; TestLog("salon : joueur %d apparence %s", id, sk.c_str()); } }
        LeaveCriticalSection(&g_lcs);
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

static void LobbyHost()
{
    SavePlayer();
    g_lobbyPort = LobbyPort();
    SOCKET ls = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    sockaddr_in a = {};
    a.sin_family = AF_INET;
    // mode d'essai : 127.0.0.1 seulement (pas de demande du pare-feu de Windows a l'ecran)
    a.sin_addr.s_addr = htonl(g_testSalon.empty() ? INADDR_ANY : INADDR_LOOPBACK);
    a.sin_port = htons((u_short)g_lobbyPort);
    BOOL excl = TRUE;
    if (ls != INVALID_SOCKET) setsockopt(ls, SOL_SOCKET, SO_EXCLUSIVEADDRUSE, (const char *)&excl, sizeof(excl));
    if (ls == INVALID_SOCKET || bind(ls, (sockaddr *)&a, sizeof(a)) != 0 || listen(ls, 8) != 0) {
        int e = WSAGetLastError();
        if (ls != INVALID_SOCKET) closesocket(ls);
        SetStatus(K_ERR, T(L"Salon impossible : port TCP %d occup\u00E9 (erreur %d)", L"Cannot open the lobby: TCP port %d in use (error %d)"), g_lobbyPort, e);
        TestLog("salon : ouverture impossible sur le port %d (erreur %d)", g_lobbyPort, e);
        return;
    }
    g_lobbyGen++;
    g_listen = ls;
    EnterCriticalSection(&g_lcs);
    g_peers.clear();
    g_conns.clear();
    g_peers.push_back({ 0, MyName(), MySkin(), MyVersion(), true, 0 });
    LeaveCriticalSection(&g_lcs);
    UdpHostOpen(g_testSalon.empty() ? INADDR_ANY : INADDR_LOOPBACK);   // (meme adresse que le salon TCP)
    g_mySkinSent = MySkin();
    g_partie = PARTIE_CONTINUER;
    g_goSent = false;
    g_myId = 0;
    g_joinFallback = false;
    g_myAddresses = LocalAddresses();
    g_lobby = LB_HOST;
    g_tab = TAB_LOBBY;
    g_scroll[TAB_LOBBY] = 0;
    g_focus = -1;
    LayoutTabs();
    HANDLE t = CreateThread(NULL, 0, AcceptThread, (void *)ls, 0, NULL);
    if (t) CloseHandle(t);
    SetStatus(K_OK, T(L"Salon ouvert \u00B7 port %d", L"Lobby open \u00B7 port %d"), g_lobbyPort);
    TestLog("salon : ouvert sur le port TCP %d (version %s, %s, %s)", g_lobbyPort, MyVersion().c_str(), MyName().c_str(), MySkin().c_str());
}

// Invites : combien, et combien pas encore prets.
static void GuestCounts(int *guests, int *notReady)
{
    *guests = *notReady = 0;
    EnterCriticalSection(&g_lcs);
    for (auto &p : g_peers) if (p.id != 0) { (*guests)++; if (!p.ready) (*notReady)++; }
    LeaveCriticalSection(&g_lcs);
}
static bool LobbyCanStart()
{
    if (g_lobby != LB_HOST || g_goSent) return false;
    int guests, notReady;
    GuestCounts(&guests, &notReady);
    return notReady == 0;
}

static void LobbyClose()
{
    g_lobbyGen++;
    g_lobby = LB_NONE;
    if (g_listen != INVALID_SOCKET) { closesocket(g_listen); g_listen = INVALID_SOCKET; }
    EnterCriticalSection(&g_lcs);
    // Port UDP rendu tout de suite : le jeu de l'hote l'ouvre juste apres (LANCER : LobbyClose puis Launch). La
    // sonde de l'invite (UdpProbeThread) s'arrete d'elle-meme sur le changement de g_lobbyGen.
    if (g_udpHost != INVALID_SOCKET) { closesocket(g_udpHost); g_udpHost = INVALID_SOCKET; }
    for (auto &c : g_conns) shutdown(c.s, SD_BOTH);   // les fils des sessions ferment leurs sockets
    g_conns.clear();
    g_peers.clear();
    if (g_guestSock != INVALID_SOCKET) shutdown(g_guestSock, SD_BOTH);   // GuestThread la ferme
    LeaveCriticalSection(&g_lcs);
    g_meReady = false;
    g_udpMine = UDP_NA;
    g_lobbyHot = -1;
    if (g_tab == TAB_LOBBY) g_tab = -1;
    LayoutTabs();
}

// LANCER : actif quand tous les invites sont prets ; sinon, une question permet de forcer.
static void HostStart()
{
    if (g_lobby != LB_HOST || g_goSent) return;
    if (g_testSalon.empty() && GameProcessRunning()) {
        SetStatus(K_ERR, T(L"My Winter Car est d\u00E9j\u00E0 lanc\u00E9 : ferme-le d'abord", L"My Winter Car is already running: close it first"));
        return;
    }
    int guests, notReady;
    GuestCounts(&guests, &notReady);
    if (notReady && g_testSalon.empty()) {
        wchar_t q[400];
        swprintf_s(q, T(L"%d invit\u00E9(s) sur %d pas encore pr\u00EAt(s).\n\nLancer quand m\u00EAme ? Leur jeu d\u00E9marrera aussi.",
                        L"%d of %d guest(s) not ready yet.\n\nStart anyway? Their game will start too."), notReady, guests);
        if (MessageBoxW(g_wnd, q, L"MWCoop", MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) != IDYES) return;
        if (g_lobby != LB_HOST || g_goSent) return;   // (le salon a pu changer pendant la question)
    }
    // UDP pas confirme pour un invite : on lance quand meme, mais on le dit (son jeu risque de ne rien recevoir).
    std::string noUdp;
    EnterCriticalSection(&g_lcs);
    for (auto &p : g_peers) if (p.id != 0 && p.udp != UDP_OK && p.udp != UDP_NA) noUdp += (noUdp.empty() ? "" : ", ") + p.name;
    LeaveCriticalSection(&g_lcs);
    g_launchWarn.clear();
    if (!noUdp.empty()) {
        wchar_t wb[300];
        swprintf_s(wb, T(L"UDP non confirm\u00E9 pour %s : redirige le port UDP %d (pas seulement TCP) sur ta box",
                         L"UDP not confirmed for %s: forward UDP port %d (not just TCP) on your router"), Widen(noUdp, CP_UTF8).c_str(), g_lobbyPort);
        g_launchWarn = wb;
        SetStatus(K_WARN, L"%s", wb);
        TestLog("salon : LANCER avec l'UDP non confirme pour %s", noUdp.c_str());
    }
    int partie = g_partie;
    g_goSent = true;
    Wr w;
    w.u8(M_GO);
    w.u8(partie);
    EnterCriticalSection(&g_lcs);
    for (auto &c : g_conns) SendMsg(c.s, w);
    LeaveCriticalSection(&g_lcs);
    TestLog("salon : GO envoye a %d invite(s) (%d pas prets), partie=%s", guests, notReady, PartieName(partie));
    for (int i = 0; i < 30; i++) {   // les invites ferment les premiers en recevant GO
        EnterCriticalSection(&g_lcs);
        bool empty = g_conns.empty();
        LeaveCriticalSection(&g_lcs);
        if (empty) break;
        Sleep(50);
    }
    LobbyClose();
    if (!g_testSalon.empty()) {   // essai : le port UDP est-il bien rendu au jeu ?
        SOCKET u = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        sockaddr_in a = {};
        a.sin_family = AF_INET;
        a.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        a.sin_port = htons((u_short)g_lobbyPort);
        bool free = u != INVALID_SOCKET && bind(u, (sockaddr *)&a, sizeof(a)) == 0;
        TestLog("udp : port UDP %d libre pour le jeu apres la fermeture du salon : %s", g_lobbyPort, free ? "oui" : "NON");
        if (u != INVALID_SOCKET) closesocket(u);
    }
    Launch(MODE_HOST, PartieName(partie));
}

// Choix de partie (hote). Nouvelle partie : confirmation (elle remplace la sauvegarde de l'hote).
static void LobbySetPartie(int p)
{
    if (g_lobby != LB_HOST || g_goSent || p == g_partie) return;
    if (p == PARTIE_NOUVELLE && g_testSalon.empty()) {
        std::wstring q = std::wstring(T(L"Commencer une NOUVELLE partie ?\n\nElle remplace ta sauvegarde actuelle de My Winter Car "
                                        L"(la partie en cours sera perdue).\n\nCouleur de la CORRIS (onglet VOITURE) : ",
                                        L"Start a NEW game?\n\nIt replaces your current My Winter Car save (the game in progress will be lost).\n\n"
                                        L"CORRIS color (CAR tab): ")) + CarColorName() + L".";
        if (MessageBoxW(g_wnd, q.c_str(), L"MWCoop", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) return;
        if (g_lobby != LB_HOST || g_goSent) return;
    }
    g_partie = p;
    TestLog("salon : partie = %s", PartieName(p));
    BroadcastState();
}

// --- invite
static void GuestSend(const Wr &w)
{
    EnterCriticalSection(&g_lcs);
    if (g_guestSock != INVALID_SOCKET) SendMsg(g_guestSock, w);
    LeaveCriticalSection(&g_lcs);
}
static SOCKET ConnectTo(const std::wstring &addr, int port, int timeoutMs)
{
    addrinfo hints = {}, *res = NULL;
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    if (getaddrinfo(Narrow(addr).c_str(), NULL, &hints, &res) != 0 || !res) return INVALID_SOCKET;
    sockaddr_in a = *(sockaddr_in *)res->ai_addr;
    freeaddrinfo(res);
    a.sin_port = htons((u_short)port);
    SOCKET s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (s == INVALID_SOCKET) return s;
    u_long nb = 1;
    ioctlsocket(s, FIONBIO, &nb);
    connect(s, (sockaddr *)&a, sizeof(a));
    fd_set wr, ex;
    FD_ZERO(&wr); FD_SET(s, &wr);
    FD_ZERO(&ex); FD_SET(s, &ex);
    timeval tv = { timeoutMs / 1000, (timeoutMs % 1000) * 1000 };
    int err = 0, len = sizeof(err);
    if (select(0, NULL, &wr, &ex, &tv) <= 0 || !FD_ISSET(s, &wr) || getsockopt(s, SOL_SOCKET, SO_ERROR, (char *)&err, &len) != 0 || err) { closesocket(s); return INVALID_SOCKET; }
    nb = 0;
    ioctlsocket(s, FIONBIO, &nb);
    DWORD to = 3000;
    setsockopt(s, SOL_SOCKET, SO_SNDTIMEO, (const char *)&to, sizeof(to));
    return s;
}
static DWORD WINAPI GuestThread(void *param)
{
    int gen = (int)(intptr_t)param;
    SOCKET s = ConnectTo(g_lobbyAddr, g_lobbyPort, 5000);
    if (s == INVALID_SOCKET) { TestLog("salon : pas de salon chez l'hote (%s:%d)", Narrow(g_lobbyAddr).c_str(), g_lobbyPort); PostMessageW(g_wnd, WM_APP_LOBBYEND, 2, gen); return 0; }
    DWORD to = 10000;
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char *)&to, sizeof(to));
    Wr hello;
    hello.u8(M_HELLO); hello.u8(LB_PROTO); hello.str(MyVersion()); hello.str(MyName()); hello.str(g_mySkinSent);
    std::string m;
    if (!SendAllS(s, "MWS1", 4) || !SendMsg(s, hello) || !RecvMsg(s, m)) {   // (ancien lanceur, autre programme sur ce port...)
        closesocket(s);
        TestLog("salon : pas de reponse du salon");
        PostMessageW(g_wnd, WM_APP_LOBBYEND, 2, gen);
        return 0;
    }
    Rd r(m);
    int t = r.u8();
    if (t == M_REJECT) {
        std::string why = r.str();
        EnterCriticalSection(&g_lcs);
        g_rejectWhy = why;
        LeaveCriticalSection(&g_lcs);
        closesocket(s);
        TestLog("salon : refuse par l'hote (%s)", why.c_str());
        PostMessageW(g_wnd, WM_APP_LOBBYEND, 1, gen);
        return 0;
    }
    if (t != M_WELCOME) { closesocket(s); PostMessageW(g_wnd, WM_APP_LOBBYEND, 2, gen); return 0; }
    int myId = r.u8();
    EnterCriticalSection(&g_lcs);
    bool stale = gen != g_lobbyGen;   // annule pendant la connexion
    if (!stale) g_guestSock = s;
    LeaveCriticalSection(&g_lcs);
    if (stale) { closesocket(s); return 0; }
    g_myId = myId;
    g_lobby = LB_GUEST;
    SetStatus(K_OK, T(L"Dans le salon de %s", L"In %s's lobby"), g_lobbyAddr.c_str());
    TestLog("salon : entre (joueur %d)", myId);
    g_udpMine = UDP_WAIT;   // test UDP : le jeu passera par la, pas par le TCP du salon
    g_hostUdpTest = false;
    if (HANDLE ut = CreateThread(NULL, 0, UdpProbeThread, (void *)(intptr_t)gen, 0, NULL)) CloseHandle(ut);
    to = 60000;   // (l'hote envoie un ping toutes les 2 s)
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char *)&to, sizeof(to));
    bool go = false;
    std::string lastList;
    while (!go && RecvMsg(s, m)) {
        Rd q(m);
        int type = q.u8();
        if (type == M_STATE) {
            int n = q.u8();
            std::vector<LobbyPeer> peers;
            std::string list;
            for (int i = 0; i < n && i < kLobbyMax; i++) {
                LobbyPeer p;
                p.id = q.u8(); p.name = q.str(); p.skin = q.str(); p.ver = q.str(); p.ready = q.u8() != 0; p.ping = q.u16();
                peers.push_back(p);
                list += (list.empty() ? "" : ", ") + p.name + " (" + (p.id == 0 ? "hote" : p.ready ? "pret" : "pas pret") + ", " + p.skin + ", " + p.ver + ")";
            }
            int partie = q.u8();
            if (q.ok && q.p < m.size()) {   // test UDP de chacun (hote recent)
                g_hostUdpTest = true;
                int k = q.u8();
                for (int i = 0; i < k && q.ok; i++) {
                    int id = q.u8(), udp = q.u8();
                    if (!q.ok) break;
                    for (auto &p : peers) if (p.id == id) p.udp = udp <= UDP_FAIL ? udp : UDP_NA;
                    if (id == g_myId && udp == UDP_OK && g_udpMine != UDP_OK) { g_udpMine = UDP_OK; TestLog("udp : l'hote a recu ma sonde"); }
                }
                q.ok = true;   // (extension abimee : la liste reste bonne)
            }
            if (q.ok) {
                EnterCriticalSection(&g_lcs);
                g_peers = peers;
                LeaveCriticalSection(&g_lcs);
                g_partie = partie == PARTIE_NOUVELLE ? PARTIE_NOUVELLE : PARTIE_CONTINUER;
                list += std::string(" ; partie=") + PartieName(g_partie);
                if (list != lastList) { TestLog("salon : liste %s", list.c_str()); lastList = list; }
            }
        } else if (type == M_PING) {
            Wr w; w.u8(M_PONG); w.u32(q.u32());
            GuestSend(w);
        } else if (type == M_GO) {
            go = true;
            int partie = q.u8();
            TestLog("salon : GO recu (partie=%s)", PartieName(partie));
            PostMessageW(g_wnd, WM_APP_GO, partie, gen);
        }
    }
    EnterCriticalSection(&g_lcs);
    g_guestSock = INVALID_SOCKET;
    LeaveCriticalSection(&g_lcs);
    closesocket(s);
    if (!go) PostMessageW(g_wnd, WM_APP_LOBBYEND, 0, gen);
    return 0;
}
static void LobbyJoin()
{
    std::wstring addr = Trim(g_fields[1].text);
    if (addr.empty()) { SetStatus(K_ERR, T(L"Entre l'adresse de l'h\u00F4te", L"Enter the host address")); g_focus = 1; return; }
    g_lobbyPort = LobbyPort();
    g_lobbyAddr = addr;
    size_t colon = addr.find(L':');   // adresse:port accepte
    if (colon != std::wstring::npos) {
        g_lobbyAddr = addr.substr(0, colon);
        int p = _wtoi(addr.c_str() + colon + 1);
        if (p > 0 && p < 65536) g_lobbyPort = p;
    }
    SavePlayer();
    g_meReady = false;
    g_joinFallback = false;
    g_myId = -1;
    EnterCriticalSection(&g_lcs);
    g_peers.clear();
    g_rejectWhy.clear();
    LeaveCriticalSection(&g_lcs);
    g_partie = PARTIE_CONTINUER;
    g_mySkinSent = MySkin();
    int gen = ++g_lobbyGen;
    g_lobby = LB_CONNECTING;
    g_tab = TAB_LOBBY;
    g_scroll[TAB_LOBBY] = 0;
    g_focus = -1;
    LayoutTabs();
    SetStatus(K_NORMAL, T(L"Connexion au salon de %s\u2026", L"Connecting to %s's lobby\u2026"), g_lobbyAddr.c_str());
    TestLog("salon : connexion a %s:%d (version %s, %s, %s)", Narrow(g_lobbyAddr).c_str(), g_lobbyPort, MyVersion().c_str(), MyName().c_str(), g_mySkinSent.c_str());
    HANDLE t = CreateThread(NULL, 0, GuestThread, (void *)(intptr_t)gen, 0, NULL);
    if (t) CloseHandle(t);
}
static void GuestToggleReady()
{
    if (g_lobby != LB_GUEST) return;
    g_meReady = !g_meReady;
    Wr w; w.u8(M_READY); w.u8(g_meReady ? 1 : 0);
    GuestSend(w);
    TestLog("salon : moi pret=%d", (int)g_meReady);
    if (g_meReady && g_udpMine == UDP_FAIL && g_hostUdpTest)
        SetStatus(K_WARN, T(L"Pr\u00EAt, mais UDP bloqu\u00E9 : l'h\u00F4te doit rediriger le port UDP %d", L"Ready, but UDP is blocked: the host must forward UDP port %d"), g_lobbyPort);
}

// Toutes les 2 s, l'hote mesure le ping de chacun ; chaque seconde (aussitot apres un choix dans l'onglet TENUE),
// l'apparence choisie (onglets COOP et TENUE) est annoncee si elle a change.
static void LobbyTick()
{
    static DWORD lastPing, lastSkin;
    DWORD now = GetTickCount();
    int lobby = g_lobby;
    if ((lobby == LB_HOST || lobby == LB_GUEST) && (now - lastSkin >= 1000 || g_skinAnnounce)) {
        lastSkin = now;
        g_skinAnnounce = false;
        std::string sk = MySkin();
        if (sk != g_mySkinSent) {
            g_mySkinSent = sk;
            if (lobby == LB_HOST) {
                EnterCriticalSection(&g_lcs);
                if (LobbyPeer *p = PeerById(0)) p->skin = sk;
                LeaveCriticalSection(&g_lcs);
                BroadcastState();
            } else { Wr w; w.u8(M_SKIN); w.str(sk); GuestSend(w); }
        }
    }
    if (lobby != LB_HOST || now - lastPing < 2000) return;
    lastPing = now;
    EnterCriticalSection(&g_lcs);   // pas de sonde UDP de l'invite apres ~20 s : bloque (box de l'hote, le plus souvent)
    for (auto &p : g_peers)
        if (p.id != 0 && p.udp == UDP_WAIT && now - p.since > kUdpWaitMs) { p.udp = UDP_FAIL; TestLog("udp : aucune sonde de %s (joueur %d)", p.name.c_str(), p.id); }
    LeaveCriticalSection(&g_lcs);
    Wr w; w.u8(M_PING); w.u32(now);
    EnterCriticalSection(&g_lcs);
    for (auto &c : g_conns) SendMsg(c.s, w);
    LeaveCriticalSection(&g_lcs);
    BroadcastState();
}

static void LobbySoundsTick()
{
    static std::vector<std::pair<int, bool>> prev;
    static bool had;
    std::vector<std::pair<int, bool>> now;
    bool in = g_lobby == LB_HOST || g_lobby == LB_GUEST;
    if (in) {
        EnterCriticalSection(&g_lcs);
        for (auto &p : g_peers) now.push_back({ p.id, p.ready });
        LeaveCriticalSection(&g_lcs);
    }
    if (!had || !in || now.empty()) { prev = now; had = in && !now.empty(); return; }
    int sound = -1;
    for (auto &n : now) {
        bool found = false;
        for (auto &o : prev) if (o.first == n.first) { found = true; if (o.second != n.second && sound < 0) sound = n.second ? SND_READY : SND_UNREADY; }
        if (!found) sound = SND_JOIN;
    }
    for (auto &o : prev) {
        bool still = false;
        for (auto &n : now) still |= n.first == o.first;
        if (!still && sound != SND_JOIN) sound = SND_LEAVE;
    }
    prev = now;
    if (sound >= 0 && g_testSalon.empty()) LobbySound(sound);   // (mode d'essai : silencieux)
}

// --- dessin du salon (panneau de droite)
static float LobbyMaxScroll()
{
    EnterCriticalSection(&g_lcs);
    int n = (int)g_peers.size();
    LeaveCriticalSection(&g_lcs);
    int rows = n + (n < kLobbyMax ? 1 : 0);
    return max(0.0f, rows * kLobbyRowH - kLobbyList.Height);
}
static int LobbyChoiceAt(float x, float y)
{
    if (g_lobby != LB_HOST || g_goSent) return -1;
    for (int i = 0; i < 2; i++) if (kChoiceR[i].Contains(x, y)) return i;
    return -1;
}

static void DrawLobby(Graphics &g)
{
    std::vector<LobbyPeer> peers;
    EnterCriticalSection(&g_lcs);
    peers = g_peers;
    LeaveCriticalSection(&g_lcs);
    int lobby = g_lobby, partie = g_partie;
    bool host = lobby == LB_HOST;
    DrawPanel(g);
    SkinsCheck();   // (portraits des joueurs)

    wchar_t head[64];
    swprintf_s(head, T(L"%d / %d joueurs", L"%d / %d players"), (int)peers.size(), kLobbyMax);
    Text(g, T(L"SALON", L"LOBBY"), RectF(460, 124, 200, 26), 17, FontStyleBold, kInk, StringAlignmentNear);
    if (lobby != LB_CONNECTING) Text(g, head, RectF(700, 124, 232, 26), 12.5f, FontStyleBold, kGrey, StringAlignmentFar);
    std::wstring sub;
    if (host) sub = std::wstring(T(L"Port ", L"Port ")) + std::to_wstring(g_lobbyPort) + T(L" (UDP et TCP) \u00B7 adresse locale : ", L" (UDP and TCP) \u00B7 local address: ") + (g_myAddresses.empty() ? L"?" : g_myAddresses);
    else sub = std::wstring(T(L"H\u00F4te : ", L"Host: ")) + g_lobbyAddr + L":" + std::to_wstring(g_lobbyPort);
    // Test UDP : l'invite (sa sonde, ou l'hote qui dit l'avoir recue) ; l'hote, chaque invite (sous la colonne du ping)
    int myUdp = g_udpMine;
    std::wstring udpLine;
    Color udpC = kGrey;
    if (lobby == LB_GUEST) {
        if (myUdp == UDP_OK) { udpLine = T(L"UDP : OK \u2713", L"UDP: OK \u2713"); udpC = kAcc; }
        else if (myUdp == UDP_FAIL && !g_hostUdpTest) udpLine = T(L"UDP : non v\u00E9rifi\u00E9 (lanceur de l'h\u00F4te ancien)", L"UDP: not checked (host has an old launcher)");
        else if (myUdp == UDP_FAIL) { udpLine = T(L"UDP : bloqu\u00E9", L"UDP: blocked"); udpC = kRed; }
        else if (myUdp == UDP_WAIT) udpLine = T(L"UDP : test en cours\u2026", L"UDP: testing\u2026");
    }
    float subW = 476;
    if (!udpLine.empty()) {
        Bitmap mb(1, 1);
        Graphics mg(&mb);
        float uw = MeasureW(mg, udpLine, 11.5f, FontStyleBold) + 4;
        Text(g, udpLine, RectF(936 - uw, 148, uw, 20), 11.5f, FontStyleBold, udpC, StringAlignmentFar);
        subW -= uw + 8;
    }
    Text(g, sub, RectF(460, 148, subW, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);

    if (lobby == LB_CONNECTING) {
        Text(g, T(L"Connexion au salon\u2026", L"Connecting to the lobby\u2026"), RectF(460, 260, 476, 30), 16, FontStyleBold, kInk);
        DrawBar(g, RectF(560, 300, 276, 5), -2);
        Para(g, T(L"Si l'h\u00F4te joue d\u00E9j\u00E0 ou n'a pas de salon, tu pourras rejoindre directement en jeu.",
                  L"If the host is already playing or has no lobby, you can join directly in game."),
             RectF(480, 320, 436, 40), 12, kGrey, StringAlignmentCenter);
    } else {
        static const int pal[kLobbyMax] = { 0x3E86D0, 0xE0702A, 0x2A9C9A, 0x8E5BD6, 0xD64545, 0xC9971A, 0xD45D9A, 0x6C7A89 };
        float sc = g_scroll[TAB_LOBBY], ms = LobbyMaxScroll();
        int rows = (int)peers.size() + ((int)peers.size() < kLobbyMax ? 1 : 0);
        g.SetClip(kLobbyList);
        for (int i = 0; i < rows; i++) {
            RectF r(kLobbyList.X + 4, kLobbyList.Y + i * kLobbyRowH - sc, kLobbyList.Width - (ms > 0 ? 16 : 8), kLobbyRowH - 6);
            if (r.Y + r.Height < kLobbyList.Y || r.Y > kLobbyList.Y + kLobbyList.Height) continue;
            GraphicsPath rp;
            RoundRect(rp, r, 11);
            if (i >= (int)peers.size()) {   // place libre
                Pen dash(WithA(kGrey, 0.55f), 1.3f);
                dash.SetDashStyle(DashStyleDash);
                g.DrawPath(&dash, &rp);
                Text(g, T(L"En attente d'un joueur\u2026", L"Waiting for a player\u2026"), r, 12.5f, FontStyleRegular, WithA(kGrey, 0.85f));
                continue;
            }
            const LobbyPeer &p = peers[i];
            bool me = p.id == g_myId;
            SolidBrush rb(me ? TH(cardSel) : TH(card));
            g.FillPath(&rb, &rp);
            Pen rpen(me ? kAcc : TH(choiceBorder), me ? 1.6f : 1.1f);
            g.DrawPath(&rpen, &rp);
            // pastille du joueur : portrait de sa tenue (onglet TENUE), sinon son initiale, sur sa couleur
            std::wstring nm = Widen(p.name, CP_UTF8);
            DrawSkinAvatar(g, RectF(r.X + 6, r.Y + 4, r.Height - 8, r.Height - 8), p.skin, CarRgb(pal[p.id % kLobbyMax]), nm);
            if (me) nm += T(L"  (toi)", L"  (you)");
            Text(g, nm, RectF(r.X + 60, r.Y + 6, 232, 20), 14, FontStyleBold, kInk, StringAlignmentNear);
            std::wstring line = SkinLabel(p.skin) + L" \u00B7 " + (p.ver == "dev" ? std::wstring(L"dev") : L"v" + Widen(p.ver, CP_UTF8));
            Text(g, line, RectF(r.X + 60, r.Y + 27, 232, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            // etat : HOTE, PRET, PAS PRET
            const wchar_t *st = p.id == 0 ? T(L"H\u00D4TE", L"HOST") : p.ready ? T(L"PR\u00CAT \u2713", L"READY \u2713") : T(L"PAS PR\u00CAT", L"NOT READY");
            RectF pr(r.X + r.Width - 172, r.Y + (r.Height - 22) / 2, 96, 22);
            GraphicsPath pp;
            RoundRect(pp, pr, 11);
            if (p.id != 0 && p.ready) { LinearGradientBrush lg(pr, kAcc, kAcc2, LinearGradientModeHorizontal); g.FillPath(&lg, &pp); }
            else if (p.id == 0) { SolidBrush hb(WithA(kAcc, 0.22f)); g.FillPath(&hb, &pp); }
            else { Pen np(WithA(kGrey, 0.8f), 1.2f); g.DrawPath(&np, &pp); }
            Text(g, st, pr, 10.5f, FontStyleBold, p.id != 0 && p.ready ? kOnAcc : p.id == 0 ? kInk : kGrey);
            if (p.id != 0) {
                wchar_t pb[32];
                swprintf_s(pb, L"%d ms", p.ping);
                int udp = me && myUdp == UDP_OK ? UDP_OK : p.udp;   // (l'invite local : sa propre sonde compte)
                if (me && udp != UDP_OK && myUdp == UDP_FAIL && g_hostUdpTest) udp = UDP_FAIL;
                if (udp == UDP_NA) Text(g, pb, RectF(r.X + r.Width - 70, r.Y, 60, r.Height), 11.5f, FontStyleRegular, kGrey, StringAlignmentFar);
                else {
                    Text(g, pb, RectF(r.X + r.Width - 74, r.Y + 3, 64, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentFar);
                    const wchar_t *ul = udp == UDP_OK ? L"UDP \u2713" : udp == UDP_FAIL ? T(L"UDP bloqu\u00E9", L"UDP blocked") : L"UDP \u2026";
                    Text(g, ul, RectF(r.X + r.Width - 94, r.Y + 22, 84, 18), 10.5f, FontStyleBold, udp == UDP_OK ? kAcc : udp == UDP_FAIL ? kRed : kGrey, StringAlignmentFar);
                }
            }
        }
        g.ResetClip();
        if (ms > 0) {
            float h = kLobbyList.Height * kLobbyList.Height / (kLobbyList.Height + ms), y = kLobbyList.Y + (kLobbyList.Height - h) * sc / ms;
            GraphicsPath sp; RoundRect(sp, RectF(kLobbyList.X + kLobbyList.Width - 6, y, 4, h), 2);
            SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
        }
    }

    // partie : continuer / nouvelle (choix de l'hote, montre aux invites)
    Text(g, host ? T(L"PARTIE", L"GAME") : T(L"PARTIE (CHOISIE PAR L'H\u00D4TE)", L"GAME (CHOSEN BY THE HOST)"), RectF(460, 428, 400, 18), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
    const wchar_t *lab[2] = { T(L"Continuer ma partie", L"Continue my game"), T(L"Nouvelle partie", L"New game") };
    if (!host) lab[0] = T(L"Continuer la partie", L"Continue the game");
    for (int i = 0; i < 2; i++) {
        RectF r = kChoiceR[i];
        GraphicsPath cp;
        RoundRect(cp, r, r.Height / 2);
        bool on = partie == i, hot = g_lobbyHot == i && host;
        float a = lobby == LB_CONNECTING ? 0.45f : 1.0f;
        if (on) { LinearGradientBrush lg(r, WithA(kAcc, a), WithA(kAcc2, a), LinearGradientModeHorizontal); g.FillPath(&lg, &cp); }
        else {
            SolidBrush cb(hot ? TH(cardSel) : TH(card)); g.FillPath(&cb, &cp);
            Pen cpen(hot ? kAcc : TH(choiceBorder), 1.2f); g.DrawPath(&cpen, &cp);
        }
        Text(g, lab[i], r, 13, FontStyleBold, on ? WithA(kOnAcc, a) : WithA(host ? kInk : kGrey, a));
    }
    std::wstring note;
    if (partie == PARTIE_NOUVELLE)
        note = host ? std::wstring(T(L"Remplace ta sauvegarde. Couleur de la CORRIS (onglet VOITURE) : ", L"Replaces your save. CORRIS color (CAR tab): ")) + CarColorName() + L"."
                    : T(L"L'h\u00F4te commence une nouvelle partie ; tu la rejoins d\u00E8s qu'il est en jeu.", L"The host starts a new game; you join as soon as they are in game.");
    else
        note = host ? T(L"Tu reprends ta sauvegarde ; les invit\u00E9s la re\u00E7oivent en arrivant.", L"You resume your save; guests receive it when they arrive.")
                    : T(L"L'h\u00F4te reprend sa sauvegarde ; tu la re\u00E7ois en arrivant (la tienne n'est pas touch\u00E9e).", L"The host resumes their save; you receive it on arrival (yours is left untouched).");
    if (lobby != LB_CONNECTING) Para(g, note, RectF(460, 488, 476, 40), 11.5f, partie == PARTIE_NOUVELLE && host ? Color(255, 205, 120, 30) : kGrey);

    Pen sep(TH(sep), 1);
    g.DrawLine(&sep, kOptPanel.X + 18, 532.0f, kOptPanel.X + kOptPanel.Width - 18, 532.0f);
    const wchar_t *hint = host ? T(L"Quand tout le monde est pr\u00EAt, LANCER d\u00E9marre le jeu de chacun ; les invit\u00E9s suivent ta partie.",
                                   L"Once everyone is ready, START launches everyone's game; the guests follow your game.")
                               : T(L"Clique sur PR\u00CAT. Ton jeu d\u00E9marre tout seul quand l'h\u00F4te lance la partie.",
                                   L"Click READY. Your game starts by itself when the host starts the session.");
    // UDP bloque (ou port UDP pris chez l'hote) : l'avertissement prend la place du conseil
    std::wstring warn;
    if (host && g_udpHostErr) {
        wchar_t wb[200];
        swprintf_s(wb, T(L"Port UDP %d d\u00E9j\u00E0 pris sur ce PC (erreur %d) : le jeu ne pourra pas l'ouvrir. Ferme le programme qui l'occupe.",
                         L"UDP port %d is already in use on this PC (error %d): the game will not be able to open it. Close the program using it."), g_lobbyPort, g_udpHostErr);
        warn = wb;
    } else if (host) {
        std::string names;
        for (auto &p : peers) if (p.id != 0 && p.udp == UDP_FAIL) names += (names.empty() ? "" : ", ") + p.name;
        if (!names.empty()) {
            wchar_t wb[300];
            swprintf_s(wb, T(L"UDP bloqu\u00E9 pour %s : redirige le port UDP %d (pas seulement TCP) sur ta box, vers ce PC. Le salon passe en TCP, le jeu en UDP.",
                             L"UDP blocked for %s: forward UDP port %d (not just TCP) on your router to this PC. The lobby uses TCP, the game uses UDP."),
                       Widen(names, CP_UTF8).c_str(), g_lobbyPort);
            warn = wb;
        }
    } else if (lobby == LB_GUEST && myUdp == UDP_FAIL && g_hostUdpTest) {
        wchar_t wb[300];
        swprintf_s(wb, T(L"UDP bloqu\u00E9 : l'h\u00F4te doit rediriger le port UDP %d (pas seulement TCP) sur sa box. Le salon passe en TCP, le jeu en UDP.",
                         L"UDP blocked: the host must forward UDP port %d (not just TCP) on their router. The lobby uses TCP, the game uses UDP."), g_lobbyPort);
        warn = wb;
    }
    if (!warn.empty()) Para(g, warn, RectF(kOptPanel.X + 20, 536, kOptPanel.Width - 40, 44), 12, Color(255, 205, 120, 30), StringAlignmentCenter);
    else Para(g, hint, RectF(kOptPanel.X + 20, 536, kOptPanel.Width - 40, 44), 12, kGrey, StringAlignmentCenter);
}

static bool LobbyClick(float x, float y)
{
    int c = LobbyChoiceAt(x, y);
    if (c >= 0) { LobbySetPartie(c); return true; }
    return kOptPanel.Contains(x, y);
}

// /testsalon hote|invite : l'hote ouvre le salon et lance des que l'invite est pret ; l'invite rejoint et se met
// pret. Abandon au bout de 60 s.
static void TestSalonStep()
{
    static DWORD start = GetTickCount(), allReadySince;
    static bool tried, readied;
    DWORD t = GetTickCount() - start;
    if (g_state != ST_IDLE || g_goWait) return;
    if (t > 60000) { TestLog("test : abandon (60 s)"); DestroyWindow(g_wnd); return; }
    if (g_testSalon == L"hote") {
        if (!tried && t > 500) {
            tried = true;
            LobbyHost();
            if (g_lobby != LB_HOST) { TestLog("test : fin (salon impossible)"); DestroyWindow(g_wnd); return; }
            if (g_testPartie == L"nouvelle") LobbySetPartie(PARTIE_NOUVELLE);
        }
        if (g_lobby != LB_HOST) return;
        int guests, notReady;
        GuestCounts(&guests, &notReady);
        bool udpDone = true;   // (et le test UDP de chacun termine : recu, ou bloque apres ~20 s)
        EnterCriticalSection(&g_lcs);
        for (auto &p : g_peers) if (p.id != 0 && p.udp == UDP_WAIT) udpDone = false;
        LeaveCriticalSection(&g_lcs);
        bool can = guests >= 1 && LobbyCanStart() && udpDone;
        if (!can) allReadySince = 0;
        else if (!allReadySince) { allReadySince = GetTickCount(); TestLog("test : tous les invites sont prets"); }
        else if (GetTickCount() - allReadySince > 1500) { TestLog("test : LANCER"); HostStart(); }
    } else {
        if (!tried && t > 2000) { tried = true; LobbyJoin(); }
        if (g_lobby == LB_GUEST && !readied && t > 4500) { readied = true; TestLog("test : clic sur PRET"); GuestToggleReady(); }
    }
}

// ---------------------------------------------------------------- page JOURNAUX : dossier, zip a envoyer
// "Ouvrir le dossier" : le dernier journal ouvert d'un clic (selectionne dans l'explorateur), sinon <jeu>\MWCoop\,
// sinon %LOCALAPPDATA%\MWCoop\ (dossier du jeu en lecture seule).
static void LogsOpenFolder()
{
    if (!g_logSelPath.empty() && FileExists(g_logSelPath)) {
        std::wstring arg = L"/select,\"" + g_logSelPath + L"\"";
        ShellExecuteW(g_wnd, L"open", L"explorer.exe", arg.c_str(), NULL, SW_SHOWNORMAL);
        return;
    }
    auto isDir = [](const std::wstring &d) { DWORD a = GetFileAttributesW(d.c_str()); return a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY); };
    std::wstring d = g_gameDir.empty() ? L"" : g_gameDir + L"MWCoop\\", ld = LocalDir();
    if ((d.empty() || !isDir(d)) && !ld.empty() && isDir(ld)) d = ld;
    else if (!d.empty() && !isDir(d)) EnsureModDir();
    if (!d.empty()) ShellExecuteW(g_wnd, L"open", d.c_str(), NULL, NULL, SW_SHOWNORMAL);
}

// Zip SANS compression (methode 0 "stored") : en-tete local + donnees par fichier, puis repertoire central et fin ;
// noms en UTF-8 (bit 11). Pas de bibliotheque, l'explorateur de Windows et tous les outils l'ouvrent.
static uint32_t Crc32(uint32_t crc, const uint8_t *p, size_t n)
{
    static uint32_t tab[256];
    if (!tab[1]) for (uint32_t i = 0; i < 256; i++) { uint32_t c = i; for (int k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320u ^ (c >> 1) : c >> 1; tab[i] = c; }
    crc = ~crc;
    while (n--) crc = tab[(crc ^ *p++) & 0xFF] ^ (crc >> 8);
    return ~crc;
}
// Contenu d'un journal (le jeu peut l'avoir encore ouvert). Au-dela de 16 Mo (output_log de Unity) : le 1er Mo et les
// derniers, avec une ligne qui le dit.
static bool ReadForZip(const std::wstring &path, std::string &out, FILETIME *mt)
{
    HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER size;
    bool ok = GetFileSizeEx(f, &size) && GetFileTime(f, NULL, NULL, mt);
    const LONGLONG cap = 16 << 20, head = 1 << 20;
    auto readAt = [&](LONGLONG at, LONGLONG n) {
        LARGE_INTEGER p; p.QuadPart = at;
        size_t base = out.size();
        out.resize(base + (size_t)n);
        DWORD got = 0;
        ok = ok && SetFilePointerEx(f, p, NULL, FILE_BEGIN) && (n == 0 || ReadFile(f, &out[base], (DWORD)n, &got, NULL));
        out.resize(base + got);
    };
    out.clear();
    if (ok && size.QuadPart <= cap) readAt(0, size.QuadPart);
    else if (ok) {
        readAt(0, head);
        char note[160];
        sprintf_s(note, "\r\n\r\n[MWCoop : %lld octets coupes ici, le journal faisait %lld octets]\r\n\r\n", size.QuadPart - cap, size.QuadPart);
        out += note;
        readAt(size.QuadPart - (cap - head), cap - head);
    }
    CloseHandle(f);
    return ok;
}
// Nom dans le zip : chemin depuis le dossier du jeu (jeu/...) ou depuis %LOCALAPPDATA%\MWCoop (LOCALAPPDATA/...).
static std::wstring ZipEntryName(const std::wstring &path)
{
    std::wstring ld = LocalDir(), rel;
    if (!g_gameDir.empty() && !_wcsnicmp(path.c_str(), g_gameDir.c_str(), g_gameDir.size())) rel = L"jeu\\" + path.substr(g_gameDir.size());
    else if (!ld.empty() && !_wcsnicmp(path.c_str(), ld.c_str(), ld.size())) rel = L"LOCALAPPDATA-MWCoop\\" + path.substr(ld.size());
    else rel = path.substr(path.find_last_of(L'\\') + 1);
    for (auto &c : rel) if (c == L'\\') c = L'/';
    return rel;
}
static bool WriteZip(const std::wstring &zip, const std::vector<std::wstring> &files, int *count)
{
    *count = 0;
    std::wstring tmp = zip + L".tmp";
    HANDLE f = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return false;
    std::string central, data;
    uint32_t off = 0;
    bool ok = true;
    auto put = [&](const std::string &b) { DWORD w = 0; ok = ok && WriteFile(f, b.data(), (DWORD)b.size(), &w, NULL) && w == b.size(); off += (uint32_t)b.size(); };
    for (const std::wstring &path : files) {
        FILETIME mt = {}, lt;
        if (!ReadForZip(path, data, &mt)) continue;
        std::string name = Narrow(ZipEntryName(path), CP_UTF8);
        WORD dd = 0x21, dt = 0;   // (1/1/1980 si la date est illisible)
        FileTimeToLocalFileTime(&mt, &lt);
        FileTimeToDosDateTime(&lt, &dd, &dt);
        uint32_t crc = Crc32(0, (const uint8_t *)data.data(), data.size()), size = (uint32_t)data.size();
        Wr h;
        h.u32(0x04034b50); h.u16(20); h.u16(0x0800); h.u16(0); h.u16(dt); h.u16(dd);
        h.u32(crc); h.u32(size); h.u32(size); h.u16((int)name.size()); h.u16(0);
        h.d += name;
        Wr c;
        c.u32(0x02014b50); c.u16(20); c.u16(20); c.u16(0x0800); c.u16(0); c.u16(dt); c.u16(dd);
        c.u32(crc); c.u32(size); c.u32(size); c.u16((int)name.size()); c.u16(0); c.u16(0); c.u16(0); c.u16(0); c.u32(0); c.u32(off);
        c.d += name;
        central += c.d;
        put(h.d);
        put(data);
        (*count)++;
    }
    Wr e;
    e.u32(0x06054b50); e.u16(0); e.u16(0); e.u16(*count); e.u16(*count); e.u32((uint32_t)central.size()); e.u32(off); e.u16(0);
    put(central);
    put(e.d);
    CloseHandle(f);
    ok = ok && *count > 0 && MoveFileExW(tmp.c_str(), zip.c_str(), MOVEFILE_REPLACE_EXISTING);
    if (!ok) DeleteFileW(tmp.c_str());
    return ok;
}
// Tous les journaux de la page (mod, chargeur, profils, secours LOCALAPPDATA, trace de lancement, Unity) avec
// MWCoop\mwcoop.ini et MWCoop\lancement.ini.
static std::vector<std::wstring> ZipFiles()
{
    LogsScan();
    std::vector<std::wstring> files;
    auto add = [&](const std::wstring &p) {
        if (!FileExists(p)) return;
        for (auto &q : files) if (!_wcsicmp(q.c_str(), p.c_str())) return;
        files.push_back(p);
    };
    for (auto &e : g_logList) add(e.path);
    if (!g_gameDir.empty()) { add(g_gameDir + L"MWCoop\\mwcoop.ini"); add(g_gameDir + L"MWCoop\\lancement.ini"); }
    return files;
}
// Bouton "Creer un zip a envoyer" : MWCoop-journaux-<date>.zip sur le Bureau, montre dans l'explorateur.
static void LogsZip()
{
    std::vector<std::wstring> files = ZipFiles();
    if (files.empty()) { SetStatus(K_WARN, T(L"Aucun journal pour l'instant : lance le jeu avec MWCoop d'abord", L"No logs yet: start the game with MWCoop first")); return; }
    wchar_t desk[MAX_PATH] = L"";
    std::wstring dir;
    if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_DESKTOPDIRECTORY, NULL, SHGFP_TYPE_CURRENT, desk)) && desk[0]) dir = WithSlash(desk);
    bool onDesk = !dir.empty();
    if (!onDesk) { dir = LocalDir(); if (!dir.empty()) CreateDirectoryW(dir.c_str(), NULL); }
    SYSTEMTIME st;
    GetLocalTime(&st);
    wchar_t name[80];
    swprintf_s(name, L"MWCoop-journaux-%04d-%02d-%02d_%02dh%02d.zip", st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute);
    std::wstring zip = dir + name;
    int n = 0;
    if (dir.empty() || !WriteZip(zip, files, &n)) {
        SetStatus(K_ERR, T(L"Zip impossible (erreur %lu)", L"Could not create the zip (error %lu)"), GetLastError());
        return;
    }
    if (onDesk) SetStatus(K_OK, T(L"Zip sur le Bureau (%d fichiers) : envoie-le", L"Zip on the Desktop (%d files): send it"), n);
    else SetStatus(K_OK, T(L"Zip cr\u00E9\u00E9 (%d fichiers) : envoie-le", L"Zip created (%d files): send it"), n);
    std::wstring arg = L"/select,\"" + zip + L"\"";
    ShellExecuteW(g_wnd, L"open", L"explorer.exe", arg.c_str(), NULL, SW_SHOWNORMAL);
}

static void OnButton(int id)
{
    switch (id) {
    case B_HOST:
        if (g_lobby == LB_HOST) HostStart();
        else if (g_lobby == LB_GUEST) GuestToggleReady();
        else if (g_lobby == LB_NONE) LobbyHost();
        break;
    case B_JOIN:
        if (g_lobby != LB_NONE) {
            bool host = g_lobby == LB_HOST;
            LobbyClose();
            SetStatus(K_NORMAL, host ? T(L"Salon ferm\u00E9 \u00B7 %s", L"Lobby closed \u00B7 %s") : T(L"Salon quitt\u00E9 \u00B7 %s", L"Left the lobby \u00B7 %s"), ModLabel().c_str());
        }
        else if (g_joinFallback) { g_joinFallback = false; g_launchWarn.clear(); Launch(MODE_GUEST); }
        else LobbyJoin();
        break;
    case B_SOLO: Launch(MODE_SOLO); break;
    case B_EXE: ChooseExe(); break;
    case B_CLOSE: LobbyClose(); g_state = ST_CLOSING; break;
    case B_MIN: ShowWindow(g_wnd, SW_MINIMIZE); break;
    case B_LOGS: g_tab = g_tab == TAB_LOGS ? -1 : TAB_LOGS; g_optHot = -1; if (g_tab == TAB_LOGS) LogsScan(); break;
    case B_THEME: g_dark = !g_dark; WritePrivateProfileStringW(L"Lanceur", L"Theme", g_dark ? L"sombre" : L"clair", g_iniLauncher.c_str()); break;
    case B_BUY: ShellExecuteW(g_wnd, L"open", kStoreUrl, NULL, NULL, SW_SHOWNORMAL); break;
    case B_COLOR: CarPickColor(); break;
    case B_LOGDIR: LogsOpenFolder(); break;
    case B_LOGZIP: LogsZip(); break;
    }
}

static void Tick()
{
    static DWORD last = GetTickCount(), lastScan;
    DWORD now = GetTickCount();
    float dt = min((now - last) / 1000.0f, 0.1f);
    last = now;
    g_time += dt;
    g_sceneT += dt;
    LobbyTick();
    LobbySoundsTick();
    if (!g_testSalon.empty()) { TestSalonStep(); return; }   // (mode d'essai : rien a dessiner)
    for (int i = 0; i < B_COUNT; i++) {
        float want = (g_hot == i && g_btn[i].enabled) ? 1.0f : 0.0f;
        g_btn[i].hover += (want - g_btn[i].hover) * min(dt * 12, 1.0f);
    }
    if (g_state == ST_CLOSING) {
        g_alpha -= dt * 4;
        if (g_alpha <= 0) { DestroyWindow(g_wnd); return; }
    } else if (g_alpha < 1) g_alpha = min(g_alpha + dt * 5, 1.0f);
    // Apercu de la voiture : rotation lente, arretee pendant un glisser et 2,5 s apres
    if (g_tab == TAB_CAR && g_state == ST_IDLE && !g_carDrag && now - g_carIdleT > 2500) g_carYaw = fmodf(g_carYaw + dt * 0.45f, 6.2831853f);
    // Tenue : un tour en 10 s ; arretee pendant un glisser, et 1,5 s apres (ou apres un changement : de face)
    if (g_tab == TAB_SKIN && g_state == ST_IDLE && !g_skinDrag && now - g_skinIdleT > 1500) g_skinYaw = fmodf(g_skinYaw + dt * 1.6f, 16.0f);

    // Ecran d'attente : jusqu'a la fenetre du jeu. Le processus lance peut se fermer tout de suite si Steam relance
    // le jeu lui-meme : on ne conclut a un echec qu'apres 15 s sans aucun mywintercar.exe.
    if (g_state == ST_LAUNCH && now - lastScan >= 250) {
        lastScan = now;
        if (g_proc && WaitForSingleObject(g_proc, 0) == WAIT_OBJECT_0) { CloseHandle(g_proc); g_proc = NULL; }
        if (!g_winSeenT && GameWindowShown()) g_winSeenT = now;
        bool running = g_proc || GameProcessRunning();
        if (running) g_noProcT = 0;
        else if (!g_noProcT) g_noProcT = now;
        if (g_noProcT && now - g_noProcT > 15000) {
            g_state = ST_IDLE;
            SetStatus(K_ERR, T(L"Le jeu s'est ferm\u00E9 au d\u00E9marrage (voir les journaux)", L"The game closed on startup (see the logs)"));
        }
    }
    if (g_state == ST_LAUNCH && ((g_winSeenT && now - g_winSeenT > 1200) || now - g_launchT > 300000)) g_state = ST_CLOSING;
    if (!IsIconic(g_wnd)) Present();   // (reduit : rien a dessiner)
}

// ---------------------------------------------------------------- fenetre
static int HitButton(float x, float y)
{
    for (int i = 0; i < B_COUNT; i++)
        if (g_btn[i].visible && g_btn[i].r.Contains(x, y)) return i;
    return -1;
}
static int HitField(float x, float y)
{
    if (g_state != ST_IDLE || g_lobby != LB_NONE || g_goWait) return -1;   // (pendant un salon : pseudo et adresse figes)
    for (int i = 0; i < 2; i++) if (g_fields[i].r.Contains(x, y)) return i;
    return -1;
}

static void TypeChar(wchar_t ch)
{
    if (g_focus < 0) return;
    Field &f = g_fields[g_focus];
    if (f.text.size() >= f.maxLen) return;
    if (f.address) { if (!(iswalnum(ch) && ch < 128) && ch != L'.' && ch != L':' && ch != L'-' && ch != L'_') return; }
    else if (ch < 32 || ch > 126) return;   // pseudo : ASCII
    f.text += ch;
}

static LRESULT CALLBACK WndProc(HWND h, UINT m, WPARAM wp, LPARAM lp)
{
    switch (m) {
    case WM_TIMER:
        if (wp == 4) {   // invite : GO recu il y a ~4 s, au tour de son jeu
            KillTimer(h, 4);
            g_goWait = false;
            TestLog("test : lancement de l'invite (%d s apres GO)", 4 + 3 * max(0, g_myId - 1));
            Launch(MODE_GUEST);
            return 0;
        }
        Tick();
        return 0;
    case WM_APP_GO:   // l'invite demarre un peu apres l'hote : deux jeux sur le meme PC ne peuvent pas demarrer ensemble
        if ((int)lp != g_lobbyGen) return 0;
    {
        bool udpOk = g_udpMine == UDP_OK;
        LobbyClose();
        g_goWait = true;
        g_launchWarn.clear();
        if (udpOk) SetStatus(K_OK, T(L"L'h\u00F4te lance la partie\u2026 ton jeu d\u00E9marre dans un instant", L"The host is starting the game\u2026 yours starts in a moment"));
        else {   // on lance quand meme : l'avertissement reste sur l'ecran d'attente
            SetStatus(K_WARN, T(L"L'h\u00F4te lance\u2026 UDP non confirm\u00E9 (port UDP %d)", L"The host is starting\u2026 UDP not confirmed (UDP port %d)"), g_lobbyPort);
            wchar_t wb[200];
            swprintf_s(wb, T(L"UDP non confirm\u00E9 : l'h\u00F4te doit rediriger le port UDP %d (pas seulement TCP) sur sa box", L"UDP not confirmed: the host must forward UDP port %d (not just TCP) on their router"), g_lobbyPort);
            g_launchWarn = wb;
            TestLog("udp : GO recu sans UDP confirme");
        }
    }
        SetTimer(h, 4, 4000 + 3000 * max(0, g_myId - 1), NULL);   // (invites sur le meme PC, en test : l'un apres l'autre)
        return 0;
    case WM_APP_LOBBYEND: {
        if ((int)lp != g_lobbyGen || g_lobby == LB_NONE) return 0;   // (salon deja quitte, ou message d'une tentative annulee)
        LobbyClose();
        std::string why;
        EnterCriticalSection(&g_lcs);
        why = g_rejectWhy;
        LeaveCriticalSection(&g_lcs);
        bool fallback = false;
        if (wp == 1) {
            if (!why.compare(0, 8, "version ")) {
                std::wstring mine = Widen(MyVersion()), his = Widen(why.substr(8));
                SetStatus(K_ERR, T(L"Version diff\u00E9rente (toi %s, h\u00F4te %s) : mettez \u00E0 jour, relancez MWCoop.exe", L"Different version (you %s, host %s): update, restart MWCoop.exe"), mine.c_str(), his.c_str());
            }
            else if (why == "full") SetStatus(K_ERR, T(L"Salon complet (%d joueurs)", L"Lobby is full (%d players)"), kLobbyMax);
            else if (why == "started") { SetStatus(K_WARN, T(L"Partie d\u00E9j\u00E0 lanc\u00E9e : \u00AB Rejoindre en jeu \u00BB", L"Session already started: \"Join in game\"")); fallback = true; }
            else if (why == "closed") SetStatus(K_WARN, T(L"L'h\u00F4te a ferm\u00E9 le salon", L"The host closed the lobby"));
            else SetStatus(K_ERR, T(L"Refus\u00E9 par l'h\u00F4te", L"Refused by the host"));
        } else if (wp == 2) {
            SetStatus(K_WARN, T(L"Pas de salon chez l'h\u00F4te : \u00AB Rejoindre en jeu \u00BB s'il joue d\u00E9j\u00E0", L"No lobby at the host: \"Join in game\" if they are already playing"));
            fallback = true;
        } else SetStatus(K_WARN, T(L"L'h\u00F4te a ferm\u00E9 le salon", L"The host closed the lobby"));
        g_joinFallback = fallback;
        if (!g_testSalon.empty()) {   // essai : l'etat dans le journal ; sans salon, on "clique" sur Rejoindre en jeu
            EnterCriticalSection(&g_cs);
            std::string st = Narrow(g_status, CP_UTF8);
            LeaveCriticalSection(&g_cs);
            TestLog("salon : fin cote invite -> \"%s\" (rejoindre en jeu propose=%d)", st.c_str(), (int)fallback);
            if (fallback) { TestLog("test : clic sur REJOINDRE EN JEU"); OnButton(B_JOIN); }
            else { TestLog("test : fin"); DestroyWindow(h); }
        }
        return 0;
    }
    case WM_MOUSEMOVE: {
        float x = (short)LOWORD(lp) / g_scale, y = (short)HIWORD(lp) / g_scale;
        if (g_carDrag) { CarDragTo(x, y); SetCursor(LoadCursor(NULL, IDC_SIZEALL)); return 0; }
        if (g_skinDrag) { SkinDragTo(x); SetCursor(LoadCursor(NULL, IDC_SIZEALL)); return 0; }
        g_hot = HitButton(x, y);
        g_tabHot = HitTab(x, y);
        HitOption(x, y, &g_optHot, &g_optPart);
        g_logRowHot = g_tab == TAB_LOGS ? LogRowAt(x, y, &g_logPart) : -1;
        g_carHot = g_tab == TAB_CAR && g_state == ST_IDLE ? CarSwatchAt(x, y) : -1;
        g_lobbyHot = g_tab == TAB_LOBBY && g_state == ST_IDLE ? LobbyChoiceAt(x, y) : -1;
        bool skinTab = g_tab == TAB_SKIN && g_state == ST_IDLE;
        g_skinHot = skinTab ? SkinCellAt(x, y) : -1;
        g_skinArrowHot = skinTab ? SkinArrowAt(x, y) : 0;
        TRACKMOUSEEVENT tme = { sizeof(tme), TME_LEAVE, h, 0 };
        TrackMouseEvent(&tme);
        bool carView = g_tab == TAB_CAR && g_state == ST_IDLE && g_car.state == 1 && kCarView.Contains(x, y);
        bool skinView = skinTab && !g_skinArrowHot && kSkinView.Contains(x, y);
        SetCursor(LoadCursor(NULL, ((g_hot >= 0 && g_btn[g_hot].enabled) || g_tabHot >= 0 || g_optHot >= 0 || g_logRowHot >= 0 || g_carHot >= 0 || g_lobbyHot >= 0 || g_skinHot >= 0 || g_skinArrowHot) ? IDC_HAND
                                   : carView || skinView ? IDC_SIZEALL : HitField(x, y) >= 0 ? IDC_IBEAM : IDC_ARROW));
        return 0;
    }
    case WM_MOUSELEAVE: g_hot = -1; g_tabHot = -1; g_optHot = -1; g_logRowHot = -1; g_carHot = -1; g_lobbyHot = -1; g_skinHot = -1; g_skinArrowHot = 0; return 0;
    case WM_KEYDOWN:   // onglet TENUE : fleches gauche / droite (hors des champs)
        if ((wp == VK_LEFT || wp == VK_RIGHT) && g_tab == TAB_SKIN && g_state == ST_IDLE && g_focus < 0) { SkinStep(wp == VK_LEFT ? -1 : 1); return 0; }
        break;
    case WM_MOUSEWHEEL:
        if (g_tab >= 0) {
            float step = -(short)HIWORD(wp) / 120.0f * kRowH * 1.5f;
            g_scroll[g_tab] = min(max(g_scroll[g_tab] + step, 0.0f), MaxScroll(g_tab));
            POINT pt = { (short)LOWORD(lp), (short)HIWORD(lp) };
            ScreenToClient(h, &pt);
            HitOption(pt.x / g_scale, pt.y / g_scale, &g_optHot, &g_optPart);
        }
        return 0;
    case WM_SETCURSOR: return TRUE;
    case WM_LBUTTONDOWN: {
        float x = (short)LOWORD(lp) / g_scale, y = (short)HIWORD(lp) / g_scale;
        int b = HitButton(x, y), f = HitField(x, y);
        if (b >= 0) { g_pressed = b; SetCapture(h); return 0; }
        if (f >= 0) { g_focus = f; g_time = 0; return 0; }
        g_focus = -1;
        int t = HitTab(x, y);
        if (t >= 0) { g_tab = g_tab == t ? -1 : t; g_optHot = -1; if (g_tab == TAB_NOTES) NotesMarkSeen(); if (g_tab == TAB_LOGS) LogsScan(); return 0; }   // un 2e clic referme
        if (g_tab == TAB_LOBBY && LobbyClick(x, y)) return 0;
        if (g_tab == TAB_LOGS && LogsMouseDown(x, y)) return 0;
        if (g_tab == TAB_CAR && CarMouseDown(x, y)) return 0;
        if (g_tab == TAB_SKIN && SkinMouseDown(x, y)) return 0;
        int row, part;
        HitOption(x, y, &row, &part);
        if (row >= 0) { OptStep(row, part < 0 ? -1 : 1); return 0; }
        if (g_tab >= 0 && kOptPanel.Contains(x, y)) return 0;
        ReleaseCapture();
        SendMessageW(h, WM_NCLBUTTONDOWN, HTCAPTION, 0);   // glisser la fenetre
        return 0;
    }
    case WM_CAPTURECHANGED:   // capture perdue en plein glisser (Alt+Tab...) : fin du glisser
        if (g_carDrag) { g_carDrag = false; g_carIdleT = GetTickCount(); }
        if (g_skinDrag) { g_skinDrag = false; g_skinIdleT = GetTickCount(); }
        return 0;
    case WM_LBUTTONUP: {
        if (g_carDrag) { g_carDrag = false; g_carIdleT = GetTickCount(); }
        if (g_skinDrag) { g_skinDrag = false; g_skinIdleT = GetTickCount(); }
        int p = g_pressed;
        g_pressed = -1;
        ReleaseCapture();
        float x = (short)LOWORD(lp) / g_scale, y = (short)HIWORD(lp) / g_scale;
        if (p >= 0 && HitButton(x, y) == p && g_btn[p].enabled) OnButton(p);
        return 0;
    }
    case WM_CHAR:
        if (g_state != ST_IDLE || g_lobby != LB_NONE || g_goWait) return 0;
        if (g_focus == 1 && wp != 9 && wp != 13 && wp != 27) g_joinFallback = false;   // autre adresse : on retente le salon
        if (wp == 8) { if (g_focus >= 0 && !g_fields[g_focus].text.empty()) g_fields[g_focus].text.pop_back(); }
        else if (wp == 127) { if (g_focus >= 0) g_fields[g_focus].text.clear(); }   // Ctrl+Retour arriere
        else if (wp == 22 && g_focus >= 0 && OpenClipboard(h)) {   // Ctrl+V
            HANDLE d = GetClipboardData(CF_UNICODETEXT);
            const wchar_t *s = d ? (const wchar_t *)GlobalLock(d) : NULL;
            if (s) { for (; *s && *s != L'\r' && *s != L'\n'; s++) TypeChar(*s); GlobalUnlock(d); }
            CloseClipboard();
        } else if (wp == 9) { g_focus = g_focus == 0 ? 1 : 0; g_time = 0; }
        else if (wp == 13) { if (g_focus == 1) { UpdateButtons(); if (g_btn[B_JOIN].enabled) OnButton(B_JOIN); } else if (g_focus == 0) { g_focus = 1; g_time = 0; } }
        else if (wp == 27) g_focus = -1;
        else if (wp >= 32) TypeChar((wchar_t)wp);
        g_time = 0.2f;
        return 0;
    case WM_APP_RELAUNCH: {
        STARTUPINFOW si = { sizeof(si) };
        PROCESS_INFORMATION pi;
        std::wstring cmd = L"\"" + g_self + L"\"";
        std::vector<wchar_t> c(cmd.begin(), cmd.end());
        c.push_back(0);
        if (CreateProcessW(g_self.c_str(), c.data(), NULL, NULL, FALSE, 0, NULL, g_dir.c_str(), &si, &pi)) {
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            DestroyWindow(h);
        }
        return 0;
    }
    case WM_CLOSE: LobbyClose(); g_state = ST_CLOSING; return 0;
    case WM_DESTROY:
        LobbyClose();
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(h, m, wp, lp);
}

static bool EncoderClsid(const wchar_t *mime, CLSID *out)
{
    UINT n = 0, size = 0;
    GetImageEncodersSize(&n, &size);
    if (!size) return false;
    std::vector<BYTE> buf(size);
    ImageCodecInfo *info = (ImageCodecInfo *)buf.data();
    GetImageEncoders(n, size, info);
    for (UINT i = 0; i < n; i++) if (!wcscmp(info[i].MimeType, mime)) { *out = info[i].Clsid; return true; }
    return false;
}

// Fond integre a l'exe (launcher.rc : 2 = clair, 3 = sombre), copie en PARGB (dessin plus rapide).
static Bitmap *LoadPngRes(int id)
{
    HRSRC r = FindResourceW(NULL, MAKEINTRESOURCEW(id), RT_RCDATA);
    HGLOBAL h = r ? LoadResource(NULL, r) : NULL;
    const BYTE *p = h ? (const BYTE *)LockResource(h) : NULL;
    return p ? LoadPngMem(p, SizeofResource(NULL, r), 1 << 14, 1 << 14) : NULL;
}

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE, LPWSTR, int)
{
    InitializeCriticalSection(&g_cs);
    InitializeCriticalSection(&g_lcs);
    WSADATA wsa;
    WSAStartup(MAKEWORD(2, 2), &wsa);
    g_fr = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_FRENCH;
    wchar_t self[MAX_PATH];
    GetModuleFileNameW(NULL, self, MAX_PATH);
    g_self = self;
    g_dir = DirOf(g_self);
    g_iniLauncher = g_dir + L"mwcoop-lanceur.ini";
    DeleteFileW((g_self + L".old").c_str());   // reste d'une mise a jour du lanceur
    // Langue : francais si Windows est en francais, anglais pour toute autre langue ; Langue=fr|en pour forcer.
    wchar_t lang[8] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Langue", L"", lang, 8, g_iniLauncher.c_str());
    if (!_wcsicmp(lang, L"fr")) g_fr = true;
    else if (!_wcsicmp(lang, L"en")) g_fr = false;
    {   // Theme : Theme=clair|sombre, sinon celui des applications de Windows
        wchar_t th[16] = L"";
        GetPrivateProfileStringW(L"Lanceur", L"Theme", L"", th, 16, g_iniLauncher.c_str());
        if (!_wcsicmp(th, L"sombre") || !_wcsicmp(th, L"dark")) g_dark = true;
        else if (!_wcsicmp(th, L"clair") || !_wcsicmp(th, L"light")) g_dark = false;
        else {
            DWORD v = 1, sz = sizeof(v);
            if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"AppsUseLightTheme", RRF_RT_REG_DWORD, NULL, &v, &sz) == ERROR_SUCCESS) g_dark = v == 0;
        }
    }

    int argc = 0;
    wchar_t **argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    for (int i = 1; i + 1 < argc; i++) {
        if (!_wcsicmp(argv[i], L"/lang")) g_fr = !_wcsicmp(argv[i + 1], L"fr");
        if (!_wcsicmp(argv[i], L"/theme")) g_dark = !_wcsicmp(argv[i + 1], L"sombre");
        if (!_wcsicmp(argv[i], L"/echelle")) g_scale = (float)_wtof(argv[i + 1]);   // captures : rendu agrandi
        if (!_wcsicmp(argv[i], L"/depot")) g_repo = argv[i + 1];
        if (!_wcsicmp(argv[i], L"/testsalon") && i + 2 < argc) { g_testSalon = argv[i + 1]; g_testSalonLog = argv[i + 2]; }
        if (!_wcsicmp(argv[i], L"/partie")) g_testPartie = argv[i + 1];
        if (!_wcsicmp(argv[i + 1], L"/sansudp")) g_testNoUdp = true;   // (dernier argument)
        if (!_wcsicmp(argv[i], L"/temps")) g_sceneT = (float)_wtof(argv[i + 1]);   // captures : instant de la scene animee
        if (!_wcsicmp(argv[i], L"/images")) g_bench = _wtoi(argv[i + 1]);
        if (!_wcsicmp(argv[i], L"/skins")) g_skinsArg = WithSlash(argv[i + 1]);   // images des tenues de test
    }
    if (!g_testSalonLog.empty()) {   // journal neuf ; role inconnu : rien
        FILE *f = _wfopen(g_testSalonLog.c_str(), L"wb");
        if (f) fclose(f);
        if (g_testSalon != L"hote" && g_testSalon != L"invite") { TestLog("test : role inconnu (hote|invite)"); return 2; }
    }

    GdiplusStartupInput gin;
    ULONG_PTR gtok;
    GdiplusStartup(&gtok, &gin, NULL);
    Layout();
    BuildOptions();
    LayoutTabs();

    SetGame(FindGame());
    // /jeu <journal> : jeu trouve (dossier, version, mod)
    if (argc >= 3 && !_wcsicmp(argv[1], L"/jeu")) {
        FILE *f = _wfopen(argv[2], L"w, ccs=UTF-8");
        if (f) { fwprintf(f, L"jeu=%s\nversion=%s\nmod=%d local=%s\n", g_gameDir.c_str(), g_gameVer.c_str(), (int)g_modOk, g_localVer.c_str()); fclose(f); }
        GdiplusShutdown(gtok);
        return g_gameDir.empty() ? 1 : 0;
    }
    g_bg = LoadPngRes(2);
    g_bgDark = LoadPngRes(3);
    if (g_gameDir.empty()) SetStatus(K_ERR, T(L"My Winter Car introuvable : choisis mywintercar.exe", L"My Winter Car not found: choose mywintercar.exe"));
    else if (LastLaunchWithoutMod()) SetStatus(K_WARN, T(L"Le dernier lancement s'est fait SANS le mod (antivirus ? version.dll ?) : voir JOURNAUX", L"The last launch ran WITHOUT the mod (antivirus? version.dll?): see LOGS"));
    else SetStatus(K_NORMAL, L"%s", ModLabel().c_str());

    // /maj <dossier du jeu> <journal> : mise a jour sans fenetre (tests) ; journal = etat final
    if (argc >= 4 && !_wcsicmp(argv[1], L"/maj")) {
        std::wstring d = WithSlash(argv[2]);
        SetGame(IsGameDir(d) ? d : L"");
        if (!g_gameDir.empty()) { g_busy = true; UpdateThread(NULL); }
        FILE *f = _wfopen(argv[3], L"w, ccs=UTF-8");
        if (f) { fwprintf(f, L"jeu=%s mod=%d local=%s releases=%d\n%s\n", g_gameDir.c_str(), (int)g_modOk, g_localVer.c_str(), (int)g_relState, g_status.c_str()); fclose(f); }
        delete g_bg; delete g_bgDark; delete g_bgCache; SkinsFree();
        GdiplusShutdown(gtok);
        return 0;
    }

    // /zip <fichier> : le zip de la page JOURNAUX (essai du format), sans explorateur ; code 0 si ecrit
    if (argc >= 3 && !_wcsicmp(argv[1], L"/zip")) {
        int n = 0;
        bool ok = WriteZip(argv[2], ZipFiles(), &n);
        delete g_bg; delete g_bgDark; delete g_bgCache; SkinsFree();
        GdiplusShutdown(gtok);
        return ok ? 0 : 1;
    }

    // Capture d'un etat, sans fenetre (verification du rendu)
    if (argc >= 4 && !_wcsicmp(argv[1], L"/capture")) {
        std::wstring st = argv[3];
        g_alpha = 1;
        g_time = 0.3f;
        if (g_localVer.empty()) g_localVer = L"0.1.0-prealpha";
        g_modOk = true;
        if (st == L"attente" || st == L"attente-udp") {
            g_state = ST_LAUNCH; g_time = 1.3f;
            if (st == L"attente-udp") g_launchWarn = T(L"UDP non confirm\u00E9 pour Kalle : redirige le port UDP 7870 (pas seulement TCP) sur ta box",
                                                       L"UDP not confirmed for Kalle: forward UDP port 7870 (not just TCP) on your router");
            wchar_t info[160];
            swprintf_s(info, T(L"%s h\u00E9berge la partie (port %d)", L"%s is hosting (port %d)"), PlayerName().c_str(), 7870);
            g_launchInfo = info;
        }
        else if (st == L"sansjeu") { g_gameDir.clear(); g_gameVer.clear(); g_localVer.clear(); g_modOk = false; SetStatus(K_ERR, T(L"My Winter Car introuvable : choisis mywintercar.exe", L"My Winter Car not found: choose mywintercar.exe")); }
        else if (st == L"coop") { g_tab = TAB_COOP; g_optHot = TabRows(TAB_COOP)[1]; g_optPart = 1; }
        else if (st == L"voiture") g_tab = TAB_CAR;   // couleur : CouleurVoiture du mwcoop.ini du jeu
        else if (st == L"tenue" || st == L"tenue-survol") {   // tenue : Apparence du mwcoop.ini ; angle : /temps (un tour en 10 s)
            g_tab = TAB_SKIN;
            g_skinYaw = fmodf(g_sceneT * 1.6f, 16.0f);
            if (st == L"tenue-survol") { g_skinHot = 29; g_skinArrowHot = 1; }
        }
        else if (st == L"notes") {   // notes d'exemple (le depot n'a pas encore de release)
            g_notes = { { L"0.1.1-prealpha", L"09/10/2026", L"\u2022 Exemple de note de version (capture).\n\u2022 Deuxi\u00E8me ligne : une correction.", L"\u2022 Sample release note (capture).\n\u2022 Second line: a fix.", L"" },
                        { L"0.1.0-prealpha", L"02/10/2026", L"\u2022 Premi\u00E8re version : chargeur, joueurs visibles.", L"\u2022 First version: loader, visible players.", L"" } };
            g_notesDone = true; g_relState = REL_OK; g_tab = TAB_NOTES;
        }
        else if (st == L"notesvide") { NotesOnlyThread(NULL); g_tab = TAB_NOTES; }
        else if (st == L"journaux") { g_tab = TAB_LOGS; LogsScan(); g_logRowHot = 0; g_btn[B_LOGS].hover = 1; }
        else if (st == L"salon" || st == L"salon-invite" || st == L"salon-udp") {   // salon a 3 joueurs (faux), vu par l'hote ou par un invite
            bool host = st == L"salon";   // (salon-udp : invite dont l'UDP est bloque)
            std::string v = MyVersion();
            g_peers = { { 0, host ? MyName() : "Pekka", host ? MySkin() : "cop_shirt", v, true, 0 },
                        { 1, host ? "Teppo" : MyName(), host ? "rally_shirt" : MySkin(), v, true, 38, UDP_OK },
                        { 2, "Kalle", "char_shirt07", v, false, 71, host ? UDP_FAIL : UDP_WAIT } };
            g_udpMine = host ? UDP_NA : UDP_OK;
            g_myId = host ? 0 : 1;
            g_lobby = host ? LB_HOST : LB_GUEST;
            g_partie = host ? PARTIE_CONTINUER : PARTIE_NOUVELLE;
            g_meReady = !host;
            g_lobbyAddr = L"192.168.1.20"; g_lobbyPort = 7870; g_myAddresses = L"192.168.1.20";
            g_tab = TAB_LOBBY;
            LayoutTabs();
            if (host) { SetStatus(K_OK, T(L"Salon ouvert \u00B7 port %d", L"Lobby open \u00B7 port %d"), 7870); g_lobbyHot = 1; }
            else SetStatus(K_OK, T(L"Dans le salon de %s", L"In %s's lobby"), L"192.168.1.20");
            if (st == L"salon-udp") { g_udpMine = UDP_FAIL; g_peers[1].udp = UDP_WAIT; }
            g_hostUdpTest = true;
        }
        else if (st == L"maj") { g_busy = true; g_progress = 0.42f; SetStatus(K_NORMAL, T(L"T\u00E9l\u00E9chargement de MWCoop %s\u2026", L"Downloading MWCoop %s\u2026"), L"0.1.1-prealpha"); g_focus = 0; g_time = 0.2f; }
        else { SetStatus(K_OK, T(L"%s \u00B7 \u00E0 jour", L"%s \u00B7 up to date"), ModLabel().c_str()); g_hot = B_HOST; g_btn[B_HOST].hover = 1; }
        int rc = 1;
        {
            Bitmap out((INT)(kImgW * g_scale), (INT)(kImgH * g_scale), PixelFormat32bppPARGB);
            RenderTo(out, g_scale);
            if (g_bench > 0) {
                LARGE_INTEGER f, a, z;
                QueryPerformanceFrequency(&f); QueryPerformanceCounter(&a);
                for (int k = 0; k < g_bench; k++) { g_sceneT += 0.016f; RenderTo(out, g_scale); }
                QueryPerformanceCounter(&z);
                FILE *bf = _wfopen((std::wstring(argv[2]) + L".txt").c_str(), L"w");
                if (bf) { fprintf(bf, "%d images, %.2f ms par image (echelle %.2f)\n", g_bench, (z.QuadPart - a.QuadPart) * 1000.0 / f.QuadPart / g_bench, g_scale); fclose(bf); }
            }
            CLSID png;
            if (EncoderClsid(L"image/png", &png) && out.Save(argv[2], &png, NULL) == Ok) rc = 0;
        }   // (detruit avant GdiplusShutdown)
        delete g_bg; delete g_bgDark; delete g_bgCache; SkinsFree();
        GdiplusShutdown(gtok);
        return rc;
    }
    LocalFree(argv);

    // /testsalon : fenetre "message only" (jamais affichee, ni souris ni clavier), dans un dossier de jeu jetable :
    // le lanceur doit etre dans le dossier du jeu (jamais celui de Steam trouve ailleurs). Pas de mise a jour.
    if (!g_testSalon.empty()) {
        int rc = 0;
        if (g_gameDir.empty() || _wcsicmp(g_gameDir.c_str(), g_dir.c_str())) {
            TestLog("test : refuse, le lanceur doit etre dans un dossier de jeu jetable (jeu trouve : %s)", Narrow(g_gameDir, CP_UTF8).c_str());
            rc = 3;
        } else {
            TestLog("test : %s, dossier %s, version %s, mod=%d", Narrow(g_testSalon).c_str(), Narrow(g_gameDir, CP_UTF8).c_str(), MyVersion().c_str(), (int)g_modOk);
            WNDCLASSEXW wc = { sizeof(wc) };
            wc.lpfnWndProc = WndProc;
            wc.hInstance = inst;
            wc.lpszClassName = L"MWCoopLauncherTest";
            RegisterClassExW(&wc);
            g_wnd = CreateWindowExW(0, wc.lpszClassName, L"MWCoop", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, inst, NULL);
            SetTimer(g_wnd, 1, 16, NULL);
            MSG msg;
            while (GetMessageW(&msg, NULL, 0, 0) > 0) DispatchMessageW(&msg);
        }
        delete g_bg; delete g_bgDark; delete g_bgCache; SkinsFree();
        GdiplusShutdown(gtok);
        WSACleanup();
        return rc;
    }

    // Taille : l'image a l'echelle de l'ecran (PPP), sans depasser 90 % de la zone de travail.
    HDC sdc = GetDC(NULL);
    g_scale = GetDeviceCaps(sdc, LOGPIXELSX) / 96.0f;
    ReleaseDC(NULL, sdc);
    RECT work;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    float fit = min((work.right - work.left) * 0.9f / kImgW, (work.bottom - work.top) * 0.9f / kImgH);
    g_scale = max(0.5f, min(g_scale, fit));
    g_winW = (int)(kImgW * g_scale);
    g_winH = (int)(kImgH * g_scale);

    WNDCLASSEXW wc = { sizeof(wc) };
    wc.lpfnWndProc = WndProc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hIcon = LoadIconW(inst, MAKEINTRESOURCEW(1));
    wc.hIconSm = (HICON)LoadImageW(inst, MAKEINTRESOURCEW(1), IMAGE_ICON, 16, 16, 0);
    wc.lpszClassName = L"MWCoopLauncher";
    RegisterClassExW(&wc);
    int x = work.left + (work.right - work.left - g_winW) / 2, y = work.top + (work.bottom - work.top - g_winH) / 2;
    g_wnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_APPWINDOW, wc.lpszClassName, L"MWCoop", WS_POPUP | WS_MINIMIZEBOX | WS_SYSMENU,
                            x, y, g_winW, g_winH, NULL, NULL, inst, NULL);

    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(bi.bmiHeader);
    bi.bmiHeader.biWidth = g_winW;
    bi.bmiHeader.biHeight = -g_winH;
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    g_memDC = CreateCompatibleDC(NULL);
    g_dib = CreateDIBSection(g_memDC, &bi, DIB_RGB_COLORS, &g_bits, NULL, 0);
    SelectObject(g_memDC, g_dib);

    Present();
    ShowWindow(g_wnd, SW_SHOW);
    SetTimer(g_wnd, 1, 16, NULL);
    if (!g_gameDir.empty()) StartUpdate();
    else {
        HANDLE nt = CreateThread(NULL, 0, NotesOnlyThread, NULL, 0, NULL);
        if (nt) CloseHandle(nt);
    }

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    if (g_proc) CloseHandle(g_proc);
    delete g_bg; delete g_bgDark; delete g_bgCache; SkinsFree();
    GdiplusShutdown(gtok);
    return 0;
}
