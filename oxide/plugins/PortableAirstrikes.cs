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
    [Info("PortableAirstrikes", "Raidlands", "0.1.39")]
    [Description("Configurable single-use CID binocular airstrike selection, automatic targeting pings, persisted manual default strikes, validation, terrain-aware, more believable multi-phase visual delivery flyovers with autoload-safe repeated sound cues, direct-command execution, audit logging, webhooks, warning markers, in-game warnings, and warning diagnostics.")]
    public class PortableAirstrikes : RustPlugin
    {
        private const int CurrentConfigVersion = 34;
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
        private const string StrikeUiName = "PortableAirstrikes.Selection";
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
        private const float DefaultFlyoverSoundIntervalSeconds = 0.75f;
        private const float DefaultVisualRotationSmoothTimeSeconds = 0.18f;
        private const float MinimumVisualRotationSmoothTimeSeconds = 0.02f;
        private const float MaximumVisualRotationSmoothTimeSeconds = 0.75f;
        private const float FlightPlanTangentSampleSeconds = 0.18f;
        private const float DefaultDroneMinimumTerrainClearance = 10f;
        private const float DefaultAircraftMinimumTerrainClearance = 42f;
        private const float DefaultPayloadMinimumTerrainClearance = 8f;
        private const float FlightPlanTerrainSampleSpacing = 28f;

        private static readonly int TargetRaycastLayer = LayerMask.GetMask(
            "Terrain",
            "World",
            "Construction",
            "Deployed",
            "Default",
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

        private Configuration config;
        private StoredData storedData;
        private ItemDefinition airstrikeCustomItemDefinition;
        private uint airstrikeIconFileId;
        private string airstrikeIconSource = "";
        private bool warnedCIDUnavailable;
        private bool warnedIconMissing;
        private readonly Dictionary<ulong, AirstrikeTarget> latestTargets = new Dictionary<ulong, AirstrikeTarget>();
        private readonly Dictionary<ulong, AirstrikeCallContext> activeCalls = new Dictionary<ulong, AirstrikeCallContext>();
        private readonly Dictionary<ulong, double> lastToolPingAt = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, string> lastProcessedToolPingKeyByUser = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, double> lastProcessedToolPingAtByUser = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, double> lastToolHelpAt = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, MapMarkerGenericRadius> toolTargetMarkers = new Dictionary<ulong, MapMarkerGenericRadius>();
        private readonly List<Timer> activeTimers = new List<Timer>();
        private readonly Dictionary<string, IStrikeExecutor> strikeExecutors = new Dictionary<string, IStrikeExecutor>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MonumentBlockZone> monumentBlockZones = new List<MonumentBlockZone>();
        private Timer toolPingWatcherTimer;
        private bool monumentBlockZonesLoaded;
        private bool auditWebhookConfigWarningPrinted;
        private ICurrencyAdapter currencyAdapter;

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

            [JsonProperty("Delivery")]
            public string Delivery = "drone";

            [JsonProperty("Payload")]
            public string Payload;

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

            [JsonProperty("BurstCount")]
            public int BurstCount;

            [JsonProperty("LineLength")]
            public float LineLength;

            [JsonProperty("Width")]
            public float Width;

            [JsonProperty("ImpactRadius")]
            public float ImpactRadius;

            [JsonProperty("PulseDelaySeconds")]
            public float PulseDelaySeconds;

            [JsonProperty("MissileCount")]
            public int MissileCount;

            [JsonProperty("RocketCount")]
            public int RocketCount;

            [JsonProperty("MaxTrackingSeconds")]
            public float MaxTrackingSeconds;

            [JsonProperty("MaxTrackingDistance")]
            public float MaxTrackingDistance;

            [JsonProperty("VehicleDamageScale")]
            public float VehicleDamageScale = 1f;

            [JsonProperty("SplashRadius")]
            public float SplashRadius;

            [JsonProperty("DamageScales")]
            public Dictionary<string, float> DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
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
        }

        private class DeliveryFlightPlan
        {
            public Vector3 Start;
            public Vector3 Release;
            public Vector3 End;
            public Vector3 Direction;
            public float Duration;
            public float FirstPayloadDelay;
            public readonly List<FlightWaypoint> Waypoints = new List<FlightWaypoint>();
        }

        private interface IStrikeExecutor
        {
            string Name { get; }
            bool CanExecute(StrikeDefinition strike);
            void Execute(AirstrikeCallContext context, Action<bool, string> callback);
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

                plugin.SetExpectedPayloadReleaseCount(context, count);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                plugin.StartDroneDeliveryVisual(context, count, DroneDropPayloadDelay, spec.FinishDelaySeconds, firstPayloadDelay);

                for (var i = 0; i < count; i++)
                {
                    var payloadIndex = i + 1;
                    plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * DroneDropPayloadDelay), () =>
                    {
                        if (!plugin.IsCallActive(context))
                        {
                            return;
                        }

                        string error;
                        if (!plugin.TrySpawnDronePayload(context, spec, payloadIndex, count, out error))
                        {
                            callback(false, error);
                        }
                    });
                }

                var finishDelay = Math.Max(0.1f, firstPayloadDelay + ((count - 1) * DroneDropPayloadDelay) + spec.FinishDelaySeconds);
                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, count + " " + spec.DisplayName + " payload(s) delivered.");
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

                plugin.SetExpectedPayloadReleaseCount(context, count);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                var postReleaseDuration = ((count - 1) * HeavyDropPayloadDelay) + spec.FinishDelaySeconds;
                plugin.StartAircraftDeliveryVisual(context, DeliveryVisualProfile.HeavyDrop, firstPayloadDelay, postReleaseDuration, "heavy drop");

                for (var i = 0; i < count; i++)
                {
                    var payloadIndex = i + 1;
                    plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * HeavyDropPayloadDelay), () =>
                    {
                        if (!plugin.IsCallActive(context))
                        {
                            return;
                        }

                        string error;
                        if (!plugin.TrySpawnHeavyDropPayload(context, spec, payloadIndex, count, out error))
                        {
                            callback(false, error);
                        }
                    });
                }

                var finishDelay = Math.Max(0.1f, firstPayloadDelay + ((count - 1) * HeavyDropPayloadDelay) + spec.FinishDelaySeconds);
                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, count + " " + spec.DisplayName + " heavy payload(s) delivered.");
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
                plugin.SetExpectedPayloadReleaseCount(context, count);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                var postReleaseDuration = ((count - 1) * RocketRunProjectileDelay) + spec.FinishDelaySeconds;
                plugin.StartAircraftDeliveryVisual(context, DeliveryVisualProfile.RocketRun, firstPayloadDelay, postReleaseDuration, "rocket run");
                for (var i = 0; i < count; i++)
                {
                    var rocketIndex = i + 1;
                    plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * RocketRunProjectileDelay), () =>
                    {
                        if (!plugin.IsCallActive(context))
                        {
                            return;
                        }

                        string error;
                        if (!plugin.TrySpawnRocketProjectile(context, spec, approach, rocketIndex, count, out error))
                        {
                            callback(false, error);
                        }
                    });
                }

                var finishDelay = Math.Max(0.1f, firstPayloadDelay + ((count - 1) * RocketRunProjectileDelay) + spec.FinishDelaySeconds);
                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, count + " " + spec.DisplayName + " rocket(s) fired.");
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
                plugin.SetExpectedPayloadReleaseCount(context, count);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                var payloadWindowDuration = (count - 1) * MlrsRocketDelay;
                var visualPostReleaseDuration = payloadWindowDuration + 2.35f;
                plugin.StartMlrsDeliveryVisual(context, approach, firstPayloadDelay, visualPostReleaseDuration);
                for (var i = 0; i < count; i++)
                {
                    var rocketIndex = i + 1;
                    plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * MlrsRocketDelay), () =>
                    {
                        if (!plugin.IsCallActive(context))
                        {
                            return;
                        }

                        string error;
                        if (!plugin.TrySpawnMlrsRocket(context, spec, approach, rocketIndex, count, out error))
                        {
                            callback(false, error);
                        }
                    });
                }

                var finishDelay = Math.Max(0.1f, firstPayloadDelay + ((count - 1) * MlrsRocketDelay) + spec.FinishDelaySeconds);
                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, count + " " + spec.DisplayName + " rocket(s) launched.");
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
                plugin.SetExpectedPayloadReleaseCount(context, count);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                var postReleaseDuration = ((count - 1) * HomingMissileLaunchDelay) + plugin.GetHomingTrackingSeconds(context.Strike) + spec.FinishDelaySeconds;
                plugin.StartAircraftDeliveryVisual(context, DeliveryVisualProfile.HomingMissile, firstPayloadDelay, postReleaseDuration, "homing missile");
                var targetId = context.Target.EntityId;
                for (var i = 0; i < count; i++)
                {
                    var missileIndex = i + 1;
                    plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * HomingMissileLaunchDelay), () =>
                    {
                        if (!plugin.IsCallActive(context))
                        {
                            return;
                        }

                        string error;
                        if (!plugin.TrySpawnHomingMissile(context, spec, approach, targetId, missileIndex, count, out error))
                        {
                            callback(false, error);
                        }
                    });
                }

                var finishDelay = Math.Max(0.1f, firstPayloadDelay + ((count - 1) * HomingMissileLaunchDelay) + plugin.GetHomingTrackingSeconds(context.Strike) + spec.FinishDelaySeconds);
                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, count + " " + spec.DisplayName + " homing missile(s) launched.");
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
                plugin.SetExpectedPayloadReleaseCount(context, burstCount);
                var firstPayloadDelay = plugin.GetDeliveryCarrierFirstPayloadDelay(context);
                plugin.StartA10DeliveryVisual(context, direction, burstCount, pulseDelay, firstPayloadDelay);
                for (var i = 0; i < burstCount; i++)
                {
                    var pulseIndex = i + 1;
                    plugin.ScheduleCallTimer(context, firstPayloadDelay + (i * pulseDelay), () =>
                    {
                        if (!plugin.IsCallActive(context))
                        {
                            return;
                        }

                        string error;
                        if (!plugin.TryRunA10Pulse(context, spec, direction, pulseIndex, burstCount, out error))
                        {
                            callback(false, error);
                        }
                    });
                }

                var finishDelay = Math.Max(0.1f, firstPayloadDelay + ((burstCount - 1) * pulseDelay) + A10FinishPaddingSeconds);
                plugin.ScheduleCallTimer(context, finishDelay, () =>
                {
                    if (plugin.IsCallActive(context))
                    {
                        callback(true, burstCount + " " + spec.DisplayName + " cannon pulse(s) completed.");
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
            Puts("Loaded " + GetEnabledStrikeCount() + " enabled strike definition(s). Charge-backed CID targeting binoculars, automatic tool ping targeting, persisted manual player defaults, scrollable CUI selection, high-rate visual delivery flyovers/artillery sources with autoload-safe repeated sound cues, loot item injection, monument blocking, audit logging/webhooks, cancellable warning calls, heavy warning markers, in-game warning fanout diagnostics, and direct-command executors are active in v0.1.39.");
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
        }

        private void Unload()
        {
            StopToolPingWatcher();

            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyStrikeUi(player);
            }

            CancelActiveCallsForUnload();
            DestroyAllToolTargetMarkers();
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
        }

        private object OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            TryApplyDeliveryCarrierHit(attacker, info);
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

            var expectedTargetType = ParseTargetType(strike.TargetType);
            if (config.Selection.AutoFilterByPingType && expectedTargetType != AirstrikeTargetType.Invalid && expectedTargetType != target.Type)
            {
                OpenDefaultSelectionMenu(player, "Your saved default " + strike.DisplayName + " needs a " + FormatTargetType(expectedTargetType) + ", but this is a " + FormatTargetType(target.Type) + ". Choose a target-compatible strike; defaults are only changed with /" + GetOpenCommand() + " default <strikeId>.");
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

            AirstrikeTarget raycastTarget;
            string raycastError;
            if (TryStoreRaycastTarget(player, AirstrikeToolPingSource, out raycastTarget, out raycastError))
            {
                return;
            }

            var associatedEntity = note.associatedId.Value == 0UL ? null : FindEntity(note.associatedId.Value);
            if (associatedEntity != null)
            {
                StoreTarget(player, ResolveMapNotePosition(note.worldPosition), associatedEntity, ClassifyTarget(associatedEntity), AirstrikeToolPingSource);
                return;
            }

            StoreMapNoteTarget(player, note.worldPosition, AirstrikeToolPingSource);
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
            var expectedType = ParseTargetType(strike.TargetType);
            var targetNote = expectedType != target.Type ? " Target type mismatch: strike expects " + FormatTargetType(expectedType) + "." : "";
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
            Timer scheduled = null;
            scheduled = timer.Once(Math.Max(0.01f, delay), () =>
            {
                activeTimers.Remove(scheduled);
                if (context != null)
                {
                    context.Timers.Remove(scheduled);
                }

                callback();
            });

            if (scheduled != null)
            {
                activeTimers.Add(scheduled);
                if (context != null)
                {
                    context.Timers.Add(scheduled);
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

            AirstrikeCallContext active;
            return activeCalls.TryGetValue(context.CallerUserId, out active) && ReferenceEquals(active, context);
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
            if (context == null || context.SpawnedEntities.Count == 0)
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
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }
            }
        }

        private void CleanupContextVisuals(AirstrikeCallContext context)
        {
            if (context == null || context.VisualEntities.Count == 0)
            {
                if (context != null)
                {
                    ClearDeliveryCarrier(context);
                }
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
            return configured > 0f ? Mathf.Clamp(configured, 0.02f, 2f) : 0.06f;
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

            var spread = Mathf.Clamp(context.Strike.SpreadRadius, 0f, 100f);
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

            var lineLength = Mathf.Clamp(context.Strike.LineLength <= 0f ? 55f : context.Strike.LineLength, 5f, 200f);
            var width = Mathf.Clamp(context.Strike.Width <= 0f ? 7f : context.Strike.Width, 0f, 50f);
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
                if (context == null || !IsCallActive(context) || !ReferenceEquals(context.DeliveryCarrier, entity))
                {
                    continue;
                }

                FailOrClearDestroyedDeliveryCarrier(context, info?.Initiator as BasePlayer);
                return;
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
                if (context == null || !IsCallActive(context) || !ReferenceEquals(context.DeliveryCarrier, hitEntity))
                {
                    continue;
                }

                if (context.DeliveryCarrierHealthRemaining <= 0f)
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

                context.DeliveryCarrierHealthRemaining = Math.Max(0f, context.DeliveryCarrierHealthRemaining - damage);
                if (config.General.DebugMode)
                {
                    Puts(context.Strike.Id + " delivery carrier " + context.DeliveryCarrierLabel + " took " + damage.ToString("0.0", CultureInfo.InvariantCulture) + " damage; " + context.DeliveryCarrierHealthRemaining.ToString("0.0", CultureInfo.InvariantCulture) + "/" + context.DeliveryCarrierMaxHealth.ToString("0.0", CultureInfo.InvariantCulture) + " health remaining.");
                }

                if (context.DeliveryCarrierHealthRemaining > 0f)
                {
                    return;
                }

                FailOrClearDestroyedDeliveryCarrier(context, attacker);
                return;
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
            if (plan == null)
            {
                return;
            }

            var safeTime = Mathf.Clamp(time, 0f, Mathf.Max(0.1f, plan.Duration));
            plan.Waypoints.Add(new FlightWaypoint
            {
                Position = position,
                Time = safeTime
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

        private Vector3 EvaluateFlightPlanPositionOnly(DeliveryFlightPlan plan, float elapsed)
        {
            if (plan == null)
            {
                return Vector3.zero;
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
                var eased = Mathf.SmoothStep(0f, 1f, segmentProgress);
                return Vector3.Lerp(a.Position, b.Position, eased);
            }

            return plan.Waypoints[lastIndex].Position;
        }

        private Vector3 GetFlightPlanTangentDirection(DeliveryFlightPlan plan, float elapsed, Vector3 fallbackDirection)
        {
            if (plan == null)
            {
                return fallbackDirection;
            }

            var safeDuration = Mathf.Max(0.1f, plan.Duration);
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
                    var eased = Mathf.SmoothStep(0f, 1f, segmentProgress);
                    position = Vector3.Lerp(a.Position, b.Position, eased);
                    direction = b.Position - a.Position;
                    if (direction.sqrMagnitude > 0.01f)
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
            var spread = Mathf.Clamp(context.Strike.SpreadRadius, 0f, 100f);
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
                var directRelease = GetPlannedImpactPosition(context, 1, Math.Max(1, payloadCount), approach, context.Strike == null ? 0f : context.Strike.SpreadRadius) + (Vector3.up * height);
                return BuildDeliveryFlightPlan(directRelease, approach, distance, firstPayloadDelay, postReleaseDuration, 2f, 18f);
            }

            var safeFirstPayloadDelay = Mathf.Max(0.1f, firstPayloadDelay);
            var safePostReleaseDuration = Mathf.Max(DronePathMinimumLoiterSeconds, postReleaseDuration);
            var safeDuration = Mathf.Clamp(safeFirstPayloadDelay + safePostReleaseDuration, 2.5f, 22f);
            var right = GetRightVector(approach);
            var wobble = config?.DeliveryVisuals == null ? 4.5f : Mathf.Clamp(config.DeliveryVisuals.DroneErraticApproachRadius, 0f, 20f);
            var start = target - (approach * distance) + (right * UnityEngine.Random.Range(-wobble, wobble)) + (Vector3.up * (height + UnityEngine.Random.Range(1.5f, 4f)));
            var firstImpact = GetPlannedImpactPosition(context, 1, Math.Max(1, payloadCount), approach, context.Strike == null ? 0f : context.Strike.SpreadRadius);
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

                var impact = GetPlannedImpactPosition(context, i, payloadCount, approach, context.Strike == null ? 0f : context.Strike.SpreadRadius);
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

        private void StartDroneDeliveryVisual(AirstrikeCallContext context, int payloadCount, float payloadDelay, float finishDelay, float initialPayloadDelay)
        {
            if (!ShouldSpawnDeliveryVisual(context) || !config.DeliveryVisuals.SpawnDroneVisuals)
            {
                return;
            }

            var approach = GetRocketApproachDirection(context);
            var target = ResolveImpactPosition(context.Target.Position);
            var distance = Mathf.Clamp(config.DeliveryVisuals.DroneFlyoverDistance, 15f, 150f);
            var height = Mathf.Clamp(config.DeliveryVisuals.DroneFlyoverHeight, 8f, 80f);
            var postReleaseDuration = ((Math.Max(1, payloadCount) - 1) * payloadDelay) + finishDelay;
            BuildDronePayloadImpactPlan(context, Math.Max(1, payloadCount), approach, target);
            var plan = BuildDroneErraticFlightPlan(context, target, approach, distance, height, Math.Max(1, payloadCount), payloadDelay, initialPayloadDelay, postReleaseDuration);

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

            StartVisualFlyover(context, F15VisualPrefab, plan, "F-15 MLRS flyover");
        }

        private void StartA10DeliveryVisual(AirstrikeCallContext context, Vector3 direction, int burstCount, float pulseDelay, float initialPayloadDelay)
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
            var postReleaseDuration = ((Math.Max(1, burstCount) - 1) * pulseDelay) + A10FinishPaddingSeconds;
            var strikeHeight = GetAircraftStrikePassHeight(context.Strike, DeliveryVisualProfile.A10, height);
            var release = target - (direction * (lineLength * 0.5f)) + (Vector3.up * strikeHeight);
            var plan = BuildA10DivingStrafeFlightPlan(target, release, direction, distance, initialPayloadDelay, postReleaseDuration, height, lineLength, 3.5f, 32f);

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

            ApplyTerrainClearanceToFlightPlan(plan, GetVisualTerrainClearance(context == null ? null : context.Strike, label));

            if (string.Equals(prefab, CargoPlaneVisualPrefab, StringComparison.OrdinalIgnoreCase) && IsLinearFlightPlan(plan))
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

                var rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
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

            var rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
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
                position = EnsurePositionAboveTerrain(position, GetVisualTerrainClearance(context == null ? null : context.Strike, label));
                var rotation = GetSmoothedVisualRotation(visual, direction, interval);

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

        private Quaternion GetSmoothedVisualRotation(BaseEntity visual, Vector3 direction, float interval)
        {
            if (visual == null || visual.IsDestroyed || direction.sqrMagnitude <= 0.01f)
            {
                return visual == null ? Quaternion.identity : visual.transform.rotation;
            }

            var targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            var smoothTime = GetVisualRotationSmoothTimeSeconds();
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

        private bool TrySpawnDronePayload(AirstrikeCallContext context, DronePayloadSpec spec, int payloadIndex, int totalPayloads, out string error)
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
            var impact = ResolveImpactPosition(GetPlannedImpactPosition(context, payloadIndex, totalPayloads, approach, context.Strike.SpreadRadius));
            var spawnHeight = GetDronePayloadSpawnHeight(spec);
            var spawn = EnsurePositionAboveTerrain(impact + Vector3.up * spawnHeight, GetPayloadTerrainClearance());
            var dropDirection = impact + (Vector3.up * 0.15f) - spawn;
            if (dropDirection.sqrMagnitude <= 0.01f)
            {
                dropDirection = Vector3.down;
            }
            else
            {
                dropDirection.Normalize();
            }
            var dropVelocity = dropDirection * PayloadDownwardVelocity;
            BaseEntity entity = null;

            try
            {
                entity = GameManager.server.CreateEntity(spec.Prefab, spawn, Quaternion.LookRotation(Vector3.down), true) as BaseEntity;
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
                    timed.timerAmountMin = spec.FuseSeconds;
                    timed.timerAmountMax = spec.FuseSeconds;
                }

                var projectile = entity.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.speed = Math.Max(projectile.speed, PayloadDownwardVelocity);
                    projectile.InitializeVelocity(dropVelocity);
                }

                entity.Spawn();

                if (projectile != null)
                {
                    projectile.SetVelocity(dropVelocity);
                }
                else
                {
                    entity.SetVelocity(dropVelocity);
                }

                context.ImpactStarted = true;
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

        private bool TrySpawnHeavyDropPayload(AirstrikeCallContext context, HeavyDropPayloadSpec spec, int payloadIndex, int totalPayloads, out string error)
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
            var impact = ResolveImpactPosition(GetPlannedImpactPosition(context, payloadIndex, totalPayloads, approach, context.Strike.SpreadRadius));
            var spawn = EnsurePositionAboveTerrain(impact + Vector3.up * HeavyDropSpawnHeight, GetPayloadTerrainClearance());
            BaseEntity entity = null;

            try
            {
                entity = GameManager.server.CreateEntity(spec.Prefab, spawn, Quaternion.LookRotation(Vector3.down), true) as BaseEntity;
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
                    projectile.speed = Math.Max(projectile.speed, HeavyDropDownwardVelocity);
                    projectile.InitializeVelocity(Vector3.down * HeavyDropDownwardVelocity);
                }

                entity.Spawn();

                if (projectile != null)
                {
                    projectile.SetVelocity(Vector3.down * HeavyDropDownwardVelocity);
                }
                else
                {
                    entity.SetVelocity(Vector3.down * HeavyDropDownwardVelocity);
                }

                context.ImpactStarted = true;
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

        private bool TrySpawnRocketProjectile(AirstrikeCallContext context, RocketRunPayloadSpec spec, Vector3 approach, int rocketIndex, int totalRockets, out string error)
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

            var impact = ResolveImpactPosition(GetRocketVolleyImpactPosition(context, approach, rocketIndex, totalRockets));
            var spawn = EnsurePositionAboveTerrain(impact - (approach * RocketRunSpawnDistance) + (Vector3.up * RocketRunSpawnHeight), GetPayloadTerrainClearance());
            var aimPoint = impact + Vector3.up * 1.25f;
            var direction = aimPoint - spawn;
            if (direction.sqrMagnitude <= 0.01f)
            {
                error = "Rocket approach direction could not be resolved.";
                return false;
            }

            var velocity = direction.normalized * Math.Max(1f, spec.ProjectileSpeed);
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
                    projectile.speed = Math.Max(projectile.speed, spec.ProjectileSpeed);
                    projectile.InitializeVelocity(velocity);
                }

                entity.Spawn();

                if (projectile != null)
                {
                    projectile.SetVelocity(velocity);
                }
                else
                {
                    entity.SetVelocity(velocity);
                }

                RunSafeEffect(RocketLaunchEffect, spawn, spec.DisplayName + " rocket launch");
                context.ImpactStarted = true;
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

        private bool TrySpawnMlrsRocket(AirstrikeCallContext context, MlrsPayloadSpec spec, Vector3 approach, int rocketIndex, int totalRockets, out string error)
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

            var impact = ResolveImpactPosition(RandomSpreadPosition(context.Target.Position, context.Strike.SpreadRadius));
            var launchJitter = new Vector3(
                UnityEngine.Random.Range(-18f, 18f),
                UnityEngine.Random.Range(-4f, 8f),
                UnityEngine.Random.Range(-18f, 18f));
            var spawn = EnsurePositionAboveTerrain(impact - (approach * MlrsRocketSpawnDistance) + (Vector3.up * MlrsRocketSpawnHeight) + launchJitter, GetPayloadTerrainClearance());
            var aimPoint = impact + Vector3.up * 1.5f;
            var direction = aimPoint - spawn;
            if (direction.sqrMagnitude <= 0.01f)
            {
                error = "MLRS launch direction could not be resolved.";
                return false;
            }

            var velocity = direction.normalized * Math.Max(1f, spec.ProjectileSpeed);
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
                    projectile.speed = Math.Max(projectile.speed, spec.ProjectileSpeed);
                    projectile.InitializeVelocity(velocity);
                }

                entity.Spawn();

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
                context.ImpactStarted = true;
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

        private bool TrySpawnHomingMissile(AirstrikeCallContext context, HomingMissileSpec spec, Vector3 approach, ulong targetId, int missileIndex, int totalMissiles, out string error)
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

            var targetPoint = GetHomingTargetPoint(target);
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
            var direction = targetPoint - spawn;
            if (direction.sqrMagnitude <= 0.01f)
            {
                error = "Homing missile launch direction could not be resolved.";
                return false;
            }

            var velocity = direction.normalized * Math.Max(1f, spec.ProjectileSpeed);
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
                    projectile.speed = Math.Max(projectile.speed, spec.ProjectileSpeed);
                    projectile.InitializeVelocity(velocity);
                }

                entity.Spawn();

                if (projectile != null)
                {
                    projectile.SetVelocity(velocity);
                }
                else
                {
                    entity.SetVelocity(velocity);
                }

                context.ImpactStarted = true;
                context.State = StrikeExecutionState.Impacting;
                context.SpawnedEntities.Add(entity);
                MarkPayloadReleased(context);

                ScheduleHomingMissileTrack(context, entity, spec, targetId, spawn, GetPreciseNow(), missileIndex, totalMissiles);

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

        private void ScheduleHomingMissileTrack(AirstrikeCallContext context, BaseEntity missile, HomingMissileSpec spec, ulong targetId, Vector3 launchPosition, double launchStartedAt, int missileIndex, int totalMissiles)
        {
            ScheduleCallTimer(context, HomingMissileTrackInterval, () =>
            {
                if (!IsCallActive(context) || missile == null || missile.IsDestroyed)
                {
                    return;
                }

                var elapsed = GetPreciseNow() - launchStartedAt;
                if (elapsed > GetHomingTrackingSeconds(context.Strike))
                {
                    missile.Kill(BaseNetworkable.DestroyMode.None);
                    if (config.General.DebugMode)
                    {
                        Puts(context.Strike.Id + " homing missile " + missileIndex + "/" + totalMissiles + " expired after tracking timeout.");
                    }
                    return;
                }

                var traveled = Vector3.Distance(launchPosition, missile.transform.position);
                if (traveled > GetHomingTrackingDistance(context.Strike))
                {
                    missile.Kill(BaseNetworkable.DestroyMode.None);
                    if (config.General.DebugMode)
                    {
                        Puts(context.Strike.Id + " homing missile " + missileIndex + "/" + totalMissiles + " expired after max tracking distance.");
                    }
                    return;
                }

                var target = FindEntity(targetId) as BaseCombatEntity;
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
                    DetonateHomingMissile(context, missile, target, targetPoint, missileIndex, totalMissiles);
                    return;
                }

                var proximity = Mathf.Clamp(Math.Max(HomingMissileProximityRadius, GetHomingSplashRadius(context.Strike) * 0.4f), 2f, 8f);
                if (direction.magnitude <= proximity)
                {
                    DetonateHomingMissile(context, missile, target, targetPoint, missileIndex, totalMissiles);
                    return;
                }

                var velocity = direction.normalized * Math.Max(1f, spec.ProjectileSpeed);
                var projectile = missile.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.speed = Math.Max(projectile.speed, spec.ProjectileSpeed);
                    projectile.SetVelocity(velocity);
                }
                else
                {
                    missile.SetVelocity(velocity);
                }

                missile.transform.rotation = Quaternion.LookRotation(direction.normalized);
                missile.SendNetworkUpdate();
                ScheduleHomingMissileTrack(context, missile, spec, targetId, launchPosition, launchStartedAt, missileIndex, totalMissiles);
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

            var entity = FindEntity(context.Target.EntityId);
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

        private void DetonateHomingMissile(AirstrikeCallContext context, BaseEntity missile, BaseCombatEntity target, Vector3 impact, int missileIndex, int totalMissiles)
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
            ApplyHomingMissileDamage(context, target, impact, out damagedCount);

            if (missile != null && !missile.IsDestroyed)
            {
                missile.Kill(BaseNetworkable.DestroyMode.None);
            }

            context.ImpactStarted = true;
            context.State = StrikeExecutionState.Impacting;

            if (config.General.DebugMode)
            {
                Puts(context.Strike.Id + " homing missile " + missileIndex + "/" + totalMissiles + " detonated at " + FormatPosition(impact) + " and damaged " + damagedCount + " combat entity/entities.");
            }
        }

        private void ApplyHomingMissileDamage(AirstrikeCallContext context, BaseCombatEntity target, Vector3 impact, out int damagedCount)
        {
            damagedCount = 0;
            var player = GetCallPlayer(context);
            var damaged = new HashSet<BaseCombatEntity>();

            if (target != null && !target.IsDestroyed && !target.IsDead())
            {
                var vehicleScale = Mathf.Clamp(context.Strike.VehicleDamageScale, 0f, 10f)
                    * GetGlobalDamageScale("Vehicles")
                    * GetStrikeDamageScale(context.Strike, "Vehicles");
                var directDamage = HomingMissileBaseVehicleDamage * Mathf.Clamp(vehicleScale, 0f, 10f);
                if (directDamage > 0f)
                {
                    ApplyHomingDamageToEntity(target, directDamage, player, ref damagedCount);
                    damaged.Add(target);
                }
            }

            var radius = GetHomingSplashRadius(context.Strike);
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
                    var scale = GetGlobalDamageScale(key) * GetStrikeDamageScale(context.Strike, key);
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

        private bool TrySpawnMortarShell(AirstrikeCallContext context, MortarPayloadSpec spec, int shellIndex, int totalShells, out string error)
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

            var impact = ResolveImpactPosition(RandomSpreadPosition(context.Target.Position, context.Strike.SpreadRadius));
            var spawn = EnsurePositionAboveTerrain(impact + Vector3.up * MortarShellSpawnHeight, GetPayloadTerrainClearance());
            BaseEntity entity = null;

            try
            {
                RunMortarLaunchVisual(context, shellIndex);
                entity = GameManager.server.CreateEntity(spec.Prefab, spawn, Quaternion.LookRotation(Vector3.down), true) as BaseEntity;
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
                    projectile.speed = Math.Max(projectile.speed, MortarShellDownwardVelocity);
                    projectile.InitializeVelocity(Vector3.down * MortarShellDownwardVelocity);
                }

                entity.Spawn();

                if (projectile != null)
                {
                    projectile.SetVelocity(Vector3.down * MortarShellDownwardVelocity);
                }
                else
                {
                    entity.SetVelocity(Vector3.down * MortarShellDownwardVelocity);
                }

                context.ImpactStarted = true;
                context.State = StrikeExecutionState.Impacting;
                context.SpawnedEntities.Add(entity);

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

        private bool TryRunA10Pulse(AirstrikeCallContext context, A10StrafeSpec spec, Vector3 direction, int pulseIndex, int totalPulses, out string error)
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
                var impact = ResolveImpactPosition(GetA10PulsePosition(context, direction, pulseIndex, totalPulses));
                RunA10PulseEffects(impact, direction, pulseIndex);

                int damagedCount;
                ApplyA10DamagePulse(context, spec, impact, out damagedCount);

                context.ImpactStarted = true;
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

        private void RunA10PulseEffects(Vector3 impact, Vector3 direction, int pulseIndex)
        {
            try
            {
                var effectPosition = impact + Vector3.up * 0.1f;
                Effect.server.Run(BulletImpactEffect, effectPosition);

                if (pulseIndex == 1 || pulseIndex % A10MuzzleEffectInterval == 0)
                {
                    var muzzlePosition = EnsurePositionAboveTerrain(impact - (direction * 45f) + (Vector3.up * 32f), GetPayloadTerrainClearance());
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

        private void ApplyA10DamagePulse(AirstrikeCallContext context, A10StrafeSpec spec, Vector3 impact, out int damagedCount)
        {
            damagedCount = 0;
            var radius = Mathf.Clamp(context.Strike.ImpactRadius <= 0f ? 2.5f : context.Strike.ImpactRadius, 0.5f, 25f);
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

                    var scale = GetA10DamageScale(context.Strike, combatEntity);
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

        private float GetA10DamageScale(StrikeDefinition strike, BaseCombatEntity entity)
        {
            var key = GetDamageScaleKey(entity);
            return Mathf.Clamp(GetGlobalDamageScale(key) * GetStrikeDamageScale(strike, key), 0f, 10f);
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
            if (strike?.DamageScales == null || string.IsNullOrWhiteSpace(key))
            {
                return 1f;
            }

            foreach (var entry in strike.DamageScales)
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return Mathf.Clamp(entry.Value, 0f, 10f);
                }
            }

            return 1f;
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

            var expectedTargetType = ParseTargetType(strike.TargetType);
            if (expectedTargetType == AirstrikeTargetType.Invalid)
            {
                return Fail(result, "invalid_strike_target_type", strike.DisplayName + " has an invalid configured target type.");
            }

            if (target.Type != expectedTargetType)
            {
                return Fail(result, "target_type_mismatch", strike.DisplayName + " requires " + FormatTargetType(expectedTargetType) + ", but your target is " + FormatTargetType(target.Type) + ".");
            }

            if (target.Type == AirstrikeTargetType.VehiclePing)
            {
                if (target.EntityId == 0UL)
                {
                    return Fail(result, "vehicle_target_missing_entity", strike.DisplayName + " requires a vehicle target with entity tracking. Aim " + GetAirstrikeItemDisplayName() + " directly at a vehicle and ping it.");
                }

                var entity = FindEntity(target.EntityId);
                var combatEntity = entity as BaseCombatEntity;
                if (entity == null || entity.IsDestroyed || (combatEntity != null && combatEntity.IsDead()))
                {
                    return Fail(result, "vehicle_target_gone", "That vehicle target is no longer valid.");
                }
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

                entries.Add(entry.Key + " (" + FormatTargetType(ParseTargetType(strike.TargetType)) + ", " + GetFinalRPCost(player, strike) + " RP)");
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

                if (!config.Selection.AutoFilterByPingType || ParseTargetType(strike.TargetType) == targetType)
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

            var entity = hit.GetEntity();
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

            var hitEntity = hit.GetEntity();
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

            if (ParseTargetType(strike.TargetType) == AirstrikeTargetType.Invalid)
            {
                strike.TargetType = fallback?.TargetType ?? "ground_ping";
            }

            if (string.IsNullOrWhiteSpace(strike.Delivery))
            {
                strike.Delivery = fallback?.Delivery ?? "drone";
            }

            if (string.IsNullOrWhiteSpace(strike.Payload))
            {
                strike.Payload = fallback?.Payload ?? id;
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
            strike.BurstCount = Mathf.Clamp(strike.BurstCount, 0, 80);
            strike.LineLength = Mathf.Clamp(strike.LineLength, 0f, 200f);
            strike.Width = Mathf.Clamp(strike.Width, 0f, 50f);
            strike.ImpactRadius = Mathf.Clamp(strike.ImpactRadius, 0f, 25f);
            strike.PulseDelaySeconds = Mathf.Clamp(strike.PulseDelaySeconds, 0f, 2f);
            strike.MissileCount = Mathf.Clamp(strike.MissileCount, 0, 12);
            strike.RocketCount = Mathf.Clamp(strike.RocketCount, 0, 48);
            strike.MaxTrackingSeconds = Mathf.Clamp(strike.MaxTrackingSeconds, 0f, 60f);
            strike.MaxTrackingDistance = Mathf.Clamp(strike.MaxTrackingDistance, 0f, 1000f);
            strike.VehicleDamageScale = Mathf.Clamp(strike.VehicleDamageScale, 0f, 10f);
            strike.SplashRadius = Mathf.Clamp(strike.SplashRadius, 0f, 50f);

            if (string.IsNullOrWhiteSpace(strike.PermissionRequired))
            {
                strike.PermissionRequired = fallback?.PermissionRequired ?? "";
            }

            if (strike.DamageScales == null)
            {
                strike.DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }
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
