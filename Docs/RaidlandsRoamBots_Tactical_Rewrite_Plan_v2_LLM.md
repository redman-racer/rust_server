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

This example reflects the canonical plugin defaults as of `RaidlandsRoamBots` v0.3.50. The checked-in `oxide/config/RaidlandsRoamBots.json` may intentionally differ when it is holding a focused local test setup or staged live-release profile.

```json
{
  "Enabled": false,
  "Target Population": 50,
  "Minimum Allowed Population": 0,
  "Maximum Allowed Population": 200,

  "Team Size Weights": {
    "solo": 55,
    "duo": 35,
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
      "AimErrorDegrees": 1.5,
      "AimWarmupSeconds": 2.5,
      "AimWarmupInitialExtraDegrees": 3.0,
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
      "AimErrorDegrees": 0.75,
      "AimWarmupSeconds": 1.75,
      "AimWarmupInitialExtraDegrees": 1.5,
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
      "AimErrorDegrees": 0.2,
      "AimWarmupSeconds": 1.0,
      "AimWarmupInitialExtraDegrees": 0.4,
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
    "Use Physics Surface Spawn Checks": true,
    "Physics Surface Raycast Height": 160.0,
    "Maximum Physical Surface Mismatch": 1.25,
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

  "Ambient Squad Respawn Rejoin": {
    "Enabled": true,
    "Minimum Teammate Health Fraction": 0.98,
    "Teammate Recent Damage Window Seconds": 15.0,
    "Direct Teammate Teleport Radius": 2.25,
    "Teleport Position Attempts": 10,
    "Wakeup Equip Delay Seconds": 4.0,
    "Fallback To Normal Spawn": true
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
    "Minimum Exposed Target Fraction To Shoot": 0.34,
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
    "Allow Crouch": true,
    "Crouch While Shooting": true,
    "Crouch While Cover Healing": true,
    "Shooting Crouch Chance Casual": 0.60,
    "Shooting Crouch Chance Average": 0.80,
    "Shooting Crouch Chance Dangerous": 0.95,
    "Shooting Crouch Min Seconds": 0.8,
    "Shooting Crouch Max Seconds": 2.4,
    "Cover Heal Crouch Min Seconds": 1.5,
    "Cover Heal Crouch Extra Seconds": 0.25,
    "Crouch Cooldown Seconds": 0.75,
    "Crouch Clear Delay Seconds": 0.15,
    "Allow Bot Clan Wars": true,

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
    "Hard Stuck Despawn Seconds": 180.0,
    "Stuck Memory Seconds": 75.0,
    "Stuck Memory Radius": 9.0,
    "Maximum Stuck Memory Points": 14,

    "Squad Flank Distance": 24.0,
    "Squad Regroup Distance": 55.0,
    "Squad Contact Commitment Seconds": 35.0,
    "Flank Cooldown Seconds": 7.0,
    "Squad Destination Reservation Radius": 7.5,
    "Squad Formation Spacing": 8.0,
    "Squad Formation Offset Attempts": 10,

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
    "Smoke Blocks Vision": true,
    "Smoke Vision Radius": 7.5,
    "Smoke Vision Height": 5.0,
    "Smoke Vision Lifetime Seconds": 30.0,
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
    "Protection Damage Trigger Percent": 15.0,
    "Protection Damage Window Seconds": 10.0,
    "Protection Commitment Seconds": 12.0,
    "Protection Distance Casual": 8.0,
    "Protection Distance Average": 5.0,
    "Protection Distance Dangerous": 3.0,

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
    "Non Syringe Heal Cooldown Seconds": 3.5,
    "Non Syringe Heal Amount": 8.0,
    "Allow Shooting While Non Syringe Healing": true,
    "Syringe Fire Lock Seconds": 2.2,
    "Syringe Cooldown Seconds": 8.0,
    "Syringe Heal Target Fraction": 0.85,
    "Grant Bot Medical Items": true,
    "Bot Medical Item Shortname": "syringe.medical",
    "Bot Medical Item Amount": 2,
    "Bot Medical Loadout": {
      "syringe.medical": 2,
      "largemedkit": 2,
      "black.raspberries": 4
    },
    "Use Real Medical Items For Cover Heal": true,
    "Real Medical Item Heal Amount": 15.0,
    "Real Medical Item Shortnames": ["syringe.medical", "largemedkit", "bandage", "black.raspberries"],
    "Barricade Anchor Casual Long Range Threshold": 40.0,
    "Barricade Anchor Average Long Range Threshold": 55.0,
    "Barricade Anchor Dangerous Long Range Threshold": 70.0,
    "Barricade Anchor Required Hitmarkers Casual": 2,
    "Barricade Anchor Required Hitmarkers Average": 3,
    "Barricade Anchor Required Hitmarkers Dangerous": 5,
    "Barricade Anchor No Action Push Seconds Casual": 10.0,
    "Barricade Anchor No Action Push Seconds Average": 15.0,
    "Barricade Anchor No Action Push Seconds Dangerous": 22.0,
    "Prevent Moving In Front Of Anchored Barricade": true,
    "Auto Reload Bot Weapons": true,

    "Do Not Enter Bases": true,
    "Base Avoidance Radius": 8.0,
    "Base Hold Seconds": 12.0
  },

  "Decision Advisor": {
    "Enabled": true,
    "Provider": "openai_compatible",
    "Mode": "shadow",
    "Shadow Mode": true,
    "Treat Unconfigured Advisor As Failure": true,
    "Fallback On Any Failure": true,
    "Endpoint Url": "https://api.openai.com/v1/chat/completions",
    "Api Key": "${OPENAI_ROAM_BOT_API_KEY}",
    "Model": "gpt-4.1-mini",
    "Timeout Milliseconds": 750,
    "Decision Ttl Milliseconds": 3000,
    "Minimum Confidence": 0.55,
    "Max Concurrent Requests": 2,
    "Min Seconds Between Requests Per Bot": 8.0,
    "Require Real Player Within Meters": 350.0,
    "Require Active Player Engagement": true,
    "Player Engagement Memory Seconds": 45.0,
    "Ask When Bot Is Stuck": true,
    "Ask When Action Scores Are Close": true,
    "Ask When Push Retreat Or Flank Is High Impact": true,
    "Ask When Same Action Failed Repeatedly": true,
    "Ask When Squad State Changes Sharply": true,
    "Log Decision Traces": true,
    "Max Decision Trace File Megabytes": 128,
    "Max Decision Trace Lines After Prune": 50000,
    "Decision Trace Prune Check Interval Seconds": 300.0,
    "Max Recent Events In Request": 24,
    "Max Candidate Actions": 8
  },

  "Player Observation Learning": {
    "Enabled": false,
    "Apply Mode": "off",
    "Source": "admin_testers",
    "Observed Player SteamIds": [],
    "Player Profile Spawn Weights": {},
    "Log Observation Traces": true,
    "Sample Interval Seconds": 1.0,
    "Outcome Window Seconds": 10.0,
    "Minimum Global Observations": 12,
    "Minimum Profile Observations": 8,
    "Maximum Global Score Delta": 24.0,
    "Maximum Profile Score Delta": 36.0,
    "Shadow Calculates Score Deltas": true,
    "Low Confidence Observation Weight": 0.35,
    "High Confidence Target Context Threshold": 0.75
  },

  "Bot Kill Integration": {
    "Broadcast Player-Like Kill Messages": true,
    "Suppress DeathNotes For Roam Bot Kills": true,
    "Chat Format": "<color=#838383>[<color=#80D000>DeathNotes</color>] {message}</color>",
    "Kill Message": "<color=#C4FF00>{killer}</color> killed <color=#C4FF00>{victim}</color> with <color=#C4FF00>{weapon}</color> from <color=#C4FF00>{distance}</color> ({method}).",
    "Award ServerRewards RP": true,
    "RP Reward Per Bot Kill": 5,
    "Tell Killer About RP Reward": true,
    "RP Reward Message": "<color=#ce422b>[Raidlands]</color> You earned <color=#B6F34A>{rp} RP</color> for killing <color=#C4FF00>{victim}</color>."
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
    "Debug Decision Advisor": false,
    "Debug Console Logs": false,
    "Debug Console Log Cooldown Seconds": 5.0,
    "Console Warning Cooldown Seconds": 30.0
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
clamp invalid visibility, foliage, hearing, defensive-healing, protection-damage, utility, health, damage, aim-warmup, stuck-memory, squad-reservation, medical-item, bot-clan-war, killfeed, quiet-console, barricade-anchor, and physics-surface spawn settings to the current defaults without resetting stricter operator tuning
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
avoid the same failed destination for a configurable short memory window
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

As of v0.3.25, grenade and smoke utility is real-entity utility driven by config and tactical legality, not by bot inventory item ownership. As of v0.3.27, low-health cover healing grants bots a small configurable stack of real medical items and consumes an allowed medical item when the cover-heal lock starts, falling back to the controlled syringe-style heal if no item is available. Full native medical animation/effect parity is still later work.

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
raidbots.decisions prune
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

## Current Progress Through v0.3.76 Ambient Squad Respawn Rejoin

### Files updated

```text
oxide/plugins/RaidlandsRoamBots.cs
oxide/config/RaidlandsRoamBots.json
oxide/plugins/BGrade.cs
oxide/config/BGrade.json
oxide/lang/en/BGrade.json
Docs/RaidlandsRoamBots_Tactical_Rewrite_Plan_v2_LLM.md
UPDATED_FILES_FOR_UPLOAD.txt
UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_INVISIBLE_ADMINS_2026-07-07.md
UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_SCIENTIST_AUDIO_2026-07-08.md
UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_FOLIAGE_LOS_2026-07-09.md
UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_SQUAD_REJOIN_2026-07-10.md
```

### Current code and config snapshot

```text
Plugin version: RaidlandsRoamBots v0.3.76
Brain mode: playerlike_tactical_brain
Current checked-in config: enabled, target=50, min=0, max=200
Current checked-in release profile: random-land spawning, no hardcoded tester anchor, debug console logs disabled, bot clan wars enabled
Current checked-in config source: refreshed from the attached live server JSON on 2026-07-10, preserving live smoke LOS tuning, expanded kit/profile lists, observed SteamIDs, and learned profile weights including fireclan=25, then layering in the new ambient squad rejoin block.
Current ambient squad respawn rejoin: enabled for ambient RoamBots only. After an ambient bot dies, its replacement keeps the dead bot's internal team id and first tries to spawn directly beside a living teammate with at least 0.98 health fraction and no damage taken in the last 15 seconds. The rejoined bot holds perception and tactical decisions for 4 seconds so it has a stand/equip window before acting. If no healthy teammate/position is available, normal population maintenance runs, which follows the current random-land spawn mode.
Current foliage LOS profile: foliage blocking enabled, terrain foliage sampling enabled, line-of-sight required to shoot, shoot exposure threshold 0.34, foliage sphere radius 0.9m, clear-through foliage distance 14m, and one foliage blocker hides an individual target probe.
Current decision advisor config: openai_compatible shadow mode with `${OPENAI_ROAM_BOT_API_KEY}` indirection, active real-player engagement required before any HTTP advisor request, a current real-player fight target/candidate gate after engagement, a 350m real-player proximity safety cap after that, and deterministic heuristic actions still executing
Current bot chat config: master chat enabled, AI replies enabled, deterministic kill banter enabled, existing OpenAI-compatible advisor endpoint/key/model reused for chat replies, 30 bot-chat OpenAI calls per hour, one concurrent chat request, clan-or-player conversation contexts idle out after 180s, each context stops AI replies after a randomized 10-15 total player/bot messages, and live trigger odds are reduced to half of the first pass: normal AI replies 22.5%, mention replies 45%, recent-fight replies 22.5%, and deterministic kill-banter chances 17.5%/22.5%/20%.
Current bot corpse loot config: enabled under `Kit Selection -> Corpse Loot`; each bot snapshots its actual live inventory after `Kits.GiveKit` and medical grants, then death/corpse/loot hooks copy that live belt/wear/main inventory into the visible corpse grid as belt/hotbar first, worn armor/clothes second, and main inventory last. Corpses can still roll default-access utility kits (`build`, `scuba`) plus capped low-tier bonus kits (`mp5`, `m16`) for up to three bots per team. RoamBots owns recent bot corpse population, forces a player-corpse style panel/name where Rust allows it, and asks BetterLoot to skip those corpses so they keep player-like inventories.
Current bot death-screen identity config: five bot avatar identities, chat sender IDs for bot-caused kills, native Rust `PlayerLifeStory.DeathInfo` patching, `BasePlayer.SetOverrideDeathBlow(...)`, and lethal-hit `ScientistNPC.AttackerInfo` overwrite prevention for the `KILLED BY` death-screen card
Current body candidates: scientistnpc_roam, scientistnpc_full_any, scientistnpc_junkpile_pistol
Current native scientist body audio: muted during body preparation by clearing `ScientistNPC.RadioChatterEffects` and `DeathEffects`, cancelling any queued `PlayRadioChatter` action, and stretching the idle chatter window. This removes native scientist radio/voice/death chatter while leaving RoamBots hearing, bot chat, weapon fire, tactical behavior, killfeed, stats, and loot logic unchanged.
Current stats data shape: players, bots, bot_clans
Current decision trace path: oxide/data/RaidlandsRoamBots/decision_traces.jsonl
Current decision trace retention: prune to 128 MB or 50000 recent lines, checked every 300s and on reload/export/prune command
Current learning trace path: oxide/data/RaidlandsRoamBots/player_observation_traces.jsonl
Current learning trace context: passive rows keep nearest-bot sample fields, while damage/kill/death rows promote the tracked bot involved as `target_context_source=combat_target` with confidence, bot key/name, combat distance, LOS, exposure, probe counts, and posture/crouch burst samples.
Current learning confidence weighting: low-confidence rows still train at 0.35 weight by default, while high-confidence target context starts at 0.75 confidence.
Current native bot map marker overlay: disabled by default, admin-controlled from `/raidbots admin` Debug tab, backed by globally broadcast `MapMarkerGenericRadius` entities using the local `assets/prefabs/tools/map/genericradiusmarker.prefab` prefab, treats the configured marker radius as a visible display-size control before converting to Rust's native map-radius value, and is warning-labeled because native marker entities may be visible to all players while enabled.
Current BGrade integration surface: `API_IsRaidlandsRoamBot(BaseEntity)` and `API_GetRaidlandsRoamBotCombatKey(BaseEntity)` expose tracked live/recent-death RoamBot identity so BGrade can treat direct RoamBot shots and RoamBot deaths like player combat without guessing from names or prefabs.
Current managed event bot API: `REBOT_SpawnGroup`, `REBOT_SpawnSingle`, `REBOT_DespawnGroup`, `REBOT_DespawnForEvent`, `REBOT_GetBotOwner`, `REBOT_SetGroupObjective`, `REBOT_SetGroupLeash`, and `REBOT_IsBot` let RaidlandsEvents borrow RoamBots-owned NPC groups while ambient population maintenance only counts ambient bots.
Current crouch posture layer: deterministic `ModelState.Flag.Ducked` toggles are enabled for syringe cover-heal locks from effective cover and skill-weighted shooting/peek windows; pure roam, push/flank movement, utility escape, stuck recovery, and base-hold movement clear crouch unless the bot is actively firing.
Current bot clan-war damage behavior: same-clan bot hits remain blocked, but if a friendly bot is incorrectly targeting another friendly bot the attacker clears that target and stops firing; different-clan hits keep native Rust damage when present, and use a small weapon-class floor only when a real legacy scientist damage event arrives with effectively zero damage. v0.3.59 deliberately does not inject forced damage without a hit event; instead, the side-panel `Dmg:` line and `raidbots.list` native combat diagnostics expose whether bot shots are producing projectile hooks, damage hooks, same-clan blocks, untracked runtime resolution, or enemy-bot damage-floor events. v0.3.62 fixes bot bullet attribution by resolving the firing bot from `HitInfo.InitiatorPlayer`, held weapon owner, initiator owner, creator entity, parent/root parent, and `OwnerID` before falling back to raw `HitInfo.Initiator`. v0.3.63 adds a real-hit bullet bridge for `OnPlayerAttack` hits. v0.3.64 covers the live `Dmg: fire n/a#0 atk none hook none` case by owning bot-vs-bot bullet resolution when Rust emits no NPC-vs-NPC bullet hooks at all: the resolver runs only while the tactical brain is allowed to fire, requires a tracked different-clan target, current LOS/exposure, weapon-class rate limiting, range/aim/exposure hit chance, and a fresh world line check before applying one bullet hit.
Current advisor bot-vs-bot cost guard: bot-vs-bot clan wars still use local deterministic tactics, but advisor HTTP submission now requires the bot's current memory target or candidate target ids to resolve to an engageable real player. Bot-only fights report `advisor_skipped_not_fighting_real_player` and do not call the API.
Current invisible admin behavior: real players with `BasePlayer.isInvisible` or a tracked Vanish hidden state are ignored by RoamBots vision, hearing, damage memory, advisor engagement, utility bystander checks, and near-player spawn anchors; if a targeted player vanishes, active bot target memory and firing are cleared immediately.
Current learned model path: oxide/data/RaidlandsRoamBots/behavior_models.json
Current learned profile spawn mix: `Player Profile Spawn Weights` remains the config key but is treated as a 100-point percentage pool. Valid learned profiles use their configured percentage, unused percentage is the `Default` remainder, and missing profile model data falls through to default skill-tier behavior. The checked-in mix is `ababmxking=50`, `sudol=50`, `Default=0`.
Current admin UI surface: `/raidbots admin` is now a guided CUI workbench with a wider shell, readable runtime/ambient/managed/anchor header, fuller tab labels, section header bands, visible input-field backgrounds, per-tab `?` help popups, first-time-admin guardrail callouts, a Chat tab for bot chat toggles/quota/stats, Learning Observe/Train/Profiles/Mix subpages, learned-profile detail/delete popups, direct profile percent controls, and confirmation dialogs before in-panel Disable/Kill/Nuke/Reload actions.
Current training run path: oxide/data/RaidlandsRoamBots/training_runs.jsonl
Required admin permission: raidlandsroambots.admin
```

The checked-in config is now a staged live-release profile. Reloading the plugin will not spawn bots until `raidbots.enable` is run, but once enabled it targets a normal fifty-bot population with a hard cap of two hundred, uses the current random-land spawn mode for the low-pop setup, and avoids safe zones and player-base positions.

### Current admin command surface

```text
raidbots.status
chat: /raidbots admin
raidbots.ui <panel action>
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
raidbots.advisor status|off|fallback|shadow|canary|stats|last [bot name/key]
raidbots.chat status|stats|reset|on|off|ai on|ai off|banter on|banter off
raidbots.learn status|observe on|off|allow <player>|unallow <player>|build|report [minutes]|export|clear|apply off|shadow|global|profiles
raidbots.learn profile list|show <profileKey>|build <player> <profileKey>|delete <profileKey>|weight <profileKey> <percent>
raidbots.land on|off
raidbots.target [population]
raidbots.mode [near_players|random]
raidbots.anchor [name-or-steamid|clear]
raidbots.list [optional-player-name-or-steamid]
raidbots.goto <player-name-or-steamid> [bot-number]
raidbots.killall
```

Current diagnostics intentionally expose `brain`, anchor/debug-viewer counts, native map marker state/count, bot-chat enable/AI/quota state, clan tag, squad role, base-restricted state, LOS/exposure probes, weapon/ammo class, aim warmup/error, wake/action-hold state, learned model/profile keys, learned score deltas, cover/flank points, active barricade count, utility status, protection trigger/source, barricade-anchor state, medical source/fire lock, crouch state, formation-reservation status, stuck/nav details, remembered bad destination counts, spawn physical-surface checks, and target status. The debug UI is split into a compact overhead line plus a right-side closest-bot panel with `Signal`, `Action`, `Learning`, `Cover`, `Wall`, `Utility`, `Sight`, `Fire`, `Heal`, `Crouch`, `Protect`, `Anchor`, `Formation`, and `Bad spots` details.

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
  - v0.3.30 adds non-blocking `openai_compatible` and `website_proxy` HTTP advisor adapters. Responses are parsed, validated against legal candidates, traced, and rejected on invalid JSON, unknown action ids, low confidence, excessive TTL, late arrival, invalid targets, or base-blocked destinations.
  - v0.3.30 added `${OPENAI_ROAM_BOT_API_KEY}` advisor key indirection for ignored local secret storage.
  - v0.3.30 still executes deterministic heuristic actions only. Shadow/canary modes are validation and trace surfaces in this adapter pass; remote advisor actions are not applied to live movement/combat yet.
  - v0.3.32 makes fallback-only deterministic behavior the checked-in release default again, so live rollout does not depend on an external advisor or make outbound advisor calls unless the owner opts in.
  - v0.3.41 restores the checked-in live advisor block to `openai_compatible` shadow mode, resolves `${OPENAI_ROAM_BOT_API_KEY}` from the process environment first, and keeps `oxide/config/Secrets.local.json` optional instead of required.
  - v0.3.42 adds disabled-by-default `Player Observation Learning` with allowlisted tester observation, compact JSONL combat episodes, global skill-tier model builds, saved player profile builds, profile spawn weights, and `off|shadow|global|profiles` apply modes.
  - v0.3.42 applies learned score deltas only after legal tactical candidates are built and before fallback/advisor validation. Learned models cannot invent actions; they reweight `BuildCandidateActions` output and the normal validation/fallback path remains the safety baseline.
  - v0.3.42 stores learned runtime/debug fields (`BehaviorModelKey`, `PlayerProfileKey`, base skill, learned score delta, admin-only profile source) and keeps public bot display names fictional.
  - v0.3.43 gates advisor submissions behind `Decision Advisor -> Require Real Player Within Meters` (350m by default). Bot-vs-bot fights, stuck recovery, and high-impact decisions still use deterministic fallback, but no HTTP LLM request is sent unless a connected real player is close enough.
  - v0.3.44 adds bounded decision trace retention so `oxide/data/RaidlandsRoamBots/decision_traces.jsonl` no longer grows without limit while trace logging is enabled.
  - v0.3.45 adds `Decision Advisor -> Require Active Player Engagement` (default `true`) and `Player Engagement Memory Seconds` (default `45`). Advisor requests now require a fresh real-player engagement signal first, such as current LOS, recent last-seen memory, recent damage to/from a real player, or recent player sound investigation; the 350m gate remains as the second safety cap.
  - v0.3.50 adds victim-linked observation context for player-vs-bot damage, kills, and deaths. The learner now records whether the primary distance/LOS/exposure fields came from a passive nearest-bot sample or from the actual tracked bot involved in combat, and model builds weight low-confidence legacy/noisy rows less heavily.
  - v0.3.51 adds an opt-in native Rust map marker overlay for active RoamBots. Admins can enable `Map markers` from `/raidbots admin` -> Debug, tune radius/update interval, and get skill-colored markers that follow active bots and clean up on death, despawn, killall, runtime stop, or plugin unload. The default remains off because native marker entities may be visible to all players while enabled.
  - v0.3.52 adds a small BGrade-facing API for tracked RoamBot identity. BGrade uses it to pause persistent BGrade on direct ranged RoamBot hits and to cancel that pause when the player kills the single low-damage RoamBot attacker within the cooldown.
  - v0.3.53 adds a deterministic crouch posture layer. Bots set `ModelState.Flag.Ducked` while syringe-healing from effective cover and during skill-weighted shooting/peek windows, clear crouch during roam/abandon/despawn/death/killall/disable/unload and movement-only tactical actions, and expose posture in `raidbots.list`, the closest-bot side panel, decision traces, advisor payloads, and learning reports/profiles.
  - v0.3.54 adds the managed event bot lending API for RaidlandsEvents. Managed bots carry owner/event/group/profile/difficulty/leash metadata, are excluded from ambient target-population trim/refill logic, can be despawned by group or event instance, and keep RoamBots tactical AI while accepting event spawn points, leash hints, health multipliers, difficulty/profile mappings, objective metadata, and owner lookup by entity/net ID.
  - v0.3.55 fixes native map marker scale by converting the configured `Debug Bot Map Marker Radius` from meters to Rust's normalized `MapMarkerGenericRadius.radius` value. The Debug tab now labels this control as `Marker meters`, and the default marker alpha is reduced so clusters stay readable.
  - v0.3.56 retunes native map marker radius to a visible display-size scale after live testing showed the literal meter conversion was too small to render reliably. RoamBot marker entities now use `globalBroadcast = true`, the Debug tab labels the control as `Marker size`, and `mapMarkers=...` diagnostics include the native radius value being sent.
  - v0.3.58 adds a second advisor cost gate after active engagement and before the 350m proximity cap. HTTP advisor calls now require the current memory target or submitted candidate target ids to resolve to an engageable real player, so different-clan bot-vs-bot fights stay on deterministic fallback and report `advisor_skipped_not_fighting_real_player` instead of submitting OpenAI-compatible requests.
  - v0.3.60 makes invisible admins non-targets for RoamBots. The plugin checks native `BasePlayer.isInvisible`, tracks standard Vanish disappear/reappear hooks, clears existing target/last-seen/last-heard/damage memory when a target disappears, and excludes hidden players from combat candidates, hearing stimuli, advisor real-player gates, utility bystander checks, and near-player spawn anchors.
  - v0.3.61 polishes `/raidbots admin` into a fuller workbench. The Learning tab now has Observe, Train, Profiles, and Mix subpages; `?` buttons open CUI help popups; profile detail/delete popups are available; profile mix is editable as percentages with an explicit default remainder; and the old `Player Profile Spawn Weights` key is preserved while over-100 raw weights normalize proportionally.

- Phase 6 partial:
  - Added candidate-player filtering, FOV checks, plugin-controlled line-of-sight checks, target memory, reaction delay, and LOS-required shooting.
  - Bots stop attacking when LOS is lost.
  - Live-test fix on 2026-07-05: foliage/resource occlusion now participates in LOS checks so trees, bushes, and resource cover can break vision instead of allowing long-range shooting through forest cover.
  - Live-test fix on 2026-07-05: normal body preparation now suppresses the underlying `ScientistBrain` player target memory unless the Raidlands tactical brain explicitly starts an attack.
  - v0.3.34 expands combat targets from real players only to real players plus tracked roam bots from different clans. Same-clan bot damage is still blocked, but different-clan bots can perceive, shoot, damage, and kill each other through the same tactical brain.
  - v0.3.57 fixes close-range bot clan-war stalemates by clearing invalid same-clan targets when friendly damage is blocked, and by applying a small weapon-class damage floor only to different-clan bot hits whose legacy scientist damage event would otherwise do effectively zero damage.

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
  - v0.3.39 wires skill `AimErrorDegrees` into bot projectile velocity and adds skill-specific aim warmup. Casual bots start with 10 extra degrees and settle over 5s, average bots start with 6 extra degrees and settle over 3s, and dangerous bots start with 3 extra degrees and settle over 1.5s. Hits that still land keep normal Rust weapon damage.
  - v0.3.40 tightens aim substantially after live feedback: casual settles to 1.5 degrees after 2.5s, average settles to 0.75 degrees after 1.75s, and dangerous settles to 0.2 degrees after 1s. Warmup extras are now 3.0, 1.5, and 0.4 degrees respectively.
  - v0.3.48 corrects Rust's separate NPC weapon damage scalar on bot-held `AttackEntity` weapons. Kit weapons such as `rifle.ak` keep normal player-like weapon/ammo damage even though the physical body is still a legacy `ScientistNPC`.
  - v0.3.57 preserves normal Rust damage first, but guards the different-clan bot-vs-bot hook against zero-damage legacy NPC hit events so bots cannot stand five meters apart and shoot forever without health moving.

- Phase 11 baseline plus advanced stuck memory:
  - Fixed stuck detection so repeated commands to the same destination no longer reset movement progress.
  - Added latched stuck state, repeated failure counts, `stuck_recovery` decision flags, alternate nearby navmesh recovery destinations, and `stuck=true/false` diagnostics.
  - v0.3.26 adds expiring per-bot bad-destination memory for blocked, base-blocked, navigator-failed, and no-progress movement targets.
  - Recovery, cover, peek, retreat, investigate, flank, regroup, push, and roam candidate sampling now avoid recently failed destinations within the configured memory radius before resorting to hard-stuck despawn.
  - v0.3.43 adds `Hard Stuck Despawn Seconds` (180s default) so a bot latched as stuck will despawn and queue population maintenance after the configured respawn delay even if it has not reached the failed-path threshold.

- Phase 12 baseline:
  - Squad blackboards now assign `solo`, `anchor`, `flanker`, and `pusher` roles.
  - Squads share last-known enemy positions and LOS counts without granting magical shooting; only a bot with its own plugin-confirmed LOS can fire.
  - Flanker/pusher bots can choose flank points from either direct contact or squad-shared last-known memory.
  - Teammates avoid claiming the same nearby cover point when the cover planner can find alternatives.
  - v0.3.27 adds a first deterministic squad destination-reservation pass. The squad board tracks current destination claims, candidate generation soft-offsets roam, investigate, search, flank, push, regroup, and hold-outside-base destinations away from teammate destination/body/cover claims, and `raidbots.list` / the side panel expose `formation=` / `Formation:` diagnostics. This is not full leader/follower pathing yet.

- Phase 13 partial:
  - Added bounded real barricade placement for damaged/exposed bots caught in open ground.
  - Added the double Wooden Barricade Cover prefab, max-active-barricade cap, oldest-wall recycling, placement distance, hold/fight-commitment time, retreat-wall distance, and cooldown config.
  - v0.3.35 adds cumulative protection-damage tracking. Bots that lose at least 15% HP inside the configured short combat window can enter protection logic even before the old low-health threshold.
  - v0.3.35 makes protection distance skill-scaled: casual bots accept cover/bot-placed barricades within 8m, average bots within 5m, and dangerous bots within 3m before choosing to place a new wall between themselves and the threat.
  - v0.3.35 adds explicit long-range barricade anchor mode. After a long-range wall placement, a bot stays behind its tracked wall, peeks/strafes from behind it, and only unlocks a push after skill-scaled damage-dealt hitmarkers, target death, compromised cover, or the configured no-action timer.

- Phase 13/10 utility baseline:
  - v0.3.24 adds real F1 grenade and smoke grenade utility actions behind deterministic candidate checks.
  - `throw_grenade` can flush covered or last-known targets inside the configured throw range, while avoiding base-restricted positions, same-team bots, and non-target bystander players.
  - `throw_smoke` can screen a hurt or pressured bot's retreat lane when the bot is not already in effective cover.
  - Utility throws use a shared per-bot cooldown, team cooldown, active utility projectile cap, creator/owner attribution, and side-panel/`raidbots.list` utility diagnostics.
  - Fresh bot F1 throws create short-lived danger zones; bots inside the zone get a high-priority escape candidate, and movement commands avoid active grenade danger destinations.
  - v0.3.27 adds inventory-backed medical support for low-health cover healing. Bots can receive a configurable real medical item stack after kit application, consume allowed medical items when the cover-heal fire lock begins, apply the configured real-med heal budget during the lock, and report `Heal: real:<shortname>` or `fallback:<reason>` in diagnostics.
  - v0.3.35 categorizes configured medical items into syringe and non-syringe behavior. Non-syringe heals can top off while the bot may still shoot with valid LOS; syringe healing starts below the low-health threshold after cover/barricade protection and locks firing until the syringe window completes.

- Phase 14 baseline:
  - Added conservative base-restricted position detection around cupboards, building blocks, doors, and owned base-like deployables.
  - Spawn, tactical destination sampling, and movement commands now avoid base-restricted areas.
  - v0.3.29 adds physics surface validation for spawn and tactical destination sampling, rejecting navmesh points that sit underneath higher terrain/world geometry and reporting `physicalSurface` / `belowPhysical` in spawn diagnostics.
  - If a target is inside a base boundary or the path would cross one, the bot chooses `HoldOutsideBase` / abandon behavior instead of chasing or shooting inside.

- Phase 15 partial:
  - `raidbots.disable` stops runtime ticks and does not kill tracked bots by default.
  - Added `raidbots.nuke` for emergency removal of tracked bots.
  - `raidbots.nuke active`, `raidbots.nuke debug`, and `raidbots.nuke all` are now explicit admin safety modes.
  - Removed combat-leash despawn behavior.

- Phase 17 partial:
  - Added `raidbots.decisions last [count]`, `raidbots.decisions bot <name/key> [count]`, and `raidbots.decisions export` for reading JSONL decision traces from the console.
  - v0.3.44 adds automatic decision-trace pruning after trace flushes, on plugin/server reload, and during `raidbots.decisions export`; `raidbots.decisions prune` forces an immediate prune. Console trace reads now tail the JSONL file from the end instead of reversing the full file.
  - v0.3.30 adds `raidbots.advisor status|off|fallback|shadow|canary|stats|last [bot name/key]` so server owners can instantly disable advisor plumbing, inspect provider state without printing secrets, and read the latest advisor trace.

- Test/debug tooling:
  - Live-test fix on 2026-07-05: `raidbots.testsetup` and `raidbots.debug on` now enable floating debug nameplates above tracked bots for admins/test observers, making it possible to find bots in the world without relying on console-only position output.
  - Live-test helper on 2026-07-05: v0.3.2 nameplates now include per-viewer distance from the admin/test observer to the bot.
  - Live-test helper on 2026-07-05: v0.3.5 moves detailed tactical debug off the floating world label and into a static right-side admin panel for the closest bot. The overhead label is intentionally compact again: bot name, tactical state, and distance.
  - The side panel includes current signal (`visible`, `last_seen`, `heard`, `damaged`, or `none`), last selected action, LOS/exposure, skill, kit, HP, weapon/ammo, bot K/D, team, destination/cover distance, shooting, failed paths, advisor status, and fallback reason.
  - Live-test helper on 2026-07-05: v0.3.6 shrinks the overhead nameplate to a single small line and raises the default height so it stays above the bot model instead of covering it.
  - v0.3.7 adds `raidbots.squadtest <optional-player-name-or-steamid>` for duo/trio squad-role live testing.
  - The side panel and `raidbots.list` now expose squad role, base-restricted state, flank point, active bot barricade count, protection state, barricade-anchor state, and medical category/fire-lock status.
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
  - Live-test resilience fix on 2026-07-05: v0.3.26 adds configurable stuck destination memory (`Stuck Memory Seconds`, `Stuck Memory Radius`, `Maximum Stuck Memory Points`) and exposes `Bad spots` / `badspots=` diagnostics.
  - Deterministic polish on 2026-07-05: v0.3.27 adds squad destination reservation/formation offsets plus inventory-backed medical item grants/consumption for cover healing.
  - Spawn reliability fix on 2026-07-05: v0.3.29 rejects lower-layer navmesh samples hidden under hilly/mountain terrain and despawns runtime positions that later report as below the physical terrain/world surface.
  - Advisor adapter pass on 2026-07-05: v0.3.30 adds provider normalization, OpenAI-compatible chat-completions JSON/schema request bodies, website-proxy request bodies, async webrequest handling, pending-request caps, response validation, advisor stats, and richer advisor trace fields.
  - Release-default pass on 2026-07-05: v0.3.32 clears the tester-only anchor, moves the live rollout target to 6 with a hard cap of 12, restores solo/duo/trio team weights, leaves random land fallback off, disables debug surfaces by default, and makes foliage blocking less permissive by using a narrower cast, two foliage blockers, and a 24m clear-through distance.
  - Admin/high-cap pass on 2026-07-05: v0.3.33 adds the in-game `/raidbots admin` CUI with tabs for overview, population, spawn, AI, utility, rewards, advisor, debug, and danger controls. It also raises live default population to 50 and the normal hard cap to 200 while keeping reload disabled-by-default.
  - v0.3.34 separates debug UI from debug console output. Nameplates and the side panel can be enabled without console spam; detailed spawn/perception/tactical/advisor/cover logs require the new `Debug Console Logs` switch and repeated spawn warnings are throttled.
  - v0.3.35 adds `protect=`, `anchor=`, and categorized `heal=` diagnostics to `raidbots.list`, the side panel, advisor request payloads, and decision trace output.
  - v0.3.36 keeps the right-side debug panel root stable and refreshes only its text child. Opening the `/raidbots admin` CUI clears and suppresses the side panel until the admin CUI is closed, but later live testing showed that text refreshes could still be re-stacked above third-party CUI menus.
  - v0.3.37 suppresses and destroys the right-side debug side panel when common menu/admin UI commands fire, including Kits editor commands such as `kits.creator`, so the next debug refresh does not re-add the side panel above another plugin's menu. Close commands use a short suppression window so the panel can return shortly after the menu closes.
  - v0.3.38 fixes the v0.3.37 close-window bug where `kits.close` could not shorten an existing menu suppression timer. Normal menu commands now use a shorter fallback suppression window, while close commands actively release the side panel after a brief delay.
  - v0.3.39 adds `Aim:` to the debug side panel and `aim=` to `raidbots.list`, showing current projectile cone error and warmup progress for the active target.
  - v0.3.42 adds a `Learning` admin tab plus `raidbots.learn` commands for tester allowlisting, observation toggles, model status, build/report/export, apply mode, and player profile list/show/build/delete/weight management.
  - v0.3.50 extends `raidbots.learn report` and `raidbots.learn profile show` with target-context quality counts so training-file reviews can quickly distinguish combat-linked rows from inferred nearest-bot samples.
  - v0.3.51 extends `/raidbots admin` Debug controls with a native map marker toggle plus radius/update steppers, adds `raidbots.ui toggle map_markers debug` and numeric setting support for marker tuning, and includes `mapMarkers=...` in `raidbots.status`, `raidbots.diag`, and `raidbots.list`.
  - v0.3.55 relabels the map-marker radius admin control to `Marker meters` and keeps the radius setting human-readable while the runtime converts it to the tiny native map value.
  - v0.3.56 relabels the map-marker radius admin control to `Marker size`, globally broadcasts marker entities, and reports the effective native map radius in status/diag output.
  - v0.3.52 exposes stable RoamBot combat keys for external plugins through `API_IsRaidlandsRoamBot` and `API_GetRaidlandsRoamBotCombatKey`.
  - v0.3.54 exposes managed event-bot hooks for RaidlandsEvents: `REBOT_SpawnGroup`, `REBOT_SpawnSingle`, `REBOT_DespawnGroup`, `REBOT_DespawnForEvent`, `REBOT_GetBotOwner`, `REBOT_SetGroupObjective`, `REBOT_SetGroupLeash`, and `REBOT_IsBot`.
  - v0.3.53 records posture samples, crouched samples, crouch seconds, combat/shooting crouch seconds, transitions, and average crouch burst length in player observation traces. Built learned models now include a nested read-only posture model, but live profile application still only reweights already legal tactical candidates.
  - v0.3.43 adds admin/config controls for the advisor real-player gate and the hard-stuck despawn timer, plus `proximity_skips` in advisor stats.
  - v0.3.44 adds config-level decision trace retention controls and the manual `raidbots.decisions prune` command for live cleanup of oversized JSONL traces.
  - v0.3.45 adds the Advisor tab `Engaged only` switch, `engagedOnly=...` status output, `engagement_skips` advisor stats, and `engaged_with_real_player` / `engagement_signal` fields in allowed advisor payloads.
  - v0.3.48 normalizes `npcDamageScale` to `1.0` after kits are applied, when the active weapon is refreshed/reloaded, and when bot projectiles fire, so legacy scientist bodies no longer make player kit guns hit like reduced NPC weapons.
  - v0.3.59 removes the forced sustained-fire damage fallback and replaces it with passive bot-vs-bot damage diagnostics. The side-panel `Dmg:` line now shows bot weapon-fire hook age/count plus the latest damage-hook classification (`enemy_bot`, `enemy_bot_floor`, `same_clan_block`, `untracked_attacker`, `untracked_victim`, `player_to_bot`, or `bot_to_player`), and `raidbots.list` native combat diagnostics now evaluate the bot's actual memory target instead of the nearest real player.
  - v0.3.62 fixes bot bullet damage attribution for bot-vs-bot fights. Damage hooks now resolve the attacker through `HitInfo.InitiatorPlayer`, weapon owner, initiator owner, creator entity, parent/root parent, and owner ID before using raw `HitInfo.Initiator`, so rifle/M16 hits from tracked RoamBots can reach the existing `enemy_bot` / `enemy_bot_floor` path instead of being treated as untracked weapon/projectile damage.
  - v0.3.63 moves the `Dmg:` diagnostic up under HP/weapon so it is visible in the clipped side panel, and adds an `OnPlayerAttack`-backed bullet bridge for real projectile hits. Enemy-bot projectile hits now show `atk enemy_bot_hit`; if native damage arrives the bridge records `br native_seen`, otherwise it applies the hit once and records `br applied <damage>`.
  - v0.3.64 adds the RoamBots-owned bot-vs-bot tactical bullet resolver for the native-missing NPC-vs-NPC bullet path. Resolver shots show `sim applied <damage>`, `sim miss`, `sim blocked`, or `sim applied_no_loss` in `Dmg:`, and successful hits record `enemy_bot_resolver` so combat memory, K/D setup, and diagnostics see damage dealt/taken.
  - v0.3.65 polishes `/raidbots admin` for first-time operators: Home/Population/Spawn/Combat/Utility/Rewards/Advisor/Learning/Debug/Safety tabs get focused help, the panel gets clearer status and section chrome, text inputs get visible field backgrounds, core tabs include guardrail callouts, and in-panel Disable/Kill/Nuke/Reload actions require confirmation.
  - v0.3.66 adds toggleable Bot Chat: global chat observation via native/BetterChat hooks, clan-or-player conversation contexts, sanitized OpenAI-compatible short replies capped at 30 calls/hour, deterministic no-AI kill banter for player/bot and bot/bot kills, `raidbots.chat ...` controls, and a `/raidbots admin` Chat tab.
  - v0.3.67 halves the Bot Chat trigger odds after live testing showed the first pass was too chatty. Normal AI replies now trigger at 22.5%, direct mention replies at 45%, recent-fight replies at 22.5%, and deterministic kill-banter chances are 17.5% for player-killed-bot, 22.5% for bot-killed-player, and 20% for bot-killed-bot.
  - v0.3.68 adds player-like bot corpse loot. A bot's selected combat kit is mirrored into the corpse from Kits data, optional default-access utility kits can roll, and up to three bots in a team can roll a low-tier bonus kit. RoamBots now handles recent bot `OnCorpsePopulate` and asks BetterLoot to skip those corpses so the kit-shaped loot is not replaced by generic NPC loot.
  - v0.3.69 fixes the visible NPC corpse layout after live screenshot review. Rust exposes the RoamBot corpse as one `Scientist` loot grid, so death prep now flattens the full kit into the main visible grid in player-readable order: belt/hotbar, worn gear, then main inventory.
  - v0.3.70 fixes the remaining planned-kit mismatch. RoamBots now snapshots each bot's actual live inventory after kit application and medical grants, refreshes that snapshot on death, uses it before kit-data fallback, applies it again in `CanLootEntity`, extends recent corpse loot memory to ten minutes, and sets corpse identity toward a player-corpse panel/title instead of generic `Scientist`.
  - v0.3.71 mutes native legacy-scientist body chatter. `PrepareNpcBody` now suppresses `ScientistNPC` radio chatter and death chatter by clearing the native radio/death effect arrays and cancelling any queued `PlayRadioChatter` action, without changing RoamBots sound investigation, bot chat, weapon fire, or tactical AI.
  - v0.3.72 tightens dense-bush and forest LOS fairness. Shooting now requires 0.34 exposed target fraction, foliage probes use a 0.9m sphere cast, clear-through foliage allowance is 14m, one foliage blocker hides a probe, and config validation now clamps invalid values without resetting stricter operator tuning on reload.
  - v0.3.73 adds smoke-aware LOS fairness. RoamBots tracks active player-thrown and bot-thrown smoke sources, treats the configured smoke volume as an opaque sight blocker during target probe checks, exposes `Smoke LOS` and `Smoke rad` controls in `/raidbots admin`, and reports smoke blocks through `Sight: smoke ...` while the fire gate remains `Fire: no_los`.
  - v0.3.74 fixes corpse-loot duplication. `OnCorpsePopulate` and the `CanLootEntity` fallback now share a per-corpse populate-once guard, so access checks and reopening a partially looted RoamBot corpse cannot rebuild its original saved inventory. The guard is removed when the corpse entity is killed.
  - v0.3.76 adds ambient-only squad respawn rejoin. Dead ambient bots capture their internal team id and, after the normal respawn delay, try to respawn directly beside a healthy living teammate from that team. Teammates below the configured health fraction or damaged inside the recent-damage window are skipped. Rejoined bots hold perception and tactical decisions during the wake/equip delay, with `wake=squad_rejoin:Ns` visible in `raidbots.list`; managed/event bots keep their existing owner-driven lifecycle.

- Bot kill integration:
  - v0.3.31 adds player-like roam bot kill chat, tracked-bot DeathNotes suppression, and optional ServerRewards RP payout for real players who kill roam bots.
  - v0.3.34 extends kill chat to bot kills against real players and bot kills against different-clan bots. The killfeed template now supports `{weapon}`, `{method}`, `{distance}`, `{distance_m}`, `{killer_clan}`, and `{victim_clan}`, and DeathNotes suppression applies to any death involving a tracked roam bot to avoid duplicate generic NPC lines.
  - v0.3.46 added configurable bot avatar identities. Each spawned bot picks one configured avatar, and bot-caused death chat can be sent through `chat.add` with that avatar's configured chat SteamID.
  - v0.3.47 removes the RoamBots CUI kill-feed popup and patches the victim's native Rust `PlayerLifeStory.DeathInfo` with the bot name/avatar SteamID so the real death screen `KILLED BY` card targets the bot identity instead of generic `Scientist`.
  - v0.3.49 hardens native death-screen identity by feeding the same bot death info through Rust's `BasePlayer.SetOverrideDeathBlow(...)` path, setting attacker distance, and clearing only the lethal bot hit's native `HitInfo.Initiator` after RoamBots records the pending killer. This prevents `ScientistNPC.AttackerInfo(...)` from overwriting the custom death-screen card back to `Scientist` while preserving RoamBots stats/killfeed attribution.
  - v0.3.46 adds five generated local portrait assets under `oxide/data/RaidlandsRoamBots/avatars/`: `raider-red.png`, `scrap-jacket.png`, `hazmat-echo.png`, `roadside-ghost.png`, and `launch-watcher.png`.

### Verified locally

```text
Roslyn compile check against RustDedicated_Data/Managed completed with no errors through v0.3.76 ambient squad respawn rejoin.

v0.3.75 adds `/lastbotinfo` for every player, with `/raidbots showlastinfo` retained as an alias. After a player kills a Raidlands bot or is killed by one, the command shows that opponent's name, fight result, learned/default profile, training source, difficulty/skill tier, kit, clan, and lifetime K/D snapshot. No permission is required; the existing `/raidbots admin` surface remains admin-only.
v0.3.76 adds the ambient squad respawn rejoin path and the `Ambient Squad Respawn Rejoin` config block. Local verification covered JSON parsing and Roslyn compile only; live death/respawn timing still needs an in-game smoke test.
The v0.3.76 checked-in config was refreshed from the attached live JSON first, so live smoke LOS settings, expanded kit/profile lists, observed SteamIDs, and learned profile weights such as `fireclan=25` are preserved alongside the new rejoin block.
Compile used the local duplicate-reference workaround: include `Newtonsoft.Json.dll` and exclude `Oxide.References.dll` plus the duplicate Newtonsoft provider.
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
- This confirms early reload/spawn/kit/nav/diagnostics smoke only. The v0.3.29 terrain-layer spawn guard, v0.3.30 advisor adapter commands, v0.3.31 kill chat/RP integration, v0.3.32 release defaults, v0.3.33 admin panel/high-cap defaults, v0.3.34 enhanced killfeed/quiet-console/clan-war behavior, v0.3.35 protection/healing/barricade-anchor polish, v0.3.40 sharper aim tuning, v0.3.42 learned skill/profile behavior, v0.3.43 advisor proximity gate and hard-stuck timer, v0.3.44 decision trace retention, v0.3.45 active player engagement advisor gate, v0.3.47/v0.3.49 native death-screen identity, v0.3.48 NPC weapon damage normalization, v0.3.50 victim-linked observation context, v0.3.51/v0.3.55/v0.3.56 native map markers, v0.3.57 clan-war damage floor, v0.3.58 advisor real-player fight gate, v0.3.59 bot-vs-bot damage diagnostics, v0.3.62 bot bullet damage attribution, v0.3.63 bot bullet hit bridge, v0.3.64 bot-vs-bot tactical bullet resolver, v0.3.52 BGrade API integration, v0.3.53 crouch posture, v0.3.54 managed event bot API, v0.3.72 stricter foliage LOS, v0.3.73 all-smoke LOS blocking, v0.3.76 ambient squad respawn rejoin, wall-hold, squad, formation reservation, cover, inventory-backed medical item use, auto-reload, hard-stuck, stuck-memory, grenade, smoke, grenade danger-zone, player-like health, and normal-damage behavior still need in-game combat/pathing retests after upload/reload.
```

### Stop point for in-game testing

Please live-test v0.3.76 before I implement advisor-selected action execution, base assault logic, full leader/follower pathing, or full native medical animation/effect parity. This pass preserves the deterministic tactical brain, adds toggleable bot chat with OpenAI chat quota controls and deterministic no-AI kill banter, cuts bot chat trigger odds to half of the first live pass, keeps player-like bot corpse loot one-time populated per corpse, mutes native legacy-scientist radio/death chatter on RoamBot bodies, tightens dense-bush/forest/smoke LOS fairness, and adds ambient squad respawn rejoin beside healthy teammates with a wake/equip delay. Live validation is still required for the rejoin timing/teammate-skip behavior and the broader combat/pathing smoke ladder.

Recommended first test ladder:

```text
1. oxide.reload RaidlandsRoamBots
2. oxide.reload BGrade
3. In game, run `/raidbots admin`.
4. Confirm the admin panel opens on Home with readable Runtime/Ambient/Managed/Anchor status, a clear tab row, section bands, and visible input fields where text entry is available.
5. Open each primary tab and click the `?` button; confirm each help popup opens and closes.
6. On Home or Population, click Disable and confirm it opens a confirmation dialog instead of immediately disabling runtime; cancel it.
7. On Safety, click Kill Active, Nuke Active, Nuke All, and Reload Config/Data; confirm each opens a matching confirmation dialog and can be cancelled.
8. Open Learning -> Observe, Train, Profiles, and Mix; confirm each subpage renders, `?` popups open/close, and Mix shows `ababmxking=50`, `sudol=50`, `Default=0`.
9. On Learning -> Mix, test `+1`, `-1`, `0`, and exact input on one profile; confirm values save, redraw, and keep the custom/default total at 100.
10. On Learning -> Train, type a player query/profile key and confirm the draft fields display. Only build a new profile after enough observation traces exist.
11. In Debug, enable Map markers and note the warning that native markers may be visible to all players.
12. raidbots.diag
13. raidbots.enable 3
14. Open the native Rust map and confirm all active bots have visible, compact skill-colored radius markers, with no map-sized red/yellow/green disks. `raidbots.diag` should show `mapMarkers=on <count>/<active> native=0.047` when the size setting is 8.
15. Move/fight long enough to confirm markers follow bots within the configured update interval.
16. raidbots.list
17. Confirm a legacy scientist body spawns with a Raidlands kit.
18. Listen near a spawned RoamBot for at least one normal idle/alert window and while damaging/killing one. Expected: no native scientist radio chatter, scientist alert voice, or scientist death chatter; weapon fire and RoamBots chat/killfeed behavior still work normally.
19. Let two different-clan bots fight with rifle/M16/SMG bullets in clear LOS and watch HP, killfeed, and the now-visible side-panel `Dmg:` line. Expected: bullet hits reduce HP and can produce a bot kill. If native damage handles the hit, `Dmg:` shows `hook in/out:enemy_bot` or `enemy_bot_floor`; if the bridge handles it, `Dmg:` shows `atk enemy_bot_hit` and `br applied <damage>`; if the v0.3.64 resolver handles the native-missing path from live testing, `Dmg:` shows `sim applied <damage>`, `sim miss`, or `sim blocked`. If bullets still fail while grenades work, capture `Dmg:` plus `raidbots.list` for `nav=...combat:target=target_bot/canAttack=.../inRange=.../best=...`; `fire n/a#0 atk none hook none sim miss/blocked` means Rust is still emitting no bullet hooks but the resolver is deciding shots are missing or blocked, while `sim applied_no_loss` means another hook is preventing the applied bullet damage from moving HP.
20. BGrade smoke: run `/bgrade 3`, wait over 30 seconds, place a block, and confirm BGrade still upgrades.
21. PvP smoke: have a player or RoamBot shoot you with direct ranged damage and confirm BGrade pauses for 30 seconds, then resumes the selected grade.
22. Cancel smoke: after one player/RoamBot deals no more than 30 percent health damage, kill that same attacker and confirm BGrade resumes immediately.
23. Stand behind terrain/walls and confirm it investigates or roams but does not shoot.
24. Step into LOS and confirm it reacts after a short delay, only shoots with LOS, visually crouches during valid shooting/peek windows, and shows `crouch=shooting` in `raidbots.list`.
25. Toggle the normal admin invis/Vanish feature while the bot has or recently had LOS. Expected: the bot stops shooting within the next tick or hook, `raidbots.list` no longer shows that player as the active target, and the side panel stops showing that player as target.
26. While still invisible, fire a gun or explosive near the bot. Expected: no sound investigation is created for the hidden admin, and `raidbots.list` does not move to `InvestigateSound` for that admin.
27. Toggle invis off and step back into LOS. Expected: normal visible-player targeting can resume.
28. Damage a bot below the low-health threshold, force it into effective cover or a barricade heal, and confirm the side panel shows `Heal:` with a syringe lock while `Crouch:` shows `cover-heal`.
29. Break LOS or wait for the fight to end and confirm crouch clears after the short configured delay.
30. Damage it from cover and confirm it investigates the damage/heard position.
31. raidbots.killall, then confirm native map markers disappear and no bot remains crouched.
32. Disable Map markers in Debug and confirm no marker entities remain.
33. Check oxide/data/RaidlandsRoamBots/decision_traces.jsonl for fallback traces when hard decisions trigger, including `crouch_state`.
34. Use raidbots.list while fighting and watch exposure=X.XX(Y/Z), weapon=<class>, aim=<degrees>/<warmup>, learn=<mode/model>, cover=<point>, stuck=<bool>, protect=<state>, anchor=<state>, heal=<reason>, crouch=<state>, formation=<reason>, utility=<reason>, and mapMarkers=<state/count>.
35. Learning smoke: `raidbots.learn allow <tester>`, `raidbots.learn observe on`, fight near bots while crouching/shooting, then `raidbots.learn report 60` and `raidbots.learn build`; confirm posture stats appear but do not alter live crouch timing.
36. Profile smoke after enough traces: `raidbots.learn profile build <tester> <profileKey>`, `raidbots.learn profile show <profileKey>`, `raidbots.learn profile weight <profileKey> 10`, `raidbots.learn apply profiles`, spawn a fresh bot, and confirm public names stay fictional while admin/debug surfaces show the profile key/source plus read-only posture stats.
37. If the three-bot smoke looks healthy, ramp through `raidbots.target 25`, then `raidbots.target 50`, while watching server performance and spawn spacing.
```

v0.3.29 terrain-layer spawn retest:

```text
1. Reload the plugin with Debug Spawn Details enabled.
2. Move the near-player anchor or tester to hilly/mountain terrain where bots previously spawned into the lower hidden bubble.
3. Run raidbots.nuke, then raidbots.squadtest <optional-player-name-or-steamid> and raidbots.enable 3.
4. Expected: accepted spawn diagnostics include physicalSurface=... and belowPhysical=False.
5. Expected: bots either spawn on the playable upper surface, pick a different valid nearby position, or despawn quickly if their runtime position becomes invalid.
6. Regression case: no bot should remain alive as a protected shooter under the visible terrain/world surface.
```

Additional retest after the 2026-07-05 visibility fix:

```text
1. Reload the plugin.
2. Spawn one bot with the same near-player test setup.
3. Put multiple trees/bushes between player and bot at 40m-120m.
4. Expected: dense foliage shows `Sight: foliage ...` or exposure below 0.34, with `Fire: no_los` if the bot has a remembered target.
5. Expected: raidbots.list should show target=none, heard, or memory, not target=visible with high exposure.
6. Expected: the bot may investigate or reposition, but should not keep firing through foliage without a clean LOS.
7. Step into open ground or a genuinely clear lane at similar distance.
8. Expected: the bot can reacquire and shoot after reaction delay.
```

Additional retest after the 2026-07-09 smoke LOS fix:

```text
1. Reload the plugin.
2. Spawn one bot with the same near-player test setup and confirm clean-lane shooting still works.
3. Throw a smoke grenade between the player and bot while the bot has or recently had target memory.
4. Expected: the bot stops firing within the next perception/fire check, `Sight: smoke ...` appears, and `Fire: no_los` appears if the bot still remembers the target.
5. Expected: the bot may search, flank, retreat, or reposition using memory/hearing, but it should not keep shooting through the smoke volume.
6. Step out of the smoke into a clear lane.
7. Expected: the bot reacquires after reaction delay and can shoot normally.
8. Hurt a bot until it throws smoke; expected bot-thrown smoke also blocks RoamBot shooting through that screen.
```

Additional retest after the 2026-07-10 ambient squad respawn rejoin:

```text
1. Upload oxide/plugins/RaidlandsRoamBots.cs and oxide/config/RaidlandsRoamBots.json, then run oxide.reload RaidlandsRoamBots.
2. Set a low test target with duo/trio teams, for example raidbots.squadtest <tester> followed by raidbots.enable 3, or use the live random-land mode and locate a same-team pair with raidbots.list.
3. Kill one bot from a team while at least one teammate from the same internal TeamId is still alive, full or near-full health, and not recently damaged.
4. After Respawn Delay Seconds, expected: the replacement spawns directly beside the healthy teammate, keeps the same internal team id, and raidbots.list briefly shows wake=squad_rejoin:Ns.
5. During the wake/equip delay, expected: the new bot does not acquire targets, move to a tactical destination, or fire.
6. Damage the surviving teammate, then kill another bot from that team before the Teammate Recent Damage Window Seconds expires.
7. Expected: the damaged/recently damaged teammate is skipped; if no other healthy teammate is available, normal population maintenance runs and the replacement follows the current random-land spawn mode.
8. Kill the entire squad.
9. Expected: no teammate rejoin is attempted because no living teammate remains; normal random-land population maintenance restores the missing ambient count.
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

v0.3.29 stuck-memory retest:

```text
1. Reload the plugin and spawn one bot near the anchor.
2. Put a cliff, large rock, wall, or base edge between the bot and one likely retreat/search destination.
3. Expected: when the bot fails to make progress, the side panel shows `Bad spots: 1 (...)` and `raidbots.list` includes `badspots=1`.
4. Keep pressure on the bot without killing it.
5. Expected: its next recovery, retreat, cover, investigate, flank, regroup, push, or roam destination should move around the remembered bad spot instead of repeatedly selecting the same blocked point.
6. Wait longer than `AI -> Stuck Memory Seconds`.
7. Expected: the bad spot count decays back down, allowing that area to be reconsidered later if the fight context changes.
```

v0.3.35 clan/foliage/base/barricade/protection/healing/ammo/utility/health/damage retest:

```text
1. raidbots.nuke
2. oxide.reload RaidlandsRoamBots
3. raidbots.squadtest <optional-player-name-or-steamid>
4. raidbots.enable 3
5. raidbots.list
6. Expected: bots show clan=<tag>, role=anchor/flanker/pusher, formation=<reason>, share last-known info, and only bots with their own LOS shoot.
7. Fight near cover and watch for search_last_known, flank_left/flank_right, or regroup_with_squad traces via raidbots.decisions last 10.
8. Enter a player base or stand just inside a base boundary.
9. Expected: bots hold outside, reposition, or abandon instead of chasing/shooting through the base.
10. Damage an exposed bot for more than 15% HP inside the protection window.
11. Expected: `protect=` / `Protect:` should show the protection trigger/source, the bot should prefer nearby natural cover or a tracked bot-placed barricade inside its skill distance, and only place a new Wooden Barricade Cover when no acceptable protection exists.
12. Damage a bot below 60% HP and keep pressure on it.
13. Expected: protection comes before syringe recovery. The bot should choose cover/barricade/tuck first, then `Heal:` should show syringe cover healing and `Fire:` should show `syringe_lock` until the syringe window completes.
14. Damage a bot to roughly 60%-99% HP while it has valid LOS.
15. Expected: non-syringe medical behavior can top off health while the bot may still shoot. Diagnostics should distinguish `heal=non_syringe...` from syringe fire-lock behavior.
16. Force a long-range wall fight beyond the skill-scaled barricade-anchor threshold.
17. Expected: after placing the wall, `anchor=` / `Anchor:` should show an active anchor. The bot should hold/peek/strafe from behind the wall and only push after the configured damage-dealt hitmarker count, target death, compromised cover, or no-action timer.
18. If the bot moves near cover but remains exposed, expected: the side panel should show `Cover: compromised` and the bot should re-plan cover/wall instead of standing still to heal.
19. Put bushes/trees between you and the bots at 40m-120m.
20. Expected: `raidbots.list` / the side panel should not show `LOS: Y` with exposure at or above 0.34 while the bot is visually hidden by dense foliage. It may search or reposition, but should not keep firing through the bush.
21. Break LOS around 150m-190m after contact.
22. Expected: clan members should keep searching/pushing the last-known or damage position during the fresh-contact window instead of immediately scattering back to a neutral regroup. When two bots would choose the same shared destination, later decisions should show `formation=...offset...` or the side panel should show `Formation: ...offset...`, and bots should spread instead of stacking directly on one point.
23. If a bot stands in place with `stuck=Y`, watch the panel and `raidbots.list`. Expected: it should select stuck recovery; if it remains latched as stuck for `AI -> Hard Stuck Despawn Seconds` (180s default) or failed paths continue past the hard-stuck threshold, the tracked bot despawns and queues a replacement check after the configured respawn delay.
24. Damage a low-health bot while its nearest retreat cover is farther than 10m.
25. Expected: it should choose `place_barricade` before crossing the open field, then fight from the wall for the configured commitment/anchor rules unless the wall is compromised or the target leaves useful range.
26. If `Shooting: N` appears while the bot has a target, check `Fire:`. Expected blockers are concrete reasons such as `no_ammo`, `no_los`, `out_of_range`, `syringe_lock`, `target_in_base`, `no_attack_interface`, or `start_failed`.
27. If a bot's held gun starts empty, expected: `Auto Reload Bot Weapons` refills the active projectile magazine so it does not stay trapped at `Ammo: 0.00`.
28. Fight in sparse forest cover at 40m-120m.
29. Expected: a clearly visible player should still be targetable in a clean lane or light brush, but dense brush should now suppress fire more often than the v0.3.71 release profile. If behavior looks wrong either way, capture `Sight:`, `Fire:`, and `Exposure:` together.
30. Force a wall on uneven terrain or across elevation.
31. Expected: the bot should only use the wall if a close behind-wall hold point exists. If not, the panel should show `Wall: hold_failed_slope` and it should re-plan instead of running to fake cover far away.
32. Break LOS or hold near cover around 12m-42m after contact.
33. Expected: a bot may choose `throw_grenade`, spawn a real F1, enter `GrenadeFlush`, and then move away or back to cover. Squadmates inside the danger radius should choose an escape move instead of standing in the blast lane.
34. Hurt a bot while exposed around 10m-55m.
35. Expected: a bot may choose `throw_smoke`, spawn a real smoke grenade, enter `Retreat`, move away through the screen, and treat that smoke as LOS-blocking cover for RoamBot shooting.
36. If utility does not happen, expected `Utility:` blockers include `utility_cd`, `team_cd`, `grenade_range`, `grenade_ally_close`, `grenade_bystander_close`, `utility_base_blocked`, or `utility_cap`.
```

v0.3.54 managed event bot API smoke:

```text
1. Upload oxide/plugins/RaidlandsRoamBots.cs and oxide/config/RaidlandsRoamBots.json, then run oxide.reload RaidlandsRoamBots.
2. From a temporary provider or RCON helper, call REBOT_SpawnGroup with OwnerPlugin=RaidlandsEvents, a unique EventInstanceId, GroupKey, Count=2, two explicit SpawnPoints, Difficulty=elite, and a Leash center/radius.
3. Expected: response has Success=true, a GroupId, two EntityIds, and only metadata fallback warnings for unmapped loadout/profile details.
4. Run raidbots.list; expected: the two bots show owner=RaidlandsEvents:<event>/<group> and a non-empty leash=..., while raidbots.status shows them in the managed count, not the ambient count.
5. Call REBOT_GetBotOwner and REBOT_IsBot for each returned entity/net ID; expected: owner dictionaries include OwnerPlugin, OwnerKind=managed_event, EventInstanceId, GroupId, GroupKey, Profile, Difficulty, and EntityId.
6. Call REBOT_SetGroupLeash and REBOT_SetGroupObjective; expected: no errors and subsequent raidbots.list reflects the leash update.
7. Call REBOT_DespawnGroup; expected: those bots despawn without ambient refill unless ambient target population itself is below target.
8. Repeat with two groups under the same EventInstanceId, then call REBOT_DespawnForEvent; expected: all event-owned groups are cleaned up and no orphaned managed groups remain.
```

v0.3.30+ optional advisor adapter smoke:

```text
1. oxide.reload RaidlandsRoamBots
2. raidbots.advisor status
3. Expected live advisor profile: enabled=True, provider=openai_compatible, mode=shadow, engagedOnly=on/45s, playerGate=350m, endpoint=set, model=set, pending=0/2.
4. Put the API key in the Rust server process environment as `OPENAI_ROAM_BOT_API_KEY`, then run `raidbots.reload` or `oxide.reload RaidlandsRoamBots`.
5. Run `raidbots.advisor shadow`.
6. Leave only idle/outpost players within 350m of active bots and wait for hard decision triggers, then run `raidbots.advisor stats`.
7. Expected idle/not-engaged case: `engagement_skips` should increase and `submitted` should not increase for those skipped decisions.
8. Move more than 350m away from an actively engaged bot/player fight and wait for hard decision triggers, then run `raidbots.advisor stats`.
9. Expected away from engaged players: `proximity_skips` should increase and `submitted` should not increase for those skipped decisions.
10. Fight or path-test within 350m until a hard decision trigger fires, then run `raidbots.decisions last 5`.
11. Expected with a configured advisor and active nearby real-player engagement: responses may validate or reject, but the selected heuristic action still executes normally in this adapter pass.
12. Run `raidbots.advisor stats`; pending requests should return to 0 after responses/timeouts.
13. Run `raidbots.advisor off` before normal release play unless intentionally keeping shadow telemetry on.
```

### Known not-yet-implemented

```text
cover/peek quality tuning beyond the v0.3.35 barricade-anchor baseline
grenade/smoke throw arc, damage, and smoke-screen tuning beyond the baseline after live validation
full native med-item animation/effect parity; v0.3.35 categorizes and consumes real medical inventory where available but still applies a controlled heal budget
full leader/follower pathing and path reservation; v0.3.29 has first-pass destination reservation/formation offsets only
base objective validation, base assault, and smarter "is this base worth holding" logic
executing advisor-selected actions in shadow/canary/canary-live modes
live endpoint tuning for OpenAI-compatible or website-proxy advisor deployments
```
