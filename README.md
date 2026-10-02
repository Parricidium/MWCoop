<p align="center">
  <img src="https://img.shields.io/badge/status-PRE--ALPHA-red?style=for-the-badge" alt="Pre-alpha">
  <a href="https://github.com/Parricidium/MWCoop/releases"><img src="https://img.shields.io/github/v/release/Parricidium/MWCoop?include_prereleases&label=Download&style=for-the-badge" alt="Download"></a>
</p>

<p align="center">
  <a href="https://store.steampowered.com/app/4164420/"><img src="https://img.shields.io/badge/Buy%20My%20Winter%20Car-Steam-1b2838?style=for-the-badge&logo=steam&logoColor=white" alt="My Winter Car on Steam"></a>
</p>
<p align="center">
  <b>This mod needs a legitimately owned copy of My Winter Car.</b> No game data is included here.
</p>

> [!WARNING]
> **PRE-ALPHA, rebuilt from scratch.** Players see each other, share the host's time and weather, doors and
> switches, see each other's vehicles move, and rebuild the car together part by part and bolt by bolt;
> shop together with separate wallets; paint, NPCs and quests are not synced yet. Expect bugs and send your logs (round LOGS button of `MWCoop.exe`).

<p align="center">
  <img src="docs/img/avatar.jpg" width="100%" alt="The host seen by a guest">
</p>
<p align="center"><i>The host ("Joueur1") as seen by a guest, wearing an outfit picked among the game's NPCs.</i></p>

<p align="center">
  <img src="docs/img/lanceur.png" width="80%" alt="The MWCoop launcher">
</p>
<p align="center"><i>The launcher: host, join or play solo; it keeps the mod up to date by itself.</i></p>

# MWCoop — My Winter Car in co-op

A co-op mod for **My Winter Car** (Steam), by the authors of [VCCoop](https://github.com/Parricidium/VCCoop),
[SACoop](https://github.com/Parricidium/SACoop) and [JACoop](https://github.com/Parricidium/JACoop). One player hosts
their world; friends join **that** world: same save, same time, same weather — and, as the roadmap goes, the same
car being rebuilt bolt by bolt.

*[Version française plus bas.](#version-française)*

## Install

1. Open the game folder (Steam: right-click *My Winter Car* > *Manage* > *Browse local files*).
2. Copy everything from the [latest release](https://github.com/Parricidium/MWCoop/releases) zip next to
   `mywintercar.exe`: `version.dll`, `MWCoop.exe` and the `MWCoop` folder.
3. Start `MWCoop.exe`. It updates the mod by itself on every start.

No other loader is needed (MWCoop does not use MSCLoader).

## Playing

- **Host**: start your game; friends join it. Open UDP port **7870** on your router, or use a VPN
  (Radmin VPN, ZeroTier…) and share your VPN address.
- **Join**: enter the host's address. Your game receives the host's save and enters their game by itself as soon as
  the host is playing. **Your own save is never touched**: guests play in a separate profile (`MWCoop\profils\invite`).
- In game: **F10** opens the co-op menu (players and ping, outfit, chat, *go to a player*), **T** opens the chat.

## Status

| Done | Next |
|---|---|
| Own loader (`version.dll`), auto-updating launcher | Paint, adjustments (carburettor...), fluids, wear |
| Players visible (NPC outfits), walking, crouching | Items, shopping bags, things in hands |
| Host's save sent to guests (isolated profile) | Engine sound, lights and wheels of driven vehicles |
| Host's time, day and weather (clouds, rain, temperature) | NPCs, traffic, quests completed for everyone |
| Doors, light switches, TV and CD player | Shared income, separate wallets; action animations |
| Vehicles: the driver's car moves for everyone, parked ones stay in place | |
| Car mechanics: parts installed/removed, every bolt turn, parts carried and dropped | |
| Money: income shared by everyone, separate wallets; shopping seen by all (bags, items) | |
| Chat, F10 menu | |

The full list is in [docs/FEUILLE-DE-ROUTE.md](docs/FEUILLE-DE-ROUTE.md) (French).

## How it works

My Winter Car runs on Unity 5.0 with its logic in PlayMaker state machines. `version.dll` is a proxy loaded by the game:
it hooks Unity's Mono, loads `MWCoop\MWCoop.dll` on the main thread, and can isolate a profile (saves, registry,
single-instance lock) so a guest's own save stays untouched. The mod then drives the game through its state machines
(time skip events, menu buttons, weather objects) and replicates the host's world over UDP.

## Building

Requirements: Visual Studio 2022 Build Tools (C++ x64), .NET SDK 8 (only its C# compiler is used), the game installed.

```bat
build.cmd
```

`build\version.dll`, `build\MWCoop.dll` and `build\MWCoop.exe`. Set `MWC_JEU` if the game is not in the default
Steam folder. `run\make-testinstances.ps1` and `run\test.ps1` start isolated, off-screen test instances.

## Disclosure

Developed with AI assistance (Claude), directed and tested by a human maintainer. My Winter Car belongs to
Amistech Games; this project is not affiliated with them.

---

# Version française

**MWCoop** est un mod coopératif pour **My Winter Car** (Steam). Un joueur héberge son monde, ses amis rejoignent
**ce** monde : même sauvegarde, même heure, même météo — et, au fil de la feuille de route, la même voiture remontée vis
par vis.

## Installation

1. Ouvrez le dossier du jeu (Steam : clic droit sur *My Winter Car* > *Gérer* > *Parcourir les fichiers locaux*).
2. Copiez-y tout le contenu du zip de la [dernière version](https://github.com/Parricidium/MWCoop/releases), à côté de
   `mywintercar.exe` : `version.dll`, `MWCoop.exe` et le dossier `MWCoop`.
3. Lancez `MWCoop.exe`. Il met le mod à jour tout seul à chaque démarrage.

Aucun autre chargeur n'est nécessaire (MWCoop n'utilise pas MSCLoader).

## Jouer

- **Héberger** : lancez votre partie, vos amis la rejoignent. Ouvrez le port UDP **7870** sur votre box, ou utilisez un
  VPN (Radmin VPN, ZeroTier…) et donnez votre adresse VPN.
- **Rejoindre** : entrez l'adresse de l'hôte. Votre jeu reçoit la sauvegarde de l'hôte et entre dans sa partie tout seul
  dès que l'hôte joue. **Votre propre sauvegarde n'est jamais touchée** : l'invité joue dans un profil à part
  (`MWCoop\profils\invite`).
- En jeu : **F10** ouvre le menu coop (joueurs et ping, apparence, tchat, *aller vers un joueur*), **T** le tchat.

## Avancement

Fait : chargeur maison, lanceur à mise à jour automatique, joueurs visibles (tenues des PNJ), sauvegarde de l'hôte
envoyée aux invités, heure/jour/météo de l'hôte, portes et interrupteurs, véhicules conduits vus par tous,
mécanique (pièces montées/démontées, chaque cran de vis, pièces portées et lâchées), revenus partagés avec
porte-monnaie séparés, achats vus par tous (sacs, articles), tchat, menu F10.
À venir : peinture et réglages, son du moteur et phares des véhicules conduits, objets et achats, portes et lumières, PNJ et circulation, quêtes
validées pour tous, revenus partagés avec porte-monnaie séparés, animations des actions.
La liste complète : [docs/FEUILLE-DE-ROUTE.md](docs/FEUILLE-DE-ROUTE.md).

Développé avec l'aide d'une IA (Claude), dirigé et testé par un humain. My Winter Car appartient à Amistech Games ;
ce projet n'a aucun lien avec eux.
