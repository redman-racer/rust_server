Portable Airstrikes CID custom item integration - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.22.
- Added `ConfigVersion=22`.
- Patched `CustomItemDefinitions.cs` so it compiles on the current Rust/Oxide build by reflecting `Translate.allServerTranslations` only when that field exists.
- Added CID-backed airstrike item registration for `raidlands.airstrike.designator`.
- The custom item is parented to `tool.binoculars` and imports the parent item mods so the held binocular behavior and ping flow are preserved.
- The player-facing item name remains `Airstrike Targeting Binoculars`.
- Added runtime icon loading from `oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png`; PortableAirstrikes stores this PNG in Rust FileStorage and passes the resulting `iconFileId` to CID.
- Added CID config fields under `AirstrikeItem`: `UseCustomItemDefinition`, `AllowVanillaFallbackIfCIDMissing`, `CustomShortname`, `CustomItemId`, `ParentShortname`, `DefaultDescription`, `IconFileId`, `IconPngDataPath`, and `ImportParentItemMods`.
- Refactored airstrike item creation and matching so new gives/loot/kits create the CID item while legacy named binoculars still work as fallback.
- Added `/strike debug item` for admin verification of CID load/registration/icon/fallback state.
- Existing binocular ping/default selection, RP charge/refund, item consume/restore, cooldowns, strike executors, warnings, webhooks, and visual delivery were left unchanged.

Rust server plugin files to upload:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/plugins/PortableAirstrikes.cs

Runtime data asset to upload:
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- Docs/AirStrikes/portable_airstrikes_cid_integration_with_custom_items_plan.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_CID_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Optional source/reference icon:
- Docs/AirStrikes/assets/airstrike-targeting-binoculars.png

Local source hashes:
- oxide/plugins/PortableAirstrikes.cs
  - SHA256: E9D319CA87A50D34B42C954F9B09EFA9D4B8455517D23DAED6030D5378C652F8
- oxide/plugins/CustomItemDefinitions.cs
  - SHA256: 13B8276700D398DBEE658C1F292545E471DF76D487561486A7DCC94F3E45A469
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png
  - SHA256: EADB957A4E1E2F0C69D1E5B8639A54B9A86BE88E68E4E6DD2B351B5CF8A8248D
- Docs/AirStrikes/assets/airstrike-targeting-binoculars.png
  - SHA256: EADB957A4E1E2F0C69D1E5B8639A54B9A86BE88E68E4E6DD2B351B5CF8A8248D

Reload after upload:
- oxide.reload CustomItemDefinitions
- oxide.reload PortableAirstrikes

Minimum setup:
- Grant basic use: `oxide.grant group default portableairstrikes.use`
- Grant strike family permissions as needed, for example `oxide.grant group default portableairstrikes.use.grenade`
- Give test item from server console: `portableairstrikes.giveitem <playerNameOrSteamId> 1`

CID item smoke:
- Upload both plugin files and the PNG data asset.
- Run `oxide.reload CustomItemDefinitions`.
- Run `oxide.reload PortableAirstrikes`.
- Confirm the banner reports v0.1.22.
- Run `/strike debug item`.
- Expected: CID enabled=True, CID loaded=True, registered=True, definition includes `raidlands.airstrike.designator`, parent is `tool.binoculars`, icon source points at the Oxide data PNG, and vanillaFallback=True.
- Give yourself one item with `portableairstrikes.giveitem <playerNameOrSteamId> 1`.
- Expected item identity: `raidlands.airstrike.designator`.
- Expected item name/icon: `Airstrike Targeting Binoculars` with the Raidlands binocular icon.
- Equip the item and confirm the usage prompt appears.
- Place a ping while holding the item.
- Expected with no saved default: a cyan/amber target marker appears and `/strike` opens.
- Confirm a valid strike from the menu, such as `beancan_drop`.
- Expected: the selected strike is saved as your airstrike binocular default and the strike starts through the normal RP/item/cooldown path.
- Place a second ping while holding the item.
- Expected with a saved default: the default strike is attempted immediately.

Fallback smoke:
- Temporarily unload CID only if you are ready to test fallback behavior.
- Run `oxide.unload CustomItemDefinitions`.
- Run `oxide.reload PortableAirstrikes`.
- Expected: PortableAirstrikes warns that CID is not loaded and uses the legacy named `tool.binoculars` fallback while `AllowVanillaFallbackIfCIDMissing=true`.
- Give a test item and confirm the v0.1.21 named-binocular flow still works.
- Reload CID and PortableAirstrikes again before normal play.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- Roslyn compile check passed for patched `oxide/plugins/CustomItemDefinitions.cs` against the same managed references.
- Direct trailing-whitespace scan passed for changed `.cs` and `.md` files.
- Runtime PNG hash matches the source/reference PNG.

Known live-verification boundary:
- Local compile verifies code shape only. The live Rust server still needs reload confirmation that CID registers the item, the icon appears for clients, and the binocular ping hook fires while the CID item is active.
