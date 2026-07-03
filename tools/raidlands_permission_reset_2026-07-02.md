# Raidlands Permission Reset Runbook

Use `oxide/plugins/RaidlandsPermissionReset.cs` for the live reset. The cfg batch is kept as a fallback, but this host did not apply Oxide permission commands through `exec`.

`RaidlandsPermissionReset` v1.0.4 also fixes the first live run issue where Oxide rejected foreign permission registration attempts and dropped grants such as `spawnheli.minicopter.spawn` and `serverrewards.paidpvpkit`. It now tries the normal Oxide permission API first, then falls back to Oxide group data for the exact grant/revoke that did not stick. Elite also receives lower-tier VIP permissions directly so it still works if Oxide only evaluates one parent level.

The current public-population baseline intentionally grants `default` the mini, skin/building-skin permissions, backpack use/GUI, a 12-slot backpack, backpack fetch, and the paid PvP kit permission. Larger backpack sizes, gather/retrieve automation, keep-on-death, and no-food-spoil remain VIP tier value.

## Before Running

1. Back up these live files:
   - `oxide/data/oxide.groups.data`
   - `oxide/data/oxide.users.data`
   - `oxide/config/WebsiteVipBridge.json`
   - `oxide/data/BetterChat.json`
2. Upload the updated JSON files:
   - `oxide/plugins/RaidlandsPermissionReset.cs`
   - `oxide/config/WebsiteVipBridge.json`
   - `oxide/data/BetterChat.json`
3. Upload the batch cfg:
   - `server/rust/cfg/raidlands_permission_reset_2026-07-02.cfg`
   - `server/rust/cfg/raidlands_permission_verify_2026-07-02.cfg`
4. Keep `tools/oxide_permission_audit_before_2026-07-02.md` as the baseline report.

## Run

Run this command in the live Rust server console or RCON:

```text
oxide.load RaidlandsPermissionReset
```

The plugin should print `Raidlands permission reset complete` and group summary lines.

If v1.0.0, v1.0.1, v1.0.2, or v1.0.3 is already loaded, upload v1.0.4 and run:

```text
oxide.reload RaidlandsPermissionReset
raidlands.permissions.apply
```

The cfg fallback is:

```text
exec raidlands_permission_reset_2026-07-02.cfg
```

If `exec` runs silently without changing permissions, use the plugin path above.

After verification, unload and remove the one-shot plugin:

```text
oxide.unload RaidlandsPermissionReset
```

## Verify

Run these checks after the command file:

```text
exec raidlands_permission_verify_2026-07-02.cfg
```

If that also prints nothing, run `oxide.show group vip_bronze` directly. If the direct command prints output, `exec` is not running or cannot find the cfg. If the direct command also prints nothing, the host console is likely filtering command replies.

Expected results:

- `default` has no parent and no longer has `lockedcratetimer.conf.use`, `autolock.use`, `autolock.item.bypass`, `friendlyfire.changestate`, `targetabledrones.untargetable`, or `toolcupboardturrets.ignore`.
- `default` has public promo conveniences including mini, skin/building-skin, backpack use/GUI, `backpacks.size.12`, `backpacks.fetch`, and `serverrewards.paidpvpkit`.
- `discord` has `kits.discord`.
- `vip_gold` inherits `vip_bronze`; `vip_elite` inherits `vip_gold`.
- `vip_bronze` directly has `spawnheli.minicopter.spawn`, `spawnheli.minicopter.fetch`, `spawnheli.minicopter.despawn`, `skins.use`, `buildingskins.use`, `buildingskins.build`, `buildingskins.tc`, `buildingskins.all`, `serverrewards.paidpvpkit`, `backpacks.size.12`, and `backpacks.fetch`.
- `perk_personal_mini` has the three `spawnheli.minicopter.*` permissions.
- `perk_raid_kit` has `serverrewards.paidpvpkit`.
- `perk_queue_priority` exists but has no permissions until a queue plugin is installed.
- `perk_supporter_badge` exists and is styled by BetterChat, but has no gameplay permission.
