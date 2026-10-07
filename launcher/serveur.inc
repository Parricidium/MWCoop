// ---------------------------------------------------------------- partie en cours : le lanceur reste ouvert - inclus par launcher.cpp
// Demande de JD (07/10) : le lanceur ne se ferme plus quand le jeu demarre (option COOP "Fermer le lanceur au lancement
// du jeu", FermerLanceur dans [Coop] de mwcoop.ini, desactivee par defaut). Etat ST_RUNNING : a gauche la partie en
// cours, a droite l'etat que le mod ecrit chaque seconde (Net/Admin.cs : serveur.txt dans son dossier de donnees, celui
// du profil invite pour un invite) : joueurs, ping, en partie ou au menu. Hote : FAIRE VENIR (le joueur arrive pres de
// lui) et EXCLURE (deux clics), passes au mod par commandes.txt. Jeu ferme : retour au menu (et verification d'un arret
// brutal, comme au demarrage).
struct SrvPlayer { int id; std::wstring name; std::string skin; int ping; bool inGame, me; };
static std::vector<SrvPlayer> g_srv;
static bool g_srvHost, g_srvSteam, g_srvFresh;
static std::wstring g_srvStatus, g_srvFile;
static int g_launchMode = MODE_SOLO;
static DWORD g_srvReadT, g_runCheckT;
static int g_srvHot = -1, g_kickArmed = -1;
static DWORD g_kickArmedT;

// Dossier des donnees du mod pour ce lancement (dossier du jeu, sinon %LOCALAPPDATA%\MWCoop si le jeu est en lecture seule).
static std::vector<std::wstring> SrvDirs()
{
    std::wstring sub = g_launchMode == MODE_GUEST ? L"profils\\invite\\" : L"";
    std::vector<std::wstring> d = { g_gameDir + L"MWCoop\\" + sub };
    if (!LocalDir().empty()) d.push_back(LocalDir() + sub);
    return d;
}

static void SrvRead()
{
    g_srvReadT = GetTickCount();
    std::wstring best;
    long long bt = 0;
    for (auto &d : SrvDirs()) { long long t = FileUnixTime(d + L"serveur.txt"); if (t > bt) { bt = t; best = d + L"serveur.txt"; } }
    g_srvFile = best;
    g_srvFresh = !best.empty() && (long long)_time64(NULL) - bt < 6;
    if (!g_srvFresh) return;
    std::vector<unsigned char> raw;
    if (!ReadAll(best, raw)) return;
    std::wstring all = Widen(std::string(raw.begin(), raw.end()), CP_UTF8);
    std::vector<SrvPlayer> list;
    size_t a = 0;
    while (a < all.size()) {
        size_t b = all.find(L'\n', a);
        std::wstring l = all.substr(a, b == std::wstring::npos ? std::wstring::npos : b - a);
        a = b == std::wstring::npos ? all.size() : b + 1;
        std::vector<std::wstring> f = SplitTab(l);
        if (f[0] == L"etat" && f.size() >= 4) { g_srvHost = f[1] == L"1"; g_srvSteam = f[2] == L"1"; g_srvStatus = f[3]; }
        else if (f[0] == L"joueur" && f.size() >= 8) {
            SrvPlayer p;
            p.id = _wtoi(f[1].c_str()); p.name = f[2]; p.ping = _wtoi(f[3].c_str()); p.inGame = f[4] == L"1"; p.skin = Narrow(f[6]); p.me = f[7] == L"1";
            list.push_back(p);
        }
    }
    std::sort(list.begin(), list.end(), [](const SrvPlayer &x, const SrvPlayer &y) { return x.id < y.id; });
    g_srv = list;
}

static void SrvCommand(const char *cmd, int id)
{
    if (g_srvFile.empty()) return;
    std::wstring path = g_srvFile.substr(0, g_srvFile.find_last_of(L'\\') + 1) + L"commandes.txt";
    FILE *f = _wfopen(path.c_str(), L"ab");
    if (f) { fprintf(f, "%s\t%d\n", cmd, id); fclose(f); }
    LaunchLog("serveur : %s %d", cmd, id);
}

static void RunStart(int mode)
{
    g_launchMode = mode;
    g_state = ST_RUNNING;
    g_srv.clear();
    g_srvFresh = false;
    g_srvReadT = g_runCheckT = 0;
    g_kickArmed = -1;
    g_tab = -1;
    LaunchLog("jeu demarre : le lanceur reste ouvert (mode %d)", mode);
}

static void RunTick()
{
    if (g_state != ST_RUNNING) return;
    DWORD now = GetTickCount();
    if (now - g_srvReadT >= 1000) SrvRead();
    if (now - g_runCheckT < 1500) return;
    g_runCheckT = now;
    if (g_proc && WaitForSingleObject(g_proc, 0) == WAIT_OBJECT_0) { CloseHandle(g_proc); g_proc = NULL; }
    if (g_proc || GameProcessRunning()) return;
    // jeu ferme : retour au menu
    g_state = ST_IDLE;
    g_srv.clear();
    LayoutTabs();
    CrashCheck();
    if (g_crashT) SetStatus(K_WARN, T(L"Arrêt brutal du jeu : voir les journaux", L"The game stopped abruptly: see the logs"));
    else SetStatus(K_NORMAL, T(L"Partie terminée · %s", L"Game over · %s"), ModLabel().c_str());
    LaunchLog("jeu ferme : retour au menu (arret brutal %d)", (int)(g_crashT != 0));
}

// ---- dessin et clics
static const RectF kSrvList(452, 176, 488, 330);
static const float kSrvRowH = 60;
static RectF SrvBtn(RectF r, int k) { return k == 0 ? RectF(r.X + r.Width - 214, r.Y + (r.Height - 26) / 2, 106, 26) : RectF(r.X + r.Width - 102, r.Y + (r.Height - 26) / 2, 94, 26); }
static RectF SrvRow(int i) { return RectF(kSrvList.X + 4, kSrvList.Y + i * kSrvRowH, kSrvList.Width - 8, kSrvRowH - 6); }

// 1000+i*2+k : bouton k (0 faire venir, 1 exclure) du joueur i
static int SrvAt(float x, float y)
{
    if (g_state != ST_RUNNING || !g_srvHost || !g_srvFresh) return -1;
    for (int i = 0; i < (int)g_srv.size(); i++) {
        if (g_srv[i].me) continue;
        RectF r = SrvRow(i);
        if (r.Y + r.Height > kSrvList.Y + kSrvList.Height) break;
        for (int k = 0; k < 2; k++) if (SrvBtn(r, k).Contains(x, y) && (k == 1 || g_srv[i].inGame)) return 1000 + i * 2 + k;
    }
    return -1;
}

static bool SrvClick(float x, float y)
{
    int h = SrvAt(x, y);
    if (h < 0) return g_state == ST_RUNNING && kOptPanel.Contains(x, y);
    const SrvPlayer &p = g_srv[(h - 1000) / 2];
    if ((h - 1000) % 2 == 0) { SrvCommand("venir", p.id); SetStatus(K_OK, T(L"%s arrive près de toi", L"%s is coming to you"), p.name.c_str()); }
    else if (g_kickArmed == p.id && GetTickCount() - g_kickArmedT < 4000) { SrvCommand("kick", p.id); g_kickArmed = -1; SetStatus(K_WARN, T(L"%s exclu de la partie", L"%s kicked from the game"), p.name.c_str()); }
    else { g_kickArmed = p.id; g_kickArmedT = GetTickCount(); }
    return true;
}

static void DrawServer(Graphics &g)
{
    DrawPanel(g);
    SkinsCheck();
    const wchar_t *title = !g_srvFresh ? T(L"PARTIE", L"GAME") : g_srvHost ? T(L"SERVEUR", L"SERVER") : T(L"PARTIE", L"GAME");
    Text(g, title, RectF(460, 124, 300, 26), 17, FontStyleBold, kInk, StringAlignmentNear);
    if (g_srvFresh) {
        wchar_t head[64];
        swprintf_s(head, T(L"%d joueur(s)", L"%d player(s)"), (int)g_srv.size());
        Text(g, head, RectF(700, 124, 232, 26), 12.5f, FontStyleBold, kGrey, StringAlignmentFar);
        std::wstring sub = g_srvStatus + (g_srvSteam ? T(L" · par Steam", L" · through Steam") : L"");
        Text(g, sub, RectF(460, 150, 476, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    } else {
        Text(g, T(L"En attente des informations du jeu…", L"Waiting for the game's information…"), RectF(460, 260, 476, 30), 15, FontStyleBold, kInk);
        DrawBar(g, RectF(560, 300, 276, 5), -2);
        Para(g, T(L"Elles arrivent une fois le mod chargé (menu du jeu).", L"They show up once the mod is loaded (game menu)."), RectF(480, 316, 436, 40), 12, kGrey, StringAlignmentCenter);
        return;
    }
    static const int pal[8] = { 0x3E86D0, 0xE0702A, 0x2A9C9A, 0x8E5BD6, 0xD64545, 0xC9971A, 0xD45D9A, 0x6C7A89 };
    bool armedLive = g_kickArmed >= 0 && GetTickCount() - g_kickArmedT < 4000;
    for (int i = 0; i < (int)g_srv.size(); i++) {
        const SrvPlayer &p = g_srv[i];
        RectF r = SrvRow(i);
        if (r.Y + r.Height > kSrvList.Y + kSrvList.Height) break;
        GraphicsPath rp; RoundRect(rp, r, 11);
        SolidBrush rb(p.me ? TH(cardSel) : TH(card)); g.FillPath(&rb, &rp);
        Pen rpen(p.me ? kAcc : TH(choiceBorder), p.me ? 1.6f : 1.1f); g.DrawPath(&rpen, &rp);
        DrawSkinAvatar(g, RectF(r.X + 6, r.Y + 4, r.Height - 8, r.Height - 8), p.skin, CarRgb(pal[p.id % 8]), p.name);
        std::wstring nm = p.name + (p.me ? T(L"  (toi)", L"  (you)") : L"") + (p.id == 0 ? T(L"  · hôte", L"  · host") : L"");
        Text(g, nm, RectF(r.X + 62, r.Y + 7, 220, 20), 14, FontStyleBold, kInk, StringAlignmentNear);
        std::wstring line = p.inGame ? T(L"en partie", L"in game") : T(L"au menu", L"at the menu");
        if (p.ping >= 0) line += L" · " + std::to_wstring(p.ping) + L" ms";
        Text(g, line, RectF(r.X + 62, r.Y + 28, 220, 18), 11.5f, FontStyleRegular, p.ping > 250 ? Color(255, 205, 120, 30) : kGrey, StringAlignmentNear);
        if (!g_srvHost || p.me) continue;
        for (int k = 0; k < 2; k++) {
            RectF br = SrvBtn(r, k);
            bool hot = g_srvHot == 1000 + i * 2 + k, armed = k == 1 && armedLive && g_kickArmed == p.id, off = k == 0 && !p.inGame;
            GraphicsPath bp; RoundRect(bp, br, 13);
            Color c = k == 1 ? kRed : kAcc;
            if (armed) { SolidBrush ab(c); g.FillPath(&ab, &bp); }
            else if (hot) { SolidBrush hb(WithA(c, 0.2f)); g.FillPath(&hb, &bp); }
            Pen bpen(WithA(c, off ? 0.3f : 0.9f), 1.3f); g.DrawPath(&bpen, &bp);
            const wchar_t *lab = k == 0 ? T(L"FAIRE VENIR", L"BRING HERE") : armed ? T(L"CONFIRMER ?", L"CONFIRM?") : T(L"EXCLURE", L"KICK");
            Text(g, lab, br, 10.5f, FontStyleBold, armed ? Color(255, 255, 255, 255) : WithA(c, off ? 0.35f : 1.0f));
        }
    }
    Pen sep(TH(sep), 1);
    g.DrawLine(&sep, kOptPanel.X + 18, 518.0f, kOptPanel.X + kOptPanel.Width - 18, 518.0f);
    Para(g, g_srvHost ? T(L"FAIRE VENIR : le joueur arrive près de toi (s'il n'est pas dans un véhicule). EXCLURE : deux clics.",
                          L"BRING HERE: the player comes next to you (unless in a vehicle). KICK: two clicks.")
                      : T(L"Le lanceur reste ouvert pendant la partie ; il revient au menu quand le jeu se ferme.", L"The launcher stays open during the game; it goes back to the menu when the game closes."),
         RectF(kOptPanel.X + 20, 526, kOptPanel.Width - 40, 44), 11.5f, kGrey, StringAlignmentCenter);
}
