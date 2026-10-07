// ---------------------------------------------------------------- lanceur 2026 : elements communs - inclus par launcher.cpp
// Demande de JD (08/10) : un lanceur plus grand et plus ergonomique, "design moderne 2026, glassmorphism", d'apres la
// maquette validee. Fenetre de 1280 x 800 (carte arrondie, ombre douce autour : image de kImgW x kImgH) :
//  - scene : nuit d'aurore boreale (sombre) ou matin d'hiver (clair), dessinee ici une fois par taille (SceneBuild) ;
//    une copie tres floutee sert de fond aux cartes de "verre" (GlassCard : scene floutee + teinte + liseré) ;
//  - a gauche, la barre de navigation : logo, version, bouton de mise a jour, pages groupees (JOUER, MON PERSONNAGE,
//    PARTAGE, INFOS), Reglages, liens GitHub / Ko-fi ;
//  - en haut, le titre de la page et ses commandes ; en bas, l'etat (ligne de statut) et le jeu trouve ;
//  - une page a la fois (g_tab = TAB_*) dans kMain.
// Les cartes de verre d'une page sont declarees pendant le dessin (Glass) ; la scene + ces cartes sont gardees dans
// une image (g_glassCache), refaite quand la mise en page change (RenderTo redessine alors l'image dans la foulee).
// Clics : zones declarees pendant le dessin (HotZone, identifiants UI_*), plus les boutons g_btn (B_*).
static const RectF kRail(16, 16, 232, 768), kMain(264, 88, 1000, 656), kFooter(264, 752, 1000, 32);

// ---- zones cliquables (refaites a chaque image)
struct HotRect { RectF r; int id; };
static std::vector<HotRect> g_zones;
static int g_uiHot = -1;
static void HotZone(RectF r, int id) { g_zones.push_back({ r, id }); }
static int ZoneAt(float x, float y)
{
    for (int i = (int)g_zones.size() - 1; i >= 0; i--) if (g_zones[i].r.Contains(x, y)) return g_zones[i].id;
    return -1;
}
enum {
    UI_NONE = 1,                  // (zone qui arrete la souris, sans action : un menu par-dessus)
    UI_NAV = 100,                 // + page
    UI_SEG = 150,                 // + k : onglets de l'en-tete (tenue / voiture ; contenu / mods)
    UI_HOME_LOOK = 170, UI_HOME_CAR, UI_HOME_NOTES, UI_HOME_LOBBY, UI_HOME_STEAMOPT,
    UI_PAGE_OK = 180, UI_PAGE_CANCEL,
    UI_LOBBY_GEAR = 190, UI_LOBBY_INVITE, UI_POP_PORTM, UI_POP_PORTP, UI_POP_CLOSE, UI_POP_CONTENT, UI_POP_X, UI_POP_LOOK,
    UI_LOBBY_ME_LOOK, UI_LOBBY_ME_CAR,
    UI_SET_AUTO = 220, UI_SET_CHECK, UI_SET_DARK, UI_SET_LIGHT, UI_SET_FR, UI_SET_EN, UI_SET_CLOSE, UI_SET_GUIDE,
    UI_SET_STEAM, UI_SET_IP, UI_SET_PORTM, UI_SET_PORTP, UI_SET_FOLDER, UI_SET_BUY, UI_SET_MODSAUTO,
    UI_NOTE = 300,                // + i : version i de la liste
    UI_NOTE_UPDATE = 290,
    UI_LOOKPIN = 400,             // + zone (0 tete, 1 torse, 2 jambes, 3 silhouette)
    UI_LOOKROT = 410,             // + 0 / 1 : tourner
    UI_LOOKRAND = 415, UI_LOOKRESET,
};

// ---- icones (traits, dans une boite de 24, comme celles de la maquette)
enum { IC_HOME, IC_USERS, IC_SHIRT, IC_CAR, IC_BOX, IC_PUZZLE, IC_SPARK, IC_FILE, IC_GEAR, IC_MOON, IC_SUN, IC_MIN, IC_CLOSE,
       IC_DOWNLOAD, IC_CHECK, IC_CHEVR, IC_CHEVL, IC_PLAY, IC_LOGIN, IC_GLOBE, IC_WIFI, IC_PLUS, IC_FOLDER, IC_ZIP, IC_ALERT,
       IC_HEART, IC_CODE, IC_SHUFFLE, IC_UNDO, IC_MUSIC, IC_IMAGE, IC_RADIO, IC_USER, IC_HASH, IC_FLAG, IC_HOMEPLUS };
static void Icon(Graphics &g, int id, float cx, float cy, float size, Color c, float stroke = 1.9f)
{
    float k = size / 24.0f;
    auto P = [&](float x, float y) { return PointF(cx + (x - 12) * k, cy + (y - 12) * k); };
    Pen pen(c, stroke * k);
    pen.SetStartCap(LineCapRound); pen.SetEndCap(LineCapRound); pen.SetLineJoin(LineJoinRound);
    auto line = [&](float x1, float y1, float x2, float y2) { g.DrawLine(&pen, P(x1, y1), P(x2, y2)); };
    auto poly = [&](std::initializer_list<float> v, bool closed) {
        std::vector<PointF> pts;
        for (auto it = v.begin(); it != v.end(); it += 2) pts.push_back(P(*it, *(it + 1)));
        if (closed) g.DrawPolygon(&pen, pts.data(), (INT)pts.size()); else g.DrawLines(&pen, pts.data(), (INT)pts.size());
    };
    auto circ = [&](float x, float y, float r) { g.DrawEllipse(&pen, cx + (x - r - 12) * k, cy + (y - r - 12) * k, 2 * r * k, 2 * r * k); };
    auto bez = [&](GraphicsPath &p, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3) { p.AddBezier(P(x0, y0), P(x1, y1), P(x2, y2), P(x3, y3)); };
    switch (id) {
    case IC_HOME: poly({ 3, 11, 12, 4, 21, 11 }, false); poly({ 5, 10, 5, 20, 19, 20, 19, 10 }, false); break;
    case IC_HOMEPLUS: poly({ 3, 11, 12, 4, 21, 11 }, false); poly({ 5, 10, 5, 20, 19, 20, 19, 10 }, false); line(12, 12, 12, 17); line(9.5f, 14.5f, 14.5f, 14.5f); break;
    case IC_USERS: {
        circ(9, 8, 3.5f);
        GraphicsPath p; bez(p, 2.5f, 20, 3.3f, 16.4f, 5.9f, 14.5f, 9, 14.5f); bez(p, 9, 14.5f, 12.1f, 14.5f, 14.7f, 16.4f, 15.5f, 20); g.DrawPath(&pen, &p);
        GraphicsPath q; bez(q, 16, 4.6f, 18.6f, 5.2f, 18.6f, 10.8f, 16, 11.4f); g.DrawPath(&pen, &q);
        GraphicsPath r; bez(r, 18, 14.8f, 19.8f, 15.4f, 21, 17.2f, 21.5f, 20); g.DrawPath(&pen, &r);
        break;
    }
    case IC_SHIRT: {
        GraphicsPath p;
        PointF a[] = { P(8, 3), P(3, 6), P(5, 11), P(8, 10), P(8, 21), P(16, 21), P(16, 10), P(19, 11), P(21, 6), P(16, 3) };
        p.AddLines(a, 10);
        bez(p, 16, 3, 15.5f, 4.6f, 14, 5.6f, 12, 5.6f); bez(p, 12, 5.6f, 10, 5.6f, 8.5f, 4.6f, 8, 3);
        p.CloseFigure(); g.DrawPath(&pen, &p);
        break;
    }
    case IC_CAR: poly({ 4, 16, 4, 12, 6.2f, 7, 17.8f, 7, 20, 12, 20, 16 }, true); line(4, 12, 20, 12); circ(7.5f, 16.5f, 1.8f); circ(16.5f, 16.5f, 1.8f); break;
    case IC_BOX: poly({ 21, 8, 12, 3, 3, 8, 12, 13 }, true); poly({ 3, 8, 3, 16, 12, 21, 21, 16, 21, 8 }, false); line(12, 13, 12, 21); break;
    case IC_PUZZLE: {
        GraphicsPath p; RoundRect(p, RectF(cx - 8 * k, cy - 4 * k, 12 * k, 12 * k), 2 * k); g.DrawPath(&pen, &p);
        circ(10, 6.2f, 2.2f); circ(18.2f, 14, 2.2f);
        break;
    }
    case IC_SPARK: poly({ 12, 3, 13.8f, 8.2f, 19, 10, 13.8f, 11.8f, 12, 17, 10.2f, 11.8f, 5, 10, 10.2f, 8.2f }, true);
                   poly({ 19, 16, 19.8f, 18.2f, 22, 19, 19.8f, 19.8f, 19, 22, 18.2f, 19.8f, 16, 19, 18.2f, 18.2f }, true); break;
    case IC_FILE: poly({ 14, 3, 6, 3, 6, 21, 18, 21, 18, 7 }, true); poly({ 14, 3, 14, 7, 18, 7 }, false); line(9, 12, 15, 12); line(9, 16, 15, 16); break;
    case IC_GEAR: {   // roue dentee : 8 dents, trou au centre
        GraphicsPath p;
        std::vector<PointF> pts;
        for (int i = 0; i < 8; i++) {
            float a = i * 3.14159265f / 4, w = 0.24f;
            float r0 = 7.2f, r1 = 9.8f;
            pts.push_back(P(12 + cosf(a - 0.39f + 0.02f) * r0, 12 + sinf(a - 0.39f + 0.02f) * r0));
            pts.push_back(P(12 + cosf(a - w) * r1, 12 + sinf(a - w) * r1));
            pts.push_back(P(12 + cosf(a + w) * r1, 12 + sinf(a + w) * r1));
            pts.push_back(P(12 + cosf(a + 0.39f - 0.02f) * r0, 12 + sinf(a + 0.39f - 0.02f) * r0));
        }
        p.AddPolygon(pts.data(), (INT)pts.size());
        g.DrawPath(&pen, &p);
        circ(12, 12, 3.2f);
        break;
    }
    case IC_MOON: {
        SolidBrush b(c);
        GraphicsPath mp; mp.AddEllipse(cx - 7.5f * k, cy - 7.5f * k, 15 * k, 15 * k);
        Region rg(&mp);
        GraphicsPath cut; cut.AddEllipse(cx - 2.5f * k, cy - 10.5f * k, 15 * k, 15 * k);
        rg.Exclude(&cut);
        g.FillRegion(&b, &rg);
        break;
    }
    case IC_SUN: circ(12, 12, 4); for (int i = 0; i < 8; i++) { float a = i * 3.14159265f / 4; line(12 + cosf(a) * 7, 12 + sinf(a) * 7, 12 + cosf(a) * 9.5f, 12 + sinf(a) * 9.5f); } break;
    case IC_MIN: line(6, 12, 18, 12); break;
    case IC_CLOSE: line(6, 6, 18, 18); line(18, 6, 6, 18); break;
    case IC_DOWNLOAD: line(12, 4, 12, 15); poly({ 7, 10, 12, 15, 17, 10 }, false); line(5, 20, 19, 20); break;
    case IC_CHECK: poly({ 5, 12.5f, 9.5f, 17, 19, 7.5f }, false); break;
    case IC_CHEVR: poly({ 9, 6, 15, 12, 9, 18 }, false); break;
    case IC_CHEVL: poly({ 15, 6, 9, 12, 15, 18 }, false); break;
    case IC_PLAY: poly({ 7, 4, 20, 12, 7, 20 }, true); break;
    case IC_LOGIN: poly({ 10, 4, 18, 4, 18, 20, 10, 20 }, false); line(3, 12, 14, 12); poly({ 10, 8, 14, 12, 10, 16 }, false); break;
    case IC_GLOBE: circ(12, 12, 9); line(3, 12, 21, 12); g.DrawEllipse(&pen, cx - 3.8f * k, cy - 9 * k, 7.6f * k, 18 * k); break;
    case IC_WIFI: {
        auto arc = [&](float ccy, float r, float a0, float sw) { g.DrawArc(&pen, cx - r * k, cy + (ccy - 12 - r) * k, 2 * r * k, 2 * r * k, a0, sw); };
        arc(20.18f, 15, 228.2f, 83.6f); arc(20.1f, 10, 229.5f, 81); arc(20, 5, 233.1f, 73.7f);
        SolidBrush b(c); g.FillEllipse(&b, cx - 1.1f * k, cy + 5.9f * k, 2.2f * k, 2.2f * k);
        break;
    }
    case IC_PLUS: line(12, 5, 12, 19); line(5, 12, 19, 12); break;
    case IC_FOLDER: poly({ 3, 6, 9, 6, 11, 8, 21, 8, 21, 19, 3, 19 }, true); break;
    case IC_ZIP: poly({ 3, 4, 21, 4, 21, 9, 3, 9 }, true); poly({ 5, 9, 5, 19, 19, 19, 19, 9 }, false); line(10, 13, 14, 13); break;
    case IC_ALERT: poly({ 12, 3, 21.5f, 20, 2.5f, 20 }, true); line(12, 10, 12, 14); line(12, 17, 12, 17.4f); break;
    case IC_HEART: {
        GraphicsPath p;
        bez(p, 12, 20, 7, 16.5f, 4.5f, 12.5f, 4.5f, 9.5f); bez(p, 4.5f, 9.5f, 4.5f, 5.5f, 10, 4.5f, 12, 7.2f);
        bez(p, 12, 7.2f, 14, 4.5f, 19.5f, 5.5f, 19.5f, 9.5f); bez(p, 19.5f, 9.5f, 19.5f, 12.5f, 17, 16.5f, 12, 20);
        g.DrawPath(&pen, &p);
        break;
    }
    case IC_CODE: poly({ 8, 8, 4, 12, 8, 16 }, false); poly({ 16, 8, 20, 12, 16, 16 }, false); break;
    case IC_SHUFFLE: {
        GraphicsPath p; p.AddLine(P(4, 7), P(7, 7)); bez(p, 7, 7, 11, 7, 13, 17, 17, 17); p.AddLine(P(17, 17), P(20, 17)); g.DrawPath(&pen, &p);
        GraphicsPath q; q.AddLine(P(4, 17), P(7, 17)); bez(q, 7, 17, 8.6f, 17, 9.8f, 15.4f, 10.8f, 13.4f); g.DrawPath(&pen, &q);
        GraphicsPath r; bez(r, 13.2f, 9.6f, 14.2f, 8.1f, 15.4f, 7, 17, 7); r.AddLine(P(17, 7), P(20, 7)); g.DrawPath(&pen, &r);
        poly({ 18, 4, 21, 7, 18, 10 }, false); poly({ 18, 14, 21, 17, 18, 20 }, false);
        break;
    }
    case IC_UNDO: g.DrawArc(&pen, cx - 8 * k, cy - 8 * k, 16 * k, 16 * k, 225, 300); poly({ 4, 4, 4, 9, 9, 9 }, false); break;
    case IC_MUSIC: poly({ 9, 18, 9, 5, 20, 3, 20, 16 }, false); circ(6.5f, 18, 2.5f); circ(17.5f, 16, 2.5f); break;
    case IC_IMAGE: { GraphicsPath p; RoundRect(p, RectF(cx - 9 * k, cy - 8 * k, 18 * k, 16 * k), 2 * k); g.DrawPath(&pen, &p); circ(9, 10, 2); poly({ 21, 16, 16, 11, 7, 20 }, false); break; }
    case IC_RADIO: { GraphicsPath p; RoundRect(p, RectF(cx - 9 * k, cy - 4 * k, 18 * k, 12 * k), 2 * k); g.DrawPath(&pen, &p); line(7, 8, 17, 4); circ(15.5f, 14, 2.5f); line(6, 13, 10, 13); line(6, 16, 10, 16); break; }
    case IC_USER: { circ(12, 8, 4); GraphicsPath p; bez(p, 4, 21, 5, 16.8f, 8, 14.5f, 12, 14.5f); bez(p, 12, 14.5f, 16, 14.5f, 19, 16.8f, 20, 21); g.DrawPath(&pen, &p); break; }
    case IC_HASH: line(9, 3, 7, 21); line(17, 3, 15, 21); line(4, 9, 21, 9); line(3, 15, 20, 15); break;
    case IC_FLAG: line(5, 21, 5, 4); poly({ 5, 4, 16, 4, 14, 8, 16, 12, 5, 12 }, false); break;
    }
}

// ---- scene (dessinee une fois par taille / theme) et verre
static Bitmap *g_scene, *g_sceneBlur, *g_glassCache;
static int g_sceneW, g_sceneH, g_sceneDarkK = -1;
static float g_sceneScale;
struct GlassRec { RectF r; float rad; bool operator==(const GlassRec &o) const { return r.Equals(o.r) && rad == o.rad; } };
static std::vector<GlassRec> g_glassNow, g_glassUsed;
static Region *g_outside;   // carte moins les cartes de verre (la neige tombe la)
static GraphicsPath *CardPath() { static GraphicsPath *p; if (!p) { p = new GraphicsPath(); RoundRect(*p, RectF(0, 0, kCardW, kCardH), 24); } return p; }

// Une carte de verre (coordonnees de la carte) : scene floutee, teinte, reflet en haut, liseré ; ombre douce dessous.
static void PaintGlass(Graphics &g, RectF r, float rad, Color tint, bool shadow)
{
    GraphicsPath p; RoundRect(p, r, rad);
    if (shadow)
        for (int k = 4; k >= 1; k--) {
            GraphicsPath sp; RoundRect(sp, RectF(r.X - k * 3.0f, r.Y - k * 3.0f + 10, r.Width + k * 6.0f, r.Height + k * 6.0f), rad + k * 3.0f);
            SolidBrush sb(Color((BYTE)(g_dark ? 12 : 7), 0, 6, 20)); g.FillPath(&sb, &sp);
        }
    GraphicsState st = g.Save();
    g.SetClip(&p, CombineModeIntersect);
    if (g_sceneBlur) {
        g.SetInterpolationMode(InterpolationModeBilinear);
        g.DrawImage(g_sceneBlur, RectF(-kM, -kM, kImgW, kImgH));
    }
    SolidBrush tb(tint); g.FillPath(&tb, &p);
    float hh = min(r.Height, 140.0f);
    LinearGradientBrush hl(RectF(r.X, r.Y - 1, r.Width, hh + 2), Color((BYTE)(g_dark ? 16 : 70), 255, 255, 255), Color(0, 255, 255, 255), LinearGradientModeVertical);
    g.FillRectangle(&hl, RectF(r.X, r.Y, r.Width, hh));
    g.Restore(st);
    Pen border(TH(panelBorder), 1.0f); g.DrawPath(&border, &p);
}
// Carte de verre de la page (gardee dans l'image de fond).
static void Glass(RectF r, float rad = 22) { g_glassNow.push_back({ r, rad }); }
// Verre dessine tout de suite (bulle, menu, fenetre par-dessus le contenu) : plus opaque.
static void GlassLive(Graphics &g, RectF r, float rad)
{
    PaintGlass(g, r, rad, g_dark ? Color(205, 18, 30, 64) : Color(225, 255, 255, 255), true);
}

// ---- petits elements
static void SmallLabel(Graphics &g, const std::wstring &s, RectF r, Color c, StringAlignment h = StringAlignmentNear)
{
    Text(g, s, r, 11, FontStyleBold, c, h);
}
static void Pill(Graphics &g, RectF r, Color fill, const std::wstring &s, Color ink, float px = 11)
{
    GraphicsPath p; RoundRect(p, r, r.Height / 2);
    SolidBrush b(fill); g.FillPath(&b, &p);
    Text(g, s, r, px, FontStyleBold, ink);
}
static float PillW(const std::wstring &s, float px = 11) { Graphics &mg = MeasureG(); return MeasureW(mg, s, px, FontStyleBold) + 16; }
// Bouton plein (accent) ou en verre clair ; zone cliquable id.
static void UiButton(Graphics &g, RectF r, const std::wstring &s, int id, bool primary, int icon = -1, bool enabled = true, float rad = 14)
{
    bool hot = enabled && g_uiHot == id;
    float a = enabled ? 1.0f : 0.4f;
    GraphicsPath p; RoundRect(p, r, min(rad, r.Height / 2));
    if (primary) {
        SolidBrush b(WithA(kAcc, a)); g.FillPath(&b, &p);
        if (hot) { SolidBrush hb(Color(60, 255, 255, 255)); g.FillPath(&hb, &p); }
    } else {
        SolidBrush b(hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
        Pen pen(WithA(TH(fieldBorder), a), 1.0f); g.DrawPath(&pen, &p);
    }
    Color ink = WithA(primary ? kOnAcc : kInk, a);
    Graphics &mg = MeasureG();
    float px = r.Height >= 44 ? 13.5f : 12.5f;
    float tw = MeasureW(mg, s, px, FontStyleBold), iw = icon >= 0 ? 24.0f : 0.0f;
    float x0 = r.X + (r.Width - tw - iw) / 2;
    if (icon >= 0) Icon(g, icon, x0 + 9, r.Y + r.Height / 2, 17, ink, 2.1f);
    Text(g, s, RectF(x0 + iw, r.Y, tw + 2, r.Height), px, FontStyleBold, ink, StringAlignmentNear);
    if (enabled) HotZone(r, id);
}
// Interrupteur (48 x 28 dans la maquette).
static void UiToggle(Graphics &g, RectF r, bool on, bool hot)
{
    GraphicsPath p; RoundRect(p, r, r.Height / 2);
    SolidBrush b(on ? kAcc : TH(toggleOff)); g.FillPath(&b, &p);
    if (hot) { Pen hp(WithA(kAcc, 0.9f), 1.4f); g.DrawPath(&hp, &p); }
    float d = r.Height - 6, x = on ? r.X + r.Width - d - 3 : r.X + 3;
    SolidBrush sh(Color(70, 0, 0, 0)); g.FillEllipse(&sh, x, r.Y + 4, d, d);
    SolidBrush k(Color(255, 255, 255, 255)); g.FillEllipse(&k, x, r.Y + 3, d, d);
}
// Choix a segments (2 ou plus) : celui qui est pris en accent ; zones id0 + i.
static void UiSeg(Graphics &g, RectF r, const std::vector<std::wstring> &labels, int sel, int id0, float px = 12.5f)
{
    GraphicsPath tp; RoundRect(tp, r, min(12.0f, r.Height / 2));
    SolidBrush tb(TH(field)); g.FillPath(&tb, &tp);
    float w = (r.Width - 6) / labels.size();
    for (int i = 0; i < (int)labels.size(); i++) {
        RectF s(r.X + 3 + i * w, r.Y + 3, w, r.Height - 6);
        bool on = i == sel, hot = g_uiHot == id0 + i;
        GraphicsPath p; RoundRect(p, s, min(9.0f, s.Height / 2));
        if (on) { SolidBrush b(kAcc); g.FillPath(&b, &p); }
        else if (hot) { SolidBrush b(TH(cardSel)); g.FillPath(&b, &p); }
        Text(g, labels[i], s, px, FontStyleBold, on ? kOnAcc : kInk);
        HotZone(s, id0 + i);
    }
}
// Ligne de reglage : titre, texte gris dessous.
static void SetRowText(Graphics &g, RectF r, const std::wstring &t, const std::wstring &d)
{
    if (d.empty()) { Text(g, t, r, 14, FontStyleBold, kInk, StringAlignmentNear); return; }
    Text(g, t, RectF(r.X, r.Y + r.Height / 2 - 19, r.Width, 20), 14, FontStyleBold, kInk, StringAlignmentNear);
    Text(g, d, RectF(r.X, r.Y + r.Height / 2 + 1, r.Width, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
}
// Bouton rond de l'en-tete (44 x 44, verre clair).
static void HeadButton(Graphics &g, int id, int icon, const wchar_t *text = NULL)
{
    Button &b = g_btn[id];
    if (!b.visible) return;
    GraphicsPath p; RoundRect(p, b.r, 14);
    bool close = id == B_CLOSE;
    SolidBrush fb(close && b.hover > 0.01f ? Mix(TH(btn2), Color(220, 230, 70, 70), b.hover) : Mix(TH(btn2), TH(btn2Hot), b.hover));
    g.FillPath(&fb, &p);
    Pen pen(TH(fieldBorder), 1.0f); g.DrawPath(&pen, &p);
    Color c = close ? Mix(kInk, Color(255, 255, 255, 255), b.hover) : kInk;
    if (text) Text(g, text, b.r, 12.5f, FontStyleBold, c);
    else Icon(g, icon, b.r.X + b.r.Width / 2, b.r.Y + b.r.Height / 2, 18, c, 2.0f);
}

static void GoPage(int p);
static void PageOk();
static void PageCancel();
static int g_contentHot = -1;          // page CONTENU : tuile survolee (5000+i, 5100)
static bool g_lobbyOpts;              // salon : menu de l'engrenage ouvert
static int g_snapSkin = -1, g_snapCar = -1, g_snapFrom = -1;   // valeurs a l'arrivee sur TENUE / VOITURE / CONTENU (ANNULER)
