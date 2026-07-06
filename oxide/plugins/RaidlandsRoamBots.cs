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
    [Info("RaidlandsRoamBots", "Raidlands", "0.3.50")]
    [Description("Spawns player-like roaming NPCs with Raidlands kits, separate NPC stats, and admin controls.")]
    public class RaidlandsRoamBots : RustPlugin
    {
        private const string AdminPermission = "raidlandsroambots.admin";
        private const string SpawnModeNearPlayers = "near_players";
        private const string SpawnModeRandom = "random";
        private const string TacticalBrainName = "playerlike_tactical_brain";
        private const string DecisionTraceDataFile = "RaidlandsRoamBots/decision_traces.jsonl";
        private const string ObservationTraceDataFile = "RaidlandsRoamBots/player_observation_traces.jsonl";
        private const string BehaviorModelDataFile = "RaidlandsRoamBots/behavior_models";
        private const string TrainingRunDataFile = "RaidlandsRoamBots/training_runs.jsonl";
        private const string StatsDataFile = "RaidlandsRoamBots/stats";
        private const string KitsDataFile = "Kits/kits_data";
        private const string SecretsConfigName = "Secrets.local";
        private const string OpenAiRoamBotApiKeySecret = "OPENAI_ROAM_BOT_API_KEY";
        private const string WoodenBarricadeCoverPrefab = "assets/prefabs/deployable/barricades/barricade.cover.wood_double.prefab";
        private const string F1GrenadePrefab = "assets/prefabs/weapons/f1 grenade/grenade.f1.deployed.prefab";
        private const string SmokeGrenadePrefab = "assets/prefabs/tools/smoke grenade/grenade.smoke.deployed.prefab";
        private const string AdvisorProviderNone = "none";
        private const string AdvisorProviderOpenAiCompatible = "openai_compatible";
        private const string AdvisorProviderWebsiteProxy = "website_proxy";
        private const string AdvisorModeFallbackOnly = "fallback_only";
        private const string AdvisorModeShadow = "shadow";
        private const string AdvisorModeCanary = "canary";
        private const string LearningApplyOff = "off";
        private const string LearningApplyShadow = "shadow";
        private const string LearningApplyGlobal = "global";
        private const string LearningApplyProfiles = "profiles";
        private const string LearningSourceAdminTesters = "admin_testers";
        private const string ObservationContextNone = "none";
        private const string ObservationContextNearestBotSample = "nearest_bot_sample";
        private const string ObservationContextCombatTarget = "combat_target";
        private const float RetreatFallbackReturnFireAfterSeconds = 2.5f;
        private const float RetreatFallbackTimeoutSeconds = 8f;
        private const float MinimumAmmoFractionToShoot = 0.01f;
        private const float BotMinPlayerLikeHealth = 100f;
        private const float BotMaxPlayerLikeHealth = 120f;
        private const float BotDefaultAverageHealth = 110f;
        private const float PlayerLikeDamageScale = 1f;
        private const int ForestSplatMask = 32;
        private const string ScoreboardNpcKills = "NPC Kills";
        private const string ScoreboardDeathsByNpc = "Killed by NPCs";
        private const string ScoreboardBotKd = "Bot K/D";
        private const string ScoreboardBotClanKd = "Bot Clan K/D";
        private const string DebugBotPanelUi = "RaidlandsRoamBots.DebugBotPanel";
        private const string DebugBotPanelTextUi = "RaidlandsRoamBots.DebugBotPanel.Text";
        private const float DebugSidePanelMenuSuppressSeconds = 20f;
        private const float DebugSidePanelMenuCloseSuppressSeconds = 2f;
        private const string AdminPanelUi = "RaidlandsRoamBots.AdminPanel";
        private const string BotAvatarImagePrefix = "raidlands_roambot_avatar_";
        private const int AdminPanelMaximumPopulation = 500;
        private static readonly string[] DebugSidePanelMenuCommandPrefixes =
        {
            "kits.",
            "liveadmin.",
            "sr.",
            "shop.",
            "store.",
            "rewards.",
            "backpack.",
            "trade.",
            "skin.",
            "skinbox.",
            "remove.",
            "bskin.",
            "clan.",
            "clans."
        };
        private static readonly string[] DebugSidePanelMenuChatCommands =
        {
            "kit",
            "kits",
            "shop",
            "store",
            "s",
            "rewards",
            "sr",
            "backpack",
            "backpackgui",
            "trade",
            "skin",
            "skinbox",
            "remove",
            "bskin",
            "clan",
            "clans",
            "admin",
            "liveadmin"
        };
        private static readonly string[] AdminPanelTabs =
        {
            "overview",
            "population",
            "spawn",
            "ai",
            "utility",
            "rewards",
            "advisor",
            "learning",
            "debug",
            "danger"
        };

        [PluginReference]
        private Plugin Kits;

        [PluginReference]
        private Plugin Scoreboards;

        [PluginReference]
        private Plugin ServerRewards;

        [PluginReference]
        private Plugin ImageLibrary;

        private Configuration config;
        private StoredData data;
        private readonly System.Random random = new System.Random();
        private readonly Dictionary<BaseCombatEntity, BotRuntime> activeBots = new Dictionary<BaseCombatEntity, BotRuntime>();
        private readonly HashSet<BaseCombatEntity> despawningBots = new HashSet<BaseCombatEntity>();
        private readonly HashSet<ulong> debugBotPanelViewers = new HashSet<ulong>();
        private readonly HashSet<ulong> adminPanelViewers = new HashSet<ulong>();
        private readonly Dictionary<ulong, float> debugSidePanelSuppressedUntil = new Dictionary<ulong, float>();
        private readonly List<BaseEntity> botPlacedEntities = new List<BaseEntity>();
        private readonly List<BotUtilityEntity> botUtilityEntities = new List<BotUtilityEntity>();
        private readonly List<UtilityDangerZone> utilityDangerZones = new List<UtilityDangerZone>();
        private readonly HashSet<string> registeredAvatarImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, KitEligibility> eligibleKits = new Dictionary<string, KitEligibility>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, SquadBlackboard> squadBlackboards = new Dictionary<int, SquadBlackboard>();
        private readonly Dictionary<string, float> recentSoundBroadcasts = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> consoleLogLastAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<DecisionTrace> pendingDecisionTraces = new List<DecisionTrace>();
        private readonly List<PlayerObservationTrace> pendingObservationTraces = new List<PlayerObservationTrace>();
        private readonly Dictionary<ulong, PlayerObservationEpisode> observationEpisodes = new Dictionary<ulong, PlayerObservationEpisode>();
        private readonly Dictionary<string, PendingAdvisorDecision> pendingAdvisorDecisions = new Dictionary<string, PendingAdvisorDecision>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, PendingBotPlayerDeath> pendingBotPlayerDeaths = new Dictionary<ulong, PendingBotPlayerDeath>();
        private readonly Dictionary<ulong, RecentBotDeath> recentBotDeaths = new Dictionary<ulong, RecentBotDeath>();
        private readonly HashSet<string> missingSecretWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private AdvisorStats advisorStats = new AdvisorStats();
        private BehaviorModelData behaviorModels = new BehaviorModelData();
        private Dictionary<string, string> secrets;
        private string secretsConfigSource;
        private IDecisionAdvisor decisionAdvisor;
        private bool serverRewardsUnavailableWarned;
        private Timer maintainTimer;
        private Timer perceptionTimer;
        private Timer brainTimer;
        private Timer squadTimer;
        private Timer nameplateTimer;
        private Timer scoreboardTimer;
        private Timer decisionTraceSaveTimer;
        private Timer observationTraceSaveTimer;
        private Timer learningTimer;
        private Timer saveTimer;
        private int teamSequence;
        private float spawnRetryBlockedUntil;
        private float lastDecisionTracePruneCheckAt;

        private class Configuration
        {
            public bool Enabled = false;

            [JsonProperty("Target Population")]
            public int TargetPopulation = 50;

            [JsonProperty("Minimum Allowed Population")]
            public int MinAllowedPopulation = 0;

            [JsonProperty("Maximum Allowed Population")]
            public int MaxAllowedPopulation = 200;

            [JsonProperty("Team Size Weights")]
            public Dictionary<string, int> TeamSizeWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["solo"] = 55,
                ["duo"] = 35,
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
                ["casual"] = 34,
                ["average"] = 33,
                ["dangerous"] = 33
            };

            [JsonProperty("Skill Definitions")]
            public Dictionary<string, SkillDefinition> SkillDefinitions = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["casual"] = new SkillDefinition { Health = 100f, DamageScale = 1f, IncomingDamageScale = 1f, ReactionMinSeconds = 0.75f, ReactionMaxSeconds = 1.35f, AimErrorDegrees = 1.5f, AimWarmupSeconds = 2.5f, AimWarmupInitialExtraDegrees = 3f, Aggression = 0.35f, Courage = 0.35f, TacticalNoise = 0.25f },
                ["average"] = new SkillDefinition { Health = 110f, DamageScale = 1f, IncomingDamageScale = 1f, ReactionMinSeconds = 0.4f, ReactionMaxSeconds = 0.85f, AimErrorDegrees = 0.75f, AimWarmupSeconds = 1.75f, AimWarmupInitialExtraDegrees = 1.5f, Aggression = 0.55f, Courage = 0.55f, TacticalNoise = 0.15f },
                ["dangerous"] = new SkillDefinition { Health = 120f, DamageScale = 1f, IncomingDamageScale = 1f, ReactionMinSeconds = 0.18f, ReactionMaxSeconds = 0.45f, AimErrorDegrees = 0.2f, AimWarmupSeconds = 1f, AimWarmupInitialExtraDegrees = 0.4f, Aggression = 0.8f, Courage = 0.8f, TacticalNoise = 0.06f }
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

            [JsonProperty("Player Observation Learning")]
            public PlayerObservationLearningConfig Learning = new PlayerObservationLearningConfig();

            [JsonProperty("Bot Kill Integration")]
            public BotKillIntegrationConfig BotKillIntegration = new BotKillIntegrationConfig();

            public PersistenceConfig Persistence = new PersistenceConfig();

            public DebugConfig Debug = new DebugConfig();

            [JsonProperty("Spawn Failure Retry Seconds")]
            public float SpawnFailureRetrySeconds = 90f;

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
            public bool UseRandomLandFallback = false;

            [JsonProperty("Max Position Attempts")]
            public int MaxPositionAttempts = 80;

            [JsonProperty("Navmesh Sample Distance")]
            public float NavmeshSampleDistance = 18f;

            [JsonProperty("Minimum Above Water")]
            public float MinimumAboveWater = 1.5f;

            [JsonProperty("Require Land Spawns")]
            public bool RequireLandSpawns = true;

            [JsonProperty("Minimum Land Height")]
            public float MinimumLandHeight = 0f;

            [JsonProperty("Maximum Below Terrain Tolerance")]
            public float MaximumBelowTerrainTolerance = 0.75f;

            [JsonProperty("Use Physics Surface Spawn Checks")]
            public bool UsePhysicsSurfaceSpawnChecks = true;

            [JsonProperty("Physics Surface Raycast Height")]
            public float PhysicsSurfaceRaycastHeight = 160f;

            [JsonProperty("Maximum Physical Surface Mismatch")]
            public float MaximumPhysicalSurfaceMismatch = 1.25f;

            [JsonProperty("Runtime Invalid Position Despawn Seconds")]
            public float RuntimeInvalidPositionDespawnSeconds = 2f;

            [JsonProperty("Group Spawn Radius")]
            public float GroupSpawnRadius = 12f;

            [JsonProperty("Use Generated Positions Near Players")]
            public bool UseGeneratedPositionsNearPlayers = true;

            [JsonProperty("Near Player Minimum Distance")]
            public float NearPlayerMinDistance = 80f;

            [JsonProperty("Near Player Maximum Distance")]
            public float NearPlayerMaxDistance = 260f;

            [JsonProperty("Near Player Attempts Per Bot")]
            public int NearPlayerAttempts = 120;

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
            public float Health = BotDefaultAverageHealth;
            public float DamageScale = 1f;
            public float IncomingDamageScale = 1f;
            public float ReactionMinSeconds = 0.4f;
            public float ReactionMaxSeconds = 0.85f;
            public float AimErrorDegrees = 0.75f;
            public float AimWarmupSeconds = 1.75f;
            public float AimWarmupInitialExtraDegrees = 1.5f;
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
            public float FoliageVisionCheckRadius = 0.65f;

            [JsonProperty("Maximum Clear Vision Through Foliage")]
            public float MaximumClearVisionThroughFoliage = 24f;

            [JsonProperty("Foliage Hits To Block Vision")]
            public int FoliageHitsToBlockVision = 2;

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

            [JsonProperty("Allow Bot Clan Wars")]
            public bool AllowBotClanWars = true;

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

            [JsonProperty("Hard Stuck Despawn Seconds")]
            public float HardStuckDespawnSeconds = 180f;

            [JsonProperty("Stuck Memory Seconds")]
            public float StuckMemorySeconds = 75f;

            [JsonProperty("Stuck Memory Radius")]
            public float StuckMemoryRadius = 9f;

            [JsonProperty("Maximum Stuck Memory Points")]
            public int MaxStuckMemoryPoints = 14;

            [JsonProperty("Squad Flank Distance")]
            public float SquadFlankDistance = 24f;

            [JsonProperty("Squad Regroup Distance")]
            public float SquadRegroupDistance = 55f;

            [JsonProperty("Squad Contact Commitment Seconds")]
            public float SquadContactCommitmentSeconds = 35f;

            [JsonProperty("Flank Cooldown Seconds")]
            public float FlankCooldownSeconds = 7f;

            [JsonProperty("Squad Destination Reservation Radius")]
            public float SquadDestinationReservationRadius = 7.5f;

            [JsonProperty("Squad Formation Spacing")]
            public float SquadFormationSpacing = 8f;

            [JsonProperty("Squad Formation Offset Attempts")]
            public int SquadFormationOffsetAttempts = 10;

            [JsonProperty("Grenade Cooldown Seconds")]
            public float GrenadeCooldownSeconds = 30f;

            [JsonProperty("Team Grenade Cooldown Seconds")]
            public float TeamGrenadeCooldownSeconds = 10f;

            [JsonProperty("Grenade Prefab")]
            public string GrenadePrefab = F1GrenadePrefab;

            [JsonProperty("Smoke Grenade Prefab")]
            public string SmokeGrenadePrefab = RaidlandsRoamBots.SmokeGrenadePrefab;

            [JsonProperty("Grenade Minimum Throw Distance")]
            public float GrenadeMinThrowDistance = 12f;

            [JsonProperty("Grenade Maximum Throw Distance")]
            public float GrenadeMaxThrowDistance = 42f;

            [JsonProperty("Smoke Minimum Throw Distance")]
            public float SmokeMinThrowDistance = 10f;

            [JsonProperty("Smoke Maximum Throw Distance")]
            public float SmokeMaxThrowDistance = 55f;

            [JsonProperty("Grenade Throw Velocity")]
            public float GrenadeThrowVelocity = 17f;

            [JsonProperty("Smoke Throw Velocity")]
            public float SmokeThrowVelocity = 14f;

            [JsonProperty("Grenade Fuse Seconds")]
            public float GrenadeFuseSeconds = 3.2f;

            [JsonProperty("Grenade Danger Radius")]
            public float GrenadeDangerRadius = 8f;

            [JsonProperty("Grenade Ally Avoid Radius")]
            public float GrenadeAllyAvoidRadius = 10f;

            [JsonProperty("Grenade Avoidance Seconds")]
            public float GrenadeAvoidanceSeconds = 5f;

            [JsonProperty("Smoke Screen Distance")]
            public float SmokeScreenDistance = 8f;

            [JsonProperty("Maximum Active Bot Utility Projectiles")]
            public int MaxActiveBotUtilityProjectiles = 8;

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

            [JsonProperty("Protection Damage Trigger Percent")]
            public float ProtectionDamageTriggerPercent = 15f;

            [JsonProperty("Protection Damage Window Seconds")]
            public float ProtectionDamageWindowSeconds = 10f;

            [JsonProperty("Protection Commitment Seconds")]
            public float ProtectionCommitmentSeconds = 12f;

            [JsonProperty("Protection Distance Casual")]
            public float ProtectionDistanceCasual = 8f;

            [JsonProperty("Protection Distance Average")]
            public float ProtectionDistanceAverage = 5f;

            [JsonProperty("Protection Distance Dangerous")]
            public float ProtectionDistanceDangerous = 3f;

            [JsonProperty("Long Range Defensive Minimum Distance")]
            public float LongRangeDefensiveMinDistance = 40f;

            [JsonProperty("Long Range Defensive Maximum Distance")]
            public float LongRangeDefensiveMaxDistance = 60f;

            [JsonProperty("Long Range Losing Fight Memory Seconds")]
            public float LongRangeLosingFightMemorySeconds = 10f;

            [JsonProperty("Nearby Defensive Cover Minimum Distance")]
            public float NearbyDefensiveCoverMinDistance = 3f;

            [JsonProperty("Nearby Defensive Cover Maximum Distance")]
            public float NearbyDefensiveCoverMaxDistance = 8f;

            [JsonProperty("Long Range Defensive Health Fraction Casual")]
            public float LongRangeDefensiveHealthFractionCasual = 0.68f;

            [JsonProperty("Long Range Defensive Health Fraction Average")]
            public float LongRangeDefensiveHealthFractionAverage = 0.82f;

            [JsonProperty("Long Range Defensive Health Fraction Dangerous")]
            public float LongRangeDefensiveHealthFractionDangerous = 0.92f;

            [JsonProperty("Full Health Cover Discipline Chance Casual")]
            public float FullHealthCoverDisciplineChanceCasual = 0.55f;

            [JsonProperty("Full Health Cover Discipline Chance Average")]
            public float FullHealthCoverDisciplineChanceAverage = 0.85f;

            [JsonProperty("Full Health Cover Discipline Chance Dangerous")]
            public float FullHealthCoverDisciplineChanceDangerous = 1f;

            [JsonProperty("Healing Return Fire Distance")]
            public float HealingReturnFireDistance = 24f;

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
            public float LowHealthCoverCommitmentSeconds = 24f;

            [JsonProperty("Low Health Cover Heal Per Second")]
            public float LowHealthCoverHealPerSecond = 5f;

            [JsonProperty("Low Health Cover Heal Target Fraction")]
            public float LowHealthCoverHealTargetFraction = 0.96f;

            [JsonProperty("Passive Combat Heal Per Second")]
            public float PassiveCombatHealPerSecond = 1.5f;

            [JsonProperty("Passive Combat Heal Target Fraction")]
            public float PassiveCombatHealTargetFraction = 1f;

            [JsonProperty("Non Syringe Heal Cooldown Seconds")]
            public float NonSyringeHealCooldownSeconds = 3.5f;

            [JsonProperty("Non Syringe Heal Amount")]
            public float NonSyringeHealAmount = 8f;

            [JsonProperty("Allow Shooting While Non Syringe Healing")]
            public bool AllowShootingWhileNonSyringeHealing = true;

            [JsonProperty("Syringe Fire Lock Seconds")]
            public float SyringeFireLockSeconds = 2.2f;

            [JsonProperty("Syringe Cooldown Seconds")]
            public float SyringeCooldownSeconds = 8f;

            [JsonProperty("Syringe Heal Target Fraction")]
            public float SyringeHealTargetFraction = 0.85f;

            [JsonProperty("Grant Bot Medical Items")]
            public bool GrantBotMedicalItems = true;

            [JsonProperty("Bot Medical Item Shortname")]
            public string BotMedicalItemShortname = "syringe.medical";

            [JsonProperty("Bot Medical Item Amount")]
            public int BotMedicalItemAmount = 2;

            [JsonProperty("Bot Medical Loadout")]
            public Dictionary<string, int> BotMedicalLoadout = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["syringe.medical"] = 2,
                ["largemedkit"] = 2,
                ["black.raspberries"] = 4
            };

            [JsonProperty("Use Real Medical Items For Cover Heal")]
            public bool UseRealMedicalItemsForCoverHeal = true;

            [JsonProperty("Real Medical Item Heal Amount")]
            public float RealMedicalItemHealAmount = 15f;

            [JsonProperty("Real Medical Item Shortnames")]
            public List<string> RealMedicalItemShortnames = new List<string> { "syringe.medical", "largemedkit", "bandage", "black.raspberries" };

            [JsonProperty("Barricade Anchor Casual Long Range Threshold")]
            public float BarricadeAnchorLongRangeThresholdCasual = 40f;

            [JsonProperty("Barricade Anchor Average Long Range Threshold")]
            public float BarricadeAnchorLongRangeThresholdAverage = 55f;

            [JsonProperty("Barricade Anchor Dangerous Long Range Threshold")]
            public float BarricadeAnchorLongRangeThresholdDangerous = 70f;

            [JsonProperty("Barricade Anchor Required Hitmarkers Casual")]
            public int BarricadeAnchorRequiredHitmarkersCasual = 2;

            [JsonProperty("Barricade Anchor Required Hitmarkers Average")]
            public int BarricadeAnchorRequiredHitmarkersAverage = 3;

            [JsonProperty("Barricade Anchor Required Hitmarkers Dangerous")]
            public int BarricadeAnchorRequiredHitmarkersDangerous = 5;

            [JsonProperty("Barricade Anchor No Action Push Seconds Casual")]
            public float BarricadeAnchorNoActionPushSecondsCasual = 10f;

            [JsonProperty("Barricade Anchor No Action Push Seconds Average")]
            public float BarricadeAnchorNoActionPushSecondsAverage = 15f;

            [JsonProperty("Barricade Anchor No Action Push Seconds Dangerous")]
            public float BarricadeAnchorNoActionPushSecondsDangerous = 22f;

            [JsonProperty("Prevent Moving In Front Of Anchored Barricade")]
            public bool PreventMovingInFrontOfAnchoredBarricade = true;

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
            public bool Enabled = false;
            public string Provider = AdvisorProviderNone;
            public string Mode = AdvisorModeFallbackOnly;

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

            [JsonProperty("Use Structured Response Schema")]
            public bool UseStructuredResponseSchema = true;

            [JsonProperty("Max Advisor Response Bytes")]
            public int MaxAdvisorResponseBytes = 8192;

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

            [JsonProperty("Require Real Player Within Meters")]
            public float RequireRealPlayerWithinMeters = 350f;

            [JsonProperty("Require Active Player Engagement")]
            public bool RequireActivePlayerEngagement = true;

            [JsonProperty("Player Engagement Memory Seconds")]
            public float PlayerEngagementMemorySeconds = 45f;

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

            [JsonProperty("Max Decision Trace File Megabytes")]
            public int MaxDecisionTraceFileMegabytes = 128;

            [JsonProperty("Max Decision Trace Lines After Prune")]
            public int MaxDecisionTraceLinesAfterPrune = 50000;

            [JsonProperty("Decision Trace Prune Check Interval Seconds")]
            public float DecisionTracePruneCheckIntervalSeconds = 300f;

            [JsonProperty("Max Recent Events In Request")]
            public int MaxRecentEventsInRequest = 24;

            [JsonProperty("Max Candidate Actions")]
            public int MaxCandidateActions = 8;
        }

        private class BotKillIntegrationConfig
        {
            [JsonProperty("Broadcast Player-Like Kill Messages")]
            public bool BroadcastPlayerLikeKillMessages = true;

            [JsonProperty("Suppress DeathNotes For Roam Bot Kills")]
            public bool SuppressDeathNotesForRoamBotKills = true;

            [JsonProperty("Use Bot Avatar As Chat Sender")]
            public bool UseBotAvatarAsChatSender = true;

            [JsonProperty("Chat Format")]
            public string ChatFormat = "<color=#838383>[<color=#80D000>DeathNotes</color>] {message}</color>";

            [JsonProperty("Kill Message")]
            public string KillMessage = "<color=#C4FF00>{killer}</color> killed <color=#C4FF00>{victim}</color> with <color=#C4FF00>{weapon}</color> from <color=#C4FF00>{distance}</color> ({method}).";

            [JsonProperty("Award ServerRewards RP")]
            public bool AwardServerRewardsRp = true;

            [JsonProperty("RP Reward Per Bot Kill")]
            public int ServerRewardsRpPerBotKill = 5;

            [JsonProperty("Tell Killer About RP Reward")]
            public bool TellKillerAboutRpReward = true;

            [JsonProperty("RP Reward Message")]
            public string RpRewardMessage = "<color=#ce422b>[Raidlands]</color> You earned <color=#B6F34A>{rp} RP</color> for killing <color=#C4FF00>{victim}</color>.";

            [JsonProperty("Bot Avatars")]
            public List<BotAvatarConfig> BotAvatars = DefaultBotAvatars();
        }

        private class BotAvatarConfig
        {
            public string Key = "";

            [JsonProperty("Display Name")]
            public string DisplayName = "";

            [JsonProperty("Image Url")]
            public string ImageUrl = "";

            [JsonProperty("Image File")]
            public string ImageFile = "";

            [JsonProperty("Chat User Id")]
            public string ChatUserId = "";
        }

        private static List<BotAvatarConfig> DefaultBotAvatars()
        {
            return new List<BotAvatarConfig>
            {
                new BotAvatarConfig { Key = "raider-red", DisplayName = "Raider Red", ImageFile = "oxide/data/RaidlandsRoamBots/avatars/raider-red.png", ChatUserId = "76561199000010461" },
                new BotAvatarConfig { Key = "scrap-jacket", DisplayName = "Scrap Jacket", ImageFile = "oxide/data/RaidlandsRoamBots/avatars/scrap-jacket.png", ChatUserId = "76561199000010462" },
                new BotAvatarConfig { Key = "hazmat-echo", DisplayName = "Hazmat Echo", ImageFile = "oxide/data/RaidlandsRoamBots/avatars/hazmat-echo.png", ChatUserId = "76561199000010463" },
                new BotAvatarConfig { Key = "roadside-ghost", DisplayName = "Roadside Ghost", ImageFile = "oxide/data/RaidlandsRoamBots/avatars/roadside-ghost.png", ChatUserId = "76561199000010464" },
                new BotAvatarConfig { Key = "launch-watcher", DisplayName = "Launch Watcher", ImageFile = "oxide/data/RaidlandsRoamBots/avatars/launch-watcher.png", ChatUserId = "76561199000010465" }
            };
        }

        private class PlayerObservationLearningConfig
        {
            public bool Enabled = false;

            [JsonProperty("Apply Mode")]
            public string ApplyMode = LearningApplyOff;

            [JsonProperty("Source")]
            public string Source = LearningSourceAdminTesters;

            [JsonProperty("Observed Player SteamIds")]
            public List<string> ObservedPlayerSteamIds = new List<string>();

            [JsonProperty("Player Profile Spawn Weights")]
            public Dictionary<string, int> PlayerProfileSpawnWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Log Observation Traces")]
            public bool LogObservationTraces = true;

            [JsonProperty("Sample Interval Seconds")]
            public float SampleIntervalSeconds = 1f;

            [JsonProperty("Outcome Window Seconds")]
            public float OutcomeWindowSeconds = 10f;

            [JsonProperty("Minimum Global Observations")]
            public int MinimumGlobalObservations = 12;

            [JsonProperty("Minimum Profile Observations")]
            public int MinimumProfileObservations = 8;

            [JsonProperty("Maximum Global Score Delta")]
            public float MaximumGlobalScoreDelta = 24f;

            [JsonProperty("Maximum Profile Score Delta")]
            public float MaximumProfileScoreDelta = 36f;

            [JsonProperty("Shadow Calculates Score Deltas")]
            public bool ShadowCalculatesScoreDeltas = true;

            [JsonProperty("Low Confidence Observation Weight")]
            public float LowConfidenceObservationWeight = 0.35f;

            [JsonProperty("High Confidence Target Context Threshold")]
            public float HighConfidenceTargetContextThreshold = 0.75f;
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
            public bool DebugUiIncludesAnchorPlayer = false;

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

            [JsonProperty("Debug Console Logs")]
            public bool DebugConsoleLogs = false;

            [JsonProperty("Debug Console Log Cooldown Seconds")]
            public float DebugConsoleLogCooldownSeconds = 5f;

            [JsonProperty("Console Warning Cooldown Seconds")]
            public float ConsoleWarningCooldownSeconds = 30f;
        }

        private class StoredData
        {
            public Dictionary<string, PlayerNpcStats> players = new Dictionary<string, PlayerNpcStats>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, BotStats> bots = new Dictionary<string, BotStats>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, BotClanStats> bot_clans = new Dictionary<string, BotClanStats>(StringComparer.OrdinalIgnoreCase);
        }

        private class BehaviorModelData
        {
            public int schema_version = 1;
            public string last_global_build_utc = "";
            public Dictionary<string, LearnedBehaviorModel> skill_models = new Dictionary<string, LearnedBehaviorModel>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, LearnedBehaviorModel> player_profiles = new Dictionary<string, LearnedBehaviorModel>(StringComparer.OrdinalIgnoreCase);
        }

        private class LearnedBehaviorModel
        {
            public string key = "";
            public string model_type = "";
            public string display_name = "";
            public string source_steam_id64 = "";
            public string built_at_utc = "";
            public int observations;
            public int positive_observations;
            public float success_rate;
            public int target_linked_observations;
            public int high_confidence_observations;
            public float average_target_context_confidence;
            public float weighted_success_rate;
            public SkillDefinition skill = new SkillDefinition();
            public Dictionary<string, float> action_score_deltas = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, float> weapon_class_biases = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            public string summary = "";
        }

        private class PlayerObservationTrace
        {
            public string trace_id = "";
            public string source_steam_id64 = "";
            public string source_display_name = "";
            public float started_at;
            public float ended_at;
            public float duration_seconds;
            public string observed_action = "";
            public string weapon_shortname = "";
            public string weapon_class = "";
            public float health_fraction;
            public float distance_to_target;
            public bool had_line_of_sight;
            public float target_exposure_fraction;
            public string target_context_source = "";
            public float target_context_confidence;
            public string target_bot_key = "";
            public string target_bot_name = "";
            public ulong target_net_id;
            public int target_context_events;
            public float sampled_distance_to_nearest_bot = -1f;
            public bool sampled_had_line_of_sight;
            public float sampled_target_exposure_fraction;
            public int sampled_nearby_enemies;
            public float combat_distance_to_target = -1f;
            public bool combat_had_line_of_sight;
            public float combat_target_exposure_fraction;
            public int combat_target_visible_probe_points;
            public int combat_target_total_probe_points;
            public int nearby_allies;
            public int nearby_enemies;
            public int shots_fired;
            public int damage_events_dealt;
            public int damage_events_taken;
            public float damage_dealt;
            public float damage_taken;
            public int explosives_thrown;
            public int melee_swings;
            public int kills;
            public bool died;
            public float response_seconds;
            public float outcome_score;
            public Vector3 start_position;
            public Vector3 end_position;
        }

        private class PlayerObservationEpisode
        {
            public ulong UserId;
            public string UserIdString = "";
            public string DisplayName = "";
            public float StartedAt;
            public float LastSampleAt;
            public Vector3 StartPosition;
            public Vector3 LastPosition;
            public string ObservedAction = "";
            public string WeaponShortname = "";
            public string WeaponClass = "";
            public float HealthFraction;
            public float DistanceToTarget = -1f;
            public bool HadLineOfSight;
            public float TargetExposureFraction;
            public string TargetContextSource = ObservationContextNone;
            public float TargetContextConfidence;
            public string TargetBotKey = "";
            public string TargetBotName = "";
            public ulong TargetNetId;
            public int TargetContextEvents;
            public float TargetContextAt;
            public float SampledDistanceToNearestBot = -1f;
            public bool SampledHadLineOfSight;
            public float SampledTargetExposureFraction;
            public int SampledNearbyEnemies;
            public float CombatDistanceToTarget = -1f;
            public bool CombatHadLineOfSight;
            public float CombatTargetExposureFraction;
            public int CombatTargetVisibleProbePoints;
            public int CombatTargetTotalProbePoints;
            public int NearbyAllies;
            public int NearbyEnemies;
            public int ShotsFired;
            public int DamageEventsDealt;
            public int DamageEventsTaken;
            public float DamageDealt;
            public float DamageTaken;
            public int ExplosivesThrown;
            public int MeleeSwings;
            public int Kills;
            public bool Died;
            public float FirstContactAt;
            public float FirstShotAt;
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
            public string behavior_model_key = "";
            public string player_profile_key = "";
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
            public SkillDefinition BaseSkill;
            public string BehaviorModelKey = "";
            public string PlayerProfileKey = "";
            public string ProfileSourceName = "";
            public string ProfileSourceSteamId = "";
            public string AvatarKey = "";
            public string AvatarDisplayName = "";
            public string AvatarImageName = "";
            public string AvatarChatUserId = "";
            public float LastLearnedScoreDelta;
            public string LastLearnedReason = "none";
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
            public float ProtectionDamageWindowStartedAt;
            public float ProtectionDamageAccumulatedFraction;
            public float ProtectionDamageAwareUntil;
            public float DamageBarricadeAwareUntil;
            public float LastDamageBarricadeAwarenessCheckAt;
            public float LowHealthCoverAwareUntil;
            public float NextLowHealthAwarenessCheckAt;
            public float LastLowHealthHealAt;
            public float LastPassiveHealAt;
            public float NextNonSyringeHealAt;
            public float PendingNonSyringeHealRemaining;
            public float MedicalFireLockedUntil;
            public float NextSyringeHealAt;
            public float PendingMedicalHealRemaining;
            public string LastMedicalUseReason = "none";
            public string LastProtectionReason = "none";
            public bool BarricadeAnchorActive;
            public float BarricadeAnchorStartedAt;
            public float BarricadeAnchorThreatDistance;
            public float BarricadeAnchorNoActionPushAt;
            public int BarricadeAnchorHitmarkers;
            public int BarricadeAnchorRequiredHitmarkers;
            public ulong BarricadeAnchorTargetUserId;
            public float BarricadeAnchorTargetDeadAt;
            public string LastBarricadeAnchorReason = "none";
            public float HoldOutsideBaseUntil;
            public float LastShotAt;
            public float LastDamageTakenAt;
            public float LastDamageDealtAt;
            public float LastSoundInvestigateCommandAt;
            public float LastSoundDebugAt;
            public ulong AimWarmupTargetUserId;
            public float AimWarmupStartedAt;
            public float CurrentAimErrorDegrees;
            public float InvalidPositionSince;
            public string LastBarricadeReason = "none";
            public string LastUtilityReason = "none";
            public string LastFireBlockReason = "none";
            public string LastSightReason = "none";
            public string LastStuckMemoryReason = "none";
            public string LastFormationReason = "none";

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
            public List<StuckDestinationMemory> AvoidedDestinations = new List<StuckDestinationMemory>();
        }

        private class StuckDestinationMemory
        {
            public Vector3 Position;
            public float RecordedAt;
            public int Failures;
            public string Reason = "";
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

        private class ProtectionPlan
        {
            public Vector3 CoverPoint;
            public Vector3 TuckPoint;
            public Vector3 PeekPoint;
            public string Source = "";
            public float Distance;
        }

        private class DecisionContext
        {
            public float LastAdvisorRequestAt;
            public string LastAdvisorStatus = "";
            public string LastAdvisorActionId = "";
            public float LastAdvisorConfidence;
            public string LastAdvisorRationale = "";
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
            public Dictionary<string, Vector3> DestinationClaims = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
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

        private class BotUtilityEntity
        {
            public BaseEntity Entity;
            public string BotKey = "";
            public int TeamId;
            public string UtilityType = "";
            public float SpawnedAt;
        }

        private class UtilityDangerZone
        {
            public Vector3 Position;
            public float Radius;
            public float ExpiresAt;
            public string BotKey = "";
            public int TeamId;
            public string UtilityType = "";
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

        private class HttpDecisionAdvisor : IDecisionAdvisor
        {
            private readonly RaidlandsRoamBots owner;
            private readonly string provider;

            public HttpDecisionAdvisor(RaidlandsRoamBots owner, string provider)
            {
                this.owner = owner;
                this.provider = provider;
            }

            public string Name => provider;
            public bool IsConfigured => owner.IsDecisionAdvisorHttpConfigured(provider);

            public bool TrySubmit(DecisionRequest request, Action<DecisionAdvisorResult> callback)
            {
                if (!IsConfigured)
                {
                    callback?.Invoke(DecisionAdvisorResult.Failure("advisor_not_configured"));
                    return false;
                }

                owner.PruneExpiredAdvisorRequests(Time.realtimeSinceStartup);
                var maxConcurrent = Math.Max(0, owner.config?.DecisionAdvisor?.MaxConcurrentRequests ?? 0);

                if (maxConcurrent <= 0 || owner.PendingAdvisorRequestCount() >= maxConcurrent)
                {
                    callback?.Invoke(DecisionAdvisorResult.Failure("advisor_capacity"));
                    return false;
                }

                var url = owner.AdvisorEndpointUrl(provider);
                var body = provider == AdvisorProviderOpenAiCompatible
                    ? owner.BuildOpenAiCompatibleAdvisorBody(request)
                    : owner.BuildWebsiteProxyAdvisorBody(request);
                var headers = owner.BuildAdvisorHeaders(provider);
                var submittedAt = Time.realtimeSinceStartup;
                var timeout = (float)Math.Max(100, owner.config.DecisionAdvisor.TimeoutMilliseconds);

                owner.SendAdvisorPost(url, body, (code, response) =>
                {
                    callback?.Invoke(owner.ParseAdvisorHttpResponse(provider, request, code, response, submittedAt));
                }, headers, timeout);

                return true;
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
            public int HttpStatusCode;
            public int LatencyMilliseconds;

            public static DecisionAdvisorResult Failure(string status)
            {
                return new DecisionAdvisorResult
                {
                    Success = false,
                    Status = status ?? "advisor_failure"
                };
            }
        }

        private class PendingAdvisorDecision
        {
            public string RequestId = "";
            public string BotKey = "";
            public string FallbackActionId = "";
            public float SubmittedAt;
            public float ExpiresAt;
        }

        private class RecentBotDeath
        {
            public BotRuntime Runtime;
            public float ExpiresAt;
        }

        private class PendingBotPlayerDeath
        {
            public BaseCombatEntity KillerEntity;
            public BotRuntime KillerRuntime;
            public HitInfo HitInfo;
            public float ExpiresAt;
        }

        private class AdvisorStats
        {
            public int TotalRequests;
            public int SubmittedRequests;
            public int SynchronousFailures;
            public int SuccessResponses;
            public int RejectedResponses;
            public int HttpFailures;
            public int InvalidJsonResponses;
            public int InvalidActionResponses;
            public int LowConfidenceResponses;
            public int LateResponses;
            public int TimeoutResponses;
            public int ProximitySkips;
            public int EngagementSkips;
            public string LastStatus = "none";
            public string LastBotKey = "";
            public string LastActionId = "";
            public float LastConfidence;
            public int LastLatencyMilliseconds;
            public string LastRationale = "";
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
            public float AimErrorDegrees;
            public float AimWarmupProgress;
            public float AmmoFraction;
            public bool HasLineOfSight;
            public float TargetExposureFraction;
            public float TargetConfidence;
            public float DistanceToTarget;
            public float NearestRealPlayerDistance;
            public float AdvisorRealPlayerGateMeters;
            public bool EngagedWithRealPlayer;
            public string EngagementSignal = "";
            public float SecondsSinceLastSeen;
            public float SecondsSinceLastHeard;
            public int NearbyAllies;
            public int NearbyKnownEnemies;
            public bool IsStuck;
            public int StuckMemoryPoints;
            public bool TargetIsInsideBaseRestrictedArea;
            public float ProtectionDamageFraction;
            public string ProtectionState = "";
            public string BarricadeAnchorState = "";
            public bool MedicalFireLocked;
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
            public float BaseHeuristicScore;
            public float HeuristicScore;
            public float LearnedScoreDelta;
            public string LearnedModelKey = "";
            public string LearnedReason = "";
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
            public string advisor_action;
            public float advisor_confidence;
            public int advisor_latency_ms;
            public string advisor_rationale;
            public string fallback_reason;
            public string final_action;
            public float final_score;
            public string behavior_model_key;
            public string player_profile_key;
            public float learned_score_delta;
            public string learned_reason;
            public string protection_state;
            public string barricade_anchor_state;
            public string medical_state;
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
            ResetSecretsCache();

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
            LoadBehaviorModels();
        }

        private void OnServerInitialized()
        {
            RefreshEligibleKits();
            RefreshDecisionAdvisor();
            RefreshLearningTimer();
            RegisterBotAvatarImages();
            MaybePruneDecisionTraceFile(DecisionTraceDataPath(), true);
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
            DestroyAdminPanels();
            debugSidePanelSuppressedUntil.Clear();
            pendingAdvisorDecisions.Clear();
            observationEpisodes.Clear();
            learningTimer?.Destroy();
            learningTimer = null;
            if (config?.Persistence?.KillBotsOnPluginUnload == true)
            {
                KillAllBots(!config.Persistence.LeaveCorpses);
            }
            SaveData();
            SaveBehaviorModels();
            FlushDecisionTraces();
            FlushObservationTraces();
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

            if (plugin?.Name == "ServerRewards")
            {
                serverRewardsUnavailableWarned = false;
            }

            if (plugin?.Name == "ImageLibrary")
            {
                registeredAvatarImages.Clear();
                timer.Once(2f, RegisterBotAvatarImages);
            }
        }

        private void RegisterBotAvatarImages()
        {
            if (ImageLibrary == null || config?.BotKillIntegration?.BotAvatars == null)
            {
                return;
            }

            foreach (var avatar in config.BotKillIntegration.BotAvatars)
            {
                if (avatar == null || string.IsNullOrWhiteSpace(avatar.Key))
                {
                    continue;
                }

                var imageName = BotAvatarImageName(avatar.Key);

                if (registeredAvatarImages.Contains(imageName))
                {
                    continue;
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(avatar.ImageUrl))
                    {
                        ImageLibrary.Call("AddImage", avatar.ImageUrl, imageName, 0UL);
                        registeredAvatarImages.Add(imageName);
                        continue;
                    }

                    var imagePath = ResolveBotAvatarImagePath(avatar.ImageFile);

                    if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                    {
                        ImageLibrary.Call("AddImageData", imageName, File.ReadAllBytes(imagePath), 0UL);
                        registeredAvatarImages.Add(imageName);
                    }
                }
                catch (Exception ex)
                {
                    ThrottledWarning($"avatar-register:{imageName}", $"Could not register roam bot avatar '{avatar.Key}' with ImageLibrary: {ex.Message}");
                }
            }
        }

        private string ResolveBotAvatarImagePath(string configuredPath)
        {
            var normalized = NormalizeRelativePath(configuredPath);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            if (Path.IsPathRooted(normalized))
            {
                return normalized;
            }

            const string oxideDataPrefix = "oxide/data/";
            const string dataPrefix = "data/";

            if (normalized.StartsWith(oxideDataPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(Interface.Oxide.DataDirectory, normalized.Substring(oxideDataPrefix.Length).Replace('/', Path.DirectorySeparatorChar));
            }

            if (normalized.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(Interface.Oxide.DataDirectory, normalized.Substring(dataPrefix.Length).Replace('/', Path.DirectorySeparatorChar));
            }

            return Path.Combine(Interface.Oxide.DataDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        }

        private string BotAvatarImageName(string key)
        {
            return $"{BotAvatarImagePrefix}{NormalizeAdminKey(key)}";
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
            observationTraceSaveTimer?.Destroy();
            observationTraceSaveTimer = null;
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

            if (config.SkillDefinitions == null || config.SkillDefinitions.Count == 0)
            {
                config.SkillDefinitions = defaults.SkillDefinitions;
            }

            var normalizedSkillDefinitions = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in config.SkillDefinitions)
            {
                var key = (entry.Key ?? "").Trim().ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                defaults.SkillDefinitions.TryGetValue(key, out var fallback);
                normalizedSkillDefinitions[key] = NormalizeSkillDefinition(key, entry.Value, fallback);
            }

            foreach (var entry in defaults.SkillDefinitions)
            {
                if (!normalizedSkillDefinitions.ContainsKey(entry.Key))
                {
                    normalizedSkillDefinitions[entry.Key] = NormalizeSkillDefinition(entry.Key, null, entry.Value);
                }
            }

            config.SkillDefinitions = normalizedSkillDefinitions;

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
            config.Spawn.PhysicsSurfaceRaycastHeight = Mathf.Clamp(config.Spawn.PhysicsSurfaceRaycastHeight <= 0f ? defaults.Spawn.PhysicsSurfaceRaycastHeight : config.Spawn.PhysicsSurfaceRaycastHeight, 24f, 320f);
            config.Spawn.MaximumPhysicalSurfaceMismatch = Mathf.Clamp(config.Spawn.MaximumPhysicalSurfaceMismatch <= 0f ? defaults.Spawn.MaximumPhysicalSurfaceMismatch : config.Spawn.MaximumPhysicalSurfaceMismatch, 0.25f, 5f);
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
            config.AI.SquadDestinationReservationRadius = Mathf.Clamp(config.AI.SquadDestinationReservationRadius <= 0f ? defaults.AI.SquadDestinationReservationRadius : config.AI.SquadDestinationReservationRadius, 3f, 18f);
            config.AI.SquadFormationSpacing = Mathf.Clamp(config.AI.SquadFormationSpacing <= 0f ? defaults.AI.SquadFormationSpacing : config.AI.SquadFormationSpacing, 3f, 22f);
            config.AI.SquadFormationOffsetAttempts = Clamp(config.AI.SquadFormationOffsetAttempts <= 0 ? defaults.AI.SquadFormationOffsetAttempts : config.AI.SquadFormationOffsetAttempts, 3, 24);
            config.AI.GrenadeCooldownSeconds = Math.Max(1f, config.AI.GrenadeCooldownSeconds);
            config.AI.TeamGrenadeCooldownSeconds = Math.Max(1f, config.AI.TeamGrenadeCooldownSeconds);
            config.AI.GrenadePrefab = F1GrenadePrefab;
            config.AI.SmokeGrenadePrefab = SmokeGrenadePrefab;
            config.AI.GrenadeMinThrowDistance = Mathf.Clamp(config.AI.GrenadeMinThrowDistance <= 0f ? defaults.AI.GrenadeMinThrowDistance : config.AI.GrenadeMinThrowDistance, 4f, 35f);
            config.AI.GrenadeMaxThrowDistance = Mathf.Clamp(config.AI.GrenadeMaxThrowDistance <= 0f ? defaults.AI.GrenadeMaxThrowDistance : config.AI.GrenadeMaxThrowDistance, config.AI.GrenadeMinThrowDistance + 2f, 90f);
            config.AI.SmokeMinThrowDistance = Mathf.Clamp(config.AI.SmokeMinThrowDistance <= 0f ? defaults.AI.SmokeMinThrowDistance : config.AI.SmokeMinThrowDistance, 3f, 35f);
            config.AI.SmokeMaxThrowDistance = Mathf.Clamp(config.AI.SmokeMaxThrowDistance <= 0f ? defaults.AI.SmokeMaxThrowDistance : config.AI.SmokeMaxThrowDistance, config.AI.SmokeMinThrowDistance + 2f, 90f);
            config.AI.GrenadeThrowVelocity = Mathf.Clamp(config.AI.GrenadeThrowVelocity <= 0f ? defaults.AI.GrenadeThrowVelocity : config.AI.GrenadeThrowVelocity, 6f, 35f);
            config.AI.SmokeThrowVelocity = Mathf.Clamp(config.AI.SmokeThrowVelocity <= 0f ? defaults.AI.SmokeThrowVelocity : config.AI.SmokeThrowVelocity, 5f, 30f);
            config.AI.GrenadeFuseSeconds = Mathf.Clamp(config.AI.GrenadeFuseSeconds <= 0f ? defaults.AI.GrenadeFuseSeconds : config.AI.GrenadeFuseSeconds, 1.5f, 8f);
            config.AI.GrenadeDangerRadius = Mathf.Clamp(config.AI.GrenadeDangerRadius <= 0f ? defaults.AI.GrenadeDangerRadius : config.AI.GrenadeDangerRadius, 3f, 18f);
            config.AI.GrenadeAllyAvoidRadius = Mathf.Clamp(config.AI.GrenadeAllyAvoidRadius <= 0f ? defaults.AI.GrenadeAllyAvoidRadius : config.AI.GrenadeAllyAvoidRadius, config.AI.GrenadeDangerRadius, 24f);
            config.AI.GrenadeAvoidanceSeconds = Mathf.Clamp(config.AI.GrenadeAvoidanceSeconds <= 0f ? defaults.AI.GrenadeAvoidanceSeconds : config.AI.GrenadeAvoidanceSeconds, config.AI.GrenadeFuseSeconds, 12f);
            config.AI.SmokeScreenDistance = Mathf.Clamp(config.AI.SmokeScreenDistance <= 0f ? defaults.AI.SmokeScreenDistance : config.AI.SmokeScreenDistance, 2f, 18f);
            config.AI.MaxActiveBotUtilityProjectiles = Clamp(config.AI.MaxActiveBotUtilityProjectiles <= 0 ? defaults.AI.MaxActiveBotUtilityProjectiles : config.AI.MaxActiveBotUtilityProjectiles, 1, 30);
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
            config.AI.ProtectionDamageTriggerPercent = Mathf.Clamp(config.AI.ProtectionDamageTriggerPercent <= 0f ? defaults.AI.ProtectionDamageTriggerPercent : config.AI.ProtectionDamageTriggerPercent, 1f, 95f);
            config.AI.ProtectionDamageWindowSeconds = Mathf.Clamp(config.AI.ProtectionDamageWindowSeconds <= 0f ? defaults.AI.ProtectionDamageWindowSeconds : config.AI.ProtectionDamageWindowSeconds, 1f, 30f);
            config.AI.ProtectionCommitmentSeconds = Mathf.Clamp(config.AI.ProtectionCommitmentSeconds <= 0f ? defaults.AI.ProtectionCommitmentSeconds : config.AI.ProtectionCommitmentSeconds, 2f, 30f);
            config.AI.ProtectionDistanceCasual = Mathf.Clamp(config.AI.ProtectionDistanceCasual <= 0f ? defaults.AI.ProtectionDistanceCasual : config.AI.ProtectionDistanceCasual, 1f, 15f);
            config.AI.ProtectionDistanceAverage = Mathf.Clamp(config.AI.ProtectionDistanceAverage <= 0f ? defaults.AI.ProtectionDistanceAverage : config.AI.ProtectionDistanceAverage, 1f, config.AI.ProtectionDistanceCasual);
            config.AI.ProtectionDistanceDangerous = Mathf.Clamp(config.AI.ProtectionDistanceDangerous <= 0f ? defaults.AI.ProtectionDistanceDangerous : config.AI.ProtectionDistanceDangerous, 1f, config.AI.ProtectionDistanceAverage);
            config.AI.LongRangeDefensiveMinDistance = Mathf.Clamp(config.AI.LongRangeDefensiveMinDistance <= 0f ? defaults.AI.LongRangeDefensiveMinDistance : config.AI.LongRangeDefensiveMinDistance, 20f, 120f);
            config.AI.LongRangeDefensiveMaxDistance = Mathf.Clamp(config.AI.LongRangeDefensiveMaxDistance <= 0f ? defaults.AI.LongRangeDefensiveMaxDistance : config.AI.LongRangeDefensiveMaxDistance, config.AI.LongRangeDefensiveMinDistance, 180f);
            config.AI.LongRangeLosingFightMemorySeconds = Mathf.Clamp(config.AI.LongRangeLosingFightMemorySeconds <= 0f ? defaults.AI.LongRangeLosingFightMemorySeconds : config.AI.LongRangeLosingFightMemorySeconds, 2f, 30f);
            config.AI.NearbyDefensiveCoverMinDistance = Mathf.Clamp(config.AI.NearbyDefensiveCoverMinDistance <= 0f ? defaults.AI.NearbyDefensiveCoverMinDistance : config.AI.NearbyDefensiveCoverMinDistance, 1f, 20f);
            config.AI.NearbyDefensiveCoverMaxDistance = Mathf.Clamp(config.AI.NearbyDefensiveCoverMaxDistance <= 0f ? defaults.AI.NearbyDefensiveCoverMaxDistance : config.AI.NearbyDefensiveCoverMaxDistance, config.AI.NearbyDefensiveCoverMinDistance, 30f);
            config.AI.LongRangeDefensiveHealthFractionCasual = Mathf.Clamp01(config.AI.LongRangeDefensiveHealthFractionCasual);
            config.AI.LongRangeDefensiveHealthFractionAverage = Mathf.Clamp01(config.AI.LongRangeDefensiveHealthFractionAverage);
            config.AI.LongRangeDefensiveHealthFractionDangerous = Mathf.Clamp01(config.AI.LongRangeDefensiveHealthFractionDangerous);
            config.AI.FullHealthCoverDisciplineChanceCasual = Mathf.Clamp01(config.AI.FullHealthCoverDisciplineChanceCasual);
            config.AI.FullHealthCoverDisciplineChanceAverage = Mathf.Clamp01(config.AI.FullHealthCoverDisciplineChanceAverage);
            config.AI.FullHealthCoverDisciplineChanceDangerous = Mathf.Clamp01(config.AI.FullHealthCoverDisciplineChanceDangerous);
            config.AI.HealingReturnFireDistance = Mathf.Clamp(config.AI.HealingReturnFireDistance <= 0f ? defaults.AI.HealingReturnFireDistance : config.AI.HealingReturnFireDistance, 8f, 60f);
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
            config.AI.NonSyringeHealCooldownSeconds = Mathf.Clamp(config.AI.NonSyringeHealCooldownSeconds <= 0f ? defaults.AI.NonSyringeHealCooldownSeconds : config.AI.NonSyringeHealCooldownSeconds, 0.5f, 20f);
            config.AI.NonSyringeHealAmount = Mathf.Clamp(config.AI.NonSyringeHealAmount <= 0f ? defaults.AI.NonSyringeHealAmount : config.AI.NonSyringeHealAmount, 1f, 40f);
            config.AI.SyringeFireLockSeconds = Mathf.Clamp(config.AI.SyringeFireLockSeconds, 0.5f, 6f);
            config.AI.SyringeCooldownSeconds = Mathf.Clamp(config.AI.SyringeCooldownSeconds, 1f, 30f);
            config.AI.SyringeHealTargetFraction = Mathf.Clamp(config.AI.SyringeHealTargetFraction <= 0f ? defaults.AI.SyringeHealTargetFraction : config.AI.SyringeHealTargetFraction, config.AI.LowHealthCoverThreshold, 1f);
            config.AI.BotMedicalItemShortname = string.IsNullOrWhiteSpace(config.AI.BotMedicalItemShortname) ? defaults.AI.BotMedicalItemShortname : config.AI.BotMedicalItemShortname.Trim();
            config.AI.BotMedicalItemAmount = Clamp(config.AI.BotMedicalItemAmount, 0, 12);
            if (config.AI.BotMedicalLoadout == null || config.AI.BotMedicalLoadout.Count == 0)
            {
                config.AI.BotMedicalLoadout = defaults.AI.BotMedicalLoadout;
            }

            config.AI.BotMedicalLoadout = config.AI.BotMedicalLoadout
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value > 0)
                .GroupBy(entry => entry.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => Clamp(group.Sum(entry => entry.Value), 0, 20), StringComparer.OrdinalIgnoreCase);
            config.AI.RealMedicalItemHealAmount = Mathf.Clamp(config.AI.RealMedicalItemHealAmount <= 0f ? defaults.AI.RealMedicalItemHealAmount : config.AI.RealMedicalItemHealAmount, 1f, 50f);
            if (config.AI.RealMedicalItemShortnames == null || config.AI.RealMedicalItemShortnames.Count == 0)
            {
                config.AI.RealMedicalItemShortnames = defaults.AI.RealMedicalItemShortnames;
            }

            config.AI.RealMedicalItemShortnames = config.AI.RealMedicalItemShortnames
                .Where(shortname => !string.IsNullOrWhiteSpace(shortname))
                .Select(shortname => shortname.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            config.AI.BarricadeAnchorLongRangeThresholdCasual = Mathf.Clamp(config.AI.BarricadeAnchorLongRangeThresholdCasual <= 0f ? defaults.AI.BarricadeAnchorLongRangeThresholdCasual : config.AI.BarricadeAnchorLongRangeThresholdCasual, 20f, 140f);
            config.AI.BarricadeAnchorLongRangeThresholdAverage = Mathf.Clamp(config.AI.BarricadeAnchorLongRangeThresholdAverage <= 0f ? defaults.AI.BarricadeAnchorLongRangeThresholdAverage : config.AI.BarricadeAnchorLongRangeThresholdAverage, config.AI.BarricadeAnchorLongRangeThresholdCasual, 160f);
            config.AI.BarricadeAnchorLongRangeThresholdDangerous = Mathf.Clamp(config.AI.BarricadeAnchorLongRangeThresholdDangerous <= 0f ? defaults.AI.BarricadeAnchorLongRangeThresholdDangerous : config.AI.BarricadeAnchorLongRangeThresholdDangerous, config.AI.BarricadeAnchorLongRangeThresholdAverage, 180f);
            config.AI.BarricadeAnchorRequiredHitmarkersCasual = Clamp(config.AI.BarricadeAnchorRequiredHitmarkersCasual <= 0 ? defaults.AI.BarricadeAnchorRequiredHitmarkersCasual : config.AI.BarricadeAnchorRequiredHitmarkersCasual, 1, 10);
            config.AI.BarricadeAnchorRequiredHitmarkersAverage = Clamp(config.AI.BarricadeAnchorRequiredHitmarkersAverage <= 0 ? defaults.AI.BarricadeAnchorRequiredHitmarkersAverage : config.AI.BarricadeAnchorRequiredHitmarkersAverage, config.AI.BarricadeAnchorRequiredHitmarkersCasual, 10);
            config.AI.BarricadeAnchorRequiredHitmarkersDangerous = Clamp(config.AI.BarricadeAnchorRequiredHitmarkersDangerous <= 0 ? defaults.AI.BarricadeAnchorRequiredHitmarkersDangerous : config.AI.BarricadeAnchorRequiredHitmarkersDangerous, config.AI.BarricadeAnchorRequiredHitmarkersAverage, 12);
            config.AI.BarricadeAnchorNoActionPushSecondsCasual = Mathf.Clamp(config.AI.BarricadeAnchorNoActionPushSecondsCasual <= 0f ? defaults.AI.BarricadeAnchorNoActionPushSecondsCasual : config.AI.BarricadeAnchorNoActionPushSecondsCasual, 2f, 45f);
            config.AI.BarricadeAnchorNoActionPushSecondsAverage = Mathf.Clamp(config.AI.BarricadeAnchorNoActionPushSecondsAverage <= 0f ? defaults.AI.BarricadeAnchorNoActionPushSecondsAverage : config.AI.BarricadeAnchorNoActionPushSecondsAverage, config.AI.BarricadeAnchorNoActionPushSecondsCasual, 60f);
            config.AI.BarricadeAnchorNoActionPushSecondsDangerous = Mathf.Clamp(config.AI.BarricadeAnchorNoActionPushSecondsDangerous <= 0f ? defaults.AI.BarricadeAnchorNoActionPushSecondsDangerous : config.AI.BarricadeAnchorNoActionPushSecondsDangerous, config.AI.BarricadeAnchorNoActionPushSecondsAverage, 75f);
            config.AI.PeekOffsetDistance = Mathf.Clamp(config.AI.PeekOffsetDistance, 1f, 8f);
            config.AI.PeekExposureMinSeconds = Math.Max(0.1f, config.AI.PeekExposureMinSeconds);
            config.AI.PeekExposureMaxSeconds = Math.Max(config.AI.PeekExposureMinSeconds, config.AI.PeekExposureMaxSeconds);
            config.AI.TuckMinSeconds = Math.Max(0.1f, config.AI.TuckMinSeconds);
            config.AI.TuckMaxSeconds = Math.Max(config.AI.TuckMinSeconds, config.AI.TuckMaxSeconds);
            config.AI.StuckDetectionSeconds = Math.Max(1f, config.AI.StuckDetectionSeconds);
            config.AI.StuckRecoveryCooldownSeconds = Math.Max(0.5f, config.AI.StuckRecoveryCooldownSeconds);
            config.AI.StuckRecoverySearchRadius = Math.Max(6f, config.AI.StuckRecoverySearchRadius);
            config.AI.HardStuckFailedPathsToDespawn = Clamp(config.AI.HardStuckFailedPathsToDespawn, 0, 200);
            config.AI.HardStuckDespawnSeconds = Mathf.Clamp(config.AI.HardStuckDespawnSeconds, 0f, 900f);
            config.AI.StuckMemorySeconds = Mathf.Clamp(config.AI.StuckMemorySeconds <= 0f ? defaults.AI.StuckMemorySeconds : config.AI.StuckMemorySeconds, 5f, 300f);
            config.AI.StuckMemoryRadius = Mathf.Clamp(config.AI.StuckMemoryRadius <= 0f ? defaults.AI.StuckMemoryRadius : config.AI.StuckMemoryRadius, 3f, 30f);
            config.AI.MaxStuckMemoryPoints = Clamp(config.AI.MaxStuckMemoryPoints <= 0 ? defaults.AI.MaxStuckMemoryPoints : config.AI.MaxStuckMemoryPoints, 1, 50);
            config.AI.BaseAvoidanceRadius = Math.Max(1f, config.AI.BaseAvoidanceRadius);
            config.AI.BaseHoldSeconds = Math.Max(2f, config.AI.BaseHoldSeconds);

            if (config.DecisionAdvisor == null)
            {
                config.DecisionAdvisor = defaults.DecisionAdvisor;
            }

            config.DecisionAdvisor.Provider = NormalizeAdvisorProvider(config.DecisionAdvisor.Provider);
            config.DecisionAdvisor.Mode = NormalizeAdvisorMode(config.DecisionAdvisor.Mode);
            config.DecisionAdvisor.EndpointUrl = string.IsNullOrWhiteSpace(config.DecisionAdvisor.EndpointUrl) && config.DecisionAdvisor.Provider == AdvisorProviderOpenAiCompatible ? defaults.DecisionAdvisor.EndpointUrl : (config.DecisionAdvisor.EndpointUrl ?? "").Trim();
            config.DecisionAdvisor.ApiKey = string.IsNullOrWhiteSpace(config.DecisionAdvisor.ApiKey) && config.DecisionAdvisor.Provider == AdvisorProviderOpenAiCompatible ? defaults.DecisionAdvisor.ApiKey : (config.DecisionAdvisor.ApiKey ?? "").Trim();
            config.DecisionAdvisor.Model = string.IsNullOrWhiteSpace(config.DecisionAdvisor.Model) && config.DecisionAdvisor.Provider == AdvisorProviderOpenAiCompatible ? defaults.DecisionAdvisor.Model : (config.DecisionAdvisor.Model ?? "").Trim();
            config.DecisionAdvisor.MaxAdvisorResponseBytes = Clamp(config.DecisionAdvisor.MaxAdvisorResponseBytes <= 0 ? defaults.DecisionAdvisor.MaxAdvisorResponseBytes : config.DecisionAdvisor.MaxAdvisorResponseBytes, 512, 65536);
            config.DecisionAdvisor.TimeoutMilliseconds = Clamp(config.DecisionAdvisor.TimeoutMilliseconds, 100, 5000);
            config.DecisionAdvisor.DecisionTtlMilliseconds = Clamp(config.DecisionAdvisor.DecisionTtlMilliseconds, 100, 10000);
            config.DecisionAdvisor.MinimumConfidence = Mathf.Clamp01(config.DecisionAdvisor.MinimumConfidence);
            config.DecisionAdvisor.MaxConcurrentRequests = Math.Max(0, config.DecisionAdvisor.MaxConcurrentRequests);
            config.DecisionAdvisor.MinSecondsBetweenRequestsPerBot = Math.Max(0f, config.DecisionAdvisor.MinSecondsBetweenRequestsPerBot);
            config.DecisionAdvisor.RequireRealPlayerWithinMeters = Mathf.Clamp(config.DecisionAdvisor.RequireRealPlayerWithinMeters, 0f, 5000f);
            config.DecisionAdvisor.PlayerEngagementMemorySeconds = Mathf.Clamp(config.DecisionAdvisor.PlayerEngagementMemorySeconds <= 0f ? defaults.DecisionAdvisor.PlayerEngagementMemorySeconds : config.DecisionAdvisor.PlayerEngagementMemorySeconds, 1f, 300f);
            config.DecisionAdvisor.MaxRecentEventsInRequest = Math.Max(0, config.DecisionAdvisor.MaxRecentEventsInRequest);
            config.DecisionAdvisor.MaxCandidateActions = Clamp(config.DecisionAdvisor.MaxCandidateActions, 1, 16);
            config.DecisionAdvisor.MaxDecisionTraceFileMegabytes = Clamp(config.DecisionAdvisor.MaxDecisionTraceFileMegabytes, 0, 4096);
            config.DecisionAdvisor.MaxDecisionTraceLinesAfterPrune = Clamp(config.DecisionAdvisor.MaxDecisionTraceLinesAfterPrune, 0, 1000000);
            config.DecisionAdvisor.DecisionTracePruneCheckIntervalSeconds = Mathf.Clamp(config.DecisionAdvisor.DecisionTracePruneCheckIntervalSeconds <= 0f ? defaults.DecisionAdvisor.DecisionTracePruneCheckIntervalSeconds : config.DecisionAdvisor.DecisionTracePruneCheckIntervalSeconds, 15f, 3600f);

            if (config.Learning == null)
            {
                config.Learning = defaults.Learning;
            }

            config.Learning.ApplyMode = NormalizeLearningApplyMode(config.Learning.ApplyMode);
            config.Learning.Source = string.IsNullOrWhiteSpace(config.Learning.Source) ? LearningSourceAdminTesters : config.Learning.Source.Trim().ToLowerInvariant();
            config.Learning.SampleIntervalSeconds = Mathf.Clamp(config.Learning.SampleIntervalSeconds <= 0f ? defaults.Learning.SampleIntervalSeconds : config.Learning.SampleIntervalSeconds, 0.25f, 10f);
            config.Learning.OutcomeWindowSeconds = Mathf.Clamp(config.Learning.OutcomeWindowSeconds <= 0f ? defaults.Learning.OutcomeWindowSeconds : config.Learning.OutcomeWindowSeconds, 3f, 60f);
            config.Learning.MinimumGlobalObservations = Math.Max(1, config.Learning.MinimumGlobalObservations);
            config.Learning.MinimumProfileObservations = Math.Max(1, config.Learning.MinimumProfileObservations);
            config.Learning.MaximumGlobalScoreDelta = Mathf.Clamp(config.Learning.MaximumGlobalScoreDelta <= 0f ? defaults.Learning.MaximumGlobalScoreDelta : config.Learning.MaximumGlobalScoreDelta, 0f, 80f);
            config.Learning.MaximumProfileScoreDelta = Mathf.Clamp(config.Learning.MaximumProfileScoreDelta <= 0f ? defaults.Learning.MaximumProfileScoreDelta : config.Learning.MaximumProfileScoreDelta, 0f, 100f);
            config.Learning.LowConfidenceObservationWeight = Mathf.Clamp(config.Learning.LowConfidenceObservationWeight <= 0f ? defaults.Learning.LowConfidenceObservationWeight : config.Learning.LowConfidenceObservationWeight, 0.05f, 1f);
            config.Learning.HighConfidenceTargetContextThreshold = Mathf.Clamp(config.Learning.HighConfidenceTargetContextThreshold <= 0f ? defaults.Learning.HighConfidenceTargetContextThreshold : config.Learning.HighConfidenceTargetContextThreshold, 0.1f, 1f);
            config.Learning.ObservedPlayerSteamIds = (config.Learning.ObservedPlayerSteamIds ?? new List<string>())
                .Where(IsSteamId64)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            config.Learning.PlayerProfileSpawnWeights = (config.Learning.PlayerProfileSpawnWeights ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value > 0)
                .GroupBy(entry => NormalizeProfileKey(entry.Key), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => Clamp(group.Sum(entry => entry.Value), 0, 1000), StringComparer.OrdinalIgnoreCase);

            if (config.BotKillIntegration == null)
            {
                config.BotKillIntegration = defaults.BotKillIntegration;
            }

            config.BotKillIntegration.ChatFormat = string.IsNullOrWhiteSpace(config.BotKillIntegration.ChatFormat) ? defaults.BotKillIntegration.ChatFormat : config.BotKillIntegration.ChatFormat.Trim();
            config.BotKillIntegration.KillMessage = string.IsNullOrWhiteSpace(config.BotKillIntegration.KillMessage) ? defaults.BotKillIntegration.KillMessage : config.BotKillIntegration.KillMessage.Trim();
            config.BotKillIntegration.ServerRewardsRpPerBotKill = Clamp(config.BotKillIntegration.ServerRewardsRpPerBotKill, 0, 100000);
            config.BotKillIntegration.RpRewardMessage = string.IsNullOrWhiteSpace(config.BotKillIntegration.RpRewardMessage) ? defaults.BotKillIntegration.RpRewardMessage : config.BotKillIntegration.RpRewardMessage.Trim();
            config.BotKillIntegration.BotAvatars = NormalizeBotAvatars(config.BotKillIntegration.BotAvatars, defaults.BotKillIntegration.BotAvatars);

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
            config.Debug.DebugConsoleLogCooldownSeconds = Mathf.Clamp(config.Debug.DebugConsoleLogCooldownSeconds, 1f, 60f);
            config.Debug.ConsoleWarningCooldownSeconds = Mathf.Clamp(config.Debug.ConsoleWarningCooldownSeconds, 5f, 300f);
        }

        private string NormalizeAdvisorProvider(string provider)
        {
            var normalized = string.IsNullOrWhiteSpace(provider) ? AdvisorProviderNone : provider.Trim().ToLowerInvariant();

            if (normalized == AdvisorProviderOpenAiCompatible || normalized == AdvisorProviderWebsiteProxy)
            {
                return normalized;
            }

            return AdvisorProviderNone;
        }

        private string NormalizeAdvisorMode(string mode)
        {
            var normalized = string.IsNullOrWhiteSpace(mode) ? AdvisorModeFallbackOnly : mode.Trim().ToLowerInvariant();

            if (normalized == AdvisorModeShadow || normalized == AdvisorModeCanary)
            {
                return normalized;
            }

            return AdvisorModeFallbackOnly;
        }

        private List<BotAvatarConfig> NormalizeBotAvatars(List<BotAvatarConfig> source, List<BotAvatarConfig> fallback)
        {
            var normalized = (source ?? new List<BotAvatarConfig>())
                .Where(avatar => avatar != null)
                .Select(avatar => new BotAvatarConfig
                {
                    Key = NormalizeAdminKey(avatar.Key),
                    DisplayName = CleanName(avatar.DisplayName),
                    ImageUrl = (avatar.ImageUrl ?? "").Trim(),
                    ImageFile = NormalizeRelativePath(avatar.ImageFile),
                    ChatUserId = IsSteamId64(avatar.ChatUserId) ? avatar.ChatUserId.Trim() : ""
                })
                .Where(avatar => !string.IsNullOrWhiteSpace(avatar.Key))
                .GroupBy(avatar => avatar.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            if (normalized.Count == 0)
            {
                normalized = (fallback ?? DefaultBotAvatars())
                    .Select(avatar => new BotAvatarConfig
                    {
                        Key = NormalizeAdminKey(avatar.Key),
                        DisplayName = CleanName(avatar.DisplayName),
                        ImageUrl = (avatar.ImageUrl ?? "").Trim(),
                        ImageFile = NormalizeRelativePath(avatar.ImageFile),
                        ChatUserId = IsSteamId64(avatar.ChatUserId) ? avatar.ChatUserId.Trim() : ""
                    })
                    .Where(avatar => !string.IsNullOrWhiteSpace(avatar.Key))
                    .ToList();
            }

            foreach (var avatar in normalized)
            {
                if (string.IsNullOrWhiteSpace(avatar.DisplayName))
                {
                    avatar.DisplayName = avatar.Key;
                }
            }

            return normalized;
        }

        private string NormalizeRelativePath(string path)
        {
            var normalized = (path ?? "").Trim().Replace('\\', '/');

            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            return normalized;
        }

        private void RefreshDecisionAdvisor()
        {
            var provider = NormalizeAdvisorProvider(config?.DecisionAdvisor?.Provider);
            decisionAdvisor = provider == AdvisorProviderOpenAiCompatible || provider == AdvisorProviderWebsiteProxy
                ? (IDecisionAdvisor)new HttpDecisionAdvisor(this, provider)
                : new NullDecisionAdvisor();
        }

        private void ResetSecretsCache()
        {
            secrets = null;
            secretsConfigSource = "";
            missingSecretWarnings.Clear();
        }

        private string ResolveSecretValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var trimmed = value.Trim();

            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return trimmed;
            }

            var key = trimmed.Substring(2, trimmed.Length - 3).Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            var environmentSecret = Environment.GetEnvironmentVariable(key);

            if (!string.IsNullOrWhiteSpace(environmentSecret))
            {
                return environmentSecret.Trim();
            }

            string secret;

            if (LoadSecrets().TryGetValue(key, out secret))
            {
                return (secret ?? "").Trim();
            }

            if (missingSecretWarnings.Add(key))
            {
                PrintWarning($"Secret variable {key} is not configured as an environment variable or in optional oxide/config/{SecretsConfigName}.json.");
            }

            return "";
        }

        private string DescribeSecretSource(string value)
        {
            var trimmed = (value ?? "").Trim();

            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return "oxide/config/RaidlandsRoamBots.json";
            }

            var key = trimmed.Substring(2, trimmed.Length - 3).Trim();

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                return $"environment variable {key}";
            }

            if (LoadSecrets().ContainsKey(key))
            {
                var source = string.IsNullOrWhiteSpace(secretsConfigSource) ? $"oxide/config/{SecretsConfigName}.json" : secretsConfigSource;
                return $"{key} in {source}";
            }

            return $"environment variable {key} or optional oxide/config/{SecretsConfigName}.json";
        }

        private bool HasResolvedAdvisorApiKey()
        {
            return !string.IsNullOrWhiteSpace(ResolveAdvisorApiKey());
        }

        private Dictionary<string, string> LoadSecrets()
        {
            if (secrets != null)
            {
                return secrets;
            }

            secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, $"{SecretsConfigName}.json");
            secretsConfigSource = $"oxide/config/{SecretsConfigName}.json";

            if (!File.Exists(path))
            {
                return secrets;
            }

            try
            {
                var loadedSecrets = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));

                if (loadedSecrets != null)
                {
                    secrets = new Dictionary<string, string>(loadedSecrets, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read oxide/config/{SecretsConfigName}.json: {ex.Message}");
            }

            return secrets;
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

        private void LoadBehaviorModels()
        {
            try
            {
                behaviorModels = Interface.Oxide.DataFileSystem.ReadObject<BehaviorModelData>(BehaviorModelDataFile) ?? new BehaviorModelData();
            }
            catch
            {
                behaviorModels = new BehaviorModelData();
            }

            NormalizeBehaviorModels();
        }

        private void SaveBehaviorModels()
        {
            NormalizeBehaviorModels();
            Interface.Oxide.DataFileSystem.WriteObject(BehaviorModelDataFile, behaviorModels);
        }

        private void NormalizeBehaviorModels()
        {
            if (behaviorModels == null)
            {
                behaviorModels = new BehaviorModelData();
            }

            behaviorModels.schema_version = Math.Max(1, behaviorModels.schema_version);

            if (behaviorModels.skill_models == null)
            {
                behaviorModels.skill_models = new Dictionary<string, LearnedBehaviorModel>(StringComparer.OrdinalIgnoreCase);
            }

            if (behaviorModels.player_profiles == null)
            {
                behaviorModels.player_profiles = new Dictionary<string, LearnedBehaviorModel>(StringComparer.OrdinalIgnoreCase);
            }

            behaviorModels.skill_models = behaviorModels.skill_models
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value != null)
                .GroupBy(entry => NormalizeAdminKey(entry.Key), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => NormalizeLearnedModel(group.Last().Value, group.Key, "skill"), StringComparer.OrdinalIgnoreCase);
            behaviorModels.player_profiles = behaviorModels.player_profiles
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value != null)
                .GroupBy(entry => NormalizeProfileKey(entry.Key), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => NormalizeLearnedModel(group.Last().Value, group.Key, "profile"), StringComparer.OrdinalIgnoreCase);
        }

        private LearnedBehaviorModel NormalizeLearnedModel(LearnedBehaviorModel model, string fallbackKey, string fallbackType)
        {
            model = model ?? new LearnedBehaviorModel();
            model.key = string.Equals(fallbackType, "profile", StringComparison.OrdinalIgnoreCase)
                ? NormalizeProfileKey(string.IsNullOrWhiteSpace(model.key) ? fallbackKey : model.key)
                : NormalizeAdminKey(string.IsNullOrWhiteSpace(model.key) ? fallbackKey : model.key);
            model.model_type = string.IsNullOrWhiteSpace(model.model_type) ? fallbackType : NormalizeAdminKey(model.model_type);
            model.display_name = CleanName(string.IsNullOrWhiteSpace(model.display_name) ? model.key : model.display_name);
            model.source_steam_id64 = IsSteamId64(model.source_steam_id64) ? model.source_steam_id64 : "";
            model.built_at_utc = model.built_at_utc ?? "";
            model.observations = Math.Max(0, model.observations);
            model.positive_observations = Clamp(model.positive_observations, 0, model.observations);
            model.success_rate = model.observations <= 0 ? 0f : Mathf.Clamp01(model.success_rate);
            model.target_linked_observations = Clamp(model.target_linked_observations, 0, model.observations);
            model.high_confidence_observations = Clamp(model.high_confidence_observations, 0, model.observations);
            model.average_target_context_confidence = Mathf.Clamp01(model.average_target_context_confidence);
            model.weighted_success_rate = model.observations <= 0 ? 0f : Mathf.Clamp01(model.weighted_success_rate);
            model.skill = NormalizeSkillDefinition(model.key, CloneSkillDefinition(model.skill), SkillFor(model.key));
            model.action_score_deltas = NormalizeModelFloatMap(model.action_score_deltas, string.Equals(model.model_type, "profile", StringComparison.OrdinalIgnoreCase) ? config.Learning.MaximumProfileScoreDelta : config.Learning.MaximumGlobalScoreDelta);
            model.weapon_class_biases = NormalizeModelFloatMap(model.weapon_class_biases, Math.Max(2f, config.Learning.MaximumProfileScoreDelta * 0.35f));
            model.summary = model.summary ?? "";
            return model;
        }

        private Dictionary<string, float> NormalizeModelFloatMap(Dictionary<string, float> source, float maxAbs)
        {
            maxAbs = Math.Max(0f, maxAbs);
            return (source ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !float.IsNaN(entry.Value) && !float.IsInfinity(entry.Value))
                .GroupBy(entry => NormalizeAdminKey(entry.Key), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => Mathf.Clamp(group.Average(entry => entry.Value), -maxAbs, maxAbs), StringComparer.OrdinalIgnoreCase);
        }

        private string NormalizeLearningApplyMode(string mode)
        {
            switch (NormalizeAdminKey(mode))
            {
                case LearningApplyShadow:
                    return LearningApplyShadow;
                case LearningApplyGlobal:
                    return LearningApplyGlobal;
                case LearningApplyProfiles:
                    return LearningApplyProfiles;
                default:
                    return LearningApplyOff;
            }
        }

        private string NormalizeProfileKey(string key)
        {
            var text = (key ?? "").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var builder = new System.Text.StringBuilder();

            foreach (var character in text)
            {
                if (char.IsLetterOrDigit(character) || character == '_' || character == '-')
                {
                    builder.Append(character);
                }
                else if (character == ' ' || character == '.')
                {
                    builder.Append('_');
                }
            }

            var normalized = builder.ToString().Trim('_', '-');
            return normalized.Length <= 48 ? normalized : normalized.Substring(0, 48).Trim('_', '-');
        }

        private void RefreshLearningTimer()
        {
            learningTimer?.Destroy();
            learningTimer = null;

            if (config?.Learning?.Enabled != true)
            {
                observationEpisodes.Clear();
                return;
            }

            learningTimer = timer.Every(Math.Max(0.25f, config.Learning.SampleIntervalSeconds), ObservationTick);
        }

        private void ObservationTick()
        {
            if (config?.Learning?.Enabled != true)
            {
                observationEpisodes.Clear();
                return;
            }

            var now = Time.realtimeSinceStartup;
            var observed = new HashSet<ulong>();

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!ShouldObservePlayer(player))
                {
                    continue;
                }

                observed.Add(player.userID);
                var episode = GetOrCreateObservationEpisode(player, now);
                UpdateObservationSample(player, episode, now);
            }

            foreach (var entry in observationEpisodes.ToList())
            {
                var episode = entry.Value;

                if (episode == null || !observed.Contains(entry.Key))
                {
                    FlushObservationEpisode(episode, now);
                    observationEpisodes.Remove(entry.Key);
                    continue;
                }

                if (now - episode.StartedAt >= Math.Max(3f, config.Learning.OutcomeWindowSeconds))
                {
                    FlushObservationEpisode(episode, now);
                    observationEpisodes.Remove(entry.Key);
                }
            }
        }

        private bool ShouldObservePlayer(BasePlayer player)
        {
            if (config?.Learning?.Enabled != true || !IsRealPlayer(player) || !player.IsConnected || player.IsDead() || player.IsSleeping() || ShouldIgnoreSafeZonePlayer(player))
            {
                return false;
            }

            if (string.Equals(config.Learning.Source, LearningSourceAdminTesters, StringComparison.OrdinalIgnoreCase))
            {
                return config.Learning.ObservedPlayerSteamIds != null
                    && config.Learning.ObservedPlayerSteamIds.Contains(player.UserIDString, StringComparer.OrdinalIgnoreCase);
            }

            return false;
        }

        private PlayerObservationEpisode GetOrCreateObservationEpisode(BasePlayer player, float now)
        {
            if (player == null)
            {
                return null;
            }

            if (observationEpisodes.TryGetValue(player.userID, out var episode) && episode != null)
            {
                if (now - episode.StartedAt >= Math.Max(3f, config.Learning.OutcomeWindowSeconds))
                {
                    FlushObservationEpisode(episode, now);
                    observationEpisodes.Remove(player.userID);
                }
                else
                {
                    return episode;
                }
            }

            episode = new PlayerObservationEpisode
            {
                UserId = player.userID,
                UserIdString = player.UserIDString,
                DisplayName = CleanName(PlayerName(player)),
                StartedAt = now,
                LastSampleAt = now,
                StartPosition = player.transform.position,
                LastPosition = player.transform.position,
                HealthFraction = PlayerHealthFraction(player)
            };

            observationEpisodes[player.userID] = episode;
            return episode;
        }

        private void UpdateObservationSample(BasePlayer player, PlayerObservationEpisode episode, float now)
        {
            if (player == null || episode == null)
            {
                return;
            }

            var position = player.transform.position;
            var previousPosition = episode.LastPosition == Vector3.zero ? position : episode.LastPosition;
            var previousSampleAt = episode.LastSampleAt <= 0f ? now : episode.LastSampleAt;
            var dt = Math.Max(0.1f, now - previousSampleAt);
            var speed = Distance2D(position, previousPosition) / dt;
            var item = player.GetActiveItem();
            var shortname = item?.info?.shortname ?? "";
            var combatProfile = BuildCombatProfile(shortname);

            episode.DisplayName = CleanName(PlayerName(player));
            episode.WeaponShortname = shortname;
            episode.WeaponClass = combatProfile.WeaponClass;
            episode.HealthFraction = PlayerHealthFraction(player);
            episode.LastSampleAt = now;
            episode.LastPosition = position;
            episode.NearbyAllies = BasePlayer.activePlayerList.Count(other => other != null && other != player && IsRealPlayer(other) && other.IsConnected && !other.IsDead() && !other.IsSleeping() && Vector3.Distance(position, other.transform.position) <= 45f);
            episode.NearbyEnemies = activeBots.Count(entry => IsLiveBot(entry.Key) && Vector3.Distance(position, entry.Key.transform.position) <= config.AI.VisionRange);
            episode.SampledNearbyEnemies = episode.NearbyEnemies;

            var nearestBot = activeBots
                .Where(entry => IsLiveBot(entry.Key))
                .OrderBy(entry => Vector3.Distance(position, entry.Key.transform.position))
                .FirstOrDefault();

            if (nearestBot.Key != null)
            {
                var visibility = ObservationVisibilityFromPlayerToTarget(player, nearestBot.Key);
                episode.SampledDistanceToNearestBot = Vector3.Distance(position, nearestBot.Key.transform.position);
                episode.SampledHadLineOfSight = visibility.CanSee;
                episode.SampledTargetExposureFraction = Mathf.Clamp01(visibility.ExposedFraction);
                ApplyObservationTargetContext(player, episode, nearestBot.Key, nearestBot.Value, ObservationContextNearestBotSample, now, 0.35f, false);

                if (episode.SampledHadLineOfSight && episode.FirstContactAt <= 0f)
                {
                    episode.FirstContactAt = now;
                }

                if (string.IsNullOrWhiteSpace(episode.ObservedAction) && episode.SampledHadLineOfSight)
                {
                    episode.ObservedAction = ActionIdString(TacticalActionId.AcquireVisibleTarget);
                }
            }
            else
            {
                episode.SampledDistanceToNearestBot = -1f;
                episode.SampledHadLineOfSight = false;
                episode.SampledTargetExposureFraction = 0f;

                if (episode.TargetContextConfidence <= 0f)
                {
                    episode.DistanceToTarget = -1f;
                    episode.HadLineOfSight = false;
                    episode.TargetExposureFraction = 0f;
                    episode.TargetContextSource = ObservationContextNone;
                }
            }

            if (string.IsNullOrWhiteSpace(episode.ObservedAction) && speed >= 2.2f)
            {
                episode.ObservedAction = ActionIdString(TacticalActionId.RoamToPoint);
            }
        }

        private void ApplyObservationTargetContext(BasePlayer player, PlayerObservationEpisode episode, BaseCombatEntity targetEntity, BotRuntime targetRuntime, string source, float now, float confidence, bool combatEvent)
        {
            if (player == null || episode == null || targetEntity == null)
            {
                return;
            }

            confidence = Mathf.Clamp01(confidence);
            var visibility = ObservationVisibilityFromPlayerToTarget(player, targetEntity);
            var distance = Vector3.Distance(player.transform.position, targetEntity.transform.position);
            var sourceId = string.IsNullOrWhiteSpace(source) ? ObservationContextNone : NormalizeAdminKey(source);

            if (combatEvent)
            {
                episode.TargetContextEvents++;
                episode.CombatDistanceToTarget = distance;
                episode.CombatHadLineOfSight = visibility.CanSee;
                episode.CombatTargetExposureFraction = Mathf.Clamp01(visibility.ExposedFraction);
                episode.CombatTargetVisibleProbePoints = Math.Max(0, visibility.VisibleProbePoints);
                episode.CombatTargetTotalProbePoints = Math.Max(0, visibility.TotalProbePoints);
            }

            var shouldPromote = confidence >= episode.TargetContextConfidence
                || combatEvent
                || string.IsNullOrWhiteSpace(episode.TargetContextSource)
                || string.Equals(episode.TargetContextSource, ObservationContextNone, StringComparison.OrdinalIgnoreCase);

            if (!shouldPromote)
            {
                return;
            }

            episode.DistanceToTarget = distance;
            episode.HadLineOfSight = visibility.CanSee;
            episode.TargetExposureFraction = Mathf.Clamp01(visibility.ExposedFraction);
            episode.TargetContextSource = sourceId;
            episode.TargetContextConfidence = confidence;
            episode.TargetBotKey = targetRuntime?.BotKey ?? "";
            episode.TargetBotName = targetRuntime?.DisplayName ?? "";
            episode.TargetNetId = NetId(targetEntity);
            episode.TargetContextAt = now;
        }

        private VisionResult ObservationVisibilityFromPlayerToTarget(BasePlayer player, BaseCombatEntity targetEntity)
        {
            var result = new VisionResult();

            if (player == null || targetEntity == null)
            {
                return result;
            }

            var targetPlayer = targetEntity as BasePlayer;

            if (targetPlayer != null)
            {
                return TargetVisibility(player, targetPlayer, config.AI.MinimumExposedTargetFraction);
            }

            result.TotalProbePoints = 1;
            var from = EyePosition(player);
            var to = EyePosition(targetEntity);

            if (from == Vector3.zero || to == Vector3.zero)
            {
                return result;
            }

            var clear = !IsWorldLineBlocked(from, to, player, targetEntity);
            result.VisibleProbePoints = clear ? 1 : 0;
            result.ExposedFraction = clear ? 1f : 0f;
            result.CanSee = clear;
            result.BlockReason = clear ? "visible" : "solid";
            result.BestVisiblePoint = clear ? to : Vector3.zero;
            return result;
        }

        private void RecordLearningPlayerEvent(BasePlayer player, string action, float now, float damageDealt = 0f, float damageTaken = 0f, bool kill = false, bool died = false, bool explosive = false, bool melee = false, BaseCombatEntity targetEntity = null, BotRuntime targetRuntime = null)
        {
            if (!ShouldObservePlayer(player))
            {
                return;
            }

            var episode = GetOrCreateObservationEpisode(player, now);
            UpdateObservationSample(player, episode, now);

            if (episode == null)
            {
                return;
            }

            if (targetEntity != null)
            {
                ApplyObservationTargetContext(player, episode, targetEntity, targetRuntime ?? RuntimeFor(targetEntity), ObservationContextCombatTarget, now, targetEntity is BasePlayer ? 1f : 0.85f, true);
            }

            var actionId = NormalizeAdminKey(action);

            if (!string.IsNullOrWhiteSpace(actionId))
            {
                episode.ObservedAction = actionId;
            }

            if (actionId == ActionIdString(TacticalActionId.AcquireVisibleTarget) || actionId == ActionIdString(TacticalActionId.SuppressTarget))
            {
                episode.ShotsFired++;

                if (episode.FirstShotAt <= 0f)
                {
                    episode.FirstShotAt = now;
                }
            }

            if (damageDealt > 0f)
            {
                episode.DamageDealt += damageDealt;
                episode.DamageEventsDealt++;
            }

            if (damageTaken > 0f)
            {
                episode.DamageTaken += damageTaken;
                episode.DamageEventsTaken++;

                if (string.IsNullOrWhiteSpace(episode.ObservedAction) || actionId == ActionIdString(TacticalActionId.RetreatToCover))
                {
                    episode.ObservedAction = ActionIdString(TacticalActionId.RetreatToCover);
                }
            }

            if (explosive)
            {
                episode.ExplosivesThrown++;
            }

            if (melee)
            {
                episode.MeleeSwings++;
            }

            if (kill)
            {
                episode.Kills++;
            }

            if (died)
            {
                episode.Died = true;
            }
        }

        private float PlayerHealthFraction(BasePlayer player)
        {
            if (player == null)
            {
                return 0f;
            }

            try
            {
                return Mathf.Clamp01(player.Health() / Math.Max(1f, player.MaxHealth()));
            }
            catch
            {
                return Mathf.Clamp01(player.health / 100f);
            }
        }

        private float HitDamageTotal(HitInfo info)
        {
            try
            {
                return Math.Max(0f, info?.damageTypes?.Total() ?? 0f);
            }
            catch
            {
                return 0f;
            }
        }

        private void FlushObservationEpisode(PlayerObservationEpisode episode, float now)
        {
            if (episode == null || config?.Learning?.LogObservationTraces != true)
            {
                return;
            }

            var duration = Math.Max(0f, now - episode.StartedAt);

            if (!HasMeaningfulObservation(episode, duration))
            {
                return;
            }

            var responseSeconds = episode.FirstContactAt > 0f && episode.FirstShotAt > 0f
                ? Math.Max(0f, episode.FirstShotAt - episode.FirstContactAt)
                : 0f;
            var trace = new PlayerObservationTrace
            {
                trace_id = $"{episode.UserIdString}-{episode.StartedAt.ToString("0.000", CultureInfo.InvariantCulture)}",
                source_steam_id64 = episode.UserIdString,
                source_display_name = episode.DisplayName,
                started_at = episode.StartedAt,
                ended_at = now,
                duration_seconds = duration,
                observed_action = NormalizeAdminKey(string.IsNullOrWhiteSpace(episode.ObservedAction) ? ActionIdString(TacticalActionId.RoamToPoint) : episode.ObservedAction),
                weapon_shortname = episode.WeaponShortname ?? "",
                weapon_class = string.IsNullOrWhiteSpace(episode.WeaponClass) ? "default" : episode.WeaponClass,
                health_fraction = Mathf.Clamp01(episode.HealthFraction),
                distance_to_target = episode.DistanceToTarget,
                had_line_of_sight = episode.HadLineOfSight,
                target_exposure_fraction = Mathf.Clamp01(episode.TargetExposureFraction),
                target_context_source = string.IsNullOrWhiteSpace(episode.TargetContextSource) ? ObservationContextNone : episode.TargetContextSource,
                target_context_confidence = Mathf.Clamp01(episode.TargetContextConfidence),
                target_bot_key = episode.TargetBotKey ?? "",
                target_bot_name = episode.TargetBotName ?? "",
                target_net_id = episode.TargetNetId,
                target_context_events = Math.Max(0, episode.TargetContextEvents),
                sampled_distance_to_nearest_bot = episode.SampledDistanceToNearestBot,
                sampled_had_line_of_sight = episode.SampledHadLineOfSight,
                sampled_target_exposure_fraction = Mathf.Clamp01(episode.SampledTargetExposureFraction),
                sampled_nearby_enemies = Math.Max(0, episode.SampledNearbyEnemies),
                combat_distance_to_target = episode.CombatDistanceToTarget,
                combat_had_line_of_sight = episode.CombatHadLineOfSight,
                combat_target_exposure_fraction = Mathf.Clamp01(episode.CombatTargetExposureFraction),
                combat_target_visible_probe_points = Math.Max(0, episode.CombatTargetVisibleProbePoints),
                combat_target_total_probe_points = Math.Max(0, episode.CombatTargetTotalProbePoints),
                nearby_allies = Math.Max(0, episode.NearbyAllies),
                nearby_enemies = Math.Max(0, episode.NearbyEnemies),
                shots_fired = Math.Max(0, episode.ShotsFired),
                damage_events_dealt = Math.Max(0, episode.DamageEventsDealt),
                damage_events_taken = Math.Max(0, episode.DamageEventsTaken),
                damage_dealt = Math.Max(0f, episode.DamageDealt),
                damage_taken = Math.Max(0f, episode.DamageTaken),
                explosives_thrown = Math.Max(0, episode.ExplosivesThrown),
                melee_swings = Math.Max(0, episode.MeleeSwings),
                kills = Math.Max(0, episode.Kills),
                died = episode.Died,
                response_seconds = responseSeconds,
                start_position = episode.StartPosition,
                end_position = episode.LastPosition
            };

            trace.outcome_score = TraceOutcomeScore(trace);
            QueueObservationTrace(trace);
        }

        private bool HasMeaningfulObservation(PlayerObservationEpisode episode, float duration)
        {
            if (episode == null || duration < 0.25f)
            {
                return false;
            }

            return episode.ShotsFired > 0
                || episode.DamageEventsDealt > 0
                || episode.DamageEventsTaken > 0
                || episode.ExplosivesThrown > 0
                || episode.MeleeSwings > 0
                || episode.Kills > 0
                || episode.Died
                || episode.HadLineOfSight && !string.IsNullOrWhiteSpace(episode.ObservedAction);
        }

        private void FlushAllObservationEpisodes(bool writeTraces)
        {
            var now = Time.realtimeSinceStartup;

            foreach (var episode in observationEpisodes.Values.ToList())
            {
                if (writeTraces)
                {
                    FlushObservationEpisode(episode, now);
                }
            }

            observationEpisodes.Clear();

            if (writeTraces)
            {
                FlushObservationTraces();
            }
        }

        private void QueueObservationTrace(PlayerObservationTrace trace)
        {
            if (trace == null || config?.Learning?.Enabled != true || config?.Learning?.LogObservationTraces != true)
            {
                return;
            }

            pendingObservationTraces.Add(trace);

            if (observationTraceSaveTimer == null || observationTraceSaveTimer.Destroyed)
            {
                observationTraceSaveTimer = timer.Once(5f, FlushObservationTraces);
            }
        }

        private void FlushObservationTraces()
        {
            observationTraceSaveTimer = null;

            if (pendingObservationTraces.Count == 0)
            {
                return;
            }

            try
            {
                var dataPath = ObservationTraceDataPath();
                Directory.CreateDirectory(Path.GetDirectoryName(dataPath));
                var lines = pendingObservationTraces.Select(trace => JsonConvert.SerializeObject(trace, Formatting.None)).ToArray();
                File.AppendAllLines(dataPath, lines);
                pendingObservationTraces.Clear();
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not write roam bot player observation traces: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private string ObservationTraceDataPath()
        {
            return Path.Combine(Interface.Oxide.DataFileSystem.Directory, ObservationTraceDataFile);
        }

        private string TrainingRunDataPath()
        {
            return Path.Combine(Interface.Oxide.DataFileSystem.Directory, TrainingRunDataFile);
        }

        private List<PlayerObservationTrace> ReadObservationTraces(int maxLines = 50000)
        {
            var path = ObservationTraceDataPath();

            if (!File.Exists(path))
            {
                return new List<PlayerObservationTrace>();
            }

            try
            {
                var lines = File.ReadLines(path);

                if (maxLines > 0)
                {
                    lines = lines.Reverse().Take(maxLines).Reverse();
                }

                return lines
                    .Select(ParseObservationTraceLine)
                    .Where(trace => trace != null && IsSteamId64(trace.source_steam_id64))
                    .ToList();
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read roam bot player observation traces: {ex.GetType().Name}: {ex.Message}");
                return new List<PlayerObservationTrace>();
            }
        }

        private PlayerObservationTrace ParseObservationTraceLine(string line)
        {
            try
            {
                return JsonConvert.DeserializeObject<PlayerObservationTrace>(line);
            }
            catch
            {
                return null;
            }
        }

        private int BuildGlobalBehaviorModels(List<PlayerObservationTrace> traces, out string summary)
        {
            traces = (traces ?? new List<PlayerObservationTrace>())
                .Where(IsUsableObservationTrace)
                .ToList();

            if (traces.Count < config.Learning.MinimumGlobalObservations)
            {
                summary = $"not_enough_observations count={traces.Count} minimum={config.Learning.MinimumGlobalObservations}";
                return 0;
            }

            var sorted = traces.OrderBy(trace => trace.outcome_score).ToList();
            var half = Math.Max(config.Learning.MinimumGlobalObservations, sorted.Count / 2);
            var casual = sorted.Take(half).ToList();
            var dangerous = sorted.Skip(Math.Max(0, sorted.Count - half)).ToList();
            var average = traces;

            behaviorModels.skill_models["casual"] = BuildModelFromTraces("casual", "skill", "Casual learned behavior", casual, config.Learning.MaximumGlobalScoreDelta);
            behaviorModels.skill_models["average"] = BuildModelFromTraces("average", "skill", "Average learned behavior", average, config.Learning.MaximumGlobalScoreDelta);
            behaviorModels.skill_models["dangerous"] = BuildModelFromTraces("dangerous", "skill", "Dangerous learned behavior", dangerous, config.Learning.MaximumGlobalScoreDelta);
            behaviorModels.last_global_build_utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            SaveBehaviorModels();
            summary = $"global_models=3 observations={traces.Count} casual={casual.Count} average={average.Count} dangerous={dangerous.Count}";
            AppendTrainingRun("global", "ok", summary);
            return 3;
        }

        private bool BuildPlayerProfile(string playerQuery, string profileKey, out string message)
        {
            profileKey = NormalizeProfileKey(profileKey);

            if (string.IsNullOrWhiteSpace(profileKey))
            {
                message = "Profile key must contain letters, numbers, underscores, or dashes.";
                return false;
            }

            FlushAllObservationEpisodes(true);
            var traces = ReadObservationTraces();

            if (!ResolveLearningPlayerIdentity(playerQuery, traces, out var steamId, out var displayName))
            {
                message = $"No connected player or observation trace matched '{playerQuery}'.";
                return false;
            }

            var playerTraces = traces
                .Where(trace => string.Equals(trace.source_steam_id64, steamId, StringComparison.OrdinalIgnoreCase))
                .Where(IsUsableObservationTrace)
                .ToList();

            if (playerTraces.Count < config.Learning.MinimumProfileObservations)
            {
                message = $"Profile '{profileKey}' needs {config.Learning.MinimumProfileObservations} observations for {displayName} ({steamId}); found {playerTraces.Count}.";
                return false;
            }

            var model = BuildModelFromTraces(profileKey, "profile", displayName, playerTraces, config.Learning.MaximumProfileScoreDelta, steamId);
            behaviorModels.player_profiles[profileKey] = model;
            SaveBehaviorModels();
            message = $"Built profile '{profileKey}' from {playerTraces.Count} observations for {displayName} ({steamId}).";
            AppendTrainingRun("profile", "ok", $"{profileKey} steam={steamId} observations={playerTraces.Count}");
            return true;
        }

        private bool ResolveLearningPlayerIdentity(string query, List<PlayerObservationTrace> traces, out string steamId, out string displayName)
        {
            steamId = "";
            displayName = "";
            query = (query ?? "").Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            var active = FindActivePlayer(query);

            if (active != null)
            {
                steamId = active.UserIDString;
                displayName = CleanName(PlayerName(active));
                return true;
            }

            if (IsSteamId64(query))
            {
                var trace = traces?.LastOrDefault(item => string.Equals(item.source_steam_id64, query, StringComparison.OrdinalIgnoreCase));
                steamId = query;
                displayName = CleanName(trace?.source_display_name ?? query);
                return true;
            }

            var match = (traces ?? new List<PlayerObservationTrace>())
                .LastOrDefault(trace => (trace.source_display_name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

            if (match == null)
            {
                return false;
            }

            steamId = match.source_steam_id64;
            displayName = CleanName(match.source_display_name);
            return IsSteamId64(steamId);
        }

        private LearnedBehaviorModel BuildModelFromTraces(string key, string modelType, string displayName, IEnumerable<PlayerObservationTrace> source, float maxDelta, string sourceSteamId = "")
        {
            var traces = (source ?? new List<PlayerObservationTrace>())
                .Where(IsUsableObservationTrace)
                .ToList();
            var positives = traces.Count(trace => trace.outcome_score > 0f);
            var targetLinked = traces.Count(IsCombatLinkedObservationTrace);
            var highConfidence = traces.Count(trace => TraceTargetContextConfidence(trace) >= config.Learning.HighConfidenceTargetContextThreshold);
            var averageContextConfidence = traces.Count <= 0 ? 0f : traces.Average(TraceTargetContextConfidence);
            var weightedSuccessRate = WeightedTraceFraction(traces, trace => trace.outcome_score > 0f);
            var model = new LearnedBehaviorModel
            {
                key = string.Equals(modelType, "profile", StringComparison.OrdinalIgnoreCase) ? NormalizeProfileKey(key) : NormalizeAdminKey(key),
                model_type = modelType,
                display_name = CleanName(displayName),
                source_steam_id64 = IsSteamId64(sourceSteamId) ? sourceSteamId : "",
                built_at_utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                observations = traces.Count,
                positive_observations = positives,
                success_rate = traces.Count <= 0 ? 0f : positives / (float) traces.Count,
                target_linked_observations = targetLinked,
                high_confidence_observations = highConfidence,
                average_target_context_confidence = averageContextConfidence,
                weighted_success_rate = weightedSuccessRate,
                skill = SkillFromTraces(key, modelType, traces),
                action_score_deltas = ActionDeltasFromTraces(traces, maxDelta),
                weapon_class_biases = WeaponClassBiasesFromTraces(traces, Math.Max(2f, maxDelta * 0.35f))
            };

            var topActions = model.action_score_deltas
                .OrderByDescending(entry => Math.Abs(entry.Value))
                .Take(4)
                .Select(entry => $"{entry.Key}:{entry.Value:0.0}");
            model.summary = $"success={model.success_rate:0.00}; weighted={model.weighted_success_rate:0.00}; ctx={model.average_target_context_confidence:0.00}; linked={model.target_linked_observations}/{model.observations}; top={string.Join(",", topActions)}";
            return NormalizeLearnedModel(model, model.key, modelType);
        }

        private bool IsUsableObservationTrace(PlayerObservationTrace trace)
        {
            return trace != null
                && IsSteamId64(trace.source_steam_id64)
                && !string.IsNullOrWhiteSpace(trace.observed_action)
                && trace.duration_seconds >= 0.2f;
        }

        private bool IsCombatLinkedObservationTrace(PlayerObservationTrace trace)
        {
            if (trace == null)
            {
                return false;
            }

            return trace.target_context_events > 0
                || string.Equals(NormalizeAdminKey(trace.target_context_source), ObservationContextCombatTarget, StringComparison.OrdinalIgnoreCase);
        }

        private float TraceTargetContextConfidence(PlayerObservationTrace trace)
        {
            if (trace == null)
            {
                return 0f;
            }

            if (trace.target_context_confidence > 0f)
            {
                return Mathf.Clamp01(trace.target_context_confidence);
            }

            var hasTargetContext = trace.distance_to_target >= 0f
                || trace.sampled_distance_to_nearest_bot >= 0f
                || trace.had_line_of_sight
                || trace.target_exposure_fraction > 0f
                || trace.nearby_enemies > 0;
            var hasCombatOutcome = trace.damage_events_dealt > 0
                || trace.damage_events_taken > 0
                || trace.damage_dealt > 0f
                || trace.damage_taken > 0f
                || trace.kills > 0
                || trace.died;

            if (!hasTargetContext)
            {
                return hasCombatOutcome ? 0.12f : 0f;
            }

            if (hasCombatOutcome && !trace.had_line_of_sight && trace.target_exposure_fraction <= 0f && trace.nearby_enemies <= 0)
            {
                return 0.18f;
            }

            return hasCombatOutcome ? 0.35f : 0.45f;
        }

        private float TraceTrainingWeight(PlayerObservationTrace trace)
        {
            var low = Mathf.Clamp(config?.Learning?.LowConfidenceObservationWeight ?? 0.35f, 0.05f, 1f);
            return Mathf.Clamp(Mathf.Lerp(low, 1f, TraceTargetContextConfidence(trace)), low, 1f);
        }

        private float WeightedTraceSum(IEnumerable<PlayerObservationTrace> traces, Func<PlayerObservationTrace, float> selector)
        {
            if (traces == null || selector == null)
            {
                return 0f;
            }

            var total = 0f;

            foreach (var trace in traces)
            {
                if (trace == null)
                {
                    continue;
                }

                total += TraceTrainingWeight(trace) * selector(trace);
            }

            return total;
        }

        private float WeightedTraceAverage(IEnumerable<PlayerObservationTrace> traces, Func<PlayerObservationTrace, float> selector, float fallback = 0f)
        {
            if (traces == null || selector == null)
            {
                return fallback;
            }

            var total = 0f;
            var weight = 0f;

            foreach (var trace in traces)
            {
                if (trace == null)
                {
                    continue;
                }

                var traceWeight = TraceTrainingWeight(trace);
                total += traceWeight * selector(trace);
                weight += traceWeight;
            }

            return weight <= 0.0001f ? fallback : total / weight;
        }

        private float WeightedTraceFraction(IEnumerable<PlayerObservationTrace> traces, Func<PlayerObservationTrace, bool> predicate)
        {
            if (traces == null || predicate == null)
            {
                return 0f;
            }

            var selected = 0f;
            var total = 0f;

            foreach (var trace in traces)
            {
                if (trace == null)
                {
                    continue;
                }

                var weight = TraceTrainingWeight(trace);
                total += weight;

                if (predicate(trace))
                {
                    selected += weight;
                }
            }

            return total <= 0.0001f ? 0f : Mathf.Clamp01(selected / total);
        }

        private SkillDefinition SkillFromTraces(string key, string modelType, List<PlayerObservationTrace> traces)
        {
            var tier = string.Equals(modelType, "skill", StringComparison.OrdinalIgnoreCase) ? NormalizeAdminKey(key) : "average";
            var fallback = SkillFor(tier);
            var skill = CloneSkillDefinition(fallback) ?? new SkillDefinition();

            if (traces == null || traces.Count == 0)
            {
                return NormalizeSkillDefinition(tier, skill, fallback);
            }

            var successRate = WeightedTraceFraction(traces, trace => trace.outcome_score > 0f);
            var shots = Math.Max(1f, WeightedTraceSum(traces, trace => trace.shots_fired));
            var accuracyProxy = Mathf.Clamp01((WeightedTraceSum(traces, trace => trace.damage_events_dealt) + WeightedTraceSum(traces, trace => trace.kills) * 1.5f) / shots);
            var responseTraces = traces.Where(trace => trace.response_seconds > 0f).Select(trace => trace.response_seconds).ToList();
            var response = responseTraces.Count == 0 ? (skill.ReactionMinSeconds + skill.ReactionMaxSeconds) * 0.5f : WeightedTraceAverage(traces.Where(trace => trace.response_seconds > 0f), trace => trace.response_seconds, (skill.ReactionMinSeconds + skill.ReactionMaxSeconds) * 0.5f);
            var aggression = WeightedTraceFraction(traces, IsAggressiveObservedAction);
            var defensive = WeightedTraceFraction(traces, IsDefensiveObservedAction);

            skill.ReactionMinSeconds = Mathf.Clamp(response * 0.42f, 0.12f, 1.1f);
            skill.ReactionMaxSeconds = Mathf.Clamp(Math.Max(skill.ReactionMinSeconds + 0.12f, response * 0.95f), skill.ReactionMinSeconds, 1.8f);
            skill.AimErrorDegrees = Mathf.Clamp(Mathf.Lerp(3.5f, 0.2f, accuracyProxy), 0.05f, 5.5f);
            skill.AimWarmupSeconds = Mathf.Clamp(Mathf.Lerp(2.8f, 0.8f, successRate), 0.4f, 3.5f);
            skill.AimWarmupInitialExtraDegrees = Mathf.Clamp(Mathf.Lerp(3.2f, 0.35f, accuracyProxy), 0.1f, 5f);
            skill.Aggression = Mathf.Clamp01(skill.Aggression * 0.35f + aggression * 0.65f);
            skill.Courage = Mathf.Clamp01(skill.Courage * 0.35f + (successRate * 0.55f + aggression * 0.25f + (1f - defensive) * 0.20f));
            skill.TacticalNoise = Mathf.Clamp01(Mathf.Lerp(0.34f, 0.05f, successRate) + ActionEntropy(traces) * 0.08f);

            if (string.Equals(tier, "casual", StringComparison.OrdinalIgnoreCase))
            {
                skill.ReactionMinSeconds = Mathf.Clamp(skill.ReactionMinSeconds + 0.18f, 0.18f, 1.3f);
                skill.ReactionMaxSeconds = Mathf.Clamp(skill.ReactionMaxSeconds + 0.28f, skill.ReactionMinSeconds, 1.8f);
                skill.AimErrorDegrees = Mathf.Clamp(skill.AimErrorDegrees + 0.55f, 0.25f, 6f);
                skill.AimWarmupInitialExtraDegrees = Mathf.Clamp(skill.AimWarmupInitialExtraDegrees + 0.65f, 0.25f, 5.5f);
                skill.Aggression = Mathf.Clamp01(skill.Aggression - 0.08f);
                skill.Courage = Mathf.Clamp01(skill.Courage - 0.08f);
                skill.TacticalNoise = Mathf.Clamp01(skill.TacticalNoise + 0.10f);
            }
            else if (string.Equals(tier, "dangerous", StringComparison.OrdinalIgnoreCase))
            {
                skill.ReactionMinSeconds = Mathf.Clamp(skill.ReactionMinSeconds - 0.10f, 0.08f, 0.9f);
                skill.ReactionMaxSeconds = Mathf.Clamp(skill.ReactionMaxSeconds - 0.16f, skill.ReactionMinSeconds + 0.05f, 1.3f);
                skill.AimErrorDegrees = Mathf.Clamp(skill.AimErrorDegrees - 0.35f, 0.05f, 4.5f);
                skill.AimWarmupInitialExtraDegrees = Mathf.Clamp(skill.AimWarmupInitialExtraDegrees - 0.35f, 0.05f, 4f);
                skill.Aggression = Mathf.Clamp01(skill.Aggression + 0.10f);
                skill.Courage = Mathf.Clamp01(skill.Courage + 0.12f);
                skill.TacticalNoise = Mathf.Clamp01(skill.TacticalNoise - 0.08f);
            }

            return NormalizeSkillDefinition(tier, skill, fallback);
        }

        private Dictionary<string, float> ActionDeltasFromTraces(List<PlayerObservationTrace> traces, float maxDelta)
        {
            var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            if (traces == null || traces.Count == 0)
            {
                return result;
            }

            var overallOutcome = WeightedTraceAverage(traces, trace => trace.outcome_score);
            var total = Math.Max(0.0001f, traces.Sum(TraceTrainingWeight));

            foreach (var group in traces.GroupBy(trace => NormalizeAdminKey(trace.observed_action), StringComparer.OrdinalIgnoreCase))
            {
                var action = group.Key;

                if (string.IsNullOrWhiteSpace(action))
                {
                    continue;
                }

                var grouped = group.ToList();
                var count = grouped.Sum(TraceTrainingWeight);
                var frequency = count / total;
                var outcomeDelta = WeightedTraceAverage(grouped, trace => trace.outcome_score) - overallOutcome;
                var frequencyBias = Mathf.Clamp((frequency - 0.12f) * 28f, -6f, 10f);
                var delta = outcomeDelta * 12f + frequencyBias;
                result[action] = Mathf.Clamp(delta, -maxDelta, maxDelta);
            }

            foreach (TacticalActionId actionId in Enum.GetValues(typeof(TacticalActionId)))
            {
                var action = ActionIdString(actionId);

                if (actionId != TacticalActionId.None && !result.ContainsKey(action))
                {
                    result[action] = 0f;
                }
            }

            return result;
        }

        private Dictionary<string, float> WeaponClassBiasesFromTraces(List<PlayerObservationTrace> traces, float maxDelta)
        {
            var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            if (traces == null || traces.Count == 0)
            {
                return result;
            }

            var overallOutcome = WeightedTraceAverage(traces, trace => trace.outcome_score);

            foreach (var group in traces.GroupBy(trace => NormalizeAdminKey(string.IsNullOrWhiteSpace(trace.weapon_class) ? "default" : trace.weapon_class), StringComparer.OrdinalIgnoreCase))
            {
                var weaponClass = group.Key;

                if (string.IsNullOrWhiteSpace(weaponClass))
                {
                    continue;
                }

                result[weaponClass] = Mathf.Clamp((WeightedTraceAverage(group, trace => trace.outcome_score) - overallOutcome) * 8f, -maxDelta, maxDelta);
            }

            return result;
        }

        private float TraceOutcomeScore(PlayerObservationTrace trace)
        {
            if (trace == null)
            {
                return 0f;
            }

            var score = 0f;
            score += trace.damage_dealt * 0.018f;
            score -= trace.damage_taken * 0.014f;
            score += trace.kills * 1.35f;
            score -= trace.died ? 1.15f : 0f;
            score += trace.had_line_of_sight ? Mathf.Clamp01(trace.target_exposure_fraction) * 0.25f * TraceTargetContextConfidence(trace) : 0f;
            score += trace.shots_fired > 0 && trace.damage_events_dealt > 0 ? 0.20f : 0f;
            score += trace.explosives_thrown > 0 ? 0.12f : 0f;
            score -= trace.damage_events_taken > 0 && trace.damage_dealt <= 0f ? 0.20f : 0f;
            return Mathf.Clamp(score, -3f, 4f);
        }

        private bool IsAggressiveObservedAction(PlayerObservationTrace trace)
        {
            var action = NormalizeAdminKey(trace?.observed_action);
            return action == ActionIdString(TacticalActionId.AcquireVisibleTarget)
                || action == ActionIdString(TacticalActionId.PushTarget)
                || action == ActionIdString(TacticalActionId.FlankLeft)
                || action == ActionIdString(TacticalActionId.FlankRight)
                || action == ActionIdString(TacticalActionId.ThrowGrenade)
                || action == ActionIdString(TacticalActionId.WideSwing);
        }

        private bool IsDefensiveObservedAction(PlayerObservationTrace trace)
        {
            var action = NormalizeAdminKey(trace?.observed_action);
            return action == ActionIdString(TacticalActionId.MoveToCover)
                || action == ActionIdString(TacticalActionId.RetreatToCover)
                || action == ActionIdString(TacticalActionId.Tuck)
                || action == ActionIdString(TacticalActionId.ThrowSmoke)
                || action == ActionIdString(TacticalActionId.PlaceBarricade);
        }

        private float ActionEntropy(List<PlayerObservationTrace> traces)
        {
            if (traces == null || traces.Count == 0)
            {
                return 0f;
            }

            var total = traces.Count;
            var entropy = 0f;
            var groups = traces.GroupBy(trace => NormalizeAdminKey(trace.observed_action)).Where(group => !string.IsNullOrWhiteSpace(group.Key)).ToList();

            if (groups.Count <= 1)
            {
                return 0f;
            }

            foreach (var group in groups)
            {
                var p = group.Count() / (float) total;
                entropy -= p * Mathf.Log(Mathf.Max(0.0001f, p), 2f);
            }

            return Mathf.Clamp01(entropy / Mathf.Log(groups.Count, 2f));
        }

        private void AppendTrainingRun(string runType, string status, string summary)
        {
            try
            {
                var path = TrainingRunDataPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var row = new JObject
                {
                    ["built_at_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["run_type"] = runType ?? "",
                    ["status"] = status ?? "",
                    ["summary"] = summary ?? "",
                    ["apply_mode"] = config?.Learning?.ApplyMode ?? LearningApplyOff,
                    ["skill_models"] = behaviorModels?.skill_models?.Count ?? 0,
                    ["player_profiles"] = behaviorModels?.player_profiles?.Count ?? 0
                };
                File.AppendAllLines(path, new[] { row.ToString(Formatting.None) });
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not write roam bot training run summary: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private LearnedBehaviorModel SelectSpawnPlayerProfileModel()
        {
            if (config?.Learning == null
                || config.Learning.ApplyMode != LearningApplyProfiles
                || config.Learning.PlayerProfileSpawnWeights == null
                || config.Learning.PlayerProfileSpawnWeights.Count == 0
                || behaviorModels?.player_profiles == null
                || behaviorModels.player_profiles.Count == 0)
            {
                return null;
            }

            var key = WeightedKey(config.Learning.PlayerProfileSpawnWeights, "");

            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return behaviorModels.player_profiles.TryGetValue(NormalizeProfileKey(key), out var model) ? model : null;
        }

        private LearnedBehaviorModel ActiveSkillBehaviorModel(string skillTier)
        {
            if (config?.Learning == null || behaviorModels?.skill_models == null)
            {
                return null;
            }

            var mode = NormalizeLearningApplyMode(config.Learning.ApplyMode);

            if (mode != LearningApplyShadow && mode != LearningApplyGlobal && mode != LearningApplyProfiles)
            {
                return null;
            }

            var key = NormalizeAdminKey(skillTier);
            return behaviorModels.skill_models.TryGetValue(key, out var model) ? model : null;
        }

        private bool ShouldApplyGlobalSkillModelAtSpawn()
        {
            var mode = NormalizeLearningApplyMode(config?.Learning?.ApplyMode);
            return mode == LearningApplyGlobal || mode == LearningApplyProfiles;
        }

        private SkillDefinition SkillFromBehaviorModel(LearnedBehaviorModel model, string skillTier, SkillDefinition fallback)
        {
            if (model?.skill == null)
            {
                return CloneSkillDefinition(fallback) ?? SkillFor(skillTier);
            }

            return NormalizeSkillDefinition(skillTier, CloneSkillDefinition(model.skill), fallback ?? SkillFor(skillTier));
        }

        private void ApplyLearnedBehaviorScoring(BaseCombatEntity bot, BotRuntime runtime, List<TacticalActionCandidate> candidates, float now)
        {
            if (runtime == null || candidates == null || candidates.Count == 0)
            {
                return;
            }

            var mode = NormalizeLearningApplyMode(config?.Learning?.ApplyMode);
            var shadow = mode == LearningApplyShadow && config.Learning.ShadowCalculatesScoreDeltas;
            var applying = mode == LearningApplyGlobal || mode == LearningApplyProfiles;

            if (!shadow && !applying)
            {
                runtime.LastLearnedScoreDelta = 0f;
                runtime.LastLearnedReason = "off";
                return;
            }

            var model = BehaviorModelForRuntime(runtime, shadow);

            if (model == null)
            {
                runtime.LastLearnedScoreDelta = 0f;
                runtime.LastLearnedReason = "no_model";
                return;
            }

            RefreshCombatProfile(bot, runtime);
            var cap = string.Equals(model.model_type, "profile", StringComparison.OrdinalIgnoreCase)
                ? config.Learning.MaximumProfileScoreDelta
                : config.Learning.MaximumGlobalScoreDelta;
            var weaponClass = NormalizeAdminKey(runtime.Combat?.WeaponClass ?? "default");
            var weaponBias = 0f;

            if (model.weapon_class_biases != null && model.weapon_class_biases.TryGetValue(weaponClass, out var foundWeaponBias))
            {
                weaponBias = foundWeaponBias;
            }

            foreach (var candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }

                var action = NormalizeAdminKey(string.IsNullOrWhiteSpace(candidate.Id) ? ActionIdString(candidate.ActionId) : candidate.Id);
                var delta = 0f;

                if (model.action_score_deltas != null && model.action_score_deltas.TryGetValue(action, out var actionDelta))
                {
                    delta += actionDelta;
                }

                if (IsWeaponBiasedCandidate(candidate.ActionId))
                {
                    delta += weaponBias;
                }

                delta = Mathf.Clamp(delta, -cap, cap);
                candidate.BaseHeuristicScore = candidate.BaseHeuristicScore == 0f ? candidate.HeuristicScore : candidate.BaseHeuristicScore;
                candidate.LearnedScoreDelta = delta;
                candidate.LearnedModelKey = model.key;
                candidate.LearnedReason = $"{model.model_type}:{model.key}:{mode}";

                if (applying)
                {
                    candidate.HeuristicScore = candidate.BaseHeuristicScore + delta;
                }
            }

            var best = candidates.OrderByDescending(candidate => Math.Abs(candidate?.LearnedScoreDelta ?? 0f)).FirstOrDefault();
            runtime.LastLearnedScoreDelta = best?.LearnedScoreDelta ?? 0f;
            runtime.LastLearnedReason = best == null ? "none" : $"{model.model_type}:{model.key}:{mode}";
        }

        private bool IsWeaponBiasedCandidate(TacticalActionId actionId)
        {
            switch (actionId)
            {
                case TacticalActionId.AcquireVisibleTarget:
                case TacticalActionId.PushTarget:
                case TacticalActionId.FlankLeft:
                case TacticalActionId.FlankRight:
                case TacticalActionId.ThrowGrenade:
                    return true;
                default:
                    return false;
            }
        }

        private LearnedBehaviorModel BehaviorModelForRuntime(BotRuntime runtime, bool includeShadow)
        {
            if (runtime == null || behaviorModels == null)
            {
                return null;
            }

            var mode = NormalizeLearningApplyMode(config?.Learning?.ApplyMode);

            if (mode == LearningApplyOff || mode == LearningApplyShadow && !includeShadow)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(runtime.PlayerProfileKey)
                && behaviorModels.player_profiles != null
                && behaviorModels.player_profiles.TryGetValue(NormalizeProfileKey(runtime.PlayerProfileKey), out var profileModel))
            {
                return profileModel;
            }

            if (!string.IsNullOrWhiteSpace(runtime.BehaviorModelKey))
            {
                if (behaviorModels.skill_models != null && behaviorModels.skill_models.TryGetValue(NormalizeAdminKey(runtime.BehaviorModelKey), out var skillModel))
                {
                    return skillModel;
                }

                if (behaviorModels.player_profiles != null && behaviorModels.player_profiles.TryGetValue(NormalizeProfileKey(runtime.BehaviorModelKey), out var profileByBehaviorKey))
                {
                    return profileByBehaviorKey;
                }
            }

            return ActiveSkillBehaviorModel(runtime.SkillTier);
        }

        private string LearningRuntimeStatus(BotRuntime runtime)
        {
            if (runtime == null)
            {
                return "none";
            }

            var model = string.IsNullOrWhiteSpace(runtime.BehaviorModelKey) ? "none" : runtime.BehaviorModelKey;
            var profile = string.IsNullOrWhiteSpace(runtime.PlayerProfileKey) ? "" : $"/{runtime.PlayerProfileKey}";
            var source = string.IsNullOrWhiteSpace(runtime.ProfileSourceName) ? "" : $" src={runtime.ProfileSourceName}";
            var delta = runtime.LastLearnedScoreDelta.ToString("0.0", CultureInfo.InvariantCulture);
            return $"{NormalizeLearningApplyMode(config?.Learning?.ApplyMode)}:{model}{profile} delta={delta} reason={runtime.LastLearnedReason}{source}";
        }

        private string LearningStatusLine()
        {
            var traceCount = CountObservationTraceLines();
            return $"Raidlands roam bot learning: enabled={config.Learning.Enabled}, apply={config.Learning.ApplyMode}, source={config.Learning.Source}, allowlisted={config.Learning.ObservedPlayerSteamIds.Count}, active_episodes={observationEpisodes.Count}, pending_traces={pendingObservationTraces.Count}, saved_traces={traceCount}, skill_models={behaviorModels.skill_models.Count}, profiles={behaviorModels.player_profiles.Count}.";
        }

        private string LearningReportLine(int minutes)
        {
            var now = Time.realtimeSinceStartup;
            var traces = ReadObservationTraces();

            if (minutes > 0)
            {
                traces = traces.Where(trace => trace.ended_at <= 0f || now - trace.ended_at <= minutes * 60f).ToList();
            }

            var players = traces.Select(trace => trace.source_steam_id64).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var avgOutcome = traces.Count == 0 ? 0f : traces.Average(trace => trace.outcome_score);
            var avgContext = traces.Count == 0 ? 0f : traces.Average(TraceTargetContextConfidence);
            var linked = traces.Count(IsCombatLinkedObservationTrace);
            var highConfidence = traces.Count(trace => TraceTargetContextConfidence(trace) >= config.Learning.HighConfidenceTargetContextThreshold);
            var topActions = traces
                .GroupBy(trace => NormalizeAdminKey(trace.observed_action), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .Take(5)
                .Select(group => $"{group.Key}:{group.Count()}");
            return $"Raidlands roam bot learning report ({(minutes <= 0 ? "all" : minutes + "m")}): traces={traces.Count}, players={players}, avg_outcome={avgOutcome:0.00}, target_context={avgContext:0.00}, combat_linked={linked}, high_confidence={highConfidence}, actions={string.Join(", ", topActions)}.";
        }

        private int CountObservationTraceLines()
        {
            var path = ObservationTraceDataPath();

            try
            {
                return File.Exists(path) ? File.ReadLines(path).Count() : 0;
            }
            catch
            {
                return 0;
            }
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
                ThrottledWarning("spawn-paused", $"Roam bot spawning is paused for {config.SpawnFailureRetrySeconds:0} seconds because no configured prefab could be placed on navmesh.");
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
                ThrottledWarning("no-eligible-kits", "No eligible default-access weapon kits found for roam bots.");
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
                            ThrottledWarning("spawn-no-position", "Could not find a valid land/navmesh spawn position for roam bots.");
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
                    DebugLog($"spawn-try:{prefab}", $"Trying legacy body prefab {prefab} at {FormatVector(position)} ({PositionDiagnostics(position)}), brain={TacticalBrainName}.");
                }

                var entity = GameManager.server.CreateEntity(prefab, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), true);

                if (entity == null)
                {
                    if (config.Debug.DebugSpawnDetails)
                    {
                        DebugLog($"spawn-create-null:{prefab}", $"Prefab {prefab} could not be created at {FormatVector(position)}.");
                    }

                    continue;
                }

                var bot = entity as BaseCombatEntity;

                if (bot == null)
                {
                    if (config.Debug.DebugSpawnDetails)
                    {
                        DebugLog($"spawn-wrong-type:{prefab}", $"Prefab {prefab} created {entity.GetType().Name}, not BaseCombatEntity; rejecting spawn attempt.");
                    }

                    SafeKillSpawnAttempt(entity);
                    continue;
                }

                entity.Spawn();

                if (!TryPlaceBotOnOwnNavmesh(bot, ref position))
                {
                    ThrottledWarning($"spawn-navmesh:{prefab}", $"Prefab {prefab} spawned but its navigator could not be placed on navmesh; trying the next candidate.");
                    SafeKillSpawnAttempt(bot);
                    continue;
                }

                if (IsBlockedLandPosition(position))
                {
                    ThrottledWarning($"spawn-blocked:{prefab}", $"Prefab {prefab} spawned at {FormatVector(position)}, but that position is blocked by terrain, water, or safe-zone rules; trying the next candidate.");
                    SafeKillSpawnAttempt(bot);
                    continue;
                }

                var runtime = ConfigureBot(bot, position, teamId, prefab);
                PrepareNpcBody(bot);
                runtime.CurrentDestination = FindRoamDestination(runtime.HomePosition, runtime);
                MoveBotTo(bot, runtime, runtime.CurrentDestination, BaseNavigator.NavigationSpeed.Fast);
                ScheduleBodyPrepare(bot);

                if (config.Debug.DebugSpawnDetails)
                {
                    DebugLog($"spawn-accepted:{prefab}", $"Accepted roam bot {runtime.DisplayName} from prefab {prefab} at {FormatVector(position)} ({PositionDiagnostics(position)}), {BotRuntimeDiagnostics(bot, runtime)}.");
                }

                return bot;
            }

            ThrottledWarning("spawn-prefabs-failed", "None of the configured NPC prefabs could be created.");
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
            var baseSkill = CloneSkillDefinition(skill);
            var profileModel = SelectSpawnPlayerProfileModel();
            var behaviorModel = profileModel ?? ActiveSkillBehaviorModel(skillTier);
            var behaviorModelKey = behaviorModel?.key ?? "";
            var playerProfileKey = profileModel?.key ?? "";

            if (profileModel != null || ShouldApplyGlobalSkillModelAtSpawn())
            {
                skill = SkillFromBehaviorModel(behaviorModel, skillTier, skill);
            }

            var kit = ShouldApplyKit(bot) ? ChooseKit() : null;
            var displayName = ChooseProfileName();
            var botKey = BotKey(displayName);
            var playerBot = bot as BasePlayer;
            var clan = ClanForTeam(teamId);
            var avatar = ChooseBotAvatar(displayName, teamId);

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
                BaseSkill = baseSkill,
                BehaviorModelKey = behaviorModelKey,
                PlayerProfileKey = playerProfileKey,
                ProfileSourceName = profileModel?.display_name ?? "",
                ProfileSourceSteamId = profileModel?.source_steam_id64 ?? "",
                AvatarKey = avatar?.Key ?? "",
                AvatarDisplayName = avatar?.DisplayName ?? "",
                AvatarImageName = avatar == null ? "" : BotAvatarImageName(avatar.Key),
                AvatarChatUserId = avatar?.ChatUserId ?? "",
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

            if (playerBot != null)
            {
                GiveBotMedicalItems(playerBot);
                NormalizeBotInventoryWeaponDamage(playerBot, runtime);
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
                ThrottledWarning($"kit-give:{kitName}", $"Kits plugin could not give {kitName} to {bot.displayName}: {message}");
            }
        }

        private void NormalizeBotInventoryWeaponDamage(BasePlayer bot, BotRuntime runtime)
        {
            if (bot?.inventory == null)
            {
                return;
            }

            var changed = false;
            changed |= NormalizeBotContainerWeaponDamage(bot.inventory.containerBelt, runtime);
            changed |= NormalizeBotContainerWeaponDamage(bot.inventory.containerMain, runtime);

            if (changed)
            {
                bot.SendNetworkUpdateImmediate();
            }
        }

        private bool NormalizeBotContainerWeaponDamage(ItemContainer container, BotRuntime runtime)
        {
            if (container?.itemList == null)
            {
                return false;
            }

            var changed = false;

            foreach (var item in container.itemList)
            {
                changed |= NormalizeBotItemWeaponDamage(item, runtime);
            }

            return changed;
        }

        private bool NormalizeBotActiveWeaponDamage(BaseCombatEntity bot, BotRuntime runtime)
        {
            var player = bot as BasePlayer;
            var item = player?.GetActiveItem();

            if (!NormalizeBotItemWeaponDamage(item, runtime))
            {
                return false;
            }

            item?.GetHeldEntity()?.SendNetworkUpdate();
            player?.SendNetworkUpdateImmediate();
            return true;
        }

        private bool NormalizeBotItemWeaponDamage(Item item, BotRuntime runtime)
        {
            var attackEntity = item?.GetHeldEntity() as AttackEntity;

            if (attackEntity == null)
            {
                return false;
            }

            return NormalizeBotWeaponDamage(attackEntity, item?.info?.shortname ?? "", runtime);
        }

        private bool NormalizeBotWeaponDamage(AttackEntity attackEntity, string weaponShortname, BotRuntime runtime)
        {
            if (attackEntity == null || Mathf.Abs(attackEntity.npcDamageScale - PlayerLikeDamageScale) <= 0.001f)
            {
                return false;
            }

            var previous = attackEntity.npcDamageScale;
            attackEntity.npcDamageScale = PlayerLikeDamageScale;

            DebugLog(
                $"npc-damage-scale:{runtime?.BotKey ?? weaponShortname}",
                $"{runtime?.DisplayName ?? "Roam bot"} normalized {weaponShortname} NPC weapon damage scale {previous.ToString("0.###", CultureInfo.InvariantCulture)} -> {PlayerLikeDamageScale.ToString("0.###", CultureInfo.InvariantCulture)}.",
                30f);

            return true;
        }

        private void GiveBotMedicalItems(BasePlayer bot)
        {
            if (bot == null
                || config?.AI?.GrantBotMedicalItems != true
                || bot.inventory == null)
            {
                return;
            }

            var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in config.AI.BotMedicalLoadout ?? new Dictionary<string, int>())
            {
                GiveBotMedicalItem(bot, entry.Key, entry.Value, granted);
            }

            if (config.AI.BotMedicalItemAmount > 0 && !string.IsNullOrWhiteSpace(config.AI.BotMedicalItemShortname))
            {
                GiveBotMedicalItem(bot, config.AI.BotMedicalItemShortname, config.AI.BotMedicalItemAmount, granted);
            }
        }

        private void GiveBotMedicalItem(BasePlayer bot, string shortname, int amount, HashSet<string> granted)
        {
            shortname = (shortname ?? "").Trim();
            amount = Math.Max(0, amount);

            if (bot == null || amount <= 0 || string.IsNullOrWhiteSpace(shortname) || granted?.Add(shortname) == false)
            {
                return;
            }

            var item = ItemManager.CreateByName(shortname, amount);

            if (item == null)
            {
                if (config.Debug.DebugSpawnDetails)
                {
                    DebugWarning("medical-create-failed", $"Could not create roam bot medical item '{shortname}'.");
                }

                return;
            }

            if (!item.MoveToContainer(bot.inventory?.containerBelt) && !item.MoveToContainer(bot.inventory?.containerMain))
            {
                bot.GiveItem(item);
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

            if (config?.Enabled == true)
            {
                timer.Once(config.RespawnDelaySeconds, MaintainPopulation);
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                ThrottledInfo($"despawn:{reason}", $"Despawned roam bot after {reason}.");
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
            var now = Time.realtimeSinceStartup;
            var damageTotal = HitDamageTotal(info);

            if (victimRuntime != null && attackerRuntime != null)
            {
                if (!IsEnemyBot(victimRuntime, attackerRuntime))
                {
                    return true;
                }

                var attackerBot = attackerEntity as BasePlayer;

                if (attackerBot != null)
                {
                    RememberDamageSource(victim, victimRuntime, attackerBot, now, info);
                }

                if (victimPlayer != null)
                {
                    RememberDamageDealt(attackerEntity, attackerRuntime, victimPlayer, now);
                }

                return null;
            }

            if (attackerRuntime != null && IsRealPlayer(victimPlayer) && ShouldIgnoreSafeZonePlayer(victimPlayer))
            {
                return true;
            }

            if (victimRuntime != null && IsRealPlayer(attacker) && ShouldIgnoreSafeZonePlayer(attacker))
            {
                return null;
            }

            if (IsRealPlayer(attacker) && IsExplosionDamage(info))
            {
                BroadcastPlayerSound(attacker, SoundPositionFromHit(info, attacker), config.AI.ExplosionHearingRange, "explosion", 1f, 0.75f);
            }

            if (victimRuntime != null && IsRealPlayer(attacker))
            {
                RememberDamageSource(victim, victimRuntime, attacker, now, info);
                RecordLearningPlayerEvent(attacker, ActionIdString(TacticalActionId.AcquireVisibleTarget), now, damageDealt: damageTotal, targetEntity: victim, targetRuntime: victimRuntime);
                return null;
            }

            if (attackerRuntime != null && IsRealPlayer(victimPlayer))
            {
                RememberDamageDealt(attackerEntity, attackerRuntime, victimPlayer, now);
                RecordLearningPlayerEvent(victimPlayer, ActionIdString(TacticalActionId.RetreatToCover), now, damageTaken: damageTotal, targetEntity: attackerEntity, targetRuntime: attackerRuntime);

                if (damageTotal >= Math.Max(0.1f, victimPlayer.Health()))
                {
                    ApplyBotNativeDeathInfo(victimPlayer, attackerEntity, attackerRuntime, info);
                    ProtectBotNativeDeathInfoFromScientistOverride(victimPlayer, attackerEntity, attackerRuntime, info, now);
                }
            }

            return null;
        }

        private void RememberDamageSource(BaseCombatEntity victim, BotRuntime victimRuntime, BasePlayer attacker, float now, HitInfo info = null)
        {
            if (victim == null || victimRuntime == null || attacker == null)
            {
                return;
            }

            RememberProtectionDamage(victim, victimRuntime, info, now);
            victimRuntime.LastDamageTakenAt = now;
            if (IsBarricadeAnchorActive(victimRuntime, now))
            {
                victimRuntime.BarricadeAnchorNoActionPushAt = now + BarricadeAnchorNoActionPushSeconds(victimRuntime);
            }

            victimRuntime.LastDamageBarricadeAwarenessCheckAt = 0f;
            victimRuntime.NextLowHealthAwarenessCheckAt = 0f;
            victimRuntime.Memory.TargetUserId = CombatTargetId(attacker);
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
        }

        private void RememberProtectionDamage(BaseCombatEntity victim, BotRuntime runtime, HitInfo info, float now)
        {
            if (victim == null || runtime == null || info == null)
            {
                return;
            }

            var damageFraction = DamageFractionOfBotHealth(victim, runtime, info);

            if (damageFraction <= 0f)
            {
                return;
            }

            if (runtime.ProtectionDamageWindowStartedAt <= 0f
                || now - runtime.ProtectionDamageWindowStartedAt > config.AI.ProtectionDamageWindowSeconds)
            {
                runtime.ProtectionDamageWindowStartedAt = now;
                runtime.ProtectionDamageAccumulatedFraction = 0f;
            }

            runtime.ProtectionDamageAccumulatedFraction += damageFraction;
            var triggerFraction = config.AI.ProtectionDamageTriggerPercent / 100f;

            if (runtime.ProtectionDamageAccumulatedFraction < triggerFraction)
            {
                runtime.LastProtectionReason = $"damage {runtime.ProtectionDamageAccumulatedFraction * 100f:0}%/{config.AI.ProtectionDamageTriggerPercent:0}%";
                return;
            }

            runtime.ProtectionDamageAwareUntil = now + config.AI.ProtectionCommitmentSeconds;
            runtime.DamageBarricadeAwareUntil = Math.Max(runtime.DamageBarricadeAwareUntil, now + config.AI.DamageWallReactionWindowSeconds);
            runtime.LastProtectionReason = $"trigger {runtime.ProtectionDamageAccumulatedFraction * 100f:0}%/{config.AI.ProtectionDamageTriggerPercent:0}%";
        }

        private float DamageFractionOfBotHealth(BaseCombatEntity victim, BotRuntime runtime, HitInfo info)
        {
            if (info?.damageTypes == null)
            {
                return 0f;
            }

            try
            {
                var damage = info.damageTypes.Total();
                return Mathf.Clamp01(damage / BotMaxHealth(victim, runtime));
            }
            catch
            {
                return 0f;
            }
        }

        private void RememberDamageDealt(BaseCombatEntity attackerEntity, BotRuntime attackerRuntime, BasePlayer victim, float now)
        {
            if (attackerEntity == null || attackerRuntime == null || victim == null)
            {
                return;
            }

            RefreshCombatProfile(attackerEntity, attackerRuntime);
            attackerRuntime.LastDamageDealtAt = now;
            RememberBarricadeAnchorHitmarker(attackerRuntime, CombatTargetId(victim), now);
            attackerRuntime.Memory.Target = victim;
            attackerRuntime.Memory.TargetUserId = CombatTargetId(victim);
            attackerRuntime.Memory.LastSeenPosition = victim.transform.position;
            attackerRuntime.Memory.LastSeenAt = now;
            attackerRuntime.Memory.TargetConfidence = Math.Max(attackerRuntime.Memory.TargetConfidence, 0.85f);
        }

        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectileShoot)
        {
            var botRuntime = RuntimeFor(player);

            if (botRuntime != null)
            {
                NormalizeBotWeaponDamage(projectile, projectile?.GetItem()?.info?.shortname ?? "", botRuntime);
                ApplyBotAimError(projectileShoot, player, botRuntime, Time.realtimeSinceStartup);
                return;
            }

            if (!IsRealPlayer(player) || ShouldIgnoreSafeZonePlayer(player))
            {
                return;
            }

            RecordLearningPlayerEvent(player, ActionIdString(TacticalActionId.AcquireVisibleTarget), Time.realtimeSinceStartup);

            if (!config.AI.AllowHearing)
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

        private void ApplyBotAimError(ProtoBuf.ProjectileShoot projectileShoot, BasePlayer bot, BotRuntime runtime, float now)
        {
            if (projectileShoot?.projectiles == null || projectileShoot.projectiles.Count == 0 || bot == null || runtime == null)
            {
                return;
            }

            var errorDegrees = AimErrorDegreesAt(runtime, now);
            runtime.CurrentAimErrorDegrees = errorDegrees;

            if (errorDegrees <= 0.01f)
            {
                return;
            }

            foreach (var firedProjectile in projectileShoot.projectiles)
            {
                if (firedProjectile == null)
                {
                    continue;
                }

                var velocity = firedProjectile.startVel;
                var speed = velocity.magnitude;

                if (speed <= 0.01f)
                {
                    continue;
                }

                var direction = velocity.sqrMagnitude > 0.0001f
                    ? velocity.normalized
                    : AimDirectionFromShot(bot, runtime, firedProjectile.startPos);

                firedProjectile.startVel = RandomDirectionInCone(direction, errorDegrees) * speed;
            }

            if (config.Debug.DebugPerception)
            {
                DebugLog($"bot-aim:{runtime.BotKey}", $"{runtime.DisplayName} fired with aim error {errorDegrees:0.0} degrees ({AimWarmupProgress(runtime, now) * 100f:0}% warm).", 1f);
            }
        }

        private Vector3 AimDirectionFromShot(BasePlayer bot, BotRuntime runtime, Vector3 startPosition)
        {
            var target = runtime?.Memory?.Target;

            if (target != null)
            {
                var targetPoint = EyePosition(target);

                if (targetPoint != Vector3.zero)
                {
                    var toTarget = targetPoint - startPosition;

                    if (toTarget.sqrMagnitude > 0.0001f)
                    {
                        return toTarget.normalized;
                    }
                }
            }

            return bot?.eyes != null ? bot.eyes.HeadForward() : Vector3.forward;
        }

        private Vector3 RandomDirectionInCone(Vector3 direction, float degrees)
        {
            if (direction.sqrMagnitude <= 0.0001f || degrees <= 0.01f)
            {
                return direction.sqrMagnitude <= 0.0001f ? Vector3.forward : direction.normalized;
            }

            direction.Normalize();
            var tangent = Vector3.Cross(direction, Vector3.up);

            if (tangent.sqrMagnitude <= 0.0001f)
            {
                tangent = Vector3.Cross(direction, Vector3.right);
            }

            tangent.Normalize();
            var bitangent = Vector3.Cross(direction, tangent).normalized;
            var spin = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            var axis = (tangent * Mathf.Cos(spin) + bitangent * Mathf.Sin(spin)).normalized;
            var angle = degrees * Mathf.Sqrt(UnityEngine.Random.Range(0f, 1f));
            return (Quaternion.AngleAxis(angle, axis) * direction).normalized;
        }

        private void OnRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            if (!IsRealPlayer(player) || ShouldIgnoreSafeZonePlayer(player))
            {
                return;
            }

            RecordLearningPlayerEvent(player, ActionIdString(TacticalActionId.ThrowGrenade), Time.realtimeSinceStartup, explosive: true);

            if (!config.AI.AllowHearing)
            {
                return;
            }

            BroadcastPlayerSound(player, player.transform.position, config.AI.ExplosionHearingRange, "rocket_launch", 1f, 0.35f);
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            if (!IsRealPlayer(player) || ShouldIgnoreSafeZonePlayer(player))
            {
                return;
            }

            RecordLearningPlayerEvent(player, ActionIdString(TacticalActionId.ThrowGrenade), Time.realtimeSinceStartup, explosive: true);

            if (!config.AI.AllowHearing)
            {
                return;
            }

            BroadcastPlayerSound(player, player.transform.position, config.AI.MeleeOrToolHearingRange, "thrown_explosive", 0.45f, 0.35f);
        }

        private void OnMeleeAttack(BasePlayer player, HitInfo info)
        {
            if (!IsRealPlayer(player) || ShouldIgnoreSafeZonePlayer(player))
            {
                return;
            }

            RecordLearningPlayerEvent(player, ActionIdString(TacticalActionId.PushTarget), Time.realtimeSinceStartup, melee: true);

            if (!config.AI.AllowHearing)
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
            var attacker = ResolveKillerPlayer(victim, info);
            var attackerEntity = ResolveKillerEntity(victim, info, attacker);
            var attackerRuntime = RuntimeFor(attackerEntity);
            var now = Time.realtimeSinceStartup;
            var pendingBotPlayerDeath = TakePendingBotPlayerDeath(victimPlayer, info, now);

            if (pendingBotPlayerDeath != null)
            {
                if (pendingBotPlayerDeath.KillerEntity != null && !pendingBotPlayerDeath.KillerEntity.IsDestroyed)
                {
                    attackerEntity = pendingBotPlayerDeath.KillerEntity;
                }

                if (pendingBotPlayerDeath.KillerRuntime != null)
                {
                    attackerRuntime = pendingBotPlayerDeath.KillerRuntime;
                }
            }

            if (victimRuntime != null)
            {
                TrackRecentBotDeath(victim, victimRuntime);
                if (victimPlayer != null)
                {
                    MarkBarricadeAnchorTargetDeath(CombatTargetId(victimPlayer), now);
                }

                activeBots.Remove(victim);

                if (!despawningBots.Remove(victim))
                {
                    var botStats = EnsureBotStats(victimRuntime);
                    botStats.deaths++;
                    EnsureClanStats(victimRuntime).deaths++;

                    if (IsRealPlayer(attacker) && attackerRuntime == null)
                    {
                        RecordLearningPlayerEvent(attacker, ActionIdString(TacticalActionId.AcquireVisibleTarget), now, kill: true, targetEntity: victim, targetRuntime: victimRuntime);
                        var playerStats = EnsurePlayerStats(attacker);
                        playerStats.npc_kills++;
                        HandlePlayerKilledBot(attacker, victim, victimRuntime, info);
                    }
                    else if (attackerRuntime != null && IsEnemyBot(attackerRuntime, victimRuntime))
                    {
                        var killerStats = EnsureBotStats(attackerRuntime);
                        killerStats.kills++;
                        EnsureClanStats(attackerRuntime).kills++;
                        HandleBotKilledBot(attackerEntity, attackerRuntime, victim, victimRuntime, info);
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
                RecordLearningPlayerEvent(victimPlayer, ActionIdString(TacticalActionId.RetreatToCover), now, died: true, targetEntity: attackerEntity, targetRuntime: attackerRuntime);
                var playerStats = EnsurePlayerStats(victimPlayer);
                playerStats.deaths_by_npc++;
                var botStats = EnsureBotStats(attackerRuntime);
                botStats.kills++;
                EnsureClanStats(attackerRuntime).kills++;
                HandleBotKilledPlayer(attackerEntity, attackerRuntime, victimPlayer, info);
                QueueSaveData();
                UpdateScoreboards();
            }

            if (victimPlayer != null)
            {
                MarkBarricadeAnchorTargetDeath(CombatTargetId(victimPlayer), now);
            }
        }

        private object OnDeathNotice(Dictionary<string, object> deathData, string message)
        {
            if (config?.BotKillIntegration?.SuppressDeathNotesForRoamBotKills != true)
            {
                return null;
            }

            var hasTrackedVictim = TryGetDeathNoticeEntity(deathData, "VictimEntity", out var victim)
                && BotRuntimeForDeathNotice(victim) != null;
            var hasTrackedKiller = TryGetDeathNoticeEntity(deathData, "KillerEntity", out var killer)
                && BotRuntimeForDeathNotice(killer) != null;

            return hasTrackedVictim || hasTrackedKiller ? (object)false : null;
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

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var command = arg?.cmd?.FullName ?? arg?.cmd?.Name;
            SuppressDebugSidePanelForMenuCommand(player, command);
            return null;
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            SuppressDebugSidePanelForMenuCommand(player, command);
            return null;
        }

        [ChatCommand("raidbots")]
        private void ChatRaidBots(BasePlayer player, string command, string[] args)
        {
            if (!CanAdmin(player))
            {
                player?.ChatMessage("You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var mode = args != null && args.Length > 0 ? (args[0] ?? "").Trim().ToLowerInvariant() : "admin";

            if (string.IsNullOrWhiteSpace(mode) || mode == "admin" || mode == "panel" || mode == "settings")
            {
                var tab = args != null && args.Length > 1 ? args[1] : "overview";
                OpenAdminPanel(player, tab);
                return;
            }

            if (mode == "close")
            {
                DestroyAdminPanel(player);
                return;
            }

            player.ChatMessage("Use /raidbots admin to open the Raidlands roam bot admin panel.");
        }

        [ConsoleCommand("raidbots.ui")]
        private void CmdAdminUi(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var player = arg?.Connection?.player as BasePlayer;

            if (player == null)
            {
                Reply(arg, "Use /raidbots admin in game to open the Raidlands roam bot admin panel.");
                return;
            }

            var action = ArgString(arg, 0).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(action) || action == "open" || action == "admin")
            {
                OpenAdminPanel(player, AdminTabFromTail(arg, "overview"));
                return;
            }

            if (action == "close")
            {
                DestroyAdminPanel(player);
                return;
            }

            var tab = AdminTabFromTail(arg, "overview");

            if (action == "tab")
            {
                DrawAdminPanel(player, NormalizeAdminTab(ArgString(arg, 1)));
                return;
            }

            if (action == "refresh")
            {
                DrawAdminPanel(player, tab);
                return;
            }

            if (ApplyAdminUiAction(arg, action, ref tab))
            {
                DrawAdminPanel(player, tab);
            }
        }

        private bool ApplyAdminUiAction(ConsoleSystem.Arg arg, string action, ref string tab)
        {
            if (action == "reload")
            {
                LoadConfig();
                LoadData();
                LoadBehaviorModels();
                RefreshEligibleKits();
                RefreshDecisionAdvisor();
                RefreshLearningTimer();

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
                return true;
            }

            if (action == "enable")
            {
                config.Enabled = true;
                SaveConfig();
                StartRuntime();
                Reply(arg, $"Raidlands roam bots enabled with target population {TargetPopulation()}.");
                return true;
            }

            if (action == "disable")
            {
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
                    Reply(arg, "Raidlands roam bots disabled. Existing bots are left alone by persistence config.");
                }

                return true;
            }

            if (action == "spawn")
            {
                var count = 1;

                if (TryReadIntArg(arg, 1, out var requested))
                {
                    count = Clamp(requested, 1, Math.Max(1, config.MaxAllowedPopulation));
                }

                if (!config.Enabled)
                {
                    Reply(arg, "Raidlands roam bots are disabled; enable them before spawning bots.");
                    return true;
                }

                CleanupInactiveBots();
                var available = Math.Max(0, TargetPopulation() - activeBots.Count);

                if (available <= 0)
                {
                    Reply(arg, $"Raidlands roam bots are already at target population {TargetPopulation()}.");
                    return true;
                }

                var spawned = SpawnBots(Math.Min(count, available), true);
                Reply(arg, $"Spawned {spawned} roam bot{(spawned == 1 ? "" : "s")}.");
                return true;
            }

            if (action == "killall")
            {
                var count = activeBots.Count;
                KillAllBots(false);
                Reply(arg, $"Despawned {count} roam bot{(count == 1 ? "" : "s")}.");
                return true;
            }

            if (action == "nuke")
            {
                if (!config.Persistence.EmergencyKillCommandEnabled)
                {
                    Reply(arg, "Raidlands roam bot emergency kill command is disabled in config.");
                    return true;
                }

                var mode = ArgString(arg, 1).ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(mode) || mode == "active")
                {
                    var count = activeBots.Count;
                    KillAllBots(false);
                    Reply(arg, $"Emergency removed {count} tracked roam bot{(count == 1 ? "" : "s")}.");
                    return true;
                }

                if (mode == "debug")
                {
                    DestroyDebugBotPanels();
                    Reply(arg, "Cleared Raidlands roam bot debug panels.");
                    return true;
                }

                if (mode == "all")
                {
                    var count = activeBots.Count;
                    KillAllBots(false);
                    DestroyDebugBotPanels();
                    Reply(arg, $"Emergency removed {count} tracked roam bot{(count == 1 ? "" : "s")} and cleared debug panels.");
                    return true;
                }
            }

            if (action == "mode")
            {
                var requested = ArgString(arg, 1);

                if (!TryNormalizeSpawnMode(requested, out var mode))
                {
                    Reply(arg, $"Unknown spawn mode '{requested}'.");
                    return true;
                }

                config.Spawn.SpawnMode = mode;
                SaveAdminConfigChange(true, false, false, false);
                Reply(arg, $"Raidlands roam bot spawn mode set to {mode}.");
                return true;
            }

            if (action == "anchor-clear")
            {
                config.Spawn.NearPlayerAnchorNameOrSteamId = "";
                SaveAdminConfigChange(true, false, false, false);
                Reply(arg, "Raidlands roam bot near-player anchor cleared; all valid players are anchors.");
                return true;
            }

            if (action == "advisor")
            {
                SetAdminAdvisorMode(ArgString(arg, 1), arg);
                return true;
            }

            if (action == "debug-all")
            {
                if (!TryParseAdminBool(ArgString(arg, 1), out var enabled))
                {
                    enabled = !config.Debug.DebugSpawnDetails
                        || !config.Debug.DebugPerception
                        || !config.Debug.DebugTacticalDecisions
                        || !config.Debug.DebugBotNameplates
                        || !config.Debug.DebugBotSidePanel;
                }

                config.Debug.DebugSpawnDetails = enabled;
                config.Debug.DebugPerception = enabled;
                config.Debug.DebugTacticalDecisions = enabled;
                config.Debug.DebugBotNameplates = enabled;
                config.Debug.DebugBotSidePanel = enabled;
                SaveAdminConfigChange(false, false, false, true);
                Reply(arg, $"Raidlands roam bot debug surfaces set to {enabled}.");
                return true;
            }

            if (action == "toggle")
            {
                var key = ArgString(arg, 1);

                if (!ToggleAdminBooleanSetting(key, out var restartRuntime, out var refreshAdvisor, out var refreshNameplates))
                {
                    Reply(arg, $"Unknown Raidlands roam bot toggle '{key}'.");
                    return true;
                }

                SaveAdminConfigChange(true, restartRuntime, refreshAdvisor, refreshNameplates);
                Reply(arg, $"Raidlands roam bot setting '{key}' toggled.");
                return true;
            }

            if (action == "seti" || action == "addi")
            {
                var key = ArgString(arg, 1);

                if (!TryReadIntArg(arg, 2, out var value))
                {
                    Reply(arg, $"Missing integer value for '{key}'.");
                    return true;
                }

                if (!(action == "seti" ? SetAdminIntegerSetting(key, value) : AdjustAdminIntegerSetting(key, value)))
                {
                    Reply(arg, $"Unknown integer Raidlands roam bot setting '{key}'.");
                    return true;
                }

                SaveAdminConfigChange(true, AdminIntegerSettingNeedsRuntimeRestart(key), false, AdminIntegerSettingNeedsNameplateRestart(key));
                Reply(arg, $"Raidlands roam bot setting '{key}' updated.");
                return true;
            }

            if (action == "setf" || action == "addf")
            {
                var key = ArgString(arg, 1);

                if (!TryReadFloatArg(arg, 2, out var value))
                {
                    Reply(arg, $"Missing numeric value for '{key}'.");
                    return true;
                }

                if (!(action == "setf" ? SetAdminFloatSetting(key, value) : AdjustAdminFloatSetting(key, value)))
                {
                    Reply(arg, $"Unknown numeric Raidlands roam bot setting '{key}'.");
                    return true;
                }

                SaveAdminConfigChange(true, AdminFloatSettingNeedsRuntimeRestart(key), false, AdminFloatSettingNeedsNameplateRestart(key));
                Reply(arg, $"Raidlands roam bot setting '{key}' updated.");
                return true;
            }

            if (action == "preset-live")
            {
                config.TargetPopulation = 50;
                config.MinAllowedPopulation = 0;
                config.MaxAllowedPopulation = 200;
                config.TeamSizeWeights["solo"] = 55;
                config.TeamSizeWeights["duo"] = 35;
                config.TeamSizeWeights["trio"] = 10;
                config.Spawn.SpawnMode = SpawnModeNearPlayers;
                config.Spawn.NearPlayerAnchorNameOrSteamId = "";
                config.Spawn.UseRandomLandFallback = false;
                config.Spawn.RequireLandSpawns = true;
                config.Spawn.AvoidSafeZoneSpawns = true;
                config.Spawn.IgnorePlayersInSafeZones = true;
                SaveAdminConfigChange(true, false, false, false);
                Reply(arg, "Applied live population preset: target=50, max=200, near players, all-player anchors.");
                return true;
            }

            Reply(arg, $"Unknown Raidlands roam bot admin action '{action}'.");
            return true;
        }

        private void SaveAdminConfigChange(bool maintainPopulation, bool restartRuntime, bool refreshAdvisor, bool refreshNameplates)
        {
            NormalizeConfig();
            SaveConfig();
            RefreshLearningTimer();
            spawnRetryBlockedUntil = 0f;

            if (refreshAdvisor)
            {
                RefreshDecisionAdvisor();
            }

            if (restartRuntime && config.Enabled)
            {
                StartRuntime();
                return;
            }

            if (refreshNameplates)
            {
                StartNameplateTimerIfEnabled();
            }

            if (maintainPopulation && config.Enabled)
            {
                MaintainPopulation();
            }
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
            LoadBehaviorModels();
            RefreshEligibleKits();
            RefreshDecisionAdvisor();
            RefreshLearningTimer();
            MaybePruneDecisionTraceFile(DecisionTraceDataPath(), true);

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
                string pruneMessage;
                var pruned = TryPruneDecisionTraceFile(path, true, out pruneMessage);
                var count = File.Exists(path) ? File.ReadLines(path).Count() : 0;
                var size = File.Exists(path) ? new FileInfo(path).Length : 0L;

                if (pruned)
                {
                    Reply(arg, pruneMessage);
                }

                Reply(arg, $"Decision trace JSONL: {path} ({count} lines, {FormatFileSize(size)}). Retention: max={config.DecisionAdvisor.MaxDecisionTraceFileMegabytes} MB, keep={config.DecisionAdvisor.MaxDecisionTraceLinesAfterPrune} recent lines.");
                return;
            }

            if (mode == "prune")
            {
                FlushDecisionTraces();
                string message;
                TryPruneDecisionTraceFile(DecisionTraceDataPath(), true, out message);
                Reply(arg, message);
                return;
            }

            Reply(arg, "Usage: raidbots.decisions [last [count]|bot <name/key> [count]|export|prune]");
        }

        [ConsoleCommand("raidbots.advisor")]
        private void CmdAdvisor(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var mode = ArgString(arg, 0).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(mode) || mode == "status")
            {
                Reply(arg, AdvisorStatusLine());
                Reply(arg, "Usage: raidbots.advisor status|off|fallback|shadow|canary|stats|last [bot name/key]");
                return;
            }

            if (mode == "off")
            {
                config.DecisionAdvisor.Enabled = false;
                config.DecisionAdvisor.Provider = AdvisorProviderNone;
                config.DecisionAdvisor.Mode = AdvisorModeFallbackOnly;
                config.DecisionAdvisor.ShadowMode = true;
                SaveConfig();
                RefreshDecisionAdvisor();
                Reply(arg, "Raidlands roam bot advisor disabled. Deterministic heuristic fallback remains active.");
                return;
            }

            if (mode == "fallback")
            {
                config.DecisionAdvisor.Enabled = true;
                config.DecisionAdvisor.Provider = AdvisorProviderNone;
                config.DecisionAdvisor.Mode = AdvisorModeFallbackOnly;
                config.DecisionAdvisor.ShadowMode = true;
                SaveConfig();
                RefreshDecisionAdvisor();
                Reply(arg, "Raidlands roam bot advisor set to fallback_only with provider none.");
                return;
            }

            if (mode == "shadow")
            {
                config.DecisionAdvisor.Enabled = true;
                config.DecisionAdvisor.Mode = AdvisorModeShadow;
                config.DecisionAdvisor.ShadowMode = true;
                SaveConfig();
                RefreshDecisionAdvisor();
                Reply(arg, $"Raidlands roam bot advisor set to shadow mode. Provider remains {config.DecisionAdvisor.Provider}; heuristic actions still execute.");
                return;
            }

            if (mode == "canary")
            {
                config.DecisionAdvisor.Enabled = true;
                config.DecisionAdvisor.Mode = AdvisorModeCanary;
                config.DecisionAdvisor.ShadowMode = false;
                SaveConfig();
                RefreshDecisionAdvisor();
                Reply(arg, "Raidlands roam bot advisor set to canary mode for validation/tracing. Remote actions are still not executed in this adapter pass.");
                return;
            }

            if (mode == "stats")
            {
                Reply(arg, AdvisorStatsLine());
                return;
            }

            if (mode == "last")
            {
                var query = ArgString(arg, 1);
                FlushDecisionTraces();
                var lines = ReadDecisionTraceLines(1, string.IsNullOrWhiteSpace(query) ? "" : BotKey(query));

                if (lines.Count == 0)
                {
                    Reply(arg, string.IsNullOrWhiteSpace(query) ? "No advisor decision traces have been written yet." : $"No advisor decision traces found for bot '{query}'.");
                    return;
                }

                Reply(arg, FormatDecisionTraceLine(lines.Last()));
                return;
            }

            Reply(arg, "Usage: raidbots.advisor status|off|fallback|shadow|canary|stats|last [bot name/key]");
        }

        [ConsoleCommand("raidbots.learn")]
        private void CmdLearn(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var mode = ArgString(arg, 0).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(mode) || mode == "status")
            {
                Reply(arg, LearningStatusLine());
                Reply(arg, "Usage: raidbots.learn status|observe on|off|allow <player>|unallow <player>|build|report [minutes]|export|apply off|shadow|global|profiles|profile list|show|build|delete|weight");
                return;
            }

            if (mode == "observe")
            {
                if (!TryReadBoolArg(arg, 1, out var enabled))
                {
                    Reply(arg, $"Raidlands roam bot observation is {config.Learning.Enabled}. Use raidbots.learn observe on|off.");
                    return;
                }

                config.Learning.Enabled = enabled;
                NormalizeConfig();
                SaveConfig();
                RefreshLearningTimer();
                Reply(arg, $"Raidlands roam bot player observation set to {config.Learning.Enabled}.");
                return;
            }

            if (mode == "allow" || mode == "unallow")
            {
                var query = ArgStringFrom(arg, 1);

                if (string.IsNullOrWhiteSpace(query))
                {
                    Reply(arg, $"Usage: raidbots.learn {mode} <player name or steam id>");
                    return;
                }

                var player = FindActivePlayer(query);
                var steamId = player?.UserIDString ?? (IsSteamId64(query) ? query.Trim() : "");

                if (!IsSteamId64(steamId))
                {
                    Reply(arg, $"No connected player or SteamID matched '{query}'.");
                    return;
                }

                if (mode == "allow")
                {
                    if (!config.Learning.ObservedPlayerSteamIds.Contains(steamId, StringComparer.OrdinalIgnoreCase))
                    {
                        config.Learning.ObservedPlayerSteamIds.Add(steamId);
                    }

                    NormalizeConfig();
                    SaveConfig();
                    RefreshLearningTimer();
                    Reply(arg, $"Added {CleanName(player == null ? query : PlayerName(player))} ({steamId}) to RoamBots observation allowlist.");
                    return;
                }

                var removed = config.Learning.ObservedPlayerSteamIds.RemoveAll(value => string.Equals(value, steamId, StringComparison.OrdinalIgnoreCase));
                NormalizeConfig();
                SaveConfig();
                RefreshLearningTimer();
                Reply(arg, removed > 0 ? $"Removed {steamId} from RoamBots observation allowlist." : $"{steamId} was not on the RoamBots observation allowlist.");
                return;
            }

            if (mode == "build")
            {
                FlushAllObservationEpisodes(true);
                var traces = ReadObservationTraces();
                var built = BuildGlobalBehaviorModels(traces, out var summary);
                Reply(arg, built > 0 ? $"Built RoamBots learned global skill models. {summary}" : $"No global models built: {summary}");
                return;
            }

            if (mode == "report")
            {
                var minutes = 60;

                if (TryReadIntArg(arg, 1, out var requested))
                {
                    minutes = Clamp(requested, 0, 10080);
                }

                FlushObservationTraces();
                Reply(arg, LearningReportLine(minutes));
                return;
            }

            if (mode == "export")
            {
                FlushObservationTraces();
                SaveBehaviorModels();
                Reply(arg, $"RoamBots learning files: traces={ObservationTraceDataPath()} ({CountObservationTraceLines()} lines), models={Path.Combine(Interface.Oxide.DataFileSystem.Directory, BehaviorModelDataFile + ".json")}, training={TrainingRunDataPath()}.");
                return;
            }

            if (mode == "clear")
            {
                FlushAllObservationEpisodes(false);
                pendingObservationTraces.Clear();
                observationTraceSaveTimer?.Destroy();
                observationTraceSaveTimer = null;

                var path = ObservationTraceDataPath();

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                AppendTrainingRun("clear", "ok", "observation traces cleared by admin command");
                Reply(arg, "Cleared RoamBots player observation traces and pending episodes. Learned models were left intact.");
                return;
            }

            if (mode == "apply")
            {
                var requested = ArgString(arg, 1);
                var normalized = NormalizeLearningApplyMode(requested);

                if (string.IsNullOrWhiteSpace(requested) || normalized == LearningApplyOff && NormalizeAdminKey(requested) != LearningApplyOff)
                {
                    Reply(arg, "Usage: raidbots.learn apply off|shadow|global|profiles");
                    return;
                }

                config.Learning.ApplyMode = normalized;
                NormalizeConfig();
                SaveConfig();
                Reply(arg, $"RoamBots learned behavior apply mode set to {config.Learning.ApplyMode}.");
                return;
            }

            if (mode == "profile")
            {
                CmdLearnProfile(arg);
                return;
            }

            Reply(arg, "Usage: raidbots.learn status|observe on|off|allow|unallow|build|report|export|apply|profile");
        }

        private void CmdLearnProfile(ConsoleSystem.Arg arg)
        {
            var action = ArgString(arg, 1).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(action) || action == "list")
            {
                if (behaviorModels.player_profiles.Count == 0)
                {
                    Reply(arg, "No RoamBots player profiles have been built yet.");
                    return;
                }

                foreach (var entry in behaviorModels.player_profiles.OrderBy(entry => entry.Key).Take(20))
                {
                    var weight = config.Learning.PlayerProfileSpawnWeights != null && config.Learning.PlayerProfileSpawnWeights.TryGetValue(entry.Key, out var configuredWeight)
                        ? configuredWeight
                        : 0;
                    Reply(arg, $"{entry.Key}: source={entry.Value.display_name} ({entry.Value.source_steam_id64}), observations={entry.Value.observations}, success={entry.Value.success_rate:0.00}, weight={weight}, {entry.Value.summary}");
                }

                return;
            }

            if (action == "show")
            {
                var key = NormalizeProfileKey(ArgString(arg, 2));

                if (string.IsNullOrWhiteSpace(key) || !behaviorModels.player_profiles.TryGetValue(key, out var model))
                {
                    Reply(arg, "Usage: raidbots.learn profile show <profileKey>");
                    return;
                }

                var deltas = string.Join(", ", model.action_score_deltas.OrderByDescending(entry => Math.Abs(entry.Value)).Take(8).Select(entry => $"{entry.Key}:{entry.Value:0.0}"));
                Reply(arg, $"Profile {key}: source={model.display_name} ({model.source_steam_id64}), built={model.built_at_utc}, observations={model.observations}, success={model.success_rate:0.00}, weighted={model.weighted_success_rate:0.00}, ctx={model.average_target_context_confidence:0.00}, linked={model.target_linked_observations}, high_conf={model.high_confidence_observations}, skill aim={model.skill.AimErrorDegrees:0.00}, reaction={model.skill.ReactionMinSeconds:0.00}-{model.skill.ReactionMaxSeconds:0.00}, aggression={model.skill.Aggression:0.00}, deltas={deltas}");
                return;
            }

            if (action == "build")
            {
                if (arg?.Args == null || arg.Args.Length < 4)
                {
                    Reply(arg, "Usage: raidbots.learn profile build <player name or steam id> <profileKey>");
                    return;
                }

                var profileKey = ArgString(arg, arg.Args.Length - 1);
                var playerQuery = ArgStringRange(arg, 2, arg.Args.Length - 1);

                if (BuildPlayerProfile(playerQuery, profileKey, out var message))
                {
                    Reply(arg, message);
                }
                else
                {
                    Reply(arg, $"Could not build RoamBots profile: {message}");
                }

                return;
            }

            if (action == "delete")
            {
                var key = NormalizeProfileKey(ArgString(arg, 2));

                if (string.IsNullOrWhiteSpace(key))
                {
                    Reply(arg, "Usage: raidbots.learn profile delete <profileKey>");
                    return;
                }

                var removed = behaviorModels.player_profiles.Remove(key);
                config.Learning.PlayerProfileSpawnWeights?.Remove(key);
                SaveBehaviorModels();
                NormalizeConfig();
                SaveConfig();
                Reply(arg, removed ? $"Deleted RoamBots player profile '{key}'. Bots will fall back to skill-tier behavior if that profile was selected." : $"No RoamBots player profile named '{key}' exists.");
                return;
            }

            if (action == "weight")
            {
                var key = NormalizeProfileKey(ArgString(arg, 2));

                if (string.IsNullOrWhiteSpace(key) || !TryReadIntArg(arg, 3, out var weight))
                {
                    Reply(arg, "Usage: raidbots.learn profile weight <profileKey> <weight>");
                    return;
                }

                if (!behaviorModels.player_profiles.ContainsKey(key))
                {
                    Reply(arg, $"No RoamBots player profile named '{key}' exists.");
                    return;
                }

                if (config.Learning.PlayerProfileSpawnWeights == null)
                {
                    config.Learning.PlayerProfileSpawnWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }

                weight = Clamp(weight, 0, 1000);

                if (weight <= 0)
                {
                    config.Learning.PlayerProfileSpawnWeights.Remove(key);
                }
                else
                {
                    config.Learning.PlayerProfileSpawnWeights[key] = weight;
                }

                NormalizeConfig();
                SaveConfig();
                Reply(arg, $"RoamBots player profile '{key}' spawn weight set to {weight}.");
                return;
            }

            Reply(arg, "Usage: raidbots.learn profile list|show <key>|build <player> <key>|delete <key>|weight <key> <weight>");
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
            var fallback = DefaultSkillDefinition(tier);

            if (config.SkillDefinitions != null && config.SkillDefinitions.TryGetValue(tier, out var definition) && definition != null)
            {
                return NormalizeSkillDefinition(tier, definition, fallback);
            }

            return NormalizeSkillDefinition(tier, null, fallback);
        }

        private SkillDefinition NormalizeSkillDefinition(string tier, SkillDefinition definition, SkillDefinition fallback = null)
        {
            var normalized = definition ?? CloneSkillDefinition(fallback) ?? new SkillDefinition();
            var fallbackHealth = fallback?.Health ?? DefaultHealthForTier(tier);

            if (normalized.Health < BotMinPlayerLikeHealth || normalized.Health > BotMaxPlayerLikeHealth)
            {
                normalized.Health = Mathf.Clamp(fallbackHealth, BotMinPlayerLikeHealth, BotMaxPlayerLikeHealth);
            }
            else
            {
                normalized.Health = Mathf.Clamp(normalized.Health, BotMinPlayerLikeHealth, BotMaxPlayerLikeHealth);
            }

            normalized.DamageScale = PlayerLikeDamageScale;
            normalized.IncomingDamageScale = PlayerLikeDamageScale;
            normalized.ReactionMinSeconds = Math.Max(0f, normalized.ReactionMinSeconds);
            normalized.ReactionMaxSeconds = Math.Max(normalized.ReactionMinSeconds, normalized.ReactionMaxSeconds);
            normalized.AimErrorDegrees = Mathf.Clamp(normalized.AimErrorDegrees, 0f, 45f);
            normalized.AimWarmupSeconds = Mathf.Clamp(normalized.AimWarmupSeconds, 0f, 5f);
            normalized.AimWarmupInitialExtraDegrees = Mathf.Clamp(normalized.AimWarmupInitialExtraDegrees, 0f, 45f);
            normalized.Aggression = Mathf.Clamp01(normalized.Aggression);
            normalized.Courage = Mathf.Clamp01(normalized.Courage);
            normalized.TacticalNoise = Mathf.Clamp01(normalized.TacticalNoise);
            return normalized;
        }

        private SkillDefinition CloneSkillDefinition(SkillDefinition source)
        {
            if (source == null)
            {
                return null;
            }

            return new SkillDefinition
            {
                Health = source.Health,
                DamageScale = source.DamageScale,
                IncomingDamageScale = source.IncomingDamageScale,
                ReactionMinSeconds = source.ReactionMinSeconds,
                ReactionMaxSeconds = source.ReactionMaxSeconds,
                AimErrorDegrees = source.AimErrorDegrees,
                AimWarmupSeconds = source.AimWarmupSeconds,
                AimWarmupInitialExtraDegrees = source.AimWarmupInitialExtraDegrees,
                Aggression = source.Aggression,
                Courage = source.Courage,
                TacticalNoise = source.TacticalNoise
            };
        }

        private SkillDefinition DefaultSkillDefinition(string tier)
        {
            if (string.Equals(tier, "casual", StringComparison.OrdinalIgnoreCase))
            {
                return new SkillDefinition { Health = 100f, DamageScale = 1f, IncomingDamageScale = 1f, ReactionMinSeconds = 0.75f, ReactionMaxSeconds = 1.35f, AimErrorDegrees = 1.5f, AimWarmupSeconds = 2.5f, AimWarmupInitialExtraDegrees = 3f, Aggression = 0.35f, Courage = 0.35f, TacticalNoise = 0.25f };
            }

            if (string.Equals(tier, "dangerous", StringComparison.OrdinalIgnoreCase))
            {
                return new SkillDefinition { Health = 120f, DamageScale = 1f, IncomingDamageScale = 1f, ReactionMinSeconds = 0.18f, ReactionMaxSeconds = 0.45f, AimErrorDegrees = 0.2f, AimWarmupSeconds = 1f, AimWarmupInitialExtraDegrees = 0.4f, Aggression = 0.8f, Courage = 0.8f, TacticalNoise = 0.06f };
            }

            return new SkillDefinition { Health = BotDefaultAverageHealth, DamageScale = 1f, IncomingDamageScale = 1f, ReactionMinSeconds = 0.4f, ReactionMaxSeconds = 0.85f, AimErrorDegrees = 0.75f, AimWarmupSeconds = 1.75f, AimWarmupInitialExtraDegrees = 1.5f, Aggression = 0.55f, Courage = 0.55f, TacticalNoise = 0.15f };
        }

        private float DefaultHealthForTier(string tier)
        {
            if (string.Equals(tier, "casual", StringComparison.OrdinalIgnoreCase))
            {
                return 100f;
            }

            if (string.Equals(tier, "dangerous", StringComparison.OrdinalIgnoreCase))
            {
                return 120f;
            }

            return BotDefaultAverageHealth;
        }

        private void StartAimWarmup(BotRuntime runtime, ulong targetUserId, float now)
        {
            if (runtime == null || targetUserId == 0UL)
            {
                return;
            }

            if (runtime.AimWarmupTargetUserId == targetUserId && runtime.AimWarmupStartedAt > 0f)
            {
                return;
            }

            runtime.AimWarmupTargetUserId = targetUserId;
            runtime.AimWarmupStartedAt = now;
            runtime.CurrentAimErrorDegrees = AimErrorDegreesAt(runtime, now);
        }

        private float AimWarmupProgress(BotRuntime runtime, float now)
        {
            var duration = runtime?.Skill == null ? 0f : runtime.Skill.AimWarmupSeconds;

            if (duration <= 0.01f)
            {
                return 1f;
            }

            var startedAt = runtime.AimWarmupStartedAt > 0f ? runtime.AimWarmupStartedAt : runtime.Memory.LastTargetSwitchAt;

            if (startedAt <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01((now - startedAt) / duration);
        }

        private float AimErrorDegreesAt(BotRuntime runtime, float now)
        {
            var skill = runtime?.Skill ?? DefaultSkillDefinition(runtime?.SkillTier);
            var baseError = Mathf.Clamp(skill?.AimErrorDegrees ?? 0f, 0f, 45f);
            var warmupExtra = Mathf.Clamp(skill?.AimWarmupInitialExtraDegrees ?? 0f, 0f, 45f);
            var progress = AimWarmupProgress(runtime, now);
            return Mathf.Clamp(baseError + warmupExtra * (1f - progress), 0f, 45f);
        }

        private string AimStatus(BotRuntime runtime, float now)
        {
            if (runtime == null)
            {
                return "none";
            }

            var error = AimErrorDegreesAt(runtime, now);
            runtime.CurrentAimErrorDegrees = error;
            return $"{error.ToString("0.0", CultureInfo.InvariantCulture)}deg/{(AimWarmupProgress(runtime, now) * 100f).ToString("0", CultureInfo.InvariantCulture)}%";
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

        private BotAvatarConfig ChooseBotAvatar(string displayName, int teamId)
        {
            var avatars = config?.BotKillIntegration?.BotAvatars;

            if (avatars == null || avatars.Count == 0)
            {
                return null;
            }

            var seed = unchecked((uint)((displayName ?? "").GetHashCode() ^ teamId ^ activeBots.Count));
            return avatars[(int)(seed % (uint)avatars.Count)];
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

                if (!TryProjectToLandSurface(ref candidate))
                {
                    continue;
                }

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

                if (!TryProjectToLandSurface(ref candidate))
                {
                    continue;
                }

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
                if (!TryProjectToLandSurface(ref candidate))
                {
                    continue;
                }

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

        private bool TryProjectToLandSurface(ref Vector3 position, float verticalOffset = 0.25f)
        {
            position.y = TerrainHeight(position) + verticalOffset;

            if (config?.Spawn?.UsePhysicsSurfaceSpawnChecks != true)
            {
                return true;
            }

            if (!TryGetPhysicalLandSurface(position, out var hit))
            {
                return true;
            }

            var tolerance = Math.Max(0.25f, config.Spawn.MaximumPhysicalSurfaceMismatch);

            if (hit.point.y > position.y - tolerance)
            {
                position.y = hit.point.y + verticalOffset;
                return true;
            }

            return position.y - hit.point.y <= tolerance;
        }

        private bool TryGetPhysicalLandSurface(Vector3 position, out RaycastHit hit)
        {
            hit = default(RaycastHit);

            if (config?.Spawn?.UsePhysicsSurfaceSpawnChecks != true)
            {
                return false;
            }

            var mask = LayerMask.GetMask("Terrain", "World");

            if (mask == 0)
            {
                return false;
            }

            var height = Math.Max(24f, config.Spawn.PhysicsSurfaceRaycastHeight);
            var originY = Math.Max(position.y, TerrainHeight(position)) + height;
            var distance = height + Math.Max(32f, config.Spawn.NavmeshSampleDistance + 16f);
            var origin = new Vector3(position.x, originY, position.z);

            try
            {
                return Physics.Raycast(origin, Vector3.down, out hit, distance, mask, QueryTriggerInteraction.Ignore);
            }
            catch
            {
                return false;
            }
        }

        private bool IsBelowPhysicalSurface(Vector3 position)
        {
            if (config?.Spawn?.RequireLandSpawns != true || config.Spawn.UsePhysicsSurfaceSpawnChecks != true)
            {
                return false;
            }

            if (!TryGetPhysicalLandSurface(position, out var hit))
            {
                return false;
            }

            return hit.point.y - position.y > Math.Max(0.25f, config.Spawn.MaximumPhysicalSurfaceMismatch);
        }

        private string PhysicalSurfaceDiagnostics(Vector3 position)
        {
            if (config?.Spawn?.UsePhysicsSurfaceSpawnChecks != true)
            {
                return "disabled";
            }

            if (!TryGetPhysicalLandSurface(position, out var hit))
            {
                return "none";
            }

            return $"{hit.point.y:0.0}, physDelta={(position.y - hit.point.y):0.0}";
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
            return $"terrain={terrain:0.0}, terrainDelta={terrainDelta:0.0}, physicalSurface={PhysicalSurfaceDiagnostics(position)}, belowTerrain={IsBelowTerrain(position)}, belowPhysical={IsBelowPhysicalSurface(position)}, waterMap={(float.IsNaN(waterMap) ? "n/a" : waterMap.ToString("0.0", CultureInfo.InvariantCulture))}, waterSurface={(float.IsNaN(waterSurface) ? "n/a" : waterSurface.ToString("0.0", CultureInfo.InvariantCulture))}, underwater={IsUnderWater(position)}, safeZone={IsBlockedSafeZoneSpawn(position)}, baseRestricted={IsBaseRestrictedPosition(position)}, unityNavSample={sample}";
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
            return IsUnderWater(position) || IsBelowTerrain(position) || IsBelowPhysicalSurface(position) || IsBlockedSafeZoneSpawn(position) || IsBaseRestrictedPosition(position);
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

        private bool TryFindOutsideBaseHoldPoint(Vector3 botPosition, Vector3 threatPosition, BotRuntime runtime, float now, out Vector3 holdPoint)
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

                if (!TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), now, out var sampled))
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
                || IsBelowPhysicalSurface(bot.transform.position)
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
                    DebugWarning($"invalid-position:{runtime.BotKey}", $"Roam bot {runtime.DisplayName} entered an invalid terrain/nav position at {FormatVector(bot.transform.position)} ({PositionDiagnostics(bot.transform.position)}); combat paused.");
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
                    var stuckSeconds = runtime.Movement.StuckSince <= 0f ? 0f : now - runtime.Movement.StuckSince;
                    DespawnBot(bot, $"hard-stuck pathing ({runtime.ConsecutiveFailedPaths} failed paths, stuck {stuckSeconds.ToString("0", CultureInfo.InvariantCulture)}s, state={runtime.State})");
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

                ApplyLearnedBehaviorScoring(bot, runtime, candidates, now);
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

                    if (runtime.CurrentDestination != Vector3.zero)
                    {
                        board.DestinationClaims[runtime.BotKey] = runtime.CurrentDestination;
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
                    var skillColor = SkillNameplateColorHex(runtime);
                    var text = $"<size={config.Debug.DebugNameplateFontSize}><color={skillColor}>{BotClanLabel(runtime)}</color> <color=#ffffff>{runtime.State} {distanceLabel}m</color></size>";
                    viewer.SendConsoleCommand("ddraw.text", duration, SkillNameplateDrawColor(runtime), position, text);
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

            if (ShouldSuppressDebugBotSidePanel(viewer))
            {
                DestroyDebugBotPanel(viewer);
                return;
            }

            EnsureDebugBotSidePanel(viewer);
            CuiHelper.DestroyUi(viewer, DebugBotPanelTextUi);
            var container = new CuiElementContainer();
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
            }, DebugBotPanelUi, DebugBotPanelTextUi);

            CuiHelper.AddUi(viewer, container);
        }

        private bool ShouldSuppressDebugBotSidePanel(BasePlayer viewer)
        {
            if (viewer == null || adminPanelViewers.Contains(viewer.userID))
            {
                return true;
            }

            if (debugSidePanelSuppressedUntil.TryGetValue(viewer.userID, out var suppressedUntil))
            {
                if (Time.realtimeSinceStartup <= suppressedUntil)
                {
                    return true;
                }

                debugSidePanelSuppressedUntil.Remove(viewer.userID);
            }

            return false;
        }

        private void SuppressDebugSidePanelForMenuCommand(BasePlayer player, string command)
        {
            if (player == null)
            {
                return;
            }

            var normalized = NormalizeDebugSidePanelMenuCommand(command);
            if (!IsDebugSidePanelMenuCommand(normalized))
            {
                return;
            }

            var isCloseCommand = IsDebugSidePanelMenuCloseCommand(normalized);
            var seconds = isCloseCommand
                ? DebugSidePanelMenuCloseSuppressSeconds
                : DebugSidePanelMenuSuppressSeconds;
            var suppressedUntil = Time.realtimeSinceStartup + seconds;

            if (isCloseCommand || !debugSidePanelSuppressedUntil.TryGetValue(player.userID, out var currentUntil) || currentUntil < suppressedUntil)
            {
                debugSidePanelSuppressedUntil[player.userID] = suppressedUntil;
            }

            DestroyDebugBotPanel(player);
        }

        private string NormalizeDebugSidePanelMenuCommand(string command)
        {
            var normalized = (command ?? "").Trim().TrimStart('/').ToLowerInvariant();
            return normalized.StartsWith("global.", StringComparison.Ordinal)
                ? normalized.Substring("global.".Length)
                : normalized;
        }

        private bool IsDebugSidePanelMenuCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || command.StartsWith("raidbots", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (var prefix in DebugSidePanelMenuCommandPrefixes)
            {
                if (command.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (var chatCommand in DebugSidePanelMenuChatCommands)
            {
                if (command.Equals(chatCommand, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsDebugSidePanelMenuCloseCommand(string command)
        {
            return command.Equals("close", StringComparison.Ordinal)
                || command.EndsWith(".close", StringComparison.Ordinal);
        }

        private void EnsureDebugBotSidePanel(BasePlayer viewer)
        {
            if (viewer == null || debugBotPanelViewers.Contains(viewer.userID))
            {
                return;
            }

            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0.03 0.04 0.05 0.76" },
                RectTransform = { AnchorMin = "0.755 0.52", AnchorMax = "0.995 0.92" }
            }, "Hud", DebugBotPanelUi);

            CuiHelper.AddUi(viewer, container);
            debugBotPanelViewers.Add(viewer.userID);
        }

        private void DestroyDebugBotPanel(BasePlayer player)
        {
            if (player != null)
            {
                debugBotPanelViewers.Remove(player.userID);
                CuiHelper.DestroyUi(player, DebugBotPanelTextUi);
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

        private void OpenAdminPanel(BasePlayer player, string tab = "overview")
        {
            if (player == null)
            {
                return;
            }

            DrawAdminPanel(player, tab);
        }

        private void DrawAdminPanel(BasePlayer player, string tab)
        {
            if (player == null)
            {
                return;
            }

            tab = NormalizeAdminTab(tab);
            RefreshEligibleKits();
            CleanupInactiveBots();
            DestroyAdminPanel(player);
            adminPanelViewers.Add(player.userID);
            DestroyDebugBotPanel(player);

            var container = new CuiElementContainer();
            var panel = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.02 0.025 0.03 0.95" },
                RectTransform = { AnchorMin = "0.145 0.095", AnchorMax = "0.855 0.91" }
            }, "Overlay", AdminPanelUi);

            AddAdminLabel(container, panel, "<b>Raidlands Roam Bots</b>", 0.025f, 0.935f, 0.50f, 0.985f, 18, TextAnchor.MiddleLeft, "0.95 0.98 1 1");
            AddAdminLabel(container, panel, AdminHeaderStatus(), 0.50f, 0.935f, 0.88f, 0.985f, 11, TextAnchor.MiddleRight, "0.72 0.78 0.84 1");
            AddAdminButton(container, panel, "X", "raidbots.ui close", 0.91f, 0.94f, 0.98f, 0.985f, "0.45 0.12 0.12 0.95", 13);

            var tabX = 0.025f;
            var tabWidth = 0.092f;

            foreach (var adminTab in AdminPanelTabs)
            {
                var selected = adminTab == tab;
                AddAdminButton(
                    container,
                    panel,
                    AdminTabLabel(adminTab),
                    $"raidbots.ui tab {adminTab}",
                    tabX,
                    0.875f,
                    tabX + tabWidth,
                    0.925f,
                    selected ? "0.14 0.34 0.52 0.98" : "0.11 0.13 0.16 0.94",
                    10);
                tabX += tabWidth + 0.006f;
            }

            switch (tab)
            {
                case "population":
                    BuildAdminPopulationTab(container, panel);
                    break;
                case "spawn":
                    BuildAdminSpawnTab(container, panel);
                    break;
                case "ai":
                    BuildAdminAiTab(container, panel);
                    break;
                case "utility":
                    BuildAdminUtilityTab(container, panel);
                    break;
                case "rewards":
                    BuildAdminRewardsTab(container, panel);
                    break;
                case "advisor":
                    BuildAdminAdvisorTab(container, panel);
                    break;
                case "learning":
                    BuildAdminLearningTab(container, panel);
                    break;
                case "debug":
                    BuildAdminDebugTab(container, panel);
                    break;
                case "danger":
                    BuildAdminDangerTab(container, panel);
                    break;
                default:
                    BuildAdminOverviewTab(container, panel);
                    break;
            }

            CuiHelper.AddUi(player, container);
        }

        private void BuildAdminOverviewTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Live Status", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminMetric(container, panel, "Enabled", AdminOnOff(config.Enabled), 0.05f, 0.76f);
            AddAdminMetric(container, panel, "Active", $"{activeBots.Count}/{TargetPopulation()}", 0.29f, 0.76f);
            AddAdminMetric(container, panel, "Max", config.MaxAllowedPopulation.ToString(CultureInfo.InvariantCulture), 0.53f, 0.76f);
            AddAdminMetric(container, panel, "Mode", config.Spawn.SpawnMode, 0.77f, 0.76f);
            AddAdminMetric(container, panel, "Anchor", SpawnAnchorLabel(), 0.05f, 0.69f);
            AddAdminMetric(container, panel, "Kits", eligibleKits.Count.ToString(CultureInfo.InvariantCulture), 0.29f, 0.69f);
            AddAdminMetric(container, panel, "Advisor", $"{config.DecisionAdvisor.Provider}/{config.DecisionAdvisor.Mode}", 0.53f, 0.69f);
            AddAdminMetric(container, panel, "Debug", AdminOnOff(config.Debug.DebugBotNameplates || config.Debug.DebugBotSidePanel), 0.77f, 0.69f);

            AddAdminButton(container, panel, "Enable", "raidbots.ui enable overview", 0.05f, 0.60f, 0.20f, 0.655f, "0.14 0.42 0.22 0.96");
            AddAdminButton(container, panel, "Disable", "raidbots.ui disable overview", 0.215f, 0.60f, 0.37f, 0.655f, "0.42 0.18 0.12 0.96");
            AddAdminButton(container, panel, "Live 50/200", "raidbots.ui preset-live overview", 0.385f, 0.60f, 0.56f, 0.655f, "0.16 0.31 0.48 0.96");
            AddAdminButton(container, panel, "Spawn 1", "raidbots.ui spawn 1 overview", 0.575f, 0.60f, 0.73f, 0.655f, "0.16 0.29 0.19 0.96");
            AddAdminButton(container, panel, "Spawn 10", "raidbots.ui spawn 10 overview", 0.745f, 0.60f, 0.90f, 0.655f, "0.16 0.29 0.19 0.96");

            AddAdminIntControl(container, panel, "Target", "target", TargetPopulation(), 5, 25, 0.05f, 0.50f, 0.47f, "overview");
            AddAdminIntControl(container, panel, "Max", "max_population", config.MaxAllowedPopulation, 25, 50, 0.53f, 0.50f, 0.95f, "overview");
            AddAdminToggle(container, panel, "Land only", "require_land", config.Spawn.RequireLandSpawns, 0.05f, 0.41f, 0.29f, "overview");
            AddAdminToggle(container, panel, "Random fallback", "random_fallback", config.Spawn.UseRandomLandFallback, 0.32f, 0.41f, 0.56f, "overview");
            AddAdminToggle(container, panel, "Nameplates", "nameplates", config.Debug.DebugBotNameplates, 0.59f, 0.41f, 0.83f, "overview");

            AddAdminButton(container, panel, "Diag", "raidbots.diag", 0.05f, 0.29f, 0.20f, 0.345f, "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "List", "raidbots.list", 0.215f, 0.29f, 0.37f, 0.345f, "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Decisions", "raidbots.decisions last 10", 0.385f, 0.29f, 0.56f, 0.345f, "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Refresh", "raidbots.ui refresh overview", 0.575f, 0.29f, 0.73f, 0.345f, "0.18 0.21 0.26 0.96");
        }

        private void BuildAdminPopulationTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Population And Kit Mix", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminIntControl(container, panel, "Target", "target", TargetPopulation(), 5, 25, 0.05f, 0.75f, 0.47f, "population");
            AddAdminIntControl(container, panel, "Minimum", "min_population", config.MinAllowedPopulation, 1, 10, 0.53f, 0.75f, 0.95f, "population");
            AddAdminIntControl(container, panel, "Maximum", "max_population", config.MaxAllowedPopulation, 25, 50, 0.05f, 0.665f, 0.47f, "population");
            AddAdminIntControl(container, panel, "High tier", "high_tier_weight", config.HighTierKitWeight, 1, 10, 0.53f, 0.665f, 0.95f, "population");
            AddAdminIntControl(container, panel, "Solo weight", "solo_weight", TeamWeight("solo"), 5, 25, 0.05f, 0.58f, 0.47f, "population");
            AddAdminIntControl(container, panel, "Duo weight", "duo_weight", TeamWeight("duo"), 5, 25, 0.53f, 0.58f, 0.95f, "population");
            AddAdminIntControl(container, panel, "Trio weight", "trio_weight", TeamWeight("trio"), 5, 25, 0.05f, 0.495f, 0.47f, "population");
            AddAdminFloatControl(container, panel, "Maintain sec", "maintain_interval", config.MaintainIntervalSeconds, 5f, 15f, 0.53f, 0.495f, 0.95f, "population");
            AddAdminFloatControl(container, panel, "Respawn sec", "respawn_delay", config.RespawnDelaySeconds, 5f, 15f, 0.05f, 0.41f, 0.47f, "population");
            AddAdminFloatControl(container, panel, "Retry sec", "spawn_retry", config.SpawnFailureRetrySeconds, 15f, 60f, 0.53f, 0.41f, 0.95f, "population");
            AddAdminButton(container, panel, "Apply Live 50/200", "raidbots.ui preset-live population", 0.05f, 0.29f, 0.29f, 0.345f, "0.16 0.31 0.48 0.96");
            AddAdminButton(container, panel, "Enable", "raidbots.ui enable population", 0.32f, 0.29f, 0.47f, 0.345f, "0.14 0.42 0.22 0.96");
            AddAdminButton(container, panel, "Disable", "raidbots.ui disable population", 0.50f, 0.29f, 0.65f, 0.345f, "0.42 0.18 0.12 0.96");
        }

        private void BuildAdminSpawnTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Spawn Routing", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminButton(container, panel, "Near Players", "raidbots.ui mode near_players spawn", 0.05f, 0.76f, 0.22f, 0.815f, config.Spawn.SpawnMode == SpawnModeNearPlayers ? "0.14 0.34 0.52 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Random Land", "raidbots.ui mode random spawn", 0.235f, 0.76f, 0.40f, 0.815f, config.Spawn.SpawnMode == SpawnModeRandom ? "0.14 0.34 0.52 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Clear Anchor", "raidbots.ui anchor-clear spawn", 0.415f, 0.76f, 0.58f, 0.815f, "0.18 0.21 0.26 0.96");
            AddAdminLabel(container, panel, $"Anchor: {AdminEscape(SpawnAnchorLabel())}", 0.61f, 0.76f, 0.95f, 0.815f, 11, TextAnchor.MiddleLeft, "0.76 0.82 0.88 1");

            AddAdminToggle(container, panel, "Random fallback", "random_fallback", config.Spawn.UseRandomLandFallback, 0.05f, 0.67f, 0.29f, "spawn");
            AddAdminToggle(container, panel, "Generated near", "generated_near", config.Spawn.UseGeneratedPositionsNearPlayers, 0.32f, 0.67f, 0.56f, "spawn");
            AddAdminToggle(container, panel, "Land only", "require_land", config.Spawn.RequireLandSpawns, 0.59f, 0.67f, 0.83f, "spawn");
            AddAdminToggle(container, panel, "Physics surface", "physics_surface", config.Spawn.UsePhysicsSurfaceSpawnChecks, 0.05f, 0.59f, 0.29f, "spawn");
            AddAdminToggle(container, panel, "Avoid safe zones", "avoid_safe_zones", config.Spawn.AvoidSafeZoneSpawns, 0.32f, 0.59f, 0.56f, "spawn");
            AddAdminToggle(container, panel, "Ignore safe players", "ignore_safe_zone_players", config.Spawn.IgnorePlayersInSafeZones, 0.59f, 0.59f, 0.83f, "spawn");

            AddAdminIntControl(container, panel, "Position tries", "max_position_attempts", config.Spawn.MaxPositionAttempts, 10, 50, 0.05f, 0.48f, 0.47f, "spawn");
            AddAdminIntControl(container, panel, "Near tries", "near_attempts", config.Spawn.NearPlayerAttempts, 10, 50, 0.53f, 0.48f, 0.95f, "spawn");
            AddAdminFloatControl(container, panel, "Near min", "near_min", config.Spawn.NearPlayerMinDistance, 10f, 50f, 0.05f, 0.395f, 0.47f, "spawn");
            AddAdminFloatControl(container, panel, "Near max", "near_max", config.Spawn.NearPlayerMaxDistance, 10f, 50f, 0.53f, 0.395f, 0.95f, "spawn");
            AddAdminFloatControl(container, panel, "Nav sample", "nav_sample", config.Spawn.NavmeshSampleDistance, 2f, 10f, 0.05f, 0.31f, 0.47f, "spawn");
            AddAdminFloatControl(container, panel, "Group radius", "group_radius", config.Spawn.GroupSpawnRadius, 1f, 5f, 0.53f, 0.31f, 0.95f, "spawn");
            AddAdminFloatControl(container, panel, "Safe buffer", "safe_buffer", config.Spawn.SafeZoneSpawnBufferDistance, 10f, 25f, 0.05f, 0.225f, 0.47f, "spawn");
        }

        private void BuildAdminAiTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Tactical Brain", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminToggle(container, panel, "LOS to shoot", "los_shoot", config.AI.RequireLineOfSightToShoot, 0.05f, 0.76f, 0.27f, "ai");
            AddAdminToggle(container, panel, "Hearing", "allow_hearing", config.AI.AllowHearing, 0.29f, 0.76f, 0.51f, "ai");
            AddAdminToggle(container, panel, "Cover", "allow_cover", config.AI.AllowCover, 0.53f, 0.76f, 0.75f, "ai");
            AddAdminToggle(container, panel, "Flanking", "allow_flanking", config.AI.AllowFlanking, 0.77f, 0.76f, 0.95f, "ai");
            AddAdminToggle(container, panel, "Grenades", "allow_grenades", config.AI.AllowGrenades, 0.05f, 0.685f, 0.27f, "ai");
            AddAdminToggle(container, panel, "Smoke", "allow_smoke", config.AI.AllowSmoke, 0.29f, 0.685f, 0.51f, "ai");
            AddAdminToggle(container, panel, "Barricades", "allow_barricades", config.AI.AllowBarricades, 0.53f, 0.685f, 0.75f, "ai");
            AddAdminToggle(container, panel, "Base avoid", "base_avoidance", config.AI.DoNotEnterBases, 0.77f, 0.685f, 0.95f, "ai");
            AddAdminToggle(container, panel, "Jiggle", "jiggle", config.AI.AllowJigglePeeking, 0.05f, 0.61f, 0.27f, "ai");
            AddAdminToggle(container, panel, "Jump peek", "jump_peek", config.AI.AllowJumpPeekApproximation, 0.29f, 0.61f, 0.51f, "ai");
            AddAdminToggle(container, panel, "Foliage", "foliage", config.AI.FoliageBlocksVision, 0.53f, 0.61f, 0.75f, "ai");
            AddAdminToggle(container, panel, "Foliage terrain", "foliage_terrain", config.AI.FoliageTerrainSampling, 0.77f, 0.61f, 0.95f, "ai");

            AddAdminFloatControl(container, panel, "Vision", "vision_range", config.AI.VisionRange, 10f, 50f, 0.05f, 0.50f, 0.47f, "ai");
            AddAdminFloatControl(container, panel, "FOV", "vision_fov", config.AI.VisionFovDegrees, 10f, 30f, 0.53f, 0.50f, 0.95f, "ai");
            AddAdminFloatControl(container, panel, "Expose seen", "exposed_min", config.AI.MinimumExposedTargetFraction, 0.05f, 0.1f, 0.05f, 0.415f, 0.47f, "ai");
            AddAdminFloatControl(container, panel, "Expose shoot", "exposed_shoot", config.AI.MinimumExposedTargetFractionToShoot, 0.05f, 0.1f, 0.53f, 0.415f, 0.95f, "ai");
            AddAdminFloatControl(container, panel, "Memory", "target_memory", config.AI.TargetMemorySeconds, 5f, 15f, 0.05f, 0.33f, 0.47f, "ai");
            AddAdminFloatControl(container, panel, "Search", "search_last_seen", config.AI.SearchLastSeenSeconds, 5f, 15f, 0.53f, 0.33f, 0.95f, "ai");
            AddAdminFloatControl(container, panel, "Gun hear", "hearing_gun", config.AI.UnsuppressedGunshotHearingRange, 10f, 50f, 0.05f, 0.245f, 0.47f, "ai");
            AddAdminFloatControl(container, panel, "Supp hear", "hearing_suppressed", config.AI.SuppressedGunshotHearingRange, 5f, 25f, 0.53f, 0.245f, 0.95f, "ai");
            AddAdminFloatControl(container, panel, "Cover radius", "cover_radius", config.AI.CoverSearchRadius, 2f, 10f, 0.05f, 0.16f, 0.47f, "ai");
            AddAdminFloatControl(container, panel, "Flank dist", "flank_distance", config.AI.SquadFlankDistance, 2f, 10f, 0.53f, 0.16f, 0.95f, "ai");
            AddAdminToggle(container, panel, "Clan wars", "bot_clan_wars", config.AI.AllowBotClanWars, 0.05f, 0.075f, 0.27f, "ai");
        }

        private void BuildAdminUtilityTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Utility, Healing, And Restrictions", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminToggle(container, panel, "Grant meds", "grant_meds", config.AI.GrantBotMedicalItems, 0.05f, 0.76f, 0.27f, "utility");
            AddAdminToggle(container, panel, "Real med heal", "real_meds", config.AI.UseRealMedicalItemsForCoverHeal, 0.29f, 0.76f, 0.51f, "utility");
            AddAdminToggle(container, panel, "Auto reload", "auto_reload", config.AI.AutoReloadBotWeapons, 0.53f, 0.76f, 0.75f, "utility");
            AddAdminToggle(container, panel, "Base avoid", "base_avoidance", config.AI.DoNotEnterBases, 0.77f, 0.76f, 0.95f, "utility");
            AddAdminIntControl(container, panel, "Utility cap", "max_utility", config.AI.MaxActiveBotUtilityProjectiles, 1, 5, 0.05f, 0.65f, 0.47f, "utility");
            AddAdminIntControl(container, panel, "Barricade cap", "max_barricades", config.AI.MaxActiveBotBarricades, 1, 5, 0.53f, 0.65f, 0.95f, "utility");
            AddAdminIntControl(container, panel, "Med amount", "bot_med_amount", config.AI.BotMedicalItemAmount, 1, 3, 0.05f, 0.565f, 0.47f, "utility");
            AddAdminIntControl(container, panel, "Stuck paths", "hard_stuck_paths", config.AI.HardStuckFailedPathsToDespawn, 5, 20, 0.53f, 0.565f, 0.95f, "utility");
            AddAdminFloatControl(container, panel, "Stuck despawn", "hard_stuck_seconds", config.AI.HardStuckDespawnSeconds, 30f, 60f, 0.05f, 0.48f, 0.47f, "utility");
            AddAdminFloatControl(container, panel, "Grenade CD", "grenade_cooldown", config.AI.GrenadeCooldownSeconds, 5f, 15f, 0.53f, 0.48f, 0.95f, "utility");
            AddAdminFloatControl(container, panel, "Team nade CD", "team_grenade_cooldown", config.AI.TeamGrenadeCooldownSeconds, 2f, 10f, 0.05f, 0.395f, 0.47f, "utility");
            AddAdminFloatControl(container, panel, "Grenade max", "grenade_max", config.AI.GrenadeMaxThrowDistance, 2f, 10f, 0.53f, 0.395f, 0.95f, "utility");
            AddAdminFloatControl(container, panel, "Smoke max", "smoke_max", config.AI.SmokeMaxThrowDistance, 2f, 10f, 0.05f, 0.31f, 0.47f, "utility");
            AddAdminFloatControl(container, panel, "Barricade CD", "barricade_cooldown", config.AI.BarricadeCooldownSeconds, 2f, 10f, 0.53f, 0.31f, 0.95f, "utility");
            AddAdminFloatControl(container, panel, "Cover heal", "cover_heal", config.AI.LowHealthCoverHealPerSecond, 1f, 5f, 0.05f, 0.225f, 0.47f, "utility");
            AddAdminFloatControl(container, panel, "Base radius", "base_radius", config.AI.BaseAvoidanceRadius, 1f, 5f, 0.53f, 0.225f, 0.95f, "utility");
            AddAdminFloatControl(container, panel, "Base hold", "base_hold", config.AI.BaseHoldSeconds, 2f, 10f, 0.05f, 0.14f, 0.47f, "utility");
        }

        private void BuildAdminRewardsTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Rewards And Kill Messages", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminToggle(container, panel, "Kill chat", "kill_chat", config.BotKillIntegration.BroadcastPlayerLikeKillMessages, 0.05f, 0.76f, 0.27f, "rewards");
            AddAdminToggle(container, panel, "Suppress DeathNotes", "deathnotes", config.BotKillIntegration.SuppressDeathNotesForRoamBotKills, 0.29f, 0.76f, 0.55f, "rewards");
            AddAdminToggle(container, panel, "Award RP", "award_rp", config.BotKillIntegration.AwardServerRewardsRp, 0.57f, 0.76f, 0.77f, "rewards");
            AddAdminToggle(container, panel, "Tell RP", "tell_rp", config.BotKillIntegration.TellKillerAboutRpReward, 0.79f, 0.76f, 0.95f, "rewards");
            AddAdminIntControl(container, panel, "RP reward", "rp_reward", config.BotKillIntegration.ServerRewardsRpPerBotKill, 1, 10, 0.05f, 0.65f, 0.47f, "rewards");
            AddAdminLabel(container, panel, $"Chat format: {AdminEscape(AdminShorten(config.BotKillIntegration.ChatFormat, 110))}", 0.05f, 0.52f, 0.95f, 0.58f, 10, TextAnchor.MiddleLeft, "0.74 0.80 0.86 1");
            AddAdminLabel(container, panel, $"Kill message: {AdminEscape(AdminShorten(config.BotKillIntegration.KillMessage, 110))}", 0.05f, 0.45f, 0.95f, 0.51f, 10, TextAnchor.MiddleLeft, "0.74 0.80 0.86 1");
            AddAdminLabel(container, panel, $"RP message: {AdminEscape(AdminShorten(config.BotKillIntegration.RpRewardMessage, 110))}", 0.05f, 0.38f, 0.95f, 0.44f, 10, TextAnchor.MiddleLeft, "0.74 0.80 0.86 1");
        }

        private void BuildAdminAdvisorTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Decision Advisor", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminButton(container, panel, "Off", "raidbots.ui advisor off advisor", 0.05f, 0.76f, 0.18f, 0.815f, config.DecisionAdvisor.Enabled ? "0.18 0.21 0.26 0.96" : "0.42 0.18 0.12 0.96");
            AddAdminButton(container, panel, "Fallback", "raidbots.ui advisor fallback advisor", 0.195f, 0.76f, 0.34f, 0.815f, config.DecisionAdvisor.Mode == AdvisorModeFallbackOnly ? "0.14 0.34 0.52 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Shadow", "raidbots.ui advisor shadow advisor", 0.355f, 0.76f, 0.50f, 0.815f, config.DecisionAdvisor.Mode == AdvisorModeShadow ? "0.14 0.34 0.52 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Canary", "raidbots.ui advisor canary advisor", 0.515f, 0.76f, 0.66f, 0.815f, config.DecisionAdvisor.Mode == AdvisorModeCanary ? "0.14 0.34 0.52 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Stats", "raidbots.advisor stats", 0.675f, 0.76f, 0.82f, 0.815f, "0.18 0.21 0.26 0.96");

            AddAdminLabel(container, panel, AdminEscape(AdminShorten(AdvisorStatusLine(), 150)), 0.05f, 0.675f, 0.95f, 0.73f, 10, TextAnchor.MiddleLeft, "0.76 0.82 0.88 1");
            AddAdminToggle(container, panel, "Enabled", "advisor_enabled", config.DecisionAdvisor.Enabled, 0.05f, 0.595f, 0.27f, "advisor");
            AddAdminToggle(container, panel, "Shadow", "advisor_shadow", config.DecisionAdvisor.ShadowMode, 0.29f, 0.595f, 0.51f, "advisor");
            AddAdminToggle(container, panel, "Fallback fail", "advisor_fallback", config.DecisionAdvisor.FallbackOnAnyFailure, 0.53f, 0.595f, 0.75f, "advisor");
            AddAdminToggle(container, panel, "Trace", "advisor_trace", config.DecisionAdvisor.LogDecisionTraces, 0.77f, 0.595f, 0.95f, "advisor");
            AddAdminToggle(container, panel, "Schema", "advisor_schema", config.DecisionAdvisor.UseStructuredResponseSchema, 0.05f, 0.52f, 0.27f, "advisor");
            AddAdminToggle(container, panel, "Ask stuck", "advisor_ask_stuck", config.DecisionAdvisor.AskWhenBotIsStuck, 0.29f, 0.52f, 0.51f, "advisor");
            AddAdminToggle(container, panel, "Ask close", "advisor_ask_close", config.DecisionAdvisor.AskWhenActionScoresAreClose, 0.53f, 0.52f, 0.75f, "advisor");
            AddAdminToggle(container, panel, "Ask squad", "advisor_ask_squad", config.DecisionAdvisor.AskWhenSquadStateChangesSharply, 0.77f, 0.52f, 0.95f, "advisor");
            AddAdminFloatControl(container, panel, "Confidence", "advisor_confidence", config.DecisionAdvisor.MinimumConfidence, 0.05f, 0.1f, 0.05f, 0.40f, 0.47f, "advisor");
            AddAdminFloatControl(container, panel, "Per bot sec", "advisor_min_seconds", config.DecisionAdvisor.MinSecondsBetweenRequestsPerBot, 1f, 5f, 0.53f, 0.40f, 0.95f, "advisor");
            AddAdminIntControl(container, panel, "Timeout ms", "advisor_timeout", config.DecisionAdvisor.TimeoutMilliseconds, 100, 500, 0.05f, 0.315f, 0.47f, "advisor");
            AddAdminIntControl(container, panel, "Concurrent", "advisor_concurrent", config.DecisionAdvisor.MaxConcurrentRequests, 1, 2, 0.53f, 0.315f, 0.95f, "advisor");
            AddAdminIntControl(container, panel, "Events", "advisor_events", config.DecisionAdvisor.MaxRecentEventsInRequest, 2, 8, 0.05f, 0.23f, 0.47f, "advisor");
            AddAdminIntControl(container, panel, "Candidates", "advisor_candidates", config.DecisionAdvisor.MaxCandidateActions, 1, 4, 0.53f, 0.23f, 0.95f, "advisor");
            AddAdminFloatControl(container, panel, "Player gate", "advisor_player_gate", config.DecisionAdvisor.RequireRealPlayerWithinMeters, 50f, 100f, 0.05f, 0.145f, 0.47f, "advisor");
            AddAdminToggle(container, panel, "Engaged only", "advisor_engaged_only", config.DecisionAdvisor.RequireActivePlayerEngagement, 0.53f, 0.145f, 0.77f, "advisor");
        }

        private void BuildAdminLearningTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Player Observation Learning", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminButton(container, panel, "Observe On", "raidbots.learn observe on", 0.05f, 0.76f, 0.20f, 0.815f, config.Learning.Enabled ? "0.14 0.42 0.22 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Observe Off", "raidbots.learn observe off", 0.215f, 0.76f, 0.37f, 0.815f, !config.Learning.Enabled ? "0.42 0.18 0.12 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Build Global", "raidbots.learn build", 0.385f, 0.76f, 0.56f, 0.815f, "0.16 0.31 0.48 0.96");
            AddAdminButton(container, panel, "Report", "raidbots.learn report 60", 0.575f, 0.76f, 0.72f, 0.815f, "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Export", "raidbots.learn export", 0.735f, 0.76f, 0.88f, 0.815f, "0.18 0.21 0.26 0.96");

            AddAdminMetric(container, panel, "Mode", config.Learning.ApplyMode, 0.05f, 0.67f);
            AddAdminMetric(container, panel, "Allowed", config.Learning.ObservedPlayerSteamIds.Count.ToString(CultureInfo.InvariantCulture), 0.29f, 0.67f);
            AddAdminMetric(container, panel, "Skill models", behaviorModels?.skill_models?.Count.ToString(CultureInfo.InvariantCulture) ?? "0", 0.53f, 0.67f);
            AddAdminMetric(container, panel, "Profiles", behaviorModels?.player_profiles?.Count.ToString(CultureInfo.InvariantCulture) ?? "0", 0.77f, 0.67f);

            AddAdminButton(container, panel, "Apply Off", "raidbots.learn apply off", 0.05f, 0.56f, 0.20f, 0.615f, config.Learning.ApplyMode == LearningApplyOff ? "0.42 0.18 0.12 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Shadow", "raidbots.learn apply shadow", 0.215f, 0.56f, 0.37f, 0.615f, config.Learning.ApplyMode == LearningApplyShadow ? "0.14 0.34 0.52 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Global", "raidbots.learn apply global", 0.385f, 0.56f, 0.54f, 0.615f, config.Learning.ApplyMode == LearningApplyGlobal ? "0.14 0.34 0.52 0.96" : "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Profiles", "raidbots.learn apply profiles", 0.555f, 0.56f, 0.72f, 0.615f, config.Learning.ApplyMode == LearningApplyProfiles ? "0.14 0.34 0.52 0.96" : "0.18 0.21 0.26 0.96");

            AddAdminFloatControl(container, panel, "Sample sec", "learning_sample", config.Learning.SampleIntervalSeconds, 0.25f, 1f, 0.05f, 0.45f, 0.47f, "learning");
            AddAdminFloatControl(container, panel, "Outcome sec", "learning_outcome", config.Learning.OutcomeWindowSeconds, 1f, 5f, 0.53f, 0.45f, 0.95f, "learning");
            AddAdminFloatControl(container, panel, "Global delta", "learning_global_delta", config.Learning.MaximumGlobalScoreDelta, 2f, 8f, 0.05f, 0.365f, 0.47f, "learning");
            AddAdminFloatControl(container, panel, "Profile delta", "learning_profile_delta", config.Learning.MaximumProfileScoreDelta, 2f, 8f, 0.53f, 0.365f, 0.95f, "learning");

            var latestProfiles = behaviorModels?.player_profiles == null || behaviorModels.player_profiles.Count == 0
                ? "none"
                : string.Join(", ", behaviorModels.player_profiles.Keys.OrderBy(key => key).Take(5));
            var spawnWeights = config.Learning.PlayerProfileSpawnWeights == null || config.Learning.PlayerProfileSpawnWeights.Count == 0
                ? "none"
                : string.Join(", ", config.Learning.PlayerProfileSpawnWeights.OrderBy(entry => entry.Key).Take(5).Select(entry => $"{entry.Key}:{entry.Value}"));
            AddAdminLabel(container, panel, $"Recent profiles: {AdminEscape(AdminShorten(latestProfiles, 120))}", 0.05f, 0.25f, 0.95f, 0.305f, 10, TextAnchor.MiddleLeft, "0.74 0.80 0.86 1");
            AddAdminLabel(container, panel, $"Profile spawn weights: {AdminEscape(AdminShorten(spawnWeights, 120))}", 0.05f, 0.19f, 0.95f, 0.245f, 10, TextAnchor.MiddleLeft, "0.74 0.80 0.86 1");
            AddAdminLabel(container, panel, "Use console: raidbots.learn allow <player>, profile build <player> <key>, profile weight <key> <weight>.", 0.05f, 0.11f, 0.95f, 0.17f, 10, TextAnchor.MiddleLeft, "0.95 0.78 0.58 1");
        }

        private void BuildAdminDebugTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Debug Surfaces", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminButton(container, panel, "Debug All On", "raidbots.ui debug-all on debug", 0.05f, 0.76f, 0.22f, 0.815f, "0.14 0.34 0.52 0.96");
            AddAdminButton(container, panel, "Debug All Off", "raidbots.ui debug-all off debug", 0.235f, 0.76f, 0.405f, 0.815f, "0.42 0.18 0.12 0.96");
            AddAdminButton(container, panel, "Diag", "raidbots.diag", 0.42f, 0.76f, 0.56f, 0.815f, "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "List", "raidbots.list", 0.575f, 0.76f, 0.715f, 0.815f, "0.18 0.21 0.26 0.96");
            AddAdminButton(container, panel, "Decisions", "raidbots.decisions last 10", 0.73f, 0.76f, 0.90f, 0.815f, "0.18 0.21 0.26 0.96");
            AddAdminToggle(container, panel, "Spawn", "debug_spawn", config.Debug.DebugSpawnDetails, 0.05f, 0.67f, 0.27f, "debug");
            AddAdminToggle(container, panel, "Perception", "debug_perception", config.Debug.DebugPerception, 0.29f, 0.67f, 0.51f, "debug");
            AddAdminToggle(container, panel, "Tactical", "debug_tactical", config.Debug.DebugTacticalDecisions, 0.53f, 0.67f, 0.75f, "debug");
            AddAdminToggle(container, panel, "Advisor", "debug_advisor", config.Debug.DebugDecisionAdvisor, 0.77f, 0.67f, 0.95f, "debug");
            AddAdminToggle(container, panel, "Nameplates", "nameplates", config.Debug.DebugBotNameplates, 0.05f, 0.595f, 0.27f, "debug");
            AddAdminToggle(container, panel, "Side panel", "side_panel", config.Debug.DebugBotSidePanel, 0.29f, 0.595f, 0.51f, "debug");
            AddAdminToggle(container, panel, "Anchor viewer", "anchor_viewer", config.Debug.DebugUiIncludesAnchorPlayer, 0.53f, 0.595f, 0.75f, "debug");
            AddAdminToggle(container, panel, "Cover scores", "cover_scores", config.Debug.DebugCoverScores, 0.77f, 0.595f, 0.95f, "debug");
            AddAdminFloatControl(container, panel, "Refresh", "nameplate_refresh", config.Debug.DebugNameplateRefreshSeconds, 0.25f, 1f, 0.05f, 0.48f, 0.47f, "debug");
            AddAdminFloatControl(container, panel, "Duration", "nameplate_duration", config.Debug.DebugNameplateDrawDurationSeconds, 0.25f, 1f, 0.53f, 0.48f, 0.95f, "debug");
            AddAdminFloatControl(container, panel, "Height", "nameplate_height", config.Debug.DebugNameplateHeight, 0.25f, 1f, 0.05f, 0.395f, 0.47f, "debug");
            AddAdminIntControl(container, panel, "Font", "nameplate_font", config.Debug.DebugNameplateFontSize, 1, 2, 0.53f, 0.395f, 0.95f, "debug");
            AddAdminFloatControl(container, panel, "Distance", "nameplate_distance", config.Debug.DebugNameplateMaxDistance, 25f, 100f, 0.05f, 0.31f, 0.47f, "debug");
            AddAdminToggle(container, panel, "Console logs", "console_logs", config.Debug.DebugConsoleLogs, 0.53f, 0.31f, 0.78f, "debug");
        }

        private void BuildAdminDangerTab(CuiElementContainer container, string panel)
        {
            AddAdminSection(container, panel, "Danger Zone", 0.04f, 0.815f, 0.96f, 0.85f);
            AddAdminLabel(container, panel, "These actions can remove active bots or reload runtime state. Use them deliberately.", 0.05f, 0.76f, 0.95f, 0.815f, 11, TextAnchor.MiddleLeft, "0.95 0.78 0.58 1");
            AddAdminToggle(container, panel, "Emergency enabled", "nuke_enabled", config.Persistence.EmergencyKillCommandEnabled, 0.05f, 0.665f, 0.32f, "danger");
            AddAdminToggle(container, panel, "Kill on disable", "kill_disable", config.Persistence.KillBotsOnDisable, 0.35f, 0.665f, 0.62f, "danger");
            AddAdminToggle(container, panel, "Kill on unload", "kill_unload", config.Persistence.KillBotsOnPluginUnload, 0.65f, 0.665f, 0.92f, "danger");
            AddAdminToggle(container, panel, "Leave corpses", "leave_corpses", config.Persistence.LeaveCorpses, 0.05f, 0.59f, 0.32f, "danger");
            AddAdminToggle(container, panel, "Leave bot entities", "leave_entities", config.Persistence.LeaveBotPlacedEntities, 0.35f, 0.59f, 0.62f, "danger");
            AddAdminButton(container, panel, "Disable Runtime", "raidbots.ui disable danger", 0.05f, 0.47f, 0.25f, 0.535f, "0.42 0.18 0.12 0.96", 12);
            AddAdminButton(container, panel, "Kill Active", "raidbots.ui killall danger", 0.275f, 0.47f, 0.475f, 0.535f, "0.52 0.16 0.10 0.96", 12);
            AddAdminButton(container, panel, "Nuke Active", "raidbots.ui nuke active danger", 0.50f, 0.47f, 0.70f, 0.535f, "0.60 0.10 0.08 0.96", 12);
            AddAdminButton(container, panel, "Nuke All", "raidbots.ui nuke all danger", 0.725f, 0.47f, 0.925f, 0.535f, "0.68 0.08 0.06 0.96", 12);
            AddAdminButton(container, panel, "Clear Debug UI", "raidbots.ui nuke debug danger", 0.05f, 0.36f, 0.25f, 0.425f, "0.18 0.21 0.26 0.96", 12);
            AddAdminButton(container, panel, "Reload Config/Data", "raidbots.ui reload danger", 0.275f, 0.36f, 0.505f, 0.425f, "0.18 0.21 0.26 0.96", 12);
            AddAdminButton(container, panel, "Refresh Panel", "raidbots.ui refresh danger", 0.53f, 0.36f, 0.73f, 0.425f, "0.18 0.21 0.26 0.96", 12);
        }

        private void DestroyAdminPanel(BasePlayer player)
        {
            if (player != null)
            {
                adminPanelViewers.Remove(player.userID);
                CuiHelper.DestroyUi(player, AdminPanelUi);
            }
        }

        private void DestroyAdminPanels()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyAdminPanel(player);
            }
        }

        private string AdminHeaderStatus()
        {
            return $"enabled={config.Enabled} active={activeBots.Count}/{TargetPopulation()} max={config.MaxAllowedPopulation} anchor={AdminShorten(SpawnAnchorLabel(), 18)}";
        }

        private string AdminTabLabel(string tab)
        {
            switch (tab)
            {
                case "population":
                    return "Pop";
                case "utility":
                    return "Util";
                default:
                    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tab);
            }
        }

        private void AddAdminSection(CuiElementContainer container, string parent, string text, float x1, float y1, float x2, float y2)
        {
            AddAdminLabel(container, parent, $"<b>{text}</b>", x1, y1, x2, y2, 13, TextAnchor.MiddleLeft, "0.88 0.94 1 1");
        }

        private void AddAdminMetric(CuiElementContainer container, string parent, string label, string value, float x, float y)
        {
            AddAdminLabel(container, parent, label, x, y + 0.035f, x + 0.20f, y + 0.07f, 9, TextAnchor.MiddleLeft, "0.53 0.60 0.67 1");
            AddAdminLabel(container, parent, AdminEscape(AdminShorten(value, 24)), x, y, x + 0.20f, y + 0.04f, 12, TextAnchor.MiddleLeft, "0.94 0.97 1 1");
        }

        private void AddAdminToggle(CuiElementContainer container, string parent, string label, string key, bool enabled, float x1, float y, float x2, string tab)
        {
            AddAdminButton(container, parent, $"{label}: {AdminOnOff(enabled)}", $"raidbots.ui toggle {key} {tab}", x1, y, x2, y + 0.055f, enabled ? "0.14 0.34 0.21 0.96" : "0.25 0.26 0.29 0.96", 10);
        }

        private void AddAdminIntControl(CuiElementContainer container, string parent, string label, string key, int value, int step, int bigStep, float x1, float y, float x2, string tab)
        {
            var width = x2 - x1;
            AddAdminLabel(container, parent, label, x1, y + 0.035f, x1 + width * 0.40f, y + 0.068f, 9, TextAnchor.MiddleLeft, "0.58 0.65 0.72 1");
            AddAdminLabel(container, parent, value.ToString(CultureInfo.InvariantCulture), x1, y, x1 + width * 0.30f, y + 0.045f, 12, TextAnchor.MiddleLeft, "0.94 0.97 1 1");
            AddAdminButton(container, parent, $"-{bigStep}", $"raidbots.ui addi {key} {-bigStep} {tab}", x1 + width * 0.32f, y, x1 + width * 0.48f, y + 0.047f, "0.24 0.27 0.31 0.96", 9);
            AddAdminButton(container, parent, $"-{step}", $"raidbots.ui addi {key} {-step} {tab}", x1 + width * 0.50f, y, x1 + width * 0.64f, y + 0.047f, "0.24 0.27 0.31 0.96", 9);
            AddAdminButton(container, parent, $"+{step}", $"raidbots.ui addi {key} {step} {tab}", x1 + width * 0.66f, y, x1 + width * 0.80f, y + 0.047f, "0.18 0.30 0.22 0.96", 9);
            AddAdminButton(container, parent, $"+{bigStep}", $"raidbots.ui addi {key} {bigStep} {tab}", x1 + width * 0.82f, y, x2, y + 0.047f, "0.18 0.30 0.22 0.96", 9);
        }

        private void AddAdminFloatControl(CuiElementContainer container, string parent, string label, string key, float value, float step, float bigStep, float x1, float y, float x2, string tab)
        {
            var width = x2 - x1;
            var stepText = AdminFloat(step);
            var bigText = AdminFloat(bigStep);
            AddAdminLabel(container, parent, label, x1, y + 0.035f, x1 + width * 0.40f, y + 0.068f, 9, TextAnchor.MiddleLeft, "0.58 0.65 0.72 1");
            AddAdminLabel(container, parent, AdminFloat(value), x1, y, x1 + width * 0.30f, y + 0.045f, 12, TextAnchor.MiddleLeft, "0.94 0.97 1 1");
            AddAdminButton(container, parent, $"-{bigText}", $"raidbots.ui addf {key} -{bigText} {tab}", x1 + width * 0.32f, y, x1 + width * 0.48f, y + 0.047f, "0.24 0.27 0.31 0.96", 9);
            AddAdminButton(container, parent, $"-{stepText}", $"raidbots.ui addf {key} -{stepText} {tab}", x1 + width * 0.50f, y, x1 + width * 0.64f, y + 0.047f, "0.24 0.27 0.31 0.96", 9);
            AddAdminButton(container, parent, $"+{stepText}", $"raidbots.ui addf {key} {stepText} {tab}", x1 + width * 0.66f, y, x1 + width * 0.80f, y + 0.047f, "0.18 0.30 0.22 0.96", 9);
            AddAdminButton(container, parent, $"+{bigText}", $"raidbots.ui addf {key} {bigText} {tab}", x1 + width * 0.82f, y, x2, y + 0.047f, "0.18 0.30 0.22 0.96", 9);
        }

        private void AddAdminButton(CuiElementContainer container, string parent, string text, string command, float x1, float y1, float x2, float y2, string color, int fontSize = 11)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command, Color = color },
                RectTransform = { AnchorMin = AdminAnchor(x1, y1), AnchorMax = AdminAnchor(x2, y2) },
                Text =
                {
                    Text = text,
                    FontSize = fontSize,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.95 0.97 1 1"
                }
            }, parent);
        }

        private void AddAdminLabel(CuiElementContainer container, string parent, string text, float x1, float y1, float x2, float y2, int fontSize, TextAnchor align, string color)
        {
            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = text,
                    FontSize = fontSize,
                    Align = align,
                    Color = color
                },
                RectTransform = { AnchorMin = AdminAnchor(x1, y1), AnchorMax = AdminAnchor(x2, y2) }
            }, parent);
        }

        private string AdminAnchor(float x, float y)
        {
            return $"{x.ToString("0.###", CultureInfo.InvariantCulture)} {y.ToString("0.###", CultureInfo.InvariantCulture)}";
        }

        private string AdminFloat(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private string AdminOnOff(bool enabled)
        {
            return enabled ? "ON" : "OFF";
        }

        private string AdminShorten(string value, int maxLength)
        {
            value = value ?? "";
            return value.Length <= maxLength ? value : value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private string AdminEscape(string value)
        {
            return (value ?? "").Replace("<", "").Replace(">", "");
        }

        private int TeamWeight(string key)
        {
            return config.TeamSizeWeights != null && config.TeamSizeWeights.ContainsKey(key)
                ? config.TeamSizeWeights[key]
                : 0;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyDebugBotPanel(player);
            DestroyAdminPanel(player);
            if (player != null)
            {
                adminPanelViewers.Remove(player.userID);
                debugSidePanelSuppressedUntil.Remove(player.userID);
            }
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
            var maxHealth = BotMaxHealth(bot, runtime).ToString("0", CultureInfo.InvariantCulture);
            var ammo = AmmoFraction(bot).ToString("0.00", CultureInfo.InvariantCulture);
            var target = runtime.Memory.Target == null ? BotTargetStatus(bot, runtime) : PlayerName(runtime.Memory.Target);
            var advisor = string.IsNullOrWhiteSpace(runtime.Decisions.LastAdvisorStatus) ? "none" : runtime.Decisions.LastAdvisorStatus;
            var fallback = string.IsNullOrWhiteSpace(runtime.Decisions.LastFallbackReason) ? "none" : runtime.Decisions.LastFallbackReason;
            var coverStatus = CoverStatus(bot, runtime);
            var medicalStatus = MedicalStatus(bot, runtime, now);
            var protectionStatus = string.IsNullOrWhiteSpace(runtime.LastProtectionReason) ? "none" : runtime.LastProtectionReason;
            var anchorStatus = BarricadeAnchorStatus(runtime, now);
            var aimStatus = AimStatus(runtime, now);
            var skillColor = SkillNameplateColorHex(runtime);
            CleanupBotPlacedEntityRefs();

            return "<b>Closest Raidlands Bot</b>"
                + $"\n<color={skillColor}>{runtime.DisplayName}</color>  {distance.ToString("0", CultureInfo.InvariantCulture)}m"
                + $"\nState: {runtime.State}  Prev: {runtime.PreviousState}"
                + $"\nSignal: {signal}  Target: {target}"
                + $"\nAction: {action}"
                + $"\nLOS: {(runtime.Memory.HasLineOfSight ? "Y" : "N")}  Exposure: {runtime.Memory.TargetExposureFraction:0.00} ({runtime.Memory.TargetVisibleProbePoints}/{runtime.Memory.TargetTotalProbePoints})"
                + $"\nSkill: <color={skillColor}>{runtime.SkillTier}</color>  Kit: {runtime.KitName}  Aim: {aimStatus}"
                + $"\nLearning: {LearningRuntimeStatus(runtime)}"
                + $"\nHP: {health}/{maxHealth}  Weapon: {weapon}  Ammo: {ammo}"
                + $"\nClan: {BotClanLabel(runtime)}"
                + $"\nK/D: {stats.kills}/{stats.deaths} ({kd})  Team: {runtime.TeamId}  Role: {runtime.SquadRole}"
                + $"\nBase: {(runtime.IsInBaseRestrictedArea ? "inside" : "clear")}  Barricades: {botPlacedEntities.Count}/{config.AI.MaxActiveBotBarricades}"
                + $"\nCover: {coverStatus}  Wall: {(string.IsNullOrWhiteSpace(runtime.LastBarricadeReason) ? "none" : runtime.LastBarricadeReason)}"
                + $"\nProtect: {protectionStatus}  Anchor: {anchorStatus}"
                + $"\nUtility: {(string.IsNullOrWhiteSpace(runtime.LastUtilityReason) ? "none" : runtime.LastUtilityReason)}"
                + $"\nHeal: {medicalStatus}"
                + $"\nFire: {(string.IsNullOrWhiteSpace(runtime.LastFireBlockReason) ? "none" : runtime.LastFireBlockReason)}"
                + $"\nSight: {(string.IsNullOrWhiteSpace(runtime.LastSightReason) ? "none" : runtime.LastSightReason)}"
                + $"\nMove: {movement}  Formation: {(string.IsNullOrWhiteSpace(runtime.LastFormationReason) ? "none" : runtime.LastFormationReason)}"
                + $"\nShooting: {(runtime.IsShooting ? "Y" : "N")}"
                + $"\nFailed paths: {runtime.ConsecutiveFailedPaths}  Bad spots: {ActiveStuckMemoryCount(runtime, now)} ({runtime.LastStuckMemoryReason})  Advisor: {advisor}"
                + $"\nFallback: {fallback}";
        }

        private string SkillNameplateColorHex(BotRuntime runtime)
        {
            var tier = (runtime?.SkillTier ?? "").Trim();

            if (string.Equals(tier, "casual", StringComparison.OrdinalIgnoreCase))
            {
                return "#7ee787";
            }

            if (string.Equals(tier, "dangerous", StringComparison.OrdinalIgnoreCase))
            {
                return "#ff5c5c";
            }

            return "#ffde59";
        }

        private Color SkillNameplateDrawColor(BotRuntime runtime)
        {
            var tier = (runtime?.SkillTier ?? "").Trim();

            if (string.Equals(tier, "casual", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.49f, 0.91f, 0.53f, 1f);
            }

            if (string.Equals(tier, "dangerous", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(1f, 0.36f, 0.36f, 1f);
            }

            return new Color(1f, 0.87f, 0.35f, 1f);
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
            var badSpots = ActiveStuckMemoryCount(runtime, Time.realtimeSinceStartup);
            return $"{destination} | {cover} | {stuck} | bad={badSpots}";
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
                var visibleTargetId = CombatTargetId(visible);
                var switched = runtime.Memory.TargetUserId != visibleTargetId;
                runtime.Memory.Target = visible;
                runtime.Memory.TargetUserId = visibleTargetId;
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
                    StartAimWarmup(runtime, visibleTargetId, now);
                    runtime.NextReactionAllowedAt = now + UnityEngine.Random.Range(runtime.Skill.ReactionMinSeconds, runtime.Skill.ReactionMaxSeconds);
                }
                else if (runtime.AimWarmupTargetUserId == 0UL)
                {
                    StartAimWarmup(runtime, visibleTargetId, now);
                }

                if (config.Debug.DebugPerception)
                {
                    DebugLog($"perception-see:{runtime.BotKey}", $"{runtime.DisplayName} sees {PlayerName(visible)} exposure={visibility.ExposedFraction:0.00} probes={visibility.VisibleProbePoints}/{visibility.TotalProbePoints} at {FormatVector(visible.transform.position)}.");
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

            foreach (var player in CombatTargetCandidates(bot, runtime))
            {
                if (!IsCandidateTarget(bot, runtime, player))
                {
                    continue;
                }

                var playerTargetId = CombatTargetId(player);
                var isKnownThreat = runtime.Memory.TargetUserId == playerTargetId
                    || runtime.Memory.LastDamageSourcePlayer == player
                    || (runtime.Memory.LastDamageSourcePlayer != null && CombatTargetId(runtime.Memory.LastDamageSourcePlayer) == playerTargetId);

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

        private IEnumerable<BasePlayer> CombatTargetCandidates(BaseCombatEntity bot, BotRuntime runtime)
        {
            var seen = new HashSet<BasePlayer>();

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && seen.Add(player))
                {
                    yield return player;
                }
            }

            foreach (var entry in activeBots)
            {
                var candidate = entry.Key as BasePlayer;

                if (candidate == null || candidate == bot || entry.Value == null || !IsEnemyBot(runtime, entry.Value) || !seen.Add(candidate))
                {
                    continue;
                }

                yield return candidate;
            }
        }

        private bool IsCandidateTarget(BaseCombatEntity bot, BotRuntime runtime, BasePlayer player)
        {
            if (bot == null || player == null || player == bot || player.IsDead() || player.IsSleeping())
            {
                return false;
            }

            var targetRuntime = RuntimeFor(player);

            if (targetRuntime != null)
            {
                if (!IsLiveBot(player) || !IsEnemyBot(runtime, targetRuntime))
                {
                    return false;
                }
            }
            else if (!IsRealPlayer(player) || !player.IsConnected || ShouldIgnoreSafeZonePlayer(player))
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
                if (IsBarricadeAnchorActive(runtime, now))
                {
                    runtime.BarricadeAnchorNoActionPushAt = now + BarricadeAnchorNoActionPushSeconds(runtime);
                }

                runtime.Memory.TargetConfidence = Math.Max(runtime.Memory.TargetConfidence, confidence);
                runtime.NextDecisionAt = Math.Min(runtime.NextDecisionAt, now);

                if (ShouldCommandSoundInvestigation(runtime, source.userID, now))
                {
                    var destination = SoundInvestigationDestination(bot.transform.position, sourcePosition, runtime);
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
                    DebugLog($"perception-hear:{runtime.BotKey}:{soundType}", $"{runtime.DisplayName} heard {soundType} from {PlayerName(source)} at {distance.ToString("0", CultureInfo.InvariantCulture)}m; investigating {FormatVector(sourcePosition)}.");
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

        private Vector3 SoundInvestigationDestination(Vector3 origin, Vector3 sourcePosition, BotRuntime runtime = null)
        {
            var distance = Vector3.Distance(origin, sourcePosition);
            var now = Time.realtimeSinceStartup;

            if (distance <= 55f && !ShouldAvoidDestination(runtime, sourcePosition, now))
            {
                return sourcePosition;
            }

            return MoveTowardPosition(origin, sourcePosition, Mathf.Clamp(distance * 0.65f, 35f, 85f), runtime);
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
            var engagementSignal = PlayerEngagementSignal(bot, runtime, now);
            var request = new DecisionRequest
            {
                RequestId = $"{runtime.BotKey}-{now.ToString("0.000", CultureInfo.InvariantCulture)}",
                BotId = runtime.BotKey,
                TeamId = runtime.TeamId,
                ClanKey = runtime.ClanKey,
                ClanTag = runtime.ClanTag,
                State = runtime.State.ToString(),
                SkillTier = runtime.SkillTier,
                HealthFraction = Mathf.Clamp01(bot.Health() / BotMaxHealth(bot, runtime)),
                WeaponShortname = ActiveWeaponShortname(bot),
                AimErrorDegrees = AimErrorDegreesAt(runtime, now),
                AimWarmupProgress = AimWarmupProgress(runtime, now),
                AmmoFraction = AmmoFraction(bot),
                HasLineOfSight = runtime.Memory.HasLineOfSight,
                TargetExposureFraction = runtime.Memory.TargetExposureFraction,
                TargetConfidence = runtime.Memory.TargetConfidence,
                DistanceToTarget = target == null ? -1f : Vector3.Distance(bot.transform.position, target.transform.position),
                NearestRealPlayerDistance = DistanceToNearestRealPlayer(bot.transform.position),
                AdvisorRealPlayerGateMeters = config.DecisionAdvisor.RequireRealPlayerWithinMeters,
                EngagedWithRealPlayer = !string.IsNullOrWhiteSpace(engagementSignal),
                EngagementSignal = string.IsNullOrWhiteSpace(engagementSignal) ? "none" : engagementSignal,
                SecondsSinceLastSeen = runtime.Memory.LastSeenAt <= 0f ? 999f : now - runtime.Memory.LastSeenAt,
                SecondsSinceLastHeard = runtime.Memory.LastHeardAt <= 0f ? 999f : now - runtime.Memory.LastHeardAt,
                NearbyAllies = NearbyAllies(bot, runtime),
                NearbyKnownEnemies = NearbyKnownEnemies(runtime, now),
                IsStuck = IsBotStuck(bot, runtime, now),
                StuckMemoryPoints = ActiveStuckMemoryCount(runtime, now),
                TargetIsInsideBaseRestrictedArea = target != null && IsBaseRestrictedPosition(target.transform.position),
                ProtectionDamageFraction = runtime.ProtectionDamageAccumulatedFraction,
                ProtectionState = runtime.LastProtectionReason ?? "none",
                BarricadeAnchorState = BarricadeAnchorStatus(runtime, now),
                MedicalFireLocked = IsMedicalFireLocked(runtime, now)
            };

            return request;
        }

        private JObject BuildAdvisorPayload(DecisionRequest request)
        {
            var candidates = new JArray();

            foreach (var candidate in request.CandidateActions ?? new List<TacticalActionCandidate>())
            {
                candidates.Add(new JObject
                {
                    ["id"] = candidate.Id ?? ActionIdString(candidate.ActionId),
                    ["heuristic_score"] = candidate.HeuristicScore,
                    ["risk"] = candidate.Risk ?? "",
                    ["reason"] = candidate.ReasonFromCode ?? "",
                    ["destination"] = VectorPayload(candidate.Destination),
                    ["target_user_id"] = candidate.TargetUserId.ToString(),
                    ["expires_at"] = candidate.ExpiresAt,
                    ["preconditions"] = new JArray(candidate.Preconditions ?? new List<string>()),
                    ["risk_flags"] = new JArray(candidate.RiskFlags ?? new List<string>())
                });
            }

            var events = new JArray();

            foreach (var decisionEvent in (request.RecentEvents ?? new List<DecisionEvent>()).Take(Math.Max(0, config.DecisionAdvisor.MaxRecentEventsInRequest)))
            {
                events.Add(new JObject
                {
                    ["time"] = decisionEvent.Time,
                    ["type"] = decisionEvent.Type ?? "",
                    ["detail"] = decisionEvent.Detail ?? "",
                    ["position"] = VectorPayload(decisionEvent.Position)
                });
            }

            return new JObject
            {
                ["schema_version"] = 1,
                ["request_id"] = request.RequestId ?? "",
                ["bot_id"] = request.BotId ?? "",
                ["team_id"] = request.TeamId,
                ["clan_key"] = request.ClanKey ?? "",
                ["clan_tag"] = request.ClanTag ?? "",
                ["state"] = request.State ?? "",
                ["skill_tier"] = request.SkillTier ?? "",
                ["health_fraction"] = request.HealthFraction,
                ["weapon_shortname"] = request.WeaponShortname ?? "",
                ["aim_error_degrees"] = request.AimErrorDegrees,
                ["aim_warmup_progress"] = request.AimWarmupProgress,
                ["ammo_fraction"] = request.AmmoFraction,
                ["has_line_of_sight"] = request.HasLineOfSight,
                ["target_exposure_fraction"] = request.TargetExposureFraction,
                ["target_confidence"] = request.TargetConfidence,
                ["distance_to_target"] = request.DistanceToTarget,
                ["nearest_real_player_distance"] = request.NearestRealPlayerDistance,
                ["advisor_real_player_gate_meters"] = request.AdvisorRealPlayerGateMeters,
                ["engaged_with_real_player"] = request.EngagedWithRealPlayer,
                ["engagement_signal"] = request.EngagementSignal ?? "none",
                ["seconds_since_last_seen"] = request.SecondsSinceLastSeen,
                ["seconds_since_last_heard"] = request.SecondsSinceLastHeard,
                ["nearby_allies"] = request.NearbyAllies,
                ["nearby_known_enemies"] = request.NearbyKnownEnemies,
                ["is_stuck"] = request.IsStuck,
                ["stuck_memory_points"] = request.StuckMemoryPoints,
                ["target_is_inside_base_restricted_area"] = request.TargetIsInsideBaseRestrictedArea,
                ["protection_damage_fraction"] = request.ProtectionDamageFraction,
                ["protection_state"] = request.ProtectionState ?? "",
                ["barricade_anchor_state"] = request.BarricadeAnchorState ?? "",
                ["medical_fire_locked"] = request.MedicalFireLocked,
                ["recent_events"] = events,
                ["candidate_actions"] = candidates
            };
        }

        private JObject VectorPayload(Vector3 vector)
        {
            return new JObject
            {
                ["x"] = Math.Round(vector.x, 2),
                ["y"] = Math.Round(vector.y, 2),
                ["z"] = Math.Round(vector.z, 2)
            };
        }

        private JObject AdvisorResponseSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["action_id"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "One id from candidate_actions."
                    },
                    ["confidence"] = new JObject
                    {
                        ["type"] = "number",
                        ["minimum"] = 0,
                        ["maximum"] = 1
                    },
                    ["ttl_ms"] = new JObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 100,
                        ["maximum"] = config.DecisionAdvisor.DecisionTtlMilliseconds
                    },
                    ["rationale"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Short visible debugging reason. No hidden reasoning."
                    },
                    ["fallback_action_id"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["risk_flags"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" }
                    }
                },
                ["required"] = new JArray("action_id", "confidence", "ttl_ms", "rationale", "fallback_action_id", "risk_flags")
            };
        }

        private string BuildOpenAiCompatibleAdvisorBody(DecisionRequest request)
        {
            var payload = BuildAdvisorPayload(request).ToString(Formatting.None);
            var body = new JObject
            {
                ["model"] = string.IsNullOrWhiteSpace(config.DecisionAdvisor.Model) ? "local-tactical-advisor" : config.DecisionAdvisor.Model,
                ["temperature"] = 0.1f,
                ["max_tokens"] = 300,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = "You are a Raidlands roam bot tactical advisor. Choose exactly one action_id from candidate_actions. Return only compact JSON matching the requested schema. Do not invent actions, destinations, timers, item use, or targets."
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = payload
                    }
                }
            };

            body["response_format"] = config.DecisionAdvisor.UseStructuredResponseSchema
                ? new JObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JObject
                    {
                        ["name"] = "raidlands_roambot_decision",
                        ["strict"] = true,
                        ["schema"] = AdvisorResponseSchema()
                    }
                }
                : new JObject { ["type"] = "json_object" };

            return body.ToString(Formatting.None);
        }

        private string BuildWebsiteProxyAdvisorBody(DecisionRequest request)
        {
            return new JObject
            {
                ["schema_version"] = 1,
                ["plugin"] = Name,
                ["advisor_mode"] = config.DecisionAdvisor.Mode,
                ["request"] = BuildAdvisorPayload(request),
                ["response_schema"] = AdvisorResponseSchema()
            }.ToString(Formatting.None);
        }

        private bool IsDecisionAdvisorHttpConfigured(string provider)
        {
            if (config?.DecisionAdvisor == null || !config.DecisionAdvisor.Enabled)
            {
                return false;
            }

            if (provider != AdvisorProviderOpenAiCompatible && provider != AdvisorProviderWebsiteProxy)
            {
                return false;
            }

            if (provider == AdvisorProviderOpenAiCompatible && string.IsNullOrWhiteSpace(ResolveAdvisorApiKey()))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(config.DecisionAdvisor.EndpointUrl);
        }

        private string AdvisorEndpointUrl(string provider)
        {
            var endpoint = (config?.DecisionAdvisor?.EndpointUrl ?? "").Trim();

            if (provider == AdvisorProviderOpenAiCompatible)
            {
                var trimmed = endpoint.TrimEnd('/');

                if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed + "/chat/completions";
                }
            }

            return endpoint;
        }

        private Dictionary<string, string> BuildAdvisorHeaders(string provider)
        {
            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            };

            var apiKey = ResolveAdvisorApiKey();

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                headers["Authorization"] = $"Bearer {apiKey}";
            }

            return headers;
        }

        private string ResolveAdvisorApiKey()
        {
            return ResolveSecretValue(config?.DecisionAdvisor?.ApiKey);
        }

        private void SendAdvisorPost(string url, string body, Action<int, string> callback, Dictionary<string, string> headers, float timeoutMilliseconds)
        {
            webrequest.Enqueue(url, body, (code, response) => callback(code, response ?? ""), this, Oxide.Core.Libraries.RequestMethod.POST, headers, timeoutMilliseconds);
        }

        private DecisionAdvisorResult ParseAdvisorHttpResponse(string provider, DecisionRequest request, int code, string response, float submittedAt)
        {
            var latency = (int)Math.Max(0f, (Time.realtimeSinceStartup - submittedAt) * 1000f);

            if (code < 200 || code >= 300)
            {
                var failure = DecisionAdvisorResult.Failure(code == 0 ? "advisor_timeout_or_network" : $"advisor_http_{code}");
                failure.HttpStatusCode = code;
                failure.LatencyMilliseconds = latency;
                return failure;
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                var failure = DecisionAdvisorResult.Failure("advisor_empty_response");
                failure.HttpStatusCode = code;
                failure.LatencyMilliseconds = latency;
                return failure;
            }

            if (response.Length > config.DecisionAdvisor.MaxAdvisorResponseBytes)
            {
                var failure = DecisionAdvisorResult.Failure("advisor_response_too_large");
                failure.HttpStatusCode = code;
                failure.LatencyMilliseconds = latency;
                return failure;
            }

            try
            {
                var root = JObject.Parse(response);
                var decisionJson = provider == AdvisorProviderOpenAiCompatible
                    ? ExtractOpenAiCompatibleDecisionJson(root)
                    : ExtractWebsiteProxyDecisionJson(root);
                var result = ParseAdvisorDecisionJson(decisionJson);
                result.HttpStatusCode = code;
                result.LatencyMilliseconds = latency;
                return result;
            }
            catch
            {
                var failure = DecisionAdvisorResult.Failure("advisor_invalid_json");
                failure.HttpStatusCode = code;
                failure.LatencyMilliseconds = latency;
                return failure;
            }
        }

        private JObject ExtractOpenAiCompatibleDecisionJson(JObject root)
        {
            if (root["action_id"] != null)
            {
                return root;
            }

            var contentToken = root.SelectToken("choices[0].message.content");

            if (contentToken is JObject contentObject)
            {
                return contentObject;
            }

            var content = contentToken?.ToString();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new JsonException("OpenAI-compatible response did not include choices[0].message.content.");
            }

            return JObject.Parse(content);
        }

        private JObject ExtractWebsiteProxyDecisionJson(JObject root)
        {
            if (root["action_id"] != null)
            {
                return root;
            }

            if (root["decision"] is JObject decision)
            {
                return decision;
            }

            throw new JsonException("Website proxy response did not include a decision object.");
        }

        private DecisionAdvisorResult ParseAdvisorDecisionJson(JObject decisionJson)
        {
            return new DecisionAdvisorResult
            {
                Success = true,
                Status = "advisor_ok",
                ActionId = ((string)decisionJson["action_id"] ?? "").Trim(),
                Confidence = decisionJson.Value<float?>("confidence") ?? 0f,
                TtlMilliseconds = decisionJson.Value<int?>("ttl_ms") ?? config.DecisionAdvisor.DecisionTtlMilliseconds,
                Rationale = ((string)decisionJson["rationale"] ?? "").Trim(),
                FallbackActionId = ((string)decisionJson["fallback_action_id"] ?? "").Trim()
            };
        }

        private List<TacticalActionCandidate> BuildCandidateActions(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            var candidates = new List<TacticalActionCandidate>();
            var target = runtime.Memory.Target;
            var hasFreshSeen = runtime.Memory.LastSeenAt > 0f && now - runtime.Memory.LastSeenAt <= config.AI.SearchLastSeenSeconds;
            var soundMemorySeconds = Math.Min(config.AI.TargetMemorySeconds, config.AI.SoundInvestigationCommitmentSeconds);
            var hasFreshHeard = config.AI.AllowHearing && runtime.Memory.LastHeardAt > 0f && now - runtime.Memory.LastHeardAt <= soundMemorySeconds;
            var healthFraction = Mathf.Clamp01(bot.Health() / BotMaxHealth(bot, runtime));
            var board = SquadBoardFor(runtime);
            var squadHasFreshContact = SquadHasFreshContact(board, now);
            var hasRecentContact = HasRecentContact(runtime, now);
            var knownThreatPosition = KnownThreatPosition(runtime);
            var damageWallAware = HasDamageWallAwareness(runtime, now);
            var protectionDamageAware = HasProtectionDamageTrigger(runtime, now);
            var knownThreatDistance = knownThreatPosition == Vector3.zero ? 0f : Vector3.Distance(bot.transform.position, knownThreatPosition);
            var longRangeDefensiveAware = ShouldPreferLongRangeDefensiveHeal(runtime, healthFraction, knownThreatDistance, hasFreshSeen || hasFreshHeard || hasRecentContact, now);
            var lowHealthAware = longRangeDefensiveAware || ShouldNoticeLowHealth(runtime, healthFraction, now);
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
                ClearBarricadeAnchor(runtime, "cover_compromised");
            }

            var wallCommitActive = runtime.BarricadeCommittedUntil > now
                && runtime.CurrentBarricadePoint != Vector3.zero
                && nearCoverNow
                && !coverCompromised;
            var barricadeAnchorActive = IsBarricadeAnchorActive(runtime, now);
            var barricadeAnchorCanLeave = CanLeaveBarricadeAnchor(runtime, now);

            var targetInBase = target != null && IsBaseRestrictedPosition(target.transform.position);
            var threatPathCrossesBase = knownThreatPosition != Vector3.zero
                && !runtime.IsInBaseRestrictedArea
                && SegmentCrossesBaseRestrictedArea(bot.transform.position, knownThreatPosition);

            CleanupBotUtilityRefs(now);
            CleanupStuckDestinationMemory(runtime, now);

            if (TryFindUtilityDangerEscapePosition(bot, runtime, now, out var utilityEscapePoint, out var utilityDangerReason))
            {
                var escape = Candidate(TacticalActionId.RetreatToCover, 148f, "medium", utilityDangerReason, utilityEscapePoint, runtime.Memory.TargetUserId, now);
                escape.RiskFlags.Add("grenade_danger_avoidance");
                candidates.Add(escape);
            }

            if (runtime.Movement.IsStuck && (now >= runtime.NextStuckRecoveryAt || runtime.ConsecutiveFailedPaths >= 6))
            {
                var recovery = Candidate(TacticalActionId.RoamToPoint, runtime.ConsecutiveFailedPaths >= 12 ? 132f : 118f, "low", "stuck recovery alternate path", FindStuckRecoveryDestination(bot, runtime), runtime.Memory.TargetUserId, now);
                recovery.RiskFlags.Add("stuck_recovery");
                candidates.Add(recovery);
            }

            if (runtime.IsInBaseRestrictedArea && TryFindOutsideBaseHoldPoint(bot.transform.position, knownThreatPosition, runtime, now, out var escapePoint))
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

                if (distanceFromTeam > regroupDistance && TrySampleTacticalPositionAvoidingStuck(runtime, board.TeamCenter, Math.Max(8f, config.Spawn.NavmeshSampleDistance), now, out var regroupPoint))
                {
                    var regroupScore = squadHasFreshContact ? 50f : 70f;
                    candidates.Add(Candidate(TacticalActionId.RegroupWithSquad, regroupScore, "low", $"too far from clan center as {runtime.SquadRole}", regroupPoint, board.SharedEnemyUserId, now));
                }
            }

            if (protectionDamageAware
                && config.AI.AllowCover
                && !atCoverNow
                && knownThreatPosition != Vector3.zero
                && TryFindNearbyProtectionPlan(bot, runtime, knownThreatPosition, target, ProtectionDistance(runtime), now, out var protectionPlan))
            {
                ApplyProtectionPlan(runtime, protectionPlan, now);
                var protection = Candidate(TacticalActionId.RetreatToCover, 158f + SkillDiscipline(runtime) * 10f, "low", $"damage protection trigger; use {protectionPlan.Source} within {protectionPlan.Distance:0.0}m", protectionPlan.TuckPoint, runtime.Memory.TargetUserId, now);
                protection.RiskFlags.Add("protection_damage");
                protection.RiskFlags.Add(protectionPlan.Source);
                candidates.Add(protection);
            }

            AddUtilityCandidates(candidates, bot, runtime, target, knownThreatPosition, healthFraction, lowHealthAware, atCoverNow, hasFreshSeen, hasFreshHeard, hasRecentContact, targetInBase, now, board);

            var hasNearbyProtectionCandidate = candidates.Any(candidate => candidate.RiskFlags.Contains("protection_damage"));

            if (config.AI.AllowBarricades && !atCoverNow && knownThreatPosition != Vector3.zero && now < runtime.NextBarricadeAt)
            {
                runtime.LastBarricadeReason = $"{(damageWallAware ? "queued" : "cooldown")} {Math.Max(0f, runtime.NextBarricadeAt - now).ToString("0", CultureInfo.InvariantCulture)}s";
            }

            if (config.AI.AllowBarricades
                && !atCoverNow
                && now >= runtime.NextBarricadeAt
                && knownThreatPosition != Vector3.zero
                && !hasNearbyProtectionCandidate
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
                    var targetDistance = target == null ? float.MaxValue : Vector3.Distance(bot.transform.position, target.transform.position);
                    var canShootFromCover = target != null
                        && CanShootVisibleTarget(bot, runtime, targetDistance, runtime.Memory.TargetExposureFraction, now);
                    var shouldReturnFireFromCover = canShootFromCover && ShouldReturnFireFromCoverWhileHealing(runtime, targetDistance, now);
                    var syringeRecoveryActive = IsMedicalFireLocked(runtime, now) || healthFraction < SyringeHealTargetFraction();

                    if (syringeRecoveryActive && tuckDestination != Vector3.zero && !shouldReturnFireFromCover)
                    {
                        var tuck = Candidate(TacticalActionId.Tuck, 142f + SkillDiscipline(runtime) * 10f, "low", "hold cover until syringe recovery finishes", tuckDestination, runtime.Memory.TargetUserId, now);
                        tuck.RiskFlags.Add("low_health_heal");
                        tuck.RiskFlags.Add("syringe_recovery");
                        candidates.Add(tuck);
                    }
                }
                else
                {
                    var retreatDestination = FindRetreatPosition(bot.transform.position, knownThreatPosition, runtime);
                    var foundCoverDestination = false;
                    var hasCurrentCoverDestination = !coverCompromised
                        && runtime.CurrentTuckPoint != Vector3.zero
                        && (runtime.State == TacticalState.BarricadeHold
                            || runtime.State == TacticalState.FightFromCover
                            || runtime.CurrentBarricadePoint != Vector3.zero);

                    if (TryFindNearbyProtectionPlan(bot, runtime, knownThreatPosition, target, ProtectionDistance(runtime), now, out var lowHealthProtection))
                    {
                        ApplyProtectionPlan(runtime, lowHealthProtection, now);
                        retreatDestination = lowHealthProtection.TuckPoint;
                        foundCoverDestination = true;
                    }
                    else if (hasCurrentCoverDestination)
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
                    var nearbyCoverDistance = NearbyDefensiveCoverDistance(runtime);
                    var retreatCoverIsNear = foundCoverDestination && distanceToRetreatCover <= nearbyCoverDistance;
                    var retreatCoverIsFar = !retreatCoverIsNear;

                    if (config.AI.AllowBarricades
                        && retreatCoverIsFar
                        && now >= runtime.NextBarricadeAt
                        && candidates.All(candidate => candidate.ActionId != TacticalActionId.PlaceBarricade)
                        && ShouldPlaceBarricade(bot, runtime, knownThreatPosition, healthFraction, runtime.Memory.TargetExposureFraction, now, out var retreatWallPoint))
                    {
                        var wallScore = 152f + (1f - healthFraction) * 22f + SkillDiscipline(runtime) * 10f;
                        var wallReason = foundCoverDestination
                            ? $"defensive cover is {distanceToRetreatCover.ToString("0", CultureInfo.InvariantCulture)}m away; wall before healing"
                            : "no nearby defensive cover; wall before healing";
                        var wall = Candidate(TacticalActionId.PlaceBarricade, wallScore, "medium", wallReason, retreatWallPoint, runtime.Memory.TargetUserId, now);
                        wall.RiskFlags.Add("real_entity");
                        wall.RiskFlags.Add("retreat_wall");
                        wall.RiskFlags.Add(longRangeDefensiveAware ? "long_range_losing_fight" : "low_health_heal");
                        candidates.Add(wall);
                    }

                    var retreatAge = runtime.State == TacticalState.Retreat ? now - runtime.StateEnteredAt : 0f;
                    var retreatScore = retreatCoverIsNear
                        ? 144f + (1f - healthFraction) * 18f + SkillDiscipline(runtime) * 8f
                        : foundCoverDestination
                            ? 96f + (1f - healthFraction) * 12f
                            : 86f + (config.AI.LowHealthCoverThreshold - healthFraction) * 10f;

                    if (longRangeDefensiveAware)
                    {
                        retreatScore += retreatCoverIsNear ? 18f : 6f;
                    }

                    if (!retreatCoverIsNear && candidates.Any(candidate => candidate.ActionId == TacticalActionId.PlaceBarricade))
                    {
                        retreatScore -= 24f;
                    }

                    if (!foundCoverDestination && retreatAge > RetreatFallbackReturnFireAfterSeconds)
                    {
                        retreatScore -= Mathf.Clamp((retreatAge - RetreatFallbackReturnFireAfterSeconds) * 6f, 0f, 28f);
                    }

                    var retreatReason = foundCoverDestination
                        ? retreatCoverIsNear
                            ? $"near cover is {distanceToRetreatCover.ToString("0", CultureInfo.InvariantCulture)}m away; move there and heal"
                            : $"cover is {distanceToRetreatCover.ToString("0", CultureInfo.InvariantCulture)}m away; move if wall is unavailable"
                        : "noticed low health but no hard cover was found; fall back and reassess";
                    var retreat = Candidate(TacticalActionId.RetreatToCover, retreatScore, "low", retreatReason, retreatDestination, runtime.Memory.TargetUserId, now);
                    retreat.RiskFlags.Add("low_health_heal");
                    retreat.RiskFlags.Add(foundCoverDestination ? "cover_destination" : "fallback_retreat");
                    retreat.RiskFlags.Add(retreatCoverIsNear ? "near_cover" : "far_cover");
                    candidates.Add(retreat);
                }
            }

            if (config.AI.DoNotEnterBases && (targetInBase || threatPathCrossesBase) && (hasFreshSeen || hasFreshHeard || target != null))
            {
                if (TryFindOutsideBaseHoldPoint(bot.transform.position, knownThreatPosition, runtime, now, out var holdPoint))
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
                var targetId = CombatTargetId(target);
                var distance = Vector3.Distance(bot.transform.position, target.transform.position);
                var rangeScore = WeaponRangeScore(runtime, distance);
                var exposure = runtime.Memory.TargetExposureFraction;
                var atCover = atCoverNow;
                var emergencyHealing = lowHealthAware && (healthFraction < SyringeHealTargetFraction() || IsMedicalFireLocked(runtime, now));
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

                    var shoot = Candidate(TacticalActionId.AcquireVisibleTarget, shootScore, shootRisk, shootReason, target.transform.position, targetId, now);

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
                    && !hasNearbyProtectionCandidate
                    && ShouldPlaceBarricade(bot, runtime, target.transform.position, healthFraction, exposure, now, out var barricadePoint))
                {
                    var barricade = Candidate(TacticalActionId.PlaceBarricade, damageWallAware ? 136f : 90f + (1f - healthFraction) * 18f + exposure * 8f, "medium", "damaged or exposed in open ground; place real barricade cover", barricadePoint, targetId, now);
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
                            candidates.Add(Candidate(TacticalActionId.Tuck, 84f, "low", "peek window expired; tuck back into cover", runtime.CurrentTuckPoint == Vector3.zero ? runtime.CurrentCover : runtime.CurrentTuckPoint, targetId, now));
                        }
                        else if (!runtime.IsShooting && now >= runtime.NextPeekAt && runtime.CurrentPeekPoint != Vector3.zero)
                        {
                            var peekAction = UnityEngine.Random.value < 0.5f ? TacticalActionId.PeekLeft : TacticalActionId.PeekRight;
                            candidates.Add(Candidate(peekAction, 66f + runtime.Skill.Aggression * 12f, "medium", "peek from current cover to re-check target", runtime.CurrentPeekPoint, targetId, now));
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

                        candidates.Add(Candidate(TacticalActionId.MoveToCover, coverScore, "low", "visible target while bot is exposed", coverPlan.CoverPoint, targetId, now));
                    }
                }

                if (distance > runtime.Combat.PreferredDistance
                    && !emergencyHealing
                    && (!barricadeAnchorActive || barricadeAnchorCanLeave))
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

                    if (barricadeAnchorActive && barricadeAnchorCanLeave)
                    {
                        pushScore += 10f;
                    }

                    candidates.Add(Candidate(TacticalActionId.PushTarget, pushScore, "medium", $"target is outside {runtime.Combat.WeaponClass} preferred range", MoveTowardPosition(bot.transform.position, target.transform.position, runtime.Combat.PushDistance, runtime), targetId, now));
                }

                if (config.AI.AllowFlanking
                    && board != null
                    && board.TeamSize > 1
                    && now >= runtime.NextFlankAt
                    && !(wallCommitActive && distance <= runtime.Combat.MaxRange)
                    && !emergencyHealing
                    && (!barricadeAnchorActive || barricadeAnchorCanLeave)
                    && distance > Math.Max(12f, runtime.Combat.RetreatDistance + 4f)
                    && (runtime.SquadRole == "flanker" || runtime.SquadRole == "pusher"))
                {
                    var side = runtime.SquadRole == "flanker" ? 1f : -1f;
                    var score = (runtime.SquadRole == "flanker" ? 78f : 66f) + runtime.Skill.Aggression * 12f;

                    if (board.AnyMemberHasLineOfSight && !runtime.Memory.HasLineOfSight)
                    {
                        score += 10f;
                    }

                    if (TryFindFlankPosition(bot.transform.position, target.transform.position, side, runtime, now, out var flankPoint))
                    {
                        candidates.Add(Candidate(side > 0f ? TacticalActionId.FlankLeft : TacticalActionId.FlankRight, score, "medium", $"squad {runtime.SquadRole} flank toward shared fight", flankPoint, targetId, now));
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
                    && TryFindFlankPosition(bot.transform.position, sharedEnemy.LastKnownPosition, runtime.SquadRole == "flanker" ? 1f : -1f, runtime, now, out var sharedFlank))
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
                var investigateDestination = SoundInvestigationDestination(bot.transform.position, runtime.Memory.LastHeardPosition, runtime);
                candidates.Add(Candidate(TacticalActionId.InvestigateSound, investigateScore, "medium", "fresh sound stimulus without visual contact", investigateDestination, runtime.Memory.TargetUserId, now));
            }

            if (!lowHealthAware && healthFraction < 0.35f && (hasFreshSeen || hasFreshHeard))
            {
                var awayFrom = runtime.Memory.LastSeenAt >= runtime.Memory.LastHeardAt ? runtime.Memory.LastSeenPosition : runtime.Memory.LastHeardPosition;
                var retreatDestination = FindRetreatPosition(bot.transform.position, awayFrom, runtime);

                if (config.AI.AllowCover && now >= runtime.NextCoverSearchAt && TryFindCoverPlan(bot, runtime, awayFrom, target, out var retreatCover))
                {
                    ApplyCoverPlan(runtime, retreatCover);
                    retreatDestination = retreatCover.CoverPoint;
                }

                candidates.Add(Candidate(TacticalActionId.RetreatToCover, 88f, "low", "critical health panic while threat is known", retreatDestination, runtime.Memory.TargetUserId, now));
            }

            if (runtime.CurrentDestination == Vector3.zero
                || Vector3.Distance(bot.transform.position, runtime.CurrentDestination) < 4f
                || ShouldAvoidDestination(runtime, runtime.CurrentDestination, now))
            {
                runtime.CurrentDestination = FindRoamDestination(runtime.HomePosition, runtime);
            }

            candidates.Add(Candidate(TacticalActionId.RoamToPoint, 15f, "low", "no higher-priority tactical stimulus", runtime.CurrentDestination, 0, now));
            ApplySquadDestinationReservations(bot, runtime, candidates, now);
            ApplyBarricadeAnchorCandidateFilter(bot, runtime, candidates, now);
            var filtered = candidates
                .Where(candidate => !IsMovementDestinationAction(candidate.ActionId) || !ShouldAvoidDestination(runtime, candidate.Destination, now))
                .OrderByDescending(candidate => candidate.HeuristicScore)
                .Take(Math.Max(1, config.DecisionAdvisor.MaxCandidateActions))
                .ToList();

            if (filtered.Count > 0)
            {
                return filtered;
            }

            var fallback = Candidate(TacticalActionId.RoamToPoint, 8f, "low", "all movement candidates were recently stuck; reset roam destination", FindRoamDestination(bot.transform.position, runtime), 0, now);
            fallback.RiskFlags.Add("stuck_memory_reset");
            return new List<TacticalActionCandidate> { fallback };
        }

        private bool CanShootVisibleTarget(BaseCombatEntity bot, BotRuntime runtime, float distance, float exposure, float now)
        {
            return bot != null
                && runtime != null
                && runtime.Memory.HasLineOfSight
                && !IsMedicalFireLocked(runtime, now)
                && (config.AI.AllowShootingWhileNonSyringeHealing || !IsNonSyringeHealing(runtime, now))
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

        private bool ShouldReturnFireFromCoverWhileHealing(BotRuntime runtime, float distance, float now)
        {
            if (runtime == null)
            {
                return false;
            }

            var pushedDistance = Math.Max(config.AI.HealingReturnFireDistance, runtime.Combat.RetreatDistance + 8f);

            if (distance <= pushedDistance)
            {
                return true;
            }

            var recentlyDamaged = runtime.LastDamageTakenAt > 0f && now - runtime.LastDamageTakenAt <= 2.5f;
            return recentlyDamaged && distance <= pushedDistance * 1.35f;
        }

        private void AddUtilityCandidates(
            List<TacticalActionCandidate> candidates,
            BaseCombatEntity bot,
            BotRuntime runtime,
            BasePlayer target,
            Vector3 knownThreatPosition,
            float healthFraction,
            bool lowHealthAware,
            bool atCover,
            bool hasFreshSeen,
            bool hasFreshHeard,
            bool hasRecentContact,
            bool targetInBase,
            float now,
            SquadBlackboard board)
        {
            if (bot == null || runtime == null || knownThreatPosition == Vector3.zero)
            {
                return;
            }

            if (targetInBase || IsBaseRestrictedPosition(knownThreatPosition) || SegmentCrossesBaseRestrictedArea(bot.transform.position, knownThreatPosition))
            {
                runtime.LastUtilityReason = "blocked_base";
                return;
            }

            if (!CanUseBotUtility(runtime, board, now, out var cooldownReason))
            {
                runtime.LastUtilityReason = cooldownReason;
                return;
            }

            CleanupBotUtilityRefs(now);

            if (botUtilityEntities.Count >= config.AI.MaxActiveBotUtilityProjectiles)
            {
                runtime.LastUtilityReason = "utility_cap";
                return;
            }

            var added = false;

            if (TryAddSmokeCandidate(candidates, bot, runtime, target, knownThreatPosition, healthFraction, lowHealthAware, atCover, hasFreshSeen, hasFreshHeard, hasRecentContact, now))
            {
                added = true;
            }

            if (TryAddGrenadeCandidate(candidates, bot, runtime, target, knownThreatPosition, healthFraction, hasFreshSeen, hasFreshHeard, hasRecentContact, now))
            {
                added = true;
            }

            if (!added && string.IsNullOrWhiteSpace(runtime.LastUtilityReason))
            {
                runtime.LastUtilityReason = "no_candidate";
            }
        }

        private bool TryAddGrenadeCandidate(
            List<TacticalActionCandidate> candidates,
            BaseCombatEntity bot,
            BotRuntime runtime,
            BasePlayer target,
            Vector3 knownThreatPosition,
            float healthFraction,
            bool hasFreshSeen,
            bool hasFreshHeard,
            bool hasRecentContact,
            float now)
        {
            if (!config.AI.AllowGrenades)
            {
                runtime.LastUtilityReason = "grenades_disabled";
                return false;
            }

            if (!hasFreshSeen && !hasFreshHeard && !hasRecentContact)
            {
                runtime.LastUtilityReason = "grenade_no_contact";
                return false;
            }

            var distance = Distance2D(bot.transform.position, knownThreatPosition);

            if (distance < config.AI.GrenadeMinThrowDistance || distance > config.AI.GrenadeMaxThrowDistance)
            {
                runtime.LastUtilityReason = $"grenade_range {distance.ToString("0", CultureInfo.InvariantCulture)}m";
                return false;
            }

            var targetIsOpen = runtime.Memory.HasLineOfSight && runtime.Memory.TargetExposureFraction >= 0.72f;
            var goodFlushMoment = !runtime.Memory.HasLineOfSight
                || runtime.Memory.TargetExposureFraction <= 0.55f
                || runtime.State == TacticalState.SearchLastKnown
                || runtime.State == TacticalState.FightFromCover
                || runtime.State == TacticalState.HoldOutsideBase;

            if (targetIsOpen && !goodFlushMoment && healthFraction > config.AI.LowHealthCoverThreshold)
            {
                runtime.LastUtilityReason = "grenade_target_open";
                return false;
            }

            if (!IsUtilityThrowSafe(bot, runtime, knownThreatPosition, runtime.Memory.TargetUserId, config.AI.GrenadeAllyAvoidRadius, true, out var safetyReason))
            {
                runtime.LastUtilityReason = safetyReason;
                return false;
            }

            var score = 84f + runtime.Skill.Aggression * 18f + runtime.Skill.Courage * 8f;

            if (!runtime.Memory.HasLineOfSight)
            {
                score += hasFreshSeen ? 18f : 10f;
            }

            if (runtime.Memory.TargetExposureFraction > 0f && runtime.Memory.TargetExposureFraction <= 0.35f)
            {
                score += 14f;
            }

            if (healthFraction < config.AI.LowHealthCoverThreshold)
            {
                score -= 18f;
            }

            if (target != null && Vector3.Distance(target.transform.position, knownThreatPosition) <= 5f)
            {
                score += 6f;
            }

            var grenade = Candidate(TacticalActionId.ThrowGrenade, score, "high", "flush last-known or covered target with a real F1 grenade", knownThreatPosition, runtime.Memory.TargetUserId, now);
            grenade.RiskFlags.Add("real_entity");
            grenade.RiskFlags.Add("explosive");
            grenade.RiskFlags.Add("team_cooldown");
            candidates.Add(grenade);
            runtime.LastUtilityReason = "grenade_candidate";
            return true;
        }

        private bool TryAddSmokeCandidate(
            List<TacticalActionCandidate> candidates,
            BaseCombatEntity bot,
            BotRuntime runtime,
            BasePlayer target,
            Vector3 knownThreatPosition,
            float healthFraction,
            bool lowHealthAware,
            bool atCover,
            bool hasFreshSeen,
            bool hasFreshHeard,
            bool hasRecentContact,
            float now)
        {
            if (!config.AI.AllowSmoke)
            {
                return false;
            }

            var underPressure = lowHealthAware
                || healthFraction <= config.AI.LowHealthCoverThreshold
                || runtime.LastDamageTakenAt > 0f && now - runtime.LastDamageTakenAt <= config.AI.DamageWallReactionWindowSeconds;

            if (!underPressure || atCover || (!hasFreshSeen && !hasFreshHeard && !hasRecentContact))
            {
                return false;
            }

            var distance = Distance2D(bot.transform.position, knownThreatPosition);

            if (distance < config.AI.SmokeMinThrowDistance || distance > config.AI.SmokeMaxThrowDistance)
            {
                return false;
            }

            var smokePosition = SmokeScreenPosition(bot.transform.position, knownThreatPosition);

            if (smokePosition == Vector3.zero)
            {
                runtime.LastUtilityReason = "smoke_no_screen";
                return false;
            }

            if (!IsUtilityThrowSafe(bot, runtime, smokePosition, runtime.Memory.TargetUserId, 0f, false, out var safetyReason))
            {
                runtime.LastUtilityReason = safetyReason;
                return false;
            }

            var wallCandidateExists = candidates.Any(candidate => candidate.ActionId == TacticalActionId.PlaceBarricade);
            var score = 112f + (1f - healthFraction) * 24f + SkillDiscipline(runtime) * 8f;

            if (wallCandidateExists)
            {
                score -= 18f;
            }

            if (!runtime.Memory.HasLineOfSight)
            {
                score -= 8f;
            }

            var smoke = Candidate(TacticalActionId.ThrowSmoke, score, "medium", "screen retreat lane with a real smoke grenade", smokePosition, runtime.Memory.TargetUserId, now);
            smoke.RiskFlags.Add("real_entity");
            smoke.RiskFlags.Add("smoke_screen");
            smoke.RiskFlags.Add("team_cooldown");
            candidates.Add(smoke);
            runtime.LastUtilityReason = "smoke_candidate";
            return true;
        }

        private bool CanUseBotUtility(BotRuntime runtime, SquadBlackboard board, float now, out string reason)
        {
            reason = "ready";

            if (runtime == null)
            {
                reason = "bad_runtime";
                return false;
            }

            if (now < runtime.NextGrenadeAt)
            {
                reason = $"utility_cd {Math.Max(0f, runtime.NextGrenadeAt - now).ToString("0", CultureInfo.InvariantCulture)}s";
                return false;
            }

            if (board != null && now < board.NextTeamGrenadeAt)
            {
                reason = $"team_cd {Math.Max(0f, board.NextTeamGrenadeAt - now).ToString("0", CultureInfo.InvariantCulture)}s";
                return false;
            }

            return true;
        }

        private bool IsUtilityThrowSafe(BaseCombatEntity bot, BotRuntime runtime, Vector3 impactPosition, ulong targetUserId, float allyAvoidRadius, bool avoidAllies, out string reason)
        {
            reason = "ready";

            if (bot == null || runtime == null || impactPosition == Vector3.zero)
            {
                reason = "utility_bad_input";
                return false;
            }

            if (IsBaseRestrictedPosition(impactPosition) || SegmentCrossesBaseRestrictedArea(bot.transform.position, impactPosition))
            {
                reason = "utility_base_blocked";
                return false;
            }

            if (avoidAllies && HasFriendlyBotNearImpact(bot, runtime, impactPosition, allyAvoidRadius))
            {
                reason = "grenade_ally_close";
                return false;
            }

            if (avoidAllies && HasNonTargetPlayerNearImpact(impactPosition, targetUserId, allyAvoidRadius))
            {
                reason = "grenade_bystander_close";
                return false;
            }

            return true;
        }

        private bool HasFriendlyBotNearImpact(BaseCombatEntity thrower, BotRuntime runtime, Vector3 impactPosition, float radius)
        {
            if (radius <= 0f)
            {
                return false;
            }

            foreach (var entry in activeBots)
            {
                var bot = entry.Key;
                var other = entry.Value;

                if (bot == null || bot == thrower || other == null || !SameBotClan(runtime, other) || !IsLiveBot(bot))
                {
                    continue;
                }

                if (Distance2D(bot.transform.position, impactPosition) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasNonTargetPlayerNearImpact(Vector3 impactPosition, ulong targetUserId, float radius)
        {
            if (radius <= 0f)
            {
                return false;
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!IsRealPlayer(player) || player.userID == targetUserId || ShouldIgnoreSafeZonePlayer(player))
                {
                    continue;
                }

                if (Distance2D(player.transform.position, impactPosition) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 SmokeScreenPosition(Vector3 botPosition, Vector3 threatPosition)
        {
            var toThreat = threatPosition - botPosition;
            toThreat.y = 0f;

            if (toThreat.sqrMagnitude <= 0.01f)
            {
                return Vector3.zero;
            }

            var distance = toThreat.magnitude;
            var screenDistance = Mathf.Clamp(config.AI.SmokeScreenDistance, 2f, Math.Max(2f, distance - 2f));
            var candidate = botPosition + toThreat.normalized * screenDistance;
            if (!TryProjectToLandSurface(ref candidate))
            {
                return Vector3.zero;
            }

            return IsBlockedLandPosition(candidate) ? Vector3.zero : candidate;
        }

        private bool TryFindUtilityDangerEscapePosition(BaseCombatEntity bot, BotRuntime runtime, float now, out Vector3 escapePoint, out string reason)
        {
            escapePoint = Vector3.zero;
            reason = "";

            if (bot == null || runtime == null)
            {
                return false;
            }

            var zone = utilityDangerZones
                .Where(danger => danger != null
                    && danger.ExpiresAt > now
                    && string.Equals(danger.UtilityType, "grenade", StringComparison.OrdinalIgnoreCase)
                    && Distance2D(bot.transform.position, danger.Position) <= danger.Radius + 1.5f)
                .OrderBy(danger => Distance2D(bot.transform.position, danger.Position))
                .FirstOrDefault();

            if (zone == null)
            {
                return false;
            }

            var away = bot.transform.position - zone.Position;
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
            var radius = Math.Max(zone.Radius + 4f, config.AI.GrenadeDangerRadius + 4f);
            var angles = new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 180f };

            foreach (var angle in angles)
            {
                var direction = Quaternion.Euler(0f, angle, 0f) * away;
                var candidate = bot.transform.position + direction.normalized * radius;

                if (!TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), now, out var sampled))
                {
                    continue;
                }

                if (SegmentCrossesBaseRestrictedArea(bot.transform.position, sampled) || IsInsideActiveUtilityDanger(sampled, now, "grenade"))
                {
                    continue;
                }

                escapePoint = sampled;
                reason = "inside bot grenade danger zone; move clear";
                runtime.LastUtilityReason = "avoid_grenade";
                return true;
            }

            escapePoint = FindRetreatPosition(bot.transform.position, zone.Position, runtime);
            reason = "inside bot grenade danger zone; fallback retreat";
            runtime.LastUtilityReason = "avoid_grenade_fallback";
            return escapePoint != Vector3.zero;
        }

        private bool IsInsideActiveUtilityDanger(Vector3 position, float now, string utilityType)
        {
            return utilityDangerZones.Any(danger => danger != null
                && danger.ExpiresAt > now
                && string.Equals(danger.UtilityType, utilityType, StringComparison.OrdinalIgnoreCase)
                && Distance2D(position, danger.Position) <= danger.Radius);
        }

        private void CleanupBotUtilityRefs(float now)
        {
            botUtilityEntities.RemoveAll(entry => entry == null || entry.Entity == null || entry.Entity.IsDestroyed);
            utilityDangerZones.RemoveAll(zone => zone == null || zone.ExpiresAt <= now);
        }

        private bool TryThrowBotUtility(BaseCombatEntity bot, BotRuntime runtime, Vector3 impactPosition, bool smoke, float now)
        {
            if (bot == null || runtime == null || impactPosition == Vector3.zero)
            {
                if (runtime != null)
                {
                    runtime.LastUtilityReason = "throw_bad_input";
                }

                return false;
            }

            CleanupBotUtilityRefs(now);

            if (botUtilityEntities.Count >= config.AI.MaxActiveBotUtilityProjectiles)
            {
                runtime.LastUtilityReason = "utility_cap";
                return false;
            }

            var prefab = smoke ? config.AI.SmokeGrenadePrefab : config.AI.GrenadePrefab;

            if (string.IsNullOrWhiteSpace(prefab))
            {
                runtime.LastUtilityReason = smoke ? "smoke_no_prefab" : "grenade_no_prefab";
                return false;
            }

            var start = EyePosition(bot) + Vector3.up * 0.1f;
            var towardImpact = impactPosition - start;

            if (towardImpact.sqrMagnitude <= 0.01f)
            {
                towardImpact = bot.transform.forward;
            }

            var rotation = Quaternion.LookRotation(new Vector3(towardImpact.x, 0f, towardImpact.z).sqrMagnitude <= 0.01f
                ? bot.transform.forward
                : new Vector3(towardImpact.x, 0f, towardImpact.z).normalized);
            var entity = GameManager.server.CreateEntity(prefab, start, rotation, true) as BaseEntity;

            if (entity == null)
            {
                runtime.LastUtilityReason = smoke ? "smoke_spawn_failed" : "grenade_spawn_failed";
                return false;
            }

            try
            {
                entity.OwnerID = (bot as BasePlayer)?.userID ?? 0UL;
                entity.SetCreatorEntity(bot);
            }
            catch
            {
            }

            var speed = smoke ? config.AI.SmokeThrowVelocity : config.AI.GrenadeThrowVelocity;
            var velocity = UtilityThrowVelocity(start, impactPosition, speed);
            var projectile = entity.GetComponent<ServerProjectile>();

            if (projectile != null)
            {
                projectile.speed = Math.Max(projectile.speed, speed);
                projectile.InitializeVelocity(velocity);
            }
            else
            {
                TryInvoke(entity, "SetVelocity", velocity);
                TryInvoke(entity, "ServerThrow", velocity);
            }

            if (!smoke)
            {
                var timed = entity.GetComponent<TimedExplosive>();

                if (timed != null)
                {
                    timed.timerAmountMin = config.AI.GrenadeFuseSeconds;
                    timed.timerAmountMax = config.AI.GrenadeFuseSeconds;
                }
            }

            entity.Spawn();

            if (projectile != null)
            {
                projectile.SetVelocity(velocity);
            }

            botUtilityEntities.Add(new BotUtilityEntity
            {
                Entity = entity,
                BotKey = runtime.BotKey,
                TeamId = runtime.TeamId,
                UtilityType = smoke ? "smoke" : "grenade",
                SpawnedAt = now
            });

            if (!smoke)
            {
                utilityDangerZones.Add(new UtilityDangerZone
                {
                    Position = impactPosition,
                    Radius = config.AI.GrenadeDangerRadius,
                    ExpiresAt = now + config.AI.GrenadeAvoidanceSeconds,
                    BotKey = runtime.BotKey,
                    TeamId = runtime.TeamId,
                    UtilityType = "grenade"
                });
            }

            runtime.LastUtilityReason = smoke
                ? $"smoke_thrown {Distance2D(bot.transform.position, impactPosition).ToString("0", CultureInfo.InvariantCulture)}m"
                : $"grenade_thrown {Distance2D(bot.transform.position, impactPosition).ToString("0", CultureInfo.InvariantCulture)}m";

            if (config.Debug.DebugTacticalDecisions)
            {
                DebugLog($"utility-throw:{runtime.BotKey}", $"{runtime.DisplayName} threw {(smoke ? "smoke" : "grenade")} toward {FormatVector(impactPosition)}.");
            }

            return true;
        }

        private Vector3 UtilityThrowVelocity(Vector3 start, Vector3 impactPosition, float speed)
        {
            var delta = impactPosition - start;
            var horizontal = new Vector3(delta.x, 0f, delta.z);

            if (horizontal.sqrMagnitude <= 0.01f)
            {
                horizontal = Vector3.forward;
            }

            var distance = horizontal.magnitude;
            var upward = Mathf.Clamp(4f + distance * 0.11f + delta.y * 0.25f, 4f, Math.Max(5f, speed * 0.78f));
            return horizontal.normalized * speed + Vector3.up * upward;
        }

        private void MarkBotUtilityCooldown(BotRuntime runtime, float now)
        {
            if (runtime == null)
            {
                return;
            }

            runtime.NextGrenadeAt = now + config.AI.GrenadeCooldownSeconds;
            var board = SquadBoardFor(runtime);

            if (board != null)
            {
                board.NextTeamGrenadeAt = now + config.AI.TeamGrenadeCooldownSeconds;
            }
        }

        private Vector3 FindUtilityPostThrowDestination(BaseCombatEntity bot, BotRuntime runtime, Vector3 impactPosition, float now)
        {
            if (bot == null || runtime == null)
            {
                return Vector3.zero;
            }

            if (TryFindUtilityDangerEscapePosition(bot, runtime, now, out var escape, out _))
            {
                return escape;
            }

            if (runtime.CurrentTuckPoint != Vector3.zero && !IsInsideActiveUtilityDanger(runtime.CurrentTuckPoint, now, "grenade"))
            {
                return runtime.CurrentTuckPoint;
            }

            return FindRetreatPosition(bot.transform.position, impactPosition, runtime);
        }

        private TacticalActionCandidate Candidate(TacticalActionId actionId, float score, string risk, string reason, Vector3 destination, ulong targetUserId, float now)
        {
            return new TacticalActionCandidate
            {
                Id = ActionIdString(actionId),
                ActionId = actionId,
                BaseHeuristicScore = score,
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
            var decision = new TacticalDecision
            {
                Selected = SelectFallbackCandidate(candidates)
            };
            DecisionAdvisorResult advisorResult = null;

            if (ShouldAskAdvisor(bot, runtime, request, candidates, now))
            {
                decision.AdvisorRequested = true;
                runtime.Decisions.LastAdvisorRequestAt = now;
                decisionAdvisor = decisionAdvisor ?? new NullDecisionAdvisor();

                var callbackReturned = false;
                var submitted = decisionAdvisor.TrySubmit(request, result =>
                {
                    if (!callbackReturned)
                    {
                        advisorResult = result;
                        return;
                    }

                    HandleAsyncAdvisorResult(request, candidates, decision.Selected, result);
                });
                callbackReturned = true;

                advisorStats.TotalRequests++;

                if (submitted)
                {
                    advisorStats.SubmittedRequests++;
                    RegisterPendingAdvisorRequest(request, runtime, decision.Selected, now);
                    decision.AdvisorStatus = "advisor_pending";
                }
                else
                {
                    decision.AdvisorStatus = advisorResult?.Status ?? "advisor_no_response";
                    advisorStats.SynchronousFailures++;
                    advisorStats.LastStatus = decision.AdvisorStatus;
                }

                runtime.Decisions.LastAdvisorStatus = decision.AdvisorStatus;
            }

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
                    advisor_action = "",
                    advisor_confidence = 0f,
                    advisor_latency_ms = 0,
                    advisor_rationale = "",
                    fallback_reason = decision.FallbackReason,
                    final_action = decision.Selected?.Id ?? "none",
                    final_score = decision.Selected?.HeuristicScore ?? 0f,
                    behavior_model_key = runtime.BehaviorModelKey ?? "",
                    player_profile_key = runtime.PlayerProfileKey ?? "",
                    learned_score_delta = decision.Selected?.LearnedScoreDelta ?? 0f,
                    learned_reason = decision.Selected?.LearnedReason ?? runtime.LastLearnedReason ?? "none",
                    protection_state = runtime.LastProtectionReason ?? "none",
                    barricade_anchor_state = BarricadeAnchorStatus(runtime, now),
                    medical_state = MedicalStatus(bot, runtime, now),
                    candidates = candidates,
                    created_at = now
                });
            }

            return decision;
        }

        private void RegisterPendingAdvisorRequest(DecisionRequest request, BotRuntime runtime, TacticalActionCandidate fallback, float now)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                return;
            }

            pendingAdvisorDecisions[request.RequestId] = new PendingAdvisorDecision
            {
                RequestId = request.RequestId,
                BotKey = runtime?.BotKey ?? request.BotId ?? "",
                FallbackActionId = fallback?.Id ?? "none",
                SubmittedAt = now,
                ExpiresAt = now + Math.Max(1f, (config.DecisionAdvisor.TimeoutMilliseconds + config.DecisionAdvisor.DecisionTtlMilliseconds) / 1000f)
            };
        }

        private void HandleAsyncAdvisorResult(DecisionRequest request, List<TacticalActionCandidate> candidates, TacticalActionCandidate fallback, DecisionAdvisorResult result)
        {
            var now = Time.realtimeSinceStartup;
            PendingAdvisorDecision pending = null;

            if (request != null && !string.IsNullOrWhiteSpace(request.RequestId))
            {
                pendingAdvisorDecisions.TryGetValue(request.RequestId, out pending);
                pendingAdvisorDecisions.Remove(request.RequestId);
            }

            var botKey = pending?.BotKey ?? request?.BotId ?? "";
            TacticalActionCandidate advisorCandidate;
            var status = ValidateAdvisorResult(request, candidates, result, now, out advisorCandidate);
            var accepted = status == "advisor_ok";

            RecordAdvisorResultStats(status, result, accepted);

            var runtime = ActiveRuntimeByKey(botKey);

            if (runtime != null)
            {
                runtime.Decisions.LastAdvisorStatus = status;
                runtime.Decisions.LastAdvisorActionId = advisorCandidate?.Id ?? "";
                runtime.Decisions.LastAdvisorConfidence = result?.Confidence ?? 0f;
                runtime.Decisions.LastAdvisorRationale = result?.Rationale ?? "";
            }

            if (config.Debug.DebugDecisionAdvisor)
            {
                DebugLog($"advisor-result:{request?.BotId ?? "unknown"}", $"Advisor result {request?.RequestId ?? "unknown"} status={status} action={advisorCandidate?.Id ?? result?.ActionId ?? "none"} confidence={(result?.Confidence ?? 0f):0.00} latency={result?.LatencyMilliseconds ?? 0}ms fallback={fallback?.Id ?? "none"}");
            }

            if (config.DecisionAdvisor.LogDecisionTraces)
            {
                QueueDecisionTrace(new DecisionTrace
                {
                    request_id = request?.RequestId ?? "",
                    bot_id = botKey,
                    team_id = request?.TeamId ?? 0,
                    clan_key = request?.ClanKey ?? "",
                    clan_tag = request?.ClanTag ?? "",
                    state = request?.State ?? "",
                    advisor_requested = true,
                    advisor_status = status,
                    advisor_action = advisorCandidate?.Id ?? result?.ActionId ?? "",
                    advisor_confidence = result?.Confidence ?? 0f,
                    advisor_latency_ms = result?.LatencyMilliseconds ?? 0,
                    advisor_rationale = result?.Rationale ?? "",
                    fallback_reason = accepted ? $"{config.DecisionAdvisor.Mode}_advisor_validated_heuristic_executed" : $"advisor_rejected:{status}",
                    final_action = fallback?.Id ?? "none",
                    final_score = fallback?.HeuristicScore ?? 0f,
                    behavior_model_key = runtime?.BehaviorModelKey ?? "",
                    player_profile_key = runtime?.PlayerProfileKey ?? "",
                    learned_score_delta = fallback?.LearnedScoreDelta ?? 0f,
                    learned_reason = fallback?.LearnedReason ?? runtime?.LastLearnedReason ?? "none",
                    protection_state = runtime?.LastProtectionReason ?? "none",
                    barricade_anchor_state = runtime == null ? "none" : BarricadeAnchorStatus(runtime, now),
                    medical_state = runtime == null ? "none" : MedicalStatus(null, runtime, now),
                    candidates = candidates,
                    created_at = now
                });
            }
        }

        private string ValidateAdvisorResult(DecisionRequest request, List<TacticalActionCandidate> candidates, DecisionAdvisorResult result, float now, out TacticalActionCandidate selected)
        {
            selected = null;

            if (result == null)
            {
                return "advisor_no_response";
            }

            if (!result.Success)
            {
                return string.IsNullOrWhiteSpace(result.Status) ? "advisor_failure" : result.Status;
            }

            if (string.IsNullOrWhiteSpace(result.ActionId))
            {
                return "advisor_missing_action";
            }

            selected = candidates?.FirstOrDefault(candidate => string.Equals(candidate.Id, result.ActionId.Trim(), StringComparison.OrdinalIgnoreCase));

            if (selected == null)
            {
                return "advisor_invalid_action";
            }

            if (result.Confidence < config.DecisionAdvisor.MinimumConfidence)
            {
                return "advisor_low_confidence";
            }

            if (result.TtlMilliseconds <= 0 || result.TtlMilliseconds > config.DecisionAdvisor.DecisionTtlMilliseconds)
            {
                return "advisor_ttl_rejected";
            }

            if (selected.ExpiresAt > 0f && now > selected.ExpiresAt)
            {
                return "advisor_late";
            }

            if (IsMovementDestinationAction(selected.ActionId) && selected.Destination != Vector3.zero && IsBaseRestrictedPosition(selected.Destination))
            {
                return "advisor_base_blocked";
            }

            if (selected.TargetUserId != 0)
            {
                var target = FindCombatTargetById(selected.TargetUserId);

                if (target == null || target.IsDead() || target.IsSleeping() || RuntimeFor(target) == null && !target.IsConnected)
                {
                    return "advisor_target_invalid";
                }
            }

            return "advisor_ok";
        }

        private void RecordAdvisorResultStats(string status, DecisionAdvisorResult result, bool accepted)
        {
            advisorStats.LastStatus = status;
            advisorStats.LastActionId = result?.ActionId ?? "";
            advisorStats.LastConfidence = result?.Confidence ?? 0f;
            advisorStats.LastLatencyMilliseconds = result?.LatencyMilliseconds ?? 0;
            advisorStats.LastRationale = result?.Rationale ?? "";

            if (accepted)
            {
                advisorStats.SuccessResponses++;
                return;
            }

            advisorStats.RejectedResponses++;

            if (status == "advisor_invalid_json" || status == "advisor_empty_response")
            {
                advisorStats.InvalidJsonResponses++;
            }
            else if (status == "advisor_invalid_action" || status == "advisor_missing_action")
            {
                advisorStats.InvalidActionResponses++;
            }
            else if (status == "advisor_low_confidence")
            {
                advisorStats.LowConfidenceResponses++;
            }
            else if (status == "advisor_late" || status == "advisor_ttl_rejected")
            {
                advisorStats.LateResponses++;
            }
            else if (status == "advisor_timeout_or_network")
            {
                advisorStats.TimeoutResponses++;
            }
            else if (status.StartsWith("advisor_http_", StringComparison.OrdinalIgnoreCase))
            {
                advisorStats.HttpFailures++;
            }
        }

        private BotRuntime ActiveRuntimeByKey(string botKey)
        {
            if (string.IsNullOrWhiteSpace(botKey))
            {
                return null;
            }

            return activeBots.Values.FirstOrDefault(runtime => string.Equals(runtime?.BotKey, botKey, StringComparison.OrdinalIgnoreCase));
        }

        private int PendingAdvisorRequestCount()
        {
            PruneExpiredAdvisorRequests(Time.realtimeSinceStartup);
            return pendingAdvisorDecisions.Count;
        }

        private void PruneExpiredAdvisorRequests(float now)
        {
            var expired = pendingAdvisorDecisions
                .Where(entry => entry.Value == null || entry.Value.ExpiresAt <= now)
                .Select(entry => entry.Key)
                .ToList();

            foreach (var key in expired)
            {
                pendingAdvisorDecisions.Remove(key);
            }

            if (expired.Count > 0)
            {
                advisorStats.TimeoutResponses += expired.Count;
                advisorStats.LastStatus = "advisor_timeout";
            }
        }

        private bool ShouldAskAdvisor(BaseCombatEntity bot, BotRuntime runtime, DecisionRequest request, List<TacticalActionCandidate> candidates, float now)
        {
            if (config.DecisionAdvisor == null || !config.DecisionAdvisor.Enabled || runtime == null || candidates == null || candidates.Count <= 1)
            {
                return false;
            }

            if (now - runtime.Decisions.LastAdvisorRequestAt < config.DecisionAdvisor.MinSecondsBetweenRequestsPerBot)
            {
                return false;
            }

            var hasDecisionTrigger = config.DecisionAdvisor.AskWhenActionScoresAreClose && AreTopScoresClose(candidates)
                || config.DecisionAdvisor.AskWhenBotIsStuck && runtime.ConsecutiveFailedPaths > 0
                || config.DecisionAdvisor.AskWhenPushRetreatOrFlankIsHighImpact && HasHighImpactCandidate(candidates);

            if (!hasDecisionTrigger)
            {
                return false;
            }

            string engagementStatus;

            if (!CanAskAdvisorForPlayerEngagement(bot, runtime, request, now, out engagementStatus))
            {
                runtime.Decisions.LastAdvisorStatus = engagementStatus;
                advisorStats.EngagementSkips++;
                advisorStats.LastStatus = engagementStatus;
                return false;
            }

            string proximityStatus;

            if (!CanAskAdvisorNearRealPlayer(bot, request, out proximityStatus))
            {
                runtime.Decisions.LastAdvisorStatus = proximityStatus;
                advisorStats.ProximitySkips++;
                advisorStats.LastStatus = proximityStatus;
                return false;
            }

            return true;
        }

        private bool CanAskAdvisorForPlayerEngagement(BaseCombatEntity bot, BotRuntime runtime, DecisionRequest request, float now, out string status)
        {
            status = "";

            if (config.DecisionAdvisor.RequireActivePlayerEngagement != true)
            {
                return true;
            }

            if (request?.EngagedWithRealPlayer == true)
            {
                return true;
            }

            var engagementSignal = PlayerEngagementSignal(bot, runtime, now);

            if (!string.IsNullOrWhiteSpace(engagementSignal))
            {
                return true;
            }

            status = "advisor_skipped_not_engaged_with_player";
            return false;
        }

        private bool CanAskAdvisorNearRealPlayer(BaseCombatEntity bot, DecisionRequest request, out string status)
        {
            status = "";
            var gateMeters = Math.Max(0f, config.DecisionAdvisor.RequireRealPlayerWithinMeters);

            if (gateMeters <= 0f)
            {
                return true;
            }

            var nearestDistance = request?.NearestRealPlayerDistance ?? -1f;

            if (nearestDistance < 0f && bot != null)
            {
                nearestDistance = DistanceToNearestRealPlayer(bot.transform.position);
            }

            if (nearestDistance >= 0f && nearestDistance <= gateMeters)
            {
                return true;
            }

            status = nearestDistance < 0f
                ? $"advisor_skipped_no_real_player_within_{gateMeters.ToString("0", CultureInfo.InvariantCulture)}m"
                : $"advisor_skipped_nearest_real_player_{nearestDistance.ToString("0", CultureInfo.InvariantCulture)}m_over_{gateMeters.ToString("0", CultureInfo.InvariantCulture)}m";

            return false;
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
                || candidate.ActionId == TacticalActionId.ThrowGrenade
                || candidate.ActionId == TacticalActionId.ThrowSmoke
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
                DebugLog($"decision:{runtime.BotKey}", $"{runtime.DisplayName} {runtime.State} -> {selected.Id} score={selected.HeuristicScore:0.0} advisor={decision.AdvisorStatus} fallback={decision.FallbackReason}");
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
                    if (action.RiskFlags.Contains("protection_damage"))
                    {
                        ResetProtectionDamageTrigger(runtime, "moved_to_protection", now);
                    }

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
                    if (IsBarricadeAnchorActive(runtime, now) && CanLeaveBarricadeAnchor(runtime, now))
                    {
                        ClearBarricadeAnchor(runtime, "push_unlocked");
                    }

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
                    if (action.RiskFlags.Contains("protection_damage"))
                    {
                        ResetProtectionDamageTrigger(runtime, "moved_to_protection", now);
                    }

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
                            runtime.CurrentDestination = FindRetreatPosition(bot.transform.position, barricadeThreatPosition, runtime);
                            MoveBotTo(bot, runtime, runtime.CurrentDestination, BaseNavigator.NavigationSpeed.Fast);
                            FacePosition(bot, barricadeThreatPosition);
                            MaintainFireOrStop(bot, runtime, now);
                            break;
                        }

                        runtime.CurrentTuckPoint = holdPoint;
                        runtime.CurrentCover = runtime.CurrentTuckPoint;
                        runtime.CurrentPeekPoint = BarricadePeekPoint(bot, runtime.CurrentTuckPoint, action.Destination, barricadeThreatPosition, runtime.Memory.Target, runtime, now);
                        runtime.CurrentDestination = runtime.CurrentTuckPoint;
                        runtime.IsPeeking = false;
                        runtime.CurrentTuckUntil = now + UnityEngine.Random.Range(config.AI.TuckMinSeconds, config.AI.TuckMaxSeconds);
                        runtime.NextPeekAt = runtime.CurrentTuckUntil;
                        runtime.HoldOutsideBaseUntil = now + config.AI.BarricadeHoldSeconds;
                        runtime.BarricadeCommittedUntil = now + config.AI.BarricadeFightCommitmentSeconds;
                        ResetProtectionDamageTrigger(runtime, "placed_barricade", now);
                        var anchorThreatDistance = barricadeThreatPosition == Vector3.zero ? 0f : Vector3.Distance(bot.transform.position, barricadeThreatPosition);
                        StartBarricadeAnchorIfNeeded(runtime, anchorThreatDistance, runtime.Memory.TargetUserId, now);
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
                    if (TryThrowBotUtility(bot, runtime, action.Destination, false, now))
                    {
                        MarkBotUtilityCooldown(runtime, now);
                        var escape = FindUtilityPostThrowDestination(bot, runtime, action.Destination, now);
                        runtime.CurrentDestination = escape;
                        MoveBotTo(bot, runtime, escape, BaseNavigator.NavigationSpeed.Fast);
                        FacePosition(bot, action.Destination);
                    }
                    else
                    {
                        runtime.NextGrenadeAt = now + Math.Min(5f, config.AI.GrenadeCooldownSeconds);
                    }
                    break;

                case TacticalActionId.ThrowSmoke:
                    SetState(runtime, TacticalState.Retreat, now);
                    StopBotAttack(bot, runtime);
                    if (TryThrowBotUtility(bot, runtime, action.Destination, true, now))
                    {
                        MarkBotUtilityCooldown(runtime, now);
                        var retreat = FindRetreatPosition(bot.transform.position, KnownThreatPosition(runtime), runtime);
                        runtime.CurrentDestination = retreat;
                        MoveBotTo(bot, runtime, retreat, BaseNavigator.NavigationSpeed.Fast);
                        FacePosition(bot, KnownThreatPosition(runtime));
                    }
                    else
                    {
                        runtime.NextGrenadeAt = now + Math.Min(5f, config.AI.GrenadeCooldownSeconds);
                    }
                    break;

                case TacticalActionId.RoamToPoint:
                    SetState(runtime, TacticalState.Roam, now);
                    StopBotAttack(bot, runtime);
                    runtime.IsPeeking = false;
                    runtime.CurrentDestination = action.Destination == Vector3.zero ? FindRoamDestination(runtime.HomePosition, runtime) : action.Destination;

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
                    runtime.CurrentDestination = action.Destination == Vector3.zero ? FindRoamDestination(runtime.HomePosition, runtime) : action.Destination;
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
            runtime.AimWarmupTargetUserId = 0UL;
            runtime.AimWarmupStartedAt = 0f;
            runtime.CurrentAimErrorDegrees = AimErrorDegreesAt(runtime, Time.realtimeSinceStartup);
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
                MaybePruneDecisionTraceFile(dataPath, false);
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
            return ReadRecentDecisionTraceLines(path, Math.Max(1, count), normalizedBotKey, DecisionTraceReadMaxBytes());
        }

        private bool MaybePruneDecisionTraceFile(string path, bool force)
        {
            string message;
            var pruned = TryPruneDecisionTraceFile(path, force, out message);

            if (pruned && !string.IsNullOrWhiteSpace(message))
            {
                Puts(message);
            }

            return pruned;
        }

        private bool TryPruneDecisionTraceFile(string path, bool force, out string message)
        {
            message = "";

            try
            {
                var decisionConfig = config?.DecisionAdvisor;

                if (decisionConfig == null)
                {
                    message = "Decision trace pruning skipped: decision advisor config is not loaded.";
                    return false;
                }

                if (decisionConfig.MaxDecisionTraceFileMegabytes <= 0 || decisionConfig.MaxDecisionTraceLinesAfterPrune <= 0)
                {
                    message = "Decision trace pruning is disabled by config.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    message = "Decision trace file does not exist yet.";
                    return false;
                }

                var now = Time.realtimeSinceStartup;
                var interval = Math.Max(15f, decisionConfig.DecisionTracePruneCheckIntervalSeconds);

                if (!force && lastDecisionTracePruneCheckAt > 0f && now - lastDecisionTracePruneCheckAt < interval)
                {
                    message = $"Decision trace pruning skipped: next check in {(interval - (now - lastDecisionTracePruneCheckAt)):0}s.";
                    return false;
                }

                lastDecisionTracePruneCheckAt = now;
                var info = new FileInfo(path);
                var maxBytes = DecisionTraceMaxBytes();

                if (info.Length <= maxBytes)
                {
                    message = $"Decision trace file is already within retention: {FormatFileSize(info.Length)} / {FormatFileSize(maxBytes)}.";
                    return false;
                }

                var beforeBytes = info.Length;
                var retainedLines = ReadRecentDecisionTraceLines(path, decisionConfig.MaxDecisionTraceLinesAfterPrune, "", maxBytes);
                var tempPath = path + ".tmp";

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(tempPath, retainedLines);

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tempPath, path);

                var afterBytes = new FileInfo(path).Length;
                message = $"Pruned decision trace JSONL from {FormatFileSize(beforeBytes)} to {FormatFileSize(afterBytes)}; kept {retainedLines.Count} recent line{(retainedLines.Count == 1 ? "" : "s")} (cap {FormatFileSize(maxBytes)} / {decisionConfig.MaxDecisionTraceLinesAfterPrune} lines).";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Could not prune roam bot decision traces: {ex.GetType().Name}: {ex.Message}";
                PrintWarning(message);
                return false;
            }
        }

        private List<string> ReadRecentDecisionTraceLines(string path, int count, string botKey, long maxBytesToScan)
        {
            var matches = new List<string>();

            if (count <= 0 || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return matches;
            }

            var normalizedBotKey = (botKey ?? "").Trim();

            try
            {
                const int bufferSize = 65536;
                var buffer = new byte[bufferSize];
                var carry = "";

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var position = stream.Length;
                    var bytesScanned = 0L;

                    while (position > 0 && matches.Count < count && (maxBytesToScan <= 0L || bytesScanned < maxBytesToScan))
                    {
                        var remainingScanBytes = maxBytesToScan <= 0L ? position : Math.Min(position, maxBytesToScan - bytesScanned);

                        if (remainingScanBytes <= 0L)
                        {
                            break;
                        }

                        var bytesToRead = (int) Math.Min(bufferSize, remainingScanBytes);
                        position -= bytesToRead;
                        stream.Seek(position, SeekOrigin.Begin);
                        var read = stream.Read(buffer, 0, bytesToRead);

                        if (read <= 0)
                        {
                            break;
                        }

                        bytesScanned += read;
                        var chunk = System.Text.Encoding.UTF8.GetString(buffer, 0, read) + carry;
                        var lines = chunk.Split('\n');
                        carry = lines.Length > 0 ? lines[0] : "";

                        for (var index = lines.Length - 1; index >= 1 && matches.Count < count; index--)
                        {
                            var line = lines[index].TrimEnd('\r');

                            if (ShouldIncludeDecisionTraceLine(line, normalizedBotKey))
                            {
                                matches.Add(line);
                            }
                        }
                    }

                    if (position <= 0 && matches.Count < count && ShouldIncludeDecisionTraceLine(carry, normalizedBotKey))
                    {
                        matches.Add(carry.TrimEnd('\r'));
                    }
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read roam bot decision traces: {ex.GetType().Name}: {ex.Message}");
            }

            matches.Reverse();
            return matches;
        }

        private bool ShouldIncludeDecisionTraceLine(string line, string normalizedBotKey)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(normalizedBotKey) || DecisionTraceLineMatchesBot(line, normalizedBotKey);
        }

        private long DecisionTraceMaxBytes()
        {
            var megabytes = Math.Max(1, config?.DecisionAdvisor?.MaxDecisionTraceFileMegabytes ?? 128);
            return megabytes * 1024L * 1024L;
        }

        private long DecisionTraceReadMaxBytes()
        {
            return Math.Max(8L * 1024L * 1024L, DecisionTraceMaxBytes());
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
                var advisorAction = (string) json["advisor_action"] ?? "";
                var advisorConfidence = json.Value<float?>("advisor_confidence") ?? 0f;
                var advisorLatency = json.Value<int?>("advisor_latency_ms") ?? 0;
                var fallback = (string) json["fallback_reason"] ?? "none";
                var protection = (string) json["protection_state"] ?? "none";
                var anchor = (string) json["barricade_anchor_state"] ?? "none";
                var medical = (string) json["medical_state"] ?? "none";
                var model = (string) json["behavior_model_key"] ?? "";
                var profile = (string) json["player_profile_key"] ?? "";
                var learnedDelta = json.Value<float?>("learned_score_delta") ?? 0f;
                var learnedReason = (string) json["learned_reason"] ?? "none";
                var candidates = (json["candidates"] as JArray)?.Count ?? 0;
                var advisor = string.IsNullOrWhiteSpace(advisorAction)
                    ? $"advisor={advisorStatus}"
                    : $"advisor={advisorStatus}/{advisorAction} conf={advisorConfidence:0.00} latency={advisorLatency}ms";
                var learning = string.IsNullOrWhiteSpace(model)
                    ? "learn=none"
                    : $"learn={model}{(string.IsNullOrWhiteSpace(profile) ? "" : "/" + profile)} delta={learnedDelta:0.0} reason={learnedReason}";

                return $"{botId}: state={state}, action={finalAction}, score={finalScore:0.0}, candidates={candidates}, {learning}, protect={protection}, anchor={anchor}, heal={medical}, {advisor}, fallback={fallback}";
            }
            catch
            {
                return line.Length <= 240 ? line : line.Substring(0, 240);
            }
        }

        private string AdvisorStatusLine()
        {
            PruneExpiredAdvisorRequests(Time.realtimeSinceStartup);
            var advisor = decisionAdvisor ?? new NullDecisionAdvisor();
            var decisionConfig = config.DecisionAdvisor;
            var endpoint = string.IsNullOrWhiteSpace(decisionConfig.EndpointUrl) ? "missing" : "set";
            var model = string.IsNullOrWhiteSpace(decisionConfig.Model) ? "missing" : "set";
            var key = decisionConfig.Provider == AdvisorProviderNone
                ? "not_used"
                : (HasResolvedAdvisorApiKey() ? "set" : "missing");
            var keySource = DescribeSecretSource(decisionConfig.ApiKey);
            var playerGate = decisionConfig.RequireRealPlayerWithinMeters <= 0f ? "off" : KillDistanceLabel(decisionConfig.RequireRealPlayerWithinMeters);
            var engagementGate = decisionConfig.RequireActivePlayerEngagement ? $"on/{decisionConfig.PlayerEngagementMemorySeconds.ToString("0", CultureInfo.InvariantCulture)}s" : "off";

            return $"Raidlands roam bot advisor: enabled={decisionConfig.Enabled}, provider={decisionConfig.Provider}, mode={decisionConfig.Mode}, shadow={decisionConfig.ShadowMode}, engagedOnly={engagementGate}, playerGate={playerGate}, configured={advisor.IsConfigured}, endpoint={endpoint}, model={model}, apiKey={key}, apiKeySource={keySource}, pending={pendingAdvisorDecisions.Count}/{decisionConfig.MaxConcurrentRequests}, last={advisorStats.LastStatus}.";
        }

        private string AdvisorStatsLine()
        {
            PruneExpiredAdvisorRequests(Time.realtimeSinceStartup);
            return $"Raidlands roam bot advisor stats: requests={advisorStats.TotalRequests}, submitted={advisorStats.SubmittedRequests}, pending={pendingAdvisorDecisions.Count}, engagement_skips={advisorStats.EngagementSkips}, proximity_skips={advisorStats.ProximitySkips}, sync_failures={advisorStats.SynchronousFailures}, ok={advisorStats.SuccessResponses}, rejected={advisorStats.RejectedResponses}, invalid_json={advisorStats.InvalidJsonResponses}, invalid_action={advisorStats.InvalidActionResponses}, low_confidence={advisorStats.LowConfidenceResponses}, late={advisorStats.LateResponses}, http={advisorStats.HttpFailures}, timeout={advisorStats.TimeoutResponses}, last={advisorStats.LastStatus}/{advisorStats.LastActionId} conf={advisorStats.LastConfidence:0.00} latency={advisorStats.LastLatencyMilliseconds}ms.";
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
                DebugLog($"body-prepare:{runtime.BotKey}:{phase}", $"NPC body prepare ({phase}) for {runtime.DisplayName}: {BotRuntimeDiagnostics(bot, runtime)}.");
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
                    DebugWarning("suppress-targeting", $"Could not suppress scientist body targeting: {ex.GetType().Name}: {ex.Message}");
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

            var now = Time.realtimeSinceStartup;

            if (IsBlockedLandPosition(destination))
            {
                runtime.ConsecutiveFailedPaths++;
                runtime.Movement.SameActionFailures++;
                RememberBadDestination(runtime, destination, "blocked_destination", now);
                return false;
            }

            runtime.IsInBaseRestrictedArea = IsBaseRestrictedPosition(bot.transform.position);

            if (!runtime.IsInBaseRestrictedArea && SegmentCrossesBaseRestrictedArea(bot.transform.position, destination))
            {
                runtime.ConsecutiveFailedPaths++;
                runtime.Movement.SameActionFailures++;
                RememberBadDestination(runtime, destination, "base_blocked_path", now);
                return false;
            }

            CleanupBotUtilityRefs(now);

            if (IsInsideActiveUtilityDanger(destination, now, "grenade")
                && TryFindUtilityDangerEscapePosition(bot, runtime, now, out var utilityEscape, out _))
            {
                destination = utilityEscape;
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
                        DebugWarning($"npc-destination:{runtime.BotKey}", $"NPCPlayer.SetDestination failed for {ShortPrefab(bot.PrefabName)}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            var navigator = bot.GetComponent<BaseNavigator>() ?? bot.GetComponentInChildren<BaseNavigator>();
            var previousDestination = runtime.CurrentDestination;
            var destinationChanged = previousDestination == Vector3.zero || Vector3.Distance(previousDestination, destination) > 2f;
            navigatorCommanded = CommandNavigator(bot, navigator, destination, speed);
            runtime.CurrentDestination = destination;
            runtime.Movement.LastCommandAt = now;
            runtime.Movement.LastCommandDestination = destination;

            var movedOk = navigatorCommanded || npcCommanded;

            if (movedOk)
            {
                runtime.Movement.LastActionId = runtime.Decisions.LastActionId;
                if (destinationChanged || runtime.Movement.LastProgressAt <= 0f)
                {
                    runtime.Movement.LastProgressAt = now;
                }

                RecordSquadDestinationClaim(runtime, destination);
            }
            else
            {
                runtime.ConsecutiveFailedPaths++;
                runtime.Movement.SameActionFailures++;
                RememberBadDestination(runtime, destination, "nav_command_failed", now);
            }

            return movedOk;
        }

        private void RecordSquadDestinationClaim(BotRuntime runtime, Vector3 destination)
        {
            if (runtime == null || destination == Vector3.zero)
            {
                return;
            }

            var board = SquadBoardFor(runtime);

            if (board == null)
            {
                return;
            }

            board.DestinationClaims[runtime.BotKey] = destination;
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
                    DebugWarning($"navigator-destination:{ShortPrefab(bot?.PrefabName)}", $"BaseNavigator.SetDestination failed for {ShortPrefab(bot?.PrefabName)}: {ex.GetType().Name}: {ex.Message}");
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
            StartAimWarmup(runtime, CombatTargetId(target), Time.realtimeSinceStartup);
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
                        DebugWarning($"attack-start:{runtime.BotKey}", $"IAIAttack.StartAttacking failed for {ShortPrefab(bot.PrefabName)}: {ex.GetType().Name}: {ex.Message}");
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

        private Vector3 FindRoamDestination(Vector3 origin, BotRuntime runtime = null)
        {
            var radius = Math.Max(12f, config.Spawn.GroupSpawnRadius * 3f);
            var now = Time.realtimeSinceStartup;

            for (var attempt = 0; attempt < 14; attempt++)
            {
                var attemptRadius = radius * UnityEngine.Random.Range(0.55f, 1.35f);
                var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var candidate = origin + new Vector3(Mathf.Cos(angle) * attemptRadius, 0f, Mathf.Sin(angle) * attemptRadius);

                if (TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(6f, config.Spawn.NavmeshSampleDistance), now, out var sampled))
                {
                    return sampled;
                }
            }

            return origin;
        }

        private Vector3 MoveTowardPosition(Vector3 origin, Vector3 targetPosition, float maxStep, BotRuntime runtime = null)
        {
            var delta = targetPosition - origin;
            delta.y = 0f;

            if (delta.sqrMagnitude <= 0.01f)
            {
                return origin;
            }

            var now = Time.realtimeSinceStartup;
            var forward = delta.normalized;
            var step = Mathf.Clamp(maxStep, 4f, 45f);
            var angles = new[] { 0f, 18f, -18f, 35f, -35f, 55f, -55f };

            foreach (var angle in angles)
            {
                var direction = Quaternion.Euler(0f, angle, 0f) * forward;
                var candidate = origin + direction.normalized * step;

                if (TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(12f, config.Spawn.NavmeshSampleDistance), now, out var sampled))
                {
                    return sampled;
                }
            }

            return origin;
        }

        private Vector3 FindRetreatPosition(Vector3 origin, Vector3 threatPosition, BotRuntime runtime = null)
        {
            var delta = origin - threatPosition;
            delta.y = 0f;

            if (delta.sqrMagnitude <= 0.01f)
            {
                return FindRoamDestination(origin, runtime);
            }

            var now = Time.realtimeSinceStartup;
            var away = delta.normalized;
            var distance = Math.Max(18f, config.AI.CoverSearchRadius);
            var angles = new[] { 0f, 25f, -25f, 50f, -50f, 85f, -85f, 135f, -135f, 180f };

            foreach (var angle in angles)
            {
                var direction = Quaternion.Euler(0f, angle, 0f) * away;
                var candidate = origin + direction.normalized * distance;

                if (TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(12f, config.Spawn.NavmeshSampleDistance), now, out var sampled))
                {
                    return sampled;
                }
            }

            return FindRoamDestination(origin, runtime);
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

        private float BotMaxHealth(BaseCombatEntity bot, BotRuntime runtime)
        {
            var configured = Mathf.Clamp(runtime?.Skill?.Health ?? BotDefaultAverageHealth, BotMinPlayerLikeHealth, BotMaxPlayerLikeHealth);

            if (bot != null)
            {
                try
                {
                    var entityMax = bot.MaxHealth();
                    var current = bot.Health();

                    if (entityMax > 1f)
                    {
                        var observedCap = Math.Max(entityMax, current);

                        if (observedCap < configured)
                        {
                            return Math.Max(1f, observedCap);
                        }
                    }
                }
                catch
                {
                }
            }

            return Math.Max(1f, configured);
        }

        private string MedicalStatus(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (bot == null || runtime == null)
            {
                return "none";
            }

            if (IsMedicalFireLocked(runtime, now))
            {
                var source = string.IsNullOrWhiteSpace(runtime.LastMedicalUseReason) ? "unknown" : runtime.LastMedicalUseReason;
                return $"{source} lock {Math.Max(0f, runtime.MedicalFireLockedUntil - now).ToString("0.0", CultureInfo.InvariantCulture)}s";
            }

            var maxHealth = BotMaxHealth(bot, runtime);
            var healthFraction = Mathf.Clamp01(bot.Health() / maxHealth);

            if (runtime.LowHealthCoverAwareUntil > now && healthFraction < SyringeHealTargetFraction())
            {
                var nextSyringe = runtime.NextSyringeHealAt > now
                    ? $" next_syringe {Math.Max(0f, runtime.NextSyringeHealAt - now).ToString("0", CultureInfo.InvariantCulture)}s"
                    : "";
                var source = string.IsNullOrWhiteSpace(runtime.LastMedicalUseReason) ? "none" : runtime.LastMedicalUseReason;
                return $"syringe_cover {source}{nextSyringe}";
            }

            if (config.AI.PassiveCombatHealPerSecond > 0f && healthFraction < config.AI.PassiveCombatHealTargetFraction)
            {
                var source = string.IsNullOrWhiteSpace(runtime.LastMedicalUseReason) ? "none" : runtime.LastMedicalUseReason;
                return healthFraction >= config.AI.LowHealthCoverThreshold
                    ? $"non_syringe {source}"
                    : "waiting_syringe_cover";
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

        private void ApplySquadDestinationReservations(BaseCombatEntity bot, BotRuntime runtime, List<TacticalActionCandidate> candidates, float now)
        {
            if (bot == null
                || runtime == null
                || candidates == null
                || config?.AI == null
                || config.AI.SquadDestinationReservationRadius <= 0f)
            {
                return;
            }

            var board = SquadBoardFor(runtime);

            if (board == null || board.TeamSize <= 1)
            {
                runtime.LastFormationReason = "solo";
                return;
            }

            runtime.LastFormationReason = "clear";

            foreach (var candidate in candidates)
            {
                if (!ShouldApplySquadDestinationReservation(candidate))
                {
                    continue;
                }

                if (!IsSquadDestinationReserved(runtime, candidate.Destination, out var blocker))
                {
                    continue;
                }

                if (TryFindSquadFormationOffset(bot, candidate.Destination, runtime, candidate.ActionId, now, out var adjusted))
                {
                    candidate.Destination = adjusted;
                    candidate.RiskFlags.Add("formation_offset");
                    candidate.ReasonFromCode = $"{candidate.ReasonFromCode}; formation offset away from {blocker}";
                    runtime.LastFormationReason = $"{candidate.Id}:offset_from_{blocker}";
                }
                else
                {
                    candidate.RiskFlags.Add("formation_reserved");
                    runtime.LastFormationReason = $"{candidate.Id}:reserved_by_{blocker}";
                }
            }
        }

        private bool ShouldApplySquadDestinationReservation(TacticalActionCandidate candidate)
        {
            if (candidate == null || candidate.Destination == Vector3.zero)
            {
                return false;
            }

            switch (candidate.ActionId)
            {
                case TacticalActionId.RoamToPoint:
                case TacticalActionId.InvestigateSound:
                case TacticalActionId.SearchLastKnown:
                case TacticalActionId.FlankLeft:
                case TacticalActionId.FlankRight:
                case TacticalActionId.PushTarget:
                case TacticalActionId.RegroupWithSquad:
                case TacticalActionId.HoldOutsideBase:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsSquadDestinationReserved(BotRuntime runtime, Vector3 destination, out string blocker)
        {
            blocker = "";

            if (runtime == null || destination == Vector3.zero || config?.AI == null)
            {
                return false;
            }

            var board = SquadBoardFor(runtime);

            if (board == null || board.TeamSize <= 1)
            {
                return false;
            }

            var radius = Math.Max(3f, config.AI.SquadDestinationReservationRadius);

            foreach (var claim in board.DestinationClaims)
            {
                if (string.Equals(claim.Key, runtime.BotKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (claim.Value != Vector3.zero && Distance2D(claim.Value, destination) <= radius)
                {
                    blocker = $"dest:{claim.Key}";
                    return true;
                }
            }

            foreach (var claim in board.CoverClaims)
            {
                if (string.Equals(claim.Key, runtime.BotKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (claim.Value != Vector3.zero && Distance2D(claim.Value, destination) <= radius)
                {
                    blocker = $"cover:{claim.Key}";
                    return true;
                }
            }

            foreach (var entry in activeBots)
            {
                var teammate = entry.Value;

                if (teammate == null
                    || entry.Key == null
                    || !IsLiveBot(entry.Key)
                    || teammate.TeamId != runtime.TeamId
                    || string.Equals(teammate.BotKey, runtime.BotKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Distance2D(entry.Key.transform.position, destination) <= radius * 0.75f)
                {
                    blocker = $"body:{teammate.BotKey}";
                    return true;
                }
            }

            return false;
        }

        private bool TryFindSquadFormationOffset(BaseCombatEntity bot, Vector3 desired, BotRuntime runtime, TacticalActionId actionId, float now, out Vector3 adjusted)
        {
            adjusted = Vector3.zero;

            if (bot == null || runtime == null || desired == Vector3.zero)
            {
                return false;
            }

            var origin = bot.transform.position;
            var forward = desired - KnownThreatPosition(runtime);
            forward.y = 0f;

            if (forward.sqrMagnitude <= 0.01f)
            {
                forward = desired - origin;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude <= 0.01f)
            {
                forward = bot.transform.forward;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude <= 0.01f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var spacing = Math.Max(3f, config.AI.SquadFormationSpacing);
            var side = SquadFormationSide(runtime, actionId);
            var primarySide = Mathf.Abs(side) <= 0.01f ? 1f : side;
            var candidates = new List<Vector3>
            {
                desired + right * primarySide * spacing,
                desired - right * primarySide * spacing,
                desired + right * primarySide * spacing + forward * (spacing * 0.55f),
                desired + right * primarySide * spacing - forward * (spacing * 0.55f),
                desired + forward * spacing,
                desired - forward * spacing
            };

            var attempts = Math.Max(3, config.AI.SquadFormationOffsetAttempts);

            for (var index = candidates.Count; index < attempts; index++)
            {
                var angle = (360f / attempts) * index;
                var direction = Quaternion.Euler(0f, angle, 0f) * forward;
                candidates.Add(desired + direction.normalized * spacing);
            }

            foreach (var candidate in candidates.Take(attempts))
            {
                if (!TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), now, out var sampled))
                {
                    continue;
                }

                if (SegmentCrossesBaseRestrictedArea(origin, sampled))
                {
                    continue;
                }

                if (IsSquadDestinationReserved(runtime, sampled, out _))
                {
                    continue;
                }

                adjusted = sampled;
                return true;
            }

            return false;
        }

        private float SquadFormationSide(BotRuntime runtime, TacticalActionId actionId)
        {
            switch (actionId)
            {
                case TacticalActionId.FlankLeft:
                    return 1f;
                case TacticalActionId.FlankRight:
                    return -1f;
            }

            var role = runtime?.SquadRole ?? "";

            if (role.Equals("flanker", StringComparison.OrdinalIgnoreCase))
            {
                return 1f;
            }

            if (role.Equals("pusher", StringComparison.OrdinalIgnoreCase))
            {
                return -1f;
            }

            return 0f;
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

        private float SkillWeightedValue(BotRuntime runtime, float casualValue, float averageValue, float dangerousValue)
        {
            var tier = runtime?.SkillTier ?? "";

            if (tier.Equals("casual", StringComparison.OrdinalIgnoreCase))
            {
                return casualValue;
            }

            if (tier.Equals("dangerous", StringComparison.OrdinalIgnoreCase))
            {
                return dangerousValue;
            }

            if (tier.Equals("average", StringComparison.OrdinalIgnoreCase))
            {
                return averageValue;
            }

            return averageValue;
        }

        private float SkillDiscipline(BotRuntime runtime)
        {
            var skill = runtime?.Skill ?? new SkillDefinition();
            return Mathf.Clamp01(skill.Courage * 0.7f + (1f - skill.TacticalNoise) * 0.3f);
        }

        private float LongRangeDefensiveDistance(BotRuntime runtime)
        {
            return Mathf.Lerp(config.AI.LongRangeDefensiveMaxDistance, config.AI.LongRangeDefensiveMinDistance, SkillDiscipline(runtime));
        }

        private float ProtectionDistance(BotRuntime runtime)
        {
            return SkillWeightedValue(
                runtime,
                config.AI.ProtectionDistanceCasual,
                config.AI.ProtectionDistanceAverage,
                config.AI.ProtectionDistanceDangerous);
        }

        private float NearbyDefensiveCoverDistance(BotRuntime runtime)
        {
            return ProtectionDistance(runtime);
        }

        private float SyringeHealTargetFraction()
        {
            return Mathf.Clamp(config.AI.SyringeHealTargetFraction, config.AI.LowHealthCoverThreshold, 1f);
        }

        private float LongRangeDefensiveHealthFraction(BotRuntime runtime)
        {
            return SkillWeightedValue(
                runtime,
                config.AI.LongRangeDefensiveHealthFractionCasual,
                config.AI.LongRangeDefensiveHealthFractionAverage,
                config.AI.LongRangeDefensiveHealthFractionDangerous);
        }

        private float FullHealthCoverDisciplineChance(BotRuntime runtime)
        {
            return SkillWeightedChance(
                runtime,
                config.AI.FullHealthCoverDisciplineChanceCasual,
                config.AI.FullHealthCoverDisciplineChanceAverage,
                config.AI.FullHealthCoverDisciplineChanceDangerous);
        }

        private bool IsAverageSkillOrHigher(BotRuntime runtime)
        {
            var tier = runtime?.SkillTier ?? "";
            return tier.Equals("average", StringComparison.OrdinalIgnoreCase)
                || tier.Equals("dangerous", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasProtectionDamageTrigger(BotRuntime runtime, float now)
        {
            return runtime != null && runtime.ProtectionDamageAwareUntil > now;
        }

        private void ResetProtectionDamageTrigger(BotRuntime runtime, string reason, float now)
        {
            if (runtime == null)
            {
                return;
            }

            runtime.ProtectionDamageAccumulatedFraction = 0f;
            runtime.ProtectionDamageWindowStartedAt = now;
            runtime.ProtectionDamageAwareUntil = 0f;
            runtime.LastProtectionReason = string.IsNullOrWhiteSpace(reason) ? "reset" : reason;
        }

        private float BarricadeAnchorLongRangeThreshold(BotRuntime runtime)
        {
            return SkillWeightedValue(
                runtime,
                config.AI.BarricadeAnchorLongRangeThresholdCasual,
                config.AI.BarricadeAnchorLongRangeThresholdAverage,
                config.AI.BarricadeAnchorLongRangeThresholdDangerous);
        }

        private int BarricadeAnchorRequiredHitmarkers(BotRuntime runtime)
        {
            return Clamp((int)SkillWeightedValue(
                runtime,
                config.AI.BarricadeAnchorRequiredHitmarkersCasual,
                config.AI.BarricadeAnchorRequiredHitmarkersAverage,
                config.AI.BarricadeAnchorRequiredHitmarkersDangerous), 1, 12);
        }

        private float BarricadeAnchorNoActionPushSeconds(BotRuntime runtime)
        {
            return SkillWeightedValue(
                runtime,
                config.AI.BarricadeAnchorNoActionPushSecondsCasual,
                config.AI.BarricadeAnchorNoActionPushSecondsAverage,
                config.AI.BarricadeAnchorNoActionPushSecondsDangerous);
        }

        private float LastThreatActionAt(BotRuntime runtime)
        {
            if (runtime == null)
            {
                return 0f;
            }

            return Math.Max(runtime.LastDamageTakenAt, runtime.Memory.LastHeardAt);
        }

        private void StartBarricadeAnchorIfNeeded(BotRuntime runtime, float threatDistance, ulong targetUserId, float now)
        {
            if (runtime == null || runtime.CurrentBarricadePoint == Vector3.zero)
            {
                return;
            }

            var threshold = BarricadeAnchorLongRangeThreshold(runtime);

            if (threatDistance < threshold)
            {
                ClearBarricadeAnchor(runtime, $"range {threatDistance:0}m<{threshold:0}m");
                return;
            }

            runtime.BarricadeAnchorActive = true;
            runtime.BarricadeAnchorStartedAt = now;
            runtime.BarricadeAnchorThreatDistance = threatDistance;
            runtime.BarricadeAnchorHitmarkers = 0;
            runtime.BarricadeAnchorRequiredHitmarkers = BarricadeAnchorRequiredHitmarkers(runtime);
            runtime.BarricadeAnchorTargetUserId = targetUserId;
            runtime.BarricadeAnchorTargetDeadAt = 0f;
            runtime.BarricadeAnchorNoActionPushAt = Math.Max(now, LastThreatActionAt(runtime)) + BarricadeAnchorNoActionPushSeconds(runtime);
            runtime.LastBarricadeAnchorReason = $"anchored {threatDistance:0}m need_hits={runtime.BarricadeAnchorRequiredHitmarkers}";
        }

        private void ClearBarricadeAnchor(BotRuntime runtime, string reason)
        {
            if (runtime == null)
            {
                return;
            }

            runtime.BarricadeAnchorActive = false;
            runtime.BarricadeAnchorTargetUserId = 0;
            runtime.BarricadeAnchorHitmarkers = 0;
            runtime.BarricadeAnchorRequiredHitmarkers = 0;
            runtime.BarricadeAnchorNoActionPushAt = 0f;
            runtime.BarricadeAnchorTargetDeadAt = 0f;
            runtime.LastBarricadeAnchorReason = string.IsNullOrWhiteSpace(reason) ? "clear" : reason;
        }

        private bool IsBarricadeAnchorActive(BotRuntime runtime, float now)
        {
            return runtime != null
                && runtime.BarricadeAnchorActive
                && runtime.CurrentBarricadePoint != Vector3.zero
                && runtime.CurrentTuckPoint != Vector3.zero;
        }

        private bool CanLeaveBarricadeAnchor(BotRuntime runtime, float now)
        {
            if (!IsBarricadeAnchorActive(runtime, now))
            {
                return true;
            }

            if (runtime.BarricadeAnchorTargetDeadAt > 0f)
            {
                runtime.LastBarricadeAnchorReason = "push target_dead";
                return true;
            }

            if (runtime.BarricadeAnchorHitmarkers >= Math.Max(1, runtime.BarricadeAnchorRequiredHitmarkers))
            {
                runtime.LastBarricadeAnchorReason = $"push hits={runtime.BarricadeAnchorHitmarkers}/{runtime.BarricadeAnchorRequiredHitmarkers}";
                return true;
            }

            if (runtime.BarricadeAnchorNoActionPushAt > 0f && now >= runtime.BarricadeAnchorNoActionPushAt)
            {
                runtime.LastBarricadeAnchorReason = "push no_action";
                return true;
            }

            runtime.LastBarricadeAnchorReason = $"hold hits={runtime.BarricadeAnchorHitmarkers}/{Math.Max(1, runtime.BarricadeAnchorRequiredHitmarkers)} no_action={Math.Max(0f, runtime.BarricadeAnchorNoActionPushAt - now):0}s";
            return false;
        }

        private void RememberBarricadeAnchorHitmarker(BotRuntime runtime, ulong targetUserId, float now)
        {
            if (!IsBarricadeAnchorActive(runtime, now) || targetUserId == 0)
            {
                return;
            }

            if (runtime.BarricadeAnchorTargetUserId != 0 && runtime.BarricadeAnchorTargetUserId != targetUserId)
            {
                return;
            }

            runtime.BarricadeAnchorHitmarkers++;
            runtime.LastBarricadeAnchorReason = $"hit {runtime.BarricadeAnchorHitmarkers}/{Math.Max(1, runtime.BarricadeAnchorRequiredHitmarkers)}";
        }

        private void MarkBarricadeAnchorTargetDeath(ulong targetUserId, float now)
        {
            if (targetUserId == 0)
            {
                return;
            }

            foreach (var runtime in activeBots.Values)
            {
                if (runtime == null
                    || !runtime.BarricadeAnchorActive
                    || runtime.BarricadeAnchorTargetUserId != targetUserId)
                {
                    continue;
                }

                runtime.BarricadeAnchorTargetDeadAt = now;
                runtime.LastBarricadeAnchorReason = "target_dead";
            }
        }

        private string BarricadeAnchorStatus(BotRuntime runtime, float now)
        {
            if (runtime == null || !runtime.BarricadeAnchorActive)
            {
                return string.IsNullOrWhiteSpace(runtime?.LastBarricadeAnchorReason) ? "none" : runtime.LastBarricadeAnchorReason;
            }

            var noAction = runtime.BarricadeAnchorNoActionPushAt > now
                ? $"{Math.Max(0f, runtime.BarricadeAnchorNoActionPushAt - now):0}s"
                : "ready";
            return $"{(CanLeaveBarricadeAnchor(runtime, now) ? "ready" : "hold")} {runtime.BarricadeAnchorHitmarkers}/{Math.Max(1, runtime.BarricadeAnchorRequiredHitmarkers)} no_action={noAction}";
        }

        private void ApplyBarricadeAnchorCandidateFilter(BaseCombatEntity bot, BotRuntime runtime, List<TacticalActionCandidate> candidates, float now)
        {
            if (bot == null
                || runtime == null
                || candidates == null
                || !config.AI.PreventMovingInFrontOfAnchoredBarricade
                || !IsBarricadeAnchorActive(runtime, now)
                || CanLeaveBarricadeAnchor(runtime, now))
            {
                return;
            }

            var holdPoint = runtime.CurrentTuckPoint == Vector3.zero ? runtime.CurrentCover : runtime.CurrentTuckPoint;

            if (holdPoint != Vector3.zero)
            {
                var hold = Candidate(TacticalActionId.Tuck, 154f + SkillDiscipline(runtime) * 12f, "low", "long-range barricade anchor; stay behind wall until confidence unlocks push", holdPoint, runtime.Memory.TargetUserId, now);
                hold.RiskFlags.Add("barricade_anchor");
                candidates.Add(hold);
            }

            candidates.RemoveAll(candidate => !IsBarricadeAnchorAllowedCandidate(runtime, candidate, now));
        }

        private bool IsBarricadeAnchorAllowedCandidate(BotRuntime runtime, TacticalActionCandidate candidate, float now)
        {
            if (runtime == null || candidate == null)
            {
                return false;
            }

            switch (candidate.ActionId)
            {
                case TacticalActionId.AcquireVisibleTarget:
                case TacticalActionId.PeekLeft:
                case TacticalActionId.PeekRight:
                case TacticalActionId.Tuck:
                    candidate.RiskFlags.Add("barricade_anchor");
                    return true;

                case TacticalActionId.MoveToCover:
                case TacticalActionId.RetreatToCover:
                    if (IsBehindAnchoredBarricade(runtime, candidate.Destination))
                    {
                        candidate.RiskFlags.Add("barricade_anchor");
                        return true;
                    }

                    runtime.LastBarricadeAnchorReason = "blocked_front_move";
                    return false;

                case TacticalActionId.PlaceBarricade:
                    return true;

                default:
                    runtime.LastBarricadeAnchorReason = "holding_wall";
                    return false;
            }
        }

        private bool IsBehindAnchoredBarricade(BotRuntime runtime, Vector3 destination)
        {
            if (runtime == null || destination == Vector3.zero || runtime.CurrentBarricadePoint == Vector3.zero)
            {
                return false;
            }

            var threat = KnownThreatPosition(runtime);

            if (threat == Vector3.zero)
            {
                return Distance2D(destination, runtime.CurrentBarricadePoint) <= 8.5f;
            }

            var awayFromThreat = runtime.CurrentBarricadePoint - threat;
            awayFromThreat.y = 0f;

            if (awayFromThreat.sqrMagnitude <= 0.01f)
            {
                return Distance2D(destination, runtime.CurrentBarricadePoint) <= 8.5f;
            }

            var toDestination = destination - runtime.CurrentBarricadePoint;
            toDestination.y = 0f;

            if (toDestination.sqrMagnitude <= 0.01f)
            {
                return true;
            }

            awayFromThreat.Normalize();
            toDestination.Normalize();
            return Vector3.Dot(awayFromThreat, toDestination) >= -0.15f
                && Distance2D(destination, runtime.CurrentBarricadePoint) <= 8.5f;
        }

        private bool HasDamageWallAwareness(BotRuntime runtime, float now)
        {
            if (runtime == null)
            {
                return false;
            }

            if (HasProtectionDamageTrigger(runtime, now))
            {
                ExtendDamageBarricadeAwarenessThroughCooldown(runtime, now);
                return true;
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

        private bool ShouldPreferLongRangeDefensiveHeal(BotRuntime runtime, float healthFraction, float threatDistance, bool hasFreshContact, float now)
        {
            if (runtime == null || !hasFreshContact || threatDistance <= 0f)
            {
                return false;
            }

            var healTarget = SyringeHealTargetFraction();

            if (runtime.LowHealthCoverAwareUntil > now && healthFraction < healTarget)
            {
                return true;
            }

            if (healthFraction >= healTarget || threatDistance < LongRangeDefensiveDistance(runtime))
            {
                return false;
            }

            var recentlyDamaged = runtime.LastDamageTakenAt > 0f
                && now - runtime.LastDamageTakenAt <= config.AI.LongRangeLosingFightMemorySeconds;
            var recentlyDealtDamage = runtime.LastDamageDealtAt > 0f
                && now - runtime.LastDamageDealtAt <= config.AI.LongRangeLosingFightMemorySeconds;
            var losingExchange = recentlyDamaged && (!recentlyDealtDamage || runtime.LastDamageTakenAt >= runtime.LastDamageDealtAt - 0.75f);
            var belowSkillDefensiveHealth = healthFraction <= LongRangeDefensiveHealthFraction(runtime);

            if (!losingExchange && !belowSkillDefensiveHealth)
            {
                return false;
            }

            if (now < runtime.NextLowHealthAwarenessCheckAt)
            {
                return false;
            }

            runtime.NextLowHealthAwarenessCheckAt = now + config.AI.LowHealthCoverRecheckSeconds;
            var chance = FullHealthCoverDisciplineChance(runtime);

            if (losingExchange)
            {
                chance = Mathf.Clamp01(chance + 0.15f);
            }

            if (healthFraction <= config.AI.LowHealthCoverThreshold)
            {
                chance = Math.Max(chance, SkillWeightedChance(runtime, config.AI.LowHealthCoverNoticeChanceCasual, config.AI.LowHealthCoverNoticeChanceAverage, config.AI.LowHealthCoverNoticeChanceDangerous));
            }

            if (UnityEngine.Random.value > chance)
            {
                return false;
            }

            runtime.LowHealthCoverAwareUntil = now + config.AI.LowHealthCoverCommitmentSeconds;
            runtime.LastLowHealthHealAt = 0f;
            return true;
        }

        private bool ShouldNoticeLowHealth(BotRuntime runtime, float healthFraction, float now)
        {
            if (runtime == null)
            {
                return false;
            }

            var healTarget = SyringeHealTargetFraction();

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

            ApplySyringeCoverHeal(bot, runtime, now);
            ApplyPassiveCombatHeal(bot, runtime, now);
        }

        private void ApplyPassiveCombatHeal(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (config.AI.PassiveCombatHealPerSecond <= 0f || IsMedicalFireLocked(runtime, now))
            {
                runtime.LastPassiveHealAt = 0f;
                return;
            }

            var maxHealth = BotMaxHealth(bot, runtime);
            var targetHealth = maxHealth * config.AI.PassiveCombatHealTargetFraction;
            var currentHealth = bot.Health();
            var healthFraction = Mathf.Clamp01(currentHealth / maxHealth);

            if (healthFraction < config.AI.LowHealthCoverThreshold || currentHealth >= targetHealth)
            {
                runtime.LastPassiveHealAt = 0f;
                runtime.PendingNonSyringeHealRemaining = 0f;
                return;
            }

            if (runtime.PendingNonSyringeHealRemaining <= 0f && now >= runtime.NextNonSyringeHealAt)
            {
                if (TryConsumeBotMedicalItem(bot, false, out var medicalItemShortname, out var medicalHealAmount, out var medicalReason))
                {
                    runtime.PendingNonSyringeHealRemaining = medicalHealAmount;
                    runtime.LastMedicalUseReason = $"non_syringe:{medicalItemShortname}";
                }
                else
                {
                    runtime.LastMedicalUseReason = $"non_syringe:fallback:{medicalReason}";
                }

                runtime.NextNonSyringeHealAt = now + config.AI.NonSyringeHealCooldownSeconds;
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
                var healAmount = config.AI.PassiveCombatHealPerSecond * elapsed;

                if (runtime.PendingNonSyringeHealRemaining > 0f)
                {
                    var realMedicalRate = Math.Max(config.AI.PassiveCombatHealPerSecond, config.AI.NonSyringeHealAmount / Math.Max(0.1f, config.AI.NonSyringeHealCooldownSeconds));
                    healAmount = Math.Min(runtime.PendingNonSyringeHealRemaining, realMedicalRate * elapsed);
                    runtime.PendingNonSyringeHealRemaining = Math.Max(0f, runtime.PendingNonSyringeHealRemaining - healAmount);
                }

                bot.SetHealth(Math.Min(targetHealth, currentHealth + healAmount));
            }
        }

        private void ApplySyringeCoverHeal(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            var maxHealth = BotMaxHealth(bot, runtime);
            var targetHealth = maxHealth * SyringeHealTargetFraction();
            var currentHealth = bot.Health();

            if (config.AI.LowHealthCoverHealPerSecond <= 0f || runtime.LowHealthCoverAwareUntil <= now)
            {
                runtime.LastLowHealthHealAt = 0f;
                runtime.PendingMedicalHealRemaining = 0f;
                return;
            }

            if (!IsMedicalFireLocked(runtime, now) && runtime.PendingMedicalHealRemaining > 0f)
            {
                runtime.PendingMedicalHealRemaining = 0f;
            }

            if (currentHealth >= targetHealth)
            {
                runtime.LowHealthCoverAwareUntil = 0f;
                runtime.LastLowHealthHealAt = 0f;
                runtime.MedicalFireLockedUntil = Math.Min(runtime.MedicalFireLockedUntil, now);
                runtime.PendingMedicalHealRemaining = 0f;
                runtime.PendingNonSyringeHealRemaining = 0f;
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
                runtime.PendingMedicalHealRemaining = 0f;
                runtime.PendingNonSyringeHealRemaining = 0f;
                if (TryConsumeBotMedicalItem(bot, true, out var medicalItemShortname, out var medicalHealAmount, out var medicalReason))
                {
                    runtime.PendingMedicalHealRemaining = medicalHealAmount;
                    runtime.LastMedicalUseReason = $"syringe:{medicalItemShortname}";
                }
                else
                {
                    runtime.LastMedicalUseReason = $"syringe:fallback:{medicalReason}";
                }

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

            var healAmount = config.AI.LowHealthCoverHealPerSecond * elapsed;

            if (runtime.PendingMedicalHealRemaining > 0f)
            {
                var realMedicalRate = Math.Max(config.AI.LowHealthCoverHealPerSecond, config.AI.RealMedicalItemHealAmount / Math.Max(0.1f, config.AI.SyringeFireLockSeconds));
                healAmount = Math.Min(runtime.PendingMedicalHealRemaining, realMedicalRate * elapsed);
                runtime.PendingMedicalHealRemaining = Math.Max(0f, runtime.PendingMedicalHealRemaining - healAmount);
            }
            else if ((runtime.LastMedicalUseReason ?? "").StartsWith("syringe:", StringComparison.OrdinalIgnoreCase)
                && !(runtime.LastMedicalUseReason ?? "").StartsWith("syringe:fallback", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bot.SetHealth(Math.Min(targetHealth, currentHealth + healAmount));
        }

        private bool TryConsumeBotMedicalItem(BaseCombatEntity bot, bool requireSyringe, out string itemShortname, out float healAmount, out string reason)
        {
            itemShortname = "";
            healAmount = 0f;
            reason = "disabled";

            if (config?.AI?.UseRealMedicalItemsForCoverHeal != true)
            {
                return false;
            }

            var player = bot as BasePlayer;

            if (player?.inventory == null)
            {
                reason = "no_inventory";
                return false;
            }

            var allowed = new HashSet<string>(config.AI.RealMedicalItemShortnames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in config.AI.BotMedicalLoadout ?? new Dictionary<string, int>())
            {
                if (!string.IsNullOrWhiteSpace(entry.Key))
                {
                    allowed.Add(entry.Key.Trim());
                }
            }

            if (!string.IsNullOrWhiteSpace(config.AI.BotMedicalItemShortname))
            {
                allowed.Add(config.AI.BotMedicalItemShortname.Trim());
            }

            if (allowed.Count == 0)
            {
                reason = "no_medical_shortnames";
                return false;
            }

            foreach (var container in new[] { player.inventory.containerBelt, player.inventory.containerMain })
            {
                if (container?.itemList == null)
                {
                    continue;
                }

                foreach (var item in container.itemList.ToList())
                {
                    var shortname = item?.info?.shortname ?? "";

                    if (item == null || item.amount <= 0 || !allowed.Contains(shortname) || IsSyringeMedicalItem(shortname) != requireSyringe)
                    {
                        continue;
                    }

                    itemShortname = shortname;
                    healAmount = requireSyringe ? config.AI.RealMedicalItemHealAmount : config.AI.NonSyringeHealAmount;
                    item.UseItem(1);
                    reason = "used";
                    return true;
                }
            }

            reason = "no_item";
            return false;
        }

        private bool IsSyringeMedicalItem(string shortname)
        {
            shortname = (shortname ?? "").Trim();
            return shortname.IndexOf("syringe", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private bool TryFindFlankPosition(Vector3 origin, Vector3 threatPosition, float sideSign, BotRuntime runtime, float now, out Vector3 flankPoint)
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
                if (!TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), now, out var sampled))
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
            var lowHealthAware = runtime.LowHealthCoverAwareUntil > now && healthFraction < SyringeHealTargetFraction();
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
                    if (!TryProjectToLandSurface(ref candidate, 0.05f))
                    {
                        continue;
                    }

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
                DebugLog($"barricade:{runtime.BotKey}", $"{runtime.DisplayName} placed barricade at {FormatVector(position)} ({botPlacedEntities.Count}/{config.AI.MaxActiveBotBarricades}).");
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
                    DebugWarning("barricade-recycle", $"Could not recycle oldest roam bot barricade: {ex.GetType().Name}: {ex.Message}");
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
                    if (!TryProjectToLandSurface(ref candidate))
                    {
                        continue;
                    }

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

        private bool TryFindNearbyProtectionPlan(BaseCombatEntity bot, BotRuntime runtime, Vector3 threatPosition, BasePlayer target, float maxDistance, float now, out ProtectionPlan plan)
        {
            plan = null;

            if (bot == null || runtime == null || threatPosition == Vector3.zero || maxDistance <= 0f)
            {
                return false;
            }

            var candidates = new List<ProtectionPlan>();

            if (TryFindNearestExistingBarricadeProtection(bot, runtime, threatPosition, target, maxDistance, now, out var barricadePlan))
            {
                candidates.Add(barricadePlan);
            }

            if (TryFindNearbyCoverProtection(bot, runtime, threatPosition, target, maxDistance, now, out var coverPlan))
            {
                candidates.Add(coverPlan);
            }

            plan = candidates
                .Where(candidate => candidate != null && candidate.TuckPoint != Vector3.zero)
                .OrderBy(candidate => candidate.Distance)
                .FirstOrDefault();

            return plan != null;
        }

        private bool TryFindNearestExistingBarricadeProtection(BaseCombatEntity bot, BotRuntime runtime, Vector3 threatPosition, BasePlayer target, float maxDistance, float now, out ProtectionPlan plan)
        {
            plan = null;
            CleanupBotPlacedEntityRefs();

            foreach (var barricade in botPlacedEntities)
            {
                if (barricade == null || barricade.IsDestroyed)
                {
                    continue;
                }

                var barricadePosition = barricade.transform.position;

                if (!TryFindBarricadeHoldPoint(bot.transform.position, barricadePosition, threatPosition, out var holdPoint))
                {
                    continue;
                }

                var distance = Distance2D(bot.transform.position, holdPoint);

                if (distance > maxDistance)
                {
                    continue;
                }

                var peek = BarricadePeekPoint(bot, holdPoint, barricadePosition, threatPosition, target, runtime, now);

                if (plan != null && distance >= plan.Distance)
                {
                    continue;
                }

                plan = new ProtectionPlan
                {
                    CoverPoint = holdPoint,
                    TuckPoint = holdPoint,
                    PeekPoint = peek,
                    Source = "existing_barricade",
                    Distance = distance
                };
            }

            return plan != null;
        }

        private bool TryFindNearbyCoverProtection(BaseCombatEntity bot, BotRuntime runtime, Vector3 threatPosition, BasePlayer target, float maxDistance, float now, out ProtectionPlan plan)
        {
            plan = null;

            if (bot == null || runtime == null || maxDistance <= 0f)
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
            var attempts = Math.Max(8, config.AI.CoverPointAttempts);
            var minRadius = Mathf.Clamp(maxDistance * 0.45f, 1.25f, maxDistance);
            var bestScore = float.MinValue;

            for (var index = 0; index < attempts; index++)
            {
                var shell = index / 8;
                var slot = index % 8;
                var shellT = attempts <= 8 ? 0f : Mathf.Clamp01(shell / Mathf.Max(1f, (attempts / 8f) - 1f));
                var radius = Mathf.Lerp(minRadius, maxDistance, shellT);
                var angleOffset = ((slot - 3.5f) * 22.5f) + shell * 11.25f;
                var direction = Quaternion.Euler(0f, angleOffset, 0f) * away;
                var candidate = origin + direction.normalized * radius;

                if (!TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(4f, config.Spawn.NavmeshSampleDistance), now, out var sampled))
                {
                    continue;
                }

                var distanceFromBot = Distance2D(origin, sampled);

                if (distanceFromBot > maxDistance + 0.35f)
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

                var left = Vector3.zero;
                var right = Vector3.zero;
                var score = 100f - distanceFromBot * 8f;

                if (TryBuildPeekPoint(bot, sampled, threatPosition, target, 1f, runtime, now, out var leftPeek))
                {
                    left = leftPeek;
                    score += 8f;
                }

                if (TryBuildPeekPoint(bot, sampled, threatPosition, target, -1f, runtime, now, out var rightPeek))
                {
                    right = rightPeek;
                    score += 8f;
                }

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                plan = new ProtectionPlan
                {
                    CoverPoint = sampled,
                    TuckPoint = sampled,
                    PeekPoint = left != Vector3.zero ? left : right,
                    Source = "natural_cover",
                    Distance = distanceFromBot
                };
            }

            return plan != null;
        }

        private void ApplyProtectionPlan(BotRuntime runtime, ProtectionPlan plan, float now)
        {
            if (runtime == null || plan == null)
            {
                return;
            }

            runtime.CurrentCover = plan.CoverPoint;
            runtime.CurrentTuckPoint = plan.TuckPoint == Vector3.zero ? plan.CoverPoint : plan.TuckPoint;
            runtime.CurrentPeekPoint = plan.PeekPoint;
            runtime.NextCoverSearchAt = now + config.AI.CoverRepositionCooldownSeconds;
            runtime.LastProtectionReason = $"{plan.Source} {plan.Distance:0.0}m";
        }

        private Vector3 BarricadePeekPoint(BaseCombatEntity bot, Vector3 holdPoint, Vector3 barricadePosition, Vector3 threatPosition, BasePlayer target, BotRuntime runtime = null, float now = 0f)
        {
            if (holdPoint == Vector3.zero || threatPosition == Vector3.zero)
            {
                return Vector3.zero;
            }

            if (now <= 0f)
            {
                now = Time.realtimeSinceStartup;
            }

            var firstSide = UnityEngine.Random.value < 0.5f ? 1f : -1f;

            if (TryBuildPeekPoint(bot, holdPoint, threatPosition, target, firstSide, runtime, now, out var firstPeek))
            {
                return firstPeek;
            }

            if (TryBuildPeekPoint(bot, holdPoint, threatPosition, target, -firstSide, runtime, now, out var secondPeek))
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
            var fallback = holdPoint + side * config.AI.PeekOffsetDistance + toThreat * 0.65f;

            return TrySampleTacticalPositionAvoidingStuck(runtime, fallback, Math.Max(6f, config.Spawn.NavmeshSampleDistance), now, out var sampled)
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

                if (!TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), Time.realtimeSinceStartup, out var sampled))
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

                if (TryBuildPeekPoint(bot, sampled, threatPosition, target, 1f, runtime, Time.realtimeSinceStartup, out var leftPeek))
                {
                    left = leftPeek;
                    score += 8f;
                }

                if (TryBuildPeekPoint(bot, sampled, threatPosition, target, -1f, runtime, Time.realtimeSinceStartup, out var rightPeek))
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
                DebugLog($"cover-score:{runtime.BotKey}", $"{runtime.DisplayName} cover score={plan.Score:0.0} cover={FormatVector(plan.CoverPoint)} peek={FormatVector(plan.PeekLeftPoint != Vector3.zero ? plan.PeekLeftPoint : plan.PeekRightPoint)}");
            }

            return true;
        }

        private bool TryBuildPeekPoint(BaseCombatEntity bot, Vector3 coverPoint, Vector3 threatPosition, BasePlayer target, float sideSign, BotRuntime runtime, float now, out Vector3 peekPoint)
        {
            peekPoint = Vector3.zero;
            if (now <= 0f)
            {
                now = Time.realtimeSinceStartup;
            }

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

            if (!TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(6f, config.Spawn.NavmeshSampleDistance), now, out var sampled))
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

                    if (!TrySampleTacticalPositionAvoidingStuck(runtime, candidate, Math.Max(8f, config.Spawn.NavmeshSampleDistance), Time.realtimeSinceStartup, out var sampled))
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

            return FindRoamDestination(origin, runtime);
        }

        private bool TrySampleTacticalPosition(Vector3 candidate, float sampleDistance, out Vector3 sampled)
        {
            sampled = Vector3.zero;
            if (!TryProjectToLandSurface(ref candidate))
            {
                return false;
            }

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

        private bool TrySampleTacticalPositionAvoidingStuck(BotRuntime runtime, Vector3 candidate, float sampleDistance, float now, out Vector3 sampled)
        {
            if (!TrySampleTacticalPosition(candidate, sampleDistance, out sampled))
            {
                return false;
            }

            return !ShouldAvoidDestination(runtime, sampled, now);
        }

        private int ActiveStuckMemoryCount(BotRuntime runtime, float now)
        {
            CleanupStuckDestinationMemory(runtime, now);
            return runtime?.Movement?.AvoidedDestinations?.Count ?? 0;
        }

        private void RememberBadDestination(BotRuntime runtime, Vector3 destination, string reason, float now)
        {
            if (runtime?.Movement == null
                || destination == Vector3.zero
                || config?.AI == null
                || config.AI.MaxStuckMemoryPoints <= 0
                || config.AI.StuckMemorySeconds <= 0f)
            {
                return;
            }

            if (runtime.Movement.AvoidedDestinations == null)
            {
                runtime.Movement.AvoidedDestinations = new List<StuckDestinationMemory>();
            }

            CleanupStuckDestinationMemory(runtime, now);

            var radius = Math.Max(3f, config.AI.StuckMemoryRadius);
            var existing = runtime.Movement.AvoidedDestinations
                .FirstOrDefault(entry => entry != null && Distance2D(entry.Position, destination) <= radius);

            if (existing == null)
            {
                runtime.Movement.AvoidedDestinations.Add(new StuckDestinationMemory
                {
                    Position = destination,
                    RecordedAt = now,
                    Failures = 1,
                    Reason = reason ?? "path_failed"
                });
            }
            else
            {
                existing.Position = destination;
                existing.RecordedAt = now;
                existing.Failures++;
                existing.Reason = reason ?? existing.Reason;
            }

            runtime.LastStuckMemoryReason = string.IsNullOrWhiteSpace(reason) ? "path_failed" : reason;

            while (runtime.Movement.AvoidedDestinations.Count > config.AI.MaxStuckMemoryPoints)
            {
                var oldest = runtime.Movement.AvoidedDestinations
                    .OrderBy(entry => entry?.RecordedAt ?? float.MaxValue)
                    .FirstOrDefault();

                if (oldest == null || !runtime.Movement.AvoidedDestinations.Remove(oldest))
                {
                    break;
                }
            }
        }

        private void CleanupStuckDestinationMemory(BotRuntime runtime, float now)
        {
            if (runtime?.Movement?.AvoidedDestinations == null)
            {
                return;
            }

            var maxAge = Math.Max(5f, config?.AI?.StuckMemorySeconds ?? 75f);
            runtime.Movement.AvoidedDestinations.RemoveAll(entry => entry == null
                || entry.Position == Vector3.zero
                || now - entry.RecordedAt > maxAge);

            if (runtime.Movement.AvoidedDestinations.Count == 0)
            {
                runtime.LastStuckMemoryReason = "none";
            }
        }

        private bool ShouldAvoidDestination(BotRuntime runtime, Vector3 destination, float now)
        {
            if (runtime?.Movement?.AvoidedDestinations == null
                || destination == Vector3.zero
                || config?.AI == null
                || config.AI.MaxStuckMemoryPoints <= 0)
            {
                return false;
            }

            CleanupStuckDestinationMemory(runtime, now);
            var radius = Math.Max(3f, config.AI.StuckMemoryRadius);
            return runtime.Movement.AvoidedDestinations.Any(entry => entry != null
                && Distance2D(entry.Position, destination) <= radius);
        }

        private bool IsMovementDestinationAction(TacticalActionId actionId)
        {
            switch (actionId)
            {
                case TacticalActionId.RoamToPoint:
                case TacticalActionId.InvestigateSound:
                case TacticalActionId.SearchLastKnown:
                case TacticalActionId.MoveToCover:
                case TacticalActionId.PeekLeft:
                case TacticalActionId.PeekRight:
                case TacticalActionId.WideSwing:
                case TacticalActionId.Tuck:
                case TacticalActionId.FlankLeft:
                case TacticalActionId.FlankRight:
                case TacticalActionId.PushTarget:
                case TacticalActionId.RetreatToCover:
                case TacticalActionId.RegroupWithSquad:
                case TacticalActionId.HoldOutsideBase:
                    return true;
                default:
                    return false;
            }
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

            NormalizeBotActiveWeaponDamage(bot, runtime);
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

            if (!config.AI.AllowShootingWhileNonSyringeHealing && IsNonSyringeHealing(runtime, now))
            {
                return BlockFire(runtime, "non_syringe_lock");
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
            NormalizeBotWeaponDamage(projectile, weaponShortname, RuntimeFor(bot));
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

        private bool IsNonSyringeHealing(BotRuntime runtime, float now)
        {
            return runtime != null && runtime.PendingNonSyringeHealRemaining > 0f;
        }

        private int NearbyAllies(BaseCombatEntity bot, BotRuntime runtime)
        {
            return activeBots.Count(entry => entry.Key != bot && IsLiveBot(entry.Key) && SameBotClan(runtime, entry.Value) && Vector3.Distance(bot.transform.position, entry.Key.transform.position) <= 45f);
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
                RememberBadDestination(runtime, runtime.CurrentDestination, "stuck_no_progress", now);
            }
            else if (now - runtime.Movement.LastStuckNotedAt > config.AI.StuckDetectionSeconds)
            {
                runtime.Movement.LastStuckNotedAt = now;
                runtime.ConsecutiveFailedPaths++;
                runtime.Movement.SameActionFailures++;
                RememberBadDestination(runtime, runtime.CurrentDestination, "repeat_stuck_no_progress", now);
            }

            return true;
        }

        private bool ShouldDespawnHardStuck(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (bot == null || runtime == null)
            {
                return false;
            }

            if (!runtime.Movement.IsStuck || runtime.Movement.StuckSince <= 0f)
            {
                return false;
            }

            var stuckSeconds = now - runtime.Movement.StuckSince;

            if (config.AI.HardStuckDespawnSeconds > 0f && stuckSeconds >= config.AI.HardStuckDespawnSeconds)
            {
                return true;
            }

            if (config.AI.HardStuckFailedPathsToDespawn <= 0 || runtime.ConsecutiveFailedPaths < config.AI.HardStuckFailedPathsToDespawn)
            {
                return false;
            }

            return stuckSeconds >= Math.Max(10f, config.AI.StuckDetectionSeconds * 3f);
        }

        private BasePlayer NearestRealPlayer(Vector3 position)
        {
            return BasePlayer.activePlayerList
                .Where(player => IsRealPlayer(player) && player.IsConnected && !player.IsDead() && !player.IsSleeping() && !ShouldIgnoreSafeZonePlayer(player))
                .OrderBy(player => Vector3.Distance(position, player.transform.position))
                .FirstOrDefault();
        }

        private float DistanceToNearestRealPlayer(Vector3 position)
        {
            var player = NearestRealPlayer(position);
            return player == null ? -1f : Vector3.Distance(position, player.transform.position);
        }

        private string PlayerEngagementSignal(BaseCombatEntity bot, BotRuntime runtime, float now)
        {
            if (bot == null || runtime?.Memory == null)
            {
                return "";
            }

            var memory = runtime.Memory;
            var window = Math.Max(1f, config?.DecisionAdvisor?.PlayerEngagementMemorySeconds ?? 45f);
            var target = ActiveRealPlayerForEngagement(memory.TargetUserId);

            if (target != null)
            {
                if (memory.HasLineOfSight
                    && (memory.Target == target || CombatTargetId(memory.Target) == CombatTargetId(target) || memory.TargetUserId == CombatTargetId(target)))
                {
                    return "visible_player";
                }

                if (memory.LastSeenAt > 0f && now - memory.LastSeenAt <= window)
                {
                    return "recent_seen_player";
                }

                var hearingWindow = Math.Min(window, Math.Max(1f, config.AI.SoundInvestigationCommitmentSeconds));

                if (memory.LastHeardAt > 0f && now - memory.LastHeardAt <= hearingWindow)
                {
                    return "recent_heard_player";
                }

                if (runtime.LastDamageDealtAt > 0f && now - runtime.LastDamageDealtAt <= window)
                {
                    return "recent_damage_to_player";
                }
            }

            if (IsEngageableRealPlayer(memory.LastDamageSourcePlayer)
                && memory.LastDamagedAt > 0f
                && now - memory.LastDamagedAt <= window)
            {
                return "recent_damage_from_player";
            }

            return "";
        }

        private BasePlayer ActiveRealPlayerForEngagement(ulong targetId)
        {
            if (targetId == 0UL)
            {
                return null;
            }

            var player = FindCombatTargetById(targetId);
            return IsEngageableRealPlayer(player) ? player : null;
        }

        private bool IsEngageableRealPlayer(BasePlayer player)
        {
            return player != null
                && IsRealPlayer(player)
                && player.IsConnected
                && !player.IsDead()
                && !player.IsSleeping()
                && !ShouldIgnoreSafeZonePlayer(player);
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
                    DebugWarning($"reflection:{target.GetType().Name}.{methodName}", $"Reflection call {target.GetType().Name}.{methodName} failed: {ex.GetType().Name}: {ex.Message}");
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
            var now = Time.realtimeSinceStartup;
            return $"type={runtime.EntityType}, state={runtime.State}, clan={runtime.ClanTag}, role={runtime.SquadRole}, los={runtime.Memory.HasLineOfSight}, exposure={runtime.Memory.TargetExposureFraction:0.00}({runtime.Memory.TargetVisibleProbePoints}/{runtime.Memory.TargetTotalProbePoints}), weapon={runtime.Combat.WeaponClass}:{runtime.Combat.WeaponShortname}, aim={AimStatus(runtime, now)}, learn={LearningRuntimeStatus(runtime)}, cover={FormatVectorSafe(runtime.CurrentCover)}, flank={FormatVectorSafe(runtime.CurrentFlankPoint)}, base={runtime.IsInBaseRestrictedArea}, barricades={botPlacedEntities.Count}/{config.AI.MaxActiveBotBarricades}, protect={runtime.LastProtectionReason}, anchor={BarricadeAnchorStatus(runtime, now)}, utility={runtime.LastUtilityReason}, heal={MedicalStatus(bot, runtime, now)}, formation={runtime.LastFormationReason}, stuck={runtime.Movement.IsStuck}, badspots={ActiveStuckMemoryCount(runtime, now)}, nav={BotNavStatus(bot)}, target={BotTargetStatus(bot, runtime)}, prefab={ShortPrefab(runtime.Prefab)}";
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

        private BasePlayer ResolveKillerPlayer(BaseCombatEntity victim, HitInfo info)
        {
            return info?.InitiatorPlayer
                ?? info?.Initiator as BasePlayer
                ?? victim?.lastAttacker as BasePlayer;
        }

        private BaseCombatEntity ResolveKillerEntity(BaseCombatEntity victim, HitInfo info, BasePlayer killerPlayer)
        {
            return info?.Initiator as BaseCombatEntity
                ?? victim?.lastAttacker as BaseCombatEntity
                ?? killerPlayer;
        }

        private void HandlePlayerKilledBot(BasePlayer killer, BaseCombatEntity victimEntity, BotRuntime victimRuntime, HitInfo info)
        {
            if (killer == null || victimRuntime == null)
            {
                return;
            }

            BroadcastKillFeed(killer, null, victimEntity, victimRuntime, info);

            if (TryAwardBotKillRp(killer, victimRuntime, out var amount)
                && amount > 0
                && config.BotKillIntegration.TellKillerAboutRpReward)
            {
                killer.ChatMessage(FormatKillFeedTemplate(config.BotKillIntegration.RpRewardMessage, killer, null, victimEntity, victimRuntime, info, amount));
            }
        }

        private void HandleBotKilledPlayer(BaseCombatEntity killerEntity, BotRuntime killerRuntime, BasePlayer victim, HitInfo info)
        {
            if (killerEntity == null || killerRuntime == null || victim == null)
            {
                return;
            }

            ApplyBotNativeDeathInfo(victim, killerEntity, killerRuntime, info);
            BroadcastKillFeed(killerEntity, killerRuntime, victim, null, info);
        }

        private void HandleBotKilledBot(BaseCombatEntity killerEntity, BotRuntime killerRuntime, BaseCombatEntity victimEntity, BotRuntime victimRuntime, HitInfo info)
        {
            if (killerEntity == null || killerRuntime == null || victimEntity == null || victimRuntime == null)
            {
                return;
            }

            BroadcastKillFeed(killerEntity, killerRuntime, victimEntity, victimRuntime, info);
        }

        private void BroadcastKillFeed(BaseCombatEntity killerEntity, BotRuntime killerRuntime, BaseCombatEntity victimEntity, BotRuntime victimRuntime, HitInfo info)
        {
            if (config?.BotKillIntegration?.BroadcastPlayerLikeKillMessages != true)
            {
                return;
            }

            var message = FormatKillFeedTemplate(config.BotKillIntegration.KillMessage, killerEntity, killerRuntime, victimEntity, victimRuntime, info, 0);
            var formatted = (config.BotKillIntegration.ChatFormat ?? "{message}").Replace("{message}", message);

            if (killerRuntime != null
                && config.BotKillIntegration.UseBotAvatarAsChatSender
                && TrySendBotChatMessage(killerRuntime, formatted))
            {
                return;
            }

            PrintToChat(formatted);
        }

        private bool TrySendBotChatMessage(BotRuntime runtime, string formatted)
        {
            if (runtime == null || string.IsNullOrWhiteSpace(runtime.AvatarChatUserId) || string.IsNullOrWhiteSpace(formatted))
            {
                return false;
            }

            if (!ulong.TryParse(runtime.AvatarChatUserId, out var chatUserId) || chatUserId == 0UL)
            {
                return false;
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                {
                    continue;
                }

                player.SendConsoleCommand("chat.add", 2, chatUserId, formatted);
            }

            return true;
        }

        private void ApplyBotNativeDeathInfo(BasePlayer victim, BaseCombatEntity killerEntity, BotRuntime killerRuntime, HitInfo info)
        {
            if (victim == null || killerRuntime == null || victim.lifeStory == null)
            {
                return;
            }

            if (victim.lifeStory.deathInfo == null)
            {
                victim.lifeStory.deathInfo = new ProtoBuf.PlayerLifeStory.DeathInfo();
            }

            var deathInfo = victim.lifeStory.deathInfo;
            deathInfo.attackerName = BotChatName(killerRuntime);

            if (ulong.TryParse(killerRuntime.AvatarChatUserId, out var avatarSteamId) && avatarSteamId != 0UL)
            {
                deathInfo.attackerSteamID = avatarSteamId;
            }

            var weaponName = KillWeaponName(info, null);

            if (!string.IsNullOrWhiteSpace(weaponName) && !string.Equals(weaponName, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                deathInfo.inflictorName = weaponName;
            }

            var distanceMeters = KillDistanceMeters(killerEntity, victim, info);

            if (distanceMeters >= 0f)
            {
                deathInfo.attackerDistance = distanceMeters;
            }

            victim.SetOverrideDeathBlow(deathInfo);
        }

        private void ProtectBotNativeDeathInfoFromScientistOverride(BasePlayer victim, BaseCombatEntity killerEntity, BotRuntime killerRuntime, HitInfo info, float now)
        {
            if (victim == null || killerRuntime == null || info == null)
            {
                return;
            }

            var victimId = CombatTargetId(victim);

            if (victimId == 0UL)
            {
                return;
            }

            pendingBotPlayerDeaths[victimId] = new PendingBotPlayerDeath
            {
                KillerEntity = killerEntity,
                KillerRuntime = killerRuntime,
                HitInfo = info,
                ExpiresAt = now + 6f
            };

            // ScientistNPC.AttackerInfo() forces the native death card name back to "Scientist".
            // Clearing only the death-blow initiator lets Rust keep weapon/hit data while preserving our override.
            info.Initiator = null;
        }

        private PendingBotPlayerDeath TakePendingBotPlayerDeath(BasePlayer victim, HitInfo info, float now)
        {
            if (victim == null)
            {
                return null;
            }

            var victimId = CombatTargetId(victim);

            if (victimId == 0UL || !pendingBotPlayerDeaths.TryGetValue(victimId, out var pending))
            {
                return null;
            }

            pendingBotPlayerDeaths.Remove(victimId);

            if (pending == null || pending.ExpiresAt < now)
            {
                return null;
            }

            if (pending.HitInfo != null && info != null && !ReferenceEquals(pending.HitInfo, info))
            {
                return null;
            }

            return pending;
        }

        private bool TryAwardBotKillRp(BasePlayer killer, BotRuntime victimRuntime, out int amount)
        {
            amount = Math.Max(0, config?.BotKillIntegration?.ServerRewardsRpPerBotKill ?? 0);

            if (killer == null || victimRuntime == null || config?.BotKillIntegration?.AwardServerRewardsRp != true || amount <= 0)
            {
                return false;
            }

            if (ServerRewards == null)
            {
                if (!serverRewardsUnavailableWarned)
                {
                    PrintWarning("ServerRewards is not loaded; roam bot kill RP rewards are enabled but could not be awarded.");
                    serverRewardsUnavailableWarned = true;
                }

                return false;
            }

            try
            {
                var result = ServerRewards.Call("AddPoints", killer.UserIDString, amount);

                if (result is bool && !(bool)result)
                {
                    PrintWarning($"ServerRewards rejected {amount} RP for {killer.UserIDString} after killing {victimRuntime.DisplayName}.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                PrintWarning($"ServerRewards AddPoints failed for roam bot kill reward: {ex.Message}");
                return false;
            }
        }

        private string FormatKillFeedTemplate(string template, BaseCombatEntity killerEntity, BotRuntime killerRuntime, BaseCombatEntity victimEntity, BotRuntime victimRuntime, HitInfo info, int rpAmount)
        {
            var distanceMeters = KillDistanceMeters(killerEntity, victimEntity, info);
            var botRuntime = victimRuntime ?? killerRuntime;

            return (template ?? "")
                .Replace("{killer}", KillFeedName(killerEntity, killerRuntime, "Unknown"))
                .Replace("{victim}", KillFeedName(victimEntity, victimRuntime, "Unknown"))
                .Replace("{bot}", BotChatName(botRuntime))
                .Replace("{killer_clan}", KillClanTag(killerRuntime))
                .Replace("{victim_clan}", KillClanTag(victimRuntime))
                .Replace("{weapon}", KillWeaponName(info, killerEntity))
                .Replace("{method}", KillMethodName(info))
                .Replace("{distance}", KillDistanceLabel(distanceMeters))
                .Replace("{distance_m}", distanceMeters < 0f ? "unknown" : distanceMeters.ToString("0", CultureInfo.InvariantCulture))
                .Replace("{rp}", rpAmount.ToString(CultureInfo.InvariantCulture));
        }

        private string KillFeedName(BaseCombatEntity entity, BotRuntime runtime, string fallback)
        {
            if (runtime != null)
            {
                return BotChatName(runtime);
            }

            var player = entity as BasePlayer;

            if (player != null)
            {
                return SafeChatName(PlayerName(player), player.UserIDString ?? fallback);
            }

            return SafeChatName(entity?.ShortPrefabName, fallback);
        }

        private string KillClanTag(BotRuntime runtime)
        {
            return SafeChatName(runtime?.ClanTag, "");
        }

        private string BotChatName(BotRuntime runtime)
        {
            if (runtime == null)
            {
                return "Roam Bot";
            }

            var name = SafeChatName(runtime.DisplayName, "Roam Bot");
            var tag = SafeChatName(runtime.ClanTag, "");

            return string.IsNullOrWhiteSpace(tag) ? name : $"[{tag}] {name}";
        }

        private string SafeChatName(string value, string fallback)
        {
            var cleaned = CleanName(value);
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }

        private string KillDistanceLabel(float distanceMeters)
        {
            return distanceMeters < 0f
                ? "unknown range"
                : $"{distanceMeters.ToString("0", CultureInfo.InvariantCulture)}m";
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return $"{(bytes / (1024f * 1024f * 1024f)).ToString("0.0", CultureInfo.InvariantCulture)} GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return $"{(bytes / (1024f * 1024f)).ToString("0.0", CultureInfo.InvariantCulture)} MB";
            }

            if (bytes >= 1024L)
            {
                return $"{(bytes / 1024f).ToString("0.0", CultureInfo.InvariantCulture)} KB";
            }

            return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
        }

        private float KillDistanceMeters(BaseCombatEntity killerEntity, BaseCombatEntity victimEntity, HitInfo info)
        {
            var killerPosition = killerEntity == null ? Vector3.zero : killerEntity.transform.position;
            var victimPosition = victimEntity == null ? Vector3.zero : victimEntity.transform.position;

            if (victimPosition == Vector3.zero)
            {
                victimPosition = info?.HitPositionWorld ?? Vector3.zero;
            }

            if (killerPosition == Vector3.zero || victimPosition == Vector3.zero)
            {
                return -1f;
            }

            return Vector3.Distance(killerPosition, victimPosition);
        }

        private string KillMethodName(HitInfo info)
        {
            try
            {
                var damageType = info?.damageTypes?.GetMajorityDamageType();
                var raw = damageType?.ToString();

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return info?.Weapon != null || info?.WeaponPrefab != null ? "weapon" : "damage";
                }

                if (raw.Equals("Bullet", StringComparison.OrdinalIgnoreCase))
                {
                    return "gunfire";
                }

                if (raw.Equals("Explosion", StringComparison.OrdinalIgnoreCase))
                {
                    return "explosion";
                }

                if (raw.Equals("Slash", StringComparison.OrdinalIgnoreCase)
                    || raw.Equals("Stab", StringComparison.OrdinalIgnoreCase)
                    || raw.Equals("Blunt", StringComparison.OrdinalIgnoreCase))
                {
                    return "melee";
                }

                return raw.Replace("_", " ").ToLowerInvariant();
            }
            catch
            {
                return "damage";
            }
        }

        private string KillWeaponName(HitInfo info, BaseCombatEntity killerEntity)
        {
            var killer = killerEntity as BasePlayer;
            var item = info?.Weapon?.GetItem() ?? killer?.GetActiveItem();
            var displayName = item?.info?.displayName?.english;

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            var prefabName = info?.WeaponPrefab?.ShortPrefabName;

            if (!string.IsNullOrWhiteSpace(prefabName))
            {
                return prefabName.Replace("_", " ").Replace(".", " ");
            }

            return KillMethodName(info);
        }

        private void TrackRecentBotDeath(BaseCombatEntity bot, BotRuntime runtime)
        {
            var netId = NetId(bot);

            if (netId == 0)
            {
                return;
            }

            recentBotDeaths[netId] = new RecentBotDeath
            {
                Runtime = runtime,
                ExpiresAt = Time.realtimeSinceStartup + 5f
            };
        }

        private BotRuntime BotRuntimeForDeathNotice(BaseCombatEntity bot)
        {
            PruneRecentBotDeaths();

            var runtime = RuntimeFor(bot);

            if (runtime != null)
            {
                return runtime;
            }

            var netId = NetId(bot);

            if (netId != 0 && recentBotDeaths.TryGetValue(netId, out var recent) && recent?.ExpiresAt >= Time.realtimeSinceStartup)
            {
                return recent.Runtime;
            }

            return null;
        }

        private void PruneRecentBotDeaths()
        {
            var now = Time.realtimeSinceStartup;
            var expired = recentBotDeaths
                .Where(entry => entry.Value == null || entry.Value.ExpiresAt < now)
                .Select(entry => entry.Key)
                .ToList();

            foreach (var key in expired)
            {
                recentBotDeaths.Remove(key);
            }
        }

        private bool TryGetDeathNoticeEntity(Dictionary<string, object> deathData, string key, out BaseCombatEntity entity)
        {
            entity = null;

            if (deathData == null || !deathData.TryGetValue(key, out var value))
            {
                return false;
            }

            entity = value as BaseCombatEntity;
            return entity != null;
        }

        private ulong NetId(BaseNetworkable entity)
        {
            try
            {
                return entity?.net?.ID.Value ?? 0UL;
            }
            catch
            {
                return 0UL;
            }
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

        private ulong CombatTargetId(BasePlayer player)
        {
            if (player == null)
            {
                return 0UL;
            }

            return player.userID != 0UL ? player.userID : NetId(player);
        }

        private bool SameBotClan(BotRuntime left, BotRuntime right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(left.ClanKey) && !string.IsNullOrWhiteSpace(right.ClanKey))
            {
                return string.Equals(left.ClanKey, right.ClanKey, StringComparison.OrdinalIgnoreCase);
            }

            return left.TeamId == right.TeamId;
        }

        private bool IsEnemyBot(BotRuntime source, BotRuntime target)
        {
            return config?.AI?.AllowBotClanWars == true
                && source != null
                && target != null
                && !SameBotClan(source, target);
        }

        private BasePlayer FindCombatTargetById(ulong targetId)
        {
            if (targetId == 0UL)
            {
                return null;
            }

            var player = BasePlayer.FindByID(targetId);

            if (player != null)
            {
                return player;
            }

            foreach (var entry in activeBots)
            {
                var botPlayer = entry.Key as BasePlayer;

                if (botPlayer != null && CombatTargetId(botPlayer) == targetId)
                {
                    return botPlayer;
                }
            }

            return null;
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
            stats.behavior_model_key = runtime.BehaviorModelKey ?? "";
            stats.player_profile_key = runtime.PlayerProfileKey ?? "";
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

        private string NormalizeAdminKey(string key)
        {
            return (key ?? "")
                .Trim()
                .ToLowerInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");
        }

        private string NormalizeAdminTab(string tab)
        {
            var key = NormalizeAdminKey(tab);
            return AdminPanelTabs.Contains(key) ? key : "overview";
        }

        private string AdminTabFromTail(ConsoleSystem.Arg arg, string fallback)
        {
            if (arg?.Args != null)
            {
                for (var index = arg.Args.Length - 1; index >= 0; index--)
                {
                    var tab = NormalizeAdminKey(arg.Args[index].ToString());

                    if (AdminPanelTabs.Contains(tab))
                    {
                        return tab;
                    }
                }
            }

            return NormalizeAdminTab(fallback);
        }

        private bool TryParseAdminBool(string text, out bool value)
        {
            value = false;
            var normalized = NormalizeAdminKey(text);

            if (normalized == "1" || normalized == "true" || normalized == "yes" || normalized == "on" || normalized == "enable" || normalized == "enabled")
            {
                value = true;
                return true;
            }

            if (normalized == "0" || normalized == "false" || normalized == "no" || normalized == "off" || normalized == "disable" || normalized == "disabled")
            {
                value = false;
                return true;
            }

            return false;
        }

        private bool ToggleAdminBooleanSetting(string key, out bool restartRuntime, out bool refreshAdvisor, out bool refreshNameplates)
        {
            restartRuntime = false;
            refreshAdvisor = false;
            refreshNameplates = false;

            switch (NormalizeAdminKey(key))
            {
                case "random_fallback":
                    config.Spawn.UseRandomLandFallback = !config.Spawn.UseRandomLandFallback;
                    return true;
                case "generated_near":
                    config.Spawn.UseGeneratedPositionsNearPlayers = !config.Spawn.UseGeneratedPositionsNearPlayers;
                    return true;
                case "require_land":
                    config.Spawn.RequireLandSpawns = !config.Spawn.RequireLandSpawns;
                    return true;
                case "physics_surface":
                    config.Spawn.UsePhysicsSurfaceSpawnChecks = !config.Spawn.UsePhysicsSurfaceSpawnChecks;
                    return true;
                case "avoid_safe_zones":
                    config.Spawn.AvoidSafeZoneSpawns = !config.Spawn.AvoidSafeZoneSpawns;
                    return true;
                case "ignore_safe_zone_players":
                    config.Spawn.IgnorePlayersInSafeZones = !config.Spawn.IgnorePlayersInSafeZones;
                    return true;
                case "los_shoot":
                    config.AI.RequireLineOfSightToShoot = !config.AI.RequireLineOfSightToShoot;
                    return true;
                case "allow_hearing":
                    config.AI.AllowHearing = !config.AI.AllowHearing;
                    return true;
                case "allow_cover":
                    config.AI.AllowCover = !config.AI.AllowCover;
                    return true;
                case "allow_flanking":
                    config.AI.AllowFlanking = !config.AI.AllowFlanking;
                    return true;
                case "allow_grenades":
                    config.AI.AllowGrenades = !config.AI.AllowGrenades;
                    return true;
                case "allow_smoke":
                    config.AI.AllowSmoke = !config.AI.AllowSmoke;
                    return true;
                case "allow_barricades":
                    config.AI.AllowBarricades = !config.AI.AllowBarricades;
                    return true;
                case "jiggle":
                    config.AI.AllowJigglePeeking = !config.AI.AllowJigglePeeking;
                    return true;
                case "jump_peek":
                    config.AI.AllowJumpPeekApproximation = !config.AI.AllowJumpPeekApproximation;
                    return true;
                case "bot_clan_wars":
                    config.AI.AllowBotClanWars = !config.AI.AllowBotClanWars;
                    return true;
                case "foliage":
                    config.AI.FoliageBlocksVision = !config.AI.FoliageBlocksVision;
                    return true;
                case "foliage_terrain":
                    config.AI.FoliageTerrainSampling = !config.AI.FoliageTerrainSampling;
                    return true;
                case "base_avoidance":
                    config.AI.DoNotEnterBases = !config.AI.DoNotEnterBases;
                    return true;
                case "grant_meds":
                    config.AI.GrantBotMedicalItems = !config.AI.GrantBotMedicalItems;
                    return true;
                case "real_meds":
                    config.AI.UseRealMedicalItemsForCoverHeal = !config.AI.UseRealMedicalItemsForCoverHeal;
                    return true;
                case "auto_reload":
                    config.AI.AutoReloadBotWeapons = !config.AI.AutoReloadBotWeapons;
                    return true;
                case "kill_chat":
                    config.BotKillIntegration.BroadcastPlayerLikeKillMessages = !config.BotKillIntegration.BroadcastPlayerLikeKillMessages;
                    return true;
                case "deathnotes":
                    config.BotKillIntegration.SuppressDeathNotesForRoamBotKills = !config.BotKillIntegration.SuppressDeathNotesForRoamBotKills;
                    return true;
                case "award_rp":
                    config.BotKillIntegration.AwardServerRewardsRp = !config.BotKillIntegration.AwardServerRewardsRp;
                    return true;
                case "tell_rp":
                    config.BotKillIntegration.TellKillerAboutRpReward = !config.BotKillIntegration.TellKillerAboutRpReward;
                    return true;
                case "kill_unload":
                    config.Persistence.KillBotsOnPluginUnload = !config.Persistence.KillBotsOnPluginUnload;
                    return true;
                case "kill_disable":
                    config.Persistence.KillBotsOnDisable = !config.Persistence.KillBotsOnDisable;
                    return true;
                case "leave_corpses":
                    config.Persistence.LeaveCorpses = !config.Persistence.LeaveCorpses;
                    return true;
                case "leave_entities":
                    config.Persistence.LeaveBotPlacedEntities = !config.Persistence.LeaveBotPlacedEntities;
                    return true;
                case "nuke_enabled":
                    config.Persistence.EmergencyKillCommandEnabled = !config.Persistence.EmergencyKillCommandEnabled;
                    return true;
                case "debug_spawn":
                    config.Debug.DebugSpawnDetails = !config.Debug.DebugSpawnDetails;
                    return true;
                case "debug_perception":
                    config.Debug.DebugPerception = !config.Debug.DebugPerception;
                    return true;
                case "debug_tactical":
                    config.Debug.DebugTacticalDecisions = !config.Debug.DebugTacticalDecisions;
                    return true;
                case "nameplates":
                    config.Debug.DebugBotNameplates = !config.Debug.DebugBotNameplates;
                    refreshNameplates = true;
                    return true;
                case "side_panel":
                    config.Debug.DebugBotSidePanel = !config.Debug.DebugBotSidePanel;
                    refreshNameplates = true;
                    return true;
                case "anchor_viewer":
                    config.Debug.DebugUiIncludesAnchorPlayer = !config.Debug.DebugUiIncludesAnchorPlayer;
                    refreshNameplates = true;
                    return true;
                case "cover_scores":
                    config.Debug.DebugCoverScores = !config.Debug.DebugCoverScores;
                    return true;
                case "debug_advisor":
                    config.Debug.DebugDecisionAdvisor = !config.Debug.DebugDecisionAdvisor;
                    return true;
                case "console_logs":
                    config.Debug.DebugConsoleLogs = !config.Debug.DebugConsoleLogs;
                    return true;
                case "advisor_enabled":
                    config.DecisionAdvisor.Enabled = !config.DecisionAdvisor.Enabled;
                    refreshAdvisor = true;
                    return true;
                case "advisor_shadow":
                    config.DecisionAdvisor.ShadowMode = !config.DecisionAdvisor.ShadowMode;
                    refreshAdvisor = true;
                    return true;
                case "advisor_fallback":
                    config.DecisionAdvisor.FallbackOnAnyFailure = !config.DecisionAdvisor.FallbackOnAnyFailure;
                    refreshAdvisor = true;
                    return true;
                case "advisor_unconfigured_failure":
                    config.DecisionAdvisor.TreatUnconfiguredAdvisorAsFailure = !config.DecisionAdvisor.TreatUnconfiguredAdvisorAsFailure;
                    refreshAdvisor = true;
                    return true;
                case "advisor_schema":
                    config.DecisionAdvisor.UseStructuredResponseSchema = !config.DecisionAdvisor.UseStructuredResponseSchema;
                    refreshAdvisor = true;
                    return true;
                case "advisor_engaged_only":
                    config.DecisionAdvisor.RequireActivePlayerEngagement = !config.DecisionAdvisor.RequireActivePlayerEngagement;
                    return true;
                case "advisor_ask_stuck":
                    config.DecisionAdvisor.AskWhenBotIsStuck = !config.DecisionAdvisor.AskWhenBotIsStuck;
                    return true;
                case "advisor_ask_close":
                    config.DecisionAdvisor.AskWhenActionScoresAreClose = !config.DecisionAdvisor.AskWhenActionScoresAreClose;
                    return true;
                case "advisor_ask_high_impact":
                    config.DecisionAdvisor.AskWhenPushRetreatOrFlankIsHighImpact = !config.DecisionAdvisor.AskWhenPushRetreatOrFlankIsHighImpact;
                    return true;
                case "advisor_ask_failed":
                    config.DecisionAdvisor.AskWhenSameActionFailedRepeatedly = !config.DecisionAdvisor.AskWhenSameActionFailedRepeatedly;
                    return true;
                case "advisor_ask_squad":
                    config.DecisionAdvisor.AskWhenSquadStateChangesSharply = !config.DecisionAdvisor.AskWhenSquadStateChangesSharply;
                    return true;
                case "advisor_trace":
                    config.DecisionAdvisor.LogDecisionTraces = !config.DecisionAdvisor.LogDecisionTraces;
                    return true;
                default:
                    return false;
            }
        }

        private bool SetAdminIntegerSetting(string key, int value)
        {
            switch (NormalizeAdminKey(key))
            {
                case "target":
                    config.TargetPopulation = Clamp(value, config.MinAllowedPopulation, Math.Min(AdminPanelMaximumPopulation, Math.Max(config.MinAllowedPopulation, config.MaxAllowedPopulation)));
                    return true;
                case "min_population":
                    config.MinAllowedPopulation = Clamp(value, 0, AdminPanelMaximumPopulation);
                    config.MaxAllowedPopulation = Math.Max(config.MinAllowedPopulation, config.MaxAllowedPopulation);
                    return true;
                case "max_population":
                    config.MaxAllowedPopulation = Clamp(value, Math.Max(0, config.MinAllowedPopulation), AdminPanelMaximumPopulation);
                    return true;
                case "solo_weight":
                    config.TeamSizeWeights["solo"] = Clamp(value, 0, 100);
                    return true;
                case "duo_weight":
                    config.TeamSizeWeights["duo"] = Clamp(value, 0, 100);
                    return true;
                case "trio_weight":
                    config.TeamSizeWeights["trio"] = Clamp(value, 0, 100);
                    return true;
                case "high_tier_weight":
                    config.HighTierKitWeight = Clamp(value, 0, 100);
                    return true;
                case "max_position_attempts":
                    config.Spawn.MaxPositionAttempts = Clamp(value, 10, 1000);
                    return true;
                case "near_attempts":
                    config.Spawn.NearPlayerAttempts = Clamp(value, 8, 1000);
                    return true;
                case "max_utility":
                    config.AI.MaxActiveBotUtilityProjectiles = Clamp(value, 1, 30);
                    return true;
                case "max_barricades":
                    config.AI.MaxActiveBotBarricades = Clamp(value, 0, 25);
                    return true;
                case "bot_med_amount":
                    config.AI.BotMedicalItemAmount = Clamp(value, 0, 12);
                    return true;
                case "hard_stuck_paths":
                    config.AI.HardStuckFailedPathsToDespawn = Clamp(value, 0, 200);
                    return true;
                case "max_stuck_points":
                    config.AI.MaxStuckMemoryPoints = Clamp(value, 1, 50);
                    return true;
                case "rp_reward":
                    config.BotKillIntegration.ServerRewardsRpPerBotKill = Clamp(value, 0, 100000);
                    return true;
                case "advisor_timeout":
                    config.DecisionAdvisor.TimeoutMilliseconds = Clamp(value, 100, 5000);
                    return true;
                case "advisor_concurrent":
                    config.DecisionAdvisor.MaxConcurrentRequests = Math.Max(0, value);
                    return true;
                case "advisor_events":
                    config.DecisionAdvisor.MaxRecentEventsInRequest = Math.Max(0, value);
                    return true;
                case "advisor_candidates":
                    config.DecisionAdvisor.MaxCandidateActions = Clamp(value, 1, 16);
                    return true;
                case "nameplate_font":
                    config.Debug.DebugNameplateFontSize = Clamp(value, 6, 14);
                    return true;
                default:
                    return false;
            }
        }

        private bool AdjustAdminIntegerSetting(string key, int delta)
        {
            if (!TryGetAdminIntegerSetting(key, out var current))
            {
                return false;
            }

            return SetAdminIntegerSetting(key, current + delta);
        }

        private bool TryGetAdminIntegerSetting(string key, out int value)
        {
            switch (NormalizeAdminKey(key))
            {
                case "target":
                    value = config.TargetPopulation;
                    return true;
                case "min_population":
                    value = config.MinAllowedPopulation;
                    return true;
                case "max_population":
                    value = config.MaxAllowedPopulation;
                    return true;
                case "solo_weight":
                    value = config.TeamSizeWeights.ContainsKey("solo") ? config.TeamSizeWeights["solo"] : 0;
                    return true;
                case "duo_weight":
                    value = config.TeamSizeWeights.ContainsKey("duo") ? config.TeamSizeWeights["duo"] : 0;
                    return true;
                case "trio_weight":
                    value = config.TeamSizeWeights.ContainsKey("trio") ? config.TeamSizeWeights["trio"] : 0;
                    return true;
                case "high_tier_weight":
                    value = config.HighTierKitWeight;
                    return true;
                case "max_position_attempts":
                    value = config.Spawn.MaxPositionAttempts;
                    return true;
                case "near_attempts":
                    value = config.Spawn.NearPlayerAttempts;
                    return true;
                case "max_utility":
                    value = config.AI.MaxActiveBotUtilityProjectiles;
                    return true;
                case "max_barricades":
                    value = config.AI.MaxActiveBotBarricades;
                    return true;
                case "bot_med_amount":
                    value = config.AI.BotMedicalItemAmount;
                    return true;
                case "hard_stuck_paths":
                    value = config.AI.HardStuckFailedPathsToDespawn;
                    return true;
                case "max_stuck_points":
                    value = config.AI.MaxStuckMemoryPoints;
                    return true;
                case "rp_reward":
                    value = config.BotKillIntegration.ServerRewardsRpPerBotKill;
                    return true;
                case "advisor_timeout":
                    value = config.DecisionAdvisor.TimeoutMilliseconds;
                    return true;
                case "advisor_concurrent":
                    value = config.DecisionAdvisor.MaxConcurrentRequests;
                    return true;
                case "advisor_events":
                    value = config.DecisionAdvisor.MaxRecentEventsInRequest;
                    return true;
                case "advisor_candidates":
                    value = config.DecisionAdvisor.MaxCandidateActions;
                    return true;
                case "nameplate_font":
                    value = config.Debug.DebugNameplateFontSize;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private bool AdminIntegerSettingNeedsRuntimeRestart(string key)
        {
            return false;
        }

        private bool AdminIntegerSettingNeedsNameplateRestart(string key)
        {
            return NormalizeAdminKey(key) == "nameplate_font";
        }

        private bool SetAdminFloatSetting(string key, float value)
        {
            switch (NormalizeAdminKey(key))
            {
                case "maintain_interval":
                    config.MaintainIntervalSeconds = Math.Max(5f, value);
                    return true;
                case "perception_tick":
                    config.AI.PerceptionTickSeconds = Mathf.Clamp(value, 0.1f, 2f);
                    return true;
                case "decision_tick":
                    config.AI.DecisionTickSeconds = Mathf.Clamp(value, 0.15f, 3f);
                    return true;
                case "squad_tick":
                    config.AI.SquadTickSeconds = Mathf.Clamp(value, 0.25f, 5f);
                    return true;
                case "spawn_retry":
                    config.SpawnFailureRetrySeconds = Math.Max(15f, value);
                    return true;
                case "respawn_delay":
                    config.RespawnDelaySeconds = Math.Max(5f, value);
                    return true;
                case "nav_sample":
                    config.Spawn.NavmeshSampleDistance = Math.Max(2f, value);
                    return true;
                case "near_min":
                    config.Spawn.NearPlayerMinDistance = Math.Max(25f, value);
                    return true;
                case "near_max":
                    config.Spawn.NearPlayerMaxDistance = Math.Max(config.Spawn.NearPlayerMinDistance + 10f, value);
                    return true;
                case "group_radius":
                    config.Spawn.GroupSpawnRadius = Math.Max(1f, value);
                    return true;
                case "safe_buffer":
                    config.Spawn.SafeZoneSpawnBufferDistance = Math.Max(0f, value);
                    return true;
                case "vision_range":
                    config.AI.VisionRange = Math.Max(20f, value);
                    return true;
                case "vision_fov":
                    config.AI.VisionFovDegrees = Mathf.Clamp(value, 30f, 360f);
                    return true;
                case "close_awareness":
                    config.AI.CloseAwarenessRadius = Math.Max(0f, value);
                    return true;
                case "exposed_min":
                    config.AI.MinimumExposedTargetFraction = Mathf.Clamp(value, 0.1f, 1f);
                    return true;
                case "exposed_shoot":
                    config.AI.MinimumExposedTargetFractionToShoot = Mathf.Clamp(value, config.AI.MinimumExposedTargetFraction, 1f);
                    return true;
                case "target_memory":
                    config.AI.TargetMemorySeconds = Math.Max(1f, value);
                    return true;
                case "search_last_seen":
                    config.AI.SearchLastSeenSeconds = Math.Max(config.AI.TargetMemorySeconds, value);
                    return true;
                case "hearing_gun":
                    config.AI.UnsuppressedGunshotHearingRange = Mathf.Clamp(value, 0f, 500f);
                    return true;
                case "hearing_suppressed":
                    config.AI.SuppressedGunshotHearingRange = Mathf.Clamp(value, 0f, config.AI.UnsuppressedGunshotHearingRange);
                    return true;
                case "hearing_explosion":
                    config.AI.ExplosionHearingRange = Mathf.Clamp(value, 0f, 800f);
                    return true;
                case "hearing_melee":
                    config.AI.MeleeOrToolHearingRange = Mathf.Clamp(value, 0f, 120f);
                    return true;
                case "hearing_sprint":
                    config.AI.SprintHearingRange = Mathf.Clamp(value, 0f, 80f);
                    return true;
                case "foliage_radius":
                    config.AI.FoliageVisionCheckRadius = Mathf.Clamp(value, 0.1f, 3f);
                    return true;
                case "foliage_clear":
                    config.AI.MaximumClearVisionThroughFoliage = Mathf.Clamp(value, 1f, config.AI.VisionRange);
                    return true;
                case "cover_radius":
                    config.AI.CoverSearchRadius = Math.Max(4f, value);
                    return true;
                case "cover_min_threat":
                    config.AI.CoverMinimumDistanceFromThreat = Math.Max(2f, value);
                    return true;
                case "flank_distance":
                    config.AI.SquadFlankDistance = Mathf.Clamp(value, 8f, 80f);
                    return true;
                case "regroup_distance":
                    config.AI.SquadRegroupDistance = Mathf.Clamp(value, 20f, 140f);
                    return true;
                case "grenade_cooldown":
                    config.AI.GrenadeCooldownSeconds = Math.Max(1f, value);
                    return true;
                case "team_grenade_cooldown":
                    config.AI.TeamGrenadeCooldownSeconds = Math.Max(1f, value);
                    return true;
                case "grenade_min":
                    config.AI.GrenadeMinThrowDistance = Mathf.Clamp(value, 4f, 35f);
                    return true;
                case "grenade_max":
                    config.AI.GrenadeMaxThrowDistance = Mathf.Clamp(value, config.AI.GrenadeMinThrowDistance + 2f, 90f);
                    return true;
                case "smoke_min":
                    config.AI.SmokeMinThrowDistance = Mathf.Clamp(value, 3f, 35f);
                    return true;
                case "smoke_max":
                    config.AI.SmokeMaxThrowDistance = Mathf.Clamp(value, config.AI.SmokeMinThrowDistance + 2f, 90f);
                    return true;
                case "barricade_cooldown":
                    config.AI.BarricadeCooldownSeconds = Mathf.Clamp(value, 5f, 45f);
                    return true;
                case "passive_heal":
                    config.AI.PassiveCombatHealPerSecond = Mathf.Clamp(value, 0f, 20f);
                    return true;
                case "cover_heal":
                    config.AI.LowHealthCoverHealPerSecond = Mathf.Clamp(value, 0f, 30f);
                    return true;
                case "base_radius":
                    config.AI.BaseAvoidanceRadius = Math.Max(1f, value);
                    return true;
                case "base_hold":
                    config.AI.BaseHoldSeconds = Math.Max(2f, value);
                    return true;
                case "hard_stuck_seconds":
                    config.AI.HardStuckDespawnSeconds = Mathf.Clamp(value, 0f, 900f);
                    return true;
                case "advisor_confidence":
                    config.DecisionAdvisor.MinimumConfidence = Mathf.Clamp01(value);
                    return true;
                case "advisor_min_seconds":
                    config.DecisionAdvisor.MinSecondsBetweenRequestsPerBot = Math.Max(0f, value);
                    return true;
                case "advisor_player_gate":
                    config.DecisionAdvisor.RequireRealPlayerWithinMeters = Mathf.Clamp(value, 0f, 5000f);
                    return true;
                case "learning_sample":
                    config.Learning.SampleIntervalSeconds = Mathf.Clamp(value, 0.25f, 10f);
                    return true;
                case "learning_outcome":
                    config.Learning.OutcomeWindowSeconds = Mathf.Clamp(value, 3f, 60f);
                    return true;
                case "learning_global_delta":
                    config.Learning.MaximumGlobalScoreDelta = Mathf.Clamp(value, 0f, 80f);
                    return true;
                case "learning_profile_delta":
                    config.Learning.MaximumProfileScoreDelta = Mathf.Clamp(value, 0f, 100f);
                    return true;
                case "nameplate_refresh":
                    config.Debug.DebugNameplateRefreshSeconds = Mathf.Clamp(value, 0.25f, 5f);
                    return true;
                case "nameplate_duration":
                    config.Debug.DebugNameplateDrawDurationSeconds = Mathf.Clamp(value, config.Debug.DebugNameplateRefreshSeconds, 10f);
                    return true;
                case "nameplate_height":
                    config.Debug.DebugNameplateHeight = Mathf.Clamp(value, 2.5f, 6f);
                    return true;
                case "nameplate_distance":
                    config.Debug.DebugNameplateMaxDistance = Mathf.Clamp(value, 25f, 1000f);
                    return true;
                default:
                    return false;
            }
        }

        private bool AdjustAdminFloatSetting(string key, float delta)
        {
            if (!TryGetAdminFloatSetting(key, out var current))
            {
                return false;
            }

            return SetAdminFloatSetting(key, current + delta);
        }

        private bool TryGetAdminFloatSetting(string key, out float value)
        {
            switch (NormalizeAdminKey(key))
            {
                case "maintain_interval":
                    value = config.MaintainIntervalSeconds;
                    return true;
                case "perception_tick":
                    value = config.AI.PerceptionTickSeconds;
                    return true;
                case "decision_tick":
                    value = config.AI.DecisionTickSeconds;
                    return true;
                case "squad_tick":
                    value = config.AI.SquadTickSeconds;
                    return true;
                case "spawn_retry":
                    value = config.SpawnFailureRetrySeconds;
                    return true;
                case "respawn_delay":
                    value = config.RespawnDelaySeconds;
                    return true;
                case "nav_sample":
                    value = config.Spawn.NavmeshSampleDistance;
                    return true;
                case "near_min":
                    value = config.Spawn.NearPlayerMinDistance;
                    return true;
                case "near_max":
                    value = config.Spawn.NearPlayerMaxDistance;
                    return true;
                case "group_radius":
                    value = config.Spawn.GroupSpawnRadius;
                    return true;
                case "safe_buffer":
                    value = config.Spawn.SafeZoneSpawnBufferDistance;
                    return true;
                case "vision_range":
                    value = config.AI.VisionRange;
                    return true;
                case "vision_fov":
                    value = config.AI.VisionFovDegrees;
                    return true;
                case "close_awareness":
                    value = config.AI.CloseAwarenessRadius;
                    return true;
                case "exposed_min":
                    value = config.AI.MinimumExposedTargetFraction;
                    return true;
                case "exposed_shoot":
                    value = config.AI.MinimumExposedTargetFractionToShoot;
                    return true;
                case "target_memory":
                    value = config.AI.TargetMemorySeconds;
                    return true;
                case "search_last_seen":
                    value = config.AI.SearchLastSeenSeconds;
                    return true;
                case "hearing_gun":
                    value = config.AI.UnsuppressedGunshotHearingRange;
                    return true;
                case "hearing_suppressed":
                    value = config.AI.SuppressedGunshotHearingRange;
                    return true;
                case "hearing_explosion":
                    value = config.AI.ExplosionHearingRange;
                    return true;
                case "hearing_melee":
                    value = config.AI.MeleeOrToolHearingRange;
                    return true;
                case "hearing_sprint":
                    value = config.AI.SprintHearingRange;
                    return true;
                case "foliage_radius":
                    value = config.AI.FoliageVisionCheckRadius;
                    return true;
                case "foliage_clear":
                    value = config.AI.MaximumClearVisionThroughFoliage;
                    return true;
                case "cover_radius":
                    value = config.AI.CoverSearchRadius;
                    return true;
                case "cover_min_threat":
                    value = config.AI.CoverMinimumDistanceFromThreat;
                    return true;
                case "flank_distance":
                    value = config.AI.SquadFlankDistance;
                    return true;
                case "regroup_distance":
                    value = config.AI.SquadRegroupDistance;
                    return true;
                case "grenade_cooldown":
                    value = config.AI.GrenadeCooldownSeconds;
                    return true;
                case "team_grenade_cooldown":
                    value = config.AI.TeamGrenadeCooldownSeconds;
                    return true;
                case "grenade_min":
                    value = config.AI.GrenadeMinThrowDistance;
                    return true;
                case "grenade_max":
                    value = config.AI.GrenadeMaxThrowDistance;
                    return true;
                case "smoke_min":
                    value = config.AI.SmokeMinThrowDistance;
                    return true;
                case "smoke_max":
                    value = config.AI.SmokeMaxThrowDistance;
                    return true;
                case "barricade_cooldown":
                    value = config.AI.BarricadeCooldownSeconds;
                    return true;
                case "passive_heal":
                    value = config.AI.PassiveCombatHealPerSecond;
                    return true;
                case "cover_heal":
                    value = config.AI.LowHealthCoverHealPerSecond;
                    return true;
                case "base_radius":
                    value = config.AI.BaseAvoidanceRadius;
                    return true;
                case "base_hold":
                    value = config.AI.BaseHoldSeconds;
                    return true;
                case "hard_stuck_seconds":
                    value = config.AI.HardStuckDespawnSeconds;
                    return true;
                case "advisor_confidence":
                    value = config.DecisionAdvisor.MinimumConfidence;
                    return true;
                case "advisor_min_seconds":
                    value = config.DecisionAdvisor.MinSecondsBetweenRequestsPerBot;
                    return true;
                case "advisor_player_gate":
                    value = config.DecisionAdvisor.RequireRealPlayerWithinMeters;
                    return true;
                case "learning_sample":
                    value = config.Learning.SampleIntervalSeconds;
                    return true;
                case "learning_outcome":
                    value = config.Learning.OutcomeWindowSeconds;
                    return true;
                case "learning_global_delta":
                    value = config.Learning.MaximumGlobalScoreDelta;
                    return true;
                case "learning_profile_delta":
                    value = config.Learning.MaximumProfileScoreDelta;
                    return true;
                case "nameplate_refresh":
                    value = config.Debug.DebugNameplateRefreshSeconds;
                    return true;
                case "nameplate_duration":
                    value = config.Debug.DebugNameplateDrawDurationSeconds;
                    return true;
                case "nameplate_height":
                    value = config.Debug.DebugNameplateHeight;
                    return true;
                case "nameplate_distance":
                    value = config.Debug.DebugNameplateMaxDistance;
                    return true;
                default:
                    value = 0f;
                    return false;
            }
        }

        private bool AdminFloatSettingNeedsRuntimeRestart(string key)
        {
            var normalized = NormalizeAdminKey(key);
            return normalized == "maintain_interval" || normalized == "perception_tick" || normalized == "decision_tick" || normalized == "squad_tick";
        }

        private bool AdminFloatSettingNeedsNameplateRestart(string key)
        {
            var normalized = NormalizeAdminKey(key);
            return normalized == "nameplate_refresh" || normalized == "nameplate_duration" || normalized == "nameplate_height" || normalized == "nameplate_distance";
        }

        private void SetAdminAdvisorMode(string mode, ConsoleSystem.Arg arg)
        {
            switch (NormalizeAdminKey(mode))
            {
                case "off":
                    config.DecisionAdvisor.Enabled = false;
                    config.DecisionAdvisor.Provider = AdvisorProviderNone;
                    config.DecisionAdvisor.Mode = AdvisorModeFallbackOnly;
                    config.DecisionAdvisor.ShadowMode = true;
                    SaveAdminConfigChange(false, false, true, false);
                    Reply(arg, "Raidlands roam bot advisor disabled. Deterministic fallback remains active.");
                    return;
                case "fallback":
                case "fallback_only":
                    config.DecisionAdvisor.Enabled = true;
                    config.DecisionAdvisor.Provider = AdvisorProviderNone;
                    config.DecisionAdvisor.Mode = AdvisorModeFallbackOnly;
                    config.DecisionAdvisor.ShadowMode = true;
                    SaveAdminConfigChange(false, false, true, false);
                    Reply(arg, "Raidlands roam bot advisor set to fallback_only with provider none.");
                    return;
                case "shadow":
                    config.DecisionAdvisor.Enabled = true;
                    config.DecisionAdvisor.Mode = AdvisorModeShadow;
                    config.DecisionAdvisor.ShadowMode = true;
                    SaveAdminConfigChange(false, false, true, false);
                    Reply(arg, $"Raidlands roam bot advisor set to shadow mode. Provider remains {config.DecisionAdvisor.Provider}.");
                    return;
                case "canary":
                    config.DecisionAdvisor.Enabled = true;
                    config.DecisionAdvisor.Mode = AdvisorModeCanary;
                    config.DecisionAdvisor.ShadowMode = false;
                    SaveAdminConfigChange(false, false, true, false);
                    Reply(arg, "Raidlands roam bot advisor set to canary mode.");
                    return;
                default:
                    Reply(arg, "Usage: advisor off|fallback|shadow|canary");
                    return;
            }
        }

        private bool CanAdmin(BasePlayer player)
        {
            if (player == null)
            {
                return true;
            }

            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        private bool CanAdmin(ConsoleSystem.Arg arg)
        {
            var player = arg?.Connection?.player as BasePlayer;
            return CanAdmin(player);
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

        private string ArgStringRange(ConsoleSystem.Arg arg, int startInclusive, int endExclusive)
        {
            if (arg?.Args == null || arg.Args.Length <= startInclusive || endExclusive <= startInclusive)
            {
                return "";
            }

            return string.Join(" ", arg.Args
                .Skip(startInclusive)
                .Take(Math.Max(0, Math.Min(endExclusive, arg.Args.Length) - startInclusive))
                .Select(value => value.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
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

        private bool TryReadFloatArg(ConsoleSystem.Arg arg, int index, out float value)
        {
            value = 0f;

            if (arg?.Args == null || arg.Args.Length <= index)
            {
                return false;
            }

            return float.TryParse(arg.Args[index].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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

        private bool ConsoleLogDue(string key, float cooldownSeconds)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return true;
            }

            if (cooldownSeconds <= 0f)
            {
                return true;
            }

            var now = Time.realtimeSinceStartup;

            if (consoleLogLastAt.TryGetValue(key, out var lastAt) && now - lastAt < cooldownSeconds)
            {
                return false;
            }

            consoleLogLastAt[key] = now;
            return true;
        }

        private void DebugLog(string key, string message, float cooldownSeconds = -1f)
        {
            if (config?.Debug?.DebugConsoleLogs != true)
            {
                return;
            }

            var cooldown = cooldownSeconds >= 0f ? cooldownSeconds : Math.Max(1f, config.Debug.DebugConsoleLogCooldownSeconds);

            if (ConsoleLogDue($"debug:{key}", cooldown))
            {
                Puts(message);
            }
        }

        private void DebugWarning(string key, string message, float cooldownSeconds = -1f)
        {
            if (config?.Debug?.DebugConsoleLogs != true)
            {
                return;
            }

            var cooldown = cooldownSeconds >= 0f ? cooldownSeconds : Math.Max(1f, config.Debug.DebugConsoleLogCooldownSeconds);

            if (ConsoleLogDue($"debug-warn:{key}", cooldown))
            {
                PrintWarning(message);
            }
        }

        private void ThrottledInfo(string key, string message, float cooldownSeconds = -1f)
        {
            var cooldown = cooldownSeconds >= 0f ? cooldownSeconds : Math.Max(5f, config?.Debug?.ConsoleWarningCooldownSeconds ?? 30f);

            if (ConsoleLogDue($"info:{key}", cooldown))
            {
                Puts(message);
            }
        }

        private void ThrottledWarning(string key, string message, float cooldownSeconds = -1f)
        {
            var cooldown = cooldownSeconds >= 0f ? cooldownSeconds : Math.Max(5f, config?.Debug?.ConsoleWarningCooldownSeconds ?? 30f);

            if (ConsoleLogDue($"warn:{key}", cooldown))
            {
                PrintWarning(message);
            }
        }

        private int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
