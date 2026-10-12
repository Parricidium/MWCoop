# Video corner guide / Guide du coin vidéo

*Since MWCoop 0.84.0 — [Français plus bas](#français)*

Film your co-op adventures with a 90s VHS camcorder and watch them together on a TV, with sound.

## English

### 1. Go to the shop

The **OSUUSKAUPPA 𝔰21** shop is just west of the PSK (Peräjärvi store) car park. Click the door handle to open the door.

In the shop:

| Item | Price | Where |
|---|---|---|
| VHS tape | 19 mk | top shelf of the white gondola, in the middle of the shop |
| VCR | 390 mk | wooden shelf on the left wall, middle unit |
| Television | 590 mk | on top of that same shelf |
| Camcorder | 890 mk | wooden shelf on the left wall, right-hand unit |

![VHS tapes](img/video/shelf-tapes.jpg)
![VCRs, TV and camcorders](img/video/shelf-vcr-tv-camcorder.jpg)

### 2. Buy

1. Look at an item on the shelf and **left click**: you take one (it disappears from the shelf). **Right click** puts it back.
2. Look at the **till** on the counter: the total shows up. **Click** to pay.
3. Your purchases come in an OSUUSKAUPPA **shopping bag** on the counter. Pick it up like any object.
4. Look at the bag: **F** (short press) takes one item out, **hold F** takes everything out. An empty bag disappears.

To film and watch, you need: **a camcorder, a tape, a VCR and a TV**. You can also use a TV of the game instead of buying one (the TV at home, in the prison, at the retirement home, at Jokke's).

### 3. Put a tape in the camcorder

1. Put the camcorder down (on a table, on the floor).
2. Pick up a tape.
3. Look at the camcorder and **left click**: the tape goes in.

To take the tape out: look at the camcorder (not in your hand) and **hold F**.

### 4. Film

**Camcorder in your hand:**

- **F** = viewfinder: you look through the camcorder (4:3 VHS picture, REC, tape counter, battery). **Mouse wheel** = zoom. **F** again to leave the viewfinder.
- **R** = start / stop recording.

![Viewfinder](img/video/viewfinder.jpg)

**Camcorder set down** (on a tripod-like spot: a table, a shelf...):

- Look at it and press **F** = start / stop recording. It films straight ahead.
- When you drop it, it turns to face where you were looking. Handy to film yourself and your friends.

![Camcorder on a table](img/video/camcorder-on-table.jpg)

Good to know:

- A red light blinks on the camcorder while it records — everybody sees it.
- The sound recorded is what **you** hear (the game around you, the music, the cars...).
- **Every take is added after the previous ones** on the tape, like a real VHS you resume after a stop. Up to 40 takes per tape. One take lasts at most 10 minutes.
- When you stop, the film is sent to the other players in the background (a few seconds per minute of film).

### 5. Watch

1. Put the **VCR** less than 2.5 m away from a **TV**.
2. Pick up the tape, look at the VCR and **left click** to insert it.
3. Look at the VCR: **F** = play / stop. **Hold F** = eject.

The film plays on the nearest TV with its sound, **for all players at the same time**. A blank tape shows static. After the end of the film: static again.

![A film on the TV](img/video/tv-playing.jpg)

### 6. Write a title on the tape

1. Pick up the tape.
2. Open the chat (**T**) and type: `/title Summer 1996` (24 characters max).
3. The title is written on the spine of the tape. `/title` alone wipes it.

### Settings

In `MWCoop\mwcoop.ini`:

```ini
[Boutique]
VideoMinutes=10   ; maximum length of one take
FilmiVauhti=3     ; how fast films are sent to the others (1 KB pieces per frame)

[Touches]
CameraViseur=F        ; viewfinder key
CameraEnregistrer=R   ; record key
```

Films are stored in `MWCoop\profils\<your profile>\videot\` (`.mwv` files, about 4 MB per minute).

---

## Français

### 1. Aller à la boutique

La boutique **OSUUSKAUPPA 𝔰21** est juste à l'ouest du parking de la PSK (le magasin de Peräjärvi). Clic sur la poignée pour ouvrir la porte.

Dans la boutique :

| Objet | Prix | Où |
|---|---|---|
| Cassette VHS | 19 mk | tablette du haut de la gondole blanche, au milieu |
| Magnétoscope | 390 mk | étagère en bois du mur gauche, celle du milieu |
| Télévision | 590 mk | sur le dessus de cette même étagère |
| Caméscope | 890 mk | étagère en bois du mur gauche, celle de droite |

### 2. Acheter

1. Vise un objet en rayon et **clic gauche** : tu en prends un (il disparaît du rayon). **Clic droit** pour le reposer.
2. Vise la **caisse** sur le comptoir : le total s'affiche. **Clic** pour payer.
3. Tes achats arrivent dans un **sac de courses** OSUUSKAUPPA sur le comptoir. Prends-le comme n'importe quel objet.
4. Vise le sac : **F** (appui bref) sort un article, **F tenu** sort tout. Vide, le sac disparaît.

Pour filmer et regarder, il faut : **un caméscope, une cassette, un magnétoscope et une TV**. Tu peux aussi utiliser une TV du jeu au lieu d'en acheter une (la TV de la maison, de la prison, de la maison de retraite, de chez Jokke).

### 3. Mettre une cassette dans le caméscope

1. Pose le caméscope (sur une table, par terre).
2. Prends une cassette en main.
3. Vise le caméscope et **clic gauche** : la cassette entre.

Pour la ressortir : vise le caméscope (posé) et **tiens F**.

### 4. Filmer

**Caméscope en main :**

- **F** = viseur : tu regardes à travers le caméscope (image 4:3 façon VHS, REC, compteur, pile). **Molette** = zoom. **F** pour sortir du viseur.
- **R** = lancer / arrêter l'enregistrement.

**Caméscope posé** (sur une table, une étagère...) :

- Vise-le, **F** = lancer / arrêter l'enregistrement. Il filme droit devant lui.
- Quand tu le lâches, il se tourne vers là où tu regardais. Pratique pour te filmer avec tes amis.

Bon à savoir :

- Un voyant rouge clignote sur le caméscope pendant l'enregistrement : tout le monde le voit.
- Le son enregistré est ce que **tu** entends (le jeu autour de toi, la musique, les voitures...).
- **Chaque prise s'ajoute à la suite des précédentes** sur la cassette, comme une vraie VHS qu'on reprend après un arrêt. 40 prises au plus par cassette, 10 minutes au plus par prise.
- À l'arrêt, le film part chez les autres joueurs en tâche de fond (quelques secondes par minute de film).

### 5. Regarder

1. Pose le **magnétoscope** à moins de 2,5 m d'une **TV**.
2. Prends la cassette, vise le magnétoscope et **clic gauche** pour l'insérer.
3. Vise le magnétoscope : **F** = lecture / stop. **F tenu** = éjecter.

Le film passe sur la TV la plus proche avec son son, **chez tous les joueurs en même temps**. Une cassette vierge donne de la neige. Après la fin du film : de la neige à nouveau.

### 6. Écrire un titre sur la cassette

1. Prends la cassette en main.
2. Ouvre le tchat (**T**) et tape : `/titre Kesä 1996` (24 caractères au plus).
3. Le titre s'écrit sur la tranche de la cassette. `/titre` seul l'efface.

### Réglages

Dans `MWCoop\mwcoop.ini` :

```ini
[Boutique]
VideoMinutes=10   ; durée maximale d'une prise
FilmiVauhti=3     ; vitesse d'envoi des films aux autres (morceaux de 1 Ko par image)

[Touches]
CameraViseur=F        ; touche du viseur
CameraEnregistrer=R   ; touche d'enregistrement
```

Les films sont rangés dans `MWCoop\profils\<ton profil>\videot\` (fichiers `.mwv`, environ 4 Mo par minute).
