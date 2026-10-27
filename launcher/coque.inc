// ---------------------------------------------------------------- COQUE de la nouvelle interface (refonte Soft UI, lot L2)
// Spec §4 (disposition), §8 (palette Ctrl+K, barre d'etat), §1 (ce qui ne bouge pas). Inclus apres tuto.inc : il se sert
// des pages, de la barre (kNav, RailFolded), des reglages (kSetItems), du Guide (kWikiArt) et des bulles (DrawTuto).
// Deux reperes :
//  - la coque (en-tete 64, barre de gauche 248 / 92 repliee, barre d'etat 40, zone de page) se dessine en DIP (pixels / PPP
//    de la fenetre : g_dpiScale) ; ses zones cliquables et ses boutons sont enregistres en coordonnees de la carte (DipToCard)
//    pour que WndProc, ZoneAt, HitButton, les bulles de la visite (g_navR) marchent sans changement ;
//  - la colonne de page = la zone de contenu de la carte d'avant (x 264..1264, y 0..744 : ligne de titre et commandes de la
//    page, puis la page) dessinee a l'echelle qui tient (g_scale, g_offX / g_offY : WinLayout -> ShellLayout), en haut a
//    gauche de la zone de page. Les pages migrent une a une vers les composants Soft (L3 / L4) ; en attendant, leurs cartes
//    de verre (Glass) deviennent des cartes en relief.
// Moteur de disposition (LayoutRun) : rangees / colonnes a poids, minimum et maximum, calcule a chaque changement de taille.
// Points de rupture (largeur en DIP) : < 1100 logo reduit, selecteur court, recherche = loupe ; < 1000 barre repliee (auto).
static RectF g_shHeader, g_shRail, g_shPage, g_shStatus;   // (DIP)
static float g_railWidth = 248;                            // largeur courante de la barre (animee)
static int g_railManual = -1;                              // -1 auto (repliee sous 1000), 0 depliee, 1 repliee ([Lanceur] BarreRepliee)
static bool g_capNew; static int g_capW = 1400, g_capH = 860;   // /capture ... /nouvelle [/taille LxH]

// ---- moteur de disposition : cellules a poids / min / max sur une longueur, avec un espace entre elles
struct LayCell { float weight, min, max; float pos, size; };
static void LayoutRun(std::vector<LayCell> &c, float start, float total, float gap)
{
    float fixed = gap * max(0, (int)c.size() - 1), wsum = 0;
    for (auto &x : c) { x.size = x.min; fixed += x.min; wsum += x.weight; }
    float rest = total - fixed;
    for (int it = 0; it < 8 && rest > 0.5f && wsum > 0; it++) {   // distribue au poids, plafonne au maximum, reboucle avec le reste
        float give = rest, w2 = 0; rest = 0;
        for (auto &x : c) {
            if (x.weight <= 0 || x.size >= x.max) continue;
            float add = give * x.weight / wsum, ns = min(x.max, x.size + add);
            rest += add - (ns - x.size); x.size = ns;
            if (x.size < x.max) w2 += x.weight;
        }
        wsum = w2;
    }
    float p = start;
    for (auto &x : c) { x.pos = p; p += x.size + gap; }
}
// Reperes : DIP <-> carte
static RectF DipToCard(RectF r)
{
    float d = g_dpiScale;
    return RectF((r.X * d - g_offX) / g_scale - kM, (r.Y * d - g_offY) / g_scale - kM, r.Width * d / g_scale, r.Height * d / g_scale);
}
static bool RailCollapsedWanted() { return g_railManual < 0 ? g_shW < 1000 : g_railManual == 1; }
static void RailManualRead() { if (g_railManual == -1) { int v = GetPrivateProfileIntW(L"Lanceur", L"BarreRepliee", -1, g_iniLauncher.c_str()); g_railManual = v == 0 || v == 1 ? v : -2; } }
static void RailCollapseToggle()
{
    RailManualRead();
    bool now = RailCollapsedWanted();
    g_railManual = now ? 0 : 1;
    WritePrivateProfileStringW(L"Lanceur", L"BarreRepliee", g_railManual ? L"1" : L"0", g_iniLauncher.c_str());
    InvalidateAll();
}
static void ShellLayout()
{
    RailManualRead();
    if (g_railManual == -2) g_railManual = -1;
    float d = g_dpiScale;
    g_shW = g_winW / d; g_shH = g_winH / d;
    float railW = AnimTo(AK(AKH_MISC, 7001), RailCollapsedWanted() ? 92.0f : 248.0f, kSoftTUi, SE_MOVE);
    g_railWidth = railW;
    std::vector<LayCell> rows = { { 0, 64, 64 }, { 1, 200, 1e9f }, { 0, 40, 40 } };
    LayoutRun(rows, 0, g_shH, 0);
    std::vector<LayCell> cols = { { 0, railW, railW }, { 1, 300, 1e9f } };
    LayoutRun(cols, 0, g_shW, 0);
    g_shHeader = RectF(0, 0, g_shW, rows[0].size);
    g_shRail = RectF(0, rows[1].pos, railW, rows[1].size);
    g_shPage = RectF(cols[1].pos, rows[1].pos, cols[1].size, rows[1].size);
    g_shStatus = RectF(0, rows[2].pos, g_shW, rows[2].size);
    // colonne de page : la carte d'avant (x 264..1264, y 0..744) a l'echelle qui tient, en haut a gauche, marges 8
    float cx = (g_shPage.X + 18) * d, cy = g_shPage.Y * d, cw = (g_shPage.Width - 26) * d, ch = (g_shPage.Height - 6) * d;   // (18 : le bouton de repli de la barre deborde de 14)
    float s = max(0.3f, min(cw / 1000.0f, ch / 744.0f));
    g_scale = s;
    g_offX = (int)(cx - (264 + kM) * s); g_offY = (int)(cy - (0 + kM) * s);
}
// ---- petits dessins
static void DrawSnowflake(Graphics &g, float cx, float cy, float r, Color c)
{
    Pen p(c, 2.0f); p.SetStartCap(LineCapRound); p.SetEndCap(LineCapRound);
    for (int i = 0; i < 3; i++) {
        float a = 3.14159f / 3 * i, dx = cosf(a) * r, dy = sinf(a) * r;
        g.DrawLine(&p, cx - dx, cy - dy, cx + dx, cy + dy);
        for (int s = -1; s <= 1; s += 2) { float bx = cx + dx * 0.55f * s, by = cy + dy * 0.55f * s, a2 = a + 3.14159f / 5, a3 = a - 3.14159f / 5; g.DrawLine(&p, bx, by, bx + cosf(a2) * r * 0.3f * s, by + sinf(a2) * r * 0.3f * s); g.DrawLine(&p, bx, by, bx + cosf(a3) * r * 0.3f * s, by + sinf(a3) * r * 0.3f * s); }
    }
}
static void DrawLoupe(Graphics &g, float cx, float cy, float r, Color c)
{
    Pen p(c, 2.0f); p.SetStartCap(LineCapRound); p.SetEndCap(LineCapRound);
    g.DrawEllipse(&p, cx - r, cy - r, 2 * r, 2 * r);
    g.DrawLine(&p, cx + r * 0.7f, cy + r * 0.7f, cx + r * 1.5f, cy + r * 1.5f);
}
// Bouton d'icone de la coque (40 x 40) : relief, icone ; 'hover' 0..1 (g_btn) ou survol de zone ; 'on' = page ouverte.
static void ShellIconBtn(Graphics &g, RectF r, int icon, float hover, bool on, bool danger, bool enabled = true)
{
    if (on) { SoftShape(g, r, 12, g_soft.bgIn); GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, r, 12); g.SetClip(&p); SoftInset(g, r, 12, 3, 6); g.Restore(st); }
    else { SoftRaised(g, r, 12, 3, 8, enabled ? 1.0f : 0.4f); SoftShape(g, r, 12, danger && hover > 0.01f ? Color((BYTE)255, (BYTE)(g_soft.bg.GetR() + (0xC0 - g_soft.bg.GetR()) * hover), (BYTE)(g_soft.bg.GetG() + (0x35 - g_soft.bg.GetG()) * hover), (BYTE)(g_soft.bg.GetB() + (0x2B - g_soft.bg.GetB()) * hover)) : g_soft.bg); }
    Color ink = on ? g_soft.acc : danger && hover > 0.5f ? Color(255, 255, 255, 255) : hover > 0.01f ? g_soft.acc : WithA(g_soft.ink, enabled ? 1.0f : 0.4f);
    if (icon >= 0) Icon(g, icon, r.X + r.Width / 2, r.Y + r.Height / 2, 18, ink, 2.0f);
}
static void ShellBtn(Graphics &g, RectF r, int id, int icon, bool danger = false)   // (un bouton g_btn[B_*] de la coque)
{
    Button &b = g_btn[id];
    b.r = DipToCard(r); b.visible = true;
    ShellIconBtn(g, r, icon, b.hover, false, danger, b.enabled);
}
static void ShellPage(Graphics &g, RectF r, int page, int icon)   // (raccourci de page de l'en-tete : API, Journaux, Signaler, Credits, Outils)
{
    bool en = PageEnabled(page), on = g_tab == page && g_state == ST_IDLE, hot = en && g_uiHot == UI_NAV + page;
    g_navR[page] = DipToCard(r);
    ShellIconBtn(g, r, page == TAB_CREDITS ? -1 : icon, hot ? 1.0f : 0.0f, on, false, en);
    if (page == TAB_CREDITS) RgbStar(g, r.X + r.Width / 2, r.Y + r.Height / 2, 20, g_time, on || hot);
    if (page == TAB_LOGS && g_crashT && !on) { RectF d(r.X + r.Width - 12, r.Y - 2, 14, 14); SolidBrush rb(g_soft.bad); g.FillEllipse(&rb, d); Pen e(g_soft.bg, 2); g.DrawEllipse(&e, d); }
    if (en) HotZone(r, UI_NAV + page);
}

// ---- en-tete : marque + selecteur de jeu | recherche Ctrl+K | raccourcis (inchanges) + Agrandir
static void ShellDrawHeader(Graphics &g)
{
    RectF h = g_shHeader;
    bool narrow = g_shW < 1100;
    Pen ln(g_soft.line, 1); g.DrawLine(&ln, h.X, h.Y + h.Height - 0.5f, h.X + h.Width, h.Y + h.Height - 0.5f);
    float x = 18, cy = h.Y + h.Height / 2;
    {   // notre logo (demande de JD, 27/10 : « mettre notre logo en haut a gauche au lieu de l'icone + texte ») : net a toute
        // echelle (une version reduite par hauteur en pixels, en cache), plus petit en fenetre etroite ; en sombre, un leger halo
        // clair derriere pour que le contour noir des lettres se detache
        static Bitmap *src; static Bitmap *cache; static int cacheH;
        if (!src) src = LoadPngRes(11);
        float lh = narrow ? 36.0f : 48.0f;
        if (src) {
            int ph = (int)(lh * g_dpiScale + 0.5f), pw = (int)(ph * (float)src->GetWidth() / src->GetHeight() + 0.5f);
            if (!cache || cacheH != ph) { delete cache; cache = new Bitmap(pw, ph, PixelFormat32bppPARGB); Graphics gc(cache); gc.SetInterpolationMode(InterpolationModeHighQualityBicubic); gc.SetPixelOffsetMode(PixelOffsetModeHalf); gc.DrawImage(src, RectF(0, 0, (float)pw, (float)ph), 0, 0, (float)src->GetWidth(), (float)src->GetHeight(), UnitPixel); cacheH = ph; }
            float lw = lh * pw / (float)ph;
            RectF lr(x, cy - lh / 2, lw, lh);
            if (g_softDark) SoftShape(g, RectF(lr.X - 6, lr.Y - 2, lr.Width + 12, lr.Height + 4), 12, Color(26, 255, 255, 255));
            InterpolationMode im = g.GetInterpolationMode(); g.SetInterpolationMode(InterpolationModeHighQualityBilinear);
            g.DrawImage(cache, lr, 0, 0, (float)pw, (float)ph, UnitPixel);
            g.SetInterpolationMode(im);
            x += lw + 14;
        }
    }
    std::vector<std::wstring> labs = { L"MSC", L"MWC" };
    float sw = SoftSegW(labs, 12.5f);
    SoftSeg(g, RectF(x, cy - 19, sw, 38), labs, g_game == GAME_MSC ? 0 : 1, UI_SET_CANAL, g_lobby == LB_NONE && !g_busy, 12.5f);
    x += sw + 16;
    // raccourcis a droite (regle 0.97.2 : jamais deplaces), en partant de la droite : Fermer, Agrandir, Reduire, Theme, Langue | API, Journaux, Signaler, Credits | Outils
    float bw = 40, gap = 6, rx = h.X + h.Width - 16;
    auto slot = [&]() { rx -= bw; RectF r(rx, cy - bw / 2, bw, bw); rx -= gap; return r; };
    ShellBtn(g, slot(), B_CLOSE, IC_CLOSE, true);
    ShellBtn(g, slot(), B_MAX, g_fullscreen || (g_wnd && IsZoomed(g_wnd)) ? IC_RESTORE : IC_MAX);
    ShellBtn(g, slot(), B_MIN, IC_MIN);
    ShellBtn(g, slot(), B_THEME, g_softDark ? IC_SUN : IC_MOON);
    {   // langue : texte
        RectF r = slot(); Button &b = g_btn[B_LANG]; b.r = DipToCard(r); b.visible = true;
        ShellIconBtn(g, r, -1, b.hover, false, false);
        Text(g, g_fr ? L"FR" : g_ru ? L"RU" : g_ar ? L"AR" : L"EN", r, 12.5f, FontStyleBold, b.hover > 0.01f ? g_soft.acc : g_soft.ink);
    }
    rx -= 8; g.DrawLine(&ln, rx, cy - 12, rx, cy + 12); rx -= 8;
    ShellPage(g, slot(), TAB_API, IC_BRACES);
    ShellPage(g, slot(), TAB_LOGS, IC_FILE);
    { RectF r = slot(); ShellBtn(g, r, B_BUG, IC_BUG); if (g_btn[B_BUG].hover > 0.05f) SoftTooltip(g, r, T(L"Signaler un bug", L"Report a bug")); }
    ShellPage(g, slot(), TAB_CREDITS, IC_STAR);
    rx -= 8; g.DrawLine(&ln, rx, cy - 12, rx, cy + 12); rx -= 8;
    ShellPage(g, slot(), TAB_TOOLS, IC_TOOLS);
    // recherche Ctrl+K au centre (loupe seule en fenetre etroite)
    float avail = rx - 10 - x;
    if (narrow || avail < 160) {
        if (avail >= 44) { RectF r(x + 4, cy - 20, 40, 40); bool hot = g_uiHot == UI_PALETTE_OPEN; ShellIconBtn(g, r, -1, hot ? 1.0f : 0.0f, false, false); DrawLoupe(g, r.X + 17, r.Y + 17, 7, hot ? g_soft.acc : g_soft.ink); HotZone(r, UI_PALETTE_OPEN); }
        return;
    }
    float fw = min(420.0f, avail), fx = x + (avail - fw) / 2;
    RectF f(fx, cy - 21, fw, 42);
    bool hot = g_uiHot == UI_PALETTE_OPEN;
    SoftShape(g, f, kSoftRCtl, g_soft.bgIn);
    { GraphicsState st = g.Save(); GraphicsPath p; RoundRect(p, f, kSoftRCtl); g.SetClip(&p); SoftInset(g, f, kSoftRCtl); g.Restore(st); }
    DrawLoupe(g, f.X + 20, cy, 6.5f, hot ? g_soft.acc : g_soft.faint);
    Text(g, T(L"Rechercher une page, un réglage, un article…", L"Search a page, a setting, an article…"), RectF(f.X + 38, f.Y, f.Width - 110, f.Height), 13, FontStyleRegular, hot ? g_soft.ink : g_soft.faint, StringAlignmentNear);
    {   // Ctrl+K
        const wchar_t *k = L"Ctrl+K"; float kw = MeasureW(MeasureG(), k, 11, FontStyleBold) + 14;
        RectF kr(f.X + f.Width - kw - 10, cy - 11, kw, 22);
        SoftRaised(g, kr, 7, 2, 4, 0.8f); SoftShape(g, kr, 7, g_soft.bg);
        Text(g, k, kr, 11, FontStyleBold, g_soft.muted);
    }
    HotZone(f, UI_PALETTE_OPEN);
}

// ---- barre de gauche : mises a jour, groupes et pages (indicateur qui glisse), Discord / code source / Soutenir
static void ShellDrawRail(Graphics &g)
{
    RectF r = g_shRail;
    bool collapsed = g_railWidth < 170;
    float labA = AnimTo(AK(AKH_MISC, 7002), RailCollapsedWanted() ? 0.0f : 1.0f, 150, SE_LINEAR);
    Pen ln(g_soft.line, 1); g.DrawLine(&ln, r.X + r.Width - 0.5f, r.Y, r.X + r.Width - 0.5f, r.Y + r.Height);
    {   // replier / deplier : rond de 28 a cheval sur le bord
        RectF c(r.X + r.Width - 14, r.Y + 10, 28, 28);
        bool hot = g_uiHot == UI_RAIL_COLLAPSE;
        SoftRaised(g, c, 14, 2, 6); SolidBrush b(g_soft.bg); g.FillEllipse(&b, c);
        Icon(g, collapsed ? IC_CHEVR : IC_CHEVL, c.X + 14, c.Y + 14, 14, hot ? g_soft.acc : g_soft.muted, 2.0f);
        HotZone(c, UI_RAIL_COLLAPSE);
    }
    float x0 = r.X + 12, w = r.Width - 24, y = r.Y + 14;
    // mises a jour proposees (mod, lanceur)
    if (g_btn[B_UPDATE].visible) {
        Button &b = g_btn[B_UPDATE];
        RectF br(x0, y, w, 40); b.r = DipToCard(br);
        float pulse = b.enabled ? 0.5f + 0.5f * sinf(g_time * 3.0f) : 0.0f;
        if (b.enabled) { SoftShape(g, RectF(br.X - 3, br.Y - 3, br.Width + 6, br.Height + 6), 15, WithA(g_soft.acc, 0.10f + 0.12f * pulse * (1 - b.hover))); InvalidateAll(); }
        SoftRaised(g, br, 12, 3, 8); SoftShape(g, br, 12, WithA(g_soft.acc, b.enabled ? 1.0f : 0.45f));
        if (b.hover > 0.01f) SoftShape(g, br, 12, Color((BYTE)(40 * b.hover), 255, 255, 255));
        std::wstring v = UpdAvail(); size_t dash = v.find(L'-'); if (dash != std::wstring::npos) v.resize(dash);
        Icon(g, IC_DOWNLOAD, collapsed ? br.X + br.Width / 2 : br.X + 20, br.Y + 20, 17, g_soft.accInk, 2.2f);
        if (!collapsed) Text(g, std::wstring(T(L"Mettre à jour · ", L"Update · ")) + v, RectF(br.X + 34, br.Y, br.Width - 40, br.Height), 13, FontStyleBold, WithA(g_soft.accInk, labA), StringAlignmentNear);
        y += 48;
    }
    if (g_btn[B_UPDLANCEUR].visible) {
        Button &b = g_btn[B_UPDLANCEUR];
        RectF br(x0, y, w, 34); b.r = DipToCard(br);
        SoftShape(g, br, 11, WithA(g_soft.acc, 0.16f + 0.14f * b.hover));
        Pen pen(WithA(g_soft.acc, b.enabled ? 0.9f : 0.4f), 1.2f); GraphicsPath p; RoundRect(p, br, 11); g.DrawPath(&pen, &p);
        std::wstring v = LancAvail();
        Icon(g, IC_DOWNLOAD, collapsed ? br.X + br.Width / 2 : br.X + 18, br.Y + 17, 15, WithA(g_soft.acc, b.enabled ? 1.0f : 0.5f), 2.0f);
        if (!collapsed) Text(g, std::wstring(T(L"Lanceur · ", L"Launcher · ")) + v.substr(0, v.find(L'-')), RectF(br.X + 32, br.Y, br.Width - 36, br.Height), 12, FontStyleBold, WithA(g_soft.ink, labA), StringAlignmentNear);
        y += 42;
    }
    // groupes et pages
    float lab = collapsed ? 10 : 26, step = 44, bottom = r.Y + r.Height - (collapsed ? 150 : 60);
    int nItems = 0;
    for (int k = 0, i = 0; k < kNavGroups; k++)
        for (int j = 0; j < kNavGroupN[k]; j++, i++) if (!NavHidden(kNav[i].page) && (!RailFolded(k) || kNav[i].page == g_tab)) nItems++;
    float avail = bottom - y, content = kNavGroups * lab + nItems * step;
    if (content > avail && nItems > 0) { step = max(36.0f, floorf((avail - kNavGroups * lab - 2) / nItems)); content = kNavGroups * lab + nItems * step; }   // (lignes resserrees jusqu'a 36 avant de defiler)
    g_railView = DipToCard(RectF(r.X, y, r.Width, avail));
    g_railScrollMax = content > avail ? content - avail + 6 : 0;
    g_railScroll = g_railScrollMax > 0 ? min(max(g_railScroll, 0.0f), g_railScrollMax) : 0;
    RectF view(r.X, y, r.Width, avail);
    GraphicsState st = g.Save();
    g.SetClip(view, CombineModeIntersect);
    float yy = y - g_railScroll, curY = -1;
    struct Row { RectF r; int page, icon; const wchar_t *fr, *en; };
    std::vector<Row> rows;
    for (int k = 0, i = 0; k < kNavGroups; k++) {
        if (!collapsed) {
            Text(g, T(kNavGroupFr[k], kNavGroupEn[k]), RectF(x0 + 12, yy + 4, w - 40, 16), 11, FontStyleBold, WithA(g_soft.faint, labA), StringAlignmentNear);
            if (RailFoldable(k)) {
                RectF hr(x0, yy, w, lab); bool hot = g_uiHot == UI_NAV_GROUP + k;
                Icon(g, RailFolded(k) ? IC_CHEVR : IC_CHEVD, x0 + w - 12, yy + 12, 12, WithA(hot ? g_soft.acc : g_soft.faint, labA), 2.0f);
                RectF hz; if (RectF::Intersect(hz, hr, view) && hz.Height > 0) HotZone(hz, UI_NAV_GROUP + k);
            }
        }
        yy += lab;
        for (int j = 0; j < kNavGroupN[k]; j++, i++) {
            const NavItem &it = kNav[i];
            if (NavHidden(it.page) || (RailFolded(k) && it.page != g_tab)) continue;
            rows.push_back({ RectF(x0, yy, w, step - 2), it.page, it.icon, it.fr, it.en });
            bool on = g_state == ST_IDLE && (it.page == TAB_ADDONS ? g_tab == TAB_CAR && AddonsOn() : g_tab == it.page && !(it.page == TAB_CAR && AddonsOn()));
            if (on) curY = yy;
            yy += step;
        }
    }
    // indicateur : un creux avec un trait d'accent, qui glisse (le meme objet)
    if (curY >= 0) {
        float iy = AnimTo(AK(AKH_TAB, 2), curY, kSoftTUi, SE_MOVE);
        RectF ir(x0, iy, w, step - 2);
        SoftShape(g, ir, 13, g_soft.bgIn);
        { GraphicsState s2 = g.Save(); GraphicsPath p; RoundRect(p, ir, 13); g.SetClip(&p); SoftInset(g, ir, 13, 3, 7); g.Restore(s2); }
        SoftShape(g, RectF(ir.X + 6, ir.Y + 11, 4, ir.Height - 22), 2, g_soft.acc);
    }
    int hotPage = -1; RectF hotR;
    for (auto &row : rows) {
        bool en = PageEnabled(row.page), on = g_state == ST_IDLE && (row.page == TAB_ADDONS ? g_tab == TAB_CAR && AddonsOn() : g_tab == row.page && !(row.page == TAB_CAR && AddonsOn())),
             hot = en && g_uiHot == UI_NAV + row.page;
        g_navR[row.page] = DipToCard(row.r);
        float a = en ? 1.0f : 0.38f;
        if (hot && !on) SoftShape(g, row.r, 13, WithA(g_soft.accSoft, 0.6f));
        float icx = collapsed ? row.r.X + row.r.Width / 2 : row.r.X + 26;
        Icon(g, row.icon, icx, row.r.Y + row.r.Height / 2, 20, WithA(on ? g_soft.acc : hot ? g_soft.ink : g_soft.muted, a), 1.9f);
        if (labA > 0.02f && !collapsed) Text(g, T(row.fr, row.en), RectF(row.r.X + 48, row.r.Y, row.r.Width - 76, row.r.Height), 14, FontStyleBold, WithA(on ? g_soft.ink : hot ? g_soft.ink : g_soft.muted, a * labA), StringAlignmentNear);
        // pastilles (depliee) ou point (repliee)
        float rx = row.r.X + row.r.Width - 10, by = row.r.Y + (row.r.Height - 20) / 2;
        auto chip = [&](const std::wstring &s, int kind) { if (collapsed) { SolidBrush d(kind == SC_WARN ? g_soft.warn : kind == SC_OK ? g_soft.ok : g_soft.acc); g.FillEllipse(&d, row.r.X + row.r.Width - 16, row.r.Y + 8, 7.0f, 7.0f); return; } float cw = SoftChipW(s); rx -= cw; SoftChip(g, rx, by - 2, s, kind); rx -= 4; };
        if (row.page == TAB_LOBBY && g_lobby != LB_NONE) { EnterCriticalSection(&g_lcs); int n = (int)g_peers.size(); LeaveCriticalSection(&g_lcs); wchar_t b[16]; swprintf_s(b, L"%d/%d", n, g_lobbyMax); chip(b, SC_OK); }
        else if (row.page == TAB_CAR && !collapsed) { RectF d(rx - 14, row.r.Y + (row.r.Height - 14) / 2, 14, 14); if (g_carColor >= 0) { SolidBrush cb(CarRgb(g_carColor)); g.FillEllipse(&cb, d); } else DrawCarWheel(g, d); Pen e(g_soft.line, 1.2f); g.DrawEllipse(&e, d); }
        else if (row.page == TAB_MODS && en) { int n = MscModCount(); if (n > 0) chip(std::to_wstring(n), SC_ACC); }
        else if (row.page == TAB_ADDONS && en && !on) { bool nw, up; AddonsBadges(nw, up); nw = nw || KuplaNew(); if (up) chip(L"UPDATE", SC_WARN); if (nw) chip(L"NEW", SC_ACC); }
        else if (row.page == TAB_NOTES && NotesUnseen() && !on) chip(T(L"NOUV.", L"NEW"), SC_ACC);
        RectF hz; if (en && RectF::Intersect(hz, row.r, view) && hz.Height > 0) HotZone(hz, UI_NAV + row.page);
        if (hot && collapsed) { hotPage = row.page; hotR = row.r; }
    }
    g.Restore(st);
    if (g_railScrollMax > 0) {   // barre de defilement fine a droite
        float th = max(30.0f, avail * avail / (avail + g_railScrollMax)), ty = y + (avail - th) * g_railScroll / g_railScrollMax;
        SoftShape(g, RectF(r.X + r.Width - 6, ty, 3, th), 1.5f, WithA(g_soft.faint, 0.6f));
    }
    if (hotPage >= 0) {   // repliee : le nom a droite, en info-bulle
        const NavItem *it = NULL; for (auto &n : kNav) if (n.page == hotPage) it = &n;
        if (it) { std::wstring s = T(it->fr, it->en); float tw = MeasureW(MeasureG(), s, 12.5f, FontStyleBold) + 24; RectF tr(r.X + r.Width + 6, hotR.Y + hotR.Height / 2 - 15, tw, 30); SoftRaised(g, tr, 10, 3, 8); SoftShape(g, tr, 10, g_soft.bg); Text(g, s, tr, 12.5f, FontStyleBold, g_soft.ink); }
    }
    // bas : Discord, code source, Soutenir (toujours la)
    if (!collapsed) {
        float by = r.Y + r.Height - 50;
        { Button &b = g_btn[B_DISCORD]; RectF br(x0, by, w - 92, 40); b.r = DipToCard(br); SoftRaised(g, br, 12, 3, 8); SoftShape(g, br, 12, kDiscordBlue); if (b.hover > 0.01f) SoftShape(g, br, 12, Color((BYTE)(50 * b.hover), 255, 255, 255)); Icon(g, IC_DISCORD, br.X + 22, br.Y + 20, 20, Color(255, 255, 255, 255)); Text(g, L"Discord", RectF(br.X + 38, br.Y, br.Width - 42, br.Height), 13, FontStyleBold, Color(255, 255, 255, 255), StringAlignmentNear); }
        ShellBtn(g, RectF(x0 + w - 86, by, 40, 40), B_GITHUB, IC_CODE);
        { RectF br(x0 + w - 40, by, 40, 40); Button &b = g_btn[B_KOFI]; b.r = DipToCard(br); ShellIconBtn(g, br, -1, b.hover, false, false); Icon(g, IC_HEART, br.X + 20, br.Y + 20, 18, Color(255, 255, 110, 140), 2.1f); }
    } else {
        float by = r.Y + r.Height - 46;
        { RectF br(r.X + (r.Width - 40) / 2, by, 40, 40); Button &b = g_btn[B_KOFI]; b.r = DipToCard(br); ShellIconBtn(g, br, -1, b.hover, false, false); Icon(g, IC_HEART, br.X + 20, br.Y + 20, 18, Color(255, 255, 110, 140), 2.1f); }
        ShellBtn(g, RectF(r.X + (r.Width - 40) / 2, by - 46, 40, 40), B_GITHUB, IC_CODE);
        { Button &b = g_btn[B_DISCORD]; RectF br(r.X + (r.Width - 40) / 2, by - 92, 40, 40); b.r = DipToCard(br); SoftRaised(g, br, 12, 3, 8); SoftShape(g, br, 12, kDiscordBlue); if (b.hover > 0.01f) SoftShape(g, br, 12, Color((BYTE)(50 * b.hover), 255, 255, 255)); Icon(g, IC_DISCORD, br.X + 20, br.Y + 20, 20, Color(255, 255, 255, 255)); }
    }
}

// ---- barre d'etat : etat du mod, jeu et version, message courant, salon, version du lanceur, langue
static void ShellDrawStatus(Graphics &g, const std::wstring &status, int kind, float prog)
{
    RectF s = g_shStatus;
    Pen ln(g_soft.line, 1); g.DrawLine(&ln, s.X, s.Y + 0.5f, s.X + s.Width, s.Y + 0.5f);
    float x = 18, cy = s.Y + s.Height / 2;
    bool ok = !g_gameDir.empty();
    std::wstring chip; int ck;
    if (!ok) { chip = T(L"Jeu introuvable", L"Game not found"); ck = SC_BAD; }
    else if (g_localVer.empty()) { chip = T(L"Mod absent", L"Mod missing"); ck = SC_BAD; }
    else if (g_busy) { chip = T(L"Mise à jour…", L"Updating…"); ck = SC_WARN; }
    else if (!UpdAvail().empty()) { chip = T(L"Mise à jour disponible", L"Update available"); ck = SC_WARN; }
    else { chip = T(L"Mod à jour", L"Mod up to date"); ck = SC_OK; }
    x += SoftChip(g, x, cy - 12, chip, ck, ck == SC_OK ? IC_CHECK : -1) + 12;
    Graphics &mg = MeasureG();
    std::wstring gl = ok ? std::wstring(GM().name) + (g_localVer.empty() ? L"" : L" · " + g_localVer.substr(0, g_localVer.find(L'-'))) : std::wstring(T(L"My Winter Car introuvable", L"My Winter Car not found"));
    float gw = MeasureW(mg, gl, 12.5f, FontStyleBold) + 4;
    Text(g, gl, RectF(x, cy - 10, gw, 20), 12.5f, FontStyleBold, ok ? g_soft.ink : g_soft.bad, StringAlignmentNear);
    x += gw + 10;
    {   // changer de dossier / choisir l'exe
        Button &eb = g_btn[B_EXE];
        const wchar_t *el = ok ? T(L"Changer de dossier", L"Change folder") : T(L"Choisir l'exe…", L"Choose exe…");
        float lw = MeasureW(mg, el, 12, FontStyleRegular) + 4;
        eb.r = DipToCard(RectF(x, cy - 10, lw, 20));
        if (eb.visible) { Text(g, el, RectF(x, cy - 10, lw, 20), 12, FontStyleUnderline, WithA(eb.hover > 0.01f ? g_soft.acc : g_soft.faint, eb.enabled ? 1.0f : 0.4f), StringAlignmentNear); x += lw + 14; }
    }
    // droite : langue, version du lanceur
    float rx = s.X + s.Width - 18;
    { const wchar_t *lg = g_fr ? L"FR" : g_ru ? L"RU" : g_ar ? L"AR" : L"EN"; float lw = SoftChipW(lg); rx -= lw; SoftChip(g, rx, cy - 12, lg, SC_ACC); rx -= 10; }
    { std::wstring lv = std::wstring(T(L"lanceur ", L"launcher ")) + LauncherVer(); float lw = MeasureW(mg, lv, 12, FontStyleRegular) + 4; rx -= lw; Text(g, lv, RectF(rx, cy - 10, lw, 20), 12, FontStyleRegular, g_soft.faint, StringAlignmentNear); rx -= 14; }
    if (g_lobby != LB_NONE) {   // salon en cours : cliquable
        EnterCriticalSection(&g_lcs); int n = (int)g_peers.size(); LeaveCriticalSection(&g_lcs);
        wchar_t b[64]; swprintf_s(b, n > 1 ? T(L"Salon en cours · %d joueurs", L"Lobby open · %d players") : T(L"Salon en cours · %d joueur", L"Lobby open · %d player"), n);
        float cw = SoftChipW(b) + 16; rx -= cw;
        SoftChip(g, rx, cy - 12, b, SC_OK, IC_USERS);
        HotZone(RectF(rx, cy - 12, cw, 24), UI_STATUS_LOBBY);
        rx -= 14;
    }
    // milieu : le message courant (couleur selon son genre), barre de progression dessous
    float sw = rx - x;
    if (sw > 60 && !status.empty()) {
        Color c = kind == K_OK ? g_soft.ok : kind == K_WARN ? g_soft.warn : kind == K_ERR ? g_soft.bad : g_soft.muted;
        SolidBrush db(c); g.FillEllipse(&db, x, cy - 4, 8.0f, 8.0f);
        Text(g, status, RectF(x + 14, cy - 10, sw - 14, 20), 12.5f, FontStyleBold, kind == K_NORMAL ? g_soft.ink : c, StringAlignmentNear);
        if (prog != -1.0f) SoftProgress(g, RectF(x + 14, cy + 10, min(260.0f, sw - 14), 5), prog);
    }
}

// ---- palette Ctrl+K : pages, reglages (kSetItems), articles du Guide ; fleches + Entree, Echap
static std::wstring g_palText; static int g_palSel;
struct PalItem { int kind, a, b, icon; std::wstring title, sub; };   // kind 0 page, 1 reglage (a : onglet SET_*, b : ligne), 2 article du Guide (a)
static std::vector<PalItem> g_palItems;
static void PalBuild()
{
    g_palItems.clear();
    std::wstring q = SetFold(g_palText);
    auto match = [&](const std::wstring &s) { return q.empty() || SetFold(s).find(q) != std::wstring::npos; };
    for (auto &n : kNav) if (!NavHidden(n.page) && match(T(n.fr, n.en))) g_palItems.push_back({ 0, n.page, 0, n.icon, T(n.fr, n.en), T(L"Page", L"Page") });
    struct { int page, icon; const wchar_t *fr, *en; } more[] = { { TAB_LOGS, IC_FILE, L"Journaux", L"Logs" }, { TAB_API, IC_BRACES, L"API pour les moddeurs", L"API for modders" }, { TAB_CREDITS, IC_STAR, L"Crédits", L"Credits" }, { TAB_TOOLS, IC_TOOLS, L"Outils", L"Tools" } };
    for (auto &m : more) if (match(T(m.fr, m.en))) g_palItems.push_back({ 0, m.page, 0, m.icon, T(m.fr, m.en), T(L"Page", L"Page") });
    if (!q.empty()) {
        for (int i = 0; i < kSetItemN && (int)g_palItems.size() < 40; i++) {
            const SetItem &it = kSetItems[i];
            if (!it.fr || !SetItemOn(it)) continue;
            std::wstring lab = T(it.fr, it.en);
            if (!match(lab) && !(it.dFr && match(T(it.dFr, it.dEn)))) continue;
            int tab = kSetCards[it.card].tab;
            g_palItems.push_back({ 1, tab, i, IC_SLIDERS, lab, std::wstring(T(L"Réglages · ", L"Settings · ")) + T(kSetTabFr[tab], kSetTabEn[tab]) });
        }
        for (int a = 0; a < kWikiArts && (int)g_palItems.size() < 60; a++) {
            if (!WikiArtShown(a)) continue;
            std::wstring t = T(kWikiArt[a].tFr, kWikiArt[a].tEn);
            if (!match(t) && !match(T(kWikiArt[a].sFr, kWikiArt[a].sEn))) continue;
            g_palItems.push_back({ 2, a, 0, IC_BOOK, t, T(L"Guide", L"Guide") });
        }
    }
    if (g_palSel >= (int)g_palItems.size()) g_palSel = max(0, (int)g_palItems.size() - 1);
}
static void PalOpen() { g_palOpen = true; g_palText.clear(); g_palSel = 0; g_focus = -1; PalBuild(); InvalidateAll(); }
static void PalClose() { g_palOpen = false; InvalidateAll(); }
static void PalActivate(int i)
{
    if (i < 0 || i >= (int)g_palItems.size()) return;
    PalItem it = g_palItems[i];
    PalClose();
    if (it.kind == 0) { if (PageEnabled(it.a)) GoPage(it.a); }
    else if (it.kind == 1) { int t = it.a <= SET_SON ? TAB_GFX : TAB_COOP; if (PageEnabled(t)) { GoPage(t); g_setTab = it.a; g_setSearch = it.title; g_setFocus = false; g_scroll[g_tab] = 0; } }
    else if (it.kind == 2) { if (PageEnabled(TAB_WIKI)) { GoPage(TAB_WIKI); WikiGoArt(it.a); } }
}
static bool PalMessage(UINT m, WPARAM wp, LPARAM lp, LRESULT &res)   // (WndProc, nouvelle interface) : vrai si pris
{
    if (m == WM_KEYDOWN && wp == 'K' && (GetKeyState(VK_CONTROL) & 0x8000) && g_state == ST_IDLE && g_tuto < 0 && !g_guide) { if (g_palOpen) PalClose(); else PalOpen(); res = 0; return true; }
    if (!g_palOpen) return false;
    if (m == WM_KEYDOWN) {
        if (wp == VK_ESCAPE) PalClose();
        else if (wp == VK_DOWN) { if (g_palSel + 1 < (int)g_palItems.size()) g_palSel++; }
        else if (wp == VK_UP) { if (g_palSel > 0) g_palSel--; }
        else if (wp == VK_RETURN) PalActivate(g_palSel);
        else if (wp == VK_BACK) { if (!g_palText.empty()) { g_palText.pop_back(); PalBuild(); } }
        else return false;
        InvalidateAll(); res = 0; return true;
    }
    if (m == WM_CHAR) {
        wchar_t c = (wchar_t)wp;
        if (c >= 32 && c != 127) { g_palText += c; g_palSel = 0; PalBuild(); InvalidateAll(); }
        res = 0; return true;
    }
    return false;
}
static void ShellDrawPalette(Graphics &g)
{
    float t = AnimTo(AK(AKH_MISC, 7003), g_palOpen ? 1.0f : 0.0f, kSoftTUi, SE_OUT);
    if (t <= 0.001f) return;
    SolidBrush veil(Color((BYTE)(115 * t), 0, 0, 0)); g.FillRectangle(&veil, RectF(0, 0, g_shW, g_shH));
    float w = min(560.0f, g_shW - 40), sc = 0.96f + 0.04f * t;
    int n = min((int)g_palItems.size(), kPalRows);
    float h = 70 + (n ? n * 44 + 14 : 48);
    RectF c((g_shW - w * sc) / 2, 80 + 10 * (1 - t), w * sc, h * sc);
    if (g_palOpen) HotZone(c, UI_NONE);
    SoftRaised(g, c, 20, 7, 16, t); SoftShape(g, c, 20, WithA(g_soft.bg, t));
    if (t < 0.6f) return;
    GraphicsState st = g.Save();
    g.TranslateTransform(c.X, c.Y); g.ScaleTransform(sc, sc);
    RectF f(16, 16, w - 32, 42);
    SoftField(g, f, g_palText, T(L"Page, réglage, article… puis Entrée", L"Page, setting, article… then Enter"), g_palOpen);
    float y = 70;
    if (!n) Text(g, T(L"Aucun résultat. Essaie « salon » ou « ombres ».", L"No result. Try \"lobby\" or \"shadows\"."), RectF(16, y, w - 32, 36), 13, FontStyleRegular, g_soft.muted, StringAlignmentNear);
    for (int i = 0; i < n; i++, y += 44) {
        const PalItem &it = g_palItems[i];
        RectF r(12, y, w - 24, 42);
        bool sel = i == g_palSel, hot = g_uiHot == UI_PAL_ROW + i;
        if (sel || hot) SoftShape(g, r, 12, sel ? g_soft.accSoft : WithA(g_soft.accSoft, 0.5f));
        Icon(g, it.icon, r.X + 22, r.Y + 21, 18, sel ? g_soft.acc : g_soft.muted, 1.9f);
        Text(g, it.title, RectF(r.X + 44, r.Y + 3, r.Width - 160, 20), 14, FontStyleBold, g_soft.ink, StringAlignmentNear);
        Text(g, it.sub, RectF(r.X + 44, r.Y + 22, r.Width - 160, 16), 11.5f, FontStyleRegular, g_soft.muted, StringAlignmentNear);
        if (sel) Text(g, L"↵", RectF(r.X + r.Width - 40, r.Y, 30, r.Height), 14, FontStyleBold, g_soft.faint);
        if (g_palOpen) HotZone(RectF(c.X + r.X * sc, c.Y + r.Y * sc, r.Width * sc, r.Height * sc), UI_PAL_ROW + i);   // (zone en DIP de la fenetre, pas de la transformee)
    }
    g.Restore(st);
}
static bool ShellClick(int id)   // (UiClick)
{
    if (id == UI_RAIL_COLLAPSE) { RailCollapseToggle(); return true; }
    if (id == UI_STATUS_LOBBY) { if (PageEnabled(TAB_LOBBY)) GoPage(TAB_LOBBY); return true; }
    if (id == UI_PALETTE_OPEN) { PalOpen(); return true; }
    if (id >= UI_PAL_ROW && id < UI_PAL_ROW + kPalRows) { PalActivate(id - UI_PAL_ROW); return true; }
    return false;
}

// ---- l'image entiere de la nouvelle interface : coque en DIP, colonne de page en coordonnees de la carte, puis les
// couches par-dessus (bulles de la visite, guides, fenetre de choix, palette, notifications)
static void RenderToNew(Bitmap &target)
{
    SoftThemeApply();
    g_skinBudget = g_wnd ? 6 : 100000;
    int w = (int)target.GetWidth(), h = (int)target.GetHeight();
    g_winW = w; g_winH = h;
    ShellLayout();
    float d = g_dpiScale;
    std::wstring status; int kind;
    EnterCriticalSection(&g_cs); status = g_status; kind = g_statusKind; LeaveCriticalSection(&g_cs);
    float prog = g_progress;
    for (int pass = 0; pass < 2; pass++) {
        Graphics g(&target);
        g.Clear(g_soft.bg);
        g.SetSmoothingMode(SmoothingModeAntiAlias);
        g.SetTextRenderingHint(g_textHint);
        g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        g.SetPixelOffsetMode(PixelOffsetModeHalf);
        g_zones.clear(); g_glassNow.clear();
        // la colonne de page (coordonnees de la carte), limitee a la zone de page
        {
            GraphicsState st = g.Save();
            g.SetClip(RectF(g_shPage.X * d, g_shPage.Y * d, g_shPage.Width * d, g_shPage.Height * d));
            g.TranslateTransform((float)g_offX, (float)g_offY);
            g.ScaleTransform(g_scale, g_scale);
            g.TranslateTransform(kM, kM);
            for (const GlassRec &gr : g_glassUsed) SoftCard(g, gr.r, gr.rad);   // (les cartes de verre des pages d'avant, en relief)
            DrawUI(g);
            g.Restore(st);
        }
        // la coque (DIP) : zones enregistrees en coordonnees de la carte
        {
            GraphicsState st = g.Save();
            g.ScaleTransform(d, d);
            g_zoneDip = true;
            ShellDrawHeader(g);
            ShellDrawRail(g);
            ShellDrawStatus(g, status, kind, prog);
            g_zoneDip = false;
            g.Restore(st);
        }
        // couches : visite, guide / invitation / choix d'interface, par-dessus tout (voile sur la fenetre entiere d'abord)
        if (g_tuto >= 0 || g_guide) { SolidBrush v(Color(g_softDark ? 110 : 80, 2, 6, 18)); g.FillRectangle(&v, RectF(0, 0, (float)w, (float)h)); }
        {
            GraphicsState st = g.Save();
            g.TranslateTransform((float)g_offX, (float)g_offY);
            g.ScaleTransform(g_scale, g_scale);
            g.TranslateTransform(kM, kM);
            DrawSteamGuide(g);
            DrawTuto(g);
            g.Restore(st);
        }
        {
            GraphicsState st = g.Save();
            g.ScaleTransform(d, d);
            g_zoneDip = true;
            ShellDrawPalette(g);
            SoftDrawToasts(g, g_shW, g_shStatus.Y - 8);
            g_zoneDip = false;
            g.Restore(st);
        }
        if (g_glassNow == g_glassUsed) break;
        g_glassUsed = g_glassNow;
    }
}
