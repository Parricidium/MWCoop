// ---------------------------------------------------------------- page GRAPHISMES - inclus par launcher.cpp
// Demande de JD (07/10), comme les GTA : les reglages graphiques dans le lanceur (et en jeu, menu F10). Ecrits dans
// [Graphismes] du mwcoop.ini du jeu (MWCoop\Gfx.cs les applique au chargement, et tout de suite si le jeu tourne) ;
// cle absente : le jeu decide ("Jeu" pour les reglages du moteur). Options du jeu (son menu) et reglages du moteur en
// plus ; prereglages Performance / Equilibre / Beau / Ultra en un clic.
struct GfxRow
{
    const char *key; const wchar_t *fr, *en;
    int def;                                   // valeur montree quand la cle est absente (-1 : "Jeu")
    std::vector<int> vals;                     // vide : interrupteur 0/1
    std::vector<const wchar_t *> lf, le;       // noms des valeurs (vals)
    int impact;                                // sur les images par seconde : 0 aucun, 1 leger, 2 moyen, 3 fort
    int group;                                 // 0 Image, 1 Ombres, 2 Distance et details, 3 Jeu et confort, 4 Lumieres, 5 Ecran
};
static const std::vector<GfxRow> &GfxRows()
{
    static std::vector<GfxRow> rows;
    if (!rows.empty()) return rows;
    rows = {
        { "Anticrenelage", L"Anticrénelage", L"Anti-aliasing", 1, {}, {}, {}, 1, 0 },
        { "HDR", L"HDR (lumière riche)", L"HDR (rich light)", 1, {}, {}, {}, 1, 0 },
        { "Bloom", L"Halo lumineux (bloom)", L"Bloom", 0, {}, {}, {}, 1, 0 },
        { "Contraste", L"Contraste renforcé", L"Contrast boost", 0, {}, {}, {}, 0, 0 },
        { "RayonsSoleil", L"Rayons du soleil", L"Sun shafts", 0, {}, {}, {}, 2, 0 },
        { "ImagesMax", L"Images par seconde max.", L"Max frames per second", -1, { -1, 30, 60, 75, 120, 144, 165, 240, 0 },
          { L"Jeu", L"30", L"60", L"75", L"120", L"144", L"165", L"240", L"Illimitées" }, { L"Game", L"30", L"60", L"75", L"120", L"144", L"165", L"240", L"Unlimited" }, 0, 0 },
        { "SyncVerticale", L"Synchro verticale", L"V-sync", -1, { -1, 0, 1 }, { L"Jeu", L"Non", L"Oui" }, { L"Game", L"Off", L"On" }, 0, 0 },
        { "OmbresSoleil", L"Ombres du soleil", L"Sun shadows", 1, {}, {}, {}, 3, 1 },
        { "OmbresLune", L"Ombres de la lune", L"Moon shadows", 0, {}, {}, {}, 2, 1 },
        { "OmbresMaisons", L"Ombres des maisons", L"House shadows", 1, {}, {}, {}, 2, 1 },
        { "OmbresDistance", L"Distance des ombres", L"Shadow distance", -1, { -1, 30, 60, 100, 150, 250, 400 },
          { L"Jeu", L"30 m", L"60 m", L"100 m", L"150 m", L"250 m", L"400 m" }, { L"Game", L"30 m", L"60 m", L"100 m", L"150 m", L"250 m", L"400 m" }, 3, 1 },
        { "OmbresCascades", L"Finesse des ombres", L"Shadow detail", -1, { -1, 1, 2, 4 }, { L"Jeu", L"Simple", L"Fine", L"Très fine" }, { L"Game", L"Basic", L"Fine", L"Very fine" }, 2, 1 },
        { "Distance", L"Distance d'affichage", L"Draw distance", 3000, { 500, 1000, 1500, 2000, 3000, 4500, 6000, 9000 },
          { L"500 m", L"1 km", L"1,5 km", L"2 km", L"3 km", L"4,5 km", L"6 km", L"9 km" }, { L"500 m", L"1 km", L"1.5 km", L"2 km", L"3 km", L"4.5 km", L"6 km", L"9 km" }, 3, 2 },
        { "Details", L"Niveau de détail", L"Level of detail", -1, { -1, 70, 100, 150, 200, 300 },
          { L"Jeu", L"Bas", L"Normal", L"Élevé", L"Très élevé", L"Max" }, { L"Game", L"Low", L"Normal", L"High", L"Very high", L"Max" }, 2, 2 },
        { "Textures", L"Textures", L"Textures", -1, { -1, 2, 1, 0 }, { L"Jeu", L"Quart", L"Moitié", L"Pleines" }, { L"Game", L"Quarter", L"Half", L"Full" }, 1, 2 },
        { "Filtrage", L"Filtrage des textures", L"Texture filtering", -1, { -1, 0, 1, 2 }, { L"Jeu", L"Non", L"Oui", L"Forcé" }, { L"Game", L"Off", L"On", L"Forced" }, 1, 2 },
        { "OmbresPhares", L"Ombres des phares", L"Headlight shadows", 0, { 0, 2, 4, 8 }, { L"Non", L"2 phares", L"4 phares", L"8 phares" }, { L"Off", L"2 lights", L"4 lights", L"8 lights" }, 3, 4 },
        { "OmbresLampadaires", L"Ombres des lampadaires", L"Street light shadows", 0, { 0, 1, 2, 4 }, { L"Non", L"1", L"2", L"4" }, { L"Off", L"1", L"2", L"4" }, 3, 4 },
        { "Halos", L"Halos lumineux (façon GTA)", L"Light coronas (GTA style)", 0, { 0, 1, 2 }, { L"Non", L"Discrets", L"Marqués" }, { L"Off", L"Subtle", L"Strong" }, 1, 4 },
        { "PorteePhares", L"Portée des phares", L"Headlight range", 0, { 0, 1 }, { L"Normale", L"Longue" }, { L"Normal", L"Long" }, 1, 4 },
        { "ChampVision", L"Champ de vision", L"Field of view", 60, { 50, 55, 60, 65, 70, 75, 80, 85, 90 },
          { L"50°", L"55°", L"60°", L"65°", L"70°", L"75°", L"80°", L"85°", L"90°" }, { L"50°", L"55°", L"60°", L"65°", L"70°", L"75°", L"80°", L"85°", L"90°" }, 0, 3 },
        { "Retroviseurs", L"Rétroviseurs", L"Mirrors", 1, { 0, 1, 2 }, { L"Coupés", L"Simples", L"Complets" }, { L"Off", L"Simple", L"Full" }, 2, 3 },
        { "Balancement", L"Tête qui balance à pied", L"Head bob on foot", 1, {}, {}, {}, 0, 3 },
        { "BalancementVoiture", L"Tête qui balance en voiture", L"Head bob in cars", 2, { 0, 1, 2 }, { L"Non", L"50 %", L"Oui" }, { L"Off", L"50 %", L"On" }, 0, 3 },
        { "CompteurImages", L"Compteur d'images", L"FPS counter", 0, {}, {}, {}, 0, 3 },
        { "IndicateurRapport", L"Rapport engagé à l'écran", L"Gear on screen", 1, {}, {}, {}, 0, 3 },
        { "PoussiereTrafic", L"Poussière de la circulation", L"Traffic dust", 1, {}, {}, {}, 1, 3 },
        { "TracesTrafic", L"Traces de pneus de la circulation", L"Traffic skid marks", 0, {}, {}, {}, 1, 3 },
    };
    // Ecran (comme les GTA, a la place de la petite fenetre Unity) : resolutions de l'ecran principal (Windows),
    // mode d'affichage, niveau de qualite du moteur. Appliques par le mod au demarrage du jeu (MWCoop\Gfx.cs).
    static std::vector<std::wstring> resLabels;
    std::vector<int> rv = { -1 };
    std::vector<std::pair<int, int>> modes;
    DEVMODEW dm = { sizeof(dm) };
    for (DWORD i = 0; EnumDisplaySettingsW(NULL, i, &dm); i++)
        if (dm.dmPelsWidth >= 800 && dm.dmPelsHeight >= 600) {
            std::pair<int, int> m((int)dm.dmPelsWidth, (int)dm.dmPelsHeight);
            if (std::find(modes.begin(), modes.end(), m) == modes.end()) modes.push_back(m);
        }
    std::sort(modes.begin(), modes.end());
    resLabels.clear();
    resLabels.reserve(modes.size() + 1);
    for (auto &m : modes) { rv.push_back(m.first * 10000 + m.second); resLabels.push_back(std::to_wstring(m.first) + L" × " + std::to_wstring(m.second)); }
    GfxRow res = { "Resolution", L"Résolution", L"Resolution", -1, rv, { L"Jeu" }, { L"Game" }, 0, 5 };
    for (auto &l : resLabels) { res.lf.push_back(l.c_str()); res.le.push_back(l.c_str()); }
    rows.insert(rows.begin(), {
        res,
        { "ModeEcran", L"Affichage", L"Display mode", -1, { -1, 0, 1, 2 }, { L"Jeu", L"Fenêtré", L"Plein écran", L"Sans bordure" }, { L"Game", L"Windowed", L"Fullscreen", L"Borderless" }, 0, 5 },
        { "QualiteUnity", L"Qualité du moteur", L"Engine quality", -1, { -1, 0, 1, 3, 5 }, { L"Jeu", L"Basse", L"Moyenne", L"Bonne", L"Golden Eye" }, { L"Game", L"Low", L"Medium", L"Good", L"Golden Eye" }, 3, 5 },
    });
    return rows;
}

// Prereglages : valeurs des options de qualite (le reste -- champ de vision, images, confort -- n'est pas touche).
struct GfxPreset { const wchar_t *fr, *en, *dFr, *dEn; std::vector<std::pair<const char *, int>> v; };
static const std::vector<GfxPreset> &GfxPresets()
{
    static std::vector<GfxPreset> p;
    if (!p.empty()) return p;
    auto mk = [](int aa, int hdr, int bloom, int shafts, int moon, int house, int sdist, int casc, int dist, int det, int tex, int filt, int mir, int dust, int skid, int hshad, int sshad, int halos) {
        return std::vector<std::pair<const char *, int>> { { "Anticrenelage", aa }, { "HDR", hdr }, { "Bloom", bloom }, { "RayonsSoleil", shafts }, { "OmbresSoleil", 1 },
            { "OmbresLune", moon }, { "OmbresMaisons", house }, { "OmbresDistance", sdist }, { "OmbresCascades", casc }, { "Distance", dist }, { "Details", det },
            { "Textures", tex }, { "Filtrage", filt }, { "Retroviseurs", mir }, { "PoussiereTrafic", dust }, { "TracesTrafic", skid },
            { "OmbresPhares", hshad }, { "OmbresLampadaires", sshad }, { "Halos", halos } };
    };
    p = {
        { L"Performance", L"Performance", L"Plus d'images", L"More frames", mk(0, 0, 0, 0, 0, 0, 30, 1, 1500, 70, 1, 0, 1, 0, 0, 0, 0, 0) },
        { L"Équilibré", L"Balanced", L"Le bon compromis", L"The sweet spot", mk(1, 1, 0, 0, 0, 1, 60, 2, 3000, 100, 0, 1, 1, 1, 0, 0, 0, 1) },
        { L"Beau", L"Pretty", L"Plus joli", L"Better looking", mk(1, 1, 1, 1, 1, 1, 150, 2, 4500, 150, 0, 1, 2, 1, 1, 2, 1, 1) },
        { L"Ultra", L"Ultra", L"Tout au max", L"Everything maxed", mk(1, 1, 1, 1, 1, 1, 400, 4, 9000, 300, 0, 2, 2, 1, 1, 4, 2, 2) },
    };
    return p;
}

static std::map<std::string, int> g_gfxVal;
static DWORD g_gfxReadT;
static void GfxLoad()
{
    g_gfxVal.clear();
    std::wstring ini = ModIni();
    for (const GfxRow &r : GfxRows()) {
        wchar_t b[32] = {};
        GetPrivateProfileStringW(L"Graphismes", Widen(r.key).c_str(), L"", b, 32, ini.c_str());
        g_gfxVal[r.key] = b[0] ? _wtoi(b) : r.def;
    }
    g_gfxReadT = GetTickCount();
}
static int GfxGet(const char *k) { auto it = g_gfxVal.find(k); return it != g_gfxVal.end() ? it->second : -1; }
static void GfxSet(const char *k, int v)
{
    g_gfxVal[k] = v;
    std::wstring s = std::to_wstring(v);
    WritePrivateProfileStringW(L"Graphismes", Widen(k).c_str(), v == -1 ? NULL : s.c_str(), ModIni().c_str());   // (-1 : la cle est retiree, le jeu decide)
}
static int GfxPresetOn()
{
    const auto &ps = GfxPresets();
    for (int i = 0; i < (int)ps.size(); i++) {
        bool all = true;
        for (auto &kv : ps[i].v) if (GfxGet(kv.first) != kv.second) { all = false; break; }
        if (all) return i;
    }
    return -1;
}

static const RectF kGfxTop(264, 88, 1000, 84), kGfxLeft(264, 184, 492, 560), kGfxRight(772, 184, 492, 560);
static const float kGfxRowH = 27;

static void DrawGfxRow(Graphics &g, const GfxRow &r, int idx, RectF row)
{
    int id = UI_GFX + idx * 3;
    bool hot = g_uiHot == id || g_uiHot == id + 1 || g_uiHot == id + 2;
    if (hot) { GraphicsPath p; RoundRect(p, RectF(row.X - 8, row.Y, row.Width + 16, row.Height), 10); SolidBrush hb(TH(card)); g.FillPath(&hb, &p); }
    const wchar_t *lab = g_fr ? r.fr : r.en;
    Text(g, lab, RectF(row.X, row.Y, row.Width - 190, row.Height), 13.5f, FontStyleRegular, kInk, StringAlignmentNear);
    if (r.impact >= 2) {   // impact sur les images : pastille apres le nom
        Graphics &mg = MeasureG();
        float w = MeasureW(mg, lab, 13.5f, FontStyleRegular);
        const wchar_t *il = r.impact >= 3 ? T(L"lourd", L"heavy") : T(L"moyen", L"medium");
        float pw = PillW(il, 9.5f);
        Color ic = r.impact >= 3 ? kRed : kWarn;
        Pill(g, RectF(row.X + w + 10, row.Y + (row.Height - 18) / 2, pw, 18), WithA(ic, 0.16f), il, ic, 9.5f);
    }
    int v = GfxGet(r.key);
    if (r.vals.empty()) {
        UiToggle(g, RectF(row.X + row.Width - 44, row.Y + (row.Height - 22) / 2, 44, 22), v == 1, hot);
        HotZone(row, id);
        return;
    }
    // ◀ valeur ▶
    int k = 0;
    for (int i = 0; i < (int)r.vals.size(); i++) if (r.vals[i] == v) k = i;
    RectF val(row.X + row.Width - 172, row.Y + 2, 172, row.Height - 4);
    RectF lft(val.X, val.Y, 30, val.Height), rgt(val.X + val.Width - 30, val.Y, 30, val.Height);
    GraphicsPath vp; RoundRect(vp, val, 10);
    SolidBrush vb(TH(btn2)); g.FillPath(&vb, &vp);
    Pen vpen(TH(fieldBorder), 1.0f); g.DrawPath(&vpen, &vp);
    bool canL = k > 0, canR = k + 1 < (int)r.vals.size();
    Icon(g, IC_CHEVL, lft.X + 15, lft.Y + lft.Height / 2, 14, WithA(g_uiHot == id + 1 ? kAcc : kInk, canL ? 1.0f : 0.25f), 2.2f);
    Icon(g, IC_CHEVR, rgt.X + 15, rgt.Y + rgt.Height / 2, 14, WithA(g_uiHot == id + 2 ? kAcc : kInk, canR ? 1.0f : 0.25f), 2.2f);
    bool game = r.vals[k] == -1;
    Text(g, g_fr ? r.lf[k] : r.le[k], RectF(lft.X + lft.Width, val.Y, val.Width - 60, val.Height), 12.5f, FontStyleBold, game ? kGrey : kInk);
    HotZone(row, UI_NONE);
    if (canL) HotZone(lft, id + 1);
    if (canR) HotZone(rgt, id + 2);
}

static void DrawGfxGroup(Graphics &g, int group, const wchar_t *fr, const wchar_t *en, float x, float &y, float w)
{
    SmallLabel(g, g_fr ? fr : en, RectF(x, y + 4, w, 20), WithA(kGrey, 0.9f));
    y += 26;
    const auto &rows = GfxRows();
    for (int i = 0; i < (int)rows.size(); i++) {
        if (rows[i].group != group) continue;
        DrawGfxRow(g, rows[i], i, RectF(x, y, w, kGfxRowH));
        y += kGfxRowH + 1;
    }
    y += 4;
}

static void DrawGfxPage(Graphics &g)
{
    if (GetTickCount() - g_gfxReadT > 2000) GfxLoad();   // (le jeu peut les avoir changes : son menu, F10)
    // prereglages
    RectF t = kGfxTop;
    Glass(t);
    Title(g, T(L"Qualité d'image", L"Image quality"), RectF(t.X + 24, t.Y + 14, 300, 26), 17, kInk);
    int on = GfxPresetOn();
    Para(g, on >= 0 ? T(L"Un clic règle tout, puis ajuste ligne par ligne.", L"One click sets everything, then fine-tune below.") : T(L"Réglage personnalisé : choisis un préréglage pour repartir.", L"Custom settings: pick a preset to start over."),
         RectF(t.X + 24, t.Y + 40, 300, 40), 12.5f, kGrey);
    const auto &ps = GfxPresets();
    float tw = 148, gap = 10, x0 = t.X + t.Width - 20 - ((int)ps.size() * tw + ((int)ps.size() - 1) * gap);
    for (int i = 0; i < (int)ps.size(); i++) {
        RectF r(x0 + i * (tw + gap), t.Y + 12, tw, t.Height - 24);
        bool sel = i == on, hot = g_uiHot == UI_GFX_PRESET + i;
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush fb(sel ? kAcc : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&fb, &p);
        if (!sel) { Pen pen(TH(fieldBorder), 1.0f); g.DrawPath(&pen, &p); }
        Color ink = sel ? kOnAcc : kInk;
        Text(g, g_fr ? ps[i].fr : ps[i].en, RectF(r.X + 14, r.Y + 7, r.Width - 20, 22), 14.5f, FontStyleBold, ink, StringAlignmentNear);
        Text(g, g_fr ? ps[i].dFr : ps[i].dEn, RectF(r.X + 14, r.Y + 29, r.Width - 20, 18), 11.5f, FontStyleRegular, sel ? WithA(kOnAcc, 0.8f) : kGrey, StringAlignmentNear);
        // barres de niveau (1 a 4)
        for (int b = 0; b <= 3; b++) {
            RectF br(r.X + r.Width - 14 - (3 - b) * 7, r.Y + 19 - b * 4, 4, 6 + b * 4);
            SolidBrush bb(b <= i ? (sel ? kOnAcc : kAcc) : WithA(ink, 0.18f)); g.FillRectangle(&bb, br);
        }
        HotZone(r, UI_GFX_PRESET + i);
    }
    // options
    Glass(kGfxLeft);
    Glass(kGfxRight);
    float y = kGfxLeft.Y + 10;
    DrawGfxGroup(g, 0, L"IMAGE", L"IMAGE", kGfxLeft.X + 24, y, kGfxLeft.Width - 48);
    DrawGfxGroup(g, 1, L"OMBRES", L"SHADOWS", kGfxLeft.X + 24, y, kGfxLeft.Width - 48);
    DrawGfxGroup(g, 4, L"LUMIÈRES", L"LIGHTS", kGfxLeft.X + 24, y, kGfxLeft.Width - 48);
    y = kGfxRight.Y + 10;
    DrawGfxGroup(g, 5, L"ÉCRAN", L"SCREEN", kGfxRight.X + 24, y, kGfxRight.Width - 48);
    DrawGfxGroup(g, 2, L"DISTANCE ET DÉTAILS", L"DISTANCE AND DETAIL", kGfxRight.X + 24, y, kGfxRight.Width - 48);
    DrawGfxGroup(g, 3, L"JEU ET CONFORT", L"GAME AND COMFORT", kGfxRight.X + 24, y, kGfxRight.Width - 48);
    Text(g, T(L"Pour toi seul · appliqué au lancement du jeu · en partie : menu F10 > Graphismes", L"Just for you · applied when the game starts · in game: F10 menu > Graphics"),
         RectF(kGfxRight.X + 24, kGfxRight.Y + kGfxRight.Height - 32, kGfxRight.Width - 48, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
}

static bool GfxClick(int id)
{
    if (id >= UI_GFX_PRESET && id < UI_GFX_PRESET + (int)GfxPresets().size()) {
        for (auto &kv : GfxPresets()[id - UI_GFX_PRESET].v) GfxSet(kv.first, kv.second);
        return true;
    }
    const auto &rows = GfxRows();
    if (id < UI_GFX || id >= UI_GFX + (int)rows.size() * 3) return false;
    const GfxRow &r = rows[(id - UI_GFX) / 3];
    int part = (id - UI_GFX) % 3, v = GfxGet(r.key);
    if (r.vals.empty()) { if (part == 0) GfxSet(r.key, v == 1 ? 0 : 1); return true; }
    int k = 0;
    for (int i = 0; i < (int)r.vals.size(); i++) if (r.vals[i] == v) k = i;
    if (part == 1 && k > 0) GfxSet(r.key, r.vals[k - 1]);
    if (part == 2 && k + 1 < (int)r.vals.size()) GfxSet(r.key, r.vals[k + 1]);
    return true;
}
