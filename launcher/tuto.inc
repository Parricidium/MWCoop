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
    { L"Toutes les pages, à gauche", L"Le salon, ton personnage, ce que tu partages et les nouveautés : un clic, sans fenêtre qui s'ouvre par-dessus.",
      L"Every page, on the left", L"The lobby, your character, what you share and the updates: one click, no window popping over.", "0.42.0" },
    { L"Tenue et voiture", L"Ton apparence complète (corps, chapeau, lunettes, cheveux, visage, haut, pantalon) en 3D, et la couleur de ta CORRIS.",
      L"Outfit and car", L"Your full look (build, hat, glasses, hair, face, top, pants) in 3D, and your CORRIS color.", "0.42.0" },
    { L"Contenu et mods", L"Ce que tu envoies à tes invités : CD, peinture, drapeau, posters… et tes mods MSCLoader (expérimental), copiés à part chez eux.",
      L"Content and mods", L"What you send to your guests: CDs, paint, flag, posters… and your MSCLoader mods (experimental), copied apart on their side.", "0.42.0" },
    { L"Graphismes", L"Rangés par catégories à déplier : ombres, distance, détails, textures, images par seconde… et maintenant les pseudos des joueurs (distance, ou cachés). Préréglages en un clic ; en jeu : menu F10.",
      L"Graphics", L"Sorted into categories you expand: shadows, distance, detail, textures, frame rate… and now player names (distance, or hidden). One-click presets; in game: F10 menu.", "0.54.0" },
    { L"Nouveautés", L"Ce qui change à chaque version. Quand une nouvelle version sort, un bouton « Mettre à jour » apparaît sous le logo : la mise à jour se fait quand tu cliques.",
      L"What's new", L"What changes in each version. When a new version is out, an \"Update\" button shows up under the logo: the update happens when you click it.", "0.42.0" },
    { L"Journaux", L"Chaque partie garde maintenant ses journaux, rangés par date : déplie celle qui a posé problème, crée son zip ou supprime les vieilles. Un « ! » rouge s'affiche si la dernière partie s'est arrêtée brutalement.",
      L"Logs", L"Every session now keeps its own logs, sorted by date: expand the one that went wrong, zip it or delete old ones. A red \"!\" shows if the last game stopped abruptly.", "0.53.0" },
    { L"API moddeurs", L"Aussi en haut maintenant. Tu fais des mods ? Ce que l'API de MWCoop permet, un exemple à copier, la DLL et la doc sur GitHub.",
      L"Modding API", L"Also up here now. Making mods? What MWCoop's API does, an example to copy, the DLL and the docs on GitHub.", "0.46.0" },
    { L"Réglages", L"Mise à jour, thème, langue, port, dossier du jeu, envoi de l'hôte chez un ami… et ce guide.",
      L"Settings", L"Updates, theme, language, port, game folder, a host's files at a friend's… and this guide.", "0.42.0" },
    { L"Thème et langue", L"Sombre ou clair, français ou anglais, d'un clic.", L"Theme and language", L"Dark or light, French or English, in one click.", "0.42.0" },
    { L"Discord", L"La communauté MWCoop : de l'aide, les bugs à signaler, les nouveautés et des joueurs pour faire équipe. GitHub et Ko-fi juste à côté.",
      L"Discord", L"The MWCoop community: help, bug reports, news and players to team up with. GitHub and Ko-fi right next to it.", "0.51.0" },
    { L"MSCLoader de MWCoop", L"Dans l'onglet Mods : OFFICIEL (celui installé dans le jeu) ou MWCOOP (installé à part, le jeu n'est pas modifié). Si tes mods ne se chargent pas, essaie MWCOOP, ou RÉINSTALLER.",
      L"MWCoop's MSCLoader", L"In the Mods tab: OFFICIAL (the one installed in the game) or MWCOOP (installed apart, the game is not changed). If your mods don't load, try MWCOOP, or REINSTALL.", "0.60.0" },
    { L"Démarrer le jeu", L"Dans Réglages > Jeu : Copie (d'origine), Dossier du jeu ou Steam. Si MWCoop ne se charge pas, essaie l'un puis l'autre ; le lanceur passe aussi tout seul au dossier du jeu.",
      L"Start the game", L"In Settings > Game: Copy (default), Game folder or Steam. If MWCoop doesn't load, try one then the other; the launcher also switches to the game folder by itself.", "0.62.2" },
    { L"Désinstaller", L"Dans Réglages > À propos : retire tout MWCoop (dossiers compris) et le MSCLoader de MWCoop, à la Corbeille. Ta sauvegarde, tes mods et le MSCLoader officiel restent.",
      L"Uninstall", L"In Settings > About: removes all of MWCoop (folders included) and MWCoop's MSCLoader, to the Recycle Bin. Your save, your mods and the official MSCLoader stay.", "0.60.8" },
    { L"Crédits", L"L'étoile en haut : ceux qui donnent quelque chose pour améliorer MWCoop. Merci à Dom pour ses 155 tenues, dans l'onglet Tenue !",
      L"Credits", L"The star at the top: the people who give something to make MWCoop better. Thanks to Dom for 155 outfits, in the Outfit tab!", "0.61.0" },
    { L"Mode débogage", L"Dans Journaux : si MWCoop ne se charge pas, active-le, lance ta partie puis crée le zip. Il note tout ce que fait le lanceur et ce que le jeu a vraiment chargé.",
      L"Debug mode", L"In Logs: if MWCoop doesn't load, turn it on, start your game, then create the zip. It records everything the launcher does and what the game really loaded.", "0.62.0" },
    { L"Modes de compatibilité", L"Dans Réglages > Jeu. Après un plantage, Windows ajoute parfois un mode de compatibilité au jeu, et MWCoop ne se charge plus : le lanceur le retire avant chaque partie.",
      L"Compatibility modes", L"In Settings > Game. After a crash, Windows sometimes adds a compatibility mode to the game, and MWCoop stops loading: the launcher removes it before every game.", "0.64.1" },
    { L"Guide", L"Tout pour jouer sans souci, rangé par catégories à déplier : installer, héberger, rejoindre, partager ses mods, Copie / Dossier / Steam… et les solutions aux problèmes courants, avec des images.",
      L"Guide", L"Everything to play trouble-free, sorted into categories you expand: install, host, join, share your mods, Copy / Folder / Steam… and fixes for common problems, with pictures.", "0.64.0" },
    { L"Touches en jeu", L"Dans Réglages > Chez un ami : clique sur une pastille puis appuie sur la touche voulue. Tchat (T), porte-monnaie pour donner de l'argent aux autres (N), vocal (V).",
      L"In-game keys", L"In Settings > At a friend's: click a pill then press the key you want. Chat (T), wallet to give money to others (N), voice (V).", "0.67.0" },
    { L"Visage importé", L"Dans Tenue : ton propre visage. Gabarit ouvre l'image du visage du jeu avec ses coutures : peins par-dessus, puis Importer. Les autres le voient en jeu.",
      L"Imported face", L"In Outfit: your own face. Template opens the game's face image with its seams: paint over it, then Import. Others see it in game.", "0.69.0" },
    { L"C'est parti !", L"Bon jeu à plusieurs en Finlande. Et si quelque chose ne va pas : les journaux, et le Discord.",
      L"Let's go!", L"Have fun together in Finland. And if something goes wrong: the logs, and the Discord.", "0.42.0" },
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
    case 6: return join(g_navR[TAB_HOME], g_navR[TAB_NOTES]);
    case 7: return join(g_navR[TAB_SKIN], g_navR[TAB_CAR]);
    case 8: return join(g_navR[TAB_CONTENT], g_navR[TAB_MODS]);
    case 9: return g_navR[TAB_GFX];
    case 10: return g_navR[TAB_NOTES];
    case 11: return g_navR[TAB_LOGS];
    case 12: return g_navR[TAB_API];
    case 13: return g_navR[TAB_COOP];
    case 14: return join(g_btn[B_LANG].r, g_btn[B_THEME].r);
    case 15: return g_btn[B_DISCORD].r;
    case 16: { RectF a = kMscSrcOff, b = kMscReinst; return RectF(a.X - 110, a.Y, b.X + b.Width - a.X + 110, a.Height); }
    case 17: return kSetSteamRow;
    case 18: return kSetUninst;
    case 19: return g_navR[TAB_CREDITS];
    case 20: return kLogsDebugRow;
    case 21: return kSetCompatRow;
    case 22: return g_navR[TAB_WIKI];
    case 23: return kSetKeysRow;
    case 24: return g_faceRect;
    }
    return RectF(0, 0, 0, 0);
}
static int TutoTab(int i) { return i == 16 ? TAB_MODS : i == 17 || i == 18 || i == 21 || i == 23 ? TAB_COOP : i == 24 ? TAB_SKIN : i == 20 ? TAB_LOGS : TAB_HOME; }   // (16 : choix de MSCLoader, onglet MODS ; 17, 18 : Reglages)

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
        DiscordAskMaybe();   // (premier lancement : l'invitation au Discord vient apres la visite)
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
