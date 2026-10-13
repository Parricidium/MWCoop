// ---------------------------------------------------------------- pages MEDIA : VHS et Album (demande de JD, 13/10)
// VHS : les cassettes de la partie de ce PC (celle qu'on heberge ou joue seul) -- liste lue dans la sauvegarde du jeu
// (LocalLow\Amistech\My Winter Car\MWCoop-esineet.txt, ecrite par le mod : "id;sorte;donnees;position;rotation"), y
// compris celles qui sont dans un magnetoscope ou un camescope ("<cassette>|<etat>"). Contenu d'une cassette : "tyhja"
// (vierge) ou "f1+f2~titre" ; films dans <jeu>\MWCoop\videot\<film>.mwv. On peut : renommer, effacer, importer une
// video (convertie par MWCoopRT.dll : VidImportStart, comme /importer en jeu ; ajoutee a la suite des prises). Jeu
// ouvert : rien n'est modifie (le mod reecrit la liste toutes les minutes).
// Album : les photos du drone (Images\CoopPictures, ou [Coop] DossierPhotos de mwcoop.ini) en vignettes ; a droite, la
// photo choisie en grand, OUVRIR, DOSSIER, SUPPRIMER (vers la Corbeille, deux clics).

enum { UI_VHS_OPEN = 2990, UI_VHS_REFRESH = 2991, UI_VHS = 3000, UI_ALB_OPEN = 3490, UI_ALB_DIR, UI_ALB_DEL, UI_ALB_FOLDER, UI_ALB = 3500 };
enum { VP_RENAME, VP_IMPORT, VP_ERASE, VP_OK, VP_CANCEL };

// ---- donnees
struct VhsTape {
    std::wstring id, kind, data;      // ligne de la sauvegarde
    std::wstring content, title, flag; // cassette ("films~titre") ; dans un appareil : "|etat" apres
    std::vector<std::wstring> films;
    float secs = 0; int missing = 0;
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
    return up[0] ? WithSlash(up) + L"AppData\\LocalLow\\Amistech\\My Winter Car\\MWCoop-esineet.txt" : L"";
}
static std::wstring VhsFilmDir() { return g_gameDir + L"MWCoop\\videot\\"; }
static std::wstring VhsFilmPath(const std::wstring &f)
{
    std::wstring p = VhsFilmDir() + f + L".mwv";
    if (FileExists(p)) return p;
    std::wstring l = LocalDir() + L"videot\\" + f + L".mwv";   // (dossier du jeu en lecture seule : donnees du mod la)
    return FileExists(l) ? l : p;
}

// duree d'un film (.mwv : derniere image ou fin du son), gardee tant que la taille ne change pas
static float VhsFilmSecs(const std::wstring &f, bool *found)
{
    std::wstring p = VhsFilmPath(f);
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a)) { *found = false; return 0; }
    *found = true;
    unsigned long long size = ((unsigned long long)a.nFileSizeHigh << 32) | a.nFileSizeLow;
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
    t.content = content; t.films.clear(); t.title.clear(); t.secs = 0; t.missing = 0;
    size_t tl = content.find(L'~');
    std::wstring films = tl == std::wstring::npos ? content : content.substr(0, tl);
    if (tl != std::wstring::npos) t.title = content.substr(tl + 1);
    if (films == L"tyhja") films.clear();
    for (size_t s = 0; s <= films.size();) {
        size_t e = films.find(L'+', s); if (e == std::wstring::npos) e = films.size();
        if (e > s) t.films.push_back(films.substr(s, e - s));
        s = e + 1;
    }
    for (auto &f : t.films) { bool ok; t.secs += VhsFilmSecs(f, &ok); if (!ok) t.missing++; }
}

static void VhsScan()
{
    g_vhsScanT = GetTickCount();
    g_vhsGameOn = GameProcessRunning();
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
    std::wstring dll = g_gameDir + L"MWCoop\\MWCoopRT.dll";
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
    CreateDirectoryW((g_gameDir + L"MWCoop").c_str(), NULL);
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

static void VhsTick()   // (Tick, chaque image)
{
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
static bool VhsChar(wchar_t c)
{
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

// ---- page VHS
static RectF kVhsList(288, 196, 952, 528);
static const float kVhsRowH = 84;
static float VhsMaxScroll() { return max(0.0f, (float)g_vhs.size() * kVhsRowH - kVhsList.Height); }

static std::wstring VhsClock(float s) { int t = (int)(s + 0.5f); wchar_t b[32]; if (t >= 3600) swprintf_s(b, L"%d:%02d:%02d", t / 3600, t / 60 % 60, t % 60); else swprintf_s(b, L"%d:%02d", t / 60, t % 60); return b; }

static void DrawVhs(Graphics &g)
{
    if (GetTickCount() - g_vhsScanT > 2000) VhsScan();
    RectF c = kMain; Glass(c);
    Title(g, T(L"Cassettes de ta partie", L"Tapes of your game"), RectF(c.X + 24, c.Y + 18, 600, 30), 20, kInk);
    Para(g, T(L"Celles de la partie que tu héberges ou joues seul (y compris dans un magnétoscope ou un caméscope). Renomme-les, efface-les, ou mets-y une vidéo de ton PC.",
              L"Those of the game you host or play alone (including inside a VCR or a camcorder). Rename them, erase them, or put a video from your PC on them."),
         RectF(c.X + 24, c.Y + 52, 640, 44), 12.5f, kGrey);
    UiButton(g, RectF(c.X + c.Width - 24 - 150, c.Y + 22, 150, 40), T(L"Dossier des films", L"Films folder"), UI_VHS_OPEN, false, IC_FOLDER);
    UiButton(g, RectF(c.X + c.Width - 24 - 150 - 12 - 44, c.Y + 22, 44, 40), L"", UI_VHS_REFRESH, false, IC_UNDO);
    if (g_vhsGameOn) Pill(g, RectF(c.X + 24, c.Y + 84, PillW(T(L"Jeu ouvert : lecture seule", L"Game open: read only"), 11.5f), 22), WithA(kWarn, 0.2f), T(L"Jeu ouvert : lecture seule", L"Game open: read only"), kWarn, 11.5f);
    if (!g_vhsFileFound || g_vhs.empty()) {
        Para(g, !g_vhsFileFound ? T(L"Aucune cassette pour l'instant : achète-en à la boutique OSUUSKAUPPA en jeu (à l'ouest du parking de la PSK).", L"No tape yet: buy some at the OSUUSKAUPPA shop in game (west of the PSK car park).")
                                : T(L"Aucune cassette dans ta partie.", L"No tape in your game."), RectF(kVhsList.X, kVhsList.Y + 20, kVhsList.Width, 60), 14, kGrey);
        return;
    }
    float sc = g_scroll[TAB_VHS] = min(g_scroll[TAB_VHS], VhsMaxScroll()), ms = VhsMaxScroll();
    GraphicsState st = g.Save(); g.SetClip(kVhsList, CombineModeIntersect);
    bool armed = g_vhsDel >= 0 && GetTickCount() - g_vhsDelT < 4000;
    for (int i = 0; i < (int)g_vhs.size(); i++) {
        VhsTape &t = g_vhs[i];
        RectF r(kVhsList.X, kVhsList.Y + i * kVhsRowH - sc, kVhsList.Width - (ms > 0 ? 14 : 0), kVhsRowH - 10);
        if (r.Y + r.Height < kVhsList.Y || r.Y > kVhsList.Y + kVhsList.Height) continue;
        bool vis = r.Y >= kVhsList.Y - 1 && r.Y + r.Height <= kVhsList.Y + kVhsList.Height + 1;   // (zones : seulement pour les lignes entieres)
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush rb(TH(card)); g.FillPath(&rb, &p);
        Pen rp(TH(choiceBorder), 1); g.DrawPath(&rp, &p);
        Icon(g, IC_FILM, r.X + 34, r.Y + r.Height / 2, 30, t.films.empty() ? WithA(kGrey, 0.7f) : kAcc, 1.8f);
        float tx = r.X + 64, bx = r.X + r.Width - 16;
        bool importing = !g_vhsImpFilm.empty() && g_vhsImpTape == t.id;
        if (g_vhsEdit == i) {
            RectF fr(tx, r.Y + 14, r.Width - 64 - 16 - 260, 40);
            GraphicsPath fp; RoundRect(fp, fr, 10); SolidBrush fb(TH(field)); g.FillPath(&fb, &fp); Pen fpn(kAcc, 2); g.DrawPath(&fpn, &fp);
            RectF trr(fr.X + 14, fr.Y, fr.Width - 28, fr.Height);
            Text(g, g_vhsEditText.empty() ? T(L"Titre de la cassette", L"Tape title") : g_vhsEditText, trr, 16, FontStyleBold, g_vhsEditText.empty() ? TH(placeholder) : kInk, StringAlignmentNear);
            if (fmodf(g_time, 1.0f) < 0.55f) {
                float cw = g_vhsEditText.empty() ? 0 : MeasureW(g, g_vhsEditText, 16, FontStyleBold);
                float cx = min(trr.X + cw + 1, trr.X + trr.Width);
                Pen cp(kAcc, 1.6f); g.DrawLine(&cp, cx, fr.Y + 10, cx, fr.Y + fr.Height - 10);
            }
            Text(g, T(L"Entrée : écrire · Échap : annuler · 24 caractères", L"Enter: write · Esc: cancel · 24 characters"), RectF(tx, r.Y + 54, fr.Width, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            if (vis) {
                UiButton(g, RectF(bx - 124, r.Y + 17, 124, 40), T(L"Écrire", L"Write"), UI_VHS + i * 8 + VP_OK, true, IC_CHECK);
                UiButton(g, RectF(bx - 124 - 10 - 124, r.Y + 17, 124, 40), T(L"Annuler", L"Cancel"), UI_VHS + i * 8 + VP_CANCEL, false, IC_CLOSE);
            }
            continue;
        }
        Text(g, t.title.empty() ? T(L"(sans titre)", L"(no title)") : t.title, RectF(tx, r.Y + 10, r.Width - 64 - 440, 28), 17, FontStyleBold, t.title.empty() ? kGrey : kInk, StringAlignmentNear);
        std::wstring info;
        if (t.films.empty()) info = T(L"Vierge", L"Blank");
        else {
            info = std::to_wstring(t.films.size()) + T(L" prise", L" take") + (t.films.size() > 1 ? L"s" : L"") + L" · " + VhsClock(t.secs);
            if (t.missing) info += T(L" · ", L" · ") + std::to_wstring(t.missing) + T(L" film(s) pas sur ce PC", L" film(s) not on this PC");
        }
        if (t.kind == L"nauhuri") info += T(L" · dans un magnétoscope", L" · in a VCR");
        else if (t.kind == L"kamera") info += T(L" · dans un caméscope", L" · in a camcorder");
        if (importing) info = T(L"Conversion de ", L"Converting ") + g_vhsImpName + L"… " + std::to_wstring((int)(g_vhsImpP * 100)) + L" %";
        Text(g, info, RectF(tx, r.Y + 40, r.Width - 64 - 440, 22), 12.5f, FontStyleRegular, importing ? kAcc : kGrey, StringAlignmentNear);
        if (importing) DrawBar(g, RectF(tx, r.Y + 64, r.Width - 64 - 440, 4), g_vhsImpP);
        bool can = !g_vhsGameOn && g_vhsImpFilm.empty();
        bool arm = armed && g_vhsDel == i;
        float bw = 132;
        if (vis) {
            UiButton(g, RectF(bx - bw, r.Y + 17, bw, 40), arm ? T(L"Confirmer ?", L"Confirm?") : T(L"Effacer", L"Erase"), UI_VHS + i * 8 + VP_ERASE, false, IC_TRASH, can && !t.films.empty());
            UiButton(g, RectF(bx - 2 * bw - 10, r.Y + 17, bw, 40), T(L"Vidéo…", L"Video…"), UI_VHS + i * 8 + VP_IMPORT, true, IC_DOWNLOAD, can);
            UiButton(g, RectF(bx - 3 * bw - 20, r.Y + 17, bw, 40), T(L"Renommer", L"Rename"), UI_VHS + i * 8 + VP_RENAME, false, IC_FILE, can);
        }
    }
    g.Restore(st);
    if (ms > 0) DrawScrollBar(g, kVhsList, sc, ms);
}

static void VhsClick(int id)
{
    if (id == UI_VHS_OPEN) { CreateDirectoryW((g_gameDir + L"MWCoop").c_str(), NULL); CreateDirectoryW(VhsFilmDir().c_str(), NULL); ShellExecuteW(g_wnd, L"open", VhsFilmDir().c_str(), NULL, NULL, SW_SHOWNORMAL); return; }
    if (id == UI_VHS_REFRESH) { g_vhsEdit = -1; VhsScan(); return; }
    int i = (id - UI_VHS) / 8, part = (id - UI_VHS) % 8;
    if (i < 0 || i >= (int)g_vhs.size()) return;
    if (part == VP_CANCEL) { g_vhsEdit = -1; return; }
    if (part == VP_OK) { VhsChar(13); return; }
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

// ---- page Album
struct AlbPhoto { std::wstring path, name; FILETIME t; Bitmap *thumb = NULL; bool tried = false; };
static std::vector<AlbPhoto> g_alb;
static DWORD g_albScanT;
static int g_albSel = -1;
static Bitmap *g_albBig; static std::wstring g_albBigOf;
static DWORD g_albDelT; static int g_albDel = -1;
static RectF kAlbGrid(288, 160, 600, 568), kAlbView(904, 88, 360, 656);
static const float kAlbCellW = 192, kAlbCellH = 132;

static std::wstring AlbDir()
{
    wchar_t b[MAX_PATH] = L"";
    if (!g_gameDir.empty()) GetPrivateProfileStringW(L"Coop", L"DossierPhotos", L"", b, MAX_PATH, ModIni().c_str());
    if (b[0]) return WithSlash(b);
    wchar_t pics[MAX_PATH] = L"";
    if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_MYPICTURES, NULL, 0, pics)) && pics[0]) return WithSlash(pics) + L"CoopPictures\\";
    return LocalDir() + L"CoopPictures\\";
}

static void AlbScan()
{
    g_albScanT = GetTickCount();
    std::wstring dir = AlbDir();
    std::vector<AlbPhoto> found;
    for (const wchar_t *pat : { L"*.png", L"*.jpg", L"*.jpeg" }) {
        WIN32_FIND_DATAW fd; HANDLE h = FindFirstFileW((dir + pat).c_str(), &fd);
        if (h == INVALID_HANDLE_VALUE) continue;
        do { if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) { AlbPhoto p; p.path = dir + fd.cFileName; p.name = fd.cFileName; p.t = fd.ftLastWriteTime; found.push_back(p); } } while (FindNextFileW(h, &fd));
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
static float AlbMaxScroll() { int rows = ((int)g_alb.size() + 2) / 3; return max(0.0f, rows * kAlbCellH - kAlbGrid.Height); }

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

static void DrawAlbum(Graphics &g)
{
    if (GetTickCount() - g_albScanT > 3000) AlbScan();
    RectF c(kMain.X, kMain.Y, 624, kMain.Height); Glass(c);
    Title(g, T(L"Photos du drone", L"Drone photos"), RectF(c.X + 24, c.Y + 18, 360, 30), 20, kInk);
    Text(g, std::to_wstring(g_alb.size()) + T(L" photo(s) · F8 en jeu pour en prendre", L" photo(s) · F8 in game to take some"), RectF(c.X + 24, c.Y + 48, 420, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    UiButton(g, RectF(c.X + c.Width - 24 - 150, c.Y + 20, 150, 40), T(L"Dossier", L"Folder"), UI_ALB_FOLDER, false, IC_FOLDER);
    float sc = g_scroll[TAB_ALBUM] = min(g_scroll[TAB_ALBUM], AlbMaxScroll()), ms = AlbMaxScroll();
    int budget = 3;   // (vignettes chargees par image : pas de gel a l'ouverture)
    GraphicsState st = g.Save(); g.SetClip(kAlbGrid, CombineModeIntersect);
    for (int i = 0; i < (int)g_alb.size(); i++) {
        RectF r(kAlbGrid.X + (i % 3) * (kAlbCellW + 4), kAlbGrid.Y + (i / 3) * kAlbCellH - sc, kAlbCellW - 8, kAlbCellH - 10);
        if (r.Y + r.Height < kAlbGrid.Y || r.Y > kAlbGrid.Y + kAlbGrid.Height) continue;
        bool hot = g_uiHot == UI_ALB + i, sel = g_albSel == i;
        GraphicsPath p; RoundRect(p, r, 12);
        SolidBrush fb(TH(field)); g.FillPath(&fb, &p);
        Bitmap *b = g_alb[i].tried ? g_alb[i].thumb : budget-- > 0 ? AlbThumb(g_alb[i], r.Width, r.Height) : NULL;
        if (b) { GraphicsState s2 = g.Save(); g.SetClip(&p, CombineModeIntersect); g.SetInterpolationMode(InterpolationModeNearestNeighbor); g.DrawImage(b, r); g.Restore(s2); }
        else Icon(g, IC_IMAGE, r.X + r.Width / 2, r.Y + r.Height / 2, 30, WithA(kGrey, 0.5f), 1.4f);
        if (sel || hot) { Pen pen(sel ? kAcc : WithA(kAcc, 0.6f), sel ? 3.0f : 1.5f); g.DrawPath(&pen, &p); }
        if (r.Y >= kAlbGrid.Y - 1 && r.Y + r.Height <= kAlbGrid.Y + kAlbGrid.Height + 1) HotZone(r, UI_ALB + i);
    }
    g.Restore(st);
    if (ms > 0) DrawScrollBar(g, kAlbGrid, sc, ms);
    if (g_alb.empty()) Para(g, T(L"Pas encore de photo. En jeu, F8 lance la caméra drone ; clic gauche prend la photo.", L"No photo yet. In game, F8 starts the drone camera; left click takes the photo."), RectF(kAlbGrid.X, kAlbGrid.Y + 20, kAlbGrid.Width - 30, 60), 14, kGrey);
    // a droite : la photo choisie
    Glass(kAlbView);
    if (g_albSel < 0 || g_albSel >= (int)g_alb.size()) {
        Icon(g, IC_IMAGE, kAlbView.X + kAlbView.Width / 2, kAlbView.Y + 250, 56, WithA(kGrey, 0.45f), 1.4f);
        Para(g, T(L"Clique sur une photo pour la voir ici.", L"Click a photo to see it here."), RectF(kAlbView.X + 24, kAlbView.Y + 300, kAlbView.Width - 48, 50), 13, kGrey, StringAlignmentNear);
        return;
    }
    AlbPhoto &ph = g_alb[g_albSel];
    if (g_albBigOf != ph.path) { delete g_albBig; g_albBig = LoadAnyImage(ph.path, NULL); g_albBigOf = ph.path; }
    RectF ir(kAlbView.X + 16, kAlbView.Y + 16, kAlbView.Width - 32, 300);
    if (g_albBig) {
        float iw = (float)g_albBig->GetWidth(), ih = (float)g_albBig->GetHeight(), k = min(ir.Width / iw, ir.Height / ih);
        RectF dr(ir.X + (ir.Width - iw * k) / 2, ir.Y + (ir.Height - ih * k) / 2, iw * k, ih * k);
        GraphicsPath p; RoundRect(p, dr, 10);
        GraphicsState s2 = g.Save(); g.SetClip(&p, CombineModeIntersect); g.SetInterpolationMode(InterpolationModeHighQualityBicubic); g.DrawImage(g_albBig, dr); g.Restore(s2);
        wchar_t sz[64]; swprintf_s(sz, L"%u × %u", g_albBig->GetWidth(), g_albBig->GetHeight());
        Text(g, sz, RectF(kAlbView.X + 24, kAlbView.Y + 354, 300, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    Text(g, ph.name, RectF(kAlbView.X + 24, kAlbView.Y + 326, kAlbView.Width - 48, 26), 14, FontStyleBold, kInk, StringAlignmentNear);
    float by = kAlbView.Y + 392, bw = kAlbView.Width - 48;
    UiButton(g, RectF(kAlbView.X + 24, by, bw, 44), T(L"Ouvrir", L"Open"), UI_ALB_OPEN, true, IC_EYE); by += 54;
    UiButton(g, RectF(kAlbView.X + 24, by, bw, 44), T(L"Afficher dans le dossier", L"Show in folder"), UI_ALB_DIR, false, IC_FOLDER); by += 54;
    bool arm = g_albDel == g_albSel && GetTickCount() - g_albDelT < 4000;
    UiButton(g, RectF(kAlbView.X + 24, by, bw, 44), arm ? T(L"Confirmer ? (Corbeille)", L"Confirm? (Recycle Bin)") : T(L"Supprimer", L"Delete"), UI_ALB_DEL, false, IC_TRASH);
}

static void AlbClick(int id)
{
    if (id == UI_ALB_FOLDER) { std::wstring d = AlbDir(); CreateDirectoryW(d.c_str(), NULL); ShellExecuteW(g_wnd, L"open", d.c_str(), NULL, NULL, SW_SHOWNORMAL); return; }
    if (id >= UI_ALB) { int i = id - UI_ALB; if (i < (int)g_alb.size()) { g_albSel = i; g_albDel = -1; } return; }
    if (g_albSel < 0 || g_albSel >= (int)g_alb.size()) return;
    AlbPhoto &ph = g_alb[g_albSel];
    if (id == UI_ALB_OPEN) { ShellExecuteW(g_wnd, L"open", ph.path.c_str(), NULL, NULL, SW_SHOWNORMAL); return; }
    if (id == UI_ALB_DIR) { std::wstring a = L"/select,\"" + ph.path + L"\""; ShellExecuteW(g_wnd, L"open", L"explorer.exe", a.c_str(), NULL, SW_SHOWNORMAL); return; }
    if (id == UI_ALB_DEL) {
        if (g_albDel == g_albSel && GetTickCount() - g_albDelT < 4000) {
            g_albDel = -1;
            std::wstring from = ph.path; from.push_back(L'\0');   // (liste terminee par deux zeros)
            SHFILEOPSTRUCTW op = {};
            op.hwnd = g_wnd; op.wFunc = FO_DELETE; op.pFrom = from.c_str();
            op.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI;
            if (SHFileOperationW(&op) == 0 && !op.fAnyOperationsAborted) { SetStatus(K_OK, T(L"Photo mise à la Corbeille", L"Photo moved to the Recycle Bin")); delete g_albBig; g_albBig = NULL; g_albBigOf.clear(); g_albSel = -1; AlbScan(); }
            else SetStatus(K_ERR, T(L"Photo pas supprimée", L"Photo not deleted"));
        } else { g_albDel = g_albSel; g_albDelT = GetTickCount(); SetStatus(K_WARN, T(L"Clique encore pour mettre la photo à la Corbeille", L"Click again to move the photo to the Recycle Bin")); }
    }
}
