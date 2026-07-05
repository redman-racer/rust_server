using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.AI;

namespace Oxide.Plugins
{
    [Info("RaidlandsRoamBots", "Raidlands", "0.3.22")]
    [Description("Spawns player-like roaming NPCs with Raidlands kits, separate NPC stats, and admin controls.")]
    public class RaidlandsRoamBots : RustPlugin
    {
        private const string AdminPermission = "raidlandsroambots.admin";
        private const string SpawnModeNearPlayers = "near_players";
        private const string SpawnModeRandom = "random";
        private const string TacticalBrainName = "playerlike_tactical_brain";
        private const string DecisionTraceDataFile = "RaidlandsRoamBots/decision_traces.jsonl";
        private const string StatsDataFile = "RaidlandsRoamBots/stats";
        private const string KitsDataFile = "Kits/kits_data";
        private const string WoodenBarricadeCoverPrefab = "assets/prefabs/deployable/barricades/barricade.cover.wood_double.prefab";
        private const float RetreatFallbackReturnFireAfterSeconds = 2.5f;
        private const float RetreatFallbackTimeoutSeconds = 8f;
        private const float MinimumAmmoFractionToShoot = 0.01f;
        private const int ForestSplatMask = 32;
        private const string ScoreboardNpcKills = "NPC Kills";
        private const string ScoreboardDeathsByNpc = "Killed by NPCs";
        private const string ScoreboardBotKd = "Bot K/D";
        private const string ScoreboardBotClanKd = "Bot Clan K/D";
        private const string DebugBotPanelUi = "RaidlandsRoamBots.DebugBotPanel";

        [PluginReference]
        private Plugin Kits;

        [PluginReference]
        private Plugin Scoreboards;

        private Configuration config;
        private StoredData data;
        private readonly System.Random random = new System.Random();
        private readonly Dictionary<BaseCombatEntity, BotRuntime> activeBots = new Dictionary<BaseCombatEntity, BotRuntime>();
        private readonly HashSet<BaseCombatEntity> despawningBots = new HashSet<BaseCombatEntity>();
        private readonly List<BaseEntity> botPlacedEntities = new List<BaseEntity>();
        private readonly Dictionary<string, KitEligibility> eligibleKits = new Dictionary<string, KitEligibility>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, SquadBlackboard> squadBlackboards = new Dictionary<int, SquadBlackboard>();
        private readonly Dictionary<string, float> recentSoundBroadcasts = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<DecisionTrace> pendingDecisionTraces = new List<DecisionTrace>();
        private IDecisionAdvisor decisionAdvisor;
        private Timer maintainTimer;
        private Timer perceptionTimer;
        private Timer brainTimer;
        private Timer squadTimer;
        private Timer nameplateTimer;
        private Timer scoreboardTimer;
        private Timer decisionTraceSaveTimer;
        private Timer saveTimer;
        private int teamSequence;
        private float spawnRetryBlockedUntil;

        private class Configuration
        {
            public bool Enabled = false;

            [JsonProperty("Target Population")]
            public int TargetPopulation = 15;

            [JsonProperty("Minimum Allowed Population")]
            public int MinAllowedPopulation = 0;

            [JsonProperty("Maximum Allowed Population")]
            public int MaxAllowedPopulation = 30;

            [JsonProperty("Team Size Weights")]
            public Dictionary<string, int> TeamSizeWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["solo"] = 60,
                ["duo"] = 30,
                ["trio"] = 10
            };

            [JsonProperty("Bot Clans")]
            public List<BotClanDefinition> BotClans = new List<BotClanDefinition>
            {
                new BotClanDefinition { Key = "north-road", Tag = "NR", Name = "North Road" },
                new BotClanDefinition { Key = "excavator-crew", Tag = "EAC", Name = "Excavator Crew" },
                new BotClanDefinition { Key = "wipe-team", Tag = "WT", Name = "Wipe Team" },
                new BotClanDefinition { Key = "launch-site", Tag = "LS", Name = "Launch Site" }
            };

            [JsonProperty("Skill Weights")]
            public Dictionary<string, int> SkillWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["casual"] = 25,
                ["average"] = 60,
                ["dangerous"] = 15
            };

            [JsonProperty("Skill Definitions")]
            public Dictionary<string, SkillDefinition> SkillDefinitions = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["casual"] = new SkillDefinition { Health = 125f, DamageScale = 0.78f, IncomingDamageScale = 1.15f, ReactionMinSeconds = 0.75f, ReactionMaxSeconds = 1.35f, AimErrorDegrees = 5f, Aggression = 0.35f, Courage = 0.35f, TacticalNoise = 0.25f },
                ["average"] = new SkillDefinition { Health = 150f, DamageScale = 1f, IncomingDamageScale = 1f, ReactionMinSeconds = 0.4f, ReactionMaxSeconds = 0.85f, AimErrorDegrees = 3f, Aggression = 0.55f, Courage = 0.55f, TacticalNoise = 0.15f },
                ["dangerous"] = new SkillDefinition { Health = 190f, DamageScale = 1.18f, IncomingDamageScale = 0.9f, ReactionMinSeconds = 0.18f, ReactionMaxSeconds = 0.45f, AimErrorDegrees = 1.5f, Aggression = 0.8f, Courage = 0.8f, TacticalNoise = 0.06f }
            };

            [JsonProperty("High Tier Kit Weight")]
            public int HighTierKitWeight = 5;

            [JsonProperty("Kit Selection")]
            public KitSelectionConfig Kits = new KitSelectionConfig();

            [JsonProperty("Spawn Settings")]
            public SpawnConfig Spawn = new SpawnConfig();

            [JsonProperty("Prefab Candidates In Order")]
            public List<string> PrefabCandidates = new List<string>
            {
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_junkpile_pistol.prefab"
            };

            public AIConfig AI = new AIConfig();

            [JsonProperty("Decision Advisor")]
            public DecisionAdvisorConfig DecisionAdvisor = new DecisionAdvisorConfig();

            public PersistenceConfig Persistence = new PersistenceConfig();

            public DebugConfig Debug = new DebugConfig();

            [JsonProperty("Spawn Failure Retry Seconds")]
            public float SpawnFailureRetrySeconds = 120f;

            [JsonProperty("Bot Profiles")]
            public List<string> BotProfiles = new List<string>
            {
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
            };

            [JsonProperty("Maintain Interval Seconds")]
            public float MaintainIntervalSeconds = 20f;

            [JsonProperty("Scoreboard Interval Seconds")]
            public float ScoreboardIntervalSeconds = 60f;

            [JsonProperty("Respawn Delay Seconds")]
            public float RespawnDelaySeconds = 20f;
        }

        private class KitSelectionConfig
        {
            [JsonProperty("Default Group")]
            public string DefaultGroup = "default";

            [JsonProperty("Eligible Kit Names")]
            public List<string> EligibleKitNames = new List<string> { "ak", "lr300", "m16", "mp5" };

            [JsonProperty("Rare High Tier Kit Names")]
            public List<string> RareHighTierKitNames = new List<string> { "raid" };

            [JsonProperty("Weapon Shortnames")]
            public List<string> WeaponShortnames = new List<string>
            {
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
            };
        }

        private class SpawnConfig
        {
            [JsonProperty("Spawn Mode")]
            public string SpawnMode = SpawnModeNearPlayers;

            [JsonProperty("Use Random Land Fallback")]
            public bool UseRandomLandFallback = true;

            [JsonProperty("Max Position Attempts")]
            public int MaxPositionAttempts = 80;

            [JsonProperty("Navmesh Sample Distance")]
            public float NavmeshSampleDistance = 12f;

            [JsonProperty("Minimum Above Water")]
            public float MinimumAboveWater = 1.5f;

            [JsonProperty("Require Land Spawns")]
            public bool RequireLandSpawns = true;

            [JsonProperty("Minimum Land Height")]
            public float MinimumLandHeight = 0f;

            [JsonProperty("Maximum Below Terrain Tolerance")]
            public float MaximumBelowTerrainTolerance = 0.75f;

            [JsonProperty("Runtime Invalid Position Despawn Seconds")]
            public float RuntimeInvalidPositionDespawnSeconds = 2f;

            [JsonProperty("Group Spawn Radius")]
            public float GroupSpawnRadius = 12f;

            [JsonProperty("Use Generated Positions Near Players")]
            public bool UseGeneratedPositionsNearPlayers = true;

            [JsonProperty("Near Player Minimum Distance")]
            public float NearPlayerMinDistance = 80f;

            [JsonProperty("Near Player Maximum Distance")]
            public float NearPlayerMaxDistance = 300f;

            [JsonProperty("Near Player Attempts Per Bot")]
            public int NearPlayerAttempts = 64;

            [JsonProperty("Near Player Anchor Name Or SteamID")]
            public string NearPlayerAnchorNameOrSteamId = "";

            [JsonProperty("Avoid Safe Zone Spawns")]
            public bool AvoidSafeZoneSpawns = true;

            [JsonProperty("Ignore Players In Safe Zones")]
            public bool IgnorePlayersInSafeZones = true;

            [JsonProperty("Safe Zone Spawn Buffer Distance")]
            public float SafeZoneSpawnBufferDistance = 75f;
        }

        private class SkillDefinition
        {
            public float Health = 150f;
            public float DamageScale = 1f;
            public float IncomingDamageScale = 1f;
            public float ReactionMinSeconds = 0.4f;
            public float ReactionMaxSeconds = 0.85f;
            public float AimErrorDegrees = 3f;
            public float Aggression = 0.55f;
            public float Courage = 0.55f;
            public float TacticalNoise = 0.15f;
        }

        private class BotClanDefinition
        {
            public string Key = "";
            public string Tag = "";
            public string Name = "";
        }

        private class AIConfig
        {
            [JsonProperty("Perception Tick Seconds")]
            public float PerceptionTickSeconds = 0.25f;

            [JsonProperty("Decision Tick Seconds")]
            public float DecisionTickSeconds = 0.35f;

            [JsonProperty("Squad Tick Seconds")]
            public float SquadTickSeconds = 0.75f;

            [JsonProperty("Vision Range")]
            public float VisionRange = 190f;

            [JsonProperty("Vision Fov Degrees")]
            public float VisionFovDegrees = 220f;

            [JsonProperty("Close Awareness Radius")]
            public float CloseAwarenessRadius = 22f;

            [JsonProperty("Minimum Exposed Target Fraction")]
            public float MinimumExposedTargetFraction = 0.25f;

            [JsonProperty("Minimum Exposed Target Fraction To Shoot")]
            public float MinimumExposedTargetFractionToShoot = 0.25f;

            [JsonProperty("Foliage Blocks Vision")]
            public bool FoliageBlocksVision = true;

            [JsonProperty("Foliage Vision Check Radius")]
            public float FoliageVisionCheckRadius = 0.9f;

            [JsonProperty("Maximum Clear Vision Through Foliage")]
            public float MaximumClearVisionThroughFoliage = 14f;

            [JsonProperty("Foliage Hits To Block Vision")]
            public int FoliageHitsToBlockVision = 1;

            [JsonProperty("Foliage Terrain Sampling")]
            public bool FoliageTerrainSampling = true;

            [JsonProperty("Foliage Terrain Sample Step")]
            public float FoliageTerrainSampleStep = 6f;

            [JsonProperty("Foliage Terrain Samples To Block Vision")]
            public int FoliageTerrainSamplesToBlockVision = 4;

            [JsonProperty("Foliage Occluder Layer Names")]
            public List<string> FoliageOccluderLayerNames = new List<string> { "Tree", "Resource", "World", "Default" };

            [JsonProperty("Target Memory Seconds")]
            public float TargetMemorySeconds = 24f;

            [JsonProperty("Search Last Seen Seconds")]
            public float SearchLastSeenSeconds = 38f;

            [JsonProperty("Unsuppressed Gunshot Hearing Range")]
            public float UnsuppressedGunshotHearingRange = 240f;

            [JsonProperty("Suppressed Gunshot Hearing Range")]
            public float SuppressedGunshotHearingRange = 85f;

            [JsonProperty("Explosion Hearing Range")]
            public float ExplosionHearingRange = 380f;

            [JsonProperty("Melee Or Tool Hearing Range")]
            public float MeleeOrToolHearingRange = 45f;

            [JsonProperty("Sprint Hearing Range")]
            public float SprintHearingRange = 28f;

            [JsonProperty("Sound Investigation Commitment Seconds")]
            public float SoundInvestigationCommitmentSeconds = 16f;

            [JsonProperty("Sound Investigation Command Cooldown Seconds")]
            public float SoundInvestigationCommandCooldownSeconds = 1.25f;

            [JsonProperty("Require Line Of Sight To Shoot")]
            public bool RequireLineOfSightToShoot = true;

            [JsonProperty("Allow Hearing")]
            public bool AllowHearing = true;

            [JsonProperty("Allow Cover")]
            public bool AllowCover = true;

            [JsonProperty("Allow Flanking")]
            public bool AllowFlanking = true;

            [JsonProperty("Allow Grenades")]
            public bool AllowGrenades = true;

            [JsonProperty("Allow Smoke")]
            public bool AllowSmoke = true;

            [JsonProperty("Allow Barricades")]
            public bool AllowBarricades = true;

            [JsonProperty("Allow Jiggle Peeking")]
            public bool AllowJigglePeeking = true;

            [JsonProperty("Allow Jump Peek Approximation")]
            public bool AllowJumpPeekApproximation = false;

            [JsonProperty("Cover Search Radius")]
            public float CoverSearchRadius = 28f;

            [JsonProperty("Cover Point Attempts")]
            public int CoverPointAttempts = 28;

            [JsonProperty("Cover Minimum Distance From Threat")]
            public float CoverMinimumDistanceFromThreat = 16f;

            [JsonProperty("Cover Reposition Cooldown Seconds")]
            public float CoverRepositionCooldownSeconds = 4f;

            [JsonProperty("Cover Arrival Distance")]
            public float CoverArrivalDistance = 1.75f;

            [JsonProperty("Effective Cover Max Exposed Fraction")]
            public float EffectiveCoverMaxExposedFraction = 0.35f;

            [JsonProperty("Peek Offset Distance")]
            public float PeekOffsetDistance = 3f;

            [JsonProperty("Peek Exposure Min Seconds")]
            public float PeekExposureMinSeconds = 0.35f;

            [JsonProperty("Peek Exposure Max Seconds")]
            public float PeekExposureMaxSeconds = 1.15f;

            [JsonProperty("Tuck Min Seconds")]
            public float TuckMinSeconds = 0.45f;

            [JsonProperty("Tuck Max Seconds")]
            public float TuckMaxSeconds = 1.6f;

            [JsonProperty("Stuck Detection Seconds")]
            public float StuckDetectionSeconds = 5f;

            [JsonProperty("Stuck Recovery Cooldown Seconds")]
            public float StuckRecoveryCooldownSeconds = 2.5f;

            [JsonProperty("Stuck Recovery Search Radius")]
            public float StuckRecoverySearchRadius = 18f;

            [JsonProperty("Hard Stuck Failed Paths To Despawn")]
            public int HardStuckFailedPathsToDespawn = 30;

            [JsonProperty("Squad Flank Distance")]
            public float SquadFlankDistance = 24f;

            [JsonProperty("Squad Regroup Distance")]
            public float SquadRegroupDistance = 55f;

            [JsonProperty("Squad Contact Commitment Seconds")]
            public float SquadContactCommitmentSeconds = 35f;

            [JsonProperty("Flank Cooldown Seconds")]
            public float FlankCooldownSeconds = 7f;

            [JsonProperty("Grenade Cooldown Seconds")]
            public float GrenadeCooldownSeconds = 30f;

            [JsonProperty("Team Grenade Cooldown Seconds")]
            public float TeamGrenadeCooldownSeconds = 10f;

            [JsonProperty("Barricade Cooldown Seconds")]
            public float BarricadeCooldownSeconds = 12f;

            [JsonProperty("Barricade Prefab")]
            public string BarricadePrefab = WoodenBarricadeCoverPrefab;

            [JsonProperty("Maximum Active Bot Barricades")]
            public int MaxActiveBotBarricades = 12;

            [JsonProperty("Recycle Oldest Barricade When Cap Reached")]
            public bool RecycleOldestBarricadeWhenCapReached = true;

            [JsonProperty("Barricade Placement Distance")]
            public float BarricadePlacementDistance = 4.5f;

            [JsonProperty("Barricade Hold Seconds")]
            public float BarricadeHoldSeconds = 8f;

            [JsonProperty("Barricade Fight Commitment Seconds")]
            public float BarricadeFightCommitmentSeconds = 10f;

            [JsonProperty("Barricade Followup Memory Seconds")]
            public float BarricadeFollowupMemorySeconds = 6f;

            [JsonProperty("Retreat Wall Cover Distance")]
            public float RetreatWallCoverDistance = 10f;

            [JsonProperty("Damage Wall Reaction Window Seconds")]
            public float DamageWallReactionWindowSeconds = 12f;

            [JsonProperty("Damage Wall Awareness Recheck Seconds")]
            public float DamageWallAwarenessRecheckSeconds = 1.5f;

            [JsonProperty("Damage Wall Chance Casual")]
            public float DamageWallChanceCasual = 0.45f;

            [JsonProperty("Damage Wall Chance Average")]
            public float DamageWallChanceAverage = 1f;

            [JsonProperty("Damage Wall Chance Dangerous")]
            public float DamageWallChanceDangerous = 1f;

            [JsonProperty("Low Health Cover Threshold")]
            public float LowHealthCoverThreshold = 0.6f;

            [JsonProperty("Low Health Cover Notice Chance Casual")]
            public float LowHealthCoverNoticeChanceCasual = 0.65f;

            [JsonProperty("Low Health Cover Notice Chance Average")]
            public float LowHealthCoverNoticeChanceAverage = 0.95f;

            [JsonProperty("Low Health Cover Notice Chance Dangerous")]
            public float LowHealthCoverNoticeChanceDangerous = 1f;

            [JsonProperty("Low Health Cover Recheck Seconds")]
            public float LowHealthCoverRecheckSeconds = 4f;

            [JsonProperty("Low Health Cover Commitment Seconds")]
            public float LowHealthCoverCommitmentSeconds = 12f;

            [JsonProperty("Low Health Cover Heal Per Second")]
            public float LowHealthCoverHealPerSecond = 5f;

            [JsonProperty("Low Health Cover Heal Target Fraction")]
            public float LowHealthCoverHealTargetFraction = 0.85f;

            [JsonProperty("Passive Combat Heal Per Second")]
            public float PassiveCombatHealPerSecond = 1.5f;

            [JsonProperty("Passive Combat Heal Target Fraction")]
            public float PassiveCombatHealTargetFraction = 1f;

            [JsonProperty("Syringe Fire Lock Seconds")]
            public float SyringeFireLockSeconds = 2.2f;

            [JsonProperty("Syringe Cooldown Seconds")]
            public float SyringeCooldownSeconds = 8f;

            [JsonProperty("Auto Reload Bot Weapons")]
            public bool AutoReloadBotWeapons = true;

            [JsonProperty("Do Not Enter Bases")]
            public bool DoNotEnterBases = true;

            [JsonProperty("Base Avoidance Radius")]
            public float BaseAvoidanceRadius = 8f;

            [JsonProperty("Base Hold Seconds")]
            public float BaseHoldSeconds = 12f;
        }

        private class DecisionAdvisorConfig
        {
            public bool Enabled = true;
            public string Provider = "none";
            public string Mode = "fallback_only";

            [JsonProperty("Shadow Mode")]
            public bool ShadowMode = true;

            [JsonProperty("Treat Unconfigured Advisor As Failure")]
            public bool TreatUnconfiguredAdvisorAsFailure = true;

            [JsonProperty("Fallback On Any Failure")]
            public bool FallbackOnAnyFailure = true;

            [JsonProperty("Endpoint Url")]
            public string EndpointUrl = "";

            [JsonProperty("Api Key")]
            public string ApiKey = "";

            public string Model = "";

            [JsonProperty("Timeout Milliseconds")]
            public int TimeoutMilliseconds = 750;

            [JsonProperty("Decision Ttl Milliseconds")]
            public int DecisionTtlMilliseconds = 3000;

            [JsonProperty("Minimum Confidence")]
            public float MinimumConfidence = 0.55f;

            [JsonProperty("Max Concurrent Requests")]
            public int MaxConcurrentRequests = 2;

            [JsonProperty("Min Seconds Between Requests Per Bot")]
            public float MinSecondsBetweenRequestsPerBot = 8f;

            [JsonProperty("Ask When Bot Is Stuck")]
            public bool AskWhenBotIsStuck = true;

            [JsonProperty("Ask When Action Scores Are Close")]
            public bool AskWhenActionScoresAreClose = true;

            [JsonProperty("Ask When Push Retreat Or Flank Is High Impact")]
            public bool AskWhenPushRetreatOrFlankIsHighImpact = true;

            [JsonProperty("Ask When Same Action Failed Repeatedly")]
            public bool AskWhenSameActionFailedRepeatedly = true;

            [JsonProperty("Ask When Squad State Changes Sharply")]
            public bool AskWhenSquadStateChangesSharply = true;

            [JsonProperty("Log Decision Traces")]
            public bool LogDecisionTraces = true;

            [JsonProperty("Max Recent Events In Request")]
            public int MaxRecentEventsInRequest = 24;

            [JsonProperty("Max Candidate Actions")]
            public int MaxCandidateActions = 8;
        }

        private class PersistenceConfig
        {
            [JsonProperty("Kill Bots On Plugin Unload")]
            public bool KillBotsOnPluginUnload = false;

            [JsonProperty("Kill Bots On Disable")]
            public bool KillBotsOnDisable = false;

            [JsonProperty("Leave Corpses")]
            public bool LeaveCorpses = true;

            [JsonProperty("Leave Bot Placed Entities")]
            public bool LeaveBotPlacedEntities = true;

            [JsonProperty("Emergency Kill Command Enabled")]
            public bool EmergencyKillCommandEnabled = true;
        }

        private class DebugConfig
        {
            [JsonProperty("Debug Spawn Details")]
            public bool DebugSpawnDetails = false;

            [JsonProperty("Debug Tactical Decisions")]
            public bool DebugTacticalDecisions = false;

            [JsonProperty("Debug Perception")]
            public bool DebugPerception = false;

            [JsonProperty("Debug Bot Nameplates")]
            public bool DebugBotNameplates = false;

            [JsonProperty("Debug Bot Side Panel")]
            public bool DebugBotSidePanel = false;

            [JsonProperty("Debug UI Includes Anchor Player")]
            public bool DebugUiIncludesAnchorPlayer = true;

            [JsonProperty("Debug Nameplate Refresh Seconds")]
            public float DebugNameplateRefreshSeconds = 1f;

            [JsonProperty("Debug Nameplate Draw Duration Seconds")]
            public float DebugNameplateDrawDurationSeconds = 1.25f;

            [JsonProperty("Debug Nameplate Height")]
            public float DebugNameplateHeight = 3.25f;

            [JsonProperty("Debug Nameplate Font Size")]
            public int DebugNameplateFontSize = 9;

            [JsonProperty("Debug Nameplate Max Distance")]
            public float DebugNameplateMaxDistance = 350f;

            [JsonProperty("Debug Cover Scores")]
            public bool DebugCoverScores = false;

            [JsonProperty("Debug Decision Advisor")]
            public bool DebugDecisionAdvisor = false;
        }

        private class StoredData
        {
            public Dictionary<string, PlayerNpcStats> players = new Dictionary<string, PlayerNpcStats>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, BotStats> bots = new Dictionary<string, BotStats>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, BotClanStats> bot_clans = new Dictionary<string, BotClanStats>(StringComparer.OrdinalIgnoreCase);
        }

        private class PlayerNpcStats
        {
            public string steam_id64 = "";
            public string display_name = "";
            public int npc_kills;
            public int deaths_by_npc;
        }

        private class BotStats
        {
            public string bot_key = "";
            public string display_name = "";
            public string kit_name = "";
            public string skill_tier = "";
            public string clan_key = "";
            public string clan_tag = "";
            public string clan_name = "";
            public int team_id;
            public string squad_role = "";
            public int spawns;
            public int kills;
            public int deaths;
        }

        private class BotClanStats
        {
            public string clan_key = "";
            public string clan_tag = "";
            public string clan_name = "";
            public int bots_spawned;
            public int kills;
            public int deaths;
        }

        private class BotRuntime
        {
            public string BotKey;
            public string DisplayName;
            public string KitName;
            public string SkillTier;
            public SkillDefinition Skill;
            public int TeamId;
            public string SquadRole = "solo";
            public string ClanKey = "";
            public string ClanTag = "";
            public string ClanName = "";

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
            public float LastDamageBarricadeAwarenessCheckAt;
            public float LowHealthCoverAwareUntil;
            public float NextLowHealthAwarenessCheckAt;
            public float LastLowHealthHealAt;
            public float LastPassiveHealAt;
            public float MedicalFireLockedUntil;
            public float NextSyringeHealAt;
            public float HoldOutsideBaseUntil;
            public float LastShotAt;
            public float LastDamageTakenAt;
            public float LastDamageDealtAt;
            public float LastSoundInvestigateCommandAt;
            public float LastSoundDebugAt;
            public float InvalidPositionSince;
            public string LastBarricadeReason = "none";
            public string LastFireBlockReason = "none";
            public string LastSightReason = "none";

            public bool IsShooting;
            public bool IsInBaseRestrictedArea;
            public int ConsecutiveFailedPaths;
        }

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

        private class TacticalMemory
        {
            public BasePlayer Target;
            public ulong TargetUserId;

            public bool HasLineOfSight;
            public float LastLineOfSightAt;
            public float TargetExposureFraction;
            public int TargetVisibleProbePoints;
            public int TargetTotalProbePoints;

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

        private class CombatProfile
        {
            public string WeaponShortname = "";
            public string WeaponClass = "default";
            public float PreferredDistance = 32f;
            public float IdealRange = 80f;
            public float HarassRange = 115f;
            public float MaxRange = 150f;
            public float PushDistance = 18f;
            public float RetreatDistance = 10f;
            public float NextPoorRangeShotAt;
            public float PoorRangeFireUntil;
        }

        private class MovementPlan
        {
            public Vector3 LastPosition;
            public float LastProgressAt;
            public Vector3 LastCommandDestination;
            public float LastCommandAt;
            public bool IsStuck;
            public float StuckSince;
            public float LastStuckNotedAt;
            public TacticalActionId LastActionId = TacticalActionId.None;
            public int SameActionFailures;
        }

        private class VisionResult
        {
            public bool CanSee;
            public float ExposedFraction;
            public int VisibleProbePoints;
            public int TotalProbePoints;
            public int SolidBlockedProbePoints;
            public int FoliageBlockedProbePoints;
            public int FoliageBlockerHits;
            public string BlockReason = "none";
            public Vector3 BestVisiblePoint;
        }

        private class CoverPlan
        {
            public Vector3 CoverPoint;
            public Vector3 TuckPoint;
            public Vector3 PeekLeftPoint;
            public Vector3 PeekRightPoint;
            public float Score;
        }

        private class DecisionContext
        {
            public float LastAdvisorRequestAt;
            public string LastAdvisorStatus = "";
            public TacticalActionId LastActionId = TacticalActionId.None;
            public float LastDecisionAt;
            public string LastFallbackReason = "";
        }

        private class EnemyMemory
        {
            public ulong UserId;
            public Vector3 LastKnownPosition;
            public float LastKnownAt;
            public float Confidence;
            public string Source = "";
        }

        private class SquadBlackboard
        {
            public int TeamId;
            public string ClanKey = "";
            public string ClanTag = "";
            public string ClanName = "";
            public Dictionary<ulong, EnemyMemory> KnownEnemies = new Dictionary<ulong, EnemyMemory>();
            public Dictionary<string, Vector3> CoverClaims = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
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

        private class DecisionAdvisorResult
        {
            public bool Success;
            public string Status = "";
            public string ActionId = "";
            public float Confidence;
            public int TtlMilliseconds;
            public string Rationale = "";
            public string FallbackActionId = "";

            public static DecisionAdvisorResult Failure(string status)
            {
                return new DecisionAdvisorResult
                {
                    Success = false,
                    Status = status ?? "advisor_failure"
                };
            }
        }

        private class DecisionRequest
        {
            public string RequestId;
            public string BotId;
            public int TeamId;
            public string ClanKey;
            public string ClanTag;
            public string State;
            public string SkillTier;
            public float HealthFraction;
            public string WeaponShortname;
            public float AmmoFraction;
            public bool HasLineOfSight;
            public float TargetExposureFraction;
            public float TargetConfidence;
            public float DistanceToTarget;
            public float SecondsSinceLastSeen;
            public float SecondsSinceLastHeard;
            public int NearbyAllies;
            public int NearbyKnownEnemies;
            public bool IsStuck;
            public bool TargetIsInsideBaseRestrictedArea;
            public List<DecisionEvent> RecentEvents = new List<DecisionEvent>();
            public List<TacticalActionCandidate> CandidateActions = new List<TacticalActionCandidate>();
        }

        private class DecisionEvent
        {
            public float Time;
            public string Type = "";
            public string Detail = "";
            public Vector3 Position;
        }

        private class TacticalActionCandidate
        {
            public string Id;
            [JsonIgnore]
            public TacticalActionId ActionId;
            public float HeuristicScore;
            public string Risk;
            public string ReasonFromCode;
            public Vector3 Destination;
            public ulong TargetUserId;
            public float ExpiresAt;
            public List<string> Preconditions = new List<string>();
            public List<string> RiskFlags = new List<string>();
        }

        private class TacticalDecision
        {
            public TacticalActionCandidate Selected;
            public bool AdvisorRequested;
            public string AdvisorStatus = "";
            public string FallbackReason = "";
        }

        private class DecisionTrace
        {
            public string request_id;
            public string bot_id;
            public int team_id;
            public string clan_key;
            public string clan_tag;
            public string state;
            public bool advisor_requested;
            public string advisor_status;
            public string fallback_reason;
            public string final_action;
            public float final_score;
            public List<TacticalActionCandidate> candidates;
            public float created_at;
        }

        private class KitEligibility
        {
            public string Name;
            public string RequiredPermission;
            public bool HighTier;
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>() ?? new Configuration();
            }
            catch (Exception ex)
            {
                PrintWarning($"Configuration was invalid; writing defaults. {ex.Message}");
                config = new Configuration();
            }

            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RefreshEligibleKits();
            decisionAdvisor = new NullDecisionAdvisor();
            CreateScoreboards();
            UpdateScoreboards();

            if (!config.Enabled)
            {
                Puts("Raidlands roam bots are disabled. Config and data are ready; no bots will spawn until raidbots.enable is run.");
                return;
            }

            StartRuntime();
        }

        private void Unload()
        {
            StopRuntime();
            DestroyDebugBotPanels();
            if (config?.Persistence?.KillBotsOnPluginUnload == true)
            {
                KillAllBots(!config.Persistence.LeaveCorpses);
            }
            SaveData();
            FlushDecisionTraces();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin?.Name == "Scoreboards")
            {
                timer.Once(2f, () =>
                {
                    CreateScoreboards();
                    UpdateScoreboards();
                });
            }

            if (plugin?.Name == "Kits")
            {
                timer.Once(2f, RefreshEligibleKits);
            }
        }

        private void StartRuntime()
        {
            StopRuntime();
            spawnRetryBlockedUntil = 0f;
            maintainTimer = timer.Every(Math.Max(5f, config.MaintainIntervalSeconds), MaintainPopulation);
            perceptionTimer = timer.Every(Math.Max(0.1f, config.AI.PerceptionTickSeconds), PerceptionTick);
            brainTimer = timer.Every(Math.Max(0.15f, config.AI.DecisionTickSeconds), TacticalBrainTick);
            squadTimer = timer.Every(Math.Max(0.25f, config.AI.SquadTickSeconds), SquadTick);
            StartNameplateTimerIfEnabled();
            scoreboardTimer = timer.Every(Math.Max(15f, config.ScoreboardIntervalSeconds), UpdateScoreboards);
            MaintainPopulation();
            Puts($"Raidlands roam bots enabled. Target population: {TargetPopulation()}.");
        }

        private void StopRuntime()
        {
            maintainTimer?.Destroy();
            maintainTimer = null;
            perceptionTimer?.Destroy();
            perceptionTimer = null;
            brainTimer?.Destroy();
            brainTimer = null;
            squadTimer?.Destroy();
            squadTimer = null;
            nameplateTimer?.Destroy();
            nameplateTimer = null;
            DestroyDebugBotPanels();
            scoreboardTimer?.Destroy();
            scoreboardTimer = null;
            decisionTraceSaveTimer?.Destroy();
            decisionTraceSaveTimer = null;
        }

        private void NormalizeConfig()
        {
            var defaults = new Configuration();
            config.MinAllowedPopulation = Math.Max(0, config.MinAllowedPopulation);
            config.MaxAllowedPopulation = Math.Max(config.MinAllowedPopulation, config.MaxAllowedPopulation);
            config.TargetPopulation = Clamp(config.TargetPopulation, config.MinAllowedPopulation, config.MaxAllowedPopulation);
            config.HighTierKitWeight = Clamp(config.HighTierKitWeight, 0, 100);

            if (config.TeamSizeWeights == null || config.TeamSizeWeights.Count == 0)
            {
                config.TeamSizeWeights = defaults.TeamSizeWeights;
            }

            if (config.BotClans == null || config.BotClans.Count == 0)
            {
                config.BotClans = defaults.BotClans;
            }

            var normalizedClans = new List<BotClanDefinition>();
            var clanKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var clan in config.BotClans)
            {
                if (clan == null)
                {
                    continue;
                }

                var name = (clan.Name ?? "").Trim();
                var tag = (clan.Tag ?? "").Trim();
                var key = (clan.Key ?? "").Trim().ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = string.IsNullOrWhiteSpace(tag) ? $"Clan {normalizedClans.Count + 1}" : tag;
                }

                if (string.IsNullOrWhiteSpace(tag))
                {
                    tag = new string(name.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant();
                }

                if (string.IsNullOrWhiteSpace(tag))
                {
                    tag = $"C{normalizedClans.Count + 1}";
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    key = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    key = $"clan-{normalizedClans.Count + 1}";
                }

                if (!clanKeys.Add(key))
                {
                    continue;
                }

                normalizedClans.Add(new BotClanDefinition
                {
                    Key = key,
                    Tag = tag.Length <= 6 ? tag : tag.Substring(0, 6),
                    Name = name
                });
            }

            config.BotClans = normalizedClans.Count == 0 ? defaults.BotClans : normalizedClans;

            if (config.SkillWeights == null || config.SkillWeights.Count == 0)
            {
                config.SkillWeights = defaults.SkillWeights;
            }

            if (config.SkillDefinitions == null)
            {
                config.SkillDefinitions = defaults.SkillDefinitions;
            }
            else
            {
                foreach (var entry in defaults.SkillDefinitions)
                {
                    if (!config.SkillDefinitions.ContainsKey(entry.Key))
                    {
                        config.SkillDefinitions[entry.Key] = entry.Value;
                    }
                }
            }

            if (config.Kits == null)
            {
                config.Kits = defaults.Kits;
            }

            if (config.Kits.EligibleKitNames == null || config.Kits.EligibleKitNames.Count == 0)
            {
                config.Kits.EligibleKitNames = defaults.Kits.EligibleKitNames;
            }

            if (config.Kits.RareHighTierKitNames == null)
            {
                config.Kits.RareHighTierKitNames = defaults.Kits.RareHighTierKitNames;
            }

            if (config.Kits.WeaponShortnames == null || config.Kits.WeaponShortnames.Count == 0)
            {
                config.Kits.WeaponShortnames = defaults.Kits.WeaponShortnames;
            }

            if (string.IsNullOrWhiteSpace(config.Kits.DefaultGroup))
            {
                config.Kits.DefaultGroup = defaults.Kits.DefaultGroup;
            }

            if (config.Spawn == null)
            {
                config.Spawn = defaults.Spawn;
            }

            config.Spawn.SpawnMode = NormalizeSpawnMode(config.Spawn.SpawnMode);
            config.Spawn.MaxPositionAttempts = Math.Max(10, config.Spawn.MaxPositionAttempts);
            config.Spawn.NavmeshSampleDistance = Math.Max(2f, config.Spawn.NavmeshSampleDistance);
            config.Spawn.MinimumAboveWater = Math.Max(0f, config.Spawn.MinimumAboveWater);
            config.Spawn.MinimumLandHeight = Math.Max(-100f, config.Spawn.MinimumLandHeight);
            config.Spawn.MaximumBelowTerrainTolerance = Math.Max(0f, config.Spawn.MaximumBelowTerrainTolerance);
            config.Spawn.RuntimeInvalidPositionDespawnSeconds = Math.Max(0.5f, config.Spawn.RuntimeInvalidPositionDespawnSeconds);
            config.Spawn.GroupSpawnRadius = Math.Max(1f, config.Spawn.GroupSpawnRadius);
            config.Spawn.NearPlayerMinDistance = Math.Max(25f, config.Spawn.NearPlayerMinDistance);
            config.Spawn.NearPlayerMaxDistance = Math.Max(config.Spawn.NearPlayerMinDistance + 10f, config.Spawn.NearPlayerMaxDistance);
            config.Spawn.NearPlayerAttempts = Math.Max(8, config.Spawn.NearPlayerAttempts);
            config.Spawn.NearPlayerAnchorNameOrSteamId = (config.Spawn.NearPlayerAnchorNameOrSteamId ?? "").Trim();
            config.Spawn.SafeZoneSpawnBufferDistance = Math.Max(0f, config.Spawn.SafeZoneSpawnBufferDistance);

            if (config.PrefabCandidates == null || config.PrefabCandidates.Count == 0)
            {
                config.PrefabCandidates = defaults.PrefabCandidates;
            }

            config.PrefabCandidates = config.PrefabCandidates
                .Where(prefab => !string.IsNullOrWhiteSpace(prefab))
                .Select(prefab => prefab.Trim())
                .Where(IsLegacyScientistBodyPrefab)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (config.PrefabCandidates.Count == 0)
            {
                config.PrefabCandidates = defaults.PrefabCandidates;
            }

            config.SpawnFailureRetrySeconds = Math.Max(15f, config.SpawnFailureRetrySeconds);

            if (config.BotProfiles == null || config.BotProfiles.Count == 0)
            {
                config.BotProfiles = defaults.BotProfiles;
            }

            config.MaintainIntervalSeconds = Math.Max(5f, config.MaintainIntervalSeconds);
            config.ScoreboardIntervalSeconds = Math.Max(15f, config.ScoreboardIntervalSeconds);
            config.RespawnDelaySeconds = Math.Max(5f, config.RespawnDelaySeconds);

            if (config.AI == null)
            {
                config.AI = defaults.AI;
            }

            config.AI.PerceptionTickSeconds = Mathf.Clamp(config.AI.PerceptionTickSeconds, 0.1f, 2f);
            config.AI.DecisionTickSeconds = Mathf.Clamp(config.AI.DecisionTickSeconds, 0.15f, 3f);
            config.AI.SquadTickSeconds = Mathf.Clamp(config.AI.SquadTickSeconds, 0.25f, 5f);
            config.AI.VisionRange = Math.Max(20f, config.AI.VisionRange);
            if (config.AI.VisionFovDegrees <= 135.1f)
            {
                config.AI.VisionFovDegrees = defaults.AI.VisionFovDegrees;
            }

            if (config.AI.CloseAwarenessRadius <= 12.1f)
            {
                config.AI.CloseAwarenessRadius = defaults.AI.CloseAwarenessRadius;
            }

            if (config.AI.MinimumExposedTargetFraction >= 0.44f)
            {
                config.AI.MinimumExposedTargetFraction = defaults.AI.MinimumExposedTargetFraction;
            }

            if (config.AI.MinimumExposedTargetFractionToShoot >= 0.49f)
            {
                config.AI.MinimumExposedTargetFractionToShoot = defaults.AI.MinimumExposedTargetFractionToShoot;
            }

            if (config.AI.FoliageVisionCheckRadius <= 0.66f || config.AI.FoliageVisionCheckRadius >= 1.5f)
            {
                config.AI.FoliageVisionCheckRadius = defaults.AI.FoliageVisionCheckRadius;
            }

            if (config.AI.MaximumClearVisionThroughFoliage <= 8.1f || config.AI.MaximumClearVisionThroughFoliage >= 23.9f)
            {
                config.AI.MaximumClearVisionThroughFoliage = defaults.AI.MaximumClearVisionThroughFoliage;
            }

            if (config.AI.FoliageHitsToBlockVision <= 0 || config.AI.FoliageHitsToBlockVision >= 2)
            {
                config.AI.FoliageHitsToBlockVision = defaults.AI.FoliageHitsToBlockVision;
            }

            config.AI.VisionFovDegrees = Mathf.Clamp(config.AI.VisionFovDegrees, 30f, 360f);
            config.AI.CloseAwarenessRadius = Math.Max(0f, config.AI.CloseAwarenessRadius);
            config.AI.MinimumExposedTargetFraction = Mathf.Clamp(config.AI.MinimumExposedTargetFraction, 0.1f, 1f);
            config.AI.MinimumExposedTargetFractionToShoot = Mathf.Clamp(config.AI.MinimumExposedTargetFractionToShoot, config.AI.MinimumExposedTargetFraction, 1f);
            config.AI.FoliageVisionCheckRadius = Mathf.Clamp(config.AI.FoliageVisionCheckRadius, 0.1f, 3f);
            config.AI.MaximumClearVisionThroughFoliage = Mathf.Clamp(config.AI.MaximumClearVisionThroughFoliage, 1f, config.AI.VisionRange);
            config.AI.FoliageHitsToBlockVision = Math.Max(1, config.AI.FoliageHitsToBlockVision);
            config.AI.FoliageTerrainSampleStep = Mathf.Clamp(config.AI.FoliageTerrainSampleStep <= 0f ? defaults.AI.FoliageTerrainSampleStep : config.AI.FoliageTerrainSampleStep, 3f, 18f);
            config.AI.FoliageTerrainSamplesToBlockVision = Clamp(config.AI.FoliageTerrainSamplesToBlockVision <= 0 ? defaults.AI.FoliageTerrainSamplesToBlockVision : config.AI.FoliageTerrainSamplesToBlockVision, 1, 12);
            if (config.AI.FoliageOccluderLayerNames == null || config.AI.FoliageOccluderLayerNames.Count == 0)
            {
                config.AI.FoliageOccluderLayerNames = defaults.AI.FoliageOccluderLayerNames;
            }

            foreach (var layerName in defaults.AI.FoliageOccluderLayerNames)
            {
                if (!config.AI.FoliageOccluderLayerNames.Contains(layerName, StringComparer.OrdinalIgnoreCase))
                {
                    config.AI.FoliageOccluderLayerNames.Add(layerName);
                }
            }

            config.AI.FoliageOccluderLayerNames = config.AI.FoliageOccluderLayerNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            config.AI.TargetMemorySeconds = Math.Max(1f, config.AI.TargetMemorySeconds);
            config.AI.SearchLastSeenSeconds = Math.Max(config.AI.TargetMemorySeconds, config.AI.SearchLastSeenSeconds);
            config.AI.UnsuppressedGunshotHearingRange = Mathf.Clamp(config.AI.UnsuppressedGunshotHearingRange, 0f, 500f);
            config.AI.SuppressedGunshotHearingRange = Mathf.Clamp(config.AI.SuppressedGunshotHearingRange, 0f, config.AI.UnsuppressedGunshotHearingRange);
            config.AI.ExplosionHearingRange = Mathf.Clamp(config.AI.ExplosionHearingRange, 0f, 800f);
            config.AI.MeleeOrToolHearingRange = Mathf.Clamp(config.AI.MeleeOrToolHearingRange, 0f, 120f);
            config.AI.SprintHearingRange = Mathf.Clamp(config.AI.SprintHearingRange, 0f, 80f);
            config.AI.SoundInvestigationCommitmentSeconds = Mathf.Clamp(config.AI.SoundInvestigationCommitmentSeconds, 2f, config.AI.SearchLastSeenSeconds);
            config.AI.SoundInvestigationCommandCooldownSeconds = Mathf.Clamp(config.AI.SoundInvestigationCommandCooldownSeconds, 0.1f, 5f);
            config.AI.CoverSearchRadius = Math.Max(4f, config.AI.CoverSearchRadius);
            config.AI.CoverPointAttempts = Clamp(config.AI.CoverPointAttempts, 8, 80);
            config.AI.CoverMinimumDistanceFromThreat = Math.Max(2f, config.AI.CoverMinimumDistanceFromThreat);
            config.AI.CoverArrivalDistance = Mathf.Clamp(config.AI.CoverArrivalDistance, 0.75f, 4f);
            config.AI.EffectiveCoverMaxExposedFraction = Mathf.Clamp(config.AI.EffectiveCoverMaxExposedFraction, 0f, 0.75f);
            config.AI.SquadFlankDistance = Mathf.Clamp(config.AI.SquadFlankDistance, 8f, 80f);
            config.AI.SquadRegroupDistance = Mathf.Clamp(config.AI.SquadRegroupDistance, 20f, 140f);
            config.AI.SquadContactCommitmentSeconds = Mathf.Clamp(config.AI.SquadContactCommitmentSeconds, config.AI.TargetMemorySeconds, 90f);
            config.AI.FlankCooldownSeconds = Math.Max(1f, config.AI.FlankCooldownSeconds);
            config.AI.GrenadeCooldownSeconds = Math.Max(1f, config.AI.GrenadeCooldownSeconds);
            config.AI.TeamGrenadeCooldownSeconds = Math.Max(1f, config.AI.TeamGrenadeCooldownSeconds);
            config.AI.BarricadeCooldownSeconds = Mathf.Clamp(Math.Min(config.AI.BarricadeCooldownSeconds, defaults.AI.BarricadeCooldownSeconds), 5f, 45f);
            config.AI.BarricadePrefab = WoodenBarricadeCoverPrefab;
            if (config.AI.MaxActiveBotBarricades <= 6)
            {
                config.AI.MaxActiveBotBarricades = defaults.AI.MaxActiveBotBarricades;
            }

            config.AI.MaxActiveBotBarricades = Clamp(config.AI.MaxActiveBotBarricades, 0, 25);
            config.AI.BarricadePlacementDistance = Mathf.Clamp(config.AI.BarricadePlacementDistance, 2.5f, 9f);
            config.AI.BarricadeHoldSeconds = Math.Max(2f, config.AI.BarricadeHoldSeconds);
            config.AI.BarricadeFightCommitmentSeconds = Mathf.Clamp(config.AI.BarricadeFightCommitmentSeconds, 2f, 30f);
            config.AI.RetreatWallCoverDistance = Mathf.Clamp(config.AI.RetreatWallCoverDistance, 2f, 40f);
            config.AI.DamageWallReactionWindowSeconds = Mathf.Clamp(config.AI.DamageWallReactionWindowSeconds, 2f, 30f);
            config.AI.BarricadeFollowupMemorySeconds = Mathf.Clamp(config.AI.BarricadeFollowupMemorySeconds <= 0f ? defaults.AI.BarricadeFollowupMemorySeconds : config.AI.BarricadeFollowupMemorySeconds, 1f, config.AI.DamageWallReactionWindowSeconds);
            config.AI.DamageWallAwarenessRecheckSeconds = Mathf.Clamp(config.AI.DamageWallAwarenessRecheckSeconds, 0.25f, 10f);
            config.AI.DamageWallChanceCasual = Mathf.Clamp01(config.AI.DamageWallChanceCasual);
            config.AI.DamageWallChanceAverage = Mathf.Clamp01(config.AI.DamageWallChanceAverage);
            config.AI.DamageWallChanceDangerous = Mathf.Clamp01(config.AI.DamageWallChanceDangerous);
            config.AI.LowHealthCoverThreshold = Mathf.Clamp(config.AI.LowHealthCoverThreshold, 0.1f, 0.95f);
            config.AI.LowHealthCoverNoticeChanceCasual = Mathf.Clamp01(config.AI.LowHealthCoverNoticeChanceCasual);
            config.AI.LowHealthCoverNoticeChanceAverage = Mathf.Clamp01(config.AI.LowHealthCoverNoticeChanceAverage);
            config.AI.LowHealthCoverNoticeChanceDangerous = Mathf.Clamp01(config.AI.LowHealthCoverNoticeChanceDangerous);
            config.AI.LowHealthCoverRecheckSeconds = Mathf.Clamp(config.AI.LowHealthCoverRecheckSeconds, 0.5f, 20f);
            config.AI.LowHealthCoverCommitmentSeconds = Mathf.Clamp(config.AI.LowHealthCoverCommitmentSeconds, 2f, 45f);
            config.AI.LowHealthCoverHealPerSecond = Mathf.Clamp(config.AI.LowHealthCoverHealPerSecond, 0f, 30f);
            config.AI.LowHealthCoverHealTargetFraction = Mathf.Clamp(config.AI.LowHealthCoverHealTargetFraction, Math.Max(config.AI.LowHealthCoverThreshold, 0.85f), 1f);
            config.AI.PassiveCombatHealPerSecond = Mathf.Clamp(config.AI.PassiveCombatHealPerSecond, 0f, 20f);
            config.AI.PassiveCombatHealTargetFraction = Mathf.Clamp(config.AI.PassiveCombatHealTargetFraction, config.AI.LowHealthCoverThreshold, 1f);
            config.AI.SyringeFireLockSeconds = Mathf.Clamp(config.AI.SyringeFireLockSeconds, 0.5f, 6f);
            config.AI.SyringeCooldownSeconds = Mathf.Clamp(config.AI.SyringeCooldownSeconds, 1f, 30f);
            config.AI.PeekOffsetDistance = Mathf.Clamp(config.AI.PeekOffsetDistance, 1f, 8f);
            config.AI.PeekExposureMinSeconds = Math.Max(0.1f, config.AI.PeekExposureMinSeconds);
            config.AI.PeekExposureMaxSeconds = Math.Max(config.AI.PeekExposureMinSeconds, config.AI.PeekExposureMaxSeconds);
            config.AI.TuckMinSeconds = Math.Max(0.1f, config.AI.TuckMinSeconds);
            config.AI.TuckMaxSeconds = Math.Max(config.AI.TuckMinSeconds, config.AI.TuckMaxSeconds);
            config.AI.StuckDetectionSeconds = Math.Max(1f, config.AI.StuckDetectionSeconds);
            config.AI.StuckRecoveryCooldownSeconds = Math.Max(0.5f, config.AI.StuckRecoveryCooldownSeconds);
            config.AI.StuckRecoverySearchRadius = Math.Max(6f, config.AI.StuckRecoverySearchRadius);
            config.AI.HardStuckFailedPathsToDespawn = Clamp(config.AI.HardStuckFailedPathsToDespawn, 0, 200);
            config.AI.BaseAvoidanceRadius = Math.Max(1f, config.AI.BaseAvoidanceRadius);
            config.AI.BaseHoldSeconds = Math.Max(2f, config.AI.BaseHoldSeconds);

            if (config.DecisionAdvisor == null)
            {
                config.DecisionAdvisor = defaults.DecisionAdvisor;
            }

            config.DecisionAdvisor.Provider = string.IsNullOrWhiteSpace(config.DecisionAdvisor.Provider) ? "none" : config.DecisionAdvisor.Provider.Trim().ToLowerInvariant();
            config.DecisionAdvisor.Mode = string.IsNullOrWhiteSpace(config.DecisionAdvisor.Mode) ? "fallback_only" : config.DecisionAdvisor.Mode.Trim().ToLowerInvariant();
            config.DecisionAdvisor.TimeoutMilliseconds = Clamp(config.DecisionAdvisor.TimeoutMilliseconds, 100, 5000);
            config.DecisionAdvisor.DecisionTtlMilliseconds = Clamp(config.DecisionAdvisor.DecisionTtlMilliseconds, 100, 10000);
            config.DecisionAdvisor.MinimumConfidence = Mathf.Clamp01(config.DecisionAdvisor.MinimumConfidence);
            config.DecisionAdvisor.MaxConcurrentRequests = Math.Max(0, config.DecisionAdvisor.MaxConcurrentRequests);
            config.DecisionAdvisor.MinSecondsBetweenRequestsPerBot = Math.Max(0f, config.DecisionAdvisor.MinSecondsBetweenRequestsPerBot);
            config.DecisionAdvisor.MaxRecentEventsInRequest = Math.Max(0, config.DecisionAdvisor.MaxRecentEventsInRequest);
            config.DecisionAdvisor.MaxCandidateActions = Clamp(config.DecisionAdvisor.MaxCandidateActions, 1, 16);

            if (config.Persistence == null)
            {
                config.Persistence = defaults.Persistence;
            }

            if (config.Debug == null)
            {
                config.Debug = defaults.Debug;
            }

            config.Debug.DebugNameplateRefreshSeconds = Mathf.Clamp(config.Debug.DebugNameplateRefreshSeconds, 0.25f, 5f);
            config.Debug.DebugNameplateDrawDurationSeconds = Mathf.Clamp(config.Debug.DebugNameplateDrawDurationSeconds, config.Debug.DebugNameplateRefreshSeconds, 10f);
            config.Debug.DebugNameplateHeight = Mathf.Clamp(config.Debug.DebugNameplateHeight, 2.5f, 6f);
            config.Debug.DebugNameplateFontSize = Clamp(config.Debug.DebugNameplateFontSize, 6, 14);
            config.Debug.DebugNameplateMaxDistance = Mathf.Clamp(config.Debug.DebugNameplateMaxDistance, 25f, 1000f);
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(StatsDataFile) ?? new StoredData();
            }
            catch
            {
                data = new StoredData();
            }

            if (data.players == null)
            {
                data.players = new Dictionary<string, PlayerNpcStats>(StringComparer.OrdinalIgnoreCase);
            }

            if (data.bots == null)
            {
                data.bots = new Dictionary<string, BotStats>(StringComparer.OrdinalIgnoreCase);
            }

            if (data.bot_clans == null)
            {
                data.bot_clans = new Dictionary<string, BotClanStats>(StringComparer.OrdinalIgnoreCase);
            }

            SaveData();
        }

        private void SaveData()
        {
            saveTimer?.Destroy();
            saveTimer = null;
            Interface.Oxide.DataFileSystem.WriteObject(StatsDataFile, data);
        }

        private void QueueSaveData()
        {
            saveTimer?.Destroy();
            saveTimer = timer.Once(5f, SaveData);
        }

        private void MaintainPopulation()
        {
            CleanupInactiveBots();

            if (!config.Enabled)
            {
                return;
            }

            RefreshEligibleKits();

            var target = TargetPopulation();

            if (TrimExcessBots(target, "target cap") > 0)
            {
                return;
            }

            if (NormalizeSpawnMode(config.Spawn.SpawnMode) == SpawnModeNearPlayers && !config.Spawn.UseRandomLandFallback && SpawnAnchorPlayers().Count == 0)
            {
                return;
            }

            var missing = target - activeBots.Count;

            if (missing <= 0)
            {
                return;
            }

            if (Time.realtimeSinceStartup < spawnRetryBlockedUntil)
            {
                return;
            }

            var spawned = SpawnBots(missing, false);

            if (spawned <= 0)
            {
                spawnRetryBlockedUntil = Time.realtimeSinceStartup + config.SpawnFailureRetrySeconds;
                PrintWarning($"Roam bot spawning is paused for {config.SpawnFailureRetrySeconds:0} seconds because no configured prefab could be placed on navmesh.");
            }
        }

        private void CleanupInactiveBots()
        {
            foreach (var entry in activeBots.ToList())
            {
                if (!IsLiveBot(entry.Key))
                {
                    activeBots.Remove(entry.Key);
                    despawningBots.Remove(entry.Key);
                }
            }
        }

        private int TrimExcessBots(int target, string reason)
        {
            target = Math.Max(0, target);
            var excess = activeBots.Count - target;

            if (excess <= 0)
            {
                return 0;
            }

            var bots = activeBots.Keys
                .Where(IsLiveBot)
                .Take(excess)
                .ToList();

            foreach (var bot in bots)
            {
                DespawnBot(bot, "");
            }

            if (bots.Count > 0)
            {
                Puts($"Trimmed {bots.Count} roam bot{(bots.Count == 1 ? "" : "s")} above target population {target} after {reason}.");
            }

            return bots.Count;
        }

        private int SpawnBots(int requested, bool manual)
        {
            if (requested <= 0)
            {
                return 0;
            }

            RefreshEligibleKits();

            if (eligibleKits.Count == 0)
            {
                PrintWarning("No eligible default-access weapon kits found for roam bots.");
                return 0;
            }

            var spawned = 0;
            var remaining = requested;
            var positionAttemptsPerTeam = Math.Max(1, Math.Min(10, config.Spawn.MaxPositionAttempts));

            while (remaining > 0)
            {
                var teamSize = Math.Min(remaining, WeightedTeamSize());
                var teamId = ++teamSequence;
                var teamSpawned = false;

                for (var spawnAttempt = 0; spawnAttempt < positionAttemptsPerTeam && remaining > 0; spawnAttempt++)
                {
                    if (!TryFindSpawnPosition(out var leaderPosition, out var preferredPrefab))
                    {
                        if (spawnAttempt == 0)
                        {
                            PrintWarning("Could not find a valid land/navmesh spawn position for roam bots.");
                        }

                        break;
                    }

                    var teamSpawnedThisAttempt = 0;

                    for (var index = 0; index < teamSize; index++)
                    {
                        var position = leaderPosition;

                        if (index > 0)
                        {
                            TryFindNearbyPosition(leaderPosition, out position);
                        }

                        if (TrySpawnBot(position, teamId, preferredPrefab) != null)
                        {
                            spawned++;
                            remaining--;
                            teamSpawned = true;
                            teamSpawnedThisAttempt++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (teamSpawnedThisAttempt > 0)
                    {
                        break;
                    }
                }

                if (!teamSpawned)
                {
                    remaining = 0;
                    break;
                }
            }

            if (manual && spawned > 0)
            {
                Puts($"Manually spawned {spawned} roam bot{(spawned == 1 ? "" : "s")}.");
            }

            return spawned;
        }

        private BaseCombatEntity TrySpawnBot(Vector3 position, int teamId, string preferredPrefab)
        {
            foreach (var prefab in ActivePrefabCandidates(preferredPrefab))
            {
                if (config.Debug.DebugSpawnDetails)
                {
                    Puts($"Trying legacy body prefab {prefab} at {FormatVector(position)} ({PositionDiagnostics(position)}), brain={TacticalBrainName}.");
                }

                var entity = GameManager.server.CreateEntity(prefab, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), true);

                if (entity == null)
                {
                    if (config.Debug.DebugSpawnDetails)
                    {
                        Puts($"Prefab {prefab} could not be created at {FormatVector(position)}.");
                    }

                    continue;
                }

                var bot = entity as BaseCombatEntity;

                if (bot == null)
                {
                    if (config.Debug.DebugSpawnDetails)
                    {
                        Puts($"Prefab {prefab} created {entity.GetType().Name}, not BaseCombatEntity; rejecting spawn attempt.");
                    }

                    SafeKillSpawnAttempt(entity);
                    continue;
                }

                entity.Spawn();

                if (!TryPlaceBotOnOwnNavmesh(bot, ref position))
                {
                    PrintWarning($"Prefab {prefab} spawned but its navigator could not be placed on navmesh; trying the next candidate.");
                    SafeKillSpawnAttempt(bot);
                    continue;
                }

                if (IsBlockedLandPosition(position))
                {
                    PrintWarning($"Prefab {prefab} spawned at {FormatVector(position)}, but that position is blocked by terrain, water, or safe-zone rules; trying the next candidate.");
                    SafeKillSpawnAttempt(bot);
                    continue;
                }

                var runtime = ConfigureBot(bot, position, teamId, prefab);
                PrepareNpcBody(bot);
                runtime.CurrentDestination = FindRoamDestination(runtime.HomePosition);
                MoveBotTo(bot, runtime, runtime.CurrentDestination, BaseNavigator.NavigationSpeed.Fast);
                ScheduleBodyPrepare(bot);

                if (config.Debug.DebugSpawnDetails)
                {
                    Puts($"Accepted roam bot {runtime.DisplayName} from prefab {prefab} at {FormatVector(position)} ({PositionDiagnostics(position)}), {BotRuntimeDiagnostics(bot, runtime)}.");
                }

                return bot;
            }

            PrintWarning("None of the configured NPC prefabs could be created.");
            return null;
        }

        private IEnumerable<string> ActivePrefabCandidates(string preferredPrefab = "")
        {
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var preferred = (preferredPrefab ?? "").Trim();

            if (CanUseRoamPrefab(preferred) && ShouldTryPreferredPrefabFirst(preferred) && yielded.Add(preferred))
            {
                yield return preferred;
            }

            foreach (var prefab in config.PrefabCandidates ?? new List<string>())
            {
                if (!CanUseRoamPrefab(prefab))
                {
                    continue;
                }

                if (yielded.Add(prefab.Trim()))
                {
                    yield return prefab.Trim();
                }
            }
        }

        private bool ShouldTryPreferredPrefabFirst(string preferredPrefab)
        {
            if (string.IsNullOrWhiteSpace(preferredPrefab))
            {
                return false;
            }

            return CanUseRoamPrefab(preferredPrefab);
        }

        private void SafeKillSpawnAttempt(BaseNetworkable entity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return;
            }

            try
            {
                entity.Kill(BaseNetworkable.DestroyMode.None);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not clean up a failed roam bot spawn attempt: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private BotRuntime ConfigureBot(BaseCombatEntity bot, Vector3 position, int teamId, string prefab)
        {
            var skillTier = WeightedKey(config.SkillWeights, "average");
            var skill = SkillFor(skillTier);
            var kit = ShouldApplyKit(bot) ? ChooseKit() : null;
            var displayName = ChooseProfileName();
            var botKey = BotKey(displayName);
            var playerBot = bot as BasePlayer;
            var clan = ClanForTeam(teamId);

            if (playerBot != null)
            {
                playerBot.displayName = string.IsNullOrWhiteSpace(clan.Tag) ? displayName : $"[{clan.Tag}] {displayName}";
            }

            bot.InitializeHealth(skill.Health, skill.Health);
            bot.SetHealth(skill.Health);

            var runtime = new BotRuntime
            {
                BotKey = botKey,
                DisplayName = displayName,
                KitName = kit?.Name ?? "legacy_scientist",
                SkillTier = skillTier,
                Skill = skill,
                TeamId = teamId,
                ClanKey = clan.Key,
                ClanTag = clan.Tag,
                ClanName = clan.Name,
                SpawnPosition = position,
                HomePosition = position,
                Prefab = prefab ?? "",
                EntityType = EntityTypeName(bot),
                State = TacticalState.Roam,
                PreviousState = TacticalState.Roam,
                StateEnteredAt = Time.realtimeSinceStartup,
                NextDecisionAt = Time.realtimeSinceStartup + UnityEngine.Random.Range(0f, Math.Max(0.1f, config.AI.DecisionTickSeconds)),
                NextPerceptionAt = Time.realtimeSinceStartup + UnityEngine.Random.Range(0f, Math.Max(0.1f, config.AI.PerceptionTickSeconds))
            };

            activeBots[bot] = runtime;
            var botStats = EnsureBotStats(runtime);
            botStats.spawns++;
            EnsureClanStats(runtime).bots_spawned++;
            QueueSaveData();

            if (kit != null && playerBot != null)
            {
                ApplyKit(playerBot, kit.Name);
            }

            RefreshCombatProfile(bot, runtime);
            bot.SendNetworkUpdateImmediate();
            return runtime;
        }

        private bool ShouldApplyKit(BaseCombatEntity bot)
        {
            return bot is BasePlayer;
        }

        private void ApplyKit(BasePlayer bot, string kitName)
        {
            if (Kits == null || string.IsNullOrWhiteSpace(kitName))
            {
                return;
            }

            bot.inventory?.Strip();
            var result = Kits.Call("GiveKit", bot, kitName);

            if (result is string message && !string.IsNullOrWhiteSpace(message))
            {
                PrintWarning($"Kits plugin could not give {kitName} to {bot.displayName}: {message}");
            }
        }

        private bool TryPlaceBotOnOwnNavmesh(BaseCombatEntity bot, ref Vector3 position)
        {
            if (bot == null)
            {
                return false;
            }

            var navigator = bot.GetComponent<BaseNavigator>() ?? bot.GetComponentInChildren<BaseNavigator>();

            if (navigator != null)
            {
                Vector3 nearest;

                if (navigator.GetNearestNavmeshPosition(position, out nearest, Math.Max(24f, config.Spawn.NavmeshSampleDistance * 3f)))
                {
                    bot.transform.position = nearest;
                    position = nearest;
                    bot.SendNetworkUpdateImmediate();
                }

                if (navigator.PlaceOnNavMesh(0f))
                {
                    position = bot.transform.position;
                    return !IsBlockedLandPosition(position);
                }

                return false;
            }

            return false;
        }

        private void DespawnBot(BaseCombatEntity bot, string reason)
        {
            if (bot == null || !activeBots.ContainsKey(bot))
            {
                return;
            }

            despawningBots.Add(bot);
            activeBots.Remove(bot);

            if (!bot.IsDestroyed)
            {
                bot.Kill(BaseNetworkable.DestroyMode.None);
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                Puts($"Despawned roam bot after {reason}.");
            }
        }

        private void KillAllBots(bool dropBodies)
        {
            foreach (var bot in activeBots.Keys.ToList())
            {
                if (bot == null)
                {
                    continue;
                }

                if (!dropBodies)
                {
                    despawningBots.Add(bot);
                }

                if (!bot.IsDestroyed)
                {
                    bot.Kill(BaseNetworkable.DestroyMode.None);
                }
            }

            activeBots.Clear();
            despawningBots.Clear();
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
            {
                return null;
            }

            var victim = entity;
            var victimPlayer = entity as BasePlayer;
            var attacker = info.Initiator as BasePlayer;
            var attackerEntity = info.Initiator as BaseCombatEntity;

            if (victim == null && attacker == null && attackerEntity == null)
            {
                return null;
            }

            var victimRuntime = RuntimeFor(victim);
            var attackerRuntime = RuntimeFor(attackerEntity);

            if (victimRuntime != null && attackerRuntime != null)
            {
                return true;
            }

            if (attackerRuntime != null && IsRealPlayer(victimPlayer) && ShouldIgnoreSafeZonePlayer(victimPlayer))
            {
                return true;
            }

            if (victimRuntime != null && IsRealPlayer(attacker) && ShouldIgnoreSafeZonePlayer(attacker))
            {
                return null;
            }

            var now = Time.realtimeSinceStartup;

            if (IsRealPlayer(attacker) && IsExplosionDamage(info))
            {
                BroadcastPlayerSound(attacker, SoundPositionFromHit(info, attacker), config.AI.ExplosionHearingRange, "explosion", 1f, 0.75f);
            }

            if (victimRuntime != null && IsRealPlayer(attacker))
            {
                info.damageTypes.ScaleAll(victimRuntime.Skill.IncomingDamageScale);
                victimRuntime.LastDamageTakenAt = now;
                victimRuntime.LastDamageBarricadeAwarenessCheckAt = 0f;
                victimRuntime.NextLowHealthAwarenessCheckAt = 0f;
                victimRuntime.Memory.TargetUserId = attacker.userID;
                victimRuntime.Memory.LastDamageSourcePlayer = attacker;
                victimRuntime.Memory.LastDamageSourcePosition = attacker.transform.position;
                victimRuntime.Memory.LastDamagedAt = now;

                if (HasLineOfSight(victim, attacker))
                {
                    victimRuntime.Memory.Target = attacker;
                    victimRuntime.Memory.HasLineOfSight = true;
                    victimRuntime.Memory.LastLineOfSightAt = now;
                    victimRuntime.Memory.LastSeenPosition = attacker.transform.position;
                    victimRuntime.Memory.LastSeenAt = now;
                    victimRuntime.Memory.TargetConfidence = Math.Max(victimRuntime.Memory.TargetConfidence, 0.7f);
                }
                else
                {
                    victimRuntime.Memory.TargetConfidence = Math.Max(victimRuntime.Memory.TargetConfidence, 0.55f);
                }

                if (config.AI.AllowHearing)
                {
                    victimRuntime.Memory.LastHeardPosition = attacker.transform.position;
                    victimRuntime.Memory.LastHeardAt = now;
                }
                return null;
            }

            if (attackerRuntime != null && IsRealPlayer(victimPlayer))
            {
                RefreshCombatProfile(attackerEntity, attackerRuntime);
                var distance = attackerEntity == null ? 0f : Vector3.Distance(attackerEntity.transform.position, victimPlayer.transform.position);
                info.damageTypes.ScaleAll(attackerRuntime.Skill.DamageScale * WeaponRangeDamageMultiplier(attackerRuntime, distance));
                attackerRuntime.LastDamageDealtAt = now;
                attackerRuntime.Memory.Target = victimPlayer;
                attackerRuntime.Memory.TargetUserId = victimPlayer.userID;
                attackerRuntime.Memory.LastSeenPosition = victimPlayer.transform.position;
                attackerRuntime.Memory.LastSeenAt = now;
                attackerRuntime.Memory.TargetConfidence = Math.Max(attackerRuntime.Memory.TargetConfidence, 0.85f);
            }

            return null;
        }

        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectileShoot)
        {
            if (!IsRealPlayer(player) || !config.AI.AllowHearing || ShouldIgnoreSafeZonePlayer(player))
            {
                return;
            }

            var item = projectile?.GetItem() ?? player.GetActiveItem();
            var shortname = item?.info?.shortname ?? "";
            var quietProjectile = IsQuietProjectileWeapon(shortname);
            var suppressed = !quietProjectile && IsSuppressedWeapon(item);
            var range = quietProjectile
                ? config.AI.MeleeOrToolHearingRange
                : suppressed
                    ? config.AI.SuppressedGunshotHearingRange
                    : config.AI.UnsuppressedGunshotHearingRange;
            var soundType = quietProjectile
                ? "quiet_projectile"
                : suppressed
                    ? "suppressed_gunshot"
                    : "gunshot";

            BroadcastPlayerSound(player, player.transform.position, range, soundType, quietProjectile ? 0.42f : suppressed ? 0.7f : 1f, 0.08f);
        }

        private void OnRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            if (!IsRealPlayer(player) || !config.AI.AllowHearing || ShouldIgnoreSafeZonePlayer(player))
            {
                return;
            }

            BroadcastPlayerSound(player, player.transform.position, config.AI.ExplosionHearingRange, "rocket_launch", 1f, 0.35f);
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            if (!IsRealPlayer(player) || !config.AI.AllowHearing || ShouldIgnoreSafeZonePlayer(player))
            {
                return;
            }

            BroadcastPlayerSound(player, player.transform.position, config.AI.MeleeOrToolHearingRange, "thrown_explosive", 0.45f, 0.35f);
        }

        private void OnMeleeAttack(BasePlayer player, HitInfo info)
        {
            if (!IsRealPlayer(player) || !config.AI.AllowHearing || ShouldIgnoreSafeZonePlayer(player))
            {
                return;
            }

            BroadcastPlayerSound(player, SoundPositionFromHit(info, player), config.AI.MeleeOrToolHearingRange, "melee_or_tool", 0.42f, 0.35f);
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity;
            var victimPlayer = entity as BasePlayer;

            if (victim == null)
            {
                return;
            }

            var victimRuntime = RuntimeFor(victim);
            var attacker = info?.Initiator as BasePlayer;
            var attackerEntity = info?.Initiator as BaseCombatEntity;
            var attackerRuntime = RuntimeFor(attackerEntity);

            if (victimRuntime != null)
            {
                activeBots.Remove(victim);

                if (!despawningBots.Remove(victim))
                {
                    var botStats = EnsureBotStats(victimRuntime);
                    botStats.deaths++;
                    EnsureClanStats(victimRuntime).deaths++;

                    if (IsRealPlayer(attacker) && attackerRuntime == null)
                    {
                        var playerStats = EnsurePlayerStats(attacker);
                        playerStats.npc_kills++;
                    }

                    QueueSaveData();
                    UpdateScoreboards();
                }

                if (config.Enabled)
                {
                    timer.Once(config.RespawnDelaySeconds, MaintainPopulation);
                }

                return;
            }

            if (attackerRuntime != null && IsRealPlayer(victimPlayer) && !ShouldIgnoreSafeZonePlayer(victimPlayer))
            {
                var playerStats = EnsurePlayerStats(victimPlayer);
                playerStats.deaths_by_npc++;
                var botStats = EnsureBotStats(attackerRuntime);
                botStats.kills++;
                EnsureClanStats(attackerRuntime).kills++;
                QueueSaveData();
                UpdateScoreboards();
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var bot = entity as BaseCombatEntity;

            if (bot == null)
            {
                return;
            }

            activeBots.Remove(bot);
            despawningBots.Remove(bot);
        }

        [ConsoleCommand("raidbots.status")]
        private void CmdStatus(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            RefreshEligibleKits();
            var retrySeconds = Math.Max(0f, spawnRetryBlockedUntil - Time.realtimeSinceStartup);
            var retryMessage = retrySeconds > 0f ? $", spawn retry in {retrySeconds:0}s" : "";
            Reply(arg, $"Raidlands roam bots: enabled={config.Enabled}, mode={config.Spawn.SpawnMode}, brain={TacticalBrainName}, anchor={SpawnAnchorLabel()}, target={TargetPopulation()}, active={activeBots.Count}{retryMessage}, advisor={config.DecisionAdvisor.Provider}/{config.DecisionAdvisor.Mode}, near-player anchors={SpawnAnchorPlayers().Count}, eligible kits={string.Join(", ", eligibleKits.Keys.OrderBy(name => name))}, tracked players={data.players.Count}, tracked bots={data.bots.Count}, tracked clans={data.bot_clans.Count}.");
        }

        [ConsoleCommand("raidbots.enable")]
        private void CmdEnable(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (TryReadIntArg(arg, 0, out var population))
            {
                config.TargetPopulation = Clamp(population, config.MinAllowedPopulation, config.MaxAllowedPopulation);
            }

            config.Enabled = true;
            SaveConfig();
            StartRuntime();
            Reply(arg, $"Raidlands roam bots enabled with target population {TargetPopulation()}.");
        }

        [ConsoleCommand("raidbots.disable")]
        private void CmdDisable(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            config.Enabled = false;
            SaveConfig();
            StopRuntime();
            if (config.Persistence.KillBotsOnDisable)
            {
                KillAllBots(!config.Persistence.LeaveCorpses);
                Reply(arg, "Raidlands roam bots disabled and active bots removed by persistence config.");
            }
            else
            {
                Reply(arg, "Raidlands roam bots disabled. Existing bots are left alone by persistence config; use raidbots.nuke to remove tracked bots.");
            }
        }

        [ConsoleCommand("raidbots.reload")]
        private void CmdReload(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            LoadConfig();
            LoadData();
            RefreshEligibleKits();

            if (config.Enabled)
            {
                StartRuntime();
            }
            else
            {
                StopRuntime();
            }

            CreateScoreboards();
            UpdateScoreboards();
            Reply(arg, "Raidlands roam bot config and data reloaded.");
        }

        [ConsoleCommand("raidbots.spawn")]
        private void CmdSpawn(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var count = 1;

            if (!config.Enabled)
            {
                Reply(arg, "Raidlands roam bots are disabled; run raidbots.enable [target] before spawning bots.");
                return;
            }

            if (TryReadIntArg(arg, 0, out var requested))
            {
                count = Clamp(requested, 1, config.MaxAllowedPopulation);
            }

            CleanupInactiveBots();

            var available = TargetPopulation() - activeBots.Count;

            if (available <= 0)
            {
                Reply(arg, $"Raidlands roam bots are already at target population {TargetPopulation()}.");
                return;
            }

            count = Math.Min(count, available);
            var spawned = SpawnBots(count, true);
            Reply(arg, $"Spawned {spawned} roam bot{(spawned == 1 ? "" : "s")}.");
        }

        [ConsoleCommand("raidbots.diag")]
        private void CmdDiag(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            RefreshEligibleKits();

            var anchors = SpawnAnchorPlayers();
            Reply(arg, $"Raidlands roam bot diag: enabled={config.Enabled}, mode={config.Spawn.SpawnMode}, brain={TacticalBrainName}, anchor={SpawnAnchorLabel()}, anchors={anchors.Count}, debugViewers={DebugUiViewerCount()}, target={TargetPopulation()}, active={activeBots.Count}, requireLand={config.Spawn.RequireLandSpawns}, advisor={config.DecisionAdvisor.Provider}/{config.DecisionAdvisor.Mode}.");

            foreach (var anchor in anchors.Take(5))
            {
                Reply(arg, $"Anchor {PlayerName(anchor)} at {FormatVector(anchor.transform.position)} ({PositionDiagnostics(anchor.transform.position)}).");
            }

            if (TryFindSpawnPosition(out var position, out var preferredPrefab))
            {
                var prefabs = string.Join(", ", ActivePrefabCandidates(preferredPrefab).Take(5));
                Reply(arg, $"Next spawn candidate: {FormatVector(position)} ({PositionDiagnostics(position)}), preferredPrefab={(string.IsNullOrWhiteSpace(preferredPrefab) ? "none" : preferredPrefab)}, activePrefabs={prefabs}.");
            }
            else
            {
                Reply(arg, "No spawn candidate found with the current mode, anchor, safe-zone, water, and distance filters.");
            }

            foreach (var entry in ActiveBotEntries().Take(5))
            {
                Reply(arg, $"Active {entry.Value.DisplayName}: {BotRuntimeDiagnostics(entry.Key, entry.Value)}.");
            }
        }

        [ConsoleCommand("raidbots.testsetup")]
        private void CmdTestSetup(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            try
            {
                var anchor = ArgStringFrom(arg, 0);
                var effectiveAnchor = string.IsNullOrWhiteSpace(anchor) ? config.Spawn.NearPlayerAnchorNameOrSteamId : anchor.Trim();

                if (!ValidateTestAnchor(arg, effectiveAnchor))
                {
                    return;
                }

                config.Enabled = false;
                config.TargetPopulation = 1;
                config.MinAllowedPopulation = 1;
                config.MaxAllowedPopulation = 3;
                config.TeamSizeWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["solo"] = 100,
                    ["duo"] = 0,
                    ["trio"] = 0
                };
                config.Spawn.SpawnMode = SpawnModeNearPlayers;
                config.Spawn.NearPlayerAnchorNameOrSteamId = effectiveAnchor;
                config.Spawn.UseGeneratedPositionsNearPlayers = true;
                config.Spawn.UseRandomLandFallback = false;
                config.Spawn.NearPlayerMinDistance = 45f;
                config.Spawn.NearPlayerMaxDistance = 180f;
                config.Spawn.NearPlayerAttempts = 160;
                config.Spawn.NavmeshSampleDistance = Math.Max(config.Spawn.NavmeshSampleDistance, 18f);
                config.Spawn.RequireLandSpawns = true;
                config.Spawn.AvoidSafeZoneSpawns = true;
                config.Spawn.IgnorePlayersInSafeZones = true;
                config.Debug.DebugSpawnDetails = true;
                config.Debug.DebugPerception = true;
                config.Debug.DebugTacticalDecisions = true;
                config.Debug.DebugBotNameplates = true;
                config.Debug.DebugBotSidePanel = true;
                config.SpawnFailureRetrySeconds = 30f;
                spawnRetryBlockedUntil = 0f;
                NormalizeConfig();
                SaveConfig();
                StopRuntime();

                Reply(arg, $"Raidlands roam bot tactical test setup applied: anchor={SpawnAnchorLabel()}, target={TargetPopulation()}, max={config.MaxAllowedPopulation}, brain={TacticalBrainName}, debug={config.Debug.DebugSpawnDetails}, requireLand={config.Spawn.RequireLandSpawns}. Run raidbots.diag, then raidbots.enable 1.");
            }
            catch (Exception ex)
            {
                PrintWarning($"raidbots.testsetup failed: {ex.GetType().Name}: {ex.Message}");
                Reply(arg, "Raidlands roam bot test setup failed; check the server console warning for details.");
            }
        }

        [ConsoleCommand("raidbots.squadtest")]
        private void CmdSquadTest(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            try
            {
                var anchor = ArgStringFrom(arg, 0);
                var effectiveAnchor = string.IsNullOrWhiteSpace(anchor) ? config.Spawn.NearPlayerAnchorNameOrSteamId : anchor.Trim();

                if (!ValidateTestAnchor(arg, effectiveAnchor))
                {
                    return;
                }

                config.Enabled = false;
                config.TargetPopulation = 3;
                config.MinAllowedPopulation = 1;
                config.MaxAllowedPopulation = Math.Max(3, config.MaxAllowedPopulation);
                config.TeamSizeWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["solo"] = 0,
                    ["duo"] = 35,
                    ["trio"] = 65
                };
                config.Spawn.SpawnMode = SpawnModeNearPlayers;
                config.Spawn.NearPlayerAnchorNameOrSteamId = effectiveAnchor;
                config.Spawn.UseGeneratedPositionsNearPlayers = true;
                config.Spawn.UseRandomLandFallback = false;
                config.Spawn.NearPlayerMinDistance = 55f;
                config.Spawn.NearPlayerMaxDistance = 185f;
                config.Spawn.NearPlayerAttempts = 180;
                config.Spawn.NavmeshSampleDistance = Math.Max(config.Spawn.NavmeshSampleDistance, 18f);
                config.Spawn.RequireLandSpawns = true;
                config.Spawn.AvoidSafeZoneSpawns = true;
                config.Spawn.IgnorePlayersInSafeZones = true;
                config.Debug.DebugSpawnDetails = true;
                config.Debug.DebugPerception = true;
                config.Debug.DebugTacticalDecisions = true;
                config.Debug.DebugBotNameplates = true;
                config.Debug.DebugBotSidePanel = true;
                config.SpawnFailureRetrySeconds = 30f;
                spawnRetryBlockedUntil = 0f;
                NormalizeConfig();
                SaveConfig();
                StopRuntime();

                Reply(arg, $"Raidlands roam bot squad test setup applied: anchor={SpawnAnchorLabel()}, target={TargetPopulation()}, teamWeights=duo/trio, brain={TacticalBrainName}. Run raidbots.diag, then raidbots.enable 3.");
            }
            catch (Exception ex)
            {
                PrintWarning($"raidbots.squadtest failed: {ex.GetType().Name}: {ex.Message}");
                Reply(arg, "Raidlands roam bot squad test setup failed; check the server console warning for details.");
            }
        }

        [ConsoleCommand("raidbots.nuke")]
        private void CmdNuke(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (!config.Persistence.EmergencyKillCommandEnabled)
            {
                Reply(arg, "Raidlands roam bot emergency kill command is disabled in config.");
                return;
            }

            var mode = ArgString(arg, 0).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(mode) || mode == "active" || mode == "bots")
            {
                var count = activeBots.Count;
                KillAllBots(false);
                Reply(arg, $"Emergency removed {count} tracked roam bot{(count == 1 ? "" : "s")}.");
                return;
            }

            if (mode == "debug")
            {
                DestroyDebugBotPanels();
                Reply(arg, "Cleared Raidlands roam bot debug panels. Active bots and bot-placed world entities were left alone.");
                return;
            }

            if (mode == "all")
            {
                var count = activeBots.Count;
                KillAllBots(false);
                DestroyDebugBotPanels();
                Reply(arg, $"Emergency removed {count} tracked roam bot{(count == 1 ? "" : "s")} and cleared debug panels. Bot-placed world entities are intentionally persistent.");
                return;
            }

            Reply(arg, "Usage: raidbots.nuke [active|debug|all]");
        }

        [ConsoleCommand("raidbots.debug")]
        private void CmdDebug(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (!TryReadBoolArg(arg, 0, out var enabled))
            {
                Reply(arg, $"Raidlands roam bot debug spawn details is {config.Debug.DebugSpawnDetails}. Use raidbots.debug on|off.");
                return;
            }

            config.Debug.DebugSpawnDetails = enabled;
            config.Debug.DebugPerception = enabled;
            config.Debug.DebugTacticalDecisions = enabled;
            config.Debug.DebugBotNameplates = enabled;
            config.Debug.DebugBotSidePanel = enabled;
            SaveConfig();
            StartNameplateTimerIfEnabled();
            Reply(arg, $"Raidlands roam bot debug details set to {config.Debug.DebugSpawnDetails}; debug UI viewers={DebugUiViewerCount()}.");
        }

        [ConsoleCommand("raidbots.decisions")]
        private void CmdDecisions(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var mode = ArgString(arg, 0).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(mode) || mode == "last")
            {
                var count = 5;

                if (TryReadIntArg(arg, 1, out var requested))
                {
                    count = Clamp(requested, 1, 20);
                }

                FlushDecisionTraces();
                var lines = ReadDecisionTraceLines(count, "");

                if (lines.Count == 0)
                {
                    Reply(arg, "No Raidlands roam bot decision traces have been written yet.");
                    return;
                }

                foreach (var line in lines)
                {
                    Reply(arg, FormatDecisionTraceLine(line));
                }

                return;
            }

            if (mode == "bot")
            {
                var query = ArgString(arg, 1);
                var count = 5;

                if (TryReadIntArg(arg, 2, out var requested))
                {
                    count = Clamp(requested, 1, 20);
                }

                if (string.IsNullOrWhiteSpace(query))
                {
                    Reply(arg, "Usage: raidbots.decisions bot <bot name/key> [count]");
                    return;
                }

                FlushDecisionTraces();
                var lines = ReadDecisionTraceLines(count, BotKey(query));

                if (lines.Count == 0)
                {
                    Reply(arg, $"No decision traces found for bot '{query}'.");
                    return;
                }

                foreach (var line in lines)
                {
                    Reply(arg, FormatDecisionTraceLine(line));
                }

                return;
            }

            if (mode == "export")
            {
                FlushDecisionTraces();
                var path = DecisionTraceDataPath();
                var count = File.Exists(path) ? File.ReadLines(path).Count() : 0;
                Reply(arg, $"Decision trace JSONL: {path} ({count} lines).");
                return;
            }

            Reply(arg, "Usage: raidbots.decisions [last [count]|bot <name/key> [count]|export]");
        }

        [ConsoleCommand("raidbots.land")]
        private void CmdLand(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (!TryReadBoolArg(arg, 0, out var enabled))
            {
                Reply(arg, $"Raidlands roam bot land-spawn requirement is {config.Spawn.RequireLandSpawns}. Use raidbots.land on|off.");
                return;
            }

            config.Spawn.RequireLandSpawns = enabled;
            SaveConfig();
            spawnRetryBlockedUntil = 0f;
            Reply(arg, $"Raidlands roam bot land-spawn requirement set to {config.Spawn.RequireLandSpawns}.");
        }

        [ConsoleCommand("raidbots.target")]
        private void CmdTarget(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (!TryReadIntArg(arg, 0, out var population))
            {
                Reply(arg, $"Raidlands roam bot target population is {TargetPopulation()}.");
                return;
            }

            config.TargetPopulation = Clamp(population, config.MinAllowedPopulation, config.MaxAllowedPopulation);
            SaveConfig();
            spawnRetryBlockedUntil = 0f;

            if (config.Enabled)
            {
                MaintainPopulation();
            }

            Reply(arg, $"Raidlands roam bot target population set to {TargetPopulation()}.");
        }

        [ConsoleCommand("raidbots.mode")]
        private void CmdMode(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var requested = ArgString(arg, 0);

            if (string.IsNullOrWhiteSpace(requested))
            {
                Reply(arg, $"Raidlands roam bot spawn mode is {config.Spawn.SpawnMode}. Use {SpawnModeNearPlayers} or {SpawnModeRandom}.");
                return;
            }

            if (!TryNormalizeSpawnMode(requested, out var mode))
            {
                Reply(arg, $"Unknown spawn mode '{requested}'. Use {SpawnModeNearPlayers} or {SpawnModeRandom}.");
                return;
            }

            config.Spawn.SpawnMode = mode;
            SaveConfig();
            spawnRetryBlockedUntil = 0f;

            if (config.Enabled)
            {
                MaintainPopulation();
            }

            Reply(arg, $"Raidlands roam bot spawn mode set to {mode}.");
        }

        [ConsoleCommand("raidbots.anchor")]
        private void CmdAnchor(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var requested = ArgStringFrom(arg, 0);

            if (string.IsNullOrWhiteSpace(requested))
            {
                Reply(arg, $"Raidlands roam bot near-player anchor is {SpawnAnchorLabel()}. Use raidbots.anchor <name or steam id>, or raidbots.anchor clear.");
                return;
            }

            if (requested.Equals("clear", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("off", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("all", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                config.Spawn.NearPlayerAnchorNameOrSteamId = "";
            }
            else
            {
                config.Spawn.NearPlayerAnchorNameOrSteamId = requested.Trim();
            }

            SaveConfig();
            spawnRetryBlockedUntil = 0f;

            if (config.Enabled)
            {
                MaintainPopulation();
            }

            Reply(arg, $"Raidlands roam bot near-player anchor set to {SpawnAnchorLabel()}.");
        }

        [ConsoleCommand("raidbots.list")]
        private void CmdList(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            CleanupInactiveBots();

            var target = ArgString(arg, 0);
            var targetPlayer = string.IsNullOrWhiteSpace(target) ? null : FindActivePlayer(target);
            var bots = ActiveBotEntries();

            if (bots.Count == 0)
            {
                Reply(arg, "No active Raidlands roam bots.");
                return;
            }

            Reply(arg, $"Active Raidlands roam bots ({bots.Count}):");

            for (var index = 0; index < bots.Count; index++)
            {
                var bot = bots[index].Key;
                var runtime = bots[index].Value;
                var position = bot.transform.position;
                var distance = targetPlayer == null ? "" : $", {Vector3.Distance(targetPlayer.transform.position, position):0}m from {PlayerName(targetPlayer)}";
                Reply(arg, $"{index + 1}. {runtime.DisplayName} [{runtime.KitName}/{runtime.SkillTier}; {BotRuntimeDiagnostics(bot, runtime)}] at {FormatVector(position)}{distance}");
            }
        }

        [ConsoleCommand("raidbots.goto")]
        private void CmdGoto(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var playerQuery = ArgString(arg, 0);

            if (string.IsNullOrWhiteSpace(playerQuery))
            {
                Reply(arg, "Usage: raidbots.goto <player name or steam id> [bot number]");
                return;
            }

            var player = FindActivePlayer(playerQuery);

            if (player == null)
            {
                Reply(arg, $"No connected player matched '{playerQuery}'.");
                return;
            }

            var bots = ActiveBotEntries();

            if (bots.Count == 0)
            {
                Reply(arg, "No active Raidlands roam bots to visit.");
                return;
            }

            var botIndex = 1;

            if (TryReadIntArg(arg, 1, out var requestedIndex))
            {
                botIndex = Clamp(requestedIndex, 1, bots.Count);
            }

            var selected = bots[botIndex - 1];
            var botPosition = selected.Key.transform.position;
            var destination = botPosition + selected.Key.transform.right * 8f + Vector3.up;

            if (TryFindNearbyPosition(botPosition, out var nearbyPosition))
            {
                destination = nearbyPosition;
            }

            destination.y = Math.Max(destination.y, TerrainHeight(destination) + 1f);
            player.Teleport(destination);
            Reply(arg, $"Moved {PlayerName(player)} near bot #{botIndex} {selected.Value.DisplayName} at {FormatVector(botPosition)}.");
        }

        [ConsoleCommand("raidbots.killall")]
        private void CmdKillAll(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var count = activeBots.Count;
            KillAllBots(false);
            Reply(arg, $"Despawned {count} roam bot{(count == 1 ? "" : "s")}.");
        }

        [HookMethod("GetRaidlandsRoamBotStats")]
        public JObject GetRaidlandsRoamBotStats()
        {
            return JObject.FromObject(data ?? new StoredData());
        }

        private void RefreshEligibleKits()
        {
            eligibleKits.Clear();

            var kitData = ReadKitsData();

            if (kitData == null)
            {
                return;
            }

            var kits = kitData["_kits"] as JObject;

            if (kits == null)
            {
                return;
            }

            var allowed = new HashSet<string>(config.Kits.EligibleKitNames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var highTier = new HashSet<string>(config.Kits.RareHighTierKitNames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var property in kits.Properties())
            {
                var name = property.Name;
                var kit = property.Value as JObject;

                if (kit == null)
                {
                    continue;
                }

                var isHighTier = highTier.Contains(name);

                if (!allowed.Contains(name) && !isHighTier)
                {
                    continue;
                }

                if (kit.Value<bool?>("IsHidden") == true)
                {
                    continue;
                }

                var requiredPermission = (string) kit["RequiredPermission"] ?? "";

                if (!DefaultGroupHasKitPermission(requiredPermission))
                {
                    continue;
                }

                if (!KitContainsWeapon(kit))
                {
                    continue;
                }

                eligibleKits[name] = new KitEligibility
                {
                    Name = name,
                    RequiredPermission = requiredPermission,
                    HighTier = isHighTier
                };
            }
        }

        private JObject ReadKitsData()
        {
            try
            {
                var dataPath = Path.Combine(Interface.Oxide.DataFileSystem.Directory, $"{KitsDataFile}.json");

                if (!File.Exists(dataPath))
                {
                    return null;
                }

                return JObject.Parse(File.ReadAllText(dataPath));
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read Kits data for roam bot eligibility: {ex.Message}");
                return null;
            }
        }

        private bool DefaultGroupHasKitPermission(string requiredPermission)
        {
            if (string.IsNullOrWhiteSpace(requiredPermission))
            {
                return true;
            }

            return permission.GroupHasPermission(config.Kits.DefaultGroup, requiredPermission);
        }

        private bool KitContainsWeapon(JObject kit)
        {
            var weaponShortnames = new HashSet<string>(config.Kits.WeaponShortnames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var container in new[] { "BeltItems", "MainItems", "WearItems" })
            {
                var items = kit[container] as JArray;

                if (items == null)
                {
                    continue;
                }

                foreach (var item in items.OfType<JObject>())
                {
                    var shortname = ((string) item["Shortname"] ?? "").Trim();

                    if (weaponShortnames.Contains(shortname))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private KitEligibility ChooseKit()
        {
            if (eligibleKits.Count == 0)
            {
                return null;
            }

            var highTier = eligibleKits.Values.Where(kit => kit.HighTier).ToList();

            if (highTier.Count > 0 && random.Next(100) < config.HighTierKitWeight)
            {
                return highTier[random.Next(highTier.Count)];
            }

            var normal = eligibleKits.Values.Where(kit => !kit.HighTier).ToList();

            if (normal.Count == 0)
            {
                normal = eligibleKits.Values.ToList();
            }

            return normal[random.Next(normal.Count)];
        }

        private int WeightedTeamSize()
        {
            var value = WeightedKey(config.TeamSizeWeights, "solo");

            if (value.Equals("trio", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (value.Equals("duo", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 1;
        }

        private BotClanDefinition ClanForTeam(int teamId)
        {
            var clans = config?.BotClans ?? new List<BotClanDefinition>();

            if (clans.Count == 0)
            {
                return new BotClanDefinition { Key = "raidlands", Tag = "RDL", Name = "Raidlands" };
            }

            var index = Math.Abs(Math.Max(1, teamId) - 1) % clans.Count;
            var clan = clans[index] ?? clans[0];

            return new BotClanDefinition
            {
                Key = clan.Key ?? "",
                Tag = clan.Tag ?? "",
                Name = clan.Name ?? ""
            };
        }

        private string WeightedKey(Dictionary<string, int> weights, string fallback)
        {
            if (weights == null || weights.Count == 0)
            {
                return fallback;
            }

            var entries = weights
                .Where(entry => entry.Value > 0 && !string.IsNullOrWhiteSpace(entry.Key))
                .ToList();

            var total = entries.Sum(entry => Math.Max(0, entry.Value));

            if (total <= 0)
            {
                return fallback;
            }

            var roll = random.Next(total);
            var running = 0;

            foreach (var entry in entries)
            {
                running += Math.Max(0, entry.Value);

                if (roll < running)
                {
                    return entry.Key;
                }
            }

            return fallback;
        }

        private SkillDefinition SkillFor(string tier)
        {
            if (config.SkillDefinitions != null && config.SkillDefinitions.TryGetValue(tier, out var definition) && definition != null)
            {
                definition.Health = Math.Max(1f, definition.Health);
                definition.DamageScale = Math.Max(0.1f, definition.DamageScale);
                definition.IncomingDamageScale = Math.Max(0.1f, definition.IncomingDamageScale);
                definition.ReactionMinSeconds = Math.Max(0f, definition.ReactionMinSeconds);
                definition.ReactionMaxSeconds = Math.Max(definition.ReactionMinSeconds, definition.ReactionMaxSeconds);
                definition.AimErrorDegrees = Math.Max(0f, definition.AimErrorDegrees);
                definition.Aggression = Mathf.Clamp01(definition.Aggression);
                definition.Courage = Mathf.Clamp01(definition.Courage);
                definition.TacticalNoise = Mathf.Clamp01(definition.TacticalNoise);
                return definition;
            }

            return new SkillDefinition();
        }

        private string ChooseProfileName()
        {
            var activeKeys = new HashSet<string>(activeBots.Values.Select(runtime => runtime.BotKey), StringComparer.OrdinalIgnoreCase);
            var candidates = (config.BotProfiles ?? new List<string>())
                .Select(name => CleanName(name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(_ => random.Next())
                .ToList();

            foreach (var candidate in candidates)
            {
                if (!activeKeys.Contains(BotKey(candidate)))
                {
                    return candidate;
                }
            }

            return $"Roamer{random.Next(1000, 9999).ToString(CultureInfo.InvariantCulture)}";
        }

        private string CleanName(string value)
        {
            var text = (value ?? "").Trim();

            if (text.Length > 32)
            {
                text = text.Substring(0, 32);
            }

            return string.Concat(text.Where(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == ' ')).Trim();
        }

        private string BotKey(string displayName)
        {
            var builder = new System.Text.StringBuilder();

            foreach (var character in (displayName ?? "").Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                    continue;
                }

                if ((character == '_' || character == '-' || char.IsWhiteSpace(character)) && builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            var key = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(key) ? "roamer" : key;
        }

        private bool TryFindSpawnPosition(out Vector3 position, out string preferredPrefab)
        {
            var mode = NormalizeSpawnMode(config.Spawn.SpawnMode);

            if (mode == SpawnModeNearPlayers)
            {
                if (TryFindNearPlayerSpawnPosition(out position, out preferredPrefab))
                {
                    return true;
                }

                if (!config.Spawn.UseRandomLandFallback)
                {
                    position = Vector3.zero;
                    preferredPrefab = "";
                    return false;
                }
            }

            return TryFindRandomLandSpawnPosition(out position, out preferredPrefab);
        }

        private bool TryFindNearPlayerSpawnPosition(out Vector3 position, out string preferredPrefab)
        {
            var players = SpawnAnchorPlayers();

            if (players.Count == 0)
            {
                position = Vector3.zero;
                preferredPrefab = "";
                return false;
            }

            if (!config.Spawn.UseGeneratedPositionsNearPlayers)
            {
                position = Vector3.zero;
                preferredPrefab = "";
                return false;
            }

            var minDistance = Math.Max(25f, config.Spawn.NearPlayerMinDistance);
            var maxDistance = Math.Max(minDistance + 10f, config.Spawn.NearPlayerMaxDistance);
            var attempts = Math.Max(8, config.Spawn.NearPlayerAttempts);

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var player = players[UnityEngine.Random.Range(0, players.Count)];
                var distance = UnityEngine.Random.Range(minDistance, maxDistance);
                var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var offset = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                var candidate = player.transform.position + offset;

                candidate.y = TerrainHeight(candidate) + 0.25f;

                if (IsBlockedLandPosition(candidate))
                {
                    continue;
                }

                if (!NavMesh.SamplePosition(candidate, out var hit, config.Spawn.NavmeshSampleDistance, NavMesh.AllAreas))
                {
                    continue;
                }

                if (IsBlockedLandPosition(hit.position) || Vector3.Distance(player.transform.position, hit.position) < minDistance * 0.8f)
                {
                    continue;
                }

                position = hit.position;
                preferredPrefab = "";
                return true;
            }

            position = Vector3.zero;
            preferredPrefab = "";
            return false;
        }

        private bool TryFindRandomLandSpawnPosition(out Vector3 position, out string preferredPrefab)
        {
            var mapSize = TerrainMeta.Size.x > 0f ? TerrainMeta.Size.x : 4500f;
            var half = mapSize * 0.5f;

            for (var attempt = 0; attempt < config.Spawn.MaxPositionAttempts; attempt++)
            {
                var candidate = new Vector3(
                    UnityEngine.Random.Range(-half, half),
                    0f,
                    UnityEngine.Random.Range(-half, half)
                );

                candidate.y = TerrainHeight(candidate) + 0.25f;

                if (IsBlockedLandPosition(candidate))
                {
                    continue;
                }

                if (!NavMesh.SamplePosition(candidate, out var hit, config.Spawn.NavmeshSampleDistance, NavMesh.AllAreas))
                {
                    continue;
                }

                if (IsBlockedLandPosition(hit.position))
                {
                    continue;
                }

                position = hit.position;
                preferredPrefab = "";
                return true;
            }

            position = Vector3.zero;
            preferredPrefab = "";
            return false;
        }

        private bool TryFindNearbyPosition(Vector3 origin, out Vector3 position)
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var offset = UnityEngine.Random.insideUnitCircle * config.Spawn.GroupSpawnRadius;
                var candidate = origin + new Vector3(offset.x, 0f, offset.y);
                candidate.y = TerrainHeight(candidate) + 0.25f;

                if (IsBlockedLandPosition(candidate))
                {
                    continue;
                }

                if (NavMesh.SamplePosition(candidate, out var hit, config.Spawn.NavmeshSampleDistance, NavMesh.AllAreas))
                {
                    if (!IsBlockedLandPosition(hit.position))
                    {
                        position = hit.position;
                        return true;
                    }
                }
            }

            position = origin;
            return false;
        }

        private float TerrainHeight(Vector3 position)
        {
            return TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(position) : position.y;
        }

        private string PositionDiagnostics(Vector3 position)
        {
            var terrain = TerrainHeight(position);
            var terrainDelta = position.y - terrain;
            var waterMap = TerrainMeta.WaterMap != null ? TerrainMeta.WaterMap.GetHeight(position) : float.NaN;
            var waterSurface = float.NaN;

            try
            {
                waterSurface = WaterLevel.GetWaterSurface(position, true, true, null);
            }
            catch
            {
            }

            var sampled = NavMesh.SamplePosition(position, out var hit, config.Spawn.NavmeshSampleDistance, NavMesh.AllAreas);
            var sample = sampled ? FormatVector(hit.position) : "none";
            return $"terrain={terrain:0.0}, terrainDelta={terrainDelta:0.0}, belowTerrain={IsBelowTerrain(position)}, waterMap={(float.IsNaN(waterMap) ? "n/a" : waterMap.ToString("0.0", CultureInfo.InvariantCulture))}, waterSurface={(float.IsNaN(waterSurface) ? "n/a" : waterSurface.ToString("0.0", CultureInfo.InvariantCulture))}, underwater={IsUnderWater(position)}, safeZone={IsBlockedSafeZoneSpawn(position)}, baseRestricted={IsBaseRestrictedPosition(position)}, unityNavSample={sample}";
        }

        private bool IsUnderWater(Vector3 position)
        {
            if (config?.Spawn?.RequireLandSpawns != true)
            {
                return false;
            }

            if (position.y < config.Spawn.MinimumLandHeight)
            {
                return true;
            }

            if (TerrainMeta.WaterMap != null && TerrainMeta.WaterMap.GetHeight(position) + config.Spawn.MinimumAboveWater > position.y)
            {
                return true;
            }

            try
            {
                if (WaterLevel.Test(position, true, true))
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                var waterSurface = WaterLevel.GetWaterSurface(position, true, true, null);

                if (!float.IsNaN(waterSurface) && position.y < waterSurface + config.Spawn.MinimumAboveWater)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsBelowTerrain(Vector3 position)
        {
            if (config?.Spawn?.RequireLandSpawns != true || TerrainMeta.HeightMap == null)
            {
                return false;
            }

            var tolerance = Math.Max(0f, config.Spawn.MaximumBelowTerrainTolerance);
            return position.y < TerrainHeight(position) - tolerance;
        }

        private bool IsBlockedLandPosition(Vector3 position)
        {
            return IsUnderWater(position) || IsBelowTerrain(position) || IsBlockedSafeZoneSpawn(position) || IsBaseRestrictedPosition(position);
        }

        private bool IsBaseRestrictedPosition(Vector3 position)
        {
            if (config?.AI?.DoNotEnterBases != true)
            {
                return false;
            }

            var radius = Math.Max(1f, config.AI.BaseAvoidanceRadius);
            var mask = LayerMask.GetMask("Construction", "Deployed");
            var colliders = Physics.OverlapSphere(position, radius, mask, QueryTriggerInteraction.Collide);

            foreach (var collider in colliders)
            {
                var entity = collider == null ? null : collider.GetComponentInParent<BaseEntity>();

                if (IsPlayerBaseEntity(entity))
                {
                    return true;
                }
            }

            return false;
        }

        private bool SegmentCrossesBaseRestrictedArea(Vector3 from, Vector3 to)
        {
            if (config?.AI?.DoNotEnterBases != true || from == Vector3.zero || to == Vector3.zero)
            {
                return false;
            }

            var distance = Vector3.Distance(from, to);
            var step = Math.Max(4f, config.AI.BaseAvoidanceRadius * 0.75f);
            var samples = Math.Max(1, Mathf.CeilToInt(distance / step));

            for (var index = 1; index <= samples; index++)
            {
                var point = Vector3.Lerp(from, to, index / (float)samples);

                if (IsBaseRestrictedPosition(point))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindOutsideBaseHoldPoint(Vector3 botPosition, Vector3 threatPosition, out Vector3 holdPoint)
        {
            holdPoint = Vector3.zero;
            var away = botPosition - threatPosition;
            away.y = 0f;

            if (threatPosition == Vector3.zero || away.sqrMagnitude <= 0.01f)
            {
                away = Vector3.forward;
            }

            away.Normalize();
            var radius = Math.Max(10f, config.AI.BaseAvoidanceRadius * 2f);
            var angles = new[] { 0f, 35f, -35f, 70f, -70f, 120f, -120f, 180f };

            foreach (var angle in angles)
            {
                var direction = Quaternion.Euler(0f, angle, 0f) * away;
                var candidate = botPosition + direction.normalized * radius;

                if (!TrySampleTacticalPosition(candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), out var sampled))
                {
                    continue;
                }

                if (IsBaseRestrictedPosition(sampled))
                {
                    continue;
                }

                holdPoint = sampled;
                return true;
            }

            return false;
        }

        private bool IsPlayerBaseEntity(BaseEntity entity)
        {
            if (entity == null)
            {
                return false;
            }

            var combat = entity as BaseCombatEntity;

            if (combat != null && activeBots.ContainsKey(combat))
            {
                return false;
            }

            if (entity is BuildingPrivlidge || entity is BuildingBlock || entity is Door)
            {
                return true;
            }

            if (entity.OwnerID == 0)
            {
                return false;
            }

            var text = $"{entity.GetType().Name} {entity.ShortPrefabName} {entity.PrefabName}".ToLowerInvariant();
            return text.Contains("cupboard")
                || text.Contains("foundation")
                || text.Contains("wall")
                || text.Contains("floor")
                || text.Contains("door")
                || text.Contains("frame")
                || text.Contains("shutter")
                || text.Contains("barricade")
                || text.Contains("gate.external");
        }

        private bool IsNavigatorOffNavmesh(BaseCombatEntity bot)
        {
            if (bot == null)
            {
                return true;
            }

            var navigator = bot.GetComponent<BaseNavigator>() ?? bot.GetComponentInChildren<BaseNavigator>();

            if (navigator == null)
            {
                return false;
            }

            try
            {
                return navigator.StuckOffNavmesh;
            }
            catch
            {
                return false;
            }
        }

        private bool IsInvalidRuntimePosition(BaseCombatEntity bot)
        {
            if (bot == null)
            {
                return true;
            }

            return IsUnderWater(bot.transform.position)
                || IsBelowTerrain(bot.transform.position)
                || IsNavigatorOffNavmesh(bot);
        }

        private bool EnsureBotPositionUsable(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (bot == null || runtime == null)
            {
                return false;
            }

            if (!IsInvalidRuntimePosition(bot))
            {
                runtime.InvalidPositionSince = 0f;
                return true;
            }

            StopBotAttack(bot, runtime);
            runtime.Memory.HasLineOfSight = false;
            runtime.Memory.TargetExposureFraction = 0f;
            runtime.Memory.TargetVisibleProbePoints = 0;
            runtime.Memory.TargetTotalProbePoints = 0;

            if (runtime.InvalidPositionSince <= 0f)
            {
                runtime.InvalidPositionSince = now;

                if (config.Debug.DebugSpawnDetails)
                {
                    PrintWarning($"Roam bot {runtime.DisplayName} entered an invalid terrain/nav position at {FormatVector(bot.transform.position)} ({PositionDiagnostics(bot.transform.position)}); combat paused.");
                }
            }

            if (now - runtime.InvalidPositionSince >= config.Spawn.RuntimeInvalidPositionDespawnSeconds)
            {
                DespawnBot(bot, $"invalid terrain/nav position at {FormatVector(bot.transform.position)} ({PositionDiagnostics(bot.transform.position)})");
            }

            return false;
        }

        private bool IsNonLandRoamPrefab(string prefab)
        {
            var value = prefab ?? "";
            return value.IndexOf("underwaterdweller", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("tunneldweller", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool CanUseRoamPrefab(string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
            {
                return false;
            }

            if (config.Spawn.RequireLandSpawns && IsNonLandRoamPrefab(prefab))
            {
                return false;
            }

            return IsLegacyScientistBodyPrefab(prefab);
        }

        private bool IsLegacyScientistBodyPrefab(string prefab)
        {
            var value = prefab ?? "";
            return value.IndexOf("/scientist/", StringComparison.OrdinalIgnoreCase) >= 0
                && value.IndexOf("scientistnpc", StringComparison.OrdinalIgnoreCase) >= 0
                && value.IndexOf("/gen2/", StringComparison.OrdinalIgnoreCase) < 0
                && value.IndexOf("scientistnpc_ptboat", StringComparison.OrdinalIgnoreCase) < 0
                && value.IndexOf("scientistnpc_rhib", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private void PerceptionTick()
        {
            var now = Time.realtimeSinceStartup;

            foreach (var entry in activeBots.ToList())
            {
                if (!IsLiveBot(entry.Key) || entry.Value == null || now < entry.Value.NextPerceptionAt)
                {
                    continue;
                }

                if (!EnsureBotPositionUsable(entry.Key, entry.Value, now))
                {
                    continue;
                }

                entry.Value.NextPerceptionAt = now + Math.Max(0.1f, config.AI.PerceptionTickSeconds);
                UpdatePerception(entry.Key, entry.Value, now);
            }
        }

        private void TacticalBrainTick()
        {
            var now = Time.realtimeSinceStartup;

            foreach (var entry in activeBots.ToList())
            {
                var bot = entry.Key;
                var runtime = entry.Value;

                if (!IsLiveBot(bot) || runtime == null || now < runtime.NextDecisionAt)
                {
                    continue;
                }

                if (!EnsureBotPositionUsable(bot, runtime, now))
                {
                    continue;
                }

                if (ShouldDespawnHardStuck(bot, runtime, now))
                {
                    DespawnBot(bot, $"hard-stuck pathing ({runtime.ConsecutiveFailedPaths} failed paths, state={runtime.State})");
                    continue;
                }

                runtime.NextDecisionAt = now + Math.Max(0.15f, config.AI.DecisionTickSeconds);
                UpdateRetreatPosture(bot, runtime, now);
                UpdateMedicalHealing(bot, runtime, now);
                var request = BuildDecisionRequest(bot, runtime, now);
                var candidates = BuildCandidateActions(bot, runtime, now);

                if (candidates.Count == 0)
                {
                    continue;
                }

                request.CandidateActions = candidates;
                var decision = DecideOrFallback(bot, runtime, request, candidates, now);
                ExecuteDecision(bot, runtime, decision, now);
            }
        }

        private void UpdateRetreatPosture(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (bot == null || runtime == null)
            {
                return;
            }

            var inRetreatPosture = runtime.State == TacticalState.Retreat || runtime.State == TacticalState.BarricadeHold;

            if (!inRetreatPosture)
            {
                return;
            }

            if (IsEffectiveCover(bot, runtime, runtime.Memory.Target))
            {
                SetState(runtime, TacticalState.FightFromCover, now);
                runtime.IsPeeking = false;

                if (runtime.CurrentTuckPoint == Vector3.zero)
                {
                    runtime.CurrentTuckPoint = runtime.CurrentCover;
                }

                if (runtime.NextPeekAt <= now)
                {
                    runtime.NextPeekAt = now + UnityEngine.Random.Range(config.AI.TuckMinSeconds, config.AI.TuckMaxSeconds);
                }

                MaintainFireOrStop(bot, runtime, now);
                return;
            }

            var reachedDestination = runtime.CurrentDestination != Vector3.zero
                && Vector3.Distance(bot.transform.position, runtime.CurrentDestination) <= Math.Max(1.25f, config.AI.CoverArrivalDistance * 1.5f);

            if (!reachedDestination)
            {
                return;
            }

            runtime.NextCoverSearchAt = 0f;

            if (runtime.State == TacticalState.Retreat
                && now - runtime.StateEnteredAt >= RetreatFallbackTimeoutSeconds
                && !HasRecentContact(runtime, now))
            {
                runtime.LowHealthCoverAwareUntil = 0f;
                SetState(runtime, runtime.Memory.TargetUserId == 0 ? TacticalState.Roam : TacticalState.SearchLastKnown, now);
            }
        }

        private void SquadTick()
        {
            squadBlackboards.Clear();

            var teams = activeBots
                .Where(entry => IsLiveBot(entry.Key) && entry.Value != null)
                .GroupBy(entry => entry.Value.TeamId)
                .ToList();

            foreach (var team in teams)
            {
                var members = team
                    .OrderBy(entry => entry.Value.BotKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (members.Count == 0)
                {
                    continue;
                }

                var board = new SquadBlackboard
                {
                    TeamId = team.Key,
                    TeamSize = members.Count,
                    ClanKey = members[0].Value.ClanKey,
                    ClanTag = members[0].Value.ClanTag,
                    ClanName = members[0].Value.ClanName
                };

                squadBlackboards[team.Key] = board;

                for (var index = 0; index < members.Count; index++)
                {
                    var bot = members[index].Key;
                    var runtime = members[index].Value;
                    runtime.SquadRole = SquadRoleFor(index, members.Count);
                    board.TeamCenter += bot.transform.position;

                    if (index == 0)
                    {
                        board.RallyPoint = runtime.HomePosition;
                    }

                    if (runtime.CurrentCover != Vector3.zero)
                    {
                        board.CoverClaims[runtime.BotKey] = runtime.CurrentCover;
                    }

                    if (runtime.Memory.HasLineOfSight && runtime.Memory.TargetUserId != 0)
                    {
                        board.AnyMemberHasLineOfSight = true;
                        board.MembersWithLineOfSight++;
                        board.SharedEnemyUserId = runtime.Memory.TargetUserId;
                        board.SharedEnemyPosition = runtime.Memory.LastSeenPosition;
                        board.SharedEnemyKnownAt = runtime.Memory.LastSeenAt;
                    }

                    if (TryBuildEnemyMemory(runtime, Time.realtimeSinceStartup, out var memory))
                    {
                        board.KnownEnemies[memory.UserId] = memory;

                        if (memory.LastKnownAt >= board.SharedEnemyKnownAt)
                        {
                            board.SharedEnemyUserId = memory.UserId;
                            board.SharedEnemyPosition = memory.LastKnownPosition;
                            board.SharedEnemyKnownAt = memory.LastKnownAt;
                        }
                    }
                }

                board.TeamCenter /= members.Count;
            }
        }

        private string SquadRoleFor(int index, int teamSize)
        {
            if (teamSize <= 1)
            {
                return "solo";
            }

            if (index == 0)
            {
                return "anchor";
            }

            if (index == 1)
            {
                return "flanker";
            }

            return "pusher";
        }

        private void StartNameplateTimerIfEnabled()
        {
            nameplateTimer?.Destroy();
            nameplateTimer = null;

            if (config?.Debug?.DebugBotNameplates != true && config?.Debug?.DebugBotSidePanel != true)
            {
                DestroyDebugBotPanels();
                return;
            }

            nameplateTimer = timer.Every(config.Debug.DebugNameplateRefreshSeconds, DrawDebugBotNameplates);
        }

        private void DrawDebugBotNameplates()
        {
            if (config?.Debug?.DebugBotNameplates != true && config?.Debug?.DebugBotSidePanel != true)
            {
                DestroyDebugBotPanels();
                return;
            }

            var viewers = BasePlayer.activePlayerList
                .Where(IsDebugUiViewer)
                .ToList();

            if (viewers.Count == 0)
            {
                return;
            }

            var duration = config.Debug.DebugNameplateDrawDurationSeconds;
            var maxDistance = config.Debug.DebugNameplateMaxDistance;
            var liveBots = activeBots
                .Where(entry => IsLiveBot(entry.Key) && entry.Value != null)
                .ToList();

            if (liveBots.Count == 0)
            {
                foreach (var viewer in viewers)
                {
                    DestroyDebugBotPanel(viewer);
                }

                return;
            }

            foreach (var viewer in viewers)
            {
                BaseCombatEntity closestBot = null;
                BotRuntime closestRuntime = null;
                var closestDistance = float.MaxValue;

                foreach (var entry in liveBots)
                {
                    var bot = entry.Key;
                    var runtime = entry.Value;
                    var distance = Vector3.Distance(viewer.transform.position, bot.transform.position);

                    if (distance < closestDistance)
                    {
                        closestBot = bot;
                        closestRuntime = runtime;
                        closestDistance = distance;
                    }

                    if (distance > maxDistance)
                    {
                        continue;
                    }

                    if (config.Debug.DebugBotNameplates != true)
                    {
                        continue;
                    }

                    var position = bot.transform.position + Vector3.up * config.Debug.DebugNameplateHeight;
                    var distanceLabel = distance.ToString("0", CultureInfo.InvariantCulture);
                    var text = $"<size={config.Debug.DebugNameplateFontSize}><color=#ffde59>{BotClanLabel(runtime)}</color> <color=#ffffff>{runtime.State} {distanceLabel}m</color></size>";
                    viewer.SendConsoleCommand("ddraw.text", duration, Color.yellow, position, text);
                }

                if (config.Debug.DebugBotSidePanel == true && closestBot != null)
                {
                    DrawDebugBotSidePanel(viewer, closestBot, closestRuntime, closestDistance, Time.realtimeSinceStartup);
                }
                else
                {
                    DestroyDebugBotPanel(viewer);
                }
            }
        }

        private bool IsDebugUiViewer(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return false;
            }

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, AdminPermission))
            {
                return true;
            }

            var anchor = config?.Spawn?.NearPlayerAnchorNameOrSteamId;

            return config?.Debug?.DebugUiIncludesAnchorPlayer == true
                && !string.IsNullOrWhiteSpace(anchor)
                && PlayerMatchesQuery(player, anchor);
        }

        private int DebugUiViewerCount()
        {
            return BasePlayer.activePlayerList.Count(IsDebugUiViewer);
        }

        private void DrawDebugBotSidePanel(BasePlayer viewer, BaseCombatEntity bot, BotRuntime runtime, float distance, float now)
        {
            if (viewer == null || runtime == null || bot == null)
            {
                return;
            }

            DestroyDebugBotPanel(viewer);

            var container = new CuiElementContainer();
            var panel = container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0.03 0.04 0.05 0.76" },
                RectTransform = { AnchorMin = "0.755 0.52", AnchorMax = "0.995 0.92" }
            }, "Hud", DebugBotPanelUi);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = BuildDebugSidePanelText(bot, runtime, distance, now),
                    FontSize = 12,
                    Align = TextAnchor.UpperLeft,
                    Color = "0.92 0.96 1 1"
                },
                RectTransform = { AnchorMin = "0.04 0.04", AnchorMax = "0.96 0.96" }
            }, panel);

            CuiHelper.AddUi(viewer, container);
        }

        private void DestroyDebugBotPanel(BasePlayer player)
        {
            if (player != null)
            {
                CuiHelper.DestroyUi(player, DebugBotPanelUi);
            }
        }

        private void DestroyDebugBotPanels()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyDebugBotPanel(player);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyDebugBotPanel(player);
        }

        private string BuildDebugSidePanelText(BaseCombatEntity bot, BotRuntime runtime, float distance, float now)
        {
            RefreshCombatProfile(bot, runtime);

            var signal = NameplateSignal(runtime, now);
            var action = NameplateAction(runtime, now);
            var movement = NameplateMovement(bot, runtime);
            var weapon = string.IsNullOrWhiteSpace(runtime.Combat.WeaponShortname) ? runtime.Combat.WeaponClass : runtime.Combat.WeaponShortname;
            var stats = EnsureBotStats(runtime);
            var kd = stats.deaths <= 0 ? stats.kills.ToString(CultureInfo.InvariantCulture) : (stats.kills / (float)Math.Max(1, stats.deaths)).ToString("0.00", CultureInfo.InvariantCulture);
            var health = bot.Health().ToString("0", CultureInfo.InvariantCulture);
            var maxHealth = Math.Max(1f, runtime.Skill.Health).ToString("0", CultureInfo.InvariantCulture);
            var ammo = AmmoFraction(bot).ToString("0.00", CultureInfo.InvariantCulture);
            var target = runtime.Memory.Target == null ? BotTargetStatus(bot, runtime) : PlayerName(runtime.Memory.Target);
            var advisor = string.IsNullOrWhiteSpace(runtime.Decisions.LastAdvisorStatus) ? "none" : runtime.Decisions.LastAdvisorStatus;
            var fallback = string.IsNullOrWhiteSpace(runtime.Decisions.LastFallbackReason) ? "none" : runtime.Decisions.LastFallbackReason;
            var coverStatus = CoverStatus(bot, runtime);
            var medicalStatus = MedicalStatus(bot, runtime, now);
            CleanupBotPlacedEntityRefs();

            return "<b>Closest Raidlands Bot</b>"
                + $"\n<color=#ffde59>{runtime.DisplayName}</color>  {distance.ToString("0", CultureInfo.InvariantCulture)}m"
                + $"\nState: {runtime.State}  Prev: {runtime.PreviousState}"
                + $"\nSignal: {signal}  Target: {target}"
                + $"\nAction: {action}"
                + $"\nLOS: {(runtime.Memory.HasLineOfSight ? "Y" : "N")}  Exposure: {runtime.Memory.TargetExposureFraction:0.00} ({runtime.Memory.TargetVisibleProbePoints}/{runtime.Memory.TargetTotalProbePoints})"
                + $"\nSkill: {runtime.SkillTier}  Kit: {runtime.KitName}"
                + $"\nHP: {health}/{maxHealth}  Weapon: {weapon}  Ammo: {ammo}"
                + $"\nClan: {BotClanLabel(runtime)}"
                + $"\nK/D: {stats.kills}/{stats.deaths} ({kd})  Team: {runtime.TeamId}  Role: {runtime.SquadRole}"
                + $"\nBase: {(runtime.IsInBaseRestrictedArea ? "inside" : "clear")}  Barricades: {botPlacedEntities.Count}/{config.AI.MaxActiveBotBarricades}"
                + $"\nCover: {coverStatus}  Wall: {(string.IsNullOrWhiteSpace(runtime.LastBarricadeReason) ? "none" : runtime.LastBarricadeReason)}"
                + $"\nHeal: {medicalStatus}"
                + $"\nFire: {(string.IsNullOrWhiteSpace(runtime.LastFireBlockReason) ? "none" : runtime.LastFireBlockReason)}"
                + $"\nSight: {(string.IsNullOrWhiteSpace(runtime.LastSightReason) ? "none" : runtime.LastSightReason)}"
                + $"\nMove: {movement}  Shooting: {(runtime.IsShooting ? "Y" : "N")}"
                + $"\nFailed paths: {runtime.ConsecutiveFailedPaths}  Advisor: {advisor}"
                + $"\nFallback: {fallback}";
        }

        private string NameplateSignal(BotRuntime runtime, float now)
        {
            if (runtime == null)
            {
                return "sig=none";
            }

            if (runtime.Memory.HasLineOfSight && runtime.Memory.Target != null)
            {
                return $"sig=visible {SecondsAgo(runtime.Memory.LastLineOfSightAt, now)}";
            }

            if (runtime.Memory.LastSeenAt > 0f && now - runtime.Memory.LastSeenAt <= config.AI.SearchLastSeenSeconds)
            {
                return $"sig=last_seen {SecondsAgo(runtime.Memory.LastSeenAt, now)}";
            }

            if (runtime.Memory.LastHeardAt > 0f && now - runtime.Memory.LastHeardAt <= config.AI.TargetMemorySeconds)
            {
                return $"sig=heard {SecondsAgo(runtime.Memory.LastHeardAt, now)}";
            }

            if (runtime.Memory.LastDamagedAt > 0f && now - runtime.Memory.LastDamagedAt <= config.AI.TargetMemorySeconds)
            {
                return $"sig=damaged {SecondsAgo(runtime.Memory.LastDamagedAt, now)}";
            }

            return "sig=none";
        }

        private string NameplateAction(BotRuntime runtime, float now)
        {
            if (runtime == null || runtime.Decisions.LastActionId == TacticalActionId.None)
            {
                return "none";
            }

            var age = runtime.Decisions.LastDecisionAt > 0f ? $" {SecondsAgo(runtime.Decisions.LastDecisionAt, now)}" : "";
            return $"{ActionIdString(runtime.Decisions.LastActionId)}{age}";
        }

        private string NameplateMovement(BaseCombatEntity bot, BotRuntime runtime)
        {
            if (bot == null || runtime == null)
            {
                return "move=none";
            }

            var destination = runtime.CurrentDestination == Vector3.zero
                ? "dest=none"
                : $"dest={Vector3.Distance(bot.transform.position, runtime.CurrentDestination).ToString("0", CultureInfo.InvariantCulture)}m";
            var cover = runtime.CurrentCover == Vector3.zero
                ? "cover=none"
                : $"cover={Vector3.Distance(bot.transform.position, runtime.CurrentCover).ToString("0", CultureInfo.InvariantCulture)}m";
            var stuck = runtime.Movement.IsStuck ? "stuck=Y" : "stuck=N";
            return $"{destination} | {cover} | {stuck}";
        }

        private string SecondsAgo(float timestamp, float now)
        {
            if (timestamp <= 0f)
            {
                return "n/a";
            }

            return $"{Math.Max(0f, now - timestamp).ToString("0", CultureInfo.InvariantCulture)}s";
        }

        private void UpdatePerception(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            var visible = FindBestVisibleTarget(bot, runtime, out var visibility);

            if (visible != null)
            {
                var switched = runtime.Memory.TargetUserId != visible.userID;
                runtime.Memory.Target = visible;
                runtime.Memory.TargetUserId = visible.userID;
                runtime.Memory.HasLineOfSight = true;
                runtime.Memory.LastLineOfSightAt = now;
                runtime.Memory.TargetExposureFraction = visibility.ExposedFraction;
                runtime.Memory.TargetVisibleProbePoints = visibility.VisibleProbePoints;
                runtime.Memory.TargetTotalProbePoints = visibility.TotalProbePoints;
                runtime.LastSightReason = DescribeVisionResult(visibility);
                runtime.Memory.LastSeenPosition = visible.transform.position;
                runtime.Memory.LastSeenAt = now;
                runtime.Memory.TargetConfidence = Mathf.Clamp(0.55f + visibility.ExposedFraction * 0.45f, 0f, 1f);
                runtime.Memory.ThreatScore = Mathf.Clamp01((1f - Vector3.Distance(bot.transform.position, visible.transform.position) / Math.Max(1f, config.AI.VisionRange)) * 0.65f + visibility.ExposedFraction * 0.35f);

                if (switched)
                {
                    runtime.Memory.LastTargetSwitchAt = now;
                    runtime.NextReactionAllowedAt = now + UnityEngine.Random.Range(runtime.Skill.ReactionMinSeconds, runtime.Skill.ReactionMaxSeconds);
                }

                if (config.Debug.DebugPerception)
                {
                    Puts($"{runtime.DisplayName} sees {PlayerName(visible)} exposure={visibility.ExposedFraction:0.00} probes={visibility.VisibleProbePoints}/{visibility.TotalProbePoints} at {FormatVector(visible.transform.position)}.");
                }

                if (runtime.IsShooting && !ShouldFireAtTarget(bot, runtime, visible, now, true))
                {
                    StopBotAttack(bot, runtime);
                }
                else if (!runtime.IsShooting && now >= runtime.NextReactionAllowedAt && ShouldFireAtTarget(bot, runtime, visible, now, true))
                {
                    StartBotAttack(bot, runtime, visible);
                }

                return;
            }

            runtime.Memory.HasLineOfSight = false;
            runtime.Memory.TargetExposureFraction = 0f;
            runtime.Memory.TargetVisibleProbePoints = visibility?.VisibleProbePoints ?? 0;
            runtime.Memory.TargetTotalProbePoints = visibility?.TotalProbePoints ?? 0;
            runtime.LastSightReason = DescribeVisionResult(visibility);

            if (runtime.IsShooting && config.AI.RequireLineOfSightToShoot)
            {
                StopBotAttack(bot, runtime);
            }
            else if (!runtime.IsShooting && runtime.Memory.TargetUserId != 0)
            {
                runtime.LastFireBlockReason = "no_visible_target";
            }

            var secondsSinceSeen = runtime.Memory.LastSeenAt <= 0f ? float.MaxValue : now - runtime.Memory.LastSeenAt;
            var secondsSinceHeard = runtime.Memory.LastHeardAt <= 0f ? float.MaxValue : now - runtime.Memory.LastHeardAt;
            var secondsSinceDamaged = runtime.Memory.LastDamagedAt <= 0f ? float.MaxValue : now - runtime.Memory.LastDamagedAt;

            if (runtime.Memory.Target != null && secondsSinceSeen > config.AI.TargetMemorySeconds)
            {
                runtime.Memory.Target = null;
                runtime.Memory.TargetConfidence = Math.Min(runtime.Memory.TargetConfidence, 0.5f);
            }

            if (runtime.Memory.TargetUserId != 0
                && secondsSinceSeen > config.AI.SearchLastSeenSeconds
                && secondsSinceHeard > config.AI.SquadContactCommitmentSeconds
                && secondsSinceDamaged > config.AI.SquadContactCommitmentSeconds)
            {
                runtime.Memory.TargetUserId = 0;
                runtime.Memory.TargetConfidence = 0f;
            }
        }

        private BasePlayer FindBestVisibleTarget(BaseCombatEntity bot, BotRuntime runtime, out VisionResult bestVisibility)
        {
            BasePlayer best = null;
            bestVisibility = null;
            var bestScore = float.MinValue;
            var bestFailedVisibility = (VisionResult)null;
            var bestFailedScore = float.MinValue;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!IsCandidateTarget(bot, player))
                {
                    continue;
                }

                var isKnownThreat = runtime.Memory.TargetUserId == player.userID
                    || runtime.Memory.LastDamageSourcePlayer == player
                    || (runtime.Memory.LastDamageSourcePlayer != null && runtime.Memory.LastDamageSourcePlayer.userID == player.userID);

                if (!isKnownThreat && !IsInVisionCone(bot, player))
                {
                    continue;
                }

                var visibility = TargetVisibility(bot, player, config.AI.MinimumExposedTargetFraction);
                var distance = Vector3.Distance(bot.transform.position, player.transform.position);

                if (!visibility.CanSee)
                {
                    var failedScore = visibility.ExposedFraction * 1000f - distance;

                    if (failedScore > bestFailedScore)
                    {
                        bestFailedVisibility = visibility;
                        bestFailedScore = failedScore;
                    }

                    continue;
                }

                var score = visibility.ExposedFraction * 1000f - distance;

                if (score <= bestScore)
                {
                    continue;
                }

                best = player;
                bestVisibility = visibility;
                bestScore = score;
            }

            if (best == null)
            {
                bestVisibility = bestFailedVisibility;
            }

            return best;
        }

        private bool IsCandidateTarget(BaseCombatEntity bot, BasePlayer player)
        {
            if (!IsRealPlayer(player) || !player.IsConnected || player.IsDead() || player.IsSleeping() || ShouldIgnoreSafeZonePlayer(player))
            {
                return false;
            }

            return Vector3.Distance(bot.transform.position, player.transform.position) <= config.AI.VisionRange;
        }

        private bool IsInVisionCone(BaseCombatEntity bot, BasePlayer player)
        {
            var distance = Vector3.Distance(bot.transform.position, player.transform.position);

            if (distance <= config.AI.CloseAwarenessRadius)
            {
                return true;
            }

            var direction = player.transform.position - bot.transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude <= 0.01f)
            {
                return true;
            }

            return Vector3.Angle(bot.transform.forward, direction.normalized) <= config.AI.VisionFovDegrees * 0.5f;
        }

        private bool HasLineOfSight(BaseCombatEntity bot, BasePlayer player)
        {
            return TargetVisibility(bot, player, config.AI.MinimumExposedTargetFraction).CanSee;
        }

        private VisionResult TargetVisibility(BaseCombatEntity bot, BasePlayer player, float requiredExposedFraction)
        {
            var result = new VisionResult();

            if (bot == null || player == null || IsInvalidRuntimePosition(bot))
            {
                return result;
            }

            var from = EyePosition(bot);
            var points = TargetProbePoints(player);
            result.TotalProbePoints = points.Count;

            if (points.Count == 0)
            {
                return result;
            }

            foreach (var point in points)
            {
                if (!IsTargetSightLineClear(bot, player, from, point, out var blockReason, out var foliageHits))
                {
                    if (string.Equals(blockReason, "foliage", StringComparison.OrdinalIgnoreCase))
                    {
                        result.FoliageBlockedProbePoints++;
                        result.FoliageBlockerHits += foliageHits;
                    }
                    else
                    {
                        result.SolidBlockedProbePoints++;
                    }

                    continue;
                }

                result.VisibleProbePoints++;

                if (result.BestVisiblePoint == Vector3.zero)
                {
                    result.BestVisiblePoint = point;
                }
            }

            result.ExposedFraction = result.TotalProbePoints <= 0 ? 0f : result.VisibleProbePoints / (float)result.TotalProbePoints;
            var distance = Vector3.Distance(bot.transform.position, player.transform.position);
            var minimum = distance <= config.AI.CloseAwarenessRadius
                ? Math.Min(requiredExposedFraction, 0.25f)
                : requiredExposedFraction;
            result.CanSee = result.VisibleProbePoints > 0 && result.ExposedFraction >= minimum;
            result.BlockReason = result.CanSee
                ? "visible"
                : result.VisibleProbePoints > 0
                    ? $"exposure {result.VisibleProbePoints}/{result.TotalProbePoints}"
                    : result.FoliageBlockedProbePoints > 0
                        ? $"foliage {result.FoliageBlockedProbePoints}/{result.TotalProbePoints} hits={result.FoliageBlockerHits}"
                        : result.SolidBlockedProbePoints > 0
                            ? $"solid {result.SolidBlockedProbePoints}/{result.TotalProbePoints}"
                            : "no_probe_clear";
            return result;
        }

        private string DescribeVisionResult(VisionResult visibility)
        {
            if (visibility == null || visibility.TotalProbePoints <= 0)
            {
                return "no_candidate";
            }

            var reason = string.IsNullOrWhiteSpace(visibility.BlockReason) ? "unknown" : visibility.BlockReason;
            return $"{reason} exp={visibility.ExposedFraction.ToString("0.00", CultureInfo.InvariantCulture)} ({visibility.VisibleProbePoints}/{visibility.TotalProbePoints})";
        }

        private List<Vector3> TargetProbePoints(BasePlayer player)
        {
            var origin = player.transform.position;
            var right = player.transform.right;

            return new List<Vector3>
            {
                EyePosition(player),
                origin + Vector3.up * 1.45f,
                origin + Vector3.up * 1.15f,
                origin + Vector3.up * 0.85f,
                origin + Vector3.up * 1.25f + right * 0.28f,
                origin + Vector3.up * 1.25f - right * 0.28f
            };
        }

        private bool IsTargetSightLineClear(BaseCombatEntity bot, BasePlayer player, Vector3 from, Vector3 to)
        {
            return IsTargetSightLineClear(bot, player, from, to, out _, out _);
        }

        private bool IsTargetSightLineClear(BaseCombatEntity bot, BasePlayer player, Vector3 from, Vector3 to, out string blockReason, out int foliageHits)
        {
            blockReason = "clear";
            foliageHits = 0;
            var mask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed", "Default", "Tree", "Resource");
            var directLineClear = true;

            if (Physics.Linecast(from, to, out var hit, mask, QueryTriggerInteraction.Ignore))
            {
                var hitEntity = hit.GetEntity();

                if (hitEntity == null || (hitEntity != player && hitEntity != bot))
                {
                    blockReason = "solid";
                    directLineClear = false;
                }
            }

            if (!directLineClear)
            {
                return false;
            }

            if (IsVisionConcealedByFoliage(bot, player, from, to, out foliageHits))
            {
                blockReason = "foliage";
                return false;
            }

            return true;
        }

        private bool IsVisionConcealedByFoliage(BaseCombatEntity bot, BasePlayer player, Vector3 from, Vector3 to)
        {
            return IsVisionConcealedByFoliage(bot, player, from, to, out _);
        }

        private bool IsVisionConcealedByFoliage(BaseCombatEntity bot, BasePlayer player, Vector3 from, Vector3 to, out int blockerHits)
        {
            blockerHits = 0;

            if (config?.AI?.FoliageBlocksVision != true)
            {
                return false;
            }

            var delta = to - from;
            var distance = delta.magnitude;

            if (distance <= config.AI.MaximumClearVisionThroughFoliage || distance <= 0.1f)
            {
                return false;
            }

            var blockers = 0;
            var mask = FoliageVisionMask();

            if (mask != 0)
            {
                var hits = Physics.SphereCastAll(from, config.AI.FoliageVisionCheckRadius, delta.normalized, distance, mask, QueryTriggerInteraction.Ignore);
                var seenColliders = new HashSet<int>();

                foreach (var foliageHit in hits)
                {
                    if (foliageHit.collider != null && !seenColliders.Add(foliageHit.collider.GetInstanceID()))
                    {
                        continue;
                    }

                    if (!IsFoliageVisionBlocker(foliageHit, bot, player))
                    {
                        continue;
                    }

                    blockers++;
                    blockerHits = blockers;

                    if (blockers >= config.AI.FoliageHitsToBlockVision)
                    {
                        return true;
                    }
                }
            }

            var terrainBlockers = FoliageTerrainSampleHits(from, to, distance);
            blockerHits = blockers + terrainBlockers;
            return terrainBlockers >= config.AI.FoliageTerrainSamplesToBlockVision;
        }

        private int FoliageVisionMask()
        {
            var mask = 0;

            foreach (var layerName in config?.AI?.FoliageOccluderLayerNames ?? new List<string>())
            {
                var layer = LayerMask.NameToLayer(layerName);

                if (layer >= 0)
                {
                    mask |= 1 << layer;
                }
            }

            return mask;
        }

        private int FoliageTerrainSampleHits(Vector3 from, Vector3 to, float distance)
        {
            if (!config.AI.FoliageTerrainSampling || TerrainMeta.SplatMap == null)
            {
                return 0;
            }

            var step = Math.Max(3f, config.AI.FoliageTerrainSampleStep);
            var firstSampleDistance = Math.Max(config.AI.MaximumClearVisionThroughFoliage, step);
            var sampleCount = Mathf.Min(48, Mathf.FloorToInt((distance - firstSampleDistance) / step));

            if (sampleCount <= 0)
            {
                return 0;
            }

            var direction = (to - from).normalized;
            var hits = 0;

            for (var i = 0; i < sampleCount; i++)
            {
                var sampleDistance = firstSampleDistance + step * (i + 1);

                if (sampleDistance >= distance - 1f)
                {
                    break;
                }

                var sample = from + direction * sampleDistance;
                sample.y = TerrainHeight(sample);

                if (!IsForestSplat(sample))
                {
                    continue;
                }

                hits++;

                if (hits >= config.AI.FoliageTerrainSamplesToBlockVision)
                {
                    return hits;
                }
            }

            return hits;
        }

        private bool IsForestSplat(Vector3 position)
        {
            try
            {
                return ((int)TerrainMeta.SplatMap.GetSplatMaxType(position) & ForestSplatMask) != 0;
            }
            catch
            {
                return false;
            }
        }

        private void BroadcastPlayerSound(BasePlayer source, Vector3 sourcePosition, float range, string soundType, float baseConfidence, float throttleSeconds)
        {
            if (!config.AI.AllowHearing || !IsRealPlayer(source) || range <= 0f || sourcePosition == Vector3.zero)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;

            if (ShouldThrottleSound(source, soundType, now, throttleSeconds))
            {
                return;
            }

            foreach (var entry in activeBots.ToList())
            {
                var bot = entry.Key;
                var runtime = entry.Value;

                if (!IsLiveBot(bot) || runtime == null || IsInvalidRuntimePosition(bot))
                {
                    continue;
                }

                var distance = Vector3.Distance(bot.transform.position, sourcePosition);

                if (distance > range)
                {
                    continue;
                }

                var sameTarget = runtime.Memory.TargetUserId == source.userID;
                var confidence = SoundConfidence(distance, range, baseConfidence);

                if (!sameTarget && HasFreshVisibleDifferentTarget(runtime, source.userID, now))
                {
                    continue;
                }

                if (!sameTarget)
                {
                    runtime.Memory.Target = null;
                }

                runtime.Memory.TargetUserId = source.userID;
                runtime.Memory.LastHeardPosition = sourcePosition;
                runtime.Memory.LastHeardAt = now;
                runtime.Memory.TargetConfidence = Math.Max(runtime.Memory.TargetConfidence, confidence);
                runtime.NextDecisionAt = Math.Min(runtime.NextDecisionAt, now);

                if (ShouldCommandSoundInvestigation(runtime, source.userID, now))
                {
                    var destination = SoundInvestigationDestination(bot.transform.position, sourcePosition);
                    var destinationChanged = runtime.CurrentDestination == Vector3.zero
                        || Vector3.Distance(runtime.CurrentDestination, destination) > 6f;

                    if (destinationChanged || now - runtime.LastSoundInvestigateCommandAt >= config.AI.SoundInvestigationCommandCooldownSeconds)
                    {
                        runtime.LastSoundInvestigateCommandAt = now;
                        runtime.CurrentDestination = destination;
                        SetState(runtime, TacticalState.InvestigateSound, now);
                        MoveBotTo(bot, runtime, destination, BaseNavigator.NavigationSpeed.Fast);
                    }

                    FacePosition(bot, sourcePosition);
                }

                if (config.Debug.DebugPerception && now - runtime.LastSoundDebugAt >= 1.5f)
                {
                    runtime.LastSoundDebugAt = now;
                    Puts($"{runtime.DisplayName} heard {soundType} from {PlayerName(source)} at {distance.ToString("0", CultureInfo.InvariantCulture)}m; investigating {FormatVector(sourcePosition)}.");
                }
            }
        }

        private bool ShouldThrottleSound(BasePlayer source, string soundType, float now, float throttleSeconds)
        {
            if (source == null || throttleSeconds <= 0f)
            {
                return false;
            }

            var key = $"{source.userID}:{soundType}";

            if (recentSoundBroadcasts.TryGetValue(key, out var lastAt) && now - lastAt < throttleSeconds)
            {
                return true;
            }

            recentSoundBroadcasts[key] = now;
            return false;
        }

        private bool HasFreshVisibleDifferentTarget(BotRuntime runtime, ulong sourceUserId, float now)
        {
            return runtime != null
                && runtime.Memory.HasLineOfSight
                && runtime.Memory.TargetUserId != 0
                && runtime.Memory.TargetUserId != sourceUserId
                && runtime.Memory.LastLineOfSightAt > 0f
                && now - runtime.Memory.LastLineOfSightAt <= 4f;
        }

        private bool ShouldCommandSoundInvestigation(BotRuntime runtime, ulong sourceUserId, float now)
        {
            if (runtime == null || runtime.IsShooting || HasFreshVisibleDifferentTarget(runtime, sourceUserId, now))
            {
                return false;
            }

            return runtime.State == TacticalState.Roam
                || runtime.State == TacticalState.InvestigateSound
                || runtime.State == TacticalState.SearchLastKnown
                || runtime.State == TacticalState.Regroup
                || !HasRecentContact(runtime, now);
        }

        private float SoundConfidence(float distance, float range, float baseConfidence)
        {
            if (range <= 0f)
            {
                return 0f;
            }

            var closeness = 1f - Mathf.Clamp01(distance / range);
            return Mathf.Clamp01(baseConfidence * Mathf.Lerp(0.35f, 1f, closeness));
        }

        private Vector3 SoundInvestigationDestination(Vector3 origin, Vector3 sourcePosition)
        {
            var distance = Vector3.Distance(origin, sourcePosition);

            if (distance <= 55f)
            {
                return sourcePosition;
            }

            return MoveTowardPosition(origin, sourcePosition, Mathf.Clamp(distance * 0.65f, 35f, 85f));
        }

        private bool IsSuppressedWeapon(Item item)
        {
            var contents = item?.contents?.itemList;

            if (contents == null)
            {
                return false;
            }

            return contents.Any(attachment => string.Equals(attachment?.info?.shortname, "weapon.mod.silencer", StringComparison.OrdinalIgnoreCase));
        }

        private bool IsQuietProjectileWeapon(string shortname)
        {
            if (string.IsNullOrWhiteSpace(shortname))
            {
                return false;
            }

            return shortname.IndexOf("bow", StringComparison.OrdinalIgnoreCase) >= 0
                || shortname.IndexOf("crossbow", StringComparison.OrdinalIgnoreCase) >= 0
                || shortname.IndexOf("speargun", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsExplosionDamage(HitInfo info)
        {
            try
            {
                return info?.damageTypes?.GetMajorityDamageType() == Rust.DamageType.Explosion;
            }
            catch
            {
                return false;
            }
        }

        private Vector3 SoundPositionFromHit(HitInfo info, BasePlayer fallback)
        {
            var position = info?.HitPositionWorld ?? Vector3.zero;
            return position == Vector3.zero && fallback != null ? fallback.transform.position : position;
        }

        private bool IsFoliageVisionBlocker(RaycastHit hit, BaseCombatEntity bot, BasePlayer player)
        {
            if (hit.collider == null)
            {
                return false;
            }

            var entity = hit.GetEntity();

            if (entity != null && (entity == bot || entity == player))
            {
                return false;
            }

            if (entity is TreeEntity)
            {
                return true;
            }

            var layerName = LayerMask.LayerToName(hit.collider.gameObject.layer);

            if (string.Equals(layerName, "Tree", StringComparison.OrdinalIgnoreCase)
                || string.Equals(layerName, "Resource", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var colliderName = hit.collider.name ?? "";
            var entityName = entity == null ? "" : $"{entity.ShortPrefabName} {entity.PrefabName}";
            var text = $"{colliderName} {entityName}".ToLowerInvariant();

            return text.Contains("tree")
                || text.Contains("bush")
                || text.Contains("shrub")
                || text.Contains("foliage")
                || text.Contains("plant")
                || text.Contains("forest")
                || text.Contains("jungle")
                || text.Contains("leaf")
                || text.Contains("leaves")
                || text.Contains("branch")
                || text.Contains("canopy")
                || text.Contains("palm")
                || text.Contains("fern")
                || text.Contains("vine")
                || text.Contains("bramble");
        }

        private Vector3 EyePosition(BaseEntity entity)
        {
            var player = entity as BasePlayer;

            if (player?.eyes != null)
            {
                return player.eyes.position;
            }

            return entity == null ? Vector3.zero : entity.transform.position + Vector3.up * 1.6f;
        }

        private DecisionRequest BuildDecisionRequest(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            var target = runtime.Memory.Target;
            var request = new DecisionRequest
            {
                RequestId = $"{runtime.BotKey}-{now.ToString("0.000", CultureInfo.InvariantCulture)}",
                BotId = runtime.BotKey,
                TeamId = runtime.TeamId,
                ClanKey = runtime.ClanKey,
                ClanTag = runtime.ClanTag,
                State = runtime.State.ToString(),
                SkillTier = runtime.SkillTier,
                HealthFraction = Mathf.Clamp01(bot.Health() / Math.Max(1f, runtime.Skill.Health)),
                WeaponShortname = ActiveWeaponShortname(bot),
                AmmoFraction = AmmoFraction(bot),
                HasLineOfSight = runtime.Memory.HasLineOfSight,
                TargetExposureFraction = runtime.Memory.TargetExposureFraction,
                TargetConfidence = runtime.Memory.TargetConfidence,
                DistanceToTarget = target == null ? -1f : Vector3.Distance(bot.transform.position, target.transform.position),
                SecondsSinceLastSeen = runtime.Memory.LastSeenAt <= 0f ? 999f : now - runtime.Memory.LastSeenAt,
                SecondsSinceLastHeard = runtime.Memory.LastHeardAt <= 0f ? 999f : now - runtime.Memory.LastHeardAt,
                NearbyAllies = NearbyAllies(bot, runtime),
                NearbyKnownEnemies = NearbyKnownEnemies(runtime, now),
                IsStuck = IsBotStuck(bot, runtime, now),
                TargetIsInsideBaseRestrictedArea = target != null && IsBaseRestrictedPosition(target.transform.position)
            };

            return request;
        }

        private List<TacticalActionCandidate> BuildCandidateActions(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            var candidates = new List<TacticalActionCandidate>();
            var target = runtime.Memory.Target;
            var hasFreshSeen = runtime.Memory.LastSeenAt > 0f && now - runtime.Memory.LastSeenAt <= config.AI.SearchLastSeenSeconds;
            var soundMemorySeconds = Math.Min(config.AI.TargetMemorySeconds, config.AI.SoundInvestigationCommitmentSeconds);
            var hasFreshHeard = config.AI.AllowHearing && runtime.Memory.LastHeardAt > 0f && now - runtime.Memory.LastHeardAt <= soundMemorySeconds;
            var healthFraction = Mathf.Clamp01(bot.Health() / Math.Max(1f, runtime.Skill.Health));
            var board = SquadBoardFor(runtime);
            var squadHasFreshContact = SquadHasFreshContact(board, now);
            var hasRecentContact = HasRecentContact(runtime, now);
            var knownThreatPosition = KnownThreatPosition(runtime);
            var damageWallAware = HasDamageWallAwareness(runtime, now);
            var lowHealthAware = ShouldNoticeLowHealth(runtime, healthFraction, now);
            RefreshCombatProfile(bot, runtime);
            runtime.IsInBaseRestrictedArea = IsBaseRestrictedPosition(bot.transform.position);
            var nearCoverNow = IsAtCover(bot, runtime);
            var atCoverNow = IsEffectiveCover(bot, runtime, target);
            var coverCompromised = nearCoverNow
                && !atCoverNow
                && runtime.Memory.HasLineOfSight
                && runtime.Memory.TargetExposureFraction > config.AI.EffectiveCoverMaxExposedFraction;

            if (coverCompromised)
            {
                runtime.NextCoverSearchAt = 0f;
                runtime.BarricadeCommittedUntil = 0f;
            }

            var wallCommitActive = runtime.BarricadeCommittedUntil > now
                && runtime.CurrentBarricadePoint != Vector3.zero
                && nearCoverNow
                && !coverCompromised;

            var targetInBase = target != null && IsBaseRestrictedPosition(target.transform.position);
            var threatPathCrossesBase = knownThreatPosition != Vector3.zero
                && !runtime.IsInBaseRestrictedArea
                && SegmentCrossesBaseRestrictedArea(bot.transform.position, knownThreatPosition);

            if (runtime.Movement.IsStuck && (now >= runtime.NextStuckRecoveryAt || runtime.ConsecutiveFailedPaths >= 6))
            {
                var recovery = Candidate(TacticalActionId.RoamToPoint, runtime.ConsecutiveFailedPaths >= 12 ? 132f : 118f, "low", "stuck recovery alternate path", FindStuckRecoveryDestination(bot, runtime), runtime.Memory.TargetUserId, now);
                recovery.RiskFlags.Add("stuck_recovery");
                candidates.Add(recovery);
            }

            if (runtime.IsInBaseRestrictedArea && TryFindOutsideBaseHoldPoint(bot.transform.position, knownThreatPosition, out var escapePoint))
            {
                var escape = Candidate(TacticalActionId.HoldOutsideBase, 98f, "low", "bot is inside a base-restricted area; move back outside", escapePoint, runtime.Memory.TargetUserId, now);
                escape.RiskFlags.Add("base_avoidance");
                candidates.Add(escape);
            }

            if (board != null && board.TeamSize > 1 && !runtime.Memory.HasLineOfSight)
            {
                var distanceFromTeam = Vector3.Distance(bot.transform.position, board.TeamCenter);
                var regroupDistance = squadHasFreshContact
                    ? config.AI.SquadRegroupDistance * 1.75f
                    : config.AI.SquadRegroupDistance;

                if (distanceFromTeam > regroupDistance && TrySampleTacticalPosition(board.TeamCenter, Math.Max(8f, config.Spawn.NavmeshSampleDistance), out var regroupPoint))
                {
                    var regroupScore = squadHasFreshContact ? 50f : 70f;
                    candidates.Add(Candidate(TacticalActionId.RegroupWithSquad, regroupScore, "low", $"too far from clan center as {runtime.SquadRole}", regroupPoint, board.SharedEnemyUserId, now));
                }
            }

            if (config.AI.AllowBarricades && !atCoverNow && knownThreatPosition != Vector3.zero && now < runtime.NextBarricadeAt)
            {
                runtime.LastBarricadeReason = $"{(damageWallAware ? "queued" : "cooldown")} {Math.Max(0f, runtime.NextBarricadeAt - now).ToString("0", CultureInfo.InvariantCulture)}s";
            }

            if (config.AI.AllowBarricades
                && !atCoverNow
                && now >= runtime.NextBarricadeAt
                && knownThreatPosition != Vector3.zero
                && ShouldPlaceBarricade(bot, runtime, knownThreatPosition, healthFraction, runtime.Memory.TargetExposureFraction, now, out var reactionBarricadePoint))
            {
                var score = damageWallAware
                    ? 136f
                    : (lowHealthAware ? 130f : 78f + (1f - healthFraction) * 16f + runtime.Memory.TargetExposureFraction * 8f);
                var barricade = Candidate(TacticalActionId.PlaceBarricade, score, "medium", damageWallAware ? "took damage; quick-place real barricade cover" : "pressured in open ground; place real barricade cover", reactionBarricadePoint, runtime.Memory.TargetUserId, now);
                barricade.RiskFlags.Add("real_entity");
                barricade.RiskFlags.Add(damageWallAware ? "damage_wall" : "pressure_wall");
                candidates.Add(barricade);
            }

            if (lowHealthAware && config.AI.AllowCover && knownThreatPosition != Vector3.zero)
            {
                if (atCoverNow)
                {
                    var tuckDestination = runtime.CurrentTuckPoint == Vector3.zero ? runtime.CurrentCover : runtime.CurrentTuckPoint;
                    var canShootFromCover = target != null
                        && CanShootVisibleTarget(bot, runtime, Vector3.Distance(bot.transform.position, target.transform.position), runtime.Memory.TargetExposureFraction, now);

                    if (tuckDestination != Vector3.zero && !canShootFromCover)
                    {
                        var tuck = Candidate(TacticalActionId.Tuck, 132f, "low", "noticed low health; stay tucked and heal", tuckDestination, runtime.Memory.TargetUserId, now);
                        tuck.RiskFlags.Add("low_health_heal");
                        candidates.Add(tuck);
                    }
                }
                else
                {
                    var retreatDestination = FindRetreatPosition(bot.transform.position, knownThreatPosition);
                    var foundCoverDestination = false;
                    var hasCurrentCoverDestination = !coverCompromised
                        && runtime.CurrentTuckPoint != Vector3.zero
                        && (runtime.State == TacticalState.BarricadeHold
                            || runtime.State == TacticalState.FightFromCover
                            || runtime.CurrentBarricadePoint != Vector3.zero);

                    if (hasCurrentCoverDestination)
                    {
                        retreatDestination = runtime.CurrentTuckPoint;
                        foundCoverDestination = true;
                    }
                    else if (now >= runtime.NextCoverSearchAt && TryFindCoverPlan(bot, runtime, knownThreatPosition, target, out var lowHealthCover))
                    {
                        ApplyCoverPlan(runtime, lowHealthCover);
                        retreatDestination = lowHealthCover.CoverPoint;
                        foundCoverDestination = true;
                    }

                    var distanceToRetreatCover = foundCoverDestination
                        ? Vector3.Distance(bot.transform.position, retreatDestination)
                        : float.MaxValue;
                    var retreatCoverIsFar = distanceToRetreatCover > config.AI.RetreatWallCoverDistance;

                    if (config.AI.AllowBarricades
                        && retreatCoverIsFar
                        && now >= runtime.NextBarricadeAt
                        && candidates.All(candidate => candidate.ActionId != TacticalActionId.PlaceBarricade)
                        && ShouldPlaceBarricade(bot, runtime, knownThreatPosition, healthFraction, runtime.Memory.TargetExposureFraction, now, out var retreatWallPoint))
                    {
                        var wall = Candidate(TacticalActionId.PlaceBarricade, 148f + (1f - healthFraction) * 18f, "medium", "low-health retreat cover is too far; wall before crossing open ground", retreatWallPoint, runtime.Memory.TargetUserId, now);
                        wall.RiskFlags.Add("real_entity");
                        wall.RiskFlags.Add("retreat_wall");
                        candidates.Add(wall);
                    }

                    var retreatAge = runtime.State == TacticalState.Retreat ? now - runtime.StateEnteredAt : 0f;
                    var retreatScore = foundCoverDestination
                        ? 124f + (config.AI.LowHealthCoverThreshold - healthFraction) * 18f
                        : 92f + (config.AI.LowHealthCoverThreshold - healthFraction) * 12f;

                    if (!foundCoverDestination && retreatAge > RetreatFallbackReturnFireAfterSeconds)
                    {
                        retreatScore -= Mathf.Clamp((retreatAge - RetreatFallbackReturnFireAfterSeconds) * 6f, 0f, 28f);
                    }

                    var retreatReason = foundCoverDestination
                        ? "noticed low health; break contact to cover and heal"
                        : "noticed low health but no hard cover was found; fall back and reassess";
                    var retreat = Candidate(TacticalActionId.RetreatToCover, retreatScore, "low", retreatReason, retreatDestination, runtime.Memory.TargetUserId, now);
                    retreat.RiskFlags.Add("low_health_heal");
                    retreat.RiskFlags.Add(foundCoverDestination ? "cover_destination" : "fallback_retreat");
                    candidates.Add(retreat);
                }
            }

            if (config.AI.DoNotEnterBases && (targetInBase || threatPathCrossesBase) && (hasFreshSeen || hasFreshHeard || target != null))
            {
                if (TryFindOutsideBaseHoldPoint(bot.transform.position, knownThreatPosition, out var holdPoint))
                {
                    var hold = Candidate(TacticalActionId.HoldOutsideBase, 91f, "low", targetInBase ? "target is inside base-restricted area" : "path to target crosses base-restricted area", holdPoint, runtime.Memory.TargetUserId, now);
                    hold.RiskFlags.Add("base_avoidance");
                    candidates.Add(hold);
                }

                if (runtime.Memory.LastSeenAt > 0f && now - runtime.Memory.LastSeenAt > Math.Min(10f, config.AI.TargetMemorySeconds))
                {
                    candidates.Add(Candidate(TacticalActionId.AbandonTarget, 42f, "low", "target stayed inside base boundary long enough to give up", Vector3.zero, runtime.Memory.TargetUserId, now));
                }
            }
            else if (runtime.Memory.HasLineOfSight && target != null)
            {
                var distance = Vector3.Distance(bot.transform.position, target.transform.position);
                var rangeScore = WeaponRangeScore(runtime, distance);
                var exposure = runtime.Memory.TargetExposureFraction;
                var atCover = atCoverNow;
                var returnFireWhileExposed = lowHealthAware && !atCover && ShouldReturnFireWhileExposed(runtime, healthFraction, distance, exposure, now);
                var canShootVisibleTarget = CanShootVisibleTarget(bot, runtime, distance, exposure, now);

                if (canShootVisibleTarget)
                {
                    var shootScore = 36f + rangeScore * 44f + exposure * 16f + runtime.Skill.Courage * 8f;
                    var shootRisk = "safe";
                    var shootReason = $"visible target exposure {exposure:0.00}, {runtime.Combat.WeaponClass} range score {rangeScore:0.00}";

                    if (distance > runtime.Combat.IdealRange)
                    {
                        shootScore -= (1f - runtime.Skill.Courage) * 18f;
                    }

                    if (atCover)
                    {
                        shootScore += 8f;
                    }
                    if (wallCommitActive && distance <= runtime.Combat.MaxRange)
                    {
                        shootScore += 34f;
                        shootReason = "holding recent barricade position with visible target";
                    }
                    else if (lowHealthAware)
                    {
                        shootScore -= 12f;
                        shootRisk = "medium";
                        shootReason = "visible target while low health; keep weapon up while moving for cover";
                    }

                    if (returnFireWhileExposed)
                    {
                        shootScore = Math.Max(shootScore + 18f, 108f + runtime.Skill.Courage * 14f);
                        shootRisk = "medium";
                        shootReason = "low health with no immediate cover/wall answer; return fire instead of endless retreat";
                    }

                    var shoot = Candidate(TacticalActionId.AcquireVisibleTarget, shootScore, shootRisk, shootReason, target.transform.position, target.userID, now);

                    if (returnFireWhileExposed)
                    {
                        shoot.RiskFlags.Add("exposed_return_fire");
                    }

                    candidates.Add(shoot);
                }

                if (config.AI.AllowBarricades
                    && !atCover
                    && now >= runtime.NextBarricadeAt
                    && candidates.All(candidate => candidate.ActionId != TacticalActionId.PlaceBarricade)
                    && ShouldPlaceBarricade(bot, runtime, target.transform.position, healthFraction, exposure, now, out var barricadePoint))
                {
                    var barricade = Candidate(TacticalActionId.PlaceBarricade, damageWallAware ? 136f : 90f + (1f - healthFraction) * 18f + exposure * 8f, "medium", "damaged or exposed in open ground; place real barricade cover", barricadePoint, target.userID, now);
                    barricade.RiskFlags.Add("real_entity");
                    barricade.RiskFlags.Add(damageWallAware ? "damage_wall" : "pressure_wall");
                    candidates.Add(barricade);
                }

                if (config.AI.AllowCover)
                {
                    if (atCover)
                    {
                        if (runtime.IsShooting && now >= runtime.CurrentPeekUntil)
                        {
                            candidates.Add(Candidate(TacticalActionId.Tuck, 84f, "low", "peek window expired; tuck back into cover", runtime.CurrentTuckPoint == Vector3.zero ? runtime.CurrentCover : runtime.CurrentTuckPoint, target.userID, now));
                        }
                        else if (!runtime.IsShooting && now >= runtime.NextPeekAt && runtime.CurrentPeekPoint != Vector3.zero)
                        {
                            var peekAction = UnityEngine.Random.value < 0.5f ? TacticalActionId.PeekLeft : TacticalActionId.PeekRight;
                            candidates.Add(Candidate(peekAction, 66f + runtime.Skill.Aggression * 12f, "medium", "peek from current cover to re-check target", runtime.CurrentPeekPoint, target.userID, now));
                        }
                    }
                    else if (now >= runtime.NextCoverSearchAt && TryFindCoverPlan(bot, runtime, target.transform.position, target, out var coverPlan))
                    {
                        ApplyCoverPlan(runtime, coverPlan);
                        var coverScore = 64f + (1f - healthFraction) * 18f + (1f - exposure) * 12f;

                        if (distance > runtime.Combat.IdealRange)
                        {
                            coverScore += 8f;
                        }

                        candidates.Add(Candidate(TacticalActionId.MoveToCover, coverScore, "low", "visible target while bot is exposed", coverPlan.CoverPoint, target.userID, now));
                    }
                }

                if (distance > runtime.Combat.PreferredDistance)
                {
                    var pushScore = 56f + runtime.Skill.Aggression * 22f;

                    if (distance > runtime.Combat.IdealRange)
                    {
                        pushScore += 18f;
                    }

                    if (atCover)
                    {
                        pushScore -= 10f;
                    }

                    if (wallCommitActive && distance <= runtime.Combat.MaxRange)
                    {
                        pushScore -= 70f;
                    }

                    candidates.Add(Candidate(TacticalActionId.PushTarget, pushScore, "medium", $"target is outside {runtime.Combat.WeaponClass} preferred range", MoveTowardPosition(bot.transform.position, target.transform.position, runtime.Combat.PushDistance), target.userID, now));
                }

                if (config.AI.AllowFlanking
                    && board != null
                    && board.TeamSize > 1
                    && now >= runtime.NextFlankAt
                    && !(wallCommitActive && distance <= runtime.Combat.MaxRange)
                    && distance > Math.Max(12f, runtime.Combat.RetreatDistance + 4f)
                    && (runtime.SquadRole == "flanker" || runtime.SquadRole == "pusher"))
                {
                    var side = runtime.SquadRole == "flanker" ? 1f : -1f;
                    var score = (runtime.SquadRole == "flanker" ? 78f : 66f) + runtime.Skill.Aggression * 12f;

                    if (board.AnyMemberHasLineOfSight && !runtime.Memory.HasLineOfSight)
                    {
                        score += 10f;
                    }

                    if (TryFindFlankPosition(bot.transform.position, target.transform.position, side, out var flankPoint))
                    {
                        candidates.Add(Candidate(side > 0f ? TacticalActionId.FlankLeft : TacticalActionId.FlankRight, score, "medium", $"squad {runtime.SquadRole} flank toward shared fight", flankPoint, target.userID, now));
                    }
                }
            }

            if (!runtime.Memory.HasLineOfSight && TryGetSharedEnemyMemory(runtime, now, out var sharedEnemy))
            {
                var sharedSearchScore = 68f + runtime.Skill.Aggression * 10f;

                if (squadHasFreshContact)
                {
                    sharedSearchScore += 10f;
                }

                if (board?.AnyMemberHasLineOfSight == true)
                {
                    sharedSearchScore += 8f;
                }

                if (runtime.SquadRole == "pusher")
                {
                    sharedSearchScore += 8f;
                }
                else if (runtime.SquadRole == "flanker")
                {
                    sharedSearchScore += 6f;
                }
                else if (runtime.SquadRole == "anchor")
                {
                    sharedSearchScore += 2f;
                }

                candidates.Add(Candidate(TacticalActionId.SearchLastKnown, sharedSearchScore, "medium", $"clan shared {sharedEnemy.Source} contact as {runtime.SquadRole}", sharedEnemy.LastKnownPosition, sharedEnemy.UserId, now));

                if (config.AI.AllowFlanking
                    && now >= runtime.NextFlankAt
                    && (runtime.SquadRole == "flanker" || runtime.SquadRole == "pusher")
                    && TryFindFlankPosition(bot.transform.position, sharedEnemy.LastKnownPosition, runtime.SquadRole == "flanker" ? 1f : -1f, out var sharedFlank))
                {
                    var sharedFlankScore = (runtime.SquadRole == "flanker" ? 74f : 70f) + runtime.Skill.Aggression * 10f;
                    candidates.Add(Candidate(runtime.SquadRole == "flanker" ? TacticalActionId.FlankLeft : TacticalActionId.FlankRight, sharedFlankScore, "medium", "flank toward clan shared last-known position without shooting", sharedFlank, sharedEnemy.UserId, now));
                }
            }

            if (!runtime.Memory.HasLineOfSight && hasFreshSeen)
            {
                var searchScore = hasRecentContact ? 80f : 66f;
                candidates.Add(Candidate(TacticalActionId.SearchLastKnown, searchScore, "medium", "target was recently seen but line of sight is lost", runtime.Memory.LastSeenPosition, runtime.Memory.TargetUserId, now));
            }

            if (!runtime.Memory.HasLineOfSight && hasFreshHeard)
            {
                var soundAge = now - runtime.Memory.LastHeardAt;
                var soundFreshness = soundMemorySeconds <= 0f ? 0f : 1f - Mathf.Clamp01(soundAge / soundMemorySeconds);
                var investigateScore = (hasRecentContact ? 98f : 86f) + soundFreshness * 12f + runtime.Skill.Aggression * 8f;
                var investigateDestination = SoundInvestigationDestination(bot.transform.position, runtime.Memory.LastHeardPosition);
                candidates.Add(Candidate(TacticalActionId.InvestigateSound, investigateScore, "medium", "fresh sound stimulus without visual contact", investigateDestination, runtime.Memory.TargetUserId, now));
            }

            if (!lowHealthAware && healthFraction < 0.35f && (hasFreshSeen || hasFreshHeard))
            {
                var awayFrom = runtime.Memory.LastSeenAt >= runtime.Memory.LastHeardAt ? runtime.Memory.LastSeenPosition : runtime.Memory.LastHeardPosition;
                var retreatDestination = FindRetreatPosition(bot.transform.position, awayFrom);

                if (config.AI.AllowCover && now >= runtime.NextCoverSearchAt && TryFindCoverPlan(bot, runtime, awayFrom, target, out var retreatCover))
                {
                    ApplyCoverPlan(runtime, retreatCover);
                    retreatDestination = retreatCover.CoverPoint;
                }

                candidates.Add(Candidate(TacticalActionId.RetreatToCover, 88f, "low", "critical health panic while threat is known", retreatDestination, runtime.Memory.TargetUserId, now));
            }

            if (runtime.CurrentDestination == Vector3.zero || Vector3.Distance(bot.transform.position, runtime.CurrentDestination) < 4f)
            {
                runtime.CurrentDestination = FindRoamDestination(runtime.HomePosition);
            }

            candidates.Add(Candidate(TacticalActionId.RoamToPoint, 15f, "low", "no higher-priority tactical stimulus", runtime.CurrentDestination, 0, now));
            return candidates
                .OrderByDescending(candidate => candidate.HeuristicScore)
                .Take(Math.Max(1, config.DecisionAdvisor.MaxCandidateActions))
                .ToList();
        }

        private bool CanShootVisibleTarget(BaseCombatEntity bot, BotRuntime runtime, float distance, float exposure, float now)
        {
            return bot != null
                && runtime != null
                && runtime.Memory.HasLineOfSight
                && !IsMedicalFireLocked(runtime, now)
                && HasAmmoToShoot(bot)
                && distance <= runtime.Combat.MaxRange
                && exposure >= config.AI.MinimumExposedTargetFractionToShoot;
        }

        private bool ShouldReturnFireWhileExposed(BotRuntime runtime, float healthFraction, float distance, float exposure, float now)
        {
            if (runtime == null || !runtime.Memory.HasLineOfSight)
            {
                return false;
            }

            if (distance > runtime.Combat.MaxRange || exposure < config.AI.MinimumExposedTargetFractionToShoot)
            {
                return false;
            }

            var recentlyDamaged = runtime.LastDamageTakenAt > 0f && now - runtime.LastDamageTakenAt <= Math.Max(3f, config.AI.DamageWallReactionWindowSeconds * 0.5f);
            var retreatAge = runtime.State == TacticalState.Retreat ? now - runtime.StateEnteredAt : 0f;
            var wallUnavailable = !config.AI.AllowBarricades
                || config.AI.MaxActiveBotBarricades <= 0
                || botPlacedEntities.Count >= config.AI.MaxActiveBotBarricades
                || now < runtime.NextBarricadeAt
                || string.Equals(runtime.LastBarricadeReason, "cap_reached", StringComparison.OrdinalIgnoreCase)
                || string.Equals(runtime.LastBarricadeReason, "no_clear_spot", StringComparison.OrdinalIgnoreCase)
                || string.Equals(runtime.LastBarricadeReason, "spawn_blocked", StringComparison.OrdinalIgnoreCase);

            if (!recentlyDamaged && retreatAge < RetreatFallbackReturnFireAfterSeconds)
            {
                return false;
            }

            if (healthFraction <= config.AI.LowHealthCoverThreshold * 0.35f && !wallUnavailable && retreatAge < RetreatFallbackTimeoutSeconds)
            {
                return false;
            }

            return wallUnavailable
                || retreatAge >= RetreatFallbackReturnFireAfterSeconds
                || runtime.Movement.IsStuck
                || runtime.ConsecutiveFailedPaths > 0;
        }

        private TacticalActionCandidate Candidate(TacticalActionId actionId, float score, string risk, string reason, Vector3 destination, ulong targetUserId, float now)
        {
            return new TacticalActionCandidate
            {
                Id = ActionIdString(actionId),
                ActionId = actionId,
                HeuristicScore = score,
                Risk = risk,
                ReasonFromCode = reason,
                Destination = destination,
                TargetUserId = targetUserId,
                ExpiresAt = now + Math.Max(0.1f, config.DecisionAdvisor.DecisionTtlMilliseconds / 1000f)
            };
        }

        private TacticalDecision DecideOrFallback(BaseCombatEntity bot, BotRuntime runtime, DecisionRequest request, List<TacticalActionCandidate> candidates, float now)
        {
            var decision = new TacticalDecision();
            DecisionAdvisorResult advisorResult = null;

            if (ShouldAskAdvisor(runtime, candidates, now))
            {
                decision.AdvisorRequested = true;
                runtime.Decisions.LastAdvisorRequestAt = now;
                decisionAdvisor = decisionAdvisor ?? new NullDecisionAdvisor();
                decisionAdvisor.TrySubmit(request, result => advisorResult = result);
                decision.AdvisorStatus = advisorResult?.Status ?? "advisor_no_response";
                runtime.Decisions.LastAdvisorStatus = decision.AdvisorStatus;
            }

            decision.Selected = SelectFallbackCandidate(candidates);
            decision.FallbackReason = decision.AdvisorRequested ? decision.AdvisorStatus : "heuristic_only";
            runtime.Decisions.LastFallbackReason = decision.FallbackReason;

            if (config.DecisionAdvisor.LogDecisionTraces)
            {
                QueueDecisionTrace(new DecisionTrace
                {
                    request_id = request.RequestId,
                    bot_id = runtime.BotKey,
                    team_id = runtime.TeamId,
                    clan_key = runtime.ClanKey,
                    clan_tag = runtime.ClanTag,
                    state = runtime.State.ToString(),
                    advisor_requested = decision.AdvisorRequested,
                    advisor_status = decision.AdvisorStatus,
                    fallback_reason = decision.FallbackReason,
                    final_action = decision.Selected?.Id ?? "none",
                    final_score = decision.Selected?.HeuristicScore ?? 0f,
                    candidates = candidates,
                    created_at = now
                });
            }

            return decision;
        }

        private bool ShouldAskAdvisor(BotRuntime runtime, List<TacticalActionCandidate> candidates, float now)
        {
            if (config.DecisionAdvisor == null || !config.DecisionAdvisor.Enabled || candidates.Count <= 1)
            {
                return false;
            }

            if (now - runtime.Decisions.LastAdvisorRequestAt < config.DecisionAdvisor.MinSecondsBetweenRequestsPerBot)
            {
                return false;
            }

            return config.DecisionAdvisor.AskWhenActionScoresAreClose && AreTopScoresClose(candidates)
                || config.DecisionAdvisor.AskWhenBotIsStuck && runtime.ConsecutiveFailedPaths > 0
                || config.DecisionAdvisor.AskWhenPushRetreatOrFlankIsHighImpact && HasHighImpactCandidate(candidates);
        }

        private bool AreTopScoresClose(List<TacticalActionCandidate> candidates)
        {
            var ordered = candidates.OrderByDescending(candidate => candidate.HeuristicScore).Take(2).ToList();
            return ordered.Count == 2 && Math.Abs(ordered[0].HeuristicScore - ordered[1].HeuristicScore) <= 12f;
        }

        private bool HasHighImpactCandidate(List<TacticalActionCandidate> candidates)
        {
            return candidates.Any(candidate => candidate.ActionId == TacticalActionId.PushTarget
                || candidate.ActionId == TacticalActionId.RetreatToCover
                || candidate.ActionId == TacticalActionId.FlankLeft
                || candidate.ActionId == TacticalActionId.FlankRight
                || candidate.ActionId == TacticalActionId.PlaceBarricade
                || candidate.ActionId == TacticalActionId.HoldOutsideBase
                || candidate.ActionId == TacticalActionId.RegroupWithSquad);
        }

        private TacticalActionCandidate SelectFallbackCandidate(List<TacticalActionCandidate> candidates)
        {
            return candidates.OrderByDescending(candidate => candidate.HeuristicScore).FirstOrDefault();
        }

        private void ExecuteDecision(BaseCombatEntity bot, BotRuntime runtime, TacticalDecision decision, float now)
        {
            var selected = decision?.Selected;

            if (selected == null)
            {
                return;
            }

            ExecuteTacticalAction(bot, runtime, selected, now);
            runtime.Decisions.LastActionId = selected.ActionId;
            runtime.Decisions.LastDecisionAt = now;

            if (config.Debug.DebugTacticalDecisions)
            {
                Puts($"{runtime.DisplayName} {runtime.State} -> {selected.Id} score={selected.HeuristicScore:0.0} advisor={decision.AdvisorStatus} fallback={decision.FallbackReason}");
            }
        }

        private void ExecuteTacticalAction(BaseCombatEntity bot, BotRuntime runtime, TacticalActionCandidate action, float now)
        {
            switch (action.ActionId)
            {
                case TacticalActionId.AcquireVisibleTarget:
                    SetState(runtime, TacticalState.AcquireTarget, now);
                    FaceEntity(bot, runtime.Memory.Target);
                    if (!config.AI.RequireLineOfSightToShoot || ShouldFireAtTarget(bot, runtime, runtime.Memory.Target, now, true))
                    {
                        if (now >= runtime.NextReactionAllowedAt)
                        {
                            StartBotAttack(bot, runtime, runtime.Memory.Target);
                        }
                    }
                    else
                    {
                        StopBotAttack(bot, runtime);
                    }
                    break;

                case TacticalActionId.MoveToCover:
                    SetState(runtime, TacticalState.FightFromCover, now);
                    runtime.CurrentCover = action.Destination;
                    runtime.CurrentDestination = action.Destination;
                    runtime.NextCoverSearchAt = now + config.AI.CoverRepositionCooldownSeconds;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    FaceEntity(bot, runtime.Memory.Target);
                    MaintainFireOrStop(bot, runtime, now);
                    break;

                case TacticalActionId.PeekLeft:
                case TacticalActionId.PeekRight:
                case TacticalActionId.WideSwing:
                    SetState(runtime, TacticalState.FightFromCover, now);
                    runtime.IsPeeking = true;
                    runtime.CurrentPeekPoint = action.Destination;
                    runtime.CurrentPeekUntil = now + UnityEngine.Random.Range(config.AI.PeekExposureMinSeconds, config.AI.PeekExposureMaxSeconds);
                    runtime.CurrentDestination = action.Destination;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    FaceEntity(bot, runtime.Memory.Target);
                    MaintainFireOrStop(bot, runtime, now);
                    break;

                case TacticalActionId.Tuck:
                    SetState(runtime, TacticalState.FightFromCover, now);
                    runtime.IsPeeking = false;
                    runtime.CurrentTuckUntil = now + UnityEngine.Random.Range(config.AI.TuckMinSeconds, config.AI.TuckMaxSeconds);
                    runtime.NextPeekAt = runtime.CurrentTuckUntil;
                    StopBotAttack(bot, runtime);
                    runtime.CurrentDestination = action.Destination;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    break;

                case TacticalActionId.PushTarget:
                    SetState(runtime, TacticalState.Push, now);
                    runtime.CurrentDestination = action.Destination;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    FaceEntity(bot, runtime.Memory.Target);
                    MaintainFireOrStop(bot, runtime, now);
                    break;

                case TacticalActionId.SearchLastKnown:
                    SetState(runtime, TacticalState.SearchLastKnown, now);
                    runtime.CurrentDestination = action.Destination;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    FaceEntity(bot, runtime.Memory.Target);
                    MaintainFireOrStop(bot, runtime, now);
                    break;

                case TacticalActionId.InvestigateSound:
                    SetState(runtime, TacticalState.InvestigateSound, now);
                    runtime.CurrentDestination = action.Destination;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    FaceEntity(bot, runtime.Memory.Target);
                    MaintainFireOrStop(bot, runtime, now);
                    break;

                case TacticalActionId.RetreatToCover:
                    SetState(runtime, TacticalState.Retreat, now);
                    runtime.CurrentDestination = action.Destination;
                    runtime.NextCoverSearchAt = now + config.AI.CoverRepositionCooldownSeconds;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    FacePosition(bot, KnownThreatPosition(runtime));
                    MaintainFireOrStop(bot, runtime, now);
                    break;

                case TacticalActionId.FlankLeft:
                case TacticalActionId.FlankRight:
                    SetState(runtime, TacticalState.Flank, now);
                    runtime.CurrentFlankPoint = action.Destination;
                    runtime.CurrentDestination = action.Destination;
                    runtime.NextFlankAt = now + config.AI.FlankCooldownSeconds;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    FaceEntity(bot, runtime.Memory.Target);
                    MaintainFireOrStop(bot, runtime, now);
                    break;

                case TacticalActionId.RegroupWithSquad:
                    SetState(runtime, TacticalState.Regroup, now);
                    runtime.CurrentDestination = action.Destination;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    FaceEntity(bot, runtime.Memory.Target);
                    MaintainFireOrStop(bot, runtime, now);
                    break;

                case TacticalActionId.PlaceBarricade:
                    SetState(runtime, TacticalState.BarricadeHold, now);
                    runtime.CurrentBarricadePoint = action.Destination;
                    runtime.NextBarricadeAt = now + config.AI.BarricadeCooldownSeconds;
                    var barricadeThreatPosition = KnownThreatPosition(runtime);

                    if (TryPlaceBarricade(bot, runtime, action.Destination, barricadeThreatPosition))
                    {
                        if (!TryFindBarricadeHoldPoint(bot.transform.position, action.Destination, barricadeThreatPosition, out var holdPoint))
                        {
                            runtime.LastBarricadeReason = "hold_failed_slope";
                            runtime.CurrentCover = Vector3.zero;
                            runtime.CurrentTuckPoint = Vector3.zero;
                            runtime.CurrentPeekPoint = Vector3.zero;
                            runtime.BarricadeCommittedUntil = 0f;
                            runtime.CurrentDestination = FindRetreatPosition(bot.transform.position, barricadeThreatPosition);
                            MoveBotTo(bot, runtime, runtime.CurrentDestination, BaseNavigator.NavigationSpeed.Fast);
                            FacePosition(bot, barricadeThreatPosition);
                            MaintainFireOrStop(bot, runtime, now);
                            break;
                        }

                        runtime.CurrentTuckPoint = holdPoint;
                        runtime.CurrentCover = runtime.CurrentTuckPoint;
                        runtime.CurrentPeekPoint = BarricadePeekPoint(bot, runtime.CurrentTuckPoint, action.Destination, barricadeThreatPosition, runtime.Memory.Target);
                        runtime.CurrentDestination = runtime.CurrentTuckPoint;
                        runtime.IsPeeking = false;
                        runtime.CurrentTuckUntil = now + UnityEngine.Random.Range(config.AI.TuckMinSeconds, config.AI.TuckMaxSeconds);
                        runtime.NextPeekAt = runtime.CurrentTuckUntil;
                        runtime.HoldOutsideBaseUntil = now + config.AI.BarricadeHoldSeconds;
                        runtime.BarricadeCommittedUntil = now + config.AI.BarricadeFightCommitmentSeconds;
                        MoveBotTo(bot, runtime, runtime.CurrentDestination, BaseNavigator.NavigationSpeed.Fast);
                        FacePosition(bot, barricadeThreatPosition);
                        MaintainFireOrStop(bot, runtime, now);
                    }
                    break;

                case TacticalActionId.HoldOutsideBase:
                    SetState(runtime, TacticalState.HoldOutsideBase, now);
                    StopBotAttack(bot, runtime);
                    runtime.HoldOutsideBaseUntil = now + config.AI.BaseHoldSeconds;
                    runtime.CurrentDestination = action.Destination;
                    MoveBotTo(bot, runtime, action.Destination, BaseNavigator.NavigationSpeed.Fast);
                    FacePosition(bot, KnownThreatPosition(runtime));
                    break;

                case TacticalActionId.ThrowGrenade:
                    SetState(runtime, TacticalState.GrenadeFlush, now);
                    StopBotAttack(bot, runtime);
                    runtime.NextGrenadeAt = now + config.AI.GrenadeCooldownSeconds;
                    break;

                case TacticalActionId.ThrowSmoke:
                    SetState(runtime, TacticalState.Retreat, now);
                    StopBotAttack(bot, runtime);
                    runtime.NextGrenadeAt = now + config.AI.GrenadeCooldownSeconds;
                    break;

                case TacticalActionId.RoamToPoint:
                    SetState(runtime, TacticalState.Roam, now);
                    StopBotAttack(bot, runtime);
                    runtime.IsPeeking = false;
                    runtime.CurrentDestination = action.Destination == Vector3.zero ? FindRoamDestination(runtime.HomePosition) : action.Destination;

                    if (action.RiskFlags.Contains("stuck_recovery"))
                    {
                        runtime.NextStuckRecoveryAt = now + config.AI.StuckRecoveryCooldownSeconds;
                    }

                    MoveBotTo(bot, runtime, runtime.CurrentDestination, BaseNavigator.NavigationSpeed.Fast);
                    break;

                case TacticalActionId.AbandonTarget:
                    ClearTargetMemory(runtime);
                    SetState(runtime, TacticalState.Roam, now);
                    StopBotAttack(bot, runtime);
                    break;

                default:
                    SetState(runtime, TacticalState.Roam, now);
                    StopBotAttack(bot, runtime);
                    runtime.IsPeeking = false;
                    runtime.CurrentDestination = action.Destination == Vector3.zero ? FindRoamDestination(runtime.HomePosition) : action.Destination;
                    MoveBotTo(bot, runtime, runtime.CurrentDestination, BaseNavigator.NavigationSpeed.Fast);
                    break;
            }
        }

        private void SetState(BotRuntime runtime, TacticalState state, float now)
        {
            if (runtime.State == state)
            {
                return;
            }

            runtime.PreviousState = runtime.State;
            runtime.State = state;
            runtime.StateEnteredAt = now;
        }

        private void ClearTargetMemory(BotRuntime runtime)
        {
            runtime.Memory.Target = null;
            runtime.Memory.TargetUserId = 0;
            runtime.Memory.HasLineOfSight = false;
            runtime.Memory.TargetConfidence = 0f;
            runtime.Memory.TargetExposureFraction = 0f;
            runtime.Memory.TargetVisibleProbePoints = 0;
            runtime.Memory.TargetTotalProbePoints = 0;
        }

        private void QueueDecisionTrace(DecisionTrace trace)
        {
            if (trace == null)
            {
                return;
            }

            pendingDecisionTraces.Add(trace);

            if (decisionTraceSaveTimer == null || decisionTraceSaveTimer.Destroyed)
            {
                decisionTraceSaveTimer = timer.Once(5f, FlushDecisionTraces);
            }
        }

        private void FlushDecisionTraces()
        {
            decisionTraceSaveTimer = null;

            if (pendingDecisionTraces.Count == 0)
            {
                return;
            }

            try
            {
                var dataPath = Path.Combine(Interface.Oxide.DataFileSystem.Directory, DecisionTraceDataFile);
                Directory.CreateDirectory(Path.GetDirectoryName(dataPath));
                var lines = pendingDecisionTraces.Select(trace => JsonConvert.SerializeObject(trace, Formatting.None)).ToArray();
                File.AppendAllLines(dataPath, lines);
                pendingDecisionTraces.Clear();
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not write roam bot decision traces: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private string DecisionTraceDataPath()
        {
            return Path.Combine(Interface.Oxide.DataFileSystem.Directory, DecisionTraceDataFile);
        }

        private List<string> ReadDecisionTraceLines(int count, string botKey)
        {
            var path = DecisionTraceDataPath();

            if (!File.Exists(path))
            {
                return new List<string>();
            }

            var normalizedBotKey = (botKey ?? "").Trim();
            var lines = File.ReadLines(path)
                .Where(line => string.IsNullOrWhiteSpace(normalizedBotKey) || DecisionTraceLineMatchesBot(line, normalizedBotKey))
                .Reverse()
                .Take(Math.Max(1, count))
                .Reverse()
                .ToList();

            return lines;
        }

        private bool DecisionTraceLineMatchesBot(string line, string botKey)
        {
            try
            {
                var json = JObject.Parse(line);
                var traceBot = ((string) json["bot_id"] ?? "").Trim();
                return traceBot.Equals(botKey, StringComparison.OrdinalIgnoreCase)
                    || traceBot.IndexOf(botKey, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private string FormatDecisionTraceLine(string line)
        {
            try
            {
                var json = JObject.Parse(line);
                var botId = (string) json["bot_id"] ?? "unknown";
                var state = (string) json["state"] ?? "unknown";
                var finalAction = (string) json["final_action"] ?? "none";
                var finalScore = json.Value<float?>("final_score") ?? 0f;
                var advisorStatus = (string) json["advisor_status"] ?? "none";
                var fallback = (string) json["fallback_reason"] ?? "none";
                var candidates = (json["candidates"] as JArray)?.Count ?? 0;

                return $"{botId}: state={state}, action={finalAction}, score={finalScore:0.0}, candidates={candidates}, advisor={advisorStatus}, fallback={fallback}";
            }
            catch
            {
                return line.Length <= 240 ? line : line.Substring(0, 240);
            }
        }

        private string ActionIdString(TacticalActionId actionId)
        {
            var text = actionId.ToString();
            var builder = new System.Text.StringBuilder();

            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];

                if (char.IsUpper(character) && index > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

        private void ScheduleBodyPrepare(BaseCombatEntity bot)
        {
            timer.Once(0.25f, () => PrepareBodyIfActive(bot, "initial"));
            timer.Once(1.25f, () => PrepareBodyIfActive(bot, "followup"));
        }

        private void PrepareBodyIfActive(BaseCombatEntity bot, string phase)
        {
            if (!IsLiveBot(bot) || !activeBots.TryGetValue(bot, out var runtime))
            {
                return;
            }

            if (runtime.IsShooting)
            {
                return;
            }

            PrepareNpcBody(bot);

            if (runtime.CurrentDestination != Vector3.zero)
            {
                MoveBotTo(bot, runtime, runtime.CurrentDestination, BaseNavigator.NavigationSpeed.Fast);
            }

            if (config.Debug.DebugSpawnDetails)
            {
                Puts($"NPC body prepare ({phase}) for {runtime.DisplayName}: {BotRuntimeDiagnostics(bot, runtime)}.");
            }
        }

        private void PrepareNpcBody(BaseCombatEntity bot)
        {
            var npc = bot as NPCPlayer;

            if (npc != null)
            {
                try
                {
                    npc.Resume();
                }
                catch
                {
                }
            }

            var brain = bot.GetComponent<BaseAIBrain>() ?? bot.GetComponentInChildren<BaseAIBrain>();

            if (brain != null)
            {
                brain.AllowedToSleep = false;
                brain.sleeping = false;
                TryInvoke(brain, "SetEnabled", true);
                TryInvoke(brain, "SetThinkMode", AIThinkMode.FixedUpdate);
                SuppressBrainPlayerTargeting(bot, brain);
            }
        }

        private void SuppressBrainPlayerTargeting(BaseCombatEntity bot, BaseAIBrain brain = null)
        {
            brain = brain ?? bot?.GetComponent<BaseAIBrain>() ?? bot?.GetComponentInChildren<BaseAIBrain>();

            if (brain == null)
            {
                return;
            }

            try
            {
                brain.HostileTargetsOnly = true;
                brain.IgnoreSafeZonePlayers = true;
                brain.RefreshKnownLOS = false;
                brain.SenseTypes &= ~EntityType.Player;
                brain.mainInterestPoint = bot == null ? Vector3.zero : bot.transform.position;

                var senses = brain.Senses;

                if (senses != null)
                {
                    senses.hostileTargetsOnly = true;
                    senses.ignoreSafeZonePlayers = true;
                    senses.refreshKnownLOS = false;
                    senses.senseTypes &= ~EntityType.Player;
                    senses.LastThreatTimestamp = 0f;

                    var memory = senses.Memory;

                    if (memory != null)
                    {
                        RemovePlayerMemory(memory.Players);
                        RemovePlayerMemory(memory.Targets);
                        RemovePlayerMemory(memory.Threats);
                        memory.LOS.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                if (config.Debug.DebugSpawnDetails)
                {
                    PrintWarning($"Could not suppress scientist body targeting: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private void RemovePlayerMemory(List<BaseEntity> entities)
        {
            if (entities == null)
            {
                return;
            }

            entities.RemoveAll(entity => entity is BasePlayer);
        }

        private bool MoveBotTo(BaseCombatEntity bot, BotRuntime runtime, Vector3 destination, BaseNavigator.NavigationSpeed speed)
        {
            if (bot == null || runtime == null || destination == Vector3.zero)
            {
                return false;
            }

            if (IsBlockedLandPosition(destination))
            {
                runtime.ConsecutiveFailedPaths++;
                runtime.Movement.SameActionFailures++;
                return false;
            }

            runtime.IsInBaseRestrictedArea = IsBaseRestrictedPosition(bot.transform.position);

            if (!runtime.IsInBaseRestrictedArea && SegmentCrossesBaseRestrictedArea(bot.transform.position, destination))
            {
                runtime.ConsecutiveFailedPaths++;
                runtime.Movement.SameActionFailures++;
                return false;
            }

            PrepareNpcBody(bot);
            var npcCommanded = false;
            var navigatorCommanded = false;
            var npc = bot as NPCPlayer;

            if (npc != null)
            {
                try
                {
                    npc.SetDestination(destination);
                    npcCommanded = true;
                }
                catch (Exception ex)
                {
                    if (config.Debug.DebugSpawnDetails)
                    {
                        PrintWarning($"NPCPlayer.SetDestination failed for {ShortPrefab(bot.PrefabName)}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            var navigator = bot.GetComponent<BaseNavigator>() ?? bot.GetComponentInChildren<BaseNavigator>();
            var previousDestination = runtime.CurrentDestination;
            var destinationChanged = previousDestination == Vector3.zero || Vector3.Distance(previousDestination, destination) > 2f;
            navigatorCommanded = CommandNavigator(bot, navigator, destination, speed);
            runtime.CurrentDestination = destination;
            runtime.Movement.LastCommandAt = Time.realtimeSinceStartup;
            runtime.Movement.LastCommandDestination = destination;

            if (navigatorCommanded || npcCommanded)
            {
                runtime.Movement.LastActionId = runtime.Decisions.LastActionId;
                if (destinationChanged || runtime.Movement.LastProgressAt <= 0f)
                {
                    runtime.Movement.LastProgressAt = Time.realtimeSinceStartup;
                }
            }
            else
            {
                runtime.ConsecutiveFailedPaths++;
                runtime.Movement.SameActionFailures++;
            }

            return navigatorCommanded || npcCommanded;
        }

        private bool CommandNavigator(BaseCombatEntity bot, BaseNavigator navigator, Vector3 destination, BaseNavigator.NavigationSpeed speed)
        {
            if (navigator == null)
            {
                return false;
            }

            try
            {
                navigator.SetNavMeshEnabled(true);
                navigator.Resume();
                navigator.CanPathFindToChaseTargetIfNoMovePoint = true;
                navigator.CanUseRandomMovePointIfNonFound = true;
                navigator.FaceMoveTowardsTarget = true;
                navigator.FaceTargetChaseDistance = Math.Max(navigator.FaceTargetChaseDistance, 80f);
                var stopDistance = Math.Min(navigator.StoppingDistance <= 0f ? config.AI.CoverArrivalDistance : navigator.StoppingDistance, config.AI.CoverArrivalDistance);
                navigator.StoppingDistance = Mathf.Clamp(stopDistance, 0.75f, 3f);
                navigator.SetCurrentSpeed(speed);
                var moved = navigator.SetDestination(destination, speed, 0f, Math.Max(6f, config.Spawn.NavmeshSampleDistance));
                TryInvoke(navigator, "Think", 0.1f);
                TryInvoke(navigator, "UpdateNavigation", 0.1f);
                TryInvoke(navigator, "UpdateMovement", 0.1f);
                return moved;
            }
            catch (Exception ex)
            {
                if (config.Debug.DebugSpawnDetails)
                {
                    PrintWarning($"BaseNavigator.SetDestination failed for {ShortPrefab(bot?.PrefabName)}: {ex.GetType().Name}: {ex.Message}");
                }

                return false;
            }
        }

        private void FaceEntity(BaseCombatEntity bot, BaseEntity target)
        {
            if (target != null)
            {
                FacePosition(bot, target.transform.position);
            }
        }

        private void FacePosition(BaseCombatEntity bot, Vector3 position)
        {
            if (bot == null)
            {
                return;
            }

            var direction = position - bot.transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude <= 0.01f)
            {
                return;
            }

            bot.transform.rotation = Quaternion.LookRotation(direction.normalized);
            bot.SendNetworkUpdateImmediate();
        }

        private bool StartBotAttack(BaseCombatEntity bot, BotRuntime runtime, BasePlayer target)
        {
            if (bot == null || runtime == null || target == null || target.IsDead() || ShouldIgnoreSafeZonePlayer(target))
            {
                return false;
            }

            if (!EnsureBotPositionUsable(bot, runtime, Time.realtimeSinceStartup))
            {
                return false;
            }

            if (config.AI.RequireLineOfSightToShoot && !ShouldFireAtTarget(bot, runtime, target, Time.realtimeSinceStartup, true))
            {
                StopBotAttack(bot, runtime);
                return false;
            }

            EnsureBotWeaponLoaded(bot);
            ConfigureBrainForTarget(bot, target);
            var attacker = GetAttackInterface(bot);
            var started = false;

            if (attacker != null)
            {
                try
                {
                    started = attacker.StartAttacking(target);
                    attacker.AttackTick(0.1f, target, true);
                }
                catch (Exception ex)
                {
                    if (config.Debug.DebugSpawnDetails)
                    {
                        PrintWarning($"IAIAttack.StartAttacking failed for {ShortPrefab(bot.PrefabName)}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            if (!started)
            {
                started = TryInvokeBool(bot, "StartAttacking", target);
            }

            runtime.IsShooting = started;
            runtime.LastShotAt = Time.realtimeSinceStartup;

            if (!started)
            {
                runtime.LastFireBlockReason = attacker == null ? "no_attack_interface" : "start_failed";
            }

            return started;
        }

        private void StopBotAttack(BaseCombatEntity bot, BotRuntime runtime)
        {
            if (bot == null || runtime == null)
            {
                return;
            }

            var attacker = GetAttackInterface(bot);

            if (attacker != null)
            {
                TryInvoke(attacker, "StopAttacking");
            }

            TryInvoke(bot, "StopAttacking");
            SuppressBrainPlayerTargeting(bot);
            runtime.IsShooting = false;
        }

        private void MaintainFireOrStop(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (bot == null || runtime == null)
            {
                return;
            }

            FaceEntity(bot, runtime.Memory.Target);

            if (ShouldFireAtTarget(bot, runtime, runtime.Memory.Target, now, true))
            {
                StartBotAttack(bot, runtime, runtime.Memory.Target);
                return;
            }

            if (runtime.IsShooting)
            {
                StopBotAttack(bot, runtime);
            }
        }

        private IAIAttack GetAttackInterface(BaseCombatEntity bot)
        {
            var attacker = bot as IAIAttack;

            if (attacker != null)
            {
                return attacker;
            }

            try
            {
                return bot.GetComponent<IAIAttack>() ?? bot.GetComponentInChildren<IAIAttack>(true);
            }
            catch
            {
                return null;
            }
        }

        private void ConfigureBrainForTarget(BaseCombatEntity bot, BasePlayer target)
        {
            var brain = bot.GetComponent<BaseAIBrain>() ?? bot.GetComponentInChildren<BaseAIBrain>();

            if (brain == null || target == null)
            {
                return;
            }

            var distance = Vector3.Distance(bot.transform.position, target.transform.position);
            brain.AllowedToSleep = false;
            brain.sleeping = false;
            brain.HostileTargetsOnly = false;
            brain.IgnoreSafeZonePlayers = false;
            brain.RefreshKnownLOS = true;
            brain.SenseTypes |= EntityType.Player;
            brain.SenseRange = Math.Max(brain.SenseRange, distance + 30f);
            brain.TargetLostRange = Math.Max(brain.TargetLostRange, distance + 60f);
            brain.ListenRange = Math.Max(brain.ListenRange, distance + 30f);
            brain.AttackRangeMultiplier = Math.Max(brain.AttackRangeMultiplier, 1.4f);
            brain.mainInterestPoint = target.transform.position;
            SeedLegacySenses(brain, bot, target);
            TryInvoke(brain, "SetThinkMode", AIThinkMode.FixedUpdate);
            TryInvoke(brain, "SwitchToState", AIState.Chase, 0);
            TryInvoke(brain, "DoThink");
        }

        private void SeedLegacySenses(BaseAIBrain brain, BaseCombatEntity bot, BasePlayer target)
        {
            if (brain == null || bot == null || target == null || !HasLineOfSight(bot, target))
            {
                return;
            }

            var senses = brain.Senses;

            if (senses == null)
            {
                return;
            }

            senses.hostileTargetsOnly = false;
            senses.ignoreSafeZonePlayers = false;
            senses.refreshKnownLOS = true;
            senses.senseTypes |= EntityType.Player;
            senses.maxRange = Math.Max(senses.maxRange, Vector3.Distance(bot.transform.position, target.transform.position) + 30f);
            senses.targetLostRange = Math.Max(senses.targetLostRange, senses.maxRange + 30f);
            senses.LastThreatTimestamp = Time.time;
            senses.TimeInAgressiveState = Math.Max(senses.TimeInAgressiveState, 0.1f);

            var memory = senses.Memory;

            if (memory != null)
            {
                memory.SetKnown(target, bot, senses);
                memory.SetLOS(target, true);
                AddMemoryEntity(memory.Players, target);
                AddMemoryEntity(memory.Targets, target);
                AddMemoryEntity(memory.Threats, target);
                memory.LOS.Add(target);
            }

            TryInvoke(senses, "DelaySenseUpdate", 0f);
            TryInvoke(senses, "UpdateKnownPlayersLOS");
            TryInvoke(senses, "UpdateSenses");
        }

        private void AddMemoryEntity(List<BaseEntity> list, BaseEntity entity)
        {
            if (list == null || entity == null || list.Contains(entity))
            {
                return;
            }

            list.Add(entity);
        }

        private Vector3 FindRoamDestination(Vector3 origin)
        {
            var radius = Math.Max(12f, config.Spawn.GroupSpawnRadius * 3f);
            var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            var candidate = origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            candidate.y = TerrainHeight(candidate) + 0.25f;

            if (NavMesh.SamplePosition(candidate, out var hit, Math.Max(6f, config.Spawn.NavmeshSampleDistance), NavMesh.AllAreas)
                && !IsBlockedLandPosition(hit.position))
            {
                return hit.position;
            }

            return origin;
        }

        private Vector3 MoveTowardPosition(Vector3 origin, Vector3 targetPosition, float maxStep)
        {
            var delta = targetPosition - origin;
            delta.y = 0f;

            if (delta.sqrMagnitude <= 0.01f)
            {
                return origin;
            }

            var candidate = origin + delta.normalized * Mathf.Clamp(maxStep, 4f, 45f);
            candidate.y = TerrainHeight(candidate) + 0.25f;

            if (NavMesh.SamplePosition(candidate, out var hit, Math.Max(12f, config.Spawn.NavmeshSampleDistance), NavMesh.AllAreas)
                && !IsBlockedLandPosition(hit.position))
            {
                return hit.position;
            }

            return origin;
        }

        private Vector3 FindRetreatPosition(Vector3 origin, Vector3 threatPosition)
        {
            var delta = origin - threatPosition;
            delta.y = 0f;

            if (delta.sqrMagnitude <= 0.01f)
            {
                return FindRoamDestination(origin);
            }

            var candidate = origin + delta.normalized * Math.Max(18f, config.AI.CoverSearchRadius);
            candidate.y = TerrainHeight(candidate) + 0.25f;

            if (NavMesh.SamplePosition(candidate, out var hit, Math.Max(12f, config.Spawn.NavmeshSampleDistance), NavMesh.AllAreas)
                && !IsBlockedLandPosition(hit.position))
            {
                return hit.position;
            }

            return FindRoamDestination(origin);
        }

        private void ApplyCoverPlan(BotRuntime runtime, CoverPlan plan)
        {
            if (runtime == null || plan == null)
            {
                return;
            }

            runtime.CurrentCover = plan.CoverPoint;
            runtime.CurrentTuckPoint = plan.TuckPoint == Vector3.zero ? plan.CoverPoint : plan.TuckPoint;
            runtime.CurrentPeekPoint = plan.PeekLeftPoint != Vector3.zero ? plan.PeekLeftPoint : plan.PeekRightPoint;
        }

        private bool IsAtCover(BaseCombatEntity bot, BotRuntime runtime)
        {
            if (bot == null || runtime == null)
            {
                return false;
            }

            var arrivalDistance = Math.Max(0.75f, config.AI.CoverArrivalDistance);

            if (runtime.CurrentCover != Vector3.zero && Vector3.Distance(bot.transform.position, runtime.CurrentCover) <= arrivalDistance)
            {
                return true;
            }

            return runtime.CurrentTuckPoint != Vector3.zero && Vector3.Distance(bot.transform.position, runtime.CurrentTuckPoint) <= arrivalDistance;
        }

        private bool IsEffectiveCover(BaseCombatEntity bot, BotRuntime runtime, BasePlayer target)
        {
            if (!IsAtCover(bot, runtime))
            {
                return false;
            }

            if (target == null || runtime.Memory.TargetUserId == 0)
            {
                return true;
            }

            if (!runtime.Memory.HasLineOfSight)
            {
                return true;
            }

            if (runtime.Memory.TargetExposureFraction <= config.AI.EffectiveCoverMaxExposedFraction)
            {
                return true;
            }

            return IsWorldLineBlocked(EyePosition(target), EyePosition(bot), target, bot);
        }

        private string CoverStatus(BaseCombatEntity bot, BotRuntime runtime)
        {
            if (bot == null || runtime == null || runtime.CurrentCover == Vector3.zero)
            {
                return "none";
            }

            if (IsEffectiveCover(bot, runtime, runtime.Memory.Target))
            {
                return "effective";
            }

            return IsAtCover(bot, runtime) ? "compromised" : "moving";
        }

        private string MedicalStatus(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (bot == null || runtime == null)
            {
                return "none";
            }

            if (IsMedicalFireLocked(runtime, now))
            {
                return $"syringe_lock {Math.Max(0f, runtime.MedicalFireLockedUntil - now).ToString("0.0", CultureInfo.InvariantCulture)}s";
            }

            var maxHealth = Math.Max(1f, runtime.Skill.Health);
            var healthFraction = Mathf.Clamp01(bot.Health() / maxHealth);

            if (runtime.LowHealthCoverAwareUntil > now && healthFraction < config.AI.LowHealthCoverHealTargetFraction)
            {
                var nextSyringe = runtime.NextSyringeHealAt > now
                    ? $" next_syringe {Math.Max(0f, runtime.NextSyringeHealAt - now).ToString("0", CultureInfo.InvariantCulture)}s"
                    : "";
                return $"cover_heal{nextSyringe}";
            }

            if (config.AI.PassiveCombatHealPerSecond > 0f && healthFraction < config.AI.PassiveCombatHealTargetFraction)
            {
                return "passive";
            }

            return "none";
        }

        private SquadBlackboard SquadBoardFor(BotRuntime runtime)
        {
            if (runtime == null)
            {
                return null;
            }

            squadBlackboards.TryGetValue(runtime.TeamId, out var board);
            return board;
        }

        private bool TryGetSharedEnemyMemory(BotRuntime runtime, float now, out EnemyMemory memory)
        {
            memory = null;
            var board = SquadBoardFor(runtime);

            if (board == null || board.KnownEnemies.Count == 0)
            {
                return false;
            }

            memory = board.KnownEnemies.Values
                .Where(enemy => enemy != null && enemy.UserId != 0 && enemy.LastKnownPosition != Vector3.zero && now - enemy.LastKnownAt <= config.AI.SearchLastSeenSeconds)
                .OrderByDescending(enemy => enemy.Confidence)
                .ThenByDescending(enemy => enemy.LastKnownAt)
                .FirstOrDefault();

            return memory != null;
        }

        private bool TryBuildEnemyMemory(BotRuntime runtime, float now, out EnemyMemory memory)
        {
            memory = null;

            if (runtime == null || runtime.Memory.TargetUserId == 0)
            {
                return false;
            }

            var position = Vector3.zero;
            var knownAt = 0f;
            var confidence = 0f;
            var source = "";

            if (runtime.Memory.LastSeenPosition != Vector3.zero
                && runtime.Memory.LastSeenAt > 0f
                && now - runtime.Memory.LastSeenAt <= config.AI.SearchLastSeenSeconds)
            {
                position = runtime.Memory.LastSeenPosition;
                knownAt = runtime.Memory.LastSeenAt;
                confidence = Math.Max(confidence, runtime.Memory.TargetConfidence);
                source = runtime.Memory.HasLineOfSight ? "visible" : "last_seen";
            }

            if (runtime.Memory.LastDamageSourcePosition != Vector3.zero
                && runtime.Memory.LastDamagedAt > knownAt
                && now - runtime.Memory.LastDamagedAt <= config.AI.SquadContactCommitmentSeconds)
            {
                position = runtime.Memory.LastDamageSourcePosition;
                knownAt = runtime.Memory.LastDamagedAt;
                confidence = Math.Max(confidence, 0.62f);
                source = "damage";
            }

            if (runtime.Memory.LastHeardPosition != Vector3.zero
                && runtime.Memory.LastHeardAt > knownAt
                && now - runtime.Memory.LastHeardAt <= config.AI.SquadContactCommitmentSeconds)
            {
                position = runtime.Memory.LastHeardPosition;
                knownAt = runtime.Memory.LastHeardAt;
                confidence = Math.Max(confidence, 0.48f);
                source = "heard";
            }

            if (position == Vector3.zero || knownAt <= 0f)
            {
                return false;
            }

            memory = new EnemyMemory
            {
                UserId = runtime.Memory.TargetUserId,
                LastKnownPosition = position,
                LastKnownAt = knownAt,
                Confidence = Mathf.Clamp01(confidence),
                Source = source
            };

            return true;
        }

        private bool HasRecentContact(BotRuntime runtime, float now)
        {
            return RecentContactAt(runtime) > 0f && now - RecentContactAt(runtime) <= config.AI.SquadContactCommitmentSeconds;
        }

        private bool SquadHasFreshContact(SquadBlackboard board, float now)
        {
            return board != null
                && board.SharedEnemyKnownAt > 0f
                && now - board.SharedEnemyKnownAt <= config.AI.SquadContactCommitmentSeconds;
        }

        private float RecentContactAt(BotRuntime runtime)
        {
            if (runtime == null)
            {
                return 0f;
            }

            return Math.Max(
                Math.Max(runtime.Memory.LastSeenAt, runtime.Memory.LastHeardAt),
                Math.Max(runtime.Memory.LastDamagedAt, runtime.LastDamageDealtAt));
        }

        private float SkillWeightedChance(BotRuntime runtime, float casualChance, float averageChance, float dangerousChance)
        {
            var tier = runtime?.SkillTier ?? "";

            if (tier.Equals("casual", StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(casualChance);
            }

            if (tier.Equals("dangerous", StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(dangerousChance);
            }

            if (tier.Equals("average", StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(averageChance);
            }

            return Mathf.Clamp01(averageChance);
        }

        private bool IsAverageSkillOrHigher(BotRuntime runtime)
        {
            var tier = runtime?.SkillTier ?? "";
            return tier.Equals("average", StringComparison.OrdinalIgnoreCase)
                || tier.Equals("dangerous", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasDamageWallAwareness(BotRuntime runtime, float now)
        {
            if (runtime == null)
            {
                return false;
            }

            if (runtime.DamageBarricadeAwareUntil > now)
            {
                ExtendDamageBarricadeAwarenessThroughCooldown(runtime, now);
                return true;
            }

            if (runtime.LastDamageTakenAt <= 0f || now - runtime.LastDamageTakenAt > config.AI.DamageWallReactionWindowSeconds)
            {
                return false;
            }

            if (runtime.LastDamageBarricadeAwarenessCheckAt > 0f
                && now - runtime.LastDamageBarricadeAwarenessCheckAt < config.AI.DamageWallAwarenessRecheckSeconds)
            {
                return false;
            }

            runtime.LastDamageBarricadeAwarenessCheckAt = now;
            var chance = SkillWeightedChance(runtime, config.AI.DamageWallChanceCasual, config.AI.DamageWallChanceAverage, config.AI.DamageWallChanceDangerous);

            if (IsAverageSkillOrHigher(runtime))
            {
                chance = 1f;
            }

            if (UnityEngine.Random.value > chance)
            {
                return false;
            }

            runtime.DamageBarricadeAwareUntil = DamageBarricadeAwarenessUntil(runtime, now);
            return true;
        }

        private void ExtendDamageBarricadeAwarenessThroughCooldown(BotRuntime runtime, float now)
        {
            if (runtime == null || !ShouldCarryDamageBarricadeAwarenessThroughCooldown(runtime, now))
            {
                return;
            }

            runtime.DamageBarricadeAwareUntil = Math.Max(runtime.DamageBarricadeAwareUntil, DamageBarricadeAwarenessUntil(runtime, now));
        }

        private float DamageBarricadeAwarenessUntil(BotRuntime runtime, float now)
        {
            var awareUntil = now + config.AI.DamageWallReactionWindowSeconds;

            if (ShouldCarryDamageBarricadeAwarenessThroughCooldown(runtime, now))
            {
                awareUntil = Math.Max(awareUntil, runtime.NextBarricadeAt + config.AI.BarricadeFollowupMemorySeconds);
            }

            return awareUntil;
        }

        private bool ShouldCarryDamageBarricadeAwarenessThroughCooldown(BotRuntime runtime, float now)
        {
            return runtime != null
                && runtime.NextBarricadeAt > now
                && runtime.LastDamageTakenAt > 0f
                && now - runtime.LastDamageTakenAt <= config.AI.DamageWallReactionWindowSeconds
                && (runtime.LastBarricadePlacedAt <= 0f || runtime.LastDamageTakenAt >= runtime.LastBarricadePlacedAt - 0.2f);
        }

        private bool ShouldNoticeLowHealth(BotRuntime runtime, float healthFraction, float now)
        {
            if (runtime == null)
            {
                return false;
            }

            var healTarget = Math.Max(config.AI.LowHealthCoverThreshold, config.AI.LowHealthCoverHealTargetFraction);

            if (runtime.LowHealthCoverAwareUntil > now && healthFraction < healTarget)
            {
                return true;
            }

            if (healthFraction >= config.AI.LowHealthCoverThreshold)
            {
                if (healthFraction >= healTarget)
                {
                    runtime.LowHealthCoverAwareUntil = 0f;
                    runtime.LastLowHealthHealAt = 0f;
                }

                return false;
            }

            if (now < runtime.NextLowHealthAwarenessCheckAt)
            {
                return false;
            }

            runtime.NextLowHealthAwarenessCheckAt = now + config.AI.LowHealthCoverRecheckSeconds;
            var chance = SkillWeightedChance(runtime, config.AI.LowHealthCoverNoticeChanceCasual, config.AI.LowHealthCoverNoticeChanceAverage, config.AI.LowHealthCoverNoticeChanceDangerous);

            if (healthFraction <= config.AI.LowHealthCoverThreshold * 0.65f)
            {
                chance = Mathf.Clamp01(chance + 0.18f);
            }

            if (IsAverageSkillOrHigher(runtime) && healthFraction <= config.AI.LowHealthCoverThreshold * 0.5f)
            {
                chance = 1f;
            }

            if (UnityEngine.Random.value > chance)
            {
                return false;
            }

            runtime.LowHealthCoverAwareUntil = now + config.AI.LowHealthCoverCommitmentSeconds;
            runtime.LastLowHealthHealAt = 0f;
            return true;
        }

        private void UpdateMedicalHealing(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (bot == null || runtime == null)
            {
                return;
            }

            ApplyPassiveCombatHeal(bot, runtime, now);
            ApplySyringeCoverHeal(bot, runtime, now);
        }

        private void ApplyPassiveCombatHeal(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (config.AI.PassiveCombatHealPerSecond <= 0f)
            {
                runtime.LastPassiveHealAt = 0f;
                return;
            }

            var maxHealth = Math.Max(1f, runtime.Skill.Health);
            var targetHealth = maxHealth * config.AI.PassiveCombatHealTargetFraction;
            var currentHealth = bot.Health();

            if (currentHealth >= targetHealth)
            {
                runtime.LastPassiveHealAt = 0f;
                return;
            }

            if (runtime.LastPassiveHealAt <= 0f || runtime.LastPassiveHealAt > now)
            {
                runtime.LastPassiveHealAt = now;
                return;
            }

            var elapsed = Mathf.Clamp(now - runtime.LastPassiveHealAt, 0f, 1f);
            runtime.LastPassiveHealAt = now;

            if (elapsed > 0f)
            {
                bot.SetHealth(Math.Min(targetHealth, currentHealth + config.AI.PassiveCombatHealPerSecond * elapsed));
            }
        }

        private void ApplySyringeCoverHeal(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            var maxHealth = Math.Max(1f, runtime.Skill.Health);
            var targetHealth = maxHealth * Math.Max(config.AI.LowHealthCoverThreshold, config.AI.LowHealthCoverHealTargetFraction);
            var currentHealth = bot.Health();

            if (config.AI.LowHealthCoverHealPerSecond <= 0f || runtime.LowHealthCoverAwareUntil <= now)
            {
                runtime.LastLowHealthHealAt = 0f;
                return;
            }

            if (currentHealth >= targetHealth)
            {
                runtime.LowHealthCoverAwareUntil = 0f;
                runtime.LastLowHealthHealAt = 0f;
                runtime.MedicalFireLockedUntil = Math.Min(runtime.MedicalFireLockedUntil, now);
                SetState(runtime, TacticalState.FightFromCover, now);
                MaintainFireOrStop(bot, runtime, now);
                return;
            }

            if (!IsEffectiveCover(bot, runtime, runtime.Memory.Target))
            {
                runtime.LastLowHealthHealAt = 0f;
                runtime.NextCoverSearchAt = 0f;
                return;
            }

            if (runtime.LastDamageTakenAt > 0f && now - runtime.LastDamageTakenAt <= 1.25f)
            {
                runtime.LastLowHealthHealAt = now;
                return;
            }

            if (!IsMedicalFireLocked(runtime, now))
            {
                if (now < runtime.NextSyringeHealAt)
                {
                    runtime.LastLowHealthHealAt = 0f;
                    return;
                }

                runtime.MedicalFireLockedUntil = now + config.AI.SyringeFireLockSeconds;
                runtime.NextSyringeHealAt = runtime.MedicalFireLockedUntil + config.AI.SyringeCooldownSeconds;
                runtime.LastLowHealthHealAt = now;
                StopBotAttack(bot, runtime);
                return;
            }

            if (runtime.LastLowHealthHealAt <= 0f || runtime.LastLowHealthHealAt > now)
            {
                runtime.LastLowHealthHealAt = now;
                return;
            }

            var elapsed = Mathf.Clamp(now - runtime.LastLowHealthHealAt, 0f, 1f);
            runtime.LastLowHealthHealAt = now;

            if (elapsed <= 0f)
            {
                return;
            }

            bot.SetHealth(Math.Min(targetHealth, currentHealth + config.AI.LowHealthCoverHealPerSecond * elapsed));
        }

        private Vector3 KnownThreatPosition(BotRuntime runtime)
        {
            if (runtime == null)
            {
                return Vector3.zero;
            }

            if (runtime.Memory.Target != null)
            {
                return runtime.Memory.Target.transform.position;
            }

            var bestKnownAt = 0f;
            var bestKnownPosition = Vector3.zero;

            if (runtime.Memory.LastSeenAt > bestKnownAt && runtime.Memory.LastSeenPosition != Vector3.zero)
            {
                bestKnownAt = runtime.Memory.LastSeenAt;
                bestKnownPosition = runtime.Memory.LastSeenPosition;
            }

            if (runtime.Memory.LastHeardAt > bestKnownAt && runtime.Memory.LastHeardPosition != Vector3.zero)
            {
                bestKnownAt = runtime.Memory.LastHeardAt;
                bestKnownPosition = runtime.Memory.LastHeardPosition;
            }

            if (runtime.Memory.LastDamagedAt > bestKnownAt && runtime.Memory.LastDamageSourcePosition != Vector3.zero)
            {
                bestKnownPosition = runtime.Memory.LastDamageSourcePosition;
            }

            return bestKnownPosition;
        }

        private bool TryFindFlankPosition(Vector3 origin, Vector3 threatPosition, float sideSign, out Vector3 flankPoint)
        {
            flankPoint = Vector3.zero;

            if (threatPosition == Vector3.zero)
            {
                return false;
            }

            var toThreat = threatPosition - origin;
            toThreat.y = 0f;

            if (toThreat.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            toThreat.Normalize();
            var side = Vector3.Cross(Vector3.up, toThreat).normalized * Mathf.Sign(sideSign);
            var distance = config.AI.SquadFlankDistance;
            var candidates = new[]
            {
                origin + side * distance + toThreat * (distance * 0.35f),
                origin + side * (distance * 0.75f) + toThreat * (distance * 0.55f),
                origin + side * (distance * 1.15f)
            };

            foreach (var candidate in candidates)
            {
                if (!TrySampleTacticalPosition(candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), out var sampled))
                {
                    continue;
                }

                if (SegmentCrossesBaseRestrictedArea(origin, sampled))
                {
                    continue;
                }

                flankPoint = sampled;
                return true;
            }

            return false;
        }

        private bool IsCoverClaimedBySquad(BotRuntime runtime, Vector3 coverPoint)
        {
            var board = SquadBoardFor(runtime);

            if (board == null || coverPoint == Vector3.zero)
            {
                return false;
            }

            foreach (var claim in board.CoverClaims)
            {
                if (string.Equals(claim.Key, runtime.BotKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (claim.Value != Vector3.zero && Vector3.Distance(claim.Value, coverPoint) < 6f)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldPlaceBarricade(BaseCombatEntity bot, BotRuntime runtime, Vector3 threatPosition, float healthFraction, float exposure, float now, out Vector3 barricadePoint)
        {
            barricadePoint = Vector3.zero;

            if (bot == null || runtime == null || !config.AI.AllowBarricades || config.AI.MaxActiveBotBarricades <= 0 || string.IsNullOrWhiteSpace(config.AI.BarricadePrefab))
            {
                if (runtime != null)
                {
                    runtime.LastBarricadeReason = "disabled_or_config";
                }

                return false;
            }

            CleanupBotPlacedEntityRefs();

            if (botPlacedEntities.Count >= config.AI.MaxActiveBotBarricades && !config.AI.RecycleOldestBarricadeWhenCapReached)
            {
                runtime.LastBarricadeReason = "cap_reached";
                return false;
            }
            else if (botPlacedEntities.Count >= config.AI.MaxActiveBotBarricades)
            {
                runtime.LastBarricadeReason = "cap_recycle_candidate";
            }

            var damageAware = HasDamageWallAwareness(runtime, now);
            var lowHealthAware = runtime.LowHealthCoverAwareUntil > now && healthFraction < Math.Max(config.AI.LowHealthCoverThreshold, config.AI.LowHealthCoverHealTargetFraction);
            var pressured = damageAware || lowHealthAware || exposure >= 0.65f;

            if (!pressured)
            {
                runtime.LastBarricadeReason = "not_pressured";
                return false;
            }

            if (IsEffectiveCover(bot, runtime, runtime.Memory.Target))
            {
                runtime.LastBarricadeReason = "already_cover";
                return false;
            }

            if (!TryFindBarricadePlacement(bot, runtime, threatPosition, out barricadePoint))
            {
                return false;
            }

            runtime.LastBarricadeReason = damageAware ? "candidate_damage" : (lowHealthAware ? "candidate_low_hp" : "candidate_exposed");
            return true;
        }

        private bool TryFindBarricadePlacement(BaseCombatEntity bot, BotRuntime runtime, Vector3 threatPosition, out Vector3 barricadePoint)
        {
            barricadePoint = Vector3.zero;

            if (bot == null || runtime == null || threatPosition == Vector3.zero)
            {
                if (runtime != null)
                {
                    runtime.LastBarricadeReason = "no_threat";
                }

                return false;
            }

            var origin = bot.transform.position;
            var toThreat = threatPosition - origin;
            toThreat.y = 0f;
            var threatDistance = toThreat.magnitude;

            if (threatDistance < 8f || threatDistance > Math.Max(75f, config.AI.VisionRange))
            {
                runtime.LastBarricadeReason = "threat_range";
                return false;
            }

            toThreat.Normalize();
            var side = Vector3.Cross(Vector3.up, toThreat).normalized;
            var distances = new[]
            {
                config.AI.BarricadePlacementDistance,
                Mathf.Max(3f, config.AI.BarricadePlacementDistance - 1.2f),
                Mathf.Min(8.5f, config.AI.BarricadePlacementDistance + 1.4f)
            };
            var sideOffsets = new[] { 0f, 1.75f, -1.75f, 3.25f, -3.25f };

            foreach (var distance in distances)
            {
                foreach (var sideOffset in sideOffsets)
                {
                    var candidate = origin + toThreat * distance + side * sideOffset;
                    candidate.y = TerrainHeight(candidate) + 0.05f;

                    if (IsBlockedLandPosition(candidate) || SegmentCrossesBaseRestrictedArea(origin, candidate) || !HasBarricadePlacementClearance(candidate))
                    {
                        continue;
                    }

                    if (!TryFindBarricadeHoldPoint(origin, candidate, threatPosition, out _))
                    {
                        continue;
                    }

                    barricadePoint = candidate;
                    return true;
                }
            }

            runtime.LastBarricadeReason = "no_clear_spot";
            return false;
        }

        private bool HasBarricadePlacementClearance(Vector3 position)
        {
            var mask = LayerMask.GetMask("Construction", "Deployed");
            return mask == 0 || !Physics.CheckSphere(position + Vector3.up * 1.1f, 0.85f, mask, QueryTriggerInteraction.Ignore);
        }

        private bool TryPlaceBarricade(BaseCombatEntity bot, BotRuntime runtime, Vector3 position, Vector3 threatPosition)
        {
            if (bot == null || runtime == null || position == Vector3.zero || string.IsNullOrWhiteSpace(config.AI.BarricadePrefab))
            {
                if (runtime != null)
                {
                    runtime.LastBarricadeReason = "spawn_bad_input";
                }

                return false;
            }

            CleanupBotPlacedEntityRefs();

            if (IsBlockedLandPosition(position) || !HasBarricadePlacementClearance(position))
            {
                runtime.LastBarricadeReason = "spawn_blocked";
                return false;
            }

            if (botPlacedEntities.Count >= config.AI.MaxActiveBotBarricades && !RecycleOldestBarricade(runtime))
            {
                runtime.LastBarricadeReason = "cap_reached";
                return false;
            }

            var forward = threatPosition == Vector3.zero ? bot.transform.forward : threatPosition - position;
            forward.y = 0f;

            if (forward.sqrMagnitude <= 0.01f)
            {
                forward = bot.transform.forward;
                forward.y = 0f;
            }

            var rotation = forward.sqrMagnitude <= 0.01f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized);
            var entity = GameManager.server.CreateEntity(config.AI.BarricadePrefab, position, rotation, true) as BaseEntity;

            if (entity == null)
            {
                runtime.LastBarricadeReason = "spawn_failed";
                runtime.NextBarricadeAt = Time.realtimeSinceStartup + Math.Min(10f, config.AI.BarricadeCooldownSeconds);
                return false;
            }

            entity.Spawn();
            botPlacedEntities.Add(entity);
            runtime.LastBarricadePlacedAt = Time.realtimeSinceStartup;
            runtime.LastBarricadeReason = "placed";

            if (config.Debug.DebugTacticalDecisions)
            {
                Puts($"{runtime.DisplayName} placed barricade at {FormatVector(position)} ({botPlacedEntities.Count}/{config.AI.MaxActiveBotBarricades}).");
            }

            return true;
        }

        private bool RecycleOldestBarricade(BotRuntime runtime)
        {
            if (!config.AI.RecycleOldestBarricadeWhenCapReached)
            {
                return false;
            }

            CleanupBotPlacedEntityRefs();

            var oldest = botPlacedEntities.FirstOrDefault(entity => entity != null && !entity.IsDestroyed);

            if (oldest == null)
            {
                CleanupBotPlacedEntityRefs();
                return botPlacedEntities.Count < config.AI.MaxActiveBotBarricades;
            }

            try
            {
                oldest.Kill(BaseNetworkable.DestroyMode.None);
                botPlacedEntities.Remove(oldest);
                if (runtime != null)
                {
                    runtime.LastBarricadeReason = "recycled_oldest";
                }
                return true;
            }
            catch (Exception ex)
            {
                if (config.Debug.DebugTacticalDecisions)
                {
                    PrintWarning($"Could not recycle oldest roam bot barricade: {ex.GetType().Name}: {ex.Message}");
                }

                CleanupBotPlacedEntityRefs();
                return botPlacedEntities.Count < config.AI.MaxActiveBotBarricades;
            }
        }

        private bool TryFindBarricadeHoldPoint(Vector3 botPosition, Vector3 barricadePosition, Vector3 threatPosition, out Vector3 holdPoint)
        {
            holdPoint = Vector3.zero;

            if (barricadePosition == Vector3.zero || threatPosition == Vector3.zero)
            {
                return false;
            }

            var awayFromThreat = barricadePosition - threatPosition;
            awayFromThreat.y = 0f;

            if (awayFromThreat.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            awayFromThreat.Normalize();
            var side = Vector3.Cross(Vector3.up, awayFromThreat).normalized;
            var barricadeTerrain = TerrainHeight(barricadePosition);
            var distances = new[] { 2.25f, 3f, 3.75f, 1.6f };
            var sideOffsets = new[] { 0f, 1.15f, -1.15f };

            foreach (var distance in distances)
            {
                foreach (var sideOffset in sideOffsets)
                {
                    var candidate = barricadePosition + awayFromThreat * distance + side * sideOffset;
                    candidate.y = TerrainHeight(candidate) + 0.25f;

                    if (Math.Abs(TerrainHeight(candidate) - barricadeTerrain) > 2.2f)
                    {
                        continue;
                    }

                    if (IsBlockedLandPosition(candidate) || SegmentCrossesBaseRestrictedArea(botPosition, candidate))
                    {
                        continue;
                    }

                    if (!NavMesh.SamplePosition(candidate, out var hit, 1.75f, NavMesh.AllAreas))
                    {
                        continue;
                    }

                    if (IsBlockedLandPosition(hit.position)
                        || Distance2D(hit.position, candidate) > 1.6f
                        || Distance2D(hit.position, barricadePosition) > 5.25f
                        || Math.Abs(hit.position.y - candidate.y) > 1.75f)
                    {
                        continue;
                    }

                    holdPoint = hit.position;
                    return true;
                }
            }

            return false;
        }

        private Vector3 BarricadePeekPoint(BaseCombatEntity bot, Vector3 holdPoint, Vector3 barricadePosition, Vector3 threatPosition, BasePlayer target)
        {
            if (holdPoint == Vector3.zero || threatPosition == Vector3.zero)
            {
                return Vector3.zero;
            }

            var firstSide = UnityEngine.Random.value < 0.5f ? 1f : -1f;

            if (TryBuildPeekPoint(bot, holdPoint, threatPosition, target, firstSide, out var firstPeek))
            {
                return firstPeek;
            }

            if (TryBuildPeekPoint(bot, holdPoint, threatPosition, target, -firstSide, out var secondPeek))
            {
                return secondPeek;
            }

            var toThreat = threatPosition - barricadePosition;
            toThreat.y = 0f;

            if (toThreat.sqrMagnitude <= 0.01f)
            {
                return Vector3.zero;
            }

            toThreat.Normalize();
            var side = Vector3.Cross(Vector3.up, toThreat).normalized * firstSide;
            var fallback = barricadePosition + side * config.AI.PeekOffsetDistance + toThreat * 1.1f;

            return TrySampleTacticalPosition(fallback, Math.Max(6f, config.Spawn.NavmeshSampleDistance), out var sampled)
                ? sampled
                : Vector3.zero;
        }

        private void CleanupBotPlacedEntityRefs()
        {
            botPlacedEntities.RemoveAll(entity => entity == null || entity.IsDestroyed);
        }

        private bool TryFindCoverPlan(BaseCombatEntity bot, BotRuntime runtime, Vector3 threatPosition, BasePlayer target, out CoverPlan plan)
        {
            plan = null;

            if (bot == null || runtime == null)
            {
                return false;
            }

            var origin = bot.transform.position;
            var away = origin - threatPosition;
            away.y = 0f;

            if (away.sqrMagnitude <= 0.01f)
            {
                away = -bot.transform.forward;
                away.y = 0f;
            }

            if (away.sqrMagnitude <= 0.01f)
            {
                away = Vector3.forward;
            }

            away.Normalize();
            var threatEye = target == null ? threatPosition + Vector3.up * 1.6f : EyePosition(target);
            var bestScore = float.MinValue;
            var attempts = Math.Max(8, config.AI.CoverPointAttempts);
            var maxRadius = Math.Max(6f, config.AI.CoverSearchRadius);

            for (var index = 0; index < attempts; index++)
            {
                var shell = index / 8;
                var slot = index % 8;
                var shellT = attempts <= 8 ? 0f : Mathf.Clamp01(shell / Mathf.Max(1f, (attempts / 8f) - 1f));
                var radius = Mathf.Lerp(6f, maxRadius, shellT);
                var angleOffset = ((slot - 3.5f) * 22.5f) + shell * 11.25f;
                var direction = Quaternion.Euler(0f, angleOffset, 0f) * away;
                var candidate = origin + direction.normalized * radius;

                if (!TrySampleTacticalPosition(candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), out var sampled))
                {
                    continue;
                }

                var distanceToThreat = Vector3.Distance(sampled, threatPosition);

                if (distanceToThreat < config.AI.CoverMinimumDistanceFromThreat)
                {
                    continue;
                }

                if (IsCoverClaimedBySquad(runtime, sampled))
                {
                    continue;
                }

                var coverEye = sampled + Vector3.up * 1.25f;

                if (!IsWorldLineBlocked(threatEye, coverEye, target, bot))
                {
                    continue;
                }

                var distanceFromBot = Vector3.Distance(origin, sampled);
                var score = 80f - distanceFromBot * 1.25f + distanceToThreat * 0.35f;
                var left = Vector3.zero;
                var right = Vector3.zero;

                if (TryBuildPeekPoint(bot, sampled, threatPosition, target, 1f, out var leftPeek))
                {
                    left = leftPeek;
                    score += 8f;
                }

                if (TryBuildPeekPoint(bot, sampled, threatPosition, target, -1f, out var rightPeek))
                {
                    right = rightPeek;
                    score += 8f;
                }

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                plan = new CoverPlan
                {
                    CoverPoint = sampled,
                    TuckPoint = sampled,
                    PeekLeftPoint = left,
                    PeekRightPoint = right,
                    Score = score
                };
            }

            if (plan == null)
            {
                runtime.NextCoverSearchAt = Time.realtimeSinceStartup + config.AI.CoverRepositionCooldownSeconds;
                return false;
            }

            if (config.Debug.DebugCoverScores)
            {
                Puts($"{runtime.DisplayName} cover score={plan.Score:0.0} cover={FormatVector(plan.CoverPoint)} peek={FormatVector(plan.PeekLeftPoint != Vector3.zero ? plan.PeekLeftPoint : plan.PeekRightPoint)}");
            }

            return true;
        }

        private bool TryBuildPeekPoint(BaseCombatEntity bot, Vector3 coverPoint, Vector3 threatPosition, BasePlayer target, float sideSign, out Vector3 peekPoint)
        {
            peekPoint = Vector3.zero;
            var toThreat = threatPosition - coverPoint;
            toThreat.y = 0f;

            if (toThreat.sqrMagnitude <= 0.01f)
            {
                toThreat = bot == null ? Vector3.forward : bot.transform.forward;
                toThreat.y = 0f;
            }

            if (toThreat.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            toThreat.Normalize();
            var side = Vector3.Cross(Vector3.up, toThreat).normalized * Mathf.Sign(sideSign);
            var candidate = coverPoint + side * config.AI.PeekOffsetDistance + toThreat * 1.25f;

            if (!TrySampleTacticalPosition(candidate, Math.Max(6f, config.Spawn.NavmeshSampleDistance), out var sampled))
            {
                return false;
            }

            if (target != null)
            {
                var from = sampled + Vector3.up * 1.55f;
                var points = TargetProbePoints(target);
                var visiblePoints = points.Count(point => IsTargetSightLineClear(bot, target, from, point));
                var exposure = points.Count == 0 ? 0f : visiblePoints / (float)points.Count;

                if (exposure < Math.Min(config.AI.MinimumExposedTargetFraction, 0.34f))
                {
                    return false;
                }
            }

            peekPoint = sampled;
            return true;
        }

        private Vector3 FindStuckRecoveryDestination(BaseCombatEntity bot, BotRuntime runtime)
        {
            if (bot == null || runtime == null)
            {
                return Vector3.zero;
            }

            var origin = bot.transform.position;
            var pressure = runtime.Memory.LastSeenAt >= runtime.Memory.LastHeardAt
                ? runtime.Memory.LastSeenPosition
                : runtime.Memory.LastHeardPosition;

            if (pressure == Vector3.zero)
            {
                pressure = runtime.CurrentDestination == Vector3.zero ? runtime.HomePosition : runtime.CurrentDestination;
            }

            var away = origin - pressure;
            away.y = 0f;

            if (away.sqrMagnitude <= 0.01f)
            {
                away = -bot.transform.forward;
                away.y = 0f;
            }

            if (away.sqrMagnitude <= 0.01f)
            {
                away = Vector3.forward;
            }

            away.Normalize();
            var radius = Math.Max(6f, config.AI.StuckRecoverySearchRadius);
            var angles = new[] { 75f, -75f, 125f, -125f, 180f, 35f, -35f, 0f };

            foreach (var angle in angles)
            {
                var direction = Quaternion.Euler(0f, angle, 0f) * away;

                for (var scale = 0.55f; scale <= 1.15f; scale += 0.3f)
                {
                    var candidate = origin + direction.normalized * radius * scale;

                    if (!TrySampleTacticalPosition(candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), out var sampled))
                    {
                        continue;
                    }

                    if (runtime.CurrentDestination != Vector3.zero && Vector3.Distance(sampled, runtime.CurrentDestination) < 8f)
                    {
                        continue;
                    }

                    return sampled;
                }
            }

            return FindRoamDestination(origin);
        }

        private bool TrySampleTacticalPosition(Vector3 candidate, float sampleDistance, out Vector3 sampled)
        {
            sampled = Vector3.zero;
            candidate.y = TerrainHeight(candidate) + 0.25f;

            if (IsBlockedLandPosition(candidate))
            {
                return false;
            }

            if (!NavMesh.SamplePosition(candidate, out var hit, sampleDistance, NavMesh.AllAreas))
            {
                return false;
            }

            if (IsBlockedLandPosition(hit.position))
            {
                return false;
            }

            sampled = hit.position;
            return true;
        }

        private bool IsWorldLineBlocked(Vector3 from, Vector3 to, BaseEntity ignoreA = null, BaseEntity ignoreB = null)
        {
            var mask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed", "Default", "Tree", "Resource");

            if (!Physics.Linecast(from, to, out var hit, mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            var entity = hit.GetEntity();
            return entity == null || (entity != ignoreA && entity != ignoreB);
        }

        private CombatProfile RefreshCombatProfile(BaseCombatEntity bot, BotRuntime runtime)
        {
            if (runtime == null)
            {
                return new CombatProfile();
            }

            var shortname = ActiveWeaponShortname(bot);

            if (runtime.Combat != null && string.Equals(runtime.Combat.WeaponShortname, shortname, StringComparison.OrdinalIgnoreCase))
            {
                return runtime.Combat;
            }

            var previous = runtime.Combat;
            runtime.Combat = BuildCombatProfile(shortname);

            if (previous != null)
            {
                runtime.Combat.NextPoorRangeShotAt = previous.NextPoorRangeShotAt;
                runtime.Combat.PoorRangeFireUntil = previous.PoorRangeFireUntil;
            }

            return runtime.Combat;
        }

        private CombatProfile BuildCombatProfile(string shortname)
        {
            var normalized = (shortname ?? "").Trim().ToLowerInvariant();
            var profile = new CombatProfile
            {
                WeaponShortname = normalized,
                WeaponClass = "default",
                PreferredDistance = 58f,
                IdealRange = 90f,
                HarassRange = 120f,
                MaxRange = 150f,
                PushDistance = 24f,
                RetreatDistance = 10f
            };

            if (normalized.Contains("shotgun") || normalized.Contains("spas12"))
            {
                profile.WeaponClass = "shotgun";
                profile.PreferredDistance = 18f;
                profile.IdealRange = 28f;
                profile.HarassRange = 38f;
                profile.MaxRange = 48f;
                profile.PushDistance = 28f;
            }
            else if (normalized.Contains("smg") || normalized.Contains("thompson"))
            {
                profile.WeaponClass = "smg";
                profile.PreferredDistance = 48f;
                profile.IdealRange = 72f;
                profile.HarassRange = 96f;
                profile.MaxRange = 115f;
                profile.PushDistance = 26f;
            }
            else if (normalized.Contains("pistol") || normalized.Contains("python") || normalized.Contains("revolver"))
            {
                profile.WeaponClass = "pistol";
                profile.PreferredDistance = 38f;
                profile.IdealRange = 58f;
                profile.HarassRange = 78f;
                profile.MaxRange = 96f;
                profile.PushDistance = 24f;
            }
            else if (normalized.Contains("bolt") || normalized.Contains("l96"))
            {
                profile.WeaponClass = "sniper";
                profile.PreferredDistance = 125f;
                profile.IdealRange = 180f;
                profile.HarassRange = 230f;
                profile.MaxRange = 260f;
                profile.PushDistance = 12f;
            }
            else if (normalized.Contains("m39") || normalized.Contains("semiauto"))
            {
                profile.WeaponClass = "marksman";
                profile.PreferredDistance = 82f;
                profile.IdealRange = 125f;
                profile.HarassRange = 165f;
                profile.MaxRange = 190f;
                profile.PushDistance = 18f;
            }
            else if (normalized.Contains("m249") || normalized.Contains("lmg"))
            {
                profile.WeaponClass = "lmg";
                profile.PreferredDistance = 95f;
                profile.IdealRange = 135f;
                profile.HarassRange = 175f;
                profile.MaxRange = 210f;
                profile.PushDistance = 16f;
            }
            else if (normalized.Contains("rifle") || normalized.Contains("m16") || normalized.Contains("lr300") || normalized.Contains("ak"))
            {
                profile.WeaponClass = "rifle";
                profile.PreferredDistance = 88f;
                profile.IdealRange = 135f;
                profile.HarassRange = 175f;
                profile.MaxRange = 205f;
                profile.PushDistance = 18f;
            }

            profile.MaxRange = Mathf.Clamp(profile.MaxRange, 20f, config.AI.VisionRange);
            profile.IdealRange = Mathf.Clamp(profile.IdealRange, 10f, profile.MaxRange);
            profile.HarassRange = Mathf.Clamp(profile.HarassRange, profile.IdealRange, profile.MaxRange);
            profile.PreferredDistance = Mathf.Clamp(profile.PreferredDistance, 8f, profile.IdealRange);
            return profile;
        }

        private float WeaponRangeScore(BotRuntime runtime, float distance)
        {
            var profile = runtime?.Combat ?? new CombatProfile();

            if (distance <= profile.IdealRange)
            {
                return 1f;
            }

            if (distance <= profile.HarassRange)
            {
                var t = Mathf.InverseLerp(profile.IdealRange, profile.HarassRange, distance);
                return Mathf.Lerp(0.65f, 0.35f, t);
            }

            if (distance <= profile.MaxRange)
            {
                var t = Mathf.InverseLerp(profile.HarassRange, profile.MaxRange, distance);
                return Mathf.Lerp(0.35f, 0.08f, t);
            }

            return 0f;
        }

        private bool ShouldFireAtTarget(BaseCombatEntity bot, BotRuntime runtime, BasePlayer target, float now, bool allowPoorRange)
        {
            if (bot == null || runtime == null || target == null || target.IsDead() || ShouldIgnoreSafeZonePlayer(target))
            {
                return BlockFire(runtime, "no_target");
            }

            if (IsMedicalFireLocked(runtime, now))
            {
                return BlockFire(runtime, "syringe_lock");
            }

            if (!HasAmmoToShoot(bot))
            {
                return BlockFire(runtime, "no_ammo");
            }

            if (IsBaseRestrictedPosition(target.transform.position))
            {
                return BlockFire(runtime, "target_in_base");
            }

            var visibility = TargetVisibility(bot, target, config.AI.MinimumExposedTargetFractionToShoot);

            if (!visibility.CanSee)
            {
                runtime.Memory.TargetExposureFraction = visibility.ExposedFraction;
                runtime.Memory.TargetVisibleProbePoints = visibility.VisibleProbePoints;
                runtime.Memory.TargetTotalProbePoints = visibility.TotalProbePoints;
                return BlockFire(runtime, "no_los");
            }

            runtime.Memory.TargetExposureFraction = visibility.ExposedFraction;
            runtime.Memory.TargetVisibleProbePoints = visibility.VisibleProbePoints;
            runtime.Memory.TargetTotalProbePoints = visibility.TotalProbePoints;

            var profile = RefreshCombatProfile(bot, runtime);
            var distance = Vector3.Distance(bot.transform.position, target.transform.position);

            if (distance > profile.MaxRange)
            {
                return BlockFire(runtime, "out_of_range");
            }

            runtime.LastFireBlockReason = "ready";
            return true;
        }

        private bool BlockFire(BotRuntime runtime, string reason)
        {
            if (runtime != null)
            {
                runtime.LastFireBlockReason = reason;
            }

            return false;
        }

        private float PoorRangeFireChance(BotRuntime runtime)
        {
            var skill = runtime?.Skill ?? new SkillDefinition();
            return Mathf.Clamp(Mathf.Lerp(0.36f, 0.12f, skill.Courage) + skill.TacticalNoise * 0.22f, 0.08f, 0.48f);
        }

        private float WeaponRangeDamageMultiplier(BotRuntime runtime, float distance)
        {
            var rangeScore = WeaponRangeScore(runtime, distance);

            if (rangeScore >= 0.98f)
            {
                return 1f;
            }

            var skill = runtime?.Skill ?? new SkillDefinition();
            var skillFloor = Mathf.Lerp(0.18f, 0.42f, skill.Courage);
            return Mathf.Clamp(skillFloor + rangeScore * Mathf.Lerp(0.45f, 0.72f, skill.Courage), 0.15f, 1f);
        }

        private string ActiveWeaponShortname(BaseCombatEntity bot)
        {
            var player = bot as BasePlayer;
            return player?.GetActiveItem()?.info?.shortname ?? "";
        }

        private float AmmoFraction(BaseCombatEntity bot)
        {
            EnsureBotWeaponLoaded(bot);

            var attacker = GetAttackInterface(bot);

            if (attacker != null)
            {
                try
                {
                    var attackerAmmo = attacker.GetAmmoFraction();

                    if (attackerAmmo > 0f)
                    {
                        return attackerAmmo;
                    }
                }
                catch
                {
                }
            }

            return ActiveWeaponMagazineFraction(bot);
        }

        private bool HasAmmoToShoot(BaseCombatEntity bot)
        {
            EnsureBotWeaponLoaded(bot);
            return AmmoFraction(bot) > MinimumAmmoFractionToShoot;
        }

        private bool EnsureBotWeaponLoaded(BaseCombatEntity bot)
        {
            if (config?.AI?.AutoReloadBotWeapons != true)
            {
                return false;
            }

            var player = bot as BasePlayer;
            var activeItem = player?.GetActiveItem();
            var projectile = activeItem?.GetHeldEntity() as BaseProjectile;
            var magazine = projectile?.primaryMagazine;

            if (projectile == null || magazine == null)
            {
                return false;
            }

            var weaponShortname = activeItem?.info?.shortname ?? "";
            var capacity = MagazineCapacity(magazine, weaponShortname);

            if (magazine.contents > 0)
            {
                return true;
            }

            var ammoType = magazine.ammoType ?? DefaultAmmoForWeapon(weaponShortname);

            if (ammoType == null)
            {
                return false;
            }

            magazine.ammoType = ammoType;
            magazine.contents = capacity;
            projectile.SendNetworkUpdate();
            player.SendNetworkUpdateImmediate();
            return true;
        }

        private float ActiveWeaponMagazineFraction(BaseCombatEntity bot)
        {
            var player = bot as BasePlayer;
            var activeItem = player?.GetActiveItem();
            var projectile = activeItem?.GetHeldEntity() as BaseProjectile;
            var magazine = projectile?.primaryMagazine;

            if (magazine == null)
            {
                return 0f;
            }

            var capacity = MagazineCapacity(magazine, activeItem?.info?.shortname ?? "");
            return capacity <= 0 ? 0f : Mathf.Clamp01(magazine.contents / (float)capacity);
        }

        private int MagazineCapacity(object magazine, string weaponShortname)
        {
            if (magazine != null)
            {
                try
                {
                    var type = magazine.GetType();
                    var field = type.GetField("capacity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);

                    if (field != null)
                    {
                        var value = Convert.ToInt32(field.GetValue(magazine));

                        if (value > 0)
                        {
                            return value;
                        }
                    }

                    var property = type.GetProperty("capacity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);

                    if (property != null)
                    {
                        var value = Convert.ToInt32(property.GetValue(magazine, null));

                        if (value > 0)
                        {
                            return value;
                        }
                    }
                }
                catch
                {
                }
            }

            return DefaultMagazineCapacity(weaponShortname);
        }

        private int DefaultMagazineCapacity(string weaponShortname)
        {
            weaponShortname = (weaponShortname ?? "").ToLowerInvariant();

            if (weaponShortname.Contains("m249") || weaponShortname.Contains("lmg"))
            {
                return 100;
            }

            if (weaponShortname.Contains("pistol") || weaponShortname.Contains("python") || weaponShortname.Contains("revolver"))
            {
                return weaponShortname.Contains("python") ? 6 : 8;
            }

            if (weaponShortname.Contains("shotgun") || weaponShortname.Contains("spas") || weaponShortname.Contains("waterpipe"))
            {
                return 6;
            }

            if (weaponShortname.Contains("thompson"))
            {
                return 20;
            }

            return 30;
        }

        private ItemDefinition DefaultAmmoForWeapon(string weaponShortname)
        {
            weaponShortname = (weaponShortname ?? "").ToLowerInvariant();

            if (weaponShortname.Contains("shotgun") || weaponShortname.Contains("spas") || weaponShortname.Contains("waterpipe"))
            {
                return ItemManager.FindItemDefinition("ammo.shotgun");
            }

            if (weaponShortname.Contains("nailgun"))
            {
                return ItemManager.FindItemDefinition("ammo.nailgun.nails");
            }

            if (weaponShortname.Contains("bow") || weaponShortname.Contains("crossbow"))
            {
                return ItemManager.FindItemDefinition("arrow.wooden");
            }

            if (weaponShortname.Contains("rifle")
                || weaponShortname.Contains("lmg")
                || weaponShortname.Contains("m249")
                || weaponShortname.Contains("m39")
                || weaponShortname.Contains("m16")
                || weaponShortname.Contains("ak"))
            {
                return ItemManager.FindItemDefinition("ammo.rifle");
            }

            return ItemManager.FindItemDefinition("ammo.pistol");
        }

        private bool IsMedicalFireLocked(BotRuntime runtime, float now)
        {
            return runtime != null && runtime.MedicalFireLockedUntil > now;
        }

        private int NearbyAllies(BaseCombatEntity bot, BotRuntime runtime)
        {
            return activeBots.Count(entry => entry.Key != bot && IsLiveBot(entry.Key) && entry.Value?.TeamId == runtime.TeamId && Vector3.Distance(bot.transform.position, entry.Key.transform.position) <= 45f);
        }

        private int NearbyKnownEnemies(BotRuntime runtime, float now)
        {
            if (!squadBlackboards.TryGetValue(runtime.TeamId, out var board))
            {
                return runtime.Memory.TargetUserId == 0 ? 0 : 1;
            }

            return board.KnownEnemies.Values.Count(enemy => now - enemy.LastKnownAt <= config.AI.TargetMemorySeconds);
        }

        private bool IsBotStuck(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            var moved = Vector3.Distance(runtime.Movement.LastPosition, bot.transform.position);

            if (runtime.Movement.LastPosition == Vector3.zero || moved > 1.5f)
            {
                runtime.Movement.LastPosition = bot.transform.position;
                runtime.Movement.LastProgressAt = now;
                runtime.Movement.IsStuck = false;
                runtime.Movement.StuckSince = 0f;
                runtime.Movement.SameActionFailures = 0;
                runtime.ConsecutiveFailedPaths = 0;
                return false;
            }

            var stuck = runtime.CurrentDestination != Vector3.zero
                && Vector3.Distance(bot.transform.position, runtime.CurrentDestination) > 6f
                && now - runtime.Movement.LastProgressAt > config.AI.StuckDetectionSeconds;

            if (!stuck)
            {
                return false;
            }

            if (!runtime.Movement.IsStuck)
            {
                runtime.Movement.IsStuck = true;
                runtime.Movement.StuckSince = now;
                runtime.Movement.LastStuckNotedAt = now;
                runtime.ConsecutiveFailedPaths++;
                runtime.Movement.SameActionFailures++;
            }
            else if (now - runtime.Movement.LastStuckNotedAt > config.AI.StuckDetectionSeconds)
            {
                runtime.Movement.LastStuckNotedAt = now;
                runtime.ConsecutiveFailedPaths++;
                runtime.Movement.SameActionFailures++;
            }

            return true;
        }

        private bool ShouldDespawnHardStuck(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (bot == null || runtime == null || config.AI.HardStuckFailedPathsToDespawn <= 0)
            {
                return false;
            }

            if (!runtime.Movement.IsStuck || runtime.Movement.StuckSince <= 0f || runtime.ConsecutiveFailedPaths < config.AI.HardStuckFailedPathsToDespawn)
            {
                return false;
            }

            return now - runtime.Movement.StuckSince >= Math.Max(10f, config.AI.StuckDetectionSeconds * 3f);
        }

        private BasePlayer NearestRealPlayer(Vector3 position)
        {
            return BasePlayer.activePlayerList
                .Where(player => IsRealPlayer(player) && player.IsConnected && !player.IsDead() && !player.IsSleeping() && !ShouldIgnoreSafeZonePlayer(player))
                .OrderBy(player => Vector3.Distance(position, player.transform.position))
                .FirstOrDefault();
        }

        private bool TryInvokeBool(object target, string methodName, params object[] args)
        {
            var result = TryInvoke(target, methodName, args);
            return result is bool value && value;
        }

        private object TryInvoke(object target, string methodName, params object[] args)
        {
            if (target == null || string.IsNullOrWhiteSpace(methodName))
            {
                return null;
            }

            try
            {
                var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var method = methods.FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == args.Length);

                return method?.Invoke(target, args);
            }
            catch (Exception ex)
            {
                if (config?.Debug?.DebugSpawnDetails == true)
                {
                    PrintWarning($"Reflection call {target.GetType().Name}.{methodName} failed: {ex.GetType().Name}: {ex.Message}");
                }

                return null;
            }
        }

        private string BotRuntimeDiagnostics(BaseCombatEntity bot, BotRuntime runtime)
        {
            if (bot == null || runtime == null)
            {
                return "type=none";
            }

            RefreshCombatProfile(bot, runtime);
            CleanupBotPlacedEntityRefs();
            return $"type={runtime.EntityType}, state={runtime.State}, clan={runtime.ClanTag}, role={runtime.SquadRole}, los={runtime.Memory.HasLineOfSight}, exposure={runtime.Memory.TargetExposureFraction:0.00}({runtime.Memory.TargetVisibleProbePoints}/{runtime.Memory.TargetTotalProbePoints}), weapon={runtime.Combat.WeaponClass}:{runtime.Combat.WeaponShortname}, cover={FormatVectorSafe(runtime.CurrentCover)}, flank={FormatVectorSafe(runtime.CurrentFlankPoint)}, base={runtime.IsInBaseRestrictedArea}, barricades={botPlacedEntities.Count}/{config.AI.MaxActiveBotBarricades}, stuck={runtime.Movement.IsStuck}, nav={BotNavStatus(bot)}, target={BotTargetStatus(bot, runtime)}, prefab={ShortPrefab(runtime.Prefab)}";
        }

        private string BotTargetStatus(BaseCombatEntity bot, BotRuntime runtime)
        {
            var npc = bot as NPCPlayer;

            if (npc != null)
            {
                try
                {
                    if (npc.HasPath)
                    {
                        return "legacy:path";
                    }
                }
                catch
                {
                }
            }

            var navigator = bot.GetComponent<BaseNavigator>() ?? bot.GetComponentInChildren<BaseNavigator>();

            if (navigator != null)
            {
                try
                {
                    if (navigator.HasPath)
                    {
                        return "legacy:path";
                    }
                }
                catch
                {
                }
            }

            if (runtime?.Memory?.Target != null)
            {
                return runtime.Memory.HasLineOfSight ? "visible" : "memory";
            }

            if (runtime?.Memory?.LastHeardAt > 0f)
            {
                return "heard";
            }

            return "none";
        }

        private string BotNavStatus(BaseCombatEntity bot)
        {
            if (bot == null)
            {
                return "none";
            }

            var navigator = bot.GetComponent<BaseNavigator>() ?? bot.GetComponentInChildren<BaseNavigator>();

            if (navigator != null)
            {
                return $"legacy:{navigator.GetType().Name},{LegacyNavDiagnostics(bot)}";
            }

            return "none";
        }

        private string LegacyNavDiagnostics(BaseCombatEntity bot)
        {
            var navigator = bot?.GetComponent<BaseNavigator>() ?? bot?.GetComponentInChildren<BaseNavigator>();
            var npc = bot as NPCPlayer;
            var parts = new List<string>();

            if (npc != null)
            {
                parts.Add($"npcPath={SafeBool(() => npc.HasPath)}");
                parts.Add($"dormant={SafeBool(() => npc.IsDormant)}");
            }

            if (navigator != null)
            {
                parts.Add($"navPath={SafeBool(() => navigator.HasPath)}");
                parts.Add($"stuck={SafeBool(() => navigator.StuckOffNavmesh)}");
                parts.Add($"navType={SafeString(() => navigator.CurrentNavigationType.ToString())}");
                parts.Add($"dest={FormatVectorSafe(SafeVector(() => navigator.Destination))}");
            }
            else
            {
                parts.Add("nav=missing");
            }

            parts.Add(AiConVarStatus());
            parts.Add(LegacyCombatDiagnostics(bot));
            return string.Join(",", parts);
        }

        private string AiConVarStatus()
        {
            try
            {
                return $"aiMove={ConVar.AI.move},aiNavthink={ConVar.AI.navthink},unityNav={ConVar.AI.useUnityNavmesh},navDisabled={Rust.Ai.AiManager.nav_disable}";
            }
            catch
            {
                return "aiConvars=unknown";
            }
        }

        private string LegacyCombatDiagnostics(BaseCombatEntity bot)
        {
            if (bot == null)
            {
                return "combat=none";
            }

            var parts = new List<string>();
            var target = NearestRealPlayer(bot.transform.position);
            var brain = bot.GetComponent<BaseAIBrain>() ?? bot.GetComponentInChildren<BaseAIBrain>();
            var attacker = bot as IAIAttack;

            if (attacker == null)
            {
                try
                {
                    attacker = bot.GetComponent<IAIAttack>() ?? bot.GetComponentInChildren<IAIAttack>(true);
                }
                catch
                {
                }
            }

            if (brain != null)
            {
                parts.Add($"brain={brain.GetType().Name}");
                parts.Add($"state={SafeString(() => brain.CurrentState?.StateType.ToString() ?? "none")}");
                parts.Add($"brainSleep={SafeBool(() => brain.sleeping)}");

                var senses = brain.Senses;

                if (senses != null)
                {
                    parts.Add($"threat={EntityLabel(SafeEntity(() => senses.GetNearestThreat(1f)), target)}");
                    parts.Add($"senseTarget={EntityLabel(SafeEntity(() => senses.GetNearestTarget(1f)), target)}");
                    parts.Add($"los={SafeBool(() => target != null && senses.Memory != null && senses.Memory.IsLOS(target))}");
                }
            }
            else
            {
                parts.Add("brain=missing");
            }

            if (attacker != null)
            {
                parts.Add("attackIf=True");
                parts.Add($"canAttack={SafeBool(() => target != null && attacker.CanAttack(target))}");
                parts.Add($"inRange={LegacyAttackRangeStatus(attacker, target)}");
                parts.Add($"ammo={SafeString(() => attacker.GetAmmoFraction().ToString("0.00", CultureInfo.InvariantCulture))}");
                parts.Add($"cooldown={SafeBool(() => attacker.IsOnCooldown())}");
                parts.Add($"best={EntityLabel(SafeEntity(() => attacker.GetBestTarget()), target)}");
            }
            else
            {
                parts.Add("attackIf=False");
            }

            parts.Add($"held={LegacyHeldWeaponStatus(bot)}");
            return $"combat:{string.Join("/", parts)}";
        }

        private string LegacyAttackRangeStatus(IAIAttack attacker, BaseEntity target)
        {
            if (attacker == null || target == null)
            {
                return "none";
            }

            try
            {
                float distance;
                return $"{attacker.IsTargetInRange(target, out distance)}@{distance.ToString("0.0", CultureInfo.InvariantCulture)}";
            }
            catch
            {
                return "unknown";
            }
        }

        private string LegacyHeldWeaponStatus(BaseCombatEntity bot)
        {
            var player = bot as BasePlayer;

            if (player == null)
            {
                return "none";
            }

            try
            {
                var activeItem = player.GetActiveItem();

                if (activeItem == null)
                {
                    return "none";
                }

                var held = activeItem.GetHeldEntity();
                return $"{activeItem.info?.shortname ?? "unknown"}:{held?.GetType().Name ?? "noheld"}";
            }
            catch
            {
                return "unknown";
            }
        }

        private BaseEntity SafeEntity(Func<BaseEntity> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return null;
            }
        }

        private string EntityLabel(BaseEntity entity, BasePlayer expectedTarget)
        {
            if (entity == null)
            {
                return "none";
            }

            if (expectedTarget != null && entity == expectedTarget)
            {
                return "player";
            }

            return entity.GetType().Name;
        }

        private bool SafeBool(Func<bool> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return false;
            }
        }

        private string SafeString(Func<string> read)
        {
            try
            {
                return read() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private Vector3 SafeVector(Func<Vector3> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return Vector3.zero;
            }
        }

        private string FormatVectorSafe(Vector3 position)
        {
            return position == Vector3.zero ? "none" : FormatVector(position);
        }

        private string EntityTypeName(BaseEntity entity)
        {
            return entity == null ? "none" : entity.GetType().FullName ?? entity.GetType().Name;
        }

        private string ShortPrefab(string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
            {
                return "none";
            }

            var clean = prefab.Replace("\\", "/").Trim('/');
            var index = clean.LastIndexOf('/');
            return index >= 0 ? clean.Substring(index + 1) : clean;
        }

        private bool IsBlockedSafeZoneSpawn(Vector3 position)
        {
            return config?.Spawn?.AvoidSafeZoneSpawns == true && IsSafeZonePosition(position, config.Spawn.SafeZoneSpawnBufferDistance);
        }

        private bool ShouldIgnoreSafeZonePlayer(BasePlayer player)
        {
            return config?.Spawn?.IgnorePlayersInSafeZones == true && PlayerInSafeZone(player);
        }

        private bool PlayerInSafeZone(BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            if (player.InSafeZone())
            {
                return true;
            }

            return IsSafeZonePosition(player.transform.position, 0f);
        }

        private bool IsSafeZonePosition(Vector3 position, float extraDistance)
        {
            var zones = TriggerSafeZone.allSafeZones;

            if (zones == null)
            {
                return false;
            }

            foreach (var zone in zones)
            {
                if (zone == null)
                {
                    continue;
                }

                var collider = zone.triggerCollider;
                var center = collider != null ? collider.bounds.center : zone.transform.position;
                var radius = 25f;

                if (collider != null)
                {
                    var extents = collider.bounds.extents;
                    radius = Mathf.Max(extents.x, extents.z);
                }

                var distance = Distance2D(position, center);

                if (distance <= radius + extraDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private float Distance2D(Vector3 a, Vector3 b)
        {
            var x = a.x - b.x;
            var z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        private bool IsLiveBot(BaseCombatEntity bot)
        {
            return bot != null && !bot.IsDestroyed && !bot.IsDead();
        }

        private BotRuntime RuntimeFor(BaseCombatEntity entity)
        {
            if (entity == null)
            {
                return null;
            }

            activeBots.TryGetValue(entity, out var runtime);
            return runtime;
        }

        private bool IsRealPlayer(BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            return IsSteamId64(player.UserIDString) && RuntimeFor(player) == null;
        }

        private List<BasePlayer> SpawnAnchorPlayers()
        {
            var players = BasePlayer.activePlayerList
                .Where(player => IsRealPlayer(player) && player.IsConnected && !player.IsDead() && !player.IsSleeping() && !ShouldIgnoreSafeZonePlayer(player))
                .ToList();

            var anchor = config?.Spawn?.NearPlayerAnchorNameOrSteamId;

            if (string.IsNullOrWhiteSpace(anchor))
            {
                return players;
            }

            return players
                .Where(player => PlayerMatchesQuery(player, anchor))
                .ToList();
        }

        private bool IsSteamId64(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 17
                && value.StartsWith("7656119", StringComparison.Ordinal)
                && value.All(char.IsDigit);
        }

        private PlayerNpcStats EnsurePlayerStats(BasePlayer player)
        {
            var steamId = player.UserIDString;

            if (!data.players.TryGetValue(steamId, out var stats) || stats == null)
            {
                stats = new PlayerNpcStats { steam_id64 = steamId };
                data.players[steamId] = stats;
            }

            stats.display_name = CleanName(PlayerName(player));
            return stats;
        }

        private BotStats EnsureBotStats(BotRuntime runtime)
        {
            if (!data.bots.TryGetValue(runtime.BotKey, out var stats) || stats == null)
            {
                stats = new BotStats { bot_key = runtime.BotKey };
                data.bots[runtime.BotKey] = stats;
            }

            stats.display_name = runtime.DisplayName;
            stats.kit_name = runtime.KitName;
            stats.skill_tier = runtime.SkillTier;
            stats.clan_key = runtime.ClanKey;
            stats.clan_tag = runtime.ClanTag;
            stats.clan_name = runtime.ClanName;
            stats.team_id = runtime.TeamId;
            stats.squad_role = runtime.SquadRole;
            return stats;
        }

        private BotClanStats EnsureClanStats(BotRuntime runtime)
        {
            if (data.bot_clans == null)
            {
                data.bot_clans = new Dictionary<string, BotClanStats>(StringComparer.OrdinalIgnoreCase);
            }

            var key = string.IsNullOrWhiteSpace(runtime?.ClanKey) ? "raidlands" : runtime.ClanKey;

            if (!data.bot_clans.TryGetValue(key, out var stats) || stats == null)
            {
                stats = new BotClanStats { clan_key = key };
                data.bot_clans[key] = stats;
            }

            stats.clan_tag = runtime?.ClanTag ?? "";
            stats.clan_name = runtime?.ClanName ?? "";
            return stats;
        }

        private void CreateScoreboards()
        {
            if (Scoreboards == null)
            {
                return;
            }

            Scoreboards.Call("CreateScoreboard", ScoreboardNpcKills, "Players with the most roaming bot kills", TopPlayerNpcKills().ToArray());
            Scoreboards.Call("CreateScoreboard", ScoreboardDeathsByNpc, "Players killed most often by roaming bots", TopPlayerDeathsByNpc().ToArray());
            Scoreboards.Call("CreateScoreboard", ScoreboardBotKd, "Roaming bot standings by K/D", TopBotKd().ToArray());
            Scoreboards.Call("CreateScoreboard", ScoreboardBotClanKd, "Roaming bot clan standings by K/D", TopBotClanKd().ToArray());
        }

        private void UpdateScoreboards()
        {
            if (Scoreboards == null)
            {
                return;
            }

            Scoreboards.Call("UpdateScoreboard", ScoreboardNpcKills, TopPlayerNpcKills().ToArray());
            Scoreboards.Call("UpdateScoreboard", ScoreboardDeathsByNpc, TopPlayerDeathsByNpc().ToArray());
            Scoreboards.Call("UpdateScoreboard", ScoreboardBotKd, TopBotKd().ToArray());
            Scoreboards.Call("UpdateScoreboard", ScoreboardBotClanKd, TopBotClanKd().ToArray());
        }

        private IEnumerable<KeyValuePair<string, string>> TopPlayerNpcKills()
        {
            return data.players.Values
                .Where(player => player != null && player.npc_kills > 0)
                .OrderByDescending(player => player.npc_kills)
                .ThenBy(player => player.display_name)
                .Take(15)
                .Select(player => new KeyValuePair<string, string>(ScoreboardName(player.display_name, player.steam_id64), player.npc_kills.ToString(CultureInfo.InvariantCulture)));
        }

        private IEnumerable<KeyValuePair<string, string>> TopPlayerDeathsByNpc()
        {
            return data.players.Values
                .Where(player => player != null && player.deaths_by_npc > 0)
                .OrderByDescending(player => player.deaths_by_npc)
                .ThenBy(player => player.display_name)
                .Take(15)
                .Select(player => new KeyValuePair<string, string>(ScoreboardName(player.display_name, player.steam_id64), player.deaths_by_npc.ToString(CultureInfo.InvariantCulture)));
        }

        private IEnumerable<KeyValuePair<string, string>> TopBotKd()
        {
            return data.bots.Values
                .Where(bot => bot != null && (bot.kills > 0 || bot.deaths > 0))
                .OrderByDescending(bot => BotKdr(bot))
                .ThenByDescending(bot => bot.kills)
                .Take(15)
                .Select(bot => new KeyValuePair<string, string>($"{bot.display_name} [{bot.clan_tag}] ({bot.kit_name}/{bot.skill_tier})", BotKdr(bot).ToString("0.00", CultureInfo.InvariantCulture)));
        }

        private IEnumerable<KeyValuePair<string, string>> TopBotClanKd()
        {
            return data.bot_clans.Values
                .Where(clan => clan != null && (clan.kills > 0 || clan.deaths > 0 || clan.bots_spawned > 0))
                .OrderByDescending(clan => ClanKdr(clan))
                .ThenByDescending(clan => clan.kills)
                .Take(15)
                .Select(clan => new KeyValuePair<string, string>(ClanScoreboardName(clan), $"{ClanKdr(clan).ToString("0.00", CultureInfo.InvariantCulture)} K/D, {clan.kills}/{clan.deaths}, {clan.bots_spawned} spawned"));
        }

        private string ScoreboardName(string displayName, string fallback)
        {
            var name = string.IsNullOrWhiteSpace(displayName) ? fallback : displayName;
            return name.Length <= 32 ? name : name.Substring(0, 32);
        }

        private string BotClanLabel(BotRuntime runtime)
        {
            if (runtime == null)
            {
                return "";
            }

            return string.IsNullOrWhiteSpace(runtime.ClanTag)
                ? runtime.DisplayName
                : $"[{runtime.ClanTag}] {runtime.DisplayName}";
        }

        private float BotKdr(BotStats bot)
        {
            return bot.deaths <= 0 ? bot.kills : (float) bot.kills / bot.deaths;
        }

        private float ClanKdr(BotClanStats clan)
        {
            return clan.deaths <= 0 ? clan.kills : (float) clan.kills / clan.deaths;
        }

        private string ClanScoreboardName(BotClanStats clan)
        {
            var tag = string.IsNullOrWhiteSpace(clan.clan_tag) ? clan.clan_key : clan.clan_tag;
            var name = string.IsNullOrWhiteSpace(clan.clan_name) ? clan.clan_key : clan.clan_name;
            return ScoreboardName($"[{tag}] {name}", clan.clan_key);
        }

        private bool CanAdmin(ConsoleSystem.Arg arg)
        {
            var player = arg?.Connection?.player as BasePlayer;

            if (player == null)
            {
                return true;
            }

            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        private bool ValidateTestAnchor(ConsoleSystem.Arg arg, string requestedAnchor)
        {
            if (string.IsNullOrWhiteSpace(requestedAnchor))
            {
                return true;
            }

            if (FindActivePlayer(requestedAnchor) != null)
            {
                return true;
            }

            var connected = BasePlayer.activePlayerList
                .Where(player => IsRealPlayer(player) && player.IsConnected && !player.IsDead())
                .Select(player => $"{PlayerName(player)} ({player.UserIDString})")
                .Take(10)
                .ToList();
            var connectedText = connected.Count == 0 ? "none" : string.Join(", ", connected);
            Reply(arg, $"No connected player matched anchor '{requestedAnchor}'. Test setup was not changed. Connected players: {connectedText}");
            return false;
        }

        private void Reply(ConsoleSystem.Arg arg, string message)
        {
            var player = arg?.Connection?.player as BasePlayer;

            if (player != null)
            {
                player.ChatMessage(message);
                return;
            }

            Puts(message);
        }

        private string ArgString(ConsoleSystem.Arg arg, int index)
        {
            if (arg?.Args == null || arg.Args.Length <= index)
            {
                return "";
            }

            return arg.Args[index].ToString().Trim();
        }

        private string ArgStringFrom(ConsoleSystem.Arg arg, int index)
        {
            if (arg?.Args == null || arg.Args.Length <= index)
            {
                return "";
            }

            return string.Join(" ", arg.Args.Skip(index).Select(value => value.ToString()).Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        }

        private List<KeyValuePair<BaseCombatEntity, BotRuntime>> ActiveBotEntries()
        {
            CleanupInactiveBots();

            return activeBots
                .Where(entry => IsLiveBot(entry.Key) && entry.Value != null)
                .OrderBy(entry => entry.Value.DisplayName)
                .ThenBy(entry => entry.Key.net?.ID.Value ?? 0UL)
                .ToList();
        }

        private BasePlayer FindActivePlayer(string partialNameOrId)
        {
            var query = (partialNameOrId ?? "").Trim();

            if (query == "")
            {
                return null;
            }

            return BasePlayer.activePlayerList.FirstOrDefault(player =>
                player != null
                    && !player.IsNpc
                    && PlayerMatchesQuery(player, query));
        }

        private bool PlayerMatchesQuery(BasePlayer player, string query)
        {
            if (player == null || string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            var name = PlayerName(player);
            var text = query.Trim();

            return player.UserIDString.Equals(text, StringComparison.OrdinalIgnoreCase)
                || name.Equals(text, StringComparison.OrdinalIgnoreCase)
                || name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string SpawnAnchorLabel()
        {
            var anchor = config?.Spawn?.NearPlayerAnchorNameOrSteamId;
            return string.IsNullOrWhiteSpace(anchor) ? "all" : anchor.Trim();
        }

        private string PlayerName(BasePlayer player)
        {
            return player == null ? "" : player.displayName.ToString();
        }

        private string FormatVector(Vector3 position)
        {
            return $"{position.x:0.0}, {position.y:0.0}, {position.z:0.0}";
        }

        private bool TryReadIntArg(ConsoleSystem.Arg arg, int index, out int value)
        {
            value = 0;

            if (arg?.Args == null || arg.Args.Length <= index)
            {
                return false;
            }

            return int.TryParse(arg.Args[index].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryReadBoolArg(ConsoleSystem.Arg arg, int index, out bool value)
        {
            value = false;
            var text = ArgString(arg, index).ToLowerInvariant();

            if (text == "1" || text == "true" || text == "yes" || text == "on" || text == "enable" || text == "enabled")
            {
                value = true;
                return true;
            }

            if (text == "0" || text == "false" || text == "no" || text == "off" || text == "disable" || text == "disabled")
            {
                value = false;
                return true;
            }

            return false;
        }

        private int TargetPopulation()
        {
            return Clamp(config.TargetPopulation, config.MinAllowedPopulation, config.MaxAllowedPopulation);
        }

        private string NormalizeSpawnMode(string value)
        {
            return TryNormalizeSpawnMode(value, out var mode) ? mode : SpawnModeNearPlayers;
        }

        private bool TryNormalizeSpawnMode(string value, out string mode)
        {
            var normalized = (value ?? "")
                .Trim()
                .ToLowerInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");

            if (normalized == "near" || normalized == "near_player" || normalized == "near_players" || normalized == "players")
            {
                mode = SpawnModeNearPlayers;
                return true;
            }

            if (normalized == SpawnModeRandom)
            {
                mode = SpawnModeRandom;
                return true;
            }

            mode = SpawnModeNearPlayers;
            return false;
        }

        private int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
