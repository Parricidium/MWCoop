# Feuille de route MWCoop

Liste de JD (02/10/2026). Le principe : tout ce qu'un joueur peut faire en solo, un invité doit
pouvoir le faire, et les autres doivent le voir.

## Monde
- [~] Monde synchronisé : météo, PNJ, circulation, interactions avec les PNJ, voix des PNJ.
      Fait (0.1) : heure, jour, météo (nuages, pluie, température) de l'hôte.
- [~] Portes, lumières, interrupteurs : ce qu'un joueur ouvre ou allume, les autres le voient.
      Fait (0.1) : portes des bâtiments, interrupteurs, télé, lecteur CD. Reste : état à l'arrivée d'un invité.
- [ ] Toutes les actions du solo possibles pour les invités : gratter le pare-brise, boutons,
      pénalités, utilisation des objets…

## Quêtes et argent
- [ ] Quêtes communes : si l'invité entame une quête et que l'hôte en remplit les conditions,
      elle se termine et est validée pour les deux.
- [x] Revenus partagés (quêtes ou autre source) : chaque joueur reçoit l'argent qui arrive (0.3).
- [x] Porte-monnaie séparés : chacun gère ses achats avec son propre argent (0.3, celui de
      l'invité est gardé d'une session à l'autre).

## Voiture
- [~] Mécanique synchronisée en temps réel : chaque pièce posée/retirée, chaque vis (serrage,
      état, rotation, mal vissée), peinture… tout ce qui sert à refaire la voiture.
      Fait (0.2) : pièces montées/démontées, vis serrées/desserrées cran par cran, pièces portées
      et lâchées. Reste : peinture, réglages (carburateur...), liquides, usure.

## Achats et objets
- [x] Chaque joueur achète de son côté ; tous voient les achats des autres (sac de courses…) (0.3).
- [~] Objets visibles dans les mains des joueurs (sac de courses, etc.).
      Fait (0.4) : l'objet porté se voit à sa vraie place et l'avatar a le bras qui porte.

## Personnages
- [x] Apparence : chaque joueur (hôte et invités) choisit un modèle de PNJ existant (F10, 0.1).
- [x] Animations : celles des PNJ (accroupi…) ou faites maison, pour que les joueurs se voient bouger.
      Fait (0.4) : marche avec les bras qui balancent, assis, accroupi, assis au volant.
- [~] Actions visibles : un joueur qui fume, boit, mange… est vu par les autres.
      Fait (0.4) : fumer, boire, porter, saluer. Reste : manger, dormir.

## Lanceur
- [~] Mise à jour automatique depuis les releases GitHub, options, salon (comme VCCoop/SACoop).
      Fait (0.1) : mise à jour, options, journaux. Reste : salon.
- [ ] Nouvelle partie : quand l'hôte lance, un onglet pour choisir la couleur de la voiture,
      avec aperçu 3D.
