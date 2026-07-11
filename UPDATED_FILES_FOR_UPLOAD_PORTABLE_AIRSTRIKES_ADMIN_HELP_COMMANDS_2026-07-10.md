# Portable Airstrikes admin help and commands - 2026-07-10

What changed:
- PortableAirstrikes is now v0.1.52.
- `/strike admin` Give Items moved its search/amount controls below the page description so the search field no longer overlaps the subtitle.
- The Strikes Add Strike button moved below the title/subtitle band, with the strike list shortened slightly to keep the button clear.
- Balance profile labels now sit above the limit/delay controls with enough vertical separation, so long profile names no longer get clipped by the Limit box.
- The admin sidebar now includes Commands and Help tabs.
- Page-level `?` buttons and field/toggle `?` buttons now show contextual explanations in the admin status line.
- The Commands tab has scope tabs for `/strike` chat commands and console commands, then category tabs inside each scope.
- The Help tab outlines the item grant, target capture, default selection, validation, execution, profile, safety, loot/audit, and edit workflows.

Rust server file to upload:
- oxide/plugins/PortableAirstrikes.cs

Carry-forward dependency:
- If the v0.1.51 admin strike-profile wrapper pass is not already live, also deploy its `oxide/config/PortableAirstrikes.json` ConfigVersion 36 update before relying on the wrapper/profile admin views.

Reload after upload:
```text
oxide.reload PortableAirstrikes
```

Quick live smoke:
- Confirm the reload banner reports PortableAirstrikes v0.1.52.
- Open `/strike admin` and verify the sidebar shows Dashboard, Give Items, Strikes, Balance, Safety, Strike Profiles, Loot/Audit, Activity, Commands, and Help.
- In Give Items, verify the search bar sits below the subtitle and the filter/sort rows still work.
- In Strikes, verify Add Strike no longer overlaps the subtitle and selecting/toggling strikes still refreshes the detail panel.
- In Balance, select a strike with included profiles and verify profile labels are readable above Limit/Delay.
- Click several page and field `?` buttons and verify the explanation appears in the footer status line.
- Open Commands, switch between `/strike` Commands and Console Commands, and verify category tabs filter the list.
- Open Help and verify the scrollable workflow reference is readable.

Local verification:
- Roslyn compile check passed for oxide/plugins/PortableAirstrikes.cs v0.1.52 against RustDedicated_Data/Managed.
- Remaining warnings were the existing Unity Rigidbody.velocity deprecation warnings in visual movement/carrier velocity helpers.
- Live Oxide reload and in-game admin panel smoke testing are still required.

Local source hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: F7BA7AA35C1A7FD7DD26D630F87B991F41FEE15A280E114437B0CAA021F33DBB
- outputs/compile/PortableAirstrikes.dll SHA256: 25D2601673154F786ACEA320431412DAE43D4BD9594CA7BCD11A7963BDE0C1B7
