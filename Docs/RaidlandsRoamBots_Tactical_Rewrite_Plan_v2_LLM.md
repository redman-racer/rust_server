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

  "Skill Weights": {
    "casual": 25,
    "average": 60,
    "dangerous": 15
  },

  "Skill Definitions": {
    "casual": {
      "Health": 125.0,
      "DamageScale": 0.78,
      "IncomingDamageScale": 1.15,
      "ReactionMinSeconds": 0.75,
      "ReactionMaxSeconds": 1.35,
      "AimErrorDegrees": 5.0,
      "Aggression": 0.35,
      "Courage": 0.35,
      "TacticalNoise": 0.25
    },
    "average": {
      "Health": 150.0,
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
      "Health": 190.0,
      "DamageScale": 1.18,
      "IncomingDamageScale": 0.9,
      "ReactionMinSeconds": 0.18,
      "ReactionMaxSeconds": 0.45,
      "AimErrorDegrees": 1.5,
      "Aggression": 0.8,
      "Courage": 0.8,
      "TacticalNoise": 0.06
    }
  },

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
    "Use Generated Positions Near Players": true,
    "Use Random Land Fallback": true,
    "Require Land Spawns": true,
    "Avoid Safe Zone Spawns": true,
    "Ignore Players In Safe Zones": true,
    "Near Player Minimum Distance": 90.0,
    "Near Player Maximum Distance": 260.0,
    "Near Player Attempts Per Bot": 64,
    "Group Spawn Radius": 12.0,
    "Navmesh Sample Distance": 12.0,
    "Minimum Above Water": 1.5,
    "Safe Zone Spawn Buffer Distance": 75.0
  },

  "Prefab Candidates In Order": [
    "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab",
    "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab"
  ],

  "AI": {
    "Perception Tick Seconds": 0.25,
    "Decision Tick Seconds": 0.35,
    "Squad Tick Seconds": 0.75,

    "Vision Range": 160.0,
    "Vision Fov Degrees": 135.0,
    "Close Awareness Radius": 12.0,
    "Target Memory Seconds": 14.0,
    "Search Last Seen Seconds": 22.0,

    "Unsuppressed Gunshot Hearing Range": 240.0,
    "Suppressed Gunshot Hearing Range": 85.0,
    "Explosion Hearing Range": 380.0,
    "Melee Or Tool Hearing Range": 45.0,
    "Sprint Hearing Range": 28.0,

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
    "Cover Reposition Cooldown Seconds": 4.0,
    "Peek Exposure Min Seconds": 0.35,
    "Peek Exposure Max Seconds": 1.15,
    "Tuck Min Seconds": 0.45,
    "Tuck Max Seconds": 1.6,

    "Grenade Cooldown Seconds": 30.0,
    "Team Grenade Cooldown Seconds": 10.0,
    "Barricade Cooldown Seconds": 45.0,

    "Do Not Enter Bases": true,
    "Base Avoidance Radius": 8.0
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
    "Debug Cover Scores": false,
    "Debug Decision Advisor": false
  }
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

    public float NextReactionAllowedAt;
    public float NextCoverSearchAt;
    public float NextPeekAt;
    public float NextGrenadeAt;
    public float NextBarricadeAt;
    public float LastShotAt;
    public float LastDamageTakenAt;
    public float LastDamageDealtAt;

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
    public Dictionary<ulong, EnemyMemory> KnownEnemies = new Dictionary<ulong, EnemyMemory>();
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
private bool HasUsableItem(BasePlayer bot, string shortname);
private bool TryEquipItem(BasePlayer bot, string shortname);
private bool TryThrowGrenade(BasePlayer bot, Vector3 targetPosition);
private bool TryThrowSmoke(BasePlayer bot, Vector3 targetPosition);
private bool TryPlaceBarricade(BasePlayer bot, Vector3 threatPosition);
```

Grenade conditions:

```text
target last seen recently
target is behind cover
range 8m-35m
team grenade cooldown ready
no teammate near blast point
bot has grenade or config allows virtual grenade
```

Smoke conditions:

```text
crossing open ground
retreating from long-range fire
regrouping under pressure
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
