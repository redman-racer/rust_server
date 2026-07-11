using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

#pragma warning disable 0649

namespace Oxide.Plugins
{
    [Info("PortableAirstrikes", "Raidlands", "0.1.52")]
    [Description("Configurable single-use CID binocular airstrike selection, automatic targeting pings, persisted manual default strikes, validation, terrain-aware, more believable multi-phase visual delivery flyovers with autoload-safe repeated sound cues, direct-command execution, audit logging, webhooks, warning markers, in-game warnings, and warning diagnostics.")]
    public class PortableAirstrikes : RustPlugin
    {
        private const int CurrentConfigVersion = 36;
        private const int DefaultAirstrikeItemMaxStackSize = 1;
        private const int MaximumAirstrikeItemMaxStackSize = 1;
        private const int DefaultAirstrikeItemMaxChargesPerItem = 65535;
        private const int MaximumAirstrikeItemMaxChargesPerItem = 65535;
        private const int DefaultRecentCallHistoryLimit = 50;
        private const int MaxDebugHistoryRows = 10;
        private const float StrikePickerRowHeightPixels = 58f;
        private const float StrikePickerRowGapPixels = 8f;
        private const float StrikePickerContentPaddingPixels = 4f;
        private const float StrikePickerMinimumScrollContentHeight = 430f;
        private const string AdminPermission = "portableairstrikes.admin";
        private const string UsePermission = "portableairstrikes.use";
        private const string DataFileName = "PortableAirstrikes_Data";
        private const string VisualProfilesDataFileName = "PortableAirstrikes/VisualProfiles";
        private const string StrikeUiName = "PortableAirstrikes.Selection";
        private const string AdminUiName = "PortableAirstrikes.Admin";
        private const string AdminNumberEditUiName = "PortableAirstrikes.Admin.NumberEdit";
        private const string DebugRaycastSource = "debug_raycast";
        private const string MapMarkerSource = "map_marker_or_ping";
        private const string AirstrikeToolPingSource = "airstrike_tool_ping";
        private const string GenericRadiusMapMarkerPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        private const string DroneVisualPrefab = "assets/prefabs/deployable/drone/drone.deployed.prefab";
        private const string PatrolHelicopterVisualPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
        private const string CargoPlaneVisualPrefab = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
        private const string F15VisualPrefab = "assets/scripts/entity/misc/f15/f15e.prefab";
        private const string MortarVisualPrefab = "assets/prefabs/deployable/mortar/mortar.entity.prefab";
        private const string MortarCrewNpcPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/gen2/scientist2.prefab";
        private const string DroneDeployEffect = "assets/prefabs/deployable/drone/effects/drone-deploy.prefab";
        private const string VehicleFlybySoundEffect = "assets/content/sound/templates/dangerous-vehicle-engine.prefab";
        private const string BulletFlybySoundEffect = "assets/content/sound/templates/bullet-flyby.prefab";
        private const string ProjectileFlightSoundEffect = "assets/content/sound/templates/projectile-flight.prefab";
        private const string LargeFastFalloffSoundEffect = "assets/content/sound/templates/large-sound-fast-falloff.prefab";
        private const string MortarAttackMuzzleEffect = "assets/prefabs/deployable/mortar/effects/attackmuzzle.prefab";
        private const string MortarDeployEffect = "assets/prefabs/deployable/mortar/effects/mortar-deploy.prefab";
        private const string BeeGrenadePrefab = "assets/prefabs/weapons/bee grenade/grenade.bee.deployed.prefab";
        private const string BeancanGrenadePrefab = "assets/prefabs/weapons/beancan grenade/grenade.beancan.deployed.prefab";
        private const string F1GrenadePrefab = "assets/prefabs/weapons/f1 grenade/grenade.f1.deployed.prefab";
        private const string SmokeGrenadePrefab = "assets/prefabs/tools/smoke grenade/grenade.smoke.deployed.prefab";
        private const string FlashbangGrenadePrefab = "assets/prefabs/weapons/flashbang/grenade.flashbang.deployed.prefab";
        private const string MolotovGrenadePrefab = "assets/prefabs/weapons/molotov cocktail/grenade.molotov.deployed.prefab";
        private const string He40mmGrenadePrefab = "assets/prefabs/ammo/40mmgrenade/40mm_grenade_he.prefab";
        private const string MortarHeShellPrefab = "assets/prefabs/deployable/mortar/mortar_shell_basic.prefab";
        private const string MortarFragShellPrefab = "assets/prefabs/deployable/mortar/mortar_shell_fragment.prefab";
        private const string CatapultBeeProjectilePrefab = "assets/content/vehicles/siegeweapons/catapult/ammo/projectiles/boulder_bee.prefab";
        private const string CatapultFirebombProjectilePrefab = "assets/content/vehicles/siegeweapons/catapult/ammo/projectiles/boulder_incendiary.prefab";
        private const string CatapultPropaneProjectilePrefab = "assets/content/vehicles/siegeweapons/catapult/ammo/projectiles/boulder_explosive.prefab";
        private const string HvRocketPrefab = "assets/prefabs/ammo/rocket/rocket_hv.prefab";
        private const string BasicRocketPrefab = "assets/prefabs/ammo/rocket/rocket_basic.prefab";
        private const string IncendiaryRocketPrefab = "assets/prefabs/ammo/rocket/rocket_fire.prefab";
        private const string MlrsRocketPrefab = "assets/content/vehicles/mlrs/rocket_mlrs.prefab";
        private const string BradleyMainCannonAttackEffect = "assets/prefabs/npc/m2bradley/effects/maincannonattack.prefab";
        private const string BradleyMainCannonShellExplosionEffect = "assets/prefabs/npc/m2bradley/effects/maincannonshell_explosion.prefab";
        private const string BulletImpactEffect = "assets/bundled/prefabs/fx/impacts/bullet/generic/generic1.prefab";
        private const string RocketLaunchEffect = "assets/prefabs/weapons/rocketlauncher/effects/rocket_launch_fx.prefab";
        private const string MlrsBackfireEffect = "assets/content/vehicles/mlrs/effects/pfx_mlrs_backfire.prefab";
        private const float DroneDropSpawnHeight = 14f;
        private const float DroneDropProjectileSpawnHeight = 18f;
        private const float DroneDropMinimumSpawnHeight = 7f;
        private const float DroneDropMaximumTimedSpawnHeight = 16f;
        private const float DroneDropPayloadDelay = 0.35f;
        private const float DronePathMinimumLoiterSeconds = 0.75f;
        private const float DronePayloadGroundSettleSeconds = 0.85f;
        private const float AircraftObservationPassFraction = 0.38f;
        private const float AircraftReEntryPassFraction = 0.72f;
        private const float MinimumStrikePassLeadSeconds = 0.65f;
        private const float HeavyDropSpawnHeight = 85f;
        private const float HeavyDropPayloadDelay = 0.65f;
        private const float HeavyDropDownwardVelocity = 28f;
        private const float HeavyDropFinishDelaySeconds = 5.0f;
        private const float RocketRunSpawnDistance = 95f;
        private const float RocketRunSpawnHeight = 28f;
        private const float RocketRunProjectileDelay = 0.45f;
        private const float RocketRunFinishDelaySeconds = 4.5f;
        private const float MlrsRocketSpawnDistance = 160f;
        private const float MlrsRocketSpawnHeight = 75f;
        private const float MlrsRocketDelay = 0.7f;
        private const float MlrsRocketSpeed = 85f;
        private const float MlrsFinishDelaySeconds = 6.5f;
        private const float HomingMissileLaunchDistance = 120f;
        private const float HomingMissileLaunchHeight = 38f;
        private const float HomingMissileLaunchDelay = 0.65f;
        private const float HomingMissileTrackInterval = 0.08f;
        private const float HomingMissileDefaultSpeed = 82f;
        private const float HomingMissileProximityRadius = 3.5f;
        private const float HomingMissileFinishPaddingSeconds = 1.5f;
        private const float HomingMissileBaseVehicleDamage = 180f;
        private const float HomingMissileBaseSplashDamage = 60f;
        private const int HomingMissileHardCap = 8;
        private const float MortarShellSpawnHeight = 90f;
        private const float MortarShellDelay = 0.55f;
        private const float MortarShellDownwardVelocity = 42f;
        private const float MortarFinishDelaySeconds = 3.0f;
        private const float A10DefaultPulseBaseDamage = 18f;
        private const float A10FinishPaddingSeconds = 1.25f;
        private const int A10MuzzleEffectInterval = 3;
        private const float NativeStrikeMapMarkerBaseRadius = 0.015f;
        private const float NativeStrikeMapMarkerRadiusPerConfiguredMeter = 0.004f;
        private const float MinimumNativeStrikeMapMarkerRadius = 0.02f;
        private const float MaximumNativeStrikeMapMarkerRadius = 0.28f;
        private const float ToolPingDebounceSeconds = 0.75f;
        private const float ToolPingPollIntervalSeconds = 0.25f;
        private const float ToolPingFreshWindowSeconds = 2.0f;
        private const float ToolPingDuplicateWindowSeconds = 2.25f;
        private const float ToolPingVehicleAimRadius = 1.75f;
        private const float ToolPingVehicleSearchRadius = 16f;
        private const float ToolEquipHelpCooldownSeconds = 45f;
        private const float BeeGrenadeFuseSeconds = 2.5f;
        private const float BeancanFuseSeconds = 2.5f;
        private const float F1FuseSeconds = 2.5f;
        private const float FlashbangFuseSeconds = 2.0f;
        private const float MolotovFuseSeconds = 1.5f;
        private const float SmokeFinishDelaySeconds = 2.0f;
        private const float He40mmFinishDelaySeconds = 2.0f;
        private const float PayloadDownwardVelocity = 18f;
        private const float DefaultVisualMoveIntervalSeconds = 0.04f;
        private const float MinimumVisualMoveIntervalSeconds = 0.025f;
        private const float MaximumVisualMoveIntervalSeconds = 0.25f;
        private const float DefaultPayloadReleaseIntervalSeconds = 0.5f;
        private const int MaxPayloadEventsInProfile = 80;
        private const int MaxVisualProfiles = 500;
        private const int MaxVisualProfileWaypoints = 256;
        private const int MaxCompiledVisualFrames = 6000;
        private const int MaxCompiledReleaseEvents = 2000;
        private const string CompiledCoordinateSystem = "unity-target-relative-local-v1";
        private const float DefaultFlyoverSoundIntervalSeconds = 0.75f;
        private const float DefaultVisualRotationSmoothTimeSeconds = 0.18f;
        private const float MinimumVisualRotationSmoothTimeSeconds = 0.02f;
        private const float MaximumVisualRotationSmoothTimeSeconds = 0.75f;
        private const float FlightPlanTangentSampleSeconds = 0.18f;
        private const float DefaultDroneMinimumTerrainClearance = 10f;
        private const float DefaultAircraftMinimumTerrainClearance = 42f;
        private const float DefaultVisualProfileDroneTerrainClearance = 12f;
        private const float DefaultVisualProfileAircraftTerrainClearance = 55f;
        private const float DefaultPayloadMinimumTerrainClearance = 8f;
        private const float FlightPlanTerrainSampleSpacing = 28f;
        private const int AdminActivityRows = 9;
        private const int AdminStrikeRows = 10;
        private const int AdminProfileRows = 8;
        private const int AdminGiveRows = 7;

        private static readonly int TargetRaycastLayer = LayerMask.GetMask(
            "Terrain",
            "World",
            "Construction",
            "Deployed",
            "Default",
            "Vehicle Detailed",
            "Vehicle Large",
            "Player (Server)");

        private static readonly int ImpactRaycastLayer = LayerMask.GetMask(
            "Terrain",
            "World",
            "Construction",
            "Deployed",
            "Default");

        private static readonly int FlightTerrainRaycastLayer = LayerMask.GetMask(
            "Terrain",
            "World",
            "Default");

        [PluginReference]
        private Plugin ServerRewards;

        [PluginReference]
        private Plugin Economics;

        [PluginReference]
        private Plugin CustomItemDefinitions;

        [PluginReference]
        private Plugin PortableAirstrikesAnimationEditor;

        private Configuration config;
        private StoredData storedData;
        private VisualProfileFile visualProfileFile;
        private Dictionary<string, string> visualProfileMotionModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> visualProfileReleaseModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> visualProfileWarnings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string lastVisualProfileLoadMessage = "Visual profiles have not been loaded yet.";
        private string lastVisualProfileLoadAtUtc = "";
        private bool lastVisualProfileLoadSucceeded;
        private ItemDefinition airstrikeCustomItemDefinition;
        private uint airstrikeIconFileId;
        private string airstrikeIconSource = "";
        private bool warnedCIDUnavailable;
        private bool warnedIconMissing;
        private readonly Dictionary<ulong, AirstrikeTarget> latestTargets = new Dictionary<ulong, AirstrikeTarget>();
        private readonly Dictionary<ulong, AirstrikeCallContext> activeCalls = new Dictionary<ulong, AirstrikeCallContext>();
        private readonly Dictionary<ulong, RuntimePayloadRelease> payloadReleaseMetadataByEntityId = new Dictionary<ulong, RuntimePayloadRelease>();
        private readonly Dictionary<ulong, double> lastToolPingAt = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, string> lastProcessedToolPingKeyByUser = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, double> lastProcessedToolPingAtByUser = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, double> lastToolHelpAt = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, MapMarkerGenericRadius> toolTargetMarkers = new Dictionary<ulong, MapMarkerGenericRadius>();
        private readonly Dictionary<ulong, AdminUiState> adminUiStates = new Dictionary<ulong, AdminUiState>();
        private readonly List<Timer> activeTimers = new List<Timer>();
        private IStrikeExecutor strikeProfileBundleExecutor;
        private readonly Dictionary<string, IStrikeExecutor> strikeExecutors = new Dictionary<string, IStrikeExecutor>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MonumentBlockZone> monumentBlockZones = new List<MonumentBlockZone>();
        private Timer toolPingWatcherTimer;
        private bool monumentBlockZonesLoaded;
        private bool auditWebhookConfigWarningPrinted;
        private ICurrencyAdapter currencyAdapter;

        private class AdminUiState
        {
            public string Tab = "dashboard";
            public string SelectedStrikeId = "";
            public string DeleteConfirmStrikeId = "";
            public string GiveSearch = "";
            public int GiveAmount = 1;
            public int GivePage;
            public string GiveSort = "name";
            public string GiveFilter = "all";
            public string CommandScope = "chat";
            public string CommandCategory = "";
            public string Status = "";
            public PendingAdminNumberEdit NumberEdit;
        }

        private class AdminCommandHelpEntry
        {
            public string Scope;
            public string Category;
            public string CategoryLabel;
            public string Command;
            public string Detail;
            public bool AdminOnly;

            public AdminCommandHelpEntry(string scope, string category, string categoryLabel, string command, string detail, bool adminOnly = false)
            {
                Scope = scope;
                Category = category;
                CategoryLabel = categoryLabel;
                Command = command;
                Detail = detail;
                AdminOnly = adminOnly;
            }
        }

        private class PendingAdminNumberEdit
        {
            public string Field = "";
            public string Id = "";
            public string Label = "";
            public string CurrentValue = "";
            public string DraftValue = "";
            public bool HasDraft;
        }

        private enum AirstrikeTargetType
        {
            Invalid,
            GroundPing,
            VehiclePing,
            PlayerPing,
            NpcPing
        }

        private enum DeliveryVisualProfile
        {
            HeavyDrop,
            RocketRun,
            HomingMissile,
            Mlrs,
            A10
        }

        private enum StrikeExecutionState
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
            Failed,
            Refunded
        }

        private class Configuration
        {
            [JsonProperty("ConfigVersion")]
            public int ConfigVersion;

            [JsonProperty("General")]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("AirstrikeItem")]
            public AirstrikeItemSettings AirstrikeItem = new AirstrikeItemSettings();

            [JsonProperty("Currency")]
            public CurrencySettings Currency = new CurrencySettings();

            [JsonProperty("Selection")]
            public SelectionSettings Selection = new SelectionSettings();

            [JsonProperty("DeliveryScaling")]
            public DeliveryScalingSettings DeliveryScaling = new DeliveryScalingSettings();

            [JsonProperty("DeliveryVisuals")]
            public DeliveryVisualSettings DeliveryVisuals = new DeliveryVisualSettings();

            [JsonProperty("DamageScales")]
            public DamageScaleSettings DamageScales = new DamageScaleSettings();

            [JsonProperty("LootDistribution")]
            public LootDistributionSettings LootDistribution = new LootDistributionSettings();

            [JsonProperty("AuditWebhooks")]
            public AuditWebhookSettings AuditWebhooks = new AuditWebhookSettings();

            [JsonProperty("StrikeDefinitions")]
            public Dictionary<string, StrikeDefinition> StrikeDefinitions = DefaultStrikeDefinitions();

            [JsonProperty("ChatPrefix")]
            public string ChatPrefix = "<color=#ce422b>[Airstrikes]</color>";
        }

        private class GeneralSettings
        {
            [JsonProperty("RequireBinocularPing")]
            public bool RequireBinocularPing = true;

            [JsonProperty("MaxPingAgeSeconds")]
            public float MaxPingAgeSeconds = 20f;

            [JsonProperty("RequireLineOfSightToPing")]
            public bool RequireLineOfSightToPing = true;

            [JsonProperty("AllowFallbackRaycastTargeting")]
            public bool AllowFallbackRaycastTargeting;

            [JsonProperty("MaxCallRange")]
            public float MaxCallRange = 250f;

            [JsonProperty("MinimumDistanceFromCaller")]
            public float MinimumDistanceFromCaller = 25f;

            [JsonProperty("BlockSafeZones")]
            public bool BlockSafeZones = true;

            [JsonProperty("SafeZoneBlockRadius")]
            public float SafeZoneBlockRadius = 150f;

            [JsonProperty("BlockMonuments")]
            public bool BlockMonuments;

            [JsonProperty("BlockMonumentsForHeavyStrikesOnly")]
            public bool BlockMonumentsForHeavyStrikesOnly = true;

            [JsonProperty("MonumentBlockRadiusPadding")]
            public float MonumentBlockRadiusPadding = 25f;

            [JsonProperty("DefaultMonumentBlockRadius")]
            public float DefaultMonumentBlockRadius = 120f;

            [JsonProperty("BlockedMonumentNames")]
            public List<string> BlockedMonumentNames = DefaultBlockedMonumentNames();

            [JsonProperty("EnableClanCooldowns")]
            public bool EnableClanCooldowns = true;

            [JsonProperty("EnableGlobalCooldowns")]
            public bool EnableGlobalCooldowns = true;

            [JsonProperty("DefaultWarningDelaySeconds")]
            public float DefaultWarningDelaySeconds = 8f;

            [JsonProperty("UseMapMarkersForHeavyStrikes")]
            public bool UseMapMarkersForHeavyStrikes = true;

            [JsonProperty("HeavyStrikeMapMarkerSize")]
            public float HeavyStrikeMapMarkerSize = 18f;

            [JsonProperty("HeavyStrikeMapMarkerAlpha")]
            public float HeavyStrikeMapMarkerAlpha = 0.35f;

            [JsonProperty("AllowPlayerCancelBeforeImpact")]
            public bool AllowPlayerCancelBeforeImpact = true;

            [JsonProperty("RefundPlayerCancelledCallsBeforeImpact")]
            public bool RefundPlayerCancelledCallsBeforeImpact = true;

            [JsonProperty("NotifyCallerTeamOnAcceptedStrike")]
            public bool NotifyCallerTeamOnAcceptedStrike = true;

            [JsonProperty("NotifyNearbyPlayersOnHeavyStrikes")]
            public bool NotifyNearbyPlayersOnHeavyStrikes;

            [JsonProperty("NearbyHeavyStrikeWarningRadius")]
            public float NearbyHeavyStrikeWarningRadius = 120f;

            [JsonProperty("DebugMode")]
            public bool DebugMode;

            [JsonProperty("RecentCallHistoryLimit")]
            public int RecentCallHistoryLimit = DefaultRecentCallHistoryLimit;

            [JsonProperty("MaxSimultaneousStrikes")]
            public int MaxSimultaneousStrikes = 8;

            [JsonProperty("MaxSimultaneousHeavyStrikes")]
            public int MaxSimultaneousHeavyStrikes = 2;
        }

        private class AirstrikeItemSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("DisplayName")]
            public string DisplayName = "Airstrike Targeting Binoculars";

            [JsonProperty("Shortname")]
            public string Shortname = "tool.binoculars";

            [JsonProperty("UseCustomItemDefinition")]
            public bool UseCustomItemDefinition = true;

            [JsonProperty("AllowVanillaFallbackIfCIDMissing")]
            public bool AllowVanillaFallbackIfCIDMissing = true;

            [JsonProperty("CustomShortname")]
            public string CustomShortname = "raidlands.airstrike.designator";

            [JsonProperty("CustomItemId")]
            public int CustomItemId = -395118447;

            [JsonProperty("ParentShortname")]
            public string ParentShortname = "tool.binoculars";

            [JsonProperty("DefaultDescription")]
            public string DefaultDescription = "Aim with the binoculars and place a ping to call your selected airstrike.";

            [JsonProperty("IconFileId")]
            public uint IconFileId;

            [JsonProperty("IconPngDataPath")]
            public string IconPngDataPath = "PortableAirstrikes/airstrike-targeting-binoculars.png";

            [JsonProperty("ImportParentItemMods")]
            public bool ImportParentItemMods = true;

            [JsonProperty("SkinId")]
            public ulong SkinId;

            [JsonProperty("RequireCustomNameOrSkin")]
            public bool RequireCustomNameOrSkin = true;

            [JsonProperty("RequiredAmount")]
            public int RequiredAmount = 1;

            [JsonProperty("MaxStackSize")]
            public int MaxStackSize = DefaultAirstrikeItemMaxStackSize;

            [JsonProperty("MaxChargesPerItem")]
            public int MaxChargesPerItem = DefaultAirstrikeItemMaxChargesPerItem;

            [JsonProperty("ConsumeOnSuccessfulCall")]
            public bool ConsumeOnSuccessfulCall = true;

            [JsonProperty("AllowAdminsWithoutItem")]
            public bool AllowAdminsWithoutItem = true;

            [JsonProperty("TreatAsTargetingTool")]
            public bool TreatAsTargetingTool = true;

            [JsonProperty("ShowEquipInstructions")]
            public bool ShowEquipInstructions = true;

            [JsonProperty("ToolTargetMarkerEnabled")]
            public bool ToolTargetMarkerEnabled = true;

            [JsonProperty("ToolTargetMarkerDurationSeconds")]
            public float ToolTargetMarkerDurationSeconds = 18f;

            [JsonProperty("ToolTargetMarkerSize")]
            public float ToolTargetMarkerSize = 10f;

            [JsonProperty("ToolTargetMarkerAlpha")]
            public float ToolTargetMarkerAlpha = 0.55f;
        }

        private class CurrencySettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Provider")]
            public string Provider = "ServerRewards";

            [JsonProperty("AllowFreeAdminCalls")]
            public bool AllowFreeAdminCalls = true;

            [JsonProperty("VipDiscountsByPermission")]
            public Dictionary<string, float> VipDiscountsByPermission = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["portableairstrikes.discount.vip"] = 0.10f,
                ["portableairstrikes.discount.vipplus"] = 0.20f,
                ["portableairstrikes.discount.elite"] = 0.30f
            };
        }

        private class SelectionSettings
        {
            [JsonProperty("PrimaryMode")]
            public string PrimaryMode = "CUI_MENU";

            [JsonProperty("AllowDirectCommand")]
            public bool AllowDirectCommand = true;

            [JsonProperty("OpenMenuCommand")]
            public string OpenMenuCommand = "strike";

            [JsonProperty("RequireConfirmation")]
            public bool RequireConfirmation = true;

            [JsonProperty("ShowLockedStrikes")]
            public bool ShowLockedStrikes = true;

            [JsonProperty("AutoFilterByPingType")]
            public bool AutoFilterByPingType = true;

            [JsonProperty("AllowRepeatLastStrike")]
            public bool AllowRepeatLastStrike = true;
        }

        private class DeliveryScalingSettings
        {
            [JsonProperty("DroneMultiplier")]
            public int DroneMultiplier = 1;

            [JsonProperty("HeliMultiplier")]
            public int HeliMultiplier = 2;

            [JsonProperty("PlaneMultiplier")]
            public int PlaneMultiplier = 3;
        }

        private class DeliveryVisualSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("SpawnDroneVisuals")]
            public bool SpawnDroneVisuals = true;

            [JsonProperty("SpawnAircraftVisuals")]
            public bool SpawnAircraftVisuals = true;

            [JsonProperty("SpawnMortarArtilleryVisuals")]
            public bool SpawnMortarArtilleryVisuals = true;

            [JsonProperty("SpawnMortarCrewNpc")]
            public bool SpawnMortarCrewNpc = true;

            [JsonProperty("SpawnFlyoverSoundEffects")]
            public bool SpawnFlyoverSoundEffects = true;

            [JsonProperty("SpawnRotorWashEffects")]
            public bool SpawnRotorWashEffects = false;

            [JsonProperty("DroneFlyoverDistance")]
            public float DroneFlyoverDistance = 70f;

            [JsonProperty("DroneFlyoverHeight")]
            public float DroneFlyoverHeight = 28f;

            [JsonProperty("DroneErraticApproachRadius")]
            public float DroneErraticApproachRadius = 2.5f;

            [JsonProperty("DroneDropLoiterRadius")]
            public float DroneDropLoiterRadius = 9.0f;

            [JsonProperty("DronePayloadSpawnHeight")]
            public float DronePayloadSpawnHeight = 16f;

            [JsonProperty("DroneMinimumTerrainClearance")]
            public float DroneMinimumTerrainClearance = DefaultDroneMinimumTerrainClearance;

            [JsonProperty("AircraftMinimumTerrainClearance")]
            public float AircraftMinimumTerrainClearance = DefaultAircraftMinimumTerrainClearance;

            [JsonProperty("PayloadMinimumTerrainClearance")]
            public float PayloadMinimumTerrainClearance = DefaultPayloadMinimumTerrainClearance;

            [JsonProperty("AircraftFlyoverDistance")]
            public float AircraftFlyoverDistance = 330f;

            [JsonProperty("AttackHeliFlyoverHeight")]
            public float AttackHeliFlyoverHeight = 78f;

            [JsonProperty("CargoPlaneFlyoverHeight")]
            public float CargoPlaneFlyoverHeight = 145f;

            [JsonProperty("MlrsAircraftFlyoverHeight")]
            public float MlrsAircraftFlyoverHeight = 118f;

            [JsonProperty("A10FlyoverHeight")]
            public float A10FlyoverHeight = 125f;

            [JsonProperty("AircraftObservationPassHeightMultiplier")]
            public float AircraftObservationPassHeightMultiplier = 1.45f;

            [JsonProperty("AircraftStrikePassHeightMultiplier")]
            public float AircraftStrikePassHeightMultiplier = 0.86f;

            [JsonProperty("AttackDiveStartHeightMultiplier")]
            public float AttackDiveStartHeightMultiplier = 2.05f;

            [JsonProperty("AttackStrikePassHeightMultiplier")]
            public float AttackStrikePassHeightMultiplier = 0.88f;

            [JsonProperty("AttackExitHeightMultiplier")]
            public float AttackExitHeightMultiplier = 1.75f;

            [JsonProperty("MortarSourceDistance")]
            public float MortarSourceDistance = 85f;

            [JsonProperty("MortarCrewOffset")]
            public float MortarCrewOffset = 2.5f;

            [JsonProperty("VisualMoveIntervalSeconds")]
            public float VisualMoveIntervalSeconds = DefaultVisualMoveIntervalSeconds;

            [JsonProperty("VisualRotationSmoothTimeSeconds")]
            public float VisualRotationSmoothTimeSeconds = DefaultVisualRotationSmoothTimeSeconds;

            [JsonProperty("FlyoverSoundIntervalSeconds")]
            public float FlyoverSoundIntervalSeconds = DefaultFlyoverSoundIntervalSeconds;

            [JsonProperty("DeliveryVehiclesCanBeDestroyed")]
            public bool DeliveryVehiclesCanBeDestroyed = true;

            [JsonProperty("PayloadRequiresLiveDeliveryVehicle")]
            public bool PayloadRequiresLiveDeliveryVehicle = true;

            [JsonProperty("RefundIfDeliveryVehicleDestroyedBeforePayload")]
            public bool RefundIfDeliveryVehicleDestroyedBeforePayload;

            [JsonProperty("DestroyableDeliveryVehicleFirstPayloadDelaySeconds")]
            public float DestroyableDeliveryVehicleFirstPayloadDelaySeconds = 1.5f;

            [JsonProperty("DroneFirstPayloadDelaySeconds")]
            public float DroneFirstPayloadDelaySeconds = 6.0f;

            [JsonProperty("AttackHeliFirstPayloadDelaySeconds")]
            public float AttackHeliFirstPayloadDelaySeconds = 9.5f;

            [JsonProperty("CargoPlaneFirstPayloadDelaySeconds")]
            public float CargoPlaneFirstPayloadDelaySeconds = 9.0f;

            [JsonProperty("A10FirstPayloadDelaySeconds")]
            public float A10FirstPayloadDelaySeconds = 6.8f;

            [JsonProperty("MlrsFirstPayloadDelaySeconds")]
            public float MlrsFirstPayloadDelaySeconds = 7.5f;

            [JsonProperty("DroneDeliveryVehicleHealth")]
            public float DroneDeliveryVehicleHealth = 125f;

            [JsonProperty("AttackHeliDeliveryVehicleHealth")]
            public float AttackHeliDeliveryVehicleHealth = 750f;

            [JsonProperty("CargoPlaneDeliveryVehicleHealth")]
            public float CargoPlaneDeliveryVehicleHealth = 1200f;

            [JsonProperty("A10DeliveryVehicleHealth")]
            public float A10DeliveryVehicleHealth = 900f;
        }

        private class DamageScaleSettings
        {
            [JsonProperty("Players")]
            public float Players = 1f;

            [JsonProperty("Buildings")]
            public float Buildings = 1f;

            [JsonProperty("Vehicles")]
            public float Vehicles = 1f;

            [JsonProperty("Deployables")]
            public float Deployables = 1f;

            [JsonProperty("Turrets")]
            public float Turrets = 1f;
        }

        private class LootDistributionSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled;

            [JsonProperty("ContainerRules")]
            public Dictionary<string, LootContainerRule> ContainerRules = new Dictionary<string, LootContainerRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["crate_normal"] = new LootContainerRule { Chance = 0.03f, MinAmount = 1, MaxAmount = 1 },
                ["crate_elite"] = new LootContainerRule { Chance = 0.08f, MinAmount = 1, MaxAmount = 2 }
            };
        }

        private class LootContainerRule
        {
            [JsonProperty("Chance")]
            public float Chance;

            [JsonProperty("MinAmount")]
            public int MinAmount = 1;

            [JsonProperty("MaxAmount")]
            public int MaxAmount = 1;
        }

        private class AuditWebhookSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled;

            [JsonProperty("DiscordWebhookUrl")]
            public string DiscordWebhookUrl = "";

            [JsonProperty("Username")]
            public string Username = "Portable Airstrikes";

            [JsonProperty("AvatarUrl")]
            public string AvatarUrl = "";

            [JsonProperty("MentionText")]
            public string MentionText = "";

            [JsonProperty("SendStartedCalls")]
            public bool SendStartedCalls = true;

            [JsonProperty("SendCompletedCalls")]
            public bool SendCompletedCalls = true;

            [JsonProperty("SendFailuresAndRefunds")]
            public bool SendFailuresAndRefunds = true;

            [JsonProperty("SendPlayerCancels")]
            public bool SendPlayerCancels = true;

            [JsonProperty("SendValidationFailures")]
            public bool SendValidationFailures;
        }

        private class DiscordWebhookPayload
        {
            [JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
            public string Content;

            [JsonProperty("username", NullValueHandling = NullValueHandling.Ignore)]
            public string Username;

            [JsonProperty("avatar_url", NullValueHandling = NullValueHandling.Ignore)]
            public string AvatarUrl;

            [JsonProperty("embeds")]
            public List<DiscordWebhookEmbed> Embeds = new List<DiscordWebhookEmbed>();
        }

        private class DiscordWebhookEmbed
        {
            [JsonProperty("title")]
            public string Title;

            [JsonProperty("description")]
            public string Description;

            [JsonProperty("color")]
            public int Color;

            [JsonProperty("fields")]
            public List<DiscordWebhookField> Fields = new List<DiscordWebhookField>();

            [JsonProperty("timestamp")]
            public string Timestamp;
        }

        private class DiscordWebhookField
        {
            [JsonProperty("name")]
            public string Name;

            [JsonProperty("value")]
            public string Value;

            [JsonProperty("inline")]
            public bool Inline;
        }

        private class StrikeDefinition
        {
            [JsonIgnore]
            public string Id;

            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("DisplayName")]
            public string DisplayName;

            [JsonProperty("TargetType")]
            public string TargetType = "ground_ping";

            [JsonProperty("AcceptedTargetTypes")]
            public List<string> AcceptedTargetTypes = new List<string>();

            [JsonProperty("Delivery")]
            public string Delivery = "drone";

            [JsonProperty("Payload")]
            public string Payload;

            [JsonProperty("VisualProfileId")]
            public string VisualProfileId = "";

            [JsonProperty("StrikeProfiles")]
            public List<StrikeProfileAssignment> StrikeProfiles = new List<StrikeProfileAssignment>();

            [JsonProperty("Tier")]
            public int Tier = 1;

            [JsonProperty("RPCost")]
            public int RPCost;

            [JsonProperty("PermissionRequired")]
            public string PermissionRequired;

            [JsonProperty("WarningDelaySeconds")]
            public float WarningDelaySeconds;

            [JsonProperty("CooldownPerPlayerSeconds")]
            public float CooldownPerPlayerSeconds;

            [JsonProperty("CooldownPerClanSeconds")]
            public float CooldownPerClanSeconds;

            [JsonProperty("GlobalCooldownSeconds")]
            public float GlobalCooldownSeconds;

            [JsonProperty("BaseCount")]
            public int BaseCount = 1;

            [JsonProperty("MaxCount")]
            public int MaxCount = 12;

            [JsonProperty("SpreadRadius")]
            public float SpreadRadius = 8f;

            [JsonProperty("SpreadMultiplier")]
            public float SpreadMultiplier = 1f;

            [JsonProperty("BurstCount")]
            public int BurstCount;

            [JsonProperty("LineLength")]
            public float LineLength;

            [JsonProperty("LineLengthMultiplier")]
            public float LineLengthMultiplier = 1f;

            [JsonProperty("Width")]
            public float Width;

            [JsonProperty("WidthMultiplier")]
            public float WidthMultiplier = 1f;

            [JsonProperty("ImpactRadius")]
            public float ImpactRadius;

            [JsonProperty("ImpactRadiusMultiplier")]
            public float ImpactRadiusMultiplier = 1f;

            [JsonProperty("PulseDelaySeconds")]
            public float PulseDelaySeconds;

            [JsonProperty("PulseDelayMultiplier")]
            public float PulseDelayMultiplier = 1f;

            [JsonProperty("MissileCount")]
            public int MissileCount;

            [JsonProperty("RocketCount")]
            public int RocketCount;

            [JsonProperty("MaxTrackingSeconds")]
            public float MaxTrackingSeconds;

            [JsonProperty("TrackingSecondsMultiplier")]
            public float TrackingSecondsMultiplier = 1f;

            [JsonProperty("MaxTrackingDistance")]
            public float MaxTrackingDistance;

            [JsonProperty("TrackingDistanceMultiplier")]
            public float TrackingDistanceMultiplier = 1f;

            [JsonProperty("VehicleDamageScale")]
            public float VehicleDamageScale = 1f;

            [JsonProperty("DamageMultiplier")]
            public float DamageMultiplier = 1f;

            [JsonProperty("VehicleDamageMultiplier")]
            public float VehicleDamageMultiplier = 1f;

            [JsonProperty("SplashRadius")]
            public float SplashRadius;

            [JsonProperty("SplashRadiusMultiplier")]
            public float SplashRadiusMultiplier = 1f;

            [JsonProperty("DamageScales")]
            public Dictionary<string, float> DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        private class StrikeProfileAssignment
        {
            [JsonProperty("ProfileId")]
            public string ProfileId = "";

            [JsonProperty("StartDelaySeconds")]
            public float StartDelaySeconds;

            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("PayloadCountLimit")]
            public int PayloadCountLimit;
        }

        private class StoredData
        {
            [JsonProperty("LastStrikeByUser")]
            public Dictionary<string, string> LastStrikeByUser = new Dictionary<string, string>();

            [JsonProperty("DefaultStrikeByUser")]
            public Dictionary<string, string> DefaultStrikeByUser = new Dictionary<string, string>();

            [JsonProperty("PlayerCooldownUntil")]
            public Dictionary<string, double> PlayerCooldownUntil = new Dictionary<string, double>();

            [JsonProperty("ClanCooldownUntil")]
            public Dictionary<string, double> ClanCooldownUntil = new Dictionary<string, double>();

            [JsonProperty("GlobalCooldownUntil")]
            public Dictionary<string, double> GlobalCooldownUntil = new Dictionary<string, double>();

            [JsonProperty("Stats")]
            public Dictionary<string, int> Stats = new Dictionary<string, int>();

            [JsonProperty("RecentCalls")]
            public List<StrikeCallAuditRecord> RecentCalls = new List<StrikeCallAuditRecord>();
        }

        private class StrikeCallAuditRecord
        {
            [JsonProperty("Time")]
            public double Time;

            [JsonProperty("CallId")]
            public string CallId = "";

            [JsonProperty("PlayerId")]
            public string PlayerId = "";

            [JsonProperty("PlayerName")]
            public string PlayerName = "";

            [JsonProperty("TeamId")]
            public string TeamId = "";

            [JsonProperty("StrikeId")]
            public string StrikeId = "";

            [JsonProperty("StrikeName")]
            public string StrikeName = "";

            [JsonProperty("TargetType")]
            public string TargetType = "";

            [JsonProperty("TargetPosition")]
            public string TargetPosition = "";

            [JsonProperty("TargetEntityId")]
            public string TargetEntityId = "";

            [JsonProperty("TargetEntity")]
            public string TargetEntity = "";

            [JsonProperty("RPCost")]
            public int RPCost;

            [JsonProperty("RpCharged")]
            public bool RpCharged;

            [JsonProperty("TokenConsumed")]
            public bool TokenConsumed;

            [JsonProperty("RefundAttempted")]
            public bool RefundAttempted;

            [JsonProperty("ImpactStarted")]
            public bool ImpactStarted;

            [JsonProperty("Result")]
            public string Result = "";

            [JsonProperty("Message")]
            public string Message = "";

            [JsonProperty("State")]
            public string State = "";
        }

        private class AirstrikeTarget
        {
            public AirstrikeTargetType Type = AirstrikeTargetType.Invalid;
            public Vector3 Position;
            public ulong EntityId;
            public string EntityShortPrefabName = "";
            public double CreatedAt;
            public string Source = "";
        }

        private class ValidationResult
        {
            public bool Success;
            public string ReasonCode;
            public string UserMessage;
            public StrikeDefinition Strike;
            public AirstrikeTarget Target;
            public int FinalRPCost;
        }

        private class AirstrikeCallContext
        {
            public string CallId;
            public BasePlayer Caller;
            public ulong CallerUserId;
            public ulong CallerTeamId;
            public string CallerName;
            public StrikeDefinition Strike;
            public AirstrikeTarget Target;
            public int FinalRPCost;
            public bool RpCharged;
            public bool TokenConsumed;
            public bool RefundAttempted;
            public bool ImpactStarted;
            public StrikeExecutionState State = StrikeExecutionState.Requested;
            public double CreatedAt;
            public double WarningEndsAt;
            public AirstrikeCallContext ParentContext;
            public readonly List<AirstrikeCallContext> ChildContexts = new List<AirstrikeCallContext>();
            public MapMarkerGenericRadius WarningMapMarker;
            public readonly List<Timer> Timers = new List<Timer>();
            public readonly List<BaseEntity> SpawnedEntities = new List<BaseEntity>();
            public readonly List<BaseEntity> VisualEntities = new List<BaseEntity>();
            public BaseEntity DeliveryCarrier;
            public bool DeliveryCarrierRequired;
            public bool DeliveryCarrierDestroyed;
            public bool FailureForfeitsRefund;
            public string DeliveryCarrierLabel = "";
            public float DeliveryCarrierMaxHealth;
            public float DeliveryCarrierHealthRemaining;
            public int ExpectedPayloadReleaseCount;
            public int PayloadReleaseCount;
            public readonly Dictionary<int, Vector3> PlannedImpactPositions = new Dictionary<int, Vector3>();
            public string ActiveVisualProfileId = "";
            public VisualProfileConfig ActiveVisualProfile;
            public readonly List<RuntimePayloadRelease> PayloadReleaseSchedule = new List<RuntimePayloadRelease>();
            public Vector3 PlannedDeliveryApproach = Vector3.forward;
            public Vector3 MortarSourcePosition;
            public bool HasMortarSourcePosition;
        }

        private class MonumentBlockZone
        {
            public Vector3 Center;
            public float Radius;
            public string Name;
        }

        private class GiveItemResult
        {
            public int Given;
            public int Dropped;
            public string Failure;
        }

        private class WarningRecipient
        {
            public BasePlayer Player;
            public string Source;
            public float Distance;
        }

        private class WarningFanoutResult
        {
            public bool TeamEnabled;
            public bool NearbyEnabled;
            public bool NearbyEligible;
            public bool IsHeavyStrike;
            public bool MarkerCreated;
            public float NearbyRadius;
            public int TeamMemberCount;
            public int TeamOfflineOrSkipped;
            public int TeamRecipients;
            public int NearbyRecipients;
            public int NearbyCandidates;
            public int NearbySkippedDeduped;
            public readonly List<WarningRecipient> Recipients = new List<WarningRecipient>();

            public int TotalRecipients
            {
                get { return TeamRecipients + NearbyRecipients; }
            }
        }

        private class DronePayloadSpec
        {
            public string Id;
            public string DisplayName;
            public string Prefab;
            public float FuseSeconds;
            public float FinishDelaySeconds;
            public bool HasTimedFuse;
        }

        private class HeavyDropPayloadSpec
        {
            public string Id;
            public string DisplayName;
            public string Prefab;
            public float FinishDelaySeconds;
        }

        private class RocketRunPayloadSpec
        {
            public string Id;
            public string DisplayName;
            public string Prefab;
            public float ProjectileSpeed;
            public float FinishDelaySeconds;
        }

        private class MlrsPayloadSpec
        {
            public string Id;
            public string DisplayName;
            public string Prefab;
            public float ProjectileSpeed;
            public float FinishDelaySeconds;
        }

        private class HomingMissileSpec
        {
            public string Id;
            public string DisplayName;
            public string Prefab;
            public float ProjectileSpeed;
            public float FinishDelaySeconds;
        }

        private class MortarPayloadSpec
        {
            public string Id;
            public string DisplayName;
            public string Prefab;
            public float FinishDelaySeconds;
        }

        private class A10StrafeSpec
        {
            public string Id;
            public string DisplayName;
            public float BaseDamage;
        }

        private class FlightWaypoint
        {
            public Vector3 Position;
            public float Time;
            public Quaternion RotationOffset = Quaternion.identity;
        }

        private class CompiledRuntimeFrame
        {
            public float Time;
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
        }

        private class DeliveryFlightPlan
        {
            public Vector3 Start;
            public Vector3 Release;
            public Vector3 End;
            public Vector3 Direction;
            public float Duration;
            public float FirstPayloadDelay;
            public bool StopAtWaypoints = true;
            public float RotationSmoothTimeSeconds;
            public float TerrainClearance = -1f;
            public bool UsesCompiledTrack;
            public readonly List<CompiledRuntimeFrame> CompiledFrames = new List<CompiledRuntimeFrame>();
            public readonly List<FlightWaypoint> Waypoints = new List<FlightWaypoint>();
        }

        private class VisualProfileFile
        {
            [JsonProperty("SchemaVersion")]
            public int SchemaVersion = 1;

            [JsonProperty("CompilerVersion", NullValueHandling = NullValueHandling.Ignore)]
            public string CompilerVersion;

            [JsonProperty("PublishedRevision", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public long PublishedRevision;

            [JsonProperty("PublishedSha256", NullValueHandling = NullValueHandling.Ignore)]
            public string PublishedSha256;

            [JsonProperty("AllowDangerousPayloadPreview")]
            public bool AllowDangerousPayloadPreview;

            [JsonProperty("Profiles")]
            public Dictionary<string, VisualProfileConfig> Profiles = new Dictionary<string, VisualProfileConfig>(StringComparer.OrdinalIgnoreCase);
        }

        private class VisualProfileConfig
        {
            [JsonProperty("Vehicle")]
            public string Vehicle = "f15";

            [JsonProperty("DurationSeconds")]
            public float DurationSeconds = 8f;

            [JsonProperty("FirstPayloadDelaySeconds")]
            public float FirstPayloadDelaySeconds = 3.5f;

            [JsonProperty("RotationSmoothTimeSeconds")]
            public float RotationSmoothTimeSeconds = 0.12f;

            [JsonProperty("StopAtWaypoints")]
            public bool StopAtWaypoints = true;

            [JsonProperty("PayloadReleaseMode")]
            public string PayloadReleaseMode = "manual";

            [JsonProperty("MaxPayloadCount")]
            public int MaxPayloadCount;

            [JsonProperty("PayloadReleaseIntervalSeconds")]
            public float PayloadReleaseIntervalSeconds = DefaultPayloadReleaseIntervalSeconds;

            [JsonProperty("ReleaseTemplate")]
            public VisualPayloadEvent ReleaseTemplate = new VisualPayloadEvent();

            [JsonProperty("MinimumTerrainClearance")]
            public float MinimumTerrainClearance = DefaultVisualProfileAircraftTerrainClearance;

            [JsonProperty("Waypoints")]
            public List<VisualProfileWaypoint> Waypoints = new List<VisualProfileWaypoint>();

            [JsonProperty("PayloadEvents")]
            public List<VisualPayloadEvent> PayloadEvents = new List<VisualPayloadEvent>();

            [JsonProperty("CompiledTrack", NullValueHandling = NullValueHandling.Ignore)]
            public CompiledVisualTrack CompiledTrack;

            [JsonProperty("CompiledReleaseEvents", NullValueHandling = NullValueHandling.Ignore)]
            public List<VisualPayloadEvent> CompiledReleaseEvents;
        }

        private class CompiledVisualTrack
        {
            [JsonProperty("CompilerVersion")]
            public string CompilerVersion = "";

            [JsonProperty("SourceHash")]
            public string SourceHash = "";

            [JsonProperty("CoordinateSystem")]
            public string CoordinateSystem = "";

            [JsonProperty("SampleRateHz")]
            public float SampleRateHz;

            [JsonProperty("SampleIntervalSeconds")]
            public float SampleIntervalSeconds;

            [JsonProperty("DurationSeconds")]
            public float DurationSeconds;

            [JsonProperty("Frames")]
            public List<CompiledVisualFrame> Frames = new List<CompiledVisualFrame>();
        }

        private class CompiledVisualFrame
        {
            [JsonProperty("Time")]
            public float Time;

            [JsonProperty("X")]
            public float X;

            [JsonProperty("Y")]
            public float Y;

            [JsonProperty("Z")]
            public float Z;

            [JsonProperty("Qx")]
            public float Qx;

            [JsonProperty("Qy")]
            public float Qy;

            [JsonProperty("Qz")]
            public float Qz;

            [JsonProperty("Qw")]
            public float Qw = 1f;
        }

        private class VisualProfileWaypoint
        {
            [JsonProperty("Time")]
            public float Time;

            [JsonProperty("X")]
            public float X;

            [JsonProperty("Y")]
            public float Y;

            [JsonProperty("Z")]
            public float Z;

            [JsonProperty("RotationX")]
            public float RotationX;

            [JsonProperty("RotationY")]
            public float RotationY;

            [JsonProperty("RotationZ")]
            public float RotationZ;
        }

        private class VisualPayloadEvent
        {
            [JsonProperty("Time")]
            public float Time;

            [JsonProperty("Payload")]
            public string Payload = "";

            [JsonProperty("Index")]
            public int Index;

            [JsonProperty("Count")]
            public int Count = 1;

            [JsonProperty("CarrierOffsetX")]
            public float CarrierOffsetX;

            [JsonProperty("CarrierOffsetY")]
            public float CarrierOffsetY;

            [JsonProperty("CarrierOffsetZ")]
            public float CarrierOffsetZ;

            [JsonProperty("TargetOffsetX")]
            public float TargetOffsetX;

            [JsonProperty("TargetOffsetY")]
            public float TargetOffsetY;

            [JsonProperty("TargetOffsetZ")]
            public float TargetOffsetZ;

            [JsonProperty("SpreadRadius")]
            public float SpreadRadius = -1f;

            [JsonProperty("LaunchSpeed")]
            public float LaunchSpeed = -1f;

            [JsonProperty("FuseSeconds")]
            public float FuseSeconds = -1f;

            [JsonProperty("DamageScale")]
            public float DamageScale = 1f;

            [JsonProperty("VehicleDamageScale")]
            public float VehicleDamageScale = 1f;

            [JsonProperty("SplashRadius")]
            public float SplashRadius = -1f;

            [JsonProperty("ImpactRadius")]
            public float ImpactRadius = -1f;

            [JsonProperty("MaxTrackingSeconds")]
            public float MaxTrackingSeconds = -1f;

            [JsonProperty("MaxTrackingDistance")]
            public float MaxTrackingDistance = -1f;

            [JsonProperty("DamageScales")]
            public Dictionary<string, float> DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        private class RuntimePayloadRelease
        {
            public VisualPayloadEvent Event;
            public string Payload = "";
            public float Time;
            public int SequenceIndex;
            public int TotalCount;
            public int SourceEventIndex;
        }

        private class StrikeProfileExecution
        {
            public StrikeProfileAssignment Assignment;
            public AirstrikeCallContext Context;
            public IStrikeExecutor Executor;
        }

        private interface IStrikeExecutor
        {
            string Name { get; }
            bool CanExecute(StrikeDefinition strike);
            void Execute(AirstrikeCallContext context, Action<bool, string> callback);
        }

        private class StrikeProfileBundleExecutor : IStrikeExecutor
        {
            private readonly PortableAirstrikes plugin;

            public StrikeProfileBundleExecutor(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "StrikeProfileBundleExecutor"; }
            }

            public bool CanExecute(StrikeDefinition strike)
            {
                return plugin.GetEnabledStrikeProfileAssignments(strike).Count > 0;
            }

            public void Execute(AirstrikeCallContext context, Action<bool, string> callback)
            {
                plugin.ExecuteStrikeProfileBundle(context, callback);
            }
        }

        private interface ICurrencyAdapter
        {
            string Name { get; }
            bool IsAvailable();
            bool GetBalance(BasePlayer player, out int balance, out string error);
            bool Withdraw(BasePlayer player, int amount, out string error);
            bool Deposit(BasePlayer player, int amount, out string error);
        }

        private class NullCurrencyAdapter : ICurrencyAdapter
        {
            public string Name
            {
                get { return "Disabled"; }
            }

            public bool IsAvailable()
            {
                return true;
            }

            public bool GetBalance(BasePlayer player, out int balance, out string error)
            {
                balance = int.MaxValue;
                error = "";
                return true;
            }

            public bool Withdraw(BasePlayer player, int amount, out string error)
            {
                error = "";
                return true;
            }

            public bool Deposit(BasePlayer player, int amount, out string error)
            {
                error = "";
                return true;
            }
        }

        private class ServerRewardsCurrencyAdapter : ICurrencyAdapter
        {
            private readonly PortableAirstrikes plugin;

            public ServerRewardsCurrencyAdapter(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "ServerRewards"; }
            }

            public bool IsAvailable()
            {
                return plugin.ServerRewards != null && plugin.ServerRewards.IsLoaded;
            }

            public bool GetBalance(BasePlayer player, out int balance, out string error)
            {
                balance = 0;

                if (!IsAvailable())
                {
                    error = "ServerRewards plugin is not loaded.";
                    return false;
                }

                try
                {
                    var result = plugin.ServerRewards.Call("CheckPoints", player.UserIDString);
                    if (result == null)
                    {
                        error = "ServerRewards CheckPoints returned no result.";
                        return false;
                    }

                    balance = Math.Max(0, Convert.ToInt32(result));
                    error = "";
                    return true;
                }
                catch (Exception ex)
                {
                    error = "ServerRewards CheckPoints failed: " + ex.Message;
                    return false;
                }
            }

            public bool Withdraw(BasePlayer player, int amount, out string error)
            {
                if (amount <= 0)
                {
                    error = "";
                    return true;
                }

                if (!IsAvailable())
                {
                    error = "ServerRewards plugin is not loaded.";
                    return false;
                }

                try
                {
                    var result = plugin.ServerRewards.Call("TakePoints", player.UserIDString, amount);
                    if (result is bool && (bool)result)
                    {
                        error = "";
                        return true;
                    }

                    error = "ServerRewards rejected the RP debit.";
                    return false;
                }
                catch (Exception ex)
                {
                    error = "ServerRewards TakePoints failed: " + ex.Message;
                    return false;
                }
            }

            public bool Deposit(BasePlayer player, int amount, out string error)
            {
                if (amount <= 0)
                {
                    error = "";
                    return true;
                }

                if (!IsAvailable())
                {
                    error = "ServerRewards plugin is not loaded.";
                    return false;
                }

                try
                {
                    var result = plugin.ServerRewards.Call("AddPoints", player.UserIDString, amount);
                    if (result is bool && (bool)result)
                    {
                        error = "";
                        return true;
                    }

                    error = "ServerRewards rejected the RP credit.";
                    return false;
                }
                catch (Exception ex)
                {
                    error = "ServerRewards AddPoints failed: " + ex.Message;
                    return false;
                }
            }
        }

        private class EconomicsCurrencyAdapter : ICurrencyAdapter
        {
            private readonly PortableAirstrikes plugin;

            public EconomicsCurrencyAdapter(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "Economics"; }
            }

            public bool IsAvailable()
            {
                return plugin.Economics != null && plugin.Economics.IsLoaded;
            }

            public bool GetBalance(BasePlayer player, out int balance, out string error)
            {
                balance = 0;

                if (!IsAvailable())
                {
                    error = "Economics plugin is not loaded.";
                    return false;
                }

                try
                {
                    var result = plugin.Economics.Call("Balance", player.userID);
                    if (result == null)
                    {
                        error = "Economics Balance returned no result.";
                        return false;
                    }

                    balance = Math.Max(0, Convert.ToInt32(Convert.ToDouble(result)));
                    error = "";
                    return true;
                }
                catch (Exception ex)
                {
                    error = "Economics Balance failed: " + ex.Message;
                    return false;
                }
            }

            public bool Withdraw(BasePlayer player, int amount, out string error)
            {
                if (amount <= 0)
                {
                    error = "";
                    return true;
                }

                if (!IsAvailable())
                {
                    error = "Economics plugin is not loaded.";
                    return false;
                }

                try
                {
                    var result = plugin.Economics.Call("Withdraw", player.userID, Convert.ToDouble(amount));
                    if (result is bool && (bool)result)
                    {
                        error = "";
                        return true;
                    }

                    error = "Economics rejected the debit.";
                    return false;
                }
                catch (Exception ex)
                {
                    error = "Economics Withdraw failed: " + ex.Message;
                    return false;
                }
            }

            public bool Deposit(BasePlayer player, int amount, out string error)
            {
                if (amount <= 0)
                {
                    error = "";
                    return true;
                }

                if (!IsAvailable())
                {
                    error = "Economics plugin is not loaded.";
                    return false;
                }

                try
                {
                    var result = plugin.Economics.Call("Deposit", player.userID, Convert.ToDouble(amount));
                    if (result is bool && (bool)result)
                    {
                        error = "";
                        return true;
                    }

                    error = "Economics rejected the credit.";
                    return false;
                }
                catch (Exception ex)
                {
                    error = "Economics Deposit failed: " + ex.Message;
                    return false;
                }
            }
        }

        private class DroneDropExecutor : IStrikeExecutor
        {
            private readonly PortableAirstrikes plugin;

            public DroneDropExecutor(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "DroneDropExecutor"; }
            }

            public bool CanExecute(StrikeDefinition strike)
            {
                DronePayloadSpec spec;
                return strike != null
                    && string.Equals(strike.Delivery, "drone", StringComparison.OrdinalIgnoreCase)
                    && plugin.TryGetDronePayloadSpec(strike.Payload, out spec);
            }

            public void Execute(AirstrikeCallContext context, Action<bool, string> callback)
            {
                if (context == null || context.Strike == null)
                {
                    callback(false, "Missing strike execution context.");
                    return;
                }

                if (!CanExecute(context.Strike))
                {
                    callback(false, "Drone drop executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                DronePayloadSpec spec;
                if (!plugin.TryGetDronePayloadSpec(context.Strike.Payload, out spec))
                {
                    callback(false, "Drone drop executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                var count = plugin.CalculatePayloadCount(context.Strike);
                if (count <= 0)
                {
                    callback(false, "Configured payload count resolved to zero.");
                    return;
                }

                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                float postReleaseDuration;
                float finishDelay;
                var effectiveCount = plugin.PreparePayloadReleaseSchedule(context, "drone", DeliveryVisualProfile.RocketRun, count, context.Strike.Payload, firstPayloadDelay, DroneDropPayloadDelay, spec.FinishDelaySeconds, out firstPayloadDelay, out postReleaseDuration, out finishDelay);
                if (effectiveCount <= 0)
                {
                    callback(false, "No payload releases fit inside the selected drone visual profile.");
                    return;
                }

                plugin.SetExpectedPayloadReleaseCount(context, effectiveCount);
                plugin.StartDroneDeliveryVisual(context, effectiveCount, DroneDropPayloadDelay, spec.FinishDelaySeconds, firstPayloadDelay, postReleaseDuration);

                if (plugin.HasPayloadReleaseSchedule(context))
                {
                    plugin.SchedulePayloadReleaseEvents(context, plugin.GetRocketApproachDirection(context), callback);
                }
                else
                {
                    for (var i = 0; i < effectiveCount; i++)
                    {
                        var payloadIndex = i + 1;
                        plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * DroneDropPayloadDelay), () =>
                        {
                            if (!plugin.IsCallActive(context))
                            {
                                return;
                            }

                            string error;
                            if (!plugin.TrySpawnDronePayload(context, spec, payloadIndex, effectiveCount, out error))
                            {
                                callback(false, error);
                            }
                        });
                    }
                }

                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, effectiveCount + " payload release(s) delivered.");
                    }
                });
            }
        }

        private class HeavyDropExecutor : IStrikeExecutor
        {
            private readonly PortableAirstrikes plugin;

            public HeavyDropExecutor(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "HeavyDropExecutor"; }
            }

            public bool CanExecute(StrikeDefinition strike)
            {
                HeavyDropPayloadSpec spec;
                return strike != null
                    && (string.Equals(strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase))
                    && plugin.TryGetHeavyDropPayloadSpec(strike.Payload, out spec);
            }

            public void Execute(AirstrikeCallContext context, Action<bool, string> callback)
            {
                if (context == null || context.Strike == null)
                {
                    callback(false, "Missing strike execution context.");
                    return;
                }

                if (!CanExecute(context.Strike))
                {
                    callback(false, "Heavy drop executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                HeavyDropPayloadSpec spec;
                if (!plugin.TryGetHeavyDropPayloadSpec(context.Strike.Payload, out spec))
                {
                    callback(false, "Heavy drop executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                var count = plugin.CalculatePayloadCount(context.Strike);
                if (count <= 0)
                {
                    callback(false, "Configured heavy payload count resolved to zero.");
                    return;
                }

                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                float postReleaseDuration;
                float finishDelay;
                var effectiveCount = plugin.PreparePayloadReleaseSchedule(context, null, DeliveryVisualProfile.HeavyDrop, count, context.Strike.Payload, firstPayloadDelay, HeavyDropPayloadDelay, spec.FinishDelaySeconds, out firstPayloadDelay, out postReleaseDuration, out finishDelay);
                if (effectiveCount <= 0)
                {
                    callback(false, "No payload releases fit inside the selected heavy-drop visual profile.");
                    return;
                }

                plugin.SetExpectedPayloadReleaseCount(context, effectiveCount);
                plugin.StartAircraftDeliveryVisual(context, DeliveryVisualProfile.HeavyDrop, firstPayloadDelay, postReleaseDuration, "heavy drop");

                if (plugin.HasPayloadReleaseSchedule(context))
                {
                    plugin.SchedulePayloadReleaseEvents(context, plugin.GetRocketApproachDirection(context), callback);
                }
                else
                {
                    for (var i = 0; i < effectiveCount; i++)
                    {
                        var payloadIndex = i + 1;
                        plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * HeavyDropPayloadDelay), () =>
                        {
                            if (!plugin.IsCallActive(context))
                            {
                                return;
                            }

                            string error;
                            if (!plugin.TrySpawnHeavyDropPayload(context, spec, payloadIndex, effectiveCount, out error))
                            {
                                callback(false, error);
                            }
                        });
                    }
                }

                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, effectiveCount + " payload release(s) delivered.");
                    }
                });
            }
        }

        private class RocketRunExecutor : IStrikeExecutor
        {
            private readonly PortableAirstrikes plugin;

            public RocketRunExecutor(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "RocketRunExecutor"; }
            }

            public bool CanExecute(StrikeDefinition strike)
            {
                RocketRunPayloadSpec spec;
                return strike != null
                    && (string.Equals(strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase))
                    && plugin.TryGetRocketPayloadSpec(strike.Payload, out spec);
            }

            public void Execute(AirstrikeCallContext context, Action<bool, string> callback)
            {
                if (context == null || context.Strike == null)
                {
                    callback(false, "Missing strike execution context.");
                    return;
                }

                if (!CanExecute(context.Strike))
                {
                    callback(false, "Rocket run executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                RocketRunPayloadSpec spec;
                if (!plugin.TryGetRocketPayloadSpec(context.Strike.Payload, out spec))
                {
                    callback(false, "Rocket run executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                var count = plugin.CalculateRocketCount(context.Strike);
                if (count <= 0)
                {
                    callback(false, "Configured rocket count resolved to zero.");
                    return;
                }

                var approach = plugin.GetRocketApproachDirection(context);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                float postReleaseDuration;
                float finishDelay;
                var effectiveCount = plugin.PreparePayloadReleaseSchedule(context, null, DeliveryVisualProfile.RocketRun, count, context.Strike.Payload, firstPayloadDelay, RocketRunProjectileDelay, spec.FinishDelaySeconds, out firstPayloadDelay, out postReleaseDuration, out finishDelay);
                if (effectiveCount <= 0)
                {
                    callback(false, "No payload releases fit inside the selected rocket-run visual profile.");
                    return;
                }

                plugin.SetExpectedPayloadReleaseCount(context, effectiveCount);
                plugin.StartAircraftDeliveryVisual(context, DeliveryVisualProfile.RocketRun, firstPayloadDelay, postReleaseDuration, "rocket run");
                if (plugin.HasPayloadReleaseSchedule(context))
                {
                    plugin.SchedulePayloadReleaseEvents(context, approach, callback);
                }
                else
                {
                    for (var i = 0; i < effectiveCount; i++)
                    {
                        var rocketIndex = i + 1;
                        plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * RocketRunProjectileDelay), () =>
                        {
                            if (!plugin.IsCallActive(context))
                            {
                                return;
                            }

                            string error;
                            if (!plugin.TrySpawnRocketProjectile(context, spec, approach, rocketIndex, effectiveCount, out error))
                            {
                                callback(false, error);
                            }
                        });
                    }
                }

                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, effectiveCount + " payload release(s) delivered.");
                    }
                });
            }
        }

        private class MlrsExecutor : IStrikeExecutor
        {
            private readonly PortableAirstrikes plugin;

            public MlrsExecutor(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "MlrsExecutor"; }
            }

            public bool CanExecute(StrikeDefinition strike)
            {
                MlrsPayloadSpec spec;
                return strike != null
                    && string.Equals(strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase)
                    && plugin.TryGetMlrsPayloadSpec(strike.Payload, out spec);
            }

            public void Execute(AirstrikeCallContext context, Action<bool, string> callback)
            {
                if (context == null || context.Strike == null)
                {
                    callback(false, "Missing strike execution context.");
                    return;
                }

                if (!CanExecute(context.Strike))
                {
                    callback(false, "MLRS executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                MlrsPayloadSpec spec;
                if (!plugin.TryGetMlrsPayloadSpec(context.Strike.Payload, out spec))
                {
                    callback(false, "MLRS executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                var count = plugin.CalculateMlrsRocketCount(context.Strike);
                if (count <= 0)
                {
                    callback(false, "Configured MLRS rocket count resolved to zero.");
                    return;
                }

                var approach = plugin.GetRocketApproachDirection(context);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                float postReleaseDuration;
                float finishDelay;
                var effectiveCount = plugin.PreparePayloadReleaseSchedule(context, "f15", DeliveryVisualProfile.Mlrs, count, context.Strike.Payload, firstPayloadDelay, MlrsRocketDelay, spec.FinishDelaySeconds, out firstPayloadDelay, out postReleaseDuration, out finishDelay);
                if (effectiveCount <= 0)
                {
                    callback(false, "No payload releases fit inside the selected MLRS visual profile.");
                    return;
                }

                plugin.SetExpectedPayloadReleaseCount(context, effectiveCount);
                plugin.StartMlrsDeliveryVisual(context, approach, firstPayloadDelay, postReleaseDuration);
                if (plugin.HasPayloadReleaseSchedule(context))
                {
                    plugin.SchedulePayloadReleaseEvents(context, approach, callback);
                }
                else
                {
                    for (var i = 0; i < effectiveCount; i++)
                    {
                        var rocketIndex = i + 1;
                        plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * MlrsRocketDelay), () =>
                        {
                            if (!plugin.IsCallActive(context))
                            {
                                return;
                            }

                            string error;
                            if (!plugin.TrySpawnMlrsRocket(context, spec, approach, rocketIndex, effectiveCount, out error))
                            {
                                callback(false, error);
                            }
                        });
                    }
                }

                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, effectiveCount + " payload release(s) delivered.");
                    }
                });
            }
        }

        private class HomingMissileExecutor : IStrikeExecutor
        {
            private readonly PortableAirstrikes plugin;

            public HomingMissileExecutor(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "HomingMissileExecutor"; }
            }

            public bool CanExecute(StrikeDefinition strike)
            {
                HomingMissileSpec spec;
                return strike != null
                    && (string.Equals(strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase))
                    && plugin.TryGetHomingMissileSpec(strike.Payload, out spec);
            }

            public void Execute(AirstrikeCallContext context, Action<bool, string> callback)
            {
                if (context == null || context.Strike == null)
                {
                    callback(false, "Missing strike execution context.");
                    return;
                }

                if (!CanExecute(context.Strike))
                {
                    callback(false, "Homing missile executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                HomingMissileSpec spec;
                if (!plugin.TryGetHomingMissileSpec(context.Strike.Payload, out spec))
                {
                    callback(false, "Homing missile executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                BaseCombatEntity target;
                string targetError;
                if (!plugin.TryResolveHomingTarget(context, out target, out targetError))
                {
                    callback(false, targetError);
                    return;
                }

                var count = plugin.CalculateHomingMissileCount(context.Strike);
                if (count <= 0)
                {
                    callback(false, "Configured homing missile count resolved to zero.");
                    return;
                }

                var approach = plugin.GetRocketApproachDirection(context);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                var finishPadding = plugin.GetHomingTrackingSeconds(context.Strike) + spec.FinishDelaySeconds;
                float postReleaseDuration;
                float finishDelay;
                var effectiveCount = plugin.PreparePayloadReleaseSchedule(context, null, DeliveryVisualProfile.HomingMissile, count, context.Strike.Payload, firstPayloadDelay, HomingMissileLaunchDelay, finishPadding, out firstPayloadDelay, out postReleaseDuration, out finishDelay);
                if (effectiveCount <= 0)
                {
                    callback(false, "No payload releases fit inside the selected homing-missile visual profile.");
                    return;
                }

                plugin.SetExpectedPayloadReleaseCount(context, effectiveCount);
                plugin.StartAircraftDeliveryVisual(context, DeliveryVisualProfile.HomingMissile, firstPayloadDelay, postReleaseDuration, "homing missile");
                var targetId = context.Target.EntityId;
                if (plugin.HasPayloadReleaseSchedule(context))
                {
                    plugin.SchedulePayloadReleaseEvents(context, approach, callback);
                }
                else
                {
                    for (var i = 0; i < effectiveCount; i++)
                    {
                        var missileIndex = i + 1;
                        plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * HomingMissileLaunchDelay), () =>
                        {
                            if (!plugin.IsCallActive(context))
                            {
                                return;
                            }

                            string error;
                            if (!plugin.TrySpawnHomingMissile(context, spec, approach, targetId, missileIndex, effectiveCount, out error))
                            {
                                callback(false, error);
                            }
                        });
                    }
                }

                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, effectiveCount + " payload release(s) delivered.");
                    }
                });
            }
        }

        private class MortarExecutor : IStrikeExecutor
        {
            private readonly PortableAirstrikes plugin;

            public MortarExecutor(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "MortarExecutor"; }
            }

            public bool CanExecute(StrikeDefinition strike)
            {
                MortarPayloadSpec spec;
                return strike != null
                    && string.Equals(strike.Delivery, "off_map_mortar", StringComparison.OrdinalIgnoreCase)
                    && plugin.TryGetMortarPayloadSpec(strike.Payload, out spec);
            }

            public void Execute(AirstrikeCallContext context, Action<bool, string> callback)
            {
                if (context == null || context.Strike == null)
                {
                    callback(false, "Missing strike execution context.");
                    return;
                }

                if (!CanExecute(context.Strike))
                {
                    callback(false, "Mortar executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                MortarPayloadSpec spec;
                if (!plugin.TryGetMortarPayloadSpec(context.Strike.Payload, out spec))
                {
                    callback(false, "Mortar executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                var count = plugin.CalculatePayloadCount(context.Strike);
                if (count <= 0)
                {
                    callback(false, "Configured mortar shell count resolved to zero.");
                    return;
                }

                plugin.StartMortarArtilleryVisual(context, count);

                for (var i = 0; i < count; i++)
                {
                    var shellIndex = i + 1;
                    plugin.ScheduleCallTimer(context, i * MortarShellDelay, () =>
                    {
                        if (!plugin.IsCallActive(context))
                        {
                            return;
                        }

                        string error;
                        if (!plugin.TrySpawnMortarShell(context, spec, shellIndex, count, out error))
                        {
                            callback(false, error);
                        }
                    });
                }

                var finishDelay = Math.Max(0.1f, (count - 1) * MortarShellDelay + spec.FinishDelaySeconds);
                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, count + " " + spec.DisplayName + " mortar shell(s) delivered.");
                    }
                });
            }
        }

        private class A10StrafeExecutor : IStrikeExecutor
        {
            private readonly PortableAirstrikes plugin;

            public A10StrafeExecutor(PortableAirstrikes plugin)
            {
                this.plugin = plugin;
            }

            public string Name
            {
                get { return "A10StrafeExecutor"; }
            }

            public bool CanExecute(StrikeDefinition strike)
            {
                A10StrafeSpec spec;
                return strike != null
                    && string.Equals(strike.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase)
                    && plugin.TryGetA10StrafeSpec(strike.Payload, out spec);
            }

            public void Execute(AirstrikeCallContext context, Action<bool, string> callback)
            {
                if (context == null || context.Strike == null)
                {
                    callback(false, "Missing strike execution context.");
                    return;
                }

                if (!CanExecute(context.Strike))
                {
                    callback(false, "A-10 strafe executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                A10StrafeSpec spec;
                if (!plugin.TryGetA10StrafeSpec(context.Strike.Payload, out spec))
                {
                    callback(false, "A-10 strafe executor does not support payload '" + context.Strike.Payload + "'.");
                    return;
                }

                var burstCount = plugin.CalculateA10BurstCount(context.Strike);
                if (burstCount <= 0)
                {
                    callback(false, "Configured A-10 burst count resolved to zero.");
                    return;
                }

                var direction = plugin.GetA10StrafeDirection(context);
                var pulseDelay = plugin.GetA10PulseDelaySeconds(context.Strike);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                float postReleaseDuration;
                float finishDelay;
                var effectiveCount = plugin.PreparePayloadReleaseSchedule(context, "a10", DeliveryVisualProfile.A10, burstCount, context.Strike.Payload, firstPayloadDelay, pulseDelay, A10FinishPaddingSeconds, out firstPayloadDelay, out postReleaseDuration, out finishDelay);
                if (effectiveCount <= 0)
                {
                    callback(false, "No payload releases fit inside the selected A-10 visual profile.");
                    return;
                }

                plugin.SetExpectedPayloadReleaseCount(context, effectiveCount);
                plugin.StartA10DeliveryVisual(context, direction, effectiveCount, pulseDelay, firstPayloadDelay, postReleaseDuration);
                if (plugin.HasPayloadReleaseSchedule(context))
                {
                    plugin.SchedulePayloadReleaseEvents(context, direction, callback);
                }
                else
                {
                    for (var i = 0; i < effectiveCount; i++)
                    {
                        var pulseIndex = i + 1;
                        plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * pulseDelay), () =>
                        {
                            if (!plugin.IsCallActive(context))
                            {
                                return;
                            }

                            string error;
                            if (!plugin.TryRunA10Pulse(context, spec, direction, pulseIndex, effectiveCount, out error))
                            {
                                callback(false, error);
                            }
                        });
                    }
                }

                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, effectiveCount + " payload release(s) delivered.");
                    }
                });
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            config.ConfigVersion = CurrentConfigVersion;
            NormalizeConfig();
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

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            LoadData();
            LoadVisualProfiles();
            RegisterPermissions();
            RegisterChatCommand();
            RefreshCurrencyAdapter();
            InitializeExecutors();
        }

        private void OnServerInitialized()
        {
            RefreshCurrencyAdapter();
            InitializeExecutors();
            ResetMonumentBlockZones();
            TryRegisterAirstrikeCustomItemDefinition();
            NormalizeOnlineAirstrikeInventories();
            StartToolPingWatcher();
            Puts("Loaded " + GetEnabledStrikeCount() + " enabled strike definition(s). Charge-backed CID targeting binoculars, automatic tool ping targeting, persisted manual player defaults, scrollable CUI selection, admin workbench layout polish with keypad numeric editing, paged give search, clickable page and field help, Commands and Help tabs, high-rate visual delivery flyovers/artillery sources with optional schema-2 compiled editor tracks, multi-release payload schedules, and editor-parity waypoint position/rotation playback, autoload-safe repeated sound cues, loot item injection, monument blocking, audit logging/webhooks, cancellable warning calls, heavy warning markers, in-game warning fanout diagnostics, vehicle-aware homing target locks, mini-ping recovery, and direct-command executors are active in v0.1.52.");
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            timer.Once(1f, () => NormalizeAirstrikeInventory(player));
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
            {
                return;
            }

            lastToolPingAt.Remove(player.userID);
            lastProcessedToolPingKeyByUser.Remove(player.userID);
            lastProcessedToolPingAtByUser.Remove(player.userID);
            lastToolHelpAt.Remove(player.userID);
            DestroyToolTargetMarker(player.userID);
            DestroyAdminUi(player);
            adminUiStates.Remove(player.userID);
        }

        private void Unload()
        {
            StopToolPingWatcher();

            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyStrikeUi(player);
                DestroyAdminUi(player);
            }

            adminUiStates.Clear();

            CancelActiveCallsForUnload();
            DestroyAllToolTargetMarkers();
            payloadReleaseMetadataByEntityId.Clear();
            SaveData();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null)
            {
                return;
            }

            if (string.Equals(plugin.Name, "ServerRewards", StringComparison.OrdinalIgnoreCase))
            {
                ServerRewards = plugin;
                RefreshCurrencyAdapter();
            }
            else if (string.Equals(plugin.Name, "Economics", StringComparison.OrdinalIgnoreCase))
            {
                Economics = plugin;
                RefreshCurrencyAdapter();
            }
            else if (string.Equals(plugin.Name, "CustomItemDefinitions", StringComparison.OrdinalIgnoreCase))
            {
                CustomItemDefinitions = plugin;
                warnedCIDUnavailable = false;
                TryRegisterAirstrikeCustomItemDefinition();
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin == null)
            {
                return;
            }

            if (string.Equals(plugin.Name, "ServerRewards", StringComparison.OrdinalIgnoreCase) && ServerRewards == plugin)
            {
                ServerRewards = null;
                RefreshCurrencyAdapter();
            }
            else if (string.Equals(plugin.Name, "Economics", StringComparison.OrdinalIgnoreCase) && Economics == plugin)
            {
                Economics = null;
                RefreshCurrencyAdapter();
            }
            else if (string.Equals(plugin.Name, "CustomItemDefinitions", StringComparison.OrdinalIgnoreCase) && CustomItemDefinitions == plugin)
            {
                CustomItemDefinitions = null;
                airstrikeCustomItemDefinition = null;
                airstrikeIconFileId = 0;
                airstrikeIconSource = "";
                warnedCIDUnavailable = false;
                PrintWarning("CustomItemDefinitions unloaded. PortableAirstrikes will use vanilla fallback item creation if enabled.");
            }
        }

        private void OnCIDLoaded(Plugin cidPlugin)
        {
            if (cidPlugin != null)
            {
                CustomItemDefinitions = cidPlugin;
            }

            warnedCIDUnavailable = false;
            TryRegisterAirstrikeCustomItemDefinition();
        }

        private void OnLootSpawn(LootContainer container)
        {
            if (container == null)
            {
                return;
            }

            var inventory = container.inventory;
            TryInjectLootToken(inventory, container.ShortPrefabName, container.PrefabName, container.name);
        }

        private void OnLootSpawn(LootFill lootFill)
        {
            if (lootFill == null)
            {
                return;
            }

            var storage = lootFill.StorageContainer;
            var inventory = storage == null ? null : storage.inventory;
            TryInjectLootToken(inventory, lootFill.name, storage?.ShortPrefabName, storage?.PrefabName, storage?.name);
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            HandleDeliveryCarrierDestroyed(entity, info);
            RemovePayloadReleaseMetadata(entity);
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            RemovePayloadReleaseMetadata(entity as BaseEntity);
        }

        private object OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            TryApplyDeliveryCarrierHit(attacker, info);
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
            {
                return null;
            }

            var initiator = info.Initiator as BaseEntity;
            if (initiator == null || initiator.net == null)
            {
                return null;
            }

            RuntimePayloadRelease release;
            if (!payloadReleaseMetadataByEntityId.TryGetValue(initiator.net.ID.Value, out release) || release == null || release.Event == null)
            {
                return null;
            }

            var key = GetDamageScaleKey(entity);
            var scale = GetReleaseDamageScale(release.Event, key);
            if (string.Equals(key, "Vehicles", StringComparison.OrdinalIgnoreCase))
            {
                scale *= GetReleaseVehicleDamageScale(release.Event);
            }

            scale = Mathf.Clamp(scale, 0f, 100f);
            if (Math.Abs(scale - 1f) <= 0.001f)
            {
                return null;
            }

            try
            {
                info.damageTypes.ScaleAll(scale);
                if (config?.General != null && config.General.DebugMode)
                {
                    Puts("Scaled native release damage from payload entity " + initiator.net.ID.Value + " against " + key + " by " + scale.ToString("0.###", CultureInfo.InvariantCulture) + ".");
                }
            }
            catch (Exception ex)
            {
                if (config?.General != null && config.General.DebugMode)
                {
                    Puts("Native payload damage scaling failed for entity " + initiator.net.ID.Value + ": " + ex.Message);
                }
            }

            return null;
        }

        private void CmdStrike(BasePlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (args == null || args.Length == 0)
            {
                ShowStrikeOverview(player);
                return;
            }

            var sub = args[0].ToLowerInvariant();
            if (sub == "admin")
            {
                ShowAdminUi(player, null);
                return;
            }

            if (sub == "reload")
            {
                CmdReload(player);
                return;
            }

            if (sub == "debug")
            {
                CmdDebug(player, args);
                return;
            }

            if (sub == "debugping")
            {
                CmdDebugPing(player);
                return;
            }

            if (sub == "giveitem")
            {
                CmdGiveItem(player, args);
                return;
            }

            if (sub == "balance")
            {
                ShowBalance(player);
                return;
            }

            if (sub == "list")
            {
                ShowStrikeList(player);
                return;
            }

            if (sub == "last")
            {
                CmdRepeatLast(player);
                return;
            }

            if (sub == "default" || sub == "setdefault")
            {
                CmdDefaultStrike(player, args);
                return;
            }

            if (sub == "status")
            {
                ShowPlayerStrikeStatus(player);
                return;
            }

            if (sub == "cancel")
            {
                CmdCancelActiveStrike(player);
                return;
            }

            if (!config.Selection.AllowDirectCommand)
            {
                Reply(player, "Direct strike commands are disabled. Use /" + GetOpenCommand() + " to open the selection flow.");
                return;
            }

            TryPrepareStrike(player, args[0], false);
        }

        [ConsoleCommand("portableairstrikes.giveitem")]
        private void CCmdGiveItem(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: portableairstrikes.giveitem <playerNameOrSteamId> [amount]");
                return;
            }

            var target = FindPlayer(arg.GetString(0));
            if (target == null)
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            var amount = 1;
            if (arg.Args.Length >= 2)
            {
                int.TryParse(arg.GetString(1), out amount);
            }

            var result = GiveAirstrikeTokensDetailed(target, Math.Max(1, amount));
            var dropped = result.Dropped > 0 ? " " + result.Dropped + " physical item(s) dropped at their feet because inventory was full." : "";
            var failure = string.IsNullOrWhiteSpace(result.Failure) ? "" : " Last failure: " + result.Failure;
            arg.ReplyWith("Gave " + result.Given + " " + GetAirstrikeItemDisplayName() + " item(s) to " + target.displayName + "." + dropped + failure);
        }

        private void OnMapMarkerAdded(BasePlayer player, ProtoBuf.MapNote note)
        {
            HandleMapMarkerOrPingAdded(player, note);
        }

        private object OnMapMarkerAdd(BasePlayer player, ProtoBuf.MapNote note)
        {
            HandleMapMarkerOrPingAdded(player, note);
            return null;
        }

        private void HandleMapMarkerOrPingAdded(BasePlayer player, ProtoBuf.MapNote note)
        {
            if (player == null || note == null)
            {
                return;
            }

            var isToolPing = IsPlayerHoldingAirstrikeTool(player);
            if (isToolPing)
            {
                if (!TryMarkToolPingForProcessing(player, note))
                {
                    return;
                }

                StoreAirstrikeToolPingTarget(player, note);
                HandleAirstrikeToolPing(player);
                return;
            }

            StoreMapNoteTarget(player, note.worldPosition, MapMarkerSource);
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (!IsAirstrikeTargetingToolItem(newItem) || !config.AirstrikeItem.ShowEquipInstructions)
            {
                return;
            }

            var now = GetNow();
            double lastHelp;
            if (lastToolHelpAt.TryGetValue(player.userID, out lastHelp) && now - lastHelp < ToolEquipHelpCooldownSeconds)
            {
                return;
            }

            lastToolHelpAt[player.userID] = now;
            var defaultText = GetDefaultStrikeSummary(player);
            Reply(player, GetAirstrikeItemDisplayName() + " ready. Aim with the binoculars and place a ping to lock a target. Your saved default is " + defaultText + ". Use /" + GetOpenCommand() + " default <strikeId> to change it.");
        }

        private void HandleAirstrikeToolPing(BasePlayer player)
        {
            if (player == null || config?.AirstrikeItem == null || !config.AirstrikeItem.TreatAsTargetingTool)
            {
                return;
            }

            var now = GetNow();
            double lastPing;
            if (lastToolPingAt.TryGetValue(player.userID, out lastPing) && now - lastPing < ToolPingDebounceSeconds)
            {
                return;
            }

            lastToolPingAt[player.userID] = now;

            var target = GetLatestTarget(player, false);
            if (target == null)
            {
                return;
            }

            CreateToolTargetMarker(player, target);

            string defaultStrikeId;
            if (!TryGetPlayerDefaultStrikeId(player, out defaultStrikeId))
            {
                OpenDefaultSelectionMenu(player, "Target locked with " + GetAirstrikeItemDisplayName() + ". Choose a strike in the menu. To save a default later, use /" + GetOpenCommand() + " default <strikeId>.");
                return;
            }

            StrikeDefinition strike;
            if (!TryGetStrike(defaultStrikeId, out strike) || strike == null || !strike.Enabled)
            {
                OpenDefaultSelectionMenu(player, "Your saved airstrike default is no longer available. Choose a strike in the menu, then use /" + GetOpenCommand() + " default <strikeId> if you want to save it.");
                return;
            }

            if (!CanPlayerUseStrike(player, strike))
            {
                OpenDefaultSelectionMenu(player, "Your saved airstrike default is locked for your current permissions. Choose a strike in the menu, then use /" + GetOpenCommand() + " default <strikeId> if you want to save it.");
                return;
            }

            if (config.Selection.AutoFilterByPingType && !StrikeAcceptsTargetType(strike, target.Type))
            {
                OpenDefaultSelectionMenu(player, "Your saved default " + strike.DisplayName + " accepts " + FormatAcceptedTargetTypes(strike) + ", but this is a " + FormatTargetType(target.Type) + ". Choose a target-compatible strike; defaults are only changed with /" + GetOpenCommand() + " default <strikeId>.");
                return;
            }

            Reply(player, "Target locked. Calling your default airstrike: " + strike.DisplayName + ".");
            TryPrepareStrike(player, strike.Id, false);
        }

        private void OpenDefaultSelectionMenu(BasePlayer player, string message)
        {
            if (player == null)
            {
                return;
            }

            Reply(player, message);
            ShowStrikeOverview(player);
        }

        private void StartToolPingWatcher()
        {
            StopToolPingWatcher();

            if (config?.AirstrikeItem == null || !config.AirstrikeItem.TreatAsTargetingTool)
            {
                return;
            }

            toolPingWatcherTimer = timer.Every(ToolPingPollIntervalSeconds, PollAirstrikeToolPings);
            if (toolPingWatcherTimer != null)
            {
                activeTimers.Add(toolPingWatcherTimer);
            }
        }

        private void StopToolPingWatcher()
        {
            if (toolPingWatcherTimer == null)
            {
                return;
            }

            activeTimers.Remove(toolPingWatcherTimer);
            toolPingWatcherTimer.Destroy();
            toolPingWatcherTimer = null;
        }

        private void PollAirstrikeToolPings()
        {
            if (config?.AirstrikeItem == null || !config.AirstrikeItem.TreatAsTargetingTool)
            {
                return;
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || !IsPlayerHoldingAirstrikeTool(player))
                {
                    continue;
                }

                ProtoBuf.MapNote note;
                if (!TryGetFreshPlayerPing(player, out note) || !TryMarkToolPingForProcessing(player, note))
                {
                    continue;
                }

                StoreAirstrikeToolPingTarget(player, note);
                HandleAirstrikeToolPing(player);
            }
        }

        private bool TryGetFreshPlayerPing(BasePlayer player, out ProtoBuf.MapNote note)
        {
            note = null;
            var pings = player?.State?.pings;
            if (pings == null || pings.Count == 0)
            {
                return false;
            }

            var bestRemaining = float.MinValue;
            foreach (var candidate in pings)
            {
                if (candidate == null || !candidate.isPing || candidate.timeRemaining <= 0f)
                {
                    continue;
                }

                if (candidate.totalDuration > 0f)
                {
                    var age = candidate.totalDuration - candidate.timeRemaining;
                    if (age < -0.25f || age > ToolPingFreshWindowSeconds)
                    {
                        continue;
                    }
                }

                if (candidate.timeRemaining <= bestRemaining)
                {
                    continue;
                }

                note = candidate;
                bestRemaining = candidate.timeRemaining;
            }

            return note != null;
        }

        private bool TryMarkToolPingForProcessing(BasePlayer player, ProtoBuf.MapNote note)
        {
            if (player == null || note == null)
            {
                return false;
            }

            var key = BuildToolPingKey(note);
            var now = GetNow();

            string lastKey;
            double lastAt;
            if (lastProcessedToolPingKeyByUser.TryGetValue(player.userID, out lastKey)
                && string.Equals(lastKey, key, StringComparison.Ordinal)
                && lastProcessedToolPingAtByUser.TryGetValue(player.userID, out lastAt)
                && now - lastAt < ToolPingDuplicateWindowSeconds)
            {
                return false;
            }

            lastProcessedToolPingKeyByUser[player.userID] = key;
            lastProcessedToolPingAtByUser[player.userID] = now;
            return true;
        }

        private string BuildToolPingKey(ProtoBuf.MapNote note)
        {
            if (note == null)
            {
                return "";
            }

            var pos = note.worldPosition;
            var entityId = note.associatedId.Value;
            return note.icon
                + ":" + entityId
                + ":" + Mathf.RoundToInt(pos.x * 10f)
                + ":" + Mathf.RoundToInt(pos.y * 10f)
                + ":" + Mathf.RoundToInt(pos.z * 10f);
        }

        private void StoreAirstrikeToolPingTarget(BasePlayer player, ProtoBuf.MapNote note)
        {
            if (player == null || note == null)
            {
                return;
            }

            if (TryStoreAssociatedPingTarget(player, note))
            {
                return;
            }

            if (TryStoreAimedVehiclePingTarget(player, AirstrikeToolPingSource))
            {
                return;
            }

            AirstrikeTarget raycastTarget;
            string raycastError;
            if (TryStoreRaycastTarget(player, AirstrikeToolPingSource, out raycastTarget, out raycastError))
            {
                if (raycastTarget != null && raycastTarget.Type != AirstrikeTargetType.GroundPing)
                {
                    return;
                }

                if (TryStoreNearbyVehiclePingTarget(player, note))
                {
                    return;
                }

                return;
            }

            if (TryStoreNearbyVehiclePingTarget(player, note))
            {
                return;
            }

            StoreMapNoteTarget(player, note.worldPosition, AirstrikeToolPingSource);
        }

        private bool TryStoreAssociatedPingTarget(BasePlayer player, ProtoBuf.MapNote note)
        {
            if (player == null || note == null || note.associatedId.Value == 0UL)
            {
                return false;
            }

            return TryStoreEntityPingTarget(player, FindEntity(note.associatedId.Value), note.worldPosition, AirstrikeToolPingSource, false);
        }

        private bool TryStoreAimedVehiclePingTarget(BasePlayer player, string source)
        {
            if (player?.eyes == null)
            {
                return false;
            }

            var range = Math.Max(10f, config?.General == null ? 250f : config.General.MaxCallRange);
            var hits = Physics.SphereCastAll(player.eyes.HeadRay(), ToolPingVehicleAimRadius, range, TargetRaycastLayer, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var entity = ResolveClassifiableTargetEntity(hit.GetEntity());
                if (entity == null || entity.IsDestroyed || ClassifyTarget(entity) != AirstrikeTargetType.VehiclePing)
                {
                    continue;
                }

                var combatEntity = entity as BaseCombatEntity;
                if (combatEntity != null && combatEntity.IsDead())
                {
                    continue;
                }

                if (TryStoreEntityPingTarget(player, entity, hit.point, source, true))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryStoreNearbyVehiclePingTarget(BasePlayer player, ProtoBuf.MapNote note)
        {
            if (player == null || note == null)
            {
                return false;
            }

            var pingPosition = ResolveMapNotePosition(note.worldPosition);
            var entities = Pool.Get<List<BaseEntity>>();
            BaseEntity bestEntity = null;
            var bestDistanceSqr = float.MaxValue;

            try
            {
                Vis.Entities(pingPosition, ToolPingVehicleSearchRadius, entities, TargetRaycastLayer, QueryTriggerInteraction.Ignore);
                foreach (var entity in entities)
                {
                    var targetEntity = ResolveClassifiableTargetEntity(entity);
                    if (targetEntity == null
                        || targetEntity.IsDestroyed
                        || ClassifyTarget(targetEntity) != AirstrikeTargetType.VehiclePing)
                    {
                        continue;
                    }

                    var combatEntity = targetEntity as BaseCombatEntity;
                    if (combatEntity != null && combatEntity.IsDead())
                    {
                        continue;
                    }

                    var targetPoint = GetEntityTargetPosition(targetEntity, pingPosition);
                    var distanceSqr = (targetPoint - pingPosition).sqrMagnitude;
                    if (distanceSqr >= bestDistanceSqr)
                    {
                        continue;
                    }

                    bestEntity = targetEntity;
                    bestDistanceSqr = distanceSqr;
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref entities);
            }

            return TryStoreEntityPingTarget(player, bestEntity, pingPosition, AirstrikeToolPingSource, true);
        }

        private bool TryStoreEntityPingTarget(BasePlayer player, BaseEntity entity, Vector3 fallbackPosition, string source, bool vehicleOnly)
        {
            if (player == null || entity == null)
            {
                return false;
            }

            var targetEntity = ResolveClassifiableTargetEntity(entity);
            if (targetEntity == null || targetEntity.IsDestroyed)
            {
                return false;
            }

            var targetType = ClassifyTarget(targetEntity);
            if (targetType == AirstrikeTargetType.GroundPing
                || targetType == AirstrikeTargetType.Invalid
                || (vehicleOnly && targetType != AirstrikeTargetType.VehiclePing))
            {
                return false;
            }

            var combatEntity = targetEntity as BaseCombatEntity;
            if (combatEntity != null && combatEntity.IsDead())
            {
                return false;
            }

            StoreTarget(player, GetEntityTargetPosition(targetEntity, ResolveMapNotePosition(fallbackPosition)), targetEntity, targetType, source);
            return true;
        }

        [HookMethod(nameof(API_GiveAirstrikeItem))]
        public int API_GiveAirstrikeItem(ulong playerId, int amount)
        {
            var player = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
            if (player == null)
            {
                return 0;
            }

            return GiveAirstrikeTokensDetailed(player, Math.Max(1, amount)).Given;
        }

        [HookMethod(nameof(API_HasAirstrikeItem))]
        public bool API_HasAirstrikeItem(ulong playerId, int amount)
        {
            var player = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
            return player != null && GetAirstrikeTokenCount(player) >= Math.Max(1, amount);
        }

        [HookMethod(nameof(API_ReloadVisualProfiles))]
        public object API_ReloadVisualProfiles()
        {
            VisualProfileFile candidate;
            Dictionary<string, string> motionModes;
            Dictionary<string, string> releaseModes;
            Dictionary<string, string> warnings;
            string message;
            if (!TryReadVisualProfiles(out candidate, out motionModes, out releaseModes, out warnings, out message))
            {
                lastVisualProfileLoadMessage = message;
                lastVisualProfileLoadAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                lastVisualProfileLoadSucceeded = false;
                return BuildVisualProfileApiResult(false, message);
            }

            ApplyVisualProfileSnapshot(candidate, motionModes, releaseModes, warnings, message);
            return BuildVisualProfileApiResult(true, message);
        }

        [HookMethod(nameof(API_GetVisualProfileStatus))]
        public object API_GetVisualProfileStatus()
        {
            return BuildVisualProfileApiResult(true, lastVisualProfileLoadMessage);
        }

        private object OnRaidlandsCreateKitItem(string shortname, int amount, ulong skin, string displayName)
        {
            if (!IsAirstrikeKitItem(shortname, skin, displayName))
            {
                return null;
            }

            return CreateAirstrikeToken(Math.Max(1, amount));
        }

        private void CmdReload(BasePlayer player)
        {
            if (!IsAdmin(player))
            {
                Reply(player, "You do not have permission to reload PortableAirstrikes.");
                return;
            }

            ClearAirstrikeCustomItemDefinition(true);
            LoadConfig();
            RegisterPermissions();
            RegisterChatCommand();
            RefreshCurrencyAdapter();
            InitializeExecutors();
            ResetMonumentBlockZones();
            TryRegisterAirstrikeCustomItemDefinition();
            StartToolPingWatcher();
            Reply(player, "PortableAirstrikes config reloaded. Loaded " + GetEnabledStrikeCount() + " enabled strike definition(s) and " + strikeExecutors.Count + " executor route(s).");
        }

        private void CmdDebug(BasePlayer player, string[] args)
        {
            if (!IsAdmin(player))
            {
                Reply(player, "You do not have permission to use airstrike debug commands.");
                return;
            }

            var mode = args.Length >= 2 ? args[1].ToLowerInvariant() : "summary";
            if (mode == "target")
            {
                var target = GetLatestTarget(player, false);
                if (target == null)
                {
                    Reply(player, "No stored target for you. Use /" + GetOpenCommand() + " debugping to create a raycast target.");
                    return;
                }

                Reply(player, "Target: " + DescribeTarget(target) + ", age " + FormatSeconds(GetNow() - target.CreatedAt) + ", source " + target.Source + ".");
                return;
            }

            if (mode == "history" || mode == "recent")
            {
                ShowDebugHistory(player, args);
                return;
            }

            if (mode == "stats")
            {
                ShowDebugStats(player);
                return;
            }

            if (mode == "active")
            {
                ShowDebugActiveCalls(player);
                return;
            }

            if (mode == "item")
            {
                ShowDebugItem(player);
                return;
            }

            if (mode == "cooldowns")
            {
                Reply(player, "Cooldown data: players=" + storedData.PlayerCooldownUntil.Count
                    + ", clans=" + storedData.ClanCooldownUntil.Count
                    + ", global=" + storedData.GlobalCooldownUntil.Count + ".");
                return;
            }

            if (mode == "strikes")
            {
                Reply(player, "Strike registry: " + GetEnabledStrikeCount() + " enabled / " + config.StrikeDefinitions.Count + " configured. Use /" + GetOpenCommand() + " list for IDs.");
                return;
            }

            if (mode == "monument" || mode == "safety")
            {
                ShowMonumentDebug(player);
                return;
            }

            if (mode == "warnings" || mode == "warning")
            {
                ShowWarningFanoutDebug(player, args);
                return;
            }

            Reply(player, "Debug commands: /" + GetOpenCommand() + " debug target, /" + GetOpenCommand() + " debug item, /" + GetOpenCommand() + " debug cooldowns, /" + GetOpenCommand() + " debug strikes, /" + GetOpenCommand() + " debug history [count], /" + GetOpenCommand() + " debug stats, /" + GetOpenCommand() + " debug active, /" + GetOpenCommand() + " debug monument, /" + GetOpenCommand() + " debug warnings <strikeId>, /" + GetOpenCommand() + " debugping.");
        }

        private void ShowDebugHistory(BasePlayer player, string[] args)
        {
            var records = storedData.RecentCalls;
            if (records == null || records.Count == 0)
            {
                Reply(player, "No recent airstrike audit records are stored yet.");
                return;
            }

            var requested = 5;
            if (args.Length >= 3)
            {
                int parsed;
                if (int.TryParse(args[2], out parsed))
                {
                    requested = parsed;
                }
            }

            requested = Math.Min(MaxDebugHistoryRows, Math.Max(1, requested));
            var shown = Math.Min(requested, records.Count);
            Reply(player, "Recent airstrike audit records, newest first (" + shown + "/" + records.Count + "):");
            for (var i = records.Count - 1; i >= 0 && shown > 0; i--, shown--)
            {
                Reply(player, FormatAuditRecordForChat(records[i]));
            }
        }

        private void ShowDebugStats(BasePlayer player)
        {
            if (storedData.Stats == null || storedData.Stats.Count == 0)
            {
                Reply(player, "No airstrike stats are stored yet.");
                return;
            }

            var keys = new List<string>(storedData.Stats.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            var parts = new List<string>();
            var limit = Math.Min(10, keys.Count);
            for (var i = 0; i < limit; i++)
            {
                var key = keys[i];
                parts.Add(key + "=" + storedData.Stats[key]);
            }

            var extra = keys.Count > limit ? " (+" + (keys.Count - limit) + " more)" : "";
            Reply(player, "Airstrike stats: " + string.Join(", ", parts.ToArray()) + extra + ".");
        }

        private void ShowDebugActiveCalls(BasePlayer player)
        {
            if (activeCalls.Count == 0)
            {
                Reply(player, "No airstrike calls are currently active.");
                return;
            }

            Reply(player, "Active airstrike calls: " + activeCalls.Count + " total, " + CountActiveHeavyStrikes() + " heavy.");
            foreach (var context in activeCalls.Values)
            {
                if (context == null)
                {
                    continue;
                }

                Reply(player, context.CallerName + " -> " + context.Strike.Id + " at " + DescribeTarget(context.Target) + ", state " + context.State + ", age " + FormatSeconds(GetNow() - context.CreatedAt) + ".");
            }
        }

        private void ShowDebugItem(BasePlayer player)
        {
            TryRegisterAirstrikeCustomItemDefinition();

            var item = config?.AirstrikeItem;
            if (item == null)
            {
                Reply(player, "Airstrike item config is missing.");
                return;
            }

            var cidLoaded = CustomItemDefinitions != null && CustomItemDefinitions.IsLoaded;
            var registered = airstrikeCustomItemDefinition != null;
            var definitionText = registered
                ? airstrikeCustomItemDefinition.shortname + " itemId=" + airstrikeCustomItemDefinition.itemid
                : "not registered";
            var iconText = airstrikeIconFileId != 0
                ? airstrikeIconFileId + " from " + airstrikeIconSource
                : string.IsNullOrWhiteSpace(airstrikeIconSource) ? "none" : airstrikeIconSource;

            Reply(player, "Airstrike item: CID enabled=" + item.UseCustomItemDefinition
                + ", CID loaded=" + cidLoaded
                + ", registered=" + registered
                + ", definition=" + definitionText
                + ", createShortname=" + GetAirstrikeCreateShortname()
                + ", parent=" + GetAirstrikeParentShortname()
                + ", actualStack=" + GetAirstrikeMaxStackSize()
                + ", maxCharges=" + GetAirstrikeMaxChargesPerItem()
                + ", icon=" + iconText
                + ", vanillaFallback=" + item.AllowVanillaFallbackIfCIDMissing + ".");
        }

        private void ShowMonumentDebug(BasePlayer player)
        {
            var target = GetLatestTarget(player, false);
            if (target == null)
            {
                Reply(player, "No stored target for you. Use /" + GetOpenCommand() + " debugping to create a raycast target.");
                return;
            }

            string monumentName;
            var blocked = IsBlockedMonumentPosition(target.Position, out monumentName);
            var mode = !config.General.BlockMonuments
                ? "disabled"
                : config.General.BlockMonumentsForHeavyStrikesOnly ? "enabled for heavy strikes" : "enabled for all strikes";
            var configuredCount = config.General.BlockedMonumentNames == null ? 0 : config.General.BlockedMonumentNames.Count;
            var result = blocked ? "target is inside " + monumentName : "target is not inside a configured blocked monument";
            Reply(player, "Monument blocking is " + mode + "; " + result + ". Zones loaded=" + monumentBlockZones.Count + ", configured names=" + configuredCount + ", target=" + FormatPosition(target.Position) + ".");
        }

        private void ShowWarningFanoutDebug(BasePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /" + GetOpenCommand() + " debug warnings <strikeId>. Use /" + GetOpenCommand() + " debugping first to set a test target.");
                return;
            }

            StrikeDefinition strike;
            if (!TryGetStrike(args[2], out strike))
            {
                Reply(player, "Unknown strike ID '" + args[2] + "'. Use /" + GetOpenCommand() + " list for configured IDs.");
                return;
            }

            var target = GetLatestTarget(player, false);
            if (target == null)
            {
                Reply(player, "No stored target for warning preview. Use /" + GetOpenCommand() + " debugping first.");
                return;
            }

            var context = new AirstrikeCallContext
            {
                CallId = "debug-warning-preview",
                Caller = player,
                CallerUserId = player.userID,
                CallerTeamId = player.currentTeam,
                CallerName = player.displayName ?? player.UserIDString,
                Strike = strike,
                Target = CopyTarget(target),
                CreatedAt = GetNow(),
                State = StrikeExecutionState.Warning
            };

            var warningDelay = GetWarningDelaySeconds(strike);
            var preview = BuildWarningFanoutPreview(context, false);
            var targetNote = !StrikeAcceptsTargetType(strike, target.Type) ? " Target type mismatch: strike accepts " + FormatAcceptedTargetTypes(strike) + "." : "";
            Reply(player, "Warning preview for " + strike.Id + " at " + FormatPosition(target.Position) + ": " + FormatWarningFanoutSummary(preview) + ". Impact delay " + FormatSeconds(warningDelay) + "." + targetNote);
            Reply(player, "Team members listed=" + preview.TeamMemberCount + ", team skipped/offline=" + preview.TeamOfflineOrSkipped + ", nearby candidates=" + preview.NearbyCandidates + ", nearby deduped/skipped=" + preview.NearbySkippedDeduped + ".");

            if (preview.Recipients.Count == 0)
            {
                Reply(player, "No online recipients would be sent a warning right now. This is expected when you are solo or nearby warnings are disabled.");
                return;
            }

            var shown = 0;
            foreach (var recipient in preview.Recipients)
            {
                if (recipient?.Player == null)
                {
                    continue;
                }

                shown++;
                var distance = recipient.Distance > 0f ? ", distance " + FormatMeters(recipient.Distance) : "";
                Reply(player, shown + ". " + recipient.Source + ": " + recipient.Player.displayName + " (" + recipient.Player.UserIDString + ")" + distance + ".");
                if (shown >= 8)
                {
                    break;
                }
            }

            if (preview.Recipients.Count > shown)
            {
                Reply(player, "...and " + (preview.Recipients.Count - shown) + " more recipient(s).");
            }
        }

        private void CmdDebugPing(BasePlayer player)
        {
            if (!IsAdmin(player))
            {
                Reply(player, "Only admins can create debug airstrike targets.");
                return;
            }

            AirstrikeTarget target;
            string error;
            if (!TryStoreRaycastTarget(player, DebugRaycastSource, out target, out error))
            {
                Reply(player, error);
                return;
            }

            Reply(player, "Stored debug target: " + DescribeTarget(target) + ".");
        }

        private void CmdGiveItem(BasePlayer player, string[] args)
        {
            if (!IsAdmin(player))
            {
                Reply(player, "You do not have permission to give airstrike items.");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "Usage: /" + GetOpenCommand() + " giveitem <playerNameOrSteamId> [amount]");
                return;
            }

            var target = FindPlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player not found.");
                return;
            }

            var amount = 1;
            if (args.Length >= 3)
            {
                int.TryParse(args[2], out amount);
            }

            var result = GiveAirstrikeTokensDetailed(target, Math.Max(1, amount));
            var dropped = result.Dropped > 0 ? " " + result.Dropped + " physical item(s) dropped at their feet because inventory was full." : "";
            var failure = string.IsNullOrWhiteSpace(result.Failure) ? "" : " Last failure: " + result.Failure;
            Reply(player, "Gave " + result.Given + " " + GetAirstrikeItemDisplayName() + " item(s) to " + target.displayName + "." + dropped + failure);
        }

        private void CmdRepeatLast(BasePlayer player)
        {
            if (!config.Selection.AllowRepeatLastStrike)
            {
                Reply(player, "Repeating the last strike is disabled.");
                return;
            }

            string lastStrikeId;
            if (!storedData.LastStrikeByUser.TryGetValue(player.UserIDString, out lastStrikeId) || string.IsNullOrWhiteSpace(lastStrikeId))
            {
                Reply(player, "You do not have a previous successful strike yet.");
                return;
            }

            TryPrepareStrike(player, lastStrikeId, true);
        }

        private void CmdDefaultStrike(BasePlayer player, string[] args)
        {
            if (args.Length < 2 || string.Equals(args[1], "show", StringComparison.OrdinalIgnoreCase))
            {
                Reply(player, "Your airstrike binocular default is " + GetDefaultStrikeSummary(player) + ". Use /" + GetOpenCommand() + " default <strikeId>, /" + GetOpenCommand() + " default clear, or /" + GetOpenCommand() + " list.");
                return;
            }

            if (string.Equals(args[1], "clear", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[1], "none", StringComparison.OrdinalIgnoreCase))
            {
                storedData.DefaultStrikeByUser.Remove(player.UserIDString);
                SaveData();
                Reply(player, "Cleared your airstrike binocular default. The next " + GetAirstrikeItemDisplayName() + " ping will open the selection menu.");
                return;
            }

            StrikeDefinition strike;
            string error;
            if (!TrySetDefaultStrike(player, args[1], out strike, out error))
            {
                Reply(player, error);
                return;
            }

            Reply(player, strike.DisplayName + " is now your airstrike binocular default. Ping a target while holding " + GetAirstrikeItemDisplayName() + " to call it.");
        }

        private void ShowPlayerStrikeStatus(BasePlayer player)
        {
            AirstrikeCallContext context;
            if (!activeCalls.TryGetValue(player.userID, out context) || context == null)
            {
                Reply(player, "You do not have an active airstrike call.");
                return;
            }

            var timing = " Active for " + FormatSeconds(GetNow() - context.CreatedAt) + ".";
            if (context.State == StrikeExecutionState.Warning && context.WarningEndsAt > 0)
            {
                timing = " Payload launches in " + FormatSeconds(Math.Max(0, context.WarningEndsAt - GetNow())) + ".";
            }

            var marker = IsWarningMapMarkerActive(context) ? " Public warning marker active." : "";
            var cancel = CanPlayerCancelCall(context) ? " Use /" + GetOpenCommand() + " cancel to cancel before impact." : "";
            Reply(player, context.Strike.DisplayName + " is " + context.State + " at " + DescribeTarget(context.Target) + "." + timing + marker + cancel);
        }

        private void CmdCancelActiveStrike(BasePlayer player)
        {
            AirstrikeCallContext context;
            if (!activeCalls.TryGetValue(player.userID, out context) || context == null)
            {
                Reply(player, "You do not have an active airstrike call to cancel.");
                return;
            }

            if (!config.General.AllowPlayerCancelBeforeImpact)
            {
                Reply(player, "Airstrike cancellation is disabled by config.");
                return;
            }

            if (!CanPlayerCancelCall(context))
            {
                Reply(player, context.ImpactStarted
                    ? "That airstrike has already started impacting and cannot be cancelled."
                    : "That airstrike cannot be cancelled in its current state.");
                return;
            }

            CancelStrikeCall(context, "Player cancelled before impact.", true);
        }

        [ConsoleCommand("portableairstrikes.ui.close")]
        private void CCmdUiClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                return;
            }

            DestroyStrikeUi(player);
        }

        [ConsoleCommand("portableairstrikes.ui.select")]
        private void CCmdUiSelect(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var strikeId = arg.GetString(0);
            if (config.Selection.RequireConfirmation)
            {
                ShowStrikeConfirmUi(player, strikeId);
                return;
            }

            DestroyStrikeUi(player);
            TryPrepareStrike(player, strikeId, false);
        }

        [ConsoleCommand("portableairstrikes.ui.confirm")]
        private void CCmdUiConfirm(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var strikeId = arg.GetString(0);
            DestroyStrikeUi(player);
            TryPrepareStrike(player, strikeId, false);
        }

        [ConsoleCommand("portableairstrikes.adminui")]
        private void CCmdAdminUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !IsAdmin(player))
            {
                return;
            }

            var state = GetAdminUiState(player);
            if (arg.Args == null || arg.Args.Length == 0)
            {
                ShowAdminUi(player, null);
                return;
            }

            var action = (arg.GetString(0) ?? "").Trim().ToLowerInvariant();
            switch (action)
            {
                case "close":
                    DestroyAdminUi(player);
                    return;

                case "tip":
                    if (arg.Args.Length >= 2)
                    {
                        state.Status = GetAdminTooltip(arg.GetString(1));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "tab":
                    if (arg.Args.Length >= 2)
                    {
                        state.Tab = NormalizeAdminTab(arg.GetString(1));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "select":
                    if (arg.Args.Length >= 2 && TryGetStrike(arg.GetString(1), out var selected))
                    {
                        state.SelectedStrikeId = selected.Id;
                        state.Tab = string.IsNullOrWhiteSpace(state.Tab) ? "strikes" : state.Tab;
                        state.DeleteConfirmStrikeId = "";
                    }
                    ShowAdminUi(player, null);
                    return;

                case "addstrike":
                    AddAdminStrike(player);
                    ShowAdminUi(player, null);
                    return;

                case "deletestrike":
                    if (arg.Args.Length >= 2)
                    {
                        DeleteAdminStrike(player, arg.GetString(1), false);
                    }
                    ShowAdminUi(player, null);
                    return;

                case "confirmdelete":
                    if (arg.Args.Length >= 2)
                    {
                        DeleteAdminStrike(player, arg.GetString(1), true);
                    }
                    ShowAdminUi(player, null);
                    return;

                case "togglestrike":
                    if (arg.Args.Length >= 2)
                    {
                        ToggleAdminStrikeEnabled(player, arg.GetString(1));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "cycle":
                    if (arg.Args.Length >= 3)
                    {
                        CycleAdminStrikeField(player, arg.GetString(1), arg.GetString(2));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "targettype":
                    if (arg.Args.Length >= 3)
                    {
                        ToggleAdminAcceptedTargetType(player, arg.GetString(1), arg.GetString(2));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "toggle":
                    if (arg.Args.Length >= 2)
                    {
                        ToggleAdminConfigField(player, arg.GetString(1));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "profiletoggle":
                    if (arg.Args.Length >= 3)
                    {
                        ToggleAdminStrikeProfile(player, arg.GetString(1), arg.GetString(2));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "give":
                    AdminGiveAirstrikeItem(player, arg.Args.Length >= 2 ? arg.GetString(1) : "search");
                    ShowAdminUi(player, null);
                    return;

                case "profile":
                    if (arg.Args.Length >= 3)
                    {
                        ShowAdminUi(player, AssignAdminVisualProfile(player, arg.GetString(1), arg.GetString(2)));
                        return;
                    }
                    ShowAdminUi(player, null);
                    return;

                case "profilecycle":
                    if (arg.Args.Length >= 2)
                    {
                        CycleAdminVisualProfile(player, arg.GetString(1));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "openeditor":
                    if (arg.Args.Length >= 2)
                    {
                        OpenAdminAnimationProfile(player, arg.GetString(1), false);
                    }
                    ShowAdminUi(player, null);
                    return;

                case "createprofile":
                    if (arg.Args.Length >= 2)
                    {
                        OpenAdminAnimationProfile(player, arg.GetString(1), true);
                    }
                    ShowAdminUi(player, null);
                    return;

                case "reloadprofiles":
                    LoadVisualProfiles();
                    if (IsAnimationEditorLoaded())
                    {
                        PortableAirstrikesAnimationEditor.Call("API_ReloadProfiles");
                    }
                    ShowAdminUi(player, "Reloaded strike profiles.");
                    return;

                case "reload":
                    LoadConfig();
                    LoadVisualProfiles();
                    RegisterPermissions();
                    RefreshCurrencyAdapter();
                    InitializeExecutors();
                    ShowAdminUi(player, "Reloaded PortableAirstrikes config and profiles.");
                    return;

                case "givepage":
                    if (arg.Args.Length >= 2)
                    {
                        ChangeAdminGivePage(player, arg.GetString(1));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "givesort":
                    if (arg.Args.Length >= 2)
                    {
                        SetAdminGiveSort(player, arg.GetString(1));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "givefilter":
                    if (arg.Args.Length >= 2)
                    {
                        SetAdminGiveFilter(player, arg.GetString(1));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "cmdscope":
                    if (arg.Args.Length >= 2)
                    {
                        state.CommandScope = NormalizeAdminCommandScope(arg.GetString(1));
                        state.CommandCategory = "";
                    }
                    ShowAdminUi(player, null);
                    return;

                case "cmdcat":
                    if (arg.Args.Length >= 2)
                    {
                        state.CommandCategory = NormalizeAdminCommandCategory(arg.GetString(1));
                    }
                    ShowAdminUi(player, null);
                    return;

                case "numberedit":
                    if (arg.Args.Length >= 2)
                    {
                        OpenAdminNumberEdit(player, arg.GetString(1), arg.Args.Length >= 3 ? arg.GetString(2) : "");
                    }
                    ShowAdminUi(player, null);
                    return;

                case "numberkey":
                    if (arg.Args.Length >= 2)
                    {
                        ApplyAdminNumberEditKey(player, arg.GetString(1));
                    }
                    ShowAdminNumberEditUi(player);
                    return;

                case "numberapply":
                    ShowAdminUi(player, CommitPendingAdminNumberEdit(player));
                    return;

                case "numbercancel":
                    ClearPendingAdminNumberEdit(state);
                    CuiHelper.DestroyUi(player, AdminNumberEditUiName);
                    ShowAdminUi(player, "Number edit cancelled.");
                    return;

                default:
                    ShowAdminUi(player, null);
                    return;
            }
        }

        [ConsoleCommand("portableairstrikes.adminfield")]
        private void CCmdAdminField(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !IsAdmin(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var field = (arg.GetString(0) ?? "").Trim().ToLowerInvariant();
            var valueStart = field.StartsWith("strike.", StringComparison.OrdinalIgnoreCase) && arg.Args.Length >= 2 ? 2 : 1;
            var id = valueStart == 2 ? arg.GetString(1) : "";
            var value = GetArgTail(arg, valueStart);

            if (field == "give_search")
            {
                var state = GetAdminUiState(player);
                state.GiveSearch = CleanAdminString(value, 64);
                state.GivePage = 0;
                ShowAdminUi(player, null);
                return;
            }

            if (field == "give_amount")
            {
                int amount;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount))
                {
                    GetAdminUiState(player).GiveAmount = Mathf.Clamp(amount, 1, GetAirstrikeMaxChargesPerItem());
                }
                ShowAdminUi(player, null);
                return;
            }

            var message = ApplyAdminField(player, field, id, value);
            ShowAdminUi(player, message);
        }

        private AdminUiState GetAdminUiState(BasePlayer player)
        {
            AdminUiState state;
            if (player == null)
            {
                return new AdminUiState();
            }

            if (!adminUiStates.TryGetValue(player.userID, out state) || state == null)
            {
                state = new AdminUiState();
                adminUiStates[player.userID] = state;
            }

            state.Tab = NormalizeAdminTab(state.Tab);
            state.GiveSort = NormalizeAdminGiveSort(state.GiveSort);
            state.GiveFilter = NormalizeAdminGiveFilter(state.GiveFilter);
            state.CommandScope = NormalizeAdminCommandScope(state.CommandScope);
            state.CommandCategory = NormalizeAdminCommandCategory(state.CommandCategory);
            if (state.GivePage < 0)
            {
                state.GivePage = 0;
            }

            if (state.GiveAmount <= 0)
            {
                state.GiveAmount = 1;
            }

            if (string.IsNullOrWhiteSpace(state.SelectedStrikeId) || !TryGetStrike(state.SelectedStrikeId, out var strike))
            {
                state.SelectedStrikeId = GetFirstStrikeId();
            }

            return state;
        }

        private string NormalizeAdminTab(string tab)
        {
            tab = (tab ?? "").Trim().Replace("-", "").Replace("_", "").ToLowerInvariant();
            switch (tab)
            {
                case "give":
                case "giveitems":
                    return "give";
                case "strike":
                case "strikes":
                    return "strikes";
                case "balance":
                    return "balance";
                case "safety":
                    return "safety";
                case "visual":
                case "visuals":
                    return "visuals";
                case "loot":
                case "audit":
                case "lootaudit":
                    return "lootaudit";
                case "activity":
                    return "activity";
                case "command":
                case "commands":
                    return "commands";
                case "help":
                    return "help";
                default:
                    return "dashboard";
            }
        }

        private string NormalizeAdminCommandScope(string scope)
        {
            scope = (scope ?? "").Trim().ToLowerInvariant();
            return scope == "console" ? "console" : "chat";
        }

        private string NormalizeAdminCommandCategory(string category)
        {
            return (category ?? "").Trim().Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
        }

        private void ShowAdminUi(BasePlayer player, string status)
        {
            if (player == null || !IsAdmin(player))
            {
                Reply(player, "You do not have permission to open the airstrike admin panel.");
                return;
            }

            DestroyStrikeUi(player);
            DestroyAdminUi(player, false);

            var state = GetAdminUiState(player);
            if (!string.IsNullOrWhiteSpace(status))
            {
                state.Status = status;
            }

            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.035 0.040 0.048 0.965" },
                RectTransform = { AnchorMin = "0.075 0.065", AnchorMax = "0.925 0.935" }
            }, "Overlay", AdminUiName);

            AddAdminPanel(container, root, "0.018 0.880", "0.982 0.982", "0.080 0.092 0.112 0.96");
            AddUiLabel(container, root, "Portable Airstrikes Admin", 20, TextAnchor.MiddleLeft, "0.035 0.925", "0.55 0.975", "1 0.86 0.58 1");
            AddUiLabel(container, root, BuildAdminHeaderSummary(), 10, TextAnchor.MiddleLeft, "0.036 0.888", "0.82 0.925", "0.68 0.76 0.82 1");
            AddUiButton(container, root, "Reload", "portableairstrikes.adminui reload", "0.785 0.913", "0.865 0.965", "0.16 0.30 0.42 0.95", 10);
            AddUiButton(container, root, "X", "portableairstrikes.adminui close", "0.905 0.913", "0.965 0.965", "0.55 0.12 0.10 0.95", 14);

            AddAdminTabs(container, root, state);

            var body = container.Add(new CuiPanel
            {
                Image = { Color = "0.055 0.062 0.074 0.92" },
                RectTransform = { AnchorMin = "0.185 0.105", AnchorMax = "0.965 0.862" }
            }, root);

            switch (state.Tab)
            {
                case "give":
                    DrawAdminGiveTab(container, body, player, state);
                    break;
                case "strikes":
                    DrawAdminStrikesTab(container, body, player, state);
                    break;
                case "balance":
                    DrawAdminBalanceTab(container, body, player, state);
                    break;
                case "safety":
                    DrawAdminSafetyTab(container, body, player, state);
                    break;
                case "visuals":
                    DrawAdminVisualsTab(container, body, player, state);
                    break;
                case "lootaudit":
                    DrawAdminLootAuditTab(container, body, player, state);
                    break;
                case "activity":
                    DrawAdminActivityTab(container, body, player, state);
                    break;
                case "commands":
                    DrawAdminCommandsTab(container, body, player, state);
                    break;
                case "help":
                    DrawAdminHelpTab(container, body, player, state);
                    break;
                default:
                    DrawAdminDashboardTab(container, body, player, state);
                    break;
            }

            var statusText = string.IsNullOrWhiteSpace(state.Status) ? "Ready." : ShortenAdminText(state.Status, 190);
            AddUiLabel(container, root, statusText, 10, TextAnchor.MiddleLeft, "0.205 0.035", "0.965 0.080", "0.66 0.74 0.80 1");
            CuiHelper.AddUi(player, container);
            if (state.NumberEdit != null)
            {
                ShowAdminNumberEditUi(player);
            }
        }

        private string BuildAdminHeaderSummary()
        {
            return GetEnabledStrikeCount() + "/" + (config.StrikeDefinitions == null ? 0 : config.StrikeDefinitions.Count) + " strikes enabled"
                + "   active " + activeCalls.Count
                + "   profiles " + CountLoadedVisualProfiles()
                + "   editor " + (IsAnimationEditorLoaded() ? "loaded" : "not loaded");
        }

        private void AddAdminTabs(CuiElementContainer container, string root, AdminUiState state)
        {
            var tabs = new[]
            {
                "dashboard:Dashboard",
                "give:Give Items",
                "strikes:Strikes",
                "balance:Balance",
                "safety:Safety",
                "visuals:Strike Profiles",
                "lootaudit:Loot/Audit",
                "activity:Activity",
                "commands:Commands",
                "help:Help"
            };

            AddAdminPanel(container, root, "0.035 0.105", "0.165 0.862", "0.044 0.050 0.060 0.96");
            for (var i = 0; i < tabs.Length; i++)
            {
                var parts = tabs[i].Split(':');
                var key = parts[0];
                var label = parts[1];
                var yMax = 0.840f - i * 0.073f;
                var yMin = yMax - 0.058f;
                var active = string.Equals(state.Tab, key, StringComparison.OrdinalIgnoreCase);
                AddUiButton(container, root, label, "portableairstrikes.adminui tab " + key,
                    "0.048 " + FormatUiFloat(yMin),
                    "0.152 " + FormatUiFloat(yMax),
                    active ? "0.38 0.16 0.10 0.98" : "0.105 0.120 0.140 0.95",
                    label.Length > 11 ? 8 : 9);
            }
        }

        private void DrawAdminDashboardTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Dashboard", "Operational snapshot for the current airstrike configuration.", "page.dashboard");

            var total = config.StrikeDefinitions == null ? 0 : config.StrikeDefinitions.Count;
            var unsupported = CountUnsupportedStrikes();
            var enabled = GetEnabledStrikeCount();
            var itemSummary = GetAirstrikeItemDisplayName() + " | consume=" + config.AirstrikeItem.ConsumeOnSuccessfulCall + " | admin bypass=" + config.AirstrikeItem.AllowAdminsWithoutItem;
            var currencySummary = config.Currency.Enabled ? config.Currency.Provider + " enabled" : "free mode";
            var profileSummary = CountLoadedVisualProfiles() + " strike profile(s); editor " + (IsAnimationEditorLoaded() ? "loaded" : "not loaded");

            AddAdminMetric(container, body, "Strikes", enabled + " enabled / " + total + " configured", unsupported == 0 ? "All executor pairs supported." : unsupported + " unsupported delivery/payload pair(s).", 0.05f, 0.62f);
            AddAdminMetric(container, body, "Items", itemSummary, "Give and inventory paths use the charge-backed single-item model.", 0.52f, 0.62f);
            AddAdminMetric(container, body, "Currency", currencySummary, "Final RP cost still respects VIP discount permissions.", 0.05f, 0.39f);
            AddAdminMetric(container, body, "Strike Profiles", profileSummary, "Selected profiles provide authored payload releases.", 0.52f, 0.39f);
            AddAdminMetric(container, body, "Active Calls", activeCalls.Count + " active, " + CountActiveHeavyStrikes() + " heavy", "Max " + config.General.MaxSimultaneousStrikes + " total / " + config.General.MaxSimultaneousHeavyStrikes + " heavy.", 0.05f, 0.16f);
            AddAdminMetric(container, body, "Safety", "range " + FormatMeters(config.General.MaxCallRange) + ", min " + FormatMeters(config.General.MinimumDistanceFromCaller), "Safe zones " + BoolText(config.General.BlockSafeZones) + ", monuments " + BoolText(config.General.BlockMonuments) + ".", 0.52f, 0.16f);
        }

        private void DrawAdminGiveTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Give Items", "Search online/sleeping players and grant charged targeting binoculars.", "page.give");
            AddAdminInput(container, body, state.GiveSearch, "portableairstrikes.adminfield give_search ", "0.05 0.740", "0.40 0.800", 11, 64, TextAnchor.MiddleLeft);
            AddAdminTipButton(container, body, "field.give_search", "0.405 0.753", "0.428 0.790");
            AddUiButton(container, body, "Amount " + state.GiveAmount, "portableairstrikes.adminui numberedit give_amount", "0.445 0.740", "0.560 0.800", "0.015 0.018 0.024 0.92", 10);
            AddAdminTipButton(container, body, "field.give_amount", "0.565 0.753", "0.588 0.790");
            AddUiButton(container, body, "+1", "portableairstrikes.adminfield give_amount " + (state.GiveAmount + 1), "0.605 0.740", "0.665 0.800", "0.16 0.27 0.18 0.95", 10);
            AddUiButton(container, body, "+5", "portableairstrikes.adminfield give_amount " + (state.GiveAmount + 5), "0.680 0.740", "0.740 0.800", "0.16 0.27 0.18 0.95", 10);
            AddUiButton(container, body, "Give Search", "portableairstrikes.adminui give search", "0.790 0.740", "0.95 0.800", "0.42 0.18 0.12 0.95", 10);

            AddUiLabel(container, body, "Filter", 9, TextAnchor.MiddleLeft, "0.055 0.675", "0.12 0.713", "0.70 0.78 0.84 1");
            AddAdminTipButton(container, body, "field.give_filter", "0.122 0.682", "0.142 0.708");
            AddAdminOptionButton(container, body, "All", "portableairstrikes.adminui givefilter all", "0.145 0.675", "0.225 0.713", string.Equals(state.GiveFilter, "all", StringComparison.OrdinalIgnoreCase));
            AddAdminOptionButton(container, body, "Online", "portableairstrikes.adminui givefilter online", "0.235 0.675", "0.335 0.713", string.Equals(state.GiveFilter, "online", StringComparison.OrdinalIgnoreCase));
            AddAdminOptionButton(container, body, "Sleeping", "portableairstrikes.adminui givefilter sleeping", "0.345 0.675", "0.465 0.713", string.Equals(state.GiveFilter, "sleeping", StringComparison.OrdinalIgnoreCase));

            AddUiLabel(container, body, "Sort", 9, TextAnchor.MiddleLeft, "0.525 0.675", "0.58 0.713", "0.70 0.78 0.84 1");
            AddAdminTipButton(container, body, "field.give_sort", "0.582 0.682", "0.602 0.708");
            AddAdminOptionButton(container, body, "Name", "portableairstrikes.adminui givesort name", "0.610 0.675", "0.690 0.713", string.Equals(state.GiveSort, "name", StringComparison.OrdinalIgnoreCase));
            AddAdminOptionButton(container, body, "Steam ID", "portableairstrikes.adminui givesort steamid", "0.700 0.675", "0.815 0.713", string.Equals(state.GiveSort, "steamid", StringComparison.OrdinalIgnoreCase));
            AddAdminOptionButton(container, body, "State", "portableairstrikes.adminui givesort state", "0.825 0.675", "0.910 0.713", string.Equals(state.GiveSort, "state", StringComparison.OrdinalIgnoreCase));

            var matches = FindAdminPlayerMatches(state.GiveSearch, state.GiveFilter, state.GiveSort);
            var pageCount = Math.Max(1, (matches.Count + AdminGiveRows - 1) / AdminGiveRows);
            if (state.GivePage >= pageCount)
            {
                state.GivePage = pageCount - 1;
            }

            AddAdminPanel(container, body, "0.05 0.575", "0.95 0.620", "0.040 0.047 0.058 0.96");
            AddUiLabel(container, body, "Player", 9, TextAnchor.MiddleLeft, "0.075 0.580", "0.42 0.615", "1 0.86 0.58 1");
            AddUiLabel(container, body, "Steam ID", 9, TextAnchor.MiddleLeft, "0.435 0.580", "0.62 0.615", "1 0.86 0.58 1");
            AddUiLabel(container, body, "State", 9, TextAnchor.MiddleCenter, "0.635 0.580", "0.735 0.615", "1 0.86 0.58 1");
            AddUiLabel(container, body, "Action", 9, TextAnchor.MiddleCenter, "0.755 0.580", "0.925 0.615", "1 0.86 0.58 1");

            if (matches.Count == 0)
            {
                AddUiLabel(container, body, "No matching players. Search by display name or Steam ID, or leave search blank to show players.", 12, TextAnchor.MiddleCenter, "0.05 0.36", "0.95 0.52", "0.80 0.86 0.90 1");
                return;
            }

            var start = state.GivePage * AdminGiveRows;
            var shown = 0;
            for (var i = start; i < matches.Count && shown < AdminGiveRows; i++, shown++)
            {
                var target = matches[i];
                var yMax = 0.555f - shown * 0.063f;
                var yMin = yMax - 0.050f;
                AddAdminPanel(container, body, "0.05 " + FormatUiFloat(yMin), "0.95 " + FormatUiFloat(yMax), shown % 2 == 0 ? "0.075 0.086 0.100 0.94" : "0.065 0.074 0.088 0.94");
                AddUiLabel(container, body, ShortenAdminText(GetAdminPlayerDisplayName(target), 32), 10, TextAnchor.MiddleLeft, "0.075 " + FormatUiFloat(yMin), "0.42 " + FormatUiFloat(yMax), target.IsConnected ? "1 1 1 1" : "0.72 0.76 0.80 1");
                AddUiLabel(container, body, target.UserIDString, 9, TextAnchor.MiddleLeft, "0.435 " + FormatUiFloat(yMin), "0.62 " + FormatUiFloat(yMax), "0.70 0.78 0.84 1");
                AddUiLabel(container, body, target.IsConnected ? "Online" : "Sleeping", 9, TextAnchor.MiddleCenter, "0.635 " + FormatUiFloat(yMin), "0.735 " + FormatUiFloat(yMax), target.IsConnected ? "0.62 0.90 0.66 1" : "0.72 0.76 0.80 1");
                AddUiButton(container, body, "Give " + state.GiveAmount, "portableairstrikes.adminui give " + target.UserIDString, "0.765 " + FormatUiFloat(yMin + 0.006f), "0.925 " + FormatUiFloat(yMax - 0.006f), "0.42 0.18 0.12 0.95", 10);
            }

            AddUiLabel(container, body, matches.Count + " match(es) | page " + (state.GivePage + 1) + "/" + pageCount, 9, TextAnchor.MiddleLeft, "0.055 0.045", "0.40 0.095", "0.70 0.78 0.84 1");
            AddUiButton(container, body, "Prev", state.GivePage > 0 ? "portableairstrikes.adminui givepage prev" : "", "0.685 0.045", "0.795 0.095", state.GivePage > 0 ? "0.13 0.18 0.24 0.95" : "0.09 0.10 0.11 0.75", 10);
            AddUiButton(container, body, "Next", state.GivePage + 1 < pageCount ? "portableairstrikes.adminui givepage next" : "", "0.815 0.045", "0.925 0.095", state.GivePage + 1 < pageCount ? "0.13 0.18 0.24 0.95" : "0.09 0.10 0.11 0.75", 10);
        }

        private void DrawAdminStrikesTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Strikes", "Economy, eligibility, warnings, cooldowns, target types, and profile assignment status.", "page.strikes");
            AddUiButton(container, body, "Add Strike", "portableairstrikes.adminui addstrike", "0.300 0.750", "0.430 0.795", "0.13 0.18 0.24 0.95", 9);
            AddAdminTipButton(container, body, "field.strike.add", "0.435 0.758", "0.456 0.788");
            var list = AddAdminScrollView(container, body, "0.04 0.08", "0.43 0.735", Math.Max(620f, GetSortedStrikeIds().Count * 62f + 20f));
            var ids = GetSortedStrikeIds();
            for (var i = 0; i < ids.Count; i++)
            {
                StrikeDefinition rowStrike;
                if (!TryGetStrike(ids[i], out rowStrike))
                {
                    continue;
                }

                var top = 10f + i * 62f;
                AddAdminStrikeListRow(container, list, state, rowStrike, top, top + 54f);
            }

            StrikeDefinition strike;
            if (!TryGetStrike(state.SelectedStrikeId, out strike))
            {
                AddUiLabel(container, body, "No strike is selected.", 13, TextAnchor.MiddleCenter, "0.48 0.42", "0.95 0.55", "1 1 1 1");
                return;
            }

            var compatible = IsStrikeExecutorCompatible(strike);
            var includedProfiles = GetEnabledStrikeProfileAssignments(strike).Count;
            AddAdminPanel(container, body, "0.47 0.08", "0.96 0.80", "0.045 0.052 0.064 0.96");
            AddUiLabel(container, body, strike.DisplayName + " (" + strike.Id + ")", 14, TextAnchor.MiddleLeft, "0.50 0.735", "0.84 0.785", "1 0.86 0.58 1");
            AddUiButton(container, body, strike.Enabled ? "Enabled" : "Disabled", "portableairstrikes.adminui togglestrike " + strike.Id, "0.85 0.735", "0.94 0.785", strike.Enabled ? "0.15 0.34 0.18 0.95" : "0.42 0.14 0.10 0.95", 9);
            AddUiLabel(container, body, compatible ? "Profiles " + includedProfiles + " | accepts " + FormatAcceptedTargetTypes(strike) : GetStrikeCompatibilityMessage(strike), 10, TextAnchor.MiddleLeft, "0.50 0.690", "0.94 0.730", compatible ? "0.62 0.90 0.66 1" : "1 0.55 0.45 1");

            AddAdminTextFieldRow(container, body, "Name", strike.DisplayName, "portableairstrikes.adminfield strike.display " + strike.Id + " ", 0.632f);
            AddAdminDetailNumberRow(container, body, "RP", strike.RPCost.ToString(CultureInfo.InvariantCulture), "portableairstrikes.adminfield strike.rpcost " + strike.Id + " ", 0.572f, "Tier", strike.Tier.ToString(CultureInfo.InvariantCulture), "portableairstrikes.adminfield strike.tier " + strike.Id + " ");
            AddAdminTextFieldRow(container, body, "Permission", strike.PermissionRequired ?? "", "portableairstrikes.adminfield strike.permission " + strike.Id + " ", 0.512f);

            AddUiLabel(container, body, "Accepted Targets", 9, TextAnchor.MiddleLeft, "0.50 0.455", "0.68 0.493", "0.70 0.78 0.84 1");
            AddAdminTargetTypeButton(container, body, strike, "ground_ping", "Ground", "0.675 0.455", "0.745 0.493");
            AddAdminTargetTypeButton(container, body, strike, "vehicle_ping", "Vehicle", "0.752 0.455", "0.827 0.493");
            AddAdminTargetTypeButton(container, body, strike, "player_ping", "Player", "0.834 0.455", "0.895 0.493");
            AddAdminTargetTypeButton(container, body, strike, "npc_ping", "NPC", "0.902 0.455", "0.940 0.493");

            AddUiLabel(container, body, "Strike Profiles", 9, TextAnchor.MiddleLeft, "0.50 0.400", "0.66 0.438", "0.70 0.78 0.84 1");
            AddUiButton(container, body, includedProfiles + " included", "portableairstrikes.adminui tab visuals", "0.665 0.400", "0.795 0.438", "0.13 0.18 0.24 0.95", 8);
            var deleteConfirm = string.Equals(state.DeleteConfirmStrikeId, strike.Id, StringComparison.OrdinalIgnoreCase);
            AddUiButton(container, body, deleteConfirm ? "Confirm Delete" : "Delete", deleteConfirm ? "portableairstrikes.adminui confirmdelete " + strike.Id : "portableairstrikes.adminui deletestrike " + strike.Id, "0.810 0.400", "0.940 0.438", deleteConfirm ? "0.70 0.12 0.08 0.95" : "0.42 0.14 0.10 0.95", 8);

            AddAdminDetailNumberRow(container, body, "Warn", FormatFloat(strike.WarningDelaySeconds), "portableairstrikes.adminfield strike.warning " + strike.Id + " ", 0.330f, "Player Cooldown", FormatFloat(strike.CooldownPerPlayerSeconds), "portableairstrikes.adminfield strike.playercd " + strike.Id + " ");
            AddAdminDetailNumberRow(container, body, "Clan Cooldown", FormatFloat(strike.CooldownPerClanSeconds), "portableairstrikes.adminfield strike.clancd " + strike.Id + " ", 0.270f, "Global Cooldown", FormatFloat(strike.GlobalCooldownSeconds), "portableairstrikes.adminfield strike.globalcd " + strike.Id + " ");
            AddUiLabel(container, body, "Payloads, release timing, spread, damage, and max counts come from included strike profiles. Use Balance for wrapper multipliers and per-profile caps.", 9, TextAnchor.MiddleLeft, "0.50 0.135", "0.94 0.205", "0.62 0.70 0.76 1");
        }

        private void DrawAdminBalanceTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Balance", "Strike wrapper limits, profile start delays, and positive multipliers.", "page.balance");
            StrikeDefinition strike;
            if (!TryGetStrike(state.SelectedStrikeId, out strike))
            {
                AddUiLabel(container, body, "No strike selected.", 13, TextAnchor.MiddleCenter, "0.05 0.40", "0.95 0.55", "1 1 1 1");
                return;
            }

            AddUiLabel(container, body, strike.DisplayName + " (" + strike.Id + ")", 15, TextAnchor.MiddleLeft, "0.05 0.760", "0.62 0.805", "1 0.86 0.58 1");
            AddUiButton(container, body, "Strike Profiles", "portableairstrikes.adminui tab visuals", "0.80 0.760", "0.94 0.805", "0.13 0.18 0.24 0.95", 10);

            var assignments = GetStrikeProfileAssignments(strike);
            if (assignments.Count == 0)
            {
                AddUiLabel(container, body, "No strike profiles are included yet.", 12, TextAnchor.MiddleLeft, "0.05 0.680", "0.70 0.725", "0.90 0.74 0.55 1");
            }

            var rowY = 0.635f;
            var shown = 0;
            foreach (var assignment in assignments)
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.ProfileId) || shown >= 4)
                {
                    continue;
                }

                var cap = GetProfilePayloadLimitCap(assignment.ProfileId, strike);
                AddUiLabel(container, body, ShortenAdminText(assignment.ProfileId + " base max " + cap, 42), 9, TextAnchor.MiddleLeft, "0.055 " + FormatUiFloat(rowY + 0.053f), "0.455 " + FormatUiFloat(rowY + 0.088f), "0.72 0.82 0.88 1");
                AddAdminNumberRow(container, body, "Limit", assignment.PayloadCountLimit <= 0 ? "profile" : assignment.PayloadCountLimit.ToString(CultureInfo.InvariantCulture), "portableairstrikes.adminfield strikeprofile.limit " + strike.Id + "|" + assignment.ProfileId + " ", rowY, "Delay", FormatFloat(assignment.StartDelaySeconds), "portableairstrikes.adminfield strikeprofile.delay " + strike.Id + "|" + assignment.ProfileId + " ");
                rowY -= 0.090f;
                shown++;
            }

            var y = 0.300f;
            if (StrikeProfilesHaveSpread(strike))
            {
                AddAdminNumberRow(container, body, "Spread x", FormatFloat(strike.SpreadMultiplier), "portableairstrikes.adminfield strike.spreadmult " + strike.Id + " ", y, "Impact x", FormatFloat(strike.ImpactRadiusMultiplier), "portableairstrikes.adminfield strike.impactmult " + strike.Id + " ");
                y -= 0.060f;
            }

            if (StrikeProfilesHaveDamage(strike))
            {
                AddAdminNumberRow(container, body, "Damage x", FormatFloat(strike.DamageMultiplier), "portableairstrikes.adminfield strike.damagemult " + strike.Id + " ", y, "Vehicle x", FormatFloat(strike.VehicleDamageMultiplier), "portableairstrikes.adminfield strike.vehicledamagemult " + strike.Id + " ");
                y -= 0.060f;
                AddAdminNumberRow(container, body, "Splash x", FormatFloat(strike.SplashRadiusMultiplier), "portableairstrikes.adminfield strike.splashmult " + strike.Id + " ", y, "Players", FormatFloat(GetStrikeDamageScale(strike, "Players")), "portableairstrikes.adminfield strike.d_players " + strike.Id + " ");
                y -= 0.060f;
                AddAdminNumberRow(container, body, "Buildings", FormatFloat(GetStrikeDamageScale(strike, "Buildings")), "portableairstrikes.adminfield strike.d_buildings " + strike.Id + " ", y, "Vehicles", FormatFloat(GetStrikeDamageScale(strike, "Vehicles")), "portableairstrikes.adminfield strike.d_vehicles " + strike.Id + " ");
                y -= 0.060f;
            }

            if (StrikeProfilesHaveHoming(strike))
            {
                AddAdminNumberRow(container, body, "Track Sec x", FormatFloat(strike.TrackingSecondsMultiplier), "portableairstrikes.adminfield strike.tracktimemult " + strike.Id + " ", y, "Track Dist x", FormatFloat(strike.TrackingDistanceMultiplier), "portableairstrikes.adminfield strike.trackdistancemult " + strike.Id + " ");
                y -= 0.060f;
            }

            if (StrikeProfilesHaveA10(strike))
            {
                AddAdminNumberRow(container, body, "Line x", FormatFloat(strike.LineLengthMultiplier), "portableairstrikes.adminfield strike.linemult " + strike.Id + " ", y, "Width x", FormatFloat(strike.WidthMultiplier), "portableairstrikes.adminfield strike.widthmult " + strike.Id + " ");
                y -= 0.060f;
                AddAdminNumberRow(container, body, "Pulse x", FormatFloat(strike.PulseDelayMultiplier), "portableairstrikes.adminfield strike.pulsemult " + strike.Id + " ", y, "", "", "");
            }
        }

        private void AddAdminTargetTypeButton(CuiElementContainer container, string parent, StrikeDefinition strike, string targetType, string label, string anchorMin, string anchorMax)
        {
            var parsed = ParseTargetType(targetType);
            var selected = StrikeAcceptsTargetType(strike, parsed);
            AddUiButton(container, parent, label, "portableairstrikes.adminui targettype " + strike.Id + " " + targetType, anchorMin, anchorMax, selected ? "0.15 0.34 0.18 0.95" : "0.13 0.18 0.24 0.95", 8);
        }

        private bool StrikeProfilesHaveSpread(StrikeDefinition strike)
        {
            return StrikeProfilesHavePayload(strike, payload =>
            {
                HomingMissileSpec homing;
                return !TryGetHomingMissileSpec(payload, out homing);
            });
        }

        private bool StrikeProfilesHaveDamage(StrikeDefinition strike)
        {
            return StrikeProfilesHavePayload(strike, payload =>
            {
                return !IsUtilityOnlyPayload(payload);
            });
        }

        private bool StrikeProfilesHaveHoming(StrikeDefinition strike)
        {
            return StrikeProfilesHavePayload(strike, payload =>
            {
                HomingMissileSpec homing;
                return TryGetHomingMissileSpec(payload, out homing);
            });
        }

        private bool StrikeProfilesHaveA10(StrikeDefinition strike)
        {
            return StrikeProfilesHavePayload(strike, payload =>
            {
                A10StrafeSpec a10;
                return TryGetA10StrafeSpec(payload, out a10);
            });
        }

        private delegate bool PayloadFeaturePredicate(string payload);

        private bool StrikeProfilesHavePayload(StrikeDefinition strike, PayloadFeaturePredicate predicate)
        {
            if (strike == null || predicate == null)
            {
                return false;
            }

            foreach (var assignment in GetStrikeProfileAssignments(strike))
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.ProfileId))
                {
                    continue;
                }

                VisualProfileConfig profile;
                if (!TryGetVisualProfileById(assignment.ProfileId, out profile))
                {
                    continue;
                }

                foreach (var payload in GetProfilePayloadIds(profile, strike.Payload))
                {
                    if (predicate(payload))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private List<string> GetProfilePayloadIds(VisualProfileConfig profile, string fallbackPayload)
        {
            var payloads = new List<string>();
            if (profile == null)
            {
                var fallback = NormalizePayloadId(fallbackPayload);
                if (IsSupportedVisualPayload(fallback))
                {
                    payloads.Add(fallback);
                }

                return payloads;
            }

            if (string.Equals(profile.PayloadReleaseMode, "generated", StringComparison.OrdinalIgnoreCase))
            {
                AddProfilePayloadId(payloads, GetReleasePayload(profile.ReleaseTemplate, fallbackPayload));
            }

            if (profile.CompiledReleaseEvents != null)
            {
                foreach (var payloadEvent in profile.CompiledReleaseEvents)
                {
                    AddProfilePayloadId(payloads, GetReleasePayload(payloadEvent, fallbackPayload));
                }
            }

            if (profile.PayloadEvents != null)
            {
                foreach (var payloadEvent in profile.PayloadEvents)
                {
                    AddProfilePayloadId(payloads, GetReleasePayload(payloadEvent, fallbackPayload));
                }
            }

            if (payloads.Count == 0)
            {
                AddProfilePayloadId(payloads, fallbackPayload);
            }

            return payloads;
        }

        private void AddProfilePayloadId(List<string> payloads, string payload)
        {
            payload = NormalizePayloadId(payload);
            if (payloads == null || !IsSupportedVisualPayload(payload) || payloads.Contains(payload))
            {
                return;
            }

            payloads.Add(payload);
        }

        private bool IsUtilityOnlyPayload(string payload)
        {
            var normalized = NormalizePayloadId(payload);
            if (string.Equals(normalized, "smoke", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "flashbang", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private void DrawAdminSafetyTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Safety", "Target validation, concurrency, warnings, cancellation, item, and economy controls.", "page.safety");
            AddAdminNumberRow(container, body, "Max Range", FormatFloat(config.General.MaxCallRange), "portableairstrikes.adminfield general.maxrange ", 0.700f, "Min Distance", FormatFloat(config.General.MinimumDistanceFromCaller), "portableairstrikes.adminfield general.mindistance ");
            AddAdminNumberRow(container, body, "Safe Radius", FormatFloat(config.General.SafeZoneBlockRadius), "portableairstrikes.adminfield general.safezoneradius ", 0.640f, "Warning Delay", FormatFloat(config.General.DefaultWarningDelaySeconds), "portableairstrikes.adminfield general.warningdelay ");
            AddAdminNumberRow(container, body, "Max Active", config.General.MaxSimultaneousStrikes.ToString(CultureInfo.InvariantCulture), "portableairstrikes.adminfield general.maxsim ", 0.580f, "Max Heavy", config.General.MaxSimultaneousHeavyStrikes.ToString(CultureInfo.InvariantCulture), "portableairstrikes.adminfield general.maxheavy ");
            AddAdminNumberRow(container, body, "Nearby Radius", FormatFloat(config.General.NearbyHeavyStrikeWarningRadius), "portableairstrikes.adminfield general.nearbyradius ", 0.520f, "History", config.General.RecentCallHistoryLimit.ToString(CultureInfo.InvariantCulture), "portableairstrikes.adminfield general.history ");
            AddAdminNumberRow(container, body, "Monument Pad", FormatFloat(config.General.MonumentBlockRadiusPadding), "portableairstrikes.adminfield general.monumentpadding ", 0.460f, "Default Zone", FormatFloat(config.General.DefaultMonumentBlockRadius), "portableairstrikes.adminfield general.monumentdefault ");

            AddAdminToggleGrid(container, body, new[]
            {
                "Require Ping:general.requireping",
                "LOS Required:general.los",
                "Safe Zones:general.safezones",
                "Monuments:general.monuments",
                "Heavy Only:general.monumentsheavy",
                "Clan CDs:general.clancd",
                "Global CDs:general.globalcd",
                "Team Warn:general.teamwarn",
                "Nearby Warn:general.nearbywarn",
                "Cancel:general.cancel",
                "Refund Cancel:general.refundcancel",
                "Debug:general.debug",
                "Currency:currency.enabled",
                "Free Admin:currency.freeadmin",
                "Consume Item:item.consume",
                "Admin Bypass:item.adminbypass"
            }, 0.050f, 0.235f);

            AddUiLabel(container, body, "Currency provider", 9, TextAnchor.MiddleLeft, "0.055 0.018", "0.200 0.058", "0.70 0.78 0.84 1");
            AddAdminTipButton(container, body, "field.currency.provider", "0.205 0.024", "0.225 0.052");
            AddUiButton(container, body, config.Currency.Provider, "portableairstrikes.adminui toggle currency.provider", "0.23 0.018", "0.42 0.058", "0.13 0.18 0.24 0.95", 9);
        }

        private void DrawAdminVisualsTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Strike Profiles", "Include authored strike profiles and keep runtime visual gates available.", "page.visuals");
            AddAdminToggleGrid(container, body, new[]
            {
                "Visuals:visual.enabled",
                "Drones:visual.drones",
                "Aircraft:visual.aircraft",
                "Mortars:visual.mortars",
                "Crew NPC:visual.crew",
                "Sounds:visual.sounds",
                "Rotor Wash:visual.rotor",
                "Destroyable:visual.destroyable",
                "Require Carrier:visual.requirecarrier",
                "Refund Carrier:visual.refundcarrier"
            }, 0.050f, 0.750f);

            DrawAdminProfileAssignment(container, body, state);
        }

        private void DrawAdminProfileAssignment(CuiElementContainer container, string body, AdminUiState state)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(state.SelectedStrikeId, out strike))
            {
                return;
            }

            AddAdminPanel(container, body, "0.05 0.045", "0.955 0.590", "0.040 0.047 0.058 0.96");
            var editorLoaded = IsAnimationEditorLoaded();
            var included = GetEnabledStrikeProfileAssignments(strike).Count;
            var profileStatus = "Selected " + strike.Id + " | included " + included + " | accepts " + FormatAcceptedTargetTypes(strike) + ".";
            AddUiLabel(container, body, "Loaded Strike Profiles", 12, TextAnchor.MiddleLeft, "0.075 0.545", "0.32 0.580", "1 0.86 0.58 1");
            AddUiLabel(container, body, profileStatus, 9, TextAnchor.MiddleLeft, "0.075 0.508", "0.70 0.540", "0.68 0.76 0.82 1");
            AddUiButton(container, body, "Open", editorLoaded && !string.IsNullOrWhiteSpace(strike.VisualProfileId) ? "portableairstrikes.adminui openeditor " + strike.Id : "", "0.705 0.520", "0.775 0.565", editorLoaded && !string.IsNullOrWhiteSpace(strike.VisualProfileId) ? "0.13 0.18 0.24 0.95" : "0.09 0.10 0.11 0.75", 8);
            AddUiButton(container, body, "Create/Open", editorLoaded ? "portableairstrikes.adminui createprofile " + strike.Id : "", "0.785 0.520", "0.895 0.565", editorLoaded ? "0.42 0.18 0.12 0.95" : "0.09 0.10 0.11 0.75", 8);
            AddUiButton(container, body, "Reload", "portableairstrikes.adminui reloadprofiles", "0.905 0.520", "0.940 0.565", "0.13 0.18 0.24 0.95", 8);

            if (visualProfileFile == null || visualProfileFile.Profiles == null || visualProfileFile.Profiles.Count == 0)
            {
                AddUiLabel(container, body, "No profiles are loaded from VisualProfiles.json.", 10, TextAnchor.MiddleCenter, "0.095 0.245", "0.910 0.330", "0.80 0.86 0.90 1");
                return;
            }

            var profileIds = new List<string>(visualProfileFile.Profiles.Keys);
            profileIds.Sort(StringComparer.OrdinalIgnoreCase);
            var rowCount = profileIds.Count;
            var list = AddAdminScrollView(container, body, "0.070 0.070", "0.935 0.485", Math.Max(410f, rowCount * 56f + 14f));
            for (var i = 0; i < profileIds.Count; i++)
            {
                var profileId = profileIds[i];
                var top = 8f + i * 56f;
                VisualProfileConfig profile;
                TryGetVisualProfileById(profileId, out profile);
                var homingBlocked = profile != null && ProfileContainsHomingPayload(profile, strike.Payload) && !StrikeAcceptsTargetType(strike, AirstrikeTargetType.VehiclePing);
                var detail = BuildAdminProfileListDetail(profileId) + (homingBlocked ? " | requires vehicle ping" : "");
                StrikeProfileAssignment assignment;
                var selected = TryGetStrikeProfileAssignment(strike, profileId, out assignment);
                var command = homingBlocked && !selected ? "" : "portableairstrikes.adminui profiletoggle " + strike.Id + " " + profileId;
                AddAdminProfileListRow(container, list, strike, profileId, detail, selected, command, top, top + 50f, selected || !homingBlocked);
            }

            if (profileIds.Count == 0)
            {
                AddUiLabel(container, body, "No profiles are loaded. Create one in the animation editor or reload profiles.", 10, TextAnchor.MiddleCenter, "0.095 0.245", "0.910 0.330", "0.80 0.86 0.90 1");
            }
        }

        private void DrawAdminLootAuditTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Loot/Audit", "Optional loot injection and Discord audit mirroring controls.", "page.lootaudit");
            AddAdminToggleGrid(container, body, new[]
            {
                "Loot Enabled:loot.enabled",
                "Audit Webhook:audit.enabled",
                "Started:audit.started",
                "Completed:audit.completed",
                "Failures:audit.failures",
                "Cancels:audit.cancels",
                "Validation:audit.validation"
            }, 0.050f, 0.705f);

            AddAdminLootRuleRows(container, body);
            AddAdminTextFieldRow(container, body, "Webhook URL", config.AuditWebhooks.DiscordWebhookUrl ?? "", "portableairstrikes.adminfield audit.url ", 0.330f);
            AddAdminTextFieldRow(container, body, "Username", config.AuditWebhooks.Username ?? "", "portableairstrikes.adminfield audit.username ", 0.270f);
            AddAdminTextFieldRow(container, body, "Mention", config.AuditWebhooks.MentionText ?? "", "portableairstrikes.adminfield audit.mention ", 0.210f);
            AddAdminTextFieldRow(container, body, "Avatar URL", config.AuditWebhooks.AvatarUrl ?? "", "portableairstrikes.adminfield audit.avatar ", 0.150f);
        }

        private void DrawAdminActivityTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Activity", "Active calls, recent audit records, and stored counters.", "page.activity");
            AddUiLabel(container, body, "Active calls: " + activeCalls.Count + " total, " + CountActiveHeavyStrikes() + " heavy.", 11, TextAnchor.MiddleLeft, "0.05 0.765", "0.95 0.810", "1 1 1 1");
            var y = 0.705f;
            foreach (var context in activeCalls.Values)
            {
                if (context == null || context.Strike == null)
                {
                    continue;
                }

                AddUiLabel(container, body, context.CallerName + " -> " + context.Strike.Id + " " + context.State + " age " + FormatSeconds(GetNow() - context.CreatedAt), 9, TextAnchor.MiddleLeft, "0.06 " + FormatUiFloat(y), "0.94 " + FormatUiFloat(y + 0.035f), "0.72 0.82 0.88 1");
                y -= 0.040f;
                if (y < 0.565f)
                {
                    break;
                }
            }

            AddUiLabel(container, body, "Recent Calls", 12, TextAnchor.MiddleLeft, "0.05 0.515", "0.35 0.555", "1 0.86 0.58 1");
            var records = storedData.RecentCalls ?? new List<StrikeCallAuditRecord>();
            var shown = 0;
            for (var i = records.Count - 1; i >= 0 && shown < AdminActivityRows; i--, shown++)
            {
                var record = records[i];
                var rowY = 0.470f - shown * 0.041f;
                AddUiLabel(container, body, ShortenAdminText(FormatAuditRecordForChat(record), 140), 8, TextAnchor.MiddleLeft, "0.06 " + FormatUiFloat(rowY), "0.94 " + FormatUiFloat(rowY + 0.032f), "0.66 0.74 0.80 1");
            }

            AddUiLabel(container, body, "Stats: " + BuildAdminStatsSummary(), 9, TextAnchor.MiddleLeft, "0.05 0.055", "0.95 0.100", "0.72 0.82 0.88 1");
        }

        private void DrawAdminCommandsTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Commands", "Reference for player chat commands and server/admin console commands.", "page.commands");

            var scope = NormalizeAdminCommandScope(state.CommandScope);
            AddAdminOptionButton(container, body, "/" + GetOpenCommand() + " Commands", "portableairstrikes.adminui cmdscope chat", "0.05 0.750", "0.245 0.798", scope == "chat");
            AddAdminOptionButton(container, body, "Console Commands", "portableairstrikes.adminui cmdscope console", "0.260 0.750", "0.455 0.798", scope == "console");

            var entries = BuildAdminCommandHelpEntries(scope);
            var categories = GetAdminCommandCategories(entries);
            if (categories.Count == 0)
            {
                AddUiLabel(container, body, "No command help entries are available.", 12, TextAnchor.MiddleCenter, "0.05 0.40", "0.95 0.55", "0.80 0.86 0.90 1");
                return;
            }

            var selectedCategory = NormalizeAdminCommandCategory(state.CommandCategory);
            if (!AdminCommandCategoryExists(categories, selectedCategory))
            {
                selectedCategory = categories[0].Category;
                state.CommandCategory = selectedCategory;
            }

            for (var i = 0; i < categories.Count && i < 6; i++)
            {
                var category = categories[i];
                var xMin = 0.05f + i * 0.145f;
                var xMax = Math.Min(0.94f, xMin + 0.135f);
                AddAdminOptionButton(container, body, category.CategoryLabel, "portableairstrikes.adminui cmdcat " + category.Category, FormatUiFloat(xMin) + " 0.690", FormatUiFloat(xMax) + " 0.732", selectedCategory == category.Category);
            }

            var shownEntries = new List<AdminCommandHelpEntry>();
            foreach (var entry in entries)
            {
                if (entry != null && string.Equals(entry.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    shownEntries.Add(entry);
                }
            }

            var list = AddAdminScrollView(container, body, "0.05 0.070", "0.95 0.660", Math.Max(520f, shownEntries.Count * 62f + 18f));
            for (var i = 0; i < shownEntries.Count; i++)
            {
                AddAdminCommandHelpRow(container, list, shownEntries[i], 8f + i * 62f, 8f + i * 62f + 54f);
            }
        }

        private void DrawAdminHelpTab(CuiElementContainer container, string body, BasePlayer player, AdminUiState state)
        {
            AddAdminTitle(container, body, "Help", "How the plugin operates and how the admin panel is meant to be used.", "page.help");

            var rows = new[]
            {
                "Player flow|Admins grant Airstrike Targeting Binocular charges. A player holds the binoculars, places a ping, and the plugin stores that target.",
                "Selection flow|If the player has a saved default, the target-compatible strike starts automatically. Otherwise the selection menu opens for that target.",
                "Validation|Before launch the plugin checks permission, item charge, RP cost, cooldowns, range, safe-zone, monument, active-strike limits, and target type.",
                "Execution|Accepted calls may warn team/nearby players, place a marker for heavy strikes, then run every included strike profile for the selected wrapper.",
                "Strikes tab|Use Strikes for the wrapper: display name, permission, RP, tier, accepted target types, warnings, cooldowns, and which profiles are included.",
                "Balance tab|Use Balance for wrapper multipliers and per-profile start delay/count limit. The authored profile still owns payload timing and release shape.",
                "Strike Profiles tab|Include authored profiles from VisualProfiles.json, open/create them in the animation editor, and reload profile data after external edits.",
                "Safety tab|Tune guardrails such as range, minimum distance, safe zones, monuments, concurrent calls, warning fanout, cancellation, currency, and item behavior.",
                "Give Items tab|Search online or sleeping players, set the number of charges, then grant the charge-backed binocular item without leaving the admin panel.",
                "Loot/Audit tab|Control optional loot injection and Discord webhook mirroring. Activity shows current calls, recent audit rows, and stored counters.",
                "Editing tips|Click number fields to open the exact-value keypad. Click a small ? button to send a field explanation to the status line at the bottom."
            };

            var list = AddAdminScrollView(container, body, "0.05 0.070", "0.95 0.805", Math.Max(620f, rows.Length * 66f + 18f));
            for (var i = 0; i < rows.Length; i++)
            {
                var parts = rows[i].Split(new[] { '|' }, 2);
                var title = parts.Length > 0 ? parts[0] : "";
                var text = parts.Length > 1 ? parts[1] : "";
                AddAdminHelpRow(container, list, title, text, 8f + i * 66f, 8f + i * 66f + 58f);
            }
        }

        private List<AdminCommandHelpEntry> BuildAdminCommandHelpEntries(string scope)
        {
            scope = NormalizeAdminCommandScope(scope);
            var entries = new List<AdminCommandHelpEntry>();
            var open = "/" + GetOpenCommand();

            if (scope == "chat")
            {
                entries.Add(new AdminCommandHelpEntry(scope, "core", "Core", open, "Open the target-aware airstrike selection menu."));
                entries.Add(new AdminCommandHelpEntry(scope, "core", "Core", open + " <strikeId>", "Call a specific strike against your stored target when direct commands are enabled."));
                entries.Add(new AdminCommandHelpEntry(scope, "core", "Core", open + " list", "Show enabled strike IDs, display names, tier, and RP cost."));
                entries.Add(new AdminCommandHelpEntry(scope, "core", "Core", open + " balance", "Show your current RP/currency balance for airstrike calls."));
                entries.Add(new AdminCommandHelpEntry(scope, "core", "Core", open + " status", "Show your active airstrike call state, timing, target, and cancel hint."));
                entries.Add(new AdminCommandHelpEntry(scope, "core", "Core", open + " cancel", "Cancel your active call before impact when cancellation is enabled."));
                entries.Add(new AdminCommandHelpEntry(scope, "core", "Core", open + " last", "Repeat your last successful strike when repeat-last is enabled."));

                entries.Add(new AdminCommandHelpEntry(scope, "defaults", "Defaults", open + " default", "Show your saved binocular default strike."));
                entries.Add(new AdminCommandHelpEntry(scope, "defaults", "Defaults", open + " default <strikeId>", "Save a default strike for future binocular ping calls."));
                entries.Add(new AdminCommandHelpEntry(scope, "defaults", "Defaults", open + " default clear", "Clear your saved binocular default so the next ping opens selection."));

                entries.Add(new AdminCommandHelpEntry(scope, "admin", "Admin", open + " admin", "Open this Portable Airstrikes admin panel.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "admin", "Admin", open + " giveitem <playerNameOrSteamId> [amount]", "Give charge-backed targeting binoculars to a player.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "admin", "Admin", open + " reload", "Reload config, permissions, currency adapter, executors, monument zones, and item definition.", true));

                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug", "Show the debug command summary.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debugping", "Store a raycast target for admin testing.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug target", "Show your stored target, age, and source.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug item", "Inspect CID/item registration, icon, stack, and fallback state.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug cooldowns", "Show player, clan, and global cooldown record counts.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug strikes", "Show enabled/configured strike counts.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug history [count]", "Show recent audit records, newest first.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug stats", "Show stored airstrike counters.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug active", "Show active calls and target/state details.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug monument", "Preview monument blocking for your stored target.", true));
                entries.Add(new AdminCommandHelpEntry(scope, "debug", "Debug", open + " debug warnings <strikeId>", "Preview warning fanout recipients for a strike and target.", true));
                return entries;
            }

            entries.Add(new AdminCommandHelpEntry(scope, "distribution", "Distribution", "portableairstrikes.giveitem <playerNameOrSteamId> [amount]", "Give charge-backed targeting binoculars from server console or an admin client.", true));

            entries.Add(new AdminCommandHelpEntry(scope, "selectionui", "Selection UI", "portableairstrikes.ui.close", "Internal CUI command used by selection/confirm close buttons."));
            entries.Add(new AdminCommandHelpEntry(scope, "selectionui", "Selection UI", "portableairstrikes.ui.select <strikeId>", "Internal CUI command used when a player selects a strike."));
            entries.Add(new AdminCommandHelpEntry(scope, "selectionui", "Selection UI", "portableairstrikes.ui.confirm <strikeId>", "Internal CUI command used by the confirmation modal."));

            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui", "Open or refresh the admin panel for the calling admin.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui tab <tab>", "Switch admin pages: dashboard, give, strikes, balance, safety, visuals, lootaudit, activity, commands, help.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui tip <key>", "Show a page or field explanation in the admin status line.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui select <strikeId>", "Select the strike wrapper shown in Strikes, Balance, and Strike Profiles.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui addstrike", "Create a disabled strike wrapper with safe defaults.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui deletestrike <strikeId>", "Ask for delete confirmation for a wrapper.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui confirmdelete <strikeId>", "Delete the wrapper after confirmation; strike profiles are not deleted.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui togglestrike <strikeId>", "Enable or disable a strike wrapper.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui targettype <strikeId> <type>", "Toggle accepted target types: ground_ping, vehicle_ping, player_ping, npc_ping.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui toggle <field>", "Toggle a safety, currency, item, visual, loot, or audit setting.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui profiletoggle <strikeId> <profileId>", "Include or remove a loaded strike profile from a wrapper.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui give <playerId|search>", "Grant the current Give Items amount to a selected player or search result.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui openeditor <strikeId>", "Open the selected wrapper's assigned animation profile when the editor is loaded.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui createprofile <strikeId>", "Create/open a compatible animation profile in the editor.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui reloadprofiles", "Reload VisualProfiles.json and refresh the animation editor if loaded.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui reload", "Reload config, visual profiles, permissions, currency, and executors.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui givepage|givesort|givefilter <value>", "Change Give Items paging, sort, or online/sleeping filter.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui numberedit|numberkey|numberapply|numbercancel", "Drive the exact-value numeric keypad.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminui", "Admin UI", "portableairstrikes.adminui cmdscope|cmdcat <value>", "Switch this Commands page between scopes and categories.", true));

            entries.Add(new AdminCommandHelpEntry(scope, "adminfields", "Admin Fields", "portableairstrikes.adminfield give_search <text>", "Update the Give Items player search text.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminfields", "Admin Fields", "portableairstrikes.adminfield give_amount <amount>", "Update the Give Items grant amount.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminfields", "Admin Fields", "portableairstrikes.adminfield strike.<field> <strikeId> <value>", "Edit strike wrapper fields such as display, permission, RP, tier, cooldowns, warnings, and multipliers.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminfields", "Admin Fields", "portableairstrikes.adminfield strikeprofile.<field> <strikeId>|<profileId> <value>", "Edit included profile limit or start delay for one wrapper/profile pair.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminfields", "Admin Fields", "portableairstrikes.adminfield general.<field> <value>", "Edit safety numeric fields such as range, radius, active limits, warning delay, history, and monument radii.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminfields", "Admin Fields", "portableairstrikes.adminfield loot.<container>.chance|min|max <value>", "Edit loot injection chance and min/max charges for a container rule.", true));
            entries.Add(new AdminCommandHelpEntry(scope, "adminfields", "Admin Fields", "portableairstrikes.adminfield audit.url|username|mention|avatar <value>", "Edit Discord audit webhook text fields.", true));
            return entries;
        }

        private List<AdminCommandHelpEntry> GetAdminCommandCategories(List<AdminCommandHelpEntry> entries)
        {
            var categories = new List<AdminCommandHelpEntry>();
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Category))
                {
                    continue;
                }

                if (AdminCommandCategoryExists(categories, entry.Category))
                {
                    continue;
                }

                categories.Add(new AdminCommandHelpEntry(entry.Scope, entry.Category, entry.CategoryLabel, "", ""));
            }

            return categories;
        }

        private bool AdminCommandCategoryExists(List<AdminCommandHelpEntry> categories, string category)
        {
            category = NormalizeAdminCommandCategory(category);
            foreach (var entry in categories)
            {
                if (entry != null && string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddAdminCommandHelpRow(CuiElementContainer container, string parent, AdminCommandHelpEntry entry, float topOffset, float bottomOffset)
        {
            var row = container.Add(new CuiPanel
            {
                Image = { Color = entry.AdminOnly ? "0.080 0.070 0.060 0.94" : "0.065 0.074 0.088 0.94" },
                RectTransform =
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = "0 -" + FormatUiPixels(bottomOffset),
                    OffsetMax = "0 -" + FormatUiPixels(topOffset)
                }
            }, parent);

            AddUiLabel(container, row, ShortenAdminText(entry.Command, 92), 10, TextAnchor.MiddleLeft, "0.025 0.50", "0.77 0.90", "1 0.86 0.58 1");
            AddUiLabel(container, row, ShortenAdminText(entry.Detail, 140), 8, TextAnchor.MiddleLeft, "0.025 0.10", "0.88 0.44", "0.66 0.74 0.80 1");
            AddUiLabel(container, row, entry.AdminOnly ? "ADMIN" : (entry.Scope == "console" ? "CONSOLE" : "PLAYER"), 8, TextAnchor.MiddleCenter, "0.83 0.24", "0.97 0.76", entry.AdminOnly ? "1 0.66 0.46 1" : "0.62 0.90 0.66 1");
        }

        private void AddAdminHelpRow(CuiElementContainer container, string parent, string title, string text, float topOffset, float bottomOffset)
        {
            var row = container.Add(new CuiPanel
            {
                Image = { Color = "0.065 0.074 0.088 0.94" },
                RectTransform =
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = "0 -" + FormatUiPixels(bottomOffset),
                    OffsetMax = "0 -" + FormatUiPixels(topOffset)
                }
            }, parent);

            AddUiLabel(container, row, title, 10, TextAnchor.MiddleLeft, "0.025 0.52", "0.35 0.90", "1 0.86 0.58 1");
            AddUiLabel(container, row, ShortenAdminText(text, 150), 8, TextAnchor.MiddleLeft, "0.025 0.10", "0.96 0.48", "0.66 0.74 0.80 1");
        }

        private string ApplyAdminField(BasePlayer player, string field, string id, string value)
        {
            if (field.StartsWith("strike.", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyAdminStrikeField(player, field.Substring("strike.".Length), id, value);
            }

            if (field.StartsWith("strikeprofile.", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyAdminStrikeProfileField(player, field.Substring("strikeprofile.".Length), id, value);
            }

            if (field.StartsWith("general.", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyAdminGeneralField(player, field.Substring("general.".Length), value);
            }

            if (field.StartsWith("visual.", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyAdminVisualField(player, field.Substring("visual.".Length), value);
            }

            if (field.StartsWith("loot.", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyAdminLootField(player, field.Substring("loot.".Length), value);
            }

            if (field.StartsWith("audit.", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyAdminAuditField(player, field.Substring("audit.".Length), value);
            }

            if (field == "currency.provider")
            {
                config.Currency.Provider = NormalizeCurrencyProvider(value);
                CommitAdminConfigChange(player, false);
                return "Currency provider set to " + config.Currency.Provider + ".";
            }

            return "Unknown admin field '" + field + "'.";
        }

        private string ApplyAdminStrikeField(BasePlayer player, string field, string id, string value)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(id, out strike))
            {
                return "Unknown strike '" + id + "'.";
            }

            field = (field ?? "").Trim().ToLowerInvariant();
            switch (field)
            {
                case "display":
                    strike.DisplayName = CleanAdminString(value, 64);
                    break;
                case "permission":
                    strike.PermissionRequired = CleanAdminString(value, 96);
                    break;
                case "target":
                    var target = NormalizeTargetTypeName(value);
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        return "Invalid target type.";
                    }
                    strike.TargetType = target;
                    break;
                case "payload":
                    if (!SetStrikePayloadSafely(strike, value, out var payloadError))
                    {
                        return payloadError;
                    }
                    break;
                case "delivery":
                    if (!SetStrikeDeliverySafely(strike, value, out var deliveryError))
                    {
                        return deliveryError;
                    }
                    break;
                case "profile":
                    return AssignAdminVisualProfile(player, strike.Id, value);
                case "rpcost":
                    strike.RPCost = ParseAdminInt(value, strike.RPCost, 0, 1000000);
                    break;
                case "tier":
                    strike.Tier = ParseAdminInt(value, strike.Tier, 1, 5);
                    break;
                case "warning":
                    strike.WarningDelaySeconds = ParseAdminFloat(value, strike.WarningDelaySeconds, 0f, 120f);
                    break;
                case "playercd":
                    strike.CooldownPerPlayerSeconds = ParseAdminFloat(value, strike.CooldownPerPlayerSeconds, 0f, 86400f);
                    break;
                case "clancd":
                    strike.CooldownPerClanSeconds = ParseAdminFloat(value, strike.CooldownPerClanSeconds, 0f, 86400f);
                    break;
                case "globalcd":
                    strike.GlobalCooldownSeconds = ParseAdminFloat(value, strike.GlobalCooldownSeconds, 0f, 86400f);
                    break;
                case "base":
                    strike.BaseCount = ParseAdminInt(value, strike.BaseCount, 1, 1000);
                    break;
                case "max":
                    strike.MaxCount = ParseAdminInt(value, strike.MaxCount, 1, 1000);
                    break;
                case "spread":
                    strike.SpreadRadius = ParseAdminFloat(value, strike.SpreadRadius, 0f, 250f);
                    break;
                case "spreadmult":
                    strike.SpreadMultiplier = ParseAdminFloat(value, strike.SpreadMultiplier, 0.01f, 100f);
                    break;
                case "rockets":
                    strike.RocketCount = ParseAdminInt(value, strike.RocketCount, 0, 48);
                    break;
                case "missiles":
                    strike.MissileCount = ParseAdminInt(value, strike.MissileCount, 0, 12);
                    break;
                case "burst":
                    strike.BurstCount = ParseAdminInt(value, strike.BurstCount, 0, 80);
                    break;
                case "line":
                    strike.LineLength = ParseAdminFloat(value, strike.LineLength, 0f, 200f);
                    break;
                case "linemult":
                    strike.LineLengthMultiplier = ParseAdminFloat(value, strike.LineLengthMultiplier, 0.01f, 100f);
                    break;
                case "width":
                    strike.Width = ParseAdminFloat(value, strike.Width, 0f, 50f);
                    break;
                case "widthmult":
                    strike.WidthMultiplier = ParseAdminFloat(value, strike.WidthMultiplier, 0.01f, 100f);
                    break;
                case "impact":
                    strike.ImpactRadius = ParseAdminFloat(value, strike.ImpactRadius, 0f, 25f);
                    break;
                case "impactmult":
                    strike.ImpactRadiusMultiplier = ParseAdminFloat(value, strike.ImpactRadiusMultiplier, 0.01f, 100f);
                    break;
                case "pulse":
                    strike.PulseDelaySeconds = ParseAdminFloat(value, strike.PulseDelaySeconds, 0f, 2f);
                    break;
                case "pulsemult":
                    strike.PulseDelayMultiplier = ParseAdminFloat(value, strike.PulseDelayMultiplier, 0.01f, 100f);
                    break;
                case "tracktime":
                    strike.MaxTrackingSeconds = ParseAdminFloat(value, strike.MaxTrackingSeconds, 0f, 60f);
                    break;
                case "tracktimemult":
                    strike.TrackingSecondsMultiplier = ParseAdminFloat(value, strike.TrackingSecondsMultiplier, 0.01f, 100f);
                    break;
                case "trackdistance":
                    strike.MaxTrackingDistance = ParseAdminFloat(value, strike.MaxTrackingDistance, 0f, 1000f);
                    break;
                case "trackdistancemult":
                    strike.TrackingDistanceMultiplier = ParseAdminFloat(value, strike.TrackingDistanceMultiplier, 0.01f, 100f);
                    break;
                case "vehiclescale":
                    strike.VehicleDamageScale = ParseAdminFloat(value, strike.VehicleDamageScale, 0f, 10f);
                    break;
                case "damagemult":
                    strike.DamageMultiplier = ParseAdminFloat(value, strike.DamageMultiplier, 0.01f, 100f);
                    break;
                case "vehicledamagemult":
                    strike.VehicleDamageMultiplier = ParseAdminFloat(value, strike.VehicleDamageMultiplier, 0.01f, 100f);
                    break;
                case "splash":
                    strike.SplashRadius = ParseAdminFloat(value, strike.SplashRadius, 0f, 50f);
                    break;
                case "splashmult":
                    strike.SplashRadiusMultiplier = ParseAdminFloat(value, strike.SplashRadiusMultiplier, 0.01f, 100f);
                    break;
                case "d_players":
                    SetStrikeDamageScale(strike, "Players", value);
                    break;
                case "d_buildings":
                    SetStrikeDamageScale(strike, "Buildings", value);
                    break;
                case "d_vehicles":
                    SetStrikeDamageScale(strike, "Vehicles", value);
                    break;
                case "d_deployables":
                    SetStrikeDamageScale(strike, "Deployables", value);
                    break;
                case "d_turrets":
                    SetStrikeDamageScale(strike, "Turrets", value);
                    break;
                default:
                    return "Unknown strike field '" + field + "'.";
            }

            CommitAdminConfigChange(player, true);
            return "Updated " + strike.DisplayName + ".";
        }

        private string ApplyAdminStrikeProfileField(BasePlayer player, string field, string id, string value)
        {
            var parts = (id ?? "").Split('|');
            if (parts.Length != 2)
            {
                return "Invalid strike profile field target.";
            }

            StrikeDefinition strike;
            if (!TryGetStrike(parts[0], out strike))
            {
                return "Unknown strike '" + parts[0] + "'.";
            }

            StrikeProfileAssignment assignment;
            if (!TryGetStrikeProfileAssignment(strike, parts[1], out assignment))
            {
                return "Strike profile '" + parts[1] + "' is not included in " + strike.DisplayName + ".";
            }

            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "delay":
                    assignment.StartDelaySeconds = ParseAdminFloat(value, assignment.StartDelaySeconds, 0f, 120f);
                    break;
                case "limit":
                    assignment.PayloadCountLimit = ParseAdminInt(value, assignment.PayloadCountLimit, 0, GetProfilePayloadLimitCap(assignment.ProfileId, strike));
                    break;
                default:
                    return "Unknown strike profile field '" + field + "'.";
            }

            CommitAdminConfigChange(player, true);
            return "Updated " + assignment.ProfileId + " for " + strike.DisplayName + ".";
        }

        private int GetProfilePayloadLimitCap(string profileId, StrikeDefinition strike)
        {
            VisualProfileConfig profile;
            if (!TryGetVisualProfileById(profileId, out profile))
            {
                return 200;
            }

            string payload;
            TryGetProfilePrimaryPayload(profile, strike == null ? "" : strike.Payload, out payload);
            return Math.Max(1, Math.Min(200, GetProfileEffectivePayloadUnitCount(profile, payload, GetFallbackPayloadCount(strike, payload))));
        }

        private string ApplyAdminGeneralField(BasePlayer player, string field, string value)
        {
            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "maxrange":
                    config.General.MaxCallRange = ParseAdminFloat(value, config.General.MaxCallRange, 25f, 2000f);
                    break;
                case "mindistance":
                    config.General.MinimumDistanceFromCaller = ParseAdminFloat(value, config.General.MinimumDistanceFromCaller, 0f, 2000f);
                    break;
                case "safezoneradius":
                    config.General.SafeZoneBlockRadius = ParseAdminFloat(value, config.General.SafeZoneBlockRadius, 0f, 1000f);
                    break;
                case "warningdelay":
                    config.General.DefaultWarningDelaySeconds = ParseAdminFloat(value, config.General.DefaultWarningDelaySeconds, 0f, 120f);
                    break;
                case "maxsim":
                    config.General.MaxSimultaneousStrikes = ParseAdminInt(value, config.General.MaxSimultaneousStrikes, 1, 100);
                    break;
                case "maxheavy":
                    config.General.MaxSimultaneousHeavyStrikes = ParseAdminInt(value, config.General.MaxSimultaneousHeavyStrikes, 1, 100);
                    break;
                case "nearbyradius":
                    config.General.NearbyHeavyStrikeWarningRadius = ParseAdminFloat(value, config.General.NearbyHeavyStrikeWarningRadius, 0f, 1000f);
                    break;
                case "history":
                    config.General.RecentCallHistoryLimit = ParseAdminInt(value, config.General.RecentCallHistoryLimit, 0, 200);
                    break;
                case "monumentpadding":
                    config.General.MonumentBlockRadiusPadding = ParseAdminFloat(value, config.General.MonumentBlockRadiusPadding, 0f, 500f);
                    ResetMonumentBlockZones();
                    break;
                case "monumentdefault":
                    config.General.DefaultMonumentBlockRadius = ParseAdminFloat(value, config.General.DefaultMonumentBlockRadius, 1f, 1000f);
                    ResetMonumentBlockZones();
                    break;
                default:
                    return "Unknown general field '" + field + "'.";
            }

            CommitAdminConfigChange(player, false);
            return "Updated safety settings.";
        }

        private string ApplyAdminVisualField(BasePlayer player, string field, string value)
        {
            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "dronedistance":
                    config.DeliveryVisuals.DroneFlyoverDistance = ParseAdminFloat(value, config.DeliveryVisuals.DroneFlyoverDistance, 15f, 150f);
                    break;
                case "droneheight":
                    config.DeliveryVisuals.DroneFlyoverHeight = ParseAdminFloat(value, config.DeliveryVisuals.DroneFlyoverHeight, 8f, 80f);
                    break;
                case "airdistance":
                    config.DeliveryVisuals.AircraftFlyoverDistance = ParseAdminFloat(value, config.DeliveryVisuals.AircraftFlyoverDistance, 60f, 500f);
                    break;
                case "moverate":
                    config.DeliveryVisuals.VisualMoveIntervalSeconds = ParseAdminFloat(value, config.DeliveryVisuals.VisualMoveIntervalSeconds, MinimumVisualMoveIntervalSeconds, MaximumVisualMoveIntervalSeconds);
                    break;
                case "heliheight":
                    config.DeliveryVisuals.AttackHeliFlyoverHeight = ParseAdminFloat(value, config.DeliveryVisuals.AttackHeliFlyoverHeight, 20f, 180f);
                    break;
                case "cargoheight":
                    config.DeliveryVisuals.CargoPlaneFlyoverHeight = ParseAdminFloat(value, config.DeliveryVisuals.CargoPlaneFlyoverHeight, 35f, 260f);
                    break;
                case "a10height":
                    config.DeliveryVisuals.A10FlyoverHeight = ParseAdminFloat(value, config.DeliveryVisuals.A10FlyoverHeight, 25f, 220f);
                    break;
                case "mlrsheight":
                    config.DeliveryVisuals.MlrsAircraftFlyoverHeight = ParseAdminFloat(value, config.DeliveryVisuals.MlrsAircraftFlyoverHeight, 35f, 200f);
                    break;
                case "dronedelay":
                    config.DeliveryVisuals.DroneFirstPayloadDelaySeconds = ParseAdminFloat(value, config.DeliveryVisuals.DroneFirstPayloadDelaySeconds, 0f, 20f);
                    break;
                case "helidelay":
                    config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds = ParseAdminFloat(value, config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds, 0f, 20f);
                    break;
                case "cargodelay":
                    config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds = ParseAdminFloat(value, config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds, 0f, 20f);
                    break;
                case "a10delay":
                    config.DeliveryVisuals.A10FirstPayloadDelaySeconds = ParseAdminFloat(value, config.DeliveryVisuals.A10FirstPayloadDelaySeconds, 0f, 20f);
                    break;
                case "mlrsdelay":
                    config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds = ParseAdminFloat(value, config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds, 0f, 20f);
                    break;
                case "soundgap":
                    config.DeliveryVisuals.FlyoverSoundIntervalSeconds = ParseAdminFloat(value, config.DeliveryVisuals.FlyoverSoundIntervalSeconds, 0.25f, 3f);
                    break;
                default:
                    return "Unknown visual field '" + field + "'.";
            }

            CommitAdminConfigChange(player, false);
            return "Updated visual settings.";
        }

        private string ApplyAdminLootField(BasePlayer player, string field, string value)
        {
            var parts = (field ?? "").Split('.');
            if (parts.Length == 2)
            {
                var rule = GetOrCreateLootRule(parts[0]);
                switch (parts[1])
                {
                    case "chance":
                        rule.Chance = ParseAdminFloat(value, rule.Chance, 0f, 1f);
                        break;
                    case "min":
                        rule.MinAmount = ParseAdminInt(value, rule.MinAmount, 1, 100);
                        break;
                    case "max":
                        rule.MaxAmount = ParseAdminInt(value, rule.MaxAmount, 1, 100);
                        break;
                    default:
                        return "Unknown loot field.";
                }

                CommitAdminConfigChange(player, false);
                return "Updated loot rule " + parts[0] + ".";
            }

            return "Unknown loot field '" + field + "'.";
        }

        private string ApplyAdminAuditField(BasePlayer player, string field, string value)
        {
            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "url":
                    config.AuditWebhooks.DiscordWebhookUrl = CleanAdminString(value, 512);
                    break;
                case "username":
                    config.AuditWebhooks.Username = CleanAdminString(value, 64);
                    break;
                case "mention":
                    config.AuditWebhooks.MentionText = CleanAdminString(value, 128);
                    break;
                case "avatar":
                    config.AuditWebhooks.AvatarUrl = CleanAdminString(value, 512);
                    break;
                default:
                    return "Unknown audit field '" + field + "'.";
            }

            CommitAdminConfigChange(player, false);
            return "Updated audit settings.";
        }

        private void CommitAdminConfigChange(BasePlayer player, bool registerPermissions)
        {
            NormalizeConfig();
            if (registerPermissions)
            {
                RegisterPermissions();
            }

            RefreshCurrencyAdapter();
            InitializeExecutors();
            SaveConfig();
        }

        private void ToggleAdminStrikeEnabled(BasePlayer player, string strikeId)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                ShowAdminStatus(player, "Unknown strike '" + strikeId + "'.");
                return;
            }

            if (!strike.Enabled && GetEnabledStrikeProfileAssignments(strike).Count == 0)
            {
                ShowAdminStatus(player, "Include at least one strike profile before enabling " + strike.DisplayName + ".");
                return;
            }

            strike.Enabled = !strike.Enabled;
            CommitAdminConfigChange(player, true);
            ShowAdminStatus(player, (strike.Enabled ? "Enabled " : "Disabled ") + strike.DisplayName + ".");
        }

        private void AddAdminStrike(BasePlayer player)
        {
            if (config.StrikeDefinitions == null)
            {
                config.StrikeDefinitions = new Dictionary<string, StrikeDefinition>(StringComparer.OrdinalIgnoreCase);
            }

            var id = GenerateAdminStrikeId();
            var strike = new StrikeDefinition
            {
                Id = id,
                Enabled = false,
                DisplayName = "New Strike",
                TargetType = "ground_ping",
                AcceptedTargetTypes = new List<string> { "ground_ping" },
                Delivery = "drone",
                Payload = "beancan",
                VisualProfileId = "",
                StrikeProfiles = new List<StrikeProfileAssignment>(),
                Tier = 1,
                RPCost = 0,
                PermissionRequired = "",
                WarningDelaySeconds = config?.General == null ? 8f : config.General.DefaultWarningDelaySeconds,
                CooldownPerPlayerSeconds = 0f,
                CooldownPerClanSeconds = 0f,
                GlobalCooldownSeconds = 0f
            };

            config.StrikeDefinitions[id] = strike;
            var state = GetAdminUiState(player);
            state.SelectedStrikeId = id;
            state.Tab = "strikes";
            state.DeleteConfirmStrikeId = "";
            CommitAdminConfigChange(player, true);
            ShowAdminStatus(player, "Added disabled strike wrapper " + id + ". Include a strike profile before enabling it.");
        }

        private string GenerateAdminStrikeId()
        {
            var index = 1;
            string id;
            do
            {
                id = "custom_strike_" + index.ToString(CultureInfo.InvariantCulture);
                index++;
            }
            while (config?.StrikeDefinitions != null && config.StrikeDefinitions.ContainsKey(id));

            return id;
        }

        private void DeleteAdminStrike(BasePlayer player, string strikeId, bool confirmed)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                ShowAdminStatus(player, "Unknown strike '" + strikeId + "'.");
                return;
            }

            var state = GetAdminUiState(player);
            if (!confirmed || !string.Equals(state.DeleteConfirmStrikeId, strike.Id, StringComparison.OrdinalIgnoreCase))
            {
                state.DeleteConfirmStrikeId = strike.Id;
                ShowAdminStatus(player, "Press Delete again to remove " + strike.DisplayName + ". Strike profiles will not be deleted.");
                return;
            }

            config.StrikeDefinitions.Remove(strike.Id);
            ClearSavedStrikeDefaults(strike.Id);
            state.DeleteConfirmStrikeId = "";
            state.SelectedStrikeId = GetSortedStrikeIds().Count == 0 ? "" : GetSortedStrikeIds()[0];
            CommitAdminConfigChange(player, true);
            SaveData();
            ShowAdminStatus(player, "Deleted strike wrapper " + strike.DisplayName + ". Strike profile data was left untouched.");
        }

        private void ClearSavedStrikeDefaults(string strikeId)
        {
            if (storedData == null || string.IsNullOrWhiteSpace(strikeId))
            {
                return;
            }

            RemoveSavedStrikeReferences(storedData.LastStrikeByUser, strikeId);
            RemoveSavedStrikeReferences(storedData.DefaultStrikeByUser, strikeId);
        }

        private void RemoveSavedStrikeReferences(Dictionary<string, string> values, string strikeId)
        {
            if (values == null)
            {
                return;
            }

            var remove = new List<string>();
            foreach (var entry in values)
            {
                if (string.Equals(entry.Value, strikeId, StringComparison.OrdinalIgnoreCase))
                {
                    remove.Add(entry.Key);
                }
            }

            foreach (var key in remove)
            {
                values.Remove(key);
            }
        }

        private void ToggleAdminAcceptedTargetType(BasePlayer player, string strikeId, string targetType)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                ShowAdminStatus(player, "Unknown strike '" + strikeId + "'.");
                return;
            }

            var parsed = ParseTargetType(targetType);
            if (parsed == AirstrikeTargetType.Invalid)
            {
                ShowAdminStatus(player, "Invalid target type.");
                return;
            }

            var normalizedTarget = NormalizeTargetTypeName(targetType);
            if (strike.AcceptedTargetTypes == null)
            {
                strike.AcceptedTargetTypes = new List<string>();
            }

            var removed = false;
            for (var i = strike.AcceptedTargetTypes.Count - 1; i >= 0; i--)
            {
                if (ParseTargetType(strike.AcceptedTargetTypes[i]) == parsed)
                {
                    strike.AcceptedTargetTypes.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
            {
                strike.AcceptedTargetTypes.Add(normalizedTarget);
            }

            if (strike.AcceptedTargetTypes.Count == 0)
            {
                strike.AcceptedTargetTypes.Add(normalizedTarget);
                removed = false;
            }

            strike.TargetType = strike.AcceptedTargetTypes[0];
            CommitAdminConfigChange(player, true);
            ShowAdminStatus(player, (removed ? "Removed " : "Added ") + FormatTargetType(parsed) + " for " + strike.DisplayName + ".");
        }

        private void ToggleAdminStrikeProfile(BasePlayer player, string strikeId, string profileId)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                ShowAdminStatus(player, "Unknown strike '" + strikeId + "'.");
                return;
            }

            profileId = (profileId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(profileId))
            {
                ShowAdminStatus(player, "Profile id is empty.");
                return;
            }

            VisualProfileConfig profile;
            if (!TryGetVisualProfileById(profileId, out profile))
            {
                ShowAdminStatus(player, "Strike profile '" + profileId + "' is not loaded.");
                return;
            }

            if (ProfileContainsHomingPayload(profile, strike.Payload) && !StrikeAcceptsTargetType(strike, AirstrikeTargetType.VehiclePing))
            {
                ShowAdminStatus(player, "Add vehicle ping to " + strike.DisplayName + " before including homing strike profile " + profileId + ".");
                return;
            }

            if (strike.StrikeProfiles == null)
            {
                strike.StrikeProfiles = new List<StrikeProfileAssignment>();
            }

            StrikeProfileAssignment assignment;
            if (TryGetStrikeProfileAssignment(strike, profileId, out assignment))
            {
                strike.StrikeProfiles.Remove(assignment);
                if (string.Equals(strike.VisualProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    strike.VisualProfileId = "";
                }

                CommitAdminConfigChange(player, true);
                ShowAdminStatus(player, "Removed strike profile " + profileId + " from " + strike.DisplayName + ".");
                return;
            }

            strike.StrikeProfiles.Add(new StrikeProfileAssignment
            {
                ProfileId = profileId,
                Enabled = true,
                StartDelaySeconds = 0f,
                PayloadCountLimit = 0
            });

            if (string.IsNullOrWhiteSpace(strike.VisualProfileId))
            {
                strike.VisualProfileId = profileId;
            }

            CommitAdminConfigChange(player, true);
            ShowAdminStatus(player, "Included strike profile " + profileId + " in " + strike.DisplayName + ".");
        }

        private bool TryGetStrikeProfileAssignment(StrikeDefinition strike, string profileId, out StrikeProfileAssignment assignment)
        {
            assignment = null;
            if (strike?.StrikeProfiles == null || string.IsNullOrWhiteSpace(profileId))
            {
                return false;
            }

            foreach (var candidate in strike.StrikeProfiles)
            {
                if (candidate != null && string.Equals(candidate.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    assignment = candidate;
                    return true;
                }
            }

            return false;
        }

        private void ToggleAdminConfigField(BasePlayer player, string field)
        {
            field = (field ?? "").Trim().ToLowerInvariant();
            switch (field)
            {
                case "general.requireping":
                    config.General.RequireBinocularPing = !config.General.RequireBinocularPing;
                    break;
                case "general.los":
                    config.General.RequireLineOfSightToPing = !config.General.RequireLineOfSightToPing;
                    break;
                case "general.safezones":
                    config.General.BlockSafeZones = !config.General.BlockSafeZones;
                    break;
                case "general.monuments":
                    config.General.BlockMonuments = !config.General.BlockMonuments;
                    ResetMonumentBlockZones();
                    break;
                case "general.monumentsheavy":
                    config.General.BlockMonumentsForHeavyStrikesOnly = !config.General.BlockMonumentsForHeavyStrikesOnly;
                    break;
                case "general.clancd":
                    config.General.EnableClanCooldowns = !config.General.EnableClanCooldowns;
                    break;
                case "general.globalcd":
                    config.General.EnableGlobalCooldowns = !config.General.EnableGlobalCooldowns;
                    break;
                case "general.teamwarn":
                    config.General.NotifyCallerTeamOnAcceptedStrike = !config.General.NotifyCallerTeamOnAcceptedStrike;
                    break;
                case "general.nearbywarn":
                    config.General.NotifyNearbyPlayersOnHeavyStrikes = !config.General.NotifyNearbyPlayersOnHeavyStrikes;
                    break;
                case "general.cancel":
                    config.General.AllowPlayerCancelBeforeImpact = !config.General.AllowPlayerCancelBeforeImpact;
                    break;
                case "general.refundcancel":
                    config.General.RefundPlayerCancelledCallsBeforeImpact = !config.General.RefundPlayerCancelledCallsBeforeImpact;
                    break;
                case "general.debug":
                    config.General.DebugMode = !config.General.DebugMode;
                    break;
                case "currency.enabled":
                    config.Currency.Enabled = !config.Currency.Enabled;
                    break;
                case "currency.freeadmin":
                    config.Currency.AllowFreeAdminCalls = !config.Currency.AllowFreeAdminCalls;
                    break;
                case "currency.provider":
                    config.Currency.Provider = string.Equals(config.Currency.Provider, "Economics", StringComparison.OrdinalIgnoreCase) ? "ServerRewards" : "Economics";
                    break;
                case "item.consume":
                    config.AirstrikeItem.ConsumeOnSuccessfulCall = !config.AirstrikeItem.ConsumeOnSuccessfulCall;
                    break;
                case "item.adminbypass":
                    config.AirstrikeItem.AllowAdminsWithoutItem = !config.AirstrikeItem.AllowAdminsWithoutItem;
                    break;
                case "visual.enabled":
                    config.DeliveryVisuals.Enabled = !config.DeliveryVisuals.Enabled;
                    break;
                case "visual.drones":
                    config.DeliveryVisuals.SpawnDroneVisuals = !config.DeliveryVisuals.SpawnDroneVisuals;
                    break;
                case "visual.aircraft":
                    config.DeliveryVisuals.SpawnAircraftVisuals = !config.DeliveryVisuals.SpawnAircraftVisuals;
                    break;
                case "visual.mortars":
                    config.DeliveryVisuals.SpawnMortarArtilleryVisuals = !config.DeliveryVisuals.SpawnMortarArtilleryVisuals;
                    break;
                case "visual.crew":
                    config.DeliveryVisuals.SpawnMortarCrewNpc = !config.DeliveryVisuals.SpawnMortarCrewNpc;
                    break;
                case "visual.sounds":
                    config.DeliveryVisuals.SpawnFlyoverSoundEffects = !config.DeliveryVisuals.SpawnFlyoverSoundEffects;
                    break;
                case "visual.rotor":
                    config.DeliveryVisuals.SpawnRotorWashEffects = !config.DeliveryVisuals.SpawnRotorWashEffects;
                    break;
                case "visual.destroyable":
                    config.DeliveryVisuals.DeliveryVehiclesCanBeDestroyed = !config.DeliveryVisuals.DeliveryVehiclesCanBeDestroyed;
                    break;
                case "visual.requirecarrier":
                    config.DeliveryVisuals.PayloadRequiresLiveDeliveryVehicle = !config.DeliveryVisuals.PayloadRequiresLiveDeliveryVehicle;
                    break;
                case "visual.refundcarrier":
                    config.DeliveryVisuals.RefundIfDeliveryVehicleDestroyedBeforePayload = !config.DeliveryVisuals.RefundIfDeliveryVehicleDestroyedBeforePayload;
                    break;
                case "loot.enabled":
                    config.LootDistribution.Enabled = !config.LootDistribution.Enabled;
                    break;
                case "audit.enabled":
                    config.AuditWebhooks.Enabled = !config.AuditWebhooks.Enabled;
                    break;
                case "audit.started":
                    config.AuditWebhooks.SendStartedCalls = !config.AuditWebhooks.SendStartedCalls;
                    break;
                case "audit.completed":
                    config.AuditWebhooks.SendCompletedCalls = !config.AuditWebhooks.SendCompletedCalls;
                    break;
                case "audit.failures":
                    config.AuditWebhooks.SendFailuresAndRefunds = !config.AuditWebhooks.SendFailuresAndRefunds;
                    break;
                case "audit.cancels":
                    config.AuditWebhooks.SendPlayerCancels = !config.AuditWebhooks.SendPlayerCancels;
                    break;
                case "audit.validation":
                    config.AuditWebhooks.SendValidationFailures = !config.AuditWebhooks.SendValidationFailures;
                    break;
                default:
                    ShowAdminStatus(player, "Unknown toggle '" + field + "'.");
                    return;
            }

            CommitAdminConfigChange(player, false);
            ShowAdminStatus(player, "Toggled " + field + ".");
        }

        private void CycleAdminStrikeField(BasePlayer player, string field, string strikeId)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                ShowAdminStatus(player, "Unknown strike '" + strikeId + "'.");
                return;
            }

            field = (field ?? "").Trim().ToLowerInvariant();
            if (field == "target")
            {
                strike.TargetType = GetNextTargetTypeName(strike.TargetType);
            }
            else if (field == "payload")
            {
                CycleStrikePayload(strike);
            }
            else if (field == "delivery")
            {
                CycleStrikeDelivery(strike);
            }
            else
            {
                ShowAdminStatus(player, "Unknown cycle field '" + field + "'.");
                return;
            }

            CommitAdminConfigChange(player, true);
            ShowAdminStatus(player, "Updated " + strike.DisplayName + " " + field + ".");
        }

        private void AdminGiveAirstrikeItem(BasePlayer player, string targetToken)
        {
            var state = GetAdminUiState(player);
            BasePlayer target = null;
            if (string.Equals(targetToken, "search", StringComparison.OrdinalIgnoreCase))
            {
                target = FindPlayer(state.GiveSearch);
            }
            else
            {
                target = FindPlayer(targetToken);
            }

            if (target == null)
            {
                ShowAdminStatus(player, "Player not found for item grant.");
                return;
            }

            var amount = Mathf.Clamp(state.GiveAmount <= 0 ? 1 : state.GiveAmount, 1, GetAirstrikeMaxChargesPerItem());
            var result = GiveAirstrikeTokensDetailed(target, amount);
            var dropped = result.Dropped > 0 ? "; " + result.Dropped + " item(s) dropped at their feet" : "";
            var failure = string.IsNullOrWhiteSpace(result.Failure) ? "" : "; last failure: " + result.Failure;
            ShowAdminStatus(player, "Gave " + result.Given + " " + GetAirstrikeItemDisplayName() + " charge(s) to " + target.displayName + dropped + failure + ".");
        }

        private string AssignAdminVisualProfile(BasePlayer player, string strikeId, string profileId)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                return "Unknown strike '" + strikeId + "'.";
            }

            if (!IsAnimationEditorLoaded())
            {
                return "Animation editor is not loaded; profile assignment is read-only.";
            }

            profileId = (profileId ?? "").Trim();
            if (string.Equals(profileId, "clear", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profileId, "auto", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(profileId))
            {
                strike.VisualProfileId = "";
                CommitAdminConfigChange(player, true);
                LoadVisualProfiles();
                return "Cleared explicit visual profile for " + strike.DisplayName + ".";
            }

            var vehicle = GetEffectiveVisualProfileVehicle(strike);
            if (string.IsNullOrWhiteSpace(vehicle))
            {
                return strike.DisplayName + " does not use waypoint visual profiles.";
            }

            if (!IsVisualProfileCompatible(profileId, vehicle, out var profileMessage))
            {
                return profileMessage;
            }

            strike.VisualProfileId = profileId;
            CommitAdminConfigChange(player, true);
            return "Assigned " + profileId + " to " + strike.DisplayName + ".";
        }

        private void CycleAdminVisualProfile(BasePlayer player, string strikeId)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                ShowAdminStatus(player, "Unknown strike '" + strikeId + "'.");
                return;
            }

            if (!IsAnimationEditorLoaded())
            {
                ShowAdminStatus(player, "Animation editor is not loaded; profile assignment is read-only.");
                return;
            }

            var vehicle = GetEffectiveVisualProfileVehicle(strike);
            var profiles = GetCompatibleVisualProfileIds(vehicle);
            if (profiles.Count == 0)
            {
                ShowAdminStatus(player, "No compatible visual profiles for vehicle " + vehicle + ".");
                return;
            }

            var currentIndex = -1;
            for (var i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i], strike.VisualProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }

            strike.VisualProfileId = profiles[(currentIndex + 1) % profiles.Count];
            CommitAdminConfigChange(player, true);
            ShowAdminStatus(player, "Assigned " + strike.VisualProfileId + " to " + strike.DisplayName + ".");
        }

        private void OpenAdminAnimationProfile(BasePlayer player, string strikeId, bool createIfMissing)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                ShowAdminStatus(player, "Unknown strike '" + strikeId + "'.");
                return;
            }

            if (!IsAnimationEditorLoaded())
            {
                ShowAdminStatus(player, "PortableAirstrikesAnimationEditor is not loaded.");
                return;
            }

            var vehicle = GetEffectiveVisualProfileVehicle(strike);
            if (string.IsNullOrWhiteSpace(vehicle))
            {
                ShowAdminStatus(player, strike.DisplayName + " does not use waypoint visual profiles.");
                return;
            }

            var profileId = string.IsNullOrWhiteSpace(strike.VisualProfileId) ? strike.Id : strike.VisualProfileId;
            object result;
            if (createIfMissing)
            {
                result = PortableAirstrikesAnimationEditor.Call("API_CreateOrOpenProfile", player.userID, profileId, vehicle);
            }
            else
            {
                result = PortableAirstrikesAnimationEditor.Call("API_OpenProfile", player.userID, profileId);
            }

            if (result is bool && (bool)result)
            {
                if (string.IsNullOrWhiteSpace(strike.VisualProfileId))
                {
                    strike.VisualProfileId = profileId;
                    CommitAdminConfigChange(player, true);
                }

                LoadVisualProfiles();
                ShowAdminStatus(player, "Opened animation profile " + profileId + ".");
            }
            else
            {
                ShowAdminStatus(player, "Could not open animation profile " + profileId + ".");
            }
        }

        private void ShowAdminStatus(BasePlayer player, string message)
        {
            if (player == null)
            {
                return;
            }

            GetAdminUiState(player).Status = message ?? "";
        }

        private bool SetStrikePayloadSafely(StrikeDefinition strike, string payload, out string error)
        {
            error = "";
            payload = NormalizeAdminToken(payload);
            if (string.IsNullOrWhiteSpace(payload))
            {
                error = "Payload cannot be empty.";
                return false;
            }

            var deliveries = GetSupportedDeliveriesForPayload(payload);
            if (deliveries.Count == 0)
            {
                error = "Payload '" + payload + "' is not supported by any live executor.";
                return false;
            }

            strike.Payload = payload;
            if (!ContainsString(deliveries, strike.Delivery))
            {
                strike.Delivery = deliveries[0];
            }

            return true;
        }

        private bool SetStrikeDeliverySafely(StrikeDefinition strike, string delivery, out string error)
        {
            error = "";
            delivery = NormalizeAdminToken(delivery);
            if (string.IsNullOrWhiteSpace(delivery))
            {
                error = "Delivery cannot be empty.";
                return false;
            }

            if (!IsDeliverySupportedForPayload(delivery, strike.Payload))
            {
                error = "Delivery '" + delivery + "' does not support payload '" + strike.Payload + "'.";
                return false;
            }

            strike.Delivery = delivery;
            return true;
        }

        private void CycleStrikePayload(StrikeDefinition strike)
        {
            var payloads = GetSupportedPayloads();
            if (payloads.Count == 0)
            {
                return;
            }

            var index = IndexOfString(payloads, strike.Payload);
            var next = payloads[(index + 1) % payloads.Count];
            string error;
            SetStrikePayloadSafely(strike, next, out error);
        }

        private void CycleStrikeDelivery(StrikeDefinition strike)
        {
            var deliveries = GetSupportedDeliveriesForPayload(strike.Payload);
            if (deliveries.Count == 0)
            {
                return;
            }

            var index = IndexOfString(deliveries, strike.Delivery);
            strike.Delivery = deliveries[(index + 1) % deliveries.Count];
        }

        private bool IsDeliverySupportedForPayload(string delivery, string payload)
        {
            if (string.IsNullOrWhiteSpace(delivery) || string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            return strikeExecutors.ContainsKey(NormalizeAdminToken(delivery) + ":" + NormalizeAdminToken(payload));
        }

        private List<string> GetSupportedDeliveriesForPayload(string payload)
        {
            payload = NormalizeAdminToken(payload);
            var deliveries = new List<string>();
            foreach (var key in strikeExecutors.Keys)
            {
                var parts = key.Split(':');
                if (parts.Length != 2 || !string.Equals(parts[1], payload, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ContainsString(deliveries, parts[0]))
                {
                    deliveries.Add(parts[0]);
                }
            }

            deliveries.Sort(StringComparer.OrdinalIgnoreCase);
            return deliveries;
        }

        private List<string> GetSupportedPayloads()
        {
            var payloads = new List<string>();
            foreach (var key in strikeExecutors.Keys)
            {
                var parts = key.Split(':');
                if (parts.Length == 2 && !ContainsString(payloads, parts[1]))
                {
                    payloads.Add(parts[1]);
                }
            }

            payloads.Sort(StringComparer.OrdinalIgnoreCase);
            return payloads;
        }

        private bool IsStrikeExecutorCompatible(StrikeDefinition strike)
        {
            EnsureExecutorCompatibilityRegistry();

            IStrikeExecutor executor;
            string message;
            return TryGetExecutor(strike, out executor, out message);
        }

        private string GetStrikeCompatibilityMessage(StrikeDefinition strike)
        {
            EnsureExecutorCompatibilityRegistry();

            IStrikeExecutor executor;
            string message;
            return TryGetExecutor(strike, out executor, out message) ? "Executor OK." : message;
        }

        private void EnsureExecutorCompatibilityRegistry()
        {
            if (strikeExecutors.Count == 0)
            {
                InitializeExecutors();
            }
        }

        private int CountUnsupportedStrikes()
        {
            var count = 0;
            if (config?.StrikeDefinitions == null)
            {
                return count;
            }

            foreach (var strike in config.StrikeDefinitions.Values)
            {
                if (strike != null && !IsStrikeExecutorCompatible(strike))
                {
                    count++;
                }
            }

            return count;
        }

        private string GetEffectiveVisualProfileVehicle(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return "";
            }

            DronePayloadSpec droneSpec;
            if (string.Equals(strike.Delivery, "drone", StringComparison.OrdinalIgnoreCase)
                && TryGetDronePayloadSpec(strike.Payload, out droneSpec))
            {
                return "drone";
            }

            MortarPayloadSpec mortarSpec;
            if (TryGetMortarPayloadSpec(strike.Payload, out mortarSpec)
                || string.Equals(strike.Delivery, "off_map_mortar", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            HeavyDropPayloadSpec heavySpec;
            if (TryGetHeavyDropPayloadSpec(strike.Payload, out heavySpec))
            {
                return "cargo_plane";
            }

            A10StrafeSpec a10Spec;
            if (TryGetA10StrafeSpec(strike.Payload, out a10Spec)
                || string.Equals(strike.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase))
            {
                return "a10";
            }

            MlrsPayloadSpec mlrsSpec;
            if (TryGetMlrsPayloadSpec(strike.Payload, out mlrsSpec))
            {
                return "f15";
            }

            if (string.Equals(strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase))
            {
                return "attack_heli";
            }

            if (string.Equals(strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase))
            {
                return "f15";
            }

            return "";
        }

        private bool IsAnimationEditorLoaded()
        {
            return PortableAirstrikesAnimationEditor != null && PortableAirstrikesAnimationEditor.IsLoaded;
        }

        private int CountLoadedVisualProfiles()
        {
            return visualProfileFile?.Profiles == null ? 0 : visualProfileFile.Profiles.Count;
        }

        private string BuildAdminProfileListDetail(string profileId)
        {
            VisualProfileConfig profile;
            if (visualProfileFile == null || visualProfileFile.Profiles == null || !visualProfileFile.Profiles.TryGetValue(profileId, out profile) || profile == null)
            {
                return "Profile metadata unavailable.";
            }

            var detail = "vehicle " + profile.Vehicle + " | motion " + GetVisualProfileMotionMode(profileId) + " | releases " + GetVisualProfileReleaseMode(profileId);
            string warning;
            if (visualProfileWarnings != null && visualProfileWarnings.TryGetValue(profileId, out warning) && !string.IsNullOrWhiteSpace(warning))
            {
                detail += " | " + warning;
            }

            return detail;
        }

        private List<string> GetCompatibleVisualProfileIds(string vehicle)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(vehicle) || visualProfileFile?.Profiles == null)
            {
                return ids;
            }

            foreach (var entry in visualProfileFile.Profiles)
            {
                if (entry.Value != null && IsVisualProfileVehicleMatch(entry.Value, vehicle))
                {
                    ids.Add(entry.Key);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        private bool IsVisualProfileCompatible(string profileId, string vehicle, out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(profileId))
            {
                message = "Profile id is empty.";
                return false;
            }

            if (visualProfileFile == null || visualProfileFile.Profiles == null || visualProfileFile.Profiles.Count == 0)
            {
                LoadVisualProfiles();
            }

            VisualProfileConfig profile;
            if (visualProfileFile == null || visualProfileFile.Profiles == null || !visualProfileFile.Profiles.TryGetValue(profileId, out profile) || profile == null)
            {
                message = "Unknown visual profile '" + profileId + "'.";
                return false;
            }

            if (!IsVisualProfileVehicleMatch(profile, vehicle))
            {
                message = "Profile '" + profileId + "' is vehicle " + profile.Vehicle + ", but this strike needs " + vehicle + ".";
                return false;
            }

            return true;
        }

        private void SetStrikeDamageScale(StrikeDefinition strike, string key, string value)
        {
            if (strike.DamageScales == null)
            {
                strike.DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }

            strike.DamageScales[key] = ParseAdminFloat(value, GetStrikeDamageScale(strike, key), 0f, 10f);
        }

        private LootContainerRule GetOrCreateLootRule(string key)
        {
            if (config.LootDistribution.ContainerRules == null)
            {
                config.LootDistribution.ContainerRules = new Dictionary<string, LootContainerRule>(StringComparer.OrdinalIgnoreCase);
            }

            key = NormalizeAdminToken(key);
            LootContainerRule rule;
            if (!config.LootDistribution.ContainerRules.TryGetValue(key, out rule) || rule == null)
            {
                rule = new LootContainerRule();
                config.LootDistribution.ContainerRules[key] = rule;
            }

            return rule;
        }

        private void ChangeAdminGivePage(BasePlayer player, string direction)
        {
            var state = GetAdminUiState(player);
            direction = (direction ?? "").Trim().ToLowerInvariant();
            if (direction == "next")
            {
                state.GivePage++;
            }
            else if (direction == "prev" || direction == "previous")
            {
                state.GivePage--;
            }
            else
            {
                int page;
                if (int.TryParse(direction, NumberStyles.Integer, CultureInfo.InvariantCulture, out page))
                {
                    state.GivePage = Math.Max(0, page - 1);
                }
            }

            if (state.GivePage < 0)
            {
                state.GivePage = 0;
            }
        }

        private void SetAdminGiveSort(BasePlayer player, string sort)
        {
            var state = GetAdminUiState(player);
            state.GiveSort = NormalizeAdminGiveSort(sort);
            state.GivePage = 0;
        }

        private void SetAdminGiveFilter(BasePlayer player, string filter)
        {
            var state = GetAdminUiState(player);
            state.GiveFilter = NormalizeAdminGiveFilter(filter);
            state.GivePage = 0;
        }

        private string NormalizeAdminGiveSort(string sort)
        {
            switch ((sort ?? "").Trim().Replace("-", "").Replace("_", "").ToLowerInvariant())
            {
                case "steam":
                case "steamid":
                case "userid":
                    return "steamid";
                case "state":
                case "status":
                case "online":
                    return "state";
                default:
                    return "name";
            }
        }

        private string NormalizeAdminGiveFilter(string filter)
        {
            switch ((filter ?? "").Trim().Replace("-", "").Replace("_", "").ToLowerInvariant())
            {
                case "online":
                case "connected":
                    return "online";
                case "sleeping":
                case "sleepers":
                case "offline":
                    return "sleeping";
                default:
                    return "all";
            }
        }

        private List<BasePlayer> FindAdminPlayerMatches(string query, string filter, string sort)
        {
            var matches = new List<BasePlayer>();
            query = (query ?? "").Trim().ToLowerInvariant();
            filter = NormalizeAdminGiveFilter(filter);
            foreach (var target in BasePlayer.allPlayerList)
            {
                if (target == null)
                {
                    continue;
                }

                if (filter == "online" && !target.IsConnected)
                {
                    continue;
                }

                if (filter == "sleeping" && target.IsConnected)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(query)
                    && !(target.UserIDString ?? "").Contains(query)
                    && (target.displayName == null || !target.displayName.ToLowerInvariant().Contains(query)))
                {
                    continue;
                }

                matches.Add(target);
            }

            matches.Sort((a, b) => CompareAdminPlayerMatches(a, b, sort));
            return matches;
        }

        private int CompareAdminPlayerMatches(BasePlayer a, BasePlayer b, string sort)
        {
            if (a == null && b == null)
            {
                return 0;
            }

            if (a == null)
            {
                return 1;
            }

            if (b == null)
            {
                return -1;
            }

            sort = NormalizeAdminGiveSort(sort);
            if (sort == "state")
            {
                var stateCompare = b.IsConnected.CompareTo(a.IsConnected);
                if (stateCompare != 0)
                {
                    return stateCompare;
                }
            }
            else if (sort == "steamid")
            {
                var idCompare = string.Compare(a.UserIDString ?? "", b.UserIDString ?? "", StringComparison.OrdinalIgnoreCase);
                if (idCompare != 0)
                {
                    return idCompare;
                }
            }

            var nameCompare = string.Compare(GetAdminPlayerDisplayName(a), GetAdminPlayerDisplayName(b), StringComparison.OrdinalIgnoreCase);
            if (nameCompare != 0)
            {
                return nameCompare;
            }

            return string.Compare(a.UserIDString ?? "", b.UserIDString ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private string GetAdminPlayerDisplayName(BasePlayer player)
        {
            if (player == null)
            {
                return "";
            }

            return string.IsNullOrWhiteSpace(player.displayName) ? player.UserIDString : player.displayName;
        }

        private List<string> GetSortedStrikeIds()
        {
            var ids = new List<string>();
            if (config?.StrikeDefinitions != null)
            {
                foreach (var entry in config.StrikeDefinitions)
                {
                    ids.Add(entry.Key);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        private string GetFirstStrikeId()
        {
            var ids = GetSortedStrikeIds();
            return ids.Count == 0 ? "" : ids[0];
        }

        private string BuildAdminStatsSummary()
        {
            if (storedData?.Stats == null || storedData.Stats.Count == 0)
            {
                return "none yet";
            }

            var keys = new List<string>(storedData.Stats.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            var parts = new List<string>();
            for (var i = 0; i < keys.Count && i < 8; i++)
            {
                parts.Add(keys[i] + "=" + storedData.Stats[keys[i]]);
            }

            if (keys.Count > 8)
            {
                parts.Add("+" + (keys.Count - 8) + " more");
            }

            return string.Join(", ", parts.ToArray());
        }

        private void AddAdminStrikeListRow(CuiElementContainer container, string parent, AdminUiState state, StrikeDefinition strike, float topOffset, float bottomOffset)
        {
            var selected = string.Equals(state.SelectedStrikeId, strike.Id, StringComparison.OrdinalIgnoreCase);
            var compatible = IsStrikeExecutorCompatible(strike);
            var color = selected ? "0.18 0.13 0.10 0.98" : strike.Enabled ? "0.070 0.082 0.098 0.94" : "0.055 0.058 0.064 0.85";
            var row = container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform =
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = "0 -" + FormatUiPixels(bottomOffset),
                    OffsetMax = "0 -" + FormatUiPixels(topOffset)
                }
            }, parent);

            AddUiLabel(container, row, ShortenAdminText(strike.DisplayName, 26), 10, TextAnchor.MiddleLeft, "0.03 0.48", "0.60 0.90", strike.Enabled ? "1 1 1 1" : "0.68 0.70 0.72 1");
            AddUiLabel(container, row, strike.Id + " | " + strike.RPCost + " RP | " + GetEnabledStrikeProfileAssignments(strike).Count + " profiles | " + FormatAcceptedTargetTypes(strike), 8, TextAnchor.MiddleLeft, "0.03 0.08", "0.70 0.42", compatible ? "0.66 0.74 0.80 1" : "1 0.52 0.42 1");
            AddUiButton(container, row, "Select", "portableairstrikes.adminui select " + strike.Id, "0.70 0.18", "0.84 0.82", "0.13 0.18 0.24 0.95", 8);
            AddUiButton(container, row, strike.Enabled ? "On" : "Off", "portableairstrikes.adminui togglestrike " + strike.Id, "0.86 0.18", "0.97 0.82", strike.Enabled ? "0.15 0.32 0.18 0.95" : "0.42 0.14 0.10 0.95", 8);
        }

        private void AddAdminTitle(CuiElementContainer container, string parent, string title, string subtitle, string tipKey = "")
        {
            AddUiLabel(container, parent, title, 17, TextAnchor.MiddleLeft, "0.04 0.860", "0.70 0.935", "1 0.86 0.58 1");
            AddUiLabel(container, parent, subtitle, 10, TextAnchor.MiddleLeft, "0.04 0.815", "0.88 0.860", "0.66 0.74 0.80 1");
            if (!string.IsNullOrWhiteSpace(tipKey))
            {
                AddAdminTipButton(container, parent, tipKey, "0.915 0.872", "0.950 0.922");
            }
        }

        private void AddAdminMetric(CuiElementContainer container, string parent, string title, string value, string detail, float x, float y)
        {
            AddAdminPanel(container, parent, FormatUiFloat(x) + " " + FormatUiFloat(y), FormatUiFloat(x + 0.43f) + " " + FormatUiFloat(y + 0.17f), "0.042 0.050 0.062 0.96");
            AddUiLabel(container, parent, title, 11, TextAnchor.MiddleLeft, FormatUiFloat(x + 0.025f) + " " + FormatUiFloat(y + 0.115f), FormatUiFloat(x + 0.40f) + " " + FormatUiFloat(y + 0.155f), "1 0.86 0.58 1");
            AddUiLabel(container, parent, value, 12, TextAnchor.MiddleLeft, FormatUiFloat(x + 0.025f) + " " + FormatUiFloat(y + 0.066f), FormatUiFloat(x + 0.40f) + " " + FormatUiFloat(y + 0.108f), "1 1 1 1");
            AddUiLabel(container, parent, detail, 9, TextAnchor.MiddleLeft, FormatUiFloat(x + 0.025f) + " " + FormatUiFloat(y + 0.020f), FormatUiFloat(x + 0.40f) + " " + FormatUiFloat(y + 0.060f), "0.62 0.70 0.76 1");
        }

        private void AddAdminTextFieldRow(CuiElementContainer container, string parent, string label, string value, string command, float y)
        {
            AddUiLabel(container, parent, label, 9, TextAnchor.MiddleLeft, "0.50 " + FormatUiFloat(y), "0.600 " + FormatUiFloat(y + 0.043f), "0.70 0.78 0.84 1");
            AddAdminFieldTipButton(container, parent, command, 0.604f, 0.626f, y);
            AddAdminInput(container, parent, value ?? "", command, "0.63 " + FormatUiFloat(y), "0.94 " + FormatUiFloat(y + 0.048f), 9, 128, TextAnchor.MiddleLeft);
        }

        private void AddAdminNumberRow(CuiElementContainer container, string parent, string labelA, string valueA, string commandA, float y, string labelB, string valueB, string commandB)
        {
            AddUiLabel(container, parent, labelA, 9, TextAnchor.MiddleLeft, "0.05 " + FormatUiFloat(y), "0.150 " + FormatUiFloat(y + 0.043f), "0.70 0.78 0.84 1");
            AddAdminFieldTipButton(container, parent, commandA, 0.152f, 0.174f, y);
            AddAdminNumberEditButton(container, parent, valueA ?? "", commandA, "0.18 " + FormatUiFloat(y), "0.39 " + FormatUiFloat(y + 0.048f), 9);

            if (!string.IsNullOrWhiteSpace(labelB))
            {
                AddUiLabel(container, parent, labelB, 9, TextAnchor.MiddleLeft, "0.50 " + FormatUiFloat(y), "0.600 " + FormatUiFloat(y + 0.043f), "0.70 0.78 0.84 1");
                AddAdminFieldTipButton(container, parent, commandB, 0.604f, 0.626f, y);
                AddAdminNumberEditButton(container, parent, valueB ?? "", commandB, "0.63 " + FormatUiFloat(y), "0.84 " + FormatUiFloat(y + 0.048f), 9);
            }
        }

        private void AddAdminDetailNumberRow(CuiElementContainer container, string parent, string labelA, string valueA, string commandA, float y, string labelB, string valueB, string commandB)
        {
            AddUiLabel(container, parent, labelA, 9, TextAnchor.MiddleLeft, "0.50 " + FormatUiFloat(y), "0.575 " + FormatUiFloat(y + 0.043f), "0.70 0.78 0.84 1");
            AddAdminFieldTipButton(container, parent, commandA, 0.578f, 0.596f, y);
            AddAdminNumberEditButton(container, parent, valueA ?? "", commandA, "0.60 " + FormatUiFloat(y), "0.70 " + FormatUiFloat(y + 0.048f), 9);

            if (!string.IsNullOrWhiteSpace(labelB))
            {
                AddUiLabel(container, parent, labelB, 9, TextAnchor.MiddleLeft, "0.725 " + FormatUiFloat(y), "0.805 " + FormatUiFloat(y + 0.043f), "0.70 0.78 0.84 1");
                AddAdminFieldTipButton(container, parent, commandB, 0.808f, 0.828f, y);
                AddAdminNumberEditButton(container, parent, valueB ?? "", commandB, "0.835 " + FormatUiFloat(y), "0.94 " + FormatUiFloat(y + 0.048f), 9);
            }
        }

        private void AddAdminFieldTipButton(CuiElementContainer container, string parent, string command, float xMin, float xMax, float y)
        {
            var tipKey = BuildAdminTipKeyFromFieldCommand(command);
            if (string.IsNullOrWhiteSpace(tipKey))
            {
                return;
            }

            AddAdminTipButton(container, parent, tipKey, FormatUiFloat(xMin) + " " + FormatUiFloat(y + 0.008f), FormatUiFloat(xMax) + " " + FormatUiFloat(y + 0.040f));
        }

        private void AddAdminTipButton(CuiElementContainer container, string parent, string tipKey, string anchorMin, string anchorMax)
        {
            if (string.IsNullOrWhiteSpace(tipKey))
            {
                return;
            }

            AddUiButton(container, parent, "?", "portableairstrikes.adminui tip " + tipKey, anchorMin, anchorMax, "0.16 0.30 0.42 0.95", 8);
        }

        private string BuildAdminTipKeyFromFieldCommand(string command)
        {
            var prefix = "portableairstrikes.adminfield ";
            command = (command ?? "").Trim();
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            var tail = command.Substring(prefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(tail))
            {
                return "";
            }

            var parts = tail.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "";
            }

            var field = parts[0].Trim().ToLowerInvariant();
            var lootParts = field.Split('.');
            if (lootParts.Length == 3 && lootParts[0] == "loot")
            {
                return "field.loot." + lootParts[2];
            }

            return "field." + field;
        }

        private string GetAdminTooltip(string key)
        {
            key = (key ?? "").Trim().ToLowerInvariant();
            switch (key)
            {
                case "page.dashboard": return "Dashboard is read-only: use it to confirm enabled strikes, item mode, currency, loaded profiles, active calls, and safety gates.";
                case "page.give": return "Give Items grants charge-backed targeting binoculars to online or sleeping players. Amount changes charges, not stack size.";
                case "page.strikes": return "Strikes are wrappers: permission, RP, tier, cooldowns, warning delay, accepted target types, and included profile assignment.";
                case "page.balance": return "Balance changes wrapper multipliers plus per-profile delay/limit. Authored profile timing and payload shape stay in VisualProfiles.json.";
                case "page.safety": return "Safety controls validation, warning fanout, cancellation, currency, item consumption, and server-wide active-call limits.";
                case "page.visuals": return "Strike Profiles includes authored VisualProfiles.json entries and opens the animation editor when it is loaded.";
                case "page.lootaudit": return "Loot/Audit controls optional loot injection and Discord webhook mirroring for call lifecycle events.";
                case "page.activity": return "Activity is read-only: active calls, recent audit rows, and stored counters for quick diagnostics.";
                case "page.commands": return "Commands is a reference split between player chat commands and server/admin console commands.";
                case "page.help": return "Help explains the operator workflow from granting items through validation, launch, profiles, and admin edits.";

                case "field.give_search": return "Search filters online and sleeping players by display name or Steam ID before granting binocular charges.";
                case "field.give_amount": return "Amount is the number of charges to add to the target player's airstrike binocular item.";
                case "field.give_filter": return "Filter chooses whether the Give table shows all known players, connected players, or sleepers only.";
                case "field.give_sort": return "Sort changes the Give table ordering by player name, Steam ID, or online/sleeping state.";
                case "field.strike.add": return "Add Strike creates a disabled wrapper with safe defaults. Include at least one strike profile before enabling it.";

                case "field.strike.display": return "Name changes the player/admin display name for this strike wrapper.";
                case "field.strike.permission": return "Permission is the oxide permission required for players to use this strike wrapper.";
                case "field.strike.rpcost": return "RP is the base currency cost before VIP discount permissions and admin/free-mode bypasses.";
                case "field.strike.tier": return "Tier controls selection ordering and display grouping; it does not grant permission by itself.";
                case "field.strike.warning": return "Warn is seconds between acceptance and payload launch for this strike wrapper.";
                case "field.strike.playercd": return "Player Cooldown is seconds before the same player can call this wrapper again.";
                case "field.strike.clancd": return "Clan Cooldown is seconds before another member of the caller's team/clan can call this wrapper.";
                case "field.strike.globalcd": return "Global Cooldown is seconds before anyone on the server can call this wrapper again.";
                case "field.strike.spreadmult": return "Spread x multiplies authored profile spread; keep positive values to avoid inverted behavior.";
                case "field.strike.impactmult": return "Impact x multiplies payload impact radius for profiles that support radius scaling.";
                case "field.strike.damagemult": return "Damage x multiplies outgoing payload damage across supported target classes.";
                case "field.strike.vehicledamagemult": return "Vehicle x applies an extra vehicle damage multiplier on supported payload hits.";
                case "field.strike.splashmult": return "Splash x multiplies splash radius for profiles that expose splash damage.";
                case "field.strike.d_players": return "Players scales damage against player targets for this wrapper.";
                case "field.strike.d_buildings": return "Buildings scales damage against building blocks and deployable structures.";
                case "field.strike.d_vehicles": return "Vehicles scales damage against vehicle entities.";
                case "field.strike.tracktimemult": return "Track Sec x multiplies homing missile tracking duration.";
                case "field.strike.trackdistancemult": return "Track Dist x multiplies homing missile acquisition distance.";
                case "field.strike.linemult": return "Line x multiplies A-10 line length for compatible profiles.";
                case "field.strike.widthmult": return "Width x multiplies A-10 pass width for compatible profiles.";
                case "field.strike.pulsemult": return "Pulse x multiplies delay between A-10 damage pulses.";
                case "field.strikeprofile.limit": return "Limit caps how many payload releases this profile can contribute to the selected wrapper. Zero uses profile default.";
                case "field.strikeprofile.delay": return "Delay waits this many seconds before this included profile starts after the wrapper is accepted.";

                case "field.general.maxrange": return "Max Range is the farthest allowed target distance from the caller.";
                case "field.general.mindistance": return "Min Distance blocks calls too close to the caller.";
                case "field.general.safezoneradius": return "Safe Radius expands safe-zone blocking around protected areas.";
                case "field.general.warningdelay": return "Warning Delay is the default launch delay for wrappers without their own warning value.";
                case "field.general.maxsim": return "Max Active limits all simultaneous airstrike calls on the server.";
                case "field.general.maxheavy": return "Max Heavy limits simultaneous heavy strikes such as large aircraft, MLRS, or other heavy profiles.";
                case "field.general.nearbyradius": return "Nearby Radius controls how far heavy-strike nearby warning messages can reach.";
                case "field.general.history": return "History controls how many recent audit records stay in the data file.";
                case "field.general.monumentpadding": return "Monument Pad expands configured monument block zones.";
                case "field.general.monumentdefault": return "Default Zone is the fallback radius for configured blocked monuments without exact bounds.";
                case "field.general.requireping": return "Require Ping forces players to use a stored ping/map target instead of loose direct calls.";
                case "field.general.los": return "LOS Required checks line of sight from caller to ping before accepting the call.";
                case "field.general.safezones": return "Safe Zones blocks targets inside or near safe zones.";
                case "field.general.monuments": return "Monuments blocks targets inside configured monuments.";
                case "field.general.monumentsheavy": return "Heavy Only applies monument blocking only to heavy strikes.";
                case "field.general.clancd": return "Clan CDs enables shared caller team/clan cooldowns.";
                case "field.general.globalcd": return "Global CDs enables server-wide wrapper cooldowns.";
                case "field.general.teamwarn": return "Team Warn sends accepted-strike warnings to the caller's team.";
                case "field.general.nearbywarn": return "Nearby Warn sends heavy-strike warnings to nearby online players inside the configured radius.";
                case "field.general.cancel": return "Cancel allows players to cancel their own calls before payload impact.";
                case "field.general.refundcancel": return "Refund Cancel returns cost/item consumption when a player cancels before impact.";
                case "field.general.debug": return "Debug enables extra PortableAirstrikes diagnostic logging.";

                case "field.currency.enabled": return "Currency toggles RP/currency charging. Off means calls are free except for item rules.";
                case "field.currency.freeadmin": return "Free Admin lets admins bypass currency cost during calls.";
                case "field.currency.provider": return "Currency provider switches the active adapter, usually ServerRewards or Economics.";
                case "field.item.consume": return "Consume Item removes one charge after a successful accepted call.";
                case "field.item.adminbypass": return "Admin Bypass lets admins call strikes without holding a targeting item.";

                case "field.visual.enabled": return "Visuals toggles delivery visuals globally; payload execution can still happen without visuals.";
                case "field.visual.drones": return "Drones toggles drone visual carriers for compatible drone/drop profiles.";
                case "field.visual.aircraft": return "Aircraft toggles aircraft visual carriers for compatible profiles.";
                case "field.visual.mortars": return "Mortars toggles mortar visual artillery sources.";
                case "field.visual.crew": return "Crew NPC toggles mortar crew NPC visuals.";
                case "field.visual.sounds": return "Sounds toggles flyover and delivery sound effects.";
                case "field.visual.rotor": return "Rotor Wash toggles rotor wash effects for supported vehicles.";
                case "field.visual.destroyable": return "Destroyable lets delivery vehicles be damaged before their payload releases.";
                case "field.visual.requirecarrier": return "Require Carrier makes payload release depend on a live delivery carrier when supported.";
                case "field.visual.refundcarrier": return "Refund Carrier refunds failed calls when the required carrier is destroyed before payload release.";

                case "field.loot.enabled": return "Loot Enabled toggles optional injection of airstrike item charges into configured loot containers.";
                case "field.loot.chance": return "Chance is the probability that a matching loot container gets an airstrike item.";
                case "field.loot.min": return "Min is the minimum charges added when a loot rule succeeds.";
                case "field.loot.max": return "Max is the maximum charges added when a loot rule succeeds.";
                case "field.audit.enabled": return "Audit Webhook toggles Discord mirroring for selected call lifecycle events.";
                case "field.audit.started": return "Started sends webhook messages when a call is accepted.";
                case "field.audit.completed": return "Completed sends webhook messages when a call finishes successfully.";
                case "field.audit.failures": return "Failures sends webhook messages for validation failures and refunds.";
                case "field.audit.cancels": return "Cancels sends webhook messages when players cancel calls.";
                case "field.audit.validation": return "Validation sends webhook messages for blocked call attempts.";
                case "field.audit.url": return "Webhook URL is the Discord endpoint used for audit mirroring.";
                case "field.audit.username": return "Username is the display name used by Discord audit messages.";
                case "field.audit.mention": return "Mention is optional text prepended to Discord audit messages.";
                case "field.audit.avatar": return "Avatar URL is the optional Discord webhook avatar image.";
            }

            return "No detailed help is registered for '" + key + "' yet.";
        }

        private void AddAdminOptionButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, bool selected)
        {
            AddUiButton(container, parent, text, command, anchorMin, anchorMax, selected ? "0.18 0.13 0.10 0.98" : "0.13 0.18 0.24 0.95", 8);
        }

        private void AddAdminNumberEditButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, int size)
        {
            AddUiButton(container, parent, text, BuildAdminNumberEditCommand(command), anchorMin, anchorMax, "0.015 0.018 0.024 0.92", size);
        }

        private string BuildAdminNumberEditCommand(string command)
        {
            var prefix = "portableairstrikes.adminfield ";
            command = (command ?? "").Trim();
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            var tail = command.Substring(prefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(tail))
            {
                return "";
            }

            var parts = tail.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "";
            }

            if (parts[0].StartsWith("strike.", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
            {
                return "portableairstrikes.adminui numberedit " + parts[0] + " " + parts[1];
            }

            if (parts[0].StartsWith("strikeprofile.", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
            {
                return "portableairstrikes.adminui numberedit " + parts[0] + " " + parts[1];
            }

            return "portableairstrikes.adminui numberedit " + parts[0];
        }

        private void AddAdminProfileListRow(CuiElementContainer container, string parent, StrikeDefinition strike, string profileId, string detail, bool selected, string command, float topOffset, float bottomOffset, bool editorLoaded)
        {
            var row = container.Add(new CuiPanel
            {
                Image = { Color = selected ? "0.18 0.13 0.10 0.98" : "0.070 0.082 0.098 0.94" },
                RectTransform =
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = "0 -" + FormatUiPixels(bottomOffset),
                    OffsetMax = "0 -" + FormatUiPixels(topOffset)
                }
            }, parent);

            var label = string.Equals(profileId, "auto", StringComparison.OrdinalIgnoreCase) ? "Auto" : profileId;
            AddUiLabel(container, row, ShortenAdminText(label, 34), 10, TextAnchor.MiddleLeft, "0.03 0.47", "0.64 0.88", selected ? "1 0.86 0.58 1" : "1 1 1 1");
            AddUiLabel(container, row, ShortenAdminText(detail, 92), 8, TextAnchor.MiddleLeft, "0.03 0.10", "0.74 0.42", "0.66 0.74 0.80 1");
            var toggleCommand = !string.IsNullOrWhiteSpace(command) && command.IndexOf("profiletoggle", StringComparison.OrdinalIgnoreCase) >= 0;
            var buttonText = toggleCommand ? (selected ? "Remove" : "Include") : (selected ? "Selected" : "Select");
            var buttonCommand = (!editorLoaded || (selected && !toggleCommand)) ? "" : command;
            AddUiButton(container, row, buttonText, buttonCommand, "0.77 0.18", "0.95 0.82", selected ? "0.15 0.31 0.18 0.95" : editorLoaded ? "0.42 0.18 0.12 0.95" : "0.09 0.10 0.11 0.75", 8);
        }

        private void AddAdminToggleGrid(CuiElementContainer container, string parent, string[] specs, float xMin, float yTop)
        {
            for (var i = 0; i < specs.Length; i++)
            {
                var split = specs[i].Split(':');
                if (split.Length != 2)
                {
                    continue;
                }

                var col = i % 4;
                var row = i / 4;
                var x = xMin + col * 0.225f;
                var y = yTop - row * 0.056f;
                var enabled = GetAdminToggleValue(split[1]);
                AddUiButton(container, parent, split[0] + " " + (enabled ? "ON" : "OFF"), "portableairstrikes.adminui toggle " + split[1],
                    FormatUiFloat(x) + " " + FormatUiFloat(y),
                    FormatUiFloat(x + 0.195f) + " " + FormatUiFloat(y + 0.040f),
                    enabled ? "0.15 0.31 0.18 0.95" : "0.13 0.15 0.17 0.95",
                    8);
                AddAdminTipButton(container, parent, "field." + split[1], FormatUiFloat(x + 0.198f) + " " + FormatUiFloat(y + 0.006f), FormatUiFloat(x + 0.218f) + " " + FormatUiFloat(y + 0.034f));
            }
        }

        private bool GetAdminToggleValue(string field)
        {
            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "general.requireping": return config.General.RequireBinocularPing;
                case "general.los": return config.General.RequireLineOfSightToPing;
                case "general.safezones": return config.General.BlockSafeZones;
                case "general.monuments": return config.General.BlockMonuments;
                case "general.monumentsheavy": return config.General.BlockMonumentsForHeavyStrikesOnly;
                case "general.clancd": return config.General.EnableClanCooldowns;
                case "general.globalcd": return config.General.EnableGlobalCooldowns;
                case "general.teamwarn": return config.General.NotifyCallerTeamOnAcceptedStrike;
                case "general.nearbywarn": return config.General.NotifyNearbyPlayersOnHeavyStrikes;
                case "general.cancel": return config.General.AllowPlayerCancelBeforeImpact;
                case "general.refundcancel": return config.General.RefundPlayerCancelledCallsBeforeImpact;
                case "general.debug": return config.General.DebugMode;
                case "currency.enabled": return config.Currency.Enabled;
                case "currency.freeadmin": return config.Currency.AllowFreeAdminCalls;
                case "item.consume": return config.AirstrikeItem.ConsumeOnSuccessfulCall;
                case "item.adminbypass": return config.AirstrikeItem.AllowAdminsWithoutItem;
                case "visual.enabled": return config.DeliveryVisuals.Enabled;
                case "visual.drones": return config.DeliveryVisuals.SpawnDroneVisuals;
                case "visual.aircraft": return config.DeliveryVisuals.SpawnAircraftVisuals;
                case "visual.mortars": return config.DeliveryVisuals.SpawnMortarArtilleryVisuals;
                case "visual.crew": return config.DeliveryVisuals.SpawnMortarCrewNpc;
                case "visual.sounds": return config.DeliveryVisuals.SpawnFlyoverSoundEffects;
                case "visual.rotor": return config.DeliveryVisuals.SpawnRotorWashEffects;
                case "visual.destroyable": return config.DeliveryVisuals.DeliveryVehiclesCanBeDestroyed;
                case "visual.requirecarrier": return config.DeliveryVisuals.PayloadRequiresLiveDeliveryVehicle;
                case "visual.refundcarrier": return config.DeliveryVisuals.RefundIfDeliveryVehicleDestroyedBeforePayload;
                case "loot.enabled": return config.LootDistribution.Enabled;
                case "audit.enabled": return config.AuditWebhooks.Enabled;
                case "audit.started": return config.AuditWebhooks.SendStartedCalls;
                case "audit.completed": return config.AuditWebhooks.SendCompletedCalls;
                case "audit.failures": return config.AuditWebhooks.SendFailuresAndRefunds;
                case "audit.cancels": return config.AuditWebhooks.SendPlayerCancels;
                case "audit.validation": return config.AuditWebhooks.SendValidationFailures;
                default: return false;
            }
        }

        private void AddAdminLootRuleRows(CuiElementContainer container, string body)
        {
            var keys = new List<string>();
            if (config.LootDistribution.ContainerRules != null)
            {
                foreach (var entry in config.LootDistribution.ContainerRules)
                {
                    keys.Add(entry.Key);
                }
            }

            keys.Sort(StringComparer.OrdinalIgnoreCase);
            AddUiLabel(container, body, "Loot container rules", 12, TextAnchor.MiddleLeft, "0.05 0.560", "0.36 0.605", "1 0.86 0.58 1");
            AddUiLabel(container, body, "Chance", 8, TextAnchor.MiddleCenter, "0.24 0.565", "0.34 0.595", "0.70 0.78 0.84 1");
            AddAdminTipButton(container, body, "field.loot.chance", "0.343 0.568", "0.362 0.592");
            AddUiLabel(container, body, "Min", 8, TextAnchor.MiddleCenter, "0.38 0.565", "0.46 0.595", "0.70 0.78 0.84 1");
            AddAdminTipButton(container, body, "field.loot.min", "0.463 0.568", "0.482 0.592");
            AddUiLabel(container, body, "Max", 8, TextAnchor.MiddleCenter, "0.50 0.565", "0.58 0.595", "0.70 0.78 0.84 1");
            AddAdminTipButton(container, body, "field.loot.max", "0.583 0.568", "0.602 0.592");
            for (var i = 0; i < keys.Count && i < 4; i++)
            {
                var key = keys[i];
                var rule = GetOrCreateLootRule(key);
                var y = 0.505f - i * 0.052f;
                AddUiLabel(container, body, key, 9, TextAnchor.MiddleLeft, "0.055 " + FormatUiFloat(y), "0.23 " + FormatUiFloat(y + 0.038f), "0.70 0.78 0.84 1");
                AddAdminNumberEditButton(container, body, FormatFloat(rule.Chance), "portableairstrikes.adminfield loot." + key + ".chance ", "0.24 " + FormatUiFloat(y), "0.36 " + FormatUiFloat(y + 0.040f), 8);
                AddAdminNumberEditButton(container, body, rule.MinAmount.ToString(CultureInfo.InvariantCulture), "portableairstrikes.adminfield loot." + key + ".min ", "0.38 " + FormatUiFloat(y), "0.48 " + FormatUiFloat(y + 0.040f), 8);
                AddAdminNumberEditButton(container, body, rule.MaxAmount.ToString(CultureInfo.InvariantCulture), "portableairstrikes.adminfield loot." + key + ".max ", "0.50 " + FormatUiFloat(y), "0.60 " + FormatUiFloat(y + 0.040f), 8);
            }
        }

        private void OpenAdminNumberEdit(BasePlayer player, string field, string id)
        {
            if (player == null)
            {
                return;
            }

            var state = GetAdminUiState(player);
            string label;
            string current;
            if (!TryGetAdminNumberFieldSnapshot(player, field, id, out label, out current))
            {
                ShowAdminStatus(player, "Unknown numeric field '" + field + "'.");
                return;
            }

            state.NumberEdit = new PendingAdminNumberEdit
            {
                Field = (field ?? "").Trim().ToLowerInvariant(),
                Id = (id ?? "").Trim(),
                Label = label,
                CurrentValue = current,
                DraftValue = current,
                HasDraft = !string.IsNullOrWhiteSpace(current)
            };
            ShowAdminStatus(player, "Editing " + label + ".");
        }

        private void ApplyAdminNumberEditKey(BasePlayer player, string token)
        {
            if (player == null)
            {
                return;
            }

            var state = GetAdminUiState(player);
            var edit = state.NumberEdit;
            if (edit == null)
            {
                return;
            }

            var key = (token ?? "").Trim().ToLowerInvariant();
            var draft = edit.HasDraft ? edit.DraftValue ?? "" : "";
            if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
            {
                draft += key;
            }
            else if (key == "dot" || key == ".")
            {
                if (!draft.Contains("."))
                {
                    draft = string.IsNullOrWhiteSpace(draft) ? "0." : draft + ".";
                }
            }
            else if (key == "minus" || key == "-")
            {
                draft = draft.StartsWith("-", StringComparison.Ordinal) ? draft.Substring(1) : "-" + draft;
            }
            else if (key == "back" || key == "del" || key == "delete")
            {
                if (draft.Length > 0)
                {
                    draft = draft.Substring(0, draft.Length - 1);
                }
            }
            else if (key == "clear" || key == "clr")
            {
                draft = "";
            }
            else if (key == "current" || key == "cur")
            {
                string label;
                string current;
                draft = TryGetAdminNumberFieldSnapshot(player, edit.Field, edit.Id, out label, out current) ? current : edit.CurrentValue;
            }
            else
            {
                return;
            }

            if (draft.Length > 32)
            {
                draft = draft.Substring(0, 32);
            }

            edit.DraftValue = draft;
            edit.HasDraft = !string.IsNullOrWhiteSpace(draft);
            ShowAdminStatus(player, "Editing " + edit.Label + ".");
        }

        private string CommitPendingAdminNumberEdit(BasePlayer player)
        {
            if (player == null)
            {
                return "";
            }

            var state = GetAdminUiState(player);
            var edit = state.NumberEdit;
            if (edit == null)
            {
                return "No number edit is open.";
            }

            if (!edit.HasDraft || string.IsNullOrWhiteSpace(edit.DraftValue))
            {
                return "No value entered for " + edit.Label + ".";
            }

            var value = edit.DraftValue.Trim();
            if (IsAdminIntegerNumberField(edit.Field))
            {
                int parsedInt;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    return "Invalid whole number for " + edit.Label + ".";
                }
            }
            else
            {
                float parsedFloat;
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedFloat))
                {
                    return "Invalid number for " + edit.Label + ".";
                }
            }

            string message;
            if (string.Equals(edit.Field, "give_amount", StringComparison.OrdinalIgnoreCase))
            {
                int amount;
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount);
                state.GiveAmount = Mathf.Clamp(amount, 1, GetAirstrikeMaxChargesPerItem());
                message = "Give amount set to " + state.GiveAmount + ".";
            }
            else
            {
                message = ApplyAdminField(player, edit.Field, edit.Id, value);
            }

            ClearPendingAdminNumberEdit(state);
            CuiHelper.DestroyUi(player, AdminNumberEditUiName);
            return message;
        }

        private void ClearPendingAdminNumberEdit(AdminUiState state)
        {
            if (state != null)
            {
                state.NumberEdit = null;
            }
        }

        private bool TryGetAdminNumberFieldSnapshot(BasePlayer player, string field, string id, out string label, out string current)
        {
            label = "";
            current = "";
            field = (field ?? "").Trim().ToLowerInvariant();
            id = (id ?? "").Trim();

            if (field == "give_amount")
            {
                var state = GetAdminUiState(player);
                label = "Give amount";
                current = state.GiveAmount.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (field.StartsWith("strike.", StringComparison.OrdinalIgnoreCase))
            {
                StrikeDefinition strike;
                if (!TryGetStrike(id, out strike))
                {
                    return false;
                }

                var sub = field.Substring("strike.".Length);
                label = ShortenAdminText(strike.DisplayName, 24) + " " + GetFriendlyAdminNumberFieldName(field);
                switch (sub)
                {
                    case "rpcost": current = strike.RPCost.ToString(CultureInfo.InvariantCulture); return true;
                    case "tier": current = strike.Tier.ToString(CultureInfo.InvariantCulture); return true;
                    case "warning": current = FormatFloat(strike.WarningDelaySeconds); return true;
                    case "playercd": current = FormatFloat(strike.CooldownPerPlayerSeconds); return true;
                    case "clancd": current = FormatFloat(strike.CooldownPerClanSeconds); return true;
                    case "globalcd": current = FormatFloat(strike.GlobalCooldownSeconds); return true;
                    case "base": current = strike.BaseCount.ToString(CultureInfo.InvariantCulture); return true;
                    case "max": current = strike.MaxCount.ToString(CultureInfo.InvariantCulture); return true;
                    case "spread": current = FormatFloat(strike.SpreadRadius); return true;
                    case "spreadmult": current = FormatFloat(strike.SpreadMultiplier); return true;
                    case "rockets": current = strike.RocketCount.ToString(CultureInfo.InvariantCulture); return true;
                    case "missiles": current = strike.MissileCount.ToString(CultureInfo.InvariantCulture); return true;
                    case "burst": current = strike.BurstCount.ToString(CultureInfo.InvariantCulture); return true;
                    case "line": current = FormatFloat(strike.LineLength); return true;
                    case "linemult": current = FormatFloat(strike.LineLengthMultiplier); return true;
                    case "width": current = FormatFloat(strike.Width); return true;
                    case "widthmult": current = FormatFloat(strike.WidthMultiplier); return true;
                    case "impact": current = FormatFloat(strike.ImpactRadius); return true;
                    case "impactmult": current = FormatFloat(strike.ImpactRadiusMultiplier); return true;
                    case "pulse": current = FormatFloat(strike.PulseDelaySeconds); return true;
                    case "pulsemult": current = FormatFloat(strike.PulseDelayMultiplier); return true;
                    case "tracktime": current = FormatFloat(strike.MaxTrackingSeconds); return true;
                    case "tracktimemult": current = FormatFloat(strike.TrackingSecondsMultiplier); return true;
                    case "trackdistance": current = FormatFloat(strike.MaxTrackingDistance); return true;
                    case "trackdistancemult": current = FormatFloat(strike.TrackingDistanceMultiplier); return true;
                    case "vehiclescale": current = FormatFloat(strike.VehicleDamageScale); return true;
                    case "damagemult": current = FormatFloat(strike.DamageMultiplier); return true;
                    case "vehicledamagemult": current = FormatFloat(strike.VehicleDamageMultiplier); return true;
                    case "splash": current = FormatFloat(strike.SplashRadius); return true;
                    case "splashmult": current = FormatFloat(strike.SplashRadiusMultiplier); return true;
                    case "d_players": current = FormatFloat(GetStrikeDamageScale(strike, "Players")); return true;
                    case "d_buildings": current = FormatFloat(GetStrikeDamageScale(strike, "Buildings")); return true;
                    case "d_vehicles": current = FormatFloat(GetStrikeDamageScale(strike, "Vehicles")); return true;
                    case "d_deployables": current = FormatFloat(GetStrikeDamageScale(strike, "Deployables")); return true;
                    case "d_turrets": current = FormatFloat(GetStrikeDamageScale(strike, "Turrets")); return true;
                }

                return false;
            }

            if (field.StartsWith("strikeprofile.", StringComparison.OrdinalIgnoreCase))
            {
                var profileParts = id.Split('|');
                if (profileParts.Length != 2)
                {
                    return false;
                }

                StrikeDefinition strike;
                StrikeProfileAssignment assignment;
                if (!TryGetStrike(profileParts[0], out strike) || !TryGetStrikeProfileAssignment(strike, profileParts[1], out assignment))
                {
                    return false;
                }

                var sub = field.Substring("strikeprofile.".Length);
                label = ShortenAdminText(strike.DisplayName, 18) + " " + ShortenAdminText(assignment.ProfileId, 18) + " " + GetFriendlyAdminNumberFieldName(field);
                switch (sub)
                {
                    case "delay": current = FormatFloat(assignment.StartDelaySeconds); return true;
                    case "limit": current = assignment.PayloadCountLimit.ToString(CultureInfo.InvariantCulture); return true;
                }

                return false;
            }

            label = GetFriendlyAdminNumberFieldName(field);
            switch (field)
            {
                case "general.maxrange": current = FormatFloat(config.General.MaxCallRange); return true;
                case "general.mindistance": current = FormatFloat(config.General.MinimumDistanceFromCaller); return true;
                case "general.safezoneradius": current = FormatFloat(config.General.SafeZoneBlockRadius); return true;
                case "general.warningdelay": current = FormatFloat(config.General.DefaultWarningDelaySeconds); return true;
                case "general.maxsim": current = config.General.MaxSimultaneousStrikes.ToString(CultureInfo.InvariantCulture); return true;
                case "general.maxheavy": current = config.General.MaxSimultaneousHeavyStrikes.ToString(CultureInfo.InvariantCulture); return true;
                case "general.nearbyradius": current = FormatFloat(config.General.NearbyHeavyStrikeWarningRadius); return true;
                case "general.history": current = config.General.RecentCallHistoryLimit.ToString(CultureInfo.InvariantCulture); return true;
                case "general.monumentpadding": current = FormatFloat(config.General.MonumentBlockRadiusPadding); return true;
                case "general.monumentdefault": current = FormatFloat(config.General.DefaultMonumentBlockRadius); return true;
                case "visual.dronedistance": current = FormatFloat(config.DeliveryVisuals.DroneFlyoverDistance); return true;
                case "visual.droneheight": current = FormatFloat(config.DeliveryVisuals.DroneFlyoverHeight); return true;
                case "visual.airdistance": current = FormatFloat(config.DeliveryVisuals.AircraftFlyoverDistance); return true;
                case "visual.moverate": current = FormatFloat(config.DeliveryVisuals.VisualMoveIntervalSeconds); return true;
                case "visual.heliheight": current = FormatFloat(config.DeliveryVisuals.AttackHeliFlyoverHeight); return true;
                case "visual.cargoheight": current = FormatFloat(config.DeliveryVisuals.CargoPlaneFlyoverHeight); return true;
                case "visual.a10height": current = FormatFloat(config.DeliveryVisuals.A10FlyoverHeight); return true;
                case "visual.mlrsheight": current = FormatFloat(config.DeliveryVisuals.MlrsAircraftFlyoverHeight); return true;
                case "visual.dronedelay": current = FormatFloat(config.DeliveryVisuals.DroneFirstPayloadDelaySeconds); return true;
                case "visual.helidelay": current = FormatFloat(config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds); return true;
                case "visual.cargodelay": current = FormatFloat(config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds); return true;
                case "visual.a10delay": current = FormatFloat(config.DeliveryVisuals.A10FirstPayloadDelaySeconds); return true;
                case "visual.mlrsdelay": current = FormatFloat(config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds); return true;
                case "visual.soundgap": current = FormatFloat(config.DeliveryVisuals.FlyoverSoundIntervalSeconds); return true;
            }

            var parts = field.Split('.');
            if (parts.Length == 3 && parts[0] == "loot" && config.LootDistribution.ContainerRules != null)
            {
                LootContainerRule rule;
                if (!config.LootDistribution.ContainerRules.TryGetValue(parts[1], out rule) || rule == null)
                {
                    return false;
                }

                label = "Loot " + parts[1] + " " + parts[2];
                switch (parts[2])
                {
                    case "chance": current = FormatFloat(rule.Chance); return true;
                    case "min": current = rule.MinAmount.ToString(CultureInfo.InvariantCulture); return true;
                    case "max": current = rule.MaxAmount.ToString(CultureInfo.InvariantCulture); return true;
                }
            }

            return false;
        }

        private bool IsAdminIntegerNumberField(string field)
        {
            field = (field ?? "").Trim().ToLowerInvariant();
            if (field == "give_amount")
            {
                return true;
            }

            if (field.StartsWith("strike.", StringComparison.OrdinalIgnoreCase))
            {
                switch (field.Substring("strike.".Length))
                {
                    case "rpcost":
                    case "tier":
                    case "base":
                    case "max":
                    case "rockets":
                    case "missiles":
                    case "burst":
                        return true;
                }
            }

            if (field.StartsWith("strikeprofile.", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(field.Substring("strikeprofile.".Length), "limit", StringComparison.OrdinalIgnoreCase);
            }

            return field == "general.maxsim"
                || field == "general.maxheavy"
                || field == "general.history"
                || field.EndsWith(".min", StringComparison.OrdinalIgnoreCase)
                || field.EndsWith(".max", StringComparison.OrdinalIgnoreCase);
        }

        private string GetFriendlyAdminNumberFieldName(string field)
        {
            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "give_amount": return "Give amount";
                case "strike.rpcost": return "RP cost";
                case "strike.tier": return "Tier";
                case "strike.warning": return "Warning delay";
                case "strike.playercd": return "Player cooldown";
                case "strike.clancd": return "Clan cooldown";
                case "strike.globalcd": return "Global cooldown";
                case "strike.base": return "Base count";
                case "strike.max": return "Max count";
                case "strike.spread": return "Spread radius";
                case "strike.rockets": return "Rocket count";
                case "strike.missiles": return "Missile count";
                case "strike.burst": return "Burst count";
                case "strike.line": return "Line length";
                case "strike.width": return "Width";
                case "strike.impact": return "Impact radius";
                case "strike.pulse": return "Pulse delay";
                case "strike.tracktime": return "Track seconds";
                case "strike.trackdistance": return "Track distance";
                case "strike.vehiclescale": return "Vehicle scale";
                case "strike.splash": return "Splash radius";
                case "strike.d_players": return "Player damage";
                case "strike.d_buildings": return "Building damage";
                case "strike.d_vehicles": return "Vehicle damage";
                case "strike.d_deployables": return "Deployable damage";
                case "strike.d_turrets": return "Turret damage";
                case "strike.spreadmult": return "Spread multiplier";
                case "strike.damagemult": return "Damage multiplier";
                case "strike.vehicledamagemult": return "Vehicle damage multiplier";
                case "strike.splashmult": return "Splash multiplier";
                case "strike.impactmult": return "Impact multiplier";
                case "strike.tracktimemult": return "Tracking seconds multiplier";
                case "strike.trackdistancemult": return "Tracking distance multiplier";
                case "strike.linemult": return "Line multiplier";
                case "strike.widthmult": return "Width multiplier";
                case "strike.pulsemult": return "Pulse multiplier";
                case "strikeprofile.delay": return "Start delay";
                case "strikeprofile.limit": return "Count limit";
                case "general.maxrange": return "Max range";
                case "general.mindistance": return "Min distance";
                case "general.safezoneradius": return "Safe radius";
                case "general.warningdelay": return "Warning delay";
                case "general.maxsim": return "Max active";
                case "general.maxheavy": return "Max heavy";
                case "general.nearbyradius": return "Nearby radius";
                case "general.history": return "History limit";
                case "general.monumentpadding": return "Monument padding";
                case "general.monumentdefault": return "Default monument radius";
                case "visual.dronedistance": return "Drone distance";
                case "visual.droneheight": return "Drone height";
                case "visual.airdistance": return "Aircraft distance";
                case "visual.moverate": return "Move rate";
                case "visual.heliheight": return "Heli height";
                case "visual.cargoheight": return "Cargo height";
                case "visual.a10height": return "A10 height";
                case "visual.mlrsheight": return "MLRS height";
                case "visual.dronedelay": return "Drone delay";
                case "visual.helidelay": return "Heli delay";
                case "visual.cargodelay": return "Cargo delay";
                case "visual.a10delay": return "A10 delay";
                case "visual.mlrsdelay": return "MLRS delay";
                case "visual.soundgap": return "Sound gap";
                default: return field;
            }
        }

        private void ShowAdminNumberEditUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            var state = GetAdminUiState(player);
            var edit = state.NumberEdit;
            if (edit == null)
            {
                CuiHelper.DestroyUi(player, AdminNumberEditUiName);
                return;
            }

            CuiHelper.DestroyUi(player, AdminNumberEditUiName);
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.030 0.036 0.045 0.985" },
                RectTransform = { AnchorMin = "0.370 0.195", AnchorMax = "0.630 0.705" }
            }, "Overlay", AdminNumberEditUiName);

            AddUiLabel(container, root, "Exact Number", 15, TextAnchor.MiddleLeft, "0.075 0.890", "0.68 0.965", "1 0.86 0.58 1");
            AddUiLabel(container, root, ShortenAdminText(edit.Label, 42), 10, TextAnchor.MiddleLeft, "0.075 0.820", "0.925 0.875", "0.70 0.78 0.84 1");
            AddAdminPanel(container, root, "0.075 0.720", "0.925 0.805", "0.012 0.015 0.020 0.95");
            AddUiLabel(container, root, edit.HasDraft ? edit.DraftValue : "", 16, TextAnchor.MiddleRight, "0.105 0.730", "0.895 0.795", "1 1 1 1");

            AddAdminNumberKeyButton(container, root, "7", "7", 0.075f, 0.595f);
            AddAdminNumberKeyButton(container, root, "8", "8", 0.255f, 0.595f);
            AddAdminNumberKeyButton(container, root, "9", "9", 0.435f, 0.595f);
            AddAdminNumberKeyButton(container, root, "DEL", "back", 0.615f, 0.595f);

            AddAdminNumberKeyButton(container, root, "4", "4", 0.075f, 0.485f);
            AddAdminNumberKeyButton(container, root, "5", "5", 0.255f, 0.485f);
            AddAdminNumberKeyButton(container, root, "6", "6", 0.435f, 0.485f);
            AddAdminNumberKeyButton(container, root, "CLR", "clear", 0.615f, 0.485f);

            AddAdminNumberKeyButton(container, root, "1", "1", 0.075f, 0.375f);
            AddAdminNumberKeyButton(container, root, "2", "2", 0.255f, 0.375f);
            AddAdminNumberKeyButton(container, root, "3", "3", 0.435f, 0.375f);
            AddAdminNumberKeyButton(container, root, "CUR", "current", 0.615f, 0.375f);

            AddAdminNumberKeyButton(container, root, "0", "0", 0.075f, 0.265f);
            AddAdminNumberKeyButton(container, root, ".", "dot", 0.255f, 0.265f);
            AddAdminNumberKeyButton(container, root, "-", "minus", 0.435f, 0.265f);

            AddUiButton(container, root, "CANCEL", "portableairstrikes.adminui numbercancel", "0.075 0.105", "0.455 0.195", "0.18 0.20 0.23 0.95", 11);
            AddUiButton(container, root, "APPLY", "portableairstrikes.adminui numberapply", "0.545 0.105", "0.925 0.195", "0.40 0.18 0.12 1", 11);
            CuiHelper.AddUi(player, container);
        }

        private void AddAdminNumberKeyButton(CuiElementContainer container, string parent, string text, string token, float x, float y)
        {
            AddUiButton(container, parent, text, "portableairstrikes.adminui numberkey " + token, FormatUiFloat(x) + " " + FormatUiFloat(y), FormatUiFloat(x + 0.145f) + " " + FormatUiFloat(y + 0.080f), "0.14 0.18 0.22 0.95", 11);
        }

        private void AddAdminPanel(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddAdminInput(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, int size, int charsLimit, TextAnchor align)
        {
            var panel = container.Add(new CuiPanel
            {
                Image = { Color = "0.015 0.018 0.024 0.92" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);

            container.Add(new CuiElement
            {
                Name = CuiHelper.GetGuid(),
                Parent = panel,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = CleanAdminUiText(text, charsLimit),
                        FontSize = size,
                        Align = align,
                        Color = "1 1 1 1",
                        Command = command ?? "",
                        CharsLimit = charsLimit,
                        NeedsKeyboard = true,
                        IsPassword = false,
                        LineType = InputField.LineType.SingleLine
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.035 0.08", AnchorMax = "0.965 0.92" }
                }
            });
        }

        private string AddAdminScrollView(CuiElementContainer container, string parent, string anchorMin, string anchorMax, float contentHeight)
        {
            var scrollName = AdminUiName + ".Scroll." + CuiHelper.GetGuid();
            var contentRect = new CuiRectTransformComponent
            {
                AnchorMin = "0 1",
                AnchorMax = "1 1",
                OffsetMin = "0 -" + FormatUiPixels(contentHeight),
                OffsetMax = "0 0"
            };

            container.Add(new CuiElement
            {
                Name = scrollName,
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    new CuiScrollViewComponent
                    {
                        Horizontal = false,
                        Vertical = true,
                        MovementType = ScrollRect.MovementType.Elastic,
                        Elasticity = 0.1f,
                        Inertia = false,
                        DecelerationRate = 0.135f,
                        ScrollSensitivity = 90f,
                        ContentTransform = contentRect,
                        VerticalScrollbar = CreateUiScrollbar()
                    }
                }
            });

            return scrollName;
        }

        private void DestroyAdminUi(BasePlayer player, bool clearNumberEdit = true)
        {
            if (player != null)
            {
                CuiHelper.DestroyUi(player, AdminUiName);
                if (clearNumberEdit)
                {
                    CuiHelper.DestroyUi(player, AdminNumberEditUiName);
                    AdminUiState state;
                    if (adminUiStates.TryGetValue(player.userID, out state))
                    {
                        ClearPendingAdminNumberEdit(state);
                    }
                }
            }
        }

        private int ParseAdminInt(string value, int fallback, int min, int max)
        {
            int parsed;
            if (!int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return Mathf.Clamp(fallback, min, max);
            }

            return Mathf.Clamp(parsed, min, max);
        }

        private float ParseAdminFloat(string value, float fallback, float min, float max)
        {
            float parsed;
            if (!float.TryParse((value ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return Mathf.Clamp(fallback, min, max);
            }

            return Mathf.Clamp(parsed, min, max);
        }

        private string NormalizeCurrencyProvider(string value)
        {
            value = (value ?? "").Trim();
            return string.Equals(value, "Economics", StringComparison.OrdinalIgnoreCase) ? "Economics" : "ServerRewards";
        }

        private string NormalizeTargetTypeName(string value)
        {
            switch (ParseTargetType(value))
            {
                case AirstrikeTargetType.GroundPing:
                    return "ground_ping";
                case AirstrikeTargetType.VehiclePing:
                    return "vehicle_ping";
                case AirstrikeTargetType.PlayerPing:
                    return "player_ping";
                case AirstrikeTargetType.NpcPing:
                    return "npc_ping";
                default:
                    return "";
            }
        }

        private string GetNextTargetTypeName(string current)
        {
            switch (ParseTargetType(current))
            {
                case AirstrikeTargetType.GroundPing:
                    return "vehicle_ping";
                case AirstrikeTargetType.VehiclePing:
                    return "player_ping";
                case AirstrikeTargetType.PlayerPing:
                    return "npc_ping";
                default:
                    return "ground_ping";
            }
        }

        private string NormalizeAdminToken(string value)
        {
            return (value ?? "").Trim().Replace(" ", "_").Replace("-", "_").ToLowerInvariant();
        }

        private string CleanAdminString(string value, int maxLength)
        {
            value = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (maxLength > 0 && value.Length > maxLength)
            {
                value = value.Substring(0, maxLength);
            }

            return value;
        }

        private string CleanAdminUiText(string value, int maxLength)
        {
            value = CleanAdminString(value, maxLength);
            return value.Replace("<", "").Replace(">", "");
        }

        private string ShortenAdminText(string value, int maxLength)
        {
            value = CleanAdminUiText(value ?? "", Math.Max(0, maxLength + 3));
            if (maxLength <= 0 || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string FormatUiFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string BoolText(bool value)
        {
            return value ? "on" : "off";
        }

        private string GetArgTail(ConsoleSystem.Arg arg, int startIndex)
        {
            if (arg == null || arg.Args == null || startIndex < 0 || arg.Args.Length <= startIndex)
            {
                return "";
            }

            var values = new List<string>();
            for (var i = startIndex; i < arg.Args.Length; i++)
            {
                values.Add(arg.GetString(i) ?? "");
            }

            return string.Join(" ", values.ToArray()).Trim();
        }

        private bool ContainsString(List<string> values, string value)
        {
            return IndexOfString(values, value) >= 0;
        }

        private int IndexOfString(List<string> values, string value)
        {
            if (values == null)
            {
                return -1;
            }

            for (var i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private void TryPrepareStrike(BasePlayer player, string strikeId, bool repeatingLast)
        {
            TryStartStrikeCall(player, strikeId, repeatingLast);
        }

        private void TryStartStrikeCall(BasePlayer player, string strikeId, bool repeatingLast)
        {
            var validation = ValidateStrikeCall(player, strikeId);
            if (!validation.Success)
            {
                Reply(player, validation.UserMessage);
                RecordValidationAudit(player, strikeId, validation);
                SaveData();
                return;
            }

            if (activeCalls.ContainsKey(player.userID))
            {
                Reply(player, "You already have an airstrike in progress.");
                return;
            }

            if (activeCalls.Count >= config.General.MaxSimultaneousStrikes)
            {
                Reply(player, "The server already has " + activeCalls.Count + " active airstrike(s). Try again shortly.");
                return;
            }

            IStrikeExecutor executor;
            string executorMessage;
            if (!TryGetExecutor(validation.Strike, out executor, out executorMessage))
            {
                Reply(player, executorMessage);
                return;
            }

            var activeHeavyStrikes = CountActiveHeavyStrikes();
            if (IsHeavyStrike(validation.Strike) && activeHeavyStrikes >= config.General.MaxSimultaneousHeavyStrikes)
            {
                Reply(player, "The server already has " + activeHeavyStrikes + " active heavy airstrike(s). Try again shortly.");
                return;
            }

            var context = new AirstrikeCallContext
            {
                CallId = player.userID.ToString() + ":" + validation.Strike.Id + ":" + GetNow().ToString("0", CultureInfo.InvariantCulture),
                Caller = player,
                CallerUserId = player.userID,
                CallerTeamId = player.currentTeam,
                CallerName = player.displayName ?? player.UserIDString,
                Strike = validation.Strike,
                Target = CopyTarget(validation.Target),
                FinalRPCost = validation.FinalRPCost,
                CreatedAt = GetNow(),
                State = StrikeExecutionState.Validating
            };

            string chargeError;
            if (!ChargeStrike(context, out chargeError))
            {
                Reply(player, chargeError);
                IncrementStat("failed_charge");
                RecordStrikeAudit(context, "charge_failed", chargeError, true);
                SaveData();
                return;
            }

            activeCalls[player.userID] = context;
            context.State = StrikeExecutionState.Charged;

            StartCooldowns(player, validation.Strike);
            IncrementStat("started");

            var suffix = repeatingLast ? " from your last strike" : "";
            var tokenText = context.TokenConsumed ? config.AirstrikeItem.RequiredAmount + " " + GetAirstrikeItemDisplayName() + " consumed" : "no airstrike item consumed";
            var rpText = context.RpCharged ? context.FinalRPCost + " RP charged" : "no RP charged";
            var warningDelay = GetWarningDelaySeconds(validation.Strike);

            context.State = StrikeExecutionState.Warning;
            context.WarningEndsAt = GetNow() + warningDelay;

            var markerCreated = CreateWarningMapMarker(context);
            var markerText = markerCreated ? " Public warning marker active." : "";
            var cancelText = CanPlayerCancelCall(context) ? " Use /" + GetOpenCommand() + " cancel before impact if needed." : "";

            var warningFanout = NotifyStrikeAccepted(context, markerCreated);

            Reply(player, validation.Strike.DisplayName + suffix + " inbound at " + DescribeTarget(context.Target) + ". " + rpText + "; " + tokenText + ". Cooldown started." + markerText + cancelText);

            RecordStrikeAudit(context, "started", "Accepted; warning delay " + FormatSeconds(warningDelay) + "." + markerText + " " + FormatWarningFanoutSummary(warningFanout) + ".", true);
            SaveData();
            ScheduleCallTimer(context, warningDelay, () => DispatchStrike(context, executor));
        }

        private void InitializeExecutors()
        {
            strikeExecutors.Clear();
            strikeProfileBundleExecutor = new StrikeProfileBundleExecutor(this);
            var droneDrop = new DroneDropExecutor(this);
            var heavyDrop = new HeavyDropExecutor(this);
            var rocketRun = new RocketRunExecutor(this);
            var mlrs = new MlrsExecutor(this);
            var homingMissile = new HomingMissileExecutor(this);
            var mortar = new MortarExecutor(this);
            var a10Strafe = new A10StrafeExecutor(this);
            strikeExecutors["drone:bee_grenade"] = droneDrop;
            strikeExecutors["drone:beancan"] = droneDrop;
            strikeExecutors["drone:f1_grenade"] = droneDrop;
            strikeExecutors["drone:smoke"] = droneDrop;
            strikeExecutors["drone:flashbang"] = droneDrop;
            strikeExecutors["drone:molotov"] = droneDrop;
            strikeExecutors["drone:he_40mm"] = droneDrop;
            strikeExecutors["attack_heli:bee_catapult_bomb"] = heavyDrop;
            strikeExecutors["attack_heli:firebomb"] = heavyDrop;
            strikeExecutors["attack_heli:propane_bomb"] = heavyDrop;
            strikeExecutors["cargo_plane_jet:bee_catapult_bomb"] = heavyDrop;
            strikeExecutors["cargo_plane_jet:firebomb"] = heavyDrop;
            strikeExecutors["cargo_plane_jet:propane_bomb"] = heavyDrop;
            strikeExecutors["attack_heli:hv_rocket"] = rocketRun;
            strikeExecutors["attack_heli:rocket"] = rocketRun;
            strikeExecutors["attack_heli:incendiary_rocket"] = rocketRun;
            strikeExecutors["cargo_plane_jet:hv_rocket"] = rocketRun;
            strikeExecutors["cargo_plane_jet:rocket"] = rocketRun;
            strikeExecutors["cargo_plane_jet:incendiary_rocket"] = rocketRun;
            strikeExecutors["cargo_plane_jet:mlrs_rocket"] = mlrs;
            strikeExecutors["attack_heli:homing_missile"] = homingMissile;
            strikeExecutors["cargo_plane_jet:homing_missile"] = homingMissile;
            strikeExecutors["off_map_mortar:mortar_he_payload"] = mortar;
            strikeExecutors["off_map_mortar:mortar_frag_payload"] = mortar;
            strikeExecutors["a10_gun_run:bradley_longbarrel_burst"] = a10Strafe;
        }

        private bool TryGetExecutor(StrikeDefinition strike, out IStrikeExecutor executor, out string message)
        {
            executor = null;
            message = "";

            if (strike == null)
            {
                message = "That strike is not configured correctly.";
                return false;
            }

            if (GetEnabledStrikeProfileAssignments(strike).Count > 0)
            {
                executor = strikeProfileBundleExecutor ?? new StrikeProfileBundleExecutor(this);
                if (executor.CanExecute(strike))
                {
                    return true;
                }

                message = strike.DisplayName + " has strike profiles selected, but none are enabled.";
                return false;
            }

            var key = BuildExecutorKey(strike);
            if (strikeExecutors.TryGetValue(key, out executor) && executor != null && executor.CanExecute(strike))
            {
                return true;
            }

            message = strike.DisplayName + " is configured, but its " + strike.Delivery + "/" + strike.Payload + " executor is not enabled yet. Current live executors support bee_swarm_drone, bee_swarm_heavy, beancan_drop, f1_cluster, smoke_screen, flash_breach, molotov_drop, he_40mm_micro, firebomb_run, propane_bomb_drop, hv_rocket_run, rocket_run, incendiary_rocket_run, mortar_he, mortar_frag, a10_strafe, mini_mlrs, full_mlrs, homing_heli, and homing_jet.";
            return false;
        }

        private string BuildExecutorKey(StrikeDefinition strike)
        {
            var delivery = string.IsNullOrWhiteSpace(strike?.Delivery) ? "" : strike.Delivery.Trim().ToLowerInvariant();
            var payload = string.IsNullOrWhiteSpace(strike?.Payload) ? "" : strike.Payload.Trim().ToLowerInvariant();
            return delivery + ":" + payload;
        }

        private List<StrikeProfileAssignment> GetEnabledStrikeProfileAssignments(StrikeDefinition strike)
        {
            var result = new List<StrikeProfileAssignment>();
            if (strike?.StrikeProfiles == null)
            {
                return result;
            }

            foreach (var assignment in strike.StrikeProfiles)
            {
                if (assignment != null && assignment.Enabled && !string.IsNullOrWhiteSpace(assignment.ProfileId))
                {
                    result.Add(assignment);
                }
            }

            return result;
        }

        private List<StrikeProfileAssignment> GetStrikeProfileAssignments(StrikeDefinition strike)
        {
            return strike?.StrikeProfiles == null
                ? new List<StrikeProfileAssignment>()
                : new List<StrikeProfileAssignment>(strike.StrikeProfiles);
        }

        private void ExecuteStrikeProfileBundle(AirstrikeCallContext context, Action<bool, string> callback)
        {
            if (context == null || context.Strike == null)
            {
                callback(false, "Missing strike profile execution context.");
                return;
            }

            string message;
            var executions = BuildStrikeProfileExecutions(context, out message);
            if (executions.Count == 0)
            {
                callback(false, string.IsNullOrWhiteSpace(message) ? "No enabled strike profiles can run for this target." : message);
                return;
            }

            var pending = executions.Count;
            var completed = 0;
            var finished = false;
            foreach (var execution in executions)
            {
                var scheduled = execution;
                ScheduleCallTimer(context, Math.Max(0f, scheduled.Assignment.StartDelaySeconds), () =>
                {
                    if (!IsCallActive(context) || finished)
                    {
                        return;
                    }

                    try
                    {
                        scheduled.Executor.Execute(scheduled.Context, (success, resultMessage) =>
                        {
                            if (!IsCallActive(context) || finished)
                            {
                                return;
                            }

                            if (!success)
                            {
                                context.DeliveryCarrierDestroyed = context.DeliveryCarrierDestroyed || scheduled.Context.DeliveryCarrierDestroyed;
                                context.FailureForfeitsRefund = context.FailureForfeitsRefund || scheduled.Context.FailureForfeitsRefund;
                                context.ImpactStarted = context.ImpactStarted || scheduled.Context.ImpactStarted;
                                finished = true;
                                callback(false, resultMessage);
                                return;
                            }

                            context.ImpactStarted = context.ImpactStarted || scheduled.Context.ImpactStarted;
                            completed++;
                            pending--;
                            if (pending <= 0)
                            {
                                finished = true;
                                callback(true, completed + " strike profile(s) delivered.");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        if (!finished)
                        {
                            finished = true;
                            callback(false, "Strike profile executor error: " + ex.Message);
                        }
                    }
                });
            }
        }

        private List<StrikeProfileExecution> BuildStrikeProfileExecutions(AirstrikeCallContext parent, out string message)
        {
            message = "";
            var executions = new List<StrikeProfileExecution>();
            var assignments = GetEnabledStrikeProfileAssignments(parent?.Strike);
            if (assignments.Count == 0)
            {
                message = "No enabled strike profiles are selected.";
                return executions;
            }

            foreach (var assignment in assignments)
            {
                VisualProfileConfig profile;
                if (!TryGetVisualProfileById(assignment.ProfileId, out profile))
                {
                    message = "Strike profile '" + assignment.ProfileId + "' is not loaded.";
                    return new List<StrikeProfileExecution>();
                }

                if (ProfileContainsHomingPayload(profile, parent.Strike.Payload) && (parent.Target == null || parent.Target.Type != AirstrikeTargetType.VehiclePing))
                {
                    continue;
                }

                AirstrikeCallContext child;
                string childMessage;
                if (!TryCreateProfileExecutionContext(parent, assignment, profile, out child, out childMessage))
                {
                    message = childMessage;
                    return new List<StrikeProfileExecution>();
                }

                IStrikeExecutor executor;
                string executorMessage;
                if (!TryGetExecutor(child.Strike, out executor, out executorMessage))
                {
                    message = executorMessage;
                    return new List<StrikeProfileExecution>();
                }

                executions.Add(new StrikeProfileExecution
                {
                    Assignment = assignment,
                    Context = child,
                    Executor = executor
                });
            }

            if (executions.Count == 0)
            {
                message = parent == null || parent.Target == null
                    ? "No selected strike profiles are compatible with this target."
                    : "No selected strike profiles are compatible with " + FormatTargetType(parent.Target.Type) + ".";
            }

            return executions;
        }

        private bool TryCreateProfileExecutionContext(AirstrikeCallContext parent, StrikeProfileAssignment assignment, VisualProfileConfig profile, out AirstrikeCallContext child, out string message)
        {
            child = null;
            message = "";
            if (parent == null || parent.Strike == null || assignment == null || profile == null)
            {
                message = "Missing strike profile execution data.";
                return false;
            }

            string payload;
            if (!TryGetProfilePrimaryPayload(profile, parent.Strike.Payload, out payload))
            {
                message = "Strike profile '" + assignment.ProfileId + "' does not define a supported payload.";
                return false;
            }

            var delivery = InferDeliveryForProfilePayload(profile, payload, parent.Strike);
            var count = GetProfileEffectivePayloadUnitCount(profile, payload, GetFallbackPayloadCount(parent.Strike, payload));
            if (assignment.PayloadCountLimit > 0)
            {
                count = count <= 0 ? assignment.PayloadCountLimit : Math.Min(count, assignment.PayloadCountLimit);
            }

            count = Math.Max(1, count);
            var profileStrike = CloneStrikeForProfileExecution(parent.Strike, assignment, delivery, payload, count);
            child = new AirstrikeCallContext
            {
                CallId = parent.CallId + ":" + assignment.ProfileId,
                Caller = parent.Caller,
                CallerUserId = parent.CallerUserId,
                CallerTeamId = parent.CallerTeamId,
                CallerName = parent.CallerName,
                Strike = profileStrike,
                Target = CopyTarget(parent.Target),
                FinalRPCost = parent.FinalRPCost,
                RpCharged = parent.RpCharged,
                TokenConsumed = parent.TokenConsumed,
                CreatedAt = GetNow(),
                State = StrikeExecutionState.Inbound,
                ParentContext = parent
            };

            parent.ChildContexts.Add(child);
            return true;
        }

        private StrikeDefinition CloneStrikeForProfileExecution(StrikeDefinition source, StrikeProfileAssignment assignment, string delivery, string payload, int count)
        {
            var clone = new StrikeDefinition
            {
                Id = source.Id + ":" + assignment.ProfileId,
                Enabled = true,
                DisplayName = source.DisplayName + " / " + assignment.ProfileId,
                TargetType = source.TargetType,
                AcceptedTargetTypes = source.AcceptedTargetTypes == null ? new List<string>() : new List<string>(source.AcceptedTargetTypes),
                Delivery = delivery,
                Payload = payload,
                VisualProfileId = assignment.ProfileId,
                StrikeProfiles = new List<StrikeProfileAssignment>(),
                Tier = source.Tier,
                RPCost = 0,
                PermissionRequired = "",
                WarningDelaySeconds = source.WarningDelaySeconds,
                CooldownPerPlayerSeconds = 0f,
                CooldownPerClanSeconds = 0f,
                GlobalCooldownSeconds = 0f,
                BaseCount = count,
                MaxCount = count,
                SpreadRadius = source.SpreadRadius,
                SpreadMultiplier = source.SpreadMultiplier,
                BurstCount = source.BurstCount,
                LineLength = source.LineLength,
                LineLengthMultiplier = source.LineLengthMultiplier,
                Width = source.Width,
                WidthMultiplier = source.WidthMultiplier,
                ImpactRadius = source.ImpactRadius,
                ImpactRadiusMultiplier = source.ImpactRadiusMultiplier,
                PulseDelaySeconds = source.PulseDelaySeconds,
                PulseDelayMultiplier = source.PulseDelayMultiplier,
                MissileCount = source.MissileCount,
                RocketCount = source.RocketCount,
                MaxTrackingSeconds = source.MaxTrackingSeconds,
                TrackingSecondsMultiplier = source.TrackingSecondsMultiplier,
                MaxTrackingDistance = source.MaxTrackingDistance,
                TrackingDistanceMultiplier = source.TrackingDistanceMultiplier,
                VehicleDamageScale = source.VehicleDamageScale,
                DamageMultiplier = source.DamageMultiplier,
                VehicleDamageMultiplier = source.VehicleDamageMultiplier,
                SplashRadius = source.SplashRadius,
                SplashRadiusMultiplier = source.SplashRadiusMultiplier,
                DamageScales = source.DamageScales == null
                    ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, float>(source.DamageScales, StringComparer.OrdinalIgnoreCase)
            };

            ApplyProfilePayloadCountToStrike(clone, payload, count);
            return clone;
        }

        private void ApplyProfilePayloadCountToStrike(StrikeDefinition strike, string payload, int count)
        {
            if (strike == null)
            {
                return;
            }

            count = Math.Max(1, count);
            strike.BaseCount = count;
            strike.MaxCount = Math.Max(strike.MaxCount, count);

            RocketRunPayloadSpec rocketSpec;
            MlrsPayloadSpec mlrsSpec;
            if (TryGetRocketPayloadSpec(payload, out rocketSpec) || TryGetMlrsPayloadSpec(payload, out mlrsSpec))
            {
                strike.RocketCount = count;
            }

            HomingMissileSpec homingSpec;
            if (TryGetHomingMissileSpec(payload, out homingSpec))
            {
                strike.MissileCount = count;
            }

            A10StrafeSpec a10Spec;
            if (TryGetA10StrafeSpec(payload, out a10Spec))
            {
                strike.BurstCount = count;
            }
        }

        private bool TryGetProfilePrimaryPayload(VisualProfileConfig profile, string fallbackPayload, out string payload)
        {
            payload = "";
            if (profile == null)
            {
                return false;
            }

            if (string.Equals(profile.PayloadReleaseMode, "generated", StringComparison.OrdinalIgnoreCase))
            {
                payload = GetReleasePayload(profile.ReleaseTemplate, fallbackPayload);
                return IsSupportedVisualPayload(payload);
            }

            if (profile.CompiledReleaseEvents != null && profile.CompiledReleaseEvents.Count > 0)
            {
                foreach (var payloadEvent in profile.CompiledReleaseEvents)
                {
                    payload = GetReleasePayload(payloadEvent, fallbackPayload);
                    if (IsSupportedVisualPayload(payload))
                    {
                        return true;
                    }
                }
            }

            if (profile.PayloadEvents != null && profile.PayloadEvents.Count > 0)
            {
                foreach (var payloadEvent in profile.PayloadEvents)
                {
                    payload = GetReleasePayload(payloadEvent, fallbackPayload);
                    if (IsSupportedVisualPayload(payload))
                    {
                        return true;
                    }
                }
            }

            payload = NormalizePayloadId(fallbackPayload);
            return IsSupportedVisualPayload(payload);
        }

        private int GetProfileEffectivePayloadUnitCount(VisualProfileConfig profile, string fallbackPayload, int fallbackCount)
        {
            if (profile == null)
            {
                return Math.Max(1, fallbackCount);
            }

            if (profile.CompiledReleaseEvents != null && profile.CompiledReleaseEvents.Count > 0)
            {
                return profile.CompiledReleaseEvents.Count;
            }

            if (string.Equals(profile.PayloadReleaseMode, "generated", StringComparison.OrdinalIgnoreCase))
            {
                if (profile.MaxPayloadCount > 0)
                {
                    return profile.MaxPayloadCount;
                }

                return Math.Max(1, profile.ReleaseTemplate == null ? fallbackCount : profile.ReleaseTemplate.Count);
            }

            var total = 0;
            if (profile.PayloadEvents != null)
            {
                foreach (var payloadEvent in profile.PayloadEvents)
                {
                    if (payloadEvent == null)
                    {
                        continue;
                    }

                    var payload = GetReleasePayload(payloadEvent, fallbackPayload);
                    if (IsSupportedVisualPayload(payload))
                    {
                        total += Math.Max(1, payloadEvent.Count);
                    }
                }
            }

            return total > 0 ? total : Math.Max(1, fallbackCount);
        }

        private int GetFallbackPayloadCount(StrikeDefinition strike, string payload)
        {
            if (strike == null)
            {
                return 1;
            }

            MlrsPayloadSpec mlrsSpec;
            if (TryGetMlrsPayloadSpec(payload, out mlrsSpec))
            {
                return CalculateMlrsRocketCount(strike);
            }

            HomingMissileSpec homingSpec;
            if (TryGetHomingMissileSpec(payload, out homingSpec))
            {
                return CalculateHomingMissileCount(strike);
            }

            A10StrafeSpec a10Spec;
            if (TryGetA10StrafeSpec(payload, out a10Spec))
            {
                return CalculateA10BurstCount(strike);
            }

            RocketRunPayloadSpec rocketSpec;
            if (TryGetRocketPayloadSpec(payload, out rocketSpec))
            {
                return CalculateRocketCount(strike);
            }

            return CalculatePayloadCount(strike);
        }

        private string InferDeliveryForProfilePayload(VisualProfileConfig profile, string payload, StrikeDefinition fallback)
        {
            var vehicle = NormalizeVisualProfileVehicle(profile == null ? "" : profile.Vehicle, fallback, null, GetDeliveryVisualProfileForStrike(fallback));

            MortarPayloadSpec mortarSpec;
            if (TryGetMortarPayloadSpec(payload, out mortarSpec))
            {
                return "off_map_mortar";
            }

            A10StrafeSpec a10Spec;
            if (TryGetA10StrafeSpec(payload, out a10Spec))
            {
                return "a10_gun_run";
            }

            DronePayloadSpec droneSpec;
            if (TryGetDronePayloadSpec(payload, out droneSpec))
            {
                return "drone";
            }

            HeavyDropPayloadSpec heavySpec;
            if (TryGetHeavyDropPayloadSpec(payload, out heavySpec))
            {
                return string.Equals(vehicle, "attack_heli", StringComparison.OrdinalIgnoreCase) ? "attack_heli" : "cargo_plane_jet";
            }

            RocketRunPayloadSpec rocketSpec;
            if (TryGetRocketPayloadSpec(payload, out rocketSpec))
            {
                return string.Equals(vehicle, "attack_heli", StringComparison.OrdinalIgnoreCase) ? "attack_heli" : "cargo_plane_jet";
            }

            HomingMissileSpec homingSpec;
            if (TryGetHomingMissileSpec(payload, out homingSpec))
            {
                return string.Equals(vehicle, "attack_heli", StringComparison.OrdinalIgnoreCase) ? "attack_heli" : "cargo_plane_jet";
            }

            MlrsPayloadSpec mlrsSpec;
            if (TryGetMlrsPayloadSpec(payload, out mlrsSpec))
            {
                return "cargo_plane_jet";
            }

            return string.IsNullOrWhiteSpace(fallback?.Delivery) ? "drone" : fallback.Delivery;
        }

        private bool TryGetVisualProfileById(string profileId, out VisualProfileConfig profile)
        {
            profile = null;
            profileId = (profileId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return false;
            }

            if ((visualProfileFile == null || visualProfileFile.Profiles == null || visualProfileFile.Profiles.Count == 0) && File.Exists(ResolveVisualProfilesDataPath()))
            {
                LoadVisualProfiles();
            }

            return visualProfileFile?.Profiles != null && visualProfileFile.Profiles.TryGetValue(profileId, out profile) && profile != null;
        }

        private bool ProfileContainsHomingPayload(VisualProfileConfig profile, string fallbackPayload)
        {
            HomingMissileSpec homingSpec;
            if (profile == null)
            {
                return TryGetHomingMissileSpec(fallbackPayload, out homingSpec);
            }

            if (string.Equals(profile.PayloadReleaseMode, "generated", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetHomingMissileSpec(GetReleasePayload(profile.ReleaseTemplate, fallbackPayload), out homingSpec);
            }

            if (profile.CompiledReleaseEvents != null)
            {
                foreach (var payloadEvent in profile.CompiledReleaseEvents)
                {
                    if (TryGetHomingMissileSpec(GetReleasePayload(payloadEvent, fallbackPayload), out homingSpec))
                    {
                        return true;
                    }
                }
            }

            if (profile.PayloadEvents != null)
            {
                foreach (var payloadEvent in profile.PayloadEvents)
                {
                    if (TryGetHomingMissileSpec(GetReleasePayload(payloadEvent, fallbackPayload), out homingSpec))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private AirstrikeTarget CopyTarget(AirstrikeTarget target)
        {
            if (target == null)
            {
                return null;
            }

            return new AirstrikeTarget
            {
                Type = target.Type,
                Position = target.Position,
                EntityId = target.EntityId,
                EntityShortPrefabName = target.EntityShortPrefabName,
                CreatedAt = target.CreatedAt,
                Source = target.Source
            };
        }

        private bool ChargeStrike(AirstrikeCallContext context, out string error)
        {
            error = "";
            var player = GetCallPlayer(context);
            if (player == null)
            {
                error = "Could not find the calling player.";
                return false;
            }

            if (context.FinalRPCost > 0 && config.Currency.Enabled)
            {
                if (currencyAdapter == null || !currencyAdapter.IsAvailable())
                {
                    error = "Currency provider '" + config.Currency.Provider + "' is not available.";
                    return false;
                }

                string withdrawError;
                if (!currencyAdapter.Withdraw(player, context.FinalRPCost, out withdrawError))
                {
                    error = "Could not charge RP: " + withdrawError;
                    return false;
                }

                context.RpCharged = true;
            }

            if (ShouldConsumeAirstrikeToken(player))
            {
                if (!ConsumeAirstrikeTokens(player, config.AirstrikeItem.RequiredAmount))
                {
                    RefundCurrencyIfNeeded(context, new List<string>());
                    error = "Could not consume " + config.AirstrikeItem.RequiredAmount + " " + GetAirstrikeItemDisplayName() + " item(s). No RP was kept.";
                    return false;
                }

                context.TokenConsumed = true;
            }

            return true;
        }

        private bool ShouldConsumeAirstrikeToken(BasePlayer player)
        {
            if (!config.AirstrikeItem.Enabled || !config.AirstrikeItem.ConsumeOnSuccessfulCall)
            {
                return false;
            }

            return !(IsAdmin(player) && config.AirstrikeItem.AllowAdminsWithoutItem);
        }

        private void DispatchStrike(AirstrikeCallContext context, IStrikeExecutor executor)
        {
            if (!IsCallActive(context))
            {
                return;
            }

            var player = GetCallPlayer(context);
            if (player == null || !player.IsConnected)
            {
                FailStrikeCall(context, "Caller left before the airstrike could launch.", true);
                return;
            }

            context.Caller = player;
            context.State = StrikeExecutionState.Inbound;
            NotifyCaller(context, context.Strike.DisplayName + " inbound. Payloads are arriving now.");

            try
            {
                executor.Execute(context, (success, message) =>
                {
                    if (!IsCallActive(context))
                    {
                        return;
                    }

                    if (success)
                    {
                        CompleteStrikeCall(context, message);
                    }
                    else
                    {
                        FailStrikeCall(context, message, !context.FailureForfeitsRefund);
                    }
                });
            }
            catch (Exception ex)
            {
                FailStrikeCall(context, "Executor error: " + ex.Message, true);
            }
        }

        private void CompleteStrikeCall(AirstrikeCallContext context, string message)
        {
            if (!IsCallActive(context))
            {
                return;
            }

            context.State = StrikeExecutionState.Complete;
            DestroyContextTimers(context);
            DestroyWarningMapMarker(context);
            CleanupContextVisuals(context);
            CleanupContextEntities(context, false);
            activeCalls.Remove(context.CallerUserId);

            storedData.LastStrikeByUser[context.CallerUserId.ToString()] = context.Strike.Id;
            IncrementStat("completed");
            IncrementStat("completed_" + context.Strike.Id);
            RecordStrikeAudit(context, "completed", string.IsNullOrWhiteSpace(message) ? context.Strike.DisplayName + " complete." : message, true);
            SaveData();

            NotifyCaller(context, string.IsNullOrWhiteSpace(message) ? context.Strike.DisplayName + " complete." : message);
        }

        private void FailStrikeCall(AirstrikeCallContext context, string message, bool allowRefund)
        {
            if (!IsCallActive(context))
            {
                return;
            }

            context.State = StrikeExecutionState.Failed;
            DestroyContextTimers(context);
            DestroyWarningMapMarker(context);
            CleanupContextVisuals(context);
            CleanupContextEntities(context, !context.ImpactStarted);
            activeCalls.Remove(context.CallerUserId);

            var notes = new List<string>();
            if (context.FailureForfeitsRefund && !context.ImpactStarted)
            {
                notes.Add("Costs and cooldowns were kept because the delivery vehicle was destroyed before payload release.");
            }
            else if (allowRefund && !context.ImpactStarted)
            {
                RefundCallCosts(context, notes);
                ClearCooldownsForContext(context);
            }

            IncrementStat(context.DeliveryCarrierDestroyed ? "intercepted_delivery_vehicle" : "failed_execution");
            IncrementStat("failed_" + context.Strike.Id);

            var suffix = notes.Count == 0 ? "" : " " + string.Join(" ", notes.ToArray());
            var result = context.DeliveryCarrierDestroyed
                ? (context.RefundAttempted ? "intercepted_refunded" : "intercepted")
                : context.RefundAttempted ? "failed_refunded" : "failed";
            RecordStrikeAudit(context, result, message + suffix, true);
            SaveData();
            NotifyCaller(context, context.Strike.DisplayName + " failed: " + message + suffix);
            PrintWarning(context.Strike.Id + " failed for " + context.CallerName + ": " + message);
        }

        private void CancelStrikeCall(AirstrikeCallContext context, string message, bool byPlayer)
        {
            if (!IsCallActive(context))
            {
                return;
            }

            context.State = StrikeExecutionState.Cancelled;
            DestroyContextTimers(context);
            DestroyWarningMapMarker(context);
            CleanupContextVisuals(context);
            CleanupContextEntities(context, true);
            activeCalls.Remove(context.CallerUserId);

            var notes = new List<string>();
            if (!context.ImpactStarted && config.General.RefundPlayerCancelledCallsBeforeImpact)
            {
                RefundCallCosts(context, notes);
                ClearCooldownsForContext(context);
            }
            else if (!context.ImpactStarted)
            {
                notes.Add("Costs and cooldowns were kept by config.");
            }

            IncrementStat(byPlayer ? "cancelled_player" : "cancelled");
            IncrementStat("cancelled_" + context.Strike.Id);

            var suffix = notes.Count == 0 ? "" : " " + string.Join(" ", notes.ToArray());
            var result = context.RefundAttempted ? "cancelled_refunded" : "cancelled";
            RecordStrikeAudit(context, result, message + suffix, true);
            SaveData();
            NotifyCaller(context, context.Strike.DisplayName + " cancelled. " + message + suffix);
        }

        private void RefundCallCosts(AirstrikeCallContext context, List<string> notes)
        {
            if (context == null || context.RefundAttempted)
            {
                return;
            }

            context.RefundAttempted = true;
            RefundCurrencyIfNeeded(context, notes);
            RestoreTokenIfNeeded(context, notes);
        }

        private void RefundCurrencyIfNeeded(AirstrikeCallContext context, List<string> notes)
        {
            if (context == null || !context.RpCharged || context.FinalRPCost <= 0 || !config.Currency.Enabled)
            {
                return;
            }

            var player = GetCallPlayer(context);
            var refundError = "currency unavailable";
            if (player != null && currencyAdapter != null && currencyAdapter.Deposit(player, context.FinalRPCost, out refundError))
            {
                notes.Add(context.FinalRPCost + " RP refunded.");
                context.RpCharged = false;
                context.State = StrikeExecutionState.Refunded;
                return;
            }

            notes.Add("RP refund failed; contact an admin.");
            PrintWarning("Could not refund " + context.FinalRPCost + " RP for " + context.CallerName + ": " + (refundError ?? "currency unavailable"));
        }

        private void RestoreTokenIfNeeded(AirstrikeCallContext context, List<string> notes)
        {
            if (context == null || !context.TokenConsumed)
            {
                return;
            }

            var player = GetCallPlayer(context);
            if (player == null)
            {
                notes.Add("Airstrike item restore skipped because the player is offline.");
                return;
            }

            var result = GiveAirstrikeTokensDetailed(player, config.AirstrikeItem.RequiredAmount);
            if (result.Given > 0)
            {
                context.TokenConsumed = false;
                var dropped = result.Dropped > 0 ? " Some restored physical item(s) dropped at your feet." : "";
                notes.Add("Airstrike item restored." + dropped);
                return;
            }

            notes.Add("Airstrike item restore failed; contact an admin.");
        }

        private void ClearCooldownsForContext(AirstrikeCallContext context)
        {
            if (context == null || context.Strike == null)
            {
                return;
            }

            var userId = context.CallerUserId.ToString();
            storedData.PlayerCooldownUntil.Remove(userId + ":" + context.Strike.Id);

            if (context.CallerTeamId != 0UL)
            {
                storedData.ClanCooldownUntil.Remove(context.CallerTeamId.ToString() + ":" + context.Strike.Id);
            }

            storedData.GlobalCooldownUntil.Remove(context.Strike.Id);
        }

        private Timer ScheduleCallTimer(AirstrikeCallContext context, float delay, Action callback)
        {
            var owner = GetRootContext(context);
            Timer scheduled = null;
            scheduled = timer.Once(Math.Max(0.01f, delay), () =>
            {
                activeTimers.Remove(scheduled);
                if (owner != null)
                {
                    owner.Timers.Remove(scheduled);
                }

                callback();
            });

            if (scheduled != null)
            {
                activeTimers.Add(scheduled);
                if (owner != null)
                {
                    owner.Timers.Add(scheduled);
                }
            }

            return scheduled;
        }

        private bool IsCallActive(AirstrikeCallContext context)
        {
            if (context == null)
            {
                return false;
            }

            var root = GetRootContext(context);
            AirstrikeCallContext active;
            return root != null && activeCalls.TryGetValue(root.CallerUserId, out active) && ReferenceEquals(active, root);
        }

        private void MarkImpactStarted(AirstrikeCallContext context)
        {
            if (context == null)
            {
                return;
            }

            context.ImpactStarted = true;
            var root = GetRootContext(context);
            if (root != null)
            {
                root.ImpactStarted = true;
            }
        }

        private AirstrikeCallContext GetRootContext(AirstrikeCallContext context)
        {
            var current = context;
            var guard = 0;
            while (current != null && current.ParentContext != null && guard++ < 16)
            {
                current = current.ParentContext;
            }

            return current;
        }

        private IEnumerable<AirstrikeCallContext> EnumerateExecutionContexts(AirstrikeCallContext root)
        {
            if (root == null)
            {
                yield break;
            }

            yield return root;
            foreach (var child in root.ChildContexts)
            {
                if (child != null)
                {
                    yield return child;
                }
            }
        }

        private int CountActiveHeavyStrikes()
        {
            var count = 0;
            foreach (var context in activeCalls.Values)
            {
                if (context != null && IsHeavyStrike(context.Strike))
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsHeavyStrike(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return false;
            }

            if (strike.Tier >= 3)
            {
                return true;
            }

            return string.Equals(strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase)
                || string.Equals(strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(strike.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase);
        }

        private bool CanPlayerCancelCall(AirstrikeCallContext context)
        {
            return config?.General != null
                && config.General.AllowPlayerCancelBeforeImpact
                && context != null
                && IsCallActive(context)
                && !context.ImpactStarted
                && (context.State == StrikeExecutionState.Charged
                    || context.State == StrikeExecutionState.Warning
                    || context.State == StrikeExecutionState.Inbound);
        }

        private bool CreateWarningMapMarker(AirstrikeCallContext context)
        {
            if (context == null
                || context.Target == null
                || context.WarningMapMarker != null
                || !config.General.UseMapMarkersForHeavyStrikes
                || !IsHeavyStrike(context.Strike))
            {
                return false;
            }

            BaseEntity entity = null;
            try
            {
                var position = ResolveImpactPosition(context.Target.Position) + Vector3.up * 0.5f;
                entity = GameManager.server.CreateEntity(GenericRadiusMapMarkerPrefab, position, Quaternion.identity, true);
                var marker = entity as MapMarkerGenericRadius;
                if (marker == null)
                {
                    if (entity != null && !entity.IsDestroyed)
                    {
                        entity.Kill(BaseNetworkable.DestroyMode.None);
                    }

                    PrintWarning("Could not create airstrike warning map marker from prefab '" + GenericRadiusMapMarkerPrefab + "'.");
                    return false;
                }

                var alpha = Mathf.Clamp01(config.General.HeavyStrikeMapMarkerAlpha);
                var red = new Color(1f, 0.18f, 0.04f, alpha);
                var amber = new Color(1f, 0.72f, 0.08f, alpha);

                marker.enableSaving = false;
                marker.globalBroadcast = true;
                marker.radius = HeavyStrikeMapMarkerNativeRadius();
                marker.alpha = alpha;
                marker.color1 = red;
                marker.color2 = amber;
                marker.Spawn();
                marker.SendUpdate();
                marker.SendNetworkUpdateImmediate();

                context.WarningMapMarker = marker;
                return true;
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                PrintWarning("Could not create airstrike warning map marker: " + ex.Message);
                return false;
            }
        }

        private void DestroyWarningMapMarker(AirstrikeCallContext context)
        {
            if (context == null)
            {
                return;
            }

            var marker = context.WarningMapMarker;
            context.WarningMapMarker = null;
            if (marker == null || marker.IsDestroyed)
            {
                return;
            }

            try
            {
                marker.Kill(BaseNetworkable.DestroyMode.None);
            }
            catch (Exception ex)
            {
                PrintWarning("Could not remove airstrike warning map marker: " + ex.Message);
            }
        }

        private bool IsWarningMapMarkerActive(AirstrikeCallContext context)
        {
            return context != null && context.WarningMapMarker != null && !context.WarningMapMarker.IsDestroyed;
        }

        private WarningFanoutResult NotifyStrikeAccepted(AirstrikeCallContext context, bool markerCreated)
        {
            if (context == null || context.Strike == null || context.Target == null)
            {
                return null;
            }

            var result = BuildWarningFanoutPreview(context, markerCreated);
            var warningDelay = GetWarningDelaySeconds(context.Strike);
            var teamMessage = BuildTeamWarningMessage(context, warningDelay, markerCreated);
            var nearbyMessage = BuildNearbyWarningMessage(context, warningDelay, markerCreated);

            foreach (var recipient in result.Recipients)
            {
                if (recipient?.Player == null || !recipient.Player.IsConnected)
                {
                    continue;
                }

                Reply(recipient.Player, string.Equals(recipient.Source, "team", StringComparison.OrdinalIgnoreCase) ? teamMessage : nearbyMessage);
            }

            IncrementStat("warning_fanout_calls");
            IncrementStatBy("warning_team_recipients", result.TeamRecipients);
            IncrementStatBy("warning_nearby_recipients", result.NearbyRecipients);
            if (result.TotalRecipients == 0)
            {
                IncrementStat("warning_no_recipients");
            }

            Puts("Warning fanout: call=" + context.CallId
                + " strike=" + context.Strike.Id
                + " caller=" + context.CallerName + "(" + context.CallerUserId + ")"
                + " teamRecipients=" + result.TeamRecipients
                + " nearbyRecipients=" + result.NearbyRecipients
                + " totalRecipients=" + result.TotalRecipients
                + " teamEnabled=" + result.TeamEnabled
                + " nearbyEnabled=" + result.NearbyEnabled
                + " nearbyEligible=" + result.NearbyEligible
                + " radius=" + FormatMeters(result.NearbyRadius)
                + " marker=" + markerCreated + ".");

            return result;
        }

        private WarningFanoutResult BuildWarningFanoutPreview(AirstrikeCallContext context, bool markerCreated)
        {
            var result = new WarningFanoutResult
            {
                TeamEnabled = config.General.NotifyCallerTeamOnAcceptedStrike,
                NearbyEnabled = config.General.NotifyNearbyPlayersOnHeavyStrikes,
                NearbyRadius = config.General.NearbyHeavyStrikeWarningRadius,
                IsHeavyStrike = IsHeavyStrike(context?.Strike),
                MarkerCreated = markerCreated
            };

            result.NearbyEligible = result.NearbyEnabled && result.IsHeavyStrike;

            if (context == null || context.Target == null)
            {
                return result;
            }

            var notified = new HashSet<ulong>();
            if (context.CallerUserId != 0UL)
            {
                notified.Add(context.CallerUserId);
            }

            AddCallerTeamWarningRecipients(context, result, notified);
            AddNearbyWarningRecipients(context, result, notified);
            return result;
        }

        private void AddCallerTeamWarningRecipients(AirstrikeCallContext context, WarningFanoutResult result, HashSet<ulong> notified)
        {
            if (context == null || result == null || notified == null || !result.TeamEnabled || context.CallerTeamId == 0UL)
            {
                return;
            }

            var team = RelationshipManager.ServerInstance?.FindPlayersTeam(context.CallerTeamId);
            if (team == null || team.members == null)
            {
                return;
            }

            foreach (var memberId in team.members)
            {
                result.TeamMemberCount++;
                if (memberId == 0UL || notified.Contains(memberId))
                {
                    result.TeamOfflineOrSkipped++;
                    continue;
                }

                var member = BasePlayer.FindByID(memberId);
                if (member == null || !member.IsConnected)
                {
                    result.TeamOfflineOrSkipped++;
                    continue;
                }

                notified.Add(memberId);
                result.TeamRecipients++;
                result.Recipients.Add(new WarningRecipient
                {
                    Player = member,
                    Source = "team",
                    Distance = context.Target == null ? 0f : Vector3.Distance(member.transform.position, context.Target.Position)
                });
            }
        }

        private void AddNearbyWarningRecipients(AirstrikeCallContext context, WarningFanoutResult result, HashSet<ulong> notified)
        {
            if (context == null || result == null || notified == null || !result.NearbyEligible || result.NearbyRadius <= 0f || BasePlayer.activePlayerList == null)
            {
                return;
            }

            foreach (var targetPlayer in BasePlayer.activePlayerList)
            {
                if (targetPlayer == null || !targetPlayer.IsConnected)
                {
                    continue;
                }

                var distance = Vector3.Distance(targetPlayer.transform.position, context.Target.Position);
                if (distance > result.NearbyRadius)
                {
                    continue;
                }

                result.NearbyCandidates++;
                if (notified.Contains(targetPlayer.userID))
                {
                    result.NearbySkippedDeduped++;
                    continue;
                }

                notified.Add(targetPlayer.userID);
                result.NearbyRecipients++;
                result.Recipients.Add(new WarningRecipient
                {
                    Player = targetPlayer,
                    Source = "nearby",
                    Distance = distance
                });
            }
        }

        private string BuildTeamWarningMessage(AirstrikeCallContext context, float warningDelay, bool markerCreated)
        {
            var markerText = markerCreated ? " Public map marker active." : "";
            return "Team airstrike: " + context.CallerName + " called " + context.Strike.DisplayName + " at " + FormatPosition(context.Target.Position) + ". Impact in " + FormatSeconds(warningDelay) + "." + markerText;
        }

        private string BuildNearbyWarningMessage(AirstrikeCallContext context, float warningDelay, bool markerCreated)
        {
            var markerText = markerCreated ? " Check your map for the warning marker." : "";
            return "Warning: " + context.Strike.DisplayName + " inbound nearby. Impact in " + FormatSeconds(warningDelay) + "." + markerText;
        }

        private string FormatWarningFanoutSummary(WarningFanoutResult result)
        {
            if (result == null)
            {
                return "warning fanout unavailable";
            }

            var teamMode = result.TeamEnabled ? "team on" : "team off";
            var nearbyMode = !result.IsHeavyStrike
                ? "nearby skipped non-heavy"
                : result.NearbyEnabled ? "nearby on radius " + FormatMeters(result.NearbyRadius) : "nearby off";
            return "warnings total=" + result.TotalRecipients + " (team=" + result.TeamRecipients + ", nearby=" + result.NearbyRecipients + "; " + teamMode + ", " + nearbyMode + ")";
        }

        private float HeavyStrikeMapMarkerNativeRadius()
        {
            var configuredSize = config?.General == null ? 18f : config.General.HeavyStrikeMapMarkerSize;
            var size = Mathf.Clamp(configuredSize <= 0f ? 18f : configuredSize, 2f, 75f);
            return Mathf.Clamp(
                NativeStrikeMapMarkerBaseRadius + size * NativeStrikeMapMarkerRadiusPerConfiguredMeter,
                MinimumNativeStrikeMapMarkerRadius,
                MaximumNativeStrikeMapMarkerRadius);
        }

        private void DestroyContextTimers(AirstrikeCallContext context)
        {
            context = GetRootContext(context);
            if (context == null || context.Timers.Count == 0)
            {
                return;
            }

            var timers = new List<Timer>(context.Timers);
            context.Timers.Clear();
            foreach (var callTimer in timers)
            {
                activeTimers.Remove(callTimer);
                callTimer?.Destroy();
            }
        }

        private void CleanupContextEntities(AirstrikeCallContext context, bool kill)
        {
            if (context == null)
            {
                return;
            }

            foreach (var child in new List<AirstrikeCallContext>(context.ChildContexts))
            {
                CleanupContextEntities(child, kill);
            }

            if (context.SpawnedEntities.Count == 0)
            {
                return;
            }

            var entities = new List<BaseEntity>(context.SpawnedEntities);
            context.SpawnedEntities.Clear();
            if (!kill)
            {
                return;
            }

            foreach (var entity in entities)
            {
                RemovePayloadReleaseMetadata(entity);
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }
            }
        }

        private void CleanupContextVisuals(AirstrikeCallContext context)
        {
            if (context == null)
            {
                return;
            }

            foreach (var child in new List<AirstrikeCallContext>(context.ChildContexts))
            {
                CleanupContextVisuals(child);
            }

            if (context.VisualEntities.Count == 0)
            {
                ClearDeliveryCarrier(context);
                return;
            }

            ClearDeliveryCarrier(context);
            var entities = new List<BaseEntity>(context.VisualEntities);
            context.VisualEntities.Clear();
            foreach (var entity in entities)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }
            }
        }

        private void CancelActiveCallsForUnload()
        {
            var contexts = new List<AirstrikeCallContext>(activeCalls.Values);
            foreach (var context in contexts)
            {
                DestroyContextTimers(context);
                DestroyWarningMapMarker(context);
                CleanupContextVisuals(context);
                CleanupContextEntities(context, true);

                if (!context.ImpactStarted)
                {
                    var notes = new List<string>();
                    RefundCallCosts(context, notes);
                    ClearCooldownsForContext(context);
                }

                IncrementStat("cancelled_unload");
                var suffix = context.RefundAttempted ? " Costs were refunded or restore was attempted." : "";
                RecordStrikeAudit(context, context.RefundAttempted ? "cancelled_refunded" : "cancelled", "Plugin unloaded before the airstrike completed." + suffix, true);
            }

            activeCalls.Clear();

            var timers = new List<Timer>(activeTimers);
            activeTimers.Clear();
            foreach (var callTimer in timers)
            {
                callTimer?.Destroy();
            }
        }

        private BasePlayer GetCallPlayer(AirstrikeCallContext context)
        {
            if (context == null)
            {
                return null;
            }

            if (context.Caller != null)
            {
                return context.Caller;
            }

            return BasePlayer.FindAwakeOrSleeping(context.CallerUserId.ToString());
        }

        private void NotifyCaller(AirstrikeCallContext context, string message)
        {
            var player = GetCallPlayer(context);
            if (player != null && player.IsConnected)
            {
                Reply(player, message);
            }
        }

        private int CalculatePayloadCount(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return 0;
            }

            var baseCount = Math.Max(1, strike.BaseCount);
            var maxCount = Math.Max(1, strike.MaxCount);
            var multiplier = GetDeliveryMultiplier(strike.Delivery);
            return Mathf.Clamp(baseCount * multiplier, 1, maxCount);
        }

        private int CalculateRocketCount(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return 0;
            }

            var configuredCount = strike.RocketCount > 0 ? strike.RocketCount : strike.BaseCount;
            var maxCount = Math.Max(1, strike.MaxCount);
            return Mathf.Clamp(Math.Max(1, configuredCount), 1, maxCount);
        }

        private int CalculateMlrsRocketCount(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return 0;
            }

            var configuredCount = strike.RocketCount > 0 ? strike.RocketCount : strike.BaseCount;
            var maxCount = Mathf.Clamp(Math.Max(1, strike.MaxCount), 1, 24);
            return Mathf.Clamp(Math.Max(1, configuredCount), 1, maxCount);
        }

        private int CalculateHomingMissileCount(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return 0;
            }

            var configuredCount = strike.MissileCount > 0 ? strike.MissileCount : strike.BaseCount;
            var maxCount = Mathf.Clamp(Math.Max(1, strike.MaxCount), 1, HomingMissileHardCap);
            return Mathf.Clamp(Math.Max(1, configuredCount), 1, maxCount);
        }

        private int CalculateA10BurstCount(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return 0;
            }

            var configuredCount = strike.BurstCount > 0 ? strike.BurstCount : 18;
            return Mathf.Clamp(configuredCount, 1, 80);
        }

        private float GetA10PulseDelaySeconds(StrikeDefinition strike)
        {
            var configured = strike == null ? 0f : strike.PulseDelaySeconds;
            var delay = configured > 0f ? Mathf.Clamp(configured, 0.02f, 2f) : 0.06f;
            return Mathf.Clamp(delay * NormalizePositiveMultiplier(strike == null ? 1f : strike.PulseDelayMultiplier), 0.01f, 10f);
        }

        private int GetDeliveryMultiplier(string delivery)
        {
            if (string.Equals(delivery, "attack_heli", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(1, config.DeliveryScaling.HeliMultiplier);
            }

            if (string.Equals(delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(1, config.DeliveryScaling.PlaneMultiplier);
            }

            return Math.Max(1, config.DeliveryScaling.DroneMultiplier);
        }

        private Vector3 GetRocketApproachDirection(AirstrikeCallContext context)
        {
            var direction = Vector3.zero;
            var player = GetCallPlayer(context);
            if (player != null && context != null && context.Target != null)
            {
                direction = context.Target.Position - player.transform.position;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude > 0.01f)
            {
                return direction.normalized;
            }

            var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        private Vector3 GetRocketVolleyImpactPosition(AirstrikeCallContext context, Vector3 approach, int rocketIndex, int totalRockets)
        {
            var center = context.Target.Position;
            var right = new Vector3(-approach.z, 0f, approach.x);
            if (right.sqrMagnitude <= 0.01f)
            {
                right = Vector3.right;
            }
            else
            {
                right.Normalize();
            }

            var spread = Mathf.Clamp(GetStrikeSpreadRadius(context.Strike), 0f, 100f);
            var linePosition = totalRockets <= 1 ? 0f : (((rocketIndex - 1f) / (totalRockets - 1f)) - 0.5f) * 2f;
            var lateralJitter = UnityEngine.Random.Range(-Mathf.Min(1.5f, spread * 0.15f), Mathf.Min(1.5f, spread * 0.15f));
            var forwardJitter = UnityEngine.Random.Range(-spread * 0.25f, spread * 0.25f);

            return center + right * ((linePosition * spread) + lateralJitter) + approach * forwardJitter;
        }

        private Vector3 GetA10StrafeDirection(AirstrikeCallContext context)
        {
            var direction = Vector3.zero;
            var player = GetCallPlayer(context);
            if (player != null && context != null && context.Target != null)
            {
                direction = context.Target.Position - player.transform.position;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude > 0.01f)
            {
                return direction.normalized;
            }

            var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        private Vector3 GetA10PulsePosition(AirstrikeCallContext context, Vector3 direction, int pulseIndex, int totalPulses)
        {
            var center = context.Target.Position;
            var right = new Vector3(-direction.z, 0f, direction.x);
            if (right.sqrMagnitude <= 0.01f)
            {
                right = Vector3.right;
            }
            else
            {
                right.Normalize();
            }

            var lineLength = Mathf.Clamp((context.Strike.LineLength <= 0f ? 55f : context.Strike.LineLength) * NormalizePositiveMultiplier(context.Strike.LineLengthMultiplier), 5f, 400f);
            var width = Mathf.Clamp((context.Strike.Width <= 0f ? 7f : context.Strike.Width) * NormalizePositiveMultiplier(context.Strike.WidthMultiplier), 0f, 100f);
            var linePosition = totalPulses <= 1 ? 0f : (((pulseIndex - 1f) / (totalPulses - 1f)) - 0.5f) * lineLength;
            var lateral = width <= 0.01f ? 0f : UnityEngine.Random.Range(width * -0.5f, width * 0.5f);

            return center + (direction * linePosition) + (right * lateral);
        }

        private void SetExpectedPayloadReleaseCount(AirstrikeCallContext context, int expectedCount)
        {
            if (context == null)
            {
                return;
            }

            context.ExpectedPayloadReleaseCount = Math.Max(context.ExpectedPayloadReleaseCount, Math.Max(0, expectedCount));
        }

        private void MarkPayloadReleased(AirstrikeCallContext context)
        {
            if (context == null)
            {
                return;
            }

            context.PayloadReleaseCount = Math.Min(int.MaxValue, context.PayloadReleaseCount + 1);
        }

        private float GetDeliveryCarrierFirstPayloadDelay(AirstrikeCallContext context)
        {
            if (!ShouldUseDestroyableDeliveryCarrier(context))
            {
                return 0f;
            }

            var visuals = config?.DeliveryVisuals;
            if (visuals == null)
            {
                return 0f;
            }

            var legacyFallback = visuals.DestroyableDeliveryVehicleFirstPayloadDelaySeconds < 0f
                ? 1.5f
                : visuals.DestroyableDeliveryVehicleFirstPayloadDelaySeconds;

            if (string.Equals(context.Strike.Delivery, "drone", StringComparison.OrdinalIgnoreCase))
            {
                return ClampDeliveryDelay(visuals.DroneFirstPayloadDelaySeconds, legacyFallback);
            }

            HeavyDropPayloadSpec heavyDropSpec;
            if (TryGetHeavyDropPayloadSpec(context.Strike.Payload, out heavyDropSpec))
            {
                return ClampDeliveryDelay(visuals.CargoPlaneFirstPayloadDelaySeconds, 9f);
            }

            MlrsPayloadSpec mlrsSpec;
            if (TryGetMlrsPayloadSpec(context.Strike.Payload, out mlrsSpec))
            {
                return ClampDeliveryDelay(visuals.MlrsFirstPayloadDelaySeconds, 12f);
            }

            if (string.Equals(context.Strike.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase))
            {
                return ClampDeliveryDelay(visuals.A10FirstPayloadDelaySeconds, 8f);
            }

            if (string.Equals(context.Strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase))
            {
                return ClampDeliveryDelay(visuals.AttackHeliFirstPayloadDelaySeconds, 7f);
            }

            if (string.Equals(context.Strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase))
            {
                return ClampDeliveryDelay(visuals.CargoPlaneFirstPayloadDelaySeconds, 9f);
            }

            return ClampDeliveryDelay(legacyFallback, 1.5f);
        }

        private float ClampDeliveryDelay(float configured, float fallback)
        {
            return Mathf.Clamp(configured < 0f ? fallback : configured, 0f, 20f);
        }

        private int PreparePayloadReleaseSchedule(
            AirstrikeCallContext context,
            string vehicle,
            DeliveryVisualProfile deliveryProfile,
            int requestedCount,
            string fallbackPayload,
            float defaultFirstPayloadDelay,
            float defaultInterval,
            float finishPadding,
            out float firstPayloadDelay,
            out float postReleaseDuration,
            out float finishDelay)
        {
            requestedCount = Math.Max(0, requestedCount);
            firstPayloadDelay = Mathf.Max(0f, defaultFirstPayloadDelay);
            var safeInterval = Mathf.Max(0.01f, defaultInterval);
            var safeFinishPadding = Mathf.Max(0.1f, finishPadding);
            postReleaseDuration = ((Math.Max(1, requestedCount) - 1) * safeInterval) + safeFinishPadding;
            finishDelay = Math.Max(0.1f, firstPayloadDelay + postReleaseDuration);

            if (context == null || requestedCount <= 0)
            {
                return requestedCount;
            }

            context.PayloadReleaseSchedule.Clear();

            string profileId;
            VisualProfileConfig profile;
            if (!TryResolveVisualProfileForRuntime(context, vehicle, deliveryProfile, out profileId, out profile))
            {
                return requestedCount;
            }

            context.ActiveVisualProfileId = profileId;
            context.ActiveVisualProfile = profile;

            List<VisualPayloadEvent> compiledReleaseEvents = null;
            string compiledReleaseError = "";
            var hasCompiledReleaseEvents = visualProfileFile != null
                && visualProfileFile.SchemaVersion >= 2
                && TryValidateCompiledReleaseEvents(profile, out compiledReleaseEvents, out compiledReleaseError);
            var hasManualEvents = profile.PayloadEvents != null && profile.PayloadEvents.Count > 0;
            if (!hasCompiledReleaseEvents && !hasManualEvents && !string.Equals(profile.PayloadReleaseMode, "generated", StringComparison.OrdinalIgnoreCase))
            {
                firstPayloadDelay = Mathf.Clamp(profile.FirstPayloadDelaySeconds, 0f, profile.DurationSeconds);
                postReleaseDuration = ((Math.Max(1, requestedCount) - 1) * safeInterval) + safeFinishPadding;
                finishDelay = Math.Max(profile.DurationSeconds, Math.Max(0.1f, firstPayloadDelay + postReleaseDuration));
                postReleaseDuration = Math.Max(0.1f, finishDelay - firstPayloadDelay);
                return requestedCount;
            }

            var budget = requestedCount;
            if (profile.MaxPayloadCount > 0)
            {
                budget = Math.Min(budget, profile.MaxPayloadCount);
            }

            if (budget <= 0)
            {
                return 0;
            }

            if (hasCompiledReleaseEvents)
            {
                BuildCompiledPayloadReleaseSchedule(context, compiledReleaseEvents, budget);
            }
            else if (string.Equals(profile.PayloadReleaseMode, "generated", StringComparison.OrdinalIgnoreCase))
            {
                BuildGeneratedPayloadReleaseSchedule(context, profile, fallbackPayload, budget);
            }
            else
            {
                BuildManualPayloadReleaseSchedule(context, profile, fallbackPayload, budget);
            }

            if (context.PayloadReleaseSchedule.Count == 0)
            {
                return 0;
            }

            for (var i = 0; i < context.PayloadReleaseSchedule.Count; i++)
            {
                context.PayloadReleaseSchedule[i].SequenceIndex = i + 1;
                context.PayloadReleaseSchedule[i].TotalCount = context.PayloadReleaseSchedule.Count;
            }

            firstPayloadDelay = Mathf.Clamp(context.PayloadReleaseSchedule[0].Time, 0f, profile.DurationSeconds);
            var latestCompletionDelay = firstPayloadDelay + safeFinishPadding;
            foreach (var release in context.PayloadReleaseSchedule)
            {
                if (release == null)
                {
                    continue;
                }

                latestCompletionDelay = Math.Max(latestCompletionDelay, release.Time + GetPayloadReleaseFinishPadding(context, release, safeFinishPadding));
            }

            finishDelay = Math.Max(profile.DurationSeconds, Math.Max(0.1f, latestCompletionDelay));
            postReleaseDuration = Math.Max(0.1f, finishDelay - firstPayloadDelay);

            if (config?.General != null && config.General.DebugMode && context.Strike != null)
            {
                Puts(context.Strike.Id + " using " + context.PayloadReleaseSchedule.Count + " release event payload unit(s) from visual profile '" + profileId + "'.");
            }

            return context.PayloadReleaseSchedule.Count;
        }

        private void BuildCompiledPayloadReleaseSchedule(AirstrikeCallContext context, List<VisualPayloadEvent> compiledEvents, int budget)
        {
            if (context == null || compiledEvents == null || budget <= 0)
            {
                return;
            }

            var count = Math.Min(budget, compiledEvents.Count);
            for (var i = 0; i < count; i++)
            {
                var payloadEvent = compiledEvents[i];
                if (payloadEvent == null)
                {
                    continue;
                }

                context.PayloadReleaseSchedule.Add(new RuntimePayloadRelease
                {
                    Event = ClonePayloadEvent(payloadEvent),
                    Payload = NormalizePayloadId(payloadEvent.Payload),
                    Time = payloadEvent.Time,
                    SourceEventIndex = Math.Max(1, payloadEvent.Index)
                });
            }

        }

        private bool TryResolveVisualProfileForRuntime(AirstrikeCallContext context, string vehicle, DeliveryVisualProfile deliveryProfile, out string profileId, out VisualProfileConfig profile)
        {
            profileId = "";
            profile = null;
            if (context == null)
            {
                return false;
            }

            var normalizedVehicle = NormalizeVisualProfileVehicle(vehicle, context.Strike, null, deliveryProfile);
            if ((visualProfileFile == null || visualProfileFile.Profiles == null || visualProfileFile.Profiles.Count == 0) && File.Exists(ResolveVisualProfilesDataPath()))
            {
                LoadVisualProfiles();
            }

            if (string.IsNullOrWhiteSpace(normalizedVehicle))
            {
                return false;
            }

            return TryGetVisualProfile(context, normalizedVehicle, deliveryProfile, out profileId, out profile);
        }

        private void BuildManualPayloadReleaseSchedule(AirstrikeCallContext context, VisualProfileConfig profile, string fallbackPayload, int budget)
        {
            if (context == null || profile == null || profile.PayloadEvents == null || budget <= 0)
            {
                return;
            }

            var remaining = budget;
            foreach (var payloadEvent in profile.PayloadEvents)
            {
                if (payloadEvent == null || remaining <= 0)
                {
                    break;
                }

                var count = Math.Min(remaining, Math.Max(1, payloadEvent.Count));
                for (var i = 0; i < count; i++)
                {
                    context.PayloadReleaseSchedule.Add(new RuntimePayloadRelease
                    {
                        Event = ClonePayloadEvent(payloadEvent),
                        Payload = GetReleasePayload(payloadEvent, fallbackPayload),
                        Time = Mathf.Clamp(payloadEvent.Time, 0f, profile.DurationSeconds),
                        SourceEventIndex = Math.Max(1, payloadEvent.Index)
                    });
                }

                remaining -= count;
            }

            context.PayloadReleaseSchedule.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        private void BuildGeneratedPayloadReleaseSchedule(AirstrikeCallContext context, VisualProfileConfig profile, string fallbackPayload, int budget)
        {
            if (context == null || profile == null || budget <= 0)
            {
                return;
            }

            var template = ClonePayloadEvent(profile.ReleaseTemplate) ?? new VisualPayloadEvent();
            var interval = Mathf.Clamp(profile.PayloadReleaseIntervalSeconds <= 0f ? DefaultPayloadReleaseIntervalSeconds : profile.PayloadReleaseIntervalSeconds, 0.01f, 30f);
            var startTime = Mathf.Clamp(template.Time > 0f ? template.Time : profile.FirstPayloadDelaySeconds, 0f, profile.DurationSeconds);
            var released = 0;
            for (var i = 0; released < budget; i++)
            {
                var time = startTime + (i * interval);
                if (time > profile.DurationSeconds + 0.001f)
                {
                    break;
                }

                var unitsAtPoint = Math.Min(Math.Max(1, template.Count), budget - released);
                for (var unit = 0; unit < unitsAtPoint; unit++)
                {
                    var releaseEvent = ClonePayloadEvent(template) ?? new VisualPayloadEvent();
                    releaseEvent.Time = Mathf.Clamp(time, 0f, profile.DurationSeconds);
                    releaseEvent.Index = i + 1;
                    releaseEvent.Count = 1;
                    context.PayloadReleaseSchedule.Add(new RuntimePayloadRelease
                    {
                        Event = releaseEvent,
                        Payload = GetReleasePayload(releaseEvent, fallbackPayload),
                        Time = releaseEvent.Time,
                        SourceEventIndex = releaseEvent.Index
                    });
                    released++;
                }
            }

            if (config?.General != null && config.General.DebugMode && released < budget && context.Strike != null)
            {
                Puts(context.Strike.Id + " generated release profile truncated " + (budget - released) + " payload unit(s) because the visual duration ended.");
            }
        }

        private VisualPayloadEvent ClonePayloadEvent(VisualPayloadEvent source)
        {
            if (source == null)
            {
                return null;
            }

            return new VisualPayloadEvent
            {
                Time = source.Time,
                Payload = source.Payload,
                Index = source.Index,
                Count = source.Count,
                CarrierOffsetX = source.CarrierOffsetX,
                CarrierOffsetY = source.CarrierOffsetY,
                CarrierOffsetZ = source.CarrierOffsetZ,
                TargetOffsetX = source.TargetOffsetX,
                TargetOffsetY = source.TargetOffsetY,
                TargetOffsetZ = source.TargetOffsetZ,
                SpreadRadius = source.SpreadRadius,
                LaunchSpeed = source.LaunchSpeed,
                FuseSeconds = source.FuseSeconds,
                DamageScale = source.DamageScale,
                VehicleDamageScale = source.VehicleDamageScale,
                SplashRadius = source.SplashRadius,
                ImpactRadius = source.ImpactRadius,
                MaxTrackingSeconds = source.MaxTrackingSeconds,
                MaxTrackingDistance = source.MaxTrackingDistance,
                DamageScales = source.DamageScales == null
                    ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, float>(source.DamageScales, StringComparer.OrdinalIgnoreCase)
            };
        }

        private string GetReleasePayload(VisualPayloadEvent payloadEvent, string fallbackPayload)
        {
            var payload = NormalizePayloadId(payloadEvent == null ? "" : payloadEvent.Payload);
            return string.IsNullOrWhiteSpace(payload) ? NormalizePayloadId(fallbackPayload) : payload;
        }

        private bool HasPayloadReleaseSchedule(AirstrikeCallContext context)
        {
            return context != null && context.PayloadReleaseSchedule != null && context.PayloadReleaseSchedule.Count > 0;
        }

        private float GetPayloadReleaseFinishPadding(AirstrikeCallContext context, RuntimePayloadRelease release, float fallbackPadding)
        {
            var payload = NormalizePayloadId(release == null ? "" : release.Payload);
            if (string.IsNullOrWhiteSpace(payload) && context?.Strike != null)
            {
                payload = NormalizePayloadId(context.Strike.Payload);
            }

            DronePayloadSpec droneSpec;
            if (TryGetDronePayloadSpec(payload, out droneSpec))
            {
                if (release?.Event != null && release.Event.FuseSeconds >= 0f && droneSpec.HasTimedFuse)
                {
                    return Mathf.Clamp(release.Event.FuseSeconds, 0f, 120f) + 1.25f;
                }

                return Math.Max(0.1f, droneSpec.FinishDelaySeconds);
            }

            HeavyDropPayloadSpec heavySpec;
            if (TryGetHeavyDropPayloadSpec(payload, out heavySpec))
            {
                return Math.Max(0.1f, heavySpec.FinishDelaySeconds);
            }

            RocketRunPayloadSpec rocketSpec;
            if (TryGetRocketPayloadSpec(payload, out rocketSpec))
            {
                return Math.Max(0.1f, rocketSpec.FinishDelaySeconds);
            }

            MlrsPayloadSpec mlrsSpec;
            if (TryGetMlrsPayloadSpec(payload, out mlrsSpec))
            {
                return Math.Max(0.1f, mlrsSpec.FinishDelaySeconds);
            }

            HomingMissileSpec homingSpec;
            if (TryGetHomingMissileSpec(payload, out homingSpec))
            {
                return GetReleaseTrackingSeconds(context == null ? null : context.Strike, release == null ? null : release.Event) + Math.Max(0.1f, homingSpec.FinishDelaySeconds);
            }

            MortarPayloadSpec mortarSpec;
            if (TryGetMortarPayloadSpec(payload, out mortarSpec))
            {
                return Math.Max(0.1f, mortarSpec.FinishDelaySeconds);
            }

            A10StrafeSpec a10Spec;
            if (TryGetA10StrafeSpec(payload, out a10Spec))
            {
                return A10FinishPaddingSeconds;
            }

            return Math.Max(0.1f, fallbackPadding);
        }

        private bool ShouldUseDestroyableDeliveryCarrier(AirstrikeCallContext context)
        {
            return context != null
                && context.Strike != null
                && config?.DeliveryVisuals != null
                && config.DeliveryVisuals.Enabled
                && config.DeliveryVisuals.DeliveryVehiclesCanBeDestroyed
                && IsDeliveryVisualEnabledForStrike(context.Strike);
        }

        private bool IsDeliveryVisualEnabledForStrike(StrikeDefinition strike)
        {
            if (strike == null || config?.DeliveryVisuals == null)
            {
                return false;
            }

            if (string.Equals(strike.Delivery, "drone", StringComparison.OrdinalIgnoreCase))
            {
                return config.DeliveryVisuals.SpawnDroneVisuals;
            }

            if (string.Equals(strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase)
                || string.Equals(strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(strike.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase))
            {
                return config.DeliveryVisuals.SpawnAircraftVisuals;
            }

            return false;
        }

        private bool HasReleasedAllCarrierPayloads(AirstrikeCallContext context)
        {
            return context == null
                || (context.ExpectedPayloadReleaseCount > 0 && context.PayloadReleaseCount >= context.ExpectedPayloadReleaseCount);
        }

        private bool ShouldRefundDestroyedDeliveryVehicle(AirstrikeCallContext context)
        {
            return config?.DeliveryVisuals != null && config.DeliveryVisuals.RefundIfDeliveryVehicleDestroyedBeforePayload;
        }

        private void RegisterDeliveryCarrier(AirstrikeCallContext context, BaseEntity entity, string label)
        {
            if (!ShouldUseDestroyableDeliveryCarrier(context) || entity == null || entity.IsDestroyed)
            {
                return;
            }

            var health = GetDeliveryCarrierHealth(context, entity, label);
            if (health <= 0f)
            {
                return;
            }

            var combat = entity as BaseCombatEntity;
            if (combat != null)
            {
                try
                {
                    combat.InitializeHealth(health, health);
                }
                catch (Exception ex)
                {
                    if (config.General.DebugMode)
                    {
                        Puts("Could not initialize delivery carrier health for " + (entity.ShortPrefabName ?? label ?? "entity") + ": " + ex.Message);
                    }
                }
            }
            else if (config.General.DebugMode)
            {
                Puts((context.Strike == null ? "unknown" : context.Strike.Id) + " visual " + label + " is not a BaseCombatEntity; using manual OnPlayerAttack hit tracking for the delivery carrier.");
            }

            context.DeliveryCarrier = entity;
            context.DeliveryCarrierRequired = config.DeliveryVisuals.PayloadRequiresLiveDeliveryVehicle;
            context.DeliveryCarrierDestroyed = false;
            context.FailureForfeitsRefund = false;
            context.DeliveryCarrierLabel = string.IsNullOrWhiteSpace(label) ? "delivery vehicle" : label;
            context.DeliveryCarrierMaxHealth = health;
            context.DeliveryCarrierHealthRemaining = health;

            if (config.General.DebugMode)
            {
                Puts(context.Strike.Id + " armed destroyable delivery carrier " + context.DeliveryCarrierLabel + " with " + health.ToString("0", CultureInfo.InvariantCulture) + " health.");
            }
        }

        private float GetDeliveryCarrierHealth(AirstrikeCallContext context, BaseEntity entity, string label)
        {
            if (context?.Strike == null || config?.DeliveryVisuals == null)
            {
                return 0f;
            }

            if (string.Equals(context.Strike.Delivery, "drone", StringComparison.OrdinalIgnoreCase))
            {
                return config.DeliveryVisuals.DroneDeliveryVehicleHealth;
            }

            HeavyDropPayloadSpec heavyDropSpec;
            if (TryGetHeavyDropPayloadSpec(context.Strike.Payload, out heavyDropSpec))
            {
                return config.DeliveryVisuals.CargoPlaneDeliveryVehicleHealth;
            }

            if (string.Equals(context.Strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase))
            {
                return config.DeliveryVisuals.AttackHeliDeliveryVehicleHealth;
            }

            if (string.Equals(context.Strike.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase))
            {
                return config.DeliveryVisuals.A10DeliveryVehicleHealth;
            }

            if (string.Equals(context.Strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase))
            {
                return config.DeliveryVisuals.CargoPlaneDeliveryVehicleHealth;
            }

            return 0f;
        }

        private bool TryRequireLiveDeliveryCarrier(AirstrikeCallContext context, string releaseLabel, out string error)
        {
            error = "";
            if (context == null || !context.DeliveryCarrierRequired || !config.DeliveryVisuals.PayloadRequiresLiveDeliveryVehicle)
            {
                return true;
            }

            if (HasReleasedAllCarrierPayloads(context))
            {
                return true;
            }

            var carrier = context.DeliveryCarrier;
            var combat = carrier as BaseCombatEntity;
            if (carrier != null
                && !carrier.IsDestroyed
                && (combat == null || !combat.IsDead())
                && context.DeliveryCarrierHealthRemaining > 0f)
            {
                return true;
            }

            context.DeliveryCarrierDestroyed = true;
            context.FailureForfeitsRefund = !ShouldRefundDestroyedDeliveryVehicle(context);
            error = BuildDeliveryCarrierDestroyedMessage(context, releaseLabel);
            return false;
        }

        private string BuildDeliveryCarrierDestroyedMessage(AirstrikeCallContext context, string releaseLabel)
        {
            var label = string.IsNullOrWhiteSpace(context?.DeliveryCarrierLabel) ? "delivery vehicle" : context.DeliveryCarrierLabel;
            var release = string.IsNullOrWhiteSpace(releaseLabel) ? "its payload" : releaseLabel;
            return label + " was destroyed before releasing " + release + ".";
        }

        private void HandleDeliveryCarrierDestroyed(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null)
            {
                return;
            }

            foreach (var context in new List<AirstrikeCallContext>(activeCalls.Values))
            {
                foreach (var executionContext in EnumerateExecutionContexts(context))
                {
                    if (executionContext == null || !IsCallActive(executionContext) || !ReferenceEquals(executionContext.DeliveryCarrier, entity))
                    {
                        continue;
                    }

                    FailOrClearDestroyedDeliveryCarrier(executionContext, info?.Initiator as BasePlayer);
                    return;
                }
            }
        }

        private void TryApplyDeliveryCarrierHit(BasePlayer attacker, HitInfo info)
        {
            if (info == null)
            {
                return;
            }

            var hitEntity = info.HitEntity as BaseEntity;
            if (hitEntity == null)
            {
                return;
            }

            foreach (var context in new List<AirstrikeCallContext>(activeCalls.Values))
            {
                foreach (var executionContext in EnumerateExecutionContexts(context))
                {
                    if (executionContext == null || !IsCallActive(executionContext) || !ReferenceEquals(executionContext.DeliveryCarrier, hitEntity))
                    {
                        continue;
                    }

                    if (executionContext.DeliveryCarrierHealthRemaining <= 0f)
                    {
                        return;
                    }

                    var damage = 0f;
                    try
                    {
                        damage = info.damageTypes == null ? 0f : info.damageTypes.Total();
                    }
                    catch
                    {
                        damage = 0f;
                    }

                    if (damage <= 0f)
                    {
                        damage = 1f;
                    }

                    executionContext.DeliveryCarrierHealthRemaining = Math.Max(0f, executionContext.DeliveryCarrierHealthRemaining - damage);
                    if (config.General.DebugMode)
                    {
                        Puts(executionContext.Strike.Id + " delivery carrier " + executionContext.DeliveryCarrierLabel + " took " + damage.ToString("0.0", CultureInfo.InvariantCulture) + " damage; " + executionContext.DeliveryCarrierHealthRemaining.ToString("0.0", CultureInfo.InvariantCulture) + "/" + executionContext.DeliveryCarrierMaxHealth.ToString("0.0", CultureInfo.InvariantCulture) + " health remaining.");
                    }

                    if (executionContext.DeliveryCarrierHealthRemaining > 0f)
                    {
                        return;
                    }

                    FailOrClearDestroyedDeliveryCarrier(executionContext, attacker);
                    return;
                }
            }
        }

        private void FailOrClearDestroyedDeliveryCarrier(AirstrikeCallContext context, BasePlayer attacker)
        {
            if (context == null || !IsCallActive(context))
            {
                return;
            }

            if (!context.DeliveryCarrierRequired || HasReleasedAllCarrierPayloads(context))
            {
                KillTrackedVisualEntity(context, context.DeliveryCarrier);
                ClearDeliveryCarrier(context);
                return;
            }

            context.DeliveryCarrierDestroyed = true;
            context.FailureForfeitsRefund = !ShouldRefundDestroyedDeliveryVehicle(context);
            IncrementStat("delivery_vehicle_destroyed");

            var attackerText = attacker == null ? "" : " by " + (attacker.displayName ?? attacker.UserIDString);
            var message = BuildDeliveryCarrierDestroyedMessage(context, "its payload") + attackerText;
            var root = GetRootContext(context);
            if (root != null && !ReferenceEquals(root, context))
            {
                root.DeliveryCarrierDestroyed = true;
                root.FailureForfeitsRefund = context.FailureForfeitsRefund;
                root.ImpactStarted = root.ImpactStarted || context.ImpactStarted;
                FailStrikeCall(root, message, ShouldRefundDestroyedDeliveryVehicle(context));
                return;
            }

            FailStrikeCall(context, message, ShouldRefundDestroyedDeliveryVehicle(context));
        }

        private void ClearDeliveryCarrier(AirstrikeCallContext context)
        {
            if (context == null)
            {
                return;
            }

            context.DeliveryCarrier = null;
            context.DeliveryCarrierRequired = false;
            context.DeliveryCarrierLabel = "";
            context.DeliveryCarrierMaxHealth = 0f;
            context.DeliveryCarrierHealthRemaining = 0f;
        }

        private DeliveryFlightPlan BuildDeliveryFlightPlan(Vector3 release, Vector3 direction, float inboundDistance, float firstPayloadDelay, float postReleaseDuration, float minimumDuration, float maximumDuration)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = Vector3.forward;
            }
            else
            {
                direction.Normalize();
            }

            var safeInboundDistance = Mathf.Clamp(inboundDistance, 10f, 500f);
            var safeFirstPayloadDelay = Mathf.Max(0f, firstPayloadDelay);
            var safePostReleaseDuration = Mathf.Max(0.1f, postReleaseDuration);
            var safeDuration = Mathf.Clamp(safeFirstPayloadDelay + safePostReleaseDuration, minimumDuration, maximumDuration);
            var speed = safeFirstPayloadDelay > 0.05f
                ? safeInboundDistance / safeFirstPayloadDelay
                : (safeInboundDistance * 2f) / Math.Max(0.1f, safeDuration);
            var start = release - (direction * safeInboundDistance);
            var end = start + (direction * Math.Max(safeInboundDistance + 20f, speed * safeDuration));
            var minimumOutboundDistance = Math.Max(25f, safeInboundDistance * 0.5f);

            if (Vector3.Dot(end - release, direction) < minimumOutboundDistance)
            {
                end = release + (direction * minimumOutboundDistance);
            }

            var plan = new DeliveryFlightPlan
            {
                Start = start,
                Release = release,
                End = end,
                Direction = direction,
                Duration = safeDuration,
                FirstPayloadDelay = safeFirstPayloadDelay
            };

            AddFlightWaypoint(plan, start, 0f);
            if (safeFirstPayloadDelay > 0.05f && safeFirstPayloadDelay < safeDuration - 0.05f)
            {
                AddFlightWaypoint(plan, release, safeFirstPayloadDelay);
            }
            AddFlightWaypoint(plan, end, safeDuration);
            FinalizeFlightPlan(plan);
            return plan;
        }

        private DeliveryFlightPlan CreateEmptyFlightPlan(Vector3 start, Vector3 release, Vector3 end, Vector3 direction, float duration, float firstPayloadDelay)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = (end - start);
                direction.y = 0f;
            }

            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = Vector3.forward;
            }
            else
            {
                direction.Normalize();
            }

            return new DeliveryFlightPlan
            {
                Start = start,
                Release = release,
                End = end,
                Direction = direction,
                Duration = Mathf.Max(0.1f, duration),
                FirstPayloadDelay = Mathf.Max(0f, firstPayloadDelay)
            };
        }

        private void AddFlightWaypoint(DeliveryFlightPlan plan, Vector3 position, float time)
        {
            AddFlightWaypoint(plan, position, time, Quaternion.identity);
        }

        private void AddFlightWaypoint(DeliveryFlightPlan plan, Vector3 position, float time, Quaternion rotationOffset)
        {
            if (plan == null)
            {
                return;
            }

            var safeTime = Mathf.Clamp(time, 0f, Mathf.Max(0.1f, plan.Duration));
            plan.Waypoints.Add(new FlightWaypoint
            {
                Position = position,
                Time = safeTime,
                RotationOffset = rotationOffset
            });
        }

        private void FinalizeFlightPlan(DeliveryFlightPlan plan)
        {
            if (plan == null)
            {
                return;
            }

            plan.Duration = Mathf.Max(0.1f, plan.Duration);
            if (plan.Waypoints.Count == 0)
            {
                AddFlightWaypoint(plan, plan.Start, 0f);
                AddFlightWaypoint(plan, plan.End, plan.Duration);
            }

            plan.Waypoints.Sort((a, b) => a.Time.CompareTo(b.Time));

            for (var i = 1; i < plan.Waypoints.Count; i++)
            {
                if (plan.Waypoints[i].Time <= plan.Waypoints[i - 1].Time + 0.01f)
                {
                    plan.Waypoints[i].Time = Mathf.Min(plan.Duration, plan.Waypoints[i - 1].Time + 0.05f);
                }
            }

            for (var i = plan.Waypoints.Count - 2; i >= 0; i--)
            {
                var current = plan.Waypoints[i];
                var next = plan.Waypoints[i + 1];
                if (Math.Abs(current.Time - next.Time) <= 0.001f && Vector3.Distance(current.Position, next.Position) <= 0.01f)
                {
                    plan.Waypoints.RemoveAt(i + 1);
                }
            }

            plan.Start = plan.Waypoints[0].Position;
            plan.End = plan.Waypoints[plan.Waypoints.Count - 1].Position;
        }

        private float GetVisualTerrainClearance(StrikeDefinition strike, string label)
        {
            var visuals = config == null ? null : config.DeliveryVisuals;
            if (visuals == null)
            {
                return DefaultAircraftMinimumTerrainClearance;
            }

            if (strike != null && string.Equals(strike.Delivery, "drone", StringComparison.OrdinalIgnoreCase))
            {
                var configured = visuals.DroneMinimumTerrainClearance;
                return Mathf.Clamp(configured <= 0f ? DefaultDroneMinimumTerrainClearance : configured, 4f, 80f);
            }

            var aircraftClearance = visuals.AircraftMinimumTerrainClearance;
            return Mathf.Clamp(aircraftClearance <= 0f ? DefaultAircraftMinimumTerrainClearance : aircraftClearance, 12f, 180f);
        }

        private float GetPayloadTerrainClearance()
        {
            var visuals = config == null ? null : config.DeliveryVisuals;
            if (visuals == null)
            {
                return DefaultPayloadMinimumTerrainClearance;
            }

            var configured = visuals.PayloadMinimumTerrainClearance;
            return Mathf.Clamp(configured <= 0f ? DefaultPayloadMinimumTerrainClearance : configured, 2f, 60f);
        }

        private bool TryGetFlightSurfaceHeight(Vector3 position, out float surfaceY)
        {
            surfaceY = 0f;
            var found = false;

            try
            {
                if (TerrainMeta.HeightMap != null)
                {
                    surfaceY = TerrainMeta.HeightMap.GetHeight(position);
                    found = true;
                }
            }
            catch
            {
            }

            try
            {
                RaycastHit hit;
                var startY = found ? Mathf.Max(position.y + 320f, surfaceY + 320f) : position.y + 320f;
                var start = new Vector3(position.x, startY, position.z);
                if (Physics.Raycast(start, Vector3.down, out hit, 720f, FlightTerrainRaycastLayer, QueryTriggerInteraction.Ignore))
                {
                    surfaceY = found ? Mathf.Max(surfaceY, hit.point.y) : hit.point.y;
                    found = true;
                }
            }
            catch
            {
            }

            return found;
        }

        private Vector3 EnsurePositionAboveTerrain(Vector3 position, float clearance)
        {
            float surfaceY;
            if (!TryGetFlightSurfaceHeight(position, out surfaceY))
            {
                return position;
            }

            var minimumY = surfaceY + Mathf.Max(0f, clearance);
            if (position.y < minimumY)
            {
                position.y = minimumY;
            }

            return position;
        }

        private void ApplyTerrainClearanceToFlightPlan(DeliveryFlightPlan plan, float clearance)
        {
            if (plan == null || plan.Waypoints == null || plan.Waypoints.Count == 0)
            {
                return;
            }

            var safeClearance = Mathf.Clamp(clearance, 0f, 200f);
            foreach (var waypoint in plan.Waypoints)
            {
                if (waypoint != null)
                {
                    waypoint.Position = EnsurePositionAboveTerrain(waypoint.Position, safeClearance);
                }
            }

            for (var pass = 0; pass < 3; pass++)
            {
                var adjusted = false;
                for (var i = 0; i < plan.Waypoints.Count - 1; i++)
                {
                    var a = plan.Waypoints[i];
                    var b = plan.Waypoints[i + 1];
                    if (a == null || b == null)
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(a.Position, b.Position);
                    var samples = Mathf.Clamp(Mathf.CeilToInt(distance / FlightPlanTerrainSampleSpacing), 2, 18);
                    var maxDelta = 0f;
                    for (var sample = 1; sample < samples; sample++)
                    {
                        var t = sample / (float)samples;
                        var samplePosition = Vector3.Lerp(a.Position, b.Position, t);
                        float surfaceY;
                        if (!TryGetFlightSurfaceHeight(samplePosition, out surfaceY))
                        {
                            continue;
                        }

                        var requiredY = surfaceY + safeClearance;
                        if (samplePosition.y < requiredY)
                        {
                            maxDelta = Mathf.Max(maxDelta, requiredY - samplePosition.y);
                        }
                    }

                    if (maxDelta <= 0.05f)
                    {
                        continue;
                    }

                    var lift = Vector3.up * (maxDelta + 1f);
                    a.Position += lift;
                    b.Position += lift;
                    adjusted = true;
                }

                if (!adjusted)
                {
                    break;
                }
            }

            FinalizeFlightPlan(plan);

            if (plan.FirstPayloadDelay > 0f)
            {
                Vector3 releasePosition;
                Vector3 releaseDirection;
                Vector3 releaseVelocity;
                EvaluateFlightPlan(plan, plan.FirstPayloadDelay, out releasePosition, out releaseDirection, out releaseVelocity);
                plan.Release = EnsurePositionAboveTerrain(releasePosition, safeClearance);
            }
            else
            {
                plan.Release = EnsurePositionAboveTerrain(plan.Release, safeClearance);
            }
        }

        private bool IsLinearFlightPlan(DeliveryFlightPlan plan)
        {
            return plan == null || plan.Waypoints == null || plan.Waypoints.Count <= 2;
        }

        private Vector3 GetPlanDirectionAt(DeliveryFlightPlan plan, float elapsed)
        {
            Vector3 position;
            Vector3 direction;
            Vector3 velocity;
            EvaluateFlightPlan(plan, elapsed, out position, out direction, out velocity);
            return direction;
        }

        private Vector3 GetPlanVelocityAt(DeliveryFlightPlan plan, float elapsed)
        {
            Vector3 position;
            Vector3 direction;
            Vector3 velocity;
            EvaluateFlightPlan(plan, elapsed, out position, out direction, out velocity);
            return velocity;
        }

        private bool TryEvaluateCompiledFlightPlan(DeliveryFlightPlan plan, float elapsed, out Vector3 position, out Quaternion rotation, out Vector3 velocity)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            velocity = Vector3.zero;
            if (plan == null || !plan.UsesCompiledTrack || plan.CompiledFrames == null || plan.CompiledFrames.Count < 2)
            {
                return false;
            }

            var frames = plan.CompiledFrames;
            var time = Mathf.Clamp(elapsed, frames[0].Time, frames[frames.Count - 1].Time);
            if (time <= frames[0].Time)
            {
                var first = frames[0];
                var next = frames[1];
                position = first.Position;
                rotation = first.Rotation;
                velocity = (next.Position - first.Position) / Mathf.Max(0.0001f, next.Time - first.Time);
                return true;
            }

            var lastIndex = frames.Count - 1;
            if (time >= frames[lastIndex].Time)
            {
                var previous = frames[lastIndex - 1];
                var last = frames[lastIndex];
                position = last.Position;
                rotation = last.Rotation;
                velocity = (last.Position - previous.Position) / Mathf.Max(0.0001f, last.Time - previous.Time);
                return true;
            }

            var low = 0;
            var high = lastIndex;
            while (high - low > 1)
            {
                var middle = low + ((high - low) / 2);
                if (frames[middle].Time <= time)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            var a = frames[low];
            var b = frames[high];
            var segmentDuration = Mathf.Max(0.0001f, b.Time - a.Time);
            var progress = Mathf.Clamp01((time - a.Time) / segmentDuration);
            position = Vector3.Lerp(a.Position, b.Position, progress);
            rotation = Quaternion.Slerp(a.Rotation, b.Rotation, progress);
            velocity = (b.Position - a.Position) / segmentDuration;
            return true;
        }

        private Vector3 EvaluateFlightPlanPositionOnly(DeliveryFlightPlan plan, float elapsed)
        {
            if (plan == null)
            {
                return Vector3.zero;
            }

            Vector3 compiledPosition;
            Quaternion compiledRotation;
            Vector3 compiledVelocity;
            if (TryEvaluateCompiledFlightPlan(plan, elapsed, out compiledPosition, out compiledRotation, out compiledVelocity))
            {
                return compiledPosition;
            }

            var safeDuration = Mathf.Max(0.1f, plan.Duration);
            var time = Mathf.Clamp(elapsed, 0f, safeDuration);
            if (plan.Waypoints == null || plan.Waypoints.Count == 0)
            {
                return Vector3.Lerp(plan.Start, plan.End, Mathf.Clamp01(time / safeDuration));
            }

            var lastIndex = plan.Waypoints.Count - 1;
            if (time <= plan.Waypoints[0].Time)
            {
                return plan.Waypoints[0].Position;
            }

            if (time >= plan.Waypoints[lastIndex].Time)
            {
                return plan.Waypoints[lastIndex].Position;
            }

            for (var i = 0; i < lastIndex; i++)
            {
                var a = plan.Waypoints[i];
                var b = plan.Waypoints[i + 1];
                if (time < a.Time || time > b.Time)
                {
                    continue;
                }

                var segmentDuration = Mathf.Max(0.05f, b.Time - a.Time);
                var segmentProgress = Mathf.Clamp01((time - a.Time) / segmentDuration);
                if (!plan.StopAtWaypoints)
                {
                    return EvaluateHermiteFlightPlanPosition(plan, i, segmentProgress, segmentDuration);
                }

                var eased = Mathf.SmoothStep(0f, 1f, segmentProgress);
                return Vector3.Lerp(a.Position, b.Position, eased);
            }

            return plan.Waypoints[lastIndex].Position;
        }

        private Vector3 EvaluateHermiteFlightPlanPosition(DeliveryFlightPlan plan, int index, float progress, float segmentDuration)
        {
            var a = plan.Waypoints[index];
            var b = plan.Waypoints[index + 1];
            var m0 = GetFlightPlanWaypointVelocity(plan, index);
            var m1 = GetFlightPlanWaypointVelocity(plan, index + 1);
            var t2 = progress * progress;
            var t3 = t2 * progress;
            return ((2f * t3 - 3f * t2 + 1f) * a.Position)
                + ((t3 - 2f * t2 + progress) * segmentDuration * m0)
                + ((-2f * t3 + 3f * t2) * b.Position)
                + ((t3 - t2) * segmentDuration * m1);
        }

        private Vector3 EvaluateHermiteFlightPlanVelocity(DeliveryFlightPlan plan, int index, float progress, float segmentDuration)
        {
            var a = plan.Waypoints[index];
            var b = plan.Waypoints[index + 1];
            var m0 = GetFlightPlanWaypointVelocity(plan, index);
            var m1 = GetFlightPlanWaypointVelocity(plan, index + 1);
            var safeDuration = Mathf.Max(0.05f, segmentDuration);
            var t2 = progress * progress;
            return (((6f * t2 - 6f * progress) / safeDuration) * a.Position)
                + ((3f * t2 - 4f * progress + 1f) * m0)
                + (((-6f * t2 + 6f * progress) / safeDuration) * b.Position)
                + ((3f * t2 - 2f * progress) * m1);
        }

        private Vector3 GetFlightPlanWaypointVelocity(DeliveryFlightPlan plan, int index)
        {
            if (plan?.Waypoints == null || plan.Waypoints.Count < 2)
            {
                return Vector3.zero;
            }

            var last = plan.Waypoints.Count - 1;
            if (index <= 0)
            {
                return GetFlightPlanSegmentVelocity(plan, 0, 1);
            }

            if (index >= last)
            {
                return GetFlightPlanSegmentVelocity(plan, last - 1, last);
            }

            var previous = GetFlightPlanSegmentVelocity(plan, index - 1, index);
            var next = GetFlightPlanSegmentVelocity(plan, index, index + 1);
            if (previous.sqrMagnitude <= 0.01f)
            {
                return next;
            }

            if (next.sqrMagnitude <= 0.01f)
            {
                return previous;
            }

            return (previous + next) * 0.5f;
        }

        private Vector3 GetFlightPlanSegmentVelocity(DeliveryFlightPlan plan, int fromIndex, int toIndex)
        {
            var a = plan.Waypoints[fromIndex];
            var b = plan.Waypoints[toIndex];
            var duration = Mathf.Max(0.05f, b.Time - a.Time);
            return (b.Position - a.Position) / duration;
        }

        private Vector3 GetFlightPlanTangentDirection(DeliveryFlightPlan plan, float elapsed, Vector3 fallbackDirection)
        {
            if (plan == null)
            {
                return fallbackDirection;
            }

            var safeDuration = Mathf.Max(0.1f, plan.Duration);
            if (!plan.StopAtWaypoints && plan.Waypoints != null && plan.Waypoints.Count >= 2)
            {
                var time = Mathf.Clamp(elapsed, 0f, safeDuration);
                for (var i = 0; i < plan.Waypoints.Count - 1; i++)
                {
                    var a = plan.Waypoints[i];
                    var b = plan.Waypoints[i + 1];
                    if (time < a.Time || time > b.Time)
                    {
                        continue;
                    }

                    var segmentDuration = Mathf.Max(0.05f, b.Time - a.Time);
                    var segmentProgress = Mathf.Clamp01((time - a.Time) / segmentDuration);
                    var blendedVelocity = EvaluateHermiteFlightPlanVelocity(plan, i, segmentProgress, segmentDuration);
                    if (blendedVelocity.sqrMagnitude > 0.01f)
                    {
                        return blendedVelocity;
                    }
                }

                var endpointVelocity = time <= plan.Waypoints[0].Time ? GetFlightPlanWaypointVelocity(plan, 0) : GetFlightPlanWaypointVelocity(plan, plan.Waypoints.Count - 1);
                if (endpointVelocity.sqrMagnitude > 0.01f)
                {
                    return endpointVelocity;
                }
            }

            var sampleSeconds = Mathf.Clamp(Mathf.Min(FlightPlanTangentSampleSeconds, safeDuration * 0.08f), 0.035f, 0.35f);
            var before = Mathf.Clamp(elapsed - sampleSeconds, 0f, safeDuration);
            var after = Mathf.Clamp(elapsed + sampleSeconds, 0f, safeDuration);
            if (after - before < 0.025f)
            {
                before = Mathf.Clamp(elapsed - sampleSeconds * 2f, 0f, safeDuration);
                after = Mathf.Clamp(elapsed + sampleSeconds * 2f, 0f, safeDuration);
            }

            var tangent = EvaluateFlightPlanPositionOnly(plan, after) - EvaluateFlightPlanPositionOnly(plan, before);
            return tangent.sqrMagnitude > 0.01f ? tangent : fallbackDirection;
        }

        private void EvaluateFlightPlan(DeliveryFlightPlan plan, float elapsed, out Vector3 position, out Vector3 direction, out Vector3 velocity)
        {
            position = Vector3.zero;
            direction = Vector3.forward;
            velocity = Vector3.zero;

            if (plan == null)
            {
                return;
            }

            Quaternion compiledRotation;
            if (TryEvaluateCompiledFlightPlan(plan, elapsed, out position, out compiledRotation, out velocity))
            {
                direction = velocity.sqrMagnitude > 0.01f
                    ? velocity.normalized
                    : (plan.Direction.sqrMagnitude > 0.01f ? plan.Direction.normalized : Vector3.forward);
                return;
            }

            var safeDuration = Mathf.Max(0.1f, plan.Duration);
            var time = Mathf.Clamp(elapsed, 0f, safeDuration);
            if (plan.Waypoints == null || plan.Waypoints.Count == 0)
            {
                var progress = Mathf.Clamp01(time / safeDuration);
                position = Vector3.Lerp(plan.Start, plan.End, progress);
                direction = plan.End - plan.Start;
                if (direction.sqrMagnitude <= 0.01f)
                {
                    direction = plan.Direction.sqrMagnitude <= 0.01f ? Vector3.forward : plan.Direction;
                }
                direction.Normalize();
                velocity = direction * (Vector3.Distance(plan.Start, plan.End) / safeDuration);
                return;
            }

            if (time <= plan.Waypoints[0].Time)
            {
                position = plan.Waypoints[0].Position;
                if (plan.Waypoints.Count > 1)
                {
                    direction = plan.Waypoints[1].Position - plan.Waypoints[0].Position;
                }
            }
            else if (time >= plan.Waypoints[plan.Waypoints.Count - 1].Time)
            {
                position = plan.Waypoints[plan.Waypoints.Count - 1].Position;
                if (plan.Waypoints.Count > 1)
                {
                    direction = plan.Waypoints[plan.Waypoints.Count - 1].Position - plan.Waypoints[plan.Waypoints.Count - 2].Position;
                }
            }
            else
            {
                for (var i = 0; i < plan.Waypoints.Count - 1; i++)
                {
                    var a = plan.Waypoints[i];
                    var b = plan.Waypoints[i + 1];
                    if (time < a.Time || time > b.Time)
                    {
                        continue;
                    }

                    var segmentDuration = Mathf.Max(0.05f, b.Time - a.Time);
                    var segmentProgress = Mathf.Clamp01((time - a.Time) / segmentDuration);
                    if (plan.StopAtWaypoints)
                    {
                        var eased = Mathf.SmoothStep(0f, 1f, segmentProgress);
                        position = Vector3.Lerp(a.Position, b.Position, eased);
                    }
                    else
                    {
                        position = EvaluateHermiteFlightPlanPosition(plan, i, segmentProgress, segmentDuration);
                        velocity = EvaluateHermiteFlightPlanVelocity(plan, i, segmentProgress, segmentDuration);
                    }

                    direction = b.Position - a.Position;
                    if (plan.StopAtWaypoints && direction.sqrMagnitude > 0.01f)
                    {
                        velocity = direction.normalized * (Vector3.Distance(a.Position, b.Position) / segmentDuration);
                    }
                    break;
                }
            }

            var tangentDirection = GetFlightPlanTangentDirection(plan, time, direction);
            if (tangentDirection.sqrMagnitude > 0.01f)
            {
                direction = tangentDirection;
            }

            var speed = velocity.magnitude;
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = plan.Direction.sqrMagnitude <= 0.01f ? Vector3.forward : plan.Direction;
            }

            direction.Normalize();
            if (speed > 0.01f)
            {
                velocity = direction * speed;
            }

            if (velocity.sqrMagnitude <= 0.01f)
            {
                velocity = direction * (Vector3.Distance(plan.Start, plan.End) / safeDuration);
            }
        }

        private Quaternion EvaluateFlightPlanRotationOffset(DeliveryFlightPlan plan, float elapsed)
        {
            if (plan?.Waypoints == null || plan.Waypoints.Count == 0)
            {
                return Quaternion.identity;
            }

            var safeDuration = Mathf.Max(0.1f, plan.Duration);
            var time = Mathf.Clamp(elapsed, 0f, safeDuration);
            if (time <= plan.Waypoints[0].Time || plan.Waypoints.Count == 1)
            {
                return plan.Waypoints[0].RotationOffset;
            }

            var last = plan.Waypoints.Count - 1;
            if (time >= plan.Waypoints[last].Time)
            {
                return plan.Waypoints[last].RotationOffset;
            }

            for (var i = 0; i < last; i++)
            {
                var a = plan.Waypoints[i];
                var b = plan.Waypoints[i + 1];
                if (time < a.Time || time > b.Time)
                {
                    continue;
                }

                var segmentDuration = Mathf.Max(0.05f, b.Time - a.Time);
                var progress = Mathf.Clamp01((time - a.Time) / segmentDuration);
                var eased = plan.StopAtWaypoints ? Mathf.SmoothStep(0f, 1f, progress) : progress;
                return Quaternion.Slerp(a.RotationOffset, b.RotationOffset, eased);
            }

            return plan.Waypoints[last].RotationOffset;
        }

        private Quaternion GetFlightPlanTargetRotation(DeliveryFlightPlan plan, float elapsed, Vector3 direction)
        {
            Vector3 compiledPosition;
            Quaternion compiledRotation;
            Vector3 compiledVelocity;
            if (TryEvaluateCompiledFlightPlan(plan, elapsed, out compiledPosition, out compiledRotation, out compiledVelocity))
            {
                return compiledRotation;
            }

            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = plan != null && plan.Direction.sqrMagnitude > 0.01f ? plan.Direction : Vector3.forward;
            }

            return Quaternion.LookRotation(direction.normalized, Vector3.up) * EvaluateFlightPlanRotationOffset(plan, elapsed);
        }

        private Vector3 GetRightVector(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.01f)
            {
                forward = Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            var right = new Vector3(-forward.z, 0f, forward.x);
            if (right.sqrMagnitude <= 0.01f)
            {
                right = Vector3.right;
            }
            else
            {
                right.Normalize();
            }

            return right;
        }

        private float GetConfiguredDronePayloadSpawnHeight()
        {
            var configured = config?.DeliveryVisuals == null ? DroneDropSpawnHeight : config.DeliveryVisuals.DronePayloadSpawnHeight;
            return Mathf.Clamp(configured <= 0f ? DroneDropSpawnHeight : configured, DroneDropMinimumSpawnHeight, 25f);
        }

        private float GetDronePayloadSpawnHeight(DronePayloadSpec spec)
        {
            if (spec == null)
            {
                return GetConfiguredDronePayloadSpawnHeight();
            }

            if (!spec.HasTimedFuse)
            {
                return spec.Id == "he_40mm" ? DroneDropProjectileSpawnHeight : Mathf.Min(GetConfiguredDronePayloadSpawnHeight(), 10f);
            }

            var fuseAwareHeight = Math.Max(DroneDropMinimumSpawnHeight, (spec.FuseSeconds - DronePayloadGroundSettleSeconds) * PayloadDownwardVelocity);
            return Mathf.Clamp(Mathf.Min(GetConfiguredDronePayloadSpawnHeight(), (float)fuseAwareHeight), DroneDropMinimumSpawnHeight, DroneDropMaximumTimedSpawnHeight);
        }

        private Vector3 GetPlannedImpactPosition(AirstrikeCallContext context, int payloadIndex, int totalPayloads, Vector3 approach, float spreadRadius)
        {
            if (context != null)
            {
                Vector3 planned;
                if (context.PlannedImpactPositions.TryGetValue(payloadIndex, out planned))
                {
                    return planned;
                }
            }

            return GetPassAlignedImpactPosition(context, approach, payloadIndex, totalPayloads, spreadRadius);
        }

        private Vector3 GetPassAlignedImpactPosition(AirstrikeCallContext context, Vector3 approach, int payloadIndex, int totalPayloads, float spreadRadius)
        {
            var center = context == null || context.Target == null ? Vector3.zero : ResolveImpactPosition(context.Target.Position);
            approach.y = 0f;
            if (approach.sqrMagnitude <= 0.01f && context != null && context.PlannedDeliveryApproach.sqrMagnitude > 0.01f)
            {
                approach = context.PlannedDeliveryApproach;
            }
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            approach.Normalize();

            var right = GetRightVector(approach);
            var spread = Mathf.Clamp(spreadRadius, 0f, 100f);
            var t = totalPayloads <= 1 ? 0.5f : Mathf.Clamp01((payloadIndex - 1f) / (totalPayloads - 1f));
            var along = (t - 0.5f) * Mathf.Min(spread * 1.35f, 34f);
            var lateral = UnityEngine.Random.Range(-Mathf.Min(spread * 0.45f, 10f), Mathf.Min(spread * 0.45f, 10f));
            var forwardJitter = UnityEngine.Random.Range(-Mathf.Min(spread * 0.22f, 6f), Mathf.Min(spread * 0.22f, 6f));
            return center + (approach * (along + forwardJitter)) + (right * lateral);
        }

        private void StorePlannedImpactPosition(AirstrikeCallContext context, int payloadIndex, Vector3 position)
        {
            if (context == null)
            {
                return;
            }

            context.PlannedImpactPositions[payloadIndex] = ResolveImpactPosition(position);
        }

        private void BuildDronePayloadImpactPlan(AirstrikeCallContext context, int payloadCount, Vector3 approach, Vector3 target)
        {
            if (context == null || context.Strike == null)
            {
                return;
            }

            context.PlannedImpactPositions.Clear();
            context.PlannedDeliveryApproach = approach.sqrMagnitude <= 0.01f ? Vector3.forward : approach.normalized;
            var right = GetRightVector(context.PlannedDeliveryApproach);
            var spread = Mathf.Clamp(GetStrikeSpreadRadius(context.Strike), 0f, 100f);
            var loiterRadius = config?.DeliveryVisuals == null ? 7f : Mathf.Clamp(config.DeliveryVisuals.DroneDropLoiterRadius, 0f, 30f);
            var usableRadius = Mathf.Min(spread, Math.Max(1f, loiterRadius));
            var randomPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            for (var i = 1; i <= payloadCount; i++)
            {
                var t = payloadCount <= 1 ? 0.5f : (i - 1f) / (payloadCount - 1f);
                var weave = Mathf.Sin((t * Mathf.PI * 2f) + randomPhase) * usableRadius * 0.45f;
                var sweep = (t - 0.5f) * usableRadius * 1.35f;
                var randomAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var randomRadius = Mathf.Sqrt(UnityEngine.Random.value) * Mathf.Min(spread, usableRadius) * 0.45f;
                var jitter = new Vector3(Mathf.Cos(randomAngle) * randomRadius, 0f, Mathf.Sin(randomAngle) * randomRadius);
                var planned = target + (context.PlannedDeliveryApproach * sweep) + (right * weave) + jitter;
                StorePlannedImpactPosition(context, i, planned);
            }
        }

        private DeliveryFlightPlan BuildDroneErraticFlightPlan(AirstrikeCallContext context, Vector3 target, Vector3 approach, float distance, float height, int payloadCount, float payloadDelay, float firstPayloadDelay, float postReleaseDuration)
        {
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            if (firstPayloadDelay < 0.65f)
            {
                var directRelease = GetPlannedImpactPosition(context, 1, Math.Max(1, payloadCount), approach, GetStrikeSpreadRadius(context.Strike)) + (Vector3.up * height);
                return BuildDeliveryFlightPlan(directRelease, approach, distance, firstPayloadDelay, postReleaseDuration, 2f, 18f);
            }

            var safeFirstPayloadDelay = Mathf.Max(0.1f, firstPayloadDelay);
            var safePostReleaseDuration = Mathf.Max(DronePathMinimumLoiterSeconds, postReleaseDuration);
            var safeDuration = Mathf.Clamp(safeFirstPayloadDelay + safePostReleaseDuration, 2.5f, 22f);
            var right = GetRightVector(approach);
            var wobble = config?.DeliveryVisuals == null ? 4.5f : Mathf.Clamp(config.DeliveryVisuals.DroneErraticApproachRadius, 0f, 20f);
            var start = target - (approach * distance) + (right * UnityEngine.Random.Range(-wobble, wobble)) + (Vector3.up * (height + UnityEngine.Random.Range(1.5f, 4f)));
            var firstImpact = GetPlannedImpactPosition(context, 1, Math.Max(1, payloadCount), approach, GetStrikeSpreadRadius(context.Strike));
            var release = firstImpact + (Vector3.up * height);
            var end = target + (approach * Mathf.Max(25f, distance * 0.8f)) + (right * UnityEngine.Random.Range(-wobble, wobble)) + (Vector3.up * (height + 5f));
            var plan = CreateEmptyFlightPlan(start, release, end, approach, safeDuration, safeFirstPayloadDelay);

            AddFlightWaypoint(plan, start, 0f);
            AddFlightWaypoint(plan, target - (approach * distance * 0.55f) + (right * UnityEngine.Random.Range(-wobble, wobble)) + (Vector3.up * (height + UnityEngine.Random.Range(0.5f, 3f))), Mathf.Min(Mathf.Max(0.25f, safeFirstPayloadDelay * 0.38f), safeFirstPayloadDelay - 0.15f));
            AddFlightWaypoint(plan, target - (approach * distance * 0.18f) + (right * UnityEngine.Random.Range(-wobble, wobble)) + (Vector3.up * height), Mathf.Min(Mathf.Max(0.45f, safeFirstPayloadDelay * 0.72f), safeFirstPayloadDelay - 0.08f));

            for (var i = 1; i <= payloadCount; i++)
            {
                var time = safeFirstPayloadDelay + ((i - 1) * payloadDelay);
                if (time >= safeDuration - 0.15f)
                {
                    break;
                }

                var impact = GetPlannedImpactPosition(context, i, payloadCount, approach, GetStrikeSpreadRadius(context.Strike));
                var drift = right * UnityEngine.Random.Range(-Mathf.Max(1f, wobble * 0.45f), Mathf.Max(1f, wobble * 0.45f));
                AddFlightWaypoint(plan, impact + drift + (Vector3.up * height), time);
            }

            AddFlightWaypoint(plan, target + (approach * distance * 0.25f) + (right * UnityEngine.Random.Range(-wobble, wobble)) + (Vector3.up * (height + 1.5f)), Mathf.Clamp(safeFirstPayloadDelay + Math.Max(DronePathMinimumLoiterSeconds, payloadCount * payloadDelay), safeFirstPayloadDelay + 0.35f, safeDuration - 0.1f));
            AddFlightWaypoint(plan, end, safeDuration);
            FinalizeFlightPlan(plan);
            return plan;
        }

        private DeliveryFlightPlan BuildJetObservationStrikeFlightPlan(Vector3 target, Vector3 release, Vector3 approach, float inboundDistance, float firstPayloadDelay, float postReleaseDuration, float configuredHeight, float minimumDuration, float maximumDuration)
        {
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            if (firstPayloadDelay < 2f)
            {
                return BuildDeliveryFlightPlan(release, approach, inboundDistance, firstPayloadDelay, postReleaseDuration, minimumDuration, maximumDuration);
            }

            var right = GetRightVector(approach);
            var safeFirstPayloadDelay = Mathf.Max(0.1f, firstPayloadDelay);
            var safeDuration = Mathf.Clamp(safeFirstPayloadDelay + Mathf.Max(0.1f, postReleaseDuration), minimumDuration, maximumDuration);
            var strikeHeight = Mathf.Clamp(configuredHeight * config.DeliveryVisuals.AircraftStrikePassHeightMultiplier, 45f, configuredHeight * 1.15f);
            var highHeight = Mathf.Clamp(configuredHeight * config.DeliveryVisuals.AircraftObservationPassHeightMultiplier, configuredHeight + 25f, 320f);
            var exitHeight = Mathf.Clamp(Mathf.Max(highHeight * 0.92f, configuredHeight + 35f), configuredHeight + 20f, 340f);
            var lowRelease = new Vector3(release.x, target.y + strikeHeight, release.z);
            var lateralTrim = right * UnityEngine.Random.Range(-inboundDistance * 0.035f, inboundDistance * 0.035f);
            var start = lowRelease - (approach * inboundDistance * 1.05f) + lateralTrim + (Vector3.up * highHeight);
            var descentCommit = lowRelease - (approach * inboundDistance * 0.48f) + (lateralTrim * 0.45f) + (Vector3.up * Mathf.Lerp(highHeight, strikeHeight, 0.45f));
            var lowExit = target + (approach * Mathf.Max(70f, inboundDistance * 0.42f)) + (Vector3.up * Mathf.Lerp(strikeHeight, exitHeight, 0.34f));
            var end = target + (approach * Mathf.Max(170f, inboundDistance * 1.05f)) + (Vector3.up * exitHeight);
            var plan = CreateEmptyFlightPlan(start, lowRelease, end, approach, safeDuration, safeFirstPayloadDelay);
            var descentCommitTime = Mathf.Clamp(safeFirstPayloadDelay * 0.62f, 1.1f, safeFirstPayloadDelay - MinimumStrikePassLeadSeconds);
            var lowExitTime = Mathf.Clamp(safeFirstPayloadDelay + Math.Min(Mathf.Max(1.4f, postReleaseDuration * 0.38f), 4.6f), safeFirstPayloadDelay + 0.75f, safeDuration - 0.25f);

            AddFlightWaypoint(plan, start, 0f);
            AddFlightWaypoint(plan, descentCommit, descentCommitTime);
            AddFlightWaypoint(plan, lowRelease, safeFirstPayloadDelay);
            AddFlightWaypoint(plan, lowExit, lowExitTime);
            AddFlightWaypoint(plan, end, safeDuration);
            FinalizeFlightPlan(plan);
            return plan;
        }

        private DeliveryFlightPlan BuildCargoPlaneDropFlightPlan(Vector3 target, Vector3 approach, float inboundDistance, float firstPayloadDelay, float postReleaseDuration, float configuredHeight, float minimumDuration, float maximumDuration)
        {
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            var safeHeight = Mathf.Clamp(configuredHeight, 70f, 320f);
            var release = target + (Vector3.up * safeHeight);
            return BuildDeliveryFlightPlan(release, approach, inboundDistance, firstPayloadDelay, postReleaseDuration, minimumDuration, maximumDuration);
        }

        private DeliveryFlightPlan BuildDivingAttackFlightPlan(Vector3 target, Vector3 release, Vector3 approach, float inboundDistance, float firstPayloadDelay, float postReleaseDuration, float configuredHeight, float minimumDuration, float maximumDuration)
        {
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            if (firstPayloadDelay < 2f)
            {
                return BuildDeliveryFlightPlan(release, approach, inboundDistance, firstPayloadDelay, postReleaseDuration, minimumDuration, maximumDuration);
            }

            var right = GetRightVector(approach);
            var safeFirstPayloadDelay = Mathf.Max(0.1f, firstPayloadDelay);
            var safeDuration = Mathf.Clamp(safeFirstPayloadDelay + Mathf.Max(0.1f, postReleaseDuration), minimumDuration, maximumDuration);
            var strikeHeight = Mathf.Clamp(configuredHeight * config.DeliveryVisuals.AttackStrikePassHeightMultiplier, 32f, configuredHeight * 1.1f);
            var startHeight = Mathf.Clamp(configuredHeight * config.DeliveryVisuals.AttackDiveStartHeightMultiplier, configuredHeight + 18f, 280f);
            var exitHeight = Mathf.Clamp(configuredHeight * config.DeliveryVisuals.AttackExitHeightMultiplier, configuredHeight + 10f, 300f);
            var lowRelease = new Vector3(release.x, target.y + strikeHeight, release.z);
            var start = lowRelease - (approach * inboundDistance) + (right * UnityEngine.Random.Range(-inboundDistance * 0.08f, inboundDistance * 0.08f)) + (Vector3.up * startHeight);
            var diveCommit = Vector3.Lerp(start, lowRelease, 0.62f) + (right * UnityEngine.Random.Range(-inboundDistance * 0.05f, inboundDistance * 0.05f));
            var lowExit = target + (approach * Mathf.Max(45f, inboundDistance * 0.38f)) + (Vector3.up * strikeHeight);
            var end = target + (approach * Mathf.Max(100f, inboundDistance * 0.85f)) + (Vector3.up * exitHeight);
            var plan = CreateEmptyFlightPlan(start, lowRelease, end, approach, safeDuration, safeFirstPayloadDelay);
            var diveCommitTime = Mathf.Clamp(safeFirstPayloadDelay * 0.68f, 0.75f, safeFirstPayloadDelay - 0.18f);
            var lowExitTime = Mathf.Clamp(safeFirstPayloadDelay + Math.Min(Mathf.Max(1.1f, postReleaseDuration * 0.5f), 3.2f), safeFirstPayloadDelay + 0.55f, safeDuration - 0.2f);

            AddFlightWaypoint(plan, start, 0f);
            AddFlightWaypoint(plan, diveCommit, diveCommitTime);
            AddFlightWaypoint(plan, lowRelease, safeFirstPayloadDelay);
            AddFlightWaypoint(plan, lowExit, lowExitTime);
            AddFlightWaypoint(plan, end, safeDuration);
            FinalizeFlightPlan(plan);
            return plan;
        }

        private DeliveryFlightPlan BuildA10DivingStrafeFlightPlan(Vector3 target, Vector3 release, Vector3 approach, float inboundDistance, float firstPayloadDelay, float postReleaseDuration, float configuredHeight, float lineLength, float minimumDuration, float maximumDuration)
        {
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            if (firstPayloadDelay < 2f)
            {
                return BuildDeliveryFlightPlan(release, approach, inboundDistance, firstPayloadDelay, postReleaseDuration, minimumDuration, maximumDuration);
            }

            var right = GetRightVector(approach);
            var safeFirstPayloadDelay = Mathf.Max(0.1f, firstPayloadDelay);
            var safeDuration = Mathf.Clamp(safeFirstPayloadDelay + Mathf.Max(0.1f, postReleaseDuration), minimumDuration, maximumDuration);
            var strikeHeight = Mathf.Clamp(configuredHeight * config.DeliveryVisuals.AttackStrikePassHeightMultiplier, 48f, configuredHeight * 1.1f);
            var startHeight = Mathf.Clamp(configuredHeight * config.DeliveryVisuals.AttackDiveStartHeightMultiplier, configuredHeight + 25f, 330f);
            var exitHeight = Mathf.Clamp(configuredHeight * config.DeliveryVisuals.AttackExitHeightMultiplier, configuredHeight + 20f, 340f);
            var lowRelease = new Vector3(release.x, target.y + strikeHeight, release.z);
            var lineEnd = target + (approach * (lineLength * 0.5f)) + (Vector3.up * strikeHeight);
            var start = lowRelease - (approach * inboundDistance) + (right * UnityEngine.Random.Range(-inboundDistance * 0.06f, inboundDistance * 0.06f)) + (Vector3.up * startHeight);
            var diveCommit = Vector3.Lerp(start, lowRelease, 0.68f) + (Vector3.up * 8f);
            var end = lineEnd + (approach * Mathf.Max(120f, inboundDistance * 0.9f)) + (Vector3.up * exitHeight);
            var plan = CreateEmptyFlightPlan(start, lowRelease, end, approach, safeDuration, safeFirstPayloadDelay);
            var diveCommitTime = Mathf.Clamp(safeFirstPayloadDelay * 0.70f, 1f, safeFirstPayloadDelay - 0.22f);
            var lineEndTime = Mathf.Clamp(safeFirstPayloadDelay + Math.Min(Mathf.Max(1.2f, postReleaseDuration * 0.85f), 4.2f), safeFirstPayloadDelay + 0.75f, safeDuration - 0.3f);

            AddFlightWaypoint(plan, start, 0f);
            AddFlightWaypoint(plan, diveCommit, diveCommitTime);
            AddFlightWaypoint(plan, lowRelease, safeFirstPayloadDelay);
            AddFlightWaypoint(plan, lineEnd, lineEndTime);
            AddFlightWaypoint(plan, end, safeDuration);
            FinalizeFlightPlan(plan);
            return plan;
        }

        private float GetAircraftStrikePassHeight(StrikeDefinition strike, DeliveryVisualProfile profile, float configuredHeight)
        {
            var height = Mathf.Max(1f, configuredHeight);
            if (string.Equals(strike?.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp(height * config.DeliveryVisuals.AttackStrikePassHeightMultiplier, 32f, height * 1.1f);
            }

            if (profile == DeliveryVisualProfile.A10 || string.Equals(strike?.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp(height * config.DeliveryVisuals.AttackStrikePassHeightMultiplier, 48f, height * 1.1f);
            }

            return Mathf.Clamp(height * config.DeliveryVisuals.AircraftStrikePassHeightMultiplier, 45f, height * 1.15f);
        }

        private Vector3 GetAircraftReleasePoint(DeliveryVisualProfile profile, Vector3 approach, float height, Vector3 target)
        {
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            switch (profile)
            {
                case DeliveryVisualProfile.RocketRun:
                    return target - (approach * RocketRunSpawnDistance) + (Vector3.up * height);

                case DeliveryVisualProfile.HomingMissile:
                    return target - (approach * HomingMissileLaunchDistance) + (Vector3.up * height);

                default:
                    return target + (Vector3.up * height);
            }
        }

        private DeliveryFlightPlan ApplyVisualProfileFlightPlan(AirstrikeCallContext context, string vehicle, DeliveryVisualProfile deliveryProfile, Vector3 target, Vector3 approach, float firstPayloadDelay, float postReleaseDuration, DeliveryFlightPlan fallbackPlan, string label)
        {
            string profileId;
            VisualProfileConfig profile;
            var normalizedVehicle = NormalizeVisualProfileVehicle(vehicle, context == null ? null : context.Strike, null, deliveryProfile);
            if ((visualProfileFile == null || visualProfileFile.Profiles == null || visualProfileFile.Profiles.Count == 0) && File.Exists(ResolveVisualProfilesDataPath()))
            {
                LoadVisualProfiles();
            }

            if (string.IsNullOrWhiteSpace(normalizedVehicle) || !TryGetVisualProfile(context, normalizedVehicle, deliveryProfile, out profileId, out profile))
            {
                return fallbackPlan;
            }

            var plan = BuildVisualProfileFlightPlan(profile, normalizedVehicle, target, approach);
            if (plan == null)
            {
                return fallbackPlan;
            }

            if (config?.General != null && config.General.DebugMode && context?.Strike != null)
            {
                Puts(context.Strike.Id + " using visual waypoint profile '" + profileId + "' for " + normalizedVehicle + " (" + label + ", motion=" + GetVisualProfileMotionMode(profileId) + ", releases=" + GetVisualProfileReleaseMode(profileId) + ").");
            }

            return plan;
        }

        private bool TryGetVisualProfile(AirstrikeCallContext context, string vehicle, DeliveryVisualProfile deliveryProfile, out string profileId, out VisualProfileConfig profile)
        {
            profileId = "";
            profile = null;

            if (visualProfileFile == null || visualProfileFile.Profiles == null || visualProfileFile.Profiles.Count == 0)
            {
                return false;
            }

            var strike = context == null ? null : context.Strike;
            if (!string.IsNullOrWhiteSpace(strike?.VisualProfileId))
            {
                var explicitId = strike.VisualProfileId.Trim();
                VisualProfileConfig explicitProfile;
                if (visualProfileFile.Profiles.TryGetValue(explicitId, out explicitProfile) && explicitProfile != null)
                {
                    if (IsVisualProfileVehicleMatch(explicitProfile, vehicle))
                    {
                        profileId = explicitId;
                        profile = explicitProfile;
                        return true;
                    }

                    PrintWarning("Strike '" + strike.Id + "' explicit visual profile '" + explicitId + "' uses vehicle '" + explicitProfile.Vehicle + "' but needs '" + vehicle + "'; falling back to compatible profile lookup.");
                }
                else
                {
                    PrintWarning("Strike '" + strike.Id + "' explicit visual profile '" + explicitId + "' was not found; falling back to compatible profile lookup.");
                }
            }

            var candidates = GetVisualProfileCandidateIds(context, vehicle, deliveryProfile);
            foreach (var candidate in candidates)
            {
                VisualProfileConfig found;
                if (!visualProfileFile.Profiles.TryGetValue(candidate, out found) || found == null)
                {
                    continue;
                }

                if (!IsVisualProfileVehicleMatch(found, vehicle))
                {
                    continue;
                }

                profileId = candidate;
                profile = found;
                return true;
            }

            return false;
        }

        private List<string> GetVisualProfileCandidateIds(AirstrikeCallContext context, string vehicle, DeliveryVisualProfile deliveryProfile)
        {
            var candidates = new List<string>();
            var strike = context == null ? null : context.Strike;
            if (strike != null)
            {
                AddVisualProfileCandidate(candidates, strike.Id);
                AddVisualProfileCandidate(candidates, strike.Id + "_" + vehicle);
                AddVisualProfileCandidate(candidates, strike.Delivery + "_" + strike.Payload);
                AddVisualProfileCandidate(candidates, strike.Delivery + "_" + vehicle);
                AddVisualProfileCandidate(candidates, strike.Payload + "_" + vehicle);
                AddVisualProfileCandidate(candidates, strike.Payload);
                AddVisualProfileCandidate(candidates, strike.Delivery);
            }

            AddVisualProfileCandidate(candidates, GetDefaultVisualProfileId(vehicle, deliveryProfile));
            return candidates;
        }

        private void AddVisualProfileCandidate(List<string> candidates, string candidate)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            candidate = candidate.Trim();
            foreach (var existing in candidates)
            {
                if (string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            candidates.Add(candidate);
        }

        private string GetDefaultVisualProfileId(string vehicle, DeliveryVisualProfile deliveryProfile)
        {
            switch ((vehicle ?? "").Trim().ToLowerInvariant())
            {
                case "drone":
                    return "drone_grenade_drop";
                case "cargo_plane":
                    return "cargo_heavy_drop";
                case "attack_heli":
                    return "attack_heli_rocket_run";
                case "a10":
                    return "a10_strafe_run";
                default:
                    return "jet_mlrs_run";
            }
        }

        private bool IsVisualProfileVehicleMatch(VisualProfileConfig profile, string vehicle)
        {
            var profileVehicle = NormalizeVisualProfileVehicle(profile == null ? null : profile.Vehicle, null, null, DeliveryVisualProfile.Mlrs);
            return !string.IsNullOrWhiteSpace(profileVehicle)
                && string.Equals(profileVehicle, vehicle, StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeVisualProfileVehicle(string vehicle, StrikeDefinition strike, string prefab, DeliveryVisualProfile deliveryProfile)
        {
            var value = (vehicle ?? "").Trim().ToLowerInvariant();
            switch (value)
            {
                case "drone":
                    return "drone";
                case "cargo":
                case "cargo_plane":
                case "cargo-plane":
                case "heavy_drop":
                    return "cargo_plane";
                case "attack_heli":
                case "attack-heli":
                case "patrol_heli":
                case "patrol-heli":
                case "heli":
                    return "attack_heli";
                case "a10":
                case "a-10":
                case "a10_gun_run":
                    return "a10";
                case "f15":
                case "f-15":
                case "jet":
                case "mlrs":
                case "cargo_plane_jet":
                    return "f15";
            }

            if (string.Equals(prefab, DroneVisualPrefab, StringComparison.OrdinalIgnoreCase))
            {
                return "drone";
            }

            if (string.Equals(prefab, CargoPlaneVisualPrefab, StringComparison.OrdinalIgnoreCase))
            {
                return "cargo_plane";
            }

            if (string.Equals(prefab, PatrolHelicopterVisualPrefab, StringComparison.OrdinalIgnoreCase))
            {
                return "attack_heli";
            }

            if (string.Equals(prefab, F15VisualPrefab, StringComparison.OrdinalIgnoreCase))
            {
                return deliveryProfile == DeliveryVisualProfile.A10 ? "a10" : "f15";
            }

            if (strike != null)
            {
                if (string.Equals(strike.Delivery, "drone", StringComparison.OrdinalIgnoreCase))
                {
                    return "drone";
                }

                if (string.Equals(strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase))
                {
                    return "attack_heli";
                }

                if (string.Equals(strike.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase))
                {
                    return "a10";
                }
            }

            if (deliveryProfile == DeliveryVisualProfile.HeavyDrop)
            {
                return "cargo_plane";
            }

            if (deliveryProfile == DeliveryVisualProfile.A10)
            {
                return "a10";
            }

            return "f15";
        }

        private string GetAircraftVisualProfileVehicle(StrikeDefinition strike, DeliveryVisualProfile deliveryProfile, string prefab)
        {
            if (string.Equals(prefab, CargoPlaneVisualPrefab, StringComparison.OrdinalIgnoreCase))
            {
                return "cargo_plane";
            }

            if (string.Equals(prefab, PatrolHelicopterVisualPrefab, StringComparison.OrdinalIgnoreCase))
            {
                return "attack_heli";
            }

            if (deliveryProfile == DeliveryVisualProfile.A10 || string.Equals(strike?.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase))
            {
                return "a10";
            }

            return "f15";
        }

        private DeliveryFlightPlan BuildVisualProfileFlightPlan(VisualProfileConfig profile, string vehicle, Vector3 target, Vector3 approach)
        {
            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count < 2)
            {
                return null;
            }

            approach.y = 0f;
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            CompiledVisualTrack compiledTrack = null;
            string compiledTrackError = "";
            if (visualProfileFile != null
                && visualProfileFile.SchemaVersion >= 2
                && TryValidateCompiledTrack(profile, out compiledTrack, out compiledTrackError))
            {
                return BuildCompiledVisualProfileFlightPlan(profile, compiledTrack, vehicle, target, approach);
            }

            var authoredDuration = Mathf.Clamp(profile.DurationSeconds <= 0f ? profile.Waypoints[profile.Waypoints.Count - 1].Time : profile.DurationSeconds, 0.5f, 120f);
            var authoredPayloadDelay = Mathf.Clamp(profile.FirstPayloadDelaySeconds < 0f ? 0f : profile.FirstPayloadDelaySeconds, 0f, authoredDuration);
            var plan = CreateEmptyFlightPlan(Vector3.zero, Vector3.zero, Vector3.zero, approach, authoredDuration, authoredPayloadDelay);
            plan.StopAtWaypoints = profile.StopAtWaypoints;
            plan.RotationSmoothTimeSeconds = Mathf.Clamp(profile.RotationSmoothTimeSeconds <= 0f ? 0.12f : profile.RotationSmoothTimeSeconds, 0.02f, 1.5f);
            var clearance = GetVisualProfileTerrainClearance(profile, vehicle);
            plan.TerrainClearance = clearance;

            foreach (var waypoint in profile.Waypoints)
            {
                if (waypoint == null)
                {
                    continue;
                }

                var time = Mathf.Clamp(waypoint.Time, 0f, authoredDuration);
                var rotationOffset = Quaternion.Euler(waypoint.RotationX, waypoint.RotationY, waypoint.RotationZ);
                AddFlightWaypoint(plan, EnsurePositionAboveTerrain(VisualProfileLocalToWorld(target, approach, waypoint), clearance), time, rotationOffset);
            }

            FinalizeFlightPlan(plan);
            Vector3 releasePosition;
            Vector3 releaseDirection;
            Vector3 releaseVelocity;
            EvaluateFlightPlan(plan, plan.FirstPayloadDelay, out releasePosition, out releaseDirection, out releaseVelocity);
            plan.Release = EnsurePositionAboveTerrain(releasePosition, clearance);

            var direction = plan.End - plan.Start;
            direction.y = 0f;
            plan.Direction = direction.sqrMagnitude > 0.01f ? direction.normalized : approach;
            return plan;
        }

        private DeliveryFlightPlan BuildCompiledVisualProfileFlightPlan(VisualProfileConfig profile, CompiledVisualTrack track, string vehicle, Vector3 target, Vector3 approach)
        {
            if (profile == null || track == null || track.Frames == null || track.Frames.Count < 2)
            {
                return null;
            }

            var duration = track.DurationSeconds;
            var payloadDelay = Mathf.Clamp(profile.FirstPayloadDelaySeconds, 0f, duration);
            var plan = CreateEmptyFlightPlan(Vector3.zero, Vector3.zero, Vector3.zero, approach, duration, payloadDelay);
            plan.UsesCompiledTrack = true;
            plan.StopAtWaypoints = false;
            plan.RotationSmoothTimeSeconds = 0f;
            var clearance = GetVisualProfileTerrainClearance(profile, vehicle);
            plan.TerrainClearance = clearance;

            var right = Vector3.Cross(Vector3.up, approach);
            if (right.sqrMagnitude <= 0.01f)
            {
                right = Vector3.right;
            }
            else
            {
                right.Normalize();
            }

            var basisRotation = Quaternion.LookRotation(approach, Vector3.up);
            Quaternion? previousLocalRotation = null;
            foreach (var frame in track.Frames)
            {
                var localRotation = NormalizeQuaternion(new Quaternion(frame.Qx, frame.Qy, frame.Qz, frame.Qw));
                if (previousLocalRotation.HasValue && Quaternion.Dot(previousLocalRotation.Value, localRotation) < 0f)
                {
                    localRotation = new Quaternion(-localRotation.x, -localRotation.y, -localRotation.z, -localRotation.w);
                }

                previousLocalRotation = localRotation;
                var worldPosition = target
                    + (right * frame.X)
                    + (Vector3.up * frame.Y)
                    + (approach * frame.Z);
                plan.CompiledFrames.Add(new CompiledRuntimeFrame
                {
                    Time = frame.Time,
                    Position = EnsurePositionAboveTerrain(worldPosition, clearance),
                    Rotation = basisRotation * localRotation
                });
            }

            plan.Start = plan.CompiledFrames[0].Position;
            plan.End = plan.CompiledFrames[plan.CompiledFrames.Count - 1].Position;
            AddFlightWaypoint(plan, plan.Start, 0f);
            AddFlightWaypoint(plan, plan.End, duration);
            FinalizeFlightPlan(plan);

            Vector3 releasePosition;
            Vector3 releaseDirection;
            Vector3 releaseVelocity;
            EvaluateFlightPlan(plan, payloadDelay, out releasePosition, out releaseDirection, out releaseVelocity);
            plan.Release = EnsurePositionAboveTerrain(releasePosition, clearance);
            var direction = plan.End - plan.Start;
            direction.y = 0f;
            plan.Direction = direction.sqrMagnitude > 0.01f ? direction.normalized : approach;
            return plan;
        }

        private Quaternion NormalizeQuaternion(Quaternion value)
        {
            var magnitude = Mathf.Sqrt((value.x * value.x) + (value.y * value.y) + (value.z * value.z) + (value.w * value.w));
            if (magnitude <= 0.000001f)
            {
                return Quaternion.identity;
            }

            var inverse = 1f / magnitude;
            return new Quaternion(value.x * inverse, value.y * inverse, value.z * inverse, value.w * inverse);
        }

        private Vector3 VisualProfileLocalToWorld(Vector3 target, Vector3 approach, VisualProfileWaypoint waypoint)
        {
            if (waypoint == null)
            {
                return target;
            }

            approach.y = 0f;
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            var right = GetRightVector(approach);
            return target + (right * waypoint.X) + (Vector3.up * waypoint.Y) + (approach * waypoint.Z);
        }

        private float GetVisualProfileTerrainClearance(VisualProfileConfig profile, string vehicle)
        {
            var fallback = string.Equals(vehicle, "drone", StringComparison.OrdinalIgnoreCase)
                ? DefaultVisualProfileDroneTerrainClearance
                : DefaultVisualProfileAircraftTerrainClearance;
            return Mathf.Clamp(profile == null || profile.MinimumTerrainClearance <= 0f ? fallback : profile.MinimumTerrainClearance, 0f, 250f);
        }

        private float GetFlightPlanTerrainClearance(DeliveryFlightPlan plan, StrikeDefinition strike, string label)
        {
            return plan != null && plan.TerrainClearance >= 0f
                ? Mathf.Clamp(plan.TerrainClearance, 0f, 250f)
                : GetVisualTerrainClearance(strike, label);
        }

        private void StartDroneDeliveryVisual(AirstrikeCallContext context, int payloadCount, float payloadDelay, float finishDelay, float initialPayloadDelay, float postReleaseDurationOverride = -1f)
        {
            if (!ShouldSpawnDeliveryVisual(context) || !config.DeliveryVisuals.SpawnDroneVisuals)
            {
                return;
            }

            var approach = GetRocketApproachDirection(context);
            var target = ResolveImpactPosition(context.Target.Position);
            var distance = Mathf.Clamp(config.DeliveryVisuals.DroneFlyoverDistance, 15f, 150f);
            var height = Mathf.Clamp(config.DeliveryVisuals.DroneFlyoverHeight, 8f, 80f);
            var postReleaseDuration = postReleaseDurationOverride >= 0f
                ? postReleaseDurationOverride
                : ((Math.Max(1, payloadCount) - 1) * payloadDelay) + finishDelay;
            BuildDronePayloadImpactPlan(context, Math.Max(1, payloadCount), approach, target);
            var plan = BuildDroneErraticFlightPlan(context, target, approach, distance, height, Math.Max(1, payloadCount), payloadDelay, initialPayloadDelay, postReleaseDuration);
            plan = ApplyVisualProfileFlightPlan(context, "drone", DeliveryVisualProfile.RocketRun, target, approach, initialPayloadDelay, postReleaseDuration, plan, "drone flyover");

            StartVisualFlyover(context, DroneVisualPrefab, plan, "drone flyover");
        }

        private void StartAircraftDeliveryVisual(AirstrikeCallContext context, DeliveryVisualProfile profile, float firstPayloadDelay, float postReleaseDuration, string label)
        {
            if (!ShouldSpawnDeliveryVisual(context) || !config.DeliveryVisuals.SpawnAircraftVisuals)
            {
                return;
            }

            string prefab;
            float height;
            if (!TryGetAircraftVisualPrefab(context.Strike, profile, out prefab, out height))
            {
                return;
            }

            var approach = GetRocketApproachDirection(context);
            var target = ResolveImpactPosition(context.Target.Position);
            var distance = Mathf.Clamp(config.DeliveryVisuals.AircraftFlyoverDistance, 60f, 500f);
            var heavyCargoDrop = profile == DeliveryVisualProfile.HeavyDrop
                && string.Equals(prefab, CargoPlaneVisualPrefab, StringComparison.OrdinalIgnoreCase);
            var strikeHeight = heavyCargoDrop ? height : GetAircraftStrikePassHeight(context.Strike, profile, height);
            var release = GetAircraftReleasePoint(profile, approach, strikeHeight, target);
            context.PlannedDeliveryApproach = approach.sqrMagnitude <= 0.01f ? Vector3.forward : approach.normalized;
            DeliveryFlightPlan plan;
            if (heavyCargoDrop)
            {
                plan = BuildCargoPlaneDropFlightPlan(target, approach, distance * 1.05f, firstPayloadDelay, postReleaseDuration, height, 6f, 45f);
            }
            else if (string.Equals(context.Strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase))
            {
                plan = BuildDivingAttackFlightPlan(target, release, approach, distance * 0.9f, firstPayloadDelay, postReleaseDuration, height, 4f, 38f);
            }
            else
            {
                plan = BuildJetObservationStrikeFlightPlan(target, release, approach, distance, firstPayloadDelay, postReleaseDuration, height, 5f, 42f);
            }

            var profileVehicle = GetAircraftVisualProfileVehicle(context.Strike, profile, prefab);
            plan = ApplyVisualProfileFlightPlan(context, profileVehicle, profile, target, approach, firstPayloadDelay, postReleaseDuration, plan, label);
            StartVisualFlyover(context, prefab, plan, label);
        }

        private void StartMlrsDeliveryVisual(AirstrikeCallContext context, Vector3 approach, float firstPayloadDelay, float postReleaseDuration)
        {
            if (!ShouldSpawnDeliveryVisual(context) || !config.DeliveryVisuals.SpawnAircraftVisuals)
            {
                return;
            }

            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = GetRocketApproachDirection(context);
            }

            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }

            approach.Normalize();
            context.PlannedDeliveryApproach = approach;
            var target = ResolveImpactPosition(context.Target.Position);
            var distance = Mathf.Clamp(config.DeliveryVisuals.AircraftFlyoverDistance * 0.95f, 100f, 420f);
            var height = Mathf.Clamp(config.DeliveryVisuals.MlrsAircraftFlyoverHeight, 35f, 200f);
            var strikeHeight = GetAircraftStrikePassHeight(context.Strike, DeliveryVisualProfile.Mlrs, height);
            var release = target - (approach * MlrsRocketSpawnDistance) + (Vector3.up * strikeHeight);
            var plan = BuildJetObservationStrikeFlightPlan(target, release, approach, distance, firstPayloadDelay, postReleaseDuration, height, 4f, 40f);
            plan = ApplyVisualProfileFlightPlan(context, "f15", DeliveryVisualProfile.Mlrs, target, approach, firstPayloadDelay, postReleaseDuration, plan, "F-15 MLRS flyover");

            StartVisualFlyover(context, F15VisualPrefab, plan, "F-15 MLRS flyover");
        }

        private void StartA10DeliveryVisual(AirstrikeCallContext context, Vector3 direction, int burstCount, float pulseDelay, float initialPayloadDelay, float postReleaseDurationOverride = -1f)
        {
            if (!ShouldSpawnDeliveryVisual(context) || !config.DeliveryVisuals.SpawnAircraftVisuals)
            {
                return;
            }

            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = GetA10StrafeDirection(context);
            }

            var target = ResolveImpactPosition(context.Target.Position);
            var distance = Mathf.Clamp(config.DeliveryVisuals.AircraftFlyoverDistance * 1.35f, 120f, 500f);
            var height = Mathf.Clamp(config.DeliveryVisuals.A10FlyoverHeight, 25f, 220f);
            var lineLength = Mathf.Clamp(context.Strike.LineLength <= 0f ? 55f : context.Strike.LineLength, 5f, 200f);
            direction.Normalize();
            context.PlannedDeliveryApproach = direction;
            var postReleaseDuration = postReleaseDurationOverride >= 0f
                ? postReleaseDurationOverride
                : ((Math.Max(1, burstCount) - 1) * pulseDelay) + A10FinishPaddingSeconds;
            var strikeHeight = GetAircraftStrikePassHeight(context.Strike, DeliveryVisualProfile.A10, height);
            var release = target - (direction * (lineLength * 0.5f)) + (Vector3.up * strikeHeight);
            var plan = BuildA10DivingStrafeFlightPlan(target, release, direction, distance, initialPayloadDelay, postReleaseDuration, height, lineLength, 3.5f, 32f);
            plan = ApplyVisualProfileFlightPlan(context, "a10", DeliveryVisualProfile.A10, target, direction, initialPayloadDelay, postReleaseDuration, plan, "A-10 F-15 flyover");

            StartVisualFlyover(context, F15VisualPrefab, plan, "A-10 F-15 flyover");
        }

        private void StartMortarArtilleryVisual(AirstrikeCallContext context, int shellCount)
        {
            if (!ShouldSpawnDeliveryVisual(context) || !config.DeliveryVisuals.SpawnMortarArtilleryVisuals)
            {
                return;
            }

            var approach = GetRocketApproachDirection(context);
            var target = ResolveImpactPosition(context.Target.Position);
            var distance = Mathf.Clamp(config.DeliveryVisuals.MortarSourceDistance, 25f, 250f);
            var source = ResolveImpactPosition(target - (approach * distance));
            var lookDirection = target - source;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude <= 0.01f)
            {
                lookDirection = approach;
            }

            lookDirection.Normalize();
            var rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            context.MortarSourcePosition = source;
            context.HasMortarSourcePosition = true;

            string error;
            var mortar = SpawnTrackedVisualEntity(context, MortarVisualPrefab, source + (Vector3.up * 0.15f), rotation, "mortar source", out error);
            if (mortar == null)
            {
                PrintVisualWarning(context, "mortar source", error);
            }
            else
            {
                RunSafeEffect(MortarDeployEffect, source + (Vector3.up * 0.25f), "mortar deploy");
            }

            if (!config.DeliveryVisuals.SpawnMortarCrewNpc)
            {
                return;
            }

            var right = new Vector3(-lookDirection.z, 0f, lookDirection.x);
            if (right.sqrMagnitude <= 0.01f)
            {
                right = Vector3.right;
            }
            else
            {
                right.Normalize();
            }

            var crewOffset = Mathf.Clamp(config.DeliveryVisuals.MortarCrewOffset, 1f, 8f);
            var crewPosition = ResolveImpactPosition(source - (lookDirection * 1.8f) + (right * crewOffset));
            var crew = SpawnTrackedVisualEntity(context, MortarCrewNpcPrefab, crewPosition, rotation, "artillery crew NPC", out error);
            if (crew == null)
            {
                PrintVisualWarning(context, "artillery crew NPC", error);
                return;
            }

            PrepareVisualCrewNpc(crew, "Raidlands Artillery", crewPosition);
            if (config.General.DebugMode)
            {
                Puts(context.Strike.Id + " spawned mortar visual source with crew for " + shellCount + " shell(s) at " + FormatPosition(source) + ".");
            }
        }

        private bool ShouldSpawnDeliveryVisual(AirstrikeCallContext context)
        {
            return context != null
                && context.Strike != null
                && context.Target != null
                && config?.DeliveryVisuals != null
                && config.DeliveryVisuals.Enabled;
        }

        private bool TryGetAircraftVisualPrefab(StrikeDefinition strike, DeliveryVisualProfile profile, out string prefab, out float height)
        {
            prefab = "";
            height = 0f;
            if (strike == null)
            {
                return false;
            }

            if (profile == DeliveryVisualProfile.HeavyDrop)
            {
                // Heavy bee, firebomb, and propane drops are visualized as airdrop-plane passes, not jet strafes.
                prefab = CargoPlaneVisualPrefab;
                height = Mathf.Clamp(config.DeliveryVisuals.CargoPlaneFlyoverHeight, 55f, 320f);
                return true;
            }

            if (profile == DeliveryVisualProfile.A10
                || string.Equals(strike.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase))
            {
                prefab = F15VisualPrefab;
                height = Mathf.Clamp(config.DeliveryVisuals.A10FlyoverHeight, 25f, 220f);
                return true;
            }

            MlrsPayloadSpec mlrsSpec;
            if (profile == DeliveryVisualProfile.Mlrs || TryGetMlrsPayloadSpec(strike.Payload, out mlrsSpec))
            {
                prefab = F15VisualPrefab;
                height = Mathf.Clamp(config.DeliveryVisuals.MlrsAircraftFlyoverHeight, 35f, 200f);
                return true;
            }

            if (string.Equals(strike.Delivery, "attack_heli", StringComparison.OrdinalIgnoreCase))
            {
                prefab = PatrolHelicopterVisualPrefab;
                height = Mathf.Clamp(config.DeliveryVisuals.AttackHeliFlyoverHeight, 20f, 180f);
                return true;
            }

            if (string.Equals(strike.Delivery, "cargo_plane_jet", StringComparison.OrdinalIgnoreCase))
            {
                prefab = F15VisualPrefab;
                height = Mathf.Clamp(config.DeliveryVisuals.CargoPlaneFlyoverHeight, 35f, 260f);
                return true;
            }

            return false;
        }

        private void StartVisualFlyover(AirstrikeCallContext context, string prefab, DeliveryFlightPlan plan, string label)
        {
            if (plan == null)
            {
                return;
            }

            ApplyTerrainClearanceToFlightPlan(plan, GetFlightPlanTerrainClearance(plan, context == null ? null : context.Strike, label));

            if (string.Equals(prefab, CargoPlaneVisualPrefab, StringComparison.OrdinalIgnoreCase) && !plan.UsesCompiledTrack)
            {
                StartCargoPlaneFlyover(context, plan, label);
                return;
            }

            if (string.Equals(prefab, PatrolHelicopterVisualPrefab, StringComparison.OrdinalIgnoreCase))
            {
                StartPatrolHelicopterFlyover(context, plan, label);
                return;
            }

            StartScriptedVisualFlyover(context, prefab, plan, label);
        }

        private void StartCargoPlaneFlyover(AirstrikeCallContext context, DeliveryFlightPlan plan, string label)
        {
            if (context == null || plan == null)
            {
                return;
            }

            var phase = "create";
            CargoPlane plane = null;

            try
            {
                var direction = plan.End - plan.Start;
                if (direction.sqrMagnitude <= 0.01f)
                {
                    direction = plan.Direction.sqrMagnitude <= 0.01f ? Vector3.forward : plan.Direction;
                }

                var rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                var safeDuration = Math.Max(0.1f, plan.Duration);

                phase = "create cargo plane prefab";
                plane = GameManager.server.CreateEntity(CargoPlaneVisualPrefab, Vector3.zero, Quaternion.identity, true) as CargoPlane;
                if (plane == null)
                {
                    PrintVisualWarning(context, label, "create cargo plane prefab failed: Could not create prefab '" + CargoPlaneVisualPrefab + "'.");
                    return;
                }

                phase = "configure cargo plane ownership";
                plane.OwnerID = context.CallerUserId;
                plane.EnableSaving(false);

                phase = "configure cargo plane drop state";
                plane.dropped = true;
                plane.InitDropPosition(plan.Release);
                plane.dropped = true;

                phase = "configure cargo plane networking";
                var networkable = plane as BaseNetworkable;
                if (networkable.net == null)
                {
                    networkable.net = Network.Net.sv.CreateNetworkable();
                }

                networkable.limitNetworking = true;

                phase = "spawn cargo plane";
                if ((int)plane.creationFrame == 0)
                {
                    plane.Spawn();
                }

                phase = "configure cargo plane route";
                plane.transform.position = plan.Start;
                plane.transform.rotation = rotation;
                plane.startPos = plan.Start;
                plane.endPos = plan.End;
                plane.secondsToTake = safeDuration;
                plane.secondsTaken = 0f;
                networkable.limitNetworking = false;
                plane.SendNetworkUpdateImmediate();

                context.VisualEntities.Add(plane);
                IncrementStat("visual_spawned");
                IncrementStat("visual_spawned_" + SanitizeStatKey(label));
                RegisterDeliveryCarrier(context, plane, label);
                RunFlyoverSoundCues(context, CargoPlaneVisualPrefab, plan.Start, plan.End, safeDuration, label);
                ScheduleCallTimer(context, safeDuration + 0.25f, () =>
                {
                    if (IsCallActive(context))
                    {
                        KillTrackedVisualEntity(context, plane);
                    }
                });

                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " spawned cargo-plane visual " + label + " from " + FormatPosition(plan.Start) + " to " + FormatPosition(plan.End) + " with release near " + FormatPosition(plan.Release) + ".");
                }
            }
            catch (Exception ex)
            {
                if (plane != null && !plane.IsDestroyed)
                {
                    plane.Kill(BaseNetworkable.DestroyMode.None);
                }

                PrintVisualWarning(context, label, phase + " failed: " + ex.Message);
            }
        }

        private void StartPatrolHelicopterFlyover(AirstrikeCallContext context, DeliveryFlightPlan plan, string label)
        {
            if (context == null || plan == null)
            {
                return;
            }

            var phase = "create";
            PatrolHelicopter patrolHeli = null;

            try
            {
                var direction = GetPlanDirectionAt(plan, 0f);
                if (direction.sqrMagnitude <= 0.01f)
                {
                    direction = plan.Direction.sqrMagnitude <= 0.01f ? Vector3.forward : plan.Direction;
                }

                var rotation = GetFlightPlanTargetRotation(plan, 0f, direction);
                var safeDuration = Math.Max(0.1f, plan.Duration);
                var velocity = GetPlanVelocityAt(plan, 0f);

                phase = "create patrol helicopter prefab";
                patrolHeli = GameManager.server.CreateEntity(PatrolHelicopterVisualPrefab, plan.Start, rotation, true) as PatrolHelicopter;
                if (patrolHeli == null)
                {
                    PrintVisualWarning(context, label, "create patrol helicopter prefab failed: Could not create prefab '" + PatrolHelicopterVisualPrefab + "'.");
                    return;
                }

                phase = "configure patrol helicopter visual";
                patrolHeli.OwnerID = context.CallerUserId;
                patrolHeli.EnableSaving(false);

                phase = "configure patrol helicopter networking";
                var networkable = patrolHeli as BaseNetworkable;
                if (networkable.net == null)
                {
                    networkable.net = Network.Net.sv.CreateNetworkable();
                }

                phase = "spawn patrol helicopter";
                patrolHeli.Spawn();

                phase = "configure spawned patrol helicopter visual";
                ConfigurePatrolHelicopterVisual(patrolHeli, true);

                phase = "prepare patrol helicopter route";
                TrySetCreatorEntity(patrolHeli, GetCallPlayer(context), label);
                context.VisualEntities.Add(patrolHeli);
                IncrementStat("visual_spawned");
                IncrementStat("visual_spawned_" + SanitizeStatKey(label));
                PreparePatrolHelicopterVisualEntity(patrolHeli, velocity);
                RegisterDeliveryCarrier(context, patrolHeli, label);
                MoveVisualEntity(patrolHeli, plan.Start, rotation, velocity, true);
                RunFlyoverSoundCues(context, PatrolHelicopterVisualPrefab, plan, label);
                ScheduleVisualFlyoverStep(context, patrolHeli, plan, GetPreciseNow(), GetVisualMoveIntervalSeconds(), label);

                if (config.General.DebugMode)
                {
                    var strikeId = context.Strike == null ? "unknown" : context.Strike.Id;
                    Puts(strikeId + " spawned patrol-helicopter visual " + label + " from " + FormatPosition(plan.Start) + " to " + FormatPosition(plan.End) + " with release near " + FormatPosition(plan.Release) + ".");
                }
            }
            catch (Exception ex)
            {
                if (patrolHeli != null && !patrolHeli.IsDestroyed)
                {
                    patrolHeli.Kill(BaseNetworkable.DestroyMode.None);
                }

                PrintVisualWarning(context, label, phase + " failed: " + ex.Message);
            }
        }

        private void StartVisualFlyover(AirstrikeCallContext context, string prefab, Vector3 start, Vector3 end, float duration, string label)
        {
            var direction = end - start;
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = Vector3.forward;
            }

            var plan = CreateEmptyFlightPlan(start, start, end, direction, duration, 0f);
            AddFlightWaypoint(plan, start, 0f);
            AddFlightWaypoint(plan, end, plan.Duration);
            FinalizeFlightPlan(plan);
            StartScriptedVisualFlyover(context, prefab, plan, label);
        }

        private void StartScriptedVisualFlyover(AirstrikeCallContext context, string prefab, DeliveryFlightPlan plan, string label)
        {
            if (string.IsNullOrWhiteSpace(prefab) || plan == null)
            {
                return;
            }

            Vector3 startPosition;
            Vector3 direction;
            Vector3 velocity;
            EvaluateFlightPlan(plan, 0f, out startPosition, out direction, out velocity);
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = Vector3.forward;
            }

            var rotation = GetFlightPlanTargetRotation(plan, 0f, direction);
            string error;
            var visual = SpawnTrackedVisualEntity(context, prefab, startPosition, rotation, label, out error);
            if (visual == null)
            {
                PrintVisualWarning(context, label, error);
                return;
            }

            PrepareVisualVehicleEntity(visual, velocity);
            RegisterDeliveryCarrier(context, visual, label);
            MoveVisualEntity(visual, startPosition, rotation, velocity, true);
            RunFlyoverSoundCues(context, prefab, plan, label);
            ScheduleVisualFlyoverStep(context, visual, plan, GetPreciseNow(), GetVisualMoveIntervalSeconds(), label);
        }

        private BaseEntity SpawnTrackedVisualEntity(AirstrikeCallContext context, string prefab, Vector3 position, Quaternion rotation, string label, out string error)
        {
            error = "";
            BaseEntity entity = null;
            var phase = "create";

            try
            {
                phase = "create prefab";
                entity = GameManager.server.CreateEntity(prefab, position, rotation, true) as BaseEntity;
                if (entity == null)
                {
                    error = phase + " failed: Could not create prefab '" + prefab + "'.";
                    return null;
                }

                phase = "configure ownership";
                entity.OwnerID = context.CallerUserId;
                entity.EnableSaving(false);

                phase = "spawn prefab";
                entity.Spawn();

                TrySetCreatorEntity(entity, GetCallPlayer(context), label);
                context.VisualEntities.Add(entity);
                IncrementStat("visual_spawned");
                IncrementStat("visual_spawned_" + SanitizeStatKey(label));

                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " spawned visual " + label + " at " + FormatPosition(position) + ".");
                }

                return entity;
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                error = phase + " failed: " + ex.Message;
                return null;
            }
        }

        private void TrySetCreatorEntity(BaseEntity entity, BasePlayer player, string label)
        {
            if (entity == null || entity.IsDestroyed || player == null)
            {
                return;
            }

            try
            {
                entity.SetCreatorEntity(player);
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Visual creator attribution skipped for " + (string.IsNullOrWhiteSpace(label) ? entity.ShortPrefabName : label) + ": " + ex.Message);
                }
            }
        }

        private void ScheduleVisualFlyoverStep(AirstrikeCallContext context, BaseEntity visual, DeliveryFlightPlan plan, double startedAt, float interval, string label)
        {
            ScheduleCallTimer(context, interval, () =>
            {
                if (!IsCallActive(context) || visual == null || visual.IsDestroyed || plan == null)
                {
                    return;
                }

                var elapsed = (float)(GetPreciseNow() - startedAt);
                var safeDuration = Math.Max(0.1f, plan.Duration);
                var progress = Mathf.Clamp01(elapsed / safeDuration);
                Vector3 position;
                Vector3 direction;
                Vector3 velocity;
                EvaluateFlightPlan(plan, elapsed, out position, out direction, out velocity);
                position = EnsurePositionAboveTerrain(position, GetFlightPlanTerrainClearance(plan, context == null ? null : context.Strike, label));
                var targetRotation = GetFlightPlanTargetRotation(plan, elapsed, direction);
                var rotation = plan.UsesCompiledTrack
                    ? targetRotation
                    : GetSmoothedVisualRotation(visual, targetRotation, interval, plan.RotationSmoothTimeSeconds);

                MoveVisualEntity(visual, position, rotation, velocity, progress >= 1f);

                if (progress >= 1f)
                {
                    KillTrackedVisualEntity(context, visual);
                    return;
                }

                ScheduleVisualFlyoverStep(context, visual, plan, startedAt, interval, label);
            });
        }

        private void MoveVisualEntity(BaseEntity entity, Vector3 position, Quaternion rotation, Vector3 velocity, bool immediate = false)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return;
            }

            var usesKinematicRigidbody = false;
            try
            {
                var rigidbody = entity.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    rigidbody.useGravity = false;
                    if (!rigidbody.isKinematic)
                    {
                        rigidbody.velocity = velocity;
                        rigidbody.angularVelocity = Vector3.zero;
                    }

                    rigidbody.isKinematic = true;
                    usesKinematicRigidbody = true;
                }
            }
            catch
            {
                // Some vehicle prefabs may not expose a usable rigidbody in every server build.
            }

            try
            {
                entity.transform.SetPositionAndRotation(position, rotation);
                if (!usesKinematicRigidbody)
                {
                    entity.SetVelocity(velocity);
                }

                entity.UpdateNetworkGroup();

                if (immediate)
                {
                    entity.SendNetworkUpdateImmediate();
                }
                else
                {
                    entity.SendNetworkUpdate();
                }
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Visual movement update failed for " + (entity.ShortPrefabName ?? entity.PrefabName ?? "entity") + ": " + ex.Message);
                }
            }
        }

        private void PrepareVisualVehicleEntity(BaseEntity entity, Vector3 velocity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return;
            }

            try
            {
                entity.SetFlagLocal(BaseEntity.Flags.On, true);

                var rigidbody = entity.GetComponent<Rigidbody>();
                var canSetVelocity = rigidbody == null || !rigidbody.isKinematic;
                if (canSetVelocity)
                {
                    entity.SetVelocity(velocity);
                }

                if (rigidbody != null)
                {
                    rigidbody.useGravity = false;
                    if (!rigidbody.isKinematic)
                    {
                        rigidbody.velocity = velocity;
                        rigidbody.angularVelocity = Vector3.zero;
                    }

                    rigidbody.isKinematic = true;
                }

                var cargoPlane = entity as CargoPlane;
                if (cargoPlane != null)
                {
                    cargoPlane.dropped = true;
                    cargoPlane.startPos = entity.transform.position;
                    cargoPlane.endPos = entity.transform.position;
                    cargoPlane.secondsToTake = float.MaxValue;
                    cargoPlane.secondsTaken = 0f;
                }

                var heli = entity as PlayerHelicopter;
                if (heli != null)
                {
                    var fuelSystem = heli.GetFuelSystem() as EntityFuelSystem;
                    if (fuelSystem != null)
                    {
                        fuelSystem.cachedHasFuel = true;
                        fuelSystem.nextFuelCheckTime = float.MaxValue;
                        var fuelContainer = fuelSystem.GetFuelContainer();
                        if (fuelContainer != null)
                        {
                            fuelContainer.SetFlagLocal(BaseEntity.Flags.Locked, true);
                        }
                    }

                    if (heli.engineController != null)
                    {
                        heli.engineController.FinishStartingEngine();
                    }
                }
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Visual vehicle engine prep failed for " + (entity.ShortPrefabName ?? entity.PrefabName ?? "entity") + ": " + ex.Message);
                }
            }
        }

        private void ConfigurePatrolHelicopterVisual(PatrolHelicopter patrolHeli, bool disableBrain)
        {
            if (patrolHeli == null || patrolHeli.IsDestroyed)
            {
                return;
            }

            try
            {
                if (disableBrain)
                {
                    patrolHeli.HasBrain = false;
                    if (patrolHeli.myAI != null)
                    {
                        patrolHeli.myAI.enabled = false;
                    }

                    patrolHeli.servergibs.guid = string.Empty;
                    patrolHeli.fireBall.guid = string.Empty;
                    patrolHeli.mapMarkerEntityPrefab.guid = string.Empty;
                    patrolHeli.fleeMapMarkerEntityPrefab.guid = string.Empty;
                    patrolHeli.DestroyFleeMarker();
                }
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Patrol helicopter visual prep skipped one native component: " + ex.Message);
                }
            }
        }

        private void PreparePatrolHelicopterVisualEntity(BaseEntity entity, Vector3 velocity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return;
            }

            try
            {
                var rigidbody = entity.GetComponent<Rigidbody>();
                var canSetVelocity = rigidbody == null || !rigidbody.isKinematic;
                if (canSetVelocity)
                {
                    entity.SetVelocity(velocity);
                }

                if (rigidbody != null)
                {
                    rigidbody.useGravity = false;
                    if (!rigidbody.isKinematic)
                    {
                        rigidbody.velocity = velocity;
                        rigidbody.angularVelocity = Vector3.zero;
                    }

                    rigidbody.isKinematic = true;
                }

                entity.UpdateNetworkGroup();
                entity.SendNetworkUpdateImmediate();
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Patrol helicopter visual movement prep failed for " + (entity.ShortPrefabName ?? entity.PrefabName ?? "entity") + ": " + ex.Message);
                }
            }
        }

        private void KillTrackedVisualEntity(AirstrikeCallContext context, BaseEntity entity)
        {
            if (context != null)
            {
                context.VisualEntities.Remove(entity);
                if (ReferenceEquals(context.DeliveryCarrier, entity))
                {
                    ClearDeliveryCarrier(context);
                }
            }

            if (entity != null && !entity.IsDestroyed)
            {
                entity.Kill(BaseNetworkable.DestroyMode.None);
            }
        }

        private float GetVisualMoveIntervalSeconds()
        {
            var configured = config?.DeliveryVisuals == null ? DefaultVisualMoveIntervalSeconds : config.DeliveryVisuals.VisualMoveIntervalSeconds;
            return Mathf.Clamp(configured <= 0f ? DefaultVisualMoveIntervalSeconds : configured, MinimumVisualMoveIntervalSeconds, MaximumVisualMoveIntervalSeconds);
        }

        private float GetVisualRotationSmoothTimeSeconds()
        {
            var configured = config?.DeliveryVisuals == null ? DefaultVisualRotationSmoothTimeSeconds : config.DeliveryVisuals.VisualRotationSmoothTimeSeconds;
            return Mathf.Clamp(configured <= 0f ? DefaultVisualRotationSmoothTimeSeconds : configured, MinimumVisualRotationSmoothTimeSeconds, MaximumVisualRotationSmoothTimeSeconds);
        }

        private Quaternion GetSmoothedVisualRotation(BaseEntity visual, Quaternion targetRotation, float interval, float authoredSmoothTimeSeconds)
        {
            if (visual == null || visual.IsDestroyed)
            {
                return visual == null ? Quaternion.identity : visual.transform.rotation;
            }

            var smoothTime = authoredSmoothTimeSeconds > 0f
                ? Mathf.Clamp(authoredSmoothTimeSeconds, 0.02f, 1.5f)
                : GetVisualRotationSmoothTimeSeconds();
            if (smoothTime <= MinimumVisualRotationSmoothTimeSeconds + 0.001f)
            {
                return targetRotation;
            }

            var blend = Mathf.Clamp01(Mathf.Max(interval, MinimumVisualMoveIntervalSeconds) / smoothTime);
            return Quaternion.Slerp(visual.transform.rotation, targetRotation, blend);
        }

        private void RunFlyoverSoundCues(AirstrikeCallContext context, string prefab, DeliveryFlightPlan plan, string label)
        {
            if (context == null || plan == null || config?.DeliveryVisuals == null || !config.DeliveryVisuals.SpawnFlyoverSoundEffects)
            {
                return;
            }

            var duration = Mathf.Max(0.1f, plan.Duration);
            var interval = GetFlyoverSoundIntervalSeconds();
            var cueCount = Mathf.Clamp(Mathf.CeilToInt(duration / interval) + 1, 2, 28);
            for (var i = 0; i < cueCount; i++)
            {
                var cueIndex = i;
                var progress = cueCount <= 1 ? 0f : cueIndex / (float)(cueCount - 1);
                var delay = Mathf.Clamp(duration * progress, 0.01f, Math.Max(0.01f, duration - 0.05f));
                Vector3 position;
                Vector3 direction;
                Vector3 velocity;
                EvaluateFlightPlan(plan, delay, out position, out direction, out velocity);
                ScheduleCallTimer(context, delay, () =>
                {
                    if (!IsCallActive(context))
                    {
                        return;
                    }

                    RunFlyoverSoundBurst(prefab, position, label, cueIndex, cueCount);
                });
            }
        }

        private void RunFlyoverSoundCues(AirstrikeCallContext context, string prefab, Vector3 start, Vector3 end, float duration, string label)
        {
            if (context == null || config?.DeliveryVisuals == null || !config.DeliveryVisuals.SpawnFlyoverSoundEffects)
            {
                return;
            }

            var interval = GetFlyoverSoundIntervalSeconds();
            var cueCount = Mathf.Clamp(Mathf.CeilToInt(duration / interval) + 1, 2, 24);
            for (var i = 0; i < cueCount; i++)
            {
                var cueIndex = i;
                var progress = cueCount <= 1 ? 0f : cueIndex / (float)(cueCount - 1);
                var delay = Mathf.Clamp(duration * progress, 0.01f, Math.Max(0.01f, duration - 0.05f));
                var position = Vector3.Lerp(start, end, progress);
                ScheduleCallTimer(context, delay, () =>
                {
                    if (!IsCallActive(context))
                    {
                        return;
                    }

                    RunFlyoverSoundBurst(prefab, position, label, cueIndex, cueCount);
                });
            }
        }

        private void RunFlyoverSoundBurst(string prefab, Vector3 position, string label, int cueIndex, int cueCount)
        {
            var firstCue = cueIndex <= 0;
            var lastCue = cueIndex >= cueCount - 1;

            RunSafeEffect(VehicleFlybySoundEffect, position, label + " engine cue");
            RunSafeEffect(ProjectileFlightSoundEffect, position + Vector3.up * 0.5f, label + " air movement cue");

            if (string.Equals(prefab, DroneVisualPrefab, StringComparison.OrdinalIgnoreCase))
            {
                if (firstCue)
                {
                    RunSafeEffect(DroneDeployEffect, position, label + " deploy cue");
                }
            }

            if (IsMlrsVisualLabel(label) || lastCue)
            {
                RunSafeEffect(BulletFlybySoundEffect, position, label + " flyby cue");
            }

            if (!IsHelicopterVisualPrefab(prefab) && !string.Equals(prefab, DroneVisualPrefab, StringComparison.OrdinalIgnoreCase))
            {
                RunSafeEffect(LargeFastFalloffSoundEffect, position, label + " large flyby cue");
            }
        }

        private bool IsMlrsVisualLabel(string label)
        {
            return !string.IsNullOrWhiteSpace(label)
                && label.IndexOf("MLRS", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsHelicopterVisualPrefab(string prefab)
        {
            return string.Equals(prefab, PatrolHelicopterVisualPrefab, StringComparison.OrdinalIgnoreCase);
        }

        private float GetFlyoverSoundIntervalSeconds()
        {
            var configured = config?.DeliveryVisuals == null ? DefaultFlyoverSoundIntervalSeconds : config.DeliveryVisuals.FlyoverSoundIntervalSeconds;
            return Mathf.Clamp(configured <= 0f ? DefaultFlyoverSoundIntervalSeconds : configured, 0.25f, 3f);
        }

        private void PrepareVisualCrewNpc(BaseEntity entity, string displayName, Vector3 holdPosition)
        {
            var player = entity as BasePlayer;
            if (player != null && !string.IsNullOrWhiteSpace(displayName))
            {
                player.displayName = displayName;
            }

            var npc = entity as NPCPlayer;
            if (npc != null)
            {
                try
                {
                    npc.Resume();
                    npc.SetDestination(holdPosition);
                }
                catch (Exception ex)
                {
                    if (config.General.DebugMode)
                    {
                        Puts("Artillery crew NPC prepare failed: " + ex.Message);
                    }
                }
            }

            SuppressVisualNpcTargeting(entity);
            entity.SendNetworkUpdateImmediate();
        }

        private void SuppressVisualNpcTargeting(BaseEntity entity)
        {
            if (entity == null)
            {
                return;
            }

            try
            {
                var brain = entity.GetComponent<BaseAIBrain>() ?? entity.GetComponentInChildren<BaseAIBrain>();
                if (brain == null)
                {
                    return;
                }

                brain.HostileTargetsOnly = true;
                brain.IgnoreSafeZonePlayers = true;
                brain.RefreshKnownLOS = false;
                brain.SenseTypes &= ~EntityType.Player;
                brain.mainInterestPoint = entity.transform.position;

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
                        RemoveVisualNpcMemory(memory.Players);
                        RemoveVisualNpcMemory(memory.Targets);
                        RemoveVisualNpcMemory(memory.Threats);
                        memory.LOS.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Artillery crew NPC targeting suppression failed: " + ex.Message);
                }
            }
        }

        private void RemoveVisualNpcMemory(List<BaseEntity> entities)
        {
            if (entities == null)
            {
                return;
            }

            entities.RemoveAll(entity => entity is BasePlayer);
        }

        private void RunMortarLaunchVisual(AirstrikeCallContext context, int shellIndex)
        {
            if (context == null || !context.HasMortarSourcePosition || !ShouldSpawnDeliveryVisual(context) || !config.DeliveryVisuals.SpawnMortarArtilleryVisuals)
            {
                return;
            }

            RunSafeEffect(MortarAttackMuzzleEffect, context.MortarSourcePosition + (Vector3.up * 1.25f), "mortar muzzle");
            if (config.General.DebugMode)
            {
                Puts(context.Strike.Id + " mortar visual fired shell " + shellIndex + " from " + FormatPosition(context.MortarSourcePosition) + ".");
            }
        }

        private void RunSafeEffect(string prefab, Vector3 position, string label)
        {
            if (string.IsNullOrWhiteSpace(prefab))
            {
                return;
            }

            try
            {
                Effect.server.Run(prefab, position);
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Visual effect " + label + " failed: " + ex.Message);
                }
            }
        }

        private void PrintVisualWarning(AirstrikeCallContext context, string label, string message)
        {
            var strikeId = context?.Strike == null ? "unknown strike" : context.Strike.Id;
            PrintWarning(strikeId + " visual " + label + " could not spawn: " + (string.IsNullOrWhiteSpace(message) ? "unknown error" : message));
            IncrementStat("visual_spawn_failed");
            IncrementStat("visual_spawn_failed_" + SanitizeStatKey(label));
        }

        private string SanitizeStatKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var chars = value.Trim().ToLowerInvariant().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars).Trim('_');
        }

        private float GetWarningDelaySeconds(StrikeDefinition strike)
        {
            var configured = strike == null ? 0f : strike.WarningDelaySeconds;
            var delay = configured > 0f ? configured : config.General.DefaultWarningDelaySeconds;
            return Mathf.Clamp(delay, 0f, 60f);
        }

        private bool TryGetDronePayloadSpec(string payload, out DronePayloadSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            switch (payload.Trim().ToLowerInvariant())
            {
                case "bee_grenade":
                    spec = new DronePayloadSpec
                    {
                        Id = "bee_grenade",
                        DisplayName = "bee grenade",
                        Prefab = BeeGrenadePrefab,
                        FuseSeconds = BeeGrenadeFuseSeconds,
                        FinishDelaySeconds = BeeGrenadeFuseSeconds + 1.25f,
                        HasTimedFuse = true
                    };
                    return true;

                case "beancan":
                    spec = new DronePayloadSpec
                    {
                        Id = "beancan",
                        DisplayName = "beancan",
                        Prefab = BeancanGrenadePrefab,
                        FuseSeconds = BeancanFuseSeconds,
                        FinishDelaySeconds = BeancanFuseSeconds + 1.25f,
                        HasTimedFuse = true
                    };
                    return true;

                case "f1_grenade":
                    spec = new DronePayloadSpec
                    {
                        Id = "f1_grenade",
                        DisplayName = "F1",
                        Prefab = F1GrenadePrefab,
                        FuseSeconds = F1FuseSeconds,
                        FinishDelaySeconds = F1FuseSeconds + 1.25f,
                        HasTimedFuse = true
                    };
                    return true;

                case "smoke":
                    spec = new DronePayloadSpec
                    {
                        Id = "smoke",
                        DisplayName = "smoke",
                        Prefab = SmokeGrenadePrefab,
                        FuseSeconds = 0f,
                        FinishDelaySeconds = SmokeFinishDelaySeconds,
                        HasTimedFuse = false
                    };
                    return true;

                case "flashbang":
                    spec = new DronePayloadSpec
                    {
                        Id = "flashbang",
                        DisplayName = "flashbang",
                        Prefab = FlashbangGrenadePrefab,
                        FuseSeconds = FlashbangFuseSeconds,
                        FinishDelaySeconds = FlashbangFuseSeconds + 1.25f,
                        HasTimedFuse = true
                    };
                    return true;

                case "molotov":
                    spec = new DronePayloadSpec
                    {
                        Id = "molotov",
                        DisplayName = "molotov",
                        Prefab = MolotovGrenadePrefab,
                        FuseSeconds = MolotovFuseSeconds,
                        FinishDelaySeconds = MolotovFuseSeconds + 2.5f,
                        HasTimedFuse = true
                    };
                    return true;

                case "he_40mm":
                    spec = new DronePayloadSpec
                    {
                        Id = "he_40mm",
                        DisplayName = "40mm HE",
                        Prefab = He40mmGrenadePrefab,
                        FuseSeconds = 0f,
                        FinishDelaySeconds = He40mmFinishDelaySeconds,
                        HasTimedFuse = false
                    };
                    return true;

                default:
                    return false;
            }
        }

        private bool TryGetHeavyDropPayloadSpec(string payload, out HeavyDropPayloadSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            switch (payload.Trim().ToLowerInvariant())
            {
                case "bee_catapult_bomb":
                    spec = new HeavyDropPayloadSpec
                    {
                        Id = "bee_catapult_bomb",
                        DisplayName = "bee catapult",
                        Prefab = CatapultBeeProjectilePrefab,
                        FinishDelaySeconds = HeavyDropFinishDelaySeconds
                    };
                    return true;

                case "firebomb":
                    spec = new HeavyDropPayloadSpec
                    {
                        Id = "firebomb",
                        DisplayName = "firebomb",
                        Prefab = CatapultFirebombProjectilePrefab,
                        FinishDelaySeconds = HeavyDropFinishDelaySeconds
                    };
                    return true;

                case "propane_bomb":
                    spec = new HeavyDropPayloadSpec
                    {
                        Id = "propane_bomb",
                        DisplayName = "propane bomb",
                        Prefab = CatapultPropaneProjectilePrefab,
                        FinishDelaySeconds = HeavyDropFinishDelaySeconds
                    };
                    return true;

                default:
                    return false;
            }
        }

        private bool TryGetRocketPayloadSpec(string payload, out RocketRunPayloadSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            switch (payload.Trim().ToLowerInvariant())
            {
                case "hv_rocket":
                    spec = new RocketRunPayloadSpec
                    {
                        Id = "hv_rocket",
                        DisplayName = "HV",
                        Prefab = HvRocketPrefab,
                        ProjectileSpeed = 70f,
                        FinishDelaySeconds = RocketRunFinishDelaySeconds
                    };
                    return true;

                case "rocket":
                    spec = new RocketRunPayloadSpec
                    {
                        Id = "rocket",
                        DisplayName = "standard",
                        Prefab = BasicRocketPrefab,
                        ProjectileSpeed = 55f,
                        FinishDelaySeconds = RocketRunFinishDelaySeconds
                    };
                    return true;

                case "incendiary_rocket":
                    spec = new RocketRunPayloadSpec
                    {
                        Id = "incendiary_rocket",
                        DisplayName = "incendiary",
                        Prefab = IncendiaryRocketPrefab,
                        ProjectileSpeed = 50f,
                        FinishDelaySeconds = RocketRunFinishDelaySeconds + 1.5f
                    };
                    return true;

                default:
                    return false;
            }
        }

        private bool TryGetMlrsPayloadSpec(string payload, out MlrsPayloadSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            switch (payload.Trim().ToLowerInvariant())
            {
                case "mlrs_rocket":
                    spec = new MlrsPayloadSpec
                    {
                        Id = "mlrs_rocket",
                        DisplayName = "MLRS",
                        Prefab = MlrsRocketPrefab,
                        ProjectileSpeed = MlrsRocketSpeed,
                        FinishDelaySeconds = MlrsFinishDelaySeconds
                    };
                    return true;

                default:
                    return false;
            }
        }

        private bool TryGetHomingMissileSpec(string payload, out HomingMissileSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            switch (payload.Trim().ToLowerInvariant())
            {
                case "homing_missile":
                    spec = new HomingMissileSpec
                    {
                        Id = "homing_missile",
                        DisplayName = "vehicle-tracking",
                        Prefab = HvRocketPrefab,
                        ProjectileSpeed = HomingMissileDefaultSpeed,
                        FinishDelaySeconds = HomingMissileFinishPaddingSeconds
                    };
                    return true;

                default:
                    return false;
            }
        }

        private bool TryGetMortarPayloadSpec(string payload, out MortarPayloadSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            switch (payload.Trim().ToLowerInvariant())
            {
                case "mortar_he_payload":
                    spec = new MortarPayloadSpec
                    {
                        Id = "mortar_he_payload",
                        DisplayName = "HE",
                        Prefab = MortarHeShellPrefab,
                        FinishDelaySeconds = MortarFinishDelaySeconds
                    };
                    return true;

                case "mortar_frag_payload":
                    spec = new MortarPayloadSpec
                    {
                        Id = "mortar_frag_payload",
                        DisplayName = "frag",
                        Prefab = MortarFragShellPrefab,
                        FinishDelaySeconds = MortarFinishDelaySeconds
                    };
                    return true;

                default:
                    return false;
            }
        }

        private bool TryGetA10StrafeSpec(string payload, out A10StrafeSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            switch (payload.Trim().ToLowerInvariant())
            {
                case "bradley_longbarrel_burst":
                    spec = new A10StrafeSpec
                    {
                        Id = "bradley_longbarrel_burst",
                        DisplayName = "Bradley longbarrel",
                        BaseDamage = A10DefaultPulseBaseDamage
                    };
                    return true;

                default:
                    return false;
            }
        }

        private void SchedulePayloadReleaseEvents(AirstrikeCallContext context, Vector3 approach, Action<bool, string> callback)
        {
            if (!HasPayloadReleaseSchedule(context))
            {
                return;
            }

            var releases = new List<RuntimePayloadRelease>(context.PayloadReleaseSchedule);
            foreach (var release in releases)
            {
                var scheduledRelease = release;
                ScheduleSinglePayloadReleaseEvent(context, approach, scheduledRelease, callback);
            }
        }

        private void ScheduleSinglePayloadReleaseEvent(AirstrikeCallContext context, Vector3 approach, RuntimePayloadRelease release, Action<bool, string> callback)
        {
            ScheduleCallTimer(context, release == null ? 0f : release.Time, () =>
            {
                if (!IsCallActive(context))
                {
                    return;
                }

                string error;
                if (!TryExecutePayloadRelease(context, release, approach, out error))
                {
                    callback(false, error);
                }
            });
        }

        private bool TryExecutePayloadRelease(AirstrikeCallContext context, RuntimePayloadRelease release, Vector3 approach, out string error)
        {
            error = "";
            if (context == null || release == null)
            {
                error = "Missing payload release event.";
                return false;
            }

            var payload = GetReleasePayload(release.Event, release.Payload);
            if (string.IsNullOrWhiteSpace(payload))
            {
                payload = NormalizePayloadId(context.Strike == null ? "" : context.Strike.Payload);
            }

            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = context.PlannedDeliveryApproach.sqrMagnitude > 0.01f ? context.PlannedDeliveryApproach : GetRocketApproachDirection(context);
            }

            DronePayloadSpec droneSpec;
            if (TryGetDronePayloadSpec(payload, out droneSpec))
            {
                return TrySpawnDronePayload(context, droneSpec, release.SequenceIndex, release.TotalCount, out error, release.Event);
            }

            HeavyDropPayloadSpec heavySpec;
            if (TryGetHeavyDropPayloadSpec(payload, out heavySpec))
            {
                return TrySpawnHeavyDropPayload(context, heavySpec, release.SequenceIndex, release.TotalCount, out error, release.Event);
            }

            RocketRunPayloadSpec rocketSpec;
            if (TryGetRocketPayloadSpec(payload, out rocketSpec))
            {
                return TrySpawnRocketProjectile(context, rocketSpec, approach, release.SequenceIndex, release.TotalCount, out error, release.Event);
            }

            MlrsPayloadSpec mlrsSpec;
            if (TryGetMlrsPayloadSpec(payload, out mlrsSpec))
            {
                return TrySpawnMlrsRocket(context, mlrsSpec, approach, release.SequenceIndex, release.TotalCount, out error, release.Event);
            }

            HomingMissileSpec homingSpec;
            if (TryGetHomingMissileSpec(payload, out homingSpec))
            {
                return TrySpawnHomingMissile(context, homingSpec, approach, context.Target == null ? 0UL : context.Target.EntityId, release.SequenceIndex, release.TotalCount, out error, release.Event);
            }

            MortarPayloadSpec mortarSpec;
            if (TryGetMortarPayloadSpec(payload, out mortarSpec))
            {
                return TrySpawnMortarShell(context, mortarSpec, release.SequenceIndex, release.TotalCount, out error, release.Event);
            }

            A10StrafeSpec a10Spec;
            if (TryGetA10StrafeSpec(payload, out a10Spec))
            {
                var direction = approach.sqrMagnitude <= 0.01f ? GetA10StrafeDirection(context) : approach.normalized;
                return TryRunA10Pulse(context, a10Spec, direction, release.SequenceIndex, release.TotalCount, out error, release.Event);
            }

            error = "Payload release event " + release.SourceEventIndex + " uses unsupported payload '" + payload + "'.";
            return false;
        }

        private bool TryGetCarrierReleaseFrame(AirstrikeCallContext context, VisualPayloadEvent releaseEvent, out Vector3 origin, out Vector3 forward, out Vector3 velocity)
        {
            origin = Vector3.zero;
            forward = Vector3.forward;
            velocity = Vector3.zero;

            var carrier = context == null ? null : context.DeliveryCarrier;
            var combat = carrier as BaseCombatEntity;
            if (carrier == null || carrier.IsDestroyed || (combat != null && combat.IsDead()))
            {
                return false;
            }

            var transform = carrier.transform;
            if (transform == null)
            {
                return false;
            }

            forward = transform.forward;
            if (forward.sqrMagnitude <= 0.01f)
            {
                forward = context.PlannedDeliveryApproach.sqrMagnitude > 0.01f ? context.PlannedDeliveryApproach : Vector3.forward;
            }

            forward.Normalize();
            var right = transform.right;
            if (right.sqrMagnitude <= 0.01f)
            {
                right = GetRightVector(forward);
            }

            var up = transform.up;
            if (up.sqrMagnitude <= 0.01f)
            {
                up = Vector3.up;
            }

            var offsetX = releaseEvent == null ? 0f : releaseEvent.CarrierOffsetX;
            var offsetY = releaseEvent == null ? 0f : releaseEvent.CarrierOffsetY;
            var offsetZ = releaseEvent == null ? 0f : releaseEvent.CarrierOffsetZ;
            origin = transform.position + (right.normalized * offsetX) + (up.normalized * offsetY) + (forward * offsetZ);
            origin = EnsurePositionAboveTerrain(origin, GetPayloadTerrainClearance());

            try
            {
                var rigidbody = carrier.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    velocity = rigidbody.velocity;
                }
            }
            catch
            {
                velocity = Vector3.zero;
            }

            return true;
        }

        private Vector3 GetReleaseTargetPosition(AirstrikeCallContext context, VisualPayloadEvent releaseEvent, Vector3 approach, Vector3 fallbackImpact, float fallbackSpreadRadius)
        {
            if (context == null || context.Target == null)
            {
                return ResolveImpactPosition(fallbackImpact);
            }

            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = context.PlannedDeliveryApproach.sqrMagnitude > 0.01f ? context.PlannedDeliveryApproach : GetRocketApproachDirection(context);
            }

            approach.y = 0f;
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            var right = GetRightVector(approach);
            var center = context.Target.Position;
            if (releaseEvent != null)
            {
                center += right * releaseEvent.TargetOffsetX;
                center += Vector3.up * releaseEvent.TargetOffsetY;
                center += approach * releaseEvent.TargetOffsetZ;
            }

            var spread = releaseEvent != null && releaseEvent.SpreadRadius >= 0f
                ? releaseEvent.SpreadRadius
                : fallbackSpreadRadius;
            return ResolveImpactPosition(RandomSpreadPosition(center, spread));
        }

        private Vector3 GetReleaseAimPoint(VisualPayloadEvent releaseEvent, Vector3 basePoint, Vector3 approach, float fallbackSpreadRadius)
        {
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }

            approach.y = 0f;
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            var right = GetRightVector(approach);
            var center = basePoint;
            if (releaseEvent != null)
            {
                center += right * releaseEvent.TargetOffsetX;
                center += Vector3.up * releaseEvent.TargetOffsetY;
                center += approach * releaseEvent.TargetOffsetZ;
            }

            var spread = releaseEvent != null && releaseEvent.SpreadRadius >= 0f
                ? releaseEvent.SpreadRadius
                : fallbackSpreadRadius;
            if (spread <= 0.01f)
            {
                return center;
            }

            var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            var distance = Mathf.Sqrt(UnityEngine.Random.value) * Mathf.Clamp(spread, 0f, 250f);
            return center + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
        }

        private float GetReleaseLaunchSpeed(VisualPayloadEvent releaseEvent, float fallback)
        {
            return releaseEvent != null && releaseEvent.LaunchSpeed >= 0f
                ? Mathf.Clamp(releaseEvent.LaunchSpeed, 1f, 350f)
                : Mathf.Max(1f, fallback);
        }

        private float GetReleaseFuseSeconds(VisualPayloadEvent releaseEvent, float fallback)
        {
            return releaseEvent != null && releaseEvent.FuseSeconds >= 0f
                ? Mathf.Clamp(releaseEvent.FuseSeconds, 0f, 120f)
                : fallback;
        }

        private float GetReleaseSplashRadius(StrikeDefinition strike, VisualPayloadEvent releaseEvent)
        {
            var radius = releaseEvent != null && releaseEvent.SplashRadius >= 0f
                ? Mathf.Clamp(releaseEvent.SplashRadius, 0f, 100f)
                : GetHomingSplashRadius(strike);
            return Mathf.Clamp(radius * NormalizePositiveMultiplier(strike == null ? 1f : strike.SplashRadiusMultiplier), 0f, 500f);
        }

        private float GetReleaseImpactRadius(StrikeDefinition strike, VisualPayloadEvent releaseEvent)
        {
            var fallback = strike == null || strike.ImpactRadius <= 0f ? 2.5f : strike.ImpactRadius;
            var radius = releaseEvent != null && releaseEvent.ImpactRadius >= 0f
                ? Mathf.Clamp(releaseEvent.ImpactRadius, 0.1f, 100f)
                : Mathf.Clamp(fallback, 0.5f, 25f);
            return Mathf.Clamp(radius * NormalizePositiveMultiplier(strike == null ? 1f : strike.ImpactRadiusMultiplier), 0.1f, 500f);
        }

        private float GetReleaseTrackingSeconds(StrikeDefinition strike, VisualPayloadEvent releaseEvent)
        {
            var seconds = releaseEvent != null && releaseEvent.MaxTrackingSeconds >= 0f
                ? Mathf.Clamp(releaseEvent.MaxTrackingSeconds, 0.1f, 120f)
                : GetHomingTrackingSeconds(strike);
            return Mathf.Clamp(seconds * NormalizePositiveMultiplier(strike == null ? 1f : strike.TrackingSecondsMultiplier), 0.1f, 600f);
        }

        private float GetReleaseTrackingDistance(StrikeDefinition strike, VisualPayloadEvent releaseEvent)
        {
            var distance = releaseEvent != null && releaseEvent.MaxTrackingDistance >= 0f
                ? Mathf.Clamp(releaseEvent.MaxTrackingDistance, 1f, 2500f)
                : GetHomingTrackingDistance(strike);
            return Mathf.Clamp(distance * NormalizePositiveMultiplier(strike == null ? 1f : strike.TrackingDistanceMultiplier), 1f, 10000f);
        }

        private float GetReleaseDamageScale(VisualPayloadEvent releaseEvent, string key)
        {
            if (releaseEvent == null)
            {
                return 1f;
            }

            var scale = Mathf.Clamp(releaseEvent.DamageScale <= 0f ? 1f : releaseEvent.DamageScale, 0f, 10f);
            if (!string.IsNullOrWhiteSpace(key) && releaseEvent.DamageScales != null)
            {
                float specific;
                if (releaseEvent.DamageScales.TryGetValue(key, out specific))
                {
                    scale *= Mathf.Clamp(specific <= 0f ? 1f : specific, 0f, 10f);
                }
            }

            return Mathf.Clamp(scale, 0f, 100f);
        }

        private float GetReleaseVehicleDamageScale(VisualPayloadEvent releaseEvent)
        {
            if (releaseEvent == null)
            {
                return 1f;
            }

            return Mathf.Clamp(releaseEvent.VehicleDamageScale <= 0f ? 1f : releaseEvent.VehicleDamageScale, 0f, 10f);
        }

        private void RegisterPayloadReleaseMetadata(BaseEntity entity, VisualPayloadEvent releaseEvent)
        {
            if (entity == null || releaseEvent == null || entity.net == null || entity.net.ID.Value == 0UL)
            {
                return;
            }

            if (Math.Abs(GetReleaseDamageScale(releaseEvent, "") - 1f) <= 0.001f
                && Math.Abs(GetReleaseVehicleDamageScale(releaseEvent) - 1f) <= 0.001f
                && (releaseEvent.DamageScales == null || releaseEvent.DamageScales.Count == 0))
            {
                return;
            }

            payloadReleaseMetadataByEntityId[entity.net.ID.Value] = new RuntimePayloadRelease
            {
                Event = ClonePayloadEvent(releaseEvent),
                Payload = releaseEvent.Payload,
                Time = releaseEvent.Time,
                SourceEventIndex = releaseEvent.Index
            };

            if (config?.General != null && config.General.DebugMode)
            {
                Puts("Tagged native payload entity " + entity.net.ID.Value + " for release damage scaling. If Rust reports explosion damage from another initiator, native damage will stay unchanged.");
            }
        }

        private void RemovePayloadReleaseMetadata(BaseEntity entity)
        {
            if (entity == null || entity.net == null || entity.net.ID.Value == 0UL)
            {
                return;
            }

            payloadReleaseMetadataByEntityId.Remove(entity.net.ID.Value);
        }

        private bool TrySpawnDronePayload(AirstrikeCallContext context, DronePayloadSpec spec, int payloadIndex, int totalPayloads, out string error, VisualPayloadEvent releaseEvent = null)
        {
            error = "";
            if (!IsCallActive(context))
            {
                return false;
            }

            if (!TryRequireLiveDeliveryCarrier(context, "payload " + payloadIndex + "/" + totalPayloads, out error))
            {
                return false;
            }

            if (spec == null || string.IsNullOrWhiteSpace(spec.Prefab))
            {
                error = "Drone payload is not configured correctly.";
                return false;
            }

            var approach = context.PlannedDeliveryApproach.sqrMagnitude <= 0.01f ? GetRocketApproachDirection(context) : context.PlannedDeliveryApproach;
            var spread = GetStrikeSpreadRadius(context.Strike);
            var impact = releaseEvent == null
                ? ResolveImpactPosition(GetPlannedImpactPosition(context, payloadIndex, totalPayloads, approach, spread))
                : GetReleaseTargetPosition(context, releaseEvent, approach, context.Target.Position, spread);
            var spawnHeight = GetDronePayloadSpawnHeight(spec);
            var spawn = EnsurePositionAboveTerrain(impact + Vector3.up * spawnHeight, GetPayloadTerrainClearance());
            Vector3 carrierForward;
            Vector3 carrierVelocity;
            var hasCarrierFrame = TryGetCarrierReleaseFrame(context, releaseEvent, out spawn, out carrierForward, out carrierVelocity);
            if (!hasCarrierFrame)
            {
                spawn = EnsurePositionAboveTerrain(impact + Vector3.up * spawnHeight, GetPayloadTerrainClearance());
            }

            var dropDirection = impact + (Vector3.up * 0.15f) - spawn;
            if (dropDirection.sqrMagnitude <= 0.01f)
            {
                dropDirection = hasCarrierFrame && carrierForward.sqrMagnitude > 0.01f ? carrierForward : Vector3.down;
            }
            else
            {
                dropDirection.Normalize();
            }
            var launchSpeed = GetReleaseLaunchSpeed(releaseEvent, PayloadDownwardVelocity);
            var dropVelocity = (dropDirection * launchSpeed) + (hasCarrierFrame ? carrierVelocity : Vector3.zero);
            BaseEntity entity = null;

            try
            {
                entity = GameManager.server.CreateEntity(spec.Prefab, spawn, Quaternion.LookRotation(dropDirection), true) as BaseEntity;
                if (entity == null)
                {
                    error = "Could not spawn " + spec.DisplayName + " payload prefab.";
                    return false;
                }

                entity.OwnerID = context.CallerUserId;
                var player = GetCallPlayer(context);
                if (player != null)
                {
                    entity.SetCreatorEntity(player);
                }

                var timed = entity.GetComponent<TimedExplosive>();
                if (timed != null && spec.HasTimedFuse)
                {
                    var fuseSeconds = GetReleaseFuseSeconds(releaseEvent, spec.FuseSeconds);
                    timed.timerAmountMin = fuseSeconds;
                    timed.timerAmountMax = fuseSeconds;
                }

                var projectile = entity.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.speed = Math.Max(projectile.speed, launchSpeed);
                    projectile.InitializeVelocity(dropVelocity);
                }

                entity.Spawn();
                RegisterPayloadReleaseMetadata(entity, releaseEvent);

                if (projectile != null)
                {
                    projectile.SetVelocity(dropVelocity);
                }
                else
                {
                    entity.SetVelocity(dropVelocity);
                }

                MarkImpactStarted(context);
                context.State = StrikeExecutionState.Impacting;
                context.SpawnedEntities.Add(entity);
                MarkPayloadReleased(context);

                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " " + spec.Id + " payload " + payloadIndex + "/" + totalPayloads + " spawned at " + FormatPosition(spawn) + " toward " + FormatPosition(impact) + ".");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                error = "Could not spawn " + spec.DisplayName + " payload: " + ex.Message;
                return false;
            }
        }

        private bool TrySpawnHeavyDropPayload(AirstrikeCallContext context, HeavyDropPayloadSpec spec, int payloadIndex, int totalPayloads, out string error, VisualPayloadEvent releaseEvent = null)
        {
            error = "";
            if (!IsCallActive(context))
            {
                return false;
            }

            if (!TryRequireLiveDeliveryCarrier(context, "heavy payload " + payloadIndex + "/" + totalPayloads, out error))
            {
                return false;
            }

            if (spec == null || string.IsNullOrWhiteSpace(spec.Prefab))
            {
                error = "Heavy drop payload is not configured correctly.";
                return false;
            }

            var approach = context.PlannedDeliveryApproach.sqrMagnitude <= 0.01f ? GetRocketApproachDirection(context) : context.PlannedDeliveryApproach;
            var spread = GetStrikeSpreadRadius(context.Strike);
            var impact = releaseEvent == null
                ? ResolveImpactPosition(GetPlannedImpactPosition(context, payloadIndex, totalPayloads, approach, spread))
                : GetReleaseTargetPosition(context, releaseEvent, approach, context.Target.Position, spread);
            var spawn = EnsurePositionAboveTerrain(impact + Vector3.up * HeavyDropSpawnHeight, GetPayloadTerrainClearance());
            Vector3 carrierForward;
            Vector3 carrierVelocity;
            var hasCarrierFrame = TryGetCarrierReleaseFrame(context, releaseEvent, out spawn, out carrierForward, out carrierVelocity);
            if (!hasCarrierFrame)
            {
                spawn = EnsurePositionAboveTerrain(impact + Vector3.up * HeavyDropSpawnHeight, GetPayloadTerrainClearance());
            }

            var dropDirection = impact + (Vector3.up * 0.25f) - spawn;
            if (dropDirection.sqrMagnitude <= 0.01f)
            {
                dropDirection = hasCarrierFrame && carrierForward.sqrMagnitude > 0.01f ? carrierForward : Vector3.down;
            }
            else
            {
                dropDirection.Normalize();
            }

            var launchSpeed = GetReleaseLaunchSpeed(releaseEvent, HeavyDropDownwardVelocity);
            var dropVelocity = (dropDirection * launchSpeed) + (hasCarrierFrame ? carrierVelocity : Vector3.zero);
            BaseEntity entity = null;

            try
            {
                entity = GameManager.server.CreateEntity(spec.Prefab, spawn, Quaternion.LookRotation(dropDirection), true) as BaseEntity;
                if (entity == null)
                {
                    error = "Could not spawn " + spec.DisplayName + " heavy payload prefab.";
                    return false;
                }

                entity.OwnerID = context.CallerUserId;
                var player = GetCallPlayer(context);
                if (player != null)
                {
                    entity.SetCreatorEntity(player);
                }

                var projectile = entity.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.speed = Math.Max(projectile.speed, launchSpeed);
                    projectile.InitializeVelocity(dropVelocity);
                }

                entity.Spawn();
                RegisterPayloadReleaseMetadata(entity, releaseEvent);

                if (projectile != null)
                {
                    projectile.SetVelocity(dropVelocity);
                }
                else
                {
                    entity.SetVelocity(dropVelocity);
                }

                MarkImpactStarted(context);
                context.State = StrikeExecutionState.Impacting;
                context.SpawnedEntities.Add(entity);
                MarkPayloadReleased(context);

                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " " + spec.Id + " heavy payload " + payloadIndex + "/" + totalPayloads + " spawned at " + FormatPosition(spawn) + " toward " + FormatPosition(impact) + ".");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                error = "Could not spawn " + spec.DisplayName + " heavy payload: " + ex.Message;
                return false;
            }
        }

        private bool TrySpawnRocketProjectile(AirstrikeCallContext context, RocketRunPayloadSpec spec, Vector3 approach, int rocketIndex, int totalRockets, out string error, VisualPayloadEvent releaseEvent = null)
        {
            error = "";
            if (!IsCallActive(context))
            {
                return false;
            }

            if (!TryRequireLiveDeliveryCarrier(context, "rocket " + rocketIndex + "/" + totalRockets, out error))
            {
                return false;
            }

            if (spec == null || string.IsNullOrWhiteSpace(spec.Prefab))
            {
                error = "Rocket payload is not configured correctly.";
                return false;
            }

            var impact = releaseEvent == null
                ? ResolveImpactPosition(GetRocketVolleyImpactPosition(context, approach, rocketIndex, totalRockets))
                : GetReleaseTargetPosition(context, releaseEvent, approach, context.Target.Position, GetStrikeSpreadRadius(context.Strike));
            var spawn = EnsurePositionAboveTerrain(impact - (approach * RocketRunSpawnDistance) + (Vector3.up * RocketRunSpawnHeight), GetPayloadTerrainClearance());
            Vector3 carrierForward;
            Vector3 carrierVelocity;
            var hasCarrierFrame = TryGetCarrierReleaseFrame(context, releaseEvent, out spawn, out carrierForward, out carrierVelocity);
            if (!hasCarrierFrame)
            {
                spawn = EnsurePositionAboveTerrain(impact - (approach * RocketRunSpawnDistance) + (Vector3.up * RocketRunSpawnHeight), GetPayloadTerrainClearance());
            }

            var aimPoint = impact + Vector3.up * 1.25f;
            var direction = aimPoint - spawn;
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = hasCarrierFrame && carrierForward.sqrMagnitude > 0.01f ? carrierForward : approach;
                if (direction.sqrMagnitude <= 0.01f)
                {
                    error = "Rocket approach direction could not be resolved.";
                    return false;
                }
            }

            var launchSpeed = GetReleaseLaunchSpeed(releaseEvent, spec.ProjectileSpeed);
            var velocity = (direction.normalized * launchSpeed) + (hasCarrierFrame ? carrierVelocity : Vector3.zero);
            BaseEntity entity = null;

            try
            {
                entity = GameManager.server.CreateEntity(spec.Prefab, spawn, Quaternion.LookRotation(direction.normalized), true) as BaseEntity;
                if (entity == null)
                {
                    error = "Could not spawn " + spec.DisplayName + " rocket prefab.";
                    return false;
                }

                entity.OwnerID = context.CallerUserId;
                var player = GetCallPlayer(context);
                if (player != null)
                {
                    entity.SetCreatorEntity(player);
                }

                var projectile = entity.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.speed = Math.Max(projectile.speed, launchSpeed);
                    projectile.InitializeVelocity(velocity);
                }

                entity.Spawn();
                RegisterPayloadReleaseMetadata(entity, releaseEvent);

                if (projectile != null)
                {
                    projectile.SetVelocity(velocity);
                }
                else
                {
                    entity.SetVelocity(velocity);
                }

                RunSafeEffect(RocketLaunchEffect, spawn, spec.DisplayName + " rocket launch");
                MarkImpactStarted(context);
                context.State = StrikeExecutionState.Impacting;
                context.SpawnedEntities.Add(entity);
                MarkPayloadReleased(context);

                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " " + spec.Id + " rocket " + rocketIndex + "/" + totalRockets + " spawned at " + FormatPosition(spawn) + " toward " + FormatPosition(impact) + ".");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                error = "Could not spawn " + spec.DisplayName + " rocket: " + ex.Message;
                return false;
            }
        }

        private bool TrySpawnMlrsRocket(AirstrikeCallContext context, MlrsPayloadSpec spec, Vector3 approach, int rocketIndex, int totalRockets, out string error, VisualPayloadEvent releaseEvent = null)
        {
            error = "";
            if (!IsCallActive(context))
            {
                return false;
            }

            if (!TryRequireLiveDeliveryCarrier(context, "MLRS rocket " + rocketIndex + "/" + totalRockets, out error))
            {
                return false;
            }

            if (spec == null || string.IsNullOrWhiteSpace(spec.Prefab))
            {
                error = "MLRS payload is not configured correctly.";
                return false;
            }

            var impact = releaseEvent == null
                ? ResolveImpactPosition(RandomSpreadPosition(context.Target.Position, GetStrikeSpreadRadius(context.Strike)))
                : GetReleaseTargetPosition(context, releaseEvent, approach, context.Target.Position, GetStrikeSpreadRadius(context.Strike));
            var launchJitter = new Vector3(
                UnityEngine.Random.Range(-18f, 18f),
                UnityEngine.Random.Range(-4f, 8f),
                UnityEngine.Random.Range(-18f, 18f));
            var spawn = EnsurePositionAboveTerrain(impact - (approach * MlrsRocketSpawnDistance) + (Vector3.up * MlrsRocketSpawnHeight) + launchJitter, GetPayloadTerrainClearance());
            Vector3 carrierForward;
            Vector3 carrierVelocity;
            var hasCarrierFrame = TryGetCarrierReleaseFrame(context, releaseEvent, out spawn, out carrierForward, out carrierVelocity);
            if (!hasCarrierFrame)
            {
                spawn = EnsurePositionAboveTerrain(impact - (approach * MlrsRocketSpawnDistance) + (Vector3.up * MlrsRocketSpawnHeight) + launchJitter, GetPayloadTerrainClearance());
            }

            var aimPoint = impact + Vector3.up * 1.5f;
            var direction = aimPoint - spawn;
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = hasCarrierFrame && carrierForward.sqrMagnitude > 0.01f ? carrierForward : approach;
                if (direction.sqrMagnitude <= 0.01f)
                {
                    error = "MLRS launch direction could not be resolved.";
                    return false;
                }
            }

            var launchSpeed = GetReleaseLaunchSpeed(releaseEvent, spec.ProjectileSpeed);
            var velocity = (direction.normalized * launchSpeed) + (hasCarrierFrame ? carrierVelocity : Vector3.zero);
            BaseEntity entity = null;

            try
            {
                entity = GameManager.server.CreateEntity(spec.Prefab, spawn, Quaternion.LookRotation(direction.normalized), true) as BaseEntity;
                if (entity == null)
                {
                    error = "Could not spawn " + spec.DisplayName + " rocket prefab.";
                    return false;
                }

                entity.OwnerID = context.CallerUserId;
                var player = GetCallPlayer(context);
                if (player != null)
                {
                    entity.SetCreatorEntity(player);
                }

                var projectile = entity.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.speed = Math.Max(projectile.speed, launchSpeed);
                    projectile.InitializeVelocity(velocity);
                }

                entity.Spawn();
                RegisterPayloadReleaseMetadata(entity, releaseEvent);

                if (projectile != null)
                {
                    projectile.SetVelocity(velocity);
                }
                else
                {
                    entity.SetVelocity(velocity);
                }

                RunSafeEffect(MlrsBackfireEffect, spawn, "MLRS backfire");
                // The MLRS thrust prefab lives in AssetScene-props.other and can spam client error overlays when sent directly.
                MarkImpactStarted(context);
                context.State = StrikeExecutionState.Impacting;
                context.SpawnedEntities.Add(entity);
                MarkPayloadReleased(context);

                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " " + spec.Id + " rocket " + rocketIndex + "/" + totalRockets + " spawned at " + FormatPosition(spawn) + " toward " + FormatPosition(impact) + ".");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                error = "Could not spawn " + spec.DisplayName + " rocket: " + ex.Message;
                return false;
            }
        }

        private bool TrySpawnHomingMissile(AirstrikeCallContext context, HomingMissileSpec spec, Vector3 approach, ulong targetId, int missileIndex, int totalMissiles, out string error, VisualPayloadEvent releaseEvent = null)
        {
            error = "";
            if (!IsCallActive(context))
            {
                return false;
            }

            if (!TryRequireLiveDeliveryCarrier(context, "homing missile " + missileIndex + "/" + totalMissiles, out error))
            {
                return false;
            }

            if (spec == null || string.IsNullOrWhiteSpace(spec.Prefab))
            {
                error = "Homing missile payload is not configured correctly.";
                return false;
            }

            BaseCombatEntity target;
            string targetError;
            if (!TryResolveHomingTarget(context, out target, out targetError))
            {
                if (context.ImpactStarted)
                {
                    return true;
                }

                error = targetError;
                return false;
            }

            var targetPoint = GetReleaseAimPoint(releaseEvent, GetHomingTargetPoint(target), approach, 0f);
            var right = new Vector3(-approach.z, 0f, approach.x);
            if (right.sqrMagnitude <= 0.01f)
            {
                right = Vector3.right;
            }
            else
            {
                right.Normalize();
            }

            var slotOffset = totalMissiles <= 1 ? 0f : (((missileIndex - 1f) / (totalMissiles - 1f)) - 0.5f) * 18f;
            var spawn = EnsurePositionAboveTerrain(targetPoint - (approach * HomingMissileLaunchDistance) + (Vector3.up * HomingMissileLaunchHeight) + (right * slotOffset), GetPayloadTerrainClearance());
            Vector3 carrierForward;
            Vector3 carrierVelocity;
            var hasCarrierFrame = TryGetCarrierReleaseFrame(context, releaseEvent, out spawn, out carrierForward, out carrierVelocity);
            if (!hasCarrierFrame)
            {
                spawn = EnsurePositionAboveTerrain(targetPoint - (approach * HomingMissileLaunchDistance) + (Vector3.up * HomingMissileLaunchHeight) + (right * slotOffset), GetPayloadTerrainClearance());
            }

            var direction = targetPoint - spawn;
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = hasCarrierFrame && carrierForward.sqrMagnitude > 0.01f ? carrierForward : approach;
                if (direction.sqrMagnitude <= 0.01f)
                {
                    error = "Homing missile launch direction could not be resolved.";
                    return false;
                }
            }

            var launchSpeed = GetReleaseLaunchSpeed(releaseEvent, spec.ProjectileSpeed);
            var velocity = (direction.normalized * launchSpeed) + (hasCarrierFrame ? carrierVelocity : Vector3.zero);
            BaseEntity entity = null;

            try
            {
                entity = GameManager.server.CreateEntity(spec.Prefab, spawn, Quaternion.LookRotation(direction.normalized), true) as BaseEntity;
                if (entity == null)
                {
                    error = "Could not spawn " + spec.DisplayName + " missile prefab.";
                    return false;
                }

                entity.OwnerID = context.CallerUserId;
                var player = GetCallPlayer(context);
                if (player != null)
                {
                    entity.SetCreatorEntity(player);
                }

                var projectile = entity.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.speed = Math.Max(projectile.speed, launchSpeed);
                    projectile.InitializeVelocity(velocity);
                }

                entity.Spawn();
                RegisterPayloadReleaseMetadata(entity, releaseEvent);

                if (projectile != null)
                {
                    projectile.SetVelocity(velocity);
                }
                else
                {
                    entity.SetVelocity(velocity);
                }

                MarkImpactStarted(context);
                context.State = StrikeExecutionState.Impacting;
                context.SpawnedEntities.Add(entity);
                MarkPayloadReleased(context);

                ScheduleHomingMissileTrack(context, entity, spec, targetId, spawn, GetPreciseNow(), missileIndex, totalMissiles, releaseEvent);

                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " " + spec.Id + " missile " + missileIndex + "/" + totalMissiles + " spawned at " + FormatPosition(spawn) + " tracking " + (target.ShortPrefabName ?? "vehicle") + "#" + targetId + ".");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                error = "Could not spawn " + spec.DisplayName + " missile: " + ex.Message;
                return false;
            }
        }

        private void ScheduleHomingMissileTrack(AirstrikeCallContext context, BaseEntity missile, HomingMissileSpec spec, ulong targetId, Vector3 launchPosition, double launchStartedAt, int missileIndex, int totalMissiles, VisualPayloadEvent releaseEvent = null)
        {
            ScheduleCallTimer(context, HomingMissileTrackInterval, () =>
            {
                if (!IsCallActive(context) || missile == null || missile.IsDestroyed)
                {
                    return;
                }

                var elapsed = GetPreciseNow() - launchStartedAt;
                if (elapsed > GetReleaseTrackingSeconds(context.Strike, releaseEvent))
                {
                    missile.Kill(BaseNetworkable.DestroyMode.None);
                    if (config.General.DebugMode)
                    {
                        Puts(context.Strike.Id + " homing missile " + missileIndex + "/" + totalMissiles + " expired after tracking timeout.");
                    }
                    return;
                }

                var traveled = Vector3.Distance(launchPosition, missile.transform.position);
                if (traveled > GetReleaseTrackingDistance(context.Strike, releaseEvent))
                {
                    missile.Kill(BaseNetworkable.DestroyMode.None);
                    if (config.General.DebugMode)
                    {
                        Puts(context.Strike.Id + " homing missile " + missileIndex + "/" + totalMissiles + " expired after max tracking distance.");
                    }
                    return;
                }

                var target = ResolveClassifiableTargetEntity(FindEntity(targetId)) as BaseCombatEntity;
                if (target == null || target.IsDestroyed || target.IsDead())
                {
                    missile.Kill(BaseNetworkable.DestroyMode.None);
                    if (config.General.DebugMode)
                    {
                        Puts(context.Strike.Id + " homing missile " + missileIndex + "/" + totalMissiles + " stopped because the target is gone.");
                    }
                    return;
                }

                var targetPoint = GetHomingTargetPoint(target);
                var direction = targetPoint - missile.transform.position;
                if (direction.sqrMagnitude <= 0.01f)
                {
                    DetonateHomingMissile(context, missile, target, targetPoint, missileIndex, totalMissiles, releaseEvent);
                    return;
                }

                var proximity = Mathf.Clamp(Math.Max(HomingMissileProximityRadius, GetReleaseSplashRadius(context.Strike, releaseEvent) * 0.4f), 2f, 8f);
                if (direction.magnitude <= proximity)
                {
                    DetonateHomingMissile(context, missile, target, targetPoint, missileIndex, totalMissiles, releaseEvent);
                    return;
                }

                var launchSpeed = GetReleaseLaunchSpeed(releaseEvent, spec.ProjectileSpeed);
                var velocity = direction.normalized * launchSpeed;
                var projectile = missile.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.speed = Math.Max(projectile.speed, launchSpeed);
                    projectile.SetVelocity(velocity);
                }
                else
                {
                    missile.SetVelocity(velocity);
                }

                missile.transform.rotation = Quaternion.LookRotation(direction.normalized);
                missile.SendNetworkUpdate();
                ScheduleHomingMissileTrack(context, missile, spec, targetId, launchPosition, launchStartedAt, missileIndex, totalMissiles, releaseEvent);
            });
        }

        private bool TryResolveHomingTarget(AirstrikeCallContext context, out BaseCombatEntity target, out string error)
        {
            target = null;
            error = "";

            if (context == null || context.Target == null || context.Target.EntityId == 0UL)
            {
                error = "Homing missiles require a vehicle target with an entity ID.";
                return false;
            }

            var entity = ResolveClassifiableTargetEntity(FindEntity(context.Target.EntityId));
            target = entity as BaseCombatEntity;
            if (entity == null || entity.IsDestroyed || target == null || target.IsDead())
            {
                error = "That vehicle target is no longer valid.";
                return false;
            }

            if (ClassifyTarget(entity) != AirstrikeTargetType.VehiclePing)
            {
                error = "The stored homing target is no longer a valid vehicle.";
                return false;
            }

            return true;
        }

        private Vector3 GetHomingTargetPoint(BaseCombatEntity target)
        {
            if (target == null)
            {
                return Vector3.zero;
            }

            try
            {
                return target.CenterPoint();
            }
            catch
            {
                return target.transform.position + Vector3.up;
            }
        }

        private float GetHomingTrackingSeconds(StrikeDefinition strike)
        {
            var configured = strike == null ? 0f : strike.MaxTrackingSeconds;
            return configured > 0f ? Mathf.Clamp(configured, 1f, 60f) : 10f;
        }

        private float GetHomingTrackingDistance(StrikeDefinition strike)
        {
            var configured = strike == null ? 0f : strike.MaxTrackingDistance;
            return configured > 0f ? Mathf.Clamp(configured, 50f, 1000f) : 300f;
        }

        private float GetHomingSplashRadius(StrikeDefinition strike)
        {
            var configured = strike == null ? 0f : strike.SplashRadius;
            return configured > 0f ? Mathf.Clamp(configured, 0.5f, 50f) : 4f;
        }

        private void DetonateHomingMissile(AirstrikeCallContext context, BaseEntity missile, BaseCombatEntity target, Vector3 impact, int missileIndex, int totalMissiles, VisualPayloadEvent releaseEvent = null)
        {
            if (!IsCallActive(context))
            {
                return;
            }

            try
            {
                Effect.server.Run(BradleyMainCannonShellExplosionEffect, impact);
                Effect.server.Run(BulletImpactEffect, impact);
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Homing missile effect failed: " + ex.Message);
                }
            }

            int damagedCount;
            ApplyHomingMissileDamage(context, target, impact, out damagedCount, releaseEvent);

            if (missile != null && !missile.IsDestroyed)
            {
                missile.Kill(BaseNetworkable.DestroyMode.None);
            }

            MarkImpactStarted(context);
            context.State = StrikeExecutionState.Impacting;

            if (config.General.DebugMode)
            {
                Puts(context.Strike.Id + " homing missile " + missileIndex + "/" + totalMissiles + " detonated at " + FormatPosition(impact) + " and damaged " + damagedCount + " combat entity/entities.");
            }
        }

        private void ApplyHomingMissileDamage(AirstrikeCallContext context, BaseCombatEntity target, Vector3 impact, out int damagedCount, VisualPayloadEvent releaseEvent = null)
        {
            damagedCount = 0;
            var player = GetCallPlayer(context);
            var damaged = new HashSet<BaseCombatEntity>();

            if (target != null && !target.IsDestroyed && !target.IsDead())
            {
                var vehicleScale = Mathf.Clamp(context.Strike.VehicleDamageScale, 0f, 10f)
                    * NormalizePositiveMultiplier(context.Strike.VehicleDamageMultiplier)
                    * GetGlobalDamageScale("Vehicles")
                    * GetStrikeDamageScale(context.Strike, "Vehicles")
                    * GetReleaseDamageScale(releaseEvent, "Vehicles")
                    * GetReleaseVehicleDamageScale(releaseEvent);
                var directDamage = HomingMissileBaseVehicleDamage * Mathf.Clamp(vehicleScale, 0f, 10f);
                if (directDamage > 0f)
                {
                    ApplyHomingDamageToEntity(target, directDamage, player, ref damagedCount);
                    damaged.Add(target);
                }
            }

            var radius = GetReleaseSplashRadius(context.Strike, releaseEvent);
            if (radius <= 0f)
            {
                return;
            }

            var entities = Pool.Get<List<BaseEntity>>();
            try
            {
                Vis.Entities(impact, radius, entities, TargetRaycastLayer, QueryTriggerInteraction.Ignore);
                foreach (var entity in entities)
                {
                    var combatEntity = entity as BaseCombatEntity;
                    if (combatEntity == null || combatEntity.IsDestroyed || combatEntity.IsDead() || !damaged.Add(combatEntity))
                    {
                        continue;
                    }

                    var key = GetDamageScaleKey(combatEntity);
                    var scale = GetGlobalDamageScale(key) * GetStrikeDamageScale(context.Strike, key) * GetReleaseDamageScale(releaseEvent, key);
                    if (string.Equals(key, "Vehicles", StringComparison.OrdinalIgnoreCase))
                    {
                        scale *= NormalizePositiveMultiplier(context.Strike.VehicleDamageMultiplier) * GetReleaseVehicleDamageScale(releaseEvent);
                    }
                    var distance = Vector3.Distance(impact, GetHomingTargetPoint(combatEntity));
                    var falloff = Mathf.Clamp01(1f - (distance / radius));
                    var damage = HomingMissileBaseSplashDamage * Mathf.Clamp(scale, 0f, 10f) * Mathf.Clamp(falloff, 0.15f, 1f);
                    if (damage <= 0f)
                    {
                        continue;
                    }

                    ApplyHomingDamageToEntity(combatEntity, damage, player, ref damagedCount);
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref entities);
            }
        }

        private void ApplyHomingDamageToEntity(BaseCombatEntity combatEntity, float damage, BasePlayer attacker, ref int damagedCount)
        {
            try
            {
                combatEntity.Hurt(damage, Rust.DamageType.Explosion, attacker, false);
                damagedCount++;
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Homing missile damage failed for " + (combatEntity.ShortPrefabName ?? combatEntity.GetType().Name) + ": " + ex.Message);
                }
            }
        }

        private bool TrySpawnMortarShell(AirstrikeCallContext context, MortarPayloadSpec spec, int shellIndex, int totalShells, out string error, VisualPayloadEvent releaseEvent = null)
        {
            error = "";
            if (!IsCallActive(context))
            {
                return false;
            }

            if (spec == null || string.IsNullOrWhiteSpace(spec.Prefab))
            {
                error = "Mortar shell payload is not configured correctly.";
                return false;
            }

            var approach = context.PlannedDeliveryApproach.sqrMagnitude <= 0.01f ? GetRocketApproachDirection(context) : context.PlannedDeliveryApproach;
            var impact = releaseEvent == null
                ? ResolveImpactPosition(RandomSpreadPosition(context.Target.Position, GetStrikeSpreadRadius(context.Strike)))
                : GetReleaseTargetPosition(context, releaseEvent, approach, context.Target.Position, GetStrikeSpreadRadius(context.Strike));
            var spawn = EnsurePositionAboveTerrain(impact + Vector3.up * MortarShellSpawnHeight, GetPayloadTerrainClearance());
            Vector3 carrierForward;
            Vector3 carrierVelocity;
            var hasCarrierFrame = TryGetCarrierReleaseFrame(context, releaseEvent, out spawn, out carrierForward, out carrierVelocity);
            if (!hasCarrierFrame)
            {
                spawn = EnsurePositionAboveTerrain(impact + Vector3.up * MortarShellSpawnHeight, GetPayloadTerrainClearance());
            }

            var direction = impact + (Vector3.up * 0.2f) - spawn;
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = hasCarrierFrame && carrierForward.sqrMagnitude > 0.01f ? carrierForward : Vector3.down;
            }
            else
            {
                direction.Normalize();
            }

            var launchSpeed = GetReleaseLaunchSpeed(releaseEvent, MortarShellDownwardVelocity);
            var velocity = (direction * launchSpeed) + (hasCarrierFrame ? carrierVelocity : Vector3.zero);
            BaseEntity entity = null;

            try
            {
                RunMortarLaunchVisual(context, shellIndex);
                entity = GameManager.server.CreateEntity(spec.Prefab, spawn, Quaternion.LookRotation(direction), true) as BaseEntity;
                if (entity == null)
                {
                    error = "Could not spawn " + spec.DisplayName + " mortar shell prefab.";
                    return false;
                }

                entity.OwnerID = context.CallerUserId;
                var player = GetCallPlayer(context);
                if (player != null)
                {
                    entity.SetCreatorEntity(player);
                }

                var projectile = entity.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.speed = Math.Max(projectile.speed, launchSpeed);
                    projectile.InitializeVelocity(velocity);
                }

                entity.Spawn();
                RegisterPayloadReleaseMetadata(entity, releaseEvent);

                if (projectile != null)
                {
                    projectile.SetVelocity(velocity);
                }
                else
                {
                    entity.SetVelocity(velocity);
                }

                MarkImpactStarted(context);
                context.State = StrikeExecutionState.Impacting;
                context.SpawnedEntities.Add(entity);
                MarkPayloadReleased(context);

                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " " + spec.Id + " shell " + shellIndex + "/" + totalShells + " spawned at " + FormatPosition(spawn) + " toward " + FormatPosition(impact) + ".");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                error = "Could not spawn " + spec.DisplayName + " mortar shell: " + ex.Message;
                return false;
            }
        }

        private bool TryRunA10Pulse(AirstrikeCallContext context, A10StrafeSpec spec, Vector3 direction, int pulseIndex, int totalPulses, out string error, VisualPayloadEvent releaseEvent = null)
        {
            error = "";
            if (!IsCallActive(context))
            {
                return false;
            }

            if (!TryRequireLiveDeliveryCarrier(context, "A-10 cannon pulse " + pulseIndex + "/" + totalPulses, out error))
            {
                return false;
            }

            if (spec == null)
            {
                error = "A-10 strafe payload is not configured correctly.";
                return false;
            }

            try
            {
                var impact = releaseEvent == null
                    ? ResolveImpactPosition(GetA10PulsePosition(context, direction, pulseIndex, totalPulses))
                    : GetReleaseTargetPosition(context, releaseEvent, direction, context.Target.Position, 0f);
                Vector3 carrierOrigin;
                Vector3 carrierForward;
                Vector3 carrierVelocity;
                var hasCarrierFrame = TryGetCarrierReleaseFrame(context, releaseEvent, out carrierOrigin, out carrierForward, out carrierVelocity);
                RunA10PulseEffects(impact, direction, pulseIndex, hasCarrierFrame, carrierOrigin);

                int damagedCount;
                ApplyA10DamagePulse(context, spec, impact, out damagedCount, releaseEvent);

                MarkImpactStarted(context);
                context.State = StrikeExecutionState.Impacting;
                MarkPayloadReleased(context);

                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " " + spec.Id + " pulse " + pulseIndex + "/" + totalPulses + " resolved at " + FormatPosition(impact) + " and damaged " + damagedCount + " combat entity/entities.");
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Could not run " + spec.DisplayName + " pulse: " + ex.Message;
                return false;
            }
        }

        private void RunA10PulseEffects(Vector3 impact, Vector3 direction, int pulseIndex, bool hasMuzzleOrigin = false, Vector3 muzzleOrigin = default(Vector3))
        {
            try
            {
                var effectPosition = impact + Vector3.up * 0.1f;
                Effect.server.Run(BulletImpactEffect, effectPosition);

                if (pulseIndex == 1 || pulseIndex % A10MuzzleEffectInterval == 0)
                {
                    var muzzlePosition = hasMuzzleOrigin
                        ? EnsurePositionAboveTerrain(muzzleOrigin, GetPayloadTerrainClearance())
                        : EnsurePositionAboveTerrain(impact - (direction * 45f) + (Vector3.up * 32f), GetPayloadTerrainClearance());
                    Effect.server.Run(BradleyMainCannonAttackEffect, muzzlePosition);
                    Effect.server.Run(BradleyMainCannonShellExplosionEffect, effectPosition);
                }
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("A-10 effect failed: " + ex.Message);
                }
            }
        }

        private void ApplyA10DamagePulse(AirstrikeCallContext context, A10StrafeSpec spec, Vector3 impact, out int damagedCount, VisualPayloadEvent releaseEvent = null)
        {
            damagedCount = 0;
            var radius = GetReleaseImpactRadius(context.Strike, releaseEvent);
            var baseDamage = Math.Max(0.1f, spec.BaseDamage);
            var player = GetCallPlayer(context);
            var entities = Pool.Get<List<BaseEntity>>();
            var damaged = new HashSet<BaseCombatEntity>();

            try
            {
                Vis.Entities(impact, radius, entities, TargetRaycastLayer, QueryTriggerInteraction.Ignore);
                foreach (var entity in entities)
                {
                    var combatEntity = entity as BaseCombatEntity;
                    if (combatEntity == null || combatEntity.IsDestroyed || combatEntity.IsDead() || !damaged.Add(combatEntity))
                    {
                        continue;
                    }

                    var scale = GetA10DamageScale(context.Strike, combatEntity, releaseEvent);
                    var damage = baseDamage * scale;
                    if (damage <= 0f)
                    {
                        continue;
                    }

                    try
                    {
                        combatEntity.Hurt(damage, Rust.DamageType.Bullet, player, false);
                        damagedCount++;
                    }
                    catch (Exception ex)
                    {
                        if (config.General.DebugMode)
                        {
                            Puts("A-10 damage failed for " + (combatEntity.ShortPrefabName ?? combatEntity.GetType().Name) + ": " + ex.Message);
                        }
                    }
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref entities);
            }
        }

        private float GetA10DamageScale(StrikeDefinition strike, BaseCombatEntity entity, VisualPayloadEvent releaseEvent = null)
        {
            var key = GetDamageScaleKey(entity);
            var scale = GetGlobalDamageScale(key) * GetStrikeDamageScale(strike, key) * GetReleaseDamageScale(releaseEvent, key);
            if (string.Equals(key, "Vehicles", StringComparison.OrdinalIgnoreCase))
            {
                scale *= NormalizePositiveMultiplier(strike == null ? 1f : strike.VehicleDamageMultiplier) * GetReleaseVehicleDamageScale(releaseEvent);
            }

            return Mathf.Clamp(scale, 0f, 100f);
        }

        private float GetGlobalDamageScale(string key)
        {
            if (config?.DamageScales == null)
            {
                return 1f;
            }

            switch ((key ?? "").Trim().ToLowerInvariant())
            {
                case "players":
                    return Mathf.Clamp(config.DamageScales.Players, 0f, 10f);
                case "buildings":
                    return Mathf.Clamp(config.DamageScales.Buildings, 0f, 10f);
                case "vehicles":
                    return Mathf.Clamp(config.DamageScales.Vehicles, 0f, 10f);
                case "turrets":
                    return Mathf.Clamp(config.DamageScales.Turrets, 0f, 10f);
                case "deployables":
                    return Mathf.Clamp(config.DamageScales.Deployables, 0f, 10f);
                default:
                    return 1f;
            }
        }

        private float GetStrikeDamageScale(StrikeDefinition strike, string key)
        {
            var scale = NormalizePositiveMultiplier(strike == null ? 1f : strike.DamageMultiplier);
            if (strike?.DamageScales == null || string.IsNullOrWhiteSpace(key))
            {
                return scale;
            }

            foreach (var entry in strike.DamageScales)
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return Mathf.Clamp(scale * Mathf.Clamp(entry.Value <= 0f ? 1f : entry.Value, 0f, 10f), 0f, 100f);
                }
            }

            return scale;
        }

        private string GetDamageScaleKey(BaseCombatEntity entity)
        {
            if (entity is BasePlayer)
            {
                return "Players";
            }

            if (entity is BuildingBlock)
            {
                return "Buildings";
            }

            var shortName = (entity?.ShortPrefabName ?? "").ToLowerInvariant();
            if (shortName.Contains("turret") || shortName.Contains("guntrap") || shortName.Contains("sam_site"))
            {
                return "Turrets";
            }

            if (shortName.Contains("vehicle")
                || shortName.Contains("bradley")
                || shortName.Contains("car")
                || shortName.Contains("heli")
                || shortName.Contains("copter")
                || shortName.Contains("boat")
                || shortName.Contains("rhib")
                || shortName.Contains("tugboat")
                || shortName.Contains("snowmobile")
                || shortName.Contains("motorbike"))
            {
                return "Vehicles";
            }

            return "Deployables";
        }

        private Vector3 RandomSpreadPosition(Vector3 center, float spreadRadius)
        {
            var radius = Mathf.Clamp(spreadRadius, 0f, 100f);
            if (radius <= 0.01f)
            {
                return center;
            }

            var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            var distance = Mathf.Sqrt(UnityEngine.Random.value) * radius;
            return center + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
        }

        private float GetStrikeSpreadRadius(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return 0f;
            }

            return Mathf.Clamp(strike.SpreadRadius * NormalizePositiveMultiplier(strike.SpreadMultiplier), 0f, 500f);
        }

        private Vector3 ResolveImpactPosition(Vector3 position)
        {
            RaycastHit hit;
            var startY = position.y + 120f;
            try
            {
                if (TerrainMeta.HeightMap != null)
                {
                    startY = Mathf.Max(startY, TerrainMeta.HeightMap.GetHeight(position) + 120f);
                }
            }
            catch
            {
            }

            var start = new Vector3(position.x, startY, position.z);
            var rayDistance = Mathf.Max(260f, startY - position.y + 140f);
            if (Physics.Raycast(start, Vector3.down, out hit, rayDistance, ImpactRaycastLayer, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            float surfaceY;
            if (TryGetFlightSurfaceHeight(position, out surfaceY))
            {
                position.y = surfaceY;
                return position;
            }

            try
            {
                if (TerrainMeta.HeightMap != null)
                {
                    position.y = TerrainMeta.HeightMap.GetHeight(position);
                }
            }
            catch
            {
            }

            return position;
        }

        private void IncrementStat(string key)
        {
            IncrementStatBy(key, 1);
        }

        private void IncrementStatBy(string key, int amount)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (amount <= 0)
            {
                return;
            }

            if (storedData.Stats == null)
            {
                storedData.Stats = new Dictionary<string, int>();
            }

            int count;
            storedData.Stats.TryGetValue(key, out count);
            storedData.Stats[key] = count + amount;
        }

        private void RecordValidationAudit(BasePlayer player, string requestedStrikeId, ValidationResult validation)
        {
            if (player == null || validation == null || validation.Success)
            {
                return;
            }

            StrikeDefinition strike = validation.Strike;
            if (strike == null && !string.IsNullOrWhiteSpace(requestedStrikeId))
            {
                TryGetStrike(requestedStrikeId, out strike);
            }

            var target = validation.Target ?? GetLatestTarget(player, false);
            var message = validation.ReasonCode + ": " + validation.UserMessage;
            var record = CreateAuditRecord(
                player,
                player.userID.ToString() + ":" + (requestedStrikeId ?? "unknown") + ":" + GetNow().ToString("0", CultureInfo.InvariantCulture),
                requestedStrikeId,
                strike,
                target,
                validation.FinalRPCost,
                false,
                false,
                false,
                false,
                StrikeExecutionState.Failed.ToString(),
                "validation_failed",
                message);

            AddAuditRecord(record, true);
        }

        private void RecordStrikeAudit(AirstrikeCallContext context, string result, string message, bool printToConsole)
        {
            if (context == null)
            {
                return;
            }

            var record = CreateAuditRecord(
                GetCallPlayer(context),
                context.CallId,
                context.Strike == null ? "" : context.Strike.Id,
                context.Strike,
                context.Target,
                context.FinalRPCost,
                context.RpCharged,
                context.TokenConsumed,
                context.RefundAttempted,
                context.ImpactStarted,
                context.State.ToString(),
                result,
                message);

            if (string.IsNullOrWhiteSpace(record.PlayerId))
            {
                record.PlayerId = context.CallerUserId.ToString();
            }

            if (string.IsNullOrWhiteSpace(record.PlayerName))
            {
                record.PlayerName = context.CallerName ?? "";
            }

            if (string.IsNullOrWhiteSpace(record.TeamId) && context.CallerTeamId != 0UL)
            {
                record.TeamId = context.CallerTeamId.ToString();
            }

            AddAuditRecord(record, printToConsole);
        }

        private StrikeCallAuditRecord CreateAuditRecord(BasePlayer player, string callId, string requestedStrikeId, StrikeDefinition strike, AirstrikeTarget target, int rpCost, bool rpCharged, bool tokenConsumed, bool refundAttempted, bool impactStarted, string state, string result, string message)
        {
            var playerId = player == null ? "" : player.userID.ToString();
            var playerName = player == null ? "" : player.displayName ?? player.UserIDString;
            var teamId = player == null || player.currentTeam == 0UL ? "" : player.currentTeam.ToString();
            var strikeId = strike == null || string.IsNullOrWhiteSpace(strike.Id) ? requestedStrikeId ?? "" : strike.Id;
            var strikeName = strike == null || string.IsNullOrWhiteSpace(strike.DisplayName) ? strikeId : strike.DisplayName;

            return new StrikeCallAuditRecord
            {
                Time = GetNow(),
                CallId = callId ?? "",
                PlayerId = playerId,
                PlayerName = playerName,
                TeamId = teamId,
                StrikeId = strikeId,
                StrikeName = strikeName,
                TargetType = target == null ? "none" : FormatTargetType(target.Type),
                TargetPosition = target == null ? "none" : FormatPosition(target.Position),
                TargetEntityId = target == null || target.EntityId == 0UL ? "" : target.EntityId.ToString(),
                TargetEntity = target == null ? "" : target.EntityShortPrefabName ?? "",
                RPCost = rpCost,
                RpCharged = rpCharged,
                TokenConsumed = tokenConsumed,
                RefundAttempted = refundAttempted,
                ImpactStarted = impactStarted,
                Result = result ?? "",
                Message = SanitizeAuditMessage(message),
                State = state ?? ""
            };
        }

        private void AddAuditRecord(StrikeCallAuditRecord record, bool printToConsole)
        {
            if (record == null)
            {
                return;
            }

            if (storedData.RecentCalls == null)
            {
                storedData.RecentCalls = new List<StrikeCallAuditRecord>();
            }

            storedData.RecentCalls.Add(record);
            TrimRecentCallHistory();

            if (printToConsole)
            {
                Puts("Audit: " + FormatAuditRecordForConsole(record));
            }

            SendAuditWebhookIfNeeded(record);
        }

        private void SendAuditWebhookIfNeeded(StrikeCallAuditRecord record)
        {
            if (record == null || !ShouldSendAuditWebhook(record))
            {
                return;
            }

            var settings = config.AuditWebhooks;
            var url = settings.DiscordWebhookUrl.Trim();
            var payload = BuildDiscordAuditPayload(record, settings);
            var body = JsonConvert.SerializeObject(payload);
            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            };

            try
            {
                webrequest.Enqueue(url, body, (code, response) =>
                {
                    if (code >= 200 && code < 300)
                    {
                        return;
                    }

                    PrintWarning("Airstrike audit webhook failed with HTTP " + code + ": " + TruncateForDiscord(response ?? "", 180));
                }, this, RequestMethod.POST, headers);
            }
            catch (Exception ex)
            {
                PrintWarning("Airstrike audit webhook could not be sent: " + ex.Message);
            }
        }

        private bool ShouldSendAuditWebhook(StrikeCallAuditRecord record)
        {
            if (record == null || config?.AuditWebhooks == null || !config.AuditWebhooks.Enabled)
            {
                return false;
            }

            var url = config.AuditWebhooks.DiscordWebhookUrl ?? "";
            if (!IsDiscordWebhookUrl(url))
            {
                if (!auditWebhookConfigWarningPrinted)
                {
                    PrintWarning("AuditWebhooks.Enabled is true, but AuditWebhooks.DiscordWebhookUrl is empty or is not a Discord webhook URL.");
                    auditWebhookConfigWarningPrinted = true;
                }

                return false;
            }

            var result = (record.Result ?? "").ToLowerInvariant();
            if (result == "validation_failed")
            {
                return config.AuditWebhooks.SendValidationFailures;
            }

            if (result == "started")
            {
                return config.AuditWebhooks.SendStartedCalls;
            }

            if (result == "completed")
            {
                return config.AuditWebhooks.SendCompletedCalls;
            }

            if (result.Contains("cancelled"))
            {
                return config.AuditWebhooks.SendPlayerCancels;
            }

            if (result.Contains("failed") || result.Contains("refund") || result.Contains("intercepted") || result == "charge_failed")
            {
                return config.AuditWebhooks.SendFailuresAndRefunds;
            }

            return false;
        }

        private bool IsDiscordWebhookUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return url.IndexOf("discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("discordapp.com/api/webhooks/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private DiscordWebhookPayload BuildDiscordAuditPayload(StrikeCallAuditRecord record, AuditWebhookSettings settings)
        {
            var title = "Airstrike " + SafeDiscordValue(record.Result, "audit");
            var description = SafeDiscordValue(record.StrikeName, record.StrikeId) + " by " + SafeDiscordValue(record.PlayerName, record.PlayerId);
            var content = string.IsNullOrWhiteSpace(settings.MentionText) ? null : settings.MentionText.Trim();

            var payload = new DiscordWebhookPayload
            {
                Content = content,
                Username = string.IsNullOrWhiteSpace(settings.Username) ? null : settings.Username.Trim(),
                AvatarUrl = string.IsNullOrWhiteSpace(settings.AvatarUrl) ? null : settings.AvatarUrl.Trim()
            };

            var embed = new DiscordWebhookEmbed
            {
                Title = TruncateForDiscord(title, 256),
                Description = TruncateForDiscord(description, 300),
                Color = GetAuditWebhookColor(record.Result),
                Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };

            AddDiscordField(embed, "Player", SafeDiscordValue(record.PlayerName, "unknown") + " (" + SafeDiscordValue(record.PlayerId, "unknown") + ")", true);
            AddDiscordField(embed, "Strike", SafeDiscordValue(record.StrikeId, "unknown"), true);
            AddDiscordField(embed, "Target", SafeDiscordValue(record.TargetType, "none") + " @ " + SafeDiscordValue(record.TargetPosition, "unknown"), false);
            AddDiscordField(embed, "Costs", "RP " + record.RPCost + ", item " + record.TokenConsumed + ", refund " + record.RefundAttempted, true);
            AddDiscordField(embed, "State", SafeDiscordValue(record.State, "unknown") + ", impact " + record.ImpactStarted, true);

            if (!string.IsNullOrWhiteSpace(record.TargetEntityId) || !string.IsNullOrWhiteSpace(record.TargetEntity))
            {
                AddDiscordField(embed, "Entity", SafeDiscordValue(record.TargetEntity, "entity") + " #" + SafeDiscordValue(record.TargetEntityId, "unknown"), false);
            }

            if (!string.IsNullOrWhiteSpace(record.Message))
            {
                AddDiscordField(embed, "Message", record.Message, false);
            }

            payload.Embeds.Add(embed);
            return payload;
        }

        private void AddDiscordField(DiscordWebhookEmbed embed, string name, string value, bool inline)
        {
            if (embed == null)
            {
                return;
            }

            embed.Fields.Add(new DiscordWebhookField
            {
                Name = TruncateForDiscord(name, 256),
                Value = TruncateForDiscord(SafeDiscordValue(value, "n/a"), 1024),
                Inline = inline
            });
        }

        private int GetAuditWebhookColor(string result)
        {
            result = (result ?? "").ToLowerInvariant();
            if (result == "completed")
            {
                return 0x2ecc71;
            }

            if (result == "started")
            {
                return 0x3498db;
            }

            if (result.Contains("failed") || result.Contains("refund") || result == "charge_failed")
            {
                return 0xe74c3c;
            }

            if (result.Contains("cancelled"))
            {
                return 0xf1c40f;
            }

            return 0xe67e22;
        }

        private string SafeDiscordValue(string primary, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary.Trim();
            }

            return string.IsNullOrWhiteSpace(fallback) ? "n/a" : fallback.Trim();
        }

        private string TruncateForDiscord(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 0)
            {
                return "";
            }

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private void TrimRecentCallHistory()
        {
            if (storedData == null)
            {
                return;
            }

            if (storedData.RecentCalls == null)
            {
                storedData.RecentCalls = new List<StrikeCallAuditRecord>();
                return;
            }

            var limit = GetRecentCallHistoryLimit();
            if (limit <= 0)
            {
                storedData.RecentCalls.Clear();
                return;
            }

            var extra = storedData.RecentCalls.Count - limit;
            if (extra > 0)
            {
                storedData.RecentCalls.RemoveRange(0, extra);
            }
        }

        private int GetRecentCallHistoryLimit()
        {
            var limit = config?.General == null ? DefaultRecentCallHistoryLimit : config.General.RecentCallHistoryLimit;
            return Math.Min(200, Math.Max(0, limit));
        }

        private string FormatAuditRecordForConsole(StrikeCallAuditRecord record)
        {
            if (record == null)
            {
                return "";
            }

            var entity = string.IsNullOrWhiteSpace(record.TargetEntityId) ? "" : "#" + record.TargetEntityId;
            var message = string.IsNullOrWhiteSpace(record.Message) ? "" : " message=\"" + record.Message + "\"";
            return record.Result
                + " call=" + record.CallId
                + " player=" + record.PlayerName + "(" + record.PlayerId + ")"
                + " strike=" + record.StrikeId
                + " target=" + record.TargetType + "@" + record.TargetPosition + entity
                + " rpCost=" + record.RPCost
                + " rpCharged=" + record.RpCharged
                + " tokenConsumed=" + record.TokenConsumed
                + " refundAttempted=" + record.RefundAttempted
                + " impactStarted=" + record.ImpactStarted
                + " state=" + record.State
                + message;
        }

        private string FormatAuditRecordForChat(StrikeCallAuditRecord record)
        {
            if (record == null)
            {
                return "";
            }

            var age = record.Time <= 0 ? "unknown age" : FormatSeconds(GetNow() - record.Time) + " ago";
            var entity = string.IsNullOrWhiteSpace(record.TargetEntityId) ? "" : " #" + record.TargetEntityId;
            var message = string.IsNullOrWhiteSpace(record.Message) ? "" : " - " + record.Message;
            return age + ": " + record.Result + " " + record.StrikeId + " by " + record.PlayerName + " at " + record.TargetType + " " + record.TargetPosition + entity + ", RP " + record.RPCost + ", item " + record.TokenConsumed + ", refund " + record.RefundAttempted + message;
        }

        private string SanitizeAuditMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "";
            }

            var sanitized = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return sanitized.Length <= 180 ? sanitized : sanitized.Substring(0, 180);
        }

        private ValidationResult ValidateStrikeCall(BasePlayer player, string strikeId)
        {
            var result = new ValidationResult
            {
                Success = false,
                ReasonCode = "unknown",
                UserMessage = "Could not validate that airstrike."
            };

            if (player == null || !player.IsConnected)
            {
                return Fail(result, "player_invalid", "Player is not connected.");
            }

            if (player.IsDead())
            {
                return Fail(result, "player_dead", "You must be alive to call an airstrike.");
            }

            if (!permission.UserHasPermission(player.UserIDString, UsePermission) && !IsAdmin(player))
            {
                return Fail(result, "missing_use_permission", "You do not have permission to call airstrikes.");
            }

            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                return Fail(result, "unknown_strike", "Unknown airstrike ID '" + strikeId + "'. Use /" + GetOpenCommand() + " list.");
            }

            if (!strike.Enabled)
            {
                return Fail(result, "strike_disabled", strike.DisplayName + " is disabled.");
            }

            IStrikeExecutor validationExecutor;
            string executorMessage;
            if (!TryGetExecutor(strike, out validationExecutor, out executorMessage))
            {
                return Fail(result, "unsupported_executor", executorMessage);
            }

            if (!string.IsNullOrWhiteSpace(strike.PermissionRequired)
                && !permission.UserHasPermission(player.UserIDString, strike.PermissionRequired)
                && !IsAdmin(player))
            {
                return Fail(result, "missing_strike_permission", "You do not have permission to call " + strike.DisplayName + ".");
            }

            var target = GetLatestTarget(player, true);
            if (target == null)
            {
                return Fail(result, "missing_target", "No fresh airstrike target found. Aim with " + GetAirstrikeItemDisplayName() + " and place a ping first.");
            }

            if (target.Source == DebugRaycastSource && !config.General.AllowFallbackRaycastTargeting && !IsAdmin(player))
            {
                return Fail(result, "debug_target_not_allowed", "Raycast targets are only enabled for admin testing.");
            }

            var acceptedTargetTypes = GetAcceptedTargetTypes(strike);
            if (acceptedTargetTypes.Count == 0)
            {
                return Fail(result, "invalid_strike_target_type", strike.DisplayName + " has no valid accepted target types.");
            }

            if (!StrikeAcceptsTargetType(strike, target.Type))
            {
                return Fail(result, "target_type_mismatch", strike.DisplayName + " accepts " + FormatAcceptedTargetTypes(strike) + ", but your target is " + FormatTargetType(target.Type) + ".");
            }

            if (target.Type == AirstrikeTargetType.VehiclePing)
            {
                if (target.EntityId == 0UL)
                {
                    return Fail(result, "vehicle_target_missing_entity", strike.DisplayName + " requires a vehicle target with entity tracking. Aim " + GetAirstrikeItemDisplayName() + " directly at a vehicle and ping it.");
                }

                var entity = ResolveClassifiableTargetEntity(FindEntity(target.EntityId));
                var combatEntity = entity as BaseCombatEntity;
                if (entity == null
                    || entity.IsDestroyed
                    || ClassifyTarget(entity) != AirstrikeTargetType.VehiclePing
                    || (combatEntity != null && combatEntity.IsDead()))
                {
                    return Fail(result, "vehicle_target_gone", "That vehicle target is no longer valid.");
                }
            }

            string homingProfileMessage;
            if (!ValidateHomingReleaseProfileTarget(strike, target, out homingProfileMessage))
            {
                return Fail(result, "homing_release_requires_vehicle", homingProfileMessage);
            }

            var distance = Vector3.Distance(player.transform.position, target.Position);
            if (distance > config.General.MaxCallRange)
            {
                return Fail(result, "target_too_far", "Target is too far away: " + FormatMeters(distance) + " / " + FormatMeters(config.General.MaxCallRange) + ".");
            }

            if (distance < config.General.MinimumDistanceFromCaller)
            {
                return Fail(result, "target_too_close", "Target is too close. Minimum distance is " + FormatMeters(config.General.MinimumDistanceFromCaller) + ".");
            }

            if (config.General.RequireLineOfSightToPing && !HasLineOfSightToTarget(player, target))
            {
                return Fail(result, "line_of_sight_blocked", "Line of sight to the target is blocked.");
            }

            if (config.General.BlockSafeZones && IsSafeZonePosition(target.Position, config.General.SafeZoneBlockRadius))
            {
                return Fail(result, "safe_zone_blocked", "Airstrikes cannot be called into safe zones.");
            }

            if (ShouldCheckMonumentBlock(strike) && IsBlockedMonumentPosition(target.Position, out var monumentName))
            {
                return Fail(result, "monument_blocked", "Airstrikes cannot be called into blocked monuments. Target is inside " + monumentName + ".");
            }

            if (!HasRequiredAirstrikeItem(player))
            {
                return Fail(result, "missing_airstrike_item", "You need " + config.AirstrikeItem.RequiredAmount + " " + GetAirstrikeItemDisplayName() + " item(s).");
            }

            var finalCost = GetFinalRPCost(player, strike);
            if (!HasSufficientCurrency(player, finalCost, out var currencyMessage))
            {
                return Fail(result, "insufficient_currency", currencyMessage);
            }

            var cooldownMessage = GetCooldownBlockMessage(player, strike);
            if (!string.IsNullOrWhiteSpace(cooldownMessage))
            {
                return Fail(result, "cooldown", cooldownMessage);
            }

            result.Success = true;
            result.ReasonCode = "ok";
            result.UserMessage = "Airstrike validation passed.";
            result.Strike = strike;
            result.Target = target;
            result.FinalRPCost = finalCost;
            return result;
        }

        private ValidationResult Fail(ValidationResult result, string reasonCode, string userMessage)
        {
            result.Success = false;
            result.ReasonCode = reasonCode;
            result.UserMessage = userMessage;
            return result;
        }

        private bool ValidateHomingReleaseProfileTarget(StrikeDefinition strike, AirstrikeTarget target, out string message)
        {
            message = "";
            if (GetEnabledStrikeProfileAssignments(strike).Count > 0)
            {
                return true;
            }

            if (!StrikeOrResolvedProfileContainsHomingPayload(strike))
            {
                return true;
            }

            if (target == null || target.Type != AirstrikeTargetType.VehiclePing || target.EntityId == 0UL)
            {
                message = (strike == null ? "This airstrike" : strike.DisplayName) + " includes homing release ordnance and requires a vehicle ping target.";
                return false;
            }

            var entity = ResolveClassifiableTargetEntity(FindEntity(target.EntityId));
            var combatEntity = entity as BaseCombatEntity;
            if (entity == null
                || entity.IsDestroyed
                || ClassifyTarget(entity) != AirstrikeTargetType.VehiclePing
                || (combatEntity != null && combatEntity.IsDead()))
            {
                message = "The homing release target is no longer a valid vehicle.";
                return false;
            }

            return true;
        }

        private bool StrikeOrResolvedProfileContainsHomingPayload(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return false;
            }

            HomingMissileSpec homingSpec;
            if (TryGetHomingMissileSpec(strike.Payload, out homingSpec))
            {
                return true;
            }

            string profileId;
            VisualProfileConfig profile;
            if (!TryResolveVisualProfileForStrike(strike, out profileId, out profile) || profile == null)
            {
                return false;
            }

            if (string.Equals(profile.PayloadReleaseMode, "generated", StringComparison.OrdinalIgnoreCase))
            {
                var payload = GetReleasePayload(profile.ReleaseTemplate, strike.Payload);
                if (TryGetHomingMissileSpec(payload, out homingSpec))
                {
                    return true;
                }

                return false;
            }

            if (profile.PayloadEvents != null && profile.PayloadEvents.Count > 0)
            {
                foreach (var payloadEvent in profile.PayloadEvents)
                {
                    if (payloadEvent == null)
                    {
                        continue;
                    }

                    var payload = GetReleasePayload(payloadEvent, strike.Payload);
                    if (TryGetHomingMissileSpec(payload, out homingSpec))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryResolveVisualProfileForStrike(StrikeDefinition strike, out string profileId, out VisualProfileConfig profile)
        {
            profileId = "";
            profile = null;
            if (strike == null)
            {
                return false;
            }

            if ((visualProfileFile == null || visualProfileFile.Profiles == null || visualProfileFile.Profiles.Count == 0) && File.Exists(ResolveVisualProfilesDataPath()))
            {
                LoadVisualProfiles();
            }

            var deliveryProfile = GetDeliveryVisualProfileForStrike(strike);
            var context = new AirstrikeCallContext
            {
                Strike = strike
            };
            var vehicle = NormalizeVisualProfileVehicle(null, strike, null, deliveryProfile);
            return TryGetVisualProfile(context, vehicle, deliveryProfile, out profileId, out profile);
        }

        private DeliveryVisualProfile GetDeliveryVisualProfileForStrike(StrikeDefinition strike)
        {
            if (strike == null)
            {
                return DeliveryVisualProfile.Mlrs;
            }

            A10StrafeSpec a10Spec;
            if (TryGetA10StrafeSpec(strike.Payload, out a10Spec) || string.Equals(strike.Delivery, "a10_gun_run", StringComparison.OrdinalIgnoreCase))
            {
                return DeliveryVisualProfile.A10;
            }

            MlrsPayloadSpec mlrsSpec;
            if (TryGetMlrsPayloadSpec(strike.Payload, out mlrsSpec))
            {
                return DeliveryVisualProfile.Mlrs;
            }

            HomingMissileSpec homingSpec;
            if (TryGetHomingMissileSpec(strike.Payload, out homingSpec))
            {
                return DeliveryVisualProfile.HomingMissile;
            }

            HeavyDropPayloadSpec heavySpec;
            if (TryGetHeavyDropPayloadSpec(strike.Payload, out heavySpec))
            {
                return DeliveryVisualProfile.HeavyDrop;
            }

            return DeliveryVisualProfile.RocketRun;
        }

        private void ShowStrikeOverview(BasePlayer player)
        {
            var target = GetLatestTarget(player, false);
            if (target == null)
            {
                DestroyStrikeUi(player);
                Reply(player, "No fresh target found. Aim with " + GetAirstrikeItemDisplayName() + " and place a ping, then run /" + GetOpenCommand() + ".");
                return;
            }

            var strikes = GetStrikesForTargetType(target.Type);
            if (strikes.Count == 0)
            {
                DestroyStrikeUi(player);
                Reply(player, "Stored target is " + FormatTargetType(target.Type) + ", but no enabled strikes match it.");
                return;
            }

            ShowStrikeSelectionUi(player, target, strikes);
        }

        private void ShowStrikeSelectionUi(BasePlayer player, AirstrikeTarget target, List<StrikeDefinition> strikes)
        {
            DestroyStrikeUi(player);

            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.05 0.06 0.07 0.92" },
                RectTransform = { AnchorMin = "0.23 0.16", AnchorMax = "0.77 0.86" }
            }, "Overlay", StrikeUiName);

            AddUiLabel(container, root, "Portable Airstrikes", 18, TextAnchor.MiddleLeft, "0.06 0.88", "0.70 0.98", "1 0.88 0.70 1");
            AddUiButton(container, root, "X", "portableairstrikes.ui.close", "0.92 0.90", "0.98 0.98", "0.50 0.12 0.10 0.95", 14);
            AddUiLabel(container, root, "Target: " + DescribeTarget(target), 12, TextAnchor.MiddleLeft, "0.06 0.81", "0.94 0.87", "0.85 0.90 0.92 1");

            var tokenCount = GetAirstrikeTokenCount(player);
            AddUiLabel(container, root, "Items: " + tokenCount + " / " + config.AirstrikeItem.RequiredAmount + "   Balance: " + BuildBalanceSummary(player) + "   Default: " + GetDefaultStrikeSummary(player), 11, TextAnchor.MiddleLeft, "0.06 0.75", "0.94 0.80", "0.72 0.78 0.82 1");

            var visible = BuildVisibleStrikeList(player, strikes);
            if (visible.Count == 0)
            {
                AddUiLabel(container, root, "No unlocked strikes are available for this target.", 13, TextAnchor.MiddleCenter, "0.08 0.42", "0.92 0.58", "1 1 1 1");
            }
            else
            {
                var contentHeight = Math.Max(
                    StrikePickerMinimumScrollContentHeight,
                    StrikePickerContentPaddingPixels * 2f + visible.Count * StrikePickerRowHeightPixels + Math.Max(0, visible.Count - 1) * StrikePickerRowGapPixels);
                var scroll = AddUiScrollView(container, root, "0.06 0.12", "0.94 0.70", contentHeight);
                for (var i = 0; i < visible.Count; i++)
                {
                    var strike = visible[i];
                    var topOffset = StrikePickerContentPaddingPixels + i * (StrikePickerRowHeightPixels + StrikePickerRowGapPixels);
                    var bottomOffset = topOffset + StrikePickerRowHeightPixels;
                    AddStrikeRowOffset(container, scroll, player, strike, topOffset, bottomOffset);
                }
            }

            AddUiLabel(container, root, "Direct: /" + GetOpenCommand() + " <id>   Save default manually: /" + GetOpenCommand() + " default <id>", 10, TextAnchor.MiddleLeft, "0.06 0.03", "0.86 0.08", "0.62 0.66 0.70 1");
            CuiHelper.AddUi(player, container);
        }

        private void ShowStrikeConfirmUi(BasePlayer player, string strikeId)
        {
            StrikeDefinition strike;
            if (!TryGetStrike(strikeId, out strike))
            {
                DestroyStrikeUi(player);
                Reply(player, "Unknown airstrike ID '" + strikeId + "'. Use /" + GetOpenCommand() + " list.");
                return;
            }

            var validation = ValidateStrikeCall(player, strikeId);
            DestroyStrikeUi(player);

            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.05 0.06 0.07 0.94" },
                RectTransform = { AnchorMin = "0.32 0.28", AnchorMax = "0.68 0.72" }
            }, "Overlay", StrikeUiName);

            AddUiLabel(container, root, "Confirm Airstrike", 17, TextAnchor.MiddleLeft, "0.08 0.84", "0.74 0.96", "1 0.88 0.70 1");
            AddUiButton(container, root, "X", "portableairstrikes.ui.close", "0.88 0.86", "0.96 0.96", "0.50 0.12 0.10 0.95", 14);
            AddUiLabel(container, root, strike.DisplayName + " (" + strike.Id + ")", 14, TextAnchor.MiddleLeft, "0.08 0.71", "0.92 0.80", "1 1 1 1");
            AddUiLabel(container, root, validation.Success ? BuildConfirmSummary(player, validation) : validation.UserMessage, 12, TextAnchor.UpperLeft, "0.08 0.34", "0.92 0.67", validation.Success ? "0.78 0.86 0.90 1" : "1 0.55 0.45 1");

            if (validation.Success)
            {
                AddUiButton(container, root, "CONFIRM", "portableairstrikes.ui.confirm " + strike.Id, "0.08 0.12", "0.47 0.25", "0.54 0.12 0.08 0.95", 13);
            }
            else
            {
                AddUiButton(container, root, "BACK", "portableairstrikes.ui.close", "0.08 0.12", "0.47 0.25", "0.20 0.24 0.28 0.95", 13);
            }

            AddUiButton(container, root, "CANCEL", "portableairstrikes.ui.close", "0.53 0.12", "0.92 0.25", "0.18 0.20 0.23 0.95", 13);
            CuiHelper.AddUi(player, container);
        }

        private List<StrikeDefinition> BuildVisibleStrikeList(BasePlayer player, List<StrikeDefinition> strikes)
        {
            var visible = new List<StrikeDefinition>();
            foreach (var strike in strikes)
            {
                if (strike == null || !strike.Enabled)
                {
                    continue;
                }

                var hasPermission = string.IsNullOrWhiteSpace(strike.PermissionRequired)
                    || permission.UserHasPermission(player.UserIDString, strike.PermissionRequired)
                    || IsAdmin(player);
                if (hasPermission || config.Selection.ShowLockedStrikes)
                {
                    visible.Add(strike);
                }
            }

            visible.Sort((a, b) =>
            {
                var tier = a.Tier.CompareTo(b.Tier);
                return tier != 0 ? tier : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });
            return visible;
        }

        private void AddStrikeRowOffset(CuiElementContainer container, string parent, BasePlayer player, StrikeDefinition strike, float topOffset, float bottomOffset)
        {
            var hasPermission = string.IsNullOrWhiteSpace(strike.PermissionRequired)
                || permission.UserHasPermission(player.UserIDString, strike.PermissionRequired)
                || IsAdmin(player);
            var validation = hasPermission ? ValidateStrikeCall(player, strike.Id) : null;
            var usable = hasPermission && validation != null && validation.Success;
            var color = usable ? "0.13 0.17 0.19 0.92" : "0.12 0.12 0.12 0.72";
            var row = container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform =
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = "0 -" + FormatUiPixels(bottomOffset),
                    OffsetMax = "0 -" + FormatUiPixels(topOffset)
                }
            }, parent);

            AddUiLabel(container, row, strike.DisplayName, 12, TextAnchor.MiddleLeft, "0.03 0.48", "0.50 0.92", usable ? "1 1 1 1" : "0.70 0.70 0.70 1");
            AddUiLabel(container, row, strike.Id + "   T" + strike.Tier + "   " + GetFinalRPCost(player, strike) + " RP", 10, TextAnchor.MiddleLeft, "0.03 0.08", "0.54 0.46", "0.68 0.75 0.78 1");

            var status = !hasPermission ? "LOCKED" : usable ? "SELECT" : validation.UserMessage;
            if (usable)
            {
                AddUiButton(container, row, status, "portableairstrikes.ui.select " + strike.Id, "0.72 0.18", "0.96 0.82", "0.54 0.12 0.08 0.95", 11);
            }
            else
            {
                AddUiLabel(container, row, status, 10, TextAnchor.MiddleRight, "0.46 0.16", "0.96 0.84", "1 0.55 0.45 1");
            }
        }

        private string BuildConfirmSummary(BasePlayer player, ValidationResult validation)
        {
            var strike = validation.Strike;
            var warningDelay = GetWarningDelaySeconds(strike);
            return "Target: " + DescribeTarget(validation.Target)
                + "\nCost: " + validation.FinalRPCost + " RP"
                + "\nItem: " + config.AirstrikeItem.RequiredAmount + " " + GetAirstrikeItemDisplayName()
                + "\nWarning delay: " + FormatSeconds(warningDelay)
                + "\nCooldown: " + FormatSeconds(strike.CooldownPerPlayerSeconds) + " player";
        }

        private string BuildBalanceSummary(BasePlayer player)
        {
            if (!config.Currency.Enabled)
            {
                return "free";
            }

            int balance;
            string error;
            return currencyAdapter != null && currencyAdapter.GetBalance(player, out balance, out error)
                ? balance + " RP"
                : "unavailable";
        }

        private string AddUiScrollView(CuiElementContainer container, string parent, string anchorMin, string anchorMax, float contentHeight)
        {
            var scrollName = StrikeUiName + ".Scroll." + CuiHelper.GetGuid();
            var contentRect = new CuiRectTransformComponent
            {
                AnchorMin = "0 1",
                AnchorMax = "1 1",
                OffsetMin = "0 -" + FormatUiPixels(contentHeight),
                OffsetMax = "0 0"
            };

            container.Add(new CuiElement
            {
                Name = scrollName,
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    new CuiScrollViewComponent
                    {
                        Horizontal = false,
                        Vertical = true,
                        MovementType = ScrollRect.MovementType.Elastic,
                        Elasticity = 0.1f,
                        Inertia = false,
                        DecelerationRate = 0.135f,
                        ScrollSensitivity = 100f,
                        ContentTransform = contentRect,
                        VerticalScrollbar = CreateUiScrollbar()
                    }
                }
            });

            return scrollName;
        }

        private CuiScrollbar CreateUiScrollbar()
        {
            return new CuiScrollbar
            {
                Invert = false,
                AutoHide = true,
                Size = 6f,
                HandleColor = "0.72 0.76 0.80 0.45",
                HighlightColor = "0.88 0.92 0.96 0.65",
                PressedColor = "1 1 1 0.80",
                TrackColor = "0.05 0.06 0.07 0.35"
            };
        }

        private void AddUiLabel(CuiElementContainer container, string parent, string text, int size, TextAnchor align, string anchorMin, string anchorMax, string color)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Align = align, Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddUiButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string color, int size)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command, Color = color },
                Text = { Text = text, FontSize = size, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private string FormatUiPixels(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void DestroyStrikeUi(BasePlayer player)
        {
            if (player != null)
            {
                CuiHelper.DestroyUi(player, StrikeUiName);
            }
        }

        private void CreateToolTargetMarker(BasePlayer player, AirstrikeTarget target)
        {
            if (player == null
                || target == null
                || config?.AirstrikeItem == null
                || !config.AirstrikeItem.ToolTargetMarkerEnabled
                || string.IsNullOrWhiteSpace(GenericRadiusMapMarkerPrefab))
            {
                return;
            }

            DestroyToolTargetMarker(player.userID);

            BaseEntity entity = null;
            try
            {
                var position = ResolveImpactPosition(target.Position) + Vector3.up * 0.65f;
                entity = GameManager.server.CreateEntity(GenericRadiusMapMarkerPrefab, position, Quaternion.identity, true);
                var marker = entity as MapMarkerGenericRadius;
                if (marker == null)
                {
                    entity?.Kill(BaseNetworkable.DestroyMode.None);
                    return;
                }

                var cyan = new Color(0.08f, 0.82f, 1.0f, 1f);
                var amber = new Color(1.0f, 0.58f, 0.12f, 1f);
                marker.enableSaving = false;
                marker.globalBroadcast = true;
                marker.radius = ToolTargetMarkerNativeRadius();
                marker.alpha = Mathf.Clamp01(config.AirstrikeItem.ToolTargetMarkerAlpha);
                marker.color1 = cyan;
                marker.color2 = amber;
                marker.Spawn();
                marker.SendUpdate();
                marker.SendNetworkUpdateImmediate();

                toolTargetMarkers[player.userID] = marker;

                var delay = Mathf.Clamp(config.AirstrikeItem.ToolTargetMarkerDurationSeconds, 3f, 60f);
                var playerId = player.userID;
                Timer cleanupTimer = null;
                cleanupTimer = timer.Once(delay, () =>
                {
                    activeTimers.Remove(cleanupTimer);
                    DestroyToolTargetMarker(playerId, marker);
                });
                if (cleanupTimer != null)
                {
                    activeTimers.Add(cleanupTimer);
                }
            }
            catch (Exception ex)
            {
                entity?.Kill(BaseNetworkable.DestroyMode.None);
                PrintWarning("Could not create airstrike targeting marker: " + ex.Message);
            }
        }

        private void DestroyToolTargetMarker(ulong playerId, MapMarkerGenericRadius expectedMarker = null)
        {
            MapMarkerGenericRadius marker;
            if (!toolTargetMarkers.TryGetValue(playerId, out marker))
            {
                return;
            }

            if (expectedMarker != null && marker != expectedMarker)
            {
                return;
            }

            toolTargetMarkers.Remove(playerId);
            if (marker == null || marker.IsDestroyed)
            {
                return;
            }

            try
            {
                marker.Kill(BaseNetworkable.DestroyMode.None);
            }
            catch (Exception ex)
            {
                PrintWarning("Could not remove airstrike targeting marker: " + ex.Message);
            }
        }

        private void DestroyAllToolTargetMarkers()
        {
            var playerIds = new List<ulong>(toolTargetMarkers.Keys);
            foreach (var playerId in playerIds)
            {
                DestroyToolTargetMarker(playerId);
            }
        }

        private float ToolTargetMarkerNativeRadius()
        {
            var size = config?.AirstrikeItem == null ? 10f : config.AirstrikeItem.ToolTargetMarkerSize;
            return Mathf.Clamp(
                NativeStrikeMapMarkerBaseRadius + Mathf.Clamp(size, 2f, 50f) * NativeStrikeMapMarkerRadiusPerConfiguredMeter,
                MinimumNativeStrikeMapMarkerRadius,
                MaximumNativeStrikeMapMarkerRadius);
        }

        private void ShowStrikeList(BasePlayer player)
        {
            var entries = new List<string>();
            foreach (var entry in config.StrikeDefinitions)
            {
                var strike = entry.Value;
                if (strike == null || !strike.Enabled)
                {
                    continue;
                }

                entries.Add(entry.Key + " (" + FormatAcceptedTargetTypes(strike) + ", " + GetFinalRPCost(player, strike) + " RP)");
            }

            Reply(player, entries.Count == 0 ? "No enabled airstrike definitions are configured." : "Enabled airstrikes: " + string.Join(", ", entries.ToArray()) + ". Set your binocular default with /" + GetOpenCommand() + " default <id>.");
        }

        private void ShowBalance(BasePlayer player)
        {
            var tokenCount = GetAirstrikeTokenCount(player);
            var discount = GetBestDiscount(player);
            var discountText = discount <= 0f ? "no discount" : Math.Round(discount * 100f).ToString("0", CultureInfo.InvariantCulture) + "% discount";

            if (!config.Currency.Enabled)
            {
                Reply(player, GetAirstrikeItemDisplayName() + " count: " + tokenCount + ". RP currency is disabled; " + discountText + ". Default: " + GetDefaultStrikeSummary(player) + ".");
                return;
            }

            int balance;
            string error = "No currency adapter is active.";
            if (currencyAdapter == null || !currencyAdapter.GetBalance(player, out balance, out error))
            {
                Reply(player, GetAirstrikeItemDisplayName() + " count: " + tokenCount + ". Currency unavailable: " + error + " Default: " + GetDefaultStrikeSummary(player) + ".");
                return;
            }

            Reply(player, GetAirstrikeItemDisplayName() + " count: " + tokenCount + ". " + currencyAdapter.Name + " balance: " + balance + " RP; " + discountText + ". Default: " + GetDefaultStrikeSummary(player) + ".");
        }

        private string BuildStrikeIdSummary(List<StrikeDefinition> strikes, BasePlayer player)
        {
            var values = new List<string>();
            foreach (var strike in strikes)
            {
                values.Add(strike.Id + "=" + GetFinalRPCost(player, strike) + "RP");
            }

            return string.Join(", ", values.ToArray());
        }

        private bool TryGetStrike(string strikeId, out StrikeDefinition strike)
        {
            strike = null;
            if (string.IsNullOrWhiteSpace(strikeId) || config.StrikeDefinitions == null)
            {
                return false;
            }

            return config.StrikeDefinitions.TryGetValue(strikeId.Trim(), out strike) && strike != null;
        }

        private bool TrySetDefaultStrike(BasePlayer player, string strikeId, out StrikeDefinition strike, out string error)
        {
            strike = null;
            error = "";
            if (!TryGetStrike(strikeId, out strike))
            {
                error = "Unknown airstrike ID '" + strikeId + "'. Use /" + GetOpenCommand() + " list.";
                return false;
            }

            if (!strike.Enabled)
            {
                error = strike.DisplayName + " is disabled and cannot be saved as your default.";
                return false;
            }

            if (!CanPlayerUseStrike(player, strike))
            {
                error = "You do not have permission to save " + strike.DisplayName + " as your default.";
                return false;
            }

            SetPlayerDefaultStrike(player, strike.Id);
            return true;
        }

        private void SetPlayerDefaultStrike(BasePlayer player, string strikeId)
        {
            if (player == null || string.IsNullOrWhiteSpace(strikeId))
            {
                return;
            }

            if (storedData.DefaultStrikeByUser == null)
            {
                storedData.DefaultStrikeByUser = new Dictionary<string, string>();
            }

            storedData.DefaultStrikeByUser[player.UserIDString] = strikeId.Trim();
            SaveData();
        }

        private bool TryGetPlayerDefaultStrikeId(BasePlayer player, out string strikeId)
        {
            strikeId = "";
            if (player == null || storedData?.DefaultStrikeByUser == null)
            {
                return false;
            }

            return storedData.DefaultStrikeByUser.TryGetValue(player.UserIDString, out strikeId)
                && !string.IsNullOrWhiteSpace(strikeId);
        }

        private string GetDefaultStrikeSummary(BasePlayer player)
        {
            string strikeId;
            if (!TryGetPlayerDefaultStrikeId(player, out strikeId))
            {
                return "not set";
            }

            StrikeDefinition strike;
            return TryGetStrike(strikeId, out strike)
                ? strike.DisplayName + " (" + strike.Id + ")"
                : strikeId + " (missing)";
        }

        private bool CanPlayerUseStrike(BasePlayer player, StrikeDefinition strike)
        {
            return player != null
                && strike != null
                && (string.IsNullOrWhiteSpace(strike.PermissionRequired)
                    || permission.UserHasPermission(player.UserIDString, strike.PermissionRequired)
                    || IsAdmin(player));
        }

        private List<StrikeDefinition> GetStrikesForTargetType(AirstrikeTargetType targetType)
        {
            var strikes = new List<StrikeDefinition>();
            foreach (var entry in config.StrikeDefinitions)
            {
                var strike = entry.Value;
                if (strike == null || !strike.Enabled)
                {
                    continue;
                }

                if (!config.Selection.AutoFilterByPingType || StrikeAcceptsTargetType(strike, targetType))
                {
                    strikes.Add(strike);
                }
            }

            return strikes;
        }

        private int GetEnabledStrikeCount()
        {
            var count = 0;
            if (config.StrikeDefinitions == null)
            {
                return count;
            }

            foreach (var strike in config.StrikeDefinitions.Values)
            {
                if (strike != null && strike.Enabled)
                {
                    count++;
                }
            }

            return count;
        }

        private void StoreMapNoteTarget(BasePlayer player, Vector3 notePosition, string source)
        {
            if (player == null)
            {
                return;
            }

            var position = ResolveMapNotePosition(notePosition);
            StoreTarget(player, position, null, AirstrikeTargetType.GroundPing, source);

            if (config.General.DebugMode)
            {
                Reply(player, "Stored airstrike map/ping target at " + FormatPosition(position) + ".");
            }
        }

        private bool TryStoreRaycastTarget(BasePlayer player, string source, out AirstrikeTarget target, out string error)
        {
            target = null;
            error = "";

            if (player?.eyes == null)
            {
                error = "No player view available for airstrike targeting.";
                return false;
            }

            var range = Math.Max(10f, config?.General == null ? 250f : config.General.MaxCallRange);
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, range, TargetRaycastLayer, QueryTriggerInteraction.Ignore))
            {
                error = "No raycast target found within " + FormatMeters(range) + ".";
                return false;
            }

            var entity = ResolveClassifiableTargetEntity(hit.GetEntity());
            StoreTarget(player, hit.point, entity, ClassifyTarget(entity), source);
            target = GetLatestTarget(player, false);
            return target != null;
        }

        private Vector3 ResolveMapNotePosition(Vector3 notePosition)
        {
            var position = notePosition;
            try
            {
                if (TerrainMeta.HeightMap != null)
                {
                    var terrainY = TerrainMeta.HeightMap.GetHeight(position);
                    if (Math.Abs(position.y) < 0.01f || position.y < terrainY - 5f || position.y > terrainY + 250f)
                    {
                        position.y = terrainY;
                    }
                }
            }
            catch
            {
                // Keep the hook payload if terrain metadata is unavailable during early load.
            }

            return position;
        }

        private void StoreTarget(BasePlayer player, Vector3 position, BaseEntity entity, AirstrikeTargetType targetType, string source)
        {
            if (player == null)
            {
                return;
            }

            var target = new AirstrikeTarget
            {
                Type = targetType,
                Position = position,
                EntityId = entity?.net == null ? 0UL : entity.net.ID.Value,
                EntityShortPrefabName = entity?.ShortPrefabName ?? "",
                CreatedAt = GetNow(),
                Source = source ?? ""
            };

            latestTargets[player.userID] = target;
        }

        private Vector3 GetEntityTargetPosition(BaseEntity entity, Vector3 fallbackPosition)
        {
            if (entity == null)
            {
                return fallbackPosition;
            }

            var combatEntity = entity as BaseCombatEntity;
            if (combatEntity != null)
            {
                return GetHomingTargetPoint(combatEntity);
            }

            try
            {
                return entity.transform.position;
            }
            catch
            {
                return fallbackPosition;
            }
        }

        private BaseEntity ResolveClassifiableTargetEntity(BaseEntity entity)
        {
            if (entity == null || IsVehicleTargetEntity(entity))
            {
                return entity;
            }

            var player = entity as BasePlayer;
            if (player != null)
            {
                var mountedVehicle = player.GetMountedVehicle();
                if (IsVehicleTargetEntity(mountedVehicle))
                {
                    return mountedVehicle;
                }

                var mounted = player.GetMounted();
                var mountedParentVehicle = mounted == null ? null : mounted.VehicleParent();
                if (IsVehicleTargetEntity(mountedParentVehicle))
                {
                    return mountedParentVehicle;
                }

                return player;
            }

            var mountable = entity as BaseMountable;
            if (mountable != null)
            {
                var mountableParentVehicle = mountable.VehicleParent();
                if (IsVehicleTargetEntity(mountableParentVehicle))
                {
                    return mountableParentVehicle;
                }
            }

            var parent = entity.GetParentEntity();
            for (var depth = 0; parent != null && depth < 4; depth++)
            {
                if (parent is BasePlayer || IsVehicleTargetEntity(parent))
                {
                    return parent;
                }

                var nextParent = parent.GetParentEntity();
                if (nextParent == parent)
                {
                    break;
                }

                parent = nextParent;
            }

            return entity;
        }

        private bool IsVehicleTargetEntity(BaseEntity entity)
        {
            return entity is BaseVehicle
                || entity is BaseHelicopter
                || entity is BradleyAPC
                || HasVehiclePrefabName(entity);
        }

        private bool HasVehiclePrefabName(BaseEntity entity)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.ShortPrefabName))
            {
                return false;
            }

            var prefabName = entity.ShortPrefabName;
            return prefabName.IndexOf("minicopter", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("scraptransport", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("attackhelicopter", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("hotairballoon", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("submarine", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("rhib", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("rowboat", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("tugboat", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("snowmobile", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("motorbike", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("pedalbike", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("modularcar", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private AirstrikeTarget GetLatestTarget(BasePlayer player, bool rejectExpired)
        {
            if (player == null)
            {
                return null;
            }

            AirstrikeTarget target;
            if (!latestTargets.TryGetValue(player.userID, out target) || target == null)
            {
                return null;
            }

            if (rejectExpired && GetNow() - target.CreatedAt > config.General.MaxPingAgeSeconds)
            {
                latestTargets.Remove(player.userID);
                return null;
            }

            return target;
        }

        private AirstrikeTargetType ClassifyTarget(BaseEntity entity)
        {
            entity = ResolveClassifiableTargetEntity(entity);
            if (entity == null)
            {
                return AirstrikeTargetType.GroundPing;
            }

            var player = entity as BasePlayer;
            if (player != null)
            {
                return player is NPCPlayer ? AirstrikeTargetType.NpcPing : AirstrikeTargetType.PlayerPing;
            }

            if (entity is BaseVehicle || entity is BaseHelicopter || entity is BradleyAPC)
            {
                return AirstrikeTargetType.VehiclePing;
            }

            if (entity is BaseAnimalNPC || entity.ShortPrefabName.IndexOf("scientist", StringComparison.OrdinalIgnoreCase) >= 0 || entity.ShortPrefabName.IndexOf("npc", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AirstrikeTargetType.NpcPing;
            }

            return AirstrikeTargetType.GroundPing;
        }

        private bool HasLineOfSightToTarget(BasePlayer player, AirstrikeTarget target)
        {
            var origin = player.eyes.position;
            var destination = target.Position + Vector3.up * 0.35f;
            var direction = destination - origin;
            var distance = direction.magnitude;
            if (distance <= 0.1f)
            {
                return true;
            }

            RaycastHit hit;
            if (!Physics.Raycast(origin, direction.normalized, out hit, distance, TargetRaycastLayer, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            if (Vector3.Distance(hit.point, destination) <= 2f)
            {
                return true;
            }

            var hitEntity = ResolveClassifiableTargetEntity(hit.GetEntity());
            return target.EntityId != 0UL && hitEntity?.net != null && hitEntity.net.ID.Value == target.EntityId;
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
                if (collider != null)
                {
                    var closest = collider.ClosestPoint(position);
                    if (Vector3.Distance(closest, position) <= Math.Max(0f, extraDistance))
                    {
                        return true;
                    }

                    continue;
                }

                if (Vector3.Distance(zone.transform.position, position) <= Math.Max(25f, extraDistance))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldCheckMonumentBlock(StrikeDefinition strike)
        {
            if (config?.General == null || !config.General.BlockMonuments)
            {
                return false;
            }

            return !config.General.BlockMonumentsForHeavyStrikesOnly || IsHeavyStrike(strike);
        }

        private bool IsBlockedMonumentPosition(Vector3 position, out string monumentName)
        {
            monumentName = "";
            EnsureMonumentBlockZones();

            foreach (var zone in monumentBlockZones)
            {
                var dx = position.x - zone.Center.x;
                var dz = position.z - zone.Center.z;
                if (dx * dx + dz * dz <= zone.Radius * zone.Radius)
                {
                    monumentName = string.IsNullOrWhiteSpace(zone.Name) ? "a blocked monument" : zone.Name;
                    return true;
                }
            }

            return false;
        }

        private void EnsureMonumentBlockZones()
        {
            if (monumentBlockZonesLoaded)
            {
                return;
            }

            monumentBlockZonesLoaded = true;
            monumentBlockZones.Clear();

            if (config?.General?.BlockedMonumentNames == null || config.General.BlockedMonumentNames.Count == 0)
            {
                return;
            }

            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null)
            {
                return;
            }

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || !IsConfiguredBlockedMonument(monument))
                {
                    continue;
                }

                var radius = GetMonumentBlockRadius(monument) + Math.Max(0f, config.General.MonumentBlockRadiusPadding);
                if (radius <= 0f)
                {
                    continue;
                }

                monumentBlockZones.Add(new MonumentBlockZone
                {
                    Center = monument.transform.position,
                    Radius = radius,
                    Name = GetMonumentBlockDisplayName(monument)
                });
            }
        }

        private void ResetMonumentBlockZones()
        {
            monumentBlockZonesLoaded = false;
            monumentBlockZones.Clear();
        }

        private bool IsConfiguredBlockedMonument(MonumentInfo monument)
        {
            var candidates = GetMonumentNameCandidates(monument);
            foreach (var configuredName in config.General.BlockedMonumentNames)
            {
                var rule = NormalizeMonumentName(configuredName);
                if (string.IsNullOrWhiteSpace(rule))
                {
                    continue;
                }

                if (rule == "*" || rule == "all")
                {
                    return true;
                }

                foreach (var candidate in candidates)
                {
                    if (MonumentNameMatches(rule, candidate))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool MonumentNameMatches(string rule, string candidate)
        {
            if (string.IsNullOrWhiteSpace(rule) || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            if (string.Equals(rule, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return candidate.StartsWith(rule + "_", StringComparison.OrdinalIgnoreCase)
                || rule.StartsWith(candidate + "_", StringComparison.OrdinalIgnoreCase);
        }

        private List<string> GetMonumentNameCandidates(MonumentInfo monument)
        {
            var candidates = new List<string>();
            AddMonumentNameCandidate(candidates, monument?.name);
            AddMonumentNameCandidate(candidates, GetMonumentShortName(monument));
            AddMonumentNameCandidate(candidates, GetMonumentDisplayPhrase(monument));
            return candidates;
        }

        private void AddMonumentNameCandidate(List<string> candidates, string value)
        {
            var normalized = NormalizeMonumentName(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            candidates.Add(normalized);
        }

        private string GetMonumentBlockDisplayName(MonumentInfo monument)
        {
            var displayName = GetMonumentDisplayPhrase(monument);
            return string.IsNullOrWhiteSpace(displayName) ? GetMonumentShortName(monument) : displayName;
        }

        private string GetMonumentDisplayPhrase(MonumentInfo monument)
        {
            if (monument == null)
            {
                return "";
            }

            try
            {
                return monument.displayPhrase.english ?? "";
            }
            catch
            {
                return "";
            }
        }

        private string GetMonumentShortName(MonumentInfo monument)
        {
            var name = monument?.name ?? "";
            var separator = name.LastIndexOf('/');
            return (separator >= 0 ? name.Substring(separator + 1) : name).Replace(".prefab", "");
        }

        private string NormalizeMonumentName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            var normalized = value.Trim().ToLowerInvariant().Replace(".prefab", "");
            var separator = normalized.LastIndexOf('/');
            if (separator >= 0)
            {
                normalized = normalized.Substring(separator + 1);
            }

            var result = "";
            for (var i = 0; i < normalized.Length; i++)
            {
                var character = normalized[i];
                if (char.IsLetterOrDigit(character))
                {
                    result += character;
                    continue;
                }

                if (result.Length > 0 && result[result.Length - 1] != '_')
                {
                    result += "_";
                }
            }

            return result.Trim('_');
        }

        private float GetMonumentBlockRadius(MonumentInfo monument)
        {
            switch (NormalizeMonumentName(GetMonumentShortName(monument)))
            {
                case "airfield_1": return 255f;
                case "bandit_town": return 105f;
                case "cave_large_hard":
                case "cave_large_medium":
                case "cave_large_sewers_hard":
                case "cave_medium_easy":
                case "cave_medium_hard":
                case "cave_medium_medium":
                case "cave_small_easy":
                case "cave_small_hard":
                case "cave_small_medium": return 75f;
                case "compound": return 255f;
                case "entrance": return 20f;
                case "excavator_1": return 150f;
                case "fishing_village_a":
                case "fishing_village_b":
                case "fishing_village_c": return 55f;
                case "gas_station_1": return 60f;
                case "harbor_1":
                case "harbor_2": return 135f;
                case "junkyard_1": return 105f;
                case "launch_site_1": return 245f;
                case "lighthouse": return 50f;
                case "military_tunnel_1": return 105f;
                case "mining_quarry_a":
                case "mining_quarry_b":
                case "mining_quarry_c": return 30f;
                case "oilrigai": return 100f;
                case "oilrigai2": return 200f;
                case "power_sub_big_1":
                case "power_sub_big_2": return 30f;
                case "power_sub_small_1":
                case "power_sub_small_2": return 25f;
                case "powerplant_1": return 145f;
                case "radtown_small_3": return 95f;
                case "satellite_dish": return 85f;
                case "sphere_tank": return 75f;
                case "stables_a":
                case "stables_b": return 80f;
                case "supermarket_1": return 60f;
                case "swamp_a":
                case "swamp_b": return 30f;
                case "swamp_c": return 55f;
                case "trainyard_1": return 145f;
                case "warehouse": return 50f;
                case "water_treatment_plant_1": return 175f;
                case "water_well_a":
                case "water_well_b":
                case "water_well_c":
                case "water_well_d":
                case "water_well_e": return 30f;
            }

            return Math.Max(1f, config.General.DefaultMonumentBlockRadius);
        }

        private bool WantsCIDAirstrikeItem()
        {
            return config?.AirstrikeItem != null
                && config.AirstrikeItem.Enabled
                && config.AirstrikeItem.UseCustomItemDefinition;
        }

        private bool IsCIDAirstrikeItemActive()
        {
            return WantsCIDAirstrikeItem() && airstrikeCustomItemDefinition != null;
        }

        private string GetCustomAirstrikeShortname()
        {
            return string.IsNullOrWhiteSpace(config?.AirstrikeItem?.CustomShortname)
                ? "raidlands.airstrike.designator"
                : config.AirstrikeItem.CustomShortname.Trim();
        }

        private string GetLegacyAirstrikeShortname()
        {
            return string.IsNullOrWhiteSpace(config?.AirstrikeItem?.Shortname)
                ? "tool.binoculars"
                : config.AirstrikeItem.Shortname.Trim();
        }

        private string GetAirstrikeParentShortname()
        {
            return string.IsNullOrWhiteSpace(config?.AirstrikeItem?.ParentShortname)
                ? "tool.binoculars"
                : config.AirstrikeItem.ParentShortname.Trim();
        }

        private string GetAirstrikeCreateShortname()
        {
            if (IsCIDAirstrikeItemActive())
            {
                return GetCustomAirstrikeShortname();
            }

            if (WantsCIDAirstrikeItem() && !config.AirstrikeItem.AllowVanillaFallbackIfCIDMissing)
            {
                return GetCustomAirstrikeShortname();
            }

            return GetLegacyAirstrikeShortname();
        }

        private ulong GetAirstrikeCreateSkinId()
        {
            return string.Equals(GetAirstrikeCreateShortname(), GetCustomAirstrikeShortname(), StringComparison.OrdinalIgnoreCase)
                ? 0UL
                : config.AirstrikeItem.SkinId;
        }

        private int GetAirstrikeMaxStackSize()
        {
            return ClampAirstrikeMaxStackSize(config?.AirstrikeItem == null ? DefaultAirstrikeItemMaxStackSize : config.AirstrikeItem.MaxStackSize);
        }

        private int ClampAirstrikeMaxStackSize(int value)
        {
            return Math.Max(1, Math.Min(value <= 0 ? DefaultAirstrikeItemMaxStackSize : value, MaximumAirstrikeItemMaxStackSize));
        }

        private int ClampAirstrikeItemStackAmount(int value)
        {
            return Math.Max(1, Math.Min(value <= 0 ? 1 : value, GetAirstrikeMaxStackSize()));
        }

        private int GetAirstrikeMaxChargesPerItem()
        {
            return ClampAirstrikeItemCharges(config?.AirstrikeItem == null ? DefaultAirstrikeItemMaxChargesPerItem : config.AirstrikeItem.MaxChargesPerItem);
        }

        private int ClampAirstrikeItemCharges(int value)
        {
            return Math.Max(1, Math.Min(value <= 0 ? DefaultAirstrikeItemMaxChargesPerItem : value, MaximumAirstrikeItemMaxChargesPerItem));
        }

        private bool TryRegisterAirstrikeCustomItemDefinition()
        {
            if (!WantsCIDAirstrikeItem())
            {
                return false;
            }

            if (airstrikeCustomItemDefinition != null)
            {
                return true;
            }

            if (CustomItemDefinitions == null || !CustomItemDefinitions.IsLoaded)
            {
                WarnCIDUnavailableOnce();
                return false;
            }

            var customShortname = GetCustomAirstrikeShortname();
            var existing = ItemManager.FindItemDefinition(customShortname);
            if (existing != null)
            {
                if (CustomItemDefinitions.Call("IsCustomDefinition", existing) is bool isCustomDefinition && isCustomDefinition)
                {
                    if (TryUnregisterExistingAirstrikeCustomItemDefinition(existing))
                    {
                        airstrikeCustomItemDefinition = null;
                    }
                    else
                    {
                        airstrikeCustomItemDefinition = existing;
                        existing.stackable = GetAirstrikeMaxStackSize();
                        PrintWarning("Using existing CID airstrike item '" + existing.shortname + "' itemId=" + existing.itemid + " after refresh failed. If the icon is still vanilla, reload CustomItemDefinitions before PortableAirstrikes.");
                        return true;
                    }
                }
                else
                {
                    PrintWarning("CID registration skipped: item shortname '" + customShortname + "' already exists but is not a CustomItemDefinitions item.");
                    return false;
                }
            }

            var parentShortname = GetAirstrikeParentShortname();
            var parent = ItemManager.FindItemDefinition(parentShortname);
            if (parent == null)
            {
                PrintWarning("CID registration failed: parent item definition '" + parentShortname + "' was not found.");
                return false;
            }

            var iconFileId = ResolveAirstrikeIconFileId();
            var description = string.IsNullOrWhiteSpace(config.AirstrikeItem.DefaultDescription)
                ? "Aim with the binoculars and place a ping to call your selected airstrike."
                : config.AirstrikeItem.DefaultDescription.Trim();

            try
            {
                var dto = new
                {
                    parentItemId = parent.itemid,
                    shortname = customShortname,
                    itemId = config.AirstrikeItem.CustomItemId,
                    iconFileId = iconFileId,
                    defaultName = GetAirstrikeItemDisplayName(),
                    defaultDescription = description,
                    defaultSkinId = config.AirstrikeItem.SkinId,
                    maxStackSize = GetAirstrikeMaxStackSize(),
                    category = ItemCategory.Tool,
                    itemMods = config.AirstrikeItem.ImportParentItemMods ? parent.itemMods : null,
                    repairable = false,
                    craftable = false,
                    defaultBlueprintUnlocked = false
                };

                var registered = CustomItemDefinitions.Call("Register", dto, this) as ItemDefinition;
                if (registered == null)
                {
                    PrintWarning("CID registration failed: CustomItemDefinitions.Register returned no ItemDefinition.");
                    return false;
                }

                airstrikeCustomItemDefinition = registered;
                registered.stackable = GetAirstrikeMaxStackSize();
                warnedCIDUnavailable = false;
                Puts("Registered CID airstrike item '" + registered.shortname + "' itemId=" + registered.itemid + " parent=" + parent.shortname + " iconFileId=" + iconFileId + " maxStackSize=" + registered.stackable + ".");
                return true;
            }
            catch (Exception ex)
            {
                PrintWarning("CID registration failed for airstrike designator: " + ex.Message);
                return false;
            }
        }

        private bool TryUnregisterExistingAirstrikeCustomItemDefinition(ItemDefinition existing)
        {
            if (existing == null || CustomItemDefinitions == null || !CustomItemDefinitions.IsLoaded)
            {
                return false;
            }

            try
            {
                var result = CustomItemDefinitions.Call("Unregister", existing, this);
                if (result is bool && (bool)result)
                {
                    Puts("Refreshed existing CID airstrike item '" + existing.shortname + "' so icon and stack metadata can be rebuilt.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Could not refresh existing CID airstrike item '" + existing.shortname + "': " + ex.Message);
            }

            return false;
        }

        private void ClearAirstrikeCustomItemDefinition(bool unregister)
        {
            if (unregister && airstrikeCustomItemDefinition != null && CustomItemDefinitions != null && CustomItemDefinitions.IsLoaded)
            {
                try
                {
                    CustomItemDefinitions.Call("Unregister", airstrikeCustomItemDefinition, this);
                }
                catch (Exception ex)
                {
                    PrintWarning("Could not unregister existing CID airstrike item during reload: " + ex.Message);
                }
            }

            airstrikeCustomItemDefinition = null;
            airstrikeIconFileId = 0;
            airstrikeIconSource = "";
            warnedIconMissing = false;
        }

        private uint ResolveAirstrikeIconFileId()
        {
            if (config?.AirstrikeItem == null)
            {
                return 0;
            }

            if (config.AirstrikeItem.IconFileId != 0)
            {
                airstrikeIconFileId = config.AirstrikeItem.IconFileId;
                airstrikeIconSource = "configured IconFileId";
                return airstrikeIconFileId;
            }

            if (airstrikeIconFileId != 0)
            {
                return airstrikeIconFileId;
            }

            var iconPath = ResolveAirstrikeIconPath();
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                airstrikeIconSource = "no IconPngDataPath configured";
                return 0;
            }

            if (!File.Exists(iconPath))
            {
                WarnIconMissingOnce(iconPath);
                airstrikeIconSource = "missing " + iconPath;
                return 0;
            }

            if (FileStorage.server == null)
            {
                airstrikeIconSource = "FileStorage unavailable";
                return 0;
            }

            try
            {
                var bytes = File.ReadAllBytes(iconPath);
                if (bytes == null || bytes.Length == 0)
                {
                    PrintWarning("CID icon file is empty: " + iconPath);
                    airstrikeIconSource = "empty " + iconPath;
                    return 0;
                }

                airstrikeIconFileId = FileStorage.server.Store(bytes, FileStorage.Type.png, default);
                airstrikeIconSource = iconPath;
                return airstrikeIconFileId;
            }
            catch (Exception ex)
            {
                PrintWarning("Could not load CID airstrike icon from '" + iconPath + "': " + ex.Message);
                airstrikeIconSource = "failed " + iconPath;
                return 0;
            }
        }

        private string ResolveAirstrikeIconPath()
        {
            var path = config?.AirstrikeItem?.IconPngDataPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            path = path.Trim();
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            path = path.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(Interface.Oxide.DataDirectory, path);
        }

        private void WarnCIDUnavailableOnce()
        {
            if (warnedCIDUnavailable)
            {
                return;
            }

            warnedCIDUnavailable = true;
            if (config?.AirstrikeItem != null && config.AirstrikeItem.AllowVanillaFallbackIfCIDMissing)
            {
                PrintWarning("CustomItemDefinitions is not loaded. PortableAirstrikes will use the legacy named binocular fallback item.");
            }
            else
            {
                PrintWarning("CustomItemDefinitions is required for the configured airstrike item, but it is not loaded.");
            }
        }

        private void WarnIconMissingOnce(string iconPath)
        {
            if (warnedIconMissing)
            {
                return;
            }

            warnedIconMissing = true;
            PrintWarning("CID airstrike icon file was not found at '" + iconPath + "'. The custom item will register without the Raidlands PNG icon unless IconFileId or SkinId is configured.");
        }

        private bool HasRequiredAirstrikeItem(BasePlayer player)
        {
            if (!config.AirstrikeItem.Enabled)
            {
                return true;
            }

            if (IsAdmin(player) && config.AirstrikeItem.AllowAdminsWithoutItem)
            {
                return true;
            }

            return GetAirstrikeTokenCount(player) >= config.AirstrikeItem.RequiredAmount;
        }

        private int GetAirstrikeTokenCount(BasePlayer player)
        {
            var count = 0;
            if (player?.inventory == null)
            {
                return count;
            }

            foreach (var container in GetInventoryContainers(player))
            {
                if (container?.itemList == null)
                {
                    continue;
                }

                foreach (var item in container.itemList)
                {
                    if (IsAirstrikeToken(item))
                    {
                        count = Math.Min(int.MaxValue, count + GetAirstrikeTokenCharges(item, true));
                    }
                }
            }

            return count;
        }

        private bool ConsumeAirstrikeTokens(BasePlayer player, int amount)
        {
            if (amount <= 0 || !config.AirstrikeItem.Enabled)
            {
                return true;
            }

            if (GetAirstrikeTokenCount(player) < amount)
            {
                return false;
            }

            var remaining = amount;
            foreach (var container in GetInventoryContainers(player))
            {
                if (container?.itemList == null)
                {
                    continue;
                }

                var items = new List<Item>(container.itemList);
                foreach (var item in items)
                {
                    if (!IsAirstrikeToken(item))
                    {
                        continue;
                    }

                    var charges = GetAirstrikeTokenCharges(item, true);
                    if (charges <= remaining)
                    {
                        remaining -= charges;
                        item.RemoveFromContainer();
                        item.Remove();
                    }
                    else
                    {
                        SetAirstrikeTokenCharges(item, charges - remaining);
                        remaining = 0;
                        return true;
                    }

                    if (remaining <= 0)
                    {
                        return true;
                    }
                }
            }

            return remaining <= 0;
        }

        private IEnumerable<ItemContainer> GetInventoryContainers(BasePlayer player)
        {
            yield return player.inventory.containerMain;
            yield return player.inventory.containerBelt;
            yield return player.inventory.containerWear;
        }

        private void NormalizeOnlineAirstrikeInventories()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                NormalizeAirstrikeInventory(player);
            }
        }

        private void NormalizeAirstrikeInventory(BasePlayer player)
        {
            if (player?.inventory == null)
            {
                return;
            }

            var activeItem = player.GetActiveItem();
            var activeItemRepaired = false;
            foreach (var container in GetInventoryContainers(player))
            {
                if (container?.itemList == null)
                {
                    continue;
                }

                var items = new List<Item>(container.itemList);
                foreach (var item in items)
                {
                    if (IsAirstrikeToken(item))
                    {
                        var repaired = NormalizeAirstrikeToken(item);
                        if (repaired && ReferenceEquals(item, activeItem))
                        {
                            activeItemRepaired = true;
                        }
                    }
                }
            }

            if (activeItemRepaired)
            {
                RefreshActiveAirstrikeItem(player);
            }
        }

        private bool IsAirstrikeToken(Item item)
        {
            if (item?.info == null || config.AirstrikeItem == null)
            {
                return false;
            }

            if (WantsCIDAirstrikeItem())
            {
                TryRegisterAirstrikeCustomItemDefinition();
                if (ReferenceEquals(item.info, airstrikeCustomItemDefinition))
                {
                    return true;
                }

                if (string.Equals(item.info.shortname, GetCustomAirstrikeShortname(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return IsLegacyAirstrikeToken(item);
        }

        private bool IsLegacyAirstrikeToken(Item item)
        {
            if (item?.info == null || config?.AirstrikeItem == null)
            {
                return false;
            }

            if (!string.Equals(item.info.shortname, GetLegacyAirstrikeShortname(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!config.AirstrikeItem.RequireCustomNameOrSkin)
            {
                return true;
            }

            var displayMatches = !string.IsNullOrWhiteSpace(config.AirstrikeItem.DisplayName)
                && ((item.name ?? "").Equals(config.AirstrikeItem.DisplayName, StringComparison.OrdinalIgnoreCase)
                    || (item.name ?? "").StartsWith(config.AirstrikeItem.DisplayName + " x", StringComparison.OrdinalIgnoreCase));
            var skinMatches = config.AirstrikeItem.SkinId != 0UL && item.skin == config.AirstrikeItem.SkinId;
            return displayMatches || skinMatches;
        }

        private bool IsAirstrikeTargetingToolItem(Item item)
        {
            return config?.AirstrikeItem != null
                && config.AirstrikeItem.TreatAsTargetingTool
                && IsAirstrikeToken(item);
        }

        private int GetAirstrikeTokenCharges(Item item, bool normalize)
        {
            if (item == null)
            {
                return 0;
            }

            if (normalize)
            {
                NormalizeAirstrikeToken(item);
            }

            return ReadAirstrikeTokenCharges(item);
        }

        private void SetAirstrikeTokenCharges(Item item, int charges)
        {
            if (item == null)
            {
                return;
            }

            charges = ClampAirstrikeItemCharges(charges);
            item.amount = 1;
            var instanceData = EnsureAirstrikeTokenInstanceData(item);
            instanceData.dataInt = charges;
            UpdateAirstrikeTokenDisplayName(item, charges);
            item.MarkDirty();
        }

        private bool NormalizeAirstrikeToken(Item item)
        {
            if (item == null)
            {
                return false;
            }

            var originalAmount = item.amount;
            var originalCharges = ReadAirstrikeTokenCharges(item);
            var originalName = item.name ?? "";
            var charges = originalCharges;
            if (originalAmount > 1)
            {
                charges = Math.Max(charges, originalAmount);
            }

            charges = ClampAirstrikeItemCharges(charges);
            var nameWasCurrent = IsAirstrikeTokenDisplayNameCurrent(item, charges);
            SetAirstrikeTokenCharges(item, charges);
            return originalAmount != 1 || originalCharges != charges || !nameWasCurrent || !string.Equals(originalName, item.name ?? "", StringComparison.Ordinal);
        }

        private int ReadAirstrikeTokenCharges(Item item)
        {
            if (item == null)
            {
                return 0;
            }

            var storedCharges = item.instanceData == null ? 0 : item.instanceData.dataInt;
            var physicalAmount = item.amount <= 0 ? 1 : item.amount;
            return ClampAirstrikeItemCharges(Math.Max(physicalAmount > 1 ? physicalAmount : 1, storedCharges <= 0 ? 1 : storedCharges));
        }

        private ProtoBuf.Item.InstanceData EnsureAirstrikeTokenInstanceData(Item item)
        {
            if (item.instanceData == null)
            {
                item.instanceData = new ProtoBuf.Item.InstanceData();
                item.instanceData.ShouldPool = false;
            }

            return item.instanceData;
        }

        private void UpdateAirstrikeTokenDisplayName(Item item, int charges)
        {
            if (item == null || string.IsNullOrWhiteSpace(config?.AirstrikeItem?.DisplayName))
            {
                return;
            }

            item.name = charges > 1
                ? config.AirstrikeItem.DisplayName + " x" + charges
                : config.AirstrikeItem.DisplayName;
        }

        private bool IsAirstrikeTokenDisplayNameCurrent(Item item, int charges)
        {
            if (item == null || string.IsNullOrWhiteSpace(config?.AirstrikeItem?.DisplayName))
            {
                return true;
            }

            var expectedName = charges > 1
                ? config.AirstrikeItem.DisplayName + " x" + charges
                : config.AirstrikeItem.DisplayName;
            return string.Equals(item.name ?? "", expectedName, StringComparison.Ordinal);
        }

        private void RefreshActiveAirstrikeItem(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }

            try
            {
                player.UpdateActiveItem(default);
                player.SendNetworkUpdateImmediate();
            }
            catch (Exception ex)
            {
                if (config.General.DebugMode)
                {
                    Puts("Active airstrike item refresh failed for " + player.displayName + ": " + ex.Message);
                }
            }
        }

        private bool IsPlayerHoldingAirstrikeTool(BasePlayer player)
        {
            return player != null && IsAirstrikeTargetingToolItem(player.GetActiveItem());
        }

        private bool IsAirstrikeKitItem(string shortname, ulong skin, string displayName)
        {
            if (config?.AirstrikeItem == null || string.IsNullOrWhiteSpace(shortname))
            {
                return false;
            }

            if (WantsCIDAirstrikeItem() && string.Equals(shortname, GetCustomAirstrikeShortname(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.Equals(shortname, GetLegacyAirstrikeShortname(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!config.AirstrikeItem.RequireCustomNameOrSkin)
            {
                return true;
            }

            var displayMatches = !string.IsNullOrWhiteSpace(config.AirstrikeItem.DisplayName)
                && string.Equals(displayName ?? "", config.AirstrikeItem.DisplayName, StringComparison.OrdinalIgnoreCase);
            var skinMatches = config.AirstrikeItem.SkinId != 0UL && skin == config.AirstrikeItem.SkinId;
            return displayMatches || skinMatches;
        }

        private GiveItemResult GiveAirstrikeTokensDetailed(BasePlayer player, int amount)
        {
            var result = new GiveItemResult();
            if (player == null)
            {
                result.Failure = "Player not found.";
                return result;
            }

            var remaining = Math.Max(1, amount);
            while (remaining > 0)
            {
                var charges = Math.Min(remaining, GetAirstrikeMaxChargesPerItem());
                var item = CreateAirstrikeToken(charges);
                if (item == null)
                {
                    result.Failure = "Could not create item '" + GetAirstrikeCreateShortname() + "'.";
                    return result;
                }

                if (player.inventory.GiveItem(item))
                {
                    result.Given += charges;
                    remaining -= charges;
                    continue;
                }

                item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                result.Given += charges;
                result.Dropped++;
                remaining -= charges;
            }

            return result;
        }

        private Item CreateAirstrikeToken()
        {
            return CreateAirstrikeToken(1);
        }

        private Item CreateAirstrikeToken(int amount)
        {
            TryRegisterAirstrikeCustomItemDefinition();

            var shortname = GetAirstrikeCreateShortname();
            var stackAmount = 1;
            var item = ItemManager.CreateByName(shortname, stackAmount, GetAirstrikeCreateSkinId());
            if (item == null)
            {
                var legacyShortname = GetLegacyAirstrikeShortname();
                if (config.AirstrikeItem.AllowVanillaFallbackIfCIDMissing
                    && !string.Equals(shortname, legacyShortname, StringComparison.OrdinalIgnoreCase))
                {
                    item = ItemManager.CreateByName(legacyShortname, stackAmount, config.AirstrikeItem.SkinId);
                }

                if (item == null)
                {
                    return null;
                }
            }

            if (!string.IsNullOrWhiteSpace(config.AirstrikeItem.DisplayName))
            {
                item.name = config.AirstrikeItem.DisplayName;
            }

            SetAirstrikeTokenCharges(item, Math.Max(1, amount));
            item.MarkDirty();
            return item;
        }

        private void TryInjectLootToken(ItemContainer inventory, params string[] containerNames)
        {
            if (inventory == null || config?.LootDistribution == null || !config.LootDistribution.Enabled)
            {
                return;
            }

            LootContainerRule rule;
            var matchedName = "";
            if (!TryGetLootRule(out rule, out matchedName, containerNames) || rule == null)
            {
                return;
            }

            if (UnityEngine.Random.value > rule.Chance)
            {
                return;
            }

            var amount = Math.Max(1, UnityEngine.Random.Range(rule.MinAmount, rule.MaxAmount + 1));
            var injected = 0;
            var item = CreateAirstrikeToken(amount);
            if (item == null)
            {
                PrintWarning("Loot injection failed: could not create item '" + GetAirstrikeCreateShortname() + "'.");
                return;
            }

            if (item.MoveToContainer(inventory))
            {
                injected = amount;
            }
            else
            {
                item.Remove();
            }

            if (injected <= 0)
            {
                return;
            }

            IncrementStat("loot_tokens_injected");
            IncrementStat("loot_tokens_injected_" + matchedName);
            SaveData();

            if (config.General.DebugMode)
            {
                Puts("Injected " + injected + " " + GetAirstrikeItemDisplayName() + " item(s) into loot container '" + matchedName + "'.");
            }
        }

        private bool TryGetLootRule(out LootContainerRule rule, out string matchedName, params string[] containerNames)
        {
            rule = null;
            matchedName = "";

            if (config?.LootDistribution?.ContainerRules == null || containerNames == null)
            {
                return false;
            }

            foreach (var rawName in containerNames)
            {
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    continue;
                }

                var candidates = GetLootContainerNameCandidates(rawName);
                foreach (var candidate in candidates)
                {
                    if (config.LootDistribution.ContainerRules.TryGetValue(candidate, out rule) && rule != null)
                    {
                        matchedName = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<string> GetLootContainerNameCandidates(string rawName)
        {
            var trimmed = rawName.Trim();
            yield return trimmed;

            var lower = trimmed.ToLowerInvariant();
            if (!string.Equals(trimmed, lower, StringComparison.Ordinal))
            {
                yield return lower;
            }

            var slash = lower.LastIndexOf('/');
            var fileName = slash >= 0 ? lower.Substring(slash + 1) : lower;
            if (!string.Equals(fileName, lower, StringComparison.Ordinal))
            {
                yield return fileName;
            }

            if (fileName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                yield return fileName.Substring(0, fileName.Length - ".prefab".Length);
            }

            const string cloneSuffix = "(clone)";
            if (fileName.EndsWith(cloneSuffix, StringComparison.OrdinalIgnoreCase))
            {
                yield return fileName.Substring(0, fileName.Length - cloneSuffix.Length).Trim();
            }
        }

        private int GetFinalRPCost(BasePlayer player, StrikeDefinition strike)
        {
            if (strike == null || !config.Currency.Enabled)
            {
                return 0;
            }

            if (IsAdmin(player) && config.Currency.AllowFreeAdminCalls)
            {
                return 0;
            }

            var cost = Math.Max(0, strike.RPCost);
            var discount = GetBestDiscount(player);
            if (discount <= 0f)
            {
                return cost;
            }

            return Math.Max(0, (int)Math.Ceiling(cost * (1f - discount)));
        }

        private float GetBestDiscount(BasePlayer player)
        {
            if (player == null || config.Currency.VipDiscountsByPermission == null)
            {
                return 0f;
            }

            var best = 0f;
            foreach (var entry in config.Currency.VipDiscountsByPermission)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                if (permission.UserHasPermission(player.UserIDString, entry.Key))
                {
                    best = Math.Max(best, Mathf.Clamp(entry.Value, 0f, 0.95f));
                }
            }

            return best;
        }

        private bool HasSufficientCurrency(BasePlayer player, int cost, out string message)
        {
            message = "";

            if (cost <= 0 || !config.Currency.Enabled)
            {
                return true;
            }

            if (currencyAdapter == null || !currencyAdapter.IsAvailable())
            {
                message = "Currency provider '" + config.Currency.Provider + "' is not available.";
                return false;
            }

            int balance;
            string error;
            if (!currencyAdapter.GetBalance(player, out balance, out error))
            {
                message = "Could not check RP balance: " + error;
                return false;
            }

            if (balance < cost)
            {
                message = "You need " + cost + " RP for that strike, but only have " + balance + ".";
                return false;
            }

            return true;
        }

        private string GetCooldownBlockMessage(BasePlayer player, StrikeDefinition strike)
        {
            var now = GetNow();
            var playerKey = player.UserIDString + ":" + strike.Id;
            double until;
            if (storedData.PlayerCooldownUntil.TryGetValue(playerKey, out until) && until > now)
            {
                return strike.DisplayName + " is on player cooldown for " + FormatSeconds(until - now) + ".";
            }

            if (config.General.EnableClanCooldowns && player.currentTeam != 0UL && strike.CooldownPerClanSeconds > 0f)
            {
                var clanKey = player.currentTeam.ToString() + ":" + strike.Id;
                if (storedData.ClanCooldownUntil.TryGetValue(clanKey, out until) && until > now)
                {
                    return strike.DisplayName + " is on team cooldown for " + FormatSeconds(until - now) + ".";
                }
            }

            if (config.General.EnableGlobalCooldowns && strike.GlobalCooldownSeconds > 0f)
            {
                if (storedData.GlobalCooldownUntil.TryGetValue(strike.Id, out until) && until > now)
                {
                    return strike.DisplayName + " is on global cooldown for " + FormatSeconds(until - now) + ".";
                }
            }

            return "";
        }

        private void StartCooldowns(BasePlayer player, StrikeDefinition strike)
        {
            var now = GetNow();
            if (strike.CooldownPerPlayerSeconds > 0f)
            {
                storedData.PlayerCooldownUntil[player.UserIDString + ":" + strike.Id] = now + strike.CooldownPerPlayerSeconds;
            }

            if (config.General.EnableClanCooldowns && player.currentTeam != 0UL && strike.CooldownPerClanSeconds > 0f)
            {
                storedData.ClanCooldownUntil[player.currentTeam.ToString() + ":" + strike.Id] = now + strike.CooldownPerClanSeconds;
            }

            if (config.General.EnableGlobalCooldowns && strike.GlobalCooldownSeconds > 0f)
            {
                storedData.GlobalCooldownUntil[strike.Id] = now + strike.GlobalCooldownSeconds;
            }

            SaveData();
        }

        private BaseEntity FindEntity(ulong entityId)
        {
            return entityId == 0UL ? null : BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as BaseEntity;
        }

        private AirstrikeTargetType ParseTargetType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return AirstrikeTargetType.Invalid;
            }

            switch (value.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant())
            {
                case "ground_ping":
                case "ground":
                    return AirstrikeTargetType.GroundPing;
                case "vehicle_ping":
                case "vehicle":
                    return AirstrikeTargetType.VehiclePing;
                case "player_ping":
                case "player":
                    return AirstrikeTargetType.PlayerPing;
                case "npc_ping":
                case "npc":
                    return AirstrikeTargetType.NpcPing;
                default:
                    return AirstrikeTargetType.Invalid;
            }
        }

        private string FormatTargetType(AirstrikeTargetType targetType)
        {
            switch (targetType)
            {
                case AirstrikeTargetType.GroundPing:
                    return "ground ping";
                case AirstrikeTargetType.VehiclePing:
                    return "vehicle ping";
                case AirstrikeTargetType.PlayerPing:
                    return "player ping";
                case AirstrikeTargetType.NpcPing:
                    return "NPC ping";
                default:
                    return "invalid ping";
            }
        }

        private bool StrikeAcceptsTargetType(StrikeDefinition strike, AirstrikeTargetType targetType)
        {
            if (strike == null || targetType == AirstrikeTargetType.Invalid)
            {
                return false;
            }

            var accepted = GetAcceptedTargetTypes(strike);
            return accepted.Contains(targetType);
        }

        private List<AirstrikeTargetType> GetAcceptedTargetTypes(StrikeDefinition strike)
        {
            var accepted = new List<AirstrikeTargetType>();
            if (strike == null)
            {
                return accepted;
            }

            if (strike.AcceptedTargetTypes != null)
            {
                foreach (var entry in strike.AcceptedTargetTypes)
                {
                    var parsed = ParseTargetType(entry);
                    if (parsed != AirstrikeTargetType.Invalid && !accepted.Contains(parsed))
                    {
                        accepted.Add(parsed);
                    }
                }
            }

            var legacy = ParseTargetType(strike.TargetType);
            if (legacy != AirstrikeTargetType.Invalid && !accepted.Contains(legacy))
            {
                accepted.Add(legacy);
            }

            return accepted;
        }

        private string FormatAcceptedTargetTypes(StrikeDefinition strike)
        {
            var accepted = GetAcceptedTargetTypes(strike);
            if (accepted.Count == 0)
            {
                return "none";
            }

            var labels = new List<string>();
            foreach (var targetType in accepted)
            {
                labels.Add(FormatTargetType(targetType));
            }

            return string.Join(", ", labels.ToArray());
        }

        private string DescribeTarget(AirstrikeTarget target)
        {
            if (target == null)
            {
                return "no target";
            }

            var entity = string.IsNullOrWhiteSpace(target.EntityShortPrefabName) ? "" : ", entity " + target.EntityShortPrefabName + "#" + target.EntityId;
            return FormatTargetType(target.Type) + " at " + FormatPosition(target.Position) + entity;
        }

        private string FormatPosition(Vector3 position)
        {
            return position.x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                + position.y.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                + position.z.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private string FormatMeters(float value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture) + "m";
        }

        private string FormatSeconds(double seconds)
        {
            seconds = Math.Max(0d, seconds);
            if (seconds >= 60d)
            {
                return Math.Ceiling(seconds / 60d).ToString("0", CultureInfo.InvariantCulture) + "m";
            }

            return Math.Ceiling(seconds).ToString("0", CultureInfo.InvariantCulture) + "s";
        }

        private string GetAirstrikeItemDisplayName()
        {
            return string.IsNullOrWhiteSpace(config.AirstrikeItem.DisplayName) ? config.AirstrikeItem.Shortname : config.AirstrikeItem.DisplayName;
        }

        private string GetOpenCommand()
        {
            return NormalizeCommand(config?.Selection?.OpenMenuCommand) ?? "strike";
        }

        private string NormalizeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return "strike";
            }

            return command.Trim().TrimStart('/');
        }

        private double GetNow()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private double GetPreciseNow()
        {
            return DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
        }

        private bool IsAdmin(BasePlayer player)
        {
            return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, AdminPermission));
        }

        private bool CanUseAdminCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null || arg.Connection.authLevel > 0 || arg.IsAdmin)
            {
                return true;
            }

            var player = arg.Connection.player as BasePlayer;
            return IsAdmin(player);
        }

        private BasePlayer FindPlayer(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            ulong id;
            if (ulong.TryParse(value, out id))
            {
                return BasePlayer.FindAwakeOrSleeping(id.ToString());
            }

            value = value.ToLowerInvariant();
            foreach (var player in BasePlayer.allPlayerList)
            {
                if (player.displayName != null && player.displayName.ToLowerInvariant().Contains(value))
                {
                    return player;
                }
            }

            return null;
        }

        private void Reply(BasePlayer player, string message)
        {
            player.ChatMessage((config?.ChatPrefix ?? "<color=#ce422b>[Airstrikes]</color>") + " " + message);
        }

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName) ?? new StoredData();
            }
            catch
            {
                PrintWarning("Could not read data; creating a fresh PortableAirstrikes data file.");
                storedData = new StoredData();
            }

            if (storedData.LastStrikeByUser == null)
            {
                storedData.LastStrikeByUser = new Dictionary<string, string>();
            }

            if (storedData.DefaultStrikeByUser == null)
            {
                storedData.DefaultStrikeByUser = new Dictionary<string, string>();
            }

            if (storedData.PlayerCooldownUntil == null)
            {
                storedData.PlayerCooldownUntil = new Dictionary<string, double>();
            }

            if (storedData.ClanCooldownUntil == null)
            {
                storedData.ClanCooldownUntil = new Dictionary<string, double>();
            }

            if (storedData.GlobalCooldownUntil == null)
            {
                storedData.GlobalCooldownUntil = new Dictionary<string, double>();
            }

            if (storedData.Stats == null)
            {
                storedData.Stats = new Dictionary<string, int>();
            }

            if (storedData.RecentCalls == null)
            {
                storedData.RecentCalls = new List<StrikeCallAuditRecord>();
            }

            TrimRecentCallHistory();

            SaveData();
        }

        private void LoadVisualProfiles()
        {
            VisualProfileFile candidate;
            Dictionary<string, string> motionModes;
            Dictionary<string, string> releaseModes;
            Dictionary<string, string> warnings;
            string message;
            if (!TryReadVisualProfiles(out candidate, out motionModes, out releaseModes, out warnings, out message))
            {
                lastVisualProfileLoadMessage = message;
                lastVisualProfileLoadAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                lastVisualProfileLoadSucceeded = false;
                PrintWarning(message);
                if (visualProfileFile == null)
                {
                    visualProfileFile = new VisualProfileFile();
                    NormalizeVisualProfiles();
                }
                return;
            }

            ApplyVisualProfileSnapshot(candidate, motionModes, releaseModes, warnings, message);
        }

        private string ResolveVisualProfilesDataPath()
        {
            var relative = (VisualProfilesDataFileName + ".json").TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(Interface.Oxide.DataDirectory, relative);
        }

        private void NormalizeVisualProfiles()
        {
            if (visualProfileFile == null)
            {
                visualProfileFile = new VisualProfileFile();
            }

            NormalizeVisualProfileFile(visualProfileFile);
        }

        private void NormalizeVisualProfileFile(VisualProfileFile file)
        {
            if (file == null)
            {
                return;
            }

            file.CompilerVersion = (file.CompilerVersion ?? "").Trim();
            file.PublishedSha256 = (file.PublishedSha256 ?? "").Trim().ToLowerInvariant();

            var normalized = new Dictionary<string, VisualProfileConfig>(StringComparer.OrdinalIgnoreCase);
            if (file.Profiles != null)
            {
                foreach (var entry in file.Profiles)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
                    {
                        continue;
                    }

                    NormalizeVisualProfile(entry.Value);
                    normalized[entry.Key.Trim()] = entry.Value;
                }
            }

            file.Profiles = normalized;
        }

        private bool TryReadVisualProfiles(
            out VisualProfileFile candidate,
            out Dictionary<string, string> motionModes,
            out Dictionary<string, string> releaseModes,
            out Dictionary<string, string> warnings,
            out string message)
        {
            candidate = null;
            motionModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            releaseModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            warnings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            message = "";
            var path = ResolveVisualProfilesDataPath();
            if (!File.Exists(path))
            {
                message = "VisualProfiles.json does not exist; the current in-memory snapshot was retained and generated fallback paths remain available.";
                return false;
            }

            try
            {
                candidate = JsonConvert.DeserializeObject<VisualProfileFile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                message = "Could not parse PortableAirstrikes VisualProfiles.json; the current in-memory snapshot was retained. Error: " + ex.Message;
                return false;
            }

            if (candidate == null)
            {
                message = "VisualProfiles.json deserialized to null; the current in-memory snapshot was retained.";
                return false;
            }

            if (!TryValidateVisualProfileFile(candidate, out motionModes, out releaseModes, out warnings, out message))
            {
                message = "VisualProfiles.json validation failed; the current in-memory snapshot was retained. " + message;
                return false;
            }

            try
            {
                NormalizeVisualProfileFile(candidate);
            }
            catch (Exception ex)
            {
                message = "VisualProfiles.json normalization failed; the current in-memory snapshot was retained. Error: " + ex.Message;
                candidate = null;
                return false;
            }

            message = "Loaded " + (candidate.Profiles == null ? 0 : candidate.Profiles.Count) + " visual profile(s), schema " + candidate.SchemaVersion + ".";
            return true;
        }

        private void ApplyVisualProfileSnapshot(
            VisualProfileFile candidate,
            Dictionary<string, string> motionModes,
            Dictionary<string, string> releaseModes,
            Dictionary<string, string> warnings,
            string message)
        {
            visualProfileFile = candidate ?? new VisualProfileFile();
            visualProfileMotionModes = motionModes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            visualProfileReleaseModes = releaseModes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            visualProfileWarnings = warnings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            lastVisualProfileLoadMessage = message ?? "Visual profiles loaded.";
            lastVisualProfileLoadAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            lastVisualProfileLoadSucceeded = true;

            foreach (var warning in visualProfileWarnings)
            {
                PrintWarning("Visual profile '" + warning.Key + "' is using fallback-because-invalid: " + warning.Value);
            }
        }

        private Dictionary<string, object> BuildVisualProfileApiResult(bool success, string message)
        {
            return new Dictionary<string, object>
            {
                ["success"] = success,
                ["profileCount"] = visualProfileFile?.Profiles == null ? 0 : visualProfileFile.Profiles.Count,
                ["schemaVersion"] = visualProfileFile?.SchemaVersion ?? 0,
                ["compilerVersion"] = visualProfileFile?.CompilerVersion ?? "",
                ["publishedRevision"] = visualProfileFile?.PublishedRevision ?? 0L,
                ["publishedSha256"] = visualProfileFile?.PublishedSha256 ?? "",
                ["loadedAtUtc"] = lastVisualProfileLoadAtUtc,
                ["lastLoadSucceeded"] = lastVisualProfileLoadSucceeded,
                ["message"] = message ?? "",
                ["motionModes"] = new Dictionary<string, string>(visualProfileMotionModes, StringComparer.OrdinalIgnoreCase),
                ["releaseModes"] = new Dictionary<string, string>(visualProfileReleaseModes, StringComparer.OrdinalIgnoreCase),
                ["warnings"] = new Dictionary<string, string>(visualProfileWarnings, StringComparer.OrdinalIgnoreCase)
            };
        }

        private string GetVisualProfileMotionMode(string profileId)
        {
            string mode;
            return !string.IsNullOrWhiteSpace(profileId) && visualProfileMotionModes.TryGetValue(profileId, out mode)
                ? mode
                : "legacy-v1";
        }

        private string GetVisualProfileReleaseMode(string profileId)
        {
            string mode;
            return !string.IsNullOrWhiteSpace(profileId) && visualProfileReleaseModes.TryGetValue(profileId, out mode)
                ? mode
                : "legacy-v1";
        }

        private bool TryValidateVisualProfileFile(
            VisualProfileFile file,
            out Dictionary<string, string> motionModes,
            out Dictionary<string, string> releaseModes,
            out Dictionary<string, string> warnings,
            out string error)
        {
            motionModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            releaseModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            warnings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            if (file == null)
            {
                error = "Profile file is null.";
                return false;
            }

            if (file.SchemaVersion != 1 && file.SchemaVersion != 2)
            {
                error = "SchemaVersion must be 1 or 2.";
                return false;
            }

            if ((file.CompilerVersion ?? "").Length > 100 || file.PublishedRevision < 0 || !IsValidOptionalSha256(file.PublishedSha256))
            {
                error = "Published metadata is invalid.";
                return false;
            }

            if (file.Profiles == null)
            {
                file.Profiles = new Dictionary<string, VisualProfileConfig>(StringComparer.OrdinalIgnoreCase);
            }

            if (file.Profiles.Count > MaxVisualProfiles)
            {
                error = "Profiles exceeds the " + MaxVisualProfiles + " profile limit.";
                return false;
            }

            foreach (var entry in file.Profiles)
            {
                var profileId = (entry.Key ?? "").Trim();
                if (!IsSafeProfileKey(profileId))
                {
                    error = "Profiles." + profileId + " has an invalid profile key.";
                    return false;
                }

                if (!TryValidateLegacyVisualProfile(entry.Value, "Profiles." + profileId, out error))
                {
                    return false;
                }

                CompiledVisualTrack compiledTrack = null;
                string compiledTrackError = "";
                if (file.SchemaVersion >= 2 && TryValidateCompiledTrack(entry.Value, out compiledTrack, out compiledTrackError))
                {
                    motionModes[profileId] = "compiled-v2";
                }
                else if (entry.Value.CompiledTrack != null)
                {
                    motionModes[profileId] = "fallback-because-invalid";
                    warnings[profileId] = file.SchemaVersion < 2
                        ? "CompiledTrack requires SchemaVersion 2."
                        : compiledTrackError;
                }
                else
                {
                    motionModes[profileId] = "legacy-v1";
                }

                List<VisualPayloadEvent> compiledEvents = null;
                string compiledReleaseError = "";
                if (file.SchemaVersion >= 2 && TryValidateCompiledReleaseEvents(entry.Value, out compiledEvents, out compiledReleaseError))
                {
                    releaseModes[profileId] = "compiled-v2";
                }
                else if (entry.Value.CompiledReleaseEvents != null && entry.Value.CompiledReleaseEvents.Count > 0)
                {
                    releaseModes[profileId] = "fallback-because-invalid";
                    var releaseWarning = file.SchemaVersion < 2
                        ? "CompiledReleaseEvents requires SchemaVersion 2."
                        : compiledReleaseError;
                    warnings[profileId] = warnings.ContainsKey(profileId)
                        ? warnings[profileId] + " " + releaseWarning
                        : releaseWarning;
                }
                else
                {
                    releaseModes[profileId] = "legacy-v1";
                }
            }

            return true;
        }

        private bool TryValidateLegacyVisualProfile(VisualProfileConfig profile, string path, out string error)
        {
            error = "";
            if (profile == null)
            {
                error = path + " is null.";
                return false;
            }

            if (!IsSupportedVisualProfileVehicle(profile.Vehicle))
            {
                error = path + ".Vehicle is unsupported.";
                return false;
            }

            if (!IsFinite(profile.DurationSeconds) || profile.DurationSeconds < 0.5f || profile.DurationSeconds > 120f
                || !IsFinite(profile.FirstPayloadDelaySeconds) || profile.FirstPayloadDelaySeconds < 0f || profile.FirstPayloadDelaySeconds > profile.DurationSeconds
                || !IsFinite(profile.RotationSmoothTimeSeconds) || profile.RotationSmoothTimeSeconds < 0f || profile.RotationSmoothTimeSeconds > 2f
                || !IsFinite(profile.MinimumTerrainClearance) || profile.MinimumTerrainClearance < 0f || profile.MinimumTerrainClearance > 250f)
            {
                error = path + " contains an invalid duration, delay, smoothing, or terrain-clearance value.";
                return false;
            }

            var releaseMode = (profile.PayloadReleaseMode ?? "").Trim().ToLowerInvariant();
            if (releaseMode != "manual" && releaseMode != "generated")
            {
                error = path + ".PayloadReleaseMode must be manual or generated.";
                return false;
            }

            if (profile.MaxPayloadCount < 0 || profile.MaxPayloadCount > 200
                || !IsFinite(profile.PayloadReleaseIntervalSeconds) || profile.PayloadReleaseIntervalSeconds <= 0f || profile.PayloadReleaseIntervalSeconds > 30f)
            {
                error = path + " has invalid release-count or interval settings.";
                return false;
            }

            if (profile.Waypoints == null || profile.Waypoints.Count < 2 || profile.Waypoints.Count > MaxVisualProfileWaypoints)
            {
                error = path + ".Waypoints must contain 2 to " + MaxVisualProfileWaypoints + " entries.";
                return false;
            }

            var previousTime = -1f;
            for (var i = 0; i < profile.Waypoints.Count; i++)
            {
                var waypoint = profile.Waypoints[i];
                if (waypoint == null
                    || !IsFinite(waypoint.Time) || waypoint.Time < 0f || waypoint.Time > profile.DurationSeconds
                    || !IsFiniteInRange(waypoint.X, -2000f, 2000f)
                    || !IsFiniteInRange(waypoint.Y, -100f, 1000f)
                    || !IsFiniteInRange(waypoint.Z, -3000f, 3000f)
                    || !IsFiniteInRange(waypoint.RotationX, -100000f, 100000f)
                    || !IsFiniteInRange(waypoint.RotationY, -100000f, 100000f)
                    || !IsFiniteInRange(waypoint.RotationZ, -100000f, 100000f))
                {
                    error = path + ".Waypoints[" + i + "] is invalid.";
                    return false;
                }

                if (i == 0 && Math.Abs(waypoint.Time) > 0.01f)
                {
                    error = path + ".Waypoints[0].Time must be zero.";
                    return false;
                }

                if (i > 0 && waypoint.Time <= previousTime)
                {
                    error = path + ".Waypoints[" + i + "].Time must be strictly increasing.";
                    return false;
                }

                previousTime = waypoint.Time;
            }

            if (!TryValidatePayloadEvent(profile.ReleaseTemplate ?? new VisualPayloadEvent(), profile.DurationSeconds, true, false, path + ".ReleaseTemplate", out error))
            {
                return false;
            }

            if (profile.PayloadEvents == null)
            {
                profile.PayloadEvents = new List<VisualPayloadEvent>();
            }

            if (profile.PayloadEvents.Count > MaxPayloadEventsInProfile)
            {
                error = path + ".PayloadEvents exceeds the " + MaxPayloadEventsInProfile + " event runtime limit.";
                return false;
            }

            for (var i = 0; i < profile.PayloadEvents.Count; i++)
            {
                if (!TryValidatePayloadEvent(profile.PayloadEvents[i], profile.DurationSeconds, false, false, path + ".PayloadEvents[" + i + "]", out error))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryValidateCompiledTrack(VisualProfileConfig profile, out CompiledVisualTrack track, out string error)
        {
            track = profile == null ? null : profile.CompiledTrack;
            error = "";
            if (track == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(track.CompilerVersion)
                || track.CompilerVersion.Length > 100
                || string.IsNullOrWhiteSpace(track.SourceHash)
                || track.SourceHash.Length > 256)
            {
                error = "CompiledTrack compiler metadata is invalid.";
                return false;
            }

            if (!string.Equals((track.CoordinateSystem ?? "").Trim(), CompiledCoordinateSystem, StringComparison.Ordinal))
            {
                error = "CompiledTrack.CoordinateSystem is unsupported.";
                return false;
            }

            if (!IsFiniteInRange(track.SampleRateHz, 1f, 240f)
                || !IsFiniteInRange(track.SampleIntervalSeconds, 0.0001f, 1f)
                || !IsFiniteInRange(track.DurationSeconds, 0.5f, 120f)
                || profile == null || Math.Abs(track.DurationSeconds - profile.DurationSeconds) > 0.02f)
            {
                error = "CompiledTrack timing metadata is invalid or does not match DurationSeconds.";
                return false;
            }

            var expectedInterval = 1f / track.SampleRateHz;
            if (Math.Abs(expectedInterval - track.SampleIntervalSeconds) > Math.Max(0.0005f, expectedInterval * 0.02f))
            {
                error = "CompiledTrack SampleRateHz and SampleIntervalSeconds do not agree.";
                return false;
            }

            if (track.Frames == null || track.Frames.Count < 2 || track.Frames.Count > MaxCompiledVisualFrames)
            {
                error = "CompiledTrack.Frames must contain 2 to " + MaxCompiledVisualFrames + " entries.";
                return false;
            }

            var previousTime = -1f;
            for (var i = 0; i < track.Frames.Count; i++)
            {
                var frame = track.Frames[i];
                if (frame == null
                    || !IsFinite(frame.Time) || frame.Time < 0f || frame.Time > track.DurationSeconds + 0.001f
                    || !IsFiniteInRange(frame.X, -2000f, 2000f)
                    || !IsFiniteInRange(frame.Y, -100f, 1000f)
                    || !IsFiniteInRange(frame.Z, -3000f, 3000f)
                    || !IsFinite(frame.Qx) || !IsFinite(frame.Qy) || !IsFinite(frame.Qz) || !IsFinite(frame.Qw))
                {
                    error = "CompiledTrack.Frames[" + i + "] contains a non-finite or out-of-range value.";
                    return false;
                }

                var magnitudeSquared = (frame.Qx * frame.Qx) + (frame.Qy * frame.Qy) + (frame.Qz * frame.Qz) + (frame.Qw * frame.Qw);
                if (!IsFinite(magnitudeSquared) || magnitudeSquared < 0.00000001f)
                {
                    error = "CompiledTrack.Frames[" + i + "] quaternion is not normalizable.";
                    return false;
                }

                if (i == 0 && Math.Abs(frame.Time) > 0.01f)
                {
                    error = "CompiledTrack first frame must start at time zero.";
                    return false;
                }

                if (i > 0 && frame.Time <= previousTime)
                {
                    error = "CompiledTrack frame times must be strictly increasing.";
                    return false;
                }

                previousTime = frame.Time;
            }

            if (Math.Abs(track.Frames[track.Frames.Count - 1].Time - track.DurationSeconds) > 0.02f)
            {
                error = "CompiledTrack final frame must end at DurationSeconds.";
                return false;
            }

            return true;
        }

        private bool TryValidateCompiledReleaseEvents(VisualProfileConfig profile, out List<VisualPayloadEvent> compiledEvents, out string error)
        {
            compiledEvents = profile == null ? null : profile.CompiledReleaseEvents;
            error = "";
            if (compiledEvents == null || compiledEvents.Count == 0)
            {
                return false;
            }

            if (compiledEvents.Count > MaxCompiledReleaseEvents)
            {
                error = "CompiledReleaseEvents exceeds the " + MaxCompiledReleaseEvents + " event limit.";
                return false;
            }

            var previousTime = -1f;
            for (var i = 0; i < compiledEvents.Count; i++)
            {
                var payloadEvent = compiledEvents[i];
                if (!TryValidatePayloadEvent(payloadEvent, profile.DurationSeconds, false, true, "CompiledReleaseEvents[" + i + "]", out error))
                {
                    return false;
                }

                if (payloadEvent.Time < previousTime)
                {
                    error = "CompiledReleaseEvents must be ordered by Time.";
                    return false;
                }

                previousTime = payloadEvent.Time;
            }

            return true;
        }

        private bool TryValidatePayloadEvent(VisualPayloadEvent payloadEvent, float duration, bool allowEmptyPayload, bool requirePerUnit, string path, out string error)
        {
            error = "";
            if (payloadEvent == null
                || !IsFiniteInRange(payloadEvent.Time, 0f, duration)
                || payloadEvent.Index < 0 || payloadEvent.Index > MaxCompiledReleaseEvents
                || payloadEvent.Count < 1 || payloadEvent.Count > 200
                || (requirePerUnit && payloadEvent.Count != 1)
                || !IsFiniteInRange(payloadEvent.CarrierOffsetX, -250f, 250f)
                || !IsFiniteInRange(payloadEvent.CarrierOffsetY, -250f, 250f)
                || !IsFiniteInRange(payloadEvent.CarrierOffsetZ, -250f, 250f)
                || !IsFiniteInRange(payloadEvent.TargetOffsetX, -500f, 500f)
                || !IsFiniteInRange(payloadEvent.TargetOffsetY, -500f, 500f)
                || !IsFiniteInRange(payloadEvent.TargetOffsetZ, -500f, 500f)
                || !IsValidOptionalFloat(payloadEvent.SpreadRadius, 0f, 250f)
                || !IsValidOptionalFloat(payloadEvent.LaunchSpeed, 0f, 500f)
                || !IsValidOptionalFloat(payloadEvent.FuseSeconds, 0f, 120f)
                || !IsFiniteInRange(payloadEvent.DamageScale, 0f, 10f)
                || !IsValidOptionalFloat(payloadEvent.VehicleDamageScale, 0f, 10f)
                || !IsValidOptionalFloat(payloadEvent.SplashRadius, 0f, 100f)
                || !IsValidOptionalFloat(payloadEvent.ImpactRadius, 0f, 100f)
                || !IsValidOptionalFloat(payloadEvent.MaxTrackingSeconds, 0f, 120f)
                || !IsValidOptionalFloat(payloadEvent.MaxTrackingDistance, 0f, 3000f))
            {
                error = path + " contains an invalid numeric value or count.";
                return false;
            }

            var payload = NormalizePayloadId(payloadEvent.Payload);
            if ((!allowEmptyPayload || !string.IsNullOrWhiteSpace(payload)) && !IsSupportedVisualPayload(payload))
            {
                error = path + ".Payload is unsupported.";
                return false;
            }

            if (payloadEvent.DamageScales != null)
            {
                if (payloadEvent.DamageScales.Count > 64)
                {
                    error = path + ".DamageScales contains too many entries.";
                    return false;
                }

                foreach (var scale in payloadEvent.DamageScales)
                {
                    if (!IsSafeDictionaryKey(scale.Key) || !IsFiniteInRange(scale.Value, 0f, 10f))
                    {
                        error = path + ".DamageScales contains an invalid key or value.";
                        return false;
                    }
                }
            }

            return true;
        }

        private bool IsSupportedVisualProfileVehicle(string vehicle)
        {
            switch ((vehicle ?? "").Trim().ToLowerInvariant())
            {
                case "drone":
                case "cargo_plane":
                case "f15":
                case "a10":
                case "attack_heli":
                    return true;
                default:
                    return false;
            }
        }

        private bool IsSupportedVisualPayload(string payload)
        {
            switch (NormalizePayloadId(payload))
            {
                case "bee_grenade":
                case "bee_catapult_bomb":
                case "beancan":
                case "f1_grenade":
                case "smoke":
                case "flashbang":
                case "he_40mm":
                case "molotov":
                case "firebomb":
                case "propane_bomb":
                case "hv_rocket":
                case "rocket":
                case "incendiary_rocket":
                case "mortar_he_payload":
                case "mortar_frag_payload":
                case "bradley_longbarrel_burst":
                case "homing_missile":
                case "mlrs_rocket":
                    return true;
                default:
                    return false;
            }
        }

        private bool IsSafeProfileKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
            {
                return false;
            }

            var first = value[0];
            if ((first < 'a' || first > 'z') && (first < '0' || first > '9'))
            {
                return false;
            }

            foreach (var character in value)
            {
                if ((character < 'a' || character > 'z')
                    && (character < '0' || character > '9')
                    && character != '.' && character != '_' && character != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSafeDictionaryKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.')
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsValidOptionalSha256(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0)
            {
                return true;
            }

            if (value.Length != 64)
            {
                return false;
            }

            foreach (var character in value)
            {
                if ((character < '0' || character > '9')
                    && (character < 'a' || character > 'f')
                    && (character < 'A' || character > 'F'))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private bool IsFiniteInRange(float value, float minimum, float maximum)
        {
            return IsFinite(value) && value >= minimum && value <= maximum;
        }

        private bool IsValidOptionalFloat(float value, float minimum, float maximum)
        {
            return IsFinite(value) && (value < 0f || value >= minimum && value <= maximum);
        }

        private void NormalizeVisualProfile(VisualProfileConfig profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.Vehicle = NormalizeVisualProfileVehicle(profile.Vehicle, null, null, DeliveryVisualProfile.Mlrs) ?? "f15";
            profile.DurationSeconds = Mathf.Clamp(profile.DurationSeconds <= 0f ? 8f : profile.DurationSeconds, 0.5f, 120f);
            profile.FirstPayloadDelaySeconds = Mathf.Clamp(profile.FirstPayloadDelaySeconds < 0f ? 0f : profile.FirstPayloadDelaySeconds, 0f, profile.DurationSeconds);
            profile.RotationSmoothTimeSeconds = Mathf.Clamp(profile.RotationSmoothTimeSeconds <= 0f ? 0.12f : profile.RotationSmoothTimeSeconds, MinimumVisualRotationSmoothTimeSeconds, 2f);
            profile.PayloadReleaseMode = NormalizePayloadReleaseMode(profile.PayloadReleaseMode);
            profile.MaxPayloadCount = Mathf.Clamp(profile.MaxPayloadCount, 0, 200);
            profile.PayloadReleaseIntervalSeconds = Mathf.Clamp(profile.PayloadReleaseIntervalSeconds <= 0f ? DefaultPayloadReleaseIntervalSeconds : profile.PayloadReleaseIntervalSeconds, 0.01f, 30f);
            if (profile.ReleaseTemplate == null)
            {
                profile.ReleaseTemplate = new VisualPayloadEvent();
            }

            NormalizePayloadEvent(profile.ReleaseTemplate, profile.FirstPayloadDelaySeconds, profile.DurationSeconds, 0);
            var fallbackClearance = string.Equals(profile.Vehicle, "drone", StringComparison.OrdinalIgnoreCase) ? DefaultVisualProfileDroneTerrainClearance : DefaultVisualProfileAircraftTerrainClearance;
            profile.MinimumTerrainClearance = Mathf.Clamp(profile.MinimumTerrainClearance <= 0f ? fallbackClearance : profile.MinimumTerrainClearance, 0f, 250f);

            if (profile.Waypoints == null)
            {
                profile.Waypoints = new List<VisualProfileWaypoint>();
            }

            profile.Waypoints.RemoveAll(wp => wp == null);
            foreach (var waypoint in profile.Waypoints)
            {
                waypoint.Time = Mathf.Clamp(waypoint.Time, 0f, profile.DurationSeconds);
                waypoint.X = Mathf.Clamp(waypoint.X, -2000f, 2000f);
                waypoint.Y = Mathf.Clamp(waypoint.Y, -100f, 1000f);
                waypoint.Z = Mathf.Clamp(waypoint.Z, -3000f, 3000f);
            }

            profile.Waypoints.Sort((a, b) => a.Time.CompareTo(b.Time));
            for (var i = 1; i < profile.Waypoints.Count; i++)
            {
                if (profile.Waypoints[i].Time <= profile.Waypoints[i - 1].Time + 0.005f)
                {
                    profile.Waypoints[i].Time = Mathf.Min(profile.DurationSeconds, profile.Waypoints[i - 1].Time + 0.01f);
                }
            }

            NormalizePayloadEvents(profile);
        }

        private void NormalizePayloadEvents(VisualProfileConfig profile)
        {
            if (profile == null)
            {
                return;
            }

            if (profile.PayloadEvents == null)
            {
                profile.PayloadEvents = new List<VisualPayloadEvent>();
            }

            profile.PayloadEvents.RemoveAll(ev => ev == null);
            if (profile.PayloadEvents.Count > MaxPayloadEventsInProfile)
            {
                profile.PayloadEvents.RemoveRange(MaxPayloadEventsInProfile, profile.PayloadEvents.Count - MaxPayloadEventsInProfile);
            }

            for (var i = 0; i < profile.PayloadEvents.Count; i++)
            {
                NormalizePayloadEvent(profile.PayloadEvents[i], profile.PayloadEvents[i].Time, profile.DurationSeconds, i + 1);
            }

            profile.PayloadEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
            for (var i = 0; i < profile.PayloadEvents.Count; i++)
            {
                profile.PayloadEvents[i].Index = i + 1;
            }
        }

        private void NormalizePayloadEvent(VisualPayloadEvent payloadEvent, float fallbackTime, float durationSeconds, int index)
        {
            if (payloadEvent == null)
            {
                return;
            }

            var safeDuration = Mathf.Clamp(durationSeconds <= 0f ? 8f : durationSeconds, 0.5f, 120f);
            payloadEvent.Time = Mathf.Clamp(payloadEvent.Time < 0f ? fallbackTime : payloadEvent.Time, 0f, safeDuration);
            payloadEvent.Payload = NormalizePayloadId(payloadEvent.Payload);
            payloadEvent.Index = Math.Max(0, index);
            payloadEvent.Count = Mathf.Clamp(payloadEvent.Count <= 0 ? 1 : payloadEvent.Count, 1, 200);
            payloadEvent.CarrierOffsetX = Mathf.Clamp(payloadEvent.CarrierOffsetX, -250f, 250f);
            payloadEvent.CarrierOffsetY = Mathf.Clamp(payloadEvent.CarrierOffsetY, -250f, 250f);
            payloadEvent.CarrierOffsetZ = Mathf.Clamp(payloadEvent.CarrierOffsetZ, -250f, 250f);
            payloadEvent.TargetOffsetX = Mathf.Clamp(payloadEvent.TargetOffsetX, -500f, 500f);
            payloadEvent.TargetOffsetY = Mathf.Clamp(payloadEvent.TargetOffsetY, -500f, 500f);
            payloadEvent.TargetOffsetZ = Mathf.Clamp(payloadEvent.TargetOffsetZ, -500f, 500f);
            payloadEvent.SpreadRadius = ClampOptional(payloadEvent.SpreadRadius, 0f, 250f);
            payloadEvent.LaunchSpeed = ClampOptional(payloadEvent.LaunchSpeed, 1f, 350f);
            payloadEvent.FuseSeconds = ClampOptional(payloadEvent.FuseSeconds, 0f, 120f);
            payloadEvent.DamageScale = NormalizeDamageScale(payloadEvent.DamageScale);
            payloadEvent.VehicleDamageScale = NormalizeDamageScale(payloadEvent.VehicleDamageScale);
            payloadEvent.SplashRadius = ClampOptional(payloadEvent.SplashRadius, 0f, 100f);
            payloadEvent.ImpactRadius = ClampOptional(payloadEvent.ImpactRadius, 0f, 100f);
            payloadEvent.MaxTrackingSeconds = ClampOptional(payloadEvent.MaxTrackingSeconds, 0.1f, 120f);
            payloadEvent.MaxTrackingDistance = ClampOptional(payloadEvent.MaxTrackingDistance, 1f, 2500f);

            if (payloadEvent.DamageScales == null)
            {
                payloadEvent.DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }

            NormalizePayloadDamageScale(payloadEvent, "Players");
            NormalizePayloadDamageScale(payloadEvent, "Buildings");
            NormalizePayloadDamageScale(payloadEvent, "Vehicles");
            NormalizePayloadDamageScale(payloadEvent, "Turrets");
            NormalizePayloadDamageScale(payloadEvent, "Deployables");
        }

        private float ClampOptional(float value, float min, float max)
        {
            return value < 0f ? -1f : Mathf.Clamp(value, min, max);
        }

        private float NormalizeDamageScale(float value)
        {
            return Mathf.Clamp(value <= 0f ? 1f : value, 0f, 10f);
        }

        private void NormalizePayloadDamageScale(VisualPayloadEvent payloadEvent, string key)
        {
            if (payloadEvent == null || payloadEvent.DamageScales == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            float value;
            if (!payloadEvent.DamageScales.TryGetValue(key, out value))
            {
                return;
            }

            payloadEvent.DamageScales[key] = NormalizeDamageScale(value);
        }

        private string NormalizePayloadReleaseMode(string mode)
        {
            return string.Equals((mode ?? "").Trim(), "generated", StringComparison.OrdinalIgnoreCase)
                ? "generated"
                : "manual";
        }

        private string NormalizePayloadId(string payload)
        {
            return string.IsNullOrWhiteSpace(payload) ? "" : payload.Trim().ToLowerInvariant();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataFileName, storedData ?? new StoredData(), true);
        }

        private void RegisterChatCommand()
        {
            cmd.AddChatCommand(GetOpenCommand(), this, nameof(CmdStrike));
        }

        private void RegisterPermissions()
        {
            permission.RegisterPermission(AdminPermission, this);
            permission.RegisterPermission(UsePermission, this);

            if (config?.Currency?.VipDiscountsByPermission != null)
            {
                foreach (var entry in config.Currency.VipDiscountsByPermission)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key))
                    {
                        permission.RegisterPermission(entry.Key, this);
                    }
                }
            }

            if (config?.StrikeDefinitions == null)
            {
                return;
            }

            foreach (var strike in config.StrikeDefinitions.Values)
            {
                if (strike != null && !string.IsNullOrWhiteSpace(strike.PermissionRequired))
                {
                    permission.RegisterPermission(strike.PermissionRequired, this);
                }
            }
        }

        private void RefreshCurrencyAdapter()
        {
            if (config?.Currency == null || !config.Currency.Enabled)
            {
                currencyAdapter = new NullCurrencyAdapter();
                return;
            }

            var provider = (config.Currency.Provider ?? "").Trim();
            if (string.Equals(provider, "Economics", StringComparison.OrdinalIgnoreCase))
            {
                currencyAdapter = new EconomicsCurrencyAdapter(this);
                return;
            }

            currencyAdapter = new ServerRewardsCurrencyAdapter(this);
        }

        private void NormalizeConfig()
        {
            var defaults = new Configuration();
            var previousConfigVersion = config?.ConfigVersion ?? 0;
            var wasBeforeExperimentalDefaultReset = config.ConfigVersion < 10;

            if (config.General == null)
            {
                config.General = defaults.General;
            }

            if (config.AirstrikeItem == null)
            {
                config.AirstrikeItem = defaults.AirstrikeItem;
            }

            if (config.Currency == null)
            {
                config.Currency = defaults.Currency;
            }

            if (config.Selection == null)
            {
                config.Selection = defaults.Selection;
            }

            if (config.DeliveryScaling == null)
            {
                config.DeliveryScaling = defaults.DeliveryScaling;
            }

            if (config.DeliveryVisuals == null)
            {
                config.DeliveryVisuals = defaults.DeliveryVisuals;
            }

            if (config.DamageScales == null)
            {
                config.DamageScales = defaults.DamageScales;
            }

            if (config.LootDistribution == null)
            {
                config.LootDistribution = defaults.LootDistribution;
            }

            if (config.AuditWebhooks == null)
            {
                config.AuditWebhooks = defaults.AuditWebhooks;
            }

            if (string.IsNullOrWhiteSpace(config.ChatPrefix))
            {
                config.ChatPrefix = defaults.ChatPrefix;
            }

            config.General.MaxPingAgeSeconds = Mathf.Clamp(config.General.MaxPingAgeSeconds, 3f, 300f);
            config.General.MaxCallRange = Mathf.Clamp(config.General.MaxCallRange, 25f, 2000f);
            config.General.MinimumDistanceFromCaller = Mathf.Clamp(config.General.MinimumDistanceFromCaller, 0f, config.General.MaxCallRange);
            config.General.SafeZoneBlockRadius = Mathf.Clamp(config.General.SafeZoneBlockRadius, 0f, 1000f);
            config.General.MonumentBlockRadiusPadding = Mathf.Clamp(config.General.MonumentBlockRadiusPadding, 0f, 500f);
            config.General.DefaultMonumentBlockRadius = Mathf.Clamp(config.General.DefaultMonumentBlockRadius, 1f, 1000f);
            NormalizeBlockedMonumentNames();
            config.General.RecentCallHistoryLimit = Math.Min(200, Math.Max(0, config.General.RecentCallHistoryLimit));
            config.General.HeavyStrikeMapMarkerSize = Mathf.Clamp(config.General.HeavyStrikeMapMarkerSize <= 0f ? defaults.General.HeavyStrikeMapMarkerSize : config.General.HeavyStrikeMapMarkerSize, 2f, 75f);
            config.General.HeavyStrikeMapMarkerAlpha = Mathf.Clamp01(config.General.HeavyStrikeMapMarkerAlpha <= 0f ? defaults.General.HeavyStrikeMapMarkerAlpha : config.General.HeavyStrikeMapMarkerAlpha);
            config.General.NearbyHeavyStrikeWarningRadius = Mathf.Clamp(config.General.NearbyHeavyStrikeWarningRadius, 0f, 1000f);
            config.General.MaxSimultaneousStrikes = Math.Max(1, config.General.MaxSimultaneousStrikes);
            config.General.MaxSimultaneousHeavyStrikes = Math.Max(1, config.General.MaxSimultaneousHeavyStrikes);

            if (string.IsNullOrWhiteSpace(config.AirstrikeItem.Shortname))
            {
                config.AirstrikeItem.Shortname = defaults.AirstrikeItem.Shortname;
            }

            if (string.IsNullOrWhiteSpace(config.AirstrikeItem.DisplayName))
            {
                config.AirstrikeItem.DisplayName = defaults.AirstrikeItem.DisplayName;
            }

            if (string.IsNullOrWhiteSpace(config.AirstrikeItem.CustomShortname))
            {
                config.AirstrikeItem.CustomShortname = defaults.AirstrikeItem.CustomShortname;
            }

            if (config.AirstrikeItem.CustomItemId == 0)
            {
                config.AirstrikeItem.CustomItemId = defaults.AirstrikeItem.CustomItemId;
            }

            if (string.IsNullOrWhiteSpace(config.AirstrikeItem.ParentShortname))
            {
                config.AirstrikeItem.ParentShortname = defaults.AirstrikeItem.ParentShortname;
            }

            if (string.IsNullOrWhiteSpace(config.AirstrikeItem.DefaultDescription))
            {
                config.AirstrikeItem.DefaultDescription = defaults.AirstrikeItem.DefaultDescription;
            }

            if (config.AirstrikeItem.IconPngDataPath == null)
            {
                config.AirstrikeItem.IconPngDataPath = defaults.AirstrikeItem.IconPngDataPath;
            }

            if (previousConfigVersion < 21
                && string.Equals(config.AirstrikeItem.Shortname, "targeting.computer", StringComparison.OrdinalIgnoreCase)
                && string.Equals(config.AirstrikeItem.DisplayName, "Airstrike Authorization Key", StringComparison.OrdinalIgnoreCase)
                && config.AirstrikeItem.SkinId == 0UL)
            {
                config.AirstrikeItem.Shortname = defaults.AirstrikeItem.Shortname;
                config.AirstrikeItem.DisplayName = defaults.AirstrikeItem.DisplayName;
                config.AirstrikeItem.RequireCustomNameOrSkin = true;
                config.AirstrikeItem.TreatAsTargetingTool = true;
                config.AirstrikeItem.ShowEquipInstructions = true;
                config.AirstrikeItem.ToolTargetMarkerEnabled = true;
            }

            config.AirstrikeItem.RequiredAmount = Math.Max(1, config.AirstrikeItem.RequiredAmount);
            config.AirstrikeItem.MaxStackSize = ClampAirstrikeMaxStackSize(config.AirstrikeItem.MaxStackSize <= 0 ? defaults.AirstrikeItem.MaxStackSize : config.AirstrikeItem.MaxStackSize);
            config.AirstrikeItem.MaxChargesPerItem = ClampAirstrikeItemCharges(config.AirstrikeItem.MaxChargesPerItem <= 0 ? defaults.AirstrikeItem.MaxChargesPerItem : config.AirstrikeItem.MaxChargesPerItem);
            config.AirstrikeItem.ToolTargetMarkerDurationSeconds = Mathf.Clamp(config.AirstrikeItem.ToolTargetMarkerDurationSeconds <= 0f ? defaults.AirstrikeItem.ToolTargetMarkerDurationSeconds : config.AirstrikeItem.ToolTargetMarkerDurationSeconds, 3f, 60f);
            config.AirstrikeItem.ToolTargetMarkerSize = Mathf.Clamp(config.AirstrikeItem.ToolTargetMarkerSize <= 0f ? defaults.AirstrikeItem.ToolTargetMarkerSize : config.AirstrikeItem.ToolTargetMarkerSize, 2f, 50f);
            config.AirstrikeItem.ToolTargetMarkerAlpha = Mathf.Clamp01(config.AirstrikeItem.ToolTargetMarkerAlpha <= 0f ? defaults.AirstrikeItem.ToolTargetMarkerAlpha : config.AirstrikeItem.ToolTargetMarkerAlpha);

            if (string.IsNullOrWhiteSpace(config.Currency.Provider))
            {
                config.Currency.Provider = defaults.Currency.Provider;
            }

            config.Currency.VipDiscountsByPermission = NormalizeDiscounts(config.Currency.VipDiscountsByPermission);
            config.Selection.OpenMenuCommand = GetOpenCommand();

            config.DeliveryScaling.DroneMultiplier = Math.Max(1, config.DeliveryScaling.DroneMultiplier);
            config.DeliveryScaling.HeliMultiplier = Math.Max(1, config.DeliveryScaling.HeliMultiplier);
            config.DeliveryScaling.PlaneMultiplier = Math.Max(1, config.DeliveryScaling.PlaneMultiplier);
            if (previousConfigVersion > 0 && previousConfigVersion < 20)
            {
                config.DeliveryVisuals.SpawnFlyoverSoundEffects = true;
                config.DeliveryVisuals.SpawnRotorWashEffects = true;
            }
            if (previousConfigVersion > 0 && previousConfigVersion < 26)
            {
                config.AirstrikeItem.MaxChargesPerItem = defaults.AirstrikeItem.MaxChargesPerItem;
                config.DeliveryVisuals.SpawnRotorWashEffects = false;
            }
            if (previousConfigVersion > 0 && previousConfigVersion < 27)
            {
                config.AirstrikeItem.MaxStackSize = defaults.AirstrikeItem.MaxStackSize;
            }
            if (previousConfigVersion > 0 && previousConfigVersion < 29)
            {
                config.DeliveryVisuals.DroneFirstPayloadDelaySeconds = defaults.DeliveryVisuals.DroneFirstPayloadDelaySeconds;
                config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds = defaults.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds;
                config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds = defaults.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds;
                config.DeliveryVisuals.A10FirstPayloadDelaySeconds = defaults.DeliveryVisuals.A10FirstPayloadDelaySeconds;
                config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds = defaults.DeliveryVisuals.MlrsFirstPayloadDelaySeconds;
            }
            if (previousConfigVersion > 0 && previousConfigVersion < 30)
            {
                MigrateDefaultHeavyDropsToCargoPlane();
            }
            if (previousConfigVersion > 0 && previousConfigVersion < 32)
            {
                MigrateDefaultDeliveryVisualAnimationPolish(defaults.DeliveryVisuals);
            }
            if (previousConfigVersion > 0 && previousConfigVersion < 33)
            {
                MigrateDefaultDeliveryVisualBelievabilityPolish(defaults.DeliveryVisuals);
            }
            if (previousConfigVersion > 0 && previousConfigVersion < 34)
            {
                MigrateDefaultJetAnimationPacingPolish(defaults.DeliveryVisuals);
            }
            config.DeliveryVisuals.DroneFlyoverDistance = Mathf.Clamp(config.DeliveryVisuals.DroneFlyoverDistance <= 0f ? defaults.DeliveryVisuals.DroneFlyoverDistance : config.DeliveryVisuals.DroneFlyoverDistance, 15f, 150f);
            config.DeliveryVisuals.DroneFlyoverHeight = Mathf.Clamp(config.DeliveryVisuals.DroneFlyoverHeight <= 0f ? defaults.DeliveryVisuals.DroneFlyoverHeight : config.DeliveryVisuals.DroneFlyoverHeight, 8f, 80f);
            config.DeliveryVisuals.DroneErraticApproachRadius = Mathf.Clamp(config.DeliveryVisuals.DroneErraticApproachRadius <= 0f ? defaults.DeliveryVisuals.DroneErraticApproachRadius : config.DeliveryVisuals.DroneErraticApproachRadius, 0f, 20f);
            config.DeliveryVisuals.DroneDropLoiterRadius = Mathf.Clamp(config.DeliveryVisuals.DroneDropLoiterRadius <= 0f ? defaults.DeliveryVisuals.DroneDropLoiterRadius : config.DeliveryVisuals.DroneDropLoiterRadius, 0f, 30f);
            config.DeliveryVisuals.DronePayloadSpawnHeight = Mathf.Clamp(config.DeliveryVisuals.DronePayloadSpawnHeight <= 0f ? defaults.DeliveryVisuals.DronePayloadSpawnHeight : config.DeliveryVisuals.DronePayloadSpawnHeight, DroneDropMinimumSpawnHeight, 25f);
            config.DeliveryVisuals.DroneMinimumTerrainClearance = Mathf.Clamp(config.DeliveryVisuals.DroneMinimumTerrainClearance <= 0f ? defaults.DeliveryVisuals.DroneMinimumTerrainClearance : config.DeliveryVisuals.DroneMinimumTerrainClearance, 4f, 80f);
            config.DeliveryVisuals.AircraftMinimumTerrainClearance = Mathf.Clamp(config.DeliveryVisuals.AircraftMinimumTerrainClearance <= 0f ? defaults.DeliveryVisuals.AircraftMinimumTerrainClearance : config.DeliveryVisuals.AircraftMinimumTerrainClearance, 12f, 180f);
            config.DeliveryVisuals.PayloadMinimumTerrainClearance = Mathf.Clamp(config.DeliveryVisuals.PayloadMinimumTerrainClearance <= 0f ? defaults.DeliveryVisuals.PayloadMinimumTerrainClearance : config.DeliveryVisuals.PayloadMinimumTerrainClearance, 2f, 60f);
            config.DeliveryVisuals.AircraftFlyoverDistance = Mathf.Clamp(config.DeliveryVisuals.AircraftFlyoverDistance <= 0f ? defaults.DeliveryVisuals.AircraftFlyoverDistance : config.DeliveryVisuals.AircraftFlyoverDistance, 60f, 500f);
            config.DeliveryVisuals.AttackHeliFlyoverHeight = Mathf.Clamp(config.DeliveryVisuals.AttackHeliFlyoverHeight <= 0f ? defaults.DeliveryVisuals.AttackHeliFlyoverHeight : config.DeliveryVisuals.AttackHeliFlyoverHeight, 20f, 180f);
            config.DeliveryVisuals.CargoPlaneFlyoverHeight = Mathf.Clamp(config.DeliveryVisuals.CargoPlaneFlyoverHeight <= 0f ? defaults.DeliveryVisuals.CargoPlaneFlyoverHeight : config.DeliveryVisuals.CargoPlaneFlyoverHeight, 35f, 260f);
            config.DeliveryVisuals.MlrsAircraftFlyoverHeight = Mathf.Clamp(config.DeliveryVisuals.MlrsAircraftFlyoverHeight <= 0f ? defaults.DeliveryVisuals.MlrsAircraftFlyoverHeight : config.DeliveryVisuals.MlrsAircraftFlyoverHeight, 35f, 200f);
            config.DeliveryVisuals.A10FlyoverHeight = Mathf.Clamp(config.DeliveryVisuals.A10FlyoverHeight <= 0f ? defaults.DeliveryVisuals.A10FlyoverHeight : config.DeliveryVisuals.A10FlyoverHeight, 25f, 220f);
            config.DeliveryVisuals.AircraftObservationPassHeightMultiplier = Mathf.Clamp(config.DeliveryVisuals.AircraftObservationPassHeightMultiplier <= 0f ? defaults.DeliveryVisuals.AircraftObservationPassHeightMultiplier : config.DeliveryVisuals.AircraftObservationPassHeightMultiplier, 1f, 3f);
            config.DeliveryVisuals.AircraftStrikePassHeightMultiplier = Mathf.Clamp(config.DeliveryVisuals.AircraftStrikePassHeightMultiplier <= 0f ? defaults.DeliveryVisuals.AircraftStrikePassHeightMultiplier : config.DeliveryVisuals.AircraftStrikePassHeightMultiplier, 0.35f, 1.25f);
            config.DeliveryVisuals.AttackDiveStartHeightMultiplier = Mathf.Clamp(config.DeliveryVisuals.AttackDiveStartHeightMultiplier <= 0f ? defaults.DeliveryVisuals.AttackDiveStartHeightMultiplier : config.DeliveryVisuals.AttackDiveStartHeightMultiplier, 1f, 3f);
            config.DeliveryVisuals.AttackStrikePassHeightMultiplier = Mathf.Clamp(config.DeliveryVisuals.AttackStrikePassHeightMultiplier <= 0f ? defaults.DeliveryVisuals.AttackStrikePassHeightMultiplier : config.DeliveryVisuals.AttackStrikePassHeightMultiplier, 0.35f, 1.25f);
            config.DeliveryVisuals.AttackExitHeightMultiplier = Mathf.Clamp(config.DeliveryVisuals.AttackExitHeightMultiplier <= 0f ? defaults.DeliveryVisuals.AttackExitHeightMultiplier : config.DeliveryVisuals.AttackExitHeightMultiplier, 1f, 3f);
            config.DeliveryVisuals.MortarSourceDistance = Mathf.Clamp(config.DeliveryVisuals.MortarSourceDistance <= 0f ? defaults.DeliveryVisuals.MortarSourceDistance : config.DeliveryVisuals.MortarSourceDistance, 25f, 250f);
            config.DeliveryVisuals.MortarCrewOffset = Mathf.Clamp(config.DeliveryVisuals.MortarCrewOffset <= 0f ? defaults.DeliveryVisuals.MortarCrewOffset : config.DeliveryVisuals.MortarCrewOffset, 1f, 8f);
            if (previousConfigVersion > 0 && previousConfigVersion < 20
                && (Math.Abs(config.DeliveryVisuals.VisualMoveIntervalSeconds - 0.2f) <= 0.001f
                    || Math.Abs(config.DeliveryVisuals.VisualMoveIntervalSeconds - 0.1f) <= 0.001f))
            {
                config.DeliveryVisuals.VisualMoveIntervalSeconds = defaults.DeliveryVisuals.VisualMoveIntervalSeconds;
            }
            config.DeliveryVisuals.VisualMoveIntervalSeconds = Mathf.Clamp(config.DeliveryVisuals.VisualMoveIntervalSeconds <= 0f ? defaults.DeliveryVisuals.VisualMoveIntervalSeconds : config.DeliveryVisuals.VisualMoveIntervalSeconds, MinimumVisualMoveIntervalSeconds, MaximumVisualMoveIntervalSeconds);
            config.DeliveryVisuals.VisualRotationSmoothTimeSeconds = Mathf.Clamp(config.DeliveryVisuals.VisualRotationSmoothTimeSeconds <= 0f ? defaults.DeliveryVisuals.VisualRotationSmoothTimeSeconds : config.DeliveryVisuals.VisualRotationSmoothTimeSeconds, MinimumVisualRotationSmoothTimeSeconds, MaximumVisualRotationSmoothTimeSeconds);
            config.DeliveryVisuals.FlyoverSoundIntervalSeconds = Mathf.Clamp(config.DeliveryVisuals.FlyoverSoundIntervalSeconds <= 0f ? defaults.DeliveryVisuals.FlyoverSoundIntervalSeconds : config.DeliveryVisuals.FlyoverSoundIntervalSeconds, 0.25f, 3f);
            config.DeliveryVisuals.DestroyableDeliveryVehicleFirstPayloadDelaySeconds = Mathf.Clamp(config.DeliveryVisuals.DestroyableDeliveryVehicleFirstPayloadDelaySeconds < 0f ? defaults.DeliveryVisuals.DestroyableDeliveryVehicleFirstPayloadDelaySeconds : config.DeliveryVisuals.DestroyableDeliveryVehicleFirstPayloadDelaySeconds, 0f, 20f);
            config.DeliveryVisuals.DroneFirstPayloadDelaySeconds = Mathf.Clamp(config.DeliveryVisuals.DroneFirstPayloadDelaySeconds < 0f ? defaults.DeliveryVisuals.DroneFirstPayloadDelaySeconds : config.DeliveryVisuals.DroneFirstPayloadDelaySeconds, 0f, 20f);
            config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds = Mathf.Clamp(config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds < 0f ? defaults.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds : config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds, 0f, 20f);
            config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds = Mathf.Clamp(config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds < 0f ? defaults.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds : config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds, 0f, 20f);
            config.DeliveryVisuals.A10FirstPayloadDelaySeconds = Mathf.Clamp(config.DeliveryVisuals.A10FirstPayloadDelaySeconds < 0f ? defaults.DeliveryVisuals.A10FirstPayloadDelaySeconds : config.DeliveryVisuals.A10FirstPayloadDelaySeconds, 0f, 20f);
            config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds = Mathf.Clamp(config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds < 0f ? defaults.DeliveryVisuals.MlrsFirstPayloadDelaySeconds : config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds, 0f, 20f);
            config.DeliveryVisuals.DroneDeliveryVehicleHealth = Mathf.Clamp(config.DeliveryVisuals.DroneDeliveryVehicleHealth <= 0f ? defaults.DeliveryVisuals.DroneDeliveryVehicleHealth : config.DeliveryVisuals.DroneDeliveryVehicleHealth, 1f, 10000f);
            config.DeliveryVisuals.AttackHeliDeliveryVehicleHealth = Mathf.Clamp(config.DeliveryVisuals.AttackHeliDeliveryVehicleHealth <= 0f ? defaults.DeliveryVisuals.AttackHeliDeliveryVehicleHealth : config.DeliveryVisuals.AttackHeliDeliveryVehicleHealth, 1f, 10000f);
            config.DeliveryVisuals.CargoPlaneDeliveryVehicleHealth = Mathf.Clamp(config.DeliveryVisuals.CargoPlaneDeliveryVehicleHealth <= 0f ? defaults.DeliveryVisuals.CargoPlaneDeliveryVehicleHealth : config.DeliveryVisuals.CargoPlaneDeliveryVehicleHealth, 1f, 10000f);
            config.DeliveryVisuals.A10DeliveryVehicleHealth = Mathf.Clamp(config.DeliveryVisuals.A10DeliveryVehicleHealth <= 0f ? defaults.DeliveryVisuals.A10DeliveryVehicleHealth : config.DeliveryVisuals.A10DeliveryVehicleHealth, 1f, 10000f);

            if (config.LootDistribution.ContainerRules == null)
            {
                config.LootDistribution.ContainerRules = defaults.LootDistribution.ContainerRules;
            }

            NormalizeLootRules();
            NormalizeAuditWebhookSettings(defaults.AuditWebhooks);
            NormalizeStrikeDefinitions(defaults.StrikeDefinitions);

            if (wasBeforeExperimentalDefaultReset)
            {
                DisableExperimentalStrikesFromOldDefaults();
            }

            config.ConfigVersion = CurrentConfigVersion;
        }

        private void MigrateDefaultJetAnimationPacingPolish(DeliveryVisualSettings defaults)
        {
            if (config?.DeliveryVisuals == null || defaults == null)
            {
                return;
            }

            if (Math.Abs(config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds - 12.5f) <= 0.001f)
            {
                config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds = defaults.CargoPlaneFirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.A10FirstPayloadDelaySeconds - 11.5f) <= 0.001f)
            {
                config.DeliveryVisuals.A10FirstPayloadDelaySeconds = defaults.A10FirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds - 14.5f) <= 0.001f)
            {
                config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds = defaults.MlrsFirstPayloadDelaySeconds;
            }

            if (config.DeliveryVisuals.VisualRotationSmoothTimeSeconds <= 0f)
            {
                config.DeliveryVisuals.VisualRotationSmoothTimeSeconds = defaults.VisualRotationSmoothTimeSeconds;
            }
        }

        private void MigrateDefaultDeliveryVisualBelievabilityPolish(DeliveryVisualSettings defaults)
        {
            if (config?.DeliveryVisuals == null || defaults == null)
            {
                return;
            }

            if (Math.Abs(config.DeliveryVisuals.DroneFlyoverDistance - 55f) <= 0.001f)
            {
                config.DeliveryVisuals.DroneFlyoverDistance = defaults.DroneFlyoverDistance;
            }
            if (Math.Abs(config.DeliveryVisuals.DroneFlyoverHeight - 22f) <= 0.001f)
            {
                config.DeliveryVisuals.DroneFlyoverHeight = defaults.DroneFlyoverHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.DroneErraticApproachRadius - 4.5f) <= 0.001f)
            {
                config.DeliveryVisuals.DroneErraticApproachRadius = defaults.DroneErraticApproachRadius;
            }
            if (Math.Abs(config.DeliveryVisuals.DroneDropLoiterRadius - 7f) <= 0.001f)
            {
                config.DeliveryVisuals.DroneDropLoiterRadius = defaults.DroneDropLoiterRadius;
            }
            if (Math.Abs(config.DeliveryVisuals.DronePayloadSpawnHeight - DroneDropSpawnHeight) <= 0.001f)
            {
                config.DeliveryVisuals.DronePayloadSpawnHeight = defaults.DronePayloadSpawnHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.AircraftFlyoverDistance - 260f) <= 0.001f)
            {
                config.DeliveryVisuals.AircraftFlyoverDistance = defaults.AircraftFlyoverDistance;
            }
            if (Math.Abs(config.DeliveryVisuals.AttackHeliFlyoverHeight - 60f) <= 0.001f)
            {
                config.DeliveryVisuals.AttackHeliFlyoverHeight = defaults.AttackHeliFlyoverHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.CargoPlaneFlyoverHeight - 110f) <= 0.001f)
            {
                config.DeliveryVisuals.CargoPlaneFlyoverHeight = defaults.CargoPlaneFlyoverHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.MlrsAircraftFlyoverHeight - 78f) <= 0.001f)
            {
                config.DeliveryVisuals.MlrsAircraftFlyoverHeight = defaults.MlrsAircraftFlyoverHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.A10FlyoverHeight - 84f) <= 0.001f)
            {
                config.DeliveryVisuals.A10FlyoverHeight = defaults.A10FlyoverHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.AircraftObservationPassHeightMultiplier - 1.65f) <= 0.001f)
            {
                config.DeliveryVisuals.AircraftObservationPassHeightMultiplier = defaults.AircraftObservationPassHeightMultiplier;
            }
            if (Math.Abs(config.DeliveryVisuals.AircraftStrikePassHeightMultiplier - 0.66f) <= 0.001f)
            {
                config.DeliveryVisuals.AircraftStrikePassHeightMultiplier = defaults.AircraftStrikePassHeightMultiplier;
            }
            if (Math.Abs(config.DeliveryVisuals.AttackDiveStartHeightMultiplier - 1.75f) <= 0.001f)
            {
                config.DeliveryVisuals.AttackDiveStartHeightMultiplier = defaults.AttackDiveStartHeightMultiplier;
            }
            if (Math.Abs(config.DeliveryVisuals.AttackStrikePassHeightMultiplier - 0.72f) <= 0.001f)
            {
                config.DeliveryVisuals.AttackStrikePassHeightMultiplier = defaults.AttackStrikePassHeightMultiplier;
            }
            if (Math.Abs(config.DeliveryVisuals.AttackExitHeightMultiplier - 1.45f) <= 0.001f)
            {
                config.DeliveryVisuals.AttackExitHeightMultiplier = defaults.AttackExitHeightMultiplier;
            }
            if (Math.Abs(config.DeliveryVisuals.DroneFirstPayloadDelaySeconds - 2.5f) <= 0.001f)
            {
                config.DeliveryVisuals.DroneFirstPayloadDelaySeconds = defaults.DroneFirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds - 8f) <= 0.001f)
            {
                config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds = defaults.AttackHeliFirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds - 11f) <= 0.001f)
            {
                config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds = defaults.CargoPlaneFirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.A10FirstPayloadDelaySeconds - 9f) <= 0.001f)
            {
                config.DeliveryVisuals.A10FirstPayloadDelaySeconds = defaults.A10FirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds - 13f) <= 0.001f)
            {
                config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds = defaults.MlrsFirstPayloadDelaySeconds;
            }
        }

        private void MigrateDefaultDeliveryVisualAnimationPolish(DeliveryVisualSettings defaults)
        {
            if (config?.DeliveryVisuals == null || defaults == null)
            {
                return;
            }

            if (Math.Abs(config.DeliveryVisuals.DroneFlyoverDistance - 45f) <= 0.001f)
            {
                config.DeliveryVisuals.DroneFlyoverDistance = defaults.DroneFlyoverDistance;
            }
            if (Math.Abs(config.DeliveryVisuals.AircraftFlyoverDistance - 175f) <= 0.001f)
            {
                config.DeliveryVisuals.AircraftFlyoverDistance = defaults.AircraftFlyoverDistance;
            }
            if (Math.Abs(config.DeliveryVisuals.AttackHeliFlyoverHeight - 48f) <= 0.001f)
            {
                config.DeliveryVisuals.AttackHeliFlyoverHeight = defaults.AttackHeliFlyoverHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.CargoPlaneFlyoverHeight - 92f) <= 0.001f)
            {
                config.DeliveryVisuals.CargoPlaneFlyoverHeight = defaults.CargoPlaneFlyoverHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.MlrsAircraftFlyoverHeight - 58f) <= 0.001f)
            {
                config.DeliveryVisuals.MlrsAircraftFlyoverHeight = defaults.MlrsAircraftFlyoverHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.A10FlyoverHeight - 68f) <= 0.001f)
            {
                config.DeliveryVisuals.A10FlyoverHeight = defaults.A10FlyoverHeight;
            }
            if (Math.Abs(config.DeliveryVisuals.DroneFirstPayloadDelaySeconds - 1.5f) <= 0.001f)
            {
                config.DeliveryVisuals.DroneFirstPayloadDelaySeconds = defaults.DroneFirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds - 7f) <= 0.001f)
            {
                config.DeliveryVisuals.AttackHeliFirstPayloadDelaySeconds = defaults.AttackHeliFirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds - 9f) <= 0.001f)
            {
                config.DeliveryVisuals.CargoPlaneFirstPayloadDelaySeconds = defaults.CargoPlaneFirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.A10FirstPayloadDelaySeconds - 8f) <= 0.001f)
            {
                config.DeliveryVisuals.A10FirstPayloadDelaySeconds = defaults.A10FirstPayloadDelaySeconds;
            }
            if (Math.Abs(config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds - 12f) <= 0.001f)
            {
                config.DeliveryVisuals.MlrsFirstPayloadDelaySeconds = defaults.MlrsFirstPayloadDelaySeconds;
            }
        }

        private void MigrateDefaultHeavyDropsToCargoPlane()
        {
            MigrateStrikeDeliveryIfMatches("bee_swarm_heavy", "bee_catapult_bomb", "attack_heli", "cargo_plane_jet");
            MigrateStrikeDeliveryIfMatches("firebomb_run", "firebomb", "attack_heli", "cargo_plane_jet");
            MigrateStrikeDeliveryIfMatches("propane_bomb_drop", "propane_bomb", "attack_heli", "cargo_plane_jet");
        }

        private void MigrateStrikeDeliveryIfMatches(string strikeId, string payload, string oldDelivery, string newDelivery)
        {
            if (config?.StrikeDefinitions == null || string.IsNullOrWhiteSpace(strikeId))
            {
                return;
            }

            StrikeDefinition strike;
            if (!config.StrikeDefinitions.TryGetValue(strikeId, out strike) || strike == null)
            {
                return;
            }

            if (string.Equals(strike.Payload, payload, StringComparison.OrdinalIgnoreCase)
                && string.Equals(strike.Delivery, oldDelivery, StringComparison.OrdinalIgnoreCase))
            {
                strike.Delivery = newDelivery;
            }
        }

        private static List<string> DefaultBlockedMonumentNames()
        {
            return new List<string>
            {
                "airfield_1",
                "bandit_town",
                "compound",
                "excavator_1",
                "gas_station_1",
                "harbor_1",
                "harbor_2",
                "junkyard_1",
                "launch_site_1",
                "lighthouse",
                "military_tunnel_1",
                "OilrigAI",
                "OilrigAI2",
                "powerplant_1",
                "radtown_small_3",
                "satellite_dish",
                "sphere_tank",
                "supermarket_1",
                "trainyard_1",
                "warehouse",
                "water_treatment_plant_1"
            };
        }

        private void NormalizeBlockedMonumentNames()
        {
            if (config.General.BlockedMonumentNames == null)
            {
                config.General.BlockedMonumentNames = DefaultBlockedMonumentNames();
            }

            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in config.General.BlockedMonumentNames)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                var trimmed = entry.Trim();
                var key = NormalizeMonumentName(trimmed);
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                {
                    continue;
                }

                normalized.Add(trimmed);
            }

            config.General.BlockedMonumentNames = normalized;
        }

        private void DisableExperimentalStrikesFromOldDefaults()
        {
            DisableStrikeIfConfigured("homing_heli");
            DisableStrikeIfConfigured("homing_jet");
            DisableStrikeIfConfigured("mini_mlrs");
            DisableStrikeIfConfigured("full_mlrs");
        }

        private void DisableStrikeIfConfigured(string strikeId)
        {
            if (config?.StrikeDefinitions == null || string.IsNullOrWhiteSpace(strikeId))
            {
                return;
            }

            StrikeDefinition strike;
            if (config.StrikeDefinitions.TryGetValue(strikeId, out strike) && strike != null)
            {
                strike.Enabled = false;
            }
        }

        private Dictionary<string, float> NormalizeDiscounts(Dictionary<string, float> discounts)
        {
            var normalized = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (discounts == null)
            {
                return normalized;
            }

            foreach (var entry in discounts)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                normalized[entry.Key.Trim().ToLowerInvariant()] = Mathf.Clamp(entry.Value, 0f, 0.95f);
            }

            return normalized;
        }

        private void NormalizeLootRules()
        {
            var normalized = new Dictionary<string, LootContainerRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in config.LootDistribution.ContainerRules)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
                {
                    continue;
                }

                entry.Value.Chance = Mathf.Clamp01(entry.Value.Chance);
                entry.Value.MinAmount = Math.Max(1, entry.Value.MinAmount);
                entry.Value.MaxAmount = Math.Max(entry.Value.MinAmount, entry.Value.MaxAmount);
                normalized[entry.Key.Trim()] = entry.Value;
            }

            config.LootDistribution.ContainerRules = normalized;
        }

        private void NormalizeAuditWebhookSettings(AuditWebhookSettings defaults)
        {
            if (config.AuditWebhooks == null)
            {
                config.AuditWebhooks = defaults ?? new AuditWebhookSettings();
            }

            if (string.IsNullOrWhiteSpace(config.AuditWebhooks.Username))
            {
                config.AuditWebhooks.Username = defaults?.Username ?? "Portable Airstrikes";
            }

            config.AuditWebhooks.DiscordWebhookUrl = config.AuditWebhooks.DiscordWebhookUrl?.Trim() ?? "";
            config.AuditWebhooks.AvatarUrl = config.AuditWebhooks.AvatarUrl?.Trim() ?? "";
            config.AuditWebhooks.MentionText = config.AuditWebhooks.MentionText?.Trim() ?? "";
        }

        private void NormalizeStrikeDefinitions(Dictionary<string, StrikeDefinition> defaults)
        {
            var normalized = new Dictionary<string, StrikeDefinition>(StringComparer.OrdinalIgnoreCase);
            if (config.StrikeDefinitions != null)
            {
                foreach (var entry in config.StrikeDefinitions)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
                    {
                        continue;
                    }

                    normalized[entry.Key.Trim()] = entry.Value;
                }
            }

            foreach (var entry in defaults)
            {
                if (!normalized.ContainsKey(entry.Key))
                {
                    normalized[entry.Key] = entry.Value;
                }
            }

            foreach (var entry in normalized)
            {
                NormalizeStrike(entry.Key, entry.Value, defaults.ContainsKey(entry.Key) ? defaults[entry.Key] : null);
            }

            config.StrikeDefinitions = normalized;
        }

        private void NormalizeStrike(string id, StrikeDefinition strike, StrikeDefinition fallback)
        {
            strike.Id = id;

            if (string.IsNullOrWhiteSpace(strike.DisplayName))
            {
                strike.DisplayName = fallback?.DisplayName ?? id;
            }

            NormalizeStrikeAcceptedTargetTypes(strike, fallback);

            if (string.IsNullOrWhiteSpace(strike.Delivery))
            {
                strike.Delivery = fallback?.Delivery ?? "drone";
            }

            if (string.IsNullOrWhiteSpace(strike.Payload))
            {
                strike.Payload = fallback?.Payload ?? id;
            }

            strike.VisualProfileId = strike.VisualProfileId == null ? "" : strike.VisualProfileId.Trim();
            NormalizeStrikeProfileAssignments(strike);

            if (!IsStrikeExecutorCompatible(strike))
            {
                PrintWarning("Strike '" + id + "' has an unsupported delivery/payload combination after config normalization: " + GetStrikeCompatibilityMessage(strike));
            }

            strike.Tier = Mathf.Clamp(strike.Tier, 1, 5);
            strike.RPCost = Math.Max(0, strike.RPCost);
            strike.WarningDelaySeconds = strike.WarningDelaySeconds <= 0f ? config.General.DefaultWarningDelaySeconds : Mathf.Clamp(strike.WarningDelaySeconds, 0f, 120f);
            strike.CooldownPerPlayerSeconds = Mathf.Clamp(strike.CooldownPerPlayerSeconds, 0f, 86400f);
            strike.CooldownPerClanSeconds = Mathf.Clamp(strike.CooldownPerClanSeconds, 0f, 86400f);
            strike.GlobalCooldownSeconds = Mathf.Clamp(strike.GlobalCooldownSeconds, 0f, 86400f);
            strike.BaseCount = Math.Max(1, strike.BaseCount);
            strike.MaxCount = Math.Max(strike.BaseCount, strike.MaxCount);
            strike.SpreadRadius = Mathf.Clamp(strike.SpreadRadius, 0f, 250f);
            strike.SpreadMultiplier = NormalizePositiveMultiplier(strike.SpreadMultiplier);
            strike.BurstCount = Mathf.Clamp(strike.BurstCount, 0, 80);
            strike.LineLength = Mathf.Clamp(strike.LineLength, 0f, 200f);
            strike.LineLengthMultiplier = NormalizePositiveMultiplier(strike.LineLengthMultiplier);
            strike.Width = Mathf.Clamp(strike.Width, 0f, 50f);
            strike.WidthMultiplier = NormalizePositiveMultiplier(strike.WidthMultiplier);
            strike.ImpactRadius = Mathf.Clamp(strike.ImpactRadius, 0f, 25f);
            strike.ImpactRadiusMultiplier = NormalizePositiveMultiplier(strike.ImpactRadiusMultiplier);
            strike.PulseDelaySeconds = Mathf.Clamp(strike.PulseDelaySeconds, 0f, 2f);
            strike.PulseDelayMultiplier = NormalizePositiveMultiplier(strike.PulseDelayMultiplier);
            strike.MissileCount = Mathf.Clamp(strike.MissileCount, 0, 12);
            strike.RocketCount = Mathf.Clamp(strike.RocketCount, 0, 48);
            strike.MaxTrackingSeconds = Mathf.Clamp(strike.MaxTrackingSeconds, 0f, 60f);
            strike.TrackingSecondsMultiplier = NormalizePositiveMultiplier(strike.TrackingSecondsMultiplier);
            strike.MaxTrackingDistance = Mathf.Clamp(strike.MaxTrackingDistance, 0f, 1000f);
            strike.TrackingDistanceMultiplier = NormalizePositiveMultiplier(strike.TrackingDistanceMultiplier);
            strike.VehicleDamageScale = Mathf.Clamp(strike.VehicleDamageScale, 0f, 10f);
            strike.DamageMultiplier = NormalizePositiveMultiplier(strike.DamageMultiplier);
            strike.VehicleDamageMultiplier = NormalizePositiveMultiplier(strike.VehicleDamageMultiplier);
            strike.SplashRadius = Mathf.Clamp(strike.SplashRadius, 0f, 50f);
            strike.SplashRadiusMultiplier = NormalizePositiveMultiplier(strike.SplashRadiusMultiplier);

            if (string.IsNullOrWhiteSpace(strike.PermissionRequired))
            {
                strike.PermissionRequired = fallback?.PermissionRequired ?? "";
            }

            if (strike.DamageScales == null)
            {
                strike.DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void NormalizeStrikeAcceptedTargetTypes(StrikeDefinition strike, StrikeDefinition fallback)
        {
            var normalized = new List<string>();
            var seen = new HashSet<AirstrikeTargetType>();

            if (strike.AcceptedTargetTypes != null)
            {
                foreach (var entry in strike.AcceptedTargetTypes)
                {
                    var parsed = ParseTargetType(entry);
                    if (parsed != AirstrikeTargetType.Invalid && seen.Add(parsed))
                    {
                        normalized.Add(NormalizeTargetTypeName(entry));
                    }
                }
            }

            var legacy = ParseTargetType(strike.TargetType);
            if (legacy == AirstrikeTargetType.Invalid && fallback != null)
            {
                legacy = ParseTargetType(fallback.TargetType);
            }

            if (legacy == AirstrikeTargetType.Invalid)
            {
                legacy = AirstrikeTargetType.GroundPing;
            }

            if (seen.Add(legacy))
            {
                normalized.Insert(0, NormalizeTargetTypeName(FormatTargetType(legacy)));
            }

            if (normalized.Count == 0)
            {
                normalized.Add("ground_ping");
            }

            strike.AcceptedTargetTypes = normalized;
            strike.TargetType = normalized[0];
        }

        private void NormalizeStrikeProfileAssignments(StrikeDefinition strike)
        {
            var normalized = new List<StrikeProfileAssignment>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (strike.StrikeProfiles != null)
            {
                foreach (var assignment in strike.StrikeProfiles)
                {
                    if (assignment == null || string.IsNullOrWhiteSpace(assignment.ProfileId))
                    {
                        continue;
                    }

                    var profileId = assignment.ProfileId.Trim();
                    if (!seen.Add(profileId))
                    {
                        continue;
                    }

                    normalized.Add(new StrikeProfileAssignment
                    {
                        ProfileId = profileId,
                        StartDelaySeconds = Mathf.Clamp(assignment.StartDelaySeconds, 0f, 120f),
                        Enabled = assignment.Enabled,
                        PayloadCountLimit = Mathf.Clamp(assignment.PayloadCountLimit, 0, 200)
                    });
                }
            }

            if (normalized.Count == 0 && !string.IsNullOrWhiteSpace(strike.VisualProfileId) && seen.Add(strike.VisualProfileId.Trim()))
            {
                normalized.Add(new StrikeProfileAssignment
                {
                    ProfileId = strike.VisualProfileId.Trim(),
                    StartDelaySeconds = 0f,
                    Enabled = true,
                    PayloadCountLimit = 0
                });
            }

            strike.StrikeProfiles = normalized;
        }

        private float NormalizePositiveMultiplier(float value)
        {
            return Mathf.Clamp(value <= 0f ? 1f : value, 0.01f, 100f);
        }

        private static Dictionary<string, StrikeDefinition> DefaultStrikeDefinitions()
        {
            var definitions = new Dictionary<string, StrikeDefinition>(StringComparer.OrdinalIgnoreCase);

            definitions["bee_swarm_drone"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Bee Swarm Drone",
                TargetType = "ground_ping",
                Delivery = "drone",
                Payload = "bee_grenade",
                Tier = 1,
                RPCost = 50,
                PermissionRequired = "portableairstrikes.use.bee",
                WarningDelaySeconds = 6f,
                CooldownPerPlayerSeconds = 120f,
                CooldownPerClanSeconds = 180f,
                BaseCount = 6,
                MaxCount = 8,
                SpreadRadius = 8f
            };

            definitions["bee_swarm_heavy"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Heavy Bee Swarm",
                TargetType = "ground_ping",
                Delivery = "cargo_plane_jet",
                Payload = "bee_catapult_bomb",
                Tier = 2,
                RPCost = 175,
                PermissionRequired = "portableairstrikes.use.bee",
                WarningDelaySeconds = 8f,
                CooldownPerPlayerSeconds = 240f,
                CooldownPerClanSeconds = 360f,
                BaseCount = 6,
                MaxCount = 18,
                SpreadRadius = 16f
            };

            definitions["beancan_drop"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Beancan Drop",
                TargetType = "ground_ping",
                Delivery = "drone",
                Payload = "beancan",
                Tier = 1,
                RPCost = 75,
                PermissionRequired = "portableairstrikes.use.grenade",
                WarningDelaySeconds = 6f,
                CooldownPerPlayerSeconds = 120f,
                CooldownPerClanSeconds = 180f,
                BaseCount = 4,
                MaxCount = 8,
                SpreadRadius = 7f
            };

            definitions["f1_cluster"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "F1 Cluster Drop",
                TargetType = "ground_ping",
                Delivery = "drone",
                Payload = "f1_grenade",
                Tier = 2,
                RPCost = 125,
                PermissionRequired = "portableairstrikes.use.grenade",
                WarningDelaySeconds = 6f,
                CooldownPerPlayerSeconds = 150f,
                CooldownPerClanSeconds = 240f,
                BaseCount = 5,
                MaxCount = 10,
                SpreadRadius = 9f
            };

            definitions["smoke_screen"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Smoke Screen Drop",
                TargetType = "ground_ping",
                Delivery = "drone",
                Payload = "smoke",
                Tier = 1,
                RPCost = 50,
                PermissionRequired = "portableairstrikes.use.utility",
                WarningDelaySeconds = 4f,
                CooldownPerPlayerSeconds = 90f,
                CooldownPerClanSeconds = 120f,
                BaseCount = 5,
                MaxCount = 12,
                SpreadRadius = 12f
            };

            definitions["flash_breach"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Flash Breach Drop",
                TargetType = "ground_ping",
                Delivery = "drone",
                Payload = "flashbang",
                Tier = 1,
                RPCost = 100,
                PermissionRequired = "portableairstrikes.use.utility",
                WarningDelaySeconds = 4f,
                CooldownPerPlayerSeconds = 120f,
                CooldownPerClanSeconds = 180f,
                BaseCount = 3,
                MaxCount = 6,
                SpreadRadius = 6f
            };

            definitions["he_40mm_micro"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "40mm HE Micro-Strike",
                TargetType = "ground_ping",
                Delivery = "drone",
                Payload = "he_40mm",
                Tier = 2,
                RPCost = 250,
                PermissionRequired = "portableairstrikes.use.40mm",
                WarningDelaySeconds = 7f,
                CooldownPerPlayerSeconds = 240f,
                CooldownPerClanSeconds = 360f,
                BaseCount = 3,
                MaxCount = 6,
                SpreadRadius = 5f
            };

            definitions["molotov_drop"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Molotov Drop",
                TargetType = "ground_ping",
                Delivery = "drone",
                Payload = "molotov",
                Tier = 1,
                RPCost = 125,
                PermissionRequired = "portableairstrikes.use.fire",
                WarningDelaySeconds = 6f,
                CooldownPerPlayerSeconds = 150f,
                CooldownPerClanSeconds = 240f,
                BaseCount = 3,
                MaxCount = 6,
                SpreadRadius = 7f
            };

            definitions["firebomb_run"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Firebomb Run",
                TargetType = "ground_ping",
                Delivery = "cargo_plane_jet",
                Payload = "firebomb",
                Tier = 3,
                RPCost = 350,
                PermissionRequired = "portableairstrikes.use.fire",
                WarningDelaySeconds = 9f,
                CooldownPerPlayerSeconds = 360f,
                CooldownPerClanSeconds = 540f,
                BaseCount = 4,
                MaxCount = 12,
                SpreadRadius = 18f
            };

            definitions["propane_bomb_drop"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Propane Bomb Drop",
                TargetType = "ground_ping",
                Delivery = "cargo_plane_jet",
                Payload = "propane_bomb",
                Tier = 3,
                RPCost = 700,
                PermissionRequired = "portableairstrikes.use.propane",
                WarningDelaySeconds = 10f,
                CooldownPerPlayerSeconds = 600f,
                CooldownPerClanSeconds = 900f,
                GlobalCooldownSeconds = 120f,
                BaseCount = 3,
                MaxCount = 8,
                SpreadRadius = 16f
            };

            definitions["hv_rocket_run"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "HV Rocket Run",
                TargetType = "ground_ping",
                Delivery = "attack_heli",
                Payload = "hv_rocket",
                Tier = 3,
                RPCost = 500,
                PermissionRequired = "portableairstrikes.use.rocket",
                WarningDelaySeconds = 9f,
                CooldownPerPlayerSeconds = 480f,
                CooldownPerClanSeconds = 720f,
                GlobalCooldownSeconds = 90f,
                RocketCount = 4,
                BaseCount = 4,
                MaxCount = 6,
                SpreadRadius = 8f
            };

            definitions["rocket_run"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Rocket Run",
                TargetType = "ground_ping",
                Delivery = "attack_heli",
                Payload = "rocket",
                Tier = 3,
                RPCost = 800,
                PermissionRequired = "portableairstrikes.use.rocket",
                WarningDelaySeconds = 10f,
                CooldownPerPlayerSeconds = 600f,
                CooldownPerClanSeconds = 900f,
                GlobalCooldownSeconds = 120f,
                RocketCount = 4,
                BaseCount = 4,
                MaxCount = 6,
                SpreadRadius = 10f
            };

            definitions["incendiary_rocket_run"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Incendiary Rocket Run",
                TargetType = "ground_ping",
                Delivery = "attack_heli",
                Payload = "incendiary_rocket",
                Tier = 3,
                RPCost = 700,
                PermissionRequired = "portableairstrikes.use.rocket",
                WarningDelaySeconds = 10f,
                CooldownPerPlayerSeconds = 600f,
                CooldownPerClanSeconds = 900f,
                GlobalCooldownSeconds = 120f,
                RocketCount = 4,
                BaseCount = 4,
                MaxCount = 6,
                SpreadRadius = 12f
            };

            definitions["mortar_he"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Mortar HE Mission",
                TargetType = "ground_ping",
                Delivery = "off_map_mortar",
                Payload = "mortar_he_payload",
                Tier = 2,
                RPCost = 300,
                PermissionRequired = "portableairstrikes.use.mortar",
                WarningDelaySeconds = 8f,
                CooldownPerPlayerSeconds = 300f,
                CooldownPerClanSeconds = 480f,
                BaseCount = 6,
                MaxCount = 10,
                SpreadRadius = 24f
            };

            definitions["mortar_frag"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "Mortar Frag Mission",
                TargetType = "ground_ping",
                Delivery = "off_map_mortar",
                Payload = "mortar_frag_payload",
                Tier = 2,
                RPCost = 250,
                PermissionRequired = "portableairstrikes.use.mortar",
                WarningDelaySeconds = 8f,
                CooldownPerPlayerSeconds = 300f,
                CooldownPerClanSeconds = 480f,
                BaseCount = 8,
                MaxCount = 12,
                SpreadRadius = 28f
            };

            definitions["a10_strafe"] = new StrikeDefinition
            {
                Enabled = true,
                DisplayName = "A-10 BRRRRT Run",
                TargetType = "ground_ping",
                Delivery = "a10_gun_run",
                Payload = "bradley_longbarrel_burst",
                Tier = 3,
                RPCost = 1000,
                PermissionRequired = "portableairstrikes.use.a10",
                WarningDelaySeconds = 10f,
                CooldownPerPlayerSeconds = 600f,
                CooldownPerClanSeconds = 900f,
                GlobalCooldownSeconds = 180f,
                BaseCount = 1,
                MaxCount = 1,
                BurstCount = 24,
                LineLength = 55f,
                Width = 7f,
                ImpactRadius = 2.5f,
                PulseDelaySeconds = 0.06f,
                DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Players"] = 1f,
                    ["Buildings"] = 0.35f,
                    ["Vehicles"] = 1.25f,
                    ["Deployables"] = 1f,
                    ["Turrets"] = 1f
                }
            };

            definitions["homing_heli"] = new StrikeDefinition
            {
                Enabled = false,
                DisplayName = "Heli Homing Strike",
                TargetType = "vehicle_ping",
                Delivery = "attack_heli",
                Payload = "homing_missile",
                Tier = 3,
                RPCost = 800,
                PermissionRequired = "portableairstrikes.use.homing.heli",
                WarningDelaySeconds = 8f,
                CooldownPerPlayerSeconds = 720f,
                CooldownPerClanSeconds = 900f,
                GlobalCooldownSeconds = 180f,
                MissileCount = 2,
                MaxTrackingSeconds = 10f,
                MaxTrackingDistance = 300f,
                VehicleDamageScale = 1.25f,
                SplashRadius = 4f,
                BaseCount = 1,
                MaxCount = 2
            };

            definitions["homing_jet"] = new StrikeDefinition
            {
                Enabled = false,
                DisplayName = "Jet Homing Strike",
                TargetType = "vehicle_ping",
                Delivery = "cargo_plane_jet",
                Payload = "homing_missile",
                Tier = 4,
                RPCost = 1500,
                PermissionRequired = "portableairstrikes.use.homing.jet",
                WarningDelaySeconds = 8f,
                CooldownPerPlayerSeconds = 900f,
                CooldownPerClanSeconds = 1200f,
                GlobalCooldownSeconds = 300f,
                MissileCount = 3,
                MaxTrackingSeconds = 12f,
                MaxTrackingDistance = 350f,
                VehicleDamageScale = 1.5f,
                SplashRadius = 5f,
                BaseCount = 1,
                MaxCount = 3
            };

            definitions["mini_mlrs"] = new StrikeDefinition
            {
                Enabled = false,
                DisplayName = "Mini MLRS Barrage",
                TargetType = "ground_ping",
                Delivery = "cargo_plane_jet",
                Payload = "mlrs_rocket",
                Tier = 4,
                RPCost = 2000,
                PermissionRequired = "portableairstrikes.use.mlrs.mini",
                WarningDelaySeconds = 15f,
                CooldownPerPlayerSeconds = 1200f,
                CooldownPerClanSeconds = 1800f,
                GlobalCooldownSeconds = 600f,
                RocketCount = 6,
                BaseCount = 6,
                MaxCount = 8,
                SpreadRadius = 35f
            };

            definitions["full_mlrs"] = new StrikeDefinition
            {
                Enabled = false,
                DisplayName = "Full MLRS Barrage",
                TargetType = "ground_ping",
                Delivery = "cargo_plane_jet",
                Payload = "mlrs_rocket",
                Tier = 5,
                RPCost = 3500,
                PermissionRequired = "portableairstrikes.use.mlrs.full",
                WarningDelaySeconds = 20f,
                CooldownPerPlayerSeconds = 1800f,
                CooldownPerClanSeconds = 2400f,
                GlobalCooldownSeconds = 1200f,
                RocketCount = 12,
                BaseCount = 12,
                MaxCount = 16,
                SpreadRadius = 55f
            };

            foreach (var entry in definitions)
            {
                entry.Value.Id = entry.Key;
            }

            return definitions;
        }
    }
}
