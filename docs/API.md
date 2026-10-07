# MWCoop API for mod authors

MWCoop (co-op for My Winter Car) exposes a small public API so that **other mods can keep their own state in sync
between the players of a co-op session**. It is the static class `MWCoop.CoopApi` in `MWCoop.dll`.

- Available since MWCoop 0.43.0 (`CoopApi.Version == 1`).
- Public names and docs are in English. The rest of the MWCoop source is in French, but it is open source and not
  obfuscated, so you never need to read it to use this API.
- Works with MSCLoader mods, or any code running in the game process.

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
| `const int Version` | API version (1). Raised when members are added. |
| `const int MaxReliableBytes` | Largest reliable payload: 64 KB (split into packets and rebuilt for you). |
| `const int MaxUnreliableBytes` | Largest unreliable payload: 1000 bytes (one packet). |
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

## Tips

- **Who decides?** Let the host decide for shared things (spawned objects, a shared counter, a world state)
  and let each player decide for their own things (their tool, their UI). Send the decision, not the input.
- **Late joiners:** answer `PlayerJoined` (on the host) by sending the full current state to that player only.
- **Guests' saves:** a guest's own `SaveLoad.WriteValue` is not kept (the host's save wins). If a guest changes something
  that must be saved, send it to the host and let the host store it.

## Version history

- **1** (MWCoop 0.43.0): first version.
