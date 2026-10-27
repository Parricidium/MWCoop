// ---------------------------------------------------------------- SIGNALER UN BUG : le site dans le lanceur (WebView2)
// Demande de JD (27/10, spec D:\1 - AI\ClaudeAI\MWCoop-spec-signalement-integre.md, etape 1) : l'insecte de l'en-tete ouvre le
// formulaire du site (kBugSiteUrl) dans un panneau du lanceur, par le moteur WebView2 (Edge). Trois conditions, sinon le
// comportement d'avant (navigateur + zip sur le Bureau, BugReport) : Windows 10 ou plus (jamais Win7/8, meme avec un
// WebView2 109 installe : plus de correctifs depuis 2023, et la page contient une connexion Discord), pas Wine, moteur
// present (on ne propose jamais de l'installer). Chargeur lie dans l'exe (webview2\WebView2LoaderStatic.lib : il n'importe
// que kernel32, advapi32 et ole32 -- jamais d'import statique d'une DLL, cf. version.dll 0.59.4-0.59.7). Donnees du moteur :
// %LOCALAPPDATA%\MWCoop\webview (la connexion Discord reste d'un lancement a l'autre).
// La fenetre principale est en couches (UpdateLayeredWindow) : un enfant n'y est pas dessine. Le panneau est donc une popup
// a part, non layered, sans bordure, possedee par g_wnd, collee sur la zone de contenu sous une bande dessinee par le
// lanceur (cadenas + domaine, Creer le zip des journaux, Ouvrir dans le navigateur, Fermer). Elle suit la fenetre
// (BugPanelSync : a chaque image et a WM_WINDOWPOSCHANGED), se cache des qu'on change de page, qu'une boite s'ouvre, que la
// visite guidee parle ou que la fenetre est reduite ; elle est fermee pour de bon (les processus du moteur avec) quand le
// jeu part, et apres une minute cachee.
// Securite : navigations (page et cadres) limitees a live.myseasonscar.workers.dev, discord.com (connexion OAuth) et
// *.hcaptcha.com ; tout le reste, et toute nouvelle fenetre, part dans le navigateur ; aucun pont entre la page et le
// lanceur (objets hote, messages web, outils de developpement, menus, zoom coupes) ; telechargements annules.
// Reglages > Lanceur > Depannage : « Signaler un bug dans le navigateur » ([Lanceur] SignalerNavigateur=1) pour qui prefere.
// Essais : /capture <png> signaler (la bande et un faux panneau, jamais de fenetre) ; /testsignal <journal> <mode> (win7,
// wine, absent : la decision ; panneau : le vrai moteur sur une fenetre jamais montree, navigation refusee a l'essai).
#include "webview2/WebView2.h"
#include <functional>

static const wchar_t *kBugHost = L"live.myseasonscar.workers.dev";
static const float kBugBandH = 56;                      // bande d'en-tete du panneau (dans kMain)
static HWND g_bugWnd;                                   // la popup qui porte le moteur
static ICoreWebView2Environment *g_bugEnv;
static ICoreWebView2Controller *g_bugCtl;
static ICoreWebView2 *g_bugWv;
static int g_bugState;                                  // 0 rien, 1 moteur en creation, 2 pret, 3 echec (g_bugErr)
static std::wstring g_bugErr;
static std::wstring g_bugHost;                          // domaine montre dans la bande (page en cours)
static bool g_bugShown;                                 // popup visible
static RECT g_bugLastR;
static DWORD g_bugHiddenT;                              // cachee depuis (fermeture apres une minute)
static bool g_bugCapture;                               // /capture signaler : jamais de fenetre
static bool g_bugTest;                                  // /testsignal panneau : fenetre jamais montree, pas de navigateur
static int g_bugTestNav, g_bugTestDone;                 // essai : navigations vues, etape (1 : example.com, 2 : racine du site, 3 : fin)
static DWORD g_bugTestEndT;
static int g_bugPossible = -1;                          // BugPanelPossible (-1 : pas encore regarde)
static std::wstring g_bugVer;                           // version du moteur
static bool TutoActive();                               // (tuto.inc)
static int PageBack();                                  // (ui.inc)
static std::vector<std::wstring> ZipFiles();            // (launcher.cpp)
static std::wstring ZipToDesktop(const std::vector<std::wstring> &files, const std::wstring &tag, const wchar_t *base, bool quiet);
static RectF BugWebR() { return RectF(kMain.X, kMain.Y + kBugBandH + 6, kMain.Width, kMain.Height - kBugBandH - 6); }

// Les trois conditions du panneau. Essais : MWCOOP_WIN7=1 (IsWin7), MWCOOP_WINE=<version> (IsWine), MWCOOP_SANS_WEBVIEW=1.
static bool BugPanelPossible()
{
    if (g_bugPossible < 0) {
        g_bugPossible = 0;
        typedef LONG(WINAPI * RtlGetVersionF)(OSVERSIONINFOW *);
        OSVERSIONINFOW ov = { sizeof(ov) };
        if (RtlGetVersionF rgv = (RtlGetVersionF)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion")) rgv(&ov);
        wchar_t e[4] = L"";
        bool noEngine = GetEnvironmentVariableW(L"MWCOOP_SANS_WEBVIEW", e, 4) && e[0] == L'1';
        char why[96] = "";
        if (IsWine()) sprintf_s(why, "Wine / Proton %s", g_wine.c_str());
        else if (IsWin7() || ov.dwMajorVersion < 10) { wchar_t e7[4] = L""; bool sim = GetEnvironmentVariableW(L"MWCOOP_WIN7", e7, 4) && e7[0] == L'1'; sprintf_s(why, sim ? "Windows 7 (essai MWCOOP_WIN7)" : "Windows %lu.%lu, avant Windows 10", ov.dwMajorVersion, ov.dwMinorVersion); }
        else if (noEngine) sprintf_s(why, "moteur absent (essai MWCOOP_SANS_WEBVIEW)");
        else {
            LPWSTR v = NULL;
            HRESULT hr = GetAvailableCoreWebView2BrowserVersionString(NULL, &v);
            if (SUCCEEDED(hr) && v && *v) { g_bugVer = v; g_bugPossible = 1; }
            else sprintf_s(why, "moteur WebView2 absent (0x%08lX)", (unsigned long)hr);
            if (v) CoTaskMemFree(v);
        }
        if (g_bugPossible) LaunchLog("signalement : panneau integre (WebView2 %s)", Narrow(g_bugVer, CP_UTF8).c_str());
        else LaunchLog("signalement : navigateur (%s)", why);
    }
    return g_bugPossible == 1;
}
static bool BugPanelWanted() { return BugPanelPossible() && GetPrivateProfileIntW(L"Lanceur", L"SignalerNavigateur", 0, g_iniLauncher.c_str()) == 0; }

// ---- adresses permises
static std::wstring BugUrlHost(const wchar_t *u)   // hote d'une adresse https (vide sinon)
{
    if (!u || _wcsnicmp(u, L"https://", 8)) return L"";
    const wchar_t *h = u + 8;
    std::wstring host(h, wcscspn(h, L"/?#"));   // (un « @ » ou un « : » restent dedans : l'hote ne correspond plus a rien)
    for (auto &c : host) c = (wchar_t)towlower(c);
    return host;
}
static bool BugHostOk(const std::wstring &host)
{
    auto under = [&](const wchar_t *d) { size_t n = wcslen(d); return host == d || (host.size() > n && host[host.size() - n - 1] == L'.' && !host.compare(host.size() - n, n, d)); };
    return host == kBugHost || under(L"discord.com") || under(L"hcaptcha.com");
}
// Sur le site lui-meme : seulement le formulaire et la connexion (/depot, /connexion, /deconnexion). Le tableau « MWCoop en
// direct » (la racine, bouton « Retour au tableau » du formulaire) et le reste du site partent dans le navigateur : le
// panneau, c'est le formulaire (retour de JD, 27/10 : le tableau s'etait ouvert dedans).
static bool BugSitePathOk(const wchar_t *u)
{
    const wchar_t *p = wcschr(u + 8, L'/');
    if (!p) return false;
    return !_wcsnicmp(p, L"/depot", 6) || !_wcsnicmp(p, L"/connexion", 10) || !_wcsnicmp(p, L"/deconnexion", 12);
}
static bool BugUrlOk(const wchar_t *u)
{
    if (!u) return false;
    if (!_wcsicmp(u, L"about:blank")) return true;
    std::wstring host = BugUrlHost(u);
    if (host == kBugHost) return BugSitePathOk(u);
    return BugHostOk(host);
}
static void BugOpenBrowser(const wchar_t *u)   // (https seulement ; en essai : le journal)
{
    std::wstring host = BugUrlHost(u);
    if (host.empty()) { LaunchLog("signalement : adresse ignoree (pas https)"); return; }
    LaunchLog("signalement : %s -> navigateur", Narrow(host, CP_UTF8).c_str());
    if (g_bugTest) return;
    ShellExecuteW(g_wnd, L"open", u, NULL, NULL, SW_SHOWNORMAL);
}

// ---- rappels COM du moteur : un seul gabarit (IUnknown + Invoke a deux arguments)
template <class I, class A, class B> struct BugCb : I {
    std::function<HRESULT(A, B)> fn;
    ULONG rc;
    BugCb(std::function<HRESULT(A, B)> f) : fn(f), rc(1) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void **pp)
    {
        if (!pp) return E_POINTER;
        if (riid == IID_IUnknown || riid == __uuidof(I)) { *pp = static_cast<I *>(this); AddRef(); return S_OK; }
        *pp = NULL;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() { return ++rc; }
    ULONG STDMETHODCALLTYPE Release() { ULONG r = --rc; if (!r) delete this; return r; }
    HRESULT STDMETHODCALLTYPE Invoke(A a, B b) { return fn(a, b); }
};
template <class I, class A, class B> static I *BugMk(HRESULT (*f)(A, B)) { return new BugCb<I, A, B>(f); }

static void BugFail(HRESULT hr, const char *what)
{
    g_bugState = 3;
    wchar_t b[200];
    swprintf_s(b, T(L"Le formulaire n'a pas pu s'ouvrir dans le lanceur (erreur 0x%08lX) : « Ouvrir dans le navigateur », en haut.",
                    L"The form could not open inside the launcher (error 0x%08lX): \"Open in the browser\", at the top."), (unsigned long)hr);
    g_bugErr = b;
    LaunchLog("signalement : echec du moteur (%s, 0x%08lX)", what, (unsigned long)hr);
    SetStatus(K_WARN, T(L"Panneau de signalement indisponible : ouvre le formulaire dans le navigateur", L"Report panel unavailable: open the form in the browser"));
    if (g_bugTest) g_bugTestDone = 1;
}
static void BugPanelClose()
{
    if (g_bugWv) { g_bugWv->Release(); g_bugWv = NULL; }
    if (g_bugCtl) { g_bugCtl->Close(); g_bugCtl->Release(); g_bugCtl = NULL; }
    if (g_bugEnv) { g_bugEnv->Release(); g_bugEnv = NULL; }
    if (g_bugWnd) { DestroyWindow(g_bugWnd); g_bugWnd = NULL; }
    if (g_bugState) LaunchLog("signalement : panneau ferme");
    g_bugState = 0; g_bugShown = false; g_bugHost.clear();
}
static HRESULT BugNavStart(ICoreWebView2 *, ICoreWebView2NavigationStartingEventArgs *a)
{
    LPWSTR u = NULL;
    if (FAILED(a->get_Uri(&u)) || !u) return S_OK;
    g_bugTestNav++;
    if (!BugUrlOk(u)) {
        a->put_Cancel(TRUE);
        std::wstring host = BugUrlHost(u);
        LaunchLog("signalement : navigation refusee (%s%s)", host.empty() ? "pas https" : Narrow(host, CP_UTF8).c_str(), host == kBugHost ? ", hors du formulaire" : "");
        BugOpenBrowser(u);
    }
    CoTaskMemFree(u);
    return S_OK;
}
static HRESULT BugNewWindow(ICoreWebView2 *, ICoreWebView2NewWindowRequestedEventArgs *a)
{
    a->put_Handled(TRUE);   // (NewWindow reste nul : pas de fenetre)
    LPWSTR u = NULL;
    if (SUCCEEDED(a->get_Uri(&u)) && u) { BugOpenBrowser(u); CoTaskMemFree(u); }
    return S_OK;
}
static HRESULT BugDownload(ICoreWebView2 *, ICoreWebView2DownloadStartingEventArgs *a)
{
    a->put_Cancel(TRUE); a->put_Handled(TRUE);
    LaunchLog("signalement : telechargement annule");
    return S_OK;
}
static HRESULT BugSource(ICoreWebView2 *wv, ICoreWebView2SourceChangedEventArgs *)
{
    LPWSTR u = NULL;
    if (SUCCEEDED(wv->get_Source(&u)) && u) { std::wstring h = BugUrlHost(u); if (!h.empty()) g_bugHost = h; CoTaskMemFree(u); }
    return S_OK;
}
static HRESULT BugNavDone(ICoreWebView2 *wv, ICoreWebView2NavigationCompletedEventArgs *a)
{
    BOOL ok = FALSE; COREWEBVIEW2_WEB_ERROR_STATUS st = COREWEBVIEW2_WEB_ERROR_STATUS_UNKNOWN;
    a->get_IsSuccess(&ok); a->get_WebErrorStatus(&st);
    if (ok || st != COREWEBVIEW2_WEB_ERROR_STATUS_OPERATION_CANCELED)   // (annulee : une navigation refusee, deja dans le journal)
        LaunchLog("signalement : page %s%s (%s)", ok ? "chargee" : "en echec ", ok ? "" : std::to_string((int)st).c_str(), Narrow(g_bugHost, CP_UTF8).c_str());
    if (g_bugTest && !g_bugTestDone) {   // essai : une adresse hors liste, puis la racine du site (le tableau), refusees ; puis fin
        g_bugTestDone = 1; g_bugTestEndT = GetTickCount() + 1200;
        LaunchLog("testsignal : essai d'une navigation vers example.com");
        wv->Navigate(L"https://example.com/");
    }
    return S_OK;
}
static HRESULT BugCtlDone(HRESULT hr, ICoreWebView2Controller *ctl)
{
    if (FAILED(hr) || !ctl) { BugFail(hr, "controleur"); return S_OK; }
    g_bugCtl = ctl; ctl->AddRef();
    if (FAILED(ctl->get_CoreWebView2(&g_bugWv)) || !g_bugWv) { BugFail(E_FAIL, "moteur"); return S_OK; }
    ICoreWebView2Settings *s = NULL;   // aucun pont entre la page et le lanceur
    if (SUCCEEDED(g_bugWv->get_Settings(&s)) && s) {
        s->put_AreHostObjectsAllowed(FALSE); s->put_IsWebMessageEnabled(FALSE); s->put_AreDevToolsEnabled(FALSE);
        s->put_AreDefaultContextMenusEnabled(FALSE); s->put_IsStatusBarEnabled(FALSE); s->put_IsZoomControlEnabled(FALSE);
        s->put_IsBuiltInErrorPageEnabled(TRUE);
        ICoreWebView2Settings3 *s3 = NULL;
        if (SUCCEEDED(s->QueryInterface(__uuidof(ICoreWebView2Settings3), (void **)&s3)) && s3) { s3->put_AreBrowserAcceleratorKeysEnabled(FALSE); s3->Release(); }
        s->Release();
    }
    EventRegistrationToken t;
    { auto *h = BugMk<ICoreWebView2NavigationStartingEventHandler, ICoreWebView2 *, ICoreWebView2NavigationStartingEventArgs *>(BugNavStart); g_bugWv->add_NavigationStarting(h, &t); g_bugWv->add_FrameNavigationStarting(h, &t); h->Release(); }
    { auto *h = BugMk<ICoreWebView2NewWindowRequestedEventHandler, ICoreWebView2 *, ICoreWebView2NewWindowRequestedEventArgs *>(BugNewWindow); g_bugWv->add_NewWindowRequested(h, &t); h->Release(); }
    { auto *h = BugMk<ICoreWebView2SourceChangedEventHandler, ICoreWebView2 *, ICoreWebView2SourceChangedEventArgs *>(BugSource); g_bugWv->add_SourceChanged(h, &t); h->Release(); }
    { auto *h = BugMk<ICoreWebView2NavigationCompletedEventHandler, ICoreWebView2 *, ICoreWebView2NavigationCompletedEventArgs *>(BugNavDone); g_bugWv->add_NavigationCompleted(h, &t); h->Release(); }
    ICoreWebView2_4 *w4 = NULL;
    if (SUCCEEDED(g_bugWv->QueryInterface(__uuidof(ICoreWebView2_4), (void **)&w4)) && w4) {
        auto *h = BugMk<ICoreWebView2DownloadStartingEventHandler, ICoreWebView2 *, ICoreWebView2DownloadStartingEventArgs *>(BugDownload); w4->add_DownloadStarting(h, &t); h->Release(); w4->Release();
    }
    ICoreWebView2Controller2 *c2 = NULL;   // fond au theme du lanceur, le temps du chargement
    if (SUCCEEDED(ctl->QueryInterface(__uuidof(ICoreWebView2Controller2), (void **)&c2)) && c2) {
        COREWEBVIEW2_COLOR bg = { 255, (BYTE)(g_dark ? 24 : 246), (BYTE)(g_dark ? 26 : 247), (BYTE)(g_dark ? 32 : 250) };
        c2->put_DefaultBackgroundColor(bg); c2->Release();
    }
    RECT r; GetClientRect(g_bugWnd, &r);
    ctl->put_Bounds(r);
    ctl->put_IsVisible(g_bugShown || g_bugTest);
    g_bugState = 2;
    g_bugHost = kBugHost;
    std::wstring url = std::wstring(kBugSiteUrl) + L"?lang=" + LangCode();
    g_bugWv->Navigate(url.c_str());
    LaunchLog("signalement : panneau pret (WebView2 %s), %s", Narrow(g_bugVer, CP_UTF8).c_str(), Narrow(url, CP_UTF8).c_str());
    return S_OK;
}
static HRESULT BugEnvDone(HRESULT hr, ICoreWebView2Environment *env)
{
    if (FAILED(hr) || !env) { BugFail(hr, "environnement"); return S_OK; }
    g_bugEnv = env; env->AddRef();
    auto *h = BugMk<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler, HRESULT, ICoreWebView2Controller *>(BugCtlDone);
    hr = env->CreateCoreWebView2Controller(g_bugWnd, h);
    h->Release();
    if (FAILED(hr)) BugFail(hr, "creation du controleur");
    return S_OK;
}
static LRESULT CALLBACK BugWndProc(HWND h, UINT m, WPARAM wp, LPARAM lp)
{
    switch (m) {
    case WM_SIZE: if (g_bugCtl) { RECT r; GetClientRect(h, &r); g_bugCtl->put_Bounds(r); } return 0;
    case WM_ERASEBKGND: {
        RECT r; GetClientRect(h, &r);
        HBRUSH b = CreateSolidBrush(g_dark ? RGB(24, 26, 32) : RGB(246, 247, 250));
        FillRect((HDC)wp, &r, b); DeleteObject(b);
        return 1;
    }
    }
    return DefWindowProcW(h, m, wp, lp);
}
static RECT BugPanelRect()   // la popup, en pixels de l'ecran : la zone du site, sous la bande
{
    RECT wr; GetWindowRect(g_wnd, &wr);
    RectF w = BugWebR();
    RECT r;
    r.left = wr.left + (LONG)((w.X + kM) * g_scale); r.top = wr.top + (LONG)((w.Y + kM) * g_scale);
    r.right = r.left + (LONG)(w.Width * g_scale); r.bottom = r.top + (LONG)(w.Height * g_scale);
    return r;
}
static void BugPanelCreate()
{
    if (g_bugState != 0 || !g_wnd || g_bugCapture || !BugPanelPossible()) return;
    if (!g_bugWnd) {
        static bool reg;
        if (!reg) {
            WNDCLASSEXW wc = { sizeof(wc) };
            wc.lpfnWndProc = BugWndProc; wc.hInstance = GetModuleHandleW(NULL); wc.hCursor = LoadCursor(NULL, IDC_ARROW); wc.lpszClassName = L"MWCoopSignalement";
            RegisterClassExW(&wc);
            reg = true;
        }
        RECT r = BugPanelRect();
        g_bugWnd = CreateWindowExW(WS_EX_TOOLWINDOW, L"MWCoopSignalement", L"", WS_POPUP | WS_CLIPCHILDREN, r.left, r.top, r.right - r.left, r.bottom - r.top, g_wnd, NULL, GetModuleHandleW(NULL), NULL);
        if (!g_bugWnd) { BugFail(HRESULT_FROM_WIN32(GetLastError()), "fenetre du panneau"); return; }
        g_bugLastR = r;
    }
    g_bugHiddenT = GetTickCount();
    g_bugErr.clear();
    g_bugState = 1;
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);   // (le moteur veut un appartement STA sur ce fil ; S_FALSE si deja fait)
    std::wstring data = LocalDir().empty() ? L"" : LocalDir() + L"webview";
    auto *h = BugMk<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler, HRESULT, ICoreWebView2Environment *>(BugEnvDone);
    HRESULT hr = CreateCoreWebView2EnvironmentWithOptions(NULL, data.empty() ? NULL : data.c_str(), NULL, h);
    h->Release();
    if (FAILED(hr)) BugFail(hr, "creation de l'environnement");
    else LaunchLog("signalement : moteur demande (donnees %s)", data.empty() ? "a cote de l'exe" : Narrow(data, CP_UTF8).c_str());
}
// A chaque image et a chaque deplacement de la fenetre : la popup suit la zone du site, ou se cache.
static void BugPanelSync()
{
    if (!g_bugWnd) return;
    if (g_state != ST_IDLE) { BugPanelClose(); return; }   // (le jeu part : les processus du moteur ne restent pas pendant la partie)
    bool want = g_tab == TAB_BUG && !TutoActive() && !g_guide && !g_sb && !g_bugTest && g_bugState != 3 && !IsIconic(g_wnd) && IsWindowVisible(g_wnd);
    if (!want) {
        if (g_bugShown) {
            ShowWindow(g_bugWnd, SW_HIDE);
            if (g_bugCtl) g_bugCtl->put_IsVisible(FALSE);
            g_bugShown = false; g_bugHiddenT = GetTickCount();
        } else if (g_tab != TAB_BUG && !g_bugTest && GetTickCount() - g_bugHiddenT > 60000) BugPanelClose();
        return;
    }
    RECT r = BugPanelRect();
    if (!g_bugShown || memcmp(&r, &g_bugLastR, sizeof r)) {
        SetWindowPos(g_bugWnd, NULL, r.left, r.top, r.right - r.left, r.bottom - r.top, SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);
        g_bugLastR = r;
        if (!g_bugShown && g_bugCtl) g_bugCtl->put_IsVisible(TRUE);
        g_bugShown = true;
    }
}
static void BugTestTick()   // (essai /testsignal panneau : fin)
{
    if (!g_bugTest || !g_bugTestDone || (g_bugTestEndT && GetTickCount() < g_bugTestEndT)) return;
    if (g_bugTestDone == 1 && g_bugWv) {
        g_bugTestDone = 2; g_bugTestEndT = GetTickCount() + 1200;
        LaunchLog("testsignal : essai d'une navigation vers la racine du site (le tableau)");
        g_bugWv->Navigate((std::wstring(L"https://") + kBugHost + L"/").c_str());
        return;
    }
    LaunchLog("testsignal : fin, etat %d, %d navigation(s) vue(s), domaine « %s »", g_bugState, g_bugTestNav, Narrow(g_bugHost, CP_UTF8).c_str());
    g_bugTest = false; g_bugTestDone = 3;
    BugPanelClose();
    DestroyWindow(g_wnd);
}

// ---- la page : la bande, et le site dessous (la popup ; sans elle, un cadre et un mot)
static void BugOpen()
{
    if (g_tab != TAB_BUG) GoPage(TAB_BUG);
    BugPanelCreate();
    SetStatus(K_NORMAL, T(L"Signaler un bug : le formulaire du site, ici · « Créer le zip des journaux » en haut, puis glisse le zip dedans",
                          L"Report a bug: the site's form, right here · \"Create the logs zip\" at the top, then drag the zip into it"));
}
static void BugZip()
{
    std::vector<std::wstring> files = ZipFiles();
    if (files.empty()) { SetStatus(K_WARN, T(L"Aucun journal pour l'instant : lance le jeu avec MWCoop d'abord", L"No logs yet: start the game with MWCoop first")); return; }
    if (ZipToDesktop(files, L"", L"MWCoop-signalement-", true).empty()) return;
    LaunchLog("signalement : zip des journaux (%d fichier(s))", (int)files.size());
    SetStatus(K_OK, T(L"Zip des journaux créé (%d fichiers) : glisse-le dans le formulaire", L"Logs zip created (%d files): drag it into the form"), (int)files.size());
}
static void DrawLock(Graphics &g, float cx, float cy, float s, Color c)
{
    float w = s * 0.74f, h = s * 0.5f;
    RectF body(cx - w / 2, cy - h / 2 + s * 0.14f, w, h);
    GraphicsPath p; RoundRect(p, body, s * 0.1f);
    SolidBrush b(c); g.FillPath(&b, &p);
    Pen pen(c, s * 0.12f); pen.SetStartCap(LineCapRound); pen.SetEndCap(LineCapRound);
    float r = s * 0.2f, top = body.Y - s * 0.1f;
    g.DrawArc(&pen, cx - r, top - r, 2 * r, 2 * r, 180.0f, 180.0f);
    g.DrawLine(&pen, cx - r, top, cx - r, body.Y + 1); g.DrawLine(&pen, cx + r, top, cx + r, body.Y + 1);
    SolidBrush k(g_dark ? Color(255, 24, 26, 32) : Color(255, 255, 255, 255)); g.FillEllipse(&k, cx - s * 0.07f, body.Y + h * 0.32f, s * 0.14f, s * 0.14f);
}
static void DrawBugPage(Graphics &g)
{
    RectF band(kMain.X, kMain.Y, kMain.Width, kBugBandH);
    Glass(band, 18);
    float cy = band.Y + band.Height / 2;
    // le cadenas et le domaine (contre le hameconnage : toujours visibles, dessines par le lanceur, pas par la page)
    DrawLock(g, band.X + 30, cy, 20, kAcc);
    Text(g, g_bugHost.empty() ? kBugHost : g_bugHost, RectF(band.X + 48, cy - 11, 400, 22), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
    // a droite : Fermer, Ouvrir dans le navigateur, Creer le zip des journaux
    float right = band.X + band.Width - 12;
    UiButton(g, RectF(right - 38, cy - 19, 38, 38), L"", UI_BUG_CLOSE, false, IC_CLOSE); right -= 48;
    std::wstring bz = T(L"Créer le zip des journaux", L"Create the logs zip"), bb = T(L"Ouvrir dans le navigateur", L"Open in the browser");
    Graphics &mg = MeasureG();
    float wz = MeasureW(mg, bz, 12.5f, FontStyleBold) + 50, wb = MeasureW(mg, bb, 12.5f, FontStyleBold) + 50;
    UiButton(g, RectF(right - wb, cy - 18, wb, 36), bb, UI_BUG_BROWSER, false, IC_GLOBE); right -= wb + 8;
    UiButton(g, RectF(right - wz, cy - 18, wz, 36), bz, UI_BUG_ZIP, true, IC_ZIP, !g_gameDir.empty());
    // la zone du site : la popup la couvre quand le moteur est pret ; sinon un cadre et un mot
    RectF w = BugWebR();
    if (g_bugCapture || !g_bugShown) {
        Glass(w, 18);
        std::wstring msg = g_bugState == 3 ? g_bugErr
                         : g_bugCapture ? T(L"Le formulaire du site de signalement s'affiche ici.", L"The bug report site's form shows here.")
                         : T(L"Chargement du formulaire…", L"Loading the form…");
        Text(g, msg, RectF(w.X + 40, w.Y + w.Height / 2 - 14, w.Width - 80, 28), 14, FontStyleRegular, g_bugState == 3 ? kWarn : kGrey, StringAlignmentCenter);
    }
}
static bool BugClick(int id)
{
    switch (id) {
    case UI_BUG_ZIP: BugZip(); return true;
    case UI_BUG_BROWSER: {
        std::wstring url = std::wstring(kBugSiteUrl) + L"?lang=" + LangCode();
        LaunchLog("signalement : formulaire ouvert dans le navigateur (bouton de la bande)");
        if (!g_bugTest) ShellExecuteW(g_wnd, L"open", url.c_str(), NULL, NULL, SW_SHOWNORMAL);
        SetStatus(K_OK, T(L"Formulaire ouvert dans le navigateur", L"Form opened in the browser"));
        return true;
    }
    case UI_BUG_CLOSE: GoPage(PageBack()); return true;
    }
    return false;
}
