// ---------------------------------------------------------------- visite guidee du premier lancement - inclus par launcher.cpp
// Demande de JD (07/10), comme certains lanceurs : au premier lancement ([Lanceur] TutoVu absent), une suite de bulles.
// La fenetre s'assombrit sauf l'element montre (entoure), une bulle a fleche dit a quoi il sert ; SUIVANT (Entree,
// fleche droite), RETOUR (fleche gauche), PASSER (Echap). Les onglets montres s'ouvrent derriere la bulle. A revoir :
// COOP > Revoir le guide de demarrage.
static int g_tuto = -1, g_tutoHot;
static bool TutoActive() { return g_tuto >= 0; }   // rang dans la visite (-1 : pas de visite) ; survol : 1 suivant, 2 passer, 3 retour
static int g_tutoTabWas = -1;

struct TutoStep { const wchar_t *tFr, *dFr, *tEn, *dEn; const char *since; };   // since : version ou l'element est apparu
static const TutoStep kTuto[] = {
    { L"Bienvenue dans MWCoop !", L"Un nouveau lanceur, plus grand. Une petite visite en quelques bulles : tu peux la passer à tout moment (Échap) et la revoir depuis Réglages.",
      L"Welcome to MWCoop!", L"A new, bigger launcher. A quick tour in a few bubbles: you can skip it at any time (Esc) and see it again from Settings.", "0.42.0" },
    { L"Ton pseudo", L"Le nom que les autres joueurs voient au-dessus de ton personnage et dans le salon.",
      L"Your nickname", L"The name the other players see above your character and in the lobby.", "0.42.0" },
    { L"Par où jouer", L"STEAM : le plus simple, tu invites tes amis Steam, sans port ni adresse. IP / VPN : par adresse (réseau local, Radmin, Hamachi…).",
      L"How to connect", L"STEAM: the easiest, you invite your Steam friends, no port or address. IP / VPN: by address (local network, Radmin, Hamachi…).", "0.42.0" },
    { L"HÉBERGER", L"Ouvre un salon : tes amis le rejoignent, chacun se met PRÊT, puis LANCER démarre le jeu de tout le monde. L'engrenage du salon garde ses options.",
      L"HOST", L"Opens a lobby: your friends join it, everyone gets READY, then START launches everyone's game. The lobby's gear holds its options.", "0.42.0" },
    { L"REJOINDRE", L"Entre dans le salon d'un ami (par Steam : son invitation, ou son salon trouvé tout seul ; par IP : son adresse juste au-dessus).",
      L"JOIN", L"Enters a friend's lobby (through Steam: their invite, or their lobby found by itself; by IP: their address just above).", "0.42.0" },
    { L"JOUER EN SOLO", L"Le jeu vanilla : MWCoop n'est pas chargé du tout (ni pastille, ni menu coop). Ta sauvegarde n'est jamais touchée par les parties des autres.",
      L"PLAY SOLO", L"The vanilla game: MWCoop isn't loaded at all (no badge, no co-op menu). Your save is never touched by other people's games.", "0.75.14" },
    { L"Toutes les pages, à gauche", L"Le salon, ton personnage, ce que tu partages et les nouveautés : un clic, sans fenêtre qui s'ouvre par-dessus.",
      L"Every page, on the left", L"The lobby, your character, what you share and the updates: one click, no window popping over.", "0.42.0" },
    { L"Personnage et voiture", L"Ton apparence complète (silhouette, chapeau, lunettes, cheveux, visage, haut, pantalon) en 3D, et la couleur de ta CORRIS.",
      L"Character and car", L"Your full look (build, hat, glasses, hair, face, top, pants) in 3D, and your CORRIS color.", "0.42.0" },
    { L"Contenu et mods", L"Ce que tu envoies à tes invités : CD, peinture, drapeau, posters… et tes mods MSCLoader (expérimental), copiés à part chez eux.",
      L"Content and mods", L"What you send to your guests: CDs, paint, flag, posters… and your MSCLoader mods (experimental), copied apart on their side.", "0.42.0" },
    { L"Graphismes", L"Réglages > Jeu : affichage, confort, lumière et ombres, pseudos des joueurs (distance, ou cachés) ; l'onglet Image & ray tracing pour le reste. Préréglages en un clic ; en jeu : menu F10.",
      L"Graphics", L"Settings > Game: display, comfort, light and shadows, player names (distance, or hidden); the Image & ray tracing tab for the rest. One-click presets; in game: F10 menu.", "0.54.0" },
    { L"Nouveautés", L"Ce qui change à chaque version. Quand une nouvelle version sort, un bouton « Mettre à jour » apparaît sous le logo : la mise à jour se fait quand tu cliques.",
      L"What's new", L"What changes in each version. When a new version is out, an \"Update\" button shows up under the logo: the update happens when you click it.", "0.42.0" },
    { L"Journaux", L"L'icône page en haut à droite. Chaque partie garde ses journaux, rangés par date : déplie celle qui a posé problème, crée son zip ou supprime les vieilles. Un « ! » rouge s'affiche si la dernière partie s'est arrêtée brutalement.",
      L"Logs", L"The page icon at the top right. Every session keeps its own logs, sorted by date: expand the one that went wrong, zip it or delete old ones. A red \"!\" shows if the last game stopped abruptly.", "0.53.0" },
    { L"API moddeurs", L"Le { } en haut à droite. Tu fais des mods ? Ce que l'API de MWCoop permet, un exemple à copier, la DLL et la doc sur GitHub.",
      L"Modding API", L"The { } at the top right. Making mods? What MWCoop's API does, an example to copy, the DLL and the docs on GitHub.", "0.46.0" },
    { L"Réglages", L"Une seule page, sept onglets : Jeu, Image & ray tracing, Son & vocal, Co-op (tes salons, le réseau, le port), Touches, Thème, Lanceur (langue, mise à jour, dossier du jeu, dépannage).",
      L"Settings", L"One page, seven tabs: Game, Image & ray tracing, Sound & voice, Co-op (your lobbies, the network, the port), Keys, Theme, Launcher (language, updates, game folder, troubleshooting).", "0.42.0" },
    { L"Thème et langue", L"Soleil ou lune : clair ou sombre, d'un clic. FR : français, anglais, russe ou arabe. Plus de thèmes et de couleurs dans Réglages > Thème.", L"Theme and language", L"Sun or moon: light or dark, in one click. FR: French, English, Russian or Arabic. More themes and colours in Settings > Theme.", "0.42.0" },
    { L"Discord", L"La communauté MWCoop : de l'aide, les bugs à signaler, les nouveautés et des joueurs pour faire équipe. GitHub et Soutenir (Ko-fi) juste à côté.",
      L"Discord", L"The MWCoop community: help, bug reports, news and players to team up with. GitHub and Support (Ko-fi) right next to it.", "0.51.0" },
    { L"MSCLoader de MWCoop", L"Dans l'onglet Mods : OFFICIEL (celui installé dans le jeu) ou MWCOOP (installé à part, le jeu n'est pas modifié). Si tes mods ne se chargent pas, essaie MWCOOP, ou RÉINSTALLER.",
      L"MWCoop's MSCLoader", L"In the Mods tab: OFFICIAL (the one installed in the game) or MWCOOP (installed apart, the game is not changed). If your mods don't load, try MWCOOP, or REINSTALL.", "0.60.0" },
    { L"Démarrer le jeu", L"Dans Réglages > Lanceur > Jeu, « Départ du jeu » : Copie (d'origine), Dossier du jeu ou Steam. Si MWCoop ne se charge pas, essaie l'un puis l'autre ; le lanceur passe aussi tout seul au dossier du jeu.",
      L"Start the game", L"In Settings > Launcher > Game, \"Game start\": Copy (default), Game folder or Steam. If MWCoop doesn't load, try one then the other; the launcher also switches to the game folder by itself.", "0.62.2" },
    { L"Désinstaller", L"Dans Réglages > Lanceur > Jeu : retire tout MWCoop (dossiers compris) et le MSCLoader de MWCoop, à la Corbeille. Ta sauvegarde, tes mods et le MSCLoader officiel restent.",
      L"Uninstall", L"In Settings > Launcher > Game: removes all of MWCoop (folders included) and MWCoop's MSCLoader, to the Recycle Bin. Your save, your mods and the official MSCLoader stay.", "0.60.8" },
    { L"Crédits", L"L'étoile en haut à droite : ceux qui donnent quelque chose pour améliorer MWCoop. Merci à Dom pour ses 155 tenues, dans Personnage > Tenue !",
      L"Credits", L"The star at the top right: the people who give something to make MWCoop better. Thanks to Dom for 155 outfits, in Character > Outfit!", "0.61.0" },
    { L"Mode débogage", L"Dans Journaux : si MWCoop ne se charge pas, active-le, lance ta partie puis crée le zip. Il note tout ce que fait le lanceur et ce que le jeu a vraiment chargé.",
      L"Debug mode", L"In Logs: if MWCoop doesn't load, turn it on, start your game, then create the zip. It records everything the launcher does and what the game really loaded.", "0.62.0" },
    { L"Modes de compatibilité", L"Dans Réglages > Lanceur > Dépannage. Après un plantage, Windows ajoute parfois un mode de compatibilité au jeu, et MWCoop ne se charge plus : le lanceur le retire avant chaque partie.",
      L"Compatibility modes", L"In Settings > Launcher > Troubleshooting. After a crash, Windows sometimes adds a compatibility mode to the game, and MWCoop stops loading: the launcher removes it before every game.", "0.64.1" },
    { L"Guide", L"Tout pour jouer sans souci, rangé par catégories à déplier : installer, héberger, rejoindre, partager ses mods, Copie / Dossier / Steam… et les solutions aux problèmes courants, avec des images.",
      L"Guide", L"Everything to play trouble-free, sorted into categories you expand: install, host, join, share your mods, Copy / Folder / Steam… and fixes for common problems, with pictures.", "0.64.0" },
    { L"Touches en jeu", L"Dans Réglages > Touches : clique sur une touche puis appuie sur la nouvelle. Tchat (T), porte-monnaie pour donner de l'argent aux autres (N), vocal (V).",
      L"In-game keys", L"In Settings > Keys: click a key then press the new one. Chat (T), wallet to give money to others (N), voice (V).", "0.67.0" },
    { L"Visage et habits importés", L"Ton propre visage, haut ou pantalon : onglet Visage, bande « Mes images » : choisis la tuile (Visage, Haut, Pantalon), Gabarit ouvre l'image du jeu avec ses coutures, peins par-dessus, puis Importer… Les autres le voient en jeu.",
      L"Imported face and clothes", L"Your own face, top or pants: Face tab, \"My images\" strip: pick the tile (Face, Top, Pants), Template opens the game's image with its seams, paint over it, then Import… Others see it in game.", "0.71.0" },
    { L"Finition de la voiture", L"Dans Voiture : en plus de la couleur, la finition de la CORRIS (L, LX, SLX, GT) pour une nouvelle partie. L'hôte la donne à ses invités.",
      L"Car trim", L"In Car: besides the color, the CORRIS trim (L, LX, SLX, GT) for a new game. The host gives it to their guests.", "0.69.1" },
    { L"Options de la coop", L"Réglages > Co-op : « Quand j'héberge » (argent de départ, triches, ajouts : les défauts de tes salons), « Chez moi » (vocal, fichiers de l'hôte, images des autres) et « Réseau & lancement ». Son & vocal et Touches ont leur onglet.",
      L"Co-op options", L"Settings > Co-op: \"When I host\" (starting money, cheats, additions: your lobbies' defaults), \"On my side\" (voice, host's files, others' images) and \"Network & launch\". Sound & voice and Keys have their own tab.", "0.70.0" },
    { L"Triches", L"L'hôte peut autoriser les triches (Réglages > Co-op > Quand j'héberge, ou dans le salon). En jeu : panneau (F7, réglable dans Touches) ou commandes du tchat (/aide) : aller à un joueur, lieux, argent, soin, heure, neige. Chaque triche est annoncée à tous.",
      L"Cheats", L"The host can allow cheats (Settings > Co-op > When I host, or in the lobby). In game: panel (F7, set in Keys) or chat commands (/help): go to a player, places, money, heal, time, snow. Every cheat is announced to everyone.", "0.72.0" },
    { L"Carte", L"En jeu, M (réglable dans Réglages > Touches) ouvre la carte : les autres joueurs en direct, les lieux, molette pour zoomer, glisser pour se déplacer.",
      L"Map", L"In game, M (set in Settings > Keys) opens the map: the other players live, places, wheel to zoom, drag to move.", "0.73.0" },
    { L"Vue à la 3e personne", L"En jeu, F5 (réglable dans Réglages > Touches) passe à la troisième personne : ton personnage vu de dos à pied, ton véhicule vu de derrière en conduisant. Viser et prendre marchent comme d'habitude.",
      L"Third-person view", L"In game, F5 (set in Settings > Keys) switches to third person: your character seen from behind on foot, your vehicle seen from behind while driving. Aiming and picking up work as usual.", "0.75.0" },
    { L"L'onglet Jeu", L"Préréglage, Affichage, Confort & affichage en jeu, et le résumé Lumière & ombres ; tout le reste est dans l'onglet Image & ray tracing. La fenêtre de configuration d'Unity est passée toute seule : l'écran se règle ici.",
      L"The Game tab", L"Preset, Display, Comfort & in-game display, and the Light & shadows summary; everything else is in the Image & ray tracing tab. Unity's configuration window is now skipped: set your screen here.", "0.75.40" },
    { L"Caméra drone", L"En jeu, F8 : ta caméra devient un petit drone pour prendre des photos (viseur, zoom, pencher, flash). Les touches se règlent dans Réglages > Touches > Caméra drone.",
      L"Drone camera", L"In game, F8: your camera becomes a small drone to take photos (viewfinder, zoom, tilt, flash). Keys are set in Settings > Keys > Drone camera.", "0.75.44" },
    { L"Vanilla et ray tracing", L"Vanilla remet le jeu tel quel, sans les ajouts de MWCoop ; Ultra allume les ombres du soleil en ray tracing (carte graphique compatible DXR, en développement). Ses réglages : onglet Image & ray tracing.",
      L"Vanilla and ray tracing", L"Vanilla brings the game back as it is, without MWCoop's additions; Ultra turns on ray-traced sun shadows (DXR-capable graphics card, in development). Its settings: Image & ray tracing tab.", "0.76.0" },
    { L"Plaque VIN de la Rivett", L"Dans Voiture, sous la finition : les options de la CORRIS pour une nouvelle partie (année, moteur, boîte, pare-brise, toit vinyle, roues, radio...), comme la plaque du jeu. L'hôte les donne à ses invités.",
      L"Rivett VIN plate", L"In Car, under the trim: the CORRIS options for a new game (year, engine, gearbox, windshield, vinyl roof, wheels, radio...), like the game's plate. The host gives them to their guests.", "0.83.3" },
    { L"Peinture perso de la Rivett", L"Dans Voiture, en haut de l'aperçu : ta propre peinture sur la CORRIS, peinte sur le gabarit du jeu. En jeu, achète « Custom paint made by artist » chez Fleetari ; l'hôte l'envoie à ses invités.",
      L"Rivett custom paint", L"In Car, at the top of the preview: your own paint on the CORRIS, painted on the game's template. In game, buy \"Custom paint made by artist\" at Fleetari's; the host sends it to their guests.", "0.83.4" },
    { L"Activer ou couper un mod", L"Dans Mods, l'interrupteur « Actif » de chaque mod : coupé, le mod n'est plus chargé du tout (sa DLL devient .dll.off) ni envoyé aux invités. Remis, il revient au prochain lancement.",
      L"Turn a mod on or off", L"In Mods, each mod's \"On\" switch: off, the mod is no longer loaded at all (its DLL becomes .dll.off) nor sent to guests. Back on, it returns at the next launch.", "0.83.6" },
    { L"Mods pris en charge", L"Dans Mods, ce bouton ouvre la liste des mods que MWCoop synchronise, de ceux qui marchent tels quels, et des mods déconseillés avec leurs bugs connus.",
      L"Mod support", L"In Mods, this button opens the list of mods MWCoop syncs, those that work as they are, and the mods to avoid with their known bugs.", "0.94.0" },
    { L"Retirer les bosses", L"Dans Voiture : toutes les voitures de ta sauvegarde repartent lisses (jeu fermé). Le fichier des bosses est mis de côté, pas effacé. En coop, c'est l'hôte qui le fait.",
      L"Remove dents", L"In Car: every car in your save is smooth again (game closed). The dents file is set aside, not deleted. In co-op, the host does it.", "0.94.0" },
    { L"Sauvegardes", L"Plusieurs parties, une par groupe d'amis : crée-les, nomme-les, choisis celle à héberger (ici ou dans le salon). Tes invités reçoivent ta sauvegarde, la leur n'est pas touchée.",
      L"Saves", L"Several games, one per group of friends: create them, name them, pick the one to host (here or in the lobby). Your guests receive your save, theirs is not touched.", "0.95.0" },
    { L"MSC ou MWC", L"Sous le numéro de version, en haut à gauche : choisis ton jeu, My Summer Car (BETA) ou My Winter Car. Le lanceur se relance sur le jeu choisi. Tout le monde doit être sur le même jeu pour jouer ensemble.",
      L"MSC or MWC", L"Under the version number, top left: pick your game, My Summer Car (BETA) or My Winter Car. The launcher restarts on the chosen game. Everyone must be on the same game to play together.", "1.1.0" },
    { L"Le salon : code et options", L"En hébergeant : Public ou Privé (code de session à COPIER pour tes amis par IP), joueurs au plus (2 à 16) et options de la partie, pour ce salon seulement, envoyées à tes invités.",
      L"The lobby: code and options", L"When hosting: Public or Private (a session code to COPY for your friends by IP), players max (2 to 16) and game options, for this lobby only, sent to your guests.", "0.96.4" },
    { L"Code de session", L"Pour rejoindre un salon privé par IP : l'adresse de l'hôte et, ici, le code qu'il t'a donné. Salon public : laisse vide. Par Steam, un ami invité entre sans rien taper.",
      L"Session code", L"To join a private lobby by IP: the host's address and, here, the code they gave you. Public lobby: leave it empty. Through Steam, an invited friend gets in without typing anything.", "0.96.4" },
    // 0.97.0 (maquette du 17/10) : tchat du salon, Chercher un reglage, onglet Theme, prereglages, onglets Personnage, chiffres
    // des sauvegardes, pistes du CD, onglets du Guide (42..49).
    { L"Le tchat du salon", L"Dans le Salon, sous les joueurs : les trois derniers messages, « Message au salon » et ENVOYER (ou Entrée). Pour se mettre d'accord avant de lancer, par Steam comme par IP.",
      L"The lobby chat", L"In the Lobby, under the players: the last three messages, \"Message to the lobby\" and SEND (or Enter). To agree on things before starting, through Steam as well as by IP.", "0.97.0" },
    { L"Chercher un réglage", L"Tape « ombre », « port » ou « vocal » : les lignes de tous les onglets qui correspondent s'affichent, avec le nom de leur carte. Échap efface.",
      L"Search a setting", L"Type \"shadow\", \"port\" or \"voice\": the matching rows of every tab show up, with the name of their card. Esc clears.", "0.97.0" },
    { L"Onglet Thème", L"Nuit boréale, Plein jour, Aurore verte ou Garage, la couleur d'accent, l'aurore et la neige, le verre dépoli, la densité et la taille du texte. L'aperçu à droite montre le résultat ; Appliquer le garde.",
      L"Theme tab", L"Northern night, Daylight, Green aurora or Garage, the accent colour, the aurora and snow, the frosted glass, the density and the text size. The preview on the right shows the result; Apply keeps it.", "0.97.0" },
    { L"Préréglages", L"Vanilla (le jeu tel quel), Léger, Équilibré, Joli, Ultra : tout d'un coup, chaque ligne reste modifiable ; dès que tu en changes une, la pastille passe sur Perso.",
      L"Presets", L"Vanilla (the game as is), Light, Balanced, Pretty, Ultra: everything at once, every row stays editable; as soon as you change one, the pill moves to Custom.", "0.97.0" },
    { L"Tenue, Visage, Voiture", L"Les trois onglets de la page Personnage. Vues Pied, Portrait ou Volant ; glisse pour tourner, molette pour zoomer. Les tuiles montrent chaque choix en 3D ; « + N » : la suite.",
      L"Outfit, Face, Car", L"The three tabs of the Character page. Full, Portrait or Wheel views; drag to turn, wheel to zoom. The tiles show every choice in 3D; \"+ N\": the rest.", "0.97.0" },
    { L"Les chiffres de ta partie", L"Pour la sauvegarde choisie : le jour, l'argent, les heures jouées et la dernière partie (quand, avec qui), notés par le mod à chaque sauvegarde. Dessous : copies de secours, Dupliquer, Exporter (zip), Recommencer.",
      L"Your game's figures", L"For the chosen save: the day, the money, the hours played and the last session (when, with whom), noted by the mod at every save. Below: backup copies, Duplicate, Export (zip), Restart.", "0.97.0" },
    { L"Les pistes du CD", L"Contenu envoyé > CD (ou Radio) : les pistes qui partent chez tes invités. « Ajouter des pistes » (.ogg) ou glisse les fichiers ici : numérotés track1…trackN pour toi. Jauge en bas : 100 Mo au plus.",
      L"The CD tracks", L"Sent content > CD (or Radio): the tracks that go to your guests. \"Add tracks\" (.ogg) or drop the files here: numbered track1…trackN for you. Gauge at the bottom: 100 MB at most.", "0.97.0" },
    { L"Guide, Nouveautés, Crédits", L"Les trois onglets de cette page, avec le champ Chercher… qui filtre les articles. En bas de chaque article : ← précédent et suivant →.",
      L"Guide, What's new, Credits", L"The three tabs of this page, with the Search… field that filters the articles. At the bottom of every article: ← previous and next →.", "0.97.0" },
    { L"Onglet HUD", L"Le style des jauges en jeu : Vanilla (celui du jeu), Carte, Anneaux ou Bandeau, avec un aperçu. Puis sa taille, sa position, sa couleur, les jauges montrées, le panneau Équipe et les alertes. En jeu aussi : F10 › HUD.",
      L"HUD tab", L"The style of the in-game gauges: Vanilla (the game's own), Card, Rings or Strip, with a preview. Then its size, position, colour, the gauges shown, the team panel and the alerts. In game too: F10 › HUD.", "0.97.11" },
    // 1.2.0 (20/10) : page OUTILS, l'editeur de carte (51)
    { L"Outils", L"La clé en haut à droite, à gauche de l'étoile : des programmes à part. Le premier, l'éditeur de carte : un clic pour le télécharger, un autre pour le lancer, sur My Summer Car ou My Winter Car.",
      L"Tools", L"The wrench at the top right, left of the star: separate programs. The first one, the map editor: one click to download it, another to launch it, on My Summer Car or My Winter Car.", "1.2.0" },
    { L"C'est parti !", L"Bon jeu à plusieurs en Finlande. Et si quelque chose ne va pas : les journaux, et le Discord.",
      L"Let's go!", L"Have fun together in Finland. And if something goes wrong: the logs, and the Discord.", "0.42.0" },
};
static const int kTutoN = (int)(sizeof(kTuto) / sizeof(kTuto[0]));
// My Summer Car (19/10, demande de JD : « tout doit parler de la SATSUMA ») : la SATSUMA n'a ni finition ni plaque VIN
// (etapes 25 et 33 sautees) ; les bulles de la voiture (7, 34) parlent de la SATSUMA.
static const TutoStep kTutoMsc7 = { L"Personnage et voiture", L"Ton apparence complète (silhouette, chapeau, lunettes, cheveux, visage, haut, pantalon) en 3D, et la couleur de ta SATSUMA.",
    L"Character and car", L"Your full look (build, hat, glasses, hair, face, top, pants) in 3D, and your SATSUMA color.", "0.42.0" };
static const TutoStep kTutoMsc34 = { L"Peinture perso de la SATSUMA", L"Dans Voiture, en haut de l'aperçu : ta propre peinture sur la SATSUMA, peinte sur le gabarit du jeu. En jeu, achète « Custom paint made by artist » à l'atelier de Fleetari ; l'hôte l'envoie à ses invités.",
    L"SATSUMA custom paint", L"In Car, at the top of the preview: your own paint on the SATSUMA, painted on the game's template. In game, buy \"Custom paint made by artist\" at Fleetari's repair shop; the host sends it to their guests.", "0.83.4" };
static const TutoStep &TutoStepOf(int i)
{
    if (g_game == GAME_MSC && i == 7) return kTutoMsc7;
    if (g_game == GAME_MSC && i == 34) return kTutoMsc34;
    return kTuto[i];
}
static bool TutoSkip(int i) { return g_game == GAME_MSC && (i == 25 || i == 33); }

// Pastille d'un onglet de la page Reglages (meme calcul que DrawSettingsPage : gfx.inc) et champ Chercher un reglage de
// l'en-tete (DrawSetSearchBox : a gauche des raccourcis API / Journaux / Credits, ui.inc DrawHeader).
static RectF TutoSetTab(int t)
{
    float x = kMain.X;
    for (int k = 0; k < t; k++) x += MeasureW(MeasureG(), T(kSetTabFr[k], kSetTabEn[k]), 13, FontStyleBold) + 30 + 6;
    return RectF(x, 92, MeasureW(MeasureG(), T(kSetTabFr[t], kSetTabEn[t]), 13, FontStyleBold) + 30, 36);
}
static RectF TutoSetSearch() { return RectF(g_headRight - 250, 22, 250, 44); }
// Element montre a l'etape i (rectangle vide : bulle au centre). Coordonnees de la carte.
static RectF TutoTarget(int i)
{
    auto join = [](RectF a, RectF b) { float x = min(a.X, b.X), y = min(a.Y, b.Y); return RectF(x, y, max(a.X + a.Width, b.X + b.Width) - x, max(a.Y + a.Height, b.Y + b.Height) - y); };
    switch (i) {
    case 1: return g_fields[0].r;
    case 2: return join(g_btn[B_NETSTEAM].r, g_btn[B_NETIP].r);
    case 3: return g_btn[B_HOST].r;
    case 4: return g_btn[B_JOIN].r;
    case 5: return g_btn[B_SOLO].r;
    case 6: return join(g_navR[TAB_HOME], g_navR[TAB_NOTES]);
    case 7: return join(g_navR[TAB_SKIN], g_navR[TAB_CAR]);
    case 8: return join(g_navR[TAB_CONTENT], g_navR[TAB_MODS]);
    case 9: return g_navR[TAB_GFX];
    case 10: return g_navR[TAB_NOTES];
    case 11: return g_navR[TAB_LOGS];
    case 12: return g_navR[TAB_API];   // (raccourci de l'en-tete : ui.inc HeadPage)
    case 13: return g_navR[TAB_COOP];
    case 14: return join(g_btn[B_LANG].r, g_btn[B_THEME].r);
    case 15: return g_btn[B_DISCORD].r;
    case 16: { RectF a = kMscSrcOff, b = kMscReinst; return RectF(a.X - 110, a.Y, b.X + b.Width - a.X + 110, a.Height); }
    case 17: return g_setSteamRow;   // (page Reglages, onglet Lanceur : gfx.inc)
    case 18: return g_setUninst;
    case 19: return g_navR[TAB_CREDITS];   // (l'etoile de l'en-tete : ui.inc HeadPage)
    case 20: return kLogsDebugRow;
    case 21: return g_setCompatRow;
    case 22: return g_navR[TAB_WIKI];
    case 23: return g_navR[TAB_GFX];
    case 24: return g_faceRect;
    case 25: return g_trimRect;
    case 26: return g_navR[TAB_GFX];
    case 27: return g_navR[TAB_GFX];
    case 28: return g_navR[TAB_GFX];
    case 29: return g_navR[TAB_GFX];
    case 30: return kGfxArea;
    case 31: return kGfxArea;
    case 32: return kGfxTop;
    case 33: return g_vinBtnRect;
    case 34: return g_paintBtnRect;
    case 35: return MscActiveRect(MscRowRect(0));
    case 36: return kMscCompat;
    case 37: return g_dentBtnRect;
    case 38: return g_navR[TAB_SAVES];
    case 39: return g_canalRect;
    case 40: return g_btn[B_HOST].r;
    case 41: return g_fields[2].r;   // (reseau IP : le champ CODE ; Steam : bulle au centre)
    // 0.97.0
    case 42: return g_navR[TAB_LOBBY];                       // (le tchat n'existe que dans un salon ouvert : la page Salon de la barre)
    case 43: return TutoSetSearch();                         // Chercher un reglage (en-tete de la page Reglages)
    case 44: return TutoSetTab(SET_THEME);                   // onglet Theme (TutoGo l'ouvre)
    case 45: return kGfxTop;                                 // carte Prereglage (onglet Jeu)
    case 46: return join(PersoTabRect(0), PersoTabRect(2));  // Tenue / Visage / Voiture (en-tete de la page Personnage : tenue.inc)
    case 47: return RectF(kSaveR.X, kSaveR.Y + 28, kSaveR.Width, 240);   // haut de la carte de droite des Sauvegardes : chiffres et derniere partie (saves.inc)
    case 48: return RectF(kContentR.X, kContentR.Y, kContentR.Width, 160);   // haut de la carte de droite : les pistes du CD (modsync.inc)
    case 49: return RectF(g_guideTabRight - GuideTabsW(), 26, GuideTabsW(), 36);   // Guide / Nouveautes / Credits (wiki.inc GuideTabsDraw)
    // 0.97.11
    case 50: return TutoSetTab(SET_HUD);                     // onglet HUD (TutoGo l'ouvre)
    // 1.2.0
    case 51: return g_navR[TAB_TOOLS];                       // raccourci Outils de l'en-tete (ui.inc DrawHeader)
    }
    return RectF(0, 0, 0, 0);
}
static int TutoTab(int i) { return i == 30 || i == 31 || i == 32 || i == 43 || i == 45 || i == 50 ? TAB_GFX : i == 16 || i == 35 || i == 36 ? TAB_MODS : i == 12 || i == 17 || i == 18 || i == 21 || i == 44 ? TAB_COOP : i == 24 || i == 46 ? TAB_SKIN : i == 25 || i == 33 || i == 34 || i == 37 ? TAB_CAR : i == 20 ? TAB_LOGS : i == 19 || i == 49 ? TAB_WIKI : i == 47 ? TAB_SAVES : i == 48 ? TAB_CONTENT : TAB_HOME; }   // (16 : choix de MSCLoader, onglet MODS ; 12, 17, 18 : Reglages > Lanceur ; 19, 49 : Guide ; 43-45 : Reglages ; 46 : Personnage ; 47 : Sauvegardes ; 48 : Contenu envoye)

// Visite complete (premier lancement, COOP > Revoir le guide), ou seulement les bulles des elements apparus depuis la
// derniere visite ([Lanceur] TutoVersion), apres une mise a jour : pour un nouveau bouton, ajouter une etape avec la
// version qui l'apporte.
static std::vector<int> g_tutoList;
static bool g_tutoAll;
static int VerCmp(const char *a, const char *b)
{
    int x[3] = {}, y[3] = {};
    sscanf_s(a, "%d.%d.%d", &x[0], &x[1], &x[2]);
    sscanf_s(b, "%d.%d.%d", &y[0], &y[1], &y[2]);
    for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i] < y[i] ? -1 : 1;
    return 0;
}
static int TutoCur() { return g_tuto >= 0 && g_tuto < (int)g_tutoList.size() ? g_tutoList[g_tuto] : 0; }

static void TutoGo(int i)
{
    if (i < 0) i = 0;
    if (i >= (int)g_tutoList.size()) {   // fini : vue jusqu'a cette version
        g_tuto = -1;
        g_tab = g_tutoTabWas >= 0 ? g_tutoTabWas : TAB_HOME;
        WritePrivateProfileStringW(L"Lanceur", L"TutoVu", L"1", g_iniLauncher.c_str());
        WritePrivateProfileStringW(L"Lanceur", L"TutoVersion", Widen(MyVersionRaw()).c_str(), g_iniLauncher.c_str());
        DiscordAskMaybe();   // (premier lancement : l'invitation au Discord vient apres la visite)
        return;
    }
    g_tuto = i;
    g_tutoHot = 0;
    int t = TutoTab(TutoCur());
    g_tab = t;
    if (t == TAB_GFX || t == TAB_COOP) SetTutoTab(TutoCur());   // (page Reglages : l'onglet de l'element montre)
    if (TutoCur() == 44) g_setTab = SET_THEME;   // (0.97.0 : la bulle de l'onglet Theme l'ouvre)
    if (TutoCur() == 50) g_setTab = SET_HUD;     // (0.97.11 : la bulle de l'onglet HUD l'ouvre)
}
static void TutoStart(bool all)
{
    if (g_state != ST_IDLE) return;
    char seen[32] = "0.0.0";
    GetPrivateProfileStringA("Lanceur", "TutoVersion", "0.0.0", seen, sizeof(seen), Narrow(g_iniLauncher).c_str());
    g_tutoList.clear();
    for (int i = 0; i < kTutoN; i++) if (!TutoSkip(i) && (all || VerCmp(kTuto[i].since, seen) > 0)) g_tutoList.push_back(i);
    if (!all && !g_tutoList.empty() && g_tutoList[0] == 0) g_tutoList.erase(g_tutoList.begin());   // (bienvenue : visite complete seulement)
    g_tutoAll = all;
    if (g_tutoList.empty()) { WritePrivateProfileStringW(L"Lanceur", L"TutoVersion", Widen(MyVersionRaw()).c_str(), g_iniLauncher.c_str()); return; }
    g_tutoTabWas = g_tab == TAB_COOP || PageSnap(g_tab) ? TAB_HOME : g_tab;
    g_lobbyOpts = false;
    g_focus = -1;
    TutoGo(0);
}
// Au demarrage : jamais vue -> visite complete ; deja vue -> les nouveautes depuis.
static void TutoAtStartup()
{
    if (GetPrivateProfileIntW(L"Lanceur", L"TutoVu", 0, g_iniLauncher.c_str()) == 0) TutoStart(true);
    else if (MyVersionRaw() != "dev") TutoStart(false);
}

// Bulle : sous l'element (ou au-dessus s'il n'y a pas la place), dans la fenetre ; fleche vers l'element.
static RectF TutoBubble(RectF tg, bool *below)
{
    const float w = 380, h = 170;
    if (tg.Width <= 0) { *below = true; return RectF(kCardW / 2 - w / 2, kCardH / 2 - h / 2, w, h); }
    if (tg.X + tg.Width < 260 && tg.Height > 120) {   // grand element de la barre de gauche : bulle a sa droite
        *below = true;
        return RectF(tg.X + tg.Width + 24, min(max(tg.Y + tg.Height / 2 - h / 2, 24.0f), kCardH - 24 - h), w, h);
    }
    float cx = tg.X + tg.Width / 2;
    float x = min(max(cx - w / 2, 24.0f), kCardW - 24 - w);
    *below = tg.Y + tg.Height + 18 + h < kCardH - 16;
    float y = *below ? tg.Y + tg.Height + 18 : tg.Y - 18 - h;
    return RectF(x, y, w, h);
}
static RectF TutoBtn(RectF b, int k)   // 1 suivant, 2 passer, 3 retour
{
    if (k == 1) return RectF(b.X + b.Width - 130, b.Y + b.Height - 50, 114, 36);
    if (k == 3) return RectF(b.X + b.Width - 226, b.Y + b.Height - 50, 88, 36);
    return RectF(b.X + 16, b.Y + b.Height - 50, 80, 36);
}
static int TutoHit(float x, float y)
{
    if (g_tuto < 0) return 0;
    bool below;
    RectF b = TutoBubble(TutoTarget(TutoCur()), &below);
    for (int k = 1; k <= 3; k++) if ((k != 3 || g_tuto > 0) && (k != 2 || g_tuto < (int)g_tutoList.size() - 1) && TutoBtn(b, k).Contains(x, y)) return k;
    return 0;
}
static void TutoClick(float x, float y)
{
    int k = TutoHit(x, y);
    if (k == 1) TutoGo(g_tuto + 1);
    else if (k == 3) TutoGo(g_tuto - 1);
    else if (k == 2) TutoGo((int)g_tutoList.size());
}
static bool TutoKey(WPARAM wp)
{
    if (g_tuto < 0) return false;
    if (wp == VK_ESCAPE) TutoGo((int)g_tutoList.size());
    else if (wp == VK_RETURN || wp == VK_RIGHT || wp == VK_SPACE) TutoGo(g_tuto + 1);
    else if (wp == VK_LEFT) TutoGo(g_tuto - 1);
    return true;
}

static void DrawTuto(Graphics &g)
{
    if (g_tuto < 0) return;
    int step = TutoCur();
    RectF tg = TutoTarget(step);
    // voile sur la fenetre, trou arrondi autour de l'element
    GraphicsPath veil;
    veil.SetFillMode(FillModeAlternate);
    RoundRect(veil, RectF(0, 0, kCardW, kCardH), 24);
    RectF hole = tg;
    if (tg.Width > 0) { hole.Inflate(6, 6); RoundRect(veil, hole, min(16.0f, hole.Height / 2)); }
    SolidBrush vb(Color(170, 2, 6, 18));
    g.FillPath(&vb, &veil);
    if (tg.Width > 0) {
        GraphicsPath hp; RoundRect(hp, hole, min(16.0f, hole.Height / 2));
        Pen ring(kAcc, 2.4f); g.DrawPath(&ring, &hp);
    }
    bool below;
    RectF b = TutoBubble(tg, &below);
    GlassLive(g, b, 18);
    Pen bpen(WithA(kAcc, 0.8f), 1.4f);
    { GraphicsPath bp; RoundRect(bp, b, 18); g.DrawPath(&bpen, &bp); }
    if (tg.Width > 0 && !(tg.X + tg.Width < 260 && tg.Height > 120)) {   // fleche vers l'element
        float ax = min(max(tg.X + tg.Width / 2, b.X + 28), b.X + b.Width - 28);
        PointF tri[3];
        if (below) { tri[0] = PointF(ax - 10, b.Y + 1); tri[1] = PointF(ax + 10, b.Y + 1); tri[2] = PointF(ax, b.Y - 11); }
        else { tri[0] = PointF(ax - 10, b.Y + b.Height - 1); tri[1] = PointF(ax + 10, b.Y + b.Height - 1); tri[2] = PointF(ax, b.Y + b.Height + 11); }
        SolidBrush tb(g_dark ? Color(235, 30, 44, 84) : Color(240, 255, 255, 255));
        g.FillPolygon(&tb, tri, 3);
        g.DrawLine(&bpen, tri[0], tri[2]);
        g.DrawLine(&bpen, tri[1], tri[2]);
    }
    const TutoStep &st = TutoStepOf(step);
    wchar_t num[16];
    swprintf_s(num, L"%d / %d", g_tuto + 1, (int)g_tutoList.size());
    Text(g, num, RectF(b.X + b.Width - 84, b.Y + 14, 68, 20), 11.5f, FontStyleBold, kGrey, StringAlignmentFar);
    float tx = b.X + 18;
    if (!g_tutoAll) {   // apres une mise a jour : etiquette NOUVEAU
        const wchar_t *nl = T(L"NOUVEAU", L"NEW");
        float w = PillW(nl, 10);
        Pill(g, RectF(tx, b.Y + 15, w, 20), kRed, nl, Color(255, 255, 255, 255), 10);
        tx += w + 8;
    }
    Title(g, T(st.tFr, st.tEn), RectF(tx, b.Y + 10, b.X + b.Width - 96 - tx, 28), 17, kAcc);
    Para(g, T(st.dFr, st.dEn), RectF(b.X + 18, b.Y + 44, b.Width - 36, b.Height - 100), 13, kInk);
    for (int k = 1; k <= 3; k++) {
        if (k == 3 && g_tuto == 0) continue;
        if (k == 2 && g_tuto == (int)g_tutoList.size() - 1) continue;   // (derniere bulle : rien a passer)
        RectF r = TutoBtn(b, k);
        bool hot = g_tutoHot == k;
        GraphicsPath p; RoundRect(p, r, 12);
        if (k == 1) { SolidBrush lg(kAcc); g.FillPath(&lg, &p); if (hot) { SolidBrush hb(Color(55, 255, 255, 255)); g.FillPath(&hb, &p); } }
        else if (hot) { SolidBrush hb(TH(btn2Hot)); g.FillPath(&hb, &p); }
        const wchar_t *lab = k == 1 ? (g_tuto == (int)g_tutoList.size() - 1 ? T(L"TERMINER", L"FINISH") : T(L"SUIVANT", L"NEXT")) : k == 2 ? T(L"Passer", L"Skip") : T(L"Retour", L"Back");
        Text(g, lab, r, 12.5f, FontStyleBold, k == 1 ? kOnAcc : hot ? kAcc : kGrey);
    }
}
