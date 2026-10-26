// ---------------------------------------------------------------- OUTILS : placement des sieges (inclus par launcher.cpp, apres
// places.inc) -- [Addons et Tools]. Bandeau sous les deux cartes de la page Outils, seulement avec une cle d'acces valide qui
// donne l'outil des places (AccesPlaces : places.inc, [Launcher]) ; spec validee par JD le 26/10. Deux boutons : Ouvrir l'outil
// (PlacesOpen : telecharge / met a jour l'outil chiffre, puis le lance) et Exporter mes voitures (PlacesExport : le jeu en
// arriere-plan, dans une copie de la partie en cours -- ses voitures doivent y etre montees).
static RectF g_placesCardR;   // (bulle du tuto : tuto.inc)

// Etat lu de temps en temps (dessine a chaque image) : WebView2 present, date de l'export des voitures de ce jeu.
static bool g_placesWv = true;
static std::wstring g_placesExpWhen;
static DWORD g_placesPollT;
static void PlacesPoll()
{
    if (g_placesPollT && GetTickCount() - g_placesPollT < 3000) return;
    g_placesPollT = GetTickCount();
    g_placesWv = WebView2Ok();
    g_placesExpWhen.clear();
    std::wstring root = PlacesRoot();
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!root.empty() && GetFileAttributesExW((root + L"\\" + GM().id + L"\\voitures.json").c_str(), GetFileExInfoStandard, &a)) {
        FILETIME lf; SYSTEMTIME s;
        FileTimeToLocalFileTime(&a.ftLastWriteTime, &lf); FileTimeToSystemTime(&lf, &s);
        wchar_t b[40]; swprintf_s(b, L"%02d/%02d %02d:%02d", s.wDay, s.wMonth, s.wHour, s.wMinute);
        g_placesExpWhen = b;
    }
}

static void DrawPlacesCard(Graphics &g, RectF c)
{
    g_placesCardR = c;
    PlacesPoll();
    bool fake = g_placesFake != 0;
    bool exporting = g_placesFake == 2 || (!fake && PlacesExporting()), busy = !fake && PlacesBusy();
    float prog = busy ? g_placesProg : -1;
    std::wstring when = fake ? std::wstring(L"26/10 16:40") : g_placesExpWhen;
    Glass(c);
    Icon(g, IC_CAR, c.X + 44, c.Y + 34, 28, kAcc, 2.0f);
    const wchar_t *title = T(L"Placement des sièges", L"Seat placement");
    Title(g, title, RectF(c.X + 76, c.Y + 16, 420, 30), 20, kInk);
    {
        const wchar_t *pill = T(L"ACCÈS RÉSERVÉ", L"RESERVED ACCESS");
        Pill(g, RectF(c.X + 76 + MeasureW(MeasureG(), title, 20, FontStyleBold) + 12, c.Y + 21, PillW(pill, 10.5f), 22), WithA(kAcc, 0.18f), pill, kAcc, 10.5f);
    }
    RectF bOpen(c.X + c.Width - 24 - 230 - 12 - 210, c.Y + 36, 210, 48), bExp(c.X + c.Width - 24 - 230, c.Y + 36, 230, 48);
    float tw = bOpen.X - 24 - (c.X + 76);
    Para(g, T(L"Place les joueurs assis, règle postures et ceintures. Exporte d'abord tes voitures depuis une partie où elles sont montées.",
              L"Place the seated players, set postures and belts. First export your cars from a save where they are assembled."),
         RectF(c.X + 76, c.Y + 48, tw, 40), 12.5f, kGrey);
    // ligne d'etat
    std::wstring st; Color sc = kGrey;
    if (exporting) { st = T(L"Export des voitures en cours : le jeu tourne en arrière-plan (2 à 3 minutes)", L"Exporting the cars: the game runs in the background (2 to 3 minutes)"); sc = kAcc; }
    else if (busy) { st = prog >= 0 ? T(L"Téléchargement de l'outil…", L"Downloading the tool…") : T(L"Ouverture de l'outil…", L"Opening the tool…"); sc = kAcc; }
    else if (!g_placesWv && !fake) { st = T(L"WebView2 manque (Windows 10 et 11 l'ont avec Edge) : l'outil ne pourra pas s'ouvrir", L"WebView2 is missing (Windows 10 and 11 have it with Edge): the tool cannot open"); sc = kWarn; }
    else if (when.empty()) { st = T(L"Pas encore de voitures exportées pour ce jeu : « Exporter mes voitures » d'abord (Satsuma : sièges montés ; Rivett : assemblée)", L"No cars exported for this game yet: « Export my cars » first (Satsuma: seats fitted; Rivett: assembled)"); sc = kWarn; }
    else {
        wchar_t b[200]; swprintf_s(b, T(L"Voitures exportées le %s · refais l'export après avoir monté d'autres pièces", L"Cars exported on %s · export again after fitting other parts"), when.c_str());
        st = b; sc = kOk;
    }
    {
        SolidBrush d(sc); g.FillEllipse(&d, c.X + 78, c.Y + c.Height - 25, 8.0f, 8.0f);
        Text(g, st, RectF(c.X + 94, c.Y + c.Height - 34, c.Width - 94 - 24, 26), 12.5f, FontStyleRegular, kInk, StringAlignmentNear);
    }
    // boutons
    if (busy && prog >= 0) {   // (telechargement : le bouton se remplit, comme les cartes d'outil)
        GraphicsPath p; RoundRect(p, bOpen, 14);
        SolidBrush bg(WithA(kAcc, 0.25f)); g.FillPath(&bg, &p);
        float k = min(1.0f, prog);
        GraphicsState s2 = g.Save();
        g.SetClip(RectF(bOpen.X, bOpen.Y, bOpen.Width * k, bOpen.Height), CombineModeIntersect);
        SolidBrush fb(kAcc); g.FillPath(&fb, &p);
        g.Restore(s2);
        wchar_t b[64]; swprintf_s(b, L"%d %%", (int)(k * 100 + 0.5f));
        Text(g, b, bOpen, 13.5f, FontStyleBold, k > 0.5f ? kOnAcc : kInk);
    } else UiButton(g, bOpen, T(L"Ouvrir l'outil", L"Open the tool"), UI_PLACES_OPEN, !when.empty(), IC_PLAY, !busy && !exporting);
    UiButton(g, bExp, exporting ? T(L"Export en cours…", L"Exporting…") : T(L"Exporter mes voitures", L"Export my cars"), UI_PLACES_EXPORT, when.empty(), IC_DOWNLOAD, !busy && !exporting);
}

static void PlacesClick(int id)
{
    if (id == UI_PLACES_OPEN) { PlacesOpen(); g_placesPollT = 0; }
    else if (id == UI_PLACES_EXPORT) { PlacesExport(); g_placesPollT = 0; }
}
