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
