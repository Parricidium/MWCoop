# Feuille de route MWCoop

Liste de JD (02/10/2026). Le principe : tout ce qu'un joueur peut faire en solo, un invité doit
pouvoir le faire, et les autres doivent le voir.

## Monde
- [x] Monde synchronisé : météo, PNJ, circulation, interactions avec les PNJ, voix des PNJ.
      Fait (0.1) : heure, jour, météo (nuages, pluie, température) de l'hôte.
      Fait (0.6) : circulation (routes, bus, train...) et passants dictés par l'hôte.
      Fait (0.8) : répliques des PNJ provoquées par un joueur et jurons des joueurs entendus par tous
      (au même endroit ; ceux qui bavardent seuls ne sont pas doublés).
- [x] Portes, lumières, interrupteurs : ce qu'un joueur ouvre ou allume, les autres le voient.
      Fait (0.1) : portes des bâtiments, interrupteurs, télé, lecteur CD. Fait (0.7) : l'invité qui arrive en
      cours de partie reçoit l'état de tout ce qui est suivi. Fait (0.10) : interrupteurs de la maison,
      portières, capots et coffres des véhicules.
- [~] Toutes les actions du solo possibles pour les invités : gratter le pare-brise, boutons,
      pénalités, utilisation des objets…
      Fait (0.10-0.11) : commandes des véhicules (clé, frein à main, vitres, boîte, boutons) rejouées
      chez tous, places passagers, tableau de bord et équipements visibles, prise du chauffage moteur.
      Fait (0.12) : tout le reste du monde par un module générique (WorldFsms) : toute action d'un joueur
      sur un automate du monde est rejouée chez les autres (factures, courrier, maison, garage, commandes
      de pièces...), l'hôte fait référence pour les états sauvegardés de la maison et des systèmes.
      Fait (0.13) : téléphone (appels tirés par l'hôte, sonnerie chez tous, décroché partagé : conséquences
      chez tous, voix chez celui qui décroche), commandes par téléphone, petites annonces identiques, colis
      de la poste suivis ; relecture : pas d'écho des rejeux, pas de double événement du monde, argent du
      receveur protégé, crochets protégés, clés stables.
      Fait (0.13.1) : programme de la télé suivi chez tous ; feux de recul ; places passagers corrigées.
      Fait (0.14) : portes automatiques et barrières pour tous, écran de la pompe à essence, voitures
      garées, PNJ visibles pareil chez tous, rayons et paniers des magasins, portières battantes.
      Fait (0.15) : sacs de courses ouverts chez tous, portières refermées en poussant, chaleur de l'habitacle.
      Fait (0.16) : portières arbitrées par l'hôte (clics croisés, spam du clic), fermeture au claquement réel.
      Fait (0.17) : coffre fermé pendant qu'un autre conduit sans envol (portières jamais figées), réapparition
      au choix (appartement ou maison des parents) au lieu du retour au menu.
- [x] Audit de la synchro (0.18) : recensement, empreintes hôte/invités (DESYNC), enregistreur d'actions non partagées.
- [x] Lot 1 de l'analyse (0.19) : portières (verrou, gauche), PNJ (calques, téléphone, clients, lissage), objets dans les
      voitures, objets empochés, revenus du monde une fois, train, machines à jeu (verrou + spectateurs).
- [x] Lot 2 (0.20) : taxi (MACHTWAGEN), givre/grattage, remorquage, appels sortants et courrier, achats hors épicerie
      (bar, contrôle technique, brocante, café, bus), vêtements portés, boulons des pièces juste montées, bois et
      fendeuse, fosses septiques, état moteur à la reprise du volant, sauvegarde coop aux toilettes.
      Reste : prix de la brocante tirés au hasard de chaque côté, appels quand l'hôte est loin de la maison.
- [x] Lot 3 (0.21) : boîtier CD (ouvert/fermé) et CD sorti suivis ; voiture du lanceur toujours complète d'origine.
- [x] Interface (0.21) : écran d'attente des invités au menu, test UDP dans le salon, journaux plus visibles (zip à
      envoyer), F10 façon GTA.
- [x] Lot 4 (0.23) : boissons, poubelle, boîtes de pièces, cuisine, feux, incendies, crics, pont, palan, dégâts,
      chargeur, boîte à gants, fusibles, kilju, gains uniques, prix de la brocante, téléphone, PNJ, police, bus, gestes.
      À vérifier en vraie partie : luge de Teimo renversée, PNJ qui sert un invité, bagarre, retrait d'un fusible par
      un invité, jus.
- [x] Corrections du 06/10 : prise du chauffage moteur (0.26.4) ; paie des boulots une seule fois (enveloppes des
      clients retirées chez les autres dès que l'un la prend), état du corps de l'invité (faim, soif, fatigue…) gardé
      d'une session à l'autre (0.27.0). Fendeuse en retard chez l'invité : déjà rattrapée (vérifié).
- [x] Jeu par Steam (0.30) : salon, invitations, pair-à-pair, avatars ; invité qui ne se reconnectait plus corrigé.
      Retours de la partie du 06/10 (0.30.1) : portes du garage des parents (poussées) suivies par l'angle de celui qui
      pousse, écran noir après une mort par le monoxyde (paupières rouvertes, son rendu).
- [~] Courses (rallye, course sur glace), demande de JD du 06/10 : tout ce qu'un joueur peut voir, et chaque joueur
      peut courir avec son temps au classement.
      Fait (0.28) : voitures IA du rallye (3 voitures, 9 pilotes) et de la glace (16) dictées par l'hôte, pilote et
      livrée compris ; classement télé du rallye identique ; course de la semaine (rallye ou glace) comme chez l'hôte ;
      inscription, chrono, feux de départ, parc fermé et contrôle technique personnels (plus rejoués chez les autres).
      Fait (0.29) : rallye à plusieurs pilotes humains : adversaires communs (graine de l'hôte gardée pour la semaine),
      temps de chaque pilote au classement de tous (podium et prix de chacun en tiennent compte), heures de départ
      distinctes données par l'hôte (une seule CORRIS), voitures IA retenues pendant qu'un invité court, participation
      de l'invité gardée d'une session à l'autre, prix des courses personnels.
      Reste : course sur glace
      complète (grille, manches, résultats, pilotes humains) (0.30), spectateurs qui remettent une voiture IA sur ses
      roues, kiosque et vendeur de pièces.
- [ ] Plus tard : mods (MSCLoader géré par le lanceur, onglet MODS, comparaison des mods dans le salon).

## Quêtes et argent
- [x] Quêtes communes : si l'invité entame une quête et que l'hôte en remplit les conditions,
      elle se termine et est validée pour les deux (0.6 : boulots rejoués chez tous, sans double paie).
- [x] Revenus partagés (quêtes ou autre source) : chaque joueur reçoit l'argent qui arrive (0.3).
- [x] Porte-monnaie séparés : chacun gère ses achats avec son propre argent (0.3, celui de
      l'invité est gardé d'une session à l'autre).

## Voiture
- [x] Mécanique synchronisée en temps réel : chaque pièce posée/retirée, chaque vis (serrage,
      état, rotation, mal vissée), peinture… tout ce qui sert à refaire la voiture.
      Fait (0.2) : pièces montées/démontées, vis serrées/desserrées cran par cran, pièces portées
      et lâchées. Fait (0.5) : peinture (pièces et carrosserie), réglages à la main (carburateur,
      molettes...). Fait (0.6) : câblage électrique, pare-brise, boutons du tableau de bord.
      Fait (0.8) : liquides (essence, huile, refroidissement, freins, bidons) et usure des pièces.

## Achats et objets
- [x] Chaque joueur achète de son côté ; tous voient les achats des autres (sac de courses…) (0.3).
- [~] Objets visibles dans les mains des joueurs (sac de courses, etc.).
      Fait (0.4) : l'objet porté se voit à sa vraie place et l'avatar a le bras qui porte.
      Fait (0.9) : les objets uniques du monde sans ID (bidons, cric, palan, hache, seaux, lanterne,
      boîtiers de CD, bûches...) sont suivis aussi.

## Personnages
- [x] Apparence : chaque joueur (hôte et invités) choisit un modèle de PNJ existant (F10, 0.1).
- [x] Animations : celles des PNJ (accroupi…) ou faites maison, pour que les joueurs se voient bouger.
      Fait (0.4) : marche avec les bras qui balancent, assis, accroupi, assis au volant.
      Fait (0.14) : accroupi à deux niveaux (squelette abaissé, jambes pliées, à genoux), places passagers
      comme celle du conducteur.
      Fait (0.15) : cigarette en main, main à la bouche quand le joueur tire, fumée quand il souffle.
      Fait (0.10, retours de JD) : pose de conduite des PNJ de la circulation (voiture / camion), tête qui
      suit la caméra au volant, buste qui suit le regard, accroupi procédural, bras au repos.
- [x] Actions visibles : un joueur qui fume, boit, mange… est vu par les autres.
      Fait (0.4) : fumer, boire, porter, saluer. Fait (0.7) : dormir ; (0.9) le temps ne passe vite
      que quand tous les joueurs dorment (chacun récupère sa fatigue), message d'attente sinon. Fait (0.8) : manger (main à la bouche ;
      ce qui est mangé, bu ou jeté disparaît chez tous).

## Lanceur
- [x] Mise à jour automatique depuis les releases GitHub, options, salon (comme VCCoop/SACoop).
      Fait (0.1) : mise à jour, options, journaux. Fait (0.7) : salon (joueurs, tenues, versions, ping,
      PRÊT, choix continuer/nouvelle partie, LANCER démarre le jeu de chacun ; port TCP en plus de l'UDP).
- [x] Nouvelle partie : quand l'hôte lance, un onglet pour choisir la couleur de la voiture,
      avec aperçu 3D (0.6 : onglet VOITURE du lanceur, maillage exporté depuis le jeu du joueur).
- [x] Logo de MWCoop (dessiné par JD) et fond animé du lanceur (0.15.1).
