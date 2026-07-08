Portable Airstrikes stacked binoculars and automatic picker targeting - 2026-07-08

Superseded:
- Do not use this note as the latest upload target. v0.1.24 lowered the stack cap from `2147483647` to `65535` after live Rust `MoveItem` testing showed `int.MaxValue` could disconnect players with `AssertionException: split_Amount <= 0`.
- Use `UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_STACK_CAP_FIX_2026-07-08.md` instead.

What changed:
- PortableAirstrikes is now v0.1.23.
- Added `ConfigVersion=23`.
- Added `AirstrikeItem.MaxStackSize`, default `2147483647`, and pass it through CID as the custom item max stack size.
- Existing CID airstrike definitions are corrected to the configured max stack size when reused after reload.
- Admin/API/Kits/loot item creation now creates stack amounts instead of one single item per amount.
- Pinging while holding `Airstrike Targeting Binoculars` now attempts the same raycast/entity target capture that `/strike debugping` used, then falls back to the map-note position if no raycast hit exists.
- Normal player-facing target guidance now tells players to aim and ping with the binoculars instead of using `/strike debugping`.
- The `/strike` selection modal now uses a vertical CUI scroll view and renders all enabled target-compatible strikes, including locked rows while `Selection.ShowLockedStrikes=true`.
- Updated StackSizeController fallback stack sizes for `tool.binoculars` and `raidlands.airstrike.designator` to `2147483647`.
- Successful strikes still consume one binocular item through the existing economy path.

Rust server plugin/config files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/config/StackSizeController.json

Runtime dependencies to keep from the v0.1.22 CID pass:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_STACKED_BINOCULARS_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Reload after upload:
- oxide.reload CustomItemDefinitions
- oxide.reload StackSizeController
- oxide.reload PortableAirstrikes

Minimum setup:
- Grant basic use: `oxide.grant group default portableairstrikes.use`
- Grant strike family permissions as needed, for example `oxide.grant group default portableairstrikes.use.grenade`
- Give test items from server console: `portableairstrikes.giveitem <playerNameOrSteamId> 25`

Stack/binocular smoke:
- Upload `oxide/plugins/PortableAirstrikes.cs` and `oxide/config/StackSizeController.json`.
- Run the reload commands above.
- Confirm the banner reports v0.1.23.
- Run `/strike debug item`.
- Expected: CID enabled=True, CID loaded=True, registered=True, definition includes `raidlands.airstrike.designator`, and max stack behavior is active.
- Give yourself 25 items with `portableairstrikes.giveitem <playerNameOrSteamId> 25`.
- Expected: the items can exist as one stack of 25.
- Equip `Airstrike Targeting Binoculars`.
- Aim at a ground target and place a ping.
- Expected with no saved default: a target marker appears and the scrollable `/strike` picker opens.
- Scroll through the picker and confirm that all enabled target-compatible strikes are visible, including locked rows.
- Confirm a valid strike, such as `beancan_drop`.
- Expected: the selected strike is saved as your airstrike binocular default, one item is consumed, and the strike starts through the normal RP/item/cooldown path.
- Place a second ping while holding the item.
- Expected with a saved default: the default strike is attempted immediately without `/strike debugping`.
- Aim directly at a vehicle and place a ping.
- Expected for vehicle strikes: the stored target has vehicle entity tracking when the raycast hits the vehicle.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- JSON parse check passed for `oxide/config/StackSizeController.json`.
- Direct trailing-whitespace scan passed for changed plugin/config/docs/upload files.

Known live-verification boundary:
- Local compile verifies code shape only. The live Rust server still needs reload confirmation that CID exposes the stack size to clients, the scroll wheel moves the CUI list as expected, and the automatic raycast target hits the intended entity while the CID item is active.
