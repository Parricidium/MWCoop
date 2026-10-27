# Kupla bêta « petites configs » — mesures du 27/10

[Addons et Tools]. Instances Carte-MWC / Carte-MSC, jeu caché en **1920x1080**, **affinité 2 cœurs** (petit PC simulé sur le PC de JD),
`Autotest=kuplamesure` (28 s par lieu, temps d'image moyen), `JournalPerf=30`, `KuplaCompteur=1`. « rétros » = à la maison, à pied, les 3
caméras des rétroviseurs allumées de force (l'entrée en voiture par autotest ne marche pas sur ces parties : DriveTrigger inactif).
Bruit entre deux runs identiques : ≈ 1 ms sur MWC, ≈ 2 ms sur MSC (navigateur et Discord actifs sur le PC pendant les mesures).

## My Winter Car (ms par image, plus petit = mieux)

| Lieu | sans Kupla | niveau 1 (léger) | niveau 2 (moyen) | niveau 3 (fort) |
|---|---|---|---|---|
| maison (face à un mur) | 15,6 | 16,3 | 15,6 | 16,6 |
| ville (Perajarvi) | 15,1 | 14,2 | **12,9** | 13,2 |
| port | 11,8 | 11,9 | 11,2 | 11,4 |
| rétros forcés | 20,1 | 16,6 | **15,9** | 15,7 |

Petits objets du décor suivis : 2175 ; cachés selon le lieu : 1170-2220. Les rétroviseurs coûtent 4,5 ms sur 2 cœurs (20,1 vs 15,6) :
1 image sur 3 en rend 15,9 (−21 %). La ville gagne 15 % au niveau 2 (petits objets + décor à 2000 m). Niveau 3 n'apporte rien de plus que 2.

## My Summer Car

| Lieu | sans Kupla (2 runs) | niveau 1 | niveau 2 (2 runs) | niveau 3 |
|---|---|---|---|---|
| ville (Peräjärvi) | 18,1 / 16,1 | 14,7 | 15,6 / 16,1 | 15,3 |
| magasin (ou ville bis) | 16,5 / 15,5 | 14,6 | 22,5* / 16,9 | 13,4 |
| rétros forcés | 17,5 / 15,3 | 14,2 | 15,2 / 13,3 | 12,8 |

(*) run bruité (à-coups de 199 ms, charge extérieure). La fenêtre « maison » de MSC tombe juste après le chargement (à-coups 180-200
ms) : retirée. Sur MSC seul le cadencement des rétroviseurs gagne nettement (15,3 → 13,3 ms, −13 %) ; les petits objets du décor
(1184 cachés / 1471 suivis en ville) ne changent rien de mesurable : les maillages de MSC sont plus gros et moins nombreux.

## Sur le PC de JD sans contrainte (1280x720, tous les cœurs, 27/10 midi)
Aucune différence mesurable (écart < bruit) pour aucun réglage : voir `conception-p2.md` §0 et le message à [Directives].

## Vérifications
- Éteint (`KuplaRendu=0`) : `Autotest=kupla` → « module absent de la boucle, aucun rappel camera, rien de change », liste des modules
  sans « kupla » ni « compteur d'images », MWC et MSC (DLL finale).
- 2 joueurs (Joueur5 hôte au volant de la SORBET, Joueur6 invité), Kupla niveau 2 des deux côtés : aucune exception, copie de la
  SORBET et ceinture du conducteur chez l'invité, avatar de l'hôte, rétros cadencés chez l'hôte (11 rendues / 22 sautées à 20 s).
- Relevé des petits objets étalé sur plusieurs images (300 par image) après un à-coup « kupla 32 ms » vu chez l'invité.
- Compteur : capture `ecran-kupla-mwc-n0.png` (« 65 i/s · Kupla éteint » en haut à droite).
