Portable Airstrikes stackable CID binoculars - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.30.
- Added `ConfigVersion=27`.
- `AirstrikeItem.MaxStackSize` now defaults/clamps to `65535`.
- Airstrike binocular counts are physical Rust stack amounts again, so inventory shows the real stack count overlay.
- Old charge-backed items migrate forward: stored `instanceData.dataInt` charges are converted into the physical item stack amount during inventory normalization.
- The custom CID definition still uses `raidlands.airstrike.designator`, parent `tool.binoculars`, the current PNG icon, and imported binocular item mods.
- StackSizeController config now sets only `raidlands.airstrike.designator` to `65535`; vanilla `tool.binoculars` stays at `1`.
- StackSizeController now handles `OnItemDefinitionRegistered`, so late CID item registrations get their configured stack size immediately.
- Existing targeting, icon, warnings, RP/cooldowns, webhooks, and strike payload behavior were left unchanged.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/plugins/StackSizeController.cs
- oxide/config/PortableAirstrikes.json
- oxide/config/StackSizeController.json

Local source/config hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: 189D327CCC6D3485D6BA3E2AEAD7AFCF08FF8F80FC86FEA0E7C322CE00D8D632
- oxide/plugins/StackSizeController.cs SHA256: F27E8EED723AB544CA51243FAF4F1ECDA798B6683CAA4AC99F2AC7A5F393E8A6
- oxide/config/PortableAirstrikes.json SHA256: 38E3B24BD689DE81D0ED4555F399EED3ECB9388B6E9A46F59DFB79442A0DB55C
- oxide/config/StackSizeController.json SHA256: 2FBD643B517F97DFADB350061EBA910F50F25C5FF4FBA86C297DEA21199B6743

Runtime dependencies to keep uploaded:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Reload after upload:
- oxide.reload CustomItemDefinitions
- oxide.reload StackSizeController
- oxide.reload PortableAirstrikes

Quick live smoke:
- Confirm the PortableAirstrikes reload banner reports v0.1.30.
- Run `/strike debug item`.
- Expected: `actualStack=65535`, `maxCharges=65535`, `createShortname=raidlands.airstrike.designator`, and the PNG-backed `icon=` source.
- Run `portableairstrikes.giveitem <playerNameOrSteamId> 25`.
- Expected: one inventory slot with the custom airstrike binocular icon and Rust stack count `x25`.
- Move the stack between inventory slots and containers.
- Expected: no disconnect, no stuck held binocular viewmodel, and the item stays stacked.
- Use one strike from the stack.
- Expected: the physical stack count decrements by one.

If stacking does not apply:
- Run `stacksizecontroller.itemsearch raidlands.airstrike`.
- Confirm `raidlands.airstrike.designator` appears.
- Run `stacksizecontroller.setstack raidlands.airstrike.designator 65535`, then `oxide.reload PortableAirstrikes`.

Local verification:
- `oxide/config/PortableAirstrikes.json` parses successfully with `ConfigVersion=27`, `MaxStackSize=65535`, and `MaxChargesPerItem=65535`.
- `oxide/config/StackSizeController.json` parses successfully with `raidlands.airstrike.designator=65535` and `tool.binoculars=1`.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.30.
- Roslyn compile check passed for `oxide/plugins/StackSizeController.cs`; remaining warnings are expected unassigned plugin-reference/config-field warnings.

Known live-verification boundary:
- Local compile verifies code shape only. The live server should confirm Rust client movement/splitting behavior for a stacked binocular-derived CID item.
