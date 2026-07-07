# RaidlandsEvents Event Manager Plugin Build Plan

**Project:** Raidlands Rust server event manager  
**Plugin name:** `RaidlandsEvents`  
**Plan version:** 0.1  
**Date:** 2026-07-06  
**Primary goal:** Build a PvP-first event manager that can schedule, launch, configure, score, reward, and clean up public events while allowing future event types to be imported or registered without rewriting the core manager.

---

## 1. Locked design decisions from planning

These are the current product decisions the plugin should treat as requirements.

1. **Events are public and PvP-first.**
   - No private/instanced events.
   - Player-purchased events are still public and counterable.
   - If a player buys a `RaidMe`-style event and another group counters or takes it over, that is intended gameplay.

2. **Events support three trigger models.**
   - Scheduled events.
   - Admin-started events.
   - Player-purchased events.

3. **Visibility is configurable per event.**
   - Exact grid.
   - Approximate grid.
   - Map marker.
   - Delayed marker.
   - Hidden until discovered.
   - Chat, Discord, and future website announcements.

4. **Difficulty and scoring scope are configurable per event.**
   - Solo events.
   - Team/clan events.
   - Player-based scoring.
   - Clan-based scoring.
   - Mixed scoring where individual contributors inside a clan can still affect reward distribution.

5. **Rewards are configurable and can be single-winner or split.**
   - Fixed placement rewards.
   - Percentage-based placement rewards from a reward pool.
   - Player-only reward payouts.
   - Clan rewards with even distribution.
   - Clan rewards with contribution-weighted distribution.

6. **Scoring is contributor-based.**
   - Kills.
   - Damage dealt.
   - NPCs killed.
   - Boss damage.
   - TC destroyed.
   - Crate unlocked.
   - Crate looted.
   - Time inside zone.
   - Event entity damage.
   - Future custom objective points.

7. **Clan integrations are soft dependencies.**
   - Existing clan plugins:
     - `Clans` by `k1lly0u`, version `0.2.10` in the current server stack.
     - `Clan Team`, rewritten, version `2.0.0` in the current server stack.
   - RaidlandsEvents should use the clan system when available.
   - RaidlandsEvents should not break if clan plugins are missing; clan-only event definitions should simply fail validation or be disabled with a clear admin warning.

8. **RaidlandsRoamBot is the bot authority.**
   - Event manager may hard-rely on RaidlandsRoamBot for NPC/boss/guard events.
   - RaidlandsEvents should not own movement/combat AI.
   - RaidlandsEvents sends spawn requests and receives bot group/entity IDs back.
   - RaidlandsRoamBot handles bot logic, profiles, movement, weapons, health, aggression, and difficulty behavior.

9. **Online player count should drive dynamic events.**
   - Dynamic/random events should scale or gate by online player count.
   - Scheduled events should always be allowed to run unless manually disabled.

10. **The MVP is the event manager framework.**
    - Do not start by overbuilding every event type.
    - Build the manager, editor, import/export, lifecycle, rules, scoring, rewards, and provider API first.
    - Event types can be added afterward as packs/modules/providers.

---

## 2. Product philosophy

RaidlandsEvents should be the **traffic controller** for server conflict.

The plugin should not just spawn loot. It should create temporary public objectives that make geared players leave base, fight over something worth taking, and generate repeatable battlefield moments.

The correct architecture is:

```text
RaidlandsEvents
  owns: scheduling, event definitions, rulesets, zones, markers, scoring, rewards, admin UI, data, cleanup, website reporting

RaidlandsRoamBot
  owns: NPC spawning, movement, difficulty, AI behavior, weapon/health profiles, bot cleanup behavior

Clans / Clan Team
  owns: clan membership, clan tags, team sync

Reward / RP / economy adapter
  owns: giving RP or running server reward commands

Website/API
  owns: display, leaderboards, event history, active events feed
```

RaidlandsEvents should avoid becoming a giant hardcoded event plugin. It should become a **manager plus registry**.

---

## 3. Dependency strategy

### 3.1 Hard dependencies

| Dependency | Required? | Reason |
|---|---:|---|
| Rust/uMod/Oxide server environment | Yes | Base plugin runtime. |
| RaidlandsRoamBot | Only for NPC/boss events | Bot-driven events should fail gracefully if bot plugin is missing. |

### 3.2 Soft dependencies / adapters

| Plugin/System | Required? | RaidlandsEvents behavior if missing |
|---|---:|---|
| `Clans` | No | Clan scoring/events disabled or fallback to player-only scoring. |
| `Clan Team` | No | No direct failure. Use Clans as source of truth, not in-game team alone. |
| RP/economy plugin | No | Reward adapter can run console commands or queue unpaid rewards. |
| Zone Manager | No | Internal rule engine should work without it. Optional bridge can create zones/domes. |
| Discord/webhook plugin | No | Discord announcements disabled. |
| Website API | No | Events still run; reports queue or skip external sync. |
| Raidable Bases / CopyPaste-style tools | No | Future event providers may use them, but core manager should not require them. |

### 3.3 Why soft dependencies matter

The server should not become fragile because one third-party plugin updates, breaks, or disappears. The event manager should own its own core lifecycle and use adapters only where useful.

For third-party plugins, wrap every call behind a small adapter class:

```text
ClanAdapter
RewardAdapter
ZoneAdapter
BotAdapter
DiscordAdapter
WebsiteAdapter
TeleportAdapter
KitsAdapter
BackpacksAdapter
```

Each adapter should have:

```csharp
bool IsAvailable { get; }
string StatusMessage { get; }
ValidationResult ValidateRequirement(EventDefinition eventDef);
```

---

## 4. Source review notes

These references informed the integration plan:

- uMod lists `Clans` by k1lly0u as a universal clans plugin with alliance support, plus API and hooks sections. This supports treating Clans as the source of truth for clan membership rather than building a new clan system inside RaidlandsEvents.  
  Source: <https://umod.org/plugins/clans>

- uMod lists `Clan Team` as a plugin that adds clan members to the same in-game team and depends on Clans. This supports treating Clan Team as a team-sync helper, not as the canonical clan database.  
  Source: <https://umod.org/plugins/clan-team>

- uMod hook documentation supports custom hooks and plugin-to-plugin integration patterns. The docs also recommend unsubscribing from CPU-intensive hooks when not needed, which should guide the scoring/rule engine implementation.  
  Source: <https://umod.org/documentation/api/hooks>

- uMod Rust API documentation groups hooks by server, player, entity, resource, structure, terrain, team, clan, plugin, and permission categories. RaidlandsEvents should use these hook groups for scoring and rules.  
  Source: <https://umod.org/documentation/games/rust>

- uMod’s Rust GUI guide covers CUI structure, buttons, input fields, and sending UI with `CuiHelper.AddUi`, which supports building the admin UI as an in-game CUI dashboard.  
  Source: <https://umod.org/guides/rust/basic-concepts-of-gui>

- Zone Manager supports zone flags such as `nobuild`, `nodeploy`, and `undest`; Dynamic PVP automatically creates/deletes Zone Manager zones for various events. These are useful patterns, but RaidlandsEvents should not require them because Raidlands is already PvP-first and needs a custom rule engine.  
  Sources: <https://umod.org/plugins/zone-manager>, <https://umod.org/plugins/dynamic-pvp>

---

## 5. High-level architecture

```text
RaidlandsEvents.cs
│
├── Core
│   ├── EventRegistry
│   ├── EventLifecycleManager
│   ├── EventInstanceStore
│   ├── EventDefinitionStore
│   ├── EventValidationService
│   └── EventCleanupService
│
├── Triggering
│   ├── ScheduleService
│   ├── WeightedRandomEventPicker
│   ├── AdminStartService
│   └── PurchaseTriggerService
│
├── Runtime Systems
│   ├── LocationService
│   ├── RuleSetService
│   ├── VisibilityService
│   ├── MarkerService
│   ├── ScoringService
│   ├── RewardService
│   ├── ParticipantService
│   └── ScalingService
│
├── Integrations
│   ├── ClanAdapter
│   ├── BotAdapter_RaidlandsRoamBot
│   ├── RewardAdapter
│   ├── ZoneAdapter_Internal
│   ├── ZoneAdapter_ZoneManagerOptional
│   ├── DiscordAdapter
│   ├── WebsiteApiAdapter
│   ├── TeleportAdapter
│   ├── KitsAdapter
│   └── BackpacksAdapter
│
├── Admin UI
│   ├── UiRouter
│   ├── UiDashboard
│   ├── UiActiveEvents
│   ├── UiEventEditor
│   ├── UiRuleSetEditor
│   ├── UiRewardEditor
│   ├── UiScheduleEditor
│   ├── UiLocationEditor
│   ├── UiImportExport
│   └── UiAuditLog
│
└── External Provider API
    ├── ProviderRegistration
    ├── EventTypeDescriptor
    ├── ProviderStartContext
    ├── ProviderStopContext
    └── ProviderValidationResult
```

---

## 6. Core plugin responsibilities

RaidlandsEvents should own the following systems from day one.

### 6.1 Event definitions

An event definition is the saved template/config for an event.

Examples:

- `sulfur_storm_small`
- `koth_clan_warzone`
- `raidme_medium_public`
- `warlord_compound_elite`
- `hqm_meteor_dynamic`

Each definition references:

- event type/provider
- display name
- enabled state
- triggers
- location rules
- ruleset
- visibility
- scoring
- rewards
- scaling
- cleanup behavior
- provider-specific config

### 6.2 Event instances

An event instance is one active runtime copy of a definition.

Example:

```text
Definition: sulfur_storm_small
Instance: sulfur_storm_small_2026-07-06_14-22-11_8f92
```

Each instance tracks:

- instance ID
- definition ID
- state
- start time
- end time
- selected location
- participants
- score ledger
- spawned entities
- spawned bot groups
- map markers
- rule zones
- objective state
- reward status
- cleanup status

### 6.3 Event lifecycle

Every event should follow the same lifecycle:

```text
Draft
  ↓
Validated
  ↓
Queued
  ↓
Starting
  ↓
Active
  ↓
Completing
  ↓
Rewarding
  ↓
CleaningUp
  ↓
Completed
```

Failure states:

```text
StartFailed
RuntimeFailed
RewardFailed
CleanupPartial
CancelledByAdmin
CancelledByUnload
```

The lifecycle manager should be strict: no event should jump from `Active` directly to `Completed` without cleanup and result finalization.

---

## 7. Data storage layout

Recommended data layout:

```text
oxide/
  plugins/
    RaidlandsEvents.cs
    RaidlandsEvents_AirdropSwarm.cs          # future optional provider
    RaidlandsEvents_KingOfTheHill.cs         # future optional provider
    RaidlandsEvents_RaidBaseProvider.cs      # future optional provider

  config/
    RaidlandsEvents.json

  data/
    RaidlandsEvents/
      event_definitions/
        sulfur_storm_small.json
        koth_clan_warzone.json
        raidme_medium_public.json

      rulesets/
        none.json
        default_warzone.json
        pure_fight.json
        raid_base_rules.json

      reward_profiles/
        small_rp_pool.json
        elite_1000x_raid_rewards.json

      schedules/
        default_schedule.json
        wipe_weekend_schedule.json

      locations/
        saved_locations.json
        generated_location_cache.json
        monument_overrides.json

      imports/
        incoming_event_pack.json

      exports/
        sulfur_storm_small.export.json

      history/
        2026-07-06.json

      active_instances.json
      audit_log.json
      pending_rewards.json
      provider_registry.json
```

### 7.1 Data persistence requirements

- Save active event state regularly.
- Save immediately on event start/end.
- Save before and after reward payout.
- Save cleanup ownership data for every spawned entity.
- On plugin unload/server restart, attempt safe cleanup.
- On plugin load, detect orphaned active instances and either restore or cleanup.

---

## 8. Event definition schema

This is the proposed top-level event definition format.

```json
{
  "SchemaVersion": 1,
  "Id": "koth_clan_warzone",
  "DisplayName": "Clan Warzone KOTH",
  "Description": "A public clan-based King of the Hill event with ranked clan rewards.",
  "Enabled": true,
  "EventType": "KingOfTheHill",
  "Provider": "RaidlandsEvents_KingOfTheHill",
  "Category": "PvPControl",
  "Tags": ["pvp", "clan", "koth", "public"],

  "Triggers": {},
  "Location": {},
  "Rules": {},
  "Visibility": {},
  "Scaling": {},
  "Scoring": {},
  "Rewards": {},
  "ProviderConfig": {},
  "Cleanup": {},
  "AdminNotes": ""
}
```

The manager should validate every definition before it can run.

---

## 9. Trigger system

Each event can enable one or more triggers.

### 9.1 Scheduled trigger

Scheduled events should always be allowed to run unless disabled by config or admin.

```json
{
  "Scheduled": {
    "Enabled": true,
    "BypassOnlineMinimum": true,
    "ScheduleMode": "Interval",
    "IntervalMinutes": 90,
    "JitterMinutes": 10,
    "AllowedHoursServerTime": [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23],
    "MaxRunsPerWipe": 0,
    "MaxRunsPerDay": 0,
    "CooldownMinutesAfterRun": 60
  }
}
```

### 9.2 Automatic/dynamic trigger

Dynamic events should consider online player count.

```json
{
  "Automatic": {
    "Enabled": true,
    "MinOnlinePlayers": 15,
    "MaxOnlinePlayers": 999,
    "WeightedPool": "default_dynamic_pool",
    "Weight": 20,
    "CooldownMinutesAfterRun": 45,
    "PreventIfSimilarEventActive": true,
    "PreventIfMajorEventActive": true
  }
}
```

### 9.3 Admin trigger

```json
{
  "Admin": {
    "Enabled": true,
    "Permission": "raidlandsevents.admin.start",
    "AllowOverrideLocation": true,
    "AllowOverrideRuleset": true,
    "AllowOverrideRewards": true,
    "AllowDryRun": true
  }
}
```

### 9.4 Player purchase trigger

Player-purchased events are public.

```json
{
  "Purchase": {
    "Enabled": true,
    "PublicEvent": true,
    "Permission": "raidlandsevents.player.purchase",
    "CooldownMinutesPerPlayer": 120,
    "CooldownMinutesGlobal": 30,
    "AllowClanPurchase": true,
    "Costs": [
      {
        "Type": "RP",
        "Amount": 5000
      },
      {
        "Type": "Item",
        "ShortName": "scrap",
        "Amount": 25000
      },
      {
        "Type": "CommandCurrency",
        "CurrencyKey": "event_token",
        "Amount": 3
      }
    ],
    "RefundOnStartFailure": true,
    "AnnouncePurchaser": true,
    "PurchaserDoesNotOwnEvent": true
  }
}
```

Important rule: `PurchaserDoesNotOwnEvent = true` means buying the event starts it, but does not reserve the reward.

---

## 10. Location system

The location system should support three location families.

### 10.1 Monument locations

Used for monument takeover events.

```json
{
  "Mode": "Monument",
  "AllowedMonuments": ["Launch Site", "Airfield", "Power Plant", "Dome"],
  "Radius": 140,
  "UseMonumentCenter": true,
  "AllowIfPlayersNearby": true,
  "AllowIfBasesNearby": true
}
```

### 10.2 Random open-world locations

Used for KOTH, sulfur storm, meteor, raid base, and temporary battlefield objectives.

```json
{
  "Mode": "RandomOpenWorld",
  "BiomePreference": "Any",
  "MinDistanceFromSafeZone": 300,
  "MinDistanceFromMonument": 150,
  "MinDistanceFromPlayerBase": 120,
  "MinDistanceFromMapEdge": 200,
  "RequireFlatArea": true,
  "RequiredFlatRadius": 45,
  "MaxSlopeDegrees": 18,
  "AllowRoadside": true,
  "AllowWater": false,
  "AllowIceberg": false,
  "MaxAttempts": 80
}
```

### 10.3 Saved/custom locations

Used for hand-curated arenas and repeatable battlefield points.

```json
{
  "Mode": "SavedLocationPool",
  "PoolId": "desert_open_warzones",
  "PickMode": "Random",
  "AllowAdminOverride": true
}
```

Saved location record:

```json
{
  "Id": "desert_warzone_01",
  "DisplayName": "Desert Warzone 01",
  "Position": {
    "X": 1234.2,
    "Y": 18.0,
    "Z": -900.5
  },
  "Radius": 125,
  "AllowedEventTypes": ["KingOfTheHill", "SulfurStorm", "AirdropSwarm"],
  "Tags": ["open", "flat", "desert", "good_pvp"]
}
```

### 10.4 Location validation rules

Before event start, validate:

- terrain is reachable
- not underwater unless allowed
- enough clearance for spawned objects
- not inside safe zone unless explicitly allowed
- not too close to protected bases unless event allows it
- no active event already overlapping unless overlap is allowed
- correct biome/road/ocean/monument requirements
- provider-specific location constraints

---

## 11. Rule-set system

Rulesets must be reusable, named, and detachable. Admins should be able to apply `None`, `DefaultWarzone`, `PureFight`, `RaidBaseRules`, or any custom ruleset to any event where it validates.

### 11.1 Ruleset example

```json
{
  "Id": "default_warzone",
  "DisplayName": "Default Warzone",
  "Enabled": true,
  "RadiusMode": "UseEventRadius",

  "BuildRules": {
    "NoBuild": true,
    "NoDeploy": true,
    "AllowBarricades": true,
    "AllowHighExternalWalls": false,
    "AllowLadders": true,
    "AllowSleepingBags": false,
    "AllowTurrets": false,
    "AllowSamSites": false,
    "AllowExternalTC": false
  },

  "CombatRules": {
    "ProtectNearbyPlayerBases": true,
    "AllowDamageToEventEntities": true,
    "AllowDamageToPlayerEntitiesInsideZone": false,
    "AllowDamageToPlayers": true,
    "PreventSleeperDamage": true,
    "PreventFriendlyScoreAbuse": true
  },

  "MovementRules": {
    "NoTeleportIn": true,
    "NoTeleportOut": false,
    "NoHomeSet": true,
    "NoRespawnInsideZone": true,
    "EjectPlayersOnStart": false,
    "EjectPlayersOnEnd": false,
    "AllowVehicles": true,
    "AllowMinicopters": true,
    "AllowBoats": true
  },

  "InventoryRules": {
    "NoKits": false,
    "NoBackpacks": true,
    "DropBackpackOnDeath": true,
    "BlockLootingEventCrateUntilComplete": true
  },

  "ScoringRules": {
    "RequireInsideZoneForScore": true,
    "RequireEventActiveForScore": true,
    "IgnoreSelfClanKills": true,
    "IgnoreAlliedClanKills": true,
    "IgnoreSleeperKills": true,
    "MinimumVictimGearScoreForKillPoints": 0
  }
}
```

### 11.2 Built-in default rulesets

| Ruleset | Purpose |
|---|---|
| `none` | No special rules. Useful for chaos/testing. |
| `default_warzone` | Balanced PvP zone. Blocks build/deploy abuse but allows fighting. |
| `pure_fight` | No building, no kits, no backpacks, no vehicles. Good for competitive KOTH. |
| `raid_base_rules` | Allows raiding event entities while protecting nearby unrelated player bases. |
| `monument_takeover` | Minimal build/deploy controls around existing monuments. |
| `moving_event_radius` | Moving ruleset attached to convoy/vehicle/event entity. |
| `admin_test` | Small radius, visible debug marker, safe cleanup. |

### 11.3 Implementation approach

Use an internal rule engine first. Optional Zone Manager integration can be added as a bridge.

Internal rule engine should evaluate:

- `CanBuild`
- deploy/block checks
- item placement checks
- entity damage checks
- teleport plugin hooks if available
- kit plugin hooks if available
- backpack plugin hooks if available
- respawn/sleeping bag placement hooks
- turret/SAM placement checks
- event crate loot checks

Important: only subscribe to expensive hooks when at least one active ruleset needs them.

---

## 12. Visibility and announcement system

Every event should define its own visibility behavior.

```json
{
  "Visibility": {
    "ChatAnnouncement": true,
    "DiscordAnnouncement": true,
    "WebsiteAnnouncement": true,
    "MapMarker": true,
    "MarkerType": "GenericRadius",
    "MarkerName": "Clan Warzone KOTH",
    "MarkerColor": "#ff3333",
    "MarkerRadius": 125,
    "MarkerDelaySeconds": 0,
    "ShowExactGrid": true,
    "ShowApproximateGrid": false,
    "ApproximateGridFuzzRadius": 2,
    "BroadcastStart": true,
    "BroadcastHalfway": true,
    "BroadcastFiveMinuteWarning": true,
    "BroadcastCompletion": true,
    "BroadcastWinner": true,
    "HiddenUntilDiscovered": false,
    "DiscoveryRadius": 80
  }
}
```

### 12.1 Announcement examples

Start:

```text
[Raidlands Event] Clan Warzone KOTH has started at G12. Hold the zone to claim the reward. PvP is active.
```

Purchased event:

```text
[Raidlands Event] Carl has purchased a public Medium RaidMe event. The base is surfacing near H8. Anyone can counter.
```

Approximate event:

```text
[Raidlands Event] HQM Meteor activity detected somewhere near the G/H gridline. Exact impact marker in 60 seconds.
```

Completion:

```text
[Raidlands Event] Clan Warzone KOTH ended. 1st: ABC, 2nd: RAID, 3rd: NOOB.
```

---

## 13. Scoring system

The scoring system should accept many contribution types and let each event decide which ones matter.

### 13.1 Score event structure

```json
{
  "EventInstanceId": "koth_clan_warzone_8f92",
  "Metric": "PlayerKill",
  "ActorUserId": "7656119...",
  "ActorClanId": "ABC",
  "TargetUserId": "7656119...",
  "TargetClanId": "RAID",
  "EntityId": 123456,
  "Position": { "X": 100.0, "Y": 20.0, "Z": -300.0 },
  "RawAmount": 1.0,
  "ScoreValue": 100.0,
  "TimestampUtc": "2026-07-06T19:05:00Z",
  "Metadata": {
    "WeaponShortName": "rifle.ak",
    "Distance": 83.4,
    "InsideZone": true
  }
}
```

### 13.2 Supported contribution metrics

| Metric | Example use |
|---|---|
| `PlayerKill` | KOTH, warzone, bounty, convoy. |
| `PlayerAssist` | Optional assist scoring. |
| `DamageToPlayer` | Contribution-based PvP rewards. |
| `NpcKill` | Boss/guard events. |
| `DamageToNpc` | Boss contribution. |
| `BossKill` | Boss completion bonus. |
| `DamageToBoss` | Boss contribution weighting. |
| `DamageToEventEntity` | Convoy truck, raid base doors/walls, meteor node. |
| `DestroyEventEntity` | Vehicle destroyed, generator destroyed, shield disabled. |
| `TCDestroyed` | Raid base objective. |
| `CrateUnlocked` | Hack/unlock contribution. |
| `CrateLooted` | Objective completion. |
| `ZoneTime` | KOTH hold score. |
| `ObjectiveTick` | Control point score. |
| `ExplosiveDamage` | Raid event scoring. |
| `HealingAlly` | Optional support scoring. |
| `ReviveAlly` | Optional support scoring. |
| `Custom` | Event provider-specific points. |

### 13.3 Scoring config example

```json
{
  "Scoring": {
    "ScoreScope": "Clan",
    "AllowSoloIfNoClan": true,
    "RankBy": "TotalScore",
    "TieBreakerOrder": ["ObjectiveScore", "PlayerKills", "DamageDealt", "FirstObjectiveTime"],
    "MinimumScoreToQualify": 250,
    "MinimumTimeInEventSeconds": 60,

    "Metrics": {
      "PlayerKill": {
        "Enabled": true,
        "Points": 100,
        "RequireInsideZone": true,
        "IgnoreSameClan": true,
        "IgnoreAlliedClan": true,
        "RepeatVictimDiminishWindowSeconds": 180,
        "RepeatVictimMultiplier": 0.25
      },
      "DamageToPlayer": {
        "Enabled": true,
        "PointsPer100Damage": 10,
        "MaxPointsPerVictimPerMinute": 50
      },
      "ZoneTime": {
        "Enabled": true,
        "PointsPerSecond": 2,
        "RequireAlive": true,
        "MaxPlayersScoringPerClan": 6
      },
      "NpcKill": {
        "Enabled": false,
        "Points": 25
      },
      "CrateUnlocked": {
        "Enabled": true,
        "Points": 300
      }
    }
  }
}
```

### 13.4 Solo vs clan aggregation

`ScoreScope` options:

```text
Player
Clan
Team
HybridPlayerWithinClan
```

Recommended behavior:

- `Player`: rank individual players only.
- `Clan`: aggregate all clan member points into clan score.
- `Team`: use Rust team or Clan Team only if explicitly configured.
- `HybridPlayerWithinClan`: rank clans first, then rank contributors inside the winning clan for weighted distribution.

For Raidlands, `Clan` and `HybridPlayerWithinClan` should be the main modes.

### 13.5 Anti-abuse filters

Add these as configurable scoring filters:

- no score for killing same clan
- no score for killing allied clan
- no score for killing sleepers
- no score for killing naked players below optional gear threshold
- reduced score for repeated same-victim farming
- no score outside event zone unless event says otherwise
- no score after objective has ended
- no score for damage to unrelated player bases
- no reward if player/clan did not meet minimum participation
- optional IP/hardware/shared household checks later if abuse becomes a problem

---

## 14. Reward system

The reward system should support both fixed rewards and percentage-split pools.

### 14.1 Reward recipient modes

```text
Player
ClanEvenSplit
ClanContributionSplit
ClanLeaderClaim
Hybrid
```

Recommended defaults:

- Solo events: `Player`.
- Clan KOTH: `ClanContributionSplit`.
- Casual clan events: `ClanEvenSplit`.
- Purchased raid events: reward based on actual event score, not purchaser identity.

### 14.2 Reward item types

| Type | Description |
|---|---|
| `RP` | Adds RP using configured economy adapter. |
| `Item` | Gives Rust item by shortname/amount/skin. |
| `Command` | Runs console command with placeholders. |
| `EventToken` | Internal event token ledger or external command currency. |
| `KitCooldownReset` | Optional integration. |
| `PermissionTemporary` | Temporary permission reward. |
| `WebhookOnly` | Website/Discord badge event without item payout. |

### 14.3 Fixed placement reward example

```json
{
  "Rewards": {
    "RewardMode": "FixedPlacements",
    "RecipientScope": "Clan",
    "ClanDistribution": "ContributionWeighted",
    "Placements": [
      {
        "Place": 1,
        "Rewards": [
          { "Type": "RP", "Amount": 20000 },
          { "Type": "Item", "ShortName": "explosive.timed", "Amount": 20 },
          { "Type": "Item", "ShortName": "rocket.launcher", "Amount": 2 }
        ]
      },
      {
        "Place": 2,
        "Rewards": [
          { "Type": "RP", "Amount": 10000 },
          { "Type": "Item", "ShortName": "explosive.timed", "Amount": 10 }
        ]
      },
      {
        "Place": 3,
        "Rewards": [
          { "Type": "RP", "Amount": 5000 }
        ]
      }
    ]
  }
}
```

### 14.4 Percentage pool reward example

```json
{
  "Rewards": {
    "RewardMode": "PercentagePool",
    "RecipientScope": "Clan",
    "ClanDistribution": "ContributionWeighted",
    "Pool": [
      { "Type": "RP", "Amount": 50000 },
      { "Type": "Item", "ShortName": "sulfur.ore", "Amount": 2000000 },
      { "Type": "Item", "ShortName": "metal.refined", "Amount": 250000 }
    ],
    "Placements": [
      { "Place": 1, "Percent": 60 },
      { "Place": 2, "Percent": 25 },
      { "Place": 3, "Percent": 15 }
    ],
    "RoundItemAmounts": true,
    "MinimumQualifiedScore": 250
  }
}
```

### 14.5 Clan distribution modes

| Mode | Behavior |
|---|---|
| `Even` | Every qualifying member of the winning clan receives equal share. |
| `ContributionWeighted` | Members receive payout based on their share of clan score. |
| `TopContributorsOnly` | Only top X contributors in the clan receive payout. |
| `LeaderClaim` | Clan leader receives claimable reward. Use carefully. |
| `ManualCommand` | Runs configured commands against clan/player placeholders. |

### 14.6 RP economy baseline

Players receive **100 RP every 10 minutes**, or **600 RP/hour**, before event rewards.

Suggested event reward bands:

| Event tier | RP-equivalent target |
|---|---:|
| Micro | 100-500 RP |
| Small | 1,000-3,000 RP |
| Medium | 4,000-10,000 RP |
| Large | 12,000-30,000 RP |
| Elite | 40,000-100,000 RP pool |
| Wipe finale | Intentionally ridiculous |

For a 1000x server, rewards should feel large and PvP-relevant, but the highest-value payouts should be tied to events that generate real conflict.

---

## 15. Online scaling system

Dynamic events should scale by online player count.

```json
{
  "Scaling": {
    "Enabled": true,
    "UseOnlinePlayerCount": true,
    "ScheduledEventsBypassOnlineGate": true,
    "Tiers": [
      {
        "MinOnline": 0,
        "MaxOnline": 9,
        "RewardMultiplier": 0.5,
        "NpcMultiplier": 0.5,
        "ObjectiveDurationMultiplier": 0.8,
        "RadiusMultiplier": 0.8
      },
      {
        "MinOnline": 10,
        "MaxOnline": 29,
        "RewardMultiplier": 1.0,
        "NpcMultiplier": 1.0,
        "ObjectiveDurationMultiplier": 1.0,
        "RadiusMultiplier": 1.0
      },
      {
        "MinOnline": 30,
        "MaxOnline": 59,
        "RewardMultiplier": 1.5,
        "NpcMultiplier": 1.5,
        "ObjectiveDurationMultiplier": 1.1,
        "RadiusMultiplier": 1.15
      },
      {
        "MinOnline": 60,
        "MaxOnline": 999,
        "RewardMultiplier": 2.0,
        "NpcMultiplier": 2.0,
        "ObjectiveDurationMultiplier": 1.2,
        "RadiusMultiplier": 1.25
      }
    ]
  }
}
```

Scaling should affect:

- reward pool
- event radius
- number of objective crates/nodes
- bot count for bot events
- boss health multiplier
- event duration
- score requirements
- max concurrent events

---

## 16. RaidlandsRoamBot integration

### 16.1 Design rule

RaidlandsEvents requests bots. RaidlandsRoamBot creates and controls bots.

RaidlandsEvents should never duplicate bot AI or movement logic.

### 16.2 Bot spawn request

```json
{
  "EventInstanceId": "warlord_compound_elite_8f92",
  "GroupKey": "warlord_guards",
  "Profile": "HeavyScientist",
  "Difficulty": "Elite",
  "Count": 8,
  "HealthMultiplier": 3.0,
  "WeaponProfile": "AK_Burst",
  "ArmorProfile": "HeavyPlate",
  "BehaviorProfile": "AggressiveCoverFighter",
  "TeamKey": "event_warlord_compound_8f92",
  "SpawnMode": "SpawnPoints",
  "SpawnPoints": [
    { "X": 100.0, "Y": 20.0, "Z": -300.0 },
    { "X": 105.0, "Y": 20.0, "Z": -305.0 }
  ],
  "Leash": {
    "Center": { "X": 100.0, "Y": 20.0, "Z": -300.0 },
    "Radius": 120
  },
  "Roam": {
    "Mode": "WithinLeash",
    "PreferCover": true,
    "CanChaseOutsideLeashSeconds": 12
  },
  "LootMode": "None",
  "ReturnEntityIds": true
}
```

### 16.3 Bot spawn response

```json
{
  "Success": true,
  "GroupId": "botgroup_warlord_guards_8f92",
  "EntityIds": [123456, 123457, 123458],
  "Warnings": []
}
```

### 16.4 Required bot adapter calls

RaidlandsEvents should expect RaidlandsRoamBot to support calls like these, even if final names change:

```text
REBOT_SpawnGroup(requestJsonOrDictionary) -> response
REBOT_SpawnSingle(requestJsonOrDictionary) -> response
REBOT_DespawnGroup(groupId, reason)
REBOT_DespawnForEvent(eventInstanceId, reason)
REBOT_GetBotOwner(entityId) -> eventInstanceId/groupId/profile
REBOT_SetGroupObjective(groupId, objectiveJsonOrDictionary)
REBOT_SetGroupLeash(groupId, center, radius)
REBOT_IsBot(entityId) -> bool
```

Implementation note as of `RaidlandsRoamBots` v0.3.54: these `REBOT_*` hook names are now the canonical RoamBots lending API. Requests may be JSON strings or Oxide-friendly dictionaries; responses are plain dictionaries with `Success`, `GroupId`, `EntityIds`, `Warnings`, and `Errors`. `REBOT_GetBotOwner` returns `OwnerPlugin`, `OwnerKind`, `EventInstanceId`, `GroupId`, `GroupKey`, `Profile`, `Difficulty`, and `EntityId` for live or recently dead tracked RoamBots.

### 16.5 Event manager responsibilities for bots

RaidlandsEvents should track:

- bot group IDs
- bot entity IDs
- which event owns each group
- bot deaths for scoring
- boss death completion
- cleanup requests
- failed bot spawn responses

RaidlandsEvents should not track:

- pathfinding
- combat tactics
- cover selection
- weapon burst logic
- medical behavior
- flank logic
- formation logic

---

## 17. External event provider system

The MVP should allow event types to be imported later.

There are two levels of event extensibility.

### 17.1 Definition-only event types

Some events can be driven almost entirely by data:

- simple KOTH
- airdrop swarm
- crate storm
- resource node burst
- basic zone control

The core plugin can include a generic provider for these.

### 17.2 External provider plugins

Complex events can be separate provider plugins:

```text
RaidlandsEvents_AirdropSwarm.cs
RaidlandsEvents_KingOfTheHill.cs
RaidlandsEvents_RaidBaseProvider.cs
RaidlandsEvents_ConvoyProvider.cs
RaidlandsEvents_MeteorProvider.cs
RaidlandsEvents_WarlordCompound.cs
```

These providers register themselves with the manager.

### 17.3 Provider registration contract

Avoid requiring shared C# classes across separate plugins. Use dictionaries/JSON-safe objects for provider communication.

Provider calls manager on load:

```csharp
RaidlandsEvents?.CallHook("API_RegisterEventProvider", new Dictionary<string, object>
{
    ["ProviderPlugin"] = Name,
    ["ProviderVersion"] = Version.ToString(),
    ["EventTypes"] = new List<object>
    {
        new Dictionary<string, object>
        {
            ["EventType"] = "KingOfTheHill",
            ["DisplayName"] = "King of the Hill",
            ["SupportsLocations"] = new [] { "RandomOpenWorld", "SavedLocationPool", "Monument" },
            ["SupportsBots"] = false,
            ["ProviderConfigSchemaVersion"] = 1
        }
    }
});
```

Manager starts provider event:

```text
Provider.CallHook("API_StartManagedEvent", startContextDictionary)
```

Provider reports spawned objects/objectives back:

```text
RaidlandsEvents.CallHook("API_RegisterSpawnedEntity", eventInstanceId, entityId, ownershipType)
RaidlandsEvents.CallHook("API_AddScore", scoreEventDictionary)
RaidlandsEvents.CallHook("API_CompleteObjective", eventInstanceId, objectiveKey, metadata)
RaidlandsEvents.CallHook("API_RequestEventComplete", eventInstanceId, reason)
```

Manager stops provider event:

```text
Provider.CallHook("API_StopManagedEvent", stopContextDictionary)
```

### 17.4 Provider validation

Before an event definition can be enabled, the provider should validate its custom config.

```text
Provider.CallHook("API_ValidateEventDefinition", eventDefinitionDictionary)
```

Validation should return:

```json
{
  "Valid": false,
  "Errors": ["ProviderConfig.DurationSeconds must be greater than 0"],
  "Warnings": ["No marker color set; default will be used"]
}
```

---

## 18. Import/export system

The admin UI must support import/export and full event setup.

### 18.1 Practical import workflow

In-game UI is not ideal for large file uploads, so support both:

1. **File import:** admin places JSON in `oxide/data/RaidlandsEvents/imports/`, then imports through UI.
2. **Paste import:** admin pastes small JSON into a CUI input field or console command.

### 18.2 Export workflow

Admin selects an event/ruleset/reward/schedule/profile in UI and chooses export.

The plugin writes:

```text
oxide/data/RaidlandsEvents/exports/<id>.export.json
```

The UI should show:

```text
Export complete: data/RaidlandsEvents/exports/koth_clan_warzone.export.json
```

### 18.3 Import package format

```json
{
  "PackageSchemaVersion": 1,
  "PackageName": "Raidlands Starter PvP Pack",
  "PackageAuthor": "Raidlands",
  "PackageVersion": "1.0.0",
  "EventDefinitions": [],
  "RuleSets": [],
  "RewardProfiles": [],
  "Schedules": [],
  "Locations": [],
  "ProviderRequirements": [
    {
      "Provider": "RaidlandsEvents_KingOfTheHill",
      "MinVersion": "1.0.0",
      "Required": true
    }
  ]
}
```

### 18.4 Import validation

Import should never blindly overwrite live configs.

Import flow:

1. Read package.
2. Validate schema version.
3. Validate providers exist or warn.
4. Validate item shortnames.
5. Validate reward adapters.
6. Validate ruleset references.
7. Validate location modes.
8. Show conflicts.
9. Admin chooses:
   - skip duplicates
   - overwrite
   - import as copy
   - merge where safe
10. Save imported objects.
11. Write audit log.

---

## 19. Admin UI requirements

The admin UI is a major part of the MVP.

### 19.1 Open commands

```text
/revents
/eventsadmin
/raidevents
```

Console:

```text
revents.open <playerNameOrId>
```

### 19.2 Permissions

```text
raidlandsevents.admin
raidlandsevents.admin.view
raidlandsevents.admin.start
raidlandsevents.admin.stop
raidlandsevents.admin.edit
raidlandsevents.admin.delete
raidlandsevents.admin.import
raidlandsevents.admin.export
raidlandsevents.admin.reload
raidlandsevents.player.view
raidlandsevents.player.purchase
```

### 19.3 UI sections

#### Dashboard

Shows:

- plugin status
- active events
- queued events
- next scheduled event
- provider status
- adapter status
- recent errors/warnings
- buttons for start/import/export/settings

#### Active Events

Shows each active instance:

- name
- instance ID
- state
- location/grid
- time remaining
- participants
- top scores
- spawned entity count
- bot group count
- buttons:
  - view
  - teleport admin to event
  - force complete
  - cancel
  - cleanup
  - debug dump

#### Event Templates

Shows saved event definitions:

- enabled/disabled
- event type
- provider
- triggers
- ruleset
- reward profile
- last run
- validation status
- buttons:
  - edit
  - clone
  - start
  - export
  - disable
  - delete

#### Event Editor Wizard

Tabs/steps:

1. Basic info.
2. Provider/type.
3. Trigger setup.
4. Location setup.
5. Ruleset selection.
6. Visibility/announcements.
7. Scoring config.
8. Rewards.
9. Scaling.
10. Provider-specific config.
11. Validation.
12. Save/start/export.

#### Ruleset Editor

- list rulesets
- clone existing
- edit toggles
- preview affected systems
- show which event definitions use the ruleset

#### Reward Editor

- fixed placement rewards
- percentage pool rewards
- clan distribution options
- RP/item/command rewards
- test payout to admin
- dry-run reward preview based on fake leaderboard

#### Schedule Editor

- schedule pools
- weighted event pools
- interval settings
- wipe phase settings
- dynamic event gating
- max concurrent event rules

#### Location Editor

- save current admin location
- edit radius
- tag locations
- assign allowed event types
- validate terrain
- preview marker
- delete saved location

#### Import/Export

- list files in `imports/`
- validate package
- show conflicts
- import selected objects
- export event/ruleset/reward/schedule/location

#### Audit/History

- recent event starts/stops
- admin edits
- imports
- exports
- reward payouts
- cleanup failures
- provider errors

### 19.4 UI implementation notes

- Use CUI pages instead of one giant UI.
- Use a simple routing command format:

```text
revents.ui dashboard
revents.ui templates page=2
revents.ui edit eventId=koth_clan_warzone tab=rewards
```

- Destroy old UI containers before drawing new ones.
- Use unique UI names with plugin prefix:

```text
RaidlandsEvents.UI.Root
RaidlandsEvents.UI.Modal
RaidlandsEvents.UI.Toast
```

- Avoid redrawing the entire UI every tick.
- Use manual refresh buttons and short refresh timers only on active event pages.

---

## 20. Player-facing UI/commands

MVP can keep player UI minimal.

### 20.1 Player commands

```text
/event
/events
/event buy
/event buy <eventId>
/event active
/event leaderboard
```

### 20.2 Player UI contents

- active events
- current location/marker info
- time remaining
- reward summary
- top leaderboard
- purchasable public events
- purchase costs/cooldowns

Do not overbuild player UI until admin/event management is stable.

---

## 21. Website/API integration plan

Website integration should be adapter-based.

### 21.1 Outbound events

RaidlandsEvents should push:

```text
event.started
event.updated
event.completed
event.cancelled
event.rewarded
event.failed
leaderboard.updated
boss.killed
clan.event_won
```

### 21.2 Payload example

```json
{
  "ServerId": "raidlands-main",
  "EventInstanceId": "koth_clan_warzone_8f92",
  "DefinitionId": "koth_clan_warzone",
  "DisplayName": "Clan Warzone KOTH",
  "State": "Completed",
  "StartedAtUtc": "2026-07-06T19:00:00Z",
  "EndedAtUtc": "2026-07-06T19:18:22Z",
  "Location": {
    "Grid": "G12",
    "X": 100.0,
    "Y": 20.0,
    "Z": -300.0,
    "Radius": 125
  },
  "Leaderboard": [
    {
      "Rank": 1,
      "Scope": "Clan",
      "Id": "ABC",
      "DisplayName": "ABC",
      "Score": 5520
    }
  ],
  "Rewards": [
    {
      "Rank": 1,
      "Type": "RP",
      "Amount": 30000
    }
  ]
}
```

### 21.3 API reliability

- Use shared secret/API key.
- Queue failed requests.
- Retry with backoff.
- Do not block event completion on website failure.
- Keep local history authoritative if website is offline.

---

## 22. Commands

### 22.1 Admin commands

```text
/revents
/revents start <eventId>
/revents start <eventId> here
/revents start <eventId> grid <grid>
/revents stop <instanceId>
/revents complete <instanceId>
/revents cleanup <instanceId>
/revents validate <eventId>
/revents reload
/revents import <filename>
/revents export event <eventId>
/revents export pack <packName>
/revents save_location <id> <radius>
/revents debug instance <instanceId>
/revents debug adapters
/revents debug providers
```

### 22.2 Console commands

```text
revents.start <eventId>
revents.stop <instanceId>
revents.complete <instanceId>
revents.cleanup <instanceId>
revents.validate <eventId>
revents.reload
revents.import <filename>
revents.export.event <eventId>
revents.status
revents.providers
revents.adapters
```

### 22.3 Player commands

```text
/event
/event active
/event buy
/event buy <eventId>
/event top
```

---

## 23. Core hook usage plan

Use hooks only when needed by active events/rules/scoring.

Likely hook categories:

| Purpose | Hook examples / category |
|---|---|
| Player kills/deaths | player/entity death hooks. |
| Damage contribution | entity damage hooks. |
| Building/deploy rules | build/deploy/structure hooks. |
| Loot/objective rules | loot/use/crate hooks. |
| Respawn/bag rules | respawn/sleeping bag hooks. |
| Resource events | resource gather hooks or spawned entity tracking. |
| Teams/clans | clan/team plugin hooks or adapter calls. |
| Plugin integrations | custom plugin hooks. |

Performance rule:

```text
If no active event needs a hook, unsubscribe from it.
```

Example:

- No active scoring events: no damage tracking hook.
- No active rulesets with build restrictions: no build hook.
- No active events with NPCs: no bot death tracking beyond generic death if already needed.

---

## 24. Cleanup and reliability

Cleanup is critical because events will spawn entities, markers, bots, zones, crates, and temporary objects.

### 24.1 Ownership tracking

Every spawned object should be registered against an event instance.

```json
{
  "EventInstanceId": "sulfur_storm_small_8f92",
  "OwnedEntities": [
    {
      "EntityId": 123456,
      "Prefab": "assets/prefabs/misc/supply drop/supply_drop.prefab",
      "OwnershipType": "EventLoot",
      "SpawnedAtUtc": "2026-07-06T19:00:00Z"
    }
  ],
  "BotGroups": ["botgroup_warlord_guards_8f92"],
  "Markers": ["marker_sulfur_storm_8f92"],
  "Zones": ["zone_sulfur_storm_8f92"]
}
```

### 24.2 Cleanup moments

Cleanup should run:

- when event completes
- when event is cancelled
- when provider fails
- on plugin unload
- on server shutdown if possible
- on plugin load for orphaned previous instances
- via manual admin cleanup command

### 24.3 Cleanup policy

Configurable per event:

```json
{
  "Cleanup": {
    "DespawnOwnedEntities": true,
    "DespawnUnlootedCrates": true,
    "DespawnBots": true,
    "RemoveMarkers": true,
    "RemoveZones": true,
    "KillTemporaryBuilds": true,
    "DelaySecondsAfterCompletion": 300,
    "AllowPlayersToLootAfterCompletion": true,
    "ForceCleanupAfterMinutes": 20
  }
}
```

---

## 25. Validation system

Every event definition should show a validation result in UI.

### 25.1 Validation levels

```text
Valid
Warning
Invalid
UnavailableDependency
ProviderMissing
AdapterMissing
LocationInvalid
RewardInvalid
RulesetInvalid
```

### 25.2 Validation checks

- ID is unique.
- provider exists.
- provider supports event type.
- trigger settings valid.
- schedule settings valid.
- purchase cost adapter available.
- location mode supported by provider.
- ruleset exists.
- scoring metrics valid.
- reward profile valid.
- item shortnames valid.
- clan mode requires Clans adapter.
- bot config requires RaidlandsRoamBot adapter.
- website settings valid if enabled.
- no impossible combinations.

Example impossible combinations:

```text
ScoreScope = Clan but Clans plugin unavailable.
Provider = Convoy but Location.Mode = SavedLocationPool without route.
RewardMode = PercentagePool but Pool is empty.
RuleSet = PureFight but Provider requires DeployAllowed.
Purchase.Enabled = true but no cost configured.
```

---

## 26. Global config

`config/RaidlandsEvents.json` should hold global settings only.

```json
{
  "ConfigVersion": 1,
  "ServerId": "raidlands-main",
  "DefaultLanguage": "en",
  "Debug": false,

  "General": {
    "MaxConcurrentEvents": 3,
    "MaxConcurrentMajorEvents": 1,
    "SaveIntervalSeconds": 60,
    "HistoryRetentionDays": 30,
    "AutoCleanupOnLoad": true,
    "AutoDisableInvalidImportedEvents": true
  },

  "Paths": {
    "EventDefinitions": "RaidlandsEvents/event_definitions",
    "RuleSets": "RaidlandsEvents/rulesets",
    "RewardProfiles": "RaidlandsEvents/reward_profiles",
    "Imports": "RaidlandsEvents/imports",
    "Exports": "RaidlandsEvents/exports"
  },

  "Adapters": {
    "Clans": {
      "Enabled": true,
      "PluginName": "Clans"
    },
    "ClanTeam": {
      "Enabled": true,
      "PluginName": "ClanTeam"
    },
    "RaidlandsRoamBot": {
      "Enabled": true,
      "PluginName": "RaidlandsRoamBot",
      "RequiredForBotEvents": true
    },
    "Rewards": {
      "Mode": "Command",
      "RpGiveCommand": "sr add {playerId} {amount}",
      "UsePendingRewardsIfOffline": true
    },
    "Website": {
      "Enabled": false,
      "BaseUrl": "https://example.com/api/raidlands/events",
      "ApiKey": "CHANGE_ME",
      "RetryFailedRequests": true
    },
    "Discord": {
      "Enabled": false,
      "WebhookUrl": ""
    },
    "ZoneManager": {
      "Enabled": false,
      "UseAsBridgeOnly": true
    }
  },

  "Ui": {
    "CommandAliases": ["revents", "eventsadmin", "raidevents"],
    "DefaultPageSize": 8,
    "ActiveEventRefreshSeconds": 5,
    "UseAdminToasts": true
  }
}
```

---

## 27. MVP scope

The MVP is **not** “all event types.”

The MVP is the manager that makes event types easy to add.

### 27.1 MVP must include

1. Core event definition loader/saver.
2. Event validation.
3. Event lifecycle manager.
4. Active event instance tracking.
5. Admin UI dashboard.
6. Admin UI event template list.
7. Admin UI create/edit/delete event definitions.
8. Admin UI import/export.
9. Ruleset system.
10. Reward system.
11. Scoring ledger.
12. Clan adapter.
13. Reward/RP adapter.
14. Basic location service.
15. Visibility/announcement service.
16. Map marker service.
17. Scheduler.
18. Admin start/stop/cleanup commands.
19. Player purchase trigger support.
20. Provider registry/API.
21. RaidlandsRoamBot adapter contract.
22. Cleanup system.
23. Audit/history logging.

### 27.2 MVP should include one simple built-in test provider

To prove the manager works before event packs exist, include a minimal generic provider:

```text
GenericZoneObjective
```

This provider can create a temporary zone, marker, timer, score by kills/time in zone, and reward winners.

It does not need to be the final KOTH implementation, but it proves:

- locations
- rulesets
- scoring
- rewards
- visibility
- schedule
- admin start
- player purchase
- cleanup
- UI editing

### 27.3 MVP does not need

- full raidable base provider
- full convoy provider
- complex boss AI
- website frontend
- every event idea implemented
- perfect CUI polish
- advanced anti-abuse system beyond basic filters

---

## 28. Suggested implementation phases

### Phase 0: Contracts and scaffolding

Deliverables:

- `RaidlandsEvents.cs` plugin shell.
- Config/data folder creation.
- Permission registration.
- Adapter status checks.
- Basic console/chat command routing.
- Data classes for definitions/instances/rules/rewards.
- Provider registration design.

Definition of done:

- `/revents status` shows plugin status and adapter availability.
- Invalid/missing dependencies are warnings, not crashes.

### Phase 1: Definition storage and admin UI shell

Deliverables:

- Load/save event definitions.
- Load/save rulesets.
- Load/save reward profiles.
- Admin dashboard CUI.
- Template list CUI.
- Basic edit/clone/delete flows.
- Import/export file workflow.

Definition of done:

- Admin can create a draft event, save it, clone it, delete it, export it, and import it back.

### Phase 2: Lifecycle, locations, visibility, cleanup

Deliverables:

- Event lifecycle manager.
- Event instance store.
- Location resolver for saved locations and simple random open-world points.
- Chat announcements.
- Basic map marker creation/removal.
- Cleanup registry.

Definition of done:

- Admin can start a generic event at current location or random location, see it announced/marked, and stop/cleanup it.

### Phase 3: Rulesets, scoring, rewards

Deliverables:

- Internal ruleset engine.
- Score ledger.
- Player and clan scoring aggregation.
- Placement ranking.
- Fixed rewards.
- Percentage pool rewards.
- RP/item/command reward adapter.
- Pending rewards if player offline.

Definition of done:

- Generic event can score players/clans and pay configured rewards with correct split mode.

### Phase 4: Scheduler and purchase triggers

Deliverables:

- Scheduled events.
- Weighted dynamic event pool.
- Online player scaling.
- Player purchase command/UI.
- Cost handling/refunds.
- Cooldowns.

Definition of done:

- Events can start automatically, by admin, and by public player purchase.

### Phase 5: Provider API

Deliverables:

- External provider registration.
- Start/stop provider calls.
- Provider validation calls.
- Provider score/objective reporting hooks.
- Provider debug UI.

Definition of done:

- A separate test provider plugin can register an event type and be started by RaidlandsEvents.

### Phase 6: RaidlandsRoamBot integration

Deliverables:

- Bot adapter.
- Bot spawn request/response.
- Bot group ownership tracking.
- Bot cleanup.
- NPC kill/damage scoring.
- Boss objective completion.

Definition of done:

- A test boss/guard event can request bots, track their kills/deaths, and clean them up.

### Phase 7: Website/API and Discord reporting

Deliverables:

- Website adapter.
- Outbound event started/completed payloads.
- Retry queue.
- Discord announcements.
- Event history export.

Definition of done:

- Website/Discord receive event start/end/result data without blocking event completion.

### Phase 8: First real event packs

Recommended order:

1. `RaidlandsEvents_KingOfTheHill`
2. `RaidlandsEvents_AirdropSwarm`
3. `RaidlandsEvents_ResourceStorm`
4. `RaidlandsEvents_MeteorStrike`
5. `RaidlandsEvents_RaidBaseProvider`
6. `RaidlandsEvents_WarlordBoss`
7. `RaidlandsEvents_ConvoyProvider`

---

## 29. First event packs after MVP

### 29.1 King of the Hill

Core systems tested:

- zone/ruleset
- scoring
- clan aggregation
- reward split
- map marker
- scheduled/admin/purchase triggers

### 29.2 Airdrop Swarm

Core systems tested:

- spawned entity ownership
- crate cleanup
- visibility
- loot timing
- public chaos events

### 29.3 Resource Storm

Core systems tested:

- spawned resource nodes/entities
- score from mining/objectives
- 1000x resource balancing
- temporary hotspot gameplay

### 29.4 Meteor Strike

Core systems tested:

- random open-world location
- delayed visibility
- mineable event entities
- entity damage contribution

### 29.5 RaidBaseProvider

Core systems tested:

- custom prefab/base spawning
- raid damage scoring
- TC/crate objectives
- ruleset protection around unrelated bases
- optional third-party plugin adapter or custom base spawner

### 29.6 WarlordBoss

Core systems tested:

- RaidlandsRoamBot integration
- boss damage contribution
- NPC guard squads
- boss death completion
- elite rewards

---

## 30. Example complete event definition: clan KOTH

```json
{
  "SchemaVersion": 1,
  "Id": "koth_clan_warzone",
  "DisplayName": "Clan Warzone KOTH",
  "Enabled": true,
  "EventType": "GenericZoneObjective",
  "Provider": "RaidlandsEvents",
  "Category": "PvPControl",
  "Tags": ["pvp", "clan", "koth"],

  "Triggers": {
    "Scheduled": {
      "Enabled": true,
      "BypassOnlineMinimum": true,
      "ScheduleMode": "Interval",
      "IntervalMinutes": 90,
      "JitterMinutes": 10,
      "CooldownMinutesAfterRun": 60
    },
    "Admin": {
      "Enabled": true,
      "Permission": "raidlandsevents.admin.start"
    },
    "Purchase": {
      "Enabled": true,
      "PublicEvent": true,
      "CooldownMinutesPerPlayer": 120,
      "CooldownMinutesGlobal": 30,
      "Costs": [
        { "Type": "RP", "Amount": 5000 },
        { "Type": "Item", "ShortName": "scrap", "Amount": 25000 }
      ],
      "RefundOnStartFailure": true,
      "PurchaserDoesNotOwnEvent": true
    }
  },

  "Location": {
    "Mode": "RandomOpenWorld",
    "MinDistanceFromSafeZone": 300,
    "MinDistanceFromMonument": 150,
    "MinDistanceFromPlayerBase": 120,
    "RequireFlatArea": true,
    "RequiredFlatRadius": 50,
    "MaxSlopeDegrees": 18,
    "MaxAttempts": 80
  },

  "Rules": {
    "RuleSetId": "default_warzone",
    "Radius": 125
  },

  "Visibility": {
    "ChatAnnouncement": true,
    "DiscordAnnouncement": true,
    "WebsiteAnnouncement": true,
    "MapMarker": true,
    "MarkerType": "GenericRadius",
    "MarkerName": "Clan Warzone KOTH",
    "MarkerRadius": 125,
    "MarkerDelaySeconds": 0,
    "ShowExactGrid": true,
    "BroadcastStart": true,
    "BroadcastHalfway": true,
    "BroadcastFiveMinuteWarning": true,
    "BroadcastCompletion": true,
    "BroadcastWinner": true
  },

  "Scaling": {
    "Enabled": true,
    "UseOnlinePlayerCount": true,
    "ScheduledEventsBypassOnlineGate": true,
    "Tiers": [
      { "MinOnline": 0, "MaxOnline": 19, "RewardMultiplier": 0.75, "RadiusMultiplier": 0.9 },
      { "MinOnline": 20, "MaxOnline": 49, "RewardMultiplier": 1.0, "RadiusMultiplier": 1.0 },
      { "MinOnline": 50, "MaxOnline": 999, "RewardMultiplier": 1.5, "RadiusMultiplier": 1.15 }
    ]
  },

  "Scoring": {
    "ScoreScope": "Clan",
    "AllowSoloIfNoClan": true,
    "RankBy": "TotalScore",
    "TieBreakerOrder": ["ZoneTime", "PlayerKills", "DamageToPlayer"],
    "MinimumScoreToQualify": 250,
    "MinimumTimeInEventSeconds": 60,
    "Metrics": {
      "PlayerKill": {
        "Enabled": true,
        "Points": 100,
        "RequireInsideZone": true,
        "IgnoreSameClan": true,
        "IgnoreAlliedClan": true,
        "RepeatVictimDiminishWindowSeconds": 180,
        "RepeatVictimMultiplier": 0.25
      },
      "DamageToPlayer": {
        "Enabled": true,
        "PointsPer100Damage": 10,
        "MaxPointsPerVictimPerMinute": 50
      },
      "ZoneTime": {
        "Enabled": true,
        "PointsPerSecond": 2,
        "RequireAlive": true,
        "MaxPlayersScoringPerClan": 6
      }
    }
  },

  "Rewards": {
    "RewardMode": "PercentagePool",
    "RecipientScope": "Clan",
    "ClanDistribution": "ContributionWeighted",
    "Pool": [
      { "Type": "RP", "Amount": 50000 },
      { "Type": "Item", "ShortName": "sulfur.ore", "Amount": 2000000 },
      { "Type": "Item", "ShortName": "metal.refined", "Amount": 250000 }
    ],
    "Placements": [
      { "Place": 1, "Percent": 60 },
      { "Place": 2, "Percent": 25 },
      { "Place": 3, "Percent": 15 }
    ],
    "RoundItemAmounts": true,
    "MinimumQualifiedScore": 250
  },

  "ProviderConfig": {
    "DurationSeconds": 900,
    "HoldMode": "AccumulatedScore",
    "ObjectiveRadius": 45,
    "ShowCaptureProgress": true
  },

  "Cleanup": {
    "DespawnOwnedEntities": true,
    "RemoveMarkers": true,
    "RemoveZones": true,
    "DelaySecondsAfterCompletion": 120,
    "ForceCleanupAfterMinutes": 10
  }
}
```

---

## 31. Example complete event definition: public purchased RaidMe shell

This is not the full raid base implementation. It shows how a purchased public event should be represented.

```json
{
  "SchemaVersion": 1,
  "Id": "raidme_medium_public",
  "DisplayName": "Public Medium RaidMe Base",
  "Enabled": true,
  "EventType": "RaidBase",
  "Provider": "RaidlandsEvents_RaidBaseProvider",
  "Category": "RaidObjective",
  "Tags": ["raid", "public", "purchaseable", "pvp"],

  "Triggers": {
    "Scheduled": {
      "Enabled": false
    },
    "Admin": {
      "Enabled": true,
      "Permission": "raidlandsevents.admin.start"
    },
    "Purchase": {
      "Enabled": true,
      "PublicEvent": true,
      "CooldownMinutesPerPlayer": 180,
      "CooldownMinutesGlobal": 45,
      "Costs": [
        { "Type": "RP", "Amount": 12000 },
        { "Type": "Item", "ShortName": "scrap", "Amount": 50000 }
      ],
      "RefundOnStartFailure": true,
      "AnnouncePurchaser": true,
      "PurchaserDoesNotOwnEvent": true
    }
  },

  "Location": {
    "Mode": "RandomOpenWorld",
    "MinDistanceFromSafeZone": 400,
    "MinDistanceFromMonument": 200,
    "MinDistanceFromPlayerBase": 180,
    "RequireFlatArea": true,
    "RequiredFlatRadius": 80,
    "MaxSlopeDegrees": 12,
    "MaxAttempts": 100
  },

  "Rules": {
    "RuleSetId": "raid_base_rules",
    "Radius": 160
  },

  "Visibility": {
    "ChatAnnouncement": true,
    "DiscordAnnouncement": true,
    "WebsiteAnnouncement": true,
    "MapMarker": true,
    "MarkerType": "RaidBase",
    "MarkerName": "Public Medium RaidMe Base",
    "MarkerRadius": 160,
    "ShowExactGrid": true,
    "BroadcastStart": true,
    "BroadcastFiveMinuteWarning": true,
    "BroadcastCompletion": true,
    "BroadcastWinner": true
  },

  "Scoring": {
    "ScoreScope": "Clan",
    "AllowSoloIfNoClan": true,
    "RankBy": "TotalScore",
    "TieBreakerOrder": ["TCDestroyed", "CrateUnlocked", "ExplosiveDamage", "PlayerKills"],
    "MinimumScoreToQualify": 500,
    "Metrics": {
      "PlayerKill": { "Enabled": true, "Points": 100, "RequireInsideZone": true },
      "ExplosiveDamage": { "Enabled": true, "PointsPer100Damage": 5 },
      "DamageToEventEntity": { "Enabled": true, "PointsPer100Damage": 2 },
      "TCDestroyed": { "Enabled": true, "Points": 1000 },
      "CrateUnlocked": { "Enabled": true, "Points": 700 },
      "CrateLooted": { "Enabled": true, "Points": 300 }
    }
  },

  "Rewards": {
    "RewardMode": "FixedPlacements",
    "RecipientScope": "Clan",
    "ClanDistribution": "ContributionWeighted",
    "Placements": [
      {
        "Place": 1,
        "Rewards": [
          { "Type": "RP", "Amount": 30000 },
          { "Type": "Item", "ShortName": "explosive.timed", "Amount": 20 },
          { "Type": "Item", "ShortName": "ammo.rocket.basic", "Amount": 40 }
        ]
      },
      {
        "Place": 2,
        "Rewards": [
          { "Type": "RP", "Amount": 15000 },
          { "Type": "Item", "ShortName": "explosive.timed", "Amount": 8 }
        ]
      }
    ]
  },

  "ProviderConfig": {
    "BaseProfile": "medium_raidme_01",
    "UseCopyPaste": false,
    "SpawnTurrets": true,
    "SpawnTraps": true,
    "SpawnLockedCrate": true,
    "RequireTCDestroyed": true,
    "DurationSeconds": 2700,
    "BotGroups": [
      {
        "GroupKey": "base_guards",
        "Profile": "RaidGuard",
        "Difficulty": "Medium",
        "Count": 6,
        "HealthMultiplier": 2.0,
        "WeaponProfile": "MixedRifle"
      }
    ]
  },

  "Cleanup": {
    "DespawnOwnedEntities": true,
    "DespawnBots": true,
    "RemoveMarkers": true,
    "RemoveZones": true,
    "KillTemporaryBuilds": true,
    "DelaySecondsAfterCompletion": 600,
    "ForceCleanupAfterMinutes": 20
  }
}
```

---

## 32. Event history model

Each completed event should write a compact result.

```json
{
  "EventInstanceId": "koth_clan_warzone_8f92",
  "DefinitionId": "koth_clan_warzone",
  "DisplayName": "Clan Warzone KOTH",
  "StartedAtUtc": "2026-07-06T19:00:00Z",
  "EndedAtUtc": "2026-07-06T19:18:22Z",
  "EndReason": "Completed",
  "LocationGrid": "G12",
  "Leaderboard": [
    {
      "Rank": 1,
      "Scope": "Clan",
      "Id": "ABC",
      "DisplayName": "ABC",
      "Score": 5520,
      "PlayerBreakdown": [
        { "UserId": "7656119...", "Name": "PlayerOne", "Score": 2500 },
        { "UserId": "7656119...", "Name": "PlayerTwo", "Score": 1800 }
      ]
    }
  ],
  "RewardsPaid": [
    {
      "UserId": "7656119...",
      "Type": "RP",
      "Amount": 12000,
      "Status": "Paid"
    }
  ],
  "Stats": {
    "PlayerKills": 42,
    "NpcKills": 0,
    "TotalDamage": 18220,
    "UniqueParticipants": 31
  }
}
```

---

## 33. Audit log model

Audit log should record admin/config actions.

```json
{
  "TimestampUtc": "2026-07-06T19:00:00Z",
  "ActorUserId": "7656119...",
  "ActorName": "Carl",
  "Action": "EventDefinitionEdited",
  "TargetId": "koth_clan_warzone",
  "Summary": "Changed reward pool RP from 30000 to 50000",
  "BeforeHash": "abc123",
  "AfterHash": "def456"
}
```

---

## 34. Error handling

### 34.1 Start failure

If an event fails to start:

- mark instance `StartFailed`
- write error to audit/history
- cleanup any partial spawns
- refund purchase cost if configured
- notify admin if admin-started
- notify purchaser if player-purchased

### 34.2 Reward failure

If a reward cannot be paid:

- do not lose the reward
- write pending reward record
- retry if adapter becomes available
- allow admin to manually retry payout

### 34.3 Provider failure

If provider throws or returns failure:

- stop event safely
- ask provider to cleanup
- run manager cleanup registry
- disable provider temporarily if repeated failure threshold reached
- show provider error in admin UI

---

## 35. Testing plan

### 35.1 Local/staging tests

Test these before production:

1. Load plugin with no optional dependencies.
2. Load plugin with Clans missing.
3. Load plugin with RaidlandsRoamBot missing.
4. Import valid event package.
5. Import invalid event package.
6. Create/edit/delete event through UI.
7. Export/import event.
8. Start generic event at admin position.
9. Start generic event at random position.
10. Stop event manually.
11. Force cleanup after provider failure.
12. Score player kills.
13. Score clan kills.
14. Split clan rewards evenly.
15. Split clan rewards by contribution.
16. Percentage reward pool distribution.
17. Offline reward pending queue.
18. Plugin unload during active event.
19. Server restart during active event.
20. Dynamic schedule with online player threshold.
21. Scheduled event bypassing online threshold.
22. Player purchase refund after start failure.

### 35.2 Abuse tests

- same clan kill farming
- allied clan kill farming
- repeated victim farming
- sleeper kills
- outside-zone kills
- teleport into event
- backpack use in disabled ruleset
- kit use in disabled ruleset
- deploying bags/turrets in event zone
- damaging nearby unrelated base
- logging out inside event
- attempting to loot crate before objective completion

### 35.3 Performance tests

- multiple active events
- high player count in event zone
- high damage event with many score entries
- UI open for multiple admins
- rapid map marker updates
- cleanup of hundreds of spawned entities
- provider plugin reload while event active

---

## 36. Security and permissions

- Never allow non-admins to import arbitrary event JSON.
- Validate all commands used in rewards.
- Restrict reward command placeholders.
- Do not allow imported packages to define unrestricted console commands unless admin explicitly approves.
- Require confirmation for delete/overwrite/import actions.
- Keep audit records for admin edits.
- Hide API keys/webhook URLs in UI.
- Do not print full secrets in logs.

---

## 37. Performance guidelines

- Keep score aggregation incremental.
- Store raw score events only for active events and compact them into history after completion.
- Avoid expensive distance checks against every event every damage tick; index active zones by radius and short-circuit quickly.
- Subscribe to costly hooks only when active rules/scoring need them.
- Do not redraw CUI every second.
- Batch website updates.
- Avoid synchronous web/API calls in event-critical paths.
- Cleanup in safe batches if deleting many entities.

---

## 38. Recommended first coding milestone

Start with this minimal skeleton:

```text
RaidlandsEvents.cs
  - Config load/save
  - Data folders
  - Permissions
  - Adapter status checks
  - /revents status
  - /revents UI shell
  - EventDefinition class
  - RuleSet class
  - RewardProfile class
  - EventInstance class
  - Validation service
  - Import/export service
```

Then add the generic event provider:

```text
GenericZoneObjective
  - start at admin position
  - create marker
  - create temporary internal zone
  - score kills inside radius
  - score time inside radius
  - end after timer
  - rank players/clans
  - pay fixed or percentage rewards
  - cleanup marker/zone
```

That proves the whole manager loop without needing raidable bases or bots yet.

---

## 39. Open implementation questions

These do not block the plan, but should be decided before coding the corresponding adapter.

1. What exact RP/economy plugin or command should be used to add/remove RP?
2. Should event tokens be internal to RaidlandsEvents or part of the existing RP/shop system?
3. Should the website API be pushed from the game server only, or should the website also query current status?
4. Should event definitions be edited primarily through CUI, JSON files, or both equally?
5. Which events should be allowed as player-purchased events at launch?
6. Should clan alliances count as friendly for scoring filters by default?
7. Should public purchased raid events announce the purchaser name or keep purchaser anonymous?
8. Should scheduled major events run during low-pop hours exactly as scheduled, or run but with smaller rewards?
9. Should reward payouts be immediate, claim-based, or both?
10. Should event history be wipe-limited or season-persistent for the website?

---

## 40. Summary

The best first build is not a single hardcoded event. It is a flexible manager:

```text
RaidlandsEvents = lifecycle + UI + import/export + rules + scoring + rewards + scheduling + adapters + provider API
```

Then real event packs can plug into it:

```text
KOTH
Airdrop Swarm
Sulfur Storm
HQM Meteor
RaidMe / Raidable Bases
Warlord Boss
Convoy
Monument Takeover
Clan Bounty
Final Wipe Purge
```

The manager should be strong enough that adding a new event type mostly means writing a provider and importing an event definition, not rewriting scoring, rewards, rules, locations, UI, cleanup, or scheduling.
