Portable Airstrikes binocular stack/icon and rotor-wash client error fix - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.29.
- Added `ConfigVersion=26`.
- Restored the intended charge-backed item behavior: `MaxChargesPerItem` now defaults/clamps to `65535`.
- Old physical binocular stacks such as inventory `x25` are normalized on reload/reconnect into one physical item with stored charges in `instanceData.dataInt`.
- If an old stacked airstrike binocular is active while it is repaired, the plugin clears the active held item to force the client viewmodel to refresh.
- `portableairstrikes.giveitem <playerNameOrSteamId> 25` now reports/gives 25 charges on one physical item instead of 25 physical binoculars.
- Existing CID airstrike definitions are refreshed on reload so the current PNG `iconFileId` and stack metadata are rebuilt instead of reusing a stale vanilla-looking definition.
- Direct rotor-wash effect sends were removed because both rotor-wash prefabs live in non-autoloaded `AssetScene-props.other`.
- Physical payload spawning, RP/token/cooldown flow, warnings, webhooks, and strike damage were left unchanged.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Runtime dependencies to keep uploaded:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png
- oxide/config/StackSizeController.json

Local source hash:
- SHA256: 4F9ECC49167F4FCCA54FFC6EFC376D6ACB5C9BB330E1C02ABA34BA3565A45C78

Reload after upload:
- oxide.reload CustomItemDefinitions
- oxide.reload PortableAirstrikes

Immediate live repair smoke:
- Confirm the reload banner reports v0.1.29.
- Run `/strike debug item`.
- Expected: `CID loaded=True`, `registered=True`, `actualStack=1`, `maxCharges=65535`, and `icon=<fileId> from ...airstrike-targeting-binoculars.png`.
- If you are holding old stacked binoculars, switch slots once after reload; the plugin should also clear the active item if it repairs the currently held stack.
- Existing inventory `x25` airstrike binocular stacks should become one physical item named like `Airstrike Targeting Binoculars x25` without a Rust stack count overlay.
- Run `portableairstrikes.giveitem <playerNameOrSteamId> 25`.
- Expected: one physical item appears with 25 stored charges, not an inventory stack of 25 binoculars.
- Run a drone/aircraft visual strike.
- Expected: no new client overlay entries for `rotorwashparticles_small.prefab` or `rotorwashparticles_large.prefab`.

If the icon is still vanilla:
- Confirm `oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png` exists on the live server.
- Reload in this order again: `oxide.reload CustomItemDefinitions`, then `oxide.reload PortableAirstrikes`.
- Run `/strike debug item` and capture the `icon=` and `createShortname=` fields.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.29 against `RustDedicated_Data/Managed`.
- Confirmed both rotor-wash prefabs are in non-autoloaded `AssetScene-props.other` in `Bundles/AssetSceneManifest.json`.
- Confirmed remaining flyover cue prefabs used by this path are in autoloaded asset scenes.

Known live-verification boundary:
- Local compile verifies code shape only. The live server should confirm icon mutation, old-stack repair, and the held-viewmodel refresh in a connected client.
