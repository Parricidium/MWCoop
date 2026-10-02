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
//  - Heberger / Rejoindre / Jouer en solo : ecrit MWCoop\lancement.ini (lu par le chargeur et le mod, valable
//    3 minutes : Steam peut relancer le jeu sans sa ligne de commande), lance mywintercar.exe avec les memes reglages
//    en arguments (-mwcoop-mode ...), puis reste en ecran d'attente jusqu'a la fenetre du jeu (UnityWndClass).
//  - Options du joueur : MWCoop\mwcoop.ini, section [Coop] (Pseudo, Adresse, Port, Apparence).
//
// Options de ligne de commande (tests, jamais de fenetre) :
//   /capture <png> <menu|coop|notes|notesvide|journaux|attente|maj|sansjeu> [/theme clair|sombre] [/lang fr|en]
//            [/echelle k] : rendu d'un etat dans un PNG ;
//   /maj <dossier du jeu> <journal> [/depot proprietaire/depot] : mise a jour sans fenetre, journal = etat final ;
//   /jeu <journal> : jeu trouve (dossier, version).

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
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
#include <string>
#include <vector>
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
static Bitmap *g_bg, *g_bgDark;
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
}

static std::wstring PlayerName() { std::wstring n = Trim(g_fields[0].text); return n.empty() ? DefaultName() : n; }

static void SavePlayer()
{
    if (g_gameDir.empty()) return;
    EnsureModDir();
    std::string ini = ModIniA();
    WritePrivateProfileStringA("Coop", "Pseudo", Narrow(PlayerName()).c_str(), ini.c_str());
    WritePrivateProfileStringA("Coop", "Adresse", Narrow(Trim(g_fields[1].text)).c_str(), ini.c_str());
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
        SetStatus(K_ERR, T(L"Mise à jour impossible (paquet incomplet : MWCoop\\version.txt manquant)", L"Update failed (incomplete package: MWCoop\\version.txt missing)"));
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
enum { B_HOST, B_JOIN, B_SOLO, B_EXE, B_BUY, B_THEME, B_CLOSE, B_MIN, B_LOGS, B_COUNT };
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
}

static void UpdateButtons()
{
    bool menu = g_state == ST_IDLE, game = !g_gameDir.empty(), busy = g_busy;
    for (int i = 0; i < B_COUNT; i++) g_btn[i].visible = true;
    g_btn[B_HOST].visible = g_btn[B_JOIN].visible = g_btn[B_SOLO].visible = g_btn[B_EXE].visible = menu;
    g_btn[B_HOST].enabled = g_btn[B_JOIN].enabled = g_btn[B_SOLO].enabled = menu && game && !busy && g_modOk;
    g_btn[B_EXE].enabled = menu && !busy;
    g_btn[B_CLOSE].enabled = g_btn[B_MIN].enabled = g_btn[B_BUY].enabled = g_btn[B_THEME].enabled = true;
    g_btn[B_LOGS].visible = menu && game;
    g_btn[B_LOGS].enabled = true;
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
        Text(g, label, r, 15, FontStyleBold, WithA(kOnAcc, a));
    } else {
        SolidBrush fill(Mix(WithA(TH(btn2), a), WithA(TH(btn2Hot), a), b.hover));
        g.FillPath(&fill, &p);
        Pen pen(WithA(kAcc, a), 1.6f);
        g.DrawPath(&pen, &p);
        Text(g, label, r, 15, FontStyleBold, WithA(kAcc, a));
    }
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
enum { TAB_COOP, TAB_NOTES, TAB_LOGS, TAB_COUNT };   // (TAB_LOGS : page du bouton journaux, pas d'onglet)
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
static int g_tab = -1, g_optHot = -1, g_optPart = 0, g_tabHot = -1;
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
        o.dFr = L"Port UDP de la partie (7870 par d\u00E9faut). L'h\u00F4te l'ouvre sur sa box (redirection UDP) ; les invit\u00E9s prennent le m\u00EAme.";
        o.dEn = L"UDP port of the session (7870 by default). The host opens it on their router (UDP forwarding); guests use the same one.";
        g_opts.push_back(o);
    }
    {   // Apparence : materiaux des corps des PNJ du jeu (Sync\Avatar.cs)
        Opt o = {};
        o.tab = TAB_COOP; o.key = "Apparence"; o.kind = O_CHOICE;
        for (int i = 1; i <= 28; i++) {
            char k[32]; sprintf_s(k, "char_shirt%02d", i);
            o.svals.push_back(k);
            o.labFr.push_back(L"Tenue " + std::to_wstring(i));
            o.labEn.push_back(L"Outfit " + std::to_wstring(i));
        }
        const char *extra[] = { "cop_shirt", "rally_shirt", "psk_shirt", "inspector_shirt" };
        const wchar_t *fr[] = { L"Policier", L"Pilote de rallye", L"Employ\u00E9 PSK", L"Inspecteur" };
        const wchar_t *en[] = { L"Police officer", L"Rally driver", L"PSK employee", L"Inspector" };
        for (int i = 0; i < 4; i++) { o.svals.push_back(extra[i]); o.labFr.push_back(fr[i]); o.labEn.push_back(en[i]); }
        for (int i = 0; i < (int)o.svals.size(); i++) o.vals.push_back(i);
        o.def = 20;   // char_shirt21 (defaut du mod)
        o.fr = L"Apparence"; o.en = L"Appearance"; o.suffix = L"";
        o.dFr = L"Le personnage que les autres joueurs voient : la tenue d'un habitant, ou celle du policier, du pilote de rallye…";
        o.dEn = L"The character the other players see: a local's outfit, or the police officer's, the rally driver's…";
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
    static const wchar_t *fr[] = { L"COOP", L"NOUVEAUT\u00C9S", L"JOURNAUX" }, *en[] = { L"CO-OP", L"UPDATES", L"LOGS" };
    return g_fr ? fr[t] : en[t];
}
static bool TabVisible(int t) { return t != TAB_LOGS; }
static void LayoutTabs()
{
    static const int order[] = { TAB_COOP, TAB_NOTES };
    float x = 440, pad = 12, gap = 6;
    Bitmap bm(1, 1);
    Graphics mg(&bm);
    for (int t = 0; t < TAB_COUNT; t++) g_tabR[t] = RectF(0, 0, 0, 0);
    for (int t : order) {
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
static float MaxScroll(int t) { return t == TAB_LOGS ? LogsMaxScroll() : t == TAB_NOTES ? NotesMaxScroll() : max(0.0f, TabRows(t).size() * kRowH - kOptList.Height); }

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

static void DrawOptions(Graphics &g)
{
    if (g_tab < 0 || g_gameDir.empty()) return;
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
        Para(g, T(L"\u2022 H\u00C9BERGER : tu lances ta partie, les autres te rejoignent. Ouvre le port ci-dessus sur ta box "
                  L"(redirection UDP) et donne-leur ton adresse IP publique.\n\n"
                  L"\u2022 REJOINDRE : entre l'adresse IP de l'h\u00F4te \u00E0 gauche (adresse:port si l'h\u00F4te a chang\u00E9 de port).\n\n"
                  L"\u2022 L'invit\u00E9 joue dans un profil \u00E0 part qui re\u00E7oit la sauvegarde de l'h\u00F4te : sa propre sauvegarde "
                  L"n'est pas touch\u00E9e.\n\n"
                  L"\u2022 JOUER EN SOLO : le jeu normal, avec MWCoop charg\u00E9 mais sans r\u00E9seau.",
                  L"\u2022 HOST: you start your game, the others join you. Open the port above on your router (UDP forwarding) "
                  L"and give them your public IP address.\n\n"
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
    if (g_tab < 0 || g_tab == TAB_NOTES || g_tab == TAB_LOGS || !kOptList.Contains(x, y)) return;
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

// ---------------------------------------------------------------- page JOURNAUX (bouton rond)
// Les journaux du chargeur et du mod (MWCoop\logs\chargeur.log, mwcoop.log, remplaces a chaque lancement du jeu),
// ceux du profil invite (MWCoop\profils\invite\logs) et celui de Unity s'il existe (mywintercar_Data\output_log.txt).
// Un clic ouvre le journal, l'icone dossier le montre dans l'explorateur.
enum { LOG_MOD, LOG_LOADER, LOG_UNITY };
struct LogEntry { std::wstring path; int kind; bool guest; uint64_t bytes; FILETIME mt; int errors; };
static std::vector<LogEntry> g_logList;
static int g_logRowHot = -1, g_logPart = 0;          // g_logPart : 0 la ligne (ouvrir), 1 dossier
static const RectF kLogsFolderR(796, 124, 140, 22), kLogsR(452, 152, 488, 374);
static const float kLogRowH = 54;

static std::wstring LogsDir() { return g_gameDir + L"MWCoop\\logs\\"; }

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
    auto add = [&](const std::wstring &p, int kind, bool guest) {
        WIN32_FILE_ATTRIBUTE_DATA a;
        if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a) || (a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) return;
        LogEntry e;
        e.path = p; e.kind = kind; e.guest = guest;
        e.bytes = ((uint64_t)a.nFileSizeHigh << 32) | a.nFileSizeLow;
        e.mt = a.ftLastWriteTime;
        e.errors = kind == LOG_UNITY ? -1 : CountErrors(p, e.bytes);   // (Unity : trop d'exceptions sans gravite)
        list.push_back(e);
    };
    if (!g_gameDir.empty()) {
        add(LogsDir() + L"mwcoop.log", LOG_MOD, false);
        add(LogsDir() + L"chargeur.log", LOG_LOADER, false);
        add(g_gameDir + L"MWCoop\\profils\\invite\\logs\\mwcoop.log", LOG_MOD, true);
        add(g_gameDir + L"MWCoop\\profils\\invite\\logs\\chargeur.log", LOG_LOADER, true);
        add(g_gameDir + L"mywintercar_Data\\output_log.txt", LOG_UNITY, false);
    }
    g_logList.swap(list);
    g_scroll[TAB_LOGS] = min(g_scroll[TAB_LOGS], LogsMaxScroll());
}

static float LogsMaxScroll() { return max(0.0f, g_logList.size() * kLogRowH - kLogsR.Height); }

static std::wstring LogTitle(const LogEntry &e)
{
    std::wstring s = e.kind == LOG_MOD ? T(L"Journal du mod", L"Mod log") : e.kind == LOG_LOADER ? T(L"Journal du chargeur", L"Loader log") : T(L"Journal de Unity", L"Unity log");
    if (e.guest) s += T(L" \u00B7 profil invit\u00E9", L" \u00B7 guest profile");
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
    Text(g, T(L"JOURNAUX", L"LOGS"), RectF(460, 122, 120, 26), 17, FontStyleBold, kInk, StringAlignmentNear);
    Text(g, T(L"Ouvrir le dossier", L"Open folder"), kLogsFolderR, 12, FontStyleUnderline, kInk, StringAlignmentFar);
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
    Para(g, T(L"Un clic ouvre le journal. Un souci en jeu : envoie mwcoop.log et chargeur.log, juste apr\u00E8s la partie "
              L"(ils sont remplac\u00E9s \u00E0 chaque lancement du jeu).",
              L"Click a log to open it. Trouble in game: send mwcoop.log and chargeur.log right after the session "
              L"(they are replaced every time the game starts)."),
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
    if (kLogsFolderR.Contains(x, y)) {
        EnsureModDir();
        CreateDirectoryW(LogsDir().c_str(), NULL);
        ShellExecuteW(g_wnd, L"open", LogsDir().c_str(), NULL, NULL, SW_SHOWNORMAL);
        return true;
    }
    int part, i = LogRowAt(x, y, &part);
    if (i < 0) return kOptPanel.Contains(x, y);
    std::wstring path = g_logList[i].path;
    if (part == 1) {
        std::wstring arg = L"/select,\"" + path + L"\"";
        ShellExecuteW(g_wnd, L"open", L"explorer.exe", arg.c_str(), NULL, SW_SHOWNORMAL);
    } else if ((INT_PTR)ShellExecuteW(g_wnd, L"open", path.c_str(), NULL, NULL, SW_SHOWNORMAL) <= 32) {
        std::wstring arg = L"\"" + path + L"\"";   // (.log sans programme associe)
        ShellExecuteW(g_wnd, L"open", L"notepad.exe", arg.c_str(), NULL, SW_SHOWNORMAL);
    }
    return true;
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
    } else {
        DrawTabs(g);
        DrawOptions(g);
        Text(g, status, RectF(60, 212, 336, 22), 13, FontStyleBold, sc);
        if (prog != -1.0f) DrawBar(g, RectF(96, 238, 264, 5), prog);
        DrawField(g, 0, T(L"PSEUDO", L"NICKNAME"));
        DrawField(g, 1, T(L"ADRESSE DE L'H\u00D4TE", L"HOST ADDRESS"));
        DrawButton(g, B_HOST, T(L"H\u00C9BERGER", L"HOST"), true);
        DrawButton(g, B_JOIN, T(L"REJOINDRE", L"JOIN"), false);
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
        Text(g, el, eb.r, 11.5f, ok ?FontStyleUnderline : FontStyleBold | FontStyleUnderline, WithA(lc, eb.enabled ? 1.0f : 0.4f), StringAlignmentFar);
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

static void RenderTo(Bitmap &target, float scale)
{
    Graphics g(&target);
    g.Clear(Color(0, 0, 0, 0));
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);
    g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
    g.SetPixelOffsetMode(PixelOffsetModeHalf);
    g.ScaleTransform(scale, scale);
    Bitmap *bgi = (g_dark && g_bgDark) ? g_bgDark : g_bg;
    if (bgi) g.DrawImage(bgi, RectF(0, 0, kImgW, kImgH));
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
static bool GameProcessRunning()
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W pe = { sizeof(pe) };
    bool found = false;
    for (BOOL ok = Process32FirstW(snap, &pe); ok && !found; ok = Process32NextW(snap, &pe))
        found = !_wcsicmp(pe.szExeFile, L"mywintercar.exe");
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
static bool WriteLaunchFile(int mode, const std::wstring &addr, int port)
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

static void Launch(int mode)
{
    if (g_gameDir.empty() || g_busy || !g_modOk) return;
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
    } else if (GameProcessRunning()) {   // (instance unique : un 2e jeu hors profil se fermerait aussitot)
        SetStatus(K_ERR, T(L"My Winter Car est d\u00E9j\u00E0 lanc\u00E9 : ferme-le d'abord", L"My Winter Car is already running: close it first"));
        return;
    }
    SavePlayer();
    if (!WriteLaunchFile(mode, addr, port)) {
        SetStatus(K_ERR, T(L"Impossible d'\u00E9crire MWCoop\\lancement.ini (erreur %lu)", L"Could not write MWCoop\\lancement.ini (error %lu)"), GetLastError());
        return;
    }
    static const wchar_t *modes[] = { L"solo", L"hote", L"invite" };
    std::wstring args = std::wstring(L"-mwcoop-mode ") + modes[mode] + L" -mwcoop-port " + std::to_wstring(port);
    if (mode == MODE_GUEST) args += L" -mwcoop-adresse " + addr + L" -mwcoop-profil invite";
    g_preWnds.clear();
    EnumWindows(ListUnityWindows, (LPARAM)&g_preWnds);
    // Lance comme un double-clic dans l'explorateur : un mode de compatibilite de l'exe peut exiger l'administrateur ;
    // CreateProcess echoue alors (erreur 740), ShellExecuteEx affiche la demande de Windows.
    std::wstring exe = g_gameDir + L"mywintercar.exe";
    SHELLEXECUTEINFOW sei = { sizeof(sei) };
    sei.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
    sei.hwnd = g_wnd;
    sei.lpVerb = L"open";
    sei.lpFile = exe.c_str();
    sei.lpParameters = args.c_str();
    sei.lpDirectory = g_gameDir.c_str();
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

static void OnButton(int id)
{
    switch (id) {
    case B_HOST: Launch(MODE_HOST); break;
    case B_JOIN: Launch(MODE_GUEST); break;
    case B_SOLO: Launch(MODE_SOLO); break;
    case B_EXE: ChooseExe(); break;
    case B_CLOSE: g_state = ST_CLOSING; break;
    case B_MIN: ShowWindow(g_wnd, SW_MINIMIZE); break;
    case B_LOGS: g_tab = g_tab == TAB_LOGS ? -1 : TAB_LOGS; g_optHot = -1; if (g_tab == TAB_LOGS) LogsScan(); break;
    case B_THEME: g_dark = !g_dark; WritePrivateProfileStringW(L"Lanceur", L"Theme", g_dark ? L"sombre" : L"clair", g_iniLauncher.c_str()); break;
    case B_BUY: ShellExecuteW(g_wnd, L"open", kStoreUrl, NULL, NULL, SW_SHOWNORMAL); break;
    }
}

static void Tick()
{
    static DWORD last = GetTickCount(), lastScan;
    DWORD now = GetTickCount();
    float dt = min((now - last) / 1000.0f, 0.1f);
    last = now;
    g_time += dt;
    for (int i = 0; i < B_COUNT; i++) {
        float want = (g_hot == i && g_btn[i].enabled) ? 1.0f : 0.0f;
        g_btn[i].hover += (want - g_btn[i].hover) * min(dt * 12, 1.0f);
    }
    if (g_state == ST_CLOSING) {
        g_alpha -= dt * 4;
        if (g_alpha <= 0) { DestroyWindow(g_wnd); return; }
    } else if (g_alpha < 1) g_alpha = min(g_alpha + dt * 5, 1.0f);

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
    Present();
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
    if (g_state != ST_IDLE) return -1;
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
        Tick();
        return 0;
    case WM_MOUSEMOVE: {
        float x = (short)LOWORD(lp) / g_scale, y = (short)HIWORD(lp) / g_scale;
        g_hot = HitButton(x, y);
        g_tabHot = HitTab(x, y);
        HitOption(x, y, &g_optHot, &g_optPart);
        g_logRowHot = g_tab == TAB_LOGS ? LogRowAt(x, y, &g_logPart) : -1;
        TRACKMOUSEEVENT tme = { sizeof(tme), TME_LEAVE, h, 0 };
        TrackMouseEvent(&tme);
        bool link = g_tab == TAB_LOGS && kLogsFolderR.Contains(x, y);
        SetCursor(LoadCursor(NULL, ((g_hot >= 0 && g_btn[g_hot].enabled) || g_tabHot >= 0 || g_optHot >= 0 || g_logRowHot >= 0 || link) ? IDC_HAND
                                   : HitField(x, y) >= 0 ? IDC_IBEAM : IDC_ARROW));
        return 0;
    }
    case WM_MOUSELEAVE: g_hot = -1; g_tabHot = -1; g_optHot = -1; g_logRowHot = -1; return 0;
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
        if (t >= 0) { g_tab = g_tab == t ? -1 : t; g_optHot = -1; if (g_tab == TAB_NOTES) NotesMarkSeen(); return 0; }   // un 2e clic referme
        if (g_tab == TAB_LOGS && LogsMouseDown(x, y)) return 0;
        int row, part;
        HitOption(x, y, &row, &part);
        if (row >= 0) { OptStep(row, part < 0 ? -1 : 1); return 0; }
        if (g_tab >= 0 && kOptPanel.Contains(x, y)) return 0;
        ReleaseCapture();
        SendMessageW(h, WM_NCLBUTTONDOWN, HTCAPTION, 0);   // glisser la fenetre
        return 0;
    }
    case WM_LBUTTONUP: {
        int p = g_pressed;
        g_pressed = -1;
        ReleaseCapture();
        float x = (short)LOWORD(lp) / g_scale, y = (short)HIWORD(lp) / g_scale;
        if (p >= 0 && HitButton(x, y) == p && g_btn[p].enabled) OnButton(p);
        return 0;
    }
    case WM_CHAR:
        if (g_state != ST_IDLE) return 0;
        if (wp == 8) { if (g_focus >= 0 && !g_fields[g_focus].text.empty()) g_fields[g_focus].text.pop_back(); }
        else if (wp == 127) { if (g_focus >= 0) g_fields[g_focus].text.clear(); }   // Ctrl+Retour arriere
        else if (wp == 22 && g_focus >= 0 && OpenClipboard(h)) {   // Ctrl+V
            HANDLE d = GetClipboardData(CF_UNICODETEXT);
            const wchar_t *s = d ? (const wchar_t *)GlobalLock(d) : NULL;
            if (s) { for (; *s && *s != L'\r' && *s != L'\n'; s++) TypeChar(*s); GlobalUnlock(d); }
            CloseClipboard();
        } else if (wp == 9) { g_focus = g_focus == 0 ? 1 : 0; g_time = 0; }
        else if (wp == 13) { if (g_focus == 1) { if (g_btn[B_JOIN].enabled) Launch(MODE_GUEST); } else if (g_focus == 0) { g_focus = 1; g_time = 0; } }
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
    case WM_CLOSE: g_state = ST_CLOSING; return 0;
    case WM_DESTROY:
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
    if (!p) return NULL;
    IStream *s = SHCreateMemStream(p, SizeofResource(NULL, r));
    if (!s) return NULL;
    Bitmap *copy = NULL;
    Bitmap *b = Bitmap::FromStream(s);
    if (b && b->GetLastStatus() == Ok) {
        copy = new Bitmap(b->GetWidth(), b->GetHeight(), PixelFormat32bppPARGB);
        Graphics g(copy);
        g.SetCompositingMode(CompositingModeSourceCopy);
        g.DrawImage(b, 0, 0, b->GetWidth(), b->GetHeight());
    }
    delete b;
    s->Release();
    return copy;
}

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE, LPWSTR, int)
{
    InitializeCriticalSection(&g_cs);
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
    else SetStatus(K_NORMAL, L"%s", ModLabel().c_str());

    // /maj <dossier du jeu> <journal> : mise a jour sans fenetre (tests) ; journal = etat final
    if (argc >= 4 && !_wcsicmp(argv[1], L"/maj")) {
        std::wstring d = WithSlash(argv[2]);
        SetGame(IsGameDir(d) ? d : L"");
        if (!g_gameDir.empty()) { g_busy = true; UpdateThread(NULL); }
        FILE *f = _wfopen(argv[3], L"w, ccs=UTF-8");
        if (f) { fwprintf(f, L"jeu=%s mod=%d local=%s releases=%d\n%s\n", g_gameDir.c_str(), (int)g_modOk, g_localVer.c_str(), (int)g_relState, g_status.c_str()); fclose(f); }
        delete g_bg; delete g_bgDark;
        GdiplusShutdown(gtok);
        return 0;
    }

    // Capture d'un etat, sans fenetre (verification du rendu)
    if (argc >= 4 && !_wcsicmp(argv[1], L"/capture")) {
        std::wstring st = argv[3];
        g_alpha = 1;
        g_time = 0.3f;
        if (g_localVer.empty()) g_localVer = L"0.1.0-prealpha";
        g_modOk = true;
        if (st == L"attente") {
            g_state = ST_LAUNCH; g_time = 1.3f;
            wchar_t info[160];
            swprintf_s(info, T(L"%s h\u00E9berge la partie (port %d)", L"%s is hosting (port %d)"), PlayerName().c_str(), 7870);
            g_launchInfo = info;
        }
        else if (st == L"sansjeu") { g_gameDir.clear(); g_gameVer.clear(); g_localVer.clear(); g_modOk = false; SetStatus(K_ERR, T(L"My Winter Car introuvable : choisis mywintercar.exe", L"My Winter Car not found: choose mywintercar.exe")); }
        else if (st == L"coop") { g_tab = TAB_COOP; g_optHot = TabRows(TAB_COOP)[1]; g_optPart = 1; }
        else if (st == L"notes") {   // notes d'exemple (le depot n'a pas encore de release)
            g_notes = { { L"0.1.1-prealpha", L"09/10/2026", L"\u2022 Exemple de note de version (capture).\n\u2022 Deuxi\u00E8me ligne : une correction.", L"\u2022 Sample release note (capture).\n\u2022 Second line: a fix.", L"" },
                        { L"0.1.0-prealpha", L"02/10/2026", L"\u2022 Premi\u00E8re version : chargeur, joueurs visibles.", L"\u2022 First version: loader, visible players.", L"" } };
            g_notesDone = true; g_relState = REL_OK; g_tab = TAB_NOTES;
        }
        else if (st == L"notesvide") { NotesOnlyThread(NULL); g_tab = TAB_NOTES; }
        else if (st == L"journaux") { g_tab = TAB_LOGS; LogsScan(); g_logRowHot = 0; g_btn[B_LOGS].hover = 1; }
        else if (st == L"maj") { g_busy = true; g_progress = 0.42f; SetStatus(K_NORMAL, T(L"T\u00E9l\u00E9chargement de MWCoop %s\u2026", L"Downloading MWCoop %s\u2026"), L"0.1.1-prealpha"); g_focus = 0; g_time = 0.2f; }
        else { SetStatus(K_OK, T(L"%s \u00B7 \u00E0 jour", L"%s \u00B7 up to date"), ModLabel().c_str()); g_hot = B_HOST; g_btn[B_HOST].hover = 1; }
        int rc = 1;
        {
            Bitmap out((INT)(kImgW * g_scale), (INT)(kImgH * g_scale), PixelFormat32bppPARGB);
            RenderTo(out, g_scale);
            CLSID png;
            if (EncoderClsid(L"image/png", &png) && out.Save(argv[2], &png, NULL) == Ok) rc = 0;
        }   // (detruit avant GdiplusShutdown)
        delete g_bg; delete g_bgDark;
        GdiplusShutdown(gtok);
        return rc;
    }
    LocalFree(argv);

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
    delete g_bg; delete g_bgDark;
    GdiplusShutdown(gtok);
    return 0;
}
