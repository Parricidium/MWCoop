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
//            salon-invite|salon-udp|salon-steam|salon-steam-amis|salon-steam-invite|menu-steam|menu-ip|guide-steam|mods|mods-page|mods-absent|crash|crash-survol|serveur|partie-invite|salon-options|tuto-<n>|tuto-maj|notes-image|salon-mods|salon-mods-demande|salon-mods-telechargement|salon-mods-prets|salon-public|salon-max|salon-moi|salon-steam-code|contenu|tenue-perso [vue]> [/theme clair|sombre] [/lang fr|en] [/echelle k] [/skins <dossier>] : rendu d'un
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
#include <wincodec.h>
#include <d3d12.h>       // (ray tracing : carte compatible ? d3d12.dll chargee a la demande, pas d'import)
#include <string>
#include <vector>
#include <map>
#include <unordered_map>
#include <set>
#include <array>
#include <atomic>
#include <mutex>
#include <stdio.h>
#include <math.h>
#include <stdarg.h>
#include <string.h>
#include <time.h>

using namespace Gdiplus;
#include <windowsx.h>
// Fenetres de message au style du lanceur (dialog.inc, demande de JD du 19/10) : toutes les MessageBoxW du lanceur y passent.
static int StyledBox(HWND owner, LPCWSTR text, LPCWSTR caption, UINT type);
#define MessageBoxW(h, t, c, f) StyledBox(h, t, c, f)

static std::wstring g_repo = L"Parricidium/MWCoop";   // (/depot : autre depot, pour tester la mise a jour)
// Le jeu (demande de JD, 19/10 : « un switch MSC et MWC a la place de Legacy et Reworked ») : [Lanceur] JeuChoisi=mwc|msc,
// change en haut a gauche (sous le numero de version) ; le lanceur se relance sur l'autre jeu. Tout ce qui nomme le jeu
// passe par GM() ; les textes de T() qui disent My Winter Car / mywintercar sont adaptes a la volee (GameSwap).
struct GameDef {
    const wchar_t *id, *name, *exe, *data;   // "msc", "My Summer Car", "mysummercar.exe", "mysummercar_Data"
    const wchar_t *steamDir;                 // steamapps\common\<...>
    const wchar_t *appid; uint32_t app;      // Steam
    const wchar_t *iniDir;                   // [Lanceur] cle du dossier retenu
    const wchar_t *docsMods;                 // Documents\<...>\Mods (MSCLoader)
};
enum { GAME_MWC, GAME_MSC };
static const GameDef kGames[2] = {
    { L"mwc", L"My Winter Car", L"mywintercar.exe", L"mywintercar_Data", L"My Winter Car", L"4164420", 4164420, L"Jeu", L"My Winter Car" },
    { L"msc", L"My Summer Car", L"mysummercar.exe", L"mysummercar_Data", L"My Summer Car", L"516750", 516750, L"JeuMSC", L"MySummerCar" },
};
static int g_game = GAME_MWC;
static const GameDef &GM() { return kGames[g_game]; }
// Nom du mod affiche : « MSCoop » sur My Summer Car (retour de Vaydon, 19/10 : « It should be MSCOOP »), « MWCoop » sinon. Les
// noms de fichiers et de dossiers (MWCoop.dll, dossier MWCoop, %LOCALAPPDATA%\MWCoop) ne changent pas.
static const wchar_t *ProductName() { return g_game == GAME_MSC ? L"MSCoop" : L"MWCoop"; }
static std::wstring StoreUrl() { return std::wstring(L"https://store.steampowered.com/app/") + GM().appid + L"/"; }
static void GameSwapIn(std::wstring &s)
{
    static const wchar_t *from[] = { L"My Winter Car", L"mywintercar" }, *to[] = { L"My Summer Car", L"mysummercar" };
    for (int k = 0; k < 2; k++) for (size_t p = s.find(from[k]); p != std::wstring::npos; p = s.find(from[k], p + wcslen(to[k]))) s.replace(p, wcslen(from[k]), to[k]);
    // Nom du mod : MWCoop -> MSCoop, sauf dans un chemin ou un nom de fichier (\MWCoop, MWCoop.dll, MWCoop\, MWCoop-lanceur...).
    for (size_t p = s.find(L"MWCoop"); p != std::wstring::npos; p = s.find(L"MWCoop", p + 6))
    {
        wchar_t before = p > 0 ? s[p - 1] : L' ', after = p + 6 < s.size() ? s[p + 6] : L' ';
        if (wcschr(L"\\/%", before) || wcschr(L"\\/.-_", after)) continue;
        s[p + 1] = L'S';
    }
}
// Texte d'un T() pour le jeu choisi : tel quel pour MWC ; pour MSC, une copie adaptee gardee (adresse stable ; cle = le texte
// d'origine lui-meme, pas son adresse : T() recoit parfois le c_str() d'une chaine temporaire).
static const wchar_t *GameSwap(const wchar_t *s)
{
    if (g_game == GAME_MWC || !s || (!wcsstr(s, L"My Winter Car") && !wcsstr(s, L"mywintercar") && !wcsstr(s, L"MWCoop"))) return s;
    if (wcsstr(s, L"My Summer Car")) return s;   // (texte qui nomme les deux jeux : le switch, son guide)
    static std::mutex mx;
    static std::unordered_map<std::wstring, std::wstring> cache;
    std::lock_guard<std::mutex> lk(mx);
    auto it = cache.find(s);
    if (it != cache.end()) return it->second.c_str();
    std::wstring t = s;
    GameSwapIn(t);
    return cache.emplace(s, t).first->second.c_str();
}
static const float kCardW = 1280, kCardH = 800, kM = 20;          // carte du lanceur (coordonnees de la mise en page)
static const float kImgW = kCardW + 2 * kM, kImgH = kCardH + 2 * kM;   // fenetre : la carte et son ombre

// ---------------------------------------------------------------- etat
enum { ST_IDLE, ST_LAUNCH, ST_CLOSING, ST_RUNNING };   // ST_RUNNING : jeu lance, lanceur reste ouvert (serveur.inc)
enum { K_NORMAL, K_OK, K_WARN, K_ERR };
enum { MODE_SOLO, MODE_HOST, MODE_GUEST };
enum { REL_OFFLINE = -1, REL_NONE = 0, REL_OK = 1 };   // reponse de GitHub

static HWND g_wnd;
static bool g_fr, g_ru, g_ar;   // langue : francais, russe (13/10 : traduction de l'anglais, ru.inc), arabe (ar.inc), sinon anglais
static std::wstring g_dir, g_self, g_iniLauncher;   // dossier du lanceur (avec \), chemin du lanceur
static std::wstring g_gameDir, g_gameVer;           // dossier de My Winter Car (avec \, vide = introuvable), version
// Ou vivent les fichiers de MWCoop du jeu choisi (version.dll, dossier MWCoop : mod, reglages, profils, journaux, cache) :
//  - lanceur installe dans %LOCALAPPDATA%\MWCoop (JD, 19/10 : « comme la plupart des mods », raccourci sur le bureau) :
//    <lanceur>\MWC\ ou <lanceur>\MSC\ ; le jeu part toujours de la copie de lancement (MWCoop y est une jonction vers ce
//    dossier, version.dll y est pose par le lanceur) : rien n'est ecrit dans le dossier du jeu ;
//  - lanceur dans le dossier du jeu (avant 1.1), ou [Lanceur] DansLeJeu=1 (Wine / Proton par defaut : jonctions pas
//    sures) : le dossier du jeu, comme avant.
static std::wstring g_modRoot;
static std::wstring g_afterInstall;   // (+apres-installation <ancien lanceur> : install.inc)
static std::wstring LocalDir();
static bool IsWine();
static bool RunningFromHome() { std::wstring l = LocalDir(); return !l.empty() && !_wcsicmp(g_dir.c_str(), l.c_str()); }
static bool ModInGame() { return !RunningFromHome() || GetPrivateProfileIntW(L"Lanceur", L"DansLeJeu", IsWine() ? 1 : 0, g_iniLauncher.c_str()) != 0; }
static std::wstring ModRootFor(const std::wstring &gameDir) { return gameDir.empty() ? L"" : ModInGame() ? gameDir : g_dir + (g_game == GAME_MSC ? L"MSC\\" : L"MWC\\"); }
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
#define WM_APP_LATE (WM_APP + 4)        // invite en retard (salon IP) : l'hote joue, ses mods arrivent
#define WM_APP_LATEEND (WM_APP + 5)     // ... connexion finie

// Salon : etat partage entre la fenetre et les fils reseau (sous g_lcs)
enum { LB_NONE, LB_HOST, LB_CONNECTING, LB_GUEST };
enum { PARTIE_CONTINUER, PARTIE_NOUVELLE };
static const int kLobbyMaxAbs = 16;                 // joueurs au plus dans un salon (tableaux) ; le choix de l'hote : g_lobbyMax
static int g_lobbyMax = 8;                          // joueurs dans le salon, hote compris (hote : [Lanceur] SalonMax, 2 a 16 ; invite : recu de l'hote)
static bool g_lobbyPrive = true;                    // salon prive (code de session demande) ou public ([Lanceur] SalonPrive)
static std::string g_lobbyCode;                     // code de session (hote : le sien ; invite : celui recu dans STATE)
static std::string g_joinCode;                      // invite : code saisi sur l'accueil (champ CODE), envoye dans HELLO
static bool g_lobbyOptGo;                           // GO / LANCER : les options du salon vont dans lancement.ini (WriteLaunchFile)
static bool g_sCodeAsk;                             // salon Steam prive sans invitation : le code est demande avant d'entrer (steam.inc)
// Test UDP du salon (le jeu passe en UDP sur le port du salon) : sans objet (ancien lanceur), en cours, recu, bloque.
enum { UDP_NA, UDP_WAIT, UDP_OK, UDP_FAIL };
struct LobbyPeer { int id; std::string name, skin, ver; bool ready; int ping; int udp; DWORD since; uint64_t sid; int dl = -1; };   // sid : compte Steam (salon Steam) ; dl : invite qui recoit l'envoi de l'hote (%), -1 fini ou rien, -2 pas encore dit
static std::atomic<int> g_lobby(LB_NONE);
static CRITICAL_SECTION g_lcs;
static std::vector<LobbyPeer> g_peers;
static int g_myId;
static std::atomic<int> g_partie(PARTIE_CONTINUER); // choix de l'hote (recopie chez les invites)
static std::wstring g_hostSaveName;                  // invite : nom de la sauvegarde de l'hote (salon ; g_lcs)
static std::atomic<bool> g_meReady(false);          // invite : pret
static bool g_goWait;                               // invite : GO recu, lancement dans un instant
static bool g_joinFallback;                         // invite : pas de salon chez l'hote -> rejoindre directement en jeu
static std::string g_rejectWhy;                     // invite : raison du refus (sous g_lcs)
static std::wstring g_testSalon, g_testSalonLog;    // /testsalon hote|invite <journal>
static std::wstring g_exportDir;                    // /exportcarte <msc|mwc> <dossier> : export de la carte pour l'editeur de carte (editor\)
static std::wstring g_testPartie = L"continuer";    // /testsalon : choix de l'hote
static std::string g_testCode;                       // /testsalon ... /code <X> : code de session (hote : le sien, salon prive ; invite : celui qu'il envoie)
static bool g_testPublic;                            // /testsalon hote ... /public : salon public (pas de code)
static bool g_testNoUdp;
static DWORD g_testHoldMs, g_testSlowMs;              // ... ; /lent <ms> (invite) : un morceau des mods par <ms>
static bool g_testForce;                             // /testsalon hote ... /forcer : LANCER des qu'un invite est la, pret ou non                          // /testsalon hote ... /garder <ms> : apres LANCER, reste ouvert (invites en retard)                            // /testsalon hote ... /sansudp : l'hote ne repond pas en UDP
static std::atomic<int> g_udpMine(UDP_NA);          // invite : reponse UDP de l'hote a ses sondes
static std::atomic<bool> g_hostUdpTest(false);      // invite : l'hote fait le test UDP (sinon : ancien lanceur)
static std::wstring g_launchWarn;                   // ecran d'attente : avertissement (UDP non confirme)
static uint64_t g_sHost;                            // salon Steam : compte de l'hote (HoteSteam de lancement.ini ; steam.inc)
static std::wstring g_sHostName;                    // ... et son nom Steam
static void SteamDown();
// Russe : la traduction du texte anglais (ru.inc, generee par ru_gen.py), l'anglais s'il n'y est pas.
#include "ru.inc"
#include "ar.inc"
static const wchar_t *Ru(const wchar_t *en)
{
    static std::unordered_map<std::wstring, const wchar_t *> m;
    if (m.empty()) for (const auto &p : kRu) m[p[0]] = p[1];
    auto it = m.find(en);
    return it != m.end() ? it->second : en;
}
// Arabe : chaque texte traduit est entoure (dans ar.inc) d'un debut et d'une fin d'incorporation droite-a-gauche
// (U+202B ... U+202C, par ligne) : il se lit de droite a gauche sans changer la mise en page du lanceur.
static const wchar_t *Ar(const wchar_t *en)
{
    static std::unordered_map<std::wstring, const wchar_t *> m;
    if (m.empty()) for (const auto &p : kAr) m[p[0]] = p[1];
    auto it = m.find(en);
    return it != m.end() ? it->second : en;
}
// Notes de version en arabe : meme incorporation droite-a-gauche, ligne par ligne (pas les lignes d'image, \x01).
static std::wstring ArNote(const std::wstring &s)
{
    std::wstring o;
    for (size_t a = 0; a <= s.size();) {
        size_t e = s.find(L'\n', a);
        std::wstring l = s.substr(a, e == std::wstring::npos ? std::wstring::npos : e - a);
        if (!l.empty() && l[0] != L'\x01') o += L"\u202B" + l + L"\u202C"; else o += l;
        if (e == std::wstring::npos) break;
        o += L'\n'; a = e + 1;
    }
    return o;
}
static const wchar_t *T(const wchar_t *fr, const wchar_t *en) { return GameSwap(g_fr ? fr : g_ru ? Ru(en) : g_ar ? Ar(en) : en); }
static std::wstring T(const std::wstring &fr, const std::wstring &en) { std::wstring t = g_fr ? fr : g_ru ? std::wstring(Ru(en.c_str())) : g_ar ? std::wstring(Ar(en.c_str())) : en; if (g_game != GAME_MWC && t.find(L"My Summer Car") == std::wstring::npos) GameSwapIn(t); return t; }
static const wchar_t *LangCode() { return g_fr ? L"fr" : g_ru ? L"ru" : g_ar ? L"ar" : L"en"; }
static void SetLang(const wchar_t *code)   // fr, en, ru : gardee dans [Lanceur] Langue
{
    g_fr = !_wcsicmp(code, L"fr"); g_ru = !_wcsicmp(code, L"ru"); g_ar = !_wcsicmp(code, L"ar");
    WritePrivateProfileStringW(L"Lanceur", L"Langue", LangCode(), g_iniLauncher.c_str());
}

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
    static char b[16384];   // (liste des DLL du jeu, mode debogage)
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

// ---------------------------------------------------------------- Wine / Proton (Linux, Steam Deck)
// Le jeu n'existe que pour Windows : sous Linux il tourne par Proton (le Wine de Steam), MWCoop avec lui (question de JD,
// 18/10 : pas de portage, une compatibilite Proton). Wine se reconnait a ntdll!wine_get_version. Sous Wine : ni pare-feu,
// ni modes de compatibilite, ni Smart App Control de Windows (rien a verifier ni a retirer) ; jonctions incertaines (le
// jeu part de son dossier par defaut : SansCopie) ; jeu lance directement par le lanceur, qui lui passe son environnement
// (Steam, WINEDLLOVERRIDES) ; notre version.dll n'est prise par Wine qu'avec version=n,b dans WINEDLLOVERRIDES (pose ici
// avant chaque lancement) ou dans le registre du prefixe pour mywintercar.exe (pose aussi : le bouton JOUER de Steam la
// prend alors sans option de lancement). Rien de tout cela ne change sous Windows.
// Essai sous Windows : variable MWCOOP_WINE=<version> fait comme sous Wine (sans toucher au registre).
static std::string g_wine;       // version de Wine ("" : Windows)
static std::string g_wineInfo;   // pour le journal : hote, build, Steam Deck, prefixe
static bool g_wineSim;           // essai MWCOOP_WINE
static bool IsWine() { return !g_wine.empty(); }
static void WineDetect()
{
    typedef const char *(__cdecl *StrFn)();
    typedef void (__cdecl *HostFn)(const char **, const char **);
    HMODULE nt = GetModuleHandleW(L"ntdll.dll");
    StrFn ver = nt ? (StrFn)GetProcAddress(nt, "wine_get_version") : NULL;
    char b[512];
    if (ver) { const char *v = ver(); g_wine = v && *v ? v : "?"; }
    else if (GetEnvironmentVariableA("MWCOOP_WINE", b, sizeof b) && b[0]) { g_wine = b; g_wineSim = true; }
    if (!IsWine()) return;
    HostFn host = nt ? (HostFn)GetProcAddress(nt, "wine_get_host_version") : NULL;
    StrFn build = nt ? (StrFn)GetProcAddress(nt, "wine_get_build_id") : NULL;
    if (host) { const char *s = NULL, *r = NULL; host(&s, &r); if (s) g_wineInfo += std::string(s) + (r ? std::string(" ") + r : ""); }
    if (build) { const char *s = build(); if (s && *s) g_wineInfo += std::string(g_wineInfo.empty() ? "" : ", ") + "build " + s; }
    if (g_wineSim) g_wineInfo += "essai MWCOOP_WINE";
    bool deck = GetEnvironmentVariableA("SteamDeck", b, sizeof b) && b[0] == '1';
    g_wineInfo += deck ? ", Steam Deck" : "";
    if (GetEnvironmentVariableA("STEAM_COMPAT_DATA_PATH", b, sizeof b) && b[0]) g_wineInfo += std::string(", Proton (prefixe ") + b + ")";
    if (GetEnvironmentVariableA("WINEDLLOVERRIDES", b, sizeof b)) g_wineInfo += std::string(", WINEDLLOVERRIDES=") + b;
    else g_wineInfo += ", sans WINEDLLOVERRIDES";
}

// WINEDLLOVERRIDES du lanceur, dont le jeu herite : version (notre chargeur) et winhttp (UnityDoorstop de MSCLoader) en
// native puis builtin -- sinon Wine prend les siennes et le mod ne se charge pas, sans un mot. Les autres entrees du
// joueur restent. Et, une fois (Wine seulement, pas l'essai), la meme chose pour mywintercar.exe dans le registre du
// prefixe (HKCU\Software\Wine\AppDefaults\mywintercar.exe\DllOverrides), retiree par la desinstallation.
static const wchar_t *const kWineDlls[] = { L"version", L"winhttp" };
static std::wstring WineAppKey() { return std::wstring(L"Software\\Wine\\AppDefaults\\") + GM().exe + L"\\DllOverrides"; }
static void WineOverrides(const char *where)
{
    if (!IsWine()) return;
    wchar_t buf[4096] = L"";
    GetEnvironmentVariableW(L"WINEDLLOVERRIDES", buf, 4096);
    std::wstring cur = buf, out;
    bool good[2] = { false, false }, changed = false;
    for (size_t i = 0; i <= cur.size();) {   // entrees "dll1,dll2=mode" separees par ';' (le mode peut contenir des virgules)
        size_t j = cur.find(L';', i);
        if (j == std::wstring::npos) j = cur.size();
        std::wstring e = Trim(cur.substr(i, j - i));
        i = j + 1;
        if (e.empty()) continue;
        size_t eq = e.find(L'=');
        std::wstring dlls = eq == std::wstring::npos ? e : e.substr(0, eq), mode = eq == std::wstring::npos ? L"" : e.substr(eq);
        bool native = mode.size() > 1 && towlower(mode[1]) == L'n';
        std::wstring keep;
        for (size_t a = 0; a <= dlls.size();) {
            size_t z = dlls.find(L',', a);
            if (z == std::wstring::npos) z = dlls.size();
            std::wstring d = Trim(dlls.substr(a, z - a)), dl = d;
            a = z + 1;
            for (auto &c : dl) c = towlower(c);
            if (dl.size() > 4 && dl.compare(dl.size() - 4, 4, L".dll") == 0) dl.resize(dl.size() - 4);
            int k = dl == kWineDlls[0] ? 0 : dl == kWineDlls[1] ? 1 : -1;
            if (k >= 0 && native && !good[k]) { good[k] = true; keep += (keep.empty() ? L"" : L",") + d; continue; }
            if (k >= 0) { changed = true; continue; }   // (builtin, coupee, ou en double : retiree, remise plus bas)
            if (!d.empty()) keep += (keep.empty() ? L"" : L",") + d;
        }
        if (!keep.empty()) out += (out.empty() ? L"" : L";") + keep + mode;
    }
    for (int k = 0; k < 2; k++) if (!good[k]) { out += (out.empty() ? L"" : L";") + std::wstring(kWineDlls[k]) + L"=n,b"; changed = true; }
    if (changed) SetEnvironmentVariableW(L"WINEDLLOVERRIDES", out.c_str());
    LaunchLog("Wine (%s) : WINEDLLOVERRIDES %s%s", where, changed ? "pose pour le jeu : " : "deja bon : ", Narrow(out, CP_UTF8).c_str());
    static bool reg;
    if (reg || g_wineSim) return;
    reg = true;
    HKEY k;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, WineAppKey().c_str(), 0, NULL, 0, KEY_SET_VALUE, NULL, &k, NULL) == ERROR_SUCCESS) {
        bool ok = true;
        for (const wchar_t *d : kWineDlls) ok &= RegSetValueExW(k, d, 0, REG_SZ, (const BYTE *)L"native,builtin", 15 * sizeof(wchar_t)) == ERROR_SUCCESS;
        RegCloseKey(k);
        LaunchLog("Wine : version et winhttp en native,builtin pour mywintercar.exe dans le registre du prefixe%s", ok ? "" : " -- ECHEC");
    } else LaunchLog("Wine : registre du prefixe impossible a ecrire (AppDefaults\\mywintercar.exe)");
}

// ---------------------------------------------------------------- My Winter Car
static bool IsGameDirOf(const std::wstring &d, int game)
{
    const GameDef &gd = kGames[game];
    return !d.empty() && FileExists(d + gd.exe) && FileExists(d + gd.data + L"\\Managed\\Assembly-CSharp.dll");
}
static bool IsGameDir(const std::wstring &d) { return IsGameDirOf(d, g_game); }
// Jeu d'un chemin d'exe (mywintercar.exe, mysummercar.exe), -1 : aucun.
static int GameOfExe(const wchar_t *path)
{
    size_t n = wcslen(path);
    for (int k = 0; k < 2; k++) { size_t m = wcslen(kGames[k].exe); if (n >= m && !_wcsicmp(path + n - m, kGames[k].exe)) return k; }
    return -1;
}
// Jeu choisi : [Lanceur] JeuChoisi (switch MSC / MWC) ; sinon celui du dossier du lanceur, sinon My Winter Car.
static void GamePick()
{
    wchar_t b[8] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"JeuChoisi", L"", b, 8, g_iniLauncher.c_str());
    if (!_wcsicmp(b, L"msc")) g_game = GAME_MSC;
    else if (!_wcsicmp(b, L"mwc")) g_game = GAME_MWC;
    else g_game = IsGameDirOf(g_dir, GAME_MSC) && !IsGameDirOf(g_dir, GAME_MWC) ? GAME_MSC : GAME_MWC;
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
    GetPrivateProfileStringW(L"Lanceur", GM().iniDir, L"", saved, MAX_PATH, g_iniLauncher.c_str());
    if (saved[0] && IsGameDir(WithSlash(saved))) return WithSlash(saved);
    std::vector<std::wstring> libs;
    SteamLibraries(libs);
    for (const std::wstring &l : libs) {
        std::wstring d = l + L"steamapps\\common\\" + GM().steamDir + L"\\";
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
struct Field { std::wstring text; RectF r; size_t maxLen; bool address; bool code; };
static Field g_fields[3];   // 0 = pseudo, 1 = adresse, 2 = code de session (salon prive)
static int g_focus = -1;
static int g_carColor = -1;   // [Coop] CouleurVoiture : 0xRRGGBB, -1 = au hasard (comme le jeu)

static std::wstring ModIni() { return g_modRoot + L"MWCoop\\mwcoop.ini"; }
static std::string ModIniA() { return Narrow(ModIni()); }
static void EnsureModDir() { if (!g_gameDir.empty()) CreateDirectoryW((g_modRoot + L"MWCoop").c_str(), NULL); }

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
    GetPrivateProfileStringW(L"Lanceur", L"DernierCode", L"", (wchar_t *)v, 32, g_iniLauncher.c_str());   // (dernier code de session saisi)
    g_fields[2].text = (wchar_t *)v;
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
    WritePrivateProfileStringW(L"Lanceur", L"DernierCode", Trim(g_fields[2].text).c_str(), g_iniLauncher.c_str());
    WritePrivateProfileStringA("Coop", "Langue", g_fr ? "fr" : g_ru ? "ru" : "en", ini.c_str());   // textes du mod en jeu dans la langue du lanceur (arabe : anglais en jeu)
}

static void LoadLocalVersion()
{
    std::vector<unsigned char> d;
    g_localVer.clear();
    // (a part : version.dll est pose par le lanceur dans la copie de lancement, a partir du sien ; pas besoin ici)
    g_modOk = !g_gameDir.empty() && (!ModInGame() || FileExists(g_modRoot + L"version.dll")) && FileExists(g_modRoot + L"MWCoop\\MWCoop.dll");
    if (g_modOk && ReadAll(g_modRoot + L"MWCoop\\version.txt", d))
        g_localVer = Trim(Widen(std::string(d.begin(), d.end())));
}

// "MWCoop 0.1.0", "MWCoop (dev)" (fichiers du mod sans version.txt), "MWCoop".
static std::wstring ModLabel()
{
    if (!g_localVer.empty()) return std::wstring(ProductName()) + L" " + g_localVer;
    return std::wstring(ProductName()) + (g_modOk ? L" (dev)" : L"");
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
    g_modRoot = ModRootFor(dir);
    g_gameVer = ReadGameVersion();
    LoadLocalVersion();
    if (!g_gameDir.empty()) LoadPlayer();
    TenuesAddShirts();
}

// "0.75.35", ou ancien "0.1.0-prealpha" : nombres compares un a un, puis le suffixe (a egalite, sans suffixe = plus recent ;
// "0.96.0-nightly.10" apres "0.96.0-nightly.9" : nombres du suffixe compares comme des nombres).
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
    for (size_t x = 0, y = 0; x < sa.size() && y < sb.size();) {
        if (iswdigit(sa[x]) && iswdigit(sb[y])) {
            long na = wcstol(sa.c_str() + x, NULL, 10), nb = wcstol(sb.c_str() + y, NULL, 10);
            if (na != nb) return na < nb ? -1 : 1;
            while (x < sa.size() && iswdigit(sa[x])) x++;
            while (y < sb.size() && iswdigit(sb[y])) y++;
            continue;
        }
        if (sa[x] != sb[y]) return sa[x] < sb[y] ? -1 : 1;
        x++; y++;
    }
    return sa.size() < sb.size() ? -1 : 1;
}

// ---------------------------------------------------------------- HTTP (WinHTTP)
// GET sur une URL https ; corps dans out (ou dans le fichier toFile), progression 0..1 si progress ; status : code HTTP
// (0 = pas de reponse).
// Derniere erreur WinHTTP d'une requete sans reponse (12007 nom introuvable, 12029 connexion impossible, 12002 delai...).
static DWORD g_httpErr;

static bool HttpGet(const std::wstring &url, std::string *out, const std::wstring &toFile, bool progress, DWORD *status = NULL, float *prog = NULL)   // (prog : ailleurs que g_progress)
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
                if (progress && total) { if (prog) *prog = (float)got / total; else g_progress = (float)got / total; }
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

// Fichier verrouille (DLL chargee par un jeu ouvert, ou par un jeu bloque a la fermeture : Matilda, 18/10) : Windows permet
// de le renommer. Mis de cote en <nom>.old, ou <nom>.old1 ... .old9 si un ancien .old est lui-meme encore tenu (efface au
// prochain demarrage, OldFilesCleanup). Vrai si le nom est libre.
static bool MoveAside(const std::wstring &d, std::wstring *as = NULL)
{
    for (int i = 0; i < 10; i++) {
        std::wstring old = d + L".old" + (i ? std::to_wstring(i) : L"");
        DeleteFileW(old.c_str());
        if (MoveFileExW(d.c_str(), old.c_str(), MOVEFILE_REPLACE_EXISTING)) { if (as) *as = old; return true; }
    }
    return false;
}

// Restes des mises a jour (version.dll.old, .old1...) : effaces quand plus rien ne les tient.
static void OldFilesCleanup(const std::wstring &dir)
{
    if (dir.empty()) return;
    for (int i = 0; i < 10; i++) DeleteFileW((dir + L"version.dll.old" + (i ? std::to_wstring(i) : L"")).c_str());
}

// Copie l'arbre extrait dans le dossier du jeu. selfReplaced : le lanceur en cours a ete remplace.
static DWORD g_copyError;
static bool g_loaderLate;   // version.dll du dossier du jeu pas remplacee (tenue par un jeu bloque) : remise au prochain lancement
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
        // (lanceur a part : son exe et les textes du paquet restent hors de MWC\ / MSC\ -- l'exe est remplace plus bas)
        if (!ModInGame() && !_wcsicmp(dst.c_str(), g_modRoot.substr(0, g_modRoot.size() - 1).c_str())
            && (!_wcsicmp(n.c_str(), L"MWCoop.exe") || !_wcsicmp(n.c_str(), L"LISEZMOI.txt") || !_wcsicmp(n.c_str(), L"README.txt"))) continue;
        if (!_wcsicmp(d.c_str(), (g_modRoot + L"MWCoop\\version.txt").c_str())) continue;   // en dernier (UpdateThread)
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
            std::wstring as;
            bool locked = e == ERROR_SHARING_VIOLATION || e == ERROR_ACCESS_DENIED || e == ERROR_LOCK_VIOLATION;
            if (locked && MoveAside(d, &as) && CopyFileW(s.c_str(), d.c_str(), FALSE))
                LaunchLog("mise a jour : %s verrouille (erreur %lu), l'ancien mis de cote (%s)", Narrow(n, CP_UTF8).c_str(), e, Narrow(as.substr(as.rfind(L'\\') + 1), CP_UTF8).c_str());
            else if (!_wcsicmp(d.c_str(), (g_modRoot + L"version.dll").c_str()) && FileExists(d)) {
                // Le chargeur seul : la mise a jour continue, LoaderEnsure le remplace avant le prochain lancement.
                g_loaderLate = true;
                LaunchLog("mise a jour : version.dll tenue (erreur %lu, renommage %lu) : gardee, remplacee au prochain lancement", e, GetLastError());
            }
            else { g_copyError = e; ok = false; *failed = n; LaunchLog("mise a jour : %s IMPOSSIBLE a remplacer (erreur %lu, renommage %lu)", Narrow(n, CP_UTF8).c_str(), e, GetLastError()); }
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
// (depuis 0.92.3, demande de JD : encore une ligne "---" et en russe ; sans : l'anglais en russe)
struct Note { std::wstring ver, date, fr, en, ru, ar, zip, editor; bool pre = false; };   // pre : pre-release de GitHub (Nightly) ; editor : paquet de l'editeur de carte (.editeur, outils.inc)
static const std::wstring &NoteBody(const Note &n) { return g_fr ? n.fr : g_ru && !n.ru.empty() ? n.ru : g_ar && !n.ar.empty() ? n.ar : n.en; }
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
            std::wstring rest = enAt == std::wstring::npos ? L"" : body.substr(enAt + 1);
            size_t sep2 = rest.find(L"\n---");   // (russe apres une seconde ligne ---)
            if (sep2 != std::wstring::npos) {
                size_t ruAt = rest.find(L'\n', sep2 + 1);
                std::wstring ruPart = ruAt == std::wstring::npos ? L"" : rest.substr(ruAt + 1);
                size_t sep3 = ruPart.find(L"\n---");   // (arabe apres une troisieme ligne ---)
                if (sep3 != std::wstring::npos) {
                    size_t arAt = ruPart.find(L'\n', sep3 + 1);
                    n.ar = ArNote(CleanNote(arAt == std::wstring::npos ? L"" : ruPart.substr(arAt + 1)));
                    ruPart = ruPart.substr(0, sep3);
                }
                n.ru = CleanNote(ruPart);
                rest = rest.substr(0, sep2);
            }
            n.en = CleanNote(rest);
            if (n.en.empty()) n.en = n.fr;
        }
        for (size_t k = p; k < end;) {
            size_t u = json.find("\"browser_download_url\"", k);
            if (u == std::string::npos || u >= end) break;
            std::string url = JsonField(json, "browser_download_url", u, end);
            k = u + 20;
            // (Nightly : paquet en .nightly, un zip sous un autre nom, que les lanceurs d'avant 0.95.2 ne prennent pas ; editeur de
            // carte : .editeur, a part -- 20/10)
            if (url.size() > 8 && !_stricmp(url.c_str() + url.size() - 8, ".editeur")) { if (n.editor.empty()) n.editor = Widen(url); continue; }
            if (!n.zip.empty()) continue;
            if (url.size() > 4 && !_stricmp(url.c_str() + url.size() - 4, ".zip")) { n.zip = Widen(url); continue; }
            if (url.size() > 8 && !_stricmp(url.c_str() + url.size() - 8, ".nightly")) { n.zip = Widen(url); n.pre = true; continue; }
        }
        bool draft = json.find("\"draft\": true", p) < end || json.find("\"draft\":true", p) < end;
        if (json.find("\"prerelease\": true", p) < end || json.find("\"prerelease\":true", p) < end || n.ver.find(L"nightly") != std::wstring::npos) n.pre = true;
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
    std::wstring cache = g_gameDir.empty() ? L"" : g_modRoot + L"MWCoop\\notes-maj.json";
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

// Versions installees : Reworked seulement, 0.96 et plus (JD, 19/10 : Legacy retiree pour de bon, le switch MSC / MWC prend sa
// place ; la cle [Lanceur] Version n'est plus lue). Une installation Legacy (moins de 0.96) passe a la plus recente, ses
// sauvegardes copiees avant (MWCoop\copie-avant-reworked, comme depuis 0.96).
static bool IsReworkedVer(const std::wstring &v) { return v.find(L"nightly") != std::wstring::npos || CmpVer(v, L"0.96.0") >= 0; }
// Versions d'essai (nightly, pre-releases de GitHub) : [Lanceur] Nightly=1 (Reglages > Lanceur) les propose comme mises a
// jour si elles sont plus recentes que la version stable ; sinon elles ne servent que de secours (aucune stable Reworked).
static bool NightlyOn() { return GetPrivateProfileIntW(L"Lanceur", L"Nightly", 0, g_iniLauncher.c_str()) != 0; }
static const Note *PickRelease(const std::vector<Note> &rel)
{
    const Note *best = NULL, *pre = NULL;
    for (const Note &n : rel) {
        if (n.zip.empty() || !IsReworkedVer(n.ver)) continue;
        if (!n.pre) { if (!best || CmpVer(n.ver, best->ver) > 0) best = &n; }
        else if (!pre || CmpVer(n.ver, pre->ver) > 0) pre = &n;
    }
    if (best && pre && NightlyOn() && CmpVer(pre->ver, best->ver) > 0) return pre;
    return best ? best : pre;
}
// A installer : plus recente, ou installation Legacy (meme vers une plus ancienne).
static bool WantVer(const std::wstring &remote, const std::wstring &local)
{
    return local.empty() || CmpVer(remote, local) > 0 || (!IsReworkedVer(local) && remote != local);
}
// My Summer Car (BETA, demande de JD du 19/10 : « les Nouveautes doivent parler de MSC ») : les versions d'avant kMscFirstVer
// sont celles de My Winter Car, cachees. Dans les suivantes, une puce propre a MWC est retiree : « MWC » (mot entier, « [MWC] »
// en tete compris) ou un mot de kMwcOnly, sauf si elle nomme aussi My Summer Car ou MSC (« [MSC] » en tete : gardee, l'etiquette
// enlevee). Decide sur les puces anglaises, applique aux autres langues (meme nombre de puces ; sinon chacune pour elle).
// Tout en bas, une entree « My Summer Car (BETA) » ecrite ici (MscBetaNote), tant que GitHub n'a pas la note de kMscFirstVer
// (hors ligne aussi). Sur My Winter Car : rien ne change.
static const wchar_t *kMscFirstVer = L"1.1.0";
static const wchar_t *kMwcOnly[] = { L"my winter car", L"corris", L"rivett", L"vaz", L"jokkis", L"sorbet", L"psk", L"osuuskauppa", L"kartturi",
                                     L"course sur glace", L"ice race", L"musée", L"museum", L"sac de couchage", L"sacs de couchage", L"sleeping bag" };
static bool NoteWord(const std::wstring &low, const wchar_t *w)   // mot entier (MWC, pas MWCoop)
{
    size_t n = wcslen(w);
    for (size_t p = low.find(w); p != std::wstring::npos; p = low.find(w, p + 1))
        if ((p == 0 || !iswalnum(low[p - 1])) && (p + n >= low.size() || !iswalnum(low[p + n]))) return true;
    return false;
}
static bool NoteMwcOnly(std::wstring s)
{
    if (!s.empty()) CharLowerBuffW(&s[0], (DWORD)s.size());
    if (s.find(L"my summer car") != std::wstring::npos || NoteWord(s, L"msc")) return false;
    if (NoteWord(s, L"mwc")) return true;
    for (const wchar_t *w : kMwcOnly) if (s.find(w) != std::wstring::npos) return true;
    return false;
}
// Puces d'une note nettoyee (CleanNote) : chacune avec ses lignes de suite et ses images ; ce qui precede la premiere, a part.
static std::vector<std::wstring> NoteItems(const std::wstring &body)
{
    std::vector<std::wstring> it;
    for (size_t a = 0; a < body.size();) {
        size_t e = body.find(L'\n', a);
        std::wstring l = body.substr(a, e == std::wstring::npos ? std::wstring::npos : e - a);
        a = e == std::wstring::npos ? body.size() : e + 1;
        size_t b = l.find_first_not_of(L" \x202B");   // (puce arabe : apres le debut d'incorporation droite-a-gauche)
        if ((b != std::wstring::npos && l[b] == L'•') || it.empty()) it.push_back(l); else it.back() += L"\n" + l;
    }
    return it;
}
// Note d'une version de MSC : sans les puces de MWC ; faux s'il n'en reste rien.
static bool NoteForMsc(Note &n)
{
    std::vector<std::wstring> en = NoteItems(n.en);
    std::vector<bool> keep;
    for (const std::wstring &i : en) keep.push_back(!NoteMwcOnly(i));
    auto filt = [&](std::wstring &body) {
        std::vector<std::wstring> it = NoteItems(body);
        std::wstring o;
        for (size_t k = 0; k < it.size(); k++) {
            if (!(it.size() == keep.size() ? keep[k] : !NoteMwcOnly(it[k]))) continue;
            std::wstring t = it[k];
            size_t p = t.find(L"[MSC] ");
            if (p != std::wstring::npos) t.erase(p, 6);
            o += (o.empty() ? L"" : L"\n") + t;
        }
        body = o;
    };
    filt(n.fr); filt(n.ru); filt(n.ar); filt(n.en);
    return !n.en.empty() || !n.fr.empty();
}
// L'entree « My Summer Car (BETA) » : ce qui marche, en puces (premiere puce = titre de l'accueil et de la colonne du Guide).
static Note MscBetaNote()
{
    static const wchar_t *b[][2] = {
        { L"My Summer Car arrive dans MWCoop, en BETA : choisis MSC sous le numéro de version, en haut à gauche du lanceur.",
          L"My Summer Car joins MWCoop, in BETA: pick MSC under the version number, top left of the launcher." },
        { L"Les joueurs, les tenues et les visages, le vocal de proximité, le tchat, les marqueurs, la sauvegarde de l'hôte envoyée aux invités.",
          L"Players, outfits and faces, proximity voice, chat, markers, the host's save sent to the guests." },
        { L"Voitures partagées : SATSUMA, GIFU, KEKMET, HAYOSIKO, FERNDALE, RUSCKO, JONNEZ, FLATBED et le bateau, avec les passagers, les portières et les tableaux de bord.",
          L"Shared cars: SATSUMA, GIFU, KEKMET, HAYOSIKO, FERNDALE, RUSCKO, JONNEZ, FLATBED and the boat, with passengers, doors and dashboards." },
        { L"HUD noir et jaune, carte M de l'été, onglets Dragsters et Rallye dans F10, vue à la 3e personne et caméra drone.",
          L"Black and yellow HUD, summer M map, Drag race and Rally tabs in F10, third-person view and drone camera." },
        { L"Lanceur : thème Été, MSCLoader pour My Summer Car, un Guide qui parle de My Summer Car.",
          L"Launcher: Summer theme, MSCLoader for My Summer Car, a Guide about My Summer Car." },
        { L"Pas encore : objets, pièces, argent, magasins, boulots, heure et météo. Ils arrivent au fil des versions.",
          L"Not yet: items, parts, money, shops, jobs, time and weather. They come over the next versions." } };
    Note n;
    n.ver = kMscFirstVer; n.date = L"BETA";
    for (auto &l : b) {
        const wchar_t *sep = n.en.empty() ? L"" : L"\n";
        n.fr += sep + std::wstring(L"• ") + l[0];
        n.en += sep + std::wstring(L"• ") + l[1];
        n.ru += sep + std::wstring(L"• ") + Ru(l[1]);
        n.ar += sep + std::wstring(L"• ") + Ar(l[1]);
    }
    return n;
}
// Notes montrees : tout (les anciennes aussi, c'est l'histoire), sauf les pre-releases d'avant 0.96 ; sur My Summer Car,
// celles de MSC seulement (plus haut).
static std::vector<Note> ChannelNotes(const std::vector<Note> &rel)
{
    std::vector<Note> out;
    for (const Note &n : rel) if (!n.pre || IsReworkedVer(n.ver)) out.push_back(n);
    if (g_game != GAME_MSC) return out;
    std::vector<Note> msc;
    bool first = false;
    for (Note n : out) {
        if (CmpVer(n.ver, kMscFirstVer) < 0 || !NoteForMsc(n)) continue;
        if (CmpVer(n.ver, kMscFirstVer) == 0) first = true;
        msc.push_back(n);
    }
    if (!first) msc.push_back(MscBetaNote());
    return msc;
}
static std::wstring SavesBackupNightly();   // (saves.inc)
static void ToolsSeen(const std::vector<Note> &rel);   // (outils.inc : derniere version de l'editeur de carte)

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
    const Note *pick = PickRelease(rel);
    ToolsSeen(rel);
    EnterCriticalSection(&g_cs);
    g_notes = ChannelNotes(rel);
    LeaveCriticalSection(&g_cs);
    g_relState = rs;
    g_notesDone = true;
    if (quiet) {   // (verification periodique : seulement le bouton, s'il y a du nouveau)
        std::wstring remote = pick ? pick->ver : L"";
        if (rs != REL_OFFLINE && !remote.empty() && !local.empty() && WantVer(remote, local) && UpdAvail() != remote) {
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
    std::wstring remote = pick ? pick->ver : L"", zipUrl = pick ? pick->zip : L"";   // la plus recente du canal, avec un paquet
    if (remote.empty() || zipUrl.empty()) {   // pas encore de release : rien a installer
        g_progress = -1;
        if (!mod) SetStatus(K_WARN, T(L"Aucune version de MWCoop publi\u00E9e pour l'instant", L"No MWCoop release published yet"));
        else SetStatus(K_OK, T(L"%s \u00B7 aucune version publi\u00E9e", L"%s \u00B7 no release published"), label.c_str());
        g_busy = false;
        return 0;
    }
    if (!local.empty() && !WantVer(remote, local)) {
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
    // Premier passage de Legacy a Reworked (par defaut depuis 0.96, sans clic) : sauvegardes copiees avant
    if (!local.empty() && !IsReworkedVer(local) && GetPrivateProfileIntW(L"Lanceur", L"CopieNightly", 0, g_iniLauncher.c_str()) == 0) {
        std::wstring dst = SavesBackupNightly();
        if (!dst.empty()) WritePrivateProfileStringW(L"Lanceur", L"CopieNightly", L"1", g_iniLauncher.c_str());
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
    g_loaderLate = false;
    LaunchLog("mise a jour : installation de %s (depuis %s)", Narrow(remote, CP_UTF8).c_str(), Narrow(local.empty() ? L"rien" : local, CP_UTF8).c_str());
    std::wstring gd = g_modRoot.substr(0, g_modRoot.size() - 1);
    SHCreateDirectoryExW(NULL, gd.c_str(), NULL);
    bool ok = CopyTree(ext, gd, &self, &failed);
    // Lanceur ailleurs que les fichiers du mod (installe a part, ou pose dans le dossier de l'AUTRE jeu) : il se met a jour lui aussi
    if (ok && !self && (RunningFromHome() || IsGameDirOf(g_dir, 1 - g_game)) && FileExists(ext + L"\\MWCoop.exe")) {
        std::wstring old = g_self + L".old";
        DeleteFileW(old.c_str());
        bool moved = MoveFileExW(g_self.c_str(), old.c_str(), MOVEFILE_REPLACE_EXISTING) != 0;
        if (moved && CopyFileW((ext + L"\\MWCoop.exe").c_str(), g_self.c_str(), FALSE)) self = true;
        else {
            LaunchLog("mise a jour : lanceur de %s pas remplace (erreur %lu)", Narrow(g_dir, CP_UTF8).c_str(), GetLastError());
            if (moved) MoveFileExW(old.c_str(), g_self.c_str(), MOVEFILE_REPLACE_EXISTING);   // (l'ancien remis en place)
        }
    }
    // version.txt seulement si tout est en place : une installation interrompue sera reprise au prochain lancement
    CreateDirectoryW((g_modRoot + L"MWCoop").c_str(), NULL);
    if (ok && !CopyFileW((ext + L"\\MWCoop\\version.txt").c_str(), (g_modRoot + L"MWCoop\\version.txt").c_str(), FALSE)) { ok = false; failed = L"version.txt"; }
    DeleteTree(work);
    g_progress = -1;
    if (!ok && g_copyError == ERROR_ACCESS_DENIED && !IsWritableDir(g_modRoot)) {
        SetStatus(K_ERR, T(L"Dossier du jeu prot\u00E9g\u00E9 : lance MWCoop.exe en administrateur pour mettre \u00E0 jour", L"Game folder is protected: run MWCoop.exe as administrator to update"));
        g_busy = false;
        return 0;
    }
    if (!ok) {
        LaunchLog("mise a jour : ECHEC sur %s (erreur %lu)", Narrow(failed, CP_UTF8).c_str(), g_copyError);
        SetStatus(K_ERR, T(L"%s est occup\u00E9 : ferme le jeu, puis relance le lanceur", L"%s is in use: close the game, then restart the launcher"), failed.c_str());
        g_busy = false;
        return 0;
    }
    LoadLocalVersion();
    LaunchLog("mise a jour : %s installee%s", Narrow(g_localVer.empty() ? remote : g_localVer, CP_UTF8).c_str(), g_loaderLate ? " (chargeur au prochain lancement)" : "");
    if (g_loaderLate) SetStatus(K_OK, T(L"Mis \u00E0 jour \u00B7 MWCoop %s (un jeu bloqu\u00E9 tient encore version.dll : remplac\u00E9e au prochain lancement)", L"Updated \u00B7 MWCoop %s (a stuck game still holds version.dll: replaced at the next launch)"), g_localVer.empty() ? remote.c_str() : g_localVer.c_str());
    else
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
    ToolsSeen(rel);
    EnterCriticalSection(&g_cs);
    g_notes = ChannelNotes(rel);
    LeaveCriticalSection(&g_cs);
    g_relState = rs;
    g_notesDone = true;
    return 0;
}

// Premier lanceur Reworked (0.96) sur une installation qui vient de l'ancienne version par la mise a jour normale (l'ancien
// lanceur a installe la 0.96.0 lui-meme, sans la copie faite au choix Legacy -> Reworked) : les sauvegardes sont copiees
// une fois (MWCoop\copie-avant-reworked), avant tout jeu. Appele au demarrage de la fenetre normale (pas /capture ni /testsalon).
static void FirstReworkedBackup()
{
    if (!g_modOk || g_localVer.empty() || g_iniLauncher.empty()) return;
    if (GetPrivateProfileIntW(L"Lanceur", L"CopieNightly", 0, g_iniLauncher.c_str()) != 0) return;
    if (!IsReworkedVer(g_localVer)) return;   // (encore en Legacy : rien, la copie se fera a la mise a jour)
    std::wstring dst = SavesBackupNightly();
    WritePrivateProfileStringW(L"Lanceur", L"CopieNightly", L"1", g_iniLauncher.c_str());   // (meme vide : rien a copier, on ne reessaie pas)
    TestLog("sauvegardes copiees avant le premier jeu en Reworked : %s", dst.empty() ? "(rien)" : Narrow(dst, CP_UTF8).c_str());
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
enum { TAB_HOME, TAB_LOBBY, TAB_SKIN, TAB_CAR, TAB_CONTENT, TAB_MODS, TAB_NOTES, TAB_LOGS, TAB_COOP, TAB_API, TAB_GFX, TAB_CREDITS, TAB_WIKI, TAB_VHS, TAB_ALBUM, TAB_SAVES, TAB_TOOLS, TAB_COUNT };
static int g_tab = TAB_HOME;

enum { B_HOST, B_JOIN, B_SOLO, B_EXE, B_BUY, B_THEME, B_CLOSE, B_MIN, B_LOGS, B_COLOR, B_LOGDIR, B_LOGZIP, B_GITHUB, B_KOFI, B_NETIP, B_NETSTEAM, B_UPDATE, B_LANG, B_DISCORD, B_COUNT };   // (Journaux, API et Credits : raccourcis de page de l'en-tete, ui.inc HeadPage)
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
static std::wstring GuestCopyDir();   // (modsync.inc)
static bool MscOwnApply(const std::wstring &m, bool enabled, bool guest);
static bool g_guide;                                 // guide "Jouer par Steam" ouvert (par-dessus tout le lanceur)
static int g_guideHot;                               // 1 copier, 2 compris, 3 ne plus afficher, 4 fermer
static bool g_guideNoMore;
static DWORD g_guideCopiedT;
struct Button { RectF r; float hover; bool visible, enabled; };
static Button g_btn[B_COUNT];
static int g_hot = -1, g_pressed = -1;

static bool PageEnabled(int p);   // (ui.inc)
static void Layout()
{
    g_fields[0].maxLen = 23; g_fields[0].address = false;
    g_fields[1].maxLen = 63; g_fields[1].address = true;
    g_fields[2].maxLen = 6; g_fields[2].code = true;   // code de session : 6 lettres / chiffres, en majuscules
    // (les autres boutons sont places par leur page en se dessinant : ui.inc)
    g_btn[B_DISCORD].r = RectF(28, 726, 104, 44);   // Discord (nom et logo), GitHub et Ko-fi (icones)
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
    Color solid;   // carte opaque (verre depoli coupe : [Lanceur] Verre=0)
};
static Theme kLight = {   // (pas const : la couleur d'accent se change dans Reglages > Theme, gfx.inc ThemeLoad)
    Color(255, 16, 30, 48), Color(255, 74, 90, 112), Color(150, 255, 255, 255), Color(190, 255, 255, 255), Color(34, 16, 30, 48),
    Color(105, 255, 255, 255), Color(200, 255, 255, 255), Color(42, 16, 30, 48), Color(60, 16, 30, 48),
    Color(150, 255, 255, 255), Color(64, 16, 30, 48), Color(255, 120, 136, 158),
    Color(120, 255, 255, 255), Color(205, 255, 255, 255), Color(235, 20, 40, 64), Color(130, 255, 255, 255), Color(215, 255, 255, 255),
    Color(140, 255, 255, 255), Color(225, 255, 255, 255), Color(255, 246, 250, 254), Color(255, 205, 220, 236),
    Color(255, 26, 98, 172), Color(255, 58, 132, 206), Color(255, 255, 255, 255), Color(255, 42, 84, 128),
    Color(255, 18, 132, 92), Color(255, 172, 100, 12), Color(255, 206, 40, 60), Color(255, 240, 247, 255) };
static Theme kDark = {
    Color(255, 238, 244, 255), Color(255, 164, 178, 202), Color(128, 14, 24, 52), Color(38, 255, 255, 255), Color(28, 255, 255, 255),
    Color(14, 255, 255, 255), Color(34, 255, 255, 255), Color(30, 255, 255, 255), Color(48, 255, 255, 255),
    Color(16, 255, 255, 255), Color(44, 255, 255, 255), Color(255, 132, 146, 170),
    Color(20, 255, 255, 255), Color(36, 255, 255, 255), Color(200, 30, 44, 74), Color(16, 255, 255, 255), Color(32, 255, 255, 255),
    Color(22, 255, 255, 255), Color(40, 255, 255, 255), Color(255, 10, 18, 34), Color(255, 24, 40, 64),
    Color(255, 166, 225, 255), Color(255, 200, 236, 255), Color(255, 6, 19, 37), Color(230, 48, 66, 100),
    Color(255, 124, 242, 196), Color(255, 255, 196, 107), Color(255, 255, 110, 110), Color(255, 22, 36, 70) };
// Aurore verte (maquette Theme, 17/10) : nuit bleu-vert profonde, contraste fort, accent menthe ; aurore verte seule.
static Theme kAurore = {
    Color(255, 236, 250, 244), Color(255, 150, 186, 174), Color(132, 8, 26, 36), Color(44, 124, 232, 168), Color(30, 180, 240, 210),
    Color(14, 255, 255, 255), Color(36, 124, 232, 168), Color(36, 124, 232, 168), Color(52, 255, 255, 255),
    Color(18, 255, 255, 255), Color(52, 124, 232, 168), Color(255, 118, 156, 144),
    Color(20, 255, 255, 255), Color(38, 124, 232, 168), Color(200, 16, 48, 54), Color(16, 255, 255, 255), Color(34, 124, 232, 168),
    Color(22, 255, 255, 255), Color(42, 124, 232, 168), Color(255, 6, 16, 24), Color(255, 12, 34, 50),
    Color(255, 124, 232, 198), Color(255, 170, 246, 220), Color(255, 4, 24, 26), Color(230, 30, 80, 76),
    Color(255, 124, 242, 196), Color(255, 255, 206, 120), Color(255, 255, 120, 120), Color(255, 12, 38, 50) };
// Garage : nuit brune et chaude (bois, lampe a huile), accent ambre ; voiles ambre et orange a la place de l'aurore.
static Theme kGarage = {
    Color(255, 252, 240, 224), Color(255, 190, 170, 146), Color(136, 32, 22, 14), Color(44, 255, 200, 120), Color(30, 255, 220, 160),
    Color(16, 255, 230, 190), Color(40, 255, 200, 120), Color(38, 255, 200, 120), Color(56, 255, 240, 220),
    Color(18, 255, 230, 190), Color(54, 255, 200, 120), Color(255, 160, 136, 110),
    Color(22, 255, 230, 190), Color(40, 255, 200, 120), Color(200, 60, 40, 24), Color(18, 255, 230, 190), Color(36, 255, 200, 120),
    Color(24, 255, 230, 190), Color(44, 255, 200, 120), Color(255, 20, 14, 10), Color(255, 42, 30, 20),
    Color(255, 255, 180, 84), Color(255, 255, 210, 140), Color(255, 36, 20, 6), Color(230, 90, 60, 30),
    Color(255, 150, 230, 160), Color(255, 255, 196, 107), Color(255, 255, 120, 110), Color(255, 46, 32, 22) };
// Ete (My Summer Car, demande de JD du 19/10 : « un nouveau theme pour la version MSC ») : noir chaud, accent jaune comme
// le logo de MSC ; theme d'office sur MSC ([Lanceur] ThemeMSC). La scene de fond suit le jeu (ete sur MSC : ScenePalette).
static Theme kEte = {
    Color(255, 246, 242, 232), Color(255, 184, 176, 160), Color(128, 18, 16, 14), Color(40, 255, 240, 210), Color(28, 255, 240, 210),
    Color(14, 255, 255, 255), Color(34, 255, 230, 170), Color(30, 255, 240, 210), Color(48, 255, 255, 255),
    Color(16, 255, 255, 255), Color(46, 255, 236, 200), Color(255, 150, 140, 124),
    Color(20, 255, 255, 255), Color(36, 255, 230, 170), Color(200, 40, 34, 26), Color(16, 255, 255, 255), Color(34, 255, 230, 170),
    Color(22, 255, 255, 255), Color(42, 255, 230, 170), Color(255, 14, 12, 10), Color(255, 34, 28, 22),
    Color(255, 255, 205, 70), Color(255, 255, 226, 140), Color(255, 30, 22, 6), Color(230, 80, 64, 30),
    Color(255, 140, 230, 150), Color(255, 255, 196, 107), Color(255, 255, 120, 110), Color(255, 30, 26, 22) };
static bool g_dark;
enum { THEME_SOMBRE, THEME_CLAIR, THEME_AURORE, THEME_GARAGE, THEME_ETE, THEME_N };
static int g_theme = THEME_SOMBRE;   // [Lanceur] Theme=sombre|clair|aurore|garage (absent : celui de Windows) ; g_dark suit (tout sauf clair)
static int g_aurore = 2;   // aurore et neige du fond : 0 off, 1 calme, 2 vivant ([Lanceur] Aurore, Reglages > Theme)
static bool g_glass = true;   // [Lanceur] Verre : cartes en verre depoli (scene floutee dessous) ou opaques
static int g_densite = 1;     // [Lanceur] Densite : 0 aeree, 1 normale, 2 compacte (pas des lignes des pages)
static int g_texte = 100;     // [Lanceur] Texte : taille du texte en % (80..110, toutes les polices)
static Theme &ThemeTab() { return g_theme == THEME_CLAIR ? kLight : g_theme == THEME_AURORE ? kAurore : g_theme == THEME_GARAGE ? kGarage : g_theme == THEME_ETE ? kEte : kDark; }
static float UiTextScale() { return g_texte / 100.0f; }
// Cle de la scene de fond (ui.inc) : refaite quand le theme, l'aurore ou le verre changent.
static int SceneKey() { return g_theme + g_aurore * 8 + (g_glass ? 32 : 0) + (g_game == GAME_MSC ? 64 : 0); }
// Couleurs de la scene de fond par theme (ui.inc SceneDraw) : ciel (4 arrets), deux jeux de voiles d'aurore (4 arrets chacun),
// collines, sapins, neige au sol.
struct ScenePal { Color sky[4], aurA[4], aurB[4], farHill, nearHill, treeInk, treeSnow, snow0, snow1, drift; };
static const ScenePal &ScenePalette()
{
    static const ScenePal pal[THEME_N] = {
        { { Color(255, 4, 8, 22), Color(255, 10, 21, 52), Color(255, 22, 40, 90), Color(255, 34, 56, 110) },
          { Color(0, 47, 255, 176), Color(190, 47, 255, 176), Color(140, 47, 214, 255), Color(0, 138, 107, 255) },
          { Color(0, 138, 107, 255), Color(128, 176, 107, 255), Color(115, 47, 255, 176), Color(0, 47, 255, 176) },
          Color(235, 19, 37, 82), Color(255, 14, 29, 68), Color(255, 10, 22, 54), Color(150, 198, 212, 234), Color(255, 159, 182, 220), Color(255, 93, 114, 153), Color(140, 125, 147, 189) },
        { { Color(255, 132, 176, 226), Color(255, 190, 216, 242), Color(255, 238, 220, 214), Color(255, 248, 233, 223) },
          { Color(0, 255, 190, 200), Color(120, 255, 200, 214), Color(100, 190, 210, 255), Color(0, 255, 230, 210) },
          { Color(0, 255, 190, 200), Color(0, 255, 200, 214), Color(0, 190, 210, 255), Color(0, 255, 230, 210) },
          Color(255, 178, 198, 228), Color(255, 150, 174, 210), Color(255, 44, 68, 94), Color(230, 250, 252, 255), Color(255, 252, 253, 255), Color(255, 214, 226, 242), Color(200, 236, 242, 250) },
        { { Color(255, 3, 10, 16), Color(255, 7, 18, 28), Color(255, 11, 32, 46), Color(255, 14, 44, 60) },   // aurore verte : verts seulement
          { Color(0, 72, 240, 176), Color(210, 72, 240, 176), Color(150, 124, 232, 168), Color(0, 60, 200, 150) },
          { Color(0, 60, 200, 150), Color(130, 120, 255, 190), Color(110, 72, 240, 176), Color(0, 72, 240, 176) },
          Color(235, 12, 42, 52), Color(255, 9, 32, 42), Color(255, 6, 24, 30), Color(150, 190, 222, 212), Color(255, 150, 190, 196), Color(255, 84, 120, 128), Color(140, 118, 158, 160) },
        { { Color(255, 12, 8, 6), Color(255, 26, 20, 16), Color(255, 42, 30, 20), Color(255, 58, 42, 26) },   // garage : ambre et orange
          { Color(0, 255, 180, 84), Color(170, 255, 180, 84), Color(120, 255, 140, 60), Color(0, 255, 120, 50) },
          { Color(0, 255, 120, 50), Color(110, 255, 200, 120), Color(100, 255, 160, 70), Color(0, 255, 180, 84) },
          Color(235, 50, 36, 26), Color(255, 38, 27, 19), Color(255, 28, 19, 13), Color(150, 230, 206, 170), Color(255, 196, 168, 132), Color(255, 120, 96, 70), Color(140, 160, 128, 96) },
        { { Color(255, 4, 8, 22), Color(255, 10, 21, 52), Color(255, 22, 40, 90), Color(255, 34, 56, 110) },   // ete sur My Winter Car : la nuit boreale
          { Color(0, 47, 255, 176), Color(190, 47, 255, 176), Color(140, 47, 214, 255), Color(0, 138, 107, 255) },
          { Color(0, 138, 107, 255), Color(128, 176, 107, 255), Color(115, 47, 255, 176), Color(0, 47, 255, 176) },
          Color(235, 19, 37, 82), Color(255, 14, 29, 68), Color(255, 10, 22, 54), Color(150, 198, 212, 234), Color(255, 159, 182, 220), Color(255, 93, 114, 153), Color(140, 125, 147, 189) } };
    // My Summer Car (19/10) : l'ete -- soir finlandais (themes sombres : ciel violet, horizon orange, nuages roses) ou plein
    // jour (clair) ; collines et sapins verts, herbe au lieu de la neige.
    static const ScenePal summer[2] = {
        { { Color(255, 14, 16, 40), Color(255, 44, 38, 84), Color(255, 132, 74, 96), Color(255, 238, 146, 86) },
          { Color(0, 255, 150, 110), Color(120, 255, 130, 120), Color(90, 255, 190, 120), Color(0, 255, 150, 110) },
          { Color(0, 200, 120, 200), Color(90, 220, 140, 200), Color(70, 255, 170, 140), Color(0, 200, 120, 200) },
          Color(235, 34, 52, 58), Color(255, 24, 40, 38), Color(255, 14, 28, 22), Color(255, 28, 50, 36), Color(255, 44, 74, 40), Color(255, 26, 48, 28), Color(140, 60, 96, 50) },
        { { Color(255, 96, 160, 230), Color(255, 150, 200, 245), Color(255, 215, 232, 250), Color(255, 250, 240, 220) },
          { Color(0, 255, 255, 255), Color(150, 255, 255, 255), Color(110, 255, 255, 255), Color(0, 255, 255, 255) },
          { Color(0, 255, 255, 255), Color(0, 255, 255, 255), Color(0, 255, 255, 255), Color(0, 255, 255, 255) },
          Color(255, 120, 160, 140), Color(255, 90, 140, 100), Color(255, 40, 80, 52), Color(255, 70, 112, 70), Color(255, 120, 176, 90), Color(255, 86, 140, 64), Color(160, 150, 196, 110) } };
    if (g_game == GAME_MSC) return summer[g_dark ? 0 : 1];
    return pal[g_theme >= 0 && g_theme < THEME_N ? g_theme : 0];
}
#define TH(x) (ThemeTab().x)
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

// Famille de police utilisable, gardee : celle demandee si elle existe, sinon la premiere presente d'une liste de
// remplacement. Sans Segoe UI (Linux / Steam Deck par Proton ou Wine, polices de Windows abimees), GDI+ ne dessinait
// AUCUN texte, sans erreur visible (retour de Donskoy, 11/10 : lanceur sans une lettre). Essai : variable
// MWCOOP_SANS_POLICES=1 (Segoe UI, Bahnschrift et Consolas comptees absentes).
// Windows 7 SP1 du joueur : Segoe UI y marche dans Paint (GDI), donc la police va bien -- c'est l'ecriture par GDI+ qui
// ne laisse rien. Au demarrage (TextProbe), le lanceur ecrit "Ag" dans une petite image reglee comme la fenetre, avec
// chaque lissage du texte ; il garde le premier qui laisse des pixels (g_textHint). Aucun : les lettres sont dessinees en
// formes vectorielles (GraphicsPath::AddString + FillPath, le moteur des icones, g_textPath). Tout passe par DrawStr.
// Journal : lanceur.log (« texte : ... »). Essai : MWCOOP_TEXTE=chemin force les formes vectorielles.
static TextRenderingHint g_textHint = TextRenderingHintAntiAliasGridFit;
static bool g_textPath;
// Windows 7 SP1 de Donskoy (11/10, 0.75.34) : GDI+ n'y trouve AUCUNE famille de police (« Segoe UI absente, remplacee
// par ? », DrawString -> 2 InvalidParameter, formes vectorielles vides : elles aussi passent par une famille GDI+). Paint
// ecrit, lui, par GDI : dernier recours, chaque texte est ecrit par GDI (DrawTextW, police par son nom) dans une petite
// image, collee ensuite dans la fenetre a la couleur voulue (g_textGdi). Essai : MWCOOP_TEXTE=gdi le force.
static bool g_textGdi;
static int GdiProbe()
{
    HDC dc = CreateCompatibleDC(NULL);
    if (!dc) return 0;
    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(bi.bmiHeader); bi.bmiHeader.biWidth = 48; bi.bmiHeader.biHeight = -24;
    bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32; bi.bmiHeader.biCompression = BI_RGB;
    void *bits = NULL;
    HBITMAP bmp = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, &bits, NULL, 0);
    int ink = 0;
    if (bmp && bits) {
        memset(bits, 0, 48 * 24 * 4);
        HGDIOBJ ob = SelectObject(dc, bmp);
        HFONT f = CreateFontW(-16, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_TT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
        HGDIOBJ of = SelectObject(dc, f);
        SetBkMode(dc, TRANSPARENT); SetTextColor(dc, RGB(255, 255, 255));
        RECT rr = { 2, 2, 46, 22 };
        DrawTextW(dc, L"Ag", 2, &rr, DT_LEFT | DT_TOP | DT_SINGLELINE | DT_NOPREFIX);
        GdiFlush();
        for (int i = 0; i < 48 * 24; i++) { BYTE *p = (BYTE *)bits + i * 4; if (max(p[0], max(p[1], p[2])) > 40) ink++; }
        SelectObject(dc, of); DeleteObject(f);
        SelectObject(dc, ob);
    }
    if (bmp) DeleteObject(bmp);
    DeleteDC(dc);
    return ink;
}
static bool g_textProbed;
static int ProbeDraw(const FontFamily *f, TextRenderingHint hint, bool path, Status *st)
{
    Bitmap b(48, 24, PixelFormat32bppPARGB);
    int ink = 0;
    Status s = GenericError;
    {
        Graphics g(&b);
        g.Clear(Color(0, 0, 0, 0));
        g.SetSmoothingMode(SmoothingModeAntiAlias);
        g.SetPixelOffsetMode(PixelOffsetModeHalf);
        g.SetTextRenderingHint(hint);
        SolidBrush w(Color(255, 255, 255, 255));
        if (path) {
            GraphicsPath p;
            p.AddString(L"Ag", -1, f, FontStyleRegular, 16, PointF(2, 2), NULL);
            s = g.FillPath(&w, &p);
        } else {
            Font font(f, 16, FontStyleRegular, UnitPixel);
            s = font.GetLastStatus() == Ok ? g.DrawString(L"Ag", -1, &font, PointF(2, 2), &w) : font.GetLastStatus();
        }
    }
    BitmapData bd;
    Rect r(0, 0, 48, 24);
    if (b.LockBits(&r, ImageLockModeRead, PixelFormat32bppPARGB, &bd) == Ok) {
        for (int y = 0; y < 24; y++) for (int x = 0; x < 48; x++) if (((BYTE *)bd.Scan0)[y * bd.Stride + x * 4 + 3] > 40) ink++;
        b.UnlockBits(&bd);
    }
    if (st) *st = s;
    return ink;
}
static void TextProbe()
{
    if (g_textProbed) return;
    g_textProbed = true;
    // Windows et GDI+ charges (le chemin de gdiplus.dll dit sa version : WinSxS 1.0 / 1.1)
    typedef LONG(WINAPI * RtlGetVersionF)(OSVERSIONINFOW *);
    OSVERSIONINFOW ov = { sizeof(ov) };
    if (RtlGetVersionF rgv = (RtlGetVersionF)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion")) rgv(&ov);
    wchar_t gp[MAX_PATH] = L"?";
    if (HMODULE m = GetModuleHandleW(L"gdiplus.dll")) GetModuleFileNameW(m, gp, MAX_PATH);
    LaunchLog("texte : Windows %lu.%lu build %lu, %ls", ov.dwMajorVersion, ov.dwMinorVersion, ov.dwBuildNumber, gp);
    wchar_t force[16] = L"";
    GetEnvironmentVariableW(L"MWCOOP_TEXTE", force, 16);
    FontFamily seg(L"Segoe UI");
    const FontFamily *f = seg.GetLastStatus() == Ok && seg.IsAvailable() ? &seg : FontFamily::GenericSansSerif();
    if (!wcscmp(force, L"chemin")) { g_textPath = true; LaunchLog("texte : formes vectorielles forcees (MWCOOP_TEXTE)"); return; }
    if (!wcscmp(force, L"gdi")) { g_textGdi = true; LaunchLog("texte : ecrit par GDI, force (MWCOOP_TEXTE, %d pixels)", GdiProbe()); return; }
    const TextRenderingHint hints[] = { TextRenderingHintAntiAliasGridFit, TextRenderingHintAntiAlias, TextRenderingHintClearTypeGridFit,
                                        TextRenderingHintSingleBitPerPixelGridFit, TextRenderingHintSystemDefault };
    for (TextRenderingHint h : hints) {
        Status st;
        int ink = ProbeDraw(f, h, false, &st);
        if (ink >= 8) {
            if (h != TextRenderingHintAntiAliasGridFit) LaunchLog("texte : lissage %d retenu (%d pixels)", (int)h, ink);
            g_textHint = h;
            return;
        }
        LaunchLog("texte : lissage %d n'ecrit rien (DrawString %d, %d pixels)", (int)h, (int)st, ink);
    }
    Status st;
    int ink = ProbeDraw(f, TextRenderingHintAntiAliasGridFit, true, &st);
    g_textPath = ink >= 8;
    LaunchLog(g_textPath ? "texte : DrawString muet, lettres en formes vectorielles (%d pixels)" : "texte : aucun rendu n'ecrit (formes : statut %d, %d pixels)",
              g_textPath ? ink : (int)st, ink);
    if (g_textPath) return;
    int gi = GdiProbe();
    g_textGdi = gi >= 8;
    LaunchLog(g_textGdi ? "texte : GDI+ muet, texte ecrit par GDI comme Paint (%d pixels)" : "texte : GDI n'ecrit rien non plus (%d pixels)", gi);
}

// Police demandee (nom, taille en pixels, style), gardee pour chaque Font du lanceur : en mode GDI, l'objet Font de GDI+
// peut etre vide (aucune famille), on ecrit avec ces valeurs.
struct FontSpec { std::wstring face; float px; int style; };
static std::map<const Font *, FontSpec> g_fontSpecs;
static FontSpec SpecOf(const Font *f)
{
    auto it = g_fontSpecs.find(f);
    if (it != g_fontSpecs.end()) return it->second;
    FontSpec s = { L"Segoe UI", 12, FontStyleRegular };
    if (f && f->GetLastStatus() == Ok) { s.px = f->GetSize(); s.style = f->GetStyle(); }
    return s;
}
static HFONT GdiFontFor(const FontSpec &sp, float scale)
{
    static std::map<std::wstring, HFONT> cache;
    int h = -max(1, (int)(sp.px * scale + 0.5f));
    wchar_t k[96];
    swprintf_s(k, L"%s|%d|%d", sp.face.c_str(), h, sp.style);
    auto it = cache.find(k);
    if (it != cache.end()) return it->second;
    HFONT f = CreateFontW(h, 0, 0, 0, (sp.style & FontStyleBold) ? FW_BOLD : FW_NORMAL, (sp.style & FontStyleItalic) != 0, (sp.style & FontStyleUnderline) != 0,
                          (sp.style & FontStyleStrikeout) != 0, DEFAULT_CHARSET, OUT_TT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH | FF_DONTCARE, sp.face.c_str());
    cache[k] = f;
    return f;
}
// Drapeaux DrawTextW d'apres le StringFormat (alignement, une ligne, points de suspension) ; le vertical est fait a part.
static UINT GdiFlags(const StringFormat *sf, bool layout)
{
    UINT f = DT_NOPREFIX;
    StringAlignment h = sf ? sf->GetAlignment() : StringAlignmentNear;
    f |= h == StringAlignmentCenter ? DT_CENTER : h == StringAlignmentFar ? DT_RIGHT : DT_LEFT;
    bool noWrap = !layout || (sf && (sf->GetFormatFlags() & StringFormatFlagsNoWrap));
    if (noWrap) f |= DT_SINGLELINE; else f |= DT_WORDBREAK;
    StringTrimming t = sf ? sf->GetTrimming() : StringTrimmingNone;
    if (layout && noWrap && (t == StringTrimmingEllipsisCharacter || t == StringTrimmingEllipsisWord || t == StringTrimmingEllipsisPath)) f |= DT_END_ELLIPSIS;
    return f;
}
static HDC GdiDC() { static HDC dc = CreateCompatibleDC(NULL); return dc; }
// Taille du texte en pixels de l'ecran (largeur maxi w ; 0 : sans limite, une ligne par saut de ligne du texte).
static SIZE GdiExtent(const std::wstring &s, HFONT f, UINT flags, int w)
{
    HDC dc = GdiDC();
    HGDIOBJ of = SelectObject(dc, f);
    RECT r = { 0, 0, w > 0 ? w : 100000, 0 };
    if (w <= 0 && s.find(L'\n') != std::wstring::npos) flags &= ~DT_SINGLELINE;
    DrawTextW(dc, s.c_str(), (int)s.size(), &r, (flags & ~(DT_CENTER | DT_RIGHT | DT_END_ELLIPSIS)) | DT_CALCRECT);
    SelectObject(dc, of);
    SIZE z = { r.right - r.left, r.bottom - r.top };
    return z;
}
// Texte ecrit par GDI dans une image (blanc sur noir = couverture), teinte a la couleur du pinceau ; images gardees
// (le lanceur redessine tout a chaque image), videes au-dela de 400.
struct GdiText { std::vector<BYTE> px; Bitmap *bmp; int w, h; };
static Status GdiDraw(Graphics &g, const std::wstring &s, const Font *font, RectF r, const StringFormat *sf, const Brush *b, bool layout)
{
    static std::map<std::wstring, GdiText *> cache;
    FontSpec sp = SpecOf(font);
    Matrix m; g.GetTransform(&m);
    REAL el[6]; m.GetElements(el);
    float sc = fabsf(el[0]) > 0.05f ? min(fabsf(el[0]), 4.0f) : 1.0f;
    HFONT hf = GdiFontFor(sp, sc);
    UINT flags = GdiFlags(sf, layout);
    Color c(255, 255, 255, 255);
    if (b && b->GetType() == BrushTypeSolidColor) ((const SolidBrush *)b)->GetColor(&c);
    int W, H, top = 0;
    SIZE ext = GdiExtent(s, hf, flags, layout ? (int)(r.Width * sc + 0.5f) : 0);
    if (!layout) {   // point d'origine : cadre a la taille du texte, place selon l'alignement
        float w = ext.cx / sc, hh = ext.cy / sc;
        StringAlignment ha = sf ? sf->GetAlignment() : StringAlignmentNear, va = sf ? sf->GetLineAlignment() : StringAlignmentNear;
        r = RectF(r.X - (ha == StringAlignmentCenter ? w / 2 : ha == StringAlignmentFar ? w : 0), r.Y - (va == StringAlignmentCenter ? hh / 2 : va == StringAlignmentFar ? hh : 0), w, hh);
        flags &= ~(DT_CENTER | DT_RIGHT);
        if (s.find(L'\n') != std::wstring::npos) flags &= ~DT_SINGLELINE;
    }
    W = (int)ceilf(r.Width * sc); H = (int)ceilf(r.Height * sc);
    if (W <= 0 || H <= 0 || W > 4096 || H > 4096 || s.empty()) return Ok;
    StringAlignment va = sf ? sf->GetLineAlignment() : StringAlignmentNear;
    if (layout && va != StringAlignmentNear) top = va == StringAlignmentCenter ? (H - min((int)ext.cy, H)) / 2 : H - min((int)ext.cy, H);
    wchar_t k[200];
    swprintf_s(k, L"|%s|%d|%d|%d|%d|%u|%d|%08x|", sp.face.c_str(), (int)(sp.px * sc * 10), sp.style, W, H, flags, top, (unsigned)c.GetValue());
    std::wstring key = s + k;
    GdiText *t;
    auto it = cache.find(key);
    if (it != cache.end()) t = it->second;
    else {
        if (cache.size() > 400) { for (auto &e : cache) { delete e.second->bmp; delete e.second; } cache.clear(); }
        t = new GdiText(); t->w = W; t->h = H; t->bmp = NULL;
        HDC dc = GdiDC();
        BITMAPINFO bi = {};
        bi.bmiHeader.biSize = sizeof(bi.bmiHeader); bi.bmiHeader.biWidth = W; bi.bmiHeader.biHeight = -H;
        bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32; bi.bmiHeader.biCompression = BI_RGB;
        void *bits = NULL;
        HBITMAP dib = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, &bits, NULL, 0);
        if (dib && bits) {
            memset(bits, 0, (size_t)W * H * 4);
            HGDIOBJ ob = SelectObject(dc, dib), of = SelectObject(dc, hf);
            SetBkMode(dc, TRANSPARENT); SetTextColor(dc, RGB(255, 255, 255));
            RECT rr = { 0, top, W, H };
            DrawTextW(dc, s.c_str(), (int)s.size(), &rr, flags);
            GdiFlush();
            t->px.resize((size_t)W * H * 4);
            const BYTE *src = (const BYTE *)bits;
            BYTE *dst = &t->px[0];
            for (size_t i = 0; i < (size_t)W * H; i++) {
                int cov = max(src[i * 4], max(src[i * 4 + 1], src[i * 4 + 2]));
                int a = cov * c.GetA() / 255;
                dst[i * 4] = (BYTE)(c.GetB() * a / 255); dst[i * 4 + 1] = (BYTE)(c.GetG() * a / 255); dst[i * 4 + 2] = (BYTE)(c.GetR() * a / 255); dst[i * 4 + 3] = (BYTE)a;
            }
            SelectObject(dc, of); SelectObject(dc, ob);
            t->bmp = new Bitmap(W, H, W * 4, PixelFormat32bppPARGB, &t->px[0]);
        }
        if (dib) DeleteObject(dib);
        cache[key] = t;
    }
    if (!t->bmp) return GenericError;
    InterpolationMode im = g.GetInterpolationMode();
    g.SetInterpolationMode(InterpolationModeNearestNeighbor);
    Status st = g.DrawImage(t->bmp, RectF(r.X, r.Y, t->w / sc, t->h / sc));
    g.SetInterpolationMode(im);
    return st;
}
static RectF GdiMeasure(const WCHAR *s, INT n, const Font *font, RectF layout, const StringFormat *sf, bool hasLayout)
{
    std::wstring str(s, n < 0 ? wcslen(s) : (size_t)n);
    FontSpec sp = SpecOf(font);
    bool wrapBox = hasLayout && layout.Width > 0;
    UINT flags = GdiFlags(sf, wrapBox);
    SIZE z = GdiExtent(str, GdiFontFor(sp, 1.0f), flags, wrapBox ? (int)layout.Width : 0);
    float h = (float)z.cy;
    if (hasLayout && layout.Height > 0) h = min(h, layout.Height);
    return RectF(layout.X, layout.Y, (float)z.cx, h);
}
// Arabe : une ligne qui contient des lettres arabes est ecrite dans une incorporation droite-a-gauche (U+202B ... U+202C,
// par ligne) : lue de droite a gauche, morceaux assembles compris, sans changer la mise en page.
static bool RtlText(const WCHAR *s, INT n, std::wstring &out)
{
    if (!g_ar) return false;
    size_t len = n < 0 ? wcslen(s) : (size_t)n;
    bool arabic = false;
    for (size_t i = 0; i < len && !arabic; i++) arabic = s[i] >= 0x0600 && s[i] <= 0x06FF;
    if (!arabic) return false;
    out = L"\u202B";
    for (size_t i = 0; i < len; i++) { if (s[i] == L'\n') out += L"\u202C\n\u202B"; else out += s[i]; }
    out += L"\u202C";
    return true;
}
// Ecriture de tout le texte du lanceur (DrawString, ou ses lettres en formes si DrawString est muet ici).
static Status DrawStr(Graphics &g, const WCHAR *s, INT n, const Font *font, const RectF &r, const StringFormat *sf, const Brush *b)
{
    std::wstring rtl;
    if (RtlText(s, n, rtl)) { s = rtl.c_str(); n = (INT)rtl.size(); }
    if (g_textGdi) return GdiDraw(g, std::wstring(s, n < 0 ? wcslen(s) : (size_t)n), font, r, sf, b, true);
    if (!g_textPath) return g.DrawString(s, n, font, r, sf, b);
    FontFamily ff;
    font->GetFamily(&ff);
    GraphicsPath p;
    p.AddString(s, n, &ff, font->GetStyle(), font->GetSize(), r, sf);
    return g.FillPath(b, &p);
}
static Status DrawStr(Graphics &g, const WCHAR *s, INT n, const Font *font, const PointF &o, const StringFormat *sf, const Brush *b)
{
    std::wstring rtl;
    if (RtlText(s, n, rtl)) { s = rtl.c_str(); n = (INT)rtl.size(); }
    if (g_textGdi) return GdiDraw(g, std::wstring(s, n < 0 ? wcslen(s) : (size_t)n), font, RectF(o.X, o.Y, 0, 0), sf, b, false);
    if (!g_textPath) return g.DrawString(s, n, font, o, sf, b);
    FontFamily ff;
    font->GetFamily(&ff);
    GraphicsPath p;
    p.AddString(s, n, &ff, font->GetStyle(), font->GetSize(), o, sf);
    return g.FillPath(b, &p);
}
// Mesure du texte (GDI+, ou GDI en mode GDI : memes dimensions que ce qui sera ecrit).
static Status MeasureStr(Graphics &g, const WCHAR *s, INT n, const Font *f, const PointF &o, const StringFormat *sf, RectF *box)
{
    if (g_textGdi) { *box = GdiMeasure(s, n, f, RectF(o.X, o.Y, 0, 0), sf, false); return Ok; }
    return g.MeasureString(s, n, f, o, sf, box);
}
static Status MeasureStr(Graphics &g, const WCHAR *s, INT n, const Font *f, const PointF &o, RectF *box) { return MeasureStr(g, s, n, f, o, NULL, box); }
static Status MeasureStr(Graphics &g, const WCHAR *s, INT n, const Font *f, const RectF &l, const StringFormat *sf, RectF *box)
{
    if (g_textGdi) { *box = GdiMeasure(s, n, f, l, sf, true); return Ok; }
    return g.MeasureString(s, n, f, l, sf, box);
}
static Status MeasureStr(Graphics &g, const WCHAR *s, INT n, const Font *f, const RectF &l, RectF *box) { return MeasureStr(g, s, n, f, l, NULL, box); }
// Police de la maquette (Nunito, arrondie, licence SIL OFL 1.1) integree a l'exe (launcher.rc : 6 RCDATA) : chargee en
// memoire pour GDI+ (PrivateFontCollection) et pour GDI (AddFontMemResourceEx, mode de secours). Illisible : Segoe UI
// comme avant. Variable (axe wght) : GDI+ n'en lit que l'instance par defaut, le gras est synthetise.
static PrivateFontCollection *g_fontCol;
static FontFamily *g_nunito, *g_nunitoSemi, *g_nunitoBold;   // variable (secours), SemiBold (textes), ExtraBold (gras) : graisses de la maquette (600-800)
static bool FamilyOk(const FontFamily *f);
static void EmbedFontLoad()
{
    g_fontCol = new PrivateFontCollection();
    const void *data[3] = {}; DWORD size[3] = {};
    for (int i = 0; i < 3; i++) {   // 6 variable, 7 SemiBold, 8 ExtraBold (launcher.rc)
        HRSRC r = FindResourceW(NULL, MAKEINTRESOURCEW(6 + i), RT_RCDATA);
        HGLOBAL h = r ? LoadResource(NULL, r) : NULL;
        void *p = h ? LockResource(h) : NULL;
        if (!p) continue;
        DWORD n = SizeofResource(NULL, r);
        if (g_fontCol->AddMemoryFont(p, n) != Ok) { LaunchLog("police : Nunito integree %d illisible", 6 + i); continue; }
        data[i] = p; size[i] = n;
    }
    int cnt = g_fontCol->GetFamilyCount(), got = 0;
    if (cnt < 1) return;
    FontFamily fams[6];
    g_fontCol->GetFamilies(min(cnt, 6), fams, &got);
    for (int i = 0; i < got; i++) {   // (les familles sont nommees : la statique porte son nom complet, "Nunito SemiBold")
        WCHAR name[LF_FACESIZE] = L"";
        fams[i].GetFamilyName(name);
        FontFamily **slot = !wcscmp(name, L"Nunito SemiBold") ? &g_nunitoSemi : !wcscmp(name, L"Nunito ExtraBold") ? &g_nunitoBold : !wcscmp(name, L"Nunito") ? &g_nunito : NULL;
        if (!slot || *slot) continue;
        *slot = fams[i].Clone();
        if (!FamilyOk(*slot)) { LaunchLog("police : %ls integree n'ecrit rien", name); delete *slot; *slot = NULL; continue; }
        LaunchLog("police : %ls integree", name);
    }
    if (!g_nunito) { LaunchLog("police : Nunito integree absente, Segoe UI gardee"); return; }
    DWORD gdiN = 0;
    for (int i = 0; i < 3; i++) if (data[i]) AddFontMemResourceEx((void *)data[i], size[i], NULL, &gdiN);
}
// Une police peut aussi etre declaree presente et ne rien ecrire (fichier abime, Windows "allege") : chaque famille
// ecrit "Ag" avec le rendu retenu ; aucun pixel = police suivante.
static bool FamilyOk(const FontFamily *f)
{
    WCHAR name[LF_FACESIZE] = L"?";
    if (!f || f->GetLastStatus() != Ok || !f->IsAvailable()) return false;
    f->GetFamilyName(name);
    TextProbe();
    Status st;
    int ink = ProbeDraw(f, g_textHint, g_textPath, &st);
    if (ink < 8) LaunchLog("police : %ls presente mais n'ecrit rien (statut %d, %d pixels)", name, (int)st, ink);
    return ink >= 8;
}
static const FontFamily *UiFamily(const wchar_t *want)
{
    static std::map<std::wstring, FontFamily *> cache;
    auto it = cache.find(want);
    if (it != cache.end()) return it->second;
    if (g_nunito && wcscmp(want, L"Consolas")) return g_nunito;   // (police de la maquette, integree a l'exe : titres et textes)
    static int noWin = -1;
    if (noWin < 0) { wchar_t v[8] = L""; GetEnvironmentVariableW(L"MWCOOP_SANS_POLICES", v, 8); noWin = v[0] == L'1'; }
    bool mono = !wcscmp(want, L"Consolas");
    static const wchar_t *sans[] = { L"Segoe UI", L"Tahoma", L"Arial", L"Microsoft Sans Serif", L"Liberation Sans", L"DejaVu Sans", L"Noto Sans" };
    static const wchar_t *fixed[] = { L"Consolas", L"Courier New", L"Liberation Mono", L"DejaVu Sans Mono" };
    std::vector<const wchar_t *> tries(1, want);
    if (mono) tries.insert(tries.end(), fixed, fixed + _countof(fixed));
    else tries.insert(tries.end(), sans, sans + _countof(sans));
    FontFamily *got = NULL;
    for (const wchar_t *n : tries) {
        if (noWin && (!wcscmp(n, L"Segoe UI") || !wcscmp(n, L"Bahnschrift") || !wcscmp(n, L"Consolas"))) continue;
        FontFamily *f = new FontFamily(n);
        if (FamilyOk(f)) { got = f; break; }
        delete f;
    }
    if (!got) got = (mono ? FontFamily::GenericMonospace() : FontFamily::GenericSansSerif())->Clone();
    WCHAR name[LF_FACESIZE] = L"?";
    if (got) got->GetFamilyName(name);
    if (wcscmp(name, want)) LaunchLog("police : %ls absente, remplacee par %ls", want, name);
    cache[want] = got;
    return got;
}
// Famille selon la graisse demandee : gras = Nunito ExtraBold, normal = Nunito SemiBold (instances statiques, style
// "Regular" dans leur fichier : le bit gras est retire, sinon GDI+ epaissirait encore) ; sans elles, la variable (gras
// synthetise), puis UiFamily (Segoe UI...). Consolas reste a part.
static const FontFamily *UiFamilyStyled(const wchar_t *want, int style)
{
    if (!wcscmp(want, L"Consolas") || !g_nunito) return UiFamily(want);
    FontFamily *f = (style & FontStyleBold) ? g_nunitoBold : g_nunitoSemi;
    return f ? f : g_nunito;
}
static int UiStyleFor(const wchar_t *want, int style)
{
    const FontFamily *f = UiFamilyStyled(want, style);
    return (f == g_nunitoBold || f == g_nunitoSemi) && f ? (style & ~FontStyleBold) : style;
}
// Police du lanceur : celle de UiFamily, avec son nom, sa taille et son style gardes pour le mode GDI. La taille est
// multipliee par la taille du texte choisie (Reglages > Theme, [Lanceur] Texte).
struct UiFont : Font
{
    UiFont(const wchar_t *face, float px, int style) : Font(UiFamilyStyled(face, style), px * UiTextScale(), UiStyleFor(face, style), UnitPixel) { FontSpec s = { face, px * UiTextScale(), style }; g_fontSpecs[this] = s; }
    ~UiFont() { g_fontSpecs.erase(this); }
};
// Police des titres : Bahnschrift (Windows 10 et plus, geometrique) si elle est la, sinon Segoe UI.
static const wchar_t *TitleFont()
{
    static int ok = -1;
    if (ok < 0) { WCHAR n[LF_FACESIZE] = L""; UiFamily(L"Bahnschrift")->GetFamilyName(n); ok = !wcscmp(n, L"Bahnschrift"); }
    return ok ? L"Bahnschrift" : L"Segoe UI";
}
// Polices gardees (en creer une a chaque texte coutait l'essentiel du temps d'une image). Jamais liberees (fin du
// programme seulement).
static Font *FontOf(const wchar_t *family, float px, int style)
{
    static std::map<std::wstring, Font *> cache;
    wchar_t k[96];
    swprintf_s(k, L"%s|%d|%d|%d", family, (int)(px * 20 + 0.5f), style, g_texte);   // (taille du texte : une police par valeur)
    auto it = cache.find(k);
    if (it != cache.end()) return it->second;
    Font *f = new UiFont(family, px, style);
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
    DrawStr(g, s.c_str(), -1, &font, r, &sf, &b);
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
    DrawStr(g, s.c_str(), -1, &font, r, &sf, &b);
}

// Texte sur plusieurs lignes (coupe aux mots), en haut du cadre.
static void Para(Graphics &g, const std::wstring &s, RectF r, float px, Color c, StringAlignment v = StringAlignmentNear)
{
    Font &font = *FontOf(L"Segoe UI", px, FontStyleRegular);
    StringFormat sf;
    sf.SetLineAlignment(v);
    sf.SetTrimming(StringTrimmingEllipsisWord);
    SolidBrush b(c);
    DrawStr(g, s.c_str(), -1, &font, r, &sf, &b);
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
    MeasureStr(g, s.c_str(), -1, &font, PointF(0, 0), &box);
    return box.Width;
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
    if (placeholder) shown = f.address ? T(L"ex. 26.12.34.56 (adresse:port accept\u00E9)", L"e.g. 26.12.34.56 (address:port accepted)") : f.code ? T(L"si priv\u00E9", L"if private") : DefaultName();
    Text(g, shown, tr, placeholder && (f.address || f.code) ? 13.5f : 16.0f, placeholder ? FontStyleRegular : FontStyleBold, placeholder ? TH(placeholder) : kInk, StringAlignmentNear);
    if (g_focus == i && fmodf(g_time, 1.0f) < 0.55f) {
        UiFont font(L"Segoe UI", 16, FontStyleBold);
        StringFormat sf(StringFormat::GenericTypographic());
        sf.SetFormatFlags(StringFormatFlagsMeasureTrailingSpaces | StringFormatFlagsNoWrap);
        RectF box;
        MeasureStr(g, f.text.c_str(), -1, &font, PointF(0, 0), &sf, &box);
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
#include "dialog.inc"

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
        if (g_game == GAME_MSC) { o.dFr = L"Tes CD, ta radio, la peinture de ta SATSUMA, ton drapeau, tes posters… copiés chez les invités de ton salon pour ta partie.";
                                  o.dEn = L"Your CDs, radio, SATSUMA paint, flag, posters… copied to your lobby's guests for your game."; }
        else o.dEn = L"Your CDs, radio, CORRIS paint, flag, posters\u2026 copied to your lobby's guests for your game.";
        g_opts.push_back(o);
    }
    {   // Mods de l'hote (modsync.inc) : TOUT ACCEPTER une fois l'allume
        Opt o = {};
        o.tab = TAB_COOP; o.key = "ModsAuto"; o.def = 0; o.kind = O_TOGGLE;
        o.fr = L"Accepter l'envoi de l'h\u00F4te sans demander"; o.en = L"Accept the host's files without asking"; o.suffix = L"";
        o.dFr = L"Les mods MSCLoader de l'h\u00F4te sont toujours re\u00E7us (copi\u00E9s \u00E0 part, les tiens ne sont pas touch\u00E9s ; MSCLoader install\u00E9 s'il manque) et le jeu attend de les avoir. Cette case : ses CD et images aussi, sans demander. Coup\u00E9 : le salon demande d'abord (TOUT ACCEPTER).";
        o.dEn = L"The host's MSCLoader mods are always received (copied separately, yours are not touched; MSCLoader installed if missing) and the game waits for them. This box: their CDs and images too, without asking. Off: the lobby asks first (ACCEPT ALL).";
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
        o.svals.push_back("perso"); o.labFr.push_back(L"Haut perso"); o.labEn.push_back(L"Custom top");   // (image importee : perso.inc)
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
        if (g_game == GAME_MSC) { o.dFr = L"Couleur de la SATSUMA pour une nouvelle partie (l'hôte la donne aux invités). Clic : aperçu en 3D dans le volet.";
                                  o.dEn = L"SATSUMA color for a new game (the host gives it to the guests). Click: 3D preview in the side panel."; }
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

static float NotesMaxScroll();
static float LogsMaxScroll();
static float GfxMaxScroll();
static float LobbyMaxScroll();
static float MscMaxScroll();
static float WikiMaxScroll();   // (wiki.inc)
static float VhsMaxScroll();    // (media.inc)
static float AlbMaxScroll();
static float CreditsMaxScroll();   // (ui.inc)
static float SavesMaxScroll();     // (saves.inc)
static void DrawMods(Graphics &g);
static void DrawServer(Graphics &g);
static float MaxScroll(int t) { return t == TAB_SAVES ? SavesMaxScroll() : t == TAB_MODS ? MscMaxScroll() : t == TAB_LOBBY ? LobbyMaxScroll() : t == TAB_LOGS ? LogsMaxScroll() : t == TAB_GFX || t == TAB_COOP ? GfxMaxScroll() : t == TAB_NOTES ? NotesMaxScroll() : t == TAB_WIKI ? WikiMaxScroll() : t == TAB_VHS ? VhsMaxScroll() : t == TAB_ALBUM ? AlbMaxScroll() : t == TAB_CREDITS ? CreditsMaxScroll() : 0.0f; }

static int ValueIndex(const Opt &o, int v)
{
    for (int i = 0; i < (int)o.vals.size(); i++) if (o.vals[i] == v) return i;
    for (int i = 0; i < (int)o.vals.size(); i++) if (o.vals[i] > v) return i;   // valeur hors liste : la suivante
    return (int)o.vals.size() - 1;
}
// Libelle i d'une option a valeurs nommees (« Tenue 21 » : en russe, « Outfit » traduit et le numero).
static std::wstring TLab(const std::vector<std::wstring> &fr, const std::vector<std::wstring> &en, size_t i)
{
    if (g_fr || (!g_ru && !g_ar)) return g_fr ? fr[i] : en[i];
    const wchar_t *r = g_ar ? Ar(en[i].c_str()) : Ru(en[i].c_str());
    if (r != en[i].c_str()) return r;
    if (en[i].compare(0, 7, L"Outfit ") == 0) return std::wstring(g_ar ? Ar(L"Outfit ") : Ru(L"Outfit ")) + en[i].substr(7);
    return en[i];
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
// (une cle par jeu : sur My Summer Car, les notes ne sont pas les memes -- ChannelNotes)
static const wchar_t *NotesSeenKey() { return g_game == GAME_MSC ? L"NotesVuesMSC" : L"NotesVues"; }
static bool NotesUnseen()
{
    std::wstring v = NewestNote();
    if (v.empty()) return false;
    wchar_t seen[64] = {};
    GetPrivateProfileStringW(L"Lanceur", NotesSeenKey(), L"", seen, 64, g_iniLauncher.c_str());
    return !seen[0] || CmpVer(v, seen) > 0;
}
static void NotesMarkSeen()
{
    std::wstring v = NewestNote();
    if (!v.empty()) WritePrivateProfileStringW(L"Lanceur", NotesSeenKey(), v.c_str(), g_iniLauncher.c_str());
}

// ---------------------------------------------------------------- images des notes
// Telechargees a la premiere ouverture de l'onglet, gardees dans %LOCALAPPDATA%\MWCoop\cache\notes\ (hors ligne ensuite).
enum { NI_WAIT, NI_LOAD, NI_OK, NI_FAIL };
struct NoteImg { int state; Bitmap *bmp; };
static std::map<std::wstring, NoteImg> g_noteImgs;   // (sous g_cs)
static std::wstring LocalDir();
static std::wstring SaveCurrentProf();                  // (saves.inc : sauvegarde utilisee, profil du chargeur ; vide : la principale)
static std::wstring SaveCurrentName();
static std::wstring SaveDirOf(const std::wstring &prof); // (dossier de la sauvegarde du jeu de ce profil)
static std::wstring SaveProfRoot();
static void SaveOpenNewGame();
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

#include "journaux.inc"

#include "voiture.inc"

#include "tenue.inc"

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
// Wine : la ligne de Linux -- Steam y lance Proton ; WINEDLLOVERRIDES pour notre version.dll, et le nom de l'exe du jeu
// remplace par MWCoop.exe dans %command% (le lanceur tourne alors dans le prefixe du jeu, avec Steam).
static std::wstring WineExeName()
{
    WIN32_FIND_DATAW fd;   // (casse reelle du fichier : Linux la respecte)
    HANDLE h = g_gameDir.empty() ? INVALID_HANDLE_VALUE : FindFirstFileW((g_gameDir + GM().exe).c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return GM().exe;
    FindClose(h);
    return fd.cFileName;
}
static std::wstring SteamLaunchLine()
{
    if (IsWine()) {
        std::wstring self = g_self.substr(g_self.find_last_of(L'\\') + 1);
        return L"WINEDLLOVERRIDES=\"version,winhttp=n,b\" bash -c 'exec \"${@/" + WineExeName() + L"/" + self + L"}\"' -- %command%";
    }
    return L"\"" + g_self + L"\" %command%";
}
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
// Lien d'invitation : jamais dans le code public (robots qui fouillent GitHub, demande de JD, 11/10) -- son code est dans
// launcher/discord.local.h (ignore par git, sur le PC qui compile), brouille. Sans ce fichier : la page GitHub du projet.
#if __has_include("discord.local.h")
#include "discord.local.h"
#define MWC_HAS_DISCORD 1
#endif
static std::wstring DiscordUrlBuild()
{
#ifdef MWC_HAS_DISCORD
    std::wstring code;
    for (unsigned char c : kDiscordCodeX) code += (wchar_t)(c ^ 0x5A);
    return L"https://discord" L".gg/" + code;
#else
    return L"https://github.com/Parricidium/MWCoop";
#endif
}
static const std::wstring g_discordUrl = DiscordUrlBuild();
static const wchar_t *kDiscordUrl = g_discordUrl.c_str();
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
        T(L"Pour que le bouton Jouer de Steam et une invitation accept\u00E9e MWCoop ferm\u00E9 ouvrent MWCoop (et pas le jeu sans le mod), chacun ajoute cette option de lancement :",
          L"So that Steam's Play button and an invite accepted with MWCoop closed open MWCoop (not the game without the mod), everyone adds this launch option:") };
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
        UiFont mf(L"Consolas", 14, FontStyleRegular);
        StringFormat sf;
        sf.SetLineAlignment(StringAlignmentCenter);
        sf.SetTrimming(StringTrimmingEllipsisPath);
        SolidBrush tb(kInk);
        std::wstring line = SteamLaunchLine();
        DrawStr(g, line.c_str(), -1, &mf, RectF(code.X + 16, code.Y + 4, code.Width - 32, code.Height - 8), &sf, &tb);
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
    wchar_t file[MAX_PATH];
    wcscpy_s(file, GM().exe);
    std::wstring filter = std::wstring(GM().exe) + L'\0' + GM().exe + L'\0' + L"*.exe" + L'\0' + L"*.exe" + L'\0';
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = filter.c_str();
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
    WritePrivateProfileStringW(L"Lanceur", GM().iniDir, g_gameDir.c_str(), g_iniLauncher.c_str());
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
    bool ok = QueryFullProcessImageNameW(p, 0, path, &n) && !_wcsicmp(path + std::wstring(path).find_last_of(L'\\') + 1, GM().exe);
    CloseHandle(p);
    return ok;
}
// Copie de lancement pour une installation Steam : un jeu lance depuis steamapps ne charge pas notre version.dll
// (Steam y injecte son overlay au demarrage, et la version.dll de Windows est deja en memoire quand le jeu cherche
// la sienne ; vu le 05/10 sur l'installation de JD, alors que les copies hors de steamapps marchent). On lance
// donc, comme les copies COOPTEST, une copie de l'exe dans %LOCALAPPDATA%\MWCoop\jeu, les gros dossiers et
// MWCoop relies par jonctions (memes donnees, reglages, journaux et sauvegardes). Vide : pas de copie.
// Depart du dossier du jeu plutot que de la copie ([Lanceur] SansCopie ; absent : oui sous Wine, ou les jonctions de la
// copie ne sont pas sures, non sous Windows).
static bool NoCopy() { return GetPrivateProfileIntW(L"Lanceur", L"SansCopie", IsWine() ? 1 : 0, g_iniLauncher.c_str()) != 0; }
static std::wstring MirrorDir()
{
    if (!ModInGame() && !g_gameDir.empty() && !LocalDir().empty()) return LocalDir() + GM().steamDir + L"\\";   // (a part : toujours la copie)
    std::wstring low = g_gameDir;
    for (auto &c : low) c = towlower(c);
    if (low.find(L"\\steamapps\\") == std::wstring::npos || LocalDir().empty()) return L"";
    // "My Winter Car" dans le chemin : le prechargeur de MSCLoader reconnait le jeu a son dossier (sinon il se croit
    // dans My Summer Car). Avant 0.31.3 : %LOCALAPPDATA%\MWCoop\jeu (laisse en place, plus utilise).
    return LocalDir() + GM().steamDir + L"\\";
}

// Avec le MSCLoader de MWCoop actif : sa copie de lancement (pare-feu, jeu deja lance).
static std::wstring GameExe()
{
    std::wstring m = MirrorDir();
    if (NoCopy()) m.clear();   // (depart du dossier du jeu)
    if (g_mscOn && MscOwn() && MscOwnInstalled()) m = MscCopyDir();
    return (m.empty() ? g_gameDir : m) + GM().exe;
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
    return ext == L".dll" || ext == L".ini" || !_wcsicmp(n.c_str(), GM().exe) || !_wcsicmp(n.c_str(), L"changelog.txt");
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

// Mod a part : le dossier MWCoop de la copie est une jonction vers <lanceur>\MWC\MWCoop (ou MSC) -- le chargeur et le mod y
// trouvent leurs fichiers comme dans le dossier du jeu. Une jonction qui vise ailleurs (l'ancien MWCoop du jeu) est refaite.
static bool ModLink(const std::wstring &m)
{
    if (ModInGame()) return true;
    std::wstring dst = m + L"MWCoop", target = g_modRoot + L"MWCoop";
    SHCreateDirectoryExW(NULL, target.c_str(), NULL);
    DWORD at = GetFileAttributesW(dst.c_str());
    if (at != INVALID_FILE_ATTRIBUTES) {
        if (SameDirTarget(dst, target)) return true;
        if (!(at & FILE_ATTRIBUTE_REPARSE_POINT)) { TestLog("copie de lancement : MWCoop est un vrai dossier, laisse (%ls)", dst.c_str()); return false; }
        RemoveDirectoryW(dst.c_str());   // (la jonction seule ; jamais ce qu'elle montre)
    }
    if (!MakeJunction(dst, target)) { TestLog("copie de lancement : jonction MWCoop impossible (erreur %lu)", GetLastError()); return false; }
    TestLog("copie de lancement : MWCoop -> %ls", target.c_str());
    return true;
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
        if (!ModInGame() && (!_wcsicmp(n.c_str(), L"MWCoop") || !_wcsicmp(n.c_str(), L"version.dll"))) continue;   // (reste d'avant 1.1 : pas le notre)
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
    if (a != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(a, Narrow(GM().appid).c_str(), (DWORD)wcslen(GM().appid), &n, NULL); CloseHandle(a); }
    if (!ModLink(m)) return false;
    TestLog("copie de lancement prete : %ls", m.c_str());
    return true;
}

// Mode debogage : Windows, Smart App Control (bloque les DLL non signees, comme notre version.dll), antivirus declares
// a Windows (Securite Windows : root/SecurityCenter2, par PowerShell, fil a part).
static DWORD WINAPI DebugSysThread(void *)
{
    if (IsWine()) {   // (ni Smart App Control, ni Securite Windows, ni PowerShell sous Wine)
        DebugLog("Wine %s (%s), lanceur %s, jeu %s", g_wine.c_str(), g_wineInfo.c_str(), Narrow(g_self, CP_UTF8).c_str(), Narrow(g_gameDir, CP_UTF8).c_str());
        return 0;
    }
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
        if (_wcsicmp(p.szExeFile, GM().exe)) continue;
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
static bool GameProcessRunning(DWORD *pidOut)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W pe = { sizeof(pe) };
    std::wstring mine = g_gameDir + GM().exe, mirror = GameExe(), guest = GuestCopyDir() + GM().exe, own = MscCopyDir() + GM().exe;
    bool found = false;
    for (BOOL ok = Process32FirstW(snap, &pe); ok && !found; ok = Process32NextW(snap, &pe)) {
        if (_wcsicmp(pe.szExeFile, GM().exe)) continue;
        HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pe.th32ProcessID);
        if (!h) { found = true; if (pidOut) *pidOut = pe.th32ProcessID; break; }   // inconnu : prudence
        wchar_t path[MAX_PATH];
        DWORD n = MAX_PATH;
        if (!QueryFullProcessImageNameW(h, 0, path, &n) || !_wcsicmp(path, mine.c_str()) || !_wcsicmp(path, mirror.c_str()) || !_wcsicmp(path, guest.c_str()) || !_wcsicmp(path, own.c_str())) { found = true; if (pidOut) *pidOut = pe.th32ProcessID; }
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
// Jeu reste en memoire sans fenetre (retour de Matilda, 0.79.3 : « My Winter Car est deja lance » alors qu'il etait
// ferme -- journal termine par « jeu ferme normalement », processus jamais sorti, Steam le montrait en cours) : le
// lanceur propose de le fermer de force. Seulement s'il tourne depuis plus d'une minute (un jeu qui demarre n'a pas
// encore de fenetre).
struct WndFind { DWORD pid; bool found; };
static BOOL CALLBACK FindPidWindow(HWND h, LPARAM lp)
{
    WndFind *f = (WndFind *)lp;
    DWORD pid = 0; GetWindowThreadProcessId(h, &pid);
    if (pid == f->pid && IsWindowVisible(h)) { f->found = true; return FALSE; }
    return TRUE;
}
// Niveau du jeu en cours d'apres serveur.txt (ecrit chaque seconde par le mod) : "MainMenu", "GAME"... ; vide si aucun etat frais.
// serveur.txt le plus recent (le mod l'ecrit chaque seconde, Net/Admin.cs) ; age en secondes (-1 : aucun).
static std::wstring NewestHeartbeat(long long *age)
{
    std::vector<std::wstring> dirs = { g_modRoot + L"MWCoop\\", g_modRoot + L"MWCoop\\profils\\invite\\" };
    if (!LocalDir().empty()) {
        dirs.push_back(LocalDir()); dirs.push_back(LocalDir() + L"profils\\invite\\");
        dirs.push_back(GuestCopyDir() + L"MWCoop\\"); dirs.push_back(GuestCopyDir() + L"MWCoop\\profils\\invite\\");
    }
    for (const std::wstring &pr : Profiles()) dirs.push_back(SaveProfRoot() + pr + L"\\");   // (sauvegardes a part : saves.inc)
    std::wstring best; long long bt = 0;
    for (auto &d : dirs) { long long t = FileUnixTime(d + L"serveur.txt"); if (t > bt) { bt = t; best = d + L"serveur.txt"; } }
    if (age) *age = best.empty() ? -1 : (long long)_time64(NULL) - bt;
    return best;
}
static std::wstring GameLevelNow()
{
    long long age = -1;
    std::wstring best = NewestHeartbeat(&age);
    if (best.empty() || age > 6) return L"";
    std::vector<unsigned char> raw;
    if (!ReadAll(best, raw)) return L"";
    std::wstring all = Widen(std::string(raw.begin(), raw.end()), CP_UTF8);
    if (all.compare(0, 5, L"etat\t")) return L"";
    std::vector<std::wstring> f;
    size_t a = 0, e = all.find(L'\n');
    std::wstring l = all.substr(0, e);
    while (a <= l.size()) { size_t b = l.find(L'\t', a); if (b == std::wstring::npos) b = l.size(); f.push_back(l.substr(a, b - a)); a = b + 1; }
    return f.size() > 4 ? f[4] : L"";
}

// Fermer le jeu : d'abord poliment (WM_CLOSE a ses fenetres), de force apres 5 s.
static BOOL CALLBACK ClosePidWindows(HWND h, LPARAM lp)
{
    DWORD pid = 0; GetWindowThreadProcessId(h, &pid);
    if (pid == (DWORD)lp) PostMessageW(h, WM_CLOSE, 0, 0);
    return TRUE;
}
static void CloseGameProcess(DWORD pid)
{
    EnumWindows(ClosePidWindows, (LPARAM)pid);
    HANDLE t = OpenProcess(PROCESS_TERMINATE | SYNCHRONIZE, FALSE, pid);
    if (!t) { LaunchLog("jeu %lu : ouverture impossible pour le fermer", pid); return; }
    bool gone = WaitForSingleObject(t, 5000) == WAIT_OBJECT_0;
    if (!gone) { TerminateProcess(t, 1); gone = WaitForSingleObject(t, 5000) == WAIT_OBJECT_0; }
    CloseHandle(t);
    LaunchLog("jeu %lu ferme depuis le lanceur : %s", pid, gone ? "fait" : "IMPOSSIBLE");
}

static bool GameRunningAsk()
{
    DWORD pid = 0;
    if (!GameProcessRunning(&pid)) return false;
    bool stuck = false, window = false, opened = false;
    ULONGLONG ageS = 0;
    long long beat = -1;
    NewestHeartbeat(&beat);
    std::string path = "(chemin inconnu)";
    if (pid) {
        WndFind f = { pid, false };
        EnumWindows(FindPidWindow, (LPARAM)&f);
        window = f.found;
        HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
        if (!h) LaunchLog("jeu %lu : ouverture impossible (erreur %lu : processus eleve, d'une autre session, ou en train de mourir)", pid, GetLastError());
        if (h) {
            opened = true;
            wchar_t p[MAX_PATH]; DWORD n = MAX_PATH;
            if (QueryFullProcessImageNameW(h, 0, p, &n)) path = Narrow(p, CP_UTF8);
            FILETIME c, e, k, u, now;
            if (GetProcessTimes(h, &c, &e, &k, &u)) {
                GetSystemTimeAsFileTime(&now);
                ULONGLONG age = (((ULONGLONG)now.dwHighDateTime << 32) | now.dwLowDateTime) - (((ULONGLONG)c.dwHighDateTime << 32) | c.dwLowDateTime);
                ageS = age / 10000000ull;
            }
            CloseHandle(h);
        }
        // Pendu : plus d'une minute, et sans fenetre, ou avec une fenetre mais le battement du mod arrete depuis plus de
        // 15 s (jeu quitte reste en memoire fenetre comprise, ou parti sans MWCoop) -- Matilda, 15-16/10 : refus « deja
        // lance » a chaque LANCER, Steam le montrait en cours, rien ne partait.
        bool silent = beat < 0 || beat > 15;
        stuck = ageS > 60 && (!window || silent);
        // Processus qu'on ne peut meme pas ouvrir (chemin et age inconnus), sans fenetre, mod muet : un reste du jeu precedent
        // (Matilda, 18/10 : « jeu ferme normalement » a 17:47:56, lanceur rouvert 30 s plus tard, pid sans fenetre, refus a
        // chaque LANCER). Traite comme bloque : fermeture proposee, et lancement quand meme si elle est impossible.
        if (!opened && !window && silent) stuck = true;
    }
    // Toujours au journal : ce refus n'y laissait aucune trace.
    std::string beatTxt = beat < 0 ? "jamais" : std::to_string(beat) + " s";
    LaunchLog("jeu %lu en cours : %s, fenetre %d, depuis %llu s, dernier battement du mod : %s", pid, path.c_str(), (int)window, ageS, beatTxt.c_str());
    // Jeu ouvert au menu principal (retour de Matilda, 14/10 : partie quittee a 14h50, jeu laisse au menu, le lanceur rouvert
    // a 16h13 refusait tout -- « deja lance », Steam le montrait en cours) : l'etat que le mod ecrit chaque seconde
    // (serveur.txt, Net/Admin.cs : niveau charge) dit s'il est au menu ; alors on propose de le fermer (rien a sauvegarder).
    std::wstring level = GameLevelNow();
    if (!stuck && level == L"MainMenu") {
        LaunchLog("jeu %lu ouvert au menu principal : fermeture proposee", pid);
        if (MessageBoxW(g_wnd, T(L"My Winter Car est encore ouvert, au menu principal (partie quitt\u00E9e).\n\nLe fermer pour relancer depuis le lanceur ?",
                                 L"My Winter Car is still open, at the main menu (game quit).\n\nClose it so you can launch again from the launcher?"),
                        L"MWCoop", MB_YESNO | MB_ICONQUESTION) == IDYES) {
            CloseGameProcess(pid);
            if (!GameProcessRunning()) return false;
        }
    }
    if (stuck && window) {
        LaunchLog("jeu %lu avec fenetre mais muet depuis plus d'une minute : fermeture proposee", pid);
        if (MessageBoxW(g_wnd, T(L"My Winter Car est encore ouvert mais ne r\u00E9pond plus \u00E0 MWCoop (rest\u00E9 bloqu\u00E9 apr\u00E8s sa fermeture, ou lanc\u00E9 sans MWCoop).\n\nLe fermer pour relancer depuis le lanceur ?",
                                 L"My Winter Car is still open but no longer answers MWCoop (stuck after closing, or started without MWCoop).\n\nClose it so you can launch again from the launcher?"),
                        L"MWCoop", MB_YESNO | MB_ICONQUESTION) == IDYES) {
            CloseGameProcess(pid);
            if (!GameProcessRunning()) return false;
        }
    } else if (stuck) {
        LaunchLog("jeu %lu en cours sans fenetre depuis plus d'une minute : fermeture proposee", pid);
        if (MessageBoxW(g_wnd, T(L"My Winter Car tourne encore en arri\u00E8re-plan, sans fen\u00EAtre (rest\u00E9 bloqu\u00E9 apr\u00E8s sa fermeture ?).\n\nLe fermer de force pour pouvoir relancer ?",
                                 L"My Winter Car is still running in the background, without a window (stuck after closing?).\n\nForce it to close so you can launch again?"),
                        L"MWCoop", MB_YESNO | MB_ICONQUESTION) == IDYES) {
            HANDLE t = OpenProcess(PROCESS_TERMINATE | SYNCHRONIZE, FALSE, pid);
            BOOL ok = t && TerminateProcess(t, 1);
            if (t) { WaitForSingleObject(t, 5000); CloseHandle(t); }
            LaunchLog("jeu %lu bloque : fermeture forcee %s", pid, ok ? "faite" : "IMPOSSIBLE");
            if (!GameProcessRunning()) return false;
            if (!ok && !opened) {
                // (droits insuffisants : le processus appartient a un autre utilisateur ou tourne eleve ; le laisser bloquer tout
                // le monde ne sert a rien -- lancer quand meme, en le disant)
                if (MessageBoxW(g_wnd, T(L"Impossible de fermer ce processus (droits insuffisants : lanc\u00E9 en administrateur, ou par un autre utilisateur).\n\nLancer le jeu quand m\u00EAme ?",
                                         L"This process cannot be closed (insufficient rights: started as administrator, or by another user).\n\nLaunch the game anyway?"),
                                L"MWCoop", MB_YESNO | MB_ICONQUESTION) == IDYES) {
                    LaunchLog("jeu %lu bloque et infermable : lancement quand meme (choix du joueur)", pid);
                    return false;
                }
            }
        }
    }
    SetStatus(K_ERR, T(L"My Winter Car est d\u00E9j\u00E0 lanc\u00E9 : ferme-le d'abord", L"My Winter Car is already running: close it first"));
    return true;
}
static BOOL CALLBACK ListUnityWindows(HWND h, LPARAM lp)
{
    wchar_t cls[64];
    if (GetClassNameW(h, cls, 64) && !wcscmp(cls, L"UnityWndClass")) ((std::vector<HWND> *)lp)->push_back(h);
    return TRUE;
}
// Fenetre de configuration d'Unity (« My Winter Car Configuration » : resolution, qualite, bouton Play!) : inutile, l'ecran
// et la qualite se reglent dans le lanceur (Options du jeu) et le mod les applique au menu (demande de JD, 11/10 : « elle
// pop toujours »). Sans MSCLoader (ou son skipConfigScreen coupe), elle s'ouvrait a chaque partie : le lanceur appuie sur
// Play! pour le joueur.
static BOOL CALLBACK SkipUnityDialogWnd(HWND h, LPARAM lp)
{
    wchar_t cls[16], title[128];
    if (!GetClassNameW(h, cls, 16) || wcscmp(cls, L"#32770") || !IsWindowVisible(h)) return TRUE;
    GetWindowTextW(h, title, 128);
    DWORD pid = 0;
    GetWindowThreadProcessId(h, &pid);
    if (!wcsstr(title, L"Configuration") || !IsGamePid(pid)) return TRUE;
    HWND ok = GetDlgItem(h, IDOK);
    PostMessageW(h, WM_COMMAND, MAKEWPARAM(IDOK, BN_CLICKED), (LPARAM)ok);
    *(bool *)lp = true;
    return TRUE;
}
static void SkipUnityDialog()
{
    static DWORD logged;
    bool done = false;
    EnumWindows(SkipUnityDialogWnd, (LPARAM)&done);
    if (done && GetTickCount() - logged > 10000) { logged = GetTickCount(); LaunchLog("fenetre de configuration d'Unity passee (Play!)"); }
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
static std::string ContentLaunchLines();   // (modsync.inc)
static std::string LobbyCoopLines(bool host);   // (salon.inc)
static bool WriteLaunchFile(int mode, const std::wstring &addr, int port, const char *partie)
{
    const Opt *ap = OptByKey("Apparence");
    std::string skin = ap ? ap->svals[OptGet(*ap)] : DefaultShirt();   // (char_shirt21 n'existe pas dans MSC : tenue.inc)
    static const char *modes[] = { "solo", "hote", "invite" };
    std::string t = "[Lancement]\r\n";
    t += std::string("Mode=") + (!g_exportDir.empty() ? "export" : modes[mode]) + "\r\n";   // (export : pas "solo", que le chargeur lance sans MWCoop)
    // Horodatage dans [Lancement], avant tout : le chargeur le lit par section (GetPrivateProfileString) et, depuis les options du
    // salon (0.96.4, [Coop] ajoute en fin), il se retrouvait sous [Coop] -- fichier juge perime, Profil= ignore, toujours la
    // sauvegarde principale (retour de JD, 18/10 : « ca charge toujours la meme sauvegarde »). Le mod, lui, lit a plat.
    t += "Horodatage=" + std::to_string((long long)_time64(NULL)) + "\r\n";
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
    t += std::string("Profil=") + (!g_exportDir.empty() ? "carte-export" : mode == MODE_GUEST ? "invite" : mode == MODE_HOST ? Narrow(SaveCurrentProf(), CP_UTF8) : "") + "\r\n";   // (saves.inc)
    if (!g_exportDir.empty()) t += "ExportCarte=" + Narrow(g_exportDir, CP_UTF8) + "\r\n";   // (editeur de carte : profil a part, jamais les sauvegardes du joueur)
    if (partie && *partie) t += std::string("Partie=") + partie + "\r\n";
    if (mode == MODE_GUEST) t += ContentLaunchLines();
    if (mode != MODE_SOLO && g_lobbyOptGo) { t += LobbyCoopLines(mode == MODE_HOST); g_lobbyOptGo = false; }   // (options choisies dans le salon : salon.inc)
    EnsureModDir();
    std::wstring path = g_modRoot + L"MWCoop\\lancement.ini", tmp = path + L".tmp";
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
// Correctifs de compatibilite de Windows sur mywintercar.exe (cause des « MWCoop ne se charge pas », 09/10) : apres un
// plantage du jeu a la fermeture, Windows ajoute tout seul "$ IgnoreFreeLibrary<d3d11>" pour CE chemin
// (HKCU\...\AppCompatFlags\Layers). Le jeu demarre alors avec le moteur de compatibilite (apphelp, AcGenral), qui charge
// la version.dll de Windows avant la notre : MWCoop n'est jamais charge. Verifie sur une instance de test (avec : VERSION.dll
// de System32 ; sans : la notre). D'ou « ca a marche une fois puis plus jamais », la copie AppData qui lache a son tour,
// Windows 10 qui marche et Windows 11 non (registres differents). Retires avant chaque lancement, au demarrage et a la
// fermeture du jeu, pour le jeu et ses copies de lancement. Ceux de tous les utilisateurs (HKLM : droits administrateur)
// sont seulement signales.
static std::wstring GuestCopyDir();
static int CompatClean(const std::wstring &exe, const char *where)
{
    static const wchar_t *kLayers = L"Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers";
    int removed = 0;
    for (int hk = 0; hk < 2; hk++) {
        HKEY k;
        if (RegOpenKeyExW(hk ? HKEY_LOCAL_MACHINE : HKEY_CURRENT_USER, kLayers, 0, hk ? KEY_QUERY_VALUE : KEY_QUERY_VALUE | KEY_SET_VALUE, &k) != ERROR_SUCCESS) continue;
        std::vector<std::wstring> hits;
        std::vector<std::wstring> vals;
        for (DWORD i = 0;; i++) {
            wchar_t name[1024], data[512];
            DWORD nl = 1024, dl = sizeof(data), type = 0;
            LONG r = RegEnumValueW(k, i, name, &nl, NULL, &type, (BYTE *)data, &dl);
            if (r == ERROR_NO_MORE_ITEMS) break;
            if (r != ERROR_SUCCESS) continue;
            if (_wcsicmp(name, exe.c_str())) continue;
            hits.push_back(name);
            vals.push_back(type == REG_SZ ? std::wstring(data, wcsnlen(data, dl / sizeof(wchar_t))) : L"?");
        }
        for (size_t i = 0; i < hits.size(); i++) {
            if (hk) {
                LaunchLog("compatibilite (%s) : %s = [%s] pour TOUS les utilisateurs : MWCoop ne se chargera pas (Proprietes du jeu > Compatibilite > Modifier les parametres pour tous les utilisateurs)", where, Narrow(exe, CP_UTF8).c_str(), Narrow(vals[i], CP_UTF8).c_str());
                SetStatus(K_WARN, T(L"Un mode de compatibilité Windows empêche MWCoop de se charger : mywintercar.exe > Propriétés > Compatibilité > pour tous les utilisateurs, tout décocher",
                                    L"A Windows compatibility mode stops MWCoop from loading: mywintercar.exe > Properties > Compatibility > for all users, untick everything"));
                continue;
            }
            bool ok = RegDeleteValueW(k, hits[i].c_str()) == ERROR_SUCCESS;
            LaunchLog("compatibilite (%s) : %s = [%s] -> %s", where, Narrow(exe, CP_UTF8).c_str(), Narrow(vals[i], CP_UTF8).c_str(), ok ? "retire (sinon la version.dll de Windows passe avant celle de MWCoop)" : "RETRAIT IMPOSSIBLE");
            if (ok) removed++;
        }
        RegCloseKey(k);
    }
    return removed;
}
static bool CompatAuto() { return GetPrivateProfileIntW(L"Lanceur", L"RetirerCompat", 1, g_iniLauncher.c_str()) != 0; }   // (Reglages > Jeu)
static void CompatCleanAll(const char *where)
{
    if (g_gameDir.empty()) return;
    if (IsWine()) return;   // (Wine : pas de moteur de compatibilite de Windows, rien a retirer)
    if (!CompatAuto()) { LaunchLog("compatibilite (%s) : retrait coupe dans les reglages", where); return; }
    std::vector<std::wstring> dirs = { g_gameDir, MirrorDir(), GuestCopyDir(), MscCopyDir() };
    for (const std::wstring &d : dirs) if (!d.empty()) CompatClean(d + GM().exe, where);
}

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
    if (ok && !MoveFileExW(tmp.c_str(), p.c_str(), MOVEFILE_REPLACE_EXISTING))
        ok = MoveAside(p) && MoveFileExW(tmp.c_str(), p.c_str(), MOVEFILE_REPLACE_EXISTING);
    if (!ok) { DWORD e = GetLastError(); DeleteFileW(tmp.c_str()); LaunchLog("chargeur (%s) : %s, remplacement IMPOSSIBLE (erreur %lu)", where, what.c_str(), e); return; }
    LaunchLog("chargeur (%s) : %s -> remplace par celui du lanceur (%s)%s", where, what.c_str(), VerText(mine).c_str(), present && !ours ? ", l'autre garde en version.dll.autre" : "");
    if (present && !ours)
        SetStatus(K_WARN, T(L"Une version.dll d'un autre programme \u00E9tait dans le dossier du jeu : mise de c\u00F4t\u00E9 (version.dll.autre)", L"A version.dll from another program was in the game folder: set aside (version.dll.autre)"));
}

static int SyncLaunchKind();   // (modsync.inc)
static bool SyncLaunchPending();
static bool g_launchAfterSync;
// Invite arrive apres le lancement (demande de JD, 13/10) : l'hote joue deja ; ses mods sont recus d'abord (salon IP : par
// le lanceur de l'hote, qui les sert encore ; Steam : par son jeu, Net/ModServe.cs), puis le jeu de l'invite part tout seul.
static bool g_lateSync, g_lateSteam;
static DWORD g_lateT;
// Hote, salon IP : apres LANCER, l'ecoute du salon reste ouverte pour servir ses mods aux invites en retard.
static std::atomic<bool> g_lateServe(false);
static void SyncWriteLateOffer();   // (modsync.inc)
static bool LateBegin(bool steam);   // (modsync.inc : invite en retard, debut / fin / echec)
static void LateEnd();
static void LateFail(const wchar_t *why);
static bool SyncHostHasMods();   // (modsync.inc)
// Hote : cet invite recoit (ou va recevoir) l'envoi -- LANCER attend. Pas encore dit (il vient d'arriver, sa liste n'est pas
// encore traitee) : compte aussi quand l'hote envoie des mods, 30 s au plus (jamais bloque pour de bon).
static bool PeerSyncPending(const LobbyPeer &p)
{
    return p.id != 0 && (p.dl >= 0 || (p.dl == -2 && SyncHostHasMods() && GetTickCount() - p.since < 30000));
}
static std::wstring GuestCopyDir();
static bool PrepareGuestCopy(const std::wstring &m, bool mods);
static bool g_syncKeep;   // GO recu : la copie des mods sert au lancement (LobbyClose ne l'oublie pas)
static void Launch(int mode, const char *partie = NULL)
{
    if (mode == MODE_SOLO) g_launchWarn.clear();   // (avertissement UDP : salon seulement)
    if (g_gameDir.empty() || g_busy || !g_modOk) { TestLog("lancement impossible (jeu=%d occupe=%d mod=%d)", (int)!g_gameDir.empty(), (int)g_busy, (int)g_modOk); return; }
    // Invite : les mods de l'hote d'abord (le jeu partira tout seul a la fin de la reception) -- un invite sans eux entrait
    // quand meme (retour de Menos, 10/10).
    if (mode == MODE_GUEST && SyncLaunchPending()) {
        if (g_lobby == LB_GUEST || g_lateSync) {
            g_launchAfterSync = true;
            SetStatus(K_NORMAL, T(L"Réception des mods de l'hôte… le jeu se lancera tout seul ensuite", L"Receiving the host's mods… the game will start on its own afterwards"));
            TestLog("lancement de l'invite retenu : mods de l'hote pas encore recus");
            return;
        }
        // (plus de salon -- l'hote a lance pendant la reception, ancien lanceur : avant, l'invite restait bloque sans jeu)
        g_launchAfterSync = false;
        TestLog("lancement de l'invite : reception des mods interrompue (salon ferme), sans eux");
    }
    if (mode == MODE_HOST) SyncWriteLateOffer();   // (son jeu sert ses mods aux invites Steam en retard)
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
    } else if (mode != MODE_GUEST && g_testSalon.empty() && GameRunningAsk()) {   // (instance unique : un 2e jeu hors profil se fermerait aussitot)
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
        ReadAll(g_modRoot + L"MWCoop\\lancement.ini", d);
        std::string text(d.begin(), d.end());
        TestLog("lancement.ini ecrit (%s) :\n%s", Narrow(g_modRoot + L"MWCoop\\lancement.ini", CP_UTF8).c_str(), text.c_str());
        if (mode == MODE_HOST && g_testHoldMs) { TestLog("test : hote garde ouvert %u ms (envoi aux invites en retard %d)", (unsigned)g_testHoldMs, (int)g_lateServe); SetTimer(g_wnd, 77, g_testHoldMs, NULL); return; }
        TestLog("test : fin (jeu NON lance, mode d'essai)");
        DestroyWindow(g_wnd);
        return;
    }
    // steam_appid.txt a cote du jeu : sans lui, le jeu lance directement se ferme et demande a Steam de le
    // relancer ; la copie relancee par Steam (overlay injecte au demarrage) a deja la version.dll de Windows en
    // memoire, et le mod ne se charge pas (vu le 05/10 sur l'installation Steam de JD). Steam doit etre ouvert.
    if (ModInGame()) {   // (lanceur installe a part : le jeu part de la copie, qui a le sien ; rien dans le dossier du jeu)
        std::wstring appid = g_gameDir + L"steam_appid.txt";
        if (GetFileAttributesW(appid.c_str()) == INVALID_FILE_ATTRIBUTES) {
            HANDLE f = CreateFileW(appid.c_str(), GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
            if (f != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(f, Narrow(GM().appid).c_str(), (DWORD)wcslen(GM().appid), &n, NULL); CloseHandle(f); TestLog("steam_appid.txt cree"); }
            else TestLog("steam_appid.txt : creation impossible (erreur %lu)", GetLastError());
        }
    }
    static const wchar_t *modes[] = { L"solo", L"hote", L"invite" };
    std::wstring args = std::wstring(L"-mwcoop-mode ") + (!g_exportDir.empty() ? L"export" : modes[mode]) + L" -mwcoop-port " + std::to_wstring(port);
    if (mode == MODE_GUEST) args += (g_steamNet ? std::wstring() : L" -mwcoop-adresse " + addr) + L" -mwcoop-profil invite";
    if (!g_exportDir.empty()) args += L" -mwcoop-profil carte-export -mwcoop-arriereplan";   // (export de la carte : fenetre hors de l'ecran)
    // Ray tracing (MWCoopRT.dll) : Unity doit dessiner sur un seul fil pour que le mod s'insere dans son rendu.
    if (GetPrivateProfileIntW(L"Graphismes", L"RayTracing", 0, ModIni().c_str()) == 1) { args += L" -force-gfx-direct"; LaunchLog("ray tracing demande : -force-gfx-direct"); }
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
    CompatCleanAll("lancement");
    if (ModInGame()) LoaderEnsure(g_gameDir, "dossier du jeu");   // (a part : rien dans le dossier du jeu)
    const Opt *viaSteam = OptByKey("LancerSteam");
    g_steamLaunch = viaSteam && OptGet(*viaSteam) && !syncKind && g_exportDir.empty() && !MirrorDir().empty() && ModInGame();   // (a part : Steam lancerait le jeu sans MWCoop)
    // Wine : steam://rungameid passerait par le Steam de Linux, hors du lanceur et sans WINEDLLOVERRIDES (le mod ne se
    // chargerait pas) : le jeu part d'ici. Pour jouer depuis Steam, l'option de lancement Linux (SteamLaunchLine).
    if (g_steamLaunch && IsWine()) { g_steamLaunch = false; LaunchLog("Wine : \"Lancer par Steam\" ignore, le jeu part du lanceur"); }
    if (g_steamLaunch) {
        Sleep(800);   // (SteamDown ci-dessus : que Steam ne voie plus le lanceur comme le jeu en cours)
        if (!LocalDir().empty()) {   // (si Steam nous relance par l'option de lancement : demarrer le jeu, wWinMain)
            HANDLE rq = CreateFileW((LocalDir() + L"lancer-par-steam.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
            if (rq != INVALID_HANDLE_VALUE) { DWORD n; WriteFile(rq, "1", 1, &n, NULL); CloseHandle(rq); }
        }
        HINSTANCE r = ShellExecuteW(g_wnd, L"open", (std::wstring(L"steam://rungameid/") + GM().appid).c_str(), NULL, NULL, SW_SHOWNORMAL);
        LaunchLog("lancement (mode %d) par Steam (steam://rungameid/%s) : %s", mode, Narrow(GM().appid).c_str(), (INT_PTR)r > 32 ? "demande" : "ECHEC");
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
    std::wstring exe = g_gameDir + GM().exe, runDir = g_gameDir;
    g_launchedFromCopy = false;
    std::wstring mirror = syncKind ? GuestCopyDir() : MirrorDir();
    // Dossier du jeu plutot que la copie de lancement ([Lanceur] SansCopie, pris tout seul quand MWCoop ne s'est pas charge
    // depuis la copie) : chez un joueur (08/10), le jeu parti de la copie dans AppData ne chargeait jamais MWCoop -- notre
    // version.dll y etait bloquee --, alors que parti du dossier du jeu, si. Chez d'autres, c'est l'inverse (copie creee
    // pour ca, 05/10) : chacun garde ce qui marche chez lui.
    bool noCopy = !syncKind && NoCopy() && ModInGame();
    if (noCopy && !mirror.empty()) { mirror.clear(); LaunchLog(IsWine() ? "depart du dossier du jeu (Wine : SansCopie par defaut)" : "depart du dossier du jeu (SansCopie : MWCoop ne se chargeait pas depuis la copie)"); }
    // MSCLoader de MWCoop (onglet MODS) : toujours une copie de lancement, avec son Doorstop (jeu Steam ou non).
    bool ownMsc = MscOwn() && MscOwnInstalled();
    if (!syncKind && ownMsc && g_mscOn) mirror = MscCopyDir();
    else ownMsc = ownMsc && syncKind;
    if (syncKind && !mirror.empty()) {   // invite d'un salon : sa copie de lancement (Mods -> les mods de l'hote)
        if (PrepareGuestCopy(mirror, syncKind == 2) && (!ownMsc || MscOwnApply(mirror, syncKind == 2, true))) { exe = mirror + GM().exe; runDir = mirror; }
        else SetStatus(K_WARN, T(L"Copie de lancement impossible : le jeu part sans les mods de l'h\u00F4te", L"Could not prepare the launch copy: the game starts without the host's mods"));
    } else if (!mirror.empty()) {
        if (PrepareMirror(mirror) && (!ownMsc || MscOwnApply(mirror, true, false))) { exe = mirror + GM().exe; runDir = mirror; g_launchedFromCopy = !ownMsc && mirror == MirrorDir(); }
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
    bool viaShell = (g_fromSteam || overlay) && !IsWine();   // (Wine : le jeu doit heriter de l'environnement du lanceur)
    WineOverrides("lancement");
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
    if (!IsWine() && ShellRunFromDesktop(exe, args, runDir)) {
        LaunchLog("jeu lance par le bureau de Windows, avec ses arguments");
        g_proc = NULL;
        g_pid = 0;
        done();
        return;
    }
    LaunchLog(IsWine() ? "Wine : lancement direct (environnement du lanceur)" : "bureau de Windows indisponible : %s", viaShell ? "explorer.exe" : "lancement direct");
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

#include "salon.inc"

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
    if (IsWine()) return FW_UNKNOWN;   // (Wine : pas de pare-feu de Windows ; celui de Linux n'est pas lisible d'ici)
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
    if (IsWine()) { g_fwAsked = true; LaunchLog("pare-feu : Wine, rien a verifier (le pare-feu de Linux, s'il y en a un, doit laisser passer le port du jeu)"); return true; }
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
    int r = MessageBoxW(g_wnd, msg.c_str(), ProductName(), MB_YESNOCANCEL | MB_ICONWARNING);
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

static void LateServeStop()
{
    if (!g_lateServe) return;
    g_lateServe = false;
    if (g_lobby == LB_NONE && g_listen != INVALID_SOCKET) { closesocket(g_listen); g_listen = INVALID_SOCKET; }
    TestLog("salon : envoi des mods aux invites en retard arrete");
}
static void LobbyHost()
{
    LateServeStop();
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
    LobbyOptImport();   // (salon prive / public, joueurs max, options de la partie : valeurs du moment, pour ce salon)
    g_lobbyCode = g_testCode.empty() ? MakeSessionCode() : g_testCode;
    if (g_testPublic) g_lobbyPrive = false;
    LobbyChatClear();
    g_lobby = LB_HOST;
    g_tab = TAB_LOBBY;
    g_scroll[TAB_LOBBY] = 0;
    g_focus = -1;
    HANDLE t = CreateThread(NULL, 0, AcceptThread, (void *)ls, 0, NULL);
    if (t) CloseHandle(t);
    SetStatus(K_OK, T(L"Salon ouvert \u00B7 port %d", L"Lobby open \u00B7 port %d"), g_lobbyPort);
    TestLog("salon : ouvert sur le port TCP %d (version %s, %s, %s)", g_lobbyPort, MyVersion().c_str(), MyName().c_str(), MySkin().c_str());
    TestLog("salon : %s, code %s, %d joueurs au plus, options de la partie %s", g_lobbyPrive ? "prive" : "public", g_lobbyCode.c_str(), g_lobbyMax, LobbyOptText().c_str());
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

// keepListen : LANCER (salon IP) -- l'ecoute reste ouverte, pour servir les mods aux invites en retard (g_lateServe).
static void LobbyClose(bool keepListen = false)
{
    if (g_lobbySteam) SteamLobbyLeave(g_lobby == LB_HOST);
    g_lobbyGen++;
    g_lobby = LB_NONE;
    EnterCriticalSection(&g_lcs); g_hostSaveName.clear(); LeaveCriticalSection(&g_lcs);
    if (!keepListen) g_lateServe = false;
    if (g_listen != INVALID_SOCKET && !keepListen) { closesocket(g_listen); g_listen = INVALID_SOCKET; }
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
    g_sCodeAsk = false;
    LobbyChatClear();   // (l'historique du tchat vit le temps du salon)
    if (!g_syncKeep) { EnterCriticalSection(&g_lcs); g_hostOpt.clear(); LeaveCriticalSection(&g_lcs); }   // (GO : gardees pour le lancement)
}

// LANCER : actif quand tous les invites sont prets ; sinon, une question permet de forcer.
static void HostStart()
{
    if (g_lobby != LB_HOST || g_goSent) return;
    if (g_testSalon.empty() && GameRunningAsk()) return;
    {   // un invite recoit encore l'envoi : pas de lancement, meme force (demande de JD, 13/10 : son jeu partirait sans les mods)
        std::string dl;
        EnterCriticalSection(&g_lcs);
        for (auto &p : g_peers) if (PeerSyncPending(p)) { char b[300]; sprintf_s(b, "%s (%d %%)", p.name.c_str(), max(0, p.dl)); dl += (dl.empty() ? "" : ", ") + std::string(b); }
        LeaveCriticalSection(&g_lcs);
        if (!dl.empty()) {
            TestLog("salon : LANCER refuse, envoi en cours chez %s", dl.c_str());
            std::wstring q = std::wstring(T(L"Ces invités reçoivent encore tes mods : ", L"These guests are still receiving your mods: ")) + Widen(dl, CP_UTF8)
                           + T(L"\n\nAttends la fin de leur téléchargement pour lancer la partie.", L"\n\nWait for their download to finish before starting the game.");
            if (g_testSalon.empty()) MessageBoxW(g_wnd, q.c_str(), ProductName(), MB_OK | MB_ICONINFORMATION);
            else SetStatus(K_WARN, L"%s", q.c_str());
            return;
        }
    }
    int guests, notReady;
    GuestCounts(&guests, &notReady);
    if (notReady && g_testSalon.empty()) {
        wchar_t q[400];
        swprintf_s(q, T(L"%d invit\u00E9(s) sur %d pas encore pr\u00EAt(s).\n\nLancer quand m\u00EAme ? Leur jeu d\u00E9marrera aussi.",
                        L"%d of %d guest(s) not ready yet.\n\nStart anyway? Their game will start too."), notReady, guests);
        if (MessageBoxW(g_wnd, q, ProductName(), MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) != IDYES) return;
        if (g_lobby != LB_HOST || g_goSent) return;   // (le salon a pu changer pendant la question)
    }
    if (g_lobbySteam) {   // salon Steam : go dans le salon, les invites lancent leur jeu en le voyant
        int partie = g_partie;
        g_goSent = true;
        g_launchWarn.clear();
        TestLog("salon steam : LANCER (%d invite(s), %d pas prets), partie=%s", guests, notReady, PartieName(partie));
        SteamHostStart(partie);
        LobbyClose();
        g_lobbyOptGo = true;   // (les options du salon dans lancement.ini)
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
    LobbyClose(true);
    g_lateServe = true;   // (ses mods restent servis aux invites qui arrivent pendant la partie)
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
    g_lobbyOptGo = true;   // (les options du salon dans lancement.ini)
    Launch(MODE_HOST, PartieName(partie));
}

// Choix de partie (hote). Nouvelle partie : fenetre des sauvegardes (saves.inc : nouvelle sauvegarde, ou en recommencer une
// avec confirmation) ; elle appelle LobbyApplyPartie une fois le choix fait.
static void LobbyApplyPartie(int p);
static void LobbySetPartie(int p)
{
    if (g_lobby != LB_HOST || g_goSent || p == g_partie) return;
    if (p == PARTIE_NOUVELLE && g_testSalon.empty()) { SaveOpenNewGame(); return; }
    LobbyApplyPartie(p);
}
static void LobbyApplyPartie(int p)
{
    if (g_lobby != LB_HOST || g_goSent) return;
    g_partie = p;
    TestLog("salon : partie = %s", PartieName(p));
    if (g_lobbySteam) SSetLobby("partie", PartieName(p));
    else BroadcastState();
}

// --- invite
// Dernier hote rejoint ([Lanceur] DernierHote, mwcoop-lanceur.ini) : la page MODS s'en sert (mods recus de cet hote).
static void LobbyLastHost(const std::string &name)
{
    if (name.empty()) return;
    WritePrivateProfileStringW(L"Lanceur", L"DernierHote", Widen(name, CP_UTF8).c_str(), g_iniLauncher.c_str());
    TestLog("salon : dernier hote %s", name.c_str());
}
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
    hello.u8(M_HELLO); hello.u8(LB_PROTO); hello.str(MyVersion()); hello.str(MyName()); hello.str(g_mySkinSent); hello.str(g_joinCode);   // (code de session : 0.96.4)
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
    if (t == M_LATE) {   // l'hote joue deja : ses mods d'abord (WM_APP_LATE), puis son jeu
        EnterCriticalSection(&g_lcs);
        bool stale = gen != g_lobbyGen;
        if (!stale) g_guestSock = s;
        LeaveCriticalSection(&g_lcs);
        if (stale) { closesocket(s); return 0; }
        TestLog("salon : partie deja lancee, reception des mods de l'hote");
        PostMessageW(g_wnd, WM_APP_LATE, 0, gen);
        to = 60000;
        setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char *)&to, sizeof(to));
        while (RecvMsg(s, m)) {
            Rd q(m);
            int type = q.u8();
            if (type == M_MODS) SyncOnOffer(m);
            else if (type == M_MODSX) SyncOnOfferMore(m);
            else if (type == M_MODDATA) SyncOnData(m);
        }
        EnterCriticalSection(&g_lcs);
        if (g_guestSock == s) g_guestSock = INVALID_SOCKET;
        LeaveCriticalSection(&g_lcs);
        closesocket(s);
        PostMessageW(g_wnd, WM_APP_LATEEND, 0, gen);
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
    bool go = false, kicked = false;
    std::string lastList, lastHost;
    while (!go && !kicked && RecvMsg(s, m)) {
        Rd q(m);
        int type = q.u8();
        if (type == M_STATE) {
            int n = q.u8();
            std::vector<LobbyPeer> peers;
            std::string list;
            for (int i = 0; i < n && i < kLobbyMaxAbs; i++) {
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
                if (q.p < m.size()) { std::string sn = q.str(); if (q.ok) { EnterCriticalSection(&g_lcs); g_hostSaveName = Widen(sn, CP_UTF8); LeaveCriticalSection(&g_lcs); list += " ; sauvegarde=" + sn; } }
                q.ok = true;
                if (q.p < m.size()) {   // salon (joueurs max, prive, code) et options de la partie de l'hote (0.96.4)
                    int mx = q.u8(), pv = q.u8();
                    std::string cd = q.str();
                    int no = q.u8();
                    std::map<std::string, int> ho;
                    for (int i = 0; i < no && q.ok; i++) { std::string k = q.str(); int v = q.u16(); if (q.ok) ho[k] = v; }
                    if (q.ok) {
                        g_lobbyMax = max(2, min(kLobbyMaxAbs, mx));
                        g_lobbyPrive = pv != 0;
                        g_lobbyCode = cd;
                        EnterCriticalSection(&g_lcs);
                        g_hostOpt = ho;
                        LeaveCriticalSection(&g_lcs);
                        std::string ot;
                        for (auto &kv : ho) ot += kv.first + "=" + std::to_string(kv.second) + ";";
                        list += " ; max=" + std::to_string(mx) + (pv ? " prive code=" + cd : " public") + " options=" + ot;
                    }
                    q.ok = true;
                }
            }
            if (q.ok) {
                EnterCriticalSection(&g_lcs);
                g_peers = peers;
                LeaveCriticalSection(&g_lcs);
                for (auto &p : peers) if (p.id == 0 && p.name != lastHost) { lastHost = p.name; LobbyLastHost(p.name); }   // (page MODS : le dernier hote)
                g_partie = partie == PARTIE_NOUVELLE ? PARTIE_NOUVELLE : PARTIE_CONTINUER;
                list += std::string(" ; partie=") + PartieName(g_partie);
                if (list != lastList) { TestLog("salon : liste %s", list.c_str()); lastList = list; }
            }
        } else if (type == M_MODS) SyncOnOffer(m);
        else if (type == M_MODSX) SyncOnOfferMore(m);
        else if (type == M_MODDATA) SyncOnData(m);
        else if (type == M_PING) {
            Wr w; w.u8(M_PONG); w.u32(q.u32());
            GuestSend(w);
        } else if (type == M_GO) {
            go = true;
            int partie = q.u8();
            TestLog("salon : GO recu (partie=%s)", PartieName(partie));
            PostMessageW(g_wnd, WM_APP_GO, partie, gen);
        } else if (type == M_CHAT) {   // (tchat : relaye par l'hote, numero du joueur puis le texte)
            int id = q.u8(), n = q.u16();
            if (q.ok && q.p + n <= m.size()) {
                std::string name;
                EnterCriticalSection(&g_lcs);
                for (auto &p : g_peers) if (p.id == id) name = p.name;
                LeaveCriticalSection(&g_lcs);
                LobbyChatAdd(id, name.empty() ? "?" : name, m.substr(q.p, n));
            }
        } else if (type == M_REJECT) {   // (en cours de salon : l'hote me met dehors)
            std::string why = q.str();
            EnterCriticalSection(&g_lcs);
            g_rejectWhy = q.ok ? why : "kicked";
            LeaveCriticalSection(&g_lcs);
            kicked = true;
            TestLog("salon : mis dehors par l'hote (%s)", why.c_str());
        }
    }
    EnterCriticalSection(&g_lcs);
    g_guestSock = INVALID_SOCKET;
    LeaveCriticalSection(&g_lcs);
    closesocket(s);
    if (!go) PostMessageW(g_wnd, WM_APP_LOBBYEND, kicked ? 1 : 0, gen);
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
    LobbyOptImport();   // (mes options pour ce salon ; celles de l'hote arrivent par STATE)
    g_lobbyPrive = false; g_lobbyMax = 8; g_lobbyCode.clear();
    g_joinCode = g_testCode.empty() ? CodeNorm(g_fields[2].text) : g_testCode;
    g_mySkinSent = MySkin();
    LobbyChatClear();
    int gen = ++g_lobbyGen;
    g_lobby = LB_CONNECTING;
    g_tab = TAB_LOBBY;
    g_scroll[TAB_LOBBY] = 0;
    g_focus = -1;
    SetStatus(K_NORMAL, T(L"Connexion au salon de %s\u2026", L"Connecting to %s's lobby\u2026"), g_lobbyAddr.c_str());
    TestLog("salon : connexion a %s:%d (version %s, %s, %s)", Narrow(g_lobbyAddr).c_str(), g_lobbyPort, MyVersion().c_str(), MyName().c_str(), g_mySkinSent.c_str());
    if (!g_joinCode.empty()) TestLog("salon : code de session envoye %s", g_joinCode.c_str());
    HANDLE t = CreateThread(NULL, 0, GuestThread, (void *)(intptr_t)gen, 0, NULL);
    if (t) CloseHandle(t);
}
// Invite : PRET pendant que l'envoi de l'hote (mods, CD, images) se telecharge -> pret des qu'il est recu (SyncTick).
// Avant : pret tout de suite ; l'hote pouvait lancer, le GO fermait le salon et coupait le telechargement, et le jeu de
// l'invite partait sans le contenu de l'hote (retours de joueurs, 09/10 : « ca telecharge, mais en jeu on ne voit rien »).
static bool g_wantReady;
static bool SyncBusy();   // (modsync.inc)
static void GuestToggleReady()
{
    if (g_lobby != LB_GUEST) return;
    if (!g_meReady && SyncBusy()) {
        g_wantReady = !g_wantReady;
        if (g_wantReady) SetStatus(K_NORMAL, T(L"Tu seras pr\u00EAt d\u00E8s que l'envoi de l'h\u00F4te est re\u00E7u", L"You'll be ready as soon as the host's files are received"));
        return;
    }
    g_wantReady = false;
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
// Reparer MWCoop (Reglages > Lanceur > Depannage) : ce que fait un lancement, sans lancer -- modes de compatibilite retires,
// chargeur (version.dll) verifie dans le dossier du jeu, copie de lancement refaite (jonctions, fichiers, Doorstop du
// MSCLoader de MWCoop) et son chargeur verifie. Resultat dans la ligne d'etat.
static void RepairMWCoop()
{
    if (g_gameDir.empty()) { SetStatus(K_WARN, T(L"My Winter Car introuvable : choisis le dossier du jeu", L"My Winter Car not found: choose the game folder")); return; }
    CompatCleanAll("reparation");
    if (ModInGame()) LoaderEnsure(g_gameDir, "reparation");   // (installe a part : pose dans la copie au lancement)
    bool ownMsc = MscOwn() && MscOwnInstalled() && g_mscOn;
    std::wstring m = ownMsc ? MscCopyDir() : MirrorDir();
    bool ok = true;
    if (!m.empty()) {
        ok = PrepareMirror(m) && (!ownMsc || MscOwnApply(m, true, false));
        if (ok) LoaderEnsure(m, "reparation");
    }
    LaunchLog("reparation : %s (copie %ls)", ok ? "ok" : "ECHEC", m.empty() ? L"aucune" : m.c_str());
    if (ok) SetStatus(K_OK, m.empty() ? T(L"MWCoop réparé : chargeur vérifié dans le dossier du jeu", L"MWCoop repaired: loader checked in the game folder")
                                      : T(L"MWCoop réparé : copie de lancement et chargeur réécrits", L"MWCoop repaired: launch copy and loader rewritten"));
    else SetStatus(K_ERR, T(L"Réparation incomplète : copie de lancement impossible (voir lanceur.log)", L"Repair incomplete: could not prepare the launch copy (see lanceur.log)"));
}
// Tester ma connexion (Reglages > Co-op > Reseau) : le port UDP et TCP du jeu se laissent ouvrir ici (ou le salon les tient
// deja), le pare-feu Windows laisse passer le jeu, Steam repond. Pas de test depuis l'exterieur (il faudrait un serveur) :
// la redirection de la box reste a verifier avec un ami. Resultat garde pour la pastille de la carte, et dans la ligne d'etat.
static std::wstring g_netTest;   // (vide : pas encore teste)
static int g_netTestKind = K_NORMAL;
static void NetTest()
{
    int port = 7870;
    if (const Opt *o = OptByKey("Port")) port = OptGet(*o);
    auto bindOk = [&](int type) {
        SOCKET s = socket(AF_INET, type, type == SOCK_DGRAM ? IPPROTO_UDP : IPPROTO_TCP);
        if (s == INVALID_SOCKET) return false;
        sockaddr_in a = {}; a.sin_family = AF_INET; a.sin_addr.s_addr = INADDR_ANY; a.sin_port = htons((u_short)port);
        BOOL excl = TRUE; setsockopt(s, SOL_SOCKET, SO_EXCLUSIVEADDRUSE, (const char *)&excl, sizeof(excl));
        bool ok = bind(s, (sockaddr *)&a, sizeof(a)) == 0;
        closesocket(s);
        return ok;
    };
    bool mine = g_lobby != LB_NONE;   // (le salon tient deja le port : c'est le notre)
    bool udp = mine || bindOk(SOCK_DGRAM), tcp = mine || bindOk(SOCK_STREAM);
    int fw = FirewallState(g_gameDir + GM().exe);
    bool steamWas = g_steamUp == 1, steam = SteamUp();
    if (steam && !steamWas && g_lobby == LB_NONE) SteamDown();   // (ouvert pour le test seulement)
    wchar_t b[200];
    swprintf_s(b, T(L"UDP %d %s · TCP %d %s · pare-feu %s · Steam %s", L"UDP %d %s · TCP %d %s · firewall %s · Steam %s"),
               port, udp ? T(L"libre", L"free") : T(L"OCCUPÉ", L"BUSY"), port, tcp ? T(L"libre", L"free") : T(L"OCCUPÉ", L"BUSY"),
               fw == FW_OK ? L"ok" : fw == FW_BLOCKED ? T(L"BLOQUE le jeu", L"BLOCKS the game") : fw == FW_MISSING ? T(L"sans règle", L"no rule") : IsWine() ? L"Linux" : L"?",
               steam ? T(L"connecté", L"connected") : T(L"fermé", L"closed"));
    bool bad = !udp || !tcp || fw == FW_BLOCKED, warn = fw == FW_MISSING || !steam;
    g_netTest = b;
    g_netTestKind = bad ? K_ERR : warn ? K_WARN : K_OK;
    LaunchLog("test de connexion : %ls", b);
    SetStatus(g_netTestKind, L"%ls", b);
}

#include "api.inc"
#include "outils.inc"
#include "gfx.inc"
#include "media.inc"
#include "saves.inc"
#include "ui.inc"
#include "wiki.inc"
#include "tuto.inc"
#include "install.inc"
// Reglages > Co-op : « Revoir le guide de l'hote » ouvre l'article du guide (wikidata.inc : Heberger une partie, 5e article).
static void WikiOpenHost()
{
    for (int a = 0; a < kWikiArts; a++) if (kWikiArt[a].b == kW_Host) { g_wikiSel = a; break; }   // (sommaire a plat depuis le rework : plus de categories pliees)
    g_scroll[TAB_WIKI] = 0;
    GoPage(TAB_WIKI);
}

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
        g.SetTextRenderingHint(g_textHint);
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

// --- dessin du salon (page SALON) : bandeau du code, Joueurs et Options a gauche ; a droite la partie, l'envoi, les mods,
// le conseil et les boutons (fonctions plus bas)
static float LobbyMaxScroll()
{
    if (g_lobbySteam && g_sFriendsView) return SteamFriendsMaxScroll();
    EnterCriticalSection(&g_lcs);
    int n = (int)g_peers.size();
    LeaveCriticalSection(&g_lcs);
    int rows = n + (n < g_lobbyMax ? 1 : 0);
    return max(0.0f, rows * kLobbyRowH - kLobbyList.Height);
}
// 0/1 : partie ; 50 retour au salon, 51 "inviter des amis" (place libre, salon Steam), 100+i ami i ; 200+i ligne i d'un
// invite (hote IP : le mettre dehors en deux clics) ; 70 : TOUT ACCEPTER / REESSAYER (invite), 71 : CHOISIR (hote, page PARTAGE).
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
        if (n < g_lobbyMax && (int)((y - kLobbyList.Y + g_scroll[TAB_LOBBY]) / kLobbyRowH) == n) return 51;
    } else if (!g_lobbySteam && kLobbyList.Contains(x, y)) {   // hote IP : la ligne d'un invite (deux clics : dehors)
        EnterCriticalSection(&g_lcs);
        int n = (int)g_peers.size();
        LeaveCriticalSection(&g_lcs);
        int i = (int)((y - kLobbyList.Y + g_scroll[TAB_LOBBY]) / kLobbyRowH);
        float ry = kLobbyList.Y + i * kLobbyRowH - g_scroll[TAB_LOBBY];
        if (i >= 1 && i < n && y < ry + kLobbyRowH - 8) return 200 + i;
    }
    for (int i = 0; i < 2; i++) if (kChoiceR[i].Contains(x, y)) return i;
    return -1;
}

// --- SALON (0.97.0, maquette validee par JD le 17/10) : en haut le bandeau du code de session (hote) ou de l'hote (invite) ;
// dessous deux colonnes, Joueurs et Options (la partie / les miennes ; chez l'invite : de l'hote, en lecture seule, / les
// miennes) ; a droite une seule carte : Partie, Envoye aux invites (ou Envoi de l'hote), Mods de la partie, conseil ou
// avertissement UDP, puis LANCER / PRET et FERMER / QUITTER. Le reseau et les identifiants UI_LOBBY_* ne changent pas.
static const GfxRow *GfxRowByKey(const char *k) { for (auto &r : GfxRows()) if (!strcmp(r.key, k)) return &r; return NULL; }
static int LobbyOptIdx(const char *key) { for (int i = 0; i < kLobbyOptN; i++) if (!strcmp(kLobbyOptDefs[i].key, key)) return i; return -1; }
static const int kPeerPal[16] = { 0x3E86D0, 0xE0702A, 0x2A9C9A, 0x8E5BD6, 0xD64545, 0xC9971A, 0xD45D9A, 0x6C7A89, 0x55B04C, 0x3FBFD9, 0xE8A33C, 0xB24FA5, 0x8C6239, 0x5C6BC0, 0xA9C43A, 0xE88A7A };   // (meme palette que Sync/Markers.cs : pastille carte, marqueur, pseudo)
// (kAjoutKeys, kAjoutN : gfx.inc, inclus plus haut -- les memes ajouts)
static int LobbyOptTab(bool host) { return g_lobbyOptTab < 0 ? (host ? 0 : 1) : g_lobbyOptTab; }   // (onglet par defaut : la partie chez l'hote, les miennes chez l'invite)
// Petit cadenas : options decidees par l'hote, en lecture seule chez l'invite.
static void DrawLock(Graphics &g, float cx, float cy, Color c)
{
    Pen pen(c, 1.6f);
    pen.SetStartCap(LineCapRound); pen.SetEndCap(LineCapRound);
    GraphicsPath body; RoundRect(body, RectF(cx - 5.5f, cy - 1, 11, 8.5f), 2);
    g.DrawPath(&pen, &body);
    g.DrawArc(&pen, cx - 3.5f, cy - 7.5f, 7.0f, 7.5f, 180.0f, 180.0f);
}
// Pastille (Hote, Pret, Attend, Pas pret ; chips de l'envoi) : fond teinte, ou simple contour gris.
static float ChipW(const std::wstring &s, float px = 11) { return PillW(s, px) + 6; }
static void Chip(Graphics &g, RectF r, const std::wstring &s, Color tint, bool outline, float px = 11)
{
    GraphicsPath p; RoundRect(p, r, r.Height / 2);
    if (outline) { SolidBrush b(TH(card)); g.FillPath(&b, &p); Pen pen(TH(fieldBorder), 1.2f); g.DrawPath(&pen, &p); }
    else { SolidBrush b(WithA(tint, 0.2f)); g.FillPath(&b, &p); Pen pen(WithA(tint, 0.45f), 1); g.DrawPath(&pen, &p); }
    Text(g, s, r, px, FontStyleBold, outline ? kGrey : tint);
}
// Portrait d'un joueur : tuile arrondie bordee de sa couleur (le portrait, un disque, deborde un peu : les coins sont couverts).
static void DrawLobbyPortrait(Graphics &g, RectF c, const std::string &skin, Color col, const std::wstring &name)
{
    GraphicsPath p; RoundRect(p, c, 14);
    RectF big(c.X - 4, c.Y - 4, c.Width + 8, c.Height + 8);
    Bitmap *th = PersoPortrait(skin, max(8, (int)(big.Width * g_scale + 0.5f)), max(8, (int)(big.Height * g_scale + 0.5f)));
    if (!th) th = SkinThumb(skin, 1, big.Width, big.Height);
    GraphicsState st = g.Save();
    g.SetClip(&p, CombineModeIntersect);
    LinearGradientBrush bg(RectF(c.X, c.Y - 1, c.Width, c.Height + 2), Mix(TH(card), col, g_dark ? 0.55f : 0.40f), Mix(TH(card), col, g_dark ? 0.25f : 0.15f), LinearGradientModeVertical);
    g.FillPath(&bg, &p);
    if (th) DrawPixels(g, th, big);
    else Text(g, name.empty() ? L"?" : name.substr(0, 1), c, c.Height * 0.45f, FontStyleBold, Color(255, 255, 255, 255));
    g.Restore(st);
    Pen ring(col, 2.0f); g.DrawPath(&ring, &p);
}
// Libelle court d'un contenu envoye (chips de la carte de droite).
static std::wstring LobbyCatLabel(const std::string &key)
{
    if (key == "radio") return T(L"Radio", L"Radio");
    if (key == "peinture") return T(L"Peinture", L"Paint");
    if (key == "mobylette") return T(L"Mobylette", L"Moped");
    if (key == "drapeau") return T(L"Drapeau", L"Flag");
    if (key == "posters") return T(L"Posters", L"Posters");
    if (key == "autocollant") return T(L"Autocollant", L"Sticker");
    if (key == "tissu") return T(L"Tissu", L"Fabric");
    if (key == "photo") return T(L"Photo", L"Photo");
    if (key == "hockey") return T(L"Hockey", L"Hockey");
    for (int i = 0; i < kContentN; i++) if (key == kContent[i].key) return T(kContent[i].fr, kContent[i].en);
    return Widen(key);
}

// Libelle d'une option dans le salon (maquette du 17/10 : gras, petite ligne grise dessous pour quelques-unes) : plus court
// que celui des Options du jeu (colonne etroite), sinon le meme.
static void LobbyRowLabel(const LobbyOptDef &d, const GfxRow *r, std::wstring &lab, std::wstring &sub)
{
    static const struct { const char *key; const wchar_t *fr, *en, *sfr, *sen; } kShort[] = {
        { "Triches", L"Triches (F7)", L"Cheats (F7)", NULL, NULL },
        { "Renverser", L"Les voitures renversent les joueurs", L"Cars run players over", L"Amis et PNJ", L"Friends and NPCs" },
        { "PvP", L"Coups de poing entre joueurs", L"Punches between players", NULL, NULL },
        { "VenttiEnjeux", L"Ventti : enjeux pour tout le groupe", L"Ventti: stakes for the whole group", NULL, NULL },
        { "Jokkis", L"Jokkis à louer (course sur glace)", L"Jokkis rentals (ice race)", NULL, NULL },
        { "GainMicro", L"Micro", L"Mic", NULL, NULL },   // (mon micro, pour les autres ; court : les pastilles prennent la place)
        { "CarteMaintenir", L"Carte (M)", L"Map (M)", NULL, NULL },
    };
    sub.clear();
    for (auto &s : kShort) if (!strcmp(s.key, d.key)) { lab = T(s.fr, s.en); if (s.sfr) sub = T(s.sfr, s.sen); return; }
    lab = r ? T(r->fr, r->en) : Widen(d.key);
}
// Options a valeurs montrees en pastilles segmentees (zones UI_LOBBY_SEGV + rang, a la suite : 5 + 4 + 4 + 2 = 15 < 3300).
static const char *kLobbySegKeys[] = { "VocalPortee", "VolumeVocal", "GainMicro", "CarteMaintenir" };
static int LobbySegBase(const char *key)
{
    int base = UI_LOBBY_SEGV;
    for (const char *k : kLobbySegKeys) {
        const GfxRow *r = GfxRowByKey(k);
        if (!strcmp(k, key)) return base;
        base += r ? (int)r->vals.size() : 0;
    }
    return -1;
}
// Libelles courts des pastilles : « 15 / 25 / 35 m / 50 / 80 » (l'unite sur la valeur prise), « 50 / 75 / 100 % »,
// « Normal / +50 % / +100 % / +200 % », « Appuyer / Maintenir ».
static std::vector<std::wstring> LobbySegLabels(const char *key, const GfxRow *r, int sel)
{
    std::vector<std::wstring> out;
    for (int j = 0; j < (int)r->vals.size(); j++) {
        wchar_t b[24];
        if (!strcmp(key, "GainMicro")) { if (r->vals[j] <= 100) wcscpy_s(b, T(L"Normal", L"Normal")); else swprintf_s(b, L"+%d %%", r->vals[j] - 100); }
        else if (!strcmp(key, "VocalPortee")) swprintf_s(b, j == sel ? L"%d m" : L"%d", r->vals[j]);
        else if (!strcmp(key, "VolumeVocal")) swprintf_s(b, j == sel ? L"%d %%" : L"%d", r->vals[j]);
        else wcscpy_s(b, T(r->lf[j], r->le[j]));
        out.push_back(b);
    }
    return out;
}
// Texte coupe avec « … » s'il depasse la largeur.
static std::wstring FitText(const std::wstring &s, float px, int style, float maxW)
{
    Graphics &mg = MeasureG();
    if (MeasureW(mg, s, px, style) <= maxW) return s;
    std::wstring t = s;
    while (t.size() > 1 && MeasureW(mg, t + L"…", px, style) > maxW) t.pop_back();
    return t + L"…";
}
// Une option du salon : libelle (et sa petite ligne), interrupteur 42 x 24 ou pastilles segmentees ; lecture seule :
// Oui / Non ou la valeur.
static void DrawLobbyOptRow(Graphics &g, RectF row, int i, int val, bool edit, bool dim)
{
    const LobbyOptDef &d = kLobbyOptDefs[i];
    const GfxRow *r = GfxRowByKey(d.key);
    std::wstring lab, sub;
    LobbyRowLabel(d, r, lab, sub);
    bool isAjout = !strncmp(d.key, "Ajout", 5);   // (un ajout : en retrait sous « Ajouts MWCoop »)
    int id = UI_LOBBY_OPT + i * 3;
    float a = dim ? 0.45f : 1.0f, ind = isAjout ? 16.0f : 0.0f, cy = row.Y + row.Height / 2;
    bool hot = edit && g_uiHot == id, seg = r && !r->vals.empty();
    if (hot && !seg) { GraphicsPath hp; RoundRect(hp, RectF(row.X - 6, row.Y + 1, row.Width + 12, row.Height - 2), 9); SolidBrush hb(TH(card)); g.FillPath(&hb, &hp); }
    int k = 0;
    if (seg) for (int j = 0; j < (int)r->vals.size(); j++) if (r->vals[j] == val) k = j;
    std::vector<std::wstring> labs;
    float ctrlW = 42, segPad = 11;   // (marge de chaque pastille ; plus serree a quatre ou cinq valeurs larges)
    if (seg) {
        labs = LobbySegLabels(d.key, r, k);
        if (edit) { ctrlW = 6; for (auto &l : labs) ctrlW += MeasureW(MeasureG(), l, 10.5f, FontStyleBold) + segPad; }
        if (edit && ctrlW > row.Width - 70) { segPad = 8; ctrlW = 6; for (auto &l : labs) ctrlW += MeasureW(MeasureG(), l, 10.5f, FontStyleBold) + segPad; }
        if (!edit) ctrlW = PillW(labs[k], 11) + 4;
    } else if (!edit) ctrlW = 46;
    float textW = row.Width - ind - ctrlW - 10;
    Color ink = WithA(kInk, a);
    if (sub.empty()) Text(g, FitText(lab, 12.5f, FontStyleBold, textW), RectF(row.X + ind, row.Y, textW, row.Height), isAjout ? 12.0f : 12.5f, isAjout ? FontStyleRegular : FontStyleBold, ink, StringAlignmentNear);
    else {
        Text(g, FitText(lab, 12.5f, FontStyleBold, textW), RectF(row.X + ind, cy - 18, textW, 18), 12.5f, FontStyleBold, ink, StringAlignmentNear);
        Text(g, FitText(sub, 10.5f, FontStyleRegular, textW), RectF(row.X + ind, cy + 1, textW, 16), 10.5f, FontStyleRegular, WithA(kGrey, a), StringAlignmentNear);
    }
    if (!strcmp(d.key, "Jokkis")) {   // en essai : pastille BETA apres le libelle, si elle tient
        float tw = MeasureW(MeasureG(), lab, 12.5f, FontStyleBold);
        if (tw + 8 + 38 <= textW) Pill(g, RectF(row.X + tw + 8, cy - 8, 38, 16), Color(255, 255, 107, 139), T(L"BÊTA", L"BETA"), Color(255, 255, 255, 255), 8.5f);
    }
    if (!seg) {   // interrupteur (plus petit sur la ligne d'un ajout)
        float th = min(24.0f, row.Height - 2), tw = th == 24 ? 42.0f : 34.0f, ph = min(22.0f, row.Height - 2);
        if (edit) { UiToggle(g, RectF(row.X + row.Width - tw, cy - th / 2, tw, th), val == 1, hot); HotZone(row, id); }
        else Pill(g, RectF(row.X + row.Width - 46, cy - ph / 2, 46, ph), val == 1 ? WithA(kAcc, 0.22f) : TH(toggleOff), val == 1 ? T(L"Oui", L"Yes") : T(L"Non", L"No"), ink, 10.5f);
        return;
    }
    if (!edit) { Pill(g, RectF(row.X + row.Width - ctrlW, cy - 11, ctrlW, 22), WithA(kAcc, 0.22f), labs[k], ink, 11); return; }
    // pastilles segmentees (fond sombre, la valeur prise en accent), chacune sa zone
    RectF sr(row.X + row.Width - ctrlW, cy - 14, ctrlW, 28);
    int base = LobbySegBase(d.key);
    GraphicsPath tp; RoundRect(tp, sr, 9);
    SolidBrush tb(TH(field)); g.FillPath(&tb, &tp);
    float sx = sr.X + 3;
    for (int j = 0; j < (int)labs.size(); j++) {
        float sw = MeasureW(MeasureG(), labs[j], 10.5f, FontStyleBold) + segPad;
        RectF s(sx, sr.Y + 3, sw, sr.Height - 6);
        bool on = j == k, shot = base >= 0 && g_uiHot == base + j;
        GraphicsPath p; RoundRect(p, s, 7);
        if (on) { SolidBrush b(kAcc); g.FillPath(&b, &p); }
        else if (shot) { SolidBrush b(TH(cardSel)); g.FillPath(&b, &p); }
        Text(g, labs[j], s, 10.5f, FontStyleBold, on ? kOnAcc : kInk);
        if (base >= 0) HotZone(s, base + j);
        sx += sw;
    }
}
// Carte Options : onglets La partie (hote : decidees par lui, envoyees aux invites ; invite : De l'hote, lecture seule) et
// Les miennes (chacun pour soi). Tout vaut pour ce salon seulement (lancement.ini). La partie : captions REGLES puis
// JEUX & AJOUTS ; les quatre ajouts tiennent en une ligne Tous / Choisir / Aucun ; joueurs au plus en 4 / 8 / 12 / 16.
// Les miennes : VOIX puis AFFICHAGE ; chez l'invite, dessous, le resume « Decide par l'hote ».
static void DrawLobbyOptCard(Graphics &g, RectF c, bool host, int which)
{
    Glass(c, 20);
    const float x = c.X + 16, w = c.Width - 32, right = c.X + c.Width - 16, bottom = c.Y + c.Height - 16;
    bool go = g_goSent;
    Title(g, T(L"Options", L"Options"), RectF(x, c.Y + 14, 110, 24), 16, kInk);
    UiSeg(g, RectF(right - 176, c.Y + 11, 176, 30), { host ? T(L"La partie", L"The game") : T(L"De l'hôte", L"Host's"), T(L"Les miennes", L"Mine") }, which, UI_LOBBY_OPTTAB, 11.5f);
    const wchar_t *help = which == 1 ? T(L"Pour ce salon seulement. Réglages > Co-op reste inchangé.", L"For this lobby only. Settings > Co-op stays unchanged.")
                        : host ? T(L"Décidées par toi, pour tous. Reprises de Réglages > Co-op, pour ce salon seulement.", L"Decided by you, for everyone. Taken from Settings > Co-op, for this lobby only.")
                               : T(L"Choisies par l'hôte pour cette partie ; tu les reçois en direct.", L"Chosen by the host for this game; you get them live.");
    Para(g, help, RectF(x, c.Y + 44, w, 30), 10.5f, kGrey);
    std::map<std::string, int> ho;
    if (!host) { EnterCriticalSection(&g_lcs); ho = g_hostOpt; LeaveCriticalSection(&g_lcs); }
    bool edit = (host || which == 1) && !go;
    auto hostVal = [&](const char *key) {   // (option de la partie : la mienne chez l'hote, celle recue chez l'invite)
        if (host) return LobbyOptGet(key);
        auto it = ho.find(key);
        if (it != ho.end()) return it->second;
        for (auto &d : kLobbyOptDefs) if (!strcmp(d.key, key)) return d.def;
        return 0;
    };
    auto val = [&](const char *key) { return host || which == 1 ? LobbyOptGet(key) : hostVal(key); };
    float y = c.Y + 76;
    const float rh = which == 1 ? 32.0f : 34.0f, rh2 = which == 1 ? 42.0f : 44.0f, sh = 20;   // ligne, ligne a deux textes, ligne d'un ajout (les miennes : un peu plus serre, le resume de l'hote dessous)
    Pen sp(TH(sep), 1);
    auto caption = [&](const wchar_t *t, bool lock) { SmallLabel(g, t, RectF(x, y, w - 20, 16), kAcc); if (lock) DrawLock(g, right - 7, y + 8, kGrey); y += 18; };
    auto row = [&](const char *key, bool dim) {
        int i = LobbyOptIdx(key);
        if (i < 0) return;
        std::wstring lab, sub;
        LobbyRowLabel(kLobbyOptDefs[i], GfxRowByKey(key), lab, sub);
        float h = !strncmp(key, "Ajout", 5) ? sh : sub.empty() ? rh : rh2;
        DrawLobbyOptRow(g, RectF(x, y, w, h), i, val(key), edit, dim);
        y += h;
        g.DrawLine(&sp, x, y - 0.5f, right, y - 0.5f);
    };
    if (which == 0) {
        caption(T(L"RÈGLES", L"RULES"), !host);
        for (const char *k : { "ArgentDepart", "Triches", "Renverser", "PvP" }) row(k, false);
        y += 6;
        caption(T(L"JEUX & AJOUTS", L"GAMES & ADDITIONS"), false);
        row("VenttiEnjeux", false);   // (Jokkis a louer retirees le 18/10 : la VAZ les remplace)
        // ajouts MWCoop : Tous / Choisir / Aucun (SansAjouts et les quatre AjoutX)
        int on = 0;
        for (const char *k : kAjoutKeys) on += val(k) == 1;
        int mode = val("SansAjouts") ? 2 : (on == kAjoutN && !(edit && g_lobbyAjoutsPick)) ? 0 : 1;
        bool pick = mode == 1;   // (les quatre ajouts listes dessous : les petites lignes deviennent inutiles, la place aussi)
        Text(g, T(L"Ajouts MWCoop", L"MWCoop additions"), RectF(x, y + (pick ? 0 : 5), w - 170, pick ? rh : 20), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
        if (edit) UiSeg(g, RectF(right - 166, y + (pick ? 2 : 4), 166, 28), { T(L"Tous", L"All"), T(L"Choisir", L"Pick"), T(L"Aucun", L"None") }, mode, UI_LOBBY_AJOUTS, 11);
        else {
            wchar_t b[24];
            swprintf_s(b, L"%d / %d", on, kAjoutN);
            Pill(g, RectF(right - 60, y + 7, 60, 22), mode == 2 ? TH(toggleOff) : WithA(kAcc, 0.22f), mode == 0 ? T(L"Tous", L"All") : mode == 2 ? T(L"Aucun", L"None") : b, kInk, 10.5f);
        }
        if (!pick) Text(g, g_game == GAME_MSC ? T(L"Caméra drone (F8)", L"Drone camera (F8)") : T(L"Drone, sacs de couchage, boutique s21, musée, VAZ-2108", L"Drone, sleeping bags, s21 shop, museum, VAZ-2108"), RectF(x, y + 25, w, 16), 10.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        y += pick ? rh : rh2;
        g.DrawLine(&sp, x, y - 0.5f, right, y - 0.5f);
        if (mode == 1) for (const char *k : kAjoutKeys) row(k, false);
        // joueurs au plus (hote compris) : 4 / 8 / 12 / 16, avertissement au-dela de 10
        static const int opts[4] = { 4, 8, 12, 16 };
        int sel = -1;
        for (int k = 0; k < 4; k++) if (g_lobbyMax == opts[k]) sel = k;
        float my = pick ? 0.0f : 5.0f;   // (compact : une seule ligne)
        Text(g, T(L"Joueurs au plus", L"Players max"), RectF(x, y + my, w - 160, pick ? rh : 20), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
        wchar_t sb[120];
        if (g_lobbyMax > 10) swprintf_s(sb, L"%s", T(L"Au-delà de 10 : très bonne connexion et bonne machine chez l'hôte", L"Above 10: a very good connection and a good machine on the host's side"));
        else if (sel < 0) swprintf_s(sb, T(L"Hôte compris · %d pour ce salon", L"Host included · %d for this lobby"), g_lobbyMax);
        else swprintf_s(sb, L"%s", T(L"Hôte compris · au-delà de 10, bonne connexion conseillée", L"Host included · above 10, a good connection is advised"));
        if (!pick) Text(g, sb, RectF(x, y + 25, w, 16), 10.5f, FontStyleRegular, g_lobbyMax > 10 ? kWarn : kGrey, StringAlignmentNear);
        if (edit) UiSeg(g, RectF(right - 152, y + (pick ? 3 : 4), 152, 28), { L"4", L"8", L"12", L"16" }, sel, UI_LOBBY_MAXSEG, 11.5f);
        else Pill(g, RectF(right - 46, y + (pick ? 6 : 7), 46, 22), WithA(kAcc, 0.22f), std::to_wstring(g_lobbyMax), kInk, 10.5f);
        y += pick ? rh : rh2;
        if (!host && ho.empty()) Text(g, T(L"Pas encore reçues de l'hôte", L"Not received from the host yet"), RectF(x, y + 4, w, 18), 11, FontStyleItalic, kGrey, StringAlignmentNear);
    } else {
        caption(T(L"VOIX", L"VOICE"), false);
        for (const char *k : { "Vocal", "VocalPortee", "VolumeVocal", "GainMicro" }) row(k, false);
        y += 6;
        caption(T(L"AFFICHAGE", L"DISPLAY"), false);
        for (const char *k : { "VoirVisages", "Marqueurs", "CarteMaintenir" }) row(k, false);
        // (ModsAuto : chez l'invite, « Tout accepter de cet hote », carte de droite)
        if (!host) {   // resume de ce que l'hote a decide (lecture seule, deux colonnes)
            y += 8;
            RectF b(x, y, w, 8 + 16 + 3 * 26 + 6);
            GraphicsPath bp; RoundRect(bp, b, 12);
            SolidBrush bb(TH(card)); g.FillPath(&bb, &bp);
            Pen pn(TH(choiceBorder), 1.0f); g.DrawPath(&pn, &bp);
            SmallLabel(g, T(L"DÉCIDÉ PAR L'HÔTE", L"DECIDED BY THE HOST"), RectF(b.X + 10, b.Y + 7, b.Width - 20, 16), kAcc);
            int onA = 0;
            for (const char *k : kAjoutKeys) onA += hostVal(k) == 1;
            wchar_t ab[16];
            swprintf_s(ab, L"%d / %d", onA, kAjoutN);
            struct Ro { const wchar_t *lab; std::wstring v; bool on; } ro[6] = {
                { T(L"Argent départ", L"Start money"), hostVal("ArgentDepart") ? T(L"Oui", L"Yes") : T(L"Non", L"No"), hostVal("ArgentDepart") == 1 },
                { T(L"Triches", L"Cheats"), hostVal("Triches") ? T(L"Oui", L"Yes") : T(L"Non", L"No"), hostVal("Triches") == 1 },
                { T(L"Renverser", L"Run over"), hostVal("Renverser") ? T(L"Oui", L"Yes") : T(L"Non", L"No"), hostVal("Renverser") == 1 },
                { T(L"Coups de poing", L"Punches"), hostVal("PvP") ? T(L"Oui", L"Yes") : T(L"Non", L"No"), hostVal("PvP") == 1 },
                { T(L"Ventti partagé", L"Shared Ventti"), hostVal("VenttiEnjeux") ? T(L"Oui", L"Yes") : T(L"Non", L"No"), hostVal("VenttiEnjeux") == 1 },
                { T(L"Ajouts", L"Additions"), hostVal("SansAjouts") ? T(L"Aucun", L"None") : onA == kAjoutN ? T(L"Tous", L"All") : ab, !hostVal("SansAjouts") && onA > 0 },
            };
            float colW = (b.Width - 20 - 10) / 2;
            for (int k = 0; k < 6; k++) {
                RectF rr(b.X + 10 + (k % 2) * (colW + 10), b.Y + 24 + (k / 2) * 26, colW, 26);
                float vw = max(36.0f, PillW(ro[k].v, 10) - 6);
                Text(g, FitText(ro[k].lab, 10.5f, FontStyleBold, rr.Width - vw - 6), RectF(rr.X, rr.Y, rr.Width - vw - 4, rr.Height), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
                GraphicsPath vp; RoundRect(vp, RectF(rr.X + rr.Width - vw, rr.Y + 3, vw, 20), 7);
                SolidBrush vb(ro[k].on ? WithA(kAcc, 0.22f) : TH(toggleOff)); g.FillPath(&vb, &vp);
                Text(g, ro[k].v, RectF(rr.X + rr.Width - vw, rr.Y + 3, vw, 20), 10, FontStyleBold, kInk);
                if (k < 4) g.DrawLine(&sp, rr.X, rr.Y + rr.Height - 0.5f, rr.X + rr.Width, rr.Y + rr.Height - 0.5f);
            }
            y += b.Height;
        }
    }
    if (host || which == 1) {   // remettre les valeurs des Reglages ; les garder comme defauts (mwcoop.ini)
        float bw = (w - 8) / 2;
        UiButton(g, RectF(x, bottom - 32, bw, 32), T(L"Remettre mes Réglages", L"Reset to my Settings"), UI_LOBBY_OPTRESET, false, -1, edit, 10);
        UiButton(g, RectF(right - bw, bottom - 32, bw, 32), T(L"Garder comme défauts", L"Keep as defaults"), UI_LOBBY_OPTKEEP, false, -1, edit, 10);
    }
}

// Bandeau : hote = ticket du code de session (Public / Prive, COPIER, nouveau code) ; invite = qui heberge, quelle partie,
// depuis combien de temps il est dans ce salon.
static void DrawLobbyBanner(Graphics &g, RectF r, bool host, const std::vector<LobbyPeer> &peers, int partie)
{
    Glass(r, 20);
    Color tint = host ? kAcc : kOk;
    {   // teinte bleu glace (ou menthe chez l'invite), plus forte a gauche
        GraphicsPath p; RoundRect(p, r, 20);
        LinearGradientBrush lg(RectF(r.X - 1, r.Y, r.Width + 2, r.Height), WithA(tint, 0.16f), WithA(tint, 0.03f), LinearGradientModeHorizontal);
        g.FillPath(&lg, &p);
    }
    const float x = r.X + 20, right = r.X + r.Width - 18;
    if (!host) {
        const LobbyPeer *hp = NULL;
        for (auto &p : peers) if (p.id == 0) hp = &p;
        std::wstring hn = hp ? Widen(hp->name, CP_UTF8) : g_sHostName;
        if (hp) DrawLobbyPortrait(g, RectF(x, r.Y + 26, 56, 56), hp->skin, CarRgb(kPeerPal[0]), hn);
        float tx = hp ? x + 70 : x, tw = right - 130 - tx;
        wchar_t b[160];
        swprintf_s(b, T(L"%s HÉBERGE", L"%s IS HOSTING"), hn.empty() ? L"?" : hn.c_str());
        SmallLabel(g, b, RectF(tx, r.Y + 14, tw, 16), kOk);
        Text(g, FitText(partie == PARTIE_NOUVELLE ? T(L"Nouvelle partie · tu la rejoins dès qu'il est en jeu", L"New game · you join as soon as the host is in game")
                                                  : T(L"Il continue sa partie · tu la rejoins dès qu'il est en jeu", L"Continuing their game · you join as soon as the host is in game"), 15, FontStyleBold, tw),
             RectF(tx, r.Y + 32, tw, 26), 15, FontStyleBold, kInk, StringAlignmentNear);
        Para(g, T(L"Sa sauvegarde, ses mods et ses fichiers arrivent chez toi tout seuls. Rien à régler, sauf tes options.", L"Their save, mods and files reach you by themselves. Nothing to set up, except your own options."),
             RectF(tx, r.Y + 60, tw, 32), 10.5f, kGrey);
        Text(g, T(L"Dans ce salon depuis", L"In this lobby for"), RectF(right - 130, r.Y + 30, 130, 16), 11, FontStyleRegular, kGrey, StringAlignmentFar);
        DWORD s = (GetTickCount() - g_lobbySinceT) / 1000;
        swprintf_s(b, L"%02lu:%02lu", (unsigned long)(s / 60), (unsigned long)(s % 60));
        TextF(g, TitleFont(), b, RectF(right - 130, r.Y + 48, 130, 30), 22, FontStyleBold, kInk, StringAlignmentFar);
        return;
    }
    bool go = g_goSent;
    SmallLabel(g, T(L"CODE DE SESSION", L"SESSION CODE"), RectF(x, r.Y + 12, 300, 16), kAcc);
    // a droite : Public / Prive, COPIER, nouveau code
    float bx = right;
    if (g_lobbyPrive) {
        RectF rg(bx - 52, r.Y + 28, 52, 52);
        bx -= 60;
        { GraphicsPath p; RoundRect(p, rg, 14); SolidBrush b(g_uiHot == UI_LOBBY_REGEN && !go ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p); Pen pen(WithA(TH(fieldBorder), go ? 0.4f : 1.0f), 1); g.DrawPath(&pen, &p); }
        Icon(g, IC_UNDO, rg.X + 26, rg.Y + 26, 18, WithA(kInk, go ? 0.4f : 1.0f), 2.1f);
        if (!go) HotZone(rg, UI_LOBBY_REGEN);
        bool copied = GetTickCount() - g_codeCopiedT < 2000;
        RectF cp(bx - 108, r.Y + 28, 108, 52);
        bx -= 118;
        UiButton(g, cp, copied ? T(L"COPIÉ ✓", L"COPIED ✓") : T(L"COPIER", L"COPY"), UI_LOBBY_COPY, true, -1, !copied && !go, 14);
    }
    if (!go) UiSeg(g, RectF(bx - 150, r.Y + 39, 150, 30), { T(L"Public", L"Public"), T(L"Privé", L"Private") }, g_lobbyPrive ? 1 : 0, UI_LOBBY_PRIV, 11.5f);
    else Pill(g, RectF(bx - 90, r.Y + 43, 90, 22), WithA(kAcc, 0.22f), g_lobbyPrive ? T(L"PRIVÉ", L"PRIVATE") : T(L"PUBLIC", L"PUBLIC"), kInk);
    float tw = bx - 160 - x;
    if (g_lobbyPrive) {   // le code en grand, lettres espacees, halo bleu glace
        float cx = x - 2;
        for (char ch : g_lobbyCode) {
            std::wstring s(1, (wchar_t)ch);
            RectF cr(cx, r.Y + 26, 38, 46);
            for (int k = 0; k < 4; k++) { RectF gr = cr; gr.Offset(k & 1 ? 1.5f : -1.5f, k & 2 ? 1.5f : -1.5f); TextF(g, TitleFont(), s, gr, 40, FontStyleBold, WithA(kAcc, 0.35f)); }
            TextF(g, TitleFont(), s, cr, 40, FontStyleBold, g_dark ? Color(255, 255, 255, 255) : kInk);
            cx += 38;
        }
        std::wstring sub = g_lobbySteam ? T(L"Privé : tes amis entrent avec ce code, ou par ton invitation Steam.", L"Private: your friends join with this code, or through your Steam invite.")
                                        : std::wstring(T(L"Privé : tes amis entrent avec ton adresse", L"Private: your friends join with your address")) + (g_myAddresses.empty() ? L"" : L" (" + g_myAddresses + L")") + T(L" et ce code.", L" and this code.");
        Para(g, sub, RectF(x, r.Y + 74, tw, 30), 10.5f, kGrey);
    } else {
        Text(g, T(L"Pas de code : tout le monde peut entrer", L"No code: anyone can join"), RectF(x, r.Y + 30, tw, 32), 17, FontStyleBold, kInk, StringAlignmentNear);
        std::wstring sub = g_lobbySteam ? T(L"Par Steam, tout ami peut entrer (« Rejoindre la partie »).", L"Through Steam, any friend can join (\"Join game\").")
                                        : std::wstring(T(L"Par IP, ton adresse suffit", L"By IP, your address is enough")) + (g_myAddresses.empty() ? L"" : L" (" + g_myAddresses + L")") + L".";
        Para(g, sub, RectF(x, r.Y + 66, tw, 30), 10.5f, kGrey);
    }
}

// Carte Joueurs : titre et compte, INVITER (hote Steam) ou « n prets », aide, les joueurs (portrait, nom, tenue · ping ·
// UDP ou envoi en cours, etat ; ma ligne teintee avec le bouton de ma tenue ; « Arrive… » pour un invite dont le test UDP
// n'a pas encore repondu), puis le tchat : les trois derniers messages, « Message au salon » et ENVOYER.
static void DrawLobbyPlayersCard(Graphics &g, RectF c, const std::vector<LobbyPeer> &peers, int lobby, int myUdp)
{
    bool host = lobby == LB_HOST, canKick = host && !g_lobbySteam && !g_goSent;
    Glass(c, 20);
    const float x = c.X + 16, w = c.Width - 32, right = c.X + c.Width - 16, bottom = c.Y + c.Height - 16;
    Title(g, T(L"Joueurs", L"Players"), RectF(x, c.Y + 14, 90, 24), 16, kInk);
    {
        wchar_t b[32];
        swprintf_s(b, L"· %d / %d", (int)peers.size(), g_lobbyMax);
        Text(g, b, RectF(x + MeasureW(MeasureG(), T(L"Joueurs", L"Players"), 16, FontStyleBold) + 6, c.Y + 14, 100, 24), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    if (host && g_lobbySteam && !g_goSent) UiButton(g, RectF(right - 100, c.Y + 11, 100, 30), T(L"Inviter", L"Invite"), UI_LOBBY_INVITE, true, IC_PLUS, true, 10);
    else {
        int guests = 0, notReady = 0;
        for (auto &p : peers) if (p.id != 0) { guests++; if (!p.ready) notReady++; }
        wchar_t b[32];
        swprintf_s(b, T(L"%d prêt(s)", L"%d ready"), guests - notReady);
        float cw = ChipW(b);
        Chip(g, RectF(right - cw, c.Y + 14, cw, 24), b, guests && !notReady ? kOk : kInk, false);
    }
    std::wstring help = canKick ? T(L"Toi : tenue, voiture · un autre : le mettre dehors.", L"You: outfit, car · another one: kick them out.") : T(L"Clique sur toi : tenue, voiture.", L"Click yourself: outfit, car.");
    Color hc = kGrey;
    if (lobby == LB_GUEST) {   // test UDP de l'invite
        if (myUdp == UDP_OK) { help = T(L"UDP : OK ✓ · clique sur toi : tenue, voiture.", L"UDP: OK ✓ · click yourself: outfit, car."); hc = kOk; }
        else if (myUdp == UDP_FAIL && !g_hostUdpTest) help = T(L"UDP non vérifié (lanceur de l'hôte ancien)", L"UDP not checked (host has an old launcher)");
        else if (myUdp == UDP_FAIL) { help = T(L"UDP : bloqué (voir à droite)", L"UDP: blocked (see on the right)"); hc = kRed; }
        else if (myUdp == UDP_WAIT) help = T(L"UDP : test en cours…", L"UDP: testing…");
    }
    Text(g, FitText(help, 10.5f, FontStyleRegular, w), RectF(x, c.Y + 42, w, 16), 10.5f, FontStyleRegular, hc, StringAlignmentNear);
    // tchat, en bas : trois lignes, le libelle, le champ et ENVOYER
    const float inH = 42, msgH = 18;
    float sendW = PillW(T(L"ENVOYER", L"SEND"), 12) + 12;
    RectF inR(x, bottom - inH, w - sendW - 8, inH), sendR(right - sendW, bottom - inH, sendW, inH);
    float msgTop = inR.Y - 22 - 3 * msgH - 6;
    Text(g, T(L"Message au salon", L"Message to the lobby"), RectF(x, inR.Y - 20, w, 16), 11, FontStyleRegular, kGrey, StringAlignmentNear);
    {
        std::vector<ChatMsg> chat;
        EnterCriticalSection(&g_lcs);
        size_t n0 = g_chat.size() > 3 ? g_chat.size() - 3 : 0;
        chat.assign(g_chat.begin() + n0, g_chat.end());
        LeaveCriticalSection(&g_lcs);
        if (chat.empty()) Text(g, T(L"Pas encore de message. Dis bonjour !", L"No message yet. Say hello!"), RectF(x, msgTop + 2 * msgH, w, msgH), 11, FontStyleItalic, TH(placeholder), StringAlignmentNear);
        for (size_t i = 0; i < chat.size(); i++) {
            float my = msgTop + (3 - chat.size() + i) * msgH;
            std::wstring an = Widen(chat[i].name, CP_UTF8) + L"  ";
            float aw = MeasureW(MeasureG(), an, 11.5f, FontStyleBold);
            Text(g, an, RectF(x, my, aw + 4, msgH), 11.5f, FontStyleBold, CarRgb(kPeerPal[chat[i].id & 15]), StringAlignmentNear);
            Text(g, FitText(chat[i].text, 11.5f, FontStyleRegular, w - aw - 4), RectF(x + aw, my, w - aw, msgH), 11.5f, FontStyleRegular, kInk, StringAlignmentNear);
        }
    }
    bool chatOn = lobby == LB_HOST || lobby == LB_GUEST;
    {   // champ
        GraphicsPath p; RoundRect(p, inR, 12);
        SolidBrush fill(TH(field)); g.FillPath(&fill, &p);
        Pen pen(g_chatFocus ? kAcc : TH(fieldBorder), g_chatFocus ? 2.0f : 1.2f); g.DrawPath(&pen, &p);
        RectF tr(inR.X + 14, inR.Y, inR.Width - 28, inR.Height);
        if (g_chatText.empty() && !g_chatFocus) Text(g, host ? T(L"On part dans 2 min…", L"Leaving in 2 min…") : T(L"J'arrive !", L"Coming!"), tr, 13, FontStyleRegular, TH(placeholder), StringAlignmentNear);
        else {
            float tw = MeasureW(MeasureG(), g_chatText, 13.5f, FontStyleBold);
            std::wstring shown = g_chatText;
            while (tw > tr.Width && shown.size() > 1) { shown.erase(0, 1); tw = MeasureW(MeasureG(), shown, 13.5f, FontStyleBold); }   // (la fin reste visible)
            Text(g, shown, tr, 13.5f, FontStyleBold, kInk, StringAlignmentNear);
            if (g_chatFocus && fmodf(g_time, 1.0f) < 0.55f) { float cx = min(tr.X + tw + 1, tr.X + tr.Width); Pen cp(kAcc, 1.6f); g.DrawLine(&cp, cx, inR.Y + 11, cx, inR.Y + inR.Height - 11); }
        }
        if (chatOn) HotZone(inR, UI_LOBBY_CHAT);
        UiButton(g, sendR, T(L"ENVOYER", L"SEND"), UI_LOBBY_SEND, false, -1, chatOn, 11);
    }
    {   // la liste : des lignes entieres (le reste de la place va au tchat)
        float avail = msgTop - 10 - (c.Y + 64);
        kLobbyList = RectF(x, c.Y + 64, w, max(1.0f, floorf(avail / kLobbyRowH)) * kLobbyRowH - 8);
    }
    float sc = g_scroll[TAB_LOBBY] = min(g_scroll[TAB_LOBBY], LobbyMaxScroll()), ms = LobbyMaxScroll();
    int rows = (int)peers.size() + ((int)peers.size() < g_lobbyMax ? 1 : 0);
    DWORD now = GetTickCount();
    GraphicsState st = g.Save();
    g.SetClip(kLobbyList, CombineModeIntersect);
    for (int i = 0; i < rows; i++) {
        RectF r(kLobbyList.X, kLobbyList.Y + i * kLobbyRowH - sc, kLobbyList.Width - (ms > 0 ? 10 : 0), kLobbyRowH - 8);
        if (r.Y + r.Height < kLobbyList.Y || r.Y > kLobbyList.Y + kLobbyList.Height) continue;
        GraphicsPath rp;
        RoundRect(rp, r, 16);
        if (i >= (int)peers.size()) {   // place libre (salon Steam, hote : inviter des amis)
            bool inv = host && g_lobbySteam, hot = inv && g_lobbyHot == 51;
            if (hot) { SolidBrush hb(TH(card)); g.FillPath(&hb, &rp); }
            Pen dash(inv ? WithA(kAcc, hot ? 1.0f : 0.7f) : TH(fieldBorder), 1.4f);
            dash.SetDashStyle(DashStyleDash);
            g.DrawPath(&dash, &rp);
            if (inv) { Icon(g, IC_PLUS, r.X + 24, r.Y + r.Height / 2, 18, kAcc, 2.2f); Text(g, T(L"Inviter des amis Steam", L"Invite Steam friends"), RectF(r.X + 40, r.Y, r.Width - 50, r.Height), 13, FontStyleBold, kAcc, StringAlignmentNear); }
            else Text(g, T(L"En attente d'un joueur…", L"Waiting for a player…"), r, 13, FontStyleRegular, kGrey);
            continue;
        }
        const LobbyPeer &p = peers[i];
        bool me = p.id == g_myId, dlp = PeerSyncPending(p);
        bool arriving = !me && p.id != 0 && !g_lobbySteam && p.udp == UDP_WAIT && !dlp;   // (connecte en TCP, sa sonde UDP pas encore vue)
        bool kickHot = canKick && !me && p.id != 0 && g_lobbyHot == 200 + i, armed = canKick && g_kickArm == p.id && now - g_kickArmT < 4000;
        float a = arriving ? 0.75f : 1.0f;
        SolidBrush rb(me ? WithA(kAcc, 0.12f) : kickHot ? TH(cardSel) : TH(card));
        g.FillPath(&rb, &rp);
        Pen rpen(me ? WithA(kAcc, 0.6f) : armed ? WithA(kRed, 0.8f) : TH(choiceBorder), me || armed ? 1.4f : 1.0f);
        g.DrawPath(&rpen, &rp);
        std::wstring nm = Widen(p.name, CP_UTF8);
        Color pc = CarRgb(kPeerPal[p.id % 16]);
        RectF av(r.X + 10, r.Y + 8, 48, 48);
        if (arriving) {   // tuile en pointilles de sa couleur, silhouette
            GraphicsPath ap; RoundRect(ap, av, 14);
            SolidBrush ab(WithA(kInk, 0.08f)); g.FillPath(&ab, &ap);
            Pen dash(pc, 2.0f); dash.SetDashStyle(DashStyleDash); g.DrawPath(&dash, &ap);
            Icon(g, IC_USER, av.X + 24, av.Y + 24, 22, pc, 2.0f);
        } else DrawLobbyPortrait(g, av, p.skin, pc, nm);
        if (p.sid) DrawSteamAvatar(g, RectF(r.X + 44, r.Y + 42, 18, 18), p.sid);   // (avatar Steam en coin)
        // etat : Hote, Pret, Attend (recoit l'envoi de l'hote : LANCER attend), Pas pret ; deux clics : Dehors ?
        std::wstring stl = armed ? T(L"Dehors ?", L"Kick?") : p.id == 0 ? T(L"Hôte", L"Host") : dlp ? T(L"Attend", L"Waiting") : p.ready ? T(L"Prêt", L"Ready") : T(L"Pas prêt", L"Not ready");
        float pw = max(56.0f, ChipW(stl));
        RectF pr(r.X + r.Width - 10 - pw, r.Y + 8, pw, 24);   // (en haut a droite : la ligne de detail prend toute la largeur)
        Chip(g, pr, stl, armed ? kRed : p.id == 0 || p.ready ? kOk : dlp ? kWarn : kInk, !armed && p.id != 0 && !dlp && !p.ready);
        float tx = r.X + 68, tr = pr.X - 8;
        if (me) {   // ma tenue (toute la ligne mene a TENUE)
            HotZone(r, UI_LOBBY_ME_LOOK);
            RectF br(pr.X - 6 - 30, r.Y + 7, 30, 26);
            GraphicsPath bp; RoundRect(bp, br, 8);
            SolidBrush bb(g_uiHot == UI_LOBBY_ME_LOOK ? TH(btn2Hot) : TH(btn2)); g.FillPath(&bb, &bp);
            Pen bpen(TH(fieldBorder), 1); g.DrawPath(&bpen, &bp);
            Icon(g, IC_SHIRT, br.X + 15, br.Y + 13, 15, kInk, 1.9f);
            HotZone(br, UI_LOBBY_ME_LOOK);
            tr = br.X - 6;
        }
        {
            std::wstring you = me ? T(L" (toi)", L" (you)") : L"";
            float nw = MeasureW(MeasureG(), nm, 14, FontStyleBold);
            std::wstring shown = FitText(nm, 14, FontStyleBold, tr - tx - (you.empty() ? 0 : MeasureW(MeasureG(), you, 12, FontStyleRegular) + 2));
            Text(g, shown, RectF(tx, r.Y + 10, tr - tx, 20), 14, FontStyleBold, WithA(kInk, a), StringAlignmentNear);
            if (me) Text(g, you, RectF(tx + min(nw, tr - tx) + 2, r.Y + 11, 60, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        }
        // detail : tenue · version (hote) ; tenue · ping · UDP (invite par IP) ; tenue · Steam ; telecharge les mods ; Arrive…
        std::wstring line = SkinLabel(p.skin);
        Color lc = kGrey;
        wchar_t pb[64];
        if (p.id == 0) line += L" · " + (p.ver == "dev" ? std::wstring(L"dev") : L"v" + Widen(p.ver, CP_UTF8));
        else if (dlp) { swprintf_s(pb, T(L" · télécharge les mods %d %%", L" · downloading the mods %d%%"), max(0, p.dl)); line += pb; lc = kWarn; }
        else if (g_lobbySteam) line += L" · Steam";
        else if (arriving) { swprintf_s(pb, T(L"Arrive… %d ms · TCP seulement", L"Arriving… %d ms · TCP only"), p.ping); line = pb; }
        else {
            swprintf_s(pb, L" · %d ms", p.ping);
            line += pb;
            int udp = me && myUdp == UDP_OK ? UDP_OK : p.udp;   // (l'invite local : sa propre sonde compte)
            if (me && udp != UDP_OK && myUdp == UDP_FAIL && g_hostUdpTest) udp = UDP_FAIL;
            if (udp == UDP_OK) line += L" · UDP ✓";
            else if (udp == UDP_FAIL) { line += T(L" · UDP bloqué", L" · UDP blocked"); lc = kWarn; }
            else if (udp == UDP_WAIT) line += L" · UDP …";
        }
        Text(g, FitText(line, 11, FontStyleRegular, r.X + r.Width - 10 - tx), RectF(tx, r.Y + 34, r.X + r.Width - 10 - tx, 18), 11, FontStyleRegular, WithA(lc, a), StringAlignmentNear);
    }
    g.Restore(st);
    if (ms > 0) {
        float h = kLobbyList.Height * kLobbyList.Height / (kLobbyList.Height + ms), y = kLobbyList.Y + (kLobbyList.Height - h) * sc / ms;
        GraphicsPath sp; RoundRect(sp, RectF(kLobbyList.X + kLobbyList.Width - 5, y, 4, h), 2);
        SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
    }
}

// Invite : ou en est l'envoi de l'hote, mods et contenu separement (fichiers "Mods\..." ou non ; g_syncGotFile par fichier).
struct SyncParts { int modsN = 0, modsDone = 0; uint64_t modsB = 0, modsGot = 0, contB = 0, contGot = 0; std::wstring cur; int curPct = 0; std::vector<std::string> cats; };
static SyncParts SyncPartsNow(int s)
{
    SyncParts sp;
    std::lock_guard<std::mutex> lk(g_syncMx);
    sp.cats = g_hostOffer.cats;
    for (int i = 0; i < (int)g_hostOffer.files.size(); i++) {
        const SyncFile &f = g_hostOffer.files[i];
        auto it = g_syncGotFile.find(i);
        uint32_t got = s == SY_DONE ? f.size : it != g_syncGotFile.end() ? min(it->second, f.size) : 0;
        if (s == SY_GET && std::find(g_syncNeed.begin(), g_syncNeed.end(), i) == g_syncNeed.end()) got = f.size;   // (deja la : rien a recevoir)
        bool mod = !f.path.compare(0, 5, "Mods\\");
        if (mod) {
            sp.modsN++; sp.modsB += f.size; sp.modsGot += got;
            if (got >= f.size) sp.modsDone++;
            else if (sp.cur.empty()) { std::string n = f.path.substr(5); size_t dot = n.rfind('.'); if (dot != std::string::npos) n.resize(dot); sp.cur = Widen(n, CP_UTF8); sp.curPct = f.size ? (int)(got * 100ULL / f.size) : 0; }
        } else { sp.contB += f.size; sp.contGot += got; }
    }
    return sp;
}

// Carte de droite : hote = Partie (sauvegarde, Continuer / Nouvelle), Envoye aux invites (chips, taille, Choisir), Mods de la
// partie (etat chez les invites, Gerer) ; invite = Envoi de l'hote (avancement, Tout accepter de cet hote, TOUT ACCEPTER /
// REESSAYER), Partie choisie par l'hote ; puis le conseil ou l'avertissement UDP, LANCER / PRET et FERMER / QUITTER.
static void DrawLobbyRightCard(Graphics &g, RectF c, const std::vector<LobbyPeer> &peers, int lobby, int partie, const std::wstring &warn)
{
    bool host = lobby == LB_HOST, go = g_goSent;
    Glass(c, 20);
    const float x = c.X + 18, w = c.Width - 36, right = c.X + c.Width - 18;
    float y = c.Y + 14;
    auto gap = [&]() { y += 14; };   // (entre deux blocs : pas de trait, de l'air, comme la maquette)
    auto choiceCards = [&]() {
        const wchar_t *lab[2] = { host ? T(L"Continuer ma partie", L"Continue my game") : T(L"Continuer la partie", L"Continue the game"), T(L"Nouvelle partie", L"New game") };
        const wchar_t *sub[2] = { host ? T(L"Ta sauvegarde, envoyée aux invités", L"Your save, sent to the guests") : T(L"La sauvegarde de l'hôte", L"The host's save"),
                                  host ? T(L"Nouvelle sauvegarde, ou en recommencer une", L"New save, or restart one") : T(L"Une partie neuve chez l'hôte", L"A fresh game at the host's") };
        for (int i = 0; i < 2; i++) {
            kChoiceR[i] = RectF(x, y, w, 50);
            RectF r = kChoiceR[i];
            bool on = partie == i, hot = g_lobbyHot == i && host && !go;
            float a = lobby == LB_CONNECTING ? 0.45f : !host && !on ? 0.6f : 1.0f;   // (invite : le choix ecarte en retrait)
            GraphicsPath cp; RoundRect(cp, r, 14);
            SolidBrush cb(on ? WithA(kAcc, 0.14f * a) : hot ? TH(cardSel) : TH(card)); g.FillPath(&cb, &cp);
            Pen cpen(on ? WithA(kAcc, 0.6f * a) : TH(choiceBorder), on ? 1.4f : 1.0f); g.DrawPath(&cpen, &cp);
            float tx = r.X + 14;
            if (host) {   // bouton radio
                RectF dot(r.X + 14, r.Y + r.Height / 2 - 9, 18, 18);
                if (on) { Pen rp(WithA(kAcc, a), 5); g.DrawEllipse(&rp, dot.X + 2.5f, dot.Y + 2.5f, 13.0f, 13.0f); }
                else { Pen rp(TH(fieldBorder), 1.5f); g.DrawEllipse(&rp, dot); }
                tx = r.X + 42;
            } else if (on) { Icon(g, IC_CHECK, r.X + 22, r.Y + r.Height / 2, 16, WithA(kAcc, a), 2.8f); tx = r.X + 38; }
            Text(g, lab[i], RectF(tx, r.Y + 7, r.X + r.Width - 12 - tx, 20), 13.5f, FontStyleBold, WithA(kInk, a), StringAlignmentNear);
            Text(g, sub[i], RectF(tx, r.Y + 27, r.X + r.Width - 12 - tx, 16), 11, FontStyleRegular, WithA(kGrey, a), StringAlignmentNear);
            y += 58;
        }
        y -= 8;
    };
    if (host) {
        Title(g, T(L"Partie", L"Game"), RectF(x, y, 100, 24), 16, kInk);
        std::wstring sn = SaveCurrentName();   // (saves.inc : clic sur la pastille -> choisir, ou en creer une)
        sn = FitText(sn, 11, FontStyleBold, w - 110);
        float cw = ChipW(sn);
        RectF sr(right - cw, y - 1, cw, 26);
        Chip(g, sr, sn, kInk, g_uiHot != UI_SAVE_LOBBY);
        if (!go) HotZone(sr, UI_SAVE_LOBBY);
        y += 36;
        choiceCards();
        std::wstring note = partie == PARTIE_NOUVELLE ? std::wstring(g_game == GAME_MSC ? T(L"Couleur de la SATSUMA : ", L"SATSUMA color: ") : T(L"Couleur de la CORRIS : ", L"CORRIS color: ")) + CarColorName() + L"." : std::wstring(T(L"Tes invités la reçoivent en arrivant.", L"Your guests receive it when they arrive."));
        Text(g, note, RectF(x, y + 2, w, 16), 10.5f, FontStyleRegular, partie == PARTIE_NOUVELLE ? kWarn : kGrey, StringAlignmentNear);
        y += 18;
        gap();
        // envoye aux invites (contenu de la page PARTAGE : g_offer)
        Title(g, T(L"Envoyé aux invités", L"Sent to guests"), RectF(x, y, w - 90, 24), 16, kInk);
        kSyncBtn = RectF(right - 84, y - 3, 84, 30);   // CHOISIR (LobbyChoiceAt : 71 -> page PARTAGE)
        { GraphicsPath p; RoundRect(p, kSyncBtn, 10); SolidBrush b(g_lobbyHot == 71 ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p); Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p); Text(g, T(L"Choisir", L"Choose"), kSyncBtn, 12, FontStyleBold, kInk); }
        y += 34;
        bool msc;
        std::vector<std::string> mods, cats;
        uint64_t bytes = 0;
        EnterCriticalSection(&g_lcs);
        msc = g_offer.msc; mods = g_offer.mods; cats = g_offer.cats;
        for (auto &f : g_offer.files) bytes += f.size;
        LeaveCriticalSection(&g_lcs);
        if (mods.empty() && cats.empty()) {
            Text(g, msc ? T(L"Rien n'est envoyé", L"Nothing is sent") : T(L"Rien (MSCLoader coupé : invités sans mods)", L"Nothing (MSCLoader off: guests without mods)"), RectF(x, y, w, 24), 12, FontStyleRegular, kGrey, StringAlignmentNear);
            y += 26;
        } else {
            std::vector<std::wstring> chips;
            int cds = 0;
            for (auto &k : cats) { if (!strncmp(k.c_str(), "cd", 2)) { cds++; continue; } chips.push_back(LobbyCatLabel(k)); }
            if (cds) { wchar_t b[16]; swprintf_s(b, L"%d CD", cds); chips.insert(chips.begin(), b); }
            if (!mods.empty()) { wchar_t b[32]; swprintf_s(b, T(L"%d mod(s)", L"%d mod(s)"), (int)mods.size()); chips.push_back(b); }
            float cx = x, cy = y;
            int lines = 1;
            for (auto &s : chips) {
                float cw = ChipW(s);
                if (cx + cw > right && cx > x) {
                    if (lines == 2) { Text(g, L"…", RectF(cx, cy, 16, 26), 12, FontStyleBold, kGrey); break; }
                    cx = x; cy += 32; lines++;
                }
                Chip(g, RectF(cx, cy, cw, 26), s, kInk, true);
                cx += cw + 6;
            }
            y = cy + 32;
            wchar_t b[120];
            swprintf_s(b, T(L"%s · copiés chez chaque invité à l'arrivée.", L"%s · copied to each guest on arrival."), SizeText(bytes).c_str());
            Text(g, b, RectF(x, y, w, 16), 10.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            y += 18;
        }
        gap();
        // mods de la partie : chacun, et ou en sont les invites (l'avancement est global par invite)
        Title(g, T(L"Mods de la partie", L"Game mods"), RectF(x, y, w - 90, 24), 16, kInk);
        UiButton(g, RectF(right - 84, y - 3, 84, 30), T(L"Gérer", L"Manage"), UI_LOBBY_MODS, false, -1, true, 10);
        y += 34;
        if (mods.empty()) {
            Text(g, msc ? T(L"Aucun mod envoyé", L"No mod sent") : T(L"MSCLoader coupé : jeu sans mods", L"MSCLoader off: game without mods"), RectF(x, y, w, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
            y += 22;
        } else {
            std::string pend;
            int guests = 0, pending = 0;
            for (auto &p : peers) if (p.id != 0) { guests++; if (PeerSyncPending(p)) { pending++; if (pend.empty()) pend = p.name + " " + std::to_string(max(0, p.dl)) + " %"; } }
            int shown = min(3, (int)mods.size());
            for (int i = 0; i < shown; i++) {
                std::wstring nm = Widen(mods[i], CP_UTF8);
                size_t dot = nm.rfind(L'.');
                if (dot != std::wstring::npos) nm.resize(dot);
                Text(g, FitText(nm, 12.5f, FontStyleBold, w - 120), RectF(x, y, w - 116, 20), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
                std::wstring stt = !guests ? T(L"personne encore", L"nobody yet") : pending ? Widen(pend, CP_UTF8) : T(L"tous", L"everyone");
                Text(g, stt, RectF(right - 110, y, 110, 20), 12, FontStyleBold, !guests ? kGrey : pending ? kWarn : kOk, StringAlignmentFar);
                y += 22;
            }
            if ((int)mods.size() > shown) { wchar_t b[48]; swprintf_s(b, T(L"+ %d autre(s)", L"+ %d more"), (int)mods.size() - shown); Text(g, b, RectF(x, y, w, 18), 11, FontStyleRegular, kGrey, StringAlignmentNear); y += 18; }
        }
    } else if (lobby == LB_GUEST) {
        Title(g, T(L"Envoi de l'hôte", L"Host's files"), RectF(x, y, w - 100, 24), 16, kInk);
        int s = g_sync, n;
        std::wstring why;
        { std::lock_guard<std::mutex> lk(g_syncMx); n = (int)g_hostOffer.mods.size(); why = Widen(g_syncWhy, CP_UTF8); }
        uint64_t tot = g_syncTotal, got = g_syncGot;
        bool shown = SyncBarShown() && s != SY_NONE && s != SY_OFF;
        std::wstring chip = s == SY_GET ? T(L"En cours", L"In progress") : s == SY_DONE ? T(L"Prêt ✓", L"Ready ✓") : s == SY_ASK ? T(L"À accepter", L"To accept") : s == SY_FAIL ? T(L"Échec", L"Failed") : s == SY_CHECK ? T(L"Vérification…", L"Checking…") : T(L"Rien", L"Nothing");
        Color cc = s == SY_GET ? kWarn : s == SY_DONE ? kOk : s == SY_ASK ? kAcc : s == SY_FAIL ? kRed : kInk;
        float cw = ChipW(chip);
        Chip(g, RectF(right - cw, y - 1, cw, 26), chip, cc, !shown);
        y += 34;
        // trois barres : mods (n / total, le fichier en cours), contenu (taille, quoi), sauvegarde (a l'arrivee en jeu)
        SyncParts sp = SyncPartsNow(s);
        wchar_t b[200];
        auto barRow = [&](const std::wstring &l, const std::wstring &rt, float prog, Color rc, Color fill) {
            Text(g, l, RectF(x, y, w - 100, 18), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
            float lw = MeasureW(MeasureG(), l, 12.5f, FontStyleBold) + 8;
            Text(g, FitText(rt, 11, FontStyleRegular, w - lw), RectF(x + lw, y, w - lw, 18), 11, FontStyleRegular, rc, StringAlignmentFar);
            RectF br(x, y + 22, w, 7);
            GraphicsPath bg; RoundRect(bg, br, 3.5f);
            SolidBrush bb(WithA(kInk, 0.10f)); g.FillPath(&bb, &bg);
            if (prog < 0) DrawBar(g, br, prog);
            else if (prog > 0) { RectF fr = br; fr.Width = max(7.0f, br.Width * min(prog, 1.0f)); GraphicsPath fp; RoundRect(fp, fr, 3.5f); SolidBrush fb(fill); g.FillPath(&fb, &fp); }
            y += 38;
        };
        std::wstring r1 = T(L"Mods", L"Mods"), r1r, r2 = T(L"Contenu", L"Content"), r2r;
        float p1 = 0, p2 = 0;
        Color c1 = kGrey, c2 = kGrey;
        if (!shown) { r1r = r2r = T(L"rien pour l'instant", L"nothing for now"); }
        else if (s == SY_CHECK) { r1r = r2r = T(L"vérification…", L"checking…"); p1 = p2 = -2; }
        else if (s == SY_FAIL) { r1r = std::wstring(T(L"échec : ", L"failed: ")) + why; c1 = kRed; r2r = L"—"; }
        else if (sp.modsN == 0 && sp.contB == 0) {   // liste des fichiers pas (encore) connue : l'avancement global sur les deux barres
            if (n) { swprintf_s(b, L" · %d", n); r1 += b; }
            p1 = p2 = s == SY_DONE ? 1.0f : tot ? (float)((double)got / tot) : 0.0f;
            if (s == SY_DONE) r1r = r2r = T(L"tout reçu", L"all received");
            else if (s == SY_GET) { swprintf_s(b, T(L"%d %% de %s", L"%d%% of %s"), (int)(p1 * 100), SizeText(tot).c_str()); r1r = r2r = b; }
            else r1r = r2r = SizeText(tot);
        } else {
            if (n) { swprintf_s(b, L" · %d / %d", sp.modsDone, max(n, sp.modsN)); r1 += b; }
            p1 = sp.modsB ? (float)((double)sp.modsGot / sp.modsB) : s == SY_DONE ? 1.0f : 0.0f;
            if (s == SY_DONE || sp.modsDone == sp.modsN) r1r = n ? T(L"tous reçus", L"all received") : T(L"aucun", L"none");
            else if (s == SY_GET && !sp.cur.empty()) { swprintf_s(b, L"%s %d %%", sp.cur.c_str(), sp.curPct); r1r = b; }
            else { swprintf_s(b, T(L"%d mods · %s", L"%d mods · %s"), n, SizeText(sp.modsB).c_str()); r1r = b; }
            if (sp.contB) { r2 += L" · " + SizeText(sp.contB); }
            p2 = sp.contB ? (float)((double)sp.contGot / sp.contB) : s == SY_DONE ? 1.0f : 0.0f;
            std::vector<std::wstring> cl;
            int cds = 0;
            for (auto &k : sp.cats) { if (!strncmp(k.c_str(), "cd", 2)) { cds++; continue; } if (cl.size() < 3) cl.push_back(LobbyCatLabel(k)); }
            if (cds) cl.insert(cl.begin(), L"CD");
            for (size_t i = 0; i < cl.size() && i < 3; i++) r2r += (i ? L", " : L"") + cl[i];
            if (cl.size() > 3) r2r += L"…";
            if (r2r.empty()) r2r = sp.cats.empty() ? T(L"rien", L"nothing") : L"";
        }
        barRow(r1, r1r, p1, c1, p1 >= 1.0f ? kOk : kAcc);
        barRow(r2, r2r, p2, c2, p2 >= 1.0f ? kOk : kAcc);
        barRow(T(L"Sauvegarde", L"Save"), partie == PARTIE_NOUVELLE ? T(L"partie neuve : rien à recevoir", L"fresh game: nothing to receive") : T(L"à l'arrivée en jeu", L"on arrival in game"), 0, kGrey, kAcc);
        int mi = LobbyOptIdx("ModsAuto");   // tout accepter de cet hote (option « les miennes », pour ce salon)
        if (mi >= 0) {
            RectF row(x, y, w, 40);
            int id = UI_LOBBY_OPT + mi * 3;
            bool hot = g_uiHot == id && !go;
            if (hot) { GraphicsPath hp; RoundRect(hp, RectF(row.X - 6, row.Y, row.Width + 12, row.Height), 9); SolidBrush hb(TH(card)); g.FillPath(&hb, &hp); }
            Text(g, T(L"Tout accepter de cet hôte", L"Accept everything from this host"), RectF(x, y + 3, w - 56, 18), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
            Text(g, T(L"Sans redemander à chaque fichier", L"Without asking for each file"), RectF(x, y + 21, w - 56, 16), 10.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            UiToggle(g, RectF(right - 42, y + 8, 42, 24), LobbyOptGet("ModsAuto") == 1, hot);
            if (!go) HotZone(row, id);
            y += 42;
        }
        if (s == SY_ASK || s == SY_FAIL) {   // TOUT ACCEPTER / REESSAYER (LobbyChoiceAt : 70)
            kSyncBtn = RectF(x, y, w, 40);
            GraphicsPath p; RoundRect(p, kSyncBtn, 12);
            SolidBrush lg(kAcc); g.FillPath(&lg, &p);
            if (g_lobbyHot == 70) { SolidBrush hb(Color(55, 255, 255, 255)); g.FillPath(&hb, &p); }
            Text(g, s == SY_ASK ? T(L"TOUT ACCEPTER", L"ACCEPT ALL") : T(L"RÉESSAYER", L"RETRY"), kSyncBtn, 13, FontStyleBold, kOnAcc);
            y += 48;
        }
        gap();
        EnterCriticalSection(&g_lcs);
        std::wstring hs = g_hostSaveName;
        LeaveCriticalSection(&g_lcs);
        std::wstring ttl = hs.empty() ? std::wstring(T(L"Partie (choisie par l'hôte)", L"Game (chosen by the host)")) : std::wstring(T(L"Partie : ", L"Game: ")) + hs;
        Title(g, FitText(ttl, 16, FontStyleBold, w), RectF(x, y, w, 24), 16, kInk);
        y += 34;
        choiceCards();
    } else {   // connexion : la partie, en attendant
        Title(g, T(L"Partie", L"Game"), RectF(x, y, w, 24), 16, kInk);
        y += 34;
        choiceCards();
    }
    // conseil, ou avertissement UDP (ambre)
    const float btnTop = c.Y + c.Height - 18 - 44 - 8 - 64;
    RectF hc(x, btnTop - 12 - 64, w, 64);
    GraphicsPath hp; RoundRect(hp, hc, 12);
    if (!warn.empty()) {
        SolidBrush fb(WithA(kWarn, 0.10f)); g.FillPath(&fb, &hp);
        Pen pen(WithA(kWarn, 0.45f), 1); g.DrawPath(&pen, &hp);
        Icon(g, IC_ALERT, hc.X + 18, hc.Y + 20, 16, kWarn, 2.0f);
        Para(g, warn, RectF(hc.X + 34, hc.Y + 6, hc.Width - 42, hc.Height - 10), 10.5f, kInk);
    } else {
        const wchar_t *hint = host && g_lobbySteam ? T(L"Invite tes amis Steam. Quand tout le monde est prêt, LANCER démarre le jeu de chacun.",
                                                     L"Invite your Steam friends. Once everyone is ready, START launches everyone's game.")
                            : host ? T(L"Quand tout le monde est prêt, LANCER démarre le jeu de chacun ; les invités suivent ta partie.",
                                       L"Once everyone is ready, START launches everyone's game; the guests follow your game.")
                                   : T(L"Clique sur PRÊT. Ton jeu démarre tout seul quand l'hôte lance la partie.",
                                       L"Click READY. Your game starts by itself when the host starts the session.");
        SolidBrush fb(WithA(kAcc, 0.08f)); g.FillPath(&fb, &hp);
        Pen pen(WithA(kAcc, 0.35f), 1); g.DrawPath(&pen, &hp);
        Icon(g, IC_SPARK, hc.X + 18, hc.Y + 20, 16, kAcc, 1.9f);
        Para(g, hint, RectF(hc.X + 34, hc.Y + 8, hc.Width - 44, hc.Height - 12), 11.5f, kInk);
    }
    // boutons : LANCER / PRET ; FERMER LE SALON / QUITTER / ANNULER
    for (int id : { B_HOST, B_JOIN }) {
        Button &b = g_btn[id];
        bool main_ = id == B_HOST;
        b.r = main_ ? RectF(x, btnTop, w, 64) : RectF(x, c.Y + c.Height - 18 - 44, w, 44);
        RectF r = b.r;
        if (g_pressed == id && g_hot == id) r.Offset(0, 1);
        float a = b.enabled ? 1.0f : 0.4f;
        GraphicsPath p; RoundRect(p, r, main_ ? 18.0f : 14.0f);
        std::wstring l1, l2;
        if (main_) {
            if (lobby == LB_HOST) {
                int guests = 0, notReady = 0;
                GuestCounts(&guests, &notReady);
                l1 = T(L"LANCER LA PARTIE", L"START THE GAME");
                wchar_t b2[120];
                std::string dln;
                int dls = 0;
                for (auto &pp : peers) if (PeerSyncPending(pp)) { dls++; if (dln.empty()) dln = pp.name; }
                if (!guests) swprintf_s(b2, L"%s", T(L"Seul pour l'instant", L"Alone for now"));
                else if (dls == 1) swprintf_s(b2, T(L"%d / %d prêts · %s télécharge les mods", L"%d / %d ready · %s is downloading the mods"), guests - notReady, guests, Widen(dln, CP_UTF8).c_str());
                else if (dls) swprintf_s(b2, T(L"%d / %d prêts · envoi des mods (%d)…", L"%d / %d ready · sending mods (%d)…"), guests - notReady, guests, dls);
                else swprintf_s(b2, T(L"%d / %d prêt(s)", L"%d / %d ready"), guests - notReady, guests);
                l2 = b2;
            } else if (lobby == LB_GUEST && g_wantReady && !g_meReady) { l1 = T(L"PRÊT APRÈS L'ENVOI", L"READY AFTER FILES"); l2 = T(L"Reçois l'envoi de l'hôte… (clique pour annuler)", L"Getting the host's files… (click to cancel)"); }
            else if (lobby == LB_GUEST) { l1 = g_meReady ? T(L"PRÊT ✓", L"READY ✓") : T(L"PRÊT ?", L"READY?"); l2 = g_meReady ? T(L"Clique pour annuler", L"Click to cancel") : T(L"Clique quand tu es prêt", L"Click when you're ready"); }
            else l1 = T(L"CONNEXION…", L"CONNECTING…");
            bool ready = lobby == LB_GUEST && g_meReady;
            SolidBrush fb(WithA(ready ? kOk : kAcc, a)); g.FillPath(&fb, &p);
            if (b.hover > 0.01f) { SolidBrush hb(Color((BYTE)(55 * b.hover), 255, 255, 255)); g.FillPath(&hb, &p); }
            Color ink = WithA(ready ? Color(255, 4, 38, 26) : kOnAcc, a);
            if (l2.empty()) TextF(g, TitleFont(), l1, r, 19, FontStyleBold, ink);
            else {
                TextF(g, TitleFont(), l1, RectF(r.X, r.Y + 9, r.Width, 28), 19, FontStyleBold, ink);
                Text(g, FitText(l2, 11.5f, FontStyleBold, r.Width - 16), RectF(r.X + 8, r.Y + 37, r.Width - 16, 18), 11.5f, FontStyleBold, WithA(ink, 0.78f));
            }
        } else {
            l1 = lobby == LB_HOST ? T(L"FERMER LE SALON", L"CLOSE THE LOBBY") : lobby == LB_GUEST ? T(L"QUITTER LE SALON", L"LEAVE THE LOBBY") : T(L"ANNULER", L"CANCEL");
            SolidBrush fb(Mix(TH(btn2), TH(btn2Hot), b.hover)); g.FillPath(&fb, &p);
            Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p);
            Text(g, l1, r, 13, FontStyleBold, WithA(kInk, a));
        }
    }
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
    kSyncBtn = RectF(0, 0, 0, 0);   // (place par la carte de droite)
    if (g_lobbySinceGen != g_lobbyGen) { g_lobbySinceGen = g_lobbyGen; g_lobbySinceT = GetTickCount(); }   // (nouveau salon : le compteur repart)
    // UDP bloque (ou port UDP pris chez l'hote) : avertissement dans la carte de droite, a la place du conseil
    std::wstring warn;
    if (host && g_udpHostErr) {
        wchar_t wb[200];
        swprintf_s(wb, T(L"Port UDP %d déjà pris sur ce PC (erreur %d) : le jeu ne pourra pas l'ouvrir. Ferme le programme qui l'occupe.",
                         L"UDP port %d is already in use on this PC (error %d): the game will not be able to open it. Close the program using it."), g_lobbyPort, g_udpHostErr);
        warn = wb;
    } else if (host) {
        std::string names;
        for (auto &p : peers) if (p.id != 0 && p.udp == UDP_FAIL) names += (names.empty() ? "" : ", ") + p.name;
        if (!names.empty()) {
            wchar_t wb[300];
            swprintf_s(wb, T(L"UDP bloqué pour %s : redirige le port UDP %d (pas seulement TCP) sur ta box, vers ce PC. Le salon passe en TCP, le jeu en UDP.",
                             L"UDP blocked for %s: forward UDP port %d (not just TCP) on your router to this PC. The lobby uses TCP, the game uses UDP."),
                       Widen(names, CP_UTF8).c_str(), g_lobbyPort);
            warn = wb;
        }
    } else if (lobby == LB_GUEST && myUdp == UDP_FAIL && g_hostUdpTest) {
        wchar_t wb[300];
        swprintf_s(wb, T(L"UDP bloqué : l'hôte doit rediriger le port UDP %d (pas seulement TCP) sur sa box. Le salon passe en TCP, le jeu en UDP.",
                         L"UDP blocked: the host must forward UDP port %d (not just TCP) on their router. The lobby uses TCP, the game uses UDP."), g_lobbyPort);
        warn = wb;
    }
    bool split = lobby != LB_CONNECTING && !(host && g_lobbySteam && g_sFriendsView);   // bandeau, Joueurs et Options ; sinon une seule carte a gauche
    if (split) {
        DrawLobbyBanner(g, kLobbyBanner, host, peers, partie);
        DrawLobbyPlayersCard(g, kLobbyPlayersCard, peers, lobby, myUdp);
        DrawLobbyOptCard(g, kLobbyOptsCard, host, LobbyOptTab(host));
    } else {
        RectF lc = kLobbyCard;
        Glass(lc);
        float top = lc.Y + (host && g_lobbySteam && g_sFriendsView ? 76 : 60);
        kLobbyList = RectF(lc.X + 20, top, lc.Width - 40, lc.Y + lc.Height - 18 - top);
        if (host && g_lobbySteam && g_sFriendsView) DrawSteamFriends(g);
        else if (g_sCodeAsk) {   // salon Steam prive, sans invitation : le code de session d'abord
            float cy = kLobbyList.Y + kLobbyList.Height / 2 - 90;
            Title(g, T(L"Salon privé : code de session", L"Private lobby: session code"), RectF(lc.X, cy, lc.Width, 34), 22, kInk, StringAlignmentCenter);
            Para(g, std::wstring(T(L"Le salon de", L"The lobby of")) + L" " + g_sHostName + L" " + T(L"est privé. Entre le code à 6 caractères qu'il t'a donné, ou demande-lui une invitation Steam (elle suffit).", L"is private. Enter the 6-character code they gave you, or ask them for a Steam invite (it is enough)."),
                 RectF(lc.X + 90, cy + 44, lc.Width - 180, 44), 13, kGrey, StringAlignmentCenter);
            g_fields[2].r = RectF(lc.X + lc.Width / 2 - 150, cy + 104, 176, 48);
            DrawField(g, 2, L"");
            UiButton(g, RectF(lc.X + lc.Width / 2 + 36, cy + 104, 114, 48), T(L"ENTRER", L"ENTER"), UI_LOBBY_CODEOK, true, IC_LOGIN);
        } else {
            bool creating = g_lobbySteam && g_myId == 0;
            float cy = kLobbyList.Y + kLobbyList.Height / 2 - 60;
            Title(g, creating ? T(L"Création du salon Steam…", L"Creating the Steam lobby…") : T(L"Connexion au salon…", L"Connecting to the lobby…"),
                  RectF(lc.X, cy, lc.Width, 34), 22, kInk, StringAlignmentCenter);
            DrawBar(g, RectF(lc.X + 170, cy + 50, lc.Width - 340, 5), -2);
            Para(g, creating ? T(L"Un salon réservé à tes amis Steam : tu pourras les inviter d'ici.", L"A lobby for your Steam friends only: you can invite them from here.")
                             : T(L"Si l'hôte joue déjà ou n'a pas de salon, tu pourras rejoindre directement en jeu.", L"If the host is already playing or has no lobby, you can join directly in game."),
                 RectF(lc.X + 100, cy + 72, lc.Width - 200, 44), 13, kGrey, StringAlignmentCenter);
        }
    }
    DrawLobbyRightCard(g, kLobbyRight, peers, lobby, partie, warn);
    if (g_lobbyOpts) DrawLobbyPopover(g);
}

// En-tete du salon : engrenage (options du salon), et pour l'hote d'un salon Steam : inviter des amis.
static void DrawLobbyHeadControls(Graphics &g, float &right)
{
    if (g_lobby == LB_CONNECTING) return;
    if (g_lobby == LB_GUEST) {   // pastille de l'invite : UDP et mon ping (IP), ou Steam
        int ping = -1, udp = g_udpMine;
        EnterCriticalSection(&g_lcs);
        for (auto &p : g_peers) if (p.id == g_myId) ping = p.ping;
        LeaveCriticalSection(&g_lcs);
        std::wstring t;
        Color col = kInk;
        if (g_lobbySteam) t = L"Steam";
        else {
            wchar_t b[64];
            if (udp == UDP_OK) { swprintf_s(b, L"UDP OK · %d ms", max(0, ping)); col = kOk; }
            else if (udp == UDP_FAIL && g_hostUdpTest) { swprintf_s(b, L"%s", T(L"UDP bloqué", L"UDP blocked")); col = kRed; }
            else swprintf_s(b, T(L"UDP · test… · %d ms", L"UDP · testing… · %d ms"), max(0, ping));
            t = b;
        }
        float cw = ChipW(t, 12);
        Chip(g, RectF(right - cw, 31, cw, 26), t, col, col.GetValue() == kInk.GetValue(), 12);
        right -= cw + 10;
    }
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

// Hote : le salon (prive / public, code, joueurs max) et les options de la partie aux invites (STATE, ou donnees du salon Steam).
static void LobbyPublish()
{
    if (g_lobby != LB_HOST) return;
    if (g_lobbySteam) { SSetLobby("prive", g_lobbyPrive ? "1" : "0"); SSetLobby("code", g_lobbyPrive ? g_lobbyCode : ""); SSetLobby("max", std::to_string(g_lobbyMax)); SSetLobby("options", LobbyOptText()); }
    else BroadcastState();
}
// Commandes du salon : Public / Prive, COPIER, nouveau code, joueurs max (- / +, ou 4 / 8 / 12 / 16), onglets et options
// (hote ; ajouts Tous / Choisir / Aucun ; Remettre, Garder comme defauts) ; mes options (invite) ; Gerer (page MODS) ;
// ENTRER (code d'un salon Steam prive).
static void LobbyOptClick(int id)
{
    bool host = g_lobby == LB_HOST;
    if (id == UI_LOBBY_CODEOK) { SteamCodeSubmit(); return; }
    if (id == UI_LOBBY_MODS) { GoPage(TAB_MODS); return; }
    if (id == UI_LOBBY_CHAT) { g_chatFocus = g_lobby == LB_HOST || g_lobby == LB_GUEST; g_time = 0; return; }   // (le champ du tchat prend le clavier)
    if (id == UI_LOBBY_SEND) { LobbyChatSend(); g_chatFocus = true; g_time = 0; return; }
    if (g_goSent) return;
    if (id >= UI_LOBBY_SEGV && id < 3300) {   // pastilles segmentees de « Les miennes » (portee, volume, micro, carte)
        int base = UI_LOBBY_SEGV;
        for (const char *k : kLobbySegKeys) {
            const GfxRow *r = GfxRowByKey(k);
            int n = r ? (int)r->vals.size() : 0;
            if (id < base + n) { g_lobbyOpt[k] = r->vals[id - base]; TestLog("salon : option %s = %d (ce salon seulement)", k, r->vals[id - base]); return; }
            base += n;
        }
        return;
    }
    if (id == UI_LOBBY_OPTTAB || id == UI_LOBBY_OPTTAB + 1) { g_lobbyOptTab = id - UI_LOBBY_OPTTAB; return; }
    if (id == UI_LOBBY_COPY) {
        if (CopyText(Widen(g_lobbyCode))) { g_codeCopiedT = GetTickCount(); SetStatus(K_OK, T(L"Code de session copié : %S", L"Session code copied: %S"), g_lobbyCode.c_str()); }
        return;
    }
    bool pub = false;
    if (id == UI_LOBBY_PRIV || id == UI_LOBBY_PRIV + 1) {
        if (!host) return;
        bool prive = id == UI_LOBBY_PRIV + 1;
        if (prive == g_lobbyPrive) return;
        g_lobbyPrive = prive;
        if (prive && g_lobbyCode.empty()) g_lobbyCode = MakeSessionCode();
        WritePrivateProfileStringW(L"Lanceur", L"SalonPrive", prive ? L"1" : L"0", g_iniLauncher.c_str());
        SetStatus(K_NORMAL, prive ? T(L"Salon privé : le code de session est demandé", L"Private lobby: the session code is required") : T(L"Salon public : pas de code", L"Public lobby: no code"));
        TestLog("salon : %s (code %s)", prive ? "prive" : "public", g_lobbyCode.c_str());
        pub = true;
    } else if (id == UI_LOBBY_REGEN) {
        if (!host) return;
        g_lobbyCode = MakeSessionCode();
        TestLog("salon : nouveau code de session %s", g_lobbyCode.c_str());
        pub = true;
    } else if (id == UI_LOBBY_MAXM || id == UI_LOBBY_MAXP) {
        if (!host) return;
        EnterCriticalSection(&g_lcs);
        int n = (int)g_peers.size();
        LeaveCriticalSection(&g_lcs);
        int v = max(max(2, n), min(kLobbyMaxAbs, g_lobbyMax + (id == UI_LOBBY_MAXP ? 1 : -1)));
        if (v == g_lobbyMax) return;
        g_lobbyMax = v;
        WritePrivateProfileStringW(L"Lanceur", L"SalonMax", std::to_wstring(v).c_str(), g_iniLauncher.c_str());
        if (g_lobbySteam) SteamLobbySetMax(v);
        TestLog("salon : %d joueurs au plus", v);
        pub = true;
    } else if (id >= UI_LOBBY_MAXSEG && id < UI_LOBBY_MAXSEG + 4) {   // 4 / 8 / 12 / 16 (jamais moins que les presents)
        if (!host) return;
        static const int opts[4] = { 4, 8, 12, 16 };
        int v = opts[id - UI_LOBBY_MAXSEG];
        EnterCriticalSection(&g_lcs);
        int n = (int)g_peers.size();
        LeaveCriticalSection(&g_lcs);
        if (v < n) { SetStatus(K_WARN, T(L"Déjà %d joueurs dans le salon", L"Already %d players in the lobby"), n); return; }
        if (v == g_lobbyMax) return;
        g_lobbyMax = v;
        WritePrivateProfileStringW(L"Lanceur", L"SalonMax", std::to_wstring(v).c_str(), g_iniLauncher.c_str());
        if (g_lobbySteam) SteamLobbySetMax(v);
        TestLog("salon : %d joueurs au plus", v);
        pub = true;
    } else if (id >= UI_LOBBY_AJOUTS && id < UI_LOBBY_AJOUTS + 3) {   // ajouts MWCoop : Tous (les quatre), Choisir (interrupteurs montres), Aucun
        if (!host) return;
        int k = id - UI_LOBBY_AJOUTS;
        g_lobbyAjoutsPick = k == 1;
        g_lobbyOpt["SansAjouts"] = k == 2 ? 1 : 0;
        if (k == 0) for (const char *key : kAjoutKeys) g_lobbyOpt[key] = 1;
        TestLog("salon : ajouts MWCoop %s (ce salon seulement)", k == 0 ? "tous" : k == 1 ? "au choix" : "aucun");
        pub = true;
    } else if (id == UI_LOBBY_OPTRESET || id == UI_LOBBY_OPTKEEP) {   // onglet courant : remettre les valeurs des Reglages ; les garder comme defauts
        bool mine = LobbyOptTab(host) == 1, reset = id == UI_LOBBY_OPTRESET;
        if (!mine && !host) return;
        std::string ini = ModIniA();
        for (auto &d : kLobbyOptDefs) {
            if (d.host == mine) continue;
            if (reset) g_lobbyOpt[d.key] = GetPrivateProfileIntA("Coop", d.key, d.def, ini.c_str());
            else if (const GfxRow *r = GfxRowByKey(d.key)) { if (r->sec && !strcmp(r->sec, "Coop")) WritePrivateProfileStringA("Coop", d.key, std::to_string(LobbyOptGet(d.key)).c_str(), ini.c_str()); }
        }
        if (!mine) g_lobbyAjoutsPick = false;
        SetStatus(K_OK, reset ? T(L"Options remises à celles des Réglages (ce salon)", L"Options reset to the Settings ones (this lobby)") : T(L"Options gardées comme défauts (Réglages > Co-op)", L"Options kept as defaults (Settings > Co-op)"));
        TestLog("salon : options %s %s", mine ? "les miennes" : "de la partie", reset ? "remises (mwcoop.ini)" : "gardees comme defauts (mwcoop.ini)");
        pub = !mine;
    } else if (id >= UI_LOBBY_OPT && id < UI_LOBBY_OPT + kLobbyOptN * 3) {
        int i = (id - UI_LOBBY_OPT) / 3, part = (id - UI_LOBBY_OPT) % 3;
        const LobbyOptDef &d = kLobbyOptDefs[i];
        if (d.host && !host) return;
        const GfxRow *r = GfxRowByKey(d.key);
        int v = LobbyOptGet(d.key);
        if (!r || r->vals.empty()) v = v ? 0 : 1;
        else {
            int k = 0;
            for (int j = 0; j < (int)r->vals.size(); j++) if (r->vals[j] == v) k = j;
            k += part == 2 ? 1 : part == 1 ? -1 : 0;
            if (k < 0 || k >= (int)r->vals.size()) return;
            v = r->vals[k];
        }
        g_lobbyOpt[d.key] = v;
        TestLog("salon : option %s = %d (ce salon seulement)", d.key, v);
        pub = d.host;
    }
    if (pub) LobbyPublish();
}

static bool LobbyClick(float x, float y)
{
    int c = LobbyChoiceAt(x, y);
    if (c >= 100) { SteamInviteFriend(c - 100); return true; }
    if (c == 50 || c == 51) { SteamShowFriends(c == 51); return true; }
    if (c == 70) { if (g_sync == SY_FAIL) SyncRetry(); else SyncAccept(); return true; }
    if (c == 71) { GoPage(TAB_CONTENT); return true; }
    if (c >= 200 && c < 300) {   // hote IP : un invite, deux clics en 4 s pour le mettre dehors
        int id = -1;
        EnterCriticalSection(&g_lcs);
        if (c - 200 < (int)g_peers.size()) id = g_peers[c - 200].id;
        std::string name = id > 0 ? PeerById(id)->name : "";
        LeaveCriticalSection(&g_lcs);
        if (id <= 0) return true;
        if (g_kickArm == id && GetTickCount() - g_kickArmT < 4000) { g_kickArm = -1; LobbyKick(id); }
        else { g_kickArm = id; g_kickArmT = GetTickCount(); SetStatus(K_WARN, T(L"Clique encore sur %S pour le mettre dehors", L"Click %S again to kick them out"), name.c_str()); }
        return true;
    }
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
        if (g_sCodeAsk) { g_fields[2].text = Widen(g_testCode); TestLog("test : code de session tape %s", g_testCode.c_str()); SteamCodeSubmit(); return; }   // (salon prive sans invitation)
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
        if (g_testForce && guests >= 1 && t > 3000) {   // (essai : LANCER pendant la reception des mods -> refuse, puis accepte)
            static DWORD lastTry;
            if (GetTickCount() - lastTry > 1500) { lastTry = GetTickCount(); TestLog("test : LANCER (force)"); HostStart(); }
            return;
        }
        static bool chatted;   // (essai du tchat : l'hote ecrit des qu'un invite est la)
        if (guests >= 1 && !chatted) { chatted = true; g_chatText = L"Bienvenue, on part dans 2 min"; TestLog("test : tchat de l'hote"); LobbyChatSend(); }
        bool can = guests >= 1 && LobbyCanStart() && udpDone;
        if (!can) allReadySince = 0;
        else if (!allReadySince) { allReadySince = GetTickCount(); TestLog("test : tous les invites sont prets"); }
        else if (GetTickCount() - allReadySince > 1500) { TestLog("test : LANCER"); HostStart(); }
    } else {
        if (!tried && t > 2000) { tried = true; LobbyJoin(); }
        int sy = g_sync;   // mods de l'hote : tout accepter, pret une fois recus
        if (g_lobby == LB_GUEST && sy == SY_ASK) { TestLog("test : clic sur TOUT ACCEPTER"); SyncAccept(); }
        if (g_lobby == LB_GUEST && !readied && t > 4500 && sy != SY_ASK && sy != SY_CHECK && sy != SY_GET) { readied = true; TestLog("test : clic sur PRET"); GuestToggleReady(); }
        static bool chatted;   // (essai du tchat : l'invite ecrit en arrivant, accents compris)
        if (g_lobby == LB_GUEST && !chatted && t > 3000) { chatted = true; g_chatText = L"J'arrive, prêt dans une minute !"; TestLog("test : tchat de l'invite"); LobbyChatSend(); }
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
    std::wstring d = g_gameDir.empty() ? L"" : g_modRoot + L"MWCoop\\", ld = LocalDir();
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
    if (!g_gameDir.empty()) { add(g_modRoot + L"MWCoop\\mwcoop.ini"); add(g_modRoot + L"MWCoop\\lancement.ini"); }
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
    if (!g_gameDir.empty()) files.push_back(g_modRoot + L"MWCoop\\mwcoop.ini");
    std::wstring tag = gr.dir.substr(0, gr.dir.size() - 1);
    tag = L"partie-" + tag.substr(tag.find_last_of(L'\\') + 1);
    ZipToDesktop(files, tag);
}

// Wine : la corbeille est celle du bureau Linux ; quand Wine ne peut pas y mettre un dossier, il l'efface en descendant
// dedans -- a travers un lien, jusqu'au vrai jeu. Un dossier ou reste un lien (jonction, lien de Linux) ou un
// mywintercar_Data (copie de lancement dont le lien n'a pas ete vu comme tel) n'y est donc jamais donne.
static bool WineUnsafeDir(const std::wstring &dir, int depth = 0)
{
    DWORD at = GetFileAttributesW(dir.c_str());
    if (at == INVALID_FILE_ATTRIBUTES || !(at & FILE_ATTRIBUTE_DIRECTORY)) return false;
    if ((at & FILE_ATTRIBUTE_REPARSE_POINT) || depth > 12) return true;
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((WithSlash(dir) + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return false;
    bool bad = false;
    do {
        std::wstring n = fd.cFileName;
        if (n == L"." || n == L"..") continue;
        if ((fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) || !_wcsicmp(n.c_str(), GM().data)) bad = true;
        else if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) bad = WineUnsafeDir(WithSlash(dir) + n, depth + 1);
    } while (!bad && FindNextFileW(h, &fd));
    FindClose(h);
    return bad;
}

// Fichiers ou dossiers a la corbeille de Windows (rien d'efface pour de bon). Wine : sans les dossiers a liens (ci-dessus),
// et Wine demande avant d'effacer pour de bon ce qu'il ne peut pas mettre a la corbeille.
static bool ToRecycleBin(const std::vector<std::wstring> &paths)
{
    if (paths.empty()) return true;
    std::wstring list;
    bool refused = false;
    for (auto &p : paths) {
        std::wstring q = p; if (!q.empty() && q.back() == L'\\') q.pop_back();
        if (IsWine() && WineUnsafeDir(q)) { refused = true; LaunchLog("Wine : %s pas mis a la corbeille (liens vers le jeu dedans)", Narrow(q, CP_UTF8).c_str()); continue; }
        list += q; list.push_back(L'\0');
    }
    if (list.empty()) return false;
    list.push_back(L'\0');
    SHFILEOPSTRUCTW op = {};
    op.hwnd = g_wnd;
    op.wFunc = FO_DELETE;
    op.pFrom = list.c_str();
    op.fFlags = FOF_ALLOWUNDO | FOF_SILENT | FOF_NOERRORUI | (IsWine() ? 0 : FOF_NOCONFIRMATION);
    return SHFileOperationW(&op) == 0 && !op.fAnyOperationsAborted && !refused;
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
        add(g_modRoot + L"MWCoop");
    }
    add(g_iniLauncher);
    add(g_self);
    add(g_self + L".old");
    if (RunningFromHome()) {   // (le raccourci « MWCoop » du bureau, cree a l'installation : install.inc)
        wchar_t desk[MAX_PATH] = L"";
        if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_DESKTOPDIRECTORY, NULL, SHGFP_TYPE_CURRENT, desk))) add(WithSlash(desk) + L"MWCoop.lnk");
    }
    std::wstring ld = LocalDir();
    int links = 0;
    if (!ld.empty() && GetFileAttributesW(ld.c_str()) != INVALID_FILE_ATTRIBUTES) {
        links = RemoveJunctionsIn(ld);
        if (!IsWine()) add(ld.substr(0, ld.size() - 1));
        else {   // (Wine : element par element ; une copie de lancement ou un lien reste est laissee -- ToRecycleBin)
            WIN32_FIND_DATAW fd;
            HANDLE h = FindFirstFileW((ld + L"*").c_str(), &fd);
            if (h != INVALID_HANDLE_VALUE) {
                do { if (wcscmp(fd.cFileName, L".") && wcscmp(fd.cFileName, L"..")) add(ld + fd.cFileName); } while (FindNextFileW(h, &fd));
                FindClose(h);
            }
        }
    }
    if (registry && IsWine() && !g_wineSim) for (const wchar_t *d : kWineDlls) RegDeleteKeyValueW(HKEY_CURRENT_USER, WineAppKey().c_str(), d);   // (pose par WineOverrides)
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
    if (IsWine() && !ok) msg += T(L"\n\nSous Linux, les copies de lancement (avec des liens vers le jeu) restent : supprime-les à la main, sans suivre les liens.",
                                  L"\n\nOn Linux, the launch copies (with links to the game) are kept: delete them by hand, without following the links.");
    MessageBoxW(g_wnd, msg.c_str(), ProductName(), MB_OK | (ok ? MB_ICONINFORMATION : MB_ICONWARNING));
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
        if (g_lateSync) { LateFail(T(L"Réception des mods de l'hôte annulée", L"Receiving the host's mods cancelled")); break; }
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
    case B_THEME: ThemeSet(g_dark ? THEME_CLAIR : THEME_SOMBRE); break;   // (lune / soleil : sombre <-> clair ; les autres themes dans Reglages > Theme)
    case B_LANG: SetLang(g_fr ? L"en" : g_ru ? L"ar" : g_ar ? L"fr" : L"ru"); break;   // FR -> EN -> RU -> AR -> FR
    case B_BUY: ShellExecuteW(g_wnd, L"open", StoreUrl().c_str(), NULL, NULL, SW_SHOWNORMAL); break;
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
    VhsTick();   // (media.inc : video importee sur une cassette)
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
        SkipUnityDialog();
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
    // Images par seconde : 60 quand on s'en sert (souris, clavier, glisser, lancement), 30 au repos, 6 en arriere-plan ;
    // partie en cours : 2, et 1 si le lanceur n'est pas devant (retour d'un hote, 18/10 : saccades en jeu, et dans d'autres
    // jeux lances avec le lanceur ouvert -- il rendait 60 images/s en GDI+ pendant toute la partie, ST_RUNNING comptant
    // comme « actif »). Chaque image = toute la fenetre (1320x840, verre, neige) : c'est le nombre d'images qui coute.
    static unsigned frameN;
    frameN++;
    bool front = GetForegroundWindow() == g_wnd;
    bool active = g_carDrag || g_skinDrag || now - g_inputT < 1500 || (g_state != ST_IDLE && g_state != ST_RUNNING) || g_alpha < 1 || g_lobby == LB_CONNECTING;
    unsigned every = g_state == ST_RUNNING && now - g_inputT >= 1500 ? (front ? 30 : 60) : active ? 1 : front ? 2 : 10;
    if (!active && g_aurore == 0) every = front ? 30 : 60;   // (Reglages > Theme > Aurore et neige : Off = fond fige, rien ne bouge : 2 images/s suffisent)
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
    if (g_state != ST_IDLE || g_goWait) return -1;
    if (g_sCodeAsk && g_tab == TAB_LOBBY) return g_fields[2].r.Width > 0 && g_fields[2].r.Contains(x, y) ? 2 : -1;   // (salon Steam prive : le code)
    if (g_lobby != LB_NONE || g_tab != TAB_HOME) return -1;   // (pendant un salon : pseudo et adresse figes)
    for (int i = 0; i < 3; i++) if (g_fields[i].r.Width > 0 && g_fields[i].r.Contains(x, y) && !(i >= 1 && g_steamNet)) return i;   // (Steam : ni adresse ni code)
    return -1;
}

static void TypeChar(wchar_t ch)
{
    if (g_focus < 0) return;
    Field &f = g_fields[g_focus];
    if (f.text.size() >= f.maxLen) return;
    if (f.code) { if (!(iswalnum(ch) && ch < 128)) return; ch = towupper(ch); }   // code de session : lettres et chiffres, en majuscules
    else if (f.address) { if (!(iswalnum(ch) && ch < 128) && ch != L'.' && ch != L':' && ch != L'-' && ch != L'_') return; }
    else if (ch < 32 || ch > 126) return;   // pseudo : ASCII
    f.text += ch;
}

static LRESULT CALLBACK WndProc(HWND h, UINT m, WPARAM wp, LPARAM lp)
{
    if ((m >= WM_MOUSEFIRST && m <= WM_MOUSELAST) || m == WM_KEYDOWN || m == WM_CHAR) g_inputT = GetTickCount();
    switch (m) {
    case WM_TIMER:
        if (wp == 77) { KillTimer(h, 77); TestLog("test : fin (hote garde ouvert)"); DestroyWindow(h); return 0; }
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
        g_lobbyOptGo = true;   // (options de l'hote et les miennes dans lancement.ini)
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
    case WM_APP_LATE:   // invite en retard (salon IP) : hors du salon, la reception continue, le jeu partira a la fin (SyncTick)
        if ((int)lp != g_lobbyGen) return 0;
        g_lobby = LB_NONE;
        if (g_tab == TAB_LOBBY) g_tab = TAB_HOME;
        LateBegin(false);
        SetStatus(K_NORMAL, T(L"La partie est déjà lancée : réception des mods de l'hôte, puis tu entres en jeu", L"The game has already started: receiving the host's mods, then you join"));
        return 0;
    case WM_APP_LATEEND:
        if ((int)lp != g_lobbyGen || !g_lateSync || g_lateSteam) return 0;
        LateFail(T(L"Connexion à l'hôte coupée avant la fin de ses mods : « Rejoindre en jeu » sans eux, ou réessaie", L"Connection to the host lost before its mods were done: \"Join in game\" without them, or try again"));
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
                VersionMismatch(Widen(why.substr(8)));
            }
            else if (!why.compare(0, 4, "full")) SetStatus(K_ERR, T(L"Salon complet (%d joueurs)", L"Lobby is full (%d players)"), why.size() > 5 ? atoi(why.c_str() + 5) : 8);
            else if (why == "code") { SetStatus(K_ERR, T(L"Code de session incorrect : demande-le à l'hôte (salon privé)", L"Wrong session code: ask the host for it (private lobby)")); g_focus = 2; g_time = 0; }
            else if (why == "started") { SetStatus(K_WARN, T(L"Partie d\u00E9j\u00E0 lanc\u00E9e : \u00AB Rejoindre en jeu \u00BB", L"Session already started: \"Join in game\"")); fallback = true; }
            else if (why == "closed") SetStatus(K_WARN, T(L"L'h\u00F4te a ferm\u00E9 le salon", L"The host closed the lobby"));
            else if (why == "kicked") SetStatus(K_WARN, T(L"L'hôte t'a mis dehors du salon", L"The host kicked you out of the lobby"));
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
    case WM_SYSKEYDOWN:   // (Alt, F10 : touches en jeu)
        if (GameKeyCapture(wp, lp)) return 0;
        break;
    case WM_XBUTTONDOWN:   // (boutons 4 et 5 de la souris : touches en jeu)
        if (g_keyCapture >= 0 && GameKeyCapture(GET_XBUTTON_WPARAM(wp) == XBUTTON1 ? VK_XBUTTON1 : VK_XBUTTON2, 0)) return TRUE;
        break;
    case WM_KEYDOWN:   // page TENUE : fleches gauche / droite (hors des champs)
        if (GameKeyCapture(wp, lp)) return 0;
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
            if (g_tab == TAB_SKIN && SkinWheel(x, y, (short)HIWORD(wp))) return 0;   // (apercu du personnage : zoom)
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
        g_chatFocus = false;   // (le champ du tchat le reprend si c'est lui qui est clique)
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
    case WM_DROPFILES: ContentDrop((HDROP)wp); return 0;   // (modsync.inc : pistes .ogg et images deposees, page Contenu envoye)
    case WM_CHAR:
        if (SaveChar((wchar_t)wp)) return 0;  // (saves.inc : nom d'une sauvegarde, fenetre)
        if (VhsChar((wchar_t)wp)) return 0;   // (media.inc : titre d'une cassette)
        if (SetChar((wchar_t)wp)) return 0;   // (gfx.inc : champ Chercher des reglages)
        if (GuideChar((wchar_t)wp)) return 0;   // (wiki.inc : champ Chercher du guide)
        if (LobbyChatChar((wchar_t)wp)) return 0;   // (salon.inc : « Message au salon »)
        if (g_state != ST_IDLE || (g_lobby != LB_NONE && !g_sCodeAsk) || g_goWait || g_guide || g_tuto >= 0) return 0;
        if (g_focus == 1 && wp != 9 && wp != 13 && wp != 27) g_joinFallback = false;   // autre adresse : on retente le salon
        if (wp == 8) { if (g_focus >= 0 && !g_fields[g_focus].text.empty()) g_fields[g_focus].text.pop_back(); }
        else if (wp == 127) { if (g_focus >= 0) g_fields[g_focus].text.clear(); }   // Ctrl+Retour arriere
        else if (wp == 22 && g_focus >= 0 && OpenClipboard(h)) {   // Ctrl+V
            HANDLE d = GetClipboardData(CF_UNICODETEXT);
            const wchar_t *s = d ? (const wchar_t *)GlobalLock(d) : NULL;
            if (s) { for (; *s && *s != L'\r' && *s != L'\n'; s++) TypeChar(*s); GlobalUnlock(d); }
            CloseClipboard();
        } else if (wp == 9) { g_focus = g_sCodeAsk ? 2 : g_steamNet ? 0 : (g_focus + 1) % 3; g_time = 0; }   // (pseudo, adresse, code)
        else if (wp == 13) {
            if (g_sCodeAsk) SteamCodeSubmit();
            else if (g_focus == 1 || g_focus == 2) { UpdateButtons(); if (g_btn[B_JOIN].enabled) OnButton(B_JOIN); }
            else if (g_focus == 0) { g_focus = 1; g_time = 0; }
        }
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
    g_ru = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_RUSSIAN;
    g_ar = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_ARABIC;
    wchar_t self[MAX_PATH];
    GetModuleFileNameW(NULL, self, MAX_PATH);
    g_self = self;
    WineDetect();   // (Linux / Steam Deck par Proton : avant tout le reste)
    g_dir = DirOf(g_self);
    g_iniLauncher = g_dir + L"mwcoop-lanceur.ini";
    {   // Steam met ses variables dans l'environnement du programme qu'il lance (avant tout SteamAPI_Init d'ici)
        wchar_t v[64];
        const wchar_t *vars[] = { L"SteamGameId", L"SteamOverlayGameId", L"SteamClientLaunch", L"SteamAppId" };
        for (const wchar_t *n : vars) if (GetEnvironmentVariableW(n, v, 64)) g_fromSteam = true;
        int ac = 0;
        wchar_t **av = CommandLineToArgvW(GetCommandLineW(), &ac);
        bool cli = ac >= 2 && av[1][0] == L'/';
        GamePick();
        for (int i = 1; i < ac; i++) {   // %command% : le chemin de l'exe du jeu en argument (ce jeu-la, pour cette fois)
            int gm = GameOfExe(av[i]);
            if (gm >= 0) { g_fromSteam = true; g_game = gm; }
            if (!_wcsicmp(av[i], L"/msc")) g_game = GAME_MSC;   // (essais : /capture ... /msc)
            if (!_wcsicmp(av[i], L"/exportcarte") && i + 1 < ac) g_game = !_wcsicmp(av[i + 1], L"msc") ? GAME_MSC : GAME_MWC;
        }
        // Lancer par Steam (Reglages > Jeu) avec l'option de lancement "<MWCoop.exe>" %command% : Steam relance le lanceur au
        // lieu du jeu (retour d'un joueur, 08/10 : "Running" dans Steam, un 2e lanceur, rien ne demarrait). Demande toute
        // fraiche du lanceur ouvert (lancer-par-steam.txt, moins de 90 s) : on demarre le jeu que Steam nous passe, comme
        // Steam l'aurait fait (son environnement), et on s'efface.
        if (!cli) {
            int gi = -1;
            for (int i = 1; i < ac && gi < 0; i++) if (GameOfExe(av[i]) >= 0) gi = i;
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
                if (IsWine()) LaunchLog("Wine %s (%s) : Linux par Proton ou Wine -- pas de pare-feu ni de compatibilite Windows, depart du dossier du jeu par defaut", g_wine.c_str(), g_wineInfo.c_str());
                WineOverrides("demarrage");   // (registre du prefixe : le bouton JOUER de Steam charge MWCoop meme sans option)
            }
        }
        LocalFree(av);
    }
    DeleteFileW((g_self + L".old").c_str());   // reste d'une mise a jour du lanceur
    OldFilesCleanup(g_dir);   // (chargeurs verrouilles pendant une mise a jour : plus utilises, sauf jeu ouvert ou bloque)
    // Langue : francais, russe ou arabe si Windows l'est, anglais pour toute autre langue ; Langue=fr|en|ru|ar pour forcer.
    wchar_t lang[8] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Langue", L"", lang, 8, g_iniLauncher.c_str());
    if (!_wcsicmp(lang, L"fr") || !_wcsicmp(lang, L"en") || !_wcsicmp(lang, L"ru") || !_wcsicmp(lang, L"ar")) { g_fr = !_wcsicmp(lang, L"fr"); g_ru = !_wcsicmp(lang, L"ru"); g_ar = !_wcsicmp(lang, L"ar"); }
    {
        wchar_t rn[16];
        GetPrivateProfileStringW(L"Lanceur", L"Reseau", L"ip", rn, 16, g_iniLauncher.c_str());
        g_steamNet = _wcsicmp(rn, L"steam") == 0;
        g_mscOn = GetPrivateProfileIntW(L"Lanceur", L"MSCLoader", 1, g_iniLauncher.c_str()) != 0;
        g_debug = GetPrivateProfileIntW(L"Lanceur", L"Debug", 0, g_iniLauncher.c_str()) != 0;
        if (g_debug) DebugSys();
        g_mscOwnPref = GetPrivateProfileIntW(L"Lanceur", L"MSCLoaderMWCoop", -1, g_iniLauncher.c_str());
        ThemeLoad();   // theme (Theme=sombre|clair|aurore|garage, sinon celui de Windows), couleur d'accent, aurore, verre, densite, texte : gfx.inc
    }

    int argc = 0;
    wchar_t **argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    for (int i = 1; i + 1 < argc; i++) {
        if (!_wcsicmp(argv[i], L"/lang")) { g_fr = !_wcsicmp(argv[i + 1], L"fr"); g_ru = !_wcsicmp(argv[i + 1], L"ru"); g_ar = !_wcsicmp(argv[i + 1], L"ar"); }
        // captures : theme, verre, densite et taille du texte sans toucher a l'ini (/theme sombre|clair|aurore|garage, /verre 0|1, /densite 0|1|2, /texte 80..110)
        if (!_wcsicmp(argv[i], L"/theme")) { int t = ThemeFromName(argv[i + 1]); if (t >= 0) ThemeSelect(t); }
        if (!_wcsicmp(argv[i], L"/verre")) g_glass = _wtoi(argv[i + 1]) != 0;
        if (!_wcsicmp(argv[i], L"/densite")) g_densite = max(0, min(2, _wtoi(argv[i + 1])));
        if (!_wcsicmp(argv[i], L"/texte")) g_texte = max(80, min(110, _wtoi(argv[i + 1])));
        if (!_wcsicmp(argv[i], L"/echelle")) g_scale = (float)_wtof(argv[i + 1]);   // captures : rendu agrandi
        if (!_wcsicmp(argv[i], L"/depot")) g_repo = argv[i + 1];
        if (!_wcsicmp(argv[i], L"/testsalon") && i + 2 < argc) { g_testSalon = argv[i + 1]; g_testSalonLog = argv[i + 2]; }
        if (!_wcsicmp(argv[i], L"/garder")) g_testHoldMs = (DWORD)_wtoi(argv[i + 1]);
        if (!_wcsicmp(argv[i], L"/lent")) g_testSlowMs = (DWORD)_wtoi(argv[i + 1]);
        if (!_wcsicmp(argv[i], L"/forcer")) g_testForce = true;
        if (!_wcsicmp(argv[i], L"/partie")) g_testPartie = argv[i + 1];
        if (!_wcsicmp(argv[i], L"/code")) g_testCode = Narrow(argv[i + 1]);   // (salon prive : code de l'hote ; code envoye par l'invite)
        if (!_wcsicmp(argv[i], L"/public") || !_wcsicmp(argv[i + 1], L"/public")) g_testPublic = true;
        if (!_wcsicmp(argv[i + 1], L"/sansudp")) g_testNoUdp = true;   // (dernier argument)
        if (!_wcsicmp(argv[i], L"/temps")) g_sceneT = (float)_wtof(argv[i + 1]);   // captures : instant de la scene animee
        if (!_wcsicmp(argv[i], L"/images")) g_bench = _wtoi(argv[i + 1]);
        if (!_wcsicmp(argv[i], L"/sauter")) g_benchSkip = _wtoi(argv[i + 1]);
        if (!_wcsicmp(argv[i], L"/skins")) g_skinsArg = WithSlash(argv[i + 1]);   // images des tenues de test
        // Steam : invitation acceptee jeu ferme, lanceur demarre par l'option de lancement ("<MWCoop.exe>" %command%)
        if (!_wcsicmp(argv[i], L"+connect_lobby")) { g_sConnect = _wcstoui64(argv[i + 1], NULL, 10); g_steamNet = true; }
        if (!_wcsicmp(argv[i], L"+apres-installation")) g_afterInstall = argv[i + 1];   // (install.inc : lanceur installe dans AppData)
        if (!_wcsicmp(argv[i], L"/steam")) { g_testSteamFile = argv[i + 1]; g_steamNet = true; }   // essai : /testsalon ... /steam <fichier>
    }
    // Essai : /testmaj <releases.json> <journal> <version locale> -- version choisie et a installer ou non
    if (argc >= 5 && !_wcsicmp(argv[1], L"/testmaj")) {
        std::vector<unsigned char> d;
        ReadAll(argv[2], d);
        std::vector<Note> rel = ParseReleases(std::string(d.begin(), d.end()));
        std::string o;
        for (const Note &n : rel) o += Narrow(n.ver, CP_UTF8) + (n.pre ? " pre" : "") + (n.zip.empty() ? " sans paquet" : " " + Narrow(n.zip, CP_UTF8)) + "\r\n";
        {
            const Note *p = PickRelease(rel);
            std::wstring v = p ? p->ver : L"";
            o += "choisie : " + Narrow(v, CP_UTF8) + (p && WantVer(v, argv[4]) ? " a installer" : " rien") + " ; notes " + std::to_string(ChannelNotes(rel).size()) + "\r\n";
        }
        HANDLE f = CreateFileW(argv[3], GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
        if (f != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(f, o.data(), (DWORD)o.size(), &w, NULL); CloseHandle(f); }
        return 0;
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
    g_gdiplusUp = true;   // (fenetres de message au style du lanceur : dialog.inc)
    TextProbe();
    EmbedFontLoad();
    Layout();
    BuildOptions();

    SetGame(FindGame());
    if (!g_gameDir.empty()) PersoMigrate();
    // /lireimage <image> <journal> : lecture d'une image comme l'import du visage (essais : GDI+, puis WIC)
    if (argc >= 4 && !_wcsicmp(argv[1], L"/lireimage")) {
        std::wstring why;
        Bitmap *b = LoadAnyImage(argv[2], &why);
        FILE *f = _wfopen(argv[3], L"w, ccs=UTF-8");
        if (f) { if (b) fwprintf(f, L"ok %ux%u\n", b->GetWidth(), b->GetHeight()); else fwprintf(f, L"echec : %s\n", why.c_str()); fclose(f); }
        delete b;
        GdiplusShutdown(gtok);
        return b ? 0 : 1;
    }
    // /jeu <journal> : jeu trouve (dossier, version, mod)
    if (argc >= 3 && !_wcsicmp(argv[1], L"/jeu")) {
        FILE *f = _wfopen(argv[2], L"w, ccs=UTF-8");
        if (f) { fwprintf(f, L"jeu=%s\nversion=%s\nmod=%d local=%s\nsteam=%d\n", g_gameDir.c_str(), g_gameVer.c_str(), (int)g_modOk, g_localVer.c_str(), (int)g_fromSteam); fclose(f); }
        GdiplusShutdown(gtok);
        return g_gameDir.empty() ? 1 : 0;
    }
    // /exportcarte <msc|mwc> <dossier> : editeur de carte (demande de JD, 19/10 ; editor\, mod : MapExport.cs). Lancement solo
    // sans fenetre du lanceur : profil a part « carte-export » (nouvelle partie, jamais les sauvegardes du joueur), sans
    // MSCLoader (la carte du jeu seule), jeu hors de l'ecran ; le mod ecrit le cache dans <dossier> puis ferme le jeu. Ici :
    // <dossier>\lanceur.txt (ok=1, ou erreur=...), code 0 si le jeu est parti.
    if (argc >= 4 && !_wcsicmp(argv[1], L"/exportcarte")) {
        g_exportDir = argv[3];
        SHCreateDirectoryExW(NULL, g_exportDir.c_str(), NULL);
        std::string why;
        DWORD pid = 0;
        if (g_gameDir.empty()) why = "jeu introuvable";
        else if (!g_modOk) why = "MWCoop absent du jeu (lance le lanceur une fois)";
        else if (GameProcessRunning(&pid)) why = "jeu-ouvert";
        else {
            WNDCLASSEXW wc = { sizeof(wc) };
            wc.lpfnWndProc = WndProc;
            wc.hInstance = inst;
            wc.lpszClassName = L"MWCoopExport";
            RegisterClassExW(&wc);
            g_wnd = CreateWindowExW(0, wc.lpszClassName, L"MWCoop", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, inst, NULL);
            g_mscOn = false;
            Launch(MODE_SOLO, "nouvelle");
            if (g_state != ST_LAUNCH) why = g_status.empty() ? "lancement impossible" : Narrow(g_status, CP_UTF8);
        }
        std::string txt = why.empty() ? "ok=1\r\n" : "erreur=" + why + "\r\n";
        HANDLE f = CreateFileW((WithSlash(g_exportDir) + L"lanceur.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
        if (f != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(f, txt.data(), (DWORD)txt.size(), &w, NULL); CloseHandle(f); }
        GdiplusShutdown(gtok);
        WSACleanup();
        return why.empty() ? 0 : 1;
    }
    g_logo = LoadPngRes(g_game == GAME_MSC ? 9 : 4);   // (logo de la barre de navigation : logo-titre.png, logo-msc.png sur My Summer Car)
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
        bool ok = !IsWine() && FirewallAllow(apps);   // (Wine : pas de pare-feu de Windows)
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
        } else if (!g_gameDir.empty() && !_wcsicmp(argv[2], L"installer-mwcoop")) {   // (le MSCLoader de MWCoop, edition du jeu : 19/10)
            MscInstallThread((void *)1);
            ok = g_mscInstall == 2;
            TestLog("mscl : MSCLoader de MWCoop %s, dossier %s, %s", ok ? "installe" : "ECHEC", Narrow(MscPackDir(), CP_UTF8).c_str(), g_mscInstallWhy.c_str());
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
    // /compat <exe> <journal> : retrait des correctifs de compatibilite de Windows pour cet exe (comme avant un lancement)
    if (argc >= 4 && !_wcsicmp(argv[1], L"/compat")) {
        g_testSalonLog = argv[3];
        FILE *f = _wfopen(argv[3], L"wb");
        if (f) fclose(f);
        int n = CompatClean(argv[2], "essai");
        TestLog("compat : %d retire(s)", n);
        GdiplusShutdown(gtok);
        return 0;
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

    // Auto-test de la liste des mods en plusieurs messages
    if (argc >= 2 && !_wcsicmp(argv[1], L"/testmodsx")) { if (argc >= 4) g_testSalonLog = argv[3]; return OfferSelfTest(argc >= 3 ? _wtoi(argv[2]) : 4000); }

    // /boite <png> <ok|ouinon|ouinonannuler> <info|question|attention|erreur> : une fenetre de message (dialog.inc) en image
    if (argc >= 5 && !_wcsicmp(argv[1], L"/boite")) {
        std::wstring k = argv[3], ic = argv[4];
        UINT t = (k == L"ouinon" ? MB_YESNO : k == L"ouinonannuler" ? MB_YESNOCANCEL : MB_OK)
               | (ic == L"question" ? MB_ICONQUESTION : ic == L"attention" ? MB_ICONWARNING : ic == L"erreur" ? MB_ICONERROR : MB_ICONINFORMATION);
        std::wstring txt = T(L"MWCoop s'installe maintenant dans AppData\\Local\\MWCoop, avec un raccourci « MWCoop » sur le bureau : un seul lanceur pour My Winter Car et My Summer Car.\n\nInstaller maintenant ? (Non : plus tard, le lanceur marche comme avant.)",
                             L"MWCoop now installs into AppData\\Local\\MWCoop, with an “MWCoop” shortcut on the desktop: one launcher for My Winter Car and My Summer Car.\n\nInstall now? (No: later, the launcher works as before.)");
        Bitmap *b = StyledBoxCapture(txt.c_str(), L"MWCoop", t);
        CLSID png;
        int rc = b && EncoderClsid(L"image/png", &png) && b->Save(argv[2], &png, NULL) == Ok ? 0 : 1;
        delete b;
        GdiplusShutdown(gtok);
        return rc;
    }
    // Capture d'un etat, sans fenetre (verification du rendu)
    if (argc >= 4 && !_wcsicmp(argv[1], L"/capture")) {
        std::wstring st = argv[3];
        g_alpha = 1;
        g_time = 0.3f;
        if (g_localVer.empty()) g_localVer = L"0.1.0";
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
        else if (st == L"coop" || st == L"reglages") { g_tab = TAB_COOP; GfxLoad(); SetEnter(TAB_COOP); }   // (page Reglages, onglet Co-op)
        else if (!wcsncmp(st.c_str(), L"reglages-", 9)) { g_tab = TAB_COOP; GfxLoad(); SetCaptureState(st); }   // reglages-jeu|image|son|coop|touches|theme|lanceur|recherche
        else if (st == L"contenu") { g_tab = TAB_CONTENT; g_contentHot = 5004; MscScan(); }   // contenu envoye
        else if (st == L"guide-recherche") { g_tab = TAB_WIKI; g_wikiSearch = L"mods"; g_wikiFocus = true; }   // (champ Chercher de l'en-tete)
        else if (st == L"guide-credits") g_tab = TAB_CREDITS;   // (pilule Credits du Guide)
        else if (st == L"album-partie" || st == L"album-favoris") {   // (Album : Par partie, Favoris -- les deux premieres photos, en memoire)
            g_tab = TAB_ALBUM; AlbScan(); g_albView = st == L"album-partie" ? 1 : 2;
            if (g_albView == 2) { for (size_t i = 0; i < g_alb.size() && i < 2; i++) g_albFav.insert(g_alb[i].name); if (!g_alb.empty()) g_albSel = 0; }
        }
        else if (st == L"voiture") { g_tab = TAB_CAR; }   // couleur : CouleurVoiture du mwcoop.ini du jeu
        else if (st == L"tenue" || st == L"tenue-survol" || st == L"tenue-perso" || st == L"visage" || st == L"volant") {   // tenue : Apparence du mwcoop.ini ; angle : /temps (un tour en 10 s) ; visage : onglet Visage ; volant : vue Volant
            g_tab = TAB_SKIN;
            g_skinYaw = fmodf(g_sceneT * 1.6f, 16.0f);
            if (st == L"tenue-survol") { g_skinRowHot = HT_TILE + 2; g_lookZone = 1; }   // (3e tuile des hauts survolee)
            if (st == L"visage") g_persoTab = 1;
            if (st == L"volant") g_persoView = PV_VOLANT;
            if (st == L"tenue-perso") { g_skinRowHot = 1; g_skinYaw = (float)_wtof(argv[argc - 1]); }   // (dernier argument : la vue 0..16)
        }
        else if (st == L"notes" || st == L"accueil-notes" || st == L"guide" || st == L"guide-nouveautes") {   // notes d'exemple (le depot n'a pas encore de release) ; accueil-notes : la carte Nouveautes de l'accueil ; guide : la colonne Nouveautes ; guide-nouveautes : la pilule du Guide
            // (passees par ParseReleases, comme une vraie release : francais --- anglais --- russe)
            // (/msc : une note de 1.1.1 en plus, melangee : ChannelNotes n'en garde que les puces de MSC, cache 0.1.x et ajoute
            // l'entree « My Summer Car (BETA) »)
            if (g_game == GAME_MSC) g_notes = ChannelNotes(ParseReleases("[{\"tag_name\":\"v1.1.1\",\"published_at\":\"2026-10-20T10:00:00Z\",\"body\":\"- My Summer Car : la SATSUMA garde sa couleur chez l'invité.\\n- VAZ-2108 : le moteur ne cale plus.\\n- [MSC] Le bateau suit l'hôte.\\n- Salon : le code de session se copie mieux.\\n- MWC : la Rivett se vend à nouveau.\\n---\\n- My Summer Car: the SATSUMA keeps its colour for the guest.\\n- VAZ-2108: the engine no longer stalls.\\n- [MSC] The boat follows the host.\\n- Lobby: the session code copies better.\\n- MWC: the Rivett is for sale again.\"},"
                "{\"tag_name\":\"v0.1.1\",\"published_at\":\"2026-10-09T10:00:00Z\",\"body\":\"- Exemple de note de version (capture).\"}]"));
            else g_notes = ChannelNotes(ParseReleases("[{\"tag_name\":\"v0.1.1\",\"published_at\":\"2026-10-09T10:00:00Z\",\"body\":\"- Exemple de note de version (capture).\\n- Deuxième ligne : une correction.\\n---\\n- Sample release note (capture).\\n- Second line: a fix.\\n---\\n- Пример заметки к версии (снимок).\\n- Вторая строка: исправление.\"},{\"tag_name\":\"v0.1.0\",\"published_at\":\"2026-10-02T10:00:00Z\",\"body\":\"- Première version : chargeur, joueurs visibles.\\n---\\n- First version: loader, visible players.\"}]"));
            g_notesDone = true; g_relState = REL_OK; g_tab = st == L"accueil-notes" ? TAB_HOME : st == L"guide" ? TAB_WIKI : TAB_NOTES;
            if (st == L"guide") g_wikiSel = 1;
        }
        else if (st == L"notesvide") { NotesOnlyThread(NULL); g_tab = TAB_NOTES; }
        else if (st == L"notes-image") {   // une note avec une image (telechargee ici, avant la capture)
            std::wstring url = L"https://raw.githubusercontent.com/" + g_repo + L"/main/docs/img/lanceur-tenue.png";
            std::wstring fr = CleanNote(L"- Volet de droite pour la tenue :\n![tenue](" + url + L")\n- Et une ligne apr\u00E8s l'image.");
            g_notes = { { L"0.36.0", L"08/10/2026", fr, fr, L"" },
                        { L"0.1.0", L"02/10/2026", L"\u2022 Premi\u00E8re version.", L"\u2022 First version.", L"" } };
            NoteImgFetch(url);
            g_notesDone = true; g_relState = REL_OK; g_tab = TAB_NOTES;
        }
        else if (st == L"journaux") { g_tab = TAB_LOGS; LogsScan(); g_logRowHot = 0; }
        else if (st == L"api") g_tab = TAB_API;   // page API des moddeurs
        // page OUTILS (outils.inc) : editeur pas installe (version publiee factice), installe, a mettre a jour, en telechargement
        else if (!wcsncmp(st.c_str(), L"outils", 6)) {
            g_tab = TAB_TOOLS; g_notesDone = true; g_relState = REL_OK;
            g_edRemoteVer = st == L"outils-maj" ? L"1.2.1" : L"1.2.0"; g_edRemoteUrl = L"https://example.invalid/EditeurCarte.editeur";
            if (st == L"outils-telechargement") { g_edBusy = true; g_edProg = 0.42f; }
            if (st == L"outils-installe" || st == L"outils-maj") g_edFakeLocal = L"1.2.0";
            if (st == L"outils-survol") g_uiHot = UI_TOOL_GET;
        }
        else if (st == L"vhs") { g_tab = TAB_VHS; VhsScan(); }        // pages MEDIA (media.inc)
        else if (st == L"vhs1") { g_tab = TAB_VHS; VhsScan(); g_vhsSel = 0; g_pvStart = GetTickCount() - 4000; }   // (cassette choisie, apercu a 4 s)
        else if (st == L"album") { g_tab = TAB_ALBUM; AlbScan(); }
        else if (st == L"sauvegardes") { g_tab = TAB_SAVES; SavesFake(); }   // (saves.inc : parties factices ; fenetres : -nouvelle, -choix, -nom, -recommencer)
        else if (st == L"sauvegardes-nouvelle") { g_tab = TAB_SAVES; SaveOpenNewGame(); }
        else if (st == L"sauvegardes-choix") { g_tab = TAB_SAVES; SaveOpenPick(); }
        else if (st == L"sauvegardes-recommencer") { g_tab = TAB_SAVES; SavesScan(); g_saveModal = SM_RESTART; }
        else if (st == L"sauvegardes-nom") { g_tab = TAB_SAVES; SaveAskName(true); }
        else if (st == L"credits") g_tab = TAB_CREDITS;
        else if (st == L"credits-images" || st == L"credits-merci" || st == L"credits-supporters") { g_tab = TAB_CREDITS; g_credTab = st == L"credits-images" ? 1 : st == L"credits-merci" ? 2 : 3; }
        else if (!wcsncmp(st.c_str(), L"guide", 5) && st != L"guide-steam") {   // guide : guide-<article>-<defilement>
            g_tab = TAB_WIKI;
            int art = 0, sc = 0;
            swscanf_s(st.c_str(), L"guide-%d-%d", &art, &sc);
            g_wikiSel = art;
            g_scroll[TAB_WIKI] = (float)sc;
        }
        else if (st == L"graphismes" || st == L"graphismes-video") { g_tab = TAB_GFX; GfxLoad(); SetEnter(TAB_GFX); }   // (page Reglages, onglet Jeu)
        else if (st == L"graphismes-ajouts") { g_tab = TAB_GFX; GfxLoad(); g_setTab = SET_COOP; }      // (ajouts COOP : onglet Co-op)
        else if (st == L"graphismes-drone" || st == L"graphismes-bas") { g_tab = TAB_GFX; GfxLoad(); g_setTab = SET_TOUCHES; }   // (camera drone, touches)
        else if (st == L"graphismes-rt") { g_tab = TAB_GFX; GfxLoad(); g_setTab = SET_IMAGE; }         // (ray tracing et ombres)
        else if (st == L"api-direct") { g_tab = TAB_API; g_apiEx = 1; }
        else if (st == L"salon" || st == L"salon-invite" || st == L"salon-invite-hote" || st == L"salon-udp" || st == L"salon-options" || st == L"salon-dl" || st == L"salon-public" || st == L"salon-max" || st == L"salon-moi" || st == L"salon-choisir" || !wcsncmp(st.c_str(), L"salon-mods", 10)) {   // salon a 3 joueurs (faux), vu par l'hote ou par un invite (salon-invite-hote : ses options de l'hote)
            bool host = st == L"salon" || st == L"salon-options" || st == L"salon-mods" || st == L"salon-dl" || st == L"salon-public" || st == L"salon-max" || st == L"salon-moi" || st == L"salon-choisir";   // (salon-udp : invite dont l'UDP est bloque ; salon-dl : Kalle recoit les mods ; salon-public : sans code ; salon-max : 12 joueurs ; salon-moi : mes options ; salon-choisir : ajouts au choix)
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
            LobbyOptImport();   // (salon prive, code, joueurs max, options de la partie : vus par l'hote ou recus par l'invite)
            g_lobbyPrive = st != L"salon-public"; g_lobbyCode = "K7PM3Q"; g_lobbyMax = st == L"salon-max" ? 12 : 8;
            if (st == L"salon-moi") g_lobbyOptTab = 1;
            if (st == L"salon-invite-hote") g_lobbyOptTab = 0;
            if (st == L"salon-choisir") { g_lobbyAjoutsPick = true; g_lobbyOpt["AjoutMusee"] = 0; }
            if (!host) { for (auto &d : kLobbyOptDefs) if (d.host) g_hostOpt[d.key] = d.def; g_hostOpt["Triches"] = 1; g_hostOpt["AjoutMusee"] = 0; }
            g_tab = TAB_LOBBY;
            g_lobbySinceGen = g_lobbyGen; g_lobbySinceT = GetTickCount() - 252000;   // (invite : dans ce salon depuis 4 min 12)
            if (st == L"salon-dl") g_peers[2].dl = 45;
            if (host) g_peers.push_back({ 3, "Jouko", "char_shirt12", v, false, 61, UDP_WAIT });   // (arrive : TCP seulement, sonde UDP pas encore vue)
            LobbyChatClear();   // (tchat : trois lignes, comme la maquette)
            LobbyChatAdd(host ? 1 : 0, host ? "Teppo" : "Pekka", host ? "On part dans 2 min ?" : "Je lance d\xC3\xA8s que tout le monde est pr\xC3\xAAt");
            LobbyChatAdd(host ? 0 : 1, MyName(), host ? "Oui, le temps de finir les options" : "J'arrive !");
            LobbyChatAdd(2, "Kalle", host ? "J'arrive, j'installe les mods" : "Quelqu'un a du caf\xC3\xA9 ?");
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
                g_hostOffer.files = { { "Mods\\BetterHeadlights.dll", 412000, 1 }, { "Mods\\TrunkLight.dll", 96000, 2 }, { "Mods\\CDPlayerEnhanced.dll", 1830000, 3 }, { "Radio\\track1.ogg", 3900000, 4 } };
                g_sync = st == L"salon-mods-demande" ? SY_ASK : st == L"salon-mods-telechargement" ? SY_GET : SY_DONE;
                g_syncNeed = { 0, 1, 2, 3 };
                if (g_sync == SY_GET) g_syncGotFile = { { 0, 412000 }, { 1, 96000 }, { 2, 1116000 }, { 3, 3900000 } };   // (deux mods recus, le troisieme a 61 %, le contenu fini)
                g_syncTotal = 6238000; g_syncGot = 5524000;
                g_meReady = g_sync == SY_DONE;
                if (g_sync == SY_ASK) g_lobbyHot = 70;
            }
        }
        else if (st == L"salon-steam" || st == L"salon-steam-amis" || st == L"salon-steam-invite" || st == L"salon-steam-code") {   // salon Steam (faux)
            bool host = st != L"salon-steam-invite" && st != L"salon-steam-code";   // (salon-steam-code : invite venu de Steam, le code lui est demande)
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
            LobbyOptImport();
            g_lobbyPrive = true; g_lobbyCode = "K7PM3Q"; g_lobbyMax = 8;
            if (!host) for (auto &d : kLobbyOptDefs) if (d.host) g_hostOpt[d.key] = d.def;
            if (st == L"salon-steam-code") { g_lobby = LB_CONNECTING; g_sCodeAsk = true; g_focus = 2; g_fields[2].text = L"K7P"; g_time = 0.2f; }
            LobbyChatClear();
            if (st != L"salon-steam-code") { LobbyChatAdd(1, "Teppo", "On part dans 2 min ?"); LobbyChatAdd(host ? 0 : 1, MyName(), "Oui, le temps de finir les options"); }
            g_tab = TAB_LOBBY;
            g_lobbySinceGen = g_lobbyGen; g_lobbySinceT = GetTickCount() - 252000;
                    if (st == L"salon-steam-amis") {
                g_sFriendsView = true;
                g_sFriends = { { 2, L"Teppo", 1, true }, { 4, L"Jouko", 1, true }, { 5, L"Arska", 1, false }, { 6, L"Fleetari", 3, false }, { 7, L"Suski", 2, false } };
                g_sOffline = 14;
                g_sInvited[4] = GetTickCount();
                g_lobbyHot = 102;
            } else if (host) g_lobbyHot = 51;
            if (host) SetStatus(K_OK, T(L"Salon ouvert · Steam · invite tes amis", L"Lobby open · Steam · invite your friends"));
            else SetStatus(K_OK, T(L"Dans le salon Steam de %s", L"In %s's Steam lobby"), L"Pekka");
        }
        else if (st == L"nouvelle-maj" || st == L"nouvelle-maj-survol" || st == L"nouvelle-maj-notes") {   // version plus recente proposee (bouton)
            UpdSet(L"0.41.0");
            if (st == L"nouvelle-maj-survol") { g_hot = B_UPDATE; g_btn[B_UPDATE].hover = 1; }
            if (st == L"nouvelle-maj-notes") g_tab = TAB_NOTES;
        }
        else if (st == L"maj") { g_busy = true; g_progress = 0.42f; SetStatus(K_NORMAL, T(L"T\u00E9l\u00E9chargement de MWCoop %s\u2026", L"Downloading MWCoop %s\u2026"), L"0.1.1"); g_focus = 0; g_time = 0.2f; }
        else { SetStatus(K_OK, T(L"%s \u00B7 \u00E0 jour", L"%s \u00B7 up to date"), ModLabel().c_str()); g_hot = B_HOST; g_btn[B_HOST].hover = 1; }
        if (g_tab == TAB_HOME) g_skinYaw = fmodf(g_sceneT * 1.6f, 16.0f);   // (accueil : le personnage tourne avec /temps)
        // /vhsimport <ligne> <video> : page VHS, import sans la fenetre de choix, attendu jusqu'au bout (essais)
        // /vhsexport <dossier> : page VHS, export de la cassette choisie (vhs1) dans ce dossier, attendu jusqu'au bout (essais)
        for (int i = 4; i + 1 < argc; i++) if (!_wcsicmp(argv[i], L"/vhsexport")) {
            g_vhsExpDirTest = WithSlash(argv[i + 1]); VhsScan(); VhsExport(max(0, g_vhsSel));
            for (int k = 0; k < 1200 && !g_vhsExpDst.empty(); k++) { Sleep(100); VhsTick(); }
            TestLog("vhsexport : %ls", g_status.c_str());
        }
        for (int i = 4; i + 2 < argc; i++) if (!_wcsicmp(argv[i], L"/vhsimport")) {
            VhsScan(); VhsImport(_wtoi(argv[i + 1]), argv[i + 2]);
            for (int k = 0; k < 1200 && !g_vhsImpFilm.empty(); k++) { Sleep(100); VhsTick(); }
            VhsScan();
        }
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
    // Installation dans %LOCALAPPDATA%\MWCoop (install.inc, 19/10) : proposee a la fenetre normale d'un lanceur qui n'y tourne
    // pas ; installe, il passe la main au lanceur installe. Juste apres : l'ancien a la Corbeille, compte rendu.
    if ((argc < 2 || (argv[1][0] != L'/' && !g_sConnect && !g_fromSteam)) && InstallAtStartup()) { GdiplusShutdown(gtok); WSACleanup(); return 0; }
    if (!g_afterInstall.empty() && RunningFromHome()) AfterInstall(g_afterInstall);
    if (!g_gameDir.empty() && _wcsicmp(g_gameDir.c_str(), g_dir.c_str()) && !RunningFromHome()) {   // (jamais depuis le lanceur installe)
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
    DragAcceptFiles(g_wnd, TRUE);   // (fichiers deposes : WM_DROPFILES, page Contenu envoye)

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
    FirstReworkedBackup();
    if (!g_gameDir.empty()) StartUpdate();
    else {
        HANDLE nt = CreateThread(NULL, 0, NotesOnlyThread, NULL, 0, NULL);
        if (nt) CloseHandle(nt);
    }
    TenuesStart();   // (tenues offertes : une fois, a part des mises a jour)
    CompatCleanAll("demarrage");

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    if (g_proc) CloseHandle(g_proc);
    delete g_bg; delete g_bgDark; delete g_bgCache; SkinsFree(); SteamAvFree();
    GdiplusShutdown(gtok);
    return 0;
}
