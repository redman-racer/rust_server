using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RaidlandsEvents", "Raidlands", "0.5.3")]
    [Description("Raidlands raid-base event manager with automatic spawning, durable rewards, history, and dedicated leaderboards.")]
    public class RaidlandsEvents : RustPlugin
    {
        private const string AdminPermission = "raidlandsevents.admin";
        private const string LayoutPermission = "raidlandsevents.admin.layouts";
        private const string StartPermission = "raidlandsevents.admin.start";
        private const string StopPermission = "raidlandsevents.admin.stop";
        private const string RewardsPermission = "raidlandsevents.admin.rewards";
        private const string DataFileName = "RaidlandsEvents";
        private const string SpawnGridDataFileName = "RaidlandsEvents/SpawnGrid";
        private const string LeaderboardsDataFileName = "RaidlandsEvents/leaderboards";
        private const string EventHistoryDataFileName = "RaidlandsEvents/history";
        private const string RewardLedgerDataFileName = "RaidlandsEvents/pending_rewards";
        private const string RewardProfilesDirectory = "RaidlandsEvents/reward_profiles/";
        private const string DefaultRewardProfileId = "default_raid_base";
        private const int RewardProfileSchemaVersion = 1;
        private const int EventDataSchemaVersion = 1;
        private const int PublicApiSchemaVersion = 1;
        private const int SpawnGridSchemaVersion = 6;
        private const int LegacyRandomSearchAttempts = 80;
        private const string CopyPasteDirectory = "copypaste/";
        private const string GenericRadiusMapMarkerPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        private const string EventsManagerUi = "RaidlandsEvents.EventsManagerUi";
        private const string EventsManagerAutomaticUi = "RaidlandsEvents.EventsManagerUi.Automatic";
        private const string EventsManagerWorkspaceUi = "RaidlandsEvents.EventsManagerUi.Workspace";
        private const float NativeMarkerBaseRadius = 0.015f;
        private const float NativeMarkerRadiusPerMeter = 0.004f;

        private static readonly int GroundLayer = LayerMask.GetMask("Terrain", "World", "Water", "Default");
        private static readonly int PlayerBaseLayer = LayerMask.GetMask("Construction", "Construction Trigger", "Deployed");
        private static readonly int AutomaticObstacleLayer = LayerMask.GetMask("World", "Construction", "Construction Trigger", "Deployed", "Vehicle Large");
        private static readonly int StaticWorldObstacleLayer = LayerMask.GetMask("World");
        private static readonly int PreventBuildingLayer = LayerMask.GetMask("Prevent Building");

        [PluginReference]
        private Plugin CopyPaste, RaidlandsSentryTurrets, Clans;

        [PluginReference]
        private Plugin ServerRewards;

        private Configuration config;
        private StoredData data;
        private LeaderboardStore leaderboardData = new LeaderboardStore();
        private EventHistoryStore historyData = new EventHistoryStore();
        private RewardLedgerStore rewardLedger = new RewardLedgerStore();
        private readonly Dictionary<string, RewardProfile> rewardProfiles = new Dictionary<string, RewardProfile>(StringComparer.OrdinalIgnoreCase);
        private Timer autoSpawnTimer;
        private Timer automaticSearchTimer;
        private Timer spawnGridBuildTimer;
        private Timer expiryTimer;
        private readonly Collider[] automaticSearchColliders = new Collider[256];
        private readonly Dictionary<string, MapMarkerGenericRadius> markers = new Dictionary<string, MapMarkerGenericRadius>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, string> entityToInstance = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, ulong> explosiveOwnerIds = new Dictionary<ulong, ulong>();
        private readonly Dictionary<ulong, int> uiLayoutPages = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> uiActivePages = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, string> uiManagerPanels = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> uiActiveEventTabs = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> uiActiveSorts = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> uiActiveFilters = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> uiRewardProfileSelections = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> uiRewardProfilePages = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, string> uiRewardPreviews = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> uiRewardDeleteConfirm = new HashSet<ulong>();
        private readonly HashSet<ulong> uiOpenPlayers = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> uiRenderGenerations = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, string> uiScoreModalInstances = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, LootEditorState> lootEditors = new Dictionary<ulong, LootEditorState>();
        private readonly HashSet<string> pendingPasteInstances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MonumentZone> monumentZones = new List<MonumentZone>();
        private bool monumentZonesLoaded;
        private double lastScoreSaveUnix;
        private AutomaticLocationSearch automaticLocationSearch;
        private double lastAutomaticSearchLogUnix;
        private long automaticSearchRejectedCandidates;
        private string automaticSearchLastRejection;
        private SpawnGridCache spawnGridCache = new SpawnGridCache();
        private readonly Dictionary<int, double> spawnGridTemporaryUntil = new Dictionary<int, double>();
        private readonly HashSet<int> spawnGridReserved = new HashSet<int>();
        private readonly HashSet<string> spawnGridLayoutRejections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> spawnGridRejectionCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LayoutPlacementStats> spawnGridLayoutStats = new Dictionary<string, LayoutPlacementStats>(StringComparer.OrdinalIgnoreCase);
        private bool spawnGridReady;
        private bool spawnGridBuilding;
        private float spawnGridNextX;
        private float spawnGridNextZ;
        private float spawnGridMin;
        private float spawnGridMax;
        private int spawnGridProcessed;
        private int spawnGridTotal;
        private double spawnGridBuildStartedUnix;
        private double spawnGridLastSuccessUnix;
        private Vector3 spawnGridLastSuccessPosition;
        private double spawnGridMaximumSliceMilliseconds;
        private double lastLeaderboardChangedHookUnix;

        private class Configuration
        {
            [JsonProperty("Server Id")]
            public string ServerId = "raidlands-main";

            [JsonProperty("Event Types")]
            public EventTypesConfig EventTypes = new EventTypesConfig();

            [JsonProperty("AutoSpawn")]
            public AutoSpawnConfig AutoSpawn = new AutoSpawnConfig();

            [JsonProperty("LayoutRotation")]
            public LayoutRotationConfig LayoutRotation = new LayoutRotationConfig();

            [JsonProperty("LocationRules")]
            public LocationRulesConfig LocationRules = new LocationRulesConfig();

            [JsonProperty("Spawn Grid")]
            public SpawnGridConfig SpawnGrid = new SpawnGridConfig();

            [JsonProperty("Paste")]
            public PasteConfig Paste = new PasteConfig();

            [JsonProperty("MapMarker")]
            public MapMarkerConfig MapMarker = new MapMarkerConfig();

            [JsonProperty("Scoring")]
            public ScoringConfig Scoring = new ScoringConfig();

            [JsonProperty("Rewards")]
            public RewardsConfig Rewards = new RewardsConfig();

            [JsonProperty("Leaderboard")]
            public LeaderboardConfig Leaderboard = new LeaderboardConfig();

            [JsonProperty("Cleanup")]
            public CleanupConfig Cleanup = new CleanupConfig();

            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "<color=#ce422b>[Raidlands]</color>";
        }

        private class EventTypesConfig
        {
            [JsonProperty("Automatic Bases")]
            public AutomaticBasesConfig AutomaticBases = new AutomaticBasesConfig();
        }

        private class AutomaticBasesConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Display Name")]
            public string DisplayName = "Automatic Bases";

            [JsonProperty("Check Frequency Minutes")]
            public float CheckFrequencyMinutes = 15f;

            [JsonProperty("Minimum Active Bases")]
            public int MinimumActiveBases = 4;

            [JsonProperty("Maximum Active Bases")]
            public int MaximumActiveBases = 8;

            [JsonProperty("Maximum Spawns Per Check")]
            public int MaximumSpawnsPerCheck = 2;

            [JsonProperty("Minimum Online Players")]
            public int MinimumOnlinePlayers = 0;

            [JsonProperty("Hard Lifetime Hours")]
            public float HardLifetimeHours = 24f;

            [JsonProperty("Percentage To Announce")]
            public float PercentageToAnnounce = 25f;

            [JsonProperty("Layouts", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<WeightedLayoutConfig> Layouts = new List<WeightedLayoutConfig>();
        }

        private class WeightedLayoutConfig
        {
            [JsonProperty("Layout Id")]
            public string LayoutId;

            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Weight")]
            public float Weight = 1f;
        }

        private class AutoSpawnConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = false;

            [JsonProperty("IntervalMinutes")]
            public float IntervalMinutes = 120f;

            [JsonProperty("JitterMinutes")]
            public float JitterMinutes = 30f;

            [JsonProperty("MinOnlinePlayers")]
            public int MinOnlinePlayers = 4;

            [JsonProperty("MaxActiveRaidBases")]
            public int MaxActiveRaidBases = 1;

            [JsonProperty("CooldownMinutesAfterRun")]
            public float CooldownMinutesAfterRun = 60f;
        }

        private class LayoutRotationConfig
        {
            [JsonProperty("EnabledLayouts", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> EnabledLayouts = new List<string>();

            [JsonProperty("IgnoredLayouts", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> IgnoredLayouts = new List<string>
            {
                "raidlands_portafort",
                "raidlands_portafort_test"
            };

            [JsonProperty("PublicDisplayName")]
            public string PublicDisplayName = "Public Raid Base";
        }

        private class LocationRulesConfig
        {
            [JsonProperty("MinDistanceFromMapEdge")]
            public float MinDistanceFromMapEdge = 100f;

            [JsonProperty("BlockWater")]
            public bool BlockWater = true;

            [JsonProperty("WaterClearance")]
            public float WaterClearance = 1.5f;

            [JsonProperty("BlockSafeZones")]
            public bool BlockSafeZones = true;

            [JsonProperty("BlockNoBuildZones")]
            public bool BlockNoBuildZones = true;

            [JsonProperty("BlockMonuments")]
            public bool BlockMonuments = true;

            [JsonProperty("MonumentRadiusPadding")]
            public float MonumentRadiusPadding = 50f;

            [JsonProperty("DefaultMonumentRadius")]
            public float DefaultMonumentRadius = 80f;

            [JsonProperty("BlockRoads")]
            public bool BlockRoads = true;

            [JsonProperty("BlockPlayerBases")]
            public bool BlockPlayerBases = true;

            [JsonProperty("PlayerBaseRadius")]
            public float PlayerBaseRadius = 95f;

            [JsonProperty("MinimumDistanceBetweenEvents")]
            public float MinimumDistanceBetweenEvents = 200f;

            [JsonProperty("MaxSlope")]
            public float MaxSlope = 0.45f;

            [JsonProperty("FlatnessSampleRadius")]
            public float FlatnessSampleRadius = 18f;

            [JsonProperty("MaxFlatnessDelta")]
            public float MaxFlatnessDelta = 5f;

            [JsonProperty("Footprint Clearance Padding")]
            public float FootprintClearancePadding = 6f;

            [JsonProperty("Search Progress Log Interval Seconds")]
            public float SearchProgressLogIntervalSeconds = 60f;
        }

        private class SpawnGridConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Cell Size")]
            public float CellSize = 12.5f;

            [JsonProperty("Build Budget Milliseconds")]
            public float BuildBudgetMilliseconds = 1f;

            [JsonProperty("Candidate Checks Per Slice")]
            public int CandidateChecksPerSlice = 25;

            [JsonProperty("Temporary Rejection Retry Seconds")]
            public float TemporaryRejectionRetrySeconds = 60f;

            [JsonProperty("Persist Cache")]
            public bool PersistCache = true;

            [JsonProperty("Minimum Healthy Candidate Count")]
            public int MinimumHealthyCandidateCount = 1000;
        }

        private class PasteConfig
        {
            [JsonProperty("CopyPaste Arguments")]
            public string[] CopyPasteArguments =
            {
                "deployables", "true",
                "inventories", "true",
                "auth", "false",
                "entityowner", "false",
                "autoheight", "false",
                "height", "0",
                "blockcollision", "0",
                "stability", "false",
                "enablesaving", "false"
            };

            [JsonProperty("RandomRotationDegreesStep")]
            public float RandomRotationDegreesStep = 90f;

            [JsonProperty("GroundClearance")]
            public float GroundClearance = 0.25f;

            [JsonProperty("Adaptive Foundations")]
            public AdaptiveFoundationsConfig AdaptiveFoundations = new AdaptiveFoundationsConfig();

            [JsonProperty("Force Pasted Turrets Attack All")]
            public bool ForcePastedTurretsAttackAll = true;

            [JsonProperty("Pasted Turret Attack All Reapply Delays Seconds")]
            public float[] PastedTurretAttackAllReapplyDelaysSeconds = { 0.1f, 0.5f, 1.5f, 3f };

            [JsonProperty("Pasted Turret Survival Audit Delays Seconds")]
            public float[] PastedTurretSurvivalAuditDelaysSeconds = { 0.25f, 1.5f, 5f };
        }

        private class AdaptiveFoundationsConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Exposure Threshold Meters")]
            public float ExposureThresholdMeters = 0.75f;

            [JsonProperty("Maximum Foundation Embed Meters")]
            public float MaximumFoundationEmbedMeters = 0.25f;

            [JsonProperty("Maximum Foundation Clearance Meters")]
            public float MaximumFoundationClearanceMeters = 1.25f;

            [JsonProperty("Maximum Origin Adjustment Meters")]
            public float MaximumOriginAdjustmentMeters = 0.75f;

            [JsonProperty("Maximum Lowering Meters")]
            public float MaximumLoweringMeters = 6f;

            [JsonProperty("Raise Base Layer Above Water")]
            public bool RaiseBaseLayerAboveWater = true;

            [JsonProperty("Water Surface Clearance Meters")]
            public float WaterSurfaceClearanceMeters = 0.25f;

            [JsonProperty("Maximum Water Depth Meters")]
            public float MaximumWaterDepthMeters = 3f;

            [JsonProperty("Stability Audit Delay Seconds")]
            public float StabilityAuditDelaySeconds = 1f;
        }

        private class MapMarkerConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("RadiusMeters")]
            public float RadiusMeters = 90f;

            [JsonProperty("Alpha")]
            public float Alpha = 0.65f;

            [JsonProperty("Color1")]
            public string Color1 = "#ce422b";

            [JsonProperty("Color2")]
            public string Color2 = "#f5c542";
        }

        private class ScoringConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Score Radius Meters")]
            public float ScoreRadiusMeters = 120f;

            [JsonProperty("Use Layout Center For Score Radius")]
            public bool UseLayoutCenterForScoreRadius = true;

            [JsonProperty("Require Attacker And Victim Inside Radius")]
            public bool RequireAttackerAndVictimInsideRadius = true;

            [JsonProperty("Player Kill Points")]
            public int PlayerKillPoints = 100;

            [JsonProperty("Player Damage Points Per 100 Damage")]
            public float PlayerDamagePointsPer100Damage = 5f;

            [JsonProperty("Event Entity Damage Points Per 100 Damage")]
            public float EventEntityDamagePointsPer100Damage = 1f;

            [JsonProperty("Explosive Event Entity Damage Bonus Points Per 100 Damage")]
            public float ExplosiveEventEntityDamageBonusPointsPer100Damage = 4f;

            [JsonProperty("Tool Cupboard Destroyed Points")]
            public int ToolCupboardDestroyedPoints = 1000;

            [JsonProperty("Minimum Score To Qualify")]
            public int MinimumScoreToQualify = 250;

            [JsonProperty("Max Leaderboard Entries")]
            public int MaxLeaderboardEntries = 5;

            [JsonProperty("Announce Leaderboard On Completion")]
            public bool AnnounceLeaderboardOnCompletion = true;

            [JsonProperty("Ignore Same Clan PVP")]
            public bool IgnoreSameClanPvp = true;

            [JsonProperty("Ignore Allied Clan PVP")]
            public bool IgnoreAlliedClanPvp = true;

            [JsonProperty("Ignore Same Rust Team PVP")]
            public bool IgnoreSameRustTeamPvp = true;

            [JsonProperty("Ignore Sleeping Victims")]
            public bool IgnoreSleepingVictims = true;

            [JsonProperty("Repeat Victim Window Seconds")]
            public float RepeatVictimWindowSeconds = 180f;

            [JsonProperty("Repeat Victim Kill Multiplier")]
            public float RepeatVictimKillMultiplier = 0.25f;

            [JsonProperty("Maximum Player Damage Points Per Victim Per Minute")]
            public int MaximumPlayerDamagePointsPerVictimPerMinute = 50;
        }

        private class RewardsConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = false;

            [JsonProperty("Automatic Event Payouts Enabled")]
            public bool AutomaticEventPayoutsEnabled = true;

            [JsonProperty("Admin Event Payouts Enabled")]
            public bool AdminEventPayoutsEnabled = false;

            [JsonProperty("Automatic Default Profile Id")]
            public string AutomaticDefaultProfileId = DefaultRewardProfileId;

            [JsonProperty("Admin Default Profile Id")]
            public string AdminDefaultProfileId = DefaultRewardProfileId;

            [JsonProperty("Layout Profile Overrides")]
            public Dictionary<string, string> LayoutProfileOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Allowed Command Prefixes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AllowedCommandPrefixes = new List<string>();

            [JsonProperty("Tell Players About Rewards")]
            public bool TellPlayersAboutRpRewards = true;

            [JsonProperty("Queue Failed Rewards")]
            public bool QueueRewardsIfServerRewardsMissing = true;

            // Legacy fields are accepted for one-way profile migration but are no longer written.
            [JsonProperty("Award ServerRewards RP")]
            public bool AwardServerRewardsRp = false;

            [JsonProperty("Placement RP Rewards", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PlacementRewardConfig> PlacementRpRewards = new List<PlacementRewardConfig>
            {
                new PlacementRewardConfig { Place = 1, ServerRewardsRp = 10000 },
                new PlacementRewardConfig { Place = 2, ServerRewardsRp = 5000 },
                new PlacementRewardConfig { Place = 3, ServerRewardsRp = 2500 }
            };

            public bool ShouldSerializeAwardServerRewardsRp() => false;
            public bool ShouldSerializePlacementRpRewards() => false;
        }

        private class PlacementRewardConfig
        {
            [JsonProperty("Place")]
            public int Place;

            [JsonProperty("ServerRewards RP")]
            public int ServerRewardsRp;
        }

        private class LeaderboardConfig
        {
            [JsonProperty("Wipe Key")]
            public string WipeKey = "";

            [JsonProperty("Maximum Detailed History Entries")]
            public int MaximumDetailedHistoryEntries = 10000;

            [JsonProperty("Maximum API Page Size")]
            public int MaximumApiPageSize = 100;

            [JsonProperty("Season Placement Points", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<SeasonPlacementPoints> SeasonPlacementPoints = new List<SeasonPlacementPoints>
            {
                new SeasonPlacementPoints { Place = 1, Points = 10 },
                new SeasonPlacementPoints { Place = 2, Points = 6 },
                new SeasonPlacementPoints { Place = 3, Points = 4 }
            };
        }

        private class SeasonPlacementPoints
        {
            [JsonProperty("Place")]
            public int Place;

            [JsonProperty("Points")]
            public int Points;
        }

        private class RewardProfile
        {
            [JsonProperty("SchemaVersion")]
            public int SchemaVersion = RewardProfileSchemaVersion;

            [JsonProperty("Id")]
            public string Id = DefaultRewardProfileId;

            [JsonProperty("DisplayName")]
            public string DisplayName = "Default Raid Base Rewards";

            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("RewardMode")]
            public string RewardMode = "FixedPlacements";

            [JsonProperty("ScoreScope")]
            public string ScoreScope = "Clan";

            [JsonProperty("AllowSoloIfNoGroup")]
            public bool AllowSoloIfNoGroup = true;

            [JsonProperty("GroupDistribution")]
            public string GroupDistribution = "ContributionWeighted";

            [JsonProperty("MinimumGroupScore")]
            public int MinimumGroupScore = 250;

            [JsonProperty("MinimumMemberScore")]
            public int MinimumMemberScore = 1;

            [JsonProperty("Placements", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<RewardPlacementDefinition> Placements = new List<RewardPlacementDefinition>();

            [JsonProperty("Pool", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<RewardDefinition> Pool = new List<RewardDefinition>();
        }

        private class RewardPlacementDefinition
        {
            [JsonProperty("Place")]
            public int Place;

            [JsonProperty("Percent")]
            public float Percent;

            [JsonProperty("Rewards", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<RewardDefinition> Rewards = new List<RewardDefinition>();
        }

        private class RewardDefinition
        {
            [JsonProperty("Type")]
            public string Type = "RP";

            [JsonProperty("Amount")]
            public int Amount;

            [JsonProperty("ShortName")]
            public string ShortName;

            [JsonProperty("SkinId")]
            public ulong SkinId;

            [JsonProperty("Command")]
            public string Command;

            [JsonProperty("RequireOnline")]
            public bool RequireOnline;
        }

        private class CleanupConfig
        {
            [JsonProperty("Remove Markers")]
            public bool RemoveMarkers = true;

            [JsonProperty("Despawn Pasted Entities")]
            public bool DespawnPastedEntities = true;

            [JsonProperty("Cleanup On Unload")]
            public bool CleanupOnUnload = true;

            [JsonProperty("Completion Cleanup Delay Seconds")]
            public float CompletionCleanupDelaySeconds = 60f;

            [JsonProperty("Forced Cleanup Timeout Seconds")]
            public float ForcedCleanupTimeoutSeconds = 10800f;
        }

        private class StoredData
        {
            [JsonProperty("Layouts")]
            public Dictionary<string, LayoutScanEntry> Layouts = new Dictionary<string, LayoutScanEntry>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("ActiveRaidBases")]
            public Dictionary<string, ActiveRaidBase> ActiveRaidBases = new Dictionary<string, ActiveRaidBase>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("LastRunUnix")]
            public double LastRunUnix;

            [JsonProperty("NextAutoAttemptUnix")]
            public double NextAutoAttemptUnix;

            [JsonProperty("PendingAutomaticSpawnRequests")]
            public int PendingAutomaticSpawnRequests;

            [JsonProperty("PendingRewards")]
            public Dictionary<string, PendingRewardRecord> PendingRewards = new Dictionary<string, PendingRewardRecord>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("PendingPurchaseRefunds")]
            public Dictionary<string, PendingPurchaseRefundRecord> PendingPurchaseRefunds = new Dictionary<string, PendingPurchaseRefundRecord>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("LayoutLootOverrides")]
            public Dictionary<string, Dictionary<string, ContainerLootOverride>> LayoutLootOverrides = new Dictionary<string, Dictionary<string, ContainerLootOverride>>(StringComparer.OrdinalIgnoreCase);

            public bool ShouldSerializePendingRewards() => PendingRewards != null && PendingRewards.Count > 0;
        }

        private class ContainerLootOverride
        {
            [JsonProperty("Prefab")]
            public string Prefab;

            [JsonProperty("LocalPosition")]
            public StoredVector3 LocalPosition = new StoredVector3();

            [JsonProperty("Items")]
            public List<LootItemEntry> Items = new List<LootItemEntry>();

            [JsonProperty("UpdatedBy")]
            public string UpdatedBy;

            [JsonProperty("UpdatedUnix")]
            public double UpdatedUnix;
        }

        private class LootItemEntry
        {
            [JsonProperty("ShortName")]
            public string ShortName;

            [JsonProperty("Amount")]
            public int Amount = 1;

            [JsonProperty("Skin")]
            public ulong Skin;

            [JsonProperty("Position")]
            public int Position;
        }

        private class LayoutContainerDescriptor
        {
            public string Fingerprint;
            public string Prefab;
            public string Label;
            public Vector3 LocalPosition;
            public int Capacity;
            public List<LootItemEntry> CopiedItems = new List<LootItemEntry>();
        }

        private class LootEditorState
        {
            public string LayoutId;
            public string ContainerFingerprint;
            public int ContainerPage;
            public int ItemPage;
            public int SlotPage;
            public string Search = string.Empty;
            public int SelectedSlot = -1;
            public List<LootItemEntry> DraftItems = new List<LootItemEntry>();
            public bool DraftLoaded;
        }

        private class EventSanitizeResult
        {
            public int Entities;
            public int Cupboards;
            public int Turrets;
            public int Locks;
            public int Sams;
            public int Traps;
            public int RemovedSteamIds;
        }

        private class LayoutScanEntry
        {
            [JsonProperty("LayoutId")]
            public string LayoutId;

            [JsonProperty("FileName")]
            public string FileName;

            [JsonProperty("Ignored")]
            public bool Ignored;

            [JsonProperty("Valid")]
            public bool Valid;

            [JsonProperty("EntityCount")]
            public int EntityCount;

            [JsonProperty("AutoTurretCount")]
            public int AutoTurretCount;

            [JsonProperty("HasToolCupboard")]
            public bool HasToolCupboard;

            [JsonProperty("HasCrateLikeEntity")]
            public bool HasCrateLikeEntity;

            [JsonProperty("ValidationErrors")]
            public List<string> ValidationErrors = new List<string>();

            [JsonProperty("BoundsMin")]
            public StoredVector3 BoundsMin = new StoredVector3();

            [JsonProperty("BoundsMax")]
            public StoredVector3 BoundsMax = new StoredVector3();

            [JsonProperty("GroundAnchorY")]
            public float GroundAnchorY;

            [JsonProperty("GroundFootprintRadius")]
            public float GroundFootprintRadius;

            [JsonProperty("GroundFootprintCells")]
            public List<GroundFootprintCell> GroundFootprintCells = new List<GroundFootprintCell>();

            [JsonProperty("LastScannedUnix")]
            public double LastScannedUnix;
        }

        private class ActiveRaidBase
        {
            [JsonProperty("InstanceId")]
            public string InstanceId;

            [JsonProperty("LayoutId")]
            public string LayoutId;

            [JsonProperty("PublicName")]
            public string PublicName;

            [JsonProperty("Position")]
            public StoredVector3 Position = new StoredVector3();

            [JsonProperty("RotationDegrees")]
            public float RotationDegrees;

            [JsonProperty("StartedUnix")]
            public double StartedUnix;

            [JsonProperty("ExpiresUnix")]
            public double ExpiresUnix;

            [JsonProperty("EventTypeId")]
            public string EventTypeId = "raid-base";

            [JsonProperty("ProviderType")]
            public string ProviderType = "CopyPaste";

            [JsonProperty("IsAnnounced")]
            public bool IsAnnounced = true;

            [JsonProperty("Status")]
            public string Status = "pasting";

            [JsonProperty("EntityIds")]
            public List<ulong> EntityIds = new List<ulong>();

            [JsonProperty("ToolCupboardId")]
            public ulong ToolCupboardId;

            [JsonProperty("HadToolCupboardInLayout")]
            public bool HadToolCupboardInLayout;

            [JsonProperty("ScoreRadiusMeters")]
            public float ScoreRadiusMeters;

            [JsonProperty("Scores")]
            public Dictionary<string, RaidBaseScoreEntry> Scores = new Dictionary<string, RaidBaseScoreEntry>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("CompletedUnix")]
            public double CompletedUnix;

            [JsonProperty("CompletedReason")]
            public string CompletedReason;

            [JsonProperty("RewardsProcessed")]
            public bool RewardsProcessed;

            [JsonProperty("PaidRewards")]
            public List<PaidRaidBaseReward> PaidRewards = new List<PaidRaidBaseReward>();

            [JsonProperty("RewardProfileId")]
            public string RewardProfileId;

            [JsonProperty("RewardProfileHash")]
            public string RewardProfileHash;

            [JsonProperty("RewardProfileSnapshot")]
            public RewardProfile RewardProfileSnapshot;

            [JsonProperty("RewardPayoutEnabled")]
            public bool RewardPayoutEnabled;

            [JsonProperty("RewardProfileError")]
            public string RewardProfileError;

            [JsonProperty("ResultCommitted")]
            public bool ResultCommitted;

            [JsonProperty("PvpVictimStates")]
            public Dictionary<string, PvpVictimState> PvpVictimStates = new Dictionary<string, PvpVictimState>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("TriggerType")]
            public string TriggerType = "admin";

            [JsonProperty("PurchaserUserId")]
            public string PurchaserUserId;

            [JsonProperty("PurchaserDisplayName")]
            public string PurchaserDisplayName;

            [JsonProperty("PurchaseCostSummary")]
            public string PurchaseCostSummary;

            [JsonProperty("PurchaseCostsPaid")]
            public List<PurchaseCostRecord> PurchaseCostsPaid = new List<PurchaseCostRecord>();

            [JsonProperty("PurchaseRefunded")]
            public bool PurchaseRefunded;

            [JsonProperty("PurchaseRefundError")]
            public string PurchaseRefundError;

            [JsonProperty("SpawnGridCandidateIndex")]
            public int SpawnGridCandidateIndex = -1;

            [JsonProperty("Adaptive Foundations Adjusted")]
            public int AdaptiveFoundationsAdjusted;

            [JsonProperty("Adaptive Generated Foundations")]
            public int AdaptiveGeneratedFoundations;

            [JsonProperty("Adaptive Generated Cap Floors")]
            public int AdaptiveGeneratedCapFloors;

            [JsonProperty("Adaptive Generated Full Walls")]
            public int AdaptiveGeneratedFullWalls;

            [JsonProperty("Adaptive Generated Half Walls")]
            public int AdaptiveGeneratedHalfWalls;

            [JsonProperty("Adaptive Maximum Lowering Meters")]
            public float AdaptiveMaximumLoweringMeters;

            [JsonProperty("Adaptive Origin Vertical Adjustment Meters")]
            public float AdaptiveOriginVerticalAdjustmentMeters;

            [JsonProperty("Adaptive Naturally Seated Foundations")]
            public int AdaptiveNaturallySeatedFoundations;

            [JsonProperty("Adaptive Water Supported Foundations")]
            public int AdaptiveWaterSupportedFoundations;

            [JsonProperty("Adaptive Maximum Water Depth Meters")]
            public float AdaptiveMaximumWaterDepthMeters;
        }

        private class GroundFootprintCell
        {
            [JsonProperty("Is Foundation")]
            public bool IsFoundation;

            [JsonProperty("Position")]
            public StoredVector3 Position = new StoredVector3();

            [JsonProperty("Radius")]
            public float Radius = 1.75f;

            [JsonProperty("Rotation Degrees")]
            public float RotationDegrees;

            [JsonProperty("Half Width")]
            public float HalfWidth = 1.5f;

            [JsonProperty("Half Depth")]
            public float HalfDepth = 1.5f;
        }

        private class SpawnGridCache
        {
            [JsonProperty("SchemaVersion")]
            public int SchemaVersion = SpawnGridSchemaVersion;

            [JsonProperty("ProtocolSave")]
            public int ProtocolSave;

            [JsonProperty("WorldSize")]
            public uint WorldSize;

            [JsonProperty("WorldSeed")]
            public uint WorldSeed;

            [JsonProperty("LevelUrl")]
            public string LevelUrl;

            [JsonProperty("RulesFingerprint")]
            public string RulesFingerprint;

            [JsonProperty("GeneratedUnix")]
            public double GeneratedUnix;

            [JsonProperty("Scanned Point Count")]
            public int ScannedPointCount;

            [JsonProperty("Candidates")]
            public List<StoredVector3> Candidates = new List<StoredVector3>();

            [JsonProperty("Static Rejections")]
            public Dictionary<string, long> StaticRejections = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        private class LayoutPlacementStats
        {
            public long Attempts;
            public long Successes;
            public string LastRejection;
        }

        private class RaidBaseScoreEntry
        {
            [JsonProperty("UserId")]
            public string UserId;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("PlayerKills")]
            public int PlayerKills;

            [JsonProperty("PlayerDeaths")]
            public int PlayerDeaths;

            [JsonProperty("DamageToPlayers")]
            public float DamageToPlayers;

            [JsonProperty("DamageToEventEntities")]
            public float DamageToEventEntities;

            [JsonProperty("ExplosiveDamageToEventEntities")]
            public float ExplosiveDamageToEventEntities;

            [JsonProperty("ToolCupboardsDestroyed")]
            public int ToolCupboardsDestroyed;

            [JsonProperty("TotalScore")]
            public int TotalScore;

            [JsonProperty("LastScoreUnix")]
            public double LastScoreUnix;

            [JsonProperty("FirstScoreUnix")]
            public double FirstScoreUnix;

            [JsonProperty("FirstClanId")]
            public string FirstClanId;

            [JsonProperty("CurrentClanId")]
            public string CurrentClanId;

            [JsonProperty("FirstTeamId")]
            public string FirstTeamId;

            [JsonProperty("CurrentTeamId")]
            public string CurrentTeamId;
        }

        private class PvpVictimState
        {
            [JsonProperty("DamageWindowStartedUnix")]
            public double DamageWindowStartedUnix;

            [JsonProperty("DamagePointsAwarded")]
            public int DamagePointsAwarded;

            [JsonProperty("LastKillUnix")]
            public double LastKillUnix;

            [JsonProperty("RepeatKills")]
            public int RepeatKills;
        }

        private class PaidRaidBaseReward
        {
            [JsonProperty("UserId")]
            public string UserId;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("Place")]
            public int Place;

            [JsonProperty("ServerRewardsRp")]
            public int ServerRewardsRp;

            [JsonProperty("Status")]
            public string Status;

            [JsonProperty("Error")]
            public string Error;

            [JsonProperty("TimestampUnix")]
            public double TimestampUnix;
        }

        private class PendingRewardRecord
        {
            [JsonProperty("RewardId")]
            public string RewardId;

            [JsonProperty("InstanceId")]
            public string InstanceId;

            [JsonProperty("UserId")]
            public string UserId;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("Place")]
            public int Place;

            [JsonProperty("ServerRewardsRp")]
            public int ServerRewardsRp;

            [JsonProperty("CreatedUnix")]
            public double CreatedUnix;

            [JsonProperty("LastAttemptUnix")]
            public double LastAttemptUnix;

            [JsonProperty("AttemptCount")]
            public int AttemptCount;

            [JsonProperty("LastError")]
            public string LastError;
        }

        private class LeaderboardStore
        {
            [JsonProperty("SchemaVersion")]
            public int SchemaVersion = EventDataSchemaVersion;

            [JsonProperty("ServerId")]
            public string ServerId;

            [JsonProperty("CurrentWipeKey")]
            public string CurrentWipeKey;

            [JsonProperty("CurrentWipeStartedAtUtc")]
            public string CurrentWipeStartedAtUtc;

            [JsonProperty("CurrentWipe")]
            public LeaderboardPeriod CurrentWipe = new LeaderboardPeriod();

            [JsonProperty("Lifetime")]
            public LeaderboardPeriod Lifetime = new LeaderboardPeriod();
        }

        private class LeaderboardPeriod
        {
            [JsonProperty("Players")]
            public Dictionary<string, LeaderboardAggregate> Players = new Dictionary<string, LeaderboardAggregate>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Clans")]
            public Dictionary<string, LeaderboardAggregate> Clans = new Dictionary<string, LeaderboardAggregate>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Teams")]
            public Dictionary<string, LeaderboardAggregate> Teams = new Dictionary<string, LeaderboardAggregate>(StringComparer.OrdinalIgnoreCase);
        }

        private class LeaderboardAggregate
        {
            [JsonProperty("Id")]
            public string Id;

            [JsonProperty("Scope")]
            public string Scope;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("EventsEntered")]
            public int EventsEntered;

            [JsonProperty("EventsQualified")]
            public int EventsQualified;

            [JsonProperty("SeasonPoints")]
            public int SeasonPoints;

            [JsonProperty("FirstPlaces")]
            public int FirstPlaces;

            [JsonProperty("SecondPlaces")]
            public int SecondPlaces;

            [JsonProperty("ThirdPlaces")]
            public int ThirdPlaces;

            [JsonProperty("TotalScore")]
            public long TotalScore;

            [JsonProperty("PlayerKills")]
            public int PlayerKills;

            [JsonProperty("PlayerDeaths")]
            public int PlayerDeaths;

            [JsonProperty("DamageToPlayers")]
            public double DamageToPlayers;

            [JsonProperty("DamageToEventEntities")]
            public double DamageToEventEntities;

            [JsonProperty("ExplosiveDamageToEventEntities")]
            public double ExplosiveDamageToEventEntities;

            [JsonProperty("ToolCupboardsDestroyed")]
            public int ToolCupboardsDestroyed;

            [JsonProperty("RpPaid")]
            public long RpPaid;

            [JsonProperty("ItemUnitsPaid")]
            public long ItemUnitsPaid;

            [JsonProperty("CommandsPaid")]
            public int CommandsPaid;

            [JsonProperty("LastQualifiedUnix")]
            public double LastQualifiedUnix;
        }

        private class EventHistoryStore
        {
            [JsonProperty("SchemaVersion")]
            public int SchemaVersion = EventDataSchemaVersion;

            [JsonProperty("CurrentWipeKey")]
            public string CurrentWipeKey;

            [JsonProperty("Results")]
            public List<RaidBaseEventResult> Results = new List<RaidBaseEventResult>();
        }

        private class RaidBaseEventResult
        {
            [JsonProperty("InstanceId")]
            public string InstanceId;

            [JsonProperty("EventTypeId")]
            public string EventTypeId;

            [JsonProperty("LayoutId")]
            public string LayoutId;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("TriggerType")]
            public string TriggerType;

            [JsonProperty("State")]
            public string State;

            [JsonProperty("StartedUnix")]
            public double StartedUnix;

            [JsonProperty("EndedUnix")]
            public double EndedUnix;

            [JsonProperty("EndReason")]
            public string EndReason;

            [JsonProperty("Position")]
            public StoredVector3 Position = new StoredVector3();

            [JsonProperty("RewardProfileId")]
            public string RewardProfileId;

            [JsonProperty("RewardProfileHash")]
            public string RewardProfileHash;

            [JsonProperty("RewardProfileSnapshot")]
            public RewardProfile RewardProfileSnapshot;

            [JsonProperty("PlayerStandings")]
            public List<EventStanding> PlayerStandings = new List<EventStanding>();

            [JsonProperty("ClanStandings")]
            public List<EventStanding> ClanStandings = new List<EventStanding>();

            [JsonProperty("TeamStandings")]
            public List<EventStanding> TeamStandings = new List<EventStanding>();

            [JsonProperty("RewardTransactions")]
            public List<RewardTransaction> RewardTransactions = new List<RewardTransaction>();
        }

        private class EventStanding
        {
            [JsonProperty("Rank")]
            public int Rank;

            [JsonProperty("Scope")]
            public string Scope;

            [JsonProperty("Id")]
            public string Id;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("Score")]
            public int Score;

            [JsonProperty("PlayerKills")]
            public int PlayerKills;

            [JsonProperty("PlayerDeaths")]
            public int PlayerDeaths;

            [JsonProperty("DamageToPlayers")]
            public float DamageToPlayers;

            [JsonProperty("DamageToEventEntities")]
            public float DamageToEventEntities;

            [JsonProperty("ExplosiveDamageToEventEntities")]
            public float ExplosiveDamageToEventEntities;

            [JsonProperty("ToolCupboardsDestroyed")]
            public int ToolCupboardsDestroyed;

            [JsonProperty("Members")]
            public List<EventStandingMember> Members = new List<EventStandingMember>();
        }

        private class EventStandingMember
        {
            [JsonProperty("UserId")]
            public string UserId;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("Score")]
            public int Score;
        }

        private class AllocationCandidate
        {
            public string UserId;
            public string DisplayName;
            public int Score;
            public double Exact;
            public int Amount;
            public double Remainder;
        }

        private class RewardLedgerStore
        {
            [JsonProperty("SchemaVersion")]
            public int SchemaVersion = EventDataSchemaVersion;

            [JsonProperty("Transactions")]
            public Dictionary<string, RewardTransaction> Transactions = new Dictionary<string, RewardTransaction>(StringComparer.OrdinalIgnoreCase);
        }

        private class RewardTransaction
        {
            [JsonProperty("TransactionId")]
            public string TransactionId;

            [JsonProperty("WipeKey")]
            public string WipeKey;

            [JsonProperty("InstanceId")]
            public string InstanceId;

            [JsonProperty("ProfileId")]
            public string ProfileId;

            [JsonProperty("Place")]
            public int Place;

            [JsonProperty("GroupScope")]
            public string GroupScope;

            [JsonProperty("GroupId")]
            public string GroupId;

            [JsonProperty("UserId")]
            public string UserId;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("Type")]
            public string Type;

            [JsonProperty("Amount")]
            public int Amount;

            [JsonProperty("ShortName")]
            public string ShortName;

            [JsonProperty("SkinId")]
            public ulong SkinId;

            [JsonProperty("Command")]
            public string Command;

            [JsonProperty("RequireOnline")]
            public bool RequireOnline;

            [JsonProperty("Status")]
            public string Status = "pending";

            [JsonProperty("CreatedUnix")]
            public double CreatedUnix;

            [JsonProperty("UpdatedUnix")]
            public double UpdatedUnix;

            [JsonProperty("AttemptCount")]
            public int AttemptCount;

            [JsonProperty("LastError")]
            public string LastError;

            [JsonProperty("PaidAggregateApplied")]
            public bool PaidAggregateApplied;
        }

        private class PurchaseCostRecord
        {
            [JsonProperty("Type")]
            public string Type;

            [JsonProperty("ShortName")]
            public string ShortName;

            [JsonProperty("Amount")]
            public int Amount;

            [JsonProperty("DisplayName")]
            public string DisplayName;
        }

        private class PendingPurchaseRefundRecord
        {
            [JsonProperty("RefundId")]
            public string RefundId;

            [JsonProperty("InstanceId")]
            public string InstanceId;

            [JsonProperty("UserId")]
            public string UserId;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("Costs")]
            public List<PurchaseCostRecord> Costs = new List<PurchaseCostRecord>();

            [JsonProperty("Reason")]
            public string Reason;

            [JsonProperty("CreatedUnix")]
            public double CreatedUnix;

            [JsonProperty("LastAttemptUnix")]
            public double LastAttemptUnix;

            [JsonProperty("AttemptCount")]
            public int AttemptCount;

            [JsonProperty("LastError")]
            public string LastError;
        }

        private class StoredVector3
        {
            [JsonProperty("x")]
            public float X;

            [JsonProperty("y")]
            public float Y;

            [JsonProperty("z")]
            public float Z;

            public StoredVector3()
            {
            }

            public StoredVector3(Vector3 vector)
            {
                X = vector.x;
                Y = vector.y;
                Z = vector.z;
            }

            public Vector3 ToVector3()
            {
                return new Vector3(X, Y, Z);
            }
        }

        private class MonumentZone
        {
            public Vector3 Center;
            public float Radius;
            public string Name;
        }

        private enum AutomaticSearchStage
        {
            Candidate,
            Terrain,
            SafeZone,
            PlayerBases,
            Obstacles,
            Paste
        }

        private class AutomaticLocationSearch
        {
            public double StartedUnix;
            public LayoutScanEntry Layout;
            public float RotationDegrees;
            public Vector3 PasteOrigin;
            public Vector3 FootprintCenter;
            public Vector3 FootprintHalfExtents;
            public Quaternion Rotation;
            public List<Vector3> Samples = new List<Vector3>();
            public int SampleIndex;
            public float MinimumTerrainHeight = float.MaxValue;
            public float MaximumTerrainHeight = float.MinValue;
            public AutomaticSearchStage Stage = AutomaticSearchStage.Candidate;
            public int CandidateIndex = -1;
            public int CandidateScanStart = -1;
            public int CandidateScanVisited;
            public HashSet<string> ExhaustedLayoutRotations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int CandidateRejectionsForCombination;
            public double CombinationRetryNotBeforeUnix;
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>() ?? new Configuration();
            }
            catch
            {
                PrintWarning("Could not read config; creating a new default config.");
                config = new Configuration();
            }

            NormalizeConfig();
            SaveConfig();
        }

        private void NormalizeConfig()
        {
            var defaults = new Configuration();
            if (config.EventTypes == null) config.EventTypes = defaults.EventTypes;
            if (config.EventTypes.AutomaticBases == null) config.EventTypes.AutomaticBases = defaults.EventTypes.AutomaticBases;
            if (config.AutoSpawn == null) config.AutoSpawn = defaults.AutoSpawn;
            if (config.LayoutRotation == null) config.LayoutRotation = defaults.LayoutRotation;
            if (config.LocationRules == null) config.LocationRules = defaults.LocationRules;
            if (config.SpawnGrid == null) config.SpawnGrid = defaults.SpawnGrid;
            if (config.Paste == null) config.Paste = defaults.Paste;
            if (config.MapMarker == null) config.MapMarker = defaults.MapMarker;
            if (config.Scoring == null) config.Scoring = defaults.Scoring;
            if (config.Rewards == null) config.Rewards = defaults.Rewards;
            if (config.Leaderboard == null) config.Leaderboard = defaults.Leaderboard;
            if (config.Cleanup == null) config.Cleanup = defaults.Cleanup;
            if (string.IsNullOrWhiteSpace(config.ServerId)) config.ServerId = defaults.ServerId;
            config.ServerId = CleanStableId(config.ServerId, defaults.ServerId);
            if (string.IsNullOrWhiteSpace(config.ChatPrefix)) config.ChatPrefix = defaults.ChatPrefix;

            var automaticBases = config.EventTypes.AutomaticBases;
            if (string.IsNullOrWhiteSpace(automaticBases.DisplayName)) automaticBases.DisplayName = defaults.EventTypes.AutomaticBases.DisplayName;
            automaticBases.CheckFrequencyMinutes = Mathf.Max(1f, automaticBases.CheckFrequencyMinutes);
            automaticBases.MinimumActiveBases = Math.Max(0, automaticBases.MinimumActiveBases);
            automaticBases.MaximumActiveBases = Math.Max(1, automaticBases.MaximumActiveBases);
            automaticBases.MinimumActiveBases = Math.Min(automaticBases.MinimumActiveBases, automaticBases.MaximumActiveBases);
            automaticBases.MaximumSpawnsPerCheck = Mathf.Clamp(automaticBases.MaximumSpawnsPerCheck, 1, automaticBases.MaximumActiveBases);
            automaticBases.MinimumOnlinePlayers = Math.Max(0, automaticBases.MinimumOnlinePlayers);
            automaticBases.HardLifetimeHours = Mathf.Clamp(automaticBases.HardLifetimeHours, 1f, 168f);
            automaticBases.PercentageToAnnounce = Mathf.Clamp(automaticBases.PercentageToAnnounce, 0f, 100f);
            if (automaticBases.Layouts == null) automaticBases.Layouts = new List<WeightedLayoutConfig>();
            automaticBases.Layouts = automaticBases.Layouts
                .Where(layout => layout != null && !string.IsNullOrWhiteSpace(layout.LayoutId))
                .Select(layout => new WeightedLayoutConfig
                {
                    LayoutId = layout.LayoutId.Trim(),
                    Enabled = layout.Enabled,
                    Weight = Mathf.Clamp(layout.Weight, 0.01f, 1000f)
                })
                .GroupBy(layout => layout.LayoutId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(layout => layout.LayoutId)
                .ToList();

            config.AutoSpawn.IntervalMinutes = Mathf.Max(1f, config.AutoSpawn.IntervalMinutes);
            config.AutoSpawn.JitterMinutes = Mathf.Max(0f, config.AutoSpawn.JitterMinutes);
            config.AutoSpawn.MinOnlinePlayers = Math.Max(0, config.AutoSpawn.MinOnlinePlayers);
            config.AutoSpawn.MaxActiveRaidBases = Math.Max(1, config.AutoSpawn.MaxActiveRaidBases);
            config.AutoSpawn.CooldownMinutesAfterRun = Mathf.Max(0f, config.AutoSpawn.CooldownMinutesAfterRun);

            if (config.LayoutRotation.EnabledLayouts == null)
                config.LayoutRotation.EnabledLayouts = new List<string>();
            if (config.LayoutRotation.IgnoredLayouts == null)
                config.LayoutRotation.IgnoredLayouts = defaults.LayoutRotation.IgnoredLayouts;
            if (string.IsNullOrWhiteSpace(config.LayoutRotation.PublicDisplayName))
                config.LayoutRotation.PublicDisplayName = defaults.LayoutRotation.PublicDisplayName;

            config.LocationRules.MinDistanceFromMapEdge = Mathf.Max(0f, config.LocationRules.MinDistanceFromMapEdge);
            config.LocationRules.WaterClearance = Mathf.Max(0f, config.LocationRules.WaterClearance);
            config.LocationRules.MonumentRadiusPadding = Mathf.Max(0f, config.LocationRules.MonumentRadiusPadding);
            config.LocationRules.DefaultMonumentRadius = Mathf.Max(1f, config.LocationRules.DefaultMonumentRadius);
            config.LocationRules.PlayerBaseRadius = Mathf.Max(1f, config.LocationRules.PlayerBaseRadius);
            config.LocationRules.MinimumDistanceBetweenEvents = Mathf.Max(0f, config.LocationRules.MinimumDistanceBetweenEvents);
            config.LocationRules.MaxSlope = Mathf.Clamp(config.LocationRules.MaxSlope, 0.01f, 2f);
            config.LocationRules.FlatnessSampleRadius = Mathf.Max(1f, config.LocationRules.FlatnessSampleRadius);
            config.LocationRules.MaxFlatnessDelta = Mathf.Max(0f, config.LocationRules.MaxFlatnessDelta);
            config.LocationRules.FootprintClearancePadding = Mathf.Clamp(config.LocationRules.FootprintClearancePadding, 0f, 50f);
            config.LocationRules.SearchProgressLogIntervalSeconds = Mathf.Clamp(config.LocationRules.SearchProgressLogIntervalSeconds, 10f, 1800f);

            config.SpawnGrid.CellSize = Mathf.Clamp(config.SpawnGrid.CellSize, 5f, 100f);
            config.SpawnGrid.BuildBudgetMilliseconds = Mathf.Clamp(config.SpawnGrid.BuildBudgetMilliseconds, 0.1f, 5f);
            config.SpawnGrid.CandidateChecksPerSlice = Mathf.Clamp(config.SpawnGrid.CandidateChecksPerSlice, 1, 250);
            config.SpawnGrid.TemporaryRejectionRetrySeconds = Mathf.Clamp(config.SpawnGrid.TemporaryRejectionRetrySeconds, 5f, 3600f);
            config.SpawnGrid.MinimumHealthyCandidateCount = Math.Max(0, config.SpawnGrid.MinimumHealthyCandidateCount);

            if (config.Paste.CopyPasteArguments == null)
                config.Paste.CopyPasteArguments = defaults.Paste.CopyPasteArguments;
            EnsureCopyPasteArgumentDefault("stability", "false");
            config.Paste.RandomRotationDegreesStep = Mathf.Max(0f, config.Paste.RandomRotationDegreesStep);
            config.Paste.GroundClearance = Mathf.Clamp(config.Paste.GroundClearance, 0f, 1.4f);
            if (config.Paste.AdaptiveFoundations == null)
                config.Paste.AdaptiveFoundations = defaults.Paste.AdaptiveFoundations;
            config.Paste.AdaptiveFoundations.ExposureThresholdMeters = Mathf.Clamp(
                config.Paste.AdaptiveFoundations.ExposureThresholdMeters, 0f, 1.5f);
            config.Paste.AdaptiveFoundations.MaximumFoundationClearanceMeters = Mathf.Clamp(
                config.Paste.AdaptiveFoundations.MaximumFoundationClearanceMeters,
                config.Paste.GroundClearance + 0.1f, 1.5f);
            var minimumFoundationEmbed = Math.Max(0f,
                1.5f - config.Paste.AdaptiveFoundations.MaximumFoundationClearanceMeters);
            config.Paste.AdaptiveFoundations.MaximumFoundationEmbedMeters = Mathf.Clamp(
                config.Paste.AdaptiveFoundations.MaximumFoundationEmbedMeters,
                minimumFoundationEmbed, 1.5f);
            config.Paste.AdaptiveFoundations.MaximumOriginAdjustmentMeters = Mathf.Clamp(
                config.Paste.AdaptiveFoundations.MaximumOriginAdjustmentMeters, 0f, 1.5f);
            config.Paste.AdaptiveFoundations.MaximumLoweringMeters = Mathf.Clamp(
                config.Paste.AdaptiveFoundations.MaximumLoweringMeters, 1.5f, 30f);
            config.Paste.AdaptiveFoundations.WaterSurfaceClearanceMeters = Mathf.Clamp(
                config.Paste.AdaptiveFoundations.WaterSurfaceClearanceMeters, 0.05f, 3f);
            config.Paste.AdaptiveFoundations.MaximumWaterDepthMeters = Mathf.Clamp(
                config.Paste.AdaptiveFoundations.MaximumWaterDepthMeters, 0.5f,
                config.Paste.AdaptiveFoundations.MaximumLoweringMeters);
            config.Paste.AdaptiveFoundations.StabilityAuditDelaySeconds = Mathf.Clamp(
                config.Paste.AdaptiveFoundations.StabilityAuditDelaySeconds, 0f, 10f);
            if (config.Paste.PastedTurretAttackAllReapplyDelaysSeconds == null || config.Paste.PastedTurretAttackAllReapplyDelaysSeconds.Length == 0)
                config.Paste.PastedTurretAttackAllReapplyDelaysSeconds = defaults.Paste.PastedTurretAttackAllReapplyDelaysSeconds;
            if (config.Paste.PastedTurretSurvivalAuditDelaysSeconds == null || config.Paste.PastedTurretSurvivalAuditDelaysSeconds.Length == 0)
                config.Paste.PastedTurretSurvivalAuditDelaysSeconds = defaults.Paste.PastedTurretSurvivalAuditDelaysSeconds;
            config.Paste.PastedTurretSurvivalAuditDelaysSeconds = config.Paste.PastedTurretSurvivalAuditDelaysSeconds
                .Where(delay => delay >= 0f)
                .Distinct()
                .OrderBy(delay => delay)
                .ToArray();

            config.MapMarker.RadiusMeters = Mathf.Clamp(config.MapMarker.RadiusMeters, 10f, 500f);
            config.MapMarker.Alpha = Mathf.Clamp01(config.MapMarker.Alpha);
            if (string.IsNullOrWhiteSpace(config.MapMarker.Color1)) config.MapMarker.Color1 = defaults.MapMarker.Color1;
            if (string.IsNullOrWhiteSpace(config.MapMarker.Color2)) config.MapMarker.Color2 = defaults.MapMarker.Color2;

            config.Scoring.ScoreRadiusMeters = Mathf.Clamp(config.Scoring.ScoreRadiusMeters, 10f, 500f);
            config.Scoring.PlayerKillPoints = Math.Max(0, config.Scoring.PlayerKillPoints);
            config.Scoring.PlayerDamagePointsPer100Damage = Mathf.Max(0f, config.Scoring.PlayerDamagePointsPer100Damage);
            config.Scoring.EventEntityDamagePointsPer100Damage = Mathf.Max(0f, config.Scoring.EventEntityDamagePointsPer100Damage);
            config.Scoring.ExplosiveEventEntityDamageBonusPointsPer100Damage = Mathf.Max(0f, config.Scoring.ExplosiveEventEntityDamageBonusPointsPer100Damage);
            config.Scoring.ToolCupboardDestroyedPoints = Math.Max(0, config.Scoring.ToolCupboardDestroyedPoints);
            config.Scoring.MinimumScoreToQualify = Math.Max(0, config.Scoring.MinimumScoreToQualify);
            config.Scoring.MaxLeaderboardEntries = Mathf.Clamp(config.Scoring.MaxLeaderboardEntries, 1, 10);
            config.Scoring.RepeatVictimWindowSeconds = Mathf.Clamp(config.Scoring.RepeatVictimWindowSeconds, 0f, 3600f);
            config.Scoring.RepeatVictimKillMultiplier = Mathf.Clamp01(config.Scoring.RepeatVictimKillMultiplier);
            config.Scoring.MaximumPlayerDamagePointsPerVictimPerMinute = Math.Max(0, config.Scoring.MaximumPlayerDamagePointsPerVictimPerMinute);

            if (config.Rewards.PlacementRpRewards == null)
                config.Rewards.PlacementRpRewards = defaults.Rewards.PlacementRpRewards;
            config.Rewards.PlacementRpRewards = config.Rewards.PlacementRpRewards
                .Where(reward => reward != null)
                .Select(reward => new PlacementRewardConfig
                {
                    Place = Math.Max(1, reward.Place),
                    ServerRewardsRp = Math.Max(0, reward.ServerRewardsRp)
                })
                .GroupBy(reward => reward.Place)
                .Select(group => group.OrderByDescending(reward => reward.ServerRewardsRp).First())
                .OrderBy(reward => reward.Place)
                .ToList();
            if (string.IsNullOrWhiteSpace(config.Rewards.AutomaticDefaultProfileId))
                config.Rewards.AutomaticDefaultProfileId = DefaultRewardProfileId;
            if (string.IsNullOrWhiteSpace(config.Rewards.AdminDefaultProfileId))
                config.Rewards.AdminDefaultProfileId = DefaultRewardProfileId;
            config.Rewards.AutomaticDefaultProfileId = NormalizeProfileId(config.Rewards.AutomaticDefaultProfileId);
            config.Rewards.AdminDefaultProfileId = NormalizeProfileId(config.Rewards.AdminDefaultProfileId);
            if (config.Rewards.LayoutProfileOverrides == null)
                config.Rewards.LayoutProfileOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            config.Rewards.LayoutProfileOverrides = config.Rewards.LayoutProfileOverrides
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                .ToDictionary(entry => entry.Key.Trim(), entry => NormalizeProfileId(entry.Value), StringComparer.OrdinalIgnoreCase);
            if (config.Rewards.AllowedCommandPrefixes == null)
                config.Rewards.AllowedCommandPrefixes = new List<string>();
            config.Rewards.AllowedCommandPrefixes = config.Rewards.AllowedCommandPrefixes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().TrimStart('/'))
                .Where(value => value.Length > 0 && value.All(IsSafeCommandNameCharacter))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToList();

            config.Leaderboard.MaximumDetailedHistoryEntries = Mathf.Clamp(config.Leaderboard.MaximumDetailedHistoryEntries, 100, 100000);
            config.Leaderboard.MaximumApiPageSize = Mathf.Clamp(config.Leaderboard.MaximumApiPageSize, 1, 500);
            if (config.Leaderboard.SeasonPlacementPoints == null)
                config.Leaderboard.SeasonPlacementPoints = defaults.Leaderboard.SeasonPlacementPoints;
            config.Leaderboard.SeasonPlacementPoints = config.Leaderboard.SeasonPlacementPoints
                .Where(entry => entry != null && entry.Place > 0 && entry.Points >= 0)
                .GroupBy(entry => entry.Place)
                .Select(group => group.OrderByDescending(entry => entry.Points).First())
                .OrderBy(entry => entry.Place)
                .ToList();
            if (config.Leaderboard.SeasonPlacementPoints.Count == 0)
                config.Leaderboard.SeasonPlacementPoints = defaults.Leaderboard.SeasonPlacementPoints;

            config.Cleanup.CompletionCleanupDelaySeconds = Mathf.Max(0f, config.Cleanup.CompletionCleanupDelaySeconds);
            config.Cleanup.ForcedCleanupTimeoutSeconds = Mathf.Max(60f, config.Cleanup.ForcedCleanupTimeoutSeconds);
        }

        private void EnsureCopyPasteArgumentDefault(string name, string value)
        {
            var arguments = (config?.Paste?.CopyPasteArguments ?? new string[0]).ToList();
            for (var index = 0; index + 1 < arguments.Count; index += 2)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            arguments.Add(name);
            arguments.Add(value);
            config.Paste.CopyPasteArguments = arguments.ToArray();
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
            permission.RegisterPermission(LayoutPermission, this);
            permission.RegisterPermission(StartPermission, this);
            permission.RegisterPermission(StopPermission, this);
            permission.RegisterPermission(RewardsPermission, this);
            LoadData();
            LoadRewardSystemData();
        }

        private void OnServerInitialized()
        {
            RebuildEntityIndex();
            ManageActiveEventSentries();
            ScanLayouts(true);
            EnsureAutomaticBaseLayouts();
            InitializeSpawnGrid(false);
            ReconcileAutomaticSpawnQueue();
            SaveData();
            RestoreMarkers();
            ScheduleAutoSpawn();
            ScheduleAutomaticLocationSearch();
            StartExpiryTimer();
            ResumeCompletedInstanceCleanup();
            timer.Once(5f, () => RetryRewardTransactions(null, false));
            timer.Once(7f, () => RetryPendingPurchaseRefunds());
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null)
                return;

            if (string.Equals(plugin.Name, "ServerRewards", StringComparison.OrdinalIgnoreCase))
            {
                timer.Once(2f, () => RetryRewardTransactions(null, false));
                timer.Once(3f, () => RetryPendingPurchaseRefunds());
                return;
            }

            if (string.Equals(plugin.Name, "Clans", StringComparison.OrdinalIgnoreCase))
            {
                timer.Once(1f, RefreshAllActiveAffiliations);
                return;
            }

            if (string.Equals(plugin.Name, "RaidlandsSentryTurrets", StringComparison.OrdinalIgnoreCase))
                timer.Once(0.5f, () => ManageActiveEventSentries());
        }

        private void Unload()
        {
            autoSpawnTimer?.Destroy();
            automaticSearchTimer?.Destroy();
            spawnGridBuildTimer?.Destroy();
            expiryTimer?.Destroy();
            DestroyEventsManagerUiForAll();

            if (config?.Cleanup?.CleanupOnUnload == true)
            {
                CleanupAll("plugin unload");
            }
            else
            {
                DestroyAllMarkers();
                SaveData();
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyEventsManagerUi(player);
            if (player != null)
            {
                uiOpenPlayers.Remove(player.userID);
                uiScoreModalInstances.Remove(player.userID);
                uiLayoutPages.Remove(player.userID);
                uiActivePages.Remove(player.userID);
                uiManagerPanels.Remove(player.userID);
                uiActiveEventTabs.Remove(player.userID);
                uiActiveSorts.Remove(player.userID);
                uiActiveFilters.Remove(player.userID);
                uiRewardProfileSelections.Remove(player.userID);
                uiRewardProfilePages.Remove(player.userID);
                uiRewardPreviews.Remove(player.userID);
                uiRewardDeleteConfirm.Remove(player.userID);
                uiRenderGenerations.Remove(player.userID);
                lootEditors.Remove(player.userID);
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null)
                return;

            timer.Once(5f, () => RetryRewardTransactions(player.UserIDString, false));
            timer.Once(5f, () => SyncMarkersToPlayer(player));
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null)
                return;

            timer.Once(1f, () => SyncMarkersToPlayer(player));
        }

        private void OnNewSave(string filename)
        {
            if (leaderboardData == null || historyData == null || rewardLedger == null)
                return;

            EnsureCurrentWipeState(true);
        }

        private void OnClanMemberJoined(string tag, string joining, List<string> members)
        {
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(joining) || data?.ActiveRaidBases == null)
                return;
            var changed = false;
            foreach (var score in data.ActiveRaidBases.Values.Where(value => value != null)
                         .Select(value => value.Scores != null && value.Scores.ContainsKey(joining) ? value.Scores[joining] : null)
                         .Where(value => value != null))
            {
                if (string.IsNullOrWhiteSpace(score.FirstClanId))
                    score.FirstClanId = tag;
                score.CurrentClanId = tag;
                changed = true;
            }
            if (changed) SaveData();
        }

        private void OnClanMemberGone(string tag, string leaving, List<string> members)
        {
            if (string.IsNullOrWhiteSpace(leaving) || data?.ActiveRaidBases == null)
                return;
            var changed = false;
            foreach (var score in data.ActiveRaidBases.Values.Where(value => value != null)
                         .Select(value => value.Scores != null && value.Scores.ContainsKey(leaving) ? value.Scores[leaving] : null)
                         .Where(value => value != null))
            {
                if (string.IsNullOrWhiteSpace(tag) || string.Equals(score.CurrentClanId, tag, StringComparison.OrdinalIgnoreCase))
                    score.CurrentClanId = null;
                changed = true;
            }
            if (changed) SaveData();
        }

        private void OnClanDisbanded(string tag, List<string> members)
        {
            if (string.IsNullOrWhiteSpace(tag) || data?.ActiveRaidBases == null)
                return;
            var changed = false;
            foreach (var score in data.ActiveRaidBases.Values.Where(value => value != null)
                         .SelectMany(value => value.Scores?.Values ?? Enumerable.Empty<RaidBaseScoreEntry>())
                         .Where(value => value != null && string.Equals(value.CurrentClanId, tag, StringComparison.OrdinalIgnoreCase)))
            {
                score.CurrentClanId = null;
                changed = true;
            }
            if (changed) SaveData();
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName) ?? new StoredData();
            }
            catch
            {
                PrintWarning("Could not read data; creating a new data file.");
                data = new StoredData();
            }

            if (data.Layouts == null)
                data.Layouts = new Dictionary<string, LayoutScanEntry>(StringComparer.OrdinalIgnoreCase);
            if (data.ActiveRaidBases == null)
                data.ActiveRaidBases = new Dictionary<string, ActiveRaidBase>(StringComparer.OrdinalIgnoreCase);
            if (data.PendingRewards == null)
                data.PendingRewards = new Dictionary<string, PendingRewardRecord>(StringComparer.OrdinalIgnoreCase);
            if (data.PendingPurchaseRefunds == null)
                data.PendingPurchaseRefunds = new Dictionary<string, PendingPurchaseRefundRecord>(StringComparer.OrdinalIgnoreCase);
            if (data.LayoutLootOverrides == null)
                data.LayoutLootOverrides = new Dictionary<string, Dictionary<string, ContainerLootOverride>>(StringComparer.OrdinalIgnoreCase);
            data.PendingAutomaticSpawnRequests = Math.Max(0, data.PendingAutomaticSpawnRequests);

            foreach (var active in data.ActiveRaidBases.Values)
                NormalizeActiveRaidBase(active);

            foreach (var layout in data.Layouts.Values)
            {
                if (layout != null && layout.GroundFootprintCells == null)
                    layout.GroundFootprintCells = new List<GroundFootprintCell>();
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataFileName, data, true);
        }

        private void NormalizeActiveRaidBase(ActiveRaidBase active)
        {
            if (active == null)
                return;

            if (active.EntityIds == null)
                active.EntityIds = new List<ulong>();
            if (active.Scores == null)
                active.Scores = new Dictionary<string, RaidBaseScoreEntry>(StringComparer.OrdinalIgnoreCase);
            if (active.PaidRewards == null)
                active.PaidRewards = new List<PaidRaidBaseReward>();
            if (active.PvpVictimStates == null)
                active.PvpVictimStates = new Dictionary<string, PvpVictimState>(StringComparer.OrdinalIgnoreCase);
            if (active.PurchaseCostsPaid == null)
                active.PurchaseCostsPaid = new List<PurchaseCostRecord>();
            if (string.IsNullOrWhiteSpace(active.TriggerType))
                active.TriggerType = string.IsNullOrWhiteSpace(active.PurchaserUserId) ? "admin" : "purchase";
            if (string.IsNullOrWhiteSpace(active.EventTypeId))
                active.EventTypeId = string.Equals(active.TriggerType, "automatic", StringComparison.OrdinalIgnoreCase) ? "automatic-bases" : "raid-base";
            if (string.IsNullOrWhiteSpace(active.ProviderType))
                active.ProviderType = "CopyPaste";
            if (active.ScoreRadiusMeters <= 0f)
                active.ScoreRadiusMeters = config?.Scoring?.ScoreRadiusMeters > 0f ? config.Scoring.ScoreRadiusMeters : 120f;
        }

        private void LoadRewardSystemData()
        {
            leaderboardData = ReadEventDataFile(LeaderboardsDataFileName, new LeaderboardStore());
            historyData = ReadEventDataFile(EventHistoryDataFileName, new EventHistoryStore());
            rewardLedger = ReadEventDataFile(RewardLedgerDataFileName, new RewardLedgerStore());

            NormalizeLeaderboardStore(leaderboardData);
            NormalizeHistoryStore(historyData);
            NormalizeRewardLedger(rewardLedger);
            LoadRewardProfiles();
            EnsureDefaultRewardProfile();
            EnsureCurrentWipeState(false);
            MigrateLegacyPendingRewards();

            foreach (var active in data.ActiveRaidBases.Values)
                EnsureActiveRewardSnapshot(active);

            SaveAllEventData();
        }

        private T ReadEventDataFile<T>(string fileName, T fallback) where T : class
        {
            try
            {
                return Interface.Oxide.DataFileSystem.ReadObject<T>(fileName) ?? fallback;
            }
            catch (Exception exception)
            {
                PrintWarning($"Could not read {fileName}.json; using an empty store: {exception.Message}");
                return fallback;
            }
        }

        private void NormalizeLeaderboardStore(LeaderboardStore store)
        {
            if (store == null)
                leaderboardData = store = new LeaderboardStore();
            store.SchemaVersion = EventDataSchemaVersion;
            store.ServerId = config.ServerId;
            if (store.CurrentWipe == null) store.CurrentWipe = new LeaderboardPeriod();
            if (store.Lifetime == null) store.Lifetime = new LeaderboardPeriod();
            NormalizeLeaderboardPeriod(store.CurrentWipe);
            NormalizeLeaderboardPeriod(store.Lifetime);
        }

        private void NormalizeLeaderboardPeriod(LeaderboardPeriod period)
        {
            if (period.Players == null) period.Players = new Dictionary<string, LeaderboardAggregate>(StringComparer.OrdinalIgnoreCase);
            if (period.Clans == null) period.Clans = new Dictionary<string, LeaderboardAggregate>(StringComparer.OrdinalIgnoreCase);
            if (period.Teams == null) period.Teams = new Dictionary<string, LeaderboardAggregate>(StringComparer.OrdinalIgnoreCase);
        }

        private void NormalizeHistoryStore(EventHistoryStore store)
        {
            if (store == null)
                historyData = store = new EventHistoryStore();
            store.SchemaVersion = EventDataSchemaVersion;
            if (store.Results == null) store.Results = new List<RaidBaseEventResult>();
            foreach (var result in store.Results.Where(value => value != null))
            {
                if (result.PlayerStandings == null) result.PlayerStandings = new List<EventStanding>();
                if (result.ClanStandings == null) result.ClanStandings = new List<EventStanding>();
                if (result.TeamStandings == null) result.TeamStandings = new List<EventStanding>();
                if (result.RewardTransactions == null) result.RewardTransactions = new List<RewardTransaction>();
                foreach (var standing in result.PlayerStandings.Concat(result.ClanStandings).Concat(result.TeamStandings).Where(value => value != null))
                    if (standing.Members == null) standing.Members = new List<EventStandingMember>();
            }
        }

        private void NormalizeRewardLedger(RewardLedgerStore store)
        {
            if (store == null)
                rewardLedger = store = new RewardLedgerStore();
            store.SchemaVersion = EventDataSchemaVersion;
            if (store.Transactions == null)
                store.Transactions = new Dictionary<string, RewardTransaction>(StringComparer.OrdinalIgnoreCase);

            foreach (var transaction in store.Transactions.Values.Where(value => value != null))
            {
                transaction.Type = NormalizeRewardType(transaction.Type);
                if (string.IsNullOrWhiteSpace(transaction.Status)) transaction.Status = "pending";
                if (string.Equals(transaction.Status, "processing", StringComparison.OrdinalIgnoreCase))
                {
                    transaction.Status = "review-required";
                    transaction.LastError = "The server restarted while this reward was processing; admin review is required before retry.";
                    transaction.UpdatedUnix = NowUnix();
                }
            }
        }

        private void SaveAllEventData()
        {
            SaveData();
            SaveLeaderboardData();
            SaveHistoryData();
            SaveRewardLedger();
        }

        private void SaveLeaderboardData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(LeaderboardsDataFileName, leaderboardData ?? new LeaderboardStore(), true);
        }

        private void SaveHistoryData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(EventHistoryDataFileName, historyData ?? new EventHistoryStore(), true);
        }

        private void SaveRewardLedger()
        {
            Interface.Oxide.DataFileSystem.WriteObject(RewardLedgerDataFileName, rewardLedger ?? new RewardLedgerStore(), true);
        }

        private void LoadRewardProfiles()
        {
            rewardProfiles.Clear();
            string[] files;
            try
            {
                files = Interface.Oxide.DataFileSystem.GetFiles(RewardProfilesDirectory) ?? Array.Empty<string>();
            }
            catch
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files.Where(value => value.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var profile = JsonConvert.DeserializeObject<RewardProfile>(File.ReadAllText(file));
                    var fileId = NormalizeProfileId(Path.GetFileNameWithoutExtension(file));
                    if (profile == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(profile.Id))
                        profile.Id = fileId;
                    NormalizeRewardProfile(profile);
                    if (!profile.Id.Equals(fileId, StringComparison.OrdinalIgnoreCase))
                    {
                        PrintWarning($"Reward profile file {Path.GetFileName(file)} declares Id '{profile.Id}'; using file id '{fileId}'.");
                        profile.Id = fileId;
                    }
                    rewardProfiles[profile.Id] = profile;
                }
                catch (Exception exception)
                {
                    PrintWarning($"Could not load reward profile {Path.GetFileName(file)}: {exception.Message}");
                }
            }
        }

        private void EnsureDefaultRewardProfile()
        {
            if (rewardProfiles.ContainsKey(DefaultRewardProfileId))
                return;

            var profile = new RewardProfile
            {
                Id = DefaultRewardProfileId,
                DisplayName = "Default Raid Base Rewards",
                Enabled = true,
                RewardMode = "FixedPlacements",
                ScoreScope = "Clan",
                AllowSoloIfNoGroup = true,
                GroupDistribution = "ContributionWeighted",
                MinimumGroupScore = config.Scoring.MinimumScoreToQualify,
                MinimumMemberScore = 1,
                Placements = (config.Rewards.PlacementRpRewards ?? new List<PlacementRewardConfig>())
                    .Where(entry => entry != null && entry.Place > 0 && entry.ServerRewardsRp > 0)
                    .OrderBy(entry => entry.Place)
                    .Select(entry => new RewardPlacementDefinition
                    {
                        Place = entry.Place,
                        Rewards = new List<RewardDefinition>
                        {
                            new RewardDefinition { Type = "RP", Amount = entry.ServerRewardsRp }
                        }
                    })
                    .ToList()
            };

            if (profile.Placements.Count == 0)
            {
                profile.Placements.Add(new RewardPlacementDefinition { Place = 1, Rewards = new List<RewardDefinition> { new RewardDefinition { Type = "RP", Amount = 10000 } } });
                profile.Placements.Add(new RewardPlacementDefinition { Place = 2, Rewards = new List<RewardDefinition> { new RewardDefinition { Type = "RP", Amount = 5000 } } });
                profile.Placements.Add(new RewardPlacementDefinition { Place = 3, Rewards = new List<RewardDefinition> { new RewardDefinition { Type = "RP", Amount = 2500 } } });
            }

            NormalizeRewardProfile(profile);
            rewardProfiles[profile.Id] = profile;
            SaveRewardProfile(profile);
            Puts($"Created migrated reward profile '{profile.Id}' from the legacy placement RP configuration; global payouts remain disabled.");
        }

        private void NormalizeRewardProfile(RewardProfile profile)
        {
            if (profile == null)
                return;

            profile.SchemaVersion = RewardProfileSchemaVersion;
            profile.Id = NormalizeProfileId(profile.Id);
            profile.DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Id : profile.DisplayName.Trim();
            profile.RewardMode = NormalizeRewardMode(profile.RewardMode);
            profile.ScoreScope = NormalizeScoreScope(profile.ScoreScope);
            profile.GroupDistribution = NormalizeGroupDistribution(profile.GroupDistribution);
            profile.MinimumGroupScore = Math.Max(0, profile.MinimumGroupScore);
            profile.MinimumMemberScore = Math.Max(0, profile.MinimumMemberScore);
            if (profile.Placements == null) profile.Placements = new List<RewardPlacementDefinition>();
            if (profile.Pool == null) profile.Pool = new List<RewardDefinition>();
            foreach (var placement in profile.Placements.Where(value => value != null))
            {
                placement.Place = Math.Max(1, placement.Place);
                placement.Percent = Mathf.Clamp(placement.Percent, 0f, 100f);
                if (placement.Rewards == null) placement.Rewards = new List<RewardDefinition>();
                foreach (var reward in placement.Rewards.Where(value => value != null)) NormalizeRewardDefinition(reward);
            }
            foreach (var reward in profile.Pool.Where(value => value != null)) NormalizeRewardDefinition(reward);
            profile.Placements = profile.Placements.Where(value => value != null).OrderBy(value => value.Place).ToList();
            profile.Pool = profile.Pool.Where(value => value != null).ToList();
        }

        private void NormalizeRewardDefinition(RewardDefinition reward)
        {
            reward.Type = NormalizeRewardType(reward.Type);
            reward.Amount = Math.Max(0, reward.Amount);
            reward.ShortName = string.IsNullOrWhiteSpace(reward.ShortName) ? null : reward.ShortName.Trim().ToLowerInvariant();
            reward.Command = string.IsNullOrWhiteSpace(reward.Command) ? null : reward.Command.Trim();
        }

        private void SaveRewardProfile(RewardProfile profile)
        {
            if (profile == null)
                return;
            NormalizeRewardProfile(profile);
            Interface.Oxide.DataFileSystem.WriteObject(RewardProfilesDirectory + profile.Id, profile, true);
        }

        private string ValidateRewardProfile(RewardProfile profile, bool includeDependencies)
        {
            var errors = new List<string>();
            if (profile == null)
                return "Profile is null.";
            if (profile.SchemaVersion != RewardProfileSchemaVersion) errors.Add($"SchemaVersion must be {RewardProfileSchemaVersion}");
            if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id != NormalizeProfileId(profile.Id)) errors.Add("Id is invalid");
            if (!IsSupportedRewardMode(profile.RewardMode)) errors.Add("RewardMode must be FixedPlacements or PercentagePool");
            if (!IsSupportedScoreScope(profile.ScoreScope)) errors.Add("ScoreScope must be Player, Clan, or RustTeam");
            if (!IsSupportedGroupDistribution(profile.GroupDistribution)) errors.Add("GroupDistribution must be Even or ContributionWeighted");
            if (includeDependencies && profile.ScoreScope.Equals("Clan", StringComparison.OrdinalIgnoreCase) && (Clans == null || !Clans.IsLoaded))
                errors.Add("Clans plugin is not loaded");
            var allRewards = profile.RewardMode.Equals("PercentagePool", StringComparison.OrdinalIgnoreCase)
                ? profile.Pool ?? new List<RewardDefinition>()
                : (profile.Placements ?? new List<RewardPlacementDefinition>()).SelectMany(value => value?.Rewards ?? new List<RewardDefinition>()).ToList();
            if (includeDependencies && allRewards.Any(value => value != null && value.Type == "RP") && (ServerRewards == null || !ServerRewards.IsLoaded))
                errors.Add("ServerRewards plugin is not loaded");

            var duplicatePlaces = profile.Placements.GroupBy(value => value.Place).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
            if (duplicatePlaces.Count > 0) errors.Add("Duplicate placements: " + string.Join(", ", duplicatePlaces));
            if (profile.Placements.Count == 0) errors.Add("At least one placement is required");

            if (profile.RewardMode.Equals("FixedPlacements", StringComparison.OrdinalIgnoreCase))
            {
                if (profile.Placements.Any(value => value.Rewards == null || value.Rewards.Count == 0))
                    errors.Add("Every fixed placement needs at least one reward");
                foreach (var reward in profile.Placements.SelectMany(value => value.Rewards ?? new List<RewardDefinition>()))
                    ValidateRewardDefinition(reward, false, errors);
            }
            else
            {
                var percent = profile.Placements.Sum(value => value.Percent);
                if (Math.Abs(percent - 100f) > 0.01f) errors.Add($"PercentagePool placements must total 100%, currently {percent:0.##}%");
                if (profile.Pool.Count == 0) errors.Add("PercentagePool needs at least one pool reward");
                foreach (var reward in profile.Pool) ValidateRewardDefinition(reward, true, errors);
            }

            return errors.Count == 0 ? null : string.Join("; ", errors.Distinct());
        }

        private void ValidateRewardDefinition(RewardDefinition reward, bool percentagePool, List<string> errors)
        {
            if (reward == null)
            {
                errors.Add("Reward row is null");
                return;
            }
            if (reward.Amount <= 0) errors.Add($"{reward.Type} reward amount must be positive");
            if (reward.Type == "Item")
            {
                if (string.IsNullOrWhiteSpace(reward.ShortName) || ItemManager.FindItemDefinition(reward.ShortName) == null)
                    errors.Add($"Unknown item shortname '{reward.ShortName ?? ""}'");
            }
            else if (reward.Type == "Command")
            {
                string commandError;
                if (!TryValidateRewardCommand(reward.Command, out commandError)) errors.Add(commandError);
            }
            else if (reward.Type != "RP")
            {
                errors.Add($"Unsupported reward type '{reward.Type}'");
            }
        }

        private bool TryValidateRewardCommand(string command, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(command))
            {
                error = "Command reward is empty";
                return false;
            }
            if (command.IndexOfAny(new[] { '\r', '\n', ';', '&', '|' }) >= 0)
            {
                error = "Command reward contains a forbidden separator";
                return false;
            }
            var prefix = command.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimStart('/');
            if (string.IsNullOrWhiteSpace(prefix) || !config.Rewards.AllowedCommandPrefixes.Any(value => value.Equals(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"Command prefix '{prefix ?? ""}' is not allowlisted";
                return false;
            }
            var scrubbed = command;
            foreach (var placeholder in new[] { "{playerId}", "{amount}", "{rank}", "{instanceId}", "{clanTag}", "{teamId}" })
                scrubbed = scrubbed.Replace(placeholder, string.Empty);
            if (scrubbed.Contains("{") || scrubbed.Contains("}"))
            {
                error = "Command reward contains an unsupported placeholder";
                return false;
            }
            return true;
        }

        private void EnsureCurrentWipeState(bool forceSave)
        {
            var wipeKey = ResolveCurrentWipeKey();
            var changed = !string.Equals(leaderboardData.CurrentWipeKey, wipeKey, StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                var previous = leaderboardData.CurrentWipeKey;
                leaderboardData.CurrentWipeKey = wipeKey;
                leaderboardData.CurrentWipeStartedAtUtc = ResolveWipeStartedAtUtc().ToString("o", CultureInfo.InvariantCulture);
                leaderboardData.CurrentWipe = new LeaderboardPeriod();
                historyData.CurrentWipeKey = wipeKey;
                historyData.Results.Clear();
                foreach (var id in rewardLedger.Transactions
                             .Where(entry => entry.Value != null && !string.Equals(entry.Value.WipeKey, wipeKey, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(entry.Value.Status, "paid", StringComparison.OrdinalIgnoreCase))
                             .Select(entry => entry.Key).ToList())
                    rewardLedger.Transactions.Remove(id);
                Puts($"Raid-base leaderboard wipe changed from '{previous ?? "none"}' to '{wipeKey}'. Current-wipe results were reset; lifetime totals were preserved.");
            }
            else if (string.IsNullOrWhiteSpace(historyData.CurrentWipeKey))
            {
                historyData.CurrentWipeKey = wipeKey;
            }

            if (changed || forceSave)
            {
                SaveLeaderboardData();
                SaveHistoryData();
                SaveRewardLedger();
            }
        }

        private DateTime ResolveWipeStartedAtUtc()
        {
            try
            {
                var value = SaveRestore.SaveCreatedTime;
                if (value != DateTime.MinValue)
                    return value.ToUniversalTime();
            }
            catch
            {
            }
            return DateTime.UtcNow;
        }

        private string ResolveCurrentWipeKey()
        {
            if (!string.IsNullOrWhiteSpace(config.Leaderboard.WipeKey))
                return CleanStableId(config.Leaderboard.WipeKey, config.ServerId + "-current");
            return $"{config.ServerId}-{ResolveWipeStartedAtUtc():yyyyMMdd'T'HHmmss'Z'}";
        }

        private void MigrateLegacyPendingRewards()
        {
            if (data.PendingRewards == null || data.PendingRewards.Count == 0)
                return;

            foreach (var legacy in data.PendingRewards.Values.Where(value => value != null && value.ServerRewardsRp > 0))
            {
                var id = string.IsNullOrWhiteSpace(legacy.RewardId)
                    ? $"legacy:{legacy.InstanceId}:{legacy.Place}:{legacy.UserId}:rp"
                    : legacy.RewardId;
                if (rewardLedger.Transactions.ContainsKey(id))
                    continue;
                rewardLedger.Transactions[id] = new RewardTransaction
                {
                    TransactionId = id,
                    WipeKey = leaderboardData.CurrentWipeKey,
                    InstanceId = legacy.InstanceId,
                    ProfileId = "legacy",
                    Place = legacy.Place,
                    GroupScope = "Player",
                    GroupId = legacy.UserId,
                    UserId = legacy.UserId,
                    DisplayName = legacy.DisplayName,
                    Type = "RP",
                    Amount = legacy.ServerRewardsRp,
                    Status = "pending",
                    CreatedUnix = legacy.CreatedUnix > 0 ? legacy.CreatedUnix : NowUnix(),
                    UpdatedUnix = legacy.LastAttemptUnix,
                    AttemptCount = legacy.AttemptCount,
                    LastError = legacy.LastError
                };
            }
            data.PendingRewards.Clear();
            SaveData();
            SaveRewardLedger();
            Puts("Migrated legacy pending RP rewards into the generic reward transaction ledger.");
        }

        private void EnsureActiveRewardSnapshot(ActiveRaidBase active)
        {
            if (active == null || active.RewardProfileSnapshot != null)
                return;
            ResolveRewardProfileForInstance(active.LayoutId, active.TriggerType, out active.RewardProfileId,
                out active.RewardProfileSnapshot, out active.RewardProfileHash, out active.RewardPayoutEnabled,
                out active.RewardProfileError);
        }

        private void ResolveRewardProfileForInstance(string layoutId, string triggerType, out string profileId,
            out RewardProfile snapshot, out string hash, out bool payoutEnabled, out string error)
        {
            profileId = null;
            snapshot = null;
            hash = null;
            error = null;
            payoutEnabled = false;

            string layoutProfile;
            if (!string.IsNullOrWhiteSpace(layoutId) && config.Rewards.LayoutProfileOverrides.TryGetValue(layoutId, out layoutProfile))
                profileId = layoutProfile;
            else if (string.Equals(triggerType, "automatic", StringComparison.OrdinalIgnoreCase))
                profileId = config.Rewards.AutomaticDefaultProfileId;
            else
                profileId = config.Rewards.AdminDefaultProfileId;

            RewardProfile profile;
            if (string.IsNullOrWhiteSpace(profileId) || !rewardProfiles.TryGetValue(profileId, out profile) || profile == null)
            {
                error = $"Assigned reward profile '{profileId ?? "none"}' was not found.";
                return;
            }

            snapshot = CloneJson(profile);
            NormalizeRewardProfile(snapshot);
            hash = RewardProfileHash(snapshot);
            error = ValidateRewardProfile(snapshot, true);
            var triggerEnabled = string.Equals(triggerType, "automatic", StringComparison.OrdinalIgnoreCase)
                ? config.Rewards.AutomaticEventPayoutsEnabled
                : config.Rewards.AdminEventPayoutsEnabled;
            payoutEnabled = config.Rewards.Enabled && triggerEnabled && snapshot.Enabled && string.IsNullOrWhiteSpace(error);
        }

        private string RewardProfileHash(RewardProfile profile)
        {
            var json = JsonConvert.SerializeObject(profile, Formatting.None);
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(json)).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private T CloneJson<T>(T value)
        {
            return value == null ? default(T) : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        }

        private static string CleanStableId(string value, string fallback)
        {
            var raw = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var cleaned = new string(raw.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.' || ch == ':' ? ch : '-').ToArray()).Trim('-', '_', '.', ':');
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }

        private static string NormalizeProfileId(string value)
        {
            return CleanStableId((value ?? string.Empty).ToLowerInvariant(), DefaultRewardProfileId);
        }

        private static bool IsSafeCommandNameCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_' || value == '-' || value == '.';
        }

        private static string NormalizeRewardMode(string value)
        {
            return string.Equals(value, "PercentagePool", StringComparison.OrdinalIgnoreCase) ? "PercentagePool" : "FixedPlacements";
        }

        private static bool IsSupportedRewardMode(string value)
        {
            return string.Equals(value, "FixedPlacements", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "PercentagePool", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeScoreScope(string value)
        {
            if (string.Equals(value, "Player", StringComparison.OrdinalIgnoreCase)) return "Player";
            if (string.Equals(value, "RustTeam", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Team", StringComparison.OrdinalIgnoreCase)) return "RustTeam";
            return "Clan";
        }

        private static bool IsSupportedScoreScope(string value)
        {
            return string.Equals(value, "Player", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Clan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "RustTeam", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeGroupDistribution(string value)
        {
            return string.Equals(value, "Even", StringComparison.OrdinalIgnoreCase) ? "Even" : "ContributionWeighted";
        }

        private static bool IsSupportedGroupDistribution(string value)
        {
            return string.Equals(value, "Even", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "ContributionWeighted", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRewardType(string value)
        {
            if (string.Equals(value, "Item", StringComparison.OrdinalIgnoreCase)) return "Item";
            if (string.Equals(value, "Command", StringComparison.OrdinalIgnoreCase)) return "Command";
            return "RP";
        }

        private object API_GetAnnouncedActiveEvents()
        {
            return data.ActiveRaidBases.Values
                .Where(active => active != null && active.IsAnnounced && active.Status == "active")
                .OrderBy(active => active.StartedUnix)
                .Select(active =>
                {
                    var center = EventCenter(active);
                    return new Dictionary<string, object>
                    {
                        ["instanceId"] = active.InstanceId,
                        ["eventTypeId"] = active.EventTypeId,
                        ["providerType"] = active.ProviderType,
                        ["publicName"] = active.PublicName,
                        ["layoutId"] = active.LayoutId,
                        ["x"] = center.x,
                        ["y"] = center.y,
                        ["z"] = center.z,
                        ["radiusMeters"] = config.MapMarker.RadiusMeters,
                        ["startedAt"] = DateTimeOffset.FromUnixTimeSeconds((long)active.StartedUnix).UtcDateTime.ToString("o"),
                        ["expiresAt"] = DateTimeOffset.FromUnixTimeSeconds((long)active.ExpiresUnix).UtcDateTime.ToString("o")
                    };
                })
                .ToList();
        }

        [HookMethod(nameof(API_GetRaidBaseLeaderboard))]
        private object API_GetRaidBaseLeaderboard(object request = null)
        {
            var query = ReadApiRequest(request);
            var periodName = ApiRequestString(query, "period", "current_wipe").ToLowerInvariant();
            var scope = NormalizeScoreScope(ApiRequestString(query, "scope", "Player"));
            var offset = Math.Max(0, ApiRequestInt(query, "offset", 0));
            var limit = Math.Max(1, Math.Min(config.Leaderboard.MaximumApiPageSize, ApiRequestInt(query, "limit", 50)));
            var period = periodName == "lifetime" ? leaderboardData.Lifetime : leaderboardData.CurrentWipe;
            periodName = periodName == "lifetime" ? "lifetime" : "current_wipe";
            var all = LeaderboardDictionary(period, scope).Values.Where(value => value != null)
                .OrderByDescending(value => value.SeasonPoints)
                .ThenByDescending(value => value.FirstPlaces)
                .ThenByDescending(value => value.TotalScore)
                .ThenByDescending(value => value.LastQualifiedUnix)
                .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var standings = all.Skip(offset).Take(limit).Select((value, index) => LeaderboardAggregateToDictionary(value, offset + index + 1, periodName == "current_wipe")).ToList();
            return ApiEnvelope(new Dictionary<string, object>
            {
                ["period"] = periodName,
                ["scope"] = scope,
                ["pagination"] = PaginationDictionary(offset, limit, all.Count, standings.Count),
                ["standings"] = standings
            });
        }

        [HookMethod(nameof(API_GetRaidBaseEventHistory))]
        private object API_GetRaidBaseEventHistory(object request = null)
        {
            var query = ReadApiRequest(request);
            var offset = Math.Max(0, ApiRequestInt(query, "offset", 0));
            var limit = Math.Max(1, Math.Min(config.Leaderboard.MaximumApiPageSize, ApiRequestInt(query, "limit", 50)));
            var state = ApiRequestString(query, "state", null);
            var layoutId = ApiRequestString(query, "layout_id", ApiRequestString(query, "layoutId", null));
            var triggerType = ApiRequestString(query, "trigger_type", ApiRequestString(query, "triggerType", null));
            IEnumerable<RaidBaseEventResult> results = historyData.Results.Where(value => value != null);
            if (!string.IsNullOrWhiteSpace(state)) results = results.Where(value => string.Equals(value.State, state, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(layoutId)) results = results.Where(value => string.Equals(value.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(triggerType)) results = results.Where(value => string.Equals(value.TriggerType, triggerType, StringComparison.OrdinalIgnoreCase));
            var all = results.OrderByDescending(value => value.EndedUnix).ThenBy(value => value.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
            var page = all.Skip(offset).Take(limit).Select(value => EventResultToDictionary(value, false)).ToList();
            return ApiEnvelope(new Dictionary<string, object>
            {
                ["period"] = "current_wipe",
                ["filters"] = new Dictionary<string, object> { ["state"] = state, ["layout_id"] = layoutId, ["trigger_type"] = triggerType },
                ["pagination"] = PaginationDictionary(offset, limit, all.Count, page.Count),
                ["results"] = page
            });
        }

        [HookMethod(nameof(API_GetRaidBaseEventResult))]
        private object API_GetRaidBaseEventResult(string instanceId)
        {
            var result = historyData.Results.FirstOrDefault(value => value != null && string.Equals(value.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
            return ApiEnvelope(new Dictionary<string, object>
            {
                ["found"] = result != null,
                ["result"] = result == null ? null : EventResultToDictionary(result, true)
            });
        }

        [HookMethod(nameof(API_GetActiveRaidBaseScores))]
        private object API_GetActiveRaidBaseScores(string instanceId)
        {
            ActiveRaidBase active;
            if (string.IsNullOrWhiteSpace(instanceId) || !data.ActiveRaidBases.TryGetValue(instanceId, out active) || active == null)
                return ApiEnvelope(new Dictionary<string, object> { ["found"] = false, ["instance_id"] = instanceId });
            var transactions = rewardLedger.Transactions.Values.Where(value => value != null && value.InstanceId == active.InstanceId).ToList();
            return ApiEnvelope(new Dictionary<string, object>
            {
                ["found"] = true,
                ["instance_id"] = active.InstanceId,
                ["event_type_id"] = active.EventTypeId,
                ["layout_id"] = active.LayoutId,
                ["display_name"] = active.PublicName,
                ["trigger_type"] = active.TriggerType,
                ["state"] = active.Status,
                ["started_at"] = UnixIso(active.StartedUnix),
                ["expires_at"] = UnixIso(active.ExpiresUnix),
                ["reward_profile_id"] = active.RewardProfileId,
                ["reward_profile_hash"] = active.RewardProfileHash,
                ["reward_payout_enabled"] = active.RewardPayoutEnabled,
                ["reward_status"] = RewardStatusDictionary(transactions),
                ["player_standings"] = BuildEventStandings(active, "Player", false, false).Select(EventStandingToDictionary).ToList(),
                ["clan_standings"] = BuildEventStandings(active, "Clan", true, false).Select(EventStandingToDictionary).ToList(),
                ["team_standings"] = BuildEventStandings(active, "RustTeam", true, false).Select(EventStandingToDictionary).ToList()
            });
        }

        private Dictionary<string, object> ApiEnvelope(Dictionary<string, object> payload)
        {
            var result = new Dictionary<string, object>
            {
                ["schema_version"] = PublicApiSchemaVersion,
                ["server_id"] = config.ServerId,
                ["wipe_id"] = leaderboardData.CurrentWipeKey,
                ["generated_at"] = DateTime.UtcNow.ToString("o")
            };
            foreach (var pair in payload) result[pair.Key] = pair.Value;
            return result;
        }

        private Dictionary<string, object> PaginationDictionary(int offset, int limit, int total, int count)
        {
            return new Dictionary<string, object>
            {
                ["offset"] = offset,
                ["limit"] = limit,
                ["count"] = count,
                ["total"] = total,
                ["has_more"] = offset + count < total
            };
        }

        private JObject ReadApiRequest(object request)
        {
            if (request == null) return new JObject();
            var token = request as JObject;
            if (token != null) return (JObject)token.DeepClone();
            try { return JObject.FromObject(request); }
            catch { return new JObject(); }
        }

        private string ApiRequestString(JObject request, string key, string fallback)
        {
            if (request == null) return fallback;
            JToken token;
            if (!request.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out token) || token == null || token.Type == JTokenType.Null) return fallback;
            var value = token.ToString().Trim();
            return value.Length == 0 ? fallback : value;
        }

        private int ApiRequestInt(JObject request, string key, int fallback)
        {
            var text = ApiRequestString(request, key, null);
            int value;
            return text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private Dictionary<string, object> LeaderboardAggregateToDictionary(LeaderboardAggregate aggregate, int rank, bool currentWipeOnly)
        {
            var matchingRewards = rewardLedger.Transactions.Values.Where(value => value != null &&
                (aggregate.Scope == "Player" ? value.UserId == aggregate.Id : value.GroupScope == aggregate.Scope && value.GroupId == aggregate.Id)).ToList();
            if (currentWipeOnly) matchingRewards = matchingRewards.Where(value => string.Equals(value.WipeKey, leaderboardData.CurrentWipeKey, StringComparison.OrdinalIgnoreCase)).ToList();
            return new Dictionary<string, object>
            {
                ["rank"] = rank, ["id"] = aggregate.Id, ["scope"] = aggregate.Scope, ["display_name"] = aggregate.DisplayName,
                ["events_entered"] = aggregate.EventsEntered, ["events_qualified"] = aggregate.EventsQualified, ["placement_points"] = aggregate.SeasonPoints,
                ["wins"] = aggregate.FirstPlaces, ["second_places"] = aggregate.SecondPlaces, ["third_places"] = aggregate.ThirdPlaces,
                ["podiums"] = aggregate.FirstPlaces + aggregate.SecondPlaces + aggregate.ThirdPlaces, ["contribution_score"] = aggregate.TotalScore,
                ["kills"] = aggregate.PlayerKills, ["deaths"] = aggregate.PlayerDeaths, ["damage_to_players"] = aggregate.DamageToPlayers,
                ["damage_to_event_entities"] = aggregate.DamageToEventEntities, ["explosive_damage_to_event_entities"] = aggregate.ExplosiveDamageToEventEntities,
                ["tool_cupboard_credits"] = aggregate.ToolCupboardsDestroyed, ["last_qualified_at"] = UnixIso(aggregate.LastQualifiedUnix),
                ["paid_reward_totals"] = new Dictionary<string, object> { ["rp"] = aggregate.RpPaid, ["item_units"] = aggregate.ItemUnitsPaid, ["commands"] = aggregate.CommandsPaid },
                ["pending_reward_totals"] = RewardTotalsDictionary(matchingRewards.Where(value => value.Status != "paid"))
            };
        }

        private Dictionary<string, object> EventResultToDictionary(RaidBaseEventResult result, bool includeDetails)
        {
            var transactions = rewardLedger.Transactions.Values.Where(value => value != null && value.InstanceId == result.InstanceId).OrderBy(value => value.TransactionId).ToList();
            var response = new Dictionary<string, object>
            {
                ["instance_id"] = result.InstanceId, ["event_type_id"] = result.EventTypeId, ["layout_id"] = result.LayoutId,
                ["display_name"] = result.DisplayName, ["trigger_type"] = result.TriggerType, ["state"] = result.State,
                ["started_at"] = UnixIso(result.StartedUnix), ["ended_at"] = UnixIso(result.EndedUnix), ["end_reason"] = result.EndReason,
                ["position"] = new Dictionary<string, object> { ["x"] = result.Position?.X ?? 0f, ["y"] = result.Position?.Y ?? 0f, ["z"] = result.Position?.Z ?? 0f },
                ["reward_profile_id"] = result.RewardProfileId, ["reward_profile_hash"] = result.RewardProfileHash,
                ["reward_status"] = RewardStatusDictionary(transactions),
                ["player_standings"] = result.PlayerStandings.Select(EventStandingToDictionary).ToList(),
                ["clan_standings"] = result.ClanStandings.Select(EventStandingToDictionary).ToList(),
                ["team_standings"] = result.TeamStandings.Select(EventStandingToDictionary).ToList()
            };
            if (includeDetails)
            {
                response["reward_profile_snapshot"] = ToDetachedJsonObject(result.RewardProfileSnapshot);
                response["reward_transactions"] = transactions.Select(RewardTransactionToDictionary).ToList();
            }
            return response;
        }

        private Dictionary<string, object> EventStandingToDictionary(EventStanding standing)
        {
            return new Dictionary<string, object>
            {
                ["rank"] = standing.Rank, ["scope"] = standing.Scope, ["id"] = standing.Id, ["display_name"] = standing.DisplayName,
                ["score"] = standing.Score, ["kills"] = standing.PlayerKills, ["deaths"] = standing.PlayerDeaths,
                ["damage_to_players"] = standing.DamageToPlayers, ["damage_to_event_entities"] = standing.DamageToEventEntities,
                ["explosive_damage_to_event_entities"] = standing.ExplosiveDamageToEventEntities, ["tool_cupboard_credits"] = standing.ToolCupboardsDestroyed,
                ["members"] = standing.Members.Select(value => new Dictionary<string, object> { ["player_id"] = value.UserId, ["display_name"] = value.DisplayName, ["score"] = value.Score }).ToList()
            };
        }

        private Dictionary<string, object> RewardTransactionToDictionary(RewardTransaction transaction)
        {
            return new Dictionary<string, object>
            {
                ["transaction_id"] = transaction.TransactionId, ["instance_id"] = transaction.InstanceId, ["profile_id"] = transaction.ProfileId,
                ["placement"] = transaction.Place, ["group_scope"] = transaction.GroupScope, ["group_id"] = transaction.GroupId,
                ["player_id"] = transaction.UserId, ["display_name"] = transaction.DisplayName, ["type"] = transaction.Type, ["amount"] = transaction.Amount,
                ["item_shortname"] = transaction.ShortName, ["item_skin_id"] = transaction.SkinId, ["command_template"] = transaction.Command,
                ["online_required"] = transaction.RequireOnline, ["status"] = transaction.Status, ["attempts"] = transaction.AttemptCount,
                ["last_error"] = transaction.LastError, ["created_at"] = UnixIso(transaction.CreatedUnix), ["updated_at"] = UnixIso(transaction.UpdatedUnix)
            };
        }

        private Dictionary<string, object> RewardStatusDictionary(IEnumerable<RewardTransaction> transactions)
        {
            var rows = transactions?.Where(value => value != null).ToList() ?? new List<RewardTransaction>();
            return new Dictionary<string, object>
            {
                ["total"] = rows.Count, ["pending"] = rows.Count(value => value.Status == "pending"), ["processing"] = rows.Count(value => value.Status == "processing"),
                ["paid"] = rows.Count(value => value.Status == "paid"), ["failed"] = rows.Count(value => value.Status == "failed"),
                ["review_required"] = rows.Count(value => value.Status == "review-required")
            };
        }

        private Dictionary<string, object> RewardTotalsDictionary(IEnumerable<RewardTransaction> transactions)
        {
            var rows = transactions?.Where(value => value != null).ToList() ?? new List<RewardTransaction>();
            return new Dictionary<string, object>
            {
                ["rp"] = rows.Where(value => value.Type == "RP").Sum(value => (long)value.Amount),
                ["item_units"] = rows.Where(value => value.Type == "Item").Sum(value => (long)value.Amount),
                ["commands"] = rows.Count(value => value.Type == "Command")
            };
        }

        private object ToDetachedJsonObject(object value)
        {
            return value == null ? null : JsonConvert.DeserializeObject<object>(JsonConvert.SerializeObject(value));
        }

        private string UnixIso(double unix)
        {
            return unix <= 0 ? null : DateTimeOffset.FromUnixTimeSeconds((long)unix).UtcDateTime.ToString("o");
        }

        [HookMethod(nameof(API_IsActiveRaidBaseEntity))]
        private bool API_IsActiveRaidBaseEntity(BaseEntity entity)
        {
            if (entity?.net == null || entity.IsDestroyed)
                return false;

            string instanceId;
            ActiveRaidBase active;
            return entityToInstance.TryGetValue(entity.net.ID.Value, out instanceId)
                   && data.ActiveRaidBases.TryGetValue(instanceId, out active)
                   && active != null
                   && !string.Equals(active.Status, "cleaning", StringComparison.OrdinalIgnoreCase);
        }

        [HookMethod(nameof(API_GetActiveRaidBaseId))]
        private object API_GetActiveRaidBaseId(BaseEntity entity)
        {
            if (!API_IsActiveRaidBaseEntity(entity))
                return null;

            string instanceId;
            return entityToInstance.TryGetValue(entity.net.ID.Value, out instanceId) ? instanceId : null;
        }

        [ConsoleCommand("revents.status")]
        private void CommandStatus(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, AdminPermission))
                return;

            Reply(arg, BuildStatusMessage(true));
        }

        [ConsoleCommand("revents.grid")]
        private void CommandSpawnGrid(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, LayoutPermission))
                return;

            var args = GetArgs(arg);
            if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Reply(arg, BuildSpawnGridStatus(true));
                return;
            }

            if (args[0].Equals("rebuild", StringComparison.OrdinalIgnoreCase))
            {
                InitializeSpawnGrid(true);
                Reply(arg, "RaidlandsEvents spawn-grid rebuild started. Use revents.grid status to monitor it.");
                return;
            }

            Reply(arg, "Usage: revents.grid status|rebuild");
        }

        [ConsoleCommand("revents.layouts")]
        private void CommandLayouts(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, LayoutPermission))
                return;

            var args = GetArgs(arg);
            if (args.Length == 0)
            {
                Reply(arg, "Usage: revents.layouts scan|list|enable <layoutId>|disable <layoutId>");
                return;
            }

            var subcommand = args[0].ToLowerInvariant();
            switch (subcommand)
            {
                case "scan":
                    var count = ScanLayouts(true);
                    Reply(arg, $"Scanned {count} CopyPaste layout(s). Use revents.layouts list to review candidates.");
                    break;

                case "list":
                    Reply(arg, BuildLayoutList());
                    break;

                case "enable":
                    if (args.Length < 2)
                    {
                        Reply(arg, "Usage: revents.layouts enable <layoutId>");
                        return;
                    }
                    string enableMessage;
                    TryEnableLayout(args[1], out enableMessage);
                    Reply(arg, enableMessage);
                    break;

                case "disable":
                    if (args.Length < 2)
                    {
                        Reply(arg, "Usage: revents.layouts disable <layoutId>");
                        return;
                    }
                    string disableMessage;
                    DisableLayout(args[1], out disableMessage);
                    Reply(arg, disableMessage);
                    break;

                default:
                    Reply(arg, "Usage: revents.layouts scan|list|enable <layoutId>|disable <layoutId>");
                    break;
            }
        }

        [ConsoleCommand("revents.start")]
        private void CommandStart(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, StartPermission))
                return;

            var args = GetArgs(arg);
            if (args.Length < 2)
            {
                Reply(arg, "Usage: revents.start <layoutId|random> here|random");
                return;
            }

            var layoutId = args[0];
            var locationMode = args[1].ToLowerInvariant();
            var player = arg.Player();
            Vector3 position;
            string failure;

            if (locationMode == "here")
            {
                if (player == null)
                {
                    Reply(arg, "The here location mode requires an in-game admin. Use random from server console.");
                    return;
                }

                if (!TryGetHerePosition(player, out position, out failure))
                {
                    Reply(arg, failure);
                    return;
                }
            }
            else if (locationMode == "random")
            {
                position = Vector3.zero;
            }
            else
            {
                Reply(arg, "Usage: revents.start <layoutId|random> here|random");
                return;
            }

            var result = StartRaidBase(layoutId, locationMode == "random", position, out failure);
            Reply(arg, result ? failure : $"RaidlandsEvents start failed: {failure}");
        }

        [ConsoleCommand("revents.auto")]
        private void CommandAuto(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, AdminPermission))
                return;

            var args = GetArgs(arg);
            if (args.Length < 1 || (args[0].ToLowerInvariant() != "on" && args[0].ToLowerInvariant() != "off"))
            {
                Reply(arg, $"Automatic Bases is {(config.EventTypes.AutomaticBases.Enabled ? "on" : "off")}. Usage: revents.auto on|off");
                return;
            }

            config.EventTypes.AutomaticBases.Enabled = args[0].Equals("on", StringComparison.OrdinalIgnoreCase);
            config.AutoSpawn.Enabled = config.EventTypes.AutomaticBases.Enabled;
            if (config.EventTypes.AutomaticBases.Enabled)
            {
                data.NextAutoAttemptUnix = Math.Min(data.NextAutoAttemptUnix, NowUnix() + 5f);
                ReconcileAutomaticSpawnQueue();
                ScheduleAutomaticLocationSearch();
            }
            else
            {
                CancelAutomaticLocationSearch(true);
            }
            SaveConfig();
            SaveData();
            ScheduleAutoSpawn();
            Reply(arg, $"Automatic Bases is now {(config.EventTypes.AutomaticBases.Enabled ? "on" : "off")}.");
        }

        [ConsoleCommand("revents.stop")]
        private void CommandStop(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, StopPermission))
                return;

            var args = GetArgs(arg);
            if (args.Length < 1)
            {
                Reply(arg, "Usage: revents.stop <instanceId|all>");
                return;
            }

            var target = args[0];
            if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var count = CleanupAll("admin stop");
                Reply(arg, $"Stopped and cleaned {count} active raid base event(s).");
                return;
            }

            if (!data.ActiveRaidBases.ContainsKey(target))
            {
                Reply(arg, $"No active raid base instance '{target}' was found.");
                return;
            }

            CleanupInstance(target, "admin stop");
            Reply(arg, $"Stopped and cleaned raid base event {target}.");
        }

        [ConsoleCommand("revents.cleanup")]
        private void CommandCleanup(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, StopPermission))
                return;

            var count = CleanupAll("admin cleanup");
            Reply(arg, $"Cleanup removed {count} active raid base event(s).");
        }

        [ConsoleCommand("revents.score")]
        private void CommandScore(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, AdminPermission))
                return;

            var args = GetArgs(arg);
            var target = args.Length > 0 ? args[0] : "all";
            Reply(arg, BuildScoreboardMessage(target, true));
        }

        [ConsoleCommand("revents.rewards")]
        private void CommandRewards(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, RewardsPermission))
                return;

            var args = GetArgs(arg);
            var subcommand = args.Length > 0 ? args[0].ToLowerInvariant() : "profiles";
            switch (subcommand)
            {
                case "profiles":
                    Reply(arg, BuildRewardProfilesMessage());
                    break;

                case "reload":
                    LoadRewardProfiles();
                    Reply(arg, $"Reloaded {rewardProfiles.Count} RaidlandsEvents reward profile(s).\n{BuildRewardProfilesMessage()}");
                    break;

                case "validate":
                    Reply(arg, BuildRewardValidationMessage(args.Length > 1 ? args[1] : null));
                    break;

                case "preview":
                    if (args.Length < 2)
                    {
                        Reply(arg, "Usage: revents.rewards preview <profileId> [instanceId]");
                        break;
                    }
                    Reply(arg, BuildRewardPreview(args[1], args.Length > 2 ? args[2] : null));
                    break;

                case "selftest":
                    Reply(arg, RunRewardCalculationSelfTest());
                    break;

                case "pending":
                case "list":
                    Reply(arg, BuildPendingRewardsMessage());
                    break;

                case "review":
                    Reply(arg, BuildRewardReviewMessage());
                    break;

                case "retry":
                    var target = args.Length > 1 ? args[1] : null;
                    var retryAll = string.IsNullOrWhiteSpace(target) || target.Equals("all", StringComparison.OrdinalIgnoreCase);
                    var paid = RetryRewardTransactions(null, !retryAll, retryAll ? null : target);
                    Reply(arg, $"Retried RaidlandsEvents rewards: paid={paid}, pending={CountRewardTransactions("pending")}, review-required={CountRewardTransactions("review-required")}.");
                    break;

                case "assign":
                    if (args.Length < 3 || !(args[1].Equals("automatic", StringComparison.OrdinalIgnoreCase) || args[1].Equals("admin", StringComparison.OrdinalIgnoreCase)))
                    {
                        Reply(arg, "Usage: revents.rewards assign automatic|admin <profileId>");
                        break;
                    }
                    RewardProfile assigned;
                    if (!rewardProfiles.TryGetValue(args[2], out assigned))
                    {
                        Reply(arg, $"Reward profile '{args[2]}' was not found.");
                        break;
                    }
                    if (args[1].Equals("automatic", StringComparison.OrdinalIgnoreCase)) config.Rewards.AutomaticDefaultProfileId = assigned.Id;
                    else config.Rewards.AdminDefaultProfileId = assigned.Id;
                    SaveConfig();
                    Reply(arg, $"Assigned reward profile '{assigned.Id}' as the {args[1].ToLowerInvariant()} default. Payout enable switches were not changed.");
                    break;

                default:
                    Reply(arg, "Usage: revents.rewards profiles|reload|validate [profileId]|preview <profileId> [instanceId]|selftest|pending|review|retry [transactionId|all]|assign automatic|admin <profileId>");
                    break;
            }
        }

        [ConsoleCommand("revents.purchases")]
        private void CommandPurchases(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, AdminPermission))
                return;

            var args = GetArgs(arg);
            var subcommand = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            switch (subcommand)
            {
                case "status":
                    Reply(arg, $"Player raid-base purchasing is retired. Legacy refund debt is still preserved and retried: pending={data.PendingPurchaseRefunds.Count}. No new purchase or charge can be created.");
                    break;

                case "refunds":
                    Reply(arg, BuildPendingPurchaseRefundsMessage());
                    break;

                case "retry":
                    var refunded = RetryPendingPurchaseRefunds();
                    Reply(arg, $"Retried pending RaidlandsEvents purchase refunds: refunded={refunded}, remaining={data.PendingPurchaseRefunds.Count}.");
                    break;

                default:
                    Reply(arg, "Usage: revents.purchases status|refunds|retry");
                    break;
            }
        }

        [ChatCommand("revents")]
        private void ChatCommandRaidlandsEvents(BasePlayer player, string command, string[] args)
        {
            if (player == null || !HasPlayerAccess(player, AdminPermission))
            {
                SendReply(player, $"{config.ChatPrefix} You do not have permission to use RaidlandsEvents.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                SendReply(player, $"{config.ChatPrefix} {BuildStatusMessage(false)}\nCommands: revents.status, revents.grid status|rebuild, revents.layouts scan|list|enable|disable, revents.start <layoutId|random> here|random, revents.score <instanceId|all>, revents.rewards profiles|validate|selftest|preview|pending|review|retry, revents.purchases status|refunds|retry, revents.auto on|off, revents.stop <instanceId|all>, revents.cleanup.");
                return;
            }

            SendReply(player, $"{config.ChatPrefix} Use console commands for this MVP: revents.status, revents.layouts, revents.start, revents.score, revents.rewards, revents.purchases, revents.auto, revents.stop, revents.cleanup.");
        }

        [ChatCommand("raidme")]
        private void ChatCommandRaidMe(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            SendReply(player, $"{config.ChatPrefix} /raidme is reserved for a future player-base defense mode and is currently nonfunctional. No RP or items were charged.");
        }

        [ChatCommand("raidbase")]
        private void ChatCommandRaidBase(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!HasPlayerAccess(player, StartPermission))
            {
                SendReply(player, $"{config.ChatPrefix} You do not have permission to spawn raid-base events.");
                return;
            }

            var layoutId = args != null && args.Length > 0 ? args[0] : "random";
            var locationMode = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : "random";
            if (locationMode != "here" && locationMode != "random")
            {
                SendReply(player, $"{config.ChatPrefix} Usage: /raidbase [layout|random] [here|random]");
                return;
            }
            StartRaidBaseFromUi(player, layoutId, locationMode);
        }

        [ChatCommand("eventsmanager")]
        private void ChatCommandEventsManager(BasePlayer player, string command, string[] args)
        {
            OpenEventsManager(player);
        }

        [ChatCommand("em")]
        private void ChatCommandEventsManagerShort(BasePlayer player, string command, string[] args)
        {
            OpenEventsManager(player);
        }

        [ConsoleCommand("revents.ui")]
        private void CommandEventsManagerUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
                return;

            if (!HasPlayerAccess(player, AdminPermission))
            {
                SendReply(player, $"{config.ChatPrefix} You do not have permission to use RaidlandsEvents.");
                return;
            }

            var args = GetArgs(arg);
            var action = args.Length > 0 ? args[0].ToLowerInvariant() : "open";
            var reopen = true;

            switch (action)
            {
                case "open":
                    break;

                case "refresh":
                    uiScoreModalInstances.Remove(player.userID);
                    break;

                case "close":
                    CloseEventsManagerUi(player);
                    return;

                case "scan":
                    var count = ScanLayouts(true);
                    EnsureAutomaticBaseLayoutEntries();
                    SendReply(player, $"{config.ChatPrefix} Scanned {count} CopyPaste layout(s).");
                    break;

                case "layoutpage":
                    if (args.Length >= 2)
                    {
                        int delta;
                        if (int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
                        {
                            int page;
                            uiLayoutPages.TryGetValue(player.userID, out page);
                            uiLayoutPages[player.userID] = Math.Max(0, page + delta);
                        }
                    }
                    break;

                case "panel":
                    if (args.Length >= 2)
                    {
                        var requestedPanel = args[1].ToLowerInvariant();
                        uiManagerPanels[player.userID] = requestedPanel == "load" || requestedPanel == "rewards" ? requestedPanel : "active";
                    }
                    break;

                case "reward":
                    if (!HasPlayerAccess(player, RewardsPermission))
                    {
                        SendReply(player, $"{config.ChatPrefix} You do not have permission to manage reward profiles.");
                        break;
                    }
                    string rewardMessage;
                    HandleRewardUiAction(player, args.Skip(1).ToArray(), out rewardMessage);
                    if (!string.IsNullOrWhiteSpace(rewardMessage)) SendReply(player, $"{config.ChatPrefix} {rewardMessage}");
                    break;

                case "activetab":
                    if (args.Length >= 2)
                    {
                        uiActiveEventTabs[player.userID] = args[1].ToLowerInvariant();
                        uiActivePages[player.userID] = 0;
                    }
                    break;

                case "activepage":
                    if (args.Length >= 2)
                    {
                        int delta;
                        if (int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
                        {
                            int page;
                            uiActivePages.TryGetValue(player.userID, out page);
                            uiActivePages[player.userID] = Math.Max(0, page + delta);
                        }
                    }
                    break;

                case "activesort":
                    if (args.Length >= 2)
                    {
                        uiActiveSorts[player.userID] = args[1].ToLowerInvariant();
                        uiActivePages[player.userID] = 0;
                    }
                    break;

                case "activefilter":
                    if (args.Length >= 2)
                    {
                        uiActiveFilters[player.userID] = args[1].ToLowerInvariant();
                        uiActivePages[player.userID] = 0;
                    }
                    break;

                case "auto":
                    if (args.Length >= 2)
                    {
                        config.EventTypes.AutomaticBases.Enabled = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                        config.AutoSpawn.Enabled = config.EventTypes.AutomaticBases.Enabled;
                        if (config.EventTypes.AutomaticBases.Enabled)
                        {
                            data.NextAutoAttemptUnix = Math.Min(data.NextAutoAttemptUnix, NowUnix() + 5f);
                            ReconcileAutomaticSpawnQueue();
                            ScheduleAutomaticLocationSearch();
                        }
                        else
                        {
                            CancelAutomaticLocationSearch(true);
                        }
                        SaveConfig();
                        SaveData();
                        ScheduleAutoSpawn();
                        SendReply(player, $"{config.ChatPrefix} Automatic Bases is now {(config.EventTypes.AutomaticBases.Enabled ? "on" : "off")}.");
                    }
                    break;

                case "autonow":
                    RunAutoSpawnTick();
                    SendReply(player, $"{config.ChatPrefix} Automatic Bases population check ran now.");
                    break;

                case "setting":
                    if (args.Length >= 3)
                    {
                        float delta;
                        if (float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out delta))
                        {
                            string settingMessage;
                            AdjustAutomaticBaseSetting(args[1], delta, out settingMessage);
                            SendReply(player, $"{config.ChatPrefix} {settingMessage}");
                        }
                    }
                    break;

                case "weight":
                    if (args.Length >= 3)
                    {
                        float delta;
                        if (float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out delta))
                        {
                            string weightMessage;
                            AdjustAutomaticLayoutWeight(args[1], delta, out weightMessage);
                            SendReply(player, $"{config.ChatPrefix} {weightMessage}");
                        }
                    }
                    break;

                case "enable":
                    if (args.Length >= 2)
                    {
                        string enableMessage;
                        TryEnableLayout(args[1], out enableMessage);
                        SendReply(player, $"{config.ChatPrefix} {enableMessage}");
                    }
                    break;

                case "disable":
                    if (args.Length >= 2)
                    {
                        string disableMessage;
                        DisableLayout(args[1], out disableMessage);
                        SendReply(player, $"{config.ChatPrefix} {disableMessage}");
                    }
                    break;

                case "start":
                    if (args.Length >= 3)
                    {
                        StartRaidBaseFromUi(player, args[1], args[2]);
                    }
                    break;

                case "lootopen":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission))
                    {
                        uiScoreModalInstances.Remove(player.userID);
                        OpenLootEditor(player, args[1]);
                        return;
                    }
                    break;

                case "lootcontainer":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission))
                    {
                        int containerIndex;
                        if (int.TryParse(args[1], out containerIndex)) SelectLootContainer(player, containerIndex);
                    }
                    break;

                case "lootcontainerpage":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission))
                    {
                        LootEditorState editor;
                        int delta;
                        if (lootEditors.TryGetValue(player.userID, out editor) && int.TryParse(args[1], out delta)) editor.ContainerPage = Math.Max(0, editor.ContainerPage + delta);
                    }
                    break;

                case "lootslot":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission))
                    {
                        int slot;
                        if (int.TryParse(args[1], out slot)) SelectLootSlot(player, slot);
                    }
                    break;

                case "lootslotpage":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission))
                    {
                        LootEditorState editor;
                        int delta;
                        if (lootEditors.TryGetValue(player.userID, out editor) && int.TryParse(args[1], out delta)) editor.SlotPage = Math.Max(0, editor.SlotPage + delta);
                    }
                    break;

                case "lootsearch":
                    if (HasPlayerAccess(player, LayoutPermission))
                    {
                        LootEditorState editor;
                        if (lootEditors.TryGetValue(player.userID, out editor))
                        {
                            editor.Search = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : string.Empty;
                            editor.ItemPage = 0;
                        }
                    }
                    break;

                case "lootitempage":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission))
                    {
                        LootEditorState editor;
                        int delta;
                        if (lootEditors.TryGetValue(player.userID, out editor) && int.TryParse(args[1], out delta)) editor.ItemPage = Math.Max(0, editor.ItemPage + delta);
                    }
                    break;

                case "lootpick":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission)) SetDraftLootItem(player, args[1]);
                    break;

                case "lootamount":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission)) AdjustDraftLootAmount(player, args[1]);
                    break;

                case "lootskin":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission)) SetDraftLootSkin(player, args[1]);
                    break;

                case "lootclear":
                    if (HasPlayerAccess(player, LayoutPermission)) ClearDraftLootSlot(player);
                    break;

                case "lootclearall":
                    if (HasPlayerAccess(player, LayoutPermission))
                    {
                        LootEditorState editor;
                        if (lootEditors.TryGetValue(player.userID, out editor)) { editor.DraftItems.Clear(); editor.DraftLoaded = true; }
                    }
                    break;

                case "lootsave":
                    if (HasPlayerAccess(player, LayoutPermission)) SaveLootOverride(player);
                    break;

                case "lootreset":
                    if (HasPlayerAccess(player, LayoutPermission)) ResetLootOverride(player);
                    break;

                case "lootdiscard":
                    lootEditors.Remove(player.userID);
                    break;

                case "lootorphandelete":
                    if (args.Length >= 2 && HasPlayerAccess(player, LayoutPermission)) DeleteOrphanedOverride(player, args[1]);
                    break;

                case "tp":
                    if (args.Length >= 2)
                    {
                        string teleportMessage;
                        TryTeleportToRaidBase(player, args[1], out teleportMessage);
                        SendReply(player, $"{config.ChatPrefix} {teleportMessage}");
                    }
                    break;

                case "score":
                    if (args.Length >= 2)
                    {
                        OpenEventsManager(player, args[1]);
                        return;
                    }
                    break;

                case "retryrewards":
                    var paidRewards = RetryRewardTransactions(null, false);
                    SendReply(player, $"{config.ChatPrefix} Retried pending rewards: paid={paidRewards}, remaining={CountRewardTransactions("pending")}.");
                    break;

                case "stop":
                    if (args.Length >= 2)
                    {
                        if (args[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                        {
                            var stopped = CleanupAll("admin UI stop");
                            SendReply(player, $"{config.ChatPrefix} Stopped and cleaned {stopped} active raid base event(s).");
                        }
                        else if (data.ActiveRaidBases.ContainsKey(args[1]))
                        {
                            CleanupInstance(args[1], "admin UI stop");
                            SendReply(player, $"{config.ChatPrefix} Stopped and cleaned raid base event {args[1]}.");
                        }
                        else
                        {
                            SendReply(player, $"{config.ChatPrefix} No active raid base instance '{args[1]}' was found.");
                        }
                    }
                    break;

                case "cleanup":
                    var cleaned = CleanupAll("admin UI cleanup");
                    SendReply(player, $"{config.ChatPrefix} Cleanup removed {cleaned} active raid base event(s).");
                    break;

                default:
                    SendReply(player, $"{config.ChatPrefix} Unknown events manager action.");
                    break;
            }

            if (reopen)
                OpenEventsManager(player);
        }

        private bool AdjustAutomaticBaseSetting(string setting, float delta, out string message)
        {
            var automaticBases = config.EventTypes.AutomaticBases;
            switch ((setting ?? string.Empty).ToLowerInvariant())
            {
                case "min":
                    automaticBases.MinimumActiveBases = Mathf.Clamp(automaticBases.MinimumActiveBases + Mathf.RoundToInt(delta), 0, automaticBases.MaximumActiveBases);
                    message = $"Automatic Bases minimum is now {automaticBases.MinimumActiveBases}.";
                    break;
                case "max":
                    automaticBases.MaximumActiveBases = Mathf.Clamp(automaticBases.MaximumActiveBases + Mathf.RoundToInt(delta), 1, 64);
                    automaticBases.MinimumActiveBases = Math.Min(automaticBases.MinimumActiveBases, automaticBases.MaximumActiveBases);
                    automaticBases.MaximumSpawnsPerCheck = Math.Min(automaticBases.MaximumSpawnsPerCheck, automaticBases.MaximumActiveBases);
                    message = $"Automatic Bases maximum is now {automaticBases.MaximumActiveBases}.";
                    break;
                case "batch":
                    automaticBases.MaximumSpawnsPerCheck = Mathf.Clamp(automaticBases.MaximumSpawnsPerCheck + Mathf.RoundToInt(delta), 1, automaticBases.MaximumActiveBases);
                    message = $"Automatic Bases per-check quantity is now {automaticBases.MaximumSpawnsPerCheck}.";
                    break;
                case "frequency":
                    automaticBases.CheckFrequencyMinutes = Mathf.Clamp(automaticBases.CheckFrequencyMinutes + delta, 1f, 1440f);
                    message = $"Automatic Bases frequency is now {automaticBases.CheckFrequencyMinutes:0.#} minutes.";
                    break;
                case "announce":
                    automaticBases.PercentageToAnnounce = Mathf.Clamp(automaticBases.PercentageToAnnounce + delta, 0f, 100f);
                    message = $"Automatic Bases announce target is now {automaticBases.PercentageToAnnounce:0.#}%.";
                    break;
                case "lifetime":
                    automaticBases.HardLifetimeHours = Mathf.Clamp(automaticBases.HardLifetimeHours + delta, 1f, 168f);
                    message = $"Automatic Bases hard lifetime is now {automaticBases.HardLifetimeHours:0.#} hours.";
                    break;
                case "players":
                    automaticBases.MinimumOnlinePlayers = Mathf.Clamp(automaticBases.MinimumOnlinePlayers + Mathf.RoundToInt(delta), 0, 500);
                    message = $"Automatic Bases minimum online players is now {automaticBases.MinimumOnlinePlayers}.";
                    break;
                default:
                    message = $"Unknown Automatic Bases setting '{setting}'.";
                    return false;
            }

            SaveConfig();
            ScheduleAutoSpawn();
            return true;
        }

        private bool AdjustAutomaticLayoutWeight(string layoutId, float delta, out string message)
        {
            var weighted = config.EventTypes.AutomaticBases.Layouts
                .FirstOrDefault(entry => entry != null && string.Equals(entry.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase));
            if (weighted == null)
            {
                message = $"Automatic Bases layout '{layoutId}' was not found.";
                return false;
            }

            weighted.Weight = Mathf.Clamp(weighted.Weight + delta, 0.1f, 1000f);
            SaveConfig();
            message = $"Automatic Bases layout {weighted.LayoutId} weight is now {weighted.Weight:0.#}.";
            return true;
        }

        private void StartRaidBaseFromUi(BasePlayer player, string layoutId, string locationMode)
        {
            if (player == null)
                return;

            if (!HasPlayerAccess(player, StartPermission))
            {
                SendReply(player, $"{config.ChatPrefix} You do not have permission to start RaidlandsEvents.");
                return;
            }

            Vector3 position;
            string failure;
            var randomLocation = locationMode.Equals("random", StringComparison.OrdinalIgnoreCase);

            if (randomLocation)
            {
                position = Vector3.zero;
            }
            else if (locationMode.Equals("here", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetHerePosition(player, out position, out failure))
                {
                    SendReply(player, $"{config.ChatPrefix} {failure}");
                    return;
                }
            }
            else
            {
                SendReply(player, $"{config.ChatPrefix} Unknown start location '{locationMode}'.");
                return;
            }

            var result = StartRaidBase(layoutId, randomLocation, position, out failure);
            SendReply(player, $"{config.ChatPrefix} {(result ? failure : $"Start failed: {failure}")}");
        }


        private void RefundFailedPurchase(ActiveRaidBase active, string reason)
        {
            if (active == null || active.PurchaseRefunded || active.PurchaseCostsPaid == null || active.PurchaseCostsPaid.Count == 0)
                return;

            var player = PlayerFromStringId(active.PurchaserUserId);
            string error;
            if (TryRefundPurchaseCosts(active.PurchaserUserId, active.PurchaserDisplayName, player, active.PurchaseCostsPaid, out error))
            {
                active.PurchaseRefunded = true;
                active.PurchaseRefundError = null;
                TellPurchasePlayer(active.PurchaserUserId, $"{config.ChatPrefix} Your RaidlandsEvents purchase was refunded because the event failed to start.");
                Puts($"Refunded failed purchase for {active.PurchaserUserId} on {active.InstanceId}: {BuildCostSummary(active.PurchaseCostsPaid)}.");
                return;
            }

            active.PurchaseRefundError = error;
            QueuePurchaseRefund(active.InstanceId, active.PurchaserUserId, active.PurchaserDisplayName, active.PurchaseCostsPaid, reason, error);
            PrintWarning($"Queued failed purchase refund for {active.PurchaserUserId} on {active.InstanceId}: {error}");
        }

        private bool TryRefundPurchaseCosts(string userId, string displayName, BasePlayer player, List<PurchaseCostRecord> costs, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(userId))
            {
                error = "Refund user id is missing.";
                return false;
            }

            foreach (var cost in costs ?? new List<PurchaseCostRecord>())
            {
                if (cost == null || cost.Amount <= 0)
                    continue;

                if (cost.Type == "RP")
                {
                    if (ServerRewards == null || !ServerRewards.IsLoaded)
                    {
                        error = "ServerRewards is not loaded.";
                        return false;
                    }
                }
                else if (cost.Type == "Item")
                {
                    if (player == null)
                    {
                        error = "Player must be online for item refund.";
                        return false;
                    }

                    if (ItemManager.FindItemDefinition(cost.ShortName) == null)
                    {
                        error = $"Refund item '{cost.ShortName}' is not valid.";
                        return false;
                    }
                }
            }

            foreach (var cost in costs ?? new List<PurchaseCostRecord>())
            {
                if (cost == null || cost.Amount <= 0)
                    continue;

                if (cost.Type == "RP")
                {
                    if (!TryAddServerRewardsPoints(userId, cost.Amount, out error))
                        return false;
                    continue;
                }

                if (cost.Type == "Item")
                {
                    var item = ItemManager.CreateByName(cost.ShortName, cost.Amount);
                    if (item == null)
                    {
                        error = $"Could not create refund item '{cost.ShortName}'.";
                        return false;
                    }

                    player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                    player.Command("note.inv", item.info.itemid, cost.Amount);
                }
            }

            return true;
        }

        private void QueuePurchaseRefund(string instanceId, string userId, string displayName, List<PurchaseCostRecord> costs, string reason, string error)
        {
            if (data.PendingPurchaseRefunds == null)
                data.PendingPurchaseRefunds = new Dictionary<string, PendingPurchaseRefundRecord>(StringComparer.OrdinalIgnoreCase);

            var refundId = string.IsNullOrWhiteSpace(instanceId)
                ? $"purchase-refund-{DateTimeOffset.UtcNow.ToUnixTimeSeconds():x}-{userId}"
                : $"{instanceId}:{userId}:purchase-refund";

            data.PendingPurchaseRefunds[refundId] = new PendingPurchaseRefundRecord
            {
                RefundId = refundId,
                InstanceId = instanceId,
                UserId = userId,
                DisplayName = displayName,
                Costs = costs?.Select(ClonePurchaseCost).ToList() ?? new List<PurchaseCostRecord>(),
                Reason = reason,
                CreatedUnix = NowUnix(),
                LastAttemptUnix = NowUnix(),
                AttemptCount = 1,
                LastError = error
            };
            SaveData();
        }

        private int RetryPendingPurchaseRefunds()
        {
            if (data?.PendingPurchaseRefunds == null || data.PendingPurchaseRefunds.Count == 0)
                return 0;

            var refunded = 0;
            foreach (var refund in data.PendingPurchaseRefunds.Values.ToList())
            {
                if (refund == null || string.IsNullOrWhiteSpace(refund.UserId))
                    continue;

                refund.AttemptCount++;
                refund.LastAttemptUnix = NowUnix();
                var player = PlayerFromStringId(refund.UserId);
                string error;
                if (!TryRefundPurchaseCosts(refund.UserId, refund.DisplayName, player, refund.Costs, out error))
                {
                    refund.LastError = error;
                    continue;
                }

                data.PendingPurchaseRefunds.Remove(refund.RefundId);
                TellPurchasePlayer(refund.UserId, $"{config.ChatPrefix} Your queued RaidlandsEvents purchase refund was paid: {BuildCostSummary(refund.Costs)}.");
                refunded++;
            }

            SaveData();
            return refunded;
        }

        private bool TryTeleportToRaidBase(BasePlayer player, string instanceId, out string message)
        {
            message = null;

            if (player == null)
            {
                message = "Player was not found.";
                return false;
            }

            if (!HasPlayerAccess(player, AdminPermission))
            {
                message = "You do not have permission to teleport to RaidlandsEvents.";
                return false;
            }

            if (player.IsDead() || player.IsWounded())
            {
                message = "You cannot teleport while dead or wounded.";
                return false;
            }

            if (player.isMounted)
            {
                message = "Dismount before teleporting to an event.";
                return false;
            }

            ActiveRaidBase active;
            if (!data.ActiveRaidBases.TryGetValue(instanceId, out active))
            {
                message = $"No active raid base instance '{instanceId}' was found.";
                return false;
            }

            Vector3 target;
            string reason;
            if (!TryGetRaidBaseTeleportPosition(active, out target, out reason))
            {
                message = reason;
                return false;
            }

            player.Teleport(target);
            message = $"Teleported to {active.PublicName} {active.InstanceId} near {FormatVector(target)}.";
            return true;
        }

        private bool TryGetRaidBaseTeleportPosition(ActiveRaidBase active, out Vector3 target, out string reason)
        {
            target = Vector3.zero;
            reason = null;

            if (active == null)
            {
                reason = "Raid base instance was not found.";
                return false;
            }

            target = active.Position.ToVector3();

            LayoutScanEntry layout;
            if (data.Layouts.TryGetValue(active.LayoutId, out layout) && layout != null)
            {
                var min = layout.BoundsMin.ToVector3();
                var max = layout.BoundsMax.ToVector3();
                var localCenter = new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
                var rotation = Quaternion.Euler(0f, active.RotationDegrees, 0f);
                var center = active.Position.ToVector3() + rotation * localCenter;
                var footprintRadius = Math.Max(max.x - min.x, max.z - min.z) * 0.5f;
                var approachDistance = Mathf.Clamp(footprintRadius + 12f, 16f, 80f);
                target = center + rotation * (Vector3.forward * approachDistance);
            }

            Vector3 ground;
            if (TrySnapToGround(target, out ground))
            {
                target = ground + Vector3.up * 2f;
                return true;
            }

            target = active.Position.ToVector3() + Vector3.up * 3f;
            return true;
        }

        private void OpenEventsManager(BasePlayer player, string scoreModalInstanceId = null)
        {
            if (player == null)
                return;

            if (!HasPlayerAccess(player, AdminPermission))
            {
                SendReply(player, $"{config.ChatPrefix} You do not have permission to use RaidlandsEvents.");
                return;
            }

            uiOpenPlayers.Add(player.userID);
            if (!string.IsNullOrWhiteSpace(scoreModalInstanceId))
                uiScoreModalInstances[player.userID] = scoreModalInstanceId;
            else
                uiScoreModalInstances.TryGetValue(player.userID, out scoreModalInstanceId);

            int generation;
            uiRenderGenerations.TryGetValue(player.userID, out generation);
            generation++;
            uiRenderGenerations[player.userID] = generation;
            DestroyEventsManagerUi(player);

            var userId = player.userID;
            NextTick(() =>
            {
                int currentGeneration;
                if (player == null || !player.IsConnected || !uiOpenPlayers.Contains(userId) ||
                    !uiRenderGenerations.TryGetValue(userId, out currentGeneration) || currentGeneration != generation)
                    return;

                RenderEventsManager(player);
            });
        }

        private void RenderEventsManager(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !HasPlayerAccess(player, AdminPermission))
                return;

            string scoreModalInstanceId;
            uiScoreModalInstances.TryGetValue(player.userID, out scoreModalInstanceId);

            if (string.IsNullOrWhiteSpace(scoreModalInstanceId) && lootEditors.ContainsKey(player.userID))
            {
                var editorContainer = new CuiElementContainer();
                var editorRoot = editorContainer.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "0.01 0.015 0.02 0.985" },
                    RectTransform = { AnchorMin = "0.05 0.04", AnchorMax = "0.95 0.96" }
                }, "Overlay", EventsManagerUi);
                BuildLootEditorUi(editorContainer, editorRoot, player);
                CuiHelper.AddUi(player, editorContainer);
                return;
            }

            var container = new CuiElementContainer();
            var panel = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.025 0.03 0.035 0.96" },
                RectTransform = { AnchorMin = "0.12 0.08", AnchorMax = "0.88 0.91" }
            }, "Overlay", EventsManagerUi);

            AddUiLabel(container, panel, "<b>Raidlands Events Manager</b>", 0.03f, 0.935f, 0.44f, 0.985f, 18, TextAnchor.MiddleLeft, "0.95 0.98 1 1");
            AddUiLabel(container, panel, UiHeaderStatus(), 0.45f, 0.935f, 0.90f, 0.985f, 11, TextAnchor.MiddleRight, "0.68 0.75 0.82 1");
            AddUiButton(container, panel, "X", "revents.ui close", 0.925f, 0.94f, 0.975f, 0.985f, "0.45 0.12 0.12 0.95", 13);

            AddUiSection(container, panel, "Controls", 0.03f, 0.865f, 0.97f, 0.91f);
            AddUiButton(container, panel, "Scan", "revents.ui scan", 0.045f, 0.802f, 0.145f, 0.85f, "0.16 0.24 0.32 0.96", 11);
            AddUiButton(container, panel, "Refresh", "revents.ui refresh", 0.155f, 0.802f, 0.255f, 0.85f, "0.16 0.24 0.32 0.96", 11);
            AddUiButton(container, panel, config.EventTypes.AutomaticBases.Enabled ? "Disable Automatic Bases" : "Enable Automatic Bases", config.EventTypes.AutomaticBases.Enabled ? "revents.ui auto off" : "revents.ui auto on", 0.265f, 0.802f, 0.445f, 0.85f, config.EventTypes.AutomaticBases.Enabled ? "0.36 0.20 0.12 0.96" : "0.14 0.30 0.22 0.96", 9);
            AddUiButton(container, panel, "Stop All Events", "revents.ui stop all", 0.455f, 0.802f, 0.575f, 0.85f, "0.36 0.18 0.16 0.96", 9);
            AddUiButton(container, panel, "Cleanup", "revents.ui cleanup", 0.585f, 0.802f, 0.695f, 0.85f, "0.36 0.18 0.16 0.96", 10);
            AddUiButton(container, panel, "Retry Pending", "revents.ui retryrewards", 0.705f, 0.802f, 0.795f, 0.85f, "0.16 0.30 0.42 0.96", 8);
            AddUiLabel(container, panel, ShortUiText(BuildStatusMessage(false), 96), 0.875f, 0.802f, 0.955f, 0.85f, 8, TextAnchor.MiddleRight, "0.66 0.72 0.78 1");

            var automaticPanel = AddUiRefreshPanel(container, panel, EventsManagerAutomaticUi);
            BuildAutomaticBasesUi(container, automaticPanel);
            var workspacePanel = AddUiRefreshPanel(container, panel, EventsManagerWorkspaceUi);
            BuildManagerWorkspaceUi(container, workspacePanel, player);

            if (!string.IsNullOrWhiteSpace(scoreModalInstanceId))
                BuildScoreModalUi(container, panel, scoreModalInstanceId);

            CuiHelper.AddUi(player, container);
        }

        private string AddUiRefreshPanel(CuiElementContainer container, string parent, string name)
        {
            container.Add(new CuiElement
            {
                Name = name,
                Parent = parent,
                Components =
                {
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }
            });

            return name;
        }

        private void BuildAutomaticBasesUi(CuiElementContainer container, string panel)
        {
            var automaticBases = config.EventTypes.AutomaticBases;
            AddUiSection(container, panel, "Event Type: Automatic Bases", 0.03f, 0.715f, 0.97f, 0.76f);
            AddUiLabel(container, panel, automaticBases.Enabled ? "RUNNING" : "DISABLED", 0.045f, 0.665f, 0.125f, 0.70f, 9, TextAnchor.MiddleLeft, automaticBases.Enabled ? "0.60 0.86 0.65 1" : "0.92 0.48 0.42 1");
            AddUiLabel(container, panel, $"Population {AutomaticBaseActiveCount()} / {automaticBases.MinimumActiveBases}-{automaticBases.MaximumActiveBases}", 0.13f, 0.665f, 0.29f, 0.70f, 9, TextAnchor.MiddleLeft, "0.82 0.88 0.94 1");
            AddUiStepper(container, panel, "Minimum bases", automaticBases.MinimumActiveBases.ToString(CultureInfo.InvariantCulture), "min", 1f, 0.30f, 0.42f, 0.665f);
            AddUiStepper(container, panel, "Maximum bases", automaticBases.MaximumActiveBases.ToString(CultureInfo.InvariantCulture), "max", 1f, 0.43f, 0.55f, 0.665f);
            AddUiStepper(container, panel, "Starts per check", automaticBases.MaximumSpawnsPerCheck.ToString(CultureInfo.InvariantCulture), "batch", 1f, 0.56f, 0.70f, 0.665f);
            AddUiStepper(container, panel, "Check interval", $"{automaticBases.CheckFrequencyMinutes:0.#}m", "frequency", 5f, 0.71f, 0.86f, 0.665f);
            AddUiButton(container, panel, "Fill Now", "revents.ui autonow", 0.87f, 0.663f, 0.955f, 0.703f, "0.16 0.32 0.24 0.96", 9);

            AddUiLabel(container, panel, ShortUiText(AutomaticSearchStatusShort(), 72), 0.045f, 0.615f, 0.30f, 0.65f, 8, TextAnchor.MiddleLeft, "0.62 0.68 0.74 1");
            AddUiStepper(container, panel, "Public percent", $"{automaticBases.PercentageToAnnounce:0.#}%", "announce", 5f, 0.30f, 0.46f, 0.615f);
            AddUiStepper(container, panel, "Minimum players", automaticBases.MinimumOnlinePlayers.ToString(CultureInfo.InvariantCulture), "players", 1f, 0.47f, 0.61f, 0.615f);
            AddUiStepper(container, panel, "Cleanup age", $"{automaticBases.HardLifetimeHours:0.#}h", "lifetime", 1f, 0.62f, 0.78f, 0.615f);
            AddUiLabel(container, panel, $"Next {FormatDuration(Math.Max(0, data.NextAutoAttemptUnix - NowUnix()))}", 0.80f, 0.615f, 0.955f, 0.65f, 8, TextAnchor.MiddleRight, "0.62 0.68 0.74 1");
        }

        private void AddUiStepper(CuiElementContainer container, string parent, string label, string value, string setting, float step, float xMin, float xMax, float y)
        {
            var width = xMax - xMin;
            AddUiLabel(container, parent, $"{label}\n<b>{value}</b>", xMin, y, xMin + width * 0.58f, y + 0.038f, 7, TextAnchor.MiddleLeft, "0.78 0.84 0.90 1");
            AddUiButton(container, parent, $"- {label}", $"revents.ui setting {setting} {(-step).ToString(CultureInfo.InvariantCulture)}", xMin + width * 0.60f, y, xMin + width * 0.79f, y + 0.038f, "0.24 0.18 0.16 0.96", 6);
            AddUiButton(container, parent, $"+ {label}", $"revents.ui setting {setting} {step.ToString(CultureInfo.InvariantCulture)}", xMin + width * 0.81f, y, xMax, y + 0.038f, "0.14 0.30 0.22 0.96", 6);
        }

        private void BuildManagerWorkspaceUi(CuiElementContainer container, string panel, BasePlayer player)
        {
            string workspace;
            if (!uiManagerPanels.TryGetValue(player.userID, out workspace))
                workspace = "active";

            AddUiButton(container, panel, $"Active Events ({ActiveEventCount()})", "revents.ui panel active", 0.03f, 0.525f, 0.205f, 0.57f, workspace == "active" ? "0.16 0.38 0.54 0.98" : "0.10 0.13 0.16 0.96", 10);
            AddUiButton(container, panel, "Load Event", "revents.ui panel load", 0.21f, 0.525f, 0.355f, 0.57f, workspace == "load" ? "0.16 0.38 0.54 0.98" : "0.10 0.13 0.16 0.96", 10);
            AddUiButton(container, panel, $"Rewards ({rewardProfiles.Count})", "revents.ui panel rewards", 0.36f, 0.525f, 0.505f, 0.57f, workspace == "rewards" ? "0.16 0.38 0.54 0.98" : "0.10 0.13 0.16 0.96", 10);
            AddUiLabel(container, panel, workspace == "active" ? "Inspect and manage running instances." : workspace == "load" ? "Choose content and load it into an event." : "Profiles, assignments, validation, dry runs, and transaction status.", 0.52f, 0.525f, 0.97f, 0.57f, 8, TextAnchor.MiddleRight, "0.62 0.70 0.78 1");

            if (workspace == "load")
                BuildLayoutsUi(container, panel, player);
            else if (workspace == "rewards")
                BuildRewardsUi(container, panel, player);
            else
                BuildActiveEventsUi(container, panel, player);
        }

        private void BuildRewardsUi(CuiElementContainer container, string panel, BasePlayer player)
        {
            AddUiSection(container, panel, $"Raid-Base Rewards  |  {RewardPayoutStateShort()}", 0.03f, 0.465f, 0.97f, 0.51f);
            if (!HasPlayerAccess(player, RewardsPermission))
            {
                AddUiLabel(container, panel, $"Missing permission: {RewardsPermission}", 0.045f, 0.31f, 0.95f, 0.38f, 10, TextAnchor.MiddleCenter, "0.92 0.48 0.42 1");
                return;
            }

            var profiles = rewardProfiles.Values.OrderBy(value => value.Id).ToList();
            string selectedId;
            if (!uiRewardProfileSelections.TryGetValue(player.userID, out selectedId) || !rewardProfiles.ContainsKey(selectedId))
            {
                selectedId = profiles.FirstOrDefault()?.Id;
                if (selectedId != null) uiRewardProfileSelections[player.userID] = selectedId;
            }
            int page;
            uiRewardProfilePages.TryGetValue(player.userID, out page);
            var pageCount = Math.Max(1, Mathf.CeilToInt(profiles.Count / 5f));
            page = Mathf.Clamp(page, 0, pageCount - 1);
            uiRewardProfilePages[player.userID] = page;

            AddUiButton(container, panel, $"MASTER: {(config.Rewards.Enabled ? "ON" : "OFF")}", "revents.ui reward payout global", 0.045f, 0.425f, 0.115f, 0.458f, RewardGateColor(config.Rewards.Enabled, config.Rewards.Enabled), 6);
            AddUiButton(container, panel, $"AUTO: {(config.Rewards.AutomaticEventPayoutsEnabled ? "ON" : "OFF")}", "revents.ui reward payout automatic", 0.12f, 0.425f, 0.19f, 0.458f, RewardGateColor(config.Rewards.AutomaticEventPayoutsEnabled, config.Rewards.Enabled && config.Rewards.AutomaticEventPayoutsEnabled), 6);
            AddUiButton(container, panel, $"ADMIN: {(config.Rewards.AdminEventPayoutsEnabled ? "ON" : "OFF")}", "revents.ui reward payout admin", 0.195f, 0.425f, 0.275f, 0.458f, RewardGateColor(config.Rewards.AdminEventPayoutsEnabled, config.Rewards.Enabled && config.Rewards.AdminEventPayoutsEnabled), 6);

            AddUiLabel(container, panel, "PROFILES", 0.045f, 0.389f, 0.102f, 0.42f, 7, TextAnchor.MiddleLeft, "0.78 0.84 0.90 1");
            AddUiButton(container, panel, "<", "revents.ui reward page -1", 0.105f, 0.389f, 0.135f, 0.42f, page > 0 ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 8);
            AddUiLabel(container, panel, $"{page + 1}/{pageCount}", 0.137f, 0.389f, 0.18f, 0.42f, 7, TextAnchor.MiddleCenter, "0.62 0.68 0.74 1");
            AddUiButton(container, panel, ">", "revents.ui reward page 1", 0.182f, 0.389f, 0.212f, 0.42f, page + 1 < pageCount ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 8);
            AddUiButton(container, panel, "Reload", "revents.ui reward reload", 0.217f, 0.389f, 0.275f, 0.42f, "0.16 0.30 0.42 0.96", 6);

            var y = 0.345f;
            foreach (var profile in profiles.Skip(page * 5).Take(5))
            {
                var selected = profile.Id == selectedId;
                AddUiButton(container, panel, ShortUiText(selected ? $"> SELECTED: {profile.DisplayName}" : $"Select: {profile.DisplayName}", 34), $"revents.ui reward select {profile.Id}", 0.045f, y, 0.275f, y + 0.035f, selected ? "0.08 0.42 0.58 0.99" : "0.10 0.13 0.16 0.96", 7);
                AddUiLabel(container, panel, ShortUiText(RewardProfileListStatus(profile, selected), 42), 0.055f, y - 0.024f, 0.275f, y, 6, TextAnchor.MiddleLeft, selected ? "0.70 0.88 0.98 1" : "0.52 0.60 0.68 1");
                y -= 0.062f;
            }
            AddUiInput(container, panel, "", "revents.ui reward new", 0.045f, 0.035f, 0.155f, 0.07f, 6, "CREATE ID + ENTER");
            AddUiInput(container, panel, "", "revents.ui reward clone", 0.165f, 0.035f, 0.275f, 0.07f, 6, "CLONE AS ID + ENTER");

            RewardProfile selectedProfile;
            if (selectedId == null || !rewardProfiles.TryGetValue(selectedId, out selectedProfile))
            {
                AddUiLabel(container, panel, "Create a profile ID to begin.", 0.32f, 0.26f, 0.95f, 0.36f, 10, TextAnchor.MiddleCenter, "0.62 0.68 0.74 1");
                return;
            }

            var validation = ValidateRewardProfile(selectedProfile, true);
            AddUiLabel(container, panel, "SELECTED", 0.31f, 0.425f, 0.365f, 0.458f, 6, TextAnchor.MiddleLeft, "0.70 0.88 0.98 1");
            AddUiInput(container, panel, selectedProfile.DisplayName, "revents.ui reward display", 0.37f, 0.425f, 0.59f, 0.458f, 8, "DISPLAY NAME + ENTER");
            AddUiLabel(container, panel, $"[{ShortUiText(selectedProfile.Id, 19)}]", 0.595f, 0.425f, 0.705f, 0.458f, 6, TextAnchor.MiddleLeft, "0.62 0.70 0.78 1");
            AddUiButton(container, panel, "DRY RUN", $"revents.ui reward preview {selectedProfile.Id}", 0.71f, 0.425f, 0.785f, 0.458f, "0.18 0.34 0.25 0.96", 7);
            var automaticDefault = config.Rewards.AutomaticDefaultProfileId == selectedProfile.Id;
            var adminDefault = config.Rewards.AdminDefaultProfileId == selectedProfile.Id;
            AddUiButton(container, panel, automaticDefault ? "AUTO DEFAULT: YES" : "SET AUTO DEFAULT", "revents.ui reward assign automatic", 0.79f, 0.425f, 0.875f, 0.458f, automaticDefault ? "0.08 0.42 0.58 0.99" : "0.16 0.25 0.34 0.96", 6);
            AddUiButton(container, panel, adminDefault ? "ADMIN DEFAULT: YES" : "SET ADMIN DEFAULT", "revents.ui reward assign admin", 0.88f, 0.425f, 0.965f, 0.458f, adminDefault ? "0.08 0.42 0.58 0.99" : "0.16 0.25 0.34 0.96", 6);
            AddUiLabel(container, panel, RewardWorkspaceStatus(selectedProfile, validation), 0.31f, 0.387f, 0.965f, 0.42f, 7, TextAnchor.MiddleLeft, RewardWorkspaceStatusColor(selectedProfile, validation));

            var groupedScope = selectedProfile.ScoreScope != "Player";
            AddUiButton(container, panel, $"PROFILE: {(selectedProfile.Enabled ? "ENABLED" : "DISABLED")}", "revents.ui reward toggle", 0.31f, 0.345f, 0.405f, 0.38f, selectedProfile.Enabled ? "0.14 0.30 0.22 0.96" : "0.40 0.16 0.14 0.96", 6);
            AddUiButton(container, panel, selectedProfile.RewardMode == "PercentagePool" ? "AWARDS: SHARED POOL" : "AWARDS: FIXED BY RANK", "revents.ui reward mode", 0.41f, 0.345f, 0.535f, 0.38f, "0.16 0.25 0.34 0.96", 6);
            AddUiButton(container, panel, $"RANK BY: {RewardScopeLabel(selectedProfile.ScoreScope)}", "revents.ui reward scope", 0.54f, 0.345f, 0.635f, 0.38f, "0.16 0.25 0.34 0.96", 6);
            AddUiButton(container, panel, groupedScope ? $"GROUP SPLIT: {(selectedProfile.GroupDistribution == "Even" ? "EVEN" : "WEIGHTED")}" : "GROUP SPLIT: N/A", groupedScope ? "revents.ui reward split" : "", 0.64f, 0.345f, 0.775f, 0.38f, groupedScope ? "0.16 0.25 0.34 0.96" : "0.11 0.13 0.15 0.90", 6);
            AddUiButton(container, panel, groupedScope ? (selectedProfile.AllowSoloIfNoGroup ? "NO GROUP: SOLO OK" : "NO GROUP: INELIGIBLE") : "NO GROUP: N/A", groupedScope ? "revents.ui reward solo" : "", 0.78f, 0.345f, 0.90f, 0.38f, groupedScope ? "0.16 0.25 0.34 0.96" : "0.11 0.13 0.15 0.90", 6);
            AddUiButton(container, panel, uiRewardDeleteConfirm.Contains(player.userID) ? "CONFIRM DELETE" : "DELETE", "revents.ui reward delete", 0.905f, 0.345f, 0.965f, 0.38f, "0.40 0.16 0.14 0.96", 6);

            AddUiLabel(container, panel, RewardProfileBehaviorSummary(selectedProfile), 0.31f, 0.309f, 0.965f, 0.34f, 6, TextAnchor.MiddleLeft, "0.64 0.72 0.80 1");
            AddUiLabel(container, panel, $"{(groupedScope ? "GROUP" : "PLAYER")} SCORE MIN: {selectedProfile.MinimumGroupScore}", 0.31f, 0.267f, 0.405f, 0.30f, 6, TextAnchor.MiddleLeft, "0.72 0.78 0.84 1");
            AddUiButton(container, panel, "-50", "revents.ui reward min group -50", 0.405f, 0.267f, 0.445f, 0.30f, "0.24 0.18 0.16 0.96", 7);
            AddUiButton(container, panel, "+50", "revents.ui reward min group 50", 0.448f, 0.267f, 0.488f, 0.30f, "0.14 0.30 0.22 0.96", 7);
            AddUiLabel(container, panel, groupedScope ? $"MEMBER SCORE MIN: {selectedProfile.MinimumMemberScore}" : "MEMBER SCORE MIN: N/A", 0.50f, 0.267f, 0.60f, 0.30f, 6, TextAnchor.MiddleLeft, "0.72 0.78 0.84 1");
            AddUiButton(container, panel, "-10", groupedScope ? "revents.ui reward min member -10" : "", 0.60f, 0.267f, 0.64f, 0.30f, groupedScope ? "0.24 0.18 0.16 0.96" : "0.11 0.13 0.15 0.90", 7);
            AddUiButton(container, panel, "+10", groupedScope ? "revents.ui reward min member 10" : "", 0.643f, 0.267f, 0.683f, 0.30f, groupedScope ? "0.14 0.30 0.22 0.96" : "0.11 0.13 0.15 0.90", 7);
            AddUiButton(container, panel, "+ WINNER RANK", "revents.ui reward addplace", 0.695f, 0.267f, 0.79f, 0.30f, "0.14 0.30 0.22 0.96", 6);
            AddUiInput(container, panel, "", "revents.ui reward layout", 0.80f, 0.267f, 0.965f, 0.30f, 6, "LAYOUT ID -> ASSIGN PROFILE");

            var placements = selectedProfile.Placements.OrderBy(value => value.Place).Take(3).ToList();
            y = 0.218f;
            var placementIndex = 0;
            foreach (var placement in placements)
            {
                var rewardRows = selectedProfile.RewardMode == "PercentagePool" ? selectedProfile.Pool : placement.Rewards;
                var first = rewardRows.FirstOrDefault();
                AddUiRowBackground(container, panel, 0.305f, y - 0.006f, 0.97f, y + 0.036f);
                AddUiLabel(container, panel, $"RANK #{placement.Place}", 0.315f, y, 0.39f, y + 0.031f, 7, TextAnchor.MiddleLeft, "0.92 0.95 0.98 1");
                if (selectedProfile.RewardMode == "PercentagePool")
                {
                    AddUiButton(container, panel, "-5%", $"revents.ui reward percent {placement.Place} -5", 0.39f, y, 0.43f, y + 0.032f, "0.24 0.18 0.16 0.96", 6);
                    AddUiButton(container, panel, "+5%", $"revents.ui reward percent {placement.Place} 5", 0.433f, y, 0.473f, y + 0.032f, "0.14 0.30 0.22 0.96", 6);
                    AddUiLabel(container, panel, placementIndex == 0
                        ? ShortUiText($"{placement.Percent:0.##}% OF SHARED POOL | {RewardRowsSummary(rewardRows)}", 58)
                        : $"{placement.Percent:0.##}% OF THE SAME SHARED POOL", 0.48f, y, 0.69f, y + 0.031f, 6, TextAnchor.MiddleLeft, "0.72 0.78 0.84 1");
                }
                else
                {
                    AddUiLabel(container, panel, RewardRowsSummary(rewardRows), 0.395f, y, 0.69f, y + 0.031f, 6, TextAnchor.MiddleLeft, "0.72 0.78 0.84 1");
                }
                var target = selectedProfile.RewardMode == "PercentagePool" ? "pool" : placement.Place.ToString(CultureInfo.InvariantCulture);
                var showPoolControls = selectedProfile.RewardMode != "PercentagePool" || placementIndex == 0;
                if (first != null && showPoolControls)
                {
                    AddUiButton(container, panel, "-100", $"revents.ui reward amount {target} 0 -100", 0.695f, y, 0.74f, y + 0.032f, "0.24 0.18 0.16 0.96", 6);
                    AddUiButton(container, panel, "+100", $"revents.ui reward amount {target} 0 100", 0.743f, y, 0.788f, y + 0.032f, "0.14 0.30 0.22 0.96", 6);
                    AddUiButton(container, panel, "Remove", $"revents.ui reward delreward {target} 0", 0.793f, y, 0.847f, y + 0.032f, "0.40 0.16 0.14 0.96", 6);
                }
                if (showPoolControls)
                {
                    AddUiButton(container, panel, "+RP", $"revents.ui reward addreward {target} RP", 0.852f, y, 0.882f, y + 0.032f, "0.14 0.30 0.22 0.96", 5);
                    AddUiButton(container, panel, "+Item", $"revents.ui reward addreward {target} Item", 0.884f, y, 0.918f, y + 0.032f, "0.14 0.30 0.22 0.96", 5);
                    AddUiButton(container, panel, "+Cmd", $"revents.ui reward addreward {target} Command", 0.92f, y, 0.952f, y + 0.032f, "0.14 0.30 0.22 0.96", 5);
                }
                AddUiButton(container, panel, "X", $"revents.ui reward delplace {placement.Place}", 0.954f, y, 0.97f, y + 0.032f, "0.40 0.16 0.14 0.96", 6);
                placementIndex++;
                y -= 0.047f;
            }

            var editPlacement = selectedProfile.Placements.OrderBy(value => value.Place).FirstOrDefault();
            var editTarget = selectedProfile.RewardMode == "PercentagePool" ? selectedProfile.Pool : editPlacement?.Rewards;
            var editTargetId = selectedProfile.RewardMode == "PercentagePool" ? "pool" : editPlacement?.Place.ToString(CultureInfo.InvariantCulture);
            var editReward = editTarget?.FirstOrDefault();
            if (editReward != null && editReward.Type == "Item")
            {
                AddUiLabel(container, panel, "FIRST ITEM ROW", 0.31f, 0.084f, 0.415f, 0.117f, 6, TextAnchor.MiddleLeft, "0.62 0.70 0.78 1");
                AddUiInput(container, panel, editReward.ShortName, $"revents.ui reward shortname {editTargetId} 0", 0.42f, 0.084f, 0.61f, 0.117f, 7, "ITEM SHORTNAME");
                AddUiInput(container, panel, editReward.SkinId.ToString(CultureInfo.InvariantCulture), $"revents.ui reward skin {editTargetId} 0", 0.615f, 0.084f, 0.75f, 0.117f, 7, "SKIN ID");
            }
            else if (editReward != null && editReward.Type == "Command")
            {
                AddUiLabel(container, panel, "FIRST COMMAND ROW", 0.31f, 0.084f, 0.43f, 0.117f, 6, TextAnchor.MiddleLeft, "0.62 0.70 0.78 1");
                AddUiInput(container, panel, editReward.Command, $"revents.ui reward command {editTargetId} 0", 0.435f, 0.084f, 0.84f, 0.117f, 7, "ALLOWLISTED COMMAND TEMPLATE");
                AddUiButton(container, panel, editReward.RequireOnline ? "PLAYER MUST BE ONLINE" : "OFFLINE ALLOWED", $"revents.ui reward online {editTargetId} 0 {(editReward.RequireOnline ? "false" : "true")}", 0.845f, 0.084f, 0.95f, 0.117f, "0.16 0.25 0.34 0.96", 5);
            }

            var preview = uiRewardPreviews.ContainsKey(player.userID) ? uiRewardPreviews[player.userID] : null;
            if (!string.IsNullOrWhiteSpace(preview))
                AddUiLabel(container, panel, ShortUiText(preview.Replace("\n", " | "), 220), 0.31f, 0.035f, 0.965f, 0.079f, 6, TextAnchor.UpperLeft, "0.66 0.78 0.88 1");
        }

        private string RewardPayoutStateShort()
        {
            if (!config.Rewards.Enabled)
                return "PAYOUTS BLOCKED - MASTER OFF";
            if (!config.Rewards.AutomaticEventPayoutsEnabled && !config.Rewards.AdminEventPayoutsEnabled)
                return "PAYOUTS BLOCKED - NO EVENT SOURCE ON";

            var sources = new List<string>();
            if (config.Rewards.AutomaticEventPayoutsEnabled) sources.Add("AUTO");
            if (config.Rewards.AdminEventPayoutsEnabled) sources.Add("ADMIN");
            return $"PAYOUTS LIVE FOR {string.Join(" + ", sources)} EVENTS";
        }

        private string RewardGateColor(bool enabled, bool effective)
        {
            if (!enabled)
                return "0.40 0.16 0.14 0.96";
            return effective ? "0.14 0.36 0.24 0.98" : "0.42 0.30 0.10 0.98";
        }

        private string RewardProfileListStatus(RewardProfile profile, bool selected)
        {
            var parts = new List<string>();
            if (selected) parts.Add("SELECTED");
            parts.Add(profile.Id);
            parts.Add(profile.Enabled ? "ENABLED" : "DISABLED");
            parts.Add(profile.RewardMode == "PercentagePool" ? "POOL" : "FIXED");
            parts.Add(RewardScopeLabel(profile.ScoreScope));
            if (config.Rewards.AutomaticDefaultProfileId == profile.Id) parts.Add("AUTO DEFAULT");
            if (config.Rewards.AdminDefaultProfileId == profile.Id) parts.Add("ADMIN DEFAULT");
            return string.Join(" | ", parts);
        }

        private string RewardScopeLabel(string scope)
        {
            if (scope == "Player") return "PLAYERS";
            if (scope == "RustTeam") return "TEAMS";
            return "CLANS";
        }

        private string RewardWorkspaceStatus(RewardProfile profile, string validation)
        {
            if (!string.IsNullOrWhiteSpace(validation))
                return ShortUiText("SELECTED PROFILE INVALID: " + validation, 145);
            if (!profile.Enabled)
                return "SELECTED PROFILE BLOCKED - profile is disabled. Changes save immediately and affect newly started events.";

            return ShortUiText($"{RewardPayoutStateShort()} | PROFILE VALID | Changes save immediately; active events keep their start-time snapshot.", 145);
        }

        private string RewardWorkspaceStatusColor(RewardProfile profile, string validation)
        {
            if (!string.IsNullOrWhiteSpace(validation) || !profile.Enabled)
                return "0.92 0.48 0.42 1";
            return config.Rewards.Enabled && (config.Rewards.AutomaticEventPayoutsEnabled || config.Rewards.AdminEventPayoutsEnabled)
                ? "0.60 0.86 0.65 1"
                : "0.94 0.72 0.34 1";
        }

        private string RewardProfileBehaviorSummary(RewardProfile profile)
        {
            var mode = profile.RewardMode == "PercentagePool"
                ? "Shared pool: each rank receives its shown percentage."
                : "Fixed awards: every rank has its own reward rows.";
            var hiddenRanks = profile.Placements.Count > 3 ? $" First 3 of {profile.Placements.Count} ranks shown." : string.Empty;
            if (profile.ScoreScope == "Player")
                return ShortUiText($"{mode} Players rank individually; the player score minimum qualifies them.{hiddenRanks} Blue buttons show current values - click to cycle.", 175);

            var scope = profile.ScoreScope == "RustTeam" ? "Rust teams" : "Clans";
            var split = profile.GroupDistribution == "Even" ? "evenly" : "by member contribution";
            var solo = profile.AllowSoloIfNoGroup ? "Solo players may rank separately." : "Players without a group are ineligible.";
            return ShortUiText($"{mode} {scope} rank by combined score; qualified group rewards split {split}. {solo}{hiddenRanks} Blue buttons show current values.", 175);
        }

        private string RewardRowsSummary(List<RewardDefinition> rewards)
        {
            if (rewards == null || rewards.Count == 0)
                return "NO REWARD ROWS";

            var first = RewardDefinitionSummary(rewards[0], rewards[0].Amount);
            return ShortUiText(rewards.Count == 1 ? first : $"{first} (+{rewards.Count - 1} more rows)", 54);
        }

        private void HandleRewardUiAction(BasePlayer player, string[] args, out string message)
        {
            message = null;
            if (player == null || args == null || args.Length == 0)
                return;
            var action = args[0].ToLowerInvariant();
            if (action == "reload")
            {
                LoadRewardProfiles();
                EnsureDefaultRewardProfile();
                message = $"Reloaded {rewardProfiles.Count} reward profile(s) from JSON.";
                return;
            }
            if (action == "page")
            {
                int delta;
                if (args.Length > 1 && int.TryParse(args[1], out delta))
                {
                    int page;
                    uiRewardProfilePages.TryGetValue(player.userID, out page);
                    uiRewardProfilePages[player.userID] = Math.Max(0, page + delta);
                }
                return;
            }
            if (action == "select")
            {
                if (args.Length > 1 && rewardProfiles.ContainsKey(args[1])) uiRewardProfileSelections[player.userID] = args[1];
                uiRewardDeleteConfirm.Remove(player.userID);
                uiRewardPreviews.Remove(player.userID);
                return;
            }
            if (action == "payout")
            {
                if (args.Length < 2) return;
                if (args[1] == "global") config.Rewards.Enabled = !config.Rewards.Enabled;
                else if (args[1] == "automatic") config.Rewards.AutomaticEventPayoutsEnabled = !config.Rewards.AutomaticEventPayoutsEnabled;
                else if (args[1] == "admin") config.Rewards.AdminEventPayoutsEnabled = !config.Rewards.AdminEventPayoutsEnabled;
                else return;
                SaveConfig();
                message = $"Reward payout switches: global={(config.Rewards.Enabled ? "on" : "off")}, automatic={(config.Rewards.AutomaticEventPayoutsEnabled ? "on" : "off")}, admin/manual={(config.Rewards.AdminEventPayoutsEnabled ? "on" : "off")}. Existing instance snapshots were not changed.";
                return;
            }
            if (action == "new" || action == "clone")
            {
                var requestedId = NormalizeProfileId(args.Length > 1 ? string.Join("-", args.Skip(1).ToArray()) : null);
                if (string.IsNullOrWhiteSpace(requestedId))
                {
                    message = "Enter a profile ID using letters, numbers, dashes, or underscores.";
                    return;
                }
                if (rewardProfiles.ContainsKey(requestedId))
                {
                    message = $"Reward profile '{requestedId}' already exists.";
                    return;
                }
                RewardProfile created;
                RewardProfile selected;
                if (action == "clone" && TryGetSelectedRewardProfile(player.userID, out selected))
                {
                    created = CloneJson(selected);
                    created.Id = requestedId;
                    created.DisplayName = selected.DisplayName + " (Clone)";
                }
                else
                {
                    created = new RewardProfile
                    {
                        Id = requestedId,
                        DisplayName = requestedId.Replace('-', ' '),
                        RewardMode = "FixedPlacements",
                        ScoreScope = "Player",
                        MinimumGroupScore = config.Scoring.MinimumScoreToQualify,
                        MinimumMemberScore = 1,
                        Placements = new List<RewardPlacementDefinition>
                        {
                            new RewardPlacementDefinition { Place = 1, Percent = 100f, Rewards = new List<RewardDefinition> { new RewardDefinition { Type = "RP", Amount = 100 } } }
                        }
                    };
                }
                SaveRewardProfileAndSelect(player.userID, created);
                message = $"Created reward profile '{created.Id}'.";
                return;
            }

            RewardProfile profile;
            if (!TryGetSelectedRewardProfile(player.userID, out profile))
            {
                message = "Select a reward profile first.";
                return;
            }
            if (action == "preview")
            {
                var preview = BuildRewardPreview(profile.Id, args.Length > 2 ? args[2] : null);
                uiRewardPreviews[player.userID] = preview;
                message = preview;
                return;
            }
            if (action == "assign")
            {
                if (args.Length < 2) return;
                if (args[1].Equals("automatic", StringComparison.OrdinalIgnoreCase)) config.Rewards.AutomaticDefaultProfileId = profile.Id;
                else if (args[1].Equals("admin", StringComparison.OrdinalIgnoreCase)) config.Rewards.AdminDefaultProfileId = profile.Id;
                else return;
                SaveConfig();
                message = $"Assigned '{profile.Id}' as the {args[1].ToLowerInvariant()} default; payout enable switches were not changed.";
                return;
            }
            if (action == "layout")
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    message = "Enter a layout ID to assign the selected profile.";
                    return;
                }
                var layoutId = args[1].Trim();
                if (!data.Layouts.ContainsKey(layoutId))
                {
                    message = $"Layout '{layoutId}' has not been discovered.";
                    return;
                }
                config.Rewards.LayoutProfileOverrides[layoutId] = profile.Id;
                SaveConfig();
                message = $"Assigned '{profile.Id}' to layout '{layoutId}'.";
                return;
            }
            if (action == "delete")
            {
                string deletionError;
                if (!CanDeleteRewardProfile(profile.Id, out deletionError))
                {
                    message = deletionError;
                    return;
                }
                if (!uiRewardDeleteConfirm.Remove(player.userID))
                {
                    uiRewardDeleteConfirm.Add(player.userID);
                    message = $"Press Delete again to permanently remove reward profile '{profile.Id}'.";
                    return;
                }
                rewardProfiles.Remove(profile.Id);
                var path = Path.Combine(Interface.Oxide.DataFileSystem.Directory, RewardProfilesDirectory.Replace('/', Path.DirectorySeparatorChar) + profile.Id + ".json");
                if (File.Exists(path)) File.Delete(path);
                uiRewardProfileSelections.Remove(player.userID);
                message = $"Deleted reward profile '{profile.Id}'.";
                return;
            }

            var changed = true;
            switch (action)
            {
                case "toggle":
                    profile.Enabled = !profile.Enabled;
                    break;
                case "mode":
                    profile.RewardMode = profile.RewardMode == "FixedPlacements" ? "PercentagePool" : "FixedPlacements";
                    PrepareProfileForMode(profile);
                    break;
                case "scope":
                    profile.ScoreScope = profile.ScoreScope == "Player" ? "Clan" : profile.ScoreScope == "Clan" ? "RustTeam" : "Player";
                    break;
                case "split":
                    profile.GroupDistribution = profile.GroupDistribution == "Even" ? "ContributionWeighted" : "Even";
                    break;
                case "solo":
                    profile.AllowSoloIfNoGroup = !profile.AllowSoloIfNoGroup;
                    break;
                case "display":
                    if (args.Length > 1) profile.DisplayName = string.Join(" ", args.Skip(1).ToArray()).Trim(); else changed = false;
                    break;
                case "min":
                    int minDelta;
                    if (args.Length < 3 || !int.TryParse(args[2], out minDelta)) { changed = false; break; }
                    if (args[1] == "group") profile.MinimumGroupScore = Math.Max(0, profile.MinimumGroupScore + minDelta);
                    else if (args[1] == "member") profile.MinimumMemberScore = Math.Max(0, profile.MinimumMemberScore + minDelta);
                    else changed = false;
                    break;
                case "addplace":
                    var nextPlace = profile.Placements.Count == 0 ? 1 : profile.Placements.Max(value => value.Place) + 1;
                    profile.Placements.Add(new RewardPlacementDefinition { Place = nextPlace, Percent = 0f, Rewards = new List<RewardDefinition> { new RewardDefinition { Type = "RP", Amount = 100 } } });
                    break;
                case "delplace":
                    int deletePlace;
                    if (args.Length < 2 || !int.TryParse(args[1], out deletePlace)) { changed = false; break; }
                    profile.Placements.RemoveAll(value => value.Place == deletePlace);
                    break;
                case "percent":
                    int percentPlace;
                    float percentDelta = 0f;
                    var percentRow = args.Length >= 3 && int.TryParse(args[1], out percentPlace) && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out percentDelta)
                        ? profile.Placements.FirstOrDefault(value => value.Place == percentPlace) : null;
                    if (percentRow == null) { changed = false; break; }
                    percentRow.Percent = Mathf.Clamp(percentRow.Percent + percentDelta, 0f, 100f);
                    break;
                case "addreward":
                    List<RewardDefinition> addTarget;
                    if (args.Length < 3 || !TryGetRewardTarget(profile, args[1], out addTarget)) { changed = false; break; }
                    var type = NormalizeRewardType(args[2]);
                    addTarget.Insert(0, new RewardDefinition { Type = type, Amount = 100, ShortName = type == "Item" ? "scrap" : null, Command = type == "Command" ? "" : null });
                    break;
                case "delreward":
                    List<RewardDefinition> deleteTarget;
                    int deleteIndex;
                    if (args.Length < 3 || !TryGetRewardTarget(profile, args[1], out deleteTarget) || !int.TryParse(args[2], out deleteIndex) || deleteIndex < 0 || deleteIndex >= deleteTarget.Count) { changed = false; break; }
                    deleteTarget.RemoveAt(deleteIndex);
                    break;
                case "amount":
                    List<RewardDefinition> amountTarget;
                    int amountIndex, amountDelta;
                    if (args.Length < 4 || !TryGetRewardTarget(profile, args[1], out amountTarget) || !int.TryParse(args[2], out amountIndex) || !int.TryParse(args[3], out amountDelta) || amountIndex < 0 || amountIndex >= amountTarget.Count) { changed = false; break; }
                    amountTarget[amountIndex].Amount = Math.Max(0, amountTarget[amountIndex].Amount + amountDelta);
                    break;
                case "shortname":
                case "skin":
                case "command":
                case "online":
                    changed = EditRewardDefinition(profile, action, args);
                    break;
                default:
                    changed = false;
                    break;
            }
            if (!changed) return;
            SaveRewardProfileAndSelect(player.userID, profile);
            uiRewardPreviews.Remove(player.userID);
            uiRewardDeleteConfirm.Remove(player.userID);
        }

        private bool TryGetSelectedRewardProfile(ulong userId, out RewardProfile profile)
        {
            profile = null;
            string id;
            if (uiRewardProfileSelections.TryGetValue(userId, out id) && rewardProfiles.TryGetValue(id, out profile)) return true;
            profile = rewardProfiles.Values.OrderBy(value => value.Id).FirstOrDefault();
            if (profile != null) uiRewardProfileSelections[userId] = profile.Id;
            return profile != null;
        }

        private void SaveRewardProfileAndSelect(ulong userId, RewardProfile profile)
        {
            NormalizeRewardProfile(profile);
            rewardProfiles[profile.Id] = profile;
            SaveRewardProfile(profile);
            uiRewardProfileSelections[userId] = profile.Id;
        }

        private void PrepareProfileForMode(RewardProfile profile)
        {
            if (profile.Placements.Count == 0) profile.Placements.Add(new RewardPlacementDefinition { Place = 1 });
            if (profile.RewardMode == "PercentagePool")
            {
                if (profile.Pool.Count == 0) profile.Pool.Add(new RewardDefinition { Type = "RP", Amount = 1000 });
                var equal = (float)Math.Floor(10000d / profile.Placements.Count) / 100f;
                for (var index = 0; index < profile.Placements.Count; index++) profile.Placements[index].Percent = index + 1 == profile.Placements.Count ? 100f - equal * index : equal;
            }
            else
            {
                foreach (var placement in profile.Placements)
                    if (placement.Rewards.Count == 0) placement.Rewards.Add(new RewardDefinition { Type = "RP", Amount = 100 });
            }
        }

        private bool TryGetRewardTarget(RewardProfile profile, string target, out List<RewardDefinition> rewards)
        {
            rewards = null;
            if (target.Equals("pool", StringComparison.OrdinalIgnoreCase))
            {
                rewards = profile.Pool;
                return true;
            }
            int place;
            var placement = int.TryParse(target, out place) ? profile.Placements.FirstOrDefault(value => value.Place == place) : null;
            if (placement == null) return false;
            rewards = placement.Rewards;
            return true;
        }

        private bool EditRewardDefinition(RewardProfile profile, string action, string[] args)
        {
            List<RewardDefinition> target;
            int index;
            if (args.Length < 4 || !TryGetRewardTarget(profile, args[1], out target) || !int.TryParse(args[2], out index) || index < 0 || index >= target.Count) return false;
            var reward = target[index];
            if (action == "shortname") reward.ShortName = string.Join(" ", args.Skip(3).ToArray()).Trim().ToLowerInvariant();
            else if (action == "command") reward.Command = string.Join(" ", args.Skip(3).ToArray()).Trim();
            else if (action == "skin")
            {
                ulong skin;
                if (!ulong.TryParse(args[3], out skin)) return false;
                reward.SkinId = skin;
            }
            else if (action == "online") reward.RequireOnline = args[3].Equals("true", StringComparison.OrdinalIgnoreCase) || args[3] == "1" || args[3].Equals("on", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private bool CanDeleteRewardProfile(string profileId, out string error)
        {
            error = null;
            if (profileId == DefaultRewardProfileId) { error = "The migrated default reward profile cannot be deleted."; return false; }
            if (string.Equals(config.Rewards.AutomaticDefaultProfileId, profileId, StringComparison.OrdinalIgnoreCase) || string.Equals(config.Rewards.AdminDefaultProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            { error = "This profile is assigned as a trigger default. Reassign it before deletion."; return false; }
            if (config.Rewards.LayoutProfileOverrides.Values.Any(value => string.Equals(value, profileId, StringComparison.OrdinalIgnoreCase)))
            { error = "This profile is assigned to one or more layouts. Remove those assignments before deletion."; return false; }
            if (data.ActiveRaidBases.Values.Any(value => value != null && string.Equals(value.RewardProfileId, profileId, StringComparison.OrdinalIgnoreCase)))
            { error = "This profile is snapshotted on an active event and cannot be deleted yet."; return false; }
            return true;
        }

        private void BuildActiveEventsUi(CuiElementContainer container, string panel, BasePlayer player)
        {
            AddUiSection(container, panel, "Running Event Instances", 0.03f, 0.465f, 0.97f, 0.51f);

            string eventTab;
            if (!uiActiveEventTabs.TryGetValue(player.userID, out eventTab))
                eventTab = "all";
            string sort;
            if (!uiActiveSorts.TryGetValue(player.userID, out sort))
                sort = "newest";
            string filter;
            if (!uiActiveFilters.TryGetValue(player.userID, out filter))
                filter = "all";

            var activeSource = data.ActiveRaidBases.Values.Where(active => active.Status != "cleaning").ToList();
            var automaticCount = activeSource.Count(IsActiveAutomaticBase);
            var raidBaseCount = activeSource.Count - automaticCount;
            AddUiButton(container, panel, $"All {activeSource.Count}", "revents.ui activetab all", 0.045f, 0.418f, 0.12f, 0.455f, eventTab == "all" ? "0.16 0.38 0.54 0.98" : "0.11 0.14 0.17 0.96", 8);
            AddUiButton(container, panel, $"Automatic Bases {automaticCount}", "revents.ui activetab automatic", 0.125f, 0.418f, 0.255f, 0.455f, eventTab == "automatic" ? "0.16 0.38 0.54 0.98" : "0.11 0.14 0.17 0.96", 8);
            AddUiButton(container, panel, $"Loaded Raid Bases {raidBaseCount}", "revents.ui activetab loaded", 0.26f, 0.418f, 0.395f, 0.455f, eventTab == "loaded" ? "0.16 0.38 0.54 0.98" : "0.11 0.14 0.17 0.96", 8);
            AddUiLabel(container, panel, "Sort", 0.48f, 0.418f, 0.515f, 0.455f, 7, TextAnchor.MiddleRight, "0.52 0.60 0.68 1");
            AddUiButton(container, panel, sort == "newest" ? "Newest first" : "Oldest first", sort == "newest" ? "revents.ui activesort oldest" : "revents.ui activesort newest", 0.52f, 0.418f, 0.62f, 0.455f, "0.16 0.25 0.34 0.96", 7);
            AddUiLabel(container, panel, "Show", 0.63f, 0.418f, 0.67f, 0.455f, 7, TextAnchor.MiddleRight, "0.52 0.60 0.68 1");
            AddUiButton(container, panel, filter == "all" ? "All visibility" : filter == "public" ? "Public only" : "Hidden only", filter == "all" ? "revents.ui activefilter public" : filter == "public" ? "revents.ui activefilter hidden" : "revents.ui activefilter all", 0.675f, 0.418f, 0.785f, 0.455f, "0.16 0.25 0.34 0.96", 7);

            IEnumerable<ActiveRaidBase> query = activeSource;
            if (eventTab == "automatic") query = query.Where(IsActiveAutomaticBase);
            if (eventTab == "loaded") query = query.Where(active => !IsActiveAutomaticBase(active));
            if (filter == "public") query = query.Where(active => active.IsAnnounced);
            if (filter == "hidden") query = query.Where(active => !active.IsAnnounced);
            query = sort == "oldest" ? query.OrderBy(active => active.StartedUnix) : query.OrderByDescending(active => active.StartedUnix);
            var allActiveEvents = query.ToList();
            int page;
            uiActivePages.TryGetValue(player.userID, out page);
            var pageCount = Math.Max(1, Mathf.CeilToInt(allActiveEvents.Count / 4f));
            page = Mathf.Clamp(page, 0, pageCount - 1);
            uiActivePages[player.userID] = page;
            var activeEvents = allActiveEvents.Skip(page * 4).Take(4).ToList();
            AddUiButton(container, panel, "<", "revents.ui activepage -1", 0.805f, 0.418f, 0.84f, 0.455f, page > 0 ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 8);
            AddUiLabel(container, panel, $"Page {page + 1}/{pageCount}", 0.845f, 0.418f, 0.915f, 0.455f, 7, TextAnchor.MiddleCenter, "0.62 0.68 0.74 1");
            AddUiButton(container, panel, ">", "revents.ui activepage 1", 0.92f, 0.418f, 0.955f, 0.455f, page + 1 < pageCount ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 8);

            if (activeEvents.Count == 0)
            {
                AddUiLabel(container, panel, "No active instances match this event tab and visibility filter.", 0.045f, 0.31f, 0.95f, 0.37f, 10, TextAnchor.MiddleCenter, "0.62 0.68 0.74 1");
                return;
            }

            var y = 0.368f;
            foreach (var active in activeEvents)
            {
                var position = active.Position.ToVector3();
                AddUiRowBackground(container, panel, y - 0.006f, y + 0.037f);
                AddUiLabel(container, panel, ShortUiText(active.InstanceId, 18), 0.045f, y, 0.175f, y + 0.032f, 8, TextAnchor.MiddleLeft, "0.92 0.95 0.98 1");
                AddUiLabel(container, panel, ShortUiText(active.LayoutId, 20), 0.18f, y, 0.32f, y + 0.032f, 8, TextAnchor.MiddleLeft, "0.76 0.82 0.88 1");
                AddUiLabel(container, panel, IsActiveAutomaticBase(active) ? "Automatic Bases" : $"Loaded / {active.TriggerType}", 0.325f, y, 0.445f, y + 0.032f, 7, TextAnchor.MiddleLeft, IsActiveAutomaticBase(active) ? "0.60 0.86 0.65 1" : "0.56 0.72 0.90 1");
                AddUiLabel(container, panel, $"{active.Status}/{(active.IsAnnounced ? "public" : "hidden")}", 0.45f, y, 0.535f, y + 0.032f, 7, TextAnchor.MiddleLeft, active.IsAnnounced ? "0.82 0.74 0.42 1" : "0.62 0.68 0.74 1");
                AddUiLabel(container, panel, $"{active.EntityIds?.Count ?? 0} ents", 0.54f, y, 0.60f, y + 0.032f, 7, TextAnchor.MiddleLeft, "0.62 0.68 0.74 1");
                AddUiLabel(container, panel, FormatVector(position), 0.605f, y, 0.775f, y + 0.032f, 7, TextAnchor.MiddleLeft, "0.62 0.68 0.74 1");
                AddUiButton(container, panel, "Score", $"revents.ui score {active.InstanceId}", 0.785f, y - 0.001f, 0.85f, y + 0.035f, "0.16 0.30 0.42 0.96", 8);
                AddUiButton(container, panel, "TP", $"revents.ui tp {active.InstanceId}", 0.855f, y - 0.001f, 0.90f, y + 0.035f, "0.16 0.30 0.42 0.96", 8);
                AddUiButton(container, panel, "End", $"revents.ui stop {active.InstanceId}", 0.905f, y - 0.001f, 0.955f, y + 0.035f, "0.40 0.16 0.14 0.96", 8);
                y -= 0.048f;
            }
        }

        private void BuildLayoutsUi(CuiElementContainer container, string panel, BasePlayer player)
        {
            AddUiSection(container, panel, "Load Raid Base", 0.03f, 0.465f, 0.97f, 0.51f);
            AddUiLabel(container, panel, "Pick a CopyPaste layout, adjust its Automatic Bases weight, or start this layout manually.", 0.045f, 0.42f, 0.72f, 0.455f, 8, TextAnchor.MiddleLeft, "0.62 0.70 0.78 1");

            var configuredLayoutIds = new HashSet<string>(config.EventTypes.AutomaticBases.Layouts.Select(layout => layout.LayoutId), StringComparer.OrdinalIgnoreCase);
            var allLayouts = data.Layouts.Values
                .OrderBy(layout => !configuredLayoutIds.Contains(layout.LayoutId))
                .ThenBy(layout => layout.Ignored)
                .ThenBy(layout => layout.LayoutId)
                .ToList();
            int page;
            uiLayoutPages.TryGetValue(player.userID, out page);
            var pageCount = Math.Max(1, Mathf.CeilToInt(allLayouts.Count / 5f));
            page = Mathf.Clamp(page, 0, pageCount - 1);
            uiLayoutPages[player.userID] = page;
            var layouts = allLayouts.Skip(page * 5).Take(5).ToList();
            if (layouts.Count == 0)
            {
                AddUiLabel(container, panel, "No layouts discovered. Use Scan in the top controls.", 0.045f, 0.31f, 0.95f, 0.37f, 10, TextAnchor.MiddleCenter, "0.62 0.68 0.74 1");
                return;
            }

            AddUiButton(container, panel, "<", "revents.ui layoutpage -1", 0.805f, 0.418f, 0.84f, 0.455f, page > 0 ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 8);
            AddUiLabel(container, panel, $"Page {page + 1}/{pageCount}", 0.845f, 0.418f, 0.915f, 0.455f, 7, TextAnchor.MiddleCenter, "0.62 0.68 0.74 1");
            AddUiButton(container, panel, ">", "revents.ui layoutpage 1", 0.92f, 0.418f, 0.955f, 0.455f, page + 1 < pageCount ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 8);

            var y = 0.368f;
            foreach (var layout in layouts)
            {
                AddUiRowBackground(container, panel, y - 0.006f, y + 0.037f);
                AddUiLabel(container, panel, ShortUiText(layout.LayoutId, 28), 0.045f, y, 0.245f, y + 0.032f, 9, TextAnchor.MiddleLeft, "0.92 0.95 0.98 1");
                AddUiLabel(container, panel, LayoutUiState(layout), 0.25f, y, 0.405f, y + 0.032f, 8, TextAnchor.MiddleLeft, LayoutUiStateColor(layout));
                AddUiLabel(container, panel, layout.EntityCount.ToString(CultureInfo.InvariantCulture), 0.41f, y, 0.50f, y + 0.032f, 8, TextAnchor.MiddleLeft, "0.72 0.78 0.84 1");
                AddUiLabel(container, panel, layout.HasToolCupboard ? "Yes" : "No", 0.515f, y, 0.575f, y + 0.032f, 8, TextAnchor.MiddleLeft, layout.HasToolCupboard ? "0.60 0.86 0.65 1" : "0.92 0.48 0.42 1");
                var weighted = config.EventTypes.AutomaticBases.Layouts.FirstOrDefault(entry => entry != null && string.Equals(entry.LayoutId, layout.LayoutId, StringComparison.OrdinalIgnoreCase));
                AddUiLabel(container, panel, weighted == null ? "-" : weighted.Weight.ToString("0.#", CultureInfo.InvariantCulture), 0.58f, y, 0.645f, y + 0.032f, 8, TextAnchor.MiddleLeft, "0.82 0.74 0.42 1");

                if (!layout.Ignored && layout.Valid)
                {
                    var enabled = IsEnabledLayout(layout.LayoutId);
                    AddUiButton(container, panel, "- Weight", $"revents.ui weight {layout.LayoutId} -0.5", 0.645f, y - 0.001f, 0.68f, y + 0.035f, "0.24 0.18 0.16 0.96", 6);
                    AddUiButton(container, panel, "+ Weight", $"revents.ui weight {layout.LayoutId} 0.5", 0.685f, y - 0.001f, 0.72f, y + 0.035f, "0.14 0.30 0.22 0.96", 6);
                    AddUiButton(container, panel, enabled ? "Off" : "On", enabled ? $"revents.ui disable {layout.LayoutId}" : $"revents.ui enable {layout.LayoutId}", 0.73f, y - 0.001f, 0.79f, y + 0.035f, enabled ? "0.34 0.20 0.14 0.96" : "0.14 0.30 0.22 0.96", 8);
                    AddUiButton(container, panel, "Loot", $"revents.ui lootopen {layout.LayoutId}", 0.795f, y - 0.001f, 0.84f, y + 0.035f, "0.30 0.23 0.10 0.96", 7);
                    AddUiButton(container, panel, "Here", enabled ? $"revents.ui start {layout.LayoutId} here" : "revents.ui refresh", 0.845f, y - 0.001f, 0.895f, y + 0.035f, enabled ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 7);
                    AddUiButton(container, panel, "Random", enabled ? $"revents.ui start {layout.LayoutId} random" : "revents.ui refresh", 0.90f, y - 0.001f, 0.95f, y + 0.035f, enabled ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 7);
                }

                y -= 0.048f;
            }
        }

        private void BuildLootEditorUi(CuiElementContainer container, string panel, BasePlayer player)
        {
            LootEditorState editor;
            if (!lootEditors.TryGetValue(player.userID, out editor))
                return;

            container.Add(new CuiPanel { Image = { Color = "0 0 0 0.72" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, panel);
            var modal = container.Add(new CuiPanel
            {
                Image = { Color = "0.025 0.032 0.04 0.995" },
                RectTransform = { AnchorMin = "0.015 0.025", AnchorMax = "0.985 0.975" }
            }, panel);

            var descriptors = ReadLayoutContainers(editor.LayoutId);
            var current = descriptors.FirstOrDefault(entry => entry.Fingerprint == editor.ContainerFingerprint);
            if (current == null && descriptors.Count > 0)
            {
                LoadLootEditorContainer(editor, descriptors[0]);
                current = descriptors[0];
            }

            AddUiLabel(container, modal, $"<b>Raid Base Loot Editor</b>  |  {ShortUiText(editor.LayoutId, 32)}", 0.025f, 0.925f, 0.68f, 0.982f, 17, TextAnchor.MiddleLeft, "0.95 0.98 1 1");
            AddUiLabel(container, modal, "Overrides affect RaidlandsEvents only. Copied layouts are never rewritten.", 0.40f, 0.93f, 0.82f, 0.975f, 8, TextAnchor.MiddleRight, "0.58 0.68 0.76 1");
            AddUiButton(container, modal, "Discard / Close", "revents.ui lootdiscard", 0.835f, 0.932f, 0.975f, 0.975f, "0.38 0.14 0.14 0.96", 9);

            AddUiSection(container, modal, "Containers", 0.025f, 0.855f, 0.245f, 0.91f);
            const int containersPerPage = 10;
            var containerPages = Math.Max(1, Mathf.CeilToInt(descriptors.Count / (float)containersPerPage));
            editor.ContainerPage = Mathf.Clamp(editor.ContainerPage, 0, containerPages - 1);
            var containerY = 0.80f;
            foreach (var pair in descriptors.Select((value, index) => new { value, index }).Skip(editor.ContainerPage * containersPerPage).Take(containersPerPage))
            {
                Dictionary<string, ContainerLootOverride> saved;
                var overridden = data.LayoutLootOverrides.TryGetValue(editor.LayoutId, out saved) && saved.ContainsKey(pair.value.Fingerprint);
                var active = pair.value.Fingerprint == editor.ContainerFingerprint;
                var label = $"{pair.value.Label}\n{(overridden ? "OVERRIDE" : "COPIED")}  {FormatVector(pair.value.LocalPosition)}";
                AddUiButton(container, modal, label, $"revents.ui lootcontainer {pair.index}", 0.03f, containerY, 0.24f, containerY + 0.047f, active ? "0.16 0.38 0.54 0.98" : overridden ? "0.30 0.23 0.10 0.96" : "0.08 0.11 0.14 0.96", 7);
                containerY -= 0.052f;
            }
            AddUiButton(container, modal, "<", "revents.ui lootcontainerpage -1", 0.03f, 0.245f, 0.075f, 0.282f, "0.12 0.22 0.30 0.96", 8);
            AddUiLabel(container, modal, $"{editor.ContainerPage + 1}/{containerPages}", 0.08f, 0.245f, 0.185f, 0.282f, 8, TextAnchor.MiddleCenter, "0.65 0.72 0.79 1");
            AddUiButton(container, modal, ">", "revents.ui lootcontainerpage 1", 0.19f, 0.245f, 0.24f, 0.282f, "0.12 0.22 0.30 0.96", 8);

            if (current == null)
            {
                AddUiLabel(container, modal, "No supported TC or storage containers were found in this CopyPaste layout.", 0.27f, 0.48f, 0.96f, 0.56f, 12, TextAnchor.MiddleCenter, "0.90 0.55 0.42 1");
                return;
            }

            AddUiSection(container, modal, $"{current.Label}  |  {current.Capacity} slots  |  {FormatVector(current.LocalPosition)}", 0.26f, 0.855f, 0.665f, 0.91f);
            const int columns = 8;
            const int slotsPerPage = 24;
            var slotPages = Math.Max(1, Mathf.CeilToInt(current.Capacity / (float)slotsPerPage));
            editor.SlotPage = Mathf.Clamp(editor.SlotPage, 0, slotPages - 1);
            var slotWidth = 0.047f;
            var slotHeight = 0.085f;
            var slotStart = editor.SlotPage * slotsPerPage;
            var slotEnd = Math.Min(current.Capacity, slotStart + slotsPerPage);
            for (var slot = slotStart; slot < slotEnd; slot++)
            {
                var visibleSlot = slot - slotStart;
                var column = visibleSlot % columns;
                var row = visibleSlot / columns;
                var x1 = 0.267f + column * 0.049f;
                var y2 = 0.825f - row * 0.09f;
                var y1 = y2 - slotHeight;
                var draftItem = editor.DraftItems.FirstOrDefault(item => item != null && item.Position == slot);
                container.Add(new CuiPanel { Image = { Color = slot == editor.SelectedSlot ? "0.18 0.42 0.60 0.98" : "0.10 0.13 0.16 0.98" }, RectTransform = { AnchorMin = UiAnchor(x1, y1), AnchorMax = UiAnchor(x1 + slotWidth, y2) } }, modal);
                if (draftItem != null)
                {
                    var definition = ItemManager.FindItemDefinition(draftItem.ShortName);
                    if (definition != null) AddUiItemImage(container, modal, definition.itemid, 0, x1 + 0.004f, y1 + 0.017f, x1 + slotWidth - 0.004f, y2 - 0.004f);
                    AddUiLabel(container, modal, draftItem.Amount.ToString("n0", CultureInfo.InvariantCulture), x1 + 0.002f, y1, x1 + slotWidth - 0.002f, y1 + 0.022f, 7, TextAnchor.LowerRight, "1 1 1 1");
                }
                AddUiButton(container, modal, string.Empty, $"revents.ui lootslot {slot}", x1, y1, x1 + slotWidth, y2, "0 0 0 0", 1);
            }

            AddUiButton(container, modal, "< Slots", "revents.ui lootslotpage -1", 0.27f, 0.49f, 0.34f, 0.53f, "0.12 0.22 0.30 0.96", 7);
            AddUiLabel(container, modal, $"Slots {editor.SlotPage + 1}/{slotPages}", 0.345f, 0.49f, 0.57f, 0.53f, 8, TextAnchor.MiddleCenter, "0.65 0.72 0.79 1");
            AddUiButton(container, modal, "Slots >", "revents.ui lootslotpage 1", 0.575f, 0.49f, 0.655f, 0.53f, "0.12 0.22 0.30 0.96", 7);

            AddUiButton(container, modal, "Use Copied", "revents.ui lootreset", 0.27f, 0.20f, 0.365f, 0.245f, "0.16 0.25 0.32 0.96", 8);
            AddUiButton(container, modal, "Clear All", "revents.ui lootclearall", 0.375f, 0.20f, 0.46f, 0.245f, "0.34 0.18 0.12 0.96", 8);
            AddUiButton(container, modal, "Save Override", "revents.ui lootsave", 0.47f, 0.20f, 0.655f, 0.245f, "0.12 0.36 0.22 0.98", 10);

            AddUiSection(container, modal, "Item Picker", 0.68f, 0.855f, 0.975f, 0.91f);
            AddUiInput(container, modal, editor.Search, "revents.ui lootsearch", 0.69f, 0.80f, 0.965f, 0.845f, 10, "Search display name or shortname...");
            var catalog = ItemManager.itemList
                .Where(definition => definition != null && (string.IsNullOrWhiteSpace(editor.Search) || definition.shortname.IndexOf(editor.Search, StringComparison.OrdinalIgnoreCase) >= 0 || definition.displayName.english.IndexOf(editor.Search, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(definition => definition.displayName.english)
                .ToList();
            const int itemsPerPage = 8;
            var itemPages = Math.Max(1, Mathf.CeilToInt(catalog.Count / (float)itemsPerPage));
            editor.ItemPage = Mathf.Clamp(editor.ItemPage, 0, itemPages - 1);
            var itemY = 0.755f;
            foreach (var definition in catalog.Skip(editor.ItemPage * itemsPerPage).Take(itemsPerPage))
            {
                AddUiItemImage(container, modal, definition.itemid, 0, 0.69f, itemY - 0.003f, 0.725f, itemY + 0.037f);
                AddUiButton(container, modal, $"{ShortUiText(definition.displayName.english, 24)}  [{definition.shortname}]", $"revents.ui lootpick {definition.shortname}", 0.73f, itemY - 0.003f, 0.965f, itemY + 0.037f, "0.08 0.12 0.15 0.96", 7);
                itemY -= 0.043f;
            }
            AddUiButton(container, modal, "<", "revents.ui lootitempage -1", 0.69f, 0.235f, 0.735f, 0.272f, "0.12 0.22 0.30 0.96", 8);
            AddUiLabel(container, modal, $"Items {editor.ItemPage + 1}/{itemPages}", 0.74f, 0.235f, 0.91f, 0.272f, 8, TextAnchor.MiddleCenter, "0.65 0.72 0.79 1");
            AddUiButton(container, modal, ">", "revents.ui lootitempage 1", 0.92f, 0.235f, 0.965f, 0.272f, "0.12 0.22 0.30 0.96", 8);

            var selected = SelectedDraftItem(editor);
            AddUiLabel(container, modal, editor.SelectedSlot < 0 ? "Select a slot to edit it." : selected == null ? $"Slot {editor.SelectedSlot + 1}: empty" : $"Slot {editor.SelectedSlot + 1}: {selected.ShortName} x{selected.Amount:n0} skin={selected.Skin}", 0.69f, 0.175f, 0.965f, 0.218f, 8, TextAnchor.MiddleLeft, "0.78 0.84 0.90 1");
            if (editor.SelectedSlot >= 0)
            {
                AddUiButton(container, modal, "-1", "revents.ui lootamount -1", 0.69f, 0.125f, 0.735f, 0.165f, "0.28 0.16 0.12 0.96", 8);
                AddUiInput(container, modal, selected?.Amount.ToString(CultureInfo.InvariantCulture) ?? "1", "revents.ui lootamount", 0.74f, 0.125f, 0.82f, 0.165f, 8, "Qty");
                AddUiButton(container, modal, "+1", "revents.ui lootamount +1", 0.825f, 0.125f, 0.87f, 0.165f, "0.12 0.30 0.18 0.96", 8);
                AddUiInput(container, modal, selected?.Skin.ToString(CultureInfo.InvariantCulture) ?? "0", "revents.ui lootskin", 0.875f, 0.125f, 0.965f, 0.165f, 8, "Skin ID");
                AddUiButton(container, modal, "Clear Slot", "revents.ui lootclear", 0.69f, 0.075f, 0.965f, 0.112f, "0.38 0.14 0.14 0.96", 8);
            }

            Dictionary<string, ContainerLootOverride> knownOverrides;
            if (data.LayoutLootOverrides.TryGetValue(editor.LayoutId, out knownOverrides))
            {
                var fingerprints = new HashSet<string>(descriptors.Select(entry => entry.Fingerprint), StringComparer.OrdinalIgnoreCase);
                var orphan = knownOverrides.Keys.FirstOrDefault(key => !fingerprints.Contains(key));
                if (orphan != null)
                    AddUiButton(container, modal, $"Delete orphan: {ShortUiText(orphan, 38)}", $"revents.ui lootorphandelete {StableUiToken(orphan)}", 0.03f, 0.12f, 0.24f, 0.19f, "0.38 0.14 0.14 0.96", 7);
            }
        }

        private void BuildScoreModalUi(CuiElementContainer container, string panel, string instanceId)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.58" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, panel);

            var modal = container.Add(new CuiPanel
            {
                Image = { Color = "0.035 0.043 0.052 0.99" },
                RectTransform = { AnchorMin = "0.105 0.17", AnchorMax = "0.895 0.82" }
            }, panel);

            ActiveRaidBase active;
            if (!data.ActiveRaidBases.TryGetValue(instanceId, out active))
            {
                AddUiLabel(container, modal, "<b>Scoreboard</b>", 0.035f, 0.895f, 0.55f, 0.965f, 17, TextAnchor.MiddleLeft, "0.95 0.98 1 1");
                AddUiButton(container, modal, "Close", "revents.ui refresh", 0.82f, 0.90f, 0.965f, 0.958f, "0.34 0.16 0.15 0.96", 11);
                AddUiLabel(container, modal, $"No active raid base instance '{ShortUiText(instanceId, 32)}' was found.", 0.05f, 0.48f, 0.95f, 0.56f, 12, TextAnchor.MiddleCenter, "0.82 0.88 0.94 1");
                return;
            }

            NormalizeActiveRaidBase(active);
            var leaderboard = BuildLeaderboard(active, false);
            AddUiLabel(container, modal, $"<b>{ShortUiText(active.PublicName, 28)} Scoreboard</b>", 0.035f, 0.895f, 0.48f, 0.965f, 17, TextAnchor.MiddleLeft, "0.95 0.98 1 1");
            AddUiLabel(container, modal, $"{ShortUiText(active.InstanceId, 18)} | {active.Status} | radius {active.ScoreRadiusMeters:0}m | entries {leaderboard.Count}", 0.49f, 0.905f, 0.80f, 0.955f, 9, TextAnchor.MiddleRight, "0.62 0.70 0.78 1");
            AddUiButton(container, modal, "Refresh", $"revents.ui score {active.InstanceId}", 0.815f, 0.90f, 0.89f, 0.958f, "0.16 0.30 0.42 0.96", 9);
            AddUiButton(container, modal, "Close", "revents.ui refresh", 0.898f, 0.90f, 0.965f, 0.958f, "0.34 0.16 0.15 0.96", 9);

            AddUiSection(container, modal, "Leaderboard", 0.035f, 0.795f, 0.965f, 0.85f);
            AddUiScoreHeader(container, modal, 0.742f);

            if (leaderboard.Count == 0)
            {
                AddUiLabel(container, modal, "No scoring entries yet.", 0.05f, 0.50f, 0.95f, 0.58f, 12, TextAnchor.MiddleCenter, "0.70 0.77 0.84 1");
                AddUiLabel(container, modal, "Damage, kills, and TC credit will appear here once players fight inside the event radius.", 0.08f, 0.44f, 0.92f, 0.50f, 9, TextAnchor.MiddleCenter, "0.52 0.60 0.68 1");
            }
            else
            {
                var y = 0.685f;
                var rows = Math.Min(8, leaderboard.Count);
                for (var index = 0; index < rows; index++)
                {
                    AddUiScoreRow(container, modal, index + 1, leaderboard[index], y);
                    y -= 0.064f;
                }

                if (leaderboard.Count > rows)
                    AddUiLabel(container, modal, $"+{leaderboard.Count - rows} more scoring entries. Use revents.score {active.InstanceId} for full text output.", 0.05f, 0.085f, 0.95f, 0.13f, 8, TextAnchor.MiddleLeft, "0.54 0.62 0.70 1");
            }

            var rewardRows = rewardLedger.Transactions.Values.Where(value => value != null && value.InstanceId == active.InstanceId).ToList();
            AddUiLabel(container, modal, $"Profile: {active.RewardProfileId ?? "none"} | payout {(active.RewardPayoutEnabled ? "enabled" : "disabled")} | paid {rewardRows.Count(value => value.Status == "paid")} | pending/review {rewardRows.Count(value => value.Status != "paid")}", 0.05f, 0.035f, 0.78f, 0.078f, 8, TextAnchor.MiddleLeft, "0.58 0.66 0.74 1");
            AddUiButton(container, modal, "Retry Pending", "revents.ui retryrewards", 0.80f, 0.03f, 0.955f, 0.082f, "0.16 0.30 0.42 0.96", 9);
        }

        private void AddUiScoreHeader(CuiElementContainer container, string parent, float y)
        {
            AddUiLabel(container, parent, "#", 0.05f, y, 0.085f, y + 0.04f, 8, TextAnchor.MiddleCenter, "0.50 0.58 0.66 1");
            AddUiLabel(container, parent, "Player", 0.095f, y, 0.30f, y + 0.04f, 8, TextAnchor.MiddleLeft, "0.50 0.58 0.66 1");
            AddUiLabel(container, parent, "Score", 0.31f, y, 0.40f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.50 0.58 0.66 1");
            AddUiLabel(container, parent, "K", 0.43f, y, 0.475f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.50 0.58 0.66 1");
            AddUiLabel(container, parent, "D", 0.50f, y, 0.545f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.50 0.58 0.66 1");
            AddUiLabel(container, parent, "PvP", 0.57f, y, 0.65f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.50 0.58 0.66 1");
            AddUiLabel(container, parent, "Base", 0.67f, y, 0.75f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.50 0.58 0.66 1");
            AddUiLabel(container, parent, "Boom", 0.77f, y, 0.85f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.50 0.58 0.66 1");
            AddUiLabel(container, parent, "TC", 0.88f, y, 0.93f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.50 0.58 0.66 1");
        }

        private void AddUiScoreRow(CuiElementContainer container, string parent, int rank, RaidBaseScoreEntry score, float y)
        {
            AddUiRowBackground(container, parent, y - 0.006f, y + 0.045f);
            AddUiLabel(container, parent, rank.ToString(CultureInfo.InvariantCulture), 0.05f, y, 0.085f, y + 0.04f, 9, TextAnchor.MiddleCenter, "0.86 0.90 0.94 1");
            AddUiLabel(container, parent, ShortUiText(score.DisplayName ?? score.UserId, 22), 0.095f, y, 0.30f, y + 0.04f, 9, TextAnchor.MiddleLeft, "0.92 0.95 0.98 1");
            AddUiLabel(container, parent, score.TotalScore.ToString(CultureInfo.InvariantCulture), 0.31f, y, 0.40f, y + 0.04f, 9, TextAnchor.MiddleRight, "0.82 0.74 0.42 1");
            AddUiLabel(container, parent, score.PlayerKills.ToString(CultureInfo.InvariantCulture), 0.43f, y, 0.475f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.76 0.82 0.88 1");
            AddUiLabel(container, parent, score.PlayerDeaths.ToString(CultureInfo.InvariantCulture), 0.50f, y, 0.545f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.76 0.82 0.88 1");
            AddUiLabel(container, parent, score.DamageToPlayers.ToString("0", CultureInfo.InvariantCulture), 0.57f, y, 0.65f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.76 0.82 0.88 1");
            AddUiLabel(container, parent, score.DamageToEventEntities.ToString("0", CultureInfo.InvariantCulture), 0.67f, y, 0.75f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.76 0.82 0.88 1");
            AddUiLabel(container, parent, score.ExplosiveDamageToEventEntities.ToString("0", CultureInfo.InvariantCulture), 0.77f, y, 0.85f, y + 0.04f, 8, TextAnchor.MiddleRight, "0.76 0.82 0.88 1");
            AddUiLabel(container, parent, score.ToolCupboardsDestroyed.ToString(CultureInfo.InvariantCulture), 0.88f, y, 0.93f, y + 0.04f, 8, TextAnchor.MiddleRight, score.ToolCupboardsDestroyed > 0 ? "0.60 0.86 0.65 1" : "0.76 0.82 0.88 1");
        }

        private void DestroyEventsManagerUi(BasePlayer player)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, EventsManagerUi);
        }

        private void CloseEventsManagerUi(BasePlayer player)
        {
            if (player == null)
                return;

            uiOpenPlayers.Remove(player.userID);
            uiScoreModalInstances.Remove(player.userID);
            uiRenderGenerations.Remove(player.userID);
            DestroyEventsManagerUi(player);
        }

        private void RefreshOpenEventsManagerUis()
        {
            foreach (var userId in uiOpenPlayers.ToList())
            {
                var player = BasePlayer.FindByID(userId);
                if (player == null || !player.IsConnected || !HasPlayerAccess(player, AdminPermission))
                {
                    uiOpenPlayers.Remove(userId);
                    uiScoreModalInstances.Remove(userId);
                    continue;
                }

                if (lootEditors.ContainsKey(userId))
                    continue;

                RefreshEventsManagerUi(player);
            }
        }

        private void RefreshEventsManagerUi(BasePlayer player)
        {
            if (player == null)
                return;

            OpenEventsManager(player);
        }

        private void DestroyEventsManagerUiForAll()
        {
            foreach (var player in BasePlayer.activePlayerList)
                DestroyEventsManagerUi(player);
            uiOpenPlayers.Clear();
            uiScoreModalInstances.Clear();
            uiRenderGenerations.Clear();
        }

        private void AddUiSection(CuiElementContainer container, string parent, string text, float x1, float y1, float x2, float y2)
        {
            var section = container.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.09 0.11 0.96" },
                RectTransform = { AnchorMin = UiAnchor(x1, y1), AnchorMax = UiAnchor(x2, y2) }
            }, parent);

            AddUiLabel(container, section, text, 0.012f, 0f, 0.98f, 1f, 11, TextAnchor.MiddleLeft, "0.86 0.90 0.94 1");
        }

        private void AddUiRowBackground(CuiElementContainer container, string parent, float y1, float y2)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = "0.045 0.055 0.065 0.72" },
                RectTransform = { AnchorMin = UiAnchor(0.04f, y1), AnchorMax = UiAnchor(0.96f, y2) }
            }, parent);
        }

        private void AddUiRowBackground(CuiElementContainer container, string parent, float x1, float y1, float x2, float y2)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = "0.045 0.055 0.065 0.72" },
                RectTransform = { AnchorMin = UiAnchor(x1, y1), AnchorMax = UiAnchor(x2, y2) }
            }, parent);
        }

        private void AddUiButton(CuiElementContainer container, string parent, string text, string command, float x1, float y1, float x2, float y2, string color, int fontSize = 10)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command, Color = color },
                RectTransform = { AnchorMin = UiAnchor(x1, y1), AnchorMax = UiAnchor(x2, y2) },
                Text =
                {
                    Text = text,
                    FontSize = fontSize,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.95 0.97 1 1"
                }
            }, parent);
        }

        private void AddUiLabel(CuiElementContainer container, string parent, string text, float x1, float y1, float x2, float y2, int fontSize, TextAnchor align, string color)
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
                RectTransform = { AnchorMin = UiAnchor(x1, y1), AnchorMax = UiAnchor(x2, y2) }
            }, parent);
        }

        private void AddUiItemImage(CuiElementContainer container, string parent, int itemId, ulong skinId, float x1, float y1, float x2, float y2)
        {
            container.Add(new CuiElement
            {
                Name = CuiHelper.GetGuid(),
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { ItemId = itemId, SkinId = skinId, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = UiAnchor(x1, y1), AnchorMax = UiAnchor(x2, y2) }
                }
            });
        }

        private void AddUiInput(CuiElementContainer container, string parent, string value, string command, float x1, float y1, float x2, float y2, int fontSize, string placeholder)
        {
            var inputPanel = container.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.09 0.11 0.98" },
                RectTransform = { AnchorMin = UiAnchor(x1, y1), AnchorMax = UiAnchor(x2, y2) }
            }, parent);

            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(placeholder))
                AddUiLabel(container, inputPanel, placeholder, 0.035f, 0f, 0.97f, 1f, fontSize, TextAnchor.MiddleLeft, "0.42 0.50 0.58 1");

            container.Add(new CuiElement
            {
                Name = CuiHelper.GetGuid(),
                Parent = inputPanel,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = string.IsNullOrEmpty(value) ? string.Empty : value,
                        Command = command ?? string.Empty,
                        FontSize = fontSize,
                        Align = TextAnchor.MiddleLeft,
                        Color = "0.92 0.95 0.98 1",
                        CharsLimit = 80,
                        NeedsKeyboard = true,
                        IsPassword = false,
                        LineType = UnityEngine.UI.InputField.LineType.SingleLine
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.035 0.08", AnchorMax = "0.965 0.92" }
                }
            });
        }

        private string UiAnchor(float x, float y)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###}", Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        private string UiHeaderStatus()
        {
            return $"autoBases={(config.EventTypes.AutomaticBases.Enabled ? "on" : "off")} {AutomaticBaseActiveCount()}+{data.PendingAutomaticSpawnRequests}/{config.EventTypes.AutomaticBases.MaximumActiveBases} | scoring={(config.Scoring.Enabled ? "on" : "off")} | rewards={(config.Rewards.Enabled ? "on" : "off")} | total={ActiveEventCount()}";
        }

        private string LayoutUiState(LayoutScanEntry layout)
        {
            if (layout.Ignored)
                return "Ignored";

            if (!layout.Valid)
                return "Invalid";

            return IsEnabledLayout(layout.LayoutId) ? "Enabled" : "Disabled";
        }

        private string LayoutUiStateColor(LayoutScanEntry layout)
        {
            if (layout.Ignored)
                return "0.62 0.68 0.74 1";

            if (!layout.Valid)
                return "0.92 0.48 0.42 1";

            return IsEnabledLayout(layout.LayoutId) ? "0.60 0.86 0.65 1" : "0.82 0.74 0.42 1";
        }

        private string ShortUiText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;

            return value.Substring(0, Math.Max(0, maxLength - 1)) + ".";
        }

        private int ScanLayouts(bool save)
        {
            string[] files;
            try
            {
                files = Interface.Oxide.DataFileSystem.GetFiles(CopyPasteDirectory) ?? Array.Empty<string>();
            }
            catch (DirectoryNotFoundException)
            {
                files = Array.Empty<string>();
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scanned = 0;

            foreach (var file in files)
            {
                var layoutId = ExtractLayoutId(file);
                if (string.IsNullOrWhiteSpace(layoutId))
                    continue;

                var entry = ScanLayout(layoutId);
                data.Layouts[layoutId] = entry;
                seen.Add(layoutId);
                scanned++;
            }

            foreach (var stale in data.Layouts.Keys.Where(key => !seen.Contains(key)).ToList())
            {
                data.Layouts[stale].Valid = false;
                data.Layouts[stale].ValidationErrors = new List<string> { "CopyPaste file is missing" };
                data.Layouts[stale].LastScannedUnix = NowUnix();
            }

            if (save)
                SaveData();

            return scanned;
        }

        private void EnsureAutomaticBaseLayouts()
        {
            var automaticBases = config?.EventTypes?.AutomaticBases;
            if (automaticBases == null || automaticBases.Layouts == null)
                return;

            if (automaticBases.Layouts.Count == 0)
            {
                var legacyEnabled = new HashSet<string>(config.LayoutRotation.EnabledLayouts ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var layout in data.Layouts.Values.Where(layout => layout != null && layout.Valid && !layout.Ignored).OrderBy(layout => layout.LayoutId))
                {
                    automaticBases.Layouts.Add(new WeightedLayoutConfig
                    {
                        LayoutId = layout.LayoutId,
                        Enabled = legacyEnabled.Count == 0 || legacyEnabled.Contains(layout.LayoutId),
                        Weight = 1f
                    });
                }
                Puts($"Automatic Bases layout migration populated {automaticBases.Layouts.Count} valid CopyPaste layout(s).");
            }

            EnsureAutomaticBaseLayoutEntries();
            config.LayoutRotation.EnabledLayouts = automaticBases.Layouts
                .Where(layout => layout.Enabled)
                .Select(layout => layout.LayoutId)
                .ToList();
            SaveConfig();
        }

        private void EnsureAutomaticBaseLayoutEntries()
        {
            var layouts = config.EventTypes.AutomaticBases.Layouts;
            var known = new HashSet<string>(layouts.Select(layout => layout.LayoutId), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var scanned in data.Layouts.Values.Where(layout => layout != null && !layout.Ignored).OrderBy(layout => layout.LayoutId))
            {
                if (known.Contains(scanned.LayoutId))
                    continue;
                layouts.Add(new WeightedLayoutConfig { LayoutId = scanned.LayoutId, Enabled = false, Weight = 1f });
                known.Add(scanned.LayoutId);
                added++;
            }
            if (added > 0)
            {
                SaveConfig();
                Puts($"Automatic Bases discovered {added} new layout definition(s); they were added disabled for admin review.");
            }
        }

        private LayoutScanEntry ScanLayout(string layoutId)
        {
            var entry = new LayoutScanEntry
            {
                LayoutId = layoutId,
                FileName = $"{layoutId}.json",
                Ignored = IsIgnoredLayout(layoutId),
                LastScannedUnix = NowUnix()
            };

            var dataPath = CopyPasteDirectory + layoutId;
            try
            {
                if (!Interface.Oxide.DataFileSystem.ExistsDatafile(dataPath))
                {
                    entry.ValidationErrors.Add("CopyPaste file is missing");
                    return entry;
                }

                var fileData = Interface.Oxide.DataFileSystem.GetDatafile(dataPath);
                if (fileData == null)
                {
                    entry.ValidationErrors.Add("CopyPaste file could not be read");
                    return entry;
                }

                if (fileData["default"] == null)
                    entry.ValidationErrors.Add("Missing default section");
                if (fileData["protocol"] == null)
                    entry.ValidationErrors.Add("Missing protocol section");

                var entities = fileData["entities"] as IEnumerable;
                if (entities == null)
                {
                    entry.ValidationErrors.Add("Missing entities section");
                    return entry;
                }

                var foundBounds = false;
                var min = Vector3.zero;
                var max = Vector3.zero;
                var foundationCells = new List<GroundFootprintCell>();
                var externalWallCells = new List<GroundFootprintCell>();
                var floorCells = new List<GroundFootprintCell>();

                foreach (var entityObject in entities)
                {
                    var entity = entityObject as Dictionary<string, object>;
                    if (entity == null)
                        continue;

                    entry.EntityCount++;
                    var prefab = GetEntityPrefab(entity);
                    if (IsToolCupboardPrefab(prefab))
                        entry.HasToolCupboard = true;
                    if (IsCrateLikePrefab(prefab))
                        entry.HasCrateLikeEntity = true;
                    if (IsAutoTurretPrefab(prefab))
                        entry.AutoTurretCount++;

                    Vector3 relativePosition;
                    if (!TryGetRelativePosition(entity, out relativePosition))
                        continue;
                    var relativeRotationDegrees = GetRelativeRotationDegrees(entity);

                    if (IsFoundationPrefab(prefab))
                        foundationCells.Add(CreateGroundFootprintCell(relativePosition, relativeRotationDegrees, prefab));
                    else if (IsExternalWallPrefab(prefab))
                        externalWallCells.Add(CreateGroundFootprintCell(relativePosition, relativeRotationDegrees, prefab));
                    else if (IsFloorPrefab(prefab))
                        floorCells.Add(CreateGroundFootprintCell(relativePosition, relativeRotationDegrees, prefab));

                    if (!foundBounds)
                    {
                        min = relativePosition;
                        max = relativePosition;
                        foundBounds = true;
                    }
                    else
                    {
                        min = Vector3.Min(min, relativePosition);
                        max = Vector3.Max(max, relativePosition);
                    }
                }

                if (entry.EntityCount <= 0)
                    entry.ValidationErrors.Add("No saved entities");
                if (!entry.HasToolCupboard)
                    entry.ValidationErrors.Add("No tool cupboard detected");

                var selectedGroundCells = new List<GroundFootprintCell>();
                if (foundationCells.Count > 0)
                {
                    selectedGroundCells.AddRange(foundationCells);
                    selectedGroundCells.AddRange(externalWallCells);
                    entry.GroundAnchorY = foundationCells.Min(cell => cell.Position.Y);
                }
                else if (externalWallCells.Count > 0)
                {
                    selectedGroundCells.AddRange(externalWallCells);
                    entry.GroundAnchorY = externalWallCells.Min(cell => cell.Position.Y);
                }
                else if (floorCells.Count > 0)
                {
                    var lowestFloorY = floorCells.Min(cell => cell.Position.Y);
                    selectedGroundCells.AddRange(floorCells.Where(cell => Math.Abs(cell.Position.Y - lowestFloorY) <= 0.6f));
                    entry.GroundAnchorY = lowestFloorY;
                }

                if (selectedGroundCells.Count == 0)
                {
                    entry.ValidationErrors.Add("No foundation, external wall, or ground-level floor footprint detected");
                }
                else
                {
                    entry.GroundFootprintCells = selectedGroundCells;
                    var centerX = selectedGroundCells.Average(cell => cell.Position.X);
                    var centerZ = selectedGroundCells.Average(cell => cell.Position.Z);
                    entry.GroundFootprintRadius = selectedGroundCells.Max(cell =>
                    {
                        var dx = cell.Position.X - centerX;
                        var dz = cell.Position.Z - centerZ;
                        return Mathf.Sqrt(dx * dx + dz * dz) + cell.Radius;
                    });
                }

                if (foundBounds)
                {
                    entry.BoundsMin = new StoredVector3(min);
                    entry.BoundsMax = new StoredVector3(max);
                }

                entry.Valid = entry.ValidationErrors.Count == 0;
                return entry;
            }
            catch (Exception exception)
            {
                entry.ValidationErrors.Add($"{exception.GetType().Name}: {exception.Message}");
                return entry;
            }
        }

        private List<LayoutContainerDescriptor> ReadLayoutContainers(string layoutId)
        {
            var result = new List<LayoutContainerDescriptor>();
            if (string.IsNullOrWhiteSpace(layoutId) || !Interface.Oxide.DataFileSystem.ExistsDatafile(CopyPasteDirectory + layoutId))
                return result;

            var fileData = Interface.Oxide.DataFileSystem.GetDatafile(CopyPasteDirectory + layoutId);
            var entities = fileData?["entities"] as IEnumerable;
            if (entities == null)
                return result;

            foreach (var raw in entities)
            {
                var entity = raw as Dictionary<string, object>;
                if (entity == null)
                    continue;

                var prefab = GetEntityPrefab(entity);
                if (!IsEditableLootContainerPrefab(prefab))
                    continue;

                Vector3 position;
                if (!TryGetRelativePosition(entity, out position))
                    continue;

                var descriptor = new LayoutContainerDescriptor
                {
                    Prefab = prefab,
                    LocalPosition = position,
                    Fingerprint = ContainerFingerprint(prefab, position),
                    Capacity = GuessContainerCapacity(prefab),
                    Label = ContainerDisplayName(prefab)
                };

                var items = entity.ContainsKey("items") ? entity["items"] as IEnumerable : null;
                if (items != null)
                {
                    foreach (var rawItem in items)
                    {
                        var itemData = rawItem as Dictionary<string, object>;
                        if (itemData == null)
                            continue;

                        int itemId;
                        int amount;
                        int slot;
                        ulong skin;
                        if (!TryDictionaryInt(itemData, "id", out itemId) || !TryDictionaryInt(itemData, "amount", out amount))
                            continue;
                        TryDictionaryInt(itemData, "position", out slot);
                        TryDictionaryUlong(itemData, "skinid", out skin);
                        var definition = ItemManager.FindItemDefinition(itemId);
                        if (definition == null)
                            continue;
                        descriptor.CopiedItems.Add(new LootItemEntry { ShortName = definition.shortname, Amount = Math.Max(1, amount), Skin = skin, Position = slot });
                        descriptor.Capacity = Math.Max(descriptor.Capacity, slot + 1);
                    }
                }

                result.Add(descriptor);
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var descriptor in result.OrderBy(entry => entry.Label).ThenBy(entry => entry.LocalPosition.x).ThenBy(entry => entry.LocalPosition.z))
            {
                int count;
                counts.TryGetValue(descriptor.Label, out count);
                count++;
                counts[descriptor.Label] = count;
                if (!descriptor.Label.StartsWith("Tool Cupboard", StringComparison.OrdinalIgnoreCase))
                    descriptor.Label += $" #{count}";
            }
            return result.OrderBy(entry => entry.Label).ToList();
        }

        private bool IsEditableLootContainerPrefab(string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
                return false;
            var value = prefab.ToLowerInvariant();
            return value.Contains("cupboard.tool") || value.Contains("box.wooden") || value.Contains("coffin")
                   || value.Contains("locker") || value.Contains("fridge") || value.Contains("stash")
                   || value.Contains("dropbox") || value.Contains("vendingmachine") || value.Contains("mailbox");
        }

        private string ContainerDisplayName(string prefab)
        {
            var value = (prefab ?? string.Empty).ToLowerInvariant();
            if (value.Contains("cupboard.tool")) return "Tool Cupboard / Upkeep";
            if (value.Contains("locker")) return "Locker";
            if (value.Contains("coffin")) return "Coffin";
            if (value.Contains("fridge")) return "Fridge";
            if (value.Contains("stash")) return "Stash";
            if (value.Contains("dropbox")) return "Drop Box";
            if (value.Contains("vendingmachine")) return "Vending Machine";
            if (value.Contains("box.wooden.large")) return "Large Wooden Box";
            return "Storage Box";
        }

        private int GuessContainerCapacity(string prefab)
        {
            var value = (prefab ?? string.Empty).ToLowerInvariant();
            if (value.Contains("box.wooden.large")) return 48;
            if (value.Contains("locker") || value.Contains("coffin")) return 42;
            if (value.Contains("vendingmachine")) return 30;
            if (value.Contains("cupboard.tool")) return 24;
            if (value.Contains("stash")) return 6;
            return 12;
        }

        private string ContainerFingerprint(string prefab, Vector3 position)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1:0.00},{2:0.00},{3:0.00}", (prefab ?? string.Empty).Trim().ToLowerInvariant(), position.x, position.y, position.z);
        }

        private bool TryDictionaryInt(Dictionary<string, object> values, string key, out int value)
        {
            value = 0;
            object raw;
            if (values == null || !values.TryGetValue(key, out raw) || raw == null)
                return false;
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryDictionaryUlong(Dictionary<string, object> values, string key, out ulong value)
        {
            value = 0;
            object raw;
            if (values == null || !values.TryGetValue(key, out raw) || raw == null)
                return false;
            return ulong.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private LootItemEntry CloneLootItem(LootItemEntry source)
        {
            return source == null ? null : new LootItemEntry { ShortName = source.ShortName, Amount = source.Amount, Skin = source.Skin, Position = source.Position };
        }

        private void OpenLootEditor(BasePlayer player, string layoutId)
        {
            LayoutScanEntry layout;
            if (player == null || !data.Layouts.TryGetValue(layoutId, out layout) || layout == null)
                return;
            var containers = ReadLayoutContainers(layoutId);
            var editor = new LootEditorState { LayoutId = layoutId };
            lootEditors[player.userID] = editor;
            if (containers.Count > 0)
                LoadLootEditorContainer(editor, containers[0]);
            OpenEventsManager(player);
        }

        private void SelectLootContainer(BasePlayer player, int index)
        {
            LootEditorState editor;
            if (player == null || !lootEditors.TryGetValue(player.userID, out editor))
                return;
            var containers = ReadLayoutContainers(editor.LayoutId);
            if (index < 0 || index >= containers.Count)
                return;
            LoadLootEditorContainer(editor, containers[index]);
        }

        private void LoadLootEditorContainer(LootEditorState editor, LayoutContainerDescriptor descriptor)
        {
            editor.ContainerFingerprint = descriptor.Fingerprint;
            editor.SelectedSlot = -1;
            editor.ItemPage = 0;
            editor.SlotPage = 0;
            editor.Search = string.Empty;
            Dictionary<string, ContainerLootOverride> layoutOverrides;
            ContainerLootOverride saved;
            var source = data.LayoutLootOverrides.TryGetValue(editor.LayoutId, out layoutOverrides)
                         && layoutOverrides.TryGetValue(descriptor.Fingerprint, out saved)
                         && saved != null
                ? saved.Items
                : descriptor.CopiedItems;
            editor.DraftItems = (source ?? new List<LootItemEntry>()).Select(CloneLootItem).Where(item => item != null).ToList();
            editor.DraftLoaded = true;
        }

        private void SelectLootSlot(BasePlayer player, int slot)
        {
            LootEditorState editor;
            LayoutContainerDescriptor descriptor;
            if (!TryGetLootEditor(player, out editor, out descriptor) || slot < 0 || slot >= descriptor.Capacity)
                return;
            editor.SelectedSlot = slot;
            editor.ItemPage = 0;
        }

        private bool TryGetLootEditor(BasePlayer player, out LootEditorState editor, out LayoutContainerDescriptor descriptor)
        {
            editor = null;
            descriptor = null;
            if (player == null || !lootEditors.TryGetValue(player.userID, out editor) || string.IsNullOrWhiteSpace(editor.ContainerFingerprint))
                return false;
            var fingerprint = editor.ContainerFingerprint;
            descriptor = ReadLayoutContainers(editor.LayoutId).FirstOrDefault(entry => entry.Fingerprint == fingerprint);
            return descriptor != null;
        }

        private LootItemEntry SelectedDraftItem(LootEditorState editor)
        {
            return editor?.DraftItems?.FirstOrDefault(item => item != null && item.Position == editor.SelectedSlot);
        }

        private void SetDraftLootItem(BasePlayer player, string shortName)
        {
            LootEditorState editor;
            LayoutContainerDescriptor descriptor;
            var definition = ItemManager.FindItemDefinition(shortName);
            if (definition == null || !TryGetLootEditor(player, out editor, out descriptor) || editor.SelectedSlot < 0)
                return;
            editor.DraftItems.RemoveAll(item => item != null && item.Position == editor.SelectedSlot);
            editor.DraftItems.Add(new LootItemEntry { ShortName = definition.shortname, Amount = 1, Position = editor.SelectedSlot });
        }

        private void AdjustDraftLootAmount(BasePlayer player, string rawValue)
        {
            LootEditorState editor;
            LayoutContainerDescriptor descriptor;
            int value;
            if (!TryGetLootEditor(player, out editor, out descriptor) || !int.TryParse(rawValue, out value))
                return;
            var item = SelectedDraftItem(editor);
            if (item == null)
                return;
            item.Amount = rawValue.StartsWith("+") || rawValue.StartsWith("-") ? Math.Max(1, item.Amount + value) : Math.Max(1, value);
        }

        private void SetDraftLootSkin(BasePlayer player, string rawValue)
        {
            LootEditorState editor;
            LayoutContainerDescriptor descriptor;
            ulong skin;
            if (!TryGetLootEditor(player, out editor, out descriptor) || !ulong.TryParse(rawValue, out skin))
                return;
            var item = SelectedDraftItem(editor);
            if (item != null) item.Skin = skin;
        }

        private void ClearDraftLootSlot(BasePlayer player)
        {
            LootEditorState editor;
            LayoutContainerDescriptor descriptor;
            if (TryGetLootEditor(player, out editor, out descriptor)) editor.DraftItems.RemoveAll(item => item != null && item.Position == editor.SelectedSlot);
        }

        private void SaveLootOverride(BasePlayer player)
        {
            LootEditorState editor;
            LayoutContainerDescriptor descriptor;
            if (!TryGetLootEditor(player, out editor, out descriptor))
                return;
            string error;
            if (!ValidateLootEntries(editor.DraftItems, descriptor.Capacity, out error))
            {
                SendReply(player, $"{config.ChatPrefix} Loot override was not saved: {error}");
                return;
            }
            Dictionary<string, ContainerLootOverride> layoutOverrides;
            if (!data.LayoutLootOverrides.TryGetValue(editor.LayoutId, out layoutOverrides))
                data.LayoutLootOverrides[editor.LayoutId] = layoutOverrides = new Dictionary<string, ContainerLootOverride>(StringComparer.OrdinalIgnoreCase);
            layoutOverrides[descriptor.Fingerprint] = new ContainerLootOverride
            {
                Prefab = descriptor.Prefab,
                LocalPosition = new StoredVector3(descriptor.LocalPosition),
                Items = editor.DraftItems.Select(CloneLootItem).OrderBy(item => item.Position).ToList(),
                UpdatedBy = player.UserIDString,
                UpdatedUnix = NowUnix()
            };
            SaveData();
            SendReply(player, $"{config.ChatPrefix} Saved {editor.DraftItems.Count} item slot(s) for {descriptor.Label} in {editor.LayoutId}.");
        }

        private bool ValidateLootEntries(List<LootItemEntry> entries, int capacity, out string error)
        {
            error = null;
            var occupied = new HashSet<int>();
            foreach (var item in entries ?? new List<LootItemEntry>())
            {
                if (item == null || ItemManager.FindItemDefinition(item.ShortName) == null) { error = "unknown item"; return false; }
                if (item.Amount <= 0) { error = $"{item.ShortName} has an invalid amount"; return false; }
                if (item.Position < 0 || item.Position >= capacity || !occupied.Add(item.Position)) { error = $"invalid or duplicate slot {item.Position}"; return false; }
            }
            return true;
        }

        private void ResetLootOverride(BasePlayer player)
        {
            LootEditorState editor;
            LayoutContainerDescriptor descriptor;
            if (!TryGetLootEditor(player, out editor, out descriptor))
                return;
            Dictionary<string, ContainerLootOverride> layoutOverrides;
            if (data.LayoutLootOverrides.TryGetValue(editor.LayoutId, out layoutOverrides))
            {
                layoutOverrides.Remove(descriptor.Fingerprint);
                if (layoutOverrides.Count == 0) data.LayoutLootOverrides.Remove(editor.LayoutId);
                SaveData();
            }
            LoadLootEditorContainer(editor, descriptor);
            SendReply(player, $"{config.ChatPrefix} {descriptor.Label} now uses copied contents.");
        }

        private void DeleteOrphanedOverride(BasePlayer player, string fingerprintToken)
        {
            LootEditorState editor;
            if (!lootEditors.TryGetValue(player.userID, out editor)) return;
            Dictionary<string, ContainerLootOverride> layoutOverrides;
            if (!data.LayoutLootOverrides.TryGetValue(editor.LayoutId, out layoutOverrides)) return;
            var key = layoutOverrides.Keys.FirstOrDefault(value => StableUiToken(value) == fingerprintToken);
            if (key == null) return;
            layoutOverrides.Remove(key);
            if (layoutOverrides.Count == 0) data.LayoutLootOverrides.Remove(editor.LayoutId);
            SaveData();
        }

        private string StableUiToken(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var character in value ?? string.Empty) hash = (hash ^ character) * 16777619;
                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        private Dictionary<string, object> BuildAdaptiveFoundationPasteOptions()
        {
            var adaptive = config?.Paste?.AdaptiveFoundations ?? new AdaptiveFoundationsConfig();
            return new Dictionary<string, object>
            {
                ["Enabled"] = adaptive.Enabled,
                ["Exposure Threshold Meters"] = adaptive.ExposureThresholdMeters,
                ["Ground Clearance Meters"] = config?.Paste?.GroundClearance ?? 0.25f,
                ["Maximum Foundation Embed Meters"] = adaptive.MaximumFoundationEmbedMeters,
                ["Maximum Foundation Clearance Meters"] = adaptive.MaximumFoundationClearanceMeters,
                ["Maximum Origin Adjustment Meters"] = adaptive.MaximumOriginAdjustmentMeters,
                ["Maximum Lowering Meters"] = adaptive.MaximumLoweringMeters,
                ["Raise Base Layer Above Water"] = adaptive.RaiseBaseLayerAboveWater,
                ["Water Surface Clearance Meters"] = adaptive.WaterSurfaceClearanceMeters,
                ["Maximum Water Depth Meters"] = adaptive.MaximumWaterDepthMeters,
                ["Stability Audit Delay Seconds"] = adaptive.StabilityAuditDelaySeconds
            };
        }

        private bool StartRaidBase(string requestedLayoutId, bool randomLocation, Vector3 requestedPosition, out string message, string triggerType = null, bool? announced = null, LayoutScanEntry preparedLayout = null, float? preparedRotationDegrees = null, bool locationPrevalidated = false, int preparedGridCandidateIndex = -1)
        {
            message = null;

            if (CopyPaste == null || !CopyPaste.IsLoaded)
            {
                message = "CopyPaste is not loaded.";
                return false;
            }

            var resolvedTriggerType = string.IsNullOrWhiteSpace(triggerType) ? "admin" : triggerType.Trim().ToLowerInvariant();
            if (string.Equals(resolvedTriggerType, "automatic", StringComparison.OrdinalIgnoreCase) && AutomaticBaseActiveCount() >= config.EventTypes.AutomaticBases.MaximumActiveBases)
            {
                message = $"Automatic Bases maximum active population reached ({config.EventTypes.AutomaticBases.MaximumActiveBases}).";
                return false;
            }

            LayoutScanEntry layout;
            if (preparedLayout != null)
            {
                layout = preparedLayout;
            }
            else if (string.Equals(resolvedTriggerType, "automatic", StringComparison.OrdinalIgnoreCase))
            {
                if (!TrySelectWeightedAutomaticLayout(out layout, out message))
                    return false;
            }
            else if (!TrySelectLayout(requestedLayoutId, out layout, out message))
                return false;

            Vector3 pasteOrigin;
            float rotationDegrees = preparedRotationDegrees ?? RandomRotationDegrees();
            var placementValidated = locationPrevalidated;
            if (placementValidated)
            {
                pasteOrigin = requestedPosition;
            }
            else if (randomLocation)
            {
                if (!TryFindRandomLocation(layout, rotationDegrees, out pasteOrigin, out preparedGridCandidateIndex, out message))
                    return false;
                placementValidated = true;
            }
            else
            {
                if (!TryBuildPasteOrigin(layout, requestedPosition, rotationDegrees, out pasteOrigin, out message))
                    return false;
            }

            if (!placementValidated && !ValidateLocation(layout, pasteOrigin, rotationDegrees, out message))
                return false;

            if (preparedGridCandidateIndex >= 0 && !ValidateDynamicRuntimeBlockers(CreatePlacementSearch(layout, pasteOrigin, rotationDegrees), out message))
            {
                TemporarilyBlockSpawnGridCandidate(preparedGridCandidateIndex, message);
                return false;
            }

            var instanceId = NewInstanceId();
            var now = NowUnix();
            var isAutomatic = string.Equals(resolvedTriggerType, "automatic", StringComparison.OrdinalIgnoreCase);
            var isAnnounced = announced ?? (isAutomatic ? ShouldAnnounceNewAutomaticBase() : true);
            var hardLifetimeSeconds = isAutomatic
                ? config.EventTypes.AutomaticBases.HardLifetimeHours * 3600f
                : config.Cleanup.ForcedCleanupTimeoutSeconds;
            string rewardProfileId;
            RewardProfile rewardProfileSnapshot;
            string rewardProfileHash;
            bool rewardPayoutEnabled;
            string rewardProfileError;
            ResolveRewardProfileForInstance(layout.LayoutId, resolvedTriggerType, out rewardProfileId,
                out rewardProfileSnapshot, out rewardProfileHash, out rewardPayoutEnabled, out rewardProfileError);
            var active = new ActiveRaidBase
            {
                InstanceId = instanceId,
                LayoutId = layout.LayoutId,
                PublicName = PublicDisplayName(),
                Position = new StoredVector3(pasteOrigin),
                RotationDegrees = rotationDegrees,
                StartedUnix = now,
                ExpiresUnix = now + hardLifetimeSeconds,
                EventTypeId = isAutomatic ? "automatic-bases" : "raid-base",
                ProviderType = "CopyPaste",
                IsAnnounced = isAnnounced,
                Status = "pasting",
                HadToolCupboardInLayout = layout.HasToolCupboard,
                ScoreRadiusMeters = config.Scoring.ScoreRadiusMeters,
                TriggerType = resolvedTriggerType,
                RewardProfileId = rewardProfileId,
                RewardProfileSnapshot = rewardProfileSnapshot,
                RewardProfileHash = rewardProfileHash,
                RewardPayoutEnabled = rewardPayoutEnabled,
                RewardProfileError = rewardProfileError,
                SpawnGridCandidateIndex = preparedGridCandidateIndex
            };

            data.ActiveRaidBases[instanceId] = active;
            pendingPasteInstances.Add(instanceId);
            SaveData();

            var pasteResult = CopyPaste.Call("API_RaidlandsTryTrackedPasteAtPosition", instanceId, layout.LayoutId,
                FormatVector(pasteOrigin), config.Paste.CopyPasteArguments, rotationDegrees,
                BuildAdaptiveFoundationPasteOptions());

            if (!IsPasteStartSuccess(pasteResult))
            {
                pendingPasteInstances.Remove(instanceId);
                CommitTerminalResult(active, "Failed", pasteResult == null ? "CopyPaste tracked paste API did not respond" : pasteResult.ToString(), false);
                data.ActiveRaidBases.Remove(instanceId);
                TemporarilyBlockSpawnGridCandidate(preparedGridCandidateIndex, "CopyPaste rejected paste start");
                SaveData();
                RefreshOpenEventsManagerUis();
                message = pasteResult == null ? "CopyPaste tracked paste API did not respond." : pasteResult.ToString();
                return false;
            }

            if (isAutomatic)
                data.LastRunUnix = now;
            SaveData();
            RefreshOpenEventsManagerUis();

            message = $"Started {active.PublicName} {instanceId} using layout {layout.LayoutId} at {FormatVector(pasteOrigin)}.";
            return true;
        }

        private void OnRaidlandsTrackedPasteFinished(string trackingId, string filename, List<ulong> pastedEntityIds,
            object player, Vector3 startPos, object adaptiveFoundationReport)
        {
            if (string.IsNullOrWhiteSpace(trackingId))
                return;

            ActiveRaidBase active;
            if (!data.ActiveRaidBases.TryGetValue(trackingId, out active))
                return;

            pendingPasteInstances.Remove(trackingId);
            active.Status = "active";
            active.EntityIds = pastedEntityIds?.Where(id => id != 0).Distinct().ToList() ?? new List<ulong>();
            active.ToolCupboardId = FindToolCupboardId(active.EntityIds);
            active.Position = new StoredVector3(startPos);
            var adaptiveSummary = ApplyAdaptiveFoundationReport(active, adaptiveFoundationReport);
            GetLayoutPlacementStats(active.LayoutId).Successes++;
            spawnGridLastSuccessUnix = NowUnix();
            spawnGridLastSuccessPosition = startPos;
            RebuildEntityIndex();
            var sanitized = SanitizeEventEntities(active.EntityIds);
            var overrideSummary = ApplyLayoutLootOverrides(active);
            var normalizedTurrets = NormalizePastedTurretsAttackAll(active.EntityIds);
            var managedSentries = ManageEventSentries(active.EntityIds);
            LayoutScanEntry scannedLayout;
            var expectedAutoTurrets = data.Layouts.TryGetValue(active.LayoutId, out scannedLayout) && scannedLayout != null ? scannedLayout.AutoTurretCount : 0;
            var survivingAutoTurrets = CountLivePastedAutoTurrets(active.EntityIds);
            ScheduleEventSanitizationReapply(active.EntityIds);
            SchedulePastedTurretAttackAllReapply(active.EntityIds);
            SchedulePastedTurretSurvivalAudit(active.InstanceId, active.EntityIds, expectedAutoTurrets);
            if (active.IsAnnounced)
                CreateOrUpdateMarker(active);
            SaveData();
            RefreshOpenEventsManagerUis();

            if (active.IsAnnounced)
            {
                var startMessage = $"{config.ChatPrefix} {active.PublicName} has appeared on the map. Bring boom and fight for it.";
                Server.Broadcast(startMessage);
            }
            Puts($"Raid base event {active.InstanceId} active: type={active.EventTypeId}, layout={active.LayoutId}, announced={active.IsAnnounced}, entities={active.EntityIds.Count}, tc={active.ToolCupboardId}, adaptiveFoundations={adaptiveSummary}, sanitized={sanitized.Entities}, cupboards={sanitized.Cupboards}, locks={sanitized.Locks}, sams={sanitized.Sams}, traps={sanitized.Traps}, removedSteamIds={sanitized.RemovedSteamIds}, lootOverrides={overrideSummary}, turretsAttackAll={normalizedTurrets}, managedSentries={managedSentries}, autoTurrets={survivingAutoTurrets}/{expectedAutoTurrets}.");
        }

        private int ManageActiveEventSentries()
        {
            if (RaidlandsSentryTurrets == null || !RaidlandsSentryTurrets.IsLoaded || data?.ActiveRaidBases == null)
                return 0;

            var entityIds = data.ActiveRaidBases.Values
                .Where(active => active != null && active.Status != "cleaning" && active.EntityIds != null)
                .SelectMany(active => active.EntityIds)
                .Where(id => id != 0)
                .Distinct()
                .ToList();

            return ManageEventSentries(entityIds);
        }

        private int ManageEventSentries(List<ulong> entityIds)
        {
            if (RaidlandsSentryTurrets == null || !RaidlandsSentryTurrets.IsLoaded || entityIds == null || entityIds.Count == 0)
                return 0;

            var result = RaidlandsSentryTurrets.Call("API_RaidlandsManageEventSentries", entityIds);
            int count;
            if (result != null && int.TryParse(result.ToString(), out count))
                return count;

            return 0;
        }

        private void OnRaidlandsTrackedPasteFailed(string trackingId, string filename, object result, Vector3 startPos,
            List<ulong> pastedEntityIds, object adaptiveFoundationReport)
        {
            if (string.IsNullOrWhiteSpace(trackingId))
                return;

            ActiveRaidBase active;
            data.ActiveRaidBases.TryGetValue(trackingId, out active);
            var partialEntityIds = pastedEntityIds?.Where(id => id != 0).Distinct().ToList() ?? new List<ulong>();
            var report = adaptiveFoundationReport as IDictionary<string, object>;
            var pasteStarted = report == null || !TryGetAdaptiveReportValue(report, "Paste Started", out var rawPasteStarted)
                || rawPasteStarted == null || !bool.TryParse(rawPasteStarted.ToString(), out var parsedPasteStarted)
                || parsedPasteStarted;
            if (partialEntityIds.Count > 0)
                DespawnEntities(partialEntityIds);
            if (pasteStarted && active != null && active.PurchaseCostsPaid != null && active.PurchaseCostsPaid.Count > 0)
                RefundFailedPurchase(active, $"tracked paste failed: {result}");

            if (pasteStarted && active != null)
                TemporarilyBlockSpawnGridCandidate(active.SpawnGridCandidateIndex, $"tracked paste failed: {result}");

            pendingPasteInstances.Remove(trackingId);
            if (active != null)
                CommitTerminalResult(active, "Failed", $"tracked paste failed: {result}", false);
            data.ActiveRaidBases.Remove(trackingId);
            SaveData();
            RefreshOpenEventsManagerUis();
            PrintWarning($"Tracked paste failed for {filename} ({trackingId}) at {FormatVector(startPos)}: {result}; partialEntitiesCleaned={partialEntityIds.Count}; adaptiveFoundations={FormatAdaptiveFoundationReport(adaptiveFoundationReport)}");
            if (pasteStarted && active != null && string.Equals(active.TriggerType, "automatic", StringComparison.OrdinalIgnoreCase))
                QueueAutomaticSpawnRequests(1, "tracked paste failed");
        }

        private string ApplyAdaptiveFoundationReport(ActiveRaidBase active, object rawReport)
        {
            if (active == null)
                return "none";
            var report = rawReport as IDictionary<string, object>;
            if (report == null || report.Count == 0)
                return "none";

            active.AdaptiveFoundationsAdjusted = ReadAdaptiveReportInt(report, "Adjusted Foundations");
            active.AdaptiveGeneratedFoundations = ReadAdaptiveReportInt(report, "Generated Foundations");
            active.AdaptiveGeneratedCapFloors = ReadAdaptiveReportInt(report, "Generated Cap Floors");
            active.AdaptiveGeneratedFullWalls = ReadAdaptiveReportInt(report, "Generated Full Walls");
            active.AdaptiveGeneratedHalfWalls = ReadAdaptiveReportInt(report, "Generated Half Walls");
            active.AdaptiveMaximumLoweringMeters = ReadAdaptiveReportFloat(report, "Maximum Lowering Meters");
            active.AdaptiveOriginVerticalAdjustmentMeters = ReadAdaptiveReportFloat(report,
                "Origin Vertical Adjustment Meters");
            active.AdaptiveNaturallySeatedFoundations = ReadAdaptiveReportInt(report,
                "Naturally Seated Foundations");
            active.AdaptiveWaterSupportedFoundations = ReadAdaptiveReportInt(report,
                "Water Supported Foundations");
            active.AdaptiveMaximumWaterDepthMeters = ReadAdaptiveReportFloat(report,
                "Maximum Water Depth Meters");
            return FormatAdaptiveFoundationReport(report);
        }

        private string FormatAdaptiveFoundationReport(object rawReport)
        {
            var report = rawReport as IDictionary<string, object>;
            return report == null ? "none" : FormatAdaptiveFoundationReport(report);
        }

        private string FormatAdaptiveFoundationReport(IDictionary<string, object> report)
        {
            if (report == null || report.Count == 0)
                return "none";
            return $"adjusted={ReadAdaptiveReportInt(report, "Adjusted Foundations")}, naturallySeated={ReadAdaptiveReportInt(report, "Naturally Seated Foundations")}, waterSupported={ReadAdaptiveReportInt(report, "Water Supported Foundations")}, maxWaterDepth={ReadAdaptiveReportFloat(report, "Maximum Water Depth Meters"):0.##}m, originAdjustment={ReadAdaptiveReportFloat(report, "Origin Vertical Adjustment Meters"):+0.##;-0.##;0}m, foundations={ReadAdaptiveReportInt(report, "Generated Foundations")}, floors={ReadAdaptiveReportInt(report, "Generated Cap Floors")}, fullWalls={ReadAdaptiveReportInt(report, "Generated Full Walls")}, halfWalls={ReadAdaptiveReportInt(report, "Generated Half Walls")}, maxLowering={ReadAdaptiveReportFloat(report, "Maximum Lowering Meters"):0.##}m";
        }

        private int ReadAdaptiveReportInt(IDictionary<string, object> report, string key)
        {
            object value;
            return TryGetAdaptiveReportValue(report, key, out value) && value != null
                ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                : 0;
        }

        private float ReadAdaptiveReportFloat(IDictionary<string, object> report, string key)
        {
            object value;
            return TryGetAdaptiveReportValue(report, key, out value) && value != null
                ? Convert.ToSingle(value, CultureInfo.InvariantCulture)
                : 0f;
        }

        private bool TryGetAdaptiveReportValue(IDictionary<string, object> report, string key, out object value)
        {
            value = null;
            if (report == null)
                return false;
            foreach (var entry in report)
            {
                if (!string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                    continue;
                value = entry.Value;
                return true;
            }
            return false;
        }

        private int NormalizePastedTurretsAttackAll(List<ulong> entityIds)
        {
            if (config?.Paste?.ForcePastedTurretsAttackAll != true || entityIds == null)
                return 0;

            var normalized = 0;
            foreach (var entityId in entityIds.Where(id => id != 0).Distinct())
            {
                var turret = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as AutoTurret;
                if (turret == null || turret.IsDestroyed)
                    continue;

                turret.authorizedPlayers.Clear();
                turret.target = null;
                DisableTurretPeacekeeperMode(turret);
                turret.SendNetworkUpdate();
                normalized++;
            }

            return normalized;
        }

        private int CountLivePastedAutoTurrets(IEnumerable<ulong> entityIds)
        {
            if (entityIds == null)
                return 0;

            var count = 0;
            foreach (var entityId in entityIds.Where(id => id != 0).Distinct())
            {
                var turret = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as AutoTurret;
                if (turret != null && !turret.IsDestroyed && IsAutoTurretPrefab(turret.PrefabName))
                    count++;
            }
            return count;
        }

        private void SchedulePastedTurretSurvivalAudit(string instanceId, List<ulong> entityIds, int expectedAutoTurrets)
        {
            if (expectedAutoTurrets <= 0 || entityIds == null || entityIds.Count == 0)
                return;

            var ids = entityIds.Where(id => id != 0).Distinct().ToList();
            var delays = config?.Paste?.PastedTurretSurvivalAuditDelaysSeconds ?? Array.Empty<float>();
            for (var index = 0; index < delays.Length; index++)
            {
                var delay = delays[index];
                var finalAudit = index == delays.Length - 1;
                timer.Once(delay, () => AuditPastedTurretSurvival(instanceId, ids, expectedAutoTurrets, delay, finalAudit));
            }
        }

        private void AuditPastedTurretSurvival(string instanceId, List<ulong> entityIds, int expectedAutoTurrets, float delay, bool finalAudit)
        {
            ActiveRaidBase active;
            if (string.IsNullOrWhiteSpace(instanceId)
                || !data.ActiveRaidBases.TryGetValue(instanceId, out active)
                || active == null
                || string.Equals(active.Status, "cleaning", StringComparison.OrdinalIgnoreCase))
                return;

            var surviving = CountLivePastedAutoTurrets(entityIds);
            if (surviving < expectedAutoTurrets)
            {
                PrintWarning($"Raid base event {instanceId} turret survival audit at +{delay:0.##}s: surviving={surviving}/{expectedAutoTurrets}, layout={active.LayoutId}. The paste completed with missing or destroyed auto turrets.");
                return;
            }

            if (finalAudit)
                Puts($"Raid base event {instanceId} turret survival audit passed: surviving={surviving}/{expectedAutoTurrets} at +{delay:0.##}s.");
        }

        private EventSanitizeResult SanitizeEventEntities(List<ulong> entityIds)
        {
            var result = new EventSanitizeResult();
            if (entityIds == null)
                return result;

            foreach (var entityId in entityIds.Where(id => id != 0).Distinct())
            {
                var entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as BaseEntity;
                if (entity == null || entity.IsDestroyed)
                    continue;

                result.Entities++;
                entity.OwnerID = 0;

                var cupboard = entity as BuildingPrivlidge;
                if (cupboard != null)
                {
                    result.Cupboards++;
                    result.RemovedSteamIds += cupboard.authorizedPlayers.Count;
                    cupboard.authorizedPlayers.Clear();
                    cupboard.SendNetworkUpdate();
                }

                var turret = entity as AutoTurret;
                if (turret != null)
                {
                    result.Turrets++;
                    result.RemovedSteamIds += turret.authorizedPlayers.Count;
                    turret.authorizedPlayers.Clear();
                    turret.target = null;
                    turret.SendNetworkUpdate();
                }

                var codeLock = entity.GetSlot(BaseEntity.Slot.Lock) as CodeLock ?? entity.GetComponent<CodeLock>();
                if (codeLock != null)
                {
                    result.Locks++;
                    result.RemovedSteamIds += codeLock.whitelistPlayers.Count + codeLock.guestPlayers.Count;
                    codeLock.OwnerID = 0;
                    codeLock.whitelistPlayers.Clear();
                    codeLock.guestPlayers.Clear();
                    codeLock.SendNetworkUpdate();
                }

                if (entity is SamSite)
                    result.Sams++;
                if (entity is GunTrap || entity is FlameTurret)
                    result.Traps++;

                entity.SendNetworkUpdate();
            }

            return result;
        }

        private void ScheduleEventSanitizationReapply(List<ulong> entityIds)
        {
            if (entityIds == null)
                return;

            var ids = entityIds.Where(id => id != 0).Distinct().ToList();
            foreach (var delay in config.Paste.PastedTurretAttackAllReapplyDelaysSeconds ?? new float[0])
            {
                if (delay >= 0f)
                    timer.Once(delay, () => SanitizeEventEntities(ids));
            }
        }

        private string ApplyLayoutLootOverrides(ActiveRaidBase active)
        {
            Dictionary<string, ContainerLootOverride> overrides;
            if (active == null || !data.LayoutLootOverrides.TryGetValue(active.LayoutId, out overrides) || overrides == null || overrides.Count == 0)
                return "0/0";

            var applied = 0;
            var failed = 0;
            var rotation = Quaternion.Euler(0f, active.RotationDegrees, 0f);
            foreach (var entityId in active.EntityIds)
            {
                var container = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as StorageContainer;
                if (container == null || container.IsDestroyed || container.inventory == null)
                    continue;

                var localPosition = Quaternion.Inverse(rotation) * (container.transform.position - active.Position.ToVector3());
                var fingerprint = ContainerFingerprint(container.PrefabName, localPosition);
                ContainerLootOverride lootOverride;
                if (!overrides.TryGetValue(fingerprint, out lootOverride) || lootOverride == null)
                    continue;

                string error;
                if (TryReplaceContainerInventory(container, lootOverride.Items, out error))
                    applied++;
                else
                {
                    failed++;
                    PrintWarning($"Loot override left copied contents intact: layout={active.LayoutId}, container={fingerprint}, error={error}");
                }
            }

            return $"{applied}/{overrides.Count}" + (failed > 0 ? $" failed={failed}" : string.Empty);
        }

        private bool TryReplaceContainerInventory(StorageContainer container, List<LootItemEntry> entries, out string error)
        {
            error = null;
            if (container?.inventory == null)
            {
                error = "container inventory is unavailable";
                return false;
            }

            var normalized = (entries ?? new List<LootItemEntry>()).Where(entry => entry != null).OrderBy(entry => entry.Position).ToList();
            var created = new List<Item>();
            var occupied = new HashSet<int>();
            foreach (var entry in normalized)
            {
                var definition = ItemManager.FindItemDefinition(entry.ShortName);
                if (definition == null || entry.Amount <= 0 || entry.Position < 0 || entry.Position >= container.inventory.capacity || !occupied.Add(entry.Position))
                {
                    error = $"invalid item/amount/slot ({entry.ShortName}, {entry.Amount}, {entry.Position})";
                    foreach (var pending in created) pending.Remove();
                    return false;
                }

                var item = ItemManager.Create(definition, entry.Amount, entry.Skin);
                if (item == null)
                {
                    error = $"could not create {entry.ShortName}";
                    foreach (var pending in created) pending.Remove();
                    return false;
                }

                item.position = entry.Position;
                created.Add(item);
            }

            var originalItems = container.inventory.itemList.ToList();
            var originalPositions = originalItems.ToDictionary(item => item, item => item.position);
            foreach (var original in originalItems)
                original.RemoveFromContainer();

            var placed = new List<Item>();
            foreach (var item in created)
            {
                if (!item.MoveToContainer(container.inventory, item.position, false))
                {
                    item.Remove();
                    error = $"could not place {item.info.shortname} in slot {item.position}";
                    break;
                }
                placed.Add(item);
            }

            if (error != null)
            {
                foreach (var item in placed) item.Remove();
                foreach (var original in originalItems)
                    original.MoveToContainer(container.inventory, originalPositions[original], false);
                container.inventory.MarkDirty();
                return false;
            }

            foreach (var original in originalItems) original.Remove();
            container.inventory.MarkDirty();
            container.SendNetworkUpdate();
            return true;
        }

        private void SchedulePastedTurretAttackAllReapply(List<ulong> entityIds)
        {
            if (config?.Paste?.ForcePastedTurretsAttackAll != true || entityIds == null)
                return;

            var ids = entityIds.Where(id => id != 0).Distinct().ToList();
            if (ids.Count == 0)
                return;

            foreach (var delay in config.Paste.PastedTurretAttackAllReapplyDelaysSeconds ?? new float[0])
            {
                if (delay < 0f)
                    continue;

                timer.Once(delay, () => NormalizePastedTurretsAttackAll(ids));
            }
        }

        private void DisableTurretPeacekeeperMode(AutoTurret turret)
        {
            InvokeBooleanMethod(turret, "SetPeacekeepermode", false);
            InvokeBooleanMethod(turret, "SetPeacekeeperMode", false);
            SetBooleanMember(turret, "peacekeepermode", false);
            SetBooleanMember(turret, "peacekeeperMode", false);
            SetBooleanMember(turret, "Peacekeepermode", false);
            SetBooleanMember(turret, "PeacekeeperMode", false);
        }

        private void InvokeBooleanMethod(object instance, string methodName, bool value)
        {
            var type = instance?.GetType();
            while (type != null)
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                    {
                        method.Invoke(instance, new object[] { value });
                        return;
                    }
                }

                type = type.BaseType;
            }
        }

        private void SetBooleanMember(object instance, string memberName, bool value)
        {
            var type = instance?.GetType();
            while (type != null)
            {
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(instance, value);
                    return;
                }

                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
                {
                    property.SetValue(instance, value, null);
                    return;
                }

                type = type.BaseType;
            }
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (config?.Scoring?.Enabled != true || entity == null || info == null)
                return;

            var attacker = ResolveScoringPlayer(info, entity);
            if (!IsScorablePlayer(attacker))
                return;

            var victim = entity as BasePlayer;
            if (victim != null)
            {
                TrackPlayerDamage(attacker, victim, info);
                return;
            }

            TrackEventEntityDamage(attacker, entity, info);
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (config?.Scoring?.Enabled != true || !IsScorablePlayer(player))
                return;

            var attacker = ResolveScoringPlayer(info, player);
            if (!IsScorablePlayer(attacker) || attacker.userID == player.userID)
                return;

            ActiveRaidBase active;
            if (!TryGetPlayerCombatInstance(attacker, player, out active))
                return;

            if (!IsPvpScoreAllowed(attacker, player))
                return;

            var killPoints = AdjustRepeatedVictimKillPoints(active, attacker, player, config.Scoring.PlayerKillPoints);

            AddRaidBaseScore(active, attacker, killPoints, score =>
            {
                score.PlayerKills++;
            });

            TouchRaidBaseScore(active, player, score =>
            {
                score.PlayerDeaths++;
            });
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            CheckObjectiveEntity(entity, "tool cupboard destroyed", info);
        }

        private void OnEntityKill(BaseNetworkable networkable)
        {
            if (networkable?.net != null)
                explosiveOwnerIds.Remove(networkable.net.ID.Value);

            CheckObjectiveEntity(networkable as BaseCombatEntity, "tool cupboard removed", null);
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            TrackExplosiveOwner(player, entity);
        }

        private void OnExplosiveDropped(BasePlayer player, BaseEntity entity, ThrownWeapon thrown)
        {
            TrackExplosiveOwner(player, entity);
        }

        private void OnRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            TrackExplosiveOwner(player, entity);
        }

        private void CheckObjectiveEntity(BaseCombatEntity entity, string reason, HitInfo info)
        {
            if (entity == null || entity.net == null)
                return;

            var entityId = entity.net.ID.Value;
            string instanceId;
            if (!entityToInstance.TryGetValue(entityId, out instanceId))
                return;

            ActiveRaidBase active;
            if (!data.ActiveRaidBases.TryGetValue(instanceId, out active))
                return;

            var isTc = active.ToolCupboardId == entityId || entity is BuildingPrivlidge || IsToolCupboardPrefab(entity.PrefabName) || IsToolCupboardPrefab(entity.ShortPrefabName);
            if (!isTc)
                return;

            AwardToolCupboardDestroyedScore(active, info);
            CompleteInstance(active, reason);
        }

        private void TrackPlayerDamage(BasePlayer attacker, BasePlayer victim, HitInfo info)
        {
            if (!IsScorablePlayer(attacker) || !IsScorablePlayer(victim) || attacker.userID == victim.userID)
                return;

            ActiveRaidBase active;
            if (!TryGetPlayerCombatInstance(attacker, victim, out active))
                return;

            if (!IsPvpScoreAllowed(attacker, victim))
                return;

            var damage = ScorableDamage(info, victim);
            var points = PointsFromDamage(damage, config.Scoring.PlayerDamagePointsPer100Damage);
            points = LimitVictimDamagePoints(active, attacker, victim, points);
            if (damage <= 0f && points <= 0)
                return;

            AddRaidBaseScore(active, attacker, points, score =>
            {
                score.DamageToPlayers += damage;
            });
        }

        private bool IsPvpScoreAllowed(BasePlayer attacker, BasePlayer victim)
        {
            if (!IsScorablePlayer(attacker) || !IsScorablePlayer(victim) || attacker.userID == victim.userID)
                return false;
            if (config.Scoring.IgnoreSleepingVictims && victim.IsSleeping())
                return false;
            if (config.Scoring.IgnoreSameRustTeamPvp && attacker.currentTeam != 0UL && attacker.currentTeam == victim.currentTeam)
                return false;

            var attackerClan = GetClanId(attacker.UserIDString);
            var victimClan = GetClanId(victim.UserIDString);
            if (config.Scoring.IgnoreSameClanPvp && !string.IsNullOrWhiteSpace(attackerClan)
                && attackerClan.Equals(victimClan, StringComparison.OrdinalIgnoreCase))
                return false;
            if (config.Scoring.IgnoreAlliedClanPvp && AreClanAllies(attacker.userID, victim.userID))
                return false;
            return true;
        }

        private int LimitVictimDamagePoints(ActiveRaidBase active, BasePlayer attacker, BasePlayer victim, int requestedPoints)
        {
            if (requestedPoints <= 0 || active == null || attacker == null || victim == null)
                return Math.Max(0, requestedPoints);
            var maximum = config.Scoring.MaximumPlayerDamagePointsPerVictimPerMinute;
            if (maximum <= 0)
                return requestedPoints;

            var state = GetPvpVictimState(active, attacker.UserIDString, victim.UserIDString);
            var now = NowUnix();
            if (state.DamageWindowStartedUnix <= 0 || now - state.DamageWindowStartedUnix >= 60d)
            {
                state.DamageWindowStartedUnix = now;
                state.DamagePointsAwarded = 0;
            }
            var allowed = Math.Max(0, maximum - state.DamagePointsAwarded);
            var awarded = Math.Min(requestedPoints, allowed);
            state.DamagePointsAwarded += awarded;
            return awarded;
        }

        private int AdjustRepeatedVictimKillPoints(ActiveRaidBase active, BasePlayer attacker, BasePlayer victim, int requestedPoints)
        {
            if (requestedPoints <= 0 || active == null || attacker == null || victim == null)
                return Math.Max(0, requestedPoints);
            var state = GetPvpVictimState(active, attacker.UserIDString, victim.UserIDString);
            var now = NowUnix();
            var repeat = state.LastKillUnix > 0 && config.Scoring.RepeatVictimWindowSeconds > 0f
                && now - state.LastKillUnix <= config.Scoring.RepeatVictimWindowSeconds;
            state.LastKillUnix = now;
            state.RepeatKills = repeat ? state.RepeatKills + 1 : 0;
            return repeat ? Mathf.Max(0, Mathf.RoundToInt(requestedPoints * config.Scoring.RepeatVictimKillMultiplier)) : requestedPoints;
        }

        private PvpVictimState GetPvpVictimState(ActiveRaidBase active, string attackerId, string victimId)
        {
            NormalizeActiveRaidBase(active);
            var key = $"{attackerId}:{victimId}";
            PvpVictimState state;
            if (!active.PvpVictimStates.TryGetValue(key, out state) || state == null)
                active.PvpVictimStates[key] = state = new PvpVictimState();
            return state;
        }

        private string GetClanId(string userId)
        {
            if (Clans == null || !Clans.IsLoaded || string.IsNullOrWhiteSpace(userId))
                return null;
            try
            {
                var result = Clans.Call("GetClanOf", userId);
                return string.IsNullOrWhiteSpace(result?.ToString()) ? null : result.ToString().Trim();
            }
            catch
            {
                return null;
            }
        }

        private bool AreClanAllies(ulong first, ulong second)
        {
            if (Clans == null || !Clans.IsLoaded || first == 0 || second == 0)
                return false;
            try
            {
                var result = Clans.Call("IsAllyPlayer", first, second);
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private string GetRustTeamId(ulong userId)
        {
            if (userId == 0 || RelationshipManager.ServerInstance == null)
                return null;
            try
            {
                var team = RelationshipManager.ServerInstance.FindPlayersTeam(userId);
                return team == null || team.teamID == 0UL ? null : team.teamID.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private void ObserveParticipantAffiliations(RaidBaseScoreEntry score, BasePlayer player)
        {
            if (score == null || player == null)
                return;
            var clan = GetClanId(player.UserIDString);
            score.CurrentClanId = clan;
            if (string.IsNullOrWhiteSpace(score.FirstClanId) && !string.IsNullOrWhiteSpace(clan))
                score.FirstClanId = clan;

            var team = GetRustTeamId(player.userID);
            score.CurrentTeamId = team;
            if (string.IsNullOrWhiteSpace(score.FirstTeamId) && !string.IsNullOrWhiteSpace(team))
                score.FirstTeamId = team;
        }

        private void RefreshScoreAffiliations(RaidBaseScoreEntry score)
        {
            if (score == null || string.IsNullOrWhiteSpace(score.UserId))
                return;
            score.CurrentClanId = GetClanId(score.UserId);
            if (string.IsNullOrWhiteSpace(score.FirstClanId) && !string.IsNullOrWhiteSpace(score.CurrentClanId))
                score.FirstClanId = score.CurrentClanId;
            ulong userId;
            if (ulong.TryParse(score.UserId, out userId))
            {
                score.CurrentTeamId = GetRustTeamId(userId);
                if (string.IsNullOrWhiteSpace(score.FirstTeamId) && !string.IsNullOrWhiteSpace(score.CurrentTeamId))
                    score.FirstTeamId = score.CurrentTeamId;
            }
        }

        private void RefreshAllActiveAffiliations()
        {
            if (data?.ActiveRaidBases == null)
                return;
            foreach (var score in data.ActiveRaidBases.Values.Where(value => value != null)
                         .SelectMany(value => value.Scores?.Values ?? Enumerable.Empty<RaidBaseScoreEntry>()))
                RefreshScoreAffiliations(score);
            SaveData();
        }

        private void TrackEventEntityDamage(BasePlayer attacker, BaseCombatEntity entity, HitInfo info)
        {
            if (!IsScorablePlayer(attacker) || entity == null || entity.net == null)
                return;

            string instanceId;
            if (!entityToInstance.TryGetValue(entity.net.ID.Value, out instanceId))
                return;

            ActiveRaidBase active;
            if (!data.ActiveRaidBases.TryGetValue(instanceId, out active) || !IsScoringActive(active))
                return;

            var damage = ScorableDamage(info, entity);
            if (damage <= 0f)
                return;

            var points = PointsFromDamage(damage, config.Scoring.EventEntityDamagePointsPer100Damage);
            var explosive = IsExplosionDamage(info);
            if (explosive)
                points += PointsFromDamage(damage, config.Scoring.ExplosiveEventEntityDamageBonusPointsPer100Damage);

            AddRaidBaseScore(active, attacker, points, score =>
            {
                score.DamageToEventEntities += damage;
                if (explosive)
                    score.ExplosiveDamageToEventEntities += damage;
            });
        }

        private void AwardToolCupboardDestroyedScore(ActiveRaidBase active, HitInfo info)
        {
            if (config?.Scoring?.Enabled != true || active == null || !IsScoringActive(active))
                return;

            var attacker = ResolveScoringPlayer(info, null);
            if (!IsScorablePlayer(attacker))
                return;

            AddRaidBaseScore(active, attacker, config.Scoring.ToolCupboardDestroyedPoints, score =>
            {
                score.ToolCupboardsDestroyed++;
            });
        }

        private bool TryGetPlayerCombatInstance(BasePlayer attacker, BasePlayer victim, out ActiveRaidBase active)
        {
            active = null;
            if (!IsScorablePlayer(attacker) || !IsScorablePlayer(victim))
                return false;

            foreach (var candidate in data.ActiveRaidBases.Values)
            {
                if (!IsScoringActive(candidate))
                    continue;

                var attackerInside = IsInsideRaidBase(candidate, attacker.transform.position);
                var victimInside = IsInsideRaidBase(candidate, victim.transform.position);
                if (config.Scoring.RequireAttackerAndVictimInsideRadius)
                {
                    if (!attackerInside || !victimInside)
                        continue;
                }
                else if (!attackerInside && !victimInside)
                {
                    continue;
                }

                active = candidate;
                return true;
            }

            return false;
        }

        private bool IsScoringActive(ActiveRaidBase active)
        {
            return active != null && active.Status == "active";
        }

        private bool IsInsideRaidBase(ActiveRaidBase active, Vector3 position)
        {
            if (active == null)
                return false;

            var center = EventCenter(active);
            center.y = position.y;
            var radius = active.ScoreRadiusMeters > 0f ? active.ScoreRadiusMeters : config.Scoring.ScoreRadiusMeters;
            return Vector3.Distance(center, position) <= radius;
        }

        private Vector3 EventCenter(ActiveRaidBase active)
        {
            if (active == null)
                return Vector3.zero;

            var origin = active.Position.ToVector3();
            if (config?.Scoring?.UseLayoutCenterForScoreRadius != true)
                return origin;

            LayoutScanEntry layout;
            if (!data.Layouts.TryGetValue(active.LayoutId, out layout) || layout == null)
                return origin;

            var min = layout.BoundsMin.ToVector3();
            var max = layout.BoundsMax.ToVector3();
            var localCenter = new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
            var rotation = Quaternion.Euler(0f, active.RotationDegrees, 0f);
            return origin + rotation * localCenter;
        }

        private bool IsScorablePlayer(BasePlayer player)
        {
            return player != null && !player.IsNpc && player.userID != 0;
        }

        private void TrackExplosiveOwner(BasePlayer player, BaseEntity entity)
        {
            if (!IsScorablePlayer(player) || entity?.net == null)
                return;

            var entityId = entity.net.ID.Value;
            explosiveOwnerIds[entityId] = player.userID;
            timer.Once(300f, () => explosiveOwnerIds.Remove(entityId));
        }

        private BasePlayer ResolveScoringPlayer(HitInfo info, BaseCombatEntity victim)
        {
            return ScorableOrNull(info?.InitiatorPlayer)
                   ?? ScorableOrNull(info?.Initiator as BasePlayer)
                   ?? OwnerPlayerFromEntity(info?.Weapon)
                   ?? OwnerPlayerFromEntity(info?.Initiator)
                   ?? OwnerPlayerFromEntity(info?.WeaponPrefab)
                   ?? ScorableOrNull(victim?.lastAttacker as BasePlayer);
        }

        private BasePlayer OwnerPlayerFromEntity(BaseEntity entity)
        {
            if (entity == null)
                return null;

            var player = ScorableOrNull(entity as BasePlayer);
            if (player != null)
                return player;

            try
            {
                player = ScorableOrNull(entity.ToPlayer());
                if (player != null)
                    return player;
            }
            catch
            {
            }

            try
            {
                player = ScorableOrNull(entity.GetParentEntity() as BasePlayer);
                if (player != null)
                    return player;
            }
            catch
            {
            }

            try
            {
                player = ScorableOrNull(entity.GetRootParentEntity() as BasePlayer);
                if (player != null)
                    return player;
            }
            catch
            {
            }

            try
            {
                player = ScorableOrNull(entity.creatorEntity as BasePlayer);
                if (player != null)
                    return player;
            }
            catch
            {
            }

            if (entity.net != null)
            {
                ulong ownerId;
                if (explosiveOwnerIds.TryGetValue(entity.net.ID.Value, out ownerId))
                {
                    player = ScorableOrNull(PlayerFromId(ownerId));
                    if (player != null)
                        return player;
                }
            }

            try
            {
                player = ScorableOrNull(PlayerFromId(entity.OwnerID));
                if (player != null)
                    return player;
            }
            catch
            {
            }

            return null;
        }

        private BasePlayer PlayerFromId(ulong userId)
        {
            return userId == 0 ? null : BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);
        }

        private BasePlayer ScorableOrNull(BasePlayer player)
        {
            return IsScorablePlayer(player) ? player : null;
        }

        private float ScorableDamage(HitInfo info, BaseCombatEntity victim)
        {
            var damage = HitDamageTotal(info);
            if (damage <= 0f || victim == null)
                return 0f;

            try
            {
                var health = Math.Max(0f, victim.Health());
                if (health > 0f)
                    damage = Math.Min(damage, health);
            }
            catch
            {
            }

            return Math.Max(0f, damage);
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

        private int PointsFromDamage(float damage, float pointsPer100Damage)
        {
            if (damage <= 0f || pointsPer100Damage <= 0f)
                return 0;

            return Mathf.Max(0, Mathf.RoundToInt(damage / 100f * pointsPer100Damage));
        }

        private void AddRaidBaseScore(ActiveRaidBase active, BasePlayer player, int points, Action<RaidBaseScoreEntry> update)
        {
            if (active == null || player == null)
                return;

            TouchRaidBaseScore(active, player, score =>
            {
                update?.Invoke(score);
                if (points > 0)
                    score.TotalScore += points;
            });
        }

        private void TouchRaidBaseScore(ActiveRaidBase active, BasePlayer player, Action<RaidBaseScoreEntry> update)
        {
            if (active == null || player == null)
                return;

            NormalizeActiveRaidBase(active);
            var userId = player.UserIDString;
            RaidBaseScoreEntry score;
            if (!active.Scores.TryGetValue(userId, out score) || score == null)
            {
                score = new RaidBaseScoreEntry
                {
                    UserId = userId
                };
                active.Scores[userId] = score;
            }

            score.DisplayName = player.displayName ?? userId;
            var now = NowUnix();
            if (score.FirstScoreUnix <= 0)
                score.FirstScoreUnix = now;
            score.LastScoreUnix = now;
            ObserveParticipantAffiliations(score, player);
            update?.Invoke(score);

            if (now - lastScoreSaveUnix >= 5)
            {
                lastScoreSaveUnix = now;
                SaveData();
            }

            if (now - lastLeaderboardChangedHookUnix >= 5)
            {
                lastLeaderboardChangedHookUnix = now;
                Interface.CallHook("OnRaidlandsRaidBaseLeaderboardChanged", active.InstanceId);
            }
        }

        private List<RaidBaseScoreEntry> BuildLeaderboard(ActiveRaidBase active, bool qualifiedOnly)
        {
            NormalizeActiveRaidBase(active);
            var scores = active?.Scores?.Values ?? Enumerable.Empty<RaidBaseScoreEntry>();
            if (qualifiedOnly)
                scores = scores.Where(score => score != null && score.TotalScore >= config.Scoring.MinimumScoreToQualify);

            return scores
                .Where(score => score != null)
                .OrderByDescending(score => score.TotalScore)
                .ThenBy(score => score.UserId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string BuildScoreboardMessage(string target, bool includeRewards)
        {
            if (string.IsNullOrWhiteSpace(target) || target.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                if (data.ActiveRaidBases.Count == 0)
                    return "No active raid base events.";

                return string.Join("\n\n", data.ActiveRaidBases.Values
                    .OrderBy(active => active.StartedUnix)
                    .Select(active => BuildScoreboardForInstance(active, includeRewards)));
            }

            ActiveRaidBase activeRaidBase;
            if (!data.ActiveRaidBases.TryGetValue(target, out activeRaidBase))
                return $"No active raid base instance '{target}' was found.";

            return BuildScoreboardForInstance(activeRaidBase, includeRewards);
        }

        private string BuildScoreboardForInstance(ActiveRaidBase active, bool includeRewards)
        {
            NormalizeActiveRaidBase(active);
            if (active == null)
                return "Raid base instance was not found.";

            var leaderboard = BuildLeaderboard(active, false);
            var lines = new List<string>
            {
                $"{active.PublicName} {active.InstanceId}: {active.Status}, scores={leaderboard.Count}, radius={active.ScoreRadiusMeters:0}m"
            };

            if (leaderboard.Count == 0)
            {
                lines.Add("No scoring entries yet.");
            }
            else
            {
                var max = Math.Min(config.Scoring.MaxLeaderboardEntries, leaderboard.Count);
                for (var index = 0; index < max; index++)
                {
                    var score = leaderboard[index];
                    lines.Add($"{index + 1}. {score.DisplayName ?? score.UserId}: {score.TotalScore} pts, kills={score.PlayerKills}, deaths={score.PlayerDeaths}, pDmg={score.DamageToPlayers:0}, eDmg={score.DamageToEventEntities:0}, boom={score.ExplosiveDamageToEventEntities:0}, tc={score.ToolCupboardsDestroyed}");
                }
            }

            if (includeRewards)
            {
                var transactions = rewardLedger.Transactions.Values.Where(value => value != null && value.InstanceId == active.InstanceId).ToList();
                lines.Add($"Rewards: profile={active.RewardProfileId ?? "none"}, enabled={active.RewardPayoutEnabled}, processed={active.RewardsProcessed}, paid={transactions.Count(value => value.Status == "paid")}, pending={transactions.Count(value => value.Status != "paid")}, globalPending={PendingRewardTransactionCount()}{(string.IsNullOrWhiteSpace(active.RewardProfileError) ? "" : ", error=" + active.RewardProfileError)}");
            }

            return string.Join("\n", lines);
        }

        private string BuildPendingRewardsMessage()
        {
            var pending = rewardLedger.Transactions.Values
                .Where(value => value != null && !string.Equals(value.Status, "paid", StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value.CreatedUnix)
                .ToList();
            if (pending.Count == 0)
                return "No pending RaidlandsEvents rewards.";

            var lines = new List<string> { $"RaidlandsEvents reward transactions requiring attention: {pending.Count}" };
            foreach (var reward in pending.Take(10))
            {
                lines.Add($"{reward.TransactionId}: {reward.DisplayName ?? reward.UserId}, place={reward.Place}, {RewardTransactionSummary(reward)}, status={reward.Status}, attempts={reward.AttemptCount}, error={reward.LastError ?? "none"}");
            }

            if (pending.Count > 10)
                lines.Add($"...and {pending.Count - 10} more.");

            return string.Join("\n", lines);
        }

        private string BuildRewardProfilesMessage()
        {
            var lines = new List<string>
            {
                $"RaidlandsEvents reward profiles: {rewardProfiles.Count}; global={(config.Rewards.Enabled ? "enabled" : "disabled")}, automatic={(config.Rewards.AutomaticEventPayoutsEnabled ? "enabled" : "disabled")}, admin/manual={(config.Rewards.AdminEventPayoutsEnabled ? "enabled" : "disabled")}",
                $"Defaults: automatic={config.Rewards.AutomaticDefaultProfileId}, admin={config.Rewards.AdminDefaultProfileId}; layout overrides={config.Rewards.LayoutProfileOverrides.Count}"
            };
            foreach (var profile in rewardProfiles.Values.OrderBy(value => value.Id))
            {
                var validation = ValidateRewardProfile(profile, true);
                lines.Add($"{profile.Id}: {profile.DisplayName}, {(profile.Enabled ? "enabled" : "disabled")}, {profile.RewardMode}/{profile.ScoreScope}/{profile.GroupDistribution}, placements={profile.Placements.Count}, pool={profile.Pool.Count}, validation={(validation == null ? "valid" : validation)}");
            }
            return string.Join("\n", lines);
        }

        private string BuildRewardValidationMessage(string profileId)
        {
            IEnumerable<RewardProfile> profiles = rewardProfiles.Values;
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                RewardProfile profile;
                if (!rewardProfiles.TryGetValue(profileId, out profile))
                    return $"Reward profile '{profileId}' was not found.";
                profiles = new[] { profile };
            }
            var lines = new List<string>();
            foreach (var profile in profiles.OrderBy(value => value.Id))
            {
                var error = ValidateRewardProfile(profile, true);
                lines.Add($"{profile.Id}: {(error == null ? "VALID" : "INVALID - " + error)}");
            }
            if (string.IsNullOrWhiteSpace(profileId))
            {
                var assignmentErrors = ValidateRewardAssignments();
                lines.Add(assignmentErrors.Count == 0 ? "Assignments: VALID" : "Assignments: INVALID - " + string.Join("; ", assignmentErrors));
            }
            return lines.Count == 0 ? "No reward profiles were found." : string.Join("\n", lines);
        }

        private List<string> ValidateRewardAssignments()
        {
            var errors = new List<string>();
            if (!rewardProfiles.ContainsKey(config.Rewards.AutomaticDefaultProfileId)) errors.Add($"automatic default profile '{config.Rewards.AutomaticDefaultProfileId}' is missing");
            if (!rewardProfiles.ContainsKey(config.Rewards.AdminDefaultProfileId)) errors.Add($"admin default profile '{config.Rewards.AdminDefaultProfileId}' is missing");
            foreach (var assignment in config.Rewards.LayoutProfileOverrides)
            {
                if (!data.Layouts.ContainsKey(assignment.Key)) errors.Add($"layout override '{assignment.Key}' references an undiscovered layout");
                if (!rewardProfiles.ContainsKey(assignment.Value)) errors.Add($"layout '{assignment.Key}' references missing profile '{assignment.Value}'");
            }
            var duplicatePoints = config.Leaderboard.SeasonPlacementPoints.GroupBy(value => value.Place).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
            if (duplicatePoints.Count > 0) errors.Add("duplicate leaderboard placement points: " + string.Join(", ", duplicatePoints));
            return errors;
        }

        private string BuildRewardReviewMessage()
        {
            var review = rewardLedger.Transactions.Values
                .Where(value => value != null && (value.Status == "review-required" || value.Status == "processing" || value.Status == "failed"))
                .OrderBy(value => value.CreatedUnix).ToList();
            if (review.Count == 0)
                return "No RaidlandsEvents reward transactions require admin review.";
            var lines = new List<string> { $"Reward transactions requiring explicit review: {review.Count}. Retry one with revents.rewards retry <transactionId>." };
            lines.AddRange(review.Take(20).Select(value => $"{value.TransactionId}: status={value.Status}, player={value.DisplayName ?? value.UserId}, {RewardTransactionSummary(value)}, attempts={value.AttemptCount}, error={value.LastError ?? "none"}"));
            if (review.Count > 20) lines.Add($"...and {review.Count - 20} more.");
            return string.Join("\n", lines);
        }

        private int CountRewardTransactions(string status)
        {
            return rewardLedger?.Transactions?.Values.Count(value => value != null && string.Equals(value.Status, status, StringComparison.OrdinalIgnoreCase)) ?? 0;
        }

        private string BuildRewardPreview(string profileId, string instanceId)
        {
            RewardProfile profile;
            if (string.IsNullOrWhiteSpace(profileId) || !rewardProfiles.TryGetValue(profileId, out profile))
                return $"Reward profile '{profileId ?? ""}' was not found.";
            var structuralError = ValidateRewardProfile(profile, false);
            if (!string.IsNullOrWhiteSpace(structuralError))
                return $"Cannot preview invalid reward profile '{profile.Id}': {structuralError}";

            ActiveRaidBase active = null;
            if (!string.IsNullOrWhiteSpace(instanceId) && !data.ActiveRaidBases.TryGetValue(instanceId, out active))
                return $"Active raid-base instance '{instanceId}' was not found.";
            if (active == null && !string.IsNullOrWhiteSpace(instanceId))
                return $"Active raid-base instance '{instanceId}' was not found.";

            var standings = active == null
                ? BuildSyntheticRewardStandings(profile.ScoreScope).Where(value => value.Score >= profile.MinimumGroupScore).ToList()
                : BuildPayoutStandings(active, profile);
            AssignStandingRanks(standings);
            var source = active == null ? "synthetic standings" : $"active instance {active.InstanceId}";
            var lines = new List<string>
            {
                $"DRY RUN ONLY - profile={profile.Id}, source={source}, mode={profile.RewardMode}, scope={profile.ScoreScope}, split={profile.GroupDistribution}",
                $"Qualified standings: {(standings.Count == 0 ? "none" : string.Join(", ", standings.Select(value => $"#{value.Rank} {value.DisplayName}={value.Score}")))}"
            };
            if (profile.RewardMode == "FixedPlacements")
            {
                foreach (var placement in profile.Placements.OrderBy(value => value.Place))
                {
                    var standing = standings.FirstOrDefault(value => value.Rank == placement.Place);
                    if (standing == null)
                    {
                        lines.Add($"#{placement.Place}: unawarded (missing/unqualified placement)");
                        continue;
                    }
                    foreach (var reward in placement.Rewards)
                        AddRewardPreviewLines(lines, profile, standing, placement.Place, reward, reward.Amount);
                }
            }
            else
            {
                foreach (var reward in profile.Pool)
                {
                    var placementAmounts = AllocatePlacementAmounts(reward.Amount, profile.Placements);
                    foreach (var placement in profile.Placements.OrderBy(value => value.Place))
                    {
                        var standing = standings.FirstOrDefault(value => value.Rank == placement.Place);
                        int total;
                        if (standing == null || !placementAmounts.TryGetValue(placement.Place, out total) || total <= 0)
                        {
                            lines.Add($"#{placement.Place}: unawarded {RewardDefinitionSummary(reward, placementAmounts.TryGetValue(placement.Place, out total) ? total : 0)}");
                            continue;
                        }
                        AddRewardPreviewLines(lines, profile, standing, placement.Place, reward, total);
                    }
                }
            }
            lines.Add("Preview performed no payout and created no reward transaction.");
            return string.Join("\n", lines);
        }

        private List<EventStanding> BuildSyntheticRewardStandings(string scope)
        {
            scope = NormalizeScoreScope(scope);
            if (scope == "Player")
            {
                return new[] { 5000, 3000, 1500, 600 }.Select((score, index) => new EventStanding
                {
                    Rank = index + 1, Scope = "Player", Id = $"synthetic-player-{index + 1}", DisplayName = $"Synthetic Player {index + 1}", Score = score,
                    Members = new List<EventStandingMember> { new EventStandingMember { UserId = $"synthetic-player-{index + 1}", DisplayName = $"Synthetic Player {index + 1}", Score = score } }
                }).ToList();
            }
            return new List<EventStanding>
            {
                new EventStanding { Rank = 1, Scope = scope, Id = "synthetic-group-a", DisplayName = "Synthetic Group A", Score = 5000, Members = new List<EventStandingMember> { new EventStandingMember { UserId = "synthetic-player-1", DisplayName = "Synthetic Player 1", Score = 3200 }, new EventStandingMember { UserId = "synthetic-player-2", DisplayName = "Synthetic Player 2", Score = 1800 } } },
                new EventStanding { Rank = 2, Scope = scope, Id = "synthetic-group-b", DisplayName = "Synthetic Group B", Score = 3000, Members = new List<EventStandingMember> { new EventStandingMember { UserId = "synthetic-player-3", DisplayName = "Synthetic Player 3", Score = 3000 } } },
                new EventStanding { Rank = 3, Scope = scope, Id = "synthetic-solo-4", DisplayName = "Synthetic Solo 4", Score = 1500, Members = new List<EventStandingMember> { new EventStandingMember { UserId = "synthetic-player-4", DisplayName = "Synthetic Player 4", Score = 1500 } } }
            };
        }

        private void AddRewardPreviewLines(List<string> lines, RewardProfile profile, EventStanding standing, int place, RewardDefinition reward, int total)
        {
            var members = standing.Scope == "Player" ? standing.Members.Take(1).ToList() : standing.Members.Where(value => value.Score >= profile.MinimumMemberScore).ToList();
            if (members.Count == 0)
            {
                lines.Add($"#{place} {standing.DisplayName}: unawarded {RewardDefinitionSummary(reward, total)} (no qualified members)");
                return;
            }
            var allocations = AllocateMemberAmounts(total, members, profile.GroupDistribution);
            lines.Add($"#{place} {standing.DisplayName}: {RewardDefinitionSummary(reward, total)} => {string.Join(", ", allocations.Where(value => value.Amount > 0).Select(value => $"{value.DisplayName}={value.Amount}"))}");
        }

        private string RewardDefinitionSummary(RewardDefinition reward, int amount)
        {
            if (reward == null) return "unknown reward";
            if (reward.Type == "Item") return $"{amount:n0} {reward.ShortName} (skin {reward.SkinId})";
            if (reward.Type == "Command") return $"command amount={amount:n0}";
            return $"{amount:n0} RP";
        }

        private int PendingRewardTransactionCount()
        {
            return rewardLedger?.Transactions?.Values.Count(value => value != null && value.Status != "paid") ?? 0;
        }

        private string RewardTransactionSummary(RewardTransaction transaction)
        {
            if (transaction == null)
                return "unknown reward";
            if (transaction.Type == "Item")
                return $"{transaction.Amount:n0} {transaction.ShortName} (skin {transaction.SkinId})";
            if (transaction.Type == "Command")
                return $"command amount={transaction.Amount:n0}";
            return $"{transaction.Amount:n0} RP";
        }

        private List<EventStanding> BuildEventStandings(ActiveRaidBase active, string scope, bool includeSolo, bool finalMembership)
        {
            NormalizeActiveRaidBase(active);
            scope = NormalizeScoreScope(scope);
            if (active == null)
                return new List<EventStanding>();

            if (scope == "Player")
            {
                var playerStandings = BuildLeaderboard(active, false).Select(score => new EventStanding
                {
                    Scope = "Player",
                    Id = score.UserId,
                    DisplayName = score.DisplayName ?? score.UserId,
                    Score = score.TotalScore,
                    PlayerKills = score.PlayerKills,
                    PlayerDeaths = score.PlayerDeaths,
                    DamageToPlayers = score.DamageToPlayers,
                    DamageToEventEntities = score.DamageToEventEntities,
                    ExplosiveDamageToEventEntities = score.ExplosiveDamageToEventEntities,
                    ToolCupboardsDestroyed = score.ToolCupboardsDestroyed,
                    Members = new List<EventStandingMember>
                    {
                        new EventStandingMember { UserId = score.UserId, DisplayName = score.DisplayName ?? score.UserId, Score = score.TotalScore }
                    }
                }).ToList();
                AssignStandingRanks(playerStandings);
                return playerStandings;
            }

            var grouped = new Dictionary<string, EventStanding>(StringComparer.OrdinalIgnoreCase);
            foreach (var score in active.Scores.Values.Where(value => value != null))
            {
                if (finalMembership)
                    RefreshScoreAffiliations(score);
                string firstId;
                string currentId;
                string groupScope;
                if (scope == "Clan")
                {
                    firstId = score.FirstClanId;
                    currentId = score.CurrentClanId;
                    groupScope = "Clan";
                }
                else
                {
                    firstId = score.FirstTeamId;
                    currentId = score.CurrentTeamId;
                    groupScope = "RustTeam";
                }

                var ownsGroup = !string.IsNullOrWhiteSpace(firstId) && string.Equals(firstId, currentId, StringComparison.OrdinalIgnoreCase);
                if (!ownsGroup && !includeSolo)
                    continue;

                var id = ownsGroup ? firstId : score.UserId;
                var standingScope = ownsGroup ? groupScope : "Player";
                var key = standingScope + ":" + id;
                EventStanding standing;
                if (!grouped.TryGetValue(key, out standing))
                {
                    standing = new EventStanding
                    {
                        Scope = standingScope,
                        Id = id,
                        DisplayName = ownsGroup ? (groupScope == "Clan" ? id : "Team " + id) : score.DisplayName ?? score.UserId
                    };
                    grouped[key] = standing;
                }

                standing.Score += score.TotalScore;
                standing.PlayerKills += score.PlayerKills;
                standing.PlayerDeaths += score.PlayerDeaths;
                standing.DamageToPlayers += score.DamageToPlayers;
                standing.DamageToEventEntities += score.DamageToEventEntities;
                standing.ExplosiveDamageToEventEntities += score.ExplosiveDamageToEventEntities;
                standing.ToolCupboardsDestroyed += score.ToolCupboardsDestroyed;
                standing.Members.Add(new EventStandingMember
                {
                    UserId = score.UserId,
                    DisplayName = score.DisplayName ?? score.UserId,
                    Score = score.TotalScore
                });
            }

            var standings = grouped.Values
                .OrderByDescending(value => value.Score)
                .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            AssignStandingRanks(standings);
            return standings;
        }

        private void AssignStandingRanks(List<EventStanding> standings)
        {
            if (standings == null)
                return;
            for (var index = 0; index < standings.Count; index++)
                standings[index].Rank = index + 1;
        }

        private List<EventStanding> BuildPayoutStandings(ActiveRaidBase active, RewardProfile profile)
        {
            if (active == null || profile == null)
                return new List<EventStanding>();
            var standings = BuildEventStandings(active, profile.ScoreScope, profile.AllowSoloIfNoGroup, true)
                .Where(value => value.Score >= profile.MinimumGroupScore)
                .ToList();
            AssignStandingRanks(standings);
            return standings;
        }

        private RaidBaseEventResult CommitTerminalResult(ActiveRaidBase active, string state, string reason, bool qualifiesForResults)
        {
            if (active == null)
                return null;
            var existing = historyData.Results.FirstOrDefault(value => value != null && value.InstanceId == active.InstanceId);
            if (existing != null)
            {
                active.ResultCommitted = true;
                return existing;
            }

            EnsureActiveRewardSnapshot(active);
            foreach (var score in active.Scores.Values.Where(value => value != null))
                RefreshScoreAffiliations(score);

            var result = new RaidBaseEventResult
            {
                InstanceId = active.InstanceId,
                EventTypeId = active.EventTypeId,
                LayoutId = active.LayoutId,
                DisplayName = active.PublicName,
                TriggerType = active.TriggerType,
                State = state,
                StartedUnix = active.StartedUnix,
                EndedUnix = NowUnix(),
                EndReason = reason,
                Position = new StoredVector3(EventCenter(active)),
                RewardProfileId = active.RewardProfileId,
                RewardProfileHash = active.RewardProfileHash,
                RewardProfileSnapshot = CloneJson(active.RewardProfileSnapshot),
                PlayerStandings = BuildEventStandings(active, "Player", false, true),
                ClanStandings = BuildEventStandings(active, "Clan", false, true),
                TeamStandings = BuildEventStandings(active, "RustTeam", false, true)
            };

            historyData.Results.Add(result);
            active.ResultCommitted = true;
            active.RewardsProcessed = true;
            if (qualifiesForResults)
            {
                ApplyCompletedEventToLeaderboards(result, active.RewardProfileSnapshot);
                PlanRewardTransactions(active, result);
            }
            TrimCurrentWipeHistory();
            SaveAllEventData();

            if (string.Equals(state, "Completed", StringComparison.OrdinalIgnoreCase))
                Interface.CallHook("OnRaidlandsRaidBaseCompleted", EventResultToDictionary(result, true));
            Interface.CallHook("OnRaidlandsRaidBaseLeaderboardChanged", active.InstanceId);
            if (qualifiesForResults && result.RewardTransactions.Count > 0)
                RetryRewardTransactionsForInstance(active.InstanceId);
            return result;
        }

        private void TrimCurrentWipeHistory()
        {
            var maximum = config.Leaderboard.MaximumDetailedHistoryEntries;
            if (historyData.Results.Count <= maximum)
                return;
            var remove = historyData.Results.Count - maximum;
            historyData.Results = historyData.Results.OrderBy(value => value.EndedUnix).Skip(remove).ToList();
            PrintWarning($"Raid-base history exceeded {maximum:n0} results; pruned {remove:n0} oldest current-wipe result(s).");
        }

        private void ApplyCompletedEventToLeaderboards(RaidBaseEventResult result, RewardProfile profile)
        {
            profile = profile ?? new RewardProfile { MinimumGroupScore = config.Scoring.MinimumScoreToQualify, MinimumMemberScore = 1 };
            ApplyStandingsToPeriods(result.PlayerStandings, "Player", Math.Max(1, config.Scoring.MinimumScoreToQualify), result.EndedUnix);
            ApplyStandingsToPeriods(result.ClanStandings, "Clan", Math.Max(1, profile.MinimumGroupScore), result.EndedUnix);
            ApplyStandingsToPeriods(result.TeamStandings, "RustTeam", Math.Max(1, profile.MinimumGroupScore), result.EndedUnix);
        }

        private void ApplyStandingsToPeriods(List<EventStanding> standings, string scope, int minimumScore, double endedUnix)
        {
            if (standings == null)
                return;
            var qualified = standings.Where(value => value != null && value.Score >= minimumScore).ToList();
            for (var index = 0; index < standings.Count; index++)
            {
                var standing = standings[index];
                if (standing == null)
                    continue;
                var qualifiedIndex = qualified.FindIndex(value => ReferenceEquals(value, standing));
                ApplyStandingToPeriod(leaderboardData.CurrentWipe, standing, scope, qualifiedIndex + 1, qualifiedIndex >= 0, endedUnix);
                ApplyStandingToPeriod(leaderboardData.Lifetime, standing, scope, qualifiedIndex + 1, qualifiedIndex >= 0, endedUnix);
            }
        }

        private void ApplyStandingToPeriod(LeaderboardPeriod period, EventStanding standing, string scope, int qualifiedPlace, bool qualified, double endedUnix)
        {
            var dictionary = LeaderboardDictionary(period, scope);
            var aggregate = GetOrCreateAggregate(dictionary, standing.Id, scope, standing.DisplayName);
            aggregate.DisplayName = standing.DisplayName ?? aggregate.DisplayName;
            aggregate.EventsEntered++;
            aggregate.TotalScore += standing.Score;
            aggregate.PlayerKills += standing.PlayerKills;
            aggregate.PlayerDeaths += standing.PlayerDeaths;
            aggregate.DamageToPlayers += standing.DamageToPlayers;
            aggregate.DamageToEventEntities += standing.DamageToEventEntities;
            aggregate.ExplosiveDamageToEventEntities += standing.ExplosiveDamageToEventEntities;
            aggregate.ToolCupboardsDestroyed += standing.ToolCupboardsDestroyed;
            if (!qualified)
                return;
            aggregate.EventsQualified++;
            aggregate.LastQualifiedUnix = Math.Max(aggregate.LastQualifiedUnix, endedUnix);
            aggregate.SeasonPoints += SeasonPointsForPlace(qualifiedPlace);
            if (qualifiedPlace == 1) aggregate.FirstPlaces++;
            if (qualifiedPlace == 2) aggregate.SecondPlaces++;
            if (qualifiedPlace == 3) aggregate.ThirdPlaces++;
        }

        private Dictionary<string, LeaderboardAggregate> LeaderboardDictionary(LeaderboardPeriod period, string scope)
        {
            NormalizeLeaderboardPeriod(period);
            if (string.Equals(scope, "Clan", StringComparison.OrdinalIgnoreCase)) return period.Clans;
            if (string.Equals(scope, "RustTeam", StringComparison.OrdinalIgnoreCase) || string.Equals(scope, "Team", StringComparison.OrdinalIgnoreCase)) return period.Teams;
            return period.Players;
        }

        private LeaderboardAggregate GetOrCreateAggregate(Dictionary<string, LeaderboardAggregate> dictionary, string id, string scope, string displayName)
        {
            LeaderboardAggregate aggregate;
            if (!dictionary.TryGetValue(id, out aggregate) || aggregate == null)
            {
                aggregate = new LeaderboardAggregate { Id = id, Scope = scope, DisplayName = displayName ?? id };
                dictionary[id] = aggregate;
            }
            return aggregate;
        }

        private int SeasonPointsForPlace(int place)
        {
            var entry = config.Leaderboard.SeasonPlacementPoints.FirstOrDefault(value => value != null && value.Place == place);
            return entry?.Points ?? 0;
        }

        private void PlanRewardTransactions(ActiveRaidBase active, RaidBaseEventResult result)
        {
            var profile = active?.RewardProfileSnapshot;
            if (active == null || result == null || profile == null || !active.RewardPayoutEnabled)
                return;

            var error = ValidateRewardProfile(profile, true);
            if (!string.IsNullOrWhiteSpace(error))
            {
                active.RewardProfileError = error;
                return;
            }

            var standings = BuildPayoutStandings(active, profile);
            if (profile.RewardMode == "FixedPlacements")
            {
                foreach (var placement in profile.Placements)
                {
                    var standing = standings.FirstOrDefault(value => value.Rank == placement.Place);
                    if (standing == null)
                        continue;
                    for (var rewardIndex = 0; rewardIndex < placement.Rewards.Count; rewardIndex++)
                        AddRewardTransactions(active, profile, result, standing, placement.Place, rewardIndex, placement.Rewards[rewardIndex], placement.Rewards[rewardIndex].Amount);
                }
            }
            else
            {
                for (var rewardIndex = 0; rewardIndex < profile.Pool.Count; rewardIndex++)
                {
                    var reward = profile.Pool[rewardIndex];
                    var placementAmounts = AllocatePlacementAmounts(reward.Amount, profile.Placements);
                    foreach (var placement in profile.Placements)
                    {
                        var standing = standings.FirstOrDefault(value => value.Rank == placement.Place);
                        int amount;
                        if (standing == null || !placementAmounts.TryGetValue(placement.Place, out amount) || amount <= 0)
                            continue;
                        AddRewardTransactions(active, profile, result, standing, placement.Place, rewardIndex, reward, amount);
                    }
                }
            }

            result.RewardTransactions = rewardLedger.Transactions.Values
                .Where(value => value != null && value.InstanceId == active.InstanceId)
                .OrderBy(value => value.Place).ThenBy(value => value.TransactionId)
                .Select(CloneJson).ToList();
        }

        private Dictionary<int, int> AllocatePlacementAmounts(int total, List<RewardPlacementDefinition> placements)
        {
            var result = new Dictionary<int, int>();
            if (total <= 0 || placements == null || placements.Count == 0)
                return result;
            var rows = placements.Select(value => new
            {
                value.Place,
                Exact = total * Math.Max(0d, value.Percent) / 100d
            }).Select(value => new
            {
                value.Place,
                value.Exact,
                Floor = (int)Math.Floor(value.Exact),
                Remainder = value.Exact - Math.Floor(value.Exact)
            }).ToList();
            foreach (var row in rows) result[row.Place] = row.Floor;
            var remaining = Math.Max(0, total - rows.Sum(value => value.Floor));
            foreach (var row in rows.OrderByDescending(value => value.Remainder).ThenBy(value => value.Place).Take(remaining))
                result[row.Place]++;
            return result;
        }

        private void AddRewardTransactions(ActiveRaidBase active, RewardProfile profile, RaidBaseEventResult result,
            EventStanding standing, int place, int rewardIndex, RewardDefinition reward, int totalAmount)
        {
            if (reward == null || totalAmount <= 0 || standing == null)
                return;
            var members = standing.Scope == "Player"
                ? standing.Members.Take(1).ToList()
                : standing.Members.Where(value => value != null && value.Score >= profile.MinimumMemberScore).ToList();
            if (members.Count == 0)
                return;
            var allocations = AllocateMemberAmounts(totalAmount, members, profile.GroupDistribution);
            foreach (var allocation in allocations.Where(value => value.Amount > 0))
            {
                var id = $"{active.InstanceId}:{place}:{rewardIndex}:{reward.Type.ToLowerInvariant()}:{allocation.UserId}";
                if (rewardLedger.Transactions.ContainsKey(id))
                    continue;
                var transaction = new RewardTransaction
                {
                    TransactionId = id,
                    WipeKey = leaderboardData.CurrentWipeKey,
                    InstanceId = active.InstanceId,
                    ProfileId = profile.Id,
                    Place = place,
                    GroupScope = standing.Scope,
                    GroupId = standing.Id,
                    UserId = allocation.UserId,
                    DisplayName = allocation.DisplayName,
                    Type = reward.Type,
                    Amount = allocation.Amount,
                    ShortName = reward.ShortName,
                    SkinId = reward.SkinId,
                    Command = reward.Command,
                    RequireOnline = reward.RequireOnline,
                    Status = "pending",
                    CreatedUnix = NowUnix(),
                    UpdatedUnix = NowUnix()
                };
                rewardLedger.Transactions[id] = transaction;
                result.RewardTransactions.Add(CloneJson(transaction));
            }
        }

        private List<AllocationCandidate> AllocateMemberAmounts(int total, List<EventStandingMember> members, string distribution)
        {
            var result = new List<AllocationCandidate>();
            if (total <= 0 || members == null || members.Count == 0)
                return result;
            var even = string.Equals(distribution, "Even", StringComparison.OrdinalIgnoreCase);
            var totalWeight = even ? members.Count : members.Sum(value => Math.Max(0, value.Score));
            if (totalWeight <= 0)
                return result;
            foreach (var member in members)
            {
                var weight = even ? 1d : Math.Max(0, member.Score);
                var exact = total * weight / totalWeight;
                result.Add(new AllocationCandidate
                {
                    UserId = member.UserId,
                    DisplayName = member.DisplayName,
                    Score = member.Score,
                    Exact = exact,
                    Amount = (int)Math.Floor(exact),
                    Remainder = exact - Math.Floor(exact)
                });
            }
            var remaining = Math.Max(0, total - result.Sum(value => value.Amount));
            foreach (var row in result.OrderByDescending(value => value.Remainder).ThenByDescending(value => even ? 0 : value.Score)
                         .ThenBy(value => value.UserId, StringComparer.OrdinalIgnoreCase).Take(remaining))
                row.Amount++;
            return result.OrderByDescending(value => value.Score).ThenBy(value => value.UserId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private string RunRewardCalculationSelfTest()
        {
            var failures = new List<string>();
            var percentage = AllocatePlacementAmounts(1001, new List<RewardPlacementDefinition>
            {
                new RewardPlacementDefinition { Place = 1, Percent = 50f },
                new RewardPlacementDefinition { Place = 2, Percent = 30f },
                new RewardPlacementDefinition { Place = 3, Percent = 20f }
            });
            if (!percentage.ContainsKey(1) || percentage[1] != 501 || percentage[2] != 300 || percentage[3] != 200 || percentage.Values.Sum() != 1001)
                failures.Add("percentage 1001@50/30/20");

            var thirdRemainder = AllocatePlacementAmounts(10, new List<RewardPlacementDefinition>
            {
                new RewardPlacementDefinition { Place = 1, Percent = 33.33f },
                new RewardPlacementDefinition { Place = 2, Percent = 33.33f },
                new RewardPlacementDefinition { Place = 3, Percent = 33.34f }
            });
            if (thirdRemainder[1] != 3 || thirdRemainder[2] != 3 || thirdRemainder[3] != 4)
                failures.Add("percentage decimal remainder");

            var members = new List<EventStandingMember>
            {
                new EventStandingMember { UserId = "a", DisplayName = "A", Score = 3 },
                new EventStandingMember { UserId = "b", DisplayName = "B", Score = 2 },
                new EventStandingMember { UserId = "c", DisplayName = "C", Score = 2 }
            };
            var even = AllocateMemberAmounts(10, members, "Even").ToDictionary(value => value.UserId, value => value.Amount);
            if (even["a"] != 4 || even["b"] != 3 || even["c"] != 3 || even.Values.Sum() != 10)
                failures.Add("even member split");
            var weighted = AllocateMemberAmounts(10, members, "ContributionWeighted").ToDictionary(value => value.UserId, value => value.Amount);
            if (weighted["a"] != 4 || weighted["b"] != 3 || weighted["c"] != 3 || weighted.Values.Sum() != 10)
                failures.Add("contribution member split");

            var zero = AllocateMemberAmounts(10, members.Where(value => value.Score >= 99).ToList(), "ContributionWeighted");
            if (zero.Count != 0) failures.Add("unqualified member shares");
            return failures.Count == 0
                ? "RaidlandsEvents reward calculation self-test: PASS (percentage rounding, decimal remainder, even split, contribution split, unqualified members). No payout was performed."
                : "RaidlandsEvents reward calculation self-test: FAIL - " + string.Join(", ", failures);
        }

        private int RetryRewardTransactionsForInstance(string instanceId)
        {
            var paid = 0;
            foreach (var transaction in rewardLedger.Transactions.Values
                         .Where(value => value != null && value.InstanceId == instanceId && value.Status == "pending").ToList())
                if (TryExecuteRewardTransaction(transaction, false)) paid++;
            return paid;
        }

        private int RetryRewardTransactions(string userId, bool includeReview, string transactionId = null)
        {
            var paid = 0;
            var query = rewardLedger.Transactions.Values.Where(value => value != null);
            if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(value => value.UserId == userId);
            if (!string.IsNullOrWhiteSpace(transactionId)) query = query.Where(value => value.TransactionId == transactionId);
            else query = query.Where(value => value.Status == "pending" || (includeReview && (value.Status == "failed" || value.Status == "review-required")));
            foreach (var transaction in query.OrderBy(value => value.CreatedUnix).ToList())
                if (TryExecuteRewardTransaction(transaction, includeReview || !string.IsNullOrWhiteSpace(transactionId))) paid++;
            return paid;
        }

        private bool TryExecuteRewardTransaction(RewardTransaction transaction, bool allowReview)
        {
            if (transaction == null || transaction.Amount <= 0 || transaction.Status == "paid")
                return false;
            if (transaction.Status == "review-required" && !allowReview)
                return false;

            var player = PlayerFromStringId(transaction.UserId);
            if ((transaction.Type == "Item" || transaction.RequireOnline) && (player == null || !player.IsConnected))
            {
                transaction.Status = "pending";
                transaction.LastError = "Player must be online for this reward.";
                transaction.UpdatedUnix = NowUnix();
                SaveRewardLedgerAndHistory(transaction);
                Interface.CallHook("OnRaidlandsRaidBaseRewardUpdated", RewardTransactionToDictionary(transaction));
                return false;
            }

            transaction.Status = "processing";
            transaction.AttemptCount++;
            transaction.UpdatedUnix = NowUnix();
            transaction.LastError = null;
            SaveRewardLedgerAndHistory(transaction);

            bool success;
            bool partial = false;
            string error;
            if (transaction.Type == "RP")
                success = TryAddServerRewardsPoints(transaction.UserId, transaction.Amount, out error);
            else if (transaction.Type == "Item")
                success = TryGiveRewardItem(player, transaction, out partial, out error);
            else
                success = TryRunRewardCommand(transaction, player, out error);

            transaction.UpdatedUnix = NowUnix();
            transaction.LastError = error;
            if (success)
            {
                transaction.Status = "paid";
                ApplyPaidRewardAggregate(transaction);
                TellRewardPlayer(transaction.UserId, $"{config.ChatPrefix} Your raid-base reward paid: <color=#B6F34A>{RewardTransactionSummary(transaction)}</color> for place #{transaction.Place}.");
            }
            else if (partial)
            {
                transaction.Status = "review-required";
                transaction.LastError = "Part of the item reward may have been delivered; admin review is required. " + error;
            }
            else
            {
                transaction.Status = transaction.Type == "Item" || config.Rewards.QueueRewardsIfServerRewardsMissing ? "pending" : "failed";
            }

            SaveRewardLedgerAndHistory(transaction);
            Interface.CallHook("OnRaidlandsRaidBaseRewardUpdated", RewardTransactionToDictionary(transaction));
            return success;
        }

        private bool TryGiveRewardItem(BasePlayer player, RewardTransaction transaction, out bool partial, out string error)
        {
            partial = false;
            error = null;
            if (player?.inventory == null)
            {
                error = "Player inventory is unavailable.";
                return false;
            }
            var definition = ItemManager.FindItemDefinition(transaction.ShortName);
            if (definition == null)
            {
                error = $"Unknown item shortname '{transaction.ShortName}'.";
                return false;
            }
            if (!CanReceiveRewardItem(player, definition, transaction.SkinId, transaction.Amount))
            {
                error = "Player inventory does not have enough stack capacity; reward remains queued.";
                return false;
            }
            var remaining = transaction.Amount;
            var stackSize = Math.Max(1, definition.stackable);
            while (remaining > 0)
            {
                var amount = Math.Min(stackSize, remaining);
                var item = ItemManager.Create(definition, amount, transaction.SkinId);
                if (item == null)
                {
                    error = "Rust could not create the reward item.";
                    return false;
                }
                if (!player.inventory.GiveItem(item))
                {
                    item.Remove();
                    partial = remaining != transaction.Amount;
                    error = "Rust rejected an inventory transfer after capacity preflight.";
                    return false;
                }
                remaining -= amount;
            }
            return true;
        }

        private bool CanReceiveRewardItem(BasePlayer player, ItemDefinition definition, ulong skinId, int amount)
        {
            if (player?.inventory == null || definition == null || amount <= 0)
                return false;
            var stackSize = Math.Max(1, definition.stackable);
            var capacity = 0L;
            foreach (var container in new[] { player.inventory.containerMain, player.inventory.containerBelt })
            {
                if (container == null)
                    continue;
                foreach (var item in container.itemList ?? new List<Item>())
                {
                    if (item?.info == definition && item.skin == skinId)
                        capacity += Math.Max(0, stackSize - item.amount);
                }
                capacity += Math.Max(0, container.capacity - (container.itemList?.Count ?? 0)) * (long)stackSize;
            }
            return capacity >= amount;
        }

        private bool TryRunRewardCommand(RewardTransaction transaction, BasePlayer player, out string error)
        {
            error = null;
            if (!TryValidateRewardCommand(transaction.Command, out error))
                return false;
            var command = transaction.Command.Trim().TrimStart('/')
                .Replace("{playerId}", transaction.UserId ?? string.Empty)
                .Replace("{amount}", transaction.Amount.ToString(CultureInfo.InvariantCulture))
                .Replace("{rank}", transaction.Place.ToString(CultureInfo.InvariantCulture))
                .Replace("{instanceId}", CleanStableId(transaction.InstanceId, "unknown"))
                .Replace("{clanTag}", transaction.GroupScope == "Clan" ? CleanStableId(transaction.GroupId, "solo") : "solo")
                .Replace("{teamId}", transaction.GroupScope == "RustTeam" ? CleanStableId(transaction.GroupId, "0") : "0");
            try
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private void ApplyPaidRewardAggregate(RewardTransaction transaction)
        {
            if (transaction == null || transaction.PaidAggregateApplied)
                return;
            ApplyPaidRewardToPeriod(leaderboardData.Lifetime, transaction);
            if (string.Equals(transaction.WipeKey, leaderboardData.CurrentWipeKey, StringComparison.OrdinalIgnoreCase))
                ApplyPaidRewardToPeriod(leaderboardData.CurrentWipe, transaction);
            transaction.PaidAggregateApplied = true;
        }

        private void ApplyPaidRewardToPeriod(LeaderboardPeriod period, RewardTransaction transaction)
        {
            var player = GetOrCreateAggregate(period.Players, transaction.UserId, "Player", transaction.DisplayName);
            AddPaidRewardMetric(player, transaction);
            if (transaction.GroupScope == "Clan" || transaction.GroupScope == "RustTeam")
            {
                var groupDictionary = LeaderboardDictionary(period, transaction.GroupScope);
                var group = GetOrCreateAggregate(groupDictionary, transaction.GroupId, transaction.GroupScope, transaction.GroupId);
                AddPaidRewardMetric(group, transaction);
            }
        }

        private void AddPaidRewardMetric(LeaderboardAggregate aggregate, RewardTransaction transaction)
        {
            if (transaction.Type == "RP") aggregate.RpPaid += transaction.Amount;
            else if (transaction.Type == "Item") aggregate.ItemUnitsPaid += transaction.Amount;
            else if (transaction.Type == "Command") aggregate.CommandsPaid++;
        }

        private void SaveRewardLedgerAndHistory(RewardTransaction transaction)
        {
            SyncHistoryRewardTransaction(transaction);
            SaveRewardLedger();
            SaveHistoryData();
            SaveLeaderboardData();
        }

        private void SyncHistoryRewardTransaction(RewardTransaction transaction)
        {
            if (transaction == null)
                return;
            var result = historyData.Results.FirstOrDefault(value => value != null && value.InstanceId == transaction.InstanceId);
            if (result == null)
                return;
            var index = result.RewardTransactions.FindIndex(value => value != null && value.TransactionId == transaction.TransactionId);
            if (index >= 0) result.RewardTransactions[index] = CloneJson(transaction);
            else result.RewardTransactions.Add(CloneJson(transaction));
        }

        private string TerminalStateFromReason(string reason)
        {
            var value = (reason ?? string.Empty).ToLowerInvariant();
            if (value.Contains("fail")) return "Failed";
            if (value.Contains("timeout") || value.Contains("expired") || value.Contains("lifetime")) return "Expired";
            return "Cancelled";
        }

        private bool TryAddServerRewardsPoints(string userId, int amount, out string error)
        {
            error = null;

            if (amount <= 0)
            {
                error = "RP amount must be positive.";
                return false;
            }

            if (ServerRewards == null || !ServerRewards.IsLoaded)
            {
                error = "ServerRewards plugin is not loaded.";
                return false;
            }

            ulong parsedUserId;
            if (!ulong.TryParse(userId, out parsedUserId))
            {
                error = "Invalid SteamID for ServerRewards credit.";
                return false;
            }

            string beforeDetails;
            var balanceBefore = CheckServerRewardsPoints(userId, out beforeDetails);
            var attempts = new List<string>();
            var player = PlayerFromStringId(userId);

            if (player != null && TryCallServerRewardsBool("BasePlayer", () => ServerRewards.Call("AddPoints", player, amount), attempts))
                return VerifyServerRewardsCredit(userId, amount, balanceBefore, "BasePlayer", attempts, out error);

            if (TryCallServerRewardsBool("parsed ulong", () => ServerRewards.Call("AddPoints", parsedUserId, amount), attempts))
                return VerifyServerRewardsCredit(userId, amount, balanceBefore, "parsed ulong", attempts, out error);

            if (TryCallServerRewardsBool("UserIDString", () => ServerRewards.Call("AddPoints", userId, amount), attempts))
                return VerifyServerRewardsCredit(userId, amount, balanceBefore, "UserIDString", attempts, out error);

            if (TryAddServerRewardsPointsViaCommand(userId, amount, balanceBefore, attempts))
                return true;

            string afterDetails;
            var balanceAfter = CheckServerRewardsPoints(userId, out afterDetails);
            error = $"ServerRewards credit did not increase balance for {userId}. Amount={amount:n0}, balanceBefore={balanceBefore:n0}, balanceAfter={balanceAfter:n0}. Checks before: {beforeDetails}. Checks after: {afterDetails}. Attempts: {string.Join("; ", attempts)}.";
            return false;
        }

        private bool VerifyServerRewardsCredit(string userId, int amount, int balanceBefore, string label, List<string> attempts, out string error)
        {
            string afterDetails;
            var balanceAfter = CheckServerRewardsPoints(userId, out afterDetails);
            var minimumExpected = balanceBefore + amount;
            attempts.Add($"{label} verify before={balanceBefore:n0}, after={balanceAfter:n0}, minimumExpected={minimumExpected:n0}, checks={afterDetails}");

            if (balanceAfter >= minimumExpected)
            {
                error = null;
                return true;
            }

            error = $"ServerRewards {label} credit returned true but balance did not increase enough. Amount={amount:n0}, balanceBefore={balanceBefore:n0}, balanceAfter={balanceAfter:n0}, minimumExpected={minimumExpected:n0}. Checks: {afterDetails}.";
            return false;
        }

        private bool TryAddServerRewardsPointsViaCommand(string userId, int amount, int balanceBefore, List<string> attempts)
        {
            foreach (var rpCommand in ServerRewardsAdminCommands())
            {
                try
                {
                    var command = $"{rpCommand} add {userId} {amount}";
                    var result = ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command);
                    string afterDetails;
                    var balanceAfter = CheckServerRewardsPoints(userId, out afterDetails);
                    var minimumExpected = balanceBefore + amount;

                    attempts.Add($"{rpCommand} add command result={FormatServerRewardsResult(result)}, before={balanceBefore:n0}, after={balanceAfter:n0}, minimumExpected={minimumExpected:n0}, checks={afterDetails}");
                    if (balanceAfter >= minimumExpected)
                        return true;
                }
                catch (Exception exception)
                {
                    attempts.Add($"{rpCommand} add command error({exception.Message})");
                }
            }

            return false;
        }

        private void TellRewardPlayer(string userId, string message)
        {
            if (config?.Rewards?.TellPlayersAboutRpRewards != true || string.IsNullOrWhiteSpace(userId))
                return;

            ulong parsed;
            if (!ulong.TryParse(userId, out parsed))
                return;

            var player = BasePlayer.FindByID(parsed);
            if (player != null)
                SendReply(player, message);
        }

        private string CompletionSummary(ActiveRaidBase active, List<RaidBaseScoreEntry> leaderboard)
        {
            if (leaderboard == null || leaderboard.Count == 0)
                return $"{active.PublicName} has been completed.";

            var winner = leaderboard[0];
            return $"{active.PublicName} has been completed. Winner: {winner.DisplayName ?? winner.UserId} with {winner.TotalScore} points.";
        }

        private string CompactLeaderboard(List<RaidBaseScoreEntry> leaderboard)
        {
            if (leaderboard == null || leaderboard.Count == 0)
                return "No qualified scores.";

            return string.Join(", ", leaderboard
                .Take(Math.Min(config.Scoring.MaxLeaderboardEntries, leaderboard.Count))
                .Select((score, index) => $"#{index + 1} {score.DisplayName ?? score.UserId} {score.TotalScore}"));
        }

        private void CompleteInstance(ActiveRaidBase active, string reason)
        {
            if (active == null || active.Status == "completed" || active.Status == "cleaning")
                return;

            var leaderboard = BuildLeaderboard(active, false);
            active.Status = "completed";
            active.CompletedUnix = NowUnix();
            active.CompletedReason = reason;
            CommitTerminalResult(active, "Completed", reason, true);
            SaveAllEventData();
            RefreshOpenEventsManagerUis();

            if (active.IsAnnounced)
            {
                Server.Broadcast($"{config.ChatPrefix} {CompletionSummary(active, leaderboard)}");
                if (config.Scoring.AnnounceLeaderboardOnCompletion && leaderboard.Count > 0)
                    Server.Broadcast($"{config.ChatPrefix} Top raiders: {CompactLeaderboard(leaderboard)}");
            }

            var instanceId = active.InstanceId;
            var delay = config.Cleanup.CompletionCleanupDelaySeconds;
            timer.Once(delay, () => CleanupInstance(instanceId, reason, true));
        }

        private void StartExpiryTimer()
        {
            expiryTimer?.Destroy();
            expiryTimer = timer.Every(60f, CheckExpiredInstances);
            CheckExpiredInstances();
        }

        private void ResumeCompletedInstanceCleanup()
        {
            foreach (var instanceId in data.ActiveRaidBases.Values
                         .Where(value => value != null && string.Equals(value.Status, "completed", StringComparison.OrdinalIgnoreCase))
                         .Select(value => value.InstanceId).ToList())
                timer.Once(2f, () => CleanupInstance(instanceId, "resumed completed-event cleanup", true));
        }

        private void CheckExpiredInstances()
        {
            var now = NowUnix();
            foreach (var entry in data.ActiveRaidBases.Values.ToList())
            {
                if (entry.ExpiresUnix > 0 && entry.ExpiresUnix <= now)
                    CleanupInstance(entry.InstanceId, "forced cleanup timeout");
            }
        }

        private void InitializeSpawnGrid(bool forceRebuild)
        {
            spawnGridBuildTimer?.Destroy();
            spawnGridBuildTimer = null;
            spawnGridBuilding = false;
            spawnGridReady = false;
            spawnGridReserved.Clear();
            spawnGridTemporaryUntil.Clear();
            spawnGridLayoutRejections.Clear();
            spawnGridRejectionCounts.Clear();

            if (config?.SpawnGrid?.Enabled != true)
            {
                spawnGridCache = new SpawnGridCache();
                return;
            }

            if (!forceRebuild && TryLoadSpawnGridCache())
            {
                spawnGridReady = true;
                spawnGridProcessed = Math.Max(spawnGridCache.ScannedPointCount, spawnGridCache.Candidates.Count);
                spawnGridTotal = Math.Max(1, spawnGridProcessed);
                foreach (var rejection in spawnGridCache.StaticRejections ?? new Dictionary<string, long>())
                    spawnGridRejectionCounts[rejection.Key] = rejection.Value;
                ReserveActiveSpawnGridCandidates();
                Puts($"Spawn grid loaded from cache with {spawnGridCache.Candidates.Count} static candidate(s) for {World.Size}/{World.Seed}.");
                WarnIfSpawnGridUnhealthy();
                ScheduleAutomaticLocationSearch(0.1f);
                return;
            }

            spawnGridCache = CreateEmptySpawnGridCache();
            var halfSize = WorldHalfSize();
            var margin = Mathf.Clamp(config.LocationRules.MinDistanceFromMapEdge, 0f, halfSize - 50f);
            spawnGridMin = -halfSize + margin;
            spawnGridMax = halfSize - margin;
            spawnGridNextX = spawnGridMin;
            spawnGridNextZ = spawnGridMin;
            spawnGridProcessed = 0;
            var axisCount = Math.Max(1, Mathf.CeilToInt((spawnGridMax - spawnGridMin) / config.SpawnGrid.CellSize));
            spawnGridTotal = axisCount * axisCount;
            spawnGridBuildStartedUnix = NowUnix();
            spawnGridMaximumSliceMilliseconds = 0d;
            spawnGridBuilding = true;
            Puts($"Spawn grid rebuild started for {World.Size}/{World.Seed}: cell={config.SpawnGrid.CellSize:0.##}m, points={spawnGridTotal}, budget={config.SpawnGrid.BuildBudgetMilliseconds:0.##}ms.");
            ScheduleSpawnGridBuildSlice(0.05f);
        }

        private SpawnGridCache CreateEmptySpawnGridCache()
        {
            return new SpawnGridCache
            {
                SchemaVersion = SpawnGridSchemaVersion,
                ProtocolSave = Rust.Protocol.save,
                WorldSize = World.Size,
                WorldSeed = World.Seed,
                LevelUrl = ConVar.Server.levelurl ?? string.Empty,
                RulesFingerprint = SpawnGridRulesFingerprint(),
                GeneratedUnix = 0d,
                Candidates = new List<StoredVector3>()
            };
        }

        private bool TryLoadSpawnGridCache()
        {
            if (!config.SpawnGrid.PersistCache || !Interface.Oxide.DataFileSystem.ExistsDatafile(SpawnGridDataFileName))
                return false;

            try
            {
                var cached = Interface.Oxide.DataFileSystem.ReadObject<SpawnGridCache>(SpawnGridDataFileName);
                if (cached == null || cached.Candidates == null || cached.SchemaVersion != SpawnGridSchemaVersion)
                    return false;
                if (cached.ProtocolSave != Rust.Protocol.save || cached.WorldSize != World.Size || cached.WorldSeed != World.Seed)
                    return false;
                if (!string.Equals(cached.LevelUrl ?? string.Empty, ConVar.Server.levelurl ?? string.Empty, StringComparison.Ordinal))
                    return false;
                if (!string.Equals(cached.RulesFingerprint, SpawnGridRulesFingerprint(), StringComparison.Ordinal))
                    return false;
                spawnGridCache = cached;
                return true;
            }
            catch (Exception exception)
            {
                PrintWarning($"Spawn grid cache could not be loaded and will be rebuilt: {exception.GetType().Name}: {exception.Message}");
                return false;
            }
        }

        private void ScheduleSpawnGridBuildSlice(float delay = 0.01f)
        {
            if (!spawnGridBuilding || spawnGridBuildTimer != null)
                return;
            spawnGridBuildTimer = timer.Once(Mathf.Max(0.01f, delay), RunSpawnGridBuildSlice);
        }

        private void RunSpawnGridBuildSlice()
        {
            spawnGridBuildTimer = null;
            if (!spawnGridBuilding)
                return;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var checks = 0;
            while (spawnGridNextX < spawnGridMax && checks < config.SpawnGrid.CandidateChecksPerSlice && stopwatch.Elapsed.TotalMilliseconds < config.SpawnGrid.BuildBudgetMilliseconds)
            {
                var position = new Vector3(spawnGridNextX, 0f, spawnGridNextZ);
                position.y = TerrainHeight(position);
                string rejection;
                if (ValidateStaticSpawnGridPoint(position, out rejection))
                    spawnGridCache.Candidates.Add(new StoredVector3(position));
                else
                    CountSpawnGridRejection("static:" + rejection);

                checks++;
                spawnGridProcessed++;
                spawnGridNextZ += config.SpawnGrid.CellSize;
                if (spawnGridNextZ >= spawnGridMax)
                {
                    spawnGridNextZ = spawnGridMin;
                    spawnGridNextX += config.SpawnGrid.CellSize;
                }
            }

            stopwatch.Stop();
            spawnGridMaximumSliceMilliseconds = Math.Max(spawnGridMaximumSliceMilliseconds, stopwatch.Elapsed.TotalMilliseconds);
            if (spawnGridNextX >= spawnGridMax)
            {
                CompleteSpawnGridBuild();
                return;
            }

            if (spawnGridProcessed > 0 && spawnGridProcessed % Math.Max(1000, spawnGridTotal / 4) < checks)
                Puts($"Spawn grid {spawnGridProcessed * 100f / Math.Max(1, spawnGridTotal):0}% complete: {spawnGridCache.Candidates.Count} candidate(s).");
            ScheduleSpawnGridBuildSlice();
        }

        private void CompleteSpawnGridBuild()
        {
            spawnGridBuilding = false;
            spawnGridReady = true;
            spawnGridCache.GeneratedUnix = NowUnix();
            spawnGridCache.ScannedPointCount = spawnGridProcessed;
            spawnGridCache.StaticRejections = spawnGridRejectionCounts
                .Where(pair => pair.Key.StartsWith("static:", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            ReserveActiveSpawnGridCandidates();
            if (config.SpawnGrid.PersistCache)
            {
                try
                {
                    Interface.Oxide.DataFileSystem.WriteObject(SpawnGridDataFileName, spawnGridCache, false);
                }
                catch (Exception exception)
                {
                    PrintWarning($"Spawn grid cache could not be saved: {exception.GetType().Name}: {exception.Message}");
                }
            }
            Puts($"Spawn grid completed in {FormatDuration(Math.Max(0d, NowUnix() - spawnGridBuildStartedUnix))}: {spawnGridCache.Candidates.Count}/{spawnGridProcessed} candidate(s), maxSlice={spawnGridMaximumSliceMilliseconds:0.###}ms.");
            WarnIfSpawnGridUnhealthy();
            ScheduleAutomaticLocationSearch(0.1f);
        }

        private bool ValidateStaticSpawnGridPoint(Vector3 position, out string reason)
        {
            reason = null;
            if (IsNearMapEdge(position)) { reason = "map edge"; return false; }
            var terrainHeight = TerrainHeight(position);
            float waterHeight;
            if (TryGetWaterSurfaceHeight(position, terrainHeight, out waterHeight))
            {
                if (config.LocationRules.BlockWater && !AdaptiveWaterSupportEnabled()) { reason = "water"; return false; }
                if (AdaptiveWaterSupportEnabled()
                    && waterHeight - terrainHeight
                    > config.Paste.AdaptiveFoundations.MaximumWaterDepthMeters + 0.001f)
                {
                    reason = "water exceeds maximum depth";
                    return false;
                }
            }
            if (!AdaptiveFoundationTerrainEnabled())
            {
                if (TerrainGrade(position, config.SpawnGrid.CellSize * 0.5f) > config.LocationRules.MaxSlope) { reason = "slope"; return false; }
                var staticSampleRadius = Mathf.Min(config.LocationRules.FlatnessSampleRadius, config.SpawnGrid.CellSize * 0.5f);
                if (!IsFlatEnough(position, staticSampleRadius)) { reason = "terrain variance"; return false; }
            }
            if (IsTopologyBlocked(position, config.SpawnGrid.CellSize * 0.5f, out reason)) return false;
            if (config.LocationRules.BlockMonuments && IsInBlockedMonument(position, out var monumentName)) { reason = "monument " + monumentName; return false; }
            if (config.LocationRules.BlockSafeZones && IsInsideNativeSafeZone(position, 0f)) { reason = "safe zone"; return false; }
            if (config.LocationRules.BlockNoBuildZones && StaticPointTouchesNoBuildZone(position, config.SpawnGrid.CellSize * 0.5f)) { reason = "no-build zone"; return false; }
            if (StaticPointTouchesSignificantWorldObstacle(position)) { reason = "world obstacle"; return false; }
            return true;
        }

        private bool IsTopologyBlocked(Vector3 position, float radius, out string reason)
        {
            reason = null;
            if (TerrainMeta.TopologyMap == null)
                return false;
            var topology = TerrainMeta.TopologyMap.GetTopology(position, Mathf.Max(1f, radius));
            if (config.LocationRules.BlockRoads && (topology & (int)(TerrainTopology.Enum.Road | TerrainTopology.Enum.Roadside | TerrainTopology.Enum.Rail | TerrainTopology.Enum.Railside)) != 0)
            {
                reason = "road or rail topology";
                return true;
            }
            if (config.LocationRules.BlockMonuments && (topology & (int)TerrainTopology.Enum.Monument) != 0)
            {
                reason = "monument topology";
                return true;
            }
            return false;
        }

        private bool IsInsideNativeSafeZone(Vector3 position, float padding)
        {
            var safeZones = TriggerSafeZone.allSafeZones;
            if (safeZones == null)
                return false;

            foreach (var safeZone in safeZones)
            {
                if (safeZone == null)
                    continue;
                var collider = safeZone.triggerCollider;
                var center = collider == null ? safeZone.transform.position : collider.bounds.center;
                var radius = collider == null ? 200f : Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.z);
                var dx = center.x - position.x;
                var dz = center.z - position.z;
                var required = radius + Mathf.Max(0f, padding);
                if (dx * dx + dz * dz <= required * required)
                    return true;
            }
            return false;
        }

        private bool StaticPointTouchesNoBuildZone(Vector3 position, float horizontalRadius)
        {
            if (PreventBuildingLayer == 0)
                return false;

            var halfExtents = new Vector3(Mathf.Max(1f, horizontalRadius), 50f, Mathf.Max(1f, horizontalRadius));
            var center = position + Vector3.up * 25f;
            return Physics.OverlapBoxNonAlloc(center, halfExtents, automaticSearchColliders, Quaternion.identity, PreventBuildingLayer, QueryTriggerInteraction.Collide) > 0;
        }

        private bool StaticPointTouchesSignificantWorldObstacle(Vector3 position)
        {
            if (StaticWorldObstacleLayer == 0)
                return false;
            var count = Physics.OverlapSphereNonAlloc(position + Vector3.up * 1.5f, 2.5f, automaticSearchColliders, StaticWorldObstacleLayer, QueryTriggerInteraction.Ignore);
            if (count >= automaticSearchColliders.Length)
                return true;
            for (var index = 0; index < count; index++)
            {
                if (IsSignificantWorldCollider(automaticSearchColliders[index]))
                    return true;
            }
            return false;
        }

        private bool IsSignificantWorldCollider(Collider collider)
        {
            if (collider == null)
                return false;
            var entity = collider.GetComponentInParent<BaseEntity>();
            var name = $"{collider.name} {collider.transform?.parent?.name} {entity?.ShortPrefabName} {entity?.PrefabName}".ToLowerInvariant();
            if (collider.isTrigger || name.Contains("terrain") || name.Contains("road") || name.Contains("river") || name.Contains("rail") || name.Contains("trigger") || name.Contains("grass") || name.Contains("bush") || name.Contains("decor") || name.Contains("flower") || name.Contains("collectable"))
                return false;
            var size = Mathf.Max(collider.bounds.size.x, collider.bounds.size.y, collider.bounds.size.z);
            if ((name.Contains("cliff") || name.Contains("formation")) && size > 2f)
                return true;
            if (name.Contains("rock") && size > 2.5f)
                return true;
            if ((name.Contains("tree") || name.Contains("trunk")) && size > 2f)
                return true;
            if ((name.Contains("building") || name.Contains("structure") || name.Contains("wall") || name.Contains("foundation") || name.Contains("bunker") || name.Contains("tunnel") || name.Contains("cave") || name.Contains("ruin")) && size > 2f)
                return true;
            return false;
        }

        private string SpawnGridRulesFingerprint()
        {
            var rules = config.LocationRules;
            var value = string.Join("|", new[]
            {
                config.SpawnGrid.CellSize.ToString("0.###", CultureInfo.InvariantCulture),
                rules.MinDistanceFromMapEdge.ToString("0.###", CultureInfo.InvariantCulture),
                rules.BlockWater.ToString(), rules.WaterClearance.ToString("0.###", CultureInfo.InvariantCulture),
                rules.BlockRoads.ToString(), rules.BlockMonuments.ToString(), rules.BlockSafeZones.ToString(), rules.BlockNoBuildZones.ToString(),
                rules.MonumentRadiusPadding.ToString("0.###", CultureInfo.InvariantCulture),
                rules.DefaultMonumentRadius.ToString("0.###", CultureInfo.InvariantCulture),
                rules.FlatnessSampleRadius.ToString("0.###", CultureInfo.InvariantCulture),
                rules.MaxFlatnessDelta.ToString("0.###", CultureInfo.InvariantCulture),
                rules.MaxSlope.ToString("0.###", CultureInfo.InvariantCulture),
                config.Paste.AdaptiveFoundations.Enabled.ToString(),
                config.Paste.AdaptiveFoundations.MaximumFoundationClearanceMeters.ToString("0.###", CultureInfo.InvariantCulture),
                config.Paste.AdaptiveFoundations.MaximumLoweringMeters.ToString("0.###", CultureInfo.InvariantCulture),
                config.Paste.AdaptiveFoundations.RaiseBaseLayerAboveWater.ToString(),
                config.Paste.AdaptiveFoundations.WaterSurfaceClearanceMeters.ToString("0.###", CultureInfo.InvariantCulture),
                config.Paste.AdaptiveFoundations.MaximumWaterDepthMeters.ToString("0.###", CultureInfo.InvariantCulture)
            });
            unchecked
            {
                uint hash = 2166136261;
                foreach (var character in value)
                    hash = (hash ^ character) * 16777619;
                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        private void CountSpawnGridRejection(string reason)
        {
            reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            long count;
            spawnGridRejectionCounts.TryGetValue(reason, out count);
            spawnGridRejectionCounts[reason] = count + 1;
        }

        private void WarnIfSpawnGridUnhealthy()
        {
            if (spawnGridCache.Candidates.Count < config.SpawnGrid.MinimumHealthyCandidateCount)
            {
                var staticReasons = spawnGridRejectionCounts
                    .Where(pair => pair.Key.StartsWith("static:", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(pair => pair.Value)
                    .Take(6)
                    .Select(pair => $"{pair.Key.Substring(7)}={pair.Value}");
                var diagnostic = string.Join(", ", staticReasons);
                PrintWarning($"Spawn grid has only {spawnGridCache.Candidates.Count} candidate(s); healthy target is {config.SpawnGrid.MinimumHealthyCandidateCount}. Automatic requests will wait rather than relax safety rules. Top static rejections: {(string.IsNullOrWhiteSpace(diagnostic) ? "not retained (cache load); run revents.grid rebuild" : diagnostic)}.");
            }
        }

        private string BuildSpawnGridStatus(bool includeRejections)
        {
            RemoveExpiredSpawnGridTemporaryBlocks();
            var state = !config.SpawnGrid.Enabled ? "disabled" : spawnGridBuilding ? "building" : spawnGridReady ? "ready" : "not ready";
            var warning = spawnGridReady && spawnGridCache.Candidates.Count < config.SpawnGrid.MinimumHealthyCandidateCount ? " POOL BELOW HEALTHY THRESHOLD." : string.Empty;
            var available = Math.Max(0, spawnGridCache.Candidates.Count - spawnGridReserved.Count - spawnGridTemporaryUntil.Count);
            var result = $"Spawn grid: {state}, map={World.Size}/{World.Seed}/{Rust.Protocol.save}, candidates={spawnGridCache.Candidates.Count}, available={available}, processed={spawnGridProcessed}/{spawnGridTotal} ({spawnGridProcessed * 100f / Math.Max(1, spawnGridTotal):0.#}%), reserved/inUse={spawnGridReserved.Count}, temporary={spawnGridTemporaryUntil.Count}, layoutRejected={spawnGridLayoutRejections.Count}, maxSlice={spawnGridMaximumSliceMilliseconds:0.###}ms, lastSuccess={(spawnGridLastSuccessUnix > 0d ? FormatVector(spawnGridLastSuccessPosition) + " " + FormatDuration(Math.Max(0d, NowUnix() - spawnGridLastSuccessUnix)) + " ago" : "none")}.{warning}";
            if (!includeRejections)
                return result;
            var details = new List<string>();
            if (spawnGridRejectionCounts.Count > 0)
                details.Add("Rejections: " + string.Join(", ", spawnGridRejectionCounts.OrderByDescending(pair => pair.Value).Take(8).Select(pair => $"{pair.Key}={pair.Value}")) + ".");
            if (spawnGridLayoutStats.Count > 0)
                details.Add("Layouts: " + string.Join(", ", spawnGridLayoutStats.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value.Successes}/{pair.Value.Attempts} ({(pair.Value.Attempts > 0 ? pair.Value.Successes * 100d / pair.Value.Attempts : 0d):0.#}%) last={pair.Value.LastRejection ?? "none"}")) + ".");
            return details.Count == 0 ? result : result + " " + string.Join(" ", details);
        }

        private void RemoveExpiredSpawnGridTemporaryBlocks()
        {
            var now = NowUnix();
            foreach (var index in spawnGridTemporaryUntil.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList())
                spawnGridTemporaryUntil.Remove(index);
        }

        private void ReserveActiveSpawnGridCandidates()
        {
            if (spawnGridCache?.Candidates == null || data?.ActiveRaidBases == null)
                return;
            var maximumDistance = config.SpawnGrid.CellSize * 0.55f;
            var maximumDistanceSquared = maximumDistance * maximumDistance;
            var changed = false;
            foreach (var active in data.ActiveRaidBases.Values.Where(value => value != null && value.SpawnGridCandidateIndex >= 0 && !string.Equals(value.Status, "cleaning", StringComparison.OrdinalIgnoreCase)))
            {
                var activePosition = active.Position.ToVector3();
                var nearestIndex = -1;
                var nearestDistanceSquared = float.MaxValue;
                for (var index = 0; index < spawnGridCache.Candidates.Count; index++)
                {
                    var candidate = spawnGridCache.Candidates[index]?.ToVector3() ?? Vector3.zero;
                    var dx = candidate.x - activePosition.x;
                    var dz = candidate.z - activePosition.z;
                    var distanceSquared = dx * dx + dz * dz;
                    if (distanceSquared >= nearestDistanceSquared)
                        continue;
                    nearestDistanceSquared = distanceSquared;
                    nearestIndex = index;
                }
                if (nearestIndex >= 0 && nearestDistanceSquared <= maximumDistanceSquared)
                {
                    changed |= active.SpawnGridCandidateIndex != nearestIndex;
                    active.SpawnGridCandidateIndex = nearestIndex;
                    spawnGridReserved.Add(nearestIndex);
                }
            }
            if (changed)
                SaveData();
        }

        private void ScheduleAutoSpawn()
        {
            autoSpawnTimer?.Destroy();
            autoSpawnTimer = null;

            if (config?.EventTypes?.AutomaticBases?.Enabled != true)
                return;

            var delay = SecondsUntilNextAutoAttempt();
            autoSpawnTimer = timer.Once(delay, RunAutoSpawnTick);
        }

        private float SecondsUntilNextAutoAttempt()
        {
            var now = NowUnix();
            if (data.NextAutoAttemptUnix > now)
                return Mathf.Max(5f, (float)(data.NextAutoAttemptUnix - now));
            return 5f;
        }

        private void RunAutoSpawnTick()
        {
            var queued = 0;
            var eligible = false;
            try
            {
                if (CanAutoSpawn(out var reason))
                {
                    eligible = true;
                    var automaticBases = config.EventTypes.AutomaticBases;
                    var availableSlots = Math.Max(0, automaticBases.MaximumActiveBases - AutomaticPopulationReservedCount());
                    var spawnCount = Math.Min(automaticBases.MaximumSpawnsPerCheck, availableSlots);
                    queued = QueueAutomaticSpawnRequests(spawnCount, "population check");
                    Puts($"Automatic Bases queued {queued}/{spawnCount} background placement request(s); reserved population={AutomaticPopulationReservedCount()}/{automaticBases.MaximumActiveBases}.");
                }
                else
                {
                    Puts($"Automatic Bases check skipped: {reason}");
                }
            }
            finally
            {
                var now = NowUnix();
                data.NextAutoAttemptUnix = eligible && AutomaticPopulationReservedCount() < config.EventTypes.AutomaticBases.MinimumActiveBases
                    ? now + 5f
                    : now + config.EventTypes.AutomaticBases.CheckFrequencyMinutes * 60f;
                SaveData();
                ScheduleAutoSpawn();
            }
        }

        private bool CanAutoSpawn(out string reason)
        {
            reason = null;

            var automaticBases = config.EventTypes.AutomaticBases;
            if (!automaticBases.Enabled)
            {
                reason = "disabled";
                return false;
            }

            if (BasePlayer.activePlayerList.Count < automaticBases.MinimumOnlinePlayers)
            {
                reason = $"online players {BasePlayer.activePlayerList.Count}/{automaticBases.MinimumOnlinePlayers}";
                return false;
            }

            if (AutomaticPopulationReservedCount() >= automaticBases.MaximumActiveBases)
            {
                reason = $"target population reached ({AutomaticPopulationReservedCount()}/{automaticBases.MaximumActiveBases}, including queued searches)";
                return false;
            }

            LayoutScanEntry candidate;
            string selectionReason;
            if (!TrySelectWeightedAutomaticLayout(out candidate, out selectionReason))
            {
                reason = selectionReason;
                return false;
            }

            return true;
        }

        private int AutomaticPopulationReservedCount()
        {
            return AutomaticBaseActiveCount() + Math.Max(0, data?.PendingAutomaticSpawnRequests ?? 0);
        }

        private void ReconcileAutomaticSpawnQueue()
        {
            if (data == null || config?.EventTypes?.AutomaticBases == null)
                return;

            if (!config.EventTypes.AutomaticBases.Enabled)
            {
                data.PendingAutomaticSpawnRequests = 0;
                automaticLocationSearch = null;
                return;
            }

            var available = Math.Max(0, config.EventTypes.AutomaticBases.MaximumActiveBases - AutomaticBaseActiveCount());
            data.PendingAutomaticSpawnRequests = Mathf.Clamp(data.PendingAutomaticSpawnRequests, 0, available);
            if (data.PendingAutomaticSpawnRequests == 0)
                automaticLocationSearch = null;
        }

        private int QueueAutomaticSpawnRequests(int requested, string reason)
        {
            if (requested <= 0 || config?.EventTypes?.AutomaticBases?.Enabled != true)
                return 0;

            ReconcileAutomaticSpawnQueue();
            var available = Math.Max(0, config.EventTypes.AutomaticBases.MaximumActiveBases - AutomaticPopulationReservedCount());
            var added = Math.Min(requested, available);
            if (added <= 0)
                return 0;

            data.PendingAutomaticSpawnRequests += added;
            SaveData();
            ScheduleAutomaticLocationSearch();
            Puts($"Automatic Bases background placement queued {added} request(s) ({reason}); pending={data.PendingAutomaticSpawnRequests}.");
            RefreshOpenEventsManagerUis();
            return added;
        }

        private void CancelAutomaticLocationSearch(bool clearQueue)
        {
            automaticSearchTimer?.Destroy();
            automaticSearchTimer = null;
            if (automaticLocationSearch != null)
                spawnGridReserved.Remove(automaticLocationSearch.CandidateIndex);
            automaticLocationSearch = null;
            if (clearQueue && data != null)
                data.PendingAutomaticSpawnRequests = 0;
        }

        private void ScheduleAutomaticLocationSearch(float delay = -1f)
        {
            if (automaticSearchTimer != null || config?.EventTypes?.AutomaticBases?.Enabled != true || data?.PendingAutomaticSpawnRequests <= 0)
                return;

            var interval = delay >= 0f ? delay : 0.05f;
            automaticSearchTimer = timer.Once(Mathf.Max(0.05f, interval), RunAutomaticLocationSearchSlice);
        }

        private void RunAutomaticLocationSearchSlice()
        {
            automaticSearchTimer = null;
            if (config?.EventTypes?.AutomaticBases?.Enabled != true || data?.PendingAutomaticSpawnRequests <= 0)
            {
                CancelAutomaticLocationSearch(config?.EventTypes?.AutomaticBases?.Enabled != true);
                return;
            }

            ReconcileAutomaticSpawnQueue();
            if (data.PendingAutomaticSpawnRequests <= 0)
                return;

            if (CopyPaste == null || !CopyPaste.IsLoaded)
            {
                automaticSearchLastRejection = "CopyPaste is not loaded";
                MaybeLogAutomaticSearchProgress();
                ScheduleAutomaticLocationSearch(Mathf.Min(5f, config.LocationRules.SearchProgressLogIntervalSeconds));
                return;
            }

            if (automaticLocationSearch == null)
            {
                automaticLocationSearch = new AutomaticLocationSearch { StartedUnix = NowUnix() };
            }

            try
            {
                ProcessAutomaticLocationSearchSlice(automaticLocationSearch);
            }
            catch (Exception ex)
            {
                RejectAutomaticCandidate($"search exception: {ex.Message}");
            }

            if (data.PendingAutomaticSpawnRequests > 0 && config.EventTypes.AutomaticBases.Enabled)
                ScheduleAutomaticLocationSearch();
        }

        private void ProcessAutomaticLocationSearchSlice(AutomaticLocationSearch search)
        {
            if (search.Stage == AutomaticSearchStage.Candidate)
            {
                if (!PrepareAutomaticCandidate(search, out var reason))
                {
                    automaticSearchLastRejection = reason;
                    MaybeLogAutomaticSearchProgress();
                    var retryDelay = reason != null && (reason.StartsWith("candidate selection slice", StringComparison.OrdinalIgnoreCase)
                                                        || reason.StartsWith("candidate combination exhausted", StringComparison.OrdinalIgnoreCase))
                        ? 0.05f
                        : reason != null && reason.StartsWith("all enabled layout/rotation", StringComparison.OrdinalIgnoreCase)
                            ? config.SpawnGrid.TemporaryRejectionRetrySeconds
                            : Mathf.Min(5f, config.LocationRules.SearchProgressLogIntervalSeconds);
                    ScheduleAutomaticLocationSearch(retryDelay);
                    return;
                }
            }

            if (search.Stage == AutomaticSearchStage.Terrain)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var budgetTicks = Math.Max(1L, (long)(System.Diagnostics.Stopwatch.Frequency * config.SpawnGrid.BuildBudgetMilliseconds / 1000d));
                var checkedThisSlice = 0;
                while (search.SampleIndex < search.Samples.Count && checkedThisSlice++ < config.SpawnGrid.CandidateChecksPerSlice)
                {
                    var sample = search.Samples[search.SampleIndex++];
                    string terrainReason;
                    if (!ValidateAutomaticTerrainSample(search, sample, out terrainReason))
                    {
                        RejectAutomaticCandidate(terrainReason);
                        return;
                    }

                    if (System.Diagnostics.Stopwatch.GetTimestamp() - started >= budgetTicks)
                        return;
                }

                search.SampleIndex = 0;
                search.Stage = AutomaticSearchStage.SafeZone;
                return;
            }

            if (search.Stage == AutomaticSearchStage.SafeZone)
            {
                if (config.LocationRules.BlockSafeZones && AutomaticFootprintTouchesSafeZone(search, out var saturated))
                {
                    RejectAutomaticCandidate(saturated ? "safe-zone query buffer saturated" : "footprint safe zone");
                    return;
                }
                if (config.LocationRules.BlockNoBuildZones && AutomaticFootprintTouchesNoBuildZone(search, out saturated))
                {
                    RejectAutomaticCandidate(saturated ? "no-build query buffer saturated" : "footprint no-build zone");
                    return;
                }
                search.Stage = AutomaticSearchStage.PlayerBases;
                return;
            }

            if (search.Stage == AutomaticSearchStage.PlayerBases)
            {
                if (config.LocationRules.BlockPlayerBases && AutomaticFootprintTouchesPlayerBase(search, out var saturated))
                {
                    RejectAutomaticCandidate(saturated ? "player-base query buffer saturated" : "footprint near player base");
                    return;
                }
                if (AutomaticFootprintTouchesPlayer(search))
                {
                    RejectAutomaticCandidate("footprint near player or sleeper");
                    return;
                }
                search.SampleIndex = 0;
                search.Stage = AutomaticSearchStage.Obstacles;
                return;
            }

            if (search.Stage == AutomaticSearchStage.Obstacles)
            {
                string obstacleReason;
                if (ProcessAutomaticFootprintObstacleSlice(search, out obstacleReason))
                {
                    RejectAutomaticCandidate(obstacleReason);
                    return;
                }
                if (search.SampleIndex < (search.Layout?.GroundFootprintCells?.Count ?? 0))
                    return;
                search.Stage = AutomaticSearchStage.Paste;
                return;
            }

            if (search.Stage != AutomaticSearchStage.Paste)
                return;

            if (!IsAutomaticLayoutEligible(search.Layout))
            {
                RejectAutomaticCandidate("layout is no longer enabled or valid");
                return;
            }

            string message;
            if (!StartRaidBase(search.Layout.LayoutId, false, search.PasteOrigin, out message, "automatic", null, search.Layout, search.RotationDegrees, true, search.CandidateIndex))
            {
                RejectAutomaticCandidate($"paste start rejected: {message}");
                return;
            }

            data.PendingAutomaticSpawnRequests = Math.Max(0, data.PendingAutomaticSpawnRequests - 1);
            SaveData();
            RefreshOpenEventsManagerUis();
            Puts($"Automatic Bases background placement succeeded after {automaticSearchRejectedCandidates} rejected candidate(s): {message}");
            automaticLocationSearch = data.PendingAutomaticSpawnRequests > 0 ? new AutomaticLocationSearch { StartedUnix = NowUnix() } : null;
            automaticSearchRejectedCandidates = 0;
            automaticSearchLastRejection = null;
        }

        private bool ValidateAutomaticTerrainSample(AutomaticLocationSearch search, Vector3 sample,
            out string reason)
        {
            reason = null;
            if (IsNearMapEdge(sample))
            {
                reason = "footprint near map edge";
                return false;
            }

            var terrainHeight = TerrainHeight(sample);
            var supportDelta = terrainHeight - sample.y;
            search.MinimumTerrainHeight = Math.Min(search.MinimumTerrainHeight, supportDelta);
            search.MaximumTerrainHeight = Math.Max(search.MaximumTerrainHeight, supportDelta);

            if (AdaptiveFoundationTerrainEnabled())
            {
                if (search.MaximumTerrainHeight - search.MinimumTerrainHeight > MaximumAdaptiveTerrainVariance() + 0.001f)
                {
                    reason = "footprint exceeds adaptive terrain range";
                    return false;
                }

                var sourceClearance = -supportDelta;
                if (sourceClearance > MaximumAdaptiveSourceClearance() + 0.001f)
                {
                    reason = "footprint exceeds adaptive support depth";
                    return false;
                }

                if (supportDelta > config.Paste.AdaptiveFoundations.MaximumOriginAdjustmentMeters + 0.001f)
                {
                    reason = "footprint terrain would bury the base layer";
                    return false;
                }
            }
            else
            {
                if (search.MaximumTerrainHeight - search.MinimumTerrainHeight > config.LocationRules.MaxFlatnessDelta)
                {
                    reason = "footprint terrain height variance";
                    return false;
                }
                if (Math.Abs(supportDelta) > config.LocationRules.MaxFlatnessDelta)
                {
                    reason = "footprint ground support gap";
                    return false;
                }
                if (TerrainGrade(sample, 1.5f) > config.LocationRules.MaxSlope)
                {
                    reason = "footprint slope";
                    return false;
                }
            }

            float waterHeight;
            if (TryGetWaterSurfaceHeight(sample, terrainHeight, out waterHeight))
            {
                if (config.LocationRules.BlockWater && !AdaptiveWaterSupportEnabled())
                {
                    reason = "footprint water";
                    return false;
                }
                if (AdaptiveWaterSupportEnabled()
                    && waterHeight - terrainHeight
                    > config.Paste.AdaptiveFoundations.MaximumWaterDepthMeters + 0.001f)
                {
                    reason = "footprint water exceeds maximum depth";
                    return false;
                }
                if (AdaptiveWaterSupportEnabled()
                    && sample.y < waterHeight + config.Paste.AdaptiveFoundations.WaterSurfaceClearanceMeters - 0.001f)
                {
                    reason = "footprint base layer below water";
                    return false;
                }
            }

            string topologyReason;
            if (IsTopologyBlocked(sample, 0f, out topologyReason))
            {
                reason = "footprint " + topologyReason;
                return false;
            }

            return true;
        }

        private bool PrepareAutomaticCandidate(AutomaticLocationSearch search, out string reason)
        {
            reason = null;
            if (!spawnGridReady)
            {
                reason = spawnGridBuilding
                    ? $"spawn grid is building ({spawnGridProcessed}/{spawnGridTotal})"
                    : "spawn grid is not ready";
                return false;
            }
            if (spawnGridCache.Candidates.Count == 0)
            {
                reason = "spawn grid has no strict candidates";
                return false;
            }
            if (search.CombinationRetryNotBeforeUnix > NowUnix())
            {
                reason = $"all enabled layout/rotation combinations completed a strict pass; retrying in {FormatDuration(search.CombinationRetryNotBeforeUnix - NowUnix())}";
                return false;
            }
            search.CombinationRetryNotBeforeUnix = 0d;

            LayoutScanEntry layout = search.Layout;
            if (layout == null)
            {
                if (!TrySelectWeightedAutomaticLayout(out layout, out reason))
                    return false;
                search.Layout = layout;
                search.RotationDegrees = RandomRotationDegrees();
            }

            int candidateIndex;
            Vector3 ground;
            if (!TryReserveSpawnGridCandidate(layout, search.RotationDegrees, out candidateIndex, out ground, out reason, -1, search))
            {
                if (reason != null && reason.StartsWith("spawn grid exhausted", StringComparison.OrdinalIgnoreCase))
                    AdvanceAutomaticLayoutRotation(search, out reason);
                return false;
            }

            var rotationDegrees = search.RotationDegrees;
            Vector3 boundsMin;
            Vector3 boundsMax;
            GetGroundFootprintBounds(layout, out boundsMin, out boundsMax);
            var rotation = Quaternion.Euler(0f, rotationDegrees, 0f);
            var pasteOrigin = BuildPasteOriginFromGroundCells(layout, ground, rotationDegrees);
            var localCenter = new Vector3((boundsMin.x + boundsMax.x) * 0.5f, LayoutGroundAnchorY(layout) + 5f, (boundsMin.z + boundsMax.z) * 0.5f);
            var padding = config.LocationRules.FootprintClearancePadding;
            var halfExtents = new Vector3(
                Math.Max(1f, (boundsMax.x - boundsMin.x) * 0.5f + padding),
                5f,
                Math.Max(1f, (boundsMax.z - boundsMin.z) * 0.5f + padding));

            search.CandidateIndex = candidateIndex;
            search.PasteOrigin = pasteOrigin;
            search.Rotation = rotation;
            search.FootprintCenter = pasteOrigin + rotation * localCenter;
            search.FootprintHalfExtents = halfExtents;
            search.Samples = BuildAutomaticFootprintSamples(layout, pasteOrigin, rotation, 0f);
            search.SampleIndex = 0;
            search.MinimumTerrainHeight = float.MaxValue;
            search.MaximumTerrainHeight = float.MinValue;

            if (config.LocationRules.BlockMonuments && AutomaticFootprintTouchesMonument(search, out var monumentName))
            {
                RejectAutomaticCandidate($"footprint monument {monumentName}");
                reason = automaticSearchLastRejection;
                return false;
            }

            if (AutomaticFootprintNearActiveEvent(search))
            {
                RejectAutomaticCandidate("near active event");
                reason = automaticSearchLastRejection;
                return false;
            }

            search.Stage = AutomaticSearchStage.Terrain;
            return true;
        }

        private bool TryReserveSpawnGridCandidate(LayoutScanEntry layout, float rotationDegrees, out int candidateIndex, out Vector3 ground, out string reason, int maximumCandidateScans = -1, AutomaticLocationSearch selectionState = null)
        {
            candidateIndex = -1;
            ground = Vector3.zero;
            reason = null;
            if (!spawnGridReady || spawnGridCache?.Candidates == null || spawnGridCache.Candidates.Count == 0)
            {
                reason = spawnGridBuilding ? "spawn grid is still building" : "spawn grid has no candidates";
                return false;
            }

            RemoveExpiredSpawnGridTemporaryBlocks();
            var count = spawnGridCache.Candidates.Count;
            if (selectionState != null && selectionState.CandidateScanStart < 0)
            {
                selectionState.CandidateScanStart = UnityEngine.Random.Range(0, count);
                selectionState.CandidateScanVisited = 0;
            }
            var start = selectionState == null
                ? UnityEngine.Random.Range(0, count)
                : (selectionState.CandidateScanStart + selectionState.CandidateScanVisited) % count;
            var remaining = selectionState == null ? count : count - selectionState.CandidateScanVisited;
            var scanLimit = Math.Min(remaining, maximumCandidateScans > 0 ? maximumCandidateScans : config.SpawnGrid.CandidateChecksPerSlice);
            var inspected = 0;
            for (var offset = 0; offset < scanLimit; offset++)
            {
                var index = (start + offset) % count;
                inspected++;
                if (spawnGridReserved.Contains(index) || spawnGridTemporaryUntil.ContainsKey(index))
                    continue;
                if (spawnGridLayoutRejections.Contains(SpawnGridLayoutCandidateKey(layout, rotationDegrees, index)))
                    continue;
                var position = spawnGridCache.Candidates[index]?.ToVector3() ?? Vector3.zero;
                if (position == Vector3.zero || IsNearActiveEventCenter(position))
                    continue;

                spawnGridReserved.Add(index);
                if (selectionState != null)
                    selectionState.CandidateScanVisited += inspected;
                candidateIndex = index;
                ground = position;
                LayoutPlacementStats stats;
                if (!spawnGridLayoutStats.TryGetValue(layout.LayoutId, out stats))
                    spawnGridLayoutStats[layout.LayoutId] = stats = new LayoutPlacementStats();
                stats.Attempts++;
                return true;
            }

            if (selectionState != null)
                selectionState.CandidateScanVisited += inspected;
            var exhausted = selectionState != null
                ? selectionState.CandidateScanVisited >= count
                : scanLimit >= count;
            reason = exhausted
                ? $"spawn grid exhausted for {layout.LayoutId} at {rotationDegrees:0.#} degrees; request will remain queued"
                : $"candidate selection slice found no available point for {layout.LayoutId} at {rotationDegrees:0.#} degrees";
            CountSpawnGridRejection(exhausted ? "runtime:pool exhausted" : "runtime:selection slice unavailable");
            if (exhausted && selectionState != null)
            {
                selectionState.CandidateScanStart = -1;
                selectionState.CandidateScanVisited = 0;
            }
            return false;
        }

        private string SpawnGridLayoutCandidateKey(LayoutScanEntry layout, float rotationDegrees, int candidateIndex)
        {
            return $"{layout?.LayoutId ?? "unknown"}|{Mathf.RoundToInt(Mathf.Repeat(rotationDegrees, 360f))}|{candidateIndex}";
        }

        private void AdvanceAutomaticLayoutRotation(AutomaticLocationSearch search, out string reason)
        {
            var exhaustedLayoutId = search.Layout?.LayoutId ?? "unknown";
            var exhaustedRotation = search.RotationDegrees;
            search.ExhaustedLayoutRotations.Add(AutomaticLayoutRotationKey(exhaustedLayoutId, exhaustedRotation));
            search.CandidateScanStart = -1;
            search.CandidateScanVisited = 0;
            search.CandidateRejectionsForCombination = 0;

            var combinations = new List<Tuple<LayoutScanEntry, float, float>>();
            foreach (var configured in config.EventTypes.AutomaticBases.Layouts.Where(value => value != null && value.Enabled && value.Weight > 0f))
            {
                LayoutScanEntry layout;
                if (!data.Layouts.TryGetValue(configured.LayoutId, out layout) || !IsAutomaticLayoutEligible(layout))
                    continue;
                foreach (var rotation in AutomaticRotationOptions())
                {
                    if (!search.ExhaustedLayoutRotations.Contains(AutomaticLayoutRotationKey(layout.LayoutId, rotation)))
                        combinations.Add(Tuple.Create(layout, rotation, configured.Weight));
                }
            }

            var otherLayouts = combinations.Where(value => !string.Equals(value.Item1.LayoutId, exhaustedLayoutId, StringComparison.OrdinalIgnoreCase)).ToList();
            var candidates = otherLayouts.Count > 0 ? otherLayouts : combinations;
            if (candidates.Count == 0)
            {
                search.ExhaustedLayoutRotations.Clear();
                search.CombinationRetryNotBeforeUnix = NowUnix() + config.SpawnGrid.TemporaryRejectionRetrySeconds;
                reason = $"all enabled layout/rotation combinations exhausted strict grid candidates; retrying after {config.SpawnGrid.TemporaryRejectionRetrySeconds:0} seconds";
                CountSpawnGridRejection("runtime:all layout rotations exhausted");
                return;
            }

            var totalWeight = candidates.Sum(value => Math.Max(0.01f, value.Item3));
            var roll = UnityEngine.Random.Range(0f, totalWeight);
            var selected = candidates[candidates.Count - 1];
            foreach (var candidate in candidates)
            {
                roll -= Math.Max(0.01f, candidate.Item3);
                if (roll > 0f)
                    continue;
                selected = candidate;
                break;
            }

            search.Layout = selected.Item1;
            search.RotationDegrees = selected.Item2;
            reason = $"candidate combination exhausted for {exhaustedLayoutId} at {exhaustedRotation:0.#} degrees; switching to {search.Layout.LayoutId} at {search.RotationDegrees:0.#} degrees";
            CountSpawnGridRejection("runtime:layout rotation advanced");
        }

        private string AutomaticLayoutRotationKey(string layoutId, float rotationDegrees)
        {
            return $"{layoutId ?? "unknown"}|{Mathf.RoundToInt(Mathf.Repeat(rotationDegrees, 360f))}";
        }

        private List<float> AutomaticRotationOptions()
        {
            var step = config.Paste.RandomRotationDegreesStep;
            if (step <= 0f)
                return new List<float> { 0f, 90f, 180f, 270f };
            var count = Mathf.Clamp(Mathf.RoundToInt(360f / step), 1, 72);
            return Enumerable.Range(0, count).Select(index => Mathf.Repeat(index * step, 360f)).Distinct().ToList();
        }

        private bool IsNearActiveEventCenter(Vector3 position)
        {
            var minimumDistance = config.LocationRules.MinimumDistanceBetweenEvents;
            if (minimumDistance <= 0f)
                return false;
            foreach (var active in data.ActiveRaidBases.Values)
            {
                if (active == null || string.Equals(active.Status, "cleaning", StringComparison.OrdinalIgnoreCase))
                    continue;
                var activeCenter = EventCenter(active);
                var dx = activeCenter.x - position.x;
                var dz = activeCenter.z - position.z;
                if (dx * dx + dz * dz < minimumDistance * minimumDistance)
                    return true;
            }
            return false;
        }

        private bool IsAutomaticLayoutEligible(LayoutScanEntry layout)
        {
            return layout != null
                   && layout.Valid
                   && !layout.Ignored
                   && config.EventTypes.AutomaticBases.Layouts.Any(entry => entry != null
                       && entry.Enabled
                       && entry.Weight > 0f
                       && string.Equals(entry.LayoutId, layout.LayoutId, StringComparison.OrdinalIgnoreCase));
        }

        private List<Vector3> BuildAutomaticFootprintSamples(LayoutScanEntry layout, Vector3 pasteOrigin, Quaternion rotation, float padding)
        {
            var cells = layout?.GroundFootprintCells ?? new List<GroundFootprintCell>();
            if (cells.Count == 0)
                return new List<Vector3>();

            var foundationCells = cells.Where(value => value != null && value.Position != null && value.IsFoundation).ToList();
            var terrainCells = foundationCells.Count > 0
                ? foundationCells
                : cells.Where(value => value != null && value.Position != null).ToList();

            var samples = new List<Vector3>();
            foreach (var cell in terrainCells)
            {
                samples.AddRange(BuildGroundCellWorldSamples(cell, pasteOrigin, rotation, padding));
            }

            return samples
                .GroupBy(position => $"{Mathf.RoundToInt(position.x * 10f)}:{Mathf.RoundToInt(position.z * 10f)}")
                .Select(group => group.First())
                .ToList();
        }

        private List<Vector3> BuildGroundCellWorldSamples(GroundFootprintCell cell, Vector3 pasteOrigin,
            Quaternion rotation, float padding)
        {
            var samples = new List<Vector3>();
            if (cell?.Position == null)
                return samples;

            var center = new Vector3(cell.Position.X, cell.Position.Y, cell.Position.Z);
            var yaw = Quaternion.Euler(0f, cell.RotationDegrees, 0f);
            var halfWidth = Mathf.Max(0.25f, cell.HalfWidth + padding);
            var halfDepth = Mathf.Max(0.25f, cell.HalfDepth + padding);
            samples.Add(pasteOrigin + rotation * center);
            samples.Add(pasteOrigin + rotation * (center + yaw * new Vector3(halfWidth, 0f, halfDepth)));
            samples.Add(pasteOrigin + rotation * (center + yaw * new Vector3(halfWidth, 0f, -halfDepth)));
            samples.Add(pasteOrigin + rotation * (center + yaw * new Vector3(-halfWidth, 0f, halfDepth)));
            samples.Add(pasteOrigin + rotation * (center + yaw * new Vector3(-halfWidth, 0f, -halfDepth)));
            return samples;
        }

        private float LayoutGroundAnchorY(LayoutScanEntry layout)
        {
            if (layout?.GroundFootprintCells != null && layout.GroundFootprintCells.Count > 0)
                return layout.GroundAnchorY;
            return layout?.BoundsMin?.Y ?? 0f;
        }

        private void GetGroundFootprintBounds(LayoutScanEntry layout, out Vector3 min, out Vector3 max)
        {
            var cells = layout?.GroundFootprintCells?.Where(cell => cell != null && cell.Position != null).ToList();
            if (cells == null || cells.Count == 0)
            {
                min = layout?.BoundsMin?.ToVector3() ?? Vector3.zero;
                max = layout?.BoundsMax?.ToVector3() ?? Vector3.zero;
                return;
            }

            min = new Vector3(float.MaxValue, LayoutGroundAnchorY(layout), float.MaxValue);
            max = new Vector3(float.MinValue, LayoutGroundAnchorY(layout), float.MinValue);
            foreach (var cell in cells)
            {
                var radius = Mathf.Max(0.5f, cell.Radius);
                min.x = Math.Min(min.x, cell.Position.X - radius);
                min.z = Math.Min(min.z, cell.Position.Z - radius);
                max.x = Math.Max(max.x, cell.Position.X + radius);
                max.z = Math.Max(max.z, cell.Position.Z + radius);
            }
        }

        private bool IsTerrainSampleUnderWater(Vector3 sample, float terrainHeight)
        {
            float waterHeight;
            return TryGetWaterSurfaceHeight(sample, terrainHeight, out waterHeight);
        }

        private bool TryGetWaterSurfaceHeight(Vector3 sample, float terrainHeight, out float waterHeight)
        {
            waterHeight = terrainHeight;
            if (TerrainMeta.WaterMap == null)
                return false;

            // WaterMap alone can report a negative mapped height below the actual ocean plane.
            // WaterLevel applies Rust's topology-aware OceanLevel correction and also supports
            // non-terrain water volumes, so placement and depth checks use the real surface.
            waterHeight = WaterLevel.GetWaterSurface(sample, false, true, null);
            if (float.IsNaN(waterHeight) || float.IsInfinity(waterHeight))
            {
                waterHeight = terrainHeight;
                return false;
            }
            return waterHeight > terrainHeight + 0.05f;
        }

        private bool AdaptiveFoundationTerrainEnabled()
        {
            return config?.Paste?.AdaptiveFoundations?.Enabled == true;
        }

        private bool AdaptiveWaterSupportEnabled()
        {
            return AdaptiveFoundationTerrainEnabled()
                   && config.Paste.AdaptiveFoundations.RaiseBaseLayerAboveWater;
        }

        private float MaximumAdaptiveSourceClearance()
        {
            var adaptive = config?.Paste?.AdaptiveFoundations;
            return adaptive == null
                ? config.LocationRules.MaxFlatnessDelta
                : adaptive.MaximumLoweringMeters + adaptive.MaximumFoundationClearanceMeters;
        }

        private float MaximumAdaptiveTerrainVariance()
        {
            var adaptive = config?.Paste?.AdaptiveFoundations;
            return adaptive == null
                ? config.LocationRules.MaxFlatnessDelta
                : adaptive.MaximumLoweringMeters
                  + adaptive.MaximumFoundationClearanceMeters
                  + adaptive.MaximumFoundationEmbedMeters;
        }

        private bool AutomaticFootprintTouchesMonument(AutomaticLocationSearch search, out string monumentName)
        {
            monumentName = null;
            EnsureMonumentZones();
            var inverse = Quaternion.Inverse(search.Rotation);
            foreach (var zone in monumentZones)
            {
                var local = inverse * (zone.Center - search.FootprintCenter);
                var dx = Math.Max(Math.Abs(local.x) - search.FootprintHalfExtents.x, 0f);
                var dz = Math.Max(Math.Abs(local.z) - search.FootprintHalfExtents.z, 0f);
                if (dx * dx + dz * dz <= zone.Radius * zone.Radius)
                {
                    monumentName = zone.Name;
                    return true;
                }
            }
            return false;
        }

        private bool AutomaticFootprintNearActiveEvent(AutomaticLocationSearch search)
        {
            return search != null && IsNearActiveEventCenter(search.FootprintCenter);
        }

        private bool AutomaticFootprintTouchesSafeZone(AutomaticLocationSearch search, out bool saturated)
        {
            saturated = false;
            if (search == null)
                return false;

            var safeZones = TriggerSafeZone.allSafeZones;
            if (safeZones == null)
                return false;

            var inverse = Quaternion.Inverse(search.Rotation);
            foreach (var safeZone in safeZones)
            {
                if (safeZone == null)
                    continue;

                var collider = safeZone.triggerCollider;
                var center = collider == null ? safeZone.transform.position : collider.bounds.center;
                var radius = collider == null ? 200f : Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.z);
                var local = inverse * (center - search.FootprintCenter);
                var dx = Math.Max(Math.Abs(local.x) - search.FootprintHalfExtents.x, 0f);
                var dz = Math.Max(Math.Abs(local.z) - search.FootprintHalfExtents.z, 0f);
                if (dx * dx + dz * dz <= radius * radius)
                    return true;
            }
            return false;
        }

        private bool AutomaticFootprintTouchesNoBuildZone(AutomaticLocationSearch search, out bool saturated)
        {
            saturated = false;
            if (search == null || PreventBuildingLayer == 0)
                return false;

            var halfExtents = search.FootprintHalfExtents;
            halfExtents.y = Mathf.Max(50f, halfExtents.y);
            var count = Physics.OverlapBoxNonAlloc(search.FootprintCenter, halfExtents, automaticSearchColliders, search.Rotation, PreventBuildingLayer, QueryTriggerInteraction.Collide);
            saturated = count >= automaticSearchColliders.Length;
            return count > 0;
        }

        private bool AutomaticFootprintTouchesPlayerBase(AutomaticLocationSearch search, out bool saturated)
        {
            var expanded = search.FootprintHalfExtents + new Vector3(config.LocationRules.PlayerBaseRadius, 5f, config.LocationRules.PlayerBaseRadius);
            var count = Physics.OverlapBoxNonAlloc(search.FootprintCenter, expanded, automaticSearchColliders, search.Rotation, PlayerBaseLayer, QueryTriggerInteraction.Collide);
            saturated = count >= automaticSearchColliders.Length;
            for (var index = 0; index < count && index < automaticSearchColliders.Length; index++)
            {
                var collider = automaticSearchColliders[index];
                var entity = collider == null ? null : collider.GetComponentInParent<BaseEntity>();
                if (IsPlayerBaseEntity(entity))
                    return true;
            }
            return saturated;
        }

        private bool AutomaticFootprintTouchesPlayer(AutomaticLocationSearch search)
        {
            var radius = Mathf.Sqrt(search.FootprintHalfExtents.x * search.FootprintHalfExtents.x + search.FootprintHalfExtents.z * search.FootprintHalfExtents.z) + 15f;
            foreach (var player in BasePlayer.activePlayerList.Concat(BasePlayer.sleepingPlayerList))
            {
                if (player == null || player.IsDestroyed)
                    continue;
                var delta = player.transform.position - search.FootprintCenter;
                delta.y = 0f;
                if (delta.sqrMagnitude <= radius * radius)
                    return true;
            }
            return false;
        }

        private bool ProcessAutomaticFootprintObstacleSlice(AutomaticLocationSearch search, out string reason)
        {
            reason = null;
            var cells = search.Layout?.GroundFootprintCells?.Where(value => value != null && value.Position != null).ToList() ?? new List<GroundFootprintCell>();
            if (AutomaticObstacleLayer == 0)
            {
                search.SampleIndex = cells.Count;
                return false;
            }

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var budgetTicks = Math.Max(1L, (long)(System.Diagnostics.Stopwatch.Frequency * config.SpawnGrid.BuildBudgetMilliseconds / 1000d));
            var checkedThisSlice = 0;
            while (search.SampleIndex < cells.Count && checkedThisSlice++ < config.SpawnGrid.CandidateChecksPerSlice)
            {
                var cell = cells[search.SampleIndex++];
                var sample = search.PasteOrigin + search.Rotation * new Vector3(cell.Position.X, 0f, cell.Position.Z);
                sample.y = TerrainHeight(sample) + 1.5f;
                var count = Physics.OverlapSphereNonAlloc(sample, Mathf.Max(0.75f, cell.Radius), automaticSearchColliders, AutomaticObstacleLayer, QueryTriggerInteraction.Ignore);
                if (count >= automaticSearchColliders.Length)
                {
                    reason = "world obstacle query buffer saturated";
                    return true;
                }

                for (var index = 0; index < count; index++)
                {
                    var collider = automaticSearchColliders[index];
                    if (collider == null)
                        continue;
                    var entity = collider.GetComponentInParent<BaseEntity>();
                    if (entity == null)
                    {
                        if (IsSignificantWorldCollider(collider))
                        {
                            reason = "footprint overlaps substantial world obstacle";
                            return true;
                        }
                        continue;
                    }

                    var entityId = entity.net == null ? 0UL : entity.net.ID.Value;
                    if (entityToInstance.ContainsKey(entityId))
                        continue;
                    var descriptor = $"{entity.GetType().Name} {entity.ShortPrefabName} {entity.PrefabName}".ToLowerInvariant();
                    if (descriptor.Contains("vehicle") || descriptor.Contains("minicopter") || descriptor.Contains("scraptransport") || descriptor.Contains("modularcar") || descriptor.Contains("horse"))
                    {
                        reason = "footprint near vehicle";
                        return true;
                    }
                    if (entity.OwnerID != 0 || entity is BuildingPrivlidge || entity is BuildingBlock || entity is Door)
                    {
                        reason = "footprint overlaps player-owned construction or deployable";
                        return true;
                    }
                    if (IsSignificantWorldCollider(collider))
                    {
                        reason = "footprint overlaps permanent world structure";
                        return true;
                    }
                }

                if (System.Diagnostics.Stopwatch.GetTimestamp() - started >= budgetTicks)
                    break;
            }
            return false;
        }

        private bool ValidateDynamicRuntimeBlockers(AutomaticLocationSearch search, out string reason)
        {
            reason = null;
            bool saturated;
            if (config.LocationRules.BlockSafeZones && AutomaticFootprintTouchesSafeZone(search, out saturated))
            {
                reason = "footprint safe zone";
                return false;
            }
            if (config.LocationRules.BlockNoBuildZones && AutomaticFootprintTouchesNoBuildZone(search, out saturated))
            {
                reason = saturated ? "no-build query buffer saturated" : "footprint no-build zone";
                return false;
            }
            if (config.LocationRules.BlockPlayerBases && AutomaticFootprintTouchesPlayerBase(search, out saturated))
            {
                reason = saturated ? "player-base query buffer saturated" : "footprint near player base";
                return false;
            }
            if (AutomaticFootprintTouchesPlayer(search)) { reason = "footprint near player or sleeper"; return false; }
            if (AutomaticFootprintNearActiveEvent(search)) { reason = "near active event"; return false; }
            if (FootprintOverlapsActiveRaid(search.Layout, search.PasteOrigin, search.RotationDegrees)) { reason = "footprint overlaps active raid base"; return false; }
            search.SampleIndex = 0;
            var count = search.Layout?.GroundFootprintCells?.Count ?? 0;
            while (search.SampleIndex < count)
            {
                if (ProcessAutomaticFootprintObstacleSlice(search, out reason))
                    return false;
            }
            return true;
        }

        private void RejectAutomaticCandidate(string reason)
        {
            automaticSearchRejectedCandidates++;
            automaticSearchLastRejection = string.IsNullOrWhiteSpace(reason) ? "unknown rejection" : reason;
            CountSpawnGridRejection("runtime:" + automaticSearchLastRejection);
            if (automaticLocationSearch != null)
            {
                var candidateIndex = automaticLocationSearch.CandidateIndex;
                if (candidateIndex >= 0)
                {
                    spawnGridReserved.Remove(candidateIndex);
                    if (IsTemporarySpawnGridRejection(automaticSearchLastRejection))
                        spawnGridTemporaryUntil[candidateIndex] = NowUnix() + config.SpawnGrid.TemporaryRejectionRetrySeconds;
                    else if (automaticLocationSearch.Layout != null)
                        spawnGridLayoutRejections.Add(SpawnGridLayoutCandidateKey(automaticLocationSearch.Layout, automaticLocationSearch.RotationDegrees, candidateIndex));
                }
                LayoutPlacementStats stats;
                if (automaticLocationSearch.Layout != null && spawnGridLayoutStats.TryGetValue(automaticLocationSearch.Layout.LayoutId, out stats))
                    stats.LastRejection = automaticSearchLastRejection;
                automaticLocationSearch.CandidateRejectionsForCombination++;
                var rejectionLimit = Math.Max(25, config.SpawnGrid.CandidateChecksPerSlice * 2);
                if (automaticLocationSearch.CandidateRejectionsForCombination >= rejectionLimit)
                {
                    var candidateRejection = automaticSearchLastRejection;
                    string switchReason;
                    AdvanceAutomaticLayoutRotation(automaticLocationSearch, out switchReason);
                    automaticSearchLastRejection = candidateRejection + "; " + switchReason;
                }
                automaticLocationSearch.Samples.Clear();
                automaticLocationSearch.SampleIndex = 0;
                automaticLocationSearch.CandidateIndex = -1;
                automaticLocationSearch.Stage = AutomaticSearchStage.Candidate;
            }
            MaybeLogAutomaticSearchProgress();
        }

        private bool IsTemporarySpawnGridRejection(string reason)
        {
            var value = (reason ?? string.Empty).ToLowerInvariant();
            return value.Contains("player") || value.Contains("active event") || value.Contains("active raid") || value.Contains("vehicle")
                   || value.Contains("buffer saturated") || value.Contains("paste") || value.Contains("exception");
        }

        private void TemporarilyBlockSpawnGridCandidate(int candidateIndex, string reason)
        {
            if (candidateIndex < 0)
                return;

            spawnGridReserved.Remove(candidateIndex);
            spawnGridTemporaryUntil[candidateIndex] = NowUnix() + config.SpawnGrid.TemporaryRejectionRetrySeconds;
            CountSpawnGridRejection("runtime:" + (string.IsNullOrWhiteSpace(reason) ? "temporary block" : reason));
        }

        private LayoutPlacementStats GetLayoutPlacementStats(string layoutId)
        {
            var key = string.IsNullOrWhiteSpace(layoutId) ? "unknown" : layoutId;
            LayoutPlacementStats stats;
            if (!spawnGridLayoutStats.TryGetValue(key, out stats))
                spawnGridLayoutStats[key] = stats = new LayoutPlacementStats();
            return stats;
        }

        private void MaybeLogAutomaticSearchProgress()
        {
            var now = NowUnix();
            if (now - lastAutomaticSearchLogUnix < config.LocationRules.SearchProgressLogIntervalSeconds)
                return;
            lastAutomaticSearchLogUnix = now;
            Puts($"Automatic Bases location search: pending={data.PendingAutomaticSpawnRequests}, rejected={automaticSearchRejectedCandidates}, elapsed={FormatDuration(Math.Max(0d, now - (automaticLocationSearch?.StartedUnix ?? now)))}, layout={automaticLocationSearch?.Layout?.LayoutId ?? "selecting"}, last={automaticSearchLastRejection ?? "none"}.");
        }

        private string AutomaticSearchStatusShort()
        {
            var grid = !config.SpawnGrid.Enabled ? "grid off" : spawnGridBuilding ? $"grid {spawnGridProcessed * 100f / Math.Max(1, spawnGridTotal):0}%" : spawnGridReady ? $"grid {spawnGridCache.Candidates.Count}" : "grid waiting";
            if (data.PendingAutomaticSpawnRequests <= 0)
                return $"Search idle | {grid}; hidden/public state is fixed when a base spawns.";
            var elapsed = Math.Max(0d, NowUnix() - (automaticLocationSearch?.StartedUnix ?? NowUnix()));
            return $"Search queued {data.PendingAutomaticSpawnRequests} | {grid} | {automaticLocationSearch?.Layout?.LayoutId ?? "selecting"} | rejected {automaticSearchRejectedCandidates} | {FormatDuration(elapsed)} | {automaticSearchLastRejection ?? "scanning"}";
        }

        private int CleanupAll(string reason)
        {
            var count = 0;
            foreach (var instanceId in data.ActiveRaidBases.Keys.ToList())
            {
                CleanupInstance(instanceId, reason);
                count++;
            }

            return count;
        }

        private void CleanupInstance(string instanceId, string reason, bool preserveRaidCompletionLoot = false)
        {
            ActiveRaidBase active;
            if (!data.ActiveRaidBases.TryGetValue(instanceId, out active))
                return;

            var wasAutomatic = IsActiveAutomaticBase(active);

            if (!active.ResultCommitted)
                CommitTerminalResult(active, TerminalStateFromReason(reason), reason, false);

            active.Status = "cleaning";

            if (config.Cleanup.RemoveMarkers)
                DestroyMarker(instanceId);

            var releasedLoot = 0;
            if (config.Cleanup.DespawnPastedEntities && preserveRaidCompletionLoot)
                releasedLoot = ReleaseRaidCompletionLoot(active.EntityIds);

            if (config.Cleanup.DespawnPastedEntities)
                DespawnEntities(active.EntityIds);

            foreach (var entityId in active.EntityIds ?? new List<ulong>())
                entityToInstance.Remove(entityId);

            pendingPasteInstances.Remove(instanceId);
            spawnGridReserved.Remove(active.SpawnGridCandidateIndex);
            data.ActiveRaidBases.Remove(instanceId);
            SaveData();
            RefreshOpenEventsManagerUis();
            Puts($"Cleaned raid base event {instanceId}: {reason}; nativeLootStacksReleased={releasedLoot}.");

            if (wasAutomatic && !string.Equals(reason, "plugin unload", StringComparison.OrdinalIgnoreCase) &&
                config.EventTypes.AutomaticBases.Enabled && AutomaticBaseActiveCount() < config.EventTypes.AutomaticBases.MinimumActiveBases)
            {
                data.NextAutoAttemptUnix = Math.Min(data.NextAutoAttemptUnix, NowUnix() + 5f);
                SaveData();
                ScheduleAutoSpawn();
                Puts($"Automatic Bases dropped below its minimum ({AutomaticBaseActiveCount()}/{config.EventTypes.AutomaticBases.MinimumActiveBases}); queued an urgent population check.");
            }
        }

        private int ReleaseRaidCompletionLoot(List<ulong> entityIds)
        {
            if (entityIds == null)
                return 0;

            var releasedStacks = 0;
            foreach (var entityId in entityIds.Distinct().ToList())
            {
                var container = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as StorageContainer;
                if (container == null || container.IsDestroyed || container.inventory?.itemList == null
                    || container.inventory.itemList.Count == 0)
                    continue;

                var itemCount = container.inventory.itemList.Count;
                try
                {
                    container.DropItems();
                    releasedStacks += itemCount;
                }
                catch (Exception exception)
                {
                    var remainingAfterNative = container.inventory?.itemList?.Count ?? 0;
                    var nativeReleased = Math.Max(0, itemCount - remainingAfterNative);
                    var fallbackReleased = DropRaidCompletionItemsIndividually(container);
                    var totalReleased = nativeReleased + fallbackReleased;
                    releasedStacks += totalReleased;
                    if (totalReleased < itemCount)
                    {
                        PrintWarning($"Could not release all raid-completion loot from event container {entityId}: native={exception.GetType().Name}: {exception.Message}; released={totalReleased}/{itemCount} stack(s).");
                    }
                }
            }

            return releasedStacks;
        }

        private int DropRaidCompletionItemsIndividually(StorageContainer container)
        {
            if (container == null || container.IsDestroyed || container.inventory?.itemList == null)
                return 0;

            var released = 0;
            var position = container.transform.position + Vector3.up * 0.5f;
            var velocity = Vector3.up * 0.15f;
            foreach (var item in container.inventory.itemList.Where(candidate => candidate != null).ToList())
            {
                try
                {
                    item.Drop(position, velocity);
                    released++;
                }
                catch
                {
                    // Cleanup will remove the container; leave a failed item in it
                    // rather than throwing from the objective-completion path.
                }
            }

            return released;
        }

        private void DespawnEntities(List<ulong> entityIds)
        {
            if (entityIds == null)
                return;

            foreach (var entityId in entityIds.ToList())
            {
                var entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as BaseEntity;
                if (entity == null || entity.IsDestroyed)
                    continue;

                try
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }
                catch (Exception exception)
                {
                    PrintWarning($"Could not despawn event entity {entityId}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        private void RestoreMarkers()
        {
            foreach (var active in data.ActiveRaidBases.Values.ToList())
                CreateOrUpdateMarker(active);
        }

        private void CreateOrUpdateMarker(ActiveRaidBase active)
        {
            if (active == null || !active.IsAnnounced || config.MapMarker.Enabled != true || active.Status != "active")
                return;

            MapMarkerGenericRadius marker;
            if (!markers.TryGetValue(active.InstanceId, out marker) || marker == null || marker.IsDestroyed)
            {
                var entity = GameManager.server.CreateEntity(GenericRadiusMapMarkerPrefab, active.Position.ToVector3(), Quaternion.identity, true);
                marker = entity as MapMarkerGenericRadius;
                if (marker == null)
                {
                    if (entity != null && !entity.IsDestroyed)
                        entity.Kill(BaseNetworkable.DestroyMode.None);

                    PrintWarning($"Could not create map marker from '{GenericRadiusMapMarkerPrefab}'.");
                    return;
                }

                marker.enableSaving = false;
                marker.globalBroadcast = true;
                marker.Spawn();
                markers[active.InstanceId] = marker;
            }

            var color1 = ParseColor(config.MapMarker.Color1, new Color(0.81f, 0.26f, 0.17f, 1f));
            var color2 = ParseColor(config.MapMarker.Color2, new Color(0.96f, 0.77f, 0.26f, 1f));
            color1.a = config.MapMarker.Alpha;
            color2.a = config.MapMarker.Alpha;

            marker.transform.position = EventCenter(active);
            marker.radius = NativeMarkerRadius();
            marker.alpha = config.MapMarker.Alpha;
            marker.color1 = color1;
            marker.color2 = color2;
            marker.SendUpdate();
            marker.SendNetworkUpdateImmediate();
        }

        private void SyncMarkersToPlayer(BasePlayer player)
        {
            if (player == null || !player.IsConnected || player.Connection == null || config?.MapMarker?.Enabled != true)
                return;

            foreach (var active in data.ActiveRaidBases.Values.Where(value => value != null && value.IsAnnounced && value.Status == "active").ToList())
            {
                MapMarkerGenericRadius marker;
                if (!markers.TryGetValue(active.InstanceId, out marker) || marker == null || marker.IsDestroyed)
                {
                    CreateOrUpdateMarker(active);
                    if (!markers.TryGetValue(active.InstanceId, out marker) || marker == null || marker.IsDestroyed)
                        continue;
                }

                var color1 = new Vector3(marker.color1.r, marker.color1.g, marker.color1.b);
                var color2 = new Vector3(marker.color2.r, marker.color2.g, marker.color2.b);
                marker.ClientRPC(RpcTarget.Player("MarkerUpdate", player.Connection), color1, marker.color1.a, color2, marker.alpha, marker.radius);
            }
        }

        private float NativeMarkerRadius()
        {
            return Mathf.Clamp(NativeMarkerBaseRadius + config.MapMarker.RadiusMeters * NativeMarkerRadiusPerMeter, 0.03f, 0.35f);
        }

        private void DestroyMarker(string instanceId)
        {
            MapMarkerGenericRadius marker;
            if (!markers.TryGetValue(instanceId, out marker))
                return;

            markers.Remove(instanceId);
            if (marker == null || marker.IsDestroyed)
                return;

            try
            {
                marker.Kill(BaseNetworkable.DestroyMode.None);
            }
            catch (Exception exception)
            {
                PrintWarning($"Could not remove map marker for {instanceId}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private void DestroyAllMarkers()
        {
            foreach (var instanceId in markers.Keys.ToList())
                DestroyMarker(instanceId);
        }

        private bool TrySelectLayout(string requestedLayoutId, out LayoutScanEntry layout, out string reason)
        {
            layout = null;
            reason = null;

            var enabledLayouts = EnabledValidLayouts();
            if (requestedLayoutId.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                if (enabledLayouts.Count == 0)
                {
                    reason = "No enabled valid layouts. Run revents.layouts scan, then revents.layouts enable <layoutId>.";
                    return false;
                }

                layout = enabledLayouts[UnityEngine.Random.Range(0, enabledLayouts.Count)];
                return true;
            }

            if (!data.Layouts.TryGetValue(requestedLayoutId, out layout))
            {
                ScanLayouts(true);
                data.Layouts.TryGetValue(requestedLayoutId, out layout);
            }

            if (layout == null)
            {
                reason = $"Layout '{requestedLayoutId}' was not discovered.";
                return false;
            }

            if (layout.Ignored)
            {
                reason = $"Layout '{requestedLayoutId}' is ignored by config.";
                return false;
            }

            if (!layout.Valid)
            {
                reason = $"Layout '{requestedLayoutId}' is not valid: {string.Join("; ", layout.ValidationErrors ?? new List<string>())}";
                return false;
            }

            if (!IsEnabledLayout(layout.LayoutId))
            {
                reason = $"Layout '{requestedLayoutId}' is valid but not enabled. Run revents.layouts enable {layout.LayoutId}.";
                return false;
            }

            return true;
        }

        private bool TrySelectWeightedAutomaticLayout(out LayoutScanEntry layout, out string reason)
        {
            layout = null;
            reason = null;
            var configured = config.EventTypes.AutomaticBases.Layouts
                .Where(entry => entry != null && entry.Enabled && entry.Weight > 0f)
                .ToList();
            var candidates = new List<KeyValuePair<LayoutScanEntry, float>>();

            foreach (var entry in configured)
            {
                LayoutScanEntry scanned;
                if (!data.Layouts.TryGetValue(entry.LayoutId, out scanned) || scanned == null || !scanned.Valid || scanned.Ignored)
                    continue;
                candidates.Add(new KeyValuePair<LayoutScanEntry, float>(scanned, entry.Weight));
            }

            if (candidates.Count == 0)
            {
                reason = "Automatic Bases has no enabled, weighted, valid CopyPaste layouts. Scan layouts and enable at least one in /em.";
                return false;
            }

            var totalWeight = candidates.Sum(candidate => candidate.Value);
            var roll = UnityEngine.Random.Range(0f, totalWeight);
            foreach (var candidate in candidates)
            {
                roll -= candidate.Value;
                if (roll <= 0f)
                {
                    layout = candidate.Key;
                    return true;
                }
            }

            layout = candidates[candidates.Count - 1].Key;
            return true;
        }

        private bool ShouldAnnounceNewAutomaticBase()
        {
            var percentageLimit = config.EventTypes.AutomaticBases.PercentageToAnnounce;
            if (percentageLimit <= 0f)
                return false;
            if (percentageLimit >= 100f)
                return true;

            var active = data.ActiveRaidBases.Values
                .Where(IsActiveAutomaticBase)
                .ToList();
            if (active.Count == 0)
                return true;

            var announced = active.Count(entry => entry.IsAnnounced);
            var currentPercentage = announced * 100f / active.Count;
            return currentPercentage < percentageLimit;
        }

        private List<LayoutScanEntry> EnabledValidLayouts()
        {
            return data.Layouts.Values
                .Where(layout => layout != null && layout.Valid && !layout.Ignored && IsEnabledLayout(layout.LayoutId))
                .OrderBy(layout => layout.LayoutId)
                .ToList();
        }

        private bool TryFindRandomLocation(LayoutScanEntry layout, float rotationDegrees, out Vector3 pasteOrigin, out int candidateIndex, out string reason)
        {
            pasteOrigin = Vector3.zero;
            candidateIndex = -1;
            reason = null;

            if (config.SpawnGrid.Enabled)
            {
                if (!spawnGridReady)
                {
                    reason = spawnGridBuilding
                        ? $"Spawn grid is still building ({spawnGridProcessed}/{spawnGridTotal}, {spawnGridCache.Candidates.Count} candidates so far)."
                        : "Spawn grid is not ready.";
                    return false;
                }

                var maximumChecks = Math.Min(spawnGridCache.Candidates.Count, 50);
                for (var check = 0; check < maximumChecks; check++)
                {
                    Vector3 ground;
                    if (!TryReserveSpawnGridCandidate(layout, rotationDegrees, out candidateIndex, out ground, out reason, Math.Min(250, spawnGridCache.Candidates.Count)))
                        continue;
                    pasteOrigin = BuildPasteOriginFromGroundCells(layout, ground, rotationDegrees);
                    if (ValidateLocation(layout, pasteOrigin, rotationDegrees, out reason))
                        return true;

                    RejectReservedGridCandidate(layout, rotationDegrees, candidateIndex, reason);
                    candidateIndex = -1;
                }

                reason = $"No runtime-valid cached candidate was available for {layout.LayoutId} after {maximumChecks} checks. Last rejection: {reason ?? "none"}.";
                CountSpawnGridRejection("runtime:immediate search exhausted");
                return false;
            }

            var worldHalfSize = WorldHalfSize();
            var margin = Mathf.Clamp(config.LocationRules.MinDistanceFromMapEdge, 0f, worldHalfSize - 50f);
            var min = -worldHalfSize + margin;
            var max = worldHalfSize - margin;

            for (var attempt = 0; attempt < LegacyRandomSearchAttempts; attempt++)
            {
                var ground = new Vector3(UnityEngine.Random.Range(min, max), 0f, UnityEngine.Random.Range(min, max));
                if (!TrySnapToGround(ground, out ground))
                    continue;

                if (!TryBuildPasteOrigin(layout, ground, rotationDegrees, out pasteOrigin, out reason))
                    continue;

                if (ValidateLocation(layout, pasteOrigin, rotationDegrees, out reason))
                    return true;
            }

            reason = $"No valid random location found after {LegacyRandomSearchAttempts} legacy attempts. Last rejection: {reason ?? "none"}";
            return false;
        }

        private void RejectReservedGridCandidate(LayoutScanEntry layout, float rotationDegrees, int candidateIndex, string reason)
        {
            spawnGridReserved.Remove(candidateIndex);
            CountSpawnGridRejection("runtime:" + (reason ?? "unknown rejection"));
            var stats = GetLayoutPlacementStats(layout?.LayoutId);
            stats.LastRejection = reason;
            if (IsTemporarySpawnGridRejection(reason))
                spawnGridTemporaryUntil[candidateIndex] = NowUnix() + config.SpawnGrid.TemporaryRejectionRetrySeconds;
            else
                spawnGridLayoutRejections.Add(SpawnGridLayoutCandidateKey(layout, rotationDegrees, candidateIndex));
        }

        private bool TryBuildPasteOrigin(LayoutScanEntry layout, Vector3 groundPoint, float rotationDegrees, out Vector3 pasteOrigin, out string reason)
        {
            pasteOrigin = groundPoint;
            reason = null;

            if (layout == null)
            {
                reason = "Layout was not supplied.";
                return false;
            }

            if (!TrySnapToGround(groundPoint, out groundPoint))
            {
                reason = "Could not snap the requested position to ground.";
                return false;
            }

            pasteOrigin = BuildPasteOriginFromGroundCells(layout, groundPoint, rotationDegrees);
            return true;
        }

        private Vector3 BuildPasteOriginFromGroundCells(LayoutScanEntry layout, Vector3 groundPoint, float rotationDegrees)
        {
            var pasteOrigin = groundPoint;
            var cells = layout?.GroundFootprintCells?.Where(cell => cell != null && cell.Position != null).ToList();
            if (cells == null || cells.Count == 0)
            {
                pasteOrigin.y = TerrainHeight(groundPoint) - LayoutGroundAnchorY(layout) + config.Paste.GroundClearance;
                return pasteOrigin;
            }

            var foundationCells = cells.Where(cell => cell.IsFoundation).ToList();
            var elevationCells = foundationCells.Count > 0 ? foundationCells : cells;

            var rotation = Quaternion.Euler(0f, rotationDegrees, 0f);
            var requiredOriginY = float.MinValue;
            foreach (var cell in elevationCells)
            {
                foreach (var sample in BuildGroundCellWorldSamples(cell, groundPoint, rotation, 0f))
                {
                    var terrainHeight = TerrainHeight(sample);
                    var requiredBaseLayerHeight = terrainHeight + config.Paste.GroundClearance;
                    float waterHeight;
                    if (AdaptiveWaterSupportEnabled() && TryGetWaterSurfaceHeight(sample, terrainHeight, out waterHeight))
                    {
                        requiredBaseLayerHeight = Math.Max(requiredBaseLayerHeight,
                            waterHeight + config.Paste.AdaptiveFoundations.WaterSurfaceClearanceMeters);
                    }

                    requiredOriginY = Math.Max(requiredOriginY, requiredBaseLayerHeight - cell.Position.Y);
                }
            }
            pasteOrigin.y = requiredOriginY;
            return pasteOrigin;
        }

        private bool ValidateLocation(LayoutScanEntry layout, Vector3 pasteOrigin, float rotationDegrees, out string reason)
        {
            reason = null;
            var search = CreatePlacementSearch(layout, pasteOrigin, rotationDegrees);
            if (search.Samples.Count == 0)
            {
                reason = "layout has no ground occupancy cells";
                return false;
            }

            foreach (var sample in search.Samples)
            {
                if (!ValidateAutomaticTerrainSample(search, sample, out reason))
                    return false;
                if (config.LocationRules.BlockSafeZones && IsInsideNativeSafeZone(sample, 0f)) { reason = "footprint safe zone"; return false; }
            }

            string monumentName;
            if (config.LocationRules.BlockMonuments && AutomaticFootprintTouchesMonument(search, out monumentName)) { reason = "footprint monument " + monumentName; return false; }
            bool saturated;
            if (config.LocationRules.BlockSafeZones && AutomaticFootprintTouchesSafeZone(search, out saturated)) { reason = "footprint safe zone"; return false; }
            if (config.LocationRules.BlockNoBuildZones && AutomaticFootprintTouchesNoBuildZone(search, out saturated)) { reason = saturated ? "no-build query buffer saturated" : "footprint no-build zone"; return false; }
            if (config.LocationRules.BlockPlayerBases && AutomaticFootprintTouchesPlayerBase(search, out saturated)) { reason = saturated ? "player-base query buffer saturated" : "footprint near player base"; return false; }
            if (AutomaticFootprintTouchesPlayer(search)) { reason = "footprint near player or sleeper"; return false; }
            if (AutomaticFootprintNearActiveEvent(search)) { reason = "near active event"; return false; }
            if (FootprintOverlapsActiveRaid(layout, pasteOrigin, rotationDegrees)) { reason = "footprint overlaps active raid base"; return false; }

            search.SampleIndex = 0;
            while (search.SampleIndex < (search.Layout?.GroundFootprintCells?.Count ?? 0))
            {
                if (ProcessAutomaticFootprintObstacleSlice(search, out reason))
                    return false;
            }
            return true;
        }

        private AutomaticLocationSearch CreatePlacementSearch(LayoutScanEntry layout, Vector3 pasteOrigin, float rotationDegrees)
        {
            Vector3 boundsMin;
            Vector3 boundsMax;
            GetGroundFootprintBounds(layout, out boundsMin, out boundsMax);
            var rotation = Quaternion.Euler(0f, rotationDegrees, 0f);
            var localCenter = new Vector3((boundsMin.x + boundsMax.x) * 0.5f, LayoutGroundAnchorY(layout) + 5f, (boundsMin.z + boundsMax.z) * 0.5f);
            var padding = config.LocationRules.FootprintClearancePadding;
            return new AutomaticLocationSearch
            {
                Layout = layout,
                RotationDegrees = rotationDegrees,
                PasteOrigin = pasteOrigin,
                Rotation = rotation,
                FootprintCenter = pasteOrigin + rotation * localCenter,
                FootprintHalfExtents = new Vector3(Math.Max(1f, (boundsMax.x - boundsMin.x) * 0.5f + padding), 5f, Math.Max(1f, (boundsMax.z - boundsMin.z) * 0.5f + padding)),
                Samples = BuildAutomaticFootprintSamples(layout, pasteOrigin, rotation, 0f)
            };
        }

        private bool FootprintOverlapsActiveRaid(LayoutScanEntry layout, Vector3 pasteOrigin, float rotationDegrees)
        {
            if (layout?.GroundFootprintCells == null)
                return false;
            var rotation = Quaternion.Euler(0f, rotationDegrees, 0f);
            foreach (var active in data.ActiveRaidBases.Values)
            {
                if (active == null || string.Equals(active.Status, "cleaning", StringComparison.OrdinalIgnoreCase))
                    continue;
                LayoutScanEntry activeLayout;
                if (!data.Layouts.TryGetValue(active.LayoutId, out activeLayout) || activeLayout?.GroundFootprintCells == null)
                    continue;
                var activeOrigin = active.Position.ToVector3();
                var activeRotation = Quaternion.Euler(0f, active.RotationDegrees, 0f);
                foreach (var cell in layout.GroundFootprintCells.Where(value => value?.Position != null))
                {
                    var position = pasteOrigin + rotation * new Vector3(cell.Position.X, 0f, cell.Position.Z);
                    foreach (var other in activeLayout.GroundFootprintCells.Where(value => value?.Position != null))
                    {
                        var otherPosition = activeOrigin + activeRotation * new Vector3(other.Position.X, 0f, other.Position.Z);
                        var dx = position.x - otherPosition.x;
                        var dz = position.z - otherPosition.z;
                        var required = Mathf.Max(0.5f, cell.Radius) + Mathf.Max(0.5f, other.Radius);
                        if (dx * dx + dz * dz < required * required)
                            return true;
                    }
                }
            }
            return false;
        }

        private bool TryGetHerePosition(BasePlayer player, out Vector3 position, out string reason)
        {
            position = Vector3.zero;
            reason = null;

            if (player == null)
            {
                reason = "Player was not found.";
                return false;
            }

            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 250f, GroundLayer, QueryTriggerInteraction.Ignore))
            {
                return TrySnapToGround(hit.point, out position);
            }

            return TrySnapToGround(player.transform.position, out position);
        }

        private bool TrySnapToGround(Vector3 position, out Vector3 groundPoint)
        {
            groundPoint = position;

            RaycastHit hit;
            var origin = position + Vector3.up * 250f;
            if (Physics.Raycast(origin, Vector3.down, out hit, 600f, GroundLayer, QueryTriggerInteraction.Ignore))
            {
                groundPoint = hit.point;
                return true;
            }

            if (TerrainMeta.HeightMap == null)
                return false;

            groundPoint.y = TerrainMeta.HeightMap.GetHeight(position);
            return true;
        }

        private bool IsNearMapEdge(Vector3 position)
        {
            var halfSize = WorldHalfSize();
            var margin = config.LocationRules.MinDistanceFromMapEdge;
            return position.x < -halfSize + margin || position.x > halfSize - margin ||
                   position.z < -halfSize + margin || position.z > halfSize - margin;
        }

        private float WorldHalfSize()
        {
            if (TerrainMeta.Size.x > 0f)
                return TerrainMeta.Size.x * 0.5f;

            return Math.Max(1000f, ConVar.Server.worldsize * 0.5f);
        }

        private float TerrainHeight(Vector3 position)
        {
            return TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(position) : position.y;
        }

        private float TerrainGrade(Vector3 position, float sampleRadius)
        {
            if (TerrainMeta.HeightMap == null)
                return 0f;

            var radius = Mathf.Max(0.5f, sampleRadius);
            var diameter = radius * 2f;
            var diagonalRun = diameter * Mathf.Sqrt(2f);
            var xGrade = Math.Abs(TerrainHeight(position + new Vector3(radius, 0f, 0f)) - TerrainHeight(position - new Vector3(radius, 0f, 0f))) / diameter;
            var zGrade = Math.Abs(TerrainHeight(position + new Vector3(0f, 0f, radius)) - TerrainHeight(position - new Vector3(0f, 0f, radius))) / diameter;
            var diagonalOne = Math.Abs(TerrainHeight(position + new Vector3(radius, 0f, radius)) - TerrainHeight(position - new Vector3(radius, 0f, radius))) / diagonalRun;
            var diagonalTwo = Math.Abs(TerrainHeight(position + new Vector3(radius, 0f, -radius)) - TerrainHeight(position - new Vector3(radius, 0f, -radius))) / diagonalRun;
            return Mathf.Max(Mathf.Max(xGrade, zGrade), Mathf.Max(diagonalOne, diagonalTwo));
        }

        private bool IsWaterSurface(Vector3 position)
        {
            if (TerrainMeta.WaterMap == null || TerrainMeta.HeightMap == null)
                return false;

            var terrainHeight = TerrainMeta.HeightMap.GetHeight(position);
            var waterHeight = TerrainMeta.WaterMap.GetHeight(position);
            if (waterHeight > terrainHeight + config.LocationRules.WaterClearance)
                return true;

            try
            {
                return WaterLevel.Test(position + Vector3.up * config.LocationRules.WaterClearance, true, true);
            }
            catch
            {
                return false;
            }
        }

        private bool IsInBlockedMonument(Vector3 position, out string monumentName)
        {
            monumentName = null;
            EnsureMonumentZones();

            foreach (var zone in monumentZones)
            {
                var dx = position.x - zone.Center.x;
                var dz = position.z - zone.Center.z;
                if (dx * dx + dz * dz <= zone.Radius * zone.Radius)
                {
                    monumentName = zone.Name;
                    return true;
                }
            }

            return false;
        }

        private void EnsureMonumentZones()
        {
            if (monumentZonesLoaded)
                return;

            monumentZonesLoaded = true;
            monumentZones.Clear();

            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null)
                return;

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null)
                    continue;

                var bounds = monument.Bounds;
                var radius = GetMonumentRadius(monument) + config.LocationRules.MonumentRadiusPadding;
                if (radius <= 0f)
                    continue;

                monumentZones.Add(new MonumentZone
                {
                    Center = bounds.size.sqrMagnitude > 0f ? bounds.center : monument.transform.position,
                    Radius = radius,
                    Name = GetMonumentShortName(monument)
                });
            }
        }

        private float GetMonumentRadius(MonumentInfo monument)
        {
            var bounds = monument.Bounds;
            var boundsRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            if (boundsRadius > 1f)
                return boundsRadius;

            switch (GetMonumentShortName(monument))
            {
                case "airfield_1": return 255f;
                case "bandit_town": return 105f;
                case "compound": return 255f;
                case "excavator_1": return 150f;
                case "gas_station_1": return 60f;
                case "harbor_1":
                case "harbor_2": return 135f;
                case "junkyard_1": return 105f;
                case "launch_site_1": return 245f;
                case "lighthouse": return 50f;
                case "military_tunnel_1": return 105f;
                case "powerplant_1": return 145f;
                case "satellite_dish": return 85f;
                case "sphere_tank": return 75f;
                case "supermarket_1": return 60f;
                case "trainyard_1": return 145f;
                case "warehouse": return 50f;
                case "water_treatment_plant_1": return 175f;
            }

            return config.LocationRules.DefaultMonumentRadius;
        }

        private string GetMonumentShortName(MonumentInfo monument)
        {
            var name = monument?.name ?? string.Empty;
            var separator = name.LastIndexOf('/');
            return (separator > 0 ? name.Substring(separator + 1) : name).Replace(".prefab", "");
        }

        private bool IsFlatEnough(Vector3 position, float sampleRadius)
        {
            if (TerrainMeta.HeightMap == null)
                return true;

            var center = TerrainMeta.HeightMap.GetHeight(position);
            var radius = Mathf.Max(1f, sampleRadius);
            var samples = new[]
            {
                new Vector3(radius, 0f, 0f),
                new Vector3(-radius, 0f, 0f),
                new Vector3(0f, 0f, radius),
                new Vector3(0f, 0f, -radius),
                new Vector3(radius, 0f, radius),
                new Vector3(radius, 0f, -radius),
                new Vector3(-radius, 0f, radius),
                new Vector3(-radius, 0f, -radius)
            };

            foreach (var offset in samples)
            {
                if (Math.Abs(TerrainMeta.HeightMap.GetHeight(position + offset) - center) > config.LocationRules.MaxFlatnessDelta)
                    return false;
            }

            return true;
        }

        private bool IsPlayerBaseEntity(BaseEntity entity)
        {
            if (entity == null)
                return false;

            var entityId = entity.net == null ? 0UL : entity.net.ID.Value;
            if (entityToInstance.ContainsKey(entityId))
                return false;

            if (entity is BuildingPrivlidge || entity is BuildingBlock || entity is Door)
                return entity.OwnerID != 0;

            if (entity.OwnerID == 0)
                return false;

            var text = $"{entity.GetType().Name} {entity.ShortPrefabName} {entity.PrefabName}".ToLowerInvariant();
            return text.Contains("cupboard")
                   || text.Contains("foundation")
                   || text.Contains("wall")
                   || text.Contains("floor")
                   || text.Contains("door")
                   || text.Contains("frame")
                   || text.Contains("gate.external");
        }

        private ulong FindToolCupboardId(List<ulong> entityIds)
        {
            if (entityIds == null)
                return 0;

            foreach (var entityId in entityIds)
            {
                var entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as BaseEntity;
                if (entity == null || entity.IsDestroyed)
                    continue;

                if (entity is BuildingPrivlidge || IsToolCupboardPrefab(entity.PrefabName) || IsToolCupboardPrefab(entity.ShortPrefabName))
                    return entityId;
            }

            return 0;
        }

        private void RebuildEntityIndex()
        {
            entityToInstance.Clear();
            foreach (var active in data.ActiveRaidBases.Values)
            {
                if (active.EntityIds == null)
                    continue;

                foreach (var entityId in active.EntityIds.Where(id => id != 0))
                    entityToInstance[entityId] = active.InstanceId;
            }
        }

        private bool TryEnableLayout(string layoutId, out string message)
        {
            message = null;

            if (string.IsNullOrWhiteSpace(layoutId))
            {
                message = "Layout id is required.";
                return false;
            }

            LayoutScanEntry layout;
            if (!data.Layouts.TryGetValue(layoutId, out layout))
            {
                ScanLayouts(true);
                data.Layouts.TryGetValue(layoutId, out layout);
            }

            if (layout == null)
            {
                message = $"Layout '{layoutId}' was not discovered.";
                return false;
            }

            if (layout.Ignored)
            {
                message = $"Layout '{layoutId}' is ignored by config.";
                return false;
            }

            if (!layout.Valid)
            {
                message = $"Layout '{layoutId}' is invalid: {string.Join("; ", layout.ValidationErrors ?? new List<string>())}";
                return false;
            }

            if (!IsEnabledLayout(layout.LayoutId))
            {
                config.LayoutRotation.EnabledLayouts.Add(layout.LayoutId);
                var weighted = config.EventTypes.AutomaticBases.Layouts.FirstOrDefault(value => value.LayoutId.Equals(layout.LayoutId, StringComparison.OrdinalIgnoreCase));
                if (weighted == null)
                {
                    config.EventTypes.AutomaticBases.Layouts.Add(new WeightedLayoutConfig { LayoutId = layout.LayoutId, Enabled = true, Weight = 1f });
                }
                else
                {
                    weighted.Enabled = true;
                }
            }

            SaveConfig();
            message = $"Enabled raid base layout {layout.LayoutId}.";
            return true;
        }

        private bool DisableLayout(string layoutId, out string message)
        {
            message = null;

            if (string.IsNullOrWhiteSpace(layoutId))
            {
                message = "Layout id is required.";
                return false;
            }

            config.LayoutRotation.EnabledLayouts.RemoveAll(value => value.Equals(layoutId, StringComparison.OrdinalIgnoreCase));
            var weighted = config.EventTypes.AutomaticBases.Layouts.FirstOrDefault(value => value.LayoutId.Equals(layoutId, StringComparison.OrdinalIgnoreCase));
            if (weighted != null)
                weighted.Enabled = false;
            SaveConfig();
            message = $"Disabled raid base layout {layoutId}.";
            return true;
        }

        private string BuildStatusMessage(bool includeDetails)
        {
            var enabledLayouts = EnabledValidLayouts();
            var activeCount = ActiveEventCount();
            var automaticBases = config.EventTypes.AutomaticBases;
            var searchElapsed = Math.Max(0d, NowUnix() - (automaticLocationSearch?.StartedUnix ?? NowUnix()));
            var message = $"RaidlandsEvents: automaticBases={(automaticBases.Enabled ? "on" : "off")} population={AutomaticBaseActiveCount()}/{automaticBases.MinimumActiveBases}-{automaticBases.MaximumActiveBases}, queuedSearches={data.PendingAutomaticSpawnRequests}, searchLayout={automaticLocationSearch?.Layout?.LayoutId ?? "none"}, searchRejected={automaticSearchRejectedCandidates}, searchElapsed={FormatDuration(searchElapsed)}, searchLast={automaticSearchLastRejection ?? "none"}, frequency={automaticBases.CheckFrequencyMinutes:0.#}m, announce={automaticBases.PercentageToAnnounce:0.#}%, scoring={(config.Scoring.Enabled ? "on" : "off")}, rewards={(config.Rewards.Enabled ? "on" : "off")}, rewardProfiles={rewardProfiles.Count}, enabledLayouts={enabledLayouts.Count}, discovered={data.Layouts.Count}, totalActive={activeCount}, pendingPastes={pendingPasteInstances.Count}, pendingRewards={PendingRewardTransactionCount()}, rewardReview={CountRewardTransactions("review-required")}, legacyPendingPurchaseRefunds={data.PendingPurchaseRefunds.Count}.";

            message += "\n" + BuildSpawnGridStatus(includeDetails);
            if (!includeDetails || data.ActiveRaidBases.Count == 0)
                return message;

            var activeLines = data.ActiveRaidBases.Values
                .OrderBy(active => active.StartedUnix)
                .Select(active => $"{active.InstanceId}: {active.Status}, layout={active.LayoutId}, entities={active.EntityIds?.Count ?? 0}, scores={active.Scores?.Count ?? 0}, tc={active.ToolCupboardId}, pos={FormatVector(active.Position.ToVector3())}");

            return message + "\n" + string.Join("\n", activeLines);
        }

        private string BuildLayoutList()
        {
            if (data.Layouts.Count == 0)
                return "No layouts discovered yet. Run revents.layouts scan.";

            var lines = new List<string> { "RaidlandsEvents layouts:" };
            foreach (var layout in data.Layouts.Values.OrderBy(layout => layout.LayoutId))
            {
                var enabled = IsEnabledLayout(layout.LayoutId) ? "enabled" : "disabled";
                var ignored = layout.Ignored ? "ignored" : "not ignored";
                var valid = layout.Valid ? "valid" : "invalid";
                var errors = layout.Valid ? "" : $" errors={string.Join("; ", layout.ValidationErrors ?? new List<string>())}";
                lines.Add($"{layout.LayoutId}: {valid}, {enabled}, {ignored}, entities={layout.EntityCount}, groundCells={layout.GroundFootprintCells?.Count ?? 0}, groundRadius={layout.GroundFootprintRadius:0.#}m, anchorY={layout.GroundAnchorY:0.##}, autoTurrets={layout.AutoTurretCount}, tc={layout.HasToolCupboard}, crateLike={layout.HasCrateLikeEntity}{errors}");
            }

            return string.Join("\n", lines);
        }

        private string BuildPendingPurchaseRefundsMessage()
        {
            if (data.PendingPurchaseRefunds.Count == 0)
                return "No pending RaidlandsEvents purchase refunds.";

            var lines = new List<string> { $"Pending RaidlandsEvents purchase refunds: {data.PendingPurchaseRefunds.Count}" };
            foreach (var refund in data.PendingPurchaseRefunds.Values.OrderBy(record => record.CreatedUnix).Take(10))
            {
                lines.Add($"{refund.RefundId}: {refund.DisplayName ?? refund.UserId}, costs={BuildCostSummary(refund.Costs)}, attempts={refund.AttemptCount}, error={refund.LastError}");
            }

            if (data.PendingPurchaseRefunds.Count > 10)
                lines.Add($"...and {data.PendingPurchaseRefunds.Count - 10} more.");

            return string.Join("\n", lines);
        }

        private int ActiveEventCount()
        {
            return data.ActiveRaidBases.Values.Count(active => active.Status != "cleaning");
        }

        private bool IsActiveAutomaticBase(ActiveRaidBase active)
        {
            return active != null && active.Status != "cleaning" &&
                (string.Equals(active.EventTypeId, "automatic-bases", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(active.TriggerType, "automatic", StringComparison.OrdinalIgnoreCase));
        }

        private int AutomaticBaseActiveCount()
        {
            return data.ActiveRaidBases.Values.Count(IsActiveAutomaticBase);
        }

        private bool IsEnabledLayout(string layoutId)
        {
            var weighted = config?.EventTypes?.AutomaticBases?.Layouts?
                .FirstOrDefault(value => value != null && value.LayoutId.Equals(layoutId, StringComparison.OrdinalIgnoreCase));
            return weighted != null
                ? weighted.Enabled
                : config.LayoutRotation.EnabledLayouts.Any(value => value.Equals(layoutId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsIgnoredLayout(string layoutId)
        {
            return config.LayoutRotation.IgnoredLayouts.Any(value => value.Equals(layoutId, StringComparison.OrdinalIgnoreCase));
        }

        private string PublicDisplayName()
        {
            return string.IsNullOrWhiteSpace(config.LayoutRotation.PublicDisplayName)
                ? "Public Raid Base"
                : config.LayoutRotation.PublicDisplayName;
        }

        private float RandomRotationDegrees()
        {
            var step = config.Paste.RandomRotationDegreesStep;
            if (step <= 0f)
                return UnityEngine.Random.Range(0f, 360f);

            var steps = Mathf.Max(1, Mathf.RoundToInt(360f / step));
            return Mathf.Repeat(UnityEngine.Random.Range(0, steps) * step, 360f);
        }

        private string NewInstanceId()
        {
            return $"rb-{DateTimeOffset.UtcNow.ToUnixTimeSeconds():x}-{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string ExtractLayoutId(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                return null;

            var normalized = file.Replace('\\', '/');
            var lastSlash = normalized.LastIndexOf('/');
            if (lastSlash >= 0)
                normalized = normalized.Substring(lastSlash + 1);

            return normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(0, normalized.Length - 5)
                : normalized;
        }

        private bool TryGetRelativePosition(Dictionary<string, object> entity, out Vector3 position)
        {
            position = Vector3.zero;
            if (entity == null)
                return false;

            object rawPos;
            if (!entity.TryGetValue("pos", out rawPos))
                return false;

            var pos = rawPos as Dictionary<string, object>;
            if (pos == null)
                return false;

            object rawX;
            object rawY;
            object rawZ;
            if (!pos.TryGetValue("x", out rawX) || !pos.TryGetValue("y", out rawY) || !pos.TryGetValue("z", out rawZ))
                return false;

            position = new Vector3(
                Convert.ToSingle(rawX, CultureInfo.InvariantCulture),
                Convert.ToSingle(rawY, CultureInfo.InvariantCulture),
                Convert.ToSingle(rawZ, CultureInfo.InvariantCulture));
            return true;
        }

        private string GetEntityPrefab(Dictionary<string, object> entity)
        {
            object prefab;
            if (entity != null && entity.TryGetValue("prefabname", out prefab) && prefab != null)
                return prefab.ToString();

            return string.Empty;
        }

        private float GetRelativeRotationDegrees(Dictionary<string, object> entity)
        {
            object rawRotation;
            var rotation = entity != null && entity.TryGetValue("rot", out rawRotation) ? rawRotation as Dictionary<string, object> : null;
            object rawY;
            if (rotation == null || !rotation.TryGetValue("y", out rawY) || rawY == null)
                return 0f;
            try
            {
                return Convert.ToSingle(rawY, CultureInfo.InvariantCulture) * Mathf.Rad2Deg;
            }
            catch
            {
                return 0f;
            }
        }

        private GroundFootprintCell CreateGroundFootprintCell(Vector3 position, float rotationDegrees, string prefab)
        {
            var lower = (prefab ?? string.Empty).ToLowerInvariant();
            var halfWidth = lower.Contains("wall.external.high") ? 3f : 1.5f;
            var halfDepth = lower.Contains("wall.external.high") ? 0.35f : lower.Contains("triangle") ? 1.3f : 1.5f;
            var radius = Mathf.Sqrt(halfWidth * halfWidth + halfDepth * halfDepth);
            return new GroundFootprintCell
            {
                IsFoundation = IsFoundationPrefab(prefab),
                Position = new StoredVector3(position),
                Radius = radius,
                RotationDegrees = rotationDegrees,
                HalfWidth = halfWidth,
                HalfDepth = halfDepth
            };
        }

        private bool IsFoundationPrefab(string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
                return false;
            var lower = prefab.ToLowerInvariant();
            return lower.Contains("/foundation") || lower.Contains("foundation.");
        }

        private bool IsExternalWallPrefab(string prefab)
        {
            return !string.IsNullOrWhiteSpace(prefab)
                   && prefab.IndexOf("wall.external.high", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsFloorPrefab(string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
                return false;
            var lower = prefab.ToLowerInvariant();
            return lower.Contains("/floor") || lower.Contains("floor.");
        }

        private bool IsToolCupboardPrefab(string prefab)
        {
            return !string.IsNullOrWhiteSpace(prefab) &&
                   prefab.IndexOf("cupboard.tool", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsAutoTurretPrefab(string prefab)
        {
            return !string.IsNullOrWhiteSpace(prefab)
                   && prefab.IndexOf("/autoturret/autoturret_deployed.prefab", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsCrateLikePrefab(string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
                return false;

            var lower = prefab.ToLowerInvariant();
            return lower.Contains("crate")
                   || lower.Contains("box.wooden")
                   || lower.Contains("coffin")
                   || lower.Contains("locker")
                   || lower.Contains("small_stash")
                   || lower.Contains("woodbox");
        }

        private bool IsPasteStartSuccess(object result)
        {
            return result is bool && (bool)result;
        }

        private string NormalizeCostType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return string.Empty;

            var normalized = type.Trim();
            if (normalized.Equals("ServerRewards", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("ServerRewards RP", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Rewards", StringComparison.OrdinalIgnoreCase))
                return "RP";

            if (normalized.Equals("Item", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("RustItem", StringComparison.OrdinalIgnoreCase))
                return "Item";

            if (normalized.Equals("RP", StringComparison.OrdinalIgnoreCase))
                return "RP";

            return normalized;
        }


        private PurchaseCostRecord ClonePurchaseCost(PurchaseCostRecord cost)
        {
            if (cost == null)
                return null;

            return new PurchaseCostRecord
            {
                Type = NormalizeCostType(cost.Type),
                ShortName = cost.ShortName,
                Amount = Math.Max(0, cost.Amount),
                DisplayName = cost.DisplayName
            };
        }

        private string BuildCostSummary(IEnumerable<PurchaseCostRecord> costs)
        {
            var parts = (costs ?? Enumerable.Empty<PurchaseCostRecord>())
                .Where(cost => cost != null && cost.Amount > 0)
                .Select(cost => string.IsNullOrWhiteSpace(cost.DisplayName)
                    ? cost.Type == "RP" ? $"{cost.Amount:n0} RP" : $"{cost.Amount:n0} {cost.ShortName}"
                    : cost.DisplayName)
                .ToList();

            return parts.Count == 0 ? "free" : string.Join(" + ", parts);
        }


        private int CheckServerRewardsPoints(string userId, out string details)
        {
            details = "unavailable";

            if (ServerRewards == null || !ServerRewards.IsLoaded || string.IsNullOrWhiteSpace(userId))
                return 0;

            ulong parsedUserId;
            var hasParsedUserId = ulong.TryParse(userId, out parsedUserId);
            var values = new List<int>();
            var attempts = new List<string>();

            if (hasParsedUserId)
                TryReadServerRewardsPoints("parsed ulong", () => ServerRewards.Call("CheckPoints", parsedUserId), values, attempts);

            TryReadServerRewardsPoints("UserIDString", () => ServerRewards.Call("CheckPoints", userId), values, attempts);

            details = attempts.Count == 0 ? "none" : string.Join("; ", attempts);
            return values.Count == 0 ? 0 : values.Max();
        }


        private IEnumerable<string> ServerRewardsAdminCommands()
        {
            var commands = new List<string>();
            AddServerRewardsAdminCommand(commands, ReadServerRewardsAdminCommandFromConfig());
            AddServerRewardsAdminCommand(commands, "xp");
            AddServerRewardsAdminCommand(commands, "rp");
            return commands;
        }

        private void AddServerRewardsAdminCommand(List<string> commands, string commandName)
        {
            if (commands == null || string.IsNullOrWhiteSpace(commandName))
                return;

            commandName = commandName.Trim().TrimStart('/');
            if (commandName.Length == 0 || commands.Any(command => command.Equals(commandName, StringComparison.OrdinalIgnoreCase)))
                return;

            commands.Add(commandName);
        }

        private string ReadServerRewardsAdminCommandFromConfig()
        {
            try
            {
                var path = System.IO.Path.Combine(Interface.Oxide.ConfigDirectory, "ServerRewards.json");
                if (!System.IO.File.Exists(path))
                    return null;

                var root = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(System.IO.File.ReadAllText(path));
                return root?["Options"]?["Admin RP Command"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private void TryReadServerRewardsPoints(string label, Func<object> call, List<int> values, List<string> attempts)
        {
            try
            {
                var result = call();
                int value;
                if (!TryConvertServerRewardsInt(result, out value))
                {
                    attempts.Add($"{label}=unreadable({FormatServerRewardsResult(result)})");
                    return;
                }

                values.Add(value);
                attempts.Add($"{label}={value:n0}");
            }
            catch (Exception exception)
            {
                attempts.Add($"{label}=error({exception.Message})");
            }
        }

        private bool TryCallServerRewardsBool(string label, Func<object> call, List<string> attempts)
        {
            try
            {
                var result = call();
                if (result is bool)
                {
                    var accepted = (bool)result;
                    attempts.Add($"{label}={accepted}");
                    return accepted;
                }

                attempts.Add($"{label}=unreadable({FormatServerRewardsResult(result)})");
                return false;
            }
            catch (Exception exception)
            {
                attempts.Add($"{label}=error({exception.Message})");
                return false;
            }
        }

        private bool TryConvertServerRewardsInt(object result, out int value)
        {
            value = 0;
            if (result == null)
                return false;

            try
            {
                value = Convert.ToInt32(result, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string FormatServerRewardsResult(object result)
        {
            return result == null ? "null" : result.ToString();
        }

        private string FormatDuration(double seconds)
        {
            if (seconds <= 0)
                return "0s";

            var rounded = Mathf.CeilToInt((float)seconds);
            var minutes = rounded / 60;
            var remainder = rounded % 60;
            if (minutes <= 0)
                return $"{remainder}s";

            return remainder <= 0 ? $"{minutes}m" : $"{minutes}m {remainder}s";
        }

        private BasePlayer PlayerFromStringId(string userId)
        {
            ulong parsed;
            return ulong.TryParse(userId, out parsed) ? PlayerFromId(parsed) : null;
        }

        private void TellPurchasePlayer(string userId, string message)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(message))
                return;

            var player = PlayerFromStringId(userId);
            if (player != null)
                SendReply(player, message);
        }

        private bool HasAccess(ConsoleSystem.Arg arg, string requiredPermission)
        {
            if (arg == null)
                return true;

            var player = arg.Player();
            if (player == null)
                return true;

            if (HasPlayerAccess(player, requiredPermission))
                return true;

            arg.ReplyWith("You do not have permission to use this RaidlandsEvents command.");
            return false;
        }

        private bool HasPlayerAccess(BasePlayer player, string requiredPermission)
        {
            if (player == null)
                return true;

            return player.IsAdmin
                   || permission.UserHasPermission(player.UserIDString, AdminPermission)
                   || permission.UserHasPermission(player.UserIDString, requiredPermission);
        }

        private string[] GetArgs(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Args == null)
                return new string[0];

            return arg.Args.Select(value => value.ToString()).ToArray();
        }

        private void Reply(ConsoleSystem.Arg arg, string message)
        {
            if (arg == null)
            {
                Puts(message);
                return;
            }

            arg.ReplyWith(message);
        }

        private string FormatVector(Vector3 position)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2:0.0}", position.x, position.y, position.z);
        }

        private Color ParseColor(string value, Color fallback)
        {
            Color color;
            if (!string.IsNullOrWhiteSpace(value) && ColorUtility.TryParseHtmlString(value, out color))
                return color;

            return fallback;
        }

        private double NowUnix()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
