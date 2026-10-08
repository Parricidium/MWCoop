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
    g_tab = TAB_HOME;
    LaunchLog("jeu demarre : le lanceur reste ouvert (mode %d)", mode);
}

static void RunTick()
{
    if (g_state != ST_RUNNING) return;
    DWORD now = GetTickCount();
    if (now - g_srvReadT >= 1000) SrvRead();
    if (now - g_runCheckT < 1500) return;
    g_runCheckT = now;
    // 30 s apres le lancement : le chargeur MWCoop a-t-il ecrit sa trace (dernier-lancement.txt) pour cette partie ? Sinon
    // le jeu tourne sans le mod (retour d'un joueur, 08/10 : menu du jeu sans MWCoop, sans aucun journal) : dit ici et
    // note avec les jeux en cours, pour le zip des journaux.
    if (!g_modChecked && now - g_launchT > 30000 && !LocalDir().empty()) {
        g_modChecked = true;
        WIN32_FILE_ATTRIBUTE_DATA a;
        FILETIME ft; GetSystemTimeAsFileTime(&ft);
        ULONGLONG nowFt = ((ULONGLONG)ft.dwHighDateTime << 32) | ft.dwLowDateTime, ageMs = ~0ULL;
        if (GetFileAttributesExW((LocalDir() + L"dernier-lancement.txt").c_str(), GetFileExInfoStandard, &a))
            ageMs = (nowFt - (((ULONGLONG)a.ftLastWriteTime.dwHighDateTime << 32) | a.ftLastWriteTime.dwLowDateTime)) / 10000;
        LogGameModules();
        if (ageMs > (ULONGLONG)(now - g_launchT) + 5000) {
            LaunchLog("ATTENTION : pas de trace du chargeur MWCoop pour cette partie (dernier-lancement.txt %s) : le jeu tourne sans le mod", ageMs == ~0ULL ? "absent" : "plus ancien");
            GameProcessRunning();   // (note les jeux d'autres dossiers)
            g_modMissing = true;
            SetStatus(K_WARN, T(L"MWCoop ne s'est pas charg\u00E9 dans le jeu : ferme-le, puis envoie les journaux (zip) sans fermer le lanceur",
                                L"MWCoop did not load in the game: close it, then send the logs (zip) without closing the launcher"));
        } else LaunchLog("chargeur MWCoop vu pour cette partie");
    }
    if (g_proc && WaitForSingleObject(g_proc, 0) == WAIT_OBJECT_0) { CloseHandle(g_proc); g_proc = NULL; }
    if (g_proc || GameProcessRunning()) return;
    // jeu ferme : retour au menu
    g_state = ST_IDLE;
    g_srv.clear();
    CrashCheck();
    if (g_crashT) SetStatus(K_WARN, T(L"Arrêt brutal du jeu : voir les journaux", L"The game stopped abruptly: see the logs"));
    else SetStatus(K_NORMAL, T(L"Partie terminée · %s", L"Game over · %s"), ModLabel().c_str());
    LaunchLog("jeu ferme : retour au menu (arret brutal %d)", (int)(g_crashT != 0));
    // MWCoop pas charge dans cette partie, lancee par la copie : proposer le lancement par Steam (une fois par partie).
    const Opt *vs = OptByKey("LancerSteam");
    if (g_modMissing && vs && !OptGet(*vs) && !MirrorDir().empty()) {
        g_modMissing = false;
        int r = MessageBoxW(g_wnd, T(L"MWCoop ne s'est pas charg\u00E9 dans le jeu.\n\nChez certains joueurs, il ne se charge que si le jeu est lanc\u00E9 par Steam (comme son bouton JOUER).\n\nActiver \u00AB Lancer le jeu par Steam \u00BB (R\u00E9glages > Jeu) et relancer ta partie ?",
                                     L"MWCoop did not load in the game.\n\nFor some players it only loads when the game is started through Steam (like its PLAY button).\n\nTurn on \"Start the game through Steam\" (Settings > Game) and start your game again?"),
                             L"MWCoop", MB_YESNO | MB_ICONQUESTION);
        LaunchLog("lancement par Steam propose : %s", r == IDYES ? "accepte" : "refuse");
        if (r == IDYES) { OptSet(*vs, 1); SetStatus(K_OK, T(L"Lancement par Steam activ\u00E9 : relance ta partie", L"Start through Steam turned on: start your game again")); }
    }
}

// ---- dessin et clics (partie en cours : a gauche la partie, a droite les joueurs)
static const RectF kSrvCard(620, 88, 644, 656), kSrvList(640, 148, 604, 500);
static const float kSrvRowH = 86;
static RectF SrvBtn(RectF r, int k) { return k == 0 ? RectF(r.X + r.Width - 16 - 112 - 10 - 132, r.Y + (r.Height - 38) / 2, 132, 38) : RectF(r.X + r.Width - 16 - 112, r.Y + (r.Height - 38) / 2, 112, 38); }
static RectF SrvRow(int i) { return RectF(kSrvList.X, kSrvList.Y + i * kSrvRowH, kSrvList.Width, kSrvRowH - 8); }

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
    if (h < 0) return false;
    const SrvPlayer &p = g_srv[(h - 1000) / 2];
    if ((h - 1000) % 2 == 0) { SrvCommand("venir", p.id); SetStatus(K_OK, T(L"%s arrive près de toi", L"%s is coming to you"), p.name.c_str()); }
    else if (g_kickArmed == p.id && GetTickCount() - g_kickArmedT < 4000) { SrvCommand("kick", p.id); g_kickArmed = -1; SetStatus(K_WARN, T(L"%s exclu de la partie", L"%s kicked from the game"), p.name.c_str()); }
    else { g_kickArmed = p.id; g_kickArmedT = GetTickCount(); }
    return true;
}

static void DrawServer(Graphics &g)
{
    SkinsCheck();
    RectF lc(264, 88, 340, 656);
    Glass(lc);
    Glass(kSrvCard);
    // a gauche : la partie
    RectF ic(lc.X + 24, lc.Y + 28, 64, 64);
    GraphicsPath ip; RoundRect(ip, ic, 20);
    SolidBrush ib(WithA(kOk, 0.16f)); g.FillPath(&ib, &ip);
    Icon(g, IC_PLAY, ic.X + 32, ic.Y + 32, 28, kOk, 2.0f);
    Title(g, T(L"Partie en cours", L"Game running"), RectF(lc.X + 24, lc.Y + 108, lc.Width - 48, 34), 24, kInk);
    Para(g, g_launchInfo, RectF(lc.X + 24, lc.Y + 146, lc.Width - 48, 44), 14, kGrey);
    if (g_srvFresh) {
        std::wstring st = g_srvStatus + (g_srvSteam ? T(L" \u00B7 par Steam", L" \u00B7 through Steam") : L"");
        RectF sr(lc.X + 24, lc.Y + 204, lc.Width - 48, 44);
        GraphicsPath sp; RoundRect(sp, sr, 14);
        SolidBrush sb(TH(card)); g.FillPath(&sb, &sp);
        SolidBrush dot(kOk); g.FillEllipse(&dot, sr.X + 16, sr.Y + 18, 8.0f, 8.0f);
        Text(g, st, RectF(sr.X + 32, sr.Y, sr.Width - 40, sr.Height), 13, FontStyleBold, kInk, StringAlignmentNear);
    }
    Para(g, T(L"Le lanceur reste ouvert pendant la partie et revient au menu quand le jeu se ferme. Pour qu'il se ferme au lancement : R\u00E9glages.",
              L"The launcher stays open during the game and goes back to the menu when the game closes. To close it at launch: Settings."),
         RectF(lc.X + 24, lc.Y + lc.Height - 120, lc.Width - 48, 96), 12.5f, kGrey);
    // a droite : les joueurs
    RectF c = kSrvCard;
    const wchar_t *title = g_srvFresh && g_srvHost ? T(L"Serveur", L"Server") : T(L"Joueurs", L"Players");
    Title(g, title, RectF(c.X + 22, c.Y + 18, 300, 28), 18, kInk);
    if (!g_srvFresh) {
        float cy = c.Y + c.Height / 2 - 60;
        Title(g, T(L"En attente des informations du jeu\u2026", L"Waiting for the game's information\u2026"), RectF(c.X, cy, c.Width, 34), 20, kInk, StringAlignmentCenter);
        DrawBar(g, RectF(c.X + 170, cy + 50, c.Width - 340, 5), -2);
        Para(g, T(L"Elles arrivent une fois le mod charg\u00E9 (menu du jeu).", L"They show up once the mod is loaded (game menu)."), RectF(c.X + 100, cy + 70, c.Width - 200, 40), 13, kGrey, StringAlignmentCenter);
        return;
    }
    wchar_t head[64];
    swprintf_s(head, T(L"%d joueur(s)", L"%d player(s)"), (int)g_srv.size());
    Text(g, head, RectF(c.X + c.Width - 240, c.Y + 18, 216, 28), 13, FontStyleBold, kGrey, StringAlignmentFar);
    static const int pal[8] = { 0x3E86D0, 0xE0702A, 0x2A9C9A, 0x8E5BD6, 0xD64545, 0xC9971A, 0xD45D9A, 0x6C7A89 };
    bool armedLive = g_kickArmed >= 0 && GetTickCount() - g_kickArmedT < 4000;
    for (int i = 0; i < (int)g_srv.size(); i++) {
        const SrvPlayer &p = g_srv[i];
        RectF r = SrvRow(i);
        if (r.Y + r.Height > kSrvList.Y + kSrvList.Height) break;
        GraphicsPath rp; RoundRect(rp, r, 18);
        SolidBrush rb(p.me ? WithA(kAcc, 0.10f) : TH(card)); g.FillPath(&rb, &rp);
        Pen rpen(p.me ? kAcc : TH(choiceBorder), p.me ? 1.5f : 1.0f); g.DrawPath(&rpen, &rp);
        DrawSkinAvatar(g, RectF(r.X + 12, r.Y + (r.Height - 54) / 2, 54, 54), p.skin, CarRgb(pal[p.id % 8]), p.name);
        std::wstring nm = p.name + (p.me ? T(L"  (toi)", L"  (you)") : L"") + (p.id == 0 ? T(L"  \u00B7 h\u00F4te", L"  \u00B7 host") : L"");
        Text(g, nm, RectF(r.X + 80, r.Y + 14, 240, 24), 16, FontStyleBold, kInk, StringAlignmentNear);
        std::wstring line = p.inGame ? T(L"en partie", L"in game") : T(L"au menu", L"at the menu");
        if (p.ping >= 0) line += L" \u00B7 " + std::to_wstring(p.ping) + L" ms";
        Text(g, line, RectF(r.X + 80, r.Y + 40, 240, 20), 12.5f, FontStyleRegular, p.ping > 250 ? kWarn : kGrey, StringAlignmentNear);
        if (!g_srvHost || p.me) continue;
        for (int k = 0; k < 2; k++) {
            RectF br = SrvBtn(r, k);
            bool hot = g_srvHot == 1000 + i * 2 + k, armed = k == 1 && armedLive && g_kickArmed == p.id, off = k == 0 && !p.inGame;
            GraphicsPath bp; RoundRect(bp, br, 12);
            Color col = k == 1 ? kRed : kAcc;
            if (armed) { SolidBrush ab(col); g.FillPath(&ab, &bp); }
            else if (hot) { SolidBrush hb(WithA(col, 0.2f)); g.FillPath(&hb, &bp); }
            Pen bpen(WithA(col, off ? 0.3f : 0.9f), 1.3f); g.DrawPath(&bpen, &bp);
            const wchar_t *lab = k == 0 ? T(L"FAIRE VENIR", L"BRING HERE") : armed ? T(L"CONFIRMER ?", L"CONFIRM?") : T(L"EXCLURE", L"KICK");
            Text(g, lab, br, 11.5f, FontStyleBold, armed ? Color(255, 255, 255, 255) : WithA(col, off ? 0.35f : 1.0f));
        }
    }
    if (g_srvHost)
        Para(g, T(L"FAIRE VENIR : le joueur arrive pr\u00E8s de toi (s'il n'est pas dans un v\u00E9hicule). EXCLURE : deux clics.",
                  L"BRING HERE: the player comes next to you (unless in a vehicle). KICK: two clicks."),
             RectF(c.X + 24, c.Y + c.Height - 70, c.Width - 48, 50), 12.5f, kGrey, StringAlignmentCenter);
}
