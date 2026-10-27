// ---------------------------------------------------------------- ADDONS : accueil Voitures / Mods (inclus par launcher.cpp, apres voiture.inc)
// Demande de JD (27/10, spec D:\1 - AI\ClaudeAI\MWCoop-spec-kupla-modules.md §5) : un clic sur Addons ouvre deux grandes cartes,
// « Voitures » (la page des voitures addons, inchangee, avec un retour) et « Mods » (« Modules du mod co-op » : des modules
// livres DANS le mod, comme la VAZ -- aucune DLL telechargee par le lanceur). Les modules sont listes sous les deux cartes ;
// Kupla, l'optimisation co-op de [Addons et Tools], est le premier.
// Tant que kKuplaShown est faux (launcher.cpp), rien de tout ca ne se voit : Addons ouvre directement les voitures.
// Interrupteurs (spec §3) : [Coop] KuplaRendu, chacun pour lui (la case de la carte) ; [Coop] KuplaBulles, l'hote pour tous
// (option du Salon, kLobbyOptDefs, cachee tant que kKuplaBullesShown est faux). Le mod les lit au lancement de la partie.
static int g_kuplaRendu = -1;          // [Coop] KuplaRendu (-1 : pas encore lu)
static bool g_kuplaNewSession;         // NEW sur la carte Mods pendant la session ou Kupla est apparu (puis [Lanceur] KuplaVu=1)
static RectF g_addonsModsR;            // carte Mods de l'accueil (bulle du tuto)
static bool KuplaNew() { return KuplaShown() && !GetPrivateProfileIntW(L"Lanceur", L"KuplaVu", 0, g_iniLauncher.c_str()); }
static int KuplaRenduGet()
{
    if (g_kuplaRendu < 0) g_kuplaRendu = g_gameDir.empty() ? 0 : GetPrivateProfileIntA("Coop", "KuplaRendu", 0, ModIniA().c_str()) ? 1 : 0;
    return g_kuplaRendu;
}
static void KuplaRenduToggle()
{
    if (g_gameDir.empty()) return;
    EnsureModDir();
    g_kuplaRendu = KuplaRenduGet() ? 0 : 1;
    WritePrivateProfileStringA("Coop", "KuplaRendu", g_kuplaRendu ? "1" : "0", ModIniA().c_str());
    TestLog("addons : Kupla, rendu pour moi %s", g_kuplaRendu ? "allume" : "coupe");
    SetStatus(K_OK, g_kuplaRendu ? T(L"Kupla : rendu allégé à la prochaine partie", L"Kupla: lighter rendering from the next game")
                                 : T(L"Kupla : rendu normal à la prochaine partie", L"Kupla: normal rendering from the next game"));
}
// Bulle de Kupla (icone de la carte) : un cercle, son reflet.
static void DrawKuplaIcon(Graphics &g, float cx, float cy, float r, Color c)
{
    Pen p(c, 2.2f); g.DrawEllipse(&p, cx - r, cy - r, 2 * r, 2 * r);
    Pen h(WithA(c, 0.7f), 2.0f); h.SetStartCap(LineCapRound); h.SetEndCap(LineCapRound);
    g.DrawArc(&h, cx - r * 0.62f, cy - r * 0.62f, r * 1.24f, r * 1.24f, 200.0f, 55.0f);
    SolidBrush d(WithA(c, 0.8f)); g.FillEllipse(&d, cx + r * 0.25f, cy + r * 0.2f, r * 0.22f, r * 0.22f);
}
static void DrawAddonsHome(Graphics &g)
{
    AddonsScan();
    if (KuplaNew()) { g_kuplaNewSession = true; WritePrivateProfileStringW(L"Lanceur", L"KuplaVu", L"1", g_iniLauncher.c_str()); }
    RectF area(kCarCard.X, kCarCard.Y, kCarPanel.X + kCarPanel.Width - kCarCard.X, kCarCard.Height);
    Glass(area);
    float x = area.X + 24, w = area.Width - 48;
    Title(g, T(L"Addons", L"Add-ons"), RectF(x, area.Y + 20, 400, 30), 22, kInk);
    Para(g, T(L"Les voitures en extension, et les modules livrés avec le mod co-op.", L"Extension cars, and the modules shipped with the co-op mod."),
         RectF(x, area.Y + 56, w, 22), 12.5f, kGrey);
    // ---- les deux cartes
    float cy = area.Y + 92, cw = (w - 16) / 2, ch = 132;
    for (int k = 0; k < 2; k++) {
        RectF r(x + k * (cw + 16), cy, cw, ch);
        int id = k == 0 ? UI_ADDONS_CARS : UI_ADDONS_MODS;
        bool hot = g_uiHot == id, cur = k == 1;   // (Mods : ses modules sont dessous)
        GraphicsPath p; RoundRect(p, r, 18);
        SolidBrush b(hot ? TH(cardSel) : cur ? WithA(kAcc, 0.08f) : TH(card)); g.FillPath(&b, &p);
        Pen pn(cur ? kAcc : hot ? WithA(kAcc, 0.6f) : TH(choiceBorder), cur ? 1.8f : 1.0f); g.DrawPath(&pn, &p);
        RectF sq(r.X + 20, r.Y + 22, 64, 64);
        GraphicsPath sp; RoundRect(sp, sq, 16);
        SolidBrush sb(Color((BYTE)(g_dark ? 70 : 16), 0, 0, 0)); g.FillPath(&sb, &sp);
        Icon(g, k == 0 ? IC_CAR : IC_PUZZLE, sq.X + 32, sq.Y + 32, 34, kAcc, 2.0f);
        float tx = sq.X + sq.Width + 18, tw = r.X + r.Width - 20 - tx;
        Text(g, k == 0 ? T(L"Voitures", L"Cars") : T(L"Mods", L"Mods"), RectF(tx, r.Y + 22, tw, 30), 20, FontStyleBold, kInk, StringAlignmentNear);
        std::wstring sub;
        if (k == 0) {
            wchar_t s[160];
            swprintf_s(s, T(L"Les voitures addons et la VAZ-2108 (%d) : télécharger, mettre à jour, livrées.", L"The add-on cars and the VAZ-2108 (%d): download, update, liveries."), (int)g_addonList.size());
            sub = s;
        } else sub = T(L"Modules du mod co-op", L"Modules of the co-op mod");
        Para(g, sub, RectF(tx, r.Y + 56, tw - 20, 40), 12.5f, kGrey);
        float bx = r.X + r.Width - 16;   // badges en haut a droite (comme la barre)
        if (k == 0) {
            bool nw, up; AddonsBadges(nw, up);
            if (up) { const wchar_t *s = L"UPDATE"; float pw = PillW(s, 9.5f); Pill(g, RectF(bx - pw, r.Y + 16, pw, 20), kWarn, s, Color(255, 255, 255, 255), 9.5f); bx -= pw + 6; }
            if (nw) { const wchar_t *s = L"NEW"; float pw = PillW(s, 9.5f); Pill(g, RectF(bx - pw, r.Y + 16, pw, 20), kAcc, s, kOnAcc, 9.5f); }
            Icon(g, IC_CHEVR, r.X + r.Width - 26, r.Y + r.Height - 26, 18, kGrey, 2.0f);
        } else {
            g_addonsModsR = r;
            if (g_kuplaNewSession) { const wchar_t *s = L"NEW"; float pw = PillW(s, 9.5f); Pill(g, RectF(bx - pw, r.Y + 16, pw, 20), kAcc, s, kOnAcc, 9.5f); }
        }
        HotZone(r, id);
    }
    // ---- les modules (Mods)
    float y = cy + ch + 26;
    SmallLabel(g, T(L"MODULES DU MOD CO-OP", L"MODULES OF THE CO-OP MOD"), RectF(x, y, w, 16), kAcc);
    y += 24;
    RectF kc(x, y, w, 150);
    GraphicsPath kp; RoundRect(kp, kc, 18);
    SolidBrush kb(TH(card)); g.FillPath(&kb, &kp);
    Pen kpn(TH(choiceBorder), 1.0f); g.DrawPath(&kpn, &kp);
    DrawKuplaIcon(g, kc.X + 48, kc.Y + 48, 24, kAcc);
    float lx = kc.X + 92, lw = kc.Width * 0.52f;
    Text(g, L"Kupla", RectF(lx, kc.Y + 20, 200, 28), 19, FontStyleBold, kInk, StringAlignmentNear);
    Text(g, T(L"Optimisation co-op · MWC et MSC", L"Co-op optimization · MWC and MSC"), RectF(lx, kc.Y + 50, lw, 20), 12.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    {   // jeton bêta (le rose des reglages en beta, gfx.inc), apres le nom : en arabe, la ligne grise se mesure mal
        std::wstring s = T(L"Bêta", L"Beta");
        float pw = PillW(s, 9.5f) - 2, nw = MeasureW(MeasureG(), L"Kupla", 19, FontStyleBold);
        Pill(g, RectF(lx + nw + 12, kc.Y + 25, pw, 18), Color(255, 255, 107, 139), s, Color(255, 255, 255, 255), 9.5f);
    }
    Para(g, T(L"Allège le jeu : distance d'affichage, ombres, rétroviseurs. Éteint, le jeu reste exactement comme avant. Pris en compte au lancement de la partie.",
              L"Lightens the game: view distance, shadows, mirrors. Off, the game stays exactly as before. Applied when the game starts."),
         RectF(lx, kc.Y + 80, lw, 60), 12.5f, kInk);
    // a droite : la case « Rendu (pour moi) », puis la ligne des bulles (reglees par l'hote)
    float rx = lx + lw + 28, rw = kc.X + kc.Width - 20 - rx;
    RectF tr(rx, kc.Y + 20, rw, 58);
    bool hot = g_uiHot == UI_KUPLA_RENDU, en = !g_gameDir.empty();
    if (hot && en) { GraphicsPath hp; RoundRect(hp, RectF(tr.X - 10, tr.Y - 4, tr.Width + 20, tr.Height + 8), 12); SolidBrush hb(TH(cardSel)); g.FillPath(&hb, &hp); }
    Text(g, T(L"Rendu (pour moi)", L"Rendering (for me)"), RectF(tr.X, tr.Y + 4, tr.Width - 60, 22), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
    Para(g, T(L"Plus d'images par seconde chez toi ; rien ne change pour les autres.", L"More frames per second on your side; nothing changes for the others."),
         RectF(tr.X, tr.Y + 26, tr.Width - 60, 34), 11.5f, kGrey);
    UiToggle(g, RectF(tr.X + tr.Width - 48, tr.Y + 6, 48, 28), en && KuplaRenduGet() == 1, hot && en);
    if (en) HotZone(tr, UI_KUPLA_RENDU);
    Pen sp(TH(sep), 1.0f); g.DrawLine(&sp, rx, kc.Y + 92, rx + rw, kc.Y + 92);
    Icon(g, IC_USERS, rx + 10, kc.Y + 118, 18, kGrey, 1.9f);
    Para(g, T(L"Bulles co-op : réglé par l'hôte dans le Salon", L"Co-op bubbles: set by the host in the Lobby"), RectF(rx + 28, kc.Y + 106, rw - 28, 40), 12.5f, kGrey);
}
