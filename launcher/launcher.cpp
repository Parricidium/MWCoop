// MWCoop - lanceur (MWCoop.exe, a poser dans le dossier du jeu, livre dans le paquet). Porte de celui de SACoop.
//
//  - Fenetre sans cadre ("layered", alpha par pixel) de 1280 x 800 : depuis 0.42 (lanceur 2026, ui.inc / uikit.inc),
//    une scene d'aurore boreale dessinee au demarrage, des cartes de verre depoli, une barre de navigation a gauche
//    et une page a la fois ; tout est dessine avec GDI+ (seul le logo est une image : ressource 4, logo-titre.png).
//  - Cherche My Winter Car : le dossier du lanceur, sinon celui retenu dans mwcoop-lanceur.ini, sinon les
//    bibliotheques Steam (registre + steamapps\libraryfolders.vdf), sinon l'exe choisi par le joueur. Jeu reconnu :
//    mywintercar.exe + mywintercar_Data\Managed\Assembly-CSharp.dll. Version du jeu : 1re ligne de changelog.txt.
//  - A chaque lancement : derniere version publiee sur GitHub (Parricidium/MWCoop, pre-versions comprises). Plus
//    recente que MWCoop\version.txt (ou mod absent) : telechargement du zip, extraction (Unzip, ici meme), copie
//    dans le dossier du jeu. MWCoop\mwcoop.ini garde les valeurs du joueur (seules les cles nouvelles sont ajoutees) ;
//    version.txt est copie en dernier. Le lanceur se remplace lui-meme (renomme en .old) puis se relance. Depot sans
//    release (ou pas encore public) : rien a installer, pas d'erreur.
//  - Heberger ouvre un SALON (TCP sur le port de la partie ; le jeu, lui, est en UDP) : liste des joueurs, choix de
//    la partie (continuer / nouvelle), puis LANCER : chaque lanceur demarre son jeu. Rejoindre entre dans le salon
//    de l'adresse saisie (sinon, si l'hote joue deja, propose de rejoindre directement en jeu). Jouer en solo :
//    lancement immediat. Reseau STEAM : le meme salon sur un salon Steam "amis seulement", invitations depuis le
//    lanceur (liste des amis Steam), avatars Steam ; voir steam.inc (+connect_lobby <salon> : invitation acceptee jeu
//    ferme, avec l'option de lancement Steam  "<MWCoop.exe>" %command%  que l'encart "par Steam" copie).
//  - Lancement : ecrit MWCoop\lancement.ini (lu par le chargeur et le mod, valable 3 minutes : Steam peut relancer
//    le jeu sans sa ligne de commande), lance mywintercar.exe avec les memes reglages en arguments
//    (-mwcoop-mode ...), puis reste en ecran d'attente jusqu'a la fenetre du jeu (UnityWndClass).
//  - Options du joueur : MWCoop\mwcoop.ini, section [Coop] (Pseudo, Adresse, Port, Apparence, CouleurVoiture).
//  - Page VOITURE : couleur de la CORRIS pour une nouvelle partie, apercu 3D (MWCoop\cache\corris.mesh, rendu
//    logiciel).
//  - Page TENUE : l'apparence complete en 3D (perso.inc), une ligne par partie ; sinon la galerie des hauts (images
//    pre-rendues par le mod dans MWCoop\cache\skins, portraits repris dans le salon).
//  - Page MODS (EXPERIMENTAL) : MSCLoader installe / active / coupe, liste des mods et leurs options (mods.inc).
//  - Page JOURNAUX : "!" rouge sur son entree si le dernier jeu s'est arrete brutalement.
//
// Options de ligne de commande (tests, jamais de fenetre) :
//   /capture <png> <menu|coop|voiture|tenue|tenue-survol|notes|notesvide|journaux|attente|attente-udp|maj|sansjeu|salon|
//            salon-invite|salon-udp|salon-steam|salon-steam-amis|salon-steam-invite|menu-steam|menu-ip|guide-steam|mods|mods-page|mods-absent|crash|crash-survol|serveur|partie-invite|salon-options|tuto-<n>|tuto-maj|notes-image|salon-mods|salon-mods-demande|salon-mods-telechargement|salon-mods-prets|contenu|tenue-perso [vue]> [/theme clair|sombre] [/lang fr|en] [/echelle k] [/skins <dossier>] : rendu d'un
//            etat dans un PNG (/skins : images des tenues prises dans ce dossier au lieu de MWCoop\cache\skins) ;
//   /testsalon <hote|invite> <journal> [/partie continuer|nouvelle] [/sansudp] : salon sans fenetre visible (fenetre
//            "message only"), dans un dossier de jeu jetable (celui du lanceur, obligatoirement) : l'hote ouvre le salon
//            et lance des que l'invite est pret (et son test UDP fini) ; l'invite rejoint et se met pret. Chacun ecrit
//            son lancement.ini et le recopie dans le journal, SANS lancer le jeu. Pas de mise a jour ; salon sur
//            127.0.0.1 seulement. /sansudp : l'hote ne repond pas aux sondes UDP (port UDP "pas redirige").
//            /steam <fichier> : le salon par Steam (Steam ouvert ; un seul compte suffit) ; l'hote ecrit le numero du
//            salon dans <fichier>, l'invite le lit et y entre ;
//   /maj <dossier du jeu> <journal> [/depot proprietaire/depot] : mise a jour sans fenetre, journal = etat final ;
//   /jeu <journal> : jeu trouve (dossier, version) ;
//   /zip <fichier.zip> : le zip des journaux (bouton de la page JOURNAUX), ecrit la ou on le demande, sans explorateur ;
//   /dezip <fichier.zip> <dossier> <journal> : extraction d'un paquet comme pendant une mise a jour ;
//   /miroir <dossier du jeu> <copie> <journal> : copie de lancement (installation Steam) de ce jeu dans ce dossier ;
//   /parefeu-etat <journal> <exe>... : etat du pare-feu Windows pour ces exe (lecture seule) ;
//   /parefeu <exe>... : (lance en administrateur par le lanceur, apres accord du joueur) autorise ces exe en entree.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <winioctl.h>
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
#include <netfw.h>
#include <exdisp.h>
#include <shldisp.h>
#include <psapi.h>
#include <string>
#include <vector>
#include <map>
#include <set>
#include <array>
#include <atomic>
#include <stdio.h>
#include <math.h>
#include <stdarg.h>
#include <string.h>
#include <time.h>

using namespace Gdiplus;

static std::wstring g_repo = L"Parricidium/MWCoop";   // (/depot : autre depot, pour tester la mise a jour)
static const wchar_t *kStoreUrl = L"https://store.steampowered.com/app/4164420/";
static const float kCardW = 1280, kCardH = 800, kM = 20;          // carte du lanceur (coordonnees de la mise en page)
static const float kImgW = kCardW + 2 * kM, kImgH = kCardH + 2 * kM;   // fenetre : la carte et son ombre

// ---------------------------------------------------------------- etat
enum { ST_IDLE, ST_LAUNCH, ST_CLOSING, ST_RUNNING };   // ST_RUNNING : jeu lance, lanceur reste ouvert (serveur.inc)
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
static int g_benchSkip;       // /capture ... /sauter n : essais de vitesse (1 sans la neige, 2 sans l'interface)
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
static bool g_modChecked;   // partie lancee : trace du chargeur verifiee (serveur.inc, RunTick)
static bool g_launchedFromCopy;   // partie lancee depuis la copie de lancement Steam (%LOCALAPPDATA%\MWCoop\My Winter Car)
static bool g_modMissing;   // ... et absente : proposer le lancement par Steam a la fermeture du jeu
static void AfterGameModCheck();   // (serveur.inc)
static void ModTraceCheck();
static bool g_steamLaunch;  // partie lancee par Steam (steam://rungameid) : Steam peut mettre du temps a demarrer le jeu
static std::vector<HWND> g_preWnds;                 // fenetres Unity deja la au lancement (un autre jeu sur ce PC)
static std::wstring g_launchInfo;
static int g_lastLaunchMode;                        // mode du dernier lancement (partie en cours : serveur.inc)

#define WM_APP_RELAUNCH (WM_APP + 1)
#define WM_APP_GO (WM_APP + 2)          // invite : l'hote a lance la partie
#define WM_APP_LOBBYEND (WM_APP + 3)    // invite : salon ferme (0), refuse (1) ou injoignable (2)

// Salon : etat partage entre la fenetre et les fils reseau (sous g_lcs)
enum { LB_NONE, LB_HOST, LB_CONNECTING, LB_GUEST };
enum { PARTIE_CONTINUER, PARTIE_NOUVELLE };
static const int kLobbyMax = 8;                     // joueurs dans un salon (hote compris)
// Test UDP du salon (le jeu passe en UDP sur le port du salon) : sans objet (ancien lanceur), en cours, recu, bloque.
enum { UDP_NA, UDP_WAIT, UDP_OK, UDP_FAIL };
struct LobbyPeer { int id; std::string name, skin, ver; bool ready; int ping; int udp; DWORD since; uint64_t sid; };   // sid : compte Steam (salon Steam)
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
static uint64_t g_sHost;                            // salon Steam : compte de l'hote (HoteSteam de lancement.ini ; steam.inc)
static std::wstring g_sHostName;                    // ... et son nom Steam
static void SteamDown();
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

// Journal du lanceur (%LOCALAPPDATA%\MWCoop\lanceur.log, refait a chaque demarrage de la fenetre) : comment il a ete
// demarre et comment il lance le jeu -- un lancement par Steam qui ne charge pas le mod s'y relit (zip des journaux).
static std::wstring g_launcherLog;
static bool g_fromSteam;   // demarre par Steam (option de lancement "<MWCoop.exe>" %command%)
static void LaunchLog(const char *fmt, ...)
{
    char b[1024];
    va_list ap;
    va_start(ap, fmt);
    _vsnprintf_s(b, _countof(b), _TRUNCATE, fmt, ap);
    va_end(ap);
    TestLog("%s", b);
    if (g_launcherLog.empty()) return;
    FILE *f = _wfopen(g_launcherLog.c_str(), L"ab");
    if (f) { SYSTEMTIME t; GetLocalTime(&t); fprintf(f, "%02d:%02d:%02d %s\r\n", t.wHour, t.wMinute, t.wSecond, b); fclose(f); }
}
// Mode debogage (page JOURNAUX, [Lanceur] Debug ; demande d'un joueur, 08/10) : journal detaille du lanceur -- requetes et
// reponses, Steam, fichiers et reglages au depart du jeu, toutes les DLL du jeu a 5, 15 et 30 s, antivirus declares.
static bool g_debug;
static void DebugLog(const char *fmt, ...)
{
    if (!g_debug) return;
    char b[1024];
    va_list ap;
    va_start(ap, fmt);
    _vsnprintf_s(b, _countof(b), _TRUNCATE, fmt, ap);
    va_end(ap);
    LaunchLog("[debug] %s", b);
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

// Version d'un exe (ressource FILEVERSION : MWV_NUM), 0 si illisible. VERSION.dll de Windows charge par son chemin
// (System32) : lie au lanceur (0.59.4), Windows prenait celle du dossier du lanceur -- le chargeur du mod, quand le
// lanceur est dans le dossier du jeu : verrouillee par le lanceur lui-meme, la mise a jour ne pouvait plus la remplacer
// ("version.dll en cours d'utilisation"), et le chargeur demarrait dans le lanceur.
static unsigned long long ExeVersion(const std::wstring &path)
{
    typedef DWORD (WINAPI *SizeFn)(LPCWSTR, LPDWORD);
    typedef BOOL (WINAPI *InfoFn)(LPCWSTR, DWORD, DWORD, LPVOID);
    typedef BOOL (WINAPI *QueryFn)(LPCVOID, LPCWSTR, LPVOID *, PUINT);
    static HMODULE ver = NULL;
    if (!ver) {
        wchar_t sys[MAX_PATH] = L"";
        GetSystemDirectoryW(sys, MAX_PATH);
        ver = LoadLibraryExW((std::wstring(sys) + L"\\version.dll").c_str(), NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (!ver) return 0;
    }
    SizeFn size = (SizeFn)GetProcAddress(ver, "GetFileVersionInfoSizeW");
    InfoFn info = (InfoFn)GetProcAddress(ver, "GetFileVersionInfoW");
    QueryFn query = (QueryFn)GetProcAddress(ver, "VerQueryValueW");
    if (!size || !info || !query) return 0;
    DWORD h = 0, n = size(path.c_str(), &h);
    if (!n) return 0;
    std::vector<char> buf(n);
    if (!info(path.c_str(), 0, n, buf.data())) return 0;
    VS_FIXEDFILEINFO *fi = NULL;
    UINT len = 0;
    if (!query(buf.data(), L"\\", (void **)&fi, &len) || !fi) return 0;
    return ((unsigned long long)fi->dwFileVersionMS << 32) | fi->dwFileVersionLS;
}

static void TenuesAddShirts();   // (tenues offertes : plus bas)
static void SetGame(const std::wstring &dir)
{
    g_gameDir = dir;
    g_gameVer = ReadGameVersion();
    LoadLocalVersion();
    if (!g_gameDir.empty()) LoadPlayer();
    TenuesAddShirts();
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
// Derniere erreur WinHTTP d'une requete sans reponse (12007 nom introuvable, 12029 connexion impossible, 12002 delai...).
static DWORD g_httpErr;

static bool HttpGet(const std::wstring &url, std::string *out, const std::wstring &toFile, bool progress, DWORD *status = NULL)
{
    if (status) *status = 0;
    g_httpErr = 0;
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
    if (!ok && !(status && *status)) g_httpErr = GetLastError();
    if (f != INVALID_HANDLE_VALUE) CloseHandle(f);
    if (r) WinHttpCloseHandle(r);
    if (c) WinHttpCloseHandle(c);
    WinHttpCloseHandle(s);
    DebugLog("http %s : %s, statut %lu, erreur %lu", Narrow(url, CP_UTF8).c_str(), ok ? "ok" : "ECHEC", status ? *status : 0, g_httpErr);
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
// Pas de processus caches (tar.exe, cmd.exe) : les antivirus a apprentissage automatique (Defender "Wacatac.B!ml",
// CrowdStrike...) classaient le lanceur en "dropper" a cause d'eux. Zip et jonctions sont donc faits ici.

// Inflate (RFC 1951) : blocs stockes, Huffman fixe et dynamique ; meme decoupage que puff.c de zlib.
struct Inflate {
    const unsigned char *in; size_t len, pos = 0;
    unsigned buf = 0; int cnt = 0; bool bad = false;
    std::vector<unsigned char> &out;
    struct Huff { short count[16]; short symbol[288]; };
    Inflate(const unsigned char *d, size_t n, std::vector<unsigned char> &o) : in(d), len(n), out(o) {}
    int Bits(int need)
    {
        unsigned v = buf;
        while (cnt < need) {
            if (pos >= len) { bad = true; return 0; }
            v |= (unsigned)in[pos++] << cnt;
            cnt += 8;
        }
        buf = v >> need;
        cnt -= need;
        return (int)(v & ((1u << need) - 1));
    }
    static int Build(Huff &h, const short *lengths, int n)
    {
        for (int l = 0; l < 16; l++) h.count[l] = 0;
        for (int i = 0; i < n; i++) h.count[lengths[i]]++;
        if (h.count[0] == n) return 0;
        int left = 1;
        for (int l = 1; l < 16; l++) { left <<= 1; left -= h.count[l]; if (left < 0) return left; }
        short offs[16];
        offs[1] = 0;
        for (int l = 1; l < 15; l++) offs[l + 1] = offs[l] + h.count[l];
        for (int i = 0; i < n; i++) if (lengths[i]) h.symbol[offs[lengths[i]]++] = (short)i;
        return left;
    }
    int Decode(const Huff &h)
    {
        int code = 0, first = 0, index = 0;
        for (int l = 1; l < 16; l++) {
            code |= Bits(1);
            int count = h.count[l];
            if (code - count < first) return h.symbol[index + (code - first)];
            index += count; first += count;
            first <<= 1; code <<= 1;
        }
        return -1;
    }
    bool Codes(const Huff &lc, const Huff &dc)
    {
        static const short lbase[29] = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
        static const short lext[29] = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
        static const short dbase[30] = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
        static const short dext[30] = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
        for (;;) {
            int sym = Decode(lc);
            if (bad || sym < 0) return false;
            if (sym < 256) { out.push_back((unsigned char)sym); continue; }
            if (sym == 256) return true;
            sym -= 257;
            if (sym >= 29) return false;
            int n = lbase[sym] + Bits(lext[sym]);
            int d = Decode(dc);
            if (bad || d < 0 || d >= 30) return false;
            size_t dist = dbase[d] + Bits(dext[d]);
            if (bad || dist > out.size()) return false;
            size_t from = out.size() - dist;
            for (int i = 0; i < n; i++) out.push_back(out[from + i]);
        }
    }
    bool Stored()
    {
        buf = 0; cnt = 0;   // aligne sur l'octet
        if (pos + 4 > len) return false;
        unsigned n = in[pos] | in[pos + 1] << 8, nn = in[pos + 2] | in[pos + 3] << 8;
        pos += 4;
        if (n != (~nn & 0xFFFF) || pos + n > len) return false;
        out.insert(out.end(), in + pos, in + pos + n);
        pos += n;
        return true;
    }
    bool Fixed()
    {
        static Huff lc, dc;
        static bool built = false;
        if (!built) {
            short l[288];
            int i = 0;
            for (; i < 144; i++) l[i] = 8;
            for (; i < 256; i++) l[i] = 9;
            for (; i < 280; i++) l[i] = 7;
            for (; i < 288; i++) l[i] = 8;
            Build(lc, l, 288);
            for (i = 0; i < 30; i++) l[i] = 5;
            Build(dc, l, 30);
            built = true;
        }
        return Codes(lc, dc);
    }
    bool Dynamic()
    {
        static const short order[19] = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };
        short l[320];
        int nlen = Bits(5) + 257, ndist = Bits(5) + 1, ncode = Bits(4) + 4;
        if (bad || nlen > 286 || ndist > 30) return false;
        int i = 0;
        for (; i < ncode; i++) l[order[i]] = (short)Bits(3);
        for (; i < 19; i++) l[order[i]] = 0;
        Huff lc, dc;
        if (bad || Build(lc, l, 19) != 0) return false;
        for (i = 0; i < nlen + ndist;) {
            int sym = Decode(lc);
            if (bad || sym < 0) return false;
            if (sym < 16) { l[i++] = (short)sym; continue; }
            short v = 0;
            int rep;
            if (sym == 16) { if (!i) return false; v = l[i - 1]; rep = 3 + Bits(2); }
            else if (sym == 17) rep = 3 + Bits(3);
            else rep = 11 + Bits(7);
            if (bad || i + rep > nlen + ndist) return false;
            while (rep--) l[i++] = v;
        }
        if (!l[256]) return false;
        int e = Build(lc, l, nlen);
        if (e < 0 || (e > 0 && nlen - lc.count[0] != 1)) return false;
        e = Build(dc, l + nlen, ndist);
        if (e < 0 || (e > 0 && ndist - dc.count[0] != 1)) return false;
        return Codes(lc, dc);
    }
    bool Run()
    {
        for (int last = 0; !last;) {
            last = Bits(1);
            int type = Bits(2);
            bool ok = bad ? false : type == 0 ? Stored() : type == 1 ? Fixed() : type == 2 ? Dynamic() : false;
            if (!ok || bad) return false;
        }
        return true;
    }
};

static unsigned Crc32(const unsigned char *d, size_t n)
{
    static unsigned table[256];
    if (!table[1]) for (unsigned i = 0; i < 256; i++) {
        unsigned c = i;
        for (int k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        table[i] = c;
    }
    unsigned c = 0xFFFFFFFFu;
    for (size_t i = 0; i < n; i++) c = table[(c ^ d[i]) & 0xFF] ^ (c >> 8);
    return ~c;
}

// Extrait le zip dans dir (repertoire central, methodes 0 et 8, CRC verifie ; chemins en ".." ou absolus refuses).
// Faux au premier probleme ; *why dit lequel.
static bool Unzip(const std::wstring &zip, const std::wstring &dir, std::string *why)
{
    std::vector<unsigned char> z;
    if (!ReadAll(zip, z) || z.size() < 22) { *why = "lecture"; return false; }
    auto u16 = [&](size_t o) { return (unsigned)(z[o] | z[o + 1] << 8); };
    auto u32 = [&](size_t o) { return (unsigned)(z[o] | z[o + 1] << 8 | z[o + 2] << 16 | (unsigned)z[o + 3] << 24); };
    size_t eocd = std::string::npos;
    for (size_t o = z.size() - 22; ; o--) {
        if (u32(o) == 0x06054b50) { eocd = o; break; }
        if (o == 0 || z.size() - o > 22 + 65535) break;
    }
    if (eocd == std::string::npos) { *why = "fin de zip introuvable"; return false; }
    unsigned count = u16(eocd + 10);
    size_t cd = u32(eocd + 16);
    for (unsigned k = 0; k < count; k++) {
        if (cd + 46 > z.size() || u32(cd) != 0x02014b50) { *why = "repertoire central"; return false; }
        unsigned method = u16(cd + 10), crc = u32(cd + 16), csize = u32(cd + 20), usize = u32(cd + 24);
        unsigned nlen = u16(cd + 28), xlen = u16(cd + 30), clen = u16(cd + 32);
        size_t lh = u32(cd + 42);
        if (cd + 46 + nlen > z.size()) { *why = "nom"; return false; }
        std::string name((const char *)&z[cd + 46], nlen);
        cd += 46 + nlen + xlen + clen;
        for (auto &c : name) if (c == '\\') c = '/';
        if (name.empty() || name[0] == '/' || name.find(':') != std::string::npos || name.find("..") != std::string::npos) { *why = "chemin refuse : " + name; return false; }
        std::wstring path = dir + L"\\" + Widen(name);
        for (auto &c : path) if (c == L'/') c = L'\\';
        if (name.back() == '/') { SHCreateDirectoryExW(NULL, path.substr(0, path.size() - 1).c_str(), NULL); continue; }
        if (lh + 30 > z.size() || u32(lh) != 0x04034b50) { *why = "entete local : " + name; return false; }
        size_t data = lh + 30 + u16(lh + 26) + u16(lh + 28);
        if (data + csize > z.size()) { *why = "donnees tronquees : " + name; return false; }
        std::vector<unsigned char> out;
        if (method == 0) out.assign(z.begin() + data, z.begin() + data + csize);
        else if (method == 8) {
            out.reserve(usize);
            Inflate inf(&z[data], csize, out);
            if (!inf.Run()) { *why = "decompression : " + name; return false; }
        } else { *why = "methode " + std::to_string(method) + " : " + name; return false; }
        if (out.size() != usize || Crc32(out.data(), out.size()) != crc) { *why = "CRC : " + name; return false; }
        size_t slash = path.find_last_of(L'\\');
        SHCreateDirectoryExW(NULL, path.substr(0, slash).c_str(), NULL);
        HANDLE f = CreateFileW(path.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (f == INVALID_HANDLE_VALUE) { *why = "ecriture : " + name; return false; }
        DWORD w = 0;
        BOOL ok = out.empty() || WriteFile(f, out.data(), (DWORD)out.size(), &w, NULL);
        CloseHandle(f);
        if (!ok || w != out.size()) { *why = "ecriture : " + name; return false; }
    }
    return true;
}

// Jonction de repertoire (comme mklink /J, sans cmd.exe) : point de montage "\??\cible" pose sur un dossier vide.
static bool MakeJunction(const std::wstring &link, const std::wstring &target)
{
    if (!CreateDirectoryW(link.c_str(), NULL)) return false;
    HANDLE h = CreateFileW(link.c_str(), GENERIC_WRITE, 0, NULL, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, NULL);
    if (h == INVALID_HANDLE_VALUE) { RemoveDirectoryW(link.c_str()); return false; }
    std::wstring sub = L"\\??\\" + target;
    size_t names = (sub.size() + 1 + target.size() + 1) * sizeof(wchar_t);
    std::vector<unsigned char> b(16 + names);
    auto put16 = [&](size_t o, size_t v) { b[o] = (unsigned char)v; b[o + 1] = (unsigned char)(v >> 8); };
    DWORD tag = IO_REPARSE_TAG_MOUNT_POINT;
    memcpy(&b[0], &tag, 4);
    put16(4, 8 + names);                                  // ReparseDataLength
    put16(8, 0);                                          // SubstituteNameOffset
    put16(10, sub.size() * sizeof(wchar_t));              // SubstituteNameLength
    put16(12, (sub.size() + 1) * sizeof(wchar_t));        // PrintNameOffset
    put16(14, target.size() * sizeof(wchar_t));           // PrintNameLength
    memcpy(&b[16], sub.c_str(), (sub.size() + 1) * sizeof(wchar_t));
    memcpy(&b[16 + (sub.size() + 1) * sizeof(wchar_t)], target.c_str(), (target.size() + 1) * sizeof(wchar_t));
    DWORD n = 0;
    BOOL ok = DeviceIoControl(h, FSCTL_SET_REPARSE_POINT, b.data(), (DWORD)b.size(), NULL, 0, &n, NULL);
    CloseHandle(h);
    if (!ok) RemoveDirectoryW(link.c_str());
    return ok != FALSE;
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
        if (!CopyFileW(s.c_str(), d.c_str(), FALSE)) {
            // Fichier verrouille (DLL chargee : jeu ouvert, ou version.dll prise par un lanceur 0.59.4-0.59.7) : Windows
            // permet de le renommer ; l'ancien est mis de cote (.old, efface au prochain demarrage), le nouveau copie.
            DWORD e = GetLastError();
            std::wstring old = d + L".old";
            DeleteFileW(old.c_str());
            if ((e == ERROR_SHARING_VIOLATION || e == ERROR_ACCESS_DENIED || e == ERROR_LOCK_VIOLATION) && MoveFileExW(d.c_str(), old.c_str(), MOVEFILE_REPLACE_EXISTING)
                && CopyFileW(s.c_str(), d.c_str(), FALSE)) TestLog("mise a jour : %ls verrouille, l'ancien mis de cote (.old)", n.c_str());
            else { g_copyError = e; ok = false; *failed = n; }
        }
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

// Ligne image d'une note : ![texte](url) (markdown) ou <img ... src="url"> (image glissee dans l'editeur de GitHub).
static std::wstring NoteImageUrl(const std::wstring &line)
{
    size_t b = line.find_first_not_of(L" \t");
    if (b == std::wstring::npos) return L"";
    std::wstring u;
    if (!line.compare(b, 2, L"![")) {
        size_t c = line.find(L"](", b), e = c == std::wstring::npos ? c : line.find(L')', c);
        if (e != std::wstring::npos) u = line.substr(c + 2, e - c - 2);
        size_t sp = u.find(L' ');   // ![x](url "titre")
        if (sp != std::wstring::npos) u.erase(sp);
    } else if (!_wcsnicmp(line.c_str() + b, L"<img", 4)) {
        size_t q = line.find(L"src=");
        if (q != std::wstring::npos && q + 5 < line.size()) {
            wchar_t d = line[q + 4];
            size_t e = line.find(d, q + 5);
            if ((d == L'"' || d == L'\'') && e != std::wstring::npos) u = line.substr(q + 5, e - q - 5);
        }
    }
    return !_wcsnicmp(u.c_str(), L"https://", 8) ? u : L"";
}

// Markdown simple : titres, gras et code retires ; puces "- " -> "\u2022" ; images -> ligne "\x01<url>" (dessinees).
static std::wstring CleanNote(const std::wstring &s)
{
    std::wstring o;
    for (size_t i = 0; i < s.size(); i++) {
        bool atLine = i == 0 || s[i - 1] == L'\n';
        if (atLine) {
            size_t e = s.find(L'\n', i);
            std::wstring url = NoteImageUrl(s.substr(i, e == std::wstring::npos ? std::wstring::npos : e - i));
            if (!url.empty()) {
                o += L'\x01'; o += url; o += L'\n';
                if (e == std::wstring::npos) break;
                i = e;
                continue;
            }
        }
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
// REL_OFFLINE : pas de reponse (json : le cache, pour les notes seulement). g_relCode / g_relErr : code HTTP (403 ou
// 429 : GitHub limite les demandes par adresse, ce n'est pas une coupure) et erreur WinHTTP, notes au journal du
// lanceur (retour d'un joueur, 07/10 : « hors ligne » alors que Steam etait connecte).
static DWORD g_relCode, g_relErr;
static int FetchReleases(std::string &json)
{
    std::wstring cache = g_gameDir.empty() ? L"" : g_gameDir + L"MWCoop\\notes-maj.json";
    DWORD code = 0;
    bool got = HttpGet(L"https://api.github.com/repos/" + g_repo + L"/releases?per_page=40", &json, L"", false, &code);
    g_relCode = code; g_relErr = got ? 0 : g_httpErr;
    LaunchLog("versions sur GitHub : %s (code HTTP %lu, erreur WinHTTP %lu)", got ? "recues" : "pas de reponse", code, g_relErr);
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
// Demande de JD (07/10) : plus de mise a jour toute seule. Au demarrage (et toutes les 30 min, sans bruit), on regarde
// seulement : une version plus recente -> bouton "NOUVELLE VERSION x \u00B7 METTRE A JOUR" a la place de la ligne d'etat
// (B_UPDATE), l'installation au clic. Sauf mod absent (premiere installation) ou [Lanceur] MajAuto=1 : installee d'office.
enum { UPD_CHECK, UPD_INSTALL, UPD_QUIET };   // parametre du fil : regarder, installer, regarder sans rien afficher
static std::wstring g_updVer;                 // version plus recente proposee (vide : rien), sous g_cs
static volatile bool g_updChecking;
static DWORD g_updCheckT;
static std::wstring UpdAvail() { EnterCriticalSection(&g_cs); std::wstring v = g_updVer; LeaveCriticalSection(&g_cs); return v; }
static void UpdSet(const std::wstring &v) { EnterCriticalSection(&g_cs); g_updVer = v; LeaveCriticalSection(&g_cs); }

static DWORD WINAPI UpdateThread(void *param)
{
    int mode = (int)(INT_PTR)param;
    bool quiet = mode == UPD_QUIET;
    struct Done { bool q; ~Done() { if (q) g_updChecking = false; } } done = { quiet };
    if (!quiet) {
        SetStatus(K_NORMAL, T(L"Recherche de mises \u00E0 jour\u2026", L"Checking for updates\u2026"));
        g_progress = -2;
    }
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
    if (quiet) {   // (verification periodique : seulement le bouton, s'il y a du nouveau)
        std::wstring remote;
        for (const Note &n : rel) if (!n.zip.empty()) { remote = n.ver; break; }
        if (rs != REL_OFFLINE && !remote.empty() && !local.empty() && CmpVer(remote, local) > 0 && UpdAvail() != remote) {
            UpdSet(remote);
            SetStatus(K_WARN, T(L"%s \u00B7 nouvelle version %s disponible", L"%s \u00B7 new version %s available"), label.c_str(), remote.c_str());
        }
        return 0;
    }
    if (rs == REL_OFFLINE) {
        g_progress = -1;
        if (!mod) SetStatus(K_ERR, T(L"Hors ligne : MWCoop n'est pas install\u00E9 ici", L"Offline: MWCoop is not installed here"));
        else if (g_relCode == 403 || g_relCode == 429) SetStatus(K_WARN, T(L"GitHub limite les demandes : r\u00E9essaie dans quelques minutes \u00B7 %s", L"GitHub rate limit: try again in a few minutes \u00B7 %s"), label.c_str());
        else if (g_relCode) SetStatus(K_WARN, T(L"GitHub r\u00E9pond mal (code %lu) \u00B7 %s", L"GitHub error (code %lu) \u00B7 %s"), g_relCode, label.c_str());
        else SetStatus(K_WARN, T(L"Hors ligne (GitHub injoignable, erreur %lu) \u00B7 %s", L"Offline (GitHub unreachable, error %lu) \u00B7 %s"), g_relErr, label.c_str());
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
        UpdSet(L"");
        SetStatus(K_OK, T(L"%s \u00B7 \u00E0 jour", L"%s \u00B7 up to date"), label.c_str());
        g_busy = false;
        return 0;
    }
    if (mode == UPD_CHECK && !local.empty()) {   // deja installe : on propose, le joueur choisit quand
        g_progress = -1;
        UpdSet(remote);
        SetStatus(K_WARN, T(L"%s \u00B7 nouvelle version %s disponible", L"%s \u00B7 new version %s available"), label.c_str(), remote.c_str());
        g_busy = false;
        return 0;
    }
    UpdSet(L"");

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
    std::string why;
    if (!Unzip(zip, ext, &why)) {
        TestLog("mise a jour : zip illisible (%s)", why.c_str());
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

// Sans jeu : les notes quand meme.
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

// install : bouton METTRE A JOUR ; sinon regarder seulement (installer d'office si [Lanceur] MajAuto=1).
static void StartUpdate(bool install = false)
{
    if (g_gameDir.empty() || g_busy) return;
    if (!install && GetPrivateProfileIntW(L"Lanceur", L"MajAuto", 0, g_iniLauncher.c_str()) != 0) install = true;
    g_busy = true;
    g_updCheckT = GetTickCount();
    if (install) UpdSet(L"");
    HANDLE t = CreateThread(NULL, 0, UpdateThread, (void *)(INT_PTR)(install ? UPD_INSTALL : UPD_CHECK), 0, NULL);
    if (t) CloseHandle(t); else g_busy = false;
}

// Toutes les 30 min, lanceur au menu : regarder sans bruit (le bouton apparait s'il sort une version entre-temps).
static void UpdateTick()
{
    if (g_gameDir.empty() || g_busy || g_updChecking || GetTickCount() - g_updCheckT < 30u * 60u * 1000u) return;
    g_updCheckT = GetTickCount();
    g_updChecking = true;
    HANDLE t = CreateThread(NULL, 0, UpdateThread, (void *)(INT_PTR)UPD_QUIET, 0, NULL);
    if (t) CloseHandle(t); else g_updChecking = false;
}

// ---------------------------------------------------------------- boutons
// Pages (lanceur 2026, barre de navigation a gauche : ui.inc). TAB_COOP : les reglages ; TAB_SKIN : la tenue.
enum { TAB_HOME, TAB_LOBBY, TAB_SKIN, TAB_CAR, TAB_CONTENT, TAB_MODS, TAB_NOTES, TAB_LOGS, TAB_COOP, TAB_API, TAB_GFX, TAB_CREDITS, TAB_WIKI, TAB_COUNT };
static int g_tab = TAB_HOME;

enum { B_HOST, B_JOIN, B_SOLO, B_EXE, B_BUY, B_THEME, B_CLOSE, B_MIN, B_LOGS, B_COLOR, B_LOGDIR, B_LOGZIP, B_GITHUB, B_KOFI, B_NETIP, B_NETSTEAM, B_UPDATE, B_LANG, B_DISCORD, B_COUNT };
// Reseau de la partie : IP (adresse:port, salon TCP du lanceur, UDP en jeu) ou Steam (salon et invitations Steam en
// jeu, pair-a-pair par les relais de Valve : ni port ni pare-feu). Garde dans [Lanceur] Reseau, passe au mod par
// lancement.ini (Reseau=).
static bool g_steamNet = false;
static bool g_mscOn = true;                          // [Lanceur] MSCLoader : le charger au lancement (onglet MODS, mods.inc)
static int g_mscOwnPref = -1;                        // [Lanceur] MSCLoaderMWCoop : 1 celui de MWCoop, 0 l'officiel, -1 pas choisi (mods.inc)
static bool MscOwn();
static void Uninstall();   // (Reglages > A propos)
static bool MscOwnInstalled();
static std::wstring MscCopyDir();
static bool MscOwnApply(const std::wstring &m, bool enabled, bool guest);
static bool g_guide;                                 // guide "Jouer par Steam" ouvert (par-dessus tout le lanceur)
static int g_guideHot;                               // 1 copier, 2 compris, 3 ne plus afficher, 4 fermer
static bool g_guideNoMore;
static DWORD g_guideCopiedT;
struct Button { RectF r; float hover; bool visible, enabled; };
static Button g_btn[B_COUNT];
static int g_hot = -1, g_pressed = -1;

static void Layout()
{
    g_fields[0].maxLen = 23; g_fields[0].address = false;
    g_fields[1].maxLen = 63; g_fields[1].address = true;
    // (les autres boutons sont places par leur page en se dessinant : ui.inc)
    g_btn[B_DISCORD].r = RectF(28, 726, 104, 44);   // Discord (nom et logo), GitHub et Ko-fi (logos)
    g_btn[B_GITHUB].r = RectF(136, 726, 48, 44);
    g_btn[B_KOFI].r = RectF(188, 726, 48, 44);
}

static void UpdateButtons()
{
    bool menu = g_state == ST_IDLE, game = !g_gameDir.empty(), busy = g_busy;
    int lobby = g_lobby;
    for (int i = 0; i < B_COUNT; i++) g_btn[i].visible = true;
    bool can = menu && game && !busy && g_modOk && !g_goWait;
    g_btn[B_HOST].enabled = can && lobby != LB_CONNECTING;   // HEBERGER, LANCER (hote) ou PRET (invite)
    g_btn[B_JOIN].enabled = can;                             // REJOINDRE, FERMER LE SALON, QUITTER ou ANNULER
    g_btn[B_SOLO].enabled = can;
    g_btn[B_EXE].enabled = menu && !busy;
    g_btn[B_CLOSE].enabled = g_btn[B_MIN].enabled = g_btn[B_BUY].enabled = g_btn[B_THEME].enabled = g_btn[B_LANG].enabled = true;
    g_btn[B_GITHUB].enabled = g_btn[B_KOFI].enabled = g_btn[B_DISCORD].enabled = true;
    g_btn[B_BUY].visible = false;
    g_btn[B_LOGS].visible = false;
    bool home = g_tab == TAB_HOME;
    g_btn[B_HOST].visible = g_btn[B_JOIN].visible = menu && game && ((home && lobby == LB_NONE) || g_tab == TAB_LOBBY);
    g_btn[B_SOLO].visible = menu && game && home && lobby == LB_NONE && !g_goWait;
    g_btn[B_EXE].visible = menu;
    g_btn[B_COLOR].visible = menu && game && g_tab == TAB_CAR;
    g_btn[B_COLOR].enabled = true;
    g_btn[B_LOGDIR].visible = g_btn[B_LOGZIP].visible = menu && game && g_tab == TAB_LOGS;
    g_btn[B_LOGDIR].enabled = g_btn[B_LOGZIP].enabled = true;
    g_btn[B_NETIP].visible = g_btn[B_NETSTEAM].visible = menu && game && home && lobby == LB_NONE && !g_goWait;
    g_btn[B_NETIP].enabled = g_btn[B_NETSTEAM].enabled = !busy;
    g_btn[B_UPDATE].visible = menu && game && !busy && !g_goWait && !UpdAvail().empty();
    g_btn[B_UPDATE].enabled = lobby == LB_NONE;   // (pendant un salon : on finit d'abord la partie)
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

// Lanceur 2026 (demande de JD, 08/10) : verre depoli sur une nuit d'aurore boreale (sombre) ou un matin d'hiver
// (clair). Les cartes sont du "verre" : la scene floutee dessous, une teinte, un liseré clair (ui.inc). Les couleurs
// ci-dessous sont donc translucides (posees sur le verre). Textes blancs / bleu nuit ; accent bleu glacier (sombre) ou
// bleu profond (clair) ; etats : vert (pret, OK), ambre (attention), rouge (erreur, PRE-ALPHA). Bouton lune / soleil ;
// Theme=clair|sombre dans mwcoop-lanceur.ini, sinon celui de Windows.
struct Theme {
    Color ink, grey, panel, panelBorder, sep, card, cardSel, choiceBorder, toggleOff, field, fieldBorder, placeholder,
          tab, tabHot, pill, btn2, btn2Hot, circle, circleHot, fallA, fallB, accent, accent2, onAccent, pillHot, ok, warn, red;
};
static const Theme kLight = {
    Color(255, 16, 30, 48), Color(255, 74, 90, 112), Color(150, 255, 255, 255), Color(190, 255, 255, 255), Color(34, 16, 30, 48),
    Color(105, 255, 255, 255), Color(200, 255, 255, 255), Color(42, 16, 30, 48), Color(60, 16, 30, 48),
    Color(150, 255, 255, 255), Color(64, 16, 30, 48), Color(255, 120, 136, 158),
    Color(120, 255, 255, 255), Color(205, 255, 255, 255), Color(235, 20, 40, 64), Color(130, 255, 255, 255), Color(215, 255, 255, 255),
    Color(140, 255, 255, 255), Color(225, 255, 255, 255), Color(255, 246, 250, 254), Color(255, 205, 220, 236),
    Color(255, 26, 98, 172), Color(255, 58, 132, 206), Color(255, 255, 255, 255), Color(255, 42, 84, 128),
    Color(255, 18, 132, 92), Color(255, 172, 100, 12), Color(255, 206, 40, 60) };
static const Theme kDark = {
    Color(255, 238, 244, 255), Color(255, 164, 178, 202), Color(128, 14, 24, 52), Color(38, 255, 255, 255), Color(28, 255, 255, 255),
    Color(14, 255, 255, 255), Color(34, 255, 255, 255), Color(30, 255, 255, 255), Color(48, 255, 255, 255),
    Color(16, 255, 255, 255), Color(44, 255, 255, 255), Color(255, 132, 146, 170),
    Color(20, 255, 255, 255), Color(36, 255, 255, 255), Color(200, 30, 44, 74), Color(16, 255, 255, 255), Color(32, 255, 255, 255),
    Color(22, 255, 255, 255), Color(40, 255, 255, 255), Color(255, 10, 18, 34), Color(255, 24, 40, 64),
    Color(255, 166, 225, 255), Color(255, 200, 236, 255), Color(255, 6, 19, 37), Color(230, 48, 66, 100),
    Color(255, 124, 242, 196), Color(255, 255, 196, 107), Color(255, 255, 110, 110) };
static bool g_dark;
#define TH(x) ((g_dark ? kDark : kLight).x)
#define kInk TH(ink)
#define kGrey TH(grey)
#define kAcc TH(accent)
#define kAcc2 TH(accent2)
#define kOnAcc TH(onAccent)
#define kRed TH(red)
#define kOk TH(ok)
#define kWarn TH(warn)

static Color Mix(Color a, Color b, float t)
{
    auto L = [&](BYTE x, BYTE y) { return (BYTE)(x + (y - x) * t); };
    return Color(L(a.GetA(), b.GetA()), L(a.GetR(), b.GetR()), L(a.GetG(), b.GetG()), L(a.GetB(), b.GetB()));
}
static Color WithA(Color c, float a) { return Color((BYTE)(c.GetA() * a), c.GetR(), c.GetG(), c.GetB()); }

// Police des titres : Bahnschrift (Windows 10 et plus, geometrique) si elle est la, sinon Segoe UI.
static const wchar_t *TitleFont()
{
    static int ok = -1;
    if (ok < 0) { FontFamily f(L"Bahnschrift"); ok = f.IsAvailable() ? 1 : 0; }
    return ok ? L"Bahnschrift" : L"Segoe UI";
}
// Polices gardees (en creer une a chaque texte coutait l'essentiel du temps d'une image). Jamais liberees (fin du
// programme seulement).
static Font *FontOf(const wchar_t *family, float px, int style)
{
    static std::map<std::wstring, Font *> cache;
    wchar_t k[96];
    swprintf_s(k, L"%s|%d|%d", family, (int)(px * 20 + 0.5f), style);
    auto it = cache.find(k);
    if (it != cache.end()) return it->second;
    FontFamily fam(family);
    Font *f = new Font(&fam, px, style, UnitPixel);
    cache[k] = f;
    return f;
}
static void TextF(Graphics &g, const wchar_t *family, const std::wstring &s, RectF r, float px, int style, Color c, StringAlignment h = StringAlignmentCenter)
{
    Font &font = *FontOf(family, px, style);
    StringFormat sf;
    sf.SetAlignment(h);
    sf.SetLineAlignment(StringAlignmentCenter);
    sf.SetTrimming(StringTrimmingEllipsisCharacter);
    sf.SetFormatFlags(StringFormatFlagsNoWrap);
    SolidBrush b(c);
    g.DrawString(s.c_str(), -1, &font, r, &sf, &b);
}
// Titre (police des titres, demi-gras).
static void Title(Graphics &g, const std::wstring &s, RectF r, float px, Color c, StringAlignment h = StringAlignmentNear)
{
    TextF(g, TitleFont(), s, r, px, !wcscmp(TitleFont(), L"Bahnschrift") ? FontStyleBold : FontStyleBold, c, h);
}
static void Text(Graphics &g, const std::wstring &s, RectF r, float px, int style, Color c, StringAlignment h = StringAlignmentCenter)
{
    Font &font = *FontOf(L"Segoe UI", px, style);
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
    Font &font = *FontOf(L"Segoe UI", px, FontStyleRegular);
    StringFormat sf;
    sf.SetLineAlignment(v);
    sf.SetTrimming(StringTrimmingEllipsisWord);
    SolidBrush b(c);
    g.DrawString(s.c_str(), -1, &font, r, &sf, &b);
}

// Graphics de mesure (une image de 1 pixel, gardee).
static Graphics &MeasureG()
{
    static Bitmap *b;
    static Graphics *g;
    if (!g) { b = new Bitmap(1, 1); g = new Graphics(b); }
    return *g;
}
static float MeasureW(Graphics &g, const std::wstring &s, float px, int style)
{
    Font &font = *FontOf(L"Segoe UI", px, style);
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
// Choix du reseau : barre a deux segments (le choisi en degrade).
static void DrawNetChoice(Graphics &g)
{
    if (!g_btn[B_NETIP].visible) return;
    RectF a = g_btn[B_NETIP].r, b2 = g_btn[B_NETSTEAM].r, track(a.X, a.Y, b2.X + b2.Width - a.X, a.Height);
    GraphicsPath tp;
    RoundRect(tp, track, track.Height / 2);
    SolidBrush tb(TH(field));
    g.FillPath(&tb, &tp);
    Pen tpen(TH(fieldBorder), 1.2f);
    g.DrawPath(&tpen, &tp);
    for (int id = B_NETIP; id <= B_NETSTEAM; id++) {
        Button &b = g_btn[id];
        bool on = (id == B_NETSTEAM) == g_steamNet;
        RectF r = b.r;
        r.Inflate(-3, -3);
        GraphicsPath p;
        RoundRect(p, r, r.Height / 2);
        if (on) { LinearGradientBrush lg(r, kAcc, kAcc2, LinearGradientModeHorizontal); g.FillPath(&lg, &p); }
        else if (b.hover > 0.01f) { SolidBrush hb(WithA(kAcc, 0.2f * b.hover)); g.FillPath(&hb, &p); }
        Text(g, id == B_NETIP ? L"IP / VPN" : L"STEAM", r, 12, FontStyleBold, on ? kOnAcc : Mix(kInk, kAcc, b.hover));
    }
}

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
    if (label && *label) Text(g, label, RectF(f.r.X + 2, f.r.Y - 18, f.r.Width, 16), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
    GraphicsPath p;
    RoundRect(p, f.r, 14);
    SolidBrush fill(TH(field));
    g.FillPath(&fill, &p);
    Pen pen(g_focus == i ? kAcc : TH(fieldBorder), g_focus == i ? 2.0f : 1.2f);
    g.DrawPath(&pen, &p);
    RectF tr(f.r.X + 16, f.r.Y, f.r.Width - 32, f.r.Height);
    std::wstring shown = f.text;
    bool placeholder = shown.empty() && g_focus != i;
    if (placeholder) shown = f.address ? T(L"ex. 26.12.34.56 (adresse:port accept\u00E9)", L"e.g. 26.12.34.56 (address:port accepted)") : DefaultName();
    Text(g, shown, tr, placeholder && f.address ? 13.5f : 16.0f, placeholder ? FontStyleRegular : FontStyleBold, placeholder ? TH(placeholder) : kInk, StringAlignmentNear);
    if (g_focus == i && fmodf(g_time, 1.0f) < 0.55f) {
        FontFamily fam(L"Segoe UI");
        Font font(&fam, 16, FontStyleBold, UnitPixel);
        StringFormat sf(StringFormat::GenericTypographic());
        sf.SetFormatFlags(StringFormatFlagsMeasureTrailingSpaces | StringFormatFlagsNoWrap);
        RectF box;
        g.MeasureString(f.text.c_str(), -1, &font, PointF(0, 0), &sf, &box);
        float x = min(tr.X + box.Width + 1, tr.X + tr.Width);
        Pen cp(kAcc, 1.6f);
        g.DrawLine(&cp, x, f.r.Y + 12, x, f.r.Y + f.r.Height - 12);
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

#include "uikit.inc"

// ---------------------------------------------------------------- options (MWCoop\mwcoop.ini, [Coop])
// Memes cles et valeurs par defaut que le mod (Net\Session.cs) ; ecrites tout de suite, prises au prochain lancement.
enum { O_TOGGLE, O_CHOICE, O_ACTION };   // O_ACTION : ouvre le volet (action = DR_*)
enum { DR_NONE, DR_SKIN, DR_CAR, DR_LOBBY, DR_CONTENT };   // contenu du volet a droite (drawer.inc)
struct Opt {
    int tab; const char *key; int def; int kind; std::vector<int> vals;
    std::vector<std::string> svals;              // valeurs texte (Apparence) : vals = 0..n-1, def = index
    const wchar_t *fr, *en;
    std::vector<std::wstring> labFr, labEn;      // vide : la valeur + suffixe
    const wchar_t *suffix;
    const wchar_t *dFr, *dEn;
    int action;                                  // O_ACTION : volet ouvert (DR_*)
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
    {   // Lanceur ferme quand le jeu demarre (sinon : partie en cours, etat du serveur ; demande de JD du 07/10)
        Opt o = {};
        o.tab = TAB_COOP; o.key = "FermerLanceur"; o.def = 0; o.kind = O_TOGGLE;
        o.fr = L"Fermer le lanceur au lancement du jeu"; o.en = L"Close the launcher when the game starts"; o.suffix = L"";
        o.dFr = L"D\u00E9sactiv\u00E9 : le lanceur reste ouvert pendant la partie (joueurs, ping ; l'h\u00F4te peut faire venir ou exclure un joueur) et revient au menu quand le jeu se ferme.";
        o.dEn = L"Off: the launcher stays open during the game (players, ping; the host can bring a player over or kick them) and goes back to the menu when the game closes.";
        g_opts.push_back(o);
    }
    {   // Lancer le jeu par Steam (steam://rungameid) au lieu de la copie de lancement : chez un joueur (08/10), MWCoop ne se
        // chargeait que par le bouton JOUER de Steam (jeu lance de la copie : menu sans MWCoop, aucun journal du chargeur).
        Opt o = {};
        o.tab = TAB_COOP; o.key = "LancerSteam"; o.def = 0; o.kind = O_TOGGLE;
        o.fr = L"Lancer le jeu par Steam"; o.en = L"Start the game through Steam"; o.suffix = L"";
        o.dFr = L"Comme le bouton JOUER de Steam, avec les r\u00E9glages de la partie. Si MWCoop ne se charge pas depuis le lanceur.";
        o.dEn = L"Like Steam's PLAY button, with the session settings. If MWCoop doesn't load from the launcher.";
        g_opts.push_back(o);
    }
    {   // Contenu envoye aux invites (modsync.inc) : volet a cocher
        Opt o = {};
        o.tab = TAB_COOP; o.key = "ContenuEnvoye"; o.kind = O_ACTION; o.action = DR_CONTENT;
        o.fr = L"Envoy\u00E9 aux invit\u00E9s (CD, images\u2026)"; o.en = L"Sent to guests (CDs, images\u2026)"; o.suffix = L"";
        o.dFr = L"Tes CD, ta radio, la peinture de ta CORRIS, ton drapeau, tes posters\u2026 copi\u00E9s chez les invit\u00E9s de ton salon pour ta partie.";
        o.dEn = L"Your CDs, radio, CORRIS paint, flag, posters\u2026 copied to your lobby's guests for your game.";
        g_opts.push_back(o);
    }
    {   // Mods de l'hote (modsync.inc) : TOUT ACCEPTER une fois l'allume
        Opt o = {};
        o.tab = TAB_COOP; o.key = "ModsAuto"; o.def = 0; o.kind = O_TOGGLE;
        o.fr = L"Accepter l'envoi de l'h\u00F4te sans demander"; o.en = L"Accept the host's files without asking"; o.suffix = L"";
        o.dFr = L"Dans un salon, les mods MSCLoader, CD et images de l'h\u00F4te sont copi\u00E9s \u00E0 part (les tiens ne sont pas touch\u00E9s) et utilis\u00E9s pour sa partie. Coup\u00E9 : le salon demande d'abord (TOUT ACCEPTER).";
        o.dEn = L"In a lobby, the host's MSCLoader mods, CDs and images are copied separately (yours are not touched) and used for their game. Off: the lobby asks first (ACCEPT ALL).";
        g_opts.push_back(o);
    }
    {   // Apparence : materiaux des corps des PNJ du jeu (Sync\Avatar.cs)
        Opt o = {};
        o.tab = TAB_COOP; o.key = "Apparence"; o.kind = O_ACTION; o.action = DR_SKIN;
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
        o.dFr = L"Le personnage que les autres joueurs voient : la tenue d'un habitant, ou celle du policier, du pilote de rallye\u2026 Clic : choix en 3D dans le volet.";
        o.dEn = L"The character the other players see: a local's outfit, or the police officer's, the rally driver's\u2026 Click: 3D picker in the side panel.";
        g_opts.push_back(o);
    }
    {   // Visite guidee (tuto.inc) : a revoir
        Opt o = {};
        o.tab = TAB_COOP; o.key = "RevoirGuide"; o.kind = O_ACTION; o.action = 100;
        o.fr = L"Revoir le guide de d\u00E9marrage"; o.en = L"See the starting guide again"; o.suffix = L"";
        o.dFr = L"La visite du lanceur en quelques bulles, comme au premier lancement.";
        o.dEn = L"The launcher tour in a few bubbles, as on the first start.";
        g_opts.push_back(o);
    }
    {   // Couleur de la CORRIS d'une nouvelle partie (volet)
        Opt o = {};
        o.tab = TAB_COOP; o.key = "CouleurVoiture"; o.kind = O_ACTION; o.action = DR_CAR;
        o.fr = L"Couleur de la voiture"; o.en = L"Car color"; o.suffix = L"";
        o.dFr = L"Couleur de la CORRIS pour une nouvelle partie (l'h\u00F4te la donne aux invit\u00E9s). Clic : aper\u00E7u en 3D dans le volet.";
        o.dEn = L"CORRIS color for a new game (the host gives it to the guests). Click: 3D preview in the side panel.";
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

static std::vector<int> TabRows(int t)
{
    std::vector<int> r;
    for (int i = 0; i < (int)g_opts.size(); i++)
        if (g_opts[i].tab == t) r.push_back(i);
    return r;
}
static float NotesMaxScroll();
static float LogsMaxScroll();
static float GfxMaxScroll();
static float LobbyMaxScroll();
static float MscMaxScroll();
static float WikiMaxScroll();   // (wiki.inc)
static void DrawMods(Graphics &g);
static void DrawServer(Graphics &g);
static float MaxScroll(int t) { return t == TAB_MODS ? MscMaxScroll() : t == TAB_LOBBY ? LobbyMaxScroll() : t == TAB_LOGS ? LogsMaxScroll() : t == TAB_GFX ? GfxMaxScroll() : t == TAB_NOTES ? NotesMaxScroll() : t == TAB_WIKI ? WikiMaxScroll() : 0.0f; }

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
static void TutoStart(bool all);
static std::wstring ContentSummary();   // (modsync.inc)
static std::wstring CarColorName();
static void OptStep(int idx, int dir)
{
    const Opt &o = g_opts[idx];
    if (o.kind == O_ACTION) { if (o.action == 100) TutoStart(true); else GoPage(o.action == DR_SKIN ? TAB_SKIN : o.action == DR_CAR ? TAB_CAR : TAB_CONTENT); return; }
    int v = OptGet(o);
    if (o.kind == O_TOGGLE) { OptSet(o, v ? 0 : 1); return; }
    int i = ValueIndex(o, v);
    if (o.vals[i] != v && dir > 0) i--;
    i = (i + dir + (int)o.vals.size()) % (int)o.vals.size();
    OptSet(o, o.vals[i]);
}

static bool NotesUnseen();
static float NotesMaxScroll();

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

// ---------------------------------------------------------------- images des notes
// Telechargees a la premiere ouverture de l'onglet, gardees dans %LOCALAPPDATA%\MWCoop\cache\notes\ (hors ligne ensuite).
enum { NI_WAIT, NI_LOAD, NI_OK, NI_FAIL };
struct NoteImg { int state; Bitmap *bmp; };
static std::map<std::wstring, NoteImg> g_noteImgs;   // (sous g_cs)
static std::wstring LocalDir();
static Bitmap *LoadPngMem(const void *p, size_t n, UINT maxW, UINT maxH);

static std::wstring NoteImgFile(const std::wstring &url)
{
    unsigned long long h = 1469598103934665603ULL;
    for (wchar_t c : url) { h ^= (unsigned)c; h *= 1099511628211ULL; }
    wchar_t n[32];
    swprintf_s(n, L"%016llx.img", h);
    std::wstring dir = LocalDir();
    if (dir.empty()) return L"";
    CreateDirectoryW(dir.c_str(), NULL);
    CreateDirectoryW((dir + L"cache").c_str(), NULL);
    CreateDirectoryW((dir + L"cache\\notes").c_str(), NULL);
    return dir + L"cache\\notes\\" + n;
}
static void NoteImgFetch(const std::wstring &url)
{
    std::wstring file = NoteImgFile(url);
    Bitmap *b = NULL;
    if (!file.empty()) {
        if (!FileExists(file)) {
            std::wstring part = file + L".part";
            if (HttpGet(url, NULL, part, false)) MoveFileExW(part.c_str(), file.c_str(), MOVEFILE_REPLACE_EXISTING);
            else DeleteFileW(part.c_str());
        }
        HANDLE f = CreateFileW(file.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
        if (f != INVALID_HANDLE_VALUE) {
            DWORD size = GetFileSize(f, NULL), r = 0;
            std::vector<char> d;
            if (size != INVALID_FILE_SIZE && size > 0 && size < 16u << 20) { d.resize(size); ReadFile(f, d.data(), size, &r, NULL); d.resize(r); }
            CloseHandle(f);
            if (!d.empty()) b = LoadPngMem(d.data(), d.size(), 8192, 8192);
            if (!b) DeleteFileW(file.c_str());   // (illisible : on reessaiera au prochain lancement)
        }
    }
    TestLog("note : image %s : %s", Narrow(url).c_str(), b ? "ok" : "illisible ou introuvable");
    EnterCriticalSection(&g_cs);
    NoteImg &ni = g_noteImgs[url];
    ni.bmp = b;
    ni.state = b ? NI_OK : NI_FAIL;
    LeaveCriticalSection(&g_cs);
}
static DWORD WINAPI NoteImgThread(void *p)
{
    std::wstring *url = (std::wstring *)p;
    NoteImgFetch(*url);
    delete url;
    return 0;
}
// Etat de l'image (et la lance si besoin) ; *bmp : l'image prete.
static int NoteImgGet(const std::wstring &url, Bitmap **bmp)
{
    EnterCriticalSection(&g_cs);
    auto it = g_noteImgs.find(url);
    if (it == g_noteImgs.end()) it = g_noteImgs.insert(std::make_pair(url, NoteImg{ NI_WAIT, NULL })).first;
    int st = it->second.state;
    *bmp = it->second.bmp;
    if (st == NI_WAIT) it->second.state = NI_LOAD;
    LeaveCriticalSection(&g_cs);
    if (st == NI_WAIT) {
        HANDLE t = CreateThread(NULL, 0, NoteImgThread, new std::wstring(url), 0, NULL);
        if (t) CloseHandle(t);
        st = NI_LOAD;
    }
    return st;
}

static RectF NotesArea();   // (ui.inc)
// Images montrees (dernier dessin) : un clic ouvre l'image en grand dans le navigateur.
static std::vector<std::pair<RectF, std::wstring>> g_noteImgHits;
static std::wstring NoteImgAt(float x, float y)
{
    if (g_tab != TAB_NOTES || g_state != ST_IDLE || !NotesArea().Contains(x, y)) return L"";
    for (const auto &h : g_noteImgHits) if (h.first.Contains(x, y)) return h.second;
    return L"";
}
static float NotesMaxScroll() { return max(0.0f, g_notesH - NotesArea().Height); }

// ---------------------------------------------------------------- page JOURNAUX (onglet, et bouton rond)
// Les journaux du chargeur et du mod (MWCoop\logs\chargeur.log, mwcoop.log), ceux des profils (MWCoop\profils\<nom>\logs)
// et celui de Unity s'il existe (mywintercar_Data\output_log.txt). Depuis 0.53 le chargeur range ceux de la partie
// precedente dans logs\sessions\<date>\ a chaque lancement (30 parties gardees) : la page les montre PAR PARTIE, un
// groupe repliable chacune (la derniere partie ouverte, les autres repliees). En-tete d'un groupe : date et heures,
// version, mode (solo, hote, invite), erreurs, arret brutal ; ses boutons : dossier, zip de cette partie, supprimer
// (corbeille de Windows, second clic pour confirmer). Un clic sur un journal l'ouvre, l'icone dossier le montre dans
// l'explorateur. En haut : "Tout supprimer" (les parties rangees), "Ouvrir le dossier" et "Creer un zip a envoyer"
// (la derniere partie + mwcoop.ini, lancement.ini et le journal du lanceur, sur le Bureau).
enum { LOG_MOD, LOG_LOADER, LOG_UNITY };
struct LogEntry { std::wstring path, profile; int kind; bool guest; uint64_t bytes; FILETIME mt; int errors; int group; };
struct LogGroup {
    std::wstring key;          // "*" : la derniere partie (journaux en place) ; sinon le dossier de la partie rangee
    std::wstring dir, profile; // (dossier : vide pour la derniere partie)
    FILETIME end = {};         // fin de la partie (nom du dossier ; en place : le journal le plus recent)
    std::wstring version, mode, startHM;
    int errors = 0, files = 0; uint64_t bytes = 0; bool crash = false;
    bool live = false;         // la derniere partie, jeu encore ouvert
    std::vector<int> items;    // entrees de g_logList
};
struct LogRow { int group, entry; float y, h; };   // entry -1 : en-tete du groupe
static std::vector<LogEntry> g_logList;
static std::vector<LogGroup> g_logGroups;
static std::vector<LogRow> g_logRows;
static std::vector<std::wstring> g_logOpen = { L"*" };   // groupes deplies (cles)
static float g_logTotalH;
static int g_logRowHot = -1, g_logPart = 0;          // ligne de g_logRows ; g_logPart : 0 la ligne, 1 dossier, 2 zip, 3 supprimer
static std::wstring g_logDelKey;                      // suppression armee (second clic dans les 4 s) : cle du groupe, ou "**" (tout)
static DWORD g_logDelT;
static RectF kLogsR(288, 172, 952, 554);   // (place par la page : ui.inc)
static std::wstring g_logSelPath;                     // dernier journal ouvert d'un clic (bouton "Ouvrir le dossier")
static const float kLogRowH = 66, kLogHeadH = 60, kLogGap = 8;
static void LogsGroupZip(int gi);
static bool LogsGroupDelete(int gi);
static void LogsDeleteAll();
static void LogsLayout();

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

// Fin anormale du dernier jeu (crash, processus tue) : depuis 0.32 le mod finit son journal par "jeu ferme
// normalement" (OnApplicationQuit). Le mwcoop.log le plus recent (joueur, profils, secours LOCALAPPDATA) sans cette
// ligne, ecrit par une 0.32 ou plus (les anciens ne l'ont jamais) -> "!" rouge sur l'icone des journaux, jusqu'a ce
// qu'on les ouvre ([Lanceur] CrashVu = heure de ce journal).
static long long g_crashT;
static long long FileUnixTime(const std::wstring &p)
{
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a)) return 0;
    ULARGE_INTEGER u; u.LowPart = a.ftLastWriteTime.dwLowDateTime; u.HighPart = a.ftLastWriteTime.dwHighDateTime;
    return (long long)(u.QuadPart / 10000000ULL) - 11644473600LL;
}
static bool GameProcessRunning();
static void CrashCheck()
{
    g_crashT = 0;
    if (g_gameDir.empty() || GameProcessRunning()) return;
    std::vector<std::wstring> logs = { LogsDir() + L"mwcoop.log" };
    for (const std::wstring &pr : Profiles()) logs.push_back(g_gameDir + L"MWCoop\\profils\\" + pr + L"\\logs\\mwcoop.log");
    if (!LocalDir().empty()) logs.push_back(LocalDir() + L"logs\\mwcoop.log");
    std::wstring newest;
    long long nt = 0;
    for (auto &l : logs) { long long t = FileUnixTime(l); if (t > nt) { nt = t; newest = l; } }
    if (newest.empty() || (long long)_time64(NULL) - nt > 14 * 86400) return;
    HANDLE f = CreateFileW(newest.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    char head[160] = {}, tail[4096] = {};
    DWORD n = 0;
    ReadFile(f, head, sizeof(head) - 1, &n, NULL);
    LARGE_INTEGER sz = {};
    GetFileSizeEx(f, &sz);
    LARGE_INTEGER at; at.QuadPart = max(0LL, sz.QuadPart - (long long)sizeof(tail) + 1);
    SetFilePointerEx(f, at, NULL, FILE_BEGIN);
    ReadFile(f, tail, sizeof(tail) - 1, &n, NULL);
    CloseHandle(f);
    // 1re ligne : "hh:mm:ss.mmm MWCoop 0.32.0-prealpha - Unity ..."
    const char *v = strstr(head, "MWCoop ");
    int a = 0, b = 0;
    if (!v || sscanf_s(v + 7, "%d.%d", &a, &b) != 2 || (a == 0 && b < 32)) return;
    if (strstr(tail, "jeu ferme normalement")) return;
    wchar_t seen[32] = L"0";
    GetPrivateProfileStringW(L"Lanceur", L"CrashVu", L"0", seen, 32, g_iniLauncher.c_str());
    if (nt <= _wtoi64(seen)) return;
    g_crashT = nt;
    TestLog("journaux : le dernier jeu s'est arrete brutalement (%s)", Narrow(newest, CP_UTF8).c_str());
}
static void CrashSeen()
{
    if (!g_crashT) return;
    WritePrivateProfileStringW(L"Lanceur", L"CrashVu", std::to_wstring(g_crashT).c_str(), g_iniLauncher.c_str());
    g_crashT = 0;
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

// Une partie d'apres son journal du mod : version (1re ligne), heure du debut, mode, et fin normale ou non.
static void SessionInfo(LogGroup &gr, const std::wstring &modLog, bool running)
{
    HANDLE f = CreateFileW(modLog.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    char head[8192] = {}, tail[4096] = {};
    DWORD n = 0;
    ReadFile(f, head, sizeof(head) - 1, &n, NULL);
    LARGE_INTEGER sz = {};
    GetFileSizeEx(f, &sz);
    LARGE_INTEGER at; at.QuadPart = max(0LL, sz.QuadPart - (long long)sizeof(tail) + 1);
    SetFilePointerEx(f, at, NULL, FILE_BEGIN);
    ReadFile(f, tail, sizeof(tail) - 1, &n, NULL);
    CloseHandle(f);
    if (head[0] >= '0' && head[0] <= '9' && head[2] == ':') gr.startHM = Widen(std::string(head, 2) + "h" + std::string(head + 3, 2));
    const char *v = strstr(head, "MWCoop ");
    int a = 0, b = 0, c = 0;
    if (v && sscanf_s(v + 7, "%d.%d.%d", &a, &b, &c) >= 2) { wchar_t vb[32]; swprintf_s(vb, L"%d.%d.%d", a, b, c); gr.version = vb; }
    if (strstr(head, "hote sur le port") || strstr(head, "hote Steam") || strstr(head, ", hote")) gr.mode = T(L"Hôte", L"Host");
    else if (strstr(head, "connexion a ") || strstr(head, "invite par Steam")) gr.mode = T(L"Invité", L"Guest");
    else if (v) gr.mode = T(L"Solo", L"Solo");
    gr.crash = !running && v && !(a == 0 && b < 32) && !strstr(tail, "jeu ferme normalement");
}

static void LogsScan()
{
    std::vector<LogEntry> list;
    std::vector<LogGroup> groups;
    bool running = GameProcessRunning();
    auto add = [&](const std::wstring &p, int kind, const std::wstring &profile, int group) {
        WIN32_FILE_ATTRIBUTE_DATA a;
        if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a) || (a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) return;
        LogEntry e;
        e.path = p; e.kind = kind; e.profile = profile; e.guest = !_wcsnicmp(profile.c_str(), L"invite", 6); e.group = group;
        e.bytes = ((uint64_t)a.nFileSizeHigh << 32) | a.nFileSizeLow;
        e.mt = a.ftLastWriteTime;
        e.errors = kind == LOG_UNITY ? -1 : CountErrors(p, e.bytes);   // (Unity : trop d'exceptions sans gravite)
        list.push_back(e);
    };
    // Groupe 0 : la derniere partie (journaux en place).
    groups.push_back(LogGroup());
    groups[0].key = L"*";
    std::wstring ld = LocalDir();
    if (!g_gameDir.empty()) {
        add(LogsDir() + L"mwcoop.log", LOG_MOD, L"", 0);
        add(LogsDir() + L"chargeur.log", LOG_LOADER, L"", 0);
        // Chaque profil (invite, ou celui de mwcoop.ini [Test] Profil) a ses propres journaux.
        for (const std::wstring &pr : Profiles()) {
            std::wstring d = g_gameDir + L"MWCoop\\profils\\" + pr + L"\\logs\\";
            add(d + L"mwcoop.log", LOG_MOD, pr, 0);
            add(d + L"chargeur.log", LOG_LOADER, pr, 0);
        }
        add(g_gameDir + L"mywintercar_Data\\output_log.txt", LOG_UNITY, L"", 0);
    }
    // Repli quand le dossier du jeu est en lecture seule, et trace de chargement du mod.
    if (!ld.empty()) {
        add(ld + L"logs\\mwcoop.log", LOG_MOD, L"(secours)", 0);
        add(ld + L"logs\\chargeur.log", LOG_LOADER, L"(secours)", 0);
        add(ld + L"profils\\invite\\logs\\mwcoop.log", LOG_MOD, L"invite (secours)", 0);
        add(ld + L"profils\\invite\\logs\\chargeur.log", LOG_LOADER, L"invite (secours)", 0);
        add(ld + L"dernier-lancement.txt", LOG_LOADER, L"trace de lancement", 0);
    }
    // Parties rangees par le chargeur : <logs>\sessions\AAAA-MM-JJ_HHhMMmSS\ (joueur, profils, secours).
    std::vector<std::pair<std::wstring, std::wstring>> bases;   // (dossier logs\, profil)
    if (!g_gameDir.empty()) {
        bases.push_back({ LogsDir(), L"" });
        for (const std::wstring &pr : Profiles()) bases.push_back({ g_gameDir + L"MWCoop\\profils\\" + pr + L"\\logs\\", pr });
    }
    if (!ld.empty()) { bases.push_back({ ld + L"logs\\", L"(secours)" }); bases.push_back({ ld + L"profils\\invite\\logs\\", L"invite (secours)" }); }
    for (auto &b : bases) {
        WIN32_FIND_DATAW fd;
        HANDLE h = FindFirstFileW((b.first + L"sessions\\*").c_str(), &fd);
        if (h == INVALID_HANDLE_VALUE) continue;
        do {
            if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || fd.cFileName[0] == L'.') continue;
            LogGroup gr;
            gr.dir = b.first + L"sessions\\" + fd.cFileName + L"\\";
            gr.key = gr.dir;
            gr.profile = b.second;
            gr.end = fd.ftLastWriteTime;
            SYSTEMTIME st = {};
            if (swscanf_s(fd.cFileName, L"%hu-%hu-%hu_%huh%hum%hu", &st.wYear, &st.wMonth, &st.wDay, &st.wHour, &st.wMinute, &st.wSecond) == 6) {
                FILETIME lt;
                if (SystemTimeToFileTime(&st, &lt)) LocalFileTimeToFileTime(&lt, &gr.end);
            }
            int gi = (int)groups.size();
            groups.push_back(gr);
            add(gr.dir + L"mwcoop.log", LOG_MOD, b.second, gi);
            add(gr.dir + L"chargeur.log", LOG_LOADER, b.second, gi);
            add(gr.dir + L"output_log.txt", LOG_UNITY, b.second, gi);
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    // Totaux des groupes ; la derniere partie : son journal le plus recent, et son journal du mod (joueur d'abord).
    for (int i = 0; i < (int)list.size(); i++) {
        LogGroup &gr = groups[list[i].group];
        gr.items.push_back(i);
        gr.files++;
        gr.bytes += list[i].bytes;
        if (list[i].errors > 0) gr.errors += list[i].errors;
        // (la fin : journaux du mod et du chargeur seulement -- la trace de lancement est commune a tous les profils,
        // celui de Unity peut etre plus vieux)
        bool own = list[i].kind != LOG_UNITY && list[i].profile != L"trace de lancement";
        if (list[i].group == 0 && own && CompareFileTime(&list[i].mt, &gr.end) > 0) gr.end = list[i].mt;
    }
    for (int gi = 0; gi < (int)groups.size(); gi++) {
        LogGroup &gr = groups[gi];
        std::wstring mod, modT;
        FILETIME best = {};
        for (int i : gr.items)
            if (list[i].kind == LOG_MOD && (mod.empty() || CompareFileTime(&list[i].mt, &best) > 0)) { mod = list[i].path; best = list[i].mt; }
        if (!mod.empty()) SessionInfo(gr, mod, gi == 0 && running);
        gr.live = gi == 0 && running;
        // Les plus recents d'abord dans un groupe.
        std::sort(gr.items.begin(), gr.items.end(), [&](int a, int b) { return CompareFileTime(&list[a].mt, &list[b].mt) > 0; });
    }
    // Groupes : la derniere partie, puis les parties rangees, les plus recentes d'abord ; sans fichier : ecartees.
    std::vector<LogGroup> kept;
    std::vector<int> remap(groups.size(), -1);
    std::vector<int> order;
    for (int gi = 1; gi < (int)groups.size(); gi++) if (groups[gi].files > 0) order.push_back(gi);
    std::sort(order.begin(), order.end(), [&](int a, int b) { return CompareFileTime(&groups[a].end, &groups[b].end) > 0; });
    if (groups[0].files > 0) order.insert(order.begin(), 0);
    for (int gi : order) { remap[gi] = (int)kept.size(); kept.push_back(groups[gi]); }
    for (auto &e : list) e.group = remap[e.group];
    g_logList.swap(list);
    g_logGroups.swap(kept);
    LogsLayout();
    g_scroll[TAB_LOGS] = min(g_scroll[TAB_LOGS], LogsMaxScroll());
}

static bool LogGroupOpen(const LogGroup &gr) { return std::find(g_logOpen.begin(), g_logOpen.end(), gr.key) != g_logOpen.end(); }

// Lignes de la liste (en-tetes, et journaux des groupes deplies) et leur hauteur totale.
static void LogsLayout()
{
    g_logRows.clear();
    float y = 0;
    for (int gi = 0; gi < (int)g_logGroups.size(); gi++) {
        g_logRows.push_back({ gi, -1, y, kLogHeadH });
        y += kLogHeadH;
        if (LogGroupOpen(g_logGroups[gi]))
            for (int i : g_logGroups[gi].items) { g_logRows.push_back({ gi, i, y, kLogRowH }); y += kLogRowH; }
        y += kLogGap;
    }
    g_logTotalH = y;
}

static float LogsMaxScroll() { return max(0.0f, g_logTotalH - kLogsR.Height); }

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

static RectF LogRowRect(int i)
{
    const LogRow &w = g_logRows[i];
    float indent = w.entry >= 0 ? 28.0f : 0.0f;
    return RectF(kLogsR.X + indent, kLogsR.Y + w.y - g_scroll[TAB_LOGS], kLogsR.Width - 10 - indent, w.h - 6);
}
static RectF LogIconRect(const RectF &r) { return RectF(r.X + r.Width - 50, r.Y + (r.Height - 38) / 2, 38, 38); }
// En-tete d'un groupe : 1 dossier, 2 zip, 3 supprimer (de gauche a droite).
static RectF LogHeadBtn(const RectF &r, int part) { return RectF(r.X + r.Width - 50 - (3 - part) * 46, r.Y + (r.Height - 38) / 2, 38, 38); }

static int LogRowAt(float x, float y, int *part)
{
    *part = 0;
    if (!kLogsR.Contains(x, y)) return -1;
    for (int i = 0; i < (int)g_logRows.size(); i++) {
        RectF r = LogRowRect(i);
        if (!r.Contains(x, y)) continue;
        if (g_logRows[i].entry < 0) {
            for (int p = 1; p <= 3; p++) { RectF b = LogHeadBtn(r, p); b.Inflate(2, 2); if (b.Contains(x, y)) *part = p; }
        } else {
            RectF f = LogIconRect(r);
            f.Inflate(2, 2);
            *part = f.Contains(x, y) ? 1 : 0;
        }
        return i;
    }
    return -1;
}

static bool LogsMouseDown(float x, float y)
{
    int part, i = LogRowAt(x, y, &part);
    if (i < 0) return false;
    const LogRow w = g_logRows[i];
    if (w.entry < 0) {
        LogGroup &gr = g_logGroups[w.group];
        if (part == 0) {   // plier / deplier
            auto it = std::find(g_logOpen.begin(), g_logOpen.end(), gr.key);
            if (it != g_logOpen.end()) g_logOpen.erase(it); else g_logOpen.push_back(gr.key);
            LogsLayout();
            g_scroll[TAB_LOGS] = min(g_scroll[TAB_LOGS], LogsMaxScroll());
        } else if (part == 1) {
            std::wstring d = !gr.dir.empty() ? gr.dir : (!gr.items.empty() ? g_logList[gr.items[0]].path : L"");
            if (!gr.dir.empty()) ShellExecuteW(g_wnd, L"open", d.c_str(), NULL, NULL, SW_SHOWNORMAL);
            else if (!d.empty()) { std::wstring arg = L"/select,\"" + d + L"\""; ShellExecuteW(g_wnd, L"open", L"explorer.exe", arg.c_str(), NULL, SW_SHOWNORMAL); }
        } else if (part == 2) LogsGroupZip(w.group);
        else if (part == 3) {
            if (g_logDelKey == gr.key && GetTickCount() - g_logDelT < 4000) { g_logDelKey.clear(); LogsGroupDelete(w.group); }
            else {
                g_logDelKey = gr.key; g_logDelT = GetTickCount();
                SetStatus(K_WARN, T(L"Clique encore sur la corbeille pour supprimer ces journaux", L"Click the bin again to delete these logs"));
            }
        }
        return true;
    }
    std::wstring path = g_logList[w.entry].path;
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
static const RectF kCarCard(264, 88, 640, 656), kCarPanel(920, 88, 344, 656);
static const RectF kCarView(284, 120, 600, 470);
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
static RectF CarSwatchRect(int i) { return RectF(kCarPanel.X + 26 + (i % 4) * 76.0f, kCarPanel.Y + 64 + (i / 4) * 72.0f, 56, 56); }
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
    Glass(kCarCard);
    Glass(kCarPanel);
    RectF view = kCarView;
    if (g_car.state != 1) {
        const wchar_t *msg = g_car.state == -2 ? T(L"Aper\u00E7u illisible (MWCoop\\cache\\corris.mesh) : lance une partie pour le refaire.", L"Preview unreadable (MWCoop\\cache\\corris.mesh): start a game to rebuild it.")
                                               : T(L"Lance une partie une fois pour voir l'aper\u00E7u de la voiture.", L"Start a game once to see the car preview.");
        Icon(g, IC_CAR, view.X + view.Width / 2, view.Y + view.Height / 2 - 40, 64, WithA(kGrey, 0.6f), 1.4f);
        Para(g, msg, RectF(view.X + 80, view.Y + view.Height / 2 + 10, view.Width - 160, 60), 14, kGrey, StringAlignmentNear);
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
            g.FillPath(&pb, &sp);
        }
        Bitmap bm(g_carW, g_carH, g_carW * 4, PixelFormat32bppPARGB, (BYTE *)g_carPix.data());
        InterpolationMode im = g.GetInterpolationMode();
        g.SetInterpolationMode(InterpolationModeNearestNeighbor);   // 1 pixel pour 1 pixel (deja lisse)
        g.DrawImage(&bm, view);
        g.SetInterpolationMode(im);
    }
    Title(g, T(L"Ta CORRIS", L"Your CORRIS"), RectF(kCarCard.X + 24, kCarCard.Y + kCarCard.Height - 100, 400, 32), 22, kInk);
    Text(g, T(L"Cette couleur pour une nouvelle partie ; l'h\u00F4te la donne \u00E0 ses invit\u00E9s.", L"This color for a new game; the host gives it to their guests."),
         RectF(kCarCard.X + 24, kCarCard.Y + kCarCard.Height - 66, 460, 22), 13, FontStyleRegular, kGrey, StringAlignmentNear);
    if (g_car.state == 1) Text(g, T(L"Glisse pour tourner", L"Drag to rotate"), RectF(kCarCard.X + kCarCard.Width - 224, kCarCard.Y + kCarCard.Height - 66, 200, 22), 12.5f, FontStyleRegular, kGrey, StringAlignmentFar);

    // Couleur : pastilles, nom, autre couleur
    RectF pn = kCarPanel;
    Title(g, T(L"Couleur", L"Color"), RectF(pn.X + 24, pn.Y + 20, 200, 26), 17, kInk);
    int sel = CarSwatchSel(), show = g_carHot >= 0 ? g_carHot : sel;
    for (int i = 0; i < kSwatchN; i++) {
        RectF r = CarSwatchRect(i);
        if (i == sel) { Pen ring(kAcc, 2.6f); g.DrawEllipse(&ring, r.X - 5, r.Y - 5, r.Width + 10, r.Height + 10); }
        else if (i == g_carHot) { Pen ring(WithA(kInk, 0.5f), 1.6f); g.DrawEllipse(&ring, r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8); }
        if (i == 0) {   // au hasard : camembert et point d'interrogation
            DrawCarWheel(g, r);
            SolidBrush mid(g_dark ? Color(255, 22, 32, 58) : Color(255, 245, 248, 252));
            g.FillEllipse(&mid, r.X + 14, r.Y + 14, r.Width - 28, r.Height - 28);
            Text(g, L"?", RectF(r.X, r.Y + 0.5f, r.Width, r.Height), 15, FontStyleBold, kInk);
        } else { SolidBrush b(CarRgb(kSwatches[i - 1].rgb)); g.FillEllipse(&b, r); }
        Pen edge(Color(70, 255, 255, 255), 1.0f);
        g.DrawEllipse(&edge, r);
    }
    std::wstring name;
    wchar_t hex[16] = L"";
    if (show == 0) name = T(L"Au hasard (comme le jeu)", L"Random (like the game)");
    else {
        int c = show > 0 ? kSwatches[show - 1].rgb : g_carColor;
        swprintf_s(hex, L"#%06X", c & 0xFFFFFF);
        name = show > 0 ? (g_fr ? kSwatches[show - 1].fr : kSwatches[show - 1].en) : T(L"Couleur personnalis\u00E9e", L"Custom color");
    }
    float ny = CarSwatchRect(kSwatchN - 1).Y + 76;
    {
        RectF nr(pn.X + 24, ny, pn.Width - 48, 52);
        GraphicsPath np; RoundRect(np, nr, 14);
        SolidBrush nb(TH(card)); g.FillPath(&nb, &np);
        Pen npen(TH(choiceBorder), 1); g.DrawPath(&npen, &np);
        RectF d(nr.X + 14, nr.Y + 13, 26, 26);
        if (show == 0) DrawCarWheel(g, d); else { SolidBrush db(CarRgb(show > 0 ? kSwatches[show - 1].rgb : g_carColor)); GraphicsPath dp; RoundRect(dp, d, 8); g.FillPath(&db, &dp); }
        Text(g, name, RectF(nr.X + 50, nr.Y + 4, nr.Width - 60, 24), 14, FontStyleBold, kInk, StringAlignmentNear);
        if (hex[0]) Text(g, hex, RectF(nr.X + 50, nr.Y + 26, nr.Width - 60, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    {   // Autre couleur... (selecteur de Windows)
        Button &b = g_btn[B_COLOR];
        b.r = RectF(pn.X + 24, ny + 66, pn.Width - 48, 46);
        GraphicsPath p; RoundRect(p, b.r, 14);
        SolidBrush fill(Mix(TH(btn2), TH(btn2Hot), b.hover)); g.FillPath(&fill, &p);
        Pen pen(sel < 0 ? kAcc : TH(fieldBorder), sel < 0 ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
        RectF dot(b.r.X + 14, b.r.Y + 11, 24, 24);
        if (sel < 0) { SolidBrush db(CarRgb(g_carColor)); g.FillEllipse(&db, dot); } else DrawCarWheel(g, dot);
        Text(g, T(L"Autre couleur\u2026", L"Other color\u2026"), RectF(dot.X + 34, b.r.Y, b.r.Width - 60, b.r.Height), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
    }
    float bw = (pn.Width - 60) / 2;
    UiButton(g, RectF(pn.X + 24, pn.Y + pn.Height - 68, bw, 48), T(L"ANNULER", L"CANCEL"), UI_PAGE_CANCEL, false);
    UiButton(g, RectF(pn.X + 36 + bw, pn.Y + pn.Height - 68, bw, 48), T(L"VALIDER", L"OK"), UI_PAGE_OK, true);
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
    return false;
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
static const RectF kSkinCard(264, 88, 440, 656), kSkinPanel(720, 88, 544, 656);
static const RectF kSkinView(284, 112, 400, 520);
static const float kSkinCell = 54, kSkinGap = 8;
static const int kSkinCols = 8;
static int g_lookZone;                                       // zone mise en avant (0 tete, 1 torse, 2 jambes, 3 silhouette)
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
    if (skin.empty()) return "";
    std::string k, top = skin.substr(0, skin.find('|'));   // (apparence complete : "haut|pantalon|...")
    if (top.size() > 64) return "";
    for (char c : top) {
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
static Bitmap *PersoPortrait(const std::string &skin, int W, int H);   // (perso.inc : apparence complete en 3D)
static void DrawSkinAvatar(Graphics &g, RectF c, const std::string &skin, Color col, const std::wstring &name)
{
    Bitmap *th = PersoPortrait(skin, max(8, (int)(c.Width * g_scale + 0.5f)), max(8, (int)(c.Height * g_scale + 0.5f)));
    if (!th) th = SkinThumb(skin, 1, c.Width, c.Height);
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
static RectF SkinCellRect(int i) { return RectF(kSkinPanel.X + 24 + (i % kSkinCols) * (kSkinCell + kSkinGap), kSkinPanel.Y + 62 + (i / kSkinCols) * (kSkinCell + kSkinGap), kSkinCell, kSkinCell); }
static RectF SkinArrowRect(int side) { return RectF(side < 0 ? kSkinCard.X + 20 : kSkinCard.X + kSkinCard.Width - 64, kSkinCard.Y + kSkinCard.Height - 106, 44, 44); }
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

static std::string MyShirt();
static std::wstring SkinLabel(const std::string &skin);
// ---------------------------------------------------------------- tenues offertes (credits : onglet CREDITS)
// Pack de Dom (08/10/2026, demande de JD) : 155 tenues (84 hauts, 27 pantalons, 44 visages) sur le patron des vetements
// des PNJ. Telecharge une fois (8,7 Mo) depuis le depot (assets/tenues), a part des mises a jour du mod, dans
// MWCoop\tenues\<auteur>\ : lu par le mod (Sync\Tenues.cs) et par l'apercu 3D d'ici. Noms a part (dom_haut_NN...) :
// jamais a la place des textures du jeu. Pack absent chez un joueur : la tenue d'origine.
struct TenuePack { const char *id; const char *ver; const wchar_t *zip; };
// (dom 2, 08/10 : sans dom_haut_77 ni dom_haut_83, retires a la demande de JD ; pas de renumerotation)
static const TenuePack kTenuePacks[] = { { "dom", "2", L"tenues-dom-2.zip" } };
static std::atomic<int> g_tenuesState(0);   // 0 rien, 1 en cours, 2 recu (listes a completer), 3 echec
static std::wstring TenuesDir() { return g_gameDir.empty() ? L"" : g_gameDir + L"MWCoop\\tenues\\"; }
static bool IsTenue(const std::string &n) { return n.find("_haut_") != std::string::npos || n.find("_pantalon_") != std::string::npos || n.find("_visage_") != std::string::npos; }
// "dom_haut_05" -> "Dom 5"
static std::wstring TenueLabel(const std::string &n)
{
    size_t a = n.find('_'), b = n.rfind('_');
    if (a == std::string::npos || a == 0 || b == a) return Widen(n);
    std::wstring who = Widen(n.substr(0, a));
    who[0] = towupper(who[0]);
    return who + L" " + std::to_wstring(atoi(n.c_str() + b + 1));
}
static std::wstring TenueFile(const std::string &n) { size_t a = n.find('_'); return a == std::string::npos || TenuesDir().empty() ? L"" : TenuesDir() + Widen(n.substr(0, a)) + L"\\" + Widen(n) + L".jpg"; }
static bool TenuePackHere(const TenuePack &p)
{
    std::vector<unsigned char> d;
    if (!ReadAll(TenuesDir() + Widen(p.id) + L"\\version.txt", d)) return false;
    return std::string(d.begin(), d.end()).find(p.ver) == 0;
}
static DWORD WINAPI TenuesThread(void *)
{
    bool any = false, fail = false;
    for (const TenuePack &p : kTenuePacks) {
        if (TenuePackHere(p)) continue;
        std::wstring url = L"https://raw.githubusercontent.com/" + g_repo + L"/main/assets/tenues/" + p.zip;
        wchar_t tmp[MAX_PATH] = L"";
        GetTempPathW(MAX_PATH, tmp);
        std::wstring zf = std::wstring(tmp) + L"mwcoop-" + p.zip;
        DWORD st = 0;
        std::string why;
        if (!HttpGet(url, NULL, zf, false, &st) || st != 200) { LaunchLog("tenues offertes : %s : telechargement impossible (HTTP %lu)", p.id, st); fail = true; DeleteFileW(zf.c_str()); continue; }
        SHCreateDirectoryExW(NULL, TenuesDir().c_str(), NULL);
        {   // version precedente : ses fichiers retires d'abord (une tenue enlevee du pack ne doit pas rester)
            std::wstring pd = TenuesDir() + Widen(p.id) + L"\\";
            WIN32_FIND_DATAW fd;
            HANDLE h = FindFirstFileW((pd + L"*").c_str(), &fd);
            int n = 0;
            if (h != INVALID_HANDLE_VALUE) {
                do { if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && DeleteFileW((pd + fd.cFileName).c_str())) n++; } while (FindNextFileW(h, &fd));
                FindClose(h);
            }
            if (n) LaunchLog("tenues offertes : %s : %d fichier(s) de la version precedente retires", p.id, n);
        }
        if (!Unzip(zf, TenuesDir(), &why)) { LaunchLog("tenues offertes : %s : %s", p.id, why.c_str()); fail = true; }
        else { LaunchLog("tenues offertes : pack %s (version %s) recu", p.id, p.ver); any = true; }
        DeleteFileW(zf.c_str());
    }
    g_tenuesState = fail ? 3 : any ? 2 : 0;
    return 0;
}
static void TenuesStart()
{
    if (g_gameDir.empty() || g_tenuesState == 1 || !g_testSalon.empty()) return;
    bool need = false;
    for (const TenuePack &p : kTenuePacks) need |= !TenuePackHere(p);
    if (!need) return;
    g_tenuesState = 1;
    HANDLE t = CreateThread(NULL, 0, TenuesThread, NULL, 0, NULL);
    if (t) CloseHandle(t); else g_tenuesState = 0;
}
// Tenues offertes presentes (noms, tries) : hauts, pantalons, visages.
static std::vector<std::string> g_tenueNames[3];
static std::vector<std::string> TenuesFind(const wchar_t *pattern)
{
    std::vector<std::string> names;
    if (TenuesDir().empty()) return names;
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((TenuesDir() + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return names;
    do {
        if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || fd.cFileName[0] == L'.') continue;
        WIN32_FIND_DATAW f2;
        HANDLE h2 = FindFirstFileW((TenuesDir() + fd.cFileName + L"\\" + pattern).c_str(), &f2);
        if (h2 == INVALID_HANDLE_VALUE) continue;
        do { std::wstring n = f2.cFileName; names.push_back(Narrow(n.substr(0, n.size() - 4), CP_UTF8)); } while (FindNextFileW(h2, &f2));
        FindClose(h2);
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    std::sort(names.begin(), names.end());
    return names;
}
// Hauts offerts dans le choix de tenue, apres ceux du jeu ("Tenue Dom 5") ; pantalons et visages gardes pour PersoChoices
// (sans attendre que le jeu refasse son export : retour de JD, 08/10, 14 visages seulement juste apres le telechargement).
static void TenuesAddShirts()
{
    g_tenueNames[0] = TenuesFind(L"*_haut_*.jpg");
    g_tenueNames[1] = TenuesFind(L"*_pantalon_*.jpg");
    g_tenueNames[2] = TenuesFind(L"*_visage_*.jpg");
    Opt *ap = NULL;
    for (auto &o : g_opts) if (!strcmp(o.key, "Apparence")) ap = &o;
    if (!ap) return;
    for (auto &n : g_tenueNames[0]) {
        if (std::find(ap->svals.begin(), ap->svals.end(), n) != ap->svals.end()) continue;
        std::wstring lab = TenueLabel(n);
        ap->vals.push_back((int)ap->svals.size());
        ap->svals.push_back(n);
        ap->labFr.push_back(L"Tenue " + lab);
        ap->labEn.push_back(L"Outfit " + lab);
    }
}

#include "perso.inc"

// Silhouette neutre (pas d'image) : tete et buste.
static void DrawSkinGhost(Graphics &g, float cx, float top, float k)
{
    SolidBrush b(WithA(kGrey, 0.22f));
    g.FillEllipse(&b, cx - 22 * k, top, 44 * k, 48 * k);
    GraphicsPath bp;
    RoundRect(bp, RectF(cx - 42 * k, top + 56 * k, 84 * k, 96 * k), 24 * k);
    g.FillPath(&b, &bp);
}

// Repere d'une zone du corps sur l'apercu : point, trait, pastille (cliquable : met la zone en avant a droite).
static RectF LookPinRect(int z)
{
    static const float fy[4] = { 0.15f, 0.38f, 0.68f, 0 };
    if (z == 3) return RectF(kSkinCard.X + 20, kSkinCard.Y + 20, 120, 36);
    return RectF(kSkinCard.X + kSkinCard.Width - 128, kSkinView.Y + kSkinView.Height * fy[z] - 18, 108, 36);
}

// Page TENUE : apercu a gauche (3D, ou la vue de la tenue), parties a droite (ou la galerie des hauts).
static void DrawSkins(Graphics &g)
{
    SkinsCheck();
    const Opt *ap = SkinOpt();
    if (!ap) return;
    Glass(kSkinCard);
    Glass(kSkinPanel);
    int n = (int)ap->svals.size(), sel = OptGet(*ap);
    const std::string &skin = ap->svals[sel];
    const std::vector<std::wstring> &lab = g_fr ? ap->labFr : ap->labEn;
    RectF view = kSkinView;
    PersoCheck();
    bool perso = g_perso.state == 1;
    {   // halo au sol
        GraphicsPath hp; hp.AddEllipse(view.X + 40, view.Y + view.Height - 46, view.Width - 80, 44.0f);
        PathGradientBrush hb(&hp);
        hb.SetCenterColor(WithA(kAcc, g_dark ? 0.32f : 0.26f));
        Color edge(0, 0, 0, 0); int one = 1; hb.SetSurroundColors(&edge, &one);
        g.FillPath(&hb, &hp);
    }
    float cx = view.X + view.Width / 2;
    float k = min((GetTickCount() - g_skinChangeT) / 180.0f, 1.0f);
    k = 1 - (1 - k) * (1 - k);
    Bitmap *strip = perso ? NULL : SkinStrip(skin), *portrait = perso || strip ? NULL : SkinPortrait(skin);
    if (perso) {
        RectF dst(view.X, view.Y, view.Width, view.Height - 16);
        if (Bitmap *pb = PersoImage(dst, g_skinYaw * 6.2831853f / 16)) DrawPixels(g, pb, dst);
    } else if (strip) {
        float fw = strip->GetWidth() / 16.0f, fh = (float)strip->GetHeight();
        int fr = (int)floorf(g_skinYaw + 0.5f) & 15;
        float h = view.Height - 16, w = h * fw / fh;
        RectF dst(cx - w / 2 + (1 - k) * 22 * g_skinChangeDir, view.Y, w, h);
        if (k < 1) {
            ColorMatrix cm = { { { 1, 0, 0, 0, 0 }, { 0, 1, 0, 0, 0 }, { 0, 0, 1, 0, 0 }, { 0, 0, 0, k, 0 }, { 0, 0, 0, 0, 1 } } };
            ImageAttributes ia;
            ia.SetColorMatrix(&cm);
            g.DrawImage(strip, dst, fr * fw, 0, fw, fh, UnitPixel, &ia);
        } else {
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
    } else if (portrait) g.DrawImage(portrait, RectF(cx - 80, view.Y + 120, 160, 160));
    else {
        DrawSkinGhost(g, cx, view.Y + 90, 1.4f);
        const wchar_t *msg = g_skinState == 1 ? T(L"Pas d'aper\u00E7u pour cette tenue.", L"No preview for this outfit.")
                                              : T(L"Les aper\u00E7us appara\u00EEssent apr\u00E8s une premi\u00E8re partie (~30 s en jeu) avec cette version.",
                                                  L"Previews appear after a first game (~30 s in game) with this version.");
        Para(g, msg, RectF(view.X + 30, view.Y + 330, view.Width - 60, 60), 13, kGrey, StringAlignmentNear);
    }
    // reperes des zones (apparence complete)
    if (perso) {
        static const wchar_t *zf[4] = { L"T\u00EAte", L"Torse", L"Jambes", L"Silhouette" }, *ze[4] = { L"Head", L"Torso", L"Legs", L"Build" };
        static const float dx[3] = { 18, 34, 22 };
        for (int z = 0; z < 4; z++) {
            RectF r = LookPinRect(z);
            bool on = g_lookZone == z, hot = g_uiHot == UI_LOOKPIN + z;
            if (z < 3) {
                float y = r.Y + r.Height / 2, x0 = cx + dx[z];
                Pen ln(Color(110, 255, 255, 255), 1.5f); g.DrawLine(&ln, x0, y, r.X, y);
                SolidBrush halo(WithA(kAcc, 0.3f)); g.FillEllipse(&halo, x0 - 9, y - 9, 18.0f, 18.0f);
                SolidBrush dot(kAcc); g.FillEllipse(&dot, x0 - 5, y - 5, 10.0f, 10.0f);
            }
            GraphicsPath p; RoundRect(p, r, r.Height / 2);
            SolidBrush b(on ? kAcc : hot ? (g_dark ? Color(220, 24, 38, 72) : Color(240, 255, 255, 255)) : (g_dark ? Color(180, 6, 12, 30) : Color(215, 255, 255, 255)));
            g.FillPath(&b, &p);
            if (!on) { Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p); }
            Text(g, g_fr ? zf[z] : ze[z], r, 13, FontStyleBold, on ? kOnAcc : kInk);
            HotZone(r, UI_LOOKPIN + z);
        }
    }
    // dessous : tourner, aleatoire, comme avant
    {
        float by = kSkinCard.Y + kSkinCard.Height - 106;
        if (perso || strip) {
            for (int s = 0; s < 2; s++) {
                RectF r(s ? kSkinCard.X + kSkinCard.Width - 64 : kSkinCard.X + 20, by, 44, 44);
                int id = UI_LOOKROT + s;
                GraphicsPath p; RoundRect(p, r, 14);
                SolidBrush b(g_uiHot == id ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
                Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p);
                Icon(g, s ? IC_CHEVR : IC_CHEVL, r.X + 22, r.Y + 22, 18, kInk, 2.2f);
                HotZone(r, id);
            }
            Text(g, perso || strip ? T(L"Glisse pour tourner", L"Drag to rotate") : L"", RectF(kSkinCard.X + 70, by, kSkinCard.Width - 140, 44), 12.5f, FontStyleRegular, kGrey);
        }
        float bw = (kSkinCard.Width - 50) / 2;
        UiButton(g, RectF(kSkinCard.X + 20, by + 56, bw, 44), T(L"Al\u00E9atoire", L"Random"), UI_LOOKRAND, false, IC_SHUFFLE);
        UiButton(g, RectF(kSkinCard.X + 30 + bw, by + 56, bw, 44), T(L"Comme avant", L"As before"), UI_LOOKRESET, false, IC_UNDO);
    }

    // --- a droite
    RectF pn = kSkinPanel;
    if (perso) DrawPersoRows(g);
    if (perso && PersoStale()) {   // export d'une ancienne version (corps des PNJ dans leur pose : la grand-mere assise...)
        RectF r(pn.X + 24, pn.Y + 532, pn.Width - 48, 0);
        r.Y = PersoRowRect(kPersoRowN - 1).Y + 58; r.Height = pn.Y + pn.Height - 80 - r.Y;
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush b(WithA(kWarn, 0.14f)); g.FillPath(&b, &p);
        Pen pen(WithA(kWarn, 0.5f), 1); g.DrawPath(&pen, &p);
        Icon(g, IC_ALERT, r.X + 22, r.Y + r.Height / 2, 18, kWarn, 2.0f);
        Para(g, T(L"Aperçu fait par une ancienne version : lance une partie (solo suffit) et reste ~30 s en jeu, il sera refait (corps debout).",
                  L"Preview made by an older version: start a game (solo is fine) and stay ~30 s in game, it will be redone (standing bodies)."),
             RectF(r.X + 42, r.Y + 6, r.Width - 54, r.Height - 8), 12, kInk, StringAlignmentCenter);
    }
    if (!perso) {   // (pas d'apparence complete : la galerie des hauts)
        Title(g, T(L"Hauts", L"Tops"), RectF(pn.X + 24, pn.Y + 20, 200, 26), 17, kInk);
        std::wstring shown = g_skinHot >= 0 && g_skinHot < n ? lab[g_skinHot] : lab[sel];
        Text(g, shown, RectF(pn.X + 200, pn.Y + 20, pn.Width - 224, 26), 14, FontStyleBold, g_skinHot >= 0 ? kAcc : kInk, StringAlignmentFar);
        for (int i = 0; i < n; i++) {
            RectF r = SkinCellRect(i);
            GraphicsPath cp; RoundRect(cp, r, 12);
            bool on = i == sel, hot = i == g_skinHot;
            SolidBrush cb(on ? WithA(kAcc, 0.16f) : hot ? TH(cardSel) : TH(card)); g.FillPath(&cb, &cp);
            if (Bitmap *th = SkinThumb(ap->svals[i], 0, r.Width, r.Height)) DrawPixels(g, th, r);
            else {
                const std::string &sv = ap->svals[i];
                std::wstring t = !sv.compare(0, 10, "char_shirt") ? Widen(sv.substr(10)) : lab[i].substr(0, 2);
                Text(g, t, r, 14, FontStyleBold, on ? kAcc : kGrey);
            }
            Pen edge(on ? kAcc : hot ? WithA(kAcc, 0.5f) : TH(choiceBorder), on ? 2.0f : 1.0f); g.DrawPath(&edge, &cp);
        }
        RectF nr(pn.X + 24, SkinCellRect(n - 1).Y + kSkinCell + 30, pn.Width - 48, 96);
        GraphicsPath np; RoundRect(np, nr, 16);
        SolidBrush nb(WithA(kAcc, 0.12f)); g.FillPath(&nb, &np);
        Pen npen(WithA(kAcc, 0.45f), 1); g.DrawPath(&npen, &np);
        Icon(g, IC_SPARK, nr.X + 28, nr.Y + 30, 20, kAcc, 1.9f);
        Text(g, T(L"Apparence compl\u00E8te", L"Full look"), RectF(nr.X + 52, nr.Y + 14, nr.Width - 70, 24), 14.5f, FontStyleBold, kInk, StringAlignmentNear);
        Para(g, T(L"Corpulence, pantalon, visage, chapeau, lunettes, cheveux : lance une partie (solo suffit) et reste ~30 s en jeu avec cette version, ils appara\u00EEtront ici.",
                  L"Build, pants, face, hat, glasses, hair: start a game (solo is fine) and stay ~30 s in game with this version, they will show up here."),
             RectF(nr.X + 52, nr.Y + 40, nr.Width - 70, 52), 12.5f, kGrey);
    }
    float bw = (pn.Width - 60) / 2;
    UiButton(g, RectF(pn.X + 24, pn.Y + pn.Height - 68, bw, 48), T(L"ANNULER", L"CANCEL"), UI_PAGE_CANCEL, false);
    UiButton(g, RectF(pn.X + 36 + bw, pn.Y + pn.Height - 68, bw, 48), T(L"VALIDER", L"OK"), UI_PAGE_OK, true);
}

// Aleatoire : une valeur au hasard pour chaque partie (apparence complete), sinon un haut au hasard.
static void LookRandom()
{
    srand(GetTickCount());
    if (g_perso.state == 1) {
        for (int f = 0; f < PF_COUNT; f++) {
            std::vector<std::string> v = PersoChoices(f);
            if (v.empty()) continue;
            if (f == PF_SHIRT) { const Opt *ap = SkinOpt(); if (ap) OptSet(*ap, rand() % (int)v.size()); }
            else PersoSet(f, v[rand() % v.size()]);
        }
        g_skinChangeT = GetTickCount();
        g_skinAnnounce = true;
    } else SkinSelect(rand() % max(1, SkinCount()), 1);
}
// Comme avant : les valeurs a l'arrivee sur la page (sans la quitter).
static void LookReset()
{
    const Opt *ap = SkinOpt();
    if (ap && g_snapSkin >= 0) OptSet(*ap, g_snapSkin);
    PersoSnapshot(true);
    g_skinAnnounce = true;
    g_skinChangeT = GetTickCount();
}

// Clic dans la page : une ligne (fleches), un portrait, ou l'apercu (debut du glisser).
static bool SkinMouseDown(float x, float y)
{
    int pr = PersoRowAt(x, y);   // apercu 3D : fleches des lignes
    if (pr >= 0) { PersoStep(pr / 2, pr % 2 ? 1 : -1); g_skinAnnounce = true; return true; }
    if (g_perso.state != 1) {
        int c = SkinCellAt(x, y);
        if (c >= 0) { const Opt *ap = SkinOpt(); SkinSelect(c, ap && c < OptGet(*ap) ? -1 : 1); return true; }
    }
    if (kSkinView.Contains(x, y)) {
        g_skinDrag = true;
        g_skinDragX = x;
        SetCapture(g_wnd);
        return true;
    }
    return false;
}
static void SkinDragTo(float x)
{
    g_skinYaw = fmodf(g_skinYaw - (x - g_skinDragX) / 14.0f, 16.0f);   // une vue tous les 14 px
    if (g_skinYaw < 0) g_skinYaw += 16;
    g_skinDragX = x;
}

// ---------------------------------------------------------------- scene animee (ui.inc)
// Tout se calcule a partir de g_sceneT, sans etat : /capture ... /temps t donne l'image a l'instant t.
static float Rnd01(int i, int k)
{
    unsigned h = (unsigned)i * 2654435761u ^ (unsigned)(k + 1) * 2246822519u;
    h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
    return (h & 0xFFFFFF) / 16777216.0f;
}

// ---------------------------------------------------------------- guide "Jouer par Steam"
// Ouvert au clic sur STEAM (sauf "ne plus afficher" : [Lanceur] GuideSteam=0) et sur l'encart de l'option de
// lancement : comment inviter, et l'option de lancement Steam  "<MWCoop.exe>" %command%  a copier (sans elle, une
// invitation acceptee MWCoop ferme lance le jeu sans le mod).
static const RectF kGuideCard(290, 116, 700, 560);
static const RectF kGuideCopy(330, 552, 300, 50), kGuideOk(646, 552, 304, 50), kGuideNoMore(330, 618, 380, 26), kGuideClose(926, 132, 44, 44);
static std::wstring SteamLaunchLine() { return L"\"" + g_self + L"\" %command%"; }
static bool CopyText(const std::wstring &t)
{
    if (!OpenClipboard(g_wnd)) return false;
    EmptyClipboard();
    size_t bytes = (t.size() + 1) * sizeof(wchar_t);
    HGLOBAL hg = GlobalAlloc(GMEM_MOVEABLE, bytes);
    bool ok = false;
    if (hg) {
        memcpy(GlobalLock(hg), t.c_str(), bytes);
        GlobalUnlock(hg);
        ok = SetClipboardData(CF_UNICODETEXT, hg) != NULL;
        if (!ok) GlobalFree(hg);
    }
    CloseClipboard();
    return ok;
}
// ---------------------------------------------------------------- invitation au Discord
// Demande de JD (08/10) : bouton Discord dans la barre de gauche, et au premier lancement (apres la visite guidee) une
// fenetre propose de rejoindre le serveur ([Lanceur] DiscordPropose=1 ensuite : une seule fois). Meme mecanique que le
// guide Steam (g_guide), autre contenu (g_guideKind 1).
static const wchar_t *kDiscordUrl = L"https://discord.gg/H7NXpWHwgY";
static const RectF kDiscCard(390, 166, 500, 476), kDiscJoin(430, 486, 420, 54), kDiscLater(430, 552, 420, 44), kDiscClose(836, 182, 40, 40);
static int g_guideKind;   // 0 guide Steam, 1 invitation au Discord
static void DiscordAskOpen() { g_guide = true; g_guideKind = 1; g_guideHot = 0; g_focus = -1; }
static void DiscordAskMaybe()
{
    if (g_guide || g_state != ST_IDLE || GetPrivateProfileIntW(L"Lanceur", L"DiscordPropose", 0, g_iniLauncher.c_str()) != 0) return;
    DiscordAskOpen();
}
static int DiscordHit(float x, float y)
{
    if (kDiscJoin.Contains(x, y)) return 1;
    if (kDiscLater.Contains(x, y)) return 2;
    if (kDiscClose.Contains(x, y)) return 4;
    return kDiscCard.Contains(x, y) ? 0 : -1;
}
static void DrawDiscordAsk(Graphics &g)
{
    SolidBrush db(Color(g_dark ? 150 : 110, 2, 6, 18));
    g.FillPath(&db, CardPath());
    RectF c = kDiscCard;
    GlassLive(g, c, 24);
    {   // fermer
        RectF r = kDiscClose;
        GraphicsPath p; RoundRect(p, r, 13);
        SolidBrush b(g_guideHot == 4 ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
        Icon(g, IC_CLOSE, r.X + 20, r.Y + 20, 17, kInk, 2.2f);
    }
    RectF logo(c.X + c.Width / 2 - 44, c.Y + 44, 88, 88);
    SolidBrush lb(kDiscordBlue); g.FillEllipse(&lb, logo);
    Icon(g, IC_DISCORD, logo.X + 44, logo.Y + 46, 50, Color(255, 255, 255, 255));
    Title(g, T(L"Rejoins-nous sur Discord !", L"Join us on Discord!"), RectF(c.X, c.Y + 150, c.Width, 36), 25, kInk, StringAlignmentCenter);
    Para(g, T(L"De l'aide, les bugs à signaler, les nouveautés en avant-première et des joueurs pour faire équipe. Salons en anglais, plus un salon en français.",
              L"Help, bug reports, early news and players to team up with. English channels, plus a French one."),
         RectF(c.X + 44, c.Y + 200, c.Width - 88, 70), 14, kGrey, StringAlignmentNear);
    Para(g, T(L"Pour un souci en jeu : envoie tes journaux (bouton Journaux, en haut) dans #bug-reports.", L"Trouble in game? Send your logs (Logs button, top right) in #bug-reports."),
         RectF(c.X + 44, c.Y + 270, c.Width - 88, 44), 12.5f, kGrey, StringAlignmentNear);
    {
        RectF r = kDiscJoin;
        GraphicsPath p; RoundRect(p, r, 16);
        SolidBrush fb(kDiscordBlue); g.FillPath(&fb, &p);
        if (g_guideHot == 1) { SolidBrush hb(Color(50, 255, 255, 255)); g.FillPath(&hb, &p); }
        Icon(g, IC_DISCORD, r.X + 120, r.Y + r.Height / 2, 24, Color(255, 255, 255, 255));
        Text(g, T(L"REJOINDRE LE DISCORD", L"JOIN THE DISCORD"), RectF(r.X + 140, r.Y, r.Width - 150, r.Height), 15, FontStyleBold, Color(255, 255, 255, 255), StringAlignmentNear);
    }
    {
        RectF r = kDiscLater;
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush fb(g_guideHot == 2 ? TH(btn2Hot) : TH(btn2)); g.FillPath(&fb, &p);
        Text(g, T(L"Plus tard", L"Later"), r, 14, FontStyleBold, kInk);
    }
    Text(g, T(L"Toujours à portée : le bouton Discord en bas à gauche.", L"Always one click away: the Discord button, bottom left."), RectF(c.X, c.Y + c.Height - 32, c.Width, 20), 12, FontStyleRegular, kGrey);
}

static void SteamGuideOpen() { g_guide = true; g_guideKind = 0; g_guideHot = 0; g_guideNoMore = false; g_guideCopiedT = 0; g_focus = -1; }
static void SteamGuideClose()
{
    g_guide = false;
    g_guideHot = 0;
    if (g_guideKind == 1) WritePrivateProfileStringW(L"Lanceur", L"DiscordPropose", L"1", g_iniLauncher.c_str());   // (proposee : plus jamais d'elle-meme)
    else if (g_guideNoMore) WritePrivateProfileStringW(L"Lanceur", L"GuideSteam", L"0", g_iniLauncher.c_str());
}
static int SteamGuideHit(float x, float y)
{
    if (g_guideKind == 1) return DiscordHit(x, y);
    if (kGuideCopy.Contains(x, y)) return 1;
    if (kGuideOk.Contains(x, y)) return 2;
    if (kGuideNoMore.Contains(x, y)) return 3;
    if (kGuideClose.Contains(x, y)) return 4;
    return kGuideCard.Contains(x, y) ? 0 : -1;
}
static void SteamGuideClick(float x, float y)
{
    int h = SteamGuideHit(x, y);
    if (g_guideKind == 1) {   // invitation au Discord
        if (h == 1) ShellExecuteW(g_wnd, L"open", kDiscordUrl, NULL, NULL, SW_SHOWNORMAL);
        if (h == 1 || h == 2 || h == 4 || h < 0) SteamGuideClose();
        return;
    }
    if (h == 1) {
        if (CopyText(SteamLaunchLine())) {
            g_guideCopiedT = GetTickCount() | 1;
            SetStatus(K_OK, T(L"Option de lancement copi\u00E9e : colle-la dans Steam", L"Launch option copied: paste it in Steam"));
        }
    } else if (h == 3) g_guideNoMore = !g_guideNoMore;
    else if (h == 2 || h == 4 || h < 0) SteamGuideClose();
}
static void DrawSteamGuide(Graphics &g)
{
    if (!g_guide) return;
    if (g_guideKind == 1) { DrawDiscordAsk(g); return; }
    SolidBrush db(Color(g_dark ? 150 : 110, 2, 6, 18));
    g.FillPath(&db, CardPath());
    RectF c = kGuideCard;
    GlassLive(g, c, 24);
    Icon(g, IC_STEAM, c.X + 56, c.Y + 45, 32, kAcc);
    Title(g, T(L"Jouer par Steam", L"Playing through Steam"), RectF(c.X + 82, c.Y + 28, 500, 34), 24, kInk);
    {   // fermer
        RectF r = kGuideClose;
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush b(g_guideHot == 4 ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
        Icon(g, IC_CLOSE, r.X + 22, r.Y + 22, 18, kInk, 2.2f);
    }
    const wchar_t *steps[3] = {
        T(L"H\u00C9BERGER ouvre un salon Steam r\u00E9serv\u00E9 \u00E0 tes amis : invite-les depuis le bouton \u00AB Inviter des amis \u00BB du salon.",
          L"HOST opens a Steam lobby for your friends only: invite them with the lobby's \"Invite friends\" button."),
        T(L"Tes amis acceptent l'invitation dans Steam (ou cliquent REJOINDRE dans MWCoop, mode STEAM) ; LANCER d\u00E9marre le jeu de chacun.",
          L"Your friends accept the invite in Steam (or click JOIN in MWCoop, STEAM mode); START launches everyone's game."),
        T(L"Pour qu'une invitation accept\u00E9e MWCoop ferm\u00E9 ouvre MWCoop (et pas le jeu sans le mod), chacun ajoute cette option de lancement :",
          L"So that an invite accepted with MWCoop closed opens MWCoop (not the game without the mod), everyone adds this launch option:") };
    float y = c.Y + 82;
    for (int i = 0; i < 3; i++) {
        RectF dot(c.X + 40, y, 28, 28);
        SolidBrush ob(kAcc);
        g.FillEllipse(&ob, dot);
        Text(g, std::to_wstring(i + 1), dot, 14, FontStyleBold, kOnAcc);
        Para(g, steps[i], RectF(c.X + 82, y + 2, c.Width - 122, 44), 14, kInk);
        y += 54;
    }
    Para(g, T(L"Steam > Biblioth\u00E8que > clic droit sur My Winter Car > Propri\u00E9t\u00E9s > G\u00E9n\u00E9ral > Options de lancement",
              L"Steam > Library > right-click My Winter Car > Properties > General > Launch options"),
         RectF(c.X + 82, y - 4, c.Width - 122, 40), 13, kAcc);
    RectF code(c.X + 40, y + 38, c.Width - 80, 54);
    {
        GraphicsPath kp;
        RoundRect(kp, code, 14);
        SolidBrush kb(TH(field));
        g.FillPath(&kb, &kp);
        Pen kpen(TH(fieldBorder), 1);
        g.DrawPath(&kpen, &kp);
        FontFamily mono(L"Consolas");
        Font mf(&mono, 14, FontStyleRegular, UnitPixel);
        StringFormat sf;
        sf.SetLineAlignment(StringAlignmentCenter);
        sf.SetTrimming(StringTrimmingEllipsisPath);
        SolidBrush tb(kInk);
        std::wstring line = SteamLaunchLine();
        g.DrawString(line.c_str(), -1, &mf, RectF(code.X + 16, code.Y + 4, code.Width - 32, code.Height - 8), &sf, &tb);
    }
    Para(g, T(L"Le bouton JOUER de Steam ouvrira alors MWCoop. Pour retrouver le jeu sans le mod, efface la ligne.",
              L"Steam's PLAY button will then open MWCoop. To get the game without the mod back, clear the line."),
         RectF(c.X + 40, code.Y + code.Height + 10, c.Width - 80, 40), 12.5f, kGrey);
    bool copied = g_guideCopiedT && GetTickCount() - g_guideCopiedT < 4000;
    {
        RectF r = kGuideCopy;
        GraphicsPath p; RoundRect(p, r, 16);
        SolidBrush lg(kAcc); g.FillPath(&lg, &p);
        if (g_guideHot == 1) { SolidBrush hb(Color(55, 255, 255, 255)); g.FillPath(&hb, &p); }
        Text(g, copied ? T(L"LIGNE COPI\u00C9E \u2713", L"LINE COPIED \u2713") : T(L"COPIER LA LIGNE", L"COPY THE LINE"), r, 15, FontStyleBold, kOnAcc);
    }
    {
        RectF r = kGuideOk;
        GraphicsPath p; RoundRect(p, r, 16);
        SolidBrush fb(g_guideHot == 2 ? TH(btn2Hot) : TH(btn2));
        g.FillPath(&fb, &p);
        Pen pp(TH(fieldBorder), 1);
        g.DrawPath(&pp, &p);
        Text(g, T(L"COMPRIS", L"GOT IT"), r, 15, FontStyleBold, kInk);
    }
    {   // ne plus afficher (au clic sur STEAM)
        RectF box(kGuideNoMore.X, kGuideNoMore.Y + 3, 20, 20);
        GraphicsPath bp; RoundRect(bp, box, 6);
        if (g_guideNoMore) { SolidBrush on(kAcc); g.FillPath(&on, &bp); Icon(g, IC_CHECK, box.X + 10, box.Y + 10, 15, kOnAcc, 2.6f); }
        else { Pen bpen(g_guideHot == 3 ? kAcc : TH(fieldBorder), 1.5f); g.DrawPath(&bpen, &bp); }
        Text(g, T(L"Ne plus afficher en choisissant STEAM", L"Don't show again when choosing STEAM"), RectF(box.X + 30, kGuideNoMore.Y, 340, 26), 13,
             FontStyleRegular, g_guideHot == 3 ? kAcc : kGrey, StringAlignmentNear);
    }
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
    // "My Winter Car" dans le chemin : le prechargeur de MSCLoader reconnait le jeu a son dossier (sinon il se croit
    // dans My Summer Car). Avant 0.31.3 : %LOCALAPPDATA%\MWCoop\jeu (laisse en place, plus utilise).
    return LocalDir() + L"My Winter Car\\";
}

// Avec le MSCLoader de MWCoop actif : sa copie de lancement (pare-feu, jeu deja lance).
static std::wstring GameExe()
{
    std::wstring m = MirrorDir();
    if (GetPrivateProfileIntW(L"Lanceur", L"SansCopie", 0, g_iniLauncher.c_str()) != 0) m.clear();   // (depart du dossier du jeu)
    if (g_mscOn && MscOwn() && MscOwnInstalled()) m = MscCopyDir();
    return (m.empty() ? g_gameDir : m) + L"mywintercar.exe";
}

static bool SameFile(const std::wstring &a, const std::wstring &b)
{
    WIN32_FILE_ATTRIBUTE_DATA x, y;
    if (!GetFileAttributesExW(a.c_str(), GetFileExInfoStandard, &x) || !GetFileAttributesExW(b.c_str(), GetFileExInfoStandard, &y)) return false;
    return x.nFileSizeLow == y.nFileSizeLow && x.nFileSizeHigh == y.nFileSizeHigh && CompareFileTime(&x.ftLastWriteTime, &y.ftLastWriteTime) == 0;
}

// Prepare la copie (fichiers recopies s'ils ont change, jonctions creees si absentes). Faux si impossible.
// Tout ce qui est pose a cote du jeu suit : l'exe, toutes les DLL et tous les .ini (MSCLoader : winhttp.dll de
// UnityDoorstop et doorstop_config.ini ; BepInEx...), et chaque dossier en jonction (Mods, Updates de MSCLoader...).
// Une DLL ou un .ini retire du jeu (MSCLoader desinstalle) est retire de la copie.
static bool CopyKind(const std::wstring &n)
{
    size_t dot = n.find_last_of(L'.');
    std::wstring ext = dot == std::wstring::npos ? L"" : n.substr(dot);
    for (auto &c : ext) c = towlower(c);
    return ext == L".dll" || ext == L".ini" || !_wcsicmp(n.c_str(), L"mywintercar.exe") || !_wcsicmp(n.c_str(), L"changelog.txt");
}
// Deux chemins menent-ils au meme dossier (jonction suivie) ?
static std::wstring FinalDir(const std::wstring &p)
{
    HANDLE h = CreateFileW(p.c_str(), 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, NULL);
    if (h == INVALID_HANDLE_VALUE) return L"";
    wchar_t buf[1024];
    DWORD n = GetFinalPathNameByHandleW(h, buf, 1024, 0);
    CloseHandle(h);
    return n && n < 1024 ? std::wstring(buf, n) : L"";
}
static bool SameDirTarget(const std::wstring &a, const std::wstring &b)
{
    std::wstring x = FinalDir(a), y = FinalDir(b);
    return !x.empty() && !_wcsicmp(x.c_str(), y.c_str());
}
// MSCLoader coupe (onglet MODS, ou salon sans mods) : son winhttp.dll retire de la copie de lancement (jamais du jeu :
// rien si la copie est le jeu). -mscloader-disable ne suffit pas : lance par l'explorateur (lanceur demarre par Steam),
// le jeu n'a pas de ligne de commande -- MSCLoader et ses mods se chargeaient, interrupteur coupe (JD, 08/10).
static void MscOffInCopy(const std::wstring &copy)
{
    std::wstring a = FinalDir(copy), g = FinalDir(g_gameDir);
    if (a.empty() || g.empty() || !_wcsicmp(a.c_str(), g.c_str())) return;
    std::wstring dll = copy + L"winhttp.dll";
    SetFileAttributesW(dll.c_str(), FILE_ATTRIBUTE_NORMAL);
    if (DeleteFileW(dll.c_str())) TestLog("MSCLoader coupe : winhttp.dll retire de la copie de lancement");
    else if (GetLastError() != ERROR_FILE_NOT_FOUND) TestLog("MSCLoader coupe : winhttp.dll de la copie impossible a retirer (erreur %lu)", GetLastError());
}
static bool DirEmptyW(const std::wstring &d)
{
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((d + L"\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return true;
    bool empty = true;
    do { if (wcscmp(fd.cFileName, L".") && wcscmp(fd.cFileName, L"..")) { empty = false; break; } } while (FindNextFileW(h, &fd));
    FindClose(h);
    return empty;
}

static bool PrepareMirror(const std::wstring &m)
{
    SHCreateDirectoryExW(NULL, m.c_str(), NULL);
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((g_gameDir + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) { TestLog("copie de lancement : dossier du jeu illisible (erreur %lu)", GetLastError()); return false; }
    do {
        std::wstring n = fd.cFileName;
        if (n == L"." || n == L"..") continue;
        std::wstring src = g_gameDir + n, dst = m + n;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            DWORD at = GetFileAttributesW(dst.c_str());
            if (at != INVALID_FILE_ATTRIBUTES) {
                // Deja la : seulement si c'est bien le dossier du jeu. Une copie restee d'une installation precedente (jeu
                // desinstalle, deplace, MSCLoader installe avant MWCoop) pouvait avoir un vrai dossier Mods vide, cree par
                // MSCLoader la, ou une jonction vers l'ancien jeu : MSCLoader n'y trouvait aucun mod, alors que le lanceur
                // listait ceux du jeu (retour d'un joueur, 08/10 : « 0 mod »). Jonction refaite ; un vrai dossier vide
                // retire, non vide renomme (jamais efface).
                if (SameDirTarget(dst, src)) continue;
                if (at & FILE_ATTRIBUTE_REPARSE_POINT) { RemoveDirectoryW(dst.c_str()); TestLog("copie de lancement : jonction %ls refaite (visait un autre dossier)", n.c_str()); }
                else if (DirEmptyW(dst)) { RemoveDirectoryW(dst.c_str()); TestLog("copie de lancement : dossier vide %ls remplace par la jonction", n.c_str()); }
                else {
                    wchar_t stamp[32]; SYSTEMTIME st; GetLocalTime(&st);
                    swprintf_s(stamp, L".ancien-%04d%02d%02d-%02d%02d%02d", st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
                    if (!MoveFileW(dst.c_str(), (dst + stamp).c_str())) { TestLog("copie de lancement : %ls n'est pas celui du jeu et ne peut etre renomme (erreur %lu)", n.c_str(), GetLastError()); continue; }
                    TestLog("copie de lancement : %ls n'etait pas celui du jeu, renomme %ls%ls", n.c_str(), n.c_str(), stamp);
                }
            }
            if (!MakeJunction(dst, src)) { TestLog("copie de lancement : jonction %ls impossible (erreur %lu)", n.c_str(), GetLastError()); FindClose(h); return false; }
        } else if (CopyKind(n) && !SameFile(src, dst)) {
            if (!CopyFileW(src.c_str(), dst.c_str(), FALSE)) { TestLog("copie de lancement : %ls impossible (erreur %lu)", n.c_str(), GetLastError()); FindClose(h); return false; }
        }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    h = FindFirstFileW((m + L"*").c_str(), &fd);   // retires du jeu
    if (h != INVALID_HANDLE_VALUE) {
        do {
            std::wstring n = fd.cFileName;
            if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || !CopyKind(n)) continue;
            if (GetFileAttributesW((g_gameDir + n).c_str()) == INVALID_FILE_ATTRIBUTES) { DeleteFileW((m + n).c_str()); TestLog("copie de lancement : %ls retire", n.c_str()); }
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    HANDLE a = CreateFileW((m + L"steam_appid.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (a != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(a, "4164420", 7, &n, NULL); CloseHandle(a); }
    TestLog("copie de lancement prete : %ls", m.c_str());
    return true;
}

// Mode debogage : Windows, Smart App Control (bloque les DLL non signees, comme notre version.dll), antivirus declares
// a Windows (Securite Windows : root/SecurityCenter2, par PowerShell, fil a part).
static DWORD WINAPI DebugSysThread(void *)
{
    DWORD sac = 99, sz = sizeof(sac);
    RegGetValueW(HKEY_LOCAL_MACHINE, L"SYSTEM\\CurrentControlSet\\Control\\CI\\Policy", L"VerifiedAndReputablePolicyState", RRF_RT_REG_DWORD, NULL, &sac, &sz);
    wchar_t build[64] = L"?"; DWORD bs = sizeof(build);
    RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", L"CurrentBuild", RRF_RT_REG_SZ, NULL, build, &bs);
    DebugLog("Windows build %s, Smart App Control %s, lanceur %s, jeu %s", Narrow(build).c_str(), sac == 0 ? "coupe" : sac == 1 ? "ACTIF" : sac == 2 ? "en evaluation" : "?", Narrow(g_self, CP_UTF8).c_str(), Narrow(g_gameDir, CP_UTF8).c_str());
    wchar_t sys[MAX_PATH]; GetSystemDirectoryW(sys, MAX_PATH);
    std::wstring cmd = std::wstring(L"\"") + sys + L"\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -NonInteractive -Command \"Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct | ForEach-Object { $_.displayName + ' (etat ' + $_.productState + ')' }\"";
    SECURITY_ATTRIBUTES sa = { sizeof(sa), NULL, TRUE };
    HANDLE rd = NULL, wr = NULL;
    if (!CreatePipe(&rd, &wr, &sa, 0)) return 0;
    SetHandleInformation(rd, HANDLE_FLAG_INHERIT, 0);
    STARTUPINFOW si = { sizeof(si) }; si.dwFlags = STARTF_USESTDHANDLES; si.hStdOutput = wr; si.hStdError = wr;
    PROCESS_INFORMATION pi = {};
    std::vector<wchar_t> cb(cmd.begin(), cmd.end()); cb.push_back(0);
    BOOL ok = CreateProcessW(NULL, cb.data(), NULL, NULL, TRUE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi);
    CloseHandle(wr);
    std::string out;
    if (ok) {
        char b[512]; DWORD n;
        while (ReadFile(rd, b, sizeof(b), &n, NULL) && n) out.append(b, n);
        WaitForSingleObject(pi.hProcess, 15000);
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    }
    CloseHandle(rd);
    while (!out.empty() && (out.back() == '\n' || out.back() == '\r')) out.pop_back();
    DebugLog("antivirus declares : %s", out.empty() ? "(aucun, ou illisible)" : out.c_str());
    return 0;
}
static void DebugSys() { HANDLE t = CreateThread(NULL, 0, DebugSysThread, NULL, 0, NULL); if (t) CloseHandle(t); }

// Lance un programme par le bureau de Windows (IShellDispatch2::ShellExecute du processus explorer du bureau, la methode
// documentee) : ce n'est plus le lanceur qui cree le jeu. Lanceur demarre par Steam, overlay de Steam dans le lanceur :
// le explorer.exe qu'il lancait pouvait demarrer le jeu lui-meme, overlay compris -- la version.dll de Windows passait
// avant la notre, MSCLoader (winhttp.dll) se chargeait mais pas MWCoop (retour d'un joueur, 08/10, 18:59). Et les
// arguments passent (-mscloader-disable, mode...), ce que explorer.exe ne permettait pas.
static bool ShellRunFromDesktop(const std::wstring &file, const std::wstring &args, const std::wstring &dir)
{
    HRESULT co = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    bool ok = false;
    HRESULT why = E_FAIL;
    IShellWindows *sw = NULL;
    if (SUCCEEDED(why = CoCreateInstance(__uuidof(ShellWindows), NULL, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&sw)))) {
        VARIANT loc; VariantInit(&loc); loc.vt = VT_I4; loc.lVal = CSIDL_DESKTOP;
        VARIANT empty; VariantInit(&empty);
        long hwnd = 0;
        IDispatch *disp = NULL;
        if ((why = sw->FindWindowSW(&loc, &empty, SWC_DESKTOP, &hwnd, SWFO_NEEDDISPATCH, &disp)) == S_OK && disp) {
            IServiceProvider *sp = NULL;
            if (SUCCEEDED(why = disp->QueryInterface(IID_PPV_ARGS(&sp)))) {
                IShellBrowser *sb = NULL;
                if (SUCCEEDED(why = sp->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&sb)))) {
                    IShellView *sv = NULL;
                    if (SUCCEEDED(why = sb->QueryActiveShellView(&sv))) {
                        IDispatch *bg = NULL;
                        if (SUCCEEDED(why = sv->GetItemObject(SVGIO_BACKGROUND, IID_PPV_ARGS(&bg)))) {
                            IShellFolderViewDual *fv = NULL;
                            if (SUCCEEDED(why = bg->QueryInterface(IID_PPV_ARGS(&fv)))) {
                                IDispatch *app = NULL;
                                if (SUCCEEDED(why = fv->get_Application(&app))) {
                                    IShellDispatch2 *sd = NULL;
                                    if (SUCCEEDED(why = app->QueryInterface(IID_PPV_ARGS(&sd)))) {
                                        BSTR f = SysAllocString(file.c_str());
                                        VARIANT a, d, op, show;
                                        VariantInit(&a); a.vt = VT_BSTR; a.bstrVal = SysAllocString(args.c_str());
                                        VariantInit(&d); d.vt = VT_BSTR; d.bstrVal = SysAllocString(dir.c_str());
                                        VariantInit(&op); op.vt = VT_BSTR; op.bstrVal = SysAllocString(L"open");
                                        VariantInit(&show); show.vt = VT_I4; show.lVal = SW_SHOWNORMAL;
                                        ok = SUCCEEDED(why = sd->ShellExecute(f, a, d, op, show));
                                        SysFreeString(f); VariantClear(&a); VariantClear(&d); VariantClear(&op);
                                        sd->Release();
                                    }
                                    app->Release();
                                }
                                fv->Release();
                            }
                            bg->Release();
                        }
                        sv->Release();
                    }
                    sb->Release();
                }
                sp->Release();
            }
            disp->Release();
        }
        sw->Release();
    }
    if (!ok) LaunchLog("bureau de Windows : lancement impossible (0x%08lx)", (unsigned long)why);
    if (SUCCEEDED(co)) CoUninitialize();
    return ok;
}

// Jeux en cours : leur version.dll (la notre ?), winhttp.dll (Doorstop de MSCLoader), overlay de Steam, et leur parent.
static void LogGameModules(bool full = false)   // (full : toutes les DLL, mode debogage)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return;
    std::vector<PROCESSENTRY32W> all;
    PROCESSENTRY32W pe = { sizeof(pe) };
    for (BOOL ok = Process32FirstW(snap, &pe); ok; ok = Process32NextW(snap, &pe)) all.push_back(pe);
    CloseHandle(snap);
    for (const PROCESSENTRY32W &p : all) {
        if (_wcsicmp(p.szExeFile, L"mywintercar.exe")) continue;
        std::wstring parent = L"?";
        for (const PROCESSENTRY32W &q : all) if (q.th32ProcessID == p.th32ParentProcessID) parent = q.szExeFile;
        HANDLE h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, FALSE, p.th32ProcessID);
        if (!h) { LaunchLog("jeu %lu (parent %s) : modules illisibles (erreur %lu)", p.th32ProcessID, Narrow(parent, CP_UTF8).c_str(), GetLastError()); continue; }
        wchar_t exe[MAX_PATH] = L""; DWORD n = MAX_PATH;
        QueryFullProcessImageNameW(h, 0, exe, &n);
        std::string found;
        static HMODULE mods[2048];
        DWORD need = 0;
        if (K32EnumProcessModulesEx(h, mods, sizeof(mods), &need, LIST_MODULES_ALL))
            for (DWORD i = 0; i < need / sizeof(HMODULE) && i < 2048; i++) {
                wchar_t m[MAX_PATH];
                if (!K32GetModuleFileNameExW(h, mods[i], m, MAX_PATH)) continue;
                const wchar_t *b = wcsrchr(m, L'\\'); b = b ? b + 1 : m;
                if (full || !_wcsicmp(b, L"version.dll") || !_wcsicmp(b, L"winhttp.dll") || !_wcsnicmp(b, L"gameoverlayrenderer", 19)) found += (full ? "\r\n    " : " ") + Narrow(m, CP_UTF8);
            }
        LaunchLog("jeu %lu %s (parent %s) :%s", p.th32ProcessID, Narrow(exe, CP_UTF8).c_str(), Narrow(parent, CP_UTF8).c_str(), found.empty() ? " aucun de version.dll / winhttp.dll / overlay" : found.c_str());
        CloseHandle(h);
    }
}

// Le jeu de CE dossier tourne-t-il deja ? Les jeux d'autres dossiers (copies pour jouer a deux sur
// un PC, instances de test) ont leur propre profil MWCoop, donc leur propre verrou d'instance unique.
static bool GameProcessRunning()
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W pe = { sizeof(pe) };
    std::wstring mine = g_gameDir + L"mywintercar.exe", mirror = GameExe(), guest = LocalDir() + L"invite\\My Winter Car\\mywintercar.exe", own = MscCopyDir() + L"mywintercar.exe";
    bool found = false;
    for (BOOL ok = Process32FirstW(snap, &pe); ok && !found; ok = Process32NextW(snap, &pe)) {
        if (_wcsicmp(pe.szExeFile, L"mywintercar.exe")) continue;
        HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pe.th32ProcessID);
        if (!h) { found = true; break; }   // inconnu : prudence
        wchar_t path[MAX_PATH];
        DWORD n = MAX_PATH;
        if (!QueryFullProcessImageNameW(h, 0, path, &n) || !_wcsicmp(path, mine.c_str()) || !_wcsicmp(path, mirror.c_str()) || !_wcsicmp(path, guest.c_str()) || !_wcsicmp(path, own.c_str())) found = true;
        else {
            // Jeu d'un autre dossier (autre installation, relance par Steam ailleurs...) : note une fois. Retour d'un joueur
            // (08/10) : « Game over » aussitot, le jeu ouvert sans MWCoop -- il tournait d'un dossier inconnu du lanceur.
            static std::vector<std::wstring> told;
            if (std::find(told.begin(), told.end(), std::wstring(path)) == told.end()) { told.push_back(path); LaunchLog("mywintercar.exe d'un autre dossier en cours : %s (dossier du jeu choisi : %s)", Narrow(path, CP_UTF8).c_str(), Narrow(g_gameDir, CP_UTF8).c_str()); }
        }
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
    if (mode != MODE_SOLO) t += std::string("Reseau=") + (g_steamNet ? "steam" : "ip") + "\r\n";
    if (mode == MODE_GUEST && g_steamNet && g_sHost) {   // salon Steam du lanceur : le mod se connecte directement a l'hote
        char hs[40];
        sprintf_s(hs, "HoteSteam=%llu\r\n", (unsigned long long)g_sHost);
        t += hs;
    }
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

// Chargeur du jeu (version.dll) verifie avant chaque lancement, la ou le jeu part (demande de JD, 08/10 : chez un joueur,
// rien ne chargeait MWCoop depuis une installation alors que les memes fichiers marchaient sur une autre). Reference :
// celui de la meme compilation que le lanceur (ressource 5). Absent, ancien MWCoop ou abime -> remis ; plus recent (lanceur
// pas a jour) -> laisse ; une version.dll d'un autre programme (ReShade...) -> mise de cote (version.dll.autre), la notre
// a sa place. Le journal dit ce qui a ete trouve.
static std::wstring VerString(const std::wstring &path, const wchar_t *key)
{
    typedef DWORD (WINAPI *SizeFn)(LPCWSTR, LPDWORD);
    typedef BOOL (WINAPI *InfoFn)(LPCWSTR, DWORD, DWORD, LPVOID);
    typedef BOOL (WINAPI *QueryFn)(LPCVOID, LPCWSTR, LPVOID *, PUINT);
    wchar_t sys[MAX_PATH] = L"";
    GetSystemDirectoryW(sys, MAX_PATH);
    HMODULE ver = LoadLibraryExW((std::wstring(sys) + L"\\version.dll").c_str(), NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!ver) return L"";
    SizeFn size = (SizeFn)GetProcAddress(ver, "GetFileVersionInfoSizeW");
    InfoFn info = (InfoFn)GetProcAddress(ver, "GetFileVersionInfoW");
    QueryFn query = (QueryFn)GetProcAddress(ver, "VerQueryValueW");
    if (!size || !info || !query) return L"";
    DWORD h = 0, n = size(path.c_str(), &h);
    if (!n) return L"";
    std::vector<char> buf(n);
    if (!info(path.c_str(), 0, n, buf.data())) return L"";
    struct { WORD lang, cp; } *tr = NULL;
    UINT len = 0;
    wchar_t sub[96];
    if (query(buf.data(), L"\\VarFileInfo\\Translation", (void **)&tr, &len) && tr && len >= 4) swprintf_s(sub, L"\\StringFileInfo\\%04x%04x\\%s", tr->lang, tr->cp, key);
    else swprintf_s(sub, L"\\StringFileInfo\\040904B0\\%s", key);
    wchar_t *v = NULL;
    if (!query(buf.data(), sub, (void **)&v, &len) || !v) return L"";
    return v;
}
static std::string VerText(unsigned long long v)
{
    char b[48]; sprintf_s(b, "%u.%u.%u", (unsigned)(v >> 48), (unsigned)((v >> 32) & 0xFFFF), (unsigned)((v >> 16) & 0xFFFF));
    return b;
}
static void LoaderEnsure(const std::wstring &dir, const char *where)
{
    HRSRC r = FindResourceW(NULL, MAKEINTRESOURCEW(5), RT_RCDATA);
    HGLOBAL hg = r ? LoadResource(NULL, r) : NULL;
    const BYTE *ref = hg ? (const BYTE *)LockResource(hg) : NULL;
    DWORD refN = ref ? SizeofResource(NULL, r) : 0;
    if (!refN || dir.empty()) return;
    std::wstring p = dir + L"version.dll";
    std::vector<unsigned char> cur;
    bool present = FileExists(p);
    if (present && ReadAll(p, cur) && cur.size() == refN && !memcmp(cur.data(), ref, refN)) return;   // la notre, a jour
    wchar_t self[MAX_PATH] = L"";
    GetModuleFileNameW(NULL, self, MAX_PATH);
    unsigned long long mine = ExeVersion(self), theirs = present ? ExeVersion(p) : 0;
    std::wstring internal = present ? VerString(p, L"InternalName") : L"", product = present ? VerString(p, L"ProductName") : L"";
    bool ours = internal == L"MWCoopLoader" || (present && cur.size() > 0 && internal.empty() && product.empty() && cur.size() < 400000 && theirs == 0);   // (chargeurs d'avant la 0.59 : sans informations de version)
    std::string what = !present ? "absente" : ours ? "MWCoop " + (theirs ? VerText(theirs) : std::string("sans version")) + " (" + std::to_string(cur.size()) + " octets)"
                                            : "AUTRE PROGRAMME : " + Narrow(product.empty() ? L"(sans nom)" : product, CP_UTF8) + " " + (theirs ? VerText(theirs) : std::string("")) + " (" + std::to_string(cur.size()) + " octets)";
    if (present && ours && theirs > mine && mine) { LaunchLog("chargeur (%s) : %s, plus recent que ce lanceur (%s) : laisse", where, what.c_str(), VerText(mine).c_str()); return; }
    if (present && !ours) {
        std::wstring keep = dir + L"version.dll.autre";
        DeleteFileW(keep.c_str());
        if (!MoveFileExW(p.c_str(), keep.c_str(), MOVEFILE_REPLACE_EXISTING)) { LaunchLog("chargeur (%s) : %s, mise de cote impossible (erreur %lu)", where, what.c_str(), GetLastError()); return; }
    }
    // ecrit a cote puis remplace (verrouille par un jeu ouvert : l'ancien renomme .old, comme les mises a jour)
    std::wstring tmp = dir + L"version.dll.mwcoop-tmp";
    HANDLE f = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    DWORD wr = 0;
    bool ok = f != INVALID_HANDLE_VALUE && WriteFile(f, ref, refN, &wr, NULL) && wr == refN;
    if (f != INVALID_HANDLE_VALUE) CloseHandle(f);
    if (ok && !MoveFileExW(tmp.c_str(), p.c_str(), MOVEFILE_REPLACE_EXISTING)) {
        std::wstring old = p + L".old";
        DeleteFileW(old.c_str());
        ok = MoveFileExW(p.c_str(), old.c_str(), MOVEFILE_REPLACE_EXISTING) && MoveFileExW(tmp.c_str(), p.c_str(), MOVEFILE_REPLACE_EXISTING);
    }
    if (!ok) { DWORD e = GetLastError(); DeleteFileW(tmp.c_str()); LaunchLog("chargeur (%s) : %s, remplacement IMPOSSIBLE (erreur %lu)", where, what.c_str(), e); return; }
    LaunchLog("chargeur (%s) : %s -> remplace par celui du lanceur (%s)%s", where, what.c_str(), VerText(mine).c_str(), present && !ours ? ", l'autre garde en version.dll.autre" : "");
    if (present && !ours)
        SetStatus(K_WARN, T(L"Une version.dll d'un autre programme \u00E9tait dans le dossier du jeu : mise de c\u00F4t\u00E9 (version.dll.autre)", L"A version.dll from another program was in the game folder: set aside (version.dll.autre)"));
}

static int SyncLaunchKind();   // (modsync.inc)
static std::wstring GuestCopyDir();
static bool PrepareGuestCopy(const std::wstring &m, bool mods);
static bool g_syncKeep;   // GO recu : la copie des mods sert au lancement (LobbyClose ne l'oublie pas)
static void Launch(int mode, const char *partie = NULL)
{
    if (mode == MODE_SOLO) g_launchWarn.clear();   // (avertissement UDP : salon seulement)
    if (g_gameDir.empty() || g_busy || !g_modOk) { TestLog("lancement impossible (jeu=%d occupe=%d mod=%d)", (int)!g_gameDir.empty(), (int)g_busy, (int)g_modOk); return; }
    std::wstring addr = Trim(g_fields[1].text);
    const Opt *po = OptByKey("Port");
    int port = po ? OptGet(*po) : 7870;
    if (mode == MODE_GUEST && !g_steamNet) {
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
    } else if (mode == MODE_GUEST) {
        addr = g_sHostName.empty() ? std::wstring(L"Steam") : g_sHostName;   // (Steam : pour l'ecran d'attente)
    } else if (mode != MODE_GUEST && g_testSalon.empty() && GameProcessRunning()) {   // (instance unique : un 2e jeu hors profil se fermerait aussitot)
        SetStatus(K_ERR, T(L"My Winter Car est d\u00E9j\u00E0 lanc\u00E9 : ferme-le d'abord", L"My Winter Car is already running: close it first"));
        return;
    }
    SavePlayer();
    if (!WriteLaunchFile(mode, addr, port, partie)) {
        SetStatus(K_ERR, T(L"Impossible d'\u00E9crire MWCoop\\lancement.ini (erreur %lu)", L"Could not write MWCoop\\lancement.ini (error %lu)"), GetLastError());
        TestLog("lancement.ini : ecriture impossible");
        return;
    }
    int syncKind = mode == MODE_GUEST ? SyncLaunchKind() : 0;   // mods de l'hote : 1 sans MSCLoader, 2 avec ses mods
    g_syncKeep = false;
    if (!g_testSalon.empty()) {   // mode d'essai : le lancement.ini dans le journal, et JAMAIS le jeu
        if (syncKind) {
            std::wstring gc = GuestCopyDir();
            TestLog("test : mods au lancement : %s, copie %s", syncKind == 2 ? "ceux de l'hote" : "sans MSCLoader", PrepareGuestCopy(gc, syncKind == 2) ? "prete" : "IMPOSSIBLE");
        }
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
    if (mode == MODE_GUEST) args += (g_steamNet ? std::wstring() : L" -mwcoop-adresse " + addr) + L" -mwcoop-profil invite";
    SteamDown();   // (le lanceur ne passe plus pour le jeu aupres de Steam)
    // MSCLoader installe mais coupe dans l'onglet MODS : son prechargeur s'arrete tout de suite (rien n'est desinstalle).
    bool mscOff = syncKind ? syncKind == 1 : !g_mscOn;   // (salon : comme l'hote)
    if (mscOff && FileExists(g_gameDir + L"winhttp.dll") && FileExists(g_gameDir + L"doorstop_config.ini")) { args += L" -mscloader-disable"; TestLog("lancement sans MSCLoader (-mscloader-disable)"); }
    g_preWnds.clear();
    EnumWindows(ListUnityWindows, (LPARAM)&g_preWnds);
    auto done = [&]() {
        g_launchT = GetTickCount();
        g_lastLaunchMode = mode;
        g_winSeenT = g_noProcT = 0;
        g_modChecked = g_modMissing = false;
        std::wstring name = PlayerName();
        wchar_t info[160];
        if (mode == MODE_HOST && g_steamNet) swprintf_s(info, T(L"%s h\u00E9berge la partie (Steam)", L"%s is hosting (Steam)"), name.c_str());
        else if (mode == MODE_HOST) swprintf_s(info, T(L"%s h\u00E9berge la partie (port %d)", L"%s is hosting (port %d)"), name.c_str(), port);
        else if (mode == MODE_GUEST) swprintf_s(info, T(L"%s rejoint %s", L"%s joins %s"), name.c_str(), addr.c_str());
        else swprintf_s(info, T(L"%s joue en solo", L"%s plays solo"), name.c_str());
        g_launchInfo = info;
        g_focus = -1;
        g_tab = TAB_HOME;
        g_lobbyOpts = false;
        g_state = ST_LAUNCH;
    };
    // Lancer par Steam (Reglages > Jeu, ou propose quand MWCoop ne s'est pas charge) : comme son bouton JOUER, qui lance le
    // jeu de son dossier ; MWCoop y lit lancement.ini. Pas pour l'invite d'un salon avec les mods de l'hote (sa copie), ni
    // hors de Steam. (MSCLoader coupe : pas de -mscloader-disable par ce chemin.)
    LoaderEnsure(g_gameDir, "dossier du jeu");
    const Opt *viaSteam = OptByKey("LancerSteam");
    g_steamLaunch = viaSteam && OptGet(*viaSteam) && !syncKind && !MirrorDir().empty();
    if (g_steamLaunch) {
        Sleep(800);   // (SteamDown ci-dessus : que Steam ne voie plus le lanceur comme le jeu en cours)
        if (!LocalDir().empty()) {   // (si Steam nous relance par l'option de lancement : demarrer le jeu, wWinMain)
            HANDLE rq = CreateFileW((LocalDir() + L"lancer-par-steam.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
            if (rq != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(rq, "1", 1, &n, NULL); CloseHandle(rq); }
        }
        HINSTANCE r = ShellExecuteW(g_wnd, L"open", L"steam://rungameid/4164420", NULL, NULL, SW_SHOWNORMAL);
        LaunchLog("lancement (mode %d) par Steam (steam://rungameid/4164420) : %s", mode, (INT_PTR)r > 32 ? "demande" : "ECHEC");
        if ((INT_PTR)r <= 32) { SetStatus(K_ERR, T(L"Steam n'a pas pu lancer le jeu (Steam est-il ouvert ?)", L"Steam could not start the game (is Steam open?)")); return; }
        g_proc = NULL;
        g_pid = 0;
        done();
        // Le lanceur se ferme : tant qu'il vit, Steam le prend pour le jeu deja lance (connecte sous son identite) et ne
        // demarre pas le vrai (deux joueurs, 08/10 : « seulement si je ferme le lanceur apres avoir heberge »).
        LaunchLog("lanceur ferme : Steam demarre le jeu");
        g_state = ST_CLOSING;
        return;
    }
    // Lance comme un double-clic dans l'explorateur : un mode de compatibilite de l'exe peut exiger l'administrateur ;
    // CreateProcess echoue alors (erreur 740), ShellExecuteEx affiche la demande de Windows.
    std::wstring exe = g_gameDir + L"mywintercar.exe", runDir = g_gameDir;
    g_launchedFromCopy = false;
    std::wstring mirror = syncKind ? GuestCopyDir() : MirrorDir();
    // Dossier du jeu plutot que la copie de lancement ([Lanceur] SansCopie, pris tout seul quand MWCoop ne s'est pas charge
    // depuis la copie) : chez un joueur (08/10), le jeu parti de la copie dans AppData ne chargeait jamais MWCoop -- notre
    // version.dll y etait bloquee --, alors que parti du dossier du jeu, si. Chez d'autres, c'est l'inverse (copie creee
    // pour ca, 05/10) : chacun garde ce qui marche chez lui.
    bool noCopy = !syncKind && GetPrivateProfileIntW(L"Lanceur", L"SansCopie", 0, g_iniLauncher.c_str()) != 0;
    if (noCopy && !mirror.empty()) { mirror.clear(); LaunchLog("depart du dossier du jeu (SansCopie : MWCoop ne se chargeait pas depuis la copie)"); }
    // MSCLoader de MWCoop (onglet MODS) : toujours une copie de lancement, avec son Doorstop (jeu Steam ou non).
    bool ownMsc = MscOwn() && MscOwnInstalled();
    if (!syncKind && ownMsc && g_mscOn) mirror = MscCopyDir();
    else ownMsc = ownMsc && syncKind;
    if (syncKind && !mirror.empty()) {   // invite d'un salon : sa copie de lancement (Mods -> les mods de l'hote)
        if (PrepareGuestCopy(mirror, syncKind == 2) && (!ownMsc || MscOwnApply(mirror, syncKind == 2, true))) { exe = mirror + L"mywintercar.exe"; runDir = mirror; }
        else SetStatus(K_WARN, T(L"Copie de lancement impossible : le jeu part sans les mods de l'h\u00F4te", L"Could not prepare the launch copy: the game starts without the host's mods"));
    } else if (!mirror.empty()) {
        if (PrepareMirror(mirror) && (!ownMsc || MscOwnApply(mirror, true, false))) { exe = mirror + L"mywintercar.exe"; runDir = mirror; g_launchedFromCopy = !ownMsc && mirror == MirrorDir(); }
        else SetStatus(K_WARN, T(L"Copie de lancement impossible : le jeu part de son dossier (le mod ou MSCLoader risquent de ne pas se charger)", L"Could not prepare the launch copy: starting from the game folder (the mod or MSCLoader may not load)"));
    }
    if (mscOff) MscOffInCopy(runDir);
    // Lanceur demarre par Steam (option de lancement "<MWCoop.exe>" %command%) : l'overlay de Steam y est injecte et
    // passerait au jeu lance d'ici, assez tot pour que la version.dll de Windows passe avant la notre (le mod ne se
    // chargerait pas, cf. steam_appid.txt). Le jeu est alors lance par l'explorateur, hors de l'arbre de Steam ; ses
    // reglages sont dans lancement.ini (pas de ligne de commande par ce chemin).
    // Demarre par Steam (vu au demarrage : ses variables, ou le chemin du jeu en argument) ou overlay deja la : le
    // 07/10 l'overlay n'a pas ete vu dans le lanceur demarre par Steam, le jeu est parti de lui et le mod ne s'est pas charge.
    if (_wcsicmp(runDir.c_str(), g_gameDir.c_str())) LoaderEnsure(runDir, "copie de lancement");
    {   // notre chargeur, la ou le jeu part (absent ou change : antivirus ?)
        auto info = [](const std::wstring &p) -> std::string {
            WIN32_FILE_ATTRIBUTE_DATA a;
            if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a)) return "ABSENTE";
            SYSTEMTIME st; FILETIME lt; FileTimeToLocalFileTime(&a.ftLastWriteTime, &lt); FileTimeToSystemTime(&lt, &st);
            char b[64]; sprintf_s(b, "%lu octets, %02d/%02d %02d:%02d", a.nFileSizeLow, st.wDay, st.wMonth, st.wHour, st.wMinute);
            return b;
        };
        LaunchLog("version.dll : jeu %s ; depart %s", info(g_gameDir + L"version.dll").c_str(), info(runDir + L"version.dll").c_str());
    }
    bool overlay = GetModuleHandleW(L"gameoverlayrenderer64.dll") != NULL;
    bool viaShell = g_fromSteam || overlay;
    LaunchLog("lancement (mode %d) : %s par %s (demarre par Steam %d, overlay %d)", mode, Narrow(exe, CP_UTF8).c_str(), viaShell ? "l'explorateur" : "le lanceur", (int)g_fromSteam, (int)overlay);
    std::wstring shellArg = L"\"" + exe + L"\"";
    // Toujours par le bureau de Windows (0.62) : le lanceur s'est connecte a Steam sous l'identite du jeu (salons), Steam y a
    // mis sa surcouche, qu'un jeu lance par lui heritait des son demarrage -- la version.dll de Windows passait avant la notre,
    // MWCoop absent (deux joueurs, 08/10 : « ca ne marche que si je ferme le lanceur apres avoir heberge »). Avant : seulement
    // quand la surcouche etait vue dans le lanceur.
    if (g_debug) {
        wchar_t v[64];
        std::string env;
        for (const wchar_t *n : { L"SteamAppId", L"SteamGameId", L"SteamOverlayGameId", L"SteamClientLaunch" }) if (GetEnvironmentVariableW(n, v, 64)) env += " " + Narrow(n) + "=" + Narrow(v);
        DebugLog("depart : dossier %s, arguments %s, environnement Steam%s", Narrow(runDir, CP_UTF8).c_str(), Narrow(args, CP_UTF8).c_str(), env.empty() ? " (aucun)" : env.c_str());
        std::vector<unsigned char> d;
        if (ReadAll(runDir + L"doorstop_config.ini", d)) DebugLog("doorstop_config.ini du depart :\r\n%s", std::string(d.begin(), d.end()).c_str());
        WIN32_FIND_DATAW fd;
        HANDLE fh = FindFirstFileW((runDir + L"*.dll").c_str(), &fd);
        std::string dlls;
        if (fh != INVALID_HANDLE_VALUE) { do { dlls += " " + Narrow(fd.cFileName, CP_UTF8) + "(" + std::to_string(fd.nFileSizeLow) + ")"; } while (FindNextFileW(fh, &fd)); FindClose(fh); }
        DebugLog("DLL du depart :%s", dlls.c_str());
    }
    if (ShellRunFromDesktop(exe, args, runDir)) {
        LaunchLog("jeu lance par le bureau de Windows, avec ses arguments");
        g_proc = NULL;
        g_pid = 0;
        done();
        return;
    }
    LaunchLog("bureau de Windows indisponible : %s", viaShell ? "explorer.exe" : "lancement direct");
    SHELLEXECUTEINFOW sei = { sizeof(sei) };
    sei.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
    sei.hwnd = g_wnd;
    sei.lpVerb = L"open";
    sei.lpFile = viaShell ? L"explorer.exe" : exe.c_str();
    sei.lpParameters = viaShell ? shellArg.c_str() : args.c_str();
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
    done();
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
enum { M_MODS = 20, M_MODGET, M_MODDATA, M_MODSREQ };   // mods de l'hote (modsync.inc)
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
static RectF kLobbyList(284, 148, 604, 576);   // (placee par DrawLobby : moins haute sous un avertissement)
static const RectF kLobbyCard(264, 88, 644, 656);
static const RectF kChoiceR[2] = { RectF(944, 136, 300, 56), RectF(944, 198, 300, 56) };
static const float kLobbyRowH = 86;
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
static std::string MyShirt() { const Opt *ap = OptByKey("Apparence"); return ap ? ap->svals[OptGet(*ap)] : "char_shirt21"; }
// Apparence annoncee au salon : complete ("haut|pantalon|visage|corps|chapeau|lunettes|cheveux", comme le mod).
static std::string MySkin() { return PersoLookString(); }
static std::string MyName() { return Narrow(PlayerName(), CP_UTF8); }
static std::string MyVersion() { return g_localVer.empty() ? "dev" : Narrow(g_localVer, CP_UTF8); }   // MWCoop\version.txt
// Apparence lisible ("Tenue 21", "Policier") : libelle de l'onglet COOP, sinon le nom brut.
static std::wstring SkinLabel(const std::string &look)
{
    std::string skin = look.substr(0, look.find('|'));   // (apparence complete : le haut)
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

// ---------------------------------------------------------------- pare-feu Windows
// Le salon passe par MWCoop.exe (deja autorise), mais le jeu recoit les invites en UDP : mywintercar.exe, ou sa copie
// de lancement pour Steam (%LOCALAPPDATA%\MWCoop\jeu, un autre exe pour Windows). Sans regle qui l'autorise sur le
// reseau actif (Radmin VPN est souvent un reseau "Public"), ou avec les regles de BLOCAGE que Windows cree quand sa
// fenetre de question est refusee ou reste cachee derriere le jeu en plein ecran, l'invite n'a aucune reponse :
// "delai depasse" chez lui (Nexus, 06/10). Les instances de test passent par 127.0.0.1 : jamais filtre, jamais vu.
enum { FW_OK, FW_MISSING, FW_BLOCKED, FW_UNKNOWN };

static INetFwPolicy2 *FwPolicy()
{
    INetFwPolicy2 *pol = NULL;
    if (FAILED(CoCreateInstance(__uuidof(NetFwPolicy2), NULL, CLSCTX_INPROC_SERVER, __uuidof(INetFwPolicy2), (void **)&pol))) return NULL;
    return pol;
}

// Appelle f(regle) pour chaque regle d'entree active (ou non, si all) de cet exe, en UDP ou tout protocole.
template <class F> static void FwEachRule(INetFwPolicy2 *pol, const std::wstring &app, bool all, F f)
{
    INetFwRules *rules = NULL;
    if (FAILED(pol->get_Rules(&rules)) || !rules) return;
    IUnknown *u = NULL;
    IEnumVARIANT *ev = NULL;
    if (SUCCEEDED(rules->get__NewEnum(&u)) && u && SUCCEEDED(u->QueryInterface(__uuidof(IEnumVARIANT), (void **)&ev)) && ev) {
        VARIANT v;
        VariantInit(&v);
        while (ev->Next(1, &v, NULL) == S_OK) {
            INetFwRule *r = NULL;
            if (v.vt == VT_DISPATCH && v.pdispVal && SUCCEEDED(v.pdispVal->QueryInterface(__uuidof(INetFwRule), (void **)&r)) && r) {
                BSTR a = NULL;
                VARIANT_BOOL en = VARIANT_FALSE;
                NET_FW_RULE_DIRECTION d = NET_FW_RULE_DIR_OUT;
                long proto = 0;
                if (SUCCEEDED(r->get_ApplicationName(&a)) && a && !_wcsicmp(a, app.c_str()) && SUCCEEDED(r->get_Enabled(&en))
                    && SUCCEEDED(r->get_Direction(&d)) && SUCCEEDED(r->get_Protocol(&proto))
                    && (all || en) && d == NET_FW_RULE_DIR_IN && (proto == NET_FW_IP_PROTOCOL_UDP || proto == NET_FW_IP_PROTOCOL_ANY))
                    f(r);
                if (a) SysFreeString(a);
                r->Release();
            }
            VariantClear(&v);
        }
        ev->Release();
    }
    if (u) u->Release();
    rules->Release();
}

// Le jeu (ou le lanceur) peut-il recevoir de l'UDP sur les reseaux actifs ? (Pare-feu coupe : oui.)
static int FirewallState(const std::wstring &app)
{
    HRESULT co = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    int st = FW_UNKNOWN;
    if (INetFwPolicy2 *pol = FwPolicy()) {
        long cur = 0, on = 0;
        pol->get_CurrentProfileTypes(&cur);
        for (long b : { (long)NET_FW_PROFILE2_DOMAIN, (long)NET_FW_PROFILE2_PRIVATE, (long)NET_FW_PROFILE2_PUBLIC }) {
            VARIANT_BOOL e = VARIANT_FALSE;
            if ((cur & b) && SUCCEEDED(pol->get_FirewallEnabled((NET_FW_PROFILE_TYPE2)b, &e)) && e) on |= b;
        }
        long allowed = 0;
        bool blocked = false;
        FwEachRule(pol, app, false, [&](INetFwRule *r) {
            NET_FW_ACTION act = NET_FW_ACTION_ALLOW;
            long prof = 0;
            r->get_Action(&act);
            r->get_Profiles(&prof);
            if (!(prof & on)) return;
            if (act == NET_FW_ACTION_BLOCK) blocked = true;
            else allowed |= prof;
        });
        st = !on ? FW_OK : blocked ? FW_BLOCKED : (allowed & on) == on ? FW_OK : FW_MISSING;
        pol->Release();
    }
    if (SUCCEEDED(co)) CoUninitialize();
    return st;
}

// En administrateur (/parefeu) : regles de blocage de ces exe desactivees (pas supprimees), puis une regle
// "MWCoop : <exe>" qui les autorise en entree, UDP et TCP, sur tous les reseaux. Vrai si tout est passe.
static bool FirewallAllow(const std::vector<std::wstring> &apps)
{
    HRESULT co = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    bool ok = false;
    if (INetFwPolicy2 *pol = FwPolicy()) {
        INetFwRules *rules = NULL;
        ok = SUCCEEDED(pol->get_Rules(&rules)) && rules;
        for (const std::wstring &app : apps) {
            if (!ok) break;
            FwEachRule(pol, app, false, [&](INetFwRule *r) {
                NET_FW_ACTION act = NET_FW_ACTION_ALLOW;
                if (SUCCEEDED(r->get_Action(&act)) && act == NET_FW_ACTION_BLOCK) r->put_Enabled(VARIANT_FALSE);
            });
            for (long proto : { (long)NET_FW_IP_PROTOCOL_UDP, (long)NET_FW_IP_PROTOCOL_TCP }) {
                std::wstring name = L"MWCoop : " + app + (proto == NET_FW_IP_PROTOCOL_UDP ? L" (UDP)" : L" (TCP)");
                BSTR bn = SysAllocString(name.c_str());
                rules->Remove(bn);   // la notre d'une fois precedente (meme nom) : pas de doublon
                INetFwRule *r = NULL;
                if (FAILED(CoCreateInstance(__uuidof(NetFwRule), NULL, CLSCTX_INPROC_SERVER, __uuidof(INetFwRule), (void **)&r)) || !r) { SysFreeString(bn); ok = false; break; }
                BSTR ba = SysAllocString(app.c_str());
                BSTR bd = SysAllocString(L"My Winter Car co-op (MWCoop): lets your friends join the game you host.");
                BSTR bg = SysAllocString(L"MWCoop");
                r->put_Name(bn);
                r->put_Description(bd);
                r->put_ApplicationName(ba);
                r->put_Protocol(proto);
                r->put_Direction(NET_FW_RULE_DIR_IN);
                r->put_Action(NET_FW_ACTION_ALLOW);
                r->put_Profiles(NET_FW_PROFILE2_ALL);
                r->put_Grouping(bg);
                r->put_Enabled(VARIANT_TRUE);
                if (FAILED(rules->Add(r))) ok = false;
                r->Release();
                SysFreeString(bn); SysFreeString(ba); SysFreeString(bd); SysFreeString(bg);
            }
        }
        if (rules) rules->Release();
        pol->Release();
    }
    if (SUCCEEDED(co)) CoUninitialize();
    return ok;
}

// Avant d'heberger : si le pare-feu va bloquer les invites, le dire et proposer de corriger (une fois par lancement
// du lanceur, demande de Windows en administrateur). Faux seulement si le joueur annule l'hebergement.
static bool g_fwAsked;
static bool FirewallBeforeHosting()
{
    if (g_fwAsked || g_gameDir.empty()) return true;
    std::vector<std::wstring> apps = { GameExe(), g_self }, bad;
    bool blocked = false;
    for (const std::wstring &a : apps) {
        int st = FirewallState(a);
        if (st == FW_MISSING || st == FW_BLOCKED) { bad.push_back(a); blocked |= st == FW_BLOCKED; }
    }
    if (bad.empty()) return true;
    g_fwAsked = true;
    std::wstring msg = blocked
        ? T(L"Le pare-feu Windows BLOQUE My Winter Car (sa question a \u00E9t\u00E9 refus\u00E9e ou est rest\u00E9e cach\u00E9e derri\u00E8re le jeu).",
            L"Windows Firewall is BLOCKING My Winter Car (its question was refused, or stayed hidden behind the game).")
        : T(L"Le pare-feu Windows n'autorise pas encore My Winter Car sur ce r\u00E9seau (Radmin VPN compte souvent comme un r\u00E9seau public).",
            L"Windows Firewall does not allow My Winter Car on this network yet (Radmin VPN often counts as a public network).");
    msg += T(L"\n\nTes amis entreraient dans le salon, mais pas dans la partie (\u00AB d\u00E9lai d\u00E9pass\u00E9 \u00BB).\n\nAutoriser My Winter Car et MWCoop dans le pare-feu ? Windows va demander l'accord administrateur.",
             L"\n\nYour friends would get into the lobby, but not into the game (\"timed out\").\n\nAllow My Winter Car and MWCoop in the firewall? Windows will ask for administrator approval.");
    int r = MessageBoxW(g_wnd, msg.c_str(), L"MWCoop", MB_YESNOCANCEL | MB_ICONWARNING);
    if (r == IDCANCEL) return false;
    if (r != IDYES) return true;
    std::wstring args = L"/parefeu";
    for (const std::wstring &a : bad) args += L" \"" + a + L"\"";
    SHELLEXECUTEINFOW sei = { sizeof(sei) };
    sei.fMask = SEE_MASK_NOCLOSEPROCESS;
    sei.hwnd = g_wnd;
    sei.lpVerb = L"runas";
    sei.lpFile = g_self.c_str();
    sei.lpParameters = args.c_str();
    sei.nShow = SW_HIDE;
    DWORD code = 1;
    if (ShellExecuteExW(&sei) && sei.hProcess) {
        WaitForSingleObject(sei.hProcess, 60000);
        GetExitCodeProcess(sei.hProcess, &code);
        CloseHandle(sei.hProcess);
    }
    bool fixed = code == 0;
    for (const std::wstring &a : bad) fixed = fixed && FirewallState(a) == FW_OK;
    if (fixed) SetStatus(K_OK, T(L"Pare-feu : My Winter Car autoris\u00E9", L"Firewall: My Winter Car allowed"));
    else SetStatus(K_WARN, T(L"Pare-feu non modifi\u00E9 : tes amis risquent de ne pas pouvoir entrer en jeu", L"Firewall unchanged: your friends may not get into the game"));
    return true;
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
    SyncOfferReset();
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
    if (g_lobbySteam) SteamLobbyLeave(g_lobby == LB_HOST);
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
    if (!g_syncKeep) SyncReset();   // (GO : gardee pour le lancement)
    if (g_tab == TAB_LOBBY) g_tab = TAB_HOME;
    g_lobbyOpts = false;
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
    if (g_lobbySteam) {   // salon Steam : go dans le salon, les invites lancent leur jeu en le voyant
        int partie = g_partie;
        g_goSent = true;
        g_launchWarn.clear();
        TestLog("salon steam : LANCER (%d invite(s), %d pas prets), partie=%s", guests, notReady, PartieName(partie));
        SteamHostStart(partie);
        LobbyClose();
        Launch(MODE_HOST, PartieName(partie));
        return;
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
                                        L"(la partie en cours sera perdue).\n\nCouleur de la CORRIS (ic\u00F4ne voiture du salon) : ",
                                        L"Start a NEW game?\n\nIt replaces your current My Winter Car save (the game in progress will be lost).\n\n"
                                        L"CORRIS color (car icon in the lobby): ")) + CarColorName() + L".";
        if (MessageBoxW(g_wnd, q.c_str(), L"MWCoop", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) return;
        if (g_lobby != LB_HOST || g_goSent) return;
    }
    g_partie = p;
    TestLog("salon : partie = %s", PartieName(p));
    if (g_lobbySteam) SSetLobby("partie", PartieName(p));
    else BroadcastState();
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
        } else if (type == M_MODS) SyncOnOffer(m);
        else if (type == M_MODDATA) SyncOnData(m);
        else if (type == M_PING) {
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
    SyncReset();
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
    SetStatus(K_NORMAL, T(L"Connexion au salon de %s\u2026", L"Connecting to %s's lobby\u2026"), g_lobbyAddr.c_str());
    TestLog("salon : connexion a %s:%d (version %s, %s, %s)", Narrow(g_lobbyAddr).c_str(), g_lobbyPort, MyVersion().c_str(), MyName().c_str(), g_mySkinSent.c_str());
    HANDLE t = CreateThread(NULL, 0, GuestThread, (void *)(intptr_t)gen, 0, NULL);
    if (t) CloseHandle(t);
}
static void GuestToggleReady()
{
    if (g_lobby != LB_GUEST) return;
    g_meReady = !g_meReady;
    if (g_lobbySteam) { SSetMember("pret", g_meReady ? "1" : "0"); TestLog("salon steam : moi pret=%d", (int)g_meReady); return; }
    Wr w; w.u8(M_READY); w.u8(g_meReady ? 1 : 0);
    GuestSend(w);
    TestLog("salon : moi pret=%d", (int)g_meReady);
    if (g_meReady && g_udpMine == UDP_FAIL && g_hostUdpTest)
        SetStatus(K_WARN, T(L"Pr\u00EAt, mais UDP bloqu\u00E9 : l'h\u00F4te doit rediriger le port UDP %d", L"Ready, but UDP is blocked: the host must forward UDP port %d"), g_lobbyPort);
}

#include "steam.inc"
#include "mods.inc"
#include "modsync.inc"
#include "serveur.inc"
static void OnButton(int id);
#include "api.inc"
#include "gfx.inc"
#include "ui.inc"
#include "wiki.inc"
#include "tuto.inc"

// Image de la fenetre : la scene et ses cartes de verre (gardees), l'animation, puis l'interface. Mise en page changee
// (autres cartes) : l'image du fond est refaite et l'interface redessinee aussitot.
static void RenderTo(Bitmap &target, float scale)
{
    g_skinBudget = g_wnd ? 6 : 100000;   // portraits lus par image (capture : tous)
    int w = (int)target.GetWidth(), h = (int)target.GetHeight();
    for (int pass = 0; pass < 3; pass++) {
        EnsureScene(w, h, scale);
        Graphics g(&target);
        g.Clear(Color(0, 0, 0, 0));
        g.SetInterpolationMode(InterpolationModeNearestNeighbor);
        g.DrawImage(g_glassCache, 0, 0, w, h);
        g.SetSmoothingMode(SmoothingModeAntiAlias);
        g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);
        g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        g.SetPixelOffsetMode(PixelOffsetModeHalf);
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(kM, kM);
        g_zones.clear();
        g_glassNow.clear();
        if (!(g_benchSkip & 1)) DrawSceneAnim(g);
        if (!(g_benchSkip & 2)) DrawUI(g);
        DrawSteamGuide(g);
        DrawTuto(g);
        if (g_glassNow == g_glassUsed) break;
        g_glassUsed = g_glassNow;
        GlassBuild(w, h, scale);
    }
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

// Toutes les 2 s, l'hote mesure le ping de chacun ; chaque seconde (aussitot apres un choix dans l'onglet TENUE),
// l'apparence choisie (onglets COOP et TENUE) est annoncee si elle a change.
static void LobbyTick()
{
    static DWORD lastPing, lastSkin;
    DWORD now = GetTickCount();
    int lobby = g_lobby;
    SyncTick();
    SyncSteamTick();
    if ((lobby == LB_HOST || lobby == LB_GUEST) && (now - lastSkin >= 1000 || g_skinAnnounce)) {
        lastSkin = now;
        g_skinAnnounce = false;
        std::string sk = MySkin();
        if (sk != g_mySkinSent) {
            g_mySkinSent = sk;
            if (g_lobbySteam) SSetMember("tenue", sk);
            else if (lobby == LB_HOST) {
                EnterCriticalSection(&g_lcs);
                if (LobbyPeer *p = PeerById(0)) p->skin = sk;
                LeaveCriticalSection(&g_lcs);
                BroadcastState();
            } else { Wr w; w.u8(M_SKIN); w.str(sk); GuestSend(w); }
        }
    }
    if (lobby != LB_HOST || g_lobbySteam || now - lastPing < 2000) return;
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

// --- dessin du salon (page SALON) : joueurs a gauche ; a droite la partie, l'envoi, le conseil et les boutons
static float LobbyMaxScroll()
{
    if (g_lobbySteam && g_sFriendsView) return SteamFriendsMaxScroll();
    EnterCriticalSection(&g_lcs);
    int n = (int)g_peers.size();
    LeaveCriticalSection(&g_lcs);
    int rows = n + (n < kLobbyMax ? 1 : 0);
    return max(0.0f, rows * kLobbyRowH - kLobbyList.Height);
}
// 0/1 : partie ; 50 retour au salon, 51 "inviter des amis" (place libre, salon Steam), 100+i ami i ;
// 70 : TOUT ACCEPTER / REESSAYER (invite), 71 : CHOISIR (hote, page PARTAGE).
static int LobbyChoiceAt(float x, float y)
{
    if ((g_lobby == LB_HOST || g_lobby == LB_GUEST) && !(g_lobbySteam && g_sFriendsView))
        if (int sb = SyncBtnAt(x, y)) return 69 + sb;
    if (g_lobby != LB_HOST || g_goSent) return -1;
    if (g_lobbySteam && g_sFriendsView) {
        if (kFriendsBack.Contains(x, y)) return 50;
        int i = SteamFriendAt(x, y);
        if (i >= 0 && !SteamInLobby(g_sFriends[i].id)) return 100 + i;
    } else if (g_lobbySteam && kLobbyList.Contains(x, y)) {
        EnterCriticalSection(&g_lcs);
        int n = (int)g_peers.size();
        LeaveCriticalSection(&g_lcs);
        if (n < kLobbyMax && (int)((y - kLobbyList.Y + g_scroll[TAB_LOBBY]) / kLobbyRowH) == n) return 51;
    }
    for (int i = 0; i < 2; i++) if (kChoiceR[i].Contains(x, y)) return i;
    return -1;
}

static void DrawLobbyPopover(Graphics &g);
static void DrawLobby(Graphics &g)
{
    std::vector<LobbyPeer> peers;
    EnterCriticalSection(&g_lcs);
    peers = g_peers;
    LeaveCriticalSection(&g_lcs);
    int lobby = g_lobby, partie = g_partie;
    bool host = lobby == LB_HOST;
    SkinsCheck();   // (portraits des joueurs)
    PersoCheck();
    int myUdp = g_udpMine;
    RectF lc = kLobbyCard;
    Glass(lc);
    // UDP bloque (ou port UDP pris chez l'hote) : avertissement en bas de la liste
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
    float bottom = lc.Y + lc.Height - 18;
    if (!warn.empty()) {
        RectF wr(lc.X + 20, bottom - 66, lc.Width - 40, 66);
        GraphicsPath p; RoundRect(p, wr, 14);
        SolidBrush fb(WithA(kWarn, 0.14f)); g.FillPath(&fb, &p);
        Pen pen(WithA(kWarn, 0.45f), 1); g.DrawPath(&pen, &p);
        Icon(g, IC_ALERT, wr.X + 24, wr.Y + 24, 20, kWarn, 2.0f);
        Para(g, warn, RectF(wr.X + 46, wr.Y + 8, wr.Width - 60, wr.Height - 12), 12.5f, kInk);
        bottom -= 78;
    }
    float top = lc.Y + (host && g_lobbySteam && g_sFriendsView ? 76 : 60);
    kLobbyList = RectF(lc.X + 20, top, lc.Width - 40, bottom - top);
    if (host && g_lobbySteam && g_sFriendsView) DrawSteamFriends(g);
    else {
        Title(g, T(L"Joueurs", L"Players"), RectF(lc.X + 22, lc.Y + 18, 300, 26), 17, kInk);
        std::wstring udpLine;
        Color udpC = kGrey;
        if (lobby == LB_GUEST) {   // test UDP de l'invite
            if (myUdp == UDP_OK) { udpLine = T(L"UDP : OK \u2713", L"UDP: OK \u2713"); udpC = kOk; }
            else if (myUdp == UDP_FAIL && !g_hostUdpTest) udpLine = T(L"UDP : non v\u00E9rifi\u00E9 (lanceur de l'h\u00F4te ancien)", L"UDP: not checked (host has an old launcher)");
            else if (myUdp == UDP_FAIL) { udpLine = T(L"UDP : bloqu\u00E9", L"UDP: blocked"); udpC = kRed; }
            else if (myUdp == UDP_WAIT) udpLine = T(L"UDP : test en cours\u2026", L"UDP: testing\u2026");
        }
        if (!udpLine.empty()) Text(g, udpLine, RectF(lc.X + lc.Width - 320, lc.Y + 18, 300, 26), 12.5f, FontStyleBold, udpC, StringAlignmentFar);
        else if (lobby != LB_CONNECTING) Text(g, T(L"Clique sur toi : tenue, voiture", L"Click yourself: outfit, car"), RectF(lc.X + lc.Width - 320, lc.Y + 18, 300, 26), 12.5f, FontStyleRegular, kGrey, StringAlignmentFar);
        if (lobby == LB_CONNECTING) {
            bool creating = g_lobbySteam && g_myId == 0;
            float cy = kLobbyList.Y + kLobbyList.Height / 2 - 60;
            Title(g, creating ? T(L"Cr\u00E9ation du salon Steam\u2026", L"Creating the Steam lobby\u2026") : T(L"Connexion au salon\u2026", L"Connecting to the lobby\u2026"),
                  RectF(lc.X, cy, lc.Width, 34), 22, kInk, StringAlignmentCenter);
            DrawBar(g, RectF(lc.X + 170, cy + 50, lc.Width - 340, 5), -2);
            Para(g, creating ? T(L"Un salon r\u00E9serv\u00E9 \u00E0 tes amis Steam : tu pourras les inviter d'ici.", L"A lobby for your Steam friends only: you can invite them from here.")
                             : T(L"Si l'h\u00F4te joue d\u00E9j\u00E0 ou n'a pas de salon, tu pourras rejoindre directement en jeu.", L"If the host is already playing or has no lobby, you can join directly in game."),
                 RectF(lc.X + 100, cy + 72, lc.Width - 200, 44), 13, kGrey, StringAlignmentCenter);
        } else {
            static const int pal[kLobbyMax] = { 0x3E86D0, 0xE0702A, 0x2A9C9A, 0x8E5BD6, 0xD64545, 0xC9971A, 0xD45D9A, 0x6C7A89 };
            float sc = g_scroll[TAB_LOBBY] = min(g_scroll[TAB_LOBBY], LobbyMaxScroll()), ms = LobbyMaxScroll();
            int rows = (int)peers.size() + ((int)peers.size() < kLobbyMax ? 1 : 0);
            GraphicsState st = g.Save();
            g.SetClip(kLobbyList, CombineModeIntersect);
            for (int i = 0; i < rows; i++) {
                RectF r(kLobbyList.X, kLobbyList.Y + i * kLobbyRowH - sc, kLobbyList.Width - (ms > 0 ? 12 : 0), kLobbyRowH - 8);
                if (r.Y + r.Height < kLobbyList.Y || r.Y > kLobbyList.Y + kLobbyList.Height) continue;
                GraphicsPath rp;
                RoundRect(rp, r, 18);
                if (i >= (int)peers.size()) {   // place libre (salon Steam, hote : inviter des amis)
                    bool inv = host && g_lobbySteam, hot = inv && g_lobbyHot == 51;
                    if (hot) { SolidBrush hb(TH(card)); g.FillPath(&hb, &rp); }
                    Pen dash(inv ? WithA(kAcc, hot ? 1.0f : 0.7f) : TH(fieldBorder), 1.4f);
                    dash.SetDashStyle(DashStyleDash);
                    g.DrawPath(&dash, &rp);
                    if (inv) { Icon(g, IC_PLUS, r.X + r.Width / 2 - 96, r.Y + r.Height / 2, 18, kAcc, 2.2f); Text(g, T(L"Inviter des amis Steam", L"Invite Steam friends"), RectF(r.X + r.Width / 2 - 80, r.Y, 240, r.Height), 14, FontStyleBold, kAcc, StringAlignmentNear); }
                    else Text(g, T(L"En attente d'un joueur\u2026", L"Waiting for a player\u2026"), r, 14, FontStyleRegular, kGrey);
                    continue;
                }
                const LobbyPeer &p = peers[i];
                bool me = p.id == g_myId;
                SolidBrush rb(me ? WithA(kAcc, 0.10f) : TH(card));
                g.FillPath(&rb, &rp);
                Pen rpen(me ? kAcc : TH(choiceBorder), me ? 1.5f : 1.0f);
                g.DrawPath(&rpen, &rp);
                std::wstring nm = Widen(p.name, CP_UTF8);
                Color pc = CarRgb(pal[p.id % kLobbyMax]);
                DrawSkinAvatar(g, RectF(r.X + 12, r.Y + (r.Height - 54) / 2, 54, 54), p.skin, pc, nm);
                if (p.sid) DrawSteamAvatar(g, RectF(r.X + 50, r.Y + r.Height - 30, 22, 22), p.sid);   // (avatar Steam en coin)
                Text(g, nm + (me ? T(L"  (toi)", L"  (you)") : L""), RectF(r.X + 80, r.Y + 13, 210, 24), 16, FontStyleBold, kInk, StringAlignmentNear);
                std::wstring line = SkinLabel(p.skin) + L" \u00B7 " + (p.ver == "dev" ? std::wstring(L"dev") : L"v" + Widen(p.ver, CP_UTF8));
                Text(g, line, RectF(r.X + 80, r.Y + 38, me ? 206.0f : 300.0f, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
                if (me) {   // sa tenue, sa voiture
                    HotZone(r, UI_LOBBY_ME_LOOK);
                    for (int k = 0; k < 2; k++) {
                        RectF br(r.X + 296 + k * 52, r.Y + (r.Height - 44) / 2, 44, 44);
                        int id = k ? UI_LOBBY_ME_CAR : UI_LOBBY_ME_LOOK;
                        GraphicsPath bp; RoundRect(bp, br, 12);
                        SolidBrush bb(g_uiHot == id ? TH(btn2Hot) : TH(btn2)); g.FillPath(&bb, &bp);
                        if (k == 0) Icon(g, IC_SHIRT, br.X + 22, br.Y + 22, 19, kInk, 1.9f);
                        else {
                            RectF d(br.X + 14, br.Y + 14, 16, 16);
                            if (g_carColor >= 0) { SolidBrush cb(CarRgb(g_carColor)); g.FillEllipse(&cb, d); } else DrawCarWheel(g, d);
                            Pen e(Color(90, 255, 255, 255), 1.2f); g.DrawEllipse(&e, d);
                        }
                        HotZone(br, id);
                    }
                }
                // etat : HOTE, PRET, PAS PRET
                const wchar_t *stl = p.id == 0 ? T(L"H\u00D4TE", L"HOST") : p.ready ? T(L"PR\u00CAT \u2713", L"READY \u2713") : T(L"PAS PR\u00CAT", L"NOT READY");
                RectF pr(r.X + r.Width - 112, r.Y + (r.Height - 32) / 2, 96, 32);
                GraphicsPath pp; RoundRect(pp, pr, 16);
                if (p.id != 0 && p.ready) { SolidBrush ob(kOk); g.FillPath(&ob, &pp); }
                else if (p.id == 0) { SolidBrush hb(TH(cardSel)); g.FillPath(&hb, &pp); }
                else { Pen np(TH(fieldBorder), 1.4f); g.DrawPath(&np, &pp); }
                Text(g, stl, pr, 11.5f, FontStyleBold, p.id != 0 && p.ready ? Color(255, 4, 38, 26) : p.id == 0 ? kInk : kGrey);
                // ping, UDP
                RectF info(pr.X - 100, r.Y, 88, r.Height);
                if (p.id != 0 && g_lobbySteam) Text(g, L"Steam", info, 12.5f, FontStyleRegular, kGrey, StringAlignmentFar);
                else if (p.id != 0) {
                    wchar_t pb[32];
                    swprintf_s(pb, L"%d ms", p.ping);
                    int udp = me && myUdp == UDP_OK ? UDP_OK : p.udp;   // (l'invite local : sa propre sonde compte)
                    if (me && udp != UDP_OK && myUdp == UDP_FAIL && g_hostUdpTest) udp = UDP_FAIL;
                    if (udp == UDP_NA) Text(g, pb, info, 12.5f, FontStyleRegular, kGrey, StringAlignmentFar);
                    else {
                        Text(g, pb, RectF(info.X, r.Y + r.Height / 2 - 21, info.Width, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentFar);
                        const wchar_t *ul = udp == UDP_OK ? L"UDP \u2713" : udp == UDP_FAIL ? T(L"UDP bloqu\u00E9", L"UDP blocked") : L"UDP \u2026";
                        Text(g, ul, RectF(info.X - 20, r.Y + r.Height / 2 + 1, info.Width + 20, 20), 12, FontStyleBold, udp == UDP_OK ? kOk : udp == UDP_FAIL ? kWarn : kGrey, StringAlignmentFar);
                    }
                }
            }
            g.Restore(st);
            if (ms > 0) {
                float h = kLobbyList.Height * kLobbyList.Height / (kLobbyList.Height + ms), y = kLobbyList.Y + (kLobbyList.Height - h) * sc / ms;
                GraphicsPath sp; RoundRect(sp, RectF(kLobbyList.X + kLobbyList.Width - 5, y, 4, h), 2);
                SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
            }
        }
    }

    // --- a droite
    const float rx = 924, rw = 340;
    RectF pc(rx, 88, rw, 236);
    Glass(pc);
    Title(g, host ? T(L"Partie", L"Game") : T(L"Partie (choisie par l'h\u00F4te)", L"Game (chosen by the host)"), RectF(pc.X + 20, pc.Y + 16, pc.Width - 40, 24), 17, kInk);
    const wchar_t *lab[2] = { host ? T(L"Continuer ma partie", L"Continue my game") : T(L"Continuer la partie", L"Continue the game"), T(L"Nouvelle partie", L"New game") };
    const wchar_t *sub[2] = { host ? T(L"Ta sauvegarde, envoy\u00E9e aux invit\u00E9s", L"Your save, sent to the guests") : T(L"La sauvegarde de l'h\u00F4te", L"The host's save"),
                              host ? T(L"Remplace ta sauvegarde", L"Replaces your save") : T(L"Une partie neuve chez l'h\u00F4te", L"A fresh game at the host's") };
    for (int i = 0; i < 2; i++) {
        RectF r = kChoiceR[i];
        bool on = partie == i, hot = g_lobbyHot == i && host;
        float a = lobby == LB_CONNECTING ? 0.45f : 1.0f;
        GraphicsPath cp; RoundRect(cp, r, 14);
        SolidBrush cb(on ? WithA(kAcc, 0.14f * a) : hot ? TH(cardSel) : TH(card)); g.FillPath(&cb, &cp);
        Pen cpen(on ? WithA(kAcc, a) : TH(choiceBorder), on ? 1.5f : 1.0f); g.DrawPath(&cpen, &cp);
        RectF dot(r.X + 14, r.Y + r.Height / 2 - 9, 18, 18);
        if (on) { Pen rp(WithA(kAcc, a), 5); g.DrawEllipse(&rp, dot.X + 2.5f, dot.Y + 2.5f, 13.0f, 13.0f); }
        else { Pen rp(TH(fieldBorder), 1.5f); g.DrawEllipse(&rp, dot); }
        Text(g, lab[i], RectF(r.X + 44, r.Y + 8, r.Width - 56, 22), 14, FontStyleBold, WithA(host || on ? kInk : kGrey, a), StringAlignmentNear);
        Text(g, sub[i], RectF(r.X + 44, r.Y + 29, r.Width - 56, 18), 12, FontStyleRegular, WithA(kGrey, a), StringAlignmentNear);
    }
    std::wstring note;
    if (partie == PARTIE_NOUVELLE) note = host ? std::wstring(T(L"Couleur de la CORRIS : ", L"CORRIS color: ")) + CarColorName() + L"."
                                               : T(L"Tu la rejoins d\u00E8s que l'h\u00F4te est en jeu.", L"You join as soon as the host is in game.");
    else note = host ? T(L"Tes invit\u00E9s la re\u00E7oivent en arrivant.", L"Your guests receive it when they arrive.")
                     : T(L"Tu la re\u00E7ois en arrivant ; la tienne n'est pas touch\u00E9e.", L"You receive it on arrival; yours is left untouched.");
    if (lobby != LB_CONNECTING) Para(g, note, RectF(pc.X + 20, pc.Y + 178, pc.Width - 40, 44), 12.5f, partie == PARTIE_NOUVELLE && host ? kWarn : kGrey);
    // envoi (mods, contenu)
    RectF ec(rx, 340, rw, 150);
    Glass(ec);
    DrawSyncCard(g, ec, g_lobbyHot == 70 ? 1 : g_lobbyHot == 71 ? 2 : 0);
    // conseil
    RectF hc(rx, 506, rw, 90);
    Glass(hc);
    const wchar_t *hint = host && g_lobbySteam ? T(L"Invite tes amis Steam. Quand tout le monde est pr\u00EAt, LANCER d\u00E9marre le jeu de chacun.",
                                                 L"Invite your Steam friends. Once everyone is ready, START launches everyone's game.")
                        : host ? T(L"Quand tout le monde est pr\u00EAt, LANCER d\u00E9marre le jeu de chacun ; les invit\u00E9s suivent ta partie.",
                                   L"Once everyone is ready, START launches everyone's game; the guests follow your game.")
                               : T(L"Clique sur PR\u00CAT. Ton jeu d\u00E9marre tout seul quand l'h\u00F4te lance la partie.",
                                   L"Click READY. Your game starts by itself when the host starts the session.");
    Icon(g, IC_SPARK, hc.X + 26, hc.Y + 28, 18, kAcc, 1.9f);
    Para(g, hint, RectF(hc.X + 46, hc.Y + 14, hc.Width - 62, hc.Height - 20), 12.5f, kInk);
    // boutons : LANCER / PRET ; FERMER LE SALON / QUITTER / ANNULER
    for (int id : { B_HOST, B_JOIN }) {
        Button &b = g_btn[id];
        bool main_ = id == B_HOST;
        b.r = main_ ? RectF(rx, 612, rw, 72) : RectF(rx, 696, rw, 48);
        RectF r = b.r;
        if (g_pressed == id && g_hot == id) r.Offset(0, 1);
        float a = b.enabled ? 1.0f : 0.4f;
        GraphicsPath p; RoundRect(p, r, main_ ? 20.0f : 16.0f);
        std::wstring l1, l2;
        if (main_) {
            if (lobby == LB_HOST) {
                int guests = 0, notReady = 0;
                GuestCounts(&guests, &notReady);
                l1 = T(L"LANCER LA PARTIE", L"START THE GAME");
                wchar_t b2[64];
                if (!guests) swprintf_s(b2, L"%s", T(L"Seul pour l'instant", L"Alone for now"));
                else swprintf_s(b2, T(L"%d / %d pr\u00EAt(s)", L"%d / %d ready"), guests - notReady, guests);
                l2 = b2;
            } else if (lobby == LB_GUEST) { l1 = g_meReady ? T(L"PR\u00CAT \u2713", L"READY \u2713") : T(L"PR\u00CAT ?", L"READY?"); l2 = g_meReady ? T(L"Clique pour annuler", L"Click to cancel") : T(L"Clique quand tu es pr\u00EAt", L"Click when you're ready"); }
            else l1 = T(L"CONNEXION\u2026", L"CONNECTING\u2026");
            bool ready = lobby == LB_GUEST && g_meReady;
            SolidBrush fb(WithA(ready ? kOk : kAcc, a)); g.FillPath(&fb, &p);
            if (b.hover > 0.01f) { SolidBrush hb(Color((BYTE)(55 * b.hover), 255, 255, 255)); g.FillPath(&hb, &p); }
            Color ink = WithA(ready ? Color(255, 4, 38, 26) : kOnAcc, a);
            if (l2.empty()) TextF(g, TitleFont(), l1, r, 20, FontStyleBold, ink);
            else {
                TextF(g, TitleFont(), l1, RectF(r.X, r.Y + 10, r.Width, 30), 20, FontStyleBold, ink);
                Text(g, l2, RectF(r.X, r.Y + 40, r.Width, 20), 12.5f, FontStyleBold, WithA(ink, 0.75f));
            }
        } else {
            l1 = lobby == LB_HOST ? T(L"FERMER LE SALON", L"CLOSE THE LOBBY") : lobby == LB_GUEST ? T(L"QUITTER LE SALON", L"LEAVE THE LOBBY") : T(L"ANNULER", L"CANCEL");
            SolidBrush fb(Mix(TH(btn2), TH(btn2Hot), b.hover)); g.FillPath(&fb, &p);
            Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p);
            Text(g, l1, r, 13.5f, FontStyleBold, WithA(kInk, a));
        }
    }
    if (g_lobbyOpts) DrawLobbyPopover(g);
}

// En-tete du salon : engrenage (options du salon), et pour l'hote d'un salon Steam : inviter des amis.
static void DrawLobbyHeadControls(Graphics &g, float &right)
{
    if (g_lobby == LB_CONNECTING) return;
    RectF gr(right - 44, 22, 44, 44);
    bool on = g_lobbyOpts, hot = g_uiHot == UI_LOBBY_GEAR;
    GraphicsPath p; RoundRect(p, gr, 14);
    SolidBrush b(on ? WithA(kAcc, 0.25f) : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
    Pen pen(on ? kAcc : TH(fieldBorder), 1); g.DrawPath(&pen, &p);
    Icon(g, IC_GEAR, gr.X + 22, gr.Y + 22, 20, kInk, 1.9f);
    HotZone(gr, UI_LOBBY_GEAR);
    g_lobbyGearR = gr;
    right -= 54;
    if (g_lobby == LB_HOST && g_lobbySteam && !g_goSent) {
        bool fv = g_sFriendsView;
        std::wstring lab = fv ? T(L"Retour au salon", L"Back to the lobby") : T(L"Inviter des amis", L"Invite friends");
        float w = PillW(lab, 13.5f) + 40;
        UiButton(g, RectF(right - w, 22, w, 44), lab, UI_LOBBY_INVITE, !fv, fv ? IC_CHEVL : IC_PLUS);
        right -= w + 10;
    }
}

// Menu de l'engrenage (options du salon), sous l'engrenage.
static void DrawLobbyPopover(Graphics &g)
{
    bool ip = !g_lobbySteam, host = g_lobby == LB_HOST;
    int rows = 2 + (ip ? 1 : 0) + (host ? 1 : 0);
    RectF r(g_lobbyGearR.X + g_lobbyGearR.Width - 360, 76, 360, 64 + rows * 58.0f);
    g_lobbyPopR = r;
    GlassLive(g, r, 20);
    HotZone(r, UI_NONE);
    {   // petite pointe vers l'engrenage
        float ax = g_lobbyGearR.X + g_lobbyGearR.Width / 2;
        PointF tri[] = { PointF(ax - 8, r.Y + 1), PointF(ax + 8, r.Y + 1), PointF(ax, r.Y - 7) };
        SolidBrush tb(g_dark ? Color(235, 30, 44, 84) : Color(240, 255, 255, 255)); g.FillPolygon(&tb, tri, 3);
    }
    Title(g, T(L"Options du salon", L"Lobby options"), RectF(r.X + 20, r.Y + 16, 260, 26), 16, kInk);
    {
        RectF x(r.X + r.Width - 48, r.Y + 12, 32, 32);
        GraphicsPath p; RoundRect(p, x, 10);
        SolidBrush b(g_uiHot == UI_POP_X ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
        Icon(g, IC_CLOSE, x.X + 16, x.Y + 16, 14, kInk, 2.4f);
        HotZone(x, UI_POP_X);
    }
    float y = r.Y + 56;
    Pen sep(TH(sep), 1);
    if (const Opt *fl = OptByKey("FermerLanceur")) {
        RectF row(r.X + 20, y, r.Width - 40, 58);
        SetRowText(g, RectF(row.X, row.Y, row.Width - 64, row.Height), T(L"Fermer le lanceur au lancement", L"Close the launcher at launch"), T(L"Sinon il revient au menu \u00E0 la fin", L"Otherwise it comes back at the end"));
        UiToggle(g, RectF(row.X + row.Width - 50, row.Y + 15, 50, 28), OptGet(*fl) != 0, g_uiHot == UI_POP_CLOSE);
        HotZone(row, UI_POP_CLOSE);
        y += 58; g.DrawLine(&sep, r.X + 20, y, r.X + r.Width - 20, y);
    }
    if (ip) if (const Opt *po = OptByKey("Port")) {
        RectF row(r.X + 20, y, r.Width - 40, 58);
        SetRowText(g, RectF(row.X, row.Y, row.Width - 150, row.Height), L"Port", T(L"Pris au prochain salon", L"Used by the next lobby"));
        RectF m(row.X + row.Width - 128, row.Y + 13, 32, 32), pl(row.X + row.Width - 32, row.Y + 13, 32, 32);
        for (int k = 0; k < 2; k++) {
            RectF br = k ? pl : m; int id = k ? UI_POP_PORTP : UI_POP_PORTM;
            GraphicsPath bp; RoundRect(bp, br, 9);
            SolidBrush bb(g_uiHot == id ? TH(btn2Hot) : TH(btn2)); g.FillPath(&bb, &bp);
            Icon(g, k ? IC_PLUS : IC_MIN, br.X + 16, br.Y + 16, 15, kInk, 2.3f);
            HotZone(br, id);
        }
        Text(g, std::to_wstring(OptGet(*po)), RectF(m.X + m.Width, row.Y, pl.X - m.X - m.Width, row.Height), 14.5f, FontStyleBold, kInk);
        y += 58; g.DrawLine(&sep, r.X + 20, y, r.X + r.Width - 20, y);
    }
    struct Link { int id; const wchar_t *t, *d; } links[2] = {
        { UI_POP_LOOK, T(L"Ma tenue et ma voiture", L"My outfit and car"), T(L"Ce que les autres voient", L"What the others see") },
        { UI_POP_CONTENT, T(L"Contenu et mods envoy\u00E9s", L"Sent content and mods"), T(L"CD, peinture, posters, mods\u2026", L"CDs, paint, posters, mods\u2026") } };
    for (int k = 0; k < (host ? 2 : 1); k++) {
        RectF row(r.X + 20, y, r.Width - 40, 58);
        if (g_uiHot == links[k].id) { GraphicsPath hp; RoundRect(hp, RectF(row.X - 8, row.Y + 4, row.Width + 16, row.Height - 8), 12); SolidBrush hb(TH(card)); g.FillPath(&hb, &hp); }
        SetRowText(g, RectF(row.X, row.Y, row.Width - 30, row.Height), links[k].t, links[k].d);
        Icon(g, IC_CHEVR, row.X + row.Width - 10, row.Y + 29, 18, kInk, 2.2f);
        HotZone(row, links[k].id);
        y += 58;
        if (k == 0 && host) g.DrawLine(&sep, r.X + 20, y, r.X + r.Width - 20, y);
    }
}

static void LobbyOptsClick(int id)
{
    switch (id) {
    case UI_LOBBY_GEAR: g_lobbyOpts = !g_lobbyOpts; break;
    case UI_LOBBY_INVITE: SteamShowFriends(!g_sFriendsView); break;
    case UI_POP_X: g_lobbyOpts = false; break;
    case UI_POP_CLOSE: if (const Opt *o = OptByKey("FermerLanceur")) OptSet(*o, OptGet(*o) ? 0 : 1); break;
    case UI_POP_PORTM: case UI_POP_PORTP: for (int i = 0; i < (int)g_opts.size(); i++) if (!strcmp(g_opts[i].key, "Port")) OptStep(i, id == UI_POP_PORTP ? 1 : -1); break;
    case UI_POP_LOOK: case UI_LOBBY_ME_LOOK: GoPage(TAB_SKIN); break;
    case UI_LOBBY_ME_CAR: GoPage(TAB_CAR); break;
    case UI_POP_CONTENT: GoPage(TAB_CONTENT); break;
    }
}

static bool LobbyClick(float x, float y)
{
    int c = LobbyChoiceAt(x, y);
    if (c >= 100) { SteamInviteFriend(c - 100); return true; }
    if (c == 50 || c == 51) { SteamShowFriends(c == 51); return true; }
    if (c == 70) { if (g_sync == SY_FAIL) SyncRetry(); else SyncAccept(); return true; }
    if (c == 71) { GoPage(TAB_CONTENT); return true; }
    if (c >= 0) { LobbySetPartie(c); return true; }
    return false;
}

// /testsalon hote|invite <journal> /steam <fichier> : le meme salon par Steam (Steam ouvert sur ce PC). L'hote cree le
// salon et ecrit son numero dans <fichier> ; l'invite le lit, y entre et se met pret ; l'hote lance des qu'il le voit
// (un seul compte pour les deux : l'invite est le meme membre du salon, son "pret" suffit).
static void TestSteamStep(DWORD t)
{
    static bool tried, readied, avLogged, friendsLogged;
    static DWORD readySince;
    if (g_testSalon == L"hote") {
        if (!tried && t > 500) {
            tried = true;
            DeleteFileW(g_testSteamFile.c_str());
            SteamLobbyHost();
            if (g_lobby == LB_NONE) { TestLog("test : fin (salon Steam impossible : %s)", Narrow(g_steamWhy, CP_UTF8).c_str()); DestroyWindow(g_wnd); }
            return;
        }
        if (tried && g_lobby == LB_NONE) { TestLog("test : fin (salon Steam ferme)"); DestroyWindow(g_wnd); return; }
        if (g_lobby != LB_HOST) return;
        if (!friendsLogged) {
            friendsLogged = true;
            SteamFriendsRefresh();
            int mwc = 0;
            for (auto &f : g_sFriends) mwc += f.mwc;
            TestLog("test : %d ami(s) en ligne (%d dans My Winter Car), %d hors ligne", (int)g_sFriends.size(), mwc, g_sOffline);
        }
        if (!avLogged) if (Bitmap *b = SteamAvatar(g_steamMe)) { avLogged = true; TestLog("test : mon avatar Steam %ux%u", b->GetWidth(), b->GetHeight()); }
        int guests, notReady;
        GuestCounts(&guests, &notReady);
        bool can = SMemberData(g_steamMe, "pret") == "1" || (guests >= 1 && notReady == 0);
        if (!can) readySince = 0;
        else if (!readySince) { readySince = GetTickCount(); TestLog("test : invite pret (%d autre(s) membre(s))", guests); }
        else if (GetTickCount() - readySince > 1500) { TestLog("test : LANCER"); HostStart(); }
    } else {
        if (!tried && t > 2000) {
            unsigned long long id = 0;
            FILE *f = _wfopen(g_testSteamFile.c_str(), L"rb");
            if (f) { if (fscanf(f, "%llu", &id) != 1) id = 0; fclose(f); }
            if (id) { tried = true; TestLog("test : salon %llu lu", id); SteamLobbyJoin(id); }
            return;
        }
        if (tried && g_lobby == LB_NONE) {
            EnterCriticalSection(&g_cs);
            std::string st = Narrow(g_status, CP_UTF8);
            LeaveCriticalSection(&g_cs);
            TestLog("test : fin cote invite -> \"%s\" (rejoindre en jeu propose=%d)", st.c_str(), (int)g_joinFallback);
            DestroyWindow(g_wnd);
            return;
        }
        if (g_lobby == LB_GUEST && !readied && t > 4500) { readied = true; TestLog("test : clic sur PRET"); GuestToggleReady(); }
    }
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
    if (!g_testSteamFile.empty()) { TestSteamStep(t); return; }
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
        int sy = g_sync;   // mods de l'hote : tout accepter, pret une fois recus
        if (g_lobby == LB_GUEST && sy == SY_ASK) { TestLog("test : clic sur TOUT ACCEPTER"); SyncAccept(); }
        if (g_lobby == LB_GUEST && !readied && t > 4500 && sy != SY_ASK && sy != SY_CHECK && sy != SY_GET) { readied = true; TestLog("test : clic sur PRET"); GuestToggleReady(); }
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
    for (auto &e : g_logList) if (e.group == 0 && !g_logGroups.empty() && g_logGroups[0].key == L"*") add(e.path);
    if (!g_gameDir.empty()) { add(g_gameDir + L"MWCoop\\mwcoop.ini"); add(g_gameDir + L"MWCoop\\lancement.ini"); }
    if (!g_launcherLog.empty()) add(g_launcherLog);
    if (!LocalDir().empty()) add(LocalDir() + L"lanceur-precedent.log");
    // Les 2 parties rangees les plus recentes : un joueur relance souvent le jeu avant de faire le zip (retour du 08/10 :
    // la partie ou MSCLoader ne trouvait aucun mod n'y etait plus).
    std::vector<int> older;
    for (int i = 0; i < (int)g_logGroups.size(); i++) if (g_logGroups[i].key != L"*") older.push_back(i);
    std::sort(older.begin(), older.end(), [](int x, int y) { return CompareFileTime(&g_logGroups[x].end, &g_logGroups[y].end) > 0; });
    for (int k = 0; k < (int)older.size() && k < 2; k++)
        for (auto &e : g_logList) if (e.group == older[k]) add(e.path);
    // MSCLoader : reglages du lanceur (MSCLoader officiel ou de MWCoop, coupe), Doorstop et journal du prechargeur, dans
    // le jeu et dans chaque copie de lancement.
    if (!g_iniLauncher.empty()) add(g_iniLauncher);
    std::vector<std::wstring> dirs = { g_gameDir, MirrorDir(), MscCopyDir(), GuestCopyDir() };
    for (const std::wstring &d : dirs) if (!d.empty()) { add(d + L"doorstop_config.ini"); add(d + L"MSCLoader_Preloader.txt"); add(d + L"output_log.txt"); }   // (output_log : MSCLoader le met a cote de l'exe, -logFile)
    if (!LocalDir().empty()) add(LocalDir() + L"mscloader-mwcoop\\version.txt");
    return files;
}
// Zip sur le Bureau (sinon %LOCALAPPDATA%\MWCoop), montre dans l'explorateur. 'tag' : suffixe du nom (date de la partie).
static void ZipToDesktop(const std::vector<std::wstring> &files, const std::wstring &tag);

// Bouton "Creer un zip a envoyer" : MWCoop-journaux-<date>.zip sur le Bureau, montre dans l'explorateur.
static void LogsZip()
{
    std::vector<std::wstring> files = ZipFiles();
    if (files.empty()) { SetStatus(K_WARN, T(L"Aucun journal pour l'instant : lance le jeu avec MWCoop d'abord", L"No logs yet: start the game with MWCoop first")); return; }
    ZipToDesktop(files, L"");
}

// Zip d'une seule partie (bouton de son en-tete) : ses journaux, et pour la derniere, mwcoop.ini et le lanceur.
static void LogsGroupZip(int gi)
{
    if (gi < 0 || gi >= (int)g_logGroups.size()) return;
    const LogGroup &gr = g_logGroups[gi];
    if (gr.key == L"*") { LogsZip(); return; }
    std::vector<std::wstring> files;
    for (int i : gr.items) files.push_back(g_logList[i].path);
    if (!g_gameDir.empty()) files.push_back(g_gameDir + L"MWCoop\\mwcoop.ini");
    std::wstring tag = gr.dir.substr(0, gr.dir.size() - 1);
    tag = L"partie-" + tag.substr(tag.find_last_of(L'\\') + 1);
    ZipToDesktop(files, tag);
}

// Fichiers ou dossiers a la corbeille de Windows (rien d'efface pour de bon).
static bool ToRecycleBin(const std::vector<std::wstring> &paths)
{
    if (paths.empty()) return true;
    std::wstring list;
    for (auto &p : paths) { std::wstring q = p; if (!q.empty() && q.back() == L'\\') q.pop_back(); list += q; list.push_back(L'\0'); }
    list.push_back(L'\0');
    SHFILEOPSTRUCTW op = {};
    op.hwnd = g_wnd;
    op.wFunc = FO_DELETE;
    op.pFrom = list.c_str();
    op.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI;
    return SHFileOperationW(&op) == 0 && !op.fAnyOperationsAborted;
}

// ---------------------------------------------------------------- desinstallation
// Reglages > A propos (demande d'un joueur, 08/10 : « une option pour tout desinstaller, dossiers compris ») : tout
// MWCoop part a la Corbeille -- dans le dossier du jeu (version.dll, dossier MWCoop : profils, journaux, contenu recu ;
// le lanceur et ses fichiers), tout %LOCALAPPDATA%\MWCoop (copies de lancement, MSCLoader de MWCoop, caches) -- et les
// reglages des profils dans le registre (HKCU\Software\MWCoop-Profils). Jamais : le MSCLoader officiel (demande de JD),
// le dossier Mods, la sauvegarde du jeu. Les copies de lancement ont des jonctions vers le vrai dossier du jeu
// (mywintercar_Data, Mods...) : retirees une a une d'abord, sans jamais entrer dedans.
static int RemoveJunctionsIn(const std::wstring &dir)
{
    int n = 0;
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((dir + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return 0;
    do {
        std::wstring name = fd.cFileName;
        if (name == L"." || name == L".." || !(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) continue;
        std::wstring p = dir + name;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) {
            if (RemoveDirectoryW(p.c_str())) n++;   // (le lien seulement : sa cible n'est pas touchee)
            else LaunchLog("desinstallation : lien %s impossible a retirer (erreur %lu)", Narrow(p, CP_UTF8).c_str(), GetLastError());
        } else n += RemoveJunctionsIn(p + L"\\");
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return n;
}

// Ce qui part (chemins existants) ; registry : aussi les reglages des profils. Faux si quelque chose est reste.
static bool UninstallCore(bool registry, std::wstring *left)
{
    std::vector<std::wstring> paths;
    auto add = [&](const std::wstring &p) { if (!p.empty() && GetFileAttributesW(p.c_str()) != INVALID_FILE_ATTRIBUTES) { for (auto &q : paths) if (!_wcsicmp(q.c_str(), p.c_str())) return; paths.push_back(p); } };
    if (!g_gameDir.empty()) {
        const wchar_t *files[] = { L"version.dll", L"version.dll.old", L"MWCoop.exe", L"MWCoop.exe.old", L"mwcoop-lanceur.ini", L"LISEZMOI.txt", L"README.txt", L"steam_appid.txt" };
        for (const wchar_t *f : files) add(g_gameDir + f);
        add(g_gameDir + L"MWCoop");
    }
    add(g_iniLauncher);
    add(g_self);
    add(g_self + L".old");
    std::wstring ld = LocalDir();
    int links = 0;
    if (!ld.empty() && GetFileAttributesW(ld.c_str()) != INVALID_FILE_ATTRIBUTES) {
        links = RemoveJunctionsIn(ld);
        add(ld.substr(0, ld.size() - 1));
    }
    LaunchLog("desinstallation : %d lien(s) retire(s), %d element(s) a la Corbeille", links, (int)paths.size());
    g_launcherLog.clear();   // (son dossier part : plus rien n'y est ecrit)
    for (auto &p : paths) ToRecycleBin({ p });   // (un par un : un echec, le lanceur en cours par exemple, n'arrete pas les autres)
    if (registry) RegDeleteTreeW(HKEY_CURRENT_USER, L"Software\\MWCoop-Profils");
    bool ok = true;
    for (auto &p : paths) {
        if (GetFileAttributesW(p.c_str()) == INVALID_FILE_ATTRIBUTES) continue;
        if (!_wcsicmp(p.c_str(), g_self.c_str())) continue;   // (le lanceur en cours : apres sa fermeture, ci-dessous)
        ok = false;
        if (left) *left += L"\n" + p;
    }
    return ok;
}

static void Uninstall()
{
    if (GameProcessRunning()) { SetStatus(K_WARN, T(L"Le jeu tourne : ferme-le avant de d\u00E9sinstaller MWCoop", L"The game is running: close it before uninstalling MWCoop")); return; }
    int r = MessageBoxW(g_wnd, T(L"D\u00E9sinstaller compl\u00E8tement MWCoop ?\n\nPart \u00E0 la Corbeille : tout MWCoop dans le dossier du jeu (version.dll, dossier MWCoop avec ses profils, journaux et contenus re\u00E7us, ce lanceur), tout %LOCALAPPDATA%\\MWCoop (copies de lancement, MSCLoader de MWCoop, caches), et les r\u00E9glages du profil invit\u00E9.\n\nGard\u00E9s : ta sauvegarde, tes mods (dossier Mods) et le MSCLoader officiel.\n\nPour rejouer en coop, il faudra ret\u00E9l\u00E9charger MWCoop.",
                                 L"Completely uninstall MWCoop?\n\nMoved to the Recycle Bin: everything MWCoop in the game folder (version.dll, the MWCoop folder with its profiles, logs and received content, this launcher), all of %LOCALAPPDATA%\\MWCoop (launch copies, MWCoop's MSCLoader, caches), and the guest profile settings.\n\nKept: your save, your mods (Mods folder) and the official MSCLoader.\n\nTo play co-op again, you will need to download MWCoop again."),
                         L"MWCoop", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2);
    if (r != IDYES) return;
    LobbyClose();
    std::wstring left;
    bool ok = UninstallCore(true, &left);
    bool selfLeft = GetFileAttributesW(g_self.c_str()) != INVALID_FILE_ATTRIBUTES;
    if (selfLeft) {   // lanceur en cours non deplace : efface 3 s apres sa fermeture
        wchar_t sys[MAX_PATH]; GetSystemDirectoryW(sys, MAX_PATH);
        std::wstring cmd = std::wstring(L"\"") + sys + L"\\cmd.exe\" /c ping 127.0.0.1 -n 4 >nul & del /f /q \"" + g_self + L"\"";
        STARTUPINFOW si = { sizeof(si) }; PROCESS_INFORMATION pi = {};
        std::vector<wchar_t> buf(cmd.begin(), cmd.end()); buf.push_back(0);
        if (CreateProcessW(NULL, buf.data(), NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, sys, &si, &pi)) { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
    }
    std::wstring msg = ok ? std::wstring(T(L"MWCoop est d\u00E9sinstall\u00E9 (tout est dans la Corbeille).", L"MWCoop is uninstalled (everything is in the Recycle Bin)."))
                          : std::wstring(T(L"MWCoop est d\u00E9sinstall\u00E9, sauf ces \u00E9l\u00E9ments (ouverts par un autre programme ?) :", L"MWCoop is uninstalled, except these items (open in another program?):")) + left;
    msg += T(L"\n\nSi tu avais mis MWCoop dans les options de lancement de Steam (Propri\u00E9t\u00E9s du jeu > G\u00E9n\u00E9ral), retire-les : sinon le jeu ne d\u00E9marrera plus depuis Steam.\n\nPour r\u00E9installer : t\u00E9l\u00E9charge MWCoop et lance MWCoop.exe.",
             L"\n\nIf you had put MWCoop in Steam's launch options (game Properties > General), remove it: otherwise the game won't start from Steam anymore.\n\nTo reinstall: download MWCoop and run MWCoop.exe.");
    MessageBoxW(g_wnd, msg.c_str(), L"MWCoop", MB_OK | (ok ? MB_ICONINFORMATION : MB_ICONWARNING));
    DestroyWindow(g_wnd);
}

// Corbeille pour une partie : son dossier (partie rangee), ou ses journaux (la derniere partie, jeu ferme).
static bool LogsGroupDelete(int gi)
{
    if (gi < 0 || gi >= (int)g_logGroups.size()) return false;
    const LogGroup &gr = g_logGroups[gi];
    bool current = gr.key == L"*";
    if (current && GameProcessRunning()) { SetStatus(K_WARN, T(L"Le jeu tourne : ferme-le avant de supprimer ses journaux", L"The game is running: close it before deleting its logs")); return false; }
    std::vector<std::wstring> paths;
    if (current) for (int i : gr.items) paths.push_back(g_logList[i].path);
    else paths.push_back(gr.dir);
    bool ok = ToRecycleBin(paths);
    if (ok) SetStatus(K_OK, T(L"Journaux mis à la corbeille", L"Logs moved to the Recycle Bin"));
    else SetStatus(K_ERR, T(L"Suppression impossible (fichier ouvert ?)", L"Could not delete (file in use?)"));
    LogsScan();
    return ok;
}

// "Tout supprimer" : toutes les parties rangees (la derniere partie reste).
static void LogsDeleteAll()
{
    std::vector<std::wstring> paths;
    for (auto &gr : g_logGroups) if (gr.key != L"*") paths.push_back(gr.dir);
    if (paths.empty()) return;
    bool ok = ToRecycleBin(paths);
    if (ok) SetStatus(K_OK, T(L"%d parties mises à la corbeille", L"%d sessions moved to the Recycle Bin"), (int)paths.size());
    else SetStatus(K_ERR, T(L"Suppression impossible (fichier ouvert ?)", L"Could not delete (file in use?)"));
    LogsScan();
}

static void ZipToDesktop(const std::vector<std::wstring> &files, const std::wstring &tag)
{
    wchar_t desk[MAX_PATH] = L"";
    std::wstring dir;
    if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_DESKTOPDIRECTORY, NULL, SHGFP_TYPE_CURRENT, desk)) && desk[0]) dir = WithSlash(desk);
    bool onDesk = !dir.empty();
    if (!onDesk) { dir = LocalDir(); if (!dir.empty()) CreateDirectoryW(dir.c_str(), NULL); }
    SYSTEMTIME st;
    GetLocalTime(&st);
    wchar_t name[80];
    swprintf_s(name, L"MWCoop-journaux-%04d-%02d-%02d_%02dh%02d.zip", st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute);
    std::wstring zip = dir + (tag.empty() ? std::wstring(name) : L"MWCoop-journaux-" + tag + L".zip");
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
        else if (g_lobby == LB_NONE && g_steamNet) SteamLobbyHost();   // Steam : salon Steam, invitations d'ici, pas de pare-feu
        else if (g_lobby == LB_NONE && FirewallBeforeHosting()) LobbyHost();
        break;
    case B_JOIN:
        if (g_lobby != LB_NONE) {
            bool host = g_lobby == LB_HOST;
            LobbyClose();
            SetStatus(K_NORMAL, host ? T(L"Salon ferm\u00E9 \u00B7 %s", L"Lobby closed \u00B7 %s") : T(L"Salon quitt\u00E9 \u00B7 %s", L"Left the lobby \u00B7 %s"), ModLabel().c_str());
        }
        else if (g_joinFallback) { g_joinFallback = false; g_launchWarn.clear(); Launch(MODE_GUEST); }
        else if (g_steamNet) SteamJoinClick();   // Steam : l'invitation recue, sinon le salon d'un ami
        else LobbyJoin();
        break;
    case B_NETIP: case B_NETSTEAM: {
        bool wasSteam = g_steamNet;
        g_steamNet = id == B_NETSTEAM;
        WritePrivateProfileStringW(L"Lanceur", L"Reseau", g_steamNet ? L"steam" : L"ip", g_iniLauncher.c_str());
        if (g_steamNet && g_focus == 1) g_focus = -1;
        if (!g_steamNet) SteamDown();
        else if (!wasSteam && GetPrivateProfileIntW(L"Lanceur", L"GuideSteam", 1, g_iniLauncher.c_str()) != 0) SteamGuideOpen();
        SetStatus(K_NORMAL, g_steamNet ? T(L"Partie par Steam : salon et invitations ici", L"Game through Steam: lobby and invites right here")
                                        : T(L"Partie par adresse IP (local, Radmin, Hamachi...)", L"Game through IP address (LAN, Radmin, Hamachi...)"));
        break;
    }
    case B_SOLO: Launch(MODE_SOLO); break;
    case B_EXE: ChooseExe(); break;
    case B_CLOSE: LobbyClose(); g_state = ST_CLOSING; break;
    case B_MIN: ShowWindow(g_wnd, SW_MINIMIZE); break;
    case B_THEME: g_dark = !g_dark; WritePrivateProfileStringW(L"Lanceur", L"Theme", g_dark ? L"sombre" : L"clair", g_iniLauncher.c_str()); break;
    case B_LANG: g_fr = !g_fr; WritePrivateProfileStringW(L"Lanceur", L"Langue", g_fr ? L"fr" : L"en", g_iniLauncher.c_str()); break;
    case B_BUY: ShellExecuteW(g_wnd, L"open", kStoreUrl, NULL, NULL, SW_SHOWNORMAL); break;
    case B_GITHUB: ShellExecuteW(g_wnd, L"open", L"https://github.com/Parricidium/MWCoop", NULL, NULL, SW_SHOWNORMAL); break;
    case B_KOFI: ShellExecuteW(g_wnd, L"open", L"https://ko-fi.com/parricidium", NULL, NULL, SW_SHOWNORMAL); break;
    case B_DISCORD: ShellExecuteW(g_wnd, L"open", kDiscordUrl, NULL, NULL, SW_SHOWNORMAL); break;
    case B_UPDATE: StartUpdate(true); break;
    case B_COLOR: CarPickColor(); break;
    case B_LOGDIR: LogsOpenFolder(); break;
    case B_LOGZIP: LogsZip(); break;
    }
}

static DWORD g_inputT;   // derniere action de la souris ou du clavier (cadence des images)
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
    SteamTick();
    MscTick();
    if (g_debug && (g_state == ST_LAUNCH || g_state == ST_RUNNING) && g_launchT) {   // (DLL du jeu a 5 et 15 s ; 30 s : RunTick)
        static DWORD doneFor, step;
        if (doneFor != g_launchT) { doneFor = g_launchT; step = 0; }
        DWORD el = now - g_launchT;
        if ((step == 0 && el > 5000) || (step == 1 && el > 15000)) { step++; DebugLog("DLL du jeu a %lu s :", el / 1000); LogGameModules(true); }
    }
    if (g_tenuesState == 2) {   // tenues offertes recues (fil) : choix de tenue, textures de l'apercu
        g_tenuesState = 0;
        TenuesAddShirts();
        for (auto &kv : g_perso.tex) if (kv.second.px.empty()) kv.second.tried = false;
        SetStatus(K_OK, T(L"Nouvelles tenues offertes par Dom : onglet Tenue (cr\u00E9dits : \u00E9toile en haut)", L"New outfits by Dom: Outfit tab (credits: star at the top)"));
    }
    if (g_state == ST_IDLE) UpdateTick();
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
    // (accueil : deux fois plus lent, l'apercu y est refait moins souvent)
    if ((g_tab == TAB_SKIN || g_tab == TAB_HOME) && g_state == ST_IDLE && !g_skinDrag && now - g_skinIdleT > 1500) g_skinYaw = fmodf(g_skinYaw + dt * (g_tab == TAB_HOME ? 0.8f : 1.6f), 16.0f);

    // Ecran d'attente : jusqu'a la fenetre du jeu. Le processus lance peut se fermer tout de suite si Steam relance
    // le jeu lui-meme : on ne conclut a un echec qu'apres 15 s sans aucun mywintercar.exe.
    if (g_state == ST_LAUNCH && now - lastScan >= 250) {
        lastScan = now;
        if (g_proc && WaitForSingleObject(g_proc, 0) == WAIT_OBJECT_0) { CloseHandle(g_proc); g_proc = NULL; }
        if (!g_winSeenT && GameWindowShown()) g_winSeenT = now;
        bool running = g_proc || GameProcessRunning();
        if (running) g_noProcT = 0;
        else if (!g_noProcT) g_noProcT = now;
        if (g_noProcT && now - g_noProcT > (g_steamLaunch ? 60000u : 15000u)) {
            g_state = ST_IDLE;
            SetStatus(K_ERR, T(L"Le jeu s'est ferm\u00E9 au d\u00E9marrage (voir les journaux)", L"The game closed on startup (see the logs)"));
            LaunchLog("jeu ferme au demarrage");
            AfterGameModCheck();
        }
    }
    if (g_state == ST_LAUNCH && ((g_winSeenT && now - g_winSeenT > 1200) || now - g_launchT > 300000)) {
        const Opt *fo = OptByKey("FermerLanceur");
        if (fo && OptGet(*fo)) g_state = ST_CLOSING;
        else RunStart(g_lastLaunchMode);
    }
    RunTick();
    // Images par seconde : 60 quand on s'en sert (souris, clavier, glisser, lancement), 30 au repos, 15 en arriere-plan
    // (la neige suffit : le lanceur ne mange plus un coeur du processeur quand il attend).
    static unsigned frameN;
    frameN++;
    bool active = g_carDrag || g_skinDrag || now - g_inputT < 1500 || g_state != ST_IDLE || g_alpha < 1 || g_lobby == LB_CONNECTING;
    unsigned every = active ? 1 : GetForegroundWindow() == g_wnd ? 2 : 4;
    if (!IsIconic(g_wnd) && frameN % every == 0) Present();   // (reduit : rien a dessiner)
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
    if (g_tab != TAB_HOME) return -1;
    for (int i = 0; i < 2; i++) if (g_fields[i].r.Width > 0 && g_fields[i].r.Contains(x, y) && !(i == 1 && g_steamNet)) return i;   // (Steam : pas d'adresse)
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
    if ((m >= WM_MOUSEFIRST && m <= WM_MOUSELAST) || m == WM_KEYDOWN || m == WM_CHAR) g_inputT = GetTickCount();
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
        g_syncKeep = true;
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
        float x = (short)LOWORD(lp) / g_scale - kM, y = (short)HIWORD(lp) / g_scale - kM;   // (coordonnees de la carte)
        if (g_carDrag) { CarDragTo(x, y); SetCursor(LoadCursor(NULL, IDC_SIZEALL)); return 0; }
        if (g_skinDrag) { SkinDragTo(x); SetCursor(LoadCursor(NULL, IDC_SIZEALL)); return 0; }
        if (g_tuto >= 0) {   // (visite guidee : elle seule repond)
            g_hot = -1; g_uiHot = -1; g_optHot = -1; g_lobbyHot = -1;
            g_tutoHot = TutoHit(x, y);
            SetCursor(LoadCursor(NULL, g_tutoHot ? IDC_HAND : IDC_ARROW));
            return 0;
        }
        if (g_guide) {   // (guide Steam ouvert : lui seul repond)
            g_hot = -1; g_uiHot = -1; g_optHot = -1; g_lobbyHot = -1;
            g_guideHot = SteamGuideHit(x, y);
            SetCursor(LoadCursor(NULL, g_guideHot > 0 ? IDC_HAND : IDC_ARROW));
            return 0;
        }
        bool idle = g_state == ST_IDLE;
        g_uiHot = ZoneAt(x, y);
        bool free = g_uiHot < 0;   // (pas sur une zone de l'interface : les elements des pages)
        g_hot = free ? HitButton(x, y) : -1;
        g_logRowHot = free && idle && g_tab == TAB_LOGS ? LogRowAt(x, y, &g_logPart) : -1;
        g_carHot = free && idle && g_tab == TAB_CAR ? CarSwatchAt(x, y) : -1;
        g_lobbyHot = free && idle && g_tab == TAB_LOBBY ? LobbyChoiceAt(x, y) : -1;
        g_mscHot = free && idle && g_tab == TAB_MODS ? MscAt(x, y) : -1;
        g_contentHot = free && idle && g_tab == TAB_CONTENT ? ContentAt(x, y) : -1;
        g_srvHot = free ? SrvAt(x, y) : -1;
        bool skinTab = free && idle && g_tab == TAB_SKIN;
        g_skinRowHot = skinTab ? PersoRowAt(x, y) : -1;
        g_skinHot = skinTab && g_perso.state != 1 ? SkinCellAt(x, y) : -1;
        g_skinArrowHot = skinTab && g_perso.state != 1 ? SkinArrowAt(x, y) : 0;
        bool noteImg = free && !NoteImgAt(x, y).empty();
        TRACKMOUSEEVENT tme = { sizeof(tme), TME_LEAVE, h, 0 };
        TrackMouseEvent(&tme);
        bool carView = free && idle && g_tab == TAB_CAR && g_car.state == 1 && kCarView.Contains(x, y) && g_carHot < 0;
        bool skinView = skinTab && !g_skinArrowHot && g_skinRowHot < 0 && kSkinView.Contains(x, y);
        bool hand = g_uiHot > UI_NONE || (g_hot >= 0 && g_btn[g_hot].enabled) || g_logRowHot >= 0 || g_carHot >= 0 || g_lobbyHot >= 0 || g_skinHot >= 0 || g_skinArrowHot
                    || g_skinRowHot >= 0 || g_mscHot >= 0 || g_contentHot >= 0 || g_srvHot >= 0 || noteImg;
        SetCursor(LoadCursor(NULL, hand ? IDC_HAND : carView || skinView ? IDC_SIZEALL : free && HitField(x, y) >= 0 ? IDC_IBEAM : IDC_ARROW));
        return 0;
    }
    case WM_MOUSELEAVE: g_hot = -1; g_uiHot = -1; g_optHot = -1; g_logRowHot = -1; g_carHot = -1; g_lobbyHot = -1; g_skinHot = -1; g_skinArrowHot = 0; g_skinRowHot = -1; g_mscHot = -1; g_contentHot = -1; return 0;
    case WM_KEYDOWN:   // page TENUE : fleches gauche / droite (hors des champs)
        if (TutoKey(wp)) return 0;
        if (g_guide && (wp == VK_ESCAPE || wp == VK_RETURN)) { SteamGuideClose(); return 0; }
        if (wp == VK_ESCAPE && g_lobbyOpts) { g_lobbyOpts = false; return 0; }
        if ((wp == VK_LEFT || wp == VK_RIGHT) && g_tab == TAB_SKIN && g_state == ST_IDLE && g_focus < 0) { SkinStep(wp == VK_LEFT ? -1 : 1); return 0; }
        break;
    case WM_MOUSEWHEEL:
        if (!g_guide && g_tuto < 0 && g_state == ST_IDLE) {
            POINT pt = { (short)LOWORD(lp), (short)HIWORD(lp) };
            ScreenToClient(h, &pt);
            float x = pt.x / g_scale - kM, y = pt.y / g_scale - kM;
            float step = -(short)HIWORD(wp) / 120.0f * 64;
            if (g_tab == TAB_NOTES && kNotesList.Contains(x, y)) g_noteListScroll += step;
            else if (g_tab == TAB_WIKI && kWikiList.Contains(x, y)) g_wikiListScroll += step;
            else g_scroll[g_tab] = min(max(g_scroll[g_tab] + step, 0.0f), MaxScroll(g_tab));
        }
        return 0;
    case WM_SETCURSOR: return TRUE;
    case WM_LBUTTONDOWN: {
        float x = (short)LOWORD(lp) / g_scale - kM, y = (short)HIWORD(lp) / g_scale - kM;
        if (g_tuto >= 0) { TutoClick(x, y); return 0; }
        if (g_guide) { SteamGuideClick(x, y); return 0; }
        if (g_lobbyOpts && !g_lobbyPopR.Contains(x, y) && !g_lobbyGearR.Contains(x, y)) { g_lobbyOpts = false; return 0; }   // (clic a cote : ferme le menu)
        int z = ZoneAt(x, y);
        if (z >= 0) { g_focus = -1; UiClick(z); return 0; }
        int b = HitButton(x, y), f = HitField(x, y);
        if (b >= 0) { g_pressed = b; SetCapture(h); return 0; }
        if (f >= 0) { g_focus = f; g_time = 0; return 0; }
        g_focus = -1;
        if (g_state == ST_RUNNING && SrvClick(x, y)) return 0;
        if (g_state == ST_IDLE) {
            if (g_tab == TAB_LOBBY && LobbyClick(x, y)) return 0;
            if (g_tab == TAB_MODS && MscClick(x, y)) return 0;
            if (g_tab == TAB_CONTENT && ContentAt(x, y) >= 0) { ContentClick(x, y); return 0; }
            if (g_tab == TAB_NOTES) { std::wstring u = NoteImgAt(x, y); if (!u.empty()) { ShellExecuteW(NULL, L"open", u.c_str(), NULL, NULL, SW_SHOWNORMAL); return 0; } }
            if (g_tab == TAB_LOGS && LogsMouseDown(x, y)) return 0;
            if (g_tab == TAB_CAR && CarMouseDown(x, y)) return 0;
            if (g_tab == TAB_SKIN && SkinMouseDown(x, y)) return 0;
        }
        // hors des cartes (en-tete, bords, pied) : glisser la fenetre
        if (y < 84 || (g_outside && g_outside->IsVisible(x, y))) {
            ReleaseCapture();
            SendMessageW(h, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }
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
        float x = (short)LOWORD(lp) / g_scale - kM, y = (short)HIWORD(lp) / g_scale - kM;
        if (p >= 0 && HitButton(x, y) == p && g_btn[p].enabled) OnButton(p);
        return 0;
    }
    case WM_CHAR:
        if (g_state != ST_IDLE || g_lobby != LB_NONE || g_goWait || g_guide || g_tuto >= 0) return 0;
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
    case WM_COPYDATA: {   // un 2e lanceur (invitation acceptee jeu ferme, option de lancement Steam) : son salon
        const COPYDATASTRUCT *cd = (const COPYDATASTRUCT *)lp;
        if (!cd || cd->dwData != 0x4D57 || cd->cbData != sizeof(uint64_t)) return FALSE;
        g_sConnect = *(const uint64_t *)cd->lpData;
        TestLog("salon steam : salon %llu recu d'un autre lanceur", (unsigned long long)g_sConnect);
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        SetForegroundWindow(h);
        return TRUE;
    }
    case WM_APP_RELAUNCH: {
        STARTUPINFOW si = { sizeof(si) };
        PROCESS_INFORMATION pi;
        std::wstring cmd = L"\"" + g_self + L"\"";
        if (g_sConnect) cmd += L" +connect_lobby " + std::to_wstring((unsigned long long)g_sConnect);   // (invitation pas encore prise)
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
        SteamDown();
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
    // DLL de Windows seulement depuis System32 : le lanceur est pose dans le dossier du jeu, ou MSCLoader met son
    // winhttp.dll (UnityDoorstop) et MWCoop son version.dll. Charges par erreur dans le lanceur, ils y restaient
    // verrouilles (une reinstallation de MSCLoader echouait). winhttp et winmm sont charges a la demande (/DELAYLOAD),
    // donc apres ceci.
    SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
    SetDllDirectoryW(L"");
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
    {   // Steam met ses variables dans l'environnement du programme qu'il lance (avant tout SteamAPI_Init d'ici)
        wchar_t v[64];
        const wchar_t *vars[] = { L"SteamGameId", L"SteamOverlayGameId", L"SteamClientLaunch", L"SteamAppId" };
        for (const wchar_t *n : vars) if (GetEnvironmentVariableW(n, v, 64)) g_fromSteam = true;
        int ac = 0;
        wchar_t **av = CommandLineToArgvW(GetCommandLineW(), &ac);
        bool cli = ac >= 2 && av[1][0] == L'/';
        for (int i = 1; i < ac; i++) {   // %command% : le chemin de mywintercar.exe en argument
            size_t n = wcslen(av[i]);
            if (n >= 15 && !_wcsicmp(av[i] + n - 15, L"mywintercar.exe")) g_fromSteam = true;
        }
        // Lancer par Steam (Reglages > Jeu) avec l'option de lancement "<MWCoop.exe>" %command% : Steam relance le lanceur au
        // lieu du jeu (retour d'un joueur, 08/10 : "Running" dans Steam, un 2e lanceur, rien ne demarrait). Demande toute
        // fraiche du lanceur ouvert (lancer-par-steam.txt, moins de 90 s) : on demarre le jeu que Steam nous passe, comme
        // Steam l'aurait fait (son environnement), et on s'efface.
        if (!cli) {
            int gi = -1;
            for (int i = 1; i < ac && gi < 0; i++) { size_t n = wcslen(av[i]); if (n >= 15 && !_wcsicmp(av[i] + n - 15, L"mywintercar.exe")) gi = i; }
            wchar_t l[MAX_PATH] = L"";
            GetEnvironmentVariableW(L"LOCALAPPDATA", l, MAX_PATH);
            std::wstring req = l[0] ? std::wstring(l) + L"\\MWCoop\\lancer-par-steam.txt" : L"";
            WIN32_FILE_ATTRIBUTE_DATA ra;
            if (gi > 0 && !req.empty() && GetFileAttributesExW(req.c_str(), GetFileExInfoStandard, &ra)) {
                FILETIME ft; GetSystemTimeAsFileTime(&ft);
                ULONGLONG age = ((((ULONGLONG)ft.dwHighDateTime << 32) | ft.dwLowDateTime) - (((ULONGLONG)ra.ftLastWriteTime.dwHighDateTime << 32) | ra.ftLastWriteTime.dwLowDateTime)) / 10000000ULL;
                DeleteFileW(req.c_str());
                if (age < 90) {
                    std::wstring cmd = L"\"" + std::wstring(av[gi]) + L"\"";
                    for (int i = gi + 1; i < ac; i++) cmd += L" \"" + std::wstring(av[i]) + L"\"";
                    std::wstring dir = av[gi];
                    dir = dir.substr(0, dir.find_last_of(L'\\'));
                    STARTUPINFOW si = { sizeof(si) }; PROCESS_INFORMATION pi = {};
                    std::vector<wchar_t> cb(cmd.begin(), cmd.end()); cb.push_back(0);
                    BOOL started = CreateProcessW(av[gi], cb.data(), NULL, NULL, FALSE, 0, NULL, dir.c_str(), &si, &pi);
                    DWORD err = started ? 0 : GetLastError();
                    FILE *f = _wfopen((std::wstring(l) + L"\\MWCoop\\lanceur.log").c_str(), L"ab");
                    if (f) { SYSTEMTIME t; GetLocalTime(&t); fprintf(f, "%02d:%02d:%02d lanceur relance par Steam (Lancer par Steam, demande de %llu s) : %s demarre %s\r\n", t.wHour, t.wMinute, t.wSecond, age, Narrow(av[gi], CP_UTF8).c_str(), started ? "" : ("ECHEC " + std::to_string(err)).c_str()); fclose(f); }
                    if (started) { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); LocalFree(av); return 0; }
                }
            }
        }
        if (!cli) {
            wchar_t l[MAX_PATH] = L"";
            GetEnvironmentVariableW(L"LOCALAPPDATA", l, MAX_PATH);
            if (l[0]) {
                std::wstring d = std::wstring(l) + L"\\MWCoop\\";
                SHCreateDirectoryExW(NULL, d.c_str(), NULL);
                g_launcherLog = d + L"lanceur.log";
                // (le journal d'avant garde la partie d'avant : un joueur relance souvent le lanceur avant de faire le zip)
                MoveFileExW(g_launcherLog.c_str(), (d + L"lanceur-precedent.log").c_str(), MOVEFILE_REPLACE_EXISTING);
                FILE *f = _wfopen(g_launcherLog.c_str(), L"wb");
                if (f) fclose(f);
                LaunchLog("MWCoop.exe %s (%s), demarre par Steam : %d, ligne : %s", Narrow(g_self, CP_UTF8).c_str(), __DATE__, (int)g_fromSteam, Narrow(GetCommandLineW(), CP_UTF8).c_str());
            }
        }
        LocalFree(av);
    }
    DeleteFileW((g_self + L".old").c_str());   // reste d'une mise a jour du lanceur
    DeleteFileW((g_dir + L"version.dll.old").c_str());   // (chargeur verrouille pendant une mise a jour : plus utilise, sauf jeu ouvert)
    // Langue : francais si Windows est en francais, anglais pour toute autre langue ; Langue=fr|en pour forcer.
    wchar_t lang[8] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Langue", L"", lang, 8, g_iniLauncher.c_str());
    if (!_wcsicmp(lang, L"fr")) g_fr = true;
    else if (!_wcsicmp(lang, L"en")) g_fr = false;
    {   // Theme : Theme=clair|sombre, sinon celui des applications de Windows
        wchar_t th[16] = L"";
        GetPrivateProfileStringW(L"Lanceur", L"Theme", L"", th, 16, g_iniLauncher.c_str());
        {
            wchar_t rn[16];
            GetPrivateProfileStringW(L"Lanceur", L"Reseau", L"ip", rn, 16, g_iniLauncher.c_str());
            g_steamNet = _wcsicmp(rn, L"steam") == 0;
            g_mscOn = GetPrivateProfileIntW(L"Lanceur", L"MSCLoader", 1, g_iniLauncher.c_str()) != 0;
            g_debug = GetPrivateProfileIntW(L"Lanceur", L"Debug", 0, g_iniLauncher.c_str()) != 0;
            if (g_debug) DebugSys();
            g_mscOwnPref = GetPrivateProfileIntW(L"Lanceur", L"MSCLoaderMWCoop", -1, g_iniLauncher.c_str());
        }
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
        if (!_wcsicmp(argv[i], L"/sauter")) g_benchSkip = _wtoi(argv[i + 1]);
        if (!_wcsicmp(argv[i], L"/skins")) g_skinsArg = WithSlash(argv[i + 1]);   // images des tenues de test
        // Steam : invitation acceptee jeu ferme, lanceur demarre par l'option de lancement ("<MWCoop.exe>" %command%)
        if (!_wcsicmp(argv[i], L"+connect_lobby")) { g_sConnect = _wcstoui64(argv[i + 1], NULL, 10); g_steamNet = true; }
        if (!_wcsicmp(argv[i], L"/steam")) { g_testSteamFile = argv[i + 1]; g_steamNet = true; }   // essai : /testsalon ... /steam <fichier>
    }
    // Un lanceur deja ouvert prend le salon (un seul lanceur, Steam s'adresse a lui ensuite)
    if (g_sConnect && g_testSalon.empty()) {
        HWND other = FindWindowW(L"MWCoopLauncher", NULL);
        if (other) {
            COPYDATASTRUCT cd = { 0x4D57, sizeof(uint64_t), &g_sConnect };
            DWORD_PTR res = 0;
            SendMessageTimeoutW(other, WM_COPYDATA, 0, (LPARAM)&cd, SMTO_ABORTIFHUNG, 3000, &res);
            if (res) { LocalFree(argv); return 0; }
        }
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

    SetGame(FindGame());
    // /jeu <journal> : jeu trouve (dossier, version, mod)
    if (argc >= 3 && !_wcsicmp(argv[1], L"/jeu")) {
        FILE *f = _wfopen(argv[2], L"w, ccs=UTF-8");
        if (f) { fwprintf(f, L"jeu=%s\nversion=%s\nmod=%d local=%s\nsteam=%d\n", g_gameDir.c_str(), g_gameVer.c_str(), (int)g_modOk, g_localVer.c_str(), (int)g_fromSteam); fclose(f); }
        GdiplusShutdown(gtok);
        return g_gameDir.empty() ? 1 : 0;
    }
    g_logo = LoadPngRes(4);   // (logo de la barre de navigation : logo-titre.png)
    if (g_gameDir.empty()) SetStatus(K_ERR, T(L"My Winter Car introuvable : choisis mywintercar.exe", L"My Winter Car not found: choose mywintercar.exe"));
    else if ((CrashCheck(), g_crashT != 0)) SetStatus(K_WARN, T(L"Arr\u00EAt brutal du jeu : voir les journaux", L"The game stopped abruptly: see the logs"));
    else if (LastLaunchWithoutMod()) SetStatus(K_WARN, T(L"Le dernier lancement s'est fait SANS le mod (antivirus ? version.dll ?) : voir JOURNAUX", L"The last launch ran WITHOUT the mod (antivirus? version.dll?): see LOGS"));
    else SetStatus(K_NORMAL, L"%s", ModLabel().c_str());

    // /maj <dossier du jeu> <journal> : mise a jour sans fenetre (tests) ; journal = etat final
    if (argc >= 4 && !_wcsicmp(argv[1], L"/maj")) {
        std::wstring d = WithSlash(argv[2]);
        SetGame(IsGameDir(d) ? d : L"");
        if (!g_gameDir.empty()) { g_busy = true; UpdateThread(NULL); }
        FILE *f = _wfopen(argv[3], L"w, ccs=UTF-8");
        wchar_t wh[MAX_PATH] = L"(pas charge)";
        if (HMODULE m = GetModuleHandleW(L"winhttp.dll")) GetModuleFileNameW(m, wh, MAX_PATH);
        if (f) { fwprintf(f, L"jeu=%s mod=%d local=%s releases=%d winhttp=%s\n%s\n", g_gameDir.c_str(), (int)g_modOk, g_localVer.c_str(), (int)g_relState, wh, g_status.c_str()); fclose(f); }
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

    // /dezip <zip> <dossier> <journal> : extraction d'un paquet (celle de la mise a jour) ; /jonction <lien> <cible>
    if (argc >= 5 && !_wcsicmp(argv[1], L"/dezip")) {
        std::string why;
        bool ok = Unzip(argv[2], argv[3], &why);
        FILE *f = _wfopen(argv[4], L"w");
        if (f) { fprintf(f, "%s %s\n", ok ? "ok" : "echec", why.c_str()); fclose(f); }
        GdiplusShutdown(gtok);
        return ok ? 0 : 1;
    }
    if (argc >= 4 && !_wcsicmp(argv[1], L"/parefeu-etat")) {
        FILE *f = _wfopen(argv[2], L"w, ccs=UTF-8");
        for (int i = 3; i < argc && f; i++) fwprintf(f, L"%d %s\n", FirewallState(argv[i]), argv[i]);
        if (f) fclose(f);
        GdiplusShutdown(gtok);
        return 0;
    }
    if (argc >= 3 && !_wcsicmp(argv[1], L"/parefeu")) {
        std::vector<std::wstring> apps;
        for (int i = 2; i < argc; i++) apps.push_back(argv[i]);
        bool ok = FirewallAllow(apps);
        GdiplusShutdown(gtok);
        return ok ? 0 : 1;
    }
    if (argc >= 4 && !_wcsicmp(argv[1], L"/jonction")) {
        bool ok = MakeJunction(argv[2], argv[3]);
        GdiplusShutdown(gtok);
        return ok ? 0 : 1;
    }
    // /mscl installer|reglages|mwcoop <dossier du jeu> <journal> : installe MSCLoader dans ce jeu, ou change deux options du 1er
    // mod connu (case inversee, curseur +1 pas) et ecrit son settings.json (essais, sans fenetre)
    if (argc >= 5 && !_wcsicmp(argv[1], L"/mscl")) {
        g_testSalonLog = argv[4];
        FILE *f = _wfopen(argv[4], L"wb");
        if (f) fclose(f);
        SetGame(WithSlash(argv[3]));
        bool ok = false;
        if (!g_gameDir.empty() && !_wcsicmp(argv[2], L"mwcoop")) {   // copie de lancement du MSCLoader de MWCoop (jeu non lance)
            g_mscOn = true;
            std::wstring m = MscCopyDir();
            TestLog("mscl : own=%d installe=%d officiel=%d, copie %s, exe %s", (int)MscOwn(), (int)MscOwnInstalled(), (int)MscOfficialInstalled(), Narrow(m, CP_UTF8).c_str(), Narrow(GameExe(), CP_UTF8).c_str());
            ok = MscOwnInstalled() && PrepareMirror(m) && MscOwnApply(m, true, false);
        } else if (!g_gameDir.empty() && !_wcsicmp(argv[2], L"installer")) {
            MscInstallThread(NULL);
            ok = g_mscInstall == 2;
            MscScan();
            TestLog("mscl : installe=%d, dossier des mods %s", (int)g_mscInstalled, Narrow(g_mscDir, CP_UTF8).c_str());
        } else if (!g_gameDir.empty()) {
            MscScan();
            TestLog("mscl : installe=%d, version %s, %d mod(s)", (int)g_mscInstalled, Narrow(g_mscVer).c_str(), (int)g_msc.size());
            for (int i = 0; i < (int)g_msc.size(); i++) {
                TestLog("mscl : mod %s (%s) connu=%d, %d option(s)", Narrow(g_msc[i].name, CP_UTF8).c_str(), Narrow(g_msc[i].file, CP_UTF8).c_str(), (int)g_msc[i].known, (int)g_msc[i].sets.size());
                if (!g_msc[i].known || ok) continue;
                g_mscPage = i;
                MscLoadPage();
                for (const MscSet &s : g_msc[i].sets) if (s.type == "CheckBox" || s.type == "Slider") MscStep(s, 1);
                ok = MscSavePage();
                std::vector<unsigned char> d;
                ReadAll(MscSettingsPath(g_msc[i]), d);
                TestLog("mscl : settings.json ecrit :\n%s", std::string(d.begin(), d.end()).c_str());
            }
        }
        GdiplusShutdown(gtok);
        return ok ? 0 : 1;
    }
    // /debugsys <journal> : releve du mode debogage (Windows, Smart App Control, antivirus), essais
    if (argc >= 3 && !_wcsicmp(argv[1], L"/debugsys")) {
        g_testSalonLog = argv[2];
        FILE *f = _wfopen(argv[2], L"wb");
        if (f) fclose(f);
        g_debug = true;
        DebugSysThread(NULL);
        GdiplusShutdown(gtok);
        return 0;
    }
    // /desinstaller <dossier du jeu> <journal> : desinstallation sans questions, sans le registre ni le lanceur lui-meme
    // (essais : LOCALAPPDATA d'essai, jeu factice)
    if (argc >= 4 && !_wcsicmp(argv[1], L"/desinstaller")) {
        g_testSalonLog = argv[3];
        FILE *f = _wfopen(argv[3], L"wb");
        if (f) fclose(f);
        SetGame(WithSlash(argv[2]));
        g_self = g_gameDir + L"MWCoop.exe";   // (pas le lanceur d'essai : celui du jeu factice)
        g_iniLauncher = g_gameDir + L"mwcoop-lanceur.ini";
        std::wstring left;
        bool ok = !g_gameDir.empty() && UninstallCore(false, &left);
        TestLog("desinstaller : %s%s", ok ? "ok" : "RESTE :", Narrow(left, CP_UTF8).c_str());
        GdiplusShutdown(gtok);
        return ok ? 0 : 1;
    }
    // /bureau <exe> <dossier> <journal> [arguments] : lance par le bureau de Windows, puis 25 s apres, les modules des jeux
    if (argc >= 5 && !_wcsicmp(argv[1], L"/bureau")) {
        g_testSalonLog = argv[4];
        FILE *f = _wfopen(argv[4], L"wb");
        if (f) fclose(f);
        bool ok = ShellRunFromDesktop(argv[2], argc >= 6 ? argv[5] : L"", argv[3]);
        TestLog("bureau : %s", ok ? "lance" : "ECHEC");
        if (ok) { Sleep(25000); LogGameModules(); }
        GdiplusShutdown(gtok);
        return ok ? 0 : 1;
    }
    // /chargeur <dossier> <journal> : verification du chargeur (version.dll) de ce dossier, comme avant un lancement
    if (argc >= 4 && !_wcsicmp(argv[1], L"/chargeur")) {
        g_testSalonLog = argv[3];
        FILE *f = _wfopen(argv[3], L"wb");
        if (f) fclose(f);
        LoaderEnsure(WithSlash(argv[2]), "essai");
        TestLog("chargeur : fin");
        GdiplusShutdown(gtok);
        return 0;
    }
    // /miroir <dossier du jeu> <copie> <journal> [coupe] : copie de lancement de ce jeu dans ce dossier (essais) ;
    // coupe : MSCLoader coupe (winhttp.dll retire de la copie)
    if (argc >= 5 && !_wcsicmp(argv[1], L"/miroir")) {
        g_testSalonLog = argv[4];
        FILE *f = _wfopen(argv[4], L"wb");
        if (f) fclose(f);
        SetGame(WithSlash(argv[2]));
        bool ok = !g_gameDir.empty() && PrepareMirror(WithSlash(argv[3]));
        if (ok && argc >= 6 && !_wcsicmp(argv[5], L"coupe")) MscOffInCopy(WithSlash(argv[3]));
        TestLog("miroir : %s", ok ? "ok" : "ECHEC");
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
        else if (st == L"menu-steam") g_steamNet = true;
        else if (st == L"guide-steam") { g_steamNet = true; SteamGuideOpen(); g_guideHot = 1; }
        else if (st == L"discord") { DiscordAskOpen(); g_guideHot = 1; }
        else if (st == L"serveur" || st == L"partie-invite") {   // partie en cours, lanceur ouvert (faux joueurs)
            bool host = st == L"serveur";
            g_state = ST_RUNNING;
            g_srvFresh = true; g_srvHost = host; g_srvSteam = true;
            g_srvStatus = host ? T(L"h\u00F4te Steam", L"Steam host") : T(L"connect\u00E9 \u00E0 Pekka (Steam)", L"connected to Pekka (Steam)");
            g_srv = { { 0, host ? Widen(MyName()) : L"Pekka", host ? MySkin() : "cop_shirt", -1, true, host },
                      { 1, host ? L"Teppo" : Widen(MyName()), host ? "rally_shirt" : MySkin(), host ? 42 : -1, true, !host },
                      { 2, L"Kalle", "char_shirt07", 87, false, false } };
            if (!host) g_srv[0].ping = 42;
            wchar_t info[160];
            swprintf_s(info, T(L"%s h\u00E9berge la partie (Steam)", L"%s is hosting (Steam)"), PlayerName().c_str());
            g_launchInfo = host ? info : T(L"JD rejoint Pekka", L"JD joins Pekka");
            if (host) { g_kickArmed = 2; g_kickArmedT = GetTickCount(); g_srvHot = 1000 + 1 * 2 + 0; }
        }
        else if (!wcsncmp(st.c_str(), L"tuto-", 5)) {   // visite guidee : etape n (tuto-maj : apres une mise a jour)
            bool maj = st == L"tuto-maj";
            g_tutoList.clear();
            for (int i = 0; i < kTutoN; i++) if (!maj || i != 0) g_tutoList.push_back(i);
            g_tutoAll = !maj;
            g_tutoTabWas = -1;
            TutoGo(maj ? 0 : _wtoi(st.c_str() + 5));
            g_tutoHot = 1;
        }
        else if (st == L"crash" || st == L"crash-survol") {
            g_crashT = 1;
            SetStatus(K_WARN, T(L"Arr\u00EAt brutal du jeu : voir les journaux", L"The game stopped abruptly: see the logs"));
            if (st == L"crash-survol") g_uiHot = UI_NAV + TAB_LOGS;
        }
        else if (st == L"mods" || st == L"mods-page" || st == L"mods-absent") {   // onglet MODS (faux mods)
            g_mscFake = true;
            g_mscInstalled = st != L"mods-absent";
            g_mscVer = L"1.4.2"; g_mscDirKind = L"GF";
            MscMod a, b, c;
            a.file = L"BetterHeadlights.dll"; a.id = L"BetterHeadlights"; a.name = L"Better Headlights"; a.ver = L"1.3"; a.author = L"Fleetari"; a.known = true;
            a.sets = { { "HeaderGroup", "", L"Phares", "", "", "", "", "", {} }, { "CheckBox", "xenon", L"Ampoules x\u00E9non", "1", "0", "", "", "", {} },
                       { "Slider", "range", L"Port\u00E9e", "1.5", "1", "0.5", "3", "2", {} }, { "DropDown", "color", L"Teinte", "1", "0", "", "", "", { L"Blanc", L"Jaune", L"Bleut\u00E9" } },
                       { "SliderInt", "angle", L"Inclinaison", "2", "0", "0", "5", "0", {} }, { "Text", "", L"Les r\u00E9glages s'appliquent apr\u00E8s un passage au garage.", "", "", "", "", "", {} } };
            b.file = L"TrunkLight.dll"; b.id = L"TrunkLight"; b.name = L"Trunk Light"; b.ver = L"2.0"; b.author = L"Suski"; b.known = true; b.disabled = true;
            c.file = L"NewMod.dll"; c.name = L"NewMod";
            g_msc = { a, b, c };
            if (st == L"mods-page") {
                g_mscPage = 0;
                g_mscVals = { { "xenon", "true" }, { "range", "1.5" }, { "color", "1" }, { "angle", "2" } };
                g_mscHot = 3000 + 2 * 4 + 2;
            } else if (g_mscInstalled) g_mscHot = 4001;   // (survol : Envoyer)
            else g_mscHot = 1001;
            g_tab = TAB_MODS;
                }
        else if (st == L"menu-ip") g_steamNet = false;
        else if (st == L"sansjeu") { g_gameDir.clear(); g_gameVer.clear(); g_localVer.clear(); g_modOk = false; SetStatus(K_ERR, T(L"My Winter Car introuvable : choisis mywintercar.exe", L"My Winter Car not found: choose mywintercar.exe")); }
        else if (st == L"coop" || st == L"reglages") { g_tab = TAB_COOP; g_uiHot = UI_SET_CLOSE; }
        else if (st == L"contenu") { g_tab = TAB_CONTENT; g_contentHot = 5004; MscScan(); }   // contenu envoye
        else if (st == L"voiture") { g_tab = TAB_CAR; }   // couleur : CouleurVoiture du mwcoop.ini du jeu
        else if (st == L"tenue" || st == L"tenue-survol" || st == L"tenue-perso") {   // tenue : Apparence du mwcoop.ini ; angle : /temps (un tour en 10 s)
            g_tab = TAB_SKIN;
            g_skinYaw = fmodf(g_sceneT * 1.6f, 16.0f);
            if (st == L"tenue-survol") { g_skinHot = 9; g_uiHot = UI_LOOKPIN + 1; g_lookZone = 1; }
            if (st == L"tenue-perso") { g_skinRowHot = 1; g_skinYaw = (float)_wtof(argv[argc - 1]); }   // (dernier argument : la vue 0..16)
        }
        else if (st == L"notes") {   // notes d'exemple (le depot n'a pas encore de release)
            g_notes = { { L"0.1.1-prealpha", L"09/10/2026", L"\u2022 Exemple de note de version (capture).\n\u2022 Deuxi\u00E8me ligne : une correction.", L"\u2022 Sample release note (capture).\n\u2022 Second line: a fix.", L"" },
                        { L"0.1.0-prealpha", L"02/10/2026", L"\u2022 Premi\u00E8re version : chargeur, joueurs visibles.", L"\u2022 First version: loader, visible players.", L"" } };
            g_notesDone = true; g_relState = REL_OK; g_tab = TAB_NOTES;
        }
        else if (st == L"notesvide") { NotesOnlyThread(NULL); g_tab = TAB_NOTES; }
        else if (st == L"notes-image") {   // une note avec une image (telechargee ici, avant la capture)
            std::wstring url = L"https://raw.githubusercontent.com/" + g_repo + L"/main/docs/img/lanceur-tenue.png";
            std::wstring fr = CleanNote(L"- Volet de droite pour la tenue :\n![tenue](" + url + L")\n- Et une ligne apr\u00E8s l'image.");
            g_notes = { { L"0.36.0-prealpha", L"08/10/2026", fr, fr, L"" },
                        { L"0.1.0-prealpha", L"02/10/2026", L"\u2022 Premi\u00E8re version.", L"\u2022 First version.", L"" } };
            NoteImgFetch(url);
            g_notesDone = true; g_relState = REL_OK; g_tab = TAB_NOTES;
        }
        else if (st == L"journaux") { g_tab = TAB_LOGS; LogsScan(); g_logRowHot = 0; }
        else if (st == L"api") g_tab = TAB_API;   // page API des moddeurs
        else if (st == L"credits") g_tab = TAB_CREDITS;
        else if (!wcsncmp(st.c_str(), L"guide", 5) && st != L"guide-steam") {   // guide : guide-<article>-<defilement>
            g_tab = TAB_WIKI;
            int art = 0, sc = 0;
            swscanf_s(st.c_str(), L"guide-%d-%d", &art, &sc);
            g_wikiSel = art;
            g_wikiClosed &= ~(1u << kWikiArt[art].cat);
            g_scroll[TAB_WIKI] = (float)sc;
        }
        else if (st == L"graphismes") { g_tab = TAB_GFX; GfxLoad(); }
        else if (st == L"api-direct") { g_tab = TAB_API; g_apiEx = 1; }
        else if (st == L"salon" || st == L"salon-invite" || st == L"salon-udp" || st == L"salon-options" || !wcsncmp(st.c_str(), L"salon-mods", 10)) {   // salon a 3 joueurs (faux), vu par l'hote ou par un invite
            bool host = st == L"salon" || st == L"salon-options" || st == L"salon-mods";   // (salon-udp : invite dont l'UDP est bloque)
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
                    if (host) { SetStatus(K_OK, T(L"Salon ouvert \u00B7 port %d", L"Lobby open \u00B7 port %d"), 7870); g_lobbyHot = 1; }
            else SetStatus(K_OK, T(L"Dans le salon de %s", L"In %s's lobby"), L"192.168.1.20");
            if (st == L"salon-udp") { g_udpMine = UDP_FAIL; g_peers[1].udp = UDP_WAIT; }
            if (wcsstr(argv[3], L"-options")) { g_lobbyOpts = true; g_uiHot = UI_POP_CLOSE; }
            g_hostUdpTest = true;
            // mods de l'hote : salon-mods (hote), salon-mods-demande | -telechargement | -prets (invite)
            std::vector<std::string> mods = { "BetterHeadlights.dll", "TrunkLight.dll", "CDPlayerEnhanced.dll" };
            if (st == L"salon-mods") {
                g_offer.msc = true; g_offer.mods = mods; g_offer.cats = { "cd1", "peinture", "drapeau", "posters" };
                g_offer.files = { { "BetterHeadlights.dll", 412000, 1 }, { "TrunkLight.dll", 96000, 2 }, { "CDPlayerEnhanced.dll", 1830000, 3 } };
                g_lobbyHot = 71;
            } else if (!wcsncmp(st.c_str(), L"salon-mods-", 11)) {
                g_hostOffer.msc = true; g_hostOffer.mods = mods; g_hostOffer.cats = { "cd1", "peinture", "drapeau", "posters" };
                g_sync = st == L"salon-mods-demande" ? SY_ASK : st == L"salon-mods-telechargement" ? SY_GET : SY_DONE;
                g_syncTotal = 2338000; g_syncGot = 1052000;
                g_meReady = g_sync == SY_DONE;
                if (g_sync == SY_ASK) g_lobbyHot = 70;
            }
        }
        else if (st == L"salon-steam" || st == L"salon-steam-amis" || st == L"salon-steam-invite") {   // salon Steam (faux)
            bool host = st != L"salon-steam-invite";
            std::string v = MyVersion();
            g_peers = { { 0, host ? MyName() : "Pekka", host ? MySkin() : "cop_shirt", v, true, 0, UDP_NA, 0, 1 },
                        { 1, host ? "Teppo" : MyName(), host ? "rally_shirt" : MySkin(), v, true, 0, UDP_NA, 0, 2 },
                        { 2, "Kalle", "char_shirt07", v, false, 0, UDP_NA, 0, 3 } };
            g_myId = host ? 0 : 1;
            g_lobby = host ? LB_HOST : LB_GUEST;
            g_lobbySteam = true;
            g_steamNet = true;
            g_meReady = !host;
            g_sHostName = L"Pekka";
            g_partie = PARTIE_CONTINUER;
            g_tab = TAB_LOBBY;
                    if (st == L"salon-steam-amis") {
                g_sFriendsView = true;
                g_sFriends = { { 2, L"Teppo", 1, true }, { 4, L"Jouko", 1, true }, { 5, L"Arska", 1, false }, { 6, L"Fleetari", 3, false }, { 7, L"Suski", 2, false } };
                g_sOffline = 14;
                g_sInvited[4] = GetTickCount();
                g_lobbyHot = 102;
            } else if (host) g_lobbyHot = 51;
            if (host) SetStatus(K_OK, T(L"Salon Steam ouvert : invite tes amis", L"Steam lobby open: invite your friends"));
            else SetStatus(K_OK, T(L"Dans le salon Steam de %s", L"In %s's Steam lobby"), L"Pekka");
        }
        else if (st == L"nouvelle-maj" || st == L"nouvelle-maj-survol" || st == L"nouvelle-maj-notes") {   // version plus recente proposee (bouton)
            UpdSet(L"0.41.0-prealpha");
            if (st == L"nouvelle-maj-survol") { g_hot = B_UPDATE; g_btn[B_UPDATE].hover = 1; }
            if (st == L"nouvelle-maj-notes") g_tab = TAB_NOTES;
        }
        else if (st == L"maj") { g_busy = true; g_progress = 0.42f; SetStatus(K_NORMAL, T(L"T\u00E9l\u00E9chargement de MWCoop %s\u2026", L"Downloading MWCoop %s\u2026"), L"0.1.1-prealpha"); g_focus = 0; g_time = 0.2f; }
        else { SetStatus(K_OK, T(L"%s \u00B7 \u00E0 jour", L"%s \u00B7 up to date"), ModLabel().c_str()); g_hot = B_HOST; g_btn[B_HOST].hover = 1; }
        if (g_tab == TAB_HOME) g_skinYaw = fmodf(g_sceneT * 1.6f, 16.0f);   // (accueil : le personnage tourne avec /temps)
        // /clic x y (coordonnees de la carte, autant de fois qu'il faut) : clics simules avant la capture, dans une
        // fenetre "message only" (ni souris ni clavier) ; l'etat apres chaque clic dans <png>.txt (essais de la navigation).
        {
            std::vector<std::pair<float, float>> clics;
            for (int i = 4; i + 2 < argc; i++) if (!_wcsicmp(argv[i], L"/clic")) clics.push_back({ (float)_wtof(argv[i + 1]), (float)_wtof(argv[i + 2]) });
            if (!clics.empty()) {
                WNDCLASSEXW wc = { sizeof(wc) };
                wc.lpfnWndProc = WndProc;
                wc.hInstance = inst;
                wc.lpszClassName = L"MWCoopCapture";
                RegisterClassExW(&wc);
                g_wnd = CreateWindowExW(0, wc.lpszClassName, L"MWCoop", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, inst, NULL);
                FILE *lf = _wfopen((std::wstring(argv[2]) + L".txt").c_str(), L"w");
                Bitmap tmp((INT)(kImgW * g_scale), (INT)(kImgH * g_scale), PixelFormat32bppPARGB);
                for (auto &c : clics) {
                    RenderTo(tmp, g_scale);
                    LPARAM lp = MAKELPARAM((int)((c.first + kM) * g_scale), (int)((c.second + kM) * g_scale));
                    SendMessageW(g_wnd, WM_MOUSEMOVE, 0, lp);
                    int hot = g_uiHot, btn = g_hot;
                    SendMessageW(g_wnd, WM_LBUTTONDOWN, MK_LBUTTON, lp);
                    SendMessageW(g_wnd, WM_LBUTTONUP, 0, lp);
                    if (lf) fprintf(lf, "clic %.0f,%.0f : zone %d bouton %d -> page %d menu %d champ %d partie %d zone tenue %d theme %d\n", c.first, c.second, hot, btn, g_tab, (int)g_lobbyOpts, g_focus, (int)g_partie, g_lookZone, (int)g_dark);
                }
                if (lf) fclose(lf);
                g_hot = -1; g_uiHot = -1;
            }
        }
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
        delete g_bg; delete g_bgDark; delete g_bgCache; SkinsFree(); SteamAvFree();
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

    // Lanceur lance hors du dossier du jeu (copie sur le bureau, dossier d'une ancienne version...) : les mises a jour
    // remplacent le MWCoop.exe du dossier du jeu, pas celui-ci, qui restait a sa version (retour d'un joueur, 08/10 :
    // lanceur 0.44 sur le bureau, mod en 0.59 ; salon et partage avec les amis d'une autre version). Celui du dossier du
    // jeu est plus recent : il est lance a notre place, avec la meme ligne de commande.
    if (!g_gameDir.empty() && _wcsicmp(g_gameDir.c_str(), g_dir.c_str())) {
        std::wstring there = g_gameDir + L"MWCoop.exe";
        unsigned long long mine = ExeVersion(g_self), theirs = FileExists(there) ? ExeVersion(there) : 0;
        if (theirs > mine && mine) {
            std::wstring cmd = L"\"" + there + L"\" " + PathGetArgsW(GetCommandLineW());
            STARTUPINFOW si = { sizeof(si) };
            PROCESS_INFORMATION pi = {};
            LaunchLog("lanceur plus recent dans le dossier du jeu (%s) : lance a la place de celui-ci", Narrow(there, CP_UTF8).c_str());
            if (CreateProcessW(there.c_str(), &cmd[0], NULL, NULL, FALSE, 0, NULL, g_gameDir.c_str(), &si, &pi)) {
                CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
                GdiplusShutdown(gtok);
                WSACleanup();
                return 0;
            }
            LaunchLog("lancement impossible (erreur %lu) : on continue avec celui-ci", GetLastError());
        }
    }

    // Taille : l'image a l'echelle de l'ecran (PPP), sans depasser 94 % de la zone de travail.
    HDC sdc = GetDC(NULL);
    g_scale = GetDeviceCaps(sdc, LOGPIXELSX) / 96.0f;
    ReleaseDC(NULL, sdc);
    RECT work;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    float fit = min((work.right - work.left) * 0.94f / kImgW, (work.bottom - work.top) * 0.97f / kImgH);
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
    if (!g_gameDir.empty() && !g_sConnect) TutoAtStartup();   // visite guidee : complete la 1re fois, puis les nouveautes
    if (!g_sConnect && g_tuto < 0) DiscordAskMaybe();          // invitation au Discord (une fois ; sinon a la fin de la visite)
    if (!g_gameDir.empty()) StartUpdate();
    else {
        HANDLE nt = CreateThread(NULL, 0, NotesOnlyThread, NULL, 0, NULL);
        if (nt) CloseHandle(nt);
    }
    TenuesStart();   // (tenues offertes : une fois, a part des mises a jour)

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    if (g_proc) CloseHandle(g_proc);
    delete g_bg; delete g_bgDark; delete g_bgCache; SkinsFree(); SteamAvFree();
    GdiplusShutdown(gtok);
    return 0;
}
