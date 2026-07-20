using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
    [Info("WebsiteBridgeHub", "Raidlands", "1.1.0")]
    [Description("Batches frequent Raidlands website bridge control and telemetry traffic into a single signed exchange.")]
    public class WebsiteBridgeHub : RustPlugin
    {
        private const int ProtocolVersion = 1;
        private const string SecretsConfigName = "Secrets.local";

        [PluginReference] private Plugin WebsiteVipBridge;
        [PluginReference] private Plugin WebsiteMapBridge;
        [PluginReference] private Plugin WebsiteClanBridge;

        private HubConfig config;
        private Timer nextExchangeTimer;
        private bool requestInFlight;
        private bool unloading;
        private long sequence;
        private int consecutiveFailures;
        private int lastHttpCode;
        private int lastRequestBytes;
        private int lastResponseBytes;
        private double lastLatencyMilliseconds;
        private DateTime lastAttemptUtc = DateTime.MinValue;
        private DateTime lastSuccessUtc = DateTime.MinValue;
        private string lastError = "never attempted";
        private float nextDelaySeconds;
        private readonly Queue<DateTime> recentCalls = new Queue<DateTime>();
        private readonly Dictionary<string, DateTime> lastModuleCollectionUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> lastModuleCollectionMilliseconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> secrets;
        private readonly Dictionary<string, string> lastModuleHealth = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vip"] = "awaiting exchange",
            ["map"] = "awaiting exchange",
            ["clans"] = "awaiting exchange"
        };

        private class HubConfig
        {
            public string ApiBaseUrl = "https://raidlands.net";
            public string ServerId = "raidlands-main";
            public string SharedSecret = "${RAIDLANDS_BRIDGE_SHARED_SECRET}";
            public int ExchangeIntervalSeconds = 30;
            public int RequestTimeoutSeconds = 20;
            public int MaximumBodyBytes = 1048576;
            public int StartupDelaySeconds = 10;
            public int MaximumBackoffSeconds = 300;
            public int VipModuleIntervalSeconds = 60;
            public int MapModuleIntervalSeconds = 120;
            public int ClanModuleIntervalSeconds = 30;
            public int SlowMapModuleIntervalSeconds = 300;
            public int SlowModuleThresholdMilliseconds = 75;
            public bool AllowInsecureHttpForDevelopment = false;
        }

        protected override void LoadDefaultConfig()
        {
            config = new HubConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<HubConfig>() ?? new HubConfig();
            }
            catch (Exception ex)
            {
                PrintWarning("Could not read WebsiteBridgeHub config; using defaults: " + ex.Message);
                config = new HubConfig();
            }

            config.ApiBaseUrl = (config.ApiBaseUrl ?? "").Trim().TrimEnd('/');
            config.ServerId = (config.ServerId ?? "").Trim();
            config.ExchangeIntervalSeconds = Math.Max(30, config.ExchangeIntervalSeconds);
            config.RequestTimeoutSeconds = Math.Max(5, Math.Min(120, config.RequestTimeoutSeconds));
            config.MaximumBodyBytes = Math.Max(65536, Math.Min(1048576, config.MaximumBodyBytes));
            config.StartupDelaySeconds = Math.Max(1, Math.Min(120, config.StartupDelaySeconds));
            config.MaximumBackoffSeconds = Math.Max(config.ExchangeIntervalSeconds, Math.Min(900, config.MaximumBackoffSeconds));
            config.VipModuleIntervalSeconds = Math.Max(config.ExchangeIntervalSeconds, Math.Min(900, config.VipModuleIntervalSeconds));
            config.MapModuleIntervalSeconds = Math.Max(config.ExchangeIntervalSeconds, Math.Min(900, config.MapModuleIntervalSeconds));
            config.ClanModuleIntervalSeconds = Math.Max(config.ExchangeIntervalSeconds, Math.Min(900, config.ClanModuleIntervalSeconds));
            config.SlowMapModuleIntervalSeconds = Math.Max(config.MapModuleIntervalSeconds, Math.Min(1800, config.SlowMapModuleIntervalSeconds));
            config.SlowModuleThresholdMilliseconds = Math.Max(25, Math.Min(500, config.SlowModuleThresholdMilliseconds));
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        private void OnServerInitialized()
        {
            if (!CanRequest(out var error))
            {
                PrintError("Website bridge exchange is disabled: " + error);
                lastError = error;
                return;
            }

            Puts("Website bridge shared secret source: " + DescribeBridgeSecretSource() + ".");
            ScheduleNext(config.StartupDelaySeconds);
        }

        private void Unload()
        {
            unloading = true;
            nextExchangeTimer?.Destroy();
            nextExchangeTimer = null;
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null || unloading || string.Equals(plugin.Name, Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (plugin.Name == "WebsiteVipBridge" || plugin.Name == "WebsiteMapBridge" || plugin.Name == "WebsiteClanBridge")
            {
                timer.Once(1f, () =>
                {
                    if (!requestInFlight && nextExchangeTimer == null)
                    {
                        ScheduleNext(1f);
                    }
                });
            }
        }

        [ConsoleCommand("websitebridge.status")]
        private void StatusCommand(ConsoleSystem.Arg arg)
        {
            if (arg != null && arg.Connection != null && !arg.IsRcon && !arg.IsAdmin && arg.Connection.authLevel < 2)
            {
                arg.ReplyWith("You must be server console, RCON, or auth level 2.");
                return;
            }

            PruneRecentCalls();
            arg.ReplyWith(BuildStatus());
        }

        [ConsoleCommand("websitebridge.sync")]
        private void SyncCommand(ConsoleSystem.Arg arg)
        {
            if (arg != null && arg.Connection != null && !arg.IsRcon && !arg.IsAdmin && arg.Connection.authLevel < 2)
            {
                arg.ReplyWith("You must be server console, RCON, or auth level 2.");
                return;
            }

            if (requestInFlight)
            {
                arg.ReplyWith("Website bridge exchange is already in flight.");
                return;
            }

            nextExchangeTimer?.Destroy();
            nextExchangeTimer = null;
            RunExchange();
            arg.ReplyWith("Website bridge exchange queued.");
        }

        [HookMethod(nameof(API_GetWebsiteBridgeStatus))]
        public object API_GetWebsiteBridgeStatus()
        {
            PruneRecentCalls();
            return new Dictionary<string, object>
            {
                ["in_flight"] = requestInFlight,
                ["sequence"] = sequence,
                ["last_http_code"] = lastHttpCode,
                ["last_success_at"] = FormatTime(lastSuccessUtc),
                ["last_error"] = lastError,
                ["consecutive_failures"] = consecutiveFailures,
                ["next_delay_seconds"] = nextDelaySeconds,
                ["calls_per_minute"] = recentCalls.Count / 5d,
                ["request_bytes"] = lastRequestBytes,
                ["response_bytes"] = lastResponseBytes,
                ["latency_ms"] = lastLatencyMilliseconds,
                ["modules"] = ModuleAvailability(),
                ["module_health"] = ModuleHealth(),
                ["module_intervals_seconds"] = ModuleIntervals(),
                ["module_collection_ms"] = new Dictionary<string, double>(lastModuleCollectionMilliseconds, StringComparer.OrdinalIgnoreCase),
                ["queue_depths"] = ModuleQueueDepths()
            };
        }

        private string BuildStatus()
        {
            return "WebsiteBridgeHub v" + Version + ": " + string.Format(
                CultureInfo.InvariantCulture,
                "server={0}, inFlight={1}, sequence={2}, lastAttempt={3}, lastSuccess={4}, http={5}, latencyMs={6:0}, bytes={7}/{8}, failures={9}, nextDelay={10:0}s, callsPerMin={11:0.00}, modules=[{12}], moduleIntervals={13}, moduleMs={14}, queues={15}, error={16}",
                config.ServerId,
                requestInFlight,
                sequence,
                FormatTime(lastAttemptUtc),
                FormatTime(lastSuccessUtc),
                lastHttpCode,
                lastLatencyMilliseconds,
                lastRequestBytes,
                lastResponseBytes,
                consecutiveFailures,
                nextDelaySeconds,
                recentCalls.Count / 5d,
                string.Join(",", ModuleHealth().Select(item => item.Key + ":" + item.Value)),
                JsonConvert.SerializeObject(ModuleIntervals()),
                JsonConvert.SerializeObject(lastModuleCollectionMilliseconds),
                JsonConvert.SerializeObject(ModuleQueueDepths()),
                string.IsNullOrWhiteSpace(lastError) ? "none" : lastError);
        }

        private Dictionary<string, int> ModuleIntervals()
        {
            return new Dictionary<string, int>
            {
                ["vip"] = config.VipModuleIntervalSeconds,
                ["map"] = EffectiveModuleIntervalSeconds("map", config.MapModuleIntervalSeconds),
                ["clans"] = config.ClanModuleIntervalSeconds
            };
        }

        private Dictionary<string, string> ModuleAvailability()
        {
            return new Dictionary<string, string>
            {
                ["vip"] = WebsiteVipBridge != null && WebsiteVipBridge.IsLoaded ? "ready" : "missing",
                ["map"] = WebsiteMapBridge != null && WebsiteMapBridge.IsLoaded ? "ready" : "missing",
                ["clans"] = WebsiteClanBridge != null && WebsiteClanBridge.IsLoaded ? "ready" : "missing"
            };
        }

        private Dictionary<string, string> ModuleHealth()
        {
            var availability = ModuleAvailability();
            return availability.ToDictionary(
                item => item.Key,
                item => item.Value == "ready" && lastModuleHealth.TryGetValue(item.Key, out var health) ? health : item.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, object> ModuleQueueDepths()
        {
            return new Dictionary<string, object>
            {
                ["vip"] = GetModuleQueueDepth(WebsiteVipBridge),
                ["map"] = GetModuleQueueDepth(WebsiteMapBridge),
                ["clans"] = GetModuleQueueDepth(WebsiteClanBridge)
            };
        }

        private object GetModuleQueueDepth(Plugin plugin)
        {
            if (plugin == null || !plugin.IsLoaded)
            {
                return null;
            }
            try
            {
                return plugin.Call("API_GetWebsiteBridgeQueueDepth");
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { ["error"] = ex.Message };
            }
        }

        private void ScheduleNext(float delaySeconds)
        {
            if (unloading)
            {
                return;
            }

            nextExchangeTimer?.Destroy();
            nextDelaySeconds = Math.Max(1f, delaySeconds);
            nextExchangeTimer = timer.Once(nextDelaySeconds, () =>
            {
                nextExchangeTimer = null;
                RunExchange();
            });
        }

        private void RunExchange()
        {
            if (unloading || requestInFlight)
            {
                return;
            }

            if (!CanRequest(out var validationError))
            {
                RecordFailure(0, validationError, 0d, 0);
                return;
            }

            var currentSequence = ++sequence;
            var modules = new JObject();
            var requestedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectModuleWhenDue(WebsiteVipBridge, "vip", currentSequence, modules, requestedModules, config.VipModuleIntervalSeconds);
            CollectModuleWhenDue(WebsiteMapBridge, "map", currentSequence, modules, requestedModules, config.MapModuleIntervalSeconds);
            CollectModuleWhenDue(WebsiteClanBridge, "clans", currentSequence, modules, requestedModules, config.ClanModuleIntervalSeconds);

            var envelope = new JObject
            {
                ["protocol"] = ProtocolVersion,
                ["sequence"] = currentSequence,
                ["server_id"] = config.ServerId,
                ["generated_at"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["modules"] = modules
            };
            var body = envelope.ToString(Formatting.None);
            lastRequestBytes = Encoding.UTF8.GetByteCount(body);
            if (lastRequestBytes > config.MaximumBodyBytes)
            {
                RecordFailure(0, $"exchange body is {lastRequestBytes} bytes; limit is {config.MaximumBodyBytes}", 0d, 0);
                return;
            }

            var url = config.ApiBaseUrl + "/api/server/bridge-exchange.php";
            var headers = BuildHeaders("POST", url, body);
            headers["Content-Type"] = "application/json";
            requestInFlight = true;
            lastAttemptUtc = DateTime.UtcNow;
            recentCalls.Enqueue(lastAttemptUtc);
            PruneRecentCalls();
            var stopwatch = Stopwatch.StartNew();

            webrequest.Enqueue(url, body, (code, response) =>
            {
                stopwatch.Stop();
                requestInFlight = false;
                lastResponseBytes = Encoding.UTF8.GetByteCount(response ?? "");
                lastLatencyMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

                if (code < 200 || code >= 300)
                {
                    RecordFailure(code, "HTTP " + code + ": " + Truncate(response, 300), lastLatencyMilliseconds, lastResponseBytes);
                    return;
                }

                JObject parsed;
                try
                {
                    parsed = JObject.Parse(response ?? "");
                }
                catch (Exception ex)
                {
                    RecordFailure(code, "invalid JSON response: " + ex.Message, lastLatencyMilliseconds, lastResponseBytes);
                    return;
                }

                if (parsed.Value<bool?>("ok") != true || parsed.Value<int?>("protocol") != ProtocolVersion || parsed.Value<long?>("sequence") != currentSequence)
                {
                    RecordFailure(code, "exchange response envelope did not match the request", lastLatencyMilliseconds, lastResponseBytes);
                    return;
                }

                var responseModules = parsed["modules"] as JObject ?? new JObject();
                ApplyRequestedModule(WebsiteVipBridge, "vip", currentSequence, responseModules, requestedModules);
                ApplyRequestedModule(WebsiteMapBridge, "map", currentSequence, responseModules, requestedModules);
                ApplyRequestedModule(WebsiteClanBridge, "clans", currentSequence, responseModules, requestedModules);

                var wasFailing = consecutiveFailures > 0;
                consecutiveFailures = 0;
                lastHttpCode = code;
                lastError = "";
                lastSuccessUtc = DateTime.UtcNow;
                if (wasFailing)
                {
                    Puts("Website bridge exchange recovered.");
                }
                ScheduleNext(config.ExchangeIntervalSeconds);
            }, this, RequestMethod.POST, headers, config.RequestTimeoutSeconds);
        }

        private void CollectModule(Plugin plugin, string key, long currentSequence, JObject modules)
        {
            if (plugin == null || !plugin.IsLoaded)
            {
                return;
            }

            try
            {
                var result = plugin.Call("API_CollectWebsiteBridgeExchange", currentSequence);
                if (result != null)
                {
                    modules[key] = result as JToken ?? JToken.FromObject(result);
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not collect {key} exchange module: {ex.Message}");
            }
        }

        private void CollectModuleWhenDue(Plugin plugin, string key, long currentSequence, JObject modules,
            HashSet<string> requestedModules, int intervalSeconds)
        {
            if (plugin == null || !plugin.IsLoaded || !IsModuleDue(key, intervalSeconds))
            {
                return;
            }

            var hadModule = modules.Property(key) != null;
            var stopwatch = Stopwatch.StartNew();
            CollectModule(plugin, key, currentSequence, modules);
            stopwatch.Stop();
            lastModuleCollectionMilliseconds[key] = stopwatch.Elapsed.TotalMilliseconds;
            if (!hadModule && modules.Property(key) != null)
            {
                lastModuleCollectionUtc[key] = DateTime.UtcNow;
                requestedModules.Add(key);
            }
        }

        private bool IsModuleDue(string key, int intervalSeconds)
        {
            return !lastModuleCollectionUtc.TryGetValue(key, out var lastCollected)
                || (DateTime.UtcNow - lastCollected).TotalSeconds >= EffectiveModuleIntervalSeconds(key, intervalSeconds);
        }

        private int EffectiveModuleIntervalSeconds(string key, int configuredIntervalSeconds)
        {
            var interval = Math.Max(config.ExchangeIntervalSeconds, configuredIntervalSeconds);
            if (string.Equals(key, "map", StringComparison.OrdinalIgnoreCase)
                && lastModuleCollectionMilliseconds.TryGetValue(key, out var elapsedMilliseconds)
                && elapsedMilliseconds >= config.SlowModuleThresholdMilliseconds)
            {
                interval = Math.Max(interval, config.SlowMapModuleIntervalSeconds);
            }
            return interval;
        }

        private void ApplyRequestedModule(Plugin plugin, string key, long currentSequence, JObject responseModules,
            HashSet<string> requestedModules)
        {
            if (!requestedModules.Contains(key))
            {
                return;
            }

            var response = responseModules[key] as JObject;
            UpdateModuleHealth(key, response);
            ApplyModule(plugin, key, currentSequence, response);
        }

        private void ApplyModule(Plugin plugin, string key, long currentSequence, JObject response)
        {
            if (plugin == null || !plugin.IsLoaded || response == null)
            {
                return;
            }

            try
            {
                plugin.Call("API_ApplyWebsiteBridgeExchange", currentSequence, response);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not apply {key} exchange module: {ex.Message}");
            }
        }

        private void UpdateModuleHealth(string key, JObject response)
        {
            if (response == null)
            {
                lastModuleHealth[key] = "not returned";
                return;
            }
            if (response.Value<bool?>("ok") == false)
            {
                lastModuleHealth[key] = "error: " + Truncate(response.Value<string>("error"), 100);
                return;
            }
            foreach (var property in response.Properties())
            {
                if (property.Value is JObject section && section.Value<bool?>("ok") == false)
                {
                    lastModuleHealth[key] = property.Name + " error: " + Truncate(section.Value<string>("error"), 80);
                    return;
                }
            }
            lastModuleHealth[key] = "ok";
        }

        private void RecordFailure(int code, string error, double latencyMilliseconds, int responseBytes)
        {
            requestInFlight = false;
            lastHttpCode = code;
            lastError = error ?? "request failed";
            lastLatencyMilliseconds = latencyMilliseconds;
            lastResponseBytes = responseBytes;
            consecutiveFailures++;
            var exponent = Math.Min(4, Math.Max(0, consecutiveFailures - 1));
            var delay = Math.Min(config.MaximumBackoffSeconds, config.ExchangeIntervalSeconds * (1 << exponent));
            PrintWarning($"Website bridge exchange failed ({consecutiveFailures}): {lastError}; retrying in {delay}s.");
            ScheduleNext(delay);
        }

        private bool CanRequest(out string error)
        {
            if (!Uri.TryCreate(config.ApiBaseUrl, UriKind.Absolute, out var uri))
            {
                error = "ApiBaseUrl is invalid";
                return false;
            }
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                var loopback = uri.Host == "127.0.0.1" || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "::1";
                if (!config.AllowInsecureHttpForDevelopment || !loopback || !string.Equals(ConVar.Server.identity, "raidlands-dev", StringComparison.Ordinal))
                {
                    error = "insecure HTTP is allowed only for the raidlands-dev loopback runtime";
                    return false;
                }
            }
            if (string.IsNullOrWhiteSpace(config.ServerId))
            {
                error = "ServerId is empty";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ResolveBridgeSharedSecret()))
            {
                error = "SharedSecret is empty after resolving WebsiteBridgeHub config, RAIDLANDS_BRIDGE_SHARED_SECRET, WEBSITE_VIP_SHARED_SECRET, and WebsiteVipBridge config";
                return false;
            }
            error = "";
            return true;
        }

        private Dictionary<string, string> BuildHeaders(string method, string url, string body)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var pathAndQuery = new Uri(url).PathAndQuery;
            var payload = method.ToUpperInvariant() + "\n" + pathAndQuery + "\n" + timestamp + "\n" + Sha256(body ?? "");
            return new Dictionary<string, string>
            {
                ["X-Raidlands-Server"] = config.ServerId,
                ["X-Raidlands-Timestamp"] = timestamp,
                ["X-Raidlands-Signature"] = HmacSha256(payload, ResolveBridgeSharedSecret()),
                ["Accept"] = "application/json"
            };
        }

        private string ResolveBridgeSharedSecret()
        {
            var configured = ResolveSecretValue(config.SharedSecret);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var bridgeSecret = ResolveSecretByKey("RAIDLANDS_BRIDGE_SHARED_SECRET");
            if (!string.IsNullOrWhiteSpace(bridgeSecret))
            {
                return bridgeSecret;
            }

            var vipSecret = ResolveSecretByKey("WEBSITE_VIP_SHARED_SECRET");
            if (!string.IsNullOrWhiteSpace(vipSecret))
            {
                return vipSecret;
            }

            return ResolveSecretValue(LoadVipBridgeSharedSecretSetting());
        }

        private string ResolveSecretByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            return LoadSecrets().TryGetValue(key.Trim(), out var secret)
                ? (secret ?? "").Trim()
                : "";
        }

        private string LoadVipBridgeSharedSecretSetting()
        {
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, "WebsiteVipBridge.json");
            try
            {
                if (!File.Exists(path))
                {
                    return "";
                }

                var json = JObject.Parse(File.ReadAllText(path));
                return (json.Value<string>("SharedSecret") ?? "").Trim();
            }
            catch (Exception ex)
            {
                PrintWarning("Could not read WebsiteVipBridge.json shared-secret setting: " + ex.Message);
                return "";
            }
        }

        private string DescribeBridgeSecretSource()
        {
            if (!string.IsNullOrWhiteSpace(ResolveSecretValue(config.SharedSecret)))
            {
                return "WebsiteBridgeHub.json SharedSecret";
            }
            if (!string.IsNullOrWhiteSpace(ResolveSecretByKey("RAIDLANDS_BRIDGE_SHARED_SECRET")))
            {
                return "RAIDLANDS_BRIDGE_SHARED_SECRET in Secrets.local.json";
            }
            if (!string.IsNullOrWhiteSpace(ResolveSecretByKey("WEBSITE_VIP_SHARED_SECRET")))
            {
                return "WEBSITE_VIP_SHARED_SECRET in Secrets.local.json";
            }
            if (!string.IsNullOrWhiteSpace(ResolveSecretValue(LoadVipBridgeSharedSecretSetting())))
            {
                return "WebsiteVipBridge.json SharedSecret";
            }
            return "unresolved";
        }

        private string ResolveSecretValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }
            var trimmed = value.Trim();
            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return trimmed;
            }
            var key = trimmed.Substring(2, trimmed.Length - 3).Trim();
            if (LoadSecrets().TryGetValue(key, out var secret))
            {
                return (secret ?? "").Trim();
            }
            return "";
        }

        private Dictionary<string, string> LoadSecrets()
        {
            if (secrets != null)
            {
                return secrets;
            }
            secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, SecretsConfigName + ".json");
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        secrets = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Could not read " + SecretsConfigName + ".json: " + ex.Message);
            }
            return secrets;
        }

        private void PruneRecentCalls()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            while (recentCalls.Count > 0 && recentCalls.Peek() < cutoff)
            {
                recentCalls.Dequeue();
            }
        }

        private static string FormatTime(DateTime value) => value == DateTime.MinValue ? "never" : value.ToString("o", CultureInfo.InvariantCulture);
        private static string Truncate(string value, int length)
        {
            var clean = new string((value ?? "").Where(character => !char.IsControl(character)).ToArray());
            return clean.Length <= length ? clean : clean.Substring(0, length);
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")).Select(item => item.ToString("x2")));
            }
        }

        private static string HmacSha256(string value, string secret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? "")))
            {
                return string.Concat(hmac.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")).Select(item => item.ToString("x2")));
            }
        }
    }
}
