Raidlands vehicle tokens CID and drop spawn - 2026-07-08

What changed:
- `RaidlandsVehicleTokens` now registers one CID-backed token per configured vehicle.
- New token shortnames:
  - `raidlands.vehicle.token.minicopter`
  - `raidlands.vehicle.token.scrap_transport_helicopter`
  - `raidlands.vehicle.token.attack_helicopter`
  - `raidlands.vehicle.token.rhib`
  - `raidlands.vehicle.token.tugboat`
  - `raidlands.vehicle.token.solo_submarine`
  - `raidlands.vehicle.token.duo_submarine`
  - `raidlands.vehicle.token.snowmobile`
  - `raidlands.vehicle.token.hot_air_balloon`
- Fixed custom item IDs are `-395118501` through `-395118509` in the order above.
- CID parent item is now `scrap`, not `wrappedgift`, so new custom tokens do not inherit the wrapped-gift `Unwrap` action client-side.
- CID vehicle tokens still do not import parent item mods; CID supplies the token name, description, max stack size, and vehicle icon.
- CID definitions are refreshed on token-plugin load so stale definitions with old wrapped-gift descriptions/actions are replaced.
- Legacy named `wrappedgift` token matching remains enabled so old tokens can still be redeemed, but new token creation no longer falls back to legacy wrapped gifts when CID is missing.
- Dropping a token now attempts to spawn the vehicle at the dropped token position after `0.1` seconds.
- Dropped-token redemption now retries owner resolution with the nearest active player if the dropped item entity does not carry an owner ID.
- Successful dropped-token redemption consumes exactly one item from the dropped stack.
- Failed dropped-token redemption now returns one token to the player's inventory, or drops it at their feet if inventory is full, then removes the failed world-copy token.
- `VehicleLicence` now exposes `SpawnLicensedVehicleAt(...)` for token drop spawns while reusing license, cooldown, cost, limit, zone, safe-zone, mounted, nearby-player, water-vehicle, and `CanLicensedVehicleSpawn` checks.
- Website Admin > Kits now merges vanilla `rust-items.json` with the Raidlands custom item overlay so these tokens can be manually added to kits.
- No SQL migration was added.
- No existing `game_kit_items` rows or kit item counts were changed.

Website files to upload:
- `C:\wamp64\www\raidlands\includes\kits.php`
- `C:\wamp64\www\raidlands\admin\index.php`
- `C:\wamp64\www\raidlands\assets\js\admin-kits.js`
- `C:\wamp64\www\raidlands\assets\data\raidlands-custom-items.json`

Website icon assets to upload:
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.minicopter.webp`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.scrap_transport_helicopter.webp`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.attack_helicopter.webp`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.rhib.webp`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.tugboat.webp`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.solo_submarine.webp`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.duo_submarine.webp`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.snowmobile.webp`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.hot_air_balloon.webp`

Oxide plugin/config files to upload:
- `oxide/plugins/RaidlandsVehicleTokens.cs`
- `oxide/plugins/VehicleLicence.cs`
- `oxide/config/RaidlandsVehicleTokens.json`
- `oxide/plugins/RaidlandsVehicleTokens.json`
- `oxide/plugins/CustomItemDefinitions.cs` only if the live server does not already have this CID dependency.

Oxide data icon assets to upload:
- `oxide/data/RaidlandsVehicleTokens/minicopter.png`
- `oxide/data/RaidlandsVehicleTokens/scrap_transport_helicopter.png`
- `oxide/data/RaidlandsVehicleTokens/attack_helicopter.png`
- `oxide/data/RaidlandsVehicleTokens/rhib.png`
- `oxide/data/RaidlandsVehicleTokens/tugboat.png`
- `oxide/data/RaidlandsVehicleTokens/solo_submarine.png`
- `oxide/data/RaidlandsVehicleTokens/duo_submarine.png`
- `oxide/data/RaidlandsVehicleTokens/snowmobile.png`
- `oxide/data/RaidlandsVehicleTokens/hot_air_balloon.png`

Icon quality note:
- `tugboat`, `solo_submarine`, `duo_submarine`, `snowmobile`, and `hot_air_balloon` were regenerated from larger 300px-class vehicle art.
- `minicopter`, `scrap_transport_helicopter`, `attack_helicopter`, and `rhib` were replaced again with sharper 512px transparent vehicle renders from the Scalbox Rust vehicle bundle image set, then normalized to 180px PNG/WebP assets.
- Upload all nine regenerated files; the four previously soft icons are no longer thumbnail-backed.

Do not upload as part of this feature:
- `oxide/data/RaidlandsVehicleTokens/temp_licenses.json`
- Any SQL migration.
- Any manual edits to `game_kit_items`.

Reload after upload:
- `oxide.reload CustomItemDefinitions`
- `oxide.reload RaidlandsVehicleTokens`

Reload `VehicleLicence` too only if the live server has not already received the earlier `SpawnLicensedVehicleAt(...)` support from this same rollout.

Important reload note:
- Reload `CustomItemDefinitions` before `RaidlandsVehicleTokens`. Old custom definitions can keep stale wrapped-gift descriptions/actions until CID is reloaded and the token plugin refreshes the definitions.

Runtime smoke:
- `raidlands.vehicle.give <playerNameOrSteamId> minicopter 5`
- Confirm the token context menu does not show `Unwrap`.
- Drop the stack.
- Expected: one minicopter spawns at the dropped token position and the world stack becomes `4`.
- `raidlands.vehicle.give <playerNameOrSteamId> rhib 1`
- Drop the RHIB token on land.
- Expected: spawn fails from the water-vehicle check and one RHIB token is returned to the player's inventory, or dropped at their feet if inventory is full.
- Add one `raidlands.vehicle.token.minicopter` row manually in Admin > Kits, publish, redeem the kit, and confirm the kit hook creates the CID token.

Local verification:
- Roslyn compile passed for `oxide/plugins/RaidlandsVehicleTokens.cs` after the no-unwrap/refund follow-up.
- Roslyn compile passed for `oxide/plugins/VehicleLicence.cs`.
- Roslyn compile passed for `oxide/plugins/CustomItemDefinitions.cs`.
- JSON validation passed for both `RaidlandsVehicleTokens.json` files after the no-unwrap/refund follow-up, and previously passed for `assets/data/raidlands-custom-items.json`.
- Duplicate check passed: 9 token definitions, 9 unique custom shortnames, 9 unique custom item IDs.
- PHP lint passed for `includes/kits.php` and `admin/index.php`.
- Node syntax check passed for `assets/js/admin-kits.js`.
- PNG/WebP image decode check passed for all generated vehicle token icons.

Local source hashes:
- `oxide/plugins/RaidlandsVehicleTokens.cs`
  - SHA256: `A5104ED8A83E7F32517461B52C8B9DE204CA892821788CF3B7B6CA9331534B1E`
- `oxide/plugins/VehicleLicence.cs`
  - SHA256: `13671A9F684A7614ECEC74DD43D083119E40DA51656B3D30AD03F49EC4BA7074`
- `oxide/plugins/CustomItemDefinitions.cs`
  - SHA256: `13B8276700D398DBEE658C1F292545E471DF76D487561486A7DCC94F3E45A469`
- `oxide/config/RaidlandsVehicleTokens.json`
  - SHA256: `290FF47383DF33DBD518FACBD306F0A992B7A799D3D82F1F46296981ED0A5066`
- `oxide/plugins/RaidlandsVehicleTokens.json`
  - SHA256: `290FF47383DF33DBD518FACBD306F0A992B7A799D3D82F1F46296981ED0A5066`
- `C:\wamp64\www\raidlands\assets\data\raidlands-custom-items.json`
  - SHA256: `74BFD5B1283B687EE1A7B9FABA343038780D39B432B4D804ACF68908955D0344`

Regenerated token icon hashes:
- `oxide/data/RaidlandsVehicleTokens/attack_helicopter.png`
  - SHA256: `B935B61C05789F636459281874916EDEAB18E24AC6D25DF1BB290DC8F290CE9D`
- `oxide/data/RaidlandsVehicleTokens/duo_submarine.png`
  - SHA256: `BBD46ACC3B76DDB74409DD4657ABBBC8B047B13A28E758238F148D257D19EC42`
- `oxide/data/RaidlandsVehicleTokens/hot_air_balloon.png`
  - SHA256: `A9B8F0E217E0DA80F47458B319D4E60CBFC1EF8DB226DD1693DE8722FAC49FAB`
- `oxide/data/RaidlandsVehicleTokens/minicopter.png`
  - SHA256: `316C28FB20B34C69D95528595132D124FE8BDB09F12A4464BC5C21E4161722A2`
- `oxide/data/RaidlandsVehicleTokens/rhib.png`
  - SHA256: `46989165FF24FE76F6DCCBA8D0E96C0AA12F005803D37374A22D933108A8BA32`
- `oxide/data/RaidlandsVehicleTokens/scrap_transport_helicopter.png`
  - SHA256: `6875F44D247B401444BA471DE068CA698FAEA5014DA5A8092F359E3194CA2013`
- `oxide/data/RaidlandsVehicleTokens/snowmobile.png`
  - SHA256: `FF8A4EFB13B5380C80FD709A4BB61342F6945EBFA38DF6E53074E452155EC9E0`
- `oxide/data/RaidlandsVehicleTokens/solo_submarine.png`
  - SHA256: `2EBBD0B63FFA87431F1A0B6CA479ECA91930C1A91910E484B3F25D1C943E9DCA`
- `oxide/data/RaidlandsVehicleTokens/tugboat.png`
  - SHA256: `71DB34AB1A2E47EC01FA42070B2E7F7A7E63998917ED75B5BE5F9687F0146173`

Updated website token WebP hashes:
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.attack_helicopter.webp`
  - SHA256: `53DF7CDAE482313D64EADE3FB34E055B8924000F36D7B5676BF786CB52BC2B10`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.minicopter.webp`
  - SHA256: `9D3C853E3FE720E349933E711ACE37F4BB85B50E695A2248CC1BFF68AEE46066`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.rhib.webp`
  - SHA256: `82467A56A3FCE71947EE7C3054F95D206CFC69F3A88F457723521131FBBC08C6`
- `C:\wamp64\www\raidlands\assets\media\rust-items\raidlands.vehicle.token.scrap_transport_helicopter.webp`
  - SHA256: `66E02D619116F3D6ECAEF4EB4F0F10045223AD2B8D6D80FD7F4EA2105E16F635`

Known live-verification boundary:
- Local compile validates code shape only. The live Rust server still needs reload confirmation that CID registers the nine token item definitions and that clients receive the icon file IDs.
