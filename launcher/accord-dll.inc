// (inclus par launcher.cpp, juste avant modsync.inc : ACCORD AVANT DE RECEVOIR LES DLL D'UN HOTE -- decision de JD, 04/11)
//
// JD (04/11) : « dans le popup il y ai la liste exacte des .dll pour que le joueur puisse voir ce qu'il reçoit avant d'accepter ».
// Spec : MWCoop-spec-confirmation-dll.md. Ici :
//  - ce qui est un fichier de programme (ProgKind) : .dll, a accepter ; .exe, .bat... : jamais ecrits, aucun vrai mod n'en a ;
//  - les hotes de confiance : compte Steam + DLL acceptees (SHA-256 calcule sur ce PC : l'empreinte FNV-1a de la liste, c'est
//    l'hote qui la donne), dans %LOCALAPPDATA%\MWCoop\hotes-confiance.txt, jamais repris d'un autre reglage ;
//  - la fenetre, au style de dialog.inc (carte, ombre, memes couleurs), avec la liste : chaque chemin en entier, coupe en
//    plusieurs lignes s'il le faut, jamais de « ... » ni de « et N autres ». Refuser est le bouton par defaut : Entree, Echap
//    et la fermeture refusent ; Accepter ne repond qu'apres une seconde (une fenetre qui surgit sous un clic ou sous Entree).

#include <functional>
#include "cle.inc"
static std::wstring SizeText(uint64_t b);   // (modsync.inc)
static std::wstring FitText(const std::wstring &s, float px, int style, float maxW);   // (launcher.cpp)

// ---- fichiers de programme
enum { PROG_NONE, PROG_DLL, PROG_INTERDIT };
// Sur un chemin deja normalise (SyncPathGuest : plus de point ni d'espace de fin, Windows les ignore).
static int ProgKind(const std::string &p)
{
    size_t dot = p.rfind('.'), sl = p.rfind('\\');
    if (dot == std::string::npos || (sl != std::string::npos && dot < sl)) return PROG_NONE;
    const char *e = p.c_str() + dot;
    if (!_stricmp(e, ".dll")) return PROG_DLL;
    for (const char *k : { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".scr", ".com", ".pif", ".lnk", ".reg", ".msi", ".msp", ".sys", ".ocx", ".cpl", ".config" })
        if (!_stricmp(e, k)) return PROG_INTERDIT;
    return PROG_NONE;
}
// Cle d'un chemin : la casse ne compte pas (x.DLL et x.dll sont le meme fichier).
static std::string ProgKey(const std::string &p)
{
    std::wstring w = Widen(p, CP_UTF8);
    if (!w.empty()) CharLowerBuffW(&w[0], (DWORD)w.size());
    return Narrow(w, CP_UTF8);
}
// Un texte de l'hote a l'ecran : ce qui est invisible ou retourne le texte (U+202E : « exe.lld » lu « dll.exe ») est montre, pas applique.
static std::wstring ShowSafe(const std::string &utf8)
{
    std::wstring in = Widen(utf8, CP_UTF8), out;
    for (wchar_t c : in) {
        bool hidden = c < 32 || (c >= 0x7F && c <= 0x9F) || c == 0xAD || c == 0x34F || c == 0x61C || c == 0x180E || (c >= 0x200B && c <= 0x200F) || (c >= 0x2028 && c <= 0x202E) || (c >= 0x2060 && c <= 0x206F) || c == 0xFEFF || (c >= 0xFFF0 && c <= 0xFFFB) || c == 0xDB40;
        if (hidden) { wchar_t b[16]; swprintf_s(b, L"[U+%04X]", (unsigned)c); out += b; }
        else out += c;
    }
    return out;
}

// ---- hotes de confiance
struct TrustFile { int game; std::string sha, path; uint32_t size; uint64_t fnv; };
struct TrustHost { uint64_t id; std::string name; std::vector<TrustFile> files; };
static std::mutex g_trustMx;
static std::wstring TrustPath() { std::wstring l = LocalDir(); return l.empty() ? L"" : l + L"hotes-confiance.txt"; }
// Une ligne par element, separee par des tabulations : « H id nom », puis ses fichiers « F jeu sha256 taille fnv chemin ».
static std::vector<TrustHost> TrustLoad()
{
    std::lock_guard<std::mutex> lk(g_trustMx);
    std::vector<TrustHost> v;
    std::vector<unsigned char> d;
    if (TrustPath().empty() || !ReadAll(TrustPath(), d)) return v;
    std::string all(d.begin(), d.end());
    for (size_t a = 0; a < all.size();) {
        size_t e = all.find('\n', a);
        std::string l = all.substr(a, e == std::string::npos ? std::string::npos : e - a);
        a = e == std::string::npos ? all.size() : e + 1;
        if (!l.empty() && l.back() == '\r') l.pop_back();
        std::vector<std::string> c;
        for (size_t x = 0;;) { size_t t = l.find('\t', x); c.push_back(l.substr(x, t == std::string::npos ? std::string::npos : t - x)); if (t == std::string::npos) break; x = t + 1; }
        if (c[0] == "H" && c.size() >= 3) { TrustHost h; h.id = _strtoui64(c[1].c_str(), NULL, 10); h.name = c[2]; if (h.id) v.push_back(h); }
        else if (c[0] == "F" && c.size() >= 6 && !v.empty()) {
            TrustFile f;
            f.game = atoi(c[1].c_str()); f.sha = c[2]; f.size = (uint32_t)strtoul(c[3].c_str(), NULL, 10); f.fnv = _strtoui64(c[4].c_str(), NULL, 16); f.path = c[5];
            v.back().files.push_back(f);
        }
    }
    return v;
}
static bool TrustSave(const std::vector<TrustHost> &v)
{
    std::lock_guard<std::mutex> lk(g_trustMx);
    std::wstring p = TrustPath();
    if (p.empty()) return false;
    SHCreateDirectoryExW(NULL, LocalDir().c_str(), NULL);
    std::string o;
    for (auto &h : v) {
        std::string nm = h.name;
        for (char &c : nm) if ((unsigned char)c < 32) c = ' ';
        o += "H\t" + std::to_string((unsigned long long)h.id) + "\t" + nm + "\n";
        for (auto &f : h.files) { char b[32]; sprintf_s(b, "%016llx", (unsigned long long)f.fnv); o += "F\t" + std::to_string(f.game) + "\t" + f.sha + "\t" + std::to_string(f.size) + "\t" + b + "\t" + f.path + "\n"; }
    }
    std::wstring tmp = p + L".tmp";
    HANDLE h = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    if (h == INVALID_HANDLE_VALUE) return false;
    DWORD w = 0;
    bool ok = WriteFile(h, o.data(), (DWORD)o.size(), &w, NULL) && w == o.size();
    CloseHandle(h);
    if (ok) ok = MoveFileExW(tmp.c_str(), p.c_str(), MOVEFILE_REPLACE_EXISTING) != 0;
    if (!ok) DeleteFileW(tmp.c_str());
    return ok;
}
// Ce qu'on sait de cet hote pour ce jeu : false s'il n'est pas de confiance.
static bool TrustFilesOf(uint64_t id, int game, std::vector<TrustFile> &out)
{
    for (auto &h : TrustLoad()) if (h.id == id) {
        for (auto &f : h.files) if (f.game == game) out.push_back(f);
        return true;
    }
    return false;
}
// Remplace ce qu'on sait de cet hote pour ce jeu (l'autre jeu garde sa liste).
static bool TrustRemember(uint64_t id, const std::string &name, int game, const std::vector<TrustFile> &files)
{
    std::vector<TrustHost> v = TrustLoad();
    TrustHost *t = NULL;
    for (auto &h : v) if (h.id == id) t = &h;
    if (!t) { TrustHost n; n.id = id; v.push_back(n); t = &v.back(); }
    t->name = name;
    t->files.erase(std::remove_if(t->files.begin(), t->files.end(), [&](const TrustFile &f) { return f.game == game; }), t->files.end());
    for (TrustFile f : files) { f.game = game; t->files.push_back(f); }
    return TrustSave(v);
}

// ---- la fenetre
enum { DB_REFUSE = 1, DB_ACCEPT, DB_ALWAYS, DB_CLOSED, DB_REMOVE, DB_DONE };   // (DB_CLOSED : plus d'actualite, sans reponse du joueur)
struct DbRow { std::wstring path, size, chip; Color chipCol; float y = 0, h = 0; };
struct DbBtn { int id; std::wstring label; RectF r; bool arm; };
struct DbState {
    std::wstring title, who, warn, sum;
    bool warnOn = true, select = false;
    std::vector<DbRow> rows;
    std::vector<DbBtn> btns;
    int focus = 0, hot = -1, down = -1, sel = -1, result = 0;
    float scale = 1, w = 0, h = 0, cardX = 0, cardY = 0, cardW = 0, cardH = 0;
    RectF list, bar;
    float scroll = 0, content = 0;
    bool drag = false; float dragY = 0, dragScroll = 0;
    DWORD armAt = 0;
    bool (*stale)(int) = NULL; int staleArg = 0;
    HWND wnd = NULL; HDC dc = NULL; HBITMAP dib = NULL; void *bits = NULL; HGDIOBJ old = NULL;
};
static DbState *g_db;
static const float kDbShadow = 24, kDbRowPx = 12.5f;

static bool DbArmed(const DbState &s, const DbBtn &b) { return !b.arm || GetTickCount() - s.armAt < 0x80000000u; }
static float DbMaxScroll(const DbState &s) { return max(0.0f, s.content - s.list.Height); }
static RectF DbThumb(const DbState &s)
{
    float th = max(28.0f, s.bar.Height * s.bar.Height / max(1.0f, s.content));
    float my = DbMaxScroll(s);
    return RectF(s.bar.X, s.bar.Y + (my > 0 ? (s.bar.Height - th) * s.scroll / my : 0), s.bar.Width, th);
}
static void DbDraw(Graphics &g, DbState &s)
{
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);
    g.ScaleTransform(s.scale, s.scale);
    RectF card(s.cardX, s.cardY, s.cardW, s.cardH);
    for (int i = 6; i >= 1; i--) {
        GraphicsPath sp; RoundRect(sp, RectF(card.X - i * 2.0f, card.Y - i * 2.0f + 6, card.Width + i * 4.0f, card.Height + i * 4.0f), 22.0f + i * 2);
        SolidBrush sb(Color((BYTE)(10 + (6 - i) * 6), 0, 0, 0)); g.FillPath(&sb, &sp);
    }
    GraphicsPath cp; RoundRect(cp, card, 22);
    LinearGradientBrush bg(card, Mix(TH(fallB), TH(solid), 0.5f), TH(fallA), LinearGradientModeVertical);
    g.FillPath(&bg, &cp);
    SolidBrush tint(TH(panel)); g.FillPath(&tint, &cp);
    Pen edge(TH(panelBorder), 1.2f); g.DrawPath(&edge, &cp);
    float x = card.X + 28, y = card.Y + 24, right = card.X + card.Width - 28;
    Color ic = s.warnOn ? kWarn : kAcc;
    SolidBrush icb(WithA(ic, 0.16f)); g.FillEllipse(&icb, x, y, 44.0f, 44.0f);
    Pen icp(WithA(ic, 0.55f), 1.2f); g.DrawEllipse(&icp, x, y, 44.0f, 44.0f);
    Icon(g, s.warnOn ? IC_ALERT : IC_SPARK, x + 22, y + 22, 22, ic, 2.2f);
    Title(g, s.title, RectF(x + 58, y - 2, right - x - 58, 28), 18, kInk);
    Text(g, s.who, RectF(x + 58, y + 25, right - x - 58, 20), 12.5f, FontStyleRegular, TH(grey), StringAlignmentNear);
    y += 58;
    if (s.warnOn) {
        RectF wr(x, y, right - x, 54);
        GraphicsPath wp; RoundRect(wp, wr, 12);
        SolidBrush wf(WithA(kRed, 0.14f)); g.FillPath(&wf, &wp);
        Pen wpn(WithA(kRed, 0.55f), 1.0f); g.DrawPath(&wpn, &wp);
        Para(g, s.warn, RectF(wr.X + 14, wr.Y + 4, wr.Width - 28, wr.Height - 8), 13, kInk, StringAlignmentCenter);
        y += 62;
    }
    if (!s.sum.empty()) Text(g, s.sum, RectF(x, y, right - x, 20), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
    // la liste
    {
        GraphicsPath lp; RoundRect(lp, s.list, 12);
        SolidBrush lf(TH(field)); g.FillPath(&lf, &lp);
        Pen lb(TH(fieldBorder), 1.0f); g.DrawPath(&lb, &lp);
        GraphicsState st = g.Save();
        g.SetClip(s.list);
        Font &font = *FontOf(L"Segoe UI", kDbRowPx, FontStyleRegular);
        for (size_t i = 0; i < s.rows.size(); i++) {
            const DbRow &r = s.rows[i];
            float ry = s.list.Y + r.y - s.scroll;
            if (ry > s.list.Y + s.list.Height || ry + r.h < s.list.Y) continue;
            if (s.select && (int)i == s.sel) { SolidBrush sb(WithA(kAcc, 0.22f)); g.FillRectangle(&sb, s.list.X + 2, ry, s.list.Width - 4, r.h); }
            if (i) { Pen sep(WithA(kInk, 0.08f), 1.0f); g.DrawLine(&sep, s.list.X + 10, ry, s.list.X + s.list.Width - 10, ry); }
            StringFormat sf;   // (jamais coupe : la ligne a la hauteur du chemin entier)
            sf.SetTrimming(StringTrimmingNone);
            SolidBrush tb(kInk);
            RectF pr(s.list.X + 12, ry + 6, s.list.Width - 12 - 212, r.h - 8);
            DrawStr(g, r.path.c_str(), -1, &font, pr, &sf, &tb);
            float cw = r.chip.empty() ? 0 : PillW(r.chip) + 6;
            float cx = s.list.X + s.list.Width - 18 - 118 - cw + 62;
            if (!r.chip.empty()) {
                RectF cr(cx, ry + 6, cw, 20);
                GraphicsPath chp; RoundRect(chp, cr, 10);
                SolidBrush cf(WithA(r.chipCol, 0.18f)); g.FillPath(&cf, &chp);
                Text(g, r.chip, cr, 11, FontStyleBold, r.chipCol);
            }
            Text(g, r.size, RectF(s.list.X + s.list.Width - 18 - 54, ry + 6, 44, 20), 11.5f, FontStyleRegular, TH(grey), StringAlignmentFar);
        }
        g.Restore(st);
        if (DbMaxScroll(s) > 0) {
            RectF t = DbThumb(s);
            GraphicsPath tp; RoundRect(tp, t, 4);
            SolidBrush tf(WithA(kInk, s.drag ? 0.55f : 0.32f)); g.FillPath(&tf, &tp);
        }
    }
    for (size_t i = 0; i < s.btns.size(); i++) {
        const DbBtn &b = s.btns[i];
        bool def = (int)i == s.focus, hot = (int)i == s.hot, on = DbArmed(s, b) && !(b.id == DB_REMOVE && s.sel < 0);
        GraphicsPath p; RoundRect(p, b.r, 14);
        if (def) { SolidBrush f(on ? kAcc : WithA(kAcc, 0.45f)); g.FillPath(&f, &p); if (hot && on) { SolidBrush hb(Color(60, 255, 255, 255)); g.FillPath(&hb, &p); } }
        else { SolidBrush f(hot && on ? TH(btn2Hot) : TH(btn2)); g.FillPath(&f, &p); Pen pn(TH(fieldBorder), 1.0f); g.DrawPath(&pn, &p); }
        Text(g, b.label, b.r, 13.5f, FontStyleBold, def ? kOnAcc : on ? kInk : TH(grey));
    }
}
static void DbRender(DbState &s)
{
    if (!s.wnd) return;
    int W = (int)s.w, H = (int)s.h;
    memset(s.bits, 0, (size_t)W * H * 4);
    {
        Bitmap frame(W, H, W * 4, PixelFormat32bppPARGB, (BYTE *)s.bits);
        Graphics g(&frame);
        DbDraw(g, s);
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
static int DbBtnAt(DbState &s, float x, float y) { for (size_t i = 0; i < s.btns.size(); i++) if (s.btns[i].r.Contains(x, y)) return (int)i; return -1; }
static void DbPress(DbState &s, int i)
{
    if (i < 0 || i >= (int)s.btns.size() || !DbArmed(s, s.btns[i])) return;
    if (s.btns[i].id == DB_REMOVE && s.sel < 0) return;
    s.result = s.btns[i].id;
}
static void DbScrollTo(DbState &s, float v) { s.scroll = max(0.0f, min(v, DbMaxScroll(s))); DbRender(s); }
static LRESULT CALLBACK DbProc(HWND h, UINT m, WPARAM wp, LPARAM lp)
{
    DbState *s = g_db;
    if (!s) return DefWindowProcW(h, m, wp, lp);
    float mx = GET_X_LPARAM(lp) / s->scale, my = GET_Y_LPARAM(lp) / s->scale;
    switch (m) {
    case WM_NCHITTEST: {   // la carte se deplace en la glissant, sauf sur les boutons et la liste
        POINT p = { GET_X_LPARAM(lp), GET_Y_LPARAM(lp) }; ScreenToClient(h, &p);
        float x = p.x / s->scale, y = p.y / s->scale;
        if (DbBtnAt(*s, x, y) >= 0 || s->list.Contains(x, y)) return HTCLIENT;
        return x >= s->cardX && y >= s->cardY && x <= s->cardX + s->cardW && y <= s->cardY + s->cardH ? HTCAPTION : HTCLIENT;
    }
    case WM_MOUSEMOVE: {
        if (s->drag) {
            RectF t = DbThumb(*s);
            float range = s->bar.Height - t.Height;
            if (range > 0) DbScrollTo(*s, s->dragScroll + (my - s->dragY) * DbMaxScroll(*s) / range);
            return 0;
        }
        int hot = DbBtnAt(*s, mx, my);
        if (hot != s->hot) { s->hot = hot; DbRender(*s); }   // (le survol ne change pas le bouton par defaut : Entree reste Refuser)
        TRACKMOUSEEVENT tme = { sizeof(tme), TME_LEAVE, h, 0 }; TrackMouseEvent(&tme);
        return 0;
    }
    case WM_MOUSELEAVE: if (s->hot >= 0) { s->hot = -1; DbRender(*s); } return 0;
    case WM_SETCURSOR: SetCursor(LoadCursor(NULL, s->hot >= 0 ? IDC_HAND : IDC_ARROW)); return TRUE;
    case WM_LBUTTONDOWN:
        s->down = DbBtnAt(*s, mx, my);
        if (s->down < 0 && DbMaxScroll(*s) > 0 && mx >= s->bar.X - 4 && mx <= s->bar.X + s->bar.Width + 4 && my >= s->bar.Y && my <= s->bar.Y + s->bar.Height) {
            RectF t = DbThumb(*s);
            if (!t.Contains(s->bar.X, my)) DbScrollTo(*s, s->scroll + (my < t.Y ? -s->list.Height : s->list.Height));
            s->drag = true; s->dragY = my; s->dragScroll = s->scroll; SetCapture(h);
            DbRender(*s);
        } else if (s->down < 0 && s->select && s->list.Contains(mx, my)) {
            float ly = my - s->list.Y + s->scroll;
            for (size_t i = 0; i < s->rows.size(); i++) if (ly >= s->rows[i].y && ly < s->rows[i].y + s->rows[i].h) s->sel = (int)i;
            DbRender(*s);
        }
        return 0;
    case WM_LBUTTONUP: {
        if (s->drag) { s->drag = false; ReleaseCapture(); DbRender(*s); return 0; }
        int b = DbBtnAt(*s, mx, my);
        if (b >= 0 && b == s->down) DbPress(*s, b);   // (appuye ET relache sur le meme bouton)
        s->down = -1;
        return 0;
    }
    case WM_MOUSEWHEEL: DbScrollTo(*s, s->scroll - GET_WHEEL_DELTA_WPARAM(wp) / 120.0f * 60); return 0;
    case WM_KEYDOWN:
        if (wp == VK_RETURN || wp == VK_SPACE) DbPress(*s, s->focus);
        else if (wp == VK_ESCAPE) s->result = s->select ? DB_DONE : DB_REFUSE;
        else if (wp == VK_LEFT || (wp == VK_TAB && (GetKeyState(VK_SHIFT) & 0x8000))) { s->focus = (s->focus + (int)s->btns.size() - 1) % (int)s->btns.size(); DbRender(*s); }
        else if (wp == VK_RIGHT || wp == VK_TAB) { s->focus = (s->focus + 1) % (int)s->btns.size(); DbRender(*s); }
        else if (wp == VK_UP) DbScrollTo(*s, s->scroll - 40);
        else if (wp == VK_DOWN) DbScrollTo(*s, s->scroll + 40);
        else if (wp == VK_PRIOR) DbScrollTo(*s, s->scroll - s->list.Height);
        else if (wp == VK_NEXT) DbScrollTo(*s, s->scroll + s->list.Height);
        else if (wp == VK_HOME) DbScrollTo(*s, 0);
        else if (wp == VK_END) DbScrollTo(*s, DbMaxScroll(*s));
        return 0;
    case WM_TIMER:
        if (s->stale && s->stale(s->staleArg)) s->result = DB_CLOSED;
        else DbRender(*s);   // (Accepter s'allume)
        return 0;
    case WM_CLOSE: s->result = s->select ? DB_DONE : DB_REFUSE; return 0;
    }
    return DefWindowProcW(h, m, wp, lp);
}
// Hauteur des lignes, boutons : la mise en page (points). Refuser (ou Fermer) a droite et choisi d'office.
static void DbLayout(DbState &s, float scale, float maxH)
{
    s.scale = scale;
    s.cardW = 780;
    s.cardH = max(420.0f, min(maxH - 2 * kDbShadow, 660.0f));
    s.cardX = s.cardY = kDbShadow;
    s.w = (s.cardW + 2 * kDbShadow) * scale; s.h = (s.cardH + 2 * kDbShadow) * scale;
    float x = s.cardX + 28, right = s.cardX + s.cardW - 28, top = s.cardY + 24 + 58 + (s.warnOn ? 62 : 0) + (s.sum.empty() ? 0 : 26);
    float by = s.cardY + s.cardH - 22 - 42;
    s.list = RectF(x, top, right - x, by - 14 - top);
    s.bar = RectF(s.list.X + s.list.Width - 9, s.list.Y + 6, 6, s.list.Height - 12);
    Graphics &mg = MeasureG();
    Font &font = *FontOf(L"Segoe UI", kDbRowPx, FontStyleRegular);
    float cy = 0;
    for (auto &r : s.rows) {
        RectF box;
        StringFormat sf;
        sf.SetTrimming(StringTrimmingNone);
        MeasureStr(mg, r.path.c_str(), -1, &font, RectF(0, 0, s.list.Width - 12 - 212, 2000), &sf, &box);
        r.y = cy;
        r.h = max(32.0f, box.Height + 14);
        cy += r.h;
    }
    s.content = cy + 2;
    float bx = right;
    for (int i = (int)s.btns.size() - 1; i >= 0; i--) {
        float bw = max(104.0f, MeasureW(mg, s.btns[i].label, 13.5f, FontStyleBold) + 40);
        bx -= bw;
        s.btns[i].r = RectF(bx, by, bw, 42);
        bx -= 10;
    }
    s.focus = (int)s.btns.size() - 1;
}
static bool DbOpen(DbState &s, HWND owner, bool hidden)
{
    static bool reg;
    if (!reg) {
        WNDCLASSW wc = {};
        wc.lpfnWndProc = DbProc; wc.hInstance = GetModuleHandleW(NULL); wc.lpszClassName = L"MWCoopAccord";
        wc.hCursor = LoadCursor(NULL, IDC_ARROW);
        wc.hIcon = LoadIconW(GetModuleHandleW(NULL), MAKEINTRESOURCEW(1));
        RegisterClassW(&wc);
        reg = true;
    }
    HDC sdc = GetDC(NULL);
    float scale = GetDeviceCaps(sdc, LOGPIXELSX) / 96.0f;
    ReleaseDC(NULL, sdc);
    RECT area, work;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    DbLayout(s, scale, (work.bottom - work.top) / scale);
    if (owner && IsWindowVisible(owner) && !IsIconic(owner)) GetWindowRect(owner, &area); else area = work;
    int W = (int)s.w, H = (int)s.h;
    int x0 = area.left + (area.right - area.left - W) / 2, y0 = area.top + (area.bottom - area.top - H) / 2;
    x0 = max((int)work.left, min(x0, (int)work.right - W)); y0 = max((int)work.top, min(y0, (int)work.bottom - H));
    s.wnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOPMOST | (owner ? WS_EX_TOOLWINDOW : WS_EX_APPWINDOW), L"MWCoopAccord", s.title.c_str(), WS_POPUP, x0, y0, W, H, owner, NULL, GetModuleHandleW(NULL), NULL);
    if (!s.wnd) return false;
    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(bi.bmiHeader); bi.bmiHeader.biWidth = W; bi.bmiHeader.biHeight = -H;
    bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32; bi.bmiHeader.biCompression = BI_RGB;
    s.dc = CreateCompatibleDC(NULL);
    s.dib = CreateDIBSection(s.dc, &bi, DIB_RGB_COLORS, &s.bits, NULL, 0);
    s.old = SelectObject(s.dc, s.dib);
    s.armAt = GetTickCount() + 1000;
    g_db = &s;
    DbRender(s);
    SetTimer(s.wnd, 1, 150, NULL);
    if (!hidden) {
        ShowWindow(s.wnd, SW_SHOW);
        SetForegroundWindow(s.wnd);
        SetFocus(s.wnd);
        if (owner && IsIconic(owner)) FlashWindow(owner, TRUE);
        MessageBeep(MB_ICONWARNING);
    }
    return true;
}
static void DbClose(DbState &s)
{
    g_db = NULL;
    if (s.wnd) { KillTimer(s.wnd, 1); DestroyWindow(s.wnd); s.wnd = NULL; }
    if (s.dc) { SelectObject(s.dc, s.old); DeleteObject(s.dib); DeleteDC(s.dc); s.dc = NULL; }
}
// Modale : la fenetre proprietaire est desactivee le temps de la reponse, le reste du lanceur tourne (minuteries). Rend DB_*.
static int DbRun(DbState &s, HWND owner)
{
    if (g_db || !g_gdiplusUp || !DbOpen(s, owner, false)) return DB_REFUSE;   // (rien a montrer : personne n'a dit oui)
    bool ownerOn = owner && IsWindowEnabled(owner);
    if (ownerOn) EnableWindow(owner, FALSE);
    MSG msg;
    while (!s.result && GetMessageW(&msg, NULL, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    if (!s.result) { s.result = s.select ? DB_DONE : DB_REFUSE; PostQuitMessage((int)msg.wParam); }   // (WM_QUIT pendant la question : rendu a la boucle principale)
    if (ownerOn) EnableWindow(owner, TRUE);
    DbClose(s);
    if (owner) SetForegroundWindow(owner);
    return s.result;
}

// Une ligne de la liste a partir d'un chemin de l'offre ; state : 0 nouveau, 1 modifie, 2 deja identique, 3 refuse d'office.
static DbRow DbFileRow(const std::string &path, uint32_t size, int state)
{
    DbRow r;
    r.path = ShowSafe(path);
    r.size = SizeText(size);
    r.chip = state == 0 ? T(L"nouveau", L"new") : state == 1 ? T(L"modifié", L"changed") : state == 2 ? T(L"déjà identique", L"already identical") : T(L"refusé d'office", L"refused outright");
    r.chipCol = state == 0 ? kWarn : state == 1 || state == 3 ? kRed : kOk;
    return r;
}
// Le texte et les boutons de la question.
static void DbConsentText(DbState &s, const std::wstring &host, const std::wstring &who, int nDll, uint64_t bytes, const std::wstring &eta, bool always)
{
    wchar_t b[300];
    swprintf_s(b, T(L"%s veut installer %d fichier(s) de programme sur ton PC", L"%s wants to install %d program file(s) on your PC"), host.c_str(), nDll);
    s.title = b;
    s.who = who;
    s.warn = T(L"Un fichier .dll est un programme. Il pourra tout faire sur ton PC. N'accepte que si tu fais confiance à cet hôte.", L"A .dll file is a program. It can do anything on your PC. Only accept if you trust this host.");
    if (bytes) swprintf_s(b, T(L"À recevoir en tout : %s (%s)", L"To receive in total: %s (%s)"), SizeText(bytes).c_str(), eta.c_str());
    else swprintf_s(b, L"%s", T(L"Rien à recevoir : ces fichiers sont déjà sur ton PC, et le jeu les chargera.", L"Nothing to receive: these files are already on your PC, and the game will load them."));
    s.sum = b;
    if (always) s.btns.push_back({ DB_ALWAYS, T(L"Toujours faire confiance à cet hôte", L"Always trust this host"), RectF(), true });
    s.btns.push_back({ DB_ACCEPT, T(L"Accepter", L"Accept"), RectF(), true });
    s.btns.push_back({ DB_REFUSE, T(L"Refuser", L"Refuse"), RectF(), false });
}

// Reglages > Co-op : les hotes de confiance, a retirer.
static void TrustManage()
{
    for (;;) {
        std::vector<TrustHost> v = TrustLoad();
        if (v.empty()) { StyledBox(g_wnd, T(L"Aucun hôte de confiance. Pour en ajouter un : « Toujours faire confiance à cet hôte », quand un hôte Steam d'un salon privé t'envoie ses mods.", L"No trusted host. To add one: \"Always trust this host\", when a Steam host of a private lobby sends you their mods."), T(L"Hôtes de confiance", L"Trusted hosts"), MB_OK | MB_ICONINFORMATION); return; }
        DbState s;
        s.title = T(L"Hôtes de confiance", L"Trusted hosts");
        s.who = T(L"Leurs mods arrivent sans question, tant qu'aucune DLL n'est nouvelle ou modifiée.", L"Their mods arrive without a question, as long as no DLL is new or changed.");
        s.warnOn = false;
        s.select = true;
        s.sel = 0;
        for (auto &h : v) {
            DbRow r;
            wchar_t b[64];
            swprintf_s(b, L"%llu", (unsigned long long)h.id);
            r.path = ShowSafe(h.name) + L"  (Steam " + b + L")";
            swprintf_s(b, T(L"%d DLL", L"%d DLL"), (int)h.files.size());
            r.chip = b;
            r.chipCol = kAcc;
            s.rows.push_back(r);
        }
        s.btns.push_back({ DB_REMOVE, T(L"Retirer", L"Remove"), RectF(), false });
        s.btns.push_back({ DB_DONE, T(L"Fermer", L"Close"), RectF(), false });
        int r = DbRun(s, g_wnd);
        if (r != DB_REMOVE || s.sel < 0 || s.sel >= (int)v.size()) return;
        LaunchLog("mods : hote de confiance retire (Steam %llu)", (unsigned long long)v[s.sel].id);
        v.erase(v.begin() + s.sel);
        TrustSave(v);
    }
}
static std::wstring TrustSummary()
{
    static int n;
    static DWORD readT;
    if (!readT || GetTickCount() - readT > 1500) { readT = GetTickCount(); n = (int)TrustLoad().size(); }   // (le fichier, pas a chaque image)
    if (!n) return T(L"Aucun", L"None");
    wchar_t b[96];
    swprintf_s(b, T(L"%d hôte(s)", L"%d host(s)"), n);
    return b;
}

// ---- essais (jamais de fenetre visible)
static void DbSample(DbState &s, int mode)
{
    s.rows.push_back(DbFileRow("Mods\\Exemple.dll", 184320, 0));
    s.rows.push_back(DbFileRow("Mods\\Autre mod avec un nom long\\Sous-dossier\\Encore un sous-dossier\\Et un autre pour voir la coupure\\Bibliotheque_de_la_voiture_de_rallye.dll", 2411724, 1));
    s.rows.push_back(DbFileRow("Mods\\References\\AttachmentSystem.dll", 40960, 2));
    s.rows.push_back(DbFileRow("Mods\\Assets\\Exemple\\plugin.dll", 12288, 0));
    s.rows.push_back(DbFileRow(std::string("Mods\\exe") + "\xE2\x80\xAE" + "lld.dll", 4096, 0));
    s.rows.push_back(DbFileRow("Mods\\installe.exe", 800000, 3));
    for (int i = 0; i < 24; i++) { char b[64]; sprintf_s(b, "Mods\\References\\Bibliotheque%02d.dll", i); s.rows.push_back(DbFileRow(b, 30000 + i * 1000, i % 3)); }
    DbConsentText(s, mode ? L"Pekka" : L"Paavo", mode ? std::wstring(L"Compte Steam : Pekka (76561198000000000)") : std::wstring(T(L"Hôte non vérifié (IP)", L"Unverified host (IP)")), 29, 3456789, T(L"moins d'une minute", L"under a minute"), mode == 1);
}
static void DbTrustSample(DbState &s)
{
    s.title = T(L"Hôtes de confiance", L"Trusted hosts");
    s.who = T(L"Leurs mods arrivent sans question, tant qu'aucune DLL n'est nouvelle ou modifiée.", L"Their mods arrive without a question, as long as no DLL is new or changed.");
    s.warnOn = false; s.select = true; s.sel = 0;
    for (int i = 0; i < 3; i++) { DbRow r; r.path = i ? L"Paavo  (Steam 7656119800000000" + std::to_wstring(i) + L")" : L"Pekka  (Steam 76561198000000000)"; r.chip = T(L"%d DLL", L"%d DLL"); r.chip.replace(0, 2, std::to_wstring(3 + i * 4)); r.chipCol = kAcc; s.rows.push_back(r); }
    s.btns.push_back({ DB_REMOVE, T(L"Retirer", L"Remove"), RectF(), false });
    s.btns.push_back({ DB_DONE, T(L"Fermer", L"Close"), RectF(), false });
}
static int DbCaptureCmd(const wchar_t *png, int mode)
{
    DbState s;
    if (mode == 3) DbTrustSample(s); else DbSample(s, mode);
    DbLayout(s, 1.5f, 1000);
    s.armAt = GetTickCount();
    Bitmap b((INT)s.w, (INT)s.h, PixelFormat32bppARGB);
    {
        Graphics g(&b);
        g.Clear(Color(255, 40, 44, 52));
        DbDraw(g, s);
    }
    CLSID id;
    return EncoderClsid(L"image/png", &id) && b.Save(png, &id, NULL) == Ok ? 0 : 1;
}
static void AccordLog(const wchar_t *f, const char *line)
{
    FILE *o = _wfopen(f, L"ab");
    if (o) { fprintf(o, "%s\r\n", line); fclose(o); }
}
static int DbSelfTest(const wchar_t *log)
{
    DeleteFileW(log);
    int bad = 0;
    auto check = [&](const char *what, int got, int want) { char b[160]; sprintf_s(b, "%s : %d (attendu %d) %s", what, got, want, got == want ? "ok" : "ECHEC"); AccordLog(log, b); if (got != want) bad++; };
    auto run = [&](const char *what, int want, std::function<void(DbState &)> act) {
        DbState s;
        DbSample(s, 1);
        if (!DbOpen(s, NULL, true)) { AccordLog(log, "fenetre impossible"); bad++; return; }
        act(s);
        check(what, s.result, want);
        DbClose(s);
    };
    auto key = [](DbState &s, WPARAM k) { SendMessageW(s.wnd, WM_KEYDOWN, k, 0); };
    auto click = [](DbState &s, RectF r) {
        LPARAM lp = MAKELPARAM((int)((r.X + r.Width / 2) * s.scale), (int)((r.Y + r.Height / 2) * s.scale));
        SendMessageW(s.wnd, WM_LBUTTONDOWN, 0, lp); SendMessageW(s.wnd, WM_LBUTTONUP, 0, lp);
    };
    auto btn = [](DbState &s, int id) { for (auto &b : s.btns) if (b.id == id) return b.r; return RectF(); };
    run("Echap refuse", DB_REFUSE, [&](DbState &s) { key(s, VK_ESCAPE); });
    run("Entree refuse (bouton par defaut)", DB_REFUSE, [&](DbState &s) { key(s, VK_RETURN); });
    run("fermeture refuse", DB_REFUSE, [&](DbState &s) { SendMessageW(s.wnd, WM_CLOSE, 0, 0); });
    run("clic sur Accepter dans la premiere seconde : rien", 0, [&](DbState &s) { click(s, btn(s, DB_ACCEPT)); });
    run("clic sur Accepter ensuite", DB_ACCEPT, [&](DbState &s) { s.armAt = GetTickCount() - 1; click(s, btn(s, DB_ACCEPT)); });
    run("clic sur Toujours ensuite", DB_ALWAYS, [&](DbState &s) { s.armAt = GetTickCount() - 1; click(s, btn(s, DB_ALWAYS)); });
    run("clic sur Refuser", DB_REFUSE, [&](DbState &s) { click(s, btn(s, DB_REFUSE)); });
    run("appuye hors du bouton, relache dessus : rien", 0, [&](DbState &s) {
        s.armAt = GetTickCount() - 1;
        RectF r = btn(s, DB_ACCEPT);
        SendMessageW(s.wnd, WM_LBUTTONDOWN, 0, MAKELPARAM((int)((s.list.X + 20) * s.scale), (int)((s.list.Y + 20) * s.scale)));
        SendMessageW(s.wnd, WM_LBUTTONUP, 0, MAKELPARAM((int)((r.X + 10) * s.scale), (int)((r.Y + 10) * s.scale)));
    });
    run("survol d'Accepter puis Entree : refuse", DB_REFUSE, [&](DbState &s) {
        s.armAt = GetTickCount() - 1;
        RectF r = btn(s, DB_ACCEPT);
        SendMessageW(s.wnd, WM_MOUSEMOVE, 0, MAKELPARAM((int)((r.X + 10) * s.scale), (int)((r.Y + 10) * s.scale)));
        key(s, VK_RETURN);
    });
    run("Tab vers Toujours puis Entree (apres une seconde)", DB_ALWAYS, [&](DbState &s) { s.armAt = GetTickCount() - 1; key(s, VK_TAB); key(s, VK_RETURN); });
    {
        DbState s;
        DbSample(s, 1);
        if (DbOpen(s, NULL, true)) {
            float my = DbMaxScroll(s);
            check("la liste defile (contenu plus haut que la liste)", my > 0, 1);
            key(s, VK_END); check("Fin : tout en bas", (int)(s.scroll + 0.5f), (int)(my + 0.5f));
            key(s, VK_HOME); check("Debut : tout en haut", (int)s.scroll, 0);
            SendMessageW(s.wnd, WM_MOUSEWHEEL, MAKEWPARAM(0, (WORD)-120), 0); check("molette : 60 points", (int)s.scroll, 60);
            bool cut = false;
            for (auto &r : s.rows) cut |= r.path.find(L"\x2026") != std::wstring::npos;
            check("aucun chemin coupe", cut, 0);
            check("toutes les lignes sont la (rien de regroupe)", (int)s.rows.size(), 30);
            check("U+202E montre, pas applique", s.rows[4].path.find(L"[U+202E]") != std::wstring::npos, 1);
            DbClose(s);
        }
    }
    {   // hotes de confiance : Echap ferme sans rien retirer ; clic sur une ligne, Retirer
        DbState s;
        DbTrustSample(s);
        if (DbOpen(s, NULL, true)) {
            check("Entree sur Fermer", (SendMessageW(s.wnd, WM_KEYDOWN, VK_RETURN, 0), s.result), DB_DONE);
            s.result = 0;
            check("Echap ferme", (SendMessageW(s.wnd, WM_KEYDOWN, VK_ESCAPE, 0), s.result), DB_DONE);
            s.result = 0;
            LPARAM row = MAKELPARAM((int)((s.list.X + 20) * s.scale), (int)((s.list.Y + s.rows[1].y + 8) * s.scale));
            SendMessageW(s.wnd, WM_LBUTTONDOWN, 0, row); SendMessageW(s.wnd, WM_LBUTTONUP, 0, row);
            check("clic sur la 2e ligne", s.sel, 1);
            click(s, btn(s, DB_REMOVE));
            check("Retirer", s.result, DB_REMOVE);
            DbClose(s);
        }
    }
    {   // salon public / IP : pas de Toujours
        DbState s;
        DbSample(s, 0);
        bool any = false;
        for (auto &b : s.btns) any |= b.id == DB_ALWAYS;
        check("IP ou public : pas de bouton Toujours", any, 0);
    }
    AccordLog(log, bad ? "ECHEC" : "TOUT OK");
    return bad ? 1 : 0;
}
static bool SyncPathGuest(std::string &p);
static int AccordSelfTest(const wchar_t *log)
{
    DeleteFileW(log);
    int bad = 0;
    auto say = [&](const std::string &l, bool good) { AccordLog(log, (l + (good ? " ok" : " ECHEC")).c_str()); if (!good) bad++; };
    struct { const char *in, *out; int kind; } paths[] = {
        { "Mods\\x.dll", "Mods\\x.dll", PROG_DLL },
        { "Mods\\x.dll.", "Mods\\x.dll", PROG_DLL },
        { "Mods\\x.DLL ", "Mods\\x.DLL", PROG_DLL },
        { "Mods\\x.dll . ", "Mods\\x.dll", PROG_DLL },
        { "mods\\x.dll", "Mods\\x.dll", PROG_DLL },
        { "Mods/References/y.dll", "Mods\\References\\y.dll", PROG_DLL },
        { "Mods\\Dossier.\\z.dll", "Mods\\Dossier\\z.dll", PROG_DLL },
        { "Mods\\Assets\\a\\b.dll", "Mods\\Assets\\a\\b.dll", PROG_DLL },
        { "Images\\x.dll", "Images\\x.dll", PROG_DLL },
        { "Mods\\e.exe", "Mods\\e.exe", PROG_INTERDIT },
        { "Mods\\e.EXE.", "Mods\\e.EXE", PROG_INTERDIT },
        { "Mods\\b.bat", "Mods\\b.bat", PROG_INTERDIT },
        { "Radio\\s.js", "Radio\\s.js", PROG_INTERDIT },
        { "Mods\\x.dll.config", "Mods\\x.dll.config", PROG_INTERDIT },
        { "Mods\\References\\x.dll.CONFIG.", "Mods\\References\\x.dll.CONFIG", PROG_INTERDIT },
        { "CD1\\track1.ogg", "CD1\\track1.ogg", PROG_NONE },
        { "Mods\\Assets\\a\\t.png", "Mods\\Assets\\a\\t.png", PROG_NONE },
        { "Mods\\x.dll.off", "Mods\\x.dll.off", PROG_NONE },
        { "..\\x.dll", NULL, 0 }, { "Mods\\..\\..\\x.dll", NULL, 0 }, { "C:\\x.dll", NULL, 0 }, { "\\\\serveur\\x.dll", NULL, 0 }, { "/x.dll", NULL, 0 },
        { "Mods\\x.dll:flux", NULL, 0 }, { "Mods\\NUL.dll", NULL, 0 }, { "Mods\\con", NULL, 0 }, { "Mods\\COM1.dll", NULL, 0 }, { "Mods\\a?.dll", NULL, 0 }, { "Mods\\a*.dll", NULL, 0 },
        { "Mods\\\\x.dll", NULL, 0 }, { "Mods\\ . \\x.dll", NULL, 0 }, { "Mods\\ctrl\x01.dll", NULL, 0 },
    };
    for (auto &t : paths) {
        std::string p = t.in;
        bool ok = SyncPathGuest(p);
        bool good = t.out ? ok && p == t.out && ProgKind(p) == t.kind : !ok;
        say(std::string(t.in) + " -> " + (ok ? p : "refuse") + " kind " + std::to_string(ok ? ProgKind(p) : -1), good);
    }
    say("cles : x.DLL = x.dll", ProgKey("Mods\\X.DLL") == ProgKey("Mods\\x.dll"));
    // SHA-256 : vecteur connu
    std::wstring tmp = LocalDir() + L"essai-sha.tmp";
    SHCreateDirectoryExW(NULL, LocalDir().c_str(), NULL);
    FILE *f = _wfopen(tmp.c_str(), L"wb"); if (f) { fwrite("abc", 1, 3, f); fclose(f); }
    say("SHA-256(abc)", cle::Hex(cle::Sha256File(tmp)) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    say("SHA-256 d'un fichier absent : vide", cle::Sha256File(tmp + L".absent").empty());
    DeleteFileW(tmp.c_str());
    // hotes de confiance : aller et retour, par jeu, retrait
    std::wstring bak = TrustPath() + L".essai";
    MoveFileExW(TrustPath().c_str(), bak.c_str(), MOVEFILE_REPLACE_EXISTING);
    std::vector<TrustFile> a = { { 0, "aaaa", "Mods\\Un fichier.dll", 12, 0x1234567890ABCDEFULL } }, b = { { 1, "bbbb", "Mods\\Deux.dll", 34, 5 } }, got;
    say("aucun hote au depart", !TrustFilesOf(42, 0, got));
    say("retenir", TrustRemember(42, "Nom\tavec tab", 0, a) && TrustRemember(42, "Nom", 1, b));
    got.clear();
    say("relu pour ce jeu", TrustFilesOf(42, 0, got) && got.size() == 1 && got[0].path == a[0].path && got[0].sha == "aaaa" && got[0].size == 12 && got[0].fnv == a[0].fnv);
    got.clear();
    say("l'autre jeu garde sa liste", TrustFilesOf(42, 1, got) && got.size() == 1 && got[0].path == b[0].path);
    say("remplace pour ce jeu", TrustRemember(42, "Nom", 0, b));
    got.clear();
    say("plus l'ancienne", TrustFilesOf(42, 0, got) && got.size() == 1 && got[0].path == "Mods\\Deux.dll");
    got.clear();
    say("autre hote inconnu", !TrustFilesOf(43, 0, got));
    TrustSave(std::vector<TrustHost>());
    say("retire", !TrustFilesOf(42, 0, got));
    DeleteFileW(TrustPath().c_str());
    MoveFileExW(bak.c_str(), TrustPath().c_str(), MOVEFILE_REPLACE_EXISTING);
    AccordLog(log, bad ? "ECHEC" : "TOUT OK");
    return bad ? 1 : 0;
}
