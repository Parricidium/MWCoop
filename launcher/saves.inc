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

struct SaveSlot { std::wstring prof, name; bool has = false; FILETIME mtime = {}; unsigned long long bytes = 0; };
static std::vector<SaveSlot> g_saves;
static DWORD g_savesScanT;
static int g_saveEdit = -1; static std::wstring g_saveEditText;   // renommage dans la liste de l'onglet
static int g_saveDelArm = -1; static DWORD g_saveDelT;
enum { SM_NONE, SM_PICK, SM_NEWGAME, SM_RESTART, SM_NAME };      // fenetre par-dessus le lanceur
static int g_saveModal = SM_NONE;
static bool g_saveNameForGame;                                     // nom demande pour « Nouvelle partie » (salon) : appliquee ensuite
static std::wstring g_saveNameText;
static const int kSaveNameMax = 32;
enum { UI_SAVE_NEW = 9000, UI_SAVE_REFRESH, UI_SAVE_MCLOSE, UI_SAVE_MNEW, UI_SAVE_MOK, UI_SAVE_MRESTART, UI_SAVE_MFRESH, UI_SAVE_LOBBY,
       UI_SAVE = 9100,       // + ligne * 8 + partie (onglet)
       UI_SAVE_PICK = 9700,  // + ligne (fenetre : liste)
       UI_SAVE_END = 9900 };
enum { SP_USE, SP_RENAME, SP_DIR, SP_DEL, SP_OK, SP_CANCEL };

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
    if (!prof.empty()) return SaveProfRoot() + prof + L"\\LocalLow\\Amistech\\My Winter Car\\";
    wchar_t up[MAX_PATH] = L"";
    GetEnvironmentVariableW(L"USERPROFILE", up, MAX_PATH);
    return up[0] ? WithSlash(up) + L"AppData\\LocalLow\\Amistech\\My Winter Car\\" : L"";
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

static void SaveInfo(SaveSlot &s)
{
    std::wstring d = SaveDirOf(s.prof);
    WIN32_FILE_ATTRIBUTE_DATA a;
    s.has = !d.empty() && GetFileAttributesExW((d + L"savefile.txt").c_str(), GetFileExInfoStandard, &a);
    if (s.has) s.mtime = a.ftLastWriteTime;
    s.bytes = 0;
    WIN32_FIND_DATAW fd;
    HANDLE h = d.empty() ? INVALID_HANDLE_VALUE : FindFirstFileW((d + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do { if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) s.bytes += ((unsigned long long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow; } while (FindNextFileW(h, &fd));
    FindClose(h);
}
static void SavesScan()
{
    g_savesScanT = GetTickCount();
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
        if (SHCreateDirectoryExW(NULL, (root + prof + L"\\LocalLow\\Amistech\\My Winter Car").c_str(), NULL) != ERROR_SUCCESS) break;
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

// ---- onglet
static const RectF kSaveList(288, 236, 952, 492);
static const float kSaveRowH = 84;
static float SavesMaxScroll() { return max(0.0f, (float)g_saves.size() * kSaveRowH - kSaveList.Height); }

static void DrawSaves(Graphics &g)
{
    if (GetTickCount() - g_savesScanT > 3000 && g_saveEdit < 0) SavesScan();
    RectF c = kMain; Glass(c);
    Title(g, T(L"Tes sauvegardes", L"Your saves"), RectF(c.X + 24, c.Y + 18, 500, 30), 20, kInk);
    Para(g, T(L"Une partie par groupe d'amis : HÉBERGER joue la sauvegarde utilisée (l'hôte la choisit aussi dans le salon) ; JOUER EN SOLO garde la partie principale. En invité, tu joues la sauvegarde de l'hôte, à part : les tiennes ne sont jamais touchées.",
              L"One game per group of friends: HOST plays the save in use (the host also picks it in the lobby); PLAY SOLO keeps the main game. As a guest, you play the host's save, kept apart: yours are never touched."),
         RectF(c.X + 24, c.Y + 54, c.Width - 48 - 250, 64), 12.5f, kGrey);
    UiButton(g, RectF(c.X + c.Width - 24 - 236, c.Y + 16, 236, 40), T(L"Nouvelle sauvegarde…", L"New save…"), UI_SAVE_NEW, true, IC_HOMEPLUS);
    UiButton(g, RectF(c.X + c.Width - 24 - 40, c.Y + 66, 40, 36), L"", UI_SAVE_REFRESH, false, IC_UNDO);
    std::wstring cur = SaveCurrentProf();
    float sc = g_scroll[TAB_SAVES] = min(g_scroll[TAB_SAVES], SavesMaxScroll()), ms = SavesMaxScroll();
    GraphicsState st = g.Save(); g.SetClip(kSaveList, CombineModeIntersect);
    for (int i = 0; i < (int)g_saves.size(); i++) {
        const SaveSlot &s = g_saves[i];
        RectF r(kSaveList.X, kSaveList.Y + i * kSaveRowH - sc, kSaveList.Width - (ms > 0 ? 14 : 0), kSaveRowH - 10);
        if (r.Y + r.Height < kSaveList.Y || r.Y > kSaveList.Y + kSaveList.Height) continue;
        bool vis = r.Y >= kSaveList.Y - 1 && r.Y + r.Height <= kSaveList.Y + kSaveList.Height + 1;
        bool used = s.prof == cur;
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush rb(used ? WithA(kAcc, 0.10f) : TH(card)); g.FillPath(&rb, &p);
        Pen rp(used ? kAcc : TH(choiceBorder), used ? 2.0f : 1.0f); g.DrawPath(&rp, &p);
        Icon(g, i == 0 ? IC_HOME : IC_FILE, r.X + 32, r.Y + r.Height / 2, 24, used ? kAcc : kGrey, 1.8f);
        float tx = r.X + 60, bx = r.X + r.Width - 16;
        int base = UI_SAVE + i * 8;
        if (g_saveEdit == i) {
            RectF fr(tx, r.Y + 12, r.Width - 60 - 16 - 250, 40);
            GraphicsPath fp; RoundRect(fp, fr, 10); SolidBrush fb(TH(field)); g.FillPath(&fb, &fp); Pen fpn(kAcc, 2); g.DrawPath(&fpn, &fp);
            RectF trr(fr.X + 14, fr.Y, fr.Width - 28, fr.Height);
            Text(g, g_saveEditText.empty() ? T(L"Nom de la sauvegarde", L"Save name") : g_saveEditText, trr, 16, FontStyleBold, g_saveEditText.empty() ? TH(placeholder) : kInk, StringAlignmentNear);
            if (fmodf(g_time, 1.0f) < 0.55f) {
                float cw = g_saveEditText.empty() ? 0 : MeasureW(g, g_saveEditText, 16, FontStyleBold);
                float cx = min(trr.X + cw + 1, trr.X + trr.Width);
                Pen cp(kAcc, 1.6f); g.DrawLine(&cp, cx, fr.Y + 10, cx, fr.Y + fr.Height - 10);
            }
            Text(g, T(L"Entrée : garder · Échap : annuler", L"Enter: keep · Esc: cancel"), RectF(tx, r.Y + 54, fr.Width, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            if (vis) {
                UiButton(g, RectF(bx - 112, r.Y + 16, 112, 40), T(L"Garder", L"Keep"), base + SP_OK, true, IC_CHECK);
                UiButton(g, RectF(bx - 112 - 10 - 112, r.Y + 16, 112, 40), T(L"Annuler", L"Cancel"), base + SP_CANCEL, false, IC_CLOSE);
            }
            continue;
        }
        Text(g, s.name, RectF(tx, r.Y + 10, r.Width - 60 - 470, 28), 17, FontStyleBold, kInk, StringAlignmentNear);
        std::wstring info = SaveWhen(s);
        if (i == 0) info += T(L" · la sauvegarde normale du jeu", L" · the game's normal save");
        Text(g, info, RectF(tx, r.Y + 40, r.Width - 60 - 470, 22), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        if (!vis) continue;
        float x = bx;
        bool arm = g_saveDelArm == i && GetTickCount() - g_saveDelT < 4000;
        if (i > 0) { x -= 44; UiButton(g, RectF(x, r.Y + 16, 44, 40), L"", base + SP_DEL, false, IC_TRASH); (void)arm; }
        x -= 10 + 44; UiButton(g, RectF(x, r.Y + 16, 44, 40), L"", base + SP_DIR, false, IC_FOLDER);
        x -= 10 + 120; UiButton(g, RectF(x, r.Y + 16, 120, 40), T(L"Renommer", L"Rename"), base + SP_RENAME, false, -1);
        x -= 10 + 130;
        if (used) Pill(g, RectF(x + 130 - PillW(T(L"UTILISÉE", L"IN USE"), 11.5f), r.Y + 25, PillW(T(L"UTILISÉE", L"IN USE"), 11.5f), 22), WithA(kAcc, 0.22f), T(L"UTILISÉE", L"IN USE"), kAcc, 11.5f);
        else UiButton(g, RectF(x, r.Y + 16, 130, 40), T(L"Utiliser", L"Use"), base + SP_USE, true, IC_CHECK);
    }
    g.Restore(st);
    if (ms > 0) DrawScrollBar(g, kSaveList, sc, ms);
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
    if (id >= UI_SAVE_PICK && id < UI_SAVE_END) { SavePickRow(id - UI_SAVE_PICK); return; }
    if (id >= UI_SAVE && id < UI_SAVE_PICK) {
        int i = (id - UI_SAVE) / 8, part = (id - UI_SAVE) % 8;
        if (i < 0 || i >= (int)g_saves.size()) return;
        if (part == SP_USE) { SaveSetCurrent(g_saves[i].prof); SetStatus(K_OK, T(L"Sauvegarde utilisée : %s", L"Save in use: %s"), g_saves[i].name.c_str()); return; }
        if (part == SP_RENAME) { g_saveEdit = i; g_saveEditText = g_saves[i].name; g_time = 0; g_focus = -1; return; }
        if (part == SP_DIR) { SaveOpenDir(i); return; }
        if (part == SP_DEL) { SaveDelete(i); return; }
        if (part == SP_CANCEL) { g_saveEdit = -1; return; }
        if (part == SP_OK) { SaveChar(13); return; }
    }
}

// ---------------------------------------------------------------- VERSION Legacy / Reworked (voir ChannelReworked)
static RectF g_canalRect;   // (bulle du guide ; selecteur sous le numero de version, ecran principal)
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
// Choisir la version : Reworked (la refonte, par defaut ; copie des sauvegardes la premiere fois) ou Legacy (l'ancienne,
// plus mise a jour). host : demande venue du salon (l'hote est sur l'autre version). Faux : le joueur a refuse.
static bool ChannelSet(bool reworked, bool host)
{
    wchar_t q[1400];
    q[0] = 0;
    if (host) wcscpy_s(q, reworked ? T(L"L'hôte joue avec la version Reworked de MWCoop : il faut la même pour jouer ensemble.\n\n", L"The host plays the Reworked version of MWCoop: you need the same one to play together.\n\n")
                                   : T(L"L'hôte joue avec la version Legacy de MWCoop : il faut la même pour jouer ensemble.\n\n", L"The host plays the Legacy version of MWCoop: you need the same one to play together.\n\n"));
    if (reworked) {
        wcscat_s(q, T(L"Reworked : le nouveau MWCoop, refait en profondeur (synchronisation, atelier, boulots, argent, réseau). C'est la version qui reçoit les mises à jour.\n\n"
                      L"Tout le monde doit être sur la même version pour jouer ensemble.\n\n"
                      L"Tes sauvegardes sont copiées avant (MWCoop\\copie-avant-reworked). Tu peux revenir en Legacy à tout moment (sous le numéro de version, en haut à gauche).\n\n"
                      L"Passer en Reworked ?",
                      L"Reworked: the new MWCoop, rebuilt in depth (sync, workshop, jobs, money, network). This is the version that gets updates.\n\n"
                      L"Everyone must be on the same version to play together.\n\n"
                      L"Your saves are copied first (MWCoop\\copie-avant-reworked). You can go back to Legacy at any time (under the version number, top left).\n\n"
                      L"Switch to Reworked?"));
        if (MessageBoxW(g_wnd, q, L"MWCoop Reworked", MB_YESNO | MB_ICONQUESTION) != IDYES) return false;
        if (GetPrivateProfileIntW(L"Lanceur", L"CopieNightly", 0, g_iniLauncher.c_str()) == 0) {
            std::wstring dst = SavesBackupNightly();
            if (!dst.empty()) WritePrivateProfileStringW(L"Lanceur", L"CopieNightly", L"1", g_iniLauncher.c_str());
        }
    } else {
        wcscat_s(q, T(L"Legacy : l'ancien MWCoop (0.95), gardé tel quel et plus mis à jour. Seulement pour jouer avec quelqu'un qui y est resté.\n\nPasser en Legacy ?",
                      L"Legacy: the old MWCoop (0.95), kept as it is and no longer updated. Only to play with someone who stayed on it.\n\nSwitch to Legacy?"));
        if (MessageBoxW(g_wnd, q, L"MWCoop Legacy", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) return false;
    }
    WritePrivateProfileStringW(L"Lanceur", L"Version", reworked ? L"reworked" : L"legacy", g_iniLauncher.c_str());
    UpdSet(L"");
    TestLog("version : %s", reworked ? "reworked" : "legacy");
    SetStatus(reworked ? K_OK : K_WARN, reworked ? T(L"Version Reworked", L"Reworked version") : T(L"Version Legacy : l'ancienne, plus mise à jour", L"Legacy version: the old one, no longer updated"));
    return true;
}
