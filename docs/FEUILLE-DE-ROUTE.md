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
      Reste : marché aux puces (articles tirés au hasard de chaque côté), facture de téléphone des appels
      passés par un invité, appels quand l'hôte est loin de la maison (la logique du téléphone ne tourne
      que près de la maison, chez l'hôte).

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
