// ---------------------------------------------------------------- page API (auteurs de mods) - inclus par launcher.cpp
// Demande de JD (07/10) : un onglet pour les moddeurs, pour savoir ou trouver l'API de MWCoop (MWCoop.CoopApi, 0.43)
// et sa documentation : ce qu'elle fait, un exemple a copier (par reflexion ou en referencant MWCoop.dll), la DLL dans
// le dossier du jeu, la doc et le code sur GitHub.
static const wchar_t *kApiDocUrl = L"https://github.com/Parricidium/MWCoop/blob/main/docs/API.md";
static const wchar_t *kApiSrcUrl = L"https://github.com/Parricidium/MWCoop/blob/main/src/MWCoop/CoopApi.cs";
static int g_apiEx;            // exemple montre : 0 par reflexion, 1 reference directe
static DWORD g_apiCopiedT;     // "Copie !" pendant 2 s
static const RectF kApiMain(264, 88, 600, 656), kApiWhere(880, 88, 384, 300), kApiTips(880, 404, 384, 340);

static const wchar_t *ApiExample(int k)
{
    if (k == 0) return
        L"// No hard dependency: null when MWCoop is not installed\n"
        L"Type api = Type.GetType(\"MWCoop.CoopApi, MWCoop\");\n"
        L"if (api != null)\n"
        L"{\n"
        L"    api.GetMethod(\"Listen\").Invoke(null, new object[] { ID,\n"
        L"        new Action<int, byte[]>((from, data) => { /* ... */ }) });\n"
        L"\n"
        L"    bool host = (bool)api.GetProperty(\"IsHost\").GetValue(null, null);\n"
        L"    api.GetMethod(\"Send\").Invoke(null, new object[] { ID, bytes });\n"
        L"}";
    return
        L"// Reference <game folder>\\MWCoop\\MWCoop.dll\n"
        L"using MWCoop;\n"
        L"\n"
        L"CoopApi.Listen(ID, (from, data) => { /* ... */ });\n"
        L"\n"
        L"// bring a newcomer up to date\n"
        L"CoopApi.OnPlayerJoined(id => CoopApi.SendTo(id, ID, MyState()));\n"
        L"\n"
        L"if (CoopApi.IsHost) CoopApi.Send(ID, bytes);";
}

static std::wstring ApiDllPath() { return g_gameDir.empty() ? L"" : g_gameDir + L"MWCoop\\MWCoop.dll"; }

static void DrawApiPage(Graphics &g)
{
    // ---- gauche : ce qu'elle fait, exemple
    RectF c = kApiMain;
    Glass(c);
    Title(g, T(L"Synchroniser votre mod en coop", L"Sync your mod in co-op"), RectF(c.X + 24, c.Y + 20, c.Width - 48, 28), 19, kInk);
    Para(g, T(L"MWCoop.CoopApi envoie les données de votre mod MSCLoader aux autres joueurs : MWCoop s'occupe du réseau, du découpage des gros messages et du relais par l'hôte.",
              L"MWCoop.CoopApi sends your MSCLoader mod's data to the other players: MWCoop handles the network, splitting big messages and relaying through the host."),
         RectF(c.X + 24, c.Y + 54, c.Width - 48, 44), 13, kGrey);
    struct Feat { const wchar_t *code, *fr, *en; };
    static const Feat feats[] = {
        { L"Send · SendTo · SendUnreliable", L"à tous ou à un joueur (64 Ko)", L"to everyone or one player (64 KB)" },
        { L"Listen", L"recevoir sur votre canal (ID du mod)", L"receive on your channel (mod ID)" },
        { L"IsHost · PlayerIds · PlayerName", L"qui héberge, qui joue", L"who hosts, who plays" },
        { L"PlayerJoined · PlayerLeft", L"arrivées et départs des joueurs", L"players joining and leaving" } };
    FontFamily mono(L"Consolas");
    Font fm(&mono, 13.5f, FontStyleBold, UnitPixel);
    StringFormat sfl; sfl.SetLineAlignment(StringAlignmentCenter); sfl.SetFormatFlags(StringFormatFlagsNoWrap);
    float y = c.Y + 108;
    for (const Feat &f : feats) {
        RectF r(c.X + 24, y, c.Width - 48, 40);
        GraphicsPath p; RoundRect(p, r, 12);
        SolidBrush fb(TH(card)); g.FillPath(&fb, &p);
        SolidBrush ab(kAcc);
        g.DrawString(f.code, -1, &fm, RectF(r.X + 14, r.Y, 260, r.Height), &sfl, &ab);
        Text(g, g_fr ? f.fr : f.en, RectF(r.X + 274, r.Y, r.Width - 284, r.Height), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
        y += 46;
    }
    // exemple
    y += 10;
    SmallLabel(g, T(L"EXEMPLE", L"EXAMPLE"), RectF(c.X + 26, y + 8, 200, 20), WithA(kGrey, 0.9f));
    UiSeg(g, RectF(c.X + c.Width - 24 - 330, y, 330, 36), { T(L"Par réflexion", L"Through reflection"), T(L"Référence directe", L"Direct reference") }, g_apiEx, UI_API_EX0);
    y += 48;
    RectF box(c.X + 24, y, c.Width - 48, c.Y + c.Height - 20 - y);
    {
        GraphicsPath p; RoundRect(p, box, 14);
        SolidBrush fb(g_dark ? Color(150, 4, 8, 20) : Color(235, 244, 247, 252)); g.FillPath(&fb, &p);
        Pen pen(TH(choiceBorder), 1); g.DrawPath(&pen, &p);
    }
    Font fc(&mono, 12.5f, FontStyleRegular, UnitPixel);
    SolidBrush ink(kInk), grey(WithA(kGrey, 0.95f)), str(kOk);
    StringFormat sf; sf.SetFormatFlags(StringFormatFlagsNoWrap | StringFormatFlagsMeasureTrailingSpaces);
    std::wstring ex = ApiExample(g_apiEx);
    float ly = box.Y + 16;
    GraphicsState st = g.Save();
    g.SetClip(box, CombineModeIntersect);
    for (size_t at = 0; at <= ex.size();) {
        size_t e = ex.find(L'\n', at);
        std::wstring line = ex.substr(at, e == std::wstring::npos ? std::wstring::npos : e - at);
        at = e == std::wstring::npos ? ex.size() + 1 : e + 1;
        size_t b = line.find_first_not_of(L' ');
        bool comment = b != std::wstring::npos && line.compare(b, 2, L"//") == 0;
        // chaines en vert, commentaires en gris, le reste en encre
        float x = box.X + 18;
        size_t i = 0;
        while (i < line.size()) {
            size_t j; SolidBrush *br;
            if (comment) { j = line.size(); br = &grey; }
            else if (line[i] == L'"') { j = line.find(L'"', i + 1); j = j == std::wstring::npos ? line.size() : j + 1; br = &str; }
            else { j = line.find(L'"', i); if (j == std::wstring::npos) j = line.size(); br = &ink; }
            std::wstring part = line.substr(i, j - i);
            RectF m; g.MeasureString(part.c_str(), -1, &fc, PointF(0, 0), &sf, &m);
            g.DrawString(part.c_str(), -1, &fc, PointF(x, ly), &sf, br);
            x += m.Width - 3.0f;   // (marges de GDI+ autour de chaque morceau)
            i = j;
        }
        ly += 19;
    }
    g.Restore(st);
    bool copied = g_apiCopiedT && GetTickCount() - g_apiCopiedT < 2000;
    UiButton(g, RectF(box.X + box.Width - 126, box.Y + 10, 114, 34), copied ? T(L"Copié !", L"Copied!") : T(L"Copier", L"Copy"), UI_API_COPY, false, copied ? IC_CHECK : IC_FILE, true, 10);

    // ---- droite : ou la trouver
    RectF w = kApiWhere;
    Glass(w);
    Title(g, T(L"Où la trouver", L"Where to get it"), RectF(w.X + 20, w.Y + 16, 220, 26), 17, kInk);
    {
        const wchar_t *v = L"API v1 · 0.43+";
        float pw = PillW(v, 10.5f);
        Pill(g, RectF(w.X + w.Width - 20 - pw, w.Y + 19, pw, 22), WithA(kAcc, 0.18f), v, kAcc, 10.5f);
    }
    {
        RectF r(w.X + 20, w.Y + 56, w.Width - 40, 56);
        GraphicsPath p; RoundRect(p, r, 12);
        SolidBrush fb(TH(card)); g.FillPath(&fb, &p);
        std::wstring dll = ApiDllPath();
        bool ok = !dll.empty() && FileExists(dll);
        Icon(g, IC_CODE, r.X + 24, r.Y + r.Height / 2, 20, ok ? kAcc : kGrey, 2.0f);
        Text(g, L"MWCoop.dll", RectF(r.X + 46, r.Y + 8, r.Width - 56, 22), 14, FontStyleBold, kInk, StringAlignmentNear);
        StringFormat sp; sp.SetTrimming(StringTrimmingEllipsisPath); sp.SetFormatFlags(StringFormatFlagsNoWrap); sp.SetLineAlignment(StringAlignmentCenter);
        FontFamily fam(L"Segoe UI"); Font f(&fam, 11.5f, FontStyleRegular, UnitPixel); SolidBrush gb(kGrey);
        std::wstring sub = ok ? dll : std::wstring(T(L"Dans le dossier du jeu, sous MWCoop\\ (une fois installé)", L"In the game folder, under MWCoop\\ (once installed)"));
        g.DrawString(sub.c_str(), -1, &f, RectF(r.X + 46, r.Y + 30, r.Width - 56, 18), &sp, &gb);
    }
    UiButton(g, RectF(w.X + 20, w.Y + 126, w.Width - 40, 46), T(L"Documentation (GitHub)", L"Documentation (GitHub)"), UI_API_DOC, true, IC_FILE);
    UiButton(g, RectF(w.X + 20, w.Y + 182, (w.Width - 50) / 2, 44), T(L"Code source", L"Source code"), UI_API_SRC, false, IC_CODE);
    UiButton(g, RectF(w.X + 30 + (w.Width - 50) / 2, w.Y + 182, (w.Width - 50) / 2, 44), T(L"Dossier", L"Folder"), UI_API_DIR, false, IC_FOLDER, !g_gameDir.empty());
    Para(g, T(L"Docs, liste complète et exemples MSCLoader dans docs/API.md.", L"Docs, full member list and MSCLoader examples in docs/API.md."),
         RectF(w.X + 20, w.Y + 240, w.Width - 40, 44), 12, kGrey);

    // ---- droite : bon a savoir
    RectF t = kApiTips;
    Glass(t);
    Title(g, T(L"Bon à savoir", L"Good to know"), RectF(t.X + 20, t.Y + 16, 300, 26), 17, kInk);
    static const wchar_t *tipsFr[] = {
        L"En anglais : noms, commentaires et doc. Le reste du code de MWCoop est en français, mais ouvert et non obfusqué : inutile de le lire.",
        L"Sans dépendance : trouvée par réflexion, votre mod marche aussi sans MWCoop.",
        L"Déjà fait pour vous : vos fichiers, vos réglages et votre sauvegarde (Mods.txt) partent chez les invités avec ceux de l'hôte.",
        L"L'hôte décide : il sauvegarde le monde. Un message envoyé avant que le mod de l'autre écoute lui est gardé." };
    static const wchar_t *tipsEn[] = {
        L"In English: names, comments and docs. The rest of MWCoop's code is in French, but open and not obfuscated: no need to read it.",
        L"No dependency: found through reflection, your mod also works without MWCoop.",
        L"Already done for you: your files, settings and save data (Mods.txt) go to the guests with the host's.",
        L"The host decides: it saves the world. A message sent before the other player's mod listens is kept for it." };
    float ty = t.Y + 56;
    FontFamily tf(L"Segoe UI");
    Font tfont(&tf, 12.5f, FontStyleRegular, UnitPixel);
    for (int i = 0; i < 4; i++) {
        const wchar_t *s = g_fr ? tipsFr[i] : tipsEn[i];
        RectF m; g.MeasureString(s, -1, &tfont, RectF(0, 0, t.Width - 58, 400), &m);
        SolidBrush dot(kAcc); g.FillEllipse(&dot, t.X + 22, ty + 7, 7.0f, 7.0f);
        Para(g, s, RectF(t.X + 38, ty, t.Width - 58, m.Height + 4), 12.5f, kInk);
        ty += m.Height + 16;
    }
}

static void ApiClick(int id)
{
    switch (id) {
    case UI_API_DOC: ShellExecuteW(g_wnd, L"open", kApiDocUrl, NULL, NULL, SW_SHOWNORMAL); break;
    case UI_API_SRC: ShellExecuteW(g_wnd, L"open", kApiSrcUrl, NULL, NULL, SW_SHOWNORMAL); break;
    case UI_API_DIR: {
        std::wstring dll = ApiDllPath();
        if (dll.empty()) break;
        if (FileExists(dll)) { std::wstring a = L"/select,\"" + dll + L"\""; ShellExecuteW(g_wnd, L"open", L"explorer.exe", a.c_str(), NULL, SW_SHOWNORMAL); }
        else ShellExecuteW(g_wnd, L"open", g_gameDir.c_str(), NULL, NULL, SW_SHOWNORMAL);
        break;
    }
    case UI_API_COPY: {
        std::wstring s = ApiExample(g_apiEx), out;
        for (wchar_t ch : s) { if (ch == L'\n') out += L'\r'; out += ch; }
        if (CopyText(out)) g_apiCopiedT = GetTickCount();
        break;
    }
    case UI_API_EX0: case UI_API_EX1: g_apiEx = id - UI_API_EX0; g_apiCopiedT = 0; break;
    }
}
