// ---------------------------------------------------------------- SAUVEGARDES (inclus par launcher.cpp)
// Plusieurs parties (demande de JD, 14/10 : « jouer avec certains joueurs, puis avec d'autres, choisir la sauvegarde, les
// nommer ; un onglet pour les gerer ; dans le salon l'hote choisit la sauvegarde ; Nouvelle partie : nouvelle ou recommencer
// une sauvegarde, avec confirmation ; les invites suivent la sauvegarde de l'hote sans toucher a leurs parties »).
// Une sauvegarde = un profil du chargeur (loader/proxy.cpp : Profil=<nom> dans lancement.ini) : MWCoop\profils\partie-N, dont
// LocalLow (la sauvegarde du jeu, celles de MSCLoader et de MWCoop), le registre du jeu (options, repris du joueur au premier
// usage) et le verrou d'instance unique sont a part. Son nom : nom.txt (UTF-8) dans ce dossier. « Partie principale » : la
// sauvegarde normale du jeu (profil vide), nom dans [Lanceur] NomPrincipale. Sauvegarde utilisee pour HEBERGER :
// [Lanceur] Sauvegarde (vide : la principale) ; JOUER EN SOLO garde la principale. L'invite garde le profil « invite », ou il recoit la sauvegarde de l'hote
// (Net/SaveTransfer.cs) : ses propres parties ne sont jamais touchees. Une sauvegarde vide commence par une nouvelle partie
// (Flow.cs : « continuer » sans sauvegarde -> « nouvelle »).

// Resume ecrit par le mod a chaque sauvegarde (Sync/SaveStats.cs : partie.txt a cote de savefile.txt, lignes cle=valeur) :
// jour (GlobalDaysPassed), argent, banque, heures (jouees, reel), date (yyyy-MM-dd HH:mm), avec (pseudos, |), role (hote|invite|solo).
// stats faux : pas de fichier (partie jamais jouee avec cette version du mod) -> « — ».
struct SaveSlot { std::wstring prof, name; bool has = false; FILETIME mtime = {}; unsigned long long bytes = 0;
                  bool stats = false; int jour = -1, argent = -1, banque = -1; float heures = -1; std::wstring date, avec, role; };
static std::vector<SaveSlot> g_saves;
static DWORD g_savesScanT;
static bool g_savesFake;   // captures : liste factice (SavesFake), pas de releve
static int g_saveEdit = -1; static std::wstring g_saveEditText;   // renommage dans la liste de l'onglet
static int g_saveDelArm = -1; static DWORD g_saveDelT;
enum { SM_NONE, SM_PICK, SM_NEWGAME, SM_RESTART, SM_NAME };      // fenetre par-dessus le lanceur
static int g_saveModal = SM_NONE;
static bool g_saveNameForGame;                                     // nom demande pour « Nouvelle partie » (salon) : appliquee ensuite
static std::wstring g_saveNameText;
static const int kSaveNameMax = 32;
enum { UI_SAVE_NEW = 9000, UI_SAVE_REFRESH, UI_SAVE_MCLOSE, UI_SAVE_MNEW, UI_SAVE_MOK, UI_SAVE_MRESTART, UI_SAVE_MFRESH, UI_SAVE_LOBBY,
       UI_SAVE_DUP, UI_SAVE_EXPORT, UI_SAVE_RESTART, UI_SAVE_DEL,   // onglet, carte de droite : sur la partie choisie (g_saveSel)
       UI_SAVE_IMPORT,       // fenetre du nom : « Depuis un zip… » (la sauvegarde d'un ami, Exporter (zip))
       UI_SAVE_RESTORE = 9050,   // + k : copie de secours k de la partie choisie (9050-9059)
       UI_SAVE = 9100,       // + ligne * 8 + partie (onglet)
       UI_SAVE_PICK = 9700,  // + ligne (fenetre : liste)
       UI_SAVE_END = 9900 };
enum { SP_USE, SP_RENAME, SP_DIR, SP_DEL, SP_OK, SP_CANCEL, SP_SEL };
static int g_saveSel = -1;   // onglet : partie choisie (detail a droite) ; -1 : celle en cours

static std::wstring SaveProfRoot()
{
    // (comme le chargeur : <jeu>\MWCoop\profils, ou %LOCALAPPDATA%\MWCoop\profils si le dossier du jeu est en lecture seule)
    static int writable = -1;
    if (writable < 0 && !g_gameDir.empty()) {
        EnsureModDir();
        std::wstring probe = g_gameDir + L"MWCoop\\.ecriture-lanceur";
        HANDLE h = CreateFileW(probe.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, NULL);
        writable = h != INVALID_HANDLE_VALUE ? 1 : 0;
        if (h != INVALID_HANDLE_VALUE) CloseHandle(h);
    }
    return (writable != 0 || LocalDir().empty() ? g_gameDir + L"MWCoop\\" : LocalDir()) + L"profils\\";
}
static std::wstring SaveDirOf(const std::wstring &prof)
{
    if (!prof.empty()) return SaveProfRoot() + prof + L"\\LocalLow\\Amistech\\" + GM().name + L"\\";
    wchar_t up[MAX_PATH] = L"";
    GetEnvironmentVariableW(L"USERPROFILE", up, MAX_PATH);
    return up[0] ? WithSlash(up) + L"AppData\\LocalLow\\Amistech\\" + GM().name + L"\\" : L"";
}
static std::wstring SaveMainName()
{
    wchar_t b[128] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"NomPrincipale", L"", b, 128, g_iniLauncher.c_str());
    return b[0] ? b : T(L"Partie principale", L"Main game");
}
static std::wstring SaveNameOf(const std::wstring &prof)
{
    if (prof.empty()) return SaveMainName();
    std::vector<unsigned char> d;
    if (ReadAll(SaveProfRoot() + prof + L"\\nom.txt", d) && !d.empty()) {
        std::wstring n = Trim(Widen(std::string(d.begin(), d.end()), CP_UTF8));
        if (!n.empty()) return n;
    }
    return prof;
}
static void SaveWriteName(const std::wstring &prof, const std::wstring &name)
{
    if (prof.empty()) { WritePrivateProfileStringW(L"Lanceur", L"NomPrincipale", name.c_str(), g_iniLauncher.c_str()); return; }
    std::string u = Narrow(name, CP_UTF8);
    HANDLE f = CreateFileW((SaveProfRoot() + prof + L"\\nom.txt").c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    DWORD w; WriteFile(f, u.data(), (DWORD)u.size(), &w, NULL); CloseHandle(f);
}
static std::wstring SaveCurrentProf()
{
    wchar_t b[64] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Sauvegarde", L"", b, 64, g_iniLauncher.c_str());
    std::wstring p = b;
    if (!p.empty() && GetFileAttributesW((SaveProfRoot() + p).c_str()) == INVALID_FILE_ATTRIBUTES) p.clear();   // (dossier efface a la main)
    return p;
}
static std::wstring SaveCurrentName() { return SaveNameOf(SaveCurrentProf()); }
static void SaveSetCurrent(const std::wstring &prof)
{
    WritePrivateProfileStringW(L"Lanceur", L"Sauvegarde", prof.empty() ? NULL : prof.c_str(), g_iniLauncher.c_str());
    TestLog("sauvegardes : utilisee %s (%s)", prof.empty() ? "principale" : Narrow(prof, CP_UTF8).c_str(), Narrow(SaveNameOf(prof), CP_UTF8).c_str());
    if (g_lobby == LB_HOST) { if (g_lobbySteam) SSetLobby("sauvegarde", Narrow(SaveNameOf(prof), CP_UTF8)); else BroadcastState(); }
}

// partie.txt (UTF-8) de la partie : cle=valeur par ligne.
static void SaveStatsRead(SaveSlot &s, const std::wstring &dir)
{
    s.stats = false;
    std::vector<unsigned char> d;
    if (dir.empty() || !ReadAll(dir + L"partie.txt", d) || d.empty()) return;
    std::wstring all = Widen(std::string(d.begin(), d.end()), CP_UTF8);
    for (size_t a = 0; a < all.size();) {
        size_t b = all.find(L'\n', a);
        std::wstring l = Trim(all.substr(a, b == std::wstring::npos ? std::wstring::npos : b - a));
        a = b == std::wstring::npos ? all.size() : b + 1;
        size_t eq = l.find(L'=');
        if (eq == std::wstring::npos) continue;
        std::wstring k = l.substr(0, eq), v = l.substr(eq + 1);
        if (k == L"jour") s.jour = _wtoi(v.c_str());
        else if (k == L"argent") s.argent = _wtoi(v.c_str());
        else if (k == L"banque") s.banque = _wtoi(v.c_str());
        else if (k == L"heures") s.heures = (float)_wtof(v.c_str());
        else if (k == L"date") s.date = v;
        else if (k == L"avec") s.avec = v;
        else if (k == L"role") s.role = v;
        s.stats = true;
    }
}
static void SaveInfo(SaveSlot &s)
{
    std::wstring d = SaveDirOf(s.prof);
    WIN32_FILE_ATTRIBUTE_DATA a;
    s.has = !d.empty() && GetFileAttributesExW((d + L"savefile.txt").c_str(), GetFileExInfoStandard, &a);
    if (s.has) s.mtime = a.ftLastWriteTime;
    s.bytes = 0;
    SaveStatsRead(s, d);
    WIN32_FIND_DATAW fd;
    HANDLE h = d.empty() ? INVALID_HANDLE_VALUE : FindFirstFileW((d + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do { if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) s.bytes += ((unsigned long long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow; } while (FindNextFileW(h, &fd));
    FindClose(h);
}
static void SavesScan()
{
    g_savesScanT = GetTickCount();
    if (g_savesFake) return;
    std::vector<SaveSlot> out;
    SaveSlot m; m.name = SaveMainName(); SaveInfo(m); out.push_back(m);
    std::vector<SaveSlot> extra;
    WIN32_FIND_DATAW fd;
    HANDLE h = g_gameDir.empty() ? INVALID_HANDLE_VALUE : FindFirstFileW((SaveProfRoot() + L"partie-*").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) continue;
            SaveSlot s; s.prof = fd.cFileName; s.name = SaveNameOf(s.prof); SaveInfo(s); extra.push_back(s);
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    std::sort(extra.begin(), extra.end(), [](const SaveSlot &a, const SaveSlot &b) { return _wtoi(a.prof.c_str() + 7) < _wtoi(b.prof.c_str() + 7); });
    out.insert(out.end(), extra.begin(), extra.end());
    g_saves = out;
}
static std::wstring SaveCreate(const std::wstring &name)
{
    std::wstring root = SaveProfRoot();
    SHCreateDirectoryExW(NULL, root.c_str(), NULL);
    for (int n = 1; n < 1000; n++) {
        std::wstring prof = L"partie-" + std::to_wstring(n);
        if (GetFileAttributesW((root + prof).c_str()) != INVALID_FILE_ATTRIBUTES) continue;
        if (SHCreateDirectoryExW(NULL, (root + prof + L"\\LocalLow\\Amistech\\" + GM().name).c_str(), NULL) != ERROR_SUCCESS) break;
        SaveWriteName(prof, name);
        TestLog("sauvegardes : %s creee (%s)", Narrow(prof, CP_UTF8).c_str(), Narrow(name, CP_UTF8).c_str());
        SavesScan();
        return prof;
    }
    SetStatus(K_ERR, T(L"Impossible de créer la sauvegarde", L"Could not create the save"));
    return L"";
}
static std::wstring SaveDefaultName()
{
    for (int n = 2; n < 1000; n++) {
        wchar_t b[64]; swprintf_s(b, T(L"Partie %d", L"Game %d"), n);
        bool used = false;
        for (auto &s : g_saves) if (s.name == b) used = true;
        if (!used) return b;
    }
    return T(L"Nouvelle partie", L"New game");
}
// Captures (/capture sauvegardes) : trois parties factices, comme la maquette (jour 14, 12 450 mk, 31 h, hier avec Teppo et Pekka).
static std::wstring SaveFakeDate(int daysAgo, int hh, int mm)
{
    SYSTEMTIME t; GetLocalTime(&t);
    FILETIME ft; SystemTimeToFileTime(&t, &ft);
    ULARGE_INTEGER u; u.LowPart = ft.dwLowDateTime; u.HighPart = ft.dwHighDateTime;
    u.QuadPart -= (unsigned long long)daysAgo * 864000000000ULL;
    ft.dwLowDateTime = u.LowPart; ft.dwHighDateTime = u.HighPart;
    FileTimeToSystemTime(&ft, &t);
    wchar_t b[32]; swprintf_s(b, L"%04d-%02d-%02d %02d:%02d", t.wYear, t.wMonth, t.wDay, hh, mm);
    return b;
}
static void SavesFake()
{
    g_savesFake = true;
    g_saves.clear();
    SaveSlot a; a.name = T(L"Partie principale", L"Main game"); a.has = true; a.bytes = 153 * 1024; a.stats = true; a.jour = 14; a.argent = 12450; a.banque = 3800; a.heures = 31.2f;
    a.date = SaveFakeDate(1, 22, 7); a.avec = L"Teppo|Pekka"; a.role = L"hote"; GetSystemTimeAsFileTime(&a.mtime);
    SaveSlot b; b.prof = L"partie-1"; b.name = T(L"Soirée Jokkis", L"Jokkis night"); b.has = true; b.bytes = 41 * 1024; b.stats = true; b.jour = 3; b.argent = 2100; b.banque = 0; b.heures = 4.5f;
    b.date = SaveFakeDate(2, 21, 30); b.role = L"solo"; b.mtime = a.mtime;
    SaveSlot c; c.prof = L"partie-2"; c.name = T(L"Test vanilla", L"Vanilla test"); c.has = true; c.bytes = 12 * 1024; c.mtime = a.mtime;   // (jamais jouee avec le mod : pas de partie.txt)
    g_saves = { a, b, c };
    g_saveSel = 0;
}
// Montants : « 12 450 » (FR : espace fine ; EN : virgule).
static std::wstring SaveMoney(int v)
{
    std::wstring d = std::to_wstring(v < 0 ? -v : v), out;
    for (size_t i = 0; i < d.size(); i++) { if (i && (d.size() - i) % 3 == 0) out += g_fr ? L" " : L","; out += d[i]; }
    return (v < 0 ? L"-" : L"") + out;
}
// Date de partie.txt (yyyy-MM-dd HH:mm) en mots : « aujourd'hui 22:07 », « hier 22:07 », « le 15/10 22:07 » (cap : « Hier 22:07 »).
static std::wstring SaveDateRel(const std::wstring &d, bool cap)
{
    int y = 0, mo = 0, da = 0, hh = 0, mm = 0;
    if (swscanf_s(d.c_str(), L"%d-%d-%d %d:%d", &y, &mo, &da, &hh, &mm) < 3) return d;
    SYSTEMTIME t; GetLocalTime(&t);
    auto days = [](int y, int m, int d) { int a = (14 - m) / 12, yy = y + 4800 - a, mmm = m + 12 * a - 3; return d + (153 * mmm + 2) / 5 + 365 * yy + yy / 4 - yy / 100 + yy / 400; };
    int ago = days(t.wYear, t.wMonth, t.wDay) - days(y, mo, da);
    wchar_t hm[16]; swprintf_s(hm, L"%02d:%02d", hh, mm);
    std::wstring w;
    if (ago == 0) w = cap ? T(L"Aujourd'hui", L"Today") : T(L"aujourd'hui", L"today");
    else if (ago == 1) w = cap ? T(L"Hier", L"Yesterday") : T(L"hier", L"yesterday");
    else { wchar_t b[48]; if (g_fr) swprintf_s(b, L"%s %02d/%02d", cap ? L"Le" : L"le", da, mo); else swprintf_s(b, L"%s %02d/%02d", cap ? L"On" : L"on", mo, da); if (y != t.wYear) { wchar_t yb[8]; swprintf_s(yb, L"/%04d", y); wcscat_s(b, yb); } w = b; }
    return w + L" " + hm;
}
// « avec Teppo, Pekka » (joueurs de la derniere partie), ou le role.
static std::wstring SaveWith(const SaveSlot &s, bool withMe)
{
    std::wstring names;
    for (size_t a = 0; a <= s.avec.size() && !s.avec.empty();) {
        size_t e = s.avec.find(L'|', a);
        std::wstring n = Trim(s.avec.substr(a, e == std::wstring::npos ? std::wstring::npos : e - a));
        if (!n.empty()) names += (names.empty() ? L"" : L", ") + n;
        if (e == std::wstring::npos) break;
        a = e + 1;
    }
    if (!names.empty()) return withMe ? T(L"toi + ", L"you + ") + names : T(L"avec ", L"with ") + names;
    if (s.role == L"invite") return T(L"chez un ami", L"at a friend's");
    return T(L"en solo", L"solo");
}
// Ligne d'une partie : « Jour 14 · 12 450 mk · jouée hier 22:07 avec Teppo, Pekka ».
static std::wstring SaveDateText(const FILETIME &ft);
static std::wstring SaveRowInfo(const SaveSlot &s)
{
    if (!s.has) return T(L"Vide : commencera par une nouvelle partie", L"Empty: will start with a new game");
    if (!s.stats) return std::wstring(T(L"Dernière sauvegarde : ", L"Last saved: ")) + SaveDateText(s.mtime) + T(L" · jamais jouée avec MWCoop", L" · never played with MWCoop");
    std::wstring r;
    if (s.jour >= 0) r += T(L"Jour ", L"Day ") + std::to_wstring(s.jour) + L" · ";
    if (s.argent >= 0) r += SaveMoney(s.argent) + L" mk · ";
    r += (s.date.empty() ? std::wstring(T(L"jouée", L"played")) : T(L"jouée ", L"played ") + SaveDateRel(s.date, false)) + L" " + SaveWith(s, false);
    return r;
}
static std::wstring SaveWhen(const SaveSlot &s)
{
    if (!s.has) return T(L"Vide : commencera par une nouvelle partie", L"Empty: will start with a new game");
    FILETIME lt; SYSTEMTIME t;
    FileTimeToLocalFileTime(&s.mtime, &lt); FileTimeToSystemTime(&lt, &t);
    wchar_t b[96];
    if (g_fr) swprintf_s(b, L"Dernière sauvegarde : %02d/%02d/%04d %02d:%02d", t.wDay, t.wMonth, t.wYear, t.wHour, t.wMinute);
    else swprintf_s(b, T(L"", L"Last saved: %04d-%02d-%02d %02d:%02d"), t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute);
    wchar_t sz[32]; swprintf_s(sz, g_fr ? L" · %.1f Mo" : L" · %.1f MB", s.bytes / 1048576.0);
    return std::wstring(b) + sz;
}
static bool SaveGuard()
{
    if (GameProcessRunning()) { SetStatus(K_WARN, T(L"Ferme le jeu avant de modifier tes sauvegardes", L"Close the game before changing your saves")); return false; }
    return true;
}
static void SaveDelete(int i)
{
    if (i <= 0 || i >= (int)g_saves.size() || !SaveGuard()) return;
    SaveSlot s = g_saves[i];
    wchar_t q[600];
    swprintf_s(q, T(L"Supprimer la sauvegarde « %s » ?\n\nElle part à la corbeille de Windows (tu peux encore l'y récupérer).",
                    L"Delete the save \"%s\"?\n\nIt goes to the Windows Recycle Bin (you can still get it back from there)."), s.name.c_str());
    if (MessageBoxW(g_wnd, q, L"MWCoop", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) return;
    std::wstring dir = SaveProfRoot() + s.prof;
    std::vector<wchar_t> from(dir.begin(), dir.end()); from.push_back(0); from.push_back(0);
    SHFILEOPSTRUCTW op = {};
    op.hwnd = g_wnd; op.wFunc = FO_DELETE; op.pFrom = from.data();
    op.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI;
    int r = SHFileOperationW(&op);
    TestLog("sauvegardes : %s a la corbeille (%d)", Narrow(s.prof, CP_UTF8).c_str(), r);
    if (r != 0 || op.fAnyOperationsAborted) { SetStatus(K_ERR, T(L"Suppression impossible (%d)", L"Could not delete (%d)"), r); SavesScan(); return; }
    if (SaveCurrentProf() == s.prof) SaveSetCurrent(L"");
    SetStatus(K_OK, T(L"« %s » mise à la corbeille", L"\"%s\" moved to the Recycle Bin"), s.name.c_str());
    SavesScan();
}
static void SaveOpenDir(int i)
{
    if (i < 0 || i >= (int)g_saves.size()) return;
    std::wstring d = SaveDirOf(g_saves[i].prof);
    SHCreateDirectoryExW(NULL, d.c_str(), NULL);
    ShellExecuteW(g_wnd, L"open", d.c_str(), NULL, NULL, SW_SHOWNORMAL);
}
static std::wstring SaveCleanName(std::wstring s)
{
    s = Trim(s);
    if (s.size() > (size_t)kSaveNameMax) s.resize(kSaveNameMax);
    return s;
}

// ---- saisie (WM_CHAR, avant le reste) : renommage dans l'onglet, ou nom dans la fenetre
static void SaveNameDone();
static bool SaveChar(wchar_t c)
{
    std::wstring *t = g_saveModal == SM_NAME ? &g_saveNameText : g_tab == TAB_SAVES && g_saveEdit >= 0 ? &g_saveEditText : NULL;
    if (!t) { if (g_saveModal != SM_NONE && c == 27) { g_saveModal = SM_NONE; return true; } return g_saveModal != SM_NONE; }
    if (c == 8) { if (!t->empty()) t->pop_back(); }
    else if (c == 127) t->clear();
    else if (c == 13) {
        if (g_saveModal == SM_NAME) SaveNameDone();
        else { int i = g_saveEdit; g_saveEdit = -1; std::wstring n = SaveCleanName(g_saveEditText); if (i >= 0 && i < (int)g_saves.size() && !n.empty()) { SaveWriteName(g_saves[i].prof, n); SetStatus(K_OK, T(L"Sauvegarde renommée", L"Save renamed")); if (SaveCurrentProf() == g_saves[i].prof) SaveSetCurrent(g_saves[i].prof); SavesScan(); } }
    }
    else if (c == 27) { if (g_saveModal == SM_NAME) g_saveModal = SM_NONE; else g_saveEdit = -1; }
    else if (c == 22 && OpenClipboard(g_wnd)) {
        HANDLE d = GetClipboardData(CF_UNICODETEXT);
        const wchar_t *s = d ? (const wchar_t *)GlobalLock(d) : NULL;
        if (s) { for (; *s && *s != L'\r' && *s != L'\n' && t->size() < (size_t)kSaveNameMax; s++) *t += *s; GlobalUnlock(d); }
        CloseClipboard();
    }
    else if (c >= 32 && t->size() < (size_t)kSaveNameMax) *t += c;
    g_time = 0.2f;
    return true;
}

// ---- ouverture de la fenetre
static void SaveOpenPick() { SavesScan(); g_saveModal = SM_PICK; }      // salon : choisir la sauvegarde de l'hote
static void SaveOpenNewGame() { SavesScan(); g_saveModal = SM_NEWGAME; } // salon : « Nouvelle partie »
static void SaveAskName(bool forGame) { SavesScan(); g_saveNameForGame = forGame; g_saveNameText = SaveDefaultName(); g_saveModal = SM_NAME; g_time = 0; }
static void LobbyApplyPartie(int p);   // (launcher.cpp)
static void SaveNameDone()
{
    std::wstring n = SaveCleanName(g_saveNameText);
    if (n.empty()) return;
    if (!SaveGuard()) return;
    std::wstring prof = SaveCreate(n);
    g_saveModal = SM_NONE;
    if (prof.empty()) return;
    SaveSetCurrent(prof);
    if (g_saveNameForGame) LobbyApplyPartie(PARTIE_NOUVELLE);
    SetStatus(K_OK, T(L"Sauvegarde « %s » créée et utilisée", L"Save \"%s\" created and in use"), n.c_str());
}
// « Depuis un zip… » (fenetre du nom) : la sauvegarde d'un ami (Exporter (zip) : fichiers a plat) dans une nouvelle partie-N
// portant le nom saisi. Le zip est deballe dans le dossier LocalLow de la partie ; rien d'autre n'est touche.
static void SaveImportZip()
{
    std::wstring n = SaveCleanName(g_saveNameText);
    if (n.empty()) { SetStatus(K_WARN, T(L"Donne d'abord un nom à la partie", L"Name the game first")); return; }
    if (!SaveGuard()) return;
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW of = { sizeof(of) };
    of.hwndOwner = g_wnd;
    of.lpstrFilter = L"Zip\0*.zip\0*.*\0*.*\0";
    of.lpstrFile = file; of.nMaxFile = MAX_PATH;
    of.lpstrTitle = T(L"Choisir le zip d'une sauvegarde (Exporter (zip) chez un ami)", L"Choose a save zip (Export (zip) at a friend's)");
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&of)) return;
    std::wstring prof = SaveCreate(n);
    if (prof.empty()) return;
    std::wstring dir = SaveDirOf(prof);
    if (!dir.empty() && dir.back() == L'\\') dir.pop_back();
    std::string why;
    if (!Unzip(file, dir, &why)) { SetStatus(K_ERR, T(L"Zip illisible (%s) : la partie « %s » reste vide", L"Unreadable zip (%s): the game \"%s\" stays empty"), Widen(why, CP_UTF8).c_str(), n.c_str()); SavesScan(); g_saveModal = SM_NONE; return; }
    TestLog("sauvegardes : %s importee dans %s", Narrow(file, CP_UTF8).c_str(), Narrow(prof, CP_UTF8).c_str());
    g_saveModal = SM_NONE;
    SavesScan();
    for (int k = 0; k < (int)g_saves.size(); k++) if (g_saves[k].prof == prof) g_saveSel = k;
    SaveSetCurrent(prof);
    SetStatus(K_OK, T(L"« %s » créée depuis le zip et utilisée", L"\"%s\" created from the zip and in use"), n.c_str());
}
static void SavePickRow(int i)
{
    if (i < 0 || i >= (int)g_saves.size()) return;
    const SaveSlot s = g_saves[i];
    if (g_saveModal == SM_RESTART) {
        wchar_t q[700];
        swprintf_s(q, T(L"Vous êtes sur le point de recommencer « %s ».\n\nCela effacera cette sauvegarde (la partie en cours sera perdue). Êtes-vous sûr ?\n\nCouleur de la CORRIS (icône voiture du salon) : %s.",
                        L"You are about to restart \"%s\".\n\nThis will erase this save (the game in progress will be lost). Are you sure?\n\nCORRIS color (car icon in the lobby): %s."), s.name.c_str(), CarColorName().c_str());
        if (MessageBoxW(g_wnd, q, L"MWCoop", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) return;
        g_saveModal = SM_NONE;
        SaveSetCurrent(s.prof);
        LobbyApplyPartie(PARTIE_NOUVELLE);
        return;
    }
    g_saveModal = SM_NONE;
    SaveSetCurrent(s.prof);
    if (g_lobby == LB_HOST) LobbyApplyPartie(PARTIE_CONTINUER);
    SetStatus(K_NORMAL, s.has ? T(L"Sauvegarde utilisée : %s", L"Save in use: %s") : T(L"Sauvegarde utilisée : %s (vide : nouvelle partie)", L"Save in use: %s (empty: new game)"), s.name.c_str());
}

// ---- onglet (maquette du 17/10 : deux cartes -- la liste des parties a gauche, le detail de la partie choisie a droite)
static const RectF kSaveL(264, 88, 596, 656), kSaveR(872, 88, 392, 656);
static const RectF kSaveList(282, 140, 560, 512);   // (lignes de la liste, puis la ligne « Nouvelle partie »)
static const float kSaveRowH = 82;                  // (ligne de 74 + 8 d'espace)
static float SavesMaxScroll() { return max(0.0f, ((float)g_saves.size() + 1) * kSaveRowH - 8 - kSaveList.Height); }
static int SaveSelIndex()
{
    if (g_saveSel >= 0 && g_saveSel < (int)g_saves.size()) return g_saveSel;
    std::wstring cur = SaveCurrentProf();
    for (int i = 0; i < (int)g_saves.size(); i++) if (g_saves[i].prof == cur) return i;
    return g_saves.empty() ? -1 : 0;
}
static std::wstring SaveDateText(const FILETIME &ft)
{
    FILETIME lt; SYSTEMTIME t;
    FileTimeToLocalFileTime(&ft, &lt); FileTimeToSystemTime(&lt, &t);
    wchar_t b[48];
    if (g_fr) swprintf_s(b, L"%02d/%02d/%04d %02d:%02d", t.wDay, t.wMonth, t.wYear, t.wHour, t.wMinute);
    else swprintf_s(b, L"%04d-%02d-%02d %02d:%02d", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute);
    return b;
}
static std::wstring SaveSizeText(unsigned long long b)
{
    wchar_t t[32];
    if (b >= 1024 * 1024) swprintf_s(t, g_fr ? L"%.1f Mo" : L"%.1f MB", b / 1048576.0);
    else swprintf_s(t, g_fr ? L"%llu Ko" : L"%llu KB", (b + 1023) / 1024);
    if (g_fr) for (wchar_t *c = t; *c; c++) if (*c == L'.') *c = L',';
    return t;
}

// Copies de secours d'une partie : MWCoop\copie-avant-reworked\<date>\<principale | partie-N> (SavesBackupNightly, faites
// avant le premier passage en Reworked). Relues toutes les 3 s pour la partie choisie.
struct SaveBak { std::wstring dir; FILETIME when = {}; unsigned long long bytes = 0; };
static std::vector<SaveBak> g_saveBak;
static std::wstring g_saveBakFor = L"?";
static DWORD g_saveBakT;
static void SaveBakScan(const std::wstring &prof)
{
    g_saveBakFor = prof; g_saveBakT = GetTickCount();
    g_saveBak.clear();
    std::wstring root = SaveProfRoot();
    if (root.size() < 8) return;
    root = root.substr(0, root.size() - 8) + L"copie-avant-reworked\\";
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((root + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || fd.cFileName[0] == L'.') continue;
        SaveBak b; b.dir = root + fd.cFileName + L"\\" + (prof.empty() ? L"principale" : prof) + L"\\";
        WIN32_FIND_DATAW f2;
        HANDLE h2 = FindFirstFileW((b.dir + L"*").c_str(), &f2);
        if (h2 == INVALID_HANDLE_VALUE) continue;
        bool any = false;
        do {
            if (f2.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
            any = true;
            b.bytes += ((unsigned long long)f2.nFileSizeHigh << 32) | f2.nFileSizeLow;
            if (CompareFileTime(&f2.ftLastWriteTime, &b.when) > 0) b.when = f2.ftLastWriteTime;
        } while (FindNextFileW(h2, &f2));
        FindClose(h2);
        if (any) g_saveBak.push_back(b);
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    std::sort(g_saveBak.begin(), g_saveBak.end(), [](const SaveBak &a, const SaveBak &b) { return CompareFileTime(&a.when, &b.when) > 0; });
}
// Copie les fichiers (sans sous-dossiers) de src dans dst. Nombre copie.
static int SaveCopyFiles(const std::wstring &src, const std::wstring &dst)
{
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((src + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return 0;
    SHCreateDirectoryExW(NULL, dst.c_str(), NULL);
    int n = 0;
    do {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
        SetFileAttributesW((dst + fd.cFileName).c_str(), FILE_ATTRIBUTE_NORMAL);
        if (CopyFileW((src + fd.cFileName).c_str(), (dst + fd.cFileName).c_str(), FALSE)) n++;
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return n;
}
static void SaveRestore(int i, int k)
{
    if (i < 0 || i >= (int)g_saves.size() || k < 0 || k >= (int)g_saveBak.size() || !SaveGuard()) return;
    const SaveSlot s = g_saves[i];
    const SaveBak b = g_saveBak[k];
    wchar_t q[600];
    swprintf_s(q, T(L"Remettre la copie du %s dans « %s » ?\n\nLa sauvegarde actuelle de cette partie est remplacée (ses fichiers sont écrasés).",
                    L"Put the copy from %s back into \"%s\"?\n\nThe current save of this game is replaced (its files are overwritten)."), SaveDateText(b.when).c_str(), s.name.c_str());
    if (MessageBoxW(g_wnd, q, L"MWCoop", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) return;
    int n = SaveCopyFiles(b.dir, SaveDirOf(s.prof));
    TestLog("sauvegardes : copie %s remise dans %s (%d fichiers)", Narrow(b.dir, CP_UTF8).c_str(), s.prof.empty() ? "principale" : Narrow(s.prof, CP_UTF8).c_str(), n);
    if (n) SetStatus(K_OK, T(L"« %s » : copie du %s remise (%d fichiers)", L"\"%s\": copy from %s restored (%d files)"), s.name.c_str(), SaveDateText(b.when).c_str(), n);
    else SetStatus(K_ERR, T(L"Copie impossible à remettre", L"Could not restore the copy"));
    SavesScan();
}
// Dupliquer : une nouvelle partie-N avec les fichiers de la partie choisie.
static void SaveDuplicate(int i)
{
    if (i < 0 || i >= (int)g_saves.size() || !SaveGuard()) return;
    const SaveSlot s = g_saves[i];
    std::wstring name = SaveCleanName(s.name + T(L" (copie)", L" (copy)"));
    std::wstring prof = SaveCreate(name);
    if (prof.empty()) return;
    int n = s.has ? SaveCopyFiles(SaveDirOf(s.prof), SaveDirOf(prof)) : 0;
    TestLog("sauvegardes : %s dupliquee en %s (%d fichiers)", s.prof.empty() ? "principale" : Narrow(s.prof, CP_UTF8).c_str(), Narrow(prof, CP_UTF8).c_str(), n);
    SavesScan();
    for (int k = 0; k < (int)g_saves.size(); k++) if (g_saves[k].prof == prof) g_saveSel = k;
    SetStatus(K_OK, T(L"« %s » créée (%d fichiers copiés)", L"\"%s\" created (%d files copied)"), name.c_str(), n);
}
// Exporter : un zip (sans compression) des fichiers de la partie sur le Bureau, montre dans l'Explorateur. Noms a plat :
// a decompresser dans le dossier LocalLow\Amistech\My Winter Car (ou celui d'une partie-N) chez un ami.
static bool SaveZip(const std::wstring &zip, const std::wstring &dir)
{
    HANDLE f = CreateFileW(zip.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return false;
    std::string central;
    unsigned off = 0;
    int n = 0;
    bool ok = true;
    auto put = [&](const std::string &b) { DWORD w = 0; ok = ok && WriteFile(f, b.data(), (DWORD)b.size(), &w, NULL) && w == b.size(); off += (unsigned)b.size(); };
    auto u16 = [](std::string &s, unsigned v) { s += (char)(v & 255); s += (char)((v >> 8) & 255); };
    auto u32 = [&](std::string &s, unsigned v) { u16(s, v & 0xFFFF); u16(s, v >> 16); };
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((dir + L"*").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
            std::vector<unsigned char> d;
            if (!ReadAll(dir + fd.cFileName, d)) continue;
            std::string name = Narrow(fd.cFileName, CP_UTF8);
            FILETIME lt; WORD dd = 0x21, dt = 0;
            FileTimeToLocalFileTime(&fd.ftLastWriteTime, &lt); FileTimeToDosDateTime(&lt, &dd, &dt);
            unsigned crc = Crc32(d.data(), d.size()), size = (unsigned)d.size(), at = off;
            std::string hd, c;
            u32(hd, 0x04034b50); u16(hd, 20); u16(hd, 0x0800); u16(hd, 0); u16(hd, dt); u16(hd, dd); u32(hd, crc); u32(hd, size); u32(hd, size); u16(hd, (unsigned)name.size()); u16(hd, 0); hd += name;
            u32(c, 0x02014b50); u16(c, 20); u16(c, 20); u16(c, 0x0800); u16(c, 0); u16(c, dt); u16(c, dd); u32(c, crc); u32(c, size); u32(c, size); u16(c, (unsigned)name.size());
            u16(c, 0); u16(c, 0); u16(c, 0); u16(c, 0); u32(c, 0); u32(c, at); c += name;
            central += c;
            put(hd); put(std::string(d.begin(), d.end()));
            n++;
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    std::string e;
    u32(e, 0x06054b50); u16(e, 0); u16(e, 0); u16(e, n); u16(e, n); u32(e, (unsigned)central.size()); u32(e, off); u16(e, 0);
    put(central); put(e);
    CloseHandle(f);
    if (!ok || !n) DeleteFileW(zip.c_str());
    return ok && n > 0;
}
static void SaveExport(int i)
{
    if (i < 0 || i >= (int)g_saves.size()) return;
    const SaveSlot s = g_saves[i];
    if (!s.has) { SetStatus(K_WARN, T(L"Cette partie est vide : rien à exporter", L"This game is empty: nothing to export")); return; }
    wchar_t desk[MAX_PATH] = L"";
    if (FAILED(SHGetFolderPathW(NULL, CSIDL_DESKTOPDIRECTORY, NULL, SHGFP_TYPE_CURRENT, desk)) || !desk[0]) { SetStatus(K_ERR, T(L"Bureau introuvable", L"Desktop not found")); return; }
    std::wstring base;
    for (wchar_t c : s.name) base += wcschr(L"\\/:*?\"<>|", c) ? L'_' : c;
    SYSTEMTIME t; GetLocalTime(&t);
    wchar_t d[32]; swprintf_s(d, L"%04d%02d%02d-%02d%02d", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute);
    std::wstring zip = WithSlash(desk) + L"MWCoop-" + base + L"-" + d + L".zip";
    if (!SaveZip(zip, SaveDirOf(s.prof))) { SetStatus(K_ERR, T(L"Export impossible (%lu)", L"Could not export (%lu)"), GetLastError()); return; }
    TestLog("sauvegardes : %s exportee dans %s", s.prof.empty() ? "principale" : Narrow(s.prof, CP_UTF8).c_str(), Narrow(zip, CP_UTF8).c_str());
    SetStatus(K_OK, T(L"Exportée sur le Bureau : %s", L"Exported to the Desktop: %s"), zip.substr(zip.find_last_of(L'\\') + 1).c_str());
    ShellExecuteW(g_wnd, L"open", L"explorer.exe", (L"/select,\"" + zip + L"\"").c_str(), NULL, SW_SHOWNORMAL);
}

// Petits elements de la page : puce (fond leger, liseré), bouton rouge (Supprimer), tuile de chiffre, etiquette de section.
static void SaveChip(Graphics &g, RectF r, const std::wstring &s, Color fill, Color ink, Color border, float px = 11.5f)
{
    GraphicsPath p; RoundRect(p, r, r.Height / 2);
    SolidBrush b(fill); g.FillPath(&b, &p);
    Pen pen(border, 1.0f); g.DrawPath(&pen, &p);
    Text(g, s, r, px, FontStyleBold, ink);
}
static void SaveDangerButton(Graphics &g, RectF r, const std::wstring &s, int id, bool enabled)
{
    bool hot = enabled && g_uiHot == id;
    float a = enabled ? 1.0f : 0.4f;
    GraphicsPath p; RoundRect(p, r, 11);
    SolidBrush b(hot ? WithA(kRed, 0.18f) : TH(btn2)); g.FillPath(&b, &p);
    Pen pen(WithA(kRed, 0.55f * a), 1.0f); g.DrawPath(&pen, &p);
    Text(g, s, r, 12.5f, FontStyleBold, WithA(kRed, a));
    if (enabled) HotZone(r, id);
}
static void SaveStat(Graphics &g, RectF r, const std::wstring &big, const std::wstring &label)
{
    GraphicsPath p; RoundRect(p, r, 12);
    SolidBrush b(Color((BYTE)(g_dark ? 64 : 18), 0, 0, 0)); g.FillPath(&b, &p);
    Pen pen(TH(choiceBorder), 1.0f); g.DrawPath(&pen, &p);
    Text(g, big, RectF(r.X + 12, r.Y + 8, r.Width - 24, 24), 17, FontStyleBold, kInk, StringAlignmentNear);
    Text(g, label, RectF(r.X + 12, r.Y + 32, r.Width - 24, 18), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
}
static void SaveSection(Graphics &g, float x, float y, float w, const std::wstring &s) { Text(g, s, RectF(x, y, w, 16), 10.5f, FontStyleBold, kAcc, StringAlignmentNear); }
// Champ de saisie du nom (renommage dans la carte de droite ; fenetre : le meme dessin).
static void SaveNameField(Graphics &g, RectF fr, const std::wstring &txt)
{
    GraphicsPath fp; RoundRect(fp, fr, 10); SolidBrush fb(TH(field)); g.FillPath(&fb, &fp); Pen fpn(kAcc, 2); g.DrawPath(&fpn, &fp);
    RectF trr(fr.X + 14, fr.Y, fr.Width - 28, fr.Height);
    Text(g, txt.empty() ? T(L"Nom de la sauvegarde", L"Save name") : txt, trr, 16, FontStyleBold, txt.empty() ? TH(placeholder) : kInk, StringAlignmentNear);
    if (fmodf(g_time, 1.0f) < 0.55f) {
        float cw = txt.empty() ? 0 : MeasureW(g, txt, 16, FontStyleBold);
        float cx = min(trr.X + cw + 1, trr.X + trr.Width);
        Pen cp(kAcc, 1.6f); g.DrawLine(&cp, cx, fr.Y + 10, cx, fr.Y + fr.Height - 10);
    }
}

static void DrawSaves(Graphics &g)
{
    if (GetTickCount() - g_savesScanT > 3000 && g_saveEdit < 0) SavesScan();
    Glass(kSaveL); Glass(kSaveR);
    // en-tete : « Nouvelle partie » a droite du titre (avant les boutons de l'en-tete, ui.inc)
    // (« Nouvelle partie » : dans l'en-tete, ui.inc DrawHeader)
    std::wstring cur = SaveCurrentProf();
    int sel = SaveSelIndex();
    Graphics &mg = MeasureG();
    // ---- a gauche : la liste
    {
        RectF c = kSaveL;
        std::wstring t = T(L"Mes parties", L"My games");
        float tw = MeasureW(mg, t, 16, FontStyleBold);
        Title(g, t, RectF(c.X + 18, c.Y + 14, tw + 6, 26), 16, kInk);
        Text(g, L"· " + std::to_wstring(g_saves.size()), RectF(c.X + 18 + tw + 6, c.Y + 15, 60, 26), 13, FontStyleBold, kGrey, StringAlignmentNear);
        Text(g, T(L"Celle qui est cochée sera proposée dans le salon", L"The checked one will be offered in the lobby"), RectF(c.X + 200, c.Y + 16, c.Width - 218, 24), 12, FontStyleRegular, kGrey, StringAlignmentFar);
        float ms = SavesMaxScroll(), sc = g_scroll[TAB_SAVES] = min(g_scroll[TAB_SAVES], ms);
        float rw = kSaveList.Width - (ms > 0 ? 14 : 0);
        GraphicsState st = g.Save(); g.SetClip(kSaveList, CombineModeIntersect);
        for (int i = 0; i <= (int)g_saves.size(); i++) {
            RectF r(kSaveList.X, kSaveList.Y + i * kSaveRowH - sc, rw, kSaveRowH - 8);
            if (r.Y + r.Height < kSaveList.Y || r.Y > kSaveList.Y + kSaveList.Height) continue;
            bool vis = r.Y >= kSaveList.Y - 1 && r.Y + r.Height <= kSaveList.Y + kSaveList.Height + 1;
            if (i == (int)g_saves.size()) {   // ligne « Nouvelle partie » (pointilles)
                bool hot = g_uiHot == UI_SAVE_NEW;
                GraphicsPath p; RoundRect(p, r, 16);
                SolidBrush b(hot ? TH(cardSel) : WithA(TH(card), 0.6f)); g.FillPath(&b, &p);
                Pen pen(hot ? WithA(kAcc, 0.8f) : TH(choiceBorder), 1.2f); pen.SetDashStyle(DashStyleDash); g.DrawPath(&pen, &p);
                RectF sq(r.X + 14, r.Y + 14, 46, 46);
                GraphicsPath sp; RoundRect(sp, sq, 13);
                SolidBrush sb(Color((BYTE)(g_dark ? 50 : 14), 0, 0, 0)); g.FillPath(&sb, &sp);
                Pen spn(WithA(kInk, 0.3f), 1.0f); spn.SetDashStyle(DashStyleDash); g.DrawPath(&spn, &sp);
                Icon(g, IC_PLUS, sq.X + 23, sq.Y + 23, 18, kGrey, 2.4f);
                Para(g, T(L"Nouvelle partie : une sauvegarde neuve, ou une copie de celle d'un ami (zip reçu).",
                          L"New game: a fresh save, or a copy of a friend's (zip received)."), RectF(r.X + 76, r.Y + 16, r.Width - 92, 44), 12.5f, kGrey);
                if (vis) HotZone(r, UI_SAVE_NEW);
                continue;
            }
            const SaveSlot &s = g_saves[i];
            bool used = s.prof == cur, on = i == sel;
            int base = UI_SAVE + i * 8;
            bool hot = g_uiHot == base + SP_SEL;
            GraphicsPath p; RoundRect(p, r, 16);
            SolidBrush rb(on ? WithA(kAcc, 0.12f) : hot ? TH(cardSel) : used ? WithA(kAcc, 0.06f) : TH(card)); g.FillPath(&rb, &p);
            Pen rp(on ? kAcc : hot ? WithA(kAcc, 0.6f) : TH(choiceBorder), on ? 2.0f : 1.0f); g.DrawPath(&rp, &p);
            RectF sq(r.X + 14, r.Y + 14, 46, 46);   // carre numerote (maison : la sauvegarde solo du jeu)
            GraphicsPath sp; RoundRect(sp, sq, 13);
            SolidBrush sb(Color((BYTE)(g_dark ? 76 : 18), 0, 0, 0)); g.FillPath(&sb, &sp);
            Pen spn(TH(fieldBorder), 1.0f); g.DrawPath(&spn, &sp);
            if (i == 0) Icon(g, IC_HOME, sq.X + 23, sq.Y + 23, 22, on || used ? kAcc : kInk, 1.9f);
            else Text(g, std::to_wstring(i), sq, 17, FontStyleBold, on || used ? kAcc : kInk);
            float tx = r.X + 76;
            std::wstring sz = SaveSizeText(s.bytes);
            float szw = PillW(sz, 11.5f) + 4;
            SaveChip(g, RectF(r.X + r.Width - 14 - szw, r.Y + 24, szw, 26), sz, WithA(kInk, 0.08f), kInk, TH(choiceBorder));
            float nw = min(MeasureW(mg, s.name, 15, FontStyleBold) + 4, r.Width - 76 - 14 - szw - 100);
            Text(g, s.name, RectF(tx, r.Y + 13, nw, 22), 15, FontStyleBold, kInk, StringAlignmentNear);
            if (used) { const wchar_t *u = T(L"En cours", L"In use"); float uw = PillW(u, 10.5f); SaveChip(g, RectF(tx + nw + 6, r.Y + 14, uw, 20), u, WithA(kOk, 0.18f), kOk, WithA(kOk, 0.35f), 10.5f); }
            Text(g, SaveRowInfo(s), RectF(tx, r.Y + 37, r.Width - 76 - 14 - szw - 10, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
            if (vis) HotZone(r, base + SP_SEL);
        }
        g.Restore(st);
        if (ms > 0) DrawScrollBar(g, kSaveList, sc, ms);
        Text(g, T(L"Ta sauvegarde solo (celle du jeu) n'est jamais touchée par MWCoop.", L"Your solo save (the game's) is never touched by MWCoop."),
             RectF(c.X + 18, c.Y + 572, c.Width - 36, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        // dossier de la partie choisie + Ouvrir
        float fy = c.Y + c.Height - 16 - 36;
        Text(g, T(L"Dossier", L"Folder"), RectF(c.X + 18, fy, 52, 36), 12, FontStyleBold, kGrey, StringAlignmentNear);
        RectF pr(c.X + 74, fy, c.Width - 74 - 18 - 10 - 90, 36);
        GraphicsPath pp; RoundRect(pp, pr, 10);
        SolidBrush pb(Color((BYTE)(g_dark ? 76 : 18), 0, 0, 0)); g.FillPath(&pb, &pp);
        Pen ppn(TH(fieldBorder), 1.0f); g.DrawPath(&ppn, &pp);
        std::wstring path = sel >= 0 ? SaveDirOf(g_saves[sel].prof) : L"";
        if (!path.empty() && path.back() == L'\\') path.pop_back();
        while (!path.empty() && MeasureW(mg, L"…" + path, 12, FontStyleRegular) > pr.Width - 24) {   // (le debut coupe : la fin compte)
            size_t k = path.find(L'\\');
            if (k == std::wstring::npos) break;
            path = path.substr(k + 1);
        }
        Text(g, path.empty() ? L"" : L"…\\" + path, RectF(pr.X + 12, pr.Y, pr.Width - 24, 36), 12, FontStyleRegular, kInk, StringAlignmentNear);
        UiButton(g, RectF(c.X + c.Width - 18 - 90, fy, 90, 36), T(L"Ouvrir", L"Open"), sel >= 0 ? UI_SAVE + sel * 8 + SP_DIR : UI_NONE, false, -1, sel >= 0, 11);
    }
    // ---- a droite : la partie choisie
    {
        RectF c = kSaveR;
        if (sel < 0) { Text(g, T(L"Aucune partie", L"No game"), RectF(c.X, c.Y + 60, c.Width, 30), 15, FontStyleBold, kGrey); return; }
        const SaveSlot &s = g_saves[sel];
        if (g_saveBakFor != s.prof || GetTickCount() - g_saveBakT > 3000) SaveBakScan(s.prof);
        int base = UI_SAVE + sel * 8;
        float x = c.X + 18, w = c.Width - 36, y = c.Y + 14;
        if (g_saveEdit == sel) {   // renommage : champ, Entree / Echap, Garder / Annuler
            SaveNameField(g, RectF(x, y, w, 40), g_saveEditText);
            Text(g, T(L"Entrée : garder · Échap : annuler", L"Enter: keep · Esc: cancel"), RectF(x, y + 44, w - 230, 36), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            UiButton(g, RectF(x + w - 100, y + 46, 100, 34), T(L"Garder", L"Keep"), base + SP_OK, true, IC_CHECK, true, 11);
            UiButton(g, RectF(x + w - 100 - 8 - 100, y + 46, 100, 34), T(L"Annuler", L"Cancel"), base + SP_CANCEL, false, -1, true, 11);
            y += 92;
        } else {
            Title(g, s.name, RectF(x, y, w - 118, 36), 16, kInk);
            UiButton(g, RectF(x + w - 110, y, 110, 36), T(L"Renommer", L"Rename"), base + SP_RENAME, false, -1, true, 11);
            y += 48;
        }
        // trois chiffres (partie.txt du mod) : jour, argent liquide, heures jouees ; « — » sans fichier. Le jour de la semaine du
        // jeu n'est pas connu du lanceur (GlobalDaysPassed compte les jours, pas la semaine) : sous-titre « — ».
        float sw = (w - 16) / 3;
        wchar_t hb[32] = L"—";
        if (s.stats && s.heures >= 0) swprintf_s(hb, s.heures < 10 ? L"%.1f h" : L"%.0f h", s.heures);
        if (g_fr) for (wchar_t *c2 = hb; *c2; c2++) if (*c2 == L'.') *c2 = L',';
        SaveStat(g, RectF(x, y, sw, 58), s.stats && s.jour >= 0 ? T(L"Jour ", L"Day ") + std::to_wstring(s.jour) : L"—", L"—");
        SaveStat(g, RectF(x + sw + 8, y, sw, 58), s.stats && s.argent >= 0 ? SaveMoney(s.argent) : L"—", L"MK");
        SaveStat(g, RectF(x + 2 * (sw + 8), y, sw, 58), hb, T(L"JOUÉES", L"PLAYED"));
        y += 68;
        {   // derniere partie : « Hier 22:07 · toi + Teppo, Pekka », puis le role et la taille
            RectF b(x, y, w, 84);
            GraphicsPath p; RoundRect(p, b, 14);
            SolidBrush bb(TH(card)); g.FillPath(&bb, &p);
            Pen pn(TH(choiceBorder), 1.0f); g.DrawPath(&pn, &p);
            SaveSection(g, b.X + 14, b.Y + 11, b.Width - 28, T(L"DERNIÈRE PARTIE", L"LAST GAME"));
            std::wstring l1, l2;
            if (!s.has) { l1 = T(L"Jamais jouée", L"Never played"); l2 = T(L"Vide : commencera par une nouvelle partie", L"Empty: will start with a new game"); }
            else if (!s.stats) { l1 = SaveDateText(s.mtime); l2 = T(L"Enregistrée par le jeu · ", L"Saved by the game · ") + SaveSizeText(s.bytes); }
            else {
                l1 = (s.date.empty() ? SaveDateText(s.mtime) : SaveDateRel(s.date, true)) + L" · " + SaveWith(s, true);
                l2 = (s.role == L"hote" ? T(L"Tu hébergeais", L"You were hosting") : s.role == L"invite" ? T(L"Chez l'hôte, sa sauvegarde", L"At the host's, their save") : T(L"En solo", L"Solo"));
                if (s.banque >= 0) l2 += T(L" · banque ", L" · bank ") + SaveMoney(s.banque) + L" mk";
                l2 += L" · " + SaveSizeText(s.bytes);
            }
            Text(g, l1, RectF(b.X + 14, b.Y + 30, b.Width - 28, 22), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
            Text(g, l2, RectF(b.X + 14, b.Y + 54, b.Width - 28, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
            y += 96;
        }
        {   // copies de secours
            SaveSection(g, x, y, w, T(L"COPIES DE SECOURS", L"BACKUP COPIES"));
            Text(g, g_saveBak.empty() ? T(L"Aucune copie pour cette partie.", L"No copy for this game.") : T(L"Faites avant le premier passage en Reworked.", L"Made before the first switch to Reworked."),
                 RectF(x, y + 18, w, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
            float by = y + 42;
            for (int k = 0; k < (int)g_saveBak.size() && k < 3; k++) {
                const SaveBak &b = g_saveBak[k];
                Text(g, SaveDateText(b.when) + L" · " + SaveSizeText(b.bytes), RectF(x, by, w - 110, 30), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
                UiButton(g, RectF(x + w - 100, by, 100, 30), T(L"Restaurer", L"Restore"), UI_SAVE_RESTORE + k, false, -1, s.prof == g_saveBakFor, 10);
                by += 36;
            }
            if (g_saveBak.size() > 3) { wchar_t more[48]; swprintf_s(more, T(L"+ %d autres", L"+ %d more"), (int)g_saveBak.size() - 3); Text(g, more, RectF(x, by, w, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear); }
        }
        // en bas : utiliser (si ce n'est pas la partie en cours), puis les quatre actions
        float ay = c.Y + c.Height - 16 - 36 * 2 - 8, bw = (w - 8) / 2;
        if (s.prof != cur) UiButton(g, RectF(x, ay - 8 - 44, w, 44), T(L"Utiliser pour héberger", L"Use for hosting"), base + SP_USE, true, IC_CHECK);
        UiButton(g, RectF(x, ay, bw, 36), T(L"Dupliquer", L"Duplicate"), UI_SAVE_DUP, false, -1, true, 11);
        UiButton(g, RectF(x + bw + 8, ay, bw, 36), T(L"Exporter (zip)", L"Export (zip)"), UI_SAVE_EXPORT, false, -1, s.has, 11);
        UiButton(g, RectF(x, ay + 44, bw, 36), T(L"Recommencer", L"Restart"), UI_SAVE_RESTART, false, -1, s.has, 11);
        SaveDangerButton(g, RectF(x + bw + 8, ay + 44, bw, 36), T(L"Supprimer", L"Delete"), UI_SAVE_DEL, sel > 0);
    }
}

// ---- fenetre (salon : choisir, nouvelle partie, recommencer, nom)
static void DrawSaveModal(Graphics &g)
{
    if (g_saveModal == SM_NONE) return;
    HotZone(RectF(0, 0, 1280, 800), UI_NONE);   // (rien en dessous)
    SolidBrush dim(Color(140, 0, 0, 0)); g.FillRectangle(&dim, RectF(0, 0, 1280, 800));
    bool list = g_saveModal == SM_PICK || g_saveModal == SM_RESTART;
    int rows = list ? (int)g_saves.size() + (g_saveModal == SM_PICK ? 1 : 0) : 0;
    if (rows > 7) rows = 7;
    float h = g_saveModal == SM_NAME ? 250 : g_saveModal == SM_NEWGAME ? 300 : 150 + rows * 62;
    RectF c(640 - 290, 400 - h / 2, 580, h);
    {   // (fond plein : le verre laissait voir la page dessous)
        GraphicsPath bp; RoundRect(bp, c, 20);
        SolidBrush bb(g_dark ? Color(255, 18, 26, 42) : Color(255, 244, 247, 251)); g.FillPath(&bb, &bp);
        Pen bpn(g_dark ? Color(90, 255, 255, 255) : Color(60, 0, 0, 0), 1.0f); g.DrawPath(&bpn, &bp);
    }
    const wchar_t *title = g_saveModal == SM_PICK ? T(L"Sauvegarde de la partie", L"Game save") : g_saveModal == SM_NEWGAME ? T(L"Nouvelle partie", L"New game")
                         : g_saveModal == SM_RESTART ? T(L"Recommencer une sauvegarde", L"Restart a save") : T(L"Nom de la sauvegarde", L"Save name");
    Title(g, title, RectF(c.X + 24, c.Y + 18, c.Width - 100, 30), 19, kInk);
    UiButton(g, RectF(c.X + c.Width - 24 - 40, c.Y + 16, 40, 36), L"", UI_SAVE_MCLOSE, false, IC_CLOSE);
    float y = c.Y + 62;
    if (g_saveModal == SM_NEWGAME) {
        struct { const wchar_t *t, *s; int id, icon; } opt[2] = {
            { T(L"Nouvelle sauvegarde", L"New save"), T(L"Une partie neuve dans une sauvegarde à part : tes autres parties ne sont pas touchées.", L"A fresh game in a separate save: your other games are not touched."), UI_SAVE_MFRESH, IC_HOMEPLUS },
            { T(L"Recommencer une sauvegarde", L"Restart a save"), T(L"Efface une de tes sauvegardes et la recommence de zéro.", L"Erases one of your saves and restarts it from scratch."), UI_SAVE_MRESTART, IC_UNDO } };
        for (int k = 0; k < 2; k++) {
            RectF r(c.X + 24, y + k * 104, c.Width - 48, 92);
            bool hot = g_uiHot == opt[k].id;
            GraphicsPath p; RoundRect(p, r, 14);
            SolidBrush b(hot ? TH(cardSel) : TH(card)); g.FillPath(&b, &p);
            Pen pn(hot ? kAcc : TH(choiceBorder), hot ? 1.6f : 1.0f); g.DrawPath(&pn, &p);
            Icon(g, opt[k].icon, r.X + 34, r.Y + r.Height / 2, 24, k == 1 ? kWarn : kAcc, 1.9f);
            Text(g, opt[k].t, RectF(r.X + 64, r.Y + 14, r.Width - 80, 26), 16, FontStyleBold, kInk, StringAlignmentNear);
            Para(g, opt[k].s, RectF(r.X + 64, r.Y + 42, r.Width - 80, 44), 12.5f, kGrey);
            HotZone(r, opt[k].id);
        }
        return;
    }
    if (g_saveModal == SM_NAME) {
        Para(g, g_saveNameForGame ? T(L"Une sauvegarde neuve pour cette partie. Tes autres sauvegardes ne bougent pas.", L"A new save for this game. Your other saves stay as they are.")
                                  : T(L"Elle sera utilisée quand tu héberges.", L"It will be used when you host."), RectF(c.X + 24, y, c.Width - 48, 40), 12.5f, kGrey);
        RectF fr(c.X + 24, y + 46, c.Width - 48, 44);
        GraphicsPath fp; RoundRect(fp, fr, 10); SolidBrush fb(TH(field)); g.FillPath(&fb, &fp); Pen fpn(kAcc, 2); g.DrawPath(&fpn, &fp);
        RectF trr(fr.X + 14, fr.Y, fr.Width - 28, fr.Height);
        Text(g, g_saveNameText.empty() ? T(L"Nom de la sauvegarde", L"Save name") : g_saveNameText, trr, 16, FontStyleBold, g_saveNameText.empty() ? TH(placeholder) : kInk, StringAlignmentNear);
        if (fmodf(g_time, 1.0f) < 0.55f) {
            float cw = g_saveNameText.empty() ? 0 : MeasureW(g, g_saveNameText, 16, FontStyleBold);
            float cx = min(trr.X + cw + 1, trr.X + trr.Width);
            Pen cp(kAcc, 1.6f); g.DrawLine(&cp, cx, fr.Y + 11, cx, fr.Y + fr.Height - 11);
        }
        UiButton(g, RectF(c.X + c.Width - 24 - 150, c.Y + c.Height - 24 - 44, 150, 44), T(L"Créer", L"Create"), UI_SAVE_MOK, true, IC_CHECK, !SaveCleanName(g_saveNameText).empty());
        UiButton(g, RectF(c.X + c.Width - 24 - 150 - 12 - 130, c.Y + c.Height - 24 - 44, 130, 44), T(L"Annuler", L"Cancel"), UI_SAVE_MCLOSE, false, -1);
        if (!g_saveNameForGame) UiButton(g, RectF(c.X + 24, c.Y + c.Height - 24 - 44, 170, 44), T(L"Depuis un zip…", L"From a zip…"), UI_SAVE_IMPORT, false, IC_ZIP, !SaveCleanName(g_saveNameText).empty());   // (la sauvegarde d'un ami)
        return;
    }
    // listes : choisir (avec « Nouvelle sauvegarde… » en bas) ou recommencer
    Para(g, g_saveModal == SM_PICK ? T(L"Tes invités reçoivent la sauvegarde choisie en arrivant ; la leur n'est pas touchée.", L"Your guests receive the chosen save when they arrive; theirs is not touched.")
                                   : T(L"Choisis la sauvegarde à effacer et recommencer.", L"Pick the save to erase and restart."), RectF(c.X + 24, y, c.Width - 48, 36), 12.5f, kGrey);
    y += 42;
    std::wstring cur = SaveCurrentProf();
    int n = 0;
    for (int i = 0; i < (int)g_saves.size() && n < 7; i++, n++) {
        const SaveSlot &s = g_saves[i];
        RectF r(c.X + 24, y + n * 62, c.Width - 48, 54);
        bool hot = g_uiHot == UI_SAVE_PICK + i, used = s.prof == cur;
        GraphicsPath p; RoundRect(p, r, 12);
        SolidBrush b(hot ? TH(cardSel) : used ? WithA(kAcc, 0.10f) : TH(card)); g.FillPath(&b, &p);
        Pen pn(used ? kAcc : hot ? WithA(kAcc, 0.6f) : TH(choiceBorder), used ? 1.6f : 1.0f); g.DrawPath(&pn, &p);
        Text(g, s.name, RectF(r.X + 16, r.Y + 6, r.Width - 140, 22), 14.5f, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, SaveWhen(s), RectF(r.X + 16, r.Y + 28, r.Width - 32, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        if (used) Pill(g, RectF(r.X + r.Width - 12 - PillW(T(L"UTILISÉE", L"IN USE"), 11), r.Y + 8, PillW(T(L"UTILISÉE", L"IN USE"), 11), 20), WithA(kAcc, 0.22f), T(L"UTILISÉE", L"IN USE"), kAcc, 11);
        if (g_saveModal == SM_RESTART && !s.has) continue;   // (vide : rien a recommencer)
        HotZone(r, UI_SAVE_PICK + i);
    }
    if (g_saveModal == SM_PICK && n < 7) {
        RectF r(c.X + 24, y + n * 62, c.Width - 48, 54);
        bool hot = g_uiHot == UI_SAVE_MNEW;
        GraphicsPath p; RoundRect(p, r, 12);
        SolidBrush b(hot ? TH(cardSel) : TH(card)); g.FillPath(&b, &p);
        Pen pn(hot ? kAcc : TH(choiceBorder), 1.0f); g.DrawPath(&pn, &p);
        Icon(g, IC_HOMEPLUS, r.X + 28, r.Y + r.Height / 2, 18, kAcc, 1.9f);
        Text(g, T(L"Nouvelle sauvegarde…", L"New save…"), RectF(r.X + 50, r.Y, r.Width - 60, r.Height), 14.5f, FontStyleBold, kAcc, StringAlignmentNear);
        HotZone(r, UI_SAVE_MNEW);
    }
}

static void SaveClick(int id)
{
    if (id == UI_SAVE_MCLOSE) { g_saveModal = SM_NONE; return; }
    if (id == UI_SAVE_REFRESH) { g_saveEdit = -1; SavesScan(); return; }
    if (id == UI_SAVE_NEW) { if (SaveGuard()) SaveAskName(false); return; }
    if (id == UI_SAVE_LOBBY) { SaveOpenPick(); return; }
    if (id == UI_SAVE_MNEW) { if (SaveGuard()) SaveAskName(false); return; }
    if (id == UI_SAVE_MFRESH) { if (SaveGuard()) SaveAskName(true); return; }
    if (id == UI_SAVE_MRESTART) { SavesScan(); g_saveModal = SM_RESTART; return; }
    if (id == UI_SAVE_MOK) { SaveNameDone(); return; }
    if (id == UI_SAVE_IMPORT) { SaveImportZip(); return; }
    if (id >= UI_SAVE_PICK && id < UI_SAVE_END) { SavePickRow(id - UI_SAVE_PICK); return; }
    int sel = SaveSelIndex();
    if (id == UI_SAVE_DUP) { SaveDuplicate(sel); return; }
    if (id == UI_SAVE_EXPORT) { SaveExport(sel); return; }
    if (id == UI_SAVE_DEL) { SaveDelete(sel); if (sel >= (int)g_saves.size()) g_saveSel = -1; return; }
    if (id == UI_SAVE_RESTART) { if (sel >= 0 && SaveGuard()) { g_saveModal = SM_RESTART; SavePickRow(sel); g_saveModal = SM_NONE; } return; }   // (meme confirmation que la fenetre)
    if (id >= UI_SAVE_RESTORE && id < UI_SAVE_RESTORE + 10) { SaveRestore(sel, id - UI_SAVE_RESTORE); return; }
    if (id >= UI_SAVE && id < UI_SAVE_PICK) {
        int i = (id - UI_SAVE) / 8, part = (id - UI_SAVE) % 8;
        if (i < 0 || i >= (int)g_saves.size()) return;
        if (part == SP_SEL) { if (g_saveEdit >= 0 && g_saveEdit != i) g_saveEdit = -1; g_saveSel = i; return; }
        if (part == SP_USE) { SaveSetCurrent(g_saves[i].prof); SetStatus(K_OK, T(L"Sauvegarde utilisée : %s", L"Save in use: %s"), g_saves[i].name.c_str()); return; }
        if (part == SP_RENAME) { g_saveSel = i; g_saveEdit = i; g_saveEditText = g_saves[i].name; g_time = 0; g_focus = -1; return; }
        if (part == SP_DIR) { SaveOpenDir(i); return; }
        if (part == SP_DEL) { SaveDelete(i); return; }
        if (part == SP_CANCEL) { g_saveEdit = -1; return; }
        if (part == SP_OK) { SaveChar(13); return; }
    }
}

// ---------------------------------------------------------------- JEU (switch MSC / MWC) et copie des sauvegardes
static RectF g_canalRect;   // (bulle du guide ; switch MSC / MWC sous le numero de version, ecran principal)
// Copie des sauvegardes (principale et partie-N : les fichiers du dossier LocalLow, sans sous-dossiers) avant le premier
// passage en Reworked : MWCoop\copie-avant-reworked\<date>\<principale | partie-N>.
static std::wstring SavesBackupNightly()
{
    std::wstring root = SaveProfRoot();
    root = root.substr(0, root.size() - 8);   // (sans "profils\")
    SYSTEMTIME t; GetLocalTime(&t);
    wchar_t d[32]; swprintf_s(d, L"%04d-%02d-%02d_%02d%02d", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute);
    std::wstring dst = root + L"copie-avant-reworked\\" + d + L"\\";
    SavesScan();
    int files = 0;
    for (const SaveSlot &sl : g_saves) {
        std::wstring src = SaveDirOf(sl.prof), to = dst + (sl.prof.empty() ? L"principale" : sl.prof) + L"\\";
        WIN32_FIND_DATAW fd;
        HANDLE h = src.empty() ? INVALID_HANDLE_VALUE : FindFirstFileW((src + L"*").c_str(), &fd);
        if (h == INVALID_HANDLE_VALUE) continue;
        SHCreateDirectoryExW(NULL, to.c_str(), NULL);
        do {
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
            if (CopyFileW((src + fd.cFileName).c_str(), (to + fd.cFileName).c_str(), FALSE)) files++;
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    TestLog("canal : %d fichier(s) de sauvegarde copies dans %s", files, Narrow(dst, CP_UTF8).c_str());
    return files ? dst : L"";
}
// Choisir le jeu (switch MSC / MWC sous le numero de version) : [Lanceur] JeuChoisi, puis le lanceur se relance sur l'autre
// jeu (dossier, mod, sauvegardes, Steam : tout est relu au demarrage). host : demande venue du salon (l'hote joue a l'autre
// jeu), avec une question. Faux : rien de change (meme jeu, refus, ou pas possible maintenant).
static bool GameProcessRunning(DWORD *pidOut);
static bool GameSet(int game, bool host)
{
    if (game == g_game || game < 0 || game > 1) return false;
    if (g_busy || GameProcessRunning(NULL)) {
        SetStatus(K_WARN, T(L"Ferme le jeu et attends la fin de la mise à jour pour changer de jeu", L"Close the game and wait for the update to finish to switch games"));
        return false;
    }
    if (host) {
        wchar_t q[600];
        swprintf_s(q, T(L"L'hôte joue à %s : il faut le même jeu pour jouer ensemble.\n\nPasser sur %s ? Le lanceur se relance.",
                        L"The host plays %s: you need the same game to play together.\n\nSwitch to %s? The launcher restarts."), kGames[game].name, kGames[game].name);
        if (MessageBoxW(g_wnd, q, L"MWCoop", MB_YESNO | MB_ICONQUESTION) != IDYES) return false;
    } else if (g_lobby != LB_NONE) {
        SetStatus(K_WARN, T(L"Quitte le salon pour changer de jeu", L"Leave the lobby to switch games"));
        return false;
    }
    WritePrivateProfileStringW(L"Lanceur", L"JeuChoisi", kGames[game].id, g_iniLauncher.c_str());
    TestLog("jeu : %s", Narrow(kGames[game].id).c_str());
    LaunchLog("jeu choisi : %s, le lanceur se relance", Narrow(kGames[game].name).c_str());
    if (g_lobby != LB_NONE) LobbyClose();
    PostMessageW(g_wnd, WM_APP_RELAUNCH, 0, 0);
    return true;
}
