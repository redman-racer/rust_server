# Live Backup Reconciliation and Loot Despawn Update — 2026-07-14

## Summary

The local Rust server project was reconciled against the latest MintServers live-server backup (`a4e1ee63-aef5-4201-b2b3-a1633faaf1f5.tar.gz`). Shared plugin, non-secret configuration, and server-CFG files were refreshed from that backup. Local-only development files were retained.

The backup comparison and synchronization covered:

- `oxide/plugins`
- `oxide/config`
- `server/rust/cfg`

`oxide/config/Secrets.local.json` was intentionally excluded because it contains deployment secrets and remains covered by `.gitignore`.

## Live-only plugins imported into the project

- `oxide/plugins/FriendlyFire.cs`
- `oxide/plugins/HostileTime.cs`
- `oxide/plugins/NPCVenderModifier.cs`
- `oxide/plugins/RaidlandsBugReports.cs`
- `oxide/plugins/RaidlandsConfigGuard.cs`
- `oxide/plugins/SAMSiteRange.cs`

Associated live configuration files were also imported where present:

- `oxide/config/HostileTime.json`
- `oxide/config/NPCVenderModifier.json`
- `oxide/config/RaidlandsBugReports.json`
- `oxide/config/RaidlandsConfigGuard.json`
- `oxide/config/SAMSiteRange.json`

## Shared live files refreshed

The backup contained real content differences in 21 shared plugin files and 28 shared configuration files. The local copies were replaced with the backup versions so the repository represents the latest captured live-server state.

Notable refreshed plugins include:

- `Guardian.cs`
- `LiveAdmin.cs`
- `NTeleportation.cs`
- `Kits.cs`
- `RaidlandsRoamBots.cs`
- `RaidlandsSentryTurrets.cs`
- `PortableAirstrikes.cs`
- `ServerInfo.cs`
- `ServerRewards.cs`
- `SmartChatBot.cs`
- `WebsiteVipBridge.cs`
- `WebsiteMapBridge.cs`
- `WebsiteAirstrikeAnimationBridge.cs`

Notable refreshed configurations include:

- `Guardian.json`
- `QuickSmelt.json`
- `TimeOfDay.json`
- `ToolCupboardTurrets.json`
- `TurretSwitches.json`
- `CompoundOptions.json`
- `Kits.json`
- `RaidlandsRoamBots.json`
- `ServerInfo.json`
- `WebsiteVipBridge.json`

## Local-only files retained

These files were not present in the live backup and were deliberately retained:

- `oxide/plugins/DroppedItemDespawn.cs`
- `oxide/config/DroppedItemDespawn.json`
- `oxide/plugins/Friends.cs`
- `oxide/plugins/RaidlandsPermissionReset.cs`
- `oxide/config/Secrets.local.example.json`

## Turret switch state synchronization

`TurretSwitches` v1.9 keeps the physical switch display aligned with the actual turret or SAM state.

- Synchronizes newly created switches immediately.
- Synchronizes existing switches recovered during plugin reload.
- Polls attached switches once per second to catch state changes made by wiring, commands, `PowerlessTurrets`, or other plugins.
- Updates the switch `On` flag only when its displayed state differs from the associated turret/SAM state.

Reload command:

```text
oxide.reload TurretSwitches
```

## ToolCupboardTurrets SAM scan performance

`ToolCupboardTurrets` v1.3.9 fixes expensive SAM scan and turret targeting paths. Live profiling reported average hook times above one second.

- Replaced the global `BaseNetworkable.serverEntities` traversal with a 150-metre `Vis.Entities` spatial query for minicopters.
- Reuses one minicopter list instead of allocating a new collection for every scan.
- Resolves the SAM building privilege once per vehicle occupant check instead of once for every occupied seat.
- Preserves occupied-minicopter targeting and TC/shared-authorization protection.
- Removes a redundant `player.IsVisible` physics raycast from `CanBeTargeted`; native turret targeting already performs line-of-sight checks.
- Caches turret/player authorization decisions for 0.5 seconds to avoid repeating TC and shared-authorization lookups on every target tick.

Reload command:

```text
oxide.reload ToolCupboardTurrets
```

## Dropped-item despawn policy

The global ordinary-item fallback was changed to three minutes:

```cfg
server.itemdespawn 180
```

This is an intentional local improvement over the backup value of `60` seconds.

The new `DroppedItemDespawn` plugin applies category-specific lifetimes suitable for the ×1000 server:

- Resources and components: 60 seconds
- Ammunition and explosives: 180 seconds
- Weapons, armour, and tools: 300 seconds
- Everything else: 180 seconds

The plugin only reschedules individually dropped `DroppedItem` entities. It does not modify corpses, backpacks, dropped containers, or supply drops.

Reload command:

```text
oxide.reload DroppedItemDespawn
```

Live console command for the global fallback:

```text
server.itemdespawn 180
```

## Security and repository hygiene

- `oxide/config/Secrets.local.json` was not copied from the backup.
- `.audit_*` backup-comparison and pre-sync snapshot directories are now ignored by Git.
- The harmless `Secrets.local.example.json` template remains available for documenting the required Discord token shape.

## Rank-based tool cupboard limits

`CupboardLimiter.json` now defines seven custom limits for the website-synchronized VIP groups. All player and team allowances remain within the requested 5–20 TC range.

- Default: 5
- VIP (`rank_vip`): 7
- VIP+ (`rank_vip_plus`): 9
- MVP (`rank_mvp`): 11
- Golden (`rank_golden_vip`): 13
- Diamond (`rank_diamond_vip`): 15
- Titan (`rank_titan_vip`): 18
- Ultimate (`rank_ultimate_vip`): 20

Team allowances are capped at 20:

- 2 players: 7
- 3 players: 10
- 4 players: 13
- 5 players: 16
- 6–8 players: 20

Cupboard permissions now map directly to the public rank names: `cupboardlimiter.vip`, `cupboardlimiter.vipplus`, `cupboardlimiter.mvp`, `cupboardlimiter.golden`, `cupboardlimiter.diamond`, `cupboardlimiter.titan`, and `cupboardlimiter.ultimate`. The permission-reset CFG grants each permission to its corresponding `rank_*` group. The old all-purpose VIP override was renamed to `cupboardlimiter.legacyvip` so it cannot override the new 7-TC VIP tier.

## Rank-based NTeleportation benefits

NTeleportation now uses dedicated permissions for the same seven website VIP groups.

Home slots follow the cupboard progression:

- Default: 5
- VIP: 7
- VIP+: 9
- MVP: 11
- Golden: 13
- Diamond: 15
- Titan: 18
- Ultimate: 20

Home, TPR, Outpost, and Bandit cooldowns/countdowns decrease by rank:

- Default: 10 seconds
- VIP: 9 seconds
- VIP+: 8 seconds
- MVP: 7 seconds
- Golden: 6 seconds
- Diamond: 5 seconds
- Titan: 3 seconds
- Ultimate: instant (0 seconds)

Existing unlimited daily home and TPR allowances remain unchanged. Permission names map directly to the public rank names: `nteleportation.vip`, `nteleportation.vipplus`, `nteleportation.mvp`, `nteleportation.golden`, `nteleportation.diamond`, `nteleportation.titan`, and `nteleportation.ultimate`. The permission-reset CFG grants each permission to its corresponding `rank_*` Oxide group.

## Three-day daylight/night schedule

`TimeOfDay` v2.3.5 prevents dusk and darkness on the first two days of each three-day cycle.

- Day 1: at 17:00, advance directly to the next day at 09:00.
- Day 2: at 17:00, advance directly to the next day at 09:00.
- Day 3: allow sunset and run the scheduled two-minute night.
- After the scheduled two-minute third night, keep Rust's natural sunrise time.
- `skippedNightCutoffHour` and `dayStartHour` are configurable and default to `17.0` and `9.0`.

Reload command:

```text
oxide.reload TimeOfDay
```

## Verification performed

- Verified all imported shared backup files byte-for-byte after synchronization.
- Verified local-only plugin and config files remained present.
- Verified `server.itemdespawn` is set to `180` after backup reconciliation.
- Verified the category despawn configuration parses as valid JSON.
- Verified plugin source brace balance for the newly added despawn plugin and turret-state update.

## Deployment order

1. Push the reconciled repository files.
2. Deploy the added or changed plugin/config files to the live server.
3. Run `server.itemdespawn 180` in the live console.
4. Reload `DroppedItemDespawn`.
5. Reload `TurretSwitches` if its current live instance predates v1.9.
6. Confirm the console reports no compilation errors.
7. Drop one resource, one ammunition/explosive item, and one weapon to verify the configured lifetimes.
8. Toggle a turret through wiring or another plugin and confirm its attached switch reflects the state within one second.

## Suggested Git commit

```text
sync live backup and add category-based loot despawn policy

- reconcile plugins, configs and server cfg with latest live backup
- import live-only management and gameplay plugins
- retain local-only development plugins and secret template
- synchronize turret switch visuals with powered turret state
- add category-specific dropped-item despawn timers for x1000 rates
- set ordinary dropped-item fallback to 180 seconds
- exclude secrets and local audit snapshots from version control
```
