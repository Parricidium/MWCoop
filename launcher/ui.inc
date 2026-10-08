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
    RectF c(0, 0, kCardW, kCardH);
    {   // ciel
        LinearGradientBrush sky(RectF(0, -1, kCardW, kCardH + 2), Color(255, 0, 0, 0), Color(255, 0, 0, 0), LinearGradientModeVertical);
        Color dc[4] = { Color(255, 4, 8, 22), Color(255, 10, 21, 52), Color(255, 22, 40, 90), Color(255, 34, 56, 110) };
        Color lc[4] = { Color(255, 132, 176, 226), Color(255, 190, 216, 242), Color(255, 238, 220, 214), Color(255, 248, 233, 223) };
        REAL pos[4] = { 0, 0.45f, 0.78f, 1 };
        sky.SetInterpolationColors(dk ? dc : lc, pos, 4);
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
            if (dk) {
                Color a[4] = { Color(0, 47, 255, 176), Color(190, 47, 255, 176), Color(140, 47, 214, 255), Color(0, 138, 107, 255) };
                Color b[4] = { Color(0, 138, 107, 255), Color(128, 176, 107, 255), Color(115, 47, 255, 176), Color(0, 47, 255, 176) };
                band({ -60, 250, 160, 120, 330, 300, 560, 180, 790, 60, 900, 60, 1140, 170, 1380, 280, 1360, 140, 1400, 120 },
                     { 1400, 230, 1180, 260, 1010, 170, 780, 290, 550, 410, 380, 330, 160, 300, -60, 270, -40, 330, -60, 340 }, a, 4, 1.0f);
                band({ -40, 140, 220, 60, 420, 200, 700, 110, 980, 20, 1080, 30, 1320, 90 },
                     { 1320, 150, 1060, 120, 900, 200, 640, 190, 380, 180, 200, 170, -40, 220 }, b, 4, 1.0f);
                band({ 300, 330, 520, 260, 760, 380, 1000, 300, 1240, 220, 1240, 270, 1320, 300 },
                     { 1320, 350, 1080, 360, 900, 400, 640, 390, 380, 380, 380, 380, 300, 380 }, a, 4, 0.6f);
            } else {
                Color a[4] = { Color(0, 255, 190, 200), Color(120, 255, 200, 214), Color(100, 190, 210, 255), Color(0, 255, 230, 210) };
                band({ -60, 250, 160, 160, 330, 300, 560, 220, 790, 140, 900, 120, 1140, 200, 1380, 280, 1360, 180, 1400, 160 },
                     { 1400, 260, 1180, 290, 1010, 220, 780, 320, 550, 410, 380, 350, 160, 330, -60, 300, -40, 350, -60, 360 }, a, 4, 0.9f);
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
    if (dk) {   // etoiles
        SolidBrush sb(Color(200, 230, 238, 255));
        for (int i = 0; i < 90; i++) {
            float x = Rnd01(i, 81) * kCardW, y = Rnd01(i, 82) * 330, r = 0.5f + Rnd01(i, 83) * 0.9f;
            sb.SetColor(Color((BYTE)(90 + 140 * Rnd01(i, 84)), 230, 238, 255));
            g.FillEllipse(&sb, x - r, y - r, 2 * r, 2 * r);
        }
    }
    {   // lune (sombre) ou soleil pale (clair)
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
        SolidBrush fb(dk ? Color(235, 19, 37, 82) : Color(255, 178, 198, 228)); g.FillPath(&fb, &fp);
        PointF near_[] = { PointF(0, 600), PointF(90, 562), PointF(200, 588), PointF(300, 548), PointF(430, 598), PointF(560, 562), PointF(700, 606), PointF(820, 566), PointF(960, 598), PointF(1100, 562), PointF(1280, 598) };
        GraphicsPath np; np.AddCurve(near_, 11, 0.45f); np.AddLine(PointF(1280, 598), PointF(1280, 800)); np.AddLine(PointF(1280, 800), PointF(0, 800)); np.CloseFigure();
        SolidBrush nb(dk ? Color(255, 14, 29, 68) : Color(255, 150, 174, 210)); g.FillPath(&nb, &np);
    }
    {   // sapins
        SolidBrush ink(dk ? Color(255, 10, 22, 54) : Color(255, 44, 68, 94)), snow(dk ? Color(150, 198, 212, 234) : Color(230, 250, 252, 255));
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
        LinearGradientBrush sb(RectF(0, 625, kCardW, 180), dk ? Color(255, 159, 182, 220) : Color(255, 252, 253, 255), dk ? Color(255, 93, 114, 153) : Color(255, 214, 226, 242), LinearGradientModeVertical);
        g.FillPath(&sb, &sp);
        GraphicsPath dp;
        BezPath(dp, { 0, 700, 260, 680, 520, 712, 780, 696, 1040, 680, 1140, 690, 1280, 704 });
        dp.AddLine(PointF(1280, 704), PointF(1280, 800)); dp.AddLine(PointF(1280, 800), PointF(0, 800)); dp.CloseFigure();
        SolidBrush db(dk ? Color(140, 125, 147, 189) : Color(200, 236, 242, 250)); g.FillPath(&db, &dp);
    }
    {   // maison eclairee et vieille voiture
        GraphicsPath gp; gp.AddEllipse(865.0f, 492.0f, 240.0f, 240.0f);
        PathGradientBrush pg(&gp);
        pg.SetCenterColor(dk ? Color(140, 255, 179, 71) : Color(70, 255, 179, 71));
        Color edge(0, 255, 179, 71); int one = 1;
        pg.SetSurroundColors(&edge, &one);
        g.FillPath(&pg, &gp);
        SolidBrush wall(Color(255, 74, 42, 36)), roofD(Color(255, 58, 34, 32)), roofS(dk ? Color(255, 223, 233, 247) : Color(255, 250, 252, 255)), win(Color(255, 255, 199, 106)), door(Color(255, 36, 19, 15));
        g.FillRectangle(&wall, 922.0f, 586.0f, 128.0f, 62.0f);
        PointF rs[] = { PointF(908, 590), PointF(986, 540), PointF(1064, 590) }; g.FillPolygon(&roofS, rs, 3);
        PointF rd[] = { PointF(916, 592), PointF(986, 548), PointF(1056, 592) }; g.FillPolygon(&roofD, rd, 3);
        g.FillRectangle(&roofD, 1022.0f, 548.0f, 12.0f, 28.0f);
        g.FillRectangle(&win, 938.0f, 602.0f, 22.0f, 20.0f); g.FillRectangle(&win, 1012.0f, 602.0f, 22.0f, 20.0f);
        g.FillRectangle(&door, 976.0f, 610.0f, 18.0f, 38.0f);
        SolidBrush car(Color(255, 20, 29, 51)), wheel(Color(255, 7, 12, 24));
        GraphicsPath cp; RoundRect(cp, RectF(1080, 626, 74, 18), 5); g.FillPath(&car, &cp);
        PointF top[] = { PointF(1094, 626), PointF(1104, 612), PointF(1134, 612), PointF(1144, 626) }; g.FillPolygon(&car, top, 4);
        PointF sn[] = { PointF(1094, 612), PointF(1104, 606), PointF(1134, 606), PointF(1140, 612) }; g.FillPolygon(&roofS, sn, 4);
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
    g_sceneW = w; g_sceneH = h; g_sceneScale = scale; g_sceneDarkK = g_dark ? 1 : 0;
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
    if (!g_scene || g_sceneW != w || g_sceneH != h || g_sceneScale != scale || g_sceneDarkK != (g_dark ? 1 : 0)) SceneBuild(w, h, scale);
    if (!g_glassCache) GlassBuild(w, h, scale);
}
// Animation par-dessus le fond : neige (hors des cartes), etoiles qui scintillent et etoile filante (sombre).
static void DrawSceneAnim(Graphics &g)
{
    float t = g_sceneT;
    GraphicsState st = g.Save();
    if (g_outside) g.SetClip(g_outside, CombineModeIntersect); else g.SetClip(CardPath(), CombineModeIntersect);
    SolidBrush b(Color(0, 0, 0, 0));
    if (g_dark) {
        for (int i = 0; i < 40; i++) {
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
        if (p > 0 && p < 0.8f) {
            float x0 = 500 + Rnd01(n, 7) * 600, y0 = 18 + Rnd01(n, 8) * 30;
            float hx = x0 - p * 230, hy = y0 + p * 230 * 0.28f, a = 230 * sinf(p / 0.8f * 3.14159f);
            PointF head(hx, hy), tail(hx + 60, hy - 60 * 0.28f);
            LinearGradientBrush lb(tail, head, Color(0, 220, 232, 255), Color((BYTE)a, 235, 242, 255));
            Pen pen(&lb, 1.4f); pen.SetStartCap(LineCapRound); pen.SetEndCap(LineCapRound);
            g.DrawLine(&pen, tail, head);
        }
    }
    struct Layer { int n; float s0, s1, speed, sway, alpha; } layers[3] = { { 150, 1.0f, 1.9f, 15, 5, 150 }, { 80, 1.9f, 3.0f, 30, 9, 190 }, { 18, 3.2f, 4.6f, 58, 15, 210 } };
    float drift = 9 * t - 46.0f * cosf(t * 0.13f);
    Color halo = g_dark ? Color(255, 160, 180, 220) : Color(255, 110, 140, 185);
    for (int l = 0; l < 3; l++) {
        const Layer &L = layers[l];
        for (int i = 0; i < L.n; i++) {
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
static bool PageEnabled(int p)
{
    if (g_state != ST_IDLE) return false;
    if (g_gameDir.empty()) return p == TAB_HOME || p == TAB_NOTES || p == TAB_COOP || p == TAB_API;
    if (p == TAB_LOBBY) return g_lobby != LB_NONE;
    return true;
}
static int PageBack() { return g_lobby != LB_NONE ? TAB_LOBBY : TAB_HOME; }
static void GoPage(int p)
{
    if (p == g_tab) return;
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
    if (p == TAB_MODS || p == TAB_CONTENT) MscScan();
    if (p == TAB_SKIN) g_skinIdleT = GetTickCount();
    if (p == TAB_GFX) GfxLoad();
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
static const NavItem kNav[] = {
    { TAB_HOME, IC_HOME, L"Accueil", L"Home" }, { TAB_LOBBY, IC_USERS, L"Salon", L"Lobby" },
    { TAB_SKIN, IC_SHIRT, L"Tenue", L"Outfit" }, { TAB_CAR, IC_CAR, L"Voiture", L"Car" },
    { TAB_CONTENT, IC_BOX, L"Contenu envoyé", L"Sent content" }, { TAB_MODS, IC_PUZZLE, L"Mods", L"Mods" },
    { TAB_GFX, IC_SLIDERS, L"Graphismes", L"Graphics" }, { TAB_NOTES, IC_SPARK, L"Nouveautés", L"What's new" } };
// Journaux et API moddeurs : boutons de l'en-tete, a gauche de la langue (demande de JD, 07/10 : place liberee a gauche).
static const int kNavGroupN[] = { 2, 2, 2, 1, 1 };   // pages de chaque groupe, dans l'ordre de kNav
static const int kNavGroups = 5;
static const wchar_t *kNavGroupFr[] = { L"JOUER", L"MON PERSONNAGE", L"PARTAGE", L"JEU", L"INFOS" }, *kNavGroupEn[] = { L"PLAY", L"MY CHARACTER", L"SHARING", L"GAME", L"INFO" };
static Bitmap *g_logo;
static int MscModCount();   // (mods.inc)
static RectF g_navR[TAB_COUNT];   // (bulles de la visite)

static void DrawNavItem(Graphics &g, RectF r, int page, int icon, const wchar_t *label)
{
    bool en = PageEnabled(page), on = g_tab == page && g_state == ST_IDLE, hot = en && g_uiHot == UI_NAV + page;
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
        wchar_t b[16]; swprintf_s(b, L"%d/%d", n, kLobbyMax);
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
    } else if (page == TAB_NOTES && NotesUnseen() && !on) {
        const wchar_t *s = T(L"NOUV.", L"NEW");
        float w = PillW(s, 10);
        Pill(g, RectF(rx - w, r.Y + (r.Height - 20) / 2, w, 20), kAcc, s, kOnAcc, 10);
    }
    if (on && page != TAB_LOBBY && page != TAB_MODS && page != TAB_CAR) { SolidBrush db(kAcc); g.FillEllipse(&db, rx - 6, r.Y + r.Height / 2 - 3, 6.0f, 6.0f); }
    if (en) HotZone(r, UI_NAV + page);
}

static void DrawRail(Graphics &g)
{
    Glass(kRail, 22);
    if (g_logo) {
        float lw = 168, lh = lw * g_logo->GetHeight() / g_logo->GetWidth();
        g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        g.DrawImage(g_logo, RectF(kRail.X + (kRail.Width - lw) / 2, 26, lw, lh));
    }
    // version, PRE-ALPHA
    std::wstring ver = g_localVer.empty() ? L"" : L"v" + g_localVer.substr(0, g_localVer.find(L'-'));
    float vw = ver.empty() ? 0 : PillW(ver, 10.5f), pw = PillW(L"PRE-ALPHA", 10.5f), x = kRail.X + (kRail.Width - vw - pw - (vw > 0 ? 6 : 0)) / 2;
    if (vw > 0) { Pill(g, RectF(x, 104, vw, 20), TH(cardSel), ver, kInk, 10.5f); x += vw + 6; }
    Pill(g, RectF(x, 104, pw, 20), Color(255, 201, 54, 59), L"PRE-ALPHA", Color(255, 255, 255, 255), 10.5f);
    float y0 = 132;
    if (g_btn[B_UPDATE].visible) {   // mise a jour proposee : bouton sous la version
        Button &b = g_btn[B_UPDATE];
        b.r = RectF(28, 134, 208, 40);
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
        y0 = 180;
    }
    // groupes jusqu'a Reglages (674) : lignes de 44 px, ou de 40 quand le bouton de mise a jour prend de la place
    const int nItems = (int)(sizeof(kNav) / sizeof(kNav[0]));
    float avail = 666 - y0, lab = 26, step = 46;
    if (avail - kNavGroups * lab - nItems * step < 6) { lab = 24; step = 42; }
    float gap = min(20.0f, max(0.0f, (avail - kNavGroups * lab - nItems * step) / (kNavGroups - 1))), y = y0;
    for (int k = 0, i = 0; k < kNavGroups; k++) {
        SmallLabel(g, g_fr ? kNavGroupFr[k] : kNavGroupEn[k], RectF(40, y + 2, 190, 20), WithA(kGrey, 0.9f));
        y += lab;
        for (int j = 0; j < kNavGroupN[k]; j++, i++) {
            const NavItem &it = kNav[i];
            DrawNavItem(g, RectF(28, y, 208, step - 2), it.page, it.icon, g_fr ? it.fr : it.en);
            y += step;
        }
        y += gap;
    }
    DrawNavItem(g, RectF(28, 674, 208, 44), TAB_COOP, IC_GEAR, T(L"Réglages", L"Settings"));
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
    if (st.wHour >= 5 && st.wHour < 18) return std::wstring(g_fr ? (st.wHour < 12 ? L"Bonjour, " : L"Bonjour, ") : (st.wHour < 12 ? L"Good morning, " : L"Good afternoon, ")) + n;
    return std::wstring(T(L"Bonsoir, ", L"Good evening, ")) + n;
}
static void DrawLobbyHeadControls(Graphics &g, float &right);   // (salon, plus bas)

// Page ouverte par un bouton de l'en-tete (Journaux, API moddeurs) : comme ceux de la fenetre, contour colore si ouverte.
static void HeadPage(Graphics &g, RectF r, int page, int icon)
{
    bool en = PageEnabled(page), on = g_tab == page && g_state == ST_IDLE, hot = en && g_uiHot == UI_NAV + page;
    g_navR[page] = r;
    GraphicsPath p; RoundRect(p, r, 14);
    SolidBrush fb(on ? TH(cardSel) : Mix(TH(btn2), TH(btn2Hot), hot ? 1.0f : 0.0f)); g.FillPath(&fb, &p);
    Pen pen(on ? WithA(kAcc, 0.8f) : TH(fieldBorder), on ? 1.4f : 1.0f); g.DrawPath(&pen, &p);
    Icon(g, icon, r.X + r.Width / 2, r.Y + r.Height / 2, 18, WithA(on ? kAcc : kInk, en ? 1.0f : 0.4f), 2.0f);
    if (page == TAB_LOGS && g_crashT && !on) {
        RectF d(r.X + r.Width - 13, r.Y - 3, 16, 16);
        SolidBrush rb(kRed); g.FillEllipse(&rb, d);
        Text(g, L"!", d, 10.5f, FontStyleBold, Color(255, 255, 255, 255));
    }
    if (en) HotZone(r, UI_NAV + page);
}
static void DrawHeader(Graphics &g)
{
    std::wstring title, sub;
    if (g_state == ST_RUNNING) { title = T(L"Partie en cours", L"Game running"); sub = g_launchInfo; }
    else if (g_state == ST_LAUNCH || g_state == ST_CLOSING) { title = T(L"Lancement", L"Starting"); sub = g_launchInfo; }
    else switch (g_tab) {
    case TAB_HOME: title = g_gameDir.empty() ? std::wstring(T(L"Bienvenue", L"Welcome")) : Greeting(); sub = T(L"Prêt pour un hiver à plusieurs ?", L"Ready for a winter together?"); break;
    case TAB_LOBBY: {
        int lobby = g_lobby;
        bool host = lobby == LB_HOST;
        if (lobby == LB_CONNECTING) title = T(L"Connexion au salon…", L"Joining the lobby…");
        else if (host) title = std::wstring(T(L"Salon de ", L"Lobby of ")) + PlayerName();
        else if (g_lobbySteam) title = std::wstring(T(L"Salon de ", L"Lobby of ")) + g_sHostName;
        else title = T(L"Salon", L"Lobby");
        EnterCriticalSection(&g_lcs);
        int n = (int)g_peers.size();
        LeaveCriticalSection(&g_lcs);
        wchar_t b[200];
        if (g_lobbySteam) swprintf_s(b, T(L"Steam · amis seulement · %d / %d joueurs", L"Steam · friends only · %d / %d players"), n, kLobbyMax);
        else if (host) swprintf_s(b, T(L"Port %d (UDP et TCP) · adresse locale %s · %d / %d", L"Port %d (UDP and TCP) · local address %s · %d / %d"), g_lobbyPort, g_myAddresses.empty() ? L"?" : g_myAddresses.c_str(), n, kLobbyMax);
        else swprintf_s(b, T(L"Hôte : %s:%d · %d / %d joueurs", L"Host: %s:%d · %d / %d players"), g_lobbyAddr.c_str(), g_lobbyPort, n, kLobbyMax);
        sub = b;
        break;
    }
    case TAB_SKIN: case TAB_CAR: title = T(L"Mon personnage", L"My character"); sub = T(L"Ce que les autres joueurs voient en jeu", L"What the other players see in game"); break;
    case TAB_CONTENT: case TAB_MODS: title = T(L"Partage", L"Sharing"); sub = T(L"Copié chez tes invités pour ta partie, à part de leurs fichiers", L"Copied to your guests for your game, apart from their own files"); break;
    case TAB_NOTES: title = T(L"Nouveautés", L"What's new"); sub = T(L"Ce qui change à chaque version", L"What changes in each version"); break;
    case TAB_LOGS: title = T(L"Journaux", L"Logs"); sub = T(L"Un souci en jeu ? Un zip à envoyer", L"Trouble in game? A zip to send"); break;
    case TAB_COOP: title = T(L"Réglages", L"Settings"); sub = T(L"Gardés dans mwcoop-lanceur.ini et MWCoop\\mwcoop.ini", L"Kept in mwcoop-lanceur.ini and MWCoop\\mwcoop.ini"); break;
    case TAB_GFX: title = T(L"Graphismes", L"Graphics"); sub = T(L"Pour toi seul · en partie : menu F10 > Graphismes", L"Just for you · in game: F10 menu > Graphics"); break;
    case TAB_API: title = T(L"API pour les moddeurs", L"API for modders"); sub = T(L"MWCoop.CoopApi : la doc, la DLL et un exemple", L"MWCoop.CoopApi: docs, DLL and an example"); break;
    }
    // commandes de droite : fermer, reduire, theme, langue, puis celles de la page
    float right = 1264;
    for (int id : { B_CLOSE, B_MIN, B_THEME, B_LANG }) { g_btn[id].r = RectF(right - 44, 22, 44, 44); right -= 54; }
    HeadButton(g, B_CLOSE, IC_CLOSE);
    HeadButton(g, B_MIN, IC_MIN);
    HeadButton(g, B_THEME, g_dark ? IC_SUN : IC_MOON);
    HeadButton(g, B_LANG, 0, g_fr ? L"FR" : L"EN");
    // | puis API moddeurs et Journaux (pastille rouge : la derniere partie s'est arretee brutalement)
    {
        Pen sp(TH(fieldBorder), 1.4f);
        g.DrawLine(&sp, right - 2, 32.0f, right - 2, 56.0f);
        right -= 14;
        HeadPage(g, RectF(right - 44, 22, 44, 44), TAB_API, IC_BRACES);
        right -= 54;
        HeadPage(g, RectF(right - 44, 22, 44, 44), TAB_LOGS, IC_FILE);
        right -= 54;
    }
    if (g_state == ST_IDLE) {
        if (g_tab == TAB_SKIN || g_tab == TAB_CAR || g_tab == TAB_CONTENT || g_tab == TAB_MODS) {
            bool look = g_tab == TAB_SKIN || g_tab == TAB_CAR;
            std::vector<std::wstring> lab;
            if (look) lab = { T(L"Tenue", L"Outfit"), T(L"Voiture", L"Car") };
            else {
                int n = MscModCount();
                lab = { std::wstring(T(L"Contenu · ", L"Content · ")) + ContentSummary(), std::wstring(L"Mods · ") + std::to_wstring(n) };
            }
            float w = look ? 220.0f : 280.0f;
            RectF r(right - w, 22, w, 44);
            UiSeg(g, r, lab, (g_tab == TAB_SKIN || g_tab == TAB_CONTENT) ? 0 : 1, UI_SEG, 13);
            right -= w + 10;
        } else if (g_tab == TAB_LOBBY) DrawLobbyHeadControls(g, right);
    }
    Title(g, title, RectF(268, 16, right - 280, 34), 26, kInk);
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
    std::wstring gl = ok ? L"My Winter Car " + g_gameVer : std::wstring(T(L"My Winter Car introuvable", L"My Winter Car not found"));
    float gw = MeasureW(mg, gl, 12.5f, FontStyleBold) + 6;
    float gx = eb.r.X - 14 - gw;
    if (ok) Icon(g, IC_CHECK, gx - 10, cy, 15, kOk, 2.6f);
    Text(g, gl, RectF(gx, kFooter.Y + 2, gw, 22), 12.5f, FontStyleBold, ok ? kInk : kRed, StringAlignmentNear);
    // a gauche : l'etat
    SolidBrush db(sc); g.FillEllipse(&db, x, cy - 4, 8.0f, 8.0f);
    float sw = gx - 30 - (x + 16);
    Text(g, status, RectF(x + 16, kFooter.Y + 2, sw, 22), 13, FontStyleBold, kind == K_NORMAL ? kInk : sc, StringAlignmentNear);
    if (prog != -1.0f) DrawBar(g, RectF(x + 16, kFooter.Y + 26, min(320.0f, sw), 4), prog);
}

// ---- page ACCUEIL
static void DrawField(Graphics &g, int i, const wchar_t *label);
static void DrawHomeLook(Graphics &g, RectF stage);
static std::vector<std::wstring> NoteBullets(const Note &n, int max);
static void DrawHome(Graphics &g)
{
    if (g_gameDir.empty()) {   // jeu introuvable : choisir l'exe, ou l'acheter
        RectF c(kMain.X + 150, kMain.Y + 110, 700, 380);
        Glass(c);
        Icon(g, IC_ALERT, c.X + c.Width / 2, c.Y + 64, 40, kWarn, 2.0f);
        Title(g, T(L"My Winter Car introuvable", L"My Winter Car not found"), RectF(c.X, c.Y + 96, c.Width, 40), 26, kInk, StringAlignmentCenter);
        Para(g, T(L"MWCoop.exe se pose dans le dossier du jeu (Steam : clic droit sur My Winter Car > Gérer > Parcourir les fichiers locaux). Sinon, montre-lui mywintercar.exe.",
                  L"MWCoop.exe goes in the game folder (Steam: right-click My Winter Car > Manage > Browse local files). Otherwise, show it mywintercar.exe."),
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
    RectF left(kMain.X, kMain.Y, 340, kMain.Height);
    Glass(left);
    SmallLabel(g, T(L"TON PERSONNAGE", L"YOUR CHARACTER"), RectF(left.X + 20, left.Y + 16, 200, 20), kGrey);
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
    float rx = kMain.X + 360, rw = kMain.Width - 360;
    int lobby = g_lobby;
    RectF net(rx, kMain.Y, rw, 188);
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
        if (steam) { Graphics &mg = MeasureG(); float sw = MeasureW(mg, L"STEAM", 15, FontStyleBold); Text(g, T(L"conseillé", L"recommended"), RectF(b.r.X + 58 + sw, b.r.Y + 13, 120, 20), 11.5f, FontStyleBold, kOk, StringAlignmentNear); }
        Text(g, steam ? T(L"Invitations, sans port ni pare-feu", L"Invites, no port or firewall") : T(L"Réseau local, Radmin, Hamachi…", L"LAN, Radmin, Hamachi…"),
             RectF(b.r.X + 54, b.r.Y + 33, b.r.Width - 60, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    RectF row(net.X + 20, net.Y + 126, net.Width - 40, 44);
    if (!g_steamNet) {
        SmallLabel(g, T(L"ADRESSE DE L'HÔTE", L"HOST ADDRESS"), RectF(row.X, row.Y, 150, row.Height), kGrey);
        g_fields[1].r = RectF(row.X + 156, row.Y, row.Width - 156, row.Height);
        DrawField(g, 1, L"");
    } else {   // option de lancement Steam : pour qu'une invitation ouvre MWCoop
        g_fields[1].r = RectF(0, 0, 0, 0);
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
            const wchar_t *lab = host ? T(L"HÉBERGER", L"HOST") : g_joinFallback ? T(L"REJOINDRE EN JEU", L"JOIN IN GAME") : T(L"REJOINDRE", L"JOIN");
            Title(g, lab, RectF(r.X + 22, r.Y + 74, r.Width - 44, 34), 25, acc);
            std::wstring d;
            if (host) d = g_steamNet ? T(L"Ouvre un salon Steam pour tes amis ; chacun se met prêt, puis tu lances.", L"Opens a Steam lobby for your friends; everyone gets ready, then you start.")
                                     : T(L"Ouvre un salon : tes amis le rejoignent, chacun se met prêt, puis tu lances.", L"Opens a lobby: your friends join, everyone gets ready, then you start.");
            else if (g_joinFallback) d = T(L"L'hôte joue déjà : rejoins sa partie directement.", L"The host is already playing: join their game directly.");
            else if (g_steamNet && g_sInvite) d = g_sInviteName + T(L" t'invite : clique pour entrer dans son salon.", L" invites you: click to enter their lobby.");
            else d = g_steamNet ? T(L"Ton invitation Steam, sinon le salon MWCoop d'un ami.", L"Your Steam invite, or else a friend's MWCoop lobby.")
                                : T(L"Le salon de l'adresse saisie au-dessus.", L"The lobby at the address entered above.");
            Para(g, d, RectF(r.X + 22, r.Y + 112, r.Width - 44, 44), 13, host ? WithA(kOnAcc, 0.78f * a) : WithA(kGrey, a));
            Text(g, host ? T(L"Jusqu'à 8 joueurs", L"Up to 8 players") : T(L"Salon d'un ami", L"A friend's lobby"), RectF(r.X + 22, r.Y + r.Height - 40, r.Width - 80, 24), 12.5f, FontStyleBold, host ? WithA(kOnAcc, a) : WithA(kInk, 0.8f * a), StringAlignmentNear);
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
            Text(g, T(L"MWCoop chargé, sans réseau. Ta sauvegarde n'est jamais touchée.", L"MWCoop loaded, no network. Your save is never touched."),
                 RectF(b.r.X + 196, b.r.Y, b.r.Width - 240, b.r.Height), 12.5f, FontStyleRegular, WithA(kGrey, a), StringAlignmentNear);
            Icon(g, IC_CHEVR, b.r.X + b.r.Width - 28, b.r.Y + 30, 18, WithA(kInk, a), 2.2f);
        }
    }
    // nouveautes
    RectF nc(rx, ty + th + 92, rw, kMain.Y + kMain.Height - (ty + th + 92));
    Glass(nc);
    std::vector<Note> notes;
    EnterCriticalSection(&g_cs);
    notes = g_notes;
    LeaveCriticalSection(&g_cs);
    std::wstring head = T(L"Nouveautés", L"What's new");
    if (!notes.empty()) head += L" · " + notes[0].ver.substr(0, notes[0].ver.find(L'-'));
    Title(g, head, RectF(nc.X + 20, nc.Y + 14, 400, 24), 17, kInk);
    {
        bool hot = g_uiHot == UI_HOME_NOTES;
        Text(g, T(L"Tout voir", L"See all"), RectF(nc.X + nc.Width - 120, nc.Y + 14, 100, 24), 13, hot ? FontStyleBold | FontStyleUnderline : FontStyleBold, kAcc, StringAlignmentFar);
        HotZone(RectF(nc.X + nc.Width - 110, nc.Y + 10, 96, 30), UI_HOME_NOTES);
    }
    if (notes.empty()) Text(g, !g_notesDone ? T(L"Chargement…", L"Loading…") : T(L"Notes indisponibles (hors ligne).", L"Notes unavailable (offline)."), RectF(nc.X + 20, nc.Y + 50, nc.Width - 40, 24), 13, FontStyleRegular, kGrey, StringAlignmentNear);
    else {
        std::vector<std::wstring> bl = NoteBullets(notes[0], 3);
        float y = nc.Y + 50;
        for (auto &s : bl) {
            if (y + 22 > nc.Y + nc.Height - 8) break;
            SolidBrush dot(kAcc); g.FillEllipse(&dot, nc.X + 22, y + 8, 6.0f, 6.0f);
            Text(g, s, RectF(nc.X + 38, y, nc.Width - 58, 22), 13.5f, FontStyleRegular, kInk, StringAlignmentNear);
            y += 30;
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
    else if (ap) chips.push_back((g_fr ? ap->labFr : ap->labEn)[OptGet(*ap)]);
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
    const std::wstring &body = g_fr ? n.fr : n.en;
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
static const RectF kNotesList(264, 88, 280, 656), kNotesBody(560, 88, 704, 656);
static RectF NotesArea() { return RectF(kNotesBody.X + 28, kNotesBody.Y + 92, kNotesBody.Width - 46, kNotesBody.Height - 108); }
static void DrawNotesPage(Graphics &g)
{
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
        int cmp = g_localVer.empty() ? 1 : CmpVer(notes[i].ver, g_localVer);
        SolidBrush dot(cmp > 0 ? kAcc : cmp == 0 ? kOk : WithA(kGrey, 0.6f));
        g.FillEllipse(&dot, r.X + 14, r.Y + r.Height / 2 - 5, 10.0f, 10.0f);
        Text(g, notes[i].ver.substr(0, notes[i].ver.find(L'-')), RectF(r.X + 36, r.Y + 10, 120, 24), 15, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, notes[i].date, RectF(r.X + 36, r.Y + 33, 140, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        if (cmp >= 0 && !g_localVer.empty()) {
            const wchar_t *lab = cmp == 0 ? T(L"INSTALLÉE", L"INSTALLED") : T(L"NOUVELLE", L"NEW");
            float w = PillW(lab, 9.5f);
            Pill(g, RectF(r.X + r.Width - w - 10, r.Y + 22, w, 20), cmp == 0 ? WithA(kOk, 0.2f) : kAcc, lab, cmp == 0 ? kOk : kOnAcc, 9.5f);
        }
        HotZone(r, UI_NOTE + i);
    }
    g.Restore(st);
    // detail
    const Note &n = notes[g_noteSel];
    RectF b = kNotesBody;
    Title(g, L"MWCoop " + n.ver.substr(0, n.ver.find(L'-')), RectF(b.X + 28, b.Y + 22, 400, 38), 28, kInk);
    Text(g, n.date, RectF(b.X + 28, b.Y + 58, 300, 20), 13, FontStyleRegular, kGrey, StringAlignmentNear);
    int cmp = g_localVer.empty() ? 1 : CmpVer(n.ver, g_localVer);
    if (cmp > 0 && !UpdAvail().empty()) UiButton(g, RectF(b.X + b.Width - 200, b.Y + 26, 172, 44), T(L"Mettre à jour", L"Update"), UI_NOTE_UPDATE, true, IC_DOWNLOAD);
    else if (cmp == 0) { const wchar_t *lab = T(L"INSTALLÉE", L"INSTALLED"); float w = PillW(lab, 11); Pill(g, RectF(b.X + b.Width - 28 - w, b.Y + 36, w, 24), WithA(kOk, 0.2f), lab, kOk); }
    Pen sep(TH(sep), 1); g.DrawLine(&sep, b.X + 28, b.Y + 84, b.X + b.Width - 28, b.Y + 84);
    // corps : puces, paragraphes, images (defilement)
    RectF area = NotesArea();
    const std::wstring &body = g_fr ? n.fr : n.en;
    FontFamily fam(L"Segoe UI");
    Font fb(&fam, 14.5f, FontStyleRegular, UnitPixel);
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
        g.MeasureString(t.c_str(), -1, &fb, RectF(0, 0, w - (x - area.X), 100000), &sf, &box);
        if (y + box.Height >= area.Y && y <= area.Y + area.Height) {
            if (bullet) { SolidBrush dot(kAcc); g.FillEllipse(&dot, area.X + 6, y + 8, 7.0f, 7.0f); }
            g.DrawString(t.c_str(), -1, &fb, RectF(x, y, w - (x - area.X), box.Height + 2), &sf, &ink);
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
    RectF c = kMain;
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
        Para(g, T(L"Aucun journal pour l'instant : le jeu en écrit à chaque lancement avec MWCoop.", L"No logs yet: the game writes them every time it starts with MWCoop."),
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

// ---- page REGLAGES
static void DrawSettings(Graphics &g)
{
    float cw = (kMain.Width - 16) / 2, lx = kMain.X, rx = kMain.X + cw + 16;
    // Mise a jour
    RectF up(lx, kMain.Y, cw, 196);
    Glass(up);
    Title(g, T(L"Mise à jour", L"Updates"), RectF(up.X + 20, up.Y + 16, 300, 24), 17, kInk);
    {
        RectF box(up.X + 20, up.Y + 52, up.Width - 40, 64);
        std::wstring av = UpdAvail();
        GraphicsPath p; RoundRect(p, box, 14);
        SolidBrush fb(av.empty() ? TH(card) : WithA(kAcc, 0.14f)); g.FillPath(&fb, &p);
        Pen pen(av.empty() ? TH(choiceBorder) : WithA(kAcc, 0.5f), 1); g.DrawPath(&pen, &p);
        std::wstring l1, l2 = g_localVer.empty() ? std::wstring(T(L"MWCoop n'est pas installé", L"MWCoop is not installed")) : std::wstring(T(L"Tu as la ", L"You have ")) + g_localVer;
        if (!av.empty()) l1 = std::wstring(T(L"Version ", L"Version ")) + av + T(L" disponible", L" available");
        else if (g_busy) l1 = T(L"Recherche ou installation en cours…", L"Checking or installing…");
        else l1 = T(L"MWCoop est à jour", L"MWCoop is up to date");
        Text(g, l1, RectF(box.X + 16, box.Y + 10, box.Width - 200, 22), 14, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, l2, RectF(box.X + 16, box.Y + 33, box.Width - 200, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        if (!av.empty()) UiButton(g, RectF(box.X + box.Width - 172, box.Y + 12, 160, 40), T(L"Mettre à jour", L"Update"), UI_NOTE_UPDATE, true, IC_DOWNLOAD, g_lobby == LB_NONE && !g_busy);
        else UiButton(g, RectF(box.X + box.Width - 172, box.Y + 12, 160, 40), T(L"Vérifier", L"Check"), UI_SET_CHECK, false, -1, !g_busy);
    }
    {
        RectF row(up.X + 20, up.Y + 128, up.Width - 40, 52);
        bool on = GetPrivateProfileIntW(L"Lanceur", L"MajAuto", 0, g_iniLauncher.c_str()) != 0;
        SetRowText(g, RectF(row.X, row.Y, row.Width - 70, row.Height), T(L"Installer automatiquement", L"Install automatically"), T(L"À chaque démarrage, sans demander", L"At every start, without asking"));
        UiToggle(g, RectF(row.X + row.Width - 50, row.Y + 12, 50, 28), on, g_uiHot == UI_SET_AUTO);
        HotZone(row, UI_SET_AUTO);
    }
    // Lanceur
    RectF la(lx, up.Y + up.Height + 16, cw, 268);
    Glass(la);
    Title(g, T(L"Lanceur", L"Launcher"), RectF(la.X + 20, la.Y + 16, 300, 24), 17, kInk);
    float y = la.Y + 54;
    Text(g, T(L"Thème", L"Theme"), RectF(la.X + 20, y, 200, 40), 14, FontStyleBold, kInk, StringAlignmentNear);
    UiSeg(g, RectF(la.X + la.Width - 220, y, 200, 40), { T(L"Sombre", L"Dark"), T(L"Clair", L"Light") }, g_dark ? 0 : 1, UI_SET_DARK);
    y += 50;
    Text(g, T(L"Langue", L"Language"), RectF(la.X + 20, y, 200, 40), 14, FontStyleBold, kInk, StringAlignmentNear);
    UiSeg(g, RectF(la.X + la.Width - 220, y, 200, 40), { L"Français", L"English" }, g_fr ? 0 : 1, UI_SET_FR);
    y += 50;
    if (const Opt *fl = OptByKey("FermerLanceur")) {
        RectF row(la.X + 20, y, la.Width - 40, 52);
        SetRowText(g, RectF(row.X, row.Y, row.Width - 70, row.Height), T(L"Fermer le lanceur au lancement", L"Close the launcher at launch"), T(L"Sinon il reste ouvert (joueurs, ping) et revient au menu", L"Otherwise it stays open (players, ping) and comes back"));
        UiToggle(g, RectF(row.X + row.Width - 50, row.Y + 12, 50, 28), OptGet(*fl) != 0, g_uiHot == UI_SET_CLOSE);
        HotZone(row, UI_SET_CLOSE);
    }
    y += 62;
    UiButton(g, RectF(la.X + 20, y, la.Width - 40, 44), T(L"Revoir le guide de démarrage", L"See the starting guide again"), UI_SET_GUIDE, false, IC_SPARK);
    // A propos (sous le lanceur)
    RectF ab(lx, la.Y + la.Height + 16, cw, kMain.Y + kMain.Height - (la.Y + la.Height + 16));
    Glass(ab);
    Title(g, T(L"À propos", L"About"), RectF(ab.X + 20, ab.Y + 16, 300, 24), 17, kInk);
    Para(g, T(L"Mod non officiel et non commercial, non affilié à Amistech Games. Nécessite une copie légale de My Winter Car.",
              L"Unofficial, non-commercial mod, not affiliated with Amistech Games. Requires a legal copy of My Winter Car."),
         RectF(ab.X + 20, ab.Y + 48, ab.Width - 40, 40), 12.5f, kGrey);
    // (kSetBuy, kSetUninst : ab.X + 20 / + 248, ab.Y + ab.Height - 60)
    UiButton(g, kSetBuy, T(L"Acheter le jeu sur Steam", L"Buy the game on Steam"), UI_SET_BUY, false);
    UiButton(g, kSetUninst, T(L"Désinstaller MWCoop", L"Uninstall MWCoop"), UI_SET_UNINST, false, IC_TRASH);

    // Reseau
    RectF ne(rx, kMain.Y, cw, 196);
    Glass(ne);
    Title(g, T(L"Réseau", L"Network"), RectF(ne.X + 20, ne.Y + 16, 300, 24), 17, kInk);
    y = ne.Y + 54;
    Text(g, T(L"Par défaut", L"Default"), RectF(ne.X + 20, y, 200, 40), 14, FontStyleBold, kInk, StringAlignmentNear);
    UiSeg(g, RectF(ne.X + ne.Width - 220, y, 200, 40), { L"Steam", L"IP / VPN" }, g_steamNet ? 0 : 1, UI_SET_STEAM);
    y += 52;
    if (const Opt *po = OptByKey("Port")) {
        RectF row(ne.X + 20, y, ne.Width - 40, 60);
        SetRowText(g, RectF(row.X, row.Y, row.Width - 170, row.Height), T(L"Port", L"Port"), T(L"UDP et TCP, pour héberger par IP", L"UDP and TCP, to host by IP"));
        RectF m(row.X + row.Width - 150, row.Y + 12, 36, 36), p(row.X + row.Width - 36, row.Y + 12, 36, 36);
        for (int k = 0; k < 2; k++) {
            RectF r = k ? p : m; int id = k ? UI_SET_PORTP : UI_SET_PORTM;
            GraphicsPath bp; RoundRect(bp, r, 10);
            SolidBrush b(g_uiHot == id ? TH(cardSel) : TH(card)); g.FillPath(&b, &bp);
            Icon(g, k ? IC_PLUS : IC_MIN, r.X + 18, r.Y + 18, 16, kInk, 2.2f);
            HotZone(r, id);
        }
        Text(g, std::to_wstring(OptGet(*po)), RectF(m.X + m.Width, row.Y, p.X - m.X - m.Width, row.Height), 15, FontStyleBold, kInk);
    }
    // Jeu
    RectF ga(rx, ne.Y + ne.Height + 16, cw, 234);
    Glass(ga);
    Title(g, T(L"Jeu", L"Game"), RectF(ga.X + 20, ga.Y + 16, 300, 24), 17, kInk);
    {
        bool ok = !g_gameDir.empty();
        if (ok) Icon(g, IC_CHECK, ga.X + 30, ga.Y + 70, 16, kOk, 2.6f);
        Text(g, ok ? L"My Winter Car " + g_gameVer : std::wstring(T(L"My Winter Car introuvable", L"My Winter Car not found")), RectF(ga.X + (ok ? 44.0f : 20.0f), ga.Y + 58, ga.Width - 70, 24), 14, FontStyleBold, ok ? kInk : kRed, StringAlignmentNear);
        StringFormat sf; sf.SetTrimming(StringTrimmingEllipsisPath); sf.SetFormatFlags(StringFormatFlagsNoWrap); sf.SetLineAlignment(StringAlignmentCenter);
        FontFamily fam(L"Segoe UI"); Font f(&fam, 12, FontStyleRegular, UnitPixel); SolidBrush gb(kGrey);
        g.DrawString(g_gameDir.c_str(), -1, &f, RectF(ga.X + 20, ga.Y + 82, ga.Width - 40, 20), &sf, &gb);
        UiButton(g, RectF(ga.X + 20, ga.Y + 112, ga.Width - 40, 44), ok ? T(L"Changer de dossier", L"Change folder") : T(L"Choisir l'exe…", L"Choose exe…"), UI_SET_FOLDER, false, IC_FOLDER);
    }
    if (const Opt *ls = OptByKey("LancerSteam")) {   // (kSetSteamRow : ga.X + 20, ga.Y + 166)
        RectF row = kSetSteamRow;
        SetRowText(g, RectF(row.X, row.Y, row.Width - 70, row.Height), T(L"Lancer le jeu par Steam", L"Start the game through Steam"), T(L"Si MWCoop ne se charge pas depuis le lanceur", L"If MWCoop doesn't load from the launcher"));
        UiToggle(g, RectF(row.X + row.Width - 50, row.Y + 12, 50, 28), OptGet(*ls) != 0, g_uiHot == UI_SET_STEAMRUN);
        HotZone(row, UI_SET_STEAMRUN);
    }
    // Invite : envoi de l'hote
    RectF iv(rx, ga.Y + ga.Height + 16, cw, kMain.Y + kMain.Height - (ga.Y + ga.Height + 16));
    Glass(iv);
    Title(g, T(L"Chez un ami", L"At a friend's"), RectF(iv.X + 20, iv.Y + 16, 300, 24), 17, kInk);
    if (const Opt *ma = OptByKey("ModsAuto")) {
        RectF row(iv.X + 20, iv.Y + 52, iv.Width - 40, 60);
        SetRowText(g, RectF(row.X, row.Y, row.Width - 70, row.Height), T(L"Accepter l'envoi de l'hôte", L"Accept the host's files"), T(L"Ses mods, CD et images, copiés à part, sans demander", L"Their mods, CDs and images, copied apart, without asking"));
        UiToggle(g, RectF(row.X + row.Width - 50, row.Y + 16, 50, 28), OptGet(*ma) != 0, g_uiHot == UI_SET_MODSAUTO);
        HotZone(row, UI_SET_MODSAUTO);
    }
    Para(g, T(L"Tes propres mods et ta sauvegarde ne sont jamais touchés : tu joues dans un profil invité à part.", L"Your own mods and save are never touched: you play in a separate guest profile."),
         RectF(iv.X + 20, iv.Y + 120, iv.Width - 40, 40), 12, kGrey);
}

// ---- lancement, partie en cours
static void DrawLaunchPage(Graphics &g)
{
    RectF c(kMain.X + 170, kMain.Y + 120, 660, 360);
    Glass(c);
    int dots = (int)(g_time * 2.5f) % 4;
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
static void DrawSkins(Graphics &g);
static void DrawCar(Graphics &g);
static void DrawContent(Graphics &g);
static void DrawMods(Graphics &g);
static void DrawServer(Graphics &g);
static void DrawUI(Graphics &g)
{
    UpdateButtons();
    std::wstring status;
    int kind;
    EnterCriticalSection(&g_cs);
    status = g_status; kind = g_statusKind;
    LeaveCriticalSection(&g_cs);
    float prog = g_progress;
    DrawRail(g);
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
    case TAB_COOP: DrawSettings(g); break;
    case TAB_API: DrawApiPage(g); break;
    case TAB_GFX: DrawGfxPage(g); break;
    default: DrawHome(g); break;
    }
    DrawHeader(g);
    DrawFooter(g, status, kind, prog);
}

// Clic sur une zone (UI_*). Vrai si traite.
static void SteamGuideOpen();
static void LobbyOptsClick(int id);
static bool UiClick(int id)
{
    if (id == UI_NONE) return true;
    if (id >= UI_NAV && id < UI_NAV + TAB_COUNT) { int p = id - UI_NAV; if (PageEnabled(p)) GoPage(p); return true; }
    if (id == UI_SEG || id == UI_SEG + 1) {
        bool look = g_tab == TAB_SKIN || g_tab == TAB_CAR;
        int p = look ? (id == UI_SEG ? TAB_SKIN : TAB_CAR) : (id == UI_SEG ? TAB_CONTENT : TAB_MODS);
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
    case UI_SET_CHECK: if (!g_busy) StartUpdate(false); return true;
    case UI_SET_AUTO: {
        bool on = GetPrivateProfileIntW(L"Lanceur", L"MajAuto", 0, g_iniLauncher.c_str()) != 0;
        WritePrivateProfileStringW(L"Lanceur", L"MajAuto", on ? L"0" : L"1", g_iniLauncher.c_str());
        return true;
    }
    case UI_SET_DARK: case UI_SET_LIGHT:
        g_dark = id == UI_SET_DARK;
        WritePrivateProfileStringW(L"Lanceur", L"Theme", g_dark ? L"sombre" : L"clair", g_iniLauncher.c_str());
        return true;
    case UI_SET_FR: case UI_SET_EN:
        g_fr = id == UI_SET_FR;
        WritePrivateProfileStringW(L"Lanceur", L"Langue", g_fr ? L"fr" : L"en", g_iniLauncher.c_str());
        return true;
    case UI_SET_CLOSE: if (const Opt *o = OptByKey("FermerLanceur")) OptSet(*o, OptGet(*o) ? 0 : 1); return true;
    case UI_SET_STEAMRUN:
        if (const Opt *o = OptByKey("LancerSteam")) {
            OptSet(*o, OptGet(*o) ? 0 : 1);
            SetStatus(K_NORMAL, OptGet(*o) ? T(L"Le jeu sera lanc\u00E9 par Steam (comme son bouton JOUER)", L"The game will be started through Steam (like its PLAY button)") : T(L"Le jeu sera lanc\u00E9 par le lanceur", L"The game will be started by the launcher"));
        }
        return true;
    case UI_SET_MODSAUTO: if (const Opt *o = OptByKey("ModsAuto")) OptSet(*o, OptGet(*o) ? 0 : 1); return true;
    case UI_SET_GUIDE: TutoStart(true); return true;
    case UI_SET_STEAM: case UI_SET_IP: if ((id == UI_SET_STEAM) != g_steamNet && g_lobby == LB_NONE) OnButton(id == UI_SET_STEAM ? B_NETSTEAM : B_NETIP); return true;
    case UI_SET_PORTM: case UI_SET_PORTP: for (int i = 0; i < (int)g_opts.size(); i++) if (!strcmp(g_opts[i].key, "Port")) OptStep(i, id == UI_SET_PORTP ? 1 : -1); return true;
    case UI_SET_FOLDER: ChooseExe(); return true;
    case UI_SET_BUY: ShellExecuteW(g_wnd, L"open", kStoreUrl, NULL, NULL, SW_SHOWNORMAL); return true;
    case UI_SET_UNINST: Uninstall(); return true;
    }
    if (id >= UI_NOTE && id < UI_NOTE + 90) { g_noteSel = id - UI_NOTE; g_scroll[TAB_NOTES] = 0; return true; }
    if (id >= UI_LOOKPIN && id < UI_LOOKPIN + 4) { g_lookZone = id - UI_LOOKPIN; return true; }
    if (id == UI_LOOKROT || id == UI_LOOKROT + 1) { g_skinYaw = fmodf(g_skinYaw + (id == UI_LOOKROT ? 2.0f : -2.0f) + 16, 16.0f); g_skinIdleT = GetTickCount(); return true; }
    if (id == UI_LOOKRAND) { LookRandom(); return true; }
    if (id == UI_LOOKRESET) { LookReset(); return true; }
    if (id >= UI_LOBBY_GEAR && id <= UI_LOBBY_ME_CAR) { LobbyOptsClick(id); return true; }
    if (id >= UI_API_DOC && id <= UI_API_EX1) { ApiClick(id); return true; }
    if (id == UI_LOG_CLEAR) {   // second clic dans les 4 s : les parties rangees a la corbeille
        if (g_logDelKey == L"**" && GetTickCount() - g_logDelT < 4000) { g_logDelKey.clear(); LogsDeleteAll(); }
        else { g_logDelKey = L"**"; g_logDelT = GetTickCount(); SetStatus(K_WARN, T(L"Clique encore pour mettre toutes les parties rangées à la corbeille", L"Click again to move every stored session to the Recycle Bin")); }
        return true;
    }
    if (id >= UI_GFX_GROUP && id < UI_GFX_GROUP + 10) { GfxToggleGroup(id - UI_GFX_GROUP); return true; }
    if (id >= UI_GFX_PRESET && GfxClick(id)) return true;
    return false;
}
