# Kupla P2 — conception (bulles co-op)

[Addons et Tools], 27/10/2026. Base : spec `MWCoop-spec-kupla-modules.md` §4, listes « jamais couper » `jamais-couper-mwc.md` ([MWC]) et
celle de [MSC] (racines Database/Spawner/Systems/…, véhicules entiers, corps suivis par Props, automates réclamés, parade Unity 5.0).
Règle de la salle blanche inchangée : rien de lu ni repris d'un autre mod d'optimisation.

## 0. Ce que P1 a appris (27/10)
- Rendu local (rétros 1 image sur N, coupe par calque) : aucun gain mesurable sur le PC de JD (écart < 1 ms de bruit). Les deux jeux
  n'ont pas de Terrain Unity ; presque tout le monde est sur le calque Default.
- Donc P2 ne vise pas l'affichage : il vise le **CPU de l'hôte** avec des joueurs dispersés (ce que la ligne perf appelle « physique
  du jeu » + le reste de l'image hors mod), et seulement **hors de toutes les bulles**.

## 1. Mesure avant tout (Autotest=kupladispers, Joueur5 hôte / Joueur6 invité)
Même partie, même durée : (a) hôte seul ; (b) invité à côté de l'hôte ; (c) invité à l'usine ; (d) invité en ville. Relevés de
l'hôte toutes les 30 s : images/s, mod, physique, corps éveillés, automates actifs par racine, rendus. Si (c)/(d) ≈ (a) + bruit :
le jeu éteint déjà lui-même ce qui est loin (ses automates de distance, redirigés par NearDoors vers le joueur le plus proche) et
il n'y a rien à gagner hors des bulles → on s'arrête là, chiffres à l'appui. Sinon, les compteurs par racine disent ce qui a grossi.

### Résultat (27/10, 13:42-13:50, Joueur5 hôte / Joueur6 invité, partie neuve, jeux cachés 960x540)
Relevés de l'hôte, stables de 60 à 150 s :

| Essai | automates actifs | corps éveillés / total | rendus | physique du jeu | mod |
|---|---|---|---|---|---|
| (a) hôte seul | 1735 | 53 / 181 | 3432 | 0,46-0,55 ms | 0,50-0,57 ms |
| (b) invité à côté de l'hôte | 1738 | 55 / 185 | 3450 | 0,66-0,71 ms | 0,80-0,98 ms |
| (d) invité à Perajarvi (ville) | 1738 | 54 / 185 | 3453 | 0,72-0,87 ms | 0,83-1,11 ms |

Chez l'invité parti en ville : automates 1636 → 1743, corps 185 → 214, rendus 3453 → 3934 (la ville s'allume chez lui).
Chez l'hôte : rien ne s'allume (+0 automate, +0 corps entre (b) et (d)). Les images/s de l'hôte (165-179 seul, 125-133 à deux)
baissent parce que deux jeux partagent le même PC, pas parce que l'hôte simule plus.

**Verdict : il n'y a rien à couper « hors des bulles » chez l'hôte.** Ce qui est actif chez lui avec un invité loin = ce qui est actif
seul (+4 automates) : le jeu éteint lui-même ce qui est loin de SON joueur, et ce que le mod garde vivant partout (TRAFFIC 47,
HUMANS 55, JOBS 68, Systems 52, Spawner 102…) est sur les listes « jamais couper ». Kupla P2 n'a pas de cible mesurable.
L'essai (c) « usine » n'a pas eu lieu (pas de lieu « usine » dans /lieu) ; il vaut un 2e (b).

## 2. Bulles (conservé pour mémoire : non réalisé, voir le verdict du §1)
- Centres (règle 6 de [MWC]) : `Game.PlayerT` ; chaque joueur distant `Session.Players[id].State.Feet` (`Level == 1`) — jamais
  l'avatar ; `VehicleSync.RemoteCarTransform(id)` ; la caméra locale quand `DroneCam.Active` (portée `DronePortee`).
- Rayon : `[Coop] KuplaRayon` (défaut 350 m, jamais sous 300 : usine 200, piétons 300, RTPortee 250, sons des copies 300) +
  vitesse du centre × `KuplaAnticipation` (défaut 8 s), plafonné à 1000 m. Hystérésis : on coupe au-delà de R + 50 m, on rallume
  dès R.
- Un objet est « dedans » s'il est dans **une** bulle, quelle qu'elle soit ; sinon « dehors ».

## 3. Ce qui peut être coupé dehors (liste blanche, rien d'autre)
Seulement des sous-objets (jamais une racine), jamais rien des §1-§4 de [MWC] ni des §1-§4 de [MSC] :
- décor pur : `MAP/*` sans automate ni corps (maillages, LOD, ombres, colliders), arbres, buissons, bâtiments sans automate ;
- lampes (`Light`) hors bulles ;
- `ParticleEmitter`/`ParticleSystem` d'ambiance (hors `Shower`, `Tap`, `Steam*`, `Skid`, feux des copies) ;
- `AudioSource` d'ambiance hors `TRAFFIC`/`NPC_CARS` (sons des copies : CopySounds) ;
- automates **sans** `GetDistance` vers `PLAYER`, non réclamés (`Replay.ClaimedByOther(f, "kupla")` faux), sous une racine de
  décor, à condition de mémoriser `RestartOnEnable` (→ false) et de le restaurer.
Jamais : un `Rigidbody` (figer = `rb.Sleep()` seulement, jamais `isKinematic`/`SetActive`, parade Unity 5.0 de [MSC]), un objet à
attache (`Joint`), un objet suivi par Props (`Props.IsProp`), une voiture ou un de ses enfants, un objet `MWCoop*`, un objet dont
`activeSelf`/`enabled` appartient au mod (§6 de [MWC]).
Comment on coupe : `Renderer.enabled = false`, `Light.enabled = false`, `ParticleEmitter.emit = false` / `AudioSource.enabled = false`,
`PlayMakerFSM.enabled = false` — jamais `SetActive(false)` (les recherches du mod ne verraient plus l'objet, les BroadcastEvent
non plus). Chaque coupure mémorise l'état d'avant ; on ne rallume que ce qu'on a coupé.

## 4. Calendrier et sauvegarde (règle 8 et §7 de [MWC])
- Rien avant 60 s après le chargement ; rien pendant `Game.Saving`, `SaveTransfer.coopPhase` 1-2, ni 30 s après l'arrivée d'un
  joueur ou un `@resync` (instantanés 20-28 s).
- Tout rallumé **au moins une image avant** chaque sauvegarde : branchements `SaveTransfer.StartCoop` (phase 1), envoi du monde à
  un arrivant, `GuestSlot.OnHostSaving`, 2e clic « sauver et quitter », tête de `"Mute audio"`/`"Save"` des toilettes
  (`FsmHooks.Insert`, sans `Replay.Claim`). Puis on ne recoupe qu'après `Game.Saving` retombé.
- Journal : « kupla : N objets coupés dehors (M automates), R m », et au `SaveHook` « kupla : tout rallumé avant la sauvegarde ».

## 5. Essais ([MWC] §10, [MSC] relit)
Éteint = journal identique ; 2 joueurs dispersés (boulot, téléphone, trafic, voiture garée recalée) ; GIFU moteur tournant loin ;
montage loin de l'hôte (digest sans écart, sauvegarde juste) ; sauvegardes (toilettes, K_NOW, arrivant) sans objet coupé ; 3e
joueur à 10 min ; objet coupé puis rallumé = même état. MSC : `mscbateau2`, `mscboulons`, `SondeReprise=1`.
