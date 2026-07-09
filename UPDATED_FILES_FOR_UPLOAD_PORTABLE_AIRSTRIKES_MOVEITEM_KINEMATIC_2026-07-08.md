Portable Airstrikes MoveItem crash and kinematic velocity repair - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.36.
- Added `ConfigVersion=31`.
- Reverted the airstrike CID binoculars away from physical Rust stacking.
- `AirstrikeItem.MaxStackSize` is back to `1`; `MaxChargesPerItem` remains `65535`.
- `StackSizeController.json` now pins both `raidlands.airstrike.designator` and vanilla `tool.binoculars` to stack size `1`.
- Airstrike item charges are stored in `item.instanceData.dataInt`; the physical item amount is forced to `1`.
- Existing bad physical stacks such as `x25` normalize into one movable item named like `Airstrike Targeting Binoculars x25`.
- Give/API/kit/loot paths now create one physical item per up-to-65535 charges instead of a movable Rust stack.
- Successful strikes consume one stored charge and update the display name/count; the item is removed when charges reach zero.
- Visual flyover rigidbody handling no longer writes velocity/angular velocity after a rigidbody has been made kinematic.
- SpawnHeli's fetch/teleport path now guards against clearing velocity on a kinematic or missing rigidbody.

Why:
- Live testing showed that moving stacked binocular-derived CID items can still trigger `RPC Error in MoveItem` plus `AssertionException: split_Amount <= 0`.
- The safer contract is the earlier v0.1.25 model: one physical held item, stored charge count, visible `xN` name.
- The repeated `Setting linear/angular velocity of a kinematic body is not supported` console spam matched the visual flyover code path that made rigidbodies kinematic, then wrote velocity.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/config/PortableAirstrikes.json
- oxide/config/StackSizeController.json
- oxide/plugins/SpawnHeli.cs

Runtime dependencies to keep uploaded:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/plugins/StackSizeController.cs
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Reload after upload:
- oxide.reload CustomItemDefinitions
- oxide.reload StackSizeController
- oxide.reload SpawnHeli
- oxide.reload PortableAirstrikes

Immediate live repair smoke:
- Confirm the PortableAirstrikes reload banner reports v0.1.36.
- `/strike debug item`
- Expected: `actualStack=1`, `maxCharges=65535`, and the PNG-backed icon source.
- `portableairstrikes.giveitem <playerNameOrSteamId> 25`
- Expected: one physical item named `Airstrike Targeting Binoculars x25`, not a Rust inventory stack count.
- Move the item between inventory slots and containers.
- Expected: no disconnect, no `RPC Error in MoveItem`, and no `split_Amount <= 0`.
- Use one valid strike.
- Expected: item remains one physical item and display name decrements to `Airstrike Targeting Binoculars x24`.
- Run one attack-heli/rocket-run visual strike.
- Expected: no repeated `Setting linear velocity of a kinematic body is not supported` or `Setting angular velocity of a kinematic body is not supported` spam from the flyover visual.

Local verification:
- JSON validation passed for `oxide/config/PortableAirstrikes.json` and `oxide/config/StackSizeController.json`.
- Roslyn compile passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.36.
- Roslyn compile sanity check passed for `oxide/plugins/SpawnHeli.cs`.
- Remaining warnings were expected Unity obsolete API and Oxide/config field warnings.

Local source/config hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: 2A32C475774F5445655F4B7A61A084BFFE0FC7E7851F9C6FB65ACEE26CBD96C5
- oxide/config/PortableAirstrikes.json SHA256: 5B1396CBE1F627D0C5A765E247E4900E127A409C8A1A79968EADC1D805F1B5BC
- oxide/config/StackSizeController.json SHA256: 2CDC563B43F5FEF55B102A3BD98C5163F2E70FAD14F3299E8D9587EB94506796
- oxide/plugins/SpawnHeli.cs SHA256: 31F9557E2C5B8F739B75047AD67B811AADD9D62CBD5B3D3A95E72A98C8E4728B
