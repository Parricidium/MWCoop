// ---------------------------------------------------------------- volet a droite - inclus par launcher.cpp
// Demande de JD (07/10) : la tenue (clic sur son personnage dans le salon, ou COOP > Apparence), la couleur de la
// voiture (icone voiture du salon, ou COOP > Couleur de la voiture) et les options du salon (engrenage du salon : port,
// fermeture du lanceur...) s'ouvrent dans un volet qui sort de sous la fenetre, a droite. VALIDER garde, ANNULER rend
// les valeurs d'avant ; les deux le replient. Les onglets TENUE et VOITURE ne sont plus dans la barre : le volet dessine
// leur contenu tel quel (DrawSkins, DrawCar, DrawOptions), decale ; la souris y est ramenee dans leur repere.
// La fenetre est elargie de kDrawerW a droite (transparente, donc sans clic, quand le volet est replie) ; le volet est
// dessine AVANT le fond de la fenetre (RenderTo) : il sort de dessous en glissant.
static int g_drawer = DR_NONE, g_drawerShown = DR_NONE;   // demande / montre (garde pendant le repli)
static float g_drawerT;                                     // 0 replie .. 1 deplie
static const float kDrawerW = 552;
static const RectF kDrawerCard(986, 60, 540, 540);   // meme hauteur que la carte du lanceur (20, 60, 960, 540)
static const float kDrawerDx = 996 - 440, kDrawerDy = 72 - 116;   // panneau des onglets (440,116) -> (996,72)
static const RectF kDrawerOk(1388, 556, 124, 30), kDrawerCancel(1256, 556, 124, 30);
static int g_drawerHot;                                     // 1 valider, 2 annuler
static int g_drSkin = -1, g_drCar = -1, g_drPort = -1, g_drClose = -1;   // valeurs a l'ouverture (ANNULER)

static float DrawerEase() { float t = min(max(g_drawerT, 0.0f), 1.0f); return t * t * (3 - 2 * t); }
static float DrawerSlide() { return (1 - DrawerEase()) * -(kDrawerCard.X + kDrawerCard.Width - 960); }
static bool DrawerOpen() { return g_drawer != DR_NONE && g_drawerT > 0.97f; }
static bool InDrawer(float x, float y) { return DrawerOpen() && kDrawerCard.Contains(x, y); }

static void DrawerShow(int kind)
{
    if (g_drawer == DR_NONE) {   // valeurs a rendre si on annule
        const Opt *ap = OptByKey("Apparence"), *po = OptByKey("Port"), *fl = OptByKey("FermerLanceur");
        g_drSkin = ap ? OptGet(*ap) : -1; g_drCar = g_carColor; g_drPort = po ? OptGet(*po) : -1; g_drClose = fl ? OptGet(*fl) : -1;
    }
    g_drawer = g_drawerShown = kind;
    g_drawerHot = 0;
    g_focus = -1;
    if (kind == DR_SKIN) g_skinIdleT = GetTickCount();
}
static void DrawerClose(bool keep)
{
    if (g_drawer == DR_NONE) return;
    if (!keep) {
        const Opt *ap = OptByKey("Apparence"), *po = OptByKey("Port"), *fl = OptByKey("FermerLanceur");
        if (ap && g_drSkin >= 0) OptSet(*ap, g_drSkin);
        if (g_drCar != g_carColor) CarSetColor(g_drCar);
        if (po && g_drPort >= 0) OptSet(*po, g_drPort);
        if (fl && g_drClose >= 0) OptSet(*fl, g_drClose);
        g_skinAnnounce = true;   // (salon : l'apparence rendue est annoncee tout de suite)
    }
    g_drawer = DR_NONE;
}
static void DrawerTick(float dt)
{
    float want = g_drawer != DR_NONE ? 1.0f : 0.0f;
    // (arrive : on n'y touche plus -- avant, ouvert a 1, il repartait vers 0 une image sur deux : le volet tremblait)
    if (want > g_drawerT) g_drawerT = min(g_drawerT + dt * 4.5f, 1.0f);
    else if (want < g_drawerT) g_drawerT = max(g_drawerT - dt * 4.5f, 0.0f);
    if (g_drawer == DR_NONE && g_drawerT <= 0) g_drawerShown = DR_NONE;
}

static void DrawDrawer(Graphics &g)
{
    if (g_drawerShown == DR_NONE || g_drawerT <= 0) return;
    float s = DrawerSlide();
    RectF card = kDrawerCard;
    card.X += s;
    {   // ombre, carte
        GraphicsPath sp; RoundRect(sp, RectF(card.X + 3, card.Y + 6, card.Width, card.Height), 26);
        SolidBrush sb(Color(70, 0, 0, 0)); g.FillPath(&sb, &sp);
    }
    GraphicsPath cp; RoundRect(cp, card, 26);
    LinearGradientBrush cb(card, TH(fallA), TH(fallB), LinearGradientModeVertical);
    g.FillPath(&cb, &cp);
    Pen cpen(TH(panelBorder), 1.5f);
    g.DrawPath(&cpen, &cp);
    GraphicsState st = g.Save();
    g.SetClip(card, CombineModeIntersect);
    g.TranslateTransform(kDrawerDx + s, kDrawerDy);
    if (g_drawerShown == DR_SKIN) DrawSkins(g);
    else if (g_drawerShown == DR_CAR) DrawCar(g);
    else { int keep = g_tab; g_tab = TAB_LOBBYOPT; DrawOptions(g); g_tab = keep; }
    g.Restore(st);
    const wchar_t *title = g_drawerShown == DR_SKIN ? T(L"TENUE", L"OUTFIT") : g_drawerShown == DR_CAR ? T(L"COULEUR DE LA VOITURE", L"CAR COLOR") : T(L"OPTIONS DU SALON", L"LOBBY OPTIONS");
    Text(g, title, RectF(card.X + 22, kDrawerOk.Y, 240, kDrawerOk.Height), 13, FontStyleBold, kInk, StringAlignmentNear);
    for (int k = 1; k <= 2; k++) {
        RectF r = k == 1 ? kDrawerOk : kDrawerCancel;
        r.X += s;
        GraphicsPath p; RoundRect(p, r, r.Height / 2);
        bool hot = g_drawerHot == k;
        if (k == 1) { LinearGradientBrush lg(r, Mix(kAcc, Color(255, 255, 255, 255), hot ? 0.15f : 0.0f), kAcc2, LinearGradientModeHorizontal); g.FillPath(&lg, &p); }
        else { SolidBrush fb(hot ? TH(cardSel) : TH(card)); g.FillPath(&fb, &p); Pen pp(hot ? kAcc : TH(choiceBorder), 1.3f); g.DrawPath(&pp, &p); }
        Text(g, k == 1 ? T(L"VALIDER", L"OK") : T(L"ANNULER", L"CANCEL"), r, 12.5f, FontStyleBold, k == 1 ? kOnAcc : (hot ? kAcc : kInk));
    }
}

// Souris (coordonnees de la fenetre) : survol ; clic (vrai s'il est pour le volet).
static void DrawerMouseMove(float x, float y)
{
    g_drawerHot = !DrawerOpen() ? 0 : kDrawerOk.Contains(x, y) ? 1 : kDrawerCancel.Contains(x, y) ? 2 : 0;
    if (!InDrawer(x, y)) return;
    float px = x - kDrawerDx, py = y - kDrawerDy;
    if (g_drawerShown == DR_SKIN) { g_skinHot = SkinCellAt(px, py); g_skinArrowHot = SkinArrowAt(px, py); }
    else if (g_drawerShown == DR_CAR) { g_carHot = CarSwatchAt(px, py); if (g_btn[B_COLOR].r.Contains(px, py)) g_hot = B_COLOR; }
    else { int keep = g_tab; g_tab = TAB_LOBBYOPT; HitOption(px, py, &g_optHot, &g_optPart); g_tab = keep; }
}
static bool DrawerHand() { return g_drawerHot != 0; }
static bool DrawerMouseDown(float x, float y)
{
    if (!DrawerOpen()) return false;
    if (kDrawerOk.Contains(x, y)) { DrawerClose(true); return true; }
    if (kDrawerCancel.Contains(x, y)) { DrawerClose(false); return true; }
    if (!kDrawerCard.Contains(x, y)) return false;
    float px = x - kDrawerDx, py = y - kDrawerDy;
    if (g_drawerShown == DR_SKIN) SkinMouseDown(px, py);
    else if (g_drawerShown == DR_CAR) { if (g_btn[B_COLOR].r.Contains(px, py)) CarPickColor(); else CarMouseDown(px, py); }
    else {
        int keep = g_tab, row, part;
        g_tab = TAB_LOBBYOPT;
        HitOption(px, py, &row, &part);
        g_tab = keep;
        if (row >= 0) OptStep(row, part < 0 ? -1 : 1);
    }
    return true;
}
