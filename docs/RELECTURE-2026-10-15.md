# Relecture de la Nightly (branche refonte, 0.96.0-nightly.14) — 15/10/2026

Quatre relectures en lecture seule, en parallèle : réseau/cœur, monde physique, gameplay, couverture solo → coop.
Règle de JD : tout ce qu'un joueur fait en solo doit être faisable par chaque joueur en coop, et chaque objet / action / quête synchronisé.

Légende gravité : **C** critique (désynchro durable, blocage, perte), **M** majeur, **m** mineur. Les chemins sont relatifs à `src\MWCoop\`.

---

## 1. Résumé

- **Le socle est sain** : transport (ordre, doublons, acquittements groupés, fenêtre), relais hôte (émetteur exclu, `who` imposé, fiabilité suivie), gardes de rejeu (`Replay.Depth` toujours en try/finally), NaN sur Vec/Quat, instantanés d'arrivée (portières, objets, liquides, consommables, taxi, 300D…), sauvegarde de l'hôte avant transfert. Les constats de la relecture du 14/10 ont été corrigés (relais `@`, Parts.Scan, CoopApi, NaN Vec/Quat).
- **12 bugs à corriger en priorité** (section 2), dont 4 viennent des correctifs de la journée (0.95.4 → 0.95.8) : envoi complet des rayons qui ne part plus (**corrigé localement, pas publié**), serrage du moteur compté deux fois, pièce démontée qui suit une pose périmée, aucune protection contre une rafale de démontages (le mécanisme exact du Rivett « qui s'envole »).
- **Couverture solo → coop** : quasi complète pour voitures, pièces, objets, boutiques, PNJ, téléphones, maison, boulots. **21 écarts** recensés (section 4), les plus visibles : course sur glace non partagée, état interne des objets portables `(itemx)` non suivi, arrivant sans l'état des crics / pont / dégâts, feu déclenché par la négligence d'un invité annulé, paie des boulots dépendant de l'ordre d'arrivée des horloges.
- **Réservé à l'hôte** : 657 `IsHost` ; presque tous légitimes (autorité monde). 5 gardes limitent réellement les invités (section 5).

---

## 2. À corriger en priorité (lot 1)

| # | Grav. | Où | Quoi | Correctif |
|---|---|---|---|---|
| 1 | C | `Sync/Parts.cs:469-492, 494-557` | **Rafale de démontages (BREAKOFF)** : si la physique d'un invité s'emballe (copie recalée, NaN, choc), le jeu passe tous ses points en « Remove part », chaque démontage part comme un geste, l'hôte rejoue et relaie → voiture désossée chez tous. Aucun contrôle d'autorité ni de débit. C'est ce qui a détruit le Rivett de Vaydon ; 0.95.6 a corrigé la cause de ce jour-là, pas la porte ouverte. | Classer un démontage « manuel » (pièce en main locale, ou serrage et vis à 0) / « casse » ; une casse ne part que si `VehicleSync.Authority(voiture) == LocalId` ; plafond 3 démontages / s / voiture à l'envoi ET chez l'hôte (journal + recalage). Même plafond pour les poses « poussées ici » (`Props.cs:418-436`). |
| 2 | C | `Net/Transport.cs:253-268, 310-321`, `Net/SteamNet.cs:289`, `Net/Session.cs:137-160` | **Reconnexion Steam bloquée à vie** : hôte figé > 15 s (chargement de GAME, Alt-Tab sans `runInBackground`) → l'invité recrée un Transport (seq 0) ; même SteamId = même clé, le pair existe et est `Accepted` → ACCEPT sans `OnConnected`, numéros non remis à zéro ; le Hello (seq 0) est jeté comme « déjà reçu », les fiables de l'hôte s'entassent dans `early`. Invité « connecté » mais muet pour toujours. (En UDP le port change, ça marche.) | Jeton de session aléatoire dans T_CONNECT ; jeton différent sur une clé connue → `DropPeer` puis nouveau pair. `Application.runInBackground = true` dans `Core`. |
| 3 | C | `Sync/Stock.cs:108-109, 136` | **Envoi complet des rayons jamais envoyé** (régression 0.95.4) : `nextFull` avancé ligne 109, puis `now >= nextFull` toujours faux ligne 136. L'arrivant garde des rayons pleins, un `BoughtAbs` perdu n'est jamais rattrapé. | `full = … && fullRound && …` — **fait localement dans les deux arbres, à compiler/tester/publier.** |
| 4 | C | `Sync/Jobs.cs:654`, `Sync/Wallet.cs:723-729` | **Revenus perso retirés 8 s après n'importe quel rejeu de boulot** : A fait avancer un automate de JOBS, B gagne de l'argent par lui-même dans les 8 s → `Wallet` le prend pour la paie rejouée et le retire. | Supprimer seulement pendant le rejeu (`Replay.Depth > 0` / `applying`), ou seulement pour l'automate et l'état qui versent. |
| 5 | C | `Sync/Wallet.cs:63-68, 328-334`, `Sync/Jobs.cs:558-566, 625-650` | **Paie des boulots (usine, pubs, taxi) en double ou perdue** : compteurs de travail recopiés par `WriteVars` (travail commun), paie versée selon l'ordre d'arrivée des horloges (« déjà fait ici » dans les 10 s → paie double ; sinon la paie de l'invité va à l'hôte). | Ne plus rejouer les transitions vers les états de `Pays` ni recopier leurs compteurs (chacun son travail, sa paie) ; ou l'hôte calcule et attribue à qui a travaillé. |
| 6 | M | `Sync/Parts.cs:542-543, 226-236` | **Serrage compté deux fois** (0.95.5) : le moteur reçoit la `Tightness` absolue de l'envoyeur, puis chaque vis rejouée ajoute +1 → après dévissage complet le serrage reste > 0 chez le receveur, pièce indémontable. Et `ApplyBolt` renvoie vrai même si la cible n'est pas atteinte (automate occupé) : vis moins serrée, jamais réessayée. | Remettre en attente si `t.Value != target` ; après les vis rejouées, reposer le serrage absolu de l'envoyeur (ou l'envoyer avec chaque `Bolt`). |
| 7 | M | `Sync/Props.cs:502-509, 448-454, 554-587` | **`HoldFromRemote` suit une pose périmée** (0.95.4) : pièce démontée → cinématique avec `RemoteBy`, et `Follow` applique l'ancien `Pos/Rot/RelCar/HeadBy` → téléportation (> 3 m) ou glissement ; sans pose de chute, lâchée à 1,5 s avec l'ancienne `Vel`. | Dans `HoldFromRemote` : `Pos = body.position`, `Rot = body.rotation`, `Vel = 0`, `RelCar = -1`, `HeadBy = -1`, `Unride(p)`. |
| 8 | M | `Sync/Props.cs:551, 891, 497` | **Vitesse périmée à la libération** : `SetKinematic(false)` remet `p.Vel` (vitesse de la main, 1-3 m/s) sur une pièce qu'on vient de monter → pousse la voiture par le joint. | `p.Vel = 0` avant `SetKinematic(p, false)` dans la branche montée, `ReleaseRemote` et le repli 1,5 s si `Mounted(p)`. |
| 9 | M | `Sync/VehicleSync.cs:1230-1238`, `Sync/Parts.cs:405-415` | **`CalmAfterPart` ne trouve pas la voiture** pour un point porté par une pièce (clé « VIN514B1/… ») : pas de bridage, pas d'exclusion `CalmUntil` → le sursaut au montage d'un piston est pris pour une poussée, le joueur prend la main. | Passer le Transform du point, `VehicleSync.CarRoot(p.Fsm.transform)`, en local et à la réception. |
| 10 | M | `Sync/Jobs.cs:628, 421` | **Clic d'un autre joueur jeté par « déjà fait ici »** : phares Off→On par le conducteur, puis rallumés par le passager < 10 s → ignoré chez le conducteur (clé `Off\|USE\|On` dans `LocalRecent`). Idem essuie-glaces, molettes. `WorldFsms` exempte les clics (`:776-780`), `Jobs` non. | Sauter le test quand `j.Control` et `prev` ∈ `ClickStates`/`InputStates`/`KeyStates`. |
| 11 | M | `Sync/Shop.cs:1028-1052`, `Net/KeyTable.cs:38-44`, `Sync/Paint.cs:138-149`, `Net/SaveTransfer.cs:191-193` | **Messages fiables sans garde de taille** (même famille que les rayons de CBetoy) : achat (`Carried` complet, quantités à 0 comprises), définitions de clés `@sonk/@emitk` (12 clés × ~95 o), peinture (toutes les variables), en-tête de sauvegarde (≥ ~45 fichiers → `SaveInPlace` en boucle, jeu figé). | Borner par octets / découper ; `try/finally` sur `waiting`. |
| 12 | M | `Sync/Jobs.cs:187, 444` | **Tirage au sort jamais diffusé** quand il découle d'un rejeu : l'invité garde son tirage sans l'envoyer, l'hôte rejoue, tire autre chose pendant `Depth > 0` et n'envoie rien → boulot désynchronisé pour de bon. | Chez l'hôte, diffuser (émetteur compris) les transitions sortant d'un `RandomStates` même si `Depth > 0`, ou recalage après rejeu. |

---

## 3. Autres bugs par domaine

### 3.1 Réseau / cœur
- **M** `Net/Transport.cs:122-126`, `Net/Session.cs:241-248, 344-348` — exclusion / refus de version : un seul BYE non fiable puis `Forget` immédiat (Steam : `CloseP2PSessionWithUser`) ; BYE perdu → l'invité réessaie à 3 s, boucle sans fin pour une version différente. Envoyer le BYE 3-5 fois, différer `Forget` d'1 s, liste noire courte avec T_REJECT.
- **M** `Sync/Events.cs:85-100, 194` — automate arrêté chez l'invité dès la réservation, alors que l'hôte ne le pilote pas toujours (initialisé chez l'un pas l'autre, ou déjà pris par un autre module) : porte / fête / lumière figées. N'arrêter qu'au premier message de l'hôte ; relever périodiquement.
- **m** `Recon.cs:435-444`, `Events.cs:95-96`, `Knobs.cs:71-72` — clés `Twin` : correctif partiel, ancêtres homonymes → « premier libre » selon l'ordre d'initialisation ; `GetSiblingIndex` bouge quand des pièces sont remontées. Rang par niveau comme `ChainKey`.
- **m** `Codec.cs:67` — `F32()` jamais vérifié : `Admin.cs:47` (Summon → téléport NaN), `Jobs.cs:1561/1371` (`@benne`, `Clamp` laisse passer NaN), `Knobs.cs:111/123`, `Events.cs:252`, `CarDoors.cs:767/784` (couple NaN → portière NaN), `Garage.cs:325/333/512/518`, `Tow.cs:350/420-429`, `PlayerSync.cs:242` (Yaw → ragdoll NaN), `Fluids.cs:243`. Quaternion nul (0,0,0,0) accepté par `Quat()`. Ajouter `F32Finite(min,max)`, rejeter |q| < 0,5.
- **m** `Game.cs:84-96`, `Consume.cs:111` — restauration après sauvegarde en jeu au niveau de rejeu 0 : les articles « finis » renvoient leur fin à tous à chaque `SaveInPlace`. Entourer de `Replay.Depth++/--`.
- **m** `Net/SaveTransfer.cs:77` — sauvegarde « fraîche » < 60 s envoyée sans re-sauver : ce qui a changé entre 25 et 60 s manque à l'arrivant. Fenêtre 25 s, ou re-sauver si un module a envoyé.
- **m** `Net/Transport.cs:143-152 vs 181-197` — un fiable part tout de suite, les non fiables de la même image au `Flush` → la « finale » (Knobs, `@drone`) peut être dépassée par le flux. Vider le lot non fiable du pair avant un envoi fiable.
- **m** `Sync/Knobs.cs:95-99, 113-116` — renvoi hôte toutes les 5 s d'une valeur en retard pendant la manœuvre d'un invité → commande qui recule 5 s. Sauter les commandes modifiées par un tiers depuis < 1 s.
- **m** `Sync/Particles.cs:198-213` — changement tombé sur le tour complet envoyé en non fiable : feu de véhicule perdu 5 s.
- **m** `Net/KeyTable.cs:19`, `WorldFsms.cs:507` — `KeyTable.Clear()` jamais appelé (table numéro→clé gardée entre deux hôtes : mauvais son/particule) ; `worldLogic` non vidé (fuite).
- **m** `Sync/Stock.cs:263, 126-135, 195, 192-198, 55-59` — écart d'invité ignoré si magasin inconnu de l'hôte ou clé absente après réassort (`SentBought` périmé → « -6 ») ; valeur absolue de l'hôte qui écrase une prise locale non encore envoyée ; panier d'un joueur parti jamais rendu au rayon.

### 3.2 Monde physique
- **M** `Sync/Parts.cs:515-518, 268`, `Paint.cs:58`, `Garage.cs:801` — tout ce qui arrive **pendant le chargement de l'arrivant** (30-90 s) est perdu : pièces montées, vis (file vidée par `OnLevelLoaded`), peinture, atelier → différences définitives. Mettre en file et rejouer après le relevé.
- **M** `Sync/Garage.cs:285-309, 487-507, 604-633`, `PlayerSync.cs:256-266` — **pas d'instantané atelier** pour l'arrivant : crics, pont, bras du palan, crics dépliés, chargeur, tôles, trains tordus (seul le crochet du palan est renvoyé toutes les 5 s). Voiture sur cric chez l'hôte → chez l'arrivant elle tombe puis saute toutes les 0,5 s. `Garage.ScheduleSnapshot(peer)` + renvoi 5 s.
- **M** `Sync/VehicleSync.cs:907-912, 251, 881, 1977` — pose finale en rendant une voiture / fin d'attelage / fin de poussée envoyée **non fiable** (le commentaire dit « fiable ») ; perdue → copie qui dérive, hôte qui téléporte le joueur garé. `Send(c, 0)` fiable.
- **M** `Sync/VehicleSync.cs:2198-2211` — pose « garée » (mode 0) en retard appliquée à une copie conduite par un autre → à-coup, protections basculées. Ignorer si `Remote(c) && c.RemoteBy != who`.
- **m** `Props.cs:856-858, 958, 551`, `Garage.cs:433-447`, `Parts.cs:501-506, 535` — bornes trop larges hors VehicleSync (1e6 m / 1e6 m/s acceptés) ; fonction commune `Sane(pos, vel)` (< 50 km, < 100 m/s ; pose relative au point < 2 m pour Parts).
- **m** `Props.cs:885-893` — garde `Mounted()` avale toutes les poses si le montage a divergé (« point introuvable », message perdu au chargement) : pièce montée pour toujours chez l'un, portée chez l'autre. Si un état 1 arrive pour une pièce montée ici depuis > 2 s → recalage demandé à l'hôte.
- **m** `VehicleSync.cs:2202` — seuil de recalage 1 m / 10° trop large pour une voiture en travaux (moteur au palan jamais à 5 cm des détecteurs, pièces à côté de la voiture). Seuil 4 cm / 1,5° quand `CalmUntil` récent ou joueur < 3 m.
- **m** `VehicleSync.cs:973-988`, `Tow.cs:536-538` — voiture tirée à la corde : `DrivenHere`/`Authority` ignorent `Tow.Carries` → contenu du coffre pas transporté, glisse chez les autres.
- **m** `VehicleSync.cs:1199-1211, 1263-1267` — `ProtectJoints` restaure un `breakForce` périmé. Ne remettre que si encore Infinity.
- **m** `Parts.cs:544-548` — `Destroy` différé du joint du palan (existe encore pendant INSTALL) : `DestroyImmediate`.
- **m** `VehicleSync.cs:2167, 832-836` — deux joueurs poussant la même voiture garée : un tiers la voit sauter. Départager (plus petit id).
- **m** `Props.cs:399-408` — objets rangés (STORED) jamais libérés au départ du joueur : cachés pour tous.
- **m** `Garage.cs:84` — appareils relevés deux fois seulement : cric/palan apparu plus tard jamais suivi.
- **perf** `ModTowCart.cs:57-70, 83` (`GetComponentsInChildren` chaque image), `VehicleSync.cs:213-228` (`Hitched()` 20×/s), `Props.cs:409-436` (`HostResends` sur 1000+ objets avec lancers de rayons, pic par seconde), `Paint.cs:188` (`KeyOf` à chaque message).

### 3.3 Gameplay
- **M** `Sync/Jobs.cs:654-665` vs `WorldFsms.cs:789-794, 834` — le rejeu d'un boulot ne protège pas les globales `Player*` (stress, fatigue, ivresse, dépenses) ; seuls les revenus sont filtrés (la fendeuse restaure à la main, `Jobs.cs:1021-1040`). Équivalent `SavePersonal`/`RestorePersonal`.
- **M** `Sync/Wallet.cs:708, 242-262` — porte-monnaie de l'invité écrit toutes les 10 s seulement, pas à la fin de session : achat puis départ < 10 s → argent dupliqué ; paie juste avant de partir perdue. `Store()` à chaque changement et à `OnLevelLoaded`.
- **M** `Sync/Npcs.cs:604-606` — à 3+ joueurs, un départ fait `owners.Clear()` chez tous les invités : pendant 5 s le client du taxi mené par A est désynchronisé. Ne retirer que le joueur parti.
- **M** `Sync/ModSecondMachtwagen.cs:77-78, 129-136, 153` — achat d'un invité jamais envoyé s'il précède l'état de l'hôte (`last = Read()` contient déjà `Own`) : voiture rachetable. `last = s`.
- **M** `Sync/ModRam1500.cs:23` — achat au parc non synchronisé (parc tiré au hasard chez chacun) ; en-tête périmé (cellule, radio, givre, peinture sont faits ; compteur kilométrique non).
- **M** `Sync/ModJonne.cs:98-99, 235-283` — cuisson lancée par un invité : fenêtre de 9 s après le geste, le plat fini au-delà n'est pas envoyé ; nouvel objet « loose » détruit à 6 s. Autorité par objet.
- **m** `Wallet.cs:385-395, 746, 61-62` — Kela / aide au logement « au plus haut » : > 30 s d'écart (invité en chargement) → versement double ; allocation reçue sans avoir posté la lettre. Trace par jour de jeu.
- **m** `Wallet.cs:770-787, 844-848` — enveloppes : paie au premier clic, pas à qui a fait le boulot ; deux clics à quelques ms → payé deux fois.
- **m** `Calls.cs:405-410` — deux commandes de pièces simultanées : même `ObjectNumberInt`, colis en collision.
- **m** `Wallet.cs:114-115`, `Home.cs:669` — don à un joueur parti débité sans retour ; crédit des puces reçu hors jeu jeté.
- **m** `Wallet.cs:263, 692-701` — 15 premières secondes de l'invité avec l'argent de l'hôte : dépense effacée par `Restore()`.
- **m** `Consume.cs:1061-1071` — deux joueurs mangent le même objet à moins d'une latence : chacun joue « Eat ».

---

## 4. Couverture solo → coop

**H** hôte fait autorité · **R** action rejouée chez les autres · **A** autorité variable · **P** propre à chacun · **∅** rien.

| Domaine | Module(s) | Méc. | Arrivant | Écarts |
|---|---|---|---|---|
| Boulots (générique, JOBS sauvegardés) | Jobs | R, tirages H | sauvegarde | tirages hôte seul (`Jobs.cs:443-444`) ; pause 30 s au-delà de 5 changements/10 s (`:458-472`) |
| Taxi | Jobs, TaxiCustomer, Npcs, MapPanel, Wallet | R + H | oui | — |
| Fosses septiques | Jobs `@tuyau`, Fluids, SewageHose, Events | A (GIFU) | oui | — |
| Bois (billot, fendeuse, plateau, benne) | Jobs, LogPiles, CutterHitch, Fires | A / H | partiel | compteur du plateau : `BedAuthority()` compte (`Jobs.cs:1455-1463`) mais seul l'hôte envoie `@tas` (`LogPiles.cs:46, 104`) |
| Kilju, Jokke | Home, Wallet | A | oui | — |
| Usine, tracts, oncle, grand-mère | Jobs, Wallet | R | sauvegarde | paie (§2 n°5) |
| Vente Rivett, fête Jokke | CarSale, Events | R / H | oui (10 s) | — |
| Magasins (PSK, Fleetari, bar, CT, puces, resto, café, bus) | Shop, Stock, Boxes, Machines, FuelPrices | R, argent coupé au rejeu | oui (60 s) | kiosque à saucisses et vendeur rallye non pris (`Shop.cs:89-90`) ; ventes aux puces hôte seul (`Wallet.cs:541-598`) |
| Boutique OSUUSKAUPPA (ajout) | Kauppa*, SleepingBags | H | oui | dépend de l'interrupteur de l'hôte |
| Objets et accessoires | Props, Consume, Drinks, HandTools, Boxes, Packages, Wear | A, hôte recale | oui (20 s) | sans ID hors `WorldRoots` ignorés (`Props.cs:128-166`) ; état interne des `(itemx)` non suivi (`WorldFsms.cs:212`, `Jobs.cs:275`) |
| Voiture : pièces, vis, peinture, réglages, liquides, câblage, VIN, pare-brise | Parts, Paint, Settings, Fluids, Vin, Windshields, Jobs | R, disputes H | oui | (§2, §3.2) |
| Atelier : crics, pont, palan, chargeur, tôles, trains | Garage | A | **non** (sauf palan) | §3.2 |
| Couleur / peinture du lanceur | CarColor, CustomPaint | H | oui | choix de l'invité ignoré (voulu) |
| Véhicules (conduite, sièges, portières, molettes, vitres, givre, fuites, corde, chocs) | VehicleSync, Seats, CarDoors, CarVisuals, Knobs, WindowCranks, Frost, CarLeaks, Tow, CarHits, RunOver, Traffic | A (conducteur, sinon H) | oui | voiture absente de la table de l'hôte non suivie (`VehicleSync.cs:17, 365`) ; commandes d'une voiture garée non renvoyées (`Jobs.cs:669-685`) |
| PNJ, trafic, train, bus, voitures garées, Teimo, voix | Npcs, Traffic, Bus, Parked, TeimoSled, Voices, CopySounds, Particles, Events, NearDoors, GarageDoor | H, PNJ confiés | oui (5-10 s) | logique interne des PNJ locale (voulu) |
| Téléphones, commandes, courrier | Calls, PhoneKeys, Packages, WorldFsms | H / R | oui | lettre Kela perso (`Calls.cs:28-29`) |
| Maison : fusibles | Home | H | oui (10 s) | — |
| Maison : poêle, cheminée, sauna | Fires, Sauna, Cooking | A (plus proche) | oui | départ de feu d'un invité annulé (`Fires.cs:204-206, 280-298`) |
| Maison : télé | Interactions, WorldFsms | H si télé de l'hôte allumée | oui | sinon chacun la sienne (`WorldFsms.cs:149-151, 817-818`) |
| Maison : ordinateur | Machines | P | — | chacun joue chez lui (`WorldFsms.cs:30-31`) |
| Maison : radio, CD | Radio, WorldFsms, Props | H / R | oui | — |
| Maison : sommeil | World, SleepingBags | H | — | phase rapide seulement si tous dorment (voulu) |
| Maison : portes, interrupteurs, robinets, factures | Interactions, PushDoors, WorldFsms | R + H | partiel | états non sauvegardés non renvoyés à l'arrivant (`WorldFsms.cs:903, 62`) |
| Pêche, chasse | — | ∅ | — | aucune référence dans le code (à vérifier si le jeu les a) |
| Bus | Bus, Traffic | H | oui | — |
| Police, amendes | Police, Npcs | barrages H, délits P | perso (`police-joueur.ini`) | voulu (`Police.cs:18-21`) |
| Feux, cuisine, boissons, nourriture | Fires, Cooking, Consume, Drinks, Shop | H / A / R | oui | §Fires |
| Argent, banque, paies | Wallet, WalletPanel | P (allocations partagées une fois) | oui | §2 n°4-5, §3.3 |
| Mort, réapparition, PvP | Respawn, CarHits, RunOver, Pvp | P + événements | — | — |
| Météo, heure | World, Cheats | H | oui | — |
| Saleté, lavage | Fluids (`Dirt`), `PlayerDirtiness` P | R / P | — | pas de module de lavage |
| VHS, caméscope, musique, contenu | Kauppa*, Contenu | H + R | oui | — |
| Rallye | Races (IA H), Rally (humains), Kartturi | H + P | oui | une seule CORRIS pour tous (`Rally.cs:22-23`) |
| Course sur glace | Races (IA seulement) | H pour l'IA, **P pour les humains** | — | `LINEUPS` sauté (`WorldFsms.cs:51`), `Races.cs:9-25` ; feuille de route `docs/FEUILLE-DE-ROUTE.md:64-65` |
| Mods tiers | ModCheatbox (+SPS), ModJonne, Mod300D, ModSecondMachtwagen, ModRam1500, ModTowCart, ModJackStands, Suitcase/YAIM (Props), Poro, CoopApi | R / H | oui (3-10 s) | §3.3 |

### Écarts de couverture, par priorité
1. **Course sur glace** : pilotes humains non partagés (chacun court seul). Transposer `Rally.cs` (graine de l'hôte, tableau, places) à `RACES/ICERACE`.
2. **Objets portables `(itemx)`** (lampe, lanterne, chargeur, bouilloire…) : aucun état partagé sans module dédié. Liste blanche d'automates `Use`/`Data` clés par l'ID Props, rejeu comme `Interactions`.
3. **Arrivant : états non sauvegardés du monde** (robinets, plaques, lampes hors liste) restent à l'état de chargement. Envoyer l'état courant de chaque automate suivi à l'arrivée.
4. **Arrivant : atelier** (crics, pont, chargeur, dégâts) — voir §3.2.
5. **Télé** commune seulement si celle de l'hôte est allumée. Auteur = joueur le plus proche d'une télé allumée (modèle `Calls.RunsHere`).
6. **Feux** : négligence d'un invité (poêle, grill, sauna) n'allume jamais rien. Autorité de proximité (`FireTools.Nearest`).
7. **Tirages au sort des boulots** hôte seul : si l'automate de l'hôte ne tourne pas, l'invité attend ou diverge. Reprise par l'invité après délai.
8. **Ventes aux puces** hôte seul (table doit tourner chez lui). Même reprise.
9. **Compteur du plateau à bois** : deux autorités (`BedAuthority()` vs hôte). L'émetteur de `@tas` doit être `BedAuthority()`.
10. **Voiture activée chez un invité seulement** non suivie (prêtée, JOKKIS, HYDROCOPTER, mods). L'invité propose ses clés, l'hôte active.
11. **Objets sans ID hors `WorldRoots`** (REPAIRSHOP, PERAPORTTI, LANDFILL, FleaMarket, RACES, INSPECTION…). Lancer `[Test] JournalSansId=1`, élargir.
12. **Ordinateur** perso : au moins allumage/écran visibles.
13. **Kela** : lettre perso mais état non gardé pour l'invité (`Wallet.cs:144-151`) → reprend celui de l'hôte à chaque session.
14. **Kiosque à saucisses, vendeur de pièces rallye, voiture IA remise sur ses roues** (`Shop.cs:89-90`, feuille de route).
15. **WorldFsms exige une saisie dans la seconde + enchaînement sûr** (`WorldFsms.cs:481-482`) : actions par déclencheur/collision jamais rejouées. Rejouer en coupant les actions perso (`Mute()` existe) au lieu de refuser.
16. **Plafond anti-boucle des boulots** (5/10 s, pause 30 s) : étapes perdues sans recalage.
17. **Commandes d'une voiture garée** (phares, contact, essuie-glaces) non renvoyées à l'arrivant.
18. **Usine : paie** (§2 n°5).
19. **Sauvegarde** : seule celle de l'hôte compte (voulu, `SaveTransfer.cs:956-957`) — toute désynchro non corrigée est perdue pour l'invité.
20. **Couleur / peinture du lanceur** ignorées pour l'invité (voulu).
21. **ModRam1500** : en-tête périmé, compteur kilométrique non synchronisé.

---

## 5. Réservé à l'hôte

Légitime (autorité monde) : heure/météo/sommeil, trafic/bus/garées/barrages, événements, prix essence, particules/sons/radio, courses, PNJ confiés, machines à jeu, rayons, miroir des variables, VIN, couleur des pièces neuves, fusibles, palan, portes poussées, envois aux arrivants, incendies de maison, vêtements, objets des ajouts, mods, triches/PvP/ajouts, admin, sauvegarde (l'invité la demande aux toilettes).

Limite réellement les invités :

| Où | Effet | Écart |
|---|---|---|
| `Fires.cs:271-298` | départ de feu de sa logique annulé | n°6 |
| `Jobs.cs:444` | tirage au sort jamais envoyé | n°7 |
| `Wallet.cs:594-598, 641-646` | ventes aux puces seulement si la table tourne chez l'hôte | n°8 |
| `LogPiles.cs:46, 104` | compteurs du plateau de l'hôte seul | n°9 |
| `WorldFsms.cs:817-818, 859` | télé commune seulement si celle de l'hôte est allumée | n°5 |
| `CarColor.cs:446, 456`, `CustomPaint.cs:37` | couleur/peinture du lanceur ignorées (voulu) | n°20 |
| `Ajouts.cs` | l'invité coupe un ajout pour lui, ne rallume pas ce que l'hôte a coupé | voulu |

---

## 6. Plan proposé

- **Lot 1 — Nightly 15 (urgences, 1 journée)** : §2 n°1, 3 (fait), 4, 6, 7, 8, 9, 10, 12 ; `runInBackground` ; `Sane(pos, vel)` + `F32Finite` ; vitesse/pose périmées de Props ; pose finale fiable de VehicleSync. Tests : monter/PorterAvant/palan/magasin/batterie.
- **Lot 2 — Nightly 16 (réseau & arrivants)** : jeton de session Steam (§2 n°2), BYE répété + liste noire, file des messages reçus pendant le chargement, instantané atelier, états non sauvegardés à l'arrivée, Stock (clé absente, panier d'un parti), KeyTable.Clear, messages bornés (Shop, KeyTable, Paint, SaveBegin).
- **Lot 3 — Nightly 17 (argent & boulots)** : paie par joueur (§2 n°5), `Suppress` ciblé, Kela par jour de jeu, porte-monnaie écrit à chaque changement, enveloppes, globales `Player*` protégées au rejeu, tirages/puces/télé/feux avec reprise par l'invité.
- **Lot 4 — couverture** : course sur glace, objets `(itemx)`, `WorldRoots`, voitures activées chez l'invité, mods (Ram parc, Jonne cuisson, Second Machtwagen `last = s`).

À noter : chaque lot = Nightly, captures/essais montrés avant publication ; Stable sur ton go.


---

## 7. Fait le 15/10 au soir (Nightly 15)

- **Lot 1** (section 2) : tout, plus remise en place locale d'une piece cassee sans autorite ; modes d'essai `casse` (CasseSansGarde=1) et `PorterAvant`.
- **Lot 2** : jeton de session Steam, exclusion fiable (BYE x3 + liste noire 10 s), lot non fiable vide avant un fiable, KeyTable borne + `ClearReceived`, en-tete de sauvegarde decoupe (protocole SaveBegin change, nightly seulement), fenetre « fraiche » 25 s, achats/peinture bornes, Events sans arret premature, Knobs sans renvoi en retard, Particles fiables sur changement, instantane atelier + renvoi 5 s + degats a l'arrivee, releve des appareils relance, Stock (cle absente, reassort, panier d'un parti, prise locale gardee), etats non sauvegardes a l'arrivee, compteur du plateau par l'autorite, PNJ a 3+, Second Machtwagen.
- **Lot 3** : porte-monnaie ecrit a chaque changement / fin de session / fermeture, allocations par jour de jeu, enveloppes payees a « State 1 », credit des puces hors jeu garde, don a un parti rendu, paie usine/pubs/taxi par joueur (etats de paie exclus du rejeu), globales `Player*` a l'abri du rejeu des boulots, numero de commande par joueur, double repas, cuisson Jonne par objet, en-tete Ram corrige (deja synchronise).
- **Lot 4** : objets portables (`ItemFsms`), course sur glace (`IceRace`, `[Coop] CourseGlace`), feux d'un invite transmis a l'hote, tele suivie depuis un invite, ecran de l'ordinateur (apprentissage a chaud, chemins a figer), tirages au sort et ventes aux puces repris par un invite (`@tirages`, `@puces`), etat Kela par invite.
- **Fait le 16/10 (0.96.1)** : ecarts 10 (voiture active chez l'invite seul : proposee a l'hote, `M_PROPOSE`), 11 (objets sans ID hors `WorldRoots` par criteres, doublons detectes), 14 (kiosque a saucisses, vendeur du rallye : comptoirs rejoues, saucisse commune, `K_Sellers`), 15 (transitions par `TriggerEvent`/`CollisionEvent` rejouees sous `MutePersonal`, `[Test] SansDeclencheurs=1`), 17 (boutons des voitures garees a l'arrivant, `@garee`) ; course sur glace a plusieurs humains (numeros canoniques, doublures, table commune, arrivees fusionnees, `K_GRID`/`K_FINISH`) ; compteurs de l'usine par joueur (`FactoryOwn`, `etat-joueur.ini`). Essais : garee, horszones, declencheur, kiosque, glace3, usine.
- **Fait le 16/10 (0.96.2)** : ecart 16 (etat renvoye pendant la pause du garde-fou des boulots), spectateurs du rallye (`Spectators::Helpers`, `K_HELPERS`), ecart 12 (ordinateur par ses pieces nommees, programme affiche chez les autres). Le jeu n'a ni peche, ni chasse, ni lavage de carrosserie (releve v.261008-01) ; la nasse n'a aucune prise. Restent voulus : ecarts 19-21.
- Batterie nightly (20 modes) : 0 erreur ; essais cibles : casse (refus hote + remise), palan, montage avec piece tenue, vis.
