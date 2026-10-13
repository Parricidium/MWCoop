// ---------------------------------------------------------------- page GRAPHISMES - inclus par launcher.cpp
// Demande de JD (07/10), comme les GTA : les reglages graphiques dans le lanceur (et en jeu, menu F10). Ecrits dans
// [Graphismes] du mwcoop.ini du jeu (MWCoop\Gfx.cs les applique au chargement, et tout de suite si le jeu tourne) ;
// cle absente : le jeu decide ("Jeu" pour les reglages du moteur). Options du jeu (son menu) et reglages du moteur en
// plus ; prereglages Performance / Equilibre / Beau / Ultra en un clic.
// ---- touches en jeu (Options du jeu > Touches) : section [Touches] de mwcoop.ini, noms des KeyCode de Unity
struct GameKey { const char *ini, *def; const wchar_t *fr, *en; const char *defAz = nullptr; };   // defAz : clavier AZERTY
static const GameKey kGameKeys[] = { { "Tchat", "T", L"Tchat", L"Chat" }, { "PorteMonnaie", "N", L"Argent", L"Wallet" }, { "Vocal", "V", L"Vocal", L"Voice" }, { "Triches", "F7", L"Triches", L"Cheats" }, { "Carte", "M", L"Carte", L"Map" }, { "Vue", "F5", L"Vue", L"View" },
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
        // Un seul interrupteur pour tout ce que MWCoop ajoute au jeu (demande de Menor, 12/10 : « un jeu 100 % vanilla » ;
        // JD : un bouton pour tous les ajouts, presents et a venir -- Sync/Ajouts.cs).
        { "SansAjouts", L"Tout désactiver (jeu vanilla)", L"Disable them all (vanilla game)", 0, {}, {}, {}, 0, 15, "Coop" },
        // (et un interrupteur par ajout -- JD, 13/10 : garder le drone, retirer la boutique et le musee...)
        { "AjoutDrone", L"Caméra drone (F8)", L"Drone camera (F8)", 1, {}, {}, {}, 0, 15, "Coop" },
        { "AjoutSacs", L"Sacs de couchage", L"Sleeping bags", 1, {}, {}, {}, 0, 15, "Coop" },
        { "AjoutBoutique", L"Boutique s21 (vidéo, KARTTURI)", L"s21 shop (video, KARTTURI)", 1, {}, {}, {}, 0, 15, "Coop" },
        { "AjoutMusee", L"Musée de la Corse", L"Museum of Corsica", 1, {}, {}, {}, 0, 15, "Coop" },
        { "Touche:Tchat", L"Écrire aux autres (tchat)", L"Write to the others (chat)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:PorteMonnaie", L"Porte-monnaie (donner de l'argent)", L"Wallet (give money)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Vocal", L"Parler (vocal, touche tenue)", L"Talk (voice, hold the key)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Triches", L"Panneau des triches", L"Cheat panel", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Carte", L"Carte (joueurs en direct)", L"Map (live players)", 0, {}, {}, {}, 0, 9, "Touches" },
        { "Touche:Vue", L"Vue à la 3e personne (à pied, en véhicule)", L"Third-person view (on foot, in vehicles)", 0, {}, {}, {}, 0, 9, "Touches" },
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
            { "OmbresPhares", hshad }, { "OmbresLampadaires", sshad }, { "Halos", halos }, { "RayTracing", 0 } };
    };
    // Vanilla (demande de JD, 11/10) : chaque option a la valeur du jeu (cle retiree pour les reglages du moteur), sans
    // les ajouts de MWCoop ; RT : Ultra + ombres du soleil en ray tracing (carte d'ombres d'Unity au plus court : elle ne
    // sert plus que de support), ombres de la lune laissees au ray tracing.
    std::vector<std::pair<const char *, int>> van = mk(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    for (auto &kv : van) for (const GfxRow &r : GfxRows()) if (!strcmp(r.key, kv.first)) kv.second = r.def;
    std::vector<std::pair<const char *, int>> rt = mk(1, 1, 1, 1, 0, 1, 30, 1, 9000, 300, 0, 2, 2, 1, 1, 4, 2, 2);
    rt.back().second = 1;   // (RayTracing)
    std::vector<std::pair<const char *, int>> ultra = mk(1, 1, 1, 1, 1, 1, 400, 4, 9000, 300, 0, 2, 2, 1, 1, 4, 2, 2);
    p = {
        { L"Vanilla", L"Vanilla", L"Comme le jeu", L"Like the game", van },
        { L"Performance", L"Performance", L"Plus d'images", L"More frames", mk(0, 0, 0, 0, 0, 0, 30, 1, 1500, 70, 1, 0, 1, 0, 0, 0, 0, 0) },
        { L"Équilibré", L"Balanced", L"Le bon compromis", L"The sweet spot", mk(1, 1, 0, 0, 0, 1, 60, 2, 3000, 100, 0, 1, 1, 1, 0, 0, 0, 1) },
        { L"Beau", L"Pretty", L"Plus joli", L"Better looking", mk(1, 1, 1, 1, 1, 1, 150, 2, 4500, 150, 0, 1, 2, 1, 1, 2, 1, 1) },
        { L"Ultra", L"Ultra", L"Tout au max", L"Everything maxed", ultra },
        { L"RT", L"RT", L"Ray tracing", L"Ray tracing", rt },
    };
    return p;
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
    if (r.group == 14 && strcmp(r.key, "RayTracing") && !rt) { *why = T(L"RT coupé", L"RT off"); return true; }
    if (rt && (!strcmp(r.key, "OmbresDistance") || !strcmp(r.key, "OmbresCascades") || !strcmp(r.key, "OmbresLune"))) { *why = T(L"ray tracing", L"ray tracing"); return true; }
    if (rt && GfxGet("RTLampes") != 0 && (!strcmp(r.key, "OmbresPhares") || !strcmp(r.key, "OmbresLampadaires"))) { *why = T(L"ray tracing", L"ray tracing"); return true; }
    return false;
}
static bool GfxPresetOff(int i) { return GfxPresets()[i].fr[0] == L'R' && GfxPresets()[i].fr[1] == L'T' && !GfxDxr(); }
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
// ascenseur quand tout ne tient pas. Rangees en trois blocs titres (demande de JD, 11/10) : la coop, les options video du
// jeu lui-meme (son menu Options), puis celles que MWCoop ajoute ; une icone par categorie. Valeurs negatives : titre de
// bloc. Deplies : [Lanceur] GfxOuverts4 (lettres a.. : numeros de groupe ; aucune par defaut, demande de JD 12/10 --
// nouvelle cle : chacun repart ferme).
static const int kGfxGroups = 16;
static const int kGfxOrder[] = { -1, 6, 7, 8, 15, 9, 13, -2, 0, 1, 2, 3, -3, 5, 10, 14, 11, 12, 4 };
static bool g_gfxOpen[kGfxGroups] = {};   // (toutes fermees par defaut : demande de JD, 12/10)
static bool g_gfxOpenRead;
static float g_gfxContentH;
static bool g_gfxZones = true;   // (ligne hors de la zone visible : pas de zone cliquable)
static void GfxOpenRead()
{
    if (g_gfxOpenRead) return;
    g_gfxOpenRead = true;
    wchar_t b[64] = {};
    GetPrivateProfileStringW(L"Lanceur", L"GfxOuverts4", L"*", b, 64, g_iniLauncher.c_str());
    if (!wcscmp(b, L"*")) return;
    for (int i = 0; i < kGfxGroups; i++) g_gfxOpen[i] = wcschr(b, L'a' + i) != NULL;
}
static void GfxToggleGroup(int gi)
{
    if (gi < 0 || gi >= kGfxGroups) return;
    g_gfxOpen[gi] = !g_gfxOpen[gi];
    std::wstring s;
    for (int i = 0; i < kGfxGroups; i++) if (g_gfxOpen[i]) s += (wchar_t)(L'a' + i);
    WritePrivateProfileStringW(L"Lanceur", L"GfxOuverts4", s.empty() ? L"-" : s.c_str(), g_iniLauncher.c_str());
}
static float GfxMaxScroll() { return max(0.0f, g_gfxContentH - (kGfxArea.Height - 20)); }

static void DrawGfxRow(Graphics &g, const GfxRow &r, int idx, RectF row)
{
    int id = UI_GFX + idx * 3;
    bool hot = g_uiHot == id || g_uiHot == id + 1 || g_uiHot == id + 2;
    if (hot) { GraphicsPath p; RoundRect(p, RectF(row.X - 8, row.Y, row.Width + 16, row.Height), 10); SolidBrush hb(TH(card)); g.FillPath(&hb, &p); }
    const wchar_t *lab = T(r.fr, r.en);
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
    const wchar_t *why = NULL;
    if (GfxRowOff(r, &why)) {   // (sans effet : valeur remplacee par la raison, pas de clic)
        RectF val(row.X + row.Width - 172, row.Y + 2, 172, row.Height - 4);
        GraphicsPath vp; RoundRect(vp, val, 10);
        SolidBrush vb(WithA(TH(btn2), 0.5f)); g.FillPath(&vb, &vp);
        Text(g, why, val, 12, FontStyleItalic, kGrey);
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
    Text(g, T(r.lf[k], r.le[k]), RectF(lft.X + lft.Width, val.Y, val.Width - 60, val.Height), 12.5f, FontStyleBold, game ? kGrey : kInk);
    if (!g_gfxZones) return;
    HotZone(row, UI_NONE);
    if (canL) HotZone(lft, id + 1);
    if (canR) HotZone(rgt, id + 2);
}

// Une categorie : en-tete (chevron, nom, nombre de reglages), puis ses reglages sur deux colonnes s'il est deplie.
// 'y' : position dans le contenu (avant defilement) ; dessine seulement ce qui croise la zone visible.
// Titre d'un bloc (pas repliable) : nom, ce qu'il contient, filet.
static const float kGfxBlockH = 52;
static void DrawGfxBlock(Graphics &g, int block, float x, float &y, float w, float top, float bottom)
{
    static const wchar_t *tFr[] = { L"Coop", L"Options vidéo de My Winter Car", L"Options vidéo améliorées MWCoop" };
    static const wchar_t *tEn[] = { L"Co-op", L"My Winter Car video options", L"MWCoop enhanced video options" };
    static const wchar_t *dFr[] = { L"Voix, joueurs, partage et touches du mod", L"Les réglages du jeu lui-même (son menu Options)", L"Ajoutés par MWCoop : écran, fluidité, ombres, détails, lumières" };
    static const wchar_t *dEn[] = { L"Voice, players, sharing and the mod's keys", L"The game's own settings (its Options menu)", L"Added by MWCoop: screen, smoothness, shadows, detail, lights" };
    float sy = top + y - g_scroll[TAB_GFX] + (y > 0 ? 8 : 0);
    if (sy + kGfxBlockH > top && sy < bottom) {
        Title(g, T(tFr[block], tEn[block]), RectF(x + 2, sy + 4, w - 4, 24), 15.5f, kAcc);
        Text(g, T(dFr[block], dEn[block]), RectF(x + 2, sy + 26, w - 4, 18), 12, FontStyleRegular, kGrey, StringAlignmentNear);
        Pen ln(WithA(kAcc, 0.35f), 1.0f);
        g.DrawLine(&ln, x + 2, sy + kGfxBlockH - 4, x + w - 2, sy + kGfxBlockH - 4);
    }
    y += kGfxBlockH + (y > 0 ? 8 : 0);
}

static void DrawGfxGroup(Graphics &g, int group, float x, float &y, float w, float top, float bottom)
{
    static const wchar_t *kFr[] = { L"Image", L"Ombres", L"Distance et vue", L"Confort", L"Lumières", L"Écran", L"Voix", L"Visages, habits et pseudos", L"Partie et partage", L"Touches",
                                    L"Fluidité", L"Ombres avancées", L"Détails et textures", L"Caméra drone", L"Ray tracing", L"Ajouts COOP" };
    static const wchar_t *kEn[] = { L"Image", L"Shadows", L"Distance and view", L"Comfort", L"Lights", L"Screen", L"Voice", L"Faces, clothes and names", L"Game and sharing", L"Keys",
                                    L"Smoothness", L"Advanced shadows", L"Detail and textures", L"Drone camera", L"Ray tracing", L"CO-OP additions" };
    static const int kIcon[] = { IC_IMAGE, IC_MOON, IC_EYE, IC_SLIDERS, IC_BULB, IC_MONITOR, IC_MIC, IC_SHIRT, IC_USERS, IC_KEYB, IC_GAUGE, IC_SUN, IC_LAYERS, IC_DRONE, IC_SPARK, IC_BOX };
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
        // icone de la categorie dans une pastille (demande de JD, 11/10)
        RectF ib(hr.X + 40, hr.Y + (hr.Height - 26) / 2, 26, 26);
        GraphicsPath ip; RoundRect(ip, ib, 8);
        SolidBrush ibb(WithA(kAcc, hot ? 0.28f : 0.16f)); g.FillPath(&ibb, &ip);
        Icon(g, kIcon[group], ib.X + 13, ib.Y + 13, 17, kAcc, 2.0f);
        Text(g, T(kFr[group], kEn[group]), RectF(hr.X + 76, hr.Y, 340, hr.Height), 14, FontStyleBold, kInk, StringAlignmentNear);
        if (group == 14) {   // (demande de JD, 12/10 : etiquette tant que le ray tracing est en chantier)
            Graphics &mg = MeasureG();
            float nw = MeasureW(mg, T(kFr[group], kEn[group]), 14, FontStyleBold);
            const wchar_t *tag = T(L"EN DÉVELOPPEMENT", L"IN DEVELOPMENT");
            Pill(g, RectF(hr.X + 76 + nw + 12, hr.Y + (hr.Height - 20) / 2, PillW(tag, 10.0f), 20), WithA(kWarn, 0.18f), tag, kWarn, 10.0f);
        }
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
         RectF(t.X + 24, t.Y + 40, 190, 40), 12, kGrey);
    const auto &ps = GfxPresets();
    float tw = 120, gap = 8, x0 = t.X + t.Width - 20 - ((int)ps.size() * tw + ((int)ps.size() - 1) * gap);
    for (int i = 0; i < (int)ps.size(); i++) {
        RectF r(x0 + i * (tw + gap), t.Y + 12, tw, t.Height - 24);
        bool sel = i == on, hot = g_uiHot == UI_GFX_PRESET + i, off = GfxPresetOff(i);
        GraphicsPath p; RoundRect(p, r, 14);
        SolidBrush fb(sel ? kAcc : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&fb, &p);
        if (!sel) { Pen pen(TH(fieldBorder), 1.0f); g.DrawPath(&pen, &p); }
        Color ink = off ? kGrey : sel ? kOnAcc : kInk;
        Text(g, T(ps[i].fr, ps[i].en), RectF(r.X + 12, r.Y + 7, r.Width - 34, 22), 12.5f, FontStyleBold, ink, StringAlignmentNear);
        Text(g, off ? T(L"Carte non compatible", L"GPU not supported") : g_fr ? ps[i].dFr : ps[i].dEn, RectF(r.X + 12, r.Y + 29, r.Width - 16, 18), 11, FontStyleRegular, sel ? WithA(kOnAcc, 0.8f) : kGrey, StringAlignmentNear);
        // barres de niveau (Vanilla aucune, Performance 1 ... Ultra et RT 4)
        int lvl = min(i, 4);
        for (int b = 0; b <= 3; b++) {
            RectF br(r.X + r.Width - 12 - (3 - b) * 6.0f, r.Y + 19 - b * 4.0f, 4.0f, 6 + b * 4.0f);
            SolidBrush bb(b < lvl ? (sel ? kOnAcc : off ? kGrey : kAcc) : WithA(ink, 0.18f)); g.FillRectangle(&bb, br);
        }
        if (!off) HotZone(r, UI_GFX_PRESET + i);
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
    for (int gi : kGfxOrder) { if (gi < 0) DrawGfxBlock(g, -gi - 1, a.X + 20, y, a.Width - 48, top, bottom); else DrawGfxGroup(g, gi, a.X + 20, y, a.Width - 48, top, bottom); }
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
        if (GfxPresetOff(id - UI_GFX_PRESET)) return true;
        for (auto &kv : GfxPresets()[id - UI_GFX_PRESET].v) GfxSet(kv.first, kv.second);
        if (GfxPresets()[id - UI_GFX_PRESET].v.back().first != std::string("RayTracing")) GfxSet("RayTracing", 0);   // (autres prereglages : RT coupe)
        return true;
    }
    const auto &rows = GfxRows();
    if (id < UI_GFX || id >= UI_GFX + (int)rows.size() * 3) return false;
    const GfxRow &r = rows[(id - UI_GFX) / 3];
    const wchar_t *why = NULL;
    if (GfxRowOff(r, &why)) return true;
    if (r.sec && !strcmp(r.sec, "Touches")) { int k = GameKeyIndex(r.key); g_keyCapture = g_keyCapture == k ? -1 : k; return true; }
    int part = (id - UI_GFX) % 3, v = GfxGet(r.key);
    if (r.vals.empty()) { if (part == 0) GfxSet(r.key, v == 1 ? 0 : 1); return true; }
    int k = 0;
    for (int i = 0; i < (int)r.vals.size(); i++) if (r.vals[i] == v) k = i;
    if (part == 1 && k > 0) GfxSet(r.key, r.vals[k - 1]);
    if (part == 2 && k + 1 < (int)r.vals.size()) GfxSet(r.key, r.vals[k + 1]);
    return true;
}
