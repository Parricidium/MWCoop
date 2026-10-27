# Chaos : relais « mauvaise connexion » et scénarios de robustesse

[MWC], 27/10/2026. Demande de JD (via [Directives]) : notre propre relais, sans le code de coop-anything.
Outil : `run\chaos.py` (Python 3, bibliothèque standard, 127.0.0.1 seulement). Voie IP seulement : la voie Steam
(relais de Valve) ne passe pas par un port UDP local.

## Mise en place

1. L'hôte (instance Joueur1 / Voitures-MSCoop) écoute comme d'habitude sur son `[Coop] Port` (7871 sur les instances
   MWC de [MWC], 7887 sur MSC).
2. Le relais, lancé caché avant les jeux :
   `python -I run\chaos.py --ecoute 7873 --cible 127.0.0.1:7871 --profil enfer --journal dumps\chaos.jsonl`
3. L'invité (Joueur2) met `[Coop] Port=7873` (et `Adresse=127.0.0.1`) dans **son** ini. Rien ne change dans le mod.
4. Le journal `dumps\chaos.jsonl` porte l'heure murale (celle de mwcoop.log) et `t` = secondes depuis le premier paquet
   de l'invité ; même origine pour `--script`. Un bilan par sens toutes les 5 s (`g2h` invité → hôte, `h2g` hôte →
   invité) : reçus, livrés, pertes, coupures, file (débit), désordre, corruption, doublons.

Avec le script d'essai de [MWC] (scratchpad `essai.ps1`) : `-Relais '<args de chaos.py>' -Coop2 'Port=7873'`, plus
`-Delai s` pour une arrivée tardive de l'invité.

Profils : `lan`, `wifi_moyen` (35 ± 25 ms, 2 % de pertes en rafales, 1 % de désordre), `mobile` (60 ± 30 ms, 1 %,
2 Mbit/s), `transatlantique`, `enfer` (200 ± 150 ms, 8 % + rafales, 10 % désordre, 5 % doublons, 512 kbit/s),
`doublons` (30 % doublons, 30 % désordre à 200 ms), `corruption` (1 % des paquets, un bit inversé). Réglages un par un
avec `--regle cle=val` ; en cours de route avec `--script "T:coupure:S[:sens],T:profil:NOM,T:regle:cle=val"`.
Rejouable : `--graine`.

## Les 7 scénarios (étude coop-anything §2) sur My Winter Car — 27/10/2026

| # | Scénario | Commande (essai.ps1) | Résultat |
|---|---|---|---|
| S1 | `wifi_moyen` en conduisant, 3 min (hôte KEKMET, invité SORBET) | `-Mode conduite -Players 2 -Seconds 190 -Extra2 'TestVoiture=SORBET(190-200psi)' -Relais '... --profil wifi_moyen'` | **PASS** : aucun « délai dépassé », digest des pièces stable (contrôle toutes les 10 s, aucun écart), aucun corps sous la carte, seuls les NaN locaux du tracteur (garde-fou connu) |
| S2 | `enfer` pendant la sauvegarde en jeu de l'hôte (`Autotest=sauver`, SAVEGAME à 60 s) | `-Mode sauver -Players 2 -Seconds 150 -Relais '... --profil lan --script "55:profil:enfer"'` | **PASS** : l'hôte fige 6,7 s pour écrire, l'invité ne le perd pas, rien de dupliqué. Note : sous `enfer` dès le départ, la sauvegarde de 1,5 Mo met 81 s à passer (24 s au débit nu : fenêtre fiable de 192 paquets envoyée d'un coup, file de 500 ms qui déborde, renvois) — ça finit, sans blocage |
| S3 | Coupures hôte → invité de 5, 14 puis 16 s (Timeout 15 s) | `-Mode camera -Players 2 -Seconds 200 -Relais '... --profil lan --script "60:coupure:5:h2g,90:coupure:14:h2g,130:coupure:16:h2g"'` | **PASS après correction** : 5 et 14 s : rien ; 16 s : l'invité perd l'hôte proprement, se reconnecte 3 s après, reçoit la sauvegarde et reste en jeu. Avant correction : pair fantôme 15 s chez l'hôte (voir bugs) |
| S4 | Doublons + désordre forts sur le canal fiable (commande d'une annonce par téléphone) | `-Mode appel -Players 2 -Seconds 120 -Extra 'TestNumero=annonce5' -Relais '... --profil doublons'` | **PASS** : 2 800 paquets doublés, 1 700 désordonnés ; la commande est créée une seule fois chez l'invité, même numéro, aucun événement rejoué deux fois |
| S5 | `corruption` à 1 % (un bit inversé, en-tête compris) en conduisant | `-Mode conduite -Players 2 -Seconds 120 -Relais '... --profil corruption'` | **PASS** : 90 paquets corrompus ; le décodeur rejette (« texte connu inconnu », « vecteur aberrant », « message tronqué »), aucun plantage, aucun NaN. Note : un bit inversé qui laisse une valeur plausible passerait (pas de somme de contrôle dans le mod) ; en vrai, la somme de contrôle UDP arrête ces paquets |
| S6 | `mobile` (2 Mbit/s) pendant l'arrivée tardive d'un invité (sauvegarde) | `-Mode camera -Players 2 -Seconds 170 -Delai 40 -Relais '... --profil mobile'` | **PASS** : 1,5 Mo reçus en 8 s, invité en jeu 16 s après sa connexion, aucun blocage (les mods ne passent pas par la voie IP : ModServe est Steam seulement) |
| S7 | Rendre une voiture conduite sous `enfer` (invité conduit le tracteur, coupe, sort ; hôte à 1,9 km) | `-Mode conduite -Players 2 -Seconds 150 -Extra1 'EntreeA=9999','PousseDebut=9999','PousseFin=9999' -Extra2 'TestSortie=1' -Relais '... --profil lan --script "70:profil:enfer"'` | **PASS** : « rendue par #1 : état du moteur repris » 2 s après la sortie, voiture reprise à 2 m près, pas d'envol (vitesse verticale et rotation sans pointe) |

Trafic mesuré en jeu (2 joueurs, lan) : hôte → invité ≈ 50 kbit/s en régime établi, invité → hôte ≈ 25 kbit/s.

## Bugs trouvés et corrigés (main, 27/10)

1. **Hôte qui charge > 15 s** (Continuer / Nouvelle partie : 24 s sur cet essai) : l'invité au menu le tenait pour
   parti (« délai dépassé »), se reconnectait, et l'hôte lui envoyait la sauvegarde deux fois. Correction :
   `Session.AnnounceLoading` (Msg.Loading, Flow.cs) juste avant le chargement ; l'invité tolère 60 s (`Transport.Grace`).
2. **Pair fantôme à la reconnexion en IP** : l'invité revient d'un port éphémère neuf, donc d'une autre adresse ; l'hôte
   gardait l'ancien pair 15 s (avatar fantôme, PNJ à lui, sauvegarde envoyée deux fois). Correction : identifiant
   d'instance (par processus) dans T_CONNECT ; même instance à une autre adresse → ancien pair remplacé tout de suite
   (« pair remplacé (nouvelle adresse) »). Compatible : un hôte d'avant ignore les octets en plus.

Pistes non traitées (pas des bugs) : pas de régulation de débit sur le canal fiable (la fenêtre de 192 paquets part
d'un coup : sous 512 kbit/s un gros transfert renvoie beaucoup) ; un invité déjà en jeu qui se reconnecte reçoit de
nouveau la sauvegarde (1,5 Mo) et fait figer l'hôte 6,7 s pour l'écrire.

## Tour de [MSC]

Mêmes scénarios sur les instances MSC (Voitures-MSCoop / MSCoop2, hôte sur 7887) : `--cible 127.0.0.1:7887`,
`[Coop] Port=<écoute>` chez l'invité. Les autotests `sauver`, `conduite`, `appel` et `camera` existent sur MSC
(`mscsauve` pour la sauvegarde en place ES2). Point à vérifier en plus sur MSC : corps rigides coupés / repris
(Unity 5.0, poses périmées des corps enfants) après une coupure de 16 s.
