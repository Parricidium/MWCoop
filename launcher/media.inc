// ---------------------------------------------------------------- pages MEDIA : VHS et Album (demande de JD, 13/10)
// VHS : les cassettes de la partie de ce PC (celle qu'on heberge ou joue seul) -- liste lue dans la sauvegarde du jeu
// (LocalLow\Amistech\My Winter Car\MWCoop-esineet.txt, ecrite par le mod : "id;sorte;donnees;position;rotation"), y
// compris celles qui sont dans un magnetoscope ou un camescope ("<cassette>|<etat>"). Contenu d'une cassette : "tyhja"
// (vierge) ou "f1+f2~titre" ; films dans <jeu>\MWCoop\videot\<film>.mwv. On peut : renommer, effacer, importer une
// video (convertie par MWCoopRT.dll : VidImportStart, comme /importer en jeu ; ajoutee a la suite des prises). Jeu
// ouvert : rien n'est modifie (le mod reecrit la liste toutes les minutes).
// La cassette choisie a droite (comme l'Album) : apercu des images de ses prises, export .mp4 (VidExportStart), et les actions.
// Album : les photos du drone (Images\CoopPictures, ou [Coop] DossierPhotos de mwcoop.ini) en vignettes ; a droite, la
// photo choisie en grand, OUVRIR, DOSSIER, SUPPRIMER (vers la Corbeille, deux clics).

// (UiClick, ui.inc, envoie 2990..3489 a VhsClick et 3490.. a AlbClick : les onglets de la page passent par VhsClick)
enum { UI_VHS_OPEN = 2990, UI_VHS_REFRESH = 2991, UI_VHS_EXPORT = 2992, UI_VHS_PLAY = 2993, UI_VHS_VIDDIR = 2994, UI_VHS_TAB = 2995, UI_VHS_IMPORT = 2997, UI_VHS = 3000,
       UI_ALB_OPEN = 3490, UI_ALB_DIR, UI_ALB_DEL, UI_ALB_FOLDER, UI_ALB_VIEW = 3494 /* + 0 recentes, 1 par partie, 2 favoris */, UI_ALB_SLIDE = 3497, UI_ALB_FAV = 3498, UI_ALB = 3500 };
enum { VP_RENAME, VP_IMPORT, VP_ERASE, VP_OK, VP_CANCEL, VP_SELECT, VP_SEND };

// ---- donnees
struct VhsTape {
    std::wstring id, kind, data;      // ligne de la sauvegarde
    std::wstring content, title, flag; // cassette ("films~titre") ; dans un appareil : "|etat" apres
    std::vector<std::wstring> films;
    float secs = 0; int missing = 0;
    unsigned long long bytes = 0;      // taille des films sur ce PC
};
static std::vector<VhsTape> g_vhs;
static std::vector<std::wstring> g_vhsLines;   // toutes les lignes du fichier (autres objets gardes tels quels)
static std::vector<int> g_vhsLineOf;           // ligne de chaque cassette
static DWORD g_vhsScanT;
static bool g_vhsGameOn, g_vhsFileFound;
static int g_vhsEdit = -1; static std::wstring g_vhsEditText;
static int g_vhsDel = -1; static DWORD g_vhsDelT;
static std::map<std::wstring, std::pair<unsigned long long, float>> g_vhsLen;   // film -> (taille, duree)

static std::wstring VhsItemFile()
{
    wchar_t up[MAX_PATH] = L"";
    GetEnvironmentVariableW(L"USERPROFILE", up, MAX_PATH);
    (void)up; std::wstring d = SaveDirOf(SaveCurrentProf());   // (la sauvegarde utilisee : saves.inc)
    return d.empty() ? L"" : d + L"MWCoop-esineet.txt";
}
static std::wstring VhsFilmDir() { return g_modRoot + L"MWCoop\\videot\\"; }
static std::wstring VhsFilmPath(const std::wstring &f)
{
    std::wstring p = VhsFilmDir() + f + L".mwv";
    if (FileExists(p)) return p;
    std::wstring l = LocalDir() + L"videot\\" + f + L".mwv";   // (dossier du jeu en lecture seule : donnees du mod la)
    return FileExists(l) ? l : p;
}

// duree d'un film (.mwv : derniere image ou fin du son), gardee tant que la taille ne change pas
static float VhsFilmSecs(const std::wstring &f, bool *found, unsigned long long *bytes = NULL)
{
    std::wstring p = VhsFilmPath(f);
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a)) { *found = false; return 0; }
    *found = true;
    unsigned long long size = ((unsigned long long)a.nFileSizeHigh << 32) | a.nFileSizeLow;
    if (bytes) *bytes = size;
    auto it = g_vhsLen.find(f);
    if (it != g_vhsLen.end() && it->second.first == size) return it->second.second;
    std::vector<unsigned char> d;
    float secs = 0;
    if (ReadAll(p, d) && d.size() >= 11 && !memcmp(d.data(), "MWV1", 4)) {
        int rate = d[9] | d[10] << 8; if (rate <= 0) rate = 11025;
        unsigned lastMs = 0; size_t snd = 0;
        for (size_t q = 11; q + 9 <= d.size();) {
            unsigned ms; int n; memcpy(&ms, &d[q + 1], 4); memcpy(&n, &d[q + 5], 4);
            int kind = d[q]; q += 9;
            if (n < 0 || q + n > d.size()) break;
            if (kind == 1 && ms > lastMs) lastMs = ms; else if (kind == 2) snd += n;
            q += n;
        }
        secs = max(lastMs / 1000.0f, (float)snd / rate);
    }
    g_vhsLen[f] = std::make_pair(size, secs);
    return secs;
}

static void VhsParse(VhsTape &t, const std::wstring &content)
{
    t.content = content; t.films.clear(); t.title.clear(); t.secs = 0; t.missing = 0; t.bytes = 0;
    size_t tl = content.find(L'~');
    std::wstring films = tl == std::wstring::npos ? content : content.substr(0, tl);
    if (tl != std::wstring::npos) t.title = content.substr(tl + 1);
    if (films == L"tyhja") films.clear();
    for (size_t s = 0; s <= films.size();) {
        size_t e = films.find(L'+', s); if (e == std::wstring::npos) e = films.size();
        if (e > s) t.films.push_back(films.substr(s, e - s));
        s = e + 1;
    }
    for (auto &f : t.films) { bool ok; unsigned long long b = 0; t.secs += VhsFilmSecs(f, &ok, &b); t.bytes += b; if (!ok) t.missing++; }
}

// Cassettes « envoyees aux invites » : les fichiers de leurs films, [Lanceur] FilmsEnvoyes (separes par |) du
// mwcoop-lanceur.ini -- la page Contenu envoye les reprendra ; ici on marque et on montre.
static std::set<std::wstring> g_vhsSent;
static void VhsSentLoad()
{
    g_vhsSent.clear();
    std::vector<wchar_t> b(16384);
    GetPrivateProfileStringW(L"Lanceur", L"FilmsEnvoyes", L"", b.data(), (DWORD)b.size(), g_iniLauncher.c_str());
    std::wstring all = b.data();
    for (size_t s = 0; s <= all.size();) {
        size_t e = all.find(L'|', s); if (e == std::wstring::npos) e = all.size();
        std::wstring f = Trim(all.substr(s, e - s));
        if (!f.empty()) g_vhsSent.insert(f);
        s = e + 1;
    }
}
static void VhsSentSave()
{
    std::wstring all;
    for (auto &f : g_vhsSent) all += (all.empty() ? L"" : L"|") + f;
    WritePrivateProfileStringW(L"Lanceur", L"FilmsEnvoyes", all.empty() ? NULL : all.c_str(), g_iniLauncher.c_str());
}
static bool VhsSent(const VhsTape &t)
{
    if (t.films.empty()) return false;
    for (auto &f : t.films) if (!g_vhsSent.count(f + L".mwv")) return false;
    return true;
}
static void VhsSendToggle(int i)
{
    if (i < 0 || i >= (int)g_vhs.size() || g_vhs[i].films.empty()) return;
    const VhsTape &t = g_vhs[i];
    bool on = !VhsSent(t);
    for (auto &f : t.films) { if (on) g_vhsSent.insert(f + L".mwv"); else g_vhsSent.erase(f + L".mwv"); }
    VhsSentSave();
    std::wstring title = t.title.empty() ? T(L"(sans titre)", L"(no title)") : t.title;
    if (on) SetStatus(K_OK, T(L"Cassette « %s » envoyée aux invités de tes prochains salons", L"Tape \"%s\" sent to the guests of your next lobbies"), title.c_str());
    else SetStatus(K_NORMAL, T(L"Cassette « %s » : plus envoyée", L"Tape \"%s\": no longer sent"), title.c_str());
}
static std::wstring VhsSize(unsigned long long b)
{
    wchar_t s[32];
    if (b < 1048576) swprintf_s(s, T(L"%llu Ko", L"%llu KB"), (b + 1023) / 1024); else swprintf_s(s, T(L"%llu Mo", L"%llu MB"), (b + 524288) / 1048576);
    return s;
}

static void VhsScan()
{
    g_vhsScanT = GetTickCount();
    g_vhsGameOn = GameProcessRunning();
    VhsSentLoad();
    if (g_vhsEdit >= 0) return;   // (renommage en cours : la liste ne bouge pas)
    g_vhs.clear(); g_vhsLines.clear(); g_vhsLineOf.clear();
    std::vector<unsigned char> d;
    g_vhsFileFound = ReadAll(VhsItemFile(), d);
    if (!g_vhsFileFound) return;
    std::wstring all = Widen(std::string(d.begin(), d.end()));
    for (size_t s = 0; s < all.size();) {
        size_t e = all.find(L'\n', s); if (e == std::wstring::npos) e = all.size();
        std::wstring line = all.substr(s, e - s);
        if (!line.empty() && line.back() == L'\r') line.pop_back();
        if (!line.empty()) g_vhsLines.push_back(line);
        s = e + 1;
    }
    for (int i = 0; i < (int)g_vhsLines.size(); i++) {
        const std::wstring &l = g_vhsLines[i];
        size_t a = l.find(L';'), b = a == std::wstring::npos ? a : l.find(L';', a + 1), c = b == std::wstring::npos ? b : l.find(L';', b + 1);
        if (c == std::wstring::npos) continue;
        VhsTape t; t.id = l.substr(0, a); t.kind = l.substr(a + 1, b - a - 1); t.data = l.substr(b + 1, c - b - 1);
        std::wstring content = t.data;
        if (t.kind == L"nauhuri" || t.kind == L"kamera") {   // appareil : la cassette avant le '|'
            size_t bar = content.find(L'|');
            if (bar != std::wstring::npos) { t.flag = content.substr(bar); content = content.substr(0, bar); }
            if (content.empty()) continue;   // (vide)
        } else if (t.kind != L"vhs") continue;
        VhsParse(t, content);
        g_vhs.push_back(t); g_vhsLineOf.push_back(i);
    }
}

// remet la cassette i dans la sauvegarde (contenu "films~titre"), ecrit le fichier (copie puis remplacement)
static bool VhsWrite(int i, const std::wstring &content)
{
    if (i < 0 || i >= (int)g_vhs.size()) return false;
    VhsTape &t = g_vhs[i];
    std::wstring data = content;
    for (auto &ch : data) if (ch == L';' || ch == L'\n' || ch == L'\r') ch = L',';
    if (t.kind != L"vhs") data += t.flag.empty() ? L"|0" : t.flag;
    std::wstring &l = g_vhsLines[g_vhsLineOf[i]];
    size_t a = l.find(L';'), b = l.find(L';', a + 1), c = l.find(L';', b + 1);
    l = l.substr(0, b + 1) + data + l.substr(c);
    std::wstring out;
    for (auto &x : g_vhsLines) out += x + L"\n";
    std::string u = Narrow(out, CP_UTF8);   // (le mod lit de l'UTF-8)
    std::wstring f = VhsItemFile(), tmp = f + L".lanceur";
    HANDLE h = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return false;
    DWORD w = 0; BOOL ok = WriteFile(h, u.data(), (DWORD)u.size(), &w, NULL); CloseHandle(h);
    if (!ok || w != u.size() || !MoveFileExW(tmp.c_str(), f.c_str(), MOVEFILE_REPLACE_EXISTING)) { DeleteFileW(tmp.c_str()); return false; }
    t.data = data;
    VhsParse(t, content);
    return true;
}

static std::wstring VhsClean(std::wstring s)
{
    for (auto &ch : s) if (ch == L'|' || ch == L'~' || ch == L';' || ch == L'+' || ch < 32) ch = L' ';
    s = Trim(s);
    if (s.size() > 24) s = Trim(s.substr(0, 24));
    return s;
}
static std::wstring VhsFilmsPart(const VhsTape &t)
{
    std::wstring s;
    for (auto &f : t.films) s += (s.empty() ? L"" : L"+") + f;
    return s.empty() ? L"tyhja" : s;
}
static bool VhsGuard()
{
    if (GameProcessRunning()) { g_vhsGameOn = true; SetStatus(K_WARN, T(L"Ferme le jeu pour modifier les cassettes (il réécrit leur liste)", L"Close the game to change the tapes (it rewrites their list)")); return false; }
    return true;
}

// ---- import d'une video (MWCoopRT.dll, comme /importer en jeu)
typedef int (__cdecl *VidStartFn)(const wchar_t *, const wchar_t *, int, int, int, int, int);
typedef int (__cdecl *VidPollFn)(float *, wchar_t *, int);
static HMODULE g_vidLib;
static VidPollFn g_vidPoll;
static std::wstring g_vhsImpTape, g_vhsImpFilm, g_vhsImpName;   // cassette (id), film en cours, fichier
static float g_vhsImpP = -1;

static void VhsImportFree() { if (g_vidLib) { FreeLibrary(g_vidLib); g_vidLib = NULL; g_vidPoll = NULL; } }   // (le lanceur ne la garde pas : mises a jour)

static void VhsImport(int i, const wchar_t *given = NULL)   // (given : essais /vhsimport, sans la fenetre de choix)
{
    if (!VhsGuard() || i < 0 || i >= (int)g_vhs.size()) return;
    if (!g_vhsImpFilm.empty()) { SetStatus(K_WARN, T(L"Une vidéo est déjà en cours de conversion", L"A video is already being converted")); return; }
    wchar_t file[MAX_PATH] = L"";
    if (given) wcsncpy_s(file, given, _TRUNCATE);
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"Vidéos\0*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm;*.mpg;*.mpeg;*.3gp\0*.*\0*.*\0";
    of.lpstrFile = file; of.nMaxFile = MAX_PATH;
    of.lpstrTitle = T(L"Choisir la vidéo à mettre sur la cassette", L"Choose the video to put on the tape");
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!given && !GetOpenFileNameW(&of)) return;
    std::wstring dll = g_modRoot + L"MWCoop\\MWCoopRT.dll";
    VhsImportFree();
    g_vidLib = LoadLibraryW(dll.c_str());
    VidStartFn start = g_vidLib ? (VidStartFn)GetProcAddress(g_vidLib, "VidImportStart") : NULL;
    g_vidPoll = g_vidLib ? (VidPollFn)GetProcAddress(g_vidLib, "VidImportPoll") : NULL;
    if (!start || !g_vidPoll) { VhsImportFree(); SetStatus(K_ERR, T(L"Conversion impossible : MWCoopRT.dll manque ou est trop ancienne (Windows 7 : pas de conversion)", L"Can't convert: MWCoopRT.dll missing or too old (no conversion on Windows 7)")); return; }
    std::wstring ini = ModIni();
    int w = GetPrivateProfileIntW(L"Boutique", L"ImportLargeur", 320, ini.c_str()), hh = GetPrivateProfileIntW(L"Boutique", L"ImportHauteur", 240, ini.c_str());
    int fps = GetPrivateProfileIntW(L"Boutique", L"ImportImages", 12, ini.c_str()), mins = GetPrivateProfileIntW(L"Boutique", L"ImportMinutes", 20, ini.c_str());
    int q = GetPrivateProfileIntW(L"Boutique", L"ImportQualite", 50, ini.c_str());
    FILETIME ft; GetSystemTimeAsFileTime(&ft);
    wchar_t id[64]; swprintf_s(id, L"fL-%llx", ((unsigned long long)ft.dwHighDateTime << 32) | ft.dwLowDateTime);
    CreateDirectoryW((g_modRoot + L"MWCoop").c_str(), NULL);
    CreateDirectoryW(VhsFilmDir().c_str(), NULL);
    std::wstring dst = VhsFilmDir() + id + L".mwv";
    if (start(file, dst.c_str(), max(64, min(640, w)), max(48, min(480, hh)), max(1, min(25, fps)), max(12, min(120, mins)) * 60, max(10, min(95, q))) != 0) {
        VhsImportFree(); SetStatus(K_ERR, T(L"Conversion pas lancée", L"Conversion not started")); return;
    }
    g_vhsImpTape = g_vhs[i].id; g_vhsImpFilm = id; g_vhsImpP = 0;
    const wchar_t *base = wcsrchr(file, L'\\'); g_vhsImpName = base ? base + 1 : file;
    g_progress = 0;
    SetStatus(K_NORMAL, T(L"Conversion de %s…", L"Converting %s…"), g_vhsImpName.c_str());
}

static bool VhsExportTick();
static void VhsTick()   // (Tick, chaque image)
{
    if (VhsExportTick()) return;
    if (g_vhsImpFilm.empty() || !g_vidPoll) return;
    float p = 0; wchar_t msg[512] = L"";
    int st = g_vidPoll(&p, msg, 512);
    if (st == 1) { g_vhsImpP = p; g_progress = p; return; }
    std::wstring film = g_vhsImpFilm, tape = g_vhsImpTape, name = g_vhsImpName;
    g_vhsImpFilm.clear(); g_vhsImpP = -1; g_progress = -1;
    VhsImportFree();
    if (st != 2) { SetStatus(K_ERR, T(L"Conversion ratée : %s", L"Conversion failed: %s"), msg); return; }
    if (GameProcessRunning()) { SetStatus(K_WARN, T(L"Vidéo convertie, mais le jeu est ouvert : ferme-le et refais l'import", L"Video converted, but the game is open: close it and import again")); return; }
    g_vhsEdit = -1; VhsScan();
    for (int i = 0; i < (int)g_vhs.size(); i++)
        if (g_vhs[i].id == tape) {
            VhsTape &t = g_vhs[i];
            std::wstring films = t.films.empty() ? film : VhsFilmsPart(t) + L"+" + film;
            std::wstring title = t.title;
            if (title.empty()) { title = name; size_t dot = title.rfind(L'.'); if (dot != std::wstring::npos) title.resize(dot); title = VhsClean(title); }
            if (VhsWrite(i, films + L"~" + title)) SetStatus(K_OK, T(L"Vidéo ajoutée sur la cassette « %s » (%s)", L"Video added to the tape \"%s\" (%s)"), title.c_str(), msg);
            else SetStatus(K_ERR, T(L"Vidéo convertie, mais la sauvegarde n'a pas pu être modifiée", L"Video converted, but the save couldn't be changed"));
            return;
        }
    SetStatus(K_WARN, T(L"Vidéo convertie, mais la cassette n'est plus dans la sauvegarde", L"Video converted, but the tape is no longer in the save"));
}

// ---- saisie du titre (WM_CHAR, avant le reste)
static bool g_albSlide;   // (Album : diaporama en cours -- Echap l'arrete)
static bool VhsChar(wchar_t c)
{
    if (g_tab == TAB_ALBUM) { if (c == 27 && g_albSlide) { g_albSlide = false; return true; } return false; }
    if (g_tab != TAB_VHS || g_vhsEdit < 0) return false;
    if (c == 8) { if (!g_vhsEditText.empty()) g_vhsEditText.pop_back(); }
    else if (c == 127) g_vhsEditText.clear();
    else if (c == 13) { int i = g_vhsEdit; g_vhsEdit = -1; if (VhsGuard() && i < (int)g_vhs.size()) { std::wstring ti = VhsClean(g_vhsEditText); if (VhsWrite(i, VhsFilmsPart(g_vhs[i]) + L"~" + ti)) SetStatus(K_OK, T(L"Cassette renommée", L"Tape renamed")); else SetStatus(K_ERR, T(L"Sauvegarde pas modifiée", L"Save not changed")); } }
    else if (c == 27) g_vhsEdit = -1;
    else if (c == 22 && OpenClipboard(g_wnd)) {
        HANDLE d = GetClipboardData(CF_UNICODETEXT);
        const wchar_t *s = d ? (const wchar_t *)GlobalLock(d) : NULL;
        if (s) { for (; *s && *s != L'\r' && *s != L'\n' && g_vhsEditText.size() < 24; s++) g_vhsEditText += *s; GlobalUnlock(d); }
        CloseClipboard();
    }
    else if (c >= 32 && c != L'|' && c != L'~' && c != L';' && c != L'+' && g_vhsEditText.size() < 24) g_vhsEditText += c;
    g_time = 0.2f;
    return true;
}

// ---- page MEDIA (maquette du 17/10) : onglets VHS / Album dans l'en-tete (ui.inc DrawHeader), carte de gauche (liste ou
// grille) et carte de droite (la cassette ou la photo choisie) a y 88 comme les autres pages.
static const RectF kMediaCard(264, 88, 624, 656), kMediaView(900, 88, 364, 656);
static const RectF kVhsList(282, 170, 588, 488);   // (marges de 18 ; sous le titre et la ligne d'aide, au-dessus de la bande de la cassette choisie)
static float g_mediaTabRight = 1264;               // bord droit des onglets (ui.inc DrawHeader)
static const float kVhsRowH = 66;                  // (ligne de 60 : cassette de 40 et 10 de marge, puis 6 d'ecart)
static float VhsMaxScroll() { return max(0.0f, (float)g_vhs.size() * kVhsRowH - kVhsList.Height); }

static std::wstring VhsClock(float s) { int t = (int)(s + 0.5f); wchar_t b[32]; if (t >= 3600) swprintf_s(b, L"%d:%02d:%02d", t / 3600, t / 60 % 60, t % 60); else swprintf_s(b, L"%d:%02d", t / 60, t % 60); return b; }

// ---- apercu de la cassette choisie (demande de JD, 13/10 : « une preview de la video comme pour les images ») : les images
// des prises (.mwv : en-tete de 11 octets puis des blocs sorte / ms / taille / donnees ; sorte 1 = une image JPEG), lues dans
// le fichier a la demande, jouees en boucle a leur rythme (sans le son).
static int g_vhsSel = -1;
struct VhsFrame { int film; uint32_t off, len; float t; };
static std::vector<VhsFrame> g_pvFrames;
static std::vector<std::wstring> g_pvPaths;
static std::wstring g_pvOf;
static float g_pvTotal;
static DWORD g_pvStart;
static bool g_pvPause;
static float g_pvAt;            // (en pause : instant montre)
static int g_pvShown = -1;
static Bitmap *g_pvBmp;

// Blocs d'un film : (sorte, ms, position des donnees, taille) ; duree = derniere image ou fin du son.
static bool VhsReadBlocks(const std::wstring &path, std::vector<std::pair<int, std::pair<uint32_t, std::pair<uint32_t, uint32_t>>>> &out, float &len, int *w = NULL, int *h = NULL, int *fps = NULL, int *rate = NULL)
{
    HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return false;
    unsigned char hd[11]; DWORD got = 0;
    bool ok = ReadFile(f, hd, 11, &got, NULL) && got == 11 && hd[0] == 'M' && hd[1] == 'W' && hd[2] == 'V' && hd[3] == '1';
    int rt = ok ? (hd[9] | hd[10] << 8) : 11025;
    if (ok) { if (w) *w = hd[4] | hd[5] << 8; if (h) *h = hd[6] | hd[7] << 8; if (fps) *fps = hd[8]; if (rate) *rate = rt; }
    LARGE_INTEGER sz; GetFileSizeEx(f, &sz);
    uint64_t pos = 11, snd = 0; uint32_t lastMs = 0;
    while (ok && pos + 9 <= (uint64_t)sz.QuadPart) {
        unsigned char b[9];
        LARGE_INTEGER li; li.QuadPart = (LONGLONG)pos;
        if (!SetFilePointerEx(f, li, NULL, FILE_BEGIN) || !ReadFile(f, b, 9, &got, NULL) || got != 9) break;
        int kind = b[0]; uint32_t ms, n; memcpy(&ms, b + 1, 4); memcpy(&n, b + 5, 4);
        pos += 9;
        if ((int32_t)n < 0 || pos + n > (uint64_t)sz.QuadPart) break;
        out.push_back({ kind, { ms, { (uint32_t)pos, n } } });
        if (kind == 2) snd += n;
        if (ms > lastMs) lastMs = ms;
        pos += n;
    }
    CloseHandle(f);
    len = max(lastMs / 1000.0f, rt > 0 ? (float)snd / rt : 0.0f);
    return ok;
}

static void VhsPreviewFor(const VhsTape &t)
{
    std::wstring key = t.id + L"|" + t.content;
    if (key == g_pvOf) return;
    g_pvOf = key; g_pvFrames.clear(); g_pvPaths.clear(); g_pvTotal = 0; g_pvShown = -1; g_pvStart = GetTickCount(); g_pvPause = false; g_pvAt = 0;
    delete g_pvBmp; g_pvBmp = NULL;
    for (auto &film : t.films) {
        std::vector<std::pair<int, std::pair<uint32_t, std::pair<uint32_t, uint32_t>>>> bl; float len = 0;
        std::wstring p = VhsFilmPath(film);
        if (!VhsReadBlocks(p, bl, len)) continue;
        int fi = (int)g_pvPaths.size(); g_pvPaths.push_back(p);
        for (auto &b : bl) if (b.first == 1) g_pvFrames.push_back({ fi, b.second.second.first, b.second.second.second, g_pvTotal + b.second.first / 1000.0f });
        g_pvTotal += len;
    }
}

static Bitmap *VhsDecode(const VhsFrame &fr)
{
    HANDLE f = CreateFileW(g_pvPaths[fr.film].c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return NULL;
    std::vector<unsigned char> buf(fr.len);
    LARGE_INTEGER li; li.QuadPart = fr.off; DWORD got = 0;
    bool ok = SetFilePointerEx(f, li, NULL, FILE_BEGIN) && ReadFile(f, buf.data(), fr.len, &got, NULL) && got == fr.len;
    CloseHandle(f);
    if (!ok) return NULL;
    IStream *s = SHCreateMemStream(buf.data(), (UINT)buf.size());
    if (!s) return NULL;
    Bitmap *src = Bitmap::FromStream(s), *copy = NULL;
    if (src && src->GetLastStatus() == Ok) {   // (copie : l'image ne doit plus dependre du flux)
        copy = new Bitmap(src->GetWidth(), src->GetHeight(), PixelFormat32bppPARGB);
        Graphics cg(copy); cg.DrawImage(src, 0, 0, (INT)src->GetWidth(), (INT)src->GetHeight());
    }
    delete src; s->Release();
    return copy;
}

static float VhsPreviewTime()
{
    if (g_pvTotal <= 0) return 0;
    if (g_pvPause) return g_pvAt;
    return fmodf((GetTickCount() - g_pvStart) / 1000.0f, g_pvTotal);
}

// ---- export .mp4 (MWCoopRT.dll : VidExportStart, comme en jeu) : les prises bout a bout dans un .mwv provisoire.
typedef int (__cdecl *VidExportFn)(const wchar_t *, const wchar_t *, int, const wchar_t *);
static std::wstring g_vhsExpDst, g_vhsExpTmp; static float g_vhsExpP = -1;
static std::wstring g_vhsExpDirTest;   // (essais : /vhsexport <dossier>)
static std::wstring VhsVideoDir()
{
    if (!g_vhsExpDirTest.empty()) return g_vhsExpDirTest;
    wchar_t b[MAX_PATH] = L"";
    if (!g_gameDir.empty()) GetPrivateProfileStringW(L"Boutique", L"DossierVideos", L"", b, MAX_PATH, ModIni().c_str());
    if (b[0]) return WithSlash(b);
    wchar_t v[MAX_PATH] = L"";
    if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_MYVIDEO, NULL, 0, v)) && v[0]) return WithSlash(v) + L"CoopVideos\\";
    return LocalDir() + L"CoopVideos\\";
}
static bool VhsJoin(const VhsTape &t, const std::wstring &dst)
{
    HANDLE o = CreateFileW(dst.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    if (o == INVALID_HANDLE_VALUE) return false;
    bool first = true; float off = 0; int n = 0; DWORD wr;
    for (auto &film : t.films) {
        std::vector<std::pair<int, std::pair<uint32_t, std::pair<uint32_t, uint32_t>>>> bl; float len = 0; int w = 256, h = 192, fps = 10, rate = 11025;
        std::wstring p = VhsFilmPath(film);
        if (!VhsReadBlocks(p, bl, len, &w, &h, &fps, &rate)) continue;
        if (first) {
            unsigned char hd[11] = { 'M', 'W', 'V', '1', (unsigned char)w, (unsigned char)(w >> 8), (unsigned char)h, (unsigned char)(h >> 8), (unsigned char)fps, (unsigned char)rate, (unsigned char)(rate >> 8) };
            WriteFile(o, hd, 11, &wr, NULL); first = false;
        }
        HANDLE f = CreateFileW(p.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
        if (f == INVALID_HANDLE_VALUE) continue;
        std::vector<unsigned char> buf;
        for (auto &b : bl) {
            if (b.first != 1 && b.first != 2) continue;
            buf.resize(b.second.second.second);
            LARGE_INTEGER li; li.QuadPart = b.second.second.first; DWORD got = 0;
            if (!SetFilePointerEx(f, li, NULL, FILE_BEGIN) || !ReadFile(f, buf.data(), (DWORD)buf.size(), &got, NULL) || got != buf.size()) break;
            unsigned char hd[9]; hd[0] = (unsigned char)b.first;
            uint32_t ms = (uint32_t)(off * 1000.0f) + b.second.first, len32 = (uint32_t)buf.size();
            memcpy(hd + 1, &ms, 4); memcpy(hd + 5, &len32, 4);
            WriteFile(o, hd, 9, &wr, NULL); if (!buf.empty()) WriteFile(o, buf.data(), (DWORD)buf.size(), &wr, NULL);
            if (b.first == 1) n++;
        }
        CloseHandle(f);
        off += len;
    }
    CloseHandle(o);
    if (n == 0) { DeleteFileW(dst.c_str()); return false; }
    return true;
}
static void VhsExport(int i)
{
    if (i < 0 || i >= (int)g_vhs.size() || g_vhs[i].films.empty()) return;
    if (!g_vhsImpFilm.empty() || !g_vhsExpDst.empty()) { SetStatus(K_WARN, T(L"Une conversion ou un export est déjà en cours", L"A conversion or an export is already running")); return; }
    VhsTape &t = g_vhs[i];
    std::wstring dir = VhsVideoDir();
    SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
    SYSTEMTIME st; GetLocalTime(&st);
    wchar_t stamp[64]; swprintf_s(stamp, L"%04d-%02d-%02d_%02d-%02d-%02d", st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
    std::wstring name = t.title.empty() ? L"MWCoop" : t.title;
    for (wchar_t &c : name) if (wcschr(L"\\/:*?\"<>|", c)) c = L'_';
    std::wstring dst = dir + name + L"_" + stamp + L".mp4", tmp = VhsFilmDir() + L"export-" + stamp + L".mwv";
    CreateDirectoryW(VhsFilmDir().c_str(), NULL);
    if (!VhsJoin(t, tmp)) { SetStatus(K_ERR, T(L"Rien à exporter : les films de cette cassette ne sont pas sur ce PC", L"Nothing to export: this tape's films aren't on this PC")); return; }
    VhsImportFree();
    g_vidLib = LoadLibraryW((g_modRoot + L"MWCoop\\MWCoopRT.dll").c_str());
    VidExportFn ex = g_vidLib ? (VidExportFn)GetProcAddress(g_vidLib, "VidExportStart") : NULL;
    g_vidPoll = g_vidLib ? (VidPollFn)GetProcAddress(g_vidLib, "VidImportPoll") : NULL;
    if (!ex || !g_vidPoll) { VhsImportFree(); DeleteFileW(tmp.c_str()); SetStatus(K_ERR, T(L"Export impossible : MWCoopRT.dll manque ou est trop ancienne (Windows 7 : pas d'export)", L"Can't export: MWCoopRT.dll missing or too old (no export on Windows 7)")); return; }
    std::wstring ini = ModIni();
    int scale = max(1, min(6, (int)GetPrivateProfileIntW(L"Boutique", L"ExportEchelle", 3, ini.c_str())));
    std::wstring logo = GetPrivateProfileIntW(L"Boutique", L"ExportLogo", 1, ini.c_str()) != 0 && FileExists(VhsFilmDir() + L"logo-mwcoop.png") ? VhsFilmDir() + L"logo-mwcoop.png" : L"";
    if (ex(tmp.c_str(), dst.c_str(), scale, logo.c_str()) != 0) { VhsImportFree(); DeleteFileW(tmp.c_str()); SetStatus(K_ERR, T(L"Export pas lancé", L"Export not started")); return; }
    g_vhsExpDst = dst; g_vhsExpTmp = tmp; g_vhsExpP = 0; g_progress = 0;
    SetStatus(K_NORMAL, T(L"Export de « %s » en .mp4…", L"Exporting \"%s\" as .mp4…"), name.c_str());
}
static bool VhsExportTick()
{
    if (g_vhsExpDst.empty() || !g_vidPoll) return false;
    float p = 0; wchar_t msg[512] = L"";
    int st = g_vidPoll(&p, msg, 512);
    if (st == 1) { g_vhsExpP = p; g_progress = p; return true; }
    std::wstring dst = g_vhsExpDst;
    g_vhsExpDst.clear(); g_vhsExpP = -1; g_progress = -1;
    VhsImportFree();
    DeleteFileW(g_vhsExpTmp.c_str()); g_vhsExpTmp.clear();
    if (st == 2) { SetStatus(K_OK, T(L"Vidéo exportée : %s", L"Video exported: %s"), dst.c_str()); std::wstring a = L"/select,\"" + dst + L"\""; ShellExecuteW(g_wnd, L"open", L"explorer.exe", a.c_str(), NULL, SW_SHOWNORMAL); }
    else SetStatus(K_ERR, T(L"Export raté : %s", L"Export failed: %s"), msg);
    return true;
}

// Onglets de la page MEDIA (VHS / Album), dessines par l'en-tete : zones UI_VHS_TAB + k, suivies par VhsClick.
static float MediaTabsW() { return PillW(L"VHS", 13) + 16 + 6 + PillW(L"Album", 13) + 16; }
static void MediaTabsDraw(Graphics &g)
{
    const wchar_t *lab[2] = { L"VHS", L"Album" };
    float x = g_mediaTabRight - MediaTabsW();
    for (int k = 0; k < 2; k++) {
        float w = PillW(lab[k], 13) + 16;
        RectF r(x, 26, w, 36);
        bool on = (g_tab == TAB_ALBUM) == (k == 1), hot = g_uiHot == UI_VHS_TAB + k;
        GraphicsPath p; RoundRect(p, r, r.Height / 2);
        SolidBrush b(on ? kAcc : hot ? TH(cardSel) : TH(btn2)); g.FillPath(&b, &p);
        if (!on) { Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p); }
        Text(g, lab[k], r, 13, FontStyleBold, on ? kOnAcc : kInk);
        HotZone(r, UI_VHS_TAB + k);
        x += w + 6;
    }
}

// Cassette (glyphe de la maquette, 64 x 40 : boitier noir #1A1A1A, lisere, deux bobines de 14 a 10 du bord, bague de 3).
static void DrawTapeGlyph(Graphics &g, RectF r, bool dim)
{
    GraphicsPath p; RoundRect(p, r, 6);
    SolidBrush b(Color(255, 26, 26, 26)); g.FillPath(&b, &p);
    Pen edge(Color(dim ? 36 : 52, 255, 255, 255), 1.0f); g.DrawPath(&edge, &p);
    for (int k = 0; k < 2; k++) {
        float cx = r.X + (k ? r.Width - 17 : 17), cy = r.Y + 19;
        SolidBrush hub(Color(255, 68, 68, 68)); g.FillEllipse(&hub, cx - 7, cy - 7, 14.0f, 14.0f);
        Pen ring(dim ? Color(255, 90, 90, 90) : Color(255, 119, 119, 119), 3.0f); g.DrawEllipse(&ring, cx - 7, cy - 7, 14.0f, 14.0f);
    }
}
// Puce d'etat (maquette .chip : 26 de haut, 11.5 gras) : neutre, verte (ok), orange (warn) ou accent.
static void MediaChip(Graphics &g, RectF r, const std::wstring &s, int kind)
{
    Color fill = kind == 1 ? WithA(kOk, 0.18f) : kind == 2 ? WithA(kWarn, 0.16f) : kind == 3 ? WithA(kAcc, 0.18f) : g_dark ? Color(26, 255, 255, 255) : Color(28, 16, 30, 48);
    Color edge = kind == 1 ? WithA(kOk, 0.35f) : kind == 2 ? WithA(kWarn, 0.35f) : kind == 3 ? WithA(kAcc, 0.35f) : g_dark ? Color(36, 255, 255, 255) : Color(44, 16, 30, 48);
    Color ink = kind == 1 ? kOk : kind == 2 ? kWarn : kind == 3 ? kAcc : WithA(kInk, 0.92f);
    GraphicsPath p; RoundRect(p, r, r.Height / 2);
    SolidBrush b(fill); g.FillPath(&b, &p);
    Pen pen(edge, 1); g.DrawPath(&pen, &p);
    Text(g, s, r, 11.5f, FontStyleBold, ink);
}
// Film importe par le lanceur (VhsImport : « fL-<hex> ») : cassette « Perso ».
static bool VhsIsPerso(const VhsTape &t) { for (auto &f : t.films) if (!f.compare(0, 3, L"fL-")) return true; return false; }

// Onglet VHS : a gauche « Mes cassettes · N » (liste, Importer une video, bande de la cassette choisie) ; a droite l'apercu.
static void DrawVhs(Graphics &g)
{
    if (GetTickCount() - g_vhsScanT > 2000) VhsScan();
    RectF c = kMediaCard, v = kMediaView;
    Glass(c);
    Glass(v);
    if (g_vhsSel >= (int)g_vhs.size()) g_vhsSel = -1;
    bool can = !g_vhsGameOn && g_vhsImpFilm.empty() && g_vhsExpDst.empty();
    float lx = c.X + 18, lw = c.Width - 36;   // (marges de 18 de la maquette)
    Title(g, T(L"Mes cassettes", L"My tapes") + (g_vhs.empty() ? L"" : L" · " + std::to_wstring(g_vhs.size())), RectF(lx, c.Y + 14, 300, 36), 16, kInk);
    UiButton(g, RectF(lx + lw - 150, c.Y + 14, 150, 36), T(L"Importer une vidéo", L"Import a video"), UI_VHS_IMPORT, true, -1, can && !g_vhs.empty(), 11);   // (bouton « glace » sm)
    UiButton(g, RectF(lx + lw - 150 - 8 - 36, c.Y + 14, 36, 36), L"", UI_VHS_REFRESH, false, IC_UNDO, true, 11);
    float subW = lw;
    if (g_vhsGameOn) {
        std::wstring w = T(L"Jeu ouvert : lecture seule", L"Game open: read only");
        float pw = PillW(w, 11.5f);
        MediaChip(g, RectF(lx + lw - pw, c.Y + 52, pw, 22), w, 2);
        subW -= pw + 12;
    }
    Text(g, T(L"Une vidéo (mp4, mkv, webm…) devient une cassette pour la télé du jeu, chez toi comme chez tes invités.", L"A video (mp4, mkv, webm…) becomes a tape for the game's TV, at your place and at your guests'."),
         RectF(lx, c.Y + 52, subW, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    // la liste
    if (!g_vhsFileFound || g_vhs.empty()) {
        Para(g, !g_vhsFileFound ? T(L"Aucune cassette pour l'instant : achète-en à la boutique OSUUSKAUPPA en jeu (à l'ouest du parking de la PSK).", L"No tape yet: buy some at the OSUUSKAUPPA shop in game (west of the PSK car park).")
                                : T(L"Aucune cassette dans ta partie.", L"No tape in your game."), RectF(kVhsList.X, kVhsList.Y + 12, kVhsList.Width - 30, 60), 13.5f, kGrey);
    } else {
        float sc = g_scroll[TAB_VHS] = min(g_scroll[TAB_VHS], VhsMaxScroll()), ms = VhsMaxScroll();
        GraphicsState st = g.Save(); g.SetClip(kVhsList, CombineModeIntersect);
        for (int i = 0; i < (int)g_vhs.size(); i++) {
            VhsTape &t = g_vhs[i];
            RectF r(kVhsList.X, kVhsList.Y + i * kVhsRowH - sc, kVhsList.Width - (ms > 0 ? 14 : 0), kVhsRowH - 6);
            if (r.Y + r.Height < kVhsList.Y || r.Y > kVhsList.Y + kVhsList.Height) continue;
            bool vis = r.Y >= kVhsList.Y - 1 && r.Y + r.Height <= kVhsList.Y + kVhsList.Height + 1;   // (zones : seulement pour les lignes entieres)
            bool sel = g_vhsSel == i, hot = g_uiHot == UI_VHS + i * 8 + VP_SELECT;
            bool importing = !g_vhsImpFilm.empty() && g_vhsImpTape == t.id, sent = VhsSent(t);
            GraphicsPath p; RoundRect(p, r, 14);
            SolidBrush rb(sel ? WithA(kAcc, 0.10f) : hot ? TH(cardSel) : TH(card)); g.FillPath(&rb, &p);
            Pen rp(sel ? kAcc : hot ? WithA(kAcc, 0.6f) : TH(choiceBorder), 1); g.DrawPath(&rp, &p);
            DrawTapeGlyph(g, RectF(r.X + 12, r.Y + 10, 64, 40), t.films.empty());
            float tx = r.X + 88, bx = r.X + r.Width - 12;
            if (g_vhsEdit == i) {   // renommage en place
                RectF fr(tx, r.Y + 13, bx - tx - 2 * 84 - 16, 34);
                GraphicsPath fp; RoundRect(fp, fr, 9); SolidBrush fb(TH(field)); g.FillPath(&fb, &fp); Pen fpn(kAcc, 1.6f); g.DrawPath(&fpn, &fp);
                RectF trr(fr.X + 12, fr.Y, fr.Width - 24, fr.Height);
                Text(g, g_vhsEditText.empty() ? T(L"Titre de la cassette", L"Tape title") : g_vhsEditText, trr, 14, FontStyleBold, g_vhsEditText.empty() ? TH(placeholder) : kInk, StringAlignmentNear);
                if (fmodf(g_time, 1.0f) < 0.55f) {
                    float cw = g_vhsEditText.empty() ? 0 : MeasureW(g, g_vhsEditText, 14, FontStyleBold);
                    float cx = min(trr.X + cw + 1, trr.X + trr.Width);
                    Pen cp(kAcc, 1.6f); g.DrawLine(&cp, cx, fr.Y + 8, cx, fr.Y + fr.Height - 8);
                }
                if (vis) {
                    UiButton(g, RectF(bx - 84, r.Y + 13, 84, 34), T(L"Écrire", L"Write"), UI_VHS + i * 8 + VP_OK, true, -1, true, 10);
                    UiButton(g, RectF(bx - 84 - 8 - 84, r.Y + 13, 84, 34), T(L"Annuler", L"Cancel"), UI_VHS + i * 8 + VP_CANCEL, false, -1, true, 10);
                }
                continue;
            }
            // etat : Prete (des prises), Perso (video importee), Vierge, En cours (conversion) ; devant : Envoyee
            std::wstring pill = importing ? T(L"En cours", L"Converting") : t.films.empty() ? T(L"Vierge", L"Blank") : VhsIsPerso(t) ? T(L"Perso", L"Custom") : T(L"Prête", L"Ready");
            int kind = importing ? 2 : t.films.empty() || VhsIsPerso(t) ? 0 : 1;
            float pw = PillW(pill, 11.5f) + 4;
            MediaChip(g, RectF(bx - pw, r.Y + (r.Height - 26) / 2, pw, 26), pill, kind);
            float right = bx - pw;
            if (sent && !importing) { std::wstring sl = T(L"Envoyée", L"Sent"); float sw = PillW(sl, 11.5f) + 4; MediaChip(g, RectF(right - 6 - sw, r.Y + (r.Height - 26) / 2, sw, 26), sl, 3); right -= 6 + sw; }
            float tw = right - 12 - tx;
            Text(g, t.title.empty() ? T(L"(sans titre)", L"(no title)") : t.title, RectF(tx, r.Y + 9, tw, 22), 14.5f, FontStyleBold, t.title.empty() ? kGrey : kInk, StringAlignmentNear);
            std::wstring info;
            if (t.films.empty()) info = T(L"Rien dessus pour l'instant", L"Nothing on it yet");
            else {
                info = std::to_wstring(t.films.size()) + T(L" prise", L" take") + (t.films.size() > 1 ? L"s" : L"") + L" · " + VhsClock(t.secs);
                if (t.bytes) info += L" · " + VhsSize(t.bytes);
                if (t.missing) info += L" · " + std::to_wstring(t.missing) + T(L" film(s) pas sur ce PC", L" film(s) not on this PC");
                if (sent) info += T(L" · envoyée aux invités", L" · sent to the guests");
            }
            if (t.kind == L"nauhuri") info += T(L" · dans un magnétoscope", L" · in a VCR");
            else if (t.kind == L"kamera") info += T(L" · dans un caméscope", L" · in a camcorder");
            if (importing) info = T(L"Conversion de ", L"Converting ") + g_vhsImpName + L"… " + std::to_wstring((int)(g_vhsImpP * 100)) + L" %";
            Text(g, info, RectF(tx, r.Y + 30, tw, 18), 12, FontStyleRegular, importing ? kAcc : kGrey, StringAlignmentNear);
            if (importing) DrawBar(g, RectF(tx, r.Y + 50, min(tw, 260.0f), 6), g_vhsImpP);
            if (vis) HotZone(r, UI_VHS + i * 8 + VP_SELECT);
        }
        g.Restore(st);
        if (ms > 0) DrawScrollBar(g, kVhsList, sc, ms);
    }
    // bande du bas (maquette .glass2) : la cassette choisie -- renommer, effacer, envoyer aux invites
    {
        RectF s(lx, c.Y + c.Height - 14 - 62, lw, 62);
        PersoStripBg(g, s);
        if (g_vhsSel < 0) {
            Text(g, T(L"Aucune cassette choisie", L"No tape selected"), RectF(s.X + 12, s.Y + 11, s.Width - 24, 20), 13, FontStyleBold, kInk, StringAlignmentNear);
            Text(g, T(L"Clique sur une cassette : la voir à droite, la renommer, l'exporter, l'envoyer aux invités ou y mettre une vidéo.", L"Click a tape: watch it on the right, rename it, export it, send it to the guests or put a video on it."), RectF(s.X + 12, s.Y + 31, s.Width - 24, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        } else {
            VhsTape &t = g_vhs[g_vhsSel];
            bool arm = g_vhsDel == g_vhsSel && GetTickCount() - g_vhsDelT < 4000;
            float bx = s.X + s.Width - 12, bw = 94;
            UiButton(g, RectF(bx - bw, s.Y + 13, bw, 36), arm ? T(L"Confirmer ?", L"Confirm?") : T(L"Effacer", L"Erase"), UI_VHS + g_vhsSel * 8 + VP_ERASE, false, -1, can && !t.films.empty(), 11);
            UiButton(g, RectF(bx - 2 * bw - 8, s.Y + 13, bw, 36), T(L"Renommer", L"Rename"), UI_VHS + g_vhsSel * 8 + VP_RENAME, false, -1, can && g_vhsEdit < 0, 11);
            float tw = bx - 2 * bw - 8 - 12 - (s.X + 12);
            Text(g, T(L"Cassette sélectionnée : ", L"Selected tape: ") + (t.title.empty() ? T(L"(sans titre)", L"(no title)") : t.title), RectF(s.X + 12, s.Y + 11, tw, 20), 13, FontStyleBold, kInk, StringAlignmentNear);
            std::wstring sub = g_vhsEdit == g_vhsSel ? T(L"Entrée : écrire · Échap : annuler · 24 caractères au plus", L"Enter: write · Esc: cancel · 24 characters at most")
                             : t.films.empty() ? T(L"Vierge · « Importer une vidéo » la remplit (qualité VHS, 4:3)", L"Blank · \"Import a video\" fills it (VHS quality, 4:3)")
                             : T(L"Qualité VHS, son mono, 4:3 · exporter en vidéo normale possible.", L"VHS quality, mono sound, 4:3 · can be exported as a normal video.");
            Text(g, sub, RectF(s.X + 12, s.Y + 31, tw, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        }
    }

    // a droite : la cassette choisie (apercu, export) et les dossiers
    float fy = v.Y + v.Height - 16 - 40, hw = (v.Width - 48 - 10) / 2;
    UiButton(g, RectF(v.X + 24, fy, hw, 40), T(L"Dossier des films", L"Films folder"), UI_VHS_OPEN, false, IC_FOLDER, true, 12);
    UiButton(g, RectF(v.X + 24 + hw + 10, fy, hw, 40), T(L"Vidéos exportées", L"Exported videos"), UI_VHS_VIDDIR, false, IC_FOLDER, true, 12);
    if (g_vhsSel < 0) {
        Icon(g, IC_FILM, v.X + v.Width / 2, v.Y + 220, 56, WithA(kGrey, 0.45f), 1.4f);
        Para(g, T(L"Clique sur une cassette pour la voir ici.", L"Click a tape to see it here."), RectF(v.X + 24, v.Y + 270, v.Width - 48, 50), 13, kGrey, StringAlignmentNear);
        return;
    }
    VhsTape &t = g_vhs[g_vhsSel];
    VhsPreviewFor(t);
    RectF pr(v.X + 16, v.Y + 16, v.Width - 32, (v.Width - 32) * 0.75f);
    GraphicsPath pp; RoundRect(pp, pr, 10);
    SolidBrush blk(Color(255, 8, 8, 10)); g.FillPath(&blk, &pp);
    float now = VhsPreviewTime();
    if (!g_pvFrames.empty()) {
        int lo = 0, hi = (int)g_pvFrames.size() - 1, k = 0;   // (derniere image dont l'instant est passe)
        while (lo <= hi) { int m = (lo + hi) / 2; if (g_pvFrames[m].t <= now) { k = m; lo = m + 1; } else hi = m - 1; }
        if (k != g_pvShown) { Bitmap *b = VhsDecode(g_pvFrames[k]); if (b) { delete g_pvBmp; g_pvBmp = b; } g_pvShown = k; }
        if (g_pvBmp) {
            float iw = (float)g_pvBmp->GetWidth(), ih = (float)g_pvBmp->GetHeight(), s = min(pr.Width / iw, pr.Height / ih);
            RectF dr(pr.X + (pr.Width - iw * s) / 2, pr.Y + (pr.Height - ih * s) / 2, iw * s, ih * s);
            GraphicsState s2 = g.Save(); g.SetClip(&pp, CombineModeIntersect); g.SetInterpolationMode(InterpolationModeBilinear); g.DrawImage(g_pvBmp, dr); g.Restore(s2);
        }
    } else Text(g, t.films.empty() ? T(L"CASSETTE VIERGE", L"BLANK TAPE") : T(L"Films pas sur ce PC", L"Films not on this PC"), pr, 15, FontStyleBold, kGrey);
    float y = pr.Y + pr.Height + 10;
    if (!g_pvFrames.empty()) {
        UiButton(g, RectF(v.X + 24, y, 44, 36), g_pvPause ? L"" : L"❚❚", UI_VHS_PLAY, false, g_pvPause ? IC_PLAY : -1);
        RectF bar(v.X + 80, y + 15, v.Width - 80 - 24 - 90, 6);
        DrawBar(g, bar, g_pvTotal > 0 ? now / g_pvTotal : 0);
        Text(g, VhsClock(now) + L" / " + VhsClock(g_pvTotal), RectF(bar.X + bar.Width + 8, y, 90, 36), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    y += 46;
    Text(g, t.title.empty() ? T(L"(sans titre)", L"(no title)") : t.title, RectF(v.X + 24, y, v.Width - 48, 26), 15, FontStyleBold, kInk, StringAlignmentNear);
    y += 34;
    bool exporting = !g_vhsExpDst.empty(), sent = VhsSent(t);
    UiButton(g, RectF(v.X + 24, y, v.Width - 48, 42), exporting ? T(L"Export… ", L"Exporting… ") + std::to_wstring((int)(max(0.0f, g_vhsExpP) * 100)) + L" %" : T(L"Exporter en .mp4", L"Export as .mp4"), UI_VHS_EXPORT, true, IC_DOWNLOAD, !t.films.empty() && g_pvFrames.size() > 0 && g_vhsImpFilm.empty() && !exporting);
    y += 52;
    // envoyer aux invites (marque dans le mwcoop-lanceur.ini : la page Contenu envoye s'en sert) ; un second clic retire
    UiButton(g, RectF(v.X + 24, y, v.Width - 48, 42), sent ? T(L"Envoyée aux invités", L"Sent to the guests") : T(L"Envoyer aux invités", L"Send to the guests"), UI_VHS + g_vhsSel * 8 + VP_SEND, false, sent ? IC_CHECK : IC_USERS, !t.films.empty() && !t.missing);
    if (sent) Text(g, T(L"Clique encore pour ne plus l'envoyer.", L"Click again to stop sending it."), RectF(v.X + 24, y + 46, v.Width - 48, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentCenter);
}

static void VhsClick(int id)
{
    if (id == UI_VHS_TAB) { GoPage(TAB_VHS); return; }
    if (id == UI_VHS_TAB + 1) { GoPage(TAB_ALBUM); return; }
    if (id == UI_VHS_IMPORT) {   // sur la cassette choisie, sinon la premiere vierge (sinon la premiere)
        int i = g_vhsSel;
        if (i < 0) { for (int k = 0; k < (int)g_vhs.size() && i < 0; k++) if (g_vhs[k].films.empty()) i = k; }
        if (i < 0 && !g_vhs.empty()) i = 0;
        if (i < 0) return;
        g_vhsSel = i; g_vhsDel = -1; g_vhsEdit = -1;
        VhsImport(i);
        return;
    }
    if (id == UI_VHS_OPEN) { CreateDirectoryW((g_modRoot + L"MWCoop").c_str(), NULL); CreateDirectoryW(VhsFilmDir().c_str(), NULL); ShellExecuteW(g_wnd, L"open", VhsFilmDir().c_str(), NULL, NULL, SW_SHOWNORMAL); return; }
    if (id == UI_VHS_REFRESH) { g_vhsEdit = -1; g_pvOf.clear(); VhsScan(); return; }
    if (id == UI_VHS_VIDDIR) { std::wstring d = VhsVideoDir(); SHCreateDirectoryExW(NULL, d.c_str(), NULL); ShellExecuteW(g_wnd, L"open", d.c_str(), NULL, NULL, SW_SHOWNORMAL); return; }
    if (id == UI_VHS_PLAY) {
        if (g_pvPause) { g_pvPause = false; g_pvStart = GetTickCount() - (DWORD)(g_pvAt * 1000); }
        else { g_pvAt = VhsPreviewTime(); g_pvPause = true; }
        return;
    }
    if (id == UI_VHS_EXPORT) { VhsExport(g_vhsSel); return; }
    int i = (id - UI_VHS) / 8, part = (id - UI_VHS) % 8;
    if (i < 0 || i >= (int)g_vhs.size()) return;
    if (part == VP_SELECT) { if (g_vhsSel != i) { g_vhsSel = i; g_vhsDel = -1; } return; }
    if (part == VP_CANCEL) { g_vhsEdit = -1; return; }
    if (part == VP_OK) { VhsChar(13); return; }
    if (part == VP_SEND) { VhsSendToggle(i); return; }   // (ini du lanceur seulement : jeu ouvert ou pas)
    if (!VhsGuard()) return;
    if (part == VP_RENAME) { g_vhsEdit = i; g_vhsEditText = g_vhs[i].title; g_focus = -1; g_time = 0; return; }
    if (part == VP_IMPORT) { g_vhsEdit = -1; VhsImport(i); return; }
    if (part == VP_ERASE) {
        if (g_vhsDel == i && GetTickCount() - g_vhsDelT < 4000) {
            g_vhsDel = -1;
            if (VhsWrite(i, L"tyhja~" + g_vhs[i].title)) SetStatus(K_OK, T(L"Cassette effacée (le titre reste)", L"Tape erased (the title stays)"));
            else SetStatus(K_ERR, T(L"Sauvegarde pas modifiée", L"Save not changed"));
        } else { g_vhsDel = i; g_vhsDelT = GetTickCount(); SetStatus(K_WARN, T(L"Clique encore sur Confirmer pour effacer cette cassette", L"Click Confirm again to erase this tape")); }
    }
}

// ---- onglet Album (maquette du 17/10) : les photos du drone en vignettes 16/9, 3 par rangee, date en pastille ; en haut
// Recentes / Par partie (par jour, un petit en-tete) / Favoris (coeur sur la photo choisie, [Album] Favoris du
// mwcoop-lanceur.ini : noms de fichiers separes par |) ; en bas Ouvrir le dossier, Diaporama (la photo de droite change
// toutes les 4 s, pastille d'avancement ; un clic ou Echap l'arrete) et le dossier ; a droite la photo choisie.
struct AlbPhoto { std::wstring path, name; FILETIME t; unsigned long long bytes = 0; Bitmap *thumb = NULL; bool tried = false; };
static std::vector<AlbPhoto> g_alb;
static DWORD g_albScanT;
static int g_albSel = -1;
static Bitmap *g_albBig; static std::wstring g_albBigOf;
static DWORD g_albDelT; static int g_albDel = -1;
static int g_albView;                 // 0 recentes, 1 par partie (jour), 2 favoris
static std::set<std::wstring> g_albFav; static bool g_albFavLoaded;
static DWORD g_albSlideT;             // (g_albSlide : plus haut, VhsChar) dernier changement de photo
static const DWORD kAlbSlideMs = 4000;
static const RectF kAlbGrid(282, 170, 588, 514);   // (marges de 18 ; sous le titre et la ligne d'aide, au-dessus de la rangee du bas)
static const float kAlbCellW = 198, kAlbCellH = 115, kAlbDayH = 28;   // pas de la grille (case de 190 x 107 : 16/9, ecart de 8) ; en-tete d'un jour

static std::wstring AlbDir()
{
    wchar_t b[MAX_PATH] = L"";
    if (!g_gameDir.empty()) GetPrivateProfileStringW(L"Coop", L"DossierPhotos", L"", b, MAX_PATH, ModIni().c_str());
    if (b[0]) return WithSlash(b);
    wchar_t pics[MAX_PATH] = L"";
    if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_MYPICTURES, NULL, 0, pics)) && pics[0]) return WithSlash(pics) + L"CoopPictures\\";
    return LocalDir() + L"CoopPictures\\";
}

static void AlbFavLoad()
{
    g_albFavLoaded = true;
    g_albFav.clear();
    std::vector<wchar_t> b(32768);
    GetPrivateProfileStringW(L"Album", L"Favoris", L"", b.data(), (DWORD)b.size(), g_iniLauncher.c_str());
    std::wstring all = b.data();
    for (size_t s = 0; s <= all.size();) {
        size_t e = all.find(L'|', s); if (e == std::wstring::npos) e = all.size();
        std::wstring f = Trim(all.substr(s, e - s));
        if (!f.empty()) g_albFav.insert(f);
        s = e + 1;
    }
}
static void AlbFavSave()
{
    std::wstring all;
    for (auto &f : g_albFav) all += (all.empty() ? L"" : L"|") + f;
    WritePrivateProfileStringW(L"Album", L"Favoris", all.empty() ? NULL : all.c_str(), g_iniLauncher.c_str());
}
static bool AlbIsFav(const AlbPhoto &p) { return g_albFav.count(p.name) != 0; }

static void AlbScan()
{
    g_albScanT = GetTickCount();
    if (!g_albFavLoaded) AlbFavLoad();
    std::wstring dir = AlbDir();
    std::vector<AlbPhoto> found;
    for (const wchar_t *pat : { L"*.png", L"*.jpg", L"*.jpeg" }) {
        WIN32_FIND_DATAW fd; HANDLE h = FindFirstFileW((dir + pat).c_str(), &fd);
        if (h == INVALID_HANDLE_VALUE) continue;
        do {
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
            AlbPhoto p; p.path = dir + fd.cFileName; p.name = fd.cFileName; p.t = fd.ftLastWriteTime;
            p.bytes = ((unsigned long long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
            found.push_back(p);
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    std::sort(found.begin(), found.end(), [](const AlbPhoto &a, const AlbPhoto &b) { return CompareFileTime(&a.t, &b.t) > 0; });
    // vignettes deja faites gardees
    for (auto &n : found) for (auto &o : g_alb) if (o.path == n.path && o.thumb) { n.thumb = o.thumb; n.tried = true; o.thumb = NULL; }
    std::wstring sel = g_albSel >= 0 && g_albSel < (int)g_alb.size() ? g_alb[g_albSel].path : L"";
    for (auto &o : g_alb) delete o.thumb;
    g_alb = found;
    g_albSel = -1;
    for (int i = 0; i < (int)g_alb.size(); i++) if (g_alb[i].path == sel) g_albSel = i;
}

// Jour local d'une photo (nombre de jours) et son etiquette : « Aujourd'hui », « Hier », sinon la date.
static ULONGLONG AlbDayOf(const FILETIME &ft)
{
    FILETIME lt; if (!FileTimeToLocalFileTime(&ft, &lt)) return 0;
    return ((((ULONGLONG)lt.dwHighDateTime << 32) | lt.dwLowDateTime) / 864000000000ULL);
}
static std::wstring AlbDayLabel(const FILETIME &ft)
{
    SYSTEMTIME now; GetLocalTime(&now);
    FILETIME nf; SystemTimeToFileTime(&now, &nf);
    ULONGLONG a = AlbDayOf(ft), b = (((ULONGLONG)nf.dwHighDateTime << 32) | nf.dwLowDateTime) / 864000000000ULL;
    if (a == b) return T(L"Aujourd'hui", L"Today");
    if (a + 1 == b) return T(L"Hier", L"Yesterday");
    FILETIME lt; SYSTEMTIME st; FileTimeToLocalFileTime(&ft, &lt); FileTimeToSystemTime(&lt, &st);
    wchar_t buf[32];
    if (g_fr) swprintf_s(buf, L"%02d/%02d/%04d", st.wDay, st.wMonth, st.wYear); else swprintf_s(buf, L"%04d-%02d-%02d", st.wYear, st.wMonth, st.wDay);
    return buf;
}

// Mise en page de la vue : les cases (photo, rectangle sans le defilement) et les en-tetes des jours ; total : hauteur.
struct AlbCell { int idx; RectF r; };
struct AlbDay { float y; std::wstring label; };
static void AlbLayout(std::vector<AlbCell> &cells, std::vector<AlbDay> &days, float &total)
{
    cells.clear(); days.clear();
    float y = 0; int col = 0; ULONGLONG lastDay = ~0ULL;
    for (int i = 0; i < (int)g_alb.size(); i++) {
        if (g_albView == 2 && !AlbIsFav(g_alb[i])) continue;
        if (g_albView == 1) {
            ULONGLONG d = AlbDayOf(g_alb[i].t);
            if (d != lastDay) { if (col) { y += kAlbCellH; col = 0; } days.push_back({ y, AlbDayLabel(g_alb[i].t) }); y += kAlbDayH; lastDay = d; }
        }
        cells.push_back({ i, RectF(kAlbGrid.X + col * kAlbCellW, kAlbGrid.Y + y, kAlbCellW - 8, kAlbCellH - 8) });
        if (++col == 3) { col = 0; y += kAlbCellH; }
    }
    if (col) y += kAlbCellH;
    total = y;
}
static float AlbMaxScroll() { std::vector<AlbCell> c; std::vector<AlbDay> d; float t = 0; AlbLayout(c, d, t); return max(0.0f, t - kAlbGrid.Height); }

static Bitmap *AlbThumb(AlbPhoto &p, float w, float h)
{
    if (p.tried) return p.thumb;
    p.tried = true;
    if (Bitmap *b = LoadAnyImage(p.path, NULL)) {
        int W = (int)(w * g_scale), H = (int)(h * g_scale);
        p.thumb = new Bitmap(W, H, PixelFormat32bppPARGB);
        Graphics tg(p.thumb); tg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        float iw = (float)b->GetWidth(), ih = (float)b->GetHeight(), k = max(W / iw, H / ih);   // (recadre : remplit la case)
        tg.DrawImage(b, RectF((W - iw * k) / 2, (H - ih * k) / 2, iw * k, ih * k));
        delete b;
    }
    return p.thumb;
}

// Date d'une photo pour sa pastille : « Aujourd'hui 22:03 », « Hier 21:40 », sinon « 15/10 14:32 ».
static std::wstring AlbWhen(const FILETIME &ft)
{
    FILETIME lt; SYSTEMTIME st, now;
    if (!FileTimeToLocalFileTime(&ft, &lt) || !FileTimeToSystemTime(&lt, &st)) return L"";
    GetLocalTime(&now);
    FILETIME nf; SystemTimeToFileTime(&now, &nf);
    const ULONGLONG day = 864000000000ULL;
    ULONGLONG a = (((ULONGLONG)lt.dwHighDateTime << 32) | lt.dwLowDateTime) / day, b = (((ULONGLONG)nf.dwHighDateTime << 32) | nf.dwLowDateTime) / day;
    wchar_t buf[48];
    if (a == b) swprintf_s(buf, T(L"Aujourd'hui %02d:%02d", L"Today %02d:%02d"), st.wHour, st.wMinute);
    else if (a + 1 == b) swprintf_s(buf, T(L"Hier %02d:%02d", L"Yesterday %02d:%02d"), st.wHour, st.wMinute);
    else swprintf_s(buf, L"%02d/%02d %02d:%02d", st.wDay, st.wMonth, st.wHour, st.wMinute);
    return buf;
}

// Diaporama : la photo suivante de la vue (en boucle), la grille la suit.
static void AlbSlideNext(const std::vector<AlbCell> &cells)
{
    if (cells.empty()) { g_albSlide = false; return; }
    int pos = -1;
    for (int k = 0; k < (int)cells.size(); k++) if (cells[k].idx == g_albSel) pos = k;
    const AlbCell &c = cells[(pos + 1) % cells.size()];
    g_albSel = c.idx; g_albDel = -1; g_albSlideT = GetTickCount();
    float top = c.r.Y - kAlbGrid.Y, sc = g_scroll[TAB_ALBUM];
    if (top < sc) g_scroll[TAB_ALBUM] = top; else if (top + c.r.Height > sc + kAlbGrid.Height) g_scroll[TAB_ALBUM] = top + c.r.Height - kAlbGrid.Height;
}

static void DrawAlbum(Graphics &g)
{
    if (GetTickCount() - g_albScanT > 3000) AlbScan();
    RectF c = kMediaCard, v = kMediaView;
    Glass(c);
    Glass(v);
    float lx = c.X + 18, lw = c.Width - 36;
    std::vector<AlbCell> cells; std::vector<AlbDay> days; float total = 0;
    AlbLayout(cells, days, total);
    if (g_albSlide && GetTickCount() - g_albSlideT >= kAlbSlideMs) AlbSlideNext(cells);
    Title(g, T(L"Album", L"Album") + (g_alb.empty() ? L"" : L" · " + std::to_wstring(g_alb.size()) + T(L" photo", L" photo") + (g_alb.size() > 1 ? L"s" : L"")), RectF(lx, c.Y + 14, 300, 36), 16, kInk);
    UiSeg(g, RectF(lx + lw - 252, c.Y + 15, 252, 34), { T(L"Récentes", L"Recent"), T(L"Par partie", L"By session"), T(L"Favoris", L"Favorites") }, g_albView, UI_ALB_VIEW, 12);
    Text(g, T(L"Photos prises au drone (F8 en jeu, clic gauche). Clic sur une photo : en grand à droite.", L"Photos taken with the drone (F8 in game, left click). Click a photo: large on the right."),
         RectF(lx, c.Y + 52, lw, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    float ms = max(0.0f, total - kAlbGrid.Height), sc = g_scroll[TAB_ALBUM] = min(g_scroll[TAB_ALBUM], ms);
    int budget = 3;   // (vignettes chargees par image : pas de gel a l'ouverture)
    GraphicsState st = g.Save(); g.SetClip(kAlbGrid, CombineModeIntersect);
    for (const AlbDay &d : days) {   // en-tetes des jours (Par partie)
        float dy = kAlbGrid.Y + d.y - sc;
        if (dy + kAlbDayH < kAlbGrid.Y || dy > kAlbGrid.Y + kAlbGrid.Height) continue;
        Text(g, d.label, RectF(kAlbGrid.X + 2, dy + 4, 300, 18), 11.5f, FontStyleBold, kGrey, StringAlignmentNear);
        Pen sep(g_dark ? Color(28, 255, 255, 255) : TH(sep), 1);
        float tw = MeasureW(MeasureG(), d.label, 11.5f, FontStyleBold);
        g.DrawLine(&sep, kAlbGrid.X + tw + 12, dy + 13, kAlbGrid.X + kAlbGrid.Width - 8 - (ms > 0 ? 14 : 0), dy + 13);
    }
    for (const AlbCell &cell : cells) {
        int i = cell.idx;
        RectF r = cell.r; r.Y -= sc;
        if (r.Y + r.Height < kAlbGrid.Y || r.Y > kAlbGrid.Y + kAlbGrid.Height) continue;
        bool hot = g_uiHot == UI_ALB + i, sel = g_albSel == i;
        GraphicsPath p; RoundRect(p, r, 12);
        SolidBrush fb(TH(field)); g.FillPath(&fb, &p);
        Bitmap *b = g_alb[i].tried ? g_alb[i].thumb : budget-- > 0 ? AlbThumb(g_alb[i], r.Width, r.Height) : NULL;
        if (b) { GraphicsState s2 = g.Save(); g.SetClip(&p, CombineModeIntersect); g.SetInterpolationMode(InterpolationModeNearestNeighbor); g.DrawImage(b, r); g.Restore(s2); }
        else Icon(g, IC_IMAGE, r.X + r.Width / 2, r.Y + r.Height / 2, 30, WithA(kGrey, 0.5f), 1.4f);
        std::wstring when = AlbWhen(g_alb[i].t);   // la date en pastille
        if (!when.empty()) Pill(g, RectF(r.X + 8, r.Y + r.Height - 8 - 20, PillW(when, 11) - 2, 20), Color(153, 10, 20, 45), when, Color(255, 255, 255, 255), 11);
        if (AlbIsFav(g_alb[i])) {   // coeur rose en haut a droite
            SolidBrush hb(Color(150, 10, 20, 45)); g.FillEllipse(&hb, r.X + r.Width - 30, r.Y + 8, 22.0f, 22.0f);
            Icon(g, IC_HEART, r.X + r.Width - 19, r.Y + 19, 14, Color(255, 255, 143, 168), 2.2f);
        }
        Pen pen(sel ? kAcc : hot ? WithA(kAcc, 0.6f) : Color(30, 255, 255, 255), sel ? 2.5f : 1.2f); g.DrawPath(&pen, &p);
        if (r.Y >= kAlbGrid.Y - 1 && r.Y + r.Height <= kAlbGrid.Y + kAlbGrid.Height + 1) HotZone(r, UI_ALB + i);
    }
    g.Restore(st);
    if (ms > 0) DrawScrollBar(g, kAlbGrid, sc, ms);
    if (g_alb.empty()) Para(g, T(L"Pas encore de photo. En jeu, F8 lance la caméra drone ; clic gauche prend la photo.", L"No photo yet. In game, F8 starts the drone camera; left click takes the photo."), RectF(kAlbGrid.X, kAlbGrid.Y + 12, kAlbGrid.Width - 30, 60), 13.5f, kGrey);
    else if (cells.empty()) Para(g, T(L"Pas encore de favori : choisis une photo, puis « Favori » à droite.", L"No favorite yet: pick a photo, then \"Favorite\" on the right."), RectF(kAlbGrid.X, kAlbGrid.Y + 12, kAlbGrid.Width - 30, 60), 13.5f, kGrey);
    {   // rangee du bas : ouvrir le dossier, diaporama, le chemin
        float fy = c.Y + c.Height - 14 - 36;
        UiButton(g, RectF(lx, fy, 150, 36), T(L"Ouvrir le dossier", L"Open the folder"), UI_ALB_FOLDER, false, IC_FOLDER, true, 11);
        UiButton(g, RectF(lx + 160, fy, 118, 36), g_albSlide ? T(L"Arrêter", L"Stop") : T(L"Diaporama", L"Slideshow"), UI_ALB_SLIDE, g_albSlide, g_albSlide ? -1 : IC_PLAY, !cells.empty(), 11);
        Text(g, T(L"Dossier : ", L"Folder: ") + AlbDir(), RectF(lx + 290, fy, lw - 290, 36), 12, FontStyleRegular, kGrey, StringAlignmentFar);
    }
    // a droite : la photo choisie
    if (g_albSel < 0 || g_albSel >= (int)g_alb.size()) {
        Icon(g, IC_IMAGE, v.X + v.Width / 2, v.Y + 220, 56, WithA(kGrey, 0.45f), 1.4f);
        Para(g, T(L"Clique sur une photo pour la voir ici.", L"Click a photo to see it here."), RectF(v.X + 24, v.Y + 270, v.Width - 48, 50), 13, kGrey, StringAlignmentCenter);
        return;
    }
    AlbPhoto &ph = g_alb[g_albSel];
    if (g_albBigOf != ph.path) { delete g_albBig; g_albBig = LoadAnyImage(ph.path, NULL); g_albBigOf = ph.path; }
    RectF ir(v.X + 16, v.Y + 16, v.Width - 32, 280);
    std::wstring info = AlbWhen(ph.t);
    if (g_albBig) {
        float iw = (float)g_albBig->GetWidth(), ih = (float)g_albBig->GetHeight(), k = min(ir.Width / iw, ir.Height / ih);
        RectF dr(ir.X + (ir.Width - iw * k) / 2, ir.Y + (ir.Height - ih * k) / 2, iw * k, ih * k);
        GraphicsPath p; RoundRect(p, dr, 10);
        GraphicsState s2 = g.Save(); g.SetClip(&p, CombineModeIntersect); g.SetInterpolationMode(InterpolationModeHighQualityBicubic); g.DrawImage(g_albBig, dr); g.Restore(s2);
        wchar_t sz[64]; swprintf_s(sz, L"%u × %u · ", g_albBig->GetWidth(), g_albBig->GetHeight());
        info = sz + VhsSize(ph.bytes) + L" · " + info;
        if (g_albSlide) {   // pastille d'avancement du diaporama, sur la photo
            int pos = 0; for (int k = 0; k < (int)cells.size(); k++) if (cells[k].idx == g_albSel) pos = k;
            std::wstring lab = T(L"Diaporama · ", L"Slideshow · ") + std::to_wstring(pos + 1) + L" / " + std::to_wstring(cells.size());
            float pw = PillW(lab, 11) + 4;
            Pill(g, RectF(dr.X + dr.Width - 8 - pw, dr.Y + 8, pw, 22), Color(170, 10, 20, 45), lab, Color(255, 255, 255, 255), 11);
            DrawBar(g, RectF(dr.X + 10, dr.Y + dr.Height - 10, dr.Width - 20, 4), min(1.0f, (GetTickCount() - g_albSlideT) / (float)kAlbSlideMs));
        }
    }
    Text(g, ph.name, RectF(v.X + 24, v.Y + 306, v.Width - 48, 26), 14, FontStyleBold, kInk, StringAlignmentNear);
    Text(g, info, RectF(v.X + 24, v.Y + 334, v.Width - 48, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    float by = v.Y + 372, bw = v.Width - 48;
    UiButton(g, RectF(v.X + 24, by, bw, 44), T(L"Ouvrir", L"Open"), UI_ALB_OPEN, true, IC_EYE); by += 54;
    bool fav = AlbIsFav(ph);
    UiButton(g, RectF(v.X + 24, by, bw, 44), fav ? T(L"Retirer des favoris", L"Remove from favorites") : T(L"Favori", L"Favorite"), UI_ALB_FAV, false, IC_HEART); by += 54;
    UiButton(g, RectF(v.X + 24, by, bw, 44), T(L"Afficher dans le dossier", L"Show in folder"), UI_ALB_DIR, false, IC_FOLDER); by += 54;
    bool arm = g_albDel == g_albSel && GetTickCount() - g_albDelT < 4000;
    UiButton(g, RectF(v.X + 24, by, bw, 44), arm ? T(L"Confirmer ? (Corbeille)", L"Confirm? (Recycle Bin)") : T(L"Supprimer", L"Delete"), UI_ALB_DEL, false, IC_TRASH);
}

static void AlbClick(int id)
{
    if (id != UI_ALB_SLIDE) g_albSlide = false;   // (un clic arrete le diaporama)
    if (id == UI_ALB_FOLDER) { std::wstring d = AlbDir(); CreateDirectoryW(d.c_str(), NULL); ShellExecuteW(g_wnd, L"open", d.c_str(), NULL, NULL, SW_SHOWNORMAL); return; }
    if (id >= UI_ALB_VIEW && id < UI_ALB_VIEW + 3) { g_albView = id - UI_ALB_VIEW; g_scroll[TAB_ALBUM] = 0; return; }
    if (id == UI_ALB_SLIDE) {
        g_albSlide = !g_albSlide;
        if (g_albSlide) {
            std::vector<AlbCell> cells; std::vector<AlbDay> days; float t = 0;
            AlbLayout(cells, days, t);
            bool shown = false;
            for (auto &c : cells) if (c.idx == g_albSel) shown = true;
            if (!shown && !cells.empty()) { g_albSel = cells[0].idx; g_albDel = -1; }
            if (cells.empty()) g_albSlide = false;
            g_albSlideT = GetTickCount();
        }
        return;
    }
    if (id >= UI_ALB) { int i = id - UI_ALB; if (i < (int)g_alb.size()) { g_albSel = i; g_albDel = -1; } return; }
    if (g_albSel < 0 || g_albSel >= (int)g_alb.size()) return;
    AlbPhoto &ph = g_alb[g_albSel];
    if (id == UI_ALB_OPEN) { ShellExecuteW(g_wnd, L"open", ph.path.c_str(), NULL, NULL, SW_SHOWNORMAL); return; }
    if (id == UI_ALB_DIR) { std::wstring a = L"/select,\"" + ph.path + L"\""; ShellExecuteW(g_wnd, L"open", L"explorer.exe", a.c_str(), NULL, SW_SHOWNORMAL); return; }
    if (id == UI_ALB_FAV) {
        if (AlbIsFav(ph)) g_albFav.erase(ph.name); else g_albFav.insert(ph.name);
        AlbFavSave();
        return;
    }
    if (id == UI_ALB_DEL) {
        if (g_albDel == g_albSel && GetTickCount() - g_albDelT < 4000) {
            g_albDel = -1;
            std::wstring from = ph.path; from.push_back(L'\0');   // (liste terminee par deux zeros)
            SHFILEOPSTRUCTW op = {};
            op.hwnd = g_wnd; op.wFunc = FO_DELETE; op.pFrom = from.c_str();
            op.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI;
            if (SHFileOperationW(&op) == 0 && !op.fAnyOperationsAborted) {
                if (g_albFav.erase(ph.name)) AlbFavSave();
                SetStatus(K_OK, T(L"Photo mise à la Corbeille", L"Photo moved to the Recycle Bin")); delete g_albBig; g_albBig = NULL; g_albBigOf.clear(); g_albSel = -1; AlbScan();
            }
            else SetStatus(K_ERR, T(L"Photo pas supprimée", L"Photo not deleted"));
        } else { g_albDel = g_albSel; g_albDelT = GetTickCount(); SetStatus(K_WARN, T(L"Clique encore pour mettre la photo à la Corbeille", L"Click again to move the photo to the Recycle Bin")); }
    }
}
