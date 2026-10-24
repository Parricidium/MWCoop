# Voyage (mod MSCLoader pour My Winter Car, compatible MWCoop)

Une agence de voyage à Peräjärvi vend des billets aller-retour vers des **circuits d'Assetto Corsa que le joueur a
convertis lui-même** avec le convertisseur Voyage. À l'aérodrome, près du cinéma, le guichetier demande si tu utilises
ton billet : oui, c'est le vol (le Cessna décolle, fondu au noir), puis l'arrivée au circuit, hors de la carte du jeu.
Un guichet du retour, posé à côté du point d'arrivée, te ramène en Finlande.

Le mod ne contient aucun circuit. Chaque joueur convertit les circuits de son propre Assetto Corsa, sur son PC. Rien
n'est envoyé ni partagé, et le mod ne contourne aucune protection : les fichiers chiffrés sont refusés.

Mod à part, séparé de MWCoop : ses mises à jour n'y touchent pas. Plus tard, il pourra rejoindre MWCoop comme le musée
(ajout COOP au choix) ou devenir un addon téléchargeable.

## Installer

1. Compiler le mod : `build.cmd` (il faut MSCLoader dans le jeu ; sortie `build\Voyage.dll`, environ 2 Mo avec le Cessna).
   Le Cessna est embarqué dans la DLL : `outils\glb2voyage.py` convertit le `.glb` en `modeles\cessna.vmesh` et ses
   textures (`python -I outils\glb2voyage.py plane.glb modeles cessna --a-part propelting`).
2. Copier `build\Voyage.dll` dans le dossier `Mods` du jeu
   (`C:\Program Files (x86)\Steam\steamapps\common\My Winter Car\Mods`).
3. Compiler le convertisseur : `dotnet build -c Release convertisseur` (sortie `build\convertisseur\ConvertisseurVoyage.exe`
   et son dossier `ui\`). Il faut le runtime WebView2 de Microsoft Edge, présent sur Windows 10 et 11.
4. En co-op, le lanceur de MWCoop propose le mod aux invités, comme les autres mods de l'hôte.

## Convertisseur de circuits

Fenêtre sombre comme l'éditeur de carte (WinForms + WebView2, interface dans `convertisseur\ui`), en français, anglais,
russe et arabe. Il trouve Assetto Corsa et My Winter Car dans les bibliothèques Steam ; sinon, on choisit les dossiers
(retenus dans `%LOCALAPPDATA%\MWCoop\convertisseur\convertisseur.ini`).

- **Bibliothèque** : les circuits de `assettocorsa\content\tracks`, avec leur image et leur tracé, la longueur, le nombre
  de stands et la taille. On peut chercher (nom, pays, auteur), filtrer (à convertir, convertis, officiels, mods) et trier.
- **Conversion** : choix du tracé (pour les circuits qui en ont plusieurs), du nom affiché à l'agence, du prix du billet
  (d'office : 300 mk + 60 mk par km, arrondi à 50), du point d'arrivée (stand, grille de départ, tour lancé), de la
  taille des textures (512, 1024 ou 2048 px) et du décor. Une barre de progression montre les étapes et le journal ;
  on peut annuler. Magione prend moins d'une seconde, Highlands environ une seconde et demie.
- **Destinations** : la liste à gauche, avec un point de couleur pour leur état. **À jour**, **à reconvertir** : le
  circuit a changé, ou le convertisseur a été amélioré (`Conversion.Version`). **Circuit introuvable** : la destination
  reste jouable. Chaque destination a :
  - un aperçu 3D (three.js) qui lit les fichiers du jeu, avec trois vues : vue d'ensemble, point d'arrivée avec la
    flèche du cap, et collisions ;
  - sa fiche à modifier : nom, prix, point d'arrivée ;
  - ses chiffres ;
  - trois actions : reconvertir, ouvrir le dossier, supprimer (dans la Corbeille).
- **Ligne de commande** (essais) : `ConvertisseurVoyage.exe --convertir <dossier du circuit> [--config <tracé>]
  --sortie <dossier des destinations> [--textures 1024] [--point pit|start|hotlap]`, journal dans `conversion.log`.

### Ce que fait la conversion (`convertisseur\src\Conversion.cs`)

- Lit les `.kn5` du tracé (`models_<tracé>.ini`, sinon `<id>.kn5`) : maillages, matières, textures.
- **Affichage** : les maillages visibles, dans leur version détaillée seulement, regroupés par texture et découpés en
  morceaux de 60 000 sommets au plus. Les aides du jeu ne sont pas gardées : équipe des stands (`AC_CREW`), repères
  `AC_PIT_` et `AC_START_`, feux de départ `KS_START_LIGHT` de `100.kn5`.
- **Repère** : Assetto Corsa est en repère droit, Unity en repère gauche. X est retourné et les triangles sont inversés ;
  sans ça, tout serait en miroir (les numéros des stands se liraient à l'envers).
- **Textures** : DXT1 et DXT5 gardées telles quelles, BGRA recopié, 16 bits (ARGB 4444, RGB 565) décodé puis recompressé
  en DXT1. Les plus grands niveaux de détail au-delà de la taille choisie sont sautés.
- **Matières multicouches** (`ksMultilayer` : herbe, terre, roche) : la texture de base est presque blanche ; la couleur
  vient de quatre textures de détail mélangées par un masque. Le convertisseur « cuit » une texture par matière :
  base × somme des couleurs moyennes des détails, pondérées par le masque. Les couleurs sont justes, le grain fin des
  détails est perdu.
- **Collisions** : les maillages physiques nommés d'après `data\surfaces.ini` (`1ROAD`, `08WALL`…), invisibles. Si un
  circuit n'en a pas, ce sont les maillages affichés.
- **Arrivée** : `AC_PIT_0`, `AC_START_0` ou `AC_HOTLAP_START_0`. Le cap est vérifié avec la trajectoire de l'IA
  (`ai\fast_lane.ai`).
- Le résultat est écrit dans `<jeu>\Mods\Assets\Voyage\destinations\<id>\` :
  - `destination.ini` : la fiche lue par le mod (nom, pays, auteur, prix, arrivée, cap, rayon, empreinte, source) ;
  - `circuit.vmesh` : les maillages ;
  - `textures\N.vtex` ;
  - `apercu.png` et `plan.png` ;
  - `infos.json` : les chiffres et les points d'arrivée possibles.

  La conversion est écrite dans `<id>.nouveau`, puis échangée avec l'ancienne : un échec garde l'ancienne.

## En jeu

| Lieu | Où | Ce qu'on y fait |
|---|---|---|
| Agence MATKATOIMISTO | Peräjärvi, à l'est du musée de la Corse (MWCoop), au sud de la petite route (-1535, 1080) | une affiche par destination (8 au plus, « +N » au-delà) : clic = billet aller-retour à son prix |
| Aérodrome LENTOKENTTÄ | champ au nord du cinéma, allée depuis la route (-380, 1668) | « PARLER » au guichet, puis clic = oui, regarder ailleurs = non ; le tableau des départs liste les destinations |
| Circuit | hors de la carte, sur un cercle de 25 km autour du jeu (16 places, d'après l'identifiant : la même chez tous les joueurs) | guichet du retour à côté du point d'arrivée, avec le crédit de l'auteur du circuit |

Les places de l'agence et de l'aérodrome ont été choisies sur un relevé du terrain (hauteurs, déclencheurs, automates).
Il n'y a aucun boulot, course ni événement à l'aérodrome. Le plus proche est le radar de police du bord de route
(Ratsia3Fields), à 110 m de l'allée. L'agence est à 42 m du bois de HouseWood4 et à 76 m de l'arrêt de bus.

- **Destinations** : lues au chargement de la partie (les fiches seulement). Le circuit choisi n'est chargé que pendant
  le noir du vol (Magione : 1,9 s ; Highlands : 2,3 s) et il est déchargé au retour. Sans destination, l'agence affiche
  « AUCUNE DESTINATION / Convertis un circuit ».
- **Vol** : au départ, une caméra au bord de la piste montre le Cessna qui roule, décolle et s'éloigne (hélice qui
  tourne, son « propeller1 » du jeu, celui de l'hydroglisseur) ; puis le noir. Au retour, après le noir, on le voit
  arriver de l'ouest (au-dessus des silos MAATALOUS), se poser et freiner, puis on est dans le terminal. L'avion revient
  se garer sur l'aire 40 s après un départ.
- **Argent** : le billet se paie en liquide (PlayerMoney). Avec MWCoop, c'est une dépense ordinaire, qui reste au joueur.
  Si le circuit a été supprimé entre l'achat et le vol, le billet est remboursé.
- **Co-op** : chacun prend et utilise son billet ; le décollage et l'atterrissage sont annoncés aux autres (API de
  MWCoop, canal « Voyage ») : ils voient l'avion partir et revenir. Pour aller ensemble au même circuit, chaque joueur
  doit l'avoir converti (même identifiant, donc même place). Les positions sont déjà partagées par MWCoop. L'heure
  n'avance pas pendant le vol (elle est commune).
- **Sauvegarde** : le billet et la destination où l'on se trouve sont gardés (SaveLoad de MSCLoader, clés `billet2` et
  `ailleurs2` ; clé suivie du pseudo pour un invité). En chargeant une partie sauvée sur un circuit, le circuit est
  rechargé. S'il a été supprimé, retour à l'aérodrome.
- **Chute** sous le circuit (30 m sous son point le plus bas) : retour au guichet du retour.
- **PNJ** : le corps des PNJ de MWCoop (le vendeur de l'épicerie). Sans MWCoop, c'est une silhouette simple.

## Crédits

- Avion : « Plane » (Cessna 172) par osmosikum, https://sketchfab.com/3d-models/plane-cedc8a07370747f7b0d14400cdf2faf9,
  licence CC BY 4.0. Il est crédité aussi dans le terminal de l'aérodrome, sous le tableau des départs.
- Circuits : ceux d'Assetto Corsa du joueur, jamais livrés avec le mod. Leur auteur (`ui_track.json`, ou Kunos
  Simulazioni pour les circuits d'origine) est affiché au guichet du retour et dans le convertisseur.
- Convertisseur : three.js (licence MIT, `ui\vendor\three-LICENSE.txt`), police Nunito (SIL Open Font License,
  `ui\fonts\OFL-Nunito.txt`), Microsoft Edge WebView2.

## Essais (sans clavier)

Variable d'environnement `VOYAGE_ESSAI`, commandes séparées par `;` (voir `src\Essai.cs`) : `tp`, `argent`,
`destinations` (la liste lue), `acheter [id]`, `embarquer`, `rentrer`, `viser`, `clic`, `main`, `sauver` (sauvegarde en
jeu par MWCoop), `photo`, `photojoueur nom [cap]`, `dessus`, `sonde` (hauteurs), `alentours` (déclencheurs et
automates), `chercher`, `matieres`, `quitter`.
Le journal est dans `<jeu>\Mods\Voyage.log` ; les photos et relevés dans `<jeu>\Mods\voyage-essai\`.

Convertisseur : `CONVERTISSEUR_JEU=<dossier du jeu>` impose le dossier de My Winter Car, `CONVERTISSEUR_HORS_ECRAN=1`
ouvre la fenêtre hors de l'écran ; `--debug` active les outils de développement de WebView2.

## Publication

Publié en « Early » dans MWCoop 1.13.0 (24/10, JD : « pas besoin de test, on fera des mises à jour au fur et à mesure ») :
le lanceur (page Outils, à côté de l'éditeur de carte) télécharge `ConvertisseurVoyage-<version>.convertisseur` (fait par
`dist\make-release.ps1` : convertisseur + `mod\Voyage.dll`) dans `%LOCALAPPDATA%\MWCoop\voyage`. Le convertisseur
installe ou met à jour le mod dans `<jeu>\Mods` (bouton dans sa barre de gauche) et prévient si MSCLoader manque.
Crédit du Cessna dans le lanceur : Guide > Crédits > Modèles 3D (traductions : `run/ar/ar_in_48.tsv`, `ru_launcher.tsv`).

## Reste à faire

- Une voiture de location à l'arrivée (copies de la SORBET, comme les Jokkis de MWCoop) pour rouler sur le circuit.
- Les circuits faits de nombreux `.kn5` très lourds (Nordschleife) : à essayer.
- Textes du mod en russe et en arabe (FR et EN pour le moment ; le convertisseur a les quatre langues).
- Marquer l'aérodrome sur la carte (M) de MWCoop.
