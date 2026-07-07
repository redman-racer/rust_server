# Portable Airstrikes Plugin — LLM Coding Agent Spec

## Purpose

Implement a Rust/uMod plugin that lets players call portable airstrikes using the binocular ping system as the targeting input.

The system should support multiple delivery platforms and ordinance types:

- Drone drops
- Attack heli strikes
- Cargo plane / jet strikes
- A-10-style Bradley longbarrel strafing runs
- Mortar / off-map artillery
- MLRS-style barrages
- Vehicle-targeted homing missile strikes

Core design principle:

```text
Target type determines strike family.
Delivery platform determines payload scale.
Ordinance determines damage behavior.
```

---

## Core Player Flow

| Step | Player Action | Plugin Behavior |
|---:|---|---|
| 1 | Player equips binoculars | Plugin waits for valid ping event or recent ping state |
| 2 | Player pings ground or vehicle | Plugin stores ping position and/or target entity |
| 3 | Player selects strike type | Command, UI, item use, or permission-based menu |
| 4 | Plugin validates strike | Range, cooldown, permissions, safe zone, target type, cost |
| 5 | Plugin consumes cost | Item, economy, RP, permission token, or custom currency |
| 6 | Warning phase begins | Audio, chat, map marker, effects, incoming vehicle visual |
| 7 | Delivery platform appears | Drone, heli, plane/jet, or off-map support |
| 8 | Payload executes | Drops, rockets, cannon impacts, homing missiles, MLRS, mortar shells |
| 9 | Damage attribution occurs | Damage/logs should credit the caller where possible |
| 10 | Cleanup | Remove spawned helper entities, markers, timers, and effects |

---

## Target Types

| Target Type | Source | Valid Strike Families | Notes |
|---|---|---|---|
| `ground_ping` | Binocular ping on terrain/building/world point | Drone drops, heli rockets, plane drops, A-10, mortar, MLRS | Primary airstrike targeting mode |
| `vehicle_ping` | Binocular ping on a vehicle entity | Homing missiles, optional A-10 anti-armor | Vehicle entity should be tracked after ping |
| `player_ping` | Ping on player entity | Usually disabled | Too abusable unless admin/event only |
| `npc_ping` | Ping on NPC / Bradley / heli | Admin/event optional | Useful for PvE events |
| `invalid_ping` | No recent ping or expired ping | None | Strike should fail gracefully |

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

Use this relationship for general dropped ordinance:

```text
DroneCount = BaseCount
HeliCount = BaseCount * 2
PlaneCount = BaseCount * 3
```

For raid-heavy ordinance such as rockets, propane bombs, homing missiles, and MLRS rockets, use explicit caps instead of pure multiplication.

| Scaling Key | Meaning | Example With Base Count 4 |
|---|---|---:|
| `X` | Drone-sized small payload | 4 drops |
| `XX` | Heli-sized medium payload | 8 drops |
| `XXX` | Plane/jet-sized large payload | 12 drops |
| `SALVO` | Mortar or MLRS configured volley | 3–12 shots |
| `BURST_LINE` | A-10 cannon pulses along strafe path | 12–40 impact pulses |

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
| Artillery | Mortar HE | `mortar_he` | Mortar shell style | Indirect explosive support | Medium | High | Off-map |
| Artillery | Mortar Frag | `mortar_frag` | Frag mortar shell style | Anti-player open-area strike | Low | Very High | Off-map |
| MLRS | MLRS Rocket | `mlrs_rocket` | MLRS-style projectile | Top-tier raid/event strike | Very High | Very High | Plane / Jet |
| A-10 | Bradley Longbarrel Burst | `bradley_longbarrel_burst` | Bradley longbarrel-style cannon line | Strafing run, anti-player, anti-deployable | Scaled | Very High | Jet |

---

## Strike Type Matrix

| Strike Type | Internal ID | Target Source | Delivery | Payload | Count Logic | Spread Pattern | Main Use |
|---|---|---|---|---|---|---|---|
| Bee Swarm Drone | `bee_swarm_drone` | Ground ping | Drone | Bee Grenades | `X` | Small circle | Cheap harassment |
| Heavy Bee Swarm | `bee_swarm_heavy` | Ground ping | Heli / Plane | Bee Catapult Bombs | `XX / XXX` | Medium circle | Area denial |
| Beancan Drop | `beancan_drop` | Ground ping | Drone | Beancans | `X` | Small random circle | Low-tier explosive strike |
| F1 Cluster Drop | `f1_cluster` | Ground ping | Drone / Heli | F1 Grenades | `X / XX` | Small-medium circle | Anti-player cluster |
| 40mm HE Micro-Strike | `he_40mm_micro` | Ground ping | Drone / Heli | 40mm HE | `X / XX` | Tight circle | Compact lethal strike |
| Smoke Screen Drop | `smoke_screen` | Ground ping | Drone / Heli | Smoke | `X / XX` | Line or circle | Push / retreat support |
| Flash Breach Drop | `flash_breach` | Ground ping | Drone | Flashbangs | `X` | Tight circle | Raid breach support |
| Molotov Drop | `molotov_drop` | Ground ping | Drone | Molotovs | `X` | Small circle | Roof denial |
| Firebomb Run | `firebomb_run` | Ground ping | Heli / Plane | Firebombs | `XX / XXX` | Medium-large circle | Larger fire denial |
| Propane Bomb Drop | `propane_bomb_drop` | Ground ping | Heli / Plane | Propane bombs | Custom cap | Medium circle | Heavy raid pressure |
| HV Rocket Run | `hv_rocket_run` | Ground ping | Attack Heli | HV Rockets | Custom cap | Line / tight volley | Fast precision damage |
| Rocket Run | `rocket_run` | Ground ping | Attack Heli | Standard Rockets | Custom cap | Line / volley | Raid support |
| Incendiary Rocket Run | `incendiary_rocket_run` | Ground ping | Heli / Plane | Incendiary Rockets | Custom cap | Line / volley | Fire + structure pressure |
| Mortar HE Mission | `mortar_he` | Ground ping | Off-map | Mortar HE | Salvo | Wide circle | Indirect fire |
| Mortar Frag Mission | `mortar_frag` | Ground ping | Off-map | Frag Mortars | Salvo | Wide circle | Anti-player artillery |
| A-10 BRRRRT Run | `a10_strafe` | Ground ping | Jet / Plane | Bradley longbarrel-style impacts | Burst line | Long rectangle | Strafing run |
| Mini MLRS Barrage | `mini_mlrs` | Ground ping | Plane / Jet | MLRS Rockets | Salvo | Large circle | Heavy raid support |
| Full MLRS Barrage | `full_mlrs` | Ground ping | Plane / Jet | MLRS Rockets | Salvo | Very large circle | Event/admin/top-tier strike |
| Heli Homing Strike | `homing_heli` | Vehicle ping | Attack Heli | Homing Missiles | Custom cap | Tracks vehicle | Anti-vehicle strike |
| Jet Homing Strike | `homing_jet` | Vehicle ping | Jet / Plane | Homing Missiles | Custom cap | Tracks vehicle | Heavy anti-vehicle strike |

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

## Final Strike Catalog

| ID | Display Name | Target Type | Delivery | Payload / Mechanic | Tier | Role |
|---|---|---|---|---|---:|---|
| `bee_swarm_drone` | Bee Swarm Drone | Ground ping | Drone | Bee Grenades | 1 | Harassment |
| `bee_swarm_heavy` | Heavy Bee Swarm | Ground ping | Heli / Plane | Bee Catapult Bombs | 2 | Area denial |
| `beancan_drop` | Beancan Drop | Ground ping | Drone | Beancans | 1 | Cheap explosives |
| `f1_cluster` | F1 Cluster Drop | Ground ping | Drone / Heli | F1 Grenades | 1–2 | Anti-player |
| `smoke_screen` | Smoke Screen Drop | Ground ping | Drone / Heli | Smoke | 1 | Utility |
| `flash_breach` | Flash Breach Drop | Ground ping | Drone | Flashbangs | 1 | Breach support |
| `he_40mm_micro` | 40mm HE Micro-Strike | Ground ping | Drone / Heli | 40mm HE | 2 | Precise explosive |
| `molotov_drop` | Molotov Drop | Ground ping | Drone | Molotovs | 1 | Roof denial |
| `firebomb_run` | Firebomb Run | Ground ping | Heli / Plane | Firebombs | 2–3 | Fire area denial |
| `propane_bomb_drop` | Propane Bomb Drop | Ground ping | Heli / Plane | Propane Bombs | 3 | Raid pressure |
| `hv_rocket_run` | HV Rocket Run | Ground ping | Attack Heli | HV Rockets | 3 | Fast precision |
| `rocket_run` | Rocket Run | Ground ping | Attack Heli | Standard Rockets | 3 | Raid support |
| `incendiary_rocket_run` | Incendiary Rocket Run | Ground ping | Heli / Plane | Incendiary Rockets | 3 | Fire raid support |
| `mortar_he` | Mortar HE Mission | Ground ping | Off-map | HE Mortars | 2 | Indirect fire |
| `mortar_frag` | Mortar Frag Mission | Ground ping | Off-map | Frag Mortars | 2 | Anti-player artillery |
| `a10_strafe` | A-10 BRRRRT Run | Ground ping | Jet / Plane | Bradley longbarrel burst | 3 | Strafing run |
| `homing_heli` | Heli Homing Strike | Vehicle ping | Attack Heli | Homing Missiles | 3 | Anti-vehicle |
| `homing_jet` | Jet Homing Strike | Vehicle ping | Jet / Plane | Homing Missiles | 4 | Heavy anti-vehicle |
| `mini_mlrs` | Mini MLRS Barrage | Ground ping | Plane / Jet | MLRS Rockets | 4 | Heavy raid support |
| `full_mlrs` | Full MLRS Barrage | Ground ping | Plane / Jet | MLRS Rockets | 5 | Event/admin/top-tier |

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

### A-10 Implementation Notes

A coding agent should implement this as controlled damage pulses instead of spawning hundreds of physical projectiles.

Suggested algorithm:

```text
1. Get target ping position.
2. Pick strafe direction:
   - Option A: random cardinal-ish attack vector
   - Option B: caller-facing direction
   - Option C: configurable approach vector
3. Calculate start and end point around target:
   - start = target - direction * lineLength / 2
   - end = target + direction * lineLength / 2
4. Divide line into BurstCount impact points.
5. For each impact point:
   - Add random lateral offset inside Width
   - Raycast downward to find ground/building surface
   - Spawn impact effect
   - Apply scaled damage in small radius
   - Delay each pulse slightly for BRRRRT rhythm
6. Attribute damage to caller.
7. Cleanup effects/timers.
```

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

### Homing Missile Implementation Notes

```text
1. Detect or store vehicle entity from binocular ping.
2. Validate entity is alive, not destroyed, and still within max tracking distance.
3. Spawn missile projectile or simulated missile object from delivery platform.
4. Continuously update missile direction toward target vehicle.
5. Stop tracking if:
   - target destroyed
   - target too far
   - missile lifetime expired
   - line of sight rules fail, if enabled
6. On impact/proximity:
   - spawn explosion effect
   - apply vehicle-scaled damage
   - apply splash damage to nearby players/deployables
7. Attribute damage to caller.
```

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
| `CostItemShortname` | All player strikes | Economy / shop integration |
| `PermissionRequired` | Premium/admin strikes | Controls access |
| `DamageScalePlayers` | All damaging strikes | PvP balance |
| `DamageScaleBuildings` | Raid strikes | Raid balance |
| `DamageScaleVehicles` | Homing/A-10/rockets | Vehicle balance |
| `WarningDelaySeconds` | All visible strikes | Counterplay |
| `ShowMapMarker` | Heavy strikes | Lets defenders react |
| `CanBeSAMTargeted` | Heli/Plane/MLRS | Counterplay option |

---

## Suggested Config Shape

Use this as the starting config model.

```json
{
  "General": {
    "RequireBinocularPing": true,
    "MaxPingAgeSeconds": 20,
    "RequireLineOfSightToPing": true,
    "MaxCallRange": 250.0,
    "MinimumDistanceFromCaller": 25.0,
    "SafeZoneBlockRadius": 150.0,
    "EnableClanCooldowns": true,
    "EnableGlobalCooldowns": true,
    "DefaultWarningDelaySeconds": 8.0,
    "UseMapMarkersForHeavyStrikes": true,
    "DebugMode": false
  },
  "DeliveryScaling": {
    "DroneMultiplier": 1,
    "HeliMultiplier": 2,
    "PlaneMultiplier": 3
  },
  "DamageScales": {
    "Players": 1.0,
    "Buildings": 1.0,
    "Vehicles": 1.0,
    "Deployables": 1.0,
    "Turrets": 1.0
  },
  "StrikeDefinitions": {
    "bee_swarm_drone": {
      "Enabled": true,
      "DisplayName": "Bee Swarm Drone",
      "TargetType": "ground_ping",
      "Delivery": "drone",
      "Payload": "bee_grenade",
      "BaseCount": 6,
      "SpreadRadius": 8.0,
      "WarningDelaySeconds": 6.0,
      "CooldownPerPlayerSeconds": 120,
      "CooldownPerClanSeconds": 180,
      "CostItemShortname": "grenade.bee",
      "CostAmount": 3,
      "PermissionRequired": "portableairstrikes.use.bee"
    },
    "f1_cluster": {
      "Enabled": true,
      "DisplayName": "F1 Cluster Drop",
      "TargetType": "ground_ping",
      "Delivery": "drone",
      "Payload": "f1_grenade",
      "BaseCount": 5,
      "SpreadRadius": 7.0,
      "WarningDelaySeconds": 7.0,
      "CooldownPerPlayerSeconds": 180,
      "CooldownPerClanSeconds": 240,
      "CostItemShortname": "grenade.f1",
      "CostAmount": 5,
      "PermissionRequired": "portableairstrikes.use.f1"
    },
    "propane_bomb_drop": {
      "Enabled": true,
      "DisplayName": "Propane Bomb Drop",
      "TargetType": "ground_ping",
      "Delivery": "cargo_plane_jet",
      "Payload": "propane_bomb",
      "BaseCount": 2,
      "MaxCount": 6,
      "SpreadRadius": 12.0,
      "WarningDelaySeconds": 14.0,
      "CooldownPerPlayerSeconds": 900,
      "CooldownPerClanSeconds": 1200,
      "GlobalCooldownSeconds": 300,
      "CostItemShortname": "catapult.ammo.explosive",
      "CostAmount": 2,
      "PermissionRequired": "portableairstrikes.use.propane"
    },
    "a10_strafe": {
      "Enabled": true,
      "DisplayName": "A-10 BRRRRT Run",
      "TargetType": "ground_ping",
      "Delivery": "a10_gun_run",
      "Payload": "bradley_longbarrel_burst",
      "BurstCount": 24,
      "LineLength": 55.0,
      "Width": 7.0,
      "ImpactRadius": 2.5,
      "PulseDelaySeconds": 0.06,
      "DamageScalePlayers": 1.0,
      "DamageScaleBuildings": 0.35,
      "DamageScaleVehicles": 1.25,
      "DamageScaleDeployables": 1.0,
      "WarningDelaySeconds": 10.0,
      "CooldownPerPlayerSeconds": 600,
      "CooldownPerClanSeconds": 900,
      "GlobalCooldownSeconds": 180,
      "PermissionRequired": "portableairstrikes.use.a10"
    },
    "homing_jet": {
      "Enabled": true,
      "DisplayName": "Jet Homing Strike",
      "TargetType": "vehicle_ping",
      "Delivery": "cargo_plane_jet",
      "Payload": "homing_missile",
      "MissileCount": 3,
      "MaxTrackingSeconds": 12.0,
      "MaxTrackingDistance": 350.0,
      "VehicleDamageScale": 1.5,
      "SplashRadius": 5.0,
      "WarningDelaySeconds": 8.0,
      "CooldownPerPlayerSeconds": 900,
      "CooldownPerClanSeconds": 1200,
      "GlobalCooldownSeconds": 300,
      "PermissionRequired": "portableairstrikes.use.homing.jet"
    },
    "mini_mlrs": {
      "Enabled": true,
      "DisplayName": "Mini MLRS Barrage",
      "TargetType": "ground_ping",
      "Delivery": "cargo_plane_jet",
      "Payload": "mlrs_rocket",
      "RocketCount": 4,
      "SpreadRadius": 20.0,
      "WarningDelaySeconds": 16.0,
      "CooldownPerPlayerSeconds": 1800,
      "CooldownPerClanSeconds": 2400,
      "GlobalCooldownSeconds": 600,
      "PermissionRequired": "portableairstrikes.use.mlrs.mini"
    }
  }
}
```

---

## Suggested Permissions

| Permission | Meaning |
|---|---|
| `portableairstrikes.admin` | Full admin access |
| `portableairstrikes.use` | Allows basic strike use |
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

## Suggested Commands

| Command | Purpose |
|---|---|
| `/strike` | Opens strike menu |
| `/strike list` | Lists available strikes |
| `/strike <id>` | Calls selected strike on latest valid ping |
| `/strike cancel` | Cancels pending strike if allowed |
| `/strike debug` | Admin debug info |
| `/strike reload` | Reload config |
| `/strike give <player> <strikeId> <amount>` | Optional strike token granting |
| `/strike cooldowns` | Show player cooldowns |

---

## Validation Checklist

Before executing a strike, validate:

```text
- Player exists and is alive.
- Player has permission for selected strike.
- Strike ID exists and is enabled.
- Player has a recent binocular ping.
- Ping age <= MaxPingAgeSeconds.
- Ping target type matches selected strike TargetType.
- Target is within MaxCallRange.
- Target is farther than MinimumDistanceFromCaller.
- Line of sight rule passes, if enabled.
- Target is not inside blocked safe zone radius.
- Target is not in blocked monument list.
- Player cooldown is ready.
- Clan cooldown is ready, if enabled.
- Global cooldown is ready, if enabled.
- Required item/currency cost is available.
- Server entity count/performance safety checks pass.
```

---

## Performance Notes

Avoid spawning excessive live explosive entities at once.

Preferred implementation patterns:

| Strike Family | Performance-Friendly Approach |
|---|---|
| Drone drops | Spawn limited physical payloads with timers |
| Heli rocket runs | Spawn controlled rocket volleys with caps |
| Plane drops | Spawn limited payloads with spread and staggered timers |
| A-10 strafe | Simulate impacts with raycasts/effects/damage pulses |
| MLRS | Use capped rocket count and stagger launches |
| Homing missiles | Use limited missile count with simple tracking loop |
| Mortars | Use timed impact simulation rather than full physics if needed |

---

## Implementation Priority

### Phase 1 — Core Targeting and Basic Drops

```text
- Store recent binocular pings.
- Add /strike command.
- Add config and permissions.
- Implement drone delivery visual.
- Implement bee, beancan, F1, smoke, flash, molotov.
- Add cooldowns and cost consumption.
```

### Phase 2 — Heavy Ground Strikes

```text
- Add 40mm HE.
- Add firebomb.
- Add propane bomb.
- Add heli delivery tier.
- Add plane delivery tier.
- Add payload scaling.
```

### Phase 3 — Rocket and Mortar Systems

```text
- Add attack heli rocket run.
- Add HV, standard, and incendiary rocket variants.
- Add off-map mortar HE and frag missions.
- Add warning markers/effects.
```

### Phase 4 — A-10 Bradley Longbarrel Strafe

```text
- Implement strafe direction calculation.
- Implement burst-line impact simulation.
- Add Bradley longbarrel-style effects and damage scaling.
- Add A-10 variants.
```

### Phase 5 — Vehicle-Targeted Homing Missiles

```text
- Detect and store vehicle pings.
- Validate vehicle target.
- Implement homing missile tracking.
- Add heli and jet homing variants.
```

### Phase 6 — MLRS and Event/Admin Tools

```text
- Add mini MLRS barrage.
- Add full MLRS barrage.
- Add admin/event-only overrides.
- Add logging and analytics.
- Add optional clan/team cooldown integration.
```

---

## Open Implementation Questions for Coding Agent

The coding agent should investigate these during implementation:

```text
1. Best Oxide/uMod hook for detecting binocular pings directly.
2. Whether ping target entity can be reliably identified for vehicle pings.
3. Best available prefab names for each payload type on the current Rust build.
4. Whether drone/heli/plane visuals should be real entities, fake effects, or temporary NPC/server entities.
5. Whether damage attribution can be cleanly credited to the caller for each payload type.
6. Whether MLRS should use existing MLRS mechanics, simulated rockets, or plugin-created projectiles.
7. Whether SAM sites should counter heli/plane/MLRS delivery.
8. How to avoid excessive entity spawning during large strikes.
```

---

## Naming Recommendation

Recommended plugin class name:

```text
PortableAirstrikes
```

Recommended config file:

```text
PortableAirstrikes.json
```

Recommended data file:

```text
PortableAirstrikes_Data.json
```

Recommended permissions prefix:

```text
portableairstrikes
```
