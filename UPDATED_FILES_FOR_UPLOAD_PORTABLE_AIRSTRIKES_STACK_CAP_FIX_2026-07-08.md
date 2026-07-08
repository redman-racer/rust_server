Portable Airstrikes inventory-safe stack cap fix - 2026-07-08

Superseded:
- Do not use this note as the latest upload target. v0.1.25 keeps the actual Rust item stack size at `1` and stores airstrike charges in item metadata because live testing still showed `RPC Error in MoveItem` with physical binocular stacks.
- Use `UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_CHARGE_BACKED_BINOCULARS_2026-07-08.md` instead.

What changed:
- PortableAirstrikes is now v0.1.24.
- Added `ConfigVersion=24`.
- Changed `AirstrikeItem.MaxStackSize` default from `2147483647` to `65535`.
- Added a hard clamp so live configs that already saved `AirstrikeItem.MaxStackSize=2147483647` normalize down to `65535` on reload.
- Existing CID airstrike definitions are corrected to the clamped stack size when reused after reload.
- Updated StackSizeController fallback stack sizes for `tool.binoculars` and `raidlands.airstrike.designator` to `65535`.
- This keeps airstrike binoculars highly stackable while avoiding the live Rust inventory `AssertionException: split_Amount <= 0` / `RPC Error in MoveItem` disconnect triggered by `int.MaxValue`.
- Existing stack creation, one-item consume, automatic raycast targeting, scrollable picker, RP/cooldown behavior, warnings, webhooks, and strike executors were left unchanged.

Rust server plugin/config files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/config/StackSizeController.json

Runtime dependencies to keep from the v0.1.22 CID pass:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_STACK_CAP_FIX_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Reload after upload:
- oxide.reload CustomItemDefinitions
- oxide.reload StackSizeController
- oxide.reload PortableAirstrikes

Immediate live repair smoke:
- Confirm the banner reports v0.1.24.
- Run `/strike debug item`.
- Give yourself a fresh stack with `portableairstrikes.giveitem <playerNameOrSteamId> 25`.
- Move the stack between inventory slots and containers.
- Expected: no disconnect, no `split_Amount <= 0`, and the stack remains movable.
- Confirm one valid strike still consumes one item from the stack.

If an old stack remains glitchy:
- After the reload, have the affected player reconnect once.
- If that specific item still disconnects on move, remove/drop that old stack and regive it with `portableairstrikes.giveitem <playerNameOrSteamId> 25`.
- Avoid any live config value above `65535` for this item unless a smaller/larger cap is deliberately live-tested.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- JSON parse check passed for `oxide/config/StackSizeController.json`; both airstrike stack entries report `65535`.
- Direct trailing-whitespace scan passed for changed plugin/config/docs/upload files.

Known live-verification boundary:
- Local compile verifies code shape only. The live server should confirm that moving/splitting the newly capped stack no longer trips the Rust `MoveItem` assertion.
