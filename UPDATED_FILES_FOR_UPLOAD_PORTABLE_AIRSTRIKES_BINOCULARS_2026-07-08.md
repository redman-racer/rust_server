Portable Airstrikes targeting binoculars and default strike flow - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.21.
- Added `ConfigVersion=21`.
- Changed the default configured item to `tool.binoculars` named `Airstrike Targeting Binoculars`.
- Added a conservative migration so only the old stock `targeting.computer` / `Airstrike Authorization Key` default changes to binoculars; deliberate custom item config is preserved.
- Added persisted player defaults in `PortableAirstrikes_Data.DefaultStrikeByUser`.
- Added `/strike default show`, `/strike default <strikeId>`, and `/strike default clear`.
- Equipping the configured item gives players a short usage prompt.
- Pinging while holding the configured item now creates a short-lived cyan/amber airstrike target marker.
- If the player has no saved default, that tool ping opens `/strike`; the confirmed selection is saved as their default.
- If the player has a saved default, that tool ping attempts the default strike immediately.
- Added `OnRaidlandsCreateKitItem(...)` support so Kits can create the configured airstrike item through PortableAirstrikes' own item creator.
- Generated a project reference icon at `Docs/AirStrikes/assets/airstrike-targeting-binoculars.png`.
- Local Rust item data says `tool.binoculars` has `HasSkins=false`, so the generated PNG cannot be used as the live inventory icon via a normal Rust skin ID. Use it for website/store/docs or another image-capable UI surface.
- Existing RP charge/refund, item consume/restore, cooldowns, payload executors, warnings, webhooks, visual delivery, and default-disabled homing/MLRS gates were left unchanged.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Optional website/docs asset:
- Docs/AirStrikes/assets/airstrike-targeting-binoculars.png

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- Docs/AirStrikes/portable_airstrikes_options_tables_v2.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_BINOCULARS_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Local source hashes:
- oxide/plugins/PortableAirstrikes.cs
  - SHA256: B225313D041508073FE833896C6F8A255650AB9831EE6EB8F486BE8DD6662E91
- Docs/AirStrikes/assets/airstrike-targeting-binoculars.png
  - SHA256: EADB957A4E1E2F0C69D1E5B8639A54B9A86BE88E68E4E6DD2B351B5CF8A8248D

Reload after upload:
- oxide.reload PortableAirstrikes

Minimum setup:
- Grant basic use: `oxide.grant group default portableairstrikes.use`
- Give test items from server console: `portableairstrikes.giveitem <playerNameOrSteamId> 2`
- Grant strike family permissions as needed, for example `oxide.grant group default portableairstrikes.use.grenade`

Binocular/default smoke:
- Upload `oxide/plugins/PortableAirstrikes.cs`.
- Run `oxide.reload PortableAirstrikes`.
- Confirm the banner reports v0.1.21 and the config saves `ConfigVersion=21`.
- Give yourself `Airstrike Targeting Binoculars`.
- Equip the item and confirm the usage prompt appears.
- Place a ping while holding the item.
- Expected with no saved default: a cyan/amber target marker appears and `/strike` opens.
- Confirm a valid strike from the menu, such as `beancan_drop`.
- Expected: the selected strike is saved as your airstrike binocular default and the strike starts through the normal RP/item/cooldown path.
- Place a second ping while holding the item.
- Expected with a saved default: the default strike is attempted immediately.
- Run `/strike default show`, `/strike default beancan_drop`, and `/strike default clear` to confirm manual default controls.

Regression smoke:
- /strike debugping
- /strike
- /strike beancan_drop
- /strike debug history 5
- /strike debug stats

Known live-verification boundary:
- The code path is compiled, but live Rust/uMod still needs confirmation that the actual player binocular/team ping action fires `OnMapMarkerAdded` on this server build while the item is active.

Local verification:
- Confirmed `Bundles/items/tool.binoculars.json` exists and is holdable/usable.
- Confirmed `tool.binoculars` reports `HasSkins=false`.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Direct trailing-whitespace scan passed for the plugin, Airstrikes docs, and this upload note.
