Portable Airstrikes admin layout cleanup - 2026-07-10

What changed:
- PortableAirstrikes is now v0.1.44.
- This is a code-only CUI layout fix; ConfigVersion stays 35.
- Moved the left sidebar tab buttons below the header so the active Dashboard tab no longer overlaps the header summary.
- Moved Dashboard metric cards down so the first row no longer overlaps the Dashboard title/subtitle.
- Fixed the Strikes detail panel RP/Tier row so it stays inside the right-side detail panel instead of drawing over the strike list.
- Moved Safety toggles and the currency-provider selector into separate vertical bands.
- Moved Visuals toggles above the visual timing fields so `Require Carrier` and `Refund Carrier` no longer overlap `Drone Dist` and other inputs.
- Moved Loot/Audit toggles above the loot-rule section so `Loot container rules` no longer collides with the second toggle row.
- Existing admin actions, config persistence, profile assignment, item grants, and strike execution behavior were left unchanged.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs

Only upload these from the prior v0.1.43 admin-panel note if that package was not already deployed:
- oxide/plugins/PortableAirstrikesAnimationEditor.cs
- oxide/config/PortableAirstrikes.json

Runtime dependencies to keep from earlier passes:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/plugins/StackSizeController.cs
- oxide/config/StackSizeController.json
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png
- oxide/data/PortableAirstrikes/VisualProfiles.json only if local animation profile data should replace live profile data.

Reload after upload:
```text
oxide.reload PortableAirstrikes
```

Quick live smoke:
- Confirm the reload banner reports PortableAirstrikes v0.1.44.
- Open `/strike admin` as an admin.
- Check Dashboard: no header/tab/title/card overlap.
- Check Give Items: search bar, amount field, increment buttons, Give Search, player rows, and Give buttons do not overlap.
- Check Strikes: list rows do not have selected-strike fields painted over them; right-side selected strike fields stay in the detail panel.
- Check Balance: all number fields and damage-scale fields remain separated.
- Check Safety: toggles no longer overlap the currency provider selector.
- Check Visuals: toggle rows no longer overlap Drone/Aircraft/Heli/A10/MLRS timing fields, and the animation profile card remains clear.
- Check Loot/Audit: toggles no longer overlap the loot-rule title or webhook fields.
- Check Activity: recent call rows and stats still fit.

Local verification:
- Roslyn compile check passed for oxide/plugins/PortableAirstrikes.cs v0.1.44 against RustDedicated_Data/Managed.
- oxide/config/PortableAirstrikes.json parsed successfully.
- Targeted `git diff --check` passed for the Portable Airstrikes admin layout files.
- Remaining warnings were existing Unity Rigidbody.velocity deprecation warnings.

Local source hash:
- oxide/plugins/PortableAirstrikes.cs SHA256: 3DDDBC4DDED50660E2D76A202F04B0910F4F2D29D1ADBD68D3C9E869233D65B1
