// ---------------------------------------------------------------- page GRAPHISMES - inclus par launcher.cpp
// Demande de JD (07/10), comme les GTA : les reglages graphiques dans le lanceur (et en jeu, menu F10). Ecrits dans
// [Graphismes] du mwcoop.ini du jeu (MWCoop\Gfx.cs les applique au chargement, et tout de suite si le jeu tourne) ;
// cle absente : le jeu decide ("Jeu" pour les reglages du moteur). Options du jeu (son menu) et reglages du moteur en
// plus ; prereglages Performance / Equilibre / Beau / Ultra en un clic.
// ---- touches en jeu (Options du jeu > Touches) : section [Touches] de mwcoop.ini, noms des KeyCode de Unity
struct GameKey { const char *ini, *def; const wchar_t *fr, *en; };
static const GameKey kGameKeys[] = { { "Tchat", "T", L"Tchat", L"Chat" }, { "PorteMonnaie", "N", L"Argent", L"Wallet" }, { "Vocal", "V", L"Vocal", L"Voice" }, { "Triches", "F7", L"Triches", L"Cheats" }, { "Carte", "M", L"Carte", L"Map" } };
static const int kGameKeysN = 5;
static int g_keyCapture = -1;   // pastille en attente d'une touche
static int GameKeyIndex(const char *rowKey) { const char *n = strchr(rowKey, ':'); n = n ? n + 1 : rowKey; for (int i = 0; i < kGameKeysN; i++) if (!strcmp(n, kGameKeys[i].ini)) return i; return -1; }
static std::string GameKeyGet(int k)
{
    char buf[64] = {};
    GetPrivateProfileStringA("Touches", kGameKeys[k].ini, kGameKeys[k].def, buf, sizeof(buf), ModIniA().c_str());
    return buf[0] ? buf : kGameKeys[k].def;
}
static std::wstring GameKeyLabel(const std::string &n)
{
    if (n.rfind("Alpha", 0) == 0) return Widen(n.substr(5));
    if (n.rfind("Keypad", 0) == 0) return std::wstring(T(L"Pavé ", L"Num ")) + Widen(n.substr(6));
    if (n == "LeftShift") return T(L"Maj gauche", L"Left Shift");
    if (n == "RightShift") return T(L"Maj droite", L"Right Shift");
    if (n == "LeftControl") return T(L"Ctrl gauche", L"Left Ctrl");
    if (n == "RightControl") return T(L"Ctrl droite", L"Right Ctrl");
    if (n == "LeftAlt") return L"Alt";
    if (n == "CapsLock") return T(L"Verr. maj", L"Caps Lock");
    if (n == "Mouse3") return T(L"Souris 4", L"Mouse 4");
    if (n == "Mouse4") return T(L"Souris 5", L"Mouse 5");
    return Widen(n);
}
// Touche Windows -> nom de KeyCode de Unity ("" : pas prise). 'ext' : bit "etendue" du message (Ctrl, Alt droits).
static std::string UnityKeyName(WPARAM vk, LPARAM lp)
{
    bool ext = (lp & (1 << 24)) != 0;
    if (vk >= 'A' && vk <= 'Z') return std::string(1, (char)vk);
    if (vk >= '0' && vk <= '9') return "Alpha" + std::string(1, (char)vk);
    if (vk >= VK_NUMPAD0 && vk <= VK_NUMPAD9) return "Keypad" + std::to_string((int)(vk - VK_NUMPAD0));
    if (vk >= VK_F1 && vk <= VK_F12) return "F" + std::to_string((int)(vk - VK_F1 + 1));
    switch (vk) {
    case VK_SHIFT: return MapVirtualKeyW((lp >> 16) & 0xFF, MAPVK_VSC_TO_VK_EX) == VK_RSHIFT ? "RightShift" : "LeftShift";
    case VK_CONTROL: return ext ? "RightControl" : "LeftControl";
    case VK_MENU: return ext ? "RightAlt" : "LeftAlt";
    case VK_SPACE: return "Space";
    case VK_TAB: return "Tab";
    case VK_RETURN: return ext ? "KeypadEnter" : "Return";
    case VK_BACK: return "Backspace";
    case VK_CAPITAL: return "CapsLock";
    case VK_INSERT: return "Insert";
    case VK_DELETE: return "Delete";
    case VK_HOME: return "Home";
    case VK_END: return "End";
    case VK_PRIOR: return "PageUp";
    case VK_NEXT: return "PageDown";
    case VK_UP: return "UpArrow";
    case VK_DOWN: return "DownArrow";
    case VK_LEFT: return "LeftArrow";
    case VK_RIGHT: return "RightArrow";
    case VK_ADD: return "KeypadPlus";
    case VK_SUBTRACT: return "KeypadMinus";
    case VK_MULTIPLY: return "KeypadMultiply";
    case VK_DIVIDE: return "KeypadDivide";
    case VK_DECIMAL: return "KeypadPeriod";
    case VK_XBUTTON1: return "Mouse3";
    case VK_XBUTTON2: return "Mouse4";
    }
    return "";
}
// Touche choisie pour la pastille en attente (Echap : annule). Vrai si prise.
static bool GameKeyCapture(WPARAM vk, LPARAM lp)
{
    if (g_keyCapture < 0) return false;
    if (vk == VK_ESCAPE) { g_keyCapture = -1; InvalidateRect(g_wnd, NULL, FALSE); return true; }
    std::string n = UnityKeyName(vk, lp);
    if (n.empty()) return true;   // (touche pas prise en charge : on attend une autre)
    WritePrivateProfileStringA("Touches", kGameKeys[g_keyCapture].ini, n.c_str(), ModIniA().c_str());
    LaunchLog("touche en jeu : %s = %s", kGameKeys[g_keyCapture].ini, n.c_str());
    g_keyCapture = -1;
    InvalidateRect(g_wnd, NULL, FALSE);
    return true;
}

struct GfxRow
{
    const char *key; const wchar_t *fr, *en;
    int def;                                   // valeur montree quand la cle est absente (-1 : "Jeu")
    std::vector<int> vals;                     // vide : interrupteur 0/1
    std::vector<const wchar_t *> lf, le;       // noms des valeurs (vals)
    int impact;                                // sur les images par seconde : 0 aucun, 1 leger, 2 moyen, 3 fort
    int group;                                 // 0 Image, 1 Ombres, 2 Distance et details, 3 Jeu et confort, 4 Lumieres, 5 Ecran, 6 Voix, 7 Visages, 8 Partie et partage, 9 Touches
    const char *sec;                           // section du mwcoop.ini (absente : Graphismes)
};
// Options du mod gardees dans [Coop] (demande de JD, 10/10 : « une section pour ces options du .ini ») : categorie Coop.
static const std::vector<GfxRow> &GfxRows()
{
    static std::vector<GfxRow> rows;
    if (!rows.empty()) return rows;
    rows = {
        { "Anticrenelage", L"Anticrénelage", L"Anti-aliasing", 1, {}, {}, {}, 1, 0 },
        { "HDR", L"HDR (lumière riche)", L"HDR (rich light)", 1, {}, {}, {}, 1, 0 },
        { "Bloom", L"Halo lumineux (bloom)", L"Bloom", 0, {}, {}, {}, 1, 0 },
        { "Contraste", L"Contraste renforcé", L"Contrast boost", 0, {}, {}, {}, 0, 0 },
        { "RayonsSoleil", L"Rayons du soleil", L"Sun shafts", 0, {}, {}, {}, 2, 0 },
        { "ImagesMax", L"Images par seconde max.", L"Max frames per second", -1, { -1, 30, 60, 75, 120, 144, 165, 240, 0 },
          { L"Jeu", L"30", L"60", L"75", L"120", L"144", L"165", L"240", L"Illimitées" }, { L"Game", L"30", L"60", L"75", L"120", L"144", L"165", L"240", L"Unlimited" }, 0, 0 },
        { "SyncVerticale", L"Synchro verticale", L"V-sync", -1, { -1, 0, 1 }, { L"Jeu", L"Non", L"Oui" }, { L"Game", L"Off", L"On" }, 0, 0 },
        { "OmbresSoleil", L"Ombres du soleil", L"Sun shadows", 1, {}, {}, {}, 3, 1 },
        { "OmbresLune", L"Ombres de la lune", L"Moon shadows", 0, {}, {}, {}, 2, 1 },
        { "OmbresMaisons", L"Ombres des maisons", L"House shadows", 1, {}, {}, {}, 2, 1 },
        { "OmbresDistance", L"Distance des ombres", L"Shadow distance", -1, { -1, 30, 60, 100, 150, 250, 400 },
          { L"Jeu", L"30 m", L"60 m", L"100 m", L"150 m", L"250 m", L"400 m" }, { L"Game", L"30 m", L"60 m", L"100 m", L"150 m", L"250 m", L"400 m" }, 3, 1 },
        { "OmbresCascades", L"Finesse des ombres", L"Shadow detail", -1, { -1, 1, 2, 4 }, { L"Jeu", L"Simple", L"Fine", L"Très fine" }, { L"Game", L"Basic", L"Fine", L"Very fine" }, 2, 1 },
        { "Distance", L"Distance d'affichage", L"Draw distance", 3000, { 500, 1000, 1500, 2000, 3000, 4500, 6000, 9000 },
          { L"500 m", L"1 km", L"1,5 km", L"2 km", L"3 km", L"4,5 km", L"6 km", L"9 km" }, { L"500 m", L"1 km", L"1.5 km", L"2 km", L"3 km", L"4.5 km", L"6 km", L"9 km" }, 3, 2 },
        { "Details", L"Niveau de détail", L"Level of detail", -1, { -1, 70, 100, 150, 200, 300 },
          { L"Jeu", L"Bas", L"Normal", L"Élevé", L"Très élevé", L"Max" }, { L"Game", L"Low", L"Normal", L"High", L"Very high", L"Max" }, 2, 2 },
        { "Textures", L"Textures", L"Textures", -1, { -1, 2, 1, 0 }, { L"Jeu", L"Quart", L"Moitié", L"Pleines" }, { L"Game", L"Quarter", L"Half", L"Full" }, 1, 2 },
        { "Filtrage", L"Filtrage des textures", L"Texture filtering", -1, { -1, 0, 1, 2 }, { L"Jeu", L"Non", L"Oui", L"Forcé" }, { L"Game", L"Off", L"On", L"Forced" }, 1, 2 },
        { "OmbresPhares", L"Ombres des phares", L"Headlight shadows", 0, { 0, 2, 4, 8 }, { L"Non", L"2 phares", L"4 phares", L"8 phares" }, { L"Off", L"2 lights", L"4 lights", L"8 lights" }, 3, 4 },
        { "OmbresLampadaires", L"Ombres des lampadaires", L"Street light shadows", 0, { 0, 1, 2, 4 }, { L"Non", L"1", L"2", L"4" }, { L"Off", L"1", L"2", L"4" }, 3, 4 },
        { "Halos", L"Halos lumineux (façon GTA)", L"Light coronas (GTA style)", 0, { 0, 1, 2 }, { L"Non", L"Discrets", L"Marqués" }, { L"Off", L"Subtle", L"Strong" }, 1, 4 },
        { "PorteePhares", L"Portée des phares", L"Headlight range", 0, { 0, 1 }, { L"Normale", L"Longue" }, { L"Normal", L"Long" }, 1, 4 },
        { "ChampVision", L"Champ de vision", L"Field of view", 60, { 50, 55, 60, 65, 70, 75, 80, 85, 90 },
          { L"50°", L"55°", L"60°", L"65°", L"70°", L"75°", L"80°", L"85°", L"90°" }, { L"50°", L"55°", L"60°", L"65°", L"70°", L"75°", L"80°", L"85°", L"90°" }, 0, 3 },
        { "Retroviseurs", L"Rétroviseurs", L"Mirrors", 1, { 0, 1, 2 }, { L"Coupés", L"Simples", L"Complets" }, { L"Off", L"Simple", L"Full" }, 2, 3 },
        { "Balancement", L"Tête qui balance à pied", L"Head bob on foot", 1, {}, {}, {}, 0, 3 },
        { "BalancementVoiture", L"Tête qui balance en voiture", L"Head bob in cars", 2, { 0, 1, 2 }, { L"Non", L"50 %", L"Oui" }, { L"Off", L"50 %", L"On" }, 0, 3 },
        { "CompteurImages", L"Compteur d'images", L"FPS counter", 0, {}, {}, {}, 0, 3 },
        { "IndicateurRapport", L"Rapport engagé à l'écran", L"Gear on screen", 1, {}, {}, {}, 0, 3 },
        { "PoussiereTrafic", L"Poussière de la circulation", L"Traffic dust", 1, {}, {}, {}, 1, 3 },
        { "TracesTrafic", L"Traces de pneus de la circulation", L"Traffic skid marks", 0, {}, {}, {}, 1, 3 },
        { "Pseudos", L"Pseudos des joueurs", L"Player names", 120, { 0, 30, 60, 120, 250 },
          { L"Cachés", L"30 m", L"60 m", L"120 m", L"250 m" }, { L"Hidden", L"30 m", L"60 m", L"120 m", L"250 m" }, 0, 3 },
        // Coop, rangees par type (demande de JD, 10/10) : 6 voix, 7 visages, 8 partie et partage, 9 touches.
        { "Vocal", L"Vocal de proximité", L"Proximity voice", 1, {}, {}, {}, 0, 6, "Coop" },
        { "VocalPortee", L"Portée de la voix", L"Voice range", 35, { 15, 25, 35, 50, 80 },
          { L"15 m", L"25 m", L"35 m", L"50 m", L"80 m" }, { L"15 m", L"25 m", L"35 m", L"50 m", L"80 m" }, 0, 6, "Coop" },
        { "VolumeVocal", L"Volume des voix", L"Voice volume", 100, { 25, 50, 75, 100 },
          { L"25 %", L"50 %", L"75 %", L"100 %" }, { L"25 %", L"50 %", L"75 %", L"100 %" }, 0, 6, "Coop" },
        { "VoirVisages", L"Voir les images importées des autres", L"See others' imported images", 1, {}, {}, {}, 0, 7, "Coop" },
        { "ModsAuto", L"Accepter les fichiers de l'hôte", L"Accept the host's files", 0, {}, {}, {}, 0, 8, "Opt" },
        { "ArgentDepart", L"Argent de départ des invités", L"Guests' starting money", 1, {}, {}, {}, 0, 8, "Coop" },
        { "Triches", L"Autoriser les triches (hôte)", L"Allow cheats (host)", 0, {}, {}, {}, 0, 8, "Coop" },
        { "Touche:Tchat", L"Écrire aux autres (tchat)", L"Write to the others (chat)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:PorteMonnaie", L"Porte-monnaie (donner de l'argent)", L"Wallet (give money)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Vocal", L"Parler (vocal, touche tenue)", L"Talk (voice, hold the key)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Triches", L"Panneau des triches", L"Cheat panel", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Carte", L"Carte (joueurs en direct)", L"Map (live players)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "CarteMaintenir", L"Carte : appuyer ou maintenir", L"Map: press or hold", 0, { 0, 1 }, { L"Appuyer", L"Maintenir" }, { L"Press", L"Hold" }, 0, 9, "Coop" },   // (appuyer : ouvre / ferme ; maintenir : tant que la touche est tenue)
    };
    // Ecran (comme les GTA, a la place de la petite fenetre Unity) : resolutions de l'ecran principal (Windows),
    // mode d'affichage, niveau de qualite du moteur. Appliques par le mod au demarrage du jeu (MWCoop\Gfx.cs).
    static std::vector<std::wstring> resLabels;
    std::vector<int> rv = { -1 };
    std::vector<std::pair<int, int>> modes;
    DEVMODEW dm = { sizeof(dm) };
    for (DWORD i = 0; EnumDisplaySettingsW(NULL, i, &dm); i++)
        if (dm.dmPelsWidth >= 800 && dm.dmPelsHeight >= 600) {
            std::pair<int, int> m((int)dm.dmPelsWidth, (int)dm.dmPelsHeight);
            if (std::find(modes.begin(), modes.end(), m) == modes.end()) modes.push_back(m);
        }
    std::sort(modes.begin(), modes.end());
    resLabels.clear();
    resLabels.reserve(modes.size() + 1);
    for (auto &m : modes) { rv.push_back(m.first * 10000 + m.second); resLabels.push_back(std::to_wstring(m.first) + L" × " + std::to_wstring(m.second)); }
    GfxRow res = { "Resolution", L"Résolution", L"Resolution", -1, rv, { L"Jeu" }, { L"Game" }, 0, 5 };
    for (auto &l : resLabels) { res.lf.push_back(l.c_str()); res.le.push_back(l.c_str()); }
    rows.insert(rows.begin(), {
        res,
        { "ModeEcran", L"Affichage", L"Display mode", -1, { -1, 0, 1, 2 }, { L"Jeu", L"Fenêtré", L"Plein écran", L"Sans bordure" }, { L"Game", L"Windowed", L"Fullscreen", L"Borderless" }, 0, 5 },
        { "QualiteUnity", L"Qualité du moteur", L"Engine quality", -1, { -1, 0, 1, 3, 5 }, { L"Jeu", L"Basse", L"Moyenne", L"Bonne", L"Golden Eye" }, { L"Game", L"Low", L"Medium", L"Good", L"Golden Eye" }, 3, 5 },
    });
    return rows;
}

// Prereglages : valeurs des options de qualite (le reste -- champ de vision, images, confort -- n'est pas touche).
struct GfxPreset { const wchar_t *fr, *en, *dFr, *dEn; std::vector<std::pair<const char *, int>> v; };
static const std::vector<GfxPreset> &GfxPresets()
{
    static std::vector<GfxPreset> p;
    if (!p.empty()) return p;
    auto mk = [](int aa, int hdr, int bloom, int shafts, int moon, int house, int sdist, int casc, int dist, int det, int tex, int filt, int mir, int dust, int skid, int hshad, int sshad, int halos) {
        return std::vector<std::pair<const char *, int>> { { "Anticrenelage", aa }, { "HDR", hdr }, { "Bloom", bloom }, { "RayonsSoleil", shafts }, { "OmbresSoleil", 1 },
            { "OmbresLune", moon }, { "OmbresMaisons", house }, { "OmbresDistance", sdist }, { "OmbresCascades", casc }, { "Distance", dist }, { "Details", det },
            { "Textures", tex }, { "Filtrage", filt }, { "Retroviseurs", mir }, { "PoussiereTrafic", dust }, { "TracesTrafic", skid },
            { "OmbresPhares", hshad }, { "OmbresLampadaires", sshad }, { "Halos", halos } };
    };
    p = {
        { L"Performance", L"Performance", L"Plus d'images", L"More frames", mk(0, 0, 0, 0, 0, 0, 30, 1, 1500, 70, 1, 0, 1, 0, 0, 0, 0, 0) },
        { L"Équilibré", L"Balanced", L"Le bon compromis", L"The sweet spot", mk(1, 1, 0, 0, 0, 1, 60, 2, 3000, 100, 0, 1, 1, 1, 0, 0, 0, 1) },
        { L"Beau", L"Pretty", L"Plus joli", L"Better looking", mk(1, 1, 1, 1, 1, 1, 150, 2, 4500, 150, 0, 1, 2, 1, 1, 2, 1, 1) },
        { L"Ultra", L"Ultra", L"Tout au max", L"Everything maxed", mk(1, 1, 1, 1, 1, 1, 400, 4, 9000, 300, 0, 2, 2, 1, 1, 4, 2, 2) },
    };
    return p;
}

static std::map<std::string, int> g_gfxVal;
static DWORD g_gfxReadT;
static void GfxLoad()
{
    g_gfxVal.clear();
    std::wstring ini = ModIni();
    for (const GfxRow &r : GfxRows()) {
        if (r.sec && !strcmp(r.sec, "Touches")) continue;   // (lues a l'affichage : GameKeyGet)
        if (r.sec && !strcmp(r.sec, "Opt")) { const Opt *o = OptByKey(r.key); g_gfxVal[r.key] = o ? OptGet(*o) : r.def; continue; }   // (option du lanceur)
        wchar_t b[32] = {};
        GetPrivateProfileStringW(r.sec ? Widen(r.sec).c_str() : L"Graphismes", Widen(r.key).c_str(), L"", b, 32, ini.c_str());
        g_gfxVal[r.key] = b[0] ? _wtoi(b) : r.def;
    }
    g_gfxReadT = GetTickCount();
}
static int GfxGet(const char *k) { auto it = g_gfxVal.find(k); return it != g_gfxVal.end() ? it->second : -1; }
static void GfxSet(const char *k, int v)
{
    g_gfxVal[k] = v;
    std::wstring s = std::to_wstring(v);
    for (const GfxRow &r : GfxRows()) if (!strcmp(r.key, k) && r.sec && !strcmp(r.sec, "Opt")) { if (const Opt *o = OptByKey(k)) OptSet(*o, v); return; }
    std::wstring sec = L"Graphismes";
    for (const GfxRow &r : GfxRows()) if (!strcmp(r.key, k) && r.sec) sec = Widen(r.sec);
    WritePrivateProfileStringW(sec.c_str(), Widen(k).c_str(), v == -1 ? NULL : s.c_str(), ModIni().c_str());   // (-1 : la cle est retiree, le jeu decide)
}
static int GfxPresetOn()
{
    const auto &ps = GfxPresets();
    for (int i = 0; i < (int)ps.size(); i++) {
        bool all = true;
        for (auto &kv : ps[i].v) if (GfxGet(kv.first) != kv.second) { all = false; break; }
        if (all) return i;
    }
    return -1;
}

static const RectF kGfxTop(264, 88, 1000, 84), kGfxArea(264, 184, 1000, 560);
static const float kGfxRowH = 27, kGfxHeadH = 44;
// Categories (demande de JD, 08/10) : en-tetes repliables comme les journaux, reglages sur deux colonnes dessous ;
// ascenseur quand tout ne tient pas. Deplies : [Lanceur] GfxOuverts (numeros de groupe ; toutes par defaut).
static const int kGfxOrder[] = { 6, 7, 8, 9, 5, 0, 1, 4, 2, 3 };
static bool g_gfxOpen[10] = { true, true, true, true, true, true, true, true, true, true };
static bool g_gfxOpenRead;
static float g_gfxContentH;
static bool g_gfxZones = true;   // (ligne hors de la zone visible : pas de zone cliquable)
static void GfxOpenRead()
{
    if (g_gfxOpenRead) return;
    g_gfxOpenRead = true;
    wchar_t b[64] = {};
    GetPrivateProfileStringW(L"Lanceur", L"GfxOuverts", L"*", b, 64, g_iniLauncher.c_str());
    if (!wcscmp(b, L"*")) return;
    for (int i = 0; i < 10; i++) g_gfxOpen[i] = wcschr(b, L'0' + i) != NULL;
}
static void GfxToggleGroup(int gi)
{
    if (gi < 0 || gi >= 10) return;
    g_gfxOpen[gi] = !g_gfxOpen[gi];
    std::wstring s;
    for (int i = 0; i < 10; i++) if (g_gfxOpen[i]) { if (!s.empty()) s += L","; s += (wchar_t)(L'0' + i); }
    WritePrivateProfileStringW(L"Lanceur", L"GfxOuverts", s.empty() ? L"-" : s.c_str(), g_iniLauncher.c_str());
}
static float GfxMaxScroll() { return max(0.0f, g_gfxContentH - (kGfxArea.Height - 20)); }

static void DrawGfxRow(Graphics &g, const GfxRow &r, int idx, RectF row)
{
    int id = UI_GFX + idx * 3;
    bool hot = g_uiHot == id || g_uiHot == id + 1 || g_uiHot == id + 2;
    if (hot) { GraphicsPath p; RoundRect(p, RectF(row.X - 8, row.Y, row.Width + 16, row.Height), 10); SolidBrush hb(TH(card)); g.FillPath(&hb, &p); }
    const wchar_t *lab = g_fr ? r.fr : r.en;
    Text(g, lab, RectF(row.X, row.Y, row.Width - 190, row.Height), 13.5f, FontStyleRegular, kInk, StringAlignmentNear);
    if (r.impact >= 2) {   // impact sur les images : pastille apres le nom
        Graphics &mg = MeasureG();
        float w = MeasureW(mg, lab, 13.5f, FontStyleRegular);
        const wchar_t *il = r.impact >= 3 ? T(L"lourd", L"heavy") : T(L"moyen", L"medium");
        float pw = PillW(il, 9.5f);
        Color ic = r.impact >= 3 ? kRed : kWarn;
        Pill(g, RectF(row.X + w + 10, row.Y + (row.Height - 18) / 2, pw, 18), WithA(ic, 0.16f), il, ic, 9.5f);
    }
    if (r.sec && !strcmp(r.sec, "Touches")) {   // pastille de la touche ; clic : la prochaine touche appuyee
        int k = GameKeyIndex(r.key);
        bool cap = k >= 0 && g_keyCapture == k;
        RectF pr(row.X + row.Width - 172, row.Y + 2, 172, row.Height - 4);
        GraphicsPath pp; RoundRect(pp, pr, 10);
        SolidBrush pb(cap ? WithA(kAcc, 0.25f) : TH(btn2)); g.FillPath(&pb, &pp);
        Pen ppen(cap ? kAcc : TH(fieldBorder), cap ? 2.0f : 1.0f); g.DrawPath(&ppen, &pp);
        Text(g, cap ? std::wstring(T(L"Appuie sur une touche…", L"Press a key…")) : k >= 0 ? GameKeyLabel(GameKeyGet(k)) : std::wstring(L"?"), pr, 12.5f, FontStyleBold, cap ? kAcc : kInk);
        if (g_gfxZones) HotZone(row, id);
        return;
    }
    int v = GfxGet(r.key);
    if (r.vals.empty()) {
        UiToggle(g, RectF(row.X + row.Width - 44, row.Y + (row.Height - 22) / 2, 44, 22), v == 1, hot);
        if (g_gfxZones) HotZone(row, id);
        return;
    }
    // ◀ valeur ▶
    int k = 0;
    for (int i = 0; i < (int)r.vals.size(); i++) if (r.vals[i] == v) k = i;
    RectF val(row.X + row.Width - 172, row.Y + 2, 172, row.Height - 4);
    RectF lft(val.X, val.Y, 30, val.Height), rgt(val.X + val.Width - 30, val.Y, 30, val.Height);
    GraphicsPath vp; RoundRect(vp, val, 10);
    SolidBrush vb(TH(btn2)); g.FillPath(&vb, &vp);
    Pen vpen(TH(fieldBorder), 1.0f); g.DrawPath(&vpen, &vp);
    bool canL = k > 0, canR = k + 1 < (int)r.vals.size();
    Icon(g, IC_CHEVL, lft.X + 15, lft.Y + lft.Height / 2, 14, WithA(g_uiHot == id + 1 ? kAcc : kInk, canL ? 1.0f : 0.25f), 2.2f);
    Icon(g, IC_CHEVR, rgt.X + 15, rgt.Y + rgt.Height / 2, 14, WithA(g_uiHot == id + 2 ? kAcc : kInk, canR ? 1.0f : 0.25f), 2.2f);
    bool game = r.vals[k] == -1;
    Text(g, g_fr ? r.lf[k] : r.le[k], RectF(lft.X + lft.Width, val.Y, val.Width - 60, val.Height), 12.5f, FontStyleBold, game ? kGrey : kInk);
    if (!g_gfxZones) return;
    HotZone(row, UI_NONE);
    if (canL) HotZone(lft, id + 1);
    if (canR) HotZone(rgt, id + 2);
}

// Une categorie : en-tete (chevron, nom, nombre de reglages), puis ses reglages sur deux colonnes s'il est deplie.
// 'y' : position dans le contenu (avant defilement) ; dessine seulement ce qui croise la zone visible.
static void DrawGfxGroup(Graphics &g, int group, float x, float &y, float w, float top, float bottom)
{
    static const wchar_t *kFr[] = { L"Image", L"Ombres", L"Distance et détails", L"Jeu et confort", L"Lumières", L"Écran", L"Voix", L"Visages et habits", L"Partie et partage", L"Touches" };
    static const wchar_t *kEn[] = { L"Image", L"Shadows", L"Distance and detail", L"Game and comfort", L"Lights", L"Screen", L"Voice", L"Faces and clothes", L"Game and sharing", L"Keys" };
    const auto &rows = GfxRows();
    std::vector<int> mine;
    for (int i = 0; i < (int)rows.size(); i++) if (rows[i].group == group) mine.push_back(i);
    float sy = top + y - g_scroll[TAB_GFX];
    RectF hr(x, sy, w, kGfxHeadH - 6);
    if (hr.Y + hr.Height > top && hr.Y < bottom) {
        bool open = g_gfxOpen[group], hot = g_uiHot == UI_GFX_GROUP + group;
        GraphicsPath p; RoundRect(p, hr, 12);
        SolidBrush fb(hot ? TH(cardSel) : TH(card)); g.FillPath(&fb, &p);
        Pen pen(hot ? WithA(kAcc, 0.7f) : TH(choiceBorder), 1.0f); g.DrawPath(&pen, &p);
        Icon(g, open ? IC_CHEVD : IC_CHEVR, hr.X + 22, hr.Y + hr.Height / 2, 16, hot ? kAcc : kInk, 2.3f);
        Text(g, g_fr ? kFr[group] : kEn[group], RectF(hr.X + 44, hr.Y, 300, hr.Height), 14, FontStyleBold, kInk, StringAlignmentNear);
        wchar_t n[48];
        swprintf_s(n, T(L"%d réglages", L"%d settings"), (int)mine.size());
        Text(g, n, RectF(hr.X + hr.Width - 220, hr.Y, 204, hr.Height), 12, FontStyleRegular, kGrey, StringAlignmentFar);
        if (hr.Y >= top - 1 && hr.Y + hr.Height <= bottom + 1) HotZone(hr, UI_GFX_GROUP + group);
    }
    y += kGfxHeadH;
    if (!g_gfxOpen[group]) { y += 4; return; }
    int half = ((int)mine.size() + 1) / 2;
    float colW = (w - 40) / 2;
    for (int k = 0; k < (int)mine.size(); k++) {
        int col = k < half ? 0 : 1, line = col ? k - half : k;
        RectF row(x + 14 + col * (colW + 20), top + y + line * (kGfxRowH + 1) - g_scroll[TAB_GFX], colW - 14, kGfxRowH);
        if (row.Y + row.Height < top || row.Y > bottom) continue;
        g_gfxZones = row.Y >= top - 1 && row.Y + row.Height <= bottom + 1;
        DrawGfxRow(g, rows[mine[k]], mine[k], row);
        g_gfxZones = true;
    }
    y += half * (kGfxRowH + 1) + 10;
}

static void DrawGfxPage(Graphics &g)
{
    if (GetTickCount() - g_gfxReadT > 2000) GfxLoad();   // (le jeu peut les avoir changes : son menu, F10)
    // prereglages
    RectF t = kGfxTop;
    Glass(t);
    Title(g, T(L"Qualité d'image", L"Image quality"), RectF(t.X + 24, t.Y + 14, 300, 26), 17, kInk);
    int on = GfxPresetOn();
    Para(g, on >= 0 ? T(L"Un clic règle tout, puis ajuste ligne par ligne.", L"One click sets everything, then fine-tune below.") : T(L"Réglage personnalisé : choisis un préréglage pour repartir.", L"Custom settings: pick a preset to start over."),
         RectF(t.X + 24, t.Y + 40, 300, 40), 12.5f, kGrey);
    const auto &ps = GfxPresets();
    float tw = 148, gap = 10, x0 = t.X + t.Width - 20 - ((int)ps.size() * tw + ((int)ps.size() - 1) * gap);
    for (int i = 0; i < (int)ps.size(); i++) {
        RectF r(x0 + i * (tw + gap), t.Y + 12, tw, t.Height - 24);
        bool sel = i == on, hot = g_uiHot == UI_GFX_PRESET + i;
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush fb(sel ? kAcc : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&fb, &p);
        if (!sel) { Pen pen(TH(fieldBorder), 1.0f); g.DrawPath(&pen, &p); }
        Color ink = sel ? kOnAcc : kInk;
        Text(g, g_fr ? ps[i].fr : ps[i].en, RectF(r.X + 14, r.Y + 7, r.Width - 20, 22), 14.5f, FontStyleBold, ink, StringAlignmentNear);
        Text(g, g_fr ? ps[i].dFr : ps[i].dEn, RectF(r.X + 14, r.Y + 29, r.Width - 20, 18), 11.5f, FontStyleRegular, sel ? WithA(kOnAcc, 0.8f) : kGrey, StringAlignmentNear);
        // barres de niveau (1 a 4)
        for (int b = 0; b <= 3; b++) {
            RectF br(r.X + r.Width - 14 - (3 - b) * 7, r.Y + 19 - b * 4, 4, 6 + b * 4);
            SolidBrush bb(b <= i ? (sel ? kOnAcc : kAcc) : WithA(ink, 0.18f)); g.FillRectangle(&bb, br);
        }
        HotZone(r, UI_GFX_PRESET + i);
    }
    // options : categories repliables, ascenseur si besoin
    GfxOpenRead();
    RectF a = kGfxArea;
    Glass(a);
    float top = a.Y + 10, bottom = a.Y + a.Height - 10;
    g_scroll[TAB_GFX] = min(max(g_scroll[TAB_GFX], 0.0f), GfxMaxScroll());
    GraphicsState st = g.Save();
    g.SetClip(RectF(a.X, top, a.Width, bottom - top), CombineModeIntersect);
    float y = 0;
    for (int gi : kGfxOrder) DrawGfxGroup(g, gi, a.X + 20, y, a.Width - 48, top, bottom);
    g.Restore(st);
    g_gfxContentH = y;
    float ms = GfxMaxScroll();
    if (ms > 0) {
        float view = bottom - top, h = view * view / (view + ms), yy = top + (view - h) * g_scroll[TAB_GFX] / ms;
        GraphicsPath sp; RoundRect(sp, RectF(a.X + a.Width - 14, yy, 5, h), 2.5f);
        SolidBrush sb(WithA(kAcc, 0.55f)); g.FillPath(&sb, &sp);
    }
}

static bool GfxClick(int id)
{
    if (id >= UI_GFX_PRESET && id < UI_GFX_PRESET + (int)GfxPresets().size()) {
        for (auto &kv : GfxPresets()[id - UI_GFX_PRESET].v) GfxSet(kv.first, kv.second);
        return true;
    }
    const auto &rows = GfxRows();
    if (id < UI_GFX || id >= UI_GFX + (int)rows.size() * 3) return false;
    const GfxRow &r = rows[(id - UI_GFX) / 3];
    if (r.sec && !strcmp(r.sec, "Touches")) { int k = GameKeyIndex(r.key); g_keyCapture = g_keyCapture == k ? -1 : k; return true; }
    int part = (id - UI_GFX) % 3, v = GfxGet(r.key);
    if (r.vals.empty()) { if (part == 0) GfxSet(r.key, v == 1 ? 0 : 1); return true; }
    int k = 0;
    for (int i = 0; i < (int)r.vals.size(); i++) if (r.vals[i] == v) k = i;
    if (part == 1 && k > 0) GfxSet(r.key, r.vals[k - 1]);
    if (part == 2 && k + 1 < (int)r.vals.size()) GfxSet(r.key, r.vals[k + 1]);
    return true;
}
