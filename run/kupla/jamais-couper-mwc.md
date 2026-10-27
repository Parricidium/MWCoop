# Kupla — liste « jamais couper » pour My Winter Car

[MWC], 27/10/2026, pour [Addons et Tools] (spec : `MWCoop-spec-kupla-modules.md`, §4 P2).
Relevé fait sur le code de `src/MWCoop/Sync` (branches `Game.Msc` ignorées). Les noms entre guillemets sont les littéraux du code ou du jeu.
« Couper » = `SetActive(false)`, `enabled = false` sur un automate ou un script, `isKinematic = true`, ou tout gel de transform.

## 0. Règles de méthode (avant toute liste)

1. **Ne jamais couper une racine de scène.** Beaucoup de relevés traitent un automate dont `transform.root` est inactif comme un modèle (prefab) et l'écartent — pour toute la partie quand le relevé est unique : `Machines.Scan` (une fois à 15 s), `Home.ScanStatus` (8 s), `PushDoors.Scan` (8 s), `Wallet.ClaimEnvelopes` (4 s), `World.Find` (`GameObject.Find` sur le soleil et la météo), `NearDoors.Scan`, `WorldFsms`, `Fires`, `ItemFsms`, `Stock`, `Npcs.SkipBody`. Couper seulement des sous-objets.
2. **Un automate rallumé repart de son état de départ** (`Fsm.RestartOnEnable`, vrai par défaut). Tout état rejoué ou recopié par la sync (portière, cric, `Starter`, `Freezing`, vitre cassée, caisse, enveloppe, feu, boulot) serait perdu, et les tirages au sort (téléphone, TV, passagers du bus, clients) relancés. Avant de couper un objet : pour chaque `PlayMakerFSM` dessous, mémoriser `f.Fsm.RestartOnEnable`, le mettre à `false`, puis le restaurer au rallumage. Modèle dans `Machines.cs` (`RestartOnEnable = false; // a la reprise : la ou il en etait`) et `KauppaKamera.cs:208`.
3. **Un objet coupé ne reçoit rien.** `PlayMakerFSM.BroadcastEvent` (SAVEGAME, PURCHASE, GRILL…) ne touche que les automates actifs. `Game.SetState` (`Fsm.SwitchState`) sur un objet inactif joue l'`OnEnter` et s'arrête là : ni attente, ni `FINISHED`, ni actions par image. Plusieurs modules jettent alors le message (« automate inactif ici », « eteint ici », « pas en piste ici », vis mise en attente 15 s puis abandonnée dans Parts).
4. **Ces recherches ne voient que les objets actifs** : `GameObject.Find`, `Object.FindObjectsOfType`, `Game.FindFsm`. Un objet coupé au moment d'un relevé est « introuvable ici » ou jamais suivi. (`Game.FindAny`, `Game.AllFsms`, `Game.FsmsNamed`, `Recon.SceneRoots` voient aussi l'inactif.)
5. **`activeSelf`, `enabled` et `isKinematic` sont des données pour le mod** (§6). Kupla note l'état avant de couper et ne rallume que ce qu'il a lui-même coupé. Jamais de `SetActive(true)` global.
6. **Centres de bulle** : le joueur local (`Game.PlayerT`), chaque joueur distant à `Session.Players[id].State.Feet` (`pi.Level == 1`) — la position réseau, pas l'avatar : l'avatar d'un joueur assis est posé d'après la copie locale de sa voiture ; copie coupée = avatar figé = blocage circulaire —, la voiture de chaque joueur distant (`VehicleSync.RemoteCarTransform(id)`), et la caméra locale quand `DroneCam.Active` (jusqu'à `[Coop] DronePortee`, 80 m).
7. **Rayon minimum** : jamais plus petit que les distances vanilla des automates visés par la bulle. Repères : usine « Wait player » 200 m, kiosque à saucisses 99 m, ray tracing `[Graphismes] RTPortee` 250 m, zone de choc des piétons gardée jusqu'à 300 m d'un invité, sons des copies 300 m.
8. **Calendrier** : rien de coupé avant **60 s** après le chargement (relevés uniques jusqu'à 30 s, instantanés pour un arrivant à 20-28 s, pièces à partir de 30 s), rien pendant `Game.Saving` ni pendant une sauvegarde coop (`SaveTransfer` `coopPhase` 1 et 2), tout rallumé pendant au moins un relevé quand un joueur arrive ou à un `@resync`.

## 1. Jamais coupé, chez tout le monde, quelle que soit la distance

### Racines et systèmes
- `PLAYER` et tout ce qui est dessous ; `GUI` (`GUI/CAM`, `GUI/Icons`, `GUI/Indicators`).
- `Systems` : `Setup Game` (`WaitPlayer`, crochet de sauvegarde), `Death` (ne **jamais** le rallumer non plus : rallumé = mort), `Expenses`, `BankAccount`, `PassOut`, `KnockOut`, `PlayerWanted`, `OptionsDB`, `CrouchTriggers`, `Statistics`, `PhoneBills*`, `TV/TVPrograms`, `Leaks` (fuites : `activeSelf` tenu par le mod).
- `Database` (réglages `Database/…::Data`), `MasterAudio`, `Skidmarks`, `CD` (persistant), `MWCoop` (`DontDestroyOnLoad`).
- `MAP/Sun/PivotSun` et `MAP/Sun/PivotSun/SUN` (heure), `MAP/WEATHER/Clouds`, `MAP/WEATHER/Clouds/CloudObjects`, `MAP/WEATHER/Forecast` (météo) ; `MAP/MESH` pendant le chargement (`Clearing` à 1 s, `MapRender` à 20 s).
- `Spawner` et tous ses distributeurs (`CreateItems`, `CreateItemsSeparate`, `BagContentsStore`, `BagContentsFleetari`, `CreateShoppingBag`, `Cigarettes`…).
- `CARPARTS` : `PARTSYSTEM/OrdersSpawnerYP`, `OrdersSpawnerAMIS`, `PhoneNumbers`, `PostSystem` (`AMISOrderList`, `VINSpawners`, `VINLIST_order`), `SPAWNERS_VIN`, `SPAWNERS_Aftermarket`, `SPAWNERS_Fleetari`, `VINPlate`. Les commandes, colis et pièces d'occasion sont rejoués par `SendEvent("SPAWNITEM")` chez tous.
- `FleaMarketProducts` (listes et `Creator`), `FleaMarket/SaleTable` (`Logic`, `Sell`).
- `Radio` (`Radio/Folk`, `Radio/CD`), `RADIO`, `RadioChannels`, lecteurs `CDAudioSource*` : chez l'hôte, les vrais canaux doivent jouer même loin, sinon tout le monde perd la musique.
- Toilettes `"SAVEGAME"::Button` (HOMENEW, YARD, COTTAGE, CABIN, STORE_AREA, LANDFILL, REPAIRSHOP, JAIL).
- `REPAIRSHOP` (`OpeningHours`, `OrderFleetari`, `Jobs`), `PERAPORTTI/ParkedCars` (automate du tirage, hôte), `PERAPORTTI/FuelPylon` (`Fuelprices`, hôte).
- Objets « en réserve » : tout ce qui est en (0,0,0), sous y ≈ -1000 (studio à y = -3000, carte à -80000, `MWCoop-FaisceauLoin` à -10000) ou en `DontDestroyOnLoad`. Exclure par position, pas par distance.

### Objets créés par le mod (`MWCoop*` et assimilés) — jamais coupés, jamais rallumés s'ils sont inactifs
- Avatars `MWCoop-Joueur-<id>` (+ `Char`, `MWCoop-Jet`, `MWCoop-Cigarette`, `MWCoop-Fumee`, `MWCoop-Couverture`, `MWCoop-Accessoire-*`, `MWCoop-Casque`, `MWCoop-Outil *`, `MWCoop-Voix-<id>`, `MWCoop-ronflement`, `MWCoop drone #<id>`), modèles inactifs `MWCoop-AvatarModele*`.
- VAZ : `MWCoop-VAZ-<n>` et ses enfants (`VAZ`, `VAZ_*`, `pieces`, `vaz_*`, `AudioEngineCorrisTune`, `EngineValveSound`), `MWCoop-VazApercu-<n>` ; glace : `MWCoop-Glace-<n>`.
- Corde : `MWCoop-CordeModele` (inactive), `MWCoop-CordeTenue-<id>`, `MWCoop-Corde-<id>`, `MWCoop-CordeBout1/2`.
- Sous les voitures : `MWCoop-SiegePassager`, `MWCoop-CeinturesArriere`, `MWCoop-CeintureArriere<i>`, `MWCoop-CeinturePassager`, `MWCoop-CeintureConducteur`, `opener MWCoop`, `vitre MWCoop`.
- `MWCoop-JoueurProche` (proxies NearDoors), `MWCoop-BusJoueur`, `MWCoop-RadioAutre`, `MWCoop-Marqueur-<n>`, `MWCoop combine`, `MWCoop touche`, `MWCoop-Boisson-*`, `MWCoop plat *`, `MWCoop custom paint`, `MWCoop-Carte`, `MWCoop-CarteCamera`, `MWCoop montage`, `MWCoop-Studio*`.
- Objets du jeu que le mod crée ou possède : `sleeping bag(itemx)` (à la racine, déjà « Parked » à plus de 40 m de tous : ne pas défaire), `Sleep MWCoop 1/2`, `winter jacket <k>(itemx)` / `winter coverall <k>(itemx)` / `helmet <k>(itemx)` / `coffee cup <k>(itemx)` (copies dont `activeSelf` appartient au mod), `* osuuskauppa(itemx)` et `shopping bag osuuskauppa(itemx)` (créés par l'hôte, suivis par Props), vendeurs `OSUUSKAUPPA-myyja`, `KINO-myyja`, `OSAT-myyja`.
- Bâtiments du mod (`OSUUSKAUPPA s21`, `MWCoop KINO TAHTI`, `MWCoop Korsikan museo`, `MWCoop CARDINAL VARAOSAT`) : coupure visuelle possible, à deux conditions — restaurer `activeSelf` pièce par pièce (articles pris, bobines, faisceau, `CrouchTriggerKino`) et relancer `Audio.Play()` de l'écran du cinéma au rallumage (fait seulement au changement de clip).
- Composant `CarHits.Watch` sur chaque corps de voiture, `MWCoopId` sur les bûches.

## 2. Jamais coupé sur CE PC, quelle que soit la distance (ce que ce PC mène ou possède)

- **Toute voiture pour laquelle `VehicleSync.DrivenHere(net)` est vrai** : conduite ici, laissée moteur tournant (`owned`), poussée, remorque attelée (`hitchSent`), tirée à la corde (`Tow.Carries`). Aussi `LocalLeadRoot`, la voiture de `Seats.SeatedCar`. Exemple : la GIFU laissée moteur tournant qui pompe une fosse pendant que son conducteur s'éloigne — coupée, `EngineRunning` tombe, la voiture est rendue à l'hôte, la pompe s'arrête.
- **L'objet tenu en main** (`PLAYER/…/Hand::PickUp`, `PickedObject`) : un objet tenu qui devient inactif est annoncé « rangé » et caché chez les autres (Props).
- **Les objets dans le coffre ou sur le plateau de toute voiture d'un joueur** (locale ou copie) : collés à la copie 8 fois par seconde ; déplacés de plus de 5 m, la cargaison saute (`MoveCargo`).
- **Paie et logique propres à chaque PC** (calculées chez chacun, pas chez l'hôte) : `JOBS/FACTORY::PlayerData` (`Base salary`, `High salary`), `JOBS/ADs::Data` (`Bank transfer 2`), `JOBS/TAXIJOB/MACHTWAGEN/TaxiFunctions::Payments`, `Systems/Expenses` (`Kela`, `Livingsupport`), `Systems/BankAccount::Data` (`Interest`) ; toutes les enveloppes `"PayMoney"::Use`, `ScrapMoney`, `MoneyFlea`, `PriceMoneyRally`, `PriceMoneyRace` ; `REPAIRSHOP/Scrapmetal/GarbageTrigger`, `PERAPORTTI/…/VoittousArea/TrashTrigger`. Coupés au moment de l'état de paie, ce joueur ne touche rien ; enveloppe coupée plus de 60 s quand un autre la prend = payable deux fois.
- **Tout `JOBS/*`** : NearDoors y redirige les `GetDistance` vers le joueur le plus proche (règle de RullER, 11/10 : « un seul joueur pour lancer un boulot »). Le site du boulot qu'un autre fait doit rester allumé ici. Idem pour toute porte ou barrière dont l'automate a un état `Open*`.
- `BreakableWindow*::Break` (vitrines) : rejeu `Shatter` sans test d'actif ; coupée puis rallumée, la vitre redevient entière. Au minimum `RestartOnEnable = false`.
- Chronos et feuilles de course du joueur qui court : `RACES/RALLY/SS1-3/TimingSS*`, `RallyTree::Clock`, `ParcFerme`, `ResultsWeekend`, `RACES/ICERACE/TrackFunctions` (`LINEUPS`, `Data`, `RESULTS`), `CORRIS/DB/RaceTrigger`.
- `SALE(Clone)` (Second Machtwagen) : feuille coupée = voiture achetable deux fois.
- Les 3 cadrans de téléphone (`activeSelf` = combiné en main ici) : `HOMENEW/Functions/FunctionsDisable/Telephone/Phone/KeypadPhone2`, `YARD/Building/LIVINGROOM/Telephone 1/Phone/KeypadPhone1`, `JOBS/TAXIJOB/MACHTWAGEN/TaxiFunctions/Carphone/KeypadPhone2`.

## 3. Chez l'HÔTE : logique commune, à garder vivante partout où un joueur est, et pour certaines partout tout court

L'hôte fait autorité sur tout ce que personne ne mène (`VehicleSync.SimAuthority` renvoie `Session.IsHost`) et il écrit la sauvegarde. Les gestes des invités sont rejoués chez lui ; rejoués sur un objet coupé, ils sont perdus ou à moitié faits, et **la sauvegarde de l'hôte est fausse**. Le seul contrôle après coup (digest des pièces, toutes les 10 s) **ignore les points de montage et les vis inactifs** : l'écart ne se voit qu'au rallumage.

### Toujours chez l'hôte, quelle que soit sa position
- `TRAFFIC` (`VehiclesHighway`, `VehiclesDirtRoad`, `Police`, `TriggerManager`, `CousinSpawns`), `TRAIN`, `NPC_CARS` (`BUS` : `Route`, `Ticket`, `LOD`, `Passengers`), `HUMANS/Randomizer/Walkers` (`Randomizer`, `Pivot/HumanTriggerCrime`). L'hôte n'envoie que les voitures `activeInHierarchy && Mesh.activeSelf` : coupée chez lui, elle disparaît chez l'invité ; `RunOver` et `HostGuard` ne traitent que les voitures actives.
- Téléphones : `HOMENEW/Functions/FunctionsDisable/Telephone/Logic/PhoneLogicNEW`, `YARD/Building/LIVINGROOM/Telephone 1/Logic/PhoneLogicOLD` (`Ring`, `Jokes`). Coupés, un invité à moins de 25 m reprend la logique (`RunsHere`), sinon personne n'est appelé.
- `STORE_AREA/TeimoInSled::Move`, `JOBS/TAXIJOB/Customer1` (`TaxiWalker`, `Suitcases`), `JOBS/JOKKEHOME` (`HouseDrunkNew`, `KiljuBuyer`), `JOBS/FACTORY`, boulots à tirage (`SendRandomEvent` : seul le tirage de l'hôte compte).
- Incendies : `YARD/Building/HOUSEFIRE/*/Fire*::Simulation`, `HouseKoistinen/FIRE::Logic`, `PERAPORTTI/…/GasolineFire::Logic`.
- Maison : `HOMENEW/Functions/ElectricThings/FuseTable`, `YARD/Building/Dynamics/FuseTable`, `fuse holder(Clone)::Use`, `HouseElectricity::Status` (relevé unique à 8 s).
- `RACES` les jours de course : `RACES/RALLY/RallyCars` (`AIdrivers`, `GenerateTimes`), voitures IA de `RACES/RALLY` et `RACES/ICERACE`, `RACES/RALLY/Spectators::Helpers`, `PartsSalesman`, kiosque `SausageHutFunctions` (`Lantakari`).
- `Systems/TV/TVPrograms` (`Schedule`, `ADs`), miroir des variables toutes les secondes sur `WorldFsms.MirrorRoots` = `Systems`, `HOMENEW`, `YARD`, `CABIN`, `COTTAGE` (+ `Fuelprices`, `PhoneNumbers`, `OrdersSpawner*::Data`, `PostSystem::Logic`).
- `PERAJARVI/Kunnalliskoti/Functions/RoomVenttiPig/Ventti/Table` (`GameManager`, états d'enjeu rejoués chez tous), machines `FuelPumps_*`, `VideoPoker`, `SlotMachine*`, `COMPUTER` (relevé unique à 15 s).
- `REPAIRSHOP/OrderFleetari`, `REPAIRSHOP/Jobs` et la voiture de référence pendant `HostWork` (15 s) ; `FleaMarketProducts::Creator` (au repos = `State 1`).
- Mods tiers menés par l'hôte quand il fait foi : objets et machines Jonne (un objet né chez l'invité sans geste est retiré après 6 s si l'hôte ne l'envoie pas), `Ram1500`, `TowCart`, `Machtwagen300D`, `SECONDMACHTWAGEN`.
- `PERAPORTTI/Building/LOD300/StaticCars` : l'hôte envoie `activeSelf` de chaque enfant toutes les 5 s ; un enfant coupé par Kupla chez l'hôte est caché chez tous.

### Chez l'hôte, dans la bulle de chaque invité (physique et scripts, affichage facultatif)
- Toute voiture garée près d'un invité (l'hôte en a l'autorité : pose envoyée toutes les 2 s, `SimParked` toutes les 10 s, seulement si `Body.gameObject.activeInHierarchy`). Une voiture inactive au relevé (`FindObjectsOfType<Rigidbody>`, toutes les 20 s) n'est pas numérotée ; un invité qui l'a active la « propose » et l'hôte fait `g.SetActive(true)` (`OnPropose`) — Kupla et le mod se battraient.
- PNJ suivis (`Npcs`) : `Char` actif et `Animation` qui joue chez l'auteur ; envoi à moins de 80/90 m des pieds d'un invité. Clients `…/Customers/` : un client coupé chez l'hôte devient « absent » chez l'invité. Règles d'engagement (portées) : `/Virpi/` 4,5 m, `/Keijo/` 12 m, `/Jouni/` 15 m, `INSPECTION/LOD/Officer/` 25 m, `Office/Fleetari/` 10 m, `TRAFFIC/Police/CopAlc` 30 m, `FighterPub/Fighter2/`, `FIGHTER/Fighter/`, `Janitor/Reijo/` 40 m, `HouseRintamaCrazy/Watcher/Oldman/` 40 m, `COPS/` 80 m, `TAXIJOB/Customer1/` 30 m.
- Foyers (`WoodTrigger::Trigger`, `SetFire::Use`, `Grilling::Logic`, `Woods/log1..4`, `HeatSource*::Data`, `garbage barrel*/Trigger::Use`) : autorité = joueur le plus proche à moins de 15 m (avatars compris), sinon l'hôte ; coupé chez son autorité, envoyé « ne brûle pas », chaleur figée, feu repris de zéro au rallumage.
- Lieux d'`Events` (automates avec `GetDistance` vers `PLAYER` + `ActivateGameObject` sous `JOBS`, `PERAJARVI`, `STORE_AREA`, `PERAPORTTI`, `REPAIRSHOP`, `DANCEHALL`, `WATERFACILITY`, `LANDFILL`, `FleaMarket`, `CABIN`, `COTTAGE`, `SOCCER`, ex. `JOBS/JOKKEHOME/HouseDrunkNew::OpeningHours`) : chez l'hôte l'automate tourne et vise le joueur le plus proche ; coupé, la fête, la porte ou la lumière n'évolue plus pour personne.
- Caisses et comptoirs (`CashRegisterLogic::Data` « Spawn bag », restaurant `PERAPORTTI/Building/LOD100/Restaurant` (Keijo `Work`, Jouni `AlaCarteRegister`), bar `Pay`, `INSPECTION`, `WATERFACILITY`, puces `TriggerFlea::Buy`, café `CoffeeButton::Buy`) : prévu inactif chez celui qui reçoit (`Emulate`), mais restaurant, verres et cafés sont alors abandonnés.
- Objets (`Props`, `FindObjectsOfType<Rigidbody>` toutes les 10 s, actifs seulement) : l'hôte recale les objets au repos toutes les 2 s sauf à moins de 3 m de la tête d'un autre ou 5 m de sa voiture — un objet coupé sort du suivi (corps à `null`).
- Chargeur KEKMET, crics (`car jack(itemx)`, `floor jack(itemx)`), pont `REPAIRSHOP/LOD/Lifter`, palan `motor hoist(itemx)` (`Hook`, `Trigger1/2`, `HoistBolted`) : voiture levée plus renvoyée = tombée chez l'autre.

## 4. Chez l'INVITÉ : ce que le mod force en boucle (Kupla ne doit pas se battre avec)

Le mod recopie l'état actif d'après l'hôte, parfois 5 fois par seconde, avec `WakeParents` qui rallume toute la chaîne des parents. Couper ici = rallumé aussitôt, avec un redémarrage d'automate à chaque bascule :
- `TRAFFIC` (`SetActive(on)` 5/s), `TRAIN`, `NPC_CARS` (`LOD` du bus à moins de 150 m, `StopButtons`, passagers), `HUMANS/Randomizer/Walkers`.
- `RACES/RALLY`, `RACES/ICERACE` et leurs voitures IA ; `TRAFFIC/Police/Checkpoints` et ses éléments ; `COPS`.
- Cibles `ActivateGameObject` d'`Events` ; clients `Present`/`Absent` (`Npcs`, `ReShow`) à moins de 100 m.
- Enfants de `PERAPORTTI/Building/LOD300/StaticCars` (`Parked`, rallumés en moins de 5 s).
- Feux des copies (`Particles`, reposés chaque seconde), `Systems/Leaks/*` et `SteamFountain*`, lampes et maillages reçus (`CarVisuals`), bûches `log1..4`, tas `LogpileBed`, modèles et `PickupTrigger` des porte-buches, étagères `INVENTORY*` (`Stock`), `CloudObjects`, copies radio.
- Copies de voitures des autres (`VehicleSync.IsCopy`) : déjà cinématiques, rien à simuler, mais elles doivent rester **actives** tant que leur conducteur est dans une bulle, transform écrit par le mod (`FixedUpdate.Follow` exige `activeInHierarchy`).
- Copies du trafic : cinématiques, prise de physique par l'invité à 9 m + 0,5 × vitesse de rapprochement.

## 5. Dans la bulle de CHAQUE joueur, chez tout le monde (le reste du monde)

Tout ce qui n'est pas aux §1-4 peut être coupé **hors de toutes les bulles**, aux conditions du §0 (`RestartOnEnable`, état mémorisé, calendrier). Dans la bulle d'un joueur distant, garder actif au minimum ce que la sync accroche là :
- Automates rejoués par `WorldFsms` (toutes les racines sauf `PLAYER`, `GUI`, `Sheets`, `COMPUTER`, `TRAFFIC`, `NPC_CARS`, `Spawner`, `Radio`, `MWCoop*`, véhicules), `ItemFsms` (`Use`, `Switch`, `Light`, `Power` des `(item*)` ; `radio(itemx)` et ses `Knob`), `Interactions` (`Handle`, `DoorHandle`, `Trigger`, `Mesh`, `Switch`, `switch_*`, `TVSwitch`, `CDSwitch`, `Eject`, `ButtonLightModes`, `ButtonWipers`), `Jobs` (automates `UT*`/`UniqueTag*` et `ControlFsms` = `Use`, `Knob`, `Screw`, `Usage`, `Change`, `Switch`, `ChangeChannel`, `ChangeTrack`, `Attach`, `Latch`, `Assemble`, `UseNew`), `PushDoors` (charnières `Use` `Open`/`Close`), `Packages` (`Post Package*::Use`, rejeu `SetState("1")` sans test), `Boxes`, `Consume` (`Use`/`Data` à `ID`, rejeu sans test), `Cooking` (`SausageTrigger`, aliments `Fire`/`Use`, `coffee pan(itemx)::Data` — un paquet de saucisses inactif peu après l'ouverture est diffusé « Destroy » à tous), `Home` (seau `bucket(itemx)`, `bucket lid(itemx)`), `WoodCarriers`, `LogPiles` (`FLATBED/Bed/LogTrigger`), `SewageHose`, `CutterHitch` (`Cutter/PTOCheck::Attach`), `Sauna` (`StoveTrigger::Steam`).
- Pièces et vis de toute voiture (`Parts` : `Data` des `VINP_*`, `Screw`, câblage `DatabaseWiring/…`) ; portières, capots, prises `cable plug*` (`CarDoors`) ; crochets de corde `HookFront`/`HookRear` (`Tow`, relevé à 9 s racine active) ; givre `Simulation/CarTemp*` (`Freezing`, `GlassFrosting`, `Scrape`, relevés à 8 et 40 s) ; pare-brise `BrokenWindshield` ; manivelles `opener` (relevé **unique à 3 s, actifs seulement**) ; boutons `Button*`/`Switch*` (`Knobs`, instantané à l'arrivant sur les voitures actives) ; `DriverHeadPivot` (`Respawn`, relevé unique à 10 s) ; `CORRIS` à 20 s (`CarColor`), `CORRIS/AssembyDatabase/PaintMaterials`.
- PNJ, foyers, lieux d'Events, caisses (§3) : chez tous, pas seulement chez l'hôte, puisque la sync les rejoue chez chacun.
- `ParticleEmitter` (hors `Shower`, `Tap`, `Steam*`, `Skid`), `AudioSource` sous `TRAFFIC`/`NPC_CARS` à moins de 300 m d'un joueur (`CopySounds`).
- `sleeping bag(itemx)` : laisser le gel « Parked » existant (40 m de la caméra et de tous les avatars).

## 6. Ce que le mod possède déjà (Kupla ne bascule ni ne « restaure »)

- `isKinematic` : corps des voitures et pièces à `FixedJoint` sur les copies (`VehicleSync.SetKinematic` mémorise `WasKinematic` puis le restaure : figé par Kupla entre-temps, la voiture reste figée), ouvrants (`CarDoors`), plateau du cric (`Garage`), vêtements (`Wear`), copies du trafic, `HostGuard`, corps des PNJ suivis, copies `Races`, portes `PushDoors`, objets `Props`, benne du plateau, sacs « Parked ».
- `enabled` : `Drivetrain`, `AxisCarController`, `Wheel`, `SoundController`, `SnowmobileController`, `MobileCarController`, `CarController` ; automates des objets son et `AudioEngine*` ; `ParkedCars` (invités) ; crochets du palan ; `Systems/Death` ; train `Move`/`Reset` (invité) ; `Crash`, `PlayerDistance` du trafic ; `Route`/`NavigationAI` du bus ; `Phone*::Logic` chez celui qui suit ; vitrines ; `PickUp` du jeu (Kauppa) ; `saleSell` (Wallet).
- `Rigidbody.interpolation`, `breakForce`/`breakTorque` des joints des copies (incassables), `FixedJoint "DeathForce"` du trafic.
- `activeSelf` (liste non exhaustive, ~60 `SetActive(false)` dans le mod) : `CarHorn`, émetteurs et objets son des copies, enfants de `StaticCars`, `Systems/Leaks/*`, `SteamFountain*`, lampes et maillages reçus, `DartCar`, vêtements portés par un autre, verrou et modèles de ceinture, `MWCoop-AvatarModele`, `Systems/Death`, `DeadBody`, paupières `*Eyes*`, portières arrière et hayon de la copie SORBET (VAZ), `envelope(parts)`, combiné `Phone/Handle`, bûches, feux de voiture, voitures IA, `FI/xNoCar`, `CloudObjects`, cibles d'Events, `FuseTrigger`, déclencheur `Sleep`, tas, modèles des porte-buches, pièces des machines côté spectateur, miroir de l'ordinateur (`SYSTEM`, `Lights`), `Vanish`, objets rangés, outils en main, bouteilles en trop, étagères, feux des copies, copie radio, marqueurs, `OrderList` du bar, présentoirs des puces, gobelet du soda, `REPAIRSHOP/Jobs`, `Offer.Shown`, `Display`/`Tag` (Osat), `CrouchTriggerKino`, bobines, faisceau, `COPS`, clé 300D, poignée `TowCart`, feux stop `Ram1500`, `CD1-3`.

## 7. Sauvegarde : tout rallumer AVANT la diffusion de SAVEGAME

- Toutes les sauvegardes en jeu passent par `Game.SaveInPlace()` : relevé des automates qui écoutent SAVEGAME par `FindObjectsOfType` (**actifs seulement**), puis `PlayMakerFSM.BroadcastEvent("SAVEGAME")` dans la même image ; `Game.Saving` reste vrai 2,5 s, le temps que `Game.Update` restaure les états.
- Le crochet `SaveHook` (tête de `"Save game"` de `Systems/Setup Game::WaitPlayer`) est **pendant** la diffusion : trop tard pour rallumer. Il ne peut servir qu'à journaliser ce qui était encore coupé.
- Rallumer **au moins une image avant** (un automate jamais démarré ne démarre qu'à l'image suivante). Points de branchement :
  - `SaveTransfer.StartCoop` quand `coopPhase = 1` (2 s d'avance avant `HostCoop`) ;
  - l'envoi du monde à un arrivant (`SaveTransfer.Update`, immédiat aujourd'hui) : réveiller puis `SaveInPlace` 1-2 images plus tard ;
  - `GuestSlot.OnHostSaving` (`K_NOW` chez l'invité) : réveiller avant `SaveInPlace` ;
  - le 2e clic « sauver et quitter » (`SaveTransfer.Buttons`, `Game.SetState(b.F, b.Target)` puis `Mute audio` → `Save` 0,3 s → `Save all`) : réveiller juste avant ;
  - toilettes non accrochées par le mod (`Broken`, ou sous un LOD éteint) : crochet propre à Kupla en tête de `"Mute audio"`/`"Save"` (prison : `"State 1"`) de chaque `"SAVEGAME"::Button`, par `FsmHooks.Insert` sans `Replay.Claim` (réservés à « sauvegarde »).
- Ne rien recouper tant que `Game.Saving` est vrai ni pendant `coopPhase` 1-2.
- Même chose avant un instantané pour un arrivant (20-28 s après son entrée : `Settings.ScheduleSnapshot`, `Knobs`, `Jobs.SnapshotTo`, voitures **actives** seulement) et à un `@resync`.

## 8. Calendrier des relevés au chargement (s après `OnLevelLoaded`)

| s | Relevé |
|---|---|
| 1 | `Clearing` (`MAP/MESH`, `GameObject.Find`) |
| 3 | `WindowCranks` (**une fois, actifs**), `VehicleSync` (puis toutes les 20 s, actifs), `SaveTransfer` toilettes |
| 4 | `Wallet.ClaimEnvelopes` (une fois) |
| 6 | `Difficulty` (`FuelTank*`, actifs) |
| 8 | `Parts`, `Frost` (et 40), `Home.ScanStatus` (une fois), `PushDoors` (une fois, actifs) |
| 9 | `Tow`, `Paint`, `Shop` (puis 60 s) |
| 10 | `Respawn` (une fois, actifs), `Wardrobe` |
| 12 | `Windshields`, `Settings`, `CarDoors` (puis 30 s), bâtiments Kauppa/Kino/Museo/Osat |
| 13-18 | `ItemFsms` (puis 20 s) |
| 15 | `NearDoors` (puis 90 s), `Machines` (**une fois**), `ModJackStands` |
| 16 | `Parked` |
| 20 | `CarColor`, `MapRender` (**une fois**, `FindObjectsOfType<Collider>`), `Vitrines`, `Audit`, instantanés à l'arrivant (20-28) |
| 25-30 | `ModJonne`, `Studio`, premier contrôle des pièces |
| 45 | `Npcs` |
| 60 | `WorldFsms` complet (puis 60 s) ; `[Test] SautModules` ne coupe qu'après 60 s |

→ Kupla ne coupe rien avant 60 s, et refait « tout actif » pendant au moins un relevé à chaque arrivée.

## 9. API du mod utilisables par Kupla

- `VehicleSync` : `Authority(net)`, `SimAuthority(Transform)`, `IsCopy`, `DrivenHere(net)`, `RemotelyDriven`, `IsPlayerCar`, `CarRoot(Transform)`, `LocalCount`/`LocalBody`/`LocalKey`, `CarIndex`, `Generation`, `RemoteCarTransform(id)`, `LocalLeadRoot`, `LocalDrivingRoot`, `HitchCarried(i)` ; `Tow.Carries(i)` ; `Seats.SeatedCar`.
- Joueurs : `Session.Players` (`Level`, `State.Feet`, `State.Head`, `StateTime`), `PlayerSync.Avatars` (`Root`, `Player`), `Game.PlayerT`, `Game.MainCam`, `DroneCam.Active`.
- Monde : `Game.FindAny`, `Game.AllFsms()` (au plus toutes les 3 s), `Recon.SceneRoots()`, `Recon.Path`, `Game.Saving`, `PlayerSync.InGame`, `Time.timeSinceLevelLoad`.
- Modèle de redirection des distances : `NearDoors.Retarget` (proxy `MWCoop-JoueurProche` à la place de `PLAYER` dans les `GetDistance`) et `GarageDoor` (hook `Nearest` = min(local, `Avatar.Root`)).
- Branchement : `Core.Mod("kupla", …)` après « joueurs » (avatars à jour), par exemple juste après « portes automatiques » ; `MscModules` seulement si valable pour MSC. Mesures : `[Test] JournalPerf=N` (ligne « perf : … corps E eveilles / T … »), `PerfAlloc=1`, `SautModules`.

## 10. Vérifications demandées avant la fusion dans main ([MWC] relit)

1. Autotest « éteint » : mêmes compteurs perf et même journal qu'avant Kupla.
2. Deux joueurs dispersés (un à la maison, un à l'usine) : boulot de l'invité qui continue, téléphone qui sonne chez l'hôte, trafic visible chez l'invité, voiture garée près de l'invité recalée par l'hôte.
3. Voiture laissée moteur tournant par un invité qui s'éloigne de plus que la bulle : moteur toujours tournant, pompe GIFU qui continue.
4. Montage de pièces et vis par un invité loin de l'hôte : digest sans écart, sauvegarde de l'hôte juste.
5. Sauvegarde (toilettes de l'hôte, `K_NOW` chez l'invité, arrivant) : journal `SaveHook` sans objet coupé.
6. Arrivée d'un 3e joueur après 10 minutes : instantanés complets (réglages, boutons, boulots).
7. Objet coupé puis rallumé : automates à l'état d'avant (pas `RestartOnEnable`), `activeSelf` des objets du §6 inchangé.
