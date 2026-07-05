# RaidlandsRoamBots Tactical Rewrite Plan v2

## Purpose

Rewrite `RaidlandsRoamBots` into a single-mode, player-like roaming bot system.

The plugin should stop trying to wake up Rust Gen2/native AI outside its intended contexts. Instead, it should use one controllable legacy scientist body as the physical actor and run a custom Raidlands tactical brain on top of it.

The tactical brain should eventually make bots:

- roam above ground around terrain, roads, forests, hills, monuments, and open-field cover;
- hear gunshots, explosions, impacts, bot damage, and eventually movement-like noise;
- investigate last-heard locations without instantly knowing a hidden player's live position;
- require plugin-confirmed line of sight before shooting;
- remember last-seen positions;
- search and clear angles after losing line of sight;
- fight from cover;
- peek, tuck, wide-swing, and approximate jiggle peeks;
- flank with teammates;
- push when they have advantage;
- retreat or regroup when they are exposed, low health, or outnumbered;
- throw grenades and smoke when the situation calls for it;
- place barricades as real world entities when caught in the open;
- avoid entering player bases for now.

After the baseline bot works, the system should add LLM decision-making tie-ins. However, the baseline architecture should be built from day one as if an LLM advisor exists. In early phases the advisor is deliberately unconfigured and returns `advisor_not_configured`, causing the bot to use fallback heuristic behavior.

---

## Non-Negotiable Product Decisions

### 1. One AI path only

Remove Gen2/native support, naval prefab support, runtime AI switching, and legacy fallback logic.

Do not keep these concepts:

```text
gen2_native
legacy_scientist runtime mode
native Gen2 fallback
naval scientist body
try Gen2, else legacy
```

The new plugin has one conceptual brain:

```text
playerlike tactical brain
```

### 2. Legacy scientist is only the body

The Rust NPC body provides:

```text
NPCPlayer / BasePlayer entity
inventory and kits
health and damage handling
model / corpse / loot behavior
BaseNavigator movement
IAIAttack / held weapon firing
```

Raidlands code provides:

```text
perception
memory
target selection
movement intent
cover choice
combat state
flanking
grenade/barricade decisions
squad coordination
future LLM advisory layer
```

### 3. Real entities are allowed

For this project, bot-created entities can be real entities.

That means:

```text
bot corpses can remain
bot loot can remain
barricades can remain
grenades and smoke can be real
bot-placed world objects do not need v1 cleanup
```

Still add cooldowns and soft caps so bots do not spam entities or hurt server performance. This is gameplay sanity, not cleanup.

### 4. No base entry yet

The first working bot should not path into player bases.

For now:

```text
if player enters base -> bot holds outside, watches exits, repositions, or gives up
```

Leave base invasion, raiding, door logic, ladder logic, compound navigation, trap handling, and roof clearing for a later `BaseAssaultModule`.

### 5. LLM is a high-level advisor, not a real-time controller

The LLM never directly controls movement, aiming, shooting, timers, cooldowns, item use, or server entities.

The deterministic tactical brain always:

1. observes the game state;
2. builds a compact decision request;
3. generates legal candidate actions;
4. optionally asks an advisor to pick among those legal actions;
5. validates the advisor response;
6. falls back to heuristics on any failure;
7. executes only validated actions.

The LLM chooses from precomputed legal actions. It does not invent arbitrary behavior.

---

## Target Architecture

```text
RaidlandsRoamBots
  ├─ SpawnManager
  ├─ KitManager
  ├─ StatsManager
  ├─ BotRegistry
  ├─ BodyController
  │   ├─ PrepareNpcBody
  │   ├─ MoveBotTo
  │   ├─ FaceEntity / FacePosition
  │   ├─ StartBotAttack
  │   └─ StopBotAttack
  ├─ PerceptionSystem
  │   ├─ Vision
  │   ├─ Hearing
  │   └─ StimulusBus
  ├─ MemorySystem
  ├─ TacticalBrain
  │   ├─ StateMachine
  │   ├─ CandidateActionBuilder
  │   ├─ HeuristicScorer
  │   ├─ DecisionArbiter
  │   └─ ActionExecutor
  ├─ DecisionAdvisorLayer
  │   ├─ NullDecisionAdvisor
  │   ├─ HeuristicFallbackPolicy
  │   ├─ DecisionRequestBuilder
  │   ├─ DecisionResponseValidator
  │   ├─ DecisionTraceLog
  │   ├─ FutureOpenAiCompatibleAdvisor
  │   └─ FutureWebsiteProxyAdvisor
  ├─ CoverPlanner
  ├─ CombatController
  ├─ EquipmentController
  ├─ SquadBlackboard
  ├─ BaseAvoidance
  ├─ PersistenceController
  └─ DebugDiagnostics
```

Keep this inside one `.cs` Oxide plugin at first. Split into files/modules only after the system is stable.

---

## Runtime Loop

Replace the current movement loop with staggered tactical loops.

```csharp
private Timer maintainTimer;
private Timer perceptionTimer;
private Timer brainTimer;
private Timer squadTimer;
private Timer scoreboardTimer;
private Timer decisionTraceSaveTimer;

private void StartRuntime()
{
    StopRuntime();
    spawnRetryBlockedUntil = 0f;

    maintainTimer = timer.Every(Math.Max(5f, config.MaintainIntervalSeconds), MaintainPopulation);
    perceptionTimer = timer.Every(config.AI.PerceptionTickSeconds, PerceptionTick);
    brainTimer = timer.Every(config.AI.DecisionTickSeconds, TacticalBrainTick);
    squadTimer = timer.Every(config.AI.SquadTickSeconds, SquadTick);
    scoreboardTimer = timer.Every(Math.Max(15f, config.ScoreboardIntervalSeconds), UpdateScoreboards);

    MaintainPopulation();
}
```

Recommended default tick rates:

```json
"AI": {
  "Perception Tick Seconds": 0.25,
  "Decision Tick Seconds": 0.35,
  "Squad Tick Seconds": 0.75
}
```

Do not call the LLM from these ticks directly. The tick should submit a decision request only when the hard-decision trigger says it is worth asking. The bot continues using deterministic behavior while an external response is pending.

---

## New Config Shape

The new config should make the single-mode design obvious.

This example reflects the canonical plugin defaults as of `RaidlandsRoamBots` v0.3.25. The checked-in `oxide/config/RaidlandsRoamBots.json` may intentionally differ when it is holding a focused local test setup.

```json
{
  "Enabled": false,
  "Target Population": 15,
  "Minimum Allowed Population": 0,
  "Maximum Allowed Population": 30,

  "Team Size Weights": {
    "solo": 60,
    "duo": 30,
    "trio": 10
  },

  "Bot Clans": [
    { "Key": "north-road", "Tag": "NR", "Name": "North Road" },
    { "Key": "excavator-crew", "Tag": "EAC", "Name": "Excavator Crew" },
    { "Key": "wipe-team", "Tag": "WT", "Name": "Wipe Team" },
    { "Key": "launch-site", "Tag": "LS", "Name": "Launch Site" }
  ],

  "Skill Weights": {
    "casual": 34,
    "average": 33,
    "dangerous": 33
  },

  "Skill Definitions": {
    "casual": {
      "Health": 100.0,
      "DamageScale": 1.0,
      "IncomingDamageScale": 1.0,
      "ReactionMinSeconds": 0.75,
      "ReactionMaxSeconds": 1.35,
      "AimErrorDegrees": 5.0,
      "Aggression": 0.35,
      "Courage": 0.35,
      "TacticalNoise": 0.25
    },
    "average": {
      "Health": 110.0,
      "DamageScale": 1.0,
      "IncomingDamageScale": 1.0,
      "ReactionMinSeconds": 0.40,
      "ReactionMaxSeconds": 0.85,
      "AimErrorDegrees": 3.0,
      "Aggression": 0.55,
      "Courage": 0.55,
      "TacticalNoise": 0.15
    },
    "dangerous": {
      "Health": 120.0,
      "DamageScale": 1.0,
      "IncomingDamageScale": 1.0,
      "ReactionMinSeconds": 0.18,
      "ReactionMaxSeconds": 0.45,
      "AimErrorDegrees": 1.5,
      "Aggression": 0.8,
      "Courage": 0.8,
      "TacticalNoise": 0.06
    }
  },

  "High Tier Kit Weight": 5,

  "Kit Selection": {
    "Default Group": "default",
    "Eligible Kit Names": ["ak", "lr300", "m16", "mp5"],
    "Rare High Tier Kit Names": ["raid"],
    "Weapon Shortnames": [
      "rifle.ak",
      "rifle.lr300",
      "m16a2",
      "smg.mp5",
      "lmg.m249",
      "rifle.m39",
      "rifle.semiauto",
      "rifle.bolt",
      "rifle.l96",
      "smg.thompson",
      "smg.2",
      "pistol.python",
      "pistol.semiauto",
      "pistol.m92"
    ]
  },

  "Spawn Settings": {
    "Spawn Mode": "near_players",
    "Use Random Land Fallback": true,
    "Max Position Attempts": 80,
    "Navmesh Sample Distance": 12.0,
    "Minimum Above Water": 1.5,
    "Require Land Spawns": true,
    "Minimum Land Height": 0.0,
    "Maximum Below Terrain Tolerance": 0.75,
    "Runtime Invalid Position Despawn Seconds": 2.0,
    "Group Spawn Radius": 12.0,
    "Use Generated Positions Near Players": true,
    "Avoid Safe Zone Spawns": true,
    "Ignore Players In Safe Zones": true,
    "Near Player Minimum Distance": 80.0,
    "Near Player Maximum Distance": 300.0,
    "Near Player Attempts Per Bot": 64,
    "Near Player Anchor Name Or SteamID": "",
    "Safe Zone Spawn Buffer Distance": 75.0
  },

  "Prefab Candidates In Order": [
    "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab",
    "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab",
    "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_junkpile_pistol.prefab"
  ],

  "AI": {
    "Perception Tick Seconds": 0.25,
    "Decision Tick Seconds": 0.35,
    "Squad Tick Seconds": 0.75,

    "Vision Range": 190.0,
    "Vision Fov Degrees": 220.0,
    "Close Awareness Radius": 22.0,
    "Minimum Exposed Target Fraction": 0.25,
    "Minimum Exposed Target Fraction To Shoot": 0.25,
    "Foliage Blocks Vision": true,
    "Foliage Vision Check Radius": 0.9,
    "Maximum Clear Vision Through Foliage": 14.0,
    "Foliage Hits To Block Vision": 1,
    "Foliage Terrain Sampling": true,
    "Foliage Terrain Sample Step": 6.0,
    "Foliage Terrain Samples To Block Vision": 4,
    "Foliage Occluder Layer Names": ["Tree", "Resource", "World", "Default"],
    "Target Memory Seconds": 24.0,
    "Search Last Seen Seconds": 38.0,

    "Unsuppressed Gunshot Hearing Range": 240.0,
    "Suppressed Gunshot Hearing Range": 85.0,
    "Explosion Hearing Range": 380.0,
    "Melee Or Tool Hearing Range": 45.0,
    "Sprint Hearing Range": 28.0,
    "Sound Investigation Commitment Seconds": 16.0,
    "Sound Investigation Command Cooldown Seconds": 1.25,

    "Require Line Of Sight To Shoot": true,
    "Allow Hearing": true,
    "Allow Cover": true,
    "Allow Flanking": true,
    "Allow Grenades": true,
    "Allow Smoke": true,
    "Allow Barricades": true,
    "Allow Jiggle Peeking": true,
    "Allow Jump Peek Approximation": false,

    "Cover Search Radius": 28.0,
    "Cover Point Attempts": 28,
    "Cover Minimum Distance From Threat": 16.0,
    "Cover Reposition Cooldown Seconds": 4.0,
    "Cover Arrival Distance": 1.75,
    "Effective Cover Max Exposed Fraction": 0.35,
    "Peek Offset Distance": 3.0,
    "Peek Exposure Min Seconds": 0.35,
    "Peek Exposure Max Seconds": 1.15,
    "Tuck Min Seconds": 0.45,
    "Tuck Max Seconds": 1.6,

    "Stuck Detection Seconds": 5.0,
    "Stuck Recovery Cooldown Seconds": 2.5,
    "Stuck Recovery Search Radius": 18.0,
    "Hard Stuck Failed Paths To Despawn": 30,

    "Squad Flank Distance": 24.0,
    "Squad Regroup Distance": 55.0,
    "Squad Contact Commitment Seconds": 35.0,
    "Flank Cooldown Seconds": 7.0,

    "Grenade Cooldown Seconds": 30.0,
    "Team Grenade Cooldown Seconds": 10.0,
    "Grenade Prefab": "assets/prefabs/weapons/f1 grenade/grenade.f1.deployed.prefab",
    "Smoke Grenade Prefab": "assets/prefabs/tools/smoke grenade/grenade.smoke.deployed.prefab",
    "Grenade Minimum Throw Distance": 12.0,
    "Grenade Maximum Throw Distance": 42.0,
    "Smoke Minimum Throw Distance": 10.0,
    "Smoke Maximum Throw Distance": 55.0,
    "Grenade Throw Velocity": 17.0,
    "Smoke Throw Velocity": 14.0,
    "Grenade Fuse Seconds": 3.2,
    "Grenade Danger Radius": 8.0,
    "Grenade Ally Avoid Radius": 10.0,
    "Grenade Avoidance Seconds": 5.0,
    "Smoke Screen Distance": 8.0,
    "Maximum Active Bot Utility Projectiles": 8,

    "Barricade Cooldown Seconds": 12.0,
    "Barricade Prefab": "assets/prefabs/deployable/barricades/barricade.cover.wood_double.prefab",
    "Maximum Active Bot Barricades": 12,
    "Recycle Oldest Barricade When Cap Reached": true,
    "Barricade Placement Distance": 4.5,
    "Barricade Hold Seconds": 8.0,
    "Barricade Fight Commitment Seconds": 10.0,
    "Barricade Followup Memory Seconds": 6.0,
    "Retreat Wall Cover Distance": 10.0,

    "Long Range Defensive Minimum Distance": 40.0,
    "Long Range Defensive Maximum Distance": 60.0,
    "Long Range Losing Fight Memory Seconds": 10.0,
    "Nearby Defensive Cover Minimum Distance": 3.0,
    "Nearby Defensive Cover Maximum Distance": 8.0,
    "Long Range Defensive Health Fraction Casual": 0.68,
    "Long Range Defensive Health Fraction Average": 0.82,
    "Long Range Defensive Health Fraction Dangerous": 0.92,
    "Full Health Cover Discipline Chance Casual": 0.55,
    "Full Health Cover Discipline Chance Average": 0.85,
    "Full Health Cover Discipline Chance Dangerous": 1.0,
    "Healing Return Fire Distance": 24.0,

    "Damage Wall Reaction Window Seconds": 12.0,
    "Damage Wall Awareness Recheck Seconds": 1.5,
    "Damage Wall Chance Casual": 0.45,
    "Damage Wall Chance Average": 1.0,
    "Damage Wall Chance Dangerous": 1.0,
    "Low Health Cover Threshold": 0.6,
    "Low Health Cover Notice Chance Casual": 0.65,
    "Low Health Cover Notice Chance Average": 0.95,
    "Low Health Cover Notice Chance Dangerous": 1.0,
    "Low Health Cover Recheck Seconds": 4.0,
    "Low Health Cover Commitment Seconds": 24.0,
    "Low Health Cover Heal Per Second": 5.0,
    "Low Health Cover Heal Target Fraction": 0.96,
    "Passive Combat Heal Per Second": 1.5,
    "Passive Combat Heal Target Fraction": 1.0,
    "Syringe Fire Lock Seconds": 2.2,
    "Syringe Cooldown Seconds": 8.0,
    "Auto Reload Bot Weapons": true,

    "Do Not Enter Bases": true,
    "Base Avoidance Radius": 8.0,
    "Base Hold Seconds": 12.0
  },

  "Decision Advisor": {
    "Enabled": true,
    "Provider": "none",
    "Mode": "fallback_only",
    "Shadow Mode": true,
    "Treat Unconfigured Advisor As Failure": true,
    "Fallback On Any Failure": true,
    "Endpoint Url": "",
    "Api Key": "",
    "Model": "",
    "Timeout Milliseconds": 750,
    "Decision Ttl Milliseconds": 3000,
    "Minimum Confidence": 0.55,
    "Max Concurrent Requests": 2,
    "Min Seconds Between Requests Per Bot": 8.0,
    "Ask When Bot Is Stuck": true,
    "Ask When Action Scores Are Close": true,
    "Ask When Push Retreat Or Flank Is High Impact": true,
    "Ask When Same Action Failed Repeatedly": true,
    "Ask When Squad State Changes Sharply": true,
    "Log Decision Traces": true,
    "Max Recent Events In Request": 24,
    "Max Candidate Actions": 8
  },

  "Persistence": {
    "Kill Bots On Plugin Unload": false,
    "Kill Bots On Disable": false,
    "Leave Corpses": true,
    "Leave Bot Placed Entities": true,
    "Emergency Kill Command Enabled": true
  },

  "Debug": {
    "Debug Spawn Details": false,
    "Debug Tactical Decisions": false,
    "Debug Perception": false,
    "Debug Bot Nameplates": false,
    "Debug Bot Side Panel": false,
    "Debug UI Includes Anchor Player": true,
    "Debug Nameplate Refresh Seconds": 1.0,
    "Debug Nameplate Draw Duration Seconds": 1.25,
    "Debug Nameplate Height": 3.25,
    "Debug Nameplate Font Size": 9,
    "Debug Nameplate Max Distance": 350.0,
    "Debug Cover Scores": false,
    "Debug Decision Advisor": false
  },

  "Bot Profiles": [
    "LaunchLoot",
    "BlueCarded",
    "ColdFurnace",
    "RoadsignMain",
    "HarborEcho",
    "StoneCeiling",
    "OxideSeven",
    "NorthOil",
    "DepotMains",
    "TwigLaw",
    "CratePilot",
    "FuseBox",
    "RoofMuted",
    "BanditQueue",
    "CargoLeft",
    "RedKeycard",
    "BenchTier3",
    "WipeClock",
    "RecyclerOne",
    "OilDock",
    "HazzyStep",
    "GarageDoor",
    "MonumentRun",
    "MetalNode",
    "WorkbenchTwo",
    "TrainYard",
    "BradleyLane",
    "OutpostEdge",
    "LaunchStairs",
    "FurnaceBase"
  ],

  "Spawn Failure Retry Seconds": 120.0,
  "Maintain Interval Seconds": 20.0,
  "Scoreboard Interval Seconds": 60.0,
  "Respawn Delay Seconds": 20.0
}
```

### Config migration from the current plugin

When loading an old config:

```text
remove AI Runtime Mode
remove Allow Legacy Scientist Fallback
remove Apply Kits To Native Gen2 Bots
remove Gen2/naval prefabs
replace prefab candidates with legacy scientist bodies
turn generated near-player positions on
turn random land fallback on
normalize older visibility, foliage, hearing, defensive-healing, utility, health, and damage settings to the current v0.3.25 defaults
pin barricade prefab to the double Wooden Barricade Cover prefab
pin grenade and smoke grenade prefabs to the current deployed F1/smoke prefab paths
preserve kits, bot profiles, population, skill weights, team weights, and stats
add Decision Advisor config with Provider = none and Mode = fallback_only
```

---

## Tactical Runtime Data

### Tactical states

```csharp
private enum TacticalState
{
    Roam,
    InvestigateSound,
    SearchLastKnown,
    AcquireTarget,
    FightFromCover,
    Suppress,
    Flank,
    GrenadeFlush,
    BarricadeHold,
    Push,
    Retreat,
    Regroup,
    HoldOutsideBase
}
```

### Action IDs

Actions are the unit that both heuristics and future LLM advisors choose from.

```csharp
private enum TacticalActionId
{
    None,
    RoamToPoint,
    InvestigateSound,
    SearchLastKnown,
    AcquireVisibleTarget,
    MoveToCover,
    PeekLeft,
    PeekRight,
    WideSwing,
    Tuck,
    SuppressTarget,
    FlankLeft,
    FlankRight,
    PushTarget,
    RetreatToCover,
    RegroupWithSquad,
    ThrowGrenade,
    ThrowSmoke,
    PlaceBarricade,
    HoldOutsideBase,
    AbandonTarget
}
```

### Bot runtime

```csharp
private class BotRuntime
{
    public string BotKey;
    public string DisplayName;
    public string KitName;
    public string SkillTier;
    public SkillDefinition Skill;
    public int TeamId;
    public string SquadRole;
    public string ClanKey;
    public string ClanTag;
    public string ClanName;

    public Vector3 SpawnPosition;
    public Vector3 HomePosition;
    public string Prefab;
    public string EntityType;

    public TacticalState State;
    public TacticalState PreviousState;
    public float StateEnteredAt;
    public float NextDecisionAt;
    public float NextPerceptionAt;

    public TacticalMemory Memory = new TacticalMemory();
    public CombatProfile Combat = new CombatProfile();
    public MovementPlan Movement = new MovementPlan();
    public DecisionContext Decisions = new DecisionContext();

    public Vector3 CurrentDestination;
    public Vector3 CurrentCover;
    public Vector3 CurrentTuckPoint;
    public Vector3 CurrentPeekPoint;
    public Vector3 CurrentFlankPoint;
    public Vector3 CurrentBarricadePoint;
    public bool IsPeeking;
    public float CurrentPeekUntil;
    public float CurrentTuckUntil;
    public float BarricadeCommittedUntil;

    public float NextReactionAllowedAt;
    public float NextCoverSearchAt;
    public float NextPeekAt;
    public float NextFlankAt;
    public float NextStuckRecoveryAt;
    public float NextGrenadeAt;
    public float NextBarricadeAt;
    public float LastBarricadePlacedAt;
    public float DamageBarricadeAwareUntil;
    public float LowHealthCoverAwareUntil;
    public float MedicalFireLockedUntil;
    public float NextSyringeHealAt;
    public float HoldOutsideBaseUntil;
    public float LastShotAt;
    public float LastDamageTakenAt;
    public float LastDamageDealtAt;
    public float LastSoundInvestigateCommandAt;
    public float InvalidPositionSince;

    public string LastBarricadeReason;
    public string LastUtilityReason;
    public string LastFireBlockReason;
    public string LastSightReason;

    public bool IsShooting;
    public bool IsInBaseRestrictedArea;
    public int ConsecutiveFailedPaths;
}
```

### Tactical memory

```csharp
private class TacticalMemory
{
    public BasePlayer Target;
    public ulong TargetUserId;

    public bool HasLineOfSight;
    public float LastLineOfSightAt;

    public Vector3 LastSeenPosition;
    public float LastSeenAt;

    public Vector3 LastHeardPosition;
    public float LastHeardAt;

    public Vector3 LastDamageSourcePosition;
    public BasePlayer LastDamageSourcePlayer;
    public float LastDamagedAt;

    public float TargetConfidence;
    public float ThreatScore;
    public float LastTargetSwitchAt;
}
```

### Squad blackboard

```csharp
private class SquadBlackboard
{
    public int TeamId;
    public string ClanKey;
    public string ClanTag;
    public string ClanName;
    public Dictionary<ulong, EnemyMemory> KnownEnemies = new Dictionary<ulong, EnemyMemory>();
    public Dictionary<string, Vector3> CoverClaims = new Dictionary<string, Vector3>();
    public int TeamSize;
    public int MembersWithLineOfSight;
    public ulong SharedEnemyUserId;
    public Vector3 SharedEnemyPosition;
    public float SharedEnemyKnownAt;
    public bool AnyMemberHasLineOfSight;
    public float NextTeamGrenadeAt;
    public float LastPushCallAt;
    public float LastRegroupCallAt;
    public Vector3 TeamCenter;
    public Vector3 RallyPoint;
}
```

---

## Decision Advisor Layer

This is the LLM-ready seam. It should exist early, even before any real LLM is configured.

### Design rule

Every meaningful tactical choice goes through the same decision flow:

```text
observe state
build legal candidates
score with heuristics
maybe ask advisor
validate advisor response
fallback on failure
execute final action
write decision trace
```

### Early behavior

In early phases:

```text
Provider = none
Mode = fallback_only
NullDecisionAdvisor returns advisor_not_configured
DecisionArbiter records advisor failure
FallbackHeuristicPolicy picks final action
ActionExecutor runs heuristic action
```

This means the code path is future-proofed for an LLM, but the bot is fully playable without one.

### Interfaces

Use C#-style interfaces/classes, not Rust traits.

```csharp
private interface IDecisionAdvisor
{
    string Name { get; }
    bool IsConfigured { get; }
    bool TrySubmit(DecisionRequest request, Action<DecisionAdvisorResult> callback);
}

private class NullDecisionAdvisor : IDecisionAdvisor
{
    public string Name => "none";
    public bool IsConfigured => false;

    public bool TrySubmit(DecisionRequest request, Action<DecisionAdvisorResult> callback)
    {
        callback?.Invoke(DecisionAdvisorResult.Failure("advisor_not_configured"));
        return false;
    }
}
```

The future HTTP advisor should use Oxide `webrequest.Enqueue` or another non-blocking HTTP mechanism. Do not block the main server thread waiting for a model.

### Decision request

```csharp
private class DecisionRequest
{
    public string RequestId;
    public string BotId;
    public int TeamId;
    public string State;
    public string SkillTier;
    public float HealthFraction;
    public string WeaponShortname;
    public float AmmoFraction;
    public bool HasLineOfSight;
    public float TargetConfidence;
    public float DistanceToTarget;
    public float SecondsSinceLastSeen;
    public float SecondsSinceLastHeard;
    public int NearbyAllies;
    public int NearbyKnownEnemies;
    public bool IsStuck;
    public bool TargetIsInsideBaseRestrictedArea;
    public List<DecisionEvent> RecentEvents;
    public List<TacticalActionCandidate> CandidateActions;
}
```

### Candidate action

```csharp
private class TacticalActionCandidate
{
    public string Id;
    public TacticalActionId ActionId;
    public float HeuristicScore;
    public string Risk;
    public string ReasonFromCode;
    public Vector3 Destination;
    public ulong TargetUserId;
    public float ExpiresAt;
    public List<string> Preconditions;
    public List<string> RiskFlags;
}
```

### Expected advisor response schema

The future LLM returns structured JSON like:

```json
{
  "action_id": "flank_left",
  "confidence": 0.78,
  "ttl_ms": 3000,
  "rationale": "Target is pinned by the anchor and the left route has lower recent pressure.",
  "fallback_action_id": "hold_cover",
  "risk_flags": ["path_may_be_stale"]
}
```

The rationale is for visible debugging. It is not private chain-of-thought. The trace should preserve factual observations, candidate actions, selected action, confidence, and a short visible reason.

### Response validation

Reject the advisor response and fall back if:

```text
action_id is missing
action_id is not in CandidateActions
confidence is below Minimum Confidence
ttl_ms is too long or expired
preconditions are no longer true
selected action targets a dead/safe-zone/sleeping/disconnected player
selected destination is no longer pathable
selected destination is inside a base-restricted area
selected action violates cooldowns
response is late
response JSON is invalid
provider returned an error
provider is not configured
```

### Hard-decision triggers

Ask the advisor only when one or more triggers fire:

```text
bot is stuck
bot failed the same action multiple times
top two heuristic actions are close in score
push vs retreat decision is high impact
flank route choice is ambiguous
grenade/barricade timing is high impact
squad state changed sharply
teammate died nearby
target behavior changed
bot has not improved position for several seconds
cooldown since last advisor request has expired
```

The default baseline still records a failed advisor attempt when this happens, then uses fallback.

### Decision trace

Keep a compact action trace, not a raw thought transcript.

```csharp
private class DecisionTrace
{
    public string RequestId;
    public float CreatedAt;
    public string BotId;
    public int TeamId;
    public string State;
    public string Trigger;
    public List<DecisionEvent> RecentEvents;
    public List<TacticalActionCandidate> CandidateActions;
    public string AdvisorName;
    public string AdvisorStatus;
    public string AdvisorSelectedAction;
    public float AdvisorConfidence;
    public string AdvisorRationale;
    public string FallbackReason;
    public string FinalAction;
    public string FinalReason;
    public float LatencyMs;
}
```

Write traces to data files only when enabled:

```text
oxide/data/RaidlandsRoamBots/decision_traces.jsonl
```

Use JSONL so large logs are append-friendly and can be sampled later.

---

# Implementable Phases

The plan is split into phases that can each be implemented, loaded on a test server, and verified before moving on.

---

## Phase 0 — Safety Branch And Baseline Reproduction

### Goal

Create a safe development branch and prove the current plugin can be loaded, spawned, and diagnosed on a test server.

### Work

1. Back up:
   - `RaidlandsRoamBots.cs`
   - `RaidlandsRoamBots.json`
   - stats data
2. Create branch:
   - `playerlike-ai-rewrite-v2`
3. Run on test server only.
4. Test with:
   - 1 bot
   - 1 player anchor
   - debug enabled
5. Capture current failure modes and diagnostics.

### Done when

- Current plugin reloads on test server.
- One legacy scientist body can be spawned.
- You have a rollback copy.
- Current behavior is documented before deletion.

---

## Phase 1 — Strip Gen2, Naval, And Runtime Switching

### Goal

Remove all multi-generation AI complexity so every bot uses the same tactical path.

### Work

Delete constants:

```csharp
AiRuntimeModeGen2Native
AiRuntimeModeLegacyScientist
NativeGen2KitName
```

Delete config fields:

```csharp
AI Runtime Mode
Allow Legacy Scientist Fallback
Apply Kits To Native Gen2 Bots
```

Delete or stop using native spawn fields for v1:

```text
Prefer Native NPC Spawn Groups
Only Use Native BasePlayer Spawn Groups
Prefer Native Spawn Group Prefabs
Native NPC Spawn Group Attempts
Prefer Native NPC Spawn Points Near Players
Near Player Native Spawn Point Attempts
```

Replace prefab list with:

```json
"Prefab Candidates In Order": [
  "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab",
  "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab"
]
```

Delete methods:

```text
IsGen2NativeMode
NormalizeAiRuntimeMode
IsGen2OrNavalPrefab
IsLegacyScientistPrefab
IsNativeGen2Bot
HasGen2Components
TryPlaceGen2AgentOnNavmesh
TrySampleGen2AgentPosition
TryActivateNativeAi
ScheduleNativeAiKick
ForceNativeFsm
SeedNativeTarget
TrySetNativeDestinationToTarget
TryPlaceUnityNavMeshAgent
TryGetUnityNavMeshAgent
IsGen2UnityAgentPlaced
NativeSenseHasTarget
UnityNavMeshAgentStatus
WorldToNavMeshPosition
NavMeshToWorldPosition
FindComponentByTypeName unless needed elsewhere
FindComponentsByTypeName unless needed elsewhere
```

Replace movement dispatch:

```csharp
DriveActiveBotMovement()
```

with a placeholder:

```csharp
TacticalBrainTick()
```

For this phase, `TacticalBrainTick()` can call a simple roam placeholder.

### Done when

- No Gen2/native strings appear in config or status output.
- No Gen2 reflection code remains.
- Bots spawn only as legacy scientist bodies.
- `raidbots.status` and `raidbots.diag` no longer mention native/legacy counts.

---

## Phase 2 — New Config Schema And Migration

### Goal

Generate the new player-like config and migrate old config safely.

### Work

1. Add new config classes:
   - `AIConfig`
   - `DecisionAdvisorConfig`
   - `PersistenceConfig`
   - `DebugConfig`
2. Expand `SkillDefinition` with:
   - reaction timing
   - aim error
   - aggression
   - courage
   - tactical noise
3. Replace deprecated fields during `NormalizeConfig()`.
4. Preserve:
   - population
   - skill weights
   - team weights
   - kits
   - bot profiles
   - stats
5. Set advisor defaults:

```json
"Decision Advisor": {
  "Enabled": true,
  "Provider": "none",
  "Mode": "fallback_only",
  "Treat Unconfigured Advisor As Failure": true,
  "Fallback On Any Failure": true,
  "Shadow Mode": true
}
```

### Done when

- Old config loads without crashing.
- Saved config is clean and has no Gen2 fields.
- Advisor config exists even though no real advisor is configured.
- Test server can reload repeatedly without config churn.

---

## Phase 3 — Low-Level Body Controller

### Goal

Split the old `KickLegacyNpcMovement()` into reusable body-control primitives.

### Work

Create:

```csharp
private bool PrepareNpcBody(BaseCombatEntity bot);
private bool MoveBotTo(BaseCombatEntity bot, Vector3 destination, MovementIntent intent);
private bool StopMoving(BaseCombatEntity bot);
private bool FacePosition(BaseCombatEntity bot, Vector3 position);
private bool FaceEntity(BaseCombatEntity bot, BaseEntity target);
private bool StartBotAttack(BaseCombatEntity bot, BasePlayer target, string reason);
private bool StopBotAttack(BaseCombatEntity bot, string reason);
private bool IsWeaponReady(BaseCombatEntity bot);
private string HeldWeaponShortname(BaseCombatEntity bot);
private float AmmoFraction(BaseCombatEntity bot);
```

Keep useful low-level calls:

```csharp
NPCPlayer.Resume();
NPCPlayer.SetDestination(destination);
BaseNavigator.SetDestination(...);
BaseNavigator.SetCurrentSpeed(...);
IAIAttack.StartAttacking(target);
IAIAttack.AttackTick(...);
```

Stop doing these automatically:

```text
nearest player chase
always seed senses
always set LOS true
always start attacking nearest player
```

### Done when

- There is no all-in-one method that chooses target, moves, and attacks.
- Movement can be commanded to an arbitrary navmesh point.
- Shooting can be started/stopped explicitly.
- The bot can idle without chasing a player.

---

## Phase 4 — Tactical Runtime, State, And Diagnostics Skeleton

### Goal

Give every bot tactical state and memory, even before advanced behavior exists.

### Work

1. Add `TacticalState` enum.
2. Add `TacticalActionId` enum.
3. Expand `BotRuntime`.
4. Add `TacticalMemory`.
5. Add `CombatProfile`.
6. Add `MovementPlan`.
7. Add `SquadBlackboard` dictionary by team id.
8. Update diagnostics:

```text
state
team
target
LOS
confidence
last seen
last heard
current destination
current action
weapon
ammo
stuck
```

### Done when

- `raidbots.list` shows each bot’s tactical state.
- `raidbots.brain <bot>` shows memory and current action.
- The bot still spawns and can be moved by a simple placeholder state.

---

## Phase 5 — Decision Arbiter Skeleton With Forced Advisor Failure

### Goal

Build the LLM-ready decision path immediately, but make it fail closed and use heuristic fallback.

### Work

Add:

```csharp
DecisionRequest
DecisionEvent
TacticalActionCandidate
DecisionAdvisorResult
DecisionTrace
IDecisionAdvisor
NullDecisionAdvisor
DecisionResponseValidator
FallbackHeuristicPolicy
DecisionArbiter
```

Baseline flow:

```csharp
private TacticalDecision ChooseDecision(BaseCombatEntity bot, BotRuntime runtime)
{
    var candidates = BuildCandidateActions(bot, runtime);
    ScoreCandidatesHeuristically(bot, runtime, candidates);

    var request = BuildDecisionRequest(bot, runtime, candidates);

    return decisionArbiter.DecideOrFallback(bot, runtime, request, candidates);
}
```

`NullDecisionAdvisor` always returns:

```json
{
  "status": "failure",
  "reason": "advisor_not_configured"
}
```

The arbiter then chooses the best valid heuristic action and logs:

```text
advisor=none
advisorStatus=advisor_not_configured
fallbackReason=advisor_not_configured
finalAction=<heuristic winner>
```

### Important

Even though the advisor fails, this is not wasted work. This phase guarantees that every future LLM response must pass through validation and fallback logic.

### Done when

- Every tactical decision produces candidate actions.
- Advisor failure is visible in debug logs.
- Fallback action is chosen every time.
- Bot behavior does not depend on a network service.
- No server-thread blocking exists.

---

## Phase 6 — Perception V1: Vision, LOS, And Target Memory

### Goal

Remove wallhack behavior and make bots only shoot what they can actually see.

### Work

Every perception tick:

1. Find real players within vision range.
2. Skip dead, sleeping, disconnected, NPC, ignored safe-zone players.
3. Apply field-of-view check.
4. Raycast to head/chest/hips sample points.
5. Calculate visibility score.
6. Select best visible target.
7. Update memory only on plugin-confirmed visibility.
8. Clear `HasLineOfSight` when raycasts fail.

Pseudo-code:

```csharp
private void UpdateVision(BaseCombatEntity bot, BotRuntime runtime)
{
    var visible = FindVisiblePlayers(bot, runtime);
    var best = SelectBestVisibleTarget(runtime, visible);

    if (best != null)
        MarkTargetSeen(runtime, best.Player, best.VisiblePosition);
    else
        runtime.Memory.HasLineOfSight = false;
}
```

### Shooting rule

The combat controller can shoot only when:

```text
target exists
target is valid
LOS is true
reaction delay passed
state allows shooting
weapon is ready
bot is not tucking or retreating
```

### Done when

- Bot does not shoot hidden players.
- Bot stops shooting after LOS is lost.
- Bot remembers `LastSeenPosition`.
- Diagnostic output clearly shows LOS true/false.

---

## Phase 7 — Stimulus Bus And Hearing V1

### Goal

Make bots react to sound and damage without live tracking hidden players.

### Work

Add:

```csharp
private enum StimulusType
{
    Gunshot,
    SuppressedGunshot,
    Explosion,
    MeleeImpact,
    ToolImpact,
    SprintFootstepApproximation,
    BotDamaged,
    TeammateDamaged,
    TeammateSawEnemy,
    BarricadePlaced,
    GrenadeThrown
}
```

Add:

```csharp
private class Stimulus
{
    public StimulusType Type;
    public Vector3 Position;
    public BasePlayer SourcePlayer;
    public BaseEntity SourceEntity;
    public float Loudness;
    public float Confidence;
    public float CreatedAt;
    public float ExpiresAt;
}
```

First reliable stimuli:

```text
OnEntityTakeDamage when bot is damaged
OnEntityTakeDamage when teammate is damaged
explosion or projectile hooks where available
optional low-frequency noisy movement polling later
```

Sound must update approximate position only:

```text
LastHeardPosition = sound position + error offset
LastHeardAt = now
TargetConfidence = sound confidence
State can become InvestigateSound
```

Do not set live target LOS from sound alone.

### Done when

- Player fires or damages a bot from behind cover.
- Bot moves toward approximate sound/damage location.
- Bot does not shoot unless it later gets LOS.
- `raidbots.brain` shows last heard position and confidence.

---

## Phase 8 — Baseline State Machine

### Goal

Implement the first playable tactical states without advanced cover yet.

### States for this phase

```text
Roam
InvestigateSound
SearchLastKnown
AcquireTarget
FightFromCover basic fallback
Push basic
Retreat basic
```

### Candidate action examples

`Roam`:

```text
RoamToPoint
InvestigateSound if recent stimulus
AcquireVisibleTarget if LOS
```

`InvestigateSound`:

```text
Move to LastHeardPosition
Scan nearby
AcquireVisibleTarget if LOS
Roam if timeout
```

`SearchLastKnown`:

```text
Move near LastSeenPosition
Scan likely exits
AcquireVisibleTarget if LOS
Roam if timeout
```

`AcquireTarget`:

```text
Face target
Wait reaction delay
Start shooting if clear
MoveToCover if exposed
PushTarget if close and target exposed
```

### Done when

- Bot roams when nothing is happening.
- Bot investigates sound.
- Bot acquires visible target after reaction delay.
- Bot searches after LOS loss.
- Bot can push or retreat using simple heuristics.
- All choices flow through the decision arbiter and fallback heuristic.

---

## Phase 9 — Cover Planner V1

### Goal

Make bots use terrain/object cover and do simple peek/tuck behavior.

### Work

Generate cover candidates around bot and fight area:

```text
rings at 6m, 10m, 16m, 24m
angle samples every 30-45 degrees
navmesh sample each candidate
raycast target -> candidate to test hidden tuck point
raycast candidate peek -> target to test firing line
skip safe zone
skip underwater
skip base restricted
skip teammate occupied points
```

Score cover:

```text
+ pathable
+ hidden from target
+ has at least one peek point
+ weapon-appropriate distance
+ not occupied by teammate
+ not inside base-restricted area
- too close to target
- too far from fight
- exposed to known enemy angle
```

Implement peek loop:

```text
Tuck
Wait 0.45-1.6s
Pick left/right peek
Move to peek
Expose 0.35-1.15s
Shoot if LOS
Return to tuck
Maybe swap side
Maybe reposition
```

### Done when

- Bot moves to cover after being shot or seeing enemy.
- Bot tucks when LOS is lost or while waiting.
- Bot peeks briefly and fires only during exposure.
- Bot does not stand still in the open every fight.

---

## Phase 10 — Combat Controller Polish

### Goal

Make shooting behavior feel less like a vanilla NPC and more like a player.

### Work

Add weapon profiles:

```text
AK / LR / SAR / M39: medium-range cover burst
MP5 / Thompson / Custom: close distance and flank/push
Bolt / L96: distance, reposition after shot
M249: suppress and hold angle
Pistol: avoid long range, use cover heavily
```

Add skill-based behavior:

```text
reaction delay
burst length
peek exposure timing
retreat threshold
push threshold
cover search quality
reposition frequency
grenade confidence later
```

Shooting must stop when:

```text
LOS lost
target invalid
state is not a shooting state
bot is tucking
bot is retreating
bot is throwing grenade/smoke
```

### Done when

- Casual, average, and dangerous bots feel behaviorally different.
- Bot does not simply win through health/damage numbers.
- Weapon kit changes behavior.
- Attack start/stop reasons appear in debug logs.

---

## Phase 11 — Stuck Detection And Movement Resilience

### Goal

Prevent bots from freezing when pathing fails.

### Work

Track movement progress:

```text
if destination exists
and bot moved < 0.75m over 3 seconds
and bot is not intentionally holding/peeking/shooting
then mark stuck
```

On stuck:

```text
increment failed path count
clear destination
try nearby navmesh point
avoid same failed destination for 15s
choose alternate cover/search/retreat point
trigger hard-decision advisor path
fallback heuristic still picks action because advisor is unconfigured
```

### Done when

- Stuck bots recover to another nearby point.
- Stuck state appears in diagnostics.
- Hard-decision trace records `bot_stuck` trigger.

---

## Phase 12 — Squad Blackboard V1

### Goal

Make duo/trio bots coordinate without giving every bot wallhack shooting.

### Work

Create squad memory:

```text
known enemies
last seen positions
last heard positions
who has LOS
current roles
current cover occupancy
team grenade cooldown
push/regroup calls
```

Assign roles:

```text
solo: self-cover
duo: anchor + flanker
trio: anchor + flanker + pusher/grenadier
```

Rules:

```text
only bot with LOS shoots
teammates can investigate or flank toward shared last-known info
teammates do not magically shoot hidden target
teammates avoid same cover
teammates avoid grenade danger zones
```

### Done when

- One bot can suppress while another flanks.
- Teammates share last-known location but not LOS.
- Duo/trio fights feel different from independent solos.

---

## Phase 13 — Grenades, Smoke, And Barricades

### Goal

Add player-like utility use after baseline shooting/cover/squad behavior works.

### Work

Equipment helpers:

```csharp
private bool TryThrowBotUtility(BaseCombatEntity bot, BotRuntime runtime, Vector3 impactPosition, bool smoke, float now);
private bool TryAddGrenadeCandidate(List<TacticalActionCandidate> candidates, BaseCombatEntity bot, BotRuntime runtime, BasePlayer target, Vector3 knownThreatPosition, float healthFraction, bool hasFreshSeen, bool hasFreshHeard, bool hasRecentContact, float now);
private bool TryAddSmokeCandidate(List<TacticalActionCandidate> candidates, BaseCombatEntity bot, BotRuntime runtime, BasePlayer target, Vector3 knownThreatPosition, float healthFraction, bool lowHealthAware, bool atCover, bool hasFreshSeen, bool hasFreshHeard, bool hasRecentContact, float now);
private bool TryPlaceBarricade(BaseCombatEntity bot, BotRuntime runtime, Vector3 position, Vector3 threatPosition);
```

As of v0.3.25, grenade and smoke utility is real-entity utility driven by config and tactical legality, not by bot inventory item ownership. Real med-item/inventory animation is still later work.

Grenade conditions:

```text
target last seen recently
target is behind cover
range 12m-42m by default
team grenade cooldown ready
no teammate near blast point
no non-target bystander player near blast point
target point is not base-restricted and the path to it does not cross a base-restricted area
active bot utility projectile cap has room
```

Smoke conditions:

```text
crossing open ground
retreating from long-range fire
regrouping under pressure
hurt or under pressure
not already in effective cover
range 10m-55m by default
target screen point is not base-restricted and does not cross a base-restricted path
```

Barricade conditions:

```text
bot took damage recently
bot is exposed
no natural cover nearby
cooldown ready
placement on valid ground
not safe zone
not base-restricted if disallowed
```

Barricade behavior:

```text
place between bot and threat
move to protected side
peek left/right
hold until confidence improves
push, flank, retreat, or fight from cover
```

### Done when

- Bots occasionally throw grenades in plausible situations.
- Bots occasionally throw smoke when hurt or pressured in the open.
- Bots avoid throwing grenades onto teammates or bystanders.
- Bots inside fresh bot grenade danger zones try to move clear.
- Bots place barricades when caught in open ground.
- Entities remain real.
- Cooldowns prevent spam.

---

## Phase 14 — Base Avoidance V1

### Goal

Keep bots out of player bases until a future base assault module exists.

### Work

Conservative checks:

```text
near BuildingPrivlidge if detectable
near dense player building blocks / doors / deployables
enclosed interior ray checks
nav points too close to walls/foundations/doors
```

API:

```csharp
private bool IsBaseRestrictedPosition(Vector3 position);
private bool SegmentCrossesBaseRestrictedArea(Vector3 from, Vector3 to);
private bool TryFindOutsideBaseHoldPoint(Vector3 botPosition, Vector3 threatPosition, out Vector3 holdPoint);
```

Behavior:

```text
if target enters restricted area -> HoldOutsideBase
if path crosses restricted area -> choose alternate or abandon
hold outside briefly
watch exits
eventually return to roam
```

### Done when

- Bot does not chase into bases.
- Bot does not repeatedly path into a door/wall.
- Bot can hold outside and eventually leave.

---

## Phase 15 — Real Entity Persistence And Admin Safety

### Goal

Make bots behave more like real players while retaining emergency admin control.

### Work

Change disable behavior:

```text
raidbots.disable stops spawning and brain ticks
does not kill existing bots by default
```

Add emergency commands:

```text
raidbots.nuke
raidbots.nuke active
raidbots.nuke debug
```

For v1, only active tracked bots need to be nuked. Broad cleanup for old barricades or corpses can be added later if needed.

Change leash behavior:

```text
no combat leash despawn
behavioral return/regroup instead
```

### Done when

- Bots are not killed just because they fought too long.
- Bot corpses/loot can remain.
- Barricades can remain.
- Server operators still have an emergency reset command.

---

## Phase 16 — Baseline Playable Milestone

### Goal

Reach a complete, fun, non-LLM baseline.

### Include

```text
single legacy body
kits
spawn near players
roam
hearing from damage/gunshots/explosions where available
LOS-based target acquisition
last-seen memory
stop shooting on LOS loss
search last known
basic cover
basic peek/tuck
basic push/retreat
stuck recovery
simple squad memory
base avoidance
DecisionAdvisor skeleton with forced failure
heuristic fallback decisions
advisor decision traces
```

### Exclude until after this milestone

```text
real HTTP LLM calls
local model hosting
website proxy
LLM-executed actions
complex jump peeking
base assault module
advanced raid logic
```

### Done when

- The bot is fun to fight without any LLM.
- Decision traces show advisor failures and fallback actions.
- The fallback system is robust enough that LLM failure never breaks behavior.

---

# LLM Integration Phases

Only start these after the baseline playable milestone is stable.

---

## Phase 17 — Decision Trace Dataset And Review Tools

### Goal

Collect the data needed to decide whether LLM assistance is useful.

### Work

1. Save decision traces in JSONL.
2. Add sampling so logs do not explode.
3. Add admin command:

```text
raidbots.decisions last
raidbots.decisions bot <number>
raidbots.decisions export
```

4. Add trace fields:

```text
advisor requested?
advisor status
fallback reason
candidate scores
final action
result after 3-10 seconds
bot survived?
target visible?
bot got unstuck?
bot improved cover?
```

5. Add manual review tags later:

```text
human_preferred_action
bad_candidate_set
bad_heuristic_score
bad_perception
bad_pathing
```

### Done when

- You can inspect the exact state and candidates behind a tactical choice.
- You can build a small set of hard decisions for offline evaluation.

---

## Phase 18 — OpenAI-Compatible Advisor Interface, Still Disabled By Default

### Goal

Implement the network adapter without letting it control bots yet.

### Work

Add provider enum/string:

```text
none
openai_compatible
website_proxy
```

Add non-blocking HTTP advisor:

```csharp
private class OpenAiCompatibleDecisionAdvisor : IDecisionAdvisor
{
    public bool TrySubmit(DecisionRequest request, Action<DecisionAdvisorResult> callback)
    {
        // Build JSON body.
        // Use webrequest.Enqueue.
        // Parse response.
        // Callback result.
        // Never block server thread.
    }
}
```

Config remains default:

```json
"Provider": "none",
"Mode": "fallback_only"
```

Test with fake endpoint first:

```text
valid response
invalid JSON
unknown action
low confidence
late response
HTTP failure
provider timeout
```

### Done when

- Adapter compiles.
- Timeout and error handling work.
- Invalid responses always fall back.
- No live bot behavior changes yet.

---

## Phase 19 — Shadow Mode LLM

### Goal

Let the LLM make recommendations without executing them.

### Work

Config:

```json
"Provider": "openai_compatible",
"Mode": "shadow",
"Shadow Mode": true
```

Behavior:

```text
heuristic action executes
LLM action is logged only
trace records disagreement
trace records whether LLM action would have been valid
```

Metrics:

```text
request count
success count
invalid JSON rate
invalid action rate
low confidence rate
timeout rate
latency p50/p95
LLM vs heuristic disagreement
would-have-selected action
rough outcome after decision
```

### Done when

- LLM can be queried without changing gameplay.
- You can compare LLM choices against heuristic choices.
- You can identify if candidate action generation is too weak.

---

## Phase 20 — Offline Evaluation And Candidate-Set Improvement

### Goal

Improve prompts, schemas, candidates, and heuristics before live control.

### Work

1. Export hard-decision traces.
2. Review manually.
3. Label preferred actions for some cases.
4. Compare:

```text
heuristic winner
LLM winner
human preferred action
actual observed outcome
```

5. Improve candidate generation if the right action was not available.
6. Improve heuristic scoring if the right action was available but ranked poorly.
7. Improve prompt/schema only after candidate generation is good.

### Success target

LLM should only move toward live control if:

```text
invalid output rate is very low
latency is acceptable
LLM picks human-preferred action in meaningful hard cases
LLM does not choose risky invalid moves
fallback still handles all failures
```

A practical first bar:

```text
LLM recommendations are preferred over heuristic in 15-25% of reviewed hard-decision cases
invalid action/JSON rate remains under 1-2%
p95 latency is within configured tolerance for non-real-time choices
```

### Done when

- You have evidence that the LLM improves some decisions.
- You know which decision types are worth asking about.
- You know which decision types should stay purely heuristic.

---

## Phase 21 — Canary Control For Low-Risk Decisions

### Goal

Allow the LLM to control only low-risk choices for a tiny subset of bots.

### Work

Config:

```json
"Mode": "canary",
"Canary Bot Percentage": 5,
"Allowed Live Decision Kinds": [
  "choose_flank_side",
  "choose_search_point",
  "choose_cover_reposition",
  "choose_regroup_or_hold"
]
```

Still do not allow LLM control for:

```text
shooting timing
aiming
raw movement steering
cooldowns
grenade throws near teammates
barricade placement spam
base entry
admin actions
entity cleanup
```

Execution rule:

```text
LLM selected action executes only if validator approves it immediately before execution
```

### Done when

- Only canary bots can execute LLM decisions.
- All actions remain allowlisted and validated.
- Failures or late responses fall back silently.
- Admin can disable advisor instantly.

---

## Phase 22 — Expand Live Advisor To High-Impact Choices

### Goal

Use the advisor for selected tactical choices where it proved useful.

### Candidate live decisions

```text
flank left vs flank right
hold cover vs reposition
push vs retreat when scores are close
which last-known point to search first
whether to regroup after teammate death
whether to abandon target outside base
```

Still deterministic:

```text
shooting
LOS checks
path validation
cooldowns
friendly fire checks
entity placement
```

### Done when

- LLM makes occasional strategic choices.
- Bots still work perfectly with advisor disabled.
- Fallback rate, latency, and invalid output are monitored.

---

## Phase 23 — Local LLM Sidecar Option

### Goal

Allow a local model service to act as the same OpenAI-compatible advisor.

### Work

Do not embed a model in the plugin process. Use a sidecar HTTP service.

Config example:

```json
"Decision Advisor": {
  "Provider": "openai_compatible",
  "Endpoint Url": "http://127.0.0.1:8080/v1/chat/completions",
  "Api Key": "local-not-needed-or-token",
  "Model": "local-tactical-advisor",
  "Timeout Milliseconds": 750,
  "Mode": "shadow"
}
```

Compare local sidecar against hosted/API model using the same trace set:

```text
same request
same candidates
same schema
same validator
same metrics
```

### Done when

- Local and hosted providers are swappable.
- The plugin does not care which model produced the response.
- Local model can be tested in shadow before canary.

---

## Phase 24 — Website Proxy Option

### Goal

Support a website/backend proxy only if product/ops needs justify it.

### Use proxy when you need:

```text
centralized billing
server identity/auth
feature flags
rate limits
audit logs
model routing
customer/server quotas
API key isolation
cross-server analytics
```

### Avoid proxy when:

```text
you only need a first prototype
you want minimum latency
your Rust server can safely hold a key
you are testing local sidecar only
```

### Done when

- The same `IDecisionAdvisor` interface supports direct and proxy endpoints.
- Proxy failures fall back exactly like local/API failures.

---

## Phase 25 — Production Advisor Guardrails

### Goal

Make LLM use safe, bounded, inspectable, and optional.

### Required guardrails

```text
advisor can be disabled live
fallback works on every failure
max concurrent requests
per-bot request cooldown
team request cooldown if needed
timeout
TTL
confidence threshold
action allowlist
precondition validation
late-response discard
schema validation
candidate-only selection
safe-zone/base-zone restrictions
friendly-fire restrictions
cost/latency logs
```

### Admin commands

```text
raidbots.advisor status
raidbots.advisor off
raidbots.advisor shadow
raidbots.advisor canary
raidbots.advisor fallback
raidbots.advisor stats
raidbots.advisor last <bot>
```

### Done when

- The LLM is useful but never required.
- Server owners can disable it instantly.
- Invalid outputs cannot directly affect bot behavior.
- The bot remains fully playable in `Provider = none` mode.

---

# Testing Ladder

## Test 1 — Single Body Spawn

Expected:

```text
legacy scientist body only
kit applied
no Gen2 diagnostics
no native fallback warnings
```

## Test 2 — Idle Roam

Expected:

```text
bot state = Roam
bot picks navmesh roam points
bot does not chase hidden nearest player
```

## Test 3 — Advisor Skeleton Fallback

Expected:

```text
hard decision trigger fires
advisor = none
advisorStatus = advisor_not_configured
fallback action selected
bot continues normally
```

## Test 4 — Hearing Without LOS

Expected:

```text
player fires from behind cover
bot state = InvestigateSound
bot moves toward approximate sound
bot does not shoot
```

## Test 5 — Visual Contact

Expected:

```text
player enters LOS
bot state = AcquireTarget
reaction delay occurs
bot shoots only after LOS + delay
```

## Test 6 — Lost LOS

Expected:

```text
player breaks LOS
bot stops shooting
bot searches last seen area
bot does not track exact hidden live position
```

## Test 7 — Cover And Peek

Expected:

```text
bot finds cover
bot tucks
bot peeks briefly
bot fires only during exposure with LOS
```

## Test 8 — Stuck Recovery

Expected:

```text
bot fails path
stuck trigger fires
advisor failure logged
fallback chooses alternate path
bot recovers
```

## Test 9 — Duo Tactics

Expected:

```text
one bot anchors
one bot flanks
flanker uses shared last-known info but does not shoot without LOS
```

## Test 10 — Barricade

Expected:

```text
bot takes damage in open
no natural cover nearby
bot places barricade
bot uses barricade as cover
barricade remains
```

## Test 11 — Base Boundary

Expected:

```text
player enters base
bot does not enter
bot holds outside or abandons
bot does not freeze against walls/doors
```

## Test 12 — Shadow LLM Later

Expected after LLM phase:

```text
heuristic action executes
LLM recommendation logged
invalid/late/low-confidence response rejected
no gameplay change in shadow mode
```

## Test 13 — Canary LLM Later

Expected after canary phase:

```text
only canary bots execute selected validated LLM actions
fallback still works
admin can disable advisor instantly
```

---

# Commit Roadmap

## Commit 1 — Single Body Cleanup

```text
remove Gen2/native config
remove Gen2 methods
remove runtime AI switching
legacy scientist prefabs only
clean status/diag output
```

## Commit 2 — Config v2

```text
new AI config
new Decision Advisor config
new Persistence config
migration from old config
```

## Commit 3 — Body Controller

```text
PrepareNpcBody
MoveBotTo
FaceEntity/FacePosition
StartBotAttack
StopBotAttack
weapon diagnostics
```

## Commit 4 — Tactical Runtime

```text
TacticalState
TacticalActionId
expanded BotRuntime
TacticalMemory
SquadBlackboard
brain diagnostics
```

## Commit 5 — Decision Skeleton

```text
DecisionRequest
TacticalActionCandidate
DecisionArbiter
NullDecisionAdvisor
FallbackHeuristicPolicy
DecisionTrace
advisor_not_configured fallback path
```

## Commit 6 — Perception V1

```text
candidate players
FOV
LOS raycasts
target memory
shooting requires LOS
```

## Commit 7 — Hearing V1

```text
StimulusBus
bot damage stimulus
teammate damage stimulus
basic sound investigation
```

## Commit 8 — State Machine V1

```text
Roam
InvestigateSound
SearchLastKnown
AcquireTarget
basic FightFromCover
basic Push
basic Retreat
```

## Commit 9 — Cover V1

```text
cover candidate generation
cover scoring
peek/tuck points
basic jiggle approximation
```

## Commit 10 — Combat Polish

```text
weapon profiles
reaction tuning
burst windows
attack stop reasons
skill behavior tuning
```

## Commit 11 — Stuck Recovery

```text
movement progress tracking
failed path memory
alternate destination fallback
stuck decision traces
```

## Commit 12 — Squad V1

```text
squad blackboard
anchor/flanker roles
shared last-known positions
cover occupancy
```

## Commit 13 — Equipment

```text
grenades
smoke
barricades
cooldowns
friendly safety checks
```

## Commit 14 — Base Avoidance

```text
base restricted checks
hold outside base
abandon base target
no base entry
```

## Commit 15 — Persistence And Admin Safety

```text
disable without killing bots
unload config
emergency nuke
no combat leash despawn
```

## Commit 16 — Baseline Playable Tuning

```text
test ladder
debug commands
performance checks
skill tuning
spawn tuning
```

## Commit 17 — Decision Trace Dataset

```text
JSONL decision traces
admin export
result tracking
review workflow
```

## Commit 18 — HTTP Advisor Adapter

```text
OpenAI-compatible endpoint
non-blocking requests
timeouts
schema parse
validator tests
still default disabled
```

## Commit 19 — Shadow Advisor

```text
shadow mode
LLM recommendation logging
heuristic still executes
comparison metrics
```

## Commit 20 — Canary Advisor

```text
small bot percentage
allowlisted decision kinds
validated execution only
instant disable
```

## Commit 21 — Local Sidecar / Proxy Options

```text
local OpenAI-compatible sidecar config
website proxy config
provider comparison
production guardrails
```

---

# Deletion Checklist

Delete or heavily rewrite:

```text
AiRuntimeModeGen2Native
AiRuntimeModeLegacyScientist
NativeGen2KitName
config.AiRuntimeMode
config.AllowLegacyScientistFallback
config.ApplyKitsToNativeGen2Bots
NativeGen2 field on BotRuntime
AiMode field on BotRuntime
IsGen2NativeMode
NormalizeAiRuntimeMode
IsGen2OrNavalPrefab
IsLegacyScientistPrefab
IsNativeGen2Bot
HasGen2Components
TryPlaceGen2AgentOnNavmesh
TrySampleGen2AgentPosition
TryActivateNativeAi
ScheduleNativeAiKick
ForceNativeFsm
SeedNativeTarget
TrySetNativeDestinationToTarget
TryPlaceUnityNavMeshAgent
TryGetUnityNavMeshAgent
IsGen2UnityAgentPlaced
NativeSenseHasTarget
UnityNavMeshAgentStatus
WorldToNavMeshPosition
NavMeshToWorldPosition
DriveActiveBotMovement
KickLegacyNpcMovement as all-in-one behavior
LegacyMoveDestination as nearest-player chase
HandleLeashes as despawn-on-combat-distance
```

Keep and adapt:

```text
RefreshEligibleKits
ApplyKit
ChooseKit
SkillFor
SpawnBots
TrySpawnBot
TryFindNearPlayerSpawnPosition
TryFindRandomLandSpawnPosition
TryFindNearbyPosition
TryPlaceBotOnOwnNavmesh
OnEntityTakeDamage
OnEntityDeath
OnEntityKill
Scoreboard integration
Bot profile names
Admin permissions
Safe-zone filters
Navmesh sample helpers
Stats data
```

---

# Acceptance Criteria

## Baseline v1 success

The rewrite is successful before LLM work when:

```text
no Gen2/native switching remains
bots spawn as legacy scientist bodies with Raidlands kits
bots do not immediately know every nearby player through walls
bots investigate gunfire/damage without shooting through terrain
bots shoot only with line of sight
bots lose targets and search last-known positions
bots use basic cover
bots peek/tuck from cover
bots push when they have advantage
bots retreat/reposition when low health or exposed
squads share memory without wallhack shooting
bots avoid entering player bases
bot deaths/stats still work
bot-created entities can remain
DecisionAdvisor skeleton exists
advisor_not_configured fallback works
admins have emergency commands and diagnostics
```

## LLM-ready success

The architecture is LLM-ready when:

```text
every hard tactical choice can be represented as legal candidate actions
DecisionRequest contains compact factual state
DecisionTrace records factual action history
NullDecisionAdvisor can fail every time without breaking behavior
ResponseValidator rejects invalid/unsafe/stale choices
FallbackHeuristicPolicy always produces a safe action
HTTP advisor can be added without rewriting tactical states
```

## LLM integration success

The LLM integration is worth keeping only when:

```text
shadow-mode decisions are inspectable
invalid output rate is low
latency is acceptable
LLM improves reviewed hard decisions enough to matter
canary mode does not degrade gameplay
fallback behavior remains complete
provider can be disabled live
```

---

# Future Module: BaseAssaultModule

Do not build this now.

Future scope:

```text
detect external doors
detect compound gates
path to breach points
decide whether to raid
use explosives
avoid traps
navigate ladders/jump-ups
clear rooms
handle roof campers
respect TC/raid rules
```

Future module shape:

```csharp
private interface ITacticalModule
{
    bool WantsControl(BaseCombatEntity bot, BotRuntime runtime);
    TacticalDecision Evaluate(BaseCombatEntity bot, BotRuntime runtime);
}
```

For now, only implement:

```text
BaseAvoidance
HoldOutsideBase
AbandonBaseTarget
```

---

# Summary

The practical path is:

```text
1. delete Gen2/native complexity
2. keep spawn/kit/stats/admin foundations
3. split body movement/combat primitives
4. add tactical state and memory
5. add an LLM-ready decision arbiter immediately
6. use NullDecisionAdvisor so advisor attempts fail and heuristics run
7. build perception, hearing, cover, peeking, squad behavior, utility use, and base avoidance
8. reach a fun baseline with no LLM dependency
9. collect decision traces
10. add OpenAI-compatible/local/proxy advisor only in shadow mode
11. move to canary only after metrics prove value
```

This lets the bots become believable Rust roamers now, while leaving a clean, safe seam for future LLM-driven high-level tactical judgment.

---

# Implementation Progress

## Current Progress Through v0.3.25 Player-Like Health / Damage Balance

### Files updated

```text
oxide/plugins/RaidlandsRoamBots.cs
oxide/config/RaidlandsRoamBots.json
Docs/RaidlandsRoamBots_Tactical_Rewrite_Plan_v2_LLM.md
UPDATED_FILES_FOR_UPLOAD.txt
```

### Current code and config snapshot

```text
Plugin version: RaidlandsRoamBots v0.3.25
Brain mode: playerlike_tactical_brain
Current checked-in config: disabled by default, target=3, min=1, max=3
Current checked-in test profile: near-player squad test anchored to ababmxking, random land fallback disabled, debug nameplates and side panel enabled
Current body candidates: scientistnpc_roam, scientistnpc_full_any, scientistnpc_junkpile_pistol
Current stats data shape: players, bots, bot_clans
Current decision trace path: oxide/data/RaidlandsRoamBots/decision_traces.jsonl
Required admin permission: raidlandsroambots.admin
```

The checked-in config is a live-test convenience profile, not the canonical production population target. For a broader live rollout, clear or change the near-player anchor, decide whether random land fallback should return, and raise the population limits intentionally.

### Current admin command surface

```text
raidbots.status
raidbots.enable [target]
raidbots.disable
raidbots.reload
raidbots.spawn [count]
raidbots.diag
raidbots.testsetup [optional-player-name-or-steamid]
raidbots.squadtest [optional-player-name-or-steamid]
raidbots.nuke [active|debug|all]
raidbots.debug on|off
raidbots.decisions [last [count]|bot <name/key> [count]|export]
raidbots.land on|off
raidbots.target [population]
raidbots.mode [near_players|random]
raidbots.anchor [name-or-steamid|clear]
raidbots.list [optional-player-name-or-steamid]
raidbots.goto <player-name-or-steamid> [bot-number]
raidbots.killall
```

Current diagnostics intentionally expose `brain`, anchor/debug-viewer counts, clan tag, squad role, base-restricted state, LOS/exposure probes, weapon/ammo class, cover/flank points, active barricade count, utility status, stuck/nav details, and target status. The debug UI is split into a compact overhead line plus a right-side closest-bot panel with `Signal`, `Action`, `Cover`, `Wall`, `Utility`, `Sight`, `Fire`, and `Heal` details.

### Implemented

- Phase 1 mostly complete:
  - Removed active Gen2/native runtime mode, native spawn-group use, runtime AI switching, Gen2 activation/kick code, native status output, and native admin toggles.
  - Config now uses only legacy scientist body prefab candidates.
  - Kept `scientistnpc_junkpile_pistol.prefab` as a third legacy body candidate because prior live testing showed it was the known working legacy body when `scientistnpc_roam` and `scientistnpc_full_any` could not place their navigator.

- Phase 2 baseline complete:
  - Added v2 config sections for `AI`, `Decision Advisor`, `Persistence`, and `Debug`.
  - Config migration on save filters prefab candidates down to legacy scientist body prefabs.
  - Updated checked-in config to the v2 shape while preserving the current near-player squad-test setup.

- Phase 3 baseline complete:
  - Split legacy body handling into explicit primitives:
    - `PrepareNpcBody`
    - `MoveBotTo`
    - `FaceEntity`
    - `FacePosition`
    - `StartBotAttack`
    - `StopBotAttack`
  - The legacy scientist is now treated as the physical body, not as the tactical brain.

- Phase 4 baseline complete:
  - Added `TacticalState`, `TacticalActionId`, expanded `BotRuntime`, `TacticalMemory`, `CombatProfile`, `MovementPlan`, `DecisionContext`, `EnemyMemory`, and `SquadBlackboard`.
  - Replaced the old movement timer with staggered maintain, perception, brain, squad, and scoreboard timers.

- Phase 5 baseline complete:
  - Added `IDecisionAdvisor`, `NullDecisionAdvisor`, `DecisionRequest`, `TacticalActionCandidate`, `DecisionAdvisorResult`, `TacticalDecision`, and JSONL decision-trace writing.
  - `Provider = none` produces `advisor_not_configured` and falls back to heuristic decisions.

- Phase 6 partial:
  - Added candidate-player filtering, FOV checks, plugin-controlled line-of-sight checks, target memory, reaction delay, and LOS-required shooting.
  - Bots stop attacking when LOS is lost.
  - Live-test fix on 2026-07-05: foliage/resource occlusion now participates in LOS checks so trees, bushes, and resource cover can break vision instead of allowing long-range shooting through forest cover.
  - Live-test fix on 2026-07-05: normal body preparation now suppresses the underlying `ScientistBrain` player target memory unless the Raidlands tactical brain explicitly starts an attack.

- Phase 7 partial:
  - Player damage against a bot records a damage/hearing stimulus so the bot can investigate without live-position wall tracking.

- Phase 8 partial:
  - Added basic heuristic actions for roam, investigate sound, search last known, acquire visible target, push visible target, retreat at low health, and abandon target.

- Phase 9 baseline:
  - Added multi-point target visibility so a single visible head/eye ray no longer counts as full target acquisition.
  - Added `Minimum Exposed Target Fraction` and `Minimum Exposed Target Fraction To Shoot` config, with diagnostics showing exposure probe counts in `raidbots.list`.
  - Added cover candidate sampling around the fight, target-to-cover occlusion scoring, basic tuck points, and left/right peek points.
  - Added `MoveToCover`, `PeekLeft`/`PeekRight`, and `Tuck` execution paths that stop shooting while tucking and only fire during valid exposure windows.

- Phase 10 baseline:
  - Added weapon-derived combat profiles for shotgun, pistol, SMG, rifle, marksman, sniper, and LMG-style weapons.
  - Bots now prefer pushing closer when their weapon is outside preferred/ideal range instead of always standing still and firing.
  - Poor range affects action scoring and repositioning only; bot weapon damage is left at Rust's normal hit damage.

- Phase 11 baseline:
  - Fixed stuck detection so repeated commands to the same destination no longer reset movement progress.
  - Added latched stuck state, repeated failure counts, `stuck_recovery` decision flags, alternate nearby navmesh recovery destinations, and `stuck=true/false` diagnostics.

- Phase 12 baseline:
  - Squad blackboards now assign `solo`, `anchor`, `flanker`, and `pusher` roles.
  - Squads share last-known enemy positions and LOS counts without granting magical shooting; only a bot with its own plugin-confirmed LOS can fire.
  - Flanker/pusher bots can choose flank points from either direct contact or squad-shared last-known memory.
  - Teammates avoid claiming the same nearby cover point when the cover planner can find alternatives.

- Phase 13 partial:
  - Added bounded real barricade placement for damaged/exposed bots caught in open ground.
  - Added the double Wooden Barricade Cover prefab, max-active-barricade cap, oldest-wall recycling, placement distance, hold/fight-commitment time, retreat-wall distance, and cooldown config.

- Phase 13/10 utility baseline:
  - v0.3.24 adds real F1 grenade and smoke grenade utility actions behind deterministic candidate checks.
  - `throw_grenade` can flush covered or last-known targets inside the configured throw range, while avoiding base-restricted positions, same-team bots, and non-target bystander players.
  - `throw_smoke` can screen a hurt or pressured bot's retreat lane when the bot is not already in effective cover.
  - Utility throws use a shared per-bot cooldown, team cooldown, active utility projectile cap, creator/owner attribution, and side-panel/`raidbots.list` utility diagnostics.
  - Fresh bot F1 throws create short-lived danger zones; bots inside the zone get a high-priority escape candidate, and movement commands avoid active grenade danger destinations.

- Phase 14 baseline:
  - Added conservative base-restricted position detection around cupboards, building blocks, doors, and owned base-like deployables.
  - Spawn, tactical destination sampling, and movement commands now avoid base-restricted areas.
  - If a target is inside a base boundary or the path would cross one, the bot chooses `HoldOutsideBase` / abandon behavior instead of chasing or shooting inside.

- Phase 15 partial:
  - `raidbots.disable` stops runtime ticks and does not kill tracked bots by default.
  - Added `raidbots.nuke` for emergency removal of tracked bots.
  - `raidbots.nuke active`, `raidbots.nuke debug`, and `raidbots.nuke all` are now explicit admin safety modes.
  - Removed combat-leash despawn behavior.

- Phase 17 partial:
  - Added `raidbots.decisions last [count]`, `raidbots.decisions bot <name/key> [count]`, and `raidbots.decisions export` for reading JSONL decision traces from the console.

- Test/debug tooling:
  - Live-test fix on 2026-07-05: `raidbots.testsetup` and `raidbots.debug on` now enable floating debug nameplates above tracked bots for admins/test observers, making it possible to find bots in the world without relying on console-only position output.
  - Live-test helper on 2026-07-05: v0.3.2 nameplates now include per-viewer distance from the admin/test observer to the bot.
  - Live-test helper on 2026-07-05: v0.3.5 moves detailed tactical debug off the floating world label and into a static right-side admin panel for the closest bot. The overhead label is intentionally compact again: bot name, tactical state, and distance.
  - The side panel includes current signal (`visible`, `last_seen`, `heard`, `damaged`, or `none`), last selected action, LOS/exposure, skill, kit, HP, weapon/ammo, bot K/D, team, destination/cover distance, shooting, failed paths, advisor status, and fallback reason.
  - Live-test helper on 2026-07-05: v0.3.6 shrinks the overhead nameplate to a single small line and raises the default height so it stays above the bot model instead of covering it.
  - v0.3.7 adds `raidbots.squadtest <optional-player-name-or-steamid>` for duo/trio squad-role live testing.
  - The side panel and `raidbots.list` now expose squad role, base-restricted state, flank point, and active bot barricade count.
  - Live-test fix on 2026-07-05: v0.3.8 makes the configured near-player anchor a debug UI viewer when debug UI is enabled, even if the in-game player is not flagged as `IsAdmin` or granted `raidlandsroambots.admin`. `raidbots.diag` and `raidbots.debug on` now report `debugViewers` / `debug UI viewers` to make missing UI recipients obvious.
  - Live-test fix on 2026-07-05: v0.3.9 prevents `raidbots.testsetup` and `raidbots.squadtest` from accepting a bad or stale anchor. Test modes now disable random land fallback, so a typo such as an unmatched player name fails loudly instead of spawning bots far from the tester. The side panel also follows the closest active bot even when that bot is outside floating-nameplate range.
  - Live-test polish on 2026-07-05: v0.3.10 initially pinned bot-placed barricades to `assets/prefabs/deployable/barricades/barricade.cover.wood.prefab`; this was superseded by the v0.3.11 double-cover prefab below.
  - v0.3.10 adds bot clan definitions, clan tags on bot bodies/nameplates, `clan_key` / `clan_tag` / `clan_name` fields on bot stats, and aggregate `bot_clans` stats for future website display.
  - v0.3.10 improves squad/clan contact cohesion: damage from a player becomes shared clan target memory, target ids persist through the last-known search window, shared search/flank candidates score above regroup during fresh contact, and regroup only wins during a fresh fight if a bot is far outside the clan envelope.
  - Live-test fix on 2026-07-05: v0.3.11 changes Wooden Barricade Cover placement to `assets/prefabs/deployable/barricades/barricade.cover.wood_double.prefab`, matching the larger/current deployable cover instead of the small single cover prefab.
  - Live-test fix on 2026-07-05: v0.3.11 makes direct target rays still pass through the foliage concealment check before counting as visible. Foliage checks now use a wider sphere cast, lower clear-through-foliage distance, and include `World` / `Default` layers in addition to `Tree` / `Resource`.
  - Live-test polish on 2026-07-05: v0.3.12 makes damaged bots consider a quick real Wooden Barricade Cover from damage memory, even if they do not currently have direct LOS. Average and dangerous bots notice this at high probability; casual bots can still miss the cue.
  - Live-test polish on 2026-07-05: v0.3.12 lowers the default barricade cooldown from 45s to 18s and allows damage-wall placement against threats out to configured vision range instead of the old 75m cap.
  - Live-test polish on 2026-07-05: v0.3.12 adds low-health cover awareness at 60% HP. Bots may fail the awareness roll to stay player-like, but when they notice they prioritize cover/tuck behavior and recover health while actually tucked/at cover and not firing.
  - Live-test fix on 2026-07-05: v0.3.13 makes survival actions decisively outscore visible-target shooting. An average/dangerous bot at critical health should no longer keep selecting `acquire_visible_target` over wall/retreat/tuck.
  - Live-test fix on 2026-07-05: v0.3.13 makes average/dangerous damage-wall awareness guaranteed by default, raises low-health notice chances, and auto-notices critical low health for average-plus bots.
  - Live-test fix on 2026-07-05: v0.3.13 changes barricade placement from one brittle point to a small fan of candidate points and stops terrain from being treated as a placement-clearance blocker.
  - Live-test helper on 2026-07-05: v0.3.13 adds a `Wall:` line to the side panel (`placed`, `cooldown`, `no_clear_spot`, `cap_reached`, etc.) so failed wall attempts can be diagnosed from screenshots.
  - Live-test polish on 2026-07-05: v0.3.13 raises stuck-recovery priority and despawns tracked bots that remain hard-stuck after repeated failed paths, avoiding fake/dead-shell combatants standing in the field.
  - Live-test fix on 2026-07-05: v0.3.14 stops treating "near the remembered cover point" as safe cover when the bot still has high current exposure to the player.
  - Live-test fix on 2026-07-05: v0.3.14 adds effective-cover validation and panel output (`Cover: effective`, `compromised`, `moving`, or `none`). Low-health healing now requires effective cover, not just proximity.
  - Live-test fix on 2026-07-05: v0.3.14 tightens cover arrival distance and navigator stopping distance so bots try to actually reach tuck/cover points instead of stopping several meters short.
  - Live-test fix on 2026-07-05: v0.3.15 stores the post-barricade hold/tuck point as the cover point instead of the barricade entity itself, so `Wall: placed` can lead to `Cover: effective`, low-health healing, and later peek/shoot behavior.
  - Live-test fix on 2026-07-05: v0.3.15 gives newly placed barricades a generated peek point and transitions reached retreat/barricade holds back into `FightFromCover` when effective cover is achieved.
  - Live-test fix on 2026-07-05: v0.3.15 lets recent attackers bypass the forward FOV cone for visibility checks while still requiring real line-of-sight, so bots retreating away can reacquire an exposed shooter behind them.
  - Live-test fix on 2026-07-05: v0.3.15 lowers the priority of fallback retreat when no hard cover was found, lets visible recent attackers trigger exposed return fire when walls/cover are unavailable, and times out stale retreat loops once the bot reaches its destination without fresh contact.
  - Live-test fix on 2026-07-05: v0.3.16 makes visible-target shooting state-independent: if the bot has ammo, passes real LOS/exposure checks, is within weapon max range, and is not syringe-locked, it can shoot while retreating, moving to cover, flanking, regrouping, searching, investigating, pushing, or holding a barricade.
  - Live-test fix on 2026-07-05: v0.3.16 removes the old poor-range fire roll as a firing gate. Bad-range shots can still be weak through damage scaling, but the bot no longer refuses to fire at a visible target just because the range score is poor.
  - Live-test fix on 2026-07-05: v0.3.16 splits healing into passive combat healing and syringe-style cover healing. Passive healing can run while moving/fighting below full HP; syringe healing starts only from effective cover below the low-health threshold, locks firing briefly, and targets 85% HP.
  - Live-test helper on 2026-07-05: v0.3.16 adds `Heal:` to the debug panel (`passive`, `cover_heal`, `syringe_lock`, or `none`) so expected no-shoot syringe windows can be separated from bad no-fire behavior.
  - Live-test fix on 2026-07-05: v0.3.17 adds the retreat-wall rule. If a low-health bot wants to retreat and the chosen cover is farther than the configured 10m threshold, it can place a real wall before crossing open ground.
  - Live-test fix on 2026-07-05: v0.3.17 commits bots to fighting from a newly placed wall for a short window, instead of placing cover and immediately pushing or wandering away from it.
  - Live-test fix on 2026-07-05: v0.3.17 raises the default bot-wall cap to 12 and can recycle the oldest bot-placed wall when urgent survival placement would otherwise be blocked by `cap_reached`.
  - Live-test fix on 2026-07-05: v0.3.17 adds bot weapon auto-reload for held projectile weapons so `Ammo: 0.00` does not strand a bot in roam/search/cover states forever.
  - Live-test helper on 2026-07-05: v0.3.17 adds `Fire:` to the side panel, showing why a bot with a target is not shooting (`no_ammo`, `no_los`, `out_of_range`, `syringe_lock`, `start_failed`, etc.).
  - Live-test fix on 2026-07-05: v0.3.18 loosens the sight gate that made bots wait until point-blank in woods. Old configs with 135 degree FOV, 12m close awareness, 45-50% exposure requirements, 1.35m foliage sphere casts, 12m clear-through-foliage, and one foliage hit to block are migrated to wider/more forgiving values on reload.
  - Live-test fix on 2026-07-05: v0.3.18 keeps solid LOS checks but makes foliage concealment require multiple narrow foliage hits, and no longer treats near-misses beside rocks/ore as foliage concealment.
  - Live-test fix on 2026-07-05: v0.3.18 lets perception start firing as soon as LOS/exposure/ammo/reaction-delay are valid, instead of waiting for the next tactical action selection.
  - Live-test helper on 2026-07-05: v0.3.18 adds `Sight:` to the side panel so no-fire screenshots can distinguish `foliage`, `solid`, `exposure`, and `no_candidate` from weapon/fire failures.
  - Live-test polish on 2026-07-05: v0.3.19 tightens foliage back from the v0.3.18 overcorrection. It keeps the no-shoot fix, but changes foliage to a 0.65m cast, two foliage blockers, and 24m clear-through range so bots are less aggressive through brush.
  - Live-test fix on 2026-07-05: v0.3.19 validates barricade hold points before accepting a wall. Hold points must stay close to the wall and terrain-aligned, so navmesh sampling on slopes cannot drag the bot to fake cover 10m-15m away.
  - Live-test helper on 2026-07-05: v0.3.19 adds `Wall: hold_failed_slope` for cases where a wall spawned but no valid behind-wall position exists; the bot re-plans instead of treating the wall as usable cover.
  - Live-test polish on 2026-07-05: v0.3.20-v0.3.23 added sound-investigation improvements, stricter foliage handling for dense jungle/forest sight lines, skill-scaled long-range defensive healing, nearby-cover preference, and fuller heal-to-cover discipline.
  - Live-test feature on 2026-07-05: v0.3.24 adds real F1/smoke utility actions, utility cooldown/cap config, squad/bystander safety checks, grenade danger-zone avoidance, and `Utility:` diagnostics in the side panel plus `utility=` in `raidbots.list`.
  - Live-test balance fix on 2026-07-05: v0.3.25 moves skill health to player-like values (`casual=100`, `average=110`, `dangerous=120`), migrates old high-health configs on reload, and removes bot outgoing/incoming damage scaling so hits use normal Rust damage.

### Verified locally

```text
Roslyn compile check against RustDedicated_Data/Managed completed with no errors through v0.3.25.
Remaining warnings are expected future-phase fields and Oxide plugin references populated at runtime.
```

### Live smoke notes

```text
2026-07-05:
- Last documented live reload/spawn smoke was on the earlier v0.3.1 tactical baseline.
- raidbots.testsetup applied the one-bot tactical test setup.
- raidbots.enable 1 spawned a tracked bot after the first two legacy body prefabs failed navigator placement and the known-working scientistnpc_junkpile_pistol prefab was accepted.
- Follow-up body preparation showed the Raidlands kit weapon applied: weapon=rifle:rifle.ak, ammo=1.00, held=rifle.ak:BaseProjectile.
- raidbots.list showed the new diagnostics surface: exposure=0.00(0/0), weapon=rifle:rifle.ak, cover=none, stuck=False, navPath=True, navDisabled=False.
- This confirms early reload/spawn/kit/nav/diagnostics smoke only. The v0.3.25 foliage, wall-hold, squad, cover, healing, auto-reload, hard-stuck, grenade, smoke, grenade danger-zone, player-like health, and normal-damage behavior still need in-game combat/pathing retests after upload/reload.
```

### Stop point for in-game testing

Please live-test v0.3.25 before I implement true formation pathing, base assault logic, real med-item use, or LLM advisor calls. This pass polishes the implemented squad/clan coordination, base-boundary behavior, foliage LOS gating, real bot-placed wooden cover barricades, damage/low-health survival reactions, effective-cover validation, barricade hold/peek behavior, retreat-loop escape, state-independent visible shooting, passive combat healing, syringe-lock healing, retreat-wall placement, wall-cap recycling, weapon auto-reload, sight-gate tuning, immediate perception firing, slope-wall validation, hard-stuck cleanup, real F1/smoke utility, grenade danger-zone avoidance, player-like HP, and normal unscaled damage.

Recommended first test ladder:

```text
1. oxide.reload RaidlandsRoamBots
2. raidbots.diag
3. raidbots.enable 1
4. raidbots.list
5. Confirm a legacy scientist body spawns with a Raidlands kit.
6. Stand behind terrain/walls and confirm it investigates or roams but does not shoot.
7. Step into LOS and confirm it reacts after a short delay and only shoots with LOS.
8. Break LOS and confirm it stops shooting and moves to last-seen/search behavior.
9. Damage it from cover and confirm it investigates the damage/heard position.
10. Check oxide/data/RaidlandsRoamBots/decision_traces.jsonl for fallback traces when hard decisions trigger.
11. Use raidbots.list while fighting and watch exposure=X.XX(Y/Z), weapon=<class>, cover=<point>, and stuck=<bool>.
```

Additional retest after the 2026-07-05 visibility fix:

```text
1. Reload the plugin.
2. Spawn one bot with the same near-player test setup.
3. Put multiple trees/bushes between player and bot at 80m-150m.
4. Expected: raidbots.list should show target=none, heard, or memory, not target=visible.
5. Expected: the bot may investigate or reposition, but should not keep firing through foliage without a clean LOS.
6. Step into open ground at similar distance.
7. Expected: the bot can reacquire and shoot after reaction delay.
```

Nameplate retest:

```text
1. Run raidbots.testsetup if needed, then reload/enable one bot.
2. Expected: admins/test observers see only a small one-line bot display name, tactical state, and distance above the bot model, not over the body.
3. Expected: a static right-side panel shows detailed debug for the closest tracked bot, including signal/action and skill/KD data.
4. Use raidbots.debug off to hide debug nameplates, the side panel, and verbose tactical logging.
```

Baseline cover/range/stuck retest:

```text
1. Reload the plugin and spawn one bot with raidbots.testsetup / raidbots.enable 1.
2. Crest a hill so only a tiny part of your head is visible.
3. Expected: raidbots.list should show low exposure and the bot should not instantly hard-lock and fire from one head ray.
4. Fight a pistol/SMG bot at 70m-100m.
5. Expected: it may harass in short windows, but should prefer pushing/repositioning closer; landed hits should use normal Rust damage.
6. Put a cliff/rock/wall between you and the bot's destination.
7. Expected: stuck eventually becomes true, a stuck_recovery trace is written, and the bot chooses another nearby navmesh point instead of staring forever.
8. Fight near rocks/terrain.
9. Expected: when a cover point is found, the bot moves to cover, tucks, peeks briefly, and only fires when the peek/exposure gate passes.
```

v0.3.25 clan/foliage/base/barricade/low-health/ammo/utility/health/damage retest:

```text
1. raidbots.nuke
2. oxide.reload RaidlandsRoamBots
3. raidbots.squadtest <optional-player-name-or-steamid>
4. raidbots.enable 3
5. raidbots.list
6. Expected: bots show clan=<tag> and role=anchor/flanker/pusher, share last-known info, and only bots with their own LOS shoot.
7. Fight near cover and watch for search_last_known, flank_left/flank_right, or regroup_with_squad traces via raidbots.decisions last 10.
8. Enter a player base or stand just inside a base boundary.
9. Expected: bots hold outside, reposition, or abandon instead of chasing/shooting through the base.
10. Damage an exposed average/dangerous bot in open ground at plausible range.
11. Expected: it should choose `place_barricade` quickly, place a full-size real Wooden Barricade Cover, then hold/peek around it. Active barricades are capped by config and intentionally remain as world entities.
12. Damage a bot below 60% HP and keep pressure on it.
13. Expected: if it notices the health cue, it should choose `place_barricade`, `retreat_to_cover`, or `tuck` over `acquire_visible_target`, stop firing while tucked, and recover health only when `Cover: effective`. Average-plus bots at critical health should notice.
14. If the bot moves near cover but remains exposed, expected: the side panel should show `Cover: compromised` and the bot should re-plan cover/wall instead of standing still to heal.
15. Put bushes/trees between you and the bots at 40m-120m.
16. Expected: `raidbots.list` / the side panel should not show `LOS: Y` with high exposure while the bot is visually hidden by dense foliage. It may search or reposition, but should not keep firing through the bush.
17. Break LOS around 150m-190m after contact.
18. Expected: clan members should keep searching/pushing the last-known or damage position during the fresh-contact window instead of immediately scattering back to a neutral regroup.
19. If a bot stands in place with `stuck=Y`, watch the panel and `raidbots.list`. Expected: it should select stuck recovery; if failed paths continue past the hard-stuck threshold, the tracked bot despawns instead of remaining as a fake/dead-shell combatant.
20. Damage a low-health bot while its nearest retreat cover is farther than 10m.
21. Expected: it should choose `place_barricade` before crossing the open field, then fight from the wall for the configured short commitment unless the wall is compromised or the target leaves useful range.
22. If `Shooting: N` appears while the bot has a target, check `Fire:`. Expected blockers are concrete reasons such as `no_ammo`, `no_los`, `out_of_range`, `syringe_lock`, `target_in_base`, `no_attack_interface`, or `start_failed`.
23. If a bot's held gun starts empty, expected: `Auto Reload Bot Weapons` refills the active projectile magazine so it does not stay trapped at `Ammo: 0.00`.
24. Fight in sparse forest cover at 40m-120m.
25. Expected: a clearly visible player should no longer be hidden by one branch or a wide near-miss sphere cast, but multiple foliage hits should now suppress fire more often than v0.3.18. If the bot still does not shoot, capture `Sight:` and `Fire:` together.
26. Force a wall on uneven terrain or across elevation.
27. Expected: the bot should only use the wall if a close behind-wall hold point exists. If not, the panel should show `Wall: hold_failed_slope` and it should re-plan instead of running to fake cover far away.
28. Break LOS or hold near cover around 12m-42m after contact.
29. Expected: a bot may choose `throw_grenade`, spawn a real F1, enter `GrenadeFlush`, and then move away or back to cover. Squadmates inside the danger radius should choose an escape move instead of standing in the blast lane.
30. Hurt a bot while exposed around 10m-55m.
31. Expected: a bot may choose `throw_smoke`, spawn a real smoke grenade, enter `Retreat`, and move away through the screen.
32. If utility does not happen, expected `Utility:` blockers include `utility_cd`, `team_cd`, `grenade_range`, `grenade_ally_close`, `grenade_bystander_close`, `utility_base_blocked`, or `utility_cap`.
```

### Known not-yet-implemented

```text
cover/peek quality tuning beyond the baseline
grenade/smoke throw arc, damage, and smoke-screen tuning beyond the baseline after live validation
real med-item animation/use; v0.3.25 health recovery is still a controlled passive/syringe-style approximation
true formation/path reservation and leader/follower pathing
base objective validation, base assault, and smarter "is this base worth holding" logic
advanced stuck memory for avoiding the same failed destination over longer windows
HTTP/OpenAI-compatible advisor
shadow/canary LLM behavior
```
