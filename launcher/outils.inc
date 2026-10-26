// ---------------------------------------------------------------- page OUTILS - inclus par launcher.cpp
// Demande de JD (20/10) : des programmes a part du lanceur, mais a portee : un raccourci de l'en-tete (cle, a gauche de
// l'etoile, separe par un trait) ouvre cette page. Une carte par outil, cote a cote (24/10) : un bouton qui le telecharge,
// puis le lance (et le met a jour quand une version plus recente est publiee).
//  - Editeur de carte (editor\, EditeurCarte.exe) : paquet EditeurCarte-<version>.editeur dans les releases GitHub (un zip
//    sous un autre nom : les lanceurs prennent le premier .zip pour le mod ; dist\make-release.ps1), installe dans
//    %LOCALAPPDATA%\MWCoop\editeur ; lance avec le jeu choisi, son dossier, ce lanceur (export de la carte : /exportcarte)
//    et la langue.
//  - Convertisseur de circuits (mods\Voyage\convertisseur, ConvertisseurVoyage.exe, JD 24/10, « Early ») : circuits de
//    l'Assetto Corsa du joueur -> destinations du mod Voyage (agence de voyage, aerodrome, vol) ; paquet
//    ConvertisseurVoyage-<version>.convertisseur (le mod Voyage.dll dedans, installe dans le jeu par le convertisseur),
//    installe dans %LOCALAPPDATA%\MWCoop\voyage ; lance avec le dossier de My Winter Car (si c'est le jeu choisi) et la langue.
struct ToolPkg {
    const wchar_t *dir, *exe;           // sous-dossier de %LOCALAPPDATA%\MWCoop, programme
    std::wstring remoteVer, remoteUrl;  // derniere version publiee (release qui a le paquet)
    volatile bool busy;                 // telechargement en cours
    float prog = -1;                    // 0..1 pendant le telechargement
    DWORD launchT;                      // « Lance ! » pendant 3 s
    std::wstring fakeLocal;             // (captures : version installee factice)
};
static ToolPkg g_ed = { L"editeur", L"EditeurCarte.exe" }, g_cv = { L"voyage", L"ConvertisseurVoyage.exe" };
static Bitmap *g_edPreview;             // apercu de la carte (ressource 10 : outils-editeur.jpg)
static Bitmap *LoadPngRes(int id);      // (launcher.cpp)
static const RectF kToolEd(264, 88, 492, 656), kToolCv(772, 88, 492, 656);

static std::wstring ToolDir(const ToolPkg &t) { std::wstring l = LocalDir(); return l.empty() ? L"" : l + t.dir + L"\\"; }
static std::wstring ToolExe(const ToolPkg &t) { std::wstring d = ToolDir(t); return d.empty() ? L"" : d + t.exe; }
static std::wstring ToolLocalVer(const ToolPkg &t)
{
    if (!t.fakeLocal.empty()) return t.fakeLocal;
    std::vector<unsigned char> d;
    if (ToolDir(t).empty() || !FileExists(ToolExe(t)) || !ReadAll(ToolDir(t) + L"version.txt", d)) return L"";
    return Trim(Widen(std::string(d.begin(), d.end())));
}

// Releases recues (fil des mises a jour, ou des notes sans jeu) : la plus recente qui a le paquet de chaque outil (Nightly
// seulement pour le canal Nightly, comme le mod).
static void PlacesSeen(const std::vector<Note> &rel);   // (places.inc)
static void ToolsSeen(const std::vector<Note> &rel)
{
    const Note *ed = NULL, *cv = NULL;
    for (const Note &n : rel) {
        if (n.pre && !NightlyOn()) continue;
        if (!n.editor.empty() && (!ed || CmpVer(n.ver, ed->ver) > 0)) ed = &n;
        if (!n.conv.empty() && (!cv || CmpVer(n.ver, cv->ver) > 0)) cv = &n;
    }
    EnterCriticalSection(&g_cs);
    g_ed.remoteVer = ed ? ed->ver : L""; g_ed.remoteUrl = ed ? ed->editor : L"";
    g_cv.remoteVer = cv ? cv->ver : L""; g_cv.remoteUrl = cv ? cv->conv : L"";
    LeaveCriticalSection(&g_cs);
    PlacesSeen(rel);   // (outil des places : places.inc)
}

static bool ToolRunning(const ToolPkg &t)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W pe = { sizeof(pe) };
    bool found = false;
    for (BOOL ok = Process32FirstW(snap, &pe); ok && !found; ok = Process32NextW(snap, &pe)) found = !_wcsicmp(pe.szExeFile, t.exe);
    CloseHandle(snap);
    return found;
}

static DWORD WINAPI ToolDownloadThread(void *p)
{
    ToolPkg &t = *(ToolPkg *)p;
    bool ed = &t == &g_ed;
    std::wstring url, ver;
    EnterCriticalSection(&g_cs); url = t.remoteUrl; ver = t.remoteVer; LeaveCriticalSection(&g_cs);
    std::wstring dir = ToolDir(t), tmp = LocalDir() + t.dir + L"-telechargement.zip";
    SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
    t.prog = 0;
    DWORD code = 0;
    bool ok = HttpGet(url, NULL, tmp, true, &code, &t.prog);
    std::string why;
    if (!ok) why = code ? "HTTP " + std::to_string(code) : "erreur " + std::to_string(g_httpErr);
    else if (!Unzip(tmp, dir, &why)) ok = false;
    DeleteFileW(tmp.c_str());
    LaunchLog("%s %s : %s (%s)", ed ? "editeur de carte" : "convertisseur de circuits", Narrow(ver).c_str(), ok ? "installe" : "ECHEC", why.c_str());
    if (ok) SetStatus(K_OK, ed ? T(L"Éditeur de carte %s installé : clique sur Lancer", L"Map editor %s installed: click Launch")
                               : T(L"Convertisseur de circuits %s installé : clique sur Lancer", L"Track converter %s installed: click Launch"), ver.c_str());
    else SetStatus(K_ERR, ed ? T(L"Téléchargement de l'éditeur de carte impossible (%s)", L"Could not download the map editor (%s)")
                             : T(L"Téléchargement du convertisseur de circuits impossible (%s)", L"Could not download the track converter (%s)"), Widen(why).c_str());
    t.prog = -1;
    t.busy = false;
    return 0;
}

static void ToolDownload(ToolPkg &t)
{
    if (t.busy || t.remoteUrl.empty() || ToolDir(t).empty()) return;
    if (ToolRunning(t)) {
        SetStatus(K_WARN, &t == &g_ed ? T(L"Ferme l'éditeur de carte avant de le mettre à jour", L"Close the map editor before updating it")
                                      : T(L"Ferme le convertisseur de circuits avant de le mettre à jour", L"Close the track converter before updating it"));
        return;
    }
    t.busy = true;
    HANDLE h = CreateThread(NULL, 0, ToolDownloadThread, &t, 0, NULL);
    if (h) CloseHandle(h); else t.busy = false;
}

// Lance un outil : la langue ; l'editeur : le jeu choisi, son dossier (sans \ final : il echapperait le guillemet), ce
// lanceur ; le convertisseur : le dossier de My Winter Car quand c'est le jeu choisi (sinon il le cherche lui-meme).
static void ToolLaunch(ToolPkg &t)
{
    std::wstring exe = ToolExe(t);
    if (exe.empty() || !FileExists(exe)) return;
    std::wstring dir = g_gameDir;
    while (!dir.empty() && (dir.back() == L'\\' || dir.back() == L'/')) dir.pop_back();
    const wchar_t *lang = g_fr ? L"fr" : g_ru ? L"ru" : g_ar ? L"ar" : L"en";
    std::wstring cmd = L"\"" + exe + L"\" --langue " + lang;
    if (&t == &g_ed) {
        cmd += std::wstring(L" --jeu ") + GM().id + L" --lanceur \"" + g_self + L"\"";
        if (!dir.empty()) cmd += L" --dossier \"" + dir + L"\"";
        if (!g_modRoot.empty()) cmd += L" --mod \"" + g_modRoot + L"MWCoop\"";   // (cartes compilees : <MWCoop du mod>\cartes, lues par MapPacks.cs)
    } else {   // (convertisseur : le jeu choisi ici, My Summer Car ou My Winter Car, et son dossier)
        cmd += std::wstring(L" --jeu ") + GM().id;
        if (!dir.empty()) cmd += L" --dossier \"" + dir + L"\"";
    }
    STARTUPINFOW si = { sizeof(si) }; PROCESS_INFORMATION pi = {};
    std::vector<wchar_t> cb(cmd.begin(), cmd.end()); cb.push_back(0);
    if (CreateProcessW(exe.c_str(), cb.data(), NULL, NULL, FALSE, 0, NULL, ToolDir(t).c_str(), &si, &pi)) {
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        t.launchT = GetTickCount();
        LaunchLog("outil lance : %s", Narrow(cmd, CP_UTF8).c_str());
    } else SetStatus(K_ERR, T(L"Impossible de lancer l'outil (erreur %lu)", L"Could not start the tool (error %lu)"), GetLastError());
}

// image du convertisseur : dessinee (pas de capture d'un circuit d'Assetto Corsa) -- un trace de circuit, la trajectoire
// pointillee d'un avion
static void DrawConvArt(Graphics &g, RectF r)
{
    LinearGradientBrush bg(PointF(r.X, r.Y), PointF(r.X, r.Y + r.Height), Color(255, 30, 52, 82), Color(255, 12, 20, 34));
    g.FillRectangle(&bg, r);
    float cx = r.X + r.Width * 0.42f, cy = r.Y + r.Height * 0.58f, k = r.Height / 210.0f;
    auto P = [&](float x, float y) { return PointF(cx + x * k, cy + y * k); };
    GraphicsPath track;
    PointF pts[] = { P(-150, 30), P(-170, -10), P(-120, -60), P(-40, -50), P(10, -20), P(60, -70), P(140, -60), P(170, -10), P(130, 40), P(40, 50), P(-60, 65) };
    track.AddClosedCurve(pts, 11, 0.55f);
    Pen edge(Color(255, 70, 84, 104), 22 * k); edge.SetLineJoin(LineJoinRound); g.DrawPath(&edge, &track);
    Pen tar(Color(255, 46, 54, 66), 17 * k); tar.SetLineJoin(LineJoinRound); g.DrawPath(&tar, &track);
    Pen mid(Color(150, 255, 255, 255), 1.6f * k); REAL dash[] = { 4, 4 }; mid.SetDashPattern(dash, 2); g.DrawPath(&mid, &track);
    // ligne de depart a damier
    for (int i = 0; i < 4; i++) for (int j = 0; j < 2; j++) {
        SolidBrush b((i + j) % 2 ? Color(255, 240, 240, 240) : Color(255, 30, 30, 30));
        g.FillRectangle(&b, cx + (-12 + j * 4.5f) * k + 158 * k - 150 * k, cy + (-9 + i * 4.5f) * k + 60 * k, 4.5f * k, 4.5f * k);
    }
    // trajectoire de l'avion et avion (vu de dessus), couleur d'accent
    GraphicsPath fl; fl.AddBezier(P(-190, 95), P(-110, -40), P(60, -120), P(205, -100));
    Pen fp(WithA(kAcc, 0.75f), 2.2f * k); REAL d2[] = { 2, 3 }; fp.SetDashPattern(d2, 2); g.DrawPath(&fp, &fl);
    GraphicsState st = g.Save();
    g.TranslateTransform(cx + 150 * k, cy - 108 * k); g.RotateTransform(80); g.ScaleTransform(k * 1.25f, k * 1.25f);
    PointF plane[] = { PointF(0, -18), PointF(2.5f, -12), PointF(2.5f, -4), PointF(20, 2), PointF(20, 5), PointF(2.5f, 2), PointF(2, 12), PointF(8, 15), PointF(8, 17.5f),
                       PointF(0, 16), PointF(-8, 17.5f), PointF(-8, 15), PointF(-2, 12), PointF(-2.5f, 2), PointF(-20, 5), PointF(-20, 2), PointF(-2.5f, -4), PointF(-2.5f, -12) };
    SolidBrush pb(kAcc); g.FillPolygon(&pb, plane, 18);
    g.Restore(st);
}

struct ToolText { const wchar_t *titleFr, *titleEn, *pill, *paraFr, *paraEn, *sizeFr, *sizeEn; struct { int ic; const wchar_t *fr, *en; } feats[3]; };

static void DrawToolCard(Graphics &g, ToolPkg &t, RectF c, const ToolText &tx, int idGet)
{
    std::wstring local = ToolLocalVer(t), remote;
    EnterCriticalSection(&g_cs); remote = t.remoteVer; LeaveCriticalSection(&g_cs);
    bool installed = !local.empty(), newer = installed && !remote.empty() && CmpVer(remote, local) > 0;
    Glass(c);
    RectF img(c.X + 24, c.Y + 24, c.Width - 48, 214);
    {
        GraphicsPath p; RoundRect(p, img, 16);
        GraphicsState st = g.Save();
        g.SetClip(&p, CombineModeIntersect);
        if (&t == &g_ed) {
            if (!g_edPreview) g_edPreview = LoadPngRes(10);
            if (g_edPreview) {   // (image 600 x 250 a l'origine : centree, rognee sur les cotes)
                float h = img.Height, w = h * g_edPreview->GetWidth() / (float)max(1u, g_edPreview->GetHeight());
                g.DrawImage(g_edPreview, RectF(img.X + (img.Width - w) / 2, img.Y, w, h));
            } else { SolidBrush fb(TH(card)); g.FillPath(&fb, &p); Icon(g, IC_MAP, img.X + img.Width / 2, img.Y + img.Height / 2, 64, kGrey, 1.6f); }
        } else DrawConvArt(g, img);
        g.Restore(st);
        Pen pen(TH(choiceBorder), 1); g.DrawPath(&pen, &p);
        const wchar_t *tag = &t == &g_ed ? L"MSC · MWC" : L"Assetto Corsa → MSC · MWC";
        float pw = PillW(tag, 10.5f);
        Pill(g, RectF(img.X + 14, img.Y + 14, pw, 22), Color(200, 10, 14, 24), tag, Color(255, 255, 255, 255), 10.5f);
    }
    float y = img.Y + img.Height + 20;
    const wchar_t *title = T(tx.titleFr, tx.titleEn);
    Title(g, title, RectF(c.X + 24, y, c.Width - 48, 30), 21, kInk);
    {
        float pw = PillW(tx.pill, 10.5f);
        Color pc = &t == &g_ed ? kAcc : kWarn;
        Pill(g, RectF(c.X + 24 + MeasureW(MeasureG(), title, 21, FontStyleBold) + 12, y + 5, pw, 22), WithA(pc, 0.18f), tx.pill, pc, 10.5f);
    }
    y += 38;
    Para(g, T(tx.paraFr, tx.paraEn), RectF(c.X + 24, y, c.Width - 48, 80), 13, kGrey);
    y += 86;
    for (const auto &f : tx.feats) {
        Color fc = f.ic == IC_ALERT ? kWarn : kOk;
        Icon(g, f.ic, c.X + 34, y + 11, 16, fc, 2.2f);
        Para(g, T(f.fr, f.en), RectF(c.X + 52, y + 1, c.Width - 76, 40), 12.5f, kInk);
        y += 40;
    }
    // ligne d'etat et boutons, en bas de la carte
    float by = c.Y + c.Height - 24 - 48;
    std::wstring st;
    Color sc = kGrey;
    if (t.busy) { st = T(L"Téléchargement…", L"Downloading…"); sc = kAcc; }
    else if (newer) { wchar_t b[160]; swprintf_s(b, T(L"Version %s installée · %s disponible", L"Version %s installed · %s available"), local.c_str(), remote.c_str()); st = b; sc = kWarn; }
    else if (installed) { wchar_t b[160]; swprintf_s(b, T(L"Installé · version %s", L"Installed · version %s"), local.c_str()); st = b; sc = kOk; }
    else if (!remote.empty()) { wchar_t b[160]; swprintf_s(b, T(tx.sizeFr, tx.sizeEn), remote.c_str()); st = b; }
    else if (g_relState == REL_OFFLINE) st = T(L"Hors ligne : GitHub injoignable", L"Offline: GitHub unreachable");
    else if (!g_notesDone) st = T(L"Recherche de la dernière version…", L"Looking for the latest version…");
    else st = T(L"Pas encore publié : il arrive avec la prochaine version", L"Not published yet: it comes with the next version");
    {
        SolidBrush d(sc); g.FillEllipse(&d, c.X + 26, by - 25, 8.0f, 8.0f);
        Text(g, st, RectF(c.X + 42, by - 34, c.Width - 66, 26), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
    }
    RectF main(c.X + 24, by, 230, 48), second(c.X + 266, by, 150, 48);
    int idRun = idGet + 1, idDir = idGet + 2;
    if (t.busy) {   // bouton plein qui se remplit
        GraphicsPath p; RoundRect(p, main, 14);
        SolidBrush bg(WithA(kAcc, 0.25f)); g.FillPath(&bg, &p);
        float k = t.prog < 0 ? 0 : min(1.0f, t.prog);
        GraphicsState s2 = g.Save();
        g.SetClip(RectF(main.X, main.Y, main.Width * k, main.Height), CombineModeIntersect);
        SolidBrush fb(kAcc); g.FillPath(&fb, &p);
        g.Restore(s2);
        wchar_t b[64]; swprintf_s(b, L"%d %%", (int)(k * 100 + 0.5f));
        Text(g, b, main, 13.5f, FontStyleBold, k > 0.5f ? kOnAcc : kInk);
    } else if (!installed) UiButton(g, main, T(L"Télécharger", L"Download"), idGet, true, IC_DOWNLOAD, !remote.empty());
    else if (newer) UiButton(g, main, T(L"Mettre à jour", L"Update"), idGet, true, IC_DOWNLOAD);
    else {
        bool just = t.launchT && GetTickCount() - t.launchT < 3000;
        UiButton(g, main, just ? T(L"Lancé !", L"Started!") : T(L"Lancer", L"Launch"), idRun, true, just ? IC_CHECK : IC_PLAY);
    }
    if (installed && !t.busy) {
        if (newer) UiButton(g, second, T(L"Lancer", L"Launch"), idRun, false, IC_PLAY);
        else UiButton(g, second, T(L"Dossier", L"Folder"), idDir, false, IC_FOLDER);
    }
}

static void DrawToolsPage(Graphics &g)
{
    static const ToolText ed = {
        L"Éditeur de carte", L"Map editor", L"BETA",
        L"Un programme à part pour modifier la carte de My Summer Car ou de My Winter Car : routes, objets du jeu, modèles 3D, végétation, relief.",
        L"A separate program to edit the map of My Summer Car or My Winter Car: roads, game objects, 3D models, vegetation, terrain.",
        L"Pas encore installé · version %s, moins de 1 Mo", L"Not installed yet · version %s, under 1 MB",
        { { IC_CHECK, L"La carte du jeu choisi, préparée une fois (le jeu tourne en arrière-plan)", L"The chosen game's map, prepared once (the game runs in the background)" },
          { IC_CHECK, L"Cartes compilées : ajoutées au jeu, envoyées aux invités en co-op", L"Compiled maps: added to the game, sent to guests in co-op" },
          { IC_CHECK, L"Les fichiers du jeu restent sur ton PC, tes sauvegardes ne sont pas touchées", L"Game files stay on your PC, your saves are not touched" } } };
    static const ToolText cv = {
        L"Convertisseur de circuits", L"Track converter", L"EARLY",
        L"Transforme les circuits de ton Assetto Corsa en destinations de voyage : un billet à l'agence de Peräjärvi, puis l'avion.",
        L"Turns the tracks of your Assetto Corsa into travel destinations: a ticket at the Peräjärvi agency, then the plane.",
        L"Pas encore installé · version %s, environ 3 Mo", L"Not installed yet · version %s, about 3 MB",
        { { IC_CHECK, L"Circuits officiels et mods, convertis sur ton PC : rien n'est partagé", L"Official and mod tracks, converted on your PC: nothing is shared" },
          { IC_CHECK, L"Mod Voyage inclus (agence, aérodrome, vol en Cessna) : installé par le convertisseur", L"Voyage mod included (agency, airfield, Cessna flight): installed by the converter" },
          { IC_ALERT, L"My Summer Car et My Winter Car, avec MSCLoader ; en co-op, chacun convertit le même circuit", L"My Summer Car and My Winter Car, with MSCLoader; in co-op, everyone converts the same track" } } };
    DrawToolCard(g, g_ed, kToolEd, ed, UI_TOOL_GET);
    DrawToolCard(g, g_cv, kToolCv, cv, UI_TOOL_CV_GET);
}

static void ToolsClick(int id)
{
    ToolPkg &t = id >= UI_TOOL_CV_GET ? g_cv : g_ed;
    switch (id >= UI_TOOL_CV_GET ? id - UI_TOOL_CV_GET : id - UI_TOOL_GET) {
    case 0: ToolDownload(t); break;
    case 1: ToolLaunch(t); break;
    case 2: if (!ToolDir(t).empty()) ShellExecuteW(g_wnd, L"open", ToolDir(t).c_str(), NULL, NULL, SW_SHOWNORMAL); break;
    }
}
