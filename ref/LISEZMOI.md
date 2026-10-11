# Référence du jeu, hors jeu

Un vidage **complet** du niveau GAME : chaque objet (inactifs compris), ses composants, chaque automate PlayMaker
avec ses états, transitions, **actions et leurs paramètres**, et ses variables. Il sert à comprendre le jeu
(qui écrit `PlayerMoney`, qui envoie tel événement, que fait tel PNJ) **sans lancer d'instance**.

Le contenu de ce dossier vient du jeu (propriété d'Amistech) : **jamais dans le dépôt**. Seul ce fichier y est.

## Contenu

`ref\<version du jeu>\` :

| Fichier | Quoi |
|---|---|
| `GAME-complet.txt` | le vidage (une sauvegarde de test en cours de partie, MSCLoader coupé) |
| `globals.txt` | les variables globales PlayMaker et leurs valeurs |
| `automates.tsv` | ligne, chemin, automate, actif, états |
| `variables.tsv` | variable (G = globale) → automate, état, action qui l'utilisent |
| `evenements.tsv` | événement → qui l'envoie (action), qui y réagit (transition) |

## Chercher

```
rg "PlayerMoney" ref\<v>\variables.tsv          # tout ce qui touche à l'argent
rg "^SPAWNITEM\t" ref\<v>\evenements.tsv         # qui envoie / reçoit un événement
rg -n "FSM 'Use'" ref\<v>\GAME-complet.txt | rg -i sausage
```

Puis lire `GAME-complet.txt` autour de la ligne donnée (Read avec offset). Avec `grep`, ajouter `-a` : quelques
textes du jeu contiennent des octets nuls et grep croirait le fichier binaire.

Les `SendEvent` disent à qui ils parlent : `cible:GameObjectFSM <objet> fsm=<automate>`, `cible:Self`,
`cible:BroadcastAll`…

Attention : les automates déjà modifiés par MWCoop (actions injectées) y apparaissent avec des actions de type
`MWCoop...` : ce ne sont pas celles du jeu. Le vidage est une photo : l'état actif et les valeurs sont ceux du moment.

## Refaire (nouvelle version du jeu)

1. Une instance à part (pas `MWCoop-Joueur*`, que `run\test.ps1` ferme) : `D:\Games\COOPTEST\My Winter Car\MWCoop-Ref`,
   `mwcoop.ini` : `[Coop] Mode=solo`, `[Test] Profil=Ref, ArrierePlan=1, PasserIntro=1, Continuer=1, Vidage=2, VidageDelai=45`.
2. Lancer `mywintercar.exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 -mscloader-disable`.
3. Copier `profils\Ref\dumps\GAME-complet.txt` et `globals.txt` dans `ref\<version>\`, puis
   `python run\ref-index.py ref\<version>`.
