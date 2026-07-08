Portable Airstrikes charge-backed binoculars MoveItem fix - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.25.
- Added `ConfigVersion=25`.
- Actual airstrike binocular item stack size is back to `1`.
- Added `AirstrikeItem.MaxChargesPerItem`, default `65535`.
- Airstrike binocular charge count is now stored on the item in `instanceData.dataInt`.
- `portableairstrikes.giveitem <playerNameOrSteamId> 25` now creates one physical item named `Airstrike Targeting Binoculars x25` instead of a Rust inventory stack of 25.
- Successful strikes consume one stored charge; the item is removed when charges reach zero.
- Existing old airstrike stacks are normalized on plugin reload and player reconnect from `amount > 1` into `amount=1` plus stored charges.
- StackSizeController fallback sizes for `tool.binoculars` and `raidlands.airstrike.designator` are back to `1`.
- Existing automatic raycast targeting, scrollable picker, RP/cooldown behavior, warnings, webhooks, and strike executors were left unchanged.

Why:
- Live testing showed even `65535` was not the issue. The Rust inventory client can still throw `AssertionException: split_Amount <= 0` / `RPC Error in MoveItem` when moving a binocular-derived holdable item with physical stack amount greater than 1.
- The safe dependency-free path is to keep Rust's physical item amount at 1 and let PortableAirstrikes own the charge count.

Rust server plugin/config files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/config/StackSizeController.json

Runtime dependencies to keep from the v0.1.22 CID pass:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_CHARGE_BACKED_BINOCULARS_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_STACK_CAP_FIX_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Reload after upload:
- oxide.reload CustomItemDefinitions
- oxide.reload StackSizeController
- oxide.reload PortableAirstrikes

Immediate live repair smoke:
- Confirm the banner reports v0.1.25.
- Run `/strike debug item`.
- Expected: `actualStack=1` and `maxCharges=65535`.
- Give yourself charges with `portableairstrikes.giveitem <playerNameOrSteamId> 25`.
- Expected: one physical item appears, named `Airstrike Targeting Binoculars x25`.
- Move the item between inventory slots and containers.
- Expected: no disconnect, no `split_Amount <= 0`, and the item remains movable.
- Confirm one valid strike consumes one charge and updates the item name/count.

If an old item still disconnects:
- After reload, reconnect once so the inventory normalization hook runs again.
- If one specific old item still crashes on move, remove/drop that old item and regive it with `portableairstrikes.giveitem <playerNameOrSteamId> 25`.
- Do not set StackSizeController or CID max stack above `1` for the airstrike binocular item; charges are now plugin-managed.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- JSON parse check passed for `oxide/config/StackSizeController.json`; both airstrike stack entries report actual stack size `1`.
- Direct trailing-whitespace scan passed for changed plugin/config/docs/upload files.

Known live-verification boundary:
- Local compile verifies code shape only. The live server should confirm that moving the charge-backed item no longer trips the Rust `MoveItem` assertion.
