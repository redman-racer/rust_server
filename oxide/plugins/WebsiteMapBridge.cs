using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Facepunch.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("WebsiteMapBridge", "Raidlands", "1.0.0")]
    [Description("Publishes the current RustMapApi map image to the Raidlands website.")]
    public class WebsiteMapBridge : CovalencePlugin
    {
        [PluginReference] private Plugin RustMapApi;

        private const string SecretsConfigName = "Secrets.local";
        private const string VipBridgeConfigName = "WebsiteVipBridge";
        private const int EncodingJpg = 1;
        private const int EncodingPng = 2;

        private Configuration config;
        private Timer autoPublishTimer;
        private Dictionary<string, string> secrets;
        private string secretsConfigSource;
        private JObject vipBridgeConfig;
        private bool publishInFlight;
        private string lastPublishedWipeKey = "";

        private class Configuration
        {
            public string ApiBaseUrl = "https://raidlands.net";
            public string ServerId = "raidlands-main";
            public string SharedSecret = "";
            public string WipeKey = "";
            public string RenderName = "Icons";
            [JsonProperty("FileType (Jpg, Png)")]
            public string FileType = "Jpg";
            public float ImageResolutionScale = 0.5f;
            public bool AutoPublishOnRustMapApiReady = true;
            public int AutoPublishDelaySeconds = 10;
            public int WebRequestTimeoutMilliseconds = 60000;
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
            public int image_width;
            public int image_height;
            public int resolution;
            public int world_size;
            public int seed;
            public int protocol;
            public string generated_at;
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
            public string wipeKey;
            public string renderName;
            public string publishedAt;
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating default WebsiteMapBridge config.");
            config = new Configuration();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            config = Config.ReadObject<Configuration>() ?? new Configuration();
            NormalizeConfig();
            Config.WriteObject(config, true);
        }

        private void OnServerInitialized()
        {
            LogBridgeSecretDiagnostics();
            QueueAutoPublish("server initialized");
        }

        private void Unload()
        {
            autoPublishTimer?.Destroy();
            autoPublishTimer = null;
        }

        private void OnRustMapApiReady()
        {
            QueueAutoPublish("RustMapApi ready");
        }

        [ConsoleCommand("rl_map_publish")]
        private void PublishCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin)
            {
                return;
            }

            var renderName = arg.GetString(0, config.RenderName);
            var scale = config.ImageResolutionScale;

            if (arg.Args != null && arg.Args.Length > 1)
            {
                float parsedScale;

                if (!float.TryParse(arg.GetString(1, ""), out parsedScale) || parsedScale <= 0f)
                {
                    arg.ReplyWith("Invalid syntax. Use rl_map_publish <renderName:optional> <resolutionScale:optional>");
                    return;
                }

                scale = parsedScale;
            }

            PublishMap("manual command", message => arg.ReplyWith(message), true, renderName, scale);
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

                var payload = new MapUploadPayload
                {
                    server_id = ResolveServerId(),
                    wipe_key = wipeKey,
                    map_name = GetMapDisplayName(),
                    render_name = renderName,
                    file_type = encoding == EncodingPng ? "Png" : "Jpg",
                    image_base64 = Convert.ToBase64String(image),
                    image_sha256 = Sha256Bytes(image),
                    image_width = Convert.ToInt32(map["width"]),
                    image_height = Convert.ToInt32(map["height"]),
                    resolution = resolution,
                    world_size = Math.Max(0, ConVar.Server.worldsize),
                    seed = Math.Max(0, ConVar.Server.seed),
                    protocol = Rust.Protocol.network,
                    generated_at = DateTime.UtcNow.ToString("o")
                };

                var body = JsonConvert.SerializeObject(payload);
                var url = $"{TrimSlash(ResolveApiBaseUrl())}/api/server/map-upload.php";

                Puts($"Publishing {renderName} map to website ({payload.image_width}x{payload.image_height}, {image.Length} bytes) after {reason}.");
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

        private void SendPost(string url, string body, Action<int, string> callback)
        {
            var headers = BuildHeaders("POST", url, body);
            headers["Content-Type"] = "application/json";
            webrequest.Enqueue(url, body, (code, response) => callback(code, response ?? ""), this, RequestMethod.POST, headers, WebRequestTimeoutMilliseconds());
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

        private void NormalizeConfig()
        {
            var defaults = new Configuration();
            config.ApiBaseUrl = ConfiguredOrDefault(config.ApiBaseUrl, defaults.ApiBaseUrl);
            config.ServerId = ConfiguredOrDefault(config.ServerId, defaults.ServerId);
            config.RenderName = ConfiguredOrDefault(config.RenderName, defaults.RenderName);
            config.FileType = string.Equals(config.FileType, "Png", StringComparison.OrdinalIgnoreCase) ? "Png" : "Jpg";
            config.ImageResolutionScale = Math.Max(0.05f, config.ImageResolutionScale <= 0f ? defaults.ImageResolutionScale : config.ImageResolutionScale);
            config.AutoPublishDelaySeconds = Math.Max(1, config.AutoPublishDelaySeconds);
            config.WebRequestTimeoutMilliseconds = Math.Max(5000, config.WebRequestTimeoutMilliseconds);
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
