# MWCoop API for mod authors

MWCoop (co-op for My Winter Car and My Summer Car) exposes a small public API so that **other mods can keep their own state in sync
between the players of a co-op session**. It is the static class `MWCoop.CoopApi` in `MWCoop.dll`.

- Available since MWCoop 0.43.0 (`CoopApi.Version == 1`). Version 2 (MWCoop 1.11.0) adds the parts shop: sell your mod's items in the game. Version 3 (MWCoop 1.15.5) adds map places: `AddMapPlace` / `RemoveMapPlace`.
- Public names and docs are in English. The rest of the MWCoop source is in French, but it is open source and not
  obfuscated, so you never need to read it to use this API.
- Works with MSCLoader mods, or any code running in the game process.
- The same API in My Winter Car and My Summer Car (MSC support since 1.1.0): one `MWCoop.dll`, nothing to change
  in your mod. Use `IsMySummerCar` / `GameName` if your mod behaves differently per game.

## What MWCoop already does for your mod

| Thing | How |
|---|---|
| **Mod files** | The host's MSCLoader mods (each DLL, its `Assets/<ID>/` and its settings `Config/Mod Settings/<ID>/`, plus `References/`) are offered to guests by the MWCoop launcher before joining. The host can mark mods as private. |
| **Mod save data** | MSCLoader's `SaveLoad` data (`Mods.txt`) is sent to guests **with the host's save**: on a guest, `SaveLoad.ReadValue` returns the host's values. |
| **Saving** | Only the host saves the world. A guest's `Setup.OnSave` still runs, but writes into the guest's MWCoop profile, which is replaced by the host's save at the next session. |
| **Game objects** | Vanilla things (cars, items, doors, time, weather, shops...) are synced by MWCoop. Objects your mod spawns are **not**: use the API below. |

## Getting the API without a hard dependency

Find the type by reflection, so your mod still loads when MWCoop is not installed:

```csharp
Type api = Type.GetType("MWCoop.CoopApi, MWCoop");   // null without MWCoop
if (api != null)
{
    int version = (int)api.GetField("Version").GetValue(null);
    bool host   = (bool)api.GetProperty("IsHost").GetValue(null, null);
}
```

Or reference `MWCoop.dll` directly (simpler, but your mod then needs MWCoop installed).

## Members

| Member | Description |
|---|---|
| `const int Version` | API version (4). Raised when members are added. |
| `const int MaxReliableBytes` | Largest reliable payload: 64 KB (split into packets and rebuilt for you). |
| `const int MaxUnreliableBytes` | Largest unreliable payload: 1000 bytes (one packet). |
| `string GameName` | "My Winter Car" or "My Summer Car" (since 1.1.0). |
| `bool IsMySummerCar` | True in My Summer Car (since 1.1.0). |
| `bool InSession` | A co-op session is running (host or guest). False in solo. |
| `bool IsHost` | This game is the host. The host has authority: save, time, weather, traffic. |
| `bool InGame` | The local player is in the game world (not the main menu). |
| `int LocalPlayerId` | Id of the local player (the host is always 0). |
| `int[] PlayerIds()` | Every player in the session, local one included, sorted. |
| `string PlayerName(int id)` | A player's nickname ("" if unknown). |
| `void Send(string channel, byte[] data)` | Reliable, ordered, to every other player. |
| `void SendTo(int playerId, string channel, byte[] data)` | Reliable, ordered, to one player. |
| `void SendUnreliable(string channel, byte[] data)` | Unreliable (may be lost, no order), to every other player. For frequent small updates. |
| `void Listen(string channel, Action<int, byte[]> handler)` | Receive a channel: `handler(senderId, data)`. |
| `void StopListening(string channel, Action<int, byte[]> handler)` | Stop receiving. |
| `event Action<int> PlayerJoined / PlayerLeft` | A player joined or left (players already there are not announced: read `PlayerIds()` once). |
| `void OnPlayerJoined(Action<int>) / OnPlayerLeft(Action<int>)` | Same events, as methods (easier through reflection). |
| `bool ShopAvailable` | **v2.** The CARDINAL VARAOSAT parts shop exists in this game (My Winter Car, add-on on; the host's choice applies to everyone). |
| `bool AddShopItem(string id, string name, float price, GameObject model, Action<int, bool, Vector3, Quaternion> onBought)` | **v2.** Puts an item of your mod on sale in the parts shop. See below. |
| `void RemoveShopItem(string id)` | **v2.** Takes it off the shelves. |
| `void AddMapPlace(string key, string alias, string nameFr, string nameEn, Vector3 at, Vector3 face)` | **v3.** Adds a place of your mod to the M map and to the `/place` cheat (`key` and `alias`: one lower-case word each; `at`: where the player is put, the ground below is found; `face`: a point he looks at). Same key again: replaced. Call it after each game load. |
| `void RemoveMapPlace(string key)` | **v3.** Removes it. |
| `string[] F10Cars()` | **v4.** Cars of the F10 menu installed here (the VAZ-2108 and the add-on cars), as `"id|name"`. Empty when the VAZ-2108 add-on is off. |
| `bool ShowF10Car(int carId, Vector3 position, float yaw)` | **v4.** Takes the local player's F10 car `carId` and puts it at `position` (0.8 m above the ground), heading `yaw` degrees. The player's current F10 car is moved if it is the same model, else given back first. Solo and co-op. False when refused (a message is shown). |

Notes:

- **Use your mod ID as the channel name**, or `"<ModID>.<something>"` if you need several.
- Everything runs on Unity's main thread; handlers are called during MWCoop's update.
- Guests talk to each other through the host (MWCoop relays), so every message has a real sender id.
- Reliable messages sent to a channel **nobody listens to yet** (for example, the other player's game is still
  loading your mod) are kept (up to 1 MB in total) and delivered as soon as you call `Listen`. Unreliable ones are dropped.
- Nothing is sent in solo: `Send` does nothing when `InSession` is false.
- The bytes are yours: MWCoop does not look inside. Keep messages small and send changes, not the whole state every frame.

## Example (MSCLoader, reflection)

The host owns a value; guests ask for it when they are ready, and the host answers.

```csharp
using System;
using System.Text;
using MSCLoader;

public class MyMod : Mod
{
    public override string ID => "MyMod";
    // ...

    Type api;
    bool asked;
    int score;

    public override void ModSetup()
    {
        SetupFunction(Setup.OnLoad, OnLoad);
        SetupFunction(Setup.Update, Update_);
    }

    void OnLoad()
    {
        api = Type.GetType("MWCoop.CoopApi, MWCoop");
        if (api != null) Call("Listen", ID, new Action<int, byte[]>(OnData));
    }

    void Update_()
    {
        // a guest, once in the game, asks the host (id 0) for the current value
        if (api == null || asked || !Is("InSession") || Is("IsHost") || !Is("InGame")) return;
        asked = true;
        Call("SendTo", 0, ID, Encoding.UTF8.GetBytes("get"));
    }

    void OnData(int from, byte[] data)
    {
        string msg = Encoding.UTF8.GetString(data);
        if (Is("IsHost") && msg == "get") Call("SendTo", from, ID, BitConverter.GetBytes(score));
        else if (!Is("IsHost") && data.Length == 4) score = BitConverter.ToInt32(data, 0);
    }

    // when the host changes the value: tell everyone
    void SetScore(int v)
    {
        score = v;
        if (api != null && Is("IsHost")) Call("Send", ID, BitConverter.GetBytes(score));
    }

    bool Is(string property) => (bool)api.GetProperty(property).GetValue(null, null);
    void Call(string method, params object[] args) => api.GetMethod(method).Invoke(null, args);
}
```

Cache the `MethodInfo`s if you call them every frame.

## Example (direct reference)

```csharp
using MWCoop;

CoopApi.Listen("MyMod", (from, data) => { /* ... */ });
CoopApi.OnPlayerJoined(id => CoopApi.SendTo(id, "MyMod", MyState()));   // bring a newcomer up to date
```

## Selling your items in the parts shop (version 2)

MWCoop builds a small parts shop, **CARDINAL VARAOSAT**, at the ski resort camping area (My Winter Car). Its left wing
(CARDINAL) is for mods: any mod can put items on its shelves with `AddShopItem`. The right wing sells used Rivett parts
(MWCoop's own, restocked every 3 game days).

- `id`: unique, and **the same on every player's game** (e.g. `"MyMod.Exhaust"`). Adding the same id again updates the item.
- `name`, `price` (mk): shown when a player looks at the item, and on its price tag.
- `model`: a GameObject shown on the shelf. Only its meshes and materials are copied; your object is never touched,
  it may be inactive (a prefab or a hidden template is fine). Big models go on a display stand, others are scaled to fit a
  shelf slot. `null` = no model.
- `onBought(buyerId, boughtHere, position, rotation)`: called on **every** player's game, in the same order, when someone
  buys the item (the buyer has already paid from their own wallet). Spawn your object at `position` / `rotation` (on the
  NOUTO pick-up pallet of the wing).
  - If your mod keeps its objects in sync by itself (it spawns them the same way on every game), spawn on every game.
  - If you prefer, spawn only where `boughtHere` is true and send the result to the others with `Send`.
- Unlimited stock. Items stay registered across game loads: call `AddShopItem` once (in `OnLoad`, for example).
- Nothing is lost if the shop is off (`ShopAvailable == false`): the items just don't show.
- Every player needs your mod for the item to show up on their game (MWCoop offers the host's mods to guests).

```csharp
// direct reference
CoopApi.AddShopItem("MyMod.Exhaust", "Sport exhaust", 450f, exhaustPrefab, (buyer, here, pos, rot) =>
{
    GameObject e = GameObject.Instantiate(exhaustPrefab, pos, rot);
    e.SetActive(true);
    // ... your own part logic, the same on every game
});

// reflection (no hard dependency); there is a single AddShopItem method, so GetMethod finds it
Type api = Type.GetType("MWCoop.CoopApi, MWCoop");
if (api != null && (int)api.GetField("Version").GetValue(null) >= 2)
    api.GetMethod("AddShopItem").Invoke(null, new object[] { "MyMod.Exhaust", "Sport exhaust", 450f, exhaustPrefab,
        new Action<int, bool, Vector3, Quaternion>(OnExhaustBought) });
```

## Tips

- **Who decides?** Let the host decide for shared things (spawned objects, a shared counter, a world state)
  and let each player decide for their own things (their tool, their UI). Send the decision, not the input.
- **Late joiners:** answer `PlayerJoined` (on the host) by sending the full current state to that player only.
- **Guests' saves:** a guest's own `SaveLoad.WriteValue` is not kept (the host's save wins). If a guest changes something
  that must be saved, send it to the host and let the host store it.

## Version history

- **1** (MWCoop 0.43.0): first version.
- **2** (MWCoop 1.11.0): parts shop: `ShopAvailable`, `AddShopItem`, `RemoveShopItem`.
- **3** (MWCoop 1.15.5): map places: `AddMapPlace`, `RemoveMapPlace`.
- **4** (MWCoop msc-1.15.25): F10 cars: `F10Cars`, `ShowF10Car`.
