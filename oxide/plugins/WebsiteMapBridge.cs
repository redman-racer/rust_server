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
    [Info("WebsiteMapBridge", "Raidlands", "1.0.13")]
    [Description("Publishes the current RustMapApi map image and sampled terrain to the Raidlands website.")]
    public class WebsiteMapBridge : CovalencePlugin
    {
        [PluginReference] private Plugin RustMapApi;
        [PluginReference] private Plugin Clans;

        private const string SecretsConfigName = "Secrets.local";
        private const string VipBridgeConfigName = "WebsiteVipBridge";
        private const int EncodingJpg = 1;
        private const int EncodingPng = 2;

        private Configuration config;
        private Timer autoPublishTimer;
        private Timer playerLocationTimer;
        private Timer environmentTimer;
        private Dictionary<string, string> secrets;
        private string secretsConfigSource;
        private JObject vipBridgeConfig;
        private bool publishInFlight;
        private string lastPublishedWipeKey = "";
        private string lastEnvironmentSyncAt = "";
        private string lastEnvironmentDiagnosticSummary = "awaiting first environment sample";
        private long lastEnvironmentSampleMilliseconds;
        private string lastWeatherOverrideSignature = "";
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
            public bool AutoPublishOnServerInitialized = false;
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
            public bool PublishEnvironment = true;
            public int EnvironmentIntervalSeconds = 30;
        }

        private class MapUploadPayload
        {
            public string server_id;
            public string wipe_key;
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
            public int version = 1;
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
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (!config.PublishReplayEvents || entity == null)
            {
                return;
            }

            var baseEntity = entity as BaseEntity;
            var prefab = baseEntity == null ? "" : (baseEntity.PrefabName ?? baseEntity.ShortPrefabName ?? "");
            if (prefab.IndexOf("supply_drop", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            timer.Once(0.25f, () => PublishAirdropReplayEvent(baseEntity));
        }

        private void Unload()
        {
            autoPublishTimer?.Destroy();
            autoPublishTimer = null;
            playerLocationTimer?.Destroy();
            playerLocationTimer = null;
            environmentTimer?.Destroy();
            environmentTimer = null;
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

        [ConsoleCommand("rl_map_status")]
        private void StatusCommand(ConsoleSystem.Arg arg)
        {
            if (!CanUsePublishCommand(arg))
            {
                ReplyToCommand(arg, "You must be server console, RCON, or auth level 2 to inspect the website map bridge.");
                return;
            }

            ReplyToCommand(arg, "WebsiteMapBridge v1.0.13 status requested; reading cached diagnostics.");

            try
            {
                var secretState = string.IsNullOrWhiteSpace(ResolveBridgeSharedSecret()) ? "missing" : "configured";
                var apiState = RustMapApi == null ? "missing" : RustMapApi.IsLoaded ? "loaded" : "not loaded";
                var heightMapState = TerrainMeta.HeightMap == null ? "missing" : "available";

                ReplyToCommand(
                    arg,
                    $"WebsiteMapBridge v1.0.13 status: server={ResolveServerId()}, api={apiState}, ready=not-probed, secret={secretState}, render={config.RenderName}, textureRender={config.TextureRenderName}, autoPublishReady={config.AutoPublishOnRustMapApiReady}, autoPublishInit={config.AutoPublishOnServerInitialized}, terrainEnabled={config.PublishTerrain}, terrainResolution={config.TerrainSampleResolution}, monumentsEnabled={config.IncludeMonuments}, skybox={config.PublishSkybox}/{config.SkyboxImagePath}, playerLocations={config.PublishPlayerLocations}/{config.PlayerLocationIntervalSeconds}s, environment={config.PublishEnvironment}/{config.EnvironmentIntervalSeconds}s last={FirstNonEmpty(lastEnvironmentSyncAt, "never")}, sampleMs={lastEnvironmentSampleMilliseconds}, sampled=[{lastEnvironmentDiagnosticSummary}], replayEvents={config.PublishReplayEvents}, heightMap={heightMapState}, lastWipe={lastPublishedWipeKey}."
                );
            }
            catch (Exception ex)
            {
                PrintError($"rl_map_status failed: {ex}");
                ReplyToCommand(arg, $"WebsiteMapBridge status failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        [ConsoleCommand("rl_map_locations_sync")]
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

            if (!config.PublishPlayerLocations)
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

        private void PublishAirdropReplayEvent(BaseEntity entity)
        {
            var secret = ResolveBridgeSharedSecret();
            if (entity == null || entity.IsDestroyed || string.IsNullOrWhiteSpace(secret))
            {
                return;
            }

            var position = entity.transform.position;
            var payload = new ReplayEventSnapshotPayload
            {
                server_id = ResolveServerId(),
                wipe_key = ResolveWipeKey(),
                events = new List<ReplayEventPayload>
                {
                    new ReplayEventPayload
                    {
                        event_key = "airdrop:" + entity.net.ID.Value + ":" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                        event_type = "airdrop",
                        occurred_at = DateTime.UtcNow.ToString("o"),
                        x = (float)Math.Round(position.x, 3),
                        y = (float)Math.Round(position.y, 3),
                        z = (float)Math.Round(position.z, 3),
                        vehicle = "cargo_plane",
                        payload = new Dictionary<string, object>
                        {
                            ["prefab"] = entity.PrefabName ?? entity.ShortPrefabName ?? "",
                            ["networkId"] = entity.net.ID.Value.ToString()
                        }
                    }
                }
            };
            var body = JsonConvert.SerializeObject(payload);
            var url = $"{TrimSlash(ResolveApiBaseUrl())}/api/server/map-replay-events-snapshot.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var requestError))
                {
                    PrintWarning("Website airdrop replay event post failed: " + requestError);
                }
            });
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

            var wipeKey = ResolveWipeKey();

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
            summary = $"terrain {resolution}x{resolution}, height {minHeight:0.###}-{maxHeight:0.###}, ocean {waterLevel:0.###}, monuments {monuments.Count}";

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
                monuments = monuments
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

        private string ResolveWipeKey()
        {
            var configured = ResolveSecretValue(config.WipeKey);

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            configured = ResolveSecretValue(LoadVipBridgeSetting("WipeKey"));

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            return $"{ResolveServerId()}-current";
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
