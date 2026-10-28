# Showroom (mod MSCLoader, My Summer Car)

Salle de photo hors de la carte (demande de JD, 28/10/2026), à activer ou désactiver dans MSCLoader comme les autres mods.

- **Entrer / sortir** : touche **F8** (réglable dans les réglages du mod). On ressort là où l'on était (à la maison si l'endroit
  est perdu). Pas depuis une voiture.
- **La salle** : 20 × 20 × 7 m, gris foncé, 150 m en l'air au-dessus de (3200 ; -3200), inaccessible autrement. Dedans, le
  soleil et le brouillard du jeu sont coupés, avec une lumière ambiante grise fixe.
- **Tableau** (mur derrière l'arrivée) : un interrupteur par projecteur (1 à 5) et « tout » ; le choix d'une voiture du F10 de
  MWCoop (VAZ-2108 et addons installés, ◀ ▶) et « sortir la voiture au centre ». C'est la vraie voiture de MWCoop : elle
  remplace celle que l'on avait prise au F10.
- Il faut **MWCoop msc-1.15.25 ou plus** (CoopApi 4 : `F10Cars`, `ShowF10Car`) pour les voitures ; sans MWCoop, la salle et les
  projecteurs marchent seuls.
- Sauvegarde du mod : projecteurs allumés, lieu de retour, voiture choisie.

Compilation : `build.cmd` -> `build\Showroom.dll` (Unity et PlayMaker de My Winter Car, MSCLoader de la copie My Summer Car du
lanceur : `%LOCALAPPDATA%\MWCoop\mscloader-mwcoop-msc\Managed`). Installation : copier `Showroom.dll` dans `Mods` du jeu.

Essais cachés : variable `SHOWROOM_ESSAI` = « secondes:commande;… » (`touche`, `bouton=spot0..4|tout|prec|suiv|sortir`,
`tourne=<cap>`, `capture=<nom>` -> `Mods\showroom-<nom>.png`, `fin`).
