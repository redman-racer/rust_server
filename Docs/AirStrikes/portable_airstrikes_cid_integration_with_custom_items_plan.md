# PortableAirstrikes + CustomItemDefinitions Integration Plan

## Implementation Status - 2026-07-08

Implemented in `PortableAirstrikes` v0.1.22.

- `CustomItemDefinitions.cs` was patched for the current Rust/Oxide build by replacing direct `Translate.allServerTranslations` access with reflection.
- `PortableAirstrikes.cs` now registers `raidlands.airstrike.designator` as a CID item parented to `tool.binoculars`.
- The item keeps the player-facing name `Airstrike Targeting Binoculars`.
- The existing binocular ping/default-strike flow is unchanged; CID only replaces the item identity/icon layer.
- The generated icon is now runtime data at `oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png`, with the source/reference copy still at `Docs/AirStrikes/assets/airstrike-targeting-binoculars.png`.
- Admin verification is available through `/strike debug item`.
- Local compile checks passed for both `oxide/plugins/CustomItemDefinitions.cs` and `oxide/plugins/PortableAirstrikes.cs`.

## Goal

Wrap the Custom Item Definitions (CID) library into `PortableAirstrikes.cs` so Raidlands gets a real custom airstrike item that:

- Appears in inventory, hotbar, loot, kits, shops, and container UI as a custom item/icon.
- Uses the binoculars parent item so the player can hold it and use the normal binocular ping flow.
- Triggers the existing PortableAirstrikes targeting flow when the player pings while holding the item.
- Keeps the existing RP cost, strike selection/default system, cooldowns, loot injection, kit hook, admin give command, and audit flow.
- Falls back safely to the current skinned/name-based vanilla `tool.binoculars` item if CID is missing or disabled.

The important idea is: **CID handles item identity/icon/client mutation; PortableAirstrikes handles targeting, validation, charging, and strike execution.**

---

## Current State in `PortableAirstrikes.cs`

`PortableAirstrikes` already has most of the airstrike-item workflow built:

- `AirstrikeItemSettings` stores the current item config:
  - `DisplayName = "Airstrike Targeting Binoculars"`
  - `Shortname = "tool.binoculars"`
  - `SkinId`
  - `RequireCustomNameOrSkin`
  - `TreatAsTargetingTool`
- `CreateAirstrikeToken()` creates the item with `ItemManager.CreateByName(config.AirstrikeItem.Shortname, amount, config.AirstrikeItem.SkinId)`.
- `IsAirstrikeToken(Item item)` identifies the item by shortname and optionally display name or skin.
- `OnMapMarkerAdded(BasePlayer player, ProtoBuf.MapNote note)` stores the ping target. If the player is holding the airstrike tool, it calls `HandleAirstrikeToolPing(player)`.
- `HandleAirstrikeToolPing()` already does the important designator flow:
  - debounce
  - create a targeting marker
  - use the player’s saved default strike
  - open selection menu if no default exists
  - validate target type
  - call `TryPrepareStrike(player, strike.Id, false)`
- Existing systems already consume/check/give the airstrike item:
  - `/strike giveitem`
  - `portableairstrikes.giveitem`
  - `API_GiveAirstrikeItem`
  - `API_HasAirstrikeItem`
  - `OnRaidlandsCreateKitItem`
  - loot injection through `TryInjectLootToken()`

So this integration should **not rewrite the airstrike system**. It should replace the item identity layer.

---

## What CID Gives Us

The uploaded CID library is an Oxide-compatible plugin named `CustomItemDefinitions`.

CID exposes a `Register` plugin call that accepts a DTO-like object with fields such as:

- `parentItemId`
- `shortname`
- `itemId`
- `defaultName`
- `defaultDescription`
- `defaultSkinId`
- `iconFileId`
- `maxStackSize`
- `category`
- `itemMods`

CID then creates a server-side custom `ItemDefinition`, stores it in `ItemManager`, and mutates the item network data sent to the client so the client sees a valid parent item while receiving the custom name/icon/skin.

The key field for this project is:

```csharp
itemMods = parent.itemMods
```

CID clears item mods from the cloned definition and then copies whatever `itemMods` we pass in. If we do not pass the binoculars parent item mods, the item may exist visually but fail to behave like binoculars.

---

## Recommended Custom Item Identity

Use a stable custom shortname and explicit item ID.

```json
{
  "CustomShortname": "raidlands.airstrike.designator",
  "ParentShortname": "tool.binoculars",
  "CustomItemId": -395118447
}
```

Notes:

- Keep the item ID stable forever once live. Changing it later can break already-issued items.
- `ParentShortname` should stay `tool.binoculars` so the held item and ping behavior are based on binoculars.
- `CustomShortname` should be what shops/kits/loot/admin APIs use once CID is enabled.
- `SkinId` can be a workshop/custom icon skin ID.
- `IconFileId` can be used if your icon pipeline produces a Rust `FileStorage` image ID. This is often the better path for custom icons on items that do not normally accept skins.

---

## High-Level Behavior After Integration

### Normal player flow

1. Player receives `raidlands.airstrike.designator` from loot, kit, shop, or admin command.
2. Client sees it as an airstrike designator item with the configured icon/name.
3. Player places it on hotbar.
4. Player equips it.
5. `OnActiveItemChanged` sees it as the airstrike tool and shows instructions.
6. Player uses binoculars and places a ping.
7. `OnMapMarkerAdded` stores the ping and sees the player is holding the custom airstrike tool.
8. `HandleAirstrikeToolPing` calls the saved default strike or opens selection.
9. Existing validation, RP charge, item consumption, warning, cooldown, and executor systems run unchanged.

### First-use flow

If the player has no default strike:

1. Player pings while holding the designator.
2. Plugin locks the target.
3. Strike menu opens.
4. Player chooses a strike.
5. That strike is saved as their default for future designator pings.

### Fallback flow if CID is unavailable

If CID is missing or disabled and fallback is allowed:

- The plugin should keep using the legacy `tool.binoculars` + `DisplayName`/`SkinId` matching path.
- The item will not be a true custom definition, but the current airstrike behavior still works.
- Console should print a clear warning that custom icon/shortname support is unavailable.

---

## Implementation Phases

## Phase 1 — Add CID config fields

Add fields to `AirstrikeItemSettings` without removing the existing fields, so existing configs do not break.

```csharp
private class AirstrikeItemSettings
{
    [JsonProperty("Enabled")]
    public bool Enabled = true;

    [JsonProperty("DisplayName")]
    public string DisplayName = "Airstrike Designator";

    // Legacy fallback shortname. Keep this for non-CID mode.
    [JsonProperty("Shortname")]
    public string Shortname = "tool.binoculars";

    [JsonProperty("UseCustomItemDefinition")]
    public bool UseCustomItemDefinition = true;

    [JsonProperty("AllowVanillaFallbackIfCIDMissing")]
    public bool AllowVanillaFallbackIfCIDMissing = true;

    [JsonProperty("CustomShortname")]
    public string CustomShortname = "raidlands.airstrike.designator";

    [JsonProperty("CustomItemId")]
    public int CustomItemId = -395118447;

    [JsonProperty("ParentShortname")]
    public string ParentShortname = "tool.binoculars";

    [JsonProperty("IconFileId")]
    public uint IconFileId;

    [JsonProperty("ImportParentItemMods")]
    public bool ImportParentItemMods = true;

    [JsonProperty("DefaultDescription")]
    public string DefaultDescription = "Aim with the binoculars and place a ping to call your selected airstrike.";

    [JsonProperty("SkinId")]
    public ulong SkinId;

    [JsonProperty("RequireCustomNameOrSkin")]
    public bool RequireCustomNameOrSkin = true;

    [JsonProperty("RequiredAmount")]
    public int RequiredAmount = 1;

    [JsonProperty("ConsumeOnSuccessfulCall")]
    public bool ConsumeOnSuccessfulCall = true;

    [JsonProperty("AllowAdminsWithoutItem")]
    public bool AllowAdminsWithoutItem = true;

    [JsonProperty("TreatAsTargetingTool")]
    public bool TreatAsTargetingTool = true;

    [JsonProperty("ShowEquipInstructions")]
    public bool ShowEquipInstructions = true;

    [JsonProperty("ToolTargetMarkerEnabled")]
    public bool ToolTargetMarkerEnabled = true;

    [JsonProperty("ToolTargetMarkerDurationSeconds")]
    public float ToolTargetMarkerDurationSeconds = 18f;

    [JsonProperty("ToolTargetMarkerSize")]
    public float ToolTargetMarkerSize = 10f;

    [JsonProperty("ToolTargetMarkerAlpha")]
    public float ToolTargetMarkerAlpha = 0.55f;
}
```

### Normalize new config fields

Add to `NormalizeConfig()` near the existing `AirstrikeItem` normalization:

```csharp
if (string.IsNullOrWhiteSpace(config.AirstrikeItem.ParentShortname))
{
    config.AirstrikeItem.ParentShortname = "tool.binoculars";
}

if (string.IsNullOrWhiteSpace(config.AirstrikeItem.CustomShortname))
{
    config.AirstrikeItem.CustomShortname = "raidlands.airstrike.designator";
}

if (config.AirstrikeItem.CustomItemId == 0)
{
    config.AirstrikeItem.CustomItemId = -395118447;
}

if (string.IsNullOrWhiteSpace(config.AirstrikeItem.DefaultDescription))
{
    config.AirstrikeItem.DefaultDescription = "Aim with the binoculars and place a ping to call your selected airstrike.";
}
```

---

## Phase 2 — Add CID plugin reference and state

Add a plugin reference near the existing `ServerRewards` and `Economics` references:

```csharp
[PluginReference]
private Plugin CustomItemDefinitions;
```

Add state fields near the other private fields:

```csharp
private ItemDefinition airstrikeCustomItemDefinition;
private bool warnedCIDUnavailable;
```

---

## Phase 3 — Register the custom definition

Call the registration method after config/data initialization and when CID loads.

### Add calls in lifecycle hooks

In `OnServerInitialized()`:

```csharp
TryRegisterAirstrikeCustomItemDefinition();
```

In `CmdReload()` after `NormalizeConfig()` / `RegisterChatCommand()` / `InitializeExecutors()`:

```csharp
TryRegisterAirstrikeCustomItemDefinition();
```

In `OnPluginLoaded(Plugin plugin)`:

```csharp
else if (string.Equals(plugin.Name, "CustomItemDefinitions", StringComparison.OrdinalIgnoreCase))
{
    CustomItemDefinitions = plugin;
    TryRegisterAirstrikeCustomItemDefinition();
}
```

In `OnPluginUnloaded(Plugin plugin)`:

```csharp
else if (string.Equals(plugin.Name, "CustomItemDefinitions", StringComparison.OrdinalIgnoreCase) && CustomItemDefinitions == plugin)
{
    CustomItemDefinitions = null;
    airstrikeCustomItemDefinition = null;
    PrintWarning("CustomItemDefinitions unloaded. PortableAirstrikes will use vanilla fallback item matching if enabled.");
}
```

Add the CID loaded hook. CID calls `OnCIDLoaded` on all plugins when it is loaded.

```csharp
private void OnCIDLoaded(Plugin cidPlugin)
{
    if (cidPlugin != null)
    {
        CustomItemDefinitions = cidPlugin;
    }

    TryRegisterAirstrikeCustomItemDefinition();
}
```

### Add the registration method

```csharp
private bool TryRegisterAirstrikeCustomItemDefinition()
{
    if (config?.AirstrikeItem == null || !config.AirstrikeItem.Enabled)
    {
        return false;
    }

    if (!config.AirstrikeItem.UseCustomItemDefinition)
    {
        return false;
    }

    if (airstrikeCustomItemDefinition != null)
    {
        return true;
    }

    if (CustomItemDefinitions == null || !CustomItemDefinitions.IsLoaded)
    {
        WarnCIDUnavailableOnce();
        return false;
    }

    var customShortname = config.AirstrikeItem.CustomShortname?.Trim();
    if (string.IsNullOrWhiteSpace(customShortname))
    {
        PrintWarning("CID registration skipped: AirstrikeItem.CustomShortname is empty.");
        return false;
    }

    var existing = ItemManager.FindItemDefinition(customShortname);
    if (existing != null)
    {
        airstrikeCustomItemDefinition = existing;
        return true;
    }

    var parentShortname = string.IsNullOrWhiteSpace(config.AirstrikeItem.ParentShortname)
        ? "tool.binoculars"
        : config.AirstrikeItem.ParentShortname.Trim();

    var parent = ItemManager.FindItemDefinition(parentShortname);
    if (parent == null)
    {
        PrintWarning("CID registration failed: parent item definition '" + parentShortname + "' was not found.");
        return false;
    }

    try
    {
        var dto = new
        {
            parentItemId = parent.itemid,
            shortname = customShortname,
            itemId = config.AirstrikeItem.CustomItemId,
            defaultName = config.AirstrikeItem.DisplayName,
            defaultDescription = config.AirstrikeItem.DefaultDescription,
            defaultSkinId = config.AirstrikeItem.SkinId,
            iconFileId = config.AirstrikeItem.IconFileId,
            maxStackSize = 1,
            category = ItemCategory.Tool,
            itemMods = config.AirstrikeItem.ImportParentItemMods ? parent.itemMods : null,
            repairable = false,
            craftable = false,
            defaultBlueprintUnlocked = false
        };

        var registered = CustomItemDefinitions.Call("Register", dto, this) as ItemDefinition;
        if (registered == null)
        {
            PrintWarning("CID registration failed: Register returned no ItemDefinition.");
            return false;
        }

        airstrikeCustomItemDefinition = registered;
        Puts("Registered CID airstrike item '" + registered.shortname + "' itemId=" + registered.itemid + " parent=" + parent.shortname + ".");
        return true;
    }
    catch (Exception ex)
    {
        PrintWarning("CID registration failed for airstrike designator: " + ex.Message);
        return false;
    }
}

private void WarnCIDUnavailableOnce()
{
    if (warnedCIDUnavailable)
    {
        return;
    }

    warnedCIDUnavailable = true;

    if (config?.AirstrikeItem != null && config.AirstrikeItem.AllowVanillaFallbackIfCIDMissing)
    {
        PrintWarning("CustomItemDefinitions is not loaded. PortableAirstrikes will use the legacy vanilla binocular fallback item.");
    }
    else
    {
        PrintWarning("CustomItemDefinitions is required for the configured airstrike item, but it is not loaded. Airstrike item creation may fail.");
    }
}
```

---

## Phase 4 — Refactor item creation to use the custom definition

Replace direct usage of `config.AirstrikeItem.Shortname` in item creation with an effective shortname helper.

```csharp
private bool IsCIDAirstrikeItemActive()
{
    return config?.AirstrikeItem != null
        && config.AirstrikeItem.UseCustomItemDefinition
        && airstrikeCustomItemDefinition != null;
}

private string GetAirstrikeCreateShortname()
{
    if (IsCIDAirstrikeItemActive())
    {
        return config.AirstrikeItem.CustomShortname;
    }

    return string.IsNullOrWhiteSpace(config?.AirstrikeItem?.Shortname)
        ? "tool.binoculars"
        : config.AirstrikeItem.Shortname;
}

private ulong GetAirstrikeCreateSkinId()
{
    // CID applies defaultSkinId/iconFileId at the definition/network-mutation layer.
    // For vanilla fallback, keep using the configured skin ID.
    return IsCIDAirstrikeItemActive() ? 0UL : config.AirstrikeItem.SkinId;
}
```

Update `CreateAirstrikeToken(int amount)`:

```csharp
private Item CreateAirstrikeToken(int amount)
{
    TryRegisterAirstrikeCustomItemDefinition();

    var shortname = GetAirstrikeCreateShortname();
    var skinId = GetAirstrikeCreateSkinId();
    var item = ItemManager.CreateByName(shortname, Math.Max(1, amount), skinId);
    if (item == null)
    {
        // If CID failed or is missing, try the configured vanilla fallback.
        if (config.AirstrikeItem.AllowVanillaFallbackIfCIDMissing
            && !string.Equals(shortname, config.AirstrikeItem.Shortname, StringComparison.OrdinalIgnoreCase))
        {
            item = ItemManager.CreateByName(config.AirstrikeItem.Shortname, Math.Max(1, amount), config.AirstrikeItem.SkinId);
        }

        if (item == null)
        {
            return null;
        }
    }

    if (!string.IsNullOrWhiteSpace(config.AirstrikeItem.DisplayName))
    {
        item.name = config.AirstrikeItem.DisplayName;
    }

    item.MarkDirty();
    return item;
}
```

Update the failure message in `GiveAirstrikeTokensDetailed()` so it reports the effective item:

```csharp
result.Failure = "Could not create item '" + GetAirstrikeCreateShortname() + "'.";
```

---

## Phase 5 — Refactor item recognition

The custom definition should be accepted by shortname alone. The old display-name/skin check should remain only for vanilla fallback compatibility.

Update `IsAirstrikeToken(Item item)`:

```csharp
private bool IsAirstrikeToken(Item item)
{
    if (item?.info == null || config?.AirstrikeItem == null)
    {
        return false;
    }

    if (IsCIDAirstrikeItemActive())
    {
        if (ReferenceEquals(item.info, airstrikeCustomItemDefinition))
        {
            return true;
        }

        if (string.Equals(item.info.shortname, config.AirstrikeItem.CustomShortname, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    if (!string.Equals(item.info.shortname, config.AirstrikeItem.Shortname, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (!config.AirstrikeItem.RequireCustomNameOrSkin)
    {
        return true;
    }

    var displayMatches = !string.IsNullOrWhiteSpace(config.AirstrikeItem.DisplayName)
        && string.Equals(item.name ?? "", config.AirstrikeItem.DisplayName, StringComparison.OrdinalIgnoreCase);
    var skinMatches = config.AirstrikeItem.SkinId != 0UL && item.skin == config.AirstrikeItem.SkinId;
    return displayMatches || skinMatches;
}
```

Update `IsAirstrikeKitItem(string shortname, ulong skin, string displayName)`:

```csharp
private bool IsAirstrikeKitItem(string shortname, ulong skin, string displayName)
{
    if (config?.AirstrikeItem == null || string.IsNullOrWhiteSpace(shortname))
    {
        return false;
    }

    if (config.AirstrikeItem.UseCustomItemDefinition
        && string.Equals(shortname, config.AirstrikeItem.CustomShortname, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!string.Equals(shortname, config.AirstrikeItem.Shortname, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (!config.AirstrikeItem.RequireCustomNameOrSkin)
    {
        return true;
    }

    var displayMatches = !string.IsNullOrWhiteSpace(config.AirstrikeItem.DisplayName)
        && string.Equals(displayName ?? "", config.AirstrikeItem.DisplayName, StringComparison.OrdinalIgnoreCase);
    var skinMatches = config.AirstrikeItem.SkinId != 0UL && skin == config.AirstrikeItem.SkinId;
    return displayMatches || skinMatches;
}
```

This keeps old kit rows working while allowing new kit rows to use:

```text
raidlands.airstrike.designator
```

---

## Phase 6 — Keep the ping flow unchanged

Do **not** rewrite these existing methods unless testing shows the game ping hook needs improvement:

- `OnMapMarkerAdded(BasePlayer player, ProtoBuf.MapNote note)`
- `IsPlayerHoldingAirstrikeTool(BasePlayer player)`
- `IsAirstrikeTargetingToolItem(Item item)`
- `HandleAirstrikeToolPing(BasePlayer player)`
- `TryPrepareStrike(...)`
- `ValidateStrikeCall(...)`

The whole point of using the binoculars parent definition is that the held item should still generate the normal binocular/team/map ping. Once `IsAirstrikeToken()` recognizes the custom definition, the existing `OnMapMarkerAdded` path should call the airstrike logic.

---

## Phase 7 — Loot, kits, shops, and APIs

### Loot tables

No separate loot-table system is needed. `TryInjectLootToken()` already calls `CreateAirstrikeToken()`. Once `CreateAirstrikeToken()` creates the CID item, loot injection automatically uses the custom item.

Recommended config:

```json
"LootDistribution": {
  "Enabled": true,
  "ContainerRules": {
    "crate_normal": {
      "Chance": 0.03,
      "MinAmount": 1,
      "MaxAmount": 1
    },
    "crate_elite": {
      "Chance": 0.08,
      "MinAmount": 1,
      "MaxAmount": 2
    }
  }
}
```

### Kits

Preferred options:

1. Best: have kits call `API_GiveAirstrikeItem(playerId, amount)`.
2. Good: have your kit item creation pipeline pass `raidlands.airstrike.designator` into `OnRaidlandsCreateKitItem`.
3. Fallback: use `tool.binoculars` with the configured display name/skin if CID is disabled.

### Shops

For custom items, avoid shop plugins that only serialize vanilla shortname/skin/name unless they are known to support CID-created `ItemDefinition`s.

Best shop integration:

- Store purchase entry as an airstrike-item product.
- On purchase success, call:

```csharp
PortableAirstrikes?.Call("API_GiveAirstrikeItem", player.userID, amount);
```

This prevents shop plugins from accidentally creating a plain vanilla binocular item instead of the custom CID item.

---

## Phase 8 — Example final config

```json
"AirstrikeItem": {
  "Enabled": true,
  "DisplayName": "Airstrike Designator",
  "Shortname": "tool.binoculars",
  "UseCustomItemDefinition": true,
  "AllowVanillaFallbackIfCIDMissing": true,
  "CustomShortname": "raidlands.airstrike.designator",
  "CustomItemId": -395118447,
  "ParentShortname": "tool.binoculars",
  "IconFileId": 0,
  "ImportParentItemMods": true,
  "DefaultDescription": "Aim with the binoculars and place a ping to call your selected airstrike.",
  "SkinId": 1234567890,
  "RequireCustomNameOrSkin": true,
  "RequiredAmount": 1,
  "ConsumeOnSuccessfulCall": true,
  "AllowAdminsWithoutItem": true,
  "TreatAsTargetingTool": true,
  "ShowEquipInstructions": true,
  "ToolTargetMarkerEnabled": true,
  "ToolTargetMarkerDurationSeconds": 18.0,
  "ToolTargetMarkerSize": 10.0,
  "ToolTargetMarkerAlpha": 0.55
}
```

If using `IconFileId`, set `SkinId` to `0` unless you intentionally want both.

If using a workshop/custom skin icon, set `SkinId` to that skin ID and leave `IconFileId` as `0`.

---

## Phase 9 — Admin commands and test sequence

### Install order

1. Put `CustomItemDefinitions.cs` in `oxide/plugins/`.
2. Put updated `PortableAirstrikes.cs` in `oxide/plugins/`.
3. Reload CID first.
4. Reload PortableAirstrikes second.

```text
oxide.reload CustomItemDefinitions
oxide.reload PortableAirstrikes
```

### Grant permissions

```text
oxide.grant group default portableairstrikes.use
oxide.grant group default portableairstrikes.use.bee
oxide.grant group default portableairstrikes.use.grenade
oxide.grant group default portableairstrikes.use.utility
oxide.grant group default portableairstrikes.use.fire
oxide.grant group admin portableairstrikes.admin
```

Add more strike permissions as needed.

### Give test item

```text
portableairstrikes.giveitem Carl 1
```

or in chat as admin:

```text
/strike giveitem Carl 1
```

### Verify item identity

The item should appear as:

```text
Airstrike Designator
```

Expected server-side shortname:

```text
raidlands.airstrike.designator
```

Expected held behavior:

```text
binoculars-style aiming / pinging
```

### First ping test

1. Equip the item.
2. Aim through binoculars.
3. Place a ping.
4. Expected result: plugin says target locked and opens the strike menu because no default is set.
5. Choose a default strike.

### Second ping test

1. Equip the item again.
2. Ping a valid target.
3. Expected result: plugin calls the saved default strike automatically.

### Debug commands

```text
/strike list
/strike balance
/strike status
/strike debug target
/strike debug history 5
```

---

## Phase 10 — Rollback plan

If CID causes issues:

1. Set:

```json
"UseCustomItemDefinition": false,
"AllowVanillaFallbackIfCIDMissing": true,
"Shortname": "tool.binoculars",
"RequireCustomNameOrSkin": true
```

2. Reload PortableAirstrikes.
3. Give players the legacy item again if needed.

Do not remove CID permanently while custom items still exist in player inventories unless you are ready to replace/clean those items. Without CID loaded, saved custom item definitions may fall back or fail to display correctly.

---

## Important Edge Cases

### CID missing at boot

Expected behavior:

- Plugin logs a warning.
- If fallback is enabled, admin give/loot/kits create the legacy binoculars item.
- If fallback is disabled, item creation fails loudly instead of silently giving the wrong item.

### Existing legacy airstrike binoculars

Old `tool.binoculars` items with the correct display name/skin should still work because `IsAirstrikeToken()` keeps the fallback path.

### Custom item reload

CID unregisters definitions when the owning plugin unloads. On PortableAirstrikes reload, `TryRegisterAirstrikeCustomItemDefinition()` should recreate the definition.

### Item ID collision

If CID registration fails with an item ID collision:

1. Pick a new negative item ID.
2. Update config.
3. Do this before giving the item to real players.
4. Do not change item ID after launch unless you also migrate/replace existing items.

### Icon does not show

Try in this order:

1. Confirm CID registered successfully.
2. Confirm the item is created as `raidlands.airstrike.designator`, not `tool.binoculars`.
3. Confirm `IconFileId` or `SkinId` is valid.
4. If `SkinId` does not work for binoculars, use the `IconFileId` path instead.
5. Reload CID first, then PortableAirstrikes.
6. Reconnect client if cached icons appear stale.

### Ping does not trigger

Check:

1. Does the item equip like binoculars?
2. Does `OnActiveItemChanged` show the airstrike instructions?
3. Does `IsAirstrikeToken(player.GetActiveItem())` return true?
4. Does `OnMapMarkerAdded` fire when placing the ping?
5. Does `latestTargets[player.userID]` update?
6. Is `TreatAsTargetingTool` true?
7. Is `RequireBinocularPing`/`MaxPingAgeSeconds` blocking validation?

If `OnMapMarkerAdded` does not fire reliably for the binocular ping, add a temporary debug log in the hook and verify Rust’s ping hook signature for the current server build. Do not change the item layer until the hook is confirmed.

---

## Acceptance Checklist

- [ ] `CustomItemDefinitions.cs` loads without compile errors on Oxide.
- [ ] `PortableAirstrikes.cs` compiles with the new plugin reference.
- [ ] Console logs successful CID registration.
- [ ] Admin give command creates `raidlands.airstrike.designator`.
- [ ] Item icon/name appears correctly in inventory and hotbar.
- [ ] Item can be placed in belt and equipped.
- [ ] Equipping item shows the airstrike help message.
- [ ] Ping while holding item stores target.
- [ ] First ping opens default-selection menu.
- [ ] Selecting a strike saves default.
- [ ] Second ping automatically calls default strike.
- [ ] RP charge works.
- [ ] Item consumption works.
- [ ] Refund on cancel/failure restores item or notes failure.
- [ ] Loot injection creates the custom item.
- [ ] Kit creation creates the custom item.
- [ ] Legacy fallback item still works if CID is disabled.
- [ ] Removing/reloading PortableAirstrikes does not leave broken UI or active strike state.

---

## Codex Implementation Prompt

Use this exact instruction if handing the task to a coding agent:

```text
Update PortableAirstrikes.cs to integrate the uploaded CustomItemDefinitions.cs library.

Do not rewrite the airstrike execution system. Preserve the existing targeting, selection, default strike, RP, cooldown, loot, kit, audit, and warning systems.

Add CID support as an item-definition layer:
- Add AirstrikeItem config fields: UseCustomItemDefinition, AllowVanillaFallbackIfCIDMissing, CustomShortname, CustomItemId, ParentShortname, IconFileId, ImportParentItemMods, DefaultDescription.
- Add [PluginReference] private Plugin CustomItemDefinitions;
- Add ItemDefinition airstrikeCustomItemDefinition state.
- Register a CID item named raidlands.airstrike.designator using parent tool.binoculars, stable item id -395118447, category ItemCategory.Tool, maxStackSize 1, defaultName/defaultDescription from config, defaultSkinId/IconFileId from config, and itemMods = parent.itemMods.
- Register during OnServerInitialized, OnCIDLoaded, and when CustomItemDefinitions loads.
- Clear custom definition state when CID unloads.
- Refactor CreateAirstrikeToken to create the CID shortname when registered, otherwise fallback to the legacy Shortname/SkinId item if allowed.
- Refactor IsAirstrikeToken so the CID custom shortname or custom ItemDefinition is enough to identify the airstrike item, while preserving the old fallback name/skin matching path for tool.binoculars.
- Refactor IsAirstrikeKitItem similarly.
- Keep OnMapMarkerAdded and HandleAirstrikeToolPing behavior unchanged except for using the updated IsAirstrikeToken identity logic.
- Add useful warnings and debug logs for CID missing, parent item missing, item ID collision, and registration failure.
- Make sure existing configs migrate without breaking.
```
