// ---------------------------------------------------------------- FENETRE : nouvelle interface (refonte Soft UI, lot L0)
// Spec D:\1 - AI\ClaudeAI\MWCoop-spec-refonte-lanceur.md (JD, 27/10 : « Oui c'est validé »), §2 et §9. Les deux interfaces
// vivent dans le meme exe : [Lanceur] Interface=ancienne (defaut) | nouvelle.
//  - Ancienne : la fenetre en couches d'avant (UpdateLayeredWindow, taille fixe kImgW x kImgH a l'echelle), rien ne change.
//  - Nouvelle : fenetre NON layered (WS_POPUP | WS_THICKFRAME, cadre dessine par le lanceur), taille libre (bords et coins,
//    960 x 600 au moins), Agrandir (zone de travail), plein ecran (F11), double-clic sur l'en-tete, glisser l'en-tete ;
//    taille gardee dans [Lanceur] Fenetre=x,y,l,h,agrandie (reprise si l'ecran existe encore, sinon centree a 94 % au plus) ;
//    PPP par fenetre (GetDpiForWindow / WM_DPICHANGED charges a la volee : Win10+, Win7 = PPP du systeme). Rendu par WM_PAINT
//    + BitBlt de la zone changee, et seulement quand quelque chose a change (Invalidate*, 0 image/s au repos ; un battement
//    toutes les 2 s tant que les composants ne s'invalident pas eux-memes, L1). Lot L0 : la carte d'avant (1280 x 800) est
//    dessinee a l'echelle qui tient dans la fenetre, centree, sur un fond uni au theme ; la coque (en-tete, barre, barre
//    d'etat) et les pages au nouveau style viennent avec L2 / L3.
//  - Choix au premier lancement (§9) : fenetre « Nouvelle interface » (g_guideKind 2) avec apercu, « Essayer la nouvelle
//    interface » / « Garder l'ancienne » ([Lanceur] InterfaceChoisie=1 ensuite) ; Reglages > Lanceur > Fenetre du lanceur
//    pour changer d'avis (le lanceur redemarre). Filet de securite : [Lanceur] NouvelleEnCours=1 pose avant la 1re image
//    de la nouvelle interface, efface 5 s plus tard ; retrouve au demarrage = elle a plante : l'ancienne s'ouvre, Interface
//    remis a ancienne, et la barre d'etat propose Signaler un bug.
// Aucun import statique (regle version.dll) : user32 recent (PPP par fenetre) et dwmapi (coins, ombre) par GetProcAddress.
static bool g_newUi;                 // nouvelle interface (lue au demarrage : UiNewRead)
static bool g_uiCrashed;             // filet : la nouvelle a plante au lancement precedent
static int g_offX, g_offY;           // nouvelle : coin de la carte (kImgW x kImgH a l'echelle) dans la fenetre
static float g_dpiScale = 1;         // nouvelle : PPP de la fenetre / 96 (taille minimale, taille par defaut)
static bool g_fullscreen;            // F11
static WINDOWPLACEMENT g_fsBefore;   // placement avant le plein ecran
static bool g_winSizeKnown;          // taille lue de l'ini (sinon centree au demarrage)
static bool g_dirtyAll;              // nouvelle : tout redessiner a la prochaine image
static RECT g_dirtyPx;               // nouvelle : zone a redessiner, en pixels de la fenetre (vide : rien)
static DWORD g_uiFlagT;              // filet : quand effacer NouvelleEnCours
static Bitmap *g_cardBmp;            // nouvelle : la carte rendue (PARGB), composee sur le fond uni de g_memDC
static const int kWinMinW = 960, kWinMinH = 600;
static void RenderTo(Bitmap &target, float scale);   // (launcher.cpp)
static int HitButton(float x, float y);
static int HitField(float x, float y);
struct WinMargins { int l, r, t, b; };   // (MARGINS de dwmapi.h, sans l'en-tete)
static void InvalidateAll() { g_dirtyAll = true; }
static void InvalidatePx(const RECT &r)
{
    if (IsRectEmpty(&g_dirtyPx)) g_dirtyPx = r; else UnionRect(&g_dirtyPx, &g_dirtyPx, &r);
}
static void InvalidateCard(RectF r)   // zone en coordonnees de la carte
{
    RECT px = { g_offX + (LONG)((r.X + kM) * g_scale) - 1, g_offY + (LONG)((r.Y + kM) * g_scale) - 1,
                g_offX + (LONG)((r.X + r.Width + kM) * g_scale) + 2, g_offY + (LONG)((r.Y + r.Height + kM) * g_scale) + 2 };
    InvalidatePx(px);
}
static bool AnimReduced()   // Reglages > Fenetre du lanceur, ou Windows « afficher les animations » coupe
{
    static int ini = -1;
    if (ini < 0) ini = GetPrivateProfileIntW(L"Lanceur", L"AnimationsReduites", 0, g_iniLauncher.c_str()) ? 1 : 0;
    if (ini) return true;
    BOOL anim = TRUE;
    SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, &anim, 0);
    return !anim;
}
static void AnimReducedSet(bool on) { WritePrivateProfileStringW(L"Lanceur", L"AnimationsReduites", on ? L"1" : NULL, g_iniLauncher.c_str()); }
static bool WinMemoryOn() { return GetPrivateProfileIntW(L"Lanceur", L"FenetreMemoire", 1, g_iniLauncher.c_str()) != 0; }
static void UiNewRead()
{
    wchar_t v[16] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Interface", L"ancienne", v, 16, g_iniLauncher.c_str());
    g_newUi = !_wcsicmp(v, L"nouvelle");
    if (g_newUi && GetPrivateProfileIntW(L"Lanceur", L"NouvelleEnCours", 0, g_iniLauncher.c_str())) {   // filet : elle a plante la derniere fois
        g_newUi = false; g_uiCrashed = true;
        WritePrivateProfileStringW(L"Lanceur", L"Interface", L"ancienne", g_iniLauncher.c_str());
        WritePrivateProfileStringW(L"Lanceur", L"NouvelleEnCours", NULL, g_iniLauncher.c_str());
        LaunchLog("interface : la nouvelle n'a pas passe son premier demarrage la derniere fois : ancienne interface, Interface=ancienne");
    }
    LaunchLog("interface : %s", g_newUi ? "nouvelle (refonte, lot L0)" : "ancienne");
}
static void UiNewSet(bool on)   // (Reglages, ou la fenetre de choix) : ecrit, puis le lanceur redemarre
{
    WritePrivateProfileStringW(L"Lanceur", L"Interface", on ? L"nouvelle" : L"ancienne", g_iniLauncher.c_str());
    WritePrivateProfileStringW(L"Lanceur", L"InterfaceChoisie", L"1", g_iniLauncher.c_str());
    LaunchLog("interface : %s choisie, relance", on ? "nouvelle" : "ancienne");
    PostMessageW(g_wnd, WM_APP_RELAUNCH, 0, 0);
}
// ---- PPP, DWM (charges a la volee)
static UINT WinDpi(HWND h)
{
    typedef UINT(WINAPI * F)(HWND);
    static F f = (F)GetProcAddress(GetModuleHandleW(L"user32.dll"), "GetDpiForWindow");
    if (f && h) { UINT d = f(h); if (d) return d; }
    HDC dc = GetDC(NULL); int d = GetDeviceCaps(dc, LOGPIXELSX); ReleaseDC(NULL, dc);
    return d ? d : 96;
}
static void WinDpiAwareness()   // avant la fenetre : PPP par moniteur (Win10 1703+), sinon le manifeste (PPP du systeme)
{
    typedef BOOL(WINAPI * F)(HANDLE);
    if (F f = (F)GetProcAddress(GetModuleHandleW(L"user32.dll"), "SetProcessDpiAwarenessContext")) f((HANDLE)-4 /* DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 */);
}
static void WinDwm(HWND h)   // coins arrondis (Win11), ombre (DWM actif) ; rien sous Win7 sans Aero ni sous Wine
{
    if (IsWine()) return;
    HMODULE dwm = LoadLibraryW(L"dwmapi.dll");
    if (!dwm) return;
    typedef HRESULT(WINAPI * SetF)(HWND, DWORD, LPCVOID, DWORD);
    typedef HRESULT(WINAPI * ExtF)(HWND, const WinMargins *);
    typedef HRESULT(WINAPI * EnF)(BOOL *);
    BOOL on = FALSE;
    if (EnF en = (EnF)GetProcAddress(dwm, "DwmIsCompositionEnabled")) en(&on);
    if (on) {
        if (SetF sa = (SetF)GetProcAddress(dwm, "DwmSetWindowAttribute")) { DWORD pref = 2 /* DWMWCP_ROUND */; sa(h, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, &pref, sizeof(pref)); }
        if (ExtF ex = (ExtF)GetProcAddress(dwm, "DwmExtendFrameIntoClientArea")) { WinMargins m = { 1, 1, 1, 1 }; ex(h, &m); }
    }
    FreeLibrary(dwm);
}
// ---- taille et place
static RECT WinWorkArea(HWND h)
{
    HMONITOR mon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi = { sizeof(mi) };
    if (mon && GetMonitorInfoW(mon, &mi)) return mi.rcWork;
    RECT w; SystemParametersInfoW(SPI_GETWORKAREA, 0, &w, 0); return w;
}
static bool WinRectRead(RECT &r, bool &maxed)   // [Lanceur] Fenetre=x,y,l,h,agrandie ; faux si absente ou hors ecran
{
    wchar_t v[96] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Fenetre", L"", v, 96, g_iniLauncher.c_str());
    int x, y, w, h, m = 0;
    if (!WinMemoryOn() || swscanf(v, L"%d,%d,%d,%d,%d", &x, &y, &w, &h, &m) < 4) return false;
    if (w < kWinMinW / 2 || h < kWinMinH / 2 || w > 16384 || h > 16384) return false;
    RECT t = { x, y, x + w, y + h };
    if (!MonitorFromRect(&t, MONITOR_DEFAULTTONULL)) return false;   // (l'ecran n'existe plus : centree)
    r = t; maxed = m != 0;
    return true;
}
static void WinRectSave()
{
    if (!g_newUi || !g_wnd || g_fullscreen || !WinMemoryOn()) return;
    WINDOWPLACEMENT wp = { sizeof(wp) };
    if (!GetWindowPlacement(g_wnd, &wp)) return;
    RECT r = wp.rcNormalPosition;
    wchar_t v[96];
    swprintf_s(v, L"%ld,%ld,%ld,%ld,%d", r.left, r.top, r.right - r.left, r.bottom - r.top, wp.showCmd == SW_SHOWMAXIMIZED ? 1 : 0);
    WritePrivateProfileStringW(L"Lanceur", L"Fenetre", v, g_iniLauncher.c_str());
}
// La carte d'avant (kImgW x kImgH) a l'echelle qui tient dans la fenetre, centree ; le tampon de la fenetre refait.
static void WinLayout()
{
    if (!g_newUi || !g_wnd) return;
    RECT c; GetClientRect(g_wnd, &c);
    int w = max(1L, c.right - c.left), h = max(1L, c.bottom - c.top);
    if (w == g_winW && h == g_winH && g_memDC) return;
    g_winW = w; g_winH = h;
    g_scale = max(0.3f, min(w / kImgW, h / kImgH));
    g_offX = (int)((w - kImgW * g_scale) / 2); g_offY = (int)((h - kImgH * g_scale) / 2);
    if (g_memDC) { DeleteDC(g_memDC); g_memDC = NULL; }
    if (g_dib) { DeleteObject(g_dib); g_dib = NULL; }
    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(bi.bmiHeader); bi.bmiHeader.biWidth = w; bi.bmiHeader.biHeight = -h; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32;
    g_memDC = CreateCompatibleDC(NULL);
    g_dib = CreateDIBSection(g_memDC, &bi, DIB_RGB_COLORS, &g_bits, NULL, 0);
    SelectObject(g_memDC, g_dib);
    delete g_cardBmp; g_cardBmp = NULL;
    delete g_bgCache; g_bgCache = NULL;   // (fond a l'echelle de la fenetre : refait a la nouvelle taille)
    InvalidateAll();
    LaunchLog("fenetre : %d x %d, echelle %.2f, carte en %d,%d", w, h, g_scale, g_offX, g_offY);
}
static void WinFullscreen(bool on)
{
    if (!g_newUi || on == g_fullscreen) return;
    g_fullscreen = on;
    if (on) {
        g_fsBefore.length = sizeof(g_fsBefore); GetWindowPlacement(g_wnd, &g_fsBefore);
        HMONITOR mon = MonitorFromWindow(g_wnd, MONITOR_DEFAULTTONEAREST);
        MONITORINFO mi = { sizeof(mi) }; GetMonitorInfoW(mon, &mi);
        SetWindowLongPtrW(g_wnd, GWL_STYLE, (GetWindowLongPtrW(g_wnd, GWL_STYLE) & ~WS_THICKFRAME) | WS_POPUP);
        SetWindowPos(g_wnd, HWND_TOP, mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right - mi.rcMonitor.left, mi.rcMonitor.bottom - mi.rcMonitor.top, SWP_FRAMECHANGED | SWP_NOACTIVATE);
    } else {
        SetWindowLongPtrW(g_wnd, GWL_STYLE, GetWindowLongPtrW(g_wnd, GWL_STYLE) | WS_THICKFRAME);
        SetWindowPlacement(g_wnd, &g_fsBefore);
        SetWindowPos(g_wnd, NULL, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }
    LaunchLog("fenetre : plein ecran %s", on ? "oui" : "non");
    WinLayout();
}
static void WinMaximizeToggle()
{
    if (!g_newUi) return;
    if (g_fullscreen) { WinFullscreen(false); return; }
    ShowWindow(g_wnd, IsZoomed(g_wnd) ? SW_RESTORE : SW_MAXIMIZE);
}
// Creation de la fenetre de la nouvelle interface (a la place de la fenetre en couches) : style, taille gardee ou centree.
static HWND WinCreateNew(HINSTANCE inst, const wchar_t *cls)
{
    RECT work; SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    g_dpiScale = WinDpi(NULL) / 96.0f;
    RECT r; bool maxed = false;
    g_winSizeKnown = WinRectRead(r, maxed);
    if (!g_winSizeKnown) {
        float fit = min((work.right - work.left) * 0.94f / kImgW, (work.bottom - work.top) * 0.97f / kImgH);
        float s = max(0.5f, min(g_dpiScale, fit));
        int w = max((int)(kImgW * s), (int)(kWinMinW * g_dpiScale)), h = max((int)(kImgH * s), (int)(kWinMinH * g_dpiScale));
        r.left = work.left + (work.right - work.left - w) / 2; r.top = work.top + (work.bottom - work.top - h) / 2; r.right = r.left + w; r.bottom = r.top + h;
    }
    HWND h = CreateWindowExW(WS_EX_APPWINDOW, cls, L"MWCoop", WS_POPUP | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_CLIPCHILDREN,
                             r.left, r.top, r.right - r.left, r.bottom - r.top, NULL, NULL, inst, NULL);
    if (!h) return NULL;
    g_wnd = h;
    g_dpiScale = WinDpi(h) / 96.0f;
    WinDwm(h);
    WinLayout();
    if (maxed) ShowWindow(h, SW_MAXIMIZE);   // (ShowWindow(SW_SHOW) plus tard, comme avant ; SW_MAXIMIZE sur une fenetre cachee la montre : seulement si demande)
    return h;
}
// Messages de la fenetre propres a la nouvelle interface. Vrai si traite (res = resultat).
static LRESULT WinNcHitTest(HWND h, LPARAM lp)
{
    POINT p = { (short)LOWORD(lp), (short)HIWORD(lp) };
    ScreenToClient(h, &p);
    RECT c; GetClientRect(h, &c);
    if (!IsZoomed(h) && !g_fullscreen) {   // bords et coins (8 px a l'echelle du PPP)
        int b = (int)(8 * g_dpiScale);
        bool l = p.x < b, r = p.x >= c.right - b, t = p.y < b, bo = p.y >= c.bottom - b;
        if (t && l) return HTTOPLEFT; if (t && r) return HTTOPRIGHT; if (bo && l) return HTBOTTOMLEFT; if (bo && r) return HTBOTTOMRIGHT;
        if (l) return HTLEFT; if (r) return HTRIGHT; if (t) return HTTOP; if (bo) return HTBOTTOM;
    }
    float x = (p.x - g_offX) / g_scale - kM, y = (p.y - g_offY) / g_scale - kM;   // (coordonnees de la carte)
    if (x < 0 || y < 0 || x > kCardW || y > kCardH) return HTCAPTION;             // (le fond autour de la carte : glisser)
    if (y < 84 && HitButton(x, y) < 0 && ZoneAt(x, y) < 0 && HitField(x, y) < 0) return HTCAPTION;   // (l'en-tete, hors de ses boutons et champs)
    return HTCLIENT;
}
static bool WinProcNew(HWND h, UINT m, WPARAM wp, LPARAM lp, LRESULT &res)
{
    if (!g_newUi) return false;
    switch (m) {
    case WM_NCCALCSIZE:   // tout est zone cliente : le cadre, c'est le lanceur qui le dessine ; agrandie, la fenetre deborde de l'ecran du cadre invisible : on le retire
        if (wp && IsZoomed(h)) {
            NCCALCSIZE_PARAMS *p = (NCCALCSIZE_PARAMS *)lp;
            int b = GetSystemMetrics(SM_CXSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
            InflateRect(&p->rgrc[0], -b, -b);
        }
        res = 0; return true;
    case WM_NCHITTEST: res = WinNcHitTest(h, lp); return true;
    case WM_GETMINMAXINFO: {
        MINMAXINFO *mi = (MINMAXINFO *)lp;
        mi->ptMinTrackSize.x = (LONG)(kWinMinW * g_dpiScale); mi->ptMinTrackSize.y = (LONG)(kWinMinH * g_dpiScale);
        RECT w = WinWorkArea(h);   // (agrandie : la zone de travail, pas par-dessus la barre des taches)
        HMONITOR mon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
        MONITORINFO mo = { sizeof(mo) }; GetMonitorInfoW(mon, &mo);
        mi->ptMaxPosition.x = w.left - mo.rcMonitor.left; mi->ptMaxPosition.y = w.top - mo.rcMonitor.top;
        mi->ptMaxSize.x = w.right - w.left; mi->ptMaxSize.y = w.bottom - w.top;
        res = 0; return true;
    }
    case WM_SIZE:
        if (wp != SIZE_MINIMIZED) { WinLayout(); if (wp == SIZE_MAXIMIZED || wp == SIZE_RESTORED) WinRectSave(); }
        res = 0; return true;
    case WM_EXITSIZEMOVE: WinRectSave(); res = 0; return true;
    case WM_DPICHANGED: {
        g_dpiScale = HIWORD(wp) / 96.0f;
        const RECT *r = (const RECT *)lp;
        SetWindowPos(h, NULL, r->left, r->top, r->right - r->left, r->bottom - r->top, SWP_NOZORDER | SWP_NOACTIVATE);
        LaunchLog("fenetre : PPP %u", (unsigned)HIWORD(wp));
        res = 0; return true;
    }
    case WM_ERASEBKGND: res = 1; return true;
    case WM_PAINT: {
        PAINTSTRUCT ps; HDC dc = BeginPaint(h, &ps);
        if (g_memDC) BitBlt(dc, ps.rcPaint.left, ps.rcPaint.top, ps.rcPaint.right - ps.rcPaint.left, ps.rcPaint.bottom - ps.rcPaint.top, g_memDC, ps.rcPaint.left, ps.rcPaint.top, SRCCOPY);
        EndPaint(h, &ps);
        res = 0; return true;
    }
    case WM_KEYDOWN:
        if (wp == VK_F11) { WinFullscreen(!g_fullscreen); res = 0; return true; }
        if (wp == VK_ESCAPE && g_fullscreen) { WinFullscreen(false); res = 0; return true; }
        return false;
    }
    return false;
}
// Image de la nouvelle interface : la carte rendue (RenderTo) puis composee sur le fond uni, et seulement la zone changee
// copiee a l'ecran. Rien a faire si rien n'a change.
static Color WinBg() { return g_dark ? Color(255, 29, 33, 41) : Color(255, 228, 233, 240); }   // (jetons bg de la spec §3)
static void PresentNew()
{
    if (!g_wnd || !g_memDC) return;
    if (!g_dirtyAll && IsRectEmpty(&g_dirtyPx)) return;
    int cw = (int)(kImgW * g_scale), ch = (int)(kImgH * g_scale);
    if (!g_cardBmp || (int)g_cardBmp->GetWidth() != cw || (int)g_cardBmp->GetHeight() != ch) { delete g_cardBmp; g_cardBmp = new Bitmap(cw, ch, PixelFormat32bppPARGB); }
    RenderTo(*g_cardBmp, g_scale);
    {
        Bitmap win(g_winW, g_winH, g_winW * 4, PixelFormat32bppPARGB, (BYTE *)g_bits);
        Graphics g(&win);
        if (g_dirtyAll) g.Clear(WinBg());
        g.SetCompositingMode(CompositingModeSourceOver);
        g.SetInterpolationMode(InterpolationModeNearestNeighbor);
        if (!g_dirtyAll) { g.Clear(WinBg()); }   // (zone partielle : la carte entiere est quand meme recomposee, L0 -- la copie a l'ecran, elle, est partielle)
        g.DrawImage(g_cardBmp, g_offX, g_offY, cw, ch);
    }
    GdiFlush();
    RECT r = g_dirtyAll ? RECT{ 0, 0, g_winW, g_winH } : g_dirtyPx;
    HDC dc = GetDC(g_wnd);
    BitBlt(dc, r.left, r.top, r.right - r.left, r.bottom - r.top, g_memDC, r.left, r.top, SRCCOPY);
    ReleaseDC(g_wnd, dc);
    g_dirtyAll = false; SetRectEmpty(&g_dirtyPx);
}
// Filet de securite : drapeau pose avant la premiere image, efface 5 s apres (Tick).
static void UiNewFlagStart() { if (g_newUi) { WritePrivateProfileStringW(L"Lanceur", L"NouvelleEnCours", L"1", g_iniLauncher.c_str()); g_uiFlagT = GetTickCount() + 5000; } }
static void UiNewFlagTick() { if (g_uiFlagT && GetTickCount() >= g_uiFlagT) { g_uiFlagT = 0; WritePrivateProfileStringW(L"Lanceur", L"NouvelleEnCours", NULL, g_iniLauncher.c_str()); } }

// ---- fenetre de choix au premier lancement (g_guideKind 2) : apercu des deux interfaces, Essayer / Garder
// (JD, 27/10 : « au 1er lancement, demander le choix du launcher en expliquant les + et les - de chacune » ; la nouvelle
// presélectionnée a sa sortie -- kUiNewDefault, et la mention « en test » partira alors ; en developpement, l'ancienne)
static const bool kUiNewDefault = false;
static const RectF kUiChoiceCard(290, 120, 700, 560), kUiTry(330, 608, 304, 50), kUiKeep(646, 608, 304, 50), kUiClose(926, 136, 44, 44);
static bool UiChoiceDue() { return !g_newUi && !GetPrivateProfileIntW(L"Lanceur", L"InterfaceChoisie", 0, g_iniLauncher.c_str()); }
static void UiChoiceOpen() { g_guide = true; g_guideKind = 2; g_guideHot = 0; g_focus = -1; }
static int UiChoiceHit(float x, float y)
{
    if (kUiTry.Contains(x, y)) return 1;
    if (kUiKeep.Contains(x, y)) return 2;
    if (kUiClose.Contains(x, y)) return 4;
    return kUiChoiceCard.Contains(x, y) ? 0 : -1;
}
static void UiChoiceClick(int h)
{
    if (h == 1) { g_guide = false; UiNewSet(true); return; }
    if (h == 2 || h == 4 || h < 0) { g_guide = false; g_guideHot = 0; WritePrivateProfileStringW(L"Lanceur", L"InterfaceChoisie", L"1", g_iniLauncher.c_str()); LaunchLog("interface : ancienne gardee (fenetre de choix)"); }
}
// Apercu miniature : l'ancienne (carte sombre, verre, aurore) et la nouvelle (Soft UI : fond uni, cartes en relief).
static void DrawUiPreview(Graphics &g, RectF r, bool soft)
{
    GraphicsPath p; RoundRect(p, r, 14);
    GraphicsState st = g.Save();
    g.SetClip(&p);   // (l'aurore de l'apercu reste dans son cadre)
    if (!soft) {
        LinearGradientBrush bg(PointF(r.X, r.Y), PointF(r.X + r.Width, r.Y + r.Height), Color(255, 14, 22, 44), Color(255, 30, 48, 80));
        g.FillPath(&bg, &p);
        SolidBrush aur(Color(70, 80, 220, 170)); g.FillEllipse(&aur, r.X + r.Width * 0.3f, r.Y - r.Height * 0.3f, r.Width * 0.7f, r.Height * 0.6f);
        SolidBrush rail(Color(50, 255, 255, 255)); RectF rl(r.X + 10, r.Y + 10, r.Width * 0.2f, r.Height - 20); GraphicsPath rp; RoundRect(rp, rl, 8); g.FillPath(&rail, &rp);
        for (int i = 0; i < 2; i++) { RectF c(r.X + r.Width * 0.26f + i * r.Width * 0.37f, r.Y + 26, r.Width * 0.34f, r.Height - 36); GraphicsPath cp; RoundRect(cp, c, 8); SolidBrush cb(Color(40, 255, 255, 255)); g.FillPath(&cb, &cp); Pen pn(Color(60, 255, 255, 255), 1); g.DrawPath(&pn, &cp); }
        SolidBrush tb(Color(255, 232, 240, 255)); g.FillRectangle(&tb, r.X + r.Width * 0.26f, r.Y + 10, r.Width * 0.3f, 6.0f);
    } else {
        Color bg(255, 228, 233, 240), shD(255, 184, 194, 207), shL(255, 255, 255, 255), ink(255, 36, 48, 61), acc(255, 62, 139, 216);
        SolidBrush b(bg); g.FillPath(&b, &p);
        auto card = [&](RectF c, float rad) {
            GraphicsPath cp; RoundRect(cp, c, rad);
            SolidBrush sd(Color(90, shD.GetR(), shD.GetG(), shD.GetB())); g.TranslateTransform(3, 3); g.FillPath(&sd, &cp); g.TranslateTransform(-6, -6);
            SolidBrush sl(Color(200, 255, 255, 255)); g.FillPath(&sl, &cp); g.TranslateTransform(3, 3);
            SolidBrush cb(bg); g.FillPath(&cb, &cp);
        };
        card(RectF(r.X + 10, r.Y + 10, r.Width * 0.2f, r.Height - 20), 8);
        for (int i = 0; i < 2; i++) card(RectF(r.X + r.Width * 0.26f + i * r.Width * 0.37f, r.Y + 26, r.Width * 0.34f, r.Height - 36), 8);
        SolidBrush tb(ink); g.FillRectangle(&tb, r.X + r.Width * 0.26f, r.Y + 10, r.Width * 0.3f, 6.0f);
        SolidBrush ab(acc); RectF btn(r.X + r.Width * 0.3f, r.Y + r.Height - 34, r.Width * 0.26f, 14); GraphicsPath bp; RoundRect(bp, btn, 6); g.FillPath(&ab, &bp);
    }
    g.Restore(st);
    Pen edge(Color(g_dark ? 60 : 40, 0, 0, 0), 1); g.DrawPath(&edge, &p);
}
static void DrawUiChoice(Graphics &g)
{
    SolidBrush db(Color(g_dark ? 150 : 110, 2, 6, 18));
    g.FillPath(&db, CardPath());
    RectF c = kUiChoiceCard;
    GlassLive(g, c, 24);
    Icon(g, IC_SPARK, c.X + 56, c.Y + 45, 32, kAcc);
    Title(g, T(L"Quelle interface pour le lanceur ?", L"Which interface for the launcher?"), RectF(c.X + 82, c.Y + 28, 560, 34), 24, kInk);
    {   // fermer (= garder l'ancienne pour l'instant)
        RectF r = kUiClose;
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush b(g_guideHot == 4 ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
        Icon(g, IC_CLOSE, r.X + 22, r.Y + 22, 18, kInk);
    }
    Para(g, T(L"Le lanceur a une nouvelle interface. Choisis celle que tu préfères : tu pourras changer à tout moment dans Réglages > Lanceur > Fenêtre du lanceur.",
              L"The launcher has a new interface. Pick the one you prefer: you can switch any time in Settings > Launcher > Launcher window."),
         RectF(c.X + 40, c.Y + 82, c.Width - 80, 44), 13.5f, kInk);
    float pw = (c.Width - 80 - 24) / 2, py = c.Y + 134, ph = 150;
    // chaque interface : apercu, nom, ses + et ses -
    struct Col { bool soft; const wchar_t *name; std::vector<std::wstring> plus, minus; };
    Col cols[2] = {
        { false, T(L"Ancienne", L"Current"),
          { T(L"Celle que tu connais", L"The one you know"), T(L"Thèmes Aurore, Garage, Été, couleur d'accent", L"Aurora, Garage, Summer themes, accent colour") },
          { T(L"Fenêtre de taille fixe", L"Fixed-size window") } },
        { true, T(L"Nouvelle", L"New"),
          { T(L"Fenêtre libre : agrandir, plein écran", L"Free window: maximize, full screen"), T(L"Plus aérée, animations, clair ou sombre", L"Roomier, animations, light or dark"), T(L"Recherche Ctrl+K, barre d'état", L"Ctrl+K search, status bar") },
          { T(L"Thèmes d'avant absents pour l'instant", L"Previous themes missing for now") } },
    };
    for (int k = 0; k < 2; k++) {
        float x = c.X + 40 + k * (pw + 24);
        DrawUiPreview(g, RectF(x, py, pw, ph), cols[k].soft);
        std::wstring nm = cols[k].name;
        if (cols[k].soft && !kUiNewDefault) nm += T(L" (en test)", L" (in test)");
        Text(g, nm, RectF(x, py + ph + 8, pw, 22), 14, FontStyleBold, cols[k].soft ? kAcc : kInk, StringAlignmentNear);
        float y = py + ph + 36;
        for (auto &l : cols[k].plus) { Text(g, L"+", RectF(x, y, 16, 20), 13.5f, FontStyleBold, Color(255, 95, 211, 154), StringAlignmentNear); Text(g, l, RectF(x + 18, y, pw - 18, 20), 12.5f, FontStyleRegular, kInk, StringAlignmentNear); y += 21; }
        for (auto &l : cols[k].minus) { Text(g, L"\u2212", RectF(x, y, 16, 20), 13.5f, FontStyleBold, kWarn, StringAlignmentNear); Text(g, l, RectF(x + 18, y, pw - 18, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear); y += 21; }
    }
    if (!kUiNewDefault)
        Para(g, T(L"La nouvelle interface est en test : si quelque chose cloche, l'ancienne revient toute seule au lancement suivant.",
                  L"The new interface is in test: if something goes wrong, the old one comes back by itself at the next start."),
             RectF(c.X + 40, c.Y + 432, c.Width - 80, 34), 12, kGrey);
    {   // (la nouvelle preselectionnee = bouton principal ; a sa sortie, kUiNewDefault)
        RectF r = kUiTry; GraphicsPath p; RoundRect(p, r, 16);
        SolidBrush b(g_guideHot == 1 ? kAcc2 : kAcc); g.FillPath(&b, &p);
        Text(g, T(L"Essayer la nouvelle interface", L"Try the new interface"), r, 14, FontStyleBold, kOnAcc);
    }
    {
        RectF r = kUiKeep; GraphicsPath p; RoundRect(p, r, 16);
        SolidBrush b(g_guideHot == 2 ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
        Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p);
        Text(g, T(L"Garder l'ancienne", L"Keep the current one"), r, 14, FontStyleBold, kInk);
    }
}

// ---- essai cache : /testfenetre <journal> -- la nouvelle fenetre jamais montree : disposition a plusieurs tailles, taille
// minimale, zones du cadre (WM_NCHITTEST), plein ecran, memoire de la taille, une image rendue (temps).
static bool g_winTest;
static void WinSelfTest()
{
    auto ht = [&](int x, int y) {   // (pixels de la fenetre -> code HT)
        POINT p = { x, y }; ClientToScreen(g_wnd, &p);
        return (int)SendMessageW(g_wnd, WM_NCHITTEST, 0, MAKELPARAM(p.x, p.y));
    };
    LaunchLog("testfenetre : PPP %.2f, GetDpiForWindow %s", g_dpiScale, GetProcAddress(GetModuleHandleW(L"user32.dll"), "GetDpiForWindow") ? "present" : "absent (PPP du systeme)");
    for (int k = 0; k < 3; k++) {
        static const int sz[3][2] = { { 1600, 900 }, { 1000, 640 }, { 2400, 1100 } };
        SetWindowPos(g_wnd, NULL, 40, 40, sz[k][0], sz[k][1], SWP_NOZORDER | SWP_NOACTIVATE);
        RECT c; GetClientRect(g_wnd, &c);
        LaunchLog("testfenetre : taille %d x %d -> client %ld x %ld, echelle %.3f, carte en %d,%d (%d x %d)", sz[k][0], sz[k][1], c.right, c.bottom, g_scale, g_offX, g_offY, (int)(kImgW * g_scale), (int)(kImgH * g_scale));
    }
    MINMAXINFO mi = {};
    SendMessageW(g_wnd, WM_GETMINMAXINFO, 0, (LPARAM)&mi);
    LaunchLog("testfenetre : minimum %ld x %ld, agrandie %ld x %ld en %ld,%ld", mi.ptMinTrackSize.x, mi.ptMinTrackSize.y, mi.ptMaxSize.x, mi.ptMaxSize.y, mi.ptMaxPosition.x, mi.ptMaxPosition.y);
    SetWindowPos(g_wnd, NULL, 40, 40, 1600, 900, SWP_NOZORDER | SWP_NOACTIVATE);
    RECT c; GetClientRect(g_wnd, &c);
    int hdr = g_offY + (int)((kM + 40) * g_scale), mid = g_offY + (int)((kM + 400) * g_scale), cx = g_offX + (int)((kM + 640) * g_scale);
    LaunchLog("testfenetre : cadre : coin %d, bord gauche %d, haut %d ; en-tete %d (HTCAPTION=%d) ; centre de la carte %d (HTCLIENT=%d) ; fond a cote %d",
              ht(3, 3), ht(3, c.bottom / 2), ht(c.right / 2, 3), ht(cx, hdr), HTCAPTION, ht(cx, mid), HTCLIENT, ht(g_offX > 12 ? g_offX / 2 : c.right - 2, c.bottom / 2));
    WinFullscreen(true);
    RECT w; GetWindowRect(g_wnd, &w);
    LaunchLog("testfenetre : plein ecran %ld,%ld %ld x %ld, THICKFRAME %s", w.left, w.top, w.right - w.left, w.bottom - w.top, (GetWindowLongPtrW(g_wnd, GWL_STYLE) & WS_THICKFRAME) ? "oui (ERREUR)" : "non");
    WinFullscreen(false);
    GetWindowRect(g_wnd, &w);
    LaunchLog("testfenetre : retour %ld,%ld %ld x %ld, THICKFRAME %s", w.left, w.top, w.right - w.left, w.bottom - w.top, (GetWindowLongPtrW(g_wnd, GWL_STYLE) & WS_THICKFRAME) ? "oui" : "non (ERREUR)");
    WinRectSave();
    RECT r; bool maxed = false;
    bool ok = WinRectRead(r, maxed);
    wchar_t v[96] = L""; GetPrivateProfileStringW(L"Lanceur", L"Fenetre", L"", v, 96, g_iniLauncher.c_str());
    LaunchLog("testfenetre : memoire Fenetre=%s -> relue %s (%ld,%ld %ld x %ld, agrandie %d)", Narrow(v, CP_UTF8).c_str(), ok ? "ok" : "REFUSEE", r.left, r.top, r.right - r.left, r.bottom - r.top, (int)maxed);
    WritePrivateProfileStringW(L"Lanceur", L"Fenetre", L"-30000,-30000,1200,700,0", g_iniLauncher.c_str());
    LaunchLog("testfenetre : memoire hors ecran -> %s", WinRectRead(r, maxed) ? "ACCEPTEE (ERREUR)" : "refusee, centree (ok)");
    WritePrivateProfileStringW(L"Lanceur", L"Fenetre", NULL, g_iniLauncher.c_str());
    LARGE_INTEGER f, a, z; QueryPerformanceFrequency(&f);
    InvalidateAll(); QueryPerformanceCounter(&a); PresentNew(); QueryPerformanceCounter(&z);
    double t1 = (z.QuadPart - a.QuadPart) * 1000.0 / f.QuadPart;
    QueryPerformanceCounter(&a); PresentNew(); QueryPerformanceCounter(&z);   // (rien de change : rien a faire)
    double t2 = (z.QuadPart - a.QuadPart) * 1000.0 / f.QuadPart;
    LaunchLog("testfenetre : image complete %.1f ms (1600 x 900), image sans changement %.2f ms", t1, t2);
    LaunchLog("testfenetre : NouvelleEnCours=%d avant l'effacement (filet), fin", GetPrivateProfileIntW(L"Lanceur", L"NouvelleEnCours", 0, g_iniLauncher.c_str()));
}
