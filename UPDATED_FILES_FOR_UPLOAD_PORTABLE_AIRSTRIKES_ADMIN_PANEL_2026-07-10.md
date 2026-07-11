Portable Airstrikes admin panel - 2026-07-10

What changed:
- PortableAirstrikes is now v0.1.43.
- PortableAirstrikes config is now ConfigVersion 35.
- Added `/strike admin`, gated by `portableairstrikes.admin`, with Dashboard, Give Items, Strikes, Balance, Safety, Visuals, Loot/Audit, and Activity tabs.
- Admins can search online players and grant charged `Airstrike Targeting Binoculars` through the same inventory-safe give path used by `/strike giveitem`.
- Admins can edit strike availability, RP cost, warning delay, cooldowns, tier, target type, delivery, payload, count/spread/rocket/missile/A-10 fields, homing fields, and damage scales.
- Admins can edit global safety, currency, item-consumption/admin-bypass, visual delivery, loot injection, and audit webhook settings.
- Strike definitions now support optional `VisualProfileId` assignments.
- Delivery/payload compatibility is enforced in config normalization, the admin UI, and runtime validation; bad hand-edited combos are shown as unsupported and cannot start.
- If PortableAirstrikesAnimationEditor is loaded, compatible animation profiles can be assigned or opened from the admin panel. If it is unloaded, profile controls disable while the rest of the panel remains usable.
- PortableAirstrikesAnimationEditor now exposes small public API hooks for listing profiles, opening profiles, creating/opening starter profiles, saving profiles, and reloading profile data.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/plugins/PortableAirstrikesAnimationEditor.cs
- oxide/config/PortableAirstrikes.json

Runtime dependencies to keep from earlier passes:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/plugins/StackSizeController.cs
- oxide/config/StackSizeController.json
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png
- oxide/data/PortableAirstrikes/VisualProfiles.json only if the local animation profile data should replace live profile data.

Reload after upload:
```text
oxide.reload PortableAirstrikesAnimationEditor
oxide.reload PortableAirstrikes
```

Quick live smoke:
- Confirm the reload banner reports PortableAirstrikes v0.1.43.
- Confirm a non-admin cannot open `/strike admin`.
- Confirm an admin can open `/strike admin`.
- In Give Items, search an online player and grant 1 or more `Airstrike Targeting Binoculars`.
- Confirm the grant creates the same charged single-item model as `/strike giveitem` and handles a full inventory by dropping the item near the player.
- In Strikes, toggle a strike, edit a cost, and reload `oxide.reload PortableAirstrikes`; confirm the values persist.
- Confirm unsupported delivery/payload combinations are not offered by the panel and cannot start if hand-edited into config.
- With PortableAirstrikesAnimationEditor loaded, assign a compatible profile to a non-mortar strike and open/create it through `/airanim`.
- With PortableAirstrikesAnimationEditor unloaded, confirm the profile controls are disabled but the rest of `/strike admin` still works.
- Confirm existing `/strike`, `/strike giveitem`, `/strike debug`, default-strike, `/strike status`, `/strike cancel`, and active call behavior still work.

Local verification:
- Roslyn compile check passed for oxide/plugins/PortableAirstrikes.cs v0.1.43 against RustDedicated_Data/Managed.
- Roslyn compile check passed for oxide/plugins/PortableAirstrikesAnimationEditor.cs v0.1.23 with the API hooks against RustDedicated_Data/Managed.
- Remaining warnings were the existing Unity Rigidbody.velocity deprecation warnings in visual movement helpers and preview movement helpers.
- oxide/config/PortableAirstrikes.json parsed successfully.
- Targeted `git diff --check` passed for the Portable Airstrikes admin panel files.

Local source hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: 257236583C517D2F1DCBBC5C6BFD42F942B0EADE65A8733BD3872125B7507C9E
- oxide/plugins/PortableAirstrikesAnimationEditor.cs SHA256: 1E71FAA63507129F4FA06ABDB1586CA9C12DAC73E7E38C8E6BF6F86FA7D30348
- oxide/config/PortableAirstrikes.json SHA256: BEC0BBA0F400D8A6A9FF589DC8BF7BC24740201FE35388F1F04D81673BC60F80
