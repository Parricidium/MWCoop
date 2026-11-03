// ---- RallyWorld (demande de JD, 03/11 : « un onglet pour presenter RallyWorld », maquette validee) : la communaute
// RallyWorld / Gravel (voitures, speciales et serveurs de rallye et de tout-terrain pour Assetto Corsa, gratuits) qui nous
// laisse utiliser ses voitures (alex_tor, 02/11 : gratuit seulement, respecter la 3D et la physique). Page de la barre,
// sous Addons, icone coeur ; leur site, leur Discord, leurs dons (rien pour MWCoop). Pastille NEW tant que la page n'a pas
// ete ouverte ([Lanceur] RallyWorldVu).
static const wchar_t *kRwSite = L"https://gravelassetto.wixsite.com/gravel";
static const wchar_t *kRwDiscord = L"https://discord.com/invite/k7mqHCnX";
static const wchar_t *kRwDon = L"https://www.paypal.com/paypalme/AlejandroA935";
static const Color kRwRed(255, 0xE3, 0x26, 0x2F), kRwGold(255, 0xFF, 0xCF, 0x4A), kRwBlurple(255, 0x58, 0x65, 0xF2);
static float g_rwContentH;
static int g_rwSeen = -1;   // (-1 : pas encore lu)
static bool g_rwFrozen;     // (/capture : l'etat du joueur n'est pas touche)
static bool RallySeen()
{
    if (g_rwSeen < 0) g_rwSeen = GetPrivateProfileIntW(L"Lanceur", L"RallyWorldVu", 0, g_iniLauncher.c_str()) ? 1 : 0;
    return g_rwSeen == 1;
}
static void RallyMarkSeen()
{
    if (RallySeen() || g_rwFrozen) return;
    g_rwSeen = 1;
    WritePrivateProfileStringW(L"Lanceur", L"RallyWorldVu", L"1", g_iniLauncher.c_str());
}
static RectF RallyArea() { return RectF(g_pageR.X, 88, g_pageR.Width, 656); }
static float RallyMaxScroll() { return max(0.0f, g_rwContentH - RallyArea().Height); }

// Hauteur d'un paragraphe (Para) coupe aux mots dans la largeur w.
static float RwParaH(const std::wstring &s, float w, float px)
{
    Font &font = *FontOf(L"Segoe UI", px, FontStyleRegular);
    StringFormat sf;
    RectF out;
    MeasureG().MeasureString(s.c_str(), -1, &font, RectF(0, 0, w, 4000), &sf, &out);
    return ceilf(out.Height) + 2;
}

// Bouton aux couleurs de la page (site, Discord, don) : libelle en gras, petit texte apres ; zone de clic dans la page visible.
static void RwButton(Graphics &g, RectF r, const std::wstring &lab, const std::wstring &extra, Color fill, Color ink, int icon, int id, RectF view)
{
    bool hot = g_uiHot == id;
    GraphicsPath p; RoundRect(p, r, 12);
    SolidBrush b(fill); g.FillPath(&b, &p);
    if (hot) { SolidBrush hb(Color(46, 255, 255, 255)); g.FillPath(&hb, &p); }
    float iw = icon >= 0 ? 24.0f : 0.0f, lw = MeasureW(MeasureG(), lab, 13.5f, FontStyleBold), sw = extra.empty() ? 0 : MeasureW(MeasureG(), extra, 11.5f, FontStyleRegular) + 8;
    float x = r.X + (r.Width - iw - lw - sw) / 2;
    if (icon >= 0) Icon(g, icon, x + 9, r.Y + r.Height / 2, 16, ink, 2.2f);
    Text(g, lab, RectF(x + iw, r.Y, lw + 2, r.Height), 13.5f, FontStyleBold, ink, StringAlignmentNear);
    if (!extra.empty()) Text(g, extra, RectF(x + iw + lw + 8, r.Y + 1, sw, r.Height), 11.5f, FontStyleRegular, WithA(ink, 0.75f), StringAlignmentNear);
    RectF z; if (RectF::Intersect(z, r, view) && z.Height > 0) HotZone(z, id);
}
static float RwChip(Graphics &g, float x, float y, const std::wstring &s, Color fill, Color ink)
{
    float w = PillW(s, 10.5f);
    Pill(g, RectF(x, y, w, 22), fill, s, ink, 10.5f);
    return w + 6;
}
static void RwBox(Graphics &g, RectF r, Color fill, float rad = 14) { GraphicsPath p; RoundRect(p, r, rad); SolidBrush b(fill); g.FillPath(&b, &p); }
static Color RwCard() { return g_newUi ? g_soft.bgIn : TH(card); }   // (grande carte : creux de la nouvelle interface, verre de l'ancienne)
static Color RwInner() { return TH(cardSel); }                       // (bloc dans une carte)

static bool g_rwBottom;   // (/capture rallyworld-bas)
static void DrawRallyPage(Graphics &g)
{
    if (g_rwBottom) { g_rwBottom = false; Bitmap tb(8, 8); Graphics tg(&tb); DrawRallyPage(tg); g_scroll[TAB_RALLY] = RallyMaxScroll(); }
    RallyMarkSeen();
    RectF c = RallyArea();
    Glass(c);
    RectF view(c.X + 2, c.Y + 2, c.Width - 4, c.Height - 4);
    float sc = g_scroll[TAB_RALLY] = min(g_scroll[TAB_RALLY], RallyMaxScroll());
    GraphicsState st = g.Save();
    { GraphicsPath cp; RoundRect(cp, view, 20); g.SetClip(&cp, CombineModeIntersect); }
    const float pad = 20, x0 = c.X + pad, W = c.Width - 2 * pad;
    float y = c.Y + pad - sc;
    const Color white(255, 255, 255, 255);

    // bandeau : rouge et noir (leurs couleurs), embleme, titre, trois boutons
    {
        RectF h(x0, y, W, 214);
        GraphicsPath hp; RoundRect(hp, h, 16);
        LinearGradientBrush hb(h, kRwRed, Color(255, 0x16, 0x0A, 0x0B), 20.0f);
        Color cols[3] = { kRwRed, Color(255, 0xB5, 0x12, 0x1B), Color(255, 0x16, 0x0A, 0x0B) };
        REAL pos[3] = { 0.0f, 0.5f, 1.0f };
        hb.SetInterpolationColors(cols, pos, 3);
        g.FillPath(&hb, &hp);
        {   // fines rayures en biais
            GraphicsState s2 = g.Save(); g.SetClip(&hp, CombineModeIntersect);
            Pen sp(Color(10, 255, 255, 255), 2.0f);
            for (float sx = h.X - h.Height; sx < h.X + h.Width; sx += 24) g.DrawLine(&sp, PointF(sx, h.Y + h.Height), PointF(sx + h.Height * 0.47f, h.Y));
            g.Restore(s2);
        }
        // embleme (boussole Gravel)
        float ex = h.X + 34 + 64, ey = h.Y + h.Height / 2 + 6;
        SolidBrush eb(Color(255, 17, 17, 17)); g.FillEllipse(&eb, ex - 64, ey - 64, 128.0f, 128.0f);
        Pen ep(white, 5.0f); g.DrawEllipse(&ep, ex - 64, ey - 64, 128.0f, 128.0f);
        Text(g, L"N", RectF(ex - 20, ey - 94, 40, 24), 15, FontStyleBold, white);
        {
            GraphicsState s3 = g.Save();
            g.TranslateTransform(ex, ey); g.RotateTransform(-6);
            RectF tag(-50, -17, 100, 34);
            RwBox(g, tag, kRwRed, 6);
            Text(g, L"GRAVEL", tag, 17, FontStyleBold, white);
            g.Restore(s3);
        }
        float tx = h.X + 196, tw = h.X + h.Width - 24 - tx;
        Text(g, T(L"COMMUNAUTÉ PARTENAIRE", L"PARTNER COMMUNITY"), RectF(tx, h.Y + 26, tw, 18), 11.5f, FontStyleBold, Color(255, 0xFF, 0xD7, 0xD9), StringAlignmentNear);
        float t1 = MeasureW(MeasureG(), L"RallyWorld", 32, FontStyleBold);
        Text(g, L"RallyWorld", RectF(tx, h.Y + 44, t1 + 8, 46), 32, FontStyleBold, white, StringAlignmentNear);
        {
            std::wstring s = L"× Gravel";
            float t2 = MeasureW(MeasureG(), s, 28, FontStyleBold);
            RectF gr(tx + t1 + 14, h.Y + 48, t2 + 22, 38);
            RwBox(g, gr, white, 7);
            Text(g, s, gr, 28, FontStyleBold, Color(255, 17, 17, 17));
        }
        // (pastille « gratuit » en haut a droite, si la place le permet)
        std::wstring fr = T(L"100 % gratuit · fait par des passionnés", L"100% free · made by enthusiasts");
        float fw = PillW(fr, 11);
        if (tx + t1 + 200 + fw < h.X + h.Width - 20) {
            RectF fp(h.X + h.Width - 20 - fw, h.Y + 18, fw, 26);
            RwBox(g, fp, Color(100, 0, 0, 0), 13);
            Text(g, fr, fp, 11, FontStyleBold, white);
        }
        Para(g, T(L"Le monde du rallye et du tout-terrain pour Assetto Corsa : des voitures, des spéciales et des serveurs, faits par une équipe de passionnés et offerts à tous.",
                  L"The world of rally and off-road for Assetto Corsa: cars, stages and servers, made by a team of enthusiasts and free for everyone."),
             RectF(tx, h.Y + 94, min(tw, 640.0f), 48), 14, Color(255, 0xFF, 0xE9, 0xEA));
        float bx = tx, by = h.Y + h.Height - 24 - 42;
        auto btnW = [&](const std::wstring &l, const std::wstring &s, bool ic) { return MeasureW(MeasureG(), l, 13.5f, FontStyleBold) + (s.empty() ? 0 : MeasureW(MeasureG(), s, 11.5f, FontStyleRegular) + 8) + (ic ? 24 : 0) + 36; };
        std::wstring l1 = T(L"Leur site", L"Their website"), l2 = T(L"Rejoindre leur Discord", L"Join their Discord"), l3 = T(L"Les soutenir", L"Support them");
        float w1 = btnW(l1, L"", true), w2 = btnW(l2, L"", true), w3 = btnW(l3, L"PayPal", true);
        RwButton(g, RectF(bx, by, w1, 42), l1, L"", white, Color(255, 0xB5, 0x12, 0x1B), IC_GLOBE, UI_RALLY_SITE, view); bx += w1 + 10;
        RwButton(g, RectF(bx, by, w2, 42), l2, L"", kRwBlurple, white, IC_DISCORD, UI_RALLY_DISCORD, view); bx += w2 + 10;
        RwButton(g, RectF(bx, by, w3, 42), l3, L"PayPal", kRwGold, Color(255, 0x3A, 0x26, 0x00), IC_HEART, UI_RALLY_DON, view);
        y += h.Height + 16;
    }

    // deux cartes : qui sont-ils, ce qu'ils font
    {
        float gap = 16, wl = floorf((W - gap) * 0.52f), wr = W - gap - wl;
        RectF L(x0, y, wl, 0), R(x0 + wl + gap, y, wr, 0);
        std::wstring p1 = T(L"RallyWorld et Gravel, c'est une communauté qui modélise et règle elle-même ses voitures de rallye et de tout-terrain pour Assetto Corsa : la 3D, mais aussi la physique, ajustée à la main. Tout est gratuit : ils organisent des épreuves en ligne ouvertes à tous et paient eux-mêmes leurs serveurs.",
                            L"RallyWorld and Gravel are a community that models and tunes its own rally and off-road cars for Assetto Corsa: the 3D, and the physics too, adjusted by hand. Everything is free: they run online events open to everyone and pay for their servers themselves.");
        std::wstring p2 = T(L"Ils nous ont autorisés à utiliser leurs voitures dans MWCoop, à une condition simple : respecter leur travail et rester gratuit. Merci à eux !",
                            L"They allowed us to use their cars in MWCoop, on one simple condition: respect their work and stay free. Thank you!");
        float h1 = RwParaH(p1, wl - 44, 13), h2 = RwParaH(p2, wl - 44, 13);
        float lh = 22 + 26 + 30 + h1 + 8 + h2 + 14 + 54 + 22;
        // (droite : quatre blocs en deux rangees)
        struct Act { int ic; const wchar_t *tFr, *tEn, *dFr, *dEn; std::vector<const wchar_t *> chips; bool red; };
        const Act acts[4] = {
            { IC_CAR, L"Voitures", L"Cars", L"Rallye, buggys, karts, UTV, trophy trucks : un catalogue rangé par classes.", L"Rally, buggies, karts, UTVs, trophy trucks: a catalogue sorted by class.",
              { L"CT1 karts", L"CT2 trucks", L"CT3 UTV", L"CT4 · CT5", L"CJR · CC4 · CTE" }, true },
            { IC_FLAG, L"Spéciales", L"Stages", L"Des tracés de terre et de gravier pour tous les styles.", L"Dirt and gravel layouts for every style.", { L"Rally", L"ORR", L"Raid" }, false },
            { IC_GLOBE, L"Serveurs & épreuves", L"Servers & events", L"Deux serveurs en ligne et des épreuves gratuites, à l'heure officielle de la communauté.", L"Two online servers and free events, on the community's official time.", {}, false },
            { IC_TOOLS, L"Pilotes & textures", L"Drivers & textures", L"Des pilotes adaptés à chaque classe (rallye, kart, baja, UTV) et une texture de poussière commune à leurs voitures.", L"Drivers suited to each class (rally, kart, baja, UTV) and a dirt texture shared by their cars.", {}, false } };
        float aw = floorf((wr - 44 - 10) / 2);
        float ah[2] = { 0, 0 };
        for (int i = 0; i < 4; i++) {
            float hh = 12 + 26 + RwParaH(T(acts[i].dFr, acts[i].dEn), aw - 24, 12) + 12;
            if (!acts[i].chips.empty()) {   // (pastilles : autant de lignes que necessaire)
                float cx = 0, rows = 1;
                for (auto s : acts[i].chips) { float w = PillW(s, 10.5f) + 6; if (cx + w > aw - 24 && cx > 0) { rows++; cx = 0; } cx += w; }
                hh += rows * 28;
            }
            ah[i / 2] = max(ah[i / 2], hh);
        }
        float rh = 22 + 26 + 30 + ah[0] + 10 + ah[1] + 22;
        float ch = max(lh, rh);
        L.Height = R.Height = ch;
        RwBox(g, L, RwCard(), 16); RwBox(g, R, RwCard(), 16);
        // gauche
        float ly = L.Y + 22;
        Title(g, T(L"Qui sont-ils ?", L"Who are they?"), RectF(L.X + 22, ly, L.Width - 44, 26), 18, kInk); ly += 26;
        Text(g, T(L"Une petite équipe hispanophone, des années de travail", L"A small Spanish-speaking team, years of work"), RectF(L.X + 22, ly + 2, L.Width - 44, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear); ly += 30;
        Para(g, p1, RectF(L.X + 22, ly, L.Width - 44, h1), 13, kInk); ly += h1 + 8;
        Para(g, p2, RectF(L.X + 22, ly, L.Width - 44, h2), 13, kInk); ly += h2 + 14;
        {
            struct M { const wchar_t *n, *fr, *en; } team[3] = { { L"Yisus", L"fondateur", L"founder" }, { L"Mario", L"fondateur", L"founder" }, { L"alex_tor", L"physique", L"physics" } };
            float mw = floorf((L.Width - 44 - 20) / 3);
            for (int i = 0; i < 3; i++) {
                RectF m(L.X + 22 + i * (mw + 10), ly, mw, 54);
                RwBox(g, m, RwInner(), 12);
                SolidBrush ab(kRwRed); g.FillEllipse(&ab, m.X + 10, m.Y + 9, 36.0f, 36.0f);
                Text(g, std::wstring(1, (wchar_t)towupper(team[i].n[0])), RectF(m.X + 10, m.Y + 9, 36, 36), 15, FontStyleBold, white);
                Text(g, team[i].n, RectF(m.X + 54, m.Y + 8, m.Width - 60, 20), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
                Text(g, T(team[i].fr, team[i].en), RectF(m.X + 54, m.Y + 27, m.Width - 60, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            }
        }
        // droite
        float ry = R.Y + 22;
        Title(g, T(L"Ce qu'ils font", L"What they do"), RectF(R.X + 22, ry, R.Width - 44, 26), 18, kInk); ry += 26;
        Text(g, T(L"Tout se télécharge sur leur site et leur Discord", L"Everything is downloaded from their website and their Discord"), RectF(R.X + 22, ry + 2, R.Width - 44, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear); ry += 30;
        for (int i = 0; i < 4; i++) {
            RectF a(R.X + 22 + (i % 2) * (aw + 10), ry + (i / 2) * (ah[0] + 10), aw, ah[i / 2]);
            RwBox(g, a, RwInner(), 12);
            RwBox(g, RectF(a.X + 12, a.Y + 12, 24, 24), WithA(kRwRed, 0.18f), 7);
            Icon(g, acts[i].ic, a.X + 24, a.Y + 24, 15, kRwRed, 2.1f);
            Text(g, T(acts[i].tFr, acts[i].tEn), RectF(a.X + 44, a.Y + 12, a.Width - 52, 24), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
            float dh = RwParaH(T(acts[i].dFr, acts[i].dEn), aw - 24, 12);
            Para(g, T(acts[i].dFr, acts[i].dEn), RectF(a.X + 12, a.Y + 42, a.Width - 24, dh), 12, kGrey);
            float cx = a.X + 12, cy = a.Y + 42 + dh + 4;
            for (auto s : acts[i].chips) {
                float w = PillW(s, 10.5f) + 6;
                if (cx + w > a.X + a.Width - 12 && cx > a.X + 12) { cx = a.X + 12; cy += 28; }
                cx += RwChip(g, cx, cy, s, acts[i].red ? WithA(kRwRed, 0.2f) : RwCard(), acts[i].red ? Color(255, 0xFF, 0x7A, 0x80) : kInk);
            }
        }
        y += ch + 16;
    }

    // leurs voitures dans MWCoop (Addons, apres le test d'alex_tor)
    {
        RectF k(x0, y, W, 158);
        RwBox(g, k, RwCard(), 16);
        Title(g, T(L"Leurs voitures dans MWCoop", L"Their cars in MWCoop"), RectF(k.X + 22, k.Y + 20, k.Width - 44, 26), 18, kInk);
        Text(g, T(L"Portées dans My Winter Car et My Summer Car, avec leur accord · ta couleur et ta livrée, comme les autres addons", L"Ported to My Winter Car and My Summer Car, with their agreement · your colour and your livery, like the other add-ons"),
             RectF(k.X + 22, k.Y + 46, k.Width - 44, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        struct C { const wchar_t *n, *fr, *en; } cars[2] = { { L"Subaru Impreza 555", L"remplacera l'Imprezza 95 · Addons", L"will replace the Imprezza 95 · Add-ons" },
                                                          { L"Peugeot 205 T16 Evo 2", L"remplacera la 205 Ebo 2 · Addons", L"will replace the 205 Ebo 2 · Add-ons" } };
        float cw = floorf((k.Width - 44 - 12) / 2);
        for (int i = 0; i < 2; i++) {
            RectF t(k.X + 22 + i * (cw + 12), k.Y + 78, cw, 60);
            RwBox(g, t, RwInner(), 12);
            RwBox(g, RectF(t.X + 12, t.Y + 10, 40, 40), WithA(kRwRed, 0.18f), 10);
            Icon(g, IC_CAR, t.X + 32, t.Y + 30, 22, kRwRed, 2.0f);
            float nw = MeasureW(MeasureG(), cars[i].n, 14, FontStyleBold);
            Text(g, cars[i].n, RectF(t.X + 64, t.Y + 9, nw + 4, 22), 14, FontStyleBold, kInk, StringAlignmentNear);
            std::wstring soon = T(L"BIENTÔT", L"SOON");
            float sw = PillW(soon, 9.5f) - 4;
            Pill(g, RectF(t.X + 64 + nw + 10, t.Y + 11, sw, 18), kAcc, soon, kOnAcc, 9.5f);
            Text(g, T(cars[i].fr, cars[i].en), RectF(t.X + 64, t.Y + 31, t.Width - 76, 20), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        }
        y += k.Height + 16;
    }

    // les soutenir : dons pour eux, rien pour MWCoop
    {
        std::wstring lab = T(L"Faire un don", L"Donate");
        float bw = MeasureW(MeasureG(), lab, 13.5f, FontStyleBold) + MeasureW(MeasureG(), L"paypal.me/AlejandroA935", 11.5f, FontStyleRegular) + 8 + 24 + 36;
        std::wstring p = T(L"Les dons qu'ils reçoivent servent à louer leurs serveurs et à organiser des épreuves gratuites. Ton don va directement à eux : MWCoop ne touche rien.",
                           L"The donations they receive pay for their servers and for free events. Your donation goes straight to them: MWCoop gets nothing.");
        float tw = W - 22 - 64 - 18 - bw - 22 - 18;
        float ph = RwParaH(p, tw, 13);
        RectF s(x0, y, W, max(104.0f, 20 + 26 + ph + 20));
        RwBox(g, s, WithA(kRwGold, 0.12f), 16);
        { GraphicsPath sp; RoundRect(sp, s, 16); Pen pp(WithA(kRwGold, 0.4f), 1.0f); g.DrawPath(&pp, &sp); }
        RectF hb(s.X + 22, s.Y + (s.Height - 64) / 2, 64, 64);
        RwBox(g, hb, kRwGold, 18);
        Icon(g, IC_HEART, hb.X + 32, hb.Y + 33, 32, Color(255, 0x3A, 0x26, 0x00), 2.6f);
        float tx = hb.X + 64 + 18;
        Title(g, T(L"Les soutenir", L"Support them"), RectF(tx, s.Y + 20, tw, 26), 18, kInk);
        Para(g, p, RectF(tx, s.Y + 48, tw, ph), 13, kInk);
        RwButton(g, RectF(s.X + s.Width - 22 - bw, s.Y + (s.Height - 44) / 2, bw, 44), lab, L"paypal.me/AlejandroA935", kRwGold, Color(255, 0x3A, 0x26, 0x00), IC_HEART, UI_RALLY_DON2, view);
        y += s.Height + 16;
    }

    // leur regle : gratuit, jamais revendu
    {
        std::wstring p = T(L"Leur règle : tout leur contenu est gratuit et ne doit jamais être revendu. Les voitures RallyWorld dans MWCoop restent gratuites, pour tout le monde.",
                           L"Their rule: all their content is free and must never be sold. The RallyWorld cars in MWCoop stay free, for everyone.");
        float ph = RwParaH(p, W - 76, 12.5f);
        RectF r(x0, y, W, max(52.0f, ph + 24));
        RwBox(g, r, RwCard(), 12);
        Pen np(kRwRed, 2.6f); g.DrawEllipse(&np, r.X + 16, r.Y + r.Height / 2 - 14, 28.0f, 28.0f);
        Text(g, L"€", RectF(r.X + 16, r.Y + r.Height / 2 - 14, 28, 28), 13, FontStyleBold, kRwRed);
        Para(g, p, RectF(r.X + 58, r.Y + (r.Height - ph) / 2, W - 76, ph), 12.5f, kGrey);
        y += r.Height + pad;
    }
    g_rwContentH = y + sc - c.Y;
    g.Restore(st);
    if (RallyMaxScroll() > 0) {   // barre de defilement fine a droite
        float vh = c.Height - 24, th = max(30.0f, vh * c.Height / g_rwContentH), ty = c.Y + 12 + (vh - th) * sc / RallyMaxScroll();
        RwBox(g, RectF(c.X + c.Width - 7, ty, 3, th), WithA(kAcc, 0.5f), 1.5f);
    }
}

static bool RallyClick(int id)
{
    switch (id) {
    case UI_RALLY_SITE: ShellOpen(kRwSite); return true;
    case UI_RALLY_DISCORD: ShellOpen(kRwDiscord); return true;
    case UI_RALLY_DON: case UI_RALLY_DON2: ShellOpen(kRwDon); return true;
    }
    return false;
}
