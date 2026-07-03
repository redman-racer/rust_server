using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("WebsiteClanBridge", "Raidlands", "1.0.0")]
    [Description("Processes Raidlands website clan management actions through k1lly0u Clans.")]
    public class WebsiteClanBridge : CovalencePlugin
    {
        [PluginReference] private Plugin Clans;

        private Configuration config;
        private Timer pollTimer;
        private Timer snapshotTimer;
        private Timer pendingSnapshotTimer;
        private Dictionary<string, string> secrets;
        private const string SecretsConfigName = "Secrets.local";
        private const string VipBridgeConfigName = "WebsiteVipBridge";
        private string secretsConfigSource;
        private string vipBridgeSharedSecretSetting;

        private class Configuration
        {
            public string ApiBaseUrl = "https://raidlands.net";
            public string ServerId = "raidlands-main";
            public string SharedSecret = "";
            public bool ClanActionsEnabled = true;
            public bool SnapshotSyncEnabled = true;
            public int PollIntervalSeconds = 30;
            public int SnapshotIntervalSeconds = 180;
            public int SnapshotDebounceSeconds = 8;
            public int ActionBatchSize = 25;
        }

        private class ClanActionResponse
        {
            public bool ok;
            public string error;
            public List<ClanAction> actions = new List<ClanAction>();
        }

        private class ClanAction
        {
            public long id;
            public string action;
            public string clan_tag;
            public string actor_steam_id64;
            public string actor_display_name;
            public string target_steam_id64;
            public string target_display_name;
            public int attempts;
        }

        private class ClanActionResultPayload
        {
            public List<ClanActionResult> results = new List<ClanActionResult>();
        }

        private class ClanActionResult
        {
            public long id;
            public bool ok;
            public string action;
            public string clan_tag;
            public string actor_role;
            public string target_steam_id64;
            public string error;
        }

        private class BridgeResponse
        {
            public bool ok;
            public string error;
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
            catch
            {
                PrintWarning("Configuration is invalid; loading defaults.");
                config = new Configuration();
            }

            var defaults = new Configuration();
            config.ApiBaseUrl = ConfiguredOrDefault(config.ApiBaseUrl, defaults.ApiBaseUrl);
            config.ServerId = ConfiguredOrDefault(config.ServerId, defaults.ServerId);

            if (config.PollIntervalSeconds <= 0)
            {
                config.PollIntervalSeconds = defaults.PollIntervalSeconds;
            }

            if (config.SnapshotIntervalSeconds <= 0)
            {
                config.SnapshotIntervalSeconds = defaults.SnapshotIntervalSeconds;
            }

            if (config.SnapshotDebounceSeconds <= 0)
            {
                config.SnapshotDebounceSeconds = defaults.SnapshotDebounceSeconds;
            }

            if (config.ActionBatchSize <= 0)
            {
                config.ActionBatchSize = defaults.ActionBatchSize;
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void OnServerInitialized()
        {
            if (!CanRequest())
            {
                return;
            }

            LogBridgeSecretDiagnostics();

            if (config.ClanActionsEnabled)
            {
                var pollInterval = Math.Max(10, config.PollIntervalSeconds);
                timer.Once(8f, PollClanActions);
                pollTimer = timer.Every(pollInterval, PollClanActions);
                Puts($"WebsiteClanBridge polling clan actions every {pollInterval} seconds.");
            }

            if (config.SnapshotSyncEnabled)
            {
                var snapshotInterval = Math.Max(60, config.SnapshotIntervalSeconds);
                timer.Once(12f, SyncClanSnapshot);
                snapshotTimer = timer.Every(snapshotInterval, SyncClanSnapshot);
                Puts($"WebsiteClanBridge syncing clan snapshots every {snapshotInterval} seconds.");
            }
        }

        private void Unload()
        {
            pollTimer?.Destroy();
            snapshotTimer?.Destroy();
            pendingSnapshotTimer?.Destroy();
        }

        private void OnClanCreate(string tag)
        {
            QueueSnapshotSync();
        }

        private void OnClanUpdate(string tag)
        {
            QueueSnapshotSync();
        }

        private void OnClanMemberJoined(string tag, string joining, List<string> members)
        {
            QueueSnapshotSync();
        }

        private void OnClanMemberGone(string tag, string leaving, List<string> members)
        {
            QueueSnapshotSync();
        }

        private void OnClanDisbanded(string tag, List<string> members)
        {
            QueueSnapshotSync();
        }

        private void PollClanActions()
        {
            if (!config.ClanActionsEnabled || !CanRequest())
            {
                return;
            }

            var limit = Math.Max(1, Math.Min(50, config.ActionBatchSize));
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/clan-actions.php?limit={limit}";

            SendGet(url, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Clan action poll failed: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<ClanActionResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"Clan action poll failed: {payload?.error ?? "invalid response"}");
                    return;
                }

                if (payload.actions == null || payload.actions.Count == 0)
                {
                    return;
                }

                var resultPayload = new ClanActionResultPayload();
                var changed = false;

                foreach (var action in payload.actions)
                {
                    var result = ExecuteClanAction(action);
                    resultPayload.results.Add(result);
                    changed = changed || result.ok;
                }

                PostClanActionResults(resultPayload, changed);
            });
        }

        private ClanActionResult ExecuteClanAction(ClanAction action)
        {
            var result = new ClanActionResult
            {
                id = action?.id ?? 0,
                action = action?.action ?? "",
                clan_tag = action?.clan_tag ?? "",
                target_steam_id64 = action?.target_steam_id64 ?? "",
                actor_role = "",
                ok = false,
                error = ""
            };

            if (action == null || action.id <= 0)
            {
                result.error = "Invalid clan action payload.";
                return result;
            }

            if (Clans == null || !Clans.IsLoaded)
            {
                result.error = "Clans plugin is not loaded.";
                return result;
            }

            var call = Clans.Call(
                "RaidlandsClanAction",
                action.action ?? "",
                action.actor_steam_id64 ?? "",
                action.actor_display_name ?? "",
                action.target_steam_id64 ?? "",
                action.target_display_name ?? "",
                action.clan_tag ?? ""
            ) as JObject;

            if (call == null)
            {
                result.error = "Clans plugin did not return an action result.";
                return result;
            }

            result.ok = call.Value<bool>("ok");
            result.error = call.Value<string>("error") ?? "";
            result.action = call.Value<string>("action") ?? result.action;
            result.clan_tag = call.Value<string>("clan_tag") ?? result.clan_tag;
            result.actor_role = call.Value<string>("actor_role") ?? "";
            result.target_steam_id64 = call.Value<string>("target_steam_id64") ?? result.target_steam_id64;

            if (result.ok)
            {
                Puts($"Processed clan action #{action.id}: {result.action} [{result.clan_tag}].");
            }
            else
            {
                PrintWarning($"Clan action #{action.id} failed: {result.error}");
            }

            return result;
        }

        private void PostClanActionResults(ClanActionResultPayload payload, bool queueSnapshot)
        {
            var body = JsonConvert.SerializeObject(payload);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/clan-action-result.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Clan action result sync failed: {error}");
                    return;
                }

                var result = JsonConvert.DeserializeObject<BridgeResponse>(response);

                if (result == null || !result.ok)
                {
                    PrintWarning($"Clan action result sync failed: {result?.error ?? "invalid response"}");
                    return;
                }

                if (queueSnapshot)
                {
                    QueueSnapshotSync();
                }
            });
        }

        private void QueueSnapshotSync()
        {
            if (!config.SnapshotSyncEnabled || !CanRequest())
            {
                return;
            }

            pendingSnapshotTimer?.Destroy();
            pendingSnapshotTimer = timer.Once(Math.Max(2, config.SnapshotDebounceSeconds), SyncClanSnapshot);
        }

        private void SyncClanSnapshot()
        {
            pendingSnapshotTimer?.Destroy();
            pendingSnapshotTimer = null;

            if (!config.SnapshotSyncEnabled || !CanRequest())
            {
                return;
            }

            if (Clans == null || !Clans.IsLoaded)
            {
                PrintWarning("Cannot sync clan snapshot because Clans is not loaded.");
                return;
            }

            var clans = Clans.Call("RaidlandsClanSnapshot") as JArray;

            if (clans == null)
            {
                PrintWarning("Cannot sync clan snapshot because Clans did not return snapshot data.");
                return;
            }

            var payload = new JObject
            {
                ["generated_at"] = DateTime.UtcNow.ToString("o"),
                ["clans"] = clans
            };
            var body = payload.ToString(Formatting.None);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/clan-snapshot.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Clan snapshot sync failed: {error}");
                    return;
                }

                var result = JsonConvert.DeserializeObject<BridgeResponse>(response);

                if (result == null || !result.ok)
                {
                    PrintWarning($"Clan snapshot sync failed: {result?.error ?? "invalid response"}");
                    return;
                }

                Puts($"Clan snapshot synced for {clans.Count} clans.");
            });
        }

        private bool CanRequest()
        {
            if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            {
                PrintWarning("ApiBaseUrl is not configured.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(ResolveBridgeSharedSecret()))
            {
                PrintWarning("SharedSecret is not configured. Leave WebsiteClanBridge SharedSecret blank to reuse WebsiteVipBridge, and configure WebsiteVipBridge SharedSecret normally.");
                return false;
            }

            return true;
        }

        private void LogBridgeSecretDiagnostics()
        {
            var sharedSecret = ResolveBridgeSharedSecret();

            if (string.IsNullOrWhiteSpace(sharedSecret))
            {
                PrintWarning("Clan bridge SharedSecret is empty after resolving secrets.");
                return;
            }

            Puts($"Clan bridge SharedSecret source: {DescribeBridgeSecretSource()}; length: {sharedSecret.Length}; fingerprint: {SecretFingerprint(sharedSecret)}");
        }

        private void SendGet(string url, Action<int, string> callback)
        {
            var headers = BuildHeaders("GET", url, "");
            webrequest.Enqueue(url, null, (code, response) => callback(code, response), this, RequestMethod.GET, headers);
        }

        private void SendPost(string url, string body, Action<int, string> callback)
        {
            var headers = BuildHeaders("POST", url, body);
            headers["Content-Type"] = "application/json";
            webrequest.Enqueue(url, body, (code, response) => callback(code, response), this, RequestMethod.POST, headers);
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
                ["X-Raidlands-Server"] = config.ServerId,
                ["X-Raidlands-Timestamp"] = timestamp,
                ["X-Raidlands-Signature"] = signature,
                ["Accept"] = "application/json"
            };
        }

        private string ResolveBridgeSharedSecret()
        {
            var configuredSecret = ResolveSecretValue(config.SharedSecret);

            if (!string.IsNullOrWhiteSpace(configuredSecret))
            {
                return configuredSecret;
            }

            return ResolveSecretValue(LoadVipBridgeSharedSecretSetting());
        }

        private string DescribeBridgeSecretSource()
        {
            var configuredSecret = ResolveSecretValue(config.SharedSecret);

            if (!string.IsNullOrWhiteSpace(configuredSecret))
            {
                return DescribeSecretSource(config.SharedSecret);
            }

            var vipSetting = LoadVipBridgeSharedSecretSetting();

            if (string.IsNullOrWhiteSpace(vipSetting))
            {
                return $"oxide/config/{VipBridgeConfigName}.json";
            }

            return $"{DescribeSecretSource(vipSetting)} via oxide/config/{VipBridgeConfigName}.json";
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

        private string DescribeSecretSource(string value)
        {
            var trimmed = (value ?? "").Trim();

            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return "oxide/config/WebsiteClanBridge.json";
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

        private string LoadVipBridgeSharedSecretSetting()
        {
            if (vipBridgeSharedSecretSetting != null)
            {
                return vipBridgeSharedSecretSetting;
            }

            vipBridgeSharedSecretSetting = "";
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, $"{VipBridgeConfigName}.json");

            if (!File.Exists(path))
            {
                PrintWarning($"VIP bridge config not found: oxide/config/{VipBridgeConfigName}.json.");
                return vipBridgeSharedSecretSetting;
            }

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                vipBridgeSharedSecretSetting = (json.Value<string>("SharedSecret") ?? "").Trim();
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read oxide/config/{VipBridgeConfigName}.json SharedSecret: {ex.Message}");
            }

            return vipBridgeSharedSecretSetting;
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
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string TrimSlash(string value)
        {
            return (value ?? "").Trim().TrimEnd('/');
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                return ToHex(bytes);
            }
        }

        private static string SecretFingerprint(string value)
        {
            var hash = Sha256(value ?? "");

            return hash.Length > 12 ? hash.Substring(0, 12) : hash;
        }

        private static string HmacSha256(string value, string secret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? "")))
            {
                var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                return ToHex(bytes);
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);

            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
