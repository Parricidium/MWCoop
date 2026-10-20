// ---------------------------------------------------------------- page OUTILS - inclus par launcher.cpp
// Demande de JD (20/10) : l'editeur de carte (editor\, EditeurCarte.exe) a part du lanceur, mais a portee : un raccourci
// de l'en-tete (cle, a gauche de l'etoile, separe par un trait) ouvre cette page ; « Editeur de carte » : un bouton qui le
// telecharge, puis le lance (et le met a jour quand une version plus recente est publiee).
//  - paquet : EditeurCarte-<version>.editeur dans les releases GitHub (un zip sous un autre nom : les lanceurs prennent le
//    premier .zip pour le mod ; dist\make-release.ps1), installe dans %LOCALAPPDATA%\MWCoop\editeur (version.txt dedans) ;
//  - lance avec le jeu choisi, son dossier, ce lanceur (qui fera l'export de la carte : /exportcarte) et la langue.
static std::wstring g_edRemoteVer, g_edRemoteUrl;   // derniere version publiee de l'editeur (release qui a un .editeur)
static volatile bool g_edBusy;                      // telechargement en cours
static float g_edProg = -1;                         // 0..1 pendant le telechargement
static DWORD g_edLaunchT;                           // « Lance ! » pendant 3 s
static Bitmap *g_edPreview;                         // apercu de la carte (ressource 10 : outils-editeur.jpg)
static Bitmap *LoadPngRes(int id);   // (launcher.cpp)
static const RectF kToolMain(264, 88, 600, 656), kToolHow(880, 88, 384, 286), kToolTips(880, 390, 384, 354);

static std::wstring EdDir() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"editeur\\"; }
static std::wstring EdExe() { std::wstring d = EdDir(); return d.empty() ? L"" : d + L"EditeurCarte.exe"; }
static std::wstring g_edFakeLocal;   // (captures : version installee factice)
static std::wstring EdLocalVer()
{
    if (!g_edFakeLocal.empty()) return g_edFakeLocal;
    std::vector<unsigned char> d;
    if (EdDir().empty() || !FileExists(EdExe()) || !ReadAll(EdDir() + L"version.txt", d)) return L"";
    return Trim(Widen(std::string(d.begin(), d.end())));
}

// Releases recues (fil des mises a jour, ou des notes sans jeu) : la plus recente qui a un paquet de l'editeur (Nightly
// seulement pour le canal Nightly, comme le mod).
static void ToolsSeen(const std::vector<Note> &rel)
{
    const Note *best = NULL;
    for (const Note &n : rel) {
        if (n.editor.empty() || (n.pre && !NightlyOn())) continue;
        if (!best || CmpVer(n.ver, best->ver) > 0) best = &n;
    }
    EnterCriticalSection(&g_cs);
    g_edRemoteVer = best ? best->ver : L"";
    g_edRemoteUrl = best ? best->editor : L"";
    LeaveCriticalSection(&g_cs);
}

static bool EdRunning()
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W pe = { sizeof(pe) };
    bool found = false;
    for (BOOL ok = Process32FirstW(snap, &pe); ok && !found; ok = Process32NextW(snap, &pe)) found = !_wcsicmp(pe.szExeFile, L"EditeurCarte.exe");
    CloseHandle(snap);
    return found;
}

static DWORD WINAPI EdDownloadThread(void *)
{
    std::wstring url, ver;
    EnterCriticalSection(&g_cs); url = g_edRemoteUrl; ver = g_edRemoteVer; LeaveCriticalSection(&g_cs);
    std::wstring dir = EdDir(), tmp = LocalDir() + L"editeur-telechargement.zip";
    SHCreateDirectoryExW(NULL, dir.c_str(), NULL);
    g_edProg = 0;
    DWORD code = 0;
    bool ok = HttpGet(url, NULL, tmp, true, &code, &g_edProg);
    std::string why;
    if (!ok) why = code ? "HTTP " + std::to_string(code) : "erreur " + std::to_string(g_httpErr);
    else if (!Unzip(tmp, dir, &why)) ok = false;
    DeleteFileW(tmp.c_str());
    LaunchLog("editeur de carte %s : %s (%s)", Narrow(ver).c_str(), ok ? "installe" : "ECHEC", why.c_str());
    if (ok) SetStatus(K_OK, T(L"Éditeur de carte %s installé : clique sur Lancer", L"Map editor %s installed: click Launch"), ver.c_str());
    else SetStatus(K_ERR, T(L"Téléchargement de l'éditeur de carte impossible (%s)", L"Could not download the map editor (%s)"), Widen(why).c_str());
    g_edProg = -1;
    g_edBusy = false;
    return 0;
}

static void EdDownload()
{
    if (g_edBusy || g_edRemoteUrl.empty() || EdDir().empty()) return;
    if (EdRunning()) { SetStatus(K_WARN, T(L"Ferme l'éditeur de carte avant de le mettre à jour", L"Close the map editor before updating it")); return; }
    g_edBusy = true;
    HANDLE h = CreateThread(NULL, 0, EdDownloadThread, NULL, 0, NULL);
    if (h) CloseHandle(h); else g_edBusy = false;
}

// Lance l'editeur : le jeu choisi, son dossier (sans \ final : il echapperait le guillemet), ce lanceur, la langue.
static void EdLaunch()
{
    std::wstring exe = EdExe();
    if (exe.empty() || !FileExists(exe)) return;
    std::wstring dir = g_gameDir;
    while (!dir.empty() && (dir.back() == L'\\' || dir.back() == L'/')) dir.pop_back();
    std::wstring cmd = L"\"" + exe + L"\" --jeu " + GM().id + L" --lanceur \"" + g_self + L"\" --langue " + (g_fr ? L"fr" : g_ru ? L"ru" : g_ar ? L"ar" : L"en");
    if (!dir.empty()) cmd += L" --dossier \"" + dir + L"\"";
    STARTUPINFOW si = { sizeof(si) }; PROCESS_INFORMATION pi = {};
    std::vector<wchar_t> cb(cmd.begin(), cmd.end()); cb.push_back(0);
    if (CreateProcessW(exe.c_str(), cb.data(), NULL, NULL, FALSE, 0, NULL, EdDir().c_str(), &si, &pi)) {
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        g_edLaunchT = GetTickCount();
        LaunchLog("editeur de carte lance : %s", Narrow(cmd, CP_UTF8).c_str());
    } else SetStatus(K_ERR, T(L"Impossible de lancer l'éditeur de carte (erreur %lu)", L"Could not start the map editor (error %lu)"), GetLastError());
}

static void DrawToolsPage(Graphics &g)
{
    std::wstring local = EdLocalVer(), remote;
    EnterCriticalSection(&g_cs); remote = g_edRemoteVer; LeaveCriticalSection(&g_cs);
    bool installed = !local.empty(), newer = installed && !remote.empty() && CmpVer(remote, local) > 0;

    // ---- gauche : l'editeur de carte
    RectF c = kToolMain;
    Glass(c);
    RectF img(c.X + 24, c.Y + 24, c.Width - 48, 250);
    {
        if (!g_edPreview) g_edPreview = LoadPngRes(10);
        GraphicsPath p; RoundRect(p, img, 16);
        GraphicsState st = g.Save();
        g.SetClip(&p, CombineModeIntersect);
        if (g_edPreview) g.DrawImage(g_edPreview, img);
        else { SolidBrush fb(TH(card)); g.FillPath(&fb, &p); Icon(g, IC_MAP, img.X + img.Width / 2, img.Y + img.Height / 2, 64, kGrey, 1.6f); }
        g.Restore(st);
        Pen pen(TH(choiceBorder), 1); g.DrawPath(&pen, &p);
        const wchar_t *tag = L"MSC · MWC";
        float pw = PillW(tag, 10.5f);
        Pill(g, RectF(img.X + 14, img.Y + 14, pw, 22), Color(200, 10, 14, 24), tag, Color(255, 255, 255, 255), 10.5f);
    }
    float y = img.Y + img.Height + 20;
    Title(g, T(L"Éditeur de carte", L"Map editor"), RectF(c.X + 24, y, 300, 30), 21, kInk);
    {
        const wchar_t *b = L"BETA";
        float pw = PillW(b, 10.5f);
        Pill(g, RectF(c.X + 24 + MeasureW(MeasureG(), T(L"Éditeur de carte", L"Map editor"), 21, FontStyleBold) + 12, y + 5, pw, 22), WithA(kAcc, 0.18f), b, kAcc, 10.5f);
    }
    y += 36;
    Para(g, T(L"Un programme à part pour modifier la carte de My Summer Car ou de My Winter Car. Cette première version prépare la vraie carte du jeu et te laisse la visiter librement en 3D.",
              L"A separate program to edit the map of My Summer Car or My Winter Car. This first version prepares the game's real map and lets you fly around it freely in 3D."),
         RectF(c.X + 24, y, c.Width - 48, 44), 13, kGrey);
    y += 52;
    struct Feat { int ic; const wchar_t *fr, *en; };
    static const Feat feats[] = {
        { IC_CHECK, L"La carte du jeu choisi, préparée une fois (le jeu tourne en arrière-plan)", L"The chosen game's map, prepared once (the game runs in the background)" },
        { IC_CHECK, L"Visite libre en 3D : voler, couches, objets à cliquer", L"Free 3D tour: fly, layers, objects to click" },
        { IC_SPARK, L"Bientôt : routes, modèles 3D, cartes partagées en co-op", L"Coming: roads, 3D models, maps shared in co-op" } };
    for (const Feat &f : feats) {
        Icon(g, f.ic, c.X + 34, y + 11, 16, f.ic == IC_SPARK ? kGrey : kOk, 2.2f);
        Text(g, T(f.fr, f.en), RectF(c.X + 52, y, c.Width - 76, 22), 12.5f, FontStyleRegular, f.ic == IC_SPARK ? kGrey : kInk, StringAlignmentNear);
        y += 26;
    }
    // ligne d'etat et boutons, en bas de la carte
    float by = c.Y + c.Height - 24 - 48;
    std::wstring st;
    Color sc = kGrey;
    if (g_edBusy) { st = T(L"Téléchargement…", L"Downloading…"); sc = kAcc; }
    else if (newer) { wchar_t b[160]; swprintf_s(b, T(L"Version %s installée · %s disponible", L"Version %s installed · %s available"), local.c_str(), remote.c_str()); st = b; sc = kWarn; }
    else if (installed) { wchar_t b[160]; swprintf_s(b, T(L"Installé · version %s", L"Installed · version %s"), local.c_str()); st = b; sc = kOk; }
    else if (!remote.empty()) { wchar_t b[160]; swprintf_s(b, T(L"Pas encore installé · version %s, moins de 1 Mo", L"Not installed yet · version %s, under 1 MB"), remote.c_str()); st = b; }
    else if (g_relState == REL_OFFLINE) st = T(L"Hors ligne : GitHub injoignable", L"Offline: GitHub unreachable");
    else if (!g_notesDone) st = T(L"Recherche de la dernière version…", L"Looking for the latest version…");
    else st = T(L"Pas encore publié : il arrive avec la prochaine version", L"Not published yet: it comes with the next version");
    {
        SolidBrush d(sc); g.FillEllipse(&d, c.X + 26, by - 25, 8.0f, 8.0f);
        Text(g, st, RectF(c.X + 42, by - 34, c.Width - 66, 26), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
    }
    RectF main(c.X + 24, by, 250, 48), second(c.X + 284, by, 160, 48);
    if (g_edBusy) {   // bouton plein qui se remplit
        GraphicsPath p; RoundRect(p, main, 14);
        SolidBrush bg(WithA(kAcc, 0.25f)); g.FillPath(&bg, &p);
        float k = g_edProg < 0 ? 0 : min(1.0f, g_edProg);
        GraphicsState s2 = g.Save();
        g.SetClip(RectF(main.X, main.Y, main.Width * k, main.Height), CombineModeIntersect);
        SolidBrush fb(kAcc); g.FillPath(&fb, &p);
        g.Restore(s2);
        wchar_t b[64]; swprintf_s(b, L"%d %%", (int)(k * 100 + 0.5f));
        Text(g, b, main, 13.5f, FontStyleBold, k > 0.5f ? kOnAcc : kInk);
    } else if (!installed) UiButton(g, main, T(L"Télécharger", L"Download"), UI_TOOL_GET, true, IC_DOWNLOAD, !remote.empty());
    else if (newer) UiButton(g, main, T(L"Mettre à jour", L"Update"), UI_TOOL_GET, true, IC_DOWNLOAD);
    else {
        bool just = g_edLaunchT && GetTickCount() - g_edLaunchT < 3000;
        UiButton(g, main, just ? T(L"Lancé !", L"Started!") : T(L"Lancer", L"Launch"), UI_TOOL_RUN, true, just ? IC_CHECK : IC_PLAY);
    }
    if (installed && !g_edBusy) {
        if (newer) UiButton(g, second, T(L"Lancer", L"Launch"), UI_TOOL_RUN, false, IC_PLAY);
        else UiButton(g, second, T(L"Dossier", L"Folder"), UI_TOOL_DIR, false, IC_FOLDER);
    }

    // ---- droite : comment ca marche
    RectF w = kToolHow;
    Glass(w);
    Title(g, T(L"Comment ça marche", L"How it works"), RectF(w.X + 20, w.Y + 16, w.Width - 40, 26), 17, kInk);
    static const wchar_t *howFr[] = {
        L"Télécharge l'éditeur : il s'installe à part, le jeu n'est pas modifié.",
        L"Lance-le : il ouvre le jeu choisi ici (My Summer Car ou My Winter Car), avec son dossier.",
        L"Prépare la carte une fois : le jeu tourne une minute en arrière-plan, sans fenêtre, dans un profil à part. Tes sauvegardes ne sont pas touchées." };
    static const wchar_t *howEn[] = {
        L"Download the editor: it installs apart, the game is not changed.",
        L"Launch it: it opens the game chosen here (My Summer Car or My Winter Car), with its folder.",
        L"Prepare the map once: the game runs for a minute in the background, with no window, in a separate profile. Your saves are not touched." };
    float hy = w.Y + 58;
    UiFont hf(L"Segoe UI", 12.5f, FontStyleRegular);
    for (int i = 0; i < 3; i++) {
        const wchar_t *s = T(howFr[i], howEn[i]);
        RectF m; MeasureStr(g, s, -1, &hf, RectF(0, 0, w.Width - 74, 400), &m);
        RectF n(w.X + 20, hy, 26, 26);
        SolidBrush nb(WithA(kAcc, 0.18f)); g.FillEllipse(&nb, n);
        Text(g, std::to_wstring(i + 1), n, 12, FontStyleBold, kAcc);
        Para(g, s, RectF(w.X + 56, hy + 3, w.Width - 74, m.Height + 4), 12.5f, kInk);
        hy += max(m.Height, 26.0f) + 16;
    }

    // ---- droite : bon a savoir
    RectF t = kToolTips;
    Glass(t);
    Title(g, T(L"Bon à savoir", L"Good to know"), RectF(t.X + 20, t.Y + 16, t.Width - 40, 26), 17, kInk);
    static const wchar_t *tipsFr[] = {
        L"Windows 10 ou 11 : rien d'autre à installer (il utilise Microsoft Edge WebView2, déjà présent).",
        L"Les fichiers du jeu restent sur ton PC : la carte préparée va dans tes Documents (My Seasons Car).",
        L"Après une mise à jour du jeu, refais la préparation de la carte dans l'éditeur.",
        L"D'autres outils arriveront sur cette page." };
    static const wchar_t *tipsEn[] = {
        L"Windows 10 or 11: nothing else to install (it uses Microsoft Edge WebView2, already there).",
        L"Game files stay on your PC: the prepared map goes to your Documents (My Seasons Car).",
        L"After a game update, prepare the map again in the editor.",
        L"More tools will come to this page." };
    float ty = t.Y + 56;
    for (int i = 0; i < 4; i++) {
        const wchar_t *s = T(tipsFr[i], tipsEn[i]);
        RectF m; MeasureStr(g, s, -1, &hf, RectF(0, 0, t.Width - 58, 400), &m);
        SolidBrush dot(kAcc); g.FillEllipse(&dot, t.X + 22, ty + 7, 7.0f, 7.0f);
        Para(g, s, RectF(t.X + 38, ty, t.Width - 58, m.Height + 4), 12.5f, kInk);
        ty += m.Height + 14;
    }
}

static void ToolsClick(int id)
{
    switch (id) {
    case UI_TOOL_GET: EdDownload(); break;
    case UI_TOOL_RUN: EdLaunch(); break;
    case UI_TOOL_DIR: if (!EdDir().empty()) ShellExecuteW(g_wnd, L"open", EdDir().c_str(), NULL, NULL, SW_SHOWNORMAL); break;
    }
}
