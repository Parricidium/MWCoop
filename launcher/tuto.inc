// ---------------------------------------------------------------- visite guidee du premier lancement - inclus par launcher.cpp
// Demande de JD (07/10), comme certains lanceurs : au premier lancement ([Lanceur] TutoVu absent), une suite de bulles.
// La fenetre s'assombrit sauf l'element montre (entoure), une bulle a fleche dit a quoi il sert ; SUIVANT (Entree,
// fleche droite), RETOUR (fleche gauche), PASSER (Echap). Les onglets montres s'ouvrent derriere la bulle. A revoir :
// COOP > Revoir le guide de demarrage.
static int g_tuto = -1, g_tutoHot;   // rang dans la visite (-1 : pas de visite) ; survol : 1 suivant, 2 passer, 3 retour
static int g_tutoTabWas = -1;

struct TutoStep { const wchar_t *tFr, *dFr, *tEn, *dEn; const char *since; };   // since : version ou l'element est apparu
static const TutoStep kTuto[] = {
    { L"Bienvenue dans MWCoop !", L"Un nouveau lanceur, plus grand. Une petite visite en quelques bulles : tu peux la passer à tout moment (Échap) et la revoir depuis Réglages.",
      L"Welcome to MWCoop!", L"A new, bigger launcher. A quick tour in a few bubbles: you can skip it at any time (Esc) and see it again from Settings.", "0.42.0" },
    { L"Ton pseudo", L"Le nom que les autres joueurs voient au-dessus de ton personnage et dans le salon.",
      L"Your nickname", L"The name the other players see above your character and in the lobby.", "0.42.0" },
    { L"Par où jouer", L"STEAM : le plus simple, tu invites tes amis Steam, sans port ni adresse. IP / VPN : par adresse (réseau local, Radmin, Hamachi…).",
      L"How to connect", L"STEAM: the easiest, you invite your Steam friends, no port or address. IP / VPN: by address (local network, Radmin, Hamachi…).", "0.42.0" },
    { L"HÉBERGER", L"Ouvre un salon : tes amis le rejoignent, chacun se met PRÊT, puis LANCER démarre le jeu de tout le monde. L'engrenage du salon garde ses options.",
      L"HOST", L"Opens a lobby: your friends join it, everyone gets READY, then START launches everyone's game. The lobby's gear holds its options.", "0.42.0" },
    { L"REJOINDRE", L"Entre dans le salon d'un ami (par Steam : son invitation, ou son salon trouvé tout seul ; par IP : son adresse juste au-dessus).",
      L"JOIN", L"Enters a friend's lobby (through Steam: their invite, or their lobby found by itself; by IP: their address just above).", "0.42.0" },
    { L"JOUER EN SOLO", L"Le jeu normal, avec MWCoop chargé mais sans réseau. Ta sauvegarde n'est jamais touchée par les parties des autres.",
      L"PLAY SOLO", L"The regular game, with MWCoop loaded but no network. Your save is never touched by other people's games.", "0.42.0" },
    { L"Toutes les pages, à gauche", L"Le salon, ton personnage, ce que tu partages, les nouveautés et les journaux : un clic, sans fenêtre qui s'ouvre par-dessus.",
      L"Every page, on the left", L"The lobby, your character, what you share, the updates and the logs: one click, no window popping over.", "0.42.0" },
    { L"Tenue et voiture", L"Ton apparence complète (corps, chapeau, lunettes, cheveux, visage, haut, pantalon) en 3D, et la couleur de ta CORRIS.",
      L"Outfit and car", L"Your full look (build, hat, glasses, hair, face, top, pants) in 3D, and your CORRIS color.", "0.42.0" },
    { L"Contenu et mods", L"Ce que tu envoies à tes invités : CD, peinture, drapeau, posters… et tes mods MSCLoader (expérimental), copiés à part chez eux.",
      L"Content and mods", L"What you send to your guests: CDs, paint, flag, posters… and your MSCLoader mods (experimental), copied apart on their side.", "0.42.0" },
    { L"Nouveautés", L"Ce qui change à chaque version. Quand une nouvelle version sort, un bouton « Mettre à jour » apparaît sous le logo : la mise à jour se fait quand tu cliques.",
      L"What's new", L"What changes in each version. When a new version is out, an \"Update\" button shows up under the logo: the update happens when you click it.", "0.42.0" },
    { L"Journaux", L"En cas de souci, crée ici un zip à envoyer. Un « ! » rouge s'y affiche si la dernière partie s'est arrêtée brutalement.",
      L"Logs", L"If something goes wrong, make a zip to send from here. A red \"!\" shows on it if the last game stopped abruptly.", "0.42.0" },
    { L"Réglages", L"Mise à jour, thème, langue, port, dossier du jeu, envoi de l'hôte chez un ami… et ce guide.",
      L"Settings", L"Updates, theme, language, port, game folder, a host's files at a friend's… and this guide.", "0.42.0" },
    { L"Thème et langue", L"Sombre ou clair, français ou anglais, d'un clic.", L"Theme and language", L"Dark or light, French or English, in one click.", "0.42.0" },
    { L"C'est parti !", L"Bon jeu à plusieurs en Finlande. Et si quelque chose ne va pas : les journaux, et le GitHub du mod.",
      L"Let's go!", L"Have fun together in Finland. And if something goes wrong: the logs, and the mod's GitHub.", "0.42.0" },
};
static const int kTutoN = (int)(sizeof(kTuto) / sizeof(kTuto[0]));

// Element montre a l'etape i (rectangle vide : bulle au centre). Coordonnees de la carte.
static RectF TutoTarget(int i)
{
    auto join = [](RectF a, RectF b) { float x = min(a.X, b.X), y = min(a.Y, b.Y); return RectF(x, y, max(a.X + a.Width, b.X + b.Width) - x, max(a.Y + a.Height, b.Y + b.Height) - y); };
    switch (i) {
    case 1: return g_fields[0].r;
    case 2: return join(g_btn[B_NETSTEAM].r, g_btn[B_NETIP].r);
    case 3: return g_btn[B_HOST].r;
    case 4: return g_btn[B_JOIN].r;
    case 5: return g_btn[B_SOLO].r;
    case 6: return join(g_navR[TAB_HOME], g_navR[TAB_LOGS]);
    case 7: return join(g_navR[TAB_SKIN], g_navR[TAB_CAR]);
    case 8: return join(g_navR[TAB_CONTENT], g_navR[TAB_MODS]);
    case 9: return g_navR[TAB_NOTES];
    case 10: return g_navR[TAB_LOGS];
    case 11: return g_navR[TAB_COOP];
    case 12: return join(g_btn[B_LANG].r, g_btn[B_THEME].r);
    }
    return RectF(0, 0, 0, 0);
}
static int TutoTab(int) { return TAB_HOME; }

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
        g_tab = g_tutoTabWas >= 0 ? g_tutoTabWas : TAB_HOME;
        WritePrivateProfileStringW(L"Lanceur", L"TutoVu", L"1", g_iniLauncher.c_str());
        WritePrivateProfileStringW(L"Lanceur", L"TutoVersion", Widen(MyVersion()).c_str(), g_iniLauncher.c_str());
        return;
    }
    g_tuto = i;
    g_tutoHot = 0;
    int t = TutoTab(TutoCur());
    g_tab = t;
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
    g_tutoTabWas = g_tab == TAB_COOP || PageSnap(g_tab) ? TAB_HOME : g_tab;
    g_lobbyOpts = false;
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
    const float w = 380, h = 170;
    if (tg.Width <= 0) { *below = true; return RectF(kCardW / 2 - w / 2, kCardH / 2 - h / 2, w, h); }
    if (tg.X + tg.Width < 260 && tg.Height > 120) {   // grand element de la barre de gauche : bulle a sa droite
        *below = true;
        return RectF(tg.X + tg.Width + 24, min(max(tg.Y + tg.Height / 2 - h / 2, 24.0f), kCardH - 24 - h), w, h);
    }
    float cx = tg.X + tg.Width / 2;
    float x = min(max(cx - w / 2, 24.0f), kCardW - 24 - w);
    *below = tg.Y + tg.Height + 18 + h < kCardH - 16;
    float y = *below ? tg.Y + tg.Height + 18 : tg.Y - 18 - h;
    return RectF(x, y, w, h);
}
static RectF TutoBtn(RectF b, int k)   // 1 suivant, 2 passer, 3 retour
{
    if (k == 1) return RectF(b.X + b.Width - 130, b.Y + b.Height - 50, 114, 36);
    if (k == 3) return RectF(b.X + b.Width - 226, b.Y + b.Height - 50, 88, 36);
    return RectF(b.X + 16, b.Y + b.Height - 50, 80, 36);
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
    RoundRect(veil, RectF(0, 0, kCardW, kCardH), 24);
    RectF hole = tg;
    if (tg.Width > 0) { hole.Inflate(6, 6); RoundRect(veil, hole, min(16.0f, hole.Height / 2)); }
    SolidBrush vb(Color(170, 2, 6, 18));
    g.FillPath(&vb, &veil);
    if (tg.Width > 0) {
        GraphicsPath hp; RoundRect(hp, hole, min(16.0f, hole.Height / 2));
        Pen ring(kAcc, 2.4f); g.DrawPath(&ring, &hp);
    }
    bool below;
    RectF b = TutoBubble(tg, &below);
    GlassLive(g, b, 18);
    Pen bpen(WithA(kAcc, 0.8f), 1.4f);
    { GraphicsPath bp; RoundRect(bp, b, 18); g.DrawPath(&bpen, &bp); }
    if (tg.Width > 0 && !(tg.X + tg.Width < 260 && tg.Height > 120)) {   // fleche vers l'element
        float ax = min(max(tg.X + tg.Width / 2, b.X + 28), b.X + b.Width - 28);
        PointF tri[3];
        if (below) { tri[0] = PointF(ax - 10, b.Y + 1); tri[1] = PointF(ax + 10, b.Y + 1); tri[2] = PointF(ax, b.Y - 11); }
        else { tri[0] = PointF(ax - 10, b.Y + b.Height - 1); tri[1] = PointF(ax + 10, b.Y + b.Height - 1); tri[2] = PointF(ax, b.Y + b.Height + 11); }
        SolidBrush tb(g_dark ? Color(235, 30, 44, 84) : Color(240, 255, 255, 255));
        g.FillPolygon(&tb, tri, 3);
        g.DrawLine(&bpen, tri[0], tri[2]);
        g.DrawLine(&bpen, tri[1], tri[2]);
    }
    const TutoStep &st = kTuto[step];
    wchar_t num[16];
    swprintf_s(num, L"%d / %d", g_tuto + 1, (int)g_tutoList.size());
    Text(g, num, RectF(b.X + b.Width - 84, b.Y + 14, 68, 20), 11.5f, FontStyleBold, kGrey, StringAlignmentFar);
    float tx = b.X + 18;
    if (!g_tutoAll) {   // apres une mise a jour : etiquette NOUVEAU
        const wchar_t *nl = T(L"NOUVEAU", L"NEW");
        float w = PillW(nl, 10);
        Pill(g, RectF(tx, b.Y + 15, w, 20), kRed, nl, Color(255, 255, 255, 255), 10);
        tx += w + 8;
    }
    Title(g, g_fr ? st.tFr : st.tEn, RectF(tx, b.Y + 10, b.X + b.Width - 96 - tx, 28), 17, kAcc);
    Para(g, g_fr ? st.dFr : st.dEn, RectF(b.X + 18, b.Y + 44, b.Width - 36, b.Height - 100), 13, kInk);
    for (int k = 1; k <= 3; k++) {
        if (k == 3 && g_tuto == 0) continue;
        if (k == 2 && g_tuto == (int)g_tutoList.size() - 1) continue;   // (derniere bulle : rien a passer)
        RectF r = TutoBtn(b, k);
        bool hot = g_tutoHot == k;
        GraphicsPath p; RoundRect(p, r, 12);
        if (k == 1) { SolidBrush lg(kAcc); g.FillPath(&lg, &p); if (hot) { SolidBrush hb(Color(55, 255, 255, 255)); g.FillPath(&hb, &p); } }
        else if (hot) { SolidBrush hb(TH(btn2Hot)); g.FillPath(&hb, &p); }
        const wchar_t *lab = k == 1 ? (g_tuto == (int)g_tutoList.size() - 1 ? T(L"TERMINER", L"FINISH") : T(L"SUIVANT", L"NEXT")) : k == 2 ? T(L"Passer", L"Skip") : T(L"Retour", L"Back");
        Text(g, lab, r, 12.5f, FontStyleBold, k == 1 ? kOnAcc : hot ? kAcc : kGrey);
    }
}
