// ---------------------------------------------------------------- page GUIDE : petit wiki des joueurs - inclus par launcher.cpp
// Demande de JD (08/10) : tout ce qu'il faut pour jouer sans probleme (installation, MSCLoader officiel / de MWCoop,
// partage des mods, options, Copie / Dossier / Steam...), range en categories pliables, avec des mini tutos en images.
// A gauche les categories (clic : plier / deplier) et leurs articles ; a droite l'article : sections pliables, etapes
// numerotees, encadres (astuce, attention), figures dessinees avec les elements du lanceur (a jour avec le theme et la
// langue, sans image a telecharger) et boutons qui ouvrent la page concernee. Contenu : wikidata.inc.
enum { WB_H, WB_P, WB_B, WB_S, WB_TIP, WB_WARN, WB_FIG, WB_GO };
// Jeu (demande de JD, 19/10 : sur My Summer Car, le guide ne parle que de MSC) : 0 = les deux jeux, WG_MWC = seulement My Winter
// Car, WG_MSC = seulement My Summer Car. Sur un article (WARTG), une touche (kWikiKeys) ou un bloc ; un WB_H marque emporte
// toute sa section (jusqu'au WB_H suivant). Un texte adapte = le bloc WG_MWC d'origine suivi de sa version WG_MSC.
enum { WG_ALL, WG_MWC, WG_MSC };
static bool WikiGame(int game) { return game == WG_ALL || game == (g_game == GAME_MSC ? WG_MSC : WG_MWC); }
struct WikiBlock { int kind; const wchar_t *fr, *en; int arg; int game; };   // arg : figure (WB_FIG), page (WB_GO) ; game : WG_*
struct WikiArticle { int cat; const wchar_t *tFr, *tEn, *sFr, *sEn; const WikiBlock *b; int n; int game; };
struct WikiCat { int icon; const wchar_t *fr, *en; };
enum { WF_FOLDER, WF_FOOTER, WF_START, WF_NET, WF_LOBBY, WF_MSC, WF_SHARE, WF_ACCEPT, WF_LOGS, WF_UPDATE, WF_KEYS, WF_GUEST, WF_CONTENT, WF_BAD };

#include "wikidata.inc"

static const int kWikiCats = (int)(sizeof(kWikiCat) / sizeof(kWikiCat[0]));
static const int kWikiArts = (int)(sizeof(kWikiArt) / sizeof(kWikiArt[0]));
static_assert(sizeof(kWikiArt) / sizeof(kWikiArt[0]) == WA_PLACES + 1, "kWikiArt et WA_* : un article par valeur, dans l'ordre");   // (WA_VAZ manquait : liens decales)
// Article, bloc ou touche du jeu choisi. Un bloc est cache par son propre jeu, par celui du WB_H de sa section, ou s'il mene
// (WB_GO) a un article cache.
static bool WikiArtShown(int a) { return a >= 0 && a < kWikiArts && WikiGame(kWikiArt[a].game); }
static bool WikiBlockShown(const WikiArticle &art, int i)
{
    const WikiBlock &k = art.b[i];
    if (!WikiGame(k.game)) return false;
    if (k.kind == WB_GO && k.arg >= 1000 && k.arg < 2000 && !WikiArtShown(k.arg - 1000)) return false;   // (2000 : onglet Addons)
    if (k.kind != WB_H) for (int j = i - 1; j >= 0; j--) if (art.b[j].kind == WB_H) return WikiGame(art.b[j].game);
    return true;
}
static int WikiKeysShown() { int n = 0; for (int i = 0; i < kWikiKeysN; i++) if (WikiGame(kWikiKeys[i].game)) n++; return n; }
// Trois colonnes (maquette du 17/10, validee par JD) : sommaire 240, article, Nouveautes + « Un souci ? » 272, marges de 12.
// En-tete : pilules Guide / Nouveautes / Credits (Nouveautes et Credits = pages TAB_NOTES et TAB_CREDITS, ui.inc) et le
// champ Chercher (filtre les articles ; taper depuis un autre onglet ramene au Guide).
static const RectF kWikiList(264, 88, 240, 656), kWikiBody(516, 88, 464, 656), kWikiNews(992, 88, 272, 656);
static int g_wikiSel;
static int g_wikiShown = -1;                  // article cadre dans le sommaire (autre article : on le remet en vue)
static std::set<int> g_wikiFold;              // sections pliees : article * 1000 + bloc
static float g_wikiListScroll, g_wikiH;
static std::wstring g_wikiSearch;             // champ Chercher de l'en-tete (vide : tout le sommaire)
static bool g_wikiFocus;                      // champ actif (curseur, WM_CHAR -> GuideChar)
static float g_guideTabRight = 1264;          // bord droit des pilules (ui.inc DrawHeader)
static RectF g_credBtnRect;                   // pilule Credits (bulle de la visite : tuto.inc, etape 19)
static const Color kWikiPink(255, 255, 107, 139), kWikiGold(255, 255, 210, 143);   // (maquette : .chip.new, etoile de l'astuce)
// Zones de la page dans le trou laisse par uikit.inc entre UI_WIKI_ART + 200 et UI_WIKI_SEC (UiClick les passe a WikiClick).
enum { UI_WIKI_TAB = UI_WIKI_ART + 200, UI_WIKI_SEARCH = UI_WIKI_ART + 210, UI_WIKI_SEARCHX, UI_WIKI_PREV, UI_WIKI_NEXT };
static RectF WikiArea() { return RectF(kWikiBody.X + 22, kWikiBody.Y + 108, kWikiBody.Width - 44, kWikiBody.Height - 108 - 52); }
static float WikiMaxScroll() { return max(0.0f, g_wikiH - WikiArea().Height); }
static std::wstring WikiUpper(std::wstring s) { if (!s.empty()) CharUpperBuffW(&s[0], (DWORD)s.size()); return s; }
static float WikiTextH(Graphics &g, const std::wstring &t, float w, float px, int style)
{
    UiFont f(L"Segoe UI", px, style);
    StringFormat sf;
    RectF box;
    MeasureStr(g, t.c_str(), -1, &f, RectF(0, 0, w, 100000), &sf, &box);
    return box.Height;
}
static void WikiText(Graphics &g, const std::wstring &t, RectF r, float px, int style, Color c)
{
    UiFont f(L"Segoe UI", px, style);
    StringFormat sf;
    SolidBrush b(c);
    DrawStr(g, t.c_str(), -1, &f, r, &sf, &b);
}

// ---- figures : petites maquettes avec les elements du lanceur (zones sans action : UI_WIKI_FIG)
static void WikiBadge(Graphics &g, float cx, float cy, int n, Color c = Color(0, 0, 0, 0))
{
    if (c.GetA() == 0) c = kAcc;
    SolidBrush b(c); g.FillEllipse(&b, cx - 11, cy - 11, 22.0f, 22.0f);
    Text(g, std::to_wstring(n), RectF(cx - 11, cy - 11, 22, 22), 12, FontStyleBold, kOnAcc);
}
static void WikiArrow(Graphics &g, PointF a, PointF b, Color c)
{
    Pen p(c, 2.2f);
    p.SetStartCap(LineCapRound);
    AdjustableArrowCap cap(5, 5, TRUE);
    p.SetCustomEndCap(&cap);
    g.DrawLine(&p, a, b);
}
static void WikiCard(Graphics &g, RectF r, bool sel = false, float rad = 12)
{
    GraphicsPath p; RoundRect(p, r, rad);
    SolidBrush b(sel ? TH(cardSel) : TH(card)); g.FillPath(&b, &p);
    Pen pen(sel ? WithA(kAcc, 0.7f) : TH(choiceBorder), 1.0f); g.DrawPath(&pen, &p);
}
// Ligne de fichier d'une arborescence : icone, nom, etiquette coloree a droite.
static void WikiFileRow(Graphics &g, float x, float y, float w, int icon, const std::wstring &name, const std::wstring &tag, Color tc, float indent = 0)
{
    Icon(g, icon, x + indent + 12, y + 13, 16, kInk, 1.8f);
    Text(g, name, RectF(x + indent + 28, y, w - indent - 28, 26), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
    if (!tag.empty()) { float tw = PillW(tag, 10); Pill(g, RectF(x + w - tw, y + 3, tw, 20), WithA(tc, 0.2f), tag, tc, 10); }
}
static void WikiCaption(Graphics &g, float x, float y, float w, int n, const std::wstring &s)
{
    WikiBadge(g, x + 11, y + 11, n);
    Para(g, s, RectF(x + 30, y + 1, w - 30, 40), 12, kGrey);
}

// Hauteur de la figure ; dessinee si draw.
static float WikiFigure(Graphics &g, int id, RectF a, bool draw)
{
    const int Z = UI_WIKI_FIG;
    float x = a.X + 16, w = a.Width - 32, y = a.Y + 16;
    switch (id) {
    case WF_FOLDER: {   // installation ideale : le dossier du jeu
        if (!draw) return 262;
        Text(g, std::wstring(L"…\\steamapps\\common\\") + GM().steamDir + L"\\", RectF(x, y, w, 22), 12, FontStyleBold, kGrey, StringAlignmentNear);
        y += 28;
        WikiFileRow(g, x, y, w, IC_PLAY, GM().exe, T(L"LE JEU", L"THE GAME"), kGrey); y += 28;
        WikiFileRow(g, x, y, w, IC_HOME, L"MWCoop.exe", T(L"LANCEUR", L"LAUNCHER"), kAcc); y += 28;
        WikiFileRow(g, x, y, w, IC_CODE, L"version.dll", T(L"CHARGEUR MWCOOP", L"MWCOOP LOADER"), kAcc); y += 28;
        WikiFileRow(g, x, y, w, IC_FOLDER, L"MWCoop\\", T(L"LE MOD, PROFILS, JOURNAUX", L"THE MOD, PROFILES, LOGS"), kAcc); y += 28;
        WikiFileRow(g, x, y, w, IC_FOLDER, L"Mods\\", T(L"TES MODS (MSCLOADER)", L"YOUR MODS (MSCLOADER)"), kOk); y += 28;
        WikiFileRow(g, x, y, w, IC_CODE, L"winhttp.dll + doorstop_config.ini", T(L"MSCLOADER OFFICIEL (OPTIONNEL)", L"OFFICIAL MSCLOADER (OPTIONAL)"), kOk); y += 28;
        WikiFileRow(g, x, y, w, IC_FOLDER, (std::wstring(GM().data) + L"\\").c_str(), T(L"DONNÉES DU JEU", L"GAME DATA"), kGrey);
        return 262;
    }
    case WF_FOOTER: {   // pied du lanceur : jeu trouve, changer de dossier
        if (!draw) return 118;
        RectF bar(x, y, w, 40);
        WikiCard(g, bar);
        Icon(g, IC_CHECK, bar.X + 22, bar.Y + 20, 16, kOk, 2.4f);
        Text(g, GM().name, RectF(bar.X + 40, bar.Y, 160, 40), 13, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, std::wstring(L"C:\\…\\steamapps\\common\\") + GM().steamDir + L"\\", RectF(bar.X + 160, bar.Y, w - 300, 40), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        UiButton(g, RectF(bar.X + bar.Width - 128, bar.Y + 6, 120, 28), T(L"Changer", L"Change"), Z, false, IC_FOLDER, true, 10);
        WikiBadge(g, bar.X + 22, bar.Y + 52, 1); WikiBadge(g, bar.X + bar.Width - 68, bar.Y + 52, 2);
        WikiCaption(g, x, y + 70, w / 2 - 8, 1, T(L"Le jeu trouvé par le lanceur, et son dossier", L"The game the launcher found, and its folder"));
        WikiCaption(g, x + w / 2 + 8, y + 70, w / 2 - 8, 2, T(L"Pour en choisir un autre (Réglages > Jeu aussi)", L"To pick another one (Settings > Game too)"));
        return 118;
    }
    case WF_START: {   // Demarrer le jeu : Copie / Dossier / Steam
        if (!draw) return 150;
        RectF row(x, y, w, 52);
        WikiCard(g, row);
        Text(g, T(L"Démarrer le jeu", L"Start the game"), RectF(row.X + 16, row.Y, 200, 52), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        int how = 0;
        if (const Opt *ls = OptByKey("LancerSteam")) how = OptGet(*ls) && !IsWine() ? 2 : NoCopy() ? 1 : 0;
        RectF seg(row.X + row.Width - 256, row.Y + 8, 240, 36);
        UiSeg(g, seg, { T(L"Copie", L"Copy"), T(L"Dossier", L"Folder"), L"Steam" }, how, Z, 12);
        float sw = (seg.Width - 6) / 3;
        for (int i = 0; i < 3; i++) WikiBadge(g, seg.X + 3 + sw * i + sw / 2, row.Y + 64, i + 1);
        float cw = (w - 16) / 3, cy = y + 84;
        WikiCaption(g, x, cy, cw, 1, T(L"Copie à part du jeu (d'origine)", L"Separate copy of the game (default)"));
        WikiCaption(g, x + cw + 8, cy, cw, 2, T(L"Directement le dossier du jeu", L"Straight from the game folder"));
        WikiCaption(g, x + 2 * (cw + 8), cy, cw, 3, T(L"Par Steam (le lanceur se ferme)", L"Through Steam (launcher closes)"));
        Text(g, T(L"(ton réglage actuel est en couleur)", L"(your current setting is highlighted)"), RectF(x, y + 128, w, 18), 11, FontStyleRegular, kGrey, StringAlignmentNear);
        return 150;
    }
    case WF_NET: {   // accueil : Steam / IP, puis heberger / rejoindre / solo
        if (!draw) return 156;
        UiSeg(g, RectF(x, y, 260, 40), { L"STEAM", L"IP / VPN" }, g_steamNet ? 0 : 1, Z, 12.5f);
        WikiBadge(g, x + 270, y + 20, 1);
        float bw = (w - 24) / 3;
        UiButton(g, RectF(x, y + 56, bw, 44), T(L"HÉBERGER", L"HOST"), Z, true, IC_HOMEPLUS);
        UiButton(g, RectF(x + bw + 12, y + 56, bw, 44), T(L"REJOINDRE", L"JOIN"), Z, false, IC_LOGIN);
        UiButton(g, RectF(x + 2 * (bw + 12), y + 56, bw, 44), T(L"SOLO", L"SOLO"), Z, false, IC_PLAY);
        WikiBadge(g, x + bw / 2, y + 112, 2); WikiBadge(g, x + bw * 1.5f + 12, y + 112, 3); WikiBadge(g, x + bw * 2.5f + 24, y + 112, 4);
        Para(g, T(L"1 réseau · 2 ouvrir un salon · 3 entrer dans celui d'un ami · 4 jouer seul, avec MWCoop", L"1 network · 2 open a lobby · 3 enter a friend's · 4 play alone, with MWCoop"),
             RectF(x, y + 126, w, 20), 11.5f, kGrey);
        return 156;
    }
    case WF_LOBBY: {   // salon : hote, amis, PRET, LANCER
        if (!draw) return 150;
        const wchar_t *lab[4] = { T(L"Héberger", L"Host"), T(L"Inviter des amis", L"Invite friends"), T(L"Chacun : PRÊT", L"Everyone: READY"), T(L"L'hôte : LANCER", L"Host: START") };
        const int ic[4] = { IC_HOMEPLUS, IC_USERS, IC_CHECK, IC_PLAY };
        float bw = (w - 3 * 28) / 4;
        for (int i = 0; i < 4; i++) {
            RectF b(x + i * (bw + 28), y + 8, bw, 92);
            WikiCard(g, b, i == 3);
            Icon(g, ic[i], b.X + b.Width / 2, b.Y + 30, 24, i == 3 ? kAcc : kInk, 2.0f);
            Para(g, lab[i], RectF(b.X + 6, b.Y + 50, b.Width - 12, 40), 12, kInk, StringAlignmentCenter);
            WikiBadge(g, b.X + 4, b.Y + 4, i + 1);
            if (i < 3) WikiArrow(g, PointF(b.X + b.Width + 4, b.Y + 46), PointF(b.X + b.Width + 24, b.Y + 46), kAcc);
        }
        Para(g, T(L"Le jeu de tout le monde démarre ensemble, dans la partie de l'hôte.", L"Everyone's game starts together, in the host's game."), RectF(x, y + 112, w, 22), 12, kGrey);
        return 150;
    }
    case WF_MSC: {   // onglet MODS : OFFICIEL / MWCOOP
        if (!draw) return 196;
        Text(g, L"MSCLoader", RectF(x, y, 92, 30), 12, FontStyleBold, kGrey, StringAlignmentFar);
        UiSeg(g, RectF(x + 100, y, 230, 30), { T(L"OFFICIEL", L"OFFICIAL"), L"MWCOOP" }, MscOwn() ? 1 : 0, Z, 12);
        UiButton(g, RectF(x + 342, y, 150, 30), T(L"RÉINSTALLER", L"REINSTALL"), Z, false, IC_DOWNLOAD, true, 15);
        WikiBadge(g, x + 157, y + 44, 1); WikiBadge(g, x + 272, y + 44, 2); WikiBadge(g, x + 417, y + 44, 3);
        float cy = y + 66;
        WikiCaption(g, x, cy, w, 1, T(L"OFFICIEL : celui installé dans le dossier du jeu (winhttp.dll). Le même qu'en solo.", L"OFFICIAL: the one installed in the game folder (winhttp.dll). The same as solo."));
        WikiCaption(g, x, cy + 40, w, 2, T(L"MWCOOP : installé à part par le lanceur, le jeu n'est pas modifié. Tes mods restent ceux du dossier Mods.", L"MWCOOP: installed apart by the launcher, the game isn't changed. Your mods stay the ones in the Mods folder."));
        WikiCaption(g, x, cy + 80, w, 3, T(L"RÉINSTALLER : remet MSCLoader à neuf (tes mods et leurs réglages sont gardés).", L"REINSTALL: puts MSCLoader back fresh (your mods and their settings are kept)."));
        return 196;
    }
    case WF_SHARE: {   // mods de l'hote -> copie a part chez l'invite
        if (!draw) return 170;
        float bw = (w - 80) / 2;
        RectF h(x, y + 6, bw, 120), gst(x + bw + 80, y + 6, bw, 120);
        WikiCard(g, h); WikiCard(g, gst, true);
        Icon(g, IC_HOMEPLUS, h.X + 24, h.Y + 22, 18, kAcc, 2.0f);
        Text(g, T(L"L'hôte", L"The host"), RectF(h.X + 40, h.Y + 8, bw - 50, 28), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        WikiFileRow(g, h.X + 10, h.Y + 44, bw - 20, IC_PUZZLE, T(L"ses mods", L"their mods"), L"", kInk);
        WikiFileRow(g, h.X + 10, h.Y + 72, bw - 20, IC_MUSIC, T(L"CD, images, peinture…", L"CDs, images, paint…"), L"", kInk);
        Icon(g, IC_USER, gst.X + 24, gst.Y + 22, 18, kAcc, 2.0f);
        Text(g, T(L"Toi (invité)", L"You (guest)"), RectF(gst.X + 40, gst.Y + 8, bw - 50, 28), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        WikiFileRow(g, gst.X + 10, gst.Y + 44, bw - 20, IC_FOLDER, T(L"copie à part", L"separate copy"), T(L"POUR CETTE PARTIE", L"FOR THIS GAME"), kAcc);
        WikiFileRow(g, gst.X + 10, gst.Y + 72, bw - 20, IC_PUZZLE, T(L"tes mods à toi", L"your own mods"), T(L"PAS TOUCHÉS", L"UNTOUCHED"), kOk);
        WikiArrow(g, PointF(h.X + bw + 10, h.Y + 60), PointF(gst.X - 10, gst.Y + 60), kAcc);
        Para(g, T(L"Envoyés au salon, copiés à part chez toi : ta sauvegarde, tes mods et tes réglages ne bougent pas.", L"Sent in the lobby, copied apart on your side: your save, mods and settings don't move."), RectF(x, y + 136, w, 24), 12, kGrey);
        return 170;
    }
    case WF_ACCEPT: {   // Options du jeu > Partie et partage : accepter l'envoi de l'hote
        if (!draw) return 96;
        RectF row(x, y, w, 60);
        WikiCard(g, row);
        bool on = false;
        if (const Opt *ma = OptByKey("ModsAuto")) on = OptGet(*ma) != 0;
        SetRowText(g, RectF(row.X + 16, row.Y, row.Width - 90, row.Height), T(L"Accepter les fichiers de l'hôte", L"Accept the host's files"), T(L"Ses mods, CD et images, copiés à part, sans demander", L"Their mods, CDs and images, copied apart, without asking"));
        UiToggle(g, RectF(row.X + row.Width - 66, row.Y + 16, 50, 28), on, false);
        HotZone(row, Z);
        Text(g, T(L"Options du jeu > Partie et partage (ton réglage actuel)", L"Game options > Game and sharing (your current setting)"), RectF(x, y + 66, w, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        return 96;
    }
    case WF_LOGS: {   // journaux : mode debogage, zip
        if (!draw) return 132;
        RectF row(x, y, w, 48);
        WikiCard(g, row, g_debug);
        Text(g, T(L"Mode débogage", L"Debug mode"), RectF(row.X + 16, row.Y, 220, 48), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        UiToggle(g, RectF(row.X + row.Width - 66, row.Y + 10, 50, 28), g_debug, false);
        WikiBadge(g, row.X + row.Width - 82, row.Y + 24, 1);
        UiButton(g, RectF(x + w - 232, y + 60, 232, 40), T(L"Créer un zip à envoyer", L"Create a zip to send"), Z, true, IC_ZIP);
        WikiBadge(g, x + w - 8, y + 62, 3);
        RectF game(x, y + 60, w - 260, 40);
        WikiCard(g, game);
        Icon(g, IC_PLAY, game.X + 20, game.Y + 20, 14, kInk, 2.0f);
        Text(g, T(L"Lance ta partie, puis ferme le jeu", L"Start your game, then close it"), RectF(game.X + 36, game.Y, game.Width - 40, 40), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
        WikiBadge(g, game.X + game.Width - 22, game.Y + 20, 2);
        Text(g, T(L"Le zip se crée sur ton bureau : envoie-le sur le Discord.", L"The zip is created on your desktop: send it on the Discord."), RectF(x, y + 108, w, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        return 132;
    }
    case WF_UPDATE: {   // bouton de mise a jour sous le logo
        if (!draw) return 92;
        RectF b(x, y + 4, 208, 40);
        GraphicsPath p; RoundRect(p, b, 12);
        SolidBrush fb(kAcc); g.FillPath(&fb, &p);
        Icon(g, IC_DOWNLOAD, b.X + 20, b.Y + 20, 17, kOnAcc, 2.2f);
        Text(g, T(L"Mettre à jour · 0.64.0", L"Update · 0.64.0"), RectF(b.X + 34, b.Y, b.Width - 40, b.Height), 13, FontStyleBold, kOnAcc, StringAlignmentNear);
        WikiBadge(g, b.X + b.Width + 18, b.Y + 20, 1);
        Para(g, T(L"Apparaît sous le logo quand une version sort. Un clic : téléchargée et installée, le lanceur redémarre.", L"Shows under the logo when a version is out. One click: downloaded and installed, the launcher restarts."),
             RectF(b.X + b.Width + 40, b.Y - 2, w - b.Width - 40, 44), 12, kGrey);
        Text(g, T(L"Hôte et invités : toujours la même version.", L"Host and guests: always the same version."), RectF(x, y + 56, w, 20), 12, FontStyleBold, kWarn, StringAlignmentNear);
        return 92;
    }
    case WF_KEYS: {   // touches en jeu (wikidata.inc : kWikiKeys)
        float h = 8 + WikiKeysShown() * 34.0f, ky = y - 34;
        if (!draw) return h + 16;
        for (int i = 0; i < kWikiKeysN; i++) {
            if (!WikiGame(kWikiKeys[i].game)) continue;
            ky += 34;
            float kw = max(44.0f, PillW(kWikiKeys[i].key, 12) + 8);
            RectF k(x, ky, kw, 28);
            GraphicsPath p; RoundRect(p, k, 7);
            SolidBrush kb(TH(field)); g.FillPath(&kb, &p);
            Pen kp(TH(fieldBorder), 1.2f); g.DrawPath(&kp, &p);
            Pen kd(TH(fieldBorder), 2.4f); g.DrawLine(&kd, k.X + 6, k.Y + k.Height, k.X + k.Width - 6, k.Y + k.Height);
            Text(g, kWikiKeys[i].key, k, 12, FontStyleBold, kInk);
            Text(g, T(kWikiKeys[i].fr, kWikiKeys[i].en), RectF(x + 110, ky, w - 110, 28), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
        }
        return h + 16;
    }
    case WF_GUEST: {   // la sauvegarde : hote / invite
        if (!draw) return 140;
        float bw = (w - 20) / 2;
        RectF h(x, y + 4, bw, 96), gs(x + bw + 20, y + 4, bw, 96);
        WikiCard(g, h); WikiCard(g, gs);
        Icon(g, IC_HOMEPLUS, h.X + 24, h.Y + 24, 18, kAcc, 2.0f);
        Text(g, T(L"Hôte", L"Host"), RectF(h.X + 42, h.Y + 10, bw - 50, 28), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        Para(g, T(L"Sa sauvegarde à lui : la partie de tout le monde. Il sauvegarde comme en solo.", L"Their own save: everyone's game. They save like in solo."), RectF(h.X + 14, h.Y + 42, bw - 28, 50), 12, kGrey);
        Icon(g, IC_USER, gs.X + 24, gs.Y + 24, 18, kAcc, 2.0f);
        Text(g, T(L"Invité", L"Guest"), RectF(gs.X + 42, gs.Y + 10, bw - 50, 28), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        Para(g, T(L"Joue dans le monde de l'hôte, avec un profil à part : sa propre sauvegarde solo n'est jamais touchée.", L"Plays in the host's world, with a separate profile: their own solo save is never touched."), RectF(gs.X + 14, gs.Y + 42, bw - 28, 50), 12, kGrey);
        Para(g, T(L"Son argent, ses délits, sa tenue : gardés pour lui, d'une partie à l'autre.", L"Their money, crimes, outfit: kept for them, from one session to the next."), RectF(x, y + 108, w, 24), 12, kGrey);
        return 140;
    }
    case WF_CONTENT: {   // contenu envoye : tuiles
        if (!draw) return 116;
        const int ic[5] = { IC_MUSIC, IC_IMAGE, IC_RADIO, IC_FLAG, IC_CAR };
        const wchar_t *lab[5] = { T(L"CD", L"CDs"), T(L"Posters", L"Posters"), T(L"Radio", L"Radio"), T(L"Drapeau", L"Flag"), T(L"Peinture", L"Paint") };
        float tw = (w - 4 * 12) / 5;
        for (int i = 0; i < 5; i++) {
            RectF t(x + i * (tw + 12), y + 4, tw, 76);
            WikiCard(g, t, i == 0);
            Icon(g, ic[i], t.X + t.Width / 2, t.Y + 28, 22, i == 0 ? kAcc : kInk, 2.0f);
            Text(g, lab[i], RectF(t.X, t.Y + 46, t.Width, 24), 12, FontStyleBold, kInk);
        }
        Para(g, T(L"Ce que tu mets dans ton jeu, tes invités le voient et l'entendent aussi.", L"What you put in your game, your guests see and hear it too."), RectF(x, y + 88, w, 22), 12, kGrey);
        return 116;
    }
    case WF_BAD: {   // a eviter : installation qui pose probleme
        if (!draw) return 152;
        struct Bad { int ic; const wchar_t *fr, *en; } bad[4] = {
            { IC_FOLDER, L"Deux installations du jeu (anciennes copies) : le lanceur et Steam ne lancent pas la même", L"Two game installs (old copies): the launcher and Steam don't start the same one" },
            { IC_ALERT, L"Antivirus qui met version.dll en quarantaine, ou bloque le dossier", L"Antivirus quarantining version.dll, or blocking the folder" },
            { IC_CODE, L"Une autre version.dll (ReShade…) à côté du jeu", L"Another version.dll (ReShade…) next to the game" },
            { IC_DOWNLOAD, L"Lanceur copié ailleurs (bureau, OneDrive) : il ne se met pas à jour", L"Launcher copied elsewhere (desktop, OneDrive): it doesn't update" } };
        for (int i = 0; i < 4; i++) {
            float ry = y + i * 34.0f;
            Icon(g, IC_CLOSE, x + 12, ry + 13, 14, kRed, 2.6f);
            Icon(g, bad[i].ic, x + 38, ry + 13, 16, kInk, 1.8f);
            Text(g, T(bad[i].fr, bad[i].en), RectF(x + 56, ry, w - 56, 26), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
        }
        return 152;
    }
    }
    return 0;
}


// ---- recherche : articles dont le titre, le sous-titre ou un bloc contient le texte (sans accents ni casse, SetFold de gfx.inc)
static std::vector<char> g_wikiHits;
static std::wstring g_wikiHitsFor = L"\x01";
static void WikiHitsUpdate()
{
    if (g_wikiHitsFor == g_wikiSearch) return;
    g_wikiHitsFor = g_wikiSearch;
    g_wikiHits.assign(kWikiArts, 1);
    std::wstring q = SetFold(Trim(g_wikiSearch));
    if (q.empty()) return;
    for (int a = 0; a < kWikiArts; a++) {
        const WikiArticle &art = kWikiArt[a];
        std::wstring all = std::wstring(T(art.tFr, art.tEn)) + L"\n" + T(art.sFr, art.sEn);
        for (int i = 0; i < art.n; i++) if (art.b[i].fr && WikiBlockShown(art, i)) { all += L"\n"; all += T(art.b[i].fr, art.b[i].en); }
        g_wikiHits[a] = SetFold(all).find(q) != std::wstring::npos;
    }
}
static bool WikiHit(int a) { WikiHitsUpdate(); return WikiArtShown(a) && (Trim(g_wikiSearch).empty() || g_wikiHits[a] != 0); }
// Ordre du sommaire : par categorie, puis l'ordre de wikidata.inc (les derniers articles ajoutes rejoignent leur categorie) ;
// les articles propres au jeu choisi (WARTG) en tete de leur categorie. Articles de l'autre jeu : absents.
static const std::vector<int> &WikiOrder()
{
    static std::vector<int> o;
    if (o.empty())
        for (int c = 0; c < kWikiCats; c++)
            for (int own = 1; own >= 0; own--)
                for (int a = 0; a < kWikiArts; a++)
                    if (kWikiArt[a].cat == c && WikiArtShown(a) && (kWikiArt[a].game != WG_ALL) == (own != 0)) o.push_back(a);
    return o;
}
// Article voisin dans le sommaire (parmi ceux montres) ; -1 au bout.
static int WikiNeighbor(int dir)
{
    const std::vector<int> &o = WikiOrder();
    int pos = -1;
    for (int i = 0; i < (int)o.size(); i++) if (o[i] == g_wikiSel) pos = i;
    for (int i = pos + dir; i >= 0 && i < (int)o.size(); i += dir) if (WikiHit(o[i])) return o[i];
    return -1;
}
static void WikiGoArt(int a) { if (!WikiArtShown(a)) return; g_wikiSel = a; g_scroll[TAB_WIKI] = 0; }
// Temps de lecture (minutes, 1 au moins) : environ 1100 caracteres par minute.
static int WikiMinutes(const WikiArticle &art)
{
    size_t n = 0;
    for (int i = 0; i < art.n; i++) if (!WikiBlockShown(art, i)) continue; else if (art.b[i].fr) n += wcslen(T(art.b[i].fr, art.b[i].en)); else n += 220;
    return max(1, (int)((n + 550) / 1100));
}
// Puce neutre de la maquette (.chip) : fond leger, lisere, texte presque blanc.
static void WikiChip(Graphics &g, RectF r, const std::wstring &s, float px = 11.5f, bool pink = false)
{
    GraphicsPath p; RoundRect(p, r, r.Height / 2);
    SolidBrush b(pink ? kWikiPink : g_dark ? Color(26, 255, 255, 255) : Color(28, 16, 30, 48)); g.FillPath(&b, &p);
    if (!pink) { Pen pen(g_dark ? Color(36, 255, 255, 255) : Color(44, 16, 30, 48), 1); g.DrawPath(&pen, &p); }
    Text(g, s, r, px, FontStyleBold, pink ? Color(255, 255, 255, 255) : WithA(kInk, 0.92f));
}

// ---- sommaire : en-tetes de categorie et articles a plat (maquette .toc) ; filtre par la recherche
static void DrawWikiList(Graphics &g)
{
    RectF lr(kWikiList.X + 10, kWikiList.Y + 12, kWikiList.Width - 20, kWikiList.Height - 24);
    const std::vector<int> &ord = WikiOrder();
    bool searching = !Trim(g_wikiSearch).empty();
    // mise en page : en-tete (10 de marge, 14 de texte, 4 dessous), articles de 36
    struct Row { int art, cat; float y; };
    std::vector<Row> rows;
    float y = 0, selY = -1;
    int lastCat = -1, found = 0;
    if (searching) y += 22;   // (la ligne « N articles »)
    for (int a : ord) {
        if (!WikiHit(a)) continue;
        found++;
        if (kWikiArt[a].cat != lastCat) { lastCat = kWikiArt[a].cat; rows.push_back({ -1, lastCat, y }); y += 28; }
        if (a == g_wikiSel) selY = y;
        rows.push_back({ a, lastCat, y }); y += 36;
    }
    float total = y, maxS = max(0.0f, total - lr.Height);
    if (g_wikiShown != g_wikiSel) {   // (autre article : le sommaire le montre)
        g_wikiShown = g_wikiSel;
        if (selY >= 0 && (selY < g_wikiListScroll || selY + 36 > g_wikiListScroll + lr.Height)) g_wikiListScroll = selY - lr.Height / 2 + 18;
    }
    g_wikiListScroll = min(max(g_wikiListScroll, 0.0f), maxS);
    GraphicsState st = g.Save();
    g.SetClip(lr, CombineModeIntersect);
    float y0 = lr.Y - g_wikiListScroll;
    if (searching) {
        wchar_t b[96];
        if (found) swprintf_s(b, found == 1 ? T(L"1 article · Échap : effacer", L"1 article · Esc: clear") : T(L"%d articles · Échap : effacer", L"%d articles · Esc: clear"), found);
        else swprintf_s(b, T(L"Rien pour « %s »", L"Nothing for \"%s\""), Trim(g_wikiSearch).c_str());
        Text(g, b, RectF(lr.X + 10, y0, lr.Width - 20, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    for (const Row &r : rows) {
        float ry = y0 + r.y;
        if (r.art < 0) {
            if (ry + 28 < lr.Y || ry > lr.Y + lr.Height) continue;
            Text(g, WikiUpper(T(kWikiCat[r.cat].fr, kWikiCat[r.cat].en)), RectF(lr.X + 10, ry + 10, lr.Width - 20, 14), 10.5f, FontStyleBold, kGrey, StringAlignmentNear);
            continue;
        }
        RectF ar(lr.X, ry, lr.Width, 36);
        if (ar.Y + ar.Height < lr.Y || ar.Y > lr.Y + lr.Height) continue;
        bool on = r.art == g_wikiSel, hot = g_uiHot == UI_WIKI_ART + r.art;
        if (on || hot) { GraphicsPath p; RoundRect(p, ar, 10); SolidBrush b(on ? WithA(kAcc, 0.18f) : TH(card)); g.FillPath(&b, &p); }
        Text(g, T(kWikiArt[r.art].tFr, kWikiArt[r.art].tEn), RectF(ar.X + 10, ar.Y, ar.Width - 20, ar.Height), 13.5f, FontStyleBold, on ? kInk : WithA(kInk, 0.85f), StringAlignmentNear);
        HotZone(RectF(ar.X, max(ar.Y, lr.Y), ar.Width, min(ar.Y + ar.Height, lr.Y + lr.Height) - max(ar.Y, lr.Y)), UI_WIKI_ART + r.art);
    }
    g.Restore(st);
    if (maxS > 0) {
        float bh = lr.Height * lr.Height / (lr.Height + maxS), by = lr.Y + (lr.Height - bh) * g_wikiListScroll / maxS;
        GraphicsPath sp; RoundRect(sp, RectF(lr.X + lr.Width - 3, by, 3, bh), 1.5f);
        SolidBrush sbr(WithA(kAcc, 0.45f)); g.FillPath(&sbr, &sp);
    }
}

// ---- colonne de droite : les trois dernieres versions (notes de GitHub, canal joue) et « Un souci ? »
// Titre = premiere puce de la note, detail = la deuxieme (NoteBullets, ui.inc).
static void DrawWikiNews(Graphics &g)
{
    RectF c = kWikiNews;
    Glass(c);
    float x = c.X + 16, w = c.Width - 32;
    Title(g, T(L"Nouveautés", L"What's new"), RectF(x, c.Y + 14, 160, 24), 16, kInk);
    {   // « Tout » : l'onglet Nouveautes
        const wchar_t *all = T(L"Tout", L"All");
        float aw = PillW(all, 12.5f);
        RectF r(x + w - aw, c.Y + 14, aw, 24);
        Text(g, all, r, 12.5f, FontStyleBold, g_uiHot == UI_WIKI_GO + 199 ? kAcc2 : kAcc, StringAlignmentFar);
        HotZone(r, UI_WIKI_GO + 199);
    }
    // « Un souci ? » en bas : le zip des journaux (icone page de l'en-tete) puis Discord
    std::wstring help = T(L"Journaux (l'icône page en haut à droite) > Faire le zip, puis poste-le dans #bug-reports sur Discord.",
                          L"Logs (the page icon at the top right) > Make the zip, then post it in #bug-reports on Discord.");
    float hh = WikiTextH(g, help, w - 24, 12, FontStyleRegular);
    RectF h(c.X + 14, 0, c.Width - 28, 10 + 18 + hh + 10 + 36 + 10);
    h.Y = c.Y + c.Height - 14 - h.Height;
    {
        GraphicsPath p; RoundRect(p, h, 14);
        SolidBrush fb(g_dark ? Color(13, 255, 255, 255) : Color(60, 255, 255, 255)); g.FillPath(&fb, &p);
        Pen pen(g_dark ? Color(31, 255, 255, 255) : TH(choiceBorder), 1); g.DrawPath(&pen, &p);
        Text(g, T(L"Un souci ?", L"Something wrong?"), RectF(h.X + 12, h.Y + 10, h.Width - 24, 18), 13, FontStyleBold, kInk, StringAlignmentNear);
        WikiText(g, help, RectF(h.X + 12, h.Y + 28, h.Width - 24, hh + 4), 12, FontStyleRegular, kGrey);
        RectF b(h.X + 12, h.Y + h.Height - 10 - 36, h.Width - 24, 36);
        GraphicsPath bp; RoundRect(bp, b, 11);
        SolidBrush db(g_uiHot == UI_WIKI_GO + 198 ? Mix(kDiscordBlue, Color(255, 255, 255, 255), 0.18f) : kDiscordBlue); g.FillPath(&db, &bp);
        float tw = MeasureW(MeasureG(), T(L"Ouvrir Discord", L"Open Discord"), 12.5f, FontStyleBold);
        Icon(g, IC_DISCORD, b.X + (b.Width - tw - 26) / 2 + 9, b.Y + 18, 17, Color(255, 255, 255, 255));
        Text(g, T(L"Ouvrir Discord", L"Open Discord"), RectF(b.X + (b.Width - tw - 26) / 2 + 26, b.Y, tw + 4, b.Height), 12.5f, FontStyleBold, Color(255, 255, 255, 255), StringAlignmentNear);
        HotZone(b, UI_WIKI_GO + 198);
    }
    std::vector<Note> notes;   // (deja triees pour le jeu choisi : ChannelNotes, launcher.cpp)
    EnterCriticalSection(&g_cs);
    notes = g_notes;
    LeaveCriticalSection(&g_cs);
    float y = c.Y + 44, bottom = h.Y - 10;
    if (notes.empty()) Text(g, !g_notesDone ? T(L"Chargement…", L"Loading…") : T(L"Pas de notes pour l'instant.", L"No notes yet."), RectF(x, y + 10, w, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    for (size_t i = 0; i < notes.size() && i < 3; i++) {
        const Note &n = notes[i];
        std::wstring ver = NoteShort(n);
        bool fresh = i == 0 && NoteCmp(n) > 0;
        std::vector<std::wstring> lines = NoteBullets(n, 2);
        std::wstring title = lines.empty() ? T(L"(sans détail)", L"(no detail)") : lines[0], sub = lines.size() > 1 ? lines[1] : L"";
        float cw = PillW(ver, 10) - 2, tx = x + cw + 12, tw = w - cw - 12;
        float sh = sub.empty() ? 0 : min(WikiTextH(g, sub, tw, 12, FontStyleRegular), 48.0f);
        float rh = 10 + 18 + (sub.empty() ? 0 : sh + 2) + 10;
        if (y + rh > bottom) break;
        WikiChip(g, RectF(x, y + 12, cw, 22), ver, 10, fresh);
        Text(g, title, RectF(tx, y + 10, tw, 18), 13, FontStyleBold, kInk, StringAlignmentNear);
        if (!sub.empty()) WikiText(g, sub, RectF(tx, y + 30, tw, sh), 12, FontStyleRegular, kGrey);
        y += rh;
        if (i + 1 < notes.size() && i + 1 < 3) { Pen sep(g_dark ? Color(18, 255, 255, 255) : TH(sep), 1); g.DrawLine(&sep, x, y, x + w, y); }
    }
}

// ---- article (maquette .art) : puces categorie + temps de lecture, titre, sous-titre, blocs, puis precedent / suivant
static void DrawWikiPage(Graphics &g)
{
    Glass(kWikiList);
    Glass(kWikiBody);
    DrawWikiNews(g);
    if (!WikiArtShown(g_wikiSel)) g_wikiSel = WikiOrder()[0];   // (article de l'autre jeu : le premier du sommaire)
    DrawWikiList(g);
    const WikiArticle &art = kWikiArt[g_wikiSel];
    RectF b = kWikiBody;
    float bx = b.X + 22, bw = b.Width - 44;
    std::wstring cat = T(kWikiCat[art.cat].fr, kWikiCat[art.cat].en);
    float cw = PillW(cat, 11.5f) + 4;
    WikiChip(g, RectF(bx, b.Y + 18, cw, 26), cat);
    Text(g, std::to_wstring(WikiMinutes(art)) + L" min", RectF(bx + cw + 10, b.Y + 18, 80, 26), 12, FontStyleRegular, kGrey, StringAlignmentNear);
    Title(g, T(art.tFr, art.tEn), RectF(bx, b.Y + 50, bw, 30), 22, kInk);
    Text(g, T(art.sFr, art.sEn), RectF(bx, b.Y + 82, bw, 18), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    // precedent / suivant, en bas
    {
        int prev = WikiNeighbor(-1), next = WikiNeighbor(1);
        float ly = b.Y + b.Height - 18 - 22;
        if (prev >= 0) {
            std::wstring s = L"← " + std::wstring(T(kWikiArt[prev].tFr, kWikiArt[prev].tEn));
            RectF r(bx, ly, min(bw / 2 - 6, MeasureW(MeasureG(), s, 12.5f, FontStyleBold) + 6), 22);
            Text(g, s, r, 12.5f, FontStyleBold, g_uiHot == UI_WIKI_PREV ? kAcc2 : kAcc, StringAlignmentNear);
            HotZone(r, UI_WIKI_PREV);
        }
        if (next >= 0) {
            std::wstring s = std::wstring(T(kWikiArt[next].tFr, kWikiArt[next].tEn)) + L" →";
            float sw = min(bw / 2 - 6, MeasureW(MeasureG(), s, 12.5f, FontStyleBold) + 6);
            RectF r(bx + bw - sw, ly, sw, 22);
            Text(g, s, r, 12.5f, FontStyleBold, g_uiHot == UI_WIKI_NEXT ? kAcc2 : kAcc, StringAlignmentFar);
            HotZone(r, UI_WIKI_NEXT);
        }
    }
    RectF area = WikiArea();
    float sc = g_scroll[TAB_WIKI], y = area.Y + 2 - sc, w = area.Width - 14;
    bool searching = !Trim(g_wikiSearch).empty();   // (recherche : rien de plie)
    Color ink = WithA(kInk, 0.92f);
    GraphicsState st = g.Save();
    g.SetClip(area, CombineModeIntersect);
    bool folded = false;
    int step = 0, sec = 0;
    auto vis = [&](float top, float h) { return top + h >= area.Y && top <= area.Y + area.Height; };
    for (int i = 0; i < art.n; i++) {
        if (!WikiBlockShown(art, i)) continue;   // (bloc de l'autre jeu)
        const WikiBlock &k = art.b[i];
        std::wstring t = k.fr ? T(k.fr, k.en) : L"";
        if (k.kind == WB_H) {   // h3 de la maquette : numero, capitales, accent ; clic : plier / deplier
            if (i > 0) y += 8;
            folded = !searching && g_wikiFold.count(g_wikiSel * 1000 + i) != 0;
            step = 0; sec++;
            RectF r(area.X, y, w, 24);
            if (vis(r.Y, r.Height)) {
                bool hot = g_uiHot == UI_WIKI_SEC + i;
                if (hot) { GraphicsPath p; RoundRect(p, RectF(r.X - 6, r.Y - 2, r.Width + 12, r.Height + 4), 8); SolidBrush hb(TH(card)); g.FillPath(&hb, &p); }
                Text(g, std::to_wstring(sec) + L". " + WikiUpper(t), RectF(r.X, r.Y, r.Width - 30, r.Height), 13.5f, FontStyleBold, kAcc, StringAlignmentNear);
                if (folded || hot) Icon(g, folded ? IC_CHEVR : IC_CHEVD, r.X + r.Width - 12, r.Y + 12, 14, folded ? kGrey : kAcc, 2.2f);
                HotZone(RectF(r.X, max(r.Y, area.Y), r.Width, min(r.Y + r.Height, area.Y + area.Height) - max(r.Y, area.Y)), UI_WIKI_SEC + i);
            }
            y += 30;
            continue;
        }
        if (folded) continue;
        switch (k.kind) {
        case WB_P: {
            float h = WikiTextH(g, t, w - 4, 13.5f, FontStyleRegular);
            if (vis(y, h)) WikiText(g, t, RectF(area.X + 2, y, w - 4, h + 2), 13.5f, FontStyleRegular, ink);
            y += h + 8;
            break;
        }
        case WB_B: {
            float h = WikiTextH(g, t, w - 24, 13.5f, FontStyleRegular);
            if (vis(y, h)) { SolidBrush d(kAcc); g.FillEllipse(&d, area.X + 6, y + 7, 6.0f, 6.0f); WikiText(g, t, RectF(area.X + 20, y, w - 24, h + 2), 13.5f, FontStyleRegular, ink); }
            y += h + 6;
            break;
        }
        case WB_S: {
            step++;
            float h = max(26.0f, WikiTextH(g, t, w - 40, 13.5f, FontStyleRegular));
            if (vis(y, h)) { WikiBadge(g, area.X + 13, y + 12, step); WikiText(g, t, RectF(area.X + 34, y + 2, w - 40, h + 2), 13.5f, FontStyleRegular, ink); }
            y += h + 10;
            break;
        }
        case WB_TIP: case WB_WARN: {   // .tip : fond et lisere accent, etoile doree ; attention : en orange
            Color c = k.kind == WB_TIP ? kAcc : kWarn;
            float th = WikiTextH(g, t, w - 52, 13, FontStyleBold), h = th + 20;
            RectF r(area.X, y, w, h);
            if (vis(r.Y, r.Height)) {
                GraphicsPath p; RoundRect(p, r, 12);
                SolidBrush fb(WithA(c, 0.10f)); g.FillPath(&fb, &p);
                Pen pen(WithA(c, 0.3f), 1); g.DrawPath(&pen, &p);
                Icon(g, k.kind == WB_TIP ? IC_STAR : IC_ALERT, r.X + 21, r.Y + 19, 18, k.kind == WB_TIP ? kWikiGold : kWarn, 2.0f);
                WikiText(g, t, RectF(r.X + 40, r.Y + 10, w - 52, th + 2), 13, FontStyleBold, kInk);
            }
            y += h + 10;
            break;
        }
        case WB_FIG: {
            RectF r(area.X, y, w, 0);
            float h = WikiFigure(g, k.arg, r, false);
            r.Height = h + 16;
            if (vis(r.Y, r.Height)) {
                GraphicsPath p; RoundRect(p, r, 14);
                SolidBrush fb(WithA(TH(field), 0.55f)); g.FillPath(&fb, &p);
                Pen pen(TH(choiceBorder), 1); g.DrawPath(&pen, &p);
                WikiFigure(g, k.arg, r, true);
            }
            y += r.Height + 12;
            break;
        }
        case WB_GO: {
            RectF r(area.X, y + 2, min(w, PillW(t, 13) + 60), 40);
            if (vis(r.Y, r.Height)) UiButton(g, r, t, UI_WIKI_GO + i, false, IC_CHEVR);
            y += 52;
            break;
        }
        }
    }
    g.Restore(st);
    g_wikiH = y + sc - area.Y;
    float ms = WikiMaxScroll();
    if (ms > 0) {
        float bh = area.Height * area.Height / (area.Height + ms), by = area.Y + (area.Height - bh) * min(sc, ms) / ms;
        GraphicsPath sp; RoundRect(sp, RectF(area.X + area.Width - 5, by, 4, bh), 2);
        SolidBrush sbr(WithA(kAcc, 0.5f)); g.FillPath(&sbr, &sp);
    }
}

// ---- en-tete (ui.inc DrawHeader) : champ Chercher puis pilules Guide / Nouveautes (version pas encore lue en rose) / Credits
static const wchar_t *GuideTabLabel(int k) { return k == 0 ? T(L"Guide", L"Guide") : k == 1 ? T(L"Nouveautés", L"What's new") : T(L"Crédits", L"Credits"); }
static std::wstring GuideNewChip()
{
    if (!NotesUnseen()) return L"";
    std::wstring v = NewestNote();
    size_t d = v.find_first_of(L"0123456789");   // (« lanceur-1.15.1 » : le numero seul)
    v = d == std::wstring::npos ? L"" : v.substr(d);
    return v.substr(0, v.find(L'-'));
}
static float GuideTabW(int k)
{
    float w = PillW(GuideTabLabel(k), 13) + 12;   // (14 de chaque cote)
    if (k == 1) { std::wstring c = GuideNewChip(); if (!c.empty()) w += PillW(c, 9.5f) - 2 + 6; }
    return w;
}
static float GuideTabsW() { return GuideTabW(0) + GuideTabW(1) + GuideTabW(2) + 12; }
static void GuideTabsDraw(Graphics &g)
{
    int sel = g_tab == TAB_NOTES ? 1 : g_tab == TAB_CREDITS ? 2 : 0;
    float x = g_guideTabRight - GuideTabsW();
    for (int k = 0; k < 3; k++) {
        float w = GuideTabW(k);
        RectF r(x, 26, w, 36);
        bool on = k == sel, hot = g_uiHot == UI_WIKI_TAB + k;
        GraphicsPath p; RoundRect(p, r, r.Height / 2);
        SolidBrush b(on ? kAcc : hot ? TH(cardSel) : TH(btn2)); g.FillPath(&b, &p);
        if (!on) { Pen pen(TH(fieldBorder), 1); g.DrawPath(&pen, &p); }
        std::wstring chip = k == 1 ? GuideNewChip() : L"";
        float tw = PillW(GuideTabLabel(k), 13) - 16;
        Text(g, GuideTabLabel(k), RectF(r.X + 14, r.Y, tw + 4, r.Height), 13, FontStyleBold, on ? kOnAcc : kInk, StringAlignmentNear);
        if (!chip.empty()) WikiChip(g, RectF(r.X + 14 + tw + 6, r.Y + 9, PillW(chip, 9.5f) - 2, 18), chip, 9.5f, true);
        HotZone(r, UI_WIKI_TAB + k);
        if (k == 2) g_credBtnRect = r;
        x += w + 6;
    }
}
static void DrawGuideSearchBox(Graphics &g, float &right)
{
    // (124 px depuis le raccourci Outils de l'en-tete, 20/10 : a 164, le titre « Guide » etait coupe)
    RectF r(right - 124, 22, 124, 44);
    GraphicsPath p; RoundRect(p, r, 14);
    SolidBrush fill(TH(field)); g.FillPath(&fill, &p);
    Pen pen(g_wikiFocus ? kAcc : TH(fieldBorder), g_wikiFocus ? 2.0f : 1.2f); g.DrawPath(&pen, &p);
    RectF tr(r.X + 16, r.Y, r.Width - 16 - 34, r.Height);
    if (g_wikiSearch.empty() && !g_wikiFocus) Text(g, T(L"Chercher…", L"Search…"), tr, 13.5f, FontStyleBold, TH(placeholder), StringAlignmentNear);
    else {
        Text(g, g_wikiSearch, tr, 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        if (g_wikiFocus && fmodf(g_time, 1.0f) < 0.55f) {
            float cx = min(tr.X + MeasureW(MeasureG(), g_wikiSearch, 13.5f, FontStyleBold) + 1, tr.X + tr.Width);
            Pen cp(kAcc, 1.6f); g.DrawLine(&cp, cx, r.Y + 12, cx, r.Y + r.Height - 12);
        }
    }
    HotZone(r, UI_WIKI_SEARCH);
    if (!g_wikiSearch.empty()) {   // (zone declaree apres : elle gagne)
        RectF xr(r.X + r.Width - 34, r.Y + 8, 28, 28);
        Icon(g, IC_CLOSE, xr.X + 14, xr.Y + 14, 14, g_uiHot == UI_WIKI_SEARCHX ? kAcc : kGrey, 2.0f);
        HotZone(xr, UI_WIKI_SEARCHX);
    }
    right -= 134;
}
// Commandes des trois onglets du Guide, de droite a gauche : Chercher, puis les pilules.
static void DrawGuideHead(Graphics &g, float &right)
{
    DrawGuideSearchBox(g, right);
    g_guideTabRight = right;
    GuideTabsDraw(g);
    right -= GuideTabsW() + 10;
}
// Caractere tape dans Chercher (WM_CHAR, launcher.cpp) ; vrai si pris. Depuis Nouveautes ou Credits : retour au Guide.
static bool GuideChar(wchar_t c)
{
    if (!g_wikiFocus || g_state != ST_IDLE || (g_tab != TAB_WIKI && g_tab != TAB_NOTES && g_tab != TAB_CREDITS)) return false;
    if (c == 27) { g_wikiSearch.clear(); g_wikiFocus = false; }
    else if (c == 8) { if (!g_wikiSearch.empty()) g_wikiSearch.pop_back(); }
    else if (c >= 32 && g_wikiSearch.size() < 40) g_wikiSearch += c;
    else return true;
    if (g_tab != TAB_WIKI) GoPage(TAB_WIKI);
    if (!WikiHit(g_wikiSel)) for (int a : WikiOrder()) if (WikiHit(a)) { WikiGoArt(a); break; }   // (le premier qui correspond)
    g_time = 0.2f;
    return true;
}

// Onglet Mods, bouton « Mods pris en charge / incompatibilites connues » (mods.inc) : cet article.
static void MscOpenCompat()
{
    for (int i = 0; i < kWikiArts; i++)
        if (kWikiArt[i].b == kW_ModCompat) {
            GoPage(TAB_WIKI);
            g_wikiSearch.clear();
            WikiGoArt(i);
            return;
        }
}

static bool WikiClick(int id)
{
    if (id != UI_WIKI_SEARCH) g_wikiFocus = false;
    if (id >= UI_WIKI_TAB && id < UI_WIKI_TAB + 3) { GoPage(id == UI_WIKI_TAB ? TAB_WIKI : id == UI_WIKI_TAB + 1 ? TAB_NOTES : TAB_CREDITS); return true; }
    if (id == UI_WIKI_SEARCH) { g_wikiFocus = true; g_time = 0.2f; if (g_tab != TAB_WIKI) GoPage(TAB_WIKI); return true; }
    if (id == UI_WIKI_SEARCHX) { g_wikiSearch.clear(); return true; }
    if (id == UI_WIKI_PREV || id == UI_WIKI_NEXT) { WikiGoArt(WikiNeighbor(id == UI_WIKI_PREV ? -1 : 1)); return true; }
    if (id >= UI_WIKI_ART && id < UI_WIKI_ART + 200) { WikiGoArt(id - UI_WIKI_ART); return true; }
    if (id >= UI_WIKI_SEC && id < UI_WIKI_SEC + 200) {
        int key = g_wikiSel * 1000 + (id - UI_WIKI_SEC);
        if (g_wikiFold.count(key)) g_wikiFold.erase(key); else g_wikiFold.insert(key);
        g_scroll[TAB_WIKI] = min(g_scroll[TAB_WIKI], WikiMaxScroll());
        return true;
    }
    if (id == UI_WIKI_GO + 199) { GoPage(TAB_NOTES); return true; }   // colonne Nouveautes : « Tout »
    if (id == UI_WIKI_GO + 197) { GoPage(TAB_CREDITS); return true; }
    if (id == UI_WIKI_GO + 198) { ShellExecuteW(g_wnd, L"open", kDiscordUrl, NULL, NULL, SW_SHOWNORMAL); return true; }
    if (id >= UI_WIKI_GO && id < UI_WIKI_GO + 200) {
        const WikiArticle &art = kWikiArt[g_wikiSel];
        int i = id - UI_WIKI_GO;
        if (i < art.n && art.b[i].kind == WB_GO) {
            int p = art.b[i].arg;
            if (p == 2000) { GoPage(TAB_CAR); g_addonsTab = true; g_addonSel = -1; g_addonScanT = 0; }   // (onglet Addons de la page Personnage)
            else if (p >= 1000) WikiGoArt(p - 1000);   // (autre article du guide)
            else if (PageEnabled(p)) GoPage(p);
        }
        return true;
    }
    return id == UI_WIKI_FIG || (id >= UI_WIKI_CAT && id < UI_WIKI_CAT + 32);
}
