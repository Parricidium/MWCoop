// (inclus par launcher.cpp, apres uikit.inc : FENETRES DE MESSAGE au style du lanceur -- 19/10)
//
// Demande de JD (19/10) : « que les popups du lanceur aient le meme design que le lanceur, pas l'aspect natif de Windows ».
// StyledBox remplace MessageBoxW partout (macro en tete de launcher.cpp) avec le meme contrat : memes types de boutons
// (MB_OK, MB_OKCANCEL, MB_YESNO, MB_YESNOCANCEL), icone (MB_ICONQUESTION / WARNING / ERROR / INFORMATION), bouton par
// defaut (MB_DEFBUTTON2 / 3), meme valeur rendue (IDOK, IDYES...). Fenetre « layered » sans cadre : carte arrondie aux
// couleurs du theme choisi (opaque : rien a flouter derriere une fenetre a part), ombre douce, icone dans une pastille,
// titre, texte coupe aux mots, boutons a droite (celui par defaut en accent). Clavier : Entree (defaut), Echap (Annuler /
// Non / OK), Gauche / Droite / Tab (bouton choisi) ; souris : survol, clic ; glisser la carte la deplace. Modale : la
// fenetre proprietaire est desactivee le temps de la reponse. Sans GDI+ (avant son demarrage) : la boite de Windows.

static bool g_gdiplusUp;   // (GdiplusStartup fait : wWinMain)

struct SbButton { int id; std::wstring label; RectF r; };
struct SbState {
    std::wstring title, text;
    UINT type;
    std::vector<SbButton> buttons;
    int focus, hot, result;
    float scale, w, h, cardX, cardY, cardW, cardH;
    HWND wnd;
    HDC dc; HBITMAP dib; void *bits;
};
static SbState *g_sb;

static const float kSbShadow = 24;   // marge autour de la carte (ombre), en points

static void SbDraw(Graphics &g, SbState &s)
{
    float k = s.scale;
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);
    g.ScaleTransform(k, k);
    RectF card(s.cardX, s.cardY, s.cardW, s.cardH);
    for (int i = 6; i >= 1; i--) {   // ombre douce
        GraphicsPath sp; RoundRect(sp, RectF(card.X - i * 2.0f, card.Y - i * 2.0f + 6, card.Width + i * 4.0f, card.Height + i * 4.0f), 22.0f + i * 2);
        SolidBrush sb(Color((BYTE)(10 + (6 - i) * 6), 0, 0, 0)); g.FillPath(&sb, &sp);
    }
    GraphicsPath cp; RoundRect(cp, card, 22);
    LinearGradientBrush bg(card, Mix(TH(fallB), TH(solid), 0.5f), TH(fallA), LinearGradientModeVertical);
    g.FillPath(&bg, &cp);
    SolidBrush tint(TH(panel)); g.FillPath(&tint, &cp);
    Pen edge(TH(panelBorder), 1.2f); g.DrawPath(&edge, &cp);
    // icone
    UINT ic = s.type & MB_ICONMASK;
    int icon = ic == MB_ICONERROR || ic == MB_ICONWARNING ? IC_ALERT : ic == MB_ICONINFORMATION ? IC_CHECK : ic == MB_ICONQUESTION ? IC_SPARK : -1;
    Color icc = ic == MB_ICONERROR ? kRed : ic == MB_ICONWARNING ? kWarn : ic == MB_ICONINFORMATION ? kOk : kAcc;
    float x = card.X + 28, y = card.Y + 26;
    if (icon >= 0) {
        SolidBrush cb(WithA(icc, 0.16f)); g.FillEllipse(&cb, x, y, 44.0f, 44.0f);
        Pen cpn(WithA(icc, 0.55f), 1.2f); g.DrawEllipse(&cpn, x, y, 44.0f, 44.0f);
        Icon(g, icon, x + 22, y + 22, 22, icc, 2.2f);
    }
    float tx = icon >= 0 ? x + 60 : x, tw = card.X + card.Width - 28 - tx;
    Title(g, s.title, RectF(tx, y - 2, tw, 30), 19, kInk);
    Para(g, s.text, RectF(tx, y + 34, tw, card.Y + card.Height - 84 - (y + 34)), 13.5f, TH(grey));
    for (size_t i = 0; i < s.buttons.size(); i++) {
        const SbButton &b = s.buttons[i];
        bool def = (int)i == s.focus, hot = (int)i == s.hot;
        GraphicsPath p; RoundRect(p, b.r, 14);
        if (def) { SolidBrush f(kAcc); g.FillPath(&f, &p); if (hot) { SolidBrush hb(Color(60, 255, 255, 255)); g.FillPath(&hb, &p); } }
        else { SolidBrush f(hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&f, &p); Pen pn(TH(fieldBorder), 1.0f); g.DrawPath(&pn, &p); }
        Text(g, b.label, b.r, 13.5f, FontStyleBold, def ? kOnAcc : kInk);
    }
}
static void SbRender(SbState &s)
{
    int W = (int)s.w, H = (int)s.h;
    memset(s.bits, 0, (size_t)W * H * 4);
    {
        Bitmap frame(W, H, W * 4, PixelFormat32bppPARGB, (BYTE *)s.bits);
        Graphics g(&frame);
        SbDraw(g, s);
    }
    BLENDFUNCTION bf = { AC_SRC_OVER, 0, 255, AC_SRC_ALPHA };
    RECT wr; GetWindowRect(s.wnd, &wr);
    POINT dst = { wr.left, wr.top }, src = { 0, 0 };
    SIZE sz = { W, H };
    HDC screen = GetDC(NULL);
    GdiFlush();
    UpdateLayeredWindow(s.wnd, screen, &dst, &sz, s.dc, &src, 0, &bf, ULW_ALPHA);
    ReleaseDC(NULL, screen);
}

static int SbButtonAt(SbState &s, int px, int py)
{
    float x = px / s.scale, y = py / s.scale;
    for (size_t i = 0; i < s.buttons.size(); i++) if (s.buttons[i].r.Contains(x, y)) return (int)i;
    return -1;
}
// Echap : Annuler, sinon Non, sinon OK
static int SbEscape(SbState &s)
{
    for (auto &b : s.buttons) if (b.id == IDCANCEL) return IDCANCEL;
    for (auto &b : s.buttons) if (b.id == IDNO) return IDNO;
    return s.buttons.empty() ? IDOK : s.buttons[0].id;
}

static LRESULT CALLBACK SbProc(HWND h, UINT m, WPARAM wp, LPARAM lp)
{
    SbState *s = g_sb;
    if (!s) return DefWindowProcW(h, m, wp, lp);
    switch (m) {
    case WM_NCHITTEST: {   // la carte se deplace en la glissant, sauf sur les boutons
        POINT p = { GET_X_LPARAM(lp), GET_Y_LPARAM(lp) }; ScreenToClient(h, &p);
        if (SbButtonAt(*s, p.x, p.y) >= 0) return HTCLIENT;
        float x = p.x / s->scale, y = p.y / s->scale;
        return x >= s->cardX && y >= s->cardY && x <= s->cardX + s->cardW && y <= s->cardY + s->cardH ? HTCAPTION : HTCLIENT;
    }
    case WM_MOUSEMOVE: {
        int hot = SbButtonAt(*s, GET_X_LPARAM(lp), GET_Y_LPARAM(lp));
        if (hot != s->hot) { s->hot = hot; if (hot >= 0) s->focus = hot; SbRender(*s); }
        TRACKMOUSEEVENT tme = { sizeof(tme), TME_LEAVE, h, 0 }; TrackMouseEvent(&tme);
        return 0;
    }
    case WM_MOUSELEAVE: if (s->hot >= 0) { s->hot = -1; SbRender(*s); } return 0;
    case WM_SETCURSOR: SetCursor(LoadCursor(NULL, s->hot >= 0 ? IDC_HAND : IDC_ARROW)); return TRUE;
    case WM_LBUTTONUP: { int b = SbButtonAt(*s, GET_X_LPARAM(lp), GET_Y_LPARAM(lp)); if (b >= 0) s->result = s->buttons[b].id; return 0; }
    case WM_KEYDOWN:
        if (wp == VK_RETURN || wp == VK_SPACE) s->result = s->buttons[s->focus].id;
        else if (wp == VK_ESCAPE) s->result = SbEscape(*s);
        else if (wp == VK_LEFT || (wp == VK_TAB && (GetKeyState(VK_SHIFT) & 0x8000))) { s->focus = (s->focus + (int)s->buttons.size() - 1) % (int)s->buttons.size(); SbRender(*s); }
        else if (wp == VK_RIGHT || wp == VK_TAB) { s->focus = (s->focus + 1) % (int)s->buttons.size(); SbRender(*s); }
        return 0;
    case WM_CLOSE: s->result = SbEscape(*s); return 0;
    }
    return DefWindowProcW(h, m, wp, lp);
}

// Contenu et mise en page (points) : titre, texte, boutons, taille de la carte.
static void SbLayout(SbState &s, LPCWSTR text, LPCWSTR caption, UINT type, float scale)
{
    s.title = caption && *caption ? caption : L"MWCoop";
    s.text = text ? text : L"";
    s.type = type;
    s.hot = -1; s.result = 0;
    s.scale = scale;
    UINT bt = type & MB_TYPEMASK;
    auto add = [&](int id, const wchar_t *fr, const wchar_t *en) { s.buttons.push_back({ id, T(fr, en), RectF() }); };
    // ordre d'affichage, de gauche a droite : l'action principale a droite
    if (bt == MB_YESNOCANCEL) { add(IDCANCEL, L"Annuler", L"Cancel"); add(IDNO, L"Non", L"No"); add(IDYES, L"Oui", L"Yes"); }
    else if (bt == MB_YESNO) { add(IDNO, L"Non", L"No"); add(IDYES, L"Oui", L"Yes"); }
    else if (bt == MB_OKCANCEL) { add(IDCANCEL, L"Annuler", L"Cancel"); add(IDOK, L"OK", L"OK"); }
    else if (bt == MB_RETRYCANCEL) { add(IDCANCEL, L"Annuler", L"Cancel"); add(IDRETRY, L"Réessayer", L"Retry"); }
    else add(IDOK, L"OK", L"OK");
    // bouton par defaut (MB_DEFBUTTONn : n-ieme dans l'ordre de Windows -- Oui, Non, Annuler / OK, Annuler)
    int defN = (type & MB_DEFMASK) == MB_DEFBUTTON2 ? 1 : (type & MB_DEFMASK) == MB_DEFBUTTON3 ? 2 : 0;
    std::vector<int> winOrder;
    if (bt == MB_YESNOCANCEL) winOrder = { IDYES, IDNO, IDCANCEL };
    else if (bt == MB_YESNO) winOrder = { IDYES, IDNO };
    else if (bt == MB_OKCANCEL) winOrder = { IDOK, IDCANCEL };
    else if (bt == MB_RETRYCANCEL) winOrder = { IDRETRY, IDCANCEL };
    else winOrder = { IDOK };
    int defId = winOrder[min((size_t)defN, winOrder.size() - 1)];
    s.focus = 0;
    for (size_t i = 0; i < s.buttons.size(); i++) if (s.buttons[i].id == defId) s.focus = (int)i;
    // mise en page (points) : largeur fixe, hauteur selon le texte
    UINT ic = type & MB_ICONMASK;
    s.cardW = 540;
    float textW = s.cardW - 56 - (ic ? 60 : 0);
    Graphics &mg = MeasureG();
    RectF box;
    {
        Font &font = *FontOf(L"Segoe UI", 13.5f, FontStyleRegular);
        StringFormat sf;
        mg.MeasureString(s.text.c_str(), -1, &font, RectF(0, 0, textW, 2000), &sf, &box);
    }
    s.cardH = min(26.0f + 34 + box.Height + 22 + 46 + 24, 700.0f);
    s.cardX = kSbShadow; s.cardY = kSbShadow;
    s.w = (s.cardW + 2 * kSbShadow) * s.scale; s.h = (s.cardH + 2 * kSbShadow) * s.scale;
    float bx = s.cardX + s.cardW - 24, by = s.cardY + s.cardH - 24 - 42;
    for (int i = (int)s.buttons.size() - 1; i >= 0; i--) {
        float bw = max(104.0f, MeasureW(mg, s.buttons[i].label, 13.5f, FontStyleBold) + 40);
        bx -= bw;
        s.buttons[i].r = RectF(bx, by, bw, 42);
        bx -= 10;
    }
}

// Essais (/boite <png> <ok|ouinon|ouinonannuler> <info|question|attention|erreur> [/theme ...] [/lang ...]) : la fenetre en image.
static Bitmap *StyledBoxCapture(LPCWSTR text, LPCWSTR caption, UINT type)
{
    SbState s = {};
    SbLayout(s, text, caption, type, 1.5f);
    Bitmap *b = new Bitmap((INT)s.w, (INT)s.h, PixelFormat32bppARGB);
    Graphics g(b);
    g.Clear(Color(255, 40, 44, 52));   // (gris neutre : le bureau derriere)
    SbDraw(g, s);
    return b;
}

static int StyledBox(HWND owner, LPCWSTR text, LPCWSTR caption, UINT type)
{
    if (!g_gdiplusUp || g_sb) return MessageBoxExW(owner, text, caption, type, 0);   // (avant GDI+, ou deja une ouverte)
    SbState s = {};
    HDC sdc = GetDC(NULL);
    float scale = GetDeviceCaps(sdc, LOGPIXELSX) / 96.0f;
    ReleaseDC(NULL, sdc);
    SbLayout(s, text, caption, type, scale);
    // fenetre : centree sur la proprietaire (ou l'ecran)
    static bool reg;
    if (!reg) {
        WNDCLASSW wc = {};
        wc.lpfnWndProc = SbProc; wc.hInstance = GetModuleHandleW(NULL); wc.lpszClassName = L"MWCoopMessage";
        wc.hCursor = LoadCursor(NULL, IDC_ARROW);
        wc.hIcon = LoadIconW(GetModuleHandleW(NULL), MAKEINTRESOURCEW(1));
        RegisterClassW(&wc);
        reg = true;
    }
    RECT area;
    if (owner && IsWindowVisible(owner)) GetWindowRect(owner, &area);
    else SystemParametersInfoW(SPI_GETWORKAREA, 0, &area, 0);
    int W = (int)s.w, H = (int)s.h;
    int x0 = area.left + (area.right - area.left - W) / 2, y0 = area.top + (area.bottom - area.top - H) / 2;
    s.wnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOPMOST | (owner ? WS_EX_TOOLWINDOW : WS_EX_APPWINDOW), L"MWCoopMessage", s.title.c_str(), WS_POPUP,
                            x0, y0, W, H, owner, NULL, GetModuleHandleW(NULL), NULL);
    if (!s.wnd) return MessageBoxExW(owner, text, caption, type, 0);
    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(bi.bmiHeader); bi.bmiHeader.biWidth = W; bi.bmiHeader.biHeight = -H;
    bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32; bi.bmiHeader.biCompression = BI_RGB;
    s.dc = CreateCompatibleDC(NULL);
    s.dib = CreateDIBSection(s.dc, &bi, DIB_RGB_COLORS, &s.bits, NULL, 0);
    HGDIOBJ old = SelectObject(s.dc, s.dib);
    g_sb = &s;
    SbRender(s);
    bool ownerOn = owner && IsWindowEnabled(owner);
    if (ownerOn) EnableWindow(owner, FALSE);
    ShowWindow(s.wnd, SW_SHOW);
    SetForegroundWindow(s.wnd);
    SetFocus(s.wnd);
    if (type & MB_ICONERROR) MessageBeep(MB_ICONERROR); else if (type & MB_ICONWARNING) MessageBeep(MB_ICONWARNING);
    MSG msg;
    while (!s.result && GetMessageW(&msg, NULL, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    if (!s.result) { s.result = SbEscape(s); PostQuitMessage((int)msg.wParam); }   // (WM_QUIT pendant la question : rendu a la boucle principale)
    if (ownerOn) EnableWindow(owner, TRUE);
    g_sb = NULL;
    DestroyWindow(s.wnd);
    SelectObject(s.dc, old); DeleteObject(s.dib); DeleteDC(s.dc);
    if (owner) SetForegroundWindow(owner);
    return s.result;
}
