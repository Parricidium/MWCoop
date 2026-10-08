// ---------------------------------------------------------------- page GUIDE : petit wiki des joueurs - inclus par launcher.cpp
// Demande de JD (08/10) : tout ce qu'il faut pour jouer sans probleme (installation, MSCLoader officiel / de MWCoop,
// partage des mods, options, Copie / Dossier / Steam...), range en categories pliables, avec des mini tutos en images.
// A gauche les categories (clic : plier / deplier) et leurs articles ; a droite l'article : sections pliables, etapes
// numerotees, encadres (astuce, attention), figures dessinees avec les elements du lanceur (a jour avec le theme et la
// langue, sans image a telecharger) et boutons qui ouvrent la page concernee. Contenu : wikidata.inc.
enum { WB_H, WB_P, WB_B, WB_S, WB_TIP, WB_WARN, WB_FIG, WB_GO };
struct WikiBlock { int kind; const wchar_t *fr, *en; int arg; };   // arg : figure (WB_FIG), page (WB_GO)
struct WikiArticle { int cat; const wchar_t *tFr, *tEn, *sFr, *sEn; const WikiBlock *b; int n; };
struct WikiCat { int icon; const wchar_t *fr, *en; };
enum { WF_FOLDER, WF_FOOTER, WF_START, WF_NET, WF_LOBBY, WF_MSC, WF_SHARE, WF_ACCEPT, WF_LOGS, WF_UPDATE, WF_KEYS, WF_GUEST, WF_CONTENT, WF_BAD };

#include "wikidata.inc"

static const int kWikiCats = (int)(sizeof(kWikiCat) / sizeof(kWikiCat[0]));
static const int kWikiArts = (int)(sizeof(kWikiArt) / sizeof(kWikiArt[0]));
static const RectF kWikiList(264, 88, 300, 656), kWikiBody(580, 88, 684, 656);
static int g_wikiSel;
static unsigned g_wikiClosed = ~1u;           // categories pliees (bit par categorie) : au depart, seule la premiere est ouverte
static std::set<int> g_wikiFold;              // sections pliees : article * 1000 + bloc
static float g_wikiListScroll, g_wikiH;
static RectF WikiArea() { return RectF(kWikiBody.X + 28, kWikiBody.Y + 96, kWikiBody.Width - 46, kWikiBody.Height - 110); }
static float WikiMaxScroll() { return max(0.0f, g_wikiH - WikiArea().Height); }

static float WikiTextH(Graphics &g, const std::wstring &t, float w, float px, int style)
{
    FontFamily fam(L"Segoe UI");
    Font f(&fam, px, style, UnitPixel);
    StringFormat sf;
    RectF box;
    g.MeasureString(t.c_str(), -1, &f, RectF(0, 0, w, 100000), &sf, &box);
    return box.Height;
}
static void WikiText(Graphics &g, const std::wstring &t, RectF r, float px, int style, Color c)
{
    FontFamily fam(L"Segoe UI");
    Font f(&fam, px, style, UnitPixel);
    StringFormat sf;
    SolidBrush b(c);
    g.DrawString(t.c_str(), -1, &f, r, &sf, &b);
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
        Text(g, L"…\\steamapps\\common\\My Winter Car\\", RectF(x, y, w, 22), 12, FontStyleBold, kGrey, StringAlignmentNear);
        y += 28;
        WikiFileRow(g, x, y, w, IC_PLAY, L"mywintercar.exe", T(L"LE JEU", L"THE GAME"), kGrey); y += 28;
        WikiFileRow(g, x, y, w, IC_HOME, L"MWCoop.exe", T(L"LANCEUR", L"LAUNCHER"), kAcc); y += 28;
        WikiFileRow(g, x, y, w, IC_CODE, L"version.dll", T(L"CHARGEUR MWCOOP", L"MWCOOP LOADER"), kAcc); y += 28;
        WikiFileRow(g, x, y, w, IC_FOLDER, L"MWCoop\\", T(L"LE MOD, PROFILS, JOURNAUX", L"THE MOD, PROFILES, LOGS"), kAcc); y += 28;
        WikiFileRow(g, x, y, w, IC_FOLDER, L"Mods\\", T(L"TES MODS (MSCLOADER)", L"YOUR MODS (MSCLOADER)"), kOk); y += 28;
        WikiFileRow(g, x, y, w, IC_CODE, L"winhttp.dll + doorstop_config.ini", T(L"MSCLOADER OFFICIEL (OPTIONNEL)", L"OFFICIAL MSCLOADER (OPTIONAL)"), kOk); y += 28;
        WikiFileRow(g, x, y, w, IC_FOLDER, L"mywintercar_Data\\", T(L"DONNÉES DU JEU", L"GAME DATA"), kGrey);
        return 262;
    }
    case WF_FOOTER: {   // pied du lanceur : jeu trouve, changer de dossier
        if (!draw) return 118;
        RectF bar(x, y, w, 40);
        WikiCard(g, bar);
        Icon(g, IC_CHECK, bar.X + 22, bar.Y + 20, 16, kOk, 2.4f);
        Text(g, L"My Winter Car", RectF(bar.X + 40, bar.Y, 160, 40), 13, FontStyleBold, kInk, StringAlignmentNear);
        Text(g, L"C:\\…\\steamapps\\common\\My Winter Car\\", RectF(bar.X + 160, bar.Y, w - 300, 40), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
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
        if (const Opt *ls = OptByKey("LancerSteam")) how = OptGet(*ls) ? 2 : GetPrivateProfileIntW(L"Lanceur", L"SansCopie", 0, g_iniLauncher.c_str()) ? 1 : 0;
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
    case WF_ACCEPT: {   // Reglages > Chez un ami : accepter l'envoi de l'hote
        if (!draw) return 96;
        RectF row(x, y, w, 60);
        WikiCard(g, row);
        bool on = false;
        if (const Opt *ma = OptByKey("ModsAuto")) on = OptGet(*ma) != 0;
        SetRowText(g, RectF(row.X + 16, row.Y, row.Width - 90, row.Height), T(L"Accepter l'envoi de l'hôte", L"Accept the host's files"), T(L"Ses mods, CD et images, copiés à part, sans demander", L"Their mods, CDs and images, copied apart, without asking"));
        UiToggle(g, RectF(row.X + row.Width - 66, row.Y + 16, 50, 28), on, false);
        HotZone(row, Z);
        Text(g, T(L"Réglages > Chez un ami (ton réglage actuel)", L"Settings > At a friend's (your current setting)"), RectF(x, y + 66, w, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
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
        float h = 8 + kWikiKeysN * 34.0f;
        if (!draw) return h + 16;
        for (int i = 0; i < kWikiKeysN; i++) {
            float ky = y + i * 34.0f;
            float kw = max(44.0f, PillW(kWikiKeys[i].key, 12) + 8);
            RectF k(x, ky, kw, 28);
            GraphicsPath p; RoundRect(p, k, 7);
            SolidBrush kb(TH(field)); g.FillPath(&kb, &p);
            Pen kp(TH(fieldBorder), 1.2f); g.DrawPath(&kp, &p);
            Pen kd(TH(fieldBorder), 2.4f); g.DrawLine(&kd, k.X + 6, k.Y + k.Height, k.X + k.Width - 6, k.Y + k.Height);
            Text(g, kWikiKeys[i].key, k, 12, FontStyleBold, kInk);
            Text(g, g_fr ? kWikiKeys[i].fr : kWikiKeys[i].en, RectF(x + 110, ky, w - 110, 28), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
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
            Text(g, g_fr ? bad[i].fr : bad[i].en, RectF(x + 56, ry, w - 56, 26), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
        }
        return 152;
    }
    }
    return 0;
}

// ---- liste des categories et articles
static void DrawWikiList(Graphics &g)
{
    RectF lr(kWikiList.X + 10, kWikiList.Y + 10, kWikiList.Width - 20, kWikiList.Height - 20);
    // hauteur totale (pour borner le defilement)
    float total = 0;
    for (int c = 0; c < kWikiCats; c++) {
        total += 46;
        if (!(g_wikiClosed & (1u << c))) for (int a = 0; a < kWikiArts; a++) if (kWikiArt[a].cat == c) total += 38;
    }
    g_wikiListScroll = min(max(g_wikiListScroll, 0.0f), max(0.0f, total - lr.Height));
    GraphicsState st = g.Save();
    g.SetClip(lr, CombineModeIntersect);
    float y = lr.Y - g_wikiListScroll;
    for (int c = 0; c < kWikiCats; c++) {
        bool open = !(g_wikiClosed & (1u << c)), hot = g_uiHot == UI_WIKI_CAT + c;
        int n = 0;
        for (int a = 0; a < kWikiArts; a++) if (kWikiArt[a].cat == c) n++;
        RectF r(lr.X, y, lr.Width, 42);
        if (hot) { GraphicsPath p; RoundRect(p, r, 12); SolidBrush b(TH(card)); g.FillPath(&b, &p); }
        Icon(g, open ? IC_CHEVD : IC_CHEVR, r.X + 16, r.Y + 21, 14, kGrey, 2.2f);
        Icon(g, kWikiCat[c].icon, r.X + 40, r.Y + 21, 18, kAcc, 1.9f);
        Text(g, g_fr ? kWikiCat[c].fr : kWikiCat[c].en, RectF(r.X + 58, r.Y, r.Width - 96, r.Height), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
        std::wstring ns = std::to_wstring(n);
        float pw = PillW(ns, 10);
        Pill(g, RectF(r.X + r.Width - pw - 8, r.Y + 11, pw, 20), TH(cardSel), ns, kGrey, 10);
        HotZone(r, UI_WIKI_CAT + c);
        y += 46;
        if (!open) continue;
        for (int a = 0; a < kWikiArts; a++) {
            if (kWikiArt[a].cat != c) continue;
            RectF ar(lr.X + 26, y, lr.Width - 26, 34);
            bool on = a == g_wikiSel, ah = g_uiHot == UI_WIKI_ART + a;
            if (on || ah) { GraphicsPath p; RoundRect(p, ar, 10); SolidBrush b(on ? TH(cardSel) : TH(card)); g.FillPath(&b, &p); }
            if (on) { SolidBrush d(kAcc); g.FillEllipse(&d, ar.X + 10, ar.Y + 14, 6.0f, 6.0f); }
            Text(g, g_fr ? kWikiArt[a].tFr : kWikiArt[a].tEn, RectF(ar.X + 24, ar.Y, ar.Width - 30, ar.Height), 12.5f, on ? FontStyleBold : FontStyleRegular, on ? kInk : WithA(kInk, 0.85f), StringAlignmentNear);
            HotZone(ar, UI_WIKI_ART + a);
            y += 38;
        }
    }
    g.Restore(st);
}

// ---- article
static void DrawWikiPage(Graphics &g)
{
    Glass(kWikiList);
    Glass(kWikiBody);
    if (g_wikiSel < 0 || g_wikiSel >= kWikiArts) g_wikiSel = 0;
    DrawWikiList(g);
    const WikiArticle &art = kWikiArt[g_wikiSel];
    RectF b = kWikiBody;
    SmallLabel(g, g_fr ? kWikiCat[art.cat].fr : kWikiCat[art.cat].en, RectF(b.X + 28, b.Y + 18, 400, 18), kAcc);
    Title(g, g_fr ? art.tFr : art.tEn, RectF(b.X + 28, b.Y + 34, b.Width - 56, 34), 24, kInk);
    Text(g, g_fr ? art.sFr : art.sEn, RectF(b.X + 28, b.Y + 66, b.Width - 56, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    Pen sep(TH(sep), 1); g.DrawLine(&sep, b.X + 28, b.Y + 90, b.X + b.Width - 28, b.Y + 90);
    RectF area = WikiArea();
    float sc = g_scroll[TAB_WIKI], y = area.Y + 8 - sc, w = area.Width - 14;
    GraphicsState st = g.Save();
    g.SetClip(area, CombineModeIntersect);
    bool folded = false;
    int step = 0;
    auto vis = [&](float top, float h) { return top + h >= area.Y && top <= area.Y + area.Height; };
    for (int i = 0; i < art.n; i++) {
        const WikiBlock &k = art.b[i];
        std::wstring t = k.fr ? (g_fr ? k.fr : k.en) : L"";
        if (k.kind == WB_H) {
            if (i > 0) y += 6;
            folded = g_wikiFold.count(g_wikiSel * 1000 + i) != 0;
            step = 0;
            RectF r(area.X, y, w, 40);
            if (vis(r.Y, r.Height)) {
                bool hot = g_uiHot == UI_WIKI_SEC + i;
                GraphicsPath p; RoundRect(p, r, 12);
                SolidBrush hb(hot ? TH(cardSel) : TH(card)); g.FillPath(&hb, &p);
                Pen pen(hot ? WithA(kAcc, 0.6f) : TH(choiceBorder), 1); g.DrawPath(&pen, &p);
                Icon(g, folded ? IC_CHEVR : IC_CHEVD, r.X + 20, r.Y + 20, 15, kAcc, 2.3f);
                Text(g, t, RectF(r.X + 38, r.Y, r.Width - 50, r.Height), 14.5f, FontStyleBold, kInk, StringAlignmentNear);
                HotZone(RectF(r.X, max(r.Y, area.Y), r.Width, min(r.Y + r.Height, area.Y + area.Height) - max(r.Y, area.Y)), UI_WIKI_SEC + i);
            }
            y += 50;
            continue;
        }
        if (folded) continue;
        switch (k.kind) {
        case WB_P: {
            float h = WikiTextH(g, t, w - 8, 13.5f, FontStyleRegular);
            if (vis(y, h)) WikiText(g, t, RectF(area.X + 4, y, w - 8, h + 2), 13.5f, FontStyleRegular, kInk);
            y += h + 10;
            break;
        }
        case WB_B: {
            float h = WikiTextH(g, t, w - 30, 13.5f, FontStyleRegular);
            if (vis(y, h)) { SolidBrush d(kAcc); g.FillEllipse(&d, area.X + 10, y + 7, 7.0f, 7.0f); WikiText(g, t, RectF(area.X + 26, y, w - 30, h + 2), 13.5f, FontStyleRegular, kInk); }
            y += h + 8;
            break;
        }
        case WB_S: {
            step++;
            float h = max(26.0f, WikiTextH(g, t, w - 44, 13.5f, FontStyleRegular));
            if (vis(y, h)) { WikiBadge(g, area.X + 15, y + 12, step); WikiText(g, t, RectF(area.X + 38, y + 2, w - 44, h + 2), 13.5f, FontStyleRegular, kInk); }
            y += h + 10;
            break;
        }
        case WB_TIP: case WB_WARN: {
            Color c = k.kind == WB_TIP ? kOk : kWarn;
            float h = WikiTextH(g, t, w - 64, 13, FontStyleRegular) + 24;
            RectF r(area.X, y, w, h);
            if (vis(r.Y, r.Height)) {
                GraphicsPath p; RoundRect(p, r, 12);
                SolidBrush fb(WithA(c, 0.13f)); g.FillPath(&fb, &p);
                Pen pen(WithA(c, 0.55f), 1); g.DrawPath(&pen, &p);
                SolidBrush bar(c); g.FillRectangle(&bar, RectF(r.X, r.Y + 10, 4, r.Height - 20));
                Icon(g, k.kind == WB_TIP ? IC_SPARK : IC_ALERT, r.X + 26, r.Y + 22, 18, c, 2.0f);
                WikiText(g, t, RectF(r.X + 48, r.Y + 12, w - 64, h), 13, FontStyleRegular, kInk);
            }
            y += h + 12;
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
            y += r.Height + 14;
            break;
        }
        case WB_GO: {
            RectF r(area.X, y, min(w, PillW(t, 13) + 60), 40);
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

static bool WikiClick(int id)
{
    if (id >= UI_WIKI_CAT && id < UI_WIKI_CAT + 32) { g_wikiClosed ^= 1u << (id - UI_WIKI_CAT); return true; }
    if (id >= UI_WIKI_ART && id < UI_WIKI_ART + 200) { g_wikiSel = id - UI_WIKI_ART; g_scroll[TAB_WIKI] = 0; return true; }
    if (id >= UI_WIKI_SEC && id < UI_WIKI_SEC + 200) {
        int key = g_wikiSel * 1000 + (id - UI_WIKI_SEC);
        if (g_wikiFold.count(key)) g_wikiFold.erase(key); else g_wikiFold.insert(key);
        g_scroll[TAB_WIKI] = min(g_scroll[TAB_WIKI], WikiMaxScroll());
        return true;
    }
    if (id >= UI_WIKI_GO && id < UI_WIKI_GO + 200) {
        const WikiArticle &art = kWikiArt[g_wikiSel];
        int i = id - UI_WIKI_GO;
        if (i < art.n && art.b[i].kind == WB_GO) {
            int p = art.b[i].arg;
            if (p >= 1000) { g_wikiSel = p - 1000; g_scroll[TAB_WIKI] = 0; g_wikiClosed &= ~(1u << kWikiArt[g_wikiSel].cat); }   // (autre article du guide, sa categorie ouverte)
            else if (PageEnabled(p)) GoPage(p);
        }
        return true;
    }
    return id == UI_WIKI_FIG;
}
