using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RaidlandsEvents", "Raidlands", "0.1.20")]
    [Description("Raidlands public event manager MVP for random CopyPaste raid bases.")]
    public class RaidlandsEvents : RustPlugin
    {
        private const string AdminPermission = "raidlandsevents.admin";
        private const string LayoutPermission = "raidlandsevents.admin.layouts";
        private const string StartPermission = "raidlandsevents.admin.start";
        private const string StopPermission = "raidlandsevents.admin.stop";
        private const string PurchasePermission = "raidlandsevents.player.purchase";
        private const string DataFileName = "RaidlandsEvents";
        private const string CopyPasteDirectory = "copypaste/";
        private const string GenericRadiusMapMarkerPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        private const string EventsManagerUi = "RaidlandsEvents.EventsManagerUi";
        private const float NativeMarkerBaseRadius = 0.015f;
        private const float NativeMarkerRadiusPerMeter = 0.004f;

        private static readonly int GroundLayer = LayerMask.GetMask("Terrain", "World", "Water", "Default");
        private static readonly int RoadCheckLayer = LayerMask.GetMask("World");
        private static readonly int PlayerBaseLayer = LayerMask.GetMask("Construction", "Deployed");
        private static readonly int OverlapLayer = LayerMask.GetMask("Construction", "Construction Trigger", "Deployed", "Vehicle Large");

        [PluginReference]
        private Plugin CopyPaste, RaidlandsSentryTurrets;

        [PluginReference]
        private Plugin ServerRewards;

        private Configuration config;
        private StoredData data;
        private Timer autoSpawnTimer;
        private Timer expiryTimer;
        private readonly Dictionary<string, MapMarkerGenericRadius> markers = new Dictionary<string, MapMarkerGenericRadius>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, string> entityToInstance = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, ulong> explosiveOwnerIds = new Dictionary<ulong, ulong>();
        private readonly HashSet<string> pendingPasteInstances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MonumentZone> monumentZones = new List<MonumentZone>();
        private bool monumentZonesLoaded;
        private double lastScoreSaveUnix;

        private class Configuration
        {
            [JsonProperty("AutoSpawn")]
            public AutoSpawnConfig AutoSpawn = new AutoSpawnConfig();

            [JsonProperty("LayoutRotation")]
            public LayoutRotationConfig LayoutRotation = new LayoutRotationConfig();

            [JsonProperty("LocationRules")]
            public LocationRulesConfig LocationRules = new LocationRulesConfig();

            [JsonProperty("Paste")]
            public PasteConfig Paste = new PasteConfig();

            [JsonProperty("MapMarker")]
            public MapMarkerConfig MapMarker = new MapMarkerConfig();

            [JsonProperty("Scoring")]
            public ScoringConfig Scoring = new ScoringConfig();

            [JsonProperty("Rewards")]
            public RewardsConfig Rewards = new RewardsConfig();

            [JsonProperty("Purchase")]
            public PurchaseConfig Purchase = new PurchaseConfig();

            [JsonProperty("Cleanup")]
            public CleanupConfig Cleanup = new CleanupConfig();

            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "<color=#ce422b>[Raidlands]</color>";
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
            [JsonProperty("RandomSearchAttempts")]
            public int RandomSearchAttempts = 80;

            [JsonProperty("MinDistanceFromMapEdge")]
            public float MinDistanceFromMapEdge = 350f;

            [JsonProperty("BlockWater")]
            public bool BlockWater = true;

            [JsonProperty("WaterClearance")]
            public float WaterClearance = 1.5f;

            [JsonProperty("BlockSafeZones")]
            public bool BlockSafeZones = true;

            [JsonProperty("SafeZoneRadius")]
            public float SafeZoneRadius = 120f;

            [JsonProperty("BlockMonuments")]
            public bool BlockMonuments = true;

            [JsonProperty("MonumentRadiusPadding")]
            public float MonumentRadiusPadding = 85f;

            [JsonProperty("DefaultMonumentRadius")]
            public float DefaultMonumentRadius = 80f;

            [JsonProperty("BlockRoads")]
            public bool BlockRoads = true;

            [JsonProperty("RoadCheckHeight")]
            public float RoadCheckHeight = 10f;

            [JsonProperty("RoadCheckDepth")]
            public float RoadCheckDepth = 40f;

            [JsonProperty("BlockPlayerBases")]
            public bool BlockPlayerBases = true;

            [JsonProperty("PlayerBaseRadius")]
            public float PlayerBaseRadius = 95f;

            [JsonProperty("MinimumDistanceBetweenEvents")]
            public float MinimumDistanceBetweenEvents = 350f;

            [JsonProperty("MaxSlope")]
            public float MaxSlope = 0.45f;

            [JsonProperty("FlatnessSampleRadius")]
            public float FlatnessSampleRadius = 18f;

            [JsonProperty("MaxFlatnessDelta")]
            public float MaxFlatnessDelta = 5f;

            [JsonProperty("OverlapPadding")]
            public float OverlapPadding = 4f;
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
                "enablesaving", "false"
            };

            [JsonProperty("RandomRotationDegreesStep")]
            public float RandomRotationDegreesStep = 90f;

            [JsonProperty("GroundClearance")]
            public float GroundClearance = 0.25f;

            [JsonProperty("Force Pasted Turrets Attack All")]
            public bool ForcePastedTurretsAttackAll = true;

            [JsonProperty("Pasted Turret Attack All Reapply Delays Seconds")]
            public float[] PastedTurretAttackAllReapplyDelaysSeconds = { 0.1f, 0.5f, 1.5f, 3f };
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
        }

        private class RewardsConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = false;

            [JsonProperty("Award ServerRewards RP")]
            public bool AwardServerRewardsRp = false;

            [JsonProperty("Tell Players About RP Rewards")]
            public bool TellPlayersAboutRpRewards = true;

            [JsonProperty("Queue Rewards If ServerRewards Missing")]
            public bool QueueRewardsIfServerRewardsMissing = true;

            [JsonProperty("Placement RP Rewards", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PlacementRewardConfig> PlacementRpRewards = new List<PlacementRewardConfig>
            {
                new PlacementRewardConfig { Place = 1, ServerRewardsRp = 10000 },
                new PlacementRewardConfig { Place = 2, ServerRewardsRp = 5000 },
                new PlacementRewardConfig { Place = 3, ServerRewardsRp = 2500 }
            };
        }

        private class PlacementRewardConfig
        {
            [JsonProperty("Place")]
            public int Place;

            [JsonProperty("ServerRewards RP")]
            public int ServerRewardsRp;
        }

        private class PurchaseConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = false;

            [JsonProperty("Permission")]
            public string Permission = PurchasePermission;

            [JsonProperty("Default Layout Id")]
            public string DefaultLayoutId = "random";

            [JsonProperty("Allowed Layout Ids", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AllowedLayoutIds = new List<string>();

            [JsonProperty("Allow Random Layout")]
            public bool AllowRandomLayout = true;

            [JsonProperty("Require Random Location")]
            public bool RequireRandomLocation = true;

            [JsonProperty("Min Online Players")]
            public int MinOnlinePlayers = 0;

            [JsonProperty("Cooldown Minutes Per Player")]
            public float CooldownMinutesPerPlayer = 180f;

            [JsonProperty("Cooldown Minutes Global")]
            public float CooldownMinutesGlobal = 45f;

            [JsonProperty("Refund On Start Failure")]
            public bool RefundOnStartFailure = true;

            [JsonProperty("Announce Purchaser")]
            public bool AnnouncePurchaser = true;

            [JsonProperty("Purchaser Does Not Own Event")]
            public bool PurchaserDoesNotOwnEvent = true;

            [JsonProperty("Costs", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PurchaseCostConfig> Costs = new List<PurchaseCostConfig>
            {
                new PurchaseCostConfig { Type = "RP", Amount = 12000, DisplayName = "12,000 RP" },
                new PurchaseCostConfig { Type = "Item", ShortName = "scrap", Amount = 50000, DisplayName = "50,000 scrap" }
            };
        }

        private class PurchaseCostConfig
        {
            [JsonProperty("Type")]
            public string Type = "RP";

            [JsonProperty("ShortName")]
            public string ShortName;

            [JsonProperty("Amount")]
            public int Amount;

            [JsonProperty("DisplayName")]
            public string DisplayName;
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

            [JsonProperty("PendingRewards")]
            public Dictionary<string, PendingRewardRecord> PendingRewards = new Dictionary<string, PendingRewardRecord>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("LastPurchaseUnix")]
            public double LastPurchaseUnix;

            [JsonProperty("LastPurchaseByPlayer")]
            public Dictionary<string, double> LastPurchaseByPlayer = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("PendingPurchaseRefunds")]
            public Dictionary<string, PendingPurchaseRefundRecord> PendingPurchaseRefunds = new Dictionary<string, PendingPurchaseRefundRecord>(StringComparer.OrdinalIgnoreCase);
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

        private class PurchaseStartContext
        {
            public BasePlayer Purchaser;
            public string UserId;
            public string DisplayName;
            public List<PurchaseCostRecord> CostsPaid = new List<PurchaseCostRecord>();
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
            if (config.AutoSpawn == null) config.AutoSpawn = defaults.AutoSpawn;
            if (config.LayoutRotation == null) config.LayoutRotation = defaults.LayoutRotation;
            if (config.LocationRules == null) config.LocationRules = defaults.LocationRules;
            if (config.Paste == null) config.Paste = defaults.Paste;
            if (config.MapMarker == null) config.MapMarker = defaults.MapMarker;
            if (config.Scoring == null) config.Scoring = defaults.Scoring;
            if (config.Rewards == null) config.Rewards = defaults.Rewards;
            if (config.Purchase == null) config.Purchase = defaults.Purchase;
            if (config.Cleanup == null) config.Cleanup = defaults.Cleanup;
            if (string.IsNullOrWhiteSpace(config.ChatPrefix)) config.ChatPrefix = defaults.ChatPrefix;

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

            config.LocationRules.RandomSearchAttempts = Mathf.Clamp(config.LocationRules.RandomSearchAttempts, 1, 500);
            config.LocationRules.MinDistanceFromMapEdge = Mathf.Max(0f, config.LocationRules.MinDistanceFromMapEdge);
            config.LocationRules.WaterClearance = Mathf.Max(0f, config.LocationRules.WaterClearance);
            config.LocationRules.SafeZoneRadius = Mathf.Max(0f, config.LocationRules.SafeZoneRadius);
            config.LocationRules.MonumentRadiusPadding = Mathf.Max(0f, config.LocationRules.MonumentRadiusPadding);
            config.LocationRules.DefaultMonumentRadius = Mathf.Max(1f, config.LocationRules.DefaultMonumentRadius);
            config.LocationRules.RoadCheckHeight = Mathf.Max(1f, config.LocationRules.RoadCheckHeight);
            config.LocationRules.RoadCheckDepth = Mathf.Max(1f, config.LocationRules.RoadCheckDepth);
            config.LocationRules.PlayerBaseRadius = Mathf.Max(1f, config.LocationRules.PlayerBaseRadius);
            config.LocationRules.MinimumDistanceBetweenEvents = Mathf.Max(0f, config.LocationRules.MinimumDistanceBetweenEvents);
            config.LocationRules.MaxSlope = Mathf.Clamp(config.LocationRules.MaxSlope, 0.01f, 2f);
            config.LocationRules.FlatnessSampleRadius = Mathf.Max(1f, config.LocationRules.FlatnessSampleRadius);
            config.LocationRules.MaxFlatnessDelta = Mathf.Max(0f, config.LocationRules.MaxFlatnessDelta);
            config.LocationRules.OverlapPadding = Mathf.Max(0f, config.LocationRules.OverlapPadding);

            if (config.Paste.CopyPasteArguments == null)
                config.Paste.CopyPasteArguments = defaults.Paste.CopyPasteArguments;
            config.Paste.RandomRotationDegreesStep = Mathf.Max(0f, config.Paste.RandomRotationDegreesStep);
            config.Paste.GroundClearance = Mathf.Max(0f, config.Paste.GroundClearance);
            if (config.Paste.PastedTurretAttackAllReapplyDelaysSeconds == null || config.Paste.PastedTurretAttackAllReapplyDelaysSeconds.Length == 0)
                config.Paste.PastedTurretAttackAllReapplyDelaysSeconds = defaults.Paste.PastedTurretAttackAllReapplyDelaysSeconds;

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

            if (string.IsNullOrWhiteSpace(config.Purchase.Permission))
                config.Purchase.Permission = PurchasePermission;
            if (string.IsNullOrWhiteSpace(config.Purchase.DefaultLayoutId))
                config.Purchase.DefaultLayoutId = defaults.Purchase.DefaultLayoutId;
            if (config.Purchase.AllowedLayoutIds == null)
                config.Purchase.AllowedLayoutIds = new List<string>();
            config.Purchase.AllowedLayoutIds = config.Purchase.AllowedLayoutIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToList();
            config.Purchase.MinOnlinePlayers = Math.Max(0, config.Purchase.MinOnlinePlayers);
            config.Purchase.CooldownMinutesPerPlayer = Mathf.Max(0f, config.Purchase.CooldownMinutesPerPlayer);
            config.Purchase.CooldownMinutesGlobal = Mathf.Max(0f, config.Purchase.CooldownMinutesGlobal);
            if (config.Purchase.Costs == null)
                config.Purchase.Costs = defaults.Purchase.Costs;
            config.Purchase.Costs = config.Purchase.Costs
                .Where(cost => cost != null && cost.Amount > 0 && !string.IsNullOrWhiteSpace(cost.Type))
                .Select(cost => new PurchaseCostConfig
                {
                    Type = NormalizeCostType(cost.Type),
                    ShortName = string.IsNullOrWhiteSpace(cost.ShortName) ? null : cost.ShortName.Trim(),
                    Amount = Math.Max(0, cost.Amount),
                    DisplayName = string.IsNullOrWhiteSpace(cost.DisplayName) ? null : cost.DisplayName.Trim()
                })
                .Where(cost => cost.Amount > 0 && IsSupportedPurchaseCost(cost))
                .ToList();

            config.Cleanup.CompletionCleanupDelaySeconds = Mathf.Max(0f, config.Cleanup.CompletionCleanupDelaySeconds);
            config.Cleanup.ForcedCleanupTimeoutSeconds = Mathf.Max(60f, config.Cleanup.ForcedCleanupTimeoutSeconds);
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
            permission.RegisterPermission(LayoutPermission, this);
            permission.RegisterPermission(StartPermission, this);
            permission.RegisterPermission(StopPermission, this);
            permission.RegisterPermission(PurchasePermission, this);
            if (config?.Purchase != null && !string.Equals(config.Purchase.Permission, PurchasePermission, StringComparison.OrdinalIgnoreCase))
                permission.RegisterPermission(config.Purchase.Permission, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RebuildEntityIndex();
            ManageActiveEventSentries();
            ScanLayouts(true);
            RestoreMarkers();
            ScheduleAutoSpawn();
            StartExpiryTimer();
            timer.Once(5f, () => RetryPendingRewards());
            timer.Once(7f, () => RetryPendingPurchaseRefunds());
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null)
                return;

            if (string.Equals(plugin.Name, "ServerRewards", StringComparison.OrdinalIgnoreCase))
            {
                timer.Once(2f, () => RetryPendingRewards());
                timer.Once(3f, () => RetryPendingPurchaseRefunds());
                return;
            }

            if (string.Equals(plugin.Name, "RaidlandsSentryTurrets", StringComparison.OrdinalIgnoreCase))
                timer.Once(0.5f, () => ManageActiveEventSentries());
        }

        private void Unload()
        {
            autoSpawnTimer?.Destroy();
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
            if (data.LastPurchaseByPlayer == null)
                data.LastPurchaseByPlayer = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (data.PendingPurchaseRefunds == null)
                data.PendingPurchaseRefunds = new Dictionary<string, PendingPurchaseRefundRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (var active in data.ActiveRaidBases.Values)
                NormalizeActiveRaidBase(active);
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
            if (active.PurchaseCostsPaid == null)
                active.PurchaseCostsPaid = new List<PurchaseCostRecord>();
            if (string.IsNullOrWhiteSpace(active.TriggerType))
                active.TriggerType = string.IsNullOrWhiteSpace(active.PurchaserUserId) ? "admin" : "purchase";
            if (active.ScoreRadiusMeters <= 0f)
                active.ScoreRadiusMeters = config?.Scoring?.ScoreRadiusMeters > 0f ? config.Scoring.ScoreRadiusMeters : 120f;
        }

        [ConsoleCommand("revents.status")]
        private void CommandStatus(ConsoleSystem.Arg arg)
        {
            if (!HasAccess(arg, AdminPermission))
                return;

            Reply(arg, BuildStatusMessage(true));
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
                Reply(arg, $"Auto-spawn is {(config.AutoSpawn.Enabled ? "on" : "off")}. Usage: revents.auto on|off");
                return;
            }

            config.AutoSpawn.Enabled = args[0].Equals("on", StringComparison.OrdinalIgnoreCase);
            SaveConfig();
            ScheduleAutoSpawn();
            Reply(arg, $"RaidlandsEvents auto-spawn is now {(config.AutoSpawn.Enabled ? "on" : "off")}.");
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
            if (!HasAccess(arg, AdminPermission))
                return;

            var args = GetArgs(arg);
            var subcommand = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            switch (subcommand)
            {
                case "list":
                    Reply(arg, BuildPendingRewardsMessage());
                    break;

                case "retry":
                    var paid = RetryPendingRewards();
                    Reply(arg, $"Retried pending RaidlandsEvents rewards: paid={paid}, remaining={data.PendingRewards.Count}.");
                    break;

                default:
                    Reply(arg, "Usage: revents.rewards list|retry");
                    break;
            }
        }

        [ConsoleCommand("revents.purchase")]
        private void CommandPurchase(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Reply(arg, "Player purchases must be run by an in-game player. Use revents.start for server/admin starts.");
                return;
            }

            if (!HasPurchaseAccess(player))
            {
                Reply(arg, "You do not have permission to purchase RaidlandsEvents.");
                return;
            }

            var args = GetArgs(arg);
            var layoutId = args.Length > 0 ? args[0] : config.Purchase.DefaultLayoutId;
            string message;
            TryPurchaseRaidBase(player, layoutId, out message);
            Reply(arg, message);
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
                    Reply(arg, BuildPurchaseStatusMessage());
                    break;

                case "refunds":
                    Reply(arg, BuildPendingPurchaseRefundsMessage());
                    break;

                case "retry":
                    var refunded = RetryPendingPurchaseRefunds();
                    Reply(arg, $"Retried pending RaidlandsEvents purchase refunds: refunded={refunded}, remaining={data.PendingPurchaseRefunds.Count}.");
                    break;

                case "balance":
                    Reply(arg, BuildPurchaseBalanceMessage(arg, args.Length > 1 ? args[1] : null));
                    break;

                default:
                    Reply(arg, "Usage: revents.purchases status|refunds|retry|balance [playerNameOrSteamId]");
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
                SendReply(player, $"{config.ChatPrefix} {BuildStatusMessage(false)}\nCommands: revents.status, revents.layouts scan|list|enable|disable, revents.start <layoutId|random> here|random, revents.score <instanceId|all>, revents.rewards list|retry, revents.purchases status|refunds|retry|balance, revents.auto on|off, revents.stop <instanceId|all>, revents.cleanup.");
                return;
            }

            SendReply(player, $"{config.ChatPrefix} Use console commands for this MVP: revents.status, revents.layouts, revents.start, revents.score, revents.rewards, revents.purchases, revents.auto, revents.stop, revents.cleanup.");
        }

        [ChatCommand("raidme")]
        private void ChatCommandRaidMe(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            SendReply(player, $"{config.ChatPrefix} /raidme is reserved for the future player-base defense raid mode. It will target your own base for bot/player raids once that mode is implemented. For the current public CopyPaste raid-base event, use /eventbuy [layoutId|random] or /raidbase [layoutId|random]. No RP or items were charged.");
        }

        [ChatCommand("eventbuy")]
        private void ChatCommandEventBuy(BasePlayer player, string command, string[] args)
        {
            ChatCommandPurchaseRaidBase(player, command, args);
        }

        [ChatCommand("raidbase")]
        private void ChatCommandRaidBase(BasePlayer player, string command, string[] args)
        {
            ChatCommandPurchaseRaidBase(player, command, args);
        }

        private void ChatCommandPurchaseRaidBase(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!HasPurchaseAccess(player))
            {
                SendReply(player, $"{config.ChatPrefix} You do not have permission to purchase public raid-base events.");
                return;
            }

            if (args != null && args.Length > 0 && args[0].Equals("balance", StringComparison.OrdinalIgnoreCase))
            {
                SendReply(player, $"{config.ChatPrefix} {BuildPlayerPurchaseBalanceMessage(player)}");
                return;
            }

            var layoutId = args != null && args.Length > 0 ? args[0] : config.Purchase.DefaultLayoutId;
            string message;
            TryPurchaseRaidBase(player, layoutId, out message);
            SendReply(player, $"{config.ChatPrefix} {message}");
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
                case "refresh":
                    break;

                case "close":
                    DestroyEventsManagerUi(player);
                    return;

                case "scan":
                    var count = ScanLayouts(true);
                    SendReply(player, $"{config.ChatPrefix} Scanned {count} CopyPaste layout(s).");
                    break;

                case "auto":
                    if (args.Length >= 2)
                    {
                        config.AutoSpawn.Enabled = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                        SaveConfig();
                        ScheduleAutoSpawn();
                        SendReply(player, $"{config.ChatPrefix} Auto-spawn is now {(config.AutoSpawn.Enabled ? "on" : "off")}.");
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
                    var paidRewards = RetryPendingRewards();
                    SendReply(player, $"{config.ChatPrefix} Retried pending rewards: paid={paidRewards}, remaining={data.PendingRewards.Count}.");
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

        private bool TryPurchaseRaidBase(BasePlayer player, string requestedLayoutId, out string message)
        {
            message = null;

            if (player == null)
            {
                message = "Player was not found.";
                return false;
            }

            if (config?.Purchase?.Enabled != true)
            {
                message = "Player-purchased raid-base events are disabled.";
                return false;
            }

            if (!HasPurchaseAccess(player))
            {
                message = "You do not have permission to purchase public raid-base events.";
                return false;
            }

            if (player.IsDead() || player.IsWounded())
            {
                message = "You cannot purchase an event while dead or wounded.";
                return false;
            }

            if (BasePlayer.activePlayerList.Count < config.Purchase.MinOnlinePlayers)
            {
                message = $"At least {config.Purchase.MinOnlinePlayers} online player(s) are required to purchase an event.";
                return false;
            }

            var now = NowUnix();
            var globalRemaining = PurchaseCooldownRemaining(data.LastPurchaseUnix, config.Purchase.CooldownMinutesGlobal, now);
            if (globalRemaining > 0)
            {
                message = $"Public raid-base purchases are on global cooldown for {FormatDuration(globalRemaining)}.";
                return false;
            }

            double playerLastPurchase;
            if (data.LastPurchaseByPlayer.TryGetValue(player.UserIDString, out playerLastPurchase))
            {
                var playerRemaining = PurchaseCooldownRemaining(playerLastPurchase, config.Purchase.CooldownMinutesPerPlayer, now);
                if (playerRemaining > 0)
                {
                    message = $"Your public raid-base purchase cooldown has {FormatDuration(playerRemaining)} remaining.";
                    return false;
                }
            }

            LayoutScanEntry layout;
            if (!TrySelectPurchasableLayout(requestedLayoutId, out layout, out message))
                return false;

            List<PurchaseCostRecord> paidCosts;
            if (!TryChargePurchaseCosts(player, out paidCosts, out message))
                return false;

            var purchase = new PurchaseStartContext
            {
                Purchaser = player,
                UserId = player.UserIDString,
                DisplayName = player.displayName ?? player.UserIDString,
                CostsPaid = paidCosts
            };

            string startMessage;
            if (!StartRaidBase(layout.LayoutId, true, Vector3.zero, out startMessage, purchase))
            {
                var refundMessage = config.Purchase.RefundOnStartFailure
                    ? RefundPurchaseCostsOrQueue(null, player.UserIDString, player.displayName, player, paidCosts, $"start failed: {startMessage}")
                    : "Purchase costs were not refunded because Refund On Start Failure is disabled.";
                message = $"Purchase failed: {startMessage} {refundMessage}";
                return false;
            }

            data.LastPurchaseUnix = now;
            data.LastPurchaseByPlayer[player.UserIDString] = now;
            SaveData();

            var costSummary = BuildCostSummary(paidCosts);
            message = $"{startMessage} Cost: {costSummary}. This is public and counterable; the purchaser does not reserve loot or rewards.";
            return true;
        }

        private bool TrySelectPurchasableLayout(string requestedLayoutId, out LayoutScanEntry layout, out string reason)
        {
            layout = null;
            reason = null;

            var layoutId = string.IsNullOrWhiteSpace(requestedLayoutId) ? config.Purchase.DefaultLayoutId : requestedLayoutId.Trim();
            if (string.IsNullOrWhiteSpace(layoutId))
                layoutId = "random";

            if (layoutId.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                if (!config.Purchase.AllowRandomLayout)
                {
                    reason = "Random purchased layouts are disabled.";
                    return false;
                }

                var candidates = PurchasableLayouts();
                if (candidates.Count == 0)
                {
                    reason = "No enabled purchasable layouts are available.";
                    return false;
                }

                layout = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                return true;
            }

            string selectionReason;
            if (!TrySelectLayout(layoutId, out layout, out selectionReason))
            {
                reason = selectionReason;
                return false;
            }

            if (!IsPurchasableLayout(layout.LayoutId))
            {
                reason = $"Layout '{layout.LayoutId}' is not allowed for player purchases.";
                return false;
            }

            return true;
        }

        private List<LayoutScanEntry> PurchasableLayouts()
        {
            return EnabledValidLayouts()
                .Where(layout => layout != null && IsPurchasableLayout(layout.LayoutId))
                .ToList();
        }

        private bool IsPurchasableLayout(string layoutId)
        {
            if (string.IsNullOrWhiteSpace(layoutId))
                return false;

            var allowed = config?.Purchase?.AllowedLayoutIds;
            return allowed == null || allowed.Count == 0 || allowed.Any(value => value.Equals(layoutId, StringComparison.OrdinalIgnoreCase));
        }

        private bool TryChargePurchaseCosts(BasePlayer player, out List<PurchaseCostRecord> paidCosts, out string message)
        {
            paidCosts = new List<PurchaseCostRecord>();
            message = null;

            if (player == null)
            {
                message = "Player was not found.";
                return false;
            }

            var costs = BuildPurchaseCostRecords(config?.Purchase?.Costs);
            foreach (var cost in costs)
            {
                if (!CanPayPurchaseCost(player, cost, out message))
                    return false;
            }

            foreach (var cost in costs)
            {
                var record = ClonePurchaseCost(cost);
                if (record == null || record.Amount <= 0)
                    continue;

                if (record.Type == "RP")
                {
                    string error;
                    if (!TryTakeServerRewardsPoints(player, record.Amount, out error))
                    {
                        RefundPurchaseCostsOrQueue(null, player.UserIDString, player.displayName, player, paidCosts, $"partial purchase charge failed: {error}");
                        message = $"Could not charge RP: {error}";
                        return false;
                    }

                    paidCosts.Add(record);
                    continue;
                }

                if (record.Type == "Item")
                {
                    var definition = ItemManager.FindItemDefinition(record.ShortName);
                    if (definition == null)
                    {
                        RefundPurchaseCostsOrQueue(null, player.UserIDString, player.displayName, player, paidCosts, $"partial purchase charge failed: missing item {record.ShortName}");
                        message = $"Unknown item cost '{record.ShortName}'.";
                        return false;
                    }

                    var taken = player.inventory.Take(null, definition.itemid, record.Amount);
                    if (taken < record.Amount)
                    {
                        if (taken > 0)
                        {
                            paidCosts.Add(new PurchaseCostRecord
                            {
                                Type = "Item",
                                ShortName = record.ShortName,
                                Amount = taken,
                                DisplayName = $"{taken:n0} {record.ShortName}"
                            });
                        }

                        RefundPurchaseCostsOrQueue(null, player.UserIDString, player.displayName, player, paidCosts, "partial item charge failed");
                        message = $"Could not remove {record.Amount:n0} {record.ShortName} from your inventory.";
                        return false;
                    }

                    player.Command("note.inv", definition.itemid, -record.Amount);
                    paidCosts.Add(record);
                }
            }

            message = "Purchase cost charged.";
            return true;
        }

        private bool CanPayPurchaseCost(BasePlayer player, PurchaseCostRecord cost, out string message)
        {
            message = null;
            if (cost == null || cost.Amount <= 0)
                return true;

            var type = NormalizeCostType(cost.Type);
            if (type == "RP")
            {
                if (ServerRewards == null || !ServerRewards.IsLoaded)
                {
                    message = "ServerRewards is not loaded, so RP event purchases are unavailable.";
                    return false;
                }

                string checkDetails;
                var points = CheckServerRewardsPoints(player, out checkDetails);
                if (points < cost.Amount)
                {
                    message = $"You need {cost.Amount:n0} RP to purchase this event. Purchaser={PlayerLabel(player)}, current RP={points:n0}. ServerRewards checks: {checkDetails}.";
                    return false;
                }

                return true;
            }

            if (type == "Item")
            {
                var definition = ItemManager.FindItemDefinition(cost.ShortName);
                if (definition == null)
                {
                    message = $"Purchase item cost '{cost.ShortName}' is not a valid Rust item shortname.";
                    return false;
                }

                var amount = player.inventory.GetAmount(definition.itemid);
                if (amount < cost.Amount)
                {
                    message = $"You need {cost.Amount:n0} {cost.ShortName} to purchase this event. Current amount: {amount:n0}.";
                    return false;
                }

                return true;
            }

            message = $"Unsupported purchase cost type '{cost.Type}'.";
            return false;
        }

        private string RefundPurchaseCostsOrQueue(string instanceId, string userId, string displayName, BasePlayer player, List<PurchaseCostRecord> costs, string reason)
        {
            if (costs == null || costs.Count == 0)
                return "No purchase costs were charged.";

            string error;
            if (TryRefundPurchaseCosts(userId, displayName, player, costs, out error))
                return "Purchase costs were refunded.";

            QueuePurchaseRefund(instanceId, userId, displayName, costs, reason, error);
            return $"Purchase refund queued: {error}";
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

            DestroyEventsManagerUi(player);

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
            AddUiButton(container, panel, "Start Random", "revents.ui start random random", 0.265f, 0.802f, 0.405f, 0.85f, "0.16 0.32 0.24 0.96", 10);
            AddUiButton(container, panel, config.AutoSpawn.Enabled ? "Auto Off" : "Auto On", config.AutoSpawn.Enabled ? "revents.ui auto off" : "revents.ui auto on", 0.415f, 0.802f, 0.525f, 0.85f, config.AutoSpawn.Enabled ? "0.36 0.20 0.12 0.96" : "0.14 0.30 0.22 0.96", 10);
            AddUiButton(container, panel, "Stop All", "revents.ui stop all", 0.535f, 0.802f, 0.645f, 0.85f, "0.36 0.18 0.16 0.96", 10);
            AddUiButton(container, panel, "Cleanup", "revents.ui cleanup", 0.655f, 0.802f, 0.765f, 0.85f, "0.36 0.18 0.16 0.96", 10);
            AddUiButton(container, panel, "Retry RP", "revents.ui retryrewards", 0.775f, 0.802f, 0.865f, 0.85f, "0.16 0.30 0.42 0.96", 9);
            AddUiLabel(container, panel, ShortUiText(BuildStatusMessage(false), 96), 0.875f, 0.802f, 0.955f, 0.85f, 8, TextAnchor.MiddleRight, "0.66 0.72 0.78 1");

            BuildActiveEventsUi(container, panel);
            BuildLayoutsUi(container, panel);

            if (!string.IsNullOrWhiteSpace(scoreModalInstanceId))
                BuildScoreModalUi(container, panel, scoreModalInstanceId);

            CuiHelper.AddUi(player, container);
        }

        private void BuildActiveEventsUi(CuiElementContainer container, string panel)
        {
            AddUiSection(container, panel, "Active Raid Bases", 0.03f, 0.715f, 0.97f, 0.76f);

            var activeEvents = data.ActiveRaidBases.Values.OrderBy(active => active.StartedUnix).Take(4).ToList();
            if (activeEvents.Count == 0)
            {
                AddUiLabel(container, panel, "No active raid bases.", 0.045f, 0.665f, 0.95f, 0.705f, 10, TextAnchor.MiddleLeft, "0.62 0.68 0.74 1");
                return;
            }

            var y = 0.665f;
            foreach (var active in activeEvents)
            {
                var position = active.Position.ToVector3();
                AddUiRowBackground(container, panel, y - 0.006f, y + 0.037f);
                AddUiLabel(container, panel, ShortUiText(active.InstanceId, 18), 0.045f, y, 0.185f, y + 0.032f, 9, TextAnchor.MiddleLeft, "0.92 0.95 0.98 1");
                AddUiLabel(container, panel, ShortUiText(active.LayoutId, 22), 0.19f, y, 0.355f, y + 0.032f, 9, TextAnchor.MiddleLeft, "0.76 0.82 0.88 1");
                AddUiLabel(container, panel, active.Status, 0.365f, y, 0.455f, y + 0.032f, 9, TextAnchor.MiddleLeft, "0.76 0.82 0.88 1");
                AddUiLabel(container, panel, $"ents={active.EntityIds?.Count ?? 0} tc={active.ToolCupboardId}", 0.465f, y, 0.67f, y + 0.032f, 8, TextAnchor.MiddleLeft, "0.62 0.68 0.74 1");
                AddUiLabel(container, panel, FormatVector(position), 0.68f, y, 0.775f, y + 0.032f, 8, TextAnchor.MiddleLeft, "0.62 0.68 0.74 1");
                AddUiButton(container, panel, "Score", $"revents.ui score {active.InstanceId}", 0.785f, y - 0.001f, 0.85f, y + 0.035f, "0.16 0.30 0.42 0.96", 8);
                AddUiButton(container, panel, "TP", $"revents.ui tp {active.InstanceId}", 0.855f, y - 0.001f, 0.90f, y + 0.035f, "0.16 0.30 0.42 0.96", 8);
                AddUiButton(container, panel, "End", $"revents.ui stop {active.InstanceId}", 0.905f, y - 0.001f, 0.955f, y + 0.035f, "0.40 0.16 0.14 0.96", 8);
                y -= 0.048f;
            }
        }

        private void BuildLayoutsUi(CuiElementContainer container, string panel)
        {
            AddUiSection(container, panel, "Layouts", 0.03f, 0.485f, 0.97f, 0.53f);

            var layouts = data.Layouts.Values.OrderBy(layout => layout.Ignored).ThenBy(layout => layout.LayoutId).Take(8).ToList();
            if (layouts.Count == 0)
            {
                AddUiLabel(container, panel, "No layouts discovered.", 0.045f, 0.435f, 0.95f, 0.475f, 10, TextAnchor.MiddleLeft, "0.62 0.68 0.74 1");
                return;
            }

            AddUiLabel(container, panel, "Layout", 0.045f, 0.445f, 0.245f, 0.475f, 9, TextAnchor.MiddleLeft, "0.48 0.56 0.64 1");
            AddUiLabel(container, panel, "State", 0.25f, 0.445f, 0.405f, 0.475f, 9, TextAnchor.MiddleLeft, "0.48 0.56 0.64 1");
            AddUiLabel(container, panel, "Entities", 0.41f, 0.445f, 0.50f, 0.475f, 9, TextAnchor.MiddleLeft, "0.48 0.56 0.64 1");
            AddUiLabel(container, panel, "TC", 0.515f, 0.445f, 0.575f, 0.475f, 9, TextAnchor.MiddleLeft, "0.48 0.56 0.64 1");
            AddUiLabel(container, panel, "Crate", 0.58f, 0.445f, 0.65f, 0.475f, 9, TextAnchor.MiddleLeft, "0.48 0.56 0.64 1");

            var y = 0.397f;
            foreach (var layout in layouts)
            {
                AddUiRowBackground(container, panel, y - 0.006f, y + 0.037f);
                AddUiLabel(container, panel, ShortUiText(layout.LayoutId, 28), 0.045f, y, 0.245f, y + 0.032f, 9, TextAnchor.MiddleLeft, "0.92 0.95 0.98 1");
                AddUiLabel(container, panel, LayoutUiState(layout), 0.25f, y, 0.405f, y + 0.032f, 8, TextAnchor.MiddleLeft, LayoutUiStateColor(layout));
                AddUiLabel(container, panel, layout.EntityCount.ToString(CultureInfo.InvariantCulture), 0.41f, y, 0.50f, y + 0.032f, 8, TextAnchor.MiddleLeft, "0.72 0.78 0.84 1");
                AddUiLabel(container, panel, layout.HasToolCupboard ? "Yes" : "No", 0.515f, y, 0.575f, y + 0.032f, 8, TextAnchor.MiddleLeft, layout.HasToolCupboard ? "0.60 0.86 0.65 1" : "0.92 0.48 0.42 1");
                AddUiLabel(container, panel, layout.HasCrateLikeEntity ? "Yes" : "No", 0.58f, y, 0.65f, y + 0.032f, 8, TextAnchor.MiddleLeft, layout.HasCrateLikeEntity ? "0.82 0.74 0.42 1" : "0.62 0.68 0.74 1");

                if (!layout.Ignored && layout.Valid)
                {
                    var enabled = IsEnabledLayout(layout.LayoutId);
                    AddUiButton(container, panel, enabled ? "Disable" : "Enable", enabled ? $"revents.ui disable {layout.LayoutId}" : $"revents.ui enable {layout.LayoutId}", 0.665f, y - 0.001f, 0.755f, y + 0.035f, enabled ? "0.34 0.20 0.14 0.96" : "0.14 0.30 0.22 0.96", 8);
                    AddUiButton(container, panel, "Here", enabled ? $"revents.ui start {layout.LayoutId} here" : "revents.ui refresh", 0.765f, y - 0.001f, 0.845f, y + 0.035f, enabled ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 8);
                    AddUiButton(container, panel, "Random", enabled ? $"revents.ui start {layout.LayoutId} random" : "revents.ui refresh", 0.855f, y - 0.001f, 0.95f, y + 0.035f, enabled ? "0.16 0.30 0.42 0.96" : "0.12 0.13 0.14 0.86", 8);
                }

                y -= 0.048f;
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

            var paid = active.PaidRewards?.Count ?? 0;
            AddUiLabel(container, modal, $"Rewards processed: {active.RewardsProcessed} | Paid/queued: {paid} | Global pending RP: {data.PendingRewards.Count}", 0.05f, 0.035f, 0.78f, 0.078f, 8, TextAnchor.MiddleLeft, "0.58 0.66 0.74 1");
            AddUiButton(container, modal, "Retry RP", "revents.ui retryrewards", 0.80f, 0.03f, 0.955f, 0.082f, "0.16 0.30 0.42 0.96", 9);
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

        private void DestroyEventsManagerUiForAll()
        {
            foreach (var player in BasePlayer.activePlayerList)
                DestroyEventsManagerUi(player);
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

        private string UiAnchor(float x, float y)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###}", Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        private string UiHeaderStatus()
        {
            return $"auto={(config.AutoSpawn.Enabled ? "on" : "off")} | buy={(config.Purchase.Enabled ? "on" : "off")} | scoring={(config.Scoring.Enabled ? "on" : "off")} | rewards={(config.Rewards.Enabled ? "on" : "off")} | active={ActiveEventCount()}/{config.AutoSpawn.MaxActiveRaidBases}";
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
            var files = Interface.Oxide.DataFileSystem.GetFiles(CopyPasteDirectory) ?? Array.Empty<string>();
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

                    Vector3 relativePosition;
                    if (!TryGetRelativePosition(entity, out relativePosition))
                        continue;

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

        private bool StartRaidBase(string requestedLayoutId, bool randomLocation, Vector3 requestedPosition, out string message, PurchaseStartContext purchase = null)
        {
            message = null;

            if (CopyPaste == null || !CopyPaste.IsLoaded)
            {
                message = "CopyPaste is not loaded.";
                return false;
            }

            if (ActiveEventCount() >= config.AutoSpawn.MaxActiveRaidBases)
            {
                message = $"Max active raid bases reached ({config.AutoSpawn.MaxActiveRaidBases}).";
                return false;
            }

            LayoutScanEntry layout;
            if (!TrySelectLayout(requestedLayoutId, out layout, out message))
                return false;

            Vector3 pasteOrigin;
            float rotationDegrees = RandomRotationDegrees();
            if (randomLocation)
            {
                if (!TryFindRandomLocation(layout, rotationDegrees, out pasteOrigin, out message))
                    return false;
            }
            else
            {
                if (!TryBuildPasteOrigin(layout, requestedPosition, rotationDegrees, out pasteOrigin, out message))
                    return false;
            }

            if (!ValidateLocation(layout, pasteOrigin, rotationDegrees, out message))
                return false;

            var instanceId = NewInstanceId();
            var now = NowUnix();
            var active = new ActiveRaidBase
            {
                InstanceId = instanceId,
                LayoutId = layout.LayoutId,
                PublicName = PublicDisplayName(),
                Position = new StoredVector3(pasteOrigin),
                RotationDegrees = rotationDegrees,
                StartedUnix = now,
                ExpiresUnix = now + config.Cleanup.ForcedCleanupTimeoutSeconds,
                Status = "pasting",
                HadToolCupboardInLayout = layout.HasToolCupboard,
                ScoreRadiusMeters = config.Scoring.ScoreRadiusMeters,
                TriggerType = purchase == null ? "admin" : "purchase",
                PurchaserUserId = purchase?.UserId,
                PurchaserDisplayName = purchase?.DisplayName,
                PurchaseCostsPaid = purchase?.CostsPaid?.Select(ClonePurchaseCost).ToList() ?? new List<PurchaseCostRecord>(),
                PurchaseCostSummary = purchase?.CostsPaid == null || purchase.CostsPaid.Count == 0 ? null : BuildCostSummary(purchase.CostsPaid)
            };

            data.ActiveRaidBases[instanceId] = active;
            pendingPasteInstances.Add(instanceId);
            SaveData();

            var pasteResult = CopyPaste.Call("API_RaidlandsTryTrackedPasteAtPosition", instanceId, layout.LayoutId,
                FormatVector(pasteOrigin), config.Paste.CopyPasteArguments, rotationDegrees);

            if (!IsPasteStartSuccess(pasteResult))
            {
                pendingPasteInstances.Remove(instanceId);
                data.ActiveRaidBases.Remove(instanceId);
                SaveData();
                message = pasteResult == null ? "CopyPaste tracked paste API did not respond." : pasteResult.ToString();
                return false;
            }

            data.LastRunUnix = now;
            data.NextAutoAttemptUnix = now + config.AutoSpawn.CooldownMinutesAfterRun * 60f;
            SaveData();

            message = $"Started {active.PublicName} {instanceId} using layout {layout.LayoutId} at {FormatVector(pasteOrigin)}.";
            return true;
        }

        private void OnRaidlandsTrackedPasteFinished(string trackingId, string filename, List<ulong> pastedEntityIds, object player, Vector3 startPos)
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
            var normalizedTurrets = NormalizePastedTurretsAttackAll(active.EntityIds);
            var managedSentries = ManageEventSentries(active.EntityIds);
            SchedulePastedTurretAttackAllReapply(active.EntityIds);
            RebuildEntityIndex();
            CreateOrUpdateMarker(active);
            SaveData();

            var startMessage = $"{config.ChatPrefix} {active.PublicName} has appeared on the map. Bring boom and fight for it.";
            if (string.Equals(active.TriggerType, "purchase", StringComparison.OrdinalIgnoreCase) && config.Purchase.AnnouncePurchaser && !string.IsNullOrWhiteSpace(active.PurchaserDisplayName))
                startMessage = $"{config.ChatPrefix} {active.PurchaserDisplayName} started a public {active.PublicName}. It is counterable and the reward is not reserved.";

            Server.Broadcast(startMessage);
            Puts($"Raid base event {active.InstanceId} active: layout={active.LayoutId}, entities={active.EntityIds.Count}, tc={active.ToolCupboardId}, turretsAttackAll={normalizedTurrets}, managedSentries={managedSentries}.");
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

        private void OnRaidlandsTrackedPasteFailed(string trackingId, string filename, object result, Vector3 startPos)
        {
            if (string.IsNullOrWhiteSpace(trackingId))
                return;

            ActiveRaidBase active;
            data.ActiveRaidBases.TryGetValue(trackingId, out active);
            if (active != null && string.Equals(active.TriggerType, "purchase", StringComparison.OrdinalIgnoreCase) && config?.Purchase?.RefundOnStartFailure == true)
                RefundFailedPurchase(active, $"tracked paste failed: {result}");

            pendingPasteInstances.Remove(trackingId);
            data.ActiveRaidBases.Remove(trackingId);
            SaveData();
            PrintWarning($"Tracked paste failed for {filename} ({trackingId}) at {FormatVector(startPos)}: {result}");
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

            AddRaidBaseScore(active, attacker, config.Scoring.PlayerKillPoints, score =>
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

            var damage = ScorableDamage(info, victim);
            var points = PointsFromDamage(damage, config.Scoring.PlayerDamagePointsPer100Damage);
            if (damage <= 0f && points <= 0)
                return;

            AddRaidBaseScore(active, attacker, points, score =>
            {
                score.DamageToPlayers += damage;
            });
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
            score.LastScoreUnix = NowUnix();
            update?.Invoke(score);

            var now = NowUnix();
            if (now - lastScoreSaveUnix >= 5)
            {
                lastScoreSaveUnix = now;
                SaveData();
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
                .ThenByDescending(score => score.ToolCupboardsDestroyed)
                .ThenByDescending(score => score.PlayerKills)
                .ThenByDescending(score => score.ExplosiveDamageToEventEntities)
                .ThenByDescending(score => score.DamageToEventEntities)
                .ThenByDescending(score => score.DamageToPlayers)
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
                var paid = active.PaidRewards?.Count ?? 0;
                lines.Add($"Rewards: processed={active.RewardsProcessed}, paidOrQueued={paid}, pendingGlobal={data.PendingRewards.Count}");
            }

            return string.Join("\n", lines);
        }

        private string BuildPendingRewardsMessage()
        {
            if (data.PendingRewards.Count == 0)
                return "No pending RaidlandsEvents rewards.";

            var lines = new List<string> { $"Pending RaidlandsEvents rewards: {data.PendingRewards.Count}" };
            foreach (var reward in data.PendingRewards.Values.OrderBy(reward => reward.CreatedUnix).Take(10))
            {
                lines.Add($"{reward.RewardId}: {reward.DisplayName ?? reward.UserId}, place={reward.Place}, rp={reward.ServerRewardsRp}, attempts={reward.AttemptCount}, error={reward.LastError}");
            }

            if (data.PendingRewards.Count > 10)
                lines.Add($"...and {data.PendingRewards.Count - 10} more.");

            return string.Join("\n", lines);
        }

        private void ProcessCompletionRewards(ActiveRaidBase active, List<RaidBaseScoreEntry> leaderboard)
        {
            NormalizeActiveRaidBase(active);
            if (active == null || active.RewardsProcessed)
                return;

            active.RewardsProcessed = true;
            if (config?.Rewards?.Enabled != true || config.Rewards.AwardServerRewardsRp != true)
                return;

            if (leaderboard == null)
                leaderboard = BuildLeaderboard(active, false);

            foreach (var placement in config.Rewards.PlacementRpRewards)
            {
                if (placement == null || placement.Place <= 0 || placement.ServerRewardsRp <= 0)
                    continue;

                var index = placement.Place - 1;
                if (index < 0 || index >= leaderboard.Count)
                    continue;

                var winner = leaderboard[index];
                if (winner == null || winner.TotalScore < config.Scoring.MinimumScoreToQualify)
                    continue;

                PayOrQueueReward(active, winner, placement.Place, placement.ServerRewardsRp);
            }
        }

        private void PayOrQueueReward(ActiveRaidBase active, RaidBaseScoreEntry winner, int place, int amount)
        {
            var reward = new PaidRaidBaseReward
            {
                UserId = winner.UserId,
                DisplayName = winner.DisplayName,
                Place = place,
                ServerRewardsRp = amount,
                TimestampUnix = NowUnix()
            };

            string error;
            if (TryAddServerRewardsPoints(winner.UserId, amount, out error))
            {
                reward.Status = "paid";
                active.PaidRewards.Add(reward);
                TellRewardPlayer(winner.UserId, $"{config.ChatPrefix} You earned <color=#B6F34A>{amount} RP</color> for placing #{place} in {active.PublicName}.");
                return;
            }

            reward.Status = "pending";
            reward.Error = error;
            active.PaidRewards.Add(reward);

            if (!config.Rewards.QueueRewardsIfServerRewardsMissing)
            {
                PrintWarning($"RaidlandsEvents reward failed and queueing is disabled: {winner.UserId} place={place} amount={amount}: {error}");
                return;
            }

            var rewardId = $"{active.InstanceId}:{place}:{winner.UserId}:rp";
            data.PendingRewards[rewardId] = new PendingRewardRecord
            {
                RewardId = rewardId,
                InstanceId = active.InstanceId,
                UserId = winner.UserId,
                DisplayName = winner.DisplayName,
                Place = place,
                ServerRewardsRp = amount,
                CreatedUnix = NowUnix(),
                LastAttemptUnix = NowUnix(),
                AttemptCount = 1,
                LastError = error
            };
        }

        private int RetryPendingRewards()
        {
            if (data?.PendingRewards == null || data.PendingRewards.Count == 0)
                return 0;

            var paid = 0;
            foreach (var reward in data.PendingRewards.Values.ToList())
            {
                if (reward == null || reward.ServerRewardsRp <= 0 || string.IsNullOrWhiteSpace(reward.UserId))
                    continue;

                reward.AttemptCount++;
                reward.LastAttemptUnix = NowUnix();

                string error;
                if (!TryAddServerRewardsPoints(reward.UserId, reward.ServerRewardsRp, out error))
                {
                    reward.LastError = error;
                    continue;
                }

                data.PendingRewards.Remove(reward.RewardId);
                TellRewardPlayer(reward.UserId, $"{config.ChatPrefix} Your queued RaidlandsEvents reward paid <color=#B6F34A>{reward.ServerRewardsRp} RP</color>.");
                paid++;
            }

            SaveData();
            return paid;
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
            ProcessCompletionRewards(active, leaderboard);
            active.Status = "completed";
            active.CompletedUnix = NowUnix();
            active.CompletedReason = reason;
            SaveData();

            Server.Broadcast($"{config.ChatPrefix} {CompletionSummary(active, leaderboard)}");
            if (config.Scoring.AnnounceLeaderboardOnCompletion && leaderboard.Count > 0)
                Server.Broadcast($"{config.ChatPrefix} Top raiders: {CompactLeaderboard(leaderboard)}");

            var instanceId = active.InstanceId;
            var delay = config.Cleanup.CompletionCleanupDelaySeconds;
            timer.Once(delay, () => CleanupInstance(instanceId, reason));
        }

        private void StartExpiryTimer()
        {
            expiryTimer?.Destroy();
            expiryTimer = timer.Every(60f, CheckExpiredInstances);
            CheckExpiredInstances();
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

        private void ScheduleAutoSpawn()
        {
            autoSpawnTimer?.Destroy();
            autoSpawnTimer = null;

            if (config?.AutoSpawn?.Enabled != true)
                return;

            var delay = SecondsUntilNextAutoAttempt();
            autoSpawnTimer = timer.Once(delay, RunAutoSpawnTick);
        }

        private float SecondsUntilNextAutoAttempt()
        {
            var now = NowUnix();
            if (data.NextAutoAttemptUnix > now)
                return Mathf.Max(5f, (float)(data.NextAutoAttemptUnix - now));

            var jitterSeconds = config.AutoSpawn.JitterMinutes <= 0f
                ? 0f
                : UnityEngine.Random.Range(0f, config.AutoSpawn.JitterMinutes * 60f);

            return Mathf.Max(5f, config.AutoSpawn.IntervalMinutes * 60f + jitterSeconds);
        }

        private void RunAutoSpawnTick()
        {
            try
            {
                if (CanAutoSpawn(out var reason))
                {
                    string message;
                    if (!StartRaidBase("random", true, Vector3.zero, out message))
                    {
                        Puts($"Auto-spawn skipped: {message}");
                    }
                }
                else
                {
                    Puts($"Auto-spawn skipped: {reason}");
                }
            }
            finally
            {
                var now = NowUnix();
                var intervalNext = now + config.AutoSpawn.IntervalMinutes * 60f;
                var cooldownNext = data.LastRunUnix + config.AutoSpawn.CooldownMinutesAfterRun * 60f;
                data.NextAutoAttemptUnix = Math.Max(intervalNext, cooldownNext);
                SaveData();
                ScheduleAutoSpawn();
            }
        }

        private bool CanAutoSpawn(out string reason)
        {
            reason = null;

            if (!config.AutoSpawn.Enabled)
            {
                reason = "disabled";
                return false;
            }

            if (BasePlayer.activePlayerList.Count < config.AutoSpawn.MinOnlinePlayers)
            {
                reason = $"online players {BasePlayer.activePlayerList.Count}/{config.AutoSpawn.MinOnlinePlayers}";
                return false;
            }

            if (ActiveEventCount() >= config.AutoSpawn.MaxActiveRaidBases)
            {
                reason = "max active reached";
                return false;
            }

            if (NowUnix() - data.LastRunUnix < config.AutoSpawn.CooldownMinutesAfterRun * 60f)
            {
                reason = "cooldown";
                return false;
            }

            if (EnabledValidLayouts().Count == 0)
            {
                reason = "no enabled valid layouts";
                return false;
            }

            return true;
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

        private void CleanupInstance(string instanceId, string reason)
        {
            ActiveRaidBase active;
            if (!data.ActiveRaidBases.TryGetValue(instanceId, out active))
                return;

            active.Status = "cleaning";

            if (config.Cleanup.RemoveMarkers)
                DestroyMarker(instanceId);

            if (config.Cleanup.DespawnPastedEntities)
                DespawnEntities(active.EntityIds);

            foreach (var entityId in active.EntityIds ?? new List<ulong>())
                entityToInstance.Remove(entityId);

            pendingPasteInstances.Remove(instanceId);
            data.ActiveRaidBases.Remove(instanceId);
            SaveData();
            Puts($"Cleaned raid base event {instanceId}: {reason}.");
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
            if (active == null || config.MapMarker.Enabled != true || active.Status != "active")
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

        private List<LayoutScanEntry> EnabledValidLayouts()
        {
            return data.Layouts.Values
                .Where(layout => layout != null && layout.Valid && !layout.Ignored && IsEnabledLayout(layout.LayoutId))
                .OrderBy(layout => layout.LayoutId)
                .ToList();
        }

        private bool TryFindRandomLocation(LayoutScanEntry layout, float rotationDegrees, out Vector3 pasteOrigin, out string reason)
        {
            pasteOrigin = Vector3.zero;
            reason = null;

            var worldHalfSize = WorldHalfSize();
            var margin = Mathf.Clamp(config.LocationRules.MinDistanceFromMapEdge, 0f, worldHalfSize - 50f);
            var min = -worldHalfSize + margin;
            var max = worldHalfSize - margin;

            for (var attempt = 0; attempt < config.LocationRules.RandomSearchAttempts; attempt++)
            {
                var ground = new Vector3(UnityEngine.Random.Range(min, max), 0f, UnityEngine.Random.Range(min, max));
                if (!TrySnapToGround(ground, out ground))
                    continue;

                if (!TryBuildPasteOrigin(layout, ground, rotationDegrees, out pasteOrigin, out reason))
                    continue;

                if (ValidateLocation(layout, pasteOrigin, rotationDegrees, out reason))
                    return true;
            }

            reason = $"No valid random location found after {config.LocationRules.RandomSearchAttempts} attempts. Last rejection: {reason ?? "none"}";
            return false;
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

            var min = layout.BoundsMin.ToVector3();
            pasteOrigin = groundPoint;
            pasteOrigin.y = groundPoint.y - min.y + config.Paste.GroundClearance;
            return true;
        }

        private bool ValidateLocation(LayoutScanEntry layout, Vector3 pasteOrigin, float rotationDegrees, out string reason)
        {
            reason = null;
            var ground = pasteOrigin;
            ground.y = TerrainHeight(pasteOrigin);

            if (IsNearMapEdge(ground))
            {
                reason = "too close to map edge";
                return false;
            }

            if (config.LocationRules.BlockWater && IsWaterSurface(ground))
            {
                reason = "water";
                return false;
            }

            if (config.LocationRules.BlockSafeZones && IsInSafeZone(ground))
            {
                reason = "safe zone";
                return false;
            }

            if (config.LocationRules.BlockMonuments && IsInBlockedMonument(ground, out var monumentName))
            {
                reason = $"monument {monumentName}";
                return false;
            }

            if (config.LocationRules.BlockRoads && IsRoadSurface(ground))
            {
                reason = "road";
                return false;
            }

            if (TerrainMeta.HeightMap != null && TerrainMeta.HeightMap.GetSlope(ground) > config.LocationRules.MaxSlope)
            {
                reason = "slope";
                return false;
            }

            if (!IsFlatEnough(ground))
            {
                reason = "not flat enough";
                return false;
            }

            if (config.LocationRules.BlockPlayerBases && IsNearPlayerBase(ground))
            {
                reason = "near player base";
                return false;
            }

            if (IsNearActiveEvent(ground))
            {
                reason = "near active event";
                return false;
            }

            if (layout != null && PlacementOverlapsWorld(layout, pasteOrigin, rotationDegrees))
            {
                reason = "overlaps world entities";
                return false;
            }

            return true;
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

        private bool IsInSafeZone(Vector3 position)
        {
            var colliders = Physics.OverlapSphere(position, config.LocationRules.SafeZoneRadius, PlayerBaseLayer, QueryTriggerInteraction.Collide);
            foreach (var collider in colliders)
            {
                var entity = collider == null ? null : collider.GetComponentInParent<BaseEntity>();
                var text = entity == null ? string.Empty : $"{entity.ShortPrefabName} {entity.PrefabName}".ToLowerInvariant();
                if (text.Contains("safezone") || text.Contains("bandit") || text.Contains("compound"))
                    return true;
            }

            return false;
        }

        private bool IsRoadSurface(Vector3 position)
        {
            if (RoadCheckLayer == 0)
                return false;

            var start = position + Vector3.up * config.LocationRules.RoadCheckHeight;
            var distance = config.LocationRules.RoadCheckHeight + config.LocationRules.RoadCheckDepth;
            foreach (var hit in Physics.RaycastAll(start, Vector3.down, distance, RoadCheckLayer, QueryTriggerInteraction.Ignore))
            {
                var colliderName = hit.collider == null ? string.Empty : hit.collider.name;
                if (colliderName.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
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

                var radius = GetMonumentRadius(monument) + config.LocationRules.MonumentRadiusPadding;
                if (radius <= 0f)
                    continue;

                monumentZones.Add(new MonumentZone
                {
                    Center = monument.transform.position,
                    Radius = radius,
                    Name = GetMonumentShortName(monument)
                });
            }
        }

        private float GetMonumentRadius(MonumentInfo monument)
        {
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

        private bool IsFlatEnough(Vector3 position)
        {
            if (TerrainMeta.HeightMap == null)
                return true;

            var center = TerrainMeta.HeightMap.GetHeight(position);
            var radius = config.LocationRules.FlatnessSampleRadius;
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

        private bool IsNearPlayerBase(Vector3 position)
        {
            var colliders = Physics.OverlapSphere(position, config.LocationRules.PlayerBaseRadius, PlayerBaseLayer, QueryTriggerInteraction.Collide);
            foreach (var collider in colliders)
            {
                var entity = collider == null ? null : collider.GetComponentInParent<BaseEntity>();
                if (IsPlayerBaseEntity(entity))
                    return true;
            }

            return false;
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

        private bool IsNearActiveEvent(Vector3 position)
        {
            var minDistance = config.LocationRules.MinimumDistanceBetweenEvents;
            if (minDistance <= 0f)
                return false;

            foreach (var active in data.ActiveRaidBases.Values)
            {
                var activePosition = active.Position.ToVector3();
                activePosition.y = position.y;
                if (Vector3.Distance(activePosition, position) < minDistance)
                    return true;
            }

            return false;
        }

        private bool PlacementOverlapsWorld(LayoutScanEntry layout, Vector3 pasteOrigin, float rotationDegrees)
        {
            if (OverlapLayer == 0 || layout == null)
                return false;

            var min = layout.BoundsMin.ToVector3();
            var max = layout.BoundsMax.ToVector3();
            var rotation = Quaternion.Euler(0f, rotationDegrees, 0f);
            var localCenter = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, (min.z + max.z) * 0.5f);
            var center = pasteOrigin + rotation * localCenter;
            var halfExtents = new Vector3(
                Math.Max(1f, (max.x - min.x) * 0.5f + config.LocationRules.OverlapPadding),
                Math.Max(1f, (max.y - min.y) * 0.5f + config.LocationRules.OverlapPadding),
                Math.Max(1f, (max.z - min.z) * 0.5f + config.LocationRules.OverlapPadding));

            return Physics.CheckBox(center, halfExtents, rotation, OverlapLayer, QueryTriggerInteraction.Ignore);
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
                config.LayoutRotation.EnabledLayouts.Add(layout.LayoutId);

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
            SaveConfig();
            message = $"Disabled raid base layout {layoutId}.";
            return true;
        }

        private string BuildStatusMessage(bool includeDetails)
        {
            var enabledLayouts = EnabledValidLayouts();
            var activeCount = ActiveEventCount();
            var message = $"RaidlandsEvents: auto={(config.AutoSpawn.Enabled ? "on" : "off")}, purchases={(config.Purchase.Enabled ? "on" : "off")}, scoring={(config.Scoring.Enabled ? "on" : "off")}, rewards={(config.Rewards.Enabled && config.Rewards.AwardServerRewardsRp ? "rp" : "off")}, enabledLayouts={enabledLayouts.Count}, discovered={data.Layouts.Count}, active={activeCount}/{config.AutoSpawn.MaxActiveRaidBases}, pendingPastes={pendingPasteInstances.Count}, pendingRewards={data.PendingRewards.Count}, pendingPurchaseRefunds={data.PendingPurchaseRefunds.Count}.";

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
                lines.Add($"{layout.LayoutId}: {valid}, {enabled}, {ignored}, entities={layout.EntityCount}, tc={layout.HasToolCupboard}, crateLike={layout.HasCrateLikeEntity}{errors}");
            }

            return string.Join("\n", lines);
        }

        private string BuildPurchaseStatusMessage()
        {
            var now = NowUnix();
            var purchaseCosts = BuildPurchaseCostRecords(config.Purchase.Costs);
            var configuredCostRows = config.Purchase.Costs?.Count ?? 0;
            var lines = new List<string>
            {
                $"RaidlandsEvents purchases: {(config.Purchase.Enabled ? "enabled" : "disabled")}, permission={config.Purchase.Permission}, defaultLayout={config.Purchase.DefaultLayoutId}, costs={BuildCostSummary(purchaseCosts)}, configuredCostRows={configuredCostRows}",
                $"Cooldowns: global={config.Purchase.CooldownMinutesGlobal:0.#}m ({FormatDuration(PurchaseCooldownRemaining(data.LastPurchaseUnix, config.Purchase.CooldownMinutesGlobal, now))} remaining), player={config.Purchase.CooldownMinutesPerPlayer:0.#}m, minOnline={config.Purchase.MinOnlinePlayers}, pendingRefunds={data.PendingPurchaseRefunds.Count}"
            };

            var purchasable = PurchasableLayouts();
            lines.Add($"Purchasable enabled layouts: {(purchasable.Count == 0 ? "none" : string.Join(", ", purchasable.Select(layout => layout.LayoutId)))}");
            lines.Add("Player command: /eventbuy [layoutId|random] or /raidbase [layoutId|random]. /raidme is reserved for future player-base defense raids.");
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

        private string BuildPurchaseBalanceMessage(ConsoleSystem.Arg arg, string targetText)
        {
            var player = ResolveBalanceTarget(arg, targetText);
            var label = player != null ? $"{player.displayName} ({player.UserIDString})" : targetText;
            string details;
            int balance;

            if (player != null)
            {
                balance = CheckServerRewardsPoints(player, out details);
            }
            else if (!string.IsNullOrWhiteSpace(targetText))
            {
                balance = CheckServerRewardsPoints(targetText, out details);
            }
            else
            {
                return "Usage: revents.purchases balance <playerNameOrSteamId>";
            }

            return $"RaidlandsEvents ServerRewards balance for {label}: {balance:n0}. Checks: {details}. Admin command candidates: {string.Join(", ", ServerRewardsAdminCommands())}.";
        }

        private string BuildPlayerPurchaseBalanceMessage(BasePlayer player)
        {
            if (player == null)
                return "Player was not found.";

            string details;
            var balance = CheckServerRewardsPoints(player, out details);
            return $"RaidlandsEvents sees purchaser={PlayerLabel(player)}, ServerRewards balance={balance:n0}. Checks: {details}.";
        }

        private string PlayerLabel(BasePlayer player)
        {
            if (player == null)
                return "unknown";

            return $"{player.displayName ?? "unknown"} ({player.UserIDString})";
        }

        private BasePlayer ResolveBalanceTarget(ConsoleSystem.Arg arg, string targetText)
        {
            if (string.IsNullOrWhiteSpace(targetText))
                return arg?.Player();

            ulong parsed;
            if (ulong.TryParse(targetText, out parsed))
                return BasePlayer.FindByID(parsed) ?? BasePlayer.FindSleeping(parsed);

            var lowered = targetText.ToLowerInvariant();
            return BasePlayer.activePlayerList.FirstOrDefault(player =>
                player != null &&
                (player.displayName ?? string.Empty).ToLowerInvariant().Contains(lowered));
        }

        private int ActiveEventCount()
        {
            return data.ActiveRaidBases.Values.Count(active => active.Status != "cleaning");
        }

        private bool IsEnabledLayout(string layoutId)
        {
            return config.LayoutRotation.EnabledLayouts.Any(value => value.Equals(layoutId, StringComparison.OrdinalIgnoreCase));
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

        private bool IsToolCupboardPrefab(string prefab)
        {
            return !string.IsNullOrWhiteSpace(prefab) &&
                   prefab.IndexOf("cupboard.tool", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private bool IsSupportedPurchaseCost(PurchaseCostConfig cost)
        {
            if (cost == null || cost.Amount <= 0)
                return false;

            var type = NormalizeCostType(cost.Type);
            if (type == "RP")
                return true;

            return type == "Item" && !string.IsNullOrWhiteSpace(cost.ShortName);
        }

        private PurchaseCostRecord ToPurchaseCostRecord(PurchaseCostConfig cost)
        {
            if (cost == null || cost.Amount <= 0)
                return null;

            var type = NormalizeCostType(cost.Type);
            if (type == "RP")
            {
                return new PurchaseCostRecord
                {
                    Type = "RP",
                    Amount = cost.Amount,
                    DisplayName = string.IsNullOrWhiteSpace(cost.DisplayName) ? $"{cost.Amount:n0} RP" : cost.DisplayName
                };
            }

            if (type == "Item" && !string.IsNullOrWhiteSpace(cost.ShortName))
            {
                return new PurchaseCostRecord
                {
                    Type = "Item",
                    ShortName = cost.ShortName.Trim(),
                    Amount = cost.Amount,
                    DisplayName = string.IsNullOrWhiteSpace(cost.DisplayName) ? $"{cost.Amount:n0} {cost.ShortName.Trim()}" : cost.DisplayName
                };
            }

            return null;
        }

        private List<PurchaseCostRecord> BuildPurchaseCostRecords(IEnumerable<PurchaseCostConfig> costs)
        {
            var source = (costs ?? Enumerable.Empty<PurchaseCostConfig>())
                .Select(ToPurchaseCostRecord)
                .Where(cost => cost != null && cost.Amount > 0)
                .ToList();

            var result = new List<PurchaseCostRecord>();
            var rpTotal = source
                .Where(cost => cost.Type == "RP")
                .Sum(cost => cost.Amount);

            if (rpTotal > 0)
            {
                result.Add(new PurchaseCostRecord
                {
                    Type = "RP",
                    Amount = rpTotal,
                    DisplayName = $"{rpTotal:n0} RP"
                });
            }

            foreach (var group in source
                         .Where(cost => cost.Type == "Item" && !string.IsNullOrWhiteSpace(cost.ShortName))
                         .GroupBy(cost => cost.ShortName.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var shortName = group.Key;
                var total = group.Sum(cost => cost.Amount);
                if (total <= 0)
                    continue;

                result.Add(new PurchaseCostRecord
                {
                    Type = "Item",
                    ShortName = shortName,
                    Amount = total,
                    DisplayName = $"{total:n0} {shortName}"
                });
            }

            return result;
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

        private int CheckServerRewardsPoints(BasePlayer player, out string details)
        {
            details = "unavailable";

            if (ServerRewards == null || !ServerRewards.IsLoaded || player == null)
                return 0;

            ulong parsedUserId;
            var hasParsedUserId = ulong.TryParse(player.UserIDString, out parsedUserId);
            var values = new List<int>();
            var attempts = new List<string>();

            TryReadServerRewardsPoints("BasePlayer", () => ServerRewards.Call("CheckPoints", player), values, attempts);
            TryReadServerRewardsPoints("player.userID", () => ServerRewards.Call("CheckPoints", player.userID), values, attempts);

            if (hasParsedUserId)
                TryReadServerRewardsPoints("parsed ulong", () => ServerRewards.Call("CheckPoints", parsedUserId), values, attempts);

            TryReadServerRewardsPoints("UserIDString", () => ServerRewards.Call("CheckPoints", player.UserIDString), values, attempts);

            details = attempts.Count == 0 ? "none" : string.Join("; ", attempts);
            return values.Count == 0 ? 0 : values.Max();
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

        private bool TryTakeServerRewardsPoints(BasePlayer player, int amount, out string error)
        {
            error = null;
            if (amount <= 0)
                return true;

            if (player == null)
            {
                error = "Player was not found for ServerRewards debit.";
                return false;
            }

            if (ServerRewards == null || !ServerRewards.IsLoaded)
            {
                error = "ServerRewards plugin is not loaded.";
                return false;
            }

            ulong parsedUserId;
            var hasParsedUserId = ulong.TryParse(player.UserIDString, out parsedUserId);
            var attempts = new List<string>();
            string beforeDetails;
            var balanceBefore = CheckServerRewardsPoints(player, out beforeDetails);

            if (balanceBefore < amount)
            {
                error = $"Insufficient RP for {PlayerLabel(player)}: required {amount:n0}, ServerRewards balance {balanceBefore:n0}. Checks: {beforeDetails}.";
                return false;
            }

            if (TryCallServerRewardsBool("BasePlayer", () => ServerRewards.Call("TakePoints", player, amount), attempts))
                return true;

            if (TryCallServerRewardsBool("player.userID", () => ServerRewards.Call("TakePoints", player.userID, amount), attempts))
                return true;

            if (hasParsedUserId && TryCallServerRewardsBool("parsed ulong", () => ServerRewards.Call("TakePoints", parsedUserId, amount), attempts))
                return true;

            if (TryCallServerRewardsBool("UserIDString", () => ServerRewards.Call("TakePoints", player.UserIDString, amount), attempts))
                return true;

            if (TryTakeServerRewardsPointsViaCommand(player, amount, balanceBefore, attempts))
                return true;

            string afterDetails;
            var balanceAfter = CheckServerRewardsPoints(player, out afterDetails);
            error = $"ServerRewards rejected the RP debit for {PlayerLabel(player)}. Amount={amount:n0}, balanceBefore={balanceBefore:n0}, balanceAfter={balanceAfter:n0}. Checks before: {beforeDetails}. Checks after: {afterDetails}. Attempts: {string.Join("; ", attempts)}.";
            return false;
        }

        private bool TryTakeServerRewardsPointsViaCommand(BasePlayer player, int amount, int balanceBefore, List<string> attempts)
        {
            foreach (var rpCommand in ServerRewardsAdminCommands())
            {
                try
                {
                    var command = $"{rpCommand} take {player.UserIDString} {amount}";
                    var result = ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command);
                    string afterDetails;
                    var balanceAfter = CheckServerRewardsPoints(player, out afterDetails);
                    var expectedAfter = balanceBefore - amount;

                    attempts.Add($"{rpCommand} command result={FormatServerRewardsResult(result)}, before={balanceBefore:n0}, after={balanceAfter:n0}, expectedAfter={expectedAfter:n0}, checks={afterDetails}");
                    if (balanceAfter == expectedAfter)
                        return true;
                }
                catch (Exception exception)
                {
                    attempts.Add($"{rpCommand} command error({exception.Message})");
                }
            }

            return false;
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

        private double PurchaseCooldownRemaining(double lastRunUnix, float cooldownMinutes, double now)
        {
            if (lastRunUnix <= 0 || cooldownMinutes <= 0f)
                return 0;

            return Math.Max(0, lastRunUnix + cooldownMinutes * 60f - now);
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

        private bool HasPurchaseAccess(BasePlayer player)
        {
            if (player == null)
                return false;

            return player.IsAdmin
                   || permission.UserHasPermission(player.UserIDString, AdminPermission)
                   || permission.UserHasPermission(player.UserIDString, PurchasePermission)
                   || permission.UserHasPermission(player.UserIDString, config.Purchase.Permission);
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
