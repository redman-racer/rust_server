# Portable Airstrikes Plugin — Codex Implementation Plan

## Document Role

This document is the planned implementation guide for a coding agent.

It is intentionally broken into context-sized chunks so Codex or another coding agent can implement the plugin progressively.

The airstrike option tables are kept separately in:

```text
portable_airstrikes_options_tables.md
```

---

## Product Goal

Build a Rust/uMod plugin named:

```text
PortableAirstrikes
```

The plugin lets players call configurable airstrikes by:

```text
1. Creating a fresh binocular target ping.
2. Possessing one generic Airstrike Authorization item.
3. Opening the airstrike menu or using a direct command.
4. Choosing the desired strike type.
5. Paying the selected strike's RP cost.
6. Waiting through a warning/inbound delay.
7. Watching the configured strike execute on the pinged target.
```

The system must avoid creating a different item for every strike.

Instead:

```text
One common item/token gates access.
RP cost determines affordability.
Permissions determine unlocks.
Config determines available strike definitions.
```

---

## Assumed Server Ecosystem

| System | Recommended Assumption | Implementation Requirement |
|---|---|---|
| Modding framework | Rust uMod/Oxide C# plugin | Implement as one `.cs` plugin |
| Currency | ServerRewards RP by default | Use adapter pattern so Economics/custom can be added later |
| Item gate | One generic airstrike item | Configurable item shortname, display name, and skin |
| Kits | Existing kits plugin or manual kit config | Provide item give command and item definition details |
| VIP keys | Existing VIP/key/reward systems | Support permissions, discounts, and token grants |
| Loot tables | Server loot plugin or plugin's own injection module | Provide optional loot injection config |
| UI | Rust CUI | Must have chat-command fallback |
| Targeting | Binocular ping preferred | Fallback debug/admin raycast targeting for testing only |

---

## Key Implementation Defaults

| Setting | Default |
|---|---|
| Plugin class | `PortableAirstrikes` |
| Config file | `PortableAirstrikes.json` |
| Data file | `PortableAirstrikes_Data.json` |
| Main command | `/strike` |
| Admin permission | `portableairstrikes.admin` |
| Base use permission | `portableairstrikes.use` |
| Airstrike item display name | `Airstrike Authorization Key` |
| Airstrike item shortname | `targeting.computer` |
| Airstrike item consumed | `true`, after successful validation and RP charge |
| Currency provider | `ServerRewards` |
| Selection mode | CUI menu with direct command fallback |
| Damage model | Config-scaled by entity type |
| A-10 model | Simulated Bradley longbarrel impact line |
| Homing missile target | Vehicle ping only by default |

---

## Required Player UX

### Primary Flow

```text
Player pings a target with binoculars.
Player runs /strike.
Plugin opens a strike picker UI.
Plugin filters options by latest ping type.
Player selects a strike.
Plugin shows confirmation with:
- Strike name
- RP cost
- Token requirement
- Cooldowns
- Warning delay
- Target type
Player confirms.
Plugin consumes 1 token and RP.
Strike begins warning/inbound sequence.
Strike executes.
```

### Direct Command Flow

```text
Player pings a target with binoculars.
Player runs /strike a10_strafe.
Plugin validates a10_strafe against target, permission, token, RP, and cooldowns.
Plugin calls strike or prints failure reason.
```

### Repeat Last Flow

```text
Player pings a new target.
Player runs /strike last.
Plugin attempts the player's last successful strike ID.
```

---

## UI Requirements

| UI Element | Required Behavior |
|---|---|
| Strike list | Show available strikes for latest ping type |
| Locked strikes | Configurable: hide or show locked with reason |
| Cost display | Show RP cost after discounts |
| Token display | Show whether player has required token |
| Cooldown display | Show player/clan/global cooldown status |
| Target display | Show ground/vehicle target summary |
| Confirmation button | Required by default for paid strikes |
| Cancel button | Always present |
| Admin debug info | Only for admin permission |

Menu filtering rules:

```text
ground_ping -> show ground strikes
vehicle_ping -> show vehicle strikes
invalid/no ping -> show targeting instructions
```

---

## Implementation Architecture

Use small internal services instead of one massive method.

Recommended internal components:

| Component | Responsibility |
|---|---|
| `ConfigData` | Root config model |
| `PluginData` | Cooldowns, last selected strikes, optional stats |
| `StrikeDefinition` | Config model for one strike |
| `StrikeRegistry` | Loads/enables strike definitions |
| `PingTargetService` | Tracks latest player pings |
| `TargetResolver` | Converts ping data into `AirstrikeTarget` |
| `AirstrikeItemService` | Finds, validates, consumes, and gives generic token |
| `CurrencyService` | RP balance, charge, refund, discounts |
| `PermissionService` | Permission checks and discount permissions |
| `CooldownService` | Player, clan, and global cooldown logic |
| `ValidationService` | Central strike validation |
| `StrikeSelectionUi` | CUI menu and confirmation |
| `StrikeCommandHandler` | `/strike` command routing |
| `StrikeExecutionService` | State machine for warning/inbound/impact |
| `DeliveryVisualService` | Drone/heli/plane/effect visuals |
| `DamageService` | Damage attribution and scaling |
| `LootDistributionService` | Optional token injection into loot containers |
| `DebugService` | Admin diagnostics and test commands |

---

## Core Data Models

The coding agent should create equivalent C# classes/enums.

```csharp
enum AirstrikeTargetType
{
    Invalid,
    GroundPing,
    VehiclePing,
    PlayerPing,
    NpcPing
}

enum DeliveryPlatformType
{
    Drone,
    AttackHeli,
    CargoPlaneJet,
    A10GunRun,
    OffMapMortar
}

enum StrikeExecutionState
{
    Requested,
    Validating,
    Confirming,
    Charged,
    Warning,
    Inbound,
    Impacting,
    Complete,
    Cancelled,
    Failed
}

class AirstrikeTarget
{
    public AirstrikeTargetType Type;
    public Vector3 Position;
    public NetworkableId? EntityId;
    public string EntityShortPrefabName;
    public double CreatedAt;
}

class StrikeDefinition
{
    public string Id;
    public bool Enabled;
    public string DisplayName;
    public AirstrikeTargetType TargetType;
    public DeliveryPlatformType Delivery;
    public string Payload;
    public int Tier;
    public int RPCost;
    public string PermissionRequired;
    public float WarningDelaySeconds;
    public float CooldownPerPlayerSeconds;
    public float CooldownPerClanSeconds;
    public float GlobalCooldownSeconds;
    public int BaseCount;
    public int MaxCount;
    public float SpreadRadius;
    public Dictionary<string, float> DamageScales;
}

class AirstrikeCallContext
{
    public BasePlayer Caller;
    public ulong CallerUserId;
    public StrikeDefinition Strike;
    public AirstrikeTarget Target;
    public int FinalRPCost;
    public bool TokenConsumed;
    public StrikeExecutionState State;
}
```

---

## Validation Pipeline

All strike calls must pass through one validation pipeline.

```text
Validate player:
- Player exists.
- Player is connected.
- Player is alive.
- Player has base use permission.
- Player has selected strike permission.

Validate strike:
- Strike ID exists.
- Strike is enabled.
- Strike target type matches latest ping.
- Delivery and payload combination is supported.

Validate target:
- Ping exists.
- Ping age <= MaxPingAgeSeconds.
- Target position is valid.
- Target is within MaxCallRange.
- Target is farther than MinimumDistanceFromCaller.
- Line of sight passes if enabled.
- Target is not in blocked safe zone.
- Target is not in blocked monument.
- Vehicle target still exists if vehicle strike.

Validate economy:
- Player has required airstrike item unless exempt.
- Player has enough RP after discounts.
- Player player cooldown is ready.
- Clan cooldown is ready if enabled.
- Global cooldown is ready if enabled.

Validate performance:
- Server entity count within safe threshold.
- Strike family not currently overloaded.
- No duplicate pending strike from same player if disallowed.
```

The validation method should return a structured result:

```csharp
class ValidationResult
{
    public bool Success;
    public string ReasonCode;
    public string UserMessage;
}
```

---

# Implementation Chunks

Each chunk below is designed to be a self-contained Codex task.

---

## Chunk 01 — Scaffold Plugin, Config, Data, Commands

### Goal

Create the base `PortableAirstrikes.cs` plugin with config, data storage, permissions, and basic commands.

### Files / Areas

```text
PortableAirstrikes.cs
ConfigData classes
PluginData classes
Permission registration
Command registration
```

### Tasks

```text
1. Create plugin metadata.
2. Create config model with:
   - General
   - AirstrikeItem
   - Currency
   - Selection
   - DeliveryScaling
   - DamageScales
   - StrikeDefinitions
   - LootDistribution
3. Load default config.
4. Save config safely.
5. Load/save data file.
6. Register base permissions.
7. Add /strike command.
8. Add /strike reload for admins.
9. Add /strike debug for admins.
```

### Acceptance Tests

```text
- Plugin loads without errors.
- Config file is created.
- Data file is created or initialized.
- /strike prints helpful message if no targeting system exists yet.
- /strike reload reloads config for admin.
- Permissions are registered.
```

---

## Chunk 02 — Generic Airstrike Item Service

### Goal

Implement the single airstrike token/item model.

### Design

The token should be a configurable Rust item.

Recommended default:

```json
{
  "DisplayName": "Airstrike Authorization Key",
  "Shortname": "targeting.computer",
  "SkinId": 0,
  "RequireCustomNameOrSkin": true,
  "RequiredAmount": 1,
  "ConsumeOnSuccessfulCall": true
}
```

### Tasks

```text
1. Implement item matching by shortname.
2. Add optional matching by display name.
3. Add optional matching by skin ID.
4. Implement HasAirstrikeToken(BasePlayer player).
5. Implement ConsumeAirstrikeToken(BasePlayer player, amount).
6. Implement GiveAirstrikeToken(BasePlayer player, amount).
7. Add admin command:
   /strike giveitem <player> <amount>
8. Add inventory checks to validation result, but do not yet charge RP.
```

### Acceptance Tests

```text
- Admin can give token item.
- Plugin detects token in player inventory.
- Plugin can consume one token.
- Plugin does not consume token on failed validation.
- Admin bypass works if configured.
```

---

## Chunk 03 — RP Currency Adapter

### Goal

Implement RP cost support without hardcoding the plugin to only one economy provider.

### Primary Provider

Use ServerRewards RP as the default provider.

### Adapter Interface

```csharp
interface ICurrencyAdapter
{
    bool IsAvailable();
    int GetBalance(ulong userId);
    bool Withdraw(ulong userId, int amount);
    bool Deposit(ulong userId, int amount);
}
```

### Tasks

```text
1. Add Currency config.
2. Implement ServerRewards adapter using plugin reference.
3. Add NullCurrency adapter for disabled/free mode.
4. Add optional Economics adapter placeholder if desired.
5. Add discount calculation by permission.
6. Add /strike balance command.
7. Add RP checks to validation.
8. Charge RP only after final validation.
9. Refund RP if strike fails after charge due to plugin error.
```

### Acceptance Tests

```text
- /strike balance shows RP or currency unavailable message.
- Player with insufficient RP cannot call strike.
- Player with discount permission sees reduced final RP cost.
- RP is withdrawn after successful confirmation.
- RP is not withdrawn on validation failure.
```

---

## Chunk 04 — Strike Registry and Default Strike Definitions

### Goal

Create the data-driven strike catalog.

### Tasks

```text
1. Add StrikeDefinition model.
2. Load StrikeDefinitions from config.
3. Create default definitions for:
   - bee_swarm_drone
   - beancan_drop
   - f1_cluster
   - smoke_screen
   - flash_breach
   - he_40mm_micro
   - molotov_drop
   - firebomb_run
   - propane_bomb_drop
   - hv_rocket_run
   - rocket_run
   - incendiary_rocket_run
   - mortar_he
   - mortar_frag
   - a10_strafe
   - homing_heli
   - homing_jet
   - mini_mlrs
   - full_mlrs
4. Add permission registration for each strike's permission.
5. Add helper methods:
   - GetStrike(id)
   - GetEnabledStrikes()
   - GetStrikesForTargetType(type)
```

### Acceptance Tests

```text
- Config contains all default strike definitions.
- Disabled strike does not show in registry.
- Unknown strike ID returns clean error.
- Permissions are registered for configured strikes.
```

---

## Chunk 05 — Ping Target Service

### Goal

Track each player's latest binocular ping and resolve it into an airstrike target.

### Important Note

The coding agent must verify the best available Rust/uMod hook for team/binocular pings on the target server build.

Do not assume a hook name without checking current available hooks or decompiled server methods.

### Preferred Behavior

```text
Use the real binocular/team ping event if available.
Store:
- player ID
- target position
- target entity ID if available
- target type
- timestamp
```

### Fallback Behavior

Add an admin/debug fallback:

```text
/strike debugping
```

This uses the player's current eye raycast to create a test target.

Do not make fallback raycast the default public targeting method unless config explicitly allows it.

### Tasks

```text
1. Implement PlayerPingTarget dictionary keyed by user ID.
2. Hook real ping event if available.
3. Resolve target type:
   - Vehicle entity -> VehiclePing
   - Terrain/building/world position -> GroundPing
   - Player entity -> PlayerPing
   - NPC/event entity -> NpcPing
4. Track ping age.
5. Implement GetLatestTarget(BasePlayer player).
6. Add debug command for admin test ping.
7. Add chat feedback when target is stored if debug mode is enabled.
```

### Acceptance Tests

```text
- Player's latest ping is stored.
- Expired pings are rejected.
- Ground pings resolve as GroundPing.
- Vehicle pings resolve as VehiclePing if entity info is available.
- /strike debugping creates a usable target for testing.
```

---

## Chunk 06 — Central Validation Service

### Goal

Create one validation path that every UI and command strike call uses.

### Tasks

```text
1. Implement ValidateStrikeCall(player, strikeId).
2. Check player state.
3. Check strike enabled.
4. Check base permission and strike permission.
5. Check target exists and target type matches strike.
6. Check ping age.
7. Check distance limits.
8. Check line of sight if enabled.
9. Check safe zone and monument block rules.
10. Check token item.
11. Check RP balance.
12. Check player/clan/global cooldowns.
13. Return structured failure reason.
```

### Acceptance Tests

```text
- Every failure gives a clear reason.
- Direct command and UI use the same validation.
- Failed validation consumes no token and no RP.
- Target-type mismatch is caught.
- Cooldown failure is caught.
```

---

## Chunk 07 — Strike Selection UI

### Goal

Implement the menu the player uses to choose the airstrike type.

### Menu Rules

```text
/strike opens the menu.
Menu reads latest valid ping.
Menu filters by ping type.
Menu shows RP cost after discounts.
Menu shows token status.
Menu shows cooldown status.
Menu supports confirmation before charge.
```

### UI States

| State | UI Behavior |
|---|---|
| No valid ping | Show instructions to ping with binoculars |
| Has ground ping | Show ground-compatible strikes |
| Has vehicle ping | Show vehicle-compatible strikes |
| Not enough RP | Show strike locked with needed RP |
| Missing token | Show token requirement |
| Cooldown active | Show remaining time |
| Permission missing | Hide or lock based on config |
| Ready | Enable select button |

### Tasks

```text
1. Build CUI layout.
2. Add strike list generation.
3. Add target summary.
4. Add selected strike confirmation.
5. Add UI callbacks.
6. Add close/cancel behavior.
7. Add direct command fallback:
   /strike <id>
8. Add repeat command:
   /strike last
```

### Acceptance Tests

```text
- /strike opens menu.
- No ping shows instructions.
- Ground ping shows ground strikes.
- Vehicle ping shows homing strikes.
- Locked strikes show correct reason.
- Confirming a strike calls the central execution pipeline.
```

---

## Chunk 08 — Charge, Consume, Cooldown, and Call State Machine

### Goal

Implement the common state machine used by every strike.

### State Order

```text
Requested
Validating
Confirming
Charged
Warning
Inbound
Impacting
Complete
```

Failure states:

```text
Cancelled
Failed
Refunded
```

### Tasks

```text
1. Create AirstrikeCallContext.
2. Revalidate immediately before charge.
3. Withdraw RP.
4. Consume token.
5. Start cooldowns.
6. Store pending strike.
7. Run warning delay.
8. Dispatch to strike executor.
9. Mark complete.
10. Refund RP if execution fails before impact.
11. Optionally do not refund if player cancels after warning starts, based on config.
```

### Acceptance Tests

```text
- Token and RP are charged exactly once.
- Failed validation charges nothing.
- Execution failure can refund RP.
- Cooldowns start at configured point.
- Player cannot spam multiple pending strikes if disabled.
```

---

## Chunk 09 — Loot, Kits, and VIP Distribution Support

### Goal

Make the generic airstrike token easy to place into kits, VIP keys, and loot tables.

### Important Concept

The plugin should not require separate token items per strike.

All distribution sources should give the same generic item.

### Tasks

```text
1. Add admin give command:
   /strike giveitem <player> <amount>
2. Add console command:
   portableairstrikes.giveitem <playerId/name> <amount>
3. Add helper docs/log output showing:
   - item shortname
   - display name
   - skin ID
4. Add optional loot injection module:
   - container prefab shortname
   - chance
   - min amount
   - max amount
5. Add VIP discount config by permission.
6. Add VIP permission unlock examples.
7. Add optional command rewards for other plugins to call.
```

### Example Loot Distribution Config

```json
{
  "LootDistribution": {
    "Enabled": true,
    "ContainerRules": {
      "crate_normal": {
        "Chance": 0.03,
        "MinAmount": 1,
        "MaxAmount": 1
      },
      "crate_normal_2": {
        "Chance": 0.04,
        "MinAmount": 1,
        "MaxAmount": 1
      },
      "crate_elite": {
        "Chance": 0.08,
        "MinAmount": 1,
        "MaxAmount": 2
      },
      "bradley_crate": {
        "Chance": 0.12,
        "MinAmount": 1,
        "MaxAmount": 2
      },
      "heli_crate": {
        "Chance": 0.12,
        "MinAmount": 1,
        "MaxAmount": 2
      }
    }
  }
}
```

### VIP Examples

```json
{
  "Currency": {
    "VipDiscountsByPermission": {
      "portableairstrikes.discount.vip": 0.10,
      "portableairstrikes.discount.vipplus": 0.20,
      "portableairstrikes.discount.elite": 0.30
    }
  }
}
```

### Kit Integration Concept

A kit plugin can include the configured item:

```text
Shortname: targeting.computer
Display Name: Airstrike Authorization Key
Skin ID: configured value
Amount: 1+
```

### Acceptance Tests

```text
- Admin can grant token through chat command.
- Console can grant token.
- Token can be added to loot containers if module enabled.
- VIP discount permissions change final RP cost.
- The same token works for every strike type.
```

---

## Chunk 10 — Base Delivery Executor Interface

### Goal

Create a common interface for all strike executors.

### Interface

```csharp
interface IStrikeExecutor
{
    string ExecutorId { get; }
    bool CanExecute(StrikeDefinition strike);
    void Execute(AirstrikeCallContext context);
}
```

### Executor Types

| Executor | Handles |
|---|---|
| `DroneDropExecutor` | Bee, beancan, F1, smoke, flash, molotov, 40mm small drops |
| `HeavyDropExecutor` | Firebomb, propane, heavy bee, plane/heli scaled drops |
| `RocketRunExecutor` | HV, standard, incendiary rocket runs |
| `MortarExecutor` | HE and frag mortar missions |
| `A10StrafeExecutor` | Bradley longbarrel strafe |
| `HomingMissileExecutor` | Vehicle-ping homing missiles |
| `MlrsExecutor` | Mini/full MLRS barrages |

### Tasks

```text
1. Create executor registry.
2. Route strike definitions to correct executor.
3. Add warning/inbound helper methods.
4. Add damage attribution helper.
5. Add effect spawning helper.
6. Add cleanup tracking.
```

### Acceptance Tests

```text
- Strike dispatches to correct executor.
- Unknown payload fails cleanly.
- Disabled executor fails cleanly.
- Executor errors are caught and logged.
```

---

## Chunk 11 — Drone Drop Executor

### Goal

Implement small dropped payloads.

### Handles

```text
bee_swarm_drone
beancan_drop
f1_cluster
smoke_screen
flash_breach
he_40mm_micro
molotov_drop
```

### Behavior

```text
Spawn drone visual.
Move or fake drone over target.
Drop BaseCount payloads with configured spread.
Stagger drops slightly.
Apply payload behavior.
Cleanup drone visual.
```

### Tasks

```text
1. Implement spawn/move/fake drone visual.
2. Implement circular spread calculation.
3. Implement staggered payload timers.
4. Implement each low-tier payload type.
5. Add safety cap for max payloads.
6. Add debug mode impact logs.
```

### Acceptance Tests

```text
- Drone strike executes on debug ground target.
- Payload count matches config.
- Spread is applied.
- Missing/invalid payload fails cleanly.
- Drone cleanup occurs.
```

---

## Chunk 12 — Heavy Drop Executor

### Goal

Implement heli/plane scaled drops.

### Handles

```text
bee_swarm_heavy
firebomb_run
propane_bomb_drop
```

### Behavior

```text
Choose delivery visual based on strike config.
Calculate final count:
- base count * platform multiplier
- capped by MaxCount
Drop payloads across wider spread.
Use warning delay longer than drone strikes.
```

### Tasks

```text
1. Implement heli visual option.
2. Implement cargo plane / jet visual option.
3. Implement platform multiplier.
4. Implement MaxCount cap.
5. Implement heavy payload drops.
6. Add stronger warning effects.
```

### Acceptance Tests

```text
- Heli drop doubles base count unless capped.
- Plane drop triples base count unless capped.
- Propane bomb count respects MaxCount.
- Warning delay runs before impact.
```

---

## Chunk 13 — Rocket Run Executor

### Goal

Implement attack-heli rocket runs.

### Handles

```text
hv_rocket_run
rocket_run
incendiary_rocket_run
```

### Behavior

```text
Spawn or fake attack heli approach.
Fire configured rocket volley across target line.
Use custom caps.
Apply projectile or simulated explosion behavior.
Cleanup visual.
```

### Tasks

```text
1. Calculate approach vector.
2. Spawn/fake attack heli visual.
3. Calculate rocket impact points.
4. Spawn rocket projectiles or simulate impacts.
5. Add incendiary effect support.
6. Apply damage attribution.
```

### Acceptance Tests

```text
- HV rocket run uses tight pattern.
- Standard rocket run uses configured volley.
- Incendiary rocket run creates fire effects.
- Damage scales apply.
```

---

## Chunk 14 — Mortar Executor

### Goal

Implement off-map mortar missions.

### Handles

```text
mortar_he
mortar_frag
```

### Behavior

```text
No aircraft required.
Announce fire mission.
After delay, impact shells in salvo.
Use wide spread.
Use impact whistle/effects.
```

### Tasks

```text
1. Implement salvo timer.
2. Implement spread points.
3. Implement HE mortar payload.
4. Implement frag mortar payload.
5. Add optional smoke mortar later.
6. Add damage scaling.
```

### Acceptance Tests

```text
- Mortar impacts happen after warning delay.
- Salvo count matches config.
- HE and frag payloads differ in damage profile.
- No aircraft entity remains.
```

---

## Chunk 15 — A-10 Bradley Longbarrel Strafe Executor

### Goal

Implement A-10 gun run using Bradley longbarrel-style mechanics.

### Critical Design

Do not spawn hundreds of projectiles.

Use controlled impact pulses:

```text
1. Calculate strafe line through target.
2. Divide line into BurstCount impact points.
3. Add random lateral offset within Width.
4. Raycast down to find hit surface.
5. Spawn tracer/impact/explosion effects.
6. Apply small-radius damage.
7. Delay each pulse slightly for BRRRRT rhythm.
```

### Handles

```text
a10_strafe
a10_short_burst
a10_standard_brrrrt
a10_heavy_brrrrt
a10_anti_armor
```

### Config Fields

```json
{
  "BurstCount": 24,
  "LineLength": 55.0,
  "Width": 7.0,
  "ImpactRadius": 2.5,
  "PulseDelaySeconds": 0.06,
  "DamageScalePlayers": 1.0,
  "DamageScaleBuildings": 0.35,
  "DamageScaleVehicles": 1.25,
  "DamageScaleDeployables": 1.0
}
```

### Tasks

```text
1. Implement strafe direction calculation.
2. Implement line start/end from target.
3. Implement burst point generation.
4. Implement downward raycasts.
5. Implement impact effects.
6. Implement scaled damage pulses.
7. Implement plane/jet flyover visual.
8. Add anti-armor variant support.
```

### Acceptance Tests

```text
- A-10 strike creates a long narrow impact line.
- BurstCount controls pulse count.
- Width controls lateral randomness.
- Building damage is reduced by config.
- Vehicle damage can be boosted by config.
- No excessive entity spawning occurs.
```

---

## Chunk 16 — Homing Missile Executor

### Goal

Implement vehicle-ping homing missile strikes.

### Handles

```text
homing_heli
homing_jet
homing_antiair_sweep
homing_anti_armor
```

### Behavior

```text
Use latest vehicle ping.
Validate target vehicle still exists.
Spawn missile(s) from heli or jet visual.
Track vehicle for limited time/distance.
Explode on impact or proximity.
Scale vehicle and splash damage.
```

### Tasks

```text
1. Resolve vehicle target by entity ID.
2. Validate vehicle alive/not destroyed.
3. Implement missile object or simulated missile.
4. Implement tracking loop.
5. Implement max tracking time.
6. Implement max tracking distance.
7. Implement proximity detonation.
8. Add anti-air filtering for flying vehicles.
```

### Acceptance Tests

```text
- Ground ping cannot call homing strike by default.
- Vehicle ping can call homing strike.
- Missile tracks target within limits.
- Missile stops if target invalid.
- Vehicle damage scale applies.
```

---

## Chunk 17 — MLRS Executor

### Goal

Implement mini and full MLRS barrages.

### Handles

```text
mini_mlrs
full_mlrs
```

### Options

The coding agent should evaluate current server capability and choose one:

```text
Option A: Use existing MLRS projectile mechanics if clean and controllable.
Option B: Simulate MLRS rockets with projectile/effect/damage pulses.
Option C: Integrate with existing MLRS plugin APIs if server already uses one.
```

### Behavior

```text
Plane/jet visual optional.
Long warning delay.
Large spread.
Staggered rocket impacts.
Strong global cooldown.
Optional map marker.
Optional SAM counterplay.
```

### Tasks

```text
1. Implement rocket count and spread.
2. Implement warning marker.
3. Implement staggered impacts.
4. Implement heavy damage scaling.
5. Implement global cooldown enforcement.
6. Add admin-only/full-barrage option if configured.
```

### Acceptance Tests

```text
- Mini MLRS launches configured rocket count.
- Full MLRS launches configured rocket count.
- Spread radius applies.
- Global cooldown prevents spam.
- Blocked zones reject MLRS calls.
```

---

## Chunk 18 — Safe Zones, Monuments, Teams, and Clan Cooldowns

### Goal

Add higher-level server safety controls.

### Tasks

```text
1. Implement safe-zone checks.
2. Implement blocked monument radius/list.
3. Implement team/clan cooldown adapter.
4. Add same-team friendly-fire option if possible.
5. Add owner/team warnings if configured.
6. Add global strike limits.
```

### Acceptance Tests

```text
- Safe-zone target is rejected.
- Blocked monument target is rejected.
- Clan cooldown blocks teammate spam.
- Global cooldown blocks heavy strike spam.
```

---

## Chunk 19 — Damage Attribution, Logging, and Admin Debugging

### Goal

Make the plugin auditable.

### Tasks

```text
1. Attribute damage to calling player where possible.
2. Log strike calls:
   - player
   - strike ID
   - target position/entity
   - RP cost
   - token consumed
   - result
3. Add debug command:
   /strike debug target
   /strike debug cooldowns
   /strike debug strikes
4. Add optional Discord/webhook log later.
```

### Acceptance Tests

```text
- Admin can inspect latest target.
- Admin can inspect cooldowns.
- Strike calls log clearly.
- Damage attribution works where possible.
```

---

## Chunk 20 — Balancing Pass and Performance Safety

### Goal

Prevent the plugin from becoming a server performance problem.

### Tasks

```text
1. Add max simultaneous strikes.
2. Add max simultaneous heavy strikes.
3. Add max spawned payloads per strike.
4. Add timer cleanup on unload.
5. Add entity cleanup on unload.
6. Add debug warnings for unsafe config values.
7. Add config sanity clamping.
```

### Acceptance Tests

```text
- Plugin unload cleans timers/entities.
- Excessive payload count is clamped.
- Too many active strikes are rejected.
- Bad config values are corrected or warned.
```

---

## Chunk 21 — End-to-End Test Pass

### Goal

Test the whole system as a player would use it.

### Test Scenarios

```text
1. Player with no token runs /strike.
2. Player with token but no RP runs /strike.
3. Player with token and RP but no ping runs /strike.
4. Player pings ground and opens menu.
5. Player calls bee_swarm_drone.
6. Player calls f1_cluster.
7. Player calls a10_strafe.
8. Player attempts homing strike on ground ping and is rejected.
9. Player pings vehicle and calls homing_heli.
10. Player attempts MLRS in blocked zone and is rejected.
11. VIP player sees discounted RP cost.
12. Admin calls /strike reload.
13. Plugin unloads during pending strike and cleans up.
```

### Acceptance Tests

```text
- All expected failures give readable messages.
- All successful calls consume exactly 1 token unless configured otherwise.
- RP is charged exactly once.
- Cooldowns apply.
- UI and direct command agree on validation.
```

---

# Suggested Default Config Skeleton

```json
{
  "General": {
    "RequireBinocularPing": true,
    "MaxPingAgeSeconds": 20,
    "RequireLineOfSightToPing": true,
    "AllowFallbackRaycastTargeting": false,
    "MaxCallRange": 250.0,
    "MinimumDistanceFromCaller": 25.0,
    "SafeZoneBlockRadius": 150.0,
    "EnableClanCooldowns": true,
    "EnableGlobalCooldowns": true,
    "DefaultWarningDelaySeconds": 8.0,
    "UseMapMarkersForHeavyStrikes": true,
    "DebugMode": false,
    "MaxSimultaneousStrikes": 8,
    "MaxSimultaneousHeavyStrikes": 2
  },
  "AirstrikeItem": {
    "Enabled": true,
    "DisplayName": "Airstrike Authorization Key",
    "Shortname": "targeting.computer",
    "SkinId": 0,
    "RequireCustomNameOrSkin": true,
    "RequiredAmount": 1,
    "ConsumeOnSuccessfulCall": true,
    "AllowAdminsWithoutItem": true
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
  },
  "DamageScales": {
    "Players": 1.0,
    "Buildings": 1.0,
    "Vehicles": 1.0,
    "Deployables": 1.0,
    "Turrets": 1.0
  },
  "LootDistribution": {
    "Enabled": false,
    "ContainerRules": {
      "crate_normal": {
        "Chance": 0.03,
        "MinAmount": 1,
        "MaxAmount": 1
      },
      "crate_elite": {
        "Chance": 0.08,
        "MinAmount": 1,
        "MaxAmount": 2
      }
    }
  },
  "StrikeDefinitions": {
    "bee_swarm_drone": {
      "Enabled": true,
      "DisplayName": "Bee Swarm Drone",
      "TargetType": "ground_ping",
      "Delivery": "drone",
      "Payload": "bee_grenade",
      "Tier": 1,
      "RPCost": 50,
      "BaseCount": 6,
      "SpreadRadius": 8.0,
      "WarningDelaySeconds": 6.0,
      "CooldownPerPlayerSeconds": 120,
      "CooldownPerClanSeconds": 180,
      "GlobalCooldownSeconds": 0,
      "PermissionRequired": "portableairstrikes.use.bee"
    },
    "a10_strafe": {
      "Enabled": true,
      "DisplayName": "A-10 BRRRRT Run",
      "TargetType": "ground_ping",
      "Delivery": "a10_gun_run",
      "Payload": "bradley_longbarrel_burst",
      "Tier": 3,
      "RPCost": 1000,
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
      "Tier": 4,
      "RPCost": 1500,
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
    }
  }
}
```

---

# Codex Handoff Instruction

When feeding this to Codex, do not ask it to implement the entire system at once.

Use this pattern:

```text
Read portable_airstrikes_options_tables.md and portable_airstrikes_codex_implementation_plan.md.

Implement Chunk XX only.

Do not start later chunks yet.

Preserve config compatibility.

Add clear comments where current Rust/uMod hook names or prefab names need verification against the server build.

After implementing the chunk, summarize:
- files changed
- new config keys
- test steps
- known unresolved verification points
```

---

# Clarifications Left Configurable

These are intentionally configurable instead of blocking implementation:

| Question | Config Default |
|---|---|
| Is the token consumed? | Yes, on successful call |
| Which RP plugin is used? | ServerRewards |
| Can admins call without token/RP? | Yes |
| Should locked strikes show in UI? | Yes |
| Can raycast targeting replace real pings? | No, debug/admin only |
| Can full MLRS be public? | No recommendation; permission/config controls it |
