# RaidlandsUiEscapeBridge Integration

## Load and test

1. Put `RaidlandsUiEscapeBridge.cs` in `oxide/plugins/`.
2. Unload the older standalone test plugin if it is still loaded.
3. Run:

```text
oxide.reload RaidlandsUiEscapeBridge
```

4. In game, run:

```text
/rluibridgetest
```

Wait roughly half a second, then press **Escape** or **Tab**. The built-in UI should close. The red test button also closes both the registered CUI and native loot session.

## Add a modal UI plugin

Add the plugin reference:

```csharp
using Oxide.Core.Plugins;

[PluginReference]
private Plugin RaidlandsUiEscapeBridge;
```

Register the UI **before** `CuiHelper.AddUi`:

```csharp
private const string UiRoot = "Raidlands.Shop.UI";

private void OpenUi(BasePlayer player)
{
    bool registered = (bool)(RaidlandsUiEscapeBridge?.Call(
        "RegisterUi",
        player,
        this,
        UiRoot,
        "OnRaidlandsUiBridgeClosed") ?? false);

    if (!registered)
    {
        player.ChatMessage("Unable to open this menu right now.");
        return;
    }

    DrawUi(player);
}
```

Add the close callback. It should clear all state owned by the UI plugin:

```csharp
private void OnRaidlandsUiBridgeClosed(BasePlayer player, string reason)
{
    _openPlayers.Remove(player.userID);
    CuiHelper.DestroyUi(player, UiRoot);
}
```

When the plugin closes normally, unregister it:

```csharp
private void CloseUi(BasePlayer player)
{
    _openPlayers.Remove(player.userID);
    CuiHelper.DestroyUi(player, UiRoot);

    RaidlandsUiEscapeBridge?.Call(
        "UnregisterUi",
        player,
        this,
        UiRoot);
}
```

The existing close button may continue calling the plugin's normal close command as long as that command runs `CloseUi`. Alternatively, use the centralized command:

```csharp
Command = "raidlandsui.close"
```

The centralized command closes every registered modal UI for that player, invokes each owner callback, destroys every registered root as a fallback, closes native loot, and removes the temporary bridge container.

## Public API

```text
RegisterUi(player, ownerPlugin, rootName, closeHookName) -> bool
UnregisterUi(player, ownerPlugin, rootName) -> bool
ClosePlayerUis(player, reason) -> bool
IsBridgeActive(player) -> bool
IsBridgeArmed(player) -> bool
GetCloseCommand() -> string
```

Pass an empty string as `closeHookName` when fallback root destruction is enough and the owning plugin has no UI state to clean.

## Current behavior

- One Escape or Tab closes all UIs registered in the active modal session.
- Opening the first registered UI closes any real native loot window already open.
- The prior loot window is not restored afterward.
- Do not register persistent HUD elements, logos, timers, hit markers, or notifications.
- Register before adding CUI so the bridge's transparent interaction shield remains behind the modal UI.
