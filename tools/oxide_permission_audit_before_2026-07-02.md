# Oxide Permission Audit

- Groups file: `C:\wamp64\www\rust_server\oxide\data\oxide.groups.data`
- Users file: `C:\wamp64\www\rust_server\oxide\data\oxide.users.data`

## Warnings
- default inherits from discord
- managed groups with no permissions or parent: vip_bronze, vip_gold, vip_elite, perk_personal_mini, perk_skinbox, perk_raid_kit, perk_queue_priority, perk_supporter_badge
- default review permissions present: lockedcratetimer.conf.use, autolock.use, autolock.item.bypass, friendlyfire.changestate, targetabledrones.untargetable, toolcupboardturrets.ignore
- 2 users have direct permissions or non-default groups

## Group Summary

| Group | Title | Rank | Parent | Permissions |
| --- | --- | ---: | --- | ---: |
| `admin` | `admin` | 1 | `` | 11 |
| `authenticated` | `authenticated` | 0 | `` | 0 |
| `default` | `default` | 0 | `discord` | 62 |
| `discord` | `` | 0 | `` | 1 |
| `perk_personal_mini` | `perk_personal_mini` | 0 | `` | 0 |
| `perk_queue_priority` | `perk_queue_priority` | 0 | `` | 0 |
| `perk_raid_kit` | `perk_raid_kit` | 0 | `` | 0 |
| `perk_skinbox` | `perk_skinbox` | 0 | `` | 0 |
| `perk_supporter_badge` | `perk_supporter_badge` | 0 | `` | 0 |
| `vip_bronze` | `vip_bronze` | 0 | `` | 0 |
| `vip_elite` | `vip_elite` | 0 | `` | 0 |
| `vip_gold` | `vip_gold` | 0 | `` | 0 |

## Group Permissions

### admin
- Title: `admin`
- Rank: `1`
- Parent: ``
- Permissions:
  - `betterchat.admin`
  - `discordauth.deauth`
  - `godmode.admin`
  - `godmode.autoenable`
  - `godmode.invulnerable`
  - `godmode.lootprotection`
  - `godmode.noattacking`
  - `godmode.toggle`
  - `godmode.untiring`
  - `Kits.admin`
  - `scoreboards.admin`

### authenticated
- Title: `authenticated`
- Rank: `0`
- Parent: ``
- Permissions: none

### default
- Title: `default`
- Rank: `0`
- Parent: `discord`
- Permissions:
  - `autodoors.use`
  - `autolock.item.bypass`
  - `autolock.use`
  - `automaticauthorization.use`
  - `AutoPickupBarrel.Barrel.InstaKill`
  - `AutoPickupBarrel.Barrel.On`
  - `AutoPickupBarrel.RoadSign.On`
  - `backpacks.gui`
  - `backpacks.size.6`
  - `backpacks.use`
  - `bgrade.all`
  - `blueprintmanager.all`
  - `buildingskins.all`
  - `buildingskins.build`
  - `buildingskins.tc`
  - `buildingskins.use`
  - `disablewet.use`
  - `discordauth.auth`
  - `friendlyfire.changestate`
  - `guishop.use`
  - `instantbuy.use`
  - `instantcraft.use`
  - `instantgather.use`
  - `instantsmelt.use`
  - `kits.build`
  - `kits.cards`
  - `kits.comp`
  - `kits.medical`
  - `kits.raid`
  - `kits.scuba`
  - `lockedcratetimer.conf.use`
  - `nteleportation.bypassfoundationcheck`
  - `nteleportation.craftbandit`
  - `nteleportation.crafthome`
  - `nteleportation.craftoutpost`
  - `nteleportation.crafttown`
  - `nteleportation.crafttpr`
  - `nteleportation.exemptfrominterruptcountdown`
  - `nteleportation.globalcooldownvip`
  - `nteleportation.home`
  - `nteleportation.tpb`
  - `nteleportation.tpbandit`
  - `nteleportation.tpisland`
  - `nteleportation.tpoutpost`
  - `nteleportation.tpr`
  - `nteleportation.tpt`
  - `nteleportation.tptown`
  - `powerlessturrets.radius`
  - `powerlessturrets.samradius`
  - `powerlessturrets.use`
  - `privatemessages.allow`
  - `quicksmelt.use`
  - `randomrespawner.use`
  - `recyclerspeed.use`
  - `removertool.normal`
  - `skins.use`
  - `sortbutton.use`
  - `spawnheli.minicopter.despawn`
  - `spawnheli.minicopter.fetch`
  - `spawnheli.minicopter.spawn`
  - `targetabledrones.untargetable`
  - `toolcupboardturrets.ignore`

### discord
- Title: ``
- Rank: `0`
- Parent: ``
- Permissions:
  - `kits.discord`

### perk_personal_mini
- Title: `perk_personal_mini`
- Rank: `0`
- Parent: ``
- Permissions: none

### perk_queue_priority
- Title: `perk_queue_priority`
- Rank: `0`
- Parent: ``
- Permissions: none

### perk_raid_kit
- Title: `perk_raid_kit`
- Rank: `0`
- Parent: ``
- Permissions: none

### perk_skinbox
- Title: `perk_skinbox`
- Rank: `0`
- Parent: ``
- Permissions: none

### perk_supporter_badge
- Title: `perk_supporter_badge`
- Rank: `0`
- Parent: ``
- Permissions: none

### vip_bronze
- Title: `vip_bronze`
- Rank: `0`
- Parent: ``
- Permissions: none

### vip_elite
- Title: `vip_elite`
- Rank: `0`
- Parent: ``
- Permissions: none

### vip_gold
- Title: `vip_gold`
- Rank: `0`
- Parent: ``
- Permissions: none

## Direct User Drift
- `76561199084450795` `Heizenberg`: groups `admin`, `default`; direct permissions none
- `76561199159976493` `SkullySlayer`: groups `default`; direct permissions `backpacks.size.12`
