// (inclus par launcher.cpp, a cette place : JOURNAUX (page et bouton rond) -- decoupe de la refonte du 14/10)
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

static std::wstring LogsDir() { return g_modRoot + L"MWCoop\\logs\\"; }

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
    HANDLE h = FindFirstFileW((g_modRoot + L"MWCoop\\profils\\*").c_str(), &fd);
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
    GetPrivateProfileStringW(L"Lancement", L"Horodatage", L"0", ts, 32, (g_modRoot + L"MWCoop\\lancement.ini").c_str());
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
    for (const std::wstring &pr : Profiles()) look(g_modRoot + L"MWCoop\\profils\\" + pr + L"\\logs\\chargeur.log");
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
static bool GameProcessRunning(DWORD *pidOut = NULL);
static bool GameRunningAsk();
static void CrashCheck()
{
    g_crashT = 0;
    if (g_gameDir.empty() || GameProcessRunning()) return;
    std::vector<std::wstring> logs = { LogsDir() + L"mwcoop.log" };
    for (const std::wstring &pr : Profiles()) logs.push_back(g_modRoot + L"MWCoop\\profils\\" + pr + L"\\logs\\mwcoop.log");
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

static bool ToRecycleBin(const std::vector<std::wstring> &paths);   // (launcher.cpp)
static int LogsSessionsKept() { int n = (int)GetPrivateProfileIntW(L"Lanceur", L"Sessions", 30, g_iniLauncher.c_str()); return n == 3 || n == 10 ? n : 30; }
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
            std::wstring d = g_modRoot + L"MWCoop\\profils\\" + pr + L"\\logs\\";
            add(d + L"mwcoop.log", LOG_MOD, pr, 0);
            add(d + L"chargeur.log", LOG_LOADER, pr, 0);
        }
        add(g_gameDir + GM().data + L"\\output_log.txt", LOG_UNITY, L"", 0);
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
        for (const std::wstring &pr : Profiles()) bases.push_back({ g_modRoot + L"MWCoop\\profils\\" + pr + L"\\logs\\", pr });
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
    {   // Sessions gardees (Reglages > Lanceur > Depannage, [Lanceur] Sessions = 3|10|30 ; le chargeur en garde 30 de lui-meme) :
        // les parties rangees au-dela, les plus anciennes, vont a la corbeille.
        int keep = LogsSessionsKept();
        std::vector<std::wstring> old;
        while ((int)order.size() > keep) { old.push_back(groups[order.back()].dir); order.pop_back(); }
        if (!old.empty()) { LaunchLog("journaux : %d partie(s) rangee(s) au-dela de %d mise(s) a la corbeille", (int)old.size(), keep); ToRecycleBin(old); }
    }
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
            if (!gr.dir.empty()) ShellOpen(d.c_str());
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
    } else if ((INT_PTR)ShellOpen(path.c_str()) <= 32) {
        std::wstring arg = L"\"" + path + L"\"";   // (.log sans programme associe)
        ShellExecuteW(g_wnd, L"open", L"notepad.exe", arg.c_str(), NULL, SW_SHOWNORMAL);
    }
    return true;
}

