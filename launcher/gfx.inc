// ---------------------------------------------------------------- page GRAPHISMES - inclus par launcher.cpp
// Demande de JD (07/10), comme les GTA : les reglages graphiques dans le lanceur (et en jeu, menu F10). Ecrits dans
// [Graphismes] du mwcoop.ini du jeu (MWCoop\Gfx.cs les applique au chargement, et tout de suite si le jeu tourne) ;
// cle absente : le jeu decide ("Jeu" pour les reglages du moteur). Options du jeu (son menu) et reglages du moteur en
// plus ; prereglages Vanilla / Leger / Equilibre / Joli / Ultra en un clic. Depuis le 17/10 : page REGLAGES unique
// (onglets Jeu, Image & ray tracing, Son & vocal, Co-op, Touches, Theme, Lanceur), voir plus bas.
// ---- touches en jeu (Options du jeu > Touches) : section [Touches] de mwcoop.ini, noms des KeyCode de Unity
struct GameKey { const char *ini, *def; const wchar_t *fr, *en; const char *defAz = nullptr; };   // defAz : clavier AZERTY
static const GameKey kGameKeys[] = { { "Tchat", "T", L"Tchat", L"Chat" }, { "PorteMonnaie", "N", L"Argent", L"Wallet" }, { "Vocal", "V", L"Vocal", L"Voice" }, { "Triches", "F7", L"Triches", L"Cheats" }, { "Carte", "M", L"Carte", L"Map" }, { "Vue", "F5", L"Vue", L"View" }, { "Marqueur", "Mouse2", L"Marqueur", L"Marker" },
    // Camera drone (mode photo, demande de JD, 11/10) : categorie Camera drone ; memes noms et defauts que MWCoop\UI\Keys.cs.
    { "Drone", "F8", L"Drone", L"Drone" }, { "DroneAvant", "W", L"Avancer", L"Forward", "Z" }, { "DroneArriere", "S", L"Reculer", L"Back" },
    { "DroneGauche", "A", L"Gauche", L"Left", "Q" }, { "DroneDroite", "D", L"Droite", L"Right" }, { "DroneMonter", "Space", L"Monter", L"Up" },
    { "DroneDescendre", "C", L"Descendre", L"Down" }, { "DroneRapide", "LeftShift", L"Rapide", L"Fast" }, { "DroneZoomPlus", "R", L"Zoom +", L"Zoom +" },
    { "DroneZoomMoins", "F", L"Zoom -", L"Zoom -" }, { "DronePencherG", "Q", L"Pencher à gauche", L"Tilt left", "A" }, { "DronePencherD", "E", L"Pencher à droite", L"Tilt right" },
    { "DroneRecentrer", "X", L"Recentrer", L"Reset" }, { "DronePhoto", "Mouse0", L"Photo", L"Photo" }, { "DroneGrille", "G", L"Grille", L"Grid" },
    { "DroneInterface", "H", L"Interface", L"Interface" } };
// Clavier AZERTY (francais de France, de Belgique, du Luxembourg, de Monaco ; pas le canadien ni le suisse) : comme Keys.Azerty du mod.
static bool KeyboardAzerty()
{
    int id = (int)((ULONG_PTR)GetKeyboardLayout(0) & 0xFFFF);
    return id == 0x040C || id == 0x080C || id == 0x140C || id == 0x180C;
}
static const char *GameKeyDefault(int k) { return kGameKeys[k].defAz && KeyboardAzerty() ? kGameKeys[k].defAz : kGameKeys[k].def; }
static const int kGameKeysN = (int)(sizeof(kGameKeys) / sizeof(kGameKeys[0]));
static int g_keyCapture = -1;   // pastille en attente d'une touche
static int GameKeyIndex(const char *rowKey) { const char *n = strchr(rowKey, ':'); n = n ? n + 1 : rowKey; for (int i = 0; i < kGameKeysN; i++) if (!strcmp(n, kGameKeys[i].ini)) return i; return -1; }
static std::string GameKeyGet(int k)
{
    char buf[64] = {};
    GetPrivateProfileStringA("Touches", kGameKeys[k].ini, GameKeyDefault(k), buf, sizeof(buf), ModIniA().c_str());
    return buf[0] ? buf : GameKeyDefault(k);
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
    if (n == "Mouse0") return T(L"Clic gauche", L"Left click");
    if (n == "Mouse1") return T(L"Clic droit", L"Right click");
    if (n == "Mouse2") return T(L"Clic molette", L"Middle click");
    if (n == "Space") return T(L"Espace", L"Space");
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
    if (vk == VK_DELETE) {   // Suppr : touche par defaut (ex. clic gauche de la photo du drone, qu'on ne peut pas « appuyer » ici)
        WritePrivateProfileStringA("Touches", kGameKeys[g_keyCapture].ini, NULL, ModIniA().c_str());
        LaunchLog("touche en jeu : %s par defaut", kGameKeys[g_keyCapture].ini);
        g_keyCapture = -1; InvalidateRect(g_wnd, NULL, FALSE); return true;
    }
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
    int group;                                 // jeu : 0 Image, 1 Ombres, 2 Distance et vue, 3 Confort ; MWCoop : 5 Ecran, 10 Fluidite, 11 Ombres
                                               // avancees, 12 Details et textures, 4 Lumieres ; coop : 6 Voix, 7 Visages/habits/pseudos, 8 Partie, 9 Touches
    const char *sec;                           // section du mwcoop.ini (absente : Graphismes)
    std::vector<int> pv;                       // pastilles de la page Reglages (maquette) : sous-ensemble de vals, noms courts pf / pe ;
    std::vector<const wchar_t *> pf, pe;       // vide : toutes les valeurs (pastilles si elles tiennent, sinon ◀ valeur ▶)
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
          { L"Jeu", L"30", L"60", L"75", L"120", L"144", L"165", L"240", L"Illimitées" }, { L"Game", L"30", L"60", L"75", L"120", L"144", L"165", L"240", L"Unlimited" }, 0, 10 },
        { "SyncVerticale", L"Synchro verticale", L"V-sync", -1, { -1, 0, 1 }, { L"Jeu", L"Non", L"Oui" }, { L"Game", L"Off", L"On" }, 0, 10 },
        { "OmbresSoleil", L"Ombres du soleil", L"Sun shadows", 1, {}, {}, {}, 3, 1 },
        { "OmbresLune", L"Ombres de la lune", L"Moon shadows", 0, {}, {}, {}, 2, 1 },
        { "OmbresMaisons", L"Ombres des maisons", L"House shadows", 1, {}, {}, {}, 2, 1 },
        { "OmbresDistance", L"Distance des ombres", L"Shadow distance", -1, { -1, 30, 60, 100, 150, 250, 400 },
          { L"Jeu", L"30 m", L"60 m", L"100 m", L"150 m", L"250 m", L"400 m" }, { L"Game", L"30 m", L"60 m", L"100 m", L"150 m", L"250 m", L"400 m" }, 3, 11 },
        { "OmbresCascades", L"Finesse des ombres", L"Shadow detail", -1, { -1, 1, 2, 4 }, { L"Jeu", L"Simple", L"Fine", L"Très fine" }, { L"Game", L"Basic", L"Fine", L"Very fine" }, 2, 11 },
        { "Distance", L"Distance d'affichage", L"Draw distance", 3000, { 500, 1000, 1500, 2000, 3000, 4500, 6000, 9000 },
          { L"500 m", L"1 km", L"1,5 km", L"2 km", L"3 km", L"4,5 km", L"6 km", L"9 km" }, { L"500 m", L"1 km", L"1.5 km", L"2 km", L"3 km", L"4.5 km", L"6 km", L"9 km" }, 3, 2 },
        { "Details", L"Niveau de détail", L"Level of detail", -1, { -1, 70, 100, 150, 200, 300 },
          { L"Jeu", L"Bas", L"Normal", L"Élevé", L"Très élevé", L"Max" }, { L"Game", L"Low", L"Normal", L"High", L"Very high", L"Max" }, 2, 12 },
        { "Textures", L"Textures", L"Textures", -1, { -1, 2, 1, 0 }, { L"Jeu", L"Quart", L"Moitié", L"Pleines" }, { L"Game", L"Quarter", L"Half", L"Full" }, 1, 12 },
        { "Filtrage", L"Filtrage des textures", L"Texture filtering", -1, { -1, 0, 1, 2 }, { L"Jeu", L"Non", L"Oui", L"Forcé" }, { L"Game", L"Off", L"On", L"Forced" }, 1, 12 },
        // Ray tracing (demande de JD, 11/10 : « comme GTAVC Coop ») : ombres du soleil par rayons (MWCoopRT.dll, carte DXR).
        // Le jeu est alors lance avec -force-gfx-direct (Unity dessine sur un seul fil : le mod peut s'inserer dans son rendu).
        { "RayTracing", L"Ombres du soleil en ray tracing", L"Ray-traced sun shadows", 0, {}, {}, {}, 2, 14 },
        { "RTRayons", L"Rayons par pixel", L"Rays per pixel", 2, { 1, 2, 4, 8 }, { L"1", L"2", L"4", L"8" }, { L"1", L"2", L"4", L"8" }, 3, 14 },
        { "RTPortee", L"Portée des ombres", L"Shadow range", 250, { 100, 150, 250, 400 },
          { L"100 m", L"150 m", L"250 m", L"400 m" }, { L"100 m", L"150 m", L"250 m", L"400 m" }, 2, 14 },
        { "RTDouceur", L"Bords des ombres", L"Shadow edges", 1, { 0, 1, 2, 4 }, { L"Nets", L"Légers", L"Doux", L"Très doux" }, { L"Sharp", L"Light", L"Soft", L"Very soft" }, 0, 14 },
        { "RTCorps", L"Ton corps dans les reflets et les ombres", L"Your body in reflections and shadows", 1, {}, {}, {}, 1, 14 },
        { "RTReflets", L"Reflets (carrosseries, surfaces lisses)", L"Reflections (car paint, smooth surfaces)", 100, { 0, 50, 100 },
          { L"Non", L"Discrets", L"Normaux" }, { L"Off", L"Subtle", L"Normal" }, 2, 14 },
        { "RTVitres", L"Reflets des vitres", L"Window reflections", 1, {}, {}, {}, 1, 14 },
        { "RTRebond", L"Rebond de lumière (illumination globale)", L"Light bounce (global illumination)", 100, { 0, 50, 100 },
          { L"Non", L"Léger", L"Normal" }, { L"Off", L"Light", L"Normal" }, 2, 14 },
        { "RTOcclusion", L"Occlusion ambiante (coins, dessous)", L"Ambient occlusion (corners, underneath)", 100, { 0, 50, 100, 150 },
          { L"Non", L"Légère", L"Normale", L"Forte" }, { L"Off", L"Light", L"Normal", L"Strong" }, 2, 14 },
        { "RTLampes", L"Ombres des lampes (phares, lampadaires)", L"Lamp shadows (headlights, street lights)", 8, { 0, 4, 8, 16 },
          { L"Non", L"4 lampes", L"8 lampes", L"16 lampes" }, { L"Off", L"4 lamps", L"8 lamps", L"16 lamps" }, 2, 14 },
        { "OmbresPhares", L"Ombres des phares", L"Headlight shadows", 0, { 0, 2, 4, 8 }, { L"Non", L"2 phares", L"4 phares", L"8 phares" }, { L"Off", L"2 lights", L"4 lights", L"8 lights" }, 3, 4 },
        { "OmbresLampadaires", L"Ombres des lampadaires", L"Street light shadows", 0, { 0, 1, 2, 4 }, { L"Non", L"1", L"2", L"4" }, { L"Off", L"1", L"2", L"4" }, 3, 4 },
        { "Halos", L"Halos lumineux (façon GTA)", L"Light coronas (GTA style)", 0, { 0, 1, 2 }, { L"Non", L"Discrets", L"Marqués" }, { L"Off", L"Subtle", L"Strong" }, 1, 4 },
        { "PorteePhares", L"Portée des phares", L"Headlight range", 0, { 0, 1 }, { L"Normale", L"Longue" }, { L"Normal", L"Long" }, 1, 4 },
        { "ChampVision", L"Champ de vision", L"Field of view", 60, { 50, 55, 60, 65, 70, 75, 80, 85, 90 },
          { L"50°", L"55°", L"60°", L"65°", L"70°", L"75°", L"80°", L"85°", L"90°" }, { L"50°", L"55°", L"60°", L"65°", L"70°", L"75°", L"80°", L"85°", L"90°" }, 0, 2 },
        { "Retroviseurs", L"Rétroviseurs", L"Mirrors", 1, { 0, 1, 2 }, { L"Coupés", L"Simples", L"Complets" }, { L"Off", L"Simple", L"Full" }, 2, 2 },
        { "Balancement", L"Tête qui balance à pied", L"Head bob on foot", 1, {}, {}, {}, 0, 3 },
        { "BalancementVoiture", L"Tête qui balance en voiture", L"Head bob in cars", 2, { 0, 1, 2 }, { L"Non", L"50 %", L"Oui" }, { L"Off", L"50 %", L"On" }, 0, 3 },
        { "CompteurImages", L"Compteur d'images", L"FPS counter", 0, {}, {}, {}, 0, 3 },
        { "IndicateurRapport", L"Rapport engagé à l'écran", L"Gear on screen", 1, {}, {}, {}, 0, 3 },
        { "PoussiereTrafic", L"Poussière de la circulation", L"Traffic dust", 1, {}, {}, {}, 1, 3 },
        { "TracesTrafic", L"Traces de pneus de la circulation", L"Traffic skid marks", 0, {}, {}, {}, 1, 3 },
        { "Pseudos", L"Pseudos des joueurs", L"Player names", 120, { 0, 30, 60, 120, 250 },
          { L"Cachés", L"30 m", L"60 m", L"120 m", L"250 m" }, { L"Hidden", L"30 m", L"60 m", L"120 m", L"250 m" }, 0, 7 },
        // HUD en jeu (18/10, UI/NewHud.cs et UI/HudSettings.cs du mod : memes cles, section [Hud]) : 16 style (tuiles de l'onglet
        // HUD), 17 personnaliser, 18 jauges affichees, 19 coop. Lus en direct par le jeu (fichier relu a chaque changement).
        { "Style", L"Style du HUD", L"HUD style", 0, { 0, 1, 2, 3 }, { L"Vanilla", L"Carte", L"Anneaux", L"Bandeau" }, { L"Vanilla", L"Card", L"Rings", L"Strip" }, 0, 16, "Hud" },
        { "Taille", L"Taille", L"Size", 100, { 70, 80, 90, 100, 110, 125, 150 }, { L"70 %", L"80 %", L"90 %", L"100 %", L"110 %", L"125 %", L"150 %" }, { L"70 %", L"80 %", L"90 %", L"100 %", L"110 %", L"125 %", L"150 %" }, 0, 17, "Hud",
          { 80, 100, 125, 150 }, { L"Petite", L"Normale", L"Grande", L"Très grande" }, { L"Small", L"Normal", L"Large", L"Huge" } },
        { "Position", L"Position", L"Position", -1, { -1, 0, 1, 2, 3, 4, 5 }, { L"Auto", L"Haut gauche", L"Haut droite", L"Bas gauche", L"Bas droite", L"Haut centre", L"Bas centre" },
          { L"Auto", L"Top left", L"Top right", L"Bottom left", L"Bottom right", L"Top centre", L"Bottom centre" }, 0, 17, "Hud" },
        { "Couleur", L"Couleur", L"Colour", 0, { 0, 1, 2, 3 }, { L"Glace", L"Bleu ciel", L"Aurore", L"Lilas" }, { L"Ice", L"Sky blue", L"Aurora", L"Lilac" }, 0, 17, "Hud" },
        { "Fond", L"Opacité du fond", L"Background opacity", 60, { 0, 30, 45, 60, 75, 90 }, { L"Aucun", L"30 %", L"45 %", L"60 %", L"75 %", L"90 %" }, { L"None", L"30 %", L"45 %", L"60 %", L"75 %", L"90 %" }, 0, 17, "Hud" },
        { "Estomper", L"Estomper les jauges au repos", L"Fade gauges at rest", 1, {}, {}, {}, 0, 17, "Hud" },
        { "Valeurs", L"Chiffres et noms", L"Numbers and names", 0, {}, {}, {}, 0, 17, "Hud" },
        { "JaugeSoif", L"Soif / problème", L"Thirst / problem", 1, {}, {}, {}, 0, 18, "Hud" },
        { "JaugeFaim", L"Faim", L"Hunger", 1, {}, {}, {}, 0, 18, "Hud" },
        { "JaugeStress", L"Stress", L"Stress", 1, {}, {}, {}, 0, 18, "Hud" },
        { "JaugeUrine", L"Urine", L"Urine", 1, {}, {}, {}, 0, 18, "Hud" },
        { "JaugeFatigue", L"Fatigue", L"Fatigue", 1, {}, {}, {}, 0, 18, "Hud" },
        { "JaugeSalete", L"Saleté", L"Dirtiness", 0, {}, {}, {}, 0, 18, "Hud" },
        { "JaugeTemperature", L"Température", L"Body temperature", 1, {}, {}, {}, 0, 18, "Hud" },
        { "JaugeSueur", L"Sueur", L"Sweat", 1, {}, {}, {}, 0, 18, "Hud" },
        { "JaugeArgent", L"Argent", L"Money", 1, {}, {}, {}, 0, 18, "Hud" },
        { "JaugeJour", L"Jour", L"Day", 1, {}, {}, {}, 0, 18, "Hud" },
        { "Equipe", L"Panneau Équipe", L"Team panel", 1, {}, {}, {}, 0, 19, "Hud" },
        { "Alertes", L"Alertes en haut de l'écran", L"Alerts at the top of the screen", 1, {}, {}, {}, 0, 19, "Hud" },
        // Coop, rangees par type (demande de JD, 10/10) : 6 voix, 7 visages, 8 partie et partage, 9 touches.
        { "Vocal", L"Vocal de proximité", L"Proximity voice", 1, {}, {}, {}, 0, 6, "Coop" },
        { "VocalPortee", L"Portée de la voix", L"Voice range", 35, { 15, 25, 35, 50, 80 },
          { L"15 m", L"25 m", L"35 m", L"50 m", L"80 m" }, { L"15 m", L"25 m", L"35 m", L"50 m", L"80 m" }, 0, 6, "Coop" },
        { "VolumeVocal", L"Volume des voix", L"Voice volume", 100, { 25, 50, 75, 100 },
          { L"25 %", L"50 %", L"75 %", L"100 %" }, { L"25 %", L"50 %", L"75 %", L"100 %" }, 0, 6, "Coop" },
        { "GainMicro", L"Volume de ton micro (pour les autres)", L"Your microphone volume (for the others)", 100, { 100, 150, 200, 300 },
          { L"Normal", L"150 %", L"200 %", L"300 %" }, { L"Normal", L"150 %", L"200 %", L"300 %" }, 0, 6, "Coop" },
        { "VoirVisages", L"Voir les images importées des autres", L"See others' imported images", 1, {}, {}, {}, 0, 7, "Coop" },
        { "ModsAuto", L"Accepter les fichiers de l'hôte", L"Accept the host's files", 0, {}, {}, {}, 0, 8, "Opt" },
        { "ArgentDepart", L"Argent de départ des invités", L"Guests' starting money", 1, {}, {}, {}, 0, 8, "Coop" },
        { "Triches", L"Autoriser les triches (hôte)", L"Allow cheats (host)", 0, {}, {}, {}, 0, 8, "Coop" },
        { "Renverser", L"Les voitures renversent les joueurs (amis, PNJ)", L"Cars run players over (friends, NPCs)", 1, {}, {}, {}, 0, 8, "Coop" },
        // (suggestion de квазарик, 13/10 : « tuer les autres a coups de poing, option de l'hote ») -- Sync/Pvp.cs
        { "PvP", L"Coups de poing entre joueurs (hôte)", L"Punches between players (host)", 0, {}, {}, {}, 0, 8, "Coop" },
        { "Jokkis", L"Course sur glace : Jokkis à louer (hôte, bêta)", L"Ice race: Jokkis rentals (host, beta)", 0, {}, {}, {}, 0, 8, "Coop" },   // (Sync/Jokkis.cs : les joueurs sans la Rivett courent dans une des 16 Jokkis du jeu ; en essai)
        // Ventti (16/10, lu par l'HOTE seulement) : au-dela de 4000 mk le cochon joue la Ruscko, la Rivett ou la maison ; gagnees ou
        // perdues par un joueur, elles le sont pour tout le monde (c'est la sauvegarde de l'hote). 0 : chacun ses enjeux. La table
        // renversee par le cochon fache est toujours partagee. (La table n'a pas de texte d'aide : ce commentaire en tient lieu.)
        { "VenttiEnjeux", L"Ventti : voiture et maison jouées pour tout le groupe", L"Ventti: car and house stakes apply to the whole group", 1, {}, {}, {}, 0, 8, "Coop" },
        // Un seul interrupteur pour tout ce que MWCoop ajoute au jeu (demande de Menor, 12/10 : « un jeu 100 % vanilla » ;
        // JD : un bouton pour tous les ajouts, presents et a venir -- Sync/Ajouts.cs).
        { "SansAjouts", L"Tout désactiver (jeu vanilla)", L"Disable them all (vanilla game)", 0, {}, {}, {}, 0, 15, "Coop" },
        // (et un interrupteur par ajout -- JD, 13/10 : garder le drone, retirer la boutique et le musee...)
        { "AjoutDrone", L"Caméra drone (F8)", L"Drone camera (F8)", 1, {}, {}, {}, 0, 15, "Coop" },
        { "AjoutSacs", L"Sacs de couchage", L"Sleeping bags", 1, {}, {}, {}, 0, 15, "Coop" },
        { "AjoutBoutique", L"Boutique s21 (vidéo, KARTTURI)", L"s21 shop (video, KARTTURI)", 1, {}, {}, {}, 0, 15, "Coop" },
        { "AjoutMusee", L"Musée de la Corse", L"Museum of Corsica", 1, {}, {}, {}, 0, 15, "Coop" },
        // (VAZ-2108 Rally des joueurs, au F10 : une couleur et un numero chacun -- JD, 18/10 ; Sync/Vaz.cs)
        { "AjoutVaz", L"VAZ-2108 des joueurs (F10)", L"Players' VAZ-2108 (F10)", 1, {}, {}, {}, 0, 15, "Coop" },
        { "Touche:Tchat", L"Écrire aux autres (tchat)", L"Write to the others (chat)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:PorteMonnaie", L"Porte-monnaie (donner de l'argent)", L"Wallet (give money)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Vocal", L"Parler (vocal, touche tenue)", L"Talk (voice, hold the key)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Triches", L"Panneau des triches", L"Cheat panel", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Carte", L"Carte (joueurs en direct)", L"Map (live players)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Vue", L"Vue à la 3e personne (à pied, en véhicule)", L"Third-person view (on foot, in vehicles)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Marqueur", L"Marqueur partagé (court : poser, tenu : retirer)", L"Shared marker (tap: place, hold: remove)", 0, {}, {}, {}, 0, 9, "Touches" },
        // Camera drone (13) : une pastille par action.
        { "Touche:Drone", L"Passer en drone / revenir (mode photo)", L"Drone on / off (photo mode)", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DronePhoto", L"Prendre la photo", L"Take the photo", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneAvant", L"Avancer", L"Move forward", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneArriere", L"Reculer", L"Move back", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneGauche", L"Aller à gauche", L"Move left", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneDroite", L"Aller à droite", L"Move right", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneMonter", L"Monter", L"Go up", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneDescendre", L"Descendre", L"Go down", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneRapide", L"Plus vite (touche tenue)", L"Faster (hold the key)", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneZoomPlus", L"Zoomer (aussi la molette)", L"Zoom in (also the wheel)", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneZoomMoins", L"Dézoomer", L"Zoom out", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DronePencherG", L"Pencher à gauche", L"Tilt left", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DronePencherD", L"Pencher à droite", L"Tilt right", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneRecentrer", L"Recentrer (penché et zoom)", L"Reset (tilt and zoom)", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneGrille", L"Grille du viseur", L"Viewfinder grid", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Touche:DroneInterface", L"Cacher le viseur", L"Hide the viewfinder", 0, {}, {}, {}, 0, 13, "Touches" },
        { "Marqueurs", L"Voir les marqueurs des autres", L"See others' markers", 1, {}, {}, {}, 0, 9, "Coop" },   // (marqueur partage, une couleur par joueur : Sync/Markers.cs)
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
        { "ModeEcran", L"Fenêtre", L"Window", -1, { -1, 0, 1, 2 }, { L"Jeu", L"Fenêtré", L"Plein écran", L"Sans bordure" }, { L"Game", L"Windowed", L"Fullscreen", L"Borderless" }, 0, 5 },
        { "QualiteUnity", L"Qualité du moteur", L"Engine quality", -1, { -1, 0, 1, 3, 5 }, { L"Jeu", L"Basse", L"Moyenne", L"Bonne", L"Golden Eye" }, { L"Game", L"Low", L"Medium", L"Good", L"Golden Eye" }, 3, 5 },
    });
    // Pastilles de la page Reglages (maquettes du 17/10) : les valeurs les plus utiles, en court ; une valeur hors de la
    // liste (menu F10 du jeu, ancien lanceur) s'ajoute a sa place avec son nom complet.
    auto pills = [&](const char *k, std::vector<int> v, std::vector<const wchar_t *> f, std::vector<const wchar_t *> e) {
        for (GfxRow &r : rows) if (!strcmp(r.key, k)) { r.pv = v; r.pf = f; r.pe = e.empty() ? f : e; }
    };
    pills("ImagesMax", { -1, 60, 144, 240, 0 }, { L"Jeu", L"60", L"144", L"240", L"∞" }, { L"Game", L"60", L"144", L"240", L"∞" });
    pills("Distance", { 1000, 2000, 3000, 6000, 9000 }, { L"1 km", L"2", L"3", L"6", L"9" }, {});
    pills("Details", { -1, 100, 150, 300 }, { L"Jeu", L"100 %", L"150", L"300" }, { L"Game", L"100 %", L"150", L"300" });
    pills("OmbresDistance", { 60, 150, 400 }, { L"60", L"150 m", L"400" }, {});
    pills("OmbresCascades", { -1, 1, 2, 4 }, { L"Jeu", L"Simple", L"Fine", L"Max" }, { L"Game", L"Basic", L"Fine", L"Max" });
    pills("OmbresPhares", { 0, 2, 4, 8 }, { L"Non", L"2", L"4", L"8" }, { L"Off", L"2", L"4", L"8" });
    pills("OmbresLampadaires", { 0, 1, 2, 4 }, { L"Non", L"1", L"2", L"4" }, { L"Off", L"1", L"2", L"4" });
    pills("Pseudos", { 0, 30, 120, 250 }, { L"Off", L"30 m", L"120", L"250" }, {});
    pills("VocalPortee", { 15, 25, 35, 50, 80 }, { L"15", L"25", L"35 m", L"50", L"80" }, {});
    pills("VolumeVocal", { 50, 75, 100 }, { L"50", L"75", L"100 %" }, {});
    pills("GainMicro", { 100, 150, 200, 300 }, { L"Normal", L"150", L"200", L"300 %" }, {});
    return rows;
}

// Carte graphique compatible (DXR 1.1 : requetes de rayons), verifiee une fois : d3d12.dll chargee a la demande (pas
// d'import : voir launcher-no-static-dll-imports).
static int g_dxr = -1;
static bool GfxDxr()
{
    if (g_dxr >= 0) return g_dxr == 1;
    g_dxr = 0;
    HMODULE m = LoadLibraryW(L"d3d12.dll");
    typedef HRESULT (WINAPI *Create_t)(IUnknown *, D3D_FEATURE_LEVEL, REFIID, void **);
    Create_t create = m ? (Create_t)GetProcAddress(m, "D3D12CreateDevice") : NULL;
    ID3D12Device *d = NULL;
    if (create && SUCCEEDED(create(NULL, D3D_FEATURE_LEVEL_11_0, __uuidof(ID3D12Device), (void **)&d)) && d) {
        D3D12_FEATURE_DATA_D3D12_OPTIONS5 o5 = {};
        if (SUCCEEDED(d->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS5, &o5, sizeof(o5))) && o5.RaytracingTier >= D3D12_RAYTRACING_TIER_1_1) g_dxr = 1;
        d->Release();
    }
    LaunchLog("ray tracing : carte %s (DXR 1.1)", g_dxr == 1 ? "compatible" : "NON compatible");
    return g_dxr == 1;
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
// Ligne sans effet : reglages RT sans carte compatible ou RT coupe ; ombres d'Unity remplacees quand le RT est actif.
static bool GfxRowOff(const GfxRow &r, const wchar_t **why)
{
    bool rt = GfxGet("RayTracing") == 1 && GfxDxr();
    if (r.group == 14 && !GfxDxr()) { *why = T(L"carte non compatible", L"GPU not supported"); return true; }
    if ((r.group == 17 || r.group == 18) && GfxGet("Style") == 0) { *why = T(L"style Vanilla", L"Vanilla style"); return true; }
    if (r.group == 14 && strcmp(r.key, "RayTracing") && !rt) { *why = T(L"RT coupé", L"RT off"); return true; }
    if (rt && (!strcmp(r.key, "OmbresDistance") || !strcmp(r.key, "OmbresCascades") || !strcmp(r.key, "OmbresLune"))) { *why = T(L"ray tracing", L"ray tracing"); return true; }
    if (rt && GfxGet("RTLampes") != 0 && (!strcmp(r.key, "OmbresPhares") || !strcmp(r.key, "OmbresLampadaires"))) { *why = T(L"ray tracing", L"ray tracing"); return true; }
    return false;
}

// Prereglages (maquette Jeu, 17/10) : Vanilla = chaque option a la valeur du jeu lui-meme (cle retiree pour les reglages
// du moteur), Leger (vieux PC), Equilibre (60 i/s), Joli (ombres, HDR), Ultra (tout au max, ray tracing si la carte le
// permet). « Perso » n'est pas un prereglage : sa tuile s'allume quand les valeurs ne correspondent a aucun. Seules les
// options de qualite sont touchees ; ecran, images par seconde, champ de vision, confort et pseudos restent a chacun.
//                     Vanilla  Leger    Equilibre  Joli      Ultra
//  QualiteUnity        jeu     basse    moyenne    bonne     Golden Eye
//  Anticrenelage       jeu     non      oui        oui       oui
//  HDR                 jeu     non      oui        oui       oui
//  Bloom               jeu     non      non        oui       oui
//  Contraste           jeu     non      non        non       non
//  RayonsSoleil        jeu     non      non        oui       oui
//  OmbresSoleil        jeu     oui      oui        oui       oui
//  OmbresLune          jeu     non      non        oui       oui
//  OmbresMaisons       jeu     non      oui        oui       oui
//  OmbresDistance      jeu     30 m     60 m       150 m     400 m
//  OmbresCascades      jeu     simple   fine       fine      tres fine
//  Distance            jeu     1,5 km   3 km       4,5 km    9 km
//  Details             jeu     bas      normal     eleve     max
//  Textures            jeu     moitie   pleines    pleines   pleines
//  Filtrage            jeu     non      oui        oui       force
//  Retroviseurs        jeu     simples  simples    complets  complets
//  PoussiereTrafic     jeu     non      oui        oui       oui
//  TracesTrafic        jeu     non      non        oui       oui
//  OmbresPhares        jeu     non      non        2         4
//  OmbresLampadaires   jeu     non      non        1         2
//  Halos               jeu     non      discrets   discrets  marques
//  RayTracing          non     non      non        non       oui (carte DXR, sinon non)
struct GfxPreset { const wchar_t *fr, *en, *dFr, *dEn; std::vector<std::pair<const char *, int>> v; };
static const std::vector<GfxPreset> &GfxPresets()
{
    static std::vector<GfxPreset> p;
    if (!p.empty()) return p;
    static const char *keys[] = { "QualiteUnity", "Anticrenelage", "HDR", "Bloom", "Contraste", "RayonsSoleil", "OmbresSoleil", "OmbresLune", "OmbresMaisons",
                                  "OmbresDistance", "OmbresCascades", "Distance", "Details", "Textures", "Filtrage", "Retroviseurs", "PoussiereTrafic", "TracesTrafic",
                                  "OmbresPhares", "OmbresLampadaires", "Halos", "RayTracing" };
    const int n = (int)(sizeof(keys) / sizeof(keys[0]));
    static const int vals[4][22] = {
        { 0, 0, 0, 0, 0, 0, 1, 0, 0, 30, 1, 1500, 70, 1, 0, 1, 0, 0, 0, 0, 0, 0 },
        { 1, 1, 1, 0, 0, 0, 1, 0, 1, 60, 2, 3000, 100, 0, 1, 1, 1, 0, 0, 0, 1, 0 },
        { 3, 1, 1, 1, 0, 1, 1, 1, 1, 150, 2, 4500, 150, 0, 1, 2, 1, 1, 2, 1, 1, 0 },
        { 5, 1, 1, 1, 0, 1, 1, 1, 1, 400, 4, 9000, 300, 0, 2, 2, 1, 1, 4, 2, 2, 1 } };
    std::vector<std::pair<const char *, int>> van;
    for (int i = 0; i < n; i++) { int d = 0; for (const GfxRow &r : GfxRows()) if (!strcmp(r.key, keys[i])) d = r.def; van.push_back(std::make_pair(keys[i], d)); }
    van.back().second = 0;   // (RayTracing : jamais en Vanilla)
    p.push_back({ L"Vanilla", L"Vanilla", L"tel quel", L"as is", van });
    static const wchar_t *nf[] = { L"Léger", L"Équilibré", L"Joli", L"Ultra" }, *ne[] = { L"Light", L"Balanced", L"Pretty", L"Ultra" };
    static const wchar_t *df[] = { L"vieux PC", L"60 i/s", L"ombres, HDR", L"ray tracing" }, *de[] = { L"old PC", L"60 fps", L"shadows, HDR", L"ray tracing" };   // (Vanilla : "le jeu tel quel")
    for (int k = 0; k < 4; k++) {
        std::vector<std::pair<const char *, int>> v;
        for (int i = 0; i < n; i++) v.push_back(std::make_pair(keys[i], vals[k][i]));
        if (k == 3 && !GfxDxr()) v.back().second = 0;   // (Ultra sans carte DXR : tout au max, sans ray tracing)
        p.push_back({ nf[k], ne[k], df[k], de[k], v });
    }
    return p;
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

// ---------------------------------------------------------------- page REGLAGES (17/10 : une seule page, onglets en pastilles)
// Maquettes validees (Jeu, Image & ray tracing, Son & vocal, Co-op, Touches, Theme, Lanceur) : TAB_GFX ouvre l'onglet Jeu,
// TAB_COOP l'onglet Co-op (g_tab reste, la pastille change g_setTab). Chaque reglage du jeu (GfxRows), du lanceur (g_opts)
// et de l'ancienne page Reglages est dans un seul onglet. Le contenu est decrit par des tables : kSetCards (cartes d'un
// onglet, colonne gauche ou droite, element d'en-tete) et kSetItems (lignes d'une carte : reglage du jeu, option du lanceur,
// titre de section, ou element special dessine a part ; pleine largeur avant ou apres les deux sous-colonnes). Le champ
// Chercher de l'en-tete filtre ces memes lignes, toutes cartes confondues. Les pastilles (choix a segments) suivent la
// maquette : valeurs courtes (GfxRow::pv), un nom qui ne tient pas passe sur deux lignes, sinon ◀ valeur ▶.
enum { SET_JEU, SET_IMAGE, SET_HUD, SET_SON, SET_COOP, SET_TOUCHES, SET_THEME, SET_LANCEUR, SET_N };
static const wchar_t *kSetTabFr[SET_N] = { L"Jeu", L"Image & ray tracing", L"HUD", L"Son & vocal", L"Co-op", L"Touches", L"Thème", L"Lanceur" };
static const wchar_t *kSetTabEn[SET_N] = { L"Game", L"Image & ray tracing", L"HUD", L"Sound & voice", L"Co-op", L"Keys", L"Theme", L"Launcher" };
static int g_setTab = SET_JEU;
static std::wstring g_setSearch;        // champ Chercher (vide : les onglets)
static bool g_setFocus;                 // champ Chercher actif (curseur)
static int g_setFound;                  // lignes trouvees (sous-titre de l'en-tete)
static float g_setContentH;             // hauteur du contenu (molette)
static float g_setTop, g_setBottom;     // zone visible : zones cliquables seulement dedans
static RectF g_setSteamRow, g_setCompatRow, g_setBuy, g_setUninst, g_setApiRow;   // (bulles du guide : tuto.inc)
static const RectF kSetArea(264, 136, 1000, 608);                   // sous les pastilles (y 92..128)
static const RectF kGfxTop(264, 136, 494, 128), kGfxArea(264, 136, 1000, 608);   // (bulles du guide 30-32 : prereglages, onglet Jeu)
static const float kSetPad = 18;
// Pas des lignes : densite choisie (Reglages > Theme) -- aeree, normale, compacte.
// (et la taille du texte : les lignes grandissent avec lui)
static float SetRowH() { return 36 * UiDensity() * UiTextScale(); }       // ligne sans explication
static float SetRowDH() { return 46 * UiDensity() * UiTextScale(); }      // ligne avec explication grise
static float SetSecH() { return 26 * UiDensity() * UiTextScale(); }       // titre de section
enum { UI_SET_TAB = 2900, UI_SET_SEARCH = 2910, UI_SET_SEARCHX, UI_SET_THEMECARD = 2920, UI_SET_ACCENT = 2925, UI_SET_AURORE = 2931, UI_SET_WINTHEME = 2935, UI_SET_THEMEDEF,
       UI_SET_MAXP = 2940, UI_SET_PRIV = 2945, UI_SET_AJOUTS = 2948, UI_SET_AJOUT = 2951, UI_SET_DRONEKB = 2956,
       UI_SET_NOTES = 2960, UI_SET_LOGDIR, UI_SET_LOGZIP, UI_SET_DISCORDASK, UI_SET_SKIN, UI_SET_CAR, UI_SET_CONTENT, UI_SET_STEAMLINE, UI_SET_GOIMAGE, UI_SET_API, UI_SET_GITHUB,
       UI_SET_DEFAULT = 2972, UI_SET_SAVE, UI_SET_NETTEST, UI_SET_HOSTGUIDE, UI_SET_REPAIR, UI_SET_NIGHTLY, UI_SET_GLASS, UI_SET_RTTOGGLE,   // (2972-2979)
       UI_SET_HUDSTYLE = 2980,       // + style (0 Vanilla .. 3 Bandeau) : tuiles de l'onglet HUD (2980-2983)
       UI_SETV = 12000,              // + ligne * 10 + valeur (pastilles d'un reglage du jeu)
       UI_SET_TEXTE = 13000,         // + k (0..6) : taille du texte 80..110 %
       UI_SET_FOV = 13020,           // + k : champ de vision (curseur)
       UI_SET_PORTSEG = 13040,       // + k : port (pastilles)
       UI_SET_SESS = 13050,          // + k (0 3, 1 10, 2 30) : sessions gardees
       UI_SET_DENS = 13054 };        // + k (0 aeree, 1 normale, 2 compacte) : densite
static void LogsOpenFolder();
static void WikiOpenHost();   // (launcher.cpp : article du guide Heberger une partie)
static void HudPreview(Graphics &g, RectF box, int style, bool big);   // (onglet HUD : apercu, plus bas)
static void LogsZip();
static float GfxMaxScroll() { return max(0.0f, g_setContentH - kSetArea.Height); }

enum { SI_GFX, SI_OPT, SI_SEC, SI_BREAK, SI_X };   // reglage du jeu (cle de GfxRows), option du lanceur (cle de g_opts), section, 2e sous-colonne, special (cle = nom)
// hdr : element a droite du titre (NULL : rien) -- goimage (lien vers l'onglet Image), hostchip, netchip (resultat du test de
// connexion), dronekb (disposition ZQSD / WASD / fleches), updchip (etat de la mise a jour), mwcver (version du jeu trouvee).
struct SetCard { int tab, col; const wchar_t *fr, *en, *sFr, *sEn; const char *hdr; };
static const SetCard kSetCards[] = {
    { SET_JEU, 0, L"Préréglage", L"Preset", L"Règle tout d'un coup · chaque ligne reste modifiable · Vanilla = le jeu tel quel", L"Sets everything at once · every line stays editable · Vanilla = the game as is" },   // 0
    { SET_JEU, 0, L"Affichage", L"Display", NULL, NULL },                                                                                                           // 1
    { SET_JEU, 1, L"Confort & affichage en jeu", L"Comfort & in-game display", NULL, NULL },                                                                         // 2
    { SET_JEU, 1, L"Lumière & ombres", L"Light & shadows", NULL, NULL, "goimage" },                                                                                 // 3
    { SET_IMAGE, 0, L"Image", L"Image", NULL, NULL },                                                                                                               // 4
    { SET_IMAGE, 0, L"Ombres", L"Shadows", NULL, NULL },                                                                                                            // 5
    { SET_IMAGE, 1, L"Ray tracing", L"Ray tracing", L"Carte DXR 1.1 · en développement", L"DXR 1.1 GPU · in development" },                                         // 6
    { SET_SON, 0, L"Vocal de proximité", L"Proximity voice", L"Qui t'entend, et à quel volume", L"Who hears you, and how loud" },                                   // 7
    { SET_SON, 1, L"Comment ça marche", L"How it works", NULL, NULL },                                                                                              // 8
    { SET_COOP, 0, L"Quand j'héberge", L"When I host", L"Repris à l'ouverture du salon · un changement là-bas ne vaut que pour lui", L"Taken when the lobby opens · a change there only applies to that lobby", "hostchip" },   // 9
    { SET_COOP, 1, L"Chez moi", L"On my side", L"Hôte ou invité", L"Host or guest" },                                                                                // 10
    { SET_COOP, 1, L"Réseau & lancement", L"Network & launch", NULL, NULL, "netchip" },                                                                             // 11
    { SET_TOUCHES, 0, L"En jeu", L"In game", L"Clique sur une touche, puis appuie sur la nouvelle · Échap : annuler · Suppr : défaut", L"Click a key, then press the new one · Esc: cancel · Del: default" },   // 12
    { SET_TOUCHES, 1, L"Caméra drone", L"Drone camera", L"Mode photo (F8)", L"Photo mode (F8)", "dronekb" },                                                        // 13
    { SET_THEME, 0, L"Thème du lanceur", L"Launcher theme", NULL, NULL },                                                                                           // 14
    { SET_THEME, 0, L"Personnaliser", L"Customize", NULL, NULL },                                                                                                   // 15
    { SET_THEME, 1, L"Aperçu en direct", L"Live preview", NULL, NULL },                                                                                             // 16
    { SET_LANCEUR, 0, L"Langue", L"Language", L"Vaut pour le lanceur et le menu F10 en jeu ; les textes du jeu suivent sa langue à lui", L"For the launcher and the in-game F10 menu; the game's own texts follow its language" },   // 17
    { SET_LANCEUR, 0, L"Version & mises à jour", L"Version & updates", NULL, NULL, "updchip" },                                                                     // 18
    { SET_LANCEUR, 1, L"Jeu", L"Game", NULL, NULL, "mwcver" },                                                                                                      // 19
    { SET_LANCEUR, 1, L"Dépannage", L"Troubleshooting", NULL, NULL },                                                                                               // 20
    { SET_HUD, 0, L"Style du HUD", L"HUD style", L"Vanilla = le HUD d'origine du jeu · les autres le remplacent", L"Vanilla = the game's own HUD · the others replace it" },   // 21
    { SET_HUD, 0, L"Personnaliser", L"Customise", L"Pour le style choisi · appliqué tout de suite, même en jeu", L"For the chosen style · applied right away, even in game" },   // 22
    { SET_HUD, 1, L"Aperçu", L"Preview", NULL, NULL },                                                                                                               // 23
    { SET_HUD, 1, L"Jauges affichées", L"Gauges shown", NULL, NULL },                                                                                                // 24
    { SET_HUD, 1, L"Coop", L"Co-op", L"Aussi avec Vanilla", L"Also with Vanilla" },                                                                                  // 25
};
static const int kSetCardN = (int)(sizeof(kSetCards) / sizeof(kSetCards[0]));
// fr/en : nom (SI_GFX / SI_OPT : remplace celui de la table ; SI_X : pour la recherche) ; dFr/dEn : explication grise dessous.
// wide : 1 toute la largeur avant les sous-colonnes, 2 apres (pied de carte : boutons Par defaut / Enregistrer...).
// Pied ("footer") : fr/en = legende, dFr = boutons -- D Par defaut, E Enregistrer, G Revoir le guide de l'hote, T Tester ma connexion.
struct SetItem { int card, kind; const char *key; const wchar_t *fr, *en, *dFr, *dEn; int wide; };
static const SetItem kSetItems[] = {
    { 0, SI_X, "presets", L"Préréglage", L"Preset", NULL, NULL },
    { 1, SI_GFX, "Resolution", NULL, NULL, NULL, NULL }, { 1, SI_GFX, "ModeEcran", NULL, NULL, NULL, NULL },
    { 1, SI_GFX, "ImagesMax", NULL, NULL, NULL, NULL }, { 1, SI_GFX, "SyncVerticale", NULL, NULL, NULL, NULL },
    { 1, SI_GFX, "ChampVision", NULL, NULL, L"60° = le jeu", L"60° = the game" }, { 1, SI_GFX, "QualiteUnity", NULL, NULL, NULL, NULL },
    { 1, SI_GFX, "Distance", NULL, NULL, NULL, NULL }, { 1, SI_GFX, "Details", NULL, NULL, NULL, NULL },
    { 1, SI_GFX, "Textures", NULL, NULL, L"Pleines = plus de mémoire vidéo", L"Full = more video memory" }, { 1, SI_GFX, "Filtrage", NULL, NULL, NULL, NULL }, { 1, SI_GFX, "Anticrenelage", NULL, NULL, NULL, NULL },
    { 2, SI_GFX, "Balancement", NULL, NULL, NULL, NULL }, { 2, SI_GFX, "BalancementVoiture", NULL, NULL, NULL, NULL }, { 2, SI_GFX, "Retroviseurs", NULL, NULL, L"Complets = coûteux", L"Full = costly" },
    { 2, SI_GFX, "IndicateurRapport", NULL, NULL, NULL, NULL }, { 2, SI_GFX, "CompteurImages", NULL, NULL, NULL, NULL },
    { 2, SI_GFX, "Pseudos", NULL, NULL, L"Distance d'affichage", L"Display distance" }, { 2, SI_GFX, "PoussiereTrafic", NULL, NULL, NULL, NULL }, { 2, SI_GFX, "TracesTrafic", NULL, NULL, NULL, NULL },
    // Lumiere & ombres (maquette Jeu) : les huit lignes les plus utiles, en deux sous-colonnes ; aussi dans l'onglet Image.
    { 3, SI_GFX, "HDR", NULL, NULL, NULL, NULL }, { 3, SI_GFX, "Bloom", NULL, NULL, NULL, NULL }, { 3, SI_GFX, "RayonsSoleil", NULL, NULL, NULL, NULL }, { 3, SI_GFX, "Halos", L"Halos des lampes", L"Lamp coronas", NULL, NULL },
    { 3, SI_BREAK, NULL, NULL, NULL, NULL, NULL },
    { 3, SI_GFX, "OmbresSoleil", NULL, NULL, NULL, NULL }, { 3, SI_GFX, "OmbresPhares", NULL, NULL, NULL, NULL }, { 3, SI_GFX, "OmbresCascades", NULL, NULL, NULL, NULL }, { 3, SI_GFX, "OmbresDistance", NULL, NULL, NULL, NULL },
    { 3, SI_X, "rtbox", L"Ray tracing", L"Ray tracing", L"Ombres du soleil et des lampes, reflets, rebond de lumière, occlusion", L"Sun and lamp shadows, reflections, light bounce, occlusion", 2 },
    { 3, SI_X, "footer", L"Appliqués au prochain lancement du jeu.", L"Applied at the next game start.", L"DE", NULL, 2 },
    { 4, SI_GFX, "HDR", NULL, NULL, NULL, NULL }, { 4, SI_GFX, "Bloom", NULL, NULL, NULL, NULL }, { 4, SI_GFX, "Contraste", NULL, NULL, NULL, NULL }, { 4, SI_GFX, "RayonsSoleil", NULL, NULL, NULL, NULL },
    { 4, SI_GFX, "Halos", NULL, NULL, NULL, NULL }, { 4, SI_GFX, "PorteePhares", NULL, NULL, NULL, NULL },
    { 5, SI_GFX, "OmbresSoleil", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresLune", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresMaisons", NULL, NULL, NULL, NULL },
    { 5, SI_GFX, "OmbresDistance", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresCascades", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresPhares", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresLampadaires", NULL, NULL, NULL, NULL },
    { 6, SI_GFX, "RayTracing", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTRayons", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTPortee", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTDouceur", NULL, NULL, NULL, NULL },
    { 6, SI_GFX, "RTCorps", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTReflets", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTVitres", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTRebond", NULL, NULL, NULL, NULL },
    { 6, SI_GFX, "RTOcclusion", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTLampes", NULL, NULL, NULL, NULL },
    { 6, SI_X, "footer", L"Appliqués au prochain lancement du jeu · en jeu : F10.", L"Applied at the next game start · in game: F10.", L"DE", NULL, 2 },
    { 7, SI_GFX, "Vocal", NULL, NULL, NULL, NULL }, { 7, SI_GFX, "VocalPortee", NULL, NULL, NULL, NULL }, { 7, SI_GFX, "VolumeVocal", NULL, NULL, L"Ce que tu entends", L"What you hear" },
    { 7, SI_GFX, "GainMicro", L"Mon micro (pour les autres)", L"My microphone (for the others)", NULL, NULL },
    { 7, SI_X, "footer", L"Appliqués tout de suite, même en jeu.", L"Applied right away, even in game.", L"DE", NULL, 2 },
    { 8, SI_X, "voicehelp", NULL, NULL, NULL, NULL },
    { 9, SI_SEC, NULL, L"Règles", L"Rules", NULL, NULL },
    { 9, SI_GFX, "ArgentDepart", NULL, NULL, L"Chaque invité démarre avec le pécule du jeu", L"Every guest starts with the game's nest egg" },
    { 9, SI_GFX, "Triches", L"Autoriser les triches (F7)", L"Allow cheats (F7)", NULL, NULL },
    { 9, SI_GFX, "Renverser", L"Les voitures renversent les joueurs", L"Cars run players over", L"Amis et PNJ", L"Friends and NPCs" },
    { 9, SI_GFX, "PvP", L"Coups de poing entre joueurs", L"Punches between players", NULL, NULL },
    { 9, SI_SEC, NULL, L"Jeux & ajouts", L"Games & additions", NULL, NULL },
    { 9, SI_GFX, "VenttiEnjeux", NULL, NULL, NULL, NULL },
    // (Jokkis a louer retirees le 18/10 : la VAZ-2108 prend la place d'une IA a la course sur glace)
    { 9, SI_X, "ajouts", L"Ajouts MWCoop", L"MWCoop additions", L"Aucun = jeu vanilla", L"None = vanilla game" },
    { 9, SI_X, "ajoutchips", NULL, NULL, NULL, NULL },
    { 9, SI_SEC, NULL, L"Salon", L"Lobby", NULL, NULL },
    { 9, SI_X, "max", L"Joueurs au plus", L"Players at most", NULL, NULL },
    { 9, SI_X, "prive", L"Salon par défaut", L"Default lobby", L"Privé : code de session demandé", L"Private: session code required" },
    { 9, SI_X, "footer", NULL, NULL, L"GD", NULL, 2 },
    // Chez moi (maquette Co-op) : vocal a gauche, partage et affichage a droite (les lignes du vocal sont aussi dans Son & vocal).
    { 10, SI_SEC, NULL, L"Vocal", L"Voice", NULL, NULL },
    { 10, SI_GFX, "Vocal", NULL, NULL, NULL, NULL },
    { 10, SI_GFX, "Touche:Vocal", L"Parler", L"Talk", L"Touche tenue", L"Hold the key" },
    { 10, SI_GFX, "VocalPortee", NULL, NULL, NULL, NULL }, { 10, SI_GFX, "VolumeVocal", NULL, NULL, NULL, NULL },
    { 10, SI_GFX, "GainMicro", L"Mon micro (pour les autres)", L"My microphone (for the others)", NULL, NULL },
    { 10, SI_BREAK, NULL, NULL, NULL, NULL, NULL },
    { 10, SI_SEC, NULL, L"Partage & affichage", L"Sharing & display", NULL, NULL },
    { 10, SI_GFX, "ModsAuto", NULL, NULL, L"Mods et contenu, sans redemander", L"Mods and content, without asking again" },
    { 10, SI_GFX, "VoirVisages", NULL, NULL, L"Visages, peintures, posters", L"Faces, paints, posters" },
    { 10, SI_GFX, "Marqueurs", NULL, NULL, NULL, NULL },
    { 10, SI_GFX, "CarteMaintenir", L"Carte (M)", L"Map (M)", NULL, NULL },
    { 10, SI_OPT, "ContenuEnvoye", L"Mon contenu envoyé", L"My content sent", NULL, NULL },
    { 11, SI_X, "net", L"Réseau par défaut", L"Default network", L"Steam : amis ; IP / VPN : port à ouvrir", L"Steam: friends; IP / VPN: port to open", 1 },
    { 11, SI_OPT, "Port", L"Port (IP / VPN)", L"Port (IP / VPN)", L"UDP et TCP ; à rediriger sur la box", L"UDP and TCP; to forward on the router", 1 },
    { 11, SI_OPT, "FermerLanceur", L"Fermer le lanceur pendant le jeu", L"Close the launcher during the game", L"Sinon il reste ouvert : joueurs, ping, renvois", L"Otherwise it stays open: players, ping, kicks" },
    { 11, SI_BREAK, NULL, NULL, NULL, NULL, NULL },
    { 11, SI_OPT, "LancerSteam", L"Lancer le jeu par Steam", L"Start the game through Steam", L"Seulement si MWCoop ne se charge pas depuis le lanceur", L"Only if MWCoop doesn't load from the launcher" },
    { 11, SI_X, "steamline", L"Option de lancement Steam", L"Steam launch option", L"Pour que JOUER ouvre MWCoop (facultatif)", L"So that PLAY opens MWCoop (optional)" },
    { 11, SI_X, "footer", L"Vérifie le port, le pare-feu et Steam.", L"Checks the port, the firewall and Steam.", L"TE", NULL, 2 },
    { 12, SI_SEC, NULL, L"Avec les autres", L"With the others", NULL, NULL },
    { 12, SI_GFX, "Touche:Tchat", NULL, NULL, NULL, NULL },
    { 12, SI_GFX, "Touche:Vocal", L"Parler", L"Talk", L"Vocal de proximité, touche tenue", L"Proximity voice, hold the key" },
    { 12, SI_GFX, "Touche:PorteMonnaie", L"Porte-monnaie", L"Wallet", L"Donner de l'argent au joueur visé", L"Give money to the player you look at" },
    { 12, SI_GFX, "Touche:Marqueur", L"Marqueur partagé", L"Shared marker", L"Court : poser · tenu : retirer", L"Tap: place · hold: remove" },
    { 12, SI_SEC, NULL, L"Voir", L"See", NULL, NULL },
    { 12, SI_GFX, "Touche:Carte", NULL, NULL, NULL, NULL }, { 12, SI_GFX, "CarteMaintenir", NULL, NULL, NULL, NULL },
    { 12, SI_GFX, "Touche:Vue", L"Vue à la 3e personne", L"Third-person view", L"À pied, en véhicule", L"On foot, in vehicles" },
    { 12, SI_GFX, "Touche:Triches", L"Panneau des triches", L"Cheat panel", L"Si l'hôte les autorise", L"If the host allows them" },
    { 12, SI_X, "f10", L"Menu MWCoop", L"MWCoop menu", L"Joueurs, synchro, options", L"Players, sync, options" },
    { 13, SI_GFX, "Touche:Drone", NULL, NULL, NULL, NULL, 1 },
    { 13, SI_SEC, NULL, L"Déplacer", L"Move", NULL, NULL },
    { 13, SI_GFX, "Touche:DroneAvant", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneArriere", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneGauche", NULL, NULL, NULL, NULL },
    { 13, SI_GFX, "Touche:DroneDroite", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneMonter", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneDescendre", NULL, NULL, NULL, NULL },
    { 13, SI_GFX, "Touche:DroneRapide", L"Plus vite", L"Faster", L"Touche tenue", L"Hold the key" },
    { 13, SI_BREAK, NULL, NULL, NULL, NULL, NULL },
    { 13, SI_SEC, NULL, L"Cadrer", L"Frame", NULL, NULL },
    { 13, SI_GFX, "Touche:DroneZoomPlus", L"Zoomer", L"Zoom in", L"Aussi la molette", L"Also the wheel" }, { 13, SI_GFX, "Touche:DroneZoomMoins", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DronePencherG", NULL, NULL, NULL, NULL },
    { 13, SI_GFX, "Touche:DronePencherD", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneRecentrer", L"Recentrer", L"Reset", L"Penché et zoom", L"Tilt and zoom" }, { 13, SI_GFX, "Touche:DroneGrille", NULL, NULL, NULL, NULL },
    { 13, SI_GFX, "Touche:DroneInterface", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DronePhoto", L"Photo", L"Photo", L"Rangée dans l'album", L"Stored in the album" },
    { 13, SI_X, "footer", L"Les touches valent pour toutes tes parties.", L"The keys apply to all your games.", L"DE", NULL, 2 },
    { 14, SI_X, "themes", L"Thème", L"Theme", NULL, NULL },
    { 15, SI_X, "accent", L"Couleur d'accent", L"Accent colour", L"Boutons, onglet actif, pastilles", L"Buttons, active tab, pills" },
    { 15, SI_X, "aurore", L"Aurore et neige", L"Aurora and snow", L"Fond animé derrière les pages", L"Animated background behind the pages" },
    { 15, SI_X, "verre", L"Verre dépoli", L"Frosted glass", L"Transparence des cartes", L"Card transparency" },
    { 15, SI_BREAK, NULL, NULL, NULL, NULL, NULL },
    { 15, SI_X, "densite", L"Densité", L"Density", L"Espace entre les lignes et les cartes", L"Space between rows and cards" },
    { 15, SI_X, "texte", L"Taille du texte", L"Text size", NULL, NULL },
    { 15, SI_X, "winfollow", L"Suivre Windows", L"Follow Windows", L"Thème clair ou sombre de Windows", L"Windows' light or dark theme" },
    { 16, SI_X, "apercu", NULL, NULL, NULL, NULL },
    { 17, SI_X, "lang", L"Langue", L"Language", NULL, NULL },
    { 18, SI_X, "update", L"Mise à jour", L"Update", NULL, NULL },
    { 18, SI_X, "canal", L"Version jouée", L"Version played", L"Reworked = la version normale ; Legacy = l'ancienne, figée en 0.95.8", L"Reworked = the normal version; Legacy = the old one, frozen at 0.95.8" },
    { 18, SI_X, "majauto", L"Mettre à jour automatiquement", L"Update automatically", L"Au démarrage du lanceur, depuis GitHub", L"At launcher start, from GitHub" },
    { 18, SI_X, "nightly", L"Versions d'essai (nightly)", L"Test builds (nightly)", L"Avant tout le monde, parfois cassées", L"Before everyone, sometimes broken" },
    { 18, SI_X, "notes", L"Notes de version", L"Release notes", L"Ce qui change à chaque version", L"What changes in each version" },
    { 18, SI_OPT, "RevoirGuide", L"Bulles d'aide du lanceur", L"Launcher help bubbles", L"Les petites explications à la première visite", L"The little explanations on the first visit" },
    { 19, SI_X, "folder", L"Dossier du jeu", L"Game folder", NULL, NULL },
    { 19, SI_X, "start", L"Départ du jeu", L"Game start", L"La copie de lancement protège ton dossier ; « dossier du jeu » si MWCoop ne se charge pas depuis la copie", L"The launch copy protects your folder; \"game folder\" if MWCoop doesn't load from the copy" },
    { 19, SI_X, "mscl", L"MSCLoader fourni par MWCoop", L"MSCLoader provided by MWCoop", L"Dans la copie de lancement, jamais dans ton dossier", L"In the launch copy, never in your folder" },
    { 19, SI_X, "about", NULL, NULL, NULL, NULL },
    { 19, SI_X, "buy", L"Acheter le jeu, désinstaller MWCoop", L"Buy the game, uninstall MWCoop", NULL, NULL },
    { 20, SI_X, "logs", L"Journaux", L"Logs", L"Un zip à poster sur le Discord", L"A zip to post on the Discord" },
    { 20, SI_X, "sessions", L"Sessions gardées", L"Sessions kept", L"Journaux des parties précédentes", L"Logs of the previous games" },
    { 20, SI_X, "compat", L"Retirer les modes de compatibilité", L"Remove compatibility modes", L"Windows en ajoute après un plantage : MWCoop ne se charge plus", L"Windows adds some after a crash: MWCoop stops loading" },
    { 20, SI_X, "debug", L"Mode débogage", L"Debug mode", L"Journaux détaillés pour le zip", L"Detailed logs for the zip" },
    { 20, SI_X, "discordask", L"Proposer le Discord", L"Offer the Discord", L"L'invitation après la visite guidée", L"The invitation after the guided tour" },
    { 20, SI_X, "repair", L"Réparer MWCoop", L"Repair MWCoop", L"Réécrit la copie de lancement et le chargeur", L"Rewrites the launch copy and the loader" },
    // (ex-boutons de l'en-tete, maquette du 17/10 : la page API des moddeurs, le depot GitHub)
    { 20, SI_X, "api", L"API pour les moddeurs", L"API for modders", L"MWCoop.CoopApi : la doc, la DLL et un exemple", L"MWCoop.CoopApi: docs, DLL and an example" },
    { 20, SI_X, "github", L"Code source", L"Source code", L"Le dépôt GitHub de MWCoop : code, versions, signalements", L"MWCoop's GitHub repository: code, releases, issues" },
    { 20, SI_X, "footer", L"build", L"build", L"DE", NULL, 2 },
    { 21, SI_X, "hudstyles", L"Style du HUD", L"HUD style", NULL, NULL },
    { 22, SI_GFX, "Taille", NULL, NULL, L"Si le HUD te paraît trop gros ou trop petit", L"If the HUD looks too big or too small" },
    { 22, SI_GFX, "Position", NULL, NULL, L"Auto : la place prévue pour ce style", L"Auto: the usual place for this style" },
    { 22, SI_GFX, "Couleur", NULL, NULL, NULL, NULL },
    { 22, SI_GFX, "Fond", NULL, NULL, NULL, NULL },
    { 22, SI_GFX, "Estomper", NULL, NULL, L"Presque vides, elles s'effacent", L"Nearly empty, they fade out" },
    { 22, SI_GFX, "Valeurs", NULL, NULL, L"Carte : les valeurs · Anneaux, Bandeau : les noms", L"Card: the values · Rings, Strip: the names" },
    { 22, SI_X, "footer", L"Par défaut : tout sauf le style.", L"Default: everything but the style.", L"D", NULL, 2 },
    { 23, SI_X, "hudapercu", NULL, NULL, NULL, NULL },
    { 24, SI_GFX, "JaugeSoif", NULL, NULL, L"« Problème » quand le jeu le dit", L"\"Problem\" when the game says so" }, { 24, SI_GFX, "JaugeFaim", NULL, NULL, NULL, NULL },
    { 24, SI_GFX, "JaugeStress", NULL, NULL, NULL, NULL }, { 24, SI_GFX, "JaugeUrine", NULL, NULL, NULL, NULL }, { 24, SI_GFX, "JaugeFatigue", NULL, NULL, NULL, NULL },
    { 24, SI_BREAK, NULL, NULL, NULL, NULL, NULL },
    { 24, SI_GFX, "JaugeSalete", NULL, NULL, NULL, NULL }, { 24, SI_GFX, "JaugeTemperature", NULL, NULL, NULL, NULL }, { 24, SI_GFX, "JaugeSueur", NULL, NULL, NULL, NULL },
    { 24, SI_GFX, "JaugeArgent", NULL, NULL, NULL, NULL }, { 24, SI_GFX, "JaugeJour", NULL, NULL, NULL, NULL },
    { 25, SI_GFX, "Equipe", NULL, NULL, L"Les jauges des autres joueurs, en petit", L"The other players' gauges, small" },
    { 25, SI_GFX, "Alertes", NULL, NULL, L"Très faim, épuisé, gelé… toi et tes amis", L"Very hungry, exhausted, freezing… you and your friends" },
};
static const int kSetItemN = (int)(sizeof(kSetItems) / sizeof(kSetItems[0]));

// ---- theme : Theme=sombre|clair|aurore|garage (absent : celui de Windows), couleur d'accent et intensite de l'aurore
// ([Lanceur] Accent=RRGGBB, Aurore=0|1|2), verre depoli (Verre=0|1), densite (Densite=0|1|2), taille du texte (Texte=80..130).
// Les tables kDark / kLight / kAurore / kGarage sont modifiees en place (TH(accent) les lit a chaque dessin) ; la scene est
// refaite quand le theme, l'aurore ou le verre changent (SceneKey).
struct SetAccent { const wchar_t *fr, *en; Color dark, darkHi, light, lightHi; };
static const SetAccent kSetAccents[5] = {
    { L"Bleu glace", L"Ice blue", Color(255, 166, 225, 255), Color(255, 200, 236, 255), Color(255, 26, 98, 172), Color(255, 58, 132, 206) },
    { L"Menthe", L"Mint", Color(255, 154, 240, 192), Color(255, 196, 248, 220), Color(255, 16, 128, 88), Color(255, 40, 160, 120) },
    { L"Ambre", L"Amber", Color(255, 255, 210, 143), Color(255, 255, 228, 180), Color(255, 176, 104, 10), Color(255, 208, 136, 40) },
    { L"Rose", L"Pink", Color(255, 240, 166, 255), Color(255, 248, 200, 255), Color(255, 150, 40, 170), Color(255, 180, 80, 200) },
    { L"Corail", L"Coral", Color(255, 255, 143, 168), Color(255, 255, 186, 200), Color(255, 200, 40, 80), Color(255, 224, 80, 120) },
};
static const Color kAuroreAccent(255, 124, 232, 198), kAuroreAccent2(255, 170, 246, 220), kGarageAccent(255, 255, 180, 84), kGarageAccent2(255, 255, 210, 140);   // (accents propres des themes)
static int g_accentSel;   // (-1 : couleur ecrite a la main dans l'ini)
// Accent sur les quatre tables : sombre / clair selon la pastille ; Aurore verte et Garage gardent leur accent propre tant que
// la pastille est celle d'origine (menthe, ambre), sinon la variante sombre de la couleur choisie.
static void ThemeApplyAccent(Color dark, Color darkHi, Color light, Color lightHi, bool custom)
{
    kDark.accent = dark; kDark.accent2 = darkHi; kLight.accent = light; kLight.accent2 = lightHi;
    kAurore.accent = custom ? dark : kAuroreAccent; kAurore.accent2 = custom ? darkHi : kAuroreAccent2;
    kGarage.accent = custom ? dark : kGarageAccent; kGarage.accent2 = custom ? darkHi : kGarageAccent2;
}
static int ThemeFromName(const wchar_t *s)
{
    if (!_wcsicmp(s, L"sombre") || !_wcsicmp(s, L"dark")) return THEME_SOMBRE;
    if (!_wcsicmp(s, L"clair") || !_wcsicmp(s, L"light")) return THEME_CLAIR;
    if (!_wcsicmp(s, L"aurore")) return THEME_AURORE;
    if (!_wcsicmp(s, L"garage")) return THEME_GARAGE;
    return -1;
}
static const wchar_t *ThemeName(int t) { static const wchar_t *n[THEME_N] = { L"sombre", L"clair", L"aurore", L"garage" }; return n[t >= 0 && t < THEME_N ? t : 0]; }
static int ThemeWindows()   // (theme des applications de Windows : sombre ou clair)
{
    DWORD v = 1, sz = sizeof(v);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"AppsUseLightTheme", RRF_RT_REG_DWORD, NULL, &v, &sz) == ERROR_SUCCESS) return v == 0 ? THEME_SOMBRE : THEME_CLAIR;
    return THEME_SOMBRE;
}
static void ThemeSelect(int t) { g_theme = t >= 0 && t < THEME_N ? t : THEME_SOMBRE; g_dark = g_theme != THEME_CLAIR; }
static void ThemeSet(int t) { WritePrivateProfileStringW(L"Lanceur", L"Theme", ThemeName(t), g_iniLauncher.c_str()); ThemeSelect(t); }
static void ThemeLoad()
{
    wchar_t th[16] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Theme", L"", th, 16, g_iniLauncher.c_str());
    int t = ThemeFromName(th);
    ThemeSelect(t >= 0 ? t : ThemeWindows());
    g_aurore = max(0, min(2, (int)GetPrivateProfileIntW(L"Lanceur", L"Aurore", 2, g_iniLauncher.c_str())));
    g_glass = GetPrivateProfileIntW(L"Lanceur", L"Verre", 1, g_iniLauncher.c_str()) != 0;
    g_densite = max(0, min(2, (int)GetPrivateProfileIntW(L"Lanceur", L"Densite", 1, g_iniLauncher.c_str())));
    g_texte = max(80, min(110, (int)GetPrivateProfileIntW(L"Lanceur", L"Texte", 100, g_iniLauncher.c_str())));   // (au-dela de 110 % le salon et la barre coupaient des textes)
    g_texte = (g_texte / 5) * 5;
    wchar_t b[16] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Accent", L"", b, 16, g_iniLauncher.c_str());
    g_accentSel = 0;
    if (wcslen(b) == 6) {
        unsigned v = (unsigned)wcstoul(b, NULL, 16);
        Color c(255, (BYTE)(v >> 16), (BYTE)(v >> 8), (BYTE)v);
        g_accentSel = -1;
        for (int i = 0; i < 5; i++) if (kSetAccents[i].dark.GetValue() == c.GetValue()) g_accentSel = i;
        if (g_accentSel < 0) {   // couleur libre : claire sur les themes sombres, assombrie sur le clair (texte blanc dessus)
            Color navy(255, 16, 30, 48);
            ThemeApplyAccent(c, Mix(c, Color(255, 255, 255, 255), 0.3f), Mix(c, navy, 0.55f), Mix(c, navy, 0.4f), true);
            return;
        }
    }
    const SetAccent &a = kSetAccents[g_accentSel];
    ThemeApplyAccent(a.dark, a.darkHi, a.light, a.lightHi, g_accentSel != 0);
}
static void ThemeSetAccent(int i)
{
    wchar_t hex[8];
    swprintf_s(hex, L"%02X%02X%02X", kSetAccents[i].dark.GetR(), kSetAccents[i].dark.GetG(), kSetAccents[i].dark.GetB());
    WritePrivateProfileStringW(L"Lanceur", L"Accent", i == 0 ? NULL : hex, g_iniLauncher.c_str());
    ThemeLoad();
}
static void ThemeSetAurore(int v) { WritePrivateProfileStringW(L"Lanceur", L"Aurore", v == 2 ? NULL : std::to_wstring(v).c_str(), g_iniLauncher.c_str()); g_aurore = v; }
static void ThemeSetGlass(bool on) { WritePrivateProfileStringW(L"Lanceur", L"Verre", on ? NULL : L"0", g_iniLauncher.c_str()); g_glass = on; }
static void ThemeSetDensite(int d) { WritePrivateProfileStringW(L"Lanceur", L"Densite", d == 1 ? NULL : std::to_wstring(d).c_str(), g_iniLauncher.c_str()); g_densite = d; }
static void ThemeSetTexte(int p) { p = max(80, min(110, (p / 5) * 5)); WritePrivateProfileStringW(L"Lanceur", L"Texte", p == 100 ? NULL : std::to_wstring(p).c_str(), g_iniLauncher.c_str()); g_texte = p; }
// Theme absent de l'ini : celui des applications de Windows (comme au premier lancement).
static bool ThemeFollowsWindows() { wchar_t th[16] = L""; GetPrivateProfileStringW(L"Lanceur", L"Theme", L"", th, 16, g_iniLauncher.c_str()); return !th[0]; }
static void ThemeFollowWindows(bool on)
{
    if (!on) { WritePrivateProfileStringW(L"Lanceur", L"Theme", ThemeName(g_theme), g_iniLauncher.c_str()); return; }
    WritePrivateProfileStringW(L"Lanceur", L"Theme", NULL, g_iniLauncher.c_str());
    ThemeSelect(ThemeWindows());
}
static bool ThemeIsDefault() { return g_accentSel == 0 && g_aurore == 2 && g_glass && g_densite == 1 && g_texte == 100 && ThemeFollowsWindows(); }

// ---- disposition des touches du drone (ZQSD / WASD / fleches) : les 4 touches de deplacement d'un coup
static int DroneLayoutGet()
{
    std::string f = GameKeyGet(GameKeyIndex("DroneAvant")), l = GameKeyGet(GameKeyIndex("DroneGauche"));
    if (f == "Z" && l == "Q") return 0;
    if (f == "W" && l == "A") return 1;
    if (f == "UpArrow" && l == "LeftArrow") return 2;
    return -1;
}
static void DroneLayoutSet(int k)
{
    static const char *keys[] = { "DroneAvant", "DroneArriere", "DroneGauche", "DroneDroite" };
    static const char *v[3][4] = { { "Z", "S", "Q", "D" }, { "W", "S", "A", "D" }, { "UpArrow", "DownArrow", "LeftArrow", "RightArrow" } };
    for (int i = 0; i < 4; i++) WritePrivateProfileStringA("Touches", keys[i], v[k][i], ModIniA().c_str());
    LaunchLog("touches du drone : disposition %s", k == 0 ? "ZQSD" : k == 1 ? "WASD" : "fleches");
}

// ---- petits elements de la page
static int SetGfxIdx(const char *key) { const auto &rows = GfxRows(); for (int i = 0; i < (int)rows.size(); i++) if (!strcmp(rows[i].key, key)) return i; return -1; }
static bool SetDrawn(RectF r) { return r.Y + r.Height > g_setTop && r.Y < g_setBottom; }
static bool SetVisible(RectF r) { return r.Y >= g_setTop - 1 && r.Y + r.Height <= g_setBottom + 1; }
// Minuscules sans accents (recherche).
static std::wstring SetFold(const std::wstring &s)
{
    static const wchar_t *from = L"àâäáãåéèêëíìîïóòôöõúùûüçñýÿœæ", *to = L"aaaaaaeeeeiiiiooooouuuucnyyoa";
    std::wstring o;
    o.reserve(s.size());
    for (wchar_t c : s) { c = towlower(c); const wchar_t *p = c ? wcschr(from, c) : NULL; if (p) c = to[p - from]; o += c; }
    return o;
}
// Texte gras sur deux lignes au plus (nom d'une ligne trop long pour sa sous-colonne, comme la maquette).
static void SetWrapText(Graphics &g, const std::wstring &s, RectF r, float px, int style, Color c)
{
    Font &font = *FontOf(L"Segoe UI", px, style);
    StringFormat sf;
    sf.SetLineAlignment(StringAlignmentNear);
    sf.SetTrimming(StringTrimmingEllipsisWord);
    SolidBrush b(c);
    DrawStr(g, s.c_str(), -1, &font, r, &sf, &b);
}
// Commande trop large pour tenir a cote du nom : dans une sous-colonne etroite elle passe SOUS le nom (a droite) ; dans une
// colonne entiere le nom passe sur deux lignes (maquette). 'wid' : largeur de la commande.
static bool SetLabelWraps(const std::wstring &t, float maxW);
static bool SetStacked(const std::wstring &lab, float w, float wid) { return w < 340 && w - wid - 12 < 110 && SetLabelWraps(lab, w - wid - 12); }   // (place pour un nom sur deux lignes : il y passe)
static float SetLabelH(const std::wstring &t, const std::wstring &d, float maxW);
static float SetCtlRowH(const std::wstring &lab, const std::wstring &desc, float w, float wid) { return SetStacked(lab, w, wid) ? (desc.empty() ? SetRowH() : SetRowDH()) + 32 : SetLabelH(lab, desc, w - wid - 12); }
static float SetLabelMaxW(const std::wstring &lab, float w, float wid) { return SetStacked(lab, w, wid) ? w : w - wid - 12; }
// Rectangle de la commande (hauteur ch) : a droite, centree, ou en bas a droite si elle est sous le nom.
static RectF SetCtlRect(RectF row, const std::wstring &lab, float wid, float ch)
{
    bool st = SetStacked(lab, row.Width, wid);
    return RectF(row.X + row.Width - wid, st ? row.Y + row.Height - ch - 5 : row.Y + (row.Height - ch) / 2, wid, ch);
}
// Nom suffixe " (beta)" : le mot va dans un jeton rose a part (maquette Co-op : Jokkis).
static bool SetBetaStrip(std::wstring &t)
{
    for (const wchar_t *suf : { L" (bêta)", L" (beta)" }) {
        size_t n = wcslen(suf);
        if (t.size() > n && !t.compare(t.size() - n, n, suf)) { t.erase(t.size() - n); return true; }
    }
    return false;
}
// Hauteur d'une ligne : nom, explication, et nom sur deux lignes s'il ne tient pas dans maxW.
static bool SetLabelWraps(const std::wstring &t, float maxW) { std::wstring s = t; SetBetaStrip(s); return MeasureW(MeasureG(), s, 13.5f, FontStyleBold) > maxW; }
static float SetLabelH(const std::wstring &t, const std::wstring &d, float maxW) { return (d.empty() ? SetRowH() : SetRowDH()) + (SetLabelWraps(t, maxW) ? 17 * UiTextScale() : 0); }
// Nom (gras) et explication (grise) d'une ligne, filet dessous ; renvoie la largeur du nom (avec son jeton beta).
static float SetRowLabel(Graphics &g, RectF row, const std::wstring &t0, const std::wstring &d, float maxW, float alpha = 1.0f)
{
    std::wstring t = t0;
    bool beta = SetBetaStrip(t), wrap = SetLabelWraps(t, maxW), top = maxW >= row.Width - 1;   // (commande sous le nom : nom en haut)
    float tw = min(maxW, MeasureW(MeasureG(), t, 13.5f, FontStyleBold)), ty = row.Y + (d.empty() && !wrap && !top ? (row.Height - 20) / 2 : 4);
    if (wrap) { float lh = 18.5f * UiTextScale(); SetWrapText(g, t, RectF(row.X, row.Y + 3, maxW, 2 * lh + 3), 13.5f, FontStyleBold, WithA(kInk, alpha)); ty = row.Y + 3 + lh; tw = maxW; }
    else Text(g, t, RectF(row.X, ty, maxW, 20), 13.5f, FontStyleBold, WithA(kInk, alpha), StringAlignmentNear);
    if (!d.empty()) Text(g, d, RectF(row.X, ty + 19 * UiTextScale(), maxW, 18 * UiTextScale()), 11.5f, FontStyleRegular, WithA(kGrey, alpha), StringAlignmentNear);
    if (beta && !wrap) {   // jeton Beta apres le nom
        std::wstring s = T(L"Bêta", L"Beta");
        float pw = PillW(s, 9.5f) - 2;
        Color pink(255, 255, 107, 139);
        Pill(g, RectF(row.X + tw + 8, ty + 1, pw, 18), WithA(pink, alpha), s, Color(255, 255, 255, 255), 9.5f);
        tw += pw + 8;
    }
    Pen ln(TH(sep), 1.0f); g.DrawLine(&ln, row.X, row.Y + row.Height - 0.5f, row.X + row.Width, row.Y + row.Height - 0.5f);
    return tw;
}
static void SetRowHover(Graphics &g, RectF row) { GraphicsPath p; RoundRect(p, RectF(row.X - 8, row.Y + 1, row.Width + 16, row.Height - 3), 10); SolidBrush hb(TH(card)); g.FillPath(&hb, &p); }
// Choix a segments de largeur selon le texte (UiSeg : largeurs egales) ; zones id0 + i.
static float SetSegW(const std::vector<std::wstring> &labels, float px) { float w = 6; for (auto &l : labels) w += MeasureW(MeasureG(), l, px, FontStyleBold) + 18; return w; }
static void SetSeg(Graphics &g, RectF r, const std::vector<std::wstring> &labels, int sel, int id0, float px = 12, bool enabled = true)
{
    GraphicsPath tp; RoundRect(tp, r, min(12.0f, r.Height / 2));
    SolidBrush tb(TH(field)); g.FillPath(&tb, &tp);
    Pen bp(TH(fieldBorder), 1.0f); g.DrawPath(&bp, &tp);
    float x = r.X + 3;
    for (int i = 0; i < (int)labels.size(); i++) {
        float w = MeasureW(MeasureG(), labels[i], px, FontStyleBold) + 18;
        RectF s(x, r.Y + 3, w, r.Height - 6);
        bool on = i == sel, hot = enabled && g_uiHot == id0 + i;
        GraphicsPath p; RoundRect(p, s, min(9.0f, s.Height / 2));
        if (on) { SolidBrush b(WithA(kAcc, enabled ? 1.0f : 0.5f)); g.FillPath(&b, &p); }
        else if (hot) { SolidBrush b(TH(cardSel)); g.FillPath(&b, &p); }
        Text(g, labels[i], s, px, FontStyleBold, WithA(on ? kOnAcc : kInk, enabled ? 1.0f : 0.5f));
        if (enabled) HotZone(s, id0 + i);
        x += w;
    }
}
// Curseur (maquette : champ de vision, taille du texte) : piste, partie prise en accent, bouton rond, valeur a droite ;
// une zone cliquable par cran (id0 + i).
static const float kSetSliderW = 150;
static void SetSlider(Graphics &g, RectF r, int n, int k, int id0, const std::wstring &val)
{
    float vw = 50, x0 = r.X + 9, x1 = r.X + r.Width - vw - 9, cy = r.Y + r.Height / 2;
    Pen track(TH(toggleOff), 4.0f); track.SetStartCap(LineCapRound); track.SetEndCap(LineCapRound);
    g.DrawLine(&track, x0, cy, x1, cy);
    float kx = n > 1 ? x0 + (x1 - x0) * k / (n - 1) : x0;
    Pen fill(kAcc, 4.0f); fill.SetStartCap(LineCapRound); fill.SetEndCap(LineCapRound);
    if (kx > x0 + 1) g.DrawLine(&fill, x0, cy, kx, cy);
    bool hot = g_uiHot >= id0 && g_uiHot < id0 + n;
    SolidBrush sh(Color(70, 0, 0, 0)); g.FillEllipse(&sh, kx - 8, cy - 7, 16.0f, 16.0f);
    SolidBrush kb(Color(255, 255, 255, 255)); g.FillEllipse(&kb, kx - 8, cy - 8, 16.0f, 16.0f);
    if (hot) { Pen hp(kAcc, 1.6f); g.DrawEllipse(&hp, kx - 8, cy - 8, 16.0f, 16.0f); }
    Text(g, val, RectF(x1 + 6, r.Y, vw + 3, r.Height), 12.5f, FontStyleBold, kInk, StringAlignmentFar);
    float step = n > 1 ? (x1 - x0) / (n - 1) : r.Width;
    for (int i = 0; i < n; i++) HotZone(RectF(x0 + step * (i - 0.5f), r.Y, step, r.Height), id0 + i);
}
// Touche en jeu (capuchon) : en attente = contour accent ; 'fixed' : touche du jeu lui-meme, pas modifiable ici.
static void SetKeyCap(Graphics &g, RectF r, const std::wstring &s, bool cap, bool hot, bool fixed = false)
{
    GraphicsPath p; RoundRect(p, r, 9);
    SolidBrush b(cap ? WithA(kAcc, 0.22f) : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
    Pen pen(cap ? kAcc : TH(fieldBorder), cap ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
    Pen under(WithA(kInk, 0.12f), 2.0f); g.DrawLine(&under, r.X + 8, r.Y + r.Height - 3, r.X + r.Width - 8, r.Y + r.Height - 3);
    Text(g, s, r, 12.5f, FontStyleBold, cap ? kAcc : WithA(kInk, fixed ? 0.6f : 1.0f));
}
static void SetChip(Graphics &g, float rightX, float cy, const std::wstring &s, Color c, bool strong = false)
{
    float w = PillW(s, 10.5f) + 4;
    Pill(g, RectF(rightX - w, cy - 11, w, 22), WithA(c, strong ? 1.0f : 0.16f), s, strong ? kOnAcc : c, 10.5f);
}
// Bouton plat de ligne (hauteur 32) ; renvoie sa largeur.
static float SetButtonW(const std::wstring &s) { return MeasureW(MeasureG(), s, 12.5f, FontStyleBold) + 32; }

// Valeurs montrees en pastilles pour un reglage (pv : les courtes, plus la valeur courante si elle n'y est pas, a sa place).
static void SetGfxVals(const GfxRow &r, int v, std::vector<int> &vals, std::vector<std::wstring> &labs)
{
    vals.clear(); labs.clear();
    for (int i = 0; i < (int)r.vals.size(); i++) {
        int pi = -1;
        for (int j = 0; j < (int)r.pv.size(); j++) if (r.pv[j] == r.vals[i]) pi = j;
        if (!r.pv.empty() && pi < 0 && r.vals[i] != v) continue;
        vals.push_back(r.vals[i]);
        labs.push_back(pi >= 0 ? T(r.pf[pi], r.pe[pi]) : T(r.lf[i], r.le[i]));
    }
}
// Mise en page d'une ligne de reglage du jeu pour une largeur : commande (touche, raison, interrupteur, curseur, pastilles,
// ◀ valeur ▶), sa largeur, nom sur deux lignes ou non. Partagee par la hauteur et le dessin.
struct GfxPlan { bool keyRow, off, toggle, slider, seg, cap; float wid; int k; std::vector<int> vals; std::vector<std::wstring> labs; std::wstring lab, desc, keyText; const wchar_t *why; float h; };
static GfxPlan SetGfxPlan(const SetItem &it, int idx, float w)
{
    const GfxRow &r = GfxRows()[idx];
    GfxPlan p = {};
    p.lab = it.fr ? T(it.fr, it.en) : T(r.fr, r.en); p.desc = it.dFr ? T(it.dFr, it.dEn) : L"";
    p.keyRow = r.sec && !strcmp(r.sec, "Touches");
    p.off = !p.keyRow && GfxRowOff(r, &p.why);
    int kk = p.keyRow ? GameKeyIndex(r.key) : -1, v = p.keyRow ? 0 : GfxGet(r.key);
    p.cap = kk >= 0 && g_keyCapture == kk;
    p.keyText = p.cap ? std::wstring(T(L"… appuie", L"… press")) : kk >= 0 ? GameKeyLabel(GameKeyGet(kk)) : std::wstring(L"?");
    p.slider = !p.off && !p.keyRow && !strcmp(r.key, "ChampVision");
    p.toggle = !p.off && !p.keyRow && r.vals.empty();
    p.wid = 44;
    if (p.keyRow) p.wid = max(44.0f, MeasureW(MeasureG(), p.keyText, 12.5f, FontStyleBold) + 24);
    else if (p.off) p.wid = r.vals.empty() && w < 340 ? 44 : 150;   // (colonne etroite : interrupteur grise, pas de texte qui deborde)
    else if (p.slider) { p.wid = kSetSliderW; for (int i = 0; i < (int)r.vals.size(); i++) if (r.vals[i] == v) p.k = i; }
    else if (!r.vals.empty()) {
        SetGfxVals(r, v, p.vals, p.labs);
        for (int i = 0; i < (int)p.vals.size(); i++) if (p.vals[i] == v) p.k = i;
        float sw = SetSegW(p.labs, 12), lw = MeasureW(MeasureG(), p.lab, 13.5f, FontStyleBold);
        // pastilles si elles tiennent a cote du nom, sous le nom (sous-colonne etroite) ou a cote d'un nom sur deux lignes
        // (au moins 110 px) ; sinon ◀ valeur ▶
        if (p.labs.size() <= 6 && (sw <= w - lw - 16 || (w < 340 && sw <= w) || sw <= w - 110 - 16)) { p.seg = true; p.wid = sw; }
        else { p.wid = 172; for (int i = 0; i < (int)r.vals.size(); i++) if (r.vals[i] == v) p.k = i; }
    }
    p.h = SetCtlRowH(p.lab, p.desc, w, p.wid);
    return p;
}
// Un reglage du jeu (ligne de GfxRows) : nom, pastille d'impact, puis sa commande a droite.
static void SetGfxRow(Graphics &g, const SetItem &it, int idx, RectF row, const GfxPlan &p)
{
    const GfxRow &r = GfxRows()[idx];
    int id = UI_GFX + idx * 3;
    bool hot = g_uiHot == id || g_uiHot == id + 1 || g_uiHot == id + 2 || (g_uiHot >= UI_SETV + idx * 10 && g_uiHot < UI_SETV + idx * 10 + 10);
    if (hot && !p.slider) SetRowHover(g, row);
    const wchar_t *il = r.impact >= 3 ? T(L"lourd", L"heavy") : T(L"moyen", L"medium");
    float pw = r.impact >= 2 && !p.keyRow ? PillW(il, 9.5f) + 10 : 0;
    float maxW = SetLabelMaxW(p.lab, row.Width, p.wid), labW = SetRowLabel(g, row, p.lab, p.desc, maxW);
    bool stacked = SetStacked(p.lab, row.Width, p.wid);
    if (pw > 0 && labW + pw < maxW && !SetLabelWraps(p.lab, maxW)) {   // impact sur les images : pastille apres le nom
        Color ic = r.impact >= 3 ? kRed : kWarn;
        Pill(g, RectF(row.X + labW + 10, row.Y + (p.desc.empty() && !stacked ? (row.Height - 18) / 2 : 5), pw - 10, 18), WithA(ic, 0.16f), il, ic, 9.5f);
    }
    bool offTog = p.off && r.vals.empty() && p.wid < 60;
    RectF ctl = SetCtlRect(row, p.lab, p.wid, p.toggle || offTog ? 24 : p.off ? 26 : 30);
    float cx = ctl.X, cy = ctl.Y + ctl.Height / 2;
    if (p.keyRow) { SetKeyCap(g, ctl, p.keyText, p.cap, hot); HotZone(row, id); return; }
    if (offTog) {   // interrupteur grise (style Vanilla...) : son etat reste visible, sans effet
        GraphicsPath tp; RoundRect(tp, ctl, ctl.Height / 2);
        SolidBrush tb(WithA(TH(toggleOff), 0.45f)); g.FillPath(&tb, &tp);
        float d = ctl.Height - 6, kx = GfxGet(r.key) == 1 ? ctl.X + ctl.Width - d - 3 : ctl.X + 3;
        SolidBrush kb(WithA(kGrey, 0.7f)); g.FillEllipse(&kb, kx, ctl.Y + 3, d, d);
        return;
    }
    if (p.off) {
        GraphicsPath vp; RoundRect(vp, ctl, 9);
        SolidBrush vb(WithA(TH(btn2), 0.5f)); g.FillPath(&vb, &vp);
        Text(g, p.why, ctl, 11.5f, FontStyleItalic, kGrey);
        return;
    }
    if (p.toggle) { UiToggle(g, ctl, GfxGet(r.key) == 1, hot); HotZone(row, id); return; }
    if (p.slider) { SetSlider(g, ctl, (int)r.vals.size(), p.k, UI_SET_FOV, T(r.lf[p.k], r.le[p.k])); return; }
    if (p.seg) { SetSeg(g, ctl, p.labs, p.k, UI_SETV + idx * 10, 12); return; }
    RectF val(cx, cy - 15, p.wid, 30), lft(val.X, val.Y, 30, val.Height), rgt(val.X + val.Width - 30, val.Y, 30, val.Height);
    GraphicsPath vp; RoundRect(vp, val, 10);
    SolidBrush vb(TH(btn2)); g.FillPath(&vb, &vp);
    Pen vpen(TH(fieldBorder), 1.0f); g.DrawPath(&vpen, &vp);
    bool canL = p.k > 0, canR = p.k + 1 < (int)r.vals.size();
    Icon(g, IC_CHEVL, lft.X + 15, lft.Y + lft.Height / 2, 14, WithA(g_uiHot == id + 1 ? kAcc : kInk, canL ? 1.0f : 0.25f), 2.2f);
    Icon(g, IC_CHEVR, rgt.X + 15, rgt.Y + rgt.Height / 2, 14, WithA(g_uiHot == id + 2 ? kAcc : kInk, canR ? 1.0f : 0.25f), 2.2f);
    Text(g, T(r.lf[p.k], r.le[p.k]), RectF(lft.X + lft.Width, val.Y, val.Width - 60, val.Height), 12.5f, FontStyleBold, r.vals[p.k] == -1 ? kGrey : kInk);
    HotZone(row, UI_NONE);
    if (canL) HotZone(lft, id + 1);
    if (canR) HotZone(rgt, id + 2);
}
// Une option du lanceur (g_opts) : port (pastilles 7870..7875), interrupteur, ou bouton vers la page (contenu, guide).
static float SetOptW(const SetItem &it, std::wstring &bt, int &id, std::wstring &desc)
{
    const Opt *o = OptByKey(it.key);
    if (!o) return 44;
    bool button = o->kind == O_ACTION;
    id = -1;
    if (!strcmp(it.key, "Port")) { std::vector<std::wstring> l; for (int v : o->vals) l.push_back(std::to_wstring(v)); return SetSegW(l, 12); }
    if (button) {
        id = !strcmp(it.key, "ContenuEnvoye") ? UI_SET_CONTENT : !strcmp(it.key, "Apparence") ? UI_SET_SKIN : !strcmp(it.key, "CouleurVoiture") ? UI_SET_CAR : UI_SET_GUIDE;
        bt = id == UI_SET_GUIDE ? T(L"Revoir", L"See again") : T(L"Choisir", L"Choose");
        if (id == UI_SET_CONTENT) desc = ContentSummary();
        else if (id == UI_SET_SKIN) desc = TLab(o->labFr, o->labEn, (size_t)OptGet(*o));
        else if (id == UI_SET_CAR) desc = CarColorName();
        return SetButtonW(bt);
    }
    id = !strcmp(it.key, "FermerLanceur") ? UI_SET_CLOSE : !strcmp(it.key, "LancerSteam") ? UI_SET_STEAMRUN : UI_SET_MODSAUTO;
    return 44;
}
static float SetOptH(const SetItem &it, float w)
{
    const Opt *o = OptByKey(it.key);
    if (!o) return SetRowDH();
    std::wstring bt, desc = it.dFr ? T(it.dFr, it.dEn) : L""; int id;
    float wid = SetOptW(it, bt, id, desc);
    return SetCtlRowH(it.fr ? T(it.fr, it.en) : T(o->fr, o->en), desc.empty() ? std::wstring(L" ") : desc, w, wid);   // (toujours la hauteur avec explication : l'ancienne page)
}
static void SetOptRow(Graphics &g, const SetItem &it, RectF row)
{
    const Opt *o = OptByKey(it.key);
    if (!o) return;
    std::wstring lab = it.fr ? T(it.fr, it.en) : T(o->fr, o->en), desc = it.dFr ? T(it.dFr, it.dEn) : L"", bt;
    int id = -1;
    float wid = SetOptW(it, bt, id, desc);
    bool port = !strcmp(it.key, "Port"), button = o->kind == O_ACTION;
    bool hot = !port && !button && g_uiHot == id;
    if (hot) SetRowHover(g, row);
    SetRowLabel(g, row, lab, desc, SetLabelMaxW(lab, row.Width, wid));
    if (port) {
        std::vector<std::wstring> l; int sel = 0, cur = OptGet(*o);
        for (int i = 0; i < (int)o->vals.size(); i++) { l.push_back(std::to_wstring(o->vals[i])); if (o->vals[i] == cur) sel = i; }
        SetSeg(g, SetCtlRect(row, lab, wid, 30), l, sel, UI_SET_PORTSEG, 12);
        return;
    }
    if (button) { UiButton(g, SetCtlRect(row, lab, wid, 32), bt, id, false); return; }
    UiToggle(g, SetCtlRect(row, lab, 44, 24), OptGet(*o) != 0, hot);
    HotZone(row, id);
}
// Interrupteur de ligne a part (cle du lanceur).
static void SetToggleRow(Graphics &g, const SetItem &it, RectF row, bool on, int id)
{
    bool hot = g_uiHot == id;
    if (hot) SetRowHover(g, row);
    std::wstring lab = T(it.fr, it.en);
    SetRowLabel(g, row, lab, it.dFr ? T(it.dFr, it.dEn) : L"", SetLabelMaxW(lab, row.Width, 44));
    UiToggle(g, SetCtlRect(row, lab, 44, 24), on, hot);
    HotZone(row, id);
}
static void SetSegRow(Graphics &g, const SetItem &it, RectF row, const std::vector<std::wstring> &labs, int sel, int id0, bool enabled = true)
{
    float sw = SetSegW(labs, 12);
    std::wstring lab = T(it.fr, it.en);
    SetRowLabel(g, row, lab, it.dFr ? T(it.dFr, it.dEn) : L"", SetLabelMaxW(lab, row.Width, sw), enabled ? 1.0f : 0.55f);
    SetSeg(g, SetCtlRect(row, lab, sw, 30), labs, sel, id0, 12, enabled);
}
static void SetButtonRow(Graphics &g, const SetItem &it, RectF row, const std::wstring &bt, int id, bool primary = false, bool enabled = true)
{
    float bw = SetButtonW(bt);
    std::wstring lab = T(it.fr, it.en);
    SetRowLabel(g, row, lab, it.dFr ? T(it.dFr, it.dEn) : L"", SetLabelMaxW(lab, row.Width, bw));
    UiButton(g, SetCtlRect(row, lab, bw, 32), bt, id, primary, -1, enabled);
}
// Ciel d'un apercu de theme : degrade, deux voiles d'aurore (degrades radiaux), une petite carte et une barre d'accent.
static void SetThemePreview(Graphics &g, RectF r, Color sky0, Color sky1, Color aur0, Color aur1, Color card, Color cardBorder, Color accent)
{
    GraphicsPath p; RoundRect(p, r, 10);
    GraphicsState st = g.Save();
    g.SetClip(&p, CombineModeIntersect);
    LinearGradientBrush sky(RectF(r.X, r.Y - 1, r.Width, r.Height + 2), sky0, sky1, LinearGradientModeVertical);
    g.FillPath(&sky, &p);
    Color aurs[2] = { aur0, aur1 };
    for (int i = 0; i < 2; i++) {
        if (!aurs[i].GetA()) continue;
        GraphicsPath e; e.AddEllipse(r.X + (i ? r.Width * 0.45f : -r.Width * 0.3f), r.Y - r.Height * 0.55f, r.Width * 0.95f, r.Height * 1.1f);
        PathGradientBrush pg(&e);
        pg.SetCenterColor(aurs[i]);
        Color edge(0, aurs[i].GetR(), aurs[i].GetG(), aurs[i].GetB()); int one = 1;
        pg.SetSurroundColors(&edge, &one);
        g.FillPath(&pg, &e);
    }
    float u = r.Height / 100;
    RectF c1(r.X + 10 * u, r.Y + 36 * u, 42 * u, 50 * u), c2(r.X + 60 * u, r.Y + 36 * u, r.Width - 70 * u, 22 * u), bar(r.X + 60 * u, r.Y + 66 * u, 50 * u, 20 * u);
    for (RectF c : { c1, c2 }) { GraphicsPath cp; RoundRect(cp, c, 6 * u); SolidBrush cb(card); g.FillPath(&cb, &cp); Pen pen(cardBorder, 1.0f); g.DrawPath(&pen, &cp); }
    GraphicsPath bp; RoundRect(bp, bar, 5 * u); SolidBrush ab(accent); g.FillPath(&ab, &bp);
    g.Restore(st);
}

// Jetons des ajouts : noms, et nombre de lignes pour une largeur (w <= 0 : la derniere largeur mesuree).
static const char *kAjoutKeys[] = { "AjoutDrone", "AjoutSacs", "AjoutBoutique", "AjoutMusee", "AjoutVaz" };
static const int kAjoutN = (int)(sizeof(kAjoutKeys) / sizeof(kAjoutKeys[0]));   // (aussi le salon, launcher.cpp)
static const wchar_t *kAjoutFr[] = { L"Caméra drone (F8)", L"Sacs de couchage", L"Boutique s21", L"Musée de la Corse", L"VAZ-2108 (F10)" }, *kAjoutEn[] = { L"Drone camera (F8)", L"Sleeping bags", L"s21 shop", L"Museum of Corsica", L"VAZ-2108 (F10)" };
static float g_chipW = 458;
static int SetChipLines(float w)
{
    if (w > 0) g_chipW = w;
    float cx = 0; int lines = 1;
    for (int i = 0; i < kAjoutN; i++) {
        float cw = PillW(T(kAjoutFr[i], kAjoutEn[i]), 11) + 22;
        if (cx > 0 && cx + cw > g_chipW) { lines++; cx = 0; }
        cx += cw + 6;
    }
    return lines;
}
// Pied de carte : legende grise a gauche, boutons a droite (dFr : D Par defaut, E Enregistrer, G guide de l'hote, T test).
static std::wstring SetFooterCaption(const SetItem &it)
{
    if (!it.fr) return L"";
    if (!wcscmp(it.fr, L"build")) { wchar_t b[96]; swprintf_s(b, T(L"MWCoop.exe · build du %hs", L"MWCoop.exe · build of %hs"), __DATE__); return b; }
    if (it.dFr && wcschr(it.dFr, L'T') && !g_netTest.empty()) return g_netTest;   // (resultat du test de connexion)
    return T(it.fr, it.en);
}
static void SetFooter(Graphics &g, const SetItem &it, RectF row)
{
    const wchar_t *codes = it.dFr ? it.dFr : L"";
    float x = row.X, right = row.X + row.Width, cy = row.Y + row.Height / 2;
    std::wstring cap = SetFooterCaption(it);
    if (wcschr(codes, L'G')) { std::wstring s = T(L"Revoir le guide de l'hôte", L"See the host guide again"); float w = SetButtonW(s); UiButton(g, RectF(x, cy - 16, w, 32), s, UI_SET_HOSTGUIDE, false, IC_BOOK); x += w + 10; }
    if (wcschr(codes, L'T')) { std::wstring s = T(L"Tester ma connexion", L"Test my connection"); float w = SetButtonW(s) + 24; UiButton(g, RectF(x, cy - 16, w, 32), s, UI_SET_NETTEST, false, IC_WIFI); x += w + 10; }
    if (wcschr(codes, L'E')) { std::wstring s = T(L"Enregistrer", L"Save"); float w = SetButtonW(s); UiButton(g, RectF(right - w, cy - 16, w, 32), s, UI_SET_SAVE, true); right -= w + 8; }
    if (wcschr(codes, L'D')) { std::wstring s = T(L"Par défaut", L"Default"); float w = SetButtonW(s); UiButton(g, RectF(right - w, cy - 16, w, 32), s, UI_SET_DEFAULT, false); right -= w + 8; }
    if (!cap.empty() && right - x > 40) {
        Color cc = (it.dFr && wcschr(it.dFr, L'T') && !g_netTest.empty()) ? (g_netTestKind == K_ERR ? kRed : g_netTestKind == K_WARN ? kWarn : kOk) : kGrey;
        Text(g, cap, RectF(x, row.Y, right - x - 6, row.Height), 11.5f, FontStyleRegular, cc, StringAlignmentNear);
    }
}
// Une ligne de carte : sa hauteur pour une largeur (dessinee seulement si elle croise la zone visible ; zones cliquables
// retirees si elle n'est pas entierement visible).
static float SetItemH(const SetItem &it, float w)
{
    if (it.kind == SI_SEC) return SetSecH();
    if (it.kind == SI_GFX) { int idx = SetGfxIdx(it.key); return idx >= 0 ? SetGfxPlan(it, idx, w).h : SetRowH(); }
    if (it.kind == SI_OPT) return SetOptH(it, w);
    const char *k = it.key;
    if (!strcmp(k, "presets")) return 72;
    if (!strcmp(k, "rtbox")) return 70;
    if (!strcmp(k, "footer")) return 48;
    if (!strcmp(k, "voicehelp")) return 150;
    if (!strcmp(k, "ajoutchips")) return 8 + 32.0f * SetChipLines(0);
    if (!strcmp(k, "themes")) return 166;
    if (!strcmp(k, "hudstyles")) return 166;
    if (!strcmp(k, "hudapercu")) return 236;
    if (!strcmp(k, "apercu")) return 256;
    if (!strcmp(k, "lang")) return 72;
    if (!strcmp(k, "folder")) return 52;
    if (!strcmp(k, "about")) return 48;
    if (!strcmp(k, "update") || !strcmp(k, "notes")) return SetRowDH();
    if (!strcmp(k, "buy")) return 50;
    if (!strcmp(k, "accent")) return SetCtlRowH(T(it.fr, it.en), T(it.dFr, it.dEn), w, 5 * 30 + 4 * 8);
    if (!strcmp(k, "texte")) return SetCtlRowH(T(it.fr, it.en), L"", w, kSetSliderW);
    if (!strcmp(k, "aurore") || !strcmp(k, "densite") || !strcmp(k, "max") || !strcmp(k, "prive") || !strcmp(k, "net") || !strcmp(k, "canal") || !strcmp(k, "sessions") || !strcmp(k, "start") || !strcmp(k, "ajouts")) {
        std::vector<std::wstring> labs;
        if (!strcmp(k, "aurore")) labs = { T(L"Off", L"Off"), T(L"Calme", L"Calm"), T(L"Vivant", L"Lively") };
        else if (!strcmp(k, "densite")) labs = { T(L"Aérée", L"Airy"), T(L"Normale", L"Normal"), T(L"Compacte", L"Compact") };
        else if (!strcmp(k, "max")) labs = { L"4", L"8", L"12", L"16" };
        else if (!strcmp(k, "prive")) labs = { T(L"Public", L"Public"), T(L"Privé", L"Private") };
        else if (!strcmp(k, "net")) labs = { L"Steam", L"IP / VPN" };
        else if (!strcmp(k, "canal")) labs = { L"Legacy", L"Reworked" };
        else if (!strcmp(k, "sessions")) labs = { L"3", L"10", L"30" };
        else if (!strcmp(k, "start")) labs = { T(L"Copie", L"Copy"), T(L"Dossier du jeu", L"Game folder"), L"Steam" };
        else labs = { T(L"Tous", L"All"), T(L"Choisir", L"Choose"), T(L"Aucun", L"None") };
        return SetCtlRowH(T(it.fr, it.en), it.dFr ? T(it.dFr, it.dEn) : L"", w, SetSegW(labs, 12));
    }
    if (it.fr) {   // interrupteur (44) ou bouton de ligne : nom sur deux lignes ou commande dessous si besoin
        float wid = 44;
        if (!strcmp(k, "steamline")) wid = SetButtonW(T(L"Voir la ligne", L"Show the line"));
        else if (!strcmp(k, "repair")) wid = SetButtonW(T(L"Réparer", L"Repair"));
        else if (!strcmp(k, "api")) wid = SetButtonW(T(L"Ouvrir", L"Open"));
        else if (!strcmp(k, "github")) wid = SetButtonW(L"GitHub");
        else if (!strcmp(k, "logs")) wid = SetButtonW(T(L"Ouvrir le dossier", L"Open the folder")) + SetButtonW(T(L"Faire le zip", L"Make the zip")) + 8;
        return SetCtlRowH(T(it.fr, it.en), it.dFr ? T(it.dFr, it.dEn) : L"", w, wid);
    }
    return it.dFr ? SetRowDH() : SetRowH();
}
static float SetDrawItem(Graphics &g, const SetItem &it, float x, float y, float w)
{
    float h = SetItemH(it, w);
    RectF row(x, y, w, h);
    if (!SetDrawn(row)) return h;
    size_t nz = g_zones.size();
    if (it.kind == SI_SEC) Text(g, T(it.fr, it.en), RectF(x, y + h - 20, w, 18), 10.5f, FontStyleBold, kAcc, StringAlignmentNear);
    else if (it.kind == SI_GFX) { int idx = SetGfxIdx(it.key); if (idx >= 0) SetGfxRow(g, it, idx, row, SetGfxPlan(it, idx, w)); }
    else if (it.kind == SI_OPT) SetOptRow(g, it, row);
    else {
        const char *k = it.key;
        float cy = y + h / 2;
        if (!strcmp(k, "presets")) {   // tuiles : les prereglages, puis Perso (allumee quand aucun ne correspond)
            const auto &ps = GfxPresets();
            int on = GfxPresetOn(), n = (int)ps.size() + 1;
            float tw = (w - (n - 1) * 8) / n;
            for (int i = 0; i < n; i++) {
                RectF r(x + i * (tw + 8), y + 4, tw, 60);
                bool perso = i == n - 1, sel = perso ? on < 0 : i == on, hot = !perso && g_uiHot == UI_GFX_PRESET + i;
                GraphicsPath p; RoundRect(p, r, 12);
                SolidBrush fb(sel ? WithA(kAcc, 0.16f) : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&fb, &p);
                Pen pen(sel ? kAcc : TH(fieldBorder), sel ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
                Text(g, perso ? T(L"Perso", L"Custom") : T(ps[i].fr, ps[i].en), RectF(r.X + 2, r.Y + 9, r.Width - 4, 20), 12.5f, FontStyleBold, kInk);
                Text(g, perso ? T(L"mes choix", L"my choices") : T(ps[i].dFr, ps[i].dEn), RectF(r.X + 2, r.Y + 31, r.Width - 4, 18), 10, FontStyleRegular, kGrey);
                if (!perso) HotZone(r, UI_GFX_PRESET + i);
            }
        } else if (!strcmp(k, "rtbox")) {   // encart ray tracing (maquette Jeu) : icone, titre + jeton, resume, interrupteur
            bool dxr = GfxDxr(), rt = dxr && GfxGet("RayTracing") == 1;
            RectF box(x, y + 6, w, 58);
            GraphicsPath p; RoundRect(p, box, 12);
            Color violet(255, 160, 110, 255);
            SolidBrush fb(WithA(violet, 0.10f)); g.FillPath(&fb, &p);
            Pen pen(WithA(violet, 0.4f), 1.0f); g.DrawPath(&pen, &p);
            RectF ib(box.X + 12, box.Y + 10, 38, 38);
            GraphicsPath ip; RoundRect(ip, ib, 11);
            SolidBrush ibb(WithA(violet, 0.25f)); g.FillPath(&ibb, &ip);
            Icon(g, IC_SPARK, ib.X + 19, ib.Y + 19, 20, Color(255, 215, 196, 255), 2.0f);
            const wchar_t *tt = L"Ray tracing";
            float tw = MeasureW(MeasureG(), tt, 13.5f, FontStyleBold), tx = box.X + 62;
            Text(g, tt, RectF(tx, box.Y + 8, tw + 4, 20), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
            std::wstring chip = dxr ? (rt ? T(L"Activé", L"On") : T(L"GPU récent", L"Recent GPU")) : T(L"Carte non compatible", L"GPU not supported");
            Color cc = dxr ? (rt ? kOk : kGrey) : kWarn;
            Pill(g, RectF(tx + tw + 8, box.Y + 9, PillW(chip, 10) + 2, 20), WithA(cc, 0.18f), chip, cc, 10);
            std::wstring l2;
            if (rt) {
                wchar_t b[200];
                swprintf_s(b, T(L"%d rayons/pixel · portée %d m · %d lampes · reflets, rebond, occlusion", L"%d rays/pixel · %d m range · %d lamps · reflections, bounce, occlusion"), GfxGet("RTRayons"), GfxGet("RTPortee"), GfxGet("RTLampes"));
                l2 = b;
            } else l2 = T(it.dFr, it.dEn);
            Text(g, l2, RectF(tx, box.Y + 30, box.Width - (tx - box.X) - 70, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            bool hot = g_uiHot == UI_SET_RTTOGGLE;
            if (dxr) { UiToggle(g, RectF(box.X + box.Width - 58, box.Y + 17, 44, 24), rt, hot); HotZone(box, UI_SET_RTTOGGLE); }
        } else if (!strcmp(k, "footer")) SetFooter(g, it, row);
        else if (!strcmp(k, "f10")) {   // touche du jeu lui-meme : montree, pas modifiable
            SetRowLabel(g, row, T(it.fr, it.en), T(it.dFr, it.dEn), w - 56);
            SetKeyCap(g, RectF(x + w - 44, cy - 15, 44, 30), L"F10", false, false, true);
        } else if (!strcmp(k, "voicehelp")) {
            Para(g, T(L"Le vocal est de proximité : on t'entend jusqu'à la portée choisie, de moins en moins fort en s'éloignant, comme en vrai. Le micro et les haut-parleurs sont ceux de Windows (périphériques par défaut).\n\nLa touche pour parler (tenue) est dans l'onglet Touches. Le volume des voix ne change que ce que tu entends ; ton micro, ce que les autres entendent de toi.",
                      L"Voice is proximity-based: you are heard up to the chosen range, fading with distance, like in real life. The microphone and speakers are Windows' default devices.\n\nThe push-to-talk key is in the Keys tab. Voice volume only changes what you hear; your microphone, what the others hear from you."),
                 RectF(x, y + 4, w, h - 8), 12.5f, kGrey);
        } else if (!strcmp(k, "ajouts")) {   // Tous / Choisir / Aucun : SansAjouts, et les quatre interrupteurs
            bool none = GfxGet("SansAjouts") == 1;
            bool all = true;
            for (const char *a : kAjoutKeys) all = all && GfxGet(a) == 1;
            SetSegRow(g, it, row, { T(L"Tous", L"All"), T(L"Choisir", L"Choose"), T(L"Aucun", L"None") }, none ? 2 : all ? 0 : 1, UI_SET_AJOUTS);
        } else if (!strcmp(k, "ajoutchips")) {   // un jeton par ajout, cliquable (grise quand tout est coupe)
            bool none = GfxGet("SansAjouts") == 1;
            SetChipLines(w);
            float cx = x, ly = y + 7;
            for (int i = 0; i < kAjoutN; i++) {
                bool on = GfxGet(kAjoutKeys[i]) == 1 && !none, hot = !none && g_uiHot == UI_SET_AJOUT + i;
                std::wstring s = T(kAjoutFr[i], kAjoutEn[i]);
                float cw = PillW(s, 11) + 22;
                if (cx > x && cx + cw > x + w) { cx = x; ly += 32; }
                RectF r(cx, ly, cw, 26);
                GraphicsPath p; RoundRect(p, r, 13);
                SolidBrush b(on ? WithA(kAcc, 0.2f) : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
                Pen pen(on ? WithA(kAcc, 0.7f) : TH(fieldBorder), 1.0f); g.DrawPath(&pen, &p);
                Color ink = WithA(on ? kAcc : kInk, none ? 0.4f : 1.0f);
                if (on) Icon(g, IC_CHECK, r.X + 14, r.Y + 13, 13, ink, 2.4f);
                Text(g, s, RectF(r.X + (on ? 22.0f : 8.0f), r.Y, r.Width - (on ? 26.0f : 12.0f), r.Height), 11, FontStyleBold, ink);
                if (!none) HotZone(r, UI_SET_AJOUT + i);
                cx += cw + 6;
            }
        } else if (!strcmp(k, "max")) {
            int mx = (int)GetPrivateProfileIntW(L"Lanceur", L"SalonMax", 8, g_iniLauncher.c_str());
            SetSegRow(g, it, row, { L"4", L"8", L"12", L"16" }, mx <= 4 ? 0 : mx <= 8 ? 1 : mx <= 12 ? 2 : 3, UI_SET_MAXP);
        } else if (!strcmp(k, "prive")) {
            bool prive = GetPrivateProfileIntW(L"Lanceur", L"SalonPrive", 1, g_iniLauncher.c_str()) != 0;
            SetSegRow(g, it, row, { T(L"Public", L"Public"), T(L"Privé", L"Private") }, prive ? 1 : 0, UI_SET_PRIV);
        } else if (!strcmp(k, "net")) SetSegRow(g, it, row, { L"Steam", L"IP / VPN" }, g_steamNet ? 0 : 1, UI_SET_STEAM, g_lobby == LB_NONE);
        else if (!strcmp(k, "steamline")) SetButtonRow(g, it, row, T(L"Voir la ligne", L"Show the line"), UI_SET_STEAMLINE);
        else if (!strcmp(k, "hudstyles")) {   // quatre tuiles : Vanilla, Carte, Anneaux, Bandeau (apercu miniature)
            static const wchar_t *nf[] = { L"Vanilla", L"Carte", L"Anneaux", L"Bandeau" }, *ne[] = { L"Vanilla", L"Card", L"Rings", L"Strip" };
            static const wchar_t *sf[] = { L"Le HUD d'origine du jeu", L"Verre dépoli dans un coin", L"Discret, en bas", L"Fin, centré en haut" };
            static const wchar_t *se[] = { L"The game's own HUD", L"Frosted glass in a corner", L"Discreet, at the bottom", L"Thin, centred at the top" };
            int cur = GfxGet("Style");
            float tw = (w - 30) / 4;
            for (int i = 0; i < 4; i++) {
                RectF r(x + i * (tw + 10), y + 4, tw, 158);
                bool sel = i == cur, hot = g_uiHot == UI_SET_HUDSTYLE + i;
                GraphicsPath p; RoundRect(p, r, 14);
                SolidBrush fb(sel ? WithA(kAcc, 0.12f) : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&fb, &p);
                Pen pen(sel ? kAcc : TH(fieldBorder), sel ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
                HudPreview(g, RectF(r.X + 8, r.Y + 8, r.Width - 16, 70), i, false);
                Text(g, T(nf[i], ne[i]), RectF(r.X + 8, r.Y + 84, r.Width - 16, 20), 13, FontStyleBold, kInk, StringAlignmentNear);
                std::wstring sub = sel ? std::wstring(T(L"Choisi · ", L"Chosen · ")) + T(sf[i], se[i]) : std::wstring(T(sf[i], se[i]));
                Para(g, sub, RectF(r.X + 8, r.Y + 104, r.Width - 16, 48), 10.5f, kGrey);
                HotZone(r, UI_SET_HUDSTYLE + i);
            }
        } else if (!strcmp(k, "hudapercu")) HudPreview(g, RectF(x, y + 4, w, 228), GfxGet("Style"), true);
        else if (!strcmp(k, "themes")) {   // quatre tuiles : Nuit boreale, Plein jour, Aurore verte, Garage
            static const wchar_t *nf[] = { L"Nuit boréale", L"Plein jour", L"Aurore verte", L"Garage" }, *ne[] = { L"Northern night", L"Daylight", L"Green aurora", L"Garage" };
            static const wchar_t *sf[] = { L"Aurore et neige", L"Clair, neige au soleil", L"Contraste fort, accent menthe", L"Bois, ambre, lampe à huile" };
            static const wchar_t *se[] = { L"Aurora and snow", L"Light, snow in the sun", L"Strong contrast, mint accent", L"Wood, amber, oil lamp" };
            float tw = (w - 30) / 4;
            for (int i = 0; i < 4; i++) {
                RectF r(x + i * (tw + 10), y + 4, tw, 158);
                bool sel = i == g_theme, hot = g_uiHot == UI_SET_THEMECARD + i;
                GraphicsPath p; RoundRect(p, r, 14);
                SolidBrush fb(sel ? WithA(kAcc, 0.12f) : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&fb, &p);
                Pen pen(sel ? kAcc : TH(fieldBorder), sel ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
                RectF pv(r.X + 8, r.Y + 8, r.Width - 16, 70);
                if (i == 0) SetThemePreview(g, pv, Color(255, 10, 21, 48), Color(255, 22, 48, 95), Color(115, 72, 240, 176), Color(100, 160, 110, 255), Color(26, 255, 255, 255), Color(50, 255, 255, 255), kDark.accent);
                else if (i == 1) SetThemePreview(g, pv, Color(255, 223, 240, 255), Color(255, 247, 251, 255), Color(140, 120, 200, 255), Color(0, 0, 0, 0), Color(205, 255, 255, 255), Color(40, 20, 50, 90), kLight.accent);
                else if (i == 2) SetThemePreview(g, pv, Color(255, 7, 18, 28), Color(255, 12, 34, 50), Color(150, 72, 240, 176), Color(0, 0, 0, 0), Color(20, 255, 255, 255), Color(75, 124, 232, 168), kAurore.accent);
                else SetThemePreview(g, pv, Color(255, 26, 20, 16), Color(255, 42, 30, 20), Color(115, 255, 180, 84), Color(0, 0, 0, 0), Color(20, 255, 255, 255), Color(75, 255, 200, 120), kGarage.accent);
                Text(g, T(nf[i], ne[i]), RectF(r.X + 8, r.Y + 84, r.Width - 16, 20), 13, FontStyleBold, kInk, StringAlignmentNear);
                std::wstring sub = sel ? std::wstring(T(L"Thème actuel · ", L"Current theme · ")) + T(sf[i], se[i]) : std::wstring(T(sf[i], se[i]));
                Para(g, sub, RectF(r.X + 8, r.Y + 104, r.Width - 16, 48), 10.5f, kGrey);
                HotZone(r, UI_SET_THEMECARD + i);
            }
        } else if (!strcmp(k, "accent")) {   // cinq pastilles de couleur
            float sw = 5 * 30 + 4 * 8;
            std::wstring lab = T(it.fr, it.en);
            SetRowLabel(g, row, lab, T(it.dFr, it.dEn), SetLabelMaxW(lab, w, sw));
            RectF ctl = SetCtlRect(row, lab, sw, 30);
            for (int i = 0; i < 5; i++) {
                RectF r(ctl.X + i * 38, ctl.Y, 30, 30);
                bool sel = i == g_accentSel, hot = g_uiHot == UI_SET_ACCENT + i;
                GraphicsPath p; RoundRect(p, r, 10);
                Color c = g_dark ? kSetAccents[i].dark : kSetAccents[i].light;
                if (i == 0 && g_theme == THEME_AURORE) c = kAuroreAccent;   // (pastille d'origine : l'accent propre du theme)
                if (i == 0 && g_theme == THEME_GARAGE) c = kGarageAccent;
                SolidBrush b(c); g.FillPath(&b, &p);
                Pen pen(sel ? kInk : WithA(kInk, hot ? 0.6f : 0.25f), sel ? 2.4f : 1.2f); g.DrawPath(&pen, &p);
                if (sel) Icon(g, IC_CHECK, r.X + 15, r.Y + 15, 14, g_dark ? Color(255, 6, 19, 37) : Color(255, 255, 255, 255), 2.6f);
                HotZone(r, UI_SET_ACCENT + i);
            }
        } else if (!strcmp(k, "aurore")) SetSegRow(g, it, row, { T(L"Off", L"Off"), T(L"Calme", L"Calm"), T(L"Vivant", L"Lively") }, g_aurore, UI_SET_AURORE);
        else if (!strcmp(k, "verre")) SetToggleRow(g, it, row, g_glass, UI_SET_GLASS);
        else if (!strcmp(k, "densite")) SetSegRow(g, it, row, { T(L"Aérée", L"Airy"), T(L"Normale", L"Normal"), T(L"Compacte", L"Compact") }, g_densite, UI_SET_DENS);
        else if (!strcmp(k, "texte")) {   // curseur 80..130 % par pas de 5
            std::wstring lab = T(it.fr, it.en);
            SetRowLabel(g, row, lab, L"", SetLabelMaxW(lab, w, kSetSliderW));
            SetSlider(g, SetCtlRect(row, lab, kSetSliderW, 30), 7, (g_texte - 80) / 5, UI_SET_TEXTE, std::to_wstring(g_texte) + L" %");
        } else if (!strcmp(k, "winfollow")) SetToggleRow(g, it, row, ThemeFollowsWindows(), UI_SET_WINTHEME);
        else if (!strcmp(k, "apercu")) {   // petit salon aux couleurs du theme ; Par defaut, Appliquer
            const ScenePal &pal = ScenePalette();
            RectF box(x, y + 4, w, 196);
            GraphicsPath p; RoundRect(p, box, 16);
            GraphicsState st = g.Save();
            g.SetClip(&p, CombineModeIntersect);
            LinearGradientBrush sky(RectF(box.X, box.Y - 1, box.Width, box.Height + 2), pal.sky[1], pal.sky[3], LinearGradientModeVertical);
            g.FillPath(&sky, &p);
            Color aurs[2] = { pal.aurA[1], g_dark ? pal.aurB[1] : Color(0, 0, 0, 0) };
            for (int i = 0; i < 2 && g_aurore > 0; i++) {
                if (!aurs[i].GetA()) continue;
                GraphicsPath e; e.AddEllipse(box.X + (i ? box.Width * 0.4f : -box.Width * 0.25f), box.Y - 70, box.Width * 0.9f, 150.0f);
                PathGradientBrush pg(&e);
                pg.SetCenterColor(WithA(aurs[i], (g_aurore == 1 ? 0.5f : 1.0f) * 0.7f));
                Color edge(0, aurs[i].GetR(), aurs[i].GetG(), aurs[i].GetB()); int one = 1;
                pg.SetSurroundColors(&edge, &one);
                g.FillPath(&pg, &e);
            }
            g.Restore(st);
            Pen bp(TH(panelBorder), 1.0f); g.DrawPath(&bp, &p);
            Text(g, L"MWCoop", RectF(box.X + 14, box.Y + 10, 120, 22), 13, FontStyleBold, kInk, StringAlignmentNear);
            SetChip(g, box.X + box.Width - 14, box.Y + 21, T(L"Privé", L"Private"), kInk);
            RectF cb(box.X + 14, box.Y + 40, box.Width - 28, 52);
            GraphicsPath cp; RoundRect(cp, cb, 12);
            SolidBrush cbb(g_glass ? TH(card) : TH(solid)); g.FillPath(&cbb, &cp);
            Pen cpen(TH(choiceBorder), 1.0f); g.DrawPath(&cpen, &cp);
            Text(g, T(L"CODE DE SESSION", L"SESSION CODE"), RectF(cb.X + 12, cb.Y + 6, 200, 14), 9.5f, FontStyleBold, kAcc, StringAlignmentNear);
            Text(g, L"K 7 P M 3 Q", RectF(cb.X + 12, cb.Y + 20, 200, 28), 19, FontStyleBold, kInk, StringAlignmentNear);
            float bw = (box.Width - 28 - 8) / 2;
            RectF b1(box.X + 14, box.Y + 102, bw, 32), b2(b1.X + bw + 8, b1.Y, bw, 32);
            GraphicsPath p1; RoundRect(p1, b1, 11); SolidBrush pb(kAcc); g.FillPath(&pb, &p1);
            Text(g, T(L"LANCER", L"START"), b1, 12, FontStyleBold, kOnAcc);
            GraphicsPath p2; RoundRect(p2, b2, 11); SolidBrush sb(TH(btn2)); g.FillPath(&sb, &p2); Pen spen(TH(fieldBorder), 1.0f); g.DrawPath(&spen, &p2);
            Text(g, T(L"INVITER", L"INVITE"), b2, 12, FontStyleBold, kInk);
            Text(g, T(L"Triches (F7)", L"Cheats (F7)"), RectF(box.X + 14, box.Y + 142, 200, 24), 12.5f, FontStyleBold, kInk, StringAlignmentNear);
            UiToggle(g, RectF(box.X + box.Width - 14 - 44, box.Y + 142, 44, 24), true, false);
            float cx2 = box.X + 14;
            const wchar_t *cf[] = { L"Prêt", L"Attend", L"Pas prêt" }, *ce[] = { L"Ready", L"Waiting", L"Not ready" };
            Color cc[] = { kOk, kWarn, kGrey };
            for (int i = 0; i < 3; i++) { std::wstring s = T(cf[i], ce[i]); float pw = PillW(s, 10.5f) + 4; Pill(g, RectF(cx2, box.Y + 174, pw, 20), WithA(cc[i], 0.18f), s, cc[i], 10.5f); cx2 += pw + 6; }
            float hw = (w - 10) / 2;
            UiButton(g, RectF(x, y + 212, hw, 36), T(L"Par défaut", L"Default"), UI_SET_THEMEDEF, false, IC_UNDO, !ThemeIsDefault());
            UiButton(g, RectF(x + hw + 10, y + 212, hw, 36), T(L"Appliquer", L"Apply"), UI_SET_SAVE, true, IC_CHECK);
        } else if (!strcmp(k, "lang")) {   // quatre tuiles : FR EN RU AR
            static const wchar_t *code[] = { L"FR", L"EN", L"RU", L"AR" }, *name[] = { L"Français", L"English", L"Русский", L"العربية" };
            int sel = g_fr ? 0 : g_ru ? 2 : g_ar ? 3 : 1;
            float tw = (w - 24) / 4;
            for (int i = 0; i < 4; i++) {
                RectF r(x + i * (tw + 8), y + 4, tw, 62);
                bool on = i == sel, hot = g_uiHot == UI_SET_LANG + i;
                GraphicsPath p; RoundRect(p, r, 12);
                SolidBrush fb(on ? WithA(kAcc, 0.16f) : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&fb, &p);
                Pen pen(on ? kAcc : TH(fieldBorder), on ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
                Text(g, code[i], RectF(r.X, r.Y + 9, r.Width, 22), 15, FontStyleBold, kInk);
                Text(g, name[i], RectF(r.X, r.Y + 33, r.Width, 18), 11, FontStyleRegular, kGrey);
                HotZone(r, UI_SET_LANG + i);
            }
        } else if (!strcmp(k, "update")) {   // etat de la mise a jour : Verifier, ou Mettre a jour
            std::wstring av = UpdAvail();
            std::wstring l1, l2 = g_localVer.empty() ? std::wstring(T(L"MWCoop n'est pas installé", L"MWCoop is not installed")) : std::wstring(T(L"Tu as la ", L"You have ")) + g_localVer;
            if (!av.empty()) l1 = std::wstring(T(L"Version ", L"Version ")) + av + T(L" disponible", L" available");
            else if (g_busy) l1 = T(L"Recherche ou installation en cours…", L"Checking or installing…");
            else l1 = T(L"MWCoop est à jour", L"MWCoop is up to date");
            std::wstring bt = av.empty() ? T(L"Vérifier", L"Check") : T(L"Mettre à jour", L"Update");
            float bw = SetButtonW(bt) + (av.empty() ? 0 : 24);
            SetRowLabel(g, row, l1, l2, w - bw - 12);
            if (!av.empty()) UiButton(g, RectF(x + w - bw, cy - 16, bw, 32), bt, UI_NOTE_UPDATE, true, IC_DOWNLOAD, g_lobby == LB_NONE && !g_busy);
            else UiButton(g, RectF(x + w - bw, cy - 16, bw, 32), bt, UI_SET_CHECK, false, -1, !g_busy);
        } else if (!strcmp(k, "canal")) SetSegRow(g, it, row, { L"Legacy", L"Reworked" }, ChannelReworked() ? 1 : 0, UI_SET_CANAL);
        else if (!strcmp(k, "majauto")) SetToggleRow(g, it, row, GetPrivateProfileIntW(L"Lanceur", L"MajAuto", 0, g_iniLauncher.c_str()) != 0, UI_SET_AUTO);
        else if (!strcmp(k, "nightly")) SetToggleRow(g, it, row, NightlyOn(), UI_SET_NIGHTLY);
        else if (!strcmp(k, "notes")) {
            wchar_t seen[32] = L"";
            GetPrivateProfileStringW(L"Lanceur", L"NotesVues", L"", seen, 32, g_iniLauncher.c_str());
            std::wstring d = seen[0] ? std::wstring(T(L"Dernière lue : ", L"Last read: ")) + seen : std::wstring(T(it.dFr, it.dEn));
            std::wstring bt = T(L"Lire", L"Read");
            float bw = SetButtonW(bt);
            SetRowLabel(g, row, T(it.fr, it.en), d, w - bw - 12);
            UiButton(g, RectF(x + w - bw, cy - 16, bw, 32), bt, UI_SET_NOTES, false);
        } else if (!strcmp(k, "folder")) {   // dossier du jeu : chemin, Changer (la version trouvee : en-tete de la carte)
            bool ok = !g_gameDir.empty();
            Text(g, T(it.fr, it.en), RectF(x, y, 200, 16), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            std::wstring bt = ok ? T(L"Changer", L"Change") : T(L"Choisir l'exe…", L"Choose exe…");
            float bw = SetButtonW(bt) + 24;
            RectF pb(x, y + 20, w - bw - 8, 30);
            GraphicsPath pp; RoundRect(pp, pb, 9);
            SolidBrush pbb(TH(field)); g.FillPath(&pbb, &pp);
            Pen ppen(TH(fieldBorder), 1.0f); g.DrawPath(&ppen, &pp);
            StringFormat sf; sf.SetTrimming(StringTrimmingEllipsisPath); sf.SetFormatFlags(StringFormatFlagsNoWrap); sf.SetLineAlignment(StringAlignmentCenter);
            SolidBrush gb(ok ? kInk : kGrey);
            DrawStr(g, ok ? g_gameDir.c_str() : T(L"(montre-lui mywintercar.exe)", L"(show it mywintercar.exe)"), -1, FontOf(L"Consolas", 11.5f, FontStyleRegular), RectF(pb.X + 10, pb.Y, pb.Width - 20, pb.Height), &sf, &gb);
            UiButton(g, RectF(x + w - bw, y + 19, bw, 32), bt, UI_SET_FOLDER, false, IC_FOLDER);
        } else if (!strcmp(k, "start")) {
            const Opt *ls = OptByKey("LancerSteam");
            int how = ls && OptGet(*ls) && !IsWine() ? 2 : NoCopy() ? 1 : 0;   // (Wine : jamais par Steam, cf. LaunchGame)
            SetSegRow(g, it, row, { T(L"Copie", L"Copy"), T(L"Dossier du jeu", L"Game folder"), L"Steam" }, how, UI_SET_START);
            g_setSteamRow = row;
        } else if (!strcmp(k, "mscl")) {
            bool own = MscOwn(), inst = own && MscOwnInstalled();
            std::wstring s = !own ? T(L"MSCLoader officiel", L"Official MSCLoader") : inst ? T(L"Installé", L"Installed") : T(L"À installer", L"To install");
            SetRowLabel(g, row, T(it.fr, it.en), T(it.dFr, it.dEn), w - PillW(s, 10.5f) - 16);
            SetChip(g, x + w, cy, s, !own ? kGrey : inst ? kOk : kWarn);
        } else if (!strcmp(k, "about")) {
            Para(g, T(L"Mod non officiel et non commercial, non affilié à Amistech Games. Nécessite une copie légale de My Winter Car.",
                      L"Unofficial, non-commercial mod, not affiliated with Amistech Games. Requires a legal copy of My Winter Car."), RectF(x, y + 6, w, 40), 11.5f, kGrey);
        } else if (!strcmp(k, "buy")) {
            g_setBuy = RectF(x, y + 6, (w - 10) / 2, 38); g_setUninst = RectF(x + (w + 10) / 2, y + 6, (w - 10) / 2, 38);
            UiButton(g, g_setBuy, T(L"Acheter le jeu sur Steam", L"Buy the game on Steam"), UI_SET_BUY, false);
            UiButton(g, g_setUninst, T(L"Désinstaller MWCoop", L"Uninstall MWCoop"), UI_SET_UNINST, false, IC_TRASH);
        } else if (!strcmp(k, "logs")) {   // deux boutons : le dossier, le zip
            std::wstring b1 = T(L"Ouvrir le dossier", L"Open the folder"), b2 = T(L"Faire le zip", L"Make the zip");
            float w1 = SetButtonW(b1), w2 = SetButtonW(b2);
            SetRowLabel(g, row, T(it.fr, it.en), T(it.dFr, it.dEn), w - w1 - w2 - 20);
            bool ok = !g_gameDir.empty();
            UiButton(g, RectF(x + w - w1 - w2 - 8, cy - 16, w1, 32), b1, UI_SET_LOGDIR, false, IC_FOLDER, ok);
            UiButton(g, RectF(x + w - w2, cy - 16, w2, 32), b2, UI_SET_LOGZIP, true, IC_ZIP, ok);
        } else if (!strcmp(k, "sessions")) { int n = LogsSessionsKept(); SetSegRow(g, it, row, { L"3", L"10", L"30" }, n == 3 ? 0 : n == 10 ? 1 : 2, UI_SET_SESS); }
        else if (!strcmp(k, "compat")) { SetToggleRow(g, it, row, CompatAuto(), UI_SET_COMPAT); g_setCompatRow = row; }
        else if (!strcmp(k, "debug")) SetToggleRow(g, it, row, g_debug, UI_LOG_DEBUG);
        else if (!strcmp(k, "discordask")) SetToggleRow(g, it, row, GetPrivateProfileIntW(L"Lanceur", L"DiscordPropose", 0, g_iniLauncher.c_str()) == 0, UI_SET_DISCORDASK);
        else if (!strcmp(k, "repair")) SetButtonRow(g, it, row, T(L"Réparer", L"Repair"), UI_SET_REPAIR, false, !g_gameDir.empty() && g_lobby == LB_NONE && !g_busy);
        else if (!strcmp(k, "api")) { SetButtonRow(g, it, row, T(L"Ouvrir", L"Open"), UI_SET_API); g_setApiRow = row; }
        else if (!strcmp(k, "github")) SetButtonRow(g, it, row, L"GitHub", UI_SET_GITHUB);
    }
    if (!SetVisible(row)) g_zones.resize(nz);   // (ligne coupee par le bord : pas de clic)
    return h;
}

// Element a droite du titre d'une carte (kSetCards::hdr).
static void SetCardHeader(Graphics &g, const SetCard &c, float right, float y, float w)
{
    if (!c.hdr) return;
    float cy = y + 26;
    if (!strcmp(c.hdr, "goimage")) {   // lien vers l'onglet Image & ray tracing
        std::wstring s = T(L"Tout l'onglet Image & ray tracing", L"The whole Image & ray tracing tab");
        float tw = MeasureW(MeasureG(), s, 12, FontStyleBold) + 22;
        if (tw > w) { s = T(L"Onglet Image", L"Image tab"); tw = MeasureW(MeasureG(), s, 12, FontStyleBold) + 22; }   // (gros texte : court)
        RectF r(right - tw, cy - 12, tw, 24);
        bool hot = g_uiHot == UI_SET_GOIMAGE;
        Text(g, s, RectF(r.X, r.Y, tw - 18, 24), 12, FontStyleBold, WithA(kAcc, hot ? 1.0f : 0.85f), StringAlignmentNear);
        Icon(g, IC_CHEVR, right - 9, cy, 14, kAcc, 2.2f);
        HotZone(r, UI_SET_GOIMAGE);
    } else if (!strcmp(c.hdr, "hostchip")) SetChip(g, right, cy, T(L"Modifiables dans chaque salon", L"Editable in each lobby"), kInk);
    else if (!strcmp(c.hdr, "netchip")) {
        if (g_netTest.empty()) return;
        int port = 7870;
        if (const Opt *o = OptByKey("Port")) port = OptGet(*o);
        wchar_t b[64];
        if (g_netTestKind == K_OK) swprintf_s(b, T(L"UDP %d ouvert", L"UDP %d open"), port);
        SetChip(g, right, cy, g_netTestKind == K_OK ? b : g_netTestKind == K_WARN ? T(L"À vérifier", L"To check") : T(L"Port ou pare-feu", L"Port or firewall"), g_netTestKind == K_OK ? kOk : g_netTestKind == K_WARN ? kWarn : kRed);
    } else if (!strcmp(c.hdr, "dronekb")) {
        std::vector<std::wstring> l = { L"ZQSD", L"WASD", T(L"Flèches", L"Arrows") };
        float sw = SetSegW(l, 12);
        SetSeg(g, RectF(right - sw, cy - 15, sw, 30), l, DroneLayoutGet(), UI_SET_DRONEKB, 12);
    } else if (!strcmp(c.hdr, "updchip")) {
        std::wstring av = UpdAvail();
        std::wstring s = !av.empty() ? av + T(L" disponible", L" available") : g_localVer.empty() ? std::wstring(T(L"non installé", L"not installed")) : g_localVer + T(L" · à jour", L" · up to date");
        SetChip(g, right, cy, s, !av.empty() ? kAcc : g_localVer.empty() ? kWarn : kOk);
    } else if (!strcmp(c.hdr, "mwcver")) {
        bool ok = !g_gameDir.empty();
        SetChip(g, right, cy, ok ? L"My Winter Car " + g_gameVer : std::wstring(T(L"My Winter Car introuvable", L"My Winter Car not found")), ok ? kOk : kRed);
    }
}
// Une carte : titre (et sous-titre, element d'en-tete), ses lignes (pleine largeur, deux sous-colonnes apres SI_BREAK,
// pied de carte), verre dessous ; renvoie sa hauteur.
static float SetDrawCard(Graphics &g, int ci, float x, float y, float w)
{
    const SetCard &c = kSetCards[ci];
    bool sub = c.sFr != NULL;
    float y0 = y, inner = w - 2 * kSetPad;
    if (SetDrawn(RectF(x, y, w, 50))) {
        float tw = MeasureW(MeasureG(), T(c.fr, c.en), 16, FontStyleBold);
        Title(g, T(c.fr, c.en), RectF(x + kSetPad, y + 14, inner, 24), 16, kInk);
        if (sub) Text(g, T(c.sFr, c.sEn), RectF(x + kSetPad, y + 38, inner, 16), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
        SetCardHeader(g, c, x + w - kSetPad, y, inner - tw - 12);
    }
    y += sub ? 60 : 48;
    bool split = false;
    for (int i = 0; i < kSetItemN; i++) if (kSetItems[i].card == ci && kSetItems[i].kind == SI_BREAK) split = true;
    for (int i = 0; i < kSetItemN; i++) if (kSetItems[i].card == ci && kSetItems[i].wide == 1) y += SetDrawItem(g, kSetItems[i], x + kSetPad, y, inner);
    float cw = split ? (inner - 24) / 2 : inner, cx = x + kSetPad, cy = y, maxY = y;
    for (int i = 0; i < kSetItemN; i++) {
        const SetItem &it = kSetItems[i];
        if (it.card != ci || it.wide) continue;
        if (it.kind == SI_BREAK) { maxY = max(maxY, cy); cx += cw + 24; cy = y; continue; }
        cy += SetDrawItem(g, it, cx, cy, cw);
    }
    maxY = max(maxY, cy);
    for (int i = 0; i < kSetItemN; i++) if (kSetItems[i].card == ci && kSetItems[i].wide == 2) maxY += SetDrawItem(g, kSetItems[i], x + kSetPad, maxY, inner);
    float h = maxY - y0 + 12;
    float t = max(y0, g_setTop), b = min(y0 + h, g_setBottom);   // (le verre n'est pas clippe : coupe a la zone visible)
    if (b - t > 6) Glass(RectF(x, t, w, b - t));
    return h;
}
// Nom et explication d'une ligne (recherche) ; faux si la ligne n'a pas de nom.
static bool SetItemLabel(const SetItem &it, std::wstring &lab, std::wstring &desc)
{
    lab.clear(); desc.clear();
    if (it.kind == SI_SEC || it.kind == SI_BREAK) return false;
    if (it.kind == SI_GFX) { int idx = SetGfxIdx(it.key); if (idx < 0) return false; const GfxRow &r = GfxRows()[idx]; lab = it.fr ? T(it.fr, it.en) : T(r.fr, r.en); }
    else if (it.kind == SI_OPT) { const Opt *o = OptByKey(it.key); if (!o) return false; lab = it.fr ? T(it.fr, it.en) : T(o->fr, o->en); }
    else { if (!it.fr || !strcmp(it.key, "footer")) return false; lab = T(it.fr, it.en); }
    if (it.dFr && (it.kind != SI_X || strcmp(it.key, "footer"))) desc = T(it.dFr, it.dEn);
    return true;
}
// Resultats de la recherche : une seule carte, les lignes trouvees sous le nom de leur onglet et de leur carte.
static float SetDrawSearch(Graphics &g, RectF a, float sc)
{
    Glass(a);
    std::wstring q = SetFold(g_setSearch);
    float x = a.X + 28, w = a.Width - 56, y = a.Y + 14 - sc;
    int n = 0, lastCard = -1;
    for (int i = 0; i < kSetItemN; i++) {
        const SetItem &it = kSetItems[i];
        std::wstring lab, desc;
        if (!SetItemLabel(it, lab, desc)) continue;
        if (SetFold(lab).find(q) == std::wstring::npos && SetFold(desc).find(q) == std::wstring::npos) continue;
        if (it.card != lastCard) {
            if (n) y += 10;
            lastCard = it.card;
            const SetCard &c = kSetCards[it.card];
            RectF hr(x, y, w, 28);
            if (SetDrawn(hr)) {
                std::wstring tn = T(kSetTabFr[c.tab], kSetTabEn[c.tab]);
                float tw = MeasureW(MeasureG(), tn, 11, FontStyleBold);
                Text(g, tn, RectF(x, y + 4, tw + 4, 20), 11, FontStyleBold, kAcc, StringAlignmentNear);
                Text(g, std::wstring(L"· ") + T(c.fr, c.en), RectF(x + tw + 10, y + 4, w - tw - 10, 20), 11, FontStyleBold, kGrey, StringAlignmentNear);
            }
            y += 28;
        }
        y += SetDrawItem(g, it, x, y, w);
        n++;
    }
    if (!n) Text(g, T(L"Aucun réglage ne correspond.", L"No setting matches."), RectF(a.X, a.Y + 40, a.Width, 30), 14, FontStyleRegular, kGrey);
    g_setFound = n;
    return y + 14;
}

// Apercu du HUD (onglet HUD) : petite scene d'hiver, et le style 'style' dessine comme en jeu (taille, position, couleur, fond,
// jauges choisies si 'big' ; tuile de style sinon : taille fixe, place du style).
static void HudPreview(Graphics &g, RectF box, int style, bool big)
{
    GraphicsPath p; RoundRect(p, box, big ? 16.0f : 10.0f);
    GraphicsState st = g.Save();
    g.SetClip(&p, CombineModeIntersect);
    LinearGradientBrush sky(RectF(box.X, box.Y - 1, box.Width, box.Height + 2), Color(255, 66, 92, 104), Color(255, 120, 132, 150), LinearGradientModeVertical);
    g.FillPath(&sky, &p);
    SolidBrush tree(Color(255, 46, 58, 60));
    for (int i = 0; i < 9; i++) {
        float tx = box.X + box.Width * (0.04f + i * 0.12f), th = min(box.Height, 110.0f) * (0.24f + 0.12f * ((i * 37) % 5) / 4.0f), base = box.Y + box.Height * 0.66f;
        PointF tri[3] = { PointF(tx, base - th), PointF(tx - th * 0.32f, base), PointF(tx + th * 0.32f, base) };
        g.FillPolygon(&tree, tri, 3);
    }
    SolidBrush snow(Color(255, 156, 158, 184)); g.FillRectangle(&snow, RectF(box.X, box.Y + box.Height * 0.66f, box.Width, box.Height * 0.34f + 1));
    static const Color acc[4] = { Color(255, 166, 216, 255), Color(255, 120, 189, 255), Color(255, 72, 240, 176), Color(255, 201, 166, 255) };
    Color ac = acc[big ? max(0, min(3, GfxGet("Couleur"))) : 0];
    float k = big ? GfxGet("Taille") / 100.0f : 0.55f;
    float sc = big ? box.Height / 600.0f * 1.6f * k : box.Height / 300.0f * 1.15f;
    int alpha = big ? (int)(255 * GfxGet("Fond") / 100.0f) : 170;
    Color bg((BYTE)alpha, 8, 18, 34);
    static const float vals[6] = { 35, 76, 12, 48, 93, 4 };
    static const char *gk[6] = { "JaugeSoif", "JaugeFaim", "JaugeStress", "JaugeUrine", "JaugeFatigue", "JaugeSueur" };
    auto tone = [&](float v) { return v >= 90 ? Color(255, 255, 107, 122) : v >= 70 ? Color(255, 255, 181, 92) : ac; };
    std::vector<float> shown;
    for (int i = 0; i < 6; i++) if (!big || GfxGet(gk[i]) == 1) shown.push_back(vals[i]);
    int pos = big ? GfxGet("Position") : -1;
    if (pos < 0) pos = style == 2 ? 2 : style == 3 ? 4 : 0;
    float m = 10 * sc;
    auto place = [&](float w, float h) {
        float px = (pos == 1 || pos == 3) ? box.X + box.Width - w - m : (pos == 4 || pos == 5) ? box.X + (box.Width - w) / 2 : box.X + m;
        float py = (pos == 2 || pos == 3 || pos == 5) ? box.Y + box.Height - h - m : box.Y + m;
        return RectF(px, py, w, h);
    };
    SolidBrush track(Color(30, 255, 255, 255));
    if (style == 0) {   // Vanilla : libelles jaunes en italique, barres blanches sur fond noir
        static const wchar_t *lab[6] = { L"THIRST", L"HUNGER", L"STRESS", L"URINE", L"FATIGUE", L"SWEAT" };
        float vs = big ? sc * 1.7f : sc, lh = 11 * vs * 1.3f, bx = box.X + box.Width * 0.24f;
        for (int i = 0; i < 6; i++) {
            float yy = box.Y + m + i * lh;
            Text(g, lab[i], RectF(box.X, yy, bx - box.X - 3, lh), max(5.0f, 7.5f * vs), FontStyleBold | FontStyleItalic, Color(255, 232, 226, 27), StringAlignmentFar);
            SolidBrush blk(Color(255, 0, 0, 0)), wh(Color(255, 255, 255, 255));
            g.FillRectangle(&blk, RectF(bx, yy + lh * 0.18f, 46 * vs, lh * 0.64f));
            g.FillRectangle(&wh, RectF(bx, yy + lh * 0.18f, 46 * vs * vals[i] / 100, lh * 0.64f));
        }
    } else if (style == 1) {   // Carte
        float w = 150 * sc, rowh = 13 * sc, h = 22 * sc + rowh * (shown.size() + 1) + 12 * sc;
        RectF r = place(w, h);
        GraphicsPath cp; RoundRect(cp, r, 9 * sc);
        SolidBrush b(bg); g.FillPath(&b, &cp);
        Pen pe(Color(46, 204, 227, 255), 1.0f); g.DrawPath(&pe, &cp);
        Text(g, T(L"Lundi", L"Monday"), RectF(r.X + 7 * sc, r.Y + 5 * sc, 70 * sc, 14 * sc), max(5.0f, 8 * sc), FontStyleBold, Color(255, 240, 245, 252), StringAlignmentNear);
        Text(g, L"2 140 mk", RectF(r.X + w - 62 * sc, r.Y + 5 * sc, 55 * sc, 14 * sc), max(5.0f, 7.5f * sc), FontStyleBold, ac, StringAlignmentFar);
        float yy = r.Y + 24 * sc;
        for (float v : shown) {
            GraphicsPath tp; RoundRect(tp, RectF(r.X + 50 * sc, yy + 4 * sc, w - 58 * sc, 4 * sc), 2 * sc); g.FillPath(&track, &tp);
            GraphicsPath fp; RoundRect(fp, RectF(r.X + 50 * sc, yy + 4 * sc, max(4 * sc, (w - 58 * sc) * v / 100), 4 * sc), 2 * sc);
            SolidBrush fb(tone(v)); g.FillPath(&fb, &fp);
            SolidBrush lb(Color(200, 220, 230, 242)); g.FillRectangle(&lb, RectF(r.X + 8 * sc, yy + 4 * sc, 32 * sc, 4 * sc));
            yy += rowh;
        }
        LinearGradientBrush tg(RectF(r.X + 50 * sc, yy + 4 * sc, w - 58 * sc, 4 * sc), Color(255, 90, 169, 255), Color(255, 255, 181, 92), LinearGradientModeHorizontal);
        g.FillRectangle(&tg, RectF(r.X + 50 * sc, yy + 4 * sc, w - 58 * sc, 4 * sc));
    } else if (style == 2) {   // Anneaux
        float ring = 22 * sc, cell = 27 * sc, w = 52 * sc + cell * (shown.size() + 1), h = ring + 16 * sc;
        RectF r = place(w, h);
        GraphicsPath cp; RoundRect(cp, r, 11 * sc);
        SolidBrush b(bg); g.FillPath(&b, &cp);
        Pen pe(Color(46, 204, 227, 255), 1.0f); g.DrawPath(&pe, &cp);
        Text(g, T(L"Lundi", L"Monday"), RectF(r.X + 6 * sc, r.Y + 5 * sc, 44 * sc, 12 * sc), max(5.0f, 7 * sc), FontStyleBold, Color(255, 240, 245, 252), StringAlignmentNear);
        Text(g, L"2 140", RectF(r.X + 6 * sc, r.Y + 17 * sc, 44 * sc, 14 * sc), max(5.0f, 9 * sc), FontStyleBold, ac, StringAlignmentNear);
        float xx = r.X + 52 * sc, cy = r.Y + h / 2;
        for (size_t i = 0; i <= shown.size(); i++) {
            float v = i < shown.size() ? shown[i] : 30;
            Color c = i < shown.size() ? tone(v) : Color(255, 143, 201, 255);
            RectF rr(xx + (cell - ring) / 2 + 2 * sc, cy - ring / 2 + 2 * sc, ring - 4 * sc, ring - 4 * sc);
            Pen tp(Color(30, 255, 255, 255), 3 * sc); g.DrawEllipse(&tp, rr);
            Pen fp(c, 3 * sc); fp.SetStartCap(LineCapRound); fp.SetEndCap(LineCapRound); g.DrawArc(&fp, rr, -90, 360 * max(0.04f, v / 100));
            xx += cell;
        }
    } else {   // Bandeau
        float ch = 14 * sc, cw = 34 * sc, w = 8 * sc + 40 * sc + 46 * sc + (cw + 3 * sc) * (shown.size() + 1), h = ch + 8 * sc;
        int keep = pos; if (pos == 0 || pos == 1) pos = 4; else if (pos == 2 || pos == 3) pos = 5;
        RectF r = place(w, h);
        if (keep == 0 || keep == 2) r.X = box.X + m; else if (keep == 1 || keep == 3) r.X = box.X + box.Width - w - m;
        GraphicsPath cp; RoundRect(cp, r, h / 2);
        SolidBrush b(bg); g.FillPath(&b, &cp);
        Pen pe(Color(46, 204, 227, 255), 1.0f); g.DrawPath(&pe, &cp);
        float xx = r.X + 4 * sc, cy = r.Y + 4 * sc;
        Text(g, T(L"Lundi", L"Monday"), RectF(xx, cy, 40 * sc, ch), max(5.0f, 7 * sc), FontStyleBold, Color(255, 240, 245, 252)); xx += 40 * sc + 3 * sc;
        GraphicsPath mp; RoundRect(mp, RectF(xx, cy, 43 * sc, ch), ch / 2); SolidBrush mb(WithA(ac, 0.18f)); g.FillPath(&mb, &mp);
        Text(g, L"2 140", RectF(xx, cy, 43 * sc, ch), max(5.0f, 7 * sc), FontStyleBold, ac); xx += 46 * sc;
        for (size_t i = 0; i <= shown.size(); i++) {
            float v = i < shown.size() ? shown[i] : 30;
            Color c = i < shown.size() ? tone(v) : Color(255, 143, 201, 255);
            GraphicsPath cpp; RoundRect(cpp, RectF(xx, cy, cw, ch), ch / 2); SolidBrush cb(v >= 70 ? WithA(c, 0.2f) : Color(14, 255, 255, 255)); g.FillPath(&cb, &cpp);
            GraphicsPath fp; RoundRect(fp, RectF(xx + 6 * sc, cy + ch / 2 - 1.5f * sc, max(3 * sc, (cw - 12 * sc) * v / 100), 3 * sc), 1.5f * sc); SolidBrush fb(c); g.FillPath(&fb, &fp);
            xx += cw + 3 * sc;
        }
    }
    g.Restore(st);
    Pen bp(TH(panelBorder), 1.0f); g.DrawPath(&bp, &p);
}

static void DrawSettingsPage(Graphics &g)
{
    if (GetTickCount() - g_gfxReadT > 2000) GfxLoad();   // (le jeu peut les avoir changes : son menu, F10)
    // pastilles des onglets
    float x = kMain.X;
    for (int t = 0; t < SET_N; t++) {
        std::wstring l = T(kSetTabFr[t], kSetTabEn[t]);
        float w = MeasureW(MeasureG(), l, 13, FontStyleBold) + 30;
        RectF r(x, 92, w, 36);
        bool on = g_setSearch.empty() && t == g_setTab, hot = g_uiHot == UI_SET_TAB + t;
        GraphicsPath p; RoundRect(p, r, 18);
        SolidBrush b(on ? kAcc : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
        if (!on) { Pen pen(TH(fieldBorder), 1.0f); g.DrawPath(&pen, &p); }
        Text(g, l, r, 13, FontStyleBold, on ? kOnAcc : kInk);
        HotZone(r, UI_SET_TAB + t);
        x += w + 6;
    }
    RectF a = kSetArea;
    g_setTop = a.Y; g_setBottom = a.Y + a.Height;
    float sc = g_scroll[g_tab] = min(max(g_scroll[g_tab], 0.0f), GfxMaxScroll());
    GraphicsState st = g.Save();
    g.SetClip(RectF(a.X - 8, a.Y - 1, a.Width + 16, a.Height + 2), CombineModeIntersect);
    float bottom;
    if (!g_setSearch.empty()) bottom = SetDrawSearch(g, a, sc);
    else {
        float lw = g_setTab == SET_THEME ? 612.0f : (a.Width - 12) / 2;
        float cx[2] = { a.X, a.X + lw + 12 }, cw[2] = { lw, a.Width - 12 - lw }, cy[2] = { a.Y - sc, a.Y - sc };
        for (int ci = 0; ci < kSetCardN; ci++) {
            const SetCard &c = kSetCards[ci];
            if (c.tab != g_setTab) continue;
            cy[c.col] += SetDrawCard(g, ci, cx[c.col], cy[c.col], cw[c.col]) + 12;
        }
        bottom = max(cy[0], cy[1]) - 12;
    }
    g.Restore(st);
    g_setContentH = bottom + sc - a.Y;
    float ms = GfxMaxScroll();
    if (ms > 0) {   // ascenseur
        float view = a.Height, h = view * view / (view + ms), yy = a.Y + (view - h) * sc / ms;
        GraphicsPath sp; RoundRect(sp, RectF(a.X + a.Width - 4, yy, 5, h), 2.5f);
        SolidBrush sb(WithA(kAcc, 0.55f)); g.FillPath(&sb, &sp);
    }
}
// Sous-titre de l'en-tete (selon l'onglet, ou le nombre de lignes trouvees).
static std::wstring SetHeaderSub()
{
    if (!g_setSearch.empty()) { wchar_t b[96]; swprintf_s(b, T(L"%d réglages trouvés · Échap : effacer", L"%d settings found · Esc: clear"), g_setFound); return b; }
    static const wchar_t *fr[SET_N] = { L"Durables, pour toutes tes parties · « Jeu » = la valeur du jeu lui-même", L"Lumière, ombres, ray tracing · en jeu : F10", L"Jauges en jeu : Vanilla, Carte, Anneaux ou Bandeau · en jeu : F10 › HUD", L"Vocal de proximité : qui t'entend, à quel volume",
                                        L"Ce que tu proposes en hébergeant, et ce que tu veux chez toi", L"Touches de MWCoop · celles du jeu : dans le jeu (Options > Contrôles)", L"Thème, couleur d'accent, aurore et neige, verre, densité, texte", L"Lanceur : langue, version, dossier du jeu, mises à jour, journaux" };
    static const wchar_t *en[SET_N] = { L"Lasting, for all your games · \"Game\" = the game's own value", L"Light, shadows, ray tracing · in game: F10", L"In-game gauges: Vanilla, Card, Rings or Strip · in game: F10 › HUD", L"Proximity voice: who hears you, how loud",
                                        L"What you offer when hosting, and what you want on your side", L"MWCoop keys · the game's own: in the game (Options > Controls)", L"Theme, accent colour, aurora and snow, glass, density, text", L"Launcher: language, version, game folder, updates, logs" };
    return T(fr[g_setTab], en[g_setTab]);
}
// Champ Chercher de l'en-tete (a droite du titre) ; 'right' recule d'autant.
static void DrawSetSearchBox(Graphics &g, float &right)
{
    RectF r(right - 250, 22, 250, 44);
    GraphicsPath p; RoundRect(p, r, 14);
    SolidBrush fill(TH(field)); g.FillPath(&fill, &p);
    Pen pen(g_setFocus ? kAcc : TH(fieldBorder), g_setFocus ? 2.0f : 1.2f); g.DrawPath(&pen, &p);
    Pen lp(kGrey, 2.0f);
    g.DrawEllipse(&lp, r.X + 14, r.Y + 13, 13.0f, 13.0f);
    g.DrawLine(&lp, r.X + 25, r.Y + 24, r.X + 31, r.Y + 30);
    RectF tr(r.X + 38, r.Y, r.Width - 38 - 34, r.Height);
    if (g_setSearch.empty() && !g_setFocus) Text(g, T(L"Chercher un réglage…", L"Search a setting…"), tr, 13, FontStyleRegular, TH(placeholder), StringAlignmentNear);
    else {
        Text(g, g_setSearch, tr, 14, FontStyleBold, kInk, StringAlignmentNear);
        if (g_setFocus && fmodf(g_time, 1.0f) < 0.55f) {
            float cx = min(tr.X + MeasureW(MeasureG(), g_setSearch, 14, FontStyleBold) + 1, tr.X + tr.Width);
            Pen cp(kAcc, 1.6f); g.DrawLine(&cp, cx, r.Y + 12, cx, r.Y + r.Height - 12);
        }
    }
    HotZone(r, UI_SET_SEARCH);
    if (!g_setSearch.empty()) {   // (zone declaree apres : elle gagne)
        RectF xr(r.X + r.Width - 34, r.Y + 8, 28, 28);
        Icon(g, IC_CLOSE, xr.X + 14, xr.Y + 14, 14, g_uiHot == UI_SET_SEARCHX ? kAcc : kGrey, 2.0f);
        HotZone(xr, UI_SET_SEARCHX);
    }
    right -= 260;
}
// Caractere tape dans le champ Chercher (WM_CHAR) ; vrai si pris.
static bool SetChar(wchar_t c)
{
    if (!g_setFocus || g_state != ST_IDLE || (g_tab != TAB_GFX && g_tab != TAB_COOP)) return false;
    if (c == 27) { g_setSearch.clear(); g_setFocus = false; }
    else if (c == 8) { if (!g_setSearch.empty()) g_setSearch.pop_back(); }
    else if (c >= 32 && g_setSearch.size() < 40) g_setSearch += c;
    else return true;
    g_scroll[g_tab] = 0;
    g_time = 0.2f;
    return true;
}
// Arrivee sur la page (barre de gauche : Jeu ou Co-op), visite guidee, capture.
static void SetEnter(int tab) { g_setTab = tab == TAB_COOP ? SET_COOP : SET_JEU; g_setSearch.clear(); g_setFocus = false; }
// La barre de gauche suit l'onglet : Jeu / Image / Son = page Jeu (TAB_GFX), le reste = Co-op (TAB_COOP).
static void SetRail() { int t = g_setTab <= SET_SON ? TAB_GFX : TAB_COOP; if (g_tab == TAB_GFX || g_tab == TAB_COOP) g_tab = t; }
static void SetTutoTab(int step) { g_setTab = step == 12 || step == 17 || step == 18 || step == 21 ? SET_LANCEUR : SET_JEU; g_setSearch.clear(); }   // (12 : API des moddeurs, carte Depannage)
static void SetCaptureState(const std::wstring &st)
{
    static const wchar_t *names[SET_N] = { L"jeu", L"image", L"hud", L"son", L"coop", L"touches", L"theme", L"lanceur" };
    std::wstring s = st.size() > 9 ? st.substr(9) : L"";   // (apres "reglages-")
    for (int i = 0; i < SET_N; i++) if (s == names[i]) g_setTab = i;
    SetRail();
    if (s == L"recherche") { g_setSearch = L"ombre"; g_setFocus = true; }
    if (s == L"coop-test") { g_setTab = SET_COOP; g_netTest = T(L"UDP 7870 libre · TCP 7870 libre · pare-feu ok · Steam connecté", L"UDP 7870 free · TCP 7870 free · firewall ok · Steam connected"); g_netTestKind = K_OK; }
}

// Par defaut : les lignes de l'onglet courant reviennent a leur valeur d'origine (reglages du jeu : cle retiree ou valeur de
// la table ; touches : celles du mod ; options du lanceur : leur defaut ; cles [Lanceur] : retirees).
static void SetTabDefaults(int tab)
{
    for (int i = 0; i < kSetItemN; i++) {
        const SetItem &it = kSetItems[i];
        if (kSetCards[it.card].tab != tab) continue;
        if (it.kind == SI_GFX) {
            int idx = SetGfxIdx(it.key);
            if (idx < 0) continue;
            const GfxRow &r = GfxRows()[idx];
            if (r.sec && !strcmp(r.sec, "Touches")) { int k = GameKeyIndex(r.key); if (k >= 0) WritePrivateProfileStringA("Touches", kGameKeys[k].ini, NULL, ModIniA().c_str()); }
            else GfxSet(r.key, r.def);
        } else if (it.kind == SI_OPT) { if (const Opt *o = OptByKey(it.key)) if (o->kind != O_ACTION) OptSet(*o, o->def); }
        else if (it.kind == SI_X) {
            const char *k = it.key;
            if (!strcmp(k, "max")) WritePrivateProfileStringW(L"Lanceur", L"SalonMax", NULL, g_iniLauncher.c_str());
            else if (!strcmp(k, "prive")) WritePrivateProfileStringW(L"Lanceur", L"SalonPrive", NULL, g_iniLauncher.c_str());
            else if (!strcmp(k, "accent")) ThemeSetAccent(0);
            else if (!strcmp(k, "aurore")) ThemeSetAurore(2);
            else if (!strcmp(k, "verre")) ThemeSetGlass(true);
            else if (!strcmp(k, "densite")) ThemeSetDensite(1);
            else if (!strcmp(k, "texte")) ThemeSetTexte(100);
            else if (!strcmp(k, "winfollow")) ThemeFollowWindows(true);
            else if (!strcmp(k, "majauto")) WritePrivateProfileStringW(L"Lanceur", L"MajAuto", NULL, g_iniLauncher.c_str());
            else if (!strcmp(k, "nightly")) WritePrivateProfileStringW(L"Lanceur", L"Nightly", NULL, g_iniLauncher.c_str());
            else if (!strcmp(k, "sessions")) WritePrivateProfileStringW(L"Lanceur", L"Sessions", NULL, g_iniLauncher.c_str());
            else if (!strcmp(k, "discordask")) WritePrivateProfileStringW(L"Lanceur", L"DiscordPropose", NULL, g_iniLauncher.c_str());
            else if (!strcmp(k, "compat")) WritePrivateProfileStringW(L"Lanceur", L"RetirerCompat", NULL, g_iniLauncher.c_str());
            else if (!strcmp(k, "debug")) { g_debug = false; WritePrivateProfileStringW(L"Lanceur", L"Debug", NULL, g_iniLauncher.c_str()); }
            else if (!strcmp(k, "start")) WritePrivateProfileStringW(L"Lanceur", L"SansCopie", NULL, g_iniLauncher.c_str());
        }
    }
    GfxLoad();
    LaunchLog("reglages : onglet %d remis par defaut", tab);
    SetStatus(K_OK, T(L"Valeurs par défaut rétablies pour cet onglet", L"Default values restored for this tab"));
}

static bool GfxClick(int id)
{
    if (id >= UI_GFX_PRESET && id < UI_GFX_PRESET + (int)GfxPresets().size()) {
        for (auto &kv : GfxPresets()[id - UI_GFX_PRESET].v) GfxSet(kv.first, kv.second);
        return true;
    }
    const auto &rows = GfxRows();
    if (id >= UI_SETV && id < UI_SETV + (int)rows.size() * 10) {   // pastilles : la valeur (liste courte, comme a l'ecran)
        const GfxRow &r = rows[(id - UI_SETV) / 10];
        int i = (id - UI_SETV) % 10;
        const wchar_t *why = NULL;
        std::vector<int> vals; std::vector<std::wstring> labs;
        SetGfxVals(r, GfxGet(r.key), vals, labs);
        if (!GfxRowOff(r, &why) && i < (int)vals.size()) GfxSet(r.key, vals[i]);
        return true;
    }
    if (id >= UI_SET_FOV && id < UI_SET_FOV + 9) {   // curseur du champ de vision
        int idx = SetGfxIdx("ChampVision");
        if (idx >= 0 && id - UI_SET_FOV < (int)rows[idx].vals.size()) GfxSet("ChampVision", rows[idx].vals[id - UI_SET_FOV]);
        return true;
    }
    if (id < UI_GFX || id >= UI_GFX + (int)rows.size() * 3) return false;
    const GfxRow &r = rows[(id - UI_GFX) / 3];
    const wchar_t *why = NULL;
    if (r.sec && !strcmp(r.sec, "Touches")) { int k = GameKeyIndex(r.key); g_keyCapture = g_keyCapture == k ? -1 : k; return true; }
    if (GfxRowOff(r, &why)) return true;
    int part = (id - UI_GFX) % 3, v = GfxGet(r.key);
    if (r.vals.empty()) { if (part == 0) GfxSet(r.key, v == 1 ? 0 : 1); return true; }
    int k = 0;
    for (int i = 0; i < (int)r.vals.size(); i++) if (r.vals[i] == v) k = i;
    if (part == 1 && k > 0) GfxSet(r.key, r.vals[k - 1]);
    if (part == 2 && k + 1 < (int)r.vals.size()) GfxSet(r.key, r.vals[k + 1]);
    return true;
}
// Clic sur la page (zones UI_SET_* et celles des reglages) ; vrai si pris.
static bool SetClick(int id)
{
    if (id != UI_SET_SEARCH) g_setFocus = false;
    if (id >= UI_SET_TAB && id < UI_SET_TAB + SET_N) { g_setTab = id - UI_SET_TAB; SetRail(); g_setSearch.clear(); g_scroll[g_tab] = 0; return true; }
    switch (id) {
    case UI_SET_SEARCH: g_setFocus = true; g_time = 0.2f; return true;
    case UI_SET_SEARCHX: g_setSearch.clear(); return true;
    case UI_SET_WINTHEME: ThemeFollowWindows(!ThemeFollowsWindows()); return true;
    case UI_SET_THEMEDEF: SetTabDefaults(SET_THEME); return true;
    case UI_SET_DEFAULT: SetTabDefaults(g_setTab); return true;
    case UI_SET_SAVE:   // (tout s'enregistre a chaque changement : le bouton le confirme)
        SetStatus(K_OK, g_setTab == SET_THEME ? T(L"Appliqué ✓ · le thème est déjà en place", L"Applied ✓ · the theme is already in place") : T(L"Enregistré ✓ · chaque changement est gardé aussitôt", L"Saved ✓ · every change is kept right away"));
        return true;
    case UI_SET_NETTEST: NetTest(); return true;
    case UI_SET_HOSTGUIDE: WikiOpenHost(); return true;
    case UI_SET_REPAIR: RepairMWCoop(); return true;
    case UI_SET_NIGHTLY: WritePrivateProfileStringW(L"Lanceur", L"Nightly", NightlyOn() ? NULL : L"1", g_iniLauncher.c_str()); if (!g_busy) StartUpdate(false); return true;
    case UI_SET_GLASS: ThemeSetGlass(!g_glass); return true;
    case UI_SET_RTTOGGLE: if (GfxDxr()) GfxSet("RayTracing", GfxGet("RayTracing") == 1 ? 0 : 1); return true;
    case UI_SET_PRIV: case UI_SET_PRIV + 1: WritePrivateProfileStringW(L"Lanceur", L"SalonPrive", id == UI_SET_PRIV ? L"0" : L"1", g_iniLauncher.c_str()); return true;
    case UI_SET_NOTES: GoPage(TAB_NOTES); return true;
    case UI_SET_API: GoPage(TAB_API); return true;
    case UI_SET_GITHUB: ShellExecuteW(g_wnd, L"open", L"https://github.com/Parricidium/MWCoop", NULL, NULL, SW_SHOWNORMAL); return true;
    case UI_SET_LOGDIR: LogsOpenFolder(); return true;
    case UI_SET_LOGZIP: LogsZip(); return true;
    case UI_SET_DISCORDASK: {
        bool off = GetPrivateProfileIntW(L"Lanceur", L"DiscordPropose", 0, g_iniLauncher.c_str()) != 0;
        WritePrivateProfileStringW(L"Lanceur", L"DiscordPropose", off ? NULL : L"1", g_iniLauncher.c_str());
        return true;
    }
    case UI_SET_SKIN: GoPage(TAB_SKIN); return true;
    case UI_SET_CAR: GoPage(TAB_CAR); return true;
    case UI_SET_CONTENT: GoPage(TAB_CONTENT); return true;
    case UI_SET_STEAMLINE: SteamGuideOpen(); return true;
    case UI_SET_GOIMAGE: g_setTab = SET_IMAGE; SetRail(); g_setSearch.clear(); g_scroll[g_tab] = 0; return true;
    }
    if (id >= UI_SET_THEMECARD && id < UI_SET_THEMECARD + THEME_N) { ThemeSet(id - UI_SET_THEMECARD); return true; }
    if (id >= UI_SET_HUDSTYLE && id < UI_SET_HUDSTYLE + 4) { GfxSet("Style", id - UI_SET_HUDSTYLE); return true; }
    if (id >= UI_SET_ACCENT && id < UI_SET_ACCENT + 5) { ThemeSetAccent(id - UI_SET_ACCENT); return true; }
    if (id >= UI_SET_AURORE && id < UI_SET_AURORE + 3) { ThemeSetAurore(id - UI_SET_AURORE); return true; }
    if (id >= UI_SET_DENS && id < UI_SET_DENS + 3) { ThemeSetDensite(id - UI_SET_DENS); return true; }
    if (id >= UI_SET_TEXTE && id < UI_SET_TEXTE + 7) { ThemeSetTexte(80 + 5 * (id - UI_SET_TEXTE)); return true; }
    if (id >= UI_SET_SESS && id < UI_SET_SESS + 3) { static const int n[] = { 3, 10, 30 }; WritePrivateProfileStringW(L"Lanceur", L"Sessions", n[id - UI_SET_SESS] == 30 ? NULL : std::to_wstring(n[id - UI_SET_SESS]).c_str(), g_iniLauncher.c_str()); return true; }
    if (id >= UI_SET_PORTSEG && id < UI_SET_PORTSEG + 8) { if (const Opt *o = OptByKey("Port")) if (id - UI_SET_PORTSEG < (int)o->vals.size()) OptSet(*o, o->vals[id - UI_SET_PORTSEG]); return true; }
    if (id >= UI_SET_MAXP && id < UI_SET_MAXP + 4) { static const int m[] = { 4, 8, 12, 16 }; WritePrivateProfileStringW(L"Lanceur", L"SalonMax", std::to_wstring(m[id - UI_SET_MAXP]).c_str(), g_iniLauncher.c_str()); return true; }
    if (id >= UI_SET_AJOUTS && id < UI_SET_AJOUTS + 3) {
        int k = id - UI_SET_AJOUTS;
        GfxSet("SansAjouts", k == 2 ? 1 : 0);
        if (k == 0) for (const char *a : kAjoutKeys) GfxSet(a, 1);
        return true;
    }
    if (id >= UI_SET_AJOUT && id < UI_SET_AJOUT + kAjoutN) { const char *a = kAjoutKeys[id - UI_SET_AJOUT]; GfxSet(a, GfxGet(a) == 1 ? 0 : 1); return true; }   // (5 identifiants au plus : UI_SET_DRONEKB suit)
    if (id >= UI_SET_DRONEKB && id < UI_SET_DRONEKB + 3) { DroneLayoutSet(id - UI_SET_DRONEKB); return true; }
    return GfxClick(id);
}
