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
        { "ModeEcran", L"Affichage", L"Display mode", -1, { -1, 0, 1, 2 }, { L"Jeu", L"Fenêtré", L"Plein écran", L"Sans bordure" }, { L"Game", L"Windowed", L"Fullscreen", L"Borderless" }, 0, 5 },
        { "QualiteUnity", L"Qualité du moteur", L"Engine quality", -1, { -1, 0, 1, 3, 5 }, { L"Jeu", L"Basse", L"Moyenne", L"Bonne", L"Golden Eye" }, { L"Game", L"Low", L"Medium", L"Good", L"Golden Eye" }, 3, 5 },
    });
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
    static const wchar_t *df[] = { L"vieux PC", L"60 i/s", L"ombres, HDR", L"ray tracing" }, *de[] = { L"old PC", L"60 fps", L"shadows, HDR", L"ray tracing" };
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
// onglet, colonne gauche ou droite) et kSetItems (lignes d'une carte : reglage du jeu, option du lanceur, titre de section,
// ou element special dessine a part). Le champ Chercher de l'en-tete filtre ces memes lignes, toutes cartes confondues.
enum { SET_JEU, SET_IMAGE, SET_SON, SET_COOP, SET_TOUCHES, SET_THEME, SET_LANCEUR, SET_N };
static const wchar_t *kSetTabFr[SET_N] = { L"Jeu", L"Image & ray tracing", L"Son & vocal", L"Co-op", L"Touches", L"Thème", L"Lanceur" };
static const wchar_t *kSetTabEn[SET_N] = { L"Game", L"Image & ray tracing", L"Sound & voice", L"Co-op", L"Keys", L"Theme", L"Launcher" };
static int g_setTab = SET_JEU;
static std::wstring g_setSearch;        // champ Chercher (vide : les onglets)
static bool g_setFocus;                 // champ Chercher actif (curseur)
static int g_setFound;                  // lignes trouvees (sous-titre de l'en-tete)
static float g_setContentH;             // hauteur du contenu (molette)
static float g_setTop, g_setBottom;     // zone visible : zones cliquables seulement dedans
static RectF g_setSteamRow, g_setCompatRow, g_setBuy, g_setUninst;   // (bulles du guide : tuto.inc)
static const RectF kSetArea(264, 136, 1000, 608);                   // sous les pastilles (y 92..128)
static const RectF kGfxTop(264, 136, 494, 128), kGfxArea(264, 136, 1000, 608);   // (bulles du guide 30-32 : prereglages, onglet Jeu)
static const float kSetRowH = 36, kSetRowDH = 46, kSetSecH = 26, kSetPad = 18;
enum { UI_SET_TAB = 2900, UI_SET_SEARCH = 2910, UI_SET_SEARCHX, UI_SET_THEMECARD = 2920, UI_SET_ACCENT = 2925, UI_SET_AURORE = 2931, UI_SET_WINTHEME = 2935, UI_SET_THEMEDEF,
       UI_SET_MAXP = 2940, UI_SET_PRIV = 2945, UI_SET_AJOUTS = 2948, UI_SET_AJOUT = 2951, UI_SET_DRONEKB = 2956,
       UI_SET_NOTES = 2960, UI_SET_LOGDIR, UI_SET_LOGZIP, UI_SET_DISCORDASK, UI_SET_SKIN, UI_SET_CAR, UI_SET_CONTENT, UI_SET_STEAMLINE, UI_SET_GOIMAGE,
       UI_SETV = 12000 };   // + ligne * 8 + valeur (choix a segments)
static void LogsOpenFolder();
static void LogsZip();
static float GfxMaxScroll() { return max(0.0f, g_setContentH - kSetArea.Height); }

enum { SI_GFX, SI_OPT, SI_SEC, SI_BREAK, SI_X };   // reglage du jeu (cle de GfxRows), option du lanceur (cle de g_opts), section, 2e sous-colonne, special (cle = nom)
struct SetCard { int tab, col; const wchar_t *fr, *en, *sFr, *sEn; };
static const SetCard kSetCards[] = {
    { SET_JEU, 0, L"Préréglage", L"Preset", L"Règle tout d'un coup ; chaque ligne reste modifiable", L"Sets everything at once; every line stays editable" },        // 0
    { SET_JEU, 0, L"Affichage", L"Display", NULL, NULL },                                                                                                           // 1
    { SET_JEU, 1, L"Confort & affichage en jeu", L"Comfort & in-game display", NULL, NULL },                                                                         // 2
    { SET_JEU, 1, L"Lumière & ombres", L"Light & shadows", NULL, NULL },                                                                                            // 3
    { SET_IMAGE, 0, L"Image", L"Image", NULL, NULL },                                                                                                               // 4
    { SET_IMAGE, 0, L"Ombres", L"Shadows", NULL, NULL },                                                                                                            // 5
    { SET_IMAGE, 1, L"Ray tracing", L"Ray tracing", L"Carte DXR 1.1 · en développement", L"DXR 1.1 GPU · in development" },                                         // 6
    { SET_SON, 0, L"Vocal de proximité", L"Proximity voice", L"Qui t'entend, et à quel volume", L"Who hears you, and how loud" },                                   // 7
    { SET_SON, 1, L"Comment ça marche", L"How it works", NULL, NULL },                                                                                              // 8
    { SET_COOP, 0, L"Quand j'héberge", L"When I host", L"Défauts de mes salons · modifiables dans chaque salon", L"My lobby defaults · editable in each lobby" },     // 9
    { SET_COOP, 1, L"Chez moi", L"On my side", L"Hôte ou invité", L"Host or guest" },                                                                                // 10
    { SET_COOP, 1, L"Réseau & lancement", L"Network & launch", NULL, NULL },                                                                                        // 11
    { SET_TOUCHES, 0, L"En jeu", L"In game", L"Clique sur une touche, puis appuie sur la nouvelle · Échap : annuler · Suppr : par défaut", L"Click a key, then press the new one · Esc: cancel · Del: default" },   // 12
    { SET_TOUCHES, 1, L"Caméra drone", L"Drone camera", L"Mode photo", L"Photo mode" },                                                                             // 13
    { SET_THEME, 0, L"Thème du lanceur", L"Launcher theme", NULL, NULL },                                                                                           // 14
    { SET_THEME, 0, L"Personnaliser", L"Customize", NULL, NULL },                                                                                                   // 15
    { SET_THEME, 1, L"Aperçu", L"Preview", NULL, NULL },                                                                                                            // 16
    { SET_LANCEUR, 0, L"Langue", L"Language", L"Lanceur et menu F10 en jeu ; les textes du jeu suivent sa langue à lui", L"Launcher and in-game F10 menu; the game's own texts follow its language" },   // 17
    { SET_LANCEUR, 0, L"Version & mises à jour", L"Version & updates", NULL, NULL },                                                                                // 18
    { SET_LANCEUR, 1, L"Jeu", L"Game", NULL, NULL },                                                                                                                // 19
    { SET_LANCEUR, 1, L"Dépannage", L"Troubleshooting", NULL, NULL },                                                                                               // 20
};
static const int kSetCardN = (int)(sizeof(kSetCards) / sizeof(kSetCards[0]));
// fr/en : nom (SI_GFX / SI_OPT : remplace celui de la table ; SI_X : pour la recherche) ; dFr/dEn : explication grise dessous.
struct SetItem { int card, kind; const char *key; const wchar_t *fr, *en, *dFr, *dEn; int wide; };   // wide : toute la largeur, avant les sous-colonnes
static const SetItem kSetItems[] = {
    { 0, SI_X, "presets", L"Préréglage", L"Preset", NULL, NULL },
    { 1, SI_GFX, "Resolution", NULL, NULL, NULL, NULL }, { 1, SI_GFX, "ModeEcran", NULL, NULL, NULL, NULL }, { 1, SI_GFX, "QualiteUnity", NULL, NULL, NULL, NULL },
    { 1, SI_GFX, "ImagesMax", NULL, NULL, NULL, NULL }, { 1, SI_GFX, "SyncVerticale", NULL, NULL, NULL, NULL },
    { 1, SI_GFX, "Distance", NULL, NULL, NULL, NULL }, { 1, SI_GFX, "Details", NULL, NULL, NULL, NULL },
    { 1, SI_GFX, "Textures", NULL, NULL, L"Pleines = plus de mémoire vidéo", L"Full = more video memory" }, { 1, SI_GFX, "Filtrage", NULL, NULL, NULL, NULL }, { 1, SI_GFX, "Anticrenelage", NULL, NULL, NULL, NULL },
    { 2, SI_GFX, "Balancement", NULL, NULL, NULL, NULL }, { 2, SI_GFX, "BalancementVoiture", NULL, NULL, NULL, NULL }, { 2, SI_GFX, "Retroviseurs", NULL, NULL, L"Complets = coûteux", L"Full = costly" },
    { 2, SI_GFX, "ChampVision", NULL, NULL, L"60° = le jeu", L"60° = the game" },
    { 2, SI_GFX, "IndicateurRapport", NULL, NULL, NULL, NULL }, { 2, SI_GFX, "CompteurImages", NULL, NULL, NULL, NULL },
    { 2, SI_GFX, "Pseudos", NULL, NULL, L"Distance d'affichage", L"Display distance" }, { 2, SI_GFX, "PoussiereTrafic", NULL, NULL, NULL, NULL }, { 2, SI_GFX, "TracesTrafic", NULL, NULL, NULL, NULL },
    { 3, SI_X, "rtsummary", NULL, NULL, NULL, NULL },
    { 4, SI_GFX, "HDR", NULL, NULL, NULL, NULL }, { 4, SI_GFX, "Bloom", NULL, NULL, NULL, NULL }, { 4, SI_GFX, "Contraste", NULL, NULL, NULL, NULL }, { 4, SI_GFX, "RayonsSoleil", NULL, NULL, NULL, NULL },
    { 4, SI_GFX, "Halos", NULL, NULL, NULL, NULL }, { 4, SI_GFX, "PorteePhares", NULL, NULL, NULL, NULL },
    { 5, SI_GFX, "OmbresSoleil", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresLune", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresMaisons", NULL, NULL, NULL, NULL },
    { 5, SI_GFX, "OmbresDistance", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresCascades", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresPhares", NULL, NULL, NULL, NULL }, { 5, SI_GFX, "OmbresLampadaires", NULL, NULL, NULL, NULL },
    { 6, SI_GFX, "RayTracing", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTRayons", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTPortee", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTDouceur", NULL, NULL, NULL, NULL },
    { 6, SI_GFX, "RTCorps", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTReflets", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTVitres", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTRebond", NULL, NULL, NULL, NULL },
    { 6, SI_GFX, "RTOcclusion", NULL, NULL, NULL, NULL }, { 6, SI_GFX, "RTLampes", NULL, NULL, NULL, NULL },
    { 7, SI_GFX, "Vocal", NULL, NULL, NULL, NULL }, { 7, SI_GFX, "VocalPortee", NULL, NULL, NULL, NULL }, { 7, SI_GFX, "VolumeVocal", NULL, NULL, L"Ce que tu entends", L"What you hear" },
    { 7, SI_GFX, "GainMicro", L"Mon micro (pour les autres)", L"My microphone (for the others)", NULL, NULL },
    { 8, SI_X, "voicehelp", NULL, NULL, NULL, NULL },
    { 9, SI_SEC, NULL, L"Règles", L"Rules", NULL, NULL },
    { 9, SI_GFX, "ArgentDepart", NULL, NULL, L"Chaque invité démarre avec le pécule du jeu", L"Every guest starts with the game's nest egg" },
    { 9, SI_GFX, "Triches", L"Autoriser les triches (F7)", L"Allow cheats (F7)", NULL, NULL },
    { 9, SI_GFX, "Renverser", L"Les voitures renversent les joueurs", L"Cars run players over", L"Amis et PNJ", L"Friends and NPCs" },
    { 9, SI_GFX, "PvP", L"Coups de poing entre joueurs", L"Punches between players", NULL, NULL },
    { 9, SI_SEC, NULL, L"Jeux & ajouts", L"Games & additions", NULL, NULL },
    { 9, SI_GFX, "VenttiEnjeux", NULL, NULL, NULL, NULL },
    { 9, SI_GFX, "Jokkis", L"Course sur glace : Jokkis à louer (bêta)", L"Ice race: Jokkis rentals (beta)", NULL, NULL },
    { 9, SI_X, "ajouts", L"Ajouts MWCoop", L"MWCoop additions", L"Aucun = jeu vanilla", L"None = vanilla game" },
    { 9, SI_X, "ajoutchips", NULL, NULL, NULL, NULL },
    { 9, SI_SEC, NULL, L"Salon", L"Lobby", NULL, NULL },
    { 9, SI_X, "max", L"Joueurs au plus", L"Players at most", NULL, NULL },
    { 9, SI_X, "prive", L"Salon par défaut", L"Default lobby", L"Privé : code de session demandé", L"Private: session code required" },
    { 10, SI_GFX, "ModsAuto", NULL, NULL, L"Mods et contenu, sans redemander", L"Mods and content, without asking again" },
    { 10, SI_GFX, "VoirVisages", NULL, NULL, L"Visages, peintures, posters", L"Faces, paints, posters" },
    { 10, SI_GFX, "Marqueurs", NULL, NULL, NULL, NULL },
    { 10, SI_OPT, "ContenuEnvoye", L"Mon contenu envoyé", L"My content sent", NULL, NULL },
    { 10, SI_OPT, "Apparence", L"Mon personnage", L"My character", NULL, NULL },
    { 10, SI_OPT, "CouleurVoiture", NULL, NULL, NULL, NULL },
    { 11, SI_X, "net", L"Réseau par défaut", L"Default network", L"Steam : amis ; IP / VPN : port à ouvrir", L"Steam: friends; IP / VPN: port to open" },
    { 11, SI_OPT, "Port", L"Port (IP / VPN)", L"Port (IP / VPN)", L"UDP et TCP ; à rediriger sur la box", L"UDP and TCP; to forward on the router" },
    { 11, SI_OPT, "FermerLanceur", L"Fermer le lanceur pendant le jeu", L"Close the launcher during the game", L"Sinon il reste ouvert : joueurs, ping", L"Otherwise it stays open: players, ping" },
    { 11, SI_X, "steamline", L"Option de lancement Steam", L"Steam launch option", L"Pour que JOUER ouvre MWCoop (facultatif)", L"So that PLAY opens MWCoop (optional)" },
    { 12, SI_SEC, NULL, L"Avec les autres", L"With the others", NULL, NULL },
    { 12, SI_GFX, "Touche:Tchat", NULL, NULL, NULL, NULL }, { 12, SI_GFX, "Touche:Vocal", NULL, NULL, NULL, NULL }, { 12, SI_GFX, "Touche:PorteMonnaie", NULL, NULL, NULL, NULL }, { 12, SI_GFX, "Touche:Marqueur", NULL, NULL, NULL, NULL },
    { 12, SI_SEC, NULL, L"Voir", L"See", NULL, NULL },
    { 12, SI_GFX, "Touche:Carte", NULL, NULL, NULL, NULL }, { 12, SI_GFX, "CarteMaintenir", NULL, NULL, NULL, NULL }, { 12, SI_GFX, "Touche:Vue", NULL, NULL, NULL, NULL }, { 12, SI_GFX, "Touche:Triches", NULL, NULL, NULL, NULL },
    { 13, SI_GFX, "Touche:Drone", NULL, NULL, NULL, NULL, 1 },
    { 13, SI_X, "dronekb", L"Disposition des déplacements", L"Movement layout", NULL, NULL, 1 },
    { 13, SI_SEC, NULL, L"Déplacer", L"Move", NULL, NULL },
    { 13, SI_GFX, "Touche:DroneAvant", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneArriere", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneGauche", NULL, NULL, NULL, NULL },
    { 13, SI_GFX, "Touche:DroneDroite", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneMonter", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneDescendre", NULL, NULL, NULL, NULL },
    { 13, SI_GFX, "Touche:DroneRapide", L"Plus vite", L"Faster", L"Touche tenue", L"Hold the key" },
    { 13, SI_BREAK, NULL, NULL, NULL, NULL, NULL },
    { 13, SI_SEC, NULL, L"Cadrer", L"Frame", NULL, NULL },
    { 13, SI_GFX, "Touche:DroneZoomPlus", L"Zoomer", L"Zoom in", L"Aussi la molette", L"Also the wheel" }, { 13, SI_GFX, "Touche:DroneZoomMoins", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DronePencherG", NULL, NULL, NULL, NULL },
    { 13, SI_GFX, "Touche:DronePencherD", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DroneRecentrer", L"Recentrer", L"Reset", L"Penché et zoom", L"Tilt and zoom" }, { 13, SI_GFX, "Touche:DroneGrille", NULL, NULL, NULL, NULL },
    { 13, SI_GFX, "Touche:DroneInterface", NULL, NULL, NULL, NULL }, { 13, SI_GFX, "Touche:DronePhoto", L"Photo", L"Photo", L"Rangée dans l'album", L"Stored in the album" },
    { 14, SI_X, "themes", L"Thème", L"Theme", NULL, NULL },
    { 15, SI_X, "accent", L"Couleur d'accent", L"Accent colour", L"Boutons, onglet actif, pastilles", L"Buttons, active tab, pills" },
    { 15, SI_X, "aurore", L"Aurore et neige", L"Aurora and snow", L"Fond animé derrière les pages", L"Animated background behind the pages" },
    { 15, SI_X, "soon", L"Verre dépoli", L"Frosted glass", L"Transparence des cartes", L"Card transparency" },
    { 15, SI_X, "soon", L"Densité", L"Density", L"Espace entre les lignes et les cartes", L"Space between rows and cards" },
    { 15, SI_X, "soon", L"Taille du texte", L"Text size", NULL, NULL },
    { 15, SI_X, "winfollow", L"Suivre Windows", L"Follow Windows", L"Thème clair ou sombre de Windows", L"Windows' light or dark theme" },
    { 16, SI_X, "apercu", NULL, NULL, NULL, NULL },
    { 17, SI_X, "lang", L"Langue", L"Language", NULL, NULL },
    { 18, SI_X, "update", L"Mise à jour", L"Update", NULL, NULL },
    { 18, SI_X, "canal", L"Version jouée", L"Version played", L"Legacy : l'ancienne, figée en 0.95.8", L"Legacy: the old one, frozen at 0.95.8" },
    { 18, SI_X, "majauto", L"Mettre à jour automatiquement", L"Update automatically", L"Au démarrage du lanceur, depuis GitHub", L"At launcher start, from GitHub" },
    { 18, SI_X, "soon", L"Versions d'essai (nightly)", L"Test builds (nightly)", L"Avant tout le monde, parfois cassées", L"Before everyone, sometimes broken" },
    { 18, SI_X, "notes", L"Notes de version", L"Release notes", L"Ce qui change à chaque version", L"What changes in each version" },
    { 18, SI_OPT, "RevoirGuide", L"Bulles d'aide du lanceur", L"Launcher help bubbles", L"La visite en quelques bulles", L"The tour in a few bubbles" },
    { 19, SI_X, "folder", L"Dossier du jeu", L"Game folder", NULL, NULL },
    { 19, SI_X, "start", L"Départ du jeu", L"Game start", L"Si MWCoop ne se charge pas", L"If MWCoop doesn't load" },
    { 19, SI_X, "mscl", L"MSCLoader fourni par MWCoop", L"MSCLoader provided by MWCoop", L"Dans la copie de lancement, jamais dans ton dossier", L"In the launch copy, never in your folder" },
    { 19, SI_X, "about", NULL, NULL, NULL, NULL },
    { 19, SI_X, "buy", L"Acheter le jeu, désinstaller MWCoop", L"Buy the game, uninstall MWCoop", NULL, NULL },
    { 20, SI_X, "logs", L"Journaux", L"Logs", L"Un zip à poster sur le Discord", L"A zip to post on the Discord" },
    { 20, SI_X, "compat", L"Retirer les modes de compatibilité", L"Remove compatibility modes", L"Windows en ajoute après un plantage : MWCoop ne se charge plus", L"Windows adds some after a crash: MWCoop stops loading" },
    { 20, SI_X, "debug", L"Mode débogage", L"Debug mode", L"Journaux détaillés pour le zip", L"Detailed logs for the zip" },
    { 20, SI_X, "discordask", L"Proposer le Discord", L"Offer the Discord", L"L'invitation après la visite guidée", L"The invitation after the guided tour" },
};
static const int kSetItemN = (int)(sizeof(kSetItems) / sizeof(kSetItems[0]));

// ---- theme : couleur d'accent et intensite de l'aurore ([Lanceur] Accent=RRGGBB, Aurore=0|1|2). Les tables kDark / kLight
// sont modifiees en place (TH(accent) les lit a chaque dessin) ; la scene est refaite quand l'aurore change.
struct SetAccent { const wchar_t *fr, *en; Color dark, darkHi, light, lightHi; };
static const SetAccent kSetAccents[5] = {
    { L"Bleu glace", L"Ice blue", Color(255, 166, 225, 255), Color(255, 200, 236, 255), Color(255, 26, 98, 172), Color(255, 58, 132, 206) },
    { L"Menthe", L"Mint", Color(255, 154, 240, 192), Color(255, 196, 248, 220), Color(255, 16, 128, 88), Color(255, 40, 160, 120) },
    { L"Ambre", L"Amber", Color(255, 255, 210, 143), Color(255, 255, 228, 180), Color(255, 176, 104, 10), Color(255, 208, 136, 40) },
    { L"Rose", L"Pink", Color(255, 240, 166, 255), Color(255, 248, 200, 255), Color(255, 150, 40, 170), Color(255, 180, 80, 200) },
    { L"Corail", L"Coral", Color(255, 255, 143, 168), Color(255, 255, 186, 200), Color(255, 200, 40, 80), Color(255, 224, 80, 120) },
};
static int g_accentSel;   // (-1 : couleur ecrite a la main dans l'ini)
static void ThemeApplyAccent(Color dark, Color darkHi, Color light, Color lightHi) { kDark.accent = dark; kDark.accent2 = darkHi; kLight.accent = light; kLight.accent2 = lightHi; }
static void ThemeLoad()
{
    g_aurore = max(0, min(2, (int)GetPrivateProfileIntW(L"Lanceur", L"Aurore", 2, g_iniLauncher.c_str())));
    wchar_t b[16] = L"";
    GetPrivateProfileStringW(L"Lanceur", L"Accent", L"", b, 16, g_iniLauncher.c_str());
    g_accentSel = 0;
    if (wcslen(b) == 6) {
        unsigned v = (unsigned)wcstoul(b, NULL, 16);
        Color c(255, (BYTE)(v >> 16), (BYTE)(v >> 8), (BYTE)v);
        g_accentSel = -1;
        for (int i = 0; i < 5; i++) if (kSetAccents[i].dark.GetValue() == c.GetValue()) g_accentSel = i;
        if (g_accentSel < 0) {   // couleur libre : claire sur le theme sombre, assombrie sur le clair (texte blanc dessus)
            Color navy(255, 16, 30, 48);
            ThemeApplyAccent(c, Mix(c, Color(255, 255, 255, 255), 0.3f), Mix(c, navy, 0.55f), Mix(c, navy, 0.4f));
            return;
        }
    }
    const SetAccent &a = kSetAccents[g_accentSel];
    ThemeApplyAccent(a.dark, a.darkHi, a.light, a.lightHi);
}
static void ThemeSetAccent(int i)
{
    wchar_t hex[8];
    swprintf_s(hex, L"%02X%02X%02X", kSetAccents[i].dark.GetR(), kSetAccents[i].dark.GetG(), kSetAccents[i].dark.GetB());
    WritePrivateProfileStringW(L"Lanceur", L"Accent", i == 0 ? NULL : hex, g_iniLauncher.c_str());
    ThemeLoad();
}
static void ThemeSetAurore(int v)
{
    WritePrivateProfileStringW(L"Lanceur", L"Aurore", v == 2 ? NULL : std::to_wstring(v).c_str(), g_iniLauncher.c_str());
    g_aurore = v;
    g_sceneDarkK = -1;   // (scene refaite)
}
// Theme=clair|sombre absent : celui des applications de Windows (comme au premier lancement).
static bool ThemeFollowsWindows() { wchar_t th[16] = L""; GetPrivateProfileStringW(L"Lanceur", L"Theme", L"", th, 16, g_iniLauncher.c_str()); return !th[0]; }
static void ThemeFollowWindows(bool on)
{
    if (!on) { WritePrivateProfileStringW(L"Lanceur", L"Theme", g_dark ? L"sombre" : L"clair", g_iniLauncher.c_str()); return; }
    WritePrivateProfileStringW(L"Lanceur", L"Theme", NULL, g_iniLauncher.c_str());
    DWORD v = 1, sz = sizeof(v);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"AppsUseLightTheme", RRF_RT_REG_DWORD, NULL, &v, &sz) == ERROR_SUCCESS) g_dark = v == 0;
}

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
// Nom (gras) et explication (grise) d'une ligne, filet dessous ; renvoie la largeur du nom.
static float SetRowLabel(Graphics &g, RectF row, const std::wstring &t, const std::wstring &d, float maxW, float alpha = 1.0f)
{
    if (d.empty()) Text(g, t, RectF(row.X, row.Y, maxW, row.Height), 13.5f, FontStyleBold, WithA(kInk, alpha), StringAlignmentNear);
    else {
        Text(g, t, RectF(row.X, row.Y + 4, maxW, 20), 13.5f, FontStyleBold, WithA(kInk, alpha), StringAlignmentNear);
        Text(g, d, RectF(row.X, row.Y + 23, maxW, 18), 11.5f, FontStyleRegular, WithA(kGrey, alpha), StringAlignmentNear);
    }
    Pen ln(TH(sep), 1.0f); g.DrawLine(&ln, row.X, row.Y + row.Height - 0.5f, row.X + row.Width, row.Y + row.Height - 0.5f);
    return min(maxW, MeasureW(MeasureG(), t, 13.5f, FontStyleBold));
}
static void SetRowHover(Graphics &g, RectF row) { GraphicsPath p; RoundRect(p, RectF(row.X - 8, row.Y + 1, row.Width + 16, row.Height - 3), 10); SolidBrush hb(TH(card)); g.FillPath(&hb, &p); }
// Choix a segments de largeur selon le texte (UiSeg : largeurs egales) ; zones id0 + i.
static float SetSegW(const std::vector<std::wstring> &labels, float px) { float w = 6; for (auto &l : labels) w += MeasureW(MeasureG(), l, px, FontStyleBold) + 20; return w; }
static void SetSeg(Graphics &g, RectF r, const std::vector<std::wstring> &labels, int sel, int id0, float px = 12, bool enabled = true)
{
    GraphicsPath tp; RoundRect(tp, r, min(12.0f, r.Height / 2));
    SolidBrush tb(TH(field)); g.FillPath(&tb, &tp);
    Pen bp(TH(fieldBorder), 1.0f); g.DrawPath(&bp, &tp);
    float x = r.X + 3;
    for (int i = 0; i < (int)labels.size(); i++) {
        float w = MeasureW(MeasureG(), labels[i], px, FontStyleBold) + 20;
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
// Touche en jeu (capuchon) : en attente = contour accent.
static void SetKeyCap(Graphics &g, RectF r, const std::wstring &s, bool cap, bool hot)
{
    GraphicsPath p; RoundRect(p, r, 9);
    SolidBrush b(cap ? WithA(kAcc, 0.22f) : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&b, &p);
    Pen pen(cap ? kAcc : TH(fieldBorder), cap ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
    Pen under(WithA(kInk, 0.12f), 2.0f); g.DrawLine(&under, r.X + 8, r.Y + r.Height - 3, r.X + r.Width - 8, r.Y + r.Height - 3);
    Text(g, s, r, 12.5f, FontStyleBold, cap ? kAcc : kInk);
}
static void SetChip(Graphics &g, float rightX, float cy, const std::wstring &s, Color c, bool strong = false)
{
    float w = PillW(s, 10.5f) + 4;
    Pill(g, RectF(rightX - w, cy - 11, w, 22), WithA(c, strong ? 1.0f : 0.16f), s, strong ? kOnAcc : c, 10.5f);
}
// Bouton plat de ligne (hauteur 32) ; renvoie sa largeur.
static float SetButtonW(const std::wstring &s) { return MeasureW(MeasureG(), s, 12.5f, FontStyleBold) + 32; }

// Un reglage du jeu (ligne de GfxRows) : nom, pastille d'impact, puis sa commande a droite (interrupteur, choix a
// segments s'il tient, sinon ◀ valeur ▶ ; touche en jeu : capuchon ; ligne sans effet : la raison).
static void SetGfxRow(Graphics &g, const SetItem &it, int idx, RectF row)
{
    const GfxRow &r = GfxRows()[idx];
    int id = UI_GFX + idx * 3;
    bool hot = g_uiHot == id || g_uiHot == id + 1 || g_uiHot == id + 2 || (g_uiHot >= UI_SETV + idx * 8 && g_uiHot < UI_SETV + idx * 8 + 8);
    if (hot) SetRowHover(g, row);
    std::wstring lab = it.fr ? T(it.fr, it.en) : T(r.fr, r.en), desc = it.dFr ? T(it.dFr, it.dEn) : L"";
    const wchar_t *why = NULL;
    bool keyRow = r.sec && !strcmp(r.sec, "Touches"), off = !keyRow && GfxRowOff(r, &why);
    int kk = keyRow ? GameKeyIndex(r.key) : -1, v = keyRow ? 0 : GfxGet(r.key), k = 0;
    bool cap = kk >= 0 && g_keyCapture == kk;
    std::wstring keyText = cap ? std::wstring(T(L"… appuie", L"… press")) : kk >= 0 ? GameKeyLabel(GameKeyGet(kk)) : std::wstring(L"?");
    std::vector<std::wstring> labs;
    float wid = 44;
    bool seg = false;
    const wchar_t *il = r.impact >= 3 ? T(L"lourd", L"heavy") : T(L"moyen", L"medium");
    float pw = r.impact >= 2 ? PillW(il, 9.5f) + 10 : 0;
    if (keyRow) wid = max(44.0f, MeasureW(MeasureG(), keyText, 12.5f, FontStyleBold) + 24);
    else if (off) wid = 150;
    else if (!r.vals.empty()) {
        for (int i = 0; i < (int)r.vals.size(); i++) { labs.push_back(T(r.lf[i], r.le[i])); if (r.vals[i] == v) k = i; }
        float sw = SetSegW(labs, 12), lw = MeasureW(MeasureG(), lab, 13.5f, FontStyleBold) + pw;
        if (labs.size() <= 5 && sw <= row.Width - lw - 16) { seg = true; wid = sw; } else wid = 172;
    }
    float labW = SetRowLabel(g, row, lab, desc, row.Width - wid - 12);
    if (pw > 0 && labW + pw < row.Width - wid - 12) {   // impact sur les images : pastille apres le nom
        Color ic = r.impact >= 3 ? kRed : kWarn;
        Pill(g, RectF(row.X + labW + 10, row.Y + (desc.empty() ? (row.Height - 18) / 2 : 5), pw - 10, 18), WithA(ic, 0.16f), il, ic, 9.5f);
    }
    float cx = row.X + row.Width - wid, cy = row.Y + row.Height / 2;
    if (keyRow) { SetKeyCap(g, RectF(cx, cy - 15, wid, 30), keyText, cap, hot); HotZone(row, id); return; }
    if (off) {
        RectF val(cx, cy - 13, wid, 26);
        GraphicsPath vp; RoundRect(vp, val, 9);
        SolidBrush vb(WithA(TH(btn2), 0.5f)); g.FillPath(&vb, &vp);
        Text(g, why, val, 11.5f, FontStyleItalic, kGrey);
        return;
    }
    if (r.vals.empty()) { UiToggle(g, RectF(cx, cy - 12, 44, 24), v == 1, hot); HotZone(row, id); return; }
    if (seg) { SetSeg(g, RectF(cx, cy - 15, wid, 30), labs, k, UI_SETV + idx * 8, 12); return; }
    RectF val(cx, cy - 15, wid, 30), lft(val.X, val.Y, 30, val.Height), rgt(val.X + val.Width - 30, val.Y, 30, val.Height);
    GraphicsPath vp; RoundRect(vp, val, 10);
    SolidBrush vb(TH(btn2)); g.FillPath(&vb, &vp);
    Pen vpen(TH(fieldBorder), 1.0f); g.DrawPath(&vpen, &vp);
    bool canL = k > 0, canR = k + 1 < (int)r.vals.size();
    Icon(g, IC_CHEVL, lft.X + 15, lft.Y + lft.Height / 2, 14, WithA(g_uiHot == id + 1 ? kAcc : kInk, canL ? 1.0f : 0.25f), 2.2f);
    Icon(g, IC_CHEVR, rgt.X + 15, rgt.Y + rgt.Height / 2, 14, WithA(g_uiHot == id + 2 ? kAcc : kInk, canR ? 1.0f : 0.25f), 2.2f);
    Text(g, labs[k], RectF(lft.X + lft.Width, val.Y, val.Width - 60, val.Height), 12.5f, FontStyleBold, r.vals[k] == -1 ? kGrey : kInk);
    HotZone(row, UI_NONE);
    if (canL) HotZone(lft, id + 1);
    if (canR) HotZone(rgt, id + 2);
}
// Une option du lanceur (g_opts) : port (- / +), interrupteur, ou bouton vers la page (tenue, voiture, contenu, guide).
static void SetOptRow(Graphics &g, const SetItem &it, RectF row)
{
    const Opt *o = OptByKey(it.key);
    if (!o) return;
    std::wstring lab = it.fr ? T(it.fr, it.en) : T(o->fr, o->en), desc = it.dFr ? T(it.dFr, it.dEn) : L"", bt;
    bool port = !strcmp(it.key, "Port"), button = o->kind == O_ACTION;
    int id = -1;
    float wid = 44;
    if (port) wid = 136;
    else if (button) {
        id = !strcmp(it.key, "ContenuEnvoye") ? UI_SET_CONTENT : !strcmp(it.key, "Apparence") ? UI_SET_SKIN : !strcmp(it.key, "CouleurVoiture") ? UI_SET_CAR : UI_SET_GUIDE;
        bt = id == UI_SET_GUIDE ? T(L"Revoir", L"See again") : T(L"Choisir", L"Choose");
        wid = SetButtonW(bt);
        if (id == UI_SET_CONTENT) desc = ContentSummary();
        else if (id == UI_SET_SKIN) desc = TLab(o->labFr, o->labEn, (size_t)OptGet(*o));
        else if (id == UI_SET_CAR) desc = CarColorName();
    } else id = !strcmp(it.key, "FermerLanceur") ? UI_SET_CLOSE : UI_SET_MODSAUTO;
    bool hot = port ? g_uiHot == UI_SET_PORTM || g_uiHot == UI_SET_PORTP : g_uiHot == id;
    if (hot && !button) SetRowHover(g, row);
    SetRowLabel(g, row, lab, desc, row.Width - wid - 12);
    float cx = row.X + row.Width - wid, cy = row.Y + row.Height / 2;
    if (port) {
        RectF m(cx, cy - 15, 30, 30), p(cx + wid - 30, cy - 15, 30, 30);
        for (int k = 0; k < 2; k++) {
            RectF r = k ? p : m; int bid = k ? UI_SET_PORTP : UI_SET_PORTM;
            GraphicsPath bp; RoundRect(bp, r, 9);
            SolidBrush b(g_uiHot == bid ? TH(cardSel) : TH(btn2)); g.FillPath(&b, &bp);
            Pen pen(TH(fieldBorder), 1.0f); g.DrawPath(&pen, &bp);
            Icon(g, k ? IC_PLUS : IC_MIN, r.X + 15, r.Y + 15, 14, kInk, 2.2f);
            HotZone(r, bid);
        }
        Text(g, std::to_wstring(OptGet(*o)), RectF(m.X + m.Width, row.Y, p.X - m.X - m.Width, row.Height), 13.5f, FontStyleBold, kInk);
        return;
    }
    if (button) { UiButton(g, RectF(cx, cy - 16, wid, 32), bt, id, false); return; }
    UiToggle(g, RectF(cx, cy - 12, 44, 24), OptGet(*o) != 0, hot);
    HotZone(row, id);
}
// Interrupteur de ligne a part (cle du lanceur).
static void SetToggleRow(Graphics &g, const SetItem &it, RectF row, bool on, int id)
{
    bool hot = g_uiHot == id;
    if (hot) SetRowHover(g, row);
    SetRowLabel(g, row, T(it.fr, it.en), it.dFr ? T(it.dFr, it.dEn) : L"", row.Width - 56);
    UiToggle(g, RectF(row.X + row.Width - 44, row.Y + row.Height / 2 - 12, 44, 24), on, hot);
    HotZone(row, id);
}
static void SetSegRow(Graphics &g, const SetItem &it, RectF row, const std::vector<std::wstring> &labs, int sel, int id0, bool enabled = true)
{
    float sw = SetSegW(labs, 12);
    SetRowLabel(g, row, T(it.fr, it.en), it.dFr ? T(it.dFr, it.dEn) : L"", row.Width - sw - 12, enabled ? 1.0f : 0.55f);
    SetSeg(g, RectF(row.X + row.Width - sw, row.Y + row.Height / 2 - 15, sw, 30), labs, sel, id0, 12, enabled);
}
static void SetButtonRow(Graphics &g, const SetItem &it, RectF row, const std::wstring &bt, int id, bool primary = false, bool enabled = true)
{
    float bw = SetButtonW(bt);
    SetRowLabel(g, row, T(it.fr, it.en), it.dFr ? T(it.dFr, it.dEn) : L"", row.Width - bw - 12);
    UiButton(g, RectF(row.X + row.Width - bw, row.Y + row.Height / 2 - 16, bw, 32), bt, id, primary, -1, enabled);
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
static const char *kAjoutKeys[] = { "AjoutDrone", "AjoutSacs", "AjoutBoutique", "AjoutMusee" };   // (aussi le salon, launcher.cpp)
static const wchar_t *kAjoutFr[] = { L"Caméra drone (F8)", L"Sacs de couchage", L"Boutique s21", L"Musée de la Corse" }, *kAjoutEn[] = { L"Drone camera (F8)", L"Sleeping bags", L"s21 shop", L"Museum of Corsica" };
static float g_chipW = 458;
static int SetChipLines(float w)
{
    if (w > 0) g_chipW = w;
    float cx = 0; int lines = 1;
    for (int i = 0; i < 4; i++) {
        float cw = PillW(T(kAjoutFr[i], kAjoutEn[i]), 11) + 22;
        if (cx > 0 && cx + cw > g_chipW) { lines++; cx = 0; }
        cx += cw + 6;
    }
    return lines;
}
// Une ligne de carte ; renvoie sa hauteur (dessinee seulement si elle croise la zone visible ; zones cliquables
// retirees si elle n'est pas entierement visible).
static float SetItemH(const SetItem &it)
{
    if (it.kind == SI_SEC) return kSetSecH;
    if (it.kind == SI_GFX || it.kind == SI_OPT) return it.dFr || it.kind == SI_OPT ? kSetRowDH : kSetRowH;
    const char *k = it.key;
    if (!strcmp(k, "presets")) return 72;
    if (!strcmp(k, "rtsummary")) return 108;
    if (!strcmp(k, "voicehelp")) return 150;
    if (!strcmp(k, "ajoutchips")) return 8 + 32.0f * SetChipLines(0);
    if (!strcmp(k, "themes")) return 150;
    if (!strcmp(k, "apercu")) return 256;
    if (!strcmp(k, "lang")) return 72;
    if (!strcmp(k, "update")) return 68;
    if (!strcmp(k, "folder")) return 76;
    if (!strcmp(k, "about")) return 48;
    if (!strcmp(k, "buy")) return 50;
    return it.dFr ? kSetRowDH : kSetRowH;
}
static float SetDrawItem(Graphics &g, const SetItem &it, float x, float y, float w)
{
    float h = SetItemH(it);
    RectF row(x, y, w, h);
    if (!SetDrawn(row)) return h;
    size_t nz = g_zones.size();
    if (it.kind == SI_SEC) Text(g, T(it.fr, it.en), RectF(x, y + 6, w, 18), 10.5f, FontStyleBold, kAcc, StringAlignmentNear);
    else if (it.kind == SI_GFX) { int idx = SetGfxIdx(it.key); if (idx >= 0) SetGfxRow(g, it, idx, row); }
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
        } else if (!strcmp(k, "rtsummary")) {   // resume du ray tracing, lien vers l'onglet Image
            bool dxr = GfxDxr(), rt = dxr && GfxGet("RayTracing") == 1;
            RectF ib(x, y + 6, 38, 38);
            GraphicsPath ip; RoundRect(ip, ib, 11);
            SolidBrush ibb(WithA(kAcc, 0.18f)); g.FillPath(&ibb, &ip);
            Icon(g, IC_SPARK, ib.X + 19, ib.Y + 19, 20, kAcc, 2.0f);
            const wchar_t *tt = L"Ray tracing";
            float tw = MeasureW(MeasureG(), tt, 14, FontStyleBold);
            Text(g, tt, RectF(x + 50, y + 4, tw + 4, 22), 14, FontStyleBold, kInk, StringAlignmentNear);
            std::wstring chip = dxr ? (rt ? T(L"Activé", L"On") : T(L"Coupé", L"Off")) : T(L"Carte non compatible", L"GPU not supported");
            Color cc = dxr ? (rt ? kOk : kGrey) : kWarn;
            Pill(g, RectF(x + 58 + tw, y + 6, PillW(chip, 10) + 2, 20), WithA(cc, 0.18f), chip, cc, 10);
            std::wstring l2;
            if (rt) {
                wchar_t b[160];
                swprintf_s(b, T(L"%d rayons/pixel · portée %d m · %d lampes · reflets, rebond, occlusion", L"%d rays/pixel · %d m range · %d lamps · reflections, bounce, occlusion"), GfxGet("RTRayons"), GfxGet("RTPortee"), GfxGet("RTLampes"));
                l2 = b;
            } else l2 = T(L"Ombres du soleil et des lampes, reflets, rebond de lumière, occlusion", L"Sun and lamp shadows, reflections, light bounce, occlusion");
            Text(g, l2, RectF(x + 50, y + 26, w - 50, 18), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            UiButton(g, RectF(x, y + 60, w, 36), T(L"Tout l'onglet Image & ray tracing", L"The whole Image & ray tracing tab"), UI_SET_GOIMAGE, false, IC_CHEVR);
        } else if (!strcmp(k, "voicehelp")) {
            Para(g, T(L"Le vocal est de proximité : on t'entend jusqu'à la portée choisie, de moins en moins fort en s'éloignant, comme en vrai. Le micro et les haut-parleurs sont ceux de Windows (périphériques par défaut).\n\nLa touche pour parler (tenue) est dans l'onglet Touches. Le volume des voix ne change que ce que tu entends ; ton micro, ce que les autres entendent de toi.",
                      L"Voice is proximity-based: you are heard up to the chosen range, fading with distance, like in real life. The microphone and speakers are Windows' default devices.\n\nThe push-to-talk key is in the Keys tab. Voice volume only changes what you hear; your microphone, what the others hear from you."),
                 RectF(x, y + 4, w, h - 8), 12.5f, kGrey);
        } else if (!strcmp(k, "ajouts")) {   // Tous / Choisir / Aucun : SansAjouts, et les quatre interrupteurs
            bool none = GfxGet("SansAjouts") == 1;
            bool all = GfxGet("AjoutDrone") == 1 && GfxGet("AjoutSacs") == 1 && GfxGet("AjoutBoutique") == 1 && GfxGet("AjoutMusee") == 1;
            SetSegRow(g, it, row, { T(L"Tous", L"All"), T(L"Choisir", L"Choose"), T(L"Aucun", L"None") }, none ? 2 : all ? 0 : 1, UI_SET_AJOUTS);
        } else if (!strcmp(k, "ajoutchips")) {   // un jeton par ajout, cliquable (grise quand tout est coupe)
            bool none = GfxGet("SansAjouts") == 1;
            SetChipLines(w);
            float cx = x, ly = y + 7;
            for (int i = 0; i < 4; i++) {
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
        else if (!strcmp(k, "dronekb")) SetSegRow(g, it, row, { L"ZQSD", L"WASD", T(L"Flèches", L"Arrows") }, DroneLayoutGet(), UI_SET_DRONEKB);
        else if (!strcmp(k, "themes")) {   // quatre tuiles : Nuit boreale (sombre), Plein jour (clair), deux a venir
            static const wchar_t *nf[] = { L"Nuit boréale", L"Plein jour", L"Aurore verte", L"Garage" }, *ne[] = { L"Northern night", L"Daylight", L"Green aurora", L"Garage" };
            static const wchar_t *sf[] = { L"Aurore et neige", L"Clair, neige au soleil", L"Accent menthe", L"Bois et ambre" };
            static const wchar_t *se[] = { L"Aurora and snow", L"Light, snow in the sun", L"Mint accent", L"Wood and amber" };
            float tw = (w - 30) / 4;
            for (int i = 0; i < 4; i++) {
                RectF r(x + i * (tw + 10), y + 4, tw, 142);
                bool soon = i >= 2, sel = !soon && (i == 0) == g_dark, hot = !soon && g_uiHot == UI_SET_THEMECARD + i;
                GraphicsPath p; RoundRect(p, r, 14);
                SolidBrush fb(sel ? WithA(kAcc, 0.12f) : hot ? TH(btn2Hot) : TH(btn2)); g.FillPath(&fb, &p);
                Pen pen(sel ? kAcc : TH(fieldBorder), sel ? 2.0f : 1.0f); g.DrawPath(&pen, &p);
                RectF pv(r.X + 8, r.Y + 8, r.Width - 16, 70);
                float a = soon ? 0.45f : 1.0f;
                if (i == 0) SetThemePreview(g, pv, Color(255, 10, 21, 48), Color(255, 22, 48, 95), Color(115, 72, 240, 176), Color(100, 160, 110, 255), Color(26, 255, 255, 255), Color(50, 255, 255, 255), kDark.accent);
                else if (i == 1) SetThemePreview(g, pv, Color(255, 223, 240, 255), Color(255, 247, 251, 255), Color(140, 120, 200, 255), Color(0, 0, 0, 0), Color(205, 255, 255, 255), Color(40, 20, 50, 90), kLight.accent);
                else if (i == 2) SetThemePreview(g, pv, Color(255, 7, 18, 28), Color(255, 12, 34, 50), Color(150, 72, 240, 176), Color(0, 0, 0, 0), Color(20, 255, 255, 255), Color(75, 124, 232, 168), Color(255, 124, 232, 198));
                else SetThemePreview(g, pv, Color(255, 26, 20, 16), Color(255, 42, 30, 20), Color(115, 255, 180, 84), Color(0, 0, 0, 0), Color(20, 255, 255, 255), Color(75, 255, 200, 120), Color(255, 255, 180, 84));
                Text(g, T(nf[i], ne[i]), RectF(r.X + 8, r.Y + 84, r.Width - 16, 20), 13, FontStyleBold, WithA(kInk, a), StringAlignmentNear);
                Text(g, T(sf[i], se[i]), RectF(r.X + 8, r.Y + 104, r.Width - 16, 32), 10.5f, FontStyleRegular, WithA(kGrey, a), StringAlignmentNear);
                if (soon) { std::wstring s = T(L"Bientôt", L"Soon"); float pw = PillW(s, 9.5f); Pill(g, RectF(r.X + r.Width - pw - 12, r.Y + 12, pw, 18), WithA(kWarn, 0.9f), s, Color(255, 20, 16, 8), 9.5f); }
                else HotZone(r, UI_SET_THEMECARD + i);
            }
        } else if (!strcmp(k, "accent")) {   // cinq pastilles de couleur
            float sw = 5 * 30 + 4 * 8;
            SetRowLabel(g, row, T(it.fr, it.en), T(it.dFr, it.dEn), w - sw - 12);
            for (int i = 0; i < 5; i++) {
                RectF r(x + w - sw + i * 38, cy - 15, 30, 30);
                bool sel = i == g_accentSel, hot = g_uiHot == UI_SET_ACCENT + i;
                GraphicsPath p; RoundRect(p, r, 10);
                SolidBrush b(g_dark ? kSetAccents[i].dark : kSetAccents[i].light); g.FillPath(&b, &p);
                Pen pen(sel ? kInk : WithA(kInk, hot ? 0.6f : 0.25f), sel ? 2.4f : 1.2f); g.DrawPath(&pen, &p);
                if (sel) Icon(g, IC_CHECK, r.X + 15, r.Y + 15, 14, g_dark ? Color(255, 6, 19, 37) : Color(255, 255, 255, 255), 2.6f);
                HotZone(r, UI_SET_ACCENT + i);
            }
        } else if (!strcmp(k, "aurore")) SetSegRow(g, it, row, { T(L"Off", L"Off"), T(L"Calme", L"Calm"), T(L"Vivant", L"Lively") }, g_aurore, UI_SET_AURORE);
        else if (!strcmp(k, "soon")) {   // a venir : ligne grisee, pastille Bientot
            std::wstring s = T(L"Bientôt", L"Soon");
            float pw = PillW(s, 10);
            SetRowLabel(g, row, T(it.fr, it.en), it.dFr ? T(it.dFr, it.dEn) : L"", w - pw - 12, 0.5f);
            Pill(g, RectF(x + w - pw, cy - 10, pw, 20), WithA(kWarn, 0.16f), s, WithA(kWarn, 0.8f), 10);
        } else if (!strcmp(k, "winfollow")) SetToggleRow(g, it, row, ThemeFollowsWindows(), UI_SET_WINTHEME);
        else if (!strcmp(k, "apercu")) {   // petit salon aux couleurs du theme ; Par defaut (accent, aurore)
            RectF box(x, y + 4, w, 196);
            GraphicsPath p; RoundRect(p, box, 16);
            GraphicsState st = g.Save();
            g.SetClip(&p, CombineModeIntersect);
            LinearGradientBrush sky(RectF(box.X, box.Y - 1, box.Width, box.Height + 2), g_dark ? Color(255, 10, 21, 48) : Color(255, 223, 240, 255), g_dark ? Color(255, 22, 48, 95) : Color(255, 247, 251, 255), LinearGradientModeVertical);
            g.FillPath(&sky, &p);
            Color aurs[2] = { g_dark ? Color(100, 72, 240, 176) : Color(120, 120, 200, 255), g_dark ? Color(90, 160, 110, 255) : Color(0, 0, 0, 0) };
            for (int i = 0; i < 2 && g_aurore > 0; i++) {
                if (!aurs[i].GetA()) continue;
                GraphicsPath e; e.AddEllipse(box.X + (i ? box.Width * 0.4f : -box.Width * 0.25f), box.Y - 70, box.Width * 0.9f, 150.0f);
                PathGradientBrush pg(&e);
                pg.SetCenterColor(WithA(aurs[i], g_aurore == 1 ? 0.5f : 1.0f));
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
            SolidBrush cbb(TH(card)); g.FillPath(&cbb, &cp);
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
            bool custom = g_accentSel != 0 || g_aurore != 2;
            UiButton(g, RectF(x, y + 212, 150, 36), T(L"Par défaut", L"Default"), UI_SET_THEMEDEF, false, IC_UNDO, custom);
            Text(g, T(L"Appliqué tout de suite", L"Applied right away"), RectF(x + 162, y + 212, w - 162, 36), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
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
            RectF box(x, y + 4, w, 58);
            GraphicsPath p; RoundRect(p, box, 12);
            SolidBrush fb(av.empty() ? TH(card) : WithA(kAcc, 0.14f)); g.FillPath(&fb, &p);
            Pen pen(av.empty() ? TH(choiceBorder) : WithA(kAcc, 0.5f), 1); g.DrawPath(&pen, &p);
            std::wstring l1, l2 = g_localVer.empty() ? std::wstring(T(L"MWCoop n'est pas installé", L"MWCoop is not installed")) : std::wstring(T(L"Tu as la ", L"You have ")) + g_localVer;
            if (!av.empty()) l1 = std::wstring(T(L"Version ", L"Version ")) + av + T(L" disponible", L" available");
            else if (g_busy) l1 = T(L"Recherche ou installation en cours…", L"Checking or installing…");
            else l1 = T(L"MWCoop est à jour", L"MWCoop is up to date");
            Text(g, l1, RectF(box.X + 14, box.Y + 9, box.Width - 170, 22), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
            Text(g, l2, RectF(box.X + 14, box.Y + 30, box.Width - 170, 20), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
            if (!av.empty()) UiButton(g, RectF(box.X + box.Width - 150, box.Y + 11, 138, 36), T(L"Mettre à jour", L"Update"), UI_NOTE_UPDATE, true, IC_DOWNLOAD, g_lobby == LB_NONE && !g_busy);
            else UiButton(g, RectF(box.X + box.Width - 150, box.Y + 11, 138, 36), T(L"Vérifier", L"Check"), UI_SET_CHECK, false, -1, !g_busy);
        } else if (!strcmp(k, "canal")) SetSegRow(g, it, row, { L"Legacy", L"Reworked" }, ChannelReworked() ? 1 : 0, UI_SET_CANAL);
        else if (!strcmp(k, "majauto")) SetToggleRow(g, it, row, GetPrivateProfileIntW(L"Lanceur", L"MajAuto", 0, g_iniLauncher.c_str()) != 0, UI_SET_AUTO);
        else if (!strcmp(k, "notes")) SetButtonRow(g, it, row, T(L"Lire", L"Read"), UI_SET_NOTES);
        else if (!strcmp(k, "folder")) {   // dossier du jeu : version trouvee, chemin, Changer
            bool ok = !g_gameDir.empty();
            Text(g, T(it.fr, it.en), RectF(x, y + 2, w - 220, 24), 13.5f, FontStyleBold, kInk, StringAlignmentNear);
            SetChip(g, x + w, y + 14, ok ? L"My Winter Car " + g_gameVer : std::wstring(T(L"My Winter Car introuvable", L"My Winter Car not found")), ok ? kOk : kRed);
            std::wstring bt = ok ? T(L"Changer", L"Change") : T(L"Choisir l'exe…", L"Choose exe…");
            float bw = SetButtonW(bt);
            RectF pb(x, y + 32, w - bw - 8, 34);
            GraphicsPath pp; RoundRect(pp, pb, 10);
            SolidBrush pbb(TH(field)); g.FillPath(&pbb, &pp);
            Pen ppen(TH(fieldBorder), 1.0f); g.DrawPath(&ppen, &pp);
            StringFormat sf; sf.SetTrimming(StringTrimmingEllipsisPath); sf.SetFormatFlags(StringFormatFlagsNoWrap); sf.SetLineAlignment(StringAlignmentCenter);
            SolidBrush gb(ok ? kInk : kGrey);
            DrawStr(g, ok ? g_gameDir.c_str() : T(L"(montre-lui mywintercar.exe)", L"(show it mywintercar.exe)"), -1, FontOf(L"Consolas", 11.5f, FontStyleRegular), RectF(pb.X + 10, pb.Y, pb.Width - 20, pb.Height), &sf, &gb);
            UiButton(g, RectF(x + w - bw, y + 33, bw, 32), bt, UI_SET_FOLDER, false, IC_FOLDER);
        } else if (!strcmp(k, "start")) {
            const Opt *ls = OptByKey("LancerSteam");
            int how = ls && OptGet(*ls) ? 2 : GetPrivateProfileIntW(L"Lanceur", L"SansCopie", 0, g_iniLauncher.c_str()) ? 1 : 0;
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
        } else if (!strcmp(k, "compat")) { SetToggleRow(g, it, row, CompatAuto(), UI_SET_COMPAT); g_setCompatRow = row; }
        else if (!strcmp(k, "debug")) SetToggleRow(g, it, row, g_debug, UI_LOG_DEBUG);
        else if (!strcmp(k, "discordask")) SetToggleRow(g, it, row, GetPrivateProfileIntW(L"Lanceur", L"DiscordPropose", 0, g_iniLauncher.c_str()) == 0, UI_SET_DISCORDASK);
    }
    if (!SetVisible(row)) g_zones.resize(nz);   // (ligne coupee par le bord : pas de clic)
    return h;
}

// Une carte : titre (et sous-titre), ses lignes (deux sous-colonnes apres SI_BREAK), verre dessous ; renvoie sa hauteur.
static float SetDrawCard(Graphics &g, int ci, float x, float y, float w)
{
    const SetCard &c = kSetCards[ci];
    bool sub = c.sFr != NULL;
    float y0 = y, inner = w - 2 * kSetPad;
    if (SetDrawn(RectF(x, y, w, 50))) {
        Title(g, T(c.fr, c.en), RectF(x + kSetPad, y + 14, inner, 24), 16, kInk);
        if (sub) Text(g, T(c.sFr, c.sEn), RectF(x + kSetPad, y + 38, inner, 16), 11.5f, FontStyleRegular, kGrey, StringAlignmentNear);
    }
    y += sub ? 60 : 48;
    bool split = false;
    for (int i = 0; i < kSetItemN; i++) if (kSetItems[i].card == ci && kSetItems[i].kind == SI_BREAK) split = true;
    for (int i = 0; i < kSetItemN; i++) if (kSetItems[i].card == ci && kSetItems[i].wide) y += SetDrawItem(g, kSetItems[i], x + kSetPad, y, inner);
    float cw = split ? (inner - 24) / 2 : inner, cx = x + kSetPad, cy = y, maxY = y;
    for (int i = 0; i < kSetItemN; i++) {
        const SetItem &it = kSetItems[i];
        if (it.card != ci || it.wide) continue;
        if (it.kind == SI_BREAK) { maxY = max(maxY, cy); cx += cw + 24; cy = y; continue; }
        cy += SetDrawItem(g, it, cx, cy, cw);
    }
    maxY = max(maxY, cy);
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
    else { if (!it.fr) return false; lab = T(it.fr, it.en); }
    if (it.dFr) desc = T(it.dFr, it.dEn);
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
    static const wchar_t *fr[SET_N] = { L"Pour toutes tes parties · « Jeu » = valeur du jeu", L"Lumière, ombres, ray tracing · en jeu : F10", L"Vocal de proximité : qui t'entend, à quel volume",
                                        L"Défauts de tes salons · ce qui ne dépend que de toi", L"Touches de MWCoop en jeu · clique, puis appuie", L"Thème, couleur d'accent, aurore et neige", L"Langue, version, dossier du jeu, dépannage" };
    static const wchar_t *en[SET_N] = { L"For all your games · \"Game\" = the game's value", L"Light, shadows, ray tracing · in game: F10", L"Proximity voice: who hears you, how loud",
                                        L"Your lobby defaults · what only depends on you", L"MWCoop keys in game · click, then press", L"Theme, accent colour, aurora and snow", L"Language, version, game folder, troubleshooting" };
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
static void SetTutoTab(int step) { g_setTab = step == 17 || step == 18 || step == 21 ? SET_LANCEUR : SET_JEU; g_setSearch.clear(); }
static void SetCaptureState(const std::wstring &st)
{
    static const wchar_t *names[SET_N] = { L"jeu", L"image", L"son", L"coop", L"touches", L"theme", L"lanceur" };
    std::wstring s = st.size() > 9 ? st.substr(9) : L"";   // (apres "reglages-")
    for (int i = 0; i < SET_N; i++) if (s == names[i]) g_setTab = i;
    if (s == L"recherche") { g_setSearch = L"ombre"; g_setFocus = true; }
}

static bool GfxClick(int id)
{
    if (id >= UI_GFX_PRESET && id < UI_GFX_PRESET + (int)GfxPresets().size()) {
        for (auto &kv : GfxPresets()[id - UI_GFX_PRESET].v) GfxSet(kv.first, kv.second);
        return true;
    }
    const auto &rows = GfxRows();
    if (id >= UI_SETV && id < UI_SETV + (int)rows.size() * 8) {   // choix a segments : la valeur
        const GfxRow &r = rows[(id - UI_SETV) / 8];
        int i = (id - UI_SETV) % 8;
        const wchar_t *why = NULL;
        if (!GfxRowOff(r, &why) && i < (int)r.vals.size()) GfxSet(r.key, r.vals[i]);
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
    if (id >= UI_SET_TAB && id < UI_SET_TAB + SET_N) { g_setTab = id - UI_SET_TAB; g_setSearch.clear(); g_scroll[g_tab] = 0; return true; }
    switch (id) {
    case UI_SET_SEARCH: g_setFocus = true; g_time = 0.2f; return true;
    case UI_SET_SEARCHX: g_setSearch.clear(); return true;
    case UI_SET_THEMECARD: case UI_SET_THEMECARD + 1:
        g_dark = id == UI_SET_THEMECARD;
        WritePrivateProfileStringW(L"Lanceur", L"Theme", g_dark ? L"sombre" : L"clair", g_iniLauncher.c_str());
        return true;
    case UI_SET_WINTHEME: ThemeFollowWindows(!ThemeFollowsWindows()); return true;
    case UI_SET_THEMEDEF: ThemeSetAccent(0); ThemeSetAurore(2); return true;
    case UI_SET_PRIV: case UI_SET_PRIV + 1: WritePrivateProfileStringW(L"Lanceur", L"SalonPrive", id == UI_SET_PRIV ? L"0" : L"1", g_iniLauncher.c_str()); return true;
    case UI_SET_NOTES: GoPage(TAB_NOTES); return true;
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
    case UI_SET_GOIMAGE: g_setTab = SET_IMAGE; g_setSearch.clear(); g_scroll[g_tab] = 0; return true;
    }
    if (id >= UI_SET_ACCENT && id < UI_SET_ACCENT + 5) { ThemeSetAccent(id - UI_SET_ACCENT); return true; }
    if (id >= UI_SET_AURORE && id < UI_SET_AURORE + 3) { ThemeSetAurore(id - UI_SET_AURORE); return true; }
    if (id >= UI_SET_MAXP && id < UI_SET_MAXP + 4) { static const int m[] = { 4, 8, 12, 16 }; WritePrivateProfileStringW(L"Lanceur", L"SalonMax", std::to_wstring(m[id - UI_SET_MAXP]).c_str(), g_iniLauncher.c_str()); return true; }
    if (id >= UI_SET_AJOUTS && id < UI_SET_AJOUTS + 3) {
        int k = id - UI_SET_AJOUTS;
        GfxSet("SansAjouts", k == 2 ? 1 : 0);
        if (k == 0) for (const char *a : { "AjoutDrone", "AjoutSacs", "AjoutBoutique", "AjoutMusee" }) GfxSet(a, 1);
        return true;
    }
    if (id >= UI_SET_AJOUT && id < UI_SET_AJOUT + 4) { static const char *keys[] = { "AjoutDrone", "AjoutSacs", "AjoutBoutique", "AjoutMusee" }; const char *a = keys[id - UI_SET_AJOUT]; GfxSet(a, GfxGet(a) == 1 ? 0 : 1); return true; }
    if (id >= UI_SET_DRONEKB && id < UI_SET_DRONEKB + 3) { DroneLayoutSet(id - UI_SET_DRONEKB); return true; }
    return GfxClick(id);
}
