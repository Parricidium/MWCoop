# MWCoop → My Summer Car : inventaire (19/10/2026)

Les deux jeux tournent sur **Unity 5.0.0f4** avec les mêmes bibliothèques (PlayMaker, ES2, cInput, HOTween).
**Steamworks.NET est présent dans les deux** (Assembly-CSharp-firstpass) : le lobby Steam et le P2P devraient marcher tels quels.

Classes :
- **G** : générique, marche dans les deux jeux tel quel
- **A** : mécanisme générique, mais des noms d'objets ou de FSM de MWC sont écrits en dur → il faut une table de noms par jeu
- **S** : contenu propre à MWC, coupé pour MSC
- **M** : support de mods tiers (abandonné, cf. 16/10)

| Classe | Fichiers | Lignes | Part |
|---|---|---|---|
| G | 65 | 18 289 | 22 % |
| A | 69 | 48 362 | 58 % |
| S | 30 | 13 657 | 16 % |
| M | 7 | 3 031 | 4 % |
| **Total** | **171** | **83 339** | |

À noter : dans MWC, la voiture à construire, c'est **CORRIS (la Rivett)**, avec ses points VINP_, sa plaque VIN et l'inspection.
C'est elle, l'équivalent de la **SATSUMA**, pas la SORBET.
Beaucoup de noms de MSC existent déjà dans la scène de MWC : GIFU, KEKMET, JONNEZ, FLATBED, REPAIRSHOP (Fleetari), Teimo, JOBS/HouseShit* / HouseWood*, ventti, KiljuBuyer, TRAIN, JAIL, YARD, COTTAGE.

## G : générique (marche tel quel)

**Noyau** : Entry, Core, Config, Log, Perf, Recon, Trace, ExitGuard, Wine, ModRefl, Version, CoopApi, Lights.

**Réseau** : Codec, Transport, Session, KeyTable, Admin, ModServe.

**Interface** : Menu (F10, onglets VAZ/Jokkis à cacher), Kit, Style (logo « My Winter Car coop » à changer), Backdrop, Chat, Hud, HudSettings, Keys, Lang, LangRu, CheatPanel, WalletPanel.

**Joueurs** : PlayerSync, ThirdPerson, Markers, Pvp, RunOver, CarHits, Voice, Studio, Tenues, CustomFace.

**Synchro générique** : Ajouts, AssembleIcon, Audit, Boxes, CarVisuals, Consume, Contenu, CopySounds, Interactions, ItemFsms, Knobs, NearDoors, Replay, SaveStats, WorldFiles, RayTracing, DroneCam.

**Caméscope et montage** (utiles seulement avec la boutique) : KauppaKamera, KauppaFilmi, KauppaMontage, KauppaTuonti, KauppaLippu.

## A : à adapter (table de noms par jeu)

**Gros morceaux**
- VehicleSync (3022 lignes) : autorité et position des voitures, cœur générique ; les cas spéciaux visent SORBET, CORRIS et le taxi.
- Shop (3156) : caisses Teimo et Fleetari probablement transposables ; PSK, marché aux puces et kiosque n'existent pas.
- Jobs (2876) : fosses septiques, bois, KEKMET et GIFU existent dans MSC ; usine et taxi n'existent pas.
- Avatar (2407), Machines (1799), Npcs (1666), Props (1447), Wallet (1409), WorldFsms (1354), Seats (1324), Traffic (1255), Garage (1240).

**Le reste**
- Bus, Calls, CarColor, CarDoors, CarLeaks, Cheats, CheatsProbe, Cooking, CustomPaint, CutterHitch, Drinks, Events, Fires, Fluids, FuelPrices, GarageDoor, Gestures, GuestSlot, HandTools, Home (fusibles, kilju).
- LightsGuard, LogPiles, Looks, MapRender, Packages, Paint, Parts, Particles, PhoneKeys, Police, PushDoors, Races, Radio, Rally, Respawn, Sauna, SaveHook, Settings, SewageHose, SleepingBags, Stock, Tow, Voices, Wear, Windshields, World.
- Flow (menus du jeu), Gfx, Game, SaveTransfer (chemin LocalLow), SteamNet, MapJobs, MapPanel, MapRaces, NewHud, WaitScreen, Autotest.

## S : propre à MWC (coupé pour MSC)

- **Course sur glace** : IceRace, IceRaceVaz, Jokkis, JokkisPanel, RacePanel.
- **VAZ-2108** : Vaz, VazCar, VazHandling, VazSound, VazPanel, RallyVaz.
- **Boutique et musée** : Kauppa, KauppaKalusteet, KauppaOsto, KauppaRakennus, KauppaVideo, KauppaKartturi, Clearing, MuseoRakennus, MuseoNayttely.
- **Hiver et lieux de MWC** : Frost, WindowCranks (SORBET), CarSale (Rivett), Plats (PSK), TaxiCustomer, TeimoSled, Parked, Vin, WoodCarriers.
- Kartturi : les tracés du rallye pourraient se refaire pour MSC.

## Ce qui suppose My Winter Car en dur

**Loader**
- `proxy.cpp:475` : ne fait rien si l'exe n'est pas `mywintercar.exe`.
- `proxy.cpp:359`, `:364` : clé de registre `Amistech\My Winter Car`.
- `proxy.cpp:428` : `mywintercar_Data\output_log.txt`.

**Mod**
- `SaveTransfer.cs:204` et `GuestSlot.cs:81` : `LocalLow\Amistech\My Winter Car`.
- Noms des scènes « GAME » et « MainMenu » à vérifier dans MSC : `Game.cs`, `Flow.cs`, `SaveStats.cs`.
- Le reste passe par `persistentDataPath` ou `dataPath` et s'adapte tout seul.

**Lanceur**
- `mywintercar` revient environ 107 fois, « My Winter Car » environ 147 fois et l'appid 4164420 dix fois.
- Fichiers concernés : `launcher.cpp`, `mods.inc`, `modsync.inc`, `saves.inc`, `journaux.inc`.
- Il faut regrouper tout ça dans une **fiche jeu** : exe, dossier `_Data`, dossier Steam, appid (MSC = 516750), nom affiché, dossier LocalLow.

## Vérifié dans le vidage de MSC (19/10, jeu v.250908-04)

Le vidage complet est rangé dans `ref\msc-v.250908-04\GAME-complet.txt` (hors dépôt).
Il contient 32 743 objets et 258 racines.

**Présents sous le même nom**
- `PLAYER`, avec le même rig : `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera`.
- `MAP/MESH/TERRAIN_OBJ`, `TRAFFIC`, `NPC_CARS`, `HUMANS/Randomizer/Walkers`, `JOBS` (HouseShit1-5, HouseWood1…), `REPAIRSHOP`, `STORE`.
- `GUI/HUD`, `GUI/Icons`, `Systems/Setup Game`, `Systems/PassOut`, `Systems/KnockOut`, `Systems/Leaks`.
- `YARD/Building`, `FLATBED`, `BOAT`, `TRAIN`, `COTTAGE`, `CABIN`, `DANCEHALL`, `LANDFILL`, `PERAJARVI`, `INSPECTION`, `MasterAudio`, `CARPARTS`.
- Le rallye, à la racine : `RALLY` (ResultsWeekend, DriversList, RallyCars…).

**Autres noms**
- Les véhicules :
  - `SATSUMA(557kg, 248)` pour la voiture à construire ;
  - `GIFU(750/450psi)`, `KEKMET(350-400psi)`, `HAYOSIKO(1500kg, 250)`, `FERNDALE(1630kg)`, `RCO_RUSCKO12(270)`, `JONNEZ ES(Clone)`.
- `MAP/SUN` et `MAP/CloudSystem` remplacent `MAP/Sun/PivotSun/SUN` et `MAP/WEATHER`.
- `Systems/Options` remplace `Systems/OptionsDB`.
- `[off] Death` et `[off] JAIL`.
- `RALLY` remplace `RACES/RALLY`.
- `STORE` remplace `STORE_AREA`, avec `TeimoInShop` et `TeimoInBike`.

**Absents**
`PERAPORTTI`, `EQUIPMENTS`, `RACES` (pas de course sur glace).

**Premier essai en jeu (solo, profil isolé)**
- Ouverture, menu, nouvelle partie et entrée en jeu : tout passe tout seul.
- Avatars construits à partir des PNJ de MSC (Teimo, policiers, mamie…).
- Aucune erreur. Seul avertissement : l'animation `worker1_sitdown` (assis) manque.

## Plan proposé

0. **Reconnaissance dans MSC** : charger MWCoop dans MSC avec seulement les modules G et `Recon`, et récupérer le dump de la scène (noms d'objets, FSM, scènes, rig du joueur). Ce dump remplit les tables de noms des modules A.
1. **Une seule DLL pour les deux jeux** : le jeu est détecté au démarrage (nom de l'exe ou `Application.productName`), puis une table de noms par jeu est chargée et seuls les modules du jeu sont activés. Ce n'est pas une copie du projet.
2. **Lanceur** : le switch MSC / MWC, la fiche jeu, deux dossiers de jeu retenus, un thème, des onglets cachés selon le jeu, et le salon qui refuse un invité sur l'autre jeu (comme le contrôle Legacy / Reworked de `salon.inc:357`).
3. **Première partie jouable** : se voir, la Satsuma (position, portes, sièges), l'heure et la météo, le trafic.
4. **Le reste des modules A**, par ordre d'intérêt : pièces et boulons, boulots, boutique de Teimo, rallye, etc.

## Voitures sur My Summer Car (19/10)

Les modules VehicleSync, CarDoors, CarVisuals et Seats tournent maintenant sur MSC (Core.MscModules et MscLevelLoaded).
Ils n'avaient presque pas de noms écrits en dur : les voitures sont trouvées par `CarDynamics` à la racine de la scène, et les automates de MSC portent les mêmes noms que ceux de MWC :
- `PlayerTrigger` sur `DriveTrigger`, avec l'état « Player in car » ;
- `Starter`, avec l'état « Running » (SATSUMA) ou « Motor running » (les autres) ;
- `Use` des portières : Open door, Open door 2, Close door, Sound, Reset 2 et Set lock 2.

Ajouts propres à MSC :
- `BOAT` (Rigidbody sans CarDynamics) est suivi comme un véhicule de mod (`VehicleSync.IsModVehicle`).
- Banquette arrière pour SATSUMA, FERNDALE et RCO_RUSCKO12 ; HAYOSIKO n'a que la place avant.
- `[Test] Muet=1` est aussi appliqué dans `LateUpdate`, parce que MSC remet le volume à chaque image (`SetGameVolume everyFrame`).

Essai `[Test] Autotest=mscvoiture` (et `TestVoiture`, `TestPorte`), avec `run\test.ps1 -Msc -Players 2 -Seconds 150` :
- KEKMET et HAYOSIKO : la copie de l'invite suit l'hôte, et la portière s'ouvre puis se ferme chez lui.
- 9 véhicules trouvés des deux côtés, 13 ouvrants suivis, 16 places passagers.

Pas encore essayé :
- la SATSUMA montée : la partie neuve a ses portières en pièces, dans CARPARTS ;
- la conduite du BOAT ;
- les sons moteur sur la copie ;
- un passager assis ;
- les variables de simulation.

## Monde, trafic, boulots, argent, magasins et mécanique sur MSC (19/10)

Tout est activé dans `Core.MscModules` / `MscLevelLoaded`.
Les noms ont été relevés dans le vidage, et chaque écart avec MWC est gardé par `Game.Msc`.

**Heure et météo** (`World.FindMsc` / `SendMsc`)
- Soleil : `MAP/SUN/Pivot/SUN::Color`, une tranche de 2 h. Pas de TIMESKIP : on pose `Time`, puis `WAKEUP` à Color et à Clock.
- L'invité n'est recalé qu'après 5 s d'écart.
- Aiguilles : `YARD/Building/Dynamics/SuomiClock/Clock`.
- Météo : `MAP/CloudSystem/Clouds::Weather` reste allumé chez l'invité. Il reçoit PosX, PosZ et Rotation.
- Quand le temps change (WeatherType, WeatherCloudID), l'invité remet les matériaux et passe par « Set cloud ».
- Sommeil instantané dans MSC : rien à accélérer.

**Trafic et PNJ**
- Traffic : sur MSC seulement la police, les pare-brise et le bus. `TRAFFIC/FittanSpawns` remplace CousinSpawns.
- Npcs : Teimo à vélo (`STORE/TeimoInBike`) ; `RALLY` est écarté comme RACES.
- Police : la cellule passe par « Animate » et « Wake up ».
- Sons des copies : CopySounds.

**Boulots** (`Jobs`)
- Clé de contact de MSC : « Wait button » -ACC-> « Test », puis « ACC on » / « ACC on 2 », « Motor starting », « Shut off » et « Motor OFF ».
- Le rejeu de « ACC on » se repose dans « Wait button » (sinon le démarreur partait tout seul) : `IgnitionLikeMsc`, `ApplyIgnitionMsc`.
- Écartés : la marche de `JOBS/Mummola::Logic` et les meubles du déménagement.
- LogPiles : variable `LogPile`.
- Objectifs de la carte (MapJobs) : pas encore sur MSC.

**Argent** (`Wallet`)
- Pas de banque : une variable à nous, toujours 0.
- Les versements et objets de MWC ne sont pas cherchés.
- Enveloppes `PayMoney` sans « State 3 » : retirées en les vidant et les cachant.
- La machine à sous et le prix du rallye sont écartés.

**Carburant** : `STORE::Fuelprices` (Price98, PriceDiesel, PriceFuelOil), les pompes `Trigger*::Use.FuelPrice` et les afficheurs `STORE/LOD/Pumps/Prices/*`.

**Rayons** : pas de table `Carried`. La variable du rayon est `Bought` (`Quantity` est le panier de la caisse).

**Magasins** (`Shop`) : les caisses s'appellent `Register`.
- Teimo et Fleetari (genres « teimo » et « fleetari ») : le panier est dans les variables Q* de la caisse.
- Le rejeu met de côté le panier local et pose celui de l'acheteur.
- Il entre directement dans « Sausages » / « Alternator ». « Purchase » est sauté : rien n'est payé, le carburant et les pompes ne sont pas remis à zéro.
- Le panier local est remis au repos.
- Bar, contrôle technique, station d'épuration, bus et vendeur du rallye : comme MWC.
- Commande de travaux chez Fleetari (`REPAIRSHOP/Order`, booléens) : pas encore sur MSC.

**Mécanique** (`PartsMsc.cs`)
- Point de montage : automate `Assembly` du déclencheur. Le clic est « Wait for assembly » -ASSEMBLE->.
- Démontage : `Removal` / « Remove part ». « Mouse off » avant = casse, envoyée seulement par l'autorité.
- Vis : `Screw` / `Stage`, accrochées en « Wait 3 » / « Wait 4 ».
- Clé de pièce : `db:` + chemin de son entrée Database.
- Rejeu d'un montage : la pièce va sur son pivot (`Parent`), cinématique comme en main, puis la chaîne du jeu tourne. Libre dans la caisse figée, son joint cassait.
- Autres modules :
  - Settings : table `Database/*`.
  - Fluids : noms de MSC, `Database/*` comptés comme la SATSUMA, `Obstacle` écarté.
  - Paint : clé `UniqueTagColor`.
  - Props : racines `ITEMS` et `STORE`.
  - Garage : `Hoist::BoltCheck`.

Essai `[Test] Autotest=mscmeca` (2 joueurs, 210 s), réussi chez l'invité :
- portière gauche montée sur son pivot ;
- vis à 2 crans ;
- courses chez Teimo rejouées (objets créés, panier local remis, seul l'hôte paie 20 mk) ;
- portière démontée.

Pas encore essayé en jeu :
- un changement de météo (toutes les 2 h 30 de jeu environ) ;
- l'heure après le sommeil d'un joueur ;
- la clé de contact ;
- les boulots (fosses, bois) ;
- Fleetari ;
- les liquides ;
- les réglages.

## Mêmes règles que MWC sur MSC (19/10, après 1.1.1)

Tout est réservé à MSC (`Game.Msc`).

**Téléphone** (`PhoneMsc.cs`)
- Chacun a son téléphone, donc les deux sonnaient et seul celui qui décrochait avait le boulot.
- Maintenant, décrocher envoie `@tel` avec le sujet (« Shit 1 », « Wood1 »…). Les autres raccrochent et rejouent les actions du sujet sans la voix ni le sous-titre : booléen de PhoneLogic, `Level.Called`, fléchette, événements aux automates que Jobs ne suit pas.
- Téléphone jamais allumé chez l'autre : une table de secours fait l'essentiel.

**Paies** (`Wallet`)
- L'état qui paie est trouvé à son `FloatAdd PlayerMoney` : « Anim » chez Jokke (déménagement), « State 6 » chez la grand-mère.
- `PayMoneyAdvert` (tracts) compte aussi comme enveloppe.
- Le premier qui prend l'enveloppe est payé. Chez les autres, l'état de paie est rejoué à 0 mk, pour que la suite du boulot ait lieu partout (fermier, oncle, Jokke, tracts…).

**Police**
- Amendes personnelles : PlayerFines, PlayerFinesDay, PlayerCopChecksum et le compte à rebours `Fines.FineWait` (30 000 s par défaut) sont gardés dans police-joueur.ini.
- Agent renversé, pilote du trafic tué : le délit va seulement à celui qui conduisait la voiture (hook en tête de « Crime »).
- En prison, l'invité ne reçoit ni le jour ni l'heure de l'hôte.
- La décharge sauvage du GIFU (ShitDumps) n'est plus rejouée.

**Magasin de Teimo**
- Magasin pas chargé chez celui qui rejoue : le sac est fait à la main, avec le même distributeur et le même nom (`shoppingbagxN`), et figé jusqu'au chargement. Les objets à part (caisse de bière…) sont figés aussi.
- Un article sorti du sac (ProductNameList « State 7 » / « State 8 ») : la même case est sortie chez les autres.
- Chez l'invité, la dette d'essence et de vitre de l'hôte (venue de la sauvegarde) est effacée.

**Essence** : le compteur de litres des pompes n'est plus partagé par Fluids. Celui qui pompe paie à la caisse, et le carburant arrive chez tous.

Essai `[Test] Autotest=mscregles` (2 joueurs, 260 s) :
- appel décroché par l'invité, la fosse notée chez l'hôte ;
- courses payées par l'invité seul ;
- sac et caisse de bière chez l'hôte ;
- article sorti et sac vidé pareil des deux côtés ;
- 10 L d'essence chez les deux, payés par l'invité.

Suite (même jour) :
- **Objectifs de la carte** (`MapJobs.JobsMsc`) : fosses, bois, ferme (foin, moissonneuse), déménagement, Jokke, grand-mère, tracts. Le boulot est à celui qui a décroché (`PhoneMsc.Owner`) ; les tracts, à celui qui a pris la pile chez Teimo.
- **Tracts** (`TractsMsc.cs`) : une boîte servie par l'un (`@tract`) l'est chez tous, comptée une seule fois.
- **Fraises** : déjà personnelles, rien n'est rejoué. Chacun est payé pour ses propres caisses.
- **Fleetari** (`FleetariMsc.cs`) :
  - achat : refait à la main chez les autres avec l'usure tirée par l'acheteur, même loin du garage ;
  - travaux : commande envoyée ; chez les autres, l'automate Order est arrêté ; ils font les travaux quand ceux de l'acheteur commencent, puis prennent ses tirages (usure, couleurs des tôles, peinture).
- **Poste** : les colis payés sortent de `STORE/Boxes` chez tous, une seule fois.
- **Pompes** (`PumpsMsc.cs`) : pistolet pris ou en place et afficheurs chez tous ; une pompe tenue par un autre est bloquée.
- **Police** : l'invité qui fuit un barrage est poursuivi chez lui. Sa copie de la voiture passe en `Throttle.Chase` et sa zone est remplacée par sa distance à la voiture (moins de 120 m). Puis « Set wanted » (PoliceEvasion), ou l'amende s'il revient.

Essai `[Test] Autotest=mscreste` (2 joueurs, 280 s), réussi :
- appel décroché par l'invité : l'objectif est chez lui seul ;
- boîte servie chez tous ;
- pistolet pris, puis reposé chez l'hôte ;
- alternateur payé par l'invité seul, même usure (94,3) des deux côtés ;
- travaux faits chez l'hôte avec les tirages de l'invité ;
- colis sorti chez l'hôte.

Pas éprouvé en jeu : la poursuite de l'invité (pas de barrage près de la maison dans l'essai).
