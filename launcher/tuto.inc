// ---------------------------------------------------------------- visite guidee du premier lancement - inclus par launcher.cpp
// Demande de JD (07/10), comme certains lanceurs : au premier lancement ([Lanceur] TutoVu absent), une suite de bulles.
// La fenetre s'assombrit sauf l'element montre (entoure), une bulle a fleche dit a quoi il sert ; SUIVANT (Entree,
// fleche droite), RETOUR (fleche gauche), PASSER (Echap). Les onglets montres s'ouvrent derriere la bulle. A revoir :
// COOP > Revoir le guide de demarrage.
static int g_tuto = -1, g_tutoHot;   // rang dans la visite (-1 : pas de visite) ; survol : 1 suivant, 2 passer, 3 retour
static int g_tutoTabWas = -1;

struct TutoStep { const wchar_t *tFr, *dFr, *tEn, *dEn; const char *since; };   // since : version ou l'element est apparu
static const TutoStep kTuto[] = {
    { L"Bienvenue dans MWCoop !", L"Une petite visite du lanceur, en quelques bulles. Tu peux la passer à tout moment (Échap) et la revoir depuis l'onglet COOP.",
      L"Welcome to MWCoop!", L"A quick tour of the launcher, in a few bubbles. You can skip it at any time (Esc) and see it again from the CO-OP tab.", "0.35.0" },
    { L"Ton pseudo", L"Le nom que les autres joueurs voient au-dessus de ton personnage et dans le salon.",
      L"Your nickname", L"The name the other players see above your character and in the lobby.", "0.35.0" },
    { L"Par où jouer", L"STEAM : le plus simple, tu invites tes amis Steam, sans port ni adresse. IP / VPN : par adresse (réseau local, Radmin, Hamachi…).",
      L"How to connect", L"STEAM: the easiest, you invite your Steam friends, no port or address. IP / VPN: by address (local network, Radmin, Hamachi…).", "0.35.0" },
    { L"HÉBERGER", L"Ouvre un salon : tes amis le rejoignent, chacun se met PRÊT, puis LANCER démarre le jeu de tout le monde. Dans le salon : clique sur ton personnage pour ta tenue, l'icône voiture pour sa couleur, l'engrenage pour les options.",
      L"HOST", L"Opens a lobby: your friends join it, everyone gets READY, then START launches everyone's game. In the lobby: click your character for your outfit, the car icon for its color, the gear for the options.", "0.35.0" },
    { L"REJOINDRE", L"Entre dans le salon d'un ami (par Steam : son invitation, ou son salon trouvé tout seul ; par IP : son adresse dans le champ au-dessus).",
      L"JOIN", L"Enters a friend's lobby (through Steam: their invite, or their lobby found by itself; by IP: their address in the field above).", "0.35.0" },
    { L"JOUER EN SOLO", L"Le jeu normal, avec MWCoop chargé mais sans réseau. Ta sauvegarde n'est jamais touchée par les parties des autres.",
      L"PLAY SOLO", L"The regular game, with MWCoop loaded but no network. Your save is never touched by other people's games.", "0.35.0" },
    { L"Onglet COOP", L"Les réglages : port, apparence de ton personnage, couleur de la voiture d'une nouvelle partie, lanceur ouvert pendant la partie…",
      L"CO-OP tab", L"The settings: port, your character's look, the car color for a new game, launcher kept open while playing…", "0.35.0" },
    { L"Onglet MODS", L"MSCLoader (expérimental) : l'installer en un clic, l'activer ou non, voir tes mods et régler leurs options (engrenage).",
      L"MODS tab", L"MSCLoader (experimental): install it in one click, turn it on or off, see your mods and set their options (gear).", "0.35.0" },
    { L"Onglet NOUVEAUTÉS", L"Ce qui change à chaque version. MWCoop se met à jour tout seul à chaque démarrage du lanceur.",
      L"UPDATES tab", L"What changes in each version. MWCoop updates itself every time the launcher starts.", "0.35.0" },
    { L"Journaux", L"En cas de souci, crée ici un zip à envoyer. Un « ! » rouge s'y affiche si la dernière partie s'est arrêtée brutalement.",
      L"Logs", L"If something goes wrong, make a zip to send from here. A red \"!\" shows on it if the last game stopped abruptly.", "0.35.0" },
    { L"Thème", L"Clair ou sombre, comme tu préfères.", L"Theme", L"Light or dark, as you like.", "0.35.0" },
    { L"C'est parti !", L"Bon jeu à plusieurs en Finlande. Et si quelque chose ne va pas : les journaux, et le GitHub du mod.",
      L"Let's go!", L"Have fun together in Finland. And if something goes wrong: the logs, and the mod's GitHub.", "0.35.0" },
};
static const int kTutoN = (int)(sizeof(kTuto) / sizeof(kTuto[0]));

// Element montre a l'etape i (rectangle vide : bulle au centre).
static RectF TutoTarget(int i)
{
    switch (i) {
    case 1: return g_fields[0].r;
    case 2: { RectF a = g_btn[B_NETIP].r, b = g_btn[B_NETSTEAM].r; return RectF(a.X, a.Y, b.X + b.Width - a.X, a.Height); }
    case 3: return g_btn[B_HOST].r;
    case 4: return g_btn[B_JOIN].r;
    case 5: return g_btn[B_SOLO].r;
    case 6: return g_tabR[TAB_COOP];
    case 7: return g_tabR[TAB_MODS];
    case 8: return g_tabR[TAB_NOTES];
    case 9: return g_btn[B_LOGS].r;
    case 10: return g_btn[B_THEME].r;
    }
    return RectF(0, 0, 0, 0);
}
static int TutoTab(int i) { return i == 6 ? TAB_COOP : i == 7 ? TAB_MODS : i == 8 ? TAB_NOTES : -1; }

// Visite complete (premier lancement, COOP > Revoir le guide), ou seulement les bulles des elements apparus depuis la
// derniere visite ([Lanceur] TutoVersion), apres une mise a jour : pour un nouveau bouton, ajouter une etape avec la
// version qui l'apporte.
static std::vector<int> g_tutoList;
static bool g_tutoAll;
static int VerCmp(const char *a, const char *b)
{
    int x[3] = {}, y[3] = {};
    sscanf_s(a, "%d.%d.%d", &x[0], &x[1], &x[2]);
    sscanf_s(b, "%d.%d.%d", &y[0], &y[1], &y[2]);
    for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i] < y[i] ? -1 : 1;
    return 0;
}
static int TutoCur() { return g_tuto >= 0 && g_tuto < (int)g_tutoList.size() ? g_tutoList[g_tuto] : 0; }

static void TutoGo(int i)
{
    if (i < 0) i = 0;
    if (i >= (int)g_tutoList.size()) {   // fini : vue jusqu'a cette version
        g_tuto = -1;
        g_tab = g_tutoTabWas;
        WritePrivateProfileStringW(L"Lanceur", L"TutoVu", L"1", g_iniLauncher.c_str());
        WritePrivateProfileStringW(L"Lanceur", L"TutoVersion", Widen(MyVersion()).c_str(), g_iniLauncher.c_str());
        return;
    }
    g_tuto = i;
    g_tutoHot = 0;
    int t = TutoTab(TutoCur());
    g_tab = t >= 0 ? t : g_tutoTabWas;
}
static void TutoStart(bool all)
{
    if (g_state != ST_IDLE) return;
    char seen[32] = "0.0.0";
    GetPrivateProfileStringA("Lanceur", "TutoVersion", "0.0.0", seen, sizeof(seen), Narrow(g_iniLauncher).c_str());
    g_tutoList.clear();
    for (int i = 0; i < kTutoN; i++) if (all || VerCmp(kTuto[i].since, seen) > 0) g_tutoList.push_back(i);
    if (!all && !g_tutoList.empty() && g_tutoList[0] == 0) g_tutoList.erase(g_tutoList.begin());   // (bienvenue : visite complete seulement)
    g_tutoAll = all;
    if (g_tutoList.empty()) { WritePrivateProfileStringW(L"Lanceur", L"TutoVersion", Widen(MyVersion()).c_str(), g_iniLauncher.c_str()); return; }
    g_tutoTabWas = g_tab == TAB_COOP ? -1 : g_tab;
    DrawerClose(true);
    g_focus = -1;
    TutoGo(0);
}
// Au demarrage : jamais vue -> visite complete ; deja vue -> les nouveautes depuis.
static void TutoAtStartup()
{
    if (GetPrivateProfileIntW(L"Lanceur", L"TutoVu", 0, g_iniLauncher.c_str()) == 0) TutoStart(true);
    else if (MyVersion() != "dev") TutoStart(false);
}

// Bulle : sous l'element (ou au-dessus s'il n'y a pas la place), dans la fenetre ; fleche vers l'element.
static RectF TutoBubble(RectF tg, bool *below)
{
    const float w = 320, h = 148;
    if (tg.Width <= 0) { *below = true; return RectF(500 - w / 2, 330 - h / 2, w, h); }
    float cx = tg.X + tg.Width / 2;
    float x = min(max(cx - w / 2, 32.0f), 968.0f - w);
    *below = tg.Y + tg.Height + 16 + h < 596;
    float y = *below ? tg.Y + tg.Height + 16 : tg.Y - 16 - h;
    return RectF(x, y, w, h);
}
static RectF TutoBtn(RectF b, int k)   // 1 suivant, 2 passer, 3 retour
{
    if (k == 1) return RectF(b.X + b.Width - 112, b.Y + b.Height - 40, 100, 28);
    if (k == 3) return RectF(b.X + b.Width - 196, b.Y + b.Height - 40, 76, 28);
    return RectF(b.X + 12, b.Y + b.Height - 40, 70, 28);
}
static int TutoHit(float x, float y)
{
    if (g_tuto < 0) return 0;
    bool below;
    RectF b = TutoBubble(TutoTarget(TutoCur()), &below);
    for (int k = 1; k <= 3; k++) if ((k != 3 || g_tuto > 0) && (k != 2 || g_tuto < (int)g_tutoList.size() - 1) && TutoBtn(b, k).Contains(x, y)) return k;
    return 0;
}
static void TutoClick(float x, float y)
{
    int k = TutoHit(x, y);
    if (k == 1) TutoGo(g_tuto + 1);
    else if (k == 3) TutoGo(g_tuto - 1);
    else if (k == 2) TutoGo((int)g_tutoList.size());
}
static bool TutoKey(WPARAM wp)
{
    if (g_tuto < 0) return false;
    if (wp == VK_ESCAPE) TutoGo((int)g_tutoList.size());
    else if (wp == VK_RETURN || wp == VK_RIGHT || wp == VK_SPACE) TutoGo(g_tuto + 1);
    else if (wp == VK_LEFT) TutoGo(g_tuto - 1);
    return true;
}

static void DrawTuto(Graphics &g)
{
    if (g_tuto < 0) return;
    int step = TutoCur();
    RectF tg = TutoTarget(step);
    // voile sur la fenetre, trou arrondi autour de l'element
    GraphicsPath veil;
    veil.SetFillMode(FillModeAlternate);
    RoundRect(veil, RectF(20, 60, 960, 540), 26);
    RectF hole = tg;
    if (tg.Width > 0) { hole.Inflate(6, 6); RoundRect(veil, hole, min(14.0f, hole.Height / 2)); }
    SolidBrush vb(Color(165, 4, 8, 18));
    g.FillPath(&vb, &veil);
    // le haut du logo depasse de la carte (fond, au-dessus de y = 60) : redessine assombri, transparence gardee
    if (Bitmap *bgi = (g_dark && g_bgDark) ? g_bgDark : g_bg) {
        ColorMatrix cm = { { { 0.35f, 0, 0, 0, 0 }, { 0, 0.35f, 0, 0, 0 }, { 0, 0, 0.35f, 0, 0 }, { 0, 0, 0, 1, 0 }, { 0, 0, 0, 0, 1 } } };
        ImageAttributes ia;
        ia.SetColorMatrix(&cm);
        float sh = bgi->GetHeight() * 60.0f / kImgH;
        g.DrawImage(bgi, RectF(0, 0, kImgW, 60), 0, 0, (REAL)bgi->GetWidth(), sh, UnitPixel, &ia);
    }
    if (tg.Width > 0) {
        GraphicsPath hp; RoundRect(hp, hole, min(14.0f, hole.Height / 2));
        Pen ring(kAcc, 2.4f); g.DrawPath(&ring, &hp);
    }
    bool below;
    RectF b = TutoBubble(tg, &below);
    { GraphicsPath sp; RoundRect(sp, RectF(b.X + 2, b.Y + 5, b.Width, b.Height), 14); SolidBrush sb(Color(90, 0, 0, 0)); g.FillPath(&sb, &sp); }
    GraphicsPath bp; RoundRect(bp, b, 14);
    SolidBrush bb(TH(card)); g.FillPath(&bb, &bp);
    Pen bpen(kAcc, 1.5f); g.DrawPath(&bpen, &bp);
    if (tg.Width > 0) {   // fleche vers l'element
        float ax = min(max(tg.X + tg.Width / 2, b.X + 24), b.X + b.Width - 24);
        PointF tri[3];
        if (below) { tri[0] = PointF(ax - 9, b.Y + 1); tri[1] = PointF(ax + 9, b.Y + 1); tri[2] = PointF(ax, b.Y - 10); }
        else { tri[0] = PointF(ax - 9, b.Y + b.Height - 1); tri[1] = PointF(ax + 9, b.Y + b.Height - 1); tri[2] = PointF(ax, b.Y + b.Height + 10); }
        g.FillPolygon(&bb, tri, 3);
        g.DrawLine(&bpen, tri[0], tri[2]);
        g.DrawLine(&bpen, tri[1], tri[2]);
    }
    const TutoStep &st = kTuto[step];
    wchar_t num[16];
    swprintf_s(num, L"%d / %d", g_tuto + 1, (int)g_tutoList.size());
    Text(g, num, RectF(b.X + b.Width - 74, b.Y + 10, 62, 18), 10.5f, FontStyleBold, kGrey, StringAlignmentFar);
    float tx = b.X + 14;
    if (!g_tutoAll) {   // apres une mise a jour : etiquette NOUVEAU
        RectF nb(tx, b.Y + 11, 64, 18);
        GraphicsPath np; RoundRect(np, nb, 9);
        SolidBrush nbr(kRed); g.FillPath(&nbr, &np);
        Text(g, T(L"NOUVEAU", L"NEW"), nb, 9.5f, FontStyleBold, Color(255, 255, 255, 255));
        tx += 72;
    }
    Text(g, g_fr ? st.tFr : st.tEn, RectF(tx, b.Y + 8, b.X + b.Width - 90 - tx, 24), 15, FontStyleBold, kAcc, StringAlignmentNear);
    Para(g, g_fr ? st.dFr : st.dEn, RectF(b.X + 14, b.Y + 36, b.Width - 28, b.Height - 82), 12, kInk);
    for (int k = 1; k <= 3; k++) {
        if (k == 3 && g_tuto == 0) continue;
        if (k == 2 && g_tuto == (int)g_tutoList.size() - 1) continue;   // (derniere bulle : rien a passer)
        RectF r = TutoBtn(b, k);
        bool hot = g_tutoHot == k;
        GraphicsPath p; RoundRect(p, r, r.Height / 2);
        if (k == 1) { LinearGradientBrush lg(r, Mix(kAcc, Color(255, 255, 255, 255), hot ? 0.15f : 0.0f), kAcc2, LinearGradientModeHorizontal); g.FillPath(&lg, &p); }
        else if (hot) { SolidBrush hb(WithA(kAcc, 0.18f)); g.FillPath(&hb, &p); }
        const wchar_t *lab = k == 1 ? (g_tuto == (int)g_tutoList.size() - 1 ? T(L"TERMINER", L"FINISH") : T(L"SUIVANT", L"NEXT")) : k == 2 ? T(L"Passer", L"Skip") : T(L"Retour", L"Back");
        Text(g, lab, r, 11.5f, FontStyleBold, k == 1 ? kOnAcc : hot ? kAcc : kGrey);
    }
}
