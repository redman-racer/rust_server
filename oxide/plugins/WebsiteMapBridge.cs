using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Facepunch.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WebsiteMapBridge", "Raidlands", "1.0.25")]
    [Description("Publishes the current RustMapApi map image and sampled terrain to the Raidlands website.")]
    public class WebsiteMapBridge : CovalencePlugin
    {
        [PluginReference] private Plugin RustMapApi;
        [PluginReference] private Plugin Clans;
        [PluginReference] private Plugin RaidlandsEvents;

        private const string SecretsConfigName = "Secrets.local";
        private const string VipBridgeConfigName = "WebsiteVipBridge";
        private const int EncodingJpg = 1;
        private const int EncodingPng = 2;

        private Configuration config;
        private Timer autoPublishTimer;
        private Timer playerLocationTimer;
        private Timer environmentTimer;
        private Timer worldEventTimer;
        private Dictionary<string, string> secrets;
        private string secretsConfigSource;
        private JObject vipBridgeConfig;
        private bool publishInFlight;
        private string lastPublishedWipeKey = "";
        private string lastEnvironmentSyncAt = "";
        private string lastEnvironmentDiagnosticSummary = "awaiting first environment sample";
        private long lastEnvironmentSampleMilliseconds;
        private string lastWeatherOverrideSignature = "";
        private bool worldEventPublishInFlight;
        private DateTime worldEventPublishStartedUtc = DateTime.MinValue;
        private long worldEventPublishGeneration;
        private long activeWorldEventPublishGeneration;
        private string lastWorldEventSyncAt = "never";
        private string lastWorldEventError = "none";
        private int lastWorldEventAccepted;
        private int lastWorldEventPayloadBytes;
        private readonly Dictionary<ulong, TrackedWorldEntity> trackedWorldEntities = new Dictionary<ulong, TrackedWorldEntity>();
        private readonly Dictionary<ulong, DiscreteWorldEntity> discreteWorldEntities = new Dictionary<ulong, DiscreteWorldEntity>();
        private readonly Dictionary<ulong, bool> discreteEntityStates = new Dictionary<ulong, bool>();
        private readonly Dictionary<ulong, AirdropCorrelation> airdropCorrelations = new Dictionary<ulong, AirdropCorrelation>();
        private readonly Dictionary<string, MemberInfo> weatherConVarMemberCache = new Dictionary<string, MemberInfo>(StringComparer.OrdinalIgnoreCase);

        private class Configuration
        {
            public string ApiBaseUrl = "https://raidlands.net";
            public string ServerId = "raidlands-main";
            public string SharedSecret = "";
            public string WipeKey = "";
            public string RenderName = "Icons";
            public string TextureRenderName = "Default";
            [JsonProperty("FileType (Jpg, Png)")]
            public string FileType = "Jpg";
            public float ImageResolutionScale = 0.5f;
            public bool AutoPublishOnRustMapApiReady = true;
            public bool AutoPublishOnServerInitialized = true;
            public int AutoPublishDelaySeconds = 10;
            public int WebRequestTimeoutMilliseconds = 60000;
            public bool PublishTerrain = true;
            public int TerrainSampleResolution = 129;
            public bool IncludeTerrainColors = true;
            public bool IncludeMonuments = true;
            public bool PublishSkybox = true;
            public string SkyboxImagePath = "oxide/data/WebsiteMapBridge/current-skybox.png";
            public bool PublishPlayerLocations = true;
            public int PlayerLocationIntervalSeconds = 15;
            public bool PublishReplayEvents = true;
            public bool PublishRaidlandsEvents = true;
            public bool PublishWorldEvents = true;
            public bool PublishDiscreteServerEvents = true;
            public int WorldEventIntervalSeconds = 5;
            public float WorldEventMinimumMovementMeters = 2f;
            public float WorldEventMinimumRotationDegrees = 3f;
            public int WorldEventRouteMaxSamples = 96;
            public int WorldEventPayloadMaxBytes = 11500;
            public float AirdropCarrierSearchRadiusMeters = 600f;
            public bool PublishEnvironment = true;
            public int EnvironmentIntervalSeconds = 30;
        }

        private class DiscreteWorldEntity
        {
            public BaseEntity entity;
            public string family;
            public string monument;
        }

        private class MapUploadPayload
        {
            public string server_id;
            public string wipe_key;
            public string wipe_started_at;
            public string map_name;
            public string render_name;
            public string file_type;
            public string image_base64;
            public string image_sha256;
            public string texture_render_name;
            public string texture_image_base64;
            public string texture_image_sha256;
            public string skybox_image_base64;
            public string skybox_image_sha256;
            public int image_width;
            public int image_height;
            public int resolution;
            public int world_size;
            public int seed;
            public int protocol;
            public string generated_at;
            public TerrainUploadPayload terrain;
        }

        private class EnvironmentSnapshotPayload
        {
            public string server_id;
            public string wipe_key;
            public string sampled_at;
            public float rust_time;
            public float day_fraction;
            public EnvironmentVectorPayload sun_direction;
            public float sun_intensity;
            public string sun_color;
            public float ambient_intensity;
            public string ambient_color;
            public float? cloud_coverage;
            public float? rain_intensity;
            public float? fog_intensity;
            public string weather_sample_summary;
            public WeatherSnapshotPayload weather;
        }

        private class WeatherSnapshotPayload
        {
            public Dictionary<string, WeatherParameterPayload> parameters;
            public WeatherStateDiagnosticsPayload state;
            public string override_mode;
            public int override_count;
            public int parameter_count;
        }

        private class WeatherStateDiagnosticsPayload
        {
            public string previous;
            public string current;
            public string target;
            public string next;
            public float? blend;
            public string seed_previous;
            public string seed_target;
            public string seed_next;
            public bool? rain_grace_active;
            public string source;
        }

        private class WeatherParameterPayload
        {
            public string key;
            public float? value;
            public float? raw;
            public bool is_dynamic;
            public string source;
        }

        private class WeatherParameterDefinition
        {
            public string name;
            public string key;
            public string member;
            public string[] effectivePath;

            public WeatherParameterDefinition(string name, string key, string member, params string[] effectivePath)
            {
                this.name = name;
                this.key = key;
                this.member = member;
                this.effectivePath = effectivePath ?? new string[0];
            }
        }

        private static readonly WeatherParameterDefinition[] WeatherParameterDefinitions =
        {
            new WeatherParameterDefinition("rain", "weather.rain", "rain", "Rain"),
            new WeatherParameterDefinition("thunder", "weather.thunder", "thunder", "Thunder"),
            new WeatherParameterDefinition("rainbow", "weather.rainbow", "rainbow", "Rainbow"),
            new WeatherParameterDefinition("fog", "weather.fog", "fog", "Atmosphere", "Fogginess"),
            new WeatherParameterDefinition("atmosphereRayleigh", "weather.atmosphere_rayleigh", "atmosphere_rayleigh", "Atmosphere", "RayleighMultiplier"),
            new WeatherParameterDefinition("atmosphereMie", "weather.atmosphere_mie", "atmosphere_mie", "Atmosphere", "MieMultiplier"),
            new WeatherParameterDefinition("atmosphereBrightness", "weather.atmosphere_brightness", "atmosphere_brightness", "Atmosphere", "Brightness"),
            new WeatherParameterDefinition("atmosphereContrast", "weather.atmosphere_contrast", "atmosphere_contrast", "Atmosphere", "Contrast"),
            new WeatherParameterDefinition("atmosphereDirectionality", "weather.atmosphere_directionality", "atmosphere_directionality", "Atmosphere", "Directionality"),
            new WeatherParameterDefinition("cloudSize", "weather.cloud_size", "cloud_size", "Clouds", "Size"),
            new WeatherParameterDefinition("cloudOpacity", "weather.cloud_opacity", "cloud_opacity", "Clouds", "Opacity"),
            new WeatherParameterDefinition("cloudCoverage", "weather.cloud_coverage", "cloud_coverage", "Clouds", "Coverage"),
            new WeatherParameterDefinition("cloudSharpness", "weather.cloud_sharpness", "cloud_sharpness", "Clouds", "Sharpness"),
            new WeatherParameterDefinition("cloudColoring", "weather.cloud_coloring", "cloud_coloring", "Clouds", "Coloring"),
            new WeatherParameterDefinition("cloudAttenuation", "weather.cloud_attenuation", "cloud_attenuation", "Clouds", "Attenuation"),
            new WeatherParameterDefinition("cloudScattering", "weather.cloud_scattering", "cloud_scattering", "Clouds", "Scattering"),
            new WeatherParameterDefinition("cloudBrightness", "weather.cloud_brightness", "cloud_brightness", "Clouds", "Brightness")
        };

        private class EnvironmentVectorPayload
        {
            public float x;
            public float y;
            public float z;
        }

        private class EnvironmentSnapshotResponse
        {
            public bool ok;
            public string error;
            public EnvironmentSnapshotResult environment;
        }

        private class EnvironmentSnapshotResult
        {
            public string sampledAt;
        }

        private class TerrainUploadPayload
        {
            public int version = 2;
            public string serverId;
            public string wipeKey;
            public string mapName;
            public int resolution;
            public int worldSize;
            public int seed;
            public float waterLevel;
            public float minHeight;
            public float maxHeight;
            public string generatedAt;
            public List<float> heights;
            public List<string> colors;
            public List<MonumentUploadPayload> monuments;
            public List<PowerLineUploadPayload> powerLines;
            public List<RoadUploadPayload> roads;
        }

        private class MonumentUploadPayload
        {
            public string name;
            public string prefab;
            public string kind;
            public float x;
            public float y;
            public float z;
            public float radius;
            public float rotationY;
        }

        private class TerrainPointUploadPayload
        {
            public float x;
            public float y;
            public float z;
        }

        private class PowerLineUploadPayload
        {
            public string name;
            public List<TerrainPointUploadPayload> points;
        }

        private class RoadUploadPayload
        {
            public string name;
            public string kind;
            public float width;
            public List<TerrainPointUploadPayload> points;
        }

        private class MapUploadResponse
        {
            public bool ok;
            public string error;
            public string url;
            public MapUploadResult map;
        }

        private class MapUploadResult
        {
            public string url;
            public string publicUrl;
            public string textureUrl;
            public string terrainUrl;
            public string skyboxUrl;
            public string wipeKey;
            public string renderName;
            public string publishedAt;
            public int terrainResolution;
        }

        private class PlayerLocationSnapshotPayload
        {
            public string server_id;
            public string wipe_key;
            public string sampled_at;
            public List<PlayerLocationPayload> players;
        }

        private class PlayerLocationPayload
        {
            public string steam_id64;
            public string display_name;
            public string clan_tag;
            public float x;
            public float y;
            public float z;
        }

        private class PlayerLocationSnapshotResponse
        {
            public bool ok;
            public string error;
            public PlayerLocationSnapshotResult locations;
        }

        private class PlayerLocationSnapshotResult
        {
            public int acceptedPlayers;
            public string sampledAt;
        }

        private class ReplayEventSnapshotPayload
        {
            public string server_id;
            public string wipe_key;
            public List<ReplayEventPayload> events;
        }

        private class ReplayEventPayload
        {
            public string event_key;
            public string event_type;
            public string occurred_at;
            public float x;
            public float y;
            public float z;
            public string vehicle;
            public Dictionary<string, object> payload;
        }

        private class ReplayEventSnapshotResponse
        {
            public bool ok;
            public string error;
            public ReplayEventSnapshotResult events;
        }

        private class ReplayEventSnapshotResult
        {
            public int acceptedEvents;
        }

        private class WorldEntityDescriptor
        {
            public string eventType;
            public string envelopeType;
            public string vehicle;
            public string assetKey;
        }

        private class WorldRouteSample
        {
            public long timestampMilliseconds;
            public Vector3 position;
            public Quaternion rotation;
        }

        private class TrackedWorldEntity
        {
            public ulong networkId;
            public string eventKey;
            public string prefab;
            public string state;
            public string spawnedAt;
            public string sampledAt;
            public string endedAt;
            public string endReason;
            public string monumentContext;
            public WorldEntityDescriptor descriptor;
            public BaseEntity entity;
            public Vector3 lastPosition;
            public Quaternion lastRotation;
            public DateTime lastSampleUtc;
            public Vector3 velocity;
            public bool dirty;
            public int revision;
            public readonly List<WorldRouteSample> route = new List<WorldRouteSample>();
        }

        private class AirdropCorrelation
        {
            public string carrierEntityKey;
            public string carrierPrefab;
            public string vehicle;
            public string assetKey;
            public string releasedAt;
            public Vector3 releasePosition;
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating default WebsiteMapBridge config.");
            config = new Configuration();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            var hasPublishSkyboxSetting = ConfigHasProperty("PublishSkybox");
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            config = Config.ReadObject<Configuration>() ?? new Configuration();
            NormalizeConfig(hasPublishSkyboxSetting);
            Config.WriteObject(config, true);
        }

        private void OnServerInitialized()
        {
            LogBridgeSecretDiagnostics();
            if (config.AutoPublishOnServerInitialized)
            {
                QueueAutoPublish("server initialized");
            }
            StartPlayerLocationPublisher();
            StartEnvironmentPublisher();
            StartWorldEventPublisher();
        }

        private void OnNewSave(string filename)
        {
            lastPublishedWipeKey = "";
            QueueAutoPublish($"new save created ({FirstNonEmpty(filename, "unknown")})");
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (!config.PublishReplayEvents || entity == null)
            {
                return;
            }

            var baseEntity = entity as BaseEntity;
            var prefab = baseEntity == null ? "" : (baseEntity.PrefabName ?? baseEntity.ShortPrefabName ?? "");
            if (prefab.IndexOf("supply_drop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                timer.Once(0.25f, () => PublishAirdropReplayEvent(baseEntity, false));
            }

            timer.Once(0.5f, () =>
            {
                TryTrackWorldEntity(baseEntity, true);
                TryIndexDiscreteWorldEntity(baseEntity);
            });
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var baseEntity = entity as BaseEntity;
            if (baseEntity == null || baseEntity.net == null)
            {
                return;
            }

            EndTrackedWorldEntity(baseEntity.net.ID.Value, "killed_or_despawned");
            discreteWorldEntities.Remove(baseEntity.net.ID.Value);
            discreteEntityStates.Remove(baseEntity.net.ID.Value);
            if ((baseEntity.PrefabName ?? baseEntity.ShortPrefabName ?? "").IndexOf("supply_drop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                airdropCorrelations.Remove(baseEntity.net.ID.Value);
            }
        }

        private void OnSupplyDropLanded(SupplyDrop entity)
        {
            if (config.PublishReplayEvents && entity != null)
            {
                PublishAirdropReplayEvent(entity, true);
            }
        }

        private void OnCrateHack(HackableLockedCrate crate)
        {
            PublishCrateHackEvent(crate, false);
        }

        private void OnCrateHackEnd(HackableLockedCrate crate)
        {
            PublishCrateHackEvent(crate, true);
        }

        private void Unload()
        {
            autoPublishTimer?.Destroy();
            autoPublishTimer = null;
            playerLocationTimer?.Destroy();
            playerLocationTimer = null;
            environmentTimer?.Destroy();
            environmentTimer = null;
            worldEventTimer?.Destroy();
            worldEventTimer = null;
            trackedWorldEntities.Clear();
            discreteWorldEntities.Clear();
            discreteEntityStates.Clear();
            airdropCorrelations.Clear();
        }

        private void OnRustMapApiReady()
        {
            QueueAutoPublish("RustMapApi ready");
        }

        [ConsoleCommand("rl_map_publish")]
        private void PublishCommand(ConsoleSystem.Arg arg)
        {
            if (!CanUsePublishCommand(arg))
            {
                ReplyToCommand(arg, "You must be server console, RCON, or auth level 2 to publish the website map.");
                return;
            }

            var renderName = arg.GetString(0, config.RenderName);
            var scale = config.ImageResolutionScale;

            if (arg.Args != null && arg.Args.Length > 1)
            {
                float parsedScale;

                if (!float.TryParse(arg.GetString(1, ""), out parsedScale) || parsedScale <= 0f)
                {
                    ReplyToCommand(arg, "Invalid syntax. Use rl_map_publish <renderName:optional> <resolutionScale:optional>");
                    return;
                }

                scale = parsedScale;
            }

            ReplyToCommand(arg, $"Website map publish requested for render '{renderName}' at scale {scale:0.###}.");
            PublishMap("manual command", message => ReplyToCommand(arg, message), true, renderName, scale);
        }

        [Command("websitemapbridge.publish")]
        private void CovalencePublishCommand(IPlayer caller, string command, string[] args)
        {
            if (!CanUseCovalenceCommand(caller))
            {
                caller?.Reply("You must be server console or an admin to publish the website map.");
                return;
            }

            if (args != null && args.Length > 2)
            {
                caller?.Reply("Invalid syntax. Use websitemapbridge.publish <renderName:optional> <resolutionScale:optional>");
                return;
            }

            var renderName = args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : config.RenderName;
            var scale = config.ImageResolutionScale;

            if (args != null && args.Length > 1)
            {
                float parsedScale;

                if (!float.TryParse(args[1], out parsedScale) || parsedScale <= 0f)
                {
                    caller?.Reply("Invalid syntax. Use websitemapbridge.publish <renderName:optional> <resolutionScale:optional>");
                    return;
                }

                scale = parsedScale;
            }

            Puts($"Covalence command received: {command}");
            var requested = $"Website map publish requested for render '{renderName}' at scale {scale:0.###}.";
            Puts(requested);
            caller?.Reply(requested);
            PublishMap("manual Covalence command", message =>
            {
                Puts(message);
                caller?.Reply(message);
            }, true, renderName, scale);
        }

        private void StatusCommand(ConsoleSystem.Arg arg)
        {
            if (!CanUsePublishCommand(arg))
            {
                ReplyToCommand(arg, "You must be server console, RCON, or auth level 2 to inspect the website map bridge.");
                return;
            }

            ReplyToCommand(arg, "WebsiteMapBridge v1.0.25 status requested; reading cached diagnostics.");

            try
            {
                var secretState = string.IsNullOrWhiteSpace(ResolveBridgeSharedSecret()) ? "missing" : "configured";
                var apiState = RustMapApi == null ? "missing" : RustMapApi.IsLoaded ? "loaded" : "not loaded";
                var heightMapState = TerrainMeta.HeightMap == null ? "missing" : "available";

                ReplyToCommand(
                    arg,
                    $"WebsiteMapBridge v1.0.25 status: server={ResolveServerId()}, api={apiState}, ready=not-probed, secret={secretState}, render={config.RenderName}, textureRender={config.TextureRenderName}, autoPublishReady={config.AutoPublishOnRustMapApiReady}, autoPublishInit={config.AutoPublishOnServerInitialized}, terrainEnabled={config.PublishTerrain}, terrainResolution={config.TerrainSampleResolution}, monumentsEnabled={config.IncludeMonuments}, roads=enabled, powerLines=enabled, skybox={config.PublishSkybox}/{config.SkyboxImagePath}, playerLocations={config.PublishPlayerLocations}/{config.PlayerLocationIntervalSeconds}s, raidlandsEvents={config.PublishRaidlandsEvents}/{(RaidlandsEvents != null && RaidlandsEvents.IsLoaded ? "ready" : "missing")}, worldEvents={config.PublishWorldEvents}/{config.WorldEventIntervalSeconds}s movement={config.WorldEventMinimumMovementMeters:0.##}m rotation={config.WorldEventMinimumRotationDegrees:0.##}deg active={trackedWorldEntities.Values.Count(entry => entry.state != "ended")} discrete={discreteWorldEntities.Count} last={lastWorldEventSyncAt} accepted={lastWorldEventAccepted} inFlight={worldEventPublishInFlight}/{WorldEventPublishAgeSeconds():0.0}s error={lastWorldEventError}, environment={config.PublishEnvironment}/{config.EnvironmentIntervalSeconds}s last={FirstNonEmpty(lastEnvironmentSyncAt, "never")}, sampleMs={lastEnvironmentSampleMilliseconds}, sampled=[{lastEnvironmentDiagnosticSummary}], replayEvents={config.PublishReplayEvents}, heightMap={heightMapState}, lastWipe={lastPublishedWipeKey}."
                );
            }
            catch (Exception ex)
            {
                PrintError($"rl_map_status failed: {ex}");
                ReplyToCommand(arg, $"WebsiteMapBridge status failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        [Command("websitemapbridge.status", "rl_map_status")]
        private void CovalenceStatusCommand(IPlayer caller, string command, string[] args)
        {
            if (!CanUseCovalenceCommand(caller))
            {
                caller?.Reply("You must be server console or an admin to inspect the website map bridge.");
                return;
            }

            Puts($"Covalence command received: {command}");
            StatusCommand(null);
        }

        private void PlayerLocationsSyncCommand(ConsoleSystem.Arg arg)
        {
            if (!CanUsePublishCommand(arg))
            {
                ReplyToCommand(arg, "You must be server console, RCON, or auth level 2 to sync website player locations.");
                return;
            }

            ReplyToCommand(arg, "Website player location sync requested.");
            PublishPlayerLocations(message => ReplyToCommand(arg, message), true);
        }

        [Command("websitemapbridge.locations_sync", "rl_map_locations_sync")]
        private void CovalencePlayerLocationsSyncCommand(IPlayer caller, string command, string[] args)
        {
            if (!CanUseCovalenceCommand(caller))
            {
                caller?.Reply("You must be server console or an admin to sync website player locations.");
                return;
            }

            Puts($"Covalence command received: {command}");
            PlayerLocationsSyncCommand(null);
        }

        [Command("websitemapbridge.events_status", "rl_map_events_status")]
        private void CovalenceEventsStatusCommand(IPlayer caller, string command, string[] args)
        {
            if (!CanUseCovalenceCommand(caller))
            {
                caller?.Reply("You must be server console or an admin to inspect website map events.");
                return;
            }

            var message = BuildWorldEventStatus();
            Puts(message);
            caller?.Reply(message);
        }

        [Command("websitemapbridge.events_sync", "rl_map_events_sync")]
        private void CovalenceEventsSyncCommand(IPlayer caller, string command, string[] args)
        {
            if (!CanUseCovalenceCommand(caller))
            {
                caller?.Reply("You must be server console or an admin to sync website map events.");
                return;
            }

            Puts($"Covalence command received: {command}");
            SampleAndPublishWorldEvents(message =>
            {
                Puts(message);
                caller?.Reply(message);
            }, true);
        }

        [ConsoleCommand("rl_map_environment_sync")]
        private void EnvironmentSyncCommand(ConsoleSystem.Arg arg)
        {
            if (!CanUsePublishCommand(arg))
            {
                ReplyToCommand(arg, "You must be server console, RCON, or auth level 2 to sync website environment.");
                return;
            }

            ReplyToCommand(arg, "Website environment sync requested.");
            try
            {
                PublishEnvironment(message => ReplyToCommand(arg, message), true);
            }
            catch (Exception ex)
            {
                PrintError($"rl_map_environment_sync failed: {ex}");
                ReplyToCommand(arg, $"Website environment sync failed before enqueue: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool CanUsePublishCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null || arg.IsRcon || arg.IsAdmin)
            {
                return true;
            }

            return arg.Connection.authLevel >= 2;
        }

        private bool CanUseCovalenceCommand(IPlayer caller)
        {
            return caller == null || caller.IsServer || caller.IsAdmin;
        }

        private void ReplyToCommand(ConsoleSystem.Arg arg, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Puts(message);
            if (arg == null)
            {
                return;
            }

            try
            {
                arg.ReplyWith(message);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not send console command reply: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void QueueAutoPublish(string reason)
        {
            if (!config.AutoPublishOnRustMapApiReady)
            {
                return;
            }

            autoPublishTimer?.Destroy();
            autoPublishTimer = timer.Once(Math.Max(1, config.AutoPublishDelaySeconds), () =>
            {
                PublishMap(reason, message => Puts(message), false, config.RenderName, config.ImageResolutionScale);
            });
        }

        private void StartPlayerLocationPublisher()
        {
            playerLocationTimer?.Destroy();
            playerLocationTimer = null;

            if (!config.PublishPlayerLocations && !config.PublishRaidlandsEvents)
            {
                return;
            }

            var interval = Math.Max(5, config.PlayerLocationIntervalSeconds);
            playerLocationTimer = timer.Every(interval, () => PublishPlayerLocations(message => Puts(message), false));
            timer.Once(Math.Max(3, Math.Min(10, interval)), () => PublishPlayerLocations(message => Puts(message), false));
        }

        private void StartEnvironmentPublisher()
        {
            environmentTimer?.Destroy();
            environmentTimer = null;

            if (!config.PublishEnvironment)
            {
                return;
            }

            var interval = Math.Max(30, config.EnvironmentIntervalSeconds);
            environmentTimer = timer.Every(interval, () => PublishEnvironment(message => Puts(message), false));
            timer.Once(Math.Max(3, Math.Min(10, interval)), () => PublishEnvironment(message => Puts(message), false));
        }

        private void StartWorldEventPublisher()
        {
            worldEventTimer?.Destroy();
            worldEventTimer = null;

            if (!config.PublishReplayEvents || (!config.PublishWorldEvents && !config.PublishDiscreteServerEvents))
            {
                return;
            }

            DiscoverWorldEntities();
            var interval = Math.Max(3, config.WorldEventIntervalSeconds);
            worldEventTimer = timer.Every(interval, () => SampleAndPublishWorldEvents(message => Puts(message), false));
            timer.Once(Math.Max(2, Math.Min(5, interval)), () => SampleAndPublishWorldEvents(message => Puts(message), false));
        }

        private void PublishEnvironment(Action<string> reply, bool verbose)
        {
            var secret = ResolveBridgeSharedSecret();

            if (string.IsNullOrWhiteSpace(secret))
            {
                if (verbose)
                {
                    reply?.Invoke("Cannot sync environment because the bridge SharedSecret is empty after resolving secrets.");
                }
                return;
            }

            EnvironmentSnapshotPayload payload;
            var sampleTimer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                payload = CreateEnvironmentPayload();
                sampleTimer.Stop();
                lastEnvironmentSampleMilliseconds = sampleTimer.ElapsedMilliseconds;
                lastEnvironmentDiagnosticSummary = $"TOD {payload.rust_time:0.00}, cloud {FormatSample(payload.cloud_coverage)}, rain {FormatSample(payload.rain_intensity)}, fog {FormatSample(payload.fog_intensity)}; {payload.weather_sample_summary}";
                ReportWeatherOverrideMode(payload.weather);
            }
            catch (Exception ex)
            {
                sampleTimer.Stop();
                lastEnvironmentSampleMilliseconds = sampleTimer.ElapsedMilliseconds;
                PrintError($"Environment sampling failed after {lastEnvironmentSampleMilliseconds}ms: {ex}");
                if (verbose)
                {
                    reply?.Invoke($"Website environment sampling failed: {ex.GetType().Name}: {ex.Message}");
                }
                return;
            }

            var body = JsonConvert.SerializeObject(payload);
            var url = $"{TrimSlash(ResolveApiBaseUrl())}/api/server/environment-snapshot.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var requestError))
                {
                    if (verbose)
                    {
                        reply?.Invoke($"Website environment sync failed: {requestError}");
                    }
                    return;
                }

                EnvironmentSnapshotResponse result = null;

                try
                {
                    result = JsonConvert.DeserializeObject<EnvironmentSnapshotResponse>(response);
                }
                catch (Exception ex)
                {
                    if (verbose)
                    {
                        reply?.Invoke($"Website environment sync returned invalid JSON: {ex.Message}");
                    }
                    return;
                }

                if (result == null || !result.ok)
                {
                    if (verbose)
                    {
                        reply?.Invoke($"Website environment sync failed: {result?.error ?? "invalid response"}");
                    }
                    return;
                }

                lastEnvironmentSyncAt = result.environment?.sampledAt ?? payload.sampled_at;
                if (verbose)
                {
                    reply?.Invoke($"Website environment synced: TOD {payload.rust_time:0.00}, sun y {payload.sun_direction.y:0.###}, cloud {FormatSample(payload.cloud_coverage)}, rain {FormatSample(payload.rain_intensity)}, fog {FormatSample(payload.fog_intensity)}; {payload.weather_sample_summary}.");
                }
            });
        }

        private EnvironmentSnapshotPayload CreateEnvironmentPayload()
        {
            var rustTime = 12f;
            var sunDirection = new Vector3(0.5f, 0.78f, 0.36f).normalized;
            var sunIntensity = 1.65f;
            var ambientIntensity = 0.38f;
            var sunColor = "#ffc47a";
            var ambientColor = "#ffead2";
            object atmosphere = null;
            object weather = null;

            var sky = TOD_Sky.Instance;
            if (sky != null)
            {
                rustTime = Mathf.Clamp((float)sky.Cycle.Hour, 0f, 24f);
                atmosphere = ReadObjectProperty(sky, "Atmosphere");
                weather = ReadObjectProperty(sky, "Weather");
                var components = ReadObjectProperty(sky, "Components");
                var sunTransform = ReadObjectProperty(components, "SunTransform") as Transform;
                var sunLight = ReadObjectProperty(components, "SunLight") as Light;
                sunDirection = sunTransform != null ? (-sunTransform.forward).normalized : SunDirectionFromHour(rustTime);
                sunIntensity = Mathf.Clamp(sunLight != null ? sunLight.intensity : SunIntensityFromDirection(sunDirection), 0f, 4f);
                ambientIntensity = Mathf.Clamp(Mathf.Lerp(0.14f, 0.48f, Mathf.Clamp01(sunDirection.y)), 0f, 2f);
                sunColor = ColorToHex(sunLight != null ? sunLight.color : SunColorFromDirection(sunDirection));
                ambientColor = ColorToHex(RenderSettings.ambientLight == default(Color) ? new Color(1f, 0.92f, 0.82f) : RenderSettings.ambientLight);
            }
            else
            {
                sunDirection = SunDirectionFromHour(rustTime);
                sunIntensity = SunIntensityFromDirection(sunDirection);
                sunColor = ColorToHex(SunColorFromDirection(sunDirection));
            }

            var samplePositions = EnvironmentSamplePositions();
            var climate = ReadClimate();
            var weatherPreset = ReadObjectProperty(climate, "WeatherState");
            var weatherOverrides = ReadObjectProperty(climate, "WeatherOverrides");

            var cloudSample = FirstValidSample(
                SampleNativeClimate("Clouds", "Climate.GetClouds", samplePositions),
                SampleFloat(atmosphere, "Cloudiness", "TOD.Atmosphere.Cloudiness"),
                SampleFloat(atmosphere, "Clouds", "TOD.Atmosphere.Clouds"),
                SampleFloat(weather, "Cloudiness", "TOD.Weather.Cloudiness"),
                SampleFloat(weather, "CloudCoverage", "TOD.Weather.CloudCoverage")
            );
            var rainSample = FirstValidSample(
                SampleNativeClimate("Rain", "Climate.GetRain", samplePositions),
                SampleFloat(weather, "Rain", "TOD.Weather.Rain"),
                SampleFloat(weather, "RainIntensity", "TOD.Weather.RainIntensity"),
                SampleFloat(weather, "Precipitation", "TOD.Weather.Precipitation")
            );
            var fogSample = FirstValidSample(
                SampleNativeClimate("Fog", "Climate.GetFog", samplePositions),
                SampleRenderFogDensity()
            );

            var weatherSnapshot = CreateWeatherSnapshot(samplePositions, weatherPreset, weatherOverrides);
            weatherSnapshot.state = CreateWeatherStateDiagnostics(climate);

            return new EnvironmentSnapshotPayload
            {
                server_id = ResolveServerId(),
                wipe_key = ResolveWipeKey(),
                sampled_at = DateTime.UtcNow.ToString("o"),
                rust_time = (float)Math.Round(rustTime, 3),
                day_fraction = (float)Math.Round(Mathf.Repeat(rustTime / 24f, 1f), 6),
                sun_direction = new EnvironmentVectorPayload
                {
                    x = (float)Math.Round(sunDirection.x, 6),
                    y = (float)Math.Round(sunDirection.y, 6),
                    z = (float)Math.Round(sunDirection.z, 6)
                },
                sun_intensity = (float)Math.Round(sunIntensity, 4),
                sun_color = sunColor,
                ambient_intensity = (float)Math.Round(ambientIntensity, 4),
                ambient_color = ambientColor,
                cloud_coverage = cloudSample.value,
                rain_intensity = rainSample.value,
                fog_intensity = fogSample.value,
                weather_sample_summary = $"cloudSource={DescribeSample(cloudSample)}, rainSource={DescribeSample(rainSample)}, fogSource={DescribeSample(fogSample)}; {WeatherParameterSummary(weatherSnapshot)}; {WeatherStateSummary(weatherSnapshot.state)}",
                weather = weatherSnapshot
            };
        }

        private class SampledFloat
        {
            public float? value;
            public string source;
            public string raw;

            public SampledFloat(float? value, string source, string raw = "")
            {
                this.value = value;
                this.source = source;
                this.raw = raw;
            }
        }

        private WeatherSnapshotPayload CreateWeatherSnapshot(List<Vector3> samplePositions, object weatherPreset, object weatherOverrides)
        {
            var payload = new WeatherSnapshotPayload
            {
                parameters = new Dictionary<string, WeatherParameterPayload>()
            };

            foreach (var definition in WeatherParameterDefinitions)
            {
                payload.parameters[definition.name] = SampleWeatherParameter(definition, samplePositions, weatherPreset, weatherOverrides);
            }

            PopulateWeatherOverrideDiagnostics(payload);

            return payload;
        }

        private WeatherParameterPayload SampleWeatherParameter(WeatherParameterDefinition definition, List<Vector3> samplePositions, object weatherPreset, object weatherOverrides)
        {
            var rawValue = ReadWeatherOverrideValue(definition, weatherOverrides, out var rawSource);
            var effectiveValue = ReadEffectiveWeatherValue(definition, samplePositions, weatherPreset, out var effectiveSource);
            var sample = new WeatherParameterPayload
            {
                key = definition.key,
                value = null,
                raw = null,
                is_dynamic = false,
                source = FirstNonEmpty(effectiveSource, rawSource)
            };

            if (rawValue != null)
            {
                sample.raw = RoundWeatherValue(rawValue.Value);
                sample.is_dynamic = rawValue.Value < 0f;
            }

            if (effectiveValue != null)
            {
                sample.value = RoundWeatherValue(effectiveValue.Value);
                if (!string.IsNullOrWhiteSpace(rawSource) && rawSource != effectiveSource)
                {
                    sample.source = effectiveSource + " raw=" + rawSource;
                }
                return sample;
            }

            if (rawValue != null && rawValue.Value >= 0f)
            {
                sample.value = RoundWeatherValue(rawValue.Value);
            }

            return sample;
        }

        private float? ReadWeatherOverrideValue(WeatherParameterDefinition definition, object weatherOverrides, out string source)
        {
            source = "Climate.WeatherOverrides missing";
            if (definition == null)
            {
                return null;
            }

            if (weatherOverrides == null)
            {
                return ReadWeatherConVar(definition.member, out source);
            }

            try
            {
                object current = weatherOverrides;
                foreach (var memberName in definition.effectivePath)
                {
                    current = ReadObjectProperty(current, memberName);
                    if (current == null)
                    {
                        source = "Climate.WeatherOverrides." + string.Join(".", definition.effectivePath) + " missing";
                        return null;
                    }
                }

                if (TryConvertFloat(current, out var value))
                {
                    source = "Climate.WeatherOverrides." + string.Join(".", definition.effectivePath);
                    return value;
                }

                source = "Climate.WeatherOverrides." + string.Join(".", definition.effectivePath) + " nonnumeric";
                return null;
            }
            catch (Exception ex)
            {
                source = "Climate.WeatherOverrides." + definition.name + " error " + ex.GetType().Name;
                return null;
            }
        }

        private void PopulateWeatherOverrideDiagnostics(WeatherSnapshotPayload payload)
        {
            if (payload == null || payload.parameters == null)
            {
                return;
            }

            payload.parameter_count = payload.parameters.Count;
            var knownCount = 0;
            var overrideCount = 0;
            foreach (var parameter in payload.parameters.Values)
            {
                if (parameter?.raw == null)
                {
                    continue;
                }

                knownCount++;
                if (parameter.raw.Value >= 0f)
                {
                    overrideCount++;
                }
            }

            payload.override_count = overrideCount;
            if (knownCount == 0)
            {
                payload.override_mode = "unknown";
            }
            else if (overrideCount == 0 && knownCount == payload.parameter_count)
            {
                payload.override_mode = "dynamic";
            }
            else if (overrideCount == knownCount && knownCount == payload.parameter_count)
            {
                payload.override_mode = "forced";
            }
            else
            {
                payload.override_mode = "partial";
            }
        }

        private void ReportWeatherOverrideMode(WeatherSnapshotPayload weather)
        {
            if (weather == null)
            {
                return;
            }

            var signature = string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1}:{2}",
                FirstNonEmpty(weather.override_mode, "unknown"),
                weather.override_count,
                weather.parameter_count
            );
            if (signature == lastWeatherOverrideSignature)
            {
                return;
            }

            lastWeatherOverrideSignature = signature;
            var message = string.Format(
                CultureInfo.InvariantCulture,
                "Rust weather override mode is {0} ({1}/{2} tracked parameters overridden).",
                FirstNonEmpty(weather.override_mode, "unknown"),
                weather.override_count,
                weather.parameter_count
            );

            if (weather.override_mode == "forced" || weather.override_mode == "partial")
            {
                PrintWarning(message + " Run weather.reset from the server console to restore dynamic weather; WebsiteMapBridge does not alter weather.");
                return;
            }

            Puts(message);
        }

        private float? ReadEffectiveWeatherValue(WeatherParameterDefinition definition, List<Vector3> samplePositions, object weatherPreset, out string source)
        {
            source = "missing";

            try
            {
                switch (definition.name)
                {
                    case "rain":
                        source = "Climate.GetRain";
                        return SampleNativeClimate("Rain", source, samplePositions).value;
                    case "thunder":
                        source = "Climate.GetThunder";
                        return SampleNativeClimate("Thunder", source, samplePositions).value;
                    case "rainbow":
                        source = "Climate.GetRainbow";
                        return SampleNativeClimate("Rainbow", source, samplePositions).value;
                    case "fog":
                        source = "Climate.GetFog";
                        return SampleNativeClimate("Fog", source, samplePositions).value;
                }

                if (weatherPreset == null || definition.effectivePath == null || definition.effectivePath.Length == 0)
                {
                    source = "Climate.WeatherState missing";
                    return null;
                }

                object current = weatherPreset;
                foreach (var memberName in definition.effectivePath)
                {
                    current = ReadObjectProperty(current, memberName);
                    if (current == null)
                    {
                        source = "Climate.WeatherState." + string.Join(".", definition.effectivePath) + " missing";
                        return null;
                    }
                }

                if (TryConvertFloat(current, out var value))
                {
                    source = "Climate.WeatherState." + string.Join(".", definition.effectivePath);
                    return value;
                }

                source = "Climate.WeatherState." + string.Join(".", definition.effectivePath) + " nonnumeric";
                return null;
            }
            catch (Exception ex)
            {
                source = "Climate.WeatherState." + definition.name + " error " + ex.GetType().Name;
                return null;
            }
        }

        private WeatherStateDiagnosticsPayload CreateWeatherStateDiagnostics(object climate)
        {
            var diagnostics = new WeatherStateDiagnosticsPayload
            {
                source = climate == null ? "Climate missing" : "Climate"
            };

            if (climate == null)
            {
                return diagnostics;
            }

            diagnostics.previous = WeatherPresetLabel(ReadObjectProperty(climate, "WeatherStatePrevious"));
            diagnostics.current = WeatherPresetLabel(ReadObjectProperty(climate, "WeatherState"));
            diagnostics.target = WeatherPresetLabel(ReadObjectProperty(climate, "WeatherStateTarget"));
            diagnostics.next = WeatherPresetLabel(ReadObjectProperty(climate, "WeatherStateNext"));
            diagnostics.blend = ReadFloatProperty(climate, "WeatherStateBlend");
            diagnostics.seed_previous = ReadDiagnosticMemberString(climate, "WeatherSeedPrevious");
            diagnostics.seed_target = ReadDiagnosticMemberString(climate, "WeatherSeedTarget");
            diagnostics.seed_next = ReadDiagnosticMemberString(climate, "WeatherSeedNext");
            diagnostics.rain_grace_active = ReadBoolProperty(typeof(ConVar.Weather), "rain_grace_active");

            return diagnostics;
        }

        private string WeatherPresetLabel(object preset)
        {
            if (preset == null)
            {
                return "";
            }

            var name = Convert.ToString(ReadObjectProperty(preset, "name"), CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            var type = ReadObjectProperty(preset, "Type");
            if (type != null)
            {
                var typeName = Convert.ToString(ReadObjectProperty(type, "name"), CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    return typeName;
                }

                var typeText = type.ToString();
                if (!string.IsNullOrWhiteSpace(typeText))
                {
                    return typeText;
                }
            }

            return preset.ToString();
        }

        private string WeatherStateSummary(WeatherStateDiagnosticsPayload state)
        {
            if (state == null)
            {
                return "weatherState=unset";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "weatherState=previous={0},current={1},target={2},next={3},blend={4}",
                string.IsNullOrWhiteSpace(state.previous) ? "unset" : state.previous,
                string.IsNullOrWhiteSpace(state.current) ? "unset" : state.current,
                string.IsNullOrWhiteSpace(state.target) ? "unset" : state.target,
                string.IsNullOrWhiteSpace(state.next) ? "unset" : state.next,
                state.blend == null ? "unset" : state.blend.Value.ToString("0.###", CultureInfo.InvariantCulture)
            );
        }

        private object ReadClimate()
        {
            try
            {
                return UnityEngine.Object.FindFirstObjectByType<Climate>();
            }
            catch
            {
                return null;
            }
        }

        private float? ReadWeatherConVar(string memberName, out string source)
        {
            source = "missing";

            try
            {
                var member = GetWeatherConVarMember(memberName);
                if (member is FieldInfo field && TryConvertFloat(field.GetValue(null), out var fieldValue))
                {
                    source = "ConVar.Weather." + field.Name + " getter";
                    return fieldValue;
                }

                if (member is PropertyInfo property && TryConvertFloat(property.GetValue(null, null), out var propertyValue))
                {
                    source = "ConVar.Weather." + property.Name + " getter";
                    return propertyValue;
                }

                source = "ConVar.Weather." + memberName + " missing";
                return null;
            }
            catch (Exception ex)
            {
                source = "ConVar.Weather." + memberName + " error " + ex.GetType().Name;
                return null;
            }
        }

        private MemberInfo GetWeatherConVarMember(string memberName)
        {
            if (string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            MemberInfo member;
            if (weatherConVarMemberCache.TryGetValue(memberName, out member))
            {
                return member;
            }

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase;
            var weatherType = typeof(ConVar.Weather);
            member = (MemberInfo)weatherType.GetField(memberName, flags) ?? weatherType.GetProperty(memberName, flags);
            weatherConVarMemberCache[memberName] = member;
            return member;
        }

        private string WeatherParameterSummary(WeatherSnapshotPayload payload)
        {
            if (payload == null || payload.parameters == null || payload.parameters.Count == 0)
            {
                return "weatherParams=unset";
            }

            string[] names = { "rain", "thunder", "fog", "cloudCoverage", "atmosphereRayleigh", "atmosphereMie" };
            var parts = new List<string>();
            foreach (var name in names)
            {
                WeatherParameterPayload parameter;
                if (!payload.parameters.TryGetValue(name, out parameter) || parameter == null)
                {
                    continue;
                }

                if (parameter.raw == null)
                {
                    parts.Add(name + "=unset");
                }
                else if (parameter.is_dynamic)
                {
                    parts.Add(name + "=" + FormatSample(parameter.value) + "(dynamic raw " + parameter.raw.Value.ToString("0.###", CultureInfo.InvariantCulture) + ")");
                }
                else
                {
                    parts.Add(name + "=" + parameter.value.GetValueOrDefault().ToString("0.###"));
                }
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "weatherOverrides={0}({1}/{2}); weatherParams={3}",
                FirstNonEmpty(payload.override_mode, "unknown"),
                payload.override_count,
                payload.parameter_count,
                parts.Count == 0 ? "unset" : string.Join(",", parts.ToArray())
            );
        }

        private string FormatSample(float? value)
        {
            return value == null ? "unset" : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string DescribeSample(SampledFloat sample)
        {
            if (sample == null)
            {
                return "unset";
            }

            var value = sample.value == null ? "unset" : sample.value.Value.ToString("0.###", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(sample.raw)
                ? $"{sample.source}:{value}"
                : $"{sample.source}:{value} {sample.raw}";
        }

        private float RoundWeatherValue(float value)
        {
            return (float)Math.Round(value, 4);
        }

        private SampledFloat SampleNativeClimate(string sampleName, string source, List<Vector3> samplePositions = null)
        {
            try
            {
                var values = new List<float>();
                var rawValues = new List<float>();
                foreach (var position in samplePositions ?? EnvironmentSamplePositions())
                {
                    float value;
                    switch (sampleName)
                    {
                        case "Clouds":
                            value = Climate.GetClouds(position);
                            break;
                        case "Rain":
                            value = Climate.GetRain(position);
                            break;
                        case "Fog":
                            value = Climate.GetFog(position);
                            break;
                        case "Thunder":
                            value = Climate.GetThunder(position);
                            break;
                        case "Rainbow":
                            value = Climate.GetRainbow(position);
                            break;
                        default:
                            return new SampledFloat(null, source, "unknown sample");
                    }

                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        continue;
                    }

                    rawValues.Add(value);
                    if (value < 0f)
                    {
                        continue;
                    }

                    var normalizedValue = Mathf.Clamp01(value);
                    values.Add(normalizedValue);
                }

                if (values.Count == 0)
                {
                    return rawValues.Count == 0
                        ? new SampledFloat(null, source, "no numeric samples")
                        : new SampledFloat(null, source, "dynamic " + FormatRawStats(rawValues));
                }

                var average = values.Average();
                return new SampledFloat(RoundWeatherValue(Mathf.Clamp01(average)), source, FormatRawStats(rawValues));
            }
            catch (Exception ex)
            {
                return new SampledFloat(null, source, "error " + ex.GetType().Name);
            }
        }

        private List<Vector3> EnvironmentSamplePositions()
        {
            var positions = new List<Vector3>();

            try
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (positions.Count >= 24)
                    {
                        break;
                    }

                    if (player == null || !player.IsConnected || player.IsDead())
                    {
                        continue;
                    }

                    positions.Add(player.transform.position);
                }
            }
            catch
            {
                positions.Clear();
            }

            if (positions.Count == 0)
            {
                positions.Add(Vector3.zero);
            }

            return positions;
        }

        private SampledFloat FirstValidSample(params SampledFloat[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return new SampledFloat(null, "unset");
            }

            SampledFloat fallback = null;
            foreach (var sample in samples)
            {
                if (sample == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = sample;
                }

                if (sample.value != null)
                {
                    return sample;
                }
            }

            return fallback ?? new SampledFloat(null, "unset");
        }

        private SampledFloat SampleRenderFogDensity()
        {
            try
            {
                return NormalizeSample(RenderSettings.fogDensity * 100f, "RenderSettings.fogDensity");
            }
            catch (Exception ex)
            {
                return new SampledFloat(null, "RenderSettings.fogDensity", "error " + ex.GetType().Name);
            }
        }

        private SampledFloat SampleFloat(object instance, string propertyName, string source)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return new SampledFloat(null, source, "missing");
            }

            try
            {
                var rawValue = ReadMemberValue(instance, propertyName);
                return rawValue == null
                    ? new SampledFloat(null, source, "missing")
                    : NormalizeSample(rawValue, source);
            }
            catch (Exception ex)
            {
                return new SampledFloat(null, source, "error " + ex.GetType().Name);
            }
        }

        private SampledFloat NormalizeSample(object rawValue, string source)
        {
            if (!TryParseSampleFloat(rawValue, out var value, out var rawText))
            {
                return new SampledFloat(null, source, rawText);
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return new SampledFloat(null, source, rawText);
            }

            if (value < 0f)
            {
                return new SampledFloat(null, source, rawText);
            }

            return new SampledFloat(RoundWeatherValue(Mathf.Clamp01(value)), source, rawText);
        }

        private bool TryConvertFloat(object value, out float result)
        {
            return TryParseSampleFloat(value, out result, out _);
        }

        private bool TryParseSampleFloat(object value, out float result, out string rawText)
        {
            result = 0f;
            rawText = value == null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture);
            try
            {
                if (value == null)
                {
                    return false;
                }

                if (value is string stringValue)
                {
                    if (!float.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                        !float.TryParse(stringValue, NumberStyles.Float, CultureInfo.CurrentCulture, out result))
                    {
                        return false;
                    }

                    return !float.IsNaN(result) && !float.IsInfinity(result);
                }

                result = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                return !float.IsNaN(result) && !float.IsInfinity(result);
            }
            catch
            {
                return false;
            }
        }

        private string FormatRawStats(List<float> rawValues)
        {
            if (rawValues == null || rawValues.Count == 0)
            {
                return "raw=none";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "raw=avg={0:0.###} min={1:0.###} max={2:0.###} samples={3}",
                rawValues.Average(),
                rawValues.Min(),
                rawValues.Max(),
                rawValues.Count
            );
        }

        private Vector3 SunDirectionFromHour(float hour)
        {
            var angle = Mathf.Repeat((hour - 6f) / 24f, 1f) * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(angle) * 0.42f, Mathf.Sin(angle), Mathf.Sin(angle) * 0.58f).normalized;
        }

        private float SunIntensityFromDirection(Vector3 direction)
        {
            return Mathf.Lerp(0.05f, 1.85f, Mathf.Clamp01(direction.y));
        }

        private Color SunColorFromDirection(Vector3 direction)
        {
            return Color.Lerp(new Color(1f, 0.36f, 0.22f), new Color(1f, 0.78f, 0.48f), Mathf.Clamp01(direction.y));
        }

        private float? ReadFloatProperty(object instance, string propertyName)
        {
            var rawValue = ReadMemberValue(instance, propertyName);
            if (TryConvertFloat(rawValue, out var value))
            {
                return RoundWeatherValue(value);
            }

            return null;
        }

        private bool? ReadBoolProperty(object instance, string propertyName)
        {
            var rawValue = ReadMemberValue(instance, propertyName);
            if (rawValue == null)
            {
                return null;
            }

            try
            {
                return Convert.ToBoolean(rawValue, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private string ReadDiagnosticMemberString(object instance, string propertyName)
        {
            var rawValue = ReadMemberValue(instance, propertyName);
            if (rawValue == null)
            {
                return "";
            }

            try
            {
                return Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "";
            }
            catch
            {
                return rawValue.ToString();
            }
        }

        private object ReadObjectProperty(object instance, string propertyName)
        {
            return ReadMemberValue(instance, propertyName);
        }

        private object ReadMemberValue(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            try
            {
                var isStaticLookup = instance is Type;
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy |
                    (isStaticLookup ? BindingFlags.Static : BindingFlags.Instance);
                var type = isStaticLookup ? (Type)instance : instance.GetType();
                var target = isStaticLookup ? null : instance;
                var property = type.GetProperty(propertyName, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(target, null);
                }

                var field = type.GetField(propertyName, flags);
                if (field != null)
                {
                    return field.GetValue(target);
                }

                var method = type.GetMethod(propertyName, flags, null, Type.EmptyTypes, null)
                    ?? type.GetMethod("get_" + propertyName, flags, null, Type.EmptyTypes, null);
                return method == null ? null : method.Invoke(target, null);
            }
            catch
            {
                return null;
            }
        }

        private string ColorToHex(Color color)
        {
            color.r = Mathf.Clamp01(color.r);
            color.g = Mathf.Clamp01(color.g);
            color.b = Mathf.Clamp01(color.b);
            return $"#{ColorUtility.ToHtmlStringRGB(color).ToLowerInvariant()}";
        }

        private void PublishPlayerLocations(Action<string> reply, bool verbose)
        {
            var secret = ResolveBridgeSharedSecret();

            if (string.IsNullOrWhiteSpace(secret))
            {
                if (verbose)
                {
                    reply?.Invoke("Cannot sync player locations because the bridge SharedSecret is empty after resolving secrets.");
                }
                return;
            }

            PublishAnnouncedRaidlandsEvents(reply, verbose);

            if (!config.PublishPlayerLocations)
            {
                return;
            }

            var players = new List<PlayerLocationPayload>();

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || player.userID == 0)
                {
                    continue;
                }

                var position = player.transform.position;
                players.Add(new PlayerLocationPayload
                {
                    steam_id64 = player.UserIDString,
                    display_name = player.displayName ?? "",
                    clan_tag = ResolveClanTag(player),
                    x = (float)Math.Round(position.x, 3),
                    y = (float)Math.Round(position.y, 3),
                    z = (float)Math.Round(position.z, 3)
                });
            }

            if (players.Count == 0 && !verbose)
            {
                return;
            }

            var payload = new PlayerLocationSnapshotPayload
            {
                server_id = ResolveServerId(),
                wipe_key = ResolveWipeKey(),
                sampled_at = DateTime.UtcNow.ToString("o"),
                players = players
            };
            var body = JsonConvert.SerializeObject(payload);
            var url = $"{TrimSlash(ResolveApiBaseUrl())}/api/server/player-locations-snapshot.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var requestError))
                {
                    if (verbose)
                    {
                        reply?.Invoke($"Website player location sync failed: {requestError}");
                    }
                    return;
                }

                PlayerLocationSnapshotResponse result = null;

                try
                {
                    result = JsonConvert.DeserializeObject<PlayerLocationSnapshotResponse>(response);
                }
                catch (Exception ex)
                {
                    if (verbose)
                    {
                        reply?.Invoke($"Website player location sync returned invalid JSON: {ex.Message}");
                    }
                    return;
                }

                if (result == null || !result.ok)
                {
                    if (verbose)
                    {
                        reply?.Invoke($"Website player location sync failed: {result?.error ?? "invalid response"}");
                    }
                    return;
                }

                if (verbose)
                {
                    reply?.Invoke($"Website player locations synced: {result.locations?.acceptedPlayers ?? players.Count} connected players.");
                }
            });
        }

        private void DiscoverWorldEntities()
        {
            if ((!config.PublishWorldEvents && !config.PublishDiscreteServerEvents) || BaseNetworkable.serverEntities == null)
            {
                return;
            }

            foreach (var networkable in BaseNetworkable.serverEntities.ToList())
            {
                var entity = networkable as BaseEntity;
                TryTrackWorldEntity(entity, false);
                TryIndexDiscreteWorldEntity(entity);
            }
        }

        private void TryIndexDiscreteWorldEntity(BaseEntity entity)
        {
            if (!config.PublishReplayEvents || !config.PublishDiscreteServerEvents || entity == null || entity.IsDestroyed || entity.net == null)
            {
                return;
            }

            var prefab = (entity.PrefabName ?? entity.ShortPrefabName ?? "").ToLowerInvariant();
            if (!prefab.Contains("diesel_engine") && !prefab.Contains("dieselengine"))
            {
                return;
            }

            var monument = FindNearestMonumentContext(entity.transform.position);
            var family = monument.IndexOf("excavator", StringComparison.OrdinalIgnoreCase) >= 0
                ? "excavator"
                : monument.IndexOf("quarry", StringComparison.OrdinalIgnoreCase) >= 0 ? "quarry" : "";
            if (string.IsNullOrEmpty(family))
            {
                return;
            }

            discreteWorldEntities[entity.net.ID.Value] = new DiscreteWorldEntity
            {
                entity = entity,
                family = family,
                monument = monument
            };
        }

        private void TryTrackWorldEntity(BaseEntity entity, bool spawnedNow)
        {
            if (!config.PublishReplayEvents || !config.PublishWorldEvents || entity == null || entity.IsDestroyed || entity.net == null)
            {
                return;
            }

            WorldEntityDescriptor descriptor;
            if (!TryDescribeWorldEntity(entity, out descriptor))
            {
                trackedWorldEntities.Remove(entity.net.ID.Value);
                return;
            }

            TrackedWorldEntity tracked;
            var newlyTracked = false;
            if (!trackedWorldEntities.TryGetValue(entity.net.ID.Value, out tracked))
            {
                var now = DateTime.UtcNow;
                tracked = new TrackedWorldEntity
                {
                    networkId = entity.net.ID.Value,
                    eventKey = "world-entity:" + descriptor.eventType + ":" + entity.net.ID.Value.ToString(CultureInfo.InvariantCulture),
                    prefab = entity.PrefabName ?? entity.ShortPrefabName ?? "",
                    state = spawnedNow ? "spawned" : "active",
                    spawnedAt = now.ToString("o", CultureInfo.InvariantCulture),
                    sampledAt = now.ToString("o", CultureInfo.InvariantCulture),
                    descriptor = descriptor,
                    entity = entity,
                     monumentContext = FindNearestMonumentContext(entity.transform.position),
                     lastPosition = entity.transform.position,
                     lastRotation = entity.transform.rotation,
                     lastSampleUtc = now,
                    dirty = true
                };
                trackedWorldEntities[tracked.networkId] = tracked;
                newlyTracked = true;
                if (spawnedNow && config.PublishDiscreteServerEvents && descriptor.eventType == "ch47" && tracked.monumentContext.IndexOf("oil", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PublishDiscreteReplayEvent(
                        "oilrig-call:" + tracked.networkId.ToString(CultureInfo.InvariantCulture),
                        "oilrig_call",
                        entity.transform.position,
                        "ch47",
                        new Dictionary<string, object>
                        {
                            ["kind"] = "server_event",
                            ["eventType"] = "oilrig_call",
                            ["state"] = "called",
                            ["networkId"] = tracked.networkId.ToString(CultureInfo.InvariantCulture),
                            ["prefab"] = tracked.prefab,
                            ["assetKey"] = descriptor.assetKey,
                            ["monument"] = tracked.monumentContext
                        });
                }
            }
            else
            {
                tracked.entity = entity;
                tracked.descriptor = descriptor;
                tracked.prefab = entity.PrefabName ?? entity.ShortPrefabName ?? tracked.prefab;
            }

            if (newlyTracked)
            {
                SampleTrackedWorldEntity(tracked, DateTime.UtcNow);
            }
        }

        private bool TryDescribeWorldEntity(BaseEntity entity, out WorldEntityDescriptor descriptor)
        {
            descriptor = null;
            if (entity == null)
            {
                return false;
            }

            var prefab = (entity.PrefabName ?? entity.ShortPrefabName ?? "").ToLowerInvariant();
            var typeName = entity.GetType().Name.ToLowerInvariant();

            if (entity is BradleyAPC || prefab.Contains("bradleyapc") || prefab.Contains("m2bradley"))
            {
                descriptor = DescribeWorldEntity("bradley", "server_event", "bradley", "rust:bradley_apc");
            }
            else if (entity is PatrolHelicopter || prefab.Contains("patrolhelicopter") || prefab.Contains("patrol helicopter"))
            {
                descriptor = DescribeWorldEntity("patrol_heli", "server_event", "patrol_heli", "rust:patrol_helicopter");
            }
            else if (typeName.Contains("f15") || prefab.Contains("f15"))
            {
                descriptor = DescribeWorldEntity("f15", "server_event", "f15", "rust:f15");
            }
            else if (entity is CargoPlane || prefab.Contains("cargo_plane") || prefab.Contains("cargoplane") || prefab.Contains("cargo plane"))
            {
                descriptor = DescribeWorldEntity("cargo_plane", "server_event", "cargo_plane", "rust:cargo_plane");
            }
            else if (typeName.Contains("ch47") || prefab.Contains("/ch47/") || prefab.Contains("ch47scientists"))
            {
                descriptor = DescribeWorldEntity("ch47", "server_event", "ch47", "rust:ch47");
            }
            else if (entity is CargoShip || prefab.Contains("cargoship"))
            {
                descriptor = DescribeWorldEntity("cargo_ship", "cargo_ship", "cargo_ship", "rust:cargo_ship");
            }

            return descriptor != null;
        }

        private WorldEntityDescriptor DescribeWorldEntity(string eventType, string envelopeType, string vehicle, string assetKey)
        {
            return new WorldEntityDescriptor
            {
                eventType = eventType,
                envelopeType = envelopeType,
                vehicle = vehicle,
                assetKey = assetKey
            };
        }

        private void SampleAndPublishWorldEvents(Action<string> reply, bool verbose)
        {
            if (!config.PublishReplayEvents || (!config.PublishWorldEvents && !config.PublishDiscreteServerEvents))
            {
                if (verbose)
                {
                    reply?.Invoke("Website world-event publishing is disabled.");
                }
                return;
            }

            if (config.PublishDiscreteServerEvents)
            {
                SampleDiscreteEntityStates();
            }

            var now = DateTime.UtcNow;
            foreach (var tracked in trackedWorldEntities.Values.ToList())
            {
                if (tracked.state == "ended")
                {
                    continue;
                }

                if (tracked.entity == null || tracked.entity.IsDestroyed || tracked.entity.net == null)
                {
                    EndTrackedWorldEntity(tracked.networkId, "missing_from_server");
                    continue;
                }

                SampleTrackedWorldEntity(tracked, now);
            }

            PublishTrackedWorldEntities(reply, verbose);
        }

        private void SampleTrackedWorldEntity(TrackedWorldEntity tracked, DateTime now)
        {
            if (tracked == null || tracked.entity == null || tracked.entity.IsDestroyed)
            {
                return;
            }

            var position = tracked.entity.transform.position;
            var rotation = tracked.entity.transform.rotation;
            var firstSample = tracked.route.Count == 0;
            var moved = Vector3.Distance(position, tracked.lastPosition) >= config.WorldEventMinimumMovementMeters;
            var rotated = Quaternion.Angle(rotation, tracked.lastRotation) >= config.WorldEventMinimumRotationDegrees;
            if (!firstSample && !moved && !rotated)
            {
                return;
            }

            var elapsed = Math.Max(0.001, (now - tracked.lastSampleUtc).TotalSeconds);
            tracked.velocity = (position - tracked.lastPosition) / (float)elapsed;
            tracked.lastPosition = position;
            tracked.lastRotation = rotation;
            tracked.lastSampleUtc = now;
            tracked.sampledAt = now.ToString("o", CultureInfo.InvariantCulture);
            tracked.route.Add(new WorldRouteSample
            {
                timestampMilliseconds = new DateTimeOffset(now).ToUnixTimeMilliseconds(),
                position = position,
                rotation = rotation
            });
            CompactWorldRoute(tracked.route);
            tracked.dirty = true;
            tracked.revision++;
        }

        private void CompactWorldRoute(List<WorldRouteSample> route)
        {
            var maximum = Math.Max(12, Math.Min(160, config.WorldEventRouteMaxSamples));
            if (route == null || route.Count <= maximum)
            {
                return;
            }

            var recentCount = Math.Min(24, maximum / 2);
            var recentStart = Math.Max(0, route.Count - recentCount);
            var stride = 2;
            List<WorldRouteSample> compacted;
            do
            {
                compacted = new List<WorldRouteSample>();
                for (var index = 0; index < recentStart; index += stride)
                {
                    compacted.Add(route[index]);
                }
                for (var index = recentStart; index < route.Count; index++)
                {
                    compacted.Add(route[index]);
                }
                stride++;
            }
            while (compacted.Count > maximum && stride < 32);

            route.Clear();
            route.AddRange(compacted.Take(maximum));
        }

        private void EndTrackedWorldEntity(ulong networkId, string reason)
        {
            TrackedWorldEntity tracked;
            if (!trackedWorldEntities.TryGetValue(networkId, out tracked) || tracked.state == "ended")
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (tracked.entity != null && !tracked.entity.IsDestroyed)
            {
                SampleTrackedWorldEntity(tracked, now);
            }
            tracked.state = "ended";
            tracked.endedAt = now.ToString("o", CultureInfo.InvariantCulture);
            tracked.sampledAt = tracked.endedAt;
            tracked.endReason = string.IsNullOrWhiteSpace(reason) ? "ended" : reason;
            tracked.dirty = true;
            tracked.revision++;
        }

        private void PublishTrackedWorldEntities(Action<string> reply, bool verbose)
        {
            if (worldEventPublishInFlight)
            {
                var inFlightAgeSeconds = WorldEventPublishAgeSeconds();
                if (inFlightAgeSeconds <= WorldEventInFlightTimeoutSeconds())
                {
                    if (verbose)
                    {
                        reply?.Invoke($"Website world-event sync is already in flight ({inFlightAgeSeconds:0.0}s).");
                    }
                    return;
                }

                lastWorldEventError = $"abandoned stuck request after {inFlightAgeSeconds:0.0}s";
                PrintWarning("Website world-event sync watchdog released a stuck request: " + lastWorldEventError);
                worldEventPublishInFlight = false;
                worldEventPublishStartedUtc = DateTime.MinValue;
                activeWorldEventPublishGeneration = 0;
            }

            var secret = ResolveBridgeSharedSecret();
            if (string.IsNullOrWhiteSpace(secret))
            {
                lastWorldEventError = "shared secret missing";
                if (verbose)
                {
                    reply?.Invoke("Cannot sync website world events because the bridge SharedSecret is empty.");
                }
                return;
            }

            var trackedToPublish = trackedWorldEntities.Values.Where(entry => entry.dirty).Take(250).ToList();
            if (trackedToPublish.Count == 0)
            {
                if (verbose)
                {
                    reply?.Invoke("Website world-event sync found no changed tracked entities. " + BuildWorldEventStatus());
                }
                return;
            }

            var events = trackedToPublish.Select(CreateTrackedWorldReplayEvent).Where(entry => entry != null).ToList();
            if (events.Count == 0)
            {
                return;
            }

            var snapshot = new ReplayEventSnapshotPayload
            {
                server_id = ResolveServerId(),
                wipe_key = ResolveWipeKey(),
                events = events
            };
            var body = JsonConvert.SerializeObject(snapshot);
            var publishedRevisions = trackedToPublish.ToDictionary(entry => entry.networkId, entry => entry.revision);
            lastWorldEventPayloadBytes = Encoding.UTF8.GetByteCount(body);
            var url = $"{TrimSlash(ResolveApiBaseUrl())}/api/server/map-replay-events-snapshot.php";
            var publishGeneration = ++worldEventPublishGeneration;
            activeWorldEventPublishGeneration = publishGeneration;
            worldEventPublishStartedUtc = DateTime.UtcNow;
            worldEventPublishInFlight = true;
            SendPost(url, body, (code, response) =>
            {
                if (activeWorldEventPublishGeneration != publishGeneration)
                {
                    return;
                }
                worldEventPublishInFlight = false;
                worldEventPublishStartedUtc = DateTime.MinValue;
                activeWorldEventPublishGeneration = 0;
                int accepted;
                string requestError;
                if (!TryValidateReplayEventResponse(code, response, events.Count, out accepted, out requestError))
                {
                    lastWorldEventAccepted = accepted;
                    lastWorldEventError = requestError;
                    if (verbose)
                    {
                        reply?.Invoke("Website world-event sync failed: " + requestError);
                    }
                    return;
                }

                lastWorldEventAccepted = accepted;
                lastWorldEventError = "none";
                lastWorldEventSyncAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                foreach (var tracked in trackedToPublish)
                {
                    int publishedRevision;
                    if (!publishedRevisions.TryGetValue(tracked.networkId, out publishedRevision) || tracked.revision != publishedRevision)
                    {
                        continue;
                    }
                    tracked.dirty = false;
                    TrimAcknowledgedWorldRoute(tracked);
                    if (tracked.state == "spawned")
                    {
                        tracked.state = "active";
                        tracked.dirty = true;
                        tracked.revision++;
                    }
                    else if (tracked.state == "ended")
                    {
                        trackedWorldEntities.Remove(tracked.networkId);
                    }
                }

                if (verbose)
                {
                    reply?.Invoke($"Website world events synced: {accepted}/{events.Count} accepted. {BuildWorldEventStatus()}");
                }
            });
        }

        private void TrimAcknowledgedWorldRoute(TrackedWorldEntity tracked)
        {
            if (tracked == null || tracked.route.Count <= 1)
            {
                return;
            }

            var anchor = tracked.route[tracked.route.Count - 1];
            tracked.route.Clear();
            tracked.route.Add(anchor);
        }

        private ReplayEventPayload CreateTrackedWorldReplayEvent(TrackedWorldEntity tracked)
        {
            if (tracked == null || tracked.descriptor == null)
            {
                return null;
            }

            var rotation = tracked.entity != null && !tracked.entity.IsDestroyed
                ? tracked.entity.transform.rotation
                : tracked.route.Count == 0 ? Quaternion.identity : tracked.route[tracked.route.Count - 1].rotation;
            var payload = new Dictionary<string, object>
            {
                ["kind"] = "world_vehicle",
                ["eventType"] = tracked.descriptor.eventType,
                ["entityKey"] = tracked.eventKey,
                ["networkId"] = tracked.networkId.ToString(CultureInfo.InvariantCulture),
                ["prefab"] = tracked.prefab ?? "",
                ["assetKey"] = tracked.descriptor.assetKey,
                ["state"] = tracked.state,
                ["spawnedAt"] = tracked.spawnedAt ?? "",
                ["sampledAt"] = tracked.sampledAt ?? "",
                ["endedAt"] = tracked.endedAt ?? "",
                ["endReason"] = tracked.endReason ?? "",
                ["monument"] = tracked.monumentContext ?? "",
                ["position"] = VectorPayload(tracked.lastPosition),
                ["rotation"] = QuaternionPayload(rotation),
                ["velocity"] = VectorPayload(tracked.velocity),
                ["routeSchema"] = "unix_ms,x,y,z,qx,qy,qz,qw",
                ["route"] = tracked.route.Select(RoutePayload).ToList()
            };
            EnforceReplayPayloadLimit(payload);

            return new ReplayEventPayload
            {
                event_key = tracked.eventKey,
                event_type = tracked.descriptor.envelopeType,
                occurred_at = tracked.sampledAt,
                x = RoundWorldValue(tracked.lastPosition.x),
                y = RoundWorldValue(tracked.lastPosition.y),
                z = RoundWorldValue(tracked.lastPosition.z),
                vehicle = tracked.descriptor.vehicle,
                payload = payload
            };
        }

        private Dictionary<string, object> VectorPayload(Vector3 value)
        {
            return new Dictionary<string, object>
            {
                ["x"] = RoundWorldValue(value.x),
                ["y"] = RoundWorldValue(value.y),
                ["z"] = RoundWorldValue(value.z)
            };
        }

        private Dictionary<string, object> QuaternionPayload(Quaternion value)
        {
            return new Dictionary<string, object>
            {
                ["x"] = RoundWorldValue(value.x),
                ["y"] = RoundWorldValue(value.y),
                ["z"] = RoundWorldValue(value.z),
                ["w"] = RoundWorldValue(value.w)
            };
        }

        private object[] RoutePayload(WorldRouteSample sample)
        {
            return new object[]
            {
                sample.timestampMilliseconds,
                RoundWorldValue(sample.position.x), RoundWorldValue(sample.position.y), RoundWorldValue(sample.position.z),
                RoundWorldValue(sample.rotation.x), RoundWorldValue(sample.rotation.y), RoundWorldValue(sample.rotation.z), RoundWorldValue(sample.rotation.w)
            };
        }

        private float RoundWorldValue(float value)
        {
            return (float)Math.Round(value, 3);
        }

        private void EnforceReplayPayloadLimit(Dictionary<string, object> payload)
        {
            var maximumBytes = Math.Max(2000, Math.Min(11900, config.WorldEventPayloadMaxBytes));
            var route = payload["route"] as List<object[]>;
            while (route != null && route.Count > 2 && Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(payload)) > maximumBytes)
            {
                route.RemoveAt(route.Count > 24 ? 1 : 0);
            }
        }

        private double WorldEventPublishAgeSeconds()
        {
            return worldEventPublishStartedUtc == DateTime.MinValue
                ? (worldEventPublishInFlight ? double.PositiveInfinity : 0d)
                : Math.Max(0d, (DateTime.UtcNow - worldEventPublishStartedUtc).TotalSeconds);
        }

        private double WorldEventInFlightTimeoutSeconds()
        {
            var requestTimeoutSeconds = Math.Max(5d, config.WebRequestTimeoutMilliseconds / 1000d);
            return Math.Max(requestTimeoutSeconds + 15d, Math.Max(20d, config.WorldEventIntervalSeconds * 4d));
        }

        private bool TryValidateReplayEventResponse(int code, string response, int expected, out int accepted, out string error)
        {
            accepted = 0;
            if (!IsSuccess(code, response, out error))
            {
                return false;
            }

            try
            {
                var result = JsonConvert.DeserializeObject<ReplayEventSnapshotResponse>(response);
                accepted = result?.events?.acceptedEvents ?? 0;
                if (result == null || !result.ok)
                {
                    error = result?.error ?? "invalid replay response";
                    return false;
                }
                if (expected > 0 && accepted <= 0)
                {
                    error = $"HTTP {code} returned success but acceptedEvents=0 for {expected} submitted event(s)";
                    return false;
                }
                if (accepted < expected)
                {
                    error = $"website accepted only {accepted}/{expected} submitted event(s)";
                    return false;
                }
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = "invalid replay response JSON: " + ex.Message;
                return false;
            }
        }

        private string BuildWorldEventStatus()
        {
            var counts = trackedWorldEntities.Values
                .Where(entry => entry.state != "ended" && entry.descriptor != null)
                .GroupBy(entry => entry.descriptor.vehicle)
                .OrderBy(group => group.Key)
                .Select(group => group.Key + "=" + group.Count())
                .ToArray();
            var routeSamples = trackedWorldEntities.Values.Sum(entry => entry.route.Count);
            var assets = trackedWorldEntities.Values
                .Where(entry => entry.descriptor != null)
                .Select(entry => entry.descriptor.assetKey)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            return $"WebsiteMapBridge events: enabled={config.PublishWorldEvents}, active={trackedWorldEntities.Values.Count(entry => entry.state != "ended")}, endedPending={trackedWorldEntities.Values.Count(entry => entry.state == "ended")}, counts=[{string.Join(",", counts)}], routeSamples={routeSamples}, assets=[{string.Join(",", assets)}], inFlight={worldEventPublishInFlight}/{WorldEventPublishAgeSeconds():0.0}s, last={lastWorldEventSyncAt}, accepted={lastWorldEventAccepted}, payloadBytes={lastWorldEventPayloadBytes}, error={lastWorldEventError}.";
        }

        private void PublishAirdropReplayEvent(BaseEntity entity, bool landed)
        {
            var secret = ResolveBridgeSharedSecret();
            if (entity == null || entity.net == null || string.IsNullOrWhiteSpace(secret))
            {
                return;
            }

            var networkId = entity.net.ID.Value;
            var position = entity.transform.position;
            AirdropCorrelation correlation;
            if (!airdropCorrelations.TryGetValue(networkId, out correlation))
            {
                TrackedWorldEntity carrier;
                TryFindNearestCargoPlane(position, out carrier);
                correlation = new AirdropCorrelation
                {
                    carrierEntityKey = carrier?.eventKey ?? "",
                    carrierPrefab = carrier?.prefab ?? "",
                    vehicle = carrier == null ? "" : "cargo_plane",
                    assetKey = carrier == null ? "rust:supply_drop" : "rust:cargo_plane",
                    releasedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    releasePosition = position
                };
                airdropCorrelations[networkId] = correlation;
            }

            var occurredAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var eventPayload = new ReplayEventPayload
            {
                event_key = "airdrop:" + networkId.ToString(CultureInfo.InvariantCulture),
                event_type = "airdrop",
                occurred_at = occurredAt,
                x = RoundWorldValue(position.x),
                y = RoundWorldValue(position.y),
                z = RoundWorldValue(position.z),
                vehicle = correlation.vehicle,
                payload = new Dictionary<string, object>
                {
                    ["kind"] = "airdrop",
                    ["eventType"] = landed ? "airdrop_landed" : "airdrop_released",
                    ["state"] = landed ? "landed" : "released",
                    ["prefab"] = entity.PrefabName ?? entity.ShortPrefabName ?? "",
                    ["assetKey"] = correlation.assetKey,
                    ["networkId"] = networkId.ToString(CultureInfo.InvariantCulture),
                    ["dropEntityKey"] = "supply-drop:" + networkId.ToString(CultureInfo.InvariantCulture),
                    ["carrierEntityKey"] = correlation.carrierEntityKey ?? "",
                    ["carrierPrefab"] = correlation.carrierPrefab ?? "",
                    ["releasedAt"] = correlation.releasedAt,
                    ["releasePosition"] = VectorPayload(correlation.releasePosition),
                    ["landedAt"] = landed ? occurredAt : "",
                    ["landingPosition"] = landed ? VectorPayload(position) : null,
                    ["noPlane"] = string.IsNullOrWhiteSpace(correlation.carrierEntityKey)
                }
            };
            PublishReplayEvents(new List<ReplayEventPayload> { eventPayload }, "airdrop", null, false);
            if (landed)
            {
                airdropCorrelations.Remove(networkId);
            }
        }

        private bool TryFindNearestCargoPlane(Vector3 position, out TrackedWorldEntity nearest)
        {
            nearest = null;
            var maximumDistance = Math.Max(50f, config.AirdropCarrierSearchRadiusMeters);
            var bestDistance = maximumDistance;
            foreach (var candidate in trackedWorldEntities.Values)
            {
                if (candidate.state == "ended" || candidate.descriptor == null || candidate.descriptor.vehicle != "cargo_plane")
                {
                    continue;
                }
                var distance = Vector3.Distance(position, candidate.lastPosition);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    nearest = candidate;
                }
            }
            return nearest != null;
        }

        private void PublishCrateHackEvent(HackableLockedCrate crate, bool completed)
        {
            if (!config.PublishReplayEvents || !config.PublishDiscreteServerEvents || crate == null || crate.net == null)
            {
                return;
            }

            var position = crate.transform.position;
            var context = FindNearestMonumentContext(position);
            var eventType = completed ? "crate_hack_complete" : "crate_hack_start";
            var eventPayload = new Dictionary<string, object>
            {
                ["kind"] = "server_event",
                ["eventType"] = eventType,
                ["state"] = completed ? "completed" : "active",
                ["networkId"] = crate.net.ID.Value.ToString(CultureInfo.InvariantCulture),
                ["prefab"] = crate.PrefabName ?? crate.ShortPrefabName ?? "",
                ["monument"] = context
            };
            PublishDiscreteReplayEvent("crate-hack:" + crate.net.ID.Value.ToString(CultureInfo.InvariantCulture), eventType, position, "", eventPayload);

            if (context.IndexOf("oil", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var oilRigType = completed ? "oilrig_stop" : "oilrig_start";
                var oilPayload = new Dictionary<string, object>(eventPayload)
                {
                    ["eventType"] = oilRigType,
                    ["sourceEventType"] = eventType
                };
                PublishDiscreteReplayEvent("oilrig-activity:" + crate.net.ID.Value.ToString(CultureInfo.InvariantCulture), oilRigType, position, "", oilPayload);
            }
        }

        private void SampleDiscreteEntityStates()
        {
            foreach (var entry in discreteWorldEntities.ToList())
            {
                var networkId = entry.Key;
                var indexed = entry.Value;
                var entity = indexed?.entity;
                if (entity == null || entity.IsDestroyed || entity.net == null)
                {
                    discreteWorldEntities.Remove(networkId);
                    discreteEntityStates.Remove(networkId);
                    continue;
                }

                var isOn = entity.HasFlag(BaseEntity.Flags.On);
                bool previous;
                if (!discreteEntityStates.TryGetValue(networkId, out previous))
                {
                    discreteEntityStates[networkId] = isOn;
                    if (!isOn)
                    {
                        continue;
                    }
                }
                else if (previous == isOn)
                {
                    continue;
                }
                else
                {
                    discreteEntityStates[networkId] = isOn;
                }

                var family = indexed.family;
                var eventType = family + (isOn ? "_start" : "_stop");
                PublishDiscreteReplayEvent(
                    "industrial:" + family + ":" + networkId.ToString(CultureInfo.InvariantCulture),
                    eventType,
                    entity.transform.position,
                    "",
                    new Dictionary<string, object>
                    {
                        ["kind"] = "server_event",
                        ["eventType"] = eventType,
                        ["state"] = isOn ? "active" : "ended",
                        ["networkId"] = networkId.ToString(CultureInfo.InvariantCulture),
                        ["prefab"] = entity.PrefabName ?? entity.ShortPrefabName ?? "",
                        ["monument"] = indexed.monument
                    });
            }
        }

        private string FindNearestMonumentContext(Vector3 position)
        {
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null)
            {
                return "";
            }

            MonumentInfo nearest = null;
            var bestDistance = 600f;
            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || monument.transform == null)
                {
                    continue;
                }
                var distance = Vector3.Distance(position, monument.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = monument;
                }
            }
            if (nearest == null)
            {
                return "";
            }
            return MonumentDisplayName(nearest, ShortPrefabName(nearest.name));
        }

        private void PublishDiscreteReplayEvent(string eventKey, string eventType, Vector3 position, string vehicle, Dictionary<string, object> eventPayload)
        {
            PublishReplayEvents(new List<ReplayEventPayload>
            {
                new ReplayEventPayload
                {
                    event_key = eventKey,
                    event_type = eventType,
                    occurred_at = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    x = RoundWorldValue(position.x),
                    y = RoundWorldValue(position.y),
                    z = RoundWorldValue(position.z),
                    vehicle = vehicle ?? "",
                    payload = eventPayload ?? new Dictionary<string, object>()
                }
            }, eventType, null, false);
        }

        private void PublishReplayEvents(List<ReplayEventPayload> events, string label, Action<string> reply, bool verbose)
        {
            if (events == null || events.Count == 0 || string.IsNullOrWhiteSpace(ResolveBridgeSharedSecret()))
            {
                return;
            }

            var submitted = events.Take(250).ToList();
            var snapshot = new ReplayEventSnapshotPayload
            {
                server_id = ResolveServerId(),
                wipe_key = ResolveWipeKey(),
                events = submitted
            };
            var body = JsonConvert.SerializeObject(snapshot);
            var url = $"{TrimSlash(ResolveApiBaseUrl())}/api/server/map-replay-events-snapshot.php";
            SendPost(url, body, (code, response) =>
            {
                int accepted;
                string requestError;
                if (!TryValidateReplayEventResponse(code, response, submitted.Count, out accepted, out requestError))
                {
                    lastWorldEventError = requestError;
                    PrintWarning("Website " + label + " replay event post failed: " + requestError);
                    if (verbose)
                    {
                        reply?.Invoke("Website " + label + " event sync failed: " + requestError);
                    }
                    return;
                }

                lastWorldEventAccepted = accepted;
                lastWorldEventError = "none";
                lastWorldEventSyncAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                if (verbose)
                {
                    reply?.Invoke($"Website {label} events synced: {accepted}/{submitted.Count} accepted.");
                }
            });
        }

        private void PublishAnnouncedRaidlandsEvents(Action<string> reply, bool verbose)
        {
            if (!config.PublishReplayEvents || !config.PublishRaidlandsEvents || RaidlandsEvents == null || !RaidlandsEvents.IsLoaded)
            {
                if (verbose && config.PublishRaidlandsEvents && (RaidlandsEvents == null || !RaidlandsEvents.IsLoaded))
                {
                    reply?.Invoke("Cannot sync Raidlands events because RaidlandsEvents is not loaded.");
                }
                return;
            }

            object result;
            try
            {
                result = RaidlandsEvents.Call("API_GetAnnouncedActiveEvents");
            }
            catch (Exception ex)
            {
                if (verbose)
                {
                    reply?.Invoke($"Website Raidlands event sync failed while reading RaidlandsEvents: {ex.Message}");
                }
                return;
            }

            if (result == null)
            {
                if (verbose)
                {
                    reply?.Invoke("Website Raidlands event sync received no response from RaidlandsEvents.");
                }
                return;
            }

            JArray eventsToken;
            try
            {
                eventsToken = JArray.FromObject(result);
            }
            catch (Exception ex)
            {
                if (verbose)
                {
                    reply?.Invoke($"Website Raidlands event sync received an invalid event list: {ex.Message}");
                }
                return;
            }

            var now = DateTime.UtcNow.ToString("o");
            var events = new List<ReplayEventPayload>();
            foreach (var token in eventsToken.OfType<JObject>())
            {
                var instanceId = token.Value<string>("instanceId");
                var x = token.Value<float?>("x");
                var z = token.Value<float?>("z");
                if (string.IsNullOrWhiteSpace(instanceId) || !x.HasValue || !z.HasValue)
                {
                    continue;
                }

                events.Add(new ReplayEventPayload
                {
                    event_key = "raidlands-event:" + instanceId,
                    event_type = "server_event",
                    occurred_at = now,
                    x = x.Value,
                    y = token.Value<float?>("y") ?? 0f,
                    z = z.Value,
                    vehicle = "",
                    payload = new Dictionary<string, object>
                    {
                        ["kind"] = "server_event",
                        ["eventType"] = "raid_base",
                        ["instanceId"] = instanceId,
                        ["eventTypeId"] = token.Value<string>("eventTypeId") ?? "raid-base",
                        ["publicName"] = token.Value<string>("publicName") ?? "Public Raid Base",
                        ["layoutId"] = token.Value<string>("layoutId") ?? "",
                        ["radiusMeters"] = token.Value<float?>("radiusMeters") ?? 90f,
                        ["startedAt"] = token.Value<string>("startedAt") ?? "",
                        ["expiresAt"] = token.Value<string>("expiresAt") ?? "",
                        ["active"] = true
                    }
                });
            }

            if (events.Count == 0)
            {
                if (verbose)
                {
                    reply?.Invoke("Website Raidlands events synced: no announced active events.");
                }
                return;
            }

            PublishReplayEvents(events, "Raidlands", reply, verbose);
        }

        private string ResolveClanTag(BasePlayer player)
        {
            if (player == null || Clans == null || !Clans.IsLoaded)
            {
                return "";
            }

            try
            {
                var byString = Clans.Call("GetClanOf", player.UserIDString);
                var tag = ClanTagFromResult(byString);

                if (!string.IsNullOrWhiteSpace(tag))
                {
                    return tag;
                }

                var byId = Clans.Call("GetClanOf", player.userID);
                return ClanTagFromResult(byId);
            }
            catch
            {
                return "";
            }
        }

        private string ClanTagFromResult(object result)
        {
            if (result == null)
            {
                return "";
            }

            if (result is string text)
            {
                return text.Trim();
            }

            var token = result as JObject;
            if (token != null)
            {
                return FirstNonEmpty(token.Value<string>("tag"), token.Value<string>("Tag"), token.Value<string>("clan_tag"));
            }

            return "";
        }

        private void PublishMap(string reason, Action<string> reply, bool force, string renderName, float scale)
        {
            if (publishInFlight)
            {
                reply?.Invoke("Website map publish is already in progress.");
                return;
            }

            if (!CanPublish(out var error))
            {
                reply?.Invoke(error);
                return;
            }

            var wipeStartedAt = GetWipeStartedAt();
            var wipeKey = ResolveWipeKey(wipeStartedAt);

            if (!force && string.Equals(lastPublishedWipeKey, wipeKey, StringComparison.OrdinalIgnoreCase))
            {
                reply?.Invoke($"Website map already published for wipe key {wipeKey}.");
                return;
            }

            publishInFlight = true;
            renderName = string.IsNullOrWhiteSpace(renderName) ? config.RenderName : renderName.Trim();
            scale = Math.Max(0.05f, scale);

            try
            {
                var resolution = Math.Max(64, (int)Math.Round(World.Size * scale));
                var encoding = GetEncodingMode();
                var mapObject = RustMapApi.Call("CreatePluginImage", this, renderName, resolution, encoding);

                if (!(mapObject is Hash<string, object> map))
                {
                    publishInFlight = false;
                    reply?.Invoke($"RustMapApi could not render {renderName}: {mapObject ?? "empty response"}");
                    return;
                }

                var image = map["image"] as byte[];

                if (image == null || image.Length == 0)
                {
                    publishInFlight = false;
                    reply?.Invoke($"RustMapApi returned an empty {renderName} image.");
                    return;
                }

                var textureRenderName = string.IsNullOrWhiteSpace(config.TextureRenderName) ? "Default" : config.TextureRenderName.Trim();
                var textureImage = RenderTextureImage(textureRenderName, renderName, resolution, encoding, out textureRenderName);
                var skyboxImage = LoadSkyboxImage(out var skyboxSummary);

                var payload = new MapUploadPayload
                {
                    server_id = ResolveServerId(),
                    wipe_key = wipeKey,
                    wipe_started_at = wipeStartedAt == DateTime.MinValue ? null : wipeStartedAt.ToString("o"),
                    map_name = GetMapDisplayName(),
                    render_name = renderName,
                    file_type = encoding == EncodingPng ? "Png" : "Jpg",
                    image_base64 = Convert.ToBase64String(image),
                    image_sha256 = Sha256Bytes(image),
                    texture_render_name = textureRenderName,
                    texture_image_base64 = textureImage != null && textureImage.Length > 0 ? Convert.ToBase64String(textureImage) : "",
                    texture_image_sha256 = textureImage != null && textureImage.Length > 0 ? Sha256Bytes(textureImage) : "",
                    skybox_image_base64 = skyboxImage != null && skyboxImage.Length > 0 ? Convert.ToBase64String(skyboxImage) : "",
                    skybox_image_sha256 = skyboxImage != null && skyboxImage.Length > 0 ? Sha256Bytes(skyboxImage) : "",
                    image_width = Convert.ToInt32(map["width"]),
                    image_height = Convert.ToInt32(map["height"]),
                    resolution = resolution,
                    world_size = Math.Max(0, ConVar.Server.worldsize),
                    seed = Math.Max(0, ConVar.Server.seed),
                    protocol = Rust.Protocol.network,
                    generated_at = DateTime.UtcNow.ToString("o")
                };
                payload.terrain = CreateTerrainPayload(payload.server_id, wipeKey, payload.map_name, payload.generated_at, out var terrainSummary);

                var body = JsonConvert.SerializeObject(payload);
                var url = $"{TrimSlash(ResolveApiBaseUrl())}/api/server/map-upload.php";

                var textureSummary = textureImage != null && textureImage.Length > 0 ? $", texture {textureRenderName} {textureImage.Length} bytes" : "";
                Puts($"Publishing {renderName} map to website ({payload.image_width}x{payload.image_height}, {image.Length} bytes{textureSummary}, {terrainSummary}, {skyboxSummary}) after {reason}.");
                SendPost(url, body, (code, response) =>
                {
                    publishInFlight = false;

                    if (!IsSuccess(code, response, out var requestError))
                    {
                        reply?.Invoke($"Website map publish failed: {requestError}");
                        return;
                    }

                    MapUploadResponse result = null;

                    try
                    {
                        result = JsonConvert.DeserializeObject<MapUploadResponse>(response);
                    }
                    catch (Exception ex)
                    {
                        reply?.Invoke($"Website map publish returned invalid JSON: {ex.Message}");
                        return;
                    }

                    if (result == null || !result.ok)
                    {
                        reply?.Invoke($"Website map publish failed: {result?.error ?? "invalid response"}");
                        return;
                    }

                    lastPublishedWipeKey = wipeKey;
                    var publicUrl = FirstNonEmpty(result.map?.url, result.map?.publicUrl, result.url);
                    var textureUrl = result.map?.textureUrl ?? "";
                    var terrainUrl = result.map?.terrainUrl ?? "";
                    var skyboxUrl = result.map?.skyboxUrl ?? "";

                    if (payload.terrain != null && string.IsNullOrWhiteSpace(terrainUrl))
                    {
                        reply?.Invoke($"Website map published: {publicUrl}. Terrain was sent, but the website response did not include a terrain URL; confirm database/migrations/050_server_map_terrain.sql and the website upload files are live.");
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(terrainUrl))
                    {
                        var terrainResolution = result.map?.terrainResolution ?? payload.terrain?.resolution ?? 0;
                        var textureMessage = string.IsNullOrWhiteSpace(textureUrl) ? "" : $" texture: {textureUrl};";
                        var skyboxMessage = string.IsNullOrWhiteSpace(skyboxUrl) ? "" : $" skybox: {skyboxUrl};";
                        reply?.Invoke($"Website map published: {publicUrl};{textureMessage}{skyboxMessage} terrain {terrainResolution}x{terrainResolution}: {terrainUrl}");
                        return;
                    }

                    reply?.Invoke($"Website map published: {publicUrl}");
                });
            }
            catch (Exception ex)
            {
                publishInFlight = false;
                reply?.Invoke($"Website map publish failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool CanPublish(out string error)
        {
            if (RustMapApi == null || !RustMapApi.IsLoaded)
            {
                error = "Cannot publish map because RustMapApi is not loaded.";
                return false;
            }

            if (!RustMapApi.Call<bool>("IsReady"))
            {
                error = "Cannot publish map because RustMapApi is not ready yet.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ResolveBridgeSharedSecret()))
            {
                error = "Cannot publish map because the bridge SharedSecret is empty after resolving secrets.";
                return false;
            }

            error = "";
            return true;
        }

        private byte[] RenderTextureImage(string configuredRenderName, string publicRenderName, int resolution, int encoding, out string usedRenderName)
        {
            usedRenderName = configuredRenderName;
            var candidates = new List<string>();

            AddRenderCandidate(candidates, configuredRenderName);
            AddRenderCandidate(candidates, "Default");

            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate, publicRenderName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var textureObject = RustMapApi.Call("CreatePluginImage", this, candidate, resolution, encoding);

                if (textureObject is Hash<string, object> textureMap)
                {
                    var image = textureMap["image"] as byte[];

                    if (image != null && image.Length > 0)
                    {
                        usedRenderName = candidate;
                        return image;
                    }
                }

                PrintWarning($"RustMapApi returned an empty {candidate} texture render.");
            }

            PrintWarning($"Terrain viewer texture render failed; it will fall back to {publicRenderName}.");
            return null;
        }

        private static void AddRenderCandidate(List<string> candidates, string renderName)
        {
            renderName = (renderName ?? "").Trim();

            if (renderName.Length == 0 || candidates.Any(existing => existing.Equals(renderName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            candidates.Add(renderName);
        }

        private byte[] LoadSkyboxImage(out string summary)
        {
            if (!config.PublishSkybox)
            {
                summary = "skybox disabled";
                return null;
            }

            var configuredPath = (config.SkyboxImagePath ?? "").Trim();

            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                summary = "skybox skipped: no path";
                return null;
            }

            var path = ResolveServerPath(configuredPath);

            if (!File.Exists(path))
            {
                summary = $"skybox skipped: missing {configuredPath}";
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                summary = bytes.Length > 0 ? $"skybox {configuredPath} {bytes.Length} bytes" : $"skybox skipped: empty {configuredPath}";
                return bytes.Length > 0 ? bytes : null;
            }
            catch (Exception ex)
            {
                summary = $"skybox skipped: {ex.Message}";
                return null;
            }
        }

        private static string ResolveServerPath(string path)
        {
            path = (path ?? "").Trim().Replace('/', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.Combine(Interface.Oxide.RootDirectory, path);
        }

        private TerrainUploadPayload CreateTerrainPayload(string serverId, string wipeKey, string mapName, string generatedAt, out string summary)
        {
            if (!config.PublishTerrain)
            {
                summary = "terrain disabled";
                return null;
            }

            if (TerrainMeta.HeightMap == null)
            {
                summary = "terrain skipped: heightmap unavailable";
                PrintWarning("Terrain export skipped because TerrainMeta.HeightMap is not available.");
                return null;
            }

            var resolution = Mathf.Clamp(config.TerrainSampleResolution, 17, 257);

            if (resolution % 2 == 0)
            {
                resolution += 1;
            }

            var worldSize = Math.Max(1, ConVar.Server.worldsize);
            var half = worldSize * 0.5f;
            var heights = new List<float>(resolution * resolution);
            var colors = config.IncludeTerrainColors ? new List<string>(resolution * resolution) : null;
            var minHeight = float.MaxValue;
            var maxHeight = float.MinValue;
            var waterLevel = EstimateOceanWaterLevel(worldSize, resolution);

            for (var row = 0; row < resolution; row++)
            {
                var v = resolution <= 1 ? 0f : row / (float)(resolution - 1);
                var z = half - (v * worldSize);

                for (var col = 0; col < resolution; col++)
                {
                    var u = resolution <= 1 ? 0f : col / (float)(resolution - 1);
                    var x = -half + (u * worldSize);
                    var position = new Vector3(x, 0f, z);
                    var height = TerrainMeta.HeightMap.GetHeight(position);

                    var sampleWaterLevel = TerrainMeta.WaterMap != null ? TerrainMeta.WaterMap.GetHeight(position) : 0f;

                    minHeight = Math.Min(minHeight, height);
                    maxHeight = Math.Max(maxHeight, height);
                    heights.Add((float)Math.Round(height, 3));

                    if (colors != null)
                    {
                        colors.Add(TerrainColor(position, height, sampleWaterLevel));
                    }
                }
            }

            if (minHeight == float.MaxValue)
            {
                minHeight = 0f;
            }

            if (maxHeight == float.MinValue)
            {
                maxHeight = 0f;
            }

            var monuments = CreateMonumentPayloads();
            var roads = CreateRoadPayloads();
            var powerLines = CreatePowerLinePayloads();
            summary = $"terrain {resolution}x{resolution}, height {minHeight:0.###}-{maxHeight:0.###}, ocean {waterLevel:0.###}, monuments {monuments.Count}, roads {roads.Count}, power lines {powerLines.Count}";

            return new TerrainUploadPayload
            {
                serverId = serverId,
                wipeKey = wipeKey,
                mapName = mapName,
                resolution = resolution,
                worldSize = worldSize,
                seed = Math.Max(0, ConVar.Server.seed),
                waterLevel = (float)Math.Round(waterLevel, 3),
                minHeight = (float)Math.Round(minHeight, 3),
                maxHeight = (float)Math.Round(maxHeight, 3),
                generatedAt = generatedAt,
                heights = heights,
                colors = colors,
                monuments = monuments,
                roads = roads,
                powerLines = powerLines
            };
        }

        private float EstimateOceanWaterLevel(float worldSize, int resolution)
        {
            if (TerrainMeta.WaterMap == null)
            {
                return 0f;
            }

            var half = worldSize * 0.5f;
            var samples = new List<float>();
            var edgeSamples = Mathf.Clamp(resolution, 17, 129);

            for (var index = 0; index < edgeSamples; index++)
            {
                var t = edgeSamples <= 1 ? 0f : index / (float)(edgeSamples - 1);
                var coord = -half + (t * worldSize);
                AddWaterLevelSample(samples, new Vector3(coord, 0f, half));
                AddWaterLevelSample(samples, new Vector3(coord, 0f, -half));
                AddWaterLevelSample(samples, new Vector3(-half, 0f, coord));
                AddWaterLevelSample(samples, new Vector3(half, 0f, coord));
            }

            if (samples.Count == 0)
            {
                return 0f;
            }

            samples.Sort();
            return samples[samples.Count / 2];
        }

        private void AddWaterLevelSample(List<float> samples, Vector3 position)
        {
            var waterLevel = TerrainMeta.WaterMap.GetHeight(position);

            if (!float.IsNaN(waterLevel) && !float.IsInfinity(waterLevel))
            {
                samples.Add(waterLevel);
            }
        }

        private List<MonumentUploadPayload> CreateMonumentPayloads()
        {
            var monuments = new List<MonumentUploadPayload>();

            if (!config.IncludeMonuments || TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null)
            {
                return monuments;
            }

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || monument.transform == null)
                {
                    continue;
                }

                var position = monument.transform.position;
                var prefab = ShortPrefabName(monument.name);
                var name = MonumentDisplayName(monument, prefab);

                monuments.Add(new MonumentUploadPayload
                {
                    name = name,
                    prefab = prefab,
                    kind = MonumentKind(name, prefab),
                    x = (float)Math.Round(position.x, 3),
                    y = (float)Math.Round(position.y, 3),
                    z = (float)Math.Round(position.z, 3),
                    radius = (float)Math.Round(MonumentRadius(prefab), 3),
                    rotationY = (float)Math.Round(monument.transform.rotation.eulerAngles.y, 3)
                });
            }

            return monuments;
        }

        private List<RoadUploadPayload> CreateRoadPayloads()
        {
            var roads = new List<RoadUploadPayload>();

            if (TerrainMeta.Path == null)
            {
                return roads;
            }

            AddRoadPayloads(roads, TerrainMeta.Path.MainRoads, "main", 14f);
            AddRoadPayloads(roads, TerrainMeta.Path.SideRoads, "side", 8f);
            AddRoadPayloads(roads, TerrainMeta.Path.TrailRoads, "trail", 3.5f);

            // Older map-generation layouts expose the aggregate list only. Use
            // it as a fallback so a valid road network is never silently lost.
            if (roads.Count == 0)
            {
                AddRoadPayloads(roads, TerrainMeta.Path.Roads, "main", 14f);
            }

            return roads;
        }

        private void AddRoadPayloads(List<RoadUploadPayload> roads, List<PathList> sourceRoads, string kind, float fallbackWidth)
        {
            if (sourceRoads == null)
            {
                return;
            }

            const float sampleSpacing = 24f;
            const int maxRoads = 96;
            const int maxPointsPerRoad = 192;

            foreach (var road in sourceRoads)
            {
                if (roads.Count >= maxRoads)
                {
                    break;
                }

                if (road == null || road.Path == null || road.Path.Points == null || road.Path.Points.Length < 2)
                {
                    continue;
                }

                var sampled = new List<TerrainPointUploadPayload>();
                var points = road.Path.Points;
                var last = points[0];
                var distanceSinceSample = sampleSpacing;

                for (var index = 0; index < points.Length && sampled.Count < maxPointsPerRoad; index++)
                {
                    var point = points[index];
                    distanceSinceSample += index == 0 ? 0f : Vector3.Distance(last, point);

                    if (index == 0 || distanceSinceSample >= sampleSpacing || index == points.Length - 1)
                    {
                        var terrainY = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(point) : point.y;
                        sampled.Add(new TerrainPointUploadPayload
                        {
                            x = (float)Math.Round(point.x, 3),
                            y = (float)Math.Round(terrainY, 3),
                            z = (float)Math.Round(point.z, 3)
                        });
                        distanceSinceSample = 0f;
                    }

                    last = point;
                }

                if (sampled.Count >= 2)
                {
                    roads.Add(new RoadUploadPayload
                    {
                        name = string.IsNullOrWhiteSpace(road.Name) ? $"{kind}-road-{roads.Count + 1}" : road.Name,
                        kind = kind,
                        width = (float)Math.Round(Mathf.Clamp(road.Width > 0f ? road.Width : fallbackWidth, 2.5f, 38f), 3),
                        points = sampled
                    });
                }
            }
        }

        private List<PowerLineUploadPayload> CreatePowerLinePayloads()
        {
            var powerLines = new List<PowerLineUploadPayload>();

            if (TerrainMeta.Path == null || TerrainMeta.Path.Powerlines == null)
            {
                return powerLines;
            }

            const float towerSpacing = 90f;

            foreach (var powerLine in TerrainMeta.Path.Powerlines)
            {
                if (powerLine == null || powerLine.Path == null || powerLine.Path.Points == null || powerLine.Path.Points.Length < 2)
                {
                    continue;
                }

                var sampled = new List<TerrainPointUploadPayload>();
                var points = powerLine.Path.Points;
                var last = points[0];
                var distanceSinceTower = towerSpacing;

                for (var index = 0; index < points.Length && sampled.Count < 128; index++)
                {
                    var point = points[index];
                    distanceSinceTower += index == 0 ? 0f : Vector3.Distance(last, point);

                    if (index == 0 || distanceSinceTower >= towerSpacing || index == points.Length - 1)
                    {
                        var terrainY = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(point) : point.y;
                        sampled.Add(new TerrainPointUploadPayload
                        {
                            x = (float)Math.Round(point.x, 3),
                            y = (float)Math.Round(terrainY, 3),
                            z = (float)Math.Round(point.z, 3)
                        });
                        distanceSinceTower = 0f;
                    }

                    last = point;
                }

                if (sampled.Count >= 2)
                {
                    powerLines.Add(new PowerLineUploadPayload
                    {
                        name = string.IsNullOrWhiteSpace(powerLine.Name) ? $"powerline-{powerLines.Count + 1}" : powerLine.Name,
                        points = sampled
                    });
                }

                if (powerLines.Count >= 32)
                {
                    break;
                }
            }

            return powerLines;
        }

        private string MonumentDisplayName(MonumentInfo monument, string prefab)
        {
            var name = monument.displayPhrase != null ? (monument.displayPhrase.english ?? "").Replace("\n", "").Trim() : "";

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (monument.Type == MonumentType.Cave)
            {
                return "Cave";
            }

            if (prefab.Contains("power_sub"))
            {
                return "Power Sub Station";
            }

            return string.IsNullOrWhiteSpace(prefab) ? "Monument" : prefab;
        }

        private string ShortPrefabName(string prefab)
        {
            prefab = prefab ?? "";
            var separator = prefab.LastIndexOf('/');

            if (separator >= 0 && separator < prefab.Length - 1)
            {
                prefab = prefab.Substring(separator + 1);
            }

            return prefab.Replace(".prefab", "");
        }

        private string MonumentKind(string name, string prefab)
        {
            var key = ((name ?? "") + " " + (prefab ?? "")).ToLowerInvariant();

            if (key.Contains("airfield")) return "airfield";
            if (key.Contains("launch")) return "launch_site";
            if (key.Contains("sphere") || key.Contains("dome")) return "dome";
            if (key.Contains("satellite")) return "satellite";
            if (key.Contains("lighthouse")) return "lighthouse";
            if (key.Contains("oilrig") || key.Contains("oil rig")) return "oilrig";
            if (key.Contains("harbor")) return "harbor";
            if (key.Contains("powerplant") || key.Contains("power plant")) return "powerplant";
            if (key.Contains("excavator")) return "excavator";
            if (key.Contains("trainyard") || key.Contains("train yard")) return "trainyard";
            if (key.Contains("military") || key.Contains("tunnel") || key.Contains("bunker")) return "military_tunnel";
            if (key.Contains("gas")) return "gas_station";
            if (key.Contains("supermarket")) return "supermarket";
            if (key.Contains("warehouse")) return "warehouse";
            if (key.Contains("quarry") || key.Contains("mining")) return "quarry";
            if (key.Contains("bandit") || key.Contains("compound")) return "settlement";
            if (key.Contains("fishing") || key.Contains("stables")) return "village";

            return "monument";
        }

        private float MonumentRadius(string prefab)
        {
            switch (prefab)
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
                case "mining_quarry_a":
                case "mining_quarry_b":
                case "mining_quarry_c": return 30f;
                case "OilrigAI": return 100f;
                case "OilrigAI2": return 200f;
                case "power_sub_big_1":
                case "power_sub_big_2": return 30f;
                case "power_sub_small_1":
                case "power_sub_small_2": return 25f;
                case "powerplant_1": return 145f;
                case "radtown_small_3": return 95f;
                case "satellite_dish": return 85f;
                case "sphere_tank": return 75f;
                case "supermarket_1": return 60f;
                case "trainyard_1": return 145f;
                case "warehouse": return 50f;
                case "water_treatment_plant_1": return 175f;
            }

            if (prefab != null && prefab.Contains("cave")) return 75f;
            if (prefab != null && prefab.Contains("fishing_village")) return 55f;
            if (prefab != null && prefab.Contains("stables")) return 80f;
            if (prefab != null && prefab.Contains("swamp")) return 55f;
            if (prefab != null && prefab.Contains("water_well")) return 30f;

            return 50f;
        }

        private string TerrainColor(Vector3 position, float height, float waterLevel)
        {
            if (TerrainMeta.WaterMap != null && height <= waterLevel + 0.35f)
            {
                return "#2f6f86";
            }

            var splat = TerrainMeta.SplatMap != null ? TerrainMeta.SplatMap.GetSplatMaxType(position).ToString().ToLowerInvariant() : "";
            var biome = TerrainMeta.BiomeMap != null ? TerrainMeta.BiomeMap.GetBiomeMaxType(position).ToString().ToLowerInvariant() : "";

            if (splat.Contains("snow") || biome.Contains("arctic") || biome.Contains("tundra"))
            {
                return "#d9dedc";
            }

            if (splat.Contains("sand") || biome.Contains("desert"))
            {
                return "#b79a6a";
            }

            if (splat.Contains("forest") || biome.Contains("forest"))
            {
                return "#315a39";
            }

            if (splat.Contains("grass") || biome.Contains("temperate"))
            {
                return "#587447";
            }

            if (splat.Contains("rock") || splat.Contains("stone"))
            {
                return "#6e7470";
            }

            if (splat.Contains("dirt") || splat.Contains("gravel") || splat.Contains("road"))
            {
                return "#705b43";
            }

            return "#586d49";
        }

        private void SendPost(string url, string body, Action<int, string> callback)
        {
            try
            {
                var headers = BuildHeaders("POST", url, body);
                headers["Content-Type"] = "application/json";
                webrequest.Enqueue(url, body, (code, response) =>
                {
                    try
                    {
                        callback?.Invoke(code, response ?? "");
                    }
                    catch (Exception ex)
                    {
                        PrintWarning($"Website POST callback failed for {url}: {ex.GetType().Name}: {ex.Message}");
                    }
                }, this, RequestMethod.POST, headers, WebRequestTimeoutMilliseconds());
            }
            catch (Exception ex)
            {
                PrintWarning($"Website POST enqueue failed for {url}: {ex.GetType().Name}: {ex.Message}");
                callback?.Invoke(0, $"enqueue error {ex.GetType().Name}: {ex.Message}");
            }
        }

        private float WebRequestTimeoutMilliseconds()
        {
            return (float)Math.Max(5000, config.WebRequestTimeoutMilliseconds);
        }

        private Dictionary<string, string> BuildHeaders(string method, string url, string body)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var pathAndQuery = new Uri(url).PathAndQuery;
            var bodyHash = Sha256(body ?? "");
            var payload = $"{method.ToUpperInvariant()}\n{pathAndQuery}\n{timestamp}\n{bodyHash}";
            var signature = HmacSha256(payload, ResolveBridgeSharedSecret());

            return new Dictionary<string, string>
            {
                ["X-Raidlands-Server"] = ResolveServerId(),
                ["X-Raidlands-Timestamp"] = timestamp,
                ["X-Raidlands-Signature"] = signature,
                ["Accept"] = "application/json"
            };
        }

        private void LogBridgeSecretDiagnostics()
        {
            var sharedSecret = ResolveBridgeSharedSecret();

            if (string.IsNullOrWhiteSpace(sharedSecret))
            {
                PrintWarning("Map bridge SharedSecret is empty after resolving secrets.");
                return;
            }

            Puts($"Map bridge SharedSecret source: {DescribeBridgeSecretSource()}; length: {sharedSecret.Length}; fingerprint: {SecretFingerprint(sharedSecret)}");
        }

        private string ResolveApiBaseUrl()
        {
            var configured = (config.ApiBaseUrl ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return FirstNonEmpty(LoadVipBridgeSetting("ApiBaseUrl"), "https://raidlands.net");
        }

        private string ResolveServerId()
        {
            var configured = (config.ServerId ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return FirstNonEmpty(LoadVipBridgeSetting("ServerId"), "raidlands-main");
        }

        private DateTime GetWipeStartedAt()
        {
            try
            {
                return SaveRestore.SaveCreatedTime.ToUniversalTime();
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private string ResolveWipeKey()
        {
            return ResolveWipeKey(GetWipeStartedAt());
        }

        private string ResolveWipeKey(DateTime wipeStartedAt)
        {
            var serverId = CleanWipeKeySegment(ResolveServerId(), "raidlands-main");
            var configured = ResolveSecretValue(config.WipeKey);

            if (!string.IsNullOrWhiteSpace(configured) && !IsGenericWipeKey(configured, serverId))
            {
                return configured.Trim();
            }

            configured = ResolveSecretValue(LoadVipBridgeSetting("WipeKey"));

            if (!string.IsNullOrWhiteSpace(configured) && !IsGenericWipeKey(configured, serverId))
            {
                return configured.Trim();
            }

            var startedAt = wipeStartedAt == DateTime.MinValue ? GetWipeStartedAt() : wipeStartedAt.ToUniversalTime();

            if (startedAt != DateTime.MinValue)
            {
                return $"{serverId}-{startedAt:yyyyMMdd'T'HHmmss'Z'}";
            }

            return $"{serverId}-current";
        }

        private static string CleanWipeKeySegment(string value, string fallback)
        {
            var raw = (value ?? "").Trim();
            var builder = new StringBuilder(raw.Length);

            foreach (var ch in raw)
            {
                builder.Append(IsWipeKeyCharacter(ch) ? ch : '-');
            }

            var cleaned = builder.ToString().Trim('-', '_', '.', ':');
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }

        private static bool IsWipeKeyCharacter(char ch)
        {
            return (ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch == '_'
                || ch == '-'
                || ch == '.'
                || ch == ':';
        }

        private static bool IsGenericWipeKey(string wipeKey, string serverId)
        {
            var normalized = (wipeKey ?? "").Trim().ToLowerInvariant();
            var normalizedServer = (serverId ?? "").Trim().ToLowerInvariant();
            return normalized == "current"
                || normalized == normalizedServer
                || normalized == normalizedServer + "-current"
                || normalized.EndsWith("-current", StringComparison.Ordinal);
        }

        private string ResolveBridgeSharedSecret()
        {
            var configuredSecret = ResolveSecretValue(config.SharedSecret);

            if (!string.IsNullOrWhiteSpace(configuredSecret))
            {
                return configuredSecret;
            }

            return ResolveSecretValue(LoadVipBridgeSetting("SharedSecret"));
        }

        private string DescribeBridgeSecretSource()
        {
            var configuredSecret = ResolveSecretValue(config.SharedSecret);

            if (!string.IsNullOrWhiteSpace(configuredSecret))
            {
                return DescribeSecretSource(config.SharedSecret, "WebsiteMapBridge");
            }

            var vipSetting = LoadVipBridgeSetting("SharedSecret");

            if (string.IsNullOrWhiteSpace(vipSetting))
            {
                return $"oxide/config/{VipBridgeConfigName}.json";
            }

            return $"{DescribeSecretSource(vipSetting, VipBridgeConfigName)} via oxide/config/{VipBridgeConfigName}.json";
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

            string secret;

            if (LoadSecrets().TryGetValue(key, out secret))
            {
                return (secret ?? "").Trim();
            }

            PrintWarning($"Secret variable {key} is not configured in oxide/config/{SecretsConfigName}.json.");
            return "";
        }

        private string DescribeSecretSource(string value, string configName)
        {
            var trimmed = (value ?? "").Trim();

            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return $"oxide/config/{configName}.json";
            }

            var key = trimmed.Substring(2, trimmed.Length - 3).Trim();
            var source = string.IsNullOrWhiteSpace(secretsConfigSource) ? $"oxide/config/{SecretsConfigName}.json" : secretsConfigSource;

            return $"{key} in {source}";
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
                PrintWarning($"Optional secrets file not found: oxide/config/{SecretsConfigName}.json.");
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

        private string LoadVipBridgeSetting(string key)
        {
            var bridgeConfig = LoadVipBridgeConfig();

            if (bridgeConfig == null)
            {
                return "";
            }

            return (bridgeConfig.Value<string>(key) ?? "").Trim();
        }

        private JObject LoadVipBridgeConfig()
        {
            if (vipBridgeConfig != null)
            {
                return vipBridgeConfig;
            }

            var path = Path.Combine(Interface.Oxide.ConfigDirectory, $"{VipBridgeConfigName}.json");

            if (!File.Exists(path))
            {
                PrintWarning($"VIP bridge config not found: oxide/config/{VipBridgeConfigName}.json.");
                return null;
            }

            try
            {
                vipBridgeConfig = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read oxide/config/{VipBridgeConfigName}.json: {ex.Message}");
            }

            return vipBridgeConfig;
        }

        private string GetMapDisplayName()
        {
            var saveFile = World.SaveFileName ?? "";

            if (saveFile.StartsWith("proceduralmap", StringComparison.OrdinalIgnoreCase))
            {
                return "Procedural Map";
            }

            if (!string.IsNullOrWhiteSpace(saveFile))
            {
                var name = Path.GetFileNameWithoutExtension(saveFile).Replace('_', ' ').Replace('-', ' ').Trim();
                return string.IsNullOrWhiteSpace(name) ? "Procedural Map" : name;
            }

            return "Procedural Map";
        }

        private int GetEncodingMode()
        {
            return string.Equals(config.FileType, "Png", StringComparison.OrdinalIgnoreCase) ? EncodingPng : EncodingJpg;
        }

        private void NormalizeConfig(bool hasPublishSkyboxSetting)
        {
            var defaults = new Configuration();
            config.ApiBaseUrl = ConfiguredOrDefault(config.ApiBaseUrl, defaults.ApiBaseUrl);
            config.ServerId = ConfiguredOrDefault(config.ServerId, defaults.ServerId);
            config.RenderName = ConfiguredOrDefault(config.RenderName, defaults.RenderName);
            config.TextureRenderName = ConfiguredOrDefault(config.TextureRenderName, defaults.TextureRenderName);
            config.FileType = string.Equals(config.FileType, "Png", StringComparison.OrdinalIgnoreCase) ? "Png" : "Jpg";
            config.ImageResolutionScale = Math.Max(0.05f, config.ImageResolutionScale <= 0f ? defaults.ImageResolutionScale : config.ImageResolutionScale);
            config.AutoPublishDelaySeconds = Math.Max(1, config.AutoPublishDelaySeconds);
            config.WebRequestTimeoutMilliseconds = Math.Max(5000, config.WebRequestTimeoutMilliseconds);
            config.TerrainSampleResolution = Math.Max(17, Math.Min(257, config.TerrainSampleResolution <= 0 ? defaults.TerrainSampleResolution : config.TerrainSampleResolution));
            config.SkyboxImagePath = ConfiguredOrDefault(config.SkyboxImagePath, defaults.SkyboxImagePath);
            if (!hasPublishSkyboxSetting)
            {
                config.PublishSkybox = defaults.PublishSkybox;
            }
            config.PlayerLocationIntervalSeconds = Math.Max(5, config.PlayerLocationIntervalSeconds <= 0 ? defaults.PlayerLocationIntervalSeconds : config.PlayerLocationIntervalSeconds);
            config.EnvironmentIntervalSeconds = Math.Max(30, config.EnvironmentIntervalSeconds <= 0 ? defaults.EnvironmentIntervalSeconds : config.EnvironmentIntervalSeconds);
            config.WorldEventIntervalSeconds = Math.Max(3, config.WorldEventIntervalSeconds <= 0 ? defaults.WorldEventIntervalSeconds : config.WorldEventIntervalSeconds);
            config.WorldEventMinimumMovementMeters = Math.Max(0.25f, Math.Min(100f, config.WorldEventMinimumMovementMeters <= 0f ? defaults.WorldEventMinimumMovementMeters : config.WorldEventMinimumMovementMeters));
            config.WorldEventMinimumRotationDegrees = Math.Max(0.25f, Math.Min(180f, config.WorldEventMinimumRotationDegrees <= 0f ? defaults.WorldEventMinimumRotationDegrees : config.WorldEventMinimumRotationDegrees));
            config.WorldEventRouteMaxSamples = Math.Max(12, Math.Min(160, config.WorldEventRouteMaxSamples <= 0 ? defaults.WorldEventRouteMaxSamples : config.WorldEventRouteMaxSamples));
            config.WorldEventPayloadMaxBytes = Math.Max(2000, Math.Min(11900, config.WorldEventPayloadMaxBytes <= 0 ? defaults.WorldEventPayloadMaxBytes : config.WorldEventPayloadMaxBytes));
            config.AirdropCarrierSearchRadiusMeters = Math.Max(50f, Math.Min(2000f, config.AirdropCarrierSearchRadiusMeters <= 0f ? defaults.AirdropCarrierSearchRadiusMeters : config.AirdropCarrierSearchRadiusMeters));
        }

        private bool ConfigHasProperty(string propertyName)
        {
            try
            {
                var path = Path.Combine(Interface.Oxide.ConfigDirectory, $"{Name}.json");

                if (!File.Exists(path))
                {
                    return false;
                }

                var json = JObject.Parse(File.ReadAllText(path));
                return json.Properties().Any(property => property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private bool IsSuccess(int code, string response, out string error)
        {
            if (code >= 200 && code < 300 && !string.IsNullOrWhiteSpace(response))
            {
                error = "";
                return true;
            }

            error = $"HTTP {code}: {response}";
            return false;
        }

        private static string ConfiguredOrDefault(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return "";
        }

        private static string TrimSlash(string value)
        {
            return (value ?? "").Trim().TrimEnd('/');
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string Sha256Bytes(byte[] value)
        {
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(value ?? Array.Empty<byte>()));
            }
        }

        private static string HmacSha256(string value, string secret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? "")))
            {
                return Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
            }
        }

        private static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);

            foreach (var item in bytes)
            {
                builder.Append(item.ToString("x2"));
            }

            return builder.ToString();
        }

        private static string SecretFingerprint(string value)
        {
            var hash = Sha256(value ?? "");
            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }
    }
}
