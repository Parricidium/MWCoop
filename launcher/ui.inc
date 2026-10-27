// ---------------------------------------------------------------- lanceur 2026 : scene, navigation, pages - inclus par launcher.cpp
// (elements communs, zones, icones, verre : uikit.inc)
static float Rnd01(int i, int k);
static void SceneSpruce(Graphics &g, float x, float base, float h, Brush *ink, Brush *snow)
{
    g.FillRectangle(ink, x - h * 0.03f, base - h * 0.12f, h * 0.06f, h * 0.14f);
    for (int i = 0; i < 5; i++) {
        float k = i / 4.0f, top = base - h + h * 0.17f * i, bot = top + h * 0.34f, half = h * (0.12f + 0.19f * k);
        PointF tri[] = { PointF(x, top), PointF(x + half, bot), PointF(x + half * 0.35f, bot - h * 0.03f), PointF(x, bot), PointF(x - half * 0.35f, bot - h * 0.03f), PointF(x - half, bot) };
        g.FillPolygon(ink, tri, 6);
        PointF cap[] = { PointF(x, top), PointF(x + half * 0.55f, top + h * 0.19f), PointF(x + half * 0.15f, top + h * 0.15f), PointF(x - half * 0.2f, top + h * 0.2f), PointF(x - half * 0.5f, top + h * 0.17f) };
        g.FillPolygon(snow, cap, 5);
    }
}
// Chemin d'une suite de courbes de Bezier (points : depart puis 3 par segment), fermee en bas de la carte au besoin.
static void BezPath(GraphicsPath &p, std::initializer_list<float> v)
{
    std::vector<PointF> pts;
    for (auto it = v.begin(); it != v.end(); it += 2) pts.push_back(PointF(*it, *(it + 1)));
    p.AddBeziers(pts.data(), (INT)pts.size());
}
static void SceneDraw(Graphics &g)
{
    bool dk = g_dark;
    const ScenePal &pal = ScenePalette();   // (couleurs du theme : launcher.cpp)
    RectF c(0, 0, kCardW, kCardH);
    {   // ciel
        LinearGradientBrush sky(RectF(0, -1, kCardW, kCardH + 2), Color(255, 0, 0, 0), Color(255, 0, 0, 0), LinearGradientModeVertical);
        REAL pos[4] = { 0, 0.45f, 0.78f, 1 };
        sky.SetInterpolationColors(pal.sky, pos, 4);
        g.FillRectangle(&sky, c);
    }
    {   // aurore (sombre) ou voiles roses (clair) : formes nettes dans une petite image, agrandie = floue
        const int lw = 256, lh = 160;
        Bitmap low(lw, lh, PixelFormat32bppPARGB);
        {
            Graphics lg(&low);
            lg.SetSmoothingMode(SmoothingModeAntiAlias);
            lg.Clear(Color(0, 0, 0, 0));
            lg.ScaleTransform(lw / kCardW, lh / kCardH);
            auto band = [&](std::initializer_list<float> top, std::initializer_list<float> bot, const Color *cols, int n, float alpha) {
                GraphicsPath p;
                BezPath(p, top);
                std::vector<float> b(bot);
                std::vector<PointF> pts;
                for (size_t i = 0; i + 1 < b.size(); i += 2) pts.push_back(PointF(b[i], b[i + 1]));
                PointF lp;
                p.GetLastPoint(&lp);
                p.AddLine(lp, pts[0]);   // (le bas, de droite a gauche)
                p.AddBeziers(pts.data(), (INT)pts.size());
                p.CloseFigure();
                LinearGradientBrush lb(RectF(-60, 0, 1460, 10), Color(0, 0, 0, 0), Color(0, 0, 0, 0), LinearGradientModeHorizontal);
                std::vector<Color> cc(cols, cols + n);
                for (auto &x : cc) x = Color((BYTE)(x.GetA() * alpha), x.GetR(), x.GetG(), x.GetB());
                std::vector<REAL> ps;
                for (int i = 0; i < n; i++) ps.push_back(n == 4 ? (i == 0 ? 0 : i == 1 ? 0.3f : i == 2 ? 0.65f : 1) : i / (float)(n - 1));
                lb.SetInterpolationColors(cc.data(), ps.data(), n);
                lg.FillPath(&lb, &p);
            };
            float af = g_aurore == 0 ? 0.0f : g_aurore == 1 ? 0.45f : 1.0f;   // (Reglages > Theme : aurore off, calme, vivante)
            if (dk && af > 0) {   // (nuit : trois voiles aux couleurs du theme -- bleu-vert et violet, verts seuls, ambre et orange)
                band({ -60, 250, 160, 120, 330, 300, 560, 180, 790, 60, 900, 60, 1140, 170, 1380, 280, 1360, 140, 1400, 120 },
                     { 1400, 230, 1180, 260, 1010, 170, 780, 290, 550, 410, 380, 330, 160, 300, -60, 270, -40, 330, -60, 340 }, pal.aurA, 4, af);
                band({ -40, 140, 220, 60, 420, 200, 700, 110, 980, 20, 1080, 30, 1320, 90 },
                     { 1320, 150, 1060, 120, 900, 200, 640, 190, 380, 180, 200, 170, -40, 220 }, pal.aurB, 4, af);
                band({ 300, 330, 520, 260, 760, 380, 1000, 300, 1240, 220, 1240, 270, 1320, 300 },
                     { 1320, 350, 1080, 360, 900, 400, 640, 390, 380, 380, 380, 380, 300, 380 }, pal.aurA, 4, 0.6f * af);
            } else if (af > 0) {
                band({ -60, 250, 160, 160, 330, 300, 560, 220, 790, 140, 900, 120, 1140, 200, 1380, 280, 1360, 180, 1400, 160 },
                     { 1400, 260, 1180, 290, 1010, 220, 780, 320, 550, 410, 380, 350, 160, 330, -60, 300, -40, 350, -60, 360 }, pal.aurA, 4, 0.9f * af);
            }
        }
        const int tw = 48, th = 30;
        Bitmap tiny(tw, th, PixelFormat32bppPARGB);
        {
            Graphics tg(&tiny);
            tg.Clear(Color(0, 0, 0, 0));
            tg.SetInterpolationMode(InterpolationModeHighQualityBilinear);
            tg.SetPixelOffsetMode(PixelOffsetModeHalf);
            tg.DrawImage(&low, RectF(0, 0, (REAL)tw, (REAL)th));
        }
        g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        ImageAttributes ia;
        ia.SetWrapMode(WrapModeTileFlipXY);
        g.DrawImage(&tiny, RectF(0, 0, kCardW, kCardH), 0, 0, (REAL)tw, (REAL)th, UnitPixel, &ia);
    }
    bool msc = g_game == GAME_MSC;   // (ete : moins d'etoiles, soleil bas au lieu de la lune, pas de neige)
    if (dk) {   // etoiles
        SolidBrush sb(Color(200, 230, 238, 255));
        for (int i = 0; i < (msc ? 30 : 90); i++) {
            float x = Rnd01(i, 81) * kCardW, y = Rnd01(i, 82) * 330, r = 0.5f + Rnd01(i, 83) * 0.9f;
            sb.SetColor(Color((BYTE)(90 + 140 * Rnd01(i, 84)), 230, 238, 255));
            g.FillEllipse(&sb, x - r, y - r, 2 * r, 2 * r);
        }
    }
    if (msc) {   // soleil : bas sur l'horizon, orange (soir d'ete), ou haut et blanc (jour)
        float mx = 1090, my = dk ? 470.0f : 120.0f, rad = dk ? 260.0f : 150.0f;
        GraphicsPath gp; gp.AddEllipse(mx - rad, my - rad, 2 * rad, 2 * rad);
        PathGradientBrush pg(&gp);
        pg.SetCenterColor(dk ? Color(200, 255, 170, 90) : Color(200, 255, 252, 230));
        Color edge(0, 255, 200, 120); int one = 1;
        pg.SetSurroundColors(&edge, &one);
        g.FillPath(&pg, &gp);
        SolidBrush disc(dk ? Color(255, 255, 196, 120) : Color(255, 255, 250, 225));
        g.FillEllipse(&disc, mx - 34, my - 34, 68.0f, 68.0f);
    } else {   // lune (sombre) ou soleil pale (clair)
        float mx = 1090, my = dk ? 120.0f : 150.0f;
        GraphicsPath gp; gp.AddEllipse(mx - 130, my - 130, 260.0f, 260.0f);
        PathGradientBrush pg(&gp);
        pg.SetCenterColor(dk ? Color(120, 234, 244, 255) : Color(170, 255, 246, 220));
        Color edge(0, 255, 255, 255); int one = 1;
        pg.SetSurroundColors(&edge, &one);
        g.FillPath(&pg, &gp);
        SolidBrush disc(dk ? Color(255, 238, 245, 255) : Color(235, 255, 250, 232));
        g.FillEllipse(&disc, mx - 30, my - 30, 60.0f, 60.0f);
        if (dk) { SolidBrush cr(Color(255, 215, 227, 245)); g.FillEllipse(&cr, mx - 16, my - 14, 12.0f, 12.0f); g.FillEllipse(&cr, mx + 6, my + 6, 8.0f, 8.0f); }
    }
    {   // collines : lointaines, proches (adoucies)
        PointF far_[] = { PointF(0, 560), PointF(120, 478), PointF(210, 516), PointF(330, 440), PointF(470, 516), PointF(590, 456), PointF(720, 526), PointF(860, 446), PointF(990, 508), PointF(1120, 456), PointF(1280, 516) };
        GraphicsPath fp; fp.AddCurve(far_, 11, 0.45f); fp.AddLine(PointF(1280, 516), PointF(1280, 800)); fp.AddLine(PointF(1280, 800), PointF(0, 800)); fp.CloseFigure();
        SolidBrush fb(pal.farHill); g.FillPath(&fb, &fp);
        PointF near_[] = { PointF(0, 600), PointF(90, 562), PointF(200, 588), PointF(300, 548), PointF(430, 598), PointF(560, 562), PointF(700, 606), PointF(820, 566), PointF(960, 598), PointF(1100, 562), PointF(1280, 598) };
        GraphicsPath np; np.AddCurve(near_, 11, 0.45f); np.AddLine(PointF(1280, 598), PointF(1280, 800)); np.AddLine(PointF(1280, 800), PointF(0, 800)); np.CloseFigure();
        SolidBrush nb(pal.nearHill); g.FillPath(&nb, &np);
    }
    {   // sapins
        SolidBrush ink(pal.treeInk), snow(pal.treeSnow);
        for (int i = 0; i < 48; i++) {
            if (Rnd01(i, 91) < 0.22f) continue;
            float x = i * 27.5f + Rnd01(i, 92) * 16 - 8, base = 652 + Rnd01(i, 93) * 12, h = 44 + Rnd01(i, 94) * 66;
            if (x > 900 && x < 1080) h *= 0.6f;   // (devant la maison : plus petits)
            SceneSpruce(g, x, base, h, &ink, &snow);
        }
    }
    {   // neige au sol, congere
        GraphicsPath sp;
        BezPath(sp, { 0, 660, 220, 630, 420, 650, 640, 640, 860, 630, 1060, 628, 1280, 650 });
        sp.AddLine(PointF(1280, 650), PointF(1280, 800)); sp.AddLine(PointF(1280, 800), PointF(0, 800)); sp.CloseFigure();
        LinearGradientBrush sb(RectF(0, 625, kCardW, 180), pal.snow0, pal.snow1, LinearGradientModeVertical);
        g.FillPath(&sb, &sp);
        GraphicsPath dp;
        BezPath(dp, { 0, 700, 260, 680, 520, 712, 780, 696, 1040, 680, 1140, 690, 1280, 704 });
        dp.AddLine(PointF(1280, 704), PointF(1280, 800)); dp.AddLine(PointF(1280, 800), PointF(0, 800)); dp.CloseFigure();
        SolidBrush db(pal.drift); g.FillPath(&db, &dp);
    }
    {   // maison eclairee et vieille voiture
        GraphicsPath gp; gp.AddEllipse(865.0f, 492.0f, 240.0f, 240.0f);
        PathGradientBrush pg(&gp);
        pg.SetCenterColor(dk ? Color(140, 255, 179, 71) : Color(70, 255, 179, 71));
        Color edge(0, 255, 179, 71); int one = 1;
        pg.SetSurroundColors(&edge, &one);
        g.FillPath(&pg, &gp);
        SolidBrush wall(Color(255, 74, 42, 36)), roofD(Color(255, 58, 34, 32)), roofS(msc ? Color(255, 70, 40, 36) : dk ? Color(255, 223, 233, 247) : Color(255, 250, 252, 255)), win(Color(255, 255, 199, 106)), door(Color(255, 36, 19, 15));
        g.FillRectangle(&wall, 922.0f, 586.0f, 128.0f, 62.0f);
        PointF rs[] = { PointF(908, 590), PointF(986, 540), PointF(1064, 590) }; g.FillPolygon(&roofS, rs, 3);
        PointF rd[] = { PointF(916, 592), PointF(986, 548), PointF(1056, 592) }; g.FillPolygon(&roofD, rd, 3);
        g.FillRectangle(&roofD, 1022.0f, 548.0f, 12.0f, 28.0f);
        g.FillRectangle(&win, 938.0f, 602.0f, 22.0f, 20.0f); g.FillRectangle(&win, 1012.0f, 602.0f, 22.0f, 20.0f);
        g.FillRectangle(&door, 976.0f, 610.0f, 18.0f, 38.0f);
        SolidBrush car(msc ? Color(255, 214, 104, 40) : Color(255, 20, 29, 51)), wheel(Color(255, 7, 12, 24));   // (MSC : la Satsuma orange)
        GraphicsPath cp; RoundRect(cp, RectF(1080, 626, 74, 18), 5); g.FillPath(&car, &cp);
        PointF top[] = { PointF(1094, 626), PointF(1104, 612), PointF(1134, 612), PointF(1144, 626) }; g.FillPolygon(&car, top, 4);
        if (!msc) { PointF sn[] = { PointF(1094, 612), PointF(1104, 606), PointF(1134, 606), PointF(1140, 612) }; g.FillPolygon(&roofS, sn, 4); }
        g.FillEllipse(&wheel, 1091.0f, 638.0f, 14.0f, 14.0f); g.FillEllipse(&wheel, 1131.0f, 638.0f, 14.0f, 14.0f);
    }
}

// Scene, scene floutee et ombre de la carte, a la taille de la fenetre (pixels) ; refaites si elle change.
static void SceneBuild(int w, int h, float scale)
{
    delete g_scene; delete g_sceneBlur; delete g_glassCache;
    g_glassCache = NULL;
    g_scene = new Bitmap(w, h, PixelFormat32bppPARGB);
    {
        Graphics cg(g_scene);
        cg.Clear(Color(0, 0, 0, 0));
        cg.SetSmoothingMode(SmoothingModeAntiAlias);
        cg.SetPixelOffsetMode(PixelOffsetModeHalf);
        {   // ombre douce : la carte en noir dans une petite image, agrandie
            int sw = max(8, w / 10), sh = max(8, h / 10);
            Bitmap sb(sw, sh, PixelFormat32bppPARGB);
            {
                Graphics sg(&sb);
                sg.SetSmoothingMode(SmoothingModeAntiAlias);
                sg.Clear(Color(0, 0, 0, 0));
                sg.ScaleTransform(sw / kImgW, sh / kImgH);
                GraphicsPath p; RoundRect(p, RectF(kM + 6, kM + 12, kCardW - 12, kCardH - 10), 24);
                SolidBrush b(Color(g_dark ? 170 : 110, 0, 8, 24)); sg.FillPath(&b, &p);
            }
            cg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
            cg.DrawImage(&sb, RectF(0, 0, (REAL)w, (REAL)h), 0, 0, (REAL)sw, (REAL)sh, UnitPixel);
        }
        cg.ScaleTransform(scale, scale);
        cg.TranslateTransform(kM, kM);
        cg.SetClip(CardPath());
        SceneDraw(cg);
        cg.ResetClip();
        Pen edge(g_dark ? Color(40, 255, 255, 255) : Color(120, 255, 255, 255), 1.0f);
        cg.DrawPath(&edge, CardPath());
    }
    {   // floue : reduite fortement puis agrandie
        int bw = max(8, w / 34), bh = max(8, h / 34);
        Bitmap tiny(bw, bh, PixelFormat32bppPARGB);
        {
            Graphics sg(&tiny);
            sg.SetInterpolationMode(InterpolationModeHighQualityBilinear);
            sg.SetPixelOffsetMode(PixelOffsetModeHalf);
            sg.DrawImage(g_scene, RectF(0, 0, (REAL)bw, (REAL)bh));
        }
        g_sceneBlur = new Bitmap(w, h, PixelFormat32bppPARGB);
        Graphics bg(g_sceneBlur);
        bg.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        bg.SetPixelOffsetMode(PixelOffsetModeHalf);
        ImageAttributes ia;
        ia.SetWrapMode(WrapModeTileFlipXY);
        bg.DrawImage(&tiny, RectF(0, 0, (REAL)w, (REAL)h), 0, 0, (REAL)bw, (REAL)bh, UnitPixel, &ia);
    }
    g_sceneW = w; g_sceneH = h; g_sceneScale = scale; g_sceneDarkK = SceneKey();
}

static const int kSnowCols = 32, kSnowRows = 20;
static bool g_snowCell[kSnowRows][kSnowCols];
static bool SnowVisible(float x, float y)
{
    int cx = (int)(x / 40), cy = (int)(y / 40);
    return cx >= 0 && cy >= 0 && cx < kSnowCols && cy < kSnowRows && g_snowCell[cy][cx];
}
static void GlassBuild(int w, int h, float scale)
{
    delete g_glassCache;
    g_glassCache = new Bitmap(w, h, PixelFormat32bppPARGB);
    Graphics g(g_glassCache);
    g.SetInterpolationMode(InterpolationModeNearestNeighbor);
    g.DrawImage(g_scene, 0, 0, w, h);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetPixelOffsetMode(PixelOffsetModeHalf);
    g.ScaleTransform(scale, scale);
    g.TranslateTransform(kM, kM);
    for (const GlassRec &gr : g_glassUsed) PaintGlass(g, gr.r, gr.rad, TH(panel), true);
    delete g_outside;
    g_outside = new Region(CardPath());
    for (const GlassRec &gr : g_glassUsed) { GraphicsPath p; RoundRect(p, gr.r, gr.rad); g_outside->Exclude(&p); }
    // cases de 40 px ou la neige se voit (les flocons caches sous les cartes ne sont pas dessines)
    for (int cy = 0; cy < kSnowRows; cy++)
        for (int cx = 0; cx < kSnowCols; cx++) g_snowCell[cy][cx] = g_outside->IsVisible(RectF(cx * 40.0f - 6, cy * 40.0f - 6, 52, 52)) != FALSE;
}
static void EnsureScene(int w, int h, float scale)
{
    if (!g_scene || g_sceneW != w || g_sceneH != h || g_sceneScale != scale || g_sceneDarkK != SceneKey()) SceneBuild(w, h, scale);
    if (!g_glassCache) GlassBuild(w, h, scale);
}
// Animation par-dessus le fond : neige (hors des cartes), etoiles qui scintillent et etoile filante (sombre).
static void DrawSceneAnim(Graphics &g)
{
    if (g_aurore == 0) return;   // (Reglages > Theme : fond fige)
    float t = g_sceneT, dens = g_aurore == 1 ? 0.35f : 1.0f;   // (calme : moins de neige, pas d'etoile filante)
    GraphicsState st = g.Save();
    if (g_outside) g.SetClip(g_outside, CombineModeIntersect); else g.SetClip(CardPath(), CombineModeIntersect);
    SolidBrush b(Color(0, 0, 0, 0));
    if (g_dark) {
        for (int i = 0; i < (int)(40 * dens); i++) {
            float x = Rnd01(i, 1) * kCardW, y = Rnd01(i, 2) * 300;
            if (!SnowVisible(x, y)) continue;
            float s = sinf(t * (0.6f + Rnd01(i, 3) * 1.6f) + Rnd01(i, 4) * 6.283f);
            float a = s > 0 ? powf(s, 6) * (150 + 100 * Rnd01(i, 5)) : 0;
            if (a < 3) continue;
            float r = 0.7f + 0.6f * Rnd01(i, 6);
            b.SetColor(Color((BYTE)a, 225, 235, 255));
            g.FillEllipse(&b, x - r, y - r, 2 * r, 2 * r);
            if (a > 120) { Pen pen(Color((BYTE)(a * 0.45f), 210, 225, 255), 0.7f); g.DrawLine(&pen, x - 4, y, x + 4, y); g.DrawLine(&pen, x, y - 4, x, y + 4); }
        }
        int n = (int)(t / 17);
        float p = fmodf(t, 17) - 6;
        if (p > 0 && p < 0.8f && g_aurore == 2) {
            float x0 = 500 + Rnd01(n, 7) * 600, y0 = 18 + Rnd01(n, 8) * 30;
            float hx = x0 - p * 230, hy = y0 + p * 230 * 0.28f, a = 230 * sinf(p / 0.8f * 3.14159f);
            PointF head(hx, hy), tail(hx + 60, hy - 60 * 0.28f);
            LinearGradientBrush lb(tail, head, Color(0, 220, 232, 255), Color((BYTE)a, 235, 242, 255));
            Pen pen(&lb, 1.4f); pen.SetStartCap(LineCapRound); pen.SetEndCap(LineCapRound);
            g.DrawLine(&pen, tail, head);
        }
    }
    if (g_game == GAME_MSC) {   // ete : lucioles le soir, pollen le jour (au lieu de la neige)
        for (int i = 0; i < (int)(60 * dens); i++) {
            float bx = Rnd01(i, 11) * kCardW, by = (g_dark ? 470 : 300) + Rnd01(i, 12) * (g_dark ? 300 : 480);
            float x = bx + 26 * sinf(t * (0.2f + 0.3f * Rnd01(i, 13)) + Rnd01(i, 14) * 6.283f), y = by + 14 * sinf(t * (0.3f + 0.4f * Rnd01(i, 15)) + Rnd01(i, 16) * 6.283f);
            if (!g_dark) y = fmodf(by - t * (6 + 8 * Rnd01(i, 17)) + 900, 820) - 10;   // (pollen : monte doucement)
            if (!SnowVisible(x, y)) continue;
            if (g_dark) {
                float s = sinf(t * (0.8f + 1.4f * Rnd01(i, 18)) + Rnd01(i, 19) * 6.283f);
                float a = s > 0 ? powf(s, 3) * 230 : 0;
                if (a < 4) continue;
                for (int k = 0; k < 2; k++) { float rr = k ? 2.4f : 6.0f; b.SetColor(Color((BYTE)(a * (k ? 0.5f : 0.14f)), 230, 255, 130)); g.FillEllipse(&b, x - rr, y - rr, 2 * rr, 2 * rr); }
                b.SetColor(Color((BYTE)a, 250, 255, 200)); g.FillEllipse(&b, x - 1.2f, y - 1.2f, 2.4f, 2.4f);
            } else {
                float r = 1.0f + 1.4f * Rnd01(i, 20);
                b.SetColor(Color((BYTE)(110 + 80 * Rnd01(i, 21)), 255, 255, 245)); g.FillEllipse(&b, x - r, y - r, 2 * r, 2 * r);
            }
        }
        g.Restore(st);
        return;
    }
    struct Layer { int n; float s0, s1, speed, sway, alpha; } layers[3] = { { 150, 1.0f, 1.9f, 15, 5, 150 }, { 80, 1.9f, 3.0f, 30, 9, 190 }, { 18, 3.2f, 4.6f, 58, 15, 210 } };
    float drift = 9 * t - 46.0f * cosf(t * 0.13f);
    Color halo = g_dark ? Color(255, 160, 180, 220) : Color(255, 110, 140, 185);
    for (int l = 0; l < 3; l++) {
        const Layer &L = layers[l];
        for (int i = 0; i < (int)(L.n * dens); i++) {
            float sp = L.speed * (0.8f + 0.4f * Rnd01(i, 10 + l));
            float y = -10 + fmodf(Rnd01(i, 20 + l) * 820 + sp * t, 820);
            float x = fmodf(Rnd01(i, 30 + l) * 1300 + drift * L.speed / 30 + L.sway * sinf(t * (0.7f + 0.5f * Rnd01(i, 40 + l)) + Rnd01(i, 50 + l) * 6.283f) + 130000, 1300) - 10;
            if (!SnowVisible(x, y)) continue;
            float a = L.alpha * (0.5f + 0.5f * Rnd01(i, 60 + l));
            float r = (L.s0 + (L.s1 - L.s0) * Rnd01(i, 70 + l)) / 2;
            if (l == 2) {
                Color c = g_dark ? Color(255, 232, 238, 255) : halo;
                for (int k = 0; k < 2; k++) { float rr = r * (k ? 1.35f : 1.9f); b.SetColor(Color((BYTE)(a * (k ? 0.22f : 0.1f)), c.GetR(), c.GetG(), c.GetB())); g.FillEllipse(&b, x - rr, y - rr, 2 * rr, 2 * rr); }
                r *= 0.75f;
            } else if (!g_dark) { b.SetColor(Color((BYTE)min(a * 0.5f, 255.0f), halo.GetR(), halo.GetG(), halo.GetB())); g.FillEllipse(&b, x - r * 1.6f, y - r * 1.6f, r * 3.2f, r * 3.2f); }
            b.SetColor(Color((BYTE)a, g_dark ? 232 : 255, g_dark ? 238 : 255, 255));
            g.FillEllipse(&b, x - r, y - r, 2 * r, 2 * r);
        }
    }
    g.Restore(st);
}

// ---- pages : entree / sortie (tenue, voiture, contenu : ANNULER rend les valeurs d'avant)
static void ContentSnapshot(bool restore);   // (modsync.inc)
static void MscScan();                       // (mods.inc)
static bool PageSnap(int p) { return p == TAB_SKIN || p == TAB_CAR || p == TAB_CONTENT; }
// Pages absentes d'un jeu : sur My Summer Car, pas de cassettes VHS (camescope et magasin de MWC ; 19/10).
static bool NavHidden(int p) { return false; }   // (VHS et addons : sur My Summer Car aussi depuis le 24/10 -- coin video et voitures)
static bool AddonsOn(); static void AddonsGo(); static void AddonsLeave();   // (voiture.inc : onglet Addons de la page Voiture)
static bool PageEnabled(int p)
{
    if (g_state != ST_IDLE || NavHidden(p)) return false;
    if (g_gameDir.empty()) return p == TAB_HOME || p == TAB_NOTES || p == TAB_COOP || p == TAB_API || p == TAB_CREDITS || p == TAB_WIKI || p == TAB_ALBUM || p == TAB_TOOLS || p == TAB_BUG;
    if (p == TAB_LOBBY) return g_lobby != LB_NONE;
    return true;
}
static int PageBack() { return g_lobby != LB_NONE ? TAB_LOBBY : TAB_HOME; }
static void GoPage(int p)
{
    // Raccourci Addons de la barre (suggestion d'un joueur, 21/10) : la page Voiture, onglet Addons ; Voiture seule : l'onglet des couleurs.
    if (p == TAB_ADDONS) { AddonsGo(); return; }
    if (p == TAB_CAR) AddonsLeave();
    if (p == g_tab) return;
    SoftPageChange(g_tab, p);   // (soft.inc, nouvelle interface : l'ancienne page part, la nouvelle arrive dans le sens de la barre)
    if (PageSnap(p) && !PageSnap(g_tab)) {   // valeurs a rendre si on annule
        const Opt *ap = OptByKey("Apparence");
        g_snapSkin = ap ? OptGet(*ap) : -1; g_snapCar = g_carColor; g_snapFrom = g_tab;
        ContentSnapshot(false);
        PersoSnapshot(false);
    }
    g_tab = p;
    g_focus = -1; g_optHot = -1; g_lobbyOpts = false;
    if (p == TAB_NOTES) NotesMarkSeen();
    if (p == TAB_LOGS) { LogsScan(); CrashSeen(); }
    if (p == TAB_VHS) { g_vhsEdit = -1; VhsScan(); }
    if (p == TAB_ALBUM) AlbScan();
    if (p == TAB_SAVES) { g_saveEdit = -1; SavesScan(); }
    if (p == TAB_MODS || p == TAB_CONTENT) MscScan();
    if (p == TAB_SKIN) g_skinIdleT = GetTickCount();
    if (p == TAB_GFX || p == TAB_COOP) { GfxLoad(); SetEnter(p); }   // (page Reglages : onglet Jeu ou Co-op)
}
static void PageCancel()
{
    const Opt *ap = OptByKey("Apparence");
    if (ap && g_snapSkin >= 0) OptSet(*ap, g_snapSkin);
    if (g_snapCar != g_carColor) CarSetColor(g_snapCar);
    ContentSnapshot(true);
    PersoSnapshot(true);
    g_skinAnnounce = true;
    int back = g_snapFrom >= 0 && !PageSnap(g_snapFrom) && PageEnabled(g_snapFrom) ? g_snapFrom : PageBack();
    g_tab = -1;   // (pas de nouvelle sauvegarde des valeurs)
    GoPage(back);
}
static void PageOk() { int back = g_snapFrom >= 0 && !PageSnap(g_snapFrom) && PageEnabled(g_snapFrom) ? g_snapFrom : PageBack(); GoPage(back); }

// ---- barre de navigation
struct NavItem { int page, icon; const wchar_t *fr, *en; };
// Rangement de la maquette validee par JD (17/10) : JOUER / MOI / REGLAGES / MEDIA & AIDE -- une page = une tache ;
// les options d'un salon se reglent dans le salon, les reglages durables dans Reglages (Jeu = graphismes, Co-op).
static const NavItem kNav[] = {
    { TAB_HOME, IC_HOME, L"Accueil", L"Home" }, { TAB_LOBBY, IC_USERS, L"Salon", L"Lobby" }, { TAB_SAVES, IC_FILE, L"Sauvegardes", L"Saves" },
    { TAB_SKIN, IC_SHIRT, L"Personnage", L"Character" }, { TAB_CAR, IC_CAR, L"Voiture", L"Car" }, { TAB_CONTENT, IC_BOX, L"Contenu envoyé", L"Sent content" },
    { TAB_GFX, IC_SLIDERS, L"Jeu", L"Game" }, { TAB_COOP, IC_GLOBE, L"Co-op", L"Co-op" }, { TAB_MODS, IC_PUZZLE, L"Mods", L"Mods" },
    { TAB_ADDONS, IC_CAR, L"Addons", L"Add-ons" },   // (sous Mods : les voitures addons et la VAZ, chacune avec sa livree -- suggestion d'un joueur, 21/10)
    { TAB_VHS, IC_FILM, L"VHS", L"VHS" }, { TAB_ALBUM, IC_IMAGE, L"Album", L"Album" },   // (MEDIA : media.inc, demande de JD, 13/10)
    { TAB_WIKI, IC_BOOK, L"Guide", L"Guide" },   // (guide des joueurs, wiki.inc)
    { TAB_NOTES, IC_SPARK, L"Nouveautés", L"What's new" } };
// Journaux et API moddeurs : boutons de l'en-tete, a gauche de la langue (demande de JD, 07/10 : place liberee a gauche).
static const int kNavGroupN[] = { 3, 3, 4, 4 };   // pages de chaque groupe, dans l'ordre de kNav
static const int kNavGroups = 4;
static const wchar_t *kNavGroupFr[] = { L"JOUER", L"MOI", L"RÉGLAGES", L"MÉDIA & AIDE" }, *kNavGroupEn[] = { L"PLAY", L"ME", L"SETTINGS", L"MEDIA & HELP" };
static Bitmap *g_logo;
// Groupes pliables (demande de JD, 10/10) : MON PERSONNAGE, PARTAGE, JEU (pas JOUER ni INFOS). Plies : [Lanceur] RailPlie
// (numeros de groupe). La page ouverte reste montree meme dans un groupe plie ; tout est deplie pendant la visite guidee.
static bool TutoActive();   // (tuto.inc)
static int g_railFold = -1;   // bits des groupes plies (-1 : pas encore lu)
static bool RailFoldable(int k) { return k >= 1 && k <= 3; }   // (MOI, REGLAGES, MEDIA & AIDE)
static bool RailFolded(int k)
{
    if (g_railFold < 0) {
        g_railFold = 0;
        wchar_t b[32] = {};
        GetPrivateProfileStringW(L"Lanceur", L"RailPlie", L"", b, 32, g_iniLauncher.c_str());
        for (int i = 1; i <= 4; i++) if (wcschr(b, L'0' + i)) g_railFold |= 1 << i;
    }
    return RailFoldable(k) && (g_railFold & (1 << k)) != 0 && !TutoActive();
}
static void RailToggle(int k)
{
    if (!RailFoldable(k)) return;
    RailFolded(k);
    g_railFold ^= 1 << k;
    std::wstring s;
    for (int i = 1; i <= 4; i++) if (g_railFold & (1 << i)) s += (wchar_t)(L'0' + i);
    WritePrivateProfileStringW(L"Lanceur", L"RailPlie", s.empty() ? NULL : s.c_str(), g_iniLauncher.c_str());
}
static int MscModCount();   // (mods.inc)
static RectF g_navR[TAB_COUNT];   // (bulles de la visite)
// Defilement de la barre (Markos, 24/10 : « a scroll feature » ; lanceur 1.15.1) : quand les pages ne tiennent plus, meme
// resserrees, la liste defile a la molette entre les boutons de mise a jour et Discord. g_railView : la zone visible ;
// les zones de clic des lignes y sont coupees (une ligne cachee ne prend pas le clic de Discord ou d'une mise a jour).
static float g_railScroll, g_railScrollMax;
static RectF g_railView;
static RectF RailClip(RectF r)
{
    if (g_railScrollMax <= 0) return r;
    RectF c;
    return RectF::Intersect(c, r, g_railView) ? c : RectF();
}

static void DrawNavItem(Graphics &g, RectF r, int page, int icon, const wchar_t *label)
{
    // (Addons : allume quand la page Voiture montre l'onglet Addons ; Voiture : quand elle montre ses couleurs)
    bool en = PageEnabled(page), on = g_state == ST_IDLE && (page == TAB_ADDONS ? g_tab == TAB_CAR && AddonsOn() : g_tab == page && !(page == TAB_CAR && AddonsOn())),
         hot = en && g_uiHot == UI_NAV + page;
    g_navR[page] = r;
    float a = en ? 1.0f : 0.38f;
    if (on || hot) {
        GraphicsPath p; RoundRect(p, r, 12);
        SolidBrush b(on ? TH(cardSel) : TH(card)); g.FillPath(&b, &p);
        if (on) { Pen pen(TH(choiceBorder), 1.0f); g.DrawPath(&pen, &p); }
    }
    Icon(g, icon, r.X + 22, r.Y + r.Height / 2, 20, WithA(on ? kAcc : kInk, on ? 1.0f : a * 0.82f), 1.9f);
    Text(g, label, RectF(r.X + 44, r.Y, r.Width - 70, r.Height), 14.5f, on ? FontStyleBold : FontStyleRegular, WithA(kInk, on ? 1.0f : a * 0.9f), StringAlignmentNear);
    // pastilles
    float rx = r.X + r.Width - 12;
    if (page == TAB_LOBBY && g_lobby != LB_NONE) {
        EnterCriticalSection(&g_lcs);
        int n = (int)g_peers.size();
        LeaveCriticalSection(&g_lcs);
        wchar_t b[16]; swprintf_s(b, L"%d/%d", n, g_lobbyMax);
        float w = PillW(b, 10.5f);
        Pill(g, RectF(rx - w, r.Y + (r.Height - 20) / 2, w, 20), WithA(kOk, 0.2f), b, kOk, 10.5f);
    } else if (page == TAB_CAR) {
        RectF d(rx - 15, r.Y + (r.Height - 14) / 2, 14, 14);
        if (g_carColor >= 0) { SolidBrush cb(CarRgb(g_carColor)); g.FillEllipse(&cb, d); }
        else DrawCarWheel(g, d);
        Pen e(Color(90, 255, 255, 255), 1.2f); g.DrawEllipse(&e, d);
    } else if (page == TAB_MODS && en) {
        int n = MscModCount();
        if (n > 0) { std::wstring s = std::to_wstring(n); float w = PillW(s, 10.5f); Pill(g, RectF(rx - w, r.Y + (r.Height - 20) / 2, w, 20), TH(cardSel), s, kInk, 10.5f); }
    } else if (page == TAB_ADDONS && en && !on) {   // (voitures du catalogue : NEW pas encore vue, UPDATE pour une installee)
        bool nw, up; AddonsBadges(nw, up);
        nw = nw || KuplaNew();   // (Kupla pas encore vu : modules.inc)
        if (up) { const wchar_t *s = L"UPDATE"; float w = PillW(s, 9) - 6; Pill(g, RectF(rx - w, r.Y + (r.Height - 18) / 2, w, 18), kWarn, s, Color(255, 255, 255, 255), 9); rx -= w + 4; }   // (etroites : les deux a cote du nom)
        if (nw) { const wchar_t *s = L"NEW"; float w = PillW(s, 9) - 6; Pill(g, RectF(rx - w, r.Y + (r.Height - 18) / 2, w, 18), kAcc, s, kOnAcc, 9); }
    } else if (page == TAB_NOTES && NotesUnseen() && !on) {
        const wchar_t *s = T(L"NOUV.", L"NEW");
        float w = PillW(s, 10);
        Pill(g, RectF(rx - w, r.Y + (r.Height - 20) / 2, w, 20), kAcc, s, kOnAcc, 10);
    }
    if (on && page != TAB_LOBBY && page != TAB_MODS && page != TAB_CAR) { SolidBrush db(kAcc); g.FillEllipse(&db, rx - 6, r.Y + r.Height / 2 - 3, 6.0f, 6.0f); }
    if (en) { RectF z = RailClip(r); if (z.Height > 0) HotZone(z, UI_NAV + page); }
}

static void DrawRail(Graphics &g)
{
    Glass(kRail, 22);
    if (g_logo) {
        float lw = 168, lh = lw * g_logo->GetHeight() / g_logo->GetWidth();
        g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        g.DrawImage(g_logo, RectF(kRail.X + (kRail.Width - lw) / 2, 26, lw, lh));
    }
    // version (plus de pastille PRE-ALPHA depuis 0.75.35, demande de JD ; un ancien suffixe "-prealpha" n'est pas montre)
    std::wstring ver = g_localVer.empty() ? L"" : L"v" + g_localVer.substr(0, g_localVer.find(L'-'));
    float vw = ver.empty() ? 0 : PillW(ver, 10.5f), x = kRail.X + (kRail.Width - vw) / 2;
    if (vw > 0) Pill(g, RectF(x, 104, vw, 20), TH(cardSel), ver, kInk, 10.5f);
    // jeu, sous le numero (JD, 19/10, a la place de Legacy / Reworked) : MSC / MWC ; clic : saves.inc GameSet, le lanceur se
    // relance sur l'autre jeu. (Etiquette BETA sur MSC retiree : decision de JD, 27/10.)
    g_canalRect = RectF(28, 130, 208, 30);
    UiSeg(g, g_canalRect, { L"MSC", L"MWC" }, g_game == GAME_MSC ? 0 : 1, UI_SET_CANAL, 12);
    float y0 = 168;
    if (g_btn[B_UPDATE].visible) {   // mise a jour proposee : bouton sous la version
        Button &b = g_btn[B_UPDATE];
        b.r = RectF(28, 170, 208, 40);
        float pulse = b.enabled ? 0.5f + 0.5f * sinf(g_time * 3.0f) : 0.0f;
        GraphicsPath gp; RoundRect(gp, RectF(b.r.X - 3, b.r.Y - 3, b.r.Width + 6, b.r.Height + 6), 15);
        SolidBrush glow(WithA(kAcc, 0.10f + 0.14f * pulse * (1 - b.hover))); g.FillPath(&glow, &gp);
        GraphicsPath p; RoundRect(p, b.r, 12);
        SolidBrush fb(WithA(kAcc, b.enabled ? 1.0f : 0.45f)); g.FillPath(&fb, &p);
        if (b.hover > 0.01f) { SolidBrush hb(Color((BYTE)(60 * b.hover), 255, 255, 255)); g.FillPath(&hb, &p); }
        std::wstring v = UpdAvail();
        size_t dash = v.find(L'-');
        if (dash != std::wstring::npos) v.resize(dash);
        Icon(g, IC_DOWNLOAD, b.r.X + 20, b.r.Y + b.r.Height / 2, 17, kOnAcc, 2.2f);
        Text(g, std::wstring(T(L"Mettre à jour · ", L"Update · ")) + v, RectF(b.r.X + 34, b.r.Y, b.r.Width - 40, b.r.Height), 13, FontStyleBold, kOnAcc, StringAlignmentNear);
        y0 = 216;
    }
    if (g_btn[B_UPDLANCEUR].visible) {   // lanceur (flux a part depuis 1.15.0) : sous celui du mod, plus discret
        Button &b = g_btn[B_UPDLANCEUR];
        b.r = RectF(28, y0 + 2, 208, 34);
        GraphicsPath p; RoundRect(p, b.r, 11);
        SolidBrush fb(Mix(WithA(kAcc, 0.16f), WithA(kAcc, 0.30f), b.hover)); g.FillPath(&fb, &p);
        Pen pen(WithA(kAcc, b.enabled ? 0.9f : 0.4f), 1.2f); g.DrawPath(&pen, &p);
        std::wstring v = LancAvail();
        Icon(g, IC_DOWNLOAD, b.r.X + 20, b.r.Y + b.r.Height / 2, 15, WithA(kAcc, b.enabled ? 1.0f : 0.5f), 2.0f);
        Text(g, std::wstring(T(L"Lanceur · ", L"Launcher · ")) + v.substr(0, v.find(L'-')), RectF(b.r.X + 34, b.r.Y, b.r.Width - 40, b.r.Height), 12, FontStyleBold, kInk, StringAlignmentNear);
        y0 += 40;
    }
    // groupes jusqu'a Reglages (674) : lignes de 44 px, ou de 40 quand le bouton de mise a jour prend de la place
    int nItems = 0;
    for (int k = 0, i = 0; k < kNavGroups; k++)
        for (int j = 0; j < kNavGroupN[k]; j++, i++) if (!NavHidden(kNav[i].page) && (!RailFolded(k) || kNav[i].page == g_tab)) nItems++;
    float avail = 712 - y0, lab = 26, step = 46;
    if (avail - kNavGroups * lab - nItems * step < 6) { lab = 24; step = 42; }
    if (avail - kNavGroups * lab - nItems * step < 6) { lab = 22; step = 40; }   // (bouton de mise a jour + 9 pages)
    if (avail - kNavGroups * lab - nItems * step < 6) { lab = 20; step = 37; }   // (11 pages depuis MEDIA)
    if (avail - kNavGroups * lab - nItems * step < 6) { lab = 18; step = 34; }
    // (toujours trop : les deux boutons de mise a jour, mod et lanceur, et toutes les pages -- lignes resserrees jusqu'a 28 px ;
    // avant 1.15.0, la derniere page passait sous Discord)
    if (avail - kNavGroups * lab - nItems * step < 6 && nItems > 0) { lab = 16; step = max(28.0f, floorf((avail - kNavGroups * lab - 6) / nItems)); }
    // (encore trop, meme a 28 px : la liste defile, Markos 24/10 -- plus de place entre les groupes)
    float content = kNavGroups * lab + nItems * step;
    g_railView = RectF(20, y0, 228, avail);
    g_railScrollMax = content > avail ? content - avail + 6 : 0;
    float gap = g_railScrollMax > 0 ? 0 : min(20.0f, max(0.0f, (avail - content) / (kNavGroups - 1)));
    {   // (page choisie hors de la vue, a son changement : ramenee dedans)
        static int lastTab = -1;
        if (g_railScrollMax > 0 && g_tab != lastTab) {
            float ty = y0, s = g_railScroll;
            for (int k = 0, i = 0; k < kNavGroups; k++) {
                ty += lab;
                for (int j = 0; j < kNavGroupN[k]; j++, i++) {
                    if (NavHidden(kNav[i].page) || (RailFolded(k) && kNav[i].page != g_tab)) continue;
                    if (kNav[i].page == g_tab) { if (ty - s < y0) s = ty - y0; else if (ty + step - s > y0 + avail) s = ty + step - y0 - avail; }
                    ty += step;
                }
            }
            g_railScroll = s;
        }
        lastTab = g_tab;
    }
    g_railScroll = g_railScrollMax > 0 ? min(max(g_railScroll, 0.0f), g_railScrollMax) : 0;
    float y = y0 - g_railScroll;
    GraphicsState railSt = g.Save();
    if (g_railScrollMax > 0) g.SetClip(g_railView, CombineModeIntersect);
    for (int k = 0, i = 0; k < kNavGroups; k++) {
        SmallLabel(g, T(kNavGroupFr[k], kNavGroupEn[k]), RectF(40, y + 2, 190, 20), WithA(kGrey, 0.9f));
        if (RailFoldable(k)) {   // chevron a droite du titre ; clic sur la ligne du titre : plier / deplier
            RectF hr(28, y, 208, lab);
            bool hot = g_uiHot == UI_NAV_GROUP + k;
            Icon(g, RailFolded(k) ? IC_CHEVR : IC_CHEVD, 220, y + 12, 12, WithA(hot ? kAcc : kGrey, 0.9f), 2.0f);
            RectF hz = RailClip(hr);
            if (hz.Height > 0) HotZone(hz, UI_NAV_GROUP + k);
        }
        y += lab;
        for (int j = 0; j < kNavGroupN[k]; j++, i++) {
            const NavItem &it = kNav[i];
            if (NavHidden(it.page) || (RailFolded(k) && it.page != g_tab)) continue;
            DrawNavItem(g, RectF(28, y, 208, step - 2), it.page, it.icon, T(it.fr, it.en));
            y += step;
        }
        y += gap;
    }
    g.Restore(railSt);
    if (g_railScrollMax > 0) {   // barre de defilement, fine, a droite des lignes
        float th = max(30.0f, avail * avail / (avail + g_railScrollMax)), ty = y0 + (avail - th) * g_railScroll / g_railScrollMax;
        GraphicsPath tp; RoundRect(tp, RectF(240, ty, 4, th), 2);
        SolidBrush tb(WithA(kGrey, 0.55f)); g.FillPath(&tb, &tp);
    }
    // bas de la barre : Discord, le code source (le < >, GitHub) et Soutenir (coeur, Ko-fi). Retour de JD (18/10) : le < >
    // rouvre le depot comme avant ; Journaux, API moddeurs et Credits sont revenus dans l'en-tete (HeadPage).
    for (int id : { B_DISCORD, B_GITHUB, B_KOFI }) {
        Button &b = g_btn[id];
        GraphicsPath p; RoundRect(p, b.r, 12);
        if (id == B_DISCORD) {   // Discord : bleu Discord, logo et nom
            SolidBrush fb(kDiscordBlue); g.FillPath(&fb, &p);
            if (b.hover > 0.01f) { SolidBrush hb(Color((BYTE)(55 * b.hover), 255, 255, 255)); g.FillPath(&hb, &p); }
            Icon(g, IC_DISCORD, b.r.X + 24, b.r.Y + b.r.Height / 2, 20, Color(255, 255, 255, 255));
            Text(g, L"Discord", RectF(b.r.X + 40, b.r.Y, b.r.Width - 44, b.r.Height), 13, FontStyleBold, Color(255, 255, 255, 255), StringAlignmentNear);
            continue;
        }
        SolidBrush fb(Mix(TH(card), TH(cardSel), b.hover)); g.FillPath(&fb, &p);
        Pen pen(TH(choiceBorder), 1.0f); g.DrawPath(&pen, &p);
        bool gh = id == B_GITHUB;
        Icon(g, gh ? IC_CODE : IC_HEART, b.r.X + b.r.Width / 2, b.r.Y + b.r.Height / 2, 18, gh ? kInk : Color(255, 255, 143, 163), 2.1f);
    }
}

// ---- en-tete et pied
static std::wstring Greeting()
{
    SYSTEMTIME st; GetLocalTime(&st);
    std::wstring n = PlayerName();
    if (st.wHour >= 5 && st.wHour < 18) return std::wstring(st.wHour < 12 ? T(L"Bonjour, ", L"Good morning, ") : T(L"Bonjour, ", L"Good afternoon, ")) + n;
    return std::wstring(T(L"Bonsoir, ", L"Good evening, ")) + n;
}
static void DrawLobbyHeadControls(Graphics &g, float &right);   // (salon, plus bas)

// Couleur de l'arc-en-ciel (teinte h en degres).
static Color Hue(float h, BYTE a = 255)
{
    h = fmodf(h, 360.0f); if (h < 0) h += 360.0f;
    float x = 1.0f - fabsf(fmodf(h / 60.0f, 2.0f) - 1.0f), r = 0, gg = 0, b = 0;
    int k = (int)(h / 60.0f);
    if (k == 0) { r = 1; gg = x; } else if (k == 1) { r = x; gg = 1; } else if (k == 2) { gg = 1; b = x; }
    else if (k == 3) { gg = x; b = 1; } else if (k == 4) { r = x; b = 1; } else { r = 1; b = x; }
    return Color(a, (BYTE)(55 + 200 * r), (BYTE)(55 + 200 * gg), (BYTE)(55 + 200 * b));   // (pastel : lisible sur le verre clair comme sombre)
}

// Etoile des credits (demande de JD, 09/10 : « un effet RGB moderne ») : halo qui change de teinte, contour en degrade
// arc-en-ciel qui tourne, du meme dessin que IC_STAR.
static void RgbStar(Graphics &g, float cx, float cy, float size, float t, bool on)
{
    float hue = t * 70.0f;
    GraphicsPath glow; glow.AddEllipse(cx - size * 0.95f, cy - size * 0.95f, size * 1.9f, size * 1.9f);
    PathGradientBrush gb(&glow);
    gb.SetCenterColor(Hue(hue, (BYTE)(on ? 120 : 80)));
    Color edge(0, 0, 0, 0); int ne = 1; gb.SetSurroundColors(&edge, &ne);
    g.FillPath(&gb, &glow);
    float k = size / 24.0f;
    static const float sp[] = { 12, 3.5f, 14.35f, 9.26f, 20.56f, 9.72f, 15.8f, 13.74f, 17.29f, 19.78f, 12, 16.5f, 6.71f, 19.78f, 8.2f, 13.74f, 3.44f, 9.72f, 9.65f, 9.26f };
    PointF pts[10];
    for (int i = 0; i < 10; i++) pts[i] = PointF(cx + (sp[2 * i] - 12) * k, cy + (sp[2 * i + 1] - 12.2f) * k);
    LinearGradientBrush lb(RectF(cx - size / 2, cy - size / 2, size, size), Hue(hue), Hue(hue + 180), fmodf(t * 90.0f, 360.0f));
    Color cols[5] = { Hue(hue), Hue(hue + 90), Hue(hue + 180), Hue(hue + 270), Hue(hue + 360) };
    REAL pos[5] = { 0.0f, 0.25f, 0.5f, 0.75f, 1.0f };
    lb.SetInterpolationColors(cols, pos, 5);
    SolidBrush fill(Hue(hue + 40, 60)); g.FillPolygon(&fill, pts, 10);
    Pen pen(&lb, 2.2f * k);
    pen.SetLineJoin(LineJoinRound);
    g.DrawPolygon(&pen, pts, 10);
}

// Raccourcis de page de l'en-tete : API moddeurs, Journaux (pastille rouge : la derniere partie s'est arretee brutalement)
// et Credits (etoile RGB). Contour accent quand la page est ouverte. (Remis a la demande de JD, 18/10.)
static void HeadPage(Graphics &g, RectF r, int page, int icon)
{
    bool en = PageEnabled(page), on = g_tab == page && g_state == ST_IDLE, hot = en && g_uiHot == UI_NAV + page;
    g_navR[page] = r;
    GraphicsPath p; RoundRect(p, r, 14);
    SolidBrush fb(on ? TH(cardSel) : Mix(TH(btn2), TH(btn2Hot), hot ? 1.0f : 0.0f)); g.FillPath(&fb, &p);
    Pen pen(on ? WithA(kAcc, 0.8f) : TH(fieldBorder), on ? 1.4f : 1.0f); g.DrawPath(&pen, &p);
    if (page == TAB_CREDITS) {   // contributeurs : etoile RGB, contour de la meme teinte
        Pen rp(Hue(g_time * 70.0f + 120.0f, (BYTE)(on || hot ? 200 : 110)), on ? 1.6f : 1.2f); g.DrawPath(&rp, &p);
        RgbStar(g, r.X + r.Width / 2, r.Y + r.Height / 2, 20, g_time, on || hot);
    } else Icon(g, icon, r.X + r.Width / 2, r.Y + r.Height / 2, 18, WithA(on ? kAcc : kInk, en ? 1.0f : 0.4f), 2.0f);
    if (page == TAB_LOGS && g_crashT && !on) {
        RectF d(r.X + r.Width - 13, r.Y - 3, 16, 16);
        SolidBrush rb(kRed); g.FillEllipse(&rb, d);
        Text(g, L"!", d, 10.5f, FontStyleBold, Color(255, 255, 255, 255));
    }
    if (en) { RectF z = RailClip(r); if (z.Height > 0) HotZone(z, UI_NAV + page); }
}
static float g_headRight = 940;   // bord gauche des raccourcis de l'en-tete (champ Chercher de la visite : tuto.inc)
static void DrawGuideHead(Graphics &g, float &right);   // (wiki.inc : champ Chercher et pilules du Guide)
static void DrawHeader(Graphics &g)
{
    std::wstring title, sub;
    if (g_state == ST_RUNNING) { title = T(L"Partie en cours", L"Game running"); sub = g_launchInfo; }
    else if (g_state == ST_LAUNCH || g_state == ST_CLOSING) { title = T(L"Lancement", L"Starting"); sub = g_launchInfo; }
    else switch (g_tab) {
    case TAB_HOME: title = g_gameDir.empty() ? std::wstring(T(L"Bienvenue", L"Welcome")) : Greeting(); sub = g_game == GAME_MSC ? T(L"Prêt pour un été à plusieurs ? Tes amis doivent choisir le même chemin que toi.", L"Ready for a summer together? Your friends must pick the same path as you.")
                                      : T(L"Prêt pour un hiver à plusieurs ? Tes amis doivent choisir le même chemin que toi.", L"Ready for a winter together? Your friends must pick the same path as you."); break;
    case TAB_LOBBY: {
        int lobby = g_lobby;
        bool host = lobby == LB_HOST;
        // (maquette du 17/10) hote : « Steam · Prive · code K7PM3Q · <sauvegarde> · n / max joueurs » (IP : « IP · port 7870 · … ») ;
        // invite : « Tu es invite · IP 192.168.1.20:7870 (ou Steam) · Prive · n / max joueurs », titre « Salon de <hote> ».
        EnterCriticalSection(&g_lcs);
        int n = (int)g_peers.size();
        std::string hn;
        for (auto &p : g_peers) if (p.id == 0) hn = p.name;
        LeaveCriticalSection(&g_lcs);
        if (lobby == LB_CONNECTING) title = T(L"Connexion au salon…", L"Joining the lobby…");
        else if (host) title = std::wstring(T(L"Salon de ", L"Lobby of ")) + PlayerName();
        else if (!hn.empty()) title = std::wstring(T(L"Salon de ", L"Lobby of ")) + Widen(hn, CP_UTF8);
        else if (g_lobbySteam) title = std::wstring(T(L"Salon de ", L"Lobby of ")) + g_sHostName;
        else title = T(L"Salon", L"Lobby");
        wchar_t b[300];
        std::wstring mode = lobby == LB_CONNECTING ? std::wstring(L"…") : LobbyModeText();   // (Public, ou Prive et le code)
        if (host) {
            std::wstring sn = SaveCurrentName(), via = g_lobbySteam ? L"Steam" : std::wstring(T(L"IP · port ", L"IP · port ")) + std::to_wstring(g_lobbyPort);
            if (sn.size() > 28) sn = sn.substr(0, 27) + L"…";
            swprintf_s(b, T(L"%s · %s · %s · %d / %d joueurs", L"%s · %s · %s · %d / %d players"), via.c_str(), mode.c_str(), sn.c_str(), n, g_lobbyMax);
        } else {
            std::wstring via = g_lobbySteam ? L"Steam" : L"IP " + g_lobbyAddr + L":" + std::to_wstring(g_lobbyPort);
            std::wstring pv = lobby == LB_CONNECTING ? std::wstring(L"…") : g_lobbyPrive ? T(L"Privé", L"Private") : T(L"Public", L"Public");
            swprintf_s(b, T(L"Tu es invité · %s · %s · %d / %d joueurs", L"You are a guest · %s · %s · %d / %d players"), via.c_str(), pv.c_str(), n, g_lobbyMax);
        }
        sub = b;
        break;
    }
    case TAB_SKIN: case TAB_CAR: title = T(L"Personnage", L"Character"); sub = T(L"Ce que les autres voient de toi : tenue, visage, voiture. Visible dès que tu entres dans un salon.", L"What the others see of you: outfit, face, car. Visible as soon as you enter a lobby."); break;
    case TAB_CONTENT: title = T(L"Contenu envoyé", L"Sent content"); sub = T(L"Tes fichiers perso, copiés chez les invités de ton salon. Ils voient et entendent la même chose que toi.", L"Your personal files, copied to your lobby's guests. They see and hear the same as you."); break;
    case TAB_MODS: title = T(L"Mods", L"Mods"); sub = T(L"Tes mods MSCLoader, et ceux que tu envoies à tes invités quand tu héberges.", L"Your MSCLoader mods, and those you send to your guests when you host."); break;
    case TAB_NOTES: case TAB_CREDITS: case TAB_WIKI:   // (une page, trois pilules : wiki.inc DrawGuideHead)
        title = T(L"Guide", L"Guide"); sub = T(L"Jouer ensemble, partage, versions.", L"Play together, sharing, versions."); break;   // (court : les pilules et Chercher prennent la place)
    case TAB_LOGS: title = T(L"Journaux", L"Logs"); sub = T(L"Un souci en jeu ? Un zip à envoyer", L"Trouble in game? A zip to send"); break;
    case TAB_COOP: case TAB_GFX: title = T(L"Réglages", L"Settings"); sub = SetHeaderSub(); break;   // (gfx.inc : une page, des onglets)
    case TAB_API: title = T(L"API pour les moddeurs", L"API for modders"); sub = T(L"MWCoop.CoopApi : la doc, la DLL et un exemple", L"MWCoop.CoopApi: docs, DLL and an example"); break;
    case TAB_TOOLS: title = T(L"Outils", L"Tools"); sub = T(L"Des programmes à part pour aller plus loin, à télécharger d'un clic.", L"Separate programs to go further, downloaded in one click."); break;
    case TAB_VHS: case TAB_ALBUM:
        if (g_game == GAME_MSC) { title = T(L"Album", L"Album"); sub = T(L"Les photos prises au drone.", L"The photos taken with the drone."); break; }   // (pas de VHS sur MSC)
        title = T(L"VHS & album", L"VHS & album"); sub = T(L"Tes cassettes pour la télé du salon, et les photos prises au drone.", L"Your tapes for the living-room TV, and the photos taken with the drone."); break;
    case TAB_BUG: title = T(L"Signaler un bug", L"Report a bug"); sub = SigActive() || g_sigCapture ? T(L"Tes dossiers et le formulaire, ici dans le lanceur · le zip des journaux joint tout seul", L"Your reports and the form, right here in the launcher · the logs zip attached for you") : T(L"Le formulaire du site, dans le lanceur · tes journaux en un clic", L"The site's form, inside the launcher · your logs in one click"); break;   // (signalement.inc)
    case TAB_SAVES: title = T(L"Sauvegardes", L"Saves"); sub = T(L"Tes parties MWCoop, à part de la sauvegarde solo du jeu. L'hôte choisit laquelle lancer ; les invités la reçoivent.", L"Your MWCoop games, apart from the game's solo save. The host picks which one to start; guests receive it."); break;
    }
    // commandes de droite : fermer, reduire, theme, langue | API moddeurs, Journaux, Credits (retour de JD, 18/10 : comme
    // avant le rework), puis celles de la page.
    float right = 1264;
    for (int id : { B_CLOSE, B_MIN, B_THEME, B_LANG }) { g_btn[id].r = RectF(right - 44, 22, 44, 44); right -= 54; }
    if (g_newUi) right = g_pageR.X + g_pageR.Width - 8;   // (nouvelle interface : les raccourcis sont dans l'en-tete de la coque ; ici, la ligne de titre et les commandes de la page, jusqu'au bord de la colonne fluide)
    else {
    HeadButton(g, B_CLOSE, IC_CLOSE);
    HeadButton(g, B_MIN, IC_MIN);
    HeadButton(g, B_THEME, g_dark ? IC_SUN : IC_MOON);
    HeadButton(g, B_LANG, 0, g_fr ? L"FR" : g_ru ? L"RU" : g_ar ? L"AR" : L"EN");
    {
        Pen sp(TH(fieldBorder), 1.4f);
        g.DrawLine(&sp, right - 2, 32.0f, right - 2, 56.0f);
        right -= 14;
        HeadPage(g, RectF(right - 44, 22, 44, 44), TAB_API, IC_BRACES);
        right -= 54;
        HeadPage(g, RectF(right - 44, 22, 44, 44), TAB_LOGS, IC_FILE);
        right -= 54;
        // Signaler un bug (demande de JD, 26/10 : a cote des Journaux) : le formulaire du site et le zip des journaux
        // (launcher.cpp BugReport). Son nom s'affiche dessous au survol.
        g_btn[B_BUG].r = RectF(right - 44, 22, 44, 44);
        HeadButton(g, B_BUG, IC_BUG);
        right -= 54;
        HeadPage(g, RectF(right - 44, 22, 44, 44), TAB_CREDITS, IC_STAR);
        right -= 54;
        // Outils (editeur de carte, 20/10 : « a gauche, un peu a part de l'etoile, separe d'un | ») : outils.inc
        g.DrawLine(&sp, right - 2, 32.0f, right - 2, 56.0f);
        right -= 14;
        HeadPage(g, RectF(right - 44, 22, 44, 44), TAB_TOOLS, IC_TOOLS);
        right -= 54;
    }
    }
    g_headRight = right;
    if (!g_newUi && g_btn[B_BUG].hover > 0.05f) {   // (etiquette sous le bouton Signaler un bug)
        const wchar_t *tl = T(L"Signaler un bug", L"Report a bug");
        float a = std::min(1.0f, g_btn[B_BUG].hover), tw = MeasureW(MeasureG(), tl, 12.5f, FontStyleBold) + 24;
        RectF br = g_btn[B_BUG].r, tr(br.X + br.Width / 2 - tw / 2, br.Y + br.Height + 8, tw, 28);
        GraphicsPath tp; RoundRect(tp, tr, 10);
        SolidBrush tb(WithA(TH(solid), a)); g.FillPath(&tb, &tp);
        Pen tpen(WithA(TH(fieldBorder), a), 1.0f); g.DrawPath(&tpen, &tp);
        Text(g, tl, tr, 12.5f, FontStyleBold, WithA(kInk, a));
    }
    if (g_state == ST_IDLE) {
        if (g_tab == TAB_SAVES) {   // (saves.inc)
            UiButton(g, RectF(right - 164, 22, 164, 44), T(L"Nouvelle partie", L"New game"), UI_SAVE_NEW, true, IC_HOMEPLUS); right -= 174;
            // Importer une sauvegarde (demande de JD, 27/10 : une sauvegarde telechargee, .zip / .rar / .7z) : a gauche de Nouvelle partie
            // (« Importer » : le nom entier, au survol -- en entier, le titre de la page ne tenait plus)
            const wchar_t *il = T(L"Importer", L"Import");
            float iw = MeasureW(MeasureG(), il, 13.5f, FontStyleBold) + 24 + 36;
            g_saveImportR = RectF(right - iw, 22, iw, 44);
            UiButton(g, g_saveImportR, il, UI_SAVE_IMPORT, false, IC_DOWNLOAD); right -= iw + 10;
            if (g_uiHot == UI_SAVE_IMPORT && g_saveModal == SM_NONE) {
                const wchar_t *tl = T(L"Importer une sauvegarde (.zip, .rar, .7z)", L"Import a save (.zip, .rar, .7z)");
                float tw = MeasureW(MeasureG(), tl, 12.5f, FontStyleBold) + 24;
                RectF br = g_saveImportR, tr(br.X + br.Width / 2 - tw / 2, br.Y + br.Height + 8, tw, 28);
                GraphicsPath tp; RoundRect(tp, tr, 10);
                SolidBrush tb(TH(solid)); g.FillPath(&tb, &tp);
                Pen tpen(TH(fieldBorder), 1.0f); g.DrawPath(&tpen, &tp);
                Text(g, tl, tr, 12.5f, FontStyleBold, kInk);
            }
        }
        if (g_tab == TAB_CONTENT || g_tab == TAB_MODS) {
            int n = MscModCount();
            std::vector<std::wstring> lab = { std::wstring(T(L"Contenu · ", L"Content · ")) + ContentSummary(), std::wstring(L"Mods · ") + std::to_wstring(n) };
            float w = 250.0f;   // (280 avant le bouton Signaler un bug, 26/10 : titre « Contenu envoye » coupe)
            RectF r(right - w, 22, w, 44);
            UiSeg(g, r, lab, g_tab == TAB_CONTENT ? 0 : 1, UI_SEG, 13);
            right -= w + 10;
        } else if (g_tab == TAB_SKIN || g_tab == TAB_CAR) { g_persoTabRight = right; PersoTabsDraw(g); right -= PersoTabsW() + 10; }   // (Tenue / Visage / Voiture : tenue.inc)
        else if (g_tab == TAB_VHS || g_tab == TAB_ALBUM) { g_mediaTabRight = right; MediaTabsDraw(g); right -= MediaTabsW() + 10; }   // (VHS / Album : media.inc)
        else if (g_tab == TAB_WIKI || g_tab == TAB_NOTES || g_tab == TAB_CREDITS) DrawGuideHead(g, right);   // (Chercher, Guide / Nouveautes / Credits : wiki.inc)
        else if (g_tab == TAB_LOBBY) DrawLobbyHeadControls(g, right);
        else if (g_tab == TAB_GFX || g_tab == TAB_COOP) DrawSetSearchBox(g, right);   // (Chercher un reglage)
    }
    // (titre trop long a cote des commandes de la page -- russe, bouton Signaler un bug du 26/10 : plus petit, jusqu'a 20 px)
    float tpx = 26;
    while (tpx > 20 && MeasureTitleW(MeasureG(), title, tpx) > right - 282) tpx -= 1;
    // Guide, Nouveautes, Credits : la pilule choisie dit deja la page -- titre qui ne tient pas, meme a 20 px (anglais, pastille
    // d'une nouvelle version sur Nouveautes) : ni titre ni sous-titre, plutot qu'un « G… ».
    if ((g_tab == TAB_WIKI || g_tab == TAB_NOTES || g_tab == TAB_CREDITS) && g_state == ST_IDLE && MeasureTitleW(MeasureG(), title, tpx) > right - 282) return;
    Title(g, title, RectF(268, 16, right - 280, 34), tpx, kInk);
    if (MeasureW(MeasureG(), sub, 13.5f, FontStyleRegular) > right - 282) {   // (trop long a cote des commandes de la page : la premiere phrase seulement)
        size_t dot = sub.find(L". ");
        if (dot != std::wstring::npos) sub.resize(dot + 1);
    }
    Text(g, sub, RectF(270, 50, right - 282, 20), 13.5f, FontStyleRegular, kGrey, StringAlignmentNear);
}

static void DrawFooter(Graphics &g, const std::wstring &status, int kind, float prog)
{
    Color sc = kind == K_OK ? kOk : kind == K_WARN ? kWarn : kind == K_ERR ? kRed : kGrey;
    float x = kFooter.X + 4, cy = kFooter.Y + 13;
    // a droite : le jeu trouve, changer de dossier
    Button &eb = g_btn[B_EXE];
    bool ok = !g_gameDir.empty();
    const wchar_t *el = ok ? T(L"Changer de dossier", L"Change folder") : T(L"Choisir l'exe…", L"Choose exe…");
    Graphics &mg = MeasureG();
    float lw = MeasureW(mg, el, 12.5f, FontStyleRegular) + 4;
    eb.r = RectF(kFooter.X + kFooter.Width - lw, kFooter.Y + 2, lw, 22);
    if (eb.visible) Text(g, el, eb.r, 12.5f, FontStyleUnderline, WithA(Mix(kGrey, kAcc, eb.hover), eb.enabled ? 1.0f : 0.4f), StringAlignmentFar);
    std::wstring gl = ok ? std::wstring(GM().name) + L" " + g_gameVer : std::wstring(T(L"My Winter Car introuvable", L"My Winter Car not found"));
    float gw = MeasureW(mg, gl, 12.5f, FontStyleBold) + 6;
    float gx = eb.r.X - 14 - gw;
    if (ok) Icon(g, IC_CHECK, gx - 10, cy, 15, kOk, 2.6f);
    Text(g, gl, RectF(gx, kFooter.Y + 2, gw, 22), 12.5f, FontStyleBold, ok ? kInk : kRed, StringAlignmentNear);
    // a gauche : l'etat
    SolidBrush db(sc); g.FillEllipse(&db, x, cy - 4, 8.0f, 8.0f);
    float sw = gx - 30 - (x + 16);
    Text(g, status, RectF(x + 16, kFooter.Y + 2, sw, 22), 13, FontStyleBold, kind == K_NORMAL ? kInk : sc, StringAlignmentNear);
    if (g_tab == TAB_LOBBY && (g_lobby == LB_HOST || g_lobby == LB_GUEST) && !g_lobbySteam) {   // salon IP : « · Ping moyen NN ms » (maquette du 17/10)
        int sum = 0, cnt = 0;
        EnterCriticalSection(&g_lcs);
        for (auto &p : g_peers) if (p.id != 0 && p.ping > 0) { sum += p.ping; cnt++; }
        LeaveCriticalSection(&g_lcs);
        float tw = MeasureW(mg, status, 13, FontStyleBold) + 10;
        if (cnt && tw + 140 < sw) {
            wchar_t pb[64];
            swprintf_s(pb, T(L"·  Ping moyen %d ms", L"·  Average ping %d ms"), sum / cnt);
            Text(g, pb, RectF(x + 16 + tw, kFooter.Y + 2, sw - tw, 22), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        }
    }
    if (prog != -1.0f) DrawBar(g, RectF(x + 16, kFooter.Y + 26, min(320.0f, sw), 4), prog);
}

// ---- page ACCUEIL
static void DrawField(Graphics &g, int i, const wchar_t *label);
static void DrawHomeLook(Graphics &g, RectF stage);
static std::vector<std::wstring> NoteBullets(const Note &n, int max);
static void DrawHome(Graphics &g)
{
    if (g_gameDir.empty()) {   // jeu introuvable : choisir l'exe, ou l'acheter
        RectF c(g_pageR.X + 150, g_pageR.Y + 110, 700, 380);
        Glass(c);
        Icon(g, IC_ALERT, c.X + c.Width / 2, c.Y + 64, 40, kWarn, 2.0f);
        Title(g, T(L"My Winter Car introuvable", L"My Winter Car not found"), RectF(c.X, c.Y + 96, c.Width, 40), 26, kInk, StringAlignmentCenter);
        Para(g, T(L"MWCoop cherche le jeu dans tes bibliothèques Steam et ne l'a pas trouvé. Montre-lui mywintercar.exe (Steam : clic droit sur My Winter Car > Gérer > Parcourir les fichiers locaux).",
                  L"MWCoop looks for the game in your Steam libraries and did not find it. Show it mywintercar.exe (Steam: right-click My Winter Car > Manage > Browse local files)."),
             RectF(c.X + 60, c.Y + 150, c.Width - 120, 70), 14, kGrey, StringAlignmentNear);
        g_btn[B_EXE].visible = true;
        UiButton(g, RectF(c.X + 120, c.Y + 250, 220, 50), T(L"Choisir l'exe…", L"Choose exe…"), UI_SET_FOLDER, true, IC_FOLDER);
        UiButton(g, RectF(c.X + 360, c.Y + 250, 220, 50), T(L"Acheter le jeu", L"Buy the game"), UI_SET_BUY, false);
        Para(g, T(L"Mod non officiel et non commercial · non affilié à Amistech Games · nécessite une copie légale de My Winter Car.",
                  L"Unofficial, non-commercial mod · not affiliated with Amistech Games · requires a legal copy of My Winter Car."),
             RectF(c.X + 40, c.Y + 324, c.Width - 80, 40), 12, kGrey, StringAlignmentNear);
        return;
    }
    // --- a gauche : le personnage et le pseudo
    RectF left(g_pageR.X, g_pageR.Y, 340, g_pageR.Height);
    Glass(left);
    Title(g, T(L"Ton personnage", L"Your character"), RectF(left.X + 20, left.Y + 14, 200, 24), 16, kInk);
    {
        bool hot = g_uiHot == UI_HOME_LOOK;
        Text(g, T(L"Modifier", L"Edit"), RectF(left.X + left.Width - 120, left.Y + 16, 100, 20), 13, hot ? FontStyleBold | FontStyleUnderline : FontStyleBold, kAcc, StringAlignmentFar);
        HotZone(RectF(left.X + left.Width - 100, left.Y + 12, 84, 28), UI_HOME_LOOK);
    }
    RectF stage(left.X + 18, left.Y + 44, 304, 430);
    DrawHomeLook(g, stage);
    HotZone(stage, UI_HOME_LOOK);
    SmallLabel(g, T(L"PSEUDO", L"NICKNAME"), RectF(left.X + 20, left.Y + 486, 200, 18), kGrey);
    g_fields[0].r = RectF(left.X + 18, left.Y + 508, 304, 48);
    DrawField(g, 0, L"");
    {   // tenue, voiture
        RectF a(left.X + 18, left.Y + 572, 148, 44), b(left.X + 174, left.Y + 572, 148, 44);
        UiButton(g, a, T(L"Tenue", L"Outfit"), UI_HOME_LOOK + 100, false, IC_SHIRT);
        UiButton(g, b, T(L"Voiture", L"Car"), UI_HOME_CAR, false, IC_CAR);
        RectF d(b.X + b.Width - 26, b.Y + 15, 14, 14);
        if (g_carColor >= 0) { SolidBrush cb(CarRgb(g_carColor)); g.FillEllipse(&cb, d); } else DrawCarWheel(g, d);
    }
    // --- a droite : par ou jouer, heberger / rejoindre, solo, nouveautes
    float rx = g_pageR.X + 360, rw = g_pageR.Width - 360;
    int lobby = g_lobby;
    RectF net(rx, g_pageR.Y, rw, 188);
    Glass(net);
    Title(g, T(L"Par où jouer", L"How to connect"), RectF(net.X + 20, net.Y + 16, 300, 24), 17, kInk);
    Text(g, T(L"Tes amis doivent choisir le même", L"Your friends must pick the same"), RectF(net.X + 300, net.Y + 16, net.Width - 320, 24), 12.5f, FontStyleRegular, kGrey, StringAlignmentFar);
    for (int id : { B_NETSTEAM, B_NETIP }) {
        Button &b = g_btn[id];
        bool steam = id == B_NETSTEAM, on = steam == g_steamNet;
        b.r = RectF(net.X + 20 + (steam ? 0 : (net.Width - 40) / 2 + 5), net.Y + 50, (net.Width - 40) / 2 - 5, 64);
        GraphicsPath p; RoundRect(p, b.r, 16);
        SolidBrush fb(on ? WithA(kAcc, 0.16f) : Mix(TH(card), TH(cardSel), b.hover)); g.FillPath(&fb, &p);
        Pen pen(on ? kAcc : TH(choiceBorder), on ? 1.6f : 1.0f); g.DrawPath(&pen, &p);
        Icon(g, steam ? IC_STEAM : IC_WIFI, b.r.X + 28, b.r.Y + 32, 24, on ? kAcc : kInk, 1.8f);
        Text(g, steam ? L"STEAM" : L"IP / VPN", RectF(b.r.X + 54, b.r.Y + 11, 160, 22), 15, FontStyleBold, kInk, StringAlignmentNear);
        bool w7 = steam && IsWin7();   // (Windows 7 : Steam Legacy ne joint plus les serveurs de Valve -- le dire sur la tuile)
        if (steam && !w7) { Graphics &mg = MeasureG(); float sw = MeasureW(mg, L"STEAM", 15, FontStyleBold); Text(g, T(L"conseillé", L"recommended"), RectF(b.r.X + 58 + sw, b.r.Y + 13, 120, 20), 11.5f, FontStyleBold, kOk, StringAlignmentNear); }
        Text(g, w7 ? T(L"Windows 7 : prends IP / VPN", L"Windows 7: use IP / VPN") : steam ? T(L"Invitations, sans port ni pare-feu", L"Invites, no port or firewall") : T(L"Réseau local, Radmin, Hamachi…", L"LAN, Radmin, Hamachi…"),
             RectF(b.r.X + 54, b.r.Y + 33, b.r.Width - 60, 20), 12.5f, w7 ? FontStyleBold : FontStyleRegular, w7 ? kWarn : kGrey, StringAlignmentNear);
    }
    RectF row(net.X + 20, net.Y + 126, net.Width - 40, 44);
    if (!g_steamNet) {
        SmallLabel(g, T(L"ADRESSE DE L'HÔTE", L"HOST ADDRESS"), RectF(row.X, row.Y, 130, row.Height), kGrey);
        g_fields[1].r = RectF(row.X + 134, row.Y, row.Width - 134 - 150, row.Height);
        DrawField(g, 1, L"");
        SmallLabel(g, T(L"CODE", L"CODE"), RectF(row.X + row.Width - 142, row.Y, 40, row.Height), kGrey);   // (code de session d'un salon privé, 0.96.4)
        g_fields[2].r = RectF(row.X + row.Width - 100, row.Y, 100, row.Height);
        DrawField(g, 2, L"");
    } else if (IsWin7()) {   // Windows 7 : a la place de l'option de lancement, pourquoi Steam ne marchera pas (JD 26/10)
        g_fields[1].r = g_fields[2].r = RectF(0, 0, 0, 0);
        GraphicsPath p; RoundRect(p, row, 12);
        SolidBrush fb(WithA(kWarn, 0.12f)); g.FillPath(&fb, &p);
        Pen pen(WithA(kWarn, 0.6f), 1.0f); g.DrawPath(&pen, &p);
        Para(g, Win7SteamText(), RectF(row.X + 14, row.Y + 4, row.Width - 28, row.Height - 6), 12, kWarn);
    } else {   // option de lancement Steam : pour qu'une invitation ouvre MWCoop
        g_fields[1].r = g_fields[2].r = RectF(0, 0, 0, 0);
        bool hot = g_uiHot == UI_HOME_STEAMOPT;
        GraphicsPath p; RoundRect(p, row, 12);
        SolidBrush fb(WithA(kAcc, hot ? 0.18f : 0.10f)); g.FillPath(&fb, &p);
        Pen pen(WithA(kAcc, hot ? 0.9f : 0.5f), 1.0f); g.DrawPath(&pen, &p);
        Text(g, T(L"Option de lancement Steam", L"Steam launch option"), RectF(row.X + 14, row.Y + 3, row.Width - 120, 20), 13, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, T(L"Pour qu'une invitation acceptée ouvre MWCoop (et pas le jeu seul)", L"So that an accepted invite opens MWCoop (not the bare game)"), RectF(row.X + 14, row.Y + 22, row.Width - 120, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        Pill(g, RectF(row.X + row.Width - 86, row.Y + 10, 74, 24), kAcc, T(L"GUIDE", L"GUIDE"), kOnAcc, 11.5f);
        HotZone(row, UI_HOME_STEAMOPT);
    }
    // heberger / rejoindre (ou : retour au salon)
    float ty = net.Y + net.Height + 16, th = 196;
    if (lobby != LB_NONE) {
        RectF t(rx, ty, rw, th);
        bool hot = g_uiHot == UI_HOME_LOBBY;
        GraphicsPath p; RoundRect(p, t, 22);
        SolidBrush fb(kAcc); g.FillPath(&fb, &p);
        if (hot) { SolidBrush hb(Color(50, 255, 255, 255)); g.FillPath(&hb, &p); }
        Icon(g, IC_USERS, t.X + 44, t.Y + 44, 26, kOnAcc, 2.0f);
        Title(g, T(L"SALON EN COURS", L"LOBBY IN PROGRESS"), RectF(t.X + 24, t.Y + 74, t.Width - 48, 34), 26, kOnAcc);
        Text(g, T(L"Retourner au salon : joueurs, partie, lancement", L"Back to the lobby: players, game, start"), RectF(t.X + 24, t.Y + 110, t.Width - 48, 22), 14, FontStyleRegular, WithA(kOnAcc, 0.8f), StringAlignmentNear);
        Icon(g, IC_CHEVR, t.X + t.Width - 40, t.Y + t.Height - 34, 22, kOnAcc, 2.4f);
        HotZone(t, UI_HOME_LOBBY);
    } else {
        for (int id : { B_HOST, B_JOIN }) {
            Button &b = g_btn[id];
            bool host = id == B_HOST;
            b.r = RectF(rx + (host ? 0 : (rw - 16) / 2 + 16), ty, (rw - 16) / 2, th);
            RectF r = b.r;
            float a = b.enabled ? 1.0f : 0.4f;
            if (g_pressed == id && g_hot == id) r.Offset(0, 1);
            GraphicsPath p; RoundRect(p, r, 22);
            if (host) {
                SolidBrush fb(WithA(kAcc, a)); g.FillPath(&fb, &p);
                if (b.hover > 0.01f) { SolidBrush hb(Color((BYTE)(55 * b.hover), 255, 255, 255)); g.FillPath(&hb, &p); }
            } else {
                SolidBrush fb(Mix(TH(card), TH(cardSel), b.hover)); g.FillPath(&fb, &p);
                Pen pen(WithA(kAcc, 0.55f * a + 0.45f * b.hover), 1.4f); g.DrawPath(&pen, &p);
            }
            Color ink = host ? WithA(kOnAcc, a) : WithA(kInk, a), acc = host ? WithA(kOnAcc, a) : WithA(kAcc, a);
            RectF ib(r.X + 22, r.Y + 20, 44, 44);
            GraphicsPath ip; RoundRect(ip, ib, 14);
            SolidBrush ibb(host ? Color(26, 6, 19, 37) : TH(cardSel)); g.FillPath(&ibb, &ip);
            Icon(g, host ? IC_HOMEPLUS : IC_LOGIN, ib.X + 22, ib.Y + 22, 22, acc, 2.0f);
            const wchar_t *lab = host ? T(L"HÉBERGER", L"HOST") : g_lateSync ? (g_sync == SY_ASK ? T(L"ACCEPTER L'ENVOI", L"ACCEPT THE FILES") : T(L"MODS DE L'HÔTE…", L"HOST'S MODS…")) : g_joinFallback ? T(L"REJOINDRE EN JEU", L"JOIN IN GAME") : T(L"REJOINDRE", L"JOIN");
            Title(g, lab, RectF(r.X + 22, r.Y + 74, r.Width - 44, 34), 25, acc);
            std::wstring d;
            if (host) d = g_steamNet ? T(L"Ouvre un salon Steam pour tes amis ; chacun se met prêt, puis tu lances.", L"Opens a Steam lobby for your friends; everyone gets ready, then you start.")
                                     : T(L"Ouvre un salon : tes amis le rejoignent, chacun se met prêt, puis tu lances.", L"Opens a lobby: your friends join, everyone gets ready, then you start.");
            else if (g_lateSync) {   // invite en retard : reception, puis le jeu
                wchar_t lb[200];
                uint64_t tot = g_syncTotal, got = g_syncGot;
                swprintf_s(lb, T(L"L'hôte joue déjà : réception de ses mods (%d %%), puis tu entres en jeu. Clique pour annuler.", L"The host is already playing: receiving their mods (%d %%), then you join. Click to cancel."), tot ? (int)min<uint64_t>(99, got * 100 / tot) : 0);
                if (g_sync == SY_ASK)   // gros envoi : la taille et le temps avant d'accepter (JD, 25/10)
                    swprintf_s(lb, T(L"%s à recevoir, %s. Clique pour accepter, Échap pour annuler.", L"%s to receive, %s. Click to accept, Esc to cancel."), SizeText(tot).c_str(), SyncEta(tot).c_str());
                d = lb;
            }
            else if (g_joinFallback) d = T(L"L'hôte joue déjà : rejoins sa partie directement.", L"The host is already playing: join their game directly.");
            else if (g_steamNet && g_sInvite) d = g_sInviteName + T(L" t'invite : clique pour entrer dans son salon.", L" invites you: click to enter their lobby.");
            else d = g_steamNet ? T(L"Ton invitation Steam, sinon le salon MWCoop d'un ami.", L"Your Steam invite, or else a friend's MWCoop lobby.")
                                : T(L"Le salon de l'adresse saisie au-dessus.", L"The lobby at the address entered above.");
            Para(g, d, RectF(r.X + 22, r.Y + 112, r.Width - 44, 44), 13, host ? WithA(kOnAcc, 0.78f * a) : WithA(kGrey, a));
            wchar_t upto[48];   // (joueurs au plus : choix du salon, [Lanceur] SalonMax)
            swprintf_s(upto, T(L"Jusqu'à %d joueurs", L"Up to %d players"), max(2, min(kLobbyMaxAbs, (int)GetPrivateProfileIntW(L"Lanceur", L"SalonMax", 8, g_iniLauncher.c_str()))));
            Text(g, host ? std::wstring(upto) + L" \u00B7 " + SaveCurrentName() : std::wstring(T(L"Salon d'un ami", L"A friend's lobby")), RectF(r.X + 22, r.Y + r.Height - 40, r.Width - 80, 24), 12.5f, FontStyleBold, host ? WithA(kOnAcc, a) : WithA(kInk, 0.8f * a), StringAlignmentNear);
            Icon(g, IC_CHEVR, r.X + r.Width - 34, r.Y + r.Height - 28, 20, acc, 2.4f);
            if (!host && g_steamNet && g_sInvite) { SolidBrush dot(kOk); g.FillEllipse(&dot, r.X + r.Width - 34, r.Y + 22, 12.0f, 12.0f); }
        }
    }
    // jouer en solo
    {
        Button &b = g_btn[B_SOLO];
        b.r = RectF(rx, ty + th + 16, rw, 60);
        b.visible = lobby == LB_NONE && !g_goWait;
        if (b.visible) {
            float a = b.enabled ? 1.0f : 0.4f;
            GraphicsPath p; RoundRect(p, b.r, 18);
            SolidBrush fb(Mix(TH(card), TH(cardSel), b.hover)); g.FillPath(&fb, &p);
            Pen pen(TH(choiceBorder), 1.0f); g.DrawPath(&pen, &p);
            Icon(g, IC_PLAY, b.r.X + 30, b.r.Y + 30, 20, WithA(kInk, a), 2.0f);
            Text(g, T(L"JOUER EN SOLO", L"PLAY SOLO"), RectF(b.r.X + 52, b.r.Y, 150, b.r.Height), 14.5f, FontStyleBold, WithA(kInk, a), StringAlignmentNear);
            Text(g, T(L"Sans Coop", L"Without Coop"),
                 RectF(b.r.X + 196, b.r.Y, b.r.Width - 240, b.r.Height), 12.5f, FontStyleRegular, WithA(kGrey, a), StringAlignmentNear);
            Icon(g, IC_CHEVR, b.r.X + b.r.Width - 28, b.r.Y + 30, 18, WithA(kInk, a), 2.2f);
        }
    }
    // nouveautes
    RectF nc(rx, ty + th + 92, rw, g_pageR.Y + g_pageR.Height - (ty + th + 92));
    Glass(nc);
    std::vector<Note> notes;
    EnterCriticalSection(&g_cs);
    notes = g_notes;
    LeaveCriticalSection(&g_cs);
    Title(g, T(L"Nouveautés", L"What's new"), RectF(nc.X + 20, nc.Y + 14, 400, 24), 16, kInk);
    {
        bool hot = g_uiHot == UI_HOME_NOTES;
        Text(g, T(L"Tout voir", L"See all"), RectF(nc.X + nc.Width - 120, nc.Y + 14, 100, 24), 13, hot ? FontStyleBold | FontStyleUnderline : FontStyleBold, kAcc, StringAlignmentFar);
        HotZone(RectF(nc.X + nc.Width - 110, nc.Y + 10, 96, 30), UI_HOME_NOTES);
    }
    if (notes.empty()) Text(g, !g_notesDone ? T(L"Chargement…", L"Loading…") : T(L"Notes indisponibles (hors ligne).", L"Notes unavailable (offline)."), RectF(nc.X + 20, nc.Y + 50, nc.Width - 40, 24), 13, FontStyleRegular, kGrey, StringAlignmentNear);
    else {   // les 2 dernieres versions (maquette) : pastille (rose si plus recente que la mienne), premiere ligne, deuxieme en gris
        float y = nc.Y + 46, rowH = (nc.Height - 54) / 2;
        for (size_t i = 0; i < notes.size() && i < 2; i++) {
            const Note &n = notes[i];
            std::wstring ver = NoteShort(n);   // (« MWC 1.15.3 », « Lanceur 1.15.4 »)
            bool fresh = i == 0 && NoteCmp(n) > 0;
            float pw = PillW(ver, 10);
            Pill(g, RectF(nc.X + 20, y + 4, pw, 20), fresh ? kRed : TH(cardSel), ver, fresh ? Color(255, 255, 255, 255) : kInk, 10);
            std::vector<std::wstring> bl = NoteBullets(n, 2);
            float tx = nc.X + 20 + pw + 10, tw = nc.Width - 40 - pw - 10;
            if (!bl.empty()) Text(g, bl[0], RectF(tx, y + 2, tw, 22), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
            if (bl.size() > 1) Text(g, bl[1], RectF(tx, y + 24, tw, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
            y += rowH;
            if (i == 0 && notes.size() > 1) { Pen sep(TH(sep), 1); g.DrawLine(&sep, nc.X + 20, y - 4, nc.X + nc.Width - 20, y - 4); }
        }
    }
}

// Apercu du personnage (accueil) : 3D si l'apparence complete est exportee, sinon la vue de la tenue, sinon un fantome.
static void DrawHomeLook(Graphics &g, RectF stage)
{
    SkinsCheck();
    PersoCheck();
    GraphicsPath sp; RoundRect(sp, stage, 18);
    {
        LinearGradientBrush lg(RectF(stage.X, stage.Y - 1, stage.Width, stage.Height + 2), Color(g_dark ? 30 : 90, 255, 255, 255), Color(g_dark ? 6 : 30, 255, 255, 255), LinearGradientModeVertical);
        g.FillPath(&lg, &sp);
        GraphicsPath hp; hp.AddEllipse(stage.X + 20, stage.Y + stage.Height - 70, stage.Width - 40, 50.0f);
        PathGradientBrush hb(&hp);
        hb.SetCenterColor(WithA(kAcc, g_dark ? 0.30f : 0.25f));
        Color edge(0, 0, 0, 0); int one = 1; hb.SetSurroundColors(&edge, &one);
        g.FillPath(&hb, &hp);
        Pen pen(TH(choiceBorder), 1.0f); g.DrawPath(&pen, &sp);
    }
    RectF dst(stage.X + 2, stage.Y + 8, stage.Width - 4, stage.Height - 30);
    const Opt *ap = SkinOpt();
    if (g_perso.state == 1) {
        float yaw = floorf(g_skinYaw * 4) / 4;   // (64 vues par tour : ~3 rendus par seconde au lieu de 60)
        g_skinHomeR = dst;
        if (Bitmap *pb = PersoImage(dst, yaw * 6.2831853f / 16)) DrawPixels(g, pb, dst);
    } else if (ap) {
        std::string skin = ap->svals[OptGet(*ap)];
        if (Bitmap *strip = SkinStrip(skin)) {
            float fw = strip->GetWidth() / 16.0f, fh = (float)strip->GetHeight();
            int fr = (int)floorf(g_skinYaw + 0.5f) & 15;
            float h = dst.Height, w = h * fw / fh;
            g.DrawImage(strip, RectF(dst.X + (dst.Width - w) / 2, dst.Y, w, h), fr * fw, 0, fw, fh, UnitPixel);
        } else {
            SolidBrush b(WithA(kGrey, 0.22f));
            float cx = stage.X + stage.Width / 2, top = stage.Y + 90;
            g.FillEllipse(&b, cx - 30, top, 60.0f, 64.0f);
            GraphicsPath bp; RoundRect(bp, RectF(cx - 56, top + 76, 112, 150), 30); g.FillPath(&b, &bp);
            Para(g, T(L"L'aperçu 3D apparaît après une première partie (~30 s en jeu) avec cette version.", L"The 3D preview shows up after a first game (~30 s in game) with this version."),
                 RectF(stage.X + 24, top + 250, stage.Width - 48, 60), 12.5f, kGrey, StringAlignmentNear);
        }
    }
    if (PersoStale()) {   // apercu d'une ancienne version : le dire a la place des etiquettes
        RectF r(stage.X + 14, stage.Y + stage.Height - 46, stage.Width - 28, 34);
        GraphicsPath p; RoundRect(p, r, 17);
        SolidBrush b(g_dark ? Color(215, 40, 30, 8) : Color(235, 255, 244, 220)); g.FillPath(&b, &p);
        Pen pen(WithA(kWarn, 0.7f), 1); g.DrawPath(&pen, &p);
        Text(g, T(L"Aperçu à refaire : lance une partie (~30 s)", L"Preview to redo: start a game (~30 s)"), r, 12, FontStyleBold, kWarn);
        return;
    }
    // etiquettes : haut, corpulence, chapeau
    std::vector<std::wstring> chips;
    if (g_perso.state == 1) { chips.push_back(PersoLabel(PF_SHIRT, PersoGet(PF_SHIRT))); chips.push_back(PersoLabel(PF_BODY, PersoGet(PF_BODY))); if (!PersoGet(PF_HAT).empty()) chips.push_back(PersoLabel(PF_HAT, PersoGet(PF_HAT))); }
    else if (ap) chips.push_back(TLab(ap->labFr, ap->labEn, OptGet(*ap)));
    float tw = 0;
    std::vector<float> ws;
    for (auto &c : chips) { float w = min(PillW(c, 12), 120.0f); ws.push_back(w); tw += w + 6; }
    float x = stage.X + (stage.Width - tw + 6) / 2;
    for (size_t i = 0; i < chips.size(); i++) {
        RectF r(x, stage.Y + stage.Height - 40, ws[i], 28);
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush b(g_dark ? Color(160, 6, 12, 30) : Color(200, 255, 255, 255)); g.FillPath(&b, &p);
        Text(g, chips[i], RectF(r.X + 6, r.Y, r.Width - 12, r.Height), 12, FontStyleBold, kInk);
        x += ws[i] + 6;
    }
}

// Lignes "a puces" d'une note (sans les images), pour l'accueil.
static std::vector<std::wstring> NoteBullets(const Note &n, int maxN)
{
    std::vector<std::wstring> out;
    const std::wstring &body = NoteBody(n);
    for (size_t at = 0; at < body.size() && (int)out.size() < maxN;) {
        size_t e = body.find(L'\n', at);
        std::wstring line = body.substr(at, e == std::wstring::npos ? std::wstring::npos : e - at);
        at = e == std::wstring::npos ? body.size() : e + 1;
        if (line.empty() || line[0] == L'\x01') continue;
        size_t b = line.find_first_not_of(L"•-* ");
        if (b == std::wstring::npos) continue;
        out.push_back(line.substr(b));
    }
    return out;
}

// ---- page NOUVEAUTES : versions a gauche, detail a droite (texte, images)
static int g_noteSel;
static float g_noteListScroll;
// (refonte L4 : plus const -- recalcules a chaque image depuis g_pageR, la zone de page fluide ; ancienne interface : valeurs d'avant)
static RectF kNotesList(264, 88, 280, 656), kNotesBody(560, 88, 704, 656);
static void NotesLayout() { float x = g_pageR.X, w = g_pageR.Width, lw = 280, gap = 16; kNotesList = RectF(x, 88, lw, 656); kNotesBody = RectF(x + lw + gap, 88, w - lw - gap, 656); }
static RectF NotesArea() { return RectF(kNotesBody.X + 28, kNotesBody.Y + 92, kNotesBody.Width - 46, kNotesBody.Height - 108); }
static void DrawNotesPage(Graphics &g)
{
    NotesLayout();   // (refonte L4 : cartes a la largeur de la colonne de page, g_pageR)
    Glass(kNotesList);
    Glass(kNotesBody);
    std::vector<Note> notes;
    EnterCriticalSection(&g_cs);
    notes = g_notes;
    LeaveCriticalSection(&g_cs);
    if (notes.empty()) {
        const wchar_t *msg = !g_notesDone ? T(L"Chargement des notes de version…", L"Loading release notes…")
                           : g_relState == REL_NONE ? T(L"Aucune version publiée pour l'instant.", L"No release published yet.")
                           : T(L"Notes de version indisponibles (hors ligne).", L"Release notes unavailable (offline).");
        Text(g, msg, kNotesBody, 14, FontStyleRegular, kGrey);
        return;
    }
    if (g_noteSel < 0 || g_noteSel >= (int)notes.size()) g_noteSel = 0;
    // liste
    RectF lr(kNotesList.X + 10, kNotesList.Y + 10, kNotesList.Width - 20, kNotesList.Height - 20);
    const float rh = 66;
    g_noteListScroll = min(max(g_noteListScroll, 0.0f), max(0.0f, notes.size() * rh - lr.Height));
    GraphicsState st = g.Save();
    g.SetClip(lr, CombineModeIntersect);
    for (int i = 0; i < (int)notes.size(); i++) {
        RectF r(lr.X, lr.Y + i * rh - g_noteListScroll, lr.Width, rh - 4);
        if (r.Y + r.Height < lr.Y || r.Y > lr.Y + lr.Height) continue;
        bool on = i == g_noteSel, hot = g_uiHot == UI_NOTE + i;
        if (on || hot) { GraphicsPath p; RoundRect(p, r, 14); SolidBrush b(on ? TH(cardSel) : TH(card)); g.FillPath(&b, &p); }
        int cmp = NoteCmp(notes[i]);
        SolidBrush dot(cmp > 0 ? kAcc : cmp == 0 ? kOk : WithA(kGrey, 0.6f));
        g.FillEllipse(&dot, r.X + 14, r.Y + r.Height / 2 - 5, 10.0f, 10.0f);
        // (nom complet sur toute la largeur -- « My Winter Car 1.15.3 », « Lanceur 1.15.4 » : JD, 25/10, les numeros seuls se
        // confondaient d'un flux a l'autre --, dessous la date et l'heure, et l'etat a droite)
        Text(g, FitText(NoteName(notes[i]), 14.5f, FontStyleBold, r.Width - 46), RectF(r.X + 36, r.Y + 10, r.Width - 46, 24), 14.5f, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, notes[i].date, RectF(r.X + 36, r.Y + 34, r.Width - 46 - 84, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        if (cmp >= 0 && (notes[i].stream == STR_LANCEUR || !g_localVer.empty())) {
            const wchar_t *lab = cmp == 0 ? T(L"INSTALLÉE", L"INSTALLED") : T(L"NOUVELLE", L"NEW");
            float w = PillW(lab, 9.5f);
            Pill(g, RectF(r.X + r.Width - w - 10, r.Y + 34, w, 20), cmp == 0 ? WithA(kOk, 0.2f) : kAcc, lab, cmp == 0 ? kOk : kOnAcc, 9.5f);
        }
        HotZone(r, UI_NOTE + i);
    }
    g.Restore(st);
    // detail
    const Note &n = notes[g_noteSel];
    RectF b = kNotesBody;
    Title(g, NoteName(n), RectF(b.X + 28, b.Y + 22, 460, 38), 28, kInk);
    Text(g, n.date, RectF(b.X + 28, b.Y + 58, 300, 20), 13, FontStyleRegular, kGrey, StringAlignmentNear);
    int cmp = NoteCmp(n);
    if (cmp > 0 && n.stream == STR_LANCEUR && !LancAvail().empty()) UiButton(g, RectF(b.X + b.Width - 200, b.Y + 26, 172, 44), T(L"Mettre à jour", L"Update"), UI_LANCEUR_UPDATE, true, IC_DOWNLOAD, !g_busy);
    else if (cmp > 0 && n.stream != STR_LANCEUR && n.stream != STR_OUTILS && !UpdAvail().empty()) UiButton(g, RectF(b.X + b.Width - 200, b.Y + 26, 172, 44), T(L"Mettre à jour", L"Update"), UI_NOTE_UPDATE, true, IC_DOWNLOAD);
    else if (cmp == 0) { const wchar_t *lab = T(L"INSTALLÉE", L"INSTALLED"); float w = PillW(lab, 11); Pill(g, RectF(b.X + b.Width - 28 - w, b.Y + 36, w, 24), WithA(kOk, 0.2f), lab, kOk); }
    Pen sep(TH(sep), 1); g.DrawLine(&sep, b.X + 28, b.Y + 84, b.X + b.Width - 28, b.Y + 84);
    // corps : puces, paragraphes, images (defilement)
    RectF area = NotesArea();
    const std::wstring &body = NoteBody(n);
    UiFont fb(L"Segoe UI", 14.5f, FontStyleRegular);
    StringFormat sf;
    SolidBrush ink(kInk);
    float sc = g_scroll[TAB_NOTES], y = area.Y + 10 - sc, w = area.Width - 14;
    st = g.Save();
    g.SetClip(area, CombineModeIntersect);
    g_noteImgHits.clear();
    for (size_t at = 0; at < body.size();) {
        size_t e = body.find(L'\n', at);
        std::wstring line = body.substr(at, e == std::wstring::npos ? std::wstring::npos : e - at);
        at = e == std::wstring::npos ? body.size() : e + 1;
        if (!line.empty() && line[0] == L'\x01') {   // image
            Bitmap *bmp = NULL;
            int s = NoteImgGet(line.substr(1), &bmp);
            if (s == NI_FAIL) continue;
            float ih = 140;
            RectF ir(area.X + 4, y + 6, w - 8, ih);
            if (s == NI_OK) { float iw = (float)bmp->GetWidth(), hh = (float)bmp->GetHeight(), k = min(1.0f, min((w - 8) / iw, 340 / hh)); ir = RectF(area.X + 4 + (w - 8 - iw * k) / 2, y + 6, iw * k, hh * k); }
            if (ir.Y + ir.Height >= area.Y && ir.Y <= area.Y + area.Height) {
                GraphicsPath ip; RoundRect(ip, ir, 12);
                if (s == NI_OK) {
                    Region old; g.GetClip(&old);
                    g.SetClip(&ip, CombineModeIntersect);
                    g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
                    g.DrawImage(bmp, ir);
                    g.SetClip(&old);
                    Pen ib(TH(choiceBorder), 1); g.DrawPath(&ib, &ip);
                    g_noteImgHits.push_back(std::make_pair(ir, line.substr(1)));
                } else { SolidBrush pb(TH(card)); g.FillPath(&pb, &ip); Text(g, T(L"Chargement de l'image…", L"Loading image…"), ir, 12, FontStyleRegular, kGrey); }
            }
            y += ir.Height + 16;
            continue;
        }
        size_t bpos = line.find_first_not_of(L" ");
        if (bpos == std::wstring::npos) { y += 6; continue; }
        bool bullet = line[bpos] == L'•' || line[bpos] == L'-' || line[bpos] == L'*';
        std::wstring t = bullet ? line.substr(line.find_first_not_of(L"•-* ")) : line.substr(bpos);
        float x = bullet ? area.X + 22 : area.X + 4;
        RectF box;
        MeasureStr(g, t.c_str(), -1, &fb, RectF(0, 0, w - (x - area.X), 100000), &sf, &box);
        if (y + box.Height >= area.Y && y <= area.Y + area.Height) {
            if (bullet) { SolidBrush dot(kAcc); g.FillEllipse(&dot, area.X + 6, y + 8, 7.0f, 7.0f); }
            DrawStr(g, t.c_str(), -1, &fb, RectF(x, y, w - (x - area.X), box.Height + 2), &sf, &ink);
        }
        y += box.Height + 10;
    }
    g.Restore(st);
    g_notesH = y + sc - area.Y;
    float ms = max(0.0f, g_notesH - area.Height);
    if (ms > 0) {
        float bh = area.Height * area.Height / (area.Height + ms), by = area.Y + (area.Height - bh) * min(sc, ms) / ms;
        GraphicsPath sp; RoundRect(sp, RectF(area.X + area.Width - 5, by, 4, bh), 2);
        SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
    }
}

// ---- page JOURNAUX
static std::wstring LogDay(const FILETIME &ft, std::wstring *hm)
{
    FILETIME lt; SYSTEMTIME st;
    FileTimeToLocalFileTime(&ft, &lt);
    FileTimeToSystemTime(&lt, &st);
    wchar_t b[40], h[16];
    if (g_fr) swprintf_s(b, L"%02d/%02d/%04d", st.wDay, st.wMonth, st.wYear); else swprintf_s(b, L"%04d-%02d-%02d", st.wYear, st.wMonth, st.wDay);
    swprintf_s(h, g_fr ? L"%02dh%02d" : L"%02d:%02d", st.wHour, st.wMinute);
    if (hm) *hm = h;
    return b;
}

// En-tete d'un groupe (une partie) : chevron, date et heures, version / mode / profil / taille, pastilles, boutons
// dossier, zip, corbeille (rouge quand la suppression attend son second clic).
static void DrawLogHead(Graphics &g, const LogGroup &gr, RectF r, int hotPart)
{
    bool open = LogGroupOpen(gr), cur = gr.key == L"*";
    GraphicsPath rp; RoundRect(rp, r, 14);
    SolidBrush rb(hotPart == 0 ? TH(cardSel) : cur ? WithA(kAcc, 0.10f) : TH(card)); g.FillPath(&rb, &rp);
    Pen rpen(hotPart >= 0 ? WithA(kAcc, 0.7f) : cur ? WithA(kAcc, 0.45f) : TH(choiceBorder), 1.0f); g.DrawPath(&rpen, &rp);
    Icon(g, open ? IC_CHEVD : IC_CHEVR, r.X + 24, r.Y + r.Height / 2, 18, hotPart == 0 ? kAcc : kInk, 2.3f);
    std::wstring endHM, day = LogDay(gr.end, &endHM), title;
    if (cur) title = std::wstring(gr.live ? T(L"Partie en cours", L"Current session") : T(L"Dernière partie", L"Last session")) + L" \u00B7 " + day;
    else title = std::wstring(T(L"Partie du ", L"Session of ")) + day;
    if (!gr.startHM.empty()) {
        std::wstring s = gr.startHM;
        if (!g_fr && s.size() == 5) s[2] = L':';
        title += L" \u00B7 " + s + (gr.live ? L"" : L" \u2192 " + endHM);
    } else if (!gr.live) title += L" \u00B7 " + endHM;
    std::wstring sub;
    auto part = [&](const std::wstring &s) { if (s.empty()) return; if (!sub.empty()) sub += L" \u00B7 "; sub += s; };
    if (!gr.version.empty()) part(L"MWCoop " + gr.version);
    part(gr.mode);
    if (!gr.profile.empty()) part((gr.profile[0] == L'(' || gr.profile.find(L' ') != std::wstring::npos ? L"" : std::wstring(T(L"profil ", L"profile "))) + gr.profile);
    wchar_t nb[64];
    if (gr.bytes < 1048576) swprintf_s(nb, T(L"%d journaux, %.0f Ko", L"%d logs, %.0f KB"), gr.files, gr.bytes / 1024.0);
    else swprintf_s(nb, T(L"%d journaux, %.1f Mo", L"%d logs, %.1f MB"), gr.files, gr.bytes / 1048576.0);
    part(nb);
    float textW = r.Width - 52 - 3 * 46 - 24;
    // Pastilles a droite du texte, avant les boutons.
    float px = LogHeadBtn(r, 1).X - 12;
    auto pill = [&](const wchar_t *lab) {
        float lw = PillW(lab, 10.5f);
        px -= lw;
        Pill(g, RectF(px, r.Y + (r.Height - 22) / 2, lw, 22), WithA(kRed, 0.18f), lab, kRed, 10.5f);
        px -= 8;
    };
    if (gr.crash) pill(T(L"ARRÊT BRUTAL", L"ABRUPT STOP"));
    if (gr.errors > 0) {
        wchar_t lab[48];
        swprintf_s(lab, gr.errors == 1 ? T(L"1 ERREUR", L"1 ERROR") : T(L"%d ERREURS", L"%d ERRORS"), gr.errors);
        pill(lab);
    }
    textW = min(textW, px - (r.X + 48) - 6);
    Text(g, title, RectF(r.X + 48, r.Y + 9, textW, 22), 14, FontStyleBold, kInk, StringAlignmentNear);
    Text(g, sub, RectF(r.X + 48, r.Y + 31, textW, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    bool armed = g_logDelKey == gr.key && GetTickCount() - g_logDelT < 4000;
    for (int p = 1; p <= 3; p++) {
        RectF b = LogHeadBtn(r, p);
        bool h = hotPart == p, red = p == 3 && armed;
        GraphicsPath bp; RoundRect(bp, b, 10);
        SolidBrush bb(red ? WithA(kRed, 0.85f) : h ? TH(cardSel) : TH(card)); g.FillPath(&bb, &bp);
        if (h || red) { Pen bpen(red ? kRed : WithA(kAcc, 0.7f), 1.0f); g.DrawPath(&bpen, &bp); }
        int ic = p == 1 ? IC_FOLDER : p == 2 ? IC_ZIP : IC_TRASH;
        Color ink = red ? Color(255, 255, 255, 255) : h ? (p == 3 ? kRed : kAcc) : kInk;
        Icon(g, ic, b.X + b.Width / 2, b.Y + b.Height / 2, 17, ink, 1.9f);
    }
}

static void DrawLogsPage(Graphics &g)
{
    RectF c = g_pageR;
    Glass(c);
    Title(g, T(L"Les journaux du jeu et du mod", L"The game's and the mod's logs"), RectF(c.X + 24, c.Y + 20, 500, 26), 17, kInk);
    g_btn[B_LOGZIP].r = RectF(c.X + c.Width - 24 - 232, c.Y + 22, 232, 44);
    g_btn[B_LOGDIR].r = RectF(g_btn[B_LOGZIP].r.X - 12 - 196, c.Y + 22, 196, 44);
    bool archived = false;
    for (auto &gr : g_logGroups) if (gr.key != L"*") archived = true;
    float subW = g_btn[B_LOGDIR].r.X - (c.X + 24) - 16;
    if (archived) {
        bool armed = g_logDelKey == L"**" && GetTickCount() - g_logDelT < 4000;
        RectF cr(g_btn[B_LOGDIR].r.X - 12 - 176, c.Y + 22, 176, 44);
        UiButton(g, cr, armed ? T(L"Confirmer ?", L"Confirm?") : T(L"Tout supprimer", L"Delete all"), UI_LOG_CLEAR, false, IC_TRASH);
        subW = cr.X - (c.X + 24) - 16;
    }
    Text(g, T(L"Chaque partie garde ses journaux (les 30 dernières).", L"Every session keeps its own logs (the last 30)."),
         RectF(c.X + 24, c.Y + 46, subW, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    for (int id : { B_LOGDIR, B_LOGZIP }) {
        Button &b = g_btn[id];
        bool zip = id == B_LOGZIP;
        GraphicsPath p; RoundRect(p, b.r, 14);
        if (zip) { SolidBrush fb(kAcc); g.FillPath(&fb, &p); if (b.hover > 0.01f) { SolidBrush hb(Color((BYTE)(55 * b.hover), 255, 255, 255)); g.FillPath(&hb, &p); } }
        else { SolidBrush fb(Mix(TH(btn2), TH(btn2Hot), b.hover)); g.FillPath(&fb, &p); Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p); }
        Color ink = zip ? kOnAcc : kInk;
        Icon(g, zip ? IC_ZIP : IC_FOLDER, b.r.X + 26, b.r.Y + 22, 17, ink, 2.1f);
        Text(g, zip ? T(L"Créer un zip à envoyer", L"Create a zip to send") : T(L"Ouvrir le dossier", L"Open folder"), RectF(b.r.X + 42, b.r.Y, b.r.Width - 50, b.r.Height), 13, FontStyleBold, ink, StringAlignmentNear);
    }
    float y = c.Y + 84;
    {   // mode debogage (kLogsDebugRow : c.X + 24, c.Y + 84)
        RectF row = kLogsDebugRow;
        GraphicsPath p; RoundRect(p, row, 14);
        SolidBrush fb(g_debug ? WithA(kAcc, 0.12f) : TH(card)); g.FillPath(&fb, &p);
        Pen pen(g_debug ? WithA(kAcc, 0.5f) : TH(choiceBorder), 1); g.DrawPath(&pen, &p);
        Text(g, T(L"Mode débogage", L"Debug mode"), RectF(row.X + 18, row.Y + 4, 220, 22), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, T(L"Journal détaillé du lanceur : chaque étape, Steam, fichiers, DLL chargées dans le jeu, antivirus. À activer si MWCoop ne se charge pas, puis zip.",
                  L"Detailed launcher log: every step, Steam, files, DLLs loaded in the game, antivirus. Turn on if MWCoop doesn't load, then zip."),
             RectF(row.X + 18, row.Y + 24, row.Width - 100, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        UiToggle(g, RectF(row.X + row.Width - 66, row.Y + 10, 50, 28), g_debug, g_uiHot == UI_LOG_DEBUG);
        HotZone(row, UI_LOG_DEBUG);
        y += 58;
    }
    if (g_crashT) {
        RectF a(c.X + 24, y, c.Width - 48, 56);
        GraphicsPath p; RoundRect(p, a, 14);
        SolidBrush fb(WithA(kRed, 0.14f)); g.FillPath(&fb, &p);
        Pen pen(WithA(kRed, 0.5f), 1); g.DrawPath(&pen, &p);
        RectF d(a.X + 16, a.Y + 16, 24, 24);
        SolidBrush rb(kRed); g.FillEllipse(&rb, d);
        Text(g, L"!", d, 14, FontStyleBold, Color(255, 255, 255, 255));
        Text(g, T(L"La dernière partie s'est arrêtée brutalement.", L"The last game stopped abruptly."), RectF(a.X + 52, a.Y + 7, a.Width - 70, 22), 14, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, T(L"Crée un zip et envoie-le (GitHub, Discord) pour qu'on regarde.", L"Create a zip and send it (GitHub, Discord) so we can take a look."), RectF(a.X + 52, a.Y + 29, a.Width - 70, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        y += 70;
    }
    kLogsR = RectF(c.X + 24, y, c.Width - 48, c.Y + c.Height - 18 - y);
    if (g_logRows.empty())
        // (le journal du lanceur, mode debogage compris, est quand meme dans le zip : un joueur dont MWCoop ne se chargeait
        // pas croyait n'avoir rien a envoyer, 08/10)
        Para(g, T(L"Aucun journal du jeu : MWCoop ne s'est encore jamais chargé en jeu. Le journal du lanceur (et du mode débogage) est quand même dans le zip : crée-le et envoie-le.",
                  L"No game logs: MWCoop hasn't loaded in game yet. The launcher's log (and debug mode's) is still in the zip: create it and send it."),
             RectF(kLogsR.X + 100, kLogsR.Y + 60, kLogsR.Width - 200, 60), 14, kGrey, StringAlignmentCenter);
    float sc = g_scroll[TAB_LOGS];
    GraphicsState st = g.Save();
    g.SetClip(kLogsR, CombineModeIntersect);
    for (int i = 0; i < (int)g_logRows.size(); i++) {
        RectF r = LogRowRect(i);
        if (r.Y + r.Height < kLogsR.Y || r.Y > kLogsR.Y + kLogsR.Height) continue;
        bool hot = i == g_logRowHot;
        if (g_logRows[i].entry < 0) { DrawLogHead(g, g_logGroups[g_logRows[i].group], r, hot ? g_logPart : -1); continue; }
        const LogEntry &e = g_logList[g_logRows[i].entry];
        GraphicsPath rp; RoundRect(rp, r, 14);
        SolidBrush rb(hot && g_logPart == 0 ? TH(cardSel) : TH(card)); g.FillPath(&rb, &rp);
        Pen rpen(hot ? WithA(kAcc, 0.7f) : TH(choiceBorder), 1.0f); g.DrawPath(&rpen, &rp);
        Icon(g, IC_FILE, r.X + 26, r.Y + r.Height / 2, 20, e.errors > 0 ? kRed : kAcc, 1.9f);
        Text(g, LogTitle(e), RectF(r.X + 52, r.Y + 8, r.Width - 260, 22), 14, FontStyleBold, kInk, StringAlignmentNear);
        std::wstring name = e.path.substr(e.path.find_last_of(L'\\') + 1);
        wchar_t info[200], size[32];
        if (e.bytes < 1048576) swprintf_s(size, L"%.0f %s", e.bytes / 1024.0, T(L"Ko", L"KB"));
        else swprintf_s(size, L"%.1f %s", e.bytes / 1048576.0, T(L"Mo", L"MB"));
        swprintf_s(info, L"%s · %s · %s", name.c_str(), LogDate(e.mt).c_str(), size);
        Text(g, info, RectF(r.X + 52, r.Y + 30, r.Width - 260, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        if (e.errors > 0) {
            wchar_t lab[48];
            swprintf_s(lab, e.errors == 1 ? T(L"1 ERREUR", L"1 ERROR") : T(L"%d ERREURS", L"%d ERRORS"), e.errors);
            float lw = PillW(lab, 10.5f);
            Pill(g, RectF(r.X + r.Width - 64 - lw, r.Y + (r.Height - 22) / 2, lw, 22), WithA(kRed, 0.18f), lab, kRed, 10.5f);
        }
        RectF f = LogIconRect(r);
        bool fh = hot && g_logPart == 1;
        GraphicsPath fp; RoundRect(fp, f, 10);
        SolidBrush fb(fh ? TH(cardSel) : TH(card)); g.FillPath(&fb, &fp);
        Icon(g, IC_FOLDER, f.X + f.Width / 2, f.Y + f.Height / 2, 17, fh ? kAcc : kInk, 1.9f);
    }
    g.Restore(st);
    float ms = LogsMaxScroll();
    if (ms > 0) {
        float h = kLogsR.Height * kLogsR.Height / (kLogsR.Height + ms), yy = kLogsR.Y + (kLogsR.Height - h) * sc / ms;
        GraphicsPath sp; RoundRect(sp, RectF(kLogsR.X + kLogsR.Width - 4, yy, 4, h), 2);
        SolidBrush sb(WithA(kAcc, 0.5f)); g.FillPath(&sb, &sp);
    }
}

// ---- credits (demande de JD, 08/10 : ceux qui donnent quelque chose pour ameliorer MWCoop). Onglets (JD, 13/10) :
// modeles 3D, images (musee de la Corse : la liste complete, generee dans credits-images.inc), remerciements, et (JD,
// 18/10) Supporters : ceux qui ont fait un don, bouton qui brille comme l'etoile RGB. Le plus recent en haut de chaque
// onglet ; la page defile a la molette.
enum { CRED_3D, CRED_IMG, CRED_THANKS, CRED_SUPPORT };
struct Credit { int cat; const wchar_t *name, *roleFr, *roleEn, *fr, *en, *date; int img; };   // img : image integree (launcher.rc), 0 : initiale
static Bitmap *LoadPngRes(int id);
static const Credit kCredits[] = {
    { CRED_3D, L"osmosikum", L"Modèle 3D", L"3D model",
      L"Le Cessna 172 de l'aérodrome du mod Voyage (agence de voyage, vols vers les circuits convertis), d'après le modèle « Plane » de osmosikum sur Sketchfab : sketchfab.com/osmosikum, licence CC BY 4.0. Textures réduites pour le jeu.",
      L"The Cessna 172 at the airfield of the Voyage mod (travel agency, flights to converted tracks), from the Plane model by osmosikum on Sketchfab: sketchfab.com/osmosikum, CC BY 4.0 license. Textures reduced for the game.", L"23/10/2026", 0 },
    { CRED_3D, L"matthew0451", L"Modèle 3D", L"3D model",
      L"La VAZ-2108 des joueurs (F10), d'après le modèle « VAZ-2108 Rally » de matthew0451 sur Sketchfab : sketchfab.com/matthew0451, licence CC BY-NC 4.0. Allégée, livrée retirée (une couleur et un numéro par joueur).",
      L"The players' VAZ-2108 (F10), from the \"VAZ-2108 Rally\" model by matthew0451 on Sketchfab: sketchfab.com/matthew0451, CC BY-NC 4.0 license. Lightened, livery removed (one colour and number per player).", L"18/10/2026", 0 },
    { CRED_3D, L"Scan the World", L"Buste de Pascal Paoli", L"Bust of Pasquale Paoli",
      L"Le buste de Paoli du musée de la Corse, d'après celui du Palazzu Naziunale de Corte, numérisé par JF Andreani pour Scan the World (MyMiniFactory), licence CC BY-NC-SA. Simplifié pour le jeu.",
      L"The Paoli bust in the Museum of Corsica, after the one in the Palazzu Naziunale in Corte, scanned by JF Andreani for Scan the World (MyMiniFactory), CC BY-NC-SA license. Simplified for the game.", L"13/10/2026", 0 },
    { CRED_3D, L"Lincoln 3D Scans", L"Buste de Napoléon", L"Bust of Napoleon",
      L"Le buste de Napoléon du musée de la Corse, d'après Antoine-Denis Chaudet, numérisé par Oliver Laric (The Usher Gallery, Lincoln), sans restriction de droits. Simplifié pour le jeu.",
      L"The Napoleon bust in the Museum of Corsica, after Antoine-Denis Chaudet, scanned by Oliver Laric (The Usher Gallery, Lincoln), free of copyright restriction. Simplified for the game.", L"13/10/2026", 0 },
    { CRED_3D, L"GalaninaKira", L"Modèle 3D", L"3D model",
      L"Le drone de la caméra drone (mode photo), par Kira sur Sketchfab : sketchfab.com/GalaninaKira, modèle « Drone », licence CC BY 4.0. Simplifié pour le jeu.",
      L"The drone of the drone camera (photo mode), by Kira on Sketchfab: sketchfab.com/GalaninaKira, model \"Drone\", CC BY 4.0 license. Simplified for the game.", L"11/10/2026", 0 },
    { CRED_3D, L"WillBarrow", L"Modèles 3D", L"3D models",
      L"Les modèles du sac de couchage (plié et déplié), sur Sketchfab : sketchfab.com/WillBarrow. Un sac par joueur, dans la chambre du départ.",
      L"The sleeping bag models (rolled and unrolled), on Sketchfab: sketchfab.com/WillBarrow. One bag per player, in the starting bedroom.", L"10/10/2026", 0 },
    { CRED_SUPPORT, L"moonmax11", L"Supporter", L"Supporter",
      L"Pour avoir fait une donation.", L"For making a donation.", L"18/10/2026", 0 },
    { CRED_THANKS, L"Menos", L"Modèle 3D", L"3D model",
      L"Le modèle 3D des ceintures de sécurité arrière de la Sorbet et de la Machtwagen (My Winter Car).",
      L"The 3D model of the rear seat belts of the Sorbet and the Machtwagen (My Winter Car).", L"25/10/2026", 0 },
    { CRED_THANKS, L"Dom", L"Tenues", L"Outfits",
      L"Un pack de 155 tenues pour ton personnage : 84 hauts, 27 pantalons et 44 visages. Dans l'onglet Tenue, à la suite de celles du jeu (« Tenue Dom 12 »…).",
      L"A pack of 155 outfits for your character: 84 tops, 27 pants and 44 faces. In the Outfit tab, after the game's own (\"Outfit Dom 12\"…).", L"08/10/2026", 6 },
};
#include "credits-images.inc"
static int g_credTab = CRED_3D;
static float g_credContentH;   // hauteur du contenu de l'onglet (pour la molette)
static RectF CreditsArea() { return RectF(g_pageR.X + 24, g_pageR.Y + 168, g_pageR.Width - 48, g_pageR.Height - 168 - 64); }   // (refonte L6 : largeur de la colonne fluide)
static float CreditsMaxScroll() { return max(0.0f, g_credContentH - CreditsArea().Height); }

static void CreditCard(Graphics &g, const Credit &k, float y, float x, float w)
{
    RectF row(x, y, w, 104);
    GraphicsPath rp; RoundRect(rp, row, 16);
    SolidBrush rb(TH(card)); g.FillPath(&rb, &rp);
    Pen rpen(TH(choiceBorder), 1); g.DrawPath(&rpen, &rp);
    RectF av(row.X + 20, row.Y + 22, 60, 60);
    static std::map<int, Bitmap *> imgs;   // (images des contributeurs : chargees une fois)
    Bitmap *im = NULL;
    if (k.img) { auto it = imgs.find(k.img); if (it == imgs.end()) im = imgs[k.img] = LoadPngRes(k.img); else im = it->second; }
    if (im) {   // photo ronde, cadrage carre au centre, anneau aux couleurs de l'etoile
        GraphicsPath cp; cp.AddEllipse(av);
        GraphicsState gs = g.Save();
        g.SetClip(&cp, CombineModeIntersect);
        g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        float iw = (float)im->GetWidth(), ih = (float)im->GetHeight(), side = min(iw, ih);
        g.DrawImage(im, av, (iw - side) / 2, (ih - side) / 2, side, side, UnitPixel);
        g.Restore(gs);
        Pen ring(Hue(g_time * 70.0f), 2.4f); g.DrawEllipse(&ring, av); SoftLive(av);
    } else {
        SolidBrush ab(WithA(kAcc, 0.85f)); g.FillEllipse(&ab, av);
        Text(g, std::wstring(1, k.name[0]), av, 26, FontStyleBold, Color(255, 255, 255, 255));
    }
    Text(g, k.name, RectF(row.X + 100, row.Y + 16, 420, 28), 19, FontStyleBold, kInk, StringAlignmentNear);
    Text(g, T(k.roleFr, k.roleEn), RectF(row.X + 100, row.Y + 44, 420, 20), 13, FontStyleBold, kAcc, StringAlignmentNear);
    Text(g, k.date, RectF(row.X + row.Width - 220, row.Y + 16, 200, 28), 12.5f, FontStyleRegular, kGrey, StringAlignmentFar);
    Para(g, T(k.fr, k.en), RectF(row.X + 100, row.Y + 64, row.Width - 124, 38), 12, kGrey);
}

static void DrawCreditsPage(Graphics &g)
{
    RectF c(g_pageR.X, g_pageR.Y, g_pageR.Width, g_pageR.Height);
    Glass(c);
    Title(g, T(L"Merci !", L"Thank you!"), RectF(c.X + 28, c.Y + 22, 400, 30), 22, kInk);
    Para(g, T(L"MWCoop grandit aussi grâce à vous et aux créateurs qui partagent leur travail. Chaque personne qui donne quelque chose au mod est créditée ici.",
              L"MWCoop also grows thanks to you and to the creators who share their work. Everyone who gives something to the mod is credited here."),
         RectF(c.X + 28, c.Y + 58, c.Width - 56, 40), 13, kGrey);
    RectF seg(c.X + 24, c.Y + 106, 700, 40);
    UiSeg(g, seg, { T(L"Modèles 3D", L"3D Models"), T(L"Images", L"Images"), T(L"Remerciements", L"Special Thanks"), L"" }, g_credTab, UI_CRED_TAB, 13);
    {   // Supporters : halo et contour arc-en-ciel qui tournent (comme l'etoile RGB de l'en-tete), coeur dessine apres le mot
        float w = (seg.Width - 6) / 4;
        RectF s(seg.X + 3 + 3 * w, seg.Y + 3, w, seg.Height - 6);
        bool on = g_credTab == CRED_SUPPORT, hot = g_uiHot == UI_CRED_TAB + CRED_SUPPORT;
        float hue = g_time * 70.0f;
        GraphicsPath sp; RoundRect(sp, s, min(9.0f, s.Height / 2));
        if (!on) { SolidBrush hb(Hue(hue, (BYTE)(hot ? 70 : 40))); g.FillPath(&hb, &sp); }
        SoftLive(s);
        LinearGradientBrush lb(s, Hue(hue), Hue(hue + 180), fmodf(g_time * 90.0f, 360.0f));
        Color cols[5] = { Hue(hue), Hue(hue + 90), Hue(hue + 180), Hue(hue + 270), Hue(hue + 360) };
        REAL pos[5] = { 0.0f, 0.25f, 0.5f, 0.75f, 1.0f };
        lb.SetInterpolationColors(cols, pos, 5);
        Pen rp(&lb, on || hot ? 2.4f : 1.8f); g.DrawPath(&rp, &sp);
        std::wstring lab = T(L"Supporter", L"Supporters");
        float tw = MeasureW(g, lab, 13, FontStyleBold), x0 = s.X + (s.Width - tw - 22) / 2;
        Text(g, lab, RectF(x0, s.Y, tw + 4, s.Height), 13, FontStyleBold, on ? kOnAcc : kInk, StringAlignmentNear);
        Icon(g, IC_HEART, x0 + tw + 14, s.Y + s.Height / 2, 14, on ? kOnAcc : Hue(hue + 40), 2.2f);
    }

    RectF area = CreditsArea();
    float sc = g_scroll[TAB_CREDITS] = min(g_scroll[TAB_CREDITS], CreditsMaxScroll());
    GraphicsState st = g.Save();
    g.SetClip(area, CombineModeIntersect);
    float y = area.Y - sc;
    if (g_credTab == CRED_IMG) {
        // musee de la Corse : un en-tete, puis une ligne par image (titre, auteur, date, licence)
        Text(g, T(L"Musée de la Corse", L"Museum of Corsica"), RectF(area.X + 4, y, 600, 28), 18, FontStyleBold, kInk, StringAlignmentNear);
        Para(g, T(L"Tableaux, cartes, cartes postales et photos d'avant 2000, du domaine public ou sous licence Creative Commons (Wikimedia Commons). Liens complets : MWCoop\\musee\\CREDITS.txt.",
                  L"Paintings, maps, postcards and photos from before 2000, public domain or under Creative Commons licenses (Wikimedia Commons). Full links: MWCoop\\musee\\CREDITS.txt."),
             RectF(area.X + 4, y + 30, area.Width - 8, 36), 12, kGrey);
        y += 74;
        int i = 0;
        for (const CreditImg &k : kCreditImgs) {
            RectF row(area.X, y, area.Width, 40);
            if (i++ % 2 == 0) { GraphicsPath rp; RoundRect(rp, row, 10); SolidBrush rb(TH(card)); g.FillPath(&rb, &rp); }
            Text(g, std::wstring(T(k.titleFr, k.titleEn)) + L"  (" + k.date + L")", RectF(row.X + 14, row.Y + 3, row.Width * 0.55f, 18), 13, FontStyleBold, kInk, StringAlignmentNear);
            Text(g, k.author, RectF(row.X + 14, row.Y + 20, row.Width * 0.6f, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            Text(g, k.license, RectF(row.X + row.Width * 0.6f, row.Y + 3, row.Width * 0.4f - 14, 34), 11.5f, FontStyleRegular, kAcc, StringAlignmentFar);
            y += 42;
        }
        y += 10;
        Text(g, T(L"Maquette en relief : altitudes SRTM (NASA, domaine public) via AWS Terrain Tiles (Mapzen)", L"Relief model: SRTM elevation data (NASA, public domain) via AWS Terrain Tiles (Mapzen)"),
             RectF(area.X + 14, y, area.Width - 28, 22), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
        y += 26;
        Text(g, T(L"Police Nunito · The Nunito Project Authors · licence SIL OFL 1.1", L"Nunito font · The Nunito Project Authors · SIL OFL 1.1 license"),
             RectF(area.X + 14, y, area.Width - 28, 22), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);   // (police du lanceur, launcher.rc 6)
        y += 30;
    } else {
        for (const Credit &k : kCredits) {
            if (k.cat != g_credTab) continue;
            CreditCard(g, k, y, area.X, area.Width);
            y += 116;
        }
    }
    g_credContentH = y + sc - area.Y;
    g.Restore(st);
    Para(g, T(L"Tu as fait quelque chose pour MWCoop ? Partage-le sur le Discord (bouton en bas à gauche) : tu seras crédité ici et dans les notes de version.",
              L"Made something for MWCoop? Share it on the Discord (button at the bottom left): you'll be credited here and in the release notes."),
         RectF(c.X + 28, c.Y + c.Height - 56, c.Width - 56, 40), 12.5f, kGrey);
}

// ---- lancement, partie en cours
static void DrawLaunchPage(Graphics &g)
{
    RectF c(kMain.X + 170, kMain.Y + 120, 660, 360);
    Glass(c);
    int dots = (int)(g_time * 2.5f) % 4;
    SoftLive(c);
    std::wstring title = T(L"My Winter Car se lance", L"My Winter Car is starting");
    title += std::wstring(dots, L'.') + std::wstring(3 - dots, L' ');
    Icon(g, IC_PLAY, c.X + c.Width / 2, c.Y + 62, 36, kAcc, 2.0f);
    Title(g, title, RectF(c.X, c.Y + 96, c.Width, 40), 26, kInk, StringAlignmentCenter);
    Text(g, g_launchInfo, RectF(c.X, c.Y + 138, c.Width, 24), 14.5f, FontStyleRegular, kGrey);
    DrawBar(g, RectF(c.X + 150, c.Y + 184, c.Width - 300, 6), -2);
    Text(g, T(L"La fenêtre du jeu va apparaître.", L"The game window will appear shortly."), RectF(c.X, c.Y + 206, c.Width, 24), 13.5f, FontStyleRegular, kInk);
    Para(g, T(L"Une petite fenêtre Unity peut d'abord demander la résolution : choisis-la puis clique sur Play.", L"A small Unity window may ask for the resolution first: pick it, then click Play."),
         RectF(c.X + 80, c.Y + 238, c.Width - 160, 40), 12.5f, kGrey, StringAlignmentCenter);
    if (!g_launchWarn.empty()) Para(g, g_launchWarn, RectF(c.X + 60, c.Y + 290, c.Width - 120, 48), 13, kWarn, StringAlignmentCenter);
}

// ---- interface
static void DrawLobby(Graphics &g);
static void DrawWikiPage(Graphics &g);   // (wiki.inc)
static bool WikiClick(int id);
static void DrawSkins(Graphics &g);
static void DrawCar(Graphics &g);
static void DrawContent(Graphics &g);
static void DrawMods(Graphics &g);
static void DrawServer(Graphics &g);
static int SoftNavIndex(int tab)   // (soft.inc : sens du changement de page = place dans la barre)
{
    if (tab == TAB_GFX) tab = TAB_COOP;   // (Jeu et Co-op : meme page Reglages)
    for (int i = 0; i < (int)(sizeof(kNav) / sizeof(kNav[0])); i++) if (kNav[i].page == tab) return i;
    if (tab == TAB_CAR) return 4;
    return -1;
}
static void DrawUI(Graphics &g)
{
    if (g_softDemo) { DrawSoftDemo(g); return; }   // (soft.inc : /capture ... composants)
    UpdateButtons();
    std::wstring status;
    int kind;
    EnterCriticalSection(&g_cs);
    status = g_status; kind = g_statusKind;
    LeaveCriticalSection(&g_cs);
    float prog = g_progress;
    if (!g_newUi) DrawRail(g);   // (nouvelle interface : la barre est dans la coque, coque.inc)
    if (g_state == ST_RUNNING) DrawServer(g);
    else if (g_state == ST_LAUNCH || g_state == ST_CLOSING) DrawLaunchPage(g);
    else switch (g_tab) {
    case TAB_LOBBY: DrawLobby(g); break;
    case TAB_SKIN: DrawSkins(g); break;
    case TAB_CAR: DrawCar(g); break;
    case TAB_CONTENT: DrawContent(g); break;
    case TAB_MODS: DrawMods(g); break;
    case TAB_NOTES: DrawNotesPage(g); break;
    case TAB_LOGS: DrawLogsPage(g); break;
    case TAB_COOP: case TAB_GFX: DrawSettingsPage(g); break;   // (gfx.inc : Jeu, Image, Son, Co-op, Touches, Theme, Lanceur)
    case TAB_API: DrawApiPage(g); break;
    case TAB_TOOLS: DrawToolsPage(g); break;
    case TAB_CREDITS: DrawCreditsPage(g); break;
    case TAB_WIKI: DrawWikiPage(g); break;
    case TAB_VHS: DrawVhs(g); break;
    case TAB_ALBUM: DrawAlbum(g); break;
    case TAB_SAVES: DrawSaves(g); break;
    case TAB_BUG: if (SigActive() || g_sigCapture) DrawSigPage(g); else DrawBugPage(g); break;   // (signalnatif.inc : la version native ; sinon   // (signalement.inc : la bande ; le site, dans sa popup par-dessus)
    default: DrawHome(g); break;
    }
    DrawHeader(g);
    if (!g_newUi) DrawFooter(g, status, kind, prog);   // (nouvelle interface : barre d'etat de la coque)
    DrawSaveModal(g);   // (saves.inc : par-dessus tout)
}

// Clic sur une zone (UI_*). Vrai si traite.
static void SteamGuideOpen();
static void LobbyOptsClick(int id);
static void LobbyOptClick(int id);   // (cartes du bas du salon : code, public / prive, joueurs max, options)
static bool UiClick(int id)
{
    if (id == UI_NONE) return true;
    if (id >= UI_SIG && id < UI_SIG + 1000) return SigClick(id);   // (signalnatif.inc)
    if (id == UI_SET_SIGNALNATIF) { bool on = SigNativeOn(); WritePrivateProfileStringW(L"Lanceur", L"SignalerNatif", on ? L"0" : NULL, g_iniLauncher.c_str()); if (on) { g_sigView = SV_LOADING; } else BugPanelClose(); return true; }
    if (id >= UI_ADDON_VIEW && id < UI_ADDON_VIEW + 2) { AddonsSetView(id - UI_ADDON_VIEW); return true; }
    if (id >= UI_ADDON_FILTER && id < UI_ADDON_FILTER + 8) { AddonsSetFilter(id - UI_ADDON_FILTER); return true; }
    if (id >= UI_ADDON_GET && id < UI_ADDON_GET + 20) { int i = id - UI_ADDON_GET; if (i < (int)g_addonList.size()) AddonDownload(g_addonList[i].key); return true; }   // (catalogue : Telecharger / Mettre a jour)
    if (id >= UI_ADDON_CARD && id < UI_ADDON_CARD + 20) { g_addonSel = id - UI_ADDON_CARD; g_addonThumbOf.clear(); return true; }   // (onglet Addons : carte -> son volet ; 1.5.2, clic oublie en 1.5.0)
    if (id >= UI_PAL_ROW && id < UI_PAL_ROW + kPalRows) return ShellClick(id);   // (coque.inc : palette)
    if (id >= UI_NAV && id < UI_NAV + TAB_COUNT) { int p = id - UI_NAV; if (PageEnabled(p)) GoPage(p); return true; }
    if (id >= UI_NAV_GROUP && id < UI_NAV_GROUP + 5) { RailToggle(id - UI_NAV_GROUP); return true; }
    if (id == UI_SEG || id == UI_SEG + 1) {   // (en-tete : Contenu / Mods ; Personnage et Media ont leurs propres onglets)
        int p = id == UI_SEG ? TAB_CONTENT : TAB_MODS;
        if (p == TAB_MODS && g_tab == TAB_CONTENT) { g_tab = TAB_MODS; MscScan(); return true; }   // (meme volet ouvert : on garde les valeurs a rendre)
        if (p == TAB_CONTENT && g_tab == TAB_MODS && PageSnap(TAB_CONTENT)) { GoPage(p); return true; }
        if (PageSnap(p) && PageSnap(g_tab)) { g_tab = p; if (p == TAB_SKIN) g_skinIdleT = GetTickCount(); return true; }
        GoPage(p);
        return true;
    }
    switch (id) {
    case UI_HOME_LOOK: case UI_HOME_LOOK + 100: GoPage(TAB_SKIN); return true;
    case UI_HOME_CAR: GoPage(TAB_CAR); return true;
    case UI_HOME_NOTES: GoPage(TAB_NOTES); return true;
    case UI_HOME_LOBBY: GoPage(TAB_LOBBY); return true;
    case UI_HOME_STEAMOPT: SteamGuideOpen(); return true;
    case UI_PAGE_OK: PageOk(); return true;
    case UI_PAGE_CANCEL: PageCancel(); return true;
    case UI_NOTE_UPDATE: if (g_lobby == LB_NONE && !g_busy) StartUpdate(true); return true;
    case UI_LANCEUR_UPDATE: if (g_lobby == LB_NONE && !g_busy) StartLauncherUpdate(); return true;
    case UI_SET_CHECK: if (!g_busy) StartUpdate(false); return true;
    case UI_SET_CANAL: case UI_SET_CANAL + 1:
        GameSet(id == UI_SET_CANAL ? GAME_MSC : GAME_MWC, false);
        return true;
    case UI_SET_AUTO: {
        bool on = GetPrivateProfileIntW(L"Lanceur", L"MajAuto", 0, g_iniLauncher.c_str()) != 0;
        WritePrivateProfileStringW(L"Lanceur", L"MajAuto", on ? L"0" : L"1", g_iniLauncher.c_str());
        return true;
    }
    case UI_SET_DARK: case UI_SET_LIGHT:
        g_dark = id == UI_SET_DARK;
        WritePrivateProfileStringW(L"Lanceur", ThemeKey(), g_dark ? (g_game == GAME_MSC ? L"ete" : L"sombre") : L"clair", g_iniLauncher.c_str());
        return true;
    case UI_SET_LANG: case UI_SET_LANG + 1: case UI_SET_LANG + 2: case UI_SET_LANG + 3:
        SetLang(id == UI_SET_LANG ? L"fr" : id == UI_SET_LANG + 1 ? L"en" : id == UI_SET_LANG + 2 ? L"ru" : L"ar");
        return true;
    case UI_SET_CLOSE: if (const Opt *o = OptByKey("FermerLanceur")) OptSet(*o, OptGet(*o) ? 0 : 1); return true;
    case UI_SET_START: case UI_SET_START1: case UI_SET_START2:
        if (const Opt *o = OptByKey("LancerSteam")) {
            int k = id - UI_SET_START;
            if (k == 2 && IsWine()) {   // (Wine : steam:// ne passe pas notre WINEDLLOVERRIDES ; cf. LaunchGame)
                SetStatus(K_WARN, T(L"Sous Linux : mets l'option de lancement Steam du GUIDE (Linux et Steam Deck) et lance MWCoop depuis Steam",
                                    L"On Linux: put the Steam launch option from the GUIDE (Linux and Steam Deck) and start MWCoop from Steam"));
                return true;
            }
            OptSet(*o, k == 2 ? 1 : 0);
            WritePrivateProfileStringW(L"Lanceur", L"SansCopie", k == 1 ? L"1" : L"0", g_iniLauncher.c_str());
            LaunchLog("demarrage du jeu choisi : %s", k == 0 ? "copie de lancement" : k == 1 ? "dossier du jeu" : "Steam");
            SetStatus(K_NORMAL, k == 0 ? T(L"Le jeu partira de la copie de lancement (r\u00E9glage d'origine)", L"The game will start from the launch copy (default)")
                              : k == 1 ? T(L"Le jeu partira directement de son dossier", L"The game will start straight from its folder")
                                       : T(L"Le jeu sera lanc\u00E9 par Steam (le lanceur se ferme)", L"The game will be started through Steam (the launcher closes)"));
        }
        return true;
    case UI_SET_STEAMRUN:
        if (const Opt *o = OptByKey("LancerSteam")) {
            OptSet(*o, OptGet(*o) ? 0 : 1);
            SetStatus(K_NORMAL, OptGet(*o) ? T(L"Le jeu sera lanc\u00E9 par Steam (comme son bouton JOUER)", L"The game will be started through Steam (like its PLAY button)") : T(L"Le jeu sera lanc\u00E9 par le lanceur", L"The game will be started by the launcher"));
        }
        return true;
    case UI_SET_MODSAUTO: if (const Opt *o = OptByKey("ModsAuto")) OptSet(*o, OptGet(*o) ? 0 : 1); return true;
    case UI_FACE_IMPORT: FaceImport(); return true;
    case UI_CAR_TRIM: case UI_CAR_TRIM + 1: case UI_CAR_TRIM + 2: case UI_CAR_TRIM + 3: case UI_CAR_TRIM + 4: CarTrimSet(id - UI_CAR_TRIM); return true;
    case UI_CAR_VIN: g_vinView = !g_vinView; g_paintView = false; return true;
    case UI_CAR_PAINT: g_paintView = !g_paintView; g_vinView = false; return true;
    case UI_CAR_EVOIMP: AddonImport(); return true;
    case UI_CAR_EVOTPL: AddonTemplate(); return true;
    case UI_CAR_EVOUSE: if (const Addon *a = AddonCur()) AddonLiveryUse(*a, !AddonLiveryOn(*a)); return true;
    case UI_CAR_EVODEL: AddonRemove(); return true;
    case UI_ADDON_BACK: g_addonSel = -1; return true;
    case UI_ADDONS_CARS: g_addonsPage = 1; g_addonSel = -1; g_scroll[TAB_CAR] = 0; return true;   // (modules.inc : accueil -> les voitures)
    case UI_ADDONS_MODS: g_kuplaNewSession = false; return true;   // (les modules sont deja dessous ; NEW lu)
    case UI_ADDONS_HOME: g_addonsPage = 0; g_addonSel = -1; return true;
    case UI_KUPLA_RENDU: KuplaRenduToggle(); return true;
    case UI_KUPLA_NIVEAU: case UI_KUPLA_NIVEAU + 1: case UI_KUPLA_NIVEAU + 2: if (KuplaRenduGet()) KuplaNiveauSet(id - UI_KUPLA_NIVEAU + 1); return true;   // (grise quand Kupla est eteint)
    case UI_KUPLA_COMPTEUR: KuplaCompteurToggle(); return true;
    case UI_BUG_ZIP: case UI_BUG_BROWSER: case UI_BUG_CLOSE: return BugClick(id);   // (signalement.inc : la bande du panneau)
    case UI_RAIL_COLLAPSE: case UI_STATUS_LOBBY: case UI_PALETTE_OPEN: return ShellClick(id);   // (coque.inc)
    case UI_SET_SIGNALNAV: {   // (Reglages > Lanceur > Depannage : Signaler un bug dans le navigateur)
        bool on = GetPrivateProfileIntW(L"Lanceur", L"SignalerNavigateur", 0, g_iniLauncher.c_str()) != 0;
        WritePrivateProfileStringW(L"Lanceur", L"SignalerNavigateur", on ? NULL : L"1", g_iniLauncher.c_str());
        return true;
    }
    case UI_ADDON_DIR: AddonsOpenDir(); return true;
    case UI_ADDON_UNINST: AddonUninstall(); return true;
    case UI_ADDON_UPD: if (const Addon *a = AddonCur()) AddonDownload(a->key); return true;
    case UI_ADDON_HELP: WikiOpenAddons(); return true;
    case UI_CAR_DENTS: CarRemoveDents(); return true;
    case UI_CAR_PAINTIMP: PaintImport(); return true;
    case UI_CAR_PAINTTPL: PaintTemplate(); return true;
    case UI_CAR_PAINTUSE: PaintUse(!PaintOn()); return true;
    case UI_CAR_PAINTDEL: PaintRemove(); return true;
    case UI_FACE_TPL: FaceTemplate(); return true;
    case UI_FACE_DEL: FaceRemove(); return true;
    case UI_SET_KEY: case UI_SET_KEY + 1: case UI_SET_KEY + 2: g_keyCapture = g_keyCapture == id - UI_SET_KEY ? -1 : id - UI_SET_KEY; return true;
    case UI_SET_GUIDE: TutoStart(true); return true;
    case UI_SET_STEAM: case UI_SET_IP: if ((id == UI_SET_STEAM) != g_steamNet && g_lobby == LB_NONE) OnButton(id == UI_SET_STEAM ? B_NETSTEAM : B_NETIP); return true;
    case UI_SET_PORTM: case UI_SET_PORTP: for (int i = 0; i < (int)g_opts.size(); i++) if (!strcmp(g_opts[i].key, "Port")) OptStep(i, id == UI_SET_PORTP ? 1 : -1); return true;
    case UI_SET_FOLDER: ChooseExe(); return true;
    case UI_SET_BUY: ShellExecuteW(g_wnd, L"open", StoreUrl().c_str(), NULL, NULL, SW_SHOWNORMAL); return true;
    case UI_SET_UNINST: Uninstall(); return true;
    case UI_SET_COMPAT: {
        bool on = !CompatAuto();
        WritePrivateProfileStringW(L"Lanceur", L"RetirerCompat", on ? L"1" : L"0", g_iniLauncher.c_str());
        LaunchLog("retrait des modes de compatibilite %s", on ? "active" : "coupe");
        if (on) CompatCleanAll("reglage");
        SetStatus(K_NORMAL, on ? T(L"Les modes de compatibilit\u00E9 de Windows seront retir\u00E9s avant chaque partie", L"Windows compatibility modes will be removed before every game")
                               : T(L"Modes de compatibilit\u00E9 gard\u00E9s : MWCoop risque de ne pas se charger", L"Compatibility modes kept: MWCoop may not load"));
        return true;
    }
    case UI_LOG_DEBUG:
        g_debug = !g_debug;
        WritePrivateProfileStringW(L"Lanceur", L"Debug", g_debug ? L"1" : L"0", g_iniLauncher.c_str());
        LaunchLog("mode debogage %s", g_debug ? "active" : "coupe");
        if (g_debug) DebugSys();
        SetStatus(K_NORMAL, g_debug ? T(L"Mode débogage activé : lance ta partie, puis crée le zip", L"Debug mode on: start your game, then create the zip") : T(L"Mode débogage coupé", L"Debug mode off"));
        return true;
    }
    if (id >= UI_CAR_VINOPT && id < UI_CAR_VINOPT + kVinKeyN) { VinCycle(id - UI_CAR_VINOPT); return true; }
    if (id >= UI_WIKI_CAT && id <= UI_WIKI_FIG) return WikiClick(id);
    if (id >= UI_NOTE && id < UI_NOTE + 90) { g_noteSel = id - UI_NOTE; g_scroll[TAB_NOTES] = 0; return true; }
    if (id >= UI_LOOKPIN && id < UI_LOOKPIN + 4) { g_lookZone = id - UI_LOOKPIN; return true; }
    if (id == UI_LOOKROT || id == UI_LOOKROT + 1) { g_skinYaw = fmodf(g_skinYaw + (id == UI_LOOKROT ? 2.0f : -2.0f) + 16, 16.0f); g_skinIdleT = GetTickCount(); return true; }
    if (id == UI_LOOKRAND) { LookRandom(); return true; }
    if (id >= UI_CRED_TAB && id < UI_CRED_TAB + 4) { g_credTab = id - UI_CRED_TAB; g_scroll[TAB_CREDITS] = 0; return true; }
    if (id == UI_LOOKRESET) { LookReset(); return true; }
    if (id >= UI_LOBBY_GEAR && id <= UI_LOBBY_ME_CAR) { LobbyOptsClick(id); return true; }
    if ((id >= UI_LOBBY_PRIV && id < 3400) || (id >= UI_LOBBY_OPT && id < UI_LOBBY_OPT + 600)) { LobbyOptClick(id); return true; }   // (3300-3399 et 10400-10999 : salon.inc)
    if (id >= UI_API_DOC && id <= UI_API_EX1) { ApiClick(id); return true; }
    if (id >= UI_TOOL_GET && id <= UI_TOOL_CV_DIR) { ToolsClick(id); return true; }
    if (id == UI_PLACES_OPEN || id == UI_PLACES_EXPORT) { PlacesClick(id); return true; }   // (page Outils : placement des sieges, outils-places.inc)
    if (id >= UI_SAVE_NEW && id < UI_SAVE_END) { SaveClick(id); return true; }   // (saves.inc)
    if (id >= UI_VHS_OPEN && id < UI_ALB_OPEN) { VhsClick(id); return true; }   // (media.inc)
    if (id >= UI_ALB_OPEN && id < UI_ALB + 4000) { AlbClick(id); return true; }
    if (id == UI_LOG_CLEAR) {   // second clic dans les 4 s : les parties rangees a la corbeille
        if (g_logDelKey == L"**" && GetTickCount() - g_logDelT < 4000) { g_logDelKey.clear(); LogsDeleteAll(); }
        else { g_logDelKey = L"**"; g_logDelT = GetTickCount(); SetStatus(K_WARN, T(L"Clique encore pour mettre toutes les parties rangées à la corbeille", L"Click again to move every stored session to the Recycle Bin")); }
        return true;
    }
    if (SetClick(id)) return true;   // (page Reglages : onglets, recherche, theme, et les reglages eux-memes)
    return false;
}
