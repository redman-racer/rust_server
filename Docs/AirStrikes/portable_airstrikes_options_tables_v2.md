# Portable Airstrikes Plugin — Airstrike Options Tables

## Document Role

This document defines the airstrike option relationships only.

Use this file to understand:

- Available target types
- Delivery platforms
- Ordinance categories
- Strike catalog
- Payload scaling
- A-10 / Bradley longbarrel behavior
- Homing missile behavior
- Single airstrike binocular item + RP selection model

Implementation sequencing belongs in:

```text
portable_airstrikes_codex_implementation_plan.md
```

---

## Core Design Summary

```text
The player uses binoculars to create a fresh target ping.
The player carries one configured Airstrike Targeting Binoculars item to access the strike system.
The player chooses the strike type from a menu or direct command.
The plugin charges RP based on the selected strike type.
The selected strike definition determines delivery platform, payload, spread, delay, damage, and cooldown.
```

Important design decision:

```text
Do not create one item per airstrike type.

Use one common airstrike binocular item.
Use config, permissions, RP costs, and cooldowns to control which airstrikes can be called.
```

---

## Single Airstrike Item Model

| Concept | Recommended Value | Notes |
|---|---|---|
| Generic item name | `Airstrike Targeting Binoculars` | Display name can be changed in config |
| Default item shortname | `tool.binoculars` | Holdable/usable binoculars; configurable |
| Skin ID | Configurable | `tool.binoculars` currently reports `HasSkins=false`, so this is mainly for alternate item shortnames that support skins |
| Consumed on call | Configurable, recommended `true` | Keeps loot-table item meaningful |
| Strike type stored on item? | No | Strike type is chosen at call time |
| RP cost required? | Yes | Higher-tier strikes cost more RP |
| VIP support | Yes | VIP groups can get discounts, permissions, or kit access |
| Loot table support | Yes | Same single item can be inserted into any configured loot source |

Recommended default behavior:

```text
Player needs:
1. A valid recent binocular ping
2. At least 1 Airstrike Targeting Binoculars item
3. Enough RP for the chosen strike
4. Permission for the chosen strike
5. No active cooldown blocking the strike
```

---

## Player Strike Selection Flow

| Step | Player Action | Plugin Behavior |
|---:|---|---|
| 1 | Player pings target with binoculars | Plugin stores latest valid ground or vehicle ping |
| 2 | Player uses `/strike` or pings while holding the configured binocular item | Plugin opens strike selection UI if no default exists, or attempts the saved default |
| 3 | Plugin detects target type | Ground ping shows ground strikes; vehicle ping shows vehicle-target strikes |
| 4 | Player views strike options | UI shows RP cost, cooldown, permission lock, item requirement, default state, and target compatibility |
| 5 | Player chooses strike | Plugin revalidates ping, item, RP, cooldown, permissions, and safe-zone rules |
| 6 | Plugin confirms or immediately calls strike | Configurable confirmation step |
| 7 | Plugin consumes item and RP | Recommended only after all validation passes |
| 8 | Warning delay starts | Audio/effects/map marker/chat warning |
| 9 | Strike executes | Delivery platform and payload behavior come from selected strike definition |

Direct command fallback:

| Command | Behavior |
|---|---|
| `/strike` | Opens available strike menu for latest ping |
| `/strike <strikeId>` | Attempts to call a specific strike |
| `/strike last` | Attempts to repeat the player's last selected strike |
| `/strike cancel` | Cancels a pending strike, if config allows |
| `/strike balance` | Shows player RP, item count, discount, and saved default status |
| `/strike default <strikeId>` | Saves a persistent default used by future airstrike-binocular pings |

---

## Target Types

| Target Type | Source | Valid Strike Families | Notes |
|---|---|---|---|
| `ground_ping` | Binocular ping on terrain, building, deployable, or world point | Drone drops, heli rockets, plane drops, A-10, mortar, MLRS | Primary airstrike targeting mode |
| `vehicle_ping` | Binocular ping on vehicle entity | Homing missiles, optional A-10 anti-armor | Vehicle entity should be tracked after ping |
| `player_ping` | Ping on player entity | Usually disabled | Too abusable unless admin/event only |
| `npc_ping` | Ping on NPC, Bradley, patrol heli, event vehicle | Admin/event optional | Useful for PvE events |
| `invalid_ping` | No recent ping or expired ping | None | Strike should fail gracefully |

---

## Strike Selection by Target Type

| Latest Ping Type | Menu Should Show | Menu Should Hide / Lock |
|---|---|---|
| `ground_ping` | Bee, grenade, smoke, flash, 40mm, molotov, firebomb, propane, rockets, mortar, A-10, MLRS | Homing missile vehicle-only strikes |
| `vehicle_ping` | Homing heli, homing jet, anti-armor A-10 if enabled | Normal area drops unless config allows ground fallback at vehicle position |
| `player_ping` | Usually none | All normal player-callable strikes unless event/admin mode |
| `npc_ping` | Event/admin strikes if allowed | Public strikes unless config allows |
| `invalid_ping` | No strikes; show targeting instructions | Everything |

---

## Delivery Platform Tiers

| Delivery Platform | Tier | Visual Asset | Main Use | Payload Scaling | Ground Ping | Vehicle Ping |
|---|---:|---|---|---:|---:|---:|
| `drone` | 1 | Drone entity | Small drops, utility, harassment | `X` | Yes | Limited |
| `attack_heli` | 2 | Player attack heli or patrol heli-style visual | Rocket runs, heavy drops, homing missile launches | `XX` | Yes | Yes |
| `cargo_plane_jet` | 3 | Cargo plane / airdrop plane as jet stand-in | Large drops, MLRS-style barrages, homing missile launches | `XXX` | Yes | Yes |
| `a10_gun_run` | Special | Cargo plane / jet stand-in | Bradley longbarrel-style strafing run | `BURST_LINE` | Yes | Optional |
| `off_map_mortar` | Special | No aircraft required | Indirect artillery support | `SALVO` | Yes | No |

---

## Payload Scaling Rules

For general dropped ordinance:

```text
DroneCount = BaseCount
HeliCount = BaseCount * 2
PlaneCount = BaseCount * 3
```

For raid-heavy ordinance such as rockets, propane bombs, homing missiles, and MLRS rockets:

```text
Use explicit MaxCount / RocketCount / MissileCount values.
Do not blindly multiply these payloads without caps.
```

| Scaling Key | Meaning | Example With Base Count 4 |
|---|---|---:|
| `X` | Drone-sized small payload | 4 drops |
| `XX` | Heli-sized medium payload | 8 drops |
| `XXX` | Plane/jet-sized large payload | 12 drops |
| `SALVO` | Mortar or MLRS configured volley | 3–12 shots |
| `BURST_LINE` | A-10 cannon pulses along strafe path | 12–40 impact pulses |

---

## Suggested RP Cost Bands

These are relative bands. Final values should be tuned to the server's RP economy.

| Tier | Strike Class | Suggested RP Cost Band | Item Requirement |
|---:|---|---:|---|
| 1 | Utility / harassment | 25–100 RP | 1 binocular item |
| 2 | Anti-player / medium support | 100–300 RP | 1 binocular item |
| 3 | Raid pressure / rockets / A-10 | 300–900 RP | 1 binocular item |
| 4 | Heavy homing / mini MLRS | 900–2000 RP | 1 binocular item |
| 5 | Full MLRS / event-level strike | 2000+ RP | 1 binocular item or admin/event only |

---

## Ordinance Categories

| Category | Ordinance | Suggested Internal ID | Rust Item / Mechanic Style | Primary Role | Building Damage | Player Damage | Best Delivery |
|---|---|---|---|---|---:|---:|---|
| Swarm | Bee Grenade | `bee_grenade` | Bee grenade item | Harassment, chaos, anti-roof | None/Low | Medium | Drone |
| Swarm | Bee Catapult Bomb | `bee_catapult_bomb` | Bee bomb / catapult-style payload | Larger bee area denial | Low | Medium | Heli / Plane |
| Grenade | Beancan Grenade | `beancan` | Beancan item | Cheap explosive harassment | Low | Medium | Drone |
| Grenade | F1 Grenade | `f1_grenade` | F1 item | Anti-player cluster | Low | High | Drone / Heli |
| Utility | Smoke | `smoke` | Smoke grenade / 40mm smoke style | Breach cover, retreat cover | None | None | Drone / Heli |
| Utility | Flash | `flashbang` | Flashbang style | Breach support, disorientation | None | Utility | Drone |
| 40mm | 40mm HE | `he_40mm` | Grenade launcher HE style | Precise micro-strike | Medium-Low | High | Drone / Heli |
| Fire | Molotov | `molotov` | Molotov item | Area denial | Low | High over time | Drone |
| Fire | Firebomb | `firebomb` | Catapult firebomb style | Larger fire area | Medium | High over time | Heli / Plane |
| Heavy Bomb | Propane Bomb | `propane_bomb` | Propane explosive bomb | Raid pressure, structure damage | High | High | Heli / Plane |
| Rocket | HV Rocket | `hv_rocket` | Rocket projectile | Fast precision strike | Medium | High | Heli |
| Rocket | Standard Rocket | `rocket` | Rocket projectile | Raid support | High | High | Heli / Plane |
| Rocket | Incendiary Rocket | `incendiary_rocket` | Rocket projectile + fire | Fire raid support | Medium | High over time | Heli / Plane |
| Guided | Homing Missile | `homing_missile` | Homing missile style | Vehicle kill / chase strike | Medium | High | Heli / Plane |
| Artillery | Mortar HE | `mortar_he_payload` | Mortar shell style | Indirect explosive support | Medium | High | Off-map |
| Artillery | Mortar Frag | `mortar_frag_payload` | Frag mortar shell style | Anti-player open-area strike | Low | Very High | Off-map |
| MLRS | MLRS Rocket | `mlrs_rocket` | MLRS-style projectile | Top-tier raid/event strike | Very High | Very High | Plane / Jet |
| A-10 | Bradley Longbarrel Burst | `bradley_longbarrel_burst` | Bradley longbarrel-style cannon line | Strafing run, anti-player, anti-deployable | Scaled | Very High | Jet |

---

## Strike Type Matrix

| Strike Type | Internal ID | Target Source | Delivery | Payload | Count Logic | Spread Pattern | Suggested Tier | Main Use |
|---|---|---|---|---|---|---|---:|---|
| Bee Swarm Drone | `bee_swarm_drone` | Ground ping | Drone | Bee Grenades | `X` | Small circle | 1 | Cheap harassment |
| Heavy Bee Swarm | `bee_swarm_heavy` | Ground ping | Heli / Plane | Bee Catapult Bombs | `XX / XXX` | Medium circle | 2 | Area denial |
| Beancan Drop | `beancan_drop` | Ground ping | Drone | Beancans | `X` | Small random circle | 1 | Low-tier explosive strike |
| F1 Cluster Drop | `f1_cluster` | Ground ping | Drone / Heli | F1 Grenades | `X / XX` | Small-medium circle | 1–2 | Anti-player cluster |
| 40mm HE Micro-Strike | `he_40mm_micro` | Ground ping | Drone / Heli | 40mm HE | `X / XX` | Tight circle | 2 | Compact lethal strike |
| Smoke Screen Drop | `smoke_screen` | Ground ping | Drone / Heli | Smoke | `X / XX` | Line or circle | 1 | Push / retreat support |
| Flash Breach Drop | `flash_breach` | Ground ping | Drone | Flashbangs | `X` | Tight circle | 1 | Raid breach support |
| Molotov Drop | `molotov_drop` | Ground ping | Drone | Molotovs | `X` | Small circle | 1 | Roof denial |
| Firebomb Run | `firebomb_run` | Ground ping | Heli / Plane | Firebombs | `XX / XXX` | Medium-large circle | 2–3 | Larger fire denial |
| Propane Bomb Drop | `propane_bomb_drop` | Ground ping | Heli / Plane | Propane bombs | Custom cap | Medium circle | 3 | Heavy raid pressure |
| HV Rocket Run | `hv_rocket_run` | Ground ping | Attack Heli | HV Rockets | Custom cap | Line / tight volley | 3 | Fast precision damage |
| Rocket Run | `rocket_run` | Ground ping | Attack Heli | Standard Rockets | Custom cap | Line / volley | 3 | Raid support |
| Incendiary Rocket Run | `incendiary_rocket_run` | Ground ping | Heli / Plane | Incendiary Rockets | Custom cap | Line / volley | 3 | Fire + structure pressure |
| Mortar HE Mission | `mortar_he` | Ground ping | Off-map | Mortar HE | Salvo | Wide circle | 2 | Indirect fire |
| Mortar Frag Mission | `mortar_frag` | Ground ping | Off-map | Frag Mortars | Salvo | Wide circle | 2 | Anti-player artillery |
| A-10 BRRRRT Run | `a10_strafe` | Ground ping | Jet / Plane | Bradley longbarrel-style impacts | Burst line | Long rectangle | 3 | Strafing run |
| Mini MLRS Barrage | `mini_mlrs` | Ground ping | Plane / Jet | MLRS Rockets | Salvo | Large circle | 4 | Heavy raid support |
| Full MLRS Barrage | `full_mlrs` | Ground ping | Plane / Jet | MLRS Rockets | Salvo | Very large circle | 5 | Event/admin/top-tier strike |
| Heli Homing Strike | `homing_heli` | Vehicle ping | Attack Heli | Homing Missiles | Custom cap | Tracks vehicle | 3 | Anti-vehicle strike |
| Jet Homing Strike | `homing_jet` | Vehicle ping | Jet / Plane | Homing Missiles | Custom cap | Tracks vehicle | 4 | Heavy anti-vehicle strike |

---

## Final Strike Catalog With Selection Costs

| ID | Display Name | Target Type | Delivery | Payload / Mechanic | Tier | Suggested RP Cost | Role |
|---|---|---|---|---|---:|---:|---|
| `bee_swarm_drone` | Bee Swarm Drone | Ground ping | Drone | Bee Grenades | 1 | 25–75 | Harassment |
| `bee_swarm_heavy` | Heavy Bee Swarm | Ground ping | Heli / Plane | Bee Catapult Bombs | 2 | 100–250 | Area denial |
| `beancan_drop` | Beancan Drop | Ground ping | Drone | Beancans | 1 | 50–100 | Cheap explosives |
| `f1_cluster` | F1 Cluster Drop | Ground ping | Drone / Heli | F1 Grenades | 1–2 | 75–175 | Anti-player |
| `smoke_screen` | Smoke Screen Drop | Ground ping | Drone / Heli | Smoke | 1 | 25–75 | Utility |
| `flash_breach` | Flash Breach Drop | Ground ping | Drone | Flashbangs | 1 | 50–125 | Breach support |
| `he_40mm_micro` | 40mm HE Micro-Strike | Ground ping | Drone / Heli | 40mm HE | 2 | 150–300 | Precise explosive |
| `molotov_drop` | Molotov Drop | Ground ping | Drone | Molotovs | 1 | 75–150 | Roof denial |
| `firebomb_run` | Firebomb Run | Ground ping | Heli / Plane | Firebombs | 2–3 | 200–500 | Fire area denial |
| `propane_bomb_drop` | Propane Bomb Drop | Ground ping | Heli / Plane | Propane Bombs | 3 | 500–900 | Raid pressure |
| `hv_rocket_run` | HV Rocket Run | Ground ping | Attack Heli | HV Rockets | 3 | 350–650 | Fast precision |
| `rocket_run` | Rocket Run | Ground ping | Attack Heli | Standard Rockets | 3 | 600–1000 | Raid support |
| `incendiary_rocket_run` | Incendiary Rocket Run | Ground ping | Heli / Plane | Incendiary Rockets | 3 | 500–900 | Fire raid support |
| `mortar_he` | Mortar HE Mission | Ground ping | Off-map | HE Mortars | 2 | 200–400 | Indirect fire |
| `mortar_frag` | Mortar Frag Mission | Ground ping | Off-map | Frag Mortars | 2 | 150–350 | Anti-player artillery |
| `a10_strafe` | A-10 BRRRRT Run | Ground ping | Jet / Plane | Bradley longbarrel burst | 3 | 750–1250 | Strafing run |
| `homing_heli` | Heli Homing Strike | Vehicle ping | Attack Heli | Homing Missiles | 3 | 600–1000 | Anti-vehicle |
| `homing_jet` | Jet Homing Strike | Vehicle ping | Jet / Plane | Homing Missiles | 4 | 1000–1800 | Heavy anti-vehicle |
| `mini_mlrs` | Mini MLRS Barrage | Ground ping | Plane / Jet | MLRS Rockets | 4 | 1500–2500 | Heavy raid support |
| `full_mlrs` | Full MLRS Barrage | Ground ping | Plane / Jet | MLRS Rockets | 5 | 3000+ | Event/admin/top-tier |

---

## Delivery Platform to Ordinance Compatibility

| Ordinance | Drone | Attack Heli | Cargo Plane / Jet | Off-map |
|---|---:|---:|---:|---:|
| Bee Grenades | Yes | Yes | Yes | No |
| Bee Catapult Bombs | No | Yes | Yes | No |
| Beancans | Yes | Yes | Yes | No |
| F1 Grenades | Yes | Yes | Yes | No |
| Smoke | Yes | Yes | Yes | No |
| Flashbangs | Yes | Maybe | No | No |
| 40mm HE | Yes | Yes | Maybe | No |
| Molotovs | Yes | Yes | Maybe | No |
| Firebombs | No | Yes | Yes | No |
| Propane Bombs | No | Yes | Yes | No |
| HV Rockets | No | Yes | Yes | No |
| Standard Rockets | No | Yes | Yes | No |
| Incendiary Rockets | No | Yes | Yes | No |
| Homing Missiles | No | Yes | Yes | No |
| Mortar HE | No | No | No | Yes |
| Mortar Frag | No | No | No | Yes |
| MLRS Rockets | No | No | Yes | Maybe |
| Bradley Longbarrel Burst | No | No | Yes | No |

---

## A-10 / Bradley Longbarrel Relationship

The A-10 strike should not drop inventory items.

It should be implemented as a strafe mechanic that uses a Bradley longbarrel-style weapon behavior:

- Fast cannon impact simulation
- Long, narrow damage path
- Repeated hit pulses
- Explosive impact effects
- Audio + tracer effects
- Damage scaled by entity type

| A-10 Component | Plugin Behavior |
|---|---|
| Target input | Binocular ground ping |
| Delivery visual | Cargo plane / jet flyover |
| Weapon model | Bradley longbarrel cannon behavior |
| Damage method | Repeated impact pulses along a strafe line |
| Projectile style | Fast cannon shell / explosive bullet simulation |
| Impact pattern | Long narrow rectangle through the ping |
| Best against | Players, turrets, vehicles, deployables, exposed defenses |
| Reduced against | Armored building blocks, high-tier raid targets |
| Counterplay | Audio warning, visible flyover, delay before impact |
| Config identity | `A10_BradleyLongbarrel_Strafe` |

### A-10 Variants

| Variant | Internal ID | Burst Count | Line Length | Width | Building Damage | Vehicle Damage | Player Damage | Use |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Short Burst | `a10_short_burst` | 10–14 | 35m | 5m | Low | Medium | High | Cheap anti-player pass |
| Standard BRRRRT | `a10_standard_brrrrt` | 18–24 | 55m | 7m | Medium-Low | High | Very High | Main A-10 strike |
| Heavy BRRRRT | `a10_heavy_brrrrt` | 28–40 | 75m | 9m | Medium | Very High | Very High | Event/high-tier version |
| Anti-Armor Run | `a10_anti_armor` | 12–18 | 45m | 5m | Medium | Very High | High | Vehicle-focused version |

---

## Homing Missile Targeting

Homing missile strikes should use vehicle pings, not normal ground pings.

| Target Type | Valid? | Behavior |
|---|---:|---|
| Ground ping | No / optional | Use normal rockets instead |
| Player ping | Optional | Probably disabled for balance |
| Vehicle ping | Yes | Primary intended use |
| Attack heli ping | Yes | Anti-air option |
| Scrap heli ping | Yes | Anti-air option |
| Minicopter ping | Yes | Anti-air option |
| Tugboat / boat ping | Yes | Naval strike option |
| Modular car ping | Yes | Ground vehicle strike |
| Bradley / NPC vehicle ping | Admin/event only | Useful for PvE events |
| Drone ping | Maybe | Funny but low priority |

### Homing Missile Strike Variants

| Homing Missile Strike | Internal ID | Delivery | Missile Count | Lock Behavior | Use |
|---|---|---|---:|---|---|
| Heli Hunter | `homing_heli` | Attack Heli | 1–2 | Tracks pinged vehicle | Light anti-vehicle strike |
| Jet Hunter | `homing_jet` | Cargo Plane / Jet | 2–4 | Tracks pinged vehicle | Heavy anti-vehicle strike |
| Anti-Air Sweep | `homing_antiair_sweep` | Jet | 2–6 | Prioritizes flying vehicles near ping | Event/admin strike |
| Anti-Armor Strike | `homing_anti_armor` | Jet | 2–3 | Tracks ground vehicle | Modular car / Bradley-style event use |

---

## Menu Availability Rules

| Check | If Passing | If Failing |
|---|---|---|
| Player has valid ping | Show target-compatible strikes | Show targeting instructions |
| Player has airstrike binocular item | Enable strike selection | Show locked reason: `Requires Airstrike Targeting Binoculars` |
| Player has enough RP | Enable affordable strike | Show locked reason: `Need X RP` |
| Player has permission | Show/use strike | Hide or show locked, configurable |
| Player cooldown ready | Enable strike | Show remaining cooldown |
| Clan cooldown ready | Enable strike | Show remaining clan cooldown |
| Global cooldown ready | Enable strike | Show global cooldown |
| Target type compatible | Show strike | Hide incompatible strike |
| Safe-zone / monument allowed | Enable strike | Show blocked-zone warning |
| Strike enabled in config | Show strike | Hide strike |

---

## Recommended Permissions

| Permission | Meaning |
|---|---|
| `portableairstrikes.admin` | Full admin access |
| `portableairstrikes.use` | Allows basic strike system access |
| `portableairstrikes.use.bee` | Bee swarm strikes |
| `portableairstrikes.use.grenade` | Beancan/F1 style strikes |
| `portableairstrikes.use.utility` | Smoke/flash support |
| `portableairstrikes.use.40mm` | 40mm HE strikes |
| `portableairstrikes.use.fire` | Molotov/firebomb strikes |
| `portableairstrikes.use.propane` | Propane bomb strikes |
| `portableairstrikes.use.rocket` | Rocket run strikes |
| `portableairstrikes.use.mortar` | Mortar fire missions |
| `portableairstrikes.use.a10` | A-10 Bradley longbarrel strafe |
| `portableairstrikes.use.homing.heli` | Heli homing strikes |
| `portableairstrikes.use.homing.jet` | Jet homing strikes |
| `portableairstrikes.use.mlrs.mini` | Mini MLRS barrage |
| `portableairstrikes.use.mlrs.full` | Full MLRS barrage |

---

## Suggested Configuration Shape

```json
{
  "AirstrikeItem": {
    "Enabled": true,
    "DisplayName": "Airstrike Targeting Binoculars",
    "Shortname": "tool.binoculars",
    "SkinId": 0,
    "RequireCustomNameOrSkin": true,
    "RequiredAmount": 1,
    "ConsumeOnSuccessfulCall": true,
    "AllowAdminsWithoutItem": true,
    "TreatAsTargetingTool": true,
    "ShowEquipInstructions": true,
    "ToolTargetMarkerEnabled": true,
    "ToolTargetMarkerDurationSeconds": 18.0,
    "ToolTargetMarkerSize": 10.0,
    "ToolTargetMarkerAlpha": 0.55
  },
  "Currency": {
    "Enabled": true,
    "Provider": "ServerRewards",
    "AllowFreeAdminCalls": true,
    "VipDiscountsByPermission": {
      "portableairstrikes.discount.vip": 0.10,
      "portableairstrikes.discount.vipplus": 0.20,
      "portableairstrikes.discount.elite": 0.30
    }
  },
  "Selection": {
    "PrimaryMode": "CUI_MENU",
    "AllowDirectCommand": true,
    "OpenMenuCommand": "strike",
    "RequireConfirmation": true,
    "ShowLockedStrikes": true,
    "AutoFilterByPingType": true,
    "AllowRepeatLastStrike": true
  },
  "DeliveryScaling": {
    "DroneMultiplier": 1,
    "HeliMultiplier": 2,
    "PlaneMultiplier": 3
  }
}
```

---

## Distribution Model

| Distribution Source | Uses Same Airstrike Item? | Suggested Implementation |
|---|---:|---|
| Kits | Yes | Add the configured item shortname, display name, and skin to kit definitions |
| VIP keys | Yes | VIP keys can grant the item, extra binocular items, RP discounts, or permissions |
| Loot tables | Yes | Add the same binocular item to selected container rules |
| Events | Yes | Reward binocular items through event plugins or admin commands |
| Admin grants | Yes | `/strike giveitem <player> <amount>` |
| Shops | Yes | Sell the single binocular item, while RP still pays for selected strike |

---

## Balance and Control Settings

| Setting | Applies To | Reason |
|---|---|---|
| `RequireBinocularPing` | All strikes | Prevents command-only abuse |
| `RequireLineOfSightToPing` | Most strikes | Prevents calling through mountains/bases |
| `MaxPingAgeSeconds` | All strikes | Player must use a fresh target |
| `MaxCallRange` | All strikes | Prevents map-wide spam |
| `MinimumDistanceFromCaller` | Explosive strikes | Prevents suicide cheese |
| `SafeZoneBlockRadius` | All damaging strikes | Protects safe zones |
| `MonumentBlockList` | MLRS, rockets, propane | Prevents monument/event griefing |
| `CooldownPerPlayer` | All strikes | Prevents spam |
| `CooldownPerClan` | Heavy strikes | Prevents clan stacking |
| `GlobalCooldown` | MLRS, A-10, full barrages | Prevents server-wide chaos |
| `CostItemShortname` | All player strikes | Generic binocular item gate |
| `RPCost` | All player strikes | Tier pricing |
| `PermissionRequired` | Premium/admin strikes | Controls access |
| `DamageScalePlayers` | All damaging strikes | PvP balance |
| `DamageScaleBuildings` | Raid strikes | Raid balance |
| `DamageScaleVehicles` | Homing/A-10/rockets | Vehicle balance |
| `WarningDelaySeconds` | All visible strikes | Counterplay |
| `ShowMapMarker` | Heavy strikes | Lets defenders react |
| `CanBeSAMTargeted` | Heli/Plane/MLRS | Counterplay option |

---

## Clean Mental Model

| Player Action | Plugin Reads | Plugin Chooses | Result |
|---|---|---|---|
| Ping ground with binoculars | Ground position | Ground strike catalog | Drone drop, mortar, rocket run, A-10, MLRS |
| Ping vehicle with binoculars | Vehicle entity ID | Vehicle strike catalog | Homing missile strike |
| Open `/strike` menu | Item, RP, cooldowns, permissions, ping type, saved default | Available strike list | Player chooses strike |
| Select drone-tier strike | Ping position | `X` payload | Small drop |
| Select heli-tier strike | Ping position or vehicle | `XX` payload / rocket pass | Medium strike |
| Select plane-tier strike | Ping position or vehicle | `XXX` payload / barrage | Heavy strike |
| Select A-10 strike | Ping position | Bradley longbarrel strafe line | Cannon burst path |
| Select MLRS strike | Ping position | MLRS salvo | Wide rocket barrage |
