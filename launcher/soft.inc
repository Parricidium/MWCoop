// ---------------------------------------------------------------- SOFT UI : jetons, ombres en cache, composants, animations (refonte, lot L1)
// Spec MWCoop-spec-refonte-lanceur.md §3 (theme), §6 (composants), §7 (animations) ; maquette MWCoop-maquette-lanceur-neumorphism.html.
// Neumorphism lisible : relief par deux ombres (claire en haut a gauche, sombre en bas a droite), textes fonces et contrastes,
// chaque etat a une couleur ET un texte ou une icone. Tout ici dessine en coordonnees de la carte, comme les helpers d'avant
// (Text, Para, Icon, HotZone) ; les pages de la nouvelle interface (L2 / L3) s'en servent, l'ancienne interface n'y touche pas.
//  - Jetons (SoftTokens) : clair / sombre, exactement les couleurs de la maquette ; accent selon le jeu (MWC bleu glacier,
//    MSC orange ete). Theme : [Lanceur] ThemeNouvelle=clair|sombre|auto (auto = celui de Windows, Win7 = clair).
//  - Ombres : un bitmap flou par (rayon, flou, couleur), flou de notre code (3 passes de boite), decoupe en 9 morceaux (coins
//    fixes, bords etires) ; les ombres interieures (creux) par taille, en cache aussi. Rien n'est recalcule a chaque image.
//  - Animations : une valeur par cle (AnimTo), courbes de la maquette (e-out 0.22,1,0.36,1 ; e-move 0.25,1,0.5,1), durees
//    140 / 220 / 280 ms, interruptibles (on repart de la valeur courante), reduites (AnimReduced) = 1 ms sans mouvement.
//    Changement de page (sens = position dans la barre), curseur des onglets, interrupteurs, appui, notifications, cercle du
//    theme depuis le bouton Theme.
//  - Planche d'essai : /capture <png> composants [/theme clair|sombre] [/msc] (DrawSoftDemo).
struct SoftTokens { Color bg, bgIn, ink, muted, faint, shD, shL, line, ok, okBg, warn, warnBg, bad, badBg, acc, accInk, accSoft; };
static const SoftTokens kSoftLight = {
    Color(255, 0xE4, 0xE9, 0xF0), Color(255, 0xD9, 0xE0, 0xE9), Color(255, 0x24, 0x30, 0x3D), Color(255, 0x56, 0x62, 0x73), Color(255, 0x7A, 0x86, 0x96),
    Color(255, 0xB8, 0xC2, 0xCF), Color(255, 0xFF, 0xFF, 0xFF), Color(26, 0x24, 0x30, 0x3D),
    Color(255, 0x1F, 0x8A, 0x5B), Color(255, 0xD3, 0xEE, 0xDF), Color(255, 0xA3, 0x5F, 0x00), Color(255, 0xF7, 0xE6, 0xC8), Color(255, 0xC0, 0x35, 0x2B), Color(255, 0xF6, 0xD6, 0xD2),
    Color(255, 0x3E, 0x8B, 0xD8), Color(255, 255, 255, 255), Color(41, 0x3E, 0x8B, 0xD8) };
static const SoftTokens kSoftDark = {
    Color(255, 0x1D, 0x21, 0x29), Color(255, 0x17, 0x1A, 0x21), Color(255, 0xE7, 0xEB, 0xF1), Color(255, 0xA6, 0xB0, 0xBE), Color(255, 0x80, 0x8B, 0x9A),
    Color(255, 0x12, 0x15, 0x1A), Color(255, 0x2A, 0x30, 0x3B), Color(23, 0xE7, 0xEB, 0xF1),
    Color(255, 0x5F, 0xD3, 0x9A), Color(255, 0x1F, 0x3A, 0x2E), Color(255, 0xF2, 0xB4, 0x5B), Color(255, 0x3D, 0x30, 0x20), Color(255, 0xFF, 0x8A, 0x80), Color(255, 0x43, 0x26, 0x24),
    Color(255, 0x5B, 0xA4, 0xEE), Color(255, 0x0B, 0x1A, 0x2A), Color(41, 0x5B, 0xA4, 0xEE) };
static SoftTokens g_soft = kSoftDark;   // jetons courants (theme + accent du jeu) : SoftThemeApply
static bool g_softDark = true;
static int g_softMode = -1;             // 0 clair, 1 sombre, 2 auto (-1 : pas encore lu)
static bool g_softDemo;                 // /capture ... composants
static const float kSoftRCard = 22, kSoftRCtl = 14;
static const int kSoftTPress = 140, kSoftTUi = 220, kSoftTPage = 280;
enum { SE_OUT, SE_MOVE, SE_LINEAR };   // courbes

static bool SoftWindowsDark()   // Win10+ : AppsUseLightTheme ; Win7 (ou Wine) : clair
{
    DWORD v = 1, sz = sizeof(v);
    if (IsWine() || RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"AppsUseLightTheme", RRF_RT_REG_DWORD, NULL, &v, &sz) != ERROR_SUCCESS) return false;
    return v == 0;
}
static int SoftModeRead()
{
    wchar_t v[16] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"ThemeNouvelle", L"auto", v, 16, g_iniLauncher.c_str());
    return !_wcsicmp(v, L"clair") ? 0 : !_wcsicmp(v, L"sombre") ? 1 : 2;
}
static void SoftThemeApply()
{
    if (g_softMode < 0) g_softMode = SoftModeRead();
    g_softDark = g_softMode == 1 || (g_softMode == 2 && (g_newUi ? SoftWindowsDark() : g_dark));   // (captures : le theme demande)
    g_soft = g_softDark ? kSoftDark : kSoftLight;
    bool msc = g_game == GAME_MSC;
    if (msc) {   // orange ete ; texte sur l'accent toujours fonce
        g_soft.acc = g_softDark ? Color(255, 0xF0, 0xA0, 0x4B) : Color(255, 0xE0, 0x89, 0x2B);
        g_soft.accInk = Color(255, 0x1E, 0x12, 0x06);
        g_soft.accSoft = Color(46, g_soft.acc.GetR(), g_soft.acc.GetG(), g_soft.acc.GetB());
    }
    if (g_newUi) g_dark = g_softDark;   // (la carte d'avant, dessinee dedans en L0 / L1, suit)
}
static void SoftModeSet(int mode)   // Reglages > Fenetre du lanceur : Clair / Sombre / Auto
{
    g_softMode = mode < 0 || mode > 2 ? 2 : mode;
    WritePrivateProfileStringW(L"Lanceur", L"ThemeNouvelle", g_softMode == 0 ? L"clair" : g_softMode == 1 ? L"sombre" : NULL, g_iniLauncher.c_str());
    SoftThemeApply();
    InvalidateAll();
}

// ---- courbes et animations
static float SoftBezier(float t, float x1, float y1, float x2, float y2)   // y pour x = t (dichotomie)
{
    if (t <= 0) return 0; if (t >= 1) return 1;
    float lo = 0, hi = 1, s = t;
    for (int i = 0; i < 12; i++) {
        s = (lo + hi) / 2;
        float u = 1 - s, x = 3 * u * u * s * x1 + 3 * u * s * s * x2 + s * s * s;
        if (x < t) lo = s; else hi = s;
    }
    float u = 1 - s;
    return 3 * u * u * s * y1 + 3 * u * s * s * y2 + s * s * s;
}
static float SoftEase(float t, int curve) { return curve == SE_OUT ? SoftBezier(t, 0.22f, 1, 0.36f, 1) : curve == SE_MOVE ? SoftBezier(t, 0.25f, 1, 0.5f, 1) : min(max(t, 0.0f), 1.0f); }
struct SoftAnim { float from, to; DWORD t0; int dur, curve; };
static std::unordered_map<long long, SoftAnim> g_softAnims;
static bool g_softAnimating;   // une animation en cours a cette image (Tick : images a 60)
static int SoftDur(int ms) { return AnimReduced() ? 1 : ms; }
// Valeur animee vers 'target' pour la cle : la premiere fois, directement a la cible (rien n'est anime au demarrage) ; un
// changement de cible repart de la valeur courante (interruptible).
static float AnimTo(long long key, float target, int dur = kSoftTUi, int curve = SE_MOVE)
{
    DWORD now = GetTickCount();
    auto it = g_softAnims.find(key);
    if (it == g_softAnims.end()) { g_softAnims[key] = { target, target, now, 1, curve }; return target; }
    SoftAnim &a = it->second;
    float t = a.dur <= 1 ? 1.0f : min(1.0f, (now - a.t0) / (float)a.dur);
    float cur = a.from + (a.to - a.from) * SoftEase(t, a.curve);
    if (target != a.to) { a.from = cur; a.to = target; a.t0 = now; a.dur = SoftDur(dur); a.curve = curve; t = 0; cur = a.from; }
    if (t < 1) { g_softAnimating = true; InvalidateAll(); }
    return cur;
}
static float AnimGet(long long key, float def) { auto it = g_softAnims.find(key); if (it == g_softAnims.end()) return def; return AnimTo(key, it->second.to); }
static long long AK(int kind, int id) { return ((long long)kind << 32) | (unsigned)id; }   // (cle : famille, id)
enum { AKH_HOVER = 1, AKH_PRESS, AKH_TOGGLE, AKH_SEG, AKH_TAB, AKH_PAGE, AKH_THEME, AKH_TOAST, AKH_MISC };
// Appui (140 ms) : note au clic (WndProc, zones UI_*), lue par les composants.
static int g_softPressId = -1; static DWORD g_softPressT;
static void SoftPressNote(int id) { g_softPressId = id; g_softPressT = GetTickCount(); InvalidateAll(); }
static float SoftPress(int id)   // 1 pendant l'appui, puis retour en 140 ms
{
    if (id < 0 || id != g_softPressId) return 0;
    DWORD el = GetTickCount() - g_softPressT;
    int d = SoftDur(kSoftTPress);
    if (el >= (DWORD)d * 2) return 0;
    g_softAnimating = true; InvalidateAll();
    return el < (DWORD)d ? 1.0f : 1.0f - SoftEase((el - d) / (float)d, SE_OUT);
}

// ---- ombres en cache
static int SoftBlurPad(int blur) { return blur + blur / 2 + 1; }
// Masque alpha d'un rectangle arrondi (w x h, rayon rad) flou 3 fois (boite de rayon blur/2) : sigma ~ blur/2, comme CSS.
static void SoftBlurMask(std::vector<BYTE> &m, int w, int h, int blur)
{
    int r = max(1, blur / 2);
    std::vector<BYTE> t(m.size());
    for (int pass = 0; pass < 3; pass++) {
        for (int y = 0; y < h; y++) {   // horizontal
            int acc = 0; const BYTE *row = &m[y * w]; BYTE *out = &t[y * w];
            for (int x = -r; x <= r; x++) acc += row[min(max(x, 0), w - 1)];
            for (int x = 0; x < w; x++) { out[x] = (BYTE)(acc / (2 * r + 1)); acc += row[min(x + r + 1, w - 1)] - row[max(x - r, 0)]; }
        }
        for (int x = 0; x < w; x++) {   // vertical
            int acc = 0;
            for (int y = -r; y <= r; y++) acc += t[min(max(y, 0), h - 1) * w + x];
            for (int y = 0; y < h; y++) { m[y * w + x] = (BYTE)(acc / (2 * r + 1)); acc += t[min(y + r + 1, h - 1) * w + x] - t[max(y - r, 0) * w + x]; }
        }
    }
}
static void SoftRoundMask(std::vector<BYTE> &m, int w, int h, RectF r, float rad, bool inverse)
{
    Bitmap b(w, h, PixelFormat32bppARGB);
    { Graphics g(&b); g.SetSmoothingMode(SmoothingModeAntiAlias); g.Clear(inverse ? Color(255, 255, 255, 255) : Color(0, 0, 0, 0)); GraphicsPath p; RoundRect(p, r, rad); SolidBrush br(inverse ? Color(0, 0, 0, 0) : Color(255, 255, 255, 255)); if (inverse) g.SetCompositingMode(CompositingModeSourceCopy); g.FillPath(&br, &p); }
    BitmapData d; Rect rc(0, 0, w, h);
    b.LockBits(&rc, ImageLockModeRead, PixelFormat32bppARGB, &d);
    for (int y = 0; y < h; y++) { const BYTE *row = (const BYTE *)d.Scan0 + y * d.Stride; for (int x = 0; x < w; x++) m[y * w + x] = row[x * 4 + 3]; }
    b.UnlockBits(&d);
}
static Bitmap *SoftMaskToBitmap(const std::vector<BYTE> &m, int w, int h, Color c)   // (PARGB, couleur uniforme)
{
    Bitmap *b = new Bitmap(w, h, PixelFormat32bppPARGB);
    BitmapData d; Rect rc(0, 0, w, h);
    b->LockBits(&rc, ImageLockModeWrite, PixelFormat32bppPARGB, &d);
    int cr = c.GetR(), cg = c.GetG(), cb = c.GetB(), ca = c.GetA();
    for (int y = 0; y < h; y++) {
        BYTE *row = (BYTE *)d.Scan0 + y * d.Stride;
        for (int x = 0; x < w; x++) { int a = m[y * w + x] * ca / 255; row[x * 4] = (BYTE)(cb * a / 255); row[x * 4 + 1] = (BYTE)(cg * a / 255); row[x * 4 + 2] = (BYTE)(cr * a / 255); row[x * 4 + 3] = (BYTE)a; }
    }
    b->UnlockBits(&d);
    return b;
}
// Ombre exterieure : bitmap en 9 morceaux par (rayon, flou, couleur). Le morceau central est uni (couleur) : rempli a part.
struct SoftShadowSlice { Bitmap *b; int cs; };   // (bitmap, taille d'un coin = rayon + marge du flou)
static std::map<unsigned long long, SoftShadowSlice> g_softShadows;
static SoftShadowSlice &SoftShadowFor(int rad, int blur, Color c)
{
    unsigned long long key = ((unsigned long long)rad << 48) | ((unsigned long long)blur << 40) | c.GetValue();
    auto it = g_softShadows.find(key);
    if (it != g_softShadows.end()) return it->second;
    int pad = SoftBlurPad(blur), cs = rad + pad, mid = 4, w = 2 * cs + mid, h = w;
    std::vector<BYTE> m(w * h);
    SoftRoundMask(m, w, h, RectF((float)pad, (float)pad, (float)(w - 2 * pad), (float)(h - 2 * pad)), (float)rad, false);
    SoftBlurMask(m, w, h, blur);
    SoftShadowSlice s = { SoftMaskToBitmap(m, w, h, c), cs };
    return g_softShadows.emplace(key, s).first->second;
}
static void SoftDraw9(Graphics &g, Bitmap *b, int cs, RectF dst)   // (dst : la zone couverte par l'ombre, coins compris)
{
    int sw = (int)b->GetWidth(), mid = sw - 2 * cs;
    float x0 = dst.X, y0 = dst.Y, x1 = dst.X + dst.Width, y1 = dst.Y + dst.Height, c = (float)cs;
    if (dst.Width < 2 * c || dst.Height < 2 * c) { g.DrawImage(b, dst, 0, 0, (float)sw, (float)sw, UnitPixel); return; }   // (trop petit : etire tout)
    auto part = [&](float dx, float dy, float dw, float dh, int sx, int sy, int sw2, int sh2) { if (dw > 0 && dh > 0) g.DrawImage(b, RectF(dx, dy, dw, dh), (float)sx, (float)sy, (float)sw2, (float)sh2, UnitPixel); };
    part(x0, y0, c, c, 0, 0, cs, cs); part(x1 - c, y0, c, c, cs + mid, 0, cs, cs); part(x0, y1 - c, c, c, 0, cs + mid, cs, cs); part(x1 - c, y1 - c, c, c, cs + mid, cs + mid, cs, cs);
    part(x0 + c, y0, dst.Width - 2 * c, c, cs, 0, mid, cs); part(x0 + c, y1 - c, dst.Width - 2 * c, c, cs, cs + mid, mid, cs);
    part(x0, y0 + c, c, dst.Height - 2 * c, 0, cs, cs, mid); part(x1 - c, y0 + c, c, dst.Height - 2 * c, cs + mid, cs, cs, mid);
    part(x0 + c, y0 + c, dst.Width - 2 * c, dst.Height - 2 * c, cs, cs, mid, mid);
}
// Relief d'un element (r : son rectangle) : ombre sombre en bas a droite (dx, dy, flou), claire en haut a gauche.
static void SoftRaised(Graphics &g, RectF r, float rad, int off, int blur, float strength = 1.0f)
{
    int pad = SoftBlurPad(blur);
    InterpolationMode im = g.GetInterpolationMode(); g.SetInterpolationMode(InterpolationModeBilinear);
    PixelOffsetMode pm = g.GetPixelOffsetMode(); g.SetPixelOffsetMode(PixelOffsetModeHalf);
    Color sd = WithA(g_soft.shD, strength), sl = WithA(g_soft.shL, g_softDark ? strength * 0.9f : strength);
    SoftShadowSlice &a = SoftShadowFor((int)rad, blur, sd);
    SoftDraw9(g, a.b, a.cs, RectF(r.X + off - pad, r.Y + off - pad, r.Width + 2 * pad, r.Height + 2 * pad));
    SoftShadowSlice &l = SoftShadowFor((int)rad, blur, sl);
    SoftDraw9(g, l.b, l.cs, RectF(r.X - off - pad, r.Y - off - pad, r.Width + 2 * pad, r.Height + 2 * pad));
    g.SetInterpolationMode(im); g.SetPixelOffsetMode(pm);
}
// Creux (ombres interieures) : bitmap par (taille, rayon, decalage, flou, theme), clippe a la forme.
static std::map<std::wstring, Bitmap *> g_softInsets;
static void SoftInset(Graphics &g, RectF r, float rad, int off = 3, int blur = 7)
{
    int w = max(1, (int)(r.Width + 0.5f)), h = max(1, (int)(r.Height + 0.5f));
    wchar_t k[64]; swprintf_s(k, L"%d,%d,%d,%d,%d,%d,%d", w, h, (int)rad, off, blur, (int)g_softDark, g_game == GAME_MSC);
    auto it = g_softInsets.find(k);
    if (it == g_softInsets.end()) {
        if (g_softInsets.size() > 300) { for (auto &kv : g_softInsets) delete kv.second; g_softInsets.clear(); }
        std::vector<BYTE> dark(w * h), light(w * h);
        SoftRoundMask(dark, w, h, RectF((float)off, (float)off, (float)w, (float)h), rad, true);    // (ce qui est hors de la forme decalee : l'ombre en haut a gauche, dedans)
        SoftRoundMask(light, w, h, RectF((float)-off, (float)-off, (float)w, (float)h), rad, true);  // (idem en bas a droite : la lumiere)
        SoftBlurMask(dark, w, h, blur); SoftBlurMask(light, w, h, blur);
        Bitmap *b = new Bitmap(w, h, PixelFormat32bppPARGB);
        { Graphics gb(b); gb.Clear(Color(0, 0, 0, 0)); GraphicsPath p; RoundRect(p, RectF(0, 0, (float)w, (float)h), rad); gb.SetSmoothingMode(SmoothingModeAntiAlias); gb.SetClip(&p);
          Bitmap *bd = SoftMaskToBitmap(dark, w, h, g_soft.shD), *bl = SoftMaskToBitmap(light, w, h, g_softDark ? WithA(g_soft.shL, 0.9f) : g_soft.shL);
          gb.DrawImage(bd, 0, 0, w, h); gb.DrawImage(bl, 0, 0, w, h); delete bd; delete bl; }
        it = g_softInsets.emplace(k, b).first;
    }
    g.DrawImage(it->second, r.X, r.Y, (float)w, (float)h);
}
static void SoftShape(Graphics &g, RectF r, float rad, Color fill) { GraphicsPath p; RoundRect(p, r, rad); SolidBrush b(fill); g.FillPath(&b, &p); }

// ---- composants
static void SoftCard(Graphics &g, RectF r, float rad) { SoftRaised(g, r, rad, 7, 16); SoftShape(g, r, rad, g_soft.bg); }
static void SoftCardTitle(Graphics &g, RectF card, const std::wstring &title, const std::wstring &sub = L"")
{
    Text(g, title, RectF(card.X + 22, card.Y + 18, card.Width - 44, 22), 16, FontStyleBold, g_soft.ink, StringAlignmentNear);
    if (!sub.empty()) Text(g, sub, RectF(card.X + 22, card.Y + 40, card.Width - 44, 18), 13, FontStyleRegular, g_soft.muted, StringAlignmentNear);
}
// Bouton : relief ; appuye = creux + 97 % ; principal = accent. Survol : texte accent (secondaire) / plus clair (principal).
static void SoftButton(Graphics &g, RectF r, const std::wstring &s, int id, bool primary = false, int icon = -1, bool enabled = true, float rad = kSoftRCtl)
{
    bool hot = enabled && g_uiHot == id;
    float press = enabled ? SoftPress(id) : 0;
    float a = enabled ? 1.0f : 0.45f;
    RectF rr = r;
    if (press > 0) { float k = 1 - 0.03f * press; rr = RectF(r.X + r.Width * (1 - k) / 2, r.Y + r.Height * (1 - k) / 2, r.Width * k, r.Height * k); }
    if (press < 1) SoftRaised(g, rr, rad, 4, 10, a * (1 - press));
    SoftShape(g, rr, rad, primary ? WithA(g_soft.acc, a) : WithA(g_soft.bg, 1.0f));
    if (press > 0) { GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, rr, rad); g.SetClip(&p); Bitmap *dummy = NULL; (void)dummy;
        SoftShape(g, rr, rad, primary ? Color((BYTE)(70 * press), 0, 0, 0) : WithA(g_soft.bgIn, press)); g.Restore(st); if (!primary) { GraphicsState s2 = g.Save(); GraphicsPath p2; RoundRect(p2, rr, rad); g.SetClip(&p2); SoftInset(g, rr, rad); g.Restore(s2); } }
    if (hot && primary) { SoftShape(g, rr, rad, Color(28, 255, 255, 255)); }
    Color ink = primary ? WithA(g_soft.accInk, a) : WithA(hot ? g_soft.acc : g_soft.ink, a);
    Graphics &mg = MeasureG();
    float px = r.Height >= 44 ? 14.0f : 13.0f;
    float tw = s.empty() ? 0 : MeasureW(mg, s, px, FontStyleBold), iw = icon >= 0 ? (s.empty() ? 0 : 26.0f) : 0.0f;
    float x0 = rr.X + (rr.Width - tw - iw) / 2;
    if (icon >= 0) Icon(g, icon, s.empty() ? rr.X + rr.Width / 2 : x0 + 9, rr.Y + rr.Height / 2, 18, ink, 2.1f);
    if (!s.empty()) Text(g, s, RectF(x0 + iw, rr.Y, tw + 2, rr.Height), px, FontStyleBold, ink, StringAlignmentNear);
    if (enabled) HotZone(r, id);
}
// Interrupteur 52 x 30 : creux, pastille qui glisse (220 ms), fond accent quand actif.
static void SoftToggle(Graphics &g, RectF r, bool on, int id, bool enabled = true)
{
    float t = AnimTo(AK(AKH_TOGGLE, id), on ? 1.0f : 0.0f, kSoftTUi, SE_MOVE);
    bool hot = enabled && g_uiHot == id;
    Color fill = Color((BYTE)255, (BYTE)(g_soft.bgIn.GetR() + (g_soft.acc.GetR() - g_soft.bgIn.GetR()) * t), (BYTE)(g_soft.bgIn.GetG() + (g_soft.acc.GetG() - g_soft.bgIn.GetG()) * t), (BYTE)(g_soft.bgIn.GetB() + (g_soft.acc.GetB() - g_soft.bgIn.GetB()) * t));
    SoftShape(g, r, r.Height / 2, WithA(fill, enabled ? 1.0f : 0.5f));
    if (t < 0.5f) { GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, r, r.Height / 2); g.SetClip(&p); SoftInset(g, r, r.Height / 2, 3, 6); g.Restore(st); }
    if (hot) { Pen hp(WithA(g_soft.acc, 0.9f), 1.5f); GraphicsPath p; RoundRect(p, r, r.Height / 2); g.DrawPath(&hp, &p); }
    float d = r.Height - 8, x = r.X + 4 + (r.Width - 8 - d) * t, y = r.Y + 4;
    SoftRaised(g, RectF(x, y, d, d), d / 2, 2, 5, 0.9f);
    SolidBrush k(enabled ? g_soft.bg : WithA(g_soft.bg, 0.7f)); g.FillEllipse(&k, x, y, d, d);
    if (enabled) HotZone(r, id);
}
// Segments / onglets : creux ; le curseur d'accent glisse sous le choix (220 ms, un seul objet) ; zones id0 + i.
static float SoftSegW(const std::vector<std::wstring> &labs, float px = 13)
{
    Graphics &mg = MeasureG(); float w = 10;
    for (auto &l : labs) w += MeasureW(mg, l, px, FontStyleBold) + 28 + 4;
    return w - 4;
}
static void SoftSeg(Graphics &g, RectF r, const std::vector<std::wstring> &labs, int sel, int id0, bool enabled = true, float px = 13)
{
    Graphics &mg = MeasureG();
    std::vector<float> w; float tot = 0;
    for (auto &l : labs) { float ww = MeasureW(mg, l, px, FontStyleBold) + 28; w.push_back(ww); tot += ww; }
    float gap = 4, extra = max(0.0f, (r.Width - 10 - tot - gap * (labs.size() - 1)) / labs.size());
    SoftShape(g, r, min(kSoftRCtl, r.Height / 2), g_soft.bgIn);
    { GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, r, min(kSoftRCtl, r.Height / 2)); g.SetClip(&p); SoftInset(g, r, min(kSoftRCtl, r.Height / 2)); g.Restore(st); }
    float x = r.X + 5, tx = x, tw = w.empty() ? 0 : w[0] + extra;
    std::vector<float> xs;
    for (size_t i = 0; i < labs.size(); i++) { xs.push_back(x); if ((int)i == sel) { tx = x; tw = w[i] + extra; } x += w[i] + extra + gap; }
    float cx = AnimTo(AK(AKH_SEG, id0), tx, kSoftTUi, SE_MOVE), cw = AnimTo(AK(AKH_SEG, id0 + 1000), tw, kSoftTUi, SE_MOVE);
    RectF th(cx, r.Y + 5, cw, r.Height - 10);
    SoftRaised(g, th, 10, 3, 8, enabled ? 0.8f : 0.4f);
    SoftShape(g, th, 10, WithA(g_soft.acc, enabled ? 1.0f : 0.5f));
    for (size_t i = 0; i < labs.size(); i++) {
        RectF b(xs[i], r.Y + 5, w[i] + extra, r.Height - 10);
        bool on = (int)i == sel, hot = enabled && g_uiHot == id0 + (int)i;
        Text(g, labs[i], b, px, FontStyleBold, on ? WithA(g_soft.accInk, enabled ? 1.0f : 0.8f) : WithA(hot ? g_soft.ink : g_soft.muted, enabled ? 1.0f : 0.5f));
        if (enabled) HotZone(b, id0 + (int)i);
    }
}
// Champ : creux, texte ou texte d'attente ; curseur quand il a le focus.
static void SoftField(Graphics &g, RectF r, const std::wstring &text, const std::wstring &placeholder, bool focus, int icon = -1)
{
    SoftShape(g, r, kSoftRCtl, g_soft.bgIn);
    { GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, r, kSoftRCtl); g.SetClip(&p); SoftInset(g, r, kSoftRCtl); g.Restore(st); }
    if (focus) { Pen fp(g_soft.acc, 2.0f); GraphicsPath p; RoundRect(p, RectF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), kSoftRCtl - 1); g.DrawPath(&fp, &p); }
    float x = r.X + 14;
    if (icon >= 0) { Icon(g, icon, x + 9, r.Y + r.Height / 2, 18, g_soft.faint, 1.9f); x += 28; }
    std::wstring s = text.empty() ? placeholder : text;
    Text(g, s, RectF(x, r.Y, r.Width - (x - r.X) - 12, r.Height), 14, text.empty() ? FontStyleRegular : FontStyleBold, text.empty() ? g_soft.faint : g_soft.ink, StringAlignmentNear);
    if (focus && (GetTickCount() / 500) % 2 == 0) { float cx = x + (text.empty() ? 0 : MeasureW(MeasureG(), text, 14, FontStyleBold)) + 1; Pen cp(g_soft.ink, 1.5f); g.DrawLine(&cp, cx, r.Y + 12, cx, r.Y + r.Height - 12); }
    if (focus) InvalidateAll();   // (clignotement)
}
enum { SC_OK, SC_WARN, SC_BAD, SC_ACC };   // puces : toujours avec un texte
static float SoftChipW(const std::wstring &s) { return MeasureW(MeasureG(), s, 12, FontStyleBold) + 20; }
static float SoftChip(Graphics &g, float x, float y, const std::wstring &s, int kind, int icon = -1)
{
    Color fg = kind == SC_OK ? g_soft.ok : kind == SC_WARN ? g_soft.warn : kind == SC_BAD ? g_soft.bad : g_soft.acc;
    Color bg = kind == SC_OK ? g_soft.okBg : kind == SC_WARN ? g_soft.warnBg : kind == SC_BAD ? g_soft.badBg : g_soft.accSoft;
    float w = SoftChipW(s) + (icon >= 0 ? 16 : 0), h = 24;
    SoftShape(g, RectF(x, y, w, h), h / 2, bg);
    float tx = x + 10;
    if (icon >= 0) { Icon(g, icon, tx + 6, y + h / 2, 13, fg, 2.0f); tx += 16; }
    Text(g, s, RectF(tx, y, w - (tx - x) - 10, h), 12, FontStyleBold, fg, StringAlignmentNear);
    return w;
}
static void SoftProgress(Graphics &g, RectF r, float frac)   // creux + barre accent (frac < 0 : indeterminee)
{
    SoftShape(g, r, r.Height / 2, g_soft.bgIn);
    { GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, r, r.Height / 2); g.SetClip(&p); SoftInset(g, r, r.Height / 2, 2, 5); g.Restore(st); }
    if (frac < 0) { float t = (GetTickCount() % 1400) / 1400.0f, w = r.Width * 0.3f, x = r.X + (r.Width + w) * t - w; GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, r, r.Height / 2); g.SetClip(&p); SoftShape(g, RectF(x, r.Y, w, r.Height), r.Height / 2, g_soft.acc); g.Restore(st); InvalidateAll(); return; }
    float w = r.Width * min(max(frac, 0.0f), 1.0f);
    if (w > 0) SoftShape(g, RectF(r.X, r.Y, max(w, r.Height), r.Height), r.Height / 2, g_soft.acc);
}
static void SoftSlider(Graphics &g, RectF r, float frac, int id, bool enabled = true)   // piste creuse, poignee en relief
{
    float cy = r.Y + r.Height / 2, h = 10;
    RectF track(r.X, cy - h / 2, r.Width, h);
    SoftShape(g, track, h / 2, g_soft.bgIn);
    { GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, track, h / 2); g.SetClip(&p); SoftInset(g, track, h / 2, 2, 5); g.Restore(st); }
    float x = r.X + r.Width * min(max(frac, 0.0f), 1.0f);
    SoftShape(g, RectF(r.X, cy - h / 2, max(x - r.X, h), h), h / 2, WithA(g_soft.acc, enabled ? 1.0f : 0.5f));
    float d = 24;
    SoftRaised(g, RectF(x - d / 2, cy - d / 2, d, d), d / 2, 2, 6);
    SolidBrush k(g_soft.bg); g.FillEllipse(&k, x - d / 2, cy - d / 2, d, d);
    SolidBrush c(g_soft.acc); g.FillEllipse(&c, x - 5, cy - 5, 10.0f, 10.0f);
    if (enabled) HotZone(RectF(r.X - 12, cy - 16, r.Width + 24, 32), id);
}
// Tuile : grande carte cliquable, creuse quand choisie ; icone dans un carre accent doux.
static void SoftTile(Graphics &g, RectF r, const std::wstring &title, const std::wstring &sub, int icon, bool selected, int id, bool enabled = true)
{
    bool hot = enabled && g_uiHot == id;
    float press = enabled ? SoftPress(id) : 0;
    RectF rr = r;
    if (press > 0) { float k = 1 - 0.025f * press; rr = RectF(r.X + r.Width * (1 - k) / 2, r.Y + r.Height * (1 - k) / 2, r.Width * k, r.Height * k); }
    if (selected || press > 0.5f) { SoftShape(g, rr, 20, g_soft.bgIn); GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, rr, 20); g.SetClip(&p); SoftInset(g, rr, 20, 4, 9); g.Restore(st); }
    else { SoftRaised(g, rr, 20, 6, 14, enabled ? 1.0f : 0.5f); SoftShape(g, rr, 20, g_soft.bg); }
    if (hot && !selected) { Pen hp(WithA(g_soft.acc, 0.5f), 1.2f); GraphicsPath p; RoundRect(p, rr, 20); g.DrawPath(&hp, &p); }
    RectF sq(rr.X + 18, rr.Y + 18, 46, 46);
    SoftShape(g, sq, 14, g_soft.accSoft);
    Icon(g, icon, sq.X + 23, sq.Y + 23, 24, g_soft.acc, 2.0f);
    Text(g, title, RectF(rr.X + 18, rr.Y + 74, rr.Width - 36, 24), 17, FontStyleBold, WithA(g_soft.ink, enabled ? 1.0f : 0.5f), StringAlignmentNear);
    Para(g, sub, RectF(rr.X + 18, rr.Y + 100, rr.Width - 36, rr.Height - 108), 13, WithA(g_soft.muted, enabled ? 1.0f : 0.5f));
    if (enabled) HotZone(r, id);
}
// Rangee de liste : ligne fine en dessous, surlignage au survol (pas une carte par element).
static void SoftRow(Graphics &g, RectF r, const std::wstring &title, const std::wstring &sub, int id, int icon = -1)
{
    bool hot = id >= 0 && g_uiHot == id;
    if (hot) SoftShape(g, r, 12, g_soft.accSoft);
    float x = r.X + 12;
    if (icon >= 0) { Icon(g, icon, x + 10, r.Y + r.Height / 2, 20, g_soft.muted, 1.9f); x += 32; }
    Text(g, title, RectF(x, r.Y + (sub.empty() ? 0 : 6), r.Width - (x - r.X) - 12, sub.empty() ? r.Height : 22), 14, FontStyleBold, g_soft.ink, StringAlignmentNear);
    if (!sub.empty()) Text(g, sub, RectF(x, r.Y + 28, r.Width - (x - r.X) - 12, 18), 12.5f, FontStyleRegular, g_soft.muted, StringAlignmentNear);
    Pen ln(g_soft.line, 1.0f); g.DrawLine(&ln, r.X + 8, r.Y + r.Height, r.X + r.Width - 8, r.Y + r.Height);
    if (id >= 0) HotZone(r, id);
}
static void SoftSkeleton(Graphics &g, RectF r, float rad = 10)   // chargement (> 300 ms) : bande qui respire
{
    float t = (GetTickCount() % 1600) / 1600.0f, a = 0.5f + 0.3f * sinf(t * 6.2832f);
    SoftShape(g, r, rad, WithA(g_soft.bgIn, a));
    InvalidateAll();
}
static void SoftTooltip(Graphics &g, RectF anchor, const std::wstring &s)   // sous l'element, centree
{
    float w = MeasureW(MeasureG(), s, 12.5f, FontStyleBold) + 24, h = 30;
    RectF r(anchor.X + anchor.Width / 2 - w / 2, anchor.Y + anchor.Height + 8, w, h);
    SoftRaised(g, r, 10, 3, 8); SoftShape(g, r, 10, g_soft.bg);
    Text(g, s, r, 12.5f, FontStyleBold, g_soft.ink);
}
static void SoftVeil(Graphics &g, float a = 0.45f) { SolidBrush b(Color((BYTE)(255 * a), 0, 0, 0)); g.FillPath(&b, CardPath()); }
// Fenetre modale : apparait depuis son declencheur (96 % -> 100 %, 220 ms) ; 'open' anime le voile (180 ms). Rend l'echelle.
static float SoftModalFrame(Graphics &g, bool open, int key)
{
    float t = AnimTo(AK(AKH_MISC, key), open ? 1.0f : 0.0f, kSoftTUi, SE_OUT);
    if (t <= 0) return 0;
    SoftVeil(g, 0.45f * t);
    return 0.96f + 0.04f * t;
}

// ---- notifications (en bas a droite de la carte, 2,6 s, sans voler le focus) ; le texte reste aussi dans la barre d'etat
struct SoftToast { std::wstring text; int kind; DWORD t0; int id; };
static std::vector<SoftToast> g_softToasts; static int g_softToastN;
static void SoftNotify(const std::wstring &text, int kind = SC_OK)
{
    g_softToasts.push_back({ text, kind, GetTickCount(), ++g_softToastN });
    if (g_softToasts.size() > 4) g_softToasts.erase(g_softToasts.begin());
    InvalidateAll();
}
static void SoftDrawToasts(Graphics &g, float areaW = kCardW, float areaH = kCardH)
{
    if (g_softToasts.empty()) return;
    DWORD now = GetTickCount();
    int in = SoftDur(260), out = SoftDur(160), life = 2600;
    for (size_t i = 0; i < g_softToasts.size();) { if (now - g_softToasts[i].t0 > (DWORD)(life + out)) g_softToasts.erase(g_softToasts.begin() + i); else i++; }
    float y = areaH - 12;
    for (int i = (int)g_softToasts.size() - 1; i >= 0; i--) {
        SoftToast &t = g_softToasts[i];
        DWORD el = now - t.t0;
        float a = 1, dx = 0, dy = 0, sc = 1;
        if (el < (DWORD)in) { float e = SoftEase(el / (float)in, SE_OUT); a = e; dy = 12 * (1 - e); sc = 0.96f + 0.04f * e; }
        else if (el > (DWORD)life) { float e = SoftEase((el - life) / (float)out, SE_MOVE); a = 1 - e; dx = 16 * e; }
        float w = MeasureW(MeasureG(), t.text, 13.5f, FontStyleBold) + 56, h = 44;
        w = min(w, areaW * 0.6f);
        RectF r(areaW - 24 - w + dx, y - h + dy, w, h);
        RectF rs(r.X + r.Width * (1 - sc) / 2, r.Y + r.Height * (1 - sc) / 2, r.Width * sc, r.Height * sc);
        SoftRaised(g, rs, 14, 4, 10, a); SoftShape(g, rs, 14, WithA(g_soft.bg, a));
        Color fg = t.kind == SC_OK ? g_soft.ok : t.kind == SC_WARN ? g_soft.warn : t.kind == SC_BAD ? g_soft.bad : g_soft.acc;
        Icon(g, t.kind == SC_OK ? IC_CHECK : IC_ALERT, rs.X + 22, rs.Y + rs.Height / 2, 18, WithA(fg, a), 2.2f);
        Text(g, t.text, RectF(rs.X + 40, rs.Y, rs.Width - 52, rs.Height), 13.5f, FontStyleBold, WithA(g_soft.ink, a), StringAlignmentNear);
        y -= h + 10;
    }
    InvalidateAll();
}

// ---- changement de page (sens = position dans la barre) et cercle du theme : composes dans PresentNew sur la carte rendue
static Bitmap *g_softPageOld;   // la zone de page de l'image precedente, au moment du changement
static int g_softPageDir;       // +1 : la nouvelle page est plus bas dans la barre (elle monte en apparaissant), -1 : plus haut
static DWORD g_softPageT;       // debut du changement (0 : aucun)
static Bitmap *g_softThemeOld;  // l'image entiere avant le changement de theme
static DWORD g_softThemeT; static PointF g_softThemeC;
static int SoftNavIndex(int tab);   // (ui.inc : place de la page dans la barre, -1 si absente)
static void SoftPageChange(int from, int to)
{
    if (!g_newUi || !g_cardBmp || from == to || AnimReduced()) return;
    int a = SoftNavIndex(from), b = SoftNavIndex(to);
    g_softPageDir = a < 0 || b < 0 ? 1 : b > a ? 1 : b < a ? -1 : 0;
    delete g_softPageOld;
    g_softPageOld = g_cardBmp->Clone(0, 0, (INT)g_cardBmp->GetWidth(), (INT)g_cardBmp->GetHeight(), PixelFormat32bppPARGB);
    g_softPageT = GetTickCount();
    InvalidateAll();
}
static void SoftThemeCircle(PointF from)   // (bouton Theme : la nouvelle image decoupee par un cercle qui grandit, 420 ms)
{
    if (!g_newUi || !g_cardBmp || AnimReduced()) return;
    delete g_softThemeOld;
    g_softThemeOld = g_cardBmp->Clone(0, 0, (INT)g_cardBmp->GetWidth(), (INT)g_cardBmp->GetHeight(), PixelFormat32bppPARGB);
    g_softThemeT = GetTickCount(); g_softThemeC = from;
    InvalidateAll();
}
// Apres RenderTo dans PresentNew : 'card' est l'image courante (nouvelle page / nouveau theme), composee avec l'ancienne.
static void SoftCompose(Bitmap &card)
{
    DWORD now = GetTickCount();
    if (g_softThemeT) {   // (pendant la bascule du theme, aucune autre transition)
        int dur = SoftDur(420);
        float t = min(1.0f, (now - g_softThemeT) / (float)dur), e = SoftEase(t, SE_OUT);
        if (t >= 1 || !g_softThemeOld) { g_softThemeT = 0; delete g_softThemeOld; g_softThemeOld = NULL; g_softPageT = 0; return; }
        Bitmap cur(card.GetWidth(), card.GetHeight(), PixelFormat32bppPARGB);
        { Graphics gc(&cur); gc.DrawImage(&card, 0, 0, (INT)card.GetWidth(), (INT)card.GetHeight()); }
        Graphics g(&card);
        g.SetCompositingMode(CompositingModeSourceCopy);
        g.DrawImage(g_softThemeOld, 0, 0, (INT)card.GetWidth(), (INT)card.GetHeight());
        float cx = g_offX + (g_softThemeC.X + kM) * g_scale, cy = g_offY + (g_softThemeC.Y + kM) * g_scale;
        float farR = sqrtf(max(cx, card.GetWidth() - cx) * max(cx, card.GetWidth() - cx) + max(cy, card.GetHeight() - cy) * max(cy, card.GetHeight() - cy)) * e;
        GraphicsPath p; p.AddEllipse(cx - farR, cy - farR, 2 * farR, 2 * farR);
        g.SetClip(&p);
        g.DrawImage(&cur, 0, 0, (INT)card.GetWidth(), (INT)card.GetHeight());
        InvalidateAll();
        return;
    }
    if (!g_softPageT) return;
    int din = SoftDur(kSoftTPage), dout = SoftDur(170);
    float t = min(1.0f, (now - g_softPageT) / (float)din);
    if (t >= 1 || !g_softPageOld) { g_softPageT = 0; delete g_softPageOld; g_softPageOld = NULL; return; }
    float ein = SoftEase(t, SE_OUT), eout = SoftEase(min(1.0f, (now - g_softPageT) / (float)dout), SE_MOVE);
    RectF page(g_offX + (264 + kM) * g_scale, g_offY + (0 + kM) * g_scale, 1000 * g_scale, 744 * g_scale);   // (la colonne de page : titre, commandes et page de la carte d'avant)
    Bitmap cur(card.GetWidth(), card.GetHeight(), PixelFormat32bppPARGB);
    { Graphics gc(&cur); gc.DrawImage(&card, 0, 0, (INT)card.GetWidth(), (INT)card.GetHeight()); }
    Graphics g(&card);
    g.SetClip(page);
    SolidBrush bg(WinBg()); g.FillRectangle(&bg, page);   // (fond uni derriere les deux pages)
    ColorMatrix mo = { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1 - eout, 0, 0, 0, 0, 0, 1 };
    ImageAttributes ao; ao.SetColorMatrix(&mo);
    float dyo = -14 * g_softPageDir * g_scale * eout;
    g.DrawImage(g_softPageOld, RectF(0, dyo, (float)card.GetWidth(), (float)card.GetHeight()), 0, 0, (float)card.GetWidth(), (float)card.GetHeight(), UnitPixel, &ao);
    ColorMatrix mi = { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, ein, 0, 0, 0, 0, 0, 1 };
    ImageAttributes ai; ai.SetColorMatrix(&mi);
    float dyi = 22 * g_softPageDir * g_scale * (1 - ein);
    g.DrawImage(&cur, RectF(0, dyi, (float)card.GetWidth(), (float)card.GetHeight()), 0, 0, (float)card.GetWidth(), (float)card.GetHeight(), UnitPixel, &ai);
    InvalidateAll();
}

// ---- planche d'essai : tous les composants, dans le theme et l'accent demandes (/capture <png> composants [/theme ...] [/msc])
enum { UI_SOFT_DEMO = 10050 };   // + 0..19 : zones de la planche
static void DrawSoftDemo(Graphics &g)
{
    SoftThemeApply();
    GraphicsState st = g.Save();
    g.SetClip(CardPath());
    SolidBrush bg(g_soft.bg); g.FillRectangle(&bg, RectF(0, 0, kCardW, kCardH));
    Text(g, g_game == GAME_MSC ? L"My Summer Car" : L"My Winter Car", RectF(32, 22, 500, 36), 28, FontStyleBold, g_soft.ink, StringAlignmentNear);
    Text(g, T(L"Composants Soft UI · planche d'essai (lot L1)", L"Soft UI components · test sheet (lot L1)"), RectF(32, 60, 700, 20), 13, FontStyleRegular, g_soft.muted, StringAlignmentNear);
    {   // puces d'etat
        float x = 760;
        x += SoftChip(g, x, 32, T(L"Mod à jour", L"Mod up to date"), SC_OK, IC_CHECK) + 8;
        x += SoftChip(g, x, 32, T(L"Mise à jour…", L"Updating…"), SC_WARN) + 8;
        x += SoftChip(g, x, 32, T(L"Hors ligne", L"Offline"), SC_BAD) + 8;
        SoftChip(g, x, 32, L"Steam", SC_ACC);
    }
    // carte 1 : boutons
    RectF c1(32, 96, 400, 300); SoftCard(g, c1, kSoftRCard); SoftCardTitle(g, c1, T(L"Boutons", L"Buttons"), T(L"Relief, principal, icône, appuyé, désactivé", L"Raised, primary, icon, pressed, disabled"));
    SoftButton(g, RectF(c1.X + 22, c1.Y + 70, 160, 44), T(L"Héberger", L"Host"), UI_SOFT_DEMO, true, IC_HOME);
    SoftButton(g, RectF(c1.X + 196, c1.Y + 70, 160, 44), T(L"Rejoindre", L"Join"), UI_SOFT_DEMO + 1, false, IC_LOGIN);
    SoftButton(g, RectF(c1.X + 22, c1.Y + 128, 44, 44), L"", UI_SOFT_DEMO + 2, false, IC_GEAR);
    SoftButton(g, RectF(c1.X + 78, c1.Y + 128, 44, 44), L"", UI_SOFT_DEMO + 3, true, IC_PLUS);
    g_softPressId = UI_SOFT_DEMO + 4; g_softPressT = GetTickCount();   // (appuye : montre l'etat)
    SoftButton(g, RectF(c1.X + 134, c1.Y + 128, 120, 44), T(L"Appuyé", L"Pressed"), UI_SOFT_DEMO + 4, false);
    g_softPressId = -1;
    SoftButton(g, RectF(c1.X + 266, c1.Y + 128, 112, 44), T(L"Inactif", L"Disabled"), UI_SOFT_DEMO + 5, false, -1, false);
    SoftProgress(g, RectF(c1.X + 22, c1.Y + 198, 356, 12), 0.62f);
    SoftSlider(g, RectF(c1.X + 34, c1.Y + 230, 332, 32), 0.4f, UI_SOFT_DEMO + 6);
    // carte 2 : interrupteurs, segments, champ
    RectF c2(456, 96, 400, 300); SoftCard(g, c2, kSoftRCard); SoftCardTitle(g, c2, T(L"Réglages", L"Settings"), T(L"Interrupteurs, segments, champ", L"Switches, segments, field"));
    Text(g, T(L"Vocal de proximité", L"Proximity voice"), RectF(c2.X + 22, c2.Y + 76, 240, 22), 14, FontStyleBold, g_soft.ink, StringAlignmentNear);
    SoftToggle(g, RectF(c2.X + c2.Width - 74, c2.Y + 72, 52, 30), true, UI_SOFT_DEMO + 7);
    Text(g, T(L"Animations réduites", L"Reduced animations"), RectF(c2.X + 22, c2.Y + 118, 240, 22), 14, FontStyleBold, g_soft.ink, StringAlignmentNear);
    SoftToggle(g, RectF(c2.X + c2.Width - 74, c2.Y + 114, 52, 30), false, UI_SOFT_DEMO + 8);
    Pen ln(g_soft.line, 1); g.DrawLine(&ln, c2.X + 22, c2.Y + 156, c2.X + c2.Width - 22, c2.Y + 156);
    std::vector<std::wstring> labs = { T(L"Clair", L"Light"), T(L"Sombre", L"Dark"), L"Auto" };
    SoftSeg(g, RectF(c2.X + 22, c2.Y + 170, SoftSegW(labs), 44), labs, g_softMode < 0 ? 2 : g_softMode, UI_SOFT_DEMO + 9);
    SoftField(g, RectF(c2.X + 22, c2.Y + 232, 356, 46), L"", T(L"Chercher un réglage…", L"Search a setting…"), false, IC_SPARK);
    // carte 3 : tuiles et rangees
    RectF c3(880, 96, 368, 300); SoftCard(g, c3, kSoftRCard); SoftCardTitle(g, c3, T(L"Tuiles et listes", L"Tiles and lists"), L"");
    SoftTile(g, RectF(c3.X + 22, c3.Y + 56, 154, 150), T(L"Voitures", L"Cars"), T(L"Addons, livrées", L"Add-ons, liveries"), IC_CAR, false, UI_SOFT_DEMO + 10);
    SoftTile(g, RectF(c3.X + 192, c3.Y + 56, 154, 150), L"Mods", T(L"Kupla…", L"Kupla…"), IC_PUZZLE, true, UI_SOFT_DEMO + 11);
    SoftRow(g, RectF(c3.X + 14, c3.Y + 218, c3.Width - 28, 34), L"mwcoop.log", L"", UI_SOFT_DEMO + 12, IC_FILE);
    SoftRow(g, RectF(c3.X + 14, c3.Y + 254, c3.Width - 28, 34), L"chargeur.log", L"", UI_SOFT_DEMO + 13, IC_FILE);
    // carte 4 : texte, squelette, info-bulle, modale
    RectF c4(32, 420, 400, 300); SoftCard(g, c4, kSoftRCard); SoftCardTitle(g, c4, T(L"Texte", L"Text"), T(L"Nunito 700 / 400 · étiquettes en capitales", L"Nunito 700 / 400 · uppercase labels"));
    Text(g, T(L"ÉTIQUETTE DE SECTION", L"SECTION LABEL"), RectF(c4.X + 22, c4.Y + 70, 300, 16), 11, FontStyleBold, g_soft.faint, StringAlignmentNear);
    Para(g, T(L"Texte courant à 14 px, contraste ≥ 4,5:1 sur le fond. Le secondaire est en 13 px gris. Chaque état a une couleur ET un mot.", L"Body text at 14 px, contrast ≥ 4.5:1 on the background. Secondary is 13 px grey. Every state has a colour AND a word."), RectF(c4.X + 22, c4.Y + 92, 356, 70), 14, g_soft.ink);
    Text(g, L"1 234 567 · 97 % · v1.15.19", RectF(c4.X + 22, c4.Y + 166, 356, 20), 13, FontStyleRegular, g_soft.muted, StringAlignmentNear);
    SoftSkeleton(g, RectF(c4.X + 22, c4.Y + 200, 240, 14)); SoftSkeleton(g, RectF(c4.X + 22, c4.Y + 222, 320, 14)); SoftSkeleton(g, RectF(c4.X + 22, c4.Y + 244, 180, 14));
    // carte 5 : couleurs des jetons
    RectF c5(456, 420, 400, 300); SoftCard(g, c5, kSoftRCard); SoftCardTitle(g, c5, T(L"Jetons", L"Tokens"), g_softDark ? T(L"Sombre", L"Dark") : T(L"Clair", L"Light"));
    struct Sw { const wchar_t *n; Color c; } sw[] = { { L"bg", g_soft.bg }, { L"bg-in", g_soft.bgIn }, { L"ink", g_soft.ink }, { L"muted", g_soft.muted }, { L"faint", g_soft.faint }, { L"acc", g_soft.acc }, { L"acc-soft", g_soft.accSoft }, { L"ok", g_soft.ok }, { L"warn", g_soft.warn }, { L"bad", g_soft.bad }, { L"sh-d", g_soft.shD }, { L"sh-l", g_soft.shL } };
    for (int i = 0; i < 12; i++) {
        float x = c5.X + 22 + (i % 4) * 90, y = c5.Y + 70 + (i / 4) * 72;
        SoftShape(g, RectF(x, y, 78, 40), 10, sw[i].c);
        Pen e(g_soft.line, 1); GraphicsPath p; RoundRect(p, RectF(x, y, 78, 40), 10); g.DrawPath(&e, &p);
        Text(g, sw[i].n, RectF(x, y + 44, 78, 16), 11.5f, FontStyleBold, g_soft.muted);
    }
    // carte 6 : notification et modale
    RectF c6(880, 420, 368, 300); SoftCard(g, c6, kSoftRCard); SoftCardTitle(g, c6, T(L"Modale, notification", L"Modal, notification"), L"");
    RectF m(c6.X + 22, c6.Y + 60, c6.Width - 44, 150);
    SoftShape(g, m, 16, WithA(Color(255, 0, 0, 0), 0.25f));
    RectF mm(m.X + 30, m.Y + 22, m.Width - 60, 106);
    SoftRaised(g, mm, 18, 7, 16); SoftShape(g, mm, 18, g_soft.bg);
    Text(g, T(L"Supprimer la partie ?", L"Delete the game?"), RectF(mm.X + 16, mm.Y + 14, mm.Width - 32, 22), 15, FontStyleBold, g_soft.ink, StringAlignmentNear);
    SoftButton(g, RectF(mm.X + 16, mm.Y + 50, 110, 40), T(L"Annuler", L"Cancel"), UI_SOFT_DEMO + 14, false);
    SoftButton(g, RectF(mm.X + mm.Width - 126, mm.Y + 50, 110, 40), T(L"Supprimer", L"Delete"), UI_SOFT_DEMO + 15, true);
    SoftTooltip(g, RectF(c6.X + 60, c6.Y + 214, 120, 10), T(L"Info-bulle", L"Tooltip"));
    if (g_softToasts.empty()) { g_softToasts.push_back({ T(L"Réglages enregistrés", L"Settings saved"), SC_OK, GetTickCount() - 400, 1 }); }
    SoftDrawToasts(g);
    g.Restore(st);
}

// ---- essai cache (appele par WinSelfTest, /testfenetre) : courbes, valeurs animees interruptibles, mode reduit,
// transitions composees sur la fenetre jamais montree (temps par image), taille du cache des ombres.
static void SoftSelfTest()
{
    LaunchLog("testsoft : e-out(0.25)=%.3f e-out(0.5)=%.3f e-move(0.5)=%.3f lineaire(0.5)=%.3f (attendu : montee rapide, > 0.5)", SoftEase(0.25f, SE_OUT), SoftEase(0.5f, SE_OUT), SoftEase(0.5f, SE_MOVE), SoftEase(0.5f, SE_LINEAR));
    float v0 = AnimTo(AK(AKH_MISC, 999), 0), v1 = AnimTo(AK(AKH_MISC, 999), 100);   // (premiere fois : a la cible ; puis cible 100 : repart de 0)
    Sleep(110); float v2 = AnimTo(AK(AKH_MISC, 999), 100);
    float v3 = AnimTo(AK(AKH_MISC, 999), 0);   // (interrompue : repart de la valeur courante vers 0)
    Sleep(260); float v4 = AnimTo(AK(AKH_MISC, 999), 0);
    LaunchLog("testsoft : anim 0 -> 100 : depart %.0f, a 0 ms %.0f, a 110 ms %.0f ; retour vers 0 depuis %.0f, a 260 ms %.0f (attendu 0)", v0, v1, v2, v3, v4);
    SoftThemeApply();
    DWORD t0 = GetTickCount();
    for (int i = 0; i < 6; i++) { SoftShadowFor(22, 16, g_soft.shD); SoftShadowFor(22, 16, g_soft.shL); SoftShadowFor(14, 10, g_soft.shD); SoftShadowFor(14, 10, g_soft.shL); }
    LaunchLog("testsoft : ombres en cache : %d bitmaps, %lu ms pour les construire (puis 0)", (int)g_softShadows.size(), GetTickCount() - t0);
    LARGE_INTEGER f, a, z; QueryPerformanceFrequency(&f);
    int from = g_tab; g_tab = TAB_HOME; InvalidateAll(); PresentNew();
    SoftPageChange(TAB_HOME, TAB_WIKI); g_tab = TAB_WIKI;
    double worst = 0; int frames = 0;
    while (g_softPageT) { InvalidateAll(); QueryPerformanceCounter(&a); PresentNew(); QueryPerformanceCounter(&z); double ms = (z.QuadPart - a.QuadPart) * 1000.0 / f.QuadPart; if (ms > worst) worst = ms; frames++; Sleep(16); }
    LaunchLog("testsoft : changement de page Accueil -> Guide (sens %+d) : %d images composees, la plus longue %.1f ms", g_softPageDir, frames, worst);
    SoftThemeCircle(PointF(1150, 44)); SoftModeSet(g_softDark ? 0 : 1);
    worst = 0; frames = 0;
    while (g_softThemeT) { InvalidateAll(); QueryPerformanceCounter(&a); PresentNew(); QueryPerformanceCounter(&z); double ms = (z.QuadPart - a.QuadPart) * 1000.0 / f.QuadPart; if (ms > worst) worst = ms; frames++; Sleep(16); }
    LaunchLog("testsoft : cercle du theme : %d images, la plus longue %.1f ms, theme maintenant %s", frames, worst, g_softDark ? "sombre" : "clair");
    SoftModeSet(2); g_tab = from;
    SoftNotify(L"essai", SC_OK);
    LaunchLog("testsoft : notification posee (%d), animations reduites : %s", (int)g_softToasts.size(), AnimReduced() ? "oui" : "non");
}
